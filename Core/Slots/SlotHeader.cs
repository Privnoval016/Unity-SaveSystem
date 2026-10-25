using System;
using Newtonsoft.Json;

namespace SaveSystem
{
    /** <summary>The JSON names of a slot header's fields.</summary> */
    public static class SlotHeaderFields
    {
        #region Properties

        /** <summary>The header layout this code writes.</summary> */
        public const int CurrentFormat = 1;

        /** <summary>The layout version.</summary> */
        public const string Format = "format";

        /** <summary>The slot's name.</summary> */
        public const string Slot = "slot";

        /** <summary>The save's sequence number.</summary> */
        public const string Sequence = "sequence";

        /** <summary>When it was saved.</summary> */
        public const string SavedAtUtc = "savedAtUtc";

        /** <summary>The game's own fields.</summary> */
        public const string Info = "info";

        #endregion
    }

    /**
     * <summary>What the save screen shows for a slot, kept in a small file beside the save so listing never reads saves.</summary>
     * <typeparam name="TInfo">The game's own fields, such as chapter, place and play time.</typeparam>
     */
    public sealed class SlotHeader<TInfo>
    {
        #region Properties

        /** <summary>The header layout it was written with.</summary> */
        [JsonProperty(SlotHeaderFields.Format)]
        public int Format { get; set; } = SlotHeaderFields.CurrentFormat;

        /** <summary>The slot's name.</summary> */
        [JsonProperty(SlotHeaderFields.Slot)]
        public string Slot { get; set; }

        /** <summary>Counts up with every save to any slot, so the oldest autosave is found without trusting the clock.</summary> */
        [JsonProperty(SlotHeaderFields.Sequence)]
        public long Sequence { get; set; }

        /** <summary>When the save was made, in UTC.</summary> */
        [JsonProperty(SlotHeaderFields.SavedAtUtc)]
        public DateTime SavedAtUtc { get; set; }

        /** <summary>The game's own fields. Plain data only: headers are read without asset or engine converters.</summary> */
        [JsonProperty(SlotHeaderFields.Info)]
        public TInfo Info { get; set; }

        #endregion
    }

    /** <summary>Whether a slot's header could be read.</summary> */
    public enum SlotHeaderState
    {
        /** <summary>Read.</summary> */
        Ok,
        /** <summary>The save exists but its header doesn't. Loading or repairing the slot rewrites it.</summary> */
        Missing,
        /** <summary>The header is damaged. Loading or repairing the slot rewrites it.</summary> */
        Damaged,
        /** <summary>The header was written by a newer build; its slot is not this build's to write over.</summary> */
        TooNew,
    }

    /** <summary>Whether a slot's save loads, found by reading it (<c>ListAsync(checkSaves: true)</c>).</summary> */
    public enum SlotSaveState
    {
        /** <summary>Not read: the list read headers only.</summary> */
        Unchecked,
        /** <summary>The save reads.</summary> */
        Ok,
        /** <summary>The save is damaged and its backup reads: loading it loads the backup.</summary> */
        FromBackup,
        /** <summary>Neither the save nor its backup reads.</summary> */
        Damaged,
        /** <summary>A newer build wrote it: this build can't load it, and won't write over it.</summary> */
        TooNew,
        /** <summary>The file is there but couldn't be read just now (another program holds it); not damaged, as far as is known.</summary> */
        Unreadable,
    }

    /** <summary>One slot as the save screen lists it.</summary> */
    public sealed class SlotSummary<TInfo>
    {
        #region Properties

        /** <summary>The slot.</summary> */
        public SlotId Slot { get; }

        /** <summary>Whether its header could be read.</summary> */
        public SlotHeaderState State { get; }

        /** <summary>Its header; null unless <see cref="SlotHeaderState.Ok" />.</summary> */
        public SlotHeader<TInfo> Header { get; }

        /** <summary>Whether its save loads; <see cref="SlotSaveState.Unchecked" /> unless the list read the saves.</summary> */
        public SlotSaveState SaveState { get; }

        /** <summary>What is wrong with its save or why its backup was used; null when nothing is or it was not read.</summary> */
        public string SaveProblem { get; }

        #endregion

        #region Public API

        /** <summary>Makes a summary.</summary> */
        public SlotSummary(SlotId slot, SlotHeaderState state, SlotHeader<TInfo> header, SlotSaveState saveState = SlotSaveState.Unchecked, string saveProblem = null)
        {
            Slot = slot;
            State = state;
            Header = header;
            SaveState = saveState;
            SaveProblem = saveProblem;
        }

        #endregion
    }
}
