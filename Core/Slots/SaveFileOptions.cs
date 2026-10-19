using System;

namespace SaveSystem
{
    /** <summary>How saves are written: which worker runs the writes, and which codec frames them.</summary> */
    public class SaveFileOptions
    {
        /** <summary>Runs reads and writes. Defaults to the thread pool.</summary> */
        public ISaveWorker Worker { get; set; } = new BackgroundSaveWorker();

        /** <summary>Frames and transforms saves. Defaults to plain framed JSON.</summary> */
        public SaveFileCodec Codec { get; set; } = SaveFileCodec.Plain;
    }

    /** <summary>How save slots are written and rotated.</summary> */
    public sealed class SlotOptions : SaveFileOptions
    {
        /** <summary>How many autosave slots rotate.</summary> */
        public int AutosaveCount { get; set; } = 3;

        /** <summary>The clock headers are stamped with.</summary> */
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
    }
}
