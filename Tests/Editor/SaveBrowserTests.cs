using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SaveSystem.EditorTools;

namespace SaveSystem.Tests
{
    /** <summary>The save browser: listing, reading, editing and deleting saves.</summary> */
    public sealed class SaveBrowserTests
    {
        private MemorySaveStorage _storage;
        private SaveService _service;
        private CounterSection _counter;

        [SetUp]
        public void SetUp()
        {
            _storage = new MemorySaveStorage();
            _service = new SaveService();
            _counter = new CounterSection("counter") { Value = 4 };
            _service.Register(_counter);
        }

        private SaveSlots<object> Slots(SaveFileCodec codec = null) =>
            new SaveSlots<object>(_service, _storage, null, new SlotOptions { Worker = new ImmediateSaveWorker(), Codec = codec ?? SaveFileCodec.Plain });

        [Test]
        public async Task Lists_SavesAndSettings_ButNotHeaders()
        {
            await Slots().SaveAsync(SlotId.Manual(1));
            await new SettingsStore(new SaveService(), _storage, new SaveFileOptions { Worker = new ImmediateSaveWorker() }).SaveAsync();
            Assert.That(new SaveBrowserModel(_storage).Entries(), Is.EqualTo(new[] { "manual-01.save", "settings.cfg" }));
        }

        [Test]
        public async Task Opens_ASave_AndShowsItsSections()
        {
            await Slots().SaveAsync(SlotId.Manual(1));
            var save = new SaveBrowserModel(_storage).Open("manual-01.save");
            Assert.That(save.Problem, Is.Null);
            Assert.That(save.Document.Meta["slot"].ToString(), Is.EqualTo("manual-01"));
            Assert.That(SaveBrowserModel.SectionText(save.Document, "counter"), Does.Contain("\"Value\": 4"));
            Assert.That(SaveBrowserModel.SectionText(save.Document, "nothing"), Is.Null);
        }

        [Test]
        public async Task EditingASection_ReSignsTheFile_AndTheGameLoadsIt()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            var model = new SaveBrowserModel(_storage);

            var result = model.WriteSection(model.Open("manual-01.save"), "counter", "{\"Value\": 99, \"Note\": \"edited\"}");
            Assert.That(result.Succeeded, result.Problem);

            var loaded = await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(loaded.Succeeded);
            Assert.That(loaded.UsedBackup, Is.False);
            Assert.That(_counter.Value, Is.EqualTo(99));
            Assert.That(_counter.Note, Is.EqualTo("edited"));
        }

        [Test]
        public async Task EditingAGzipSave_KeepsItGzipped()
        {
            var slots = Slots(new SaveFileCodec(new[] { new GzipTransform() }));
            await slots.SaveAsync(SlotId.Manual(1));
            var model = new SaveBrowserModel(_storage);
            var save = model.Open("manual-01.save");
            Assert.That(save.Transforms, Is.EqualTo(new[] { "gzip" }));

            Assert.That(model.WriteSection(save, "counter", "{\"Value\": 7}").Succeeded);
            Assert.That(model.Open("manual-01.save").Transforms, Is.EqualTo(new[] { "gzip" }));
            await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(_counter.Value, Is.EqualTo(7));
        }

        [Test]
        public async Task InvalidJson_IsRefused_AndTheFileIsUntouched()
        {
            await Slots().SaveAsync(SlotId.Manual(1));
            var before = _storage.Read("manual-01.save");
            var model = new SaveBrowserModel(_storage);

            var result = model.WriteSection(model.Open("manual-01.save"), "counter", "{\"Value\": ");
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Problem, Does.Contain("JSON"));
            Assert.That(_storage.Read("manual-01.save"), Is.EqualTo(before));
        }

        [Test]
        public async Task EncryptedSave_SaysWhyItCantBeOpened_AndRefusesEdits()
        {
            await Slots(new SaveFileCodec(new[] { new AesTransform(new byte[32]) })).SaveAsync(SlotId.Manual(1));
            var model = new SaveBrowserModel(_storage);
            var save = model.Open("manual-01.save");

            Assert.That(save.Document, Is.Null);
            Assert.That(save.Problem, Does.Contain("aes"));
            Assert.That(model.WriteSection(save, "counter", "{}").Succeeded, Is.False);
        }

        [Test]
        public async Task DamagedSave_OpensItsBackup_AndSaysSo()
        {
            var slots = Slots();
            await slots.SaveAsync(SlotId.Manual(1));
            await slots.SaveAsync(SlotId.Manual(1));
            _storage.Overwrite("manual-01.save", Encoding.UTF8.GetBytes("junk"));

            var save = new SaveBrowserModel(_storage).Open("manual-01.save");
            Assert.That(save.UsedBackup);
            Assert.That(save.Document, Is.Not.Null);
        }

        [Test]
        public async Task Delete_RemovesTheSlotAndItsHeader()
        {
            await Slots().SaveAsync(SlotId.Manual(1));
            Assert.That(new SaveBrowserModel(_storage).Delete("manual-01.save"), Is.Null);
            Assert.That(_storage.List(), Is.Empty);
        }

        [Test]
        public void MissingEntry_SaysSo()
        {
            var save = new SaveBrowserModel(_storage).Open("nothing.save");
            Assert.That(save.Document, Is.Null);
            Assert.That(save.Problem, Is.EqualTo("missing"));
        }

        [Test]
        public void UnknownSection_IsRefused()
        {
            var service = new SaveService();
            var model = new SaveBrowserModel(_storage);
            var store = new SaveFileStore(_storage, new SaveSerializer());
            store.Write("a.save", service.Capture().ToJson());
            Assert.That(model.WriteSection(model.Open("a.save"), "nope", "{}").Succeeded, Is.False);
        }
    }
}
