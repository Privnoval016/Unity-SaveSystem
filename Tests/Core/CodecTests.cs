using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>The file frame: header, length, checksum, gzip and AES.</summary> */
    public sealed class CodecTests
    {
        private static readonly byte[] Payload = Encoding.UTF8.GetBytes("{\n  \"format\": 1,\n  \"text\": \"hello hello hello hello\"\n}");
        private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        private static readonly byte[] OtherKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

        private static int HeaderEnd(byte[] file) => Array.IndexOf(file, (byte)'\n');

        [Test]
        public void Plain_RoundTrips_AndStaysReadable()
        {
            var file = SaveFileCodec.Plain.Encode(Payload);
            var text = Encoding.UTF8.GetString(file);
            Assert.That(text, Does.StartWith("{\"format\":1,"));
            Assert.That(text, Does.EndWith(Encoding.UTF8.GetString(Payload)));

            var decoded = SaveFileCodec.Plain.Decode(file);
            Assert.That(decoded.Status, Is.EqualTo(DecodeStatus.Ok));
            Assert.That(decoded.Payload, Is.EqualTo(Payload));
        }

        [Test]
        public void EmptyPayload_RoundTrips()
        {
            var decoded = SaveFileCodec.Plain.Decode(SaveFileCodec.Plain.Encode(new byte[0]));
            Assert.That(decoded.Status, Is.EqualTo(DecodeStatus.Ok));
            Assert.That(decoded.Payload, Is.Empty);
        }

        [Test]
        public void FlippedByte_FailsTheChecksum()
        {
            var file = SaveFileCodec.Plain.Encode(Payload);
            file[file.Length - 3] ^= 0x01;
            Assert.That(SaveFileCodec.Plain.Decode(file).Status, Is.EqualTo(DecodeStatus.ChecksumMismatch));
        }

        [Test]
        public void SameLengthEdit_FailsTheChecksum()
        {
            var file = Encoding.UTF8.GetString(SaveFileCodec.Plain.Encode(Payload)).Replace("hello", "jello");
            Assert.That(SaveFileCodec.Plain.Decode(Encoding.UTF8.GetBytes(file)).Status, Is.EqualTo(DecodeStatus.ChecksumMismatch));
        }

        [Test]
        public void Truncated_IsWrongLength()
        {
            var file = SaveFileCodec.Plain.Encode(Payload);
            Assert.That(SaveFileCodec.Plain.Decode(file.Take(file.Length - 5).ToArray()).Status, Is.EqualTo(DecodeStatus.WrongLength));
        }

        [Test]
        public void CutRightAfterTheHeader_IsWrongLength()
        {
            var file = SaveFileCodec.Plain.Encode(Payload);
            Assert.That(SaveFileCodec.Plain.Decode(file.Take(HeaderEnd(file) + 1).ToArray()).Status, Is.EqualTo(DecodeStatus.WrongLength));
        }

        [Test]
        public void Appended_IsWrongLength()
        {
            var file = SaveFileCodec.Plain.Encode(Payload).Concat(new byte[] { 0, 0 }).ToArray();
            Assert.That(SaveFileCodec.Plain.Decode(file).Status, Is.EqualTo(DecodeStatus.WrongLength));
        }

        [Test]
        public void Empty_AndNull_AreEmpty()
        {
            Assert.That(SaveFileCodec.Plain.Decode(new byte[0]).Status, Is.EqualTo(DecodeStatus.Empty));
            Assert.That(SaveFileCodec.Plain.Decode(null).Status, Is.EqualTo(DecodeStatus.Empty));
        }

        [TestCase("no newline at all")]
        [TestCase("not json\n{}")]
        [TestCase("{}\n{}")]
        [TestCase("[]\n{}")]
        [TestCase("{\"format\":0,\"length\":2,\"sha256\":\"x\",\"transforms\":[]}\n{}")]
        [TestCase("{\"format\":\"one\",\"length\":2,\"sha256\":\"x\",\"transforms\":[]}\n{}")]
        [TestCase("{\"format\":1,\"sha256\":\"x\",\"transforms\":[]}\n{}")]
        [TestCase("{\"format\":1,\"length\":-1,\"sha256\":\"x\",\"transforms\":[]}\n{}")]
        [TestCase("{\"format\":1,\"length\":2,\"transforms\":[]}\n{}")]
        [TestCase("{\"format\":1,\"length\":2,\"sha256\":\"x\"}\n{}")]
        [TestCase("{\"format\":1,\"length\":\"two\",\"sha256\":\"x\",\"transforms\":[]}\n{}")]
        public void GarbledHeader_IsBadHeader(string file)
        {
            Assert.That(SaveFileCodec.Plain.Decode(Encoding.UTF8.GetBytes(file)).Status, Is.EqualTo(DecodeStatus.BadHeader));
        }

        [Test]
        public void HeaderLongerThanTheLimit_IsBadHeader()
        {
            var file = Encoding.UTF8.GetBytes(new string(' ', 2000) + "\n{}");
            Assert.That(SaveFileCodec.Plain.Decode(file).Status, Is.EqualTo(DecodeStatus.BadHeader));
        }

        [Test]
        public void RandomBytes_NeverThrow()
        {
            var random = new Random(1234);
            for (var i = 0; i < 500; i++)
            {
                var bytes = new byte[random.Next(0, 300)];
                random.NextBytes(bytes);
                Assert.That(SaveFileCodec.Plain.Decode(bytes).Status, Is.Not.EqualTo(DecodeStatus.Ok));
            }
        }

        [Test]
        public void UnknownTransform_IsReported()
        {
            var file = new SaveFileCodec(new[] { new GzipTransform() }).Encode(Payload);
            Assert.That(SaveFileCodec.Plain.Decode(file).Status, Is.EqualTo(DecodeStatus.UnknownTransform));
        }

        [Test]
        public void Gzip_RoundTrips_AndShrinks()
        {
            var codec = new SaveFileCodec(new[] { new GzipTransform() });
            var repetitive = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"value\":12345},", 2000)));
            var file = codec.Encode(repetitive);
            Assert.That(file.Length, Is.LessThan(repetitive.Length / 4));
            Assert.That(codec.Decode(file).Payload, Is.EqualTo(repetitive));
            Assert.That(codec.WritesPlainText, Is.False);
        }

        [Test]
        public void Aes_RoundTrips_HidesThePayload_AndDiffersEveryWrite()
        {
            var codec = new SaveFileCodec(new[] { new AesTransform(Key) });
            var first = codec.Encode(Payload);
            var second = codec.Encode(Payload);
            Assert.That(Encoding.UTF8.GetString(first), Does.Not.Contain("hello"));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(codec.Decode(first).Payload, Is.EqualTo(Payload));
            Assert.That(codec.Decode(second).Payload, Is.EqualTo(Payload));
        }

        [Test]
        public void GzipThenAes_AndAesThenGzip_BothRoundTrip()
        {
            var gzipThenAes = new SaveFileCodec(new ISaveTransform[] { new GzipTransform(), new AesTransform(Key) });
            var aesThenGzip = new SaveFileCodec(new ISaveTransform[] { new AesTransform(Key), new GzipTransform() });
            Assert.That(gzipThenAes.Decode(gzipThenAes.Encode(Payload)).Payload, Is.EqualTo(Payload));
            Assert.That(aesThenGzip.Decode(aesThenGzip.Encode(Payload)).Payload, Is.EqualTo(Payload));
        }

        [Test]
        public void WrongKey_NeverYieldsThePayload()
        {
            var file = new SaveFileCodec(new[] { new AesTransform(Key) }).Encode(Payload);
            var decoded = new SaveFileCodec(new[] { new AesTransform(OtherKey) }).Decode(file);
            Assert.That(decoded.Status != DecodeStatus.Ok || !decoded.Payload.SequenceEqual(Payload));
        }

        [Test]
        public void WrongKey_UnderGzip_FailsTheTransform()
        {
            var file = new SaveFileCodec(new ISaveTransform[] { new GzipTransform(), new AesTransform(Key) }).Encode(Payload);
            var decoded = new SaveFileCodec(new ISaveTransform[] { new GzipTransform(), new AesTransform(OtherKey) }).Decode(file);
            Assert.That(decoded.Status, Is.EqualTo(DecodeStatus.TransformFailed));
        }

        [Test]
        public void TransformNoLongerWritten_IsStillRead()
        {
            var aes = new AesTransform(Key);
            var file = new SaveFileCodec(new[] { aes }).Encode(Payload);
            var plainNow = new SaveFileCodec(alsoReadable: new[] { aes });
            Assert.That(plainNow.Decode(file).Payload, Is.EqualTo(Payload));
            Assert.That(plainNow.WritesPlainText);
        }

        [Test]
        public void TwoTransformsWithOneId_Throw()
        {
            Assert.Throws<ArgumentException>(() => new SaveFileCodec(new[] { new AesTransform(Key) }, new[] { new AesTransform(OtherKey) }));
        }

        [TestCase(0)]
        [TestCase(15)]
        [TestCase(33)]
        public void AesKeyOfWrongLength_Throws(int length)
        {
            Assert.Throws<ArgumentException>(() => new AesTransform(new byte[length]));
        }

        [TestCase(16)]
        [TestCase(24)]
        public void ShorterAesKeys_Work(int length)
        {
            var codec = new SaveFileCodec(new[] { new AesTransform(new byte[length]) });
            Assert.That(codec.Decode(codec.Encode(Payload)).Payload, Is.EqualTo(Payload));
        }

        [Test]
        public void AesKey_IsCopied()
        {
            var key = (byte[])Key.Clone();
            var aes = new AesTransform(key);
            var encrypted = aes.Apply(Payload);
            key[0] ^= 0xFF;
            Assert.That(aes.Revert(encrypted), Is.EqualTo(Payload));
        }

        [Test]
        public void TooShortCiphertext_Throws()
        {
            Assert.Throws<CryptographicException>(() => new AesTransform(Key).Revert(new byte[5]));
        }

        [Test]
        public void CorruptGzip_FailsTheTransform()
        {
            var codec = new SaveFileCodec(new[] { new GzipTransform() });
            var header = "{\"format\":1,\"length\":4,\"sha256\":\"" + Sha("abcd") + "\",\"transforms\":[\"gzip\"]}\nabcd";
            Assert.That(codec.Decode(Encoding.UTF8.GetBytes(header)).Status, Is.EqualTo(DecodeStatus.TransformFailed));
        }

        private static string Sha(string text)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));
        }
    }
}
