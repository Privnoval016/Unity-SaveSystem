using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /** <summary>Saves a small engine value type, such as a vector, as an object of named numbers.</summary> */
    public abstract class ComponentsConverter<T> : JsonConverter<T>
    {
        #region Properties

        /** <summary>The names of the components, in order.</summary> */
        protected abstract string[] Names { get; }

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public override void WriteJson(JsonWriter writer, T value, JsonSerializer serializer)
        {
            var components = ToComponents(value);
            writer.WriteStartObject();
            for (var i = 0; i < Names.Length; i++)
            {
                writer.WritePropertyName(Names[i]);
                WriteComponent(writer, components[i]);
            }
            writer.WriteEndObject();
        }

        /** <inheritdoc /> */
        public override T ReadJson(JsonReader reader, Type objectType, T existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return default;
            var json = JObject.Load(reader);
            var components = new double[Names.Length];
            for (var i = 0; i < Names.Length; i++)
                components[i] = json[Names[i]]?.ToObject<double>(serializer) ?? 0;
            return FromComponents(components);
        }

        #endregion

        #region Helpers

        /** <summary>The value's components, in the order of <see cref="Names" />.</summary> */
        protected abstract double[] ToComponents(T value);

        /** <summary>Builds a value from its components; a missing component is 0.</summary> */
        protected abstract T FromComponents(double[] components);

        /** <summary>Writes one component as a float; whole-number types write integers.</summary> */
        protected virtual void WriteComponent(JsonWriter writer, double component) => writer.WriteValue((float)component);

        #endregion
    }

    /** <summary>A components converter whose components are whole numbers.</summary> */
    public abstract class IntComponentsConverter<T> : ComponentsConverter<T>
    {
        /** <inheritdoc /> */
        protected override void WriteComponent(JsonWriter writer, double component) => writer.WriteValue((long)component);
    }
}
