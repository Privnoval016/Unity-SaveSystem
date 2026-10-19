using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaveSystem.Tests
{
    /** <summary>A save and load at the scale a full game reaches; the pass message carries the timings.</summary> */
    public sealed class ScaleBenchmarks
    {
        private const int Sections = 300;
        private const int Entities = 1000;

        private readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made)
                if (made != null) Object.DestroyImmediate(made);
            _made.Clear();
        }

        [Test]
        public async Task ManySectionsAndABigArea()
        {
            var serializer = UnitySave.CreateSerializer(null);
            var service = new SaveService(serializer);
            for (var i = 0; i < Sections; i++)
            {
                var profile = new ProfileSection();
                profile.Data.Numbers = Enumerable.Range(0, 20).ToList();
                profile.Data.Name = "section " + i;
                service.Register(new KeyedProfile($"system-{i:000}", profile));
            }

            var entities = new List<SaveableEntity>();
            for (var i = 0; i < Entities; i++)
            {
                var go = new GameObject("Entity " + i);
                _made.Add(go);
                var entity = go.AddComponent<SaveableEntity>();
                if (string.IsNullOrEmpty(entity.Id)) entity.AssignNewId();
                go.AddComponent<TestChest>().Coins = i;
                go.AddComponent<TestDoor>().Hinge = new Vector3(i, i * 0.5f, 1);
                entities.Add(entity);
            }

            var clock = Stopwatch.StartNew();
            var scene = new SceneSaveSection("world/area", entities, serializer);
            var buildMs = clock.Elapsed.TotalMilliseconds;
            service.Register(scene);

            clock.Restart();
            var document = service.Capture();
            var captureMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            service.Capture();
            var secondCaptureMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            scene.Capture(serializer.Json);
            var sceneOnlyMs = clock.Elapsed.TotalMilliseconds;

            var storage = new MemorySaveStorage();
            var slots = new SaveSlots<object>(service, storage, null, new SlotOptions(new ImmediateSaveWorker()));
            clock.Restart();
            await slots.SaveAsync(SlotId.Manual(1));
            var saveMs = clock.Elapsed.TotalMilliseconds;
            var bytes = storage.Read("manual-01.save").Length;

            clock.Restart();
            var gzipped = new SaveFileCodec(new[] { new GzipTransform() }).Encode(serializer.ToBytes(document.ToJson(), false)).Length;
            var gzipMs = clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            var result = await slots.LoadAsync(SlotId.Manual(1));
            var loadMs = clock.Elapsed.TotalMilliseconds;
            Assert.That(result.Succeeded, result.ToString());

            Assert.Pass($"sections {Sections}, entities {Entities}: build scene section {buildMs:F0} ms; capture (main thread) {captureMs:F0} ms first, {secondCaptureMs:F0} ms again, scene alone {sceneOnlyMs:F0} ms; " +
                        $"save total {saveMs:F0} ms; load total {loadMs:F0} ms; file {bytes / 1024} KB plain, {gzipped / 1024} KB gzip ({gzipMs:F0} ms)");
        }

        /** <summary>A profile section under a chosen key.</summary> */
        private sealed class KeyedProfile : SaveSection<ProfileData>
        {
            private readonly string _key;
            private readonly ProfileSection _inner;

            public KeyedProfile(string key, ProfileSection inner)
            {
                _key = key;
                _inner = inner;
            }

            public override string Key => _key;

            protected override ProfileData Capture() => _inner.Data;

            protected override void Restore(ProfileData data) => _inner.Data = data;
        }
    }
}
