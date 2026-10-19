using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /**
     * <summary>A section whose saved form is a plain data class: capture fills one, restore reads one.</summary>
     * <typeparam name="TData">The data class. Its fields are the section's schema on disk.</typeparam>
     */
    public abstract class SaveSection<TData> : ISaveSection where TData : class, new()
    {
        #region Private Fields

        private static readonly SectionUpgrade[] NoUpgrades = Array.Empty<SectionUpgrade>();

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public abstract string Key { get; }

        /** <inheritdoc /> */
        public virtual int Version => 1;

        /** <inheritdoc /> */
        public virtual int RestoreOrder => 0;

        /** <inheritdoc /> */
        public virtual IReadOnlyList<SectionUpgrade> Upgrades => NoUpgrades;

        #endregion

        #region Public API

        JToken ISaveSection.Capture(JsonSerializer serializer)
        {
            var data = Capture();
            return data == null ? JValue.CreateNull() : JToken.FromObject(data, serializer);
        }

        IPreparedRestore ISaveSection.Prepare(JToken data, JsonSerializer serializer)
        {
            var read = data == null || data.Type == JTokenType.Null ? null : data.ToObject<TData>(serializer);
            return new PreparedRestore(this, read ?? CreateDefault());
        }

        IPreparedRestore ISaveSection.PrepareDefault() => new PreparedRestore(this, CreateDefault());

        #endregion

        #region Helpers

        /** <summary>Returns a snapshot of the current state. It must not share mutable objects with live state.</summary> */
        protected abstract TData Capture();

        /** <summary>Sets live state from saved data.</summary> */
        protected abstract void Restore(TData data);

        /** <summary>The data a new game starts with.</summary> */
        protected virtual TData CreateDefault() => new TData();

        private sealed class PreparedRestore : IPreparedRestore
        {
            private readonly SaveSection<TData> _section;
            private readonly TData _data;

            public PreparedRestore(SaveSection<TData> section, TData data)
            {
                _section = section;
                _data = data;
            }

            public void Apply() => _section.Restore(_data);
        }

        #endregion
    }
}
