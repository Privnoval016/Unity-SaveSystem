using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaveSystem.EditorTools;

namespace SaveSystem.Tests
{
    /** <summary>The scene audit, on scene text as Unity writes it.</summary> */
    public sealed class SceneIdAuditTests
    {
        private const string SceneScript = "1111111111111111aaaaaaaaaaaaaaaa";
        private const string EntityScript = "2222222222222222bbbbbbbbbbbbbbbb";
        private const string IdA = "0123456789abcdef0123456789abcdef";
        private const string IdB = "fedcba9876543210fedcba9876543210";

        private static string Marker(string id) =>
            "--- !u!114 &100\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: " + SceneScript + ", type: 3}\n  sceneId:\n    value: " + id + "\n";

        private const string Entity = "--- !u!114 &200\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: " + EntityScript + ", type: 3}\n  entityId: abc\n";
        private const string Plain = "--- !u!1 &1\nGameObject:\n  m_Name: Rock\n";

        private static List<SceneIdIssue> Audit(params (string path, string text)[] scenes) =>
            SceneIdAudit.Audit(scenes.Select(scene => new KeyValuePair<string, string>(scene.path, scene.text)), SceneScript, EntityScript);

        [Test]
        public void DistinctIds_AreClean()
        {
            Assert.That(Audit(("A.unity", Marker(IdA) + Entity), ("B.unity", Marker(IdB)), ("Menu.unity", Plain)), Is.Empty);
        }

        [Test]
        public void CopiedScene_SharingAnId_IsFlaggedOnBoth()
        {
            var issues = Audit(("Area.unity", Marker(IdA)), ("Area Copy.unity", Marker(IdA.ToUpperInvariant())));
            Assert.That(issues.Select(issue => issue.Problem), Is.All.EqualTo(SceneIdProblem.SharedId));
            Assert.That(issues.Select(issue => issue.ScenePath), Is.EquivalentTo(new[] { "Area.unity", "Area Copy.unity" }));
        }

        [Test]
        public void EntitiesWithoutAMarker_AreFlagged()
        {
            Assert.That(Audit(("Lost.unity", Plain + Entity)).Single().Problem, Is.EqualTo(SceneIdProblem.NoSaveableScene));
        }

        [Test]
        public void MarkerWithoutAnId_IsFlagged()
        {
            Assert.That(Audit(("New.unity", Marker(""))).Single().Problem, Is.EqualTo(SceneIdProblem.MissingId));
        }

        [Test]
        public void TwoMarkers_AreFlagged()
        {
            Assert.That(Audit(("Twice.unity", Marker(IdA) + Marker(IdB))).Single().Problem, Is.EqualTo(SceneIdProblem.SeveralSaveableScenes));
        }

        [Test]
        public void WindowsLineEndings_AreRead()
        {
            Assert.That(Audit(("A.unity", Marker(IdA).Replace("\n", "\r\n")), ("B.unity", Marker(IdA))).Count, Is.EqualTo(2));
        }
    }

    /** <summary>The id index the postprocessor uses to avoid project scans.</summary> */
    public sealed class SaveableAssetIndexTests
    {
        private static readonly AssetId A = AssetId.New();
        private static readonly AssetId B = AssetId.New();

        [Test]
        public void Record_ThenFind()
        {
            var index = new SaveableAssetIndex();
            index.Record("x.asset", new[] { A, B });
            index.Record("y.asset", new[] { A });
            Assert.That(index.Contains(A));
            Assert.That(index.PathsHolding(new[] { A }), Is.EquivalentTo(new[] { "x.asset", "y.asset" }));
            Assert.That(index.PathsHolding(new[] { A, B }).Count(), Is.EqualTo(2));
        }

        [Test]
        public void Rerecording_ReplacesAFilesIds()
        {
            var index = new SaveableAssetIndex();
            index.Record("x.asset", new[] { A });
            index.Record("x.asset", new[] { B });
            Assert.That(index.Contains(A), Is.False);
            Assert.That(index.PathsHolding(new[] { B }), Is.EqualTo(new[] { "x.asset" }));
        }

        [Test]
        public void Forget_RemovesAFile_AndEmptyIdsAreIgnored()
        {
            var index = new SaveableAssetIndex();
            index.Record("x.asset", new[] { A, default });
            index.Forget("x.asset");
            index.Forget("never.asset");
            Assert.That(index.Contains(A), Is.False);
            Assert.That(index.Contains(default), Is.False);
        }
    }
}
