using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>Held data, registration and the prepare/apply split across several loads and saves.</summary> */
    public sealed class LoadLifecycleTests
    {
        private static SaveDocument ThroughBytes(SaveDocument document)
        {
            var serializer = new SaveSerializer();
            return SaveDocument.FromJson(serializer.Parse(serializer.ToBytes(document.ToJson(), true)));
        }

        private static SaveDocument Saved(string key, int value, int version = 1)
        {
            var document = new SaveDocument();
            document.Sections[key] = new SectionRecord(version, new JObject { ["Value"] = value });
            return document;
        }

        [Test]
        public void Register_WhenRestoreThrows_IsNotRegistered_AndTheDataStaysHeld()
        {
            var log = new RecordingLog();
            var service = new SaveService(log: log);
            service.Restore(Saved("area", 9));

            var report = service.Register(new CounterSection("area") { ThrowOnRestore = true });

            Assert.That(report.Outcome, Is.EqualTo(SectionOutcome.Failed));
            Assert.That(service.IsRegistered("area"), Is.False);
            Assert.That(service.Capture().Sections["area"].Data["Value"].ToObject<int>(), Is.EqualTo(9));
            Assert.That(log.Errors, Has.Count.EqualTo(2));

            var working = new CounterSection("area");
            Assert.That(service.Register(working).Outcome, Is.EqualTo(SectionOutcome.Restored));
            Assert.That(working.Value, Is.EqualTo(9));
        }

        [Test]
        public void SectionRegisteredBetweenPrepareAndApply_IsRestored()
        {
            var service = new SaveService();
            var load = service.Prepare(Saved("area", 7));
            var area = new CounterSection("area");
            service.Register(area);

            var result = service.Apply(load);

            Assert.That(result.Succeeded);
            Assert.That(area.Value, Is.EqualTo(7));
            Assert.That(service.HeldKeys, Does.Not.Contain("area"));
        }

        [Test]
        public void SectionRegisteredBetweenPrepareAndApply_ThatCantRead_KeepsItsDataHeld()
        {
            var service = new SaveService();
            var load = service.Prepare(Saved("area", 7, 1));
            var area = new CounterSection("area", 2);
            service.Register(area);

            var result = service.Apply(load);

            Assert.That(result.Status, Is.EqualTo(LoadStatus.RestoreFailed));
            Assert.That(service.IsRegistered("area"), Is.False);
            Assert.That(service.Capture().Sections["area"].Data["Value"].ToObject<int>(), Is.EqualTo(7));
        }

        [Test]
        public void SectionUnregisteredBetweenPrepareAndApply_HoldsTheLoadedData()
        {
            var service = new SaveService();
            var area = new CounterSection("area");
            service.Register(area);
            var load = service.Prepare(Saved("area", 5));
            service.Unregister(area);

            service.Apply(load);
            Assert.That(service.Capture().Sections["area"].Data["Value"].ToObject<int>(), Is.EqualTo(5));
        }

        [Test]
        public void HeldSection_SurvivesManyCycles_Unchanged()
        {
            var original = Saved("retired", 3);
            original.Sections["retired"] = new SectionRecord(4, JToken.Parse("{\"deep\":{\"list\":[1,2.5,\"ü\",null,{\"x\":true}]}}"));
            var document = ThroughBytes(original);

            var kept = new CounterSection("kept");
            for (var cycle = 0; cycle < 5; cycle++)
            {
                var service = new SaveService();
                service.Register(kept);
                service.Restore(document);
                if (cycle % 2 == 0)
                {
                    service.Unregister(kept);
                    service.Register(kept);
                }
                document = ThroughBytes(service.Capture());
            }

            Assert.That(document.Sections["retired"].Version, Is.EqualTo(4));
            Assert.That(JToken.DeepEquals(document.Sections["retired"].Data, original.Sections["retired"].Data));
        }

        [Test]
        public void UpgradedData_IsSavedAtTheNewVersion_AndReloadsWithoutUpgradingAgain()
        {
            var upgrades = 0;
            CounterSection Make() => new CounterSection("a", 2, 0, new SectionUpgrade(1, data =>
            {
                upgrades++;
                data["Value"] = data["Value"].ToObject<int>() + 100;
                return data;
            }));

            var first = new SaveService();
            var a = Make();
            first.Register(a);
            first.Restore(Saved("a", 1));
            var resaved = ThroughBytes(first.Capture());

            var second = new SaveService();
            var b = Make();
            second.Register(b);
            second.Restore(resaved);

            Assert.That(upgrades, Is.EqualTo(1));
            Assert.That(b.Value, Is.EqualTo(101));
        }

        [Test]
        public void RestoringOneDocumentTwice_GivesTheSameState()
        {
            var section = new CounterSection("a", 2, 0, new SectionUpgrade(1, data => new JObject { ["Value"] = data["Value"].ToObject<int>() * 2 }));
            var service = new SaveService();
            service.Register(section);
            var document = Saved("a", 4);

            service.Restore(document);
            var once = section.Value;
            service.Restore(document);
            Assert.That(section.Value, Is.EqualTo(once).And.EqualTo(8));
            Assert.That(document.Sections["a"].Data["Value"].ToObject<int>(), Is.EqualTo(4));
        }

        [Test]
        public void LoadResult_FromBackup_LeavesTheOriginalUntouched()
        {
            var original = new LoadResult(LoadStatus.Loaded);
            var fromBackup = original.FromBackup("damaged");
            Assert.That(original.UsedBackup, Is.False);
            Assert.That(original.Problem, Is.Null);
            Assert.That(fromBackup.UsedBackup);
            Assert.That(fromBackup.Problem, Is.EqualTo("damaged"));
            Assert.That(fromBackup.Status, Is.EqualTo(LoadStatus.Loaded));
        }

        [Test]
        public void NewGame_WithAFailingDefault_ChangesNothing()
        {
            var service = new SaveService();
            var good = new CounterSection("good") { Value = 5, DefaultValue = 0 };
            service.Register(good);
            service.Register(new ThrowingDefaultSection());
            Assert.That(service.NewGame().Status, Is.EqualTo(LoadStatus.SectionFailed));
            Assert.That(good.Value, Is.EqualTo(5));
        }

        private sealed class ThrowingDefaultSection : SaveSection<CounterData>
        {
            public override string Key => "throws";

            protected override CounterData Capture() => new CounterData();

            protected override void Restore(CounterData data) { }

            protected override CounterData CreateDefault() => throw new InvalidOperationException("no default");
        }
    }
}
