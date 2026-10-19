namespace SaveSystem
{
    /**
     * <summary>A ScriptableObject a save can point at. It holds one serialized <see cref="AssetId" /> field, which the
     * editor fills in and keeps unique.</summary>
     */
    public interface ISaveableAsset
    {
        /** <summary>The asset's id. Empty until the editor assigns it.</summary> */
        AssetId AssetId { get; }
    }
}
