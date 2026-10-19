using System;
using System.Threading.Tasks;

namespace SaveSystem
{
    /**
     * <summary>The shared shape of saving and loading a service: capture now on the calling thread, write and read on
     * the worker one job at a time, restore on the thread that awaited the load.</summary>
     */
    public sealed class SaveDocumentQueue
    {
        #region Private Fields

        private readonly string _queue;

        #endregion

        #region Properties

        /** <summary>The sections saved and loaded.</summary> */
        public SaveService Service { get; }

        /** <summary>Runs the jobs.</summary> */
        public ISaveWorker Worker { get; }

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

        /** <summary>Captures the service now, then runs the write on the worker. A capture that throws writes nothing.</summary> */
        public Task<SaveResult> SaveAsync(Func<SaveDocument, SaveResult> write)
        {
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

        /** <summary>Runs the read on the worker, then restores what it read on the calling thread.</summary> */
        public async Task<LoadResult> LoadAsync(Func<ReadResult<SaveDocument>> read)
        {
            var result = await Worker.Run(_queue, read);
            switch (result.Status)
            {
                case ReadStatus.Missing:
                    return new LoadResult(LoadStatus.Missing);
                case ReadStatus.Damaged:
                    return new LoadResult(LoadStatus.Damaged, problem: result.Problem);
                default:
                    var loaded = Service.Restore(result.Value);
                    return result.UsedBackup ? loaded.FromBackup(result.Problem) : loaded;
            }
        }

        /** <summary>Blocks until pending jobs finish, or the timeout passes.</summary> <returns>True when all finished.</returns> */
        public bool Flush(TimeSpan timeout) => Worker.WaitForAll(timeout);

        #endregion
    }
}
