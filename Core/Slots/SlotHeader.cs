using System;
using Newtonsoft.Json;

namespace SaveSystem
{
    /**
     * <summary>What the save screen shows for a slot, kept in a small file beside the save so listing never reads saves.</summary>
     * <typeparam name="TInfo">The game's own fields, such as chapter, place and play time.</typeparam>
     */
    public sealed class SlotHeader<TInfo>
    {
        #region Properties

        /** <summary>The slot's name.</summary> */
        [JsonProperty("slot")]
        public string Slot { get; set; }

        /** <summary>Counts up with every save to any slot, so the oldest autosave is found without trusting the clock.</summary> */
        [JsonProperty("sequence")]
        public long Sequence { get; set; }

        /** <summary>When the save was made, in UTC.</summary> */
        [JsonProperty("savedAtUtc")]
        public DateTime SavedAtUtc { get; set; }

        /** <summary>The game's own fields.</summary> */
        [JsonProperty("info")]
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
