using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SaveSystem.Tests
{
    /** <summary>Storage over memory that counts reads, can fail writes, and can hold a write until released.</summary> */
    public sealed class SpyStorage : ISaveStorage
    {
        #region Private Fields

        private readonly object _lock = new object();

        /** <summary>Keyed by entry name, resolving to how many times it was read.</summary> */
        private readonly Dictionary<string, int> _reads = new Dictionary<string, int>(StringComparer.Ordinal);

        #endregion

        #region Properties

        public MemorySaveStorage Inner { get; } = new MemorySaveStorage();
        public Func<string, bool> FailWrite { get; set; } = _ => false;
        public ManualResetEventSlim WriteGate { get; } = new ManualResetEventSlim(true);
        public ManualResetEventSlim WriteStarted { get; } = new ManualResetEventSlim(false);

        #endregion

        #region Public API

        public int ReadsOf(string name)
        {
            lock (_lock)
                return _reads.TryGetValue(name, out var count) ? count : 0;
        }

        public int ReadsEndingWith(string suffix)
        {
            var total = 0;
            lock (_lock)
                foreach (var pair in _reads)
                    if (pair.Key.EndsWith(suffix, StringComparison.Ordinal)) total += pair.Value;
            return total;
        }

        public byte[] Read(string name)
        {
            Count(name);
            return Inner.Read(name);
        }

        public byte[] ReadBackup(string name)
        {
            Count(name);
            return Inner.ReadBackup(name);
        }

        public void Write(string name, byte[] data)
        {
            WriteStarted.Set();
            WriteGate.Wait();
            if (FailWrite(name)) throw new IOException("disk full");
            Inner.Write(name, data);
        }

        public void Delete(string name) => Inner.Delete(name);

        public IReadOnlyList<string> List() => Inner.List();

        #endregion

        #region Helpers

        private void Count(string name)
        {
            lock (_lock)
                _reads[name] = ReadsOf(name) + 1;
        }

        #endregion
    }
}
