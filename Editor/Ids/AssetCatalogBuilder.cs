using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SaveSystem.EditorTools
{
    /** <summary>Fills every <see cref="AssetCatalog" /> in the project with every saveable asset that has an id.</summary> */
    public static class AssetCatalogBuilder
    {
        #region Private Fields

        private const string CatalogFilter = "t:" + nameof(AssetCatalog);

        #endregion

        #region Public API

        /** <summary>Rebuilds every catalog in the project.</summary> <returns>How many catalogs changed.</returns> */
        [MenuItem("Tools/Save System/Rebuild Asset Catalog")]
        public static int RebuildAll() => Rebuild(FindCatalogs(), SaveableAssetDatabase.FindAll());

        /**
         * <summary>Fills the given catalogs with the given saveable assets that have ids, sorted by id, and saves them.</summary>
         * <returns>How many catalogs changed.</returns>
         */
        public static int Rebuild(IEnumerable<AssetCatalog> catalogs, IEnumerable<ScriptableObject> assets)
        {
            var sorted = assets
                .Where(asset => SaveableAssets.TryGetId(asset, out _))
                .OrderBy(asset => SaveableAssets.IdOf(asset).Value, StringComparer.Ordinal)
                .ToList();

            var changed = 0;
            foreach (var catalog in catalogs)
            {
                if (!catalog.SetAssets(sorted)) continue;
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                changed++;
            }
            return changed;
        }

        /** <summary>Every catalog asset in the project.</summary> */
        public static List<AssetCatalog> FindCatalogs() => AssetDatabase.FindAssets(CatalogFilter)
            .Select(guid => AssetDatabase.LoadAssetAtPath<AssetCatalog>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(catalog => catalog != null)
            .ToList();

        #endregion
    }
}
