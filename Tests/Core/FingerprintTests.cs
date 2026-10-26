using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>A snapshot is the document a save would write, leaving nothing behind; a fingerprint is that document as text, the same for the same state.</summary> */
    public sealed class FingerprintTests
    {
        private static SaveDocument Document(params (string Key, int Version, JToken Data)[] sections)
        {
            var document = new SaveDocument();
            foreach (var (key, version, data) in sections) document.Sections[key] = new SectionRecord(version, data);
            return document;
        }

        [Test]
        public void ASnapshot_IsWhatASaveWouldWrite()
        {
            var service = new SaveService();
            service.Register(new CounterSection("a") { Value = 3, Note = "x" });
            service.Register(new CounterSection("b") { Value = 7 });

            Assert.That(SaveFingerprint.Of(service.Snapshot()), Is.EqualTo(SaveFingerprint.Of(service.Capture())));
        }

        [Test]
        public void ASnapshot_LeavesTheLastSave_AsWhatAFailedUnregisterKeeps()
        {
            var service = new SaveService();
            var section = new CounterSection("counter") { Value = 1 };
            service.Register(section);
            service.Capture();
            section.Value = 2;
            service.Snapshot();

            section.ThrowOnCapture = true;
            service.Unregister(section);

            Assert.That(service.Capture().Sections["counter"].Data["Value"].ToObject<int>(), Is.EqualTo(1), "the save's state, not the snapshot's");
        }

        [Test]
        public void TheSameState_GivesTheSameText_WhateverOrderItsPropertiesCameIn()
        {
            var first = Document(("party", 1, new JObject { ["b"] = 2, ["a"] = new JObject { ["y"] = 1, ["x"] = 0 } }), ("bag", 1, new JArray(1, 2)));
            var second = Document(("bag", 1, new JArray(1, 2)), ("party", 1, new JObject { ["a"] = new JObject { ["x"] = 0, ["y"] = 1 }, ["b"] = 2 }));

            Assert.That(SaveFingerprint.Of(first), Is.EqualTo(SaveFingerprint.Of(second)));
            Assert.That(SaveFingerprint.Of(first).IndexOf("== bag v1"), Is.LessThan(SaveFingerprint.Of(first).IndexOf("== party v1")), "sections in key order");
        }

        [Test]
        public void AFingerprint_LeavesTheRecordsAsTheyWere()
        {
            var data = new JObject { ["b"] = 2, ["a"] = 1 };

            SaveFingerprint.Of(Document(("s", 1, data)));

            Assert.That(((JProperty)data.First).Name, Is.EqualTo("b"));
        }

        [Test]
        public void TheKeysLeftOut_AreNotInIt()
        {
            var text = SaveFingerprint.Of(Document(("play-time", 1, new JObject { ["Seconds"] = 12.5 }), ("bag", 1, new JArray())), "play-time");

            Assert.That(text, Does.Not.Contain("play-time"));
            Assert.That(text, Does.Contain("== bag v1"));
        }

        [Test]
        public void RoundedToSomePlaces_ANudgeBelowThemIsTheSame_AndAChangeAboveThemIsNot()
        {
            var still = SaveFingerprint.Of(Document(("place", 1, new JObject { ["x"] = 0.3966125, ["count"] = 3 })), 4);

            Assert.That(SaveFingerprint.Of(Document(("place", 1, new JObject { ["x"] = 0.396612555, ["count"] = 3 })), 4), Is.EqualTo(still));
            Assert.That(SaveFingerprint.Of(Document(("place", 1, new JObject { ["x"] = 0.3971, ["count"] = 3 })), 4), Is.Not.EqualTo(still));
            Assert.That(still, Does.Contain("0.3966").And.Contain("\"count\": 3"), "whole numbers as they are");
        }

        [Test]
        public void Differences_NameEachSectionThatChanged_OrIsInOnlyOne()
        {
            var before = Document(("same", 1, new JObject { ["v"] = 1 }), ("changed", 1, new JObject { ["v"] = 1 }), ("versioned", 1, new JObject()), ("gone", 1, new JObject()), ("clock", 1, new JObject { ["s"] = 1 }));
            var after = Document(("same", 1, new JObject { ["v"] = 1 }), ("changed", 1, new JObject { ["v"] = 2 }), ("versioned", 2, new JObject()), ("new", 1, new JObject()), ("clock", 1, new JObject { ["s"] = 9 }));

            Assert.That(SaveFingerprint.Differences(before, after, "clock"), Is.EqualTo(new[] { "changed", "gone", "new", "versioned" }));
            Assert.That(SaveFingerprint.Differences(before, before), Is.Empty);
        }
    }
}
