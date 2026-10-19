using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SaveSystem.Tests
{
    /** <summary>Engine value types in saves, and the Unity defaults.</summary> */
    public sealed class ValueConverterTests
    {
        public sealed class Values
        {
            public Vector2 V2;
            public Vector3 V3;
            public Vector4 V4;
            public Quaternion Rotation;
            public Color Tint;
            public Color32 Tint32;
            public Vector2Int Cell;
            public Vector3Int Voxel;
        }

        private static readonly SaveSerializer Serializer = UnitySave.CreateSerializer(null);

        private static Values RoundTrip(Values values, out JToken json)
        {
            json = JToken.FromObject(values, Serializer.Json);
            return Serializer.Parse(Serializer.ToBytes(json, true)).ToObject<Values>(Serializer.Json);
        }

        [Test]
        public void EngineValues_RoundTrip()
        {
            var values = new Values
            {
                V2 = new Vector2(1.5f, -2f), V3 = new Vector3(0.1f, 2e10f, -3f), V4 = new Vector4(1, 2, 3, 4),
                Rotation = Quaternion.Euler(10, 20, 30), Tint = new Color(0.25f, 0.5f, 0.75f, 1), Tint32 = new Color32(1, 128, 255, 0),
                Cell = new Vector2Int(-5, int.MaxValue), Voxel = new Vector3Int(1, -2, int.MinValue),
            };
            var loaded = RoundTrip(values, out _);
            Assert.That(loaded.V2, Is.EqualTo(values.V2));
            Assert.That(loaded.V3, Is.EqualTo(values.V3));
            Assert.That(loaded.V4, Is.EqualTo(values.V4));
            Assert.That(loaded.Rotation, Is.EqualTo(values.Rotation));
            Assert.That(loaded.Tint, Is.EqualTo(values.Tint));
            Assert.That(loaded.Tint32, Is.EqualTo(values.Tint32));
            Assert.That(loaded.Cell, Is.EqualTo(values.Cell));
            Assert.That(loaded.Voxel, Is.EqualTo(values.Voxel));
        }

        [Test]
        public void Floats_KeepEveryBit()
        {
            var awkward = new Vector3(0.1f, 1f / 3f, float.Epsilon);
            Assert.That(RoundTrip(new Values { V3 = awkward }, out _).V3, Is.EqualTo(awkward));
        }

        [Test]
        public void Values_AreWrittenAsNamedNumbers()
        {
            RoundTrip(new Values { V3 = new Vector3(1, 2, 3), Cell = new Vector2Int(4, 5), Tint32 = new Color32(1, 2, 3, 4) }, out var json);
            Assert.That(JToken.DeepEquals(json["V3"], JToken.Parse("{\"x\":1.0,\"y\":2.0,\"z\":3.0}")));
            Assert.That(json["Cell"].ToString(Formatting.None), Is.EqualTo("{\"x\":4,\"y\":5}"));
            Assert.That(json["Tint32"]["a"].Type, Is.EqualTo(JTokenType.Integer));
        }

        [Test]
        public void NaNComponents_RoundTrip()
        {
            Assert.That(float.IsNaN(RoundTrip(new Values { V3 = new Vector3(float.NaN, 0, 0) }, out _).V3.x));
        }

        [Test]
        public void MissingComponents_AreZero_AndNullIsDefault()
        {
            var loaded = JToken.Parse("{\"V3\":{\"x\":2},\"V2\":null}").ToObject<Values>(Serializer.Json);
            Assert.That(loaded.V3, Is.EqualTo(new Vector3(2, 0, 0)));
            Assert.That(loaded.V2, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Worker_InTheEditor_IsBackground()
        {
            Assert.That(UnitySave.CreateWorker(), Is.TypeOf<BackgroundSaveWorker>());
        }

        [Test]
        public void FlushOnQuit_AddsOneQuitHandler_AndDisposingRemovesIt()
        {
            var before = QuitHandlers();
            var flush = UnitySave.FlushOnQuit(new ImmediateSaveWorker(), TimeSpan.FromSeconds(1));
            Assert.That(QuitHandlers(), Is.EqualTo(before + 1));
            flush.Dispose();
            Assert.That(QuitHandlers(), Is.EqualTo(before));
        }

        private static int QuitHandlers()
        {
            var field = typeof(Application).GetField("quitting", BindingFlags.Static | BindingFlags.NonPublic);
            return (field?.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
        }
    }
}
