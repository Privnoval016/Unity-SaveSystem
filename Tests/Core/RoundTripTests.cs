using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>Capturing and restoring sections, including awkward values and all-or-nothing loads.</summary> */
    public sealed class RoundTripTests
    {
        private static SaveDocument ThroughBytes(SaveService service)
        {
            var serializer = new SaveSerializer();
            return SaveDocument.FromJson(serializer.Parse(serializer.ToBytes(service.Capture().ToJson(), true)));
        }

        [Test]
        public void OneSection_RoundTrips()
        {
            var saving = new SaveService();
            saving.Register(new CounterSection("a") { Value = 5, Note = "hi" });
            var document = ThroughBytes(saving);

            var loading = new SaveService();
            var section = new CounterSection("a");
            loading.Register(section);
            var result = loading.Restore(document);

            Assert.That(result.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(section.Value, Is.EqualTo(5));
            Assert.That(section.Note, Is.EqualTo("hi"));
        }

        [Test]
        public void ManySections_RoundTrip()
        {
            var saving = new SaveService();
            for (var i = 0; i < 20; i++)
                saving.Register(new CounterSection($"s{i}") { Value = i * 3 });
            var document = ThroughBytes(saving);

            var loading = new SaveService();
            var sections = Enumerable.Range(0, 20).Select(i => new CounterSection($"s{i}")).ToList();
            sections.ForEach(section => loading.Register(section));
            loading.Restore(document);

            Assert.That(sections.Select(section => section.Value), Is.EqualTo(Enumerable.Range(0, 20).Select(i => i * 3)));
        }

        [Test]
        public void MissingSection_GetsItsDefault()
        {
            var document = ThroughBytes(new SaveService());
            var loading = new SaveService();
            var section = new CounterSection("added-later") { Value = 99, DefaultValue = 10 };
            loading.Register(section);

            var result = loading.Restore(document);
            Assert.That(result.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(section.Value, Is.EqualTo(10));
            Assert.That(result.Sections.Single().Outcome, Is.EqualTo(SectionOutcome.Defaulted));
        }

        [Test]
        public void UnknownSection_IsHeld_AndWrittenBackUnchanged()
        {
            var saving = new SaveService();
            saving.Register(new CounterSection("removed-system") { Value = 8, Note = "ünïcode ✓" });
            saving.Register(new CounterSection("kept") { Value = 1 });
            var original = ThroughBytes(saving);

            var loading = new SaveService();
            loading.Register(new CounterSection("kept"));
            var result = loading.Restore(original);
            var resaved = ThroughBytes(loading);

            Assert.That(result.Sections.Single(report => report.Key == "removed-system").Outcome, Is.EqualTo(SectionOutcome.Held));
            Assert.That(JToken.DeepEquals(resaved.Sections["removed-system"].Data, original.Sections["removed-system"].Data));
            Assert.That(resaved.Sections["removed-system"].Version, Is.EqualTo(1));
        }

        [Test]
        public void HeldSection_IsRestoredWhenItsSystemRegistersLater()
        {
            var saving = new SaveService();
            saving.Register(new CounterSection("world/cave") { Value = 3 });
            var document = ThroughBytes(saving);

            var loading = new SaveService();
            loading.Restore(document);
            var cave = new CounterSection("world/cave");
            var report = loading.Register(cave);

            Assert.That(report.Outcome, Is.EqualTo(SectionOutcome.Restored));
            Assert.That(cave.Value, Is.EqualTo(3));
        }

        [Test]
        public void LargeData_RoundTrips()
        {
            var saving = new SaveService();
            var profile = new ProfileSection();
            profile.Data.Numbers = Enumerable.Range(0, 10000).ToList();
            for (var i = 0; i < 10000; i++)
                profile.Data.Labels[$"key-{i}"] = $"value-{i}";
            saving.Register(profile);
            var document = ThroughBytes(saving);

            var loading = new SaveService();
            var loaded = new ProfileSection();
            loading.Register(loaded);
            loading.Restore(document);

            Assert.That(loaded.Data.Numbers, Is.EqualTo(profile.Data.Numbers));
            Assert.That(loaded.Data.Labels, Has.Count.EqualTo(10000));
            Assert.That(loaded.Data.Labels["key-9999"], Is.EqualTo("value-9999"));
        }

        [Test]
        public void NullsEmptiesAndUnicode_RoundTrip()
        {
            var saving = new SaveService();
            var profile = new ProfileSection();
            profile.Data.Name = "Ärger \"quoted\" \\ back\nline \u0000 nul 🗡";
            profile.Data.Numbers = new List<int>();
            profile.Data.Labels = new Dictionary<string, string> { ["ключ"] = "значение", ["emoji 🗝"] = null, [""] = "empty key" };
            profile.Data.Children = null;
            saving.Register(profile);
            var document = ThroughBytes(saving);

            var loading = new SaveService();
            var loaded = new ProfileSection();
            loading.Register(loaded);
            loading.Restore(document);

            Assert.That(loaded.Data.Name, Is.EqualTo(profile.Data.Name));
            Assert.That(loaded.Data.Numbers, Is.Empty);
            Assert.That(loaded.Data.Labels, Is.EquivalentTo(profile.Data.Labels));
            Assert.That(loaded.Data.Children, Is.Null);
            Assert.That(loaded.Data.Missing, Is.Null);
        }

        [Test]
        public void CaptureReturningNull_RestoresDefault()
        {
            var saving = new SaveService();
            saving.Register(new ProfileSection { Data = null });
            var document = ThroughBytes(saving);

            var loading = new SaveService();
            var loaded = new ProfileSection { Data = null };
            loading.Register(loaded);
            Assert.That(loading.Restore(document).Succeeded);
            Assert.That(loaded.Data, Is.Not.Null);
        }

        [Test]
        public void CaptureThrowing_ThrowsWithTheKey()
        {
            var service = new SaveService();
            service.Register(new CounterSection("fine"));
            service.Register(new CounterSection("broken") { ThrowOnCapture = true });
            var error = Assert.Throws<SectionCaptureException>(() => service.Capture());
            Assert.That(error.Key, Is.EqualTo("broken"));
        }

        [Test]
        public void UnreadableSectionData_RestoresNothing()
        {
            var saving = new SaveService();
            saving.Register(new CounterSection("a") { Value = 1 });
            saving.Register(new CounterSection("b") { Value = 2 });
            var document = ThroughBytes(saving);
            document.Sections["b"] = new SectionRecord(1, new JObject { ["Value"] = "not a number" });

            var loading = new SaveService();
            var a = new CounterSection("a") { Value = 100 };
            var b = new CounterSection("b") { Value = 200 };
            loading.Register(a);
            loading.Register(b);
            var result = loading.Restore(document);

            Assert.That(result.Status, Is.EqualTo(LoadStatus.SectionFailed));
            Assert.That(result.Problem, Does.Contain("b"));
            Assert.That(a.Value, Is.EqualTo(100));
            Assert.That(b.Value, Is.EqualTo(200));
        }

        [Test]
        public void FailedLoad_KeepsPreviouslyHeldData()
        {
            var service = new SaveService();
            var area = new CounterSection("area") { Value = 6 };
            service.Register(area);
            service.Unregister(area);
            service.Register(new CounterSection("b"));

            var bad = new SaveDocument();
            bad.Sections["b"] = new SectionRecord(1, new JObject { ["Value"] = "x" });
            service.Restore(bad);

            Assert.That(service.HeldKeys, Does.Contain("area"));
        }

        [Test]
        public void RestoreThrowing_ReportsIt_AndPutsEverySectionBack()
        {
            var saving = new SaveService();
            saving.Register(new CounterSection("a") { Value = 1 });
            saving.Register(new CounterSection("b") { Value = 2 });
            var document = ThroughBytes(saving);

            var log = new RecordingLog();
            var loading = new SaveService(log: log);
            var a = new CounterSection("a") { Value = 8, ThrowOnRestoreOf = 1 };
            var b = new CounterSection("b") { Value = 9 };
            loading.Register(a);
            loading.Register(b);
            var result = loading.Restore(document);

            Assert.That((result.Status, result.RolledBack, result.LeftStateUnchanged), Is.EqualTo((LoadStatus.RestoreFailed, true, true)));
            Assert.That(result.Sections.Single(report => report.Key == "a").Outcome, Is.EqualTo(SectionOutcome.Failed));
            Assert.That((a.Value, b.Value), Is.EqualTo((8, 9)), "b was restored, then put back");
            Assert.That(log.Errors, Has.Count.EqualTo(1));
            Assert.That(loading.PinnedKeys, Is.Empty);
        }

        [Test]
        public void ASectionThatCantBePutBack_IsSavedWithItsLastGoodData_UntilALoadRestoresIt()
        {
            var saving = new SaveService();
            saving.Register(new CounterSection("a") { Value = 1 });
            saving.Register(new CounterSection("b") { Value = 2 });
            var document = ThroughBytes(saving);

            var log = new RecordingLog();
            var loading = new SaveService(log: log);
            var a = new CounterSection("a") { Value = 8, ThrowOnRestore = true };
            var b = new CounterSection("b") { Value = 9 };
            loading.Register(a);
            loading.Register(b);
            var result = loading.Restore(document);

            Assert.That((result.Status, result.RolledBack, result.LeftStateUnchanged), Is.EqualTo((LoadStatus.RestoreFailed, false, false)));
            Assert.That(loading.PinnedKeys, Is.EqualTo(new[] { "a" }));
            Assert.That(b.Value, Is.EqualTo(9), "everything that could be put back was");
            a.Value = 77;
            Assert.That(loading.Capture().Sections["a"].Data["Value"].ToObject<int>(), Is.EqualTo(8), "a is saved as it was before the load, never its half-restored state");

            a.ThrowOnRestore = false;
            Assert.That(loading.Restore(document).Succeeded);
            Assert.That((loading.PinnedKeys.Count, a.Value), Is.EqualTo((0, 1)));
        }

        [Test]
        public void NewerFormat_IsRefused()
        {
            var json = new SaveService().Capture().ToJson();
            json["format"] = SaveDocument.CurrentFormat + 1;
            var section = new CounterSection("a") { Value = 4 };
            var service = new SaveService();
            service.Register(section);

            var result = service.Restore(SaveDocument.FromJson(json));
            Assert.That(result.Status, Is.EqualTo(LoadStatus.TooNew));
            Assert.That(section.Value, Is.EqualTo(4));
        }

        [Test]
        public void NewGame_DefaultsEverything_AndForgetsHeldData()
        {
            var service = new SaveService();
            var area = new CounterSection("area") { Value = 6 };
            service.Register(area);
            service.Unregister(area);
            var kept = new CounterSection("kept") { Value = 9, DefaultValue = 1 };
            service.Register(kept);

            var result = service.NewGame();
            Assert.That(result.Succeeded);
            Assert.That(kept.Value, Is.EqualTo(1));
            Assert.That(service.HeldKeys, Is.Empty);
        }

        [Test]
        public void LoadTwice_ReplacesHeldData()
        {
            var first = new SaveService();
            first.Register(new CounterSection("old-area") { Value = 1 });
            var second = new SaveService();
            second.Register(new CounterSection("new-area") { Value = 2 });

            var service = new SaveService();
            service.Restore(ThroughBytes(first));
            service.Restore(ThroughBytes(second));
            Assert.That(service.HeldKeys, Is.EquivalentTo(new[] { "new-area" }));
        }

        [Test]
        public void Prepare_ChangesNothing_UntilApplied()
        {
            var saving = new SaveService();
            saving.Register(new CounterSection("a") { Value = 5 });
            var document = ThroughBytes(saving);

            var loading = new SaveService();
            var section = new CounterSection("a") { Value = 1 };
            loading.Register(section);
            var load = loading.Prepare(document);

            Assert.That(load.CanApply);
            Assert.That(section.Value, Is.EqualTo(1));
            Assert.That(loading.Apply(load).Succeeded);
            Assert.That(section.Value, Is.EqualTo(5));
            Assert.That(load.CanApply, Is.False);
        }

        [Test]
        public void PreparedLoad_AppliesOnlyOnce()
        {
            var service = new SaveService();
            service.Register(new CounterSection("a"));
            var load = service.Prepare(service.Capture());
            service.Apply(load);
            Assert.Throws<InvalidOperationException>(() => service.Apply(load));
        }

        [Test]
        public void FailedPrepare_CannotBeApplied()
        {
            var service = new SaveService();
            service.Register(new CounterSection("a"));
            var bad = new SaveDocument();
            bad.Sections["a"] = new SectionRecord(1, new JObject { ["Value"] = "x" });

            var load = service.Prepare(bad);
            Assert.That(load.CanApply, Is.False);
            Assert.That(load.Failure.Status, Is.EqualTo(LoadStatus.SectionFailed));
            Assert.Throws<InvalidOperationException>(() => service.Apply(load));
        }

        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("{\"format\":1}")]
        [TestCase("{\"format\":0,\"sections\":{}}")]
        [TestCase("{\"format\":1.5,\"sections\":{}}")]
        [TestCase("{\"format\":\"1\",\"sections\":{}}")]
        [TestCase("{\"format\":1,\"sections\":[]}")]
        [TestCase("{\"format\":1,\"sections\":{\"a\":5}}")]
        [TestCase("{\"format\":1,\"sections\":{\"a\":{\"data\":1}}}")]
        [TestCase("{\"format\":1,\"sections\":{\"a\":{\"version\":-1,\"data\":1}}}")]
        [TestCase("{\"format\":1,\"sections\":{\"a\":{\"version\":99999999999,\"data\":1}}}")]
        [TestCase("{\"format\":1,\"sections\":{\"Bad Key\":{\"version\":1,\"data\":1}}}")]
        [TestCase("{\"format\":1,\"meta\":5,\"sections\":{}}")]
        public void MalformedDocument_IsRejected(string json)
        {
            Assert.Throws<FormatException>(() => SaveDocument.FromJson(JToken.Parse(json)));
        }

        [Test]
        public void Document_WithoutData_ReadsAsNull()
        {
            var document = SaveDocument.FromJson(JToken.Parse("{\"format\":1,\"sections\":{\"a\":{\"version\":1}}}"));
            Assert.That(document.Sections["a"].Data.Type, Is.EqualTo(JTokenType.Null));
        }
    }
}
