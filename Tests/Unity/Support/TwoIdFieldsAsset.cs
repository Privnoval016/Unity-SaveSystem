using UnityEngine;

namespace SaveSystem.Tests
{
    /** <summary>A saveable asset with two id fields, which the audit must reject.</summary> */
    public sealed class TwoIdFieldsAsset : ScriptableObject, ISaveableAsset
    {
        [Tooltip("The asset's id.")]
        [SerializeField] private AssetId assetId;

        [Tooltip("A second id, which is not allowed.")]
        [SerializeField] private AssetId otherId;

        public AssetId AssetId => assetId;
    }
}
