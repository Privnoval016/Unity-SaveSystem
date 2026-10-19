using System.IO;
using System.IO.Compression;

namespace SaveSystem
{
    /** <summary>Compresses a save with gzip.</summary> */
    public sealed class GzipTransform : ISaveTransform
    {
        /** <inheritdoc /> */
        public string Id => "gzip";

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
                input.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
