using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    public sealed class FixtureTests
    {
        public sealed class HealthData
        {
            [JsonProperty("current")] public int Current;
            [JsonProperty("max")] public int Max;
        }

        public sealed class PlayerData
        {
            [JsonProperty("name")] public string Name;
            [JsonProperty("health")] public HealthData Health;
        }

        /** <summary>The test player section at version 3, with the upgrades from 1 and 2.</summary> */
        public sealed class PlayerSection : SaveSection<PlayerData>
        {
            private static readonly SectionUpgrade[] Steps =
            {
                new SectionUpgrade(1, data =>
                {
                    data["health"] = data["hp"];
                    ((JObject)data).Remove("hp");
                    return data;
                }),
                new SectionUpgrade(2, data =>
                {
                    var health = data["health"];
                    data["health"] = new JObject { ["current"] = health, ["max"] = health };
                    return data;
                }),
            };

            public PlayerData Data { get; private set; }

            public override string Key => "player";
            public override int Version => 3;
            public override IReadOnlyList<SectionUpgrade> Upgrades => Steps;

            protected override PlayerData Capture() => Data;

            protected override void Restore(PlayerData data) => Data = data;
        }

        private static string FixturePath(string name, [CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures", name);

        private static (SaveService service, PlayerSection player, LoadResult result) Load(string fixture)
        {
            var storage = new MemorySaveStorage();
            storage.Overwrite("fixture.save", File.ReadAllBytes(FixturePath(fixture)));
            var store = new SaveFileStore(storage, new SaveSerializer(), new SaveFileCodec(alsoReadable: new[] { new GzipTransform() }));
            var read = store.Read("fixture.save", SaveDocument.FromJson);
            Assert.That(read.Status, Is.EqualTo(ReadStatus.Ok), read.Problem);

            var service = new SaveService();
            var player = new PlayerSection();
            service.Register(player);
            return (service, player, service.Restore(read.Value));
        }

        [TestCase("player-v1.save", SectionOutcome.Upgraded)]
        [TestCase("player-v2.save", SectionOutcome.Upgraded)]
        [TestCase("player-v3.save", SectionOutcome.Restored)]
        [TestCase("player-v3-gzip.save", SectionOutcome.Restored)]
        public void Fixture_LoadsToTheExpectedState(string fixture, SectionOutcome outcome)
        {
            var (service, _, result) = Load(fixture);
            Assert.That(result.Status, Is.EqualTo(LoadStatus.Loaded), result.Problem);
            Assert.That(result.Sections[0].Outcome, Is.EqualTo(outcome));

            var expected = JToken.Parse(File.ReadAllText(FixturePath("player-expected.json")));
            Assert.That(JToken.DeepEquals(service.Capture().Sections["player"].Data, expected));
        }

        [TestCase("player-v1.save")]
        [TestCase("player-v3-gzip.save")]
        public void Fixture_KeepsItsUnknownSectionExactly(string fixture)
        {
            var (service, _, _) = Load(fixture);
            var retired = service.Capture().Sections["retired"];
            Assert.That(retired.Version, Is.EqualTo(2));
            Assert.That(JToken.DeepEquals(retired.Data, JToken.Parse("{\"kept\":[\"exactly\",1.25,null,{\"nested\":true}]}")));
        }

        [Test]
        public void Fixture_KeepsItsMeta()
        {
            var storage = new MemorySaveStorage();
            storage.Overwrite("fixture.save", File.ReadAllBytes(FixturePath("player-v2.save")));
            var document = new SaveFileStore(storage, new SaveSerializer()).Read("fixture.save", SaveDocument.FromJson).Value;
            Assert.That(document.Meta["savedAtUtc"].ToString(), Is.EqualTo("2026-10-19T12:00:00Z"));
            Assert.That(document.Meta["info"]["place"].ToString(), Is.EqualTo("harbour"));
        }
    }
}
