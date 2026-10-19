using System;
using System.Collections.Generic;
using System.Linq;

namespace SaveSystem.EditorTools
{
    /** <summary>One saveable asset as the id planner sees it.</summary> */
    public readonly struct AssetIdRecord
    {
        #region Properties

        /** <summary>The asset's path; sub-assets share their file's path.</summary> */
        public string Path { get; }

        /** <summary>The file's GUID, 32 hex digits.</summary> */
        public string FileGuid { get; }

        /** <summary>True for the file's main asset; sub-assets can't use the file's GUID as their id.</summary> */
        public bool IsMainAsset { get; }

        /** <summary>The id the asset holds now; may be empty.</summary> */
        public AssetId Current { get; }

        /** <summary>True when the asset was just imported (created, duplicated or changed).</summary> */
        public bool JustImported { get; }

        #endregion

        #region Public API

        /** <summary>Makes a record.</summary> */
        public AssetIdRecord(string path, string fileGuid, bool isMainAsset, AssetId current, bool justImported)
        {
            Path = path;
            FileGuid = fileGuid;
            IsMainAsset = isMainAsset;
            Current = current;
            JustImported = justImported;
        }

        #endregion
    }

    /**
     * <summary>Decides which saveable assets need a new id: those without one, and every copy of a shared id but one.</summary>
     * <remarks>
     * A stored id is the authority and never changes unless shared. A new id is the file's GUID when that is free, so
     * ids match what the editor shows; otherwise a random one. Of assets sharing an id, the keeper is the one whose
     * GUID it is, then one that was already there (not just imported), then the first by path.
     * </remarks>
     */
    public static class AssetIdPlanner
    {
        /** <summary>Returns, by index into <paramref name="records" />, each asset that needs a new id and the id it gets.</summary> */
        public static IReadOnlyList<KeyValuePair<int, AssetId>> Plan(IReadOnlyList<AssetIdRecord> records, Func<AssetId> newRandomId = null)
        {
            newRandomId = newRandomId ?? AssetId.New;
            var taken = new HashSet<AssetId>(records.Where(record => !record.Current.IsEmpty).Select(record => record.Current));
            var changes = new List<KeyValuePair<int, AssetId>>();

            foreach (var index in NeedingIds(records))
            {
                var id = PickId(records[index], taken, newRandomId);
                taken.Add(id);
                changes.Add(new KeyValuePair<int, AssetId>(index, id));
            }
            return changes;
        }

        private static IEnumerable<int> NeedingIds(IReadOnlyList<AssetIdRecord> records)
        {
            var needing = new List<int>();
            for (var i = 0; i < records.Count; i++)
                if (records[i].Current.IsEmpty) needing.Add(i);

            var groups = Enumerable.Range(0, records.Count)
                .Where(i => !records[i].Current.IsEmpty)
                .GroupBy(i => records[i].Current)
                .Where(group => group.Count() > 1);
            foreach (var group in groups)
            {
                var keeper = PickKeeper(records, group.ToList());
                needing.AddRange(group.Where(i => i != keeper));
            }

            return needing.OrderBy(i => records[i].Path, StringComparer.Ordinal).ThenBy(i => i);
        }

        private static int PickKeeper(IReadOnlyList<AssetIdRecord> records, List<int> sharing)
        {
            var ordered = sharing.OrderBy(i => records[i].Path, StringComparer.Ordinal).ThenBy(i => i).ToList();
            foreach (var i in ordered)
                if (records[i].IsMainAsset && string.Equals(records[i].Current.Value, records[i].FileGuid, StringComparison.Ordinal)) return i;
            foreach (var i in ordered)
                if (!records[i].JustImported) return i;
            return ordered[0];
        }

        private static AssetId PickId(AssetIdRecord record, HashSet<AssetId> taken, Func<AssetId> newRandomId)
        {
            if (record.IsMainAsset && AssetId.TryParse(record.FileGuid, out var fromGuid) && !taken.Contains(fromGuid)) return fromGuid;
            AssetId id;
            do id = newRandomId();
            while (taken.Contains(id));
            return id;
        }
    }
}
