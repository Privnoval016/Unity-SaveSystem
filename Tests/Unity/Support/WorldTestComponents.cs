using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveSystem.Tests
{
    /** <summary>A chest's saved form.</summary> */
    public sealed class ChestData
    {
        public bool Open;
        public int Coins;
    }

    /** <summary>A test chest. Kept in a file of another name: test assemblies can't add components that have their own file.</summary> */
    public sealed class TestChest : SaveableComponent<ChestData>
    {
        public bool Open { get; set; }
        public int Coins { get; set; } = 10;
        public bool ThrowOnRestore { get; set; }
        public bool ThrowOnCapture { get; set; }

        public override string Key => "chest";

        protected override ChestData CaptureState() =>
            ThrowOnCapture ? throw new InvalidOperationException("chest capture broke") : new ChestData { Open = Open, Coins = Coins };

        protected override void RestoreState(ChestData data)
        {
            if (ThrowOnRestore) throw new InvalidOperationException("chest broke");
            Open = data.Open;
            Coins = data.Coins;
        }
    }

    /** <summary>A door's saved form.</summary> */
    public sealed class DoorData
    {
        public Vector3 Hinge;
        public float Angle;
    }

    /** <summary>A test door, at version 2: version 1 saved the angle as "degrees".</summary> */
    public sealed class TestDoor : SaveableComponent<DoorData>
    {
        private static readonly SectionUpgrade[] Steps =
        {
            new SectionUpgrade(1, data =>
            {
                data["Angle"] = data["degrees"];
                return data;
            }),
        };

        public float Angle { get; set; }
        public Vector3 Hinge { get; set; }

        public override string Key => "door";
        public override int Version => 2;
        public override IReadOnlyList<SectionUpgrade> Upgrades => Steps;

        protected override DoorData CaptureState() => new DoorData { Angle = Angle, Hinge = Hinge };

        protected override void RestoreState(DoorData data)
        {
            Angle = data.Angle;
            Hinge = data.Hinge;
        }
    }
}
