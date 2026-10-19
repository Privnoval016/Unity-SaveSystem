using System;
using System.Threading.Tasks;

namespace SaveSystem
{
    /** <summary>Runs save work away from the caller, one job at a time per queue, in the order it was asked for.</summary> */
    public interface ISaveWorker
    {
        /** <summary>Queues work after everything already queued under the same name.</summary> */
        Task<T> Run<T>(string queue, Func<T> work);

        /** <summary>Blocks until every queued job has finished, or the timeout passes.</summary> <returns>True when all finished.</returns> */
        bool WaitForAll(TimeSpan timeout);
    }
}
