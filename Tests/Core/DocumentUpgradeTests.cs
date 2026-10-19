using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>Whole-save upgrades: renaming, splitting and retiring sections, once per save.</summary> */
    public sealed class DocumentUpgradeTests
    {
        private static DocumentUpgrade Rename(string id, string from, string to) => new DocumentUpgrade(id, document =>
        {
            if (!document.Sections.TryGetValue(from, out var record)) return;
            document.Sections.Remove(from);
            document.Sections[to] = record;
        });

        private static SaveDocument OldSave()
        {
            var document = new SaveDocument();
            document.Sections["party"] = new SectionRecord(1, new JObject { ["Value"] = 3, ["Note"] = "flags" });
            return document;
        }

        [Test]
        public void Rename_MovesTheDataToTheNewKey()
        {
            var service = new SaveService();
            service.AddDocumentUpgrade(Rename("party-to-roster", "party", "roster"));
            var roster = new CounterSection("roster");
            service.Register(roster);

            Assert.That(service.Restore(OldSave()).Succeeded);
            Assert.That(roster.Value, Is.EqualTo(3));
            Assert.That(service.HeldKeys, Does.Not.Contain("party"));
        }

        [Test]
        public void Split_FeedsTwoSections()
        {
            var service = new SaveService();
            service.AddDocumentUpgrade(new DocumentUpgrade("split-party", document =>
            {
                var data = document.Sections["party"].Data;
                document.Sections.Remove("party");
                document.Sections["party"] = new SectionRecord(1, new JObject { ["Value"] = data["Value"] });
                document.Sections["story"] = new SectionRecord(1, new JObject { ["Note"] = data["Note"] });
            }));
            var party = new CounterSection("party");
            var story = new CounterSection("story");
            service.Register(party);
            service.Register(story);

            service.Restore(OldSave());
            Assert.That(party.Value, Is.EqualTo(3));
            Assert.That(party.Note, Is.Null);
            Assert.That(story.Note, Is.EqualTo("flags"));
        }

        [Test]
        public void Retire_DropsHeldData()
        {
            var service = new SaveService();
            service.AddDocumentUpgrade(new DocumentUpgrade("retire-party", document => document.Sections.Remove("party")));
            service.Restore(OldSave());
            Assert.That(service.HeldKeys, Is.Empty);
            Assert.That(service.Capture().Sections.ContainsKey("party"), Is.False);
        }

        [Test]
        public void Upgrades_RunOncePerSave()
        {
            var runs = 0;
            DocumentUpgrade Counting() => new DocumentUpgrade("count", _ => runs++);

            var first = new SaveService();
            first.AddDocumentUpgrade(Counting());
            first.Restore(OldSave());
            var saved = first.Capture();
            Assert.That(saved.AppliedUpgrades, Does.Contain("count"));

            var second = new SaveService();
            second.AddDocumentUpgrade(Counting());
            second.Restore(SaveDocument.FromJson(saved.ToJson()));
            Assert.That(runs, Is.EqualTo(1));
        }

        [Test]
        public void NewSaves_RecordEveryUpgrade_SoNoneRunOnThem()
        {
            var ran = false;
            var service = new SaveService();
            service.AddDocumentUpgrade(new DocumentUpgrade("never-needed", _ => ran = true));
            service.NewGame();
            var saved = service.Capture();

            var loading = new SaveService();
            loading.AddDocumentUpgrade(new DocumentUpgrade("never-needed", _ => ran = true));
            loading.Restore(saved);
            Assert.That(ran, Is.False);
        }

        [Test]
        public void RetiredUpgradeIds_AreKept()
        {
            var document = OldSave();
            document.AppliedUpgrades.Add("from-an-old-build");
            var service = new SaveService();
            service.Restore(document);
            Assert.That(service.Capture().AppliedUpgrades, Does.Contain("from-an-old-build"));
        }

        [Test]
        public void ThrowingUpgrade_RestoresNothing_AndLeavesTheDocumentAlone()
        {
            var service = new SaveService();
            service.AddDocumentUpgrade(new DocumentUpgrade("move", document => document.Sections.Remove("party")));
            service.AddDocumentUpgrade(new DocumentUpgrade("broken", _ => throw new InvalidOperationException("bad")));
            var party = new CounterSection("party") { Value = 50 };
            service.Register(party);
            var document = OldSave();

            var result = service.Restore(document);

            Assert.That(result.Status, Is.EqualTo(LoadStatus.SectionFailed));
            Assert.That(result.Problem, Does.Contain("broken"));
            Assert.That(party.Value, Is.EqualTo(50));
            Assert.That(document.Sections.ContainsKey("party"));
            Assert.That(document.AppliedUpgrades, Is.Empty);
        }

        [Test]
        public void Upgrades_RunInTheOrderAdded()
        {
            var order = new List<string>();
            var service = new SaveService();
            service.AddDocumentUpgrade(new DocumentUpgrade("b-first", _ => order.Add("b-first")));
            service.AddDocumentUpgrade(new DocumentUpgrade("a-second", _ => order.Add("a-second")));
            service.Restore(OldSave());
            Assert.That(order, Is.EqualTo(new[] { "b-first", "a-second" }));
        }

        [Test]
        public void DuplicateOrInvalidIds_Throw()
        {
            var service = new SaveService();
            service.AddDocumentUpgrade(new DocumentUpgrade("one", _ => { }));
            Assert.Throws<InvalidOperationException>(() => service.AddDocumentUpgrade(new DocumentUpgrade("one", _ => { })));
            Assert.Throws<ArgumentException>(() => new DocumentUpgrade("Has Space", _ => { }));
            Assert.Throws<ArgumentNullException>(() => new DocumentUpgrade("ok", null));
        }

        [TestCase("{\"format\":1,\"upgrades\":5,\"sections\":{}}")]
        [TestCase("{\"format\":1,\"upgrades\":[1],\"sections\":{}}")]
        public void MalformedUpgradeList_IsRejected(string json)
        {
            Assert.Throws<FormatException>(() => SaveDocument.FromJson(JToken.Parse(json)));
        }

        [Test]
        public void UpgradeList_RoundTrips_Sorted()
        {
            var document = new SaveDocument();
            document.AppliedUpgrades.Add("b");
            document.AppliedUpgrades.Add("a");
            var json = document.ToJson();
            Assert.That(json["upgrades"].Values<string>(), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(SaveDocument.FromJson(json).AppliedUpgrades, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(new SaveDocument().ToJson()["upgrades"], Is.Null);
        }
    }
}
