using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaveSystem
{
    /**
     * <summary>Saves a reference to a saveable asset as its id, and finds the asset again on load.</summary>
     * <remarks>
     * Data classes hold the asset itself (any type that is an <see cref="ISaveableAsset" />). An id with no asset, or
     * with an asset of another type, loads as null with one warning per id.
     * </remarks>
     */
    public sealed class SaveableAssetConverter : JsonConverter
    {
        #region Private Systems

        private readonly IAssetResolver _assets;
        private readonly ISaveLog _log;

        #endregion

        #region Private Fields

        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Public API

        /** <summary>Makes a converter that finds assets through the resolver.</summary> */
        public SaveableAssetConverter(IAssetResolver assets, ISaveLog log = null)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _log = log ?? UnitySaveLog.Instance;
        }

        /** <inheritdoc /> */
        public override bool CanConvert(Type objectType) => typeof(ISaveableAsset).IsAssignableFrom(objectType);

        /** <inheritdoc /> */
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var asset = value as Object;
            if (asset == null)
            {
                writer.WriteNull();
                return;
            }
            if (!SaveableAssets.TryGetId(asset, out var id))
            {
                WarnOnce(asset.name, $"Save: {asset.name} has no asset id; saved as nothing.");
                writer.WriteNull();
                return;
            }
            writer.WriteValue(id.Value);
        }

        /** <inheritdoc /> */
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var text = reader.Value as string;
            reader.Skip();
            if (!AssetId.TryParse(text, out var id))
            {
                WarnOnce(text ?? reader.TokenType.ToString(), $"Save: '{text ?? reader.TokenType.ToString()}' is not an asset id.");
                return null;
            }
            if (!_assets.TryFind(id, out var asset))
            {
                WarnOnce(id.Value, $"Save: no asset has id {id}.");
                return null;
            }
            if (!objectType.IsInstanceOfType(asset))
            {
                WarnOnce(id.Value, $"Save: asset {asset.name} ({id}) is not a {objectType.Name}.");
                return null;
            }
            return asset;
        }

        #endregion

        #region Helpers

        private void WarnOnce(string key, string message)
        {
            if (_warned.Add(key)) _log.Warning(message);
        }

        #endregion
    }
}
