using UnityEngine;

namespace SaveSystem.Tests
{
    /** <summary>A saveable asset type for tests.</summary> */
    public sealed class OtherSaveableAsset : ScriptableObject, ISaveableAsset
    {
        [Tooltip("The asset's id.")]
        [SerializeField] private AssetId assetId;

        public AssetId AssetId => assetId;

        public void SetId(AssetId id) => assetId = id;
    }
}
