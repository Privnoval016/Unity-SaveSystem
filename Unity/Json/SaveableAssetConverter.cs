using System.Collections.Generic;
using System;
using Extensions.Ids;
using Newtonsoft.Json;
using Object = UnityEngine.Object;
using UnityEngine;

namespace SaveSystem
{
    /**
     * <summary>Saves a reference to a saveable asset as its id, and finds the asset again on load.</summary>
     * <remarks>
     * Data classes hold the asset itself (any type that is an <see cref="IIdentifiedAsset" />). What happens to an asset
     * with no id, an id with no asset, or an asset of another type is the <see cref="UnresolvedAssets" /> policy: by
     * default the save or load fails. Data that must keep ids the game may not have (items from a removed pack) holds
     * the ids as text instead.
     * </remarks>
     */
    public sealed class SaveableAssetConverter : JsonConverter
    {
        #region Private Systems

        private readonly IAssetResolver _assets;
        private readonly ISaveLog _log;
        private readonly UnresolvedAssets _unresolved;

        #endregion

        #region Private Fields

        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Public API

        /** <summary>Makes a converter that finds assets through the resolver, failing on what it can't resolve unless told otherwise.</summary> */
        public SaveableAssetConverter(IAssetResolver assets, ISaveLog log = null, UnresolvedAssets unresolved = UnresolvedAssets.Fail)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _log = log ?? UnitySaveLog.Instance;
            _unresolved = unresolved;
        }

        /** <inheritdoc /> */
        public override bool CanConvert(Type objectType) => typeof(IIdentifiedAsset).IsAssignableFrom(objectType);

        /** <inheritdoc /> */
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            var asset = value as Object;
            if (asset == null)
            {
                Unresolved(value.GetType().Name, value is Object ? $"Save: a {value.GetType().Name} reference points at a destroyed asset" : $"Save: {value.GetType().Name} is not a Unity asset, so it has no asset id");
                writer.WriteNull();
                return;
            }
            if (!AssetIds.TryGetId(asset, out var id))
            {
                Unresolved(asset.name, $"Save: {asset.name} has no asset id");
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
                Unresolved(text ?? reader.TokenType.ToString(), $"Save: '{text ?? reader.TokenType.ToString()}' is not an asset id");
                return null;
            }
            if (!_assets.TryFind(id, out var asset))
            {
                Unresolved(id.Value, $"Save: no asset has id {id}");
                return null;
            }
            if (!objectType.IsInstanceOfType(asset))
            {
                Unresolved(id.Value, $"Save: asset {asset.name} ({id}) is not a {objectType.Name}");
                return null;
            }
            return asset;
        }

        #endregion

        #region Helpers

        /** <summary>Fails the save or load under <see cref="UnresolvedAssets.Fail" />; otherwise warns once for the key and lets it be null.</summary> */
        private void Unresolved(string key, string message)
        {
            if (_unresolved == UnresolvedAssets.Fail) throw new JsonSerializationException(message + ".");
            if (_warned.Add(key)) _log.Warning(message + "; it is saved and loaded as nothing.");
        }

        #endregion
    }
}
