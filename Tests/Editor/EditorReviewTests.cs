using System.Threading.Tasks;
using Extensions.Ids;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using SaveSystem.EditorTools;

namespace SaveSystem.Tests
{
    /** <summary>Browser edits found in review: date-like strings, plain values, encrypted saves.</summary> */
    public sealed class EditorReviewTests
    {
        [Test]
        public async Task BrowserEdit_KeepsDateLikeStrings()
        {
            var storage = new MemorySaveStorage();
            var service = new SaveService();
            var profile = new ProfileSection();
            profile.Data.Name = "2024-03-05";
            service.Register(profile);
            var slots = new SaveSlots<object>(service, storage, null, new SlotOptions(new ImmediateSaveWorker()));
            await slots.SaveAsync(SlotId.Manual(1));

            var model = new SaveBrowserModel(storage);
            var save = model.Open("manual-01.save");
            var text = SaveBrowserModel.SectionText(save.Document, "profile");
            Assert.That(model.WriteSection(save, "profile", text.Replace("\"BigNumber\": 0", "\"BigNumber\": 7")).Succeeded);

            await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(profile.Data.Name, Is.EqualTo("2024-03-05"));
            Assert.That(profile.Data.BigNumber, Is.EqualTo(7));
        }

        [Test]
        public void BrowserText_ForAPlainValue_IsValidJson()
        {
            var document = new SaveDocument();
            document.Sections["text"] = new SectionRecord(1, new JValue("hello"));
            Assert.That(SaveBrowserModel.SectionText(document, "text"), Is.EqualTo("\"hello\""));
        }

        [Test]
        public async Task Browser_GivenTheKey_OpensAndEditsAnEncryptedSave()
        {
            var aes = new AesTransform(new byte[32]);
            var storage = new MemorySaveStorage();
            var service = new SaveService();
            var counter = new CounterSection("counter");
            service.Register(counter);
            var slots = new SaveSlots<object>(service, storage, null, new SlotOptions(new ImmediateSaveWorker()) { Codec = new SaveFileCodec(new[] { aes }) });
            await slots.SaveAsync(SlotId.Manual(1));

            var model = new SaveBrowserModel(storage, new ISaveTransform[] { aes });
            var save = model.Open("manual-01.save");
            Assert.That(save.Document, Is.Not.Null);
            Assert.That(model.WriteSection(save, "counter", "{\"Value\": 12}").Succeeded);
            await slots.LoadAsync(SlotId.Manual(1));
            Assert.That(counter.Value, Is.EqualTo(12));
        }
    }
}
