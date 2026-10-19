using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveSystem
{
    /**
     * <summary>Every saveable asset in the project, listed by the editor so a build can find an asset from its id.</summary>
     * <remarks>The editor rebuilds it on import and before every build. It holds references, so the assets it lists load with it.</remarks>
     */
    [CreateAssetMenu(menuName = "Save System/Asset Catalog", fileName = "AssetCatalog")]
    public sealed class AssetCatalog : ScriptableObject, IAssetResolver
    {
        #region Inspector Fields

        [Tooltip("Filled in by the editor: every asset with an asset id, sorted by id. Not edited by hand.")]
        [SerializeField] private List<ScriptableObject> assets = new List<ScriptableObject>();

        #endregion

        #region Private Fields

        /** <summary>Keyed by asset id, resolving to the listed asset. Built on first use.</summary> */
        private Dictionary<AssetId, ScriptableObject> _byId;

        #endregion

        #region Properties

        /** <summary>The listed assets.</summary> */
        public IReadOnlyList<ScriptableObject> Assets => assets;

        #endregion

        #region MonoBehaviour Callbacks

        private void OnEnable() => _byId = null;

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public bool TryFind(AssetId id, out ScriptableObject asset)
        {
            if (_byId == null) _byId = BuildIndex();
            return _byId.TryGetValue(id, out asset) && asset != null;
        }

        /**
         * <summary>Replaces the listed assets. Called by the editor's catalog builder.</summary>
         * <returns>True when the list changed.</returns>
         */
        public bool SetAssets(IReadOnlyList<ScriptableObject> sorted)
        {
            if (sorted == null) throw new ArgumentNullException(nameof(sorted));
            var changed = sorted.Count != assets.Count;
            for (var i = 0; !changed && i < sorted.Count; i++)
                changed = sorted[i] != assets[i];
            if (!changed) return false;

            assets = new List<ScriptableObject>(sorted);
            _byId = null;
            return true;
        }

        #endregion

        #region Helpers

        private Dictionary<AssetId, ScriptableObject> BuildIndex()
        {
            var index = new Dictionary<AssetId, ScriptableObject>();
            foreach (var asset in assets)
            {
                if (!SaveableAssets.TryGetId(asset, out var id)) continue;
                if (index.ContainsKey(id)) Debug.LogWarning($"Asset catalog: duplicate id {id} on {asset.name}.", this);
                else index.Add(id, asset);
            }
            return index;
        }

        #endregion
    }
}
