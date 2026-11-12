using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BS.Bridge;

/// <summary>
/// BSScene's Unity -> JS property queue. The spin-lock version it replaced stranded updates: an update landing between
/// "nothing left" and the worker clearing itself sat there until a later update woke it, and a burst of component reads
/// stalled until GetProperties gave up.
/// </summary>
static class DrainQueueTests
{
    static bool WaitFor(Func<bool> done, int ms)
    {
        var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * ms / 1000;
        while (!done())
        {
            if (Stopwatch.GetTimestamp() > until) return false;
            Thread.SpinWait(50);
        }
        return true;
    }

    public static void Run()
    {
        T.Run("drain queue: a burst is never left waiting for a later add, one worker at a time, in order", () =>
        {
            long done = 0, last = 0;
            int inside = 0, overlaps = 0, outOfOrder = 0;
            var q = new DrainQueue<long>(batch =>
            {
                if (Interlocked.Increment(ref inside) != 1) overlaps++;
                foreach (var n in batch)
                {
                    if (n != last + 1) outOfOrder++;
                    last = n;
                }
                Interlocked.Add(ref done, batch.Count);
                Interlocked.Decrement(ref inside);
            }, drain => Task.Run(drain), e => throw e);

            var rng = new Random(1);
            var one = new List<long> { 0 };
            long sent = 0;
            // The old queue stranded about once in 2,400 of these bursts.
            for (int burst = 0; burst < 150_000; burst++)
            {
                int n = 1 + rng.Next(12);
                for (int i = 0; i < n; i++)
                {
                    one[0] = ++sent;
                    q.Add(one);
                    Thread.SpinWait(rng.Next(400));
                }
                var want = sent;
                T.True(WaitFor(() => Interlocked.Read(ref done) >= want, 2000), "stranded at burst " + burst);
            }
            T.Eq(0, overlaps, "workers at once");
            T.Eq(0, outOfOrder, "out of order");
        });

        T.Run("drain queue: what the worker itself adds is drained too", () =>
        {
            var seen = new List<int>();
            DrainQueue<int> q = null;
            q = new DrainQueue<int>(batch =>
            {
                seen.AddRange(batch);
                if (batch[0] < 3) q.Add(new List<int> { batch[0] + 1 });
            }, drain => drain(), e => throw e);
            q.Add(new List<int> { 1 });
            T.Seq(new[] { 1, 2, 3 }, seen);
        });

        T.Run("drain queue: a batch that throws doesn't stop the next one", () =>
        {
            var seen = new List<int>();
            var errors = new List<Exception>();
            var q = new DrainQueue<int>(batch =>
            {
                if (batch[0] == 1) throw new InvalidOperationException("bad");
                seen.AddRange(batch);
            }, drain => drain(), errors.Add);
            q.Add(new List<int> { 1 });
            q.Add(new List<int> { 2 });
            T.Eq(1, errors.Count, "errors");
            T.Seq(new[] { 2 }, seen);
        });

        T.Run("drain queue: the caller can reuse its list straight away", () =>
        {
            var seen = new List<int>();
            Action held = null;
            var q = new DrainQueue<int>(seen.AddRange, drain => held = drain, e => throw e);
            var list = new List<int> { 1 };
            q.Add(list);
            list.Clear();
            held();
            T.Seq(new[] { 1 }, seen);
        });
    }
}
