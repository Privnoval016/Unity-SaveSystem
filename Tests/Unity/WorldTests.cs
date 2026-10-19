using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SaveSystem.Tests
{
    public sealed class WorldTests
    {
        private static readonly List<GameObject> Made = new List<GameObject>();

        private Scene _scene;
        private SaveSerializer _serializer;
        private RecordingLog _log;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.GetActiveScene();
            _serializer = UnitySave.CreateSerializer(null);
            _log = new RecordingLog();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var made in Made)
                if (made != null) Object.DestroyImmediate(made);
            Made.Clear();
        }

        private static GameObject Make(string name)
        {
            var made = new GameObject(name);
            Made.Add(made);
            return made;
        }

        private static SaveableEntity Entity(string name)
        {
            var entity = Make(name).AddComponent<SaveableEntity>();
            if (string.IsNullOrEmpty(entity.Id)) entity.AssignNewId();
            return entity;
        }

        private static T With<T>(SaveableEntity entity) where T : Component => entity.gameObject.AddComponent<T>();

        private SceneSaveSection Section(IEnumerable<SaveableEntity> entities) => new SceneSaveSection("world/test", entities, _serializer, log: _log);

        private SaveDocument Save(params SaveableEntity[] entities)
        {
            var service = new SaveService(_serializer);
            service.Register(Section(entities));
            return SaveDocument.FromJson(_serializer.Parse(_serializer.ToBytes(service.Capture().ToJson(), true)));
        }

        private LoadResult Load(SaveDocument document, params SaveableEntity[] entities)
        {
            var service = new SaveService(_serializer, _log);
            service.Register(Section(entities));
            return service.Restore(document);
        }

        [Test]
        public void Components_RoundTrip()
        {
            var entity = Entity("Chest");
            var chest = With<TestChest>(entity);
            var door = With<TestDoor>(entity);
            chest.Open = true;
            chest.Coins = 3;
            door.Angle = 90;
            door.Hinge = new Vector3(1, 2, 3);
            var document = Save(entity);

            chest.Open = false;
            chest.Coins = 10;
            door.Angle = 0;
            door.Hinge = Vector3.zero;
            var result = Load(document, entity);

            Assert.That(result.Succeeded, result.ToString());
            Assert.That(chest.Open);
            Assert.That(chest.Coins, Is.EqualTo(3));
            Assert.That(door.Angle, Is.EqualTo(90));
            Assert.That(door.Hinge, Is.EqualTo(new Vector3(1, 2, 3)));
        }

        [Test]
        public void ManyEntities_KeepTheirOwnState()
        {
            var entities = Enumerable.Range(0, 30).Select(i => Entity($"Chest {i}")).ToArray();
            var chests = entities.Select(With<TestChest>).ToArray();
            for (var i = 0; i < chests.Length; i++) chests[i].Coins = i;
            var document = Save(entities);
            foreach (var chest in chests) chest.Coins = -1;

            Load(document, entities);
            Assert.That(chests.Select(chest => chest.Coins), Is.EqualTo(Enumerable.Range(0, 30)));
        }

        [Test]
        public void DespawnedEntity_StaysGone_AfterReload()
        {
            var pickup = Entity("Pickup");
            With<TestChest>(pickup);
            var id = pickup.Id;
            var service = new SaveService(_serializer);
            var section = Section(new[] { pickup });
            service.Register(section);
            pickup.Despawn();
            Assert.That(section.Despawned, Does.Contain(id));
            var document = service.Capture();

            var reloaded = Make("Pickup").AddComponent<SaveableEntity>();
            SetId(reloaded, id);
            With<TestChest>(reloaded);
            Assert.That(Load(document, reloaded).Succeeded);
            Assert.That(reloaded == null);
        }

        [Test]
        public void UnregisteringTheScene_HoldsItsState_UntilItLoadsAgain()
        {
            var entity = Entity("Lever");
            var chest = With<TestChest>(entity);
            chest.Coins = 77;
            var service = new SaveService(_serializer);
            var section = Section(new[] { entity });
            service.Register(section);
            service.Unregister(section);
            chest.Coins = 0;

            var report = service.Register(Section(new[] { entity }));
            Assert.That(report.Outcome, Is.EqualTo(SectionOutcome.Restored));
            Assert.That(chest.Coins, Is.EqualTo(77));
        }

        [Test]
        public void RemovedComponent_KeepsItsData()
        {
            var entity = Entity("Chest");
            var chest = With<TestChest>(entity);
            With<TestDoor>(entity).Angle = 45;
            chest.Coins = 5;
            var document = Save(entity);

            Object.DestroyImmediate(entity.GetComponent<TestDoor>());
            var service = new SaveService(_serializer);
            service.Register(Section(new[] { entity }));
            Assert.That(service.Restore(document).Succeeded);
            var resaved = service.Capture();

            var door = resaved.Sections["world/test"].Data["entities"][entity.Id]["sections"]["door"];
            Assert.That(door["data"]["Angle"].ToObject<float>(), Is.EqualTo(45));
        }

        [Test]
        public void NewComponent_WithNoData_KeepsItsSceneState()
        {
            var entity = Entity("Chest");
            With<TestChest>(entity).Coins = 5;
            var document = Save(entity);

            var door = With<TestDoor>(entity);
            door.Angle = 30;
            Assert.That(Load(document, entity).Succeeded);
            Assert.That(door.Angle, Is.EqualTo(30));
        }

        [Test]
        public void EntityWithNoData_KeepsItsSceneState()
        {
            var document = Save();
            var entity = Entity("New In This Version");
            var chest = With<TestChest>(entity);
            chest.Coins = 12;
            Assert.That(Load(document, entity).Succeeded);
            Assert.That(chest.Coins, Is.EqualTo(12));
        }

        [Test]
        public void DataForEntitiesNotInTheScene_IsKept()
        {
            var removed = Entity("Removed From The Level");
            With<TestChest>(removed).Coins = 4;
            var removedId = removed.Id;
            var document = Save(removed);
            Object.DestroyImmediate(removed.gameObject);

            var other = Entity("Other");
            With<TestChest>(other);
            var service = new SaveService(_serializer);
            service.Register(Section(new[] { other }));
            service.Restore(document);
            var resaved = service.Capture();

            Assert.That(resaved.Sections["world/test"].Data["entities"][removedId], Is.Not.Null);
        }

        [Test]
        public void ComponentUpgrades_Run()
        {
            var entity = Entity("Door");
            var door = With<TestDoor>(entity);
            var json = new JObject
            {
                ["entities"] = new JObject
                {
                    [entity.Id] = JObject.Parse("{\"format\":1,\"sections\":{\"door\":{\"version\":1,\"data\":{\"degrees\":120}}}}"),
                },
                ["despawned"] = new JArray(),
            };
            var document = new SaveDocument();
            document.Sections["world/test"] = new SectionRecord(1, json);

            Assert.That(Load(document, entity).Succeeded);
            Assert.That(door.Angle, Is.EqualTo(120));
        }

        [Test]
        public void OneBadEntity_FailsTheScene_AndRestoresNothing()
        {
            var good = Entity("Good");
            var goodChest = With<TestChest>(good);
            var bad = Entity("Bad");
            With<TestChest>(bad);
            goodChest.Coins = 1;
            var document = Save(good, bad);
            document.Sections["world/test"].Data["entities"][bad.Id]["sections"]["chest"]["data"]["Coins"] = "lots";
            goodChest.Coins = 50;

            var result = Load(document, good, bad);
            Assert.That(result.Status, Is.EqualTo(LoadStatus.SectionFailed));
            Assert.That(goodChest.Coins, Is.EqualTo(50));
        }

        [Test]
        public void ThrowingComponent_IsReported_AndTheRestRestore()
        {
            var good = Entity("Good");
            var goodChest = With<TestChest>(good);
            var bad = Entity("Bad");
            var badChest = With<TestChest>(bad);
            goodChest.Coins = 1;
            var document = Save(good, bad);
            goodChest.Coins = 50;
            badChest.ThrowOnRestore = true;

            var result = Load(document, good, bad);
            Assert.That(result.Status, Is.EqualTo(LoadStatus.RestoreFailed));
            Assert.That(goodChest.Coins, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateAndMissingIds_AreSkippedWithAWarning()
        {
            var first = Entity("First");
            var copy = Entity("Copy");
            SetId(copy, first.Id);
            var unnamed = Make("No Id").AddComponent<SaveableEntity>();
            SetId(unnamed, "");

            Section(new[] { first, copy, unnamed });
            Assert.That(_log.Warnings.Count(warning => warning.Contains("Copy")), Is.EqualTo(1));
            Assert.That(_log.Warnings.Count(warning => warning.Contains("No Id")), Is.EqualTo(1));
        }

        [Test]
        public void DuplicatedEntity_GetsAFreshIdInTheEditor()
        {
            var original = Entity("Original");
            var copyObject = Object.Instantiate(original.gameObject);
            Made.Add(copyObject);
            var copy = copyObject.GetComponent<SaveableEntity>();
            SetId(copy, original.Id);
            InvokeOnValidate(copy);
            Assert.That(copy.Id, Is.Not.EqualTo(original.Id));
            Assert.That(copy.Id, Has.Length.EqualTo(32));
        }

        [Test]
        public void SaveableScene_FindsItsEntities()
        {
            var marker = Make("Saveable Scene").AddComponent<SaveableScene>();
            marker.AssignNewId();
            var entity = Entity("Chest");
            With<TestChest>(entity).Coins = 9;

            Assert.That(SaveableScene.FindIn(_scene), Is.SameAs(marker));
            var service = new SaveService(_serializer);
            service.Register(marker.CreateSection(_serializer));
            Assert.That(service.Capture().Sections[marker.Key].Data["entities"][entity.Id], Is.Not.Null);
            Assert.That(marker.Key, Does.StartWith("world/"));
        }

        [Test]
        public void SceneWithoutAMarker_FindsNothing()
        {
            Assert.That(SaveableScene.FindIn(_scene), Is.Null);
        }

        [Test]
        public void SectionWithNoData_LeavesTheSceneAlone()
        {
            var entity = Entity("Chest");
            var chest = With<TestChest>(entity);
            chest.Coins = 3;
            Assert.That(Load(new SaveDocument(), entity).Succeeded);
            Assert.That(chest.Coins, Is.EqualTo(3));
            Assert.That(entity != null);
        }

        private static void SetId(SaveableEntity entity, string id) =>
            typeof(SaveableEntity).GetField("entityId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(entity, id);

        private static void InvokeOnValidate(SaveableEntity entity) =>
            typeof(SaveableEntity).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(entity, null);
    }
}
