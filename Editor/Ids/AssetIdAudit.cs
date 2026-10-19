using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SaveSystem.EditorTools
{
    /** <summary>What is wrong with a saveable asset's id.</summary> */
    public enum AssetIdProblem
    {
        /** <summary>It has no id yet.</summary> */
        Missing,
        /** <summary>Another asset has the same id.</summary> */
        Duplicate,
        /** <summary>It doesn't have exactly one serialized <see cref="AssetId" /> field.</summary> */
        NoSingleIdField,
        /** <summary>A catalog doesn't list it.</summary> */
        NotInCatalog,
    }

    /** <summary>One problem found by the audit.</summary> */
    public readonly struct AssetIdIssue
    {
        #region Properties

        /** <summary>What is wrong.</summary> */
        public AssetIdProblem Problem { get; }

        /** <summary>The asset it is wrong with.</summary> */
        public ScriptableObject Asset { get; }

        #endregion

        #region Public API

        /** <summary>Makes an issue.</summary> */
        public AssetIdIssue(AssetIdProblem problem, ScriptableObject asset)
        {
            Problem = problem;
            Asset = asset;
        }

        /** <inheritdoc /> */
        public override string ToString() => $"{Problem}: {AssetDatabase.GetAssetPath(Asset)} ({(Asset == null ? "missing" : Asset.name)})";

        #endregion
    }

    /** <summary>Checks every saveable asset: one id field, an id, a unique id, and listed in every catalog.</summary> */
    public static class AssetIdAudit
    {
        #region Public API

        /** <summary>Runs the audit.</summary> */
        public static List<AssetIdIssue> Run()
        {
            var assets = SaveableAssetDatabase.FindAll();
            var issues = new List<AssetIdIssue>();
            foreach (var asset in assets)
            {
                if (SaveableAssetDatabase.CountIdFields(asset) != 1) issues.Add(new AssetIdIssue(AssetIdProblem.NoSingleIdField, asset));
                else if (!SaveableAssets.TryGetId(asset, out _)) issues.Add(new AssetIdIssue(AssetIdProblem.Missing, asset));
            }

            foreach (var group in assets.Where(asset => SaveableAssets.TryGetId(asset, out _)).GroupBy(SaveableAssets.IdOf).Where(group => group.Count() > 1))
                issues.AddRange(group.Select(asset => new AssetIdIssue(AssetIdProblem.Duplicate, asset)));

            foreach (var catalog in AssetCatalogBuilder.FindCatalogs())
            {
                var listed = new HashSet<ScriptableObject>(catalog.Assets);
                issues.AddRange(assets.Where(asset => SaveableAssets.TryGetId(asset, out _) && !listed.Contains(asset))
                    .Select(asset => new AssetIdIssue(AssetIdProblem.NotInCatalog, asset)));
            }
            return issues;
        }

        #endregion

        #region Helpers

        [MenuItem("Tools/Save System/Audit Asset Ids")]
        private static void RunFromMenu()
        {
            var issues = Run();
            if (issues.Count == 0) Debug.Log("Save: asset ids are clean.");
            foreach (var issue in issues)
                Debug.LogWarning($"Save: {issue}", issue.Asset);
        }

        #endregion
    }
}
