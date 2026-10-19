using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaveSystem.EditorTools;

namespace SaveSystem.Tests
{
    public sealed class AssetIdPlannerTests
    {
        private const string GuidA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string GuidB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string GuidC = "cccccccccccccccccccccccccccccccc";

        private static AssetId Id(string text) => AssetId.Parse(text);

        private static Dictionary<int, AssetId> Plan(params AssetIdRecord[] records) =>
            AssetIdPlanner.Plan(records).ToDictionary(change => change.Key, change => change.Value);

        [Test]
        public void NewAsset_GetsItsFileGuid()
        {
            var plan = Plan(new AssetIdRecord("Assets/a.asset", GuidA, true, default, true));
            Assert.That(plan[0], Is.EqualTo(Id(GuidA)));
        }

        [Test]
        public void AssetWithAnId_IsLeftAlone()
        {
            Assert.That(Plan(new AssetIdRecord("Assets/a.asset", GuidA, true, Id(GuidA), false)), Is.Empty);
        }

        [Test]
        public void StoredIdDifferentFromItsGuid_IsKept()
        {
            Assert.That(Plan(new AssetIdRecord("Assets/a.asset", GuidB, true, Id(GuidC), true)), Is.Empty);
        }

        [Test]
        public void Duplicate_OriginalKeeps_CopyGetsItsOwnGuid()
        {
            var plan = Plan(
                new AssetIdRecord("Assets/z original.asset", GuidA, true, Id(GuidA), false),
                new AssetIdRecord("Assets/a copy.asset", GuidB, true, Id(GuidA), true));
            Assert.That(plan.Keys, Is.EqualTo(new[] { 1 }));
            Assert.That(plan[1], Is.EqualTo(Id(GuidB)));
        }

        [Test]
        public void Duplicate_WhenNeitherOwnsTheGuid_TheOneAlreadyThereKeeps()
        {
            var plan = Plan(
                new AssetIdRecord("Assets/b.asset", GuidB, true, Id(GuidC), true),
                new AssetIdRecord("Assets/a.asset", GuidA, true, Id(GuidC), false));
            Assert.That(plan.Keys, Is.EqualTo(new[] { 0 }));
            Assert.That(plan[0], Is.EqualTo(Id(GuidB)));
        }

        [Test]
        public void Duplicate_TheOneWhoseGuidItIs_Keeps()
        {
            var plan = Plan(
                new AssetIdRecord("Assets/a.asset", GuidA, true, Id(GuidC), false),
                new AssetIdRecord("Assets/c.asset", GuidC, true, Id(GuidC), true));
            Assert.That(plan.Keys, Is.EqualTo(new[] { 0 }));
            Assert.That(plan[0], Is.EqualTo(Id(GuidA)));
        }

        [Test]
        public void Duplicate_AllJustImported_FirstByPathKeeps()
        {
            var plan = Plan(
                new AssetIdRecord("Assets/b.asset", GuidB, true, Id(GuidC), true),
                new AssetIdRecord("Assets/a.asset", GuidA, true, Id(GuidC), true),
                new AssetIdRecord("Assets/c.asset", AssetId.New().Value, true, Id(GuidC), true));
            Assert.That(plan.Keys, Is.EquivalentTo(new[] { 0, 2 }));
        }

        [Test]
        public void SubAsset_NeverUsesTheFileGuid()
        {
            var plan = Plan(
                new AssetIdRecord("Assets/a.asset", GuidA, true, Id(GuidA), false),
                new AssetIdRecord("Assets/a.asset", GuidA, false, default, true));
            Assert.That(plan[1], Is.Not.EqualTo(Id(GuidA)));
            Assert.That(plan[1].IsEmpty, Is.False);
        }

        [Test]
        public void GuidAlreadyUsedAsAnotherAssetsId_GetsARandomId()
        {
            var plan = Plan(
                new AssetIdRecord("Assets/a.asset", GuidA, true, Id(GuidB), false),
                new AssetIdRecord("Assets/b.asset", GuidB, true, default, true));
            Assert.That(plan[1], Is.Not.EqualTo(Id(GuidB)));
        }

        [Test]
        public void RandomIds_NeverCollide()
        {
            var sequence = new Queue<AssetId>(new[] { Id(GuidA), Id(GuidA), Id(GuidB) });
            var changes = AssetIdPlanner.Plan(new[]
            {
                new AssetIdRecord("Assets/x.asset", GuidC, true, Id(GuidA), false),
                new AssetIdRecord("Assets/x.asset", GuidC, false, default, true),
            }, () => sequence.Dequeue());
            Assert.That(changes.Single().Value, Is.EqualTo(Id(GuidB)));
        }

        [Test]
        public void ManyCopies_AllEndUnique()
        {
            var records = Enumerable.Range(0, 50)
                .Select(i => new AssetIdRecord($"Assets/{i:000}.asset", AssetId.New().Value, i % 3 != 0, Id(GuidA), i > 0))
                .ToArray();
            var plan = Plan(records);
            var finalIds = records.Select((record, i) => plan.TryGetValue(i, out var id) ? id : record.Current).ToList();
            Assert.That(finalIds.Distinct().Count(), Is.EqualTo(50));
            Assert.That(plan.ContainsKey(0), Is.False);
        }

        [Test]
        public void InvalidFileGuid_FallsBackToRandom()
        {
            var plan = Plan(new AssetIdRecord("Assets/a.asset", "", true, default, true));
            Assert.That(plan[0].IsEmpty, Is.False);
        }
    }
}
