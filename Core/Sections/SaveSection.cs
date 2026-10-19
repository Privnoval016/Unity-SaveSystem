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
        #region Properties

        /** <inheritdoc /> */
        public abstract string Key { get; }

        /** <inheritdoc /> */
        public virtual int Version => 1;

        /** <inheritdoc /> */
        public virtual IReadOnlyList<SectionUpgrade> Upgrades => SectionUpgrade.None;

        #endregion

        #region Public API

        JToken ISaveSection.Capture(JsonSerializer serializer) => SectionData.Capture(Capture(), serializer);

        IPreparedRestore ISaveSection.Prepare(JToken data, JsonSerializer serializer) =>
            new PreparedRestore<TData>(Restore, SectionData.Read<TData>(data, serializer) ?? CreateDefault());

        IPreparedRestore ISaveSection.PrepareDefault() => new PreparedRestore<TData>(Restore, CreateDefault());

        #endregion

        #region Helpers

        /** <summary>Returns a snapshot of the current state. It must not share mutable objects with live state.</summary> */
        protected abstract TData Capture();

        /** <summary>Sets live state from saved data.</summary> */
        protected abstract void Restore(TData data);

        /** <summary>The data a new game starts with.</summary> */
        protected virtual TData CreateDefault() => new TData();

        #endregion
    }
}
