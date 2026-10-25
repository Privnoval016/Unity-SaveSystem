using System.IO;
using System.IO.Compression;

namespace SaveSystem
{
    /** <summary>Compresses a save with gzip.</summary> */
    public sealed class GzipTransform : ISaveTransform
    {
        #region Properties

        /** <summary>The id written in each file's header.</summary> */
        public const string GzipId = "gzip";

        /** <inheritdoc /> */
        public string Id => GzipId;

        /** <summary>The most a file may unpack to, so a crafted file can't fill memory.</summary> */
        public long MaxBytes { get; }

        #endregion

        #region Public API

        /** <summary>Compresses, unpacking at most <paramref name="maxBytes" /> (256 MB by default).</summary> */
        public GzipTransform(long maxBytes = 256L * 1024 * 1024) => MaxBytes = maxBytes;

        /** <inheritdoc /> */
        public byte[] Apply(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                    gzip.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }

        /** <inheritdoc /> */
        public byte[] Revert(byte[] data)
        {
            using (var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaxBytes) throw new InvalidDataException($"The data unpacks to more than {MaxBytes} bytes.");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        #endregion
    }
}
