using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /** <summary>How reading an entry ended.</summary> */
    public enum ReadStatus
    {
        /** <summary>Read, from the entry or its backup.</summary> */
        Ok,
        /** <summary>Neither the entry nor a backup exists.</summary> */
        Missing,
        /** <summary>Neither the entry nor its backup could be read.</summary> */
        Damaged,
    }

    /** <summary>The outcome of reading an entry: the value, whether it came from the backup, and what went wrong.</summary> */
    public readonly struct ReadResult<T>
    {
        #region Properties

        /** <summary>How reading ended.</summary> */
        public ReadStatus Status { get; }

        /** <summary>The value read; default unless <see cref="ReadStatus.Ok" />.</summary> */
        public T Value { get; }

        /** <summary>True when the entry was unusable and its backup was read instead.</summary> */
        public bool UsedBackup { get; }

        /** <summary>What went wrong with the entry, or with both; null when nothing did.</summary> */
        public string Problem { get; }

        #endregion

        #region Public API

        /** <summary>Makes a result.</summary> */
        public ReadResult(ReadStatus status, T value, bool usedBackup, string problem)
        {
            Status = status;
            Value = value;
            UsedBackup = usedBackup;
            Problem = problem;
        }

        #endregion
    }

    /**
     * <summary>Reads and writes JSON entries through a codec, falling back to an entry's backup when it is damaged.</summary>
     * <remarks>Synchronous and safe to call from a background thread. Expected failures are returned, never thrown.</remarks>
     */
    public sealed class SaveFileStore
    {
        #region Private Fields

        private readonly SaveSerializer _serializer;
        private readonly SaveFileCodec _codec;

        #endregion

        #region Properties

        /** <summary>Where the entries live.</summary> */
        public ISaveStorage Storage { get; }

        #endregion

        #region Public API

        /** <summary>Makes a store; with no codec, entries are written as plain framed JSON.</summary> */
        public SaveFileStore(ISaveStorage storage, SaveSerializer serializer, SaveFileCodec codec = null)
        {
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _codec = codec ?? SaveFileCodec.Plain;
        }

        /**
         * <summary>Encodes and writes JSON to an entry. The JSON is only read, so it may be shared with no other writer.</summary>
         * <remarks>
         * JSON nested deeper than <see cref="SaveSerializer.MaxDepth" /> is refused, since it could not be read back. When
         * the current entry is damaged and its backup is good, the backup is kept as the backup instead of the damage.
         * </remarks>
         */
        public SaveResult Write(string entry, JToken json)
        {
            if (SaveSerializer.DepthOf(json) > SaveSerializer.MaxDepth)
                return new SaveResult(SaveStatus.WriteFailed, $"Writing '{entry}' failed: the data nests deeper than {SaveSerializer.MaxDepth} levels.");
            try
            {
                var file = _codec.Encode(_serializer.ToBytes(json, _codec.WritesPlainText));
                KeepGoodBackup(entry);
                Storage.Write(entry, file);
                return new SaveResult(SaveStatus.Saved, savedTo: entry);
            }
            catch (Exception e)
            {
                return new SaveResult(SaveStatus.WriteFailed, $"Writing '{entry}' failed: {e.Message}");
            }
        }

        /**
         * <summary>Reads an entry; if it is missing or unusable, reads its backup.</summary>
         * <param name="parse">Turns the JSON into a value, throwing when it is not valid.</param>
         */
        public ReadResult<T> Read<T>(string entry, Func<JToken, T> parse)
        {
            var problems = new List<string>();
            var bytes = TryRead(() => Storage.Read(entry), "the entry", problems);
            if (bytes != null && TryDecode(bytes, parse, "the entry", problems, out var value))
                return new ReadResult<T>(ReadStatus.Ok, value, false, null);

            var backup = TryRead(() => Storage.ReadBackup(entry), "the backup", problems);
            if (bytes == null && backup == null && problems.Count == 0)
                return new ReadResult<T>(ReadStatus.Missing, default, false, null);

            if (bytes == null && problems.Count == 0) problems.Add("the entry is missing");
            if (backup != null && TryDecode(backup, parse, "the backup", problems, out value))
                return new ReadResult<T>(ReadStatus.Ok, value, true, string.Join("; ", problems));

            if (backup == null) problems.Add("there is no backup");
            return new ReadResult<T>(ReadStatus.Damaged, default, false, string.Join("; ", problems));
        }

        /** <summary>Deletes an entry and its backup.</summary> <returns>Null on success, otherwise what went wrong.</returns> */
        public string Delete(string entry)
        {
            try
            {
                Storage.Delete(entry);
                return null;
            }
            catch (Exception e)
            {
                return $"Deleting '{entry}' failed: {e.Message}";
            }
        }

        #endregion

        #region Helpers

        private void KeepGoodBackup(string entry)
        {
            var current = Storage.Read(entry);
            if (current == null || _codec.Decode(current).Status == DecodeStatus.Ok) return;
            var backup = Storage.ReadBackup(entry);
            if (backup != null && _codec.Decode(backup).Status == DecodeStatus.Ok) Storage.Write(entry, backup);
        }

        private static byte[] TryRead(Func<byte[]> read, string what, List<string> problems)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                problems.Add($"{what} could not be read: {e.Message}");
                return null;
            }
        }

        private bool TryDecode<T>(byte[] bytes, Func<JToken, T> parse, string what, List<string> problems, out T value)
        {
            value = default;
            var decoded = _codec.Decode(bytes);
            if (decoded.Status != DecodeStatus.Ok)
            {
                problems.Add($"{what}: {decoded.Problem}");
                return false;
            }
            try
            {
                value = parse(_serializer.Parse(decoded.Payload));
                return true;
            }
            catch (Exception e)
            {
                problems.Add($"{what} is not valid: {e.Message}");
                return false;
            }
        }

        #endregion
    }
}
