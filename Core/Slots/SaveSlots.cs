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
     * <typeparam name="TInfo">The game's own header fields.</typeparam>
     */
    public sealed class SaveSlots<TInfo> where TInfo : class
    {
        #region Private Fields

        private const string Queue = "slots";
        private const string SaveSuffix = ".save";
        private const string HeaderSuffix = ".head";
        private const int MaxAutosaves = 99;

        private readonly SaveService _service;
        private readonly Func<TInfo> _captureInfo;
        private readonly SlotOptions _options;
        private readonly ISaveWorker _worker;
        private readonly SaveFileStore _saves;
        private readonly SaveFileStore _headers;

        #endregion

        #region Public API

        /**
         * <param name="service">The sections to save.</param>
         * <param name="storage">Where slots live. Give settings their own storage.</param>
         * <param name="captureInfo">Returns the game's header fields at the moment of saving; may be null.</param>
         * <param name="options">Worker, codec, autosave count and clock.</param>
         */
        public SaveSlots(SaveService service, ISaveStorage storage, Func<TInfo> captureInfo, SlotOptions options = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _captureInfo = captureInfo ?? (() => null);
            _options = options ?? new SlotOptions();
            if (_options.AutosaveCount < 1 || _options.AutosaveCount > MaxAutosaves)
                throw new ArgumentOutOfRangeException(nameof(options), $"Autosave count must be 1 to {MaxAutosaves}.");
            _worker = _options.Worker ?? throw new ArgumentException("Options have no worker.", nameof(options));
            _saves = new SaveFileStore(storage, service.Serializer, _options.Codec);
            _headers = new SaveFileStore(storage, service.Serializer);
        }

        /** <summary>Captures the game now and writes it to a slot in the background.</summary> */
        public Task<SaveResult> SaveAsync(SlotId slot)
        {
            if (slot.Name == null) throw new ArgumentException("No slot given.", nameof(slot));
            return Save(() => slot);
        }

        /** <summary>Captures the game now and writes it to the autosave slot that is empty or oldest.</summary> */
        public Task<SaveResult> AutosaveAsync() => Save(PickAutosaveSlot);

        /** <summary>Reads a slot in the background, then restores it on the calling thread. A missing or stale header is rewritten.</summary> */
        public async Task<LoadResult> LoadAsync(SlotId slot)
        {
            var read = await _worker.Run(Queue, () => ReadSlot(slot));
            return DocumentRestore.Restore(_service, read);
        }

        /** <summary>Lists every slot that has a save, by name, reading only headers.</summary> */
        public Task<IReadOnlyList<SlotSummary<TInfo>>> ListAsync() => _worker.Run(Queue, ListSlots);

        /** <summary>Deletes a slot's save, header and backups.</summary> <returns>True when everything was deleted.</returns> */
        public Task<bool> DeleteAsync(SlotId slot) => _worker.Run(Queue, () =>
        {
            var saveDeleted = _saves.Delete(SaveEntry(slot)) == null;
            var headerDeleted = _headers.Delete(HeaderEntry(slot)) == null;
            return saveDeleted && headerDeleted;
        });

        /** <summary>Rewrites a slot's header from the copy inside its save.</summary> <returns>True when the header was rewritten.</returns> */
        public Task<bool> RepairHeaderAsync(SlotId slot) => _worker.Run(Queue, () =>
        {
            var read = _saves.Read(SaveEntry(slot), SaveDocument.FromJson);
            return read.Status == ReadStatus.Ok && read.Value.Meta != null && _headers.Write(HeaderEntry(slot), read.Value.Meta).Succeeded;
        });

        /** <summary>Blocks until pending writes finish, or the timeout passes. Call on quit.</summary> <returns>True when all finished.</returns> */
        public bool Flush(TimeSpan timeout) => _worker.WaitForAll(timeout);

        #endregion

        #region Helpers

        private static string SaveEntry(SlotId slot) => slot.Name + SaveSuffix;

        private static string HeaderEntry(SlotId slot) => slot.Name + HeaderSuffix;

        private Task<SaveResult> Save(Func<SlotId> chooseSlot)
        {
            SaveDocument document;
            JToken info;
            try
            {
                document = _service.Capture();
                info = CaptureInfo();
            }
            catch (Exception e)
            {
                return Task.FromResult(new SaveResult(SaveStatus.CaptureFailed, e.Message));
            }
            return _worker.Run(Queue, () => WriteSlot(chooseSlot(), document, info));
        }

        private JToken CaptureInfo()
        {
            var info = _captureInfo();
            return info == null ? JValue.CreateNull() : JToken.FromObject(info, _service.Serializer.Json);
        }

        private SaveResult WriteSlot(SlotId slot, SaveDocument document, JToken info)
        {
            var header = new JObject
            {
                ["slot"] = slot.Name,
                ["sequence"] = NextSequence(),
                ["savedAtUtc"] = _options.UtcNow(),
                ["info"] = info,
            };
            document.Meta = header;

            var saved = _saves.Write(SaveEntry(slot), document.ToJson());
            if (!saved.Succeeded) return saved;

            var headed = _headers.Write(HeaderEntry(slot), header);
            return new SaveResult(SaveStatus.Saved, headed.Problem, slot.Name);
        }

        private ReadResult<SaveDocument> ReadSlot(SlotId slot)
        {
            var read = _saves.Read(SaveEntry(slot), SaveDocument.FromJson);
            if (read.Status == ReadStatus.Ok && read.Value.Meta != null) RefreshHeader(slot, read.Value.Meta);
            return read;
        }

        private void RefreshHeader(SlotId slot, JObject meta)
        {
            var current = _headers.Read(HeaderEntry(slot), json => json);
            if (current.Status != ReadStatus.Ok || current.UsedBackup || !JToken.DeepEquals(current.Value, meta))
                _headers.Write(HeaderEntry(slot), meta);
        }

        private IReadOnlyList<SlotSummary<TInfo>> ListSlots()
        {
            var slots = new List<SlotSummary<TInfo>>();
            foreach (var entry in ListEntries())
            {
                if (!entry.EndsWith(SaveSuffix, StringComparison.Ordinal)) continue;
                if (!SlotId.TryParse(entry.Substring(0, entry.Length - SaveSuffix.Length), out var slot)) continue;
                slots.Add(Summarize(slot));
            }
            return slots;
        }

        private IReadOnlyList<string> ListEntries()
        {
            try
            {
                return _saves.Storage.List();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private SlotSummary<TInfo> Summarize(SlotId slot)
        {
            var read = _headers.Read(HeaderEntry(slot), json => ParseHeader(json, slot));
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
            var header = json.ToObject<SlotHeader<TInfo>>(_service.Serializer.Json);
            if (header == null || !string.Equals(header.Slot, slot.Name, StringComparison.Ordinal))
                throw new FormatException($"The header is not for slot '{slot.Name}'.");
            return header;
        }

        private long NextSequence()
        {
            var highest = 0L;
            foreach (var summary in ListSlots())
                if (summary.State == SlotHeaderState.Ok && summary.Header.Sequence > highest) highest = summary.Header.Sequence;
            return highest + 1;
        }

        private SlotId PickAutosaveSlot()
        {
            var existing = new HashSet<string>(ListEntries(), StringComparer.Ordinal);
            var oldest = SlotId.Auto(1);
            var oldestSequence = long.MaxValue;
            for (var number = 1; number <= _options.AutosaveCount; number++)
            {
                var slot = SlotId.Auto(number);
                if (!existing.Contains(SaveEntry(slot))) return slot;
                var summary = Summarize(slot);
                var sequence = summary.State == SlotHeaderState.Ok ? summary.Header.Sequence : -1;
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
