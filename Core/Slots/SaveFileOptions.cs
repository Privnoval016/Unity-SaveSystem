using System;

namespace SaveSystem
{
    /** <summary>How saves are written: which worker runs the writes, which codec frames them, and where problems go.</summary> */
    public class SaveFileOptions
    {
        #region Properties

        /** <summary>Runs reads and writes. Give the slots and the settings the same one, so one flush waits for both.</summary> */
        public ISaveWorker Worker { get; }

        /** <summary>Frames and transforms saves. Defaults to plain framed JSON.</summary> */
        public SaveFileCodec Codec { get; set; } = SaveFileCodec.Plain;

        /** <summary>Where problems the store works around are reported.</summary> */
        public ISaveLog Log { get; set; } = SilentSaveLog.Instance;

        #endregion

        #region Public API

        /** <summary>Options with the worker every read and write runs on.</summary> */
        public SaveFileOptions(ISaveWorker worker) => Worker = worker ?? throw new ArgumentNullException(nameof(worker));

        #endregion
    }

    /** <summary>How save slots are written and rotated.</summary> */
    public sealed class SlotOptions : SaveFileOptions
    {
        #region Properties

        /** <summary>How many autosave slots rotate.</summary> */
        public int AutosaveCount { get; set; } = 3;

        /** <summary>The clock headers are stamped with.</summary> */
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        #endregion

        #region Public API

        /** <summary>Options with the worker every read and write runs on.</summary> */
        public SlotOptions(ISaveWorker worker) : base(worker) { }

        #endregion
    }
}
