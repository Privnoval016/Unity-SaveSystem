using Extensions.Ids;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>Asset ids inside save data, through the serializer the save system builds.</summary> */
    public sealed class AssetIdJsonTests
    {
        private static readonly SaveSerializer Serializer = UnitySave.CreateSerializer(null);

        public sealed class Holder
        {
            public AssetId Id;
            public AssetId Empty;
        }

        [Test]
        public void AssetIdField_RoundTripsAsItsHex()
        {
            var serializer = Serializer;
            var id = AssetId.New();
            var json = JToken.FromObject(new Holder { Id = id }, serializer.Json);

            Assert.That(json["Id"].ToString(), Is.EqualTo(id.Value));
            Assert.That(json["Empty"].Type, Is.EqualTo(JTokenType.Null));
            var loaded = serializer.Parse(serializer.ToBytes(json, false)).ToObject<Holder>(serializer.Json);
            Assert.That(loaded.Id, Is.EqualTo(id));
            Assert.That(loaded.Empty.IsEmpty);
        }

        [TestCase("\"not hex\"")]
        [TestCase("12")]
        [TestCase("{}")]
        [TestCase("{\"a\":{\"b\":1}}")]
        [TestCase("[1,[2]]")]
        public void Garbage_ReadsAsEmpty_AndTheRestStillLoads(string value)
        {
            var id = AssetId.New();
            var loaded = Serializer.Parse(SaveSerializer.Utf8.GetBytes("{\"Id\":" + value + ",\"Empty\":\"" + id.Value + "\"}")).ToObject<Holder>(Serializer.Json);
            Assert.That(loaded.Id.IsEmpty);
            Assert.That(loaded.Empty, Is.EqualTo(id));
        }
    }
}
