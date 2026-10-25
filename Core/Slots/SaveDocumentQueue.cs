using System;
using System.Threading.Tasks;

namespace SaveSystem
{
    /**
     * <summary>The shared shape of saving and loading a service: capture now on the calling thread, write and read on
     * the worker one job at a time, restore on the thread that awaited the load. While a load is under way, a save or
     * another load is refused as <c>Busy</c>, so nothing captures the game being left or restores twice.</summary>
     */
    internal sealed class SaveDocumentQueue
    {
        #region Private Fields

        private readonly string _queue;

        /** <summary>True from a load's start until it has restored (or failed); read and set on the calling (main) thread.</summary> */
        private volatile bool _loading;

        #endregion

        #region Properties

        /** <summary>The sections saved and loaded.</summary> */
        public SaveService Service { get; }

        /** <summary>Runs the jobs.</summary> */
        public ISaveWorker Worker { get; }

        /** <summary>True while a load is under way.</summary> */
        public bool Loading => _loading;

        #endregion

        #region Public API

        /**
         * <summary>Makes a queue.</summary>
         * <param name="service">The sections.</param>
         * <param name="worker">Runs the jobs; share one worker between queues so one flush waits for all.</param>
         * <param name="queue">The worker queue jobs run on, in order.</param>
         */
        public SaveDocumentQueue(SaveService service, ISaveWorker worker, string queue)
        {
            Service = service ?? throw new ArgumentNullException(nameof(service));
            Worker = worker ?? throw new ArgumentNullException(nameof(worker));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        /** <summary>Captures the service now, then runs the write on the worker. A capture that throws writes nothing; so does a save asked for while a load is under way.</summary> */
        public Task<SaveResult> SaveAsync(Func<SaveDocument, SaveResult> write)
        {
            if (_loading) return Task.FromResult(new SaveResult(SaveStatus.Busy, "A load is under way."));
            SaveDocument document;
            try
            {
                document = Service.Capture();
            }
            catch (Exception e)
            {
                return Task.FromResult(new SaveResult(SaveStatus.CaptureFailed, e.Message));
            }
            return Worker.Run(_queue, () => write(document));
        }

        /** <summary>Runs work on the worker after every job already queued.</summary> */
        public Task<T> Run<T>(Func<T> work) => Worker.Run(_queue, work);

        /** <summary>Runs the read on the worker, then restores what it read on the calling thread. Refused while another load is under way.</summary> */
        public async Task<LoadResult> LoadAsync(Func<ReadResult<SaveDocument>> read)
        {
            if (_loading) return new LoadResult(LoadStatus.Busy, problem: "Another load is under way.");
            _loading = true;
            try
            {
                ReadResult<SaveDocument> result;
                try
                {
                    result = await Worker.Run(_queue, read);
                }
                catch (Exception e)
                {
                    return new LoadResult(LoadStatus.Damaged, problem: $"Reading failed: {e.Message}");
                }
                switch (result.Status)
                {
                    case ReadStatus.Missing:
                        return new LoadResult(LoadStatus.Missing);
                    case ReadStatus.Damaged:
                        return new LoadResult(LoadStatus.Damaged, problem: result.Problem);
                    case ReadStatus.TooNew:
                        return new LoadResult(LoadStatus.TooNew, problem: result.Problem);
                    case ReadStatus.Unreadable:
                        return new LoadResult(LoadStatus.Unreadable, problem: result.Problem);
                    default:
                        var loaded = Service.Restore(result.Value);
                        if (result.UsedBackup) return loaded.WithBackup(result.Problem);
                        return result.Source == ReadSource.Pending ? loaded.WithNote(result.Problem) : loaded;
                }
            }
            finally
            {
                _loading = false;
            }
        }

        /** <summary>Blocks until pending jobs finish, or the timeout passes.</summary> <returns>True when all finished.</returns> */
        public bool Flush(TimeSpan timeout) => Worker.WaitForAll(timeout);

        #endregion
    }
}
