using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>Storage, file format, naming and slot cases found in review.</summary> */
    public sealed class ReviewFixTests
    {
        private TestFolder _folder;

        [SetUp]
        public void SetUp() => _folder = new TestFolder();

        [TearDown]
        public void TearDown() => _folder.Dispose();

        private static JObject Nested(int depth)
        {
            var root = new JObject();
            var current = root;
            for (var i = 1; i < depth; i++)
            {
                var child = new JObject();
                current["c"] = child;
                current = child;
            }
            return root;
        }

        [Test]
        public void DataNestedPastTheLimit_IsRefused_AndThePreviousSaveIsIntact()
        {
            var storage = new MemorySaveStorage();
            var store = new SaveFileStore(storage, new SaveSerializer());
            store.Write("a.save", new JObject { ["v"] = 1 });

            var result = store.Write("a.save", Nested(SaveSerializer.MaxDepth + 1));

            Assert.That(result.Status, Is.EqualTo(SaveStatus.WriteFailed));
            Assert.That(result.Problem, Does.Contain("deeper"));
            Assert.That(store.Read("a.save", json => json).Value["v"].ToObject<int>(), Is.EqualTo(1));
            Assert.That(storage.ReadBackup("a.save"), Is.Null);
        }

        [Test]
        public void DataAtTheLimit_IsWrittenAndReadBack()
        {
            var store = new SaveFileStore(new MemorySaveStorage(), new SaveSerializer());
            Assert.That(store.Write("a.save", Nested(SaveSerializer.MaxDepth)).Succeeded);
            Assert.That(store.Read("a.save", json => json).Status, Is.EqualTo(ReadStatus.Ok));
        }

        [TestCase(1, "1")]
        [TestCase(1, "{}")]
        [TestCase(2, "{\"a\":[]}")]
        [TestCase(3, "{\"a\":[1]}")]
        [TestCase(4, "[[[1]]]")]
        public void DepthOf_CountsNesting(int expected, string json)
        {
            Assert.That(SaveSerializer.DepthOf(JToken.Parse(json)), Is.EqualTo(expected));
        }

        [Test]
        public void SavingOverADamagedEntry_KeepsTheGoodBackup()
        {
            var storage = new MemorySaveStorage();
            var store = new SaveFileStore(storage, new SaveSerializer());
            store.Write("a.save", new JObject { ["v"] = 1 });
            store.Write("a.save", new JObject { ["v"] = 2 });
            storage.Overwrite("a.save", Encoding.UTF8.GetBytes("damaged"));

            store.Write("a.save", new JObject { ["v"] = 3 });

            Assert.That(store.Read("a.save", json => json).Value["v"].ToObject<int>(), Is.EqualTo(3));
            Assert.That(SaveFileCodec.Plain.Decode(storage.ReadBackup("a.save")).Status, Is.EqualTo(DecodeStatus.Ok));
        }

        [Test]
        public void HalfFinishedSwap_StillListsTheEntry()
        {
            var storage = new FileSaveStorage(_folder.Root);
            storage.Write("a.save", new byte[] { 1 });
            storage.Write("a.save", new byte[] { 2 });
            File.Delete(_folder.FileIn("a.save"));

            Assert.That(storage.List(), Is.EqualTo(new[] { "a.save" }));
            Assert.That(storage.Read("a.save"), Is.Null);
            Assert.That(storage.ReadBackup("a.save"), Is.EqualTo(new byte[] { 1 }));
        }

        [Test]
        public void ReadOnlyEntryAndBackup_DoNotBlockTheNextSave()
        {
            var storage = new FileSaveStorage(_folder.Root);
            storage.Write("a.save", new byte[] { 1 });
            storage.Write("a.save", new byte[] { 2 });
            File.SetAttributes(_folder.FileIn("a.save"), FileAttributes.ReadOnly);
            File.SetAttributes(_folder.FileIn("a.save.bak"), FileAttributes.ReadOnly);

            storage.Write("a.save", new byte[] { 3 });

            Assert.That(storage.Read("a.save"), Is.EqualTo(new byte[] { 3 }));
            Assert.That(storage.ReadBackup("a.save"), Is.EqualTo(new byte[] { 2 }));
        }

        [TestCase("con")]
        [TestCase("nul.save")]
        [TestCase("aux")]
        [TestCase("com1.head")]
        [TestCase("lpt9.save")]
        [TestCase("prn")]
        [TestCase("ends-with-dot.")]
        public void WindowsReservedNames_AreRejected(string name)
        {
            Assert.That(SaveNames.IsValidEntryName(name), Is.False);
        }

        [TestCase("console.save")]
        [TestCase("com10.save")]
        [TestCase("null")]
        [TestCase("a.con")]
        public void NamesThatMerelyResembleThem_AreAllowed(string name)
        {
            Assert.That(SaveNames.IsValidEntryName(name));
        }

        [Test]
        public void SlotNamedLikeADevice_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => SlotId.Named("con"));
            Assert.That(SlotId.TryParse("nul", out _), Is.False);
        }

        [Test]
        public void ReadTransformIds_ReadsTheHeader_AndNeverThrows()
        {
            var file = new SaveFileCodec(new ISaveTransform[] { new GzipTransform(), new AesTransform(new byte[32], "aes-2") }).Encode(new byte[] { 1 });
            Assert.That(SaveFileCodec.ReadTransformIds(file), Is.EqualTo(new[] { "gzip", "aes-2" }));
            Assert.That(SaveFileCodec.ReadTransformIds(null), Is.Empty);
            Assert.That(SaveFileCodec.ReadTransformIds(Encoding.UTF8.GetBytes("junk")), Is.Empty);
        }

        [Test]
        public void RotatedAesKey_OldSavesStillLoad()
        {
            var oldKey = new AesTransform(Enumerable.Repeat((byte)1, 32).ToArray(), "aes-1");
            var newKey = new AesTransform(Enumerable.Repeat((byte)2, 32).ToArray(), "aes-2");
            var oldFile = new SaveFileCodec(new[] { oldKey }).Encode(new byte[] { 7, 8 });

            var current = new SaveFileCodec(new[] { newKey }, new[] { oldKey });
            Assert.That(current.Decode(oldFile).Payload, Is.EqualTo(new byte[] { 7, 8 }));
            Assert.That(SaveFileCodec.ReadTransformIds(current.Encode(new byte[] { 1 })), Is.EqualTo(new[] { "aes-2" }));
        }

        [Test]
        public void SameJson_WritesTheSameBytes()
        {
            var json = JToken.Parse("{\"a\":[1,2.5,\"x\"],\"b\":{\"c\":null}}");
            var serializer = new SaveSerializer();
            Assert.That(SaveFileCodec.Plain.Encode(serializer.ToBytes(json, true)), Is.EqualTo(SaveFileCodec.Plain.Encode(serializer.ToBytes(json.DeepClone(), true))));
        }

        [Test]
        public async Task SlotHeader_RecordsItsFormat()
        {
            var storage = new MemorySaveStorage();
            var slots = new SaveSlots<object>(new SaveService(), storage, null, new SlotOptions { Worker = new ImmediateSaveWorker() });
            await slots.SaveAsync(SlotId.Manual(1));
            Assert.That((await slots.ListAsync()).Single().Header.Format, Is.EqualTo(SlotHeader<object>.CurrentFormat));
            Assert.That(Encoding.UTF8.GetString(storage.Read("manual-01.head")), Does.Contain("\"format\": 1"));
        }

        [Test]
        public async Task SaveCopiedToAnotherSlot_GetsAHeaderForItsNewSlotOnLoad()
        {
            var storage = new MemorySaveStorage();
            var slots = new SaveSlots<object>(new SaveService(), storage, null, new SlotOptions { Worker = new ImmediateSaveWorker() });
            await slots.SaveAsync(SlotId.Manual(1));
            storage.Overwrite("manual-02.save", storage.Read("manual-01.save"));

            Assert.That((await slots.LoadAsync(SlotId.Manual(2))).Succeeded);
            var summary = (await slots.ListAsync()).Single(item => item.Slot.Name == "manual-02");
            Assert.That(summary.State, Is.EqualTo(SlotHeaderState.Ok));
        }

        [Test]
        public void EveryOperation_RejectsAMissingSlot()
        {
            var slots = new SaveSlots<object>(new SaveService(), new MemorySaveStorage(), null, new SlotOptions { Worker = new ImmediateSaveWorker() });
            Assert.Throws<ArgumentException>(() => slots.LoadAsync(default));
            Assert.Throws<ArgumentException>(() => slots.DeleteAsync(default));
            Assert.Throws<ArgumentException>(() => slots.RepairHeaderAsync(default));
        }

        [Test]
        public async Task ListingStorageThatThrows_ListsNothing()
        {
            var slots = new SaveSlots<object>(new SaveService(), new BrokenListStorage(), null, new SlotOptions { Worker = new ImmediateSaveWorker() });
            Assert.That(await slots.ListAsync(), Is.Empty);
        }

        [Test]
        public async Task RepairHeader_WithNoSave_ReturnsFalse()
        {
            var slots = new SaveSlots<object>(new SaveService(), new MemorySaveStorage(), null, new SlotOptions { Worker = new ImmediateSaveWorker() });
            Assert.That(await slots.RepairHeaderAsync(SlotId.Manual(1)), Is.False);
        }

        [Test]
        public async Task DamagedSettings_LoadTheBackup()
        {
            var storage = new MemorySaveStorage();
            var service = new SaveService();
            var volume = new CounterSection("audio") { Value = 1 };
            service.Register(volume);
            var store = new SettingsStore(service, storage, new SaveFileOptions { Worker = new ImmediateSaveWorker() });
            await store.SaveAsync();
            volume.Value = 2;
            await store.SaveAsync();
            storage.Overwrite(SettingsStore.Entry, Encoding.UTF8.GetBytes("junk"));

            var result = await store.LoadAsync();
            Assert.That(result.Succeeded);
            Assert.That(result.UsedBackup);
            Assert.That(volume.Value, Is.EqualTo(1));
        }

        [Test]
        public async Task SettingsWriteFailure_IsReported()
        {
            var storage = new SpyStorage { FailWrite = _ => true };
            var store = new SettingsStore(new SaveService(), storage, new SaveFileOptions { Worker = new ImmediateSaveWorker() });
            Assert.That((await store.SaveAsync()).Status, Is.EqualTo(SaveStatus.WriteFailed));
        }

        private sealed class BrokenListStorage : ISaveStorage
        {
            private readonly MemorySaveStorage _inner = new MemorySaveStorage();
            public byte[] Read(string name) => _inner.Read(name);
            public byte[] ReadBackup(string name) => _inner.ReadBackup(name);
            public void Write(string name, byte[] data) => _inner.Write(name, data);
            public void Delete(string name) => _inner.Delete(name);
            public IReadOnlyList<string> List() => throw new IOException("listing broke");
        }
    }
}
