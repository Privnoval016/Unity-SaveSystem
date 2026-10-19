using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaveSystem.Tests
{
    /** <summary>Asset ids: parsing, equality and Unity serialization.</summary> */
    public sealed class AssetIdTests
    {
        [Serializable]
        private sealed class Holder
        {
            public AssetId Id;
        }

        [Test]
        public void New_IsValid_AndUnique()
        {
            var ids = Enumerable.Range(0, 1000).Select(_ => AssetId.New()).ToList();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(1000));
            Assert.That(ids.All(id => AssetId.TryParse(id.Value, out var parsed) && parsed == id));
        }

        [Test]
        public void Parse_LowerCasesHex()
        {
            var id = AssetId.Parse("ABCDEF0123456789ABCDEF0123456789");
            Assert.That(id.Value, Is.EqualTo("abcdef0123456789abcdef0123456789"));
            Assert.That(id, Is.EqualTo(AssetId.Parse("abcdef0123456789abcdef0123456789")));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("abcdef0123456789abcdef012345678")]
        [TestCase("abcdef0123456789abcdef01234567890")]
        [TestCase("ghijkl0123456789abcdef0123456789")]
        [TestCase("abcdef01-3456789abcdef0123456789")]
        [TestCase("abcdef0123456789abcdef012345678 ")]
        public void InvalidText_IsRejected(string text)
        {
            Assert.That(AssetId.TryParse(text, out _), Is.False);
            Assert.Throws<FormatException>(() => AssetId.Parse(text));
        }

        [Test]
        public void Default_IsEmpty()
        {
            Assert.That(default(AssetId).IsEmpty);
            Assert.That(default(AssetId).Value, Is.EqualTo(string.Empty));
            Assert.That(default(AssetId), Is.EqualTo(new AssetId()));
            Assert.That(AssetId.New().IsEmpty, Is.False);
        }

        [Test]
        public void Equality_AndHashing_FollowTheText()
        {
            var a = AssetId.Parse("0123456789abcdef0123456789abcdef");
            var b = AssetId.Parse("0123456789ABCDEF0123456789ABCDEF");
            Assert.That(a == b);
            Assert.That(a != AssetId.New());
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.Equals((object)b));
        }

        [Test]
        public void UnitySerialization_KeepsTheId()
        {
            var id = AssetId.New();
            var json = JsonUtility.ToJson(new Holder { Id = id });
            Assert.That(json, Does.Contain(id.Value));
            Assert.That(JsonUtility.FromJson<Holder>(json).Id, Is.EqualTo(id));
        }

        [Test]
        public void SaveableAssets_ReadIdsSafely()
        {
            var asset = ScriptableObject.CreateInstance<TestSaveableAsset>();
            var plain = ScriptableObject.CreateInstance<AssetCatalog>();
            try
            {
                Assert.That(SaveableAssets.IsSaveable(asset));
                Assert.That(SaveableAssets.TryGetId(asset, out _), Is.False);
                var id = AssetId.New();
                asset.SetId(id);
                Assert.That(SaveableAssets.TryGetId(asset, out var read) && read == id);
                Assert.That(SaveableAssets.IsSaveable(plain), Is.False);
                Assert.That(SaveableAssets.IdOf(plain).IsEmpty);
                Assert.That(SaveableAssets.IsSaveable(null), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(asset);
            }
            Assert.That(SaveableAssets.IsSaveable(asset), Is.False);
        }
    }
}
