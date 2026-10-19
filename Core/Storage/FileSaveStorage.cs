using System;
using System.Collections.Generic;
using System.IO;

namespace SaveSystem
{
    /** <summary>Stores each entry as a file in one folder, written through a temporary file and keeping a <c>.bak</c>.</summary> */
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

            var movedToBackup = false;
            if (File.Exists(path))
            {
                DeleteIfExists(backup);
                File.Move(path, backup);
                movedToBackup = true;
            }
            try
            {
                File.Move(temp, path);
            }
            catch (Exception)
            {
                if (movedToBackup && !File.Exists(path)) File.Move(backup, path);
                throw;
            }
        }

        /** <inheritdoc /> */
        public void Delete(string name)
        {
            var path = PathOf(name);
            DeleteIfExists(path);
            DeleteIfExists(path + SaveNames.BackupSuffix);
            DeleteIfExists(path + SaveNames.TempSuffix);
        }

        /** <inheritdoc /> <remarks>An entry left with only its backup, by a write cut short, is listed too.</remarks> */
        public IReadOnlyList<string> List()
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            if (!Directory.Exists(Root)) return new List<string>();
            foreach (var path in Directory.GetFiles(Root))
            {
                var name = Path.GetFileName(path);
                if (name.EndsWith(SaveNames.BackupSuffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - SaveNames.BackupSuffix.Length);
                if (SaveNames.IsValidEntryName(name)) names.Add(name);
            }
            return new List<string>(names);
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
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }

        #endregion
    }
}
