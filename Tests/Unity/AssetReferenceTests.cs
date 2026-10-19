using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SaveSystem.Tests
{
    /** <summary>Saving references to assets as ids and finding them again through the catalog.</summary> */
    public sealed class AssetReferenceTests
    {
        public sealed class LoadoutData
        {
            public TestSaveableAsset Main;
            public List<TestSaveableAsset> Bag = new List<TestSaveableAsset>();
            public ISaveableAsset Anything;
            public OtherSaveableAsset Other;
        }

        private sealed class LoadoutSection : SaveSection<LoadoutData>
        {
            public LoadoutData Data { get; set; } = new LoadoutData();

            public override string Key => "loadout";

            protected override LoadoutData Capture() => Data;

            protected override void Restore(LoadoutData data) => Data = data;
        }

        private readonly List<Object> _made = new List<Object>();
        private AssetCatalog _catalog;
        private RecordingLog _log;

        [SetUp]
        public void SetUp()
        {
            _catalog = Make<AssetCatalog>();
            _log = new RecordingLog();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made)
                if (made != null) Object.DestroyImmediate(made);
            _made.Clear();
        }

        private T Make<T>() where T : ScriptableObject
        {
            var made = ScriptableObject.CreateInstance<T>();
            _made.Add(made);
            return made;
        }

        private TestSaveableAsset MakeSaveable(string name)
        {
            var asset = Make<TestSaveableAsset>();
            asset.name = name;
            asset.SetId(AssetId.New());
            return asset;
        }

        private SaveService Service() => new SaveService(UnitySave.CreateSerializer(_catalog, _log));

        private LoadoutData RoundTrip(LoadoutData data, out JToken saved)
        {
            var saving = Service();
            saving.Register(new LoadoutSection { Data = data });
            saved = saving.Capture().Sections["loadout"].Data;
            var serializer = new SaveSerializer();
            var document = SaveDocument.FromJson(serializer.Parse(serializer.ToBytes(saving.Capture().ToJson(), true)));

            var loading = Service();
            var section = new LoadoutSection();
            loading.Register(section);
            Assert.That(loading.Restore(document).Succeeded);
            return section.Data;
        }

        [Test]
        public void References_AreSavedAsIds_AndFoundAgain()
        {
            var sword = MakeSaveable("Sword");
            var potion = MakeSaveable("Potion");
            _catalog.SetAssets(new ScriptableObject[] { sword, potion });

            var loaded = RoundTrip(new LoadoutData { Main = sword, Bag = { potion, sword, null }, Anything = potion }, out var saved);

            Assert.That(saved["Main"].ToString(), Is.EqualTo(sword.AssetId.Value));
            Assert.That(loaded.Main, Is.SameAs(sword));
            Assert.That(loaded.Bag, Is.EqualTo(new Object[] { potion, sword, null }));
            Assert.That(loaded.Anything, Is.SameAs(potion));
            Assert.That(_log.Warnings, Is.Empty);
        }

        [Test]
        public void RenamedAsset_StillResolves()
        {
            var sword = MakeSaveable("Sword");
            _catalog.SetAssets(new ScriptableObject[] { sword });
            var saving = Service();
            saving.Register(new LoadoutSection { Data = new LoadoutData { Main = sword } });
            var document = saving.Capture();
            sword.name = "Renamed Sword";

            var loading = Service();
            var section = new LoadoutSection();
            loading.Register(section);
            loading.Restore(document);
            Assert.That(section.Data.Main, Is.SameAs(sword));
        }

        [Test]
        public void MissingAsset_LoadsAsNull_WithOneWarning()
        {
            var sword = MakeSaveable("Sword");
            var loaded = RoundTrip(new LoadoutData { Main = sword, Bag = { sword, sword } }, out _);
            Assert.That(loaded.Main, Is.Null);
            Assert.That(loaded.Bag, Is.EqualTo(new Object[] { null, null }));
            Assert.That(_log.Warnings, Has.Count.EqualTo(1));
            Assert.That(_log.Warnings[0], Does.Contain(sword.AssetId.Value));
        }

        [Test]
        public void AssetOfAnotherType_LoadsAsNull_WithAWarning()
        {
            var other = Make<OtherSaveableAsset>();
            other.name = "Other";
            other.SetId(AssetId.New());
            _catalog.SetAssets(new ScriptableObject[] { other });

            var saving = Service();
            var section = new LoadoutSection { Data = new LoadoutData { Other = other } };
            saving.Register(section);
            var json = saving.Capture().ToJson();
            json["sections"]["loadout"]["data"]["Main"] = other.AssetId.Value;

            var loading = Service();
            var loaded = new LoadoutSection();
            loading.Register(loaded);
            loading.Restore(SaveDocument.FromJson(json));
            Assert.That(loaded.Data.Main, Is.Null);
            Assert.That(loaded.Data.Other, Is.SameAs(other));
            Assert.That(_log.Warnings.Single(), Does.Contain("not a TestSaveableAsset"));
        }

        [Test]
        public void AssetWithoutAnId_IsSavedAsNothing_WithAWarning()
        {
            var unassigned = Make<TestSaveableAsset>();
            unassigned.name = "Unassigned";
            var loaded = RoundTrip(new LoadoutData { Main = unassigned }, out var saved);
            Assert.That(saved["Main"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(loaded.Main, Is.Null);
            Assert.That(_log.Warnings.Single(), Does.Contain("Unassigned"));
        }

        [Test]
        public void DestroyedAsset_IsSavedAsNothing()
        {
            var sword = MakeSaveable("Sword");
            Object.DestroyImmediate(sword);
            var loaded = RoundTrip(new LoadoutData { Main = sword }, out var saved);
            Assert.That(saved["Main"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(loaded.Main == null);
        }

        [TestCase("\"not-an-id\"")]
        [TestCase("12")]
        [TestCase("true")]
        [TestCase("{\"nested\":{\"a\":1}}")]
        [TestCase("[1,[2]]")]
        public void Garbage_LoadsAsNull(string value)
        {
            var json = JObject.Parse("{\"format\":1,\"sections\":{\"loadout\":{\"version\":1,\"data\":{\"Main\":" + value + ",\"Bag\":[]}}}}");
            var loading = Service();
            var section = new LoadoutSection();
            loading.Register(section);
            Assert.That(loading.Restore(SaveDocument.FromJson(json)).Succeeded);
            Assert.That(section.Data.Main, Is.Null);
            Assert.That(_log.Warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public void Catalog_FindsListedAssets_AndNothingElse()
        {
            var sword = MakeSaveable("Sword");
            var gone = MakeSaveable("Gone");
            Assert.That(_catalog.SetAssets(new ScriptableObject[] { sword, gone }));
            Assert.That(_catalog.SetAssets(new ScriptableObject[] { sword, gone }), Is.False);

            Assert.That(_catalog.TryFind(sword.AssetId, out var found) && found == sword);
            Assert.That(_catalog.TryFind(AssetId.New(), out _), Is.False);
            Assert.That(_catalog.TryFind(default, out _), Is.False);
            var goneId = gone.AssetId;
            Object.DestroyImmediate(gone);
            Assert.That(_catalog.TryFind(goneId, out _), Is.False);
        }

        [Test]
        public void Catalog_WithADuplicateId_KeepsTheFirst_AndWarns()
        {
            var first = MakeSaveable("First");
            var copy = MakeSaveable("Copy");
            copy.SetId(first.AssetId);
            _catalog.SetAssets(new ScriptableObject[] { first, copy, null });

            LogAssert.Expect(LogType.Warning, new Regex("duplicate id"));
            Assert.That(_catalog.TryFind(first.AssetId, out var found) && found == first);
        }

        [Test]
        public void Catalog_SeesChangesAfterSetAssets()
        {
            var sword = MakeSaveable("Sword");
            _catalog.TryFind(sword.AssetId, out _);
            _catalog.SetAssets(new ScriptableObject[] { sword });
            Assert.That(_catalog.TryFind(sword.AssetId, out _));
        }
    }
}
