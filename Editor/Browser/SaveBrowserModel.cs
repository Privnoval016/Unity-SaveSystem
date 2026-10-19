using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem.EditorTools
{
    /** <summary>One save file as the browser shows it.</summary> */
    public sealed class BrowsedSave
    {
        #region Properties

        /** <summary>The entry's name in the folder.</summary> */
        public string Entry { get; }

        /** <summary>The document, or null when it couldn't be read.</summary> */
        public SaveDocument Document { get; }

        /** <summary>True when the entry was damaged and its backup was read.</summary> */
        public bool UsedBackup { get; }

        /** <summary>What went wrong reading it; null when nothing did.</summary> */
        public string Problem { get; }

        /** <summary>The transforms the file was written with, which an edit writes it with again.</summary> */
        public IReadOnlyList<string> Transforms { get; }

        #endregion

        #region Public API

        /** <summary>Makes a browsed save.</summary> */
        public BrowsedSave(string entry, SaveDocument document, bool usedBackup, string problem, IReadOnlyList<string> transforms)
        {
            Entry = entry;
            Document = document;
            UsedBackup = usedBackup;
            Problem = problem;
            Transforms = transforms;
        }

        #endregion
    }

    /**
     * <summary>What the save browser does, apart from drawing: list a folder's saves, read one, edit a section and
     * write it back with a fresh checksum, delete.</summary>
     * <remarks>Reads plain and gzip saves; an encrypted save shows why it can't be opened.</remarks>
     */
    public sealed class SaveBrowserModel
    {
        #region Private Fields

        private readonly ISaveStorage _storage;
        private readonly SaveSerializer _serializer = new SaveSerializer();

        /** <summary>Keyed by transform id, resolving to the transforms the browser can read and write again.</summary> */
        private readonly Dictionary<string, ISaveTransform> _transforms = new Dictionary<string, ISaveTransform>(StringComparer.Ordinal);

        #endregion

        #region Public API

        /**
         * <summary>Browses the entries in a storage.</summary>
         * <param name="storage">Where the saves are.</param>
         * <param name="transforms">The transforms saves may use; gzip when none are given. Encrypted saves need their transform.</param>
         */
        public SaveBrowserModel(ISaveStorage storage, IEnumerable<ISaveTransform> transforms = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            foreach (var transform in transforms ?? new ISaveTransform[] { new GzipTransform() })
                _transforms[transform.Id] = transform;
        }

        /** <summary>Every entry that can hold a save document: everything but slot headers.</summary> */
        public IReadOnlyList<string> Entries()
        {
            try
            {
                return _storage.List().Where(entry => !SlotEntries.IsHeader(entry)).ToList();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /** <summary>Reads an entry, falling back to its backup like the game does.</summary> */
        public BrowsedSave Open(string entry)
        {
            var transforms = ReadTransforms(entry);
            var store = new SaveFileStore(_storage, _serializer, new SaveFileCodec(alsoReadable: _transforms.Values));
            var read = store.Read(entry, SaveDocument.FromJson);
            return new BrowsedSave(entry, read.Status == ReadStatus.Ok ? read.Value : null, read.UsedBackup,
                read.Status == ReadStatus.Missing ? "missing" : read.Problem, transforms);
        }

        /** <summary>A section's data as indented JSON.</summary> */
        public static string SectionText(SaveDocument document, string key) =>
            document.Sections.TryGetValue(key, out var record) ? record.Data.ToString(Formatting.Indented) : null;

        /**
         * <summary>Replaces a section's data, keeping its version, and writes the save back with a fresh checksum.
         * The previous file becomes the backup.</summary>
         */
        public SaveResult WriteSection(BrowsedSave save, string key, string json)
        {
            if (save?.Document == null) return new SaveResult(SaveStatus.WriteFailed, "The save could not be read.");
            if (!save.Document.Sections.TryGetValue(key, out var record)) return new SaveResult(SaveStatus.WriteFailed, $"No section '{key}'.");

            JToken data;
            try
            {
                data = _serializer.Parse(SaveSerializer.Utf8.GetBytes(json ?? string.Empty));
            }
            catch (Exception e)
            {
                return new SaveResult(SaveStatus.WriteFailed, $"Not valid JSON: {e.Message}");
            }
            if (!TryCodecFor(save.Transforms, out var codec, out var problem)) return new SaveResult(SaveStatus.WriteFailed, problem);

            var edited = SaveDocument.FromJson(save.Document.ToJson());
            edited.Sections[key] = new SectionRecord(record.Version, data);
            return new SaveFileStore(_storage, _serializer, codec).Write(save.Entry, edited.ToJson());
        }

        /** <summary>Deletes an entry, its backup and, for a slot, its header.</summary> <returns>Null on success, otherwise what went wrong.</returns> */
        public string Delete(string entry)
        {
            try
            {
                _storage.Delete(entry);
                var header = SlotEntries.HeaderBeside(entry);
                if (header != null) _storage.Delete(header);
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        #endregion

        #region Helpers

        private IReadOnlyList<string> ReadTransforms(string entry)
        {
            try
            {
                return SaveFileCodec.ReadTransformIds(_storage.Read(entry) ?? _storage.ReadBackup(entry));
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private bool TryCodecFor(IReadOnlyList<string> transforms, out SaveFileCodec codec, out string problem)
        {
            codec = null;
            problem = null;
            var write = new List<ISaveTransform>();
            foreach (var id in transforms)
            {
                if (!_transforms.TryGetValue(id, out var transform))
                {
                    problem = $"The save uses '{id}', which the browser wasn't given.";
                    return false;
                }
                write.Add(transform);
            }
            codec = new SaveFileCodec(write, _transforms.Values);
            return true;
        }

        #endregion
    }
}
