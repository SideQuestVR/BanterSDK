using System;
using System.Threading;
using System.Threading.Tasks;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// Marks a task's failure as observed the moment it fails. The relay's socket and HTTP calls are always awaited,
    /// but Unity unloads the scripting domain (leaving Play, a script reload) while a cancelled call's await
    /// continuation is still queued on the thread pool. The failed task is then only ever seen by its finalizer, and
    /// TaskScheduler.UnobservedTaskException reports it as an error (BSStarterUpper logs those). Code awaiting the
    /// task still gets the exception exactly as before.
    /// </summary>
    internal static class TaskFaults
    {
        static readonly Action<Task> s_observe = task => _ = task.Exception;

        public static TTask Observe<TTask>(TTask task) where TTask : Task
        {
            task?.ContinueWith(s_observe, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }
}
