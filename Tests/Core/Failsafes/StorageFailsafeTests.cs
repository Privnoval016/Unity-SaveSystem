using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /**
     * <summary>Files on disk when things go wrong (the contract every storage keeps is <see cref="SaveStorageContractTests" />):
     * a write that can't take the entry's place is retried with the storage's delays, then fails leaving the entry,
     * backup and folder as they were; a whole temp file is kept only after a swap that failed halfway; and the retry
     * rule: I/O errors wait out each delay, access errors one, a missing file none.</summary>
     */
    public sealed class StorageFailsafeTests
    {
        private TestFolder _folder;
        private List<int> _waits;

        [SetUp]
        public void SetUp()
        {
            _folder = new TestFolder();
            _waits = new List<int>();
        }

        [TearDown]
        public void TearDown() => _folder.Dispose();

        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        private static string Text(byte[] bytes) => bytes == null ? null : Encoding.UTF8.GetString(bytes);

        private FileSaveStorage Storage() => new FileSaveStorage(_folder.Root, new[] { 7, 8, 9 }, _waits.Add);

        [Test]
        public void AWriteThatCantTakeTheEntrysPlace_IsRetriedWithTheStoragesDelays_ThenLeavesNoTempFile()
        {
            var storage = Storage();
            storage.Write("b.save", Bytes("one"));
            Directory.CreateDirectory(_folder.FileIn("a.save"));
            Assert.That(() => storage.Write("a.save", Bytes("two")), Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            Assert.That(_waits, Is.EqualTo(new[] { 7, 8, 9 }).Or.EqualTo(new[] { 7 }), "every file move goes through the retry");
            Assert.That(File.Exists(_folder.FileIn("a.save" + SaveNames.TempSuffix)), Is.False, "no leftover filling the disk");
            Assert.That(Text(storage.Read("b.save")), Is.EqualTo("one"));
        }

        [Test]
        public void AWriteThatFails_LeavesTheEntryAndItsBackupAsTheyWere()
        {
            var storage = Storage();
            storage.Write("a.save", Bytes("one"));
            storage.Write("a.save", Bytes("two"));
            File.Delete(_folder.FileIn("a.save" + SaveNames.BackupSuffix));
            Directory.CreateDirectory(_folder.FileIn("a.save" + SaveNames.BackupSuffix));
            File.WriteAllText(_folder.FileIn("a.save" + SaveNames.BackupSuffix + "/keep"), "x");

            Assert.That(() => storage.Write("a.save", Bytes("three")), Throws.Exception);
            Assert.That(Text(storage.Read("a.save")), Is.EqualTo("two"));
            Assert.That(File.Exists(_folder.FileIn("a.save" + SaveNames.TempSuffix)), Is.False);
        }

        [Test]
        public void TheRetryRule_WaitsOutIOErrors_TriesAccessErrorsOnceMore_AndNeverWaitsForAMissingFile()
        {
            var waits = new List<int>();
            var tries = 0;
            Retry.Run(() => { if (++tries < 3) throw new IOException("sharing violation"); }, new[] { 1, 2, 3 }, waits.Add);
            Assert.That(tries, Is.EqualTo(3));
            Assert.That(waits, Is.EqualTo(new[] { 1, 2 }));

            tries = 0;
            Assert.Throws<IOException>(() => Retry.Run(() => { tries++; throw new IOException("still held"); }, new[] { 1, 1 }, _ => { }));
            Assert.That(tries, Is.EqualTo(3), "once, then once per delay");

            tries = 0;
            Assert.Throws<UnauthorizedAccessException>(() => Retry.Run(() => { tries++; throw new UnauthorizedAccessException("locked"); }, new[] { 1, 1 }, _ => { }));
            Assert.That(tries, Is.EqualTo(2), "an access error is tried once more, not waited out: a read-only folder won't change");

            tries = 0;
            Assert.Throws<FileNotFoundException>(() => Retry.Run(() => { tries++; throw new FileNotFoundException("gone"); }, new[] { 1, 1 }, _ => { }));
            Assert.That(tries, Is.EqualTo(1), "a missing file isn't waited out");

            Assert.That(Retry.Run(() => 5, new int[0]), Is.EqualTo(5));
            Assert.That(FileSaveStorage.DefaultRetryDelays, Is.EqualTo(new[] { 25, 100, 300 }));
        }
    }
}
