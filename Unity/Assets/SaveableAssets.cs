using UnityEngine;

namespace SaveSystem
{
    /** <summary>The one place that asks whether an asset is saveable, and reads its id.</summary> */
    public static class SaveableAssets
    {
        /** <summary>True for a live asset that implements <see cref="ISaveableAsset" />.</summary> */
        public static bool IsSaveable(Object asset) => asset != null && asset is ISaveableAsset;

        /** <summary>The asset's id; empty when it has none or isn't saveable.</summary> */
        public static AssetId IdOf(Object asset) => IsSaveable(asset) ? ((ISaveableAsset)asset).AssetId : default;

        /** <summary>Reads the asset's id when it is a live saveable asset with an id.</summary> */
        public static bool TryGetId(Object asset, out AssetId id)
        {
            id = IdOf(asset);
            return !id.IsEmpty;
        }
    }
}
