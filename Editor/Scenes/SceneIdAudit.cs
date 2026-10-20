using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Extensions.Menus;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SaveSystem.EditorTools
{
    /** <summary>What is wrong with a scene's saved-world setup.</summary> */
    public enum SceneIdProblem
    {
        /** <summary>Another scene saves under the same id, so the two would share one world state: usually a copied scene.</summary> */
        SharedId,
        /** <summary>The scene's <see cref="SaveableScene" /> has no id yet.</summary> */
        MissingId,
        /** <summary>The scene has saveable entities but no <see cref="SaveableScene" />, so they are never saved.</summary> */
        NoSaveableScene,
        /** <summary>The scene has more than one <see cref="SaveableScene" />.</summary> */
        SeveralSaveableScenes,
    }

    /** <summary>One problem found in a scene file.</summary> */
    public readonly struct SceneIdIssue
    {
        #region Properties

        /** <summary>What is wrong.</summary> */
        public SceneIdProblem Problem { get; }

        /** <summary>The scene's path.</summary> */
        public string ScenePath { get; }

        #endregion

        #region Public API

        /** <summary>Makes an issue.</summary> */
        public SceneIdIssue(SceneIdProblem problem, string scenePath)
        {
            Problem = problem;
            ScenePath = scenePath;
        }

        /** <inheritdoc /> */
        public override string ToString() => $"{Problem}: {ScenePath}";

        #endregion
    }

    /** <summary>Checks every scene's saved-world setup by reading the scene files as text, so no scene is opened.</summary> */
    public static class SceneIdAudit
    {
        #region Private Fields

        private static readonly Regex Block = new Regex(@"^--- !u!", RegexOptions.Multiline);
        private static readonly Regex SceneId = new Regex(@"sceneId:\s*\r?\n\s*value:\s*([0-9a-fA-F]{32})?");

        #endregion

        #region Public API

        /** <summary>Audits every scene in the project.</summary> */
        public static List<SceneIdIssue> Run()
        {
            var sceneScript = ScriptGuid(nameof(SaveableScene));
            var entityScript = ScriptGuid(nameof(SaveableEntity));
            var scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => new KeyValuePair<string, string>(path, File.ReadAllText(path)));
            return Audit(scenes, sceneScript, entityScript);
        }

        /**
         * <summary>Audits scene files given as path and text.</summary>
         * <param name="scenes">Each scene's path and its file's text.</param>
         * <param name="sceneScriptGuid">The GUID of the <see cref="SaveableScene" /> script.</param>
         * <param name="entityScriptGuid">The GUID of the <see cref="SaveableEntity" /> script.</param>
         */
        public static List<SceneIdIssue> Audit(IEnumerable<KeyValuePair<string, string>> scenes, string sceneScriptGuid, string entityScriptGuid)
        {
            var issues = new List<SceneIdIssue>();
            var scenesById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var scene in scenes)
            {
                var markers = Block.Split(scene.Value).Where(block => block.Contains("guid: " + sceneScriptGuid)).ToList();
                var hasEntities = scene.Value.Contains("guid: " + entityScriptGuid);
                if (markers.Count == 0)
                {
                    if (hasEntities) issues.Add(new SceneIdIssue(SceneIdProblem.NoSaveableScene, scene.Key));
                    continue;
                }
                if (markers.Count > 1) issues.Add(new SceneIdIssue(SceneIdProblem.SeveralSaveableScenes, scene.Key));

                var match = SceneId.Match(markers[0]);
                var id = match.Success && match.Groups[1].Success ? match.Groups[1].Value.ToLowerInvariant() : null;
                if (id == null)
                {
                    issues.Add(new SceneIdIssue(SceneIdProblem.MissingId, scene.Key));
                    continue;
                }
                if (!scenesById.TryGetValue(id, out var holders)) scenesById[id] = holders = new List<string>();
                holders.Add(scene.Key);
            }

            foreach (var holders in scenesById.Values.Where(holders => holders.Count > 1))
                issues.AddRange(holders.Select(path => new SceneIdIssue(SceneIdProblem.SharedId, path)));
            return issues;
        }

        #endregion

        #region Helpers

        private static string ScriptGuid(string className) => AssetDatabase.FindAssets($"{className} t:MonoScript")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => Path.GetFileNameWithoutExtension(path) == className)
            .Select(AssetDatabase.AssetPathToGUID)
            .FirstOrDefault() ?? string.Empty;

        [MenuItem(SaveMenus.Tools + "Audit Scene Ids", priority = ToolMenus.Commands)]
        private static void RunFromMenu()
        {
            var issues = Run();
            if (issues.Count == 0) Debug.Log("Save: scene ids are clean.");
            foreach (var issue in issues)
                Debug.LogWarning($"Save: {issue}", AssetDatabase.LoadAssetAtPath<SceneAsset>(issue.ScenePath));
        }

        #endregion
    }

    /** <summary>Before every build: fails it if a scene's saved-world id is shared or missing.</summary> */
    public sealed class SceneIdBuildCheck : IPreprocessBuildWithReport
    {
        #region Properties

        /** <inheritdoc /> */
        public int callbackOrder => 1;

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public void OnPreprocessBuild(BuildReport report)
        {
            var blocking = SceneIdAudit.Run().Where(issue => issue.Problem != SceneIdProblem.NoSaveableScene).ToList();
            if (blocking.Count > 0) throw new BuildFailedException("Save: scene id problems: " + string.Join("; ", blocking));
        }

        #endregion
    }
}
