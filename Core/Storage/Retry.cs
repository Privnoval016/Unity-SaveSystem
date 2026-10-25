using System;
using System.IO;
using System.Threading;

namespace SaveSystem
{
    /**
     * <summary>Runs a file operation again after a short wait when another program has the file for a moment (a virus
     * scanner, a sync client). An I/O error is retried after each delay; an access error only once, since a read-only
     * folder won't change; a missing file or folder is never waited out.</summary>
     */
    internal static class Retry
    {
        #region Public API

        /**
         * <summary>Runs <paramref name="work" />, again after each delay while it fails as above; the last failure is thrown.</summary>
         * <param name="delays">The waits before each retry, in milliseconds; empty runs it once.</param>
         * <param name="wait">How to wait; sleeps the thread by default (file work runs on the save worker).</param>
         */
        public static void Run(Action work, int[] delays, Action<int> wait = null) => Run(() =>
        {
            work();
            return true;
        }, delays, wait);

        /** <summary>As <see cref="Run(Action, int[], Action{int})" />, returning what <paramref name="work" /> returns.</summary> */
        public static T Run<T>(Func<T> work, int[] delays, Action<int> wait = null)
        {
            wait = wait ?? Thread.Sleep;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return work();
                }
                catch (Exception e) when (attempt < AttemptsFor(e, delays.Length))
                {
                    wait(delays[attempt]);
                }
            }
        }

        #endregion

        #region Helpers

        /** <summary>How many retries this failure gets.</summary> */
        private static int AttemptsFor(Exception e, int delays)
        {
            if (e is FileNotFoundException || e is DirectoryNotFoundException) return 0;
            if (e is UnauthorizedAccessException) return Math.Min(1, delays);
            return e is IOException ? delays : 0;
        }

        #endregion
    }
}
