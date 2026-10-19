namespace SaveSystem
{
    /**
     * <summary>A component on a <see cref="SaveableEntity" /> whose state is saved: a chest's lid, a door, a lever.</summary>
     * <remarks>It is a section of its entity's own small save, so it has a key, a version and upgrades like any section.</remarks>
     */
    public interface ISaveableComponent : ISaveSection { }
}
