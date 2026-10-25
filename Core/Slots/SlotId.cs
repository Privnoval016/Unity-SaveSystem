using System;
using System.Globalization;

namespace SaveSystem
{
    /** <summary>A save slot's name: a numbered manual slot, a numbered autosave, or a named slot such as a quicksave.</summary> */
    public readonly struct SlotId : IEquatable<SlotId>
    {
        #region Private Fields

        private const string ManualPrefix = "manual-";
        private const string AutoPrefix = "auto-";
        private const int MaxNameLength = 32;

        #endregion

        #region Properties

        /** <summary>The highest slot number.</summary> */
        public const int MaxNumber = 999;

        /** <summary>The slot's name on disk.</summary> */
        public string Name { get; }

        /** <summary>True for an autosave slot.</summary> */
        public bool IsAuto => Name != null && Name.StartsWith(AutoPrefix, StringComparison.Ordinal);

        #endregion

        #region Public API

        /** <summary>Manual slot <paramref name="number" />, from 1.</summary> */
        public static SlotId Manual(int number) => new SlotId(ManualPrefix + FormatNumber(number));

        /** <summary>Autosave slot <paramref name="number" />, from 1.</summary> */
        public static SlotId Auto(int number) => new SlotId(AutoPrefix + FormatNumber(number));

        /**
         * <summary>A slot with its own name: lower-case letters, digits and '-', at most 32 characters, not a name Windows reserves.</summary>
         * <exception cref="ArgumentException">The name is invalid or starts with the autosave prefix.</exception>
         */
        public static SlotId Named(string name)
        {
            if (!TryParse(name, out var slot) || slot.IsAuto) throw new ArgumentException($"Invalid slot name '{name}'.", nameof(name));
            return slot;
        }

        /** <summary>Reads a slot from its name on disk.</summary> */
        public static bool TryParse(string name, out SlotId slot)
        {
            slot = default;
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || !SaveNames.IsValidEntryName(name)) return false;
            foreach (var c in name)
                if (c == '.' || c == '_') return false;
            slot = new SlotId(name);
            return true;
        }

        /** <summary>The number of a numbered manual slot or autosave; false for a named slot.</summary> */
        public bool TryGetNumber(out int number)
        {
            number = 0;
            var prefix = IsAuto ? AutoPrefix : ManualPrefix;
            if (Name == null || !Name.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var digits = Name.Substring(prefix.Length);
            if (digits.Length == 0) return false;
            foreach (var c in digits)
                if (c < '0' || c > '9') return false;
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 1 && number <= MaxNumber;
        }

        /** <inheritdoc /> */
        public bool Equals(SlotId other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

        /** <inheritdoc /> */
        public override bool Equals(object obj) => obj is SlotId other && Equals(other);

        /** <inheritdoc /> */
        public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);

        /** <inheritdoc /> */
        public override string ToString() => Name ?? "(none)";

        #endregion

        #region Helpers

        private SlotId(string name) => Name = name;

        private static string FormatNumber(int number)
        {
            if (number < 1 || number > MaxNumber) throw new ArgumentOutOfRangeException(nameof(number), $"Slot numbers run from 1 to {MaxNumber}.");
            return number.ToString("00", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
