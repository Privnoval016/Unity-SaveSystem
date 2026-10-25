namespace SaveSystem
{
    /**
     * <summary>A section with state derived from other sections' (a slot count raised by another system's bonus, a
     * maximum that clamps a value): its restore takes the saved values as they are, and <see cref="Settle" /> fits them
     * once every section is back.</summary>
     * <remarks>
     * Settle runs after a load, a new game and a section registering with held data, on every registered section that
     * implements it, in registration order. A throw counts as a failed restore: a load is rolled back.
     * </remarks>
     */
    public interface ISettlesAfterRestore
    {
        /** <summary>Fits restored values to what the other sections now say.</summary> */
        void Settle();
    }
}
