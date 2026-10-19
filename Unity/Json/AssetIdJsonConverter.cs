using System;
using Newtonsoft.Json;

namespace SaveSystem
{
    /** <summary>Writes an <see cref="AssetId" /> as its 32 hex digits, or null when empty; anything else reads as empty.</summary> */
    public sealed class AssetIdJsonConverter : JsonConverter<AssetId>
    {
        #region Public API

        /** <inheritdoc /> */
        public override void WriteJson(JsonWriter writer, AssetId value, JsonSerializer serializer)
        {
            if (value.IsEmpty) writer.WriteNull();
            else writer.WriteValue(value.Value);
        }

        /** <inheritdoc /> */
        public override AssetId ReadJson(JsonReader reader, Type objectType, AssetId existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var text = reader.Value as string;
            reader.Skip();
            return AssetId.TryParse(text, out var id) ? id : default;
        }

        #endregion
    }
}
