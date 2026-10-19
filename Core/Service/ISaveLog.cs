namespace SaveSystem
{
    /** <summary>Where the save system reports problems it handles without failing the caller.</summary> */
    public interface ISaveLog
    {
        /** <summary>Something was wrong but the save system carried on.</summary> */
        void Warning(string message);

        /** <summary>Something failed and data may not be what the player expects.</summary> */
        void Error(string message);
    }

    /** <summary>A log that drops every message, for when no log is supplied.</summary> */
    public sealed class SilentSaveLog : ISaveLog
    {
        /** <summary>The shared instance.</summary> */
        public static readonly SilentSaveLog Instance = new SilentSaveLog();

        /** <inheritdoc /> */
        public void Warning(string message) { }

        /** <inheritdoc /> */
        public void Error(string message) { }
    }
}
