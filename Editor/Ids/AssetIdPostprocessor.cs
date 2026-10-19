using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SaveSystem.EditorTools
{
    /**
     * <summary>Keeps ids and catalogs current as saveable assets are imported, moved and deleted, loading only the
     * changed files and those sharing their ids. The project is scanned once per session, and whenever the set of
     * saveable types changes, which also gives existing assets of a newly saveable type their ids.</summary>
     */
    [InitializeOnLoad]
    public sealed class AssetIdPostprocessor : AssetPostprocessor
    {
        #region Private Fields

        private const string AssetExtension = ".asset";
        private static readonly string TypesRecordPath = Path.Combine("Library", "SaveSystem", "saveable-types.txt");

        private static readonly HashSet<string> PendingImports = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> PendingRemovals = new HashSet<string>(StringComparer.Ordinal);
        private static SaveableAssetIndex _index;
        private static bool _scheduled;

        #endregion

        #region MonoBehaviour Callbacks

        static AssetIdPostprocessor() => EditorApplication.delayCall += FixAllIfSaveableTypesChanged;

        #endregion

        #region Public API

        /** <summary>The id index, built from a project scan the first time it is needed.</summary> */
        public static SaveableAssetIndex Index => _index ?? (_index = BuildIndex(SaveableAssetDatabase.FindAll()));

        #endregion

        #region Helpers

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var removals = deleted.Concat(movedFrom).Where(IsAssetFile).ToList();
            var imports = imported.Concat(moved).Where(IsAssetFile).ToList();
            if (removals.Count == 0 && imports.Count == 0) return;

            PendingRemovals.UnionWith(removals);
            PendingImports.UnionWith(imports);
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += ProcessPending;
        }

        private static void ProcessPending()
        {
            _scheduled = false;
            var imported = PendingImports.ToList();
            var removed = PendingRemovals.ToList();
            PendingImports.Clear();
            PendingRemovals.Clear();

            foreach (var path in removed)
                _index?.Forget(path);
            var importedAssets = SaveableAssetDatabase.LoadAt(imported);
            if (importedAssets.Count == 0)
            {
                if (removed.Count > 0) AssetCatalogBuilder.Merge(AssetCatalogBuilder.FindCatalogs(), Array.Empty<ScriptableObject>());
                return;
            }

            var index = Index;
            Record(index, importedAssets);
            var involved = SaveableAssetDatabase.LoadAt(imported.Concat(index.PathsHolding(importedAssets.Select(SaveableAssets.IdOf))));
            var involvedPaths = new HashSet<string>(involved.Select(AssetDatabase.GetAssetPath), StringComparer.Ordinal);
            AssetIdFixer.Fix(involved, imported, id => index.PathsHolding(new[] { id }).Any(path => !involvedPaths.Contains(path)));
            Record(index, involved);
            AssetCatalogBuilder.Merge(AssetCatalogBuilder.FindCatalogs(), involved);
        }

        private static SaveableAssetIndex BuildIndex(IEnumerable<ScriptableObject> assets)
        {
            var index = new SaveableAssetIndex();
            Record(index, assets);
            return index;
        }

        private static void Record(SaveableAssetIndex index, IEnumerable<ScriptableObject> assets)
        {
            foreach (var file in assets.GroupBy(AssetDatabase.GetAssetPath))
                index.Record(file.Key, file.Select(SaveableAssets.IdOf));
        }

        private static void FixAllIfSaveableTypesChanged()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var types = string.Join("\n", TypeCache.GetTypesDerivedFrom<ISaveableAsset>()
                .Where(type => !type.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(type))
                .Select(type => type.FullName)
                .OrderBy(name => name, StringComparer.Ordinal));
            var recorded = File.Exists(TypesRecordPath) ? File.ReadAllText(TypesRecordPath) : null;
            if (recorded == types) return;

            var assets = SaveableAssetDatabase.FindAll();
            AssetIdFixer.Fix(assets);
            AssetCatalogBuilder.Rebuild(AssetCatalogBuilder.FindCatalogs(), assets);
            _index = BuildIndex(assets);
            Directory.CreateDirectory(Path.GetDirectoryName(TypesRecordPath));
            File.WriteAllText(TypesRecordPath, types);
        }

        private static bool IsAssetFile(string path) => path.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase);

        #endregion
    }

    /** <summary>Before every build: fixes asset ids, rebuilds catalogs, and fails the build if any asset or scene id is still shared or missing.</summary> */
    public sealed class AssetIdBuildCheck : IPreprocessBuildWithReport
    {
        #region Properties

        /** <inheritdoc /> */
        public int callbackOrder => 0;

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public void OnPreprocessBuild(BuildReport report)
        {
            var assets = SaveableAssetDatabase.FindAll();
            AssetIdFixer.Fix(assets);
            AssetCatalogBuilder.Rebuild(AssetCatalogBuilder.FindCatalogs(), assets);
            var blocking = AssetIdAudit.Run().Where(issue => issue.Problem != AssetIdProblem.NotInCatalog).Select(issue => issue.ToString())
                .Concat(SceneIdAudit.Run().Where(issue => issue.Problem != SceneIdProblem.NoSaveableScene).Select(issue => issue.ToString()))
                .ToList();
            if (blocking.Count > 0) throw new BuildFailedException("Save: id problems: " + string.Join("; ", blocking));
        }

        #endregion
    }
}
