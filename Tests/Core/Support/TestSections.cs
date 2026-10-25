using System;
using System.Collections.Generic;

namespace SaveSystem.Tests
{
    /** <summary>A counter's saved form.</summary> */
    public sealed class CounterData
    {
        public int Value;
        public string Note;
    }

    /** <summary>A configurable section holding one number and a note, which records the order it restores in.</summary> */
    public sealed class CounterSection : SaveSection<CounterData>
    {
        #region Private Fields

        private readonly string _key;
        private readonly int _version;
        private readonly SectionUpgrade[] _upgrades;

        #endregion

        #region Properties

        public int Value { get; set; }
        public string Note { get; set; }
        public int DefaultValue { get; set; }
        public bool ThrowOnCapture { get; set; }
        public bool ThrowOnRestore { get; set; }
        public int? ThrowOnRestoreOf { get; set; }
        public int RestoreCount { get; private set; }
        public int CaptureCount { get; private set; }
        public List<string> RestoreLog { get; set; }

        public override string Key => _key;
        public override int Version => _version;
        public override IReadOnlyList<SectionUpgrade> Upgrades => _upgrades;

        #endregion

        #region Public API

        public CounterSection(string key, int version = 1, params SectionUpgrade[] upgrades)
        {
            _key = key;
            _version = version;
            _upgrades = upgrades;
        }

        #endregion

        #region Helpers

        protected override CounterData Capture()
        {
            if (ThrowOnCapture) throw new InvalidOperationException("capture broke");
            CaptureCount++;
            return new CounterData { Value = Value, Note = Note };
        }

        protected override void Restore(CounterData data)
        {
            if (ThrowOnRestore || data.Value == ThrowOnRestoreOf) throw new InvalidOperationException("restore broke");
            Value = data.Value;
            Note = data.Note;
            RestoreCount++;
            RestoreLog?.Add(_key);
        }

        protected override CounterData CreateDefault() => new CounterData { Value = DefaultValue };

        #endregion
    }

    /** <summary>A richer saved form, for round trips of awkward values.</summary> */
    public sealed class ProfileData
    {
        public string Name;
        public List<int> Numbers = new List<int>();
        public Dictionary<string, string> Labels = new Dictionary<string, string>();
        public List<ProfileData> Children;
        public DateTime SavedAt;
        public string DateLikeText;
        public float FloatValue;
        public double DoubleValue;
        public long BigNumber;
        public string Missing;
    }

    /** <summary>A section that saves whatever <see cref="ProfileData" /> it holds.</summary> */
    public sealed class ProfileSection : SaveSection<ProfileData>
    {
        public ProfileData Data { get; set; } = new ProfileData();

        public override string Key => "profile";

        protected override ProfileData Capture() => Data;

        protected override void Restore(ProfileData data) => Data = data;
    }

    /** <summary>A saved form whose list starts with items, to prove a load replaces it instead of appending.</summary> */
    public sealed class PrefilledData
    {
        public List<int> Items = new List<int> { 1, 2 };
    }

    /** <summary>A section over <see cref="PrefilledData" />.</summary> */
    public sealed class PrefilledSection : SaveSection<PrefilledData>
    {
        public List<int> Items { get; set; } = new List<int>();

        public override string Key => "prefilled";

        protected override PrefilledData Capture() => new PrefilledData { Items = new List<int>(Items) };

        protected override void Restore(PrefilledData data) => Items = data.Items;
    }
}
