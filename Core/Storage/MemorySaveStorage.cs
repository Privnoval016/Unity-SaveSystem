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

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public byte[] Read(string name) => Get(_entries, name);

        /** <inheritdoc /> */
        public byte[] ReadBackup(string name) => Get(_backups, name);

        /** <inheritdoc /> */
        public void Write(string name, byte[] data)
        {
            Check(name);
            lock (_lock)
            {
                if (_entries.TryGetValue(name, out var previous)) _backups[name] = previous;
                _entries[name] = (byte[])data.Clone();
            }
        }

        /** <inheritdoc /> */
        public void Delete(string name)
        {
            Check(name);
            lock (_lock)
            {
                _entries.Remove(name);
                _backups.Remove(name);
            }
        }

        /** <inheritdoc /> */
        public IReadOnlyList<string> List()
        {
            lock (_lock)
                return _entries.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        /** <summary>Replaces an entry's bytes directly, keeping no backup: for simulating damage in tests.</summary> */
        public void Overwrite(string name, byte[] data)
        {
            Check(name);
            lock (_lock)
                _entries[name] = (byte[])data.Clone();
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
