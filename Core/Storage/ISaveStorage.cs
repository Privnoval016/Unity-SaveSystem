using System.Collections.Generic;

namespace SaveSystem
{
    /**
     * <summary>Where saved bytes live, by entry name. Files today; a console's or a cloud's storage later.</summary>
     * <remarks>
     * Entry names follow <see cref="SaveNames.IsValidEntryName" />. Calls may come from a background thread, but never
     * two at once for the same entry.
     * </remarks>
     */
    public interface ISaveStorage
    {
        /** <summary>The entry's bytes, or null when it doesn't exist.</summary> */
        byte[] Read(string name);

        /** <summary>The bytes the entry held before its last write, or null when there are none.</summary> */
        byte[] ReadBackup(string name);

        /**
         * <summary>Replaces the entry's bytes. After a crash at any point the entry holds either the old bytes or the new,
         * and the old bytes are kept as the backup.</summary>
         */
        void Write(string name, byte[] data);

        /** <summary>Removes the entry, its backup and anything a failed write left behind. Does nothing when there is none.</summary> */
        void Delete(string name);

        /** <summary>Every entry's name. Backups and leftovers are never listed.</summary> */
        IReadOnlyList<string> List();
    }
}
