using System;
using System.Collections.Generic;

namespace SaveSystem
{
    /** <summary>How a load ended.</summary> */
    public enum LoadStatus
    {
        /** <summary>Every section was restored.</summary> */
        Loaded,
        /** <summary>There is no save by that name.</summary> */
        Missing,
        /** <summary>The save and its backup are damaged or unreadable.</summary> */
        Damaged,
        /** <summary>The save was written by a newer build. Nothing was restored.</summary> */
        TooNew,
        /** <summary>A section's data could not be upgraded or read. Nothing was restored.</summary> */
        SectionFailed,
        /** <summary>A section threw while applying its data. The other sections were restored.</summary> */
        RestoreFailed,
    }

    /** <summary>The outcome of a load: its status, whether the backup was used, and what happened to each section.</summary> */
    public sealed class LoadResult
    {
        #region Properties

        /** <summary>How the load ended.</summary> */
        public LoadStatus Status { get; }

        /** <summary>True when the save itself was damaged and its backup was loaded instead.</summary> */
        public bool UsedBackup { get; private set; }

        /** <summary>What went wrong, including with the save when the backup was used; null when nothing did.</summary> */
        public string Problem { get; private set; }

        /** <summary>Every section's outcome, in restore order, then the held ones.</summary> */
        public IReadOnlyList<SectionReport> Sections { get; }

        /** <summary>True when live state now matches the save.</summary> */
        public bool Succeeded => Status == LoadStatus.Loaded;

        #endregion

        #region Public API

        /** <summary>Makes a result.</summary> */
        public LoadResult(LoadStatus status, IReadOnlyList<SectionReport> sections = null, string problem = null)
        {
            Status = status;
            Sections = sections ?? Array.Empty<SectionReport>();
            Problem = problem;
        }

        /** <summary>Marks the result as loaded from the backup because of the given problem with the save.</summary> */
        public LoadResult FromBackup(string saveProblem)
        {
            UsedBackup = true;
            Problem = Problem == null ? saveProblem : $"{saveProblem}; {Problem}";
            return this;
        }

        /** <inheritdoc /> */
        public override string ToString() => Problem == null ? Status.ToString() : $"{Status}: {Problem}";

        #endregion
    }
}
