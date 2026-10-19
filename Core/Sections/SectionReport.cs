namespace SaveSystem
{
    /** <summary>What happened to one section during a load or a registration.</summary> */
    public enum SectionOutcome
    {
        /** <summary>Restored from data at the current version.</summary> */
        Restored,
        /** <summary>Restored from older data after upgrading it.</summary> */
        Upgraded,
        /** <summary>The save had no data for it, so it was given a new game's state.</summary> */
        Defaulted,
        /** <summary>No system has this key; the data is kept and written back on the next save.</summary> */
        Held,
        /** <summary>Its data could not be used.</summary> */
        Failed,
        /** <summary>Registered with nothing held for it, so its live state was left as it was.</summary> */
        Unchanged,
    }

    /** <summary>One section's part in a load: its key, what happened, and why if it failed.</summary> */
    public readonly struct SectionReport
    {
        #region Properties

        /** <summary>The section's key.</summary> */
        public string Key { get; }

        /** <summary>What happened to it.</summary> */
        public SectionOutcome Outcome { get; }

        /** <summary>The version its data was stored at; 0 when there was none.</summary> */
        public int StoredVersion { get; }

        /** <summary>Why it failed; null otherwise.</summary> */
        public string Problem { get; }

        #endregion

        #region Public API

        /** <summary>Makes a report.</summary> */
        public SectionReport(string key, SectionOutcome outcome, int storedVersion, string problem = null)
        {
            Key = key;
            Outcome = outcome;
            StoredVersion = storedVersion;
            Problem = problem;
        }

        /** <inheritdoc /> */
        public override string ToString() => Problem == null ? $"{Key}: {Outcome}" : $"{Key}: {Outcome} ({Problem})";

        #endregion
    }
}
