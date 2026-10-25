using System;
using System.Collections.Generic;
using System.IO;

namespace SaveSystem
{
    /**
     * <summary>Stores each entry as a file in one folder. A write goes to a temporary file flushed to disk, which then
     * replaces the entry while the entry becomes the <c>.bak</c>; a file another program holds open for a moment (a
     * virus scanner, a sync client) is retried.</summary>
     */
    public sealed class FileSaveStorage : ISaveStorage
    {
        #region Private Fields

        /** <summary>How long to wait before each retry of a file operation another program blocked, in milliseconds.</summary> */
        internal static readonly int[] DefaultRetryDelays = { 25, 100, 300 };

        private readonly int[] _retryDelays;
        private readonly Action<int> _wait;

        #endregion

        #region Properties

        /** <summary>The folder entries are stored in. Created on the first write.</summary> */
        public string Root { get; }

        #endregion

        #region Public API

        /** <summary>Stores entries in the given folder.</summary> */
        public FileSaveStorage(string root) : this(root, DefaultRetryDelays, null) { }

        /** <summary>Stores entries in the given folder, retrying blocked file operations after these delays, waiting with <paramref name="wait" /> (tests).</summary> */
        internal FileSaveStorage(string root, int[] retryDelays, Action<int> wait)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("A storage folder is required.", nameof(root));
            Root = Path.GetFullPath(root);
            _retryDelays = retryDelays ?? DefaultRetryDelays;
            _wait = wait;
        }

        /** <inheritdoc /> */
        public byte[] Read(string name) => ReadIfExists(PathOf(name));

        /** <inheritdoc /> */
        public byte[] ReadBackup(string name) => ReadIfExists(PathOf(name) + SaveNames.BackupSuffix);

        /** <inheritdoc /> */
        public byte[] ReadPending(string name) => ReadIfExists(PathOf(name) + SaveNames.TempSuffix);

        /** <inheritdoc /> */
        public void CommitPending(string name)
        {
            var path = PathOf(name);
            var temp = path + SaveNames.TempSuffix;
            Retried(() =>
            {
                if (!File.Exists(path) && File.Exists(temp)) File.Move(temp, path);
            });
        }

        /** <inheritdoc /> */
        public void Write(string name, byte[] data) => Put(PathOf(name), data, true);

        /** <inheritdoc /> */
        public void Repair(string name, byte[] data) => Put(PathOf(name), data, false);

        /** <inheritdoc /> */
        public void Delete(string name)
        {
            var path = PathOf(name);
            Retried(() => DeleteIfExists(path + SaveNames.BackupSuffix));
            Retried(() => DeleteIfExists(path + SaveNames.TempSuffix));
            Retried(() => DeleteIfExists(path));
        }

        /** <inheritdoc /> */
        public IReadOnlyList<string> List()
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            if (!Directory.Exists(Root)) return new List<string>();
            foreach (var path in Directory.GetFiles(Root))
            {
                var name = Path.GetFileName(path);
                if (name.EndsWith(SaveNames.BackupSuffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - SaveNames.BackupSuffix.Length);
                else if (name.EndsWith(SaveNames.TempSuffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - SaveNames.TempSuffix.Length);
                if (SaveNames.IsValidEntryName(name)) names.Add(name);
            }
            return new List<string>(names);
        }

        #endregion

        #region Helpers

        /** <summary>Runs <paramref name="work" />, retrying it after this storage's delays while another program blocks the file.</summary> */
        private void Retried(Action work) => Retry.Run(work, _retryDelays, _wait);

        /** <summary>As <see cref="Retried(Action)" />, returning what <paramref name="work" /> gives.</summary> */
        private T Retried<T>(Func<T> work) => Retry.Run(work, _retryDelays, _wait);

        /**
         * <summary>Writes <paramref name="data" /> to the temporary file, flushed to disk, then puts it in the entry's
         * place: the entry becoming the backup when <paramref name="keepAsBackup" />, the backup untouched otherwise. A
         * failure leaves the entry and backup as they were and removes the temporary file, except a whole one when a
         * swap failed halfway (the entry already moved to the backup): it is then the newest copy, read as pending.</summary>
         */
        private void Put(string path, byte[] data, bool keepAsBackup)
        {
            var temp = path + SaveNames.TempSuffix;
            var whole = false;
            var hadEntry = File.Exists(path);
            Directory.CreateDirectory(Root);
            try
            {
                Retried(() =>
                {
                    using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(data, 0, data.Length);
                        stream.Flush(true);
                    }
                });
                whole = true;
                Retried(() => Swap(temp, path, keepAsBackup ? path + SaveNames.BackupSuffix : null));
            }
            catch (Exception)
            {
                var movedAway = whole && hadEntry && keepAsBackup && !File.Exists(path) && File.Exists(path + SaveNames.BackupSuffix);
                if (!movedAway) TryDelete(temp);
                throw;
            }
        }

        /**
         * <summary>Puts the temporary file in the entry's place, deciding afresh on each try whether the entry is there (a
         * swap that failed halfway may have moved it). Where <see cref="File.Replace(string, string, string, bool)" /> isn't
         * supported, the entry is copied to the backup before it is removed, so a crash still leaves a copy.</summary>
         */
        private static void Swap(string temp, string path, string backup)
        {
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }
            try
            {
                File.Replace(temp, path, backup, true);
            }
            catch (Exception e) when (e is NotSupportedException || e is PlatformNotSupportedException)
            {
                if (backup != null) File.Copy(path, backup, true);
                File.Delete(path);
                File.Move(temp, path);
            }
        }

        private string PathOf(string name)
        {
            if (!SaveNames.IsValidEntryName(name)) throw new ArgumentException($"Invalid entry name '{name}'.", nameof(name));
            return Path.Combine(Root, name);
        }

        private byte[] ReadIfExists(string path) => Retried(() => File.Exists(path) ? File.ReadAllBytes(path) : null);

        private static void TryDelete(string path)
        {
            try
            {
                DeleteIfExists(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }

        #endregion
    }
}
