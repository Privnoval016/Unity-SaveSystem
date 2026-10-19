using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>Per-section upgrades: order, missing and failing steps, newer data, purity.</summary> */
    public sealed class UpgradeTests
    {
        private static SaveDocument DocumentWith(string key, int version, JToken data)
        {
            var document = new SaveDocument();
            document.Sections[key] = new SectionRecord(version, data);
            return document;
        }

        [Test]
        public void Steps_RunInOrder()
        {
            var steps = new List<int>();
            var section = new CounterSection("a", 3, 0,
                new SectionUpgrade(2, data =>
                {
                    steps.Add(2);
                    data["Value"] = data["Value"].ToObject<int>() * 10;
                    return data;
                }),
                new SectionUpgrade(1, data =>
                {
                    steps.Add(1);
                    return new JObject { ["Value"] = data["Old"] };
                }));
            var service = new SaveService();
            service.Register(section);

            var result = service.Restore(DocumentWith("a", 1, new JObject { ["Old"] = 4 }));

            Assert.That(result.Succeeded);
            Assert.That(steps, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(section.Value, Is.EqualTo(40));
            Assert.That(result.Sections[0].Outcome, Is.EqualTo(SectionOutcome.Upgraded));
            Assert.That(result.Sections[0].StoredVersion, Is.EqualTo(1));
        }

        [Test]
        public void MissingStep_FailsAndRestoresNothing()
        {
            var a = new CounterSection("a") { Value = 1 };
            var b = new CounterSection("b", 3, 0, new SectionUpgrade(2, data => data)) { Value = 2 };
            var service = new SaveService();
            service.Register(a);
            service.Register(b);
            var document = DocumentWith("b", 1, new JObject { ["Value"] = 9 });
            document.Sections["a"] = new SectionRecord(1, new JObject { ["Value"] = 50 });

            var result = service.Restore(document);

            Assert.That(result.Status, Is.EqualTo(LoadStatus.SectionFailed));
            Assert.That(result.Problem, Does.Contain("no upgrade from version 1"));
            Assert.That(a.Value, Is.EqualTo(1));
            Assert.That(b.Value, Is.EqualTo(2));
        }

        [Test]
        public void ThrowingStep_FailsAndRestoresNothing()
        {
            var a = new CounterSection("a") { Value = 1 };
            var b = new CounterSection("b", 2, 0, new SectionUpgrade(1, data => throw new InvalidOperationException("bad step")));
            var service = new SaveService();
            service.Register(a);
            service.Register(b);
            var document = DocumentWith("b", 1, new JObject());
            document.Sections["a"] = new SectionRecord(1, new JObject { ["Value"] = 50 });

            var result = service.Restore(document);

            Assert.That(result.Status, Is.EqualTo(LoadStatus.SectionFailed));
            Assert.That(result.Problem, Does.Contain("bad step"));
            Assert.That(a.Value, Is.EqualTo(1));
        }

        [Test]
        public void StepReturningNothing_Fails()
        {
            var section = new CounterSection("a", 2, 0, new SectionUpgrade(1, data => null));
            var service = new SaveService();
            service.Register(section);
            Assert.That(service.Restore(DocumentWith("a", 1, new JObject())).Status, Is.EqualTo(LoadStatus.SectionFailed));
        }

        [Test]
        public void NewerData_IsRefused_AndRestoresNothing()
        {
            var a = new CounterSection("a") { Value = 1 };
            var service = new SaveService();
            service.Register(a);
            service.Register(new CounterSection("b", 2, 0, new SectionUpgrade(1, data => data)));
            var document = DocumentWith("b", 3, new JObject());
            document.Sections["a"] = new SectionRecord(1, new JObject { ["Value"] = 50 });

            var result = service.Restore(document);

            Assert.That(result.Status, Is.EqualTo(LoadStatus.TooNew));
            Assert.That(a.Value, Is.EqualTo(1));
        }

        [Test]
        public void Step_NeverChangesItsInput()
        {
            var original = new JObject { ["Value"] = 1, ["List"] = new JArray(1, 2) };
            var before = original.DeepClone();
            var upgrade = new SectionUpgrade(1, data =>
            {
                data["Value"] = 2;
                ((JArray)data["List"]).Add(3);
                return data;
            });

            var upgraded = upgrade.Apply(original);

            Assert.That(JToken.DeepEquals(original, before));
            Assert.That(upgraded["Value"].ToObject<int>(), Is.EqualTo(2));
        }

        [Test]
        public void Step_ReceivingNull_GetsAJsonNull()
        {
            JToken seen = null;
            new SectionUpgrade(1, data => seen = data).Apply(null);
            Assert.That(seen.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void HeldData_IsUpgradedWhenItsSystemRegisters()
        {
            var service = new SaveService();
            service.Restore(DocumentWith("area", 1, new JObject { ["Old"] = 7 }));
            var area = new CounterSection("area", 2, 0, new SectionUpgrade(1, data => new JObject { ["Value"] = data["Old"] }));

            var report = service.Register(area);

            Assert.That(report.Outcome, Is.EqualTo(SectionOutcome.Upgraded));
            Assert.That(area.Value, Is.EqualTo(7));
        }

        [Test]
        public void UpgradedSection_IsSavedAtTheNewVersion()
        {
            var section = new CounterSection("a", 2, 0, new SectionUpgrade(1, data => new JObject { ["Value"] = 5 }));
            var service = new SaveService();
            service.Register(section);
            service.Restore(DocumentWith("a", 1, new JObject()));

            Assert.That(service.Capture().Sections["a"].Version, Is.EqualTo(2));
        }
    }
}
