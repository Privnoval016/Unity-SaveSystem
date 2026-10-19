using System;
using Newtonsoft.Json;
using UnityEngine;

namespace SaveSystem
{
    /** <summary>A stable id for an asset a save can point at: 32 lower-case hex digits, filled in by the editor.</summary> */
    [Serializable]
    [JsonConverter(typeof(AssetIdJsonConverter))]
    public struct AssetId : IEquatable<AssetId>
    {
        #region Inspector Fields

        [Tooltip("Filled in by the editor when the asset is created. Never edit it: saves point at it.")]
        [SerializeField] private string value;

        #endregion

        #region Private Fields

        private const int Length = 32;

        /** <summary>The serialized field's name, for editor tools that write it.</summary> */
        internal const string ValueFieldName = nameof(value);

        #endregion

        #region Properties

        /** <summary>The id as 32 lower-case hex digits, or empty.</summary> */
        public string Value => value ?? string.Empty;

        /** <summary>True when no id has been assigned.</summary> */
        public bool IsEmpty => string.IsNullOrEmpty(value);

        #endregion

        #region Public API

        /** <summary>A new random id.</summary> */
        public static AssetId New() => new AssetId(Guid.NewGuid().ToString("N"));

        /** <summary>Reads an id; upper-case hex is accepted and stored in lower case.</summary> */
        public static bool TryParse(string text, out AssetId id)
        {
            id = default;
            if (text == null || text.Length != Length) return false;
            foreach (var c in text)
                if (!Uri.IsHexDigit(c)) return false;
            id = new AssetId(text.ToLowerInvariant());
            return true;
        }

        /**
         * <summary>Reads an id.</summary>
         * <exception cref="FormatException">The text is not 32 hex digits.</exception>
         */
        public static AssetId Parse(string text) =>
            TryParse(text, out var id) ? id : throw new FormatException($"'{text}' is not an asset id.");

        /** <inheritdoc /> */
        public bool Equals(AssetId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        /** <inheritdoc /> */
        public override bool Equals(object obj) => obj is AssetId other && Equals(other);

        /** <inheritdoc /> */
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        /** <inheritdoc /> */
        public override string ToString() => IsEmpty ? "(no id)" : Value;

        /** <summary>Ids are equal when their text is.</summary> */
        public static bool operator ==(AssetId left, AssetId right) => left.Equals(right);

        /** <summary>Ids differ when their text does.</summary> */
        public static bool operator !=(AssetId left, AssetId right) => !left.Equals(right);

        #endregion

        #region Helpers

        private AssetId(string text) => value = text;

        #endregion
    }
}
