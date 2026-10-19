using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    public sealed class ConcurrencyTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private TestFolder _folder;
        private SaveService _service;
        private CounterSection _counter;

        [SetUp]
        public void SetUp()
        {
            _folder = new TestFolder();
            _service = new SaveService();
            _counter = new CounterSection("counter");
            _service.Register(_counter);
        }

        [TearDown]
        public void TearDown() => _folder.Dispose();

        private SaveSlots<object> Slots(ISaveStorage storage, ISaveWorker worker) =>
            new SaveSlots<object>(_service, storage, null, new SlotOptions { Worker = worker });

        [Test]
        public async Task TwoSavesToOneSlot_RunInOrder_AndTheSecondWins()
        {
            var slots = Slots(new FileSaveStorage(_folder.Root), new BackgroundSaveWorker());
            _counter.Value = 1;
            var first = slots.SaveAsync(SlotId.Manual(1));
            _counter.Value = 2;
            var second = slots.SaveAsync(SlotId.Manual(1));
            await Task.WhenAll(first, second);

            _counter.Value = 0;
            await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(_counter.Value, Is.EqualTo(2));
        }

        [Test]
        public async Task Capture_HappensWhenSaveIsCalled_NotWhenTheWriteRuns()
        {
            var storage = new SpyStorage();
            storage.WriteGate.Reset();
            var slots = Slots(storage, new BackgroundSaveWorker());
            _counter.Value = 1;
            var save = slots.SaveAsync(SlotId.Manual(1));
            _counter.Value = 99;
            storage.WriteGate.Set();
            await save;

            _counter.Value = 0;
            await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(_counter.Value, Is.EqualTo(1));
        }

        [Test]
        public async Task LoadDuringASave_WaitsForIt()
        {
            var storage = new SpyStorage();
            var slots = Slots(storage, new BackgroundSaveWorker());
            _counter.Value = 1;
            await slots.SaveAsync(SlotId.Manual(1));

            storage.WriteGate.Reset();
            storage.WriteStarted.Reset();
            _counter.Value = 2;
            var save = slots.SaveAsync(SlotId.Manual(1));
            Assert.That(storage.WriteStarted.Wait(Patience));
            var load = slots.LoadAsync(SlotId.Manual(1));
            await Task.Delay(100);
            Assert.That(load.IsCompleted, Is.False);

            storage.WriteGate.Set();
            await save;
            _counter.Value = 0;
            await load;
            Assert.That(_counter.Value, Is.EqualTo(2));
        }

        [Test]
        public async Task ManyInterleavedSaves_LeaveEveryFileWhole()
        {
            var storage = new FileSaveStorage(_folder.Root);
            var slots = Slots(storage, new BackgroundSaveWorker());
            var saves = new List<Task<SaveResult>>();
            for (var i = 1; i <= 50; i++)
            {
                _counter.Value = i;
                saves.Add(slots.SaveAsync(SlotId.Manual(i % 2 + 1)));
                saves.Add(slots.AutosaveAsync());
            }
            var results = await Task.WhenAll(saves);
            Assert.That(results.All(result => result.Succeeded));

            foreach (var slot in new[] { SlotId.Manual(1), SlotId.Manual(2), SlotId.Auto(1), SlotId.Auto(2), SlotId.Auto(3) })
            {
                var loaded = await slots.LoadAsync(slot);
                Assert.That(loaded.Succeeded, slot.Name);
                Assert.That(loaded.UsedBackup, Is.False, slot.Name);
            }
            await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(_counter.Value, Is.EqualTo(50));
            await slots.LoadAsync(SlotId.Manual(2));
            Assert.That(_counter.Value, Is.EqualTo(49));

            var sequences = (await slots.ListAsync()).Select(summary => summary.Header.Sequence).ToList();
            Assert.That(sequences.Distinct().Count(), Is.EqualTo(sequences.Count));
            Assert.That(sequences.Max(), Is.EqualTo(100));
        }

        [Test]
        public void Flush_WaitsForPendingWrites_WithoutNeedingTheMainThread()
        {
            var storage = new SpyStorage();
            storage.WriteGate.Reset();
            var slots = Slots(storage, new BackgroundSaveWorker());
            var save = slots.SaveAsync(SlotId.Manual(1));

            Assert.That(slots.Flush(TimeSpan.FromMilliseconds(100)), Is.False);
            storage.WriteGate.Set();
            Assert.That(slots.Flush(Patience));
            Assert.That(save.IsCompleted);
            Assert.That(save.Result.Succeeded);
        }

        [Test]
        public async Task Queues_DoNotWaitForEachOther()
        {
            var worker = new BackgroundSaveWorker();
            var gate = new ManualResetEventSlim(false);
            var blocked = worker.Run("a", () => gate.Wait(Patience));
            var other = await worker.Run("b", () => 7);
            Assert.That(other, Is.EqualTo(7));
            Assert.That(blocked.IsCompleted, Is.False);
            gate.Set();
            Assert.That(await blocked);
        }

        [Test]
        public async Task OneQueue_RunsInOrder_EvenWhenAJobThrows()
        {
            var worker = new BackgroundSaveWorker();
            var order = new List<int>();
            var jobs = new List<Task>();
            for (var i = 0; i < 20; i++)
            {
                var number = i;
                jobs.Add(worker.Run("q", () =>
                {
                    lock (order) order.Add(number);
                    if (number == 5) throw new InvalidOperationException("job 5");
                    return number;
                }));
            }
            try
            {
                await Task.WhenAll(jobs);
            }
            catch (InvalidOperationException)
            {
            }
            Assert.That(order, Is.EqualTo(Enumerable.Range(0, 20)));
            Assert.That(jobs[5].IsFaulted);
            Assert.That(jobs[6].IsFaulted, Is.False);
        }

        [Test]
        public async Task BackgroundWrites_UseInvariantCulture_WhateverTheThreadCulture()
        {
            var previous = CultureInfo.DefaultThreadCurrentCulture;
            try
            {
                CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("de-DE");
                var profile = new ProfileSection();
                profile.Data.FloatValue = 2.5f;
                _service.Register(profile);
                var storage = new MemorySaveStorage();
                var slots = Slots(storage, new BackgroundSaveWorker());
                await slots.SaveAsync(SlotId.Manual(1));

                Assert.That(Encoding.UTF8.GetString(storage.Read("manual-01.save")), Does.Contain("2.5"));
                profile.Data = new ProfileData();
                await slots.LoadAsync(SlotId.Manual(1));
                Assert.That(profile.Data.FloatValue, Is.EqualTo(2.5f));
            }
            finally
            {
                CultureInfo.DefaultThreadCurrentCulture = previous;
            }
        }

        [Test]
        public void ImmediateWorker_RunsAtOnce_AndCarriesExceptions()
        {
            var worker = new ImmediateSaveWorker();
            Assert.That(worker.Run("q", () => 3).Result, Is.EqualTo(3));
            Assert.That(worker.Run<int>("q", () => throw new InvalidOperationException()).IsFaulted);
            Assert.That(worker.WaitForAll(TimeSpan.Zero));
        }
    }
}
