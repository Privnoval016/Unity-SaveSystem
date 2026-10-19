using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /** <summary>Why a file could not be decoded.</summary> */
    public enum DecodeStatus
    {
        /** <summary>Decoded.</summary> */
        Ok,
        /** <summary>The file has no bytes.</summary> */
        Empty,
        /** <summary>The first line is not a valid header.</summary> */
        BadHeader,
        /** <summary>The payload is not as long as the header says: the file was cut short or added to.</summary> */
        WrongLength,
        /** <summary>The payload does not match its checksum: the file is damaged or was edited.</summary> */
        ChecksumMismatch,
        /** <summary>The header names a transform this reader doesn't know.</summary> */
        UnknownTransform,
        /** <summary>A transform could not reverse the payload, such as decryption with the wrong key.</summary> */
        TransformFailed,
    }

    /** <summary>The outcome of decoding a file: its status, the payload when it worked, and why when it didn't.</summary> */
    public readonly struct DecodeResult
    {
        #region Properties

        /** <summary>How decoding ended.</summary> */
        public DecodeStatus Status { get; }

        /** <summary>The payload with every transform reversed; null unless <see cref="DecodeStatus.Ok" />.</summary> */
        public byte[] Payload { get; }

        /** <summary>What went wrong; null when nothing did.</summary> */
        public string Problem { get; }

        #endregion

        #region Public API

        /** <summary>Makes a result.</summary> */
        public DecodeResult(DecodeStatus status, byte[] payload, string problem)
        {
            Status = status;
            Payload = payload;
            Problem = problem;
        }

        #endregion
    }

    /**
     * <summary>Frames a save's bytes: one header line (format, length, SHA-256, transforms), a newline, then the payload.</summary>
     * <remarks>
     * The checksum covers the stored payload, so damage is found before anything is decrypted or parsed. With no
     * transforms the payload is the JSON itself, so the file stays readable in a text editor.
     * </remarks>
     */
    public sealed class SaveFileCodec
    {
        #region Private Fields

        private const int HeaderFormat = 1;
        private const int MaxHeaderLength = 1024;
        private const byte NewLine = (byte)'\n';
        private const string FormatProperty = "format";
        private const string LengthProperty = "length";
        private const string ChecksumProperty = "sha256";
        private const string TransformsProperty = "transforms";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly ISaveTransform[] _write;

        /** <summary>Keyed by transform id, resolving to every transform this codec can reverse.</summary> */
        private readonly Dictionary<string, ISaveTransform> _known;

        #endregion

        #region Properties

        /** <summary>A codec with no transforms.</summary> */
        public static SaveFileCodec Plain { get; } = new SaveFileCodec();

        /** <summary>True when files are written with no transforms, so the payload is plain text.</summary> */
        public bool WritesPlainText => _write.Length == 0;

        #endregion

        #region Public API

        /**
         * <param name="write">Applied in order on every write.</param>
         * <param name="alsoReadable">Transforms that are no longer written but must still be read, such as after turning encryption off.</param>
         * <exception cref="ArgumentException">Two different transforms share an id, or an id is not a valid name.</exception>
         */
        public SaveFileCodec(IEnumerable<ISaveTransform> write = null, IEnumerable<ISaveTransform> alsoReadable = null)
        {
            _write = (write ?? Enumerable.Empty<ISaveTransform>()).ToArray();
            _known = new Dictionary<string, ISaveTransform>(StringComparer.Ordinal);
            foreach (var transform in _write.Concat(alsoReadable ?? Enumerable.Empty<ISaveTransform>()))
                AddKnown(transform);
        }

        /** <summary>Transforms the payload and frames it with a header.</summary> */
        public byte[] Encode(byte[] payload)
        {
            var stored = payload;
            foreach (var transform in _write)
                stored = transform.Apply(stored);

            var header = new JObject
            {
                [FormatProperty] = HeaderFormat,
                [LengthProperty] = stored.Length,
                [ChecksumProperty] = Checksum(stored),
                [TransformsProperty] = new JArray(_write.Select(transform => (object)transform.Id).ToArray()),
            };
            var headerBytes = Utf8.GetBytes(header.ToString(Formatting.None));

            var file = new byte[headerBytes.Length + 1 + stored.Length];
            Buffer.BlockCopy(headerBytes, 0, file, 0, headerBytes.Length);
            file[headerBytes.Length] = NewLine;
            Buffer.BlockCopy(stored, 0, file, headerBytes.Length + 1, stored.Length);
            return file;
        }

        /** <summary>Checks the frame and checksum, then reverses the transforms. Never throws for bad input.</summary> */
        public DecodeResult Decode(byte[] file)
        {
            if (file == null || file.Length == 0) return Fail(DecodeStatus.Empty, "The file is empty.");

            var lineEnd = Array.IndexOf(file, NewLine, 0, Math.Min(file.Length, MaxHeaderLength));
            if (lineEnd < 0) return Fail(DecodeStatus.BadHeader, "No header line.");
            if (!TryReadHeader(file, lineEnd, out var length, out var checksum, out var transforms, out var problem))
                return Fail(DecodeStatus.BadHeader, problem);

            var storedLength = file.Length - lineEnd - 1;
            if (storedLength != length) return Fail(DecodeStatus.WrongLength, $"Payload is {storedLength} bytes; the header says {length}.");

            var stored = new byte[storedLength];
            Buffer.BlockCopy(file, lineEnd + 1, stored, 0, storedLength);
            if (!string.Equals(Checksum(stored), checksum, StringComparison.Ordinal))
                return Fail(DecodeStatus.ChecksumMismatch, "The checksum does not match.");

            return Untransform(stored, transforms);
        }

        #endregion

        #region Helpers

        private void AddKnown(ISaveTransform transform)
        {
            if (transform == null) throw new ArgumentException("A transform is null.");
            if (!SaveNames.IsValidEntryName(transform.Id)) throw new ArgumentException($"Invalid transform id '{transform.Id}'.");
            if (_known.TryGetValue(transform.Id, out var existing) && !ReferenceEquals(existing, transform))
                throw new ArgumentException($"Two transforms use the id '{transform.Id}'.");
            _known[transform.Id] = transform;
        }

        private static bool TryReadHeader(byte[] file, int lineEnd, out long length, out string checksum, out string[] transforms, out string problem)
        {
            length = 0;
            checksum = null;
            transforms = null;
            try
            {
                var header = JObject.Parse(Utf8.GetString(file, 0, lineEnd));
                if (header.Value<int?>(FormatProperty) != HeaderFormat) return Problem("Unknown header format.", out problem);
                length = header.Value<long?>(LengthProperty) ?? -1;
                checksum = header.Value<string>(ChecksumProperty);
                transforms = (header[TransformsProperty] as JArray)?.Select(token => token.Value<string>()).ToArray();
                if (length < 0 || string.IsNullOrEmpty(checksum) || transforms == null) return Problem("The header is missing a field.", out problem);
                problem = null;
                return true;
            }
            catch (Exception e)
            {
                return Problem($"The header is not valid: {e.Message}", out problem);
            }
        }

        private static bool Problem(string message, out string problem)
        {
            problem = message;
            return false;
        }

        private DecodeResult Untransform(byte[] stored, string[] transforms)
        {
            var payload = stored;
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                if (transforms[i] == null || !_known.TryGetValue(transforms[i], out var transform))
                    return Fail(DecodeStatus.UnknownTransform, $"Unknown transform '{transforms[i]}'.");
                try
                {
                    payload = transform.Revert(payload);
                }
                catch (Exception e)
                {
                    return Fail(DecodeStatus.TransformFailed, $"Transform '{transforms[i]}' failed: {e.Message}");
                }
            }
            return new DecodeResult(DecodeStatus.Ok, payload, null);
        }

        private static string Checksum(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(data);
                var text = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        private static DecodeResult Fail(DecodeStatus status, string problem) => new DecodeResult(status, null, problem);

        #endregion
    }
}
