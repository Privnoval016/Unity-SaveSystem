using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /**
     * <summary>Saves a service's state to named slots, each with a small header file for the save screen, and rotates autosaves.</summary>
     * <remarks>
     * Capture happens on the calling thread; writing, reading and listing happen on the worker, one at a time. A
     * load restores on the thread that awaited it. Each save also carries its header, so a lost header is rebuilt.
     * </remarks>
     * <typeparam name="TInfo">The game's own header fields: plain data, since headers are read without converters.</typeparam>
     */
    public sealed class SaveSlots<TInfo> where TInfo : class
    {
        #region Private Fields

        private const string Queue = "slots";
        private const long UnknownSequence = -1;

        private readonly SaveDocumentQueue _documents;
        private readonly Func<TInfo> _captureInfo;
        private readonly SlotOptions _options;
        private readonly SaveFileStore _saves;
        private readonly SaveFileStore _headers;
        private readonly SaveSerializer _plain = new SaveSerializer();
        private readonly ISaveLog _log;
        private long? _highestSequence;

        #endregion

        #region Public API

        /**
         * <summary>Makes the slots for a service.</summary>
         * <param name="service">The sections to save.</param>
         * <param name="storage">Where slots live. Give settings their own storage.</param>
         * <param name="captureInfo">Returns the game's header fields at the moment of saving; may be null.</param>
         * <param name="options">Worker, codec, autosave count and clock.</param>
         */
        public SaveSlots(SaveService service, ISaveStorage storage, Func<TInfo> captureInfo, SlotOptions options)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _log = options.Log ?? SilentSaveLog.Instance;
            if (_options.AutosaveCount < 1 || _options.AutosaveCount > SlotId.MaxNumber)
                throw new ArgumentOutOfRangeException(nameof(options), $"Autosave count must be 1 to {SlotId.MaxNumber}.");
            _documents = new SaveDocumentQueue(service, _options.Worker, Queue);
            _captureInfo = captureInfo ?? (() => null);
            _saves = new SaveFileStore(storage, service.Serializer, _options.Codec);
            _headers = new SaveFileStore(storage, _plain);
        }

        /** <summary>Captures the game now and writes it to a slot in the background.</summary> */
        public Task<SaveResult> SaveAsync(SlotId slot)
        {
            Require(slot);
            return Save(() => slot);
        }

        /** <summary>Captures the game now and writes it to the autosave slot that is empty or oldest.</summary> */
        public Task<SaveResult> AutosaveAsync() => Save(PickAutosaveSlot);

        /** <summary>Reads a slot in the background, then restores it on the calling thread. A missing or stale header is rewritten.</summary> */
        public Task<LoadResult> LoadAsync(SlotId slot)
        {
            Require(slot);
            return _documents.LoadAsync(() => ReadSlot(slot));
        }

        /** <summary>Lists every slot that has a save, by name, reading only headers.</summary> */
        public Task<IReadOnlyList<SlotSummary<TInfo>>> ListAsync() => _documents.Run(ListSlots);

        /** <summary>Deletes a slot's save, header and backups.</summary> <returns>True when everything was deleted.</returns> */
        public Task<bool> DeleteAsync(SlotId slot)
        {
            Require(slot);
            return _documents.Run(() =>
            {
                var saveDeleted = _saves.Delete(SlotEntries.Save(slot)) == null;
                var headerDeleted = _headers.Delete(SlotEntries.Header(slot)) == null;
                return saveDeleted && headerDeleted;
            });
        }

        /** <summary>Rewrites a slot's header from the copy inside its save.</summary> <returns>True when the header was rewritten.</returns> */
        public Task<bool> RepairHeaderAsync(SlotId slot)
        {
            Require(slot);
            return _documents.Run(() =>
            {
                var read = _saves.Read(SlotEntries.Save(slot), SaveDocument.FromJson);
                return read.Status == ReadStatus.Ok && read.Value.Meta != null && WriteHeader(slot, read.Value.Meta).Succeeded;
            });
        }

        /** <summary>Blocks until pending writes finish, or the timeout passes. Call on quit.</summary> <returns>True when all finished.</returns> */
        public bool Flush(TimeSpan timeout) => _documents.Flush(timeout);

        #endregion

        #region Helpers

        private static void Require(SlotId slot)
        {
            if (slot.Name == null) throw new ArgumentException("No slot given.", nameof(slot));
        }

        private Task<SaveResult> Save(Func<SlotId> chooseSlot)
        {
            JToken info;
            try
            {
                info = SectionData.Capture(_captureInfo(), _documents.Service.Serializer.Json);
            }
            catch (Exception e)
            {
                return Task.FromResult(new SaveResult(SaveStatus.CaptureFailed, $"Slot info failed to capture: {e.Message}"));
            }
            return _documents.SaveAsync(document => WriteSlot(chooseSlot(), document, info));
        }

        private SaveResult WriteSlot(SlotId slot, SaveDocument document, JToken info)
        {
            var sequence = NextSequence();
            var header = new SlotHeader<JToken> { Slot = slot.Name, Sequence = sequence, SavedAtUtc = _options.UtcNow(), Info = info };
            var headerJson = JObject.FromObject(header, _plain.Json);
            document.Meta = headerJson;

            var saved = _saves.Write(SlotEntries.Save(slot), document.ToJson());
            if (!saved.Succeeded) return saved;
            _highestSequence = sequence;

            var headed = WriteHeader(slot, headerJson);
            if (!headed.Succeeded) _headers.Delete(SlotEntries.Header(slot));
            return new SaveResult(SaveStatus.Saved, headed.Problem, slot.Name);
        }

        private static JObject HeaderFor(SlotId slot, JObject meta)
        {
            var header = (JObject)meta.DeepClone();
            header[SlotHeaderFields.Slot] = slot.Name;
            return header;
        }

        private SaveResult WriteHeader(SlotId slot, JObject meta) => _headers.Write(SlotEntries.Header(slot), HeaderFor(slot, meta));

        private ReadResult<SaveDocument> ReadSlot(SlotId slot)
        {
            var read = _saves.Read(SlotEntries.Save(slot), SaveDocument.FromJson);
            if (read.Status == ReadStatus.Ok && read.Value.Meta != null) RefreshHeader(slot, read.Value.Meta);
            return read;
        }

        private void RefreshHeader(SlotId slot, JObject meta)
        {
            var current = _headers.Read(SlotEntries.Header(slot), json => json);
            var expected = HeaderFor(slot, meta);
            if (current.Status != ReadStatus.Ok || current.UsedBackup || !JToken.DeepEquals(current.Value, expected))
                _headers.Write(SlotEntries.Header(slot), expected);
        }

        private IReadOnlyList<SlotSummary<TInfo>> ListSlots()
        {
            var slots = new List<SlotSummary<TInfo>>();
            foreach (var entry in ListEntries())
                if (SlotEntries.TryParseSave(entry, out var slot)) slots.Add(Summarize(slot));
            return slots;
        }

        private IReadOnlyList<string> ListEntries()
        {
            try
            {
                return _saves.Storage.List();
            }
            catch (Exception e)
            {
                _log.Error($"Save: listing slots failed: {e.Message}");
                return Array.Empty<string>();
            }
        }

        private SlotSummary<TInfo> Summarize(SlotId slot)
        {
            var read = _headers.Read(SlotEntries.Header(slot), json => ParseHeader(json, slot));
            switch (read.Status)
            {
                case ReadStatus.Ok:
                    return new SlotSummary<TInfo>(slot, SlotHeaderState.Ok, read.Value);
                case ReadStatus.Missing:
                    return new SlotSummary<TInfo>(slot, SlotHeaderState.Missing, null);
                default:
                    return new SlotSummary<TInfo>(slot, SlotHeaderState.Damaged, null);
            }
        }

        private SlotHeader<TInfo> ParseHeader(JToken json, SlotId slot)
        {
            var header = json.ToObject<SlotHeader<TInfo>>(_plain.Json);
            if (header == null || !string.Equals(header.Slot, slot.Name, StringComparison.Ordinal))
                throw new FormatException($"The header is not for slot '{slot.Name}'.");
            if (header.Format < 1 || header.Format > SlotHeaderFields.CurrentFormat)
                throw new FormatException($"Header format {header.Format} is not one this code reads.");
            return header;
        }

        private long SequenceOf(SlotSummary<TInfo> summary)
        {
            if (summary.State == SlotHeaderState.Ok) return summary.Header.Sequence;
            var read = _saves.Read(SlotEntries.Save(summary.Slot), SaveDocument.FromJson);
            var sequence = read.Status == ReadStatus.Ok ? read.Value.Meta?[SlotHeaderFields.Sequence] : null;
            return sequence != null && sequence.Type == JTokenType.Integer ? sequence.Value<long>() : UnknownSequence;
        }

        private long NextSequence()
        {
            if (_highestSequence == null)
            {
                var highest = 0L;
                foreach (var summary in ListSlots())
                    highest = Math.Max(highest, SequenceOf(summary));
                _highestSequence = highest;
            }
            return _highestSequence.Value + 1;
        }

        private SlotId PickAutosaveSlot()
        {
            var existing = new HashSet<string>(ListEntries(), StringComparer.Ordinal);
            var oldest = SlotId.Auto(1);
            var oldestSequence = long.MaxValue;
            for (var number = 1; number <= _options.AutosaveCount; number++)
            {
                var slot = SlotId.Auto(number);
                if (!existing.Contains(SlotEntries.Save(slot))) return slot;
                var sequence = SequenceOf(Summarize(slot));
                if (sequence < oldestSequence)
                {
                    oldest = slot;
                    oldestSequence = sequence;
                }
            }
            return oldest;
        }

        #endregion
    }
}
