using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /**
     * <summary>One system's part of a save: a stable key, a version, and how its state is captured and restored.</summary>
     * <remarks>
     * Sections restore in the order they were registered, so the order lives in one place: the code that registers
     * them. Most systems derive from <see cref="SaveSection{TData}" /> rather than implementing this directly.
     * </remarks>
     */
    public interface ISaveSection
    {
        /** <summary>The section's name in the file. It must never change once saves exist.</summary> */
        string Key { get; }

        /** <summary>The version of the data this section writes, from 1.</summary> */
        int Version { get; }

        /** <summary>The steps that bring older data up to <see cref="Version" />, one per older version.</summary> */
        IReadOnlyList<SectionUpgrade> Upgrades { get; }

        /** <summary>Captures the current state as JSON. Called on the main thread.</summary> */
        JToken Capture(JsonSerializer serializer);

        /**
         * <summary>Reads saved data, already at <see cref="Version" />, without touching live state.</summary>
         * <returns>A restore to apply once every section has been read successfully.</returns>
         */
        IPreparedRestore Prepare(JToken data, JsonSerializer serializer);

        /** <summary>Prepares the state a new game starts with, for when a save has no data for this section.</summary> */
        IPreparedRestore PrepareDefault();
    }

    /** <summary>Section data that has been read and checked, waiting to be applied to live state.</summary> */
    public interface IPreparedRestore
    {
        /** <summary>Applies the data to live state.</summary> */
        void Apply();
    }
}
