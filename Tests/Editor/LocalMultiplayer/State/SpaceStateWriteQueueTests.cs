using System;
using System.Collections.Generic;
using BS.LocalMultiplayer.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The coalescing space-state queue (production SpaceStateWriteQueue.cs): latest value wins per (scope, path) in
    /// its original position, a batch never mixes scopes or splits a write's ops, nothing leaves before its ack, and
    /// writes queued for 60 s while offline are dropped.
    /// </summary>
    public class SpaceStateWriteQueueTests
    {
        static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        static int OneSlot(PendingWrite write) => 1;

        static List<string> Paths(List<PendingWrite> writes)
        {
            var paths = new List<string>();
            foreach (var write in writes) paths.Add(write.Path);
            return paths;
        }

        [Test]
        public void Constants_MatchProduction()
        {
            Assert.AreEqual(64, SpaceStateWriteQueue.MaxOpsPerBatch);
            Assert.AreEqual(60.0, SpaceStateWriteQueue.MaxPendingAgeSeconds);
        }

        [Test]
        public void LatestValueWins_AndKeepsItsPlace()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("a", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Enqueue("b", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Enqueue("a", "2", StateWriteKind.Set, RoomStateScope.Public, T0);

            Assert.AreEqual(2, queue.Count);
            var batch = queue.TakeBatch(64, OneSlot);
            CollectionAssert.AreEqual(new[] { "a", "b" }, Paths(batch));
            Assert.AreEqual("2", (string)batch[0].Value);
        }

        [Test]
        public void SamePathInBothScopes_IsTwoWrites()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("a", "pub", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Enqueue("a", "prot", StateWriteKind.Set, RoomStateScope.Protected, T0);
            Assert.AreEqual(2, queue.Count);
        }

        [Test]
        public void TakeBatch_DrainsOnlyTheOldestScope()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("p1", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Enqueue("x1", "1", StateWriteKind.Set, RoomStateScope.Protected, T0);
            queue.Enqueue("p2", "1", StateWriteKind.Set, RoomStateScope.Public, T0);

            var first = queue.TakeBatch(64, OneSlot);
            CollectionAssert.AreEqual(new[] { "p1", "p2" }, Paths(first));
            foreach (var write in first) Assert.AreEqual(RoomStateScope.Public, write.Scope);

            queue.Ack(first);
            var second = queue.TakeBatch(64, OneSlot);
            CollectionAssert.AreEqual(new[] { "x1" }, Paths(second));
            Assert.AreEqual(RoomStateScope.Protected, second[0].Scope);
        }

        [Test]
        public void TakeBatch_NeverSplitsAWrite_AndTakesAnOversizedOneAlone()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("a", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Enqueue("b", "1", StateWriteKind.Replace, RoomStateScope.Public, T0);
            int Slots(PendingWrite write) => write.Kind == StateWriteKind.Replace ? 2 : 1;

            // a (1) + b (2) needs 3 slots: with 2, b waits for the next batch rather than being split.
            CollectionAssert.AreEqual(new[] { "a" }, Paths(queue.TakeBatch(2, Slots)));

            var lone = new SpaceStateWriteQueue();
            lone.Enqueue("big", "1", StateWriteKind.Replace, RoomStateScope.Public, T0);
            // A single write bigger than the batch would wedge the queue, so it goes alone.
            CollectionAssert.AreEqual(new[] { "big" }, Paths(lone.TakeBatch(1, Slots)));
        }

        [Test]
        public void TakeBatch_LeavesWritesPendingUntilAcked()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("a", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.TakeBatch(64, OneSlot);
            Assert.AreEqual(1, queue.Count, "nothing is removed before the ack");
        }

        [Test]
        public void Ack_KeepsAWriteThatChangedMidFlight()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("a", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Enqueue("b", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            var inFlight = queue.TakeBatch(64, OneSlot);

            queue.Enqueue("a", "2", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Ack(inFlight);

            Assert.AreEqual(1, queue.Count);
            var resend = queue.TakeBatch(64, OneSlot);
            CollectionAssert.AreEqual(new[] { "a" }, Paths(resend));
            Assert.AreEqual("2", (string)resend[0].Value, "the newer value is sent again");
        }

        [Test]
        public void Drop_OnlyDropsTheWriteThatWasTaken()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("a", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            var taken = queue.TakeBatch(64, OneSlot)[0];

            queue.Enqueue("a", "2", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Drop(taken);
            Assert.AreEqual(1, queue.Count, "a newer write survives the drop of an older one");

            queue.Drop(queue.TakeBatch(64, OneSlot)[0]);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void DropStale_DropsWritesOlderThan60Seconds()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("old", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Enqueue("new", "1", StateWriteKind.Set, RoomStateScope.Public, T0.AddSeconds(30));

            Assert.AreEqual(0, queue.DropStale(T0.AddSeconds(60)).Count, "exactly 60 s is not stale yet");
            var dropped = queue.DropStale(T0.AddSeconds(61));
            CollectionAssert.AreEqual(new[] { "old" }, Paths(dropped));
            Assert.AreEqual(1, queue.Count);
            CollectionAssert.AreEqual(new[] { "new" }, Paths(queue.TakeBatch(64, OneSlot)));
        }

        [Test]
        public void Updating_RestartsTheStaleClock()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("a", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Enqueue("a", "2", StateWriteKind.Set, RoomStateScope.Public, T0.AddSeconds(50));
            Assert.AreEqual(0, queue.DropStale(T0.AddSeconds(70)).Count);
        }

        [Test]
        public void EmptyPaths_AreIgnored_AndClearEmptiesTheQueue()
        {
            var queue = new SpaceStateWriteQueue();
            queue.Enqueue("", "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            queue.Enqueue(null, "1", StateWriteKind.Set, RoomStateScope.Public, T0);
            Assert.AreEqual(0, queue.Count);

            queue.Enqueue("a", JValue.CreateNull(), StateWriteKind.Delete, RoomStateScope.Public, T0);
            queue.Clear();
            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(0, queue.TakeBatch(64, OneSlot).Count);
        }
    }
}
