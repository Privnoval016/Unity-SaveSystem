using System;

namespace SaveSystem
{
    /** <summary>How slots are named in storage: <c>&lt;slot&gt;.save</c> for the save and <c>&lt;slot&gt;.head</c> for its header.</summary> */
    public static class SlotEntries
    {
        #region Properties

        /** <summary>The suffix of a slot's save.</summary> */
        public const string SaveSuffix = ".save";

        /** <summary>The suffix of a slot's header.</summary> */
        public const string HeaderSuffix = ".head";

        #endregion

        #region Public API

        /** <summary>The entry a slot's save is stored in.</summary> */
        public static string Save(SlotId slot) => slot.Name + SaveSuffix;

        /** <summary>The entry a slot's header is stored in.</summary> */
        public static string Header(SlotId slot) => slot.Name + HeaderSuffix;

        /** <summary>The header entry beside a save entry; null when the entry isn't a slot's save.</summary> */
        public static string HeaderBeside(string saveEntry) =>
            TryParseSave(saveEntry, out var slot) ? Header(slot) : null;

        /** <summary>Reads the slot a save entry belongs to.</summary> */
        public static bool TryParseSave(string entry, out SlotId slot)
        {
            slot = default;
            return entry != null && entry.EndsWith(SaveSuffix, StringComparison.Ordinal)
                && SlotId.TryParse(entry.Substring(0, entry.Length - SaveSuffix.Length), out slot);
        }

        /** <summary>True for a header entry.</summary> */
        public static bool IsHeader(string entry) => entry != null && entry.EndsWith(HeaderSuffix, StringComparison.Ordinal);

        #endregion
    }
}
