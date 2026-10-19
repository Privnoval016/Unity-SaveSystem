using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /** <summary>Turns a section's data class into JSON and back, the same way for every kind of section.</summary> */
    public static class SectionData
    {
        /** <summary>The data as JSON; a JSON null for no data.</summary> */
        public static JToken Capture<TData>(TData data, JsonSerializer serializer) where TData : class =>
            data == null ? JValue.CreateNull() : JToken.FromObject(data, serializer);

        /** <summary>The data class read from JSON; null for missing or null JSON. Throws when the JSON doesn't fit.</summary> */
        public static TData Read<TData>(JToken json, JsonSerializer serializer) where TData : class =>
            json == null || json.Type == JTokenType.Null ? null : json.ToObject<TData>(serializer);
    }

    /** <summary>Read data waiting to be handed to whatever restores it.</summary> */
    public sealed class PreparedRestore<TData> : IPreparedRestore
    {
        #region Private Fields

        private readonly Action<TData> _restore;
        private readonly TData _data;

        #endregion

        #region Public API

        /** <summary>Holds the data until <see cref="Apply" />.</summary> */
        public PreparedRestore(Action<TData> restore, TData data)
        {
            _restore = restore;
            _data = data;
        }

        /** <inheritdoc /> */
        public void Apply() => _restore(_data);

        #endregion
    }

    /** <summary>A restore that changes nothing.</summary> */
    public sealed class NoRestore : IPreparedRestore
    {
        #region Properties

        /** <summary>The shared instance.</summary> */
        public static NoRestore Instance { get; } = new NoRestore();

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public void Apply() { }

        #endregion
    }
}
