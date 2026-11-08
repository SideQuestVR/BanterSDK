using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BS.Utilities.Async;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// UnityMainThreadTaskScheduler's per-frame pass (issue 22): it runs everything queued before the pass, in order,
    /// within a time budget, and carries the rest over; running out of time doubles the next budget up to one frame,
    /// and a world load uses a larger budget. Each MoveNext of Coroutine() is one frame. Its enumerators are never
    /// disposed: the finally logs "EXITED THE COROUTINE" as an error.
    /// </summary>
    public class MainThreadSchedulerTests
    {
        // Fake time, in Stopwatch ticks, advanced by the tasks themselves.
        long now;

        UnityMainThreadTaskScheduler Started(out IEnumerator frame, UnityMainThreadTaskScheduler scheduler = null)
        {
            scheduler ??= new UnityMainThreadTaskScheduler();
            scheduler.Clock = () => now;
            frame = scheduler.Coroutine();
            Assert.IsTrue(frame.MoveNext(), "the immediate first yield");
            return scheduler;
        }

        static long Ticks(TimeSpan time) => time.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
        static long Budget(UnityMainThreadTaskScheduler s) => Ticks(s.YieldAfterTime);
        static long FrameCap => Ticks(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 72));
        static long LoadingBudget => Ticks(TimeSpan.FromMilliseconds(25));

        [Test]
        public void OnePass_RunsEveryTaskQueuedBeforeIt()
        {
            var scheduler = Started(out var frame);
            int ran = 0;
            for (int i = 0; i < 10; i++) scheduler.Enqueue(() => ran++);
            frame.MoveNext();
            Assert.AreEqual(10, ran); // 5 before the fix
        }

        [Test]
        public void TwoWritesFromOneTick_LandInTheSameFrame()
        {
            var scheduler = Started(out var frame);
            string label = null;
            scheduler.Enqueue(() => label = "A");
            scheduler.Enqueue(() => label = "B");
            frame.MoveNext();
            Assert.AreEqual("B", label, "when the pass isn't over budget, a frame never draws the intermediate A");
        }

        [Test]
        public void WorkQueuedDuringAPass_WaitsForTheNextOne()
        {
            var scheduler = Started(out var frame);
            int ran = 0;
            Action again = null;
            again = () => { ran++; scheduler.Enqueue(again); };
            scheduler.Enqueue(again);
            frame.MoveNext(); Assert.AreEqual(1, ran);
            frame.MoveNext(); Assert.AreEqual(2, ran);
        }

        // Hex: a task in the pass makes another thread enqueue and waits until that Enqueue has returned. The new item
        // is in the queue before the pass ends, but after the snapshot, so it waits for the next pass.
        [Test]
        public void ItemEnqueuedByAnotherThreadDuringThePass_WaitsForTheNextPass()
        {
            var scheduler = Started(out var frame);
            int late = 0;
            bool joinTimedOut = false; // never block a test run forever
            scheduler.Enqueue(() =>
            {
                var thread = new Thread(() => scheduler.Enqueue(() => late++));
                thread.Start();
                if (!thread.Join(10000)) joinTimedOut = true;
            });
            scheduler.Enqueue(() => { });
            frame.MoveNext();
            Assert.IsFalse(joinTimedOut, "the other thread's Enqueue did not return");
            Assert.AreEqual(0, late);
            frame.MoveNext();
            Assert.AreEqual(1, late);
        }

        [Test]
        public void DefaultBudget_IsFourMilliseconds_AndTaskCountIsUnlimited()
        {
            Assert.AreEqual(TimeSpan.FromMilliseconds(4), UnityMainThreadTaskScheduler.Default.YieldAfterTime);
            Assert.AreEqual(int.MaxValue, UnityMainThreadTaskScheduler.Default.YieldAfterTasks);

            var scheduler = Started(out var frame);
            Assert.AreEqual(Budget(scheduler), scheduler.PassBudgetTicks);
            long quarter = (Budget(scheduler) + 3) / 4; // about 1 ms
            int ran = 0;
            for (int i = 0; i < 6; i++) scheduler.Enqueue(() => { now += quarter; ran++; });
            frame.MoveNext();
            Assert.AreEqual(4, ran, "1 ms tasks: the fourth reaches 4 ms");
        }

        [Test]
        public void OverBudget_TheRestCarriesOverInOrder_EachTaskOnce()
        {
            var scheduler = Started(out var frame);
            long step = Budget(scheduler) * 3 / 10; // the fourth task crosses the budget
            var order = new List<int>();
            for (int i = 0; i < 10; i++) { int k = i; scheduler.Enqueue(() => { now += step; order.Add(k); }); }

            var perFrame = new List<int>();
            while (order.Count < 10) { int before = order.Count; frame.MoveNext(); perFrame.Add(order.Count - before); }

            CollectionAssert.AreEqual(new[] { 4, 6 }, perFrame, "the second pass has twice the budget");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, order);
        }

        [Test]
        public void CarriedOverWork_RunsBeforeWorkQueuedLater()
        {
            var scheduler = Started(out var frame);
            long step = Budget(scheduler) * 3 / 10;
            var order = new List<string>();
            for (int i = 0; i < 6; i++) { int k = i; scheduler.Enqueue(() => { now += step; order.Add("old" + k); }); }
            frame.MoveNext();
            scheduler.Enqueue(() => order.Add("new"));
            frame.MoveNext();
            CollectionAssert.AreEqual(new[] { "old0", "old1", "old2", "old3", "old4", "old5", "new" }, order);
        }

        [Test]
        public void ATaskLongerThanTheBudget_StillRuns_OnePerFrame()
        {
            var scheduler = Started(out var frame);
            long step = FrameCap * 2;
            int ran = 0;
            for (int i = 0; i < 3; i++) scheduler.Enqueue(() => { now += step; ran++; });
            frame.MoveNext(); Assert.AreEqual(1, ran);
            frame.MoveNext(); Assert.AreEqual(2, ran);
            frame.MoveNext(); Assert.AreEqual(3, ran);
        }

        [Test]
        public void OutOfTime_DoublesTheNextBudget_UpToOneFrame_ThenResets()
        {
            var scheduler = Started(out var frame);
            long budget = Budget(scheduler), quarter = (budget + 3) / 4; // about 1 ms
            int ran = 0;
            for (int i = 0; i < 40; i++) scheduler.Enqueue(() => { now += quarter; ran++; });

            var budgets = new List<long>();
            var perFrame = new List<int>();
            while (ran < 40)
            {
                budgets.Add(scheduler.PassBudgetTicks);
                int before = ran;
                frame.MoveNext();
                perFrame.Add(ran - before);
            }

            CollectionAssert.AreEqual(new[] { budget, 2 * budget, FrameCap, FrameCap }, budgets);
            CollectionAssert.AreEqual(new[] { 4, 8, 14, 14 }, perFrame);
            Assert.AreEqual(budget, scheduler.PassBudgetTicks, "caught up: back to the normal budget");
        }

        // Hex's sustained-arrival model: 5 ms of work arrives every frame against the 4 ms budget. Without escalation
        // the queue grew by a millisecond's worth every frame, forever.
        [Test]
        public void ArrivalsAboveTheBudget_DoNotGrowTheQueue()
        {
            var scheduler = Started(out var frame);
            long cost = Budget(scheduler) / 20; // 0.2 ms
            int maxQueued = 0;
            for (int f = 0; f < 500; f++)
            {
                for (int i = 0; i < 25; i++) scheduler.Enqueue(() => now += cost);
                frame.MoveNext();
                maxQueued = Math.Max(maxQueued, scheduler.QueuedCount);
            }
            Assert.LessOrEqual(maxQueued, 25, "never more than a frame's arrivals left over");
        }

        [Test]
        public void YieldAfterTasks_IsHonoured()
        {
            var scheduler = Started(out var frame);
            scheduler.YieldAfterTasks = 3;
            int ran = 0;
            for (int i = 0; i < 7; i++) scheduler.Enqueue(() => ran++);
            frame.MoveNext(); Assert.AreEqual(3, ran);
            frame.MoveNext(); Assert.AreEqual(6, ran);
            frame.MoveNext(); Assert.AreEqual(7, ran);
        }

        [Test]
        public void Loading_UsesTheLoadingBudget_ThenOneFrameUntilCaughtUp()
        {
            var scheduler = Started(out var frame);
            long budget = Budget(scheduler), quarter = (budget + 3) / 4; // about 1 ms
            int ran = 0;
            for (int i = 0; i < 60; i++) scheduler.Enqueue(() => { now += quarter; ran++; });

            scheduler.SetLoading(true);
            Assert.IsTrue(scheduler.IsLoading);
            Assert.AreEqual(LoadingBudget, scheduler.PassBudgetTicks);
            frame.MoveNext();
            Assert.AreEqual(25, ran, "25 ms while loading");

            scheduler.SetLoading(false);
            Assert.AreEqual(FrameCap, scheduler.PassBudgetTicks, "load over, backlog left: a frame's worth until it catches up");
            frame.MoveNext(); Assert.AreEqual(39, ran);
            frame.MoveNext(); Assert.AreEqual(53, ran);
            frame.MoveNext(); Assert.AreEqual(60, ran);
            Assert.AreEqual(budget, scheduler.PassBudgetTicks);
        }

        // The editor Cancel at play-mode exit, then the next session's Coroutine: neither the loading budget nor counted
        // items may outlive the session.
        [Test]
        public void LoadingAndCountedItems_DoNotOutliveTheSession()
        {
            var scheduler = Started(out var frame);
            var pending = new UnityMainThreadTaskScheduler.PendingCounter();
            scheduler.SetLoading(true);
            for (int i = 0; i < 3; i++) scheduler.EnqueueCounted(() => { }, pending);
            Assert.AreEqual(3, pending.Count);

            scheduler.Cancel(); // editor: drops the queue
            Assert.IsFalse(scheduler.IsLoading);
            Assert.AreEqual(0, pending.Count, "dropped items are released");
            Assert.AreEqual(Budget(scheduler), scheduler.PassBudgetTicks);

            scheduler.SetLoading(true); // a load that never cleared it
            var next = scheduler.Coroutine();
            Assert.IsTrue(next.MoveNext());
            Assert.IsFalse(scheduler.IsLoading, "a new session starts with the normal budget");
        }

        [Test]
        public void CountedItems_StayCountedUntilTheyRun()
        {
            var scheduler = Started(out var frame);
            var pending = new UnityMainThreadTaskScheduler.PendingCounter();
            long quarter = (Budget(scheduler) + 3) / 4;
            for (int i = 0; i < 6; i++) scheduler.EnqueueCounted(() => now += quarter, pending);
            scheduler.EnqueueCounted(() => throw new InvalidOperationException("counted boom"), pending);
            scheduler.Enqueue(() => { });
            Assert.AreEqual(7, pending.Count);

            frame.MoveNext();
            Assert.AreEqual(3, pending.Count, "four ran; the rest are still queued");

            LogAssert.Expect(LogType.Error, "task.Action exception occurred:");
            LogAssert.Expect(LogType.Exception, new Regex("counted boom"));
            frame.MoveNext();
            Assert.AreEqual(0, pending.Count, "one that throws is released too");
        }

        // The Clock stamps an item as it is built for the queue. The count must already include it by then: a reader on
        // another thread (the load gate) must never see an item queued but not yet counted.
        [Test]
        public void ACountedItem_IsCountedBeforeItIsQueued()
        {
            var scheduler = Started(out var frame);
            var pending = new UnityMainThreadTaskScheduler.PendingCounter();
            int countWhenStamped = -1;
            scheduler.Clock = () => { countWhenStamped = pending.Count; return now; };
            scheduler.EnqueueCounted(() => { }, pending);
            Assert.AreEqual(1, countWhenStamped);
        }

        [Test]
        public void BacklogWarning_IgnoresFreshWorkBehindAnOldItem()
        {
            var scheduler = Started(out var frame);
            long second = Stopwatch.Frequency, budget = Budget(scheduler);
            scheduler.Enqueue(() => now += budget); // old, and uses up the pass
            now += 2 * second;
            for (int i = 0; i < 3; i++) scheduler.Enqueue(() => { });
            frame.MoveNext();
            Assert.AreEqual(0, scheduler.BacklogWarnings, "the oldest item still queued has waited a few ms");
        }

        [Test]
        public void BacklogWarning_NamesTheOldestWait_AtMostEveryTenSeconds()
        {
            var scheduler = Started(out var frame);
            long second = Stopwatch.Frequency, quarter = (Budget(scheduler) + 3) / 4;
            for (int i = 0; i < 400; i++) scheduler.Enqueue(() => now += quarter);

            frame.MoveNext();
            Assert.AreEqual(0, scheduler.BacklogWarnings, "out of time, but nothing has waited a second");

            now += 2 * second; // a stall
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Scheduler\] .* the oldest for 2\.\d s"));
            frame.MoveNext();
            Assert.AreEqual(1, scheduler.BacklogWarnings);

            now += 2 * second;
            frame.MoveNext();
            Assert.AreEqual(1, scheduler.BacklogWarnings, "within ten seconds of the last one");

            now += 10 * second;
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Scheduler\] "));
            frame.MoveNext();
            Assert.AreEqual(2, scheduler.BacklogWarnings);
        }

        [Test]
        public void AThrowingTask_DoesNotStopThePass()
        {
            var scheduler = Started(out var frame);
            int ran = 0;
            scheduler.Enqueue(() => ran++);
            scheduler.Enqueue(() => throw new InvalidOperationException("scheduler test boom"));
            scheduler.Enqueue(() => ran++);
            LogAssert.Expect(LogType.Error, "task.Action exception occurred:");
            LogAssert.Expect(LogType.Exception, new Regex("scheduler test boom"));
            frame.MoveNext();
            Assert.AreEqual(2, ran);
        }

        [Test]
        public void EnqueueAsync_CompletesInThePass_AndCarriesItsException()
        {
            var scheduler = Started(out var frame);
            var ok = scheduler.EnqueueAsync(() => { });
            var bad = scheduler.EnqueueAsync(() => throw new InvalidOperationException("async boom"));
            frame.MoveNext();
            Assert.AreEqual(TaskStatus.RanToCompletion, ok.Status);
            Assert.IsTrue(bad.IsFaulted);
            // Reading the exception observes it: otherwise the finalizer reports it later, as an error in whichever test runs then.
            Assert.IsInstanceOf<InvalidOperationException>(bad.Exception.InnerException);
        }

        class InlineProbe : UnityMainThreadTaskScheduler
        {
            public bool Inline(Task task) => TryExecuteTaskInline(task, true);
        }

        [Test]
        public void ATaskAlreadyRunInline_IsNotRunAgain_AndCountsTowardsThePass()
        {
            UnityGame.SetMainThread(); // TryExecuteTaskInline only inlines on the main thread
            var scheduler = (InlineProbe)Started(out var frame, new InlineProbe());
            int runs = 0, after = 0, late = 0;
            var task = new TaskFactory(scheduler).StartNew(() => runs++);
            scheduler.Enqueue(() => { after++; scheduler.Enqueue(() => late++); });
            Assert.IsTrue(scheduler.Inline(task));
            Assert.AreEqual(1, runs);
            frame.MoveNext();
            Assert.AreEqual(1, runs, "its queue entry is skipped");
            Assert.AreEqual(1, after);
            Assert.AreEqual(0, late, "the skipped entry is one of the pass's own items, not room for newer work");
        }

        // Hex: an item Waits on a Task queued behind it in the same snapshot (inlined once, its entry skipped later in
        // the pass), and on one it queues itself after the snapshot (inlined, its entry skipped in the next pass).
        [Test]
        public void WaitOnScheduledTask_FromInsideAPass_InlinesOnce()
        {
            UnityGame.SetMainThread();
            var scheduler = Started(out var frame);
            var factory = new TaskFactory(scheduler);
            int t1 = 0, t2 = 0, after = 0;
            Task queuedBehind = null;
            // Wait() with no timeout on purpose: only the untimed Wait inlines a task queued on this scheduler, which is
            // what this test checks (Wait(int) does not, so a deadline here would change the behaviour under test).
            scheduler.Enqueue(() =>
            {
                queuedBehind.Wait();
                var created = factory.StartNew(() => t2++);
                created.Wait();
            });
            queuedBehind = factory.StartNew(() => t1++);
            scheduler.Enqueue(() => after++);
            frame.MoveNext();
            Assert.AreEqual(1, t1); Assert.AreEqual(1, t2); Assert.AreEqual(1, after);
            frame.MoveNext();
            frame.MoveNext();
            Assert.AreEqual(1, t1); Assert.AreEqual(1, t2);
        }

        // Random costs, loading switched on and off, tasks that enqueue more tasks, half of them counted: every task
        // runs once, in the order it was queued, never in the pass that queued it; a pass only stops early just after
        // the task that used up its budget; the counter always equals the counted tasks not yet run.
        [Test]
        public void RandomWorkload_KeepsOrderAndRunsEachTaskOnce([Values(1, 2, 3, 4, 5)] int seed)
        {
            var random = new System.Random(seed);
            var scheduler = Started(out var frame);
            var pending = new UnityMainThreadTaskScheduler.PendingCounter();
            long baseBudget = Budget(scheduler);
            int pass = 0, queuedCount = 0, ranTwice = 0, countedLeft = 0;
            var ranOrder = new List<int>();
            var queuedInPass = new Dictionary<int, int>();
            var ranInPass = new Dictionary<int, int>();
            var costs = new Dictionary<int, long>();

            void Add()
            {
                int id = queuedCount++;
                costs[id] = (long)(baseBudget * random.NextDouble() * (random.Next(10) == 0 ? 8.0 : 0.4));
                queuedInPass[id] = pass;
                bool counted = random.Next(2) == 0;
                Action body = () =>
                {
                    // No Assert in here: the scheduler catches what a task throws.
                    if (ranInPass.ContainsKey(id)) ranTwice++;
                    ranInPass[id] = pass;
                    ranOrder.Add(id);
                    if (counted) countedLeft--;
                    now += costs[id];
                    if (queuedCount < 3000 && random.Next(4) == 0) Add();
                };
                if (counted) { countedLeft++; scheduler.EnqueueCounted(body, pending); }
                else scheduler.Enqueue(body);
            }

            for (int i = 0; i < 400; i++) Add();
            while (ranOrder.Count < queuedCount)
            {
                pass++;
                if (random.Next(15) == 0) scheduler.SetLoading(random.Next(2) == 0);
                int before = ranOrder.Count, queued = queuedCount;
                long start = now, budget = scheduler.PassBudgetTicks;
                frame.MoveNext();
                int ran = ranOrder.Count - before;
                Assert.Greater(ran, 0, "a pass with work always runs something");
                Assert.AreEqual(countedLeft, pending.Count);
                if (before + ran < queued)
                {
                    Assert.GreaterOrEqual(now - start, budget, "stopped early under budget");
                    Assert.Less(now - start - costs[ranOrder[ranOrder.Count - 1]], budget, "ran on past the budget");
                }
                if (queuedCount < 3000 && random.Next(3) == 0) Add(); // work from the next frame's Update
                Assert.Less(pass, 100000);
            }

            Assert.AreEqual(0, ranTwice, "ran twice");
            Assert.AreEqual(queuedCount, ranOrder.Count);
            Assert.AreEqual(0, pending.Count);
            for (int i = 0; i < ranOrder.Count; i++) Assert.AreEqual(i, ranOrder[i], "FIFO");
            foreach (var kv in ranInPass) Assert.Greater(kv.Value, queuedInPass[kv.Key], "ran in the pass that queued it");
        }
    }
}
