using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SaveSystem
{
    /**
     * <summary>A component whose state is saved as a plain data class. Put it beside a <see cref="SaveableEntity" />.</summary>
     * <remarks>With no saved data for it (a new game, or a component added later) it keeps the state the scene gave it.</remarks>
     * <typeparam name="TData">The saved form.</typeparam>
     */
    [RequireComponent(typeof(SaveableEntity))]
    public abstract class SaveableComponent<TData> : MonoBehaviour, ISaveableComponent where TData : class, new()
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

        JToken ISaveSection.Capture(JsonSerializer serializer) => SectionData.Capture(CaptureState(), serializer);

        IPreparedRestore ISaveSection.Prepare(JToken data, JsonSerializer serializer)
        {
            var read = SectionData.Read<TData>(data, serializer);
            return read == null ? (IPreparedRestore)NoRestore.Instance : new PreparedRestore<TData>(RestoreIfAlive, read);
        }

        IPreparedRestore ISaveSection.PrepareDefault() => NoRestore.Instance;

        #endregion

        #region Helpers

        /** <summary>Returns a snapshot of the component's state.</summary> */
        protected abstract TData CaptureState();

        /** <summary>Sets the component's state from saved data.</summary> */
        protected abstract void RestoreState(TData data);

        private void RestoreIfAlive(TData data)
        {
            if (this != null) RestoreState(data);
        }

        #endregion
    }
}
