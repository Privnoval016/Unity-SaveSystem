using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /**
     * <summary>Reading and writing through damage: a whole write a crash left pending is read and moved (not rewritten)
     * into place, in memory and on real files, while a partial one, or a stale one beside an entry that is there, is
     * not; a file that is only a cut-off write is damaged, not missing; empty files are damage; a damaged entry's good
     * backup survives a write that fails after the repair; a backup counts as good only when its JSON parses; a file
     * that unpacks too large is damage and its backup is read instead.</summary>
     */
    public sealed class ReadRecoveryTests
    {
        private MemorySaveStorage _storage;
        private SaveFileStore _store;

        [SetUp]
        public void SetUp()
        {
            _storage = new MemorySaveStorage();
            _store = new SaveFileStore(_storage, new SaveSerializer());
        }

        private static JObject Value(int v) => new JObject { ["v"] = v };

        private static byte[] Encoded(int v) => SaveFileCodec.Plain.Encode(new SaveSerializer().ToBytes(Value(v), true));

        private static ReadResult<int> ReadFrom(SaveFileStore store) => store.Read("a.save", json => json["v"].Value<int>());

        [Test]
        public void AWholeWriteACrashLeftPending_IsRead_AndMovedIntoPlace_TheBackupKept()
        {
            _store.Write("a.save", Value(1));
            _store.Write("a.save", Value(2));
            _storage.CrashMidWrite("a.save", Encoded(3));

            var read = ReadFrom(_store);
            Assert.That((read.Status, read.Value, read.Source), Is.EqualTo((ReadStatus.Ok, 3, ReadSource.Pending)));
            Assert.That(read.Problem, Does.Contain("missing"));
            Assert.That(_storage.Read("a.save"), Is.EqualTo(Encoded(3)));
            Assert.That(_storage.ReadBackup("a.save"), Is.EqualTo(Encoded(2)));
            Assert.That(_storage.ReadPending("a.save"), Is.Null);
            Assert.That(ReadFrom(_store).Source, Is.EqualTo(ReadSource.Entry));
        }

        [Test]
        public void OnRealFiles_AWholeWriteACrashLeftBetweenTheTwoMoves_IsRecovered()
        {
            using (var folder = new TestFolder())
            {
                var files = new FileSaveStorage(folder.Root);
                var store = new SaveFileStore(files, new SaveSerializer());
                store.Write("a.save", Value(1));
                store.Write("a.save", Value(2));
                File.Delete(folder.FileIn("a.save.bak"));
                File.Move(folder.FileIn("a.save"), folder.FileIn("a.save.bak"));
                File.WriteAllBytes(folder.FileIn("a.save.tmp"), Encoded(3));

                var read = ReadFrom(store);
                Assert.That((read.Value, read.Source), Is.EqualTo((3, ReadSource.Pending)));
                Assert.That((File.Exists(folder.FileIn("a.save")), File.Exists(folder.FileIn("a.save.tmp"))), Is.EqualTo((true, false)));
                Assert.That(files.ReadBackup("a.save"), Is.EqualTo(Encoded(2)));
            }
        }

        [Test]
        public void APartialPendingWrite_IsIgnored_AndTheBackupIsRead()
        {
            _store.Write("a.save", Value(1));
            var whole = Encoded(2);
            var partial = new byte[whole.Length - 5];
            System.Array.Copy(whole, partial, partial.Length);
            _storage.CrashMidWrite("a.save", partial);
            var read = ReadFrom(_store);
            Assert.That((read.Status, read.Value, read.Source), Is.EqualTo((ReadStatus.Ok, 1, ReadSource.Backup)));
        }

        [Test]
        public void AStalePendingWrite_BesideAnEntryThatIsThere_IsNeverRead_AndTheNextWriteClearsIt()
        {
            _store.Write("a.save", Value(2));
            _storage.LeavePending("a.save", Encoded(1));
            Assert.That((ReadFrom(_store).Value, ReadFrom(_store).Source), Is.EqualTo((2, ReadSource.Entry)));
            _store.Write("a.save", Value(3));
            Assert.That(_storage.ReadPending("a.save"), Is.Null);
        }

        [Test]
        public void AFileThatIsOnlyACutOffWrite_IsDamaged_NotMissing()
        {
            _storage.LeavePending("a.save", new byte[] { 1, 2, 3 });
            Assert.That(ReadFrom(_store).Status, Is.EqualTo(ReadStatus.Damaged));
        }

        [Test]
        public void APendingWriteThatCantBeRead_IsUnreadable_NotPassedOverForTheBackup()
        {
            var spy = new SpyStorage();
            var store = new SaveFileStore(spy, new SaveSerializer());
            store.Write("a.save", Value(1));
            store.Write("a.save", Value(2));
            spy.Inner.CrashMidWrite("a.save", Encoded(3));
            spy.FailRead = name => true;
            spy.FailReadPending = true;
            Assert.That(store.Read("a.save", json => json).Status, Is.EqualTo(ReadStatus.Unreadable));
        }

        [Test]
        public void EmptyFiles_AreDamage()
        {
            _store.Write("a.save", Value(1));
            _storage.Write("a.save", new byte[0]);
            Assert.That(ReadFrom(_store).Value, Is.EqualTo(1), "an empty entry: its backup is read");
            _storage.Overwrite("a.save", new byte[0]);
            _storage.Write("a.save", new byte[0]);
            Assert.That(ReadFrom(_store).Status, Is.EqualTo(ReadStatus.Damaged));
        }

        [Test]
        public void ADamagedEntrysGoodBackup_SurvivesAWriteThatFailsAfterTheRepair()
        {
            var spy = new SpyStorage();
            var store = new SaveFileStore(spy, new SaveSerializer());
            store.Write("a.save", Value(1));
            store.Write("a.save", Value(2));
            spy.Inner.Overwrite("a.save", Encoding.UTF8.GetBytes("garbage"));

            spy.FailWrite = _ => true;
            Assert.That(store.Write("a.save", Value(3)).Status, Is.EqualTo(SaveStatus.WriteFailed));
            Assert.That(spy.Inner.Read("a.save"), Is.EqualTo(Encoded(1)), "the good backup was put back in place, not written through (which would make the damage the backup)");
            Assert.That(spy.Inner.ReadBackup("a.save"), Is.EqualTo(Encoded(1)));

            spy.FailWrite = _ => false;
            Assert.That(store.Write("a.save", Value(4)).Succeeded);
            Assert.That(ReadFrom(store).Value, Is.EqualTo(4));
            Assert.That(spy.Inner.ReadBackup("a.save"), Is.EqualTo(Encoded(1)));
        }

        [Test]
        public void ABackupWhoseJsonDoesNotParse_IsNotPutBackOverTheEntry()
        {
            var badJson = SaveFileCodec.Plain.Encode(Encoding.UTF8.GetBytes("{ not json"));
            _storage.Write("a.save", badJson);
            _storage.Write("a.save", Encoding.UTF8.GetBytes("damaged entry"));
            Assert.That(SaveFileCodec.Verify(_storage.ReadBackup("a.save")), Is.EqualTo(DecodeStatus.Ok), "the backup's frame is fine; only its JSON is not");

            Assert.That(_store.Write("a.save", Value(2)).Succeeded);
            Assert.That(Encoding.UTF8.GetString(_storage.ReadBackup("a.save")), Is.EqualTo("damaged entry"), "the unparseable backup was not promoted");
            Assert.That(ReadFrom(_store).Value, Is.EqualTo(2));
        }

        [Test]
        public void AFileThatUnpacksTooLarge_IsDamage_AndItsBackupIsRead()
        {
            var small = new GzipTransform(1000);
            Assert.Throws<InvalidDataException>(() => small.Revert(new GzipTransform().Apply(new byte[5000])));
            Assert.That(small.Revert(new GzipTransform().Apply(new byte[1000])), Has.Length.EqualTo(1000));

            var store = new SaveFileStore(_storage, new SaveSerializer(), new SaveFileCodec(new ISaveTransform[] { small }));
            store.Write("a.save", Value(1));
            var bomb = new SaveFileCodec(new ISaveTransform[] { new GzipTransform() }).Encode(new SaveSerializer().ToBytes(new JObject { ["v"] = new string('x', 5000) }, false));
            _storage.Write("a.save", bomb);
            var read = ReadFrom(store);
            Assert.That((read.Value, read.Source), Is.EqualTo((1, ReadSource.Backup)));
        }
    }
}
