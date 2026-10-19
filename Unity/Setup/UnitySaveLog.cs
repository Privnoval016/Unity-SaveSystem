using UnityEngine;

namespace SaveSystem
{
    /** <summary>Reports save problems to the Unity console.</summary> */
    public sealed class UnitySaveLog : ISaveLog
    {
        #region Properties

        /** <summary>The shared instance.</summary> */
        public static UnitySaveLog Instance { get; } = new UnitySaveLog();

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public void Warning(string message) => Debug.LogWarning(message);

        /** <inheritdoc /> */
        public void Error(string message) => Debug.LogError(message);

        #endregion
    }
}
