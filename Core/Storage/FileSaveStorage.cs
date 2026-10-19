using System;
using System.Collections.Generic;
using System.IO;

namespace SaveSystem
{
    /**
     * <summary>Stores each entry as a file in one folder.</summary>
     * <remarks>
     * A write goes to <c>name.tmp</c>, flushed to disk; the current file then becomes <c>name.bak</c> and the temporary
     * file takes its place. A crash before the swap leaves the old file; a crash during it leaves the backup.
     * </remarks>
     */
    public sealed class FileSaveStorage : ISaveStorage
    {
        #region Properties

        /** <summary>The folder entries are stored in. Created on the first write.</summary> */
        public string Root { get; }

        #endregion

        #region Public API

        /** <summary>Stores entries in the given folder.</summary> */
        public FileSaveStorage(string root)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("A storage folder is required.", nameof(root));
            Root = Path.GetFullPath(root);
        }

        /** <inheritdoc /> */
        public byte[] Read(string name) => ReadIfExists(PathOf(name));

        /** <inheritdoc /> */
        public byte[] ReadBackup(string name) => ReadIfExists(PathOf(name) + SaveNames.BackupSuffix);

        /** <inheritdoc /> */
        public void Write(string name, byte[] data)
        {
            var path = PathOf(name);
            var temp = path + SaveNames.TempSuffix;
            var backup = path + SaveNames.BackupSuffix;

            Directory.CreateDirectory(Root);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(path, backup);
            }
            File.Move(temp, path);
        }

        /** <inheritdoc /> */
        public void Delete(string name)
        {
            var path = PathOf(name);
            DeleteIfExists(path);
            DeleteIfExists(path + SaveNames.BackupSuffix);
            DeleteIfExists(path + SaveNames.TempSuffix);
        }

        /** <inheritdoc /> */
        public IReadOnlyList<string> List()
        {
            var names = new List<string>();
            if (!Directory.Exists(Root)) return names;
            foreach (var path in Directory.GetFiles(Root))
            {
                var name = Path.GetFileName(path);
                if (SaveNames.IsValidEntryName(name)) names.Add(name);
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        #endregion

        #region Helpers

        private string PathOf(string name)
        {
            if (!SaveNames.IsValidEntryName(name)) throw new ArgumentException($"Invalid entry name '{name}'.", nameof(name));
            return Path.Combine(Root, name);
        }

        private static byte[] ReadIfExists(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        #endregion
    }
}
