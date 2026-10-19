using Object = UnityEngine.Object;

namespace SaveSystem
{
    /** <summary>Finds a saveable asset from its id, in a build as well as in the editor.</summary> */
    public interface IAssetResolver
    {
        /** <summary>Finds the asset with this id.</summary> <returns>False when no asset has it.</returns> */
        bool TryFind(AssetId id, out Object asset);
    }
}
