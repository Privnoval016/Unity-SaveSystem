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
    /** <summary>Background writes: ordering, waiting, flushing, culture on worker threads.</summary> */
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

        /** <summary>Runs every job under a given culture on the pool thread itself.</summary> */
        private sealed class CultureWorker : ISaveWorker
        {
            private readonly BackgroundSaveWorker _inner = new BackgroundSaveWorker();
            private readonly CultureInfo _culture;

            public CultureWorker(CultureInfo culture) => _culture = culture;

            public Task<T> Run<T>(string queue, Func<T> work) => _inner.Run(queue, () =>
            {
                var previous = Thread.CurrentThread.CurrentCulture;
                Thread.CurrentThread.CurrentCulture = _culture;
                try
                {
                    return work();
                }
                finally
                {
                    Thread.CurrentThread.CurrentCulture = previous;
                }
            });

            public bool WaitForAll(TimeSpan timeout) => _inner.WaitForAll(timeout);
        }

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
            Task<SaveResult> save;
            try
            {
                save = slots.SaveAsync(SlotId.Manual(1));
                _counter.Value = 99;
            }
            finally
            {
                storage.WriteGate.Set();
            }
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
            Task<SaveResult> save;
            Task<LoadResult> load;
            try
            {
                save = slots.SaveAsync(SlotId.Manual(1));
                Assert.That(storage.WriteStarted.Wait(Patience));
                load = slots.LoadAsync(SlotId.Manual(1));
                await Task.Delay(300);
                Assert.That(load.IsCompleted, Is.False);
            }
            finally
            {
                storage.WriteGate.Set();
            }
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
            try
            {
                Assert.That(slots.Flush(TimeSpan.FromMilliseconds(100)), Is.False);
            }
            finally
            {
                storage.WriteGate.Set();
            }
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

        [TestCase("de-DE")]
        [TestCase("tr-TR")]
        public async Task BackgroundWrites_UseInvariantCulture_WhateverTheWorkerThreadsCulture(string culture)
        {
            var profile = new ProfileSection();
            profile.Data.FloatValue = 2.5f;
            profile.Data.Name = "TITLE İı";
            _service.Register(profile);
            var storage = new MemorySaveStorage();
            var slots = Slots(storage, new CultureWorker(new CultureInfo(culture)));
            await slots.SaveAsync(SlotId.Named("quick"));

            Assert.That(Encoding.UTF8.GetString(storage.Read("quick.save")), Does.Contain("2.5"));
            profile.Data = new ProfileData();
            Assert.That((await slots.LoadAsync(SlotId.Named("quick"))).Succeeded);
            Assert.That(profile.Data.FloatValue, Is.EqualTo(2.5f));
            Assert.That(profile.Data.Name, Is.EqualTo("TITLE İı"));
        }

        [Test]
        public void WaitForAll_WithAFaultedJob_StillReportsDone()
        {
            var worker = new BackgroundSaveWorker();
            var failing = worker.Run<int>("q", () => throw new InvalidOperationException("boom"));
            Assert.That(worker.WaitForAll(Patience));
            Assert.That(failing.IsFaulted);
        }

        [Test]
        public void WaitForAll_WithNothingQueued_IsDone()
        {
            Assert.That(new BackgroundSaveWorker().WaitForAll(TimeSpan.Zero));
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
