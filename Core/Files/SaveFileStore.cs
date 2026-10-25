using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /** <summary>How reading an entry ended.</summary> */
    public enum ReadStatus
    {
        /** <summary>Read, from the entry, a write a crash cut off, or the backup (<see cref="ReadResult{T}.Source" />).</summary> */
        Ok,
        /** <summary>Neither the entry nor a backup exists.</summary> */
        Missing,
        /** <summary>Neither the entry nor its backup could be read.</summary> */
        Damaged,
        /** <summary>The entry (or the copy read in its place) was written by a newer build. Nothing was read, and nothing older was tried.</summary> */
        TooNew,
        /**
         * <summary>A file is there but couldn't be read, after retries (another program holds it, a permission). Nothing
         * older was read in its place, since the file may be fine: ask again later.</summary>
         */
        Unreadable,
    }

    /** <summary>Where an entry's value was read from.</summary> */
    public enum ReadSource
    {
        /** <summary>The entry itself.</summary> */
        Entry,
        /** <summary>A whole write that a crash left before it took the entry's place; it now has.</summary> */
        Pending,
        /** <summary>The backup, because the entry was missing or damaged.</summary> */
        Backup,
    }

    /** <summary>The outcome of reading an entry: the value, where it came from, and what went wrong.</summary> */
    public readonly struct ReadResult<T>
    {
        #region Properties

        /** <summary>How reading ended.</summary> */
        public ReadStatus Status { get; }

        /** <summary>The value read; default unless <see cref="ReadStatus.Ok" />.</summary> */
        public T Value { get; }

        /** <summary>Where the value came from.</summary> */
        public ReadSource Source { get; }

        /** <summary>True when the entry was unusable and its backup was read instead.</summary> */
        public bool UsedBackup => Source == ReadSource.Backup;

        /** <summary>What went wrong with the entry, or with both; null when nothing did.</summary> */
        public string Problem { get; }

        #endregion

        #region Public API

        /** <summary>Makes a result.</summary> */
        public ReadResult(ReadStatus status, T value, ReadSource source, string problem)
        {
            Status = status;
            Value = value;
            Source = source;
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
         * JSON nested deeper than <see cref="SaveSerializer.MaxDepth" /> is refused, since it could not be read back. An
         * entry or backup a newer build wrote is never written over (<see cref="SaveStatus.TooNew" />): a newer file
         * format, or what <paramref name="isNewer" /> says of its JSON (a newer document or section version). When the
         * current entry is damaged and its backup is good, the backup is kept as the backup instead of the damage; an
         * entry this build can't read but isn't damaged (another key or transform) is never repaired over.
         * </remarks>
         * <param name="isNewer">Says whether an entry's JSON is from a newer build than this one; null checks the file format only.</param>
         */
        public SaveResult Write(string entry, JToken json, Func<JToken, bool> isNewer = null)
        {
            if (SaveSerializer.DepthOf(json) > SaveSerializer.MaxDepth)
                return new SaveResult(SaveStatus.WriteFailed, $"Writing '{entry}' failed: the data nests deeper than {SaveSerializer.MaxDepth} levels.");
            try
            {
                var newer = NewerThanThisBuild(entry, isNewer);
                if (newer != null) return new SaveResult(SaveStatus.TooNew, $"'{entry}' {newer}; it is kept as it is.");
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
         * <summary>Reads an entry. If it is missing, a whole write a crash left pending is read and moved into its place;
         * if it is damaged, its backup is read. A file a newer build wrote is reported as such and nothing older is tried;
         * so is a file that is there but can't be read (<see cref="ReadStatus.Unreadable" />).</summary>
         * <param name="parse">Turns the JSON into a value, throwing when it is not valid.</param>
         */
        public ReadResult<T> Read<T>(string entry, Func<JToken, T> parse)
        {
            var problems = new List<string>();
            if (!TryRead(() => Storage.Read(entry), out var bytes, out var unreadable))
                return new ReadResult<T>(ReadStatus.Unreadable, default, ReadSource.Entry, $"the entry could not be read: {unreadable}");
            byte[] pending = null;
            if (bytes != null)
            {
                var status = TryDecode(bytes, parse, "the entry", problems, out var value);
                if (status == DecodeStatus.Ok) return new ReadResult<T>(ReadStatus.Ok, value, ReadSource.Entry, null);
                if (status == DecodeStatus.TooNew) return new ReadResult<T>(ReadStatus.TooNew, default, ReadSource.Entry, string.Join("; ", problems));
            }
            else if (!TryRead(() => Storage.ReadPending(entry), out pending, out unreadable))
                return new ReadResult<T>(ReadStatus.Unreadable, default, ReadSource.Pending, $"the entry is missing; a write a crash cut off could not be read: {unreadable}");
            else if (pending != null)
            {
                var status = TryDecode(pending, parse, "a write a crash cut off", problems, out var recovered);
                if (status == DecodeStatus.TooNew) return new ReadResult<T>(ReadStatus.TooNew, default, ReadSource.Pending, string.Join("; ", problems));
                if (status == DecodeStatus.Ok)
                {
                    TryCommitPending(entry);
                    return new ReadResult<T>(ReadStatus.Ok, recovered, ReadSource.Pending, "the entry was missing; the whole write a crash left was used");
                }
            }

            if (!TryRead(() => Storage.ReadBackup(entry), out var backup, out unreadable))
                return new ReadResult<T>(ReadStatus.Unreadable, default, ReadSource.Backup, $"{Joined(problems, "the entry is missing")}; the backup could not be read: {unreadable}");
            if (bytes == null && backup == null && pending == null)
                return new ReadResult<T>(ReadStatus.Missing, default, ReadSource.Entry, null);

            if (bytes == null) problems.Insert(0, "the entry is missing");
            if (backup != null)
            {
                var status = TryDecode(backup, parse, "the backup", problems, out var value);
                if (status == DecodeStatus.Ok) return new ReadResult<T>(ReadStatus.Ok, value, ReadSource.Backup, string.Join("; ", problems));
                if (status == DecodeStatus.TooNew) return new ReadResult<T>(ReadStatus.TooNew, default, ReadSource.Backup, string.Join("; ", problems));
            }

            if (backup == null) problems.Add("there is no backup");
            return new ReadResult<T>(ReadStatus.Damaged, default, ReadSource.Entry, string.Join("; ", problems));
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

        /**
         * <summary>Puts a good backup back in place of a damaged entry, keeping it as the backup, so the write about to
         * come keeps it as the backup instead of the damage. Only real damage is repaired over: an entry another key or
         * transform wrote is left for the write to keep as the backup.</summary>
         */
        private void KeepGoodBackup(string entry)
        {
            var current = Storage.Read(entry);
            if (current == null || !IsDamaged(current)) return;
            var backup = Storage.ReadBackup(entry);
            if (backup != null && IsGood(backup)) Storage.Repair(entry, backup);
        }

        /**
         * <summary>Why the entry, its backup, or (with no entry) a pending write is from a newer build ("was written
         * by..."); null when none is. A file that can't be read throws, so nothing is written over it.</summary>
         */
        private string NewerThanThisBuild(string entry, Func<JToken, bool> isNewer)
        {
            var current = Storage.Read(entry);
            foreach (var file in new[] { current, Storage.ReadBackup(entry), current == null ? Storage.ReadPending(entry) : null })
            {
                if (file == null) continue;
                var frame = SaveFileCodec.Verify(file);
                if (frame == DecodeStatus.TooNew) return "was written in a newer build's file format";
                if (frame == DecodeStatus.Ok && isNewer != null && TryDecode(file, json => json, "the file", new List<string>(), out var json) == DecodeStatus.Ok && isNewer(json))
                    return "was written by a newer build";
            }
            return null;
        }

        /** <summary>True when the file decodes and its JSON parses.</summary> */
        private bool IsGood(byte[] file) => TryDecode(file, json => json, "the file", new List<string>(), out _) == DecodeStatus.Ok;

        /** <summary>True when the file is damaged (empty, cut short, checksum or JSON wrong); false when it is fine or only foreign (another key or transform, a newer build).</summary> */
        private bool IsDamaged(byte[] file)
        {
            switch (SaveFileCodec.Verify(file))
            {
                case DecodeStatus.Ok:
                    return TryDecode(file, json => json, "the file", new List<string>(), out _) == DecodeStatus.BadHeader;
                case DecodeStatus.TooNew:
                case DecodeStatus.UnknownTransform:
                case DecodeStatus.TransformFailed:
                    return false;
                default:
                    return true;
            }
        }

        private void TryCommitPending(string entry)
        {
            try
            {
                Storage.CommitPending(entry);
            }
            catch (Exception)
            {
                // The value was read; the pending write stays where it is and is read again until it can be moved.
            }
        }

        /** <summary>Reads through <paramref name="read" />; false (with why) when it threw.</summary> */
        private static bool TryRead(Func<byte[]> read, out byte[] bytes, out string problem)
        {
            try
            {
                bytes = read();
                problem = null;
                return true;
            }
            catch (Exception e)
            {
                bytes = null;
                problem = e.Message;
                return false;
            }
        }

        private static string Joined(List<string> problems, string fallback) => problems.Count > 0 ? string.Join("; ", problems) : fallback;

        /** <summary>Decodes and parses a file; the status is Ok, TooNew, or another decode failure (a parse failure reads as damaged).</summary> */
        private DecodeStatus TryDecode<T>(byte[] bytes, Func<JToken, T> parse, string what, List<string> problems, out T value)
        {
            value = default;
            var decoded = _codec.Decode(bytes);
            if (decoded.Status != DecodeStatus.Ok)
            {
                problems.Add($"{what}: {decoded.Problem}");
                return decoded.Status;
            }
            try
            {
                value = parse(_serializer.Parse(decoded.Payload));
                return DecodeStatus.Ok;
            }
            catch (Exception e)
            {
                problems.Add($"{what} is not valid: {e.Message}");
                return DecodeStatus.BadHeader;
            }
        }

        #endregion
    }
}
