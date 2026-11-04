#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;


namespace BS.Utilities.Async
{
    /// <summary>
    /// A task scheduler that runs tasks on the Unity main thread via coroutines.
    /// </summary>
    public class UnityMainThreadTaskScheduler : TaskScheduler, IDisposable
    {
        /// <summary>
        /// Gets the default main thread scheduler that is managed by BSIPA.
        /// </summary>
        /// <value>a scheduler that is managed by BSIPA</value>
        public static new UnityMainThreadTaskScheduler Default { get; } = new UnityMainThreadTaskScheduler();
        /// <summary>
        /// Gets a factory for creating tasks on <see cref="Default"/>.
        /// </summary>
        /// <value>a factory for creating tasks on the default scheduler</value>
        public static TaskFactory Factory { get; } = new TaskFactory(Default);

        private readonly ConcurrentQueue<QueueItem> tasks = new();
        private static readonly ConditionalWeakTable<Task, QueueItem> itemTable = new();

        private class QueueItem : IEquatable<Task>, IEquatable<QueueItem>
        {
            private bool hasTask;
            public bool HasTask
            {
                get => hasTask;
                set
                {
                    hasTask = value;
                    if (!hasTask) Task = null;
                }
            }

            public Task? Task { get; private set; }

            public Action? Action { get; private set; }

            public IEnumerator? Coroutine { get; private set; }

            // Clock() when queued: the backlog warning reports how long the oldest item has waited.
            public long EnqueuedAt { get; set; }

            // EnqueueCounted items: released once the item has run, or been dropped by an editor Cancel.
            public PendingCounter? Counter { get; set; }

            public QueueItem(Task task)
            {
                HasTask = true;
                Task = task;
            }

            public QueueItem(Action action)
            {
                HasTask = true;
                Action = action;
            }

            public QueueItem(IEnumerator coroutine)
            {
                HasTask = true;
                Coroutine = coroutine;
            }

            public bool Equals(Task? other) => other is not null && HasTask && other.Equals(Task);
            public bool Equals(QueueItem other) => other.HasTask == HasTask && Equals(other.Task);
        }

        /// <summary>
        /// Gets whether or not this scheduler is currently executing tasks.
        /// </summary>
        /// <value><see langword="true"/> if the scheduler is running, <see langword="false"/> otherwise</value>
        public bool IsRunning { get; private set; } = false;

        /// <summary>
        /// Gets whether or not this scheduler is in the process of shutting down.
        /// </summary>
        /// <value><see langword="true"/> if the scheduler is shutting down, <see langword="false"/> otherwise</value>
        public bool Cancelling { get; private set; } = false;

        private int yieldAfterTasks = int.MaxValue;
        /// <summary>
        /// Gets or sets the number of tasks to execute before yielding back to Unity. Unlimited by default: task costs
        /// differ by orders of magnitude, so <see cref="YieldAfterTime"/> is the limit that counts.
        /// </summary>
        /// <value>the number of tasks to execute per resume</value>
        public int YieldAfterTasks
        {
            get => yieldAfterTasks;
            set
            {
                ThrowIfDisposed();
                if (value < 1)
                    throw new ArgumentException("Value cannot be less than 1", nameof(value));
                yieldAfterTasks = value;
            }
        }

        // A const, not a static field: Default is built by a static initializer above, before any static field
        // declared below it is set, and would get a zero budget.
        private const double DefaultYieldAfterMilliseconds = 4;
        private TimeSpan yieldAfterTime = TimeSpan.FromMilliseconds(DefaultYieldAfterMilliseconds);
        private long yieldAfterTicks = ToStopwatchTicks(TimeSpan.FromMilliseconds(DefaultYieldAfterMilliseconds));
        /// <summary>
        /// Gets or sets the amount of time to execute tasks for before yielding back to Unity. Default is 4 ms.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each frame's pass runs, in order, what was queued before it, and stops once its budget is used up; the rest
        /// waits at the head of the queue for the next frame. Ordinary frames finish well inside 4 ms, which is under a
        /// third of a 72 Hz frame (13.9 ms) and half of a 120 Hz one (8.3 ms): the budget only bites when work backs up,
        /// and then spreads it over frames instead of stalling one. The budget is checked after each task, so a long
        /// task overruns it, and at least one task runs every pass.
        /// </para>
        /// <para>
        /// A pass that runs out of time doubles the next pass's budget, up to one 72 Hz frame, until a pass gets through
        /// everything queued before it; so work arriving faster than 4 ms a frame can't grow the queue without limit.
        /// While a world loads (<see cref="SetLoading"/>) the budget is 25 ms instead.
        /// </para>
        /// </remarks>
        /// <value>the amount of time to execute tasks for before yielding back to Unity</value>
        public TimeSpan YieldAfterTime
        {
            get => yieldAfterTime;
            set
            {
                ThrowIfDisposed();
                if (value <= TimeSpan.Zero)
                    throw new ArgumentException("Value must be greater than zero", nameof(value));
                yieldAfterTime = value;
                yieldAfterTicks = ToStopwatchTicks(value);
            }
        }

        private static long ToStopwatchTicks(TimeSpan time)
        {
            long seconds = time.Ticks / TimeSpan.TicksPerSecond, rest = time.Ticks % TimeSpan.TicksPerSecond;
            if (seconds > (long.MaxValue - Stopwatch.Frequency) / Stopwatch.Frequency) return long.MaxValue;
            return seconds * Stopwatch.Frequency + rest * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
        }

        // The budget's time source, in Stopwatch ticks. Tests swap in a fake clock.
        internal Func<long> Clock = Stopwatch.GetTimestamp;

        // While a world loads, the queue holds its object and component creation and the loading cage hides long frames.
        // At 4 ms a large load took several times longer, and components still queued were invisible to the load gate.
        private const double LoadingYieldAfterMilliseconds = 25;
        private long loadingYieldAfterTicks = ToStopwatchTicks(TimeSpan.FromMilliseconds(LoadingYieldAfterMilliseconds));
        private volatile bool loading;

        // One 72 Hz frame (Greenfield's default refresh rate), from ticks: Mono's FromMilliseconds rounds to whole ms.
        private long escalationCapTicks = ToStopwatchTicks(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 72));
        private long escalatedTicks;

        private long lastBacklogWarning;
        private bool backlogWarned;

        /// <summary>How many <c>[Scheduler]</c> backlog warnings this scheduler has logged.</summary>
        internal int BacklogWarnings { get; private set; }

        /// <summary>Items in the queue, including any a pass will skip because they already ran inline.</summary>
        internal int QueuedCount => tasks.Count;

        /// <summary>Whether the loading budget is in use. See <see cref="SetLoading"/>.</summary>
        internal bool IsLoading => loading;

        /// <summary>The time budget of the next pass, in Stopwatch ticks.</summary>
        internal long PassBudgetTicks => loading
            ? Math.Max(loadingYieldAfterTicks, yieldAfterTicks)
            : Math.Max(yieldAfterTicks, escalatedTicks);

        /// <summary>
        /// Uses the 25 ms loading budget instead of <see cref="YieldAfterTime"/> while <paramref name="value"/> is
        /// true. For the scene while a world loads behind the loading cage, where long frames are hidden and queued
        /// creation should finish as soon as possible. Cleared by <see cref="Cancel"/> and whenever
        /// <see cref="Coroutine"/> starts, so it can't outlive a play session; a domain reload builds a new scheduler.
        /// </summary>
        internal void SetLoading(bool value) => loading = value;

        /// <summary>
        /// Counts items queued with <see cref="EnqueueCounted"/> that haven't run yet, so a caller can tell work that
        /// is still queued from work that never came. Thread-safe.
        /// </summary>
        internal sealed class PendingCounter
        {
            private int count;
            internal int Count => Volatile.Read(ref count);
            internal void Add() => Interlocked.Increment(ref count);
            internal void Release() => Interlocked.Decrement(ref count);
        }

        /// <summary>
        /// <see cref="Enqueue(Action)"/>, counted in <paramref name="counter"/> from now until the action has run
        /// (thrown included), or an editor <see cref="Cancel"/> has dropped it. Only the action's synchronous part is
        /// covered: an async lambda counts as run at its first await.
        /// </summary>
        internal void EnqueueCounted(Action action, PendingCounter counter)
        {
            ThrowIfDisposed();

            counter.Add();
            tasks.Enqueue(new(BS.Bridge.BridgeContext.Bind(action)) { EnqueuedAt = Clock(), Counter = counter });
        }

        /// <summary>
        /// When used as a Unity coroutine, runs the scheduler. Otherwise, this is an invalid call.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Do not ever call <see cref="UnityEngine.MonoBehaviour.StopCoroutine(IEnumerator)"/> on this
        /// coroutine, nor <see cref="UnityEngine.MonoBehaviour.StopAllCoroutines"/> on the behaviour hosting
        /// this coroutine. This has no way to detect this, and this object will become invalid.
        /// </para>
        /// <para>
        /// If you need to stop this coroutine, first call <see cref="Cancel"/>, then wait for it to
        /// exit on its own.
        /// </para>
        /// </remarks>
        /// <returns>a Unity coroutine</returns>
        /// <exception cref="ObjectDisposedException">if this scheduler is disposed</exception>
        /// <exception cref="InvalidOperationException">if the scheduler is already running</exception>
        public IEnumerator Coroutine()
        {
            //UnityEngine.Debug.LogError("ENTERED THE COROUTINE");
            ThrowIfDisposed();

            if (IsRunning)
                throw new InvalidOperationException("Scheduler already running");

            Cancelling = false;
            IsRunning = true;
            // A new session: nothing from the last one's load carries over.
            loading = false;
            escalatedTicks = 0;
            yield return null; // yield immediately

            // Here rather than static: the first use of Default, which runs the static initializers, can be on any thread.
            var passMarker = new ProfilerMarker("UnityMainThreadTaskScheduler.RunPass");

            try
            {
                while (!Cancelling)
                {
                    if (!tasks.IsEmpty)
                    {
                        using (passMarker.Auto())
                            RunPass();
                    }
                    yield return null;
                }
            }
            finally
            {
                IsRunning = false;
                UnityEngine.Debug.LogError("EXITED THE COROUTINE, BAD THING TO HAPPEN");
            }

        }

        /// <summary>
        /// One resume's work: runs, in order, the items that were queued when the pass began, stopping early once
        /// <see cref="PassBudgetTicks"/> or <see cref="YieldAfterTasks"/> is used up. Items it doesn't reach stay at
        /// the head of the queue for the next pass. Work queued during the pass, by a task or another thread, waits for
        /// the next pass, so a task that enqueues itself can't spin the loop. At least one task runs per pass. Running
        /// out of time doubles the next pass's budget (see <see cref="YieldAfterTime"/>); getting through the whole
        /// snapshot resets it.
        /// </summary>
        /// <returns>the number of items taken off the queue, including ones already run inline</returns>
        internal int RunPass()
        {
            // Snapshot the count. Testing tasks.Count on every iteration while dequeuing ran only about half the queue
            // a pass, and always left the last task of a burst for a frame on its own.
            int queued = tasks.Count;
            int taken = 0, ran = 0;
            long budget = PassBudgetTicks;
            long start = Clock(), now = start, lastRunEnqueuedAt = start;
            bool outOfTime = false;
            while (taken < queued && tasks.TryDequeue(out var task))
            {
                taken++;
                if (!task.HasTask) continue; // a Task that TryExecuteTaskInline already ran
                lastRunEnqueuedAt = task.EnqueuedAt;
                try
                {
                    RunItem(task);
                }
                finally
                {
                    task.Counter?.Release();
                }
                if (++ran >= yieldAfterTasks) break;
                now = Clock();
                if (now - start >= budget)
                {
                    outOfTime = true;
                    break;
                }
            }

            if (taken >= queued)
            {
                escalatedTicks = 0; // caught up with everything queued before the pass
            }
            else if (outOfTime)
            {
                long cap = Math.Max(yieldAfterTicks, escalationCapTicks);
                escalatedTicks = budget >= cap / 2 ? cap : budget * 2;
                WarnIfBehind(now, lastRunEnqueuedAt);
            }
            return taken;
        }

        // [Scheduler] warning once the oldest queued item has waited a second, at most every ten seconds.
        private void WarnIfBehind(long now, long lastRunEnqueuedAt)
        {
            long second = Stopwatch.Frequency;
            if (backlogWarned && now - lastBacklogWarning < 10 * second) return;
            // The queue's head was queued no earlier than the item that just ran: skip the peek (which stops the
            // queue reusing its head segment) until that one has waited a second.
            if (now - lastRunEnqueuedAt < second || !tasks.TryPeek(out var oldest)) return;
            long waited = now - oldest.EnqueuedAt;
            if (waited < second) return;
            backlogWarned = true;
            lastBacklogWarning = now;
            BacklogWarnings++;
            UnityEngine.Debug.LogWarning(FormattableString.Invariant(
                $"[Scheduler] The main-thread queue is behind: {tasks.Count} items queued, the oldest for {waited / (double)second:0.0} s (next pass budget {PassBudgetTicks * 1000.0 / second:0.#} ms{(loading ? ", loading" : "")})."));
        }

        private void RunItem(QueueItem task)
        {
            try
            {
                if (task.Task is not null)
                {
                    if (!TryExecuteTask(task.Task))
                    {

                        if (task.Task?.Exception != null)
                        {
                            UnityEngine.Debug.LogError("TryExecuteTask failed with exception:");
                            UnityEngine.Debug.LogException(task.Task.Exception);
                        }
                        else
                        {
                            UnityEngine.Debug.LogError("TryExecuteTask failed without exception");
                        }

                    }
                }
                if (task.Action is not null)
                {
                    try
                    {
                        task.Action.Invoke();
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError("task.Action exception occurred:");
                        UnityEngine.Debug.LogException(ex);
                    }
                }
                else if (task.Coroutine is not null)
                {
                    try
                    {
                        _monoBehaviour.StartCoroutine(task.Coroutine);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError("task.Coroutine exception occurred:");
                        UnityEngine.Debug.LogException(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("Thread Scheduler unhandled exception dealing with task!");
                UnityEngine.Debug.LogException(ex);
            }
        }

        private MonoBehaviour _monoBehaviour = null;

        internal void SetMonoBehaviour(MonoBehaviour m) => _monoBehaviour = m;

        /// <summary>
        /// Cancels the scheduler. If the scheduler is currently executing tasks, that batch will finish first.
        /// All remaining tasks will be left in the queue, except in the editor, where they are dropped.
        /// </summary>
        /// <exception cref="ObjectDisposedException">if this scheduler is disposed</exception>
        /// <exception cref="InvalidOperationException">if the scheduler is not running</exception>
        public void Cancel()
        {
            ThrowIfDisposed();

            if (!IsRunning) throw new InvalidOperationException("The scheduler is not running");
            Cancelling = true;
            loading = false;
            escalatedTicks = 0;
#if UNITY_EDITOR
            IsRunning = false;
            // Play stopped. With Enter Play Mode Options skipping the domain reload this static scheduler
            // lives on, and work the ending session queued (Visual Scripting user-left events for the
            // players destroyed at exit) would run at the start of the next one.
            while (tasks.TryDequeue(out var dropped)) dropped.Counter?.Release();
#endif
        }

        /// <summary>
        /// Throws a <see cref="NotSupportedException"/>.
        /// </summary>
        /// <returns>nothing</returns>
        /// <exception cref="NotSupportedException">Always.</exception>
        protected override IEnumerable<Task> GetScheduledTasks()
            => tasks.ToArray().Where(q => q.HasTask).Select(q => q.Task).NonNull().ToArray();

        /// <summary>
        /// Queues a given <see cref="Task"/> to this scheduler. The <see cref="Task"/> <i>must</i> be
        /// scheduled for this <see cref="TaskScheduler"/> by the runtime.
        /// </summary>
        /// <param name="task">the <see cref="Task"/> to queue</param>
        /// <exception cref="ObjectDisposedException">Thrown if this object has already been disposed.</exception>
        protected override void QueueTask(Task task)
        {
            ThrowIfDisposed();

            var item = new QueueItem(task) { EnqueuedAt = Clock() };
            itemTable.Add(task, item);
            tasks.Enqueue(item);
        }

        public void Enqueue(Action action)
        {
            ThrowIfDisposed();

            // Bridge 2: queued work answers the document whose op queued it (a bare delegate does not carry the
            // ExecutionContext, unlike a Task or an await). The action itself when no document epoch is set.
            tasks.Enqueue(new(BS.Bridge.BridgeContext.Bind(action)) { EnqueuedAt = Clock() });
        }

        private IEnumerator ActionWrapper(Action a)
        {
            a();
            yield return null;
        }

        public Task EnqueueAsync(Action action)
        {
            ThrowIfDisposed();

            var tcs = new TaskCompletionSource<bool>();
            Enqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }


        public void Enqueue(IEnumerator coroutine)
        {
            ThrowIfDisposed();

            // Bridge 2: each step of the coroutine (and coroutines it yields) runs in the queuing document's epoch.
            tasks.Enqueue(new(BS.Bridge.BridgeContext.Bind(coroutine)) { EnqueuedAt = Clock() });
        }

        /// <summary>
        /// Runs the task inline if the current thread is the Unity main thread.
        /// </summary>
        /// <param name="task">the task to attempt to execute</param>
        /// <param name="taskWasPreviouslyQueued">whether the task was previously queued to this scheduler</param>
        /// <returns><see langword="false"/> if the task could not be run, <see langword="true"/> if it was</returns>
        /// <exception cref="ObjectDisposedException">Thrown if this object has already been disposed.</exception>
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            ThrowIfDisposed();

            if (!UnityGame.OnMainThread) return false;

            if (taskWasPreviouslyQueued)
            {
                if (itemTable.TryGetValue(task, out var item))
                {
                    if (!item.HasTask) return false;
                    item.HasTask = false;
                }
                else return false; // if we couldn't remove it, its not in our queue, so it already ran
            }

            return TryExecuteTask(task);
        }

        private void ThrowIfDisposed()
        {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(SingleThreadTaskScheduler));
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        /// <summary>
        /// Disposes this object.
        /// </summary>
        /// <param name="disposing">whether or not to dispose managed objects</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {

                }

                disposedValue = true;
            }
        }

        /// <summary>
        /// Disposes this object. This puts the object into an unusable state.
        /// </summary>
        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }

}