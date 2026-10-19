using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    public sealed class SectionRegistrationTests
    {
        [Test]
        public void DuplicateKey_Throws()
        {
            var service = new SaveService();
            service.Register(new CounterSection("party"));
            Assert.Throws<InvalidOperationException>(() => service.Register(new CounterSection("party")));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Party")]
        [TestCase("has space")]
        [TestCase("a//b")]
        [TestCase("/a")]
        [TestCase("a/")]
        [TestCase("tab\t")]
        [TestCase("ünïcode")]
        public void InvalidKey_Throws(string key)
        {
            Assert.Throws<ArgumentException>(() => new SaveService().Register(new CounterSection(key)));
        }

        [Test]
        public void KeyLongerThanLimit_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SaveService().Register(new CounterSection(new string('a', SaveNames.MaxLength + 1))));
        }

        [TestCase("world/scene-1.a_b")]
        [TestCase("a")]
        [TestCase("0")]
        public void ValidKey_Registers(string key)
        {
            var service = new SaveService();
            service.Register(new CounterSection(key));
            Assert.That(service.IsRegistered(key));
        }

        [Test]
        public void KeyAtLimit_Registers()
        {
            var key = new string('a', SaveNames.MaxLength);
            var service = new SaveService();
            service.Register(new CounterSection(key));
            Assert.That(service.IsRegistered(key));
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void VersionBelowOne_Throws(int version)
        {
            Assert.Throws<ArgumentException>(() => new SaveService().Register(new CounterSection("a", version)));
        }

        [Test]
        public void UpgradeNotBelowVersion_Throws()
        {
            var section = new CounterSection("a", 2, 0, new SectionUpgrade(2, data => data));
            Assert.Throws<ArgumentException>(() => new SaveService().Register(section));
        }

        [Test]
        public void TwoUpgradesFromOneVersion_Throws()
        {
            var section = new CounterSection("a", 3, 0, new SectionUpgrade(1, data => data), new SectionUpgrade(1, data => data));
            Assert.Throws<ArgumentException>(() => new SaveService().Register(section));
        }

        [Test]
        public void NullUpgrade_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SaveService().Register(new CounterSection("a", 2, 0, (SectionUpgrade)null)));
        }

        [Test]
        public void UpgradeFromVersionZero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SectionUpgrade(0, data => data));
        }

        [Test]
        public void NullSection_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SaveService().Register(null));
        }

        [Test]
        public void Register_WithNothingHeld_LeavesStateUnchanged()
        {
            var section = new CounterSection("a") { Value = 7 };
            var report = new SaveService().Register(section);
            Assert.That(report.Outcome, Is.EqualTo(SectionOutcome.Unchanged));
            Assert.That(section.Value, Is.EqualTo(7));
            Assert.That(section.RestoreCount, Is.EqualTo(0));
        }

        [Test]
        public void Unregister_HoldsState_AndRegisterAgainRestoresIt()
        {
            var service = new SaveService();
            var first = new CounterSection("area") { Value = 12 };
            service.Register(first);
            service.Unregister(first);

            Assert.That(service.IsRegistered("area"), Is.False);
            Assert.That(service.HeldKeys, Does.Contain("area"));

            var second = new CounterSection("area");
            var report = service.Register(second);
            Assert.That(report.Outcome, Is.EqualTo(SectionOutcome.Restored));
            Assert.That(second.Value, Is.EqualTo(12));
            Assert.That(service.HeldKeys, Does.Not.Contain("area"));
        }

        [Test]
        public void UnregisteredState_IsSaved()
        {
            var service = new SaveService();
            var area = new CounterSection("area") { Value = 4 };
            service.Register(area);
            service.Unregister(area);

            var document = service.Capture();
            Assert.That(document.Sections["area"].Data["Value"].ToObject<int>(), Is.EqualTo(4));
        }

        [Test]
        public void Unregister_Unknown_DoesNothing()
        {
            var service = new SaveService();
            service.Unregister(new CounterSection("a"));
            service.Unregister(null);
            Assert.That(service.HeldKeys, Is.Empty);
        }

        [Test]
        public void Unregister_WhenCaptureThrows_LogsAndRemoves()
        {
            var log = new RecordingLog();
            var service = new SaveService(log: log);
            var section = new CounterSection("a") { ThrowOnCapture = true };
            service.Register(section);
            service.Unregister(section);
            Assert.That(service.IsRegistered("a"), Is.False);
            Assert.That(log.Errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void Register_WhenHeldDataFails_IsNotRegistered_AndDataStaysHeld()
        {
            var log = new RecordingLog();
            var service = new SaveService(log: log);
            var old = new CounterSection("area") { Value = 3 };
            service.Register(old);
            service.Unregister(old);

            var newer = new CounterSection("area", 2);
            var report = service.Register(newer);

            Assert.That(report.Outcome, Is.EqualTo(SectionOutcome.Failed));
            Assert.That(report.Problem, Does.Contain("no upgrade"));
            Assert.That(service.IsRegistered("area"), Is.False);
            Assert.That(service.HeldKeys, Does.Contain("area"));
            Assert.That(log.Errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void RestoreOrder_IsAscending_ThenRegistrationOrder()
        {
            var log = new List<string>();
            var service = new SaveService();
            foreach (var section in new[]
                     {
                         new CounterSection("late", order: 5), new CounterSection("first-zero"), new CounterSection("early", order: -1),
                         new CounterSection("second-zero"),
                     })
            {
                section.RestoreLog = log;
                service.Register(section);
            }

            service.Restore(service.Capture());
            Assert.That(log, Is.EqualTo(new[] { "early", "first-zero", "second-zero", "late" }));
        }
    }

    /** <summary>A log that keeps what it was told.</summary> */
    public sealed class RecordingLog : ISaveLog
    {
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public void Warning(string message) => Warnings.Add(message);

        public void Error(string message) => Errors.Add(message);
    }
}
