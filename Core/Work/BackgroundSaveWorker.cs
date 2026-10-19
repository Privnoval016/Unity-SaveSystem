using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SaveSystem
{
    /**
     * <summary>Runs save work on the thread pool, chained per queue so jobs on one queue never overlap or reorder.</summary>
     * <remarks>Jobs never need the main thread, so waiting for them from the main thread (on quit) cannot deadlock.</remarks>
     */
    public sealed class BackgroundSaveWorker : ISaveWorker
    {
        #region Private Fields

        private readonly object _lock = new object();

        /** <summary>Keyed by queue name, resolving to the last job queued on it.</summary> */
        private readonly Dictionary<string, Task> _tails = new Dictionary<string, Task>(StringComparer.Ordinal);

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public Task<T> Run<T>(string queue, Func<T> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            lock (_lock)
            {
                var tail = _tails.TryGetValue(queue, out var last) ? last : Task.CompletedTask;
                var next = tail.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);
                _tails[queue] = next;
                return next;
            }
        }

        /** <inheritdoc /> */
        public bool WaitForAll(TimeSpan timeout)
        {
            Task[] pending;
            lock (_lock)
                pending = _tails.Values.Where(task => !task.IsCompleted).ToArray();
            try
            {
                return Task.WaitAll(pending, timeout);
            }
            catch (AggregateException)
            {
                return pending.All(task => task.IsCompleted);
            }
        }

        #endregion
    }
}
