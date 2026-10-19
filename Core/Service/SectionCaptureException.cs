using System;

namespace SaveSystem
{
    /** <summary>A section threw while capturing its state.</summary> */
    public sealed class SectionCaptureException : Exception
    {
        /** <summary>The key of the section that failed.</summary> */
        public string Key { get; }

        /** <summary>Wraps the section's exception.</summary> */
        public SectionCaptureException(string key, Exception inner) : base($"Section '{key}' failed to capture: {inner.Message}", inner) => Key = key;
    }
}
