using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /**
     * <summary>A save document as readable text, the same for the same state: each section in key order, its version,
     * then its data indented with every object's properties in ordinal order. Compare two by text; <see cref="Differences" />
     * names the sections that differ.</summary>
     */
    public static class SaveFingerprint
    {
        #region Private Fields

        private const string Heading = "== ";

        #endregion

        #region Public API

        /** <summary>The fingerprint of <paramref name="document" />, without the sections keyed <paramref name="leaveOut" /> (state that changes on its own, such as play time).</summary> */
        public static string Of(SaveDocument document, params string[] leaveOut) => Of(document, -1, leaveOut);

        /**
         * <summary>As <see cref="Of(SaveDocument, string[])" />, with every number that has a fraction rounded to
         * <paramref name="decimals" /> places: for state a physics step nudges (a body standing still), so only a real change shows.</summary>
         */
        public static string Of(SaveDocument document, int decimals, params string[] leaveOut)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var text = new StringBuilder();
            foreach (var pair in document.Sections)
            {
                if (Array.IndexOf(leaveOut, pair.Key) >= 0) continue;
                text.Append(Heading).Append(pair.Key).Append(" v").Append(pair.Value.Version).Append('\n');
                text.Append(Text(pair.Value, decimals)).Append('\n');
            }

            return text.ToString();
        }

        /** <summary>The keys whose section differs between <paramref name="before" /> and <paramref name="after" />, or is in only one, in key order; empty when they match.</summary> */
        public static IReadOnlyList<string> Differences(SaveDocument before, SaveDocument after, params string[] leaveOut)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));

            var keys = new SortedSet<string>(before.Sections.Keys, StringComparer.Ordinal);
            keys.UnionWith(after.Sections.Keys);
            var differing = new List<string>();
            foreach (var key in keys)
            {
                if (Array.IndexOf(leaveOut, key) >= 0) continue;
                var inBefore = before.Sections.TryGetValue(key, out var was);
                var inAfter = after.Sections.TryGetValue(key, out var now);
                if (inBefore != inAfter || inBefore && (was.Version != now.Version || Text(was, -1) != Text(now, -1))) differing.Add(key);
            }

            return differing;
        }

        /** <summary>Each section of a fingerprint's <paramref name="text" /> by key: its heading line and data, line endings made <c>\n</c> (a fingerprint kept in a file may have come back with others).</summary> */
        public static IReadOnlyDictionary<string, string> Sections(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var sections = new SortedDictionary<string, string>(StringComparer.Ordinal);
            string key = null;
            var block = new StringBuilder();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.StartsWith(Heading, StringComparison.Ordinal))
                {
                    if (key != null) sections[key] = block.ToString();
                    var version = line.LastIndexOf(" v", StringComparison.Ordinal);
                    key = version > Heading.Length ? line.Substring(Heading.Length, version - Heading.Length) : line.Substring(Heading.Length);
                    block.Clear();
                }
                block.Append(line).Append('\n');
            }
            if (key != null) sections[key] = block.ToString();

            return sections;
        }

        /** <summary>As <see cref="Differences(SaveDocument, SaveDocument, string[])" />, between two fingerprints' texts.</summary> */
        public static IReadOnlyList<string> Differences(string before, string after)
        {
            var was = Sections(before);
            var now = Sections(after);
            var keys = new SortedSet<string>(was.Keys, StringComparer.Ordinal);
            keys.UnionWith(now.Keys);
            var differing = new List<string>();
            foreach (var key in keys)
                if (!was.TryGetValue(key, out var a) || !now.TryGetValue(key, out var b) || a != b) differing.Add(key);

            return differing;
        }

        #endregion

        #region Helpers

        /** <summary>A record's data indented in canonical order, numbers rounded to <paramref name="decimals" /> places unless it is negative (on a copy: the record is left as it is).</summary> */
        private static string Text(SectionRecord record, int decimals)
        {
            if (record.Data == null) return "null";
            var data = SaveSerializer.Canonical(record.Data.DeepClone());
            if (decimals >= 0) Round(data, decimals);
            return data.ToString(Formatting.Indented);
        }

        /** <summary>Rounds every fractional number in <paramref name="json" />, in place; adding 0.0 makes a rounded tiny negative 0, never -0.</summary> */
        private static void Round(JToken json, int decimals)
        {
            if (json is JValue { Type: JTokenType.Float } number)
            {
                number.Value = Math.Round(number.Value<double>(), decimals, MidpointRounding.AwayFromZero) + 0.0;
                return;
            }

            foreach (var child in json.Children()) Round(child, decimals);
        }

        #endregion
    }
}
