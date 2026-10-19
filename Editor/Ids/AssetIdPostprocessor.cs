using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace SaveSystem.EditorTools
{
    /**
     * <summary>Keeps ids and catalogs current: after saveable assets are imported, moved or deleted it fixes ids and
     * rebuilds the catalogs, once the import has finished.</summary>
     */
    public sealed class AssetIdPostprocessor : AssetPostprocessor
    {
        #region Private Fields

        private static readonly HashSet<string> Pending = new HashSet<string>();
        private static bool _scheduled;

        #endregion

        #region Helpers

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var saveableImports = SaveableAssetDatabase.LoadAt(imported.Where(IsAssetFile)).Select(AssetDatabase.GetAssetPath).ToList();
            var catalogMayBeStale = deleted.Any(IsAssetFile) || moved.Any(IsAssetFile) || imported.Any(IsCatalog);
            if (saveableImports.Count == 0 && !catalogMayBeStale) return;

            Pending.UnionWith(saveableImports);
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            _scheduled = false;
            var imported = Pending.ToList();
            Pending.Clear();
            AssetIdFixer.FixAll(imported);
            AssetCatalogBuilder.RebuildAll();
        }

        private static bool IsAssetFile(string path) => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);

        private static bool IsCatalog(string path) => IsAssetFile(path) && AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(AssetCatalog);

        #endregion
    }

    /** <summary>Before every build: fixes ids, rebuilds catalogs, and fails the build if ids are still shared or missing.</summary> */
    public sealed class AssetIdBuildCheck : IPreprocessBuildWithReport
    {
        /** <inheritdoc /> */
        public int callbackOrder => 0;

        /** <inheritdoc /> */
        public void OnPreprocessBuild(BuildReport report)
        {
            AssetIdFixer.FixAll();
            AssetCatalogBuilder.RebuildAll();
            var blocking = AssetIdAudit.Run().Where(issue => issue.Problem != AssetIdProblem.NotInCatalog).ToList();
            if (blocking.Count > 0) throw new BuildFailedException("Save: asset id problems: " + string.Join("; ", blocking));
        }
    }
}
