using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SaveSystem
{
    /** <summary>The Unity defaults for setting up saving: folders, the worker for the platform, the serializer and quitting.</summary> */
    public static class UnitySave
    {
        #region Private Fields

        private const string SavesFolderName = "saves";
        private const string SettingsFolderName = "settings";

        #endregion

        #region Properties

        /** <summary>Where save slots live by default.</summary> */
        public static string SavesFolder => Path.Combine(Application.persistentDataPath, SavesFolderName);

        /** <summary>Where settings live by default, apart from the slots.</summary> */
        public static string SettingsFolder => Path.Combine(Application.persistentDataPath, SettingsFolderName);

        #endregion

        #region Public API

        /** <summary>A background worker, or an immediate one on platforms without threads.</summary> */
        public static ISaveWorker CreateWorker() =>
            Application.platform == RuntimePlatform.WebGLPlayer ? (ISaveWorker)new ImmediateSaveWorker() : new BackgroundSaveWorker();

        /** <summary>A serializer that writes engine value types, and saveable assets as their ids.</summary> */
        public static SaveSerializer CreateSerializer(IAssetResolver assets, ISaveLog log = null)
        {
            var converters = new List<JsonConverter>(UnityValueConverters.Create());
            if (assets != null) converters.Add(new SaveableAssetConverter(assets, log));
            return new SaveSerializer(converters);
        }

        /**
         * <summary>Waits for pending writes when the application quits, so a save made just before quitting is finished.</summary>
         * <returns>Dispose to stop waiting on quit.</returns>
         */
        public static IDisposable FlushOnQuit(ISaveWorker worker, TimeSpan timeout)
        {
            if (worker == null) throw new ArgumentNullException(nameof(worker));
            return new QuitFlush(worker, timeout);
        }

        #endregion

        #region Helpers

        /** <summary>Waits for a worker when the application quits, until disposed.</summary> */
        private sealed class QuitFlush : IDisposable
        {
            private readonly ISaveWorker _worker;
            private readonly TimeSpan _timeout;

            public QuitFlush(ISaveWorker worker, TimeSpan timeout)
            {
                _worker = worker;
                _timeout = timeout;
                Application.quitting += Flush;
            }

            public void Dispose() => Application.quitting -= Flush;

            private void Flush()
            {
                if (!_worker.WaitForAll(_timeout)) Debug.LogError("Save: writes still pending at quit.");
            }
        }

        #endregion
    }
}
