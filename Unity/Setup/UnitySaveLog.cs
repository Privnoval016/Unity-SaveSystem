using UnityEngine;

namespace SaveSystem
{
    /** <summary>Reports save problems to the Unity console.</summary> */
    public sealed class UnitySaveLog : ISaveLog
    {
        #region Public API

        /** <inheritdoc /> */
        public void Warning(string message) => Debug.LogWarning(message);

        /** <inheritdoc /> */
        public void Error(string message) => Debug.LogError(message);
    

        #endregion
    }
}
