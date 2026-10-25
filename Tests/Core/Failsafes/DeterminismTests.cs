using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /**
     * <summary>The same state always writes the same bytes: lines end in LF on every platform; a dictionary's insertion
     * order never shows; properties are in ordinal order at every depth while lists keep their order; and loading a save
     * then saving the unchanged game writes the same section data.</summary>
     */
    public sealed class DeterminismTests
    {
        private static SaveService ServiceWith(ProfileData data)
        {
            var service = new SaveService();
            service.Register(new ProfileSection { Data = data });
            return service;
        }

        private static string SectionsText(SaveService service)
        {
            var json = service.Capture().ToJson();
            json.Remove("meta");
            return Encoding.UTF8.GetString(new SaveSerializer().ToBytes(json, true));
        }

        [Test]
        public void WrittenText_EndsLinesInLF_Only_OnEveryPlatform()
        {
            using (var stream = new System.IO.MemoryStream())
            using (var writer = SaveSerializer.TextWriterFor(stream))
                Assert.That(writer.NewLine, Is.EqualTo("\n"), "the writer's own line ending, not the platform's (CRLF on Windows)");
            var bytes = new SaveSerializer().ToBytes(JObject.Parse("{\"a\":{\"b\":[1,2,{\"c\":3}]}}"), true);
            Assert.That(bytes, Has.No.Member((byte)'\r'));
            Assert.That(Encoding.UTF8.GetString(bytes), Does.Contain("\n"));
        }

        [Test]
        public void ADictionarysInsertionOrder_NeverChangesTheBytes()
        {
            var first = new ProfileData { Labels = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1", ["c"] = "3" } };
            var second = new ProfileData { Labels = new Dictionary<string, string> { ["c"] = "3", ["a"] = "1", ["b"] = "2" } };
            Assert.That(SectionsText(ServiceWith(first)), Is.EqualTo(SectionsText(ServiceWith(second))));
        }

        [Test]
        public void Properties_AreInOrdinalOrderAtEveryDepth_AndListsKeepTheirOrder()
        {
            var json = SaveSerializer.Canonical(JObject.Parse("{\"b\":{\"z\":1,\"Y\":2,\"a\":3},\"a\":[{\"q\":1,\"p\":2},3,1]}"));
            Assert.That(json.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"a\":[{\"p\":2,\"q\":1},3,1],\"b\":{\"Y\":2,\"a\":3,\"z\":1}}"));
        }

        [Test]
        public void LoadingThenSavingTheUnchangedGame_WritesTheSameSectionData()
        {
            var data = new ProfileData
            {
                Name = "Ash",
                Numbers = { 3, 1, 2 },
                Labels = new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" },
                FloatValue = 0.1f,
                DoubleValue = 1.0 / 3.0,
                BigNumber = long.MaxValue,
                Children = new List<ProfileData> { new ProfileData { Name = "child", FloatValue = float.NaN } },
            };
            var saving = ServiceWith(data);
            saving.Register(new CounterSection("counter") { Value = 7, Note = "n" });
            var first = SectionsText(saving);

            var serializer = new SaveSerializer();
            var document = SaveDocument.FromJson(serializer.Parse(serializer.ToBytes(saving.Capture().ToJson(), true)));
            var loading = new SaveService();
            loading.Register(new ProfileSection());
            loading.Register(new CounterSection("counter"));
            Assert.That(loading.Restore(document).Succeeded);
            Assert.That(SectionsText(loading), Is.EqualTo(first));
        }
    }
}
