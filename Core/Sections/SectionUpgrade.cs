using System;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /** <summary>One step that turns a section's data from one version into the next, working on the raw JSON.</summary> */
    public sealed class SectionUpgrade
    {
        #region Private Fields

        private readonly Func<JToken, JToken> _upgrade;

        #endregion

        #region Properties

        /** <summary>The version this step reads; it produces <c>FromVersion + 1</c>.</summary> */
        public int FromVersion { get; }

        #endregion

        #region Public API

        /**
         * <param name="fromVersion">The version this step reads, from 1.</param>
         * <param name="upgrade">Returns the next version's data. It receives a copy, so it may change it in place.</param>
         */
        public SectionUpgrade(int fromVersion, Func<JToken, JToken> upgrade)
        {
            if (fromVersion < 1) throw new ArgumentOutOfRangeException(nameof(fromVersion), "Versions start at 1.");
            FromVersion = fromVersion;
            _upgrade = upgrade ?? throw new ArgumentNullException(nameof(upgrade));
        }

        /**
         * <summary>Upgrades a copy of the data; the data passed in is never changed.</summary>
         * <exception cref="InvalidOperationException">The step returned nothing.</exception>
         */
        public JToken Apply(JToken data)
        {
            var copy = data == null ? JValue.CreateNull() : data.DeepClone();
            return _upgrade(copy) ?? throw new InvalidOperationException($"Upgrade from version {FromVersion} returned nothing.");
        }

        #endregion
    }
}
