using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /**
     * <summary>Restores that never leave a half-loaded game: settling runs once every section is back (after a load, a
     * new game and a registration with held data) and sees the others restored; a throw while settling, or a new game's
     * section throwing, puts everything back, held data and upgrades included; with no snapshot to go back to, the
     * failed sections are saved with the save's data rather than their half-restored state; a section that fails to
     * capture as it unregisters keeps its last saved state.</summary>
     */
    public sealed class RollbackTests
    {
        /** <summary>A counter whose settle copies another section's value, recording when it ran.</summary> */
        private sealed class FollowerSection : SaveSection<CounterData>, ISettlesAfterRestore
        {
            private readonly CounterSection _leader;

            public int Value { get; set; }
            public int Followed { get; private set; } = -1;
            public int? ThrowWhenLeaderIs { get; set; }
            public List<string> Order { get; set; }

            public FollowerSection(CounterSection leader) => _leader = leader;

            public override string Key => "follower";

            public void Settle()
            {
                if (_leader.Value == ThrowWhenLeaderIs) throw new InvalidOperationException("settle broke");
                Followed = _leader.Value;
                Order?.Add("settle");
            }

            protected override CounterData Capture() => new CounterData { Value = Value };

            protected override void Restore(CounterData data)
            {
                Value = data.Value;
                Order?.Add("restore follower");
            }
        }

        private static SaveDocument Saved(params (string Key, int Value)[] values)
        {
            var service = new SaveService();
            foreach (var (key, value) in values) service.Register(new CounterSection(key) { Value = value });
            return service.Capture();
        }

        [Test]
        public void Settle_RunsOnceEverySectionIsBack_AfterALoadANewGameAndARegistration()
        {
            var order = new List<string>();
            var service = new SaveService();
            var leader = new CounterSection("leader") { RestoreLog = order };
            var follower = new FollowerSection(leader) { Order = order };
            service.Register(follower);
            service.Register(leader);

            Assert.That(service.Restore(Saved(("follower", 2), ("leader", 9))).Succeeded);
            Assert.That(order, Is.EqualTo(new[] { "restore follower", "leader", "settle" }), "the follower is registered first, yet settles after the leader is back");
            Assert.That(follower.Followed, Is.EqualTo(9));

            leader.DefaultValue = 4;
            Assert.That(service.NewGame().Succeeded);
            Assert.That(follower.Followed, Is.EqualTo(4));

            var late = new SaveService();
            var lateLeader = new CounterSection("leader");
            var lateFollower = new FollowerSection(lateLeader);
            late.Register(lateFollower);
            Assert.That(late.Restore(Saved(("leader", 6))).Succeeded);
            late.Register(lateLeader);
            Assert.That(lateFollower.Followed, Is.EqualTo(6), "the held leader's data settles its follower as it registers");
        }

        [Test]
        public void AThrowWhileSettling_PutsEverythingBack_HeldDataAndUpgradesIncluded()
        {
            var service = new SaveService();
            var leader = new CounterSection("leader") { Value = 1 };
            var follower = new FollowerSection(leader) { Value = 2 };
            service.Register(leader);
            service.Register(follower);
            service.AddDocumentUpgrade(new DocumentUpgrade("noop", _ => { }));
            var earlier = Saved(("leader", 1), ("retired", 3));
            Assert.That(service.Restore(earlier).Succeeded);
            follower.Value = 2;
            var heldBefore = service.HeldKeys.ToList();
            var upgradesBefore = service.Capture().AppliedUpgrades.ToList();

            follower.ThrowWhenLeaderIs = 50;
            var failing = Saved(("leader", 50), ("follower", 60), ("other", 4));
            failing.AppliedUpgrades.Add("from-the-failed-save");
            var result = service.Restore(failing);
            Assert.That((result.Status, result.RolledBack, result.NeedsReset), Is.EqualTo((LoadStatus.RestoreFailed, true, false)));
            Assert.That((leader.Value, follower.Value), Is.EqualTo((1, 2)));
            Assert.That(service.HeldKeys, Is.EquivalentTo(heldBefore));
            Assert.That(service.Capture().AppliedUpgrades, Is.EquivalentTo(upgradesBefore), "the failed save's upgrade ids went with it");
        }

        [Test]
        public void ANewGameWhoseSectionThrows_PutsEverythingBack()
        {
            var service = new SaveService();
            var a = new CounterSection("a") { Value = 5 };
            var b = new CounterSection("b") { Value = 6, DefaultValue = 99, ThrowOnRestoreOf = 99 };
            service.Register(a);
            service.Register(b);
            var result = service.NewGame();
            Assert.That((result.Status, result.RolledBack, a.Value, b.Value), Is.EqualTo((LoadStatus.RestoreFailed, true, 5, 6)));
        }

        [Test]
        public void WithNoSnapshot_TheFailedSectionsAreSavedWithTheSavesData()
        {
            var log = new RecordingLog();
            var service = new SaveService(log: log);
            var a = new CounterSection("a") { Value = 5, ThrowOnRestore = true };
            var b = new CounterSection("b") { Value = 6, ThrowOnCapture = true };
            service.Register(a);
            service.Register(b);
            var saved = Saved(("a", 1), ("b", 2));

            var result = service.Restore(saved);
            Assert.That((result.Status, result.RolledBack, result.LeftStateUnchanged, result.NeedsReset), Is.EqualTo((LoadStatus.RestoreFailed, false, false, true)));
            Assert.That(service.PinnedKeys, Is.EqualTo(new[] { "a" }));
            b.ThrowOnCapture = false;
            Assert.That(service.Capture().Sections["a"].Data["Value"].ToObject<int>(), Is.EqualTo(1));
            Assert.That(log.Warnings.Concat(log.Errors), Has.Some.Contains("could not be captured"));
        }

        [Test]
        public void ASectionThatFailsToCaptureAsItUnregisters_KeepsItsLastSavedState()
        {
            var log = new RecordingLog();
            var service = new SaveService(log: log);
            var area = new CounterSection("area");
            service.Register(area);
            service.Restore(Saved(("area", 3)));
            area.Value = 4;
            service.Capture();
            area.ThrowOnCapture = true;
            service.Unregister(area);

            Assert.That(service.HeldKeys, Is.EqualTo(new[] { "area" }));
            Assert.That(service.Capture().Sections["area"].Data["Value"].ToObject<int>(), Is.EqualTo(4), "the last capture, not lost");
            Assert.That(log.Errors.Single(), Does.Contain("last saved state is kept"));

            var fresh = new SaveService(log: log);
            var never = new CounterSection("never") { ThrowOnCapture = true };
            fresh.Register(never);
            fresh.Unregister(never);
            Assert.That(fresh.HeldKeys, Is.Empty, "nothing saved before, so nothing to keep");
        }

        /** <summary>A counter that settles by recording its key, and can throw.</summary> */
        private sealed class Settler : SaveSection<CounterData>, ISettlesAfterRestore
        {
            private readonly string _key;
            private readonly List<string> _order;

            public bool Throw { get; set; }

            public Settler(string key, List<string> order)
            {
                _key = key;
                _order = order;
            }

            public override string Key => _key;

            public void Settle()
            {
                _order.Add(_key);
                if (Throw) throw new InvalidOperationException("settle broke");
            }

            protected override CounterData Capture() => new CounterData();

            protected override void Restore(CounterData data) { }
        }

        [Test]
        public void EverySettler_SettlesInRegistrationOrder_AndOneThrowing_StillLetsTheOthersSettle_ThenRollsBack()
        {
            var order = new List<string>();
            var service = new SaveService();
            var counter = new CounterSection("counter") { Value = 1 };
            service.Register(new Settler("first", order));
            service.Register(counter);
            var second = new Settler("second", order) { Throw = true };
            service.Register(second);
            service.Register(new Settler("third", order));

            var result = service.Restore(Saved(("counter", 5)));
            Assert.That(order.Take(3), Is.EqualTo(new[] { "first", "second", "third" }));
            Assert.That((result.RolledBack, counter.Value), Is.EqualTo((false, 1)), "putting it back settles again, and second throws again");
            second.Throw = false;
            order.Clear();
            Assert.That(service.Restore(Saved(("counter", 5))).Succeeded);
            Assert.That(order, Is.EqualTo(new[] { "first", "second", "third" }));
            Assert.That(counter.Value, Is.EqualTo(5));
        }

        [Test]
        public void ASettleThrowAsASectionRegistersWithHeldData_LeavesItUnregistered_WithItsDataHeld()
        {
            var order = new List<string>();
            var log = new RecordingLog();
            var service = new SaveService(log: log);
            var settler = new Settler("settler", order) { Throw = true };
            service.Register(settler);
            Assert.That(service.Restore(Saved(("area", 4))).Status, Is.EqualTo(LoadStatus.RestoreFailed));
            settler.Throw = false;
            Assert.That(service.Restore(Saved(("area", 4))).Succeeded);
            settler.Throw = true;

            var area = new CounterSection("area");
            var report = service.Register(area);
            Assert.That(report.Outcome, Is.EqualTo(SectionOutcome.Failed));
            Assert.That((service.IsRegistered("area"), service.HeldKeys.Contains("area")), Is.EqualTo((false, true)));
            Assert.That(service.Capture().Sections["area"].Data["Value"].ToObject<int>(), Is.EqualTo(4), "its saved data is kept, not the state that restored before the settle threw");
            Assert.That(log.Errors, Has.Some.Contains("not registered"));
        }

        [Test]
        public void APinnedSectionThatUnregisters_HoldsItsPinnedData_AndANewGameClearsPins()
        {
            var service = new SaveService();
            var a = new CounterSection("a") { Value = 5, ThrowOnRestore = true };
            var b = new CounterSection("b") { ThrowOnCapture = true };
            service.Register(a);
            service.Register(b);
            service.Restore(Saved(("a", 1), ("b", 2)));
            Assert.That(service.PinnedKeys, Is.EqualTo(new[] { "a" }));

            service.Unregister(a);
            b.ThrowOnCapture = false;
            Assert.That((service.PinnedKeys.Count, service.Capture().Sections["a"].Data["Value"].ToObject<int>()), Is.EqualTo((0, 1)));
            a.ThrowOnRestore = false;
            service.Register(a);
            Assert.That(a.Value, Is.EqualTo(1), "re-registering restores the pinned data");

            a.ThrowOnRestore = true;
            b.ThrowOnCapture = true;
            service.Restore(Saved(("a", 3), ("b", 2)));
            Assert.That(service.PinnedKeys, Is.Not.Empty);
            a.ThrowOnRestore = false;
            b.ThrowOnCapture = false;
            Assert.That(service.NewGame().Succeeded);
            Assert.That(service.PinnedKeys, Is.Empty);
        }

        [Test]
        public void WithNoSnapshot_ASectionTheLoadRestored_IsNoLongerPinned_AndANewGameKeepsHeldData()
        {
            var service = new SaveService();
            var a = new CounterSection("a") { Value = 5, ThrowOnRestore = true };
            var b = new CounterSection("b") { ThrowOnCapture = true };
            service.Register(a);
            service.Register(b);
            service.Restore(Saved(("a", 1), ("b", 2), ("held", 9)));
            Assert.That(service.PinnedKeys, Is.EqualTo(new[] { "a" }));

            a.ThrowOnRestore = false;
            var c = new CounterSection("c") { ThrowOnRestoreOf = 3 };
            service.Register(c);
            service.Restore(Saved(("a", 7), ("b", 2), ("c", 3), ("held", 9)));
            Assert.That(service.PinnedKeys, Is.EqualTo(new[] { "c" }), "a restored this time, so it is trusted again; c is pinned");

            c.ThrowOnRestoreOf = 0;
            c.DefaultValue = 0;
            var result = service.NewGame();
            Assert.That(result.NeedsReset, Is.True);
            b.ThrowOnCapture = false;
            Assert.That(service.Capture().Sections.ContainsKey("held"), Is.True, "held data survives a new game that couldn't be put back");
        }

        [Test]
        public void ALateRegisteredSectionThatFails_IsNotAReset_TheRestOfTheGameIsLoaded()
        {
            var service = new SaveService();
            var counter = new CounterSection("counter");
            service.Register(counter);
            var load = service.Prepare(Saved(("counter", 6), ("area", 7)));
            service.Register(new CounterSection("area", 2, new SectionUpgrade(1, _ => throw new InvalidOperationException("upgrade broke"))));
            var result = service.Apply(load);
            Assert.That((result.Status, result.RolledBack, result.NeedsReset, counter.Value), Is.EqualTo((LoadStatus.RestoreFailed, false, false, 6)));
        }

        /** <summary>A section that returns the same JSON token every time, owned by something else.</summary> */
        private sealed class CachedJsonSection : ISaveSection
        {
            public JObject Owner { get; } = new JObject { ["cached"] = new JObject { ["z"] = 1, ["a"] = 2 } };

            public string Key => "cached";
            public int Version => 1;
            public IReadOnlyList<SectionUpgrade> Upgrades => Array.Empty<SectionUpgrade>();

            public JToken Capture(Newtonsoft.Json.JsonSerializer serializer) => Owner["cached"];

            public IPreparedRestore Prepare(JToken data, Newtonsoft.Json.JsonSerializer serializer) => NoRestore.Instance;

            public IPreparedRestore PrepareDefault() => NoRestore.Instance;
        }

        [Test]
        public void ASectionsSharedJson_IsWrittenInOrder_ButNeverReorderedInPlace()
        {
            var service = new SaveService();
            var section = new CachedJsonSection();
            service.Register(section);
            var saved = service.Capture().Sections["cached"].Data;
            Assert.That(((JObject)saved).Properties().Select(p => p.Name), Is.EqualTo(new[] { "a", "z" }));
            Assert.That(((JObject)section.Owner["cached"]).Properties().Select(p => p.Name), Is.EqualTo(new[] { "z", "a" }));
        }
    }
}
