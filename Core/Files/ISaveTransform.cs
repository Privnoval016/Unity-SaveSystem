namespace SaveSystem
{
    /** <summary>A reversible step applied to a save's bytes on the way to disk, such as compression or encryption.</summary> */
    public interface ISaveTransform
    {
        /** <summary>The name written in the file's header so a reader knows to reverse it. It must never change.</summary> */
        string Id { get; }

        /** <summary>Transforms bytes for writing.</summary> */
        byte[] Apply(byte[] data);

        /** <summary>Reverses <see cref="Apply" />. Throws when the bytes were not made by it.</summary> */
        byte[] Revert(byte[] data);
    }
}
