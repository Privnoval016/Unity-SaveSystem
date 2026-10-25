using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>Files on disk: atomic writes, backups, leftovers, names and folders.</summary> */
    public sealed class FileStorageTests
    {
        private TestFolder _folder;

        [SetUp]
        public void SetUp() => _folder = new TestFolder();

        [TearDown]
        public void TearDown() => _folder.Dispose();

        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        private static string Text(byte[] bytes) => bytes == null ? null : Encoding.UTF8.GetString(bytes);

        [Test]
        public void Write_CreatesTheFolder_AndReadsBack()
        {
            var storage = new FileSaveStorage(Path.Combine(_folder.Root, "deeper", "still"));
            storage.Write("a.save", Bytes("one"));
            Assert.That(Text(storage.Read("a.save")), Is.EqualTo("one"));
        }

        [Test]
        public void SecondWrite_KeepsTheFirstAsBackup()
        {
            var storage = new FileSaveStorage(_folder.Root);
            storage.Write("a.save", Bytes("one"));
            Assert.That(storage.ReadBackup("a.save"), Is.Null);
            storage.Write("a.save", Bytes("two"));
            storage.Write("a.save", Bytes("three"));
            Assert.That(Text(storage.Read("a.save")), Is.EqualTo("three"));
            Assert.That(Text(storage.ReadBackup("a.save")), Is.EqualTo("two"));
        }

        [Test]
        public void Missing_ReadsNull()
        {
            var storage = new FileSaveStorage(_folder.Root);
            Assert.That(storage.Read("none.save"), Is.Null);
            Assert.That(storage.ReadBackup("none.save"), Is.Null);
            Assert.That(storage.List(), Is.Empty);
        }

        [Test]
        public void LeftoverTempFile_IsIgnored_AndClearedByTheNextWrite()
        {
            var storage = new FileSaveStorage(_folder.Root);
            storage.Write("a.save", Bytes("good"));
            File.WriteAllText(_folder.FileIn("a.save.tmp"), "half writ");

            Assert.That(Text(storage.Read("a.save")), Is.EqualTo("good"));
            Assert.That(storage.List(), Is.EqualTo(new[] { "a.save" }));

            storage.Write("a.save", Bytes("next"));
            Assert.That(File.Exists(_folder.FileIn("a.save.tmp")), Is.False);
            Assert.That(Text(storage.Read("a.save")), Is.EqualTo("next"));
            Assert.That(Text(storage.ReadBackup("a.save")), Is.EqualTo("good"));
        }

        [Test]
        public void CrashMidSwap_LeavesTheBackupReadable()
        {
            var storage = new FileSaveStorage(_folder.Root);
            var store = new SaveFileStore(storage, new SaveSerializer());
            store.Write("a.save", new JObject { ["v"] = 1 });
            store.Write("a.save", new JObject { ["v"] = 2 });
            File.Delete(_folder.FileIn("a.save.bak"));
            File.Move(_folder.FileIn("a.save"), _folder.FileIn("a.save.bak"));
            File.WriteAllText(_folder.FileIn("a.save.tmp"), "the new save, never swapped in");

            var read = store.Read("a.save", json => json);
            Assert.That(read.Status, Is.EqualTo(ReadStatus.Ok));
            Assert.That(read.UsedBackup);
            Assert.That(read.Problem, Does.Contain("missing"));
            Assert.That(read.Value["v"].ToObject<int>(), Is.EqualTo(2));

            store.Write("a.save", new JObject { ["v"] = 3 });
            Assert.That(store.Read("a.save", json => json).Value["v"].ToObject<int>(), Is.EqualTo(3));
            Assert.That(File.Exists(_folder.FileIn("a.save.tmp")), Is.False);
        }

        [Test]
        public void List_NamesEachEntryOnce_AnEntryWithOnlyAPendingWriteIncluded_AndSkipsForeignFiles()
        {
            var storage = new FileSaveStorage(_folder.Root);
            storage.Write("b.save", Bytes("1"));
            storage.Write("a.save", Bytes("1"));
            storage.Write("a.save", Bytes("2"));
            File.WriteAllText(_folder.FileIn("c.save.tmp"), "x");
            File.WriteAllText(_folder.FileIn(".DS_Store"), "x");
            File.WriteAllText(_folder.FileIn("README.TXT"), "x");
            Directory.CreateDirectory(_folder.FileIn("folder.save"));

            File.WriteAllText(_folder.FileIn("a.save.tmp"), "x");

            Assert.That(storage.List(), Is.EqualTo(new[] { "a.save", "b.save", "c.save" }), "c has only a pending write a crash left on a first save; reading decides whether it is whole");
        }

        [Test]
        public void Delete_RemovesEntryBackupAndTemp()
        {
            var storage = new FileSaveStorage(_folder.Root);
            storage.Write("a.save", Bytes("1"));
            storage.Write("a.save", Bytes("2"));
            File.WriteAllText(_folder.FileIn("a.save.tmp"), "x");

            storage.Delete("a.save");
            storage.Delete("never-existed.save");

            Assert.That(Directory.GetFiles(_folder.Root), Is.Empty);
        }

        [TestCase("save dir with spaces")]
        [TestCase("Ünïcødé Сохранения ψ")]
        public void UnusualFolderNames_Work(string name)
        {
            var storage = new FileSaveStorage(Path.Combine(_folder.Root, name));
            storage.Write("a.save", Bytes("ok"));
            Assert.That(Text(storage.Read("a.save")), Is.EqualTo("ok"));
            Assert.That(storage.List(), Is.EqualTo(new[] { "a.save" }));
        }

        [TestCase("../escape.save")]
        [TestCase("sub/dir.save")]
        [TestCase("sub\\dir.save")]
        [TestCase("a.save.bak")]
        [TestCase("a.save.tmp")]
        [TestCase(".hidden")]
        [TestCase("Upper.save")]
        [TestCase("")]
        [TestCase(null)]
        public void InvalidNames_Throw(string name)
        {
            var storage = new FileSaveStorage(_folder.Root);
            Assert.Throws<ArgumentException>(() => storage.Write(name, Bytes("x")));
            Assert.Throws<ArgumentException>(() => storage.Read(name));
            Assert.Throws<ArgumentException>(() => new MemorySaveStorage().Write(name, Bytes("x")));
        }

        [Test]
        public void RootThatIsAFile_FailsTheWrite_WithoutThrowingFromTheStore()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_folder.Root));
            File.WriteAllText(_folder.Root, "I am a file");
            var store = new SaveFileStore(new FileSaveStorage(_folder.Root), new SaveSerializer());

            var result = store.Write("a.save", new JObject());
            Assert.That(result.Status, Is.EqualTo(SaveStatus.WriteFailed));
            Assert.That(result.Problem, Does.Contain("a.save"));
            Assert.That(store.Read("a.save", json => json).Status, Is.Not.EqualTo(ReadStatus.Ok));
            File.Delete(_folder.Root);
        }

        [Test]
        public void ReadOnlyExistingFile_IsStillReadable()
        {
            var storage = new FileSaveStorage(_folder.Root);
            storage.Write("a.save", Bytes("1"));
            File.SetAttributes(_folder.FileIn("a.save"), FileAttributes.ReadOnly);
            Assert.That(Text(storage.Read("a.save")), Is.EqualTo("1"));
        }

        [Test]
        public void MemoryStorage_BehavesLikeFiles()
        {
            var storage = new MemorySaveStorage();
            storage.Write("a.save", Bytes("1"));
            storage.Write("a.save", Bytes("2"));
            Assert.That(Text(storage.Read("a.save")), Is.EqualTo("2"));
            Assert.That(Text(storage.ReadBackup("a.save")), Is.EqualTo("1"));
            storage.Delete("a.save");
            Assert.That(storage.Read("a.save"), Is.Null);
            Assert.That(storage.ReadBackup("a.save"), Is.Null);
            Assert.That(storage.List(), Is.Empty);
        }

        [Test]
        public void MemoryStorage_ReturnsCopies()
        {
            var storage = new MemorySaveStorage();
            var data = Bytes("abc");
            storage.Write("a.save", data);
            data[0] = (byte)'z';
            storage.Read("a.save")[1] = (byte)'z';
            Assert.That(Text(storage.Read("a.save")), Is.EqualTo("abc"));
        }
    }
}
