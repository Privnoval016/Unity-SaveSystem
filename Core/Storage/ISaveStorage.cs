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
         * <summary>The bytes of a write that was cut off before it took the entry's place, or null when there are none.
         * They may be whole or partial; the reader checks them.</summary>
         */
        byte[] ReadPending(string name);

        /**
         * <summary>Replaces the entry's bytes, keeping the old bytes as the backup. After a crash at any point the entry
         * or its backup holds the old bytes, and the entry or the pending write holds the new; the old backup is never
         * removed before the new one is in place.</summary>
         */
        void Write(string name, byte[] data);

        /**
         * <summary>Puts a pending write in the missing entry's place as it is, without rewriting it (the bytes may be the
         * only copy of the newest save). Does nothing when there is no pending write or the entry exists.</summary>
         */
        void CommitPending(string name);

        /**
         * <summary>Replaces the entry's bytes and leaves its backup as it is: for putting a good backup back over a
         * damaged entry without losing the backup. Crash safe as <see cref="Write" /> is.</summary>
         */
        void Repair(string name, byte[] data);

        /**
         * <summary>Removes the entry, its backup and any pending write, the entry last, so a delete that fails partway
         * leaves the entry readable rather than bringing an older backup back. Does nothing when there is none.</summary>
         */
        void Delete(string name);

        /**
         * <summary>Every entry's name: an entry left with only its backup or only a pending write included, each name
         * once. Backup and pending names themselves are never listed.</summary>
         */
        IReadOnlyList<string> List();
    }
}
