using System.IO;
using System.Text;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /**
     * <summary>What every <see cref="ISaveStorage" /> must do, run for each backend by subclassing: nothing reads as null;
     * a write keeps the previous bytes as the backup and clears a pending write; a repair replaces the entry and keeps
     * the backup; a whole pending write is moved into a missing entry's place and nothing else; a delete removes
     * everything and repeats harmlessly; the list names each entry once, one left with only a backup or a pending write
     * included; bytes handed back are copies. A console's or cloud's storage subclasses this to prove it fits.</summary>
     */
    public abstract class SaveStorageContractTests
    {
        protected ISaveStorage Storage { get; private set; }

        /** <summary>A new, empty storage.</summary> */
        protected abstract ISaveStorage Make();

        /** <summary>Leaves the entry gone and its last bytes as the backup, as a crash between a write's two moves would.</summary> */
        protected abstract void LoseEntry(string name);

        /** <summary>Leaves <paramref name="data" /> as a pending write beside whatever the entry holds.</summary> */
        protected abstract void LeavePending(string name, byte[] data);

        [SetUp]
        public void MakeStorage() => Storage = Make();

        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        private static string Text(byte[] bytes) => bytes == null ? null : Encoding.UTF8.GetString(bytes);

        [Test]
        public void Nothing_ReadsAsNull_AndListsNothing()
        {
            Assert.That((Storage.Read("a.save"), Storage.ReadBackup("a.save"), Storage.ReadPending("a.save")), Is.EqualTo(((byte[])null, (byte[])null, (byte[])null)));
            Assert.That(Storage.List(), Is.Empty);
        }

        [Test]
        public void AWrite_KeepsThePreviousBytesAsTheBackup_AndClearsAPendingWrite()
        {
            Storage.Write("a.save", Bytes("one"));
            Assert.That(Storage.ReadBackup("a.save"), Is.Null);
            Storage.Write("a.save", Bytes("two"));
            LeavePending("a.save", Bytes("cut off"));
            Storage.Write("a.save", Bytes("three"));
            Assert.That((Text(Storage.Read("a.save")), Text(Storage.ReadBackup("a.save")), Storage.ReadPending("a.save")), Is.EqualTo(("three", "two", (byte[])null)));
        }

        [Test]
        public void ARepair_ReplacesTheEntry_AndKeepsTheBackup_OrWritesANewEntryWithNone()
        {
            Storage.Write("a.save", Bytes("good"));
            Storage.Write("a.save", Bytes("damaged"));
            Storage.Repair("a.save", Bytes("good"));
            Assert.That((Text(Storage.Read("a.save")), Text(Storage.ReadBackup("a.save"))), Is.EqualTo(("good", "good")));
            Storage.Repair("b.save", Bytes("new"));
            Assert.That((Text(Storage.Read("b.save")), Storage.ReadBackup("b.save")), Is.EqualTo(("new", (byte[])null)));
        }

        [Test]
        public void CommitPending_MovesAWholePendingWriteIntoTheMissingEntrysPlace_KeepingTheBackup()
        {
            Storage.Write("a.save", Bytes("one"));
            Storage.Write("a.save", Bytes("two"));
            LoseEntry("a.save");
            LeavePending("a.save", Bytes("three"));
            Storage.CommitPending("a.save");
            Assert.That((Text(Storage.Read("a.save")), Text(Storage.ReadBackup("a.save")), Storage.ReadPending("a.save")), Is.EqualTo(("three", "two", (byte[])null)));
        }

        [Test]
        public void CommitPending_ChangesNothing_WhenTheEntryIsThere_OrNothingIsPending()
        {
            Storage.CommitPending("a.save");
            Assert.That(Storage.Read("a.save"), Is.Null);
            Storage.Write("a.save", Bytes("current"));
            LeavePending("a.save", Bytes("stale"));
            Storage.CommitPending("a.save");
            Assert.That((Text(Storage.Read("a.save")), Text(Storage.ReadPending("a.save"))), Is.EqualTo(("current", "stale")), "a pending write never replaces an entry that is there");
        }

        [Test]
        public void ADelete_RemovesTheEntryBackupAndPendingWrite_AndRepeatsHarmlessly()
        {
            Storage.Write("a.save", Bytes("one"));
            Storage.Write("a.save", Bytes("two"));
            LeavePending("a.save", Bytes("cut off"));
            Storage.Delete("a.save");
            Assert.That((Storage.Read("a.save"), Storage.ReadBackup("a.save"), Storage.ReadPending("a.save")), Is.EqualTo(((byte[])null, (byte[])null, (byte[])null)));
            Assert.That(Storage.List(), Is.Empty);
            Assert.DoesNotThrow(() => Storage.Delete("a.save"));
        }

        [Test]
        public void TheList_NamesEachEntryOnce_InOrder_OneWithOnlyABackupOrAPendingWriteIncluded()
        {
            Storage.Write("c.save", Bytes("1"));
            Storage.Write("a.save", Bytes("1"));
            Storage.Write("a.save", Bytes("2"));
            Storage.Write("b.save", Bytes("1"));
            Storage.Write("b.save", Bytes("2"));
            LoseEntry("b.save");
            LeavePending("d.save", Bytes("cut off"));
            Assert.That(Storage.List(), Is.EqualTo(new[] { "a.save", "b.save", "c.save", "d.save" }));
        }

        [Test]
        public void BytesHandedBack_AreCopies()
        {
            var written = Bytes("one");
            Storage.Write("a.save", written);
            written[0] = (byte)'X';
            var read = Storage.Read("a.save");
            read[0] = (byte)'Y';
            Assert.That(Text(Storage.Read("a.save")), Is.EqualTo("one"));
        }
    }

    /** <summary>The storage contract on files.</summary> */
    public sealed class FileSaveStorageContractTests : SaveStorageContractTests
    {
        private TestFolder _folder;

        protected override ISaveStorage Make()
        {
            _folder = new TestFolder();
            return new FileSaveStorage(_folder.Root);
        }

        protected override void LoseEntry(string name)
        {
            var path = _folder.FileIn(name);
            File.Delete(path + SaveNames.BackupSuffix);
            File.Move(path, path + SaveNames.BackupSuffix);
        }

        protected override void LeavePending(string name, byte[] data)
        {
            Directory.CreateDirectory(_folder.Root);
            File.WriteAllBytes(_folder.FileIn(name + SaveNames.TempSuffix), data);
        }

        [TearDown]
        public void RemoveFolder() => _folder.Dispose();
    }

    /** <summary>The storage contract in memory.</summary> */
    public sealed class MemorySaveStorageContractTests : SaveStorageContractTests
    {
        private MemorySaveStorage _memory;

        protected override ISaveStorage Make() => _memory = new MemorySaveStorage();

        protected override void LoseEntry(string name) => _memory.LoseEntry(name);

        protected override void LeavePending(string name, byte[] data) => _memory.LeavePending(name, data);
    }
}
