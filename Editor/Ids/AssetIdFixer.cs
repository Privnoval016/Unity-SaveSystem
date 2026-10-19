using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SaveSystem.EditorTools
{
    /** <summary>Gives saveable assets missing or shared ids their own, and saves them.</summary> */
    public static class AssetIdFixer
    {
        #region Public API

        /** <summary>Fixes every saveable asset's id, from the menu.</summary> */
        [MenuItem("Tools/Save System/Fix Asset Ids")]
        public static void FixAllFromMenu() => FixAll();

        /** <summary>Plans and writes ids across every saveable asset in the project.</summary> <returns>The assets whose id changed.</returns> */
        public static List<ScriptableObject> FixAll(ICollection<string> justImported = null) => Fix(SaveableAssetDatabase.FindAll(), justImported);

        /**
         * <summary>Plans and writes ids across the given saveable assets.</summary>
         * <param name="assets">Every saveable asset, or the ones that changed plus every asset sharing an id with them.</param>
         * <param name="justImported">Paths just imported; their assets lose a tie for a shared id.</param>
         * <param name="isTakenElsewhere">True for an id held outside <paramref name="assets" />; null when they are every asset.</param>
         * <returns>The assets whose id changed.</returns>
         */
        public static List<ScriptableObject> Fix(IReadOnlyList<ScriptableObject> assets, ICollection<string> justImported = null, Func<AssetId, bool> isTakenElsewhere = null)
        {
            var imported = new HashSet<string>(justImported ?? Array.Empty<string>());
            var records = assets.Select(asset => SaveableAssetDatabase.RecordOf(asset, imported)).ToList();

            var changed = new List<ScriptableObject>();
            foreach (var change in AssetIdPlanner.Plan(records, isTakenElsewhere))
            {
                var asset = assets[change.Index];
                if (!SaveableAssetDatabase.WriteId(asset, change.Id)) continue;
                AssetDatabase.SaveAssetIfDirty(asset);
                changed.Add(asset);
            }
            if (changed.Count > 0) Debug.Log($"Save: assigned {changed.Count} asset id(s).");
            return changed;
        }

        #endregion
    }
}
