using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        private const string SaveSuffix = ".save";
        private const string HeaderSuffix = ".head";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly ISaveStorage _storage;
        private readonly SaveSerializer _serializer = new SaveSerializer();
        private readonly GzipTransform _gzip = new GzipTransform();

        #endregion

        #region Public API

        /** <summary>Browses the entries in a storage.</summary> */
        public SaveBrowserModel(ISaveStorage storage) => _storage = storage ?? throw new ArgumentNullException(nameof(storage));

        /** <summary>Every entry that can hold a save document: everything but slot headers.</summary> */
        public IReadOnlyList<string> Entries()
        {
            try
            {
                return _storage.List().Where(entry => !entry.EndsWith(HeaderSuffix, StringComparison.Ordinal)).ToList();
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
            var store = new SaveFileStore(_storage, _serializer, new SaveFileCodec(alsoReadable: new[] { _gzip }));
            var read = store.Read(entry, SaveDocument.FromJson);
            return new BrowsedSave(entry, read.Status == ReadStatus.Ok ? read.Value : null, read.UsedBackup,
                read.Status == ReadStatus.Missing ? "missing" : read.Problem, transforms);
        }

        /** <summary>A section's data as indented JSON.</summary> */
        public static string SectionText(SaveDocument document, string key) =>
            document.Sections.TryGetValue(key, out var record) ? record.Data.ToString() : null;

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
                data = JToken.Parse(json);
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
                if (entry.EndsWith(SaveSuffix, StringComparison.Ordinal))
                    _storage.Delete(entry.Substring(0, entry.Length - SaveSuffix.Length) + HeaderSuffix);
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
                var bytes = _storage.Read(entry) ?? _storage.ReadBackup(entry);
                var lineEnd = bytes == null ? -1 : Array.IndexOf(bytes, (byte)'\n');
                if (lineEnd < 0) return Array.Empty<string>();
                var header = JObject.Parse(Utf8.GetString(bytes, 0, lineEnd));
                return (header["transforms"] as JArray)?.Select(token => token.ToString()).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
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
                if (id != _gzip.Id)
                {
                    problem = $"The save uses '{id}', which the browser can't write.";
                    return false;
                }
                write.Add(_gzip);
            }
            codec = new SaveFileCodec(write);
            return true;
        }

        #endregion
    }
}
