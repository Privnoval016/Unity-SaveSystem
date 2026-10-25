using System;
using System.Collections.Generic;
using System.Linq;

namespace SaveSystem
{
    /** <summary>Keeps entries in memory, for tests and for platforms with nowhere to write.</summary> */
    public sealed class MemorySaveStorage : ISaveStorage
    {
        #region Private Fields

        private readonly object _lock = new object();

        /** <summary>Keyed by entry name, resolving to its current bytes.</summary> */
        private readonly Dictionary<string, byte[]> _entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /** <summary>Keyed by entry name, resolving to the bytes it held before its last write.</summary> */
        private readonly Dictionary<string, byte[]> _backups = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /** <summary>Keyed by entry name, resolving to a write cut off before it took the entry's place (set by tests).</summary> */
        private readonly Dictionary<string, byte[]> _pending = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public byte[] Read(string name) => Get(_entries, name);

        /** <inheritdoc /> */
        public byte[] ReadBackup(string name) => Get(_backups, name);

        /** <inheritdoc /> */
        public byte[] ReadPending(string name) => Get(_pending, name);

        /** <inheritdoc /> */
        public void Write(string name, byte[] data)
        {
            Check(name);
            lock (_lock)
            {
                if (_entries.TryGetValue(name, out var previous)) _backups[name] = previous;
                _entries[name] = (byte[])data.Clone();
                _pending.Remove(name);
            }
        }

        /** <inheritdoc /> */
        public void CommitPending(string name)
        {
            Check(name);
            lock (_lock)
            {
                if (_entries.ContainsKey(name) || !_pending.TryGetValue(name, out var pending)) return;
                _entries[name] = pending;
                _pending.Remove(name);
            }
        }

        /** <inheritdoc /> */
        public void Repair(string name, byte[] data)
        {
            Check(name);
            lock (_lock)
            {
                _entries[name] = (byte[])data.Clone();
                _pending.Remove(name);
            }
        }

        /** <inheritdoc /> */
        public void Delete(string name)
        {
            Check(name);
            lock (_lock)
            {
                _backups.Remove(name);
                _pending.Remove(name);
                _entries.Remove(name);
            }
        }

        /** <inheritdoc /> */
        public IReadOnlyList<string> List()
        {
            lock (_lock)
                return _entries.Keys.Union(_backups.Keys).Union(_pending.Keys).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        /** <summary>Replaces an entry's bytes directly, keeping no backup: for simulating damage in tests.</summary> */
        internal void Overwrite(string name, byte[] data)
        {
            Check(name);
            lock (_lock)
                _entries[name] = (byte[])data.Clone();
        }

        /**
         * <summary>Leaves the storage as a crash between a write's two moves would on disk: the entry gone, its bytes
         * the backup, and <paramref name="data" /> as the pending write. For tests.</summary>
         */
        internal void CrashMidWrite(string name, byte[] data)
        {
            Check(name);
            lock (_lock)
            {
                if (_entries.TryGetValue(name, out var previous)) _backups[name] = previous;
                _entries.Remove(name);
                _pending[name] = (byte[])data.Clone();
            }
        }

        /** <summary>Leaves <paramref name="data" /> as a pending write beside whatever the entry holds. For tests.</summary> */
        internal void LeavePending(string name, byte[] data)
        {
            Check(name);
            lock (_lock)
                _pending[name] = (byte[])data.Clone();
        }

        /** <summary>Removes the entry, its bytes becoming the backup, as a crash between a write's two moves leaves it. For tests.</summary> */
        internal void LoseEntry(string name)
        {
            Check(name);
            lock (_lock)
            {
                if (!_entries.TryGetValue(name, out var current)) return;
                _backups[name] = current;
                _entries.Remove(name);
            }
        }

        #endregion

        #region Helpers

        private byte[] Get(Dictionary<string, byte[]> source, string name)
        {
            Check(name);
            lock (_lock)
                return source.TryGetValue(name, out var data) ? (byte[])data.Clone() : null;
        }

        private static void Check(string name)
        {
            if (!SaveNames.IsValidEntryName(name)) throw new ArgumentException($"Invalid entry name '{name}'.", nameof(name));
        }

        #endregion
    }
}
