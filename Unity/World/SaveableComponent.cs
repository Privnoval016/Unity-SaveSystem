using System;
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
            var data = CaptureState();
            return data == null ? JValue.CreateNull() : JToken.FromObject(data, serializer);
        }

        IPreparedRestore ISaveSection.Prepare(JToken data, JsonSerializer serializer)
        {
            var read = data == null || data.Type == JTokenType.Null ? null : data.ToObject<TData>(serializer);
            return read == null ? NoChange.Instance : new PreparedState(this, read);
        }

        IPreparedRestore ISaveSection.PrepareDefault() => NoChange.Instance;

        #endregion

        #region Helpers

        /** <summary>Returns a snapshot of the component's state.</summary> */
        protected abstract TData CaptureState();

        /** <summary>Sets the component's state from saved data.</summary> */
        protected abstract void RestoreState(TData data);

        private sealed class PreparedState : IPreparedRestore
        {
            private readonly SaveableComponent<TData> _component;
            private readonly TData _data;

            public PreparedState(SaveableComponent<TData> component, TData data)
            {
                _component = component;
                _data = data;
            }

            public void Apply()
            {
                if (_component != null) _component.RestoreState(_data);
            }
        }

        private sealed class NoChange : IPreparedRestore
        {
            public static readonly NoChange Instance = new NoChange();

            public void Apply() { }
        }

        #endregion
    }
}
