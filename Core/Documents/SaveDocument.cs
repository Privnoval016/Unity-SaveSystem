using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /** <summary>A whole save as data: its format, optional metadata, and every section by key.</summary> */
    public sealed class SaveDocument
    {
        #region Private Fields

        private const string FormatProperty = "format";
        private const string MetaProperty = "meta";
        private const string SectionsProperty = "sections";
        private const string UpgradesProperty = "upgrades";
        private const string VersionProperty = "version";
        private const string DataProperty = "data";

        #endregion

        #region Properties

        /** <summary>The document layout this code writes. Newer documents are refused.</summary> */
        public const int CurrentFormat = 1;

        /** <summary>The layout version the document was written with.</summary> */
        public int Format { get; }

        /** <summary>Data about the save itself, such as a slot's header; null when there is none.</summary> */
        public JObject Meta { get; set; }

        /** <summary>Keyed by section key, resolving to that section's stored record. Ordered by key.</summary> */
        public IDictionary<string, SectionRecord> Sections { get; } = new SortedDictionary<string, SectionRecord>(StringComparer.Ordinal);

        /** <summary>The ids of the <see cref="DocumentUpgrade" />s this save has had.</summary> */
        public ISet<string> AppliedUpgrades { get; } = new SortedSet<string>(StringComparer.Ordinal);

        #endregion

        #region Public API

        /** <summary>Makes an empty document in the current format.</summary> */
        public SaveDocument() : this(CurrentFormat) { }

        private SaveDocument(int format) => Format = format;

        /** <summary>Writes the document as a JSON tree. Section data is copied, so the tree is independent of this document.</summary> */
        public JObject ToJson() => ToJson(true);

        /**
         * <summary>Writes the document as a JSON tree.</summary>
         * <param name="copySectionData">False to use section data as it is, for a document just captured and used once.</param>
         */
        public JObject ToJson(bool copySectionData)
        {
            var sections = new JObject();
            foreach (var pair in Sections)
                sections[pair.Key] = new JObject
                {
                    [VersionProperty] = pair.Value.Version,
                    [DataProperty] = copySectionData || pair.Value.Data.Parent != null ? pair.Value.Data.DeepClone() : pair.Value.Data,
                };

            var json = new JObject { [FormatProperty] = Format };
            if (Meta != null) json[MetaProperty] = Meta.DeepClone();
            if (AppliedUpgrades.Count > 0) json[UpgradesProperty] = new JArray(AppliedUpgrades);
            json[SectionsProperty] = sections;
            return json;
        }

        /** <summary>A deep copy, independent of this document.</summary> */
        public SaveDocument Copy() => FromJson(ToJson());

        /**
         * <summary>Reads a document from a JSON tree, checking its shape.</summary>
         * <exception cref="FormatException">The JSON is not a save document.</exception>
         */
        public static SaveDocument FromJson(JToken json)
        {
            if (!(json is JObject root)) throw new FormatException("A save document must be a JSON object.");

            var document = new SaveDocument(ReadPositiveInt(root, FormatProperty));
            if (root.TryGetValue(MetaProperty, out var meta) && meta.Type != JTokenType.Null)
                document.Meta = meta as JObject ?? throw new FormatException("'meta' must be an object.");

            if (root.TryGetValue(UpgradesProperty, out var upgrades) && upgrades.Type != JTokenType.Null)
            {
                if (!(upgrades is JArray ids)) throw new FormatException("'upgrades' must be a list.");
                foreach (var id in ids)
                    document.AppliedUpgrades.Add(id.Type == JTokenType.String ? id.Value<string>() : throw new FormatException("Upgrade ids must be text."));
            }

            if (!(root[SectionsProperty] is JObject sections)) throw new FormatException("'sections' must be an object.");
            foreach (var property in sections.Properties())
            {
                if (!SaveNames.IsValidSectionKey(property.Name)) throw new FormatException($"Invalid section key '{property.Name}'.");
                if (!(property.Value is JObject record)) throw new FormatException($"Section '{property.Name}' must be an object.");
                document.Sections[property.Name] = new SectionRecord(ReadPositiveInt(record, VersionProperty), record[DataProperty]);
            }
            return document;
        }

        #endregion

        #region Helpers

        private static int ReadPositiveInt(JObject json, string property)
        {
            var token = json[property];
            if (token == null || token.Type != JTokenType.Integer) throw new FormatException($"'{property}' must be a whole number.");
            var value = token.Value<long>();
            if (value < 1 || value > int.MaxValue) throw new FormatException($"'{property}' is out of range.");
            return (int)value;
        }

        #endregion
    }
}
