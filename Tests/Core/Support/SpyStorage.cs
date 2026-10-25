using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SaveSystem.Tests
{
    /** <summary>Storage over memory that counts reads, can fail reads, writes, repairs and listing, and can hold a read or a write until released.</summary> */
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
        public Func<string, bool> FailRepair { get; set; } = _ => false;
        public Func<string, bool> FailRead { get; set; } = _ => false;
        public bool FailReadPending { get; set; }
        public bool FailList { get; set; }
        public ManualResetEventSlim ReadGate { get; } = new ManualResetEventSlim(true);
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
            if (!ReadGate.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("read gate never opened");
            Count(name);
            if (FailRead(name)) throw new IOException("held by another program");
            return Inner.Read(name);
        }

        public byte[] ReadBackup(string name)
        {
            Count(name);
            return Inner.ReadBackup(name);
        }

        public byte[] ReadPending(string name)
        {
            Count(name);
            if (FailReadPending) throw new IOException("held by another program");
            return Inner.ReadPending(name);
        }

        public void Write(string name, byte[] data)
        {
            WriteStarted.Set();
            if (!WriteGate.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("write gate never opened");
            if (FailWrite(name)) throw new IOException("disk full");
            Inner.Write(name, data);
        }

        public void CommitPending(string name) => Inner.CommitPending(name);

        public void Repair(string name, byte[] data)
        {
            if (FailRepair(name)) throw new IOException("disk full");
            Inner.Repair(name, data);
        }

        public void Delete(string name) => Inner.Delete(name);

        public IReadOnlyList<string> List() => FailList ? throw new IOException("listing broke") : Inner.List();

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
