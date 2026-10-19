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
        /** <summary>Rebuilds the catalogs.</summary> <returns>How many catalogs changed.</returns> */
        [MenuItem("Tools/Save System/Rebuild Asset Catalog")]
        public static int RebuildAll()
        {
            var catalogs = FindCatalogs();
            if (catalogs.Count == 0) return 0;

            var sorted = SaveableAssetDatabase.FindAll()
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
        public static List<AssetCatalog> FindCatalogs() => AssetDatabase.FindAssets("t:" + nameof(AssetCatalog))
            .Select(guid => AssetDatabase.LoadAssetAtPath<AssetCatalog>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(catalog => catalog != null)
            .ToList();
    }
}
