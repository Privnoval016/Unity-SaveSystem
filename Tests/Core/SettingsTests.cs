using System.Threading.Tasks;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    public sealed class SettingsTests
    {
        [Test]
        public async Task Settings_RoundTrip()
        {
            var service = new SaveService();
            var volume = new CounterSection("audio") { Value = 70 };
            service.Register(volume);
            var store = new SettingsStore(service, new MemorySaveStorage(), new SaveFileOptions { Worker = new ImmediateSaveWorker() });

            Assert.That((await store.SaveAsync()).Succeeded);
            volume.Value = 0;
            Assert.That((await store.LoadAsync()).Succeeded);
            Assert.That(volume.Value, Is.EqualTo(70));
        }

        [Test]
        public async Task NoSettingsYet_IsMissing()
        {
            var store = new SettingsStore(new SaveService(), new MemorySaveStorage(), new SaveFileOptions { Worker = new ImmediateSaveWorker() });
            Assert.That((await store.LoadAsync()).Status, Is.EqualTo(LoadStatus.Missing));
        }

        [Test]
        public async Task DeletingEverySlot_LeavesSettingsAlone()
        {
            var storage = new MemorySaveStorage();
            var options = new SlotOptions { Worker = new ImmediateSaveWorker() };
            var settingsService = new SaveService();
            var volume = new CounterSection("audio") { Value = 70 };
            settingsService.Register(volume);
            var settings = new SettingsStore(settingsService, storage, options);
            var gameService = new SaveService();
            gameService.Register(new CounterSection("party"));
            var slots = new SaveSlots<object>(gameService, storage, null, options);

            await settings.SaveAsync();
            await slots.SaveAsync(SlotId.Manual(1));
            await slots.AutosaveAsync();
            foreach (var summary in await slots.ListAsync())
                await slots.DeleteAsync(summary.Slot);

            Assert.That(await slots.ListAsync(), Is.Empty);
            volume.Value = 0;
            Assert.That((await settings.LoadAsync()).Succeeded);
            Assert.That(volume.Value, Is.EqualTo(70));
        }

        [Test]
        public async Task SettingsCaptureFailure_WritesNothing()
        {
            var service = new SaveService();
            service.Register(new CounterSection("audio") { ThrowOnCapture = true });
            var storage = new MemorySaveStorage();
            var store = new SettingsStore(service, storage, new SaveFileOptions { Worker = new ImmediateSaveWorker() });
            Assert.That((await store.SaveAsync()).Status, Is.EqualTo(SaveStatus.CaptureFailed));
            Assert.That(storage.List(), Is.Empty);
        }
    }
}
