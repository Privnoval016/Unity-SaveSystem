using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /**
     * <summary>The JSON rules every save uses, the same on every machine.</summary>
     * <remarks>
     * Culture-invariant numbers; dates as UTC ISO 8601; NaN and infinities written as strings; no type names, so a
     * save can never choose a type to construct; collections replaced on read rather than appended to; and strings
     * that look like dates kept as strings.
     * </remarks>
     */
    public sealed class SaveSerializer
    {
                #region Properties

        /** <summary>The deepest JSON nesting a save may have. Writing deeper data fails, so nothing unreadable is ever written.</summary> */
        public const int MaxDepth = 128;

        /** <summary>UTF-8 without a byte order mark, the encoding of every save.</summary> */
        public static Encoding Utf8 { get; } = new UTF8Encoding(false);

        /** <summary>The serializer sections use to turn data classes into JSON and back.</summary> */
        public JsonSerializer Json { get; }

        #endregion

        #region Public API

        /** <summary>Makes a serializer, with extra converters for types the defaults can't write (engine types, asset references).</summary> */
        public SaveSerializer(IEnumerable<JsonConverter> converters = null)
        {
            var settings = CreateSettings();
            if (converters != null)
                foreach (var converter in converters)
                    settings.Converters.Add(converter);
            Json = JsonSerializer.Create(settings);
        }

        /** <summary>The settings every save uses.</summary> */
        public static JsonSerializerSettings CreateSettings() => new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.None,
            FloatFormatHandling = FloatFormatHandling.String,
            FloatParseHandling = FloatParseHandling.Double,
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            MaxDepth = MaxDepth,
        };

        /** <summary>How deeply a JSON tree nests: 1 for a value, plus 1 for each object or list around it.</summary> */
        public static int DepthOf(JToken json)
        {
            var deepest = 0;
            var pending = new Stack<KeyValuePair<JToken, int>>();
            pending.Push(new KeyValuePair<JToken, int>(json, 1));
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                deepest = Math.Max(deepest, item.Value);
                if (item.Key is JContainer container)
                    foreach (var child in container.Children())
                        pending.Push(new KeyValuePair<JToken, int>(child is JProperty property ? property.Value : child, item.Value + 1));
            }
            return deepest;
        }

        /** <summary>Writes a JSON tree as UTF-8 text without a byte order mark.</summary> */
        public byte[] ToBytes(JToken json, bool indented)
        {
            using (var stream = new MemoryStream())
            {
                using (var text = new StreamWriter(stream, Utf8))
                using (var writer = new JsonTextWriter(text))
                {
                    writer.Formatting = indented ? Formatting.Indented : Formatting.None;
                    writer.Culture = CultureInfo.InvariantCulture;
                    writer.DateFormatHandling = DateFormatHandling.IsoDateFormat;
                    writer.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                    writer.FloatFormatHandling = FloatFormatHandling.String;
                    json.WriteTo(writer);
                }
                return stream.ToArray();
            }
        }

        /**
         * <summary>Reads UTF-8 JSON text into a tree.</summary>
         * <exception cref="JsonException">The text is not valid JSON, or nests too deeply.</exception>
         */
        public JToken Parse(byte[] bytes)
        {
            using (var text = new StreamReader(new MemoryStream(bytes), Utf8, true))
            using (var reader = new JsonTextReader(text))
            {
                reader.Culture = CultureInfo.InvariantCulture;
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                reader.MaxDepth = MaxDepth;
                var json = JToken.ReadFrom(reader);
                if (reader.Read()) throw new JsonReaderException("Unexpected text after the JSON value.");
                return json;
            }
        }

        #endregion
    }
}
