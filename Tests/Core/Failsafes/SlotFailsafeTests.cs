using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /**
     * <summary>Slots under pressure: while a load reads, saves, autosaves, loads and a new game are busy and nothing is
     * captured or written, for slots and settings alike; the busy flag clears however a load ends (missing, damaged, too
     * new, unreadable); a slot write that fails returns a result and logs; a save carries the time it was captured,
     * read once on the calling thread; header info is written in canonical order; a checked list says which saves load,
     * from the backup, are damaged or unreadable, and rewrites stale, missing and damaged headers.</summary>
     */
    public sealed class SlotFailsafeTests
    {
        /** <summary>Header info whose dictionary's insertion order must never show.</summary> */
        public sealed class TaggedInfo
        {
            public Dictionary<string, int> Tags = new Dictionary<string, int>();
        }

        private SaveService _service;
        private CounterSection _counter;

        [SetUp]
        public void SetUp()
        {
            _service = new SaveService();
            _counter = new CounterSection("c") { Value = 1 };
            _service.Register(_counter);
        }

        private SaveSlots<object> Slots(ISaveStorage storage, ISaveWorker worker = null, RecordingLog log = null) =>
            new SaveSlots<object>(_service, storage, null, new SlotOptions(worker ?? new ImmediateSaveWorker()) { Log = log });

        [Test]
        public async Task WhileALoadReads_SavesLoadsAndANewGameAreBusy_AndNothingIsCapturedOrWritten()
        {
            var storage = new SpyStorage();
            var slots = Slots(storage, new BackgroundSaveWorker());
            await slots.SaveAsync(SlotId.Manual(1));
            storage.WriteGate.Reset();
            try
            {
                storage.WriteStarted.Reset();
                var blocker = slots.SaveAsync(SlotId.Manual(2));
                Assert.That(storage.WriteStarted.Wait(TimeSpan.FromSeconds(10)));
                _counter.Value = 2;

                var load = slots.LoadAsync(SlotId.Manual(1));
                var captures = _counter.CaptureCount;
                Assert.That(slots.Loading, Is.True);
                Assert.That((await slots.SaveAsync(SlotId.Manual(3))).Status, Is.EqualTo(SaveStatus.Busy));
                Assert.That((await slots.AutosaveAsync()).Status, Is.EqualTo(SaveStatus.Busy));
                Assert.That((await slots.LoadAsync(SlotId.Manual(2))).Status, Is.EqualTo(LoadStatus.Busy));
                Assert.That((slots.NewGame().Status, _counter.Value), Is.EqualTo((LoadStatus.Busy, 2)), "a new game would be overwritten by the load landing on it");
                Assert.That(_counter.CaptureCount, Is.EqualTo(captures), "a busy save captures nothing");

                storage.WriteGate.Set();
                await blocker;
                Assert.That((await load).Succeeded);
            }
            finally
            {
                storage.WriteGate.Set();
            }
            Assert.That((slots.Loading, _counter.Value), Is.EqualTo((false, 1)));
            Assert.That(storage.Inner.Read(SlotEntries.Save(SlotId.Manual(3))), Is.Null, "the busy save wrote nothing");
            Assert.That((await slots.SaveAsync(SlotId.Manual(3))).Succeeded);
            Assert.That(slots.NewGame().Succeeded);
        }

        [Test]
        public async Task TheBusyFlag_ClearsHoweverALoadEnds()
        {
            var storage = new SpyStorage();
            var slots = Slots(storage);
            await slots.SaveAsync(SlotId.Manual(1));
            await slots.SaveAsync(SlotId.Manual(2));
            storage.Inner.Overwrite(SlotEntries.Save(SlotId.Manual(2)), Encoding.UTF8.GetBytes("damaged"));
            storage.Inner.Write(SlotEntries.Save(SlotId.Manual(2)), Encoding.UTF8.GetBytes("damaged"));

            Assert.That((await slots.LoadAsync(SlotId.Manual(9))).Status, Is.EqualTo(LoadStatus.Missing));
            Assert.That(slots.Loading, Is.False);
            Assert.That((await slots.LoadAsync(SlotId.Manual(2))).Status, Is.EqualTo(LoadStatus.Damaged));
            Assert.That(slots.Loading, Is.False);
            storage.FailRead = name => name.EndsWith(".save", StringComparison.Ordinal);
            Assert.That((await slots.LoadAsync(SlotId.Manual(1))).Status, Is.EqualTo(LoadStatus.Unreadable));
            Assert.That(slots.Loading, Is.False);
            storage.FailRead = _ => false;
            Assert.That((await slots.SaveAsync(SlotId.Manual(3))).Succeeded, "saving works again after each");
        }

        [Test]
        public async Task Autosave_PassesOverASlotThatCantBeReadJustNow()
        {
            var storage = new SpyStorage();
            var slots = new SaveSlots<object>(_service, storage, null, new SlotOptions(new ImmediateSaveWorker()) { AutosaveCount = 2 });
            await slots.AutosaveAsync();
            await slots.AutosaveAsync();
            storage.FailRead = name => name == SlotEntries.Save(SlotId.Auto(1));
            for (var i = 0; i < 2; i++)
                Assert.That((await slots.AutosaveAsync()).SavedTo, Is.EqualTo("auto-02"), "the oldest is held by another program; the other rotates");
        }

        [Test]
        public async Task SettingsToo_AreBusyWhileTheyLoad()
        {
            var storage = new SpyStorage();
            var settings = new SettingsStore(_service, storage, new SaveFileOptions(new BackgroundSaveWorker()));
            await settings.SaveAsync();
            storage.WriteGate.Reset();
            try
            {
                storage.WriteStarted.Reset();
                var blocker = settings.SaveAsync();
                Assert.That(storage.WriteStarted.Wait(TimeSpan.FromSeconds(10)));
                var load = settings.LoadAsync();
                Assert.That(settings.Loading, Is.True);
                Assert.That((await settings.SaveAsync()).Status, Is.EqualTo(SaveStatus.Busy));
                storage.WriteGate.Set();
                await blocker;
                Assert.That((await load).Succeeded);
            }
            finally
            {
                storage.WriteGate.Set();
            }
            Assert.That(settings.Loading, Is.False);
        }

        [Test]
        public async Task AFailedSlotWrite_IsAResult_AndLogged()
        {
            var log = new RecordingLog();
            var storage = new SpyStorage { FailWrite = name => name.EndsWith(".save", StringComparison.Ordinal) };
            var result = await Slots(storage, log: log).SaveAsync(SlotId.Manual(1));
            Assert.That(result.Status, Is.EqualTo(SaveStatus.WriteFailed));
            Assert.That(log.Warnings.Single(), Does.Contain("manual-01"));
        }

        [Test]
        public async Task ASave_CarriesTheTimeItWasCaptured_ReadOnceOnTheCallingThread()
        {
            var storage = new SpyStorage();
            var start = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var calls = new List<int>();
            var slots = new SaveSlots<object>(_service, storage, null, new SlotOptions(new BackgroundSaveWorker())
            {
                UtcNow = () =>
                {
                    lock (calls) calls.Add(Thread.CurrentThread.ManagedThreadId);
                    return start.AddMinutes(calls.Count);
                },
            });
            var caller = Thread.CurrentThread.ManagedThreadId;
            Assert.That((await slots.SaveAsync(SlotId.Manual(1))).Succeeded);
            Assert.That(calls, Is.EqualTo(new[] { caller }));
            Assert.That((await slots.ListAsync()).Single().Header.SavedAtUtc, Is.EqualTo(start.AddMinutes(1)));
        }

        [Test]
        public async Task HeaderInfo_IsWrittenInCanonicalOrder()
        {
            var first = new MemorySaveStorage();
            var second = new MemorySaveStorage();
            var a = new TaggedInfo { Tags = { ["b"] = 2, ["a"] = 1 } };
            var b = new TaggedInfo { Tags = { ["a"] = 1, ["b"] = 2 } };
            var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await new SaveSlots<TaggedInfo>(_service, first, () => a, new SlotOptions(new ImmediateSaveWorker()) { UtcNow = () => at }).SaveAsync(SlotId.Manual(1));
            await new SaveSlots<TaggedInfo>(_service, second, () => b, new SlotOptions(new ImmediateSaveWorker()) { UtcNow = () => at }).SaveAsync(SlotId.Manual(1));
            Assert.That(first.Read(SlotEntries.Header(SlotId.Manual(1))), Is.EqualTo(second.Read(SlotEntries.Header(SlotId.Manual(1)))));
            Assert.That(first.Read(SlotEntries.Save(SlotId.Manual(1))), Is.EqualTo(second.Read(SlotEntries.Save(SlotId.Manual(1)))), "the whole save file, header copy and checksum included");
        }

        [Test]
        public async Task ACheckedList_SaysWhichSavesLoad_AndRewritesStaleMissingAndDamagedHeaders()
        {
            var storage = new SpyStorage();
            var slots = Slots(storage);
            for (var number = 1; number <= 5; number++)
            {
                await slots.SaveAsync(SlotId.Manual(number));
                await slots.SaveAsync(SlotId.Manual(number));
            }
            var staleHeader = storage.Inner.Read(SlotEntries.Header(SlotId.Manual(1)));
            await slots.SaveAsync(SlotId.Manual(1));
            storage.Inner.Overwrite(SlotEntries.Header(SlotId.Manual(1)), staleHeader);
            storage.Inner.Overwrite(SlotEntries.Save(SlotId.Manual(2)), Encoding.UTF8.GetBytes("damaged"));
            storage.Inner.Write(SlotEntries.Save(SlotId.Manual(3)), Encoding.UTF8.GetBytes("damaged"));
            storage.Inner.Write(SlotEntries.Save(SlotId.Manual(3)), Encoding.UTF8.GetBytes("damaged too"));
            storage.Inner.Delete(SlotEntries.Header(SlotId.Manual(4)));
            storage.Inner.Overwrite(SlotEntries.Header(SlotId.Manual(5)), Encoding.UTF8.GetBytes("garbage"));
            storage.FailRead = name => name == SlotEntries.Save(SlotId.Manual(5));

            Assert.That((await slots.ListAsync()).Select(slot => slot.SaveState), Is.All.EqualTo(SlotSaveState.Unchecked));
            var listed = await slots.ListAsync(true);
            Assert.That(listed.Select(slot => slot.SaveState), Is.EqualTo(new[] { SlotSaveState.Ok, SlotSaveState.FromBackup, SlotSaveState.Damaged, SlotSaveState.Ok, SlotSaveState.Unreadable }));
            Assert.That(listed[1].SaveProblem, Is.Not.Empty);
            Assert.That(listed[0].Header.Sequence, Is.EqualTo(11), "the stale header was rewritten from the save");
            Assert.That((listed[3].State, listed[3].Header.Sequence), Is.EqualTo((SlotHeaderState.Ok, 8L)), "a missing header was rebuilt");
            storage.FailRead = _ => false;
            var again = await slots.ListAsync(true);
            Assert.That((again[4].State, again[4].Header.Sequence), Is.EqualTo((SlotHeaderState.Ok, 10L)), "a damaged header was rebuilt once the save could be read");
        }
    }
}
