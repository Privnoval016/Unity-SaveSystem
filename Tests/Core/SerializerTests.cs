using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    public sealed class SerializerTests
    {
        private static ProfileData RoundTrip(ProfileData data, out string text)
        {
            var serializer = new SaveSerializer();
            var bytes = serializer.ToBytes(JToken.FromObject(data, serializer.Json), true);
            text = Encoding.UTF8.GetString(bytes);
            return serializer.Parse(bytes).ToObject<ProfileData>(serializer.Json);
        }

        private static void InCulture(string name, Action test)
        {
            var thread = Thread.CurrentThread.CurrentCulture;
            var ui = Thread.CurrentThread.CurrentUICulture;
            var defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
            try
            {
                var culture = new CultureInfo(name);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                test();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = thread;
                Thread.CurrentThread.CurrentUICulture = ui;
                CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
            }
        }

        [TestCase("de-DE")]
        [TestCase("tr-TR")]
        [TestCase("fr-FR")]
        [TestCase("ar-SA")]
        public void Numbers_AreCultureInvariant(string culture)
        {
            InCulture(culture, () =>
            {
                var loaded = RoundTrip(new ProfileData { FloatValue = 1.5f, DoubleValue = 1234567.125, Name = "TITLE id İı" }, out var text);
                Assert.That(text, Does.Contain("1.5"));
                Assert.That(text, Does.Contain("1234567.125"));
                Assert.That(loaded.FloatValue, Is.EqualTo(1.5f));
                Assert.That(loaded.DoubleValue, Is.EqualTo(1234567.125));
                Assert.That(loaded.Name, Is.EqualTo("TITLE id İı"));
            });
        }

        [TestCase("tr-TR")]
        public void KeyRules_IgnoreCulture(string culture)
        {
            InCulture(culture, () =>
            {
                Assert.That(SaveNames.IsValidSectionKey("items"));
                Assert.That(SaveNames.IsValidSectionKey("ITEMS"), Is.False);
                Assert.That(SaveNames.IsValidSectionKey("ıtems"), Is.False);
            });
        }

        [Test]
        public void Dates_AreWrittenInUtc_AndReadBackAsTheSameInstant()
        {
            var local = new DateTime(2026, 10, 19, 15, 30, 0, DateTimeKind.Local);
            var loaded = RoundTrip(new ProfileData { SavedAt = local }, out var text);
            Assert.That(text, Does.Contain(local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
            Assert.That(text, Does.Contain("Z\""));
            Assert.That(loaded.SavedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(loaded.SavedAt, Is.EqualTo(local.ToUniversalTime()));
        }

        [Test]
        public void StringsThatLookLikeDates_StayExactlyAsWritten()
        {
            var loaded = RoundTrip(new ProfileData { DateLikeText = "2026-10-19T12:00:00", Name = "2026-10-19" }, out _);
            Assert.That(loaded.DateLikeText, Is.EqualTo("2026-10-19T12:00:00"));
            Assert.That(loaded.Name, Is.EqualTo("2026-10-19"));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(float.MaxValue)]
        [TestCase(float.Epsilon)]
        [TestCase(-0f)]
        public void SpecialFloats_RoundTrip(float value)
        {
            Assert.That(RoundTrip(new ProfileData { FloatValue = value }, out _).FloatValue, Is.EqualTo(value));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(0.1)]
        [TestCase(double.MaxValue)]
        public void SpecialDoubles_RoundTrip(double value)
        {
            Assert.That(RoundTrip(new ProfileData { DoubleValue = value }, out _).DoubleValue, Is.EqualTo(value));
        }

        [Test]
        public void SpecialFloats_AreValidJson()
        {
            RoundTrip(new ProfileData { FloatValue = float.NaN, DoubleValue = double.PositiveInfinity }, out var text);
            Assert.That(text, Does.Contain("\"NaN\""));
            Assert.That(text, Does.Contain("\"Infinity\""));
        }

        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public void LongValues_KeepEveryDigit(long value)
        {
            Assert.That(RoundTrip(new ProfileData { BigNumber = value }, out _).BigNumber, Is.EqualTo(value));
        }

        [Test]
        public void TypeNames_InTheData_AreIgnored()
        {
            var serializer = new SaveSerializer();
            var json = JToken.Parse("{\"$type\":\"System.IO.FileInfo, mscorlib\",\"Name\":\"safe\",\"Children\":[{\"$type\":\"System.Diagnostics.Process, System\",\"Name\":\"child\"}]}");
            var loaded = json.ToObject<ProfileData>(serializer.Json);
            Assert.That(loaded.Name, Is.EqualTo("safe"));
            Assert.That(loaded.Children[0], Is.TypeOf<ProfileData>());
            Assert.That(loaded.Children[0].Name, Is.EqualTo("child"));
        }

        [Test]
        public void ReferenceMetadata_IsIgnored()
        {
            var serializer = new SaveSerializer();
            var json = JToken.Parse("{\"$id\":\"1\",\"Name\":\"a\",\"Children\":[{\"$ref\":\"1\"}]}");
            var loaded = json.ToObject<ProfileData>(serializer.Json);
            Assert.That(loaded.Children[0], Is.Not.SameAs(loaded));
            Assert.That(loaded.Children[0].Name, Is.Null);
        }

        [Test]
        public void Lists_AreReplacedOnLoad_NotAppendedTo()
        {
            var saving = new SaveService();
            saving.Register(new PrefilledSection { Items = { 7 } });
            var document = saving.Capture();

            var loading = new SaveService();
            var loaded = new PrefilledSection();
            loading.Register(loaded);
            loading.Restore(SaveDocument.FromJson(document.ToJson()));
            Assert.That(loaded.Items, Is.EqualTo(new[] { 7 }));

            saving = new SaveService();
            saving.Register(new PrefilledSection());
            loading.Restore(SaveDocument.FromJson(saving.Capture().ToJson()));
            Assert.That(loaded.Items, Is.Empty);
        }

        [Test]
        public void DeepNesting_WithinTheLimit_RoundTrips()
        {
            var root = new ProfileData { Name = "0" };
            var current = root;
            for (var i = 1; i < 40; i++)
            {
                var child = new ProfileData { Name = i.ToString(CultureInfo.InvariantCulture) };
                current.Children = new List<ProfileData> { child };
                current = child;
            }

            var loaded = RoundTrip(root, out _);
            for (var i = 1; i < 40; i++)
                loaded = loaded.Children[0];
            Assert.That(loaded.Name, Is.EqualTo("39"));
        }

        [Test]
        public void NestingPastTheLimit_FailsCleanly()
        {
            var text = new StringBuilder();
            for (var i = 0; i < 10000; i++) text.Append('[');
            for (var i = 0; i < 10000; i++) text.Append(']');
            Assert.Throws<JsonReaderException>(() => new SaveSerializer().Parse(Encoding.UTF8.GetBytes(text.ToString())));
        }

        [Test]
        public void TrailingText_IsRejected()
        {
            Assert.Throws<JsonReaderException>(() => new SaveSerializer().Parse(Encoding.UTF8.GetBytes("{\"a\":1} {\"b\":2}")));
        }

        [Test]
        public void ByteOrderMark_IsAccepted()
        {
            var bytes = Encoding.UTF8.GetPreamble();
            var json = Encoding.UTF8.GetBytes("{\"a\":1}");
            var withMark = new byte[bytes.Length + json.Length];
            bytes.CopyTo(withMark, 0);
            json.CopyTo(withMark, bytes.Length);
            Assert.That(new SaveSerializer().Parse(withMark)["a"].ToObject<int>(), Is.EqualTo(1));
        }

        [Test]
        public void Output_HasNoByteOrderMark()
        {
            var bytes = new SaveSerializer().ToBytes(new JObject(), false);
            Assert.That(bytes[0], Is.EqualTo((byte)'{'));
        }

        [Test]
        public void ExtraConverters_AreUsed()
        {
            var serializer = new SaveSerializer(new JsonConverter[] { new UpperCaseConverter() });
            Assert.That(JToken.FromObject(new ProfileData { Name = "abc" }, serializer.Json)["Name"].ToString(), Is.EqualTo("ABC"));
        }

        private sealed class UpperCaseConverter : JsonConverter<string>
        {
            public override void WriteJson(JsonWriter writer, string value, JsonSerializer serializer) => writer.WriteValue(value?.ToUpperInvariant());

            public override string ReadJson(JsonReader reader, Type objectType, string existingValue, bool hasExistingValue, JsonSerializer serializer) =>
                (string)reader.Value;
        }
    }
}
