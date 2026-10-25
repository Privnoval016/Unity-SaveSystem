namespace SaveSystem
{
    /** <summary>What happens to an asset reference that can't be saved or found again.</summary> */
    public enum UnresolvedAssets
    {
        /**
         * <summary>The save or load fails: an asset with no id refuses the save (the last save is kept), and an id with
         * no asset, or one of another type, fails its section's load (nothing changes). Nothing is lost silently.</summary>
         */
        Fail,
        /**
         * <summary>Saved and loaded as null, with one warning each. The next save then writes null, so the id is gone:
         * only for data where losing a reference is acceptable.</summary>
         */
        LoadAsNull,
    }
}
