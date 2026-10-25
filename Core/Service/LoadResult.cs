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
        /**
         * <summary>A section threw while applying its data or settling. Every section was then put back as it was before
         * the load (<see cref="LoadResult.RolledBack" />); if that failed too, the sections that couldn't be put back are
         * saved with their last good data until a load or new game restores them.</summary>
         */
        RestoreFailed,
        /** <summary>Another load was under way, so nothing was read or changed; ask again once it ends.</summary> */
        Busy,
        /** <summary>The save is there but couldn't be read (another program holds it, a permission). Nothing changed, and nothing older was loaded in its place; ask again later.</summary> */
        Unreadable,
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

        /** <summary>True when a restore failed and every section was put back as it was before, so live state is the game that was being played.</summary> */
        public bool RolledBack { get; }

        /** <summary>True when live state is unchanged by this load: it never applied, or it failed and was put back.</summary> */
        public bool LeftStateUnchanged => Status != LoadStatus.Loaded && (Status != LoadStatus.RestoreFailed || RolledBack);

        /**
         * <summary>True when a restore failed and could not be put back: live state is partly the save and partly the game
         * before it, and the sections that failed are saved with their last good data (<c>SaveService.PinnedKeys</c>). Go
         * to a new game or the title; don't play on.</summary>
         */
        public bool NeedsReset { get; }

        #endregion

        #region Public API

        /** <summary>Makes a result.</summary> */
        public LoadResult(LoadStatus status, IReadOnlyList<SectionReport> sections = null, string problem = null, bool usedBackup = false, bool rolledBack = false, bool needsReset = false)
        {
            Status = status;
            Sections = sections ?? Array.Empty<SectionReport>();
            Problem = problem;
            UsedBackup = usedBackup;
            RolledBack = rolledBack;
            NeedsReset = needsReset;
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
            new LoadResult(Status, Sections, Problem == null ? saveProblem : $"{saveProblem}; {Problem}", true, RolledBack, NeedsReset);

        /** <summary>The same result with a note on how the save was read (a write a crash left, put in place).</summary> */
        public LoadResult WithNote(string note) =>
            new LoadResult(Status, Sections, Problem == null ? note : $"{note}; {Problem}", UsedBackup, RolledBack, NeedsReset);

        /** <summary>The same failed result, marked as put back or, if not, as needing a reset, with what went wrong putting it back.</summary> */
        public LoadResult WithRollback(bool rolledBack, string rollbackProblem) =>
            new LoadResult(Status, Sections, rollbackProblem == null ? Problem : $"{Problem}; {rollbackProblem}", UsedBackup, rolledBack, !rolledBack);

        /** <inheritdoc /> */
        public override string ToString() => Problem == null ? Status.ToString() : $"{Status}: {Problem}";

        #endregion
    }
}
