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

        #endregion

        #region Public API

        /** <summary>Makes a summary.</summary> */
        public SlotSummary(SlotId slot, SlotHeaderState state, SlotHeader<TInfo> header)
        {
            Slot = slot;
            State = state;
            Header = header;
        }

        #endregion
    }
}
