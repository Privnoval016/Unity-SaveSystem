using System;
using System.Threading.Tasks;

namespace SaveSystem
{
    /** <summary>Runs save work at once on the caller's thread, for platforms without threads and for tests.</summary> */
    public sealed class ImmediateSaveWorker : ISaveWorker
    {
        #region Public API

        /** <inheritdoc /> */
        public Task<T> Run<T>(string queue, Func<T> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            try
            {
                return Task.FromResult(work());
            }
            catch (Exception e)
            {
                return Task.FromException<T>(e);
            }
        }

        /** <inheritdoc /> */
        public bool WaitForAll(TimeSpan timeout) => true;

        #endregion
    }
}
