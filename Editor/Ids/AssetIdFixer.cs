using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SaveSystem.EditorTools
{
    /** <summary>Gives saveable assets missing or shared ids their own, and saves them.</summary> */
    public static class AssetIdFixer
    {
        /**
         * <summary>Plans and writes ids across every saveable asset in the project.</summary>
         * <param name="justImported">Paths just imported; their assets lose a tie for a shared id.</param>
         * <returns>The assets whose id changed.</returns>
         */
        public static List<ScriptableObject> FixAll(ICollection<string> justImported = null)
        {
            var assets = SaveableAssetDatabase.FindAll();
            var imported = new HashSet<string>(justImported ?? new string[0]);
            var records = assets.Select(asset => SaveableAssetDatabase.RecordOf(asset, imported)).ToList();

            var changed = new List<ScriptableObject>();
            foreach (var change in AssetIdPlanner.Plan(records))
            {
                var asset = assets[change.Key];
                if (!SaveableAssetDatabase.WriteId(asset, change.Value)) continue;
                changed.Add(asset);
            }
            foreach (var asset in changed)
                AssetDatabase.SaveAssetIfDirty(asset);
            if (changed.Count > 0) Debug.Log($"Save: assigned {changed.Count} asset id(s).");
            return changed;
        }
    }
}
