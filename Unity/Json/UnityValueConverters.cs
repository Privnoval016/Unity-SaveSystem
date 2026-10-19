using Newtonsoft.Json;
using UnityEngine;

namespace SaveSystem
{
    /** <summary>Converters for the engine's value types, which Newtonsoft can't write on its own.</summary> */
    public static class UnityValueConverters
    {
        #region Private Fields

        private static readonly string[] Xy = { "x", "y" };
        private static readonly string[] Xyz = { "x", "y", "z" };
        private static readonly string[] Xyzw = { "x", "y", "z", "w" };
        private static readonly string[] Rgba = { "r", "g", "b", "a" };

        #endregion

        #region Public API

        /** <summary>One of each converter.</summary> */
        public static JsonConverter[] Create() => new JsonConverter[]
        {
            new Vector2Converter(), new Vector3Converter(), new Vector4Converter(), new QuaternionConverter(), new ColorConverter(),
            new Color32Converter(), new Vector2IntConverter(), new Vector3IntConverter(),
        };

        #endregion

        #region Helpers

        /** <summary>Writes a Vector2 as named numbers.</summary> */
        private sealed class Vector2Converter : ComponentsConverter<Vector2>
        {
            protected override string[] Names => Xy;
            protected override double[] ToComponents(Vector2 v) => new double[] { v.x, v.y };
            protected override Vector2 FromComponents(double[] c) => new Vector2((float)c[0], (float)c[1]);
        }

        /** <summary>Writes a Vector3 as named numbers.</summary> */
        private sealed class Vector3Converter : ComponentsConverter<Vector3>
        {
            protected override string[] Names => Xyz;
            protected override double[] ToComponents(Vector3 v) => new double[] { v.x, v.y, v.z };
            protected override Vector3 FromComponents(double[] c) => new Vector3((float)c[0], (float)c[1], (float)c[2]);
        }

        /** <summary>Writes a Vector4 as named numbers.</summary> */
        private sealed class Vector4Converter : ComponentsConverter<Vector4>
        {
            protected override string[] Names => Xyzw;
            protected override double[] ToComponents(Vector4 v) => new double[] { v.x, v.y, v.z, v.w };
            protected override Vector4 FromComponents(double[] c) => new Vector4((float)c[0], (float)c[1], (float)c[2], (float)c[3]);
        }

        /** <summary>Writes a Quaternion as named numbers.</summary> */
        private sealed class QuaternionConverter : ComponentsConverter<Quaternion>
        {
            protected override string[] Names => Xyzw;
            protected override double[] ToComponents(Quaternion q) => new double[] { q.x, q.y, q.z, q.w };
            protected override Quaternion FromComponents(double[] c) => new Quaternion((float)c[0], (float)c[1], (float)c[2], (float)c[3]);
        }

        /** <summary>Writes a Color as named numbers.</summary> */
        private sealed class ColorConverter : ComponentsConverter<Color>
        {
            protected override string[] Names => Rgba;
            protected override double[] ToComponents(Color v) => new double[] { v.r, v.g, v.b, v.a };
            protected override Color FromComponents(double[] c) => new Color((float)c[0], (float)c[1], (float)c[2], (float)c[3]);
        }

        /** <summary>Writes a Color32 as named numbers.</summary> */
        private sealed class Color32Converter : IntComponentsConverter<Color32>
        {
            protected override string[] Names => Rgba;
            protected override double[] ToComponents(Color32 v) => new double[] { v.r, v.g, v.b, v.a };
            protected override Color32 FromComponents(double[] c) => new Color32((byte)c[0], (byte)c[1], (byte)c[2], (byte)c[3]);
        }

        /** <summary>Writes a Vector2Int as named numbers.</summary> */
        private sealed class Vector2IntConverter : IntComponentsConverter<Vector2Int>
        {
            protected override string[] Names => Xy;
            protected override double[] ToComponents(Vector2Int v) => new double[] { v.x, v.y };
            protected override Vector2Int FromComponents(double[] c) => new Vector2Int((int)c[0], (int)c[1]);
        }

        /** <summary>Writes a Vector3Int as named numbers.</summary> */
        private sealed class Vector3IntConverter : IntComponentsConverter<Vector3Int>
        {
            protected override string[] Names => Xyz;
            protected override double[] ToComponents(Vector3Int v) => new double[] { v.x, v.y, v.z };
            protected override Vector3Int FromComponents(double[] c) => new Vector3Int((int)c[0], (int)c[1], (int)c[2]);
        }

        #endregion
    }
}
