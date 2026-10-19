using System;

namespace SaveSystem
{
    /** <summary>The rules for section keys and storage entry names, which are written to disk and never change.</summary> */
    public static class SaveNames
    {
        #region Properties

        /** <summary>The longest key or entry name allowed.</summary> */
        public const int MaxLength = 64;

        /** <summary>The suffix of a write in progress.</summary> */
        public const string TempSuffix = ".tmp";

        /** <summary>The suffix of an entry's previous bytes.</summary> */
        public const string BackupSuffix = ".bak";

        #endregion

        #region Public API

        /** <summary>Lower-case letters, digits, '.', '_', '-' and '/' between non-empty parts; at most <see cref="MaxLength" />.</summary> */
        public static bool IsValidSectionKey(string key)
        {
            if (!HasValidLength(key) || key[0] == '/' || key[key.Length - 1] == '/' || key.Contains("//")) return false;
            foreach (var c in key)
                if (!IsNameCharacter(c) && c != '/') return false;
            return true;
        }

        /**
         * <summary>Lower-case letters, digits, '.', '_' and '-', not starting with '.', not ending in a reserved suffix
         * (<c>.tmp</c>, <c>.bak</c>); at most <see cref="MaxLength" />.</summary>
         */
        public static bool IsValidEntryName(string name)
        {
            if (!HasValidLength(name) || name[0] == '.' || name.Contains("..") || HasReservedSuffix(name)) return false;
            foreach (var c in name)
                if (!IsNameCharacter(c)) return false;
            return true;
        }

        /** <summary>True for the suffixes storage uses for its own temporary and backup files.</summary> */
        public static bool HasReservedSuffix(string name) =>
            name.EndsWith(TempSuffix, StringComparison.Ordinal) || name.EndsWith(BackupSuffix, StringComparison.Ordinal);

        #endregion

        #region Helpers

        private static bool HasValidLength(string value) => !string.IsNullOrEmpty(value) && value.Length <= MaxLength;

        private static bool IsNameCharacter(char c) =>
            (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';

        #endregion
    }
}
