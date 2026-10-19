namespace SaveSystem
{
    /** <summary>How a save ended.</summary> */
    public enum SaveStatus
    {
        /** <summary>Written.</summary> */
        Saved,
        /** <summary>A section threw while capturing. Nothing was written, so the previous save is intact.</summary> */
        CaptureFailed,
        /** <summary>The file could not be written. The previous save is intact.</summary> */
        WriteFailed,
    }

    /** <summary>The outcome of a save.</summary> */
    public sealed class SaveResult
    {
        #region Properties

        /** <summary>How the save ended.</summary> */
        public SaveStatus Status { get; }

        /** <summary>What went wrong; null when nothing did.</summary> */
        public string Problem { get; }

        /** <summary>The entry or slot written to; null when nothing was written.</summary> */
        public string SavedTo { get; }

        /** <summary>True when the save was written.</summary> */
        public bool Succeeded => Status == SaveStatus.Saved;

        #endregion

        #region Public API

        /** <summary>Makes a result.</summary> */
        public SaveResult(SaveStatus status, string problem = null, string savedTo = null)
        {
            Status = status;
            Problem = problem;
            SavedTo = savedTo;
        }

        /** <inheritdoc /> */
        public override string ToString() => Problem == null ? Status.ToString() : $"{Status}: {Problem}";

        #endregion
    }
}
