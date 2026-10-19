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
        /** <summary>A whole-save <see cref="DocumentUpgrade" /> threw. Nothing was restored.</summary> */
        UpgradeFailed,
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
        public bool UsedBackup { get; }

        /** <summary>What went wrong, including with the save when the backup was used; null when nothing did.</summary> */
        public string Problem { get; }

        /** <summary>Every section's outcome, in restore order, then the held ones.</summary> */
        public IReadOnlyList<SectionReport> Sections { get; }

        /** <summary>True when live state now matches the save.</summary> */
        public bool Succeeded => Status == LoadStatus.Loaded;

        #endregion

        #region Public API

        /** <summary>Makes a result.</summary> */
        public LoadResult(LoadStatus status, IReadOnlyList<SectionReport> sections = null, string problem = null, bool usedBackup = false)
        {
            Status = status;
            Sections = sections ?? Array.Empty<SectionReport>();
            Problem = problem;
            UsedBackup = usedBackup;
        }

        /** <summary>Loaded, or restore-failed when any section's report failed.</summary> */
        public static LoadResult FromReports(IReadOnlyList<SectionReport> reports)
        {
            string failed = null;
            foreach (var report in reports)
                if (report.Outcome == SectionOutcome.Failed) failed = failed == null ? report.ToString() : $"{failed}; {report}";
            return new LoadResult(failed == null ? LoadStatus.Loaded : LoadStatus.RestoreFailed, reports, failed);
        }

        /** <summary>The same result, marked as loaded from the backup because of the given problem with the save.</summary> */
        public LoadResult WithBackup(string saveProblem) =>
            new LoadResult(Status, Sections, Problem == null ? saveProblem : $"{saveProblem}; {Problem}", true);

        /** <inheritdoc /> */
        public override string ToString() => Problem == null ? Status.ToString() : $"{Status}: {Problem}";

        #endregion
    }
}
