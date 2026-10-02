using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The engine's own user state (EditableOwnUserState, PacketPartyClient.cs:2924-3029): a diff of the
    /// document against what the relay has, sent as atomic app.userState.batch chunks of at most 64
    /// operations, deletes first, both in path order.
    /// </summary>
    public class EngineUserStateChannelTests
    {
        sealed class FakeRelay
        {
            public readonly List<JObject> Batches = new List<JObject>();
            public Func<int, JObject> Reply = _ => new JObject { ["ok"] = true };

            public Task<JObject> Send(JObject body, CancellationToken cancellation)
            {
                Batches.Add((JObject)body.DeepClone());
                return Task.FromResult(Reply(Batches.Count));
            }

            public List<string> Ops(int batch) =>
                ((JArray)Batches[batch]["ops"]).Select(op => $"{op["op"]} {op["path"]}" + (op["value"] != null ? $"={op["value"]}" : "")).ToList();
        }

        static EngineUserStateChannel Channel(FakeRelay relay) => new EngineUserStateChannel(relay.Send);

        static void Sync(EngineUserStateChannel channel)
        {
            var task = channel.SyncNowAsync();
            Assert.IsTrue(task.IsCompleted);
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
        }

        [Test]
        public void FirstSync_SetsEveryKey_InPathOrder_Atomically()
        {
            var relay = new FakeRelay();
            var channel = Channel(relay);
            channel.Set("pilot", "seat|0,0.2,0|0,0,0,1");
            channel.Set("attachment_b", "v1|b");
            channel.Set("attachment_a", "v1|a");

            Sync(channel);

            Assert.AreEqual(1, relay.Batches.Count);
            Assert.AreEqual(true, (bool)relay.Batches[0]["atomic"]);
            CollectionAssert.AreEqual(new[]
            {
                "set attachment_a=v1|a",
                "set attachment_b=v1|b",
                "set pilot=seat|0,0.2,0|0,0,0,1",
            }, relay.Ops(0));
        }

        [Test]
        public void LaterSyncs_SendDeletesFirst_ThenChangedSets_EmptyIsAValue()
        {
            var relay = new FakeRelay();
            var channel = Channel(relay);
            channel.Set("k2", "two");
            channel.Set("k1", "one");
            channel.Set("k3", "three");
            Sync(channel);

            channel.Set("k2", null);       // gone: a delete
            channel.Set("k1", "changed");  // changed: a set
            channel.Set("k0", "");         // "" is the attachment bridges' "cleared": a value, not a delete
            Sync(channel);

            Assert.AreEqual(2, relay.Batches.Count);
            CollectionAssert.AreEqual(new[] { "delete k2", "set k0=", "set k1=changed" }, relay.Ops(1));
            Assert.AreEqual(JTokenType.String, relay.Batches[1]["ops"][1]["value"].Type);
        }

        [Test]
        public void NothingChanged_SendsNothing()
        {
            var relay = new FakeRelay();
            var channel = Channel(relay);
            channel.Set("pilot", "x");
            Sync(channel);

            channel.Set("pilot", "x");
            Sync(channel);

            Assert.AreEqual(1, relay.Batches.Count);
        }

        [Test]
        public void ManyKeys_GoInChunksOf64()
        {
            var relay = new FakeRelay();
            var channel = Channel(relay);
            for (var i = 0; i < 150; i++) channel.Set("attachment_" + i.ToString("000"), "v1");

            Sync(channel);

            CollectionAssert.AreEqual(new[] { 64, 64, 22 }, relay.Batches.Select(b => ((JArray)b["ops"]).Count).ToArray());
            Assert.IsTrue(relay.Batches.All(b => (bool)b["atomic"]));
            Assert.AreEqual("set attachment_000=v1", relay.Ops(0)[0]);
            Assert.AreEqual("set attachment_149=v1", relay.Ops(2).Last());
        }

        [Test]
        public void ARefusedChunk_KeepsTheChunksBefore_AndLatchesThatDocument()
        {
            var relay = new FakeRelay();
            relay.Reply = n => n == 2 ? new JObject { ["ok"] = false, ["error"] = "too_many_keys" } : new JObject { ["ok"] = true };
            var channel = Channel(relay);
            for (var i = 0; i < 70; i++) channel.Set("attachment_" + i.ToString("00"), "v1");

            var failed = channel.SyncNowAsync();

            Assert.IsTrue(failed.IsFaulted);
            var error = failed.Exception.InnerExceptions.OfType<LocalRelayException>().Single();
            Assert.AreEqual("too_many_keys", error.Code);
            Assert.AreEqual(2, relay.Batches.Count);

            // The automatic 50 ms sync leaves the same document alone (the failed fingerprint).
            relay.Reply = _ => new JObject { ["ok"] = true };
            channel.Tick(true, 100f, CancellationToken.None);
            Assert.AreEqual(2, relay.Batches.Count);

            // A change lifts the latch; the first chunk is not sent again.
            channel.Set("attachment_99", "v1");
            channel.Tick(true, 200f, CancellationToken.None);
            Assert.AreEqual(3, relay.Batches.Count);
            Assert.AreEqual(7, ((JArray)relay.Batches[2]["ops"]).Count);
            Assert.AreEqual("set attachment_64=v1", relay.Ops(2)[0]);
        }

        [Test]
        public void ANewSession_PushesEverythingAgain_AResumedOneDoesNot()
        {
            var relay = new FakeRelay();
            var channel = Channel(relay);
            channel.Set("pilot", "x");
            channel.Set("attachment_a", "y");
            Sync(channel);

            channel.ResetForResume();
            Sync(channel);
            Assert.AreEqual(1, relay.Batches.Count);

            channel.ResetForNewSession();
            Sync(channel);
            Assert.AreEqual(2, relay.Batches.Count);
            CollectionAssert.AreEqual(new[] { "set attachment_a=y", "set pilot=x" }, relay.Ops(1));
        }

        [Test]
        public void AutoSync_RunsOnlyWhileConnected_AndEvery50Ms()
        {
            var relay = new FakeRelay();
            var channel = Channel(relay);
            channel.Set("pilot", "a");

            channel.Tick(false, 1f, CancellationToken.None);
            Assert.AreEqual(0, relay.Batches.Count);

            channel.Tick(true, 1f, CancellationToken.None);
            Assert.AreEqual(1, relay.Batches.Count);

            channel.Set("pilot", "b");
            channel.Tick(true, 1.02f, CancellationToken.None);
            Assert.AreEqual(1, relay.Batches.Count);
            channel.Tick(true, 1f + EngineUserStateChannel.SyncIntervalSeconds, CancellationToken.None);
            Assert.AreEqual(2, relay.Batches.Count);
        }

        [Test]
        public void BuildOperations_IsProductionsDiff()
        {
            var baseline = new Dictionary<string, JToken>(StringComparer.Ordinal)
            {
                ["b"] = "1",
                ["a"] = "1",
                ["c"] = "same",
            };
            var target = new Dictionary<string, JToken>(StringComparer.Ordinal)
            {
                ["c"] = "same",
                ["e"] = "",
                ["d"] = "new",
            };

            var ops = EngineUserStateChannel.BuildOperations(baseline, target).Select(op => $"{op["op"]} {op["path"]}").ToArray();

            CollectionAssert.AreEqual(new[] { "delete a", "delete b", "set d", "set e" }, ops);
        }

        [Test]
        public void Flatten_UsesPacketPartysDottedLeaves()
        {
            var document = JObject.Parse("{\"a\":{\"b\":1,\"c\":{\"d\":true}},\"e\":[1,2],\"f\":{}}");

            var flat = EngineUserStateChannel.FlatState.Flatten(document);

            CollectionAssert.AreEquivalent(new[] { "a.b", "a.c.d", "e", "f" }, flat.Keys);
            Assert.AreEqual(JTokenType.Array, flat["e"].Type);
            Assert.AreEqual(JTokenType.Object, flat["f"].Type);
        }
    }
}
