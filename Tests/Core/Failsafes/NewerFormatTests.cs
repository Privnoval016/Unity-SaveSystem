using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /**
     * <summary>A save from a newer build is never lost to an older one, at any level: a newer file format, document
     * format or section version. Read as too new, not damaged; nothing older is loaded in its place, whether it is the
     * entry, the backup or a pending write; never written over, whichever of entry and backup is newer, a damaged entry
     * beside a newer backup included; a slot holding one loads as too new, changes nothing, refuses a save, lists as too
     * new, and is never picked by autosave, which rotates the others (or refuses when every autosave is newer).</summary>
     */
    public sealed class NewerFormatTests
    {
        private MemorySaveStorage _storage;
        private SaveFileStore _store;

        [SetUp]
        public void SetUp()
        {
            _storage = new MemorySaveStorage();
            _store = new SaveFileStore(_storage, new SaveSerializer());
        }

        /** <summary>The file with its header's format raised, as a newer build would write it; the checksum covers the payload only, so it stays valid.</summary> */
        private static byte[] Newer(byte[] file) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(file).Replace("{\"format\":1,", "{\"format\":2,"));

        private static byte[] Encoded(int v) => SaveFileCodec.Plain.Encode(new SaveSerializer().ToBytes(new JObject { ["v"] = v }, true));

        private ReadStatus Read() => _store.Read("a.save", json => json).Status;

        [Test]
        public void TheCodec_ReadsANewerFileFormat_AsTooNew_BeforeAnyOtherCheck()
        {
            var file = Newer(SaveFileCodec.Plain.Encode(Encoding.UTF8.GetBytes("{}")));
            Assert.That((SaveFileCodec.Verify(file), SaveFileCodec.Plain.Decode(file).Status), Is.EqualTo((DecodeStatus.TooNew, DecodeStatus.TooNew)));
            Assert.That(SaveFileCodec.Verify(Encoding.UTF8.GetBytes("{\"format\":5}\n{}")), Is.EqualTo(DecodeStatus.TooNew), "too new even with the other fields missing");
        }

        [Test]
        public void ANewerEntry_IsNeitherReplacedByItsBackupOnRead_NorWrittenOver()
        {
            _store.Write("a.save", new JObject { ["v"] = 1 });
            _store.Write("a.save", new JObject { ["v"] = 2 });
            var newer = Newer(_storage.Read("a.save"));
            _storage.Overwrite("a.save", newer);

            Assert.That(Read(), Is.EqualTo(ReadStatus.TooNew));
            Assert.That(_store.Write("a.save", new JObject { ["v"] = 3 }).Status, Is.EqualTo(SaveStatus.TooNew));
            Assert.That(_storage.Read("a.save"), Is.EqualTo(newer));
        }

        [Test]
        public void ANewerBackup_IsKept_BesideAMissingOrDamagedEntry()
        {
            var newer = Newer(Encoded(9));
            _storage.Write("a.save", newer);
            _storage.Write("a.save", Encoding.UTF8.GetBytes("damaged"));
            Assert.That(Read(), Is.EqualTo(ReadStatus.TooNew), "the damaged entry's backup is newer, so nothing is loaded");
            Assert.That(_store.Write("a.save", new JObject { ["v"] = 3 }).Status, Is.EqualTo(SaveStatus.TooNew), "writing would push the newer backup out");
            Assert.That(_storage.ReadBackup("a.save"), Is.EqualTo(newer));

            _storage.Write("b.save", newer);
            _storage.LoseEntry("b.save");
            Assert.That(_store.Read("b.save", json => json).Status, Is.EqualTo(ReadStatus.TooNew), "a missing entry whose backup is newer");
            Assert.That(_store.Write("b.save", new JObject { ["v"] = 3 }).Status, Is.EqualTo(SaveStatus.TooNew));
            Assert.That(_storage.ReadBackup("b.save"), Is.EqualTo(newer));
        }

        [Test]
        public void ANewerPendingWrite_IsNotPassedOverForTheOlderBackup()
        {
            _storage.Write("a.save", Encoded(1));
            _storage.CrashMidWrite("a.save", Newer(Encoded(2)));
            var read = _store.Read("a.save", json => json);
            Assert.That((read.Status, read.Source), Is.EqualTo((ReadStatus.TooNew, ReadSource.Pending)));
        }

        [Test]
        public void ANewerPendingWrite_BesideAMissingEntry_IsNeverWrittenOver()
        {
            var newer = Newer(Encoded(2));
            _storage.CrashMidWrite("a.save", newer);
            Assert.That(_store.Write("a.save", new JObject { ["v"] = 3 }).Status, Is.EqualTo(SaveStatus.TooNew));
            Assert.That(_storage.ReadPending("a.save"), Is.EqualTo(newer));
        }

        [Test]
        public void ANewerDocumentOrSectionVersion_IsFromANewerBuild_AndANewerSlotIsKept()
        {
            var service = new SaveService();
            service.Register(new CounterSection("c", 2, new SectionUpgrade(1, data => data)));
            Assert.That(service.IsFromNewerBuild(JObject.Parse("{\"format\":2,\"sections\":{}}")), Is.True);
            Assert.That(service.IsFromNewerBuild(JObject.Parse("{\"format\":1,\"sections\":{\"c\":{\"version\":3,\"data\":{}}}}")), Is.True);
            Assert.That(service.IsFromNewerBuild(JObject.Parse("{\"format\":1,\"sections\":{\"c\":{\"version\":2,\"data\":{}},\"other\":{\"version\":99}}}")), Is.False, "a section this build doesn't have is held, not newer");
            Assert.That(service.IsFromNewerBuild(JValue.CreateNull()), Is.False);
        }

        [Test]
        public async Task ASlotFromANewerBuild_LoadsAsTooNew_RefusesASave_AndListsAsTooNew()
        {
            var service = new SaveService();
            var counter = new CounterSection("c") { Value = 1 };
            service.Register(counter);
            var slots = new SaveSlots<object>(service, _storage, null, new SlotOptions(new ImmediateSaveWorker()));
            await slots.SaveAsync(SlotId.Manual(1));
            var entry = SlotEntries.Save(SlotId.Manual(1));
            var newer = Newer(_storage.Read(entry));
            _storage.Overwrite(entry, newer);
            counter.Value = 5;

            var load = await slots.LoadAsync(SlotId.Manual(1));
            Assert.That((load.Status, load.LeftStateUnchanged, counter.Value), Is.EqualTo((LoadStatus.TooNew, true, 5)));
            Assert.That((await slots.SaveAsync(SlotId.Manual(1))).Status, Is.EqualTo(SaveStatus.TooNew));
            Assert.That(_storage.Read(entry), Is.EqualTo(newer));
            Assert.That((await slots.ListAsync(true)).Single().SaveState, Is.EqualTo(SlotSaveState.TooNew));

            var header = SlotEntries.Header(SlotId.Manual(1));
            _storage.Overwrite(header, SaveFileCodec.Plain.Encode(Encoding.UTF8.GetBytes("{\"format\":9,\"slot\":\"manual-01\"}")));
            Assert.That((await slots.ListAsync()).Single().State, Is.EqualTo(SlotHeaderState.TooNew));
        }

        [Test]
        public async Task ASlotWhoseSectionIsNewer_IsTooNew_AndIsNeverWrittenOver()
        {
            var newerService = new SaveService();
            newerService.Register(new CounterSection("c", 2, new SectionUpgrade(1, data => data)) { Value = 7 });
            var newerSlots = new SaveSlots<object>(newerService, _storage, null, new SlotOptions(new ImmediateSaveWorker()));
            await newerSlots.SaveAsync(SlotId.Manual(1));
            var written = _storage.Read(SlotEntries.Save(SlotId.Manual(1)));

            var service = new SaveService();
            service.Register(new CounterSection("c"));
            var slots = new SaveSlots<object>(service, _storage, null, new SlotOptions(new ImmediateSaveWorker()));
            Assert.That((await slots.LoadAsync(SlotId.Manual(1))).Status, Is.EqualTo(LoadStatus.TooNew));
            Assert.That((await slots.ListAsync(true)).Single().SaveState, Is.EqualTo(SlotSaveState.TooNew));
            Assert.That((await slots.SaveAsync(SlotId.Manual(1))).Status, Is.EqualTo(SaveStatus.TooNew));
            Assert.That(_storage.Read(SlotEntries.Save(SlotId.Manual(1))), Is.EqualTo(written));
        }

        [Test]
        public async Task Autosave_NeverPicksASlotHoldingANewerSave_RotatesTheOthers_AndRefusesWhenEveryOneIs()
        {
            var service = new SaveService();
            var counter = new CounterSection("c");
            service.Register(counter);
            var slots = new SaveSlots<object>(service, _storage, null, new SlotOptions(new ImmediateSaveWorker()) { AutosaveCount = 2 });
            await slots.AutosaveAsync();
            await slots.AutosaveAsync();
            var oldest = SlotEntries.Save(SlotId.Auto(1));
            var newer = Newer(_storage.Read(oldest));
            _storage.Overwrite(oldest, newer);

            for (var i = 0; i < 3; i++)
            {
                counter.Value = 10 + i;
                var result = await slots.AutosaveAsync();
                Assert.That((result.Status, result.SavedTo), Is.EqualTo((SaveStatus.Saved, "auto-02")));
            }
            Assert.That(_storage.Read(oldest), Is.EqualTo(newer));

            var other = SlotEntries.Save(SlotId.Auto(2));
            _storage.Overwrite(other, Newer(_storage.Read(other)));
            Assert.That((await slots.AutosaveAsync()).Status, Is.EqualTo(SaveStatus.TooNew));
        }
    }
}
