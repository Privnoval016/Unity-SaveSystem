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
     * load restores on the thread that awaited it; while it is under way, saves and other loads return <c>Busy</c>.
     * Each save also carries its header, so a lost header is rebuilt. Nothing here throws for a failed save: every
     * failure is a result, and an unexpected one is logged.
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

        #region Properties

        /** <summary>True while a load is under way: saves and loads asked for now return <c>Busy</c>.</summary> */
        public bool Loading => _documents.Loading;

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

        /**
         * <summary>Lists every slot that has a save, by name, reading only headers.</summary>
         * <param name="checkSaves">
         * Also read each save to say whether it loads (<see cref="SlotSummary{TInfo}.SaveState" />), and rewrite a header
         * that is missing, damaged or stale from the copy inside the save. Reads every save, so call it when a save
         * screen opens rather than every frame.
         * </param>
         */
        public Task<IReadOnlyList<SlotSummary<TInfo>>> ListAsync(bool checkSaves = false) => _documents.Run(() => ListSlots(checkSaves));

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

        /**
         * <summary>Gives every section a new game's state (<see cref="SaveService.NewGame" />), unless a load is under way:
         * then nothing changes and the result is <see cref="LoadStatus.Busy" />, since the load would land on top of it.</summary>
         */
        public LoadResult NewGame() => _documents.Loading ? new LoadResult(LoadStatus.Busy, problem: "A load is under way.") : _documents.Service.NewGame();

        /** <summary>Blocks until pending writes finish, or the timeout passes. Call on quit.</summary> <returns>True when all finished.</returns> */
        public bool Flush(TimeSpan timeout) => _documents.Flush(timeout);

        #endregion

        #region Helpers

        private static void Require(SlotId slot)
        {
            if (slot.Name == null) throw new ArgumentException("No slot given.", nameof(slot));
        }

        /** <summary>Captures the header info and the time on the calling thread, then the service, then writes on the worker.</summary> */
        private Task<SaveResult> Save(Func<SlotId> chooseSlot)
        {
            if (_documents.Loading) return Task.FromResult(new SaveResult(SaveStatus.Busy, "A load is under way."));
            JToken info;
            DateTime savedAt;
            try
            {
                info = SaveSerializer.Canonical(SectionData.Capture(_captureInfo(), _documents.Service.Serializer.Json));
                savedAt = _options.UtcNow();
            }
            catch (Exception e)
            {
                return Task.FromResult(new SaveResult(SaveStatus.CaptureFailed, $"Slot info failed to capture: {e.Message}"));
            }
            return _documents.SaveAsync(document => WriteSlot(chooseSlot, document, info, savedAt));
        }

        /** <summary>Writes the save, then its header. Never throws: anything unexpected is logged and returned as a failed write.</summary> */
        private SaveResult WriteSlot(Func<SlotId> chooseSlot, SaveDocument document, JToken info, DateTime savedAt)
        {
            try
            {
                var slot = chooseSlot();
                if (slot.Name == null) return new SaveResult(SaveStatus.TooNew, "Every autosave slot holds a newer build's save; none is written over.");
                var sequence = NextSequence();
                var header = new SlotHeader<JToken> { Slot = slot.Name, Sequence = sequence, SavedAtUtc = savedAt, Info = info };
                var headerJson = JObject.FromObject(header, _plain.Json);
                document.Meta = headerJson;

                var saved = _saves.Write(SlotEntries.Save(slot), document.ToJson(), _documents.Service.IsFromNewerBuild);
                if (!saved.Succeeded)
                {
                    if (saved.Status == SaveStatus.WriteFailed) _log.Warning($"Save: slot '{slot.Name}' was not written: {saved.Problem}");
                    return saved;
                }
                _highestSequence = sequence;

                var headed = WriteHeader(slot, headerJson);
                if (!headed.Succeeded) _headers.Delete(SlotEntries.Header(slot));
                return new SaveResult(SaveStatus.Saved, headed.Problem, slot.Name);
            }
            catch (Exception e)
            {
                _log.Error($"Save: writing a slot threw: {e}");
                return new SaveResult(SaveStatus.WriteFailed, $"Writing the slot threw: {e.Message}");
            }
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
            var read = ReadSave(slot);
            if (read.Status == ReadStatus.Ok && !read.Value.Newer && read.Value.Document.Meta != null) RefreshHeader(slot, read.Value.Document.Meta);
            return new ReadResult<SaveDocument>(read.Status, read.Value.Document, read.Source, read.Problem);
        }

        private void RefreshHeader(SlotId slot, JObject meta)
        {
            var current = _headers.Read(SlotEntries.Header(slot), json => json);
            var expected = HeaderFor(slot, meta);
            if (current.Status != ReadStatus.Ok || current.UsedBackup || !JToken.DeepEquals(current.Value, expected))
                _headers.Write(SlotEntries.Header(slot), expected);
        }

        private IReadOnlyList<SlotSummary<TInfo>> ListSlots(bool checkSaves)
        {
            var slots = new List<SlotSummary<TInfo>>();
            foreach (var entry in ListEntries())
                if (SlotEntries.TryParseSave(entry, out var slot)) slots.Add(checkSaves ? Check(slot) : Summarize(slot));
            return slots;
        }

        /** <summary>The slot's summary after reading its save: whether it loads, and its header rewritten from the save when it was missing, damaged or stale.</summary> */
        private SlotSummary<TInfo> Check(SlotId slot)
        {
            var read = ReadSave(slot);
            var newer = read.Status == ReadStatus.Ok && read.Value.Newer;
            if (read.Status == ReadStatus.Ok && !newer && read.Value.Document.Meta != null) RefreshHeader(slot, read.Value.Document.Meta);
            var summary = Summarize(slot);
            var state = read.Status switch
            {
                ReadStatus.Ok when newer => SlotSaveState.TooNew,
                ReadStatus.Ok => read.UsedBackup ? SlotSaveState.FromBackup : SlotSaveState.Ok,
                ReadStatus.TooNew => SlotSaveState.TooNew,
                ReadStatus.Unreadable => SlotSaveState.Unreadable,
                _ => SlotSaveState.Damaged,
            };
            return new SlotSummary<TInfo>(slot, summary.State, summary.Header, state, read.Problem);
        }

        /** <summary>The slot's save, read, with whether a newer build wrote it (a newer document or section version).</summary> */
        private ReadResult<(SaveDocument Document, bool Newer)> ReadSave(SlotId slot) =>
            _saves.Read(SlotEntries.Save(slot), json => (SaveDocument.FromJson(json), _documents.Service.IsFromNewerBuild(json)));

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
                case ReadStatus.Ok when read.Value == null:
                    return new SlotSummary<TInfo>(slot, SlotHeaderState.TooNew, null);
                case ReadStatus.Ok:
                    return new SlotSummary<TInfo>(slot, SlotHeaderState.Ok, read.Value);
                case ReadStatus.Missing:
                    return new SlotSummary<TInfo>(slot, SlotHeaderState.Missing, null);
                case ReadStatus.TooNew:
                    return new SlotSummary<TInfo>(slot, SlotHeaderState.TooNew, null);
                default:
                    return new SlotSummary<TInfo>(slot, SlotHeaderState.Damaged, null);
            }
        }

        /** <summary>The header read from JSON; null for a header layout newer than this build's (its slot is listed as too new, not damaged).</summary> */
        private SlotHeader<TInfo> ParseHeader(JToken json, SlotId slot)
        {
            var format = json is JObject obj ? obj.Value<int?>(SlotHeaderFields.Format) : null;
            if (format > SlotHeaderFields.CurrentFormat) return null;
            var header = json.ToObject<SlotHeader<TInfo>>(_plain.Json);
            if (header == null || !string.Equals(header.Slot, slot.Name, StringComparison.Ordinal))
                throw new FormatException($"The header is not for slot '{slot.Name}'.");
            if (header.Format < 1)
                throw new FormatException($"Header format {header.Format} is not one this code reads.");
            return header;
        }

        /**
         * <summary>How new a slot's save is: the higher of its header's sequence and the copy inside the save (a crash
         * between the two writes leaves the header behind), and whether autosave must pass it over: it holds a newer
         * build's save, or can't be read just now.</summary>
         */
        private (long Sequence, bool Newer) AgeOf(SlotId slot)
        {
            var summary = Summarize(slot);
            var sequence = summary.State == SlotHeaderState.Ok ? summary.Header.Sequence : UnknownSequence;
            var read = ReadSave(slot);
            var saved = read.Status == ReadStatus.Ok ? read.Value.Document.Meta?[SlotHeaderFields.Sequence] : null;
            if (saved != null && saved.Type == JTokenType.Integer) sequence = Math.Max(sequence, saved.Value<long>());
            var blocked = summary.State == SlotHeaderState.TooNew || read.Status == ReadStatus.TooNew || read.Status == ReadStatus.Unreadable || read.Status == ReadStatus.Ok && read.Value.Newer;
            return (sequence, blocked);
        }

        private long NextSequence()
        {
            if (_highestSequence == null)
            {
                var highest = 0L;
                foreach (var summary in ListSlots(false))
                    highest = Math.Max(highest, AgeOf(summary.Slot).Sequence);
                _highestSequence = highest;
            }
            return _highestSequence.Value + 1;
        }

        /** <summary>The autosave slot to write: the first empty one, else the oldest; never one holding a newer build's save. None (a default id) when every one does.</summary> */
        private SlotId PickAutosaveSlot()
        {
            var existing = new HashSet<string>(ListEntries(), StringComparer.Ordinal);
            SlotId oldest = default;
            var oldestSequence = long.MaxValue;
            for (var number = 1; number <= _options.AutosaveCount; number++)
            {
                var slot = SlotId.Auto(number);
                if (!existing.Contains(SlotEntries.Save(slot))) return slot;
                var (sequence, newer) = AgeOf(slot);
                if (newer || sequence >= oldestSequence) continue;
                oldest = slot;
                oldestSequence = sequence;
            }
            return oldest;
        }

        #endregion
    }
}
