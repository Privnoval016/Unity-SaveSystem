using System;
using System.Threading.Tasks;

namespace SaveSystem
{
    /**
     * <summary>Saves a service of settings sections to one entry, apart from the save slots.</summary>
     * <remarks>Give it its own service, so settings and game state never mix, and its own storage folder.</remarks>
     */
    public sealed class SettingsStore
    {
        #region Private Fields

        private const string Queue = "settings";

        private readonly SaveDocumentQueue _documents;
        private readonly SaveFileStore _store;

        #endregion

        #region Properties

        /** <summary>The entry settings are stored in.</summary> */
        public const string Entry = "settings.cfg";

        #endregion

        #region Public API

        /** <summary>Makes a store for the service's sections.</summary> */
        public SettingsStore(SaveService service, ISaveStorage storage, SaveFileOptions options = null)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            options = options ?? new SaveFileOptions();
            _documents = new SaveDocumentQueue(service, options.Worker, Queue);
            _store = new SaveFileStore(storage, service.Serializer, options.Codec);
        }

        /** <summary>Captures the settings now and writes them in the background.</summary> */
        public Task<SaveResult> SaveAsync() => _documents.SaveAsync(document => _store.Write(Entry, document.ToJson()));

        /** <summary>Reads the settings in the background, then restores them on the calling thread.</summary> */
        public Task<LoadResult> LoadAsync() => _documents.LoadAsync(() => _store.Read(Entry, SaveDocument.FromJson));

        /** <summary>Blocks until pending writes finish, or the timeout passes.</summary> <returns>True when all finished.</returns> */
        public bool Flush(TimeSpan timeout) => _documents.Flush(timeout);

        #endregion
    }
}
