using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>Reading and writing entries through the codec, falling back to backups.</summary> */
    public sealed class FileStoreTests
    {
        private MemorySaveStorage _storage;
        private SaveFileStore _store;

        [SetUp]
        public void SetUp()
        {
            _storage = new MemorySaveStorage();
            _store = new SaveFileStore(_storage, new SaveSerializer());
        }

        private ReadResult<JToken> Read() => _store.Read("a.save", json => json);

        [Test]
        public void Missing_IsMissing()
        {
            Assert.That(Read().Status, Is.EqualTo(ReadStatus.Missing));
        }

        [Test]
        public void Good_IsReadWithoutTheBackup()
        {
            _store.Write("a.save", new JObject { ["v"] = 1 });
            var read = Read();
            Assert.That(read.Status, Is.EqualTo(ReadStatus.Ok));
            Assert.That(read.UsedBackup, Is.False);
            Assert.That(read.Problem, Is.Null);
        }

        [Test]
        public void DamagedEntry_FallsBackToTheBackup_AndSaysWhy()
        {
            _store.Write("a.save", new JObject { ["v"] = 1 });
            _store.Write("a.save", new JObject { ["v"] = 2 });
            var damaged = _storage.Read("a.save");
            damaged[damaged.Length - 2] ^= 0x20;
            _storage.Overwrite("a.save", damaged);

            var read = Read();
            Assert.That(read.Status, Is.EqualTo(ReadStatus.Ok));
            Assert.That(read.UsedBackup);
            Assert.That(read.Problem, Does.Contain("checksum"));
            Assert.That(read.Value["v"].ToObject<int>(), Is.EqualTo(1));
        }

        [Test]
        public void DamagedEntry_WithNoBackup_IsDamaged()
        {
            _storage.Overwrite("a.save", Encoding.UTF8.GetBytes("garbage"));
            var read = Read();
            Assert.That(read.Status, Is.EqualTo(ReadStatus.Damaged));
            Assert.That(read.Problem, Does.Contain("no backup"));
        }

        [Test]
        public void EntryAndBackupBothDamaged_IsDamaged()
        {
            _storage.Write("a.save", Encoding.UTF8.GetBytes("junk backup"));
            _storage.Write("a.save", new byte[0]);

            var read = Read();
            Assert.That(read.Status, Is.EqualTo(ReadStatus.Damaged));
            Assert.That(read.Problem, Does.Contain("the entry"));
            Assert.That(read.Problem, Does.Contain("the backup"));
        }

        [Test]
        public void ValidFrameButInvalidJson_FallsBack()
        {
            _store.Write("a.save", new JObject { ["v"] = 1 });
            _store.Write("a.save", new JObject { ["v"] = 2 });
            _storage.Overwrite("a.save", SaveFileCodec.Plain.Encode(Encoding.UTF8.GetBytes("{\"v\":")));
            Assert.That(Read().UsedBackup);
        }

        [Test]
        public void ParseRejecting_FallsBack()
        {
            _store.Write("a.save", new JObject { ["v"] = 1 });
            _store.Write("a.save", new JObject { ["v"] = 2 });
            var read = _store.Read("a.save", json => json["v"].ToObject<int>() == 2 ? throw new FormatException("rejected") : json);
            Assert.That(read.UsedBackup);
            Assert.That(read.Problem, Does.Contain("rejected"));
        }

        [Test]
        public void UnreadableStorage_IsDamaged_NotThrown()
        {
            var store = new SaveFileStore(new ThrowingStorage(), new SaveSerializer());
            var read = store.Read("a.save", json => json);
            Assert.That(read.Status, Is.EqualTo(ReadStatus.Damaged));
            Assert.That(read.Problem, Does.Contain("denied"));
            Assert.That(store.Delete("a.save"), Does.Contain("denied"));
        }

        [Test]
        public void EncryptedStore_WithTheWrongKey_IsDamaged()
        {
            var right = new SaveFileStore(_storage, new SaveSerializer(), new SaveFileCodec(new ISaveTransform[] { new GzipTransform(), new AesTransform(new byte[32]) }));
            right.Write("a.save", new JObject { ["v"] = 1 });
            var key = new byte[32];
            key[5] = 1;
            var wrong = new SaveFileStore(_storage, new SaveSerializer(), new SaveFileCodec(new ISaveTransform[] { new GzipTransform(), new AesTransform(key) }));

            Assert.That(wrong.Read("a.save", json => json).Status, Is.EqualTo(ReadStatus.Damaged));
            Assert.That(right.Read("a.save", json => json).Status, Is.EqualTo(ReadStatus.Ok));
        }

        [Test]
        public void PlainStore_WritesIndentedJson()
        {
            _store.Write("a.save", new JObject { ["v"] = 1 });
            Assert.That(Encoding.UTF8.GetString(_storage.Read("a.save")), Does.Contain("\n  \"v\": 1"));
        }

        private sealed class ThrowingStorage : ISaveStorage
        {
            public byte[] Read(string name) => throw new UnauthorizedAccessException("denied");
            public byte[] ReadBackup(string name) => throw new UnauthorizedAccessException("denied");
            public void Write(string name, byte[] data) => throw new UnauthorizedAccessException("denied");
            public void Delete(string name) => throw new UnauthorizedAccessException("denied");
            public IReadOnlyList<string> List() => throw new UnauthorizedAccessException("denied");
        }
    }
}
