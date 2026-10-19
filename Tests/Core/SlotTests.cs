using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>Save slots: headers, sequences, autosave rotation, repair and deletion.</summary> */
    public sealed class SlotTests
    {
        public sealed class TestInfo
        {
            public string Place;
            public double PlaySeconds;
        }

        private SpyStorage _storage;
        private SaveService _service;
        private CounterSection _counter;
        private DateTime _now;
        private string _place;

        [SetUp]
        public void SetUp()
        {
            _storage = new SpyStorage();
            _service = new SaveService();
            _counter = new CounterSection("counter");
            _service.Register(_counter);
            _now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
            _place = "harbour";
        }

        private SaveSlots<TestInfo> Slots(int autosaves = 3) => new SaveSlots<TestInfo>(_service, _storage,
            () => new TestInfo { Place = _place, PlaySeconds = 61.5 },
            new SlotOptions(new ImmediateSaveWorker()) { AutosaveCount = autosaves, UtcNow = () => _now });

        [Test]
        public async Task Save_ThenLoad_RestoresTheState()
        {
            var slots = Slots();
            _counter.Value = 5;
            var saved = await slots.SaveAsync(SlotId.Manual(1));
            _counter.Value = 0;
            var loaded = await slots.LoadAsync(SlotId.Manual(1));

            Assert.That(saved.Succeeded);
            Assert.That(saved.SavedTo, Is.EqualTo("manual-01"));
            Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(_counter.Value, Is.EqualTo(5));
        }

        [Test]
        public async Task Header_HoldsTheGamesFieldsAndTime()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(2));
            var summary = (await slots.ListAsync()).Single();

            Assert.That(summary.Slot, Is.EqualTo(SlotId.Manual(2)));
            Assert.That(summary.State, Is.EqualTo(SlotHeaderState.Ok));
            Assert.That(summary.Header.Info.Place, Is.EqualTo("harbour"));
            Assert.That(summary.Header.Info.PlaySeconds, Is.EqualTo(61.5));
            Assert.That(summary.Header.SavedAtUtc, Is.EqualTo(_now));
            Assert.That(summary.Header.Sequence, Is.EqualTo(1));
        }

        [Test]
        public async Task NoInfo_IsAllowed()
        {
            var slots = new SaveSlots<TestInfo>(_service, _storage, null, new SlotOptions(new ImmediateSaveWorker()));
            await slots.SaveAsync(SlotId.Manual(1));
            var summary = (await slots.ListAsync()).Single();
            Assert.That(summary.State, Is.EqualTo(SlotHeaderState.Ok));
            Assert.That(summary.Header.Info, Is.Null);
        }

        [Test]
        public async Task Sequence_CountsUpAcrossSlots()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            await slots.SaveAsync(SlotId.Manual(2));
            await slots.SaveAsync(SlotId.Manual(1));
            var sequences = (await slots.ListAsync()).ToDictionary(summary => summary.Slot.Name, summary => summary.Header.Sequence);
            Assert.That(sequences["manual-01"], Is.EqualTo(3));
            Assert.That(sequences["manual-02"], Is.EqualTo(2));
        }

        [Test]
        public async Task Autosave_FillsEmptySlotsThenOverwritesTheOldest()
        {
            var slots = Slots(3);
            var used = new string[5];
            for (var i = 0; i < 5; i++)
                used[i] = (await slots.AutosaveAsync()).SavedTo;
            Assert.That(used, Is.EqualTo(new[] { "auto-01", "auto-02", "auto-03", "auto-01", "auto-02" }));
        }

        [Test]
        public async Task Autosave_IgnoresTheClock_WhenItGoesBackwards()
        {
            var slots = Slots(2);
            await slots.AutosaveAsync();
            _now = _now.AddYears(-5);
            await slots.AutosaveAsync();
            _now = _now.AddYears(10);
            Assert.That((await slots.AutosaveAsync()).SavedTo, Is.EqualTo("auto-01"));
        }

        [Test]
        public async Task Autosave_RefillsADeletedSlotFirst()
        {
            var slots = Slots(3);
            for (var i = 0; i < 3; i++) await slots.AutosaveAsync();
            await slots.DeleteAsync(SlotId.Auto(2));
            Assert.That((await slots.AutosaveAsync()).SavedTo, Is.EqualTo("auto-02"));
        }

        [Test]
        public async Task Autosave_WithADamagedHeader_UsesTheSequenceInsideTheSave()
        {
            var slots = Slots(3);
            for (var i = 0; i < 3; i++) await slots.AutosaveAsync();
            _storage.Inner.Delete("auto-03.head");
            _storage.Inner.Overwrite("auto-03.head", Encoding.UTF8.GetBytes("junk"));
            Assert.That((await slots.AutosaveAsync()).SavedTo, Is.EqualTo("auto-01"));
        }

        [Test]
        public async Task Autosave_WithTheNewestHeaderMissing_DoesNotOverwriteTheNewest()
        {
            var slots = Slots(3);
            for (var i = 0; i < 3; i++) await slots.AutosaveAsync();
            _storage.Inner.Delete("auto-03.head");
            Assert.That((await slots.AutosaveAsync()).SavedTo, Is.EqualTo("auto-01"));
            Assert.That((await slots.ListAsync()).Single(summary => summary.Slot.Name == "auto-01").Header.Sequence, Is.EqualTo(4));
        }

        [Test]
        public async Task Autosave_WithSaveAndHeaderUnreadable_OverwritesThatSlotFirst()
        {
            var slots = Slots(3);
            for (var i = 0; i < 3; i++) await slots.AutosaveAsync();
            _storage.Inner.Delete("auto-02.head");
            _storage.Inner.Delete("auto-02.save");
            _storage.Inner.Overwrite("auto-02.save", Encoding.UTF8.GetBytes("junk"));
            Assert.That((await slots.AutosaveAsync()).SavedTo, Is.EqualTo("auto-02"));
        }

        [Test]
        public async Task OneAutosave_AlwaysUsesIt()
        {
            var slots = Slots(1);
            await slots.AutosaveAsync();
            Assert.That((await slots.AutosaveAsync()).SavedTo, Is.EqualTo("auto-01"));
        }

        [TestCase(0)]
        [TestCase(SlotId.MaxNumber + 1)]
        public void AutosaveCountOutOfRange_Throws(int count)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Slots(count));
        }

        [Test]
        public async Task Listing_ReadsOnlyHeaders()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            await slots.AutosaveAsync();
            var before = _storage.ReadsEndingWith(".save");

            var listed = await slots.ListAsync();

            Assert.That(listed.Select(summary => summary.Slot.Name), Is.EqualTo(new[] { "auto-01", "manual-01" }));
            Assert.That(_storage.ReadsEndingWith(".save"), Is.EqualTo(before));
        }

        [Test]
        public async Task MissingHeader_IsListed_AndLoadingRewritesIt()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            _storage.Inner.Delete("manual-01.head");

            Assert.That((await slots.ListAsync()).Single().State, Is.EqualTo(SlotHeaderState.Missing));
            await slots.LoadAsync(SlotId.Manual(1));
            var summary = (await slots.ListAsync()).Single();
            Assert.That(summary.State, Is.EqualTo(SlotHeaderState.Ok));
            Assert.That(summary.Header.Info.Place, Is.EqualTo("harbour"));
        }

        [Test]
        public async Task DamagedHeader_IsListed_AndRepairRewritesIt()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            _storage.Inner.Delete("manual-01.head");
            _storage.Inner.Overwrite("manual-01.head", Encoding.UTF8.GetBytes("{broken"));

            Assert.That((await slots.ListAsync()).Single().State, Is.EqualTo(SlotHeaderState.Damaged));
            Assert.That(await slots.RepairHeaderAsync(SlotId.Manual(1)));
            Assert.That((await slots.ListAsync()).Single().State, Is.EqualTo(SlotHeaderState.Ok));
        }

        [Test]
        public async Task HeaderCopiedFromAnotherSlot_IsDamaged()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            await slots.SaveAsync(SlotId.Manual(2));
            _storage.Inner.Delete("manual-02.head");
            _storage.Inner.Overwrite("manual-02.head", _storage.Inner.Read("manual-01.head"));

            var summary = (await slots.ListAsync()).Single(item => item.Slot.Name == "manual-02");
            Assert.That(summary.State, Is.EqualTo(SlotHeaderState.Damaged));
        }

        [Test]
        public async Task StaleHeader_IsRefreshedOnLoad()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            var oldHeader = _storage.Inner.Read("manual-01.head");
            _place = "mountain";
            await slots.SaveAsync(SlotId.Manual(1));
            _storage.Inner.Overwrite("manual-01.head", oldHeader);

            Assert.That((await slots.ListAsync()).Single().Header.Info.Place, Is.EqualTo("harbour"));
            await slots.LoadAsync(SlotId.Manual(1));
            Assert.That((await slots.ListAsync()).Single().Header.Info.Place, Is.EqualTo("mountain"));
        }

        [Test]
        public async Task Delete_RemovesEverything()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            await slots.SaveAsync(SlotId.Manual(1));

            Assert.That(await slots.DeleteAsync(SlotId.Manual(1)));
            Assert.That(_storage.List(), Is.Empty);
            Assert.That(_storage.ReadBackup("manual-01.save"), Is.Null);
            Assert.That(_storage.ReadBackup("manual-01.head"), Is.Null);
            Assert.That((await slots.LoadAsync(SlotId.Manual(1))).Status, Is.EqualTo(LoadStatus.Missing));
        }

        [Test]
        public async Task LoadingAnEmptySlot_IsMissing_AndChangesNothing()
        {
            _counter.Value = 3;
            Assert.That((await Slots().LoadAsync(SlotId.Manual(9))).Status, Is.EqualTo(LoadStatus.Missing));
            Assert.That(_counter.Value, Is.EqualTo(3));
        }

        [Test]
        public async Task DamagedSave_LoadsTheBackup_AndSaysSo()
        {
            var slots = Slots();
            _counter.Value = 1;
            await slots.SaveAsync(SlotId.Manual(1));
            _counter.Value = 2;
            await slots.SaveAsync(SlotId.Manual(1));
            var bytes = _storage.Inner.Read("manual-01.save");
            _storage.Inner.Overwrite("manual-01.save", bytes.Take(bytes.Length / 2).ToArray());

            var result = await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(result.Succeeded);
            Assert.That(result.UsedBackup);
            Assert.That(result.Problem, Does.Contain("bytes"));
            Assert.That(_counter.Value, Is.EqualTo(1));
        }

        [Test]
        public async Task SaveAndBackupDamaged_IsDamaged_AndChangesNothing()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            _storage.Inner.Overwrite("manual-01.save", Encoding.UTF8.GetBytes("nope"));
            _counter.Value = 42;

            var result = await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(result.Status, Is.EqualTo(LoadStatus.Damaged));
            Assert.That(_counter.Value, Is.EqualTo(42));
        }

        [Test]
        public async Task CaptureFailure_WritesNothing()
        {
            var slots = Slots();
            _counter.Value = 1;
            await slots.SaveAsync(SlotId.Manual(1));
            var before = _storage.Inner.Read("manual-01.save");
            _counter.ThrowOnCapture = true;

            var result = await slots.SaveAsync(SlotId.Manual(1));
            Assert.That(result.Status, Is.EqualTo(SaveStatus.CaptureFailed));
            Assert.That(result.Problem, Does.Contain("counter"));
            Assert.That(_storage.Inner.Read("manual-01.save"), Is.EqualTo(before));
        }

        [Test]
        public async Task InfoCaptureFailure_WritesNothing()
        {
            var slots = new SaveSlots<TestInfo>(_service, _storage, () => throw new InvalidOperationException("no info"),
                new SlotOptions(new ImmediateSaveWorker()));
            var result = await slots.SaveAsync(SlotId.Manual(1));
            Assert.That(result.Status, Is.EqualTo(SaveStatus.CaptureFailed));
            Assert.That(_storage.List(), Is.Empty);
        }

        [Test]
        public async Task WriteFailure_KeepsThePreviousSaveAndHeader()
        {
            var slots = Slots();
            _counter.Value = 1;
            await slots.SaveAsync(SlotId.Manual(1));
            _storage.FailWrite = name => name == "manual-01.save";
            _counter.Value = 2;

            var result = await slots.SaveAsync(SlotId.Manual(1));
            _storage.FailWrite = _ => false;
            Assert.That(result.Status, Is.EqualTo(SaveStatus.WriteFailed));
            Assert.That((await slots.ListAsync()).Single().Header.Sequence, Is.EqualTo(1));
            await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(_counter.Value, Is.EqualTo(1));
        }

        [Test]
        public async Task HeaderWriteFailure_StillSaves_AndSaysSo()
        {
            var slots = Slots();
            _storage.FailWrite = name => name.EndsWith(".head", StringComparison.Ordinal);
            var result = await slots.SaveAsync(SlotId.Manual(1));
            _storage.FailWrite = _ => false;

            Assert.That(result.Succeeded);
            Assert.That(result.Problem, Does.Contain("manual-01.head"));
            Assert.That((await slots.ListAsync()).Single().State, Is.EqualTo(SlotHeaderState.Missing));
        }

        [Test]
        public async Task OtherEntries_AreNotListedAsSlots()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            _storage.Inner.Write("settings.cfg", new byte[] { 1 });
            _storage.Inner.Write("notes.head", new byte[] { 1 });
            _storage.Inner.Write("this-name-is-far-too-long-to-be-a-slot-name.save", new byte[] { 1 });
            Assert.That((await slots.ListAsync()).Select(summary => summary.Slot.Name), Is.EqualTo(new[] { "manual-01" }));
        }

        [Test]
        public async Task NamedSlot_Works()
        {
            var slots = Slots();
            _counter.Value = 8;
            await slots.SaveAsync(SlotId.Named("quick"));
            _counter.Value = 0;
            await slots.LoadAsync(SlotId.Named("quick"));
            Assert.That(_counter.Value, Is.EqualTo(8));
        }

        [Test]
        public async Task EncryptedSlots_RoundTrip_AndHeadersStayPlain()
        {
            var slots = new SaveSlots<TestInfo>(_service, _storage, () => new TestInfo { Place = "harbour" }, new SlotOptions(new ImmediateSaveWorker())
            {
                Codec = new SaveFileCodec(new ISaveTransform[] { new GzipTransform(), new AesTransform(new byte[32]) }),
            });
            _counter.Value = 77;
            await slots.SaveAsync(SlotId.Manual(1));
            _counter.Value = 0;
            await slots.LoadAsync(SlotId.Manual(1));

            Assert.That(_counter.Value, Is.EqualTo(77));
            Assert.That(Encoding.UTF8.GetString(_storage.Inner.Read("manual-01.save")), Does.Not.Contain("counter"));
            Assert.That(Encoding.UTF8.GetString(_storage.Inner.Read("manual-01.head")), Does.Contain("harbour"));
        }

        [Test]
        public void SlotIds_FollowTheRules()
        {
            Assert.That(SlotId.Manual(1).Name, Is.EqualTo("manual-01"));
            Assert.That(SlotId.Manual(123).Name, Is.EqualTo("manual-123"));
            Assert.That(SlotId.Auto(3).IsAuto);
            Assert.That(SlotId.Manual(3).IsAuto, Is.False);
            Assert.That(SlotId.Named("quick"), Is.EqualTo(SlotId.Named("quick")));
            Assert.That(SlotId.Named("quick").GetHashCode(), Is.EqualTo(SlotId.Named("quick").GetHashCode()));
            Assert.Throws<ArgumentOutOfRangeException>(() => SlotId.Manual(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SlotId.Auto(1000));
            Assert.Throws<ArgumentException>(() => SlotId.Named("auto-01"));
            Assert.Throws<ArgumentException>(() => SlotId.Named("Quick"));
            Assert.Throws<ArgumentException>(() => SlotId.Named("quick.save"));
            Assert.Throws<ArgumentException>(() => SlotId.Named(""));
            Assert.Throws<ArgumentException>(() => SlotId.Named(new string('a', 33)));
            Assert.Throws<ArgumentException>(() => Slots().SaveAsync(default));
        }
    }
}
