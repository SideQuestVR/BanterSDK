using System.Collections.Generic;
using System.Threading;
using BS.LocalMultiplayer.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The ported UserStateService against a fake relay session (production UserStateService.cs:96-471): the
    /// 0.1 s non-atomic flush with deletes first, per-op failures, requeue on transient errors, the rejoin
    /// republish, and inbound state filtered down to the page's props.
    /// </summary>
    public class UserStateServiceTests
    {
        FakeLocalSession _session;
        MainThreadQueue _main;
        UserStateClient _client;
        UserStateService _service;
        float _now;
        List<string> _failures;
        List<string> _changes;
        List<string> _removed;

        [SetUp]
        public void SetUp()
        {
            _session = new FakeLocalSession { OwnRoomSessionId = "rsess_me" };
            _main = new MainThreadQueue();
            _client = new UserStateClient(_session);
            _client.Attach();
            _now = 100f;
            _service = new UserStateService(_client, _main.Post, CancellationToken.None, () => _now);
            _failures = new List<string>();
            _changes = new List<string>();
            _removed = new List<string>();
            _service.WriteFailed += (key, code, message) => _failures.Add(key + ":" + code);
            _service.PropChanged += (rsid, key, value, previous) =>
                _changes.Add($"{rsid} {key}={Show(value)} was {Show(previous)}");
            _service.PropsRemoved += rsid => _removed.Add(rsid);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
            _client.Detach();
            _main.Close();
        }

        static string Show(JToken token) => token == null ? "null" : token.ToString(Newtonsoft.Json.Formatting.None);

        void Frame()
        {
            _now += 1f;
            _service.Tick();
            _main.Drain();
        }

        static JObject Ok() => JObject.Parse("{\"ok\":true,\"revision\":1}");

        JObject LastRequest => _session.Requests[_session.Requests.Count - 1].Body;

        static List<string> Ops(JObject request)
        {
            var ops = new List<string>();
            foreach (var op in (JArray)request["ops"]) ops.Add((string)op["op"] + " " + (string)op["path"]);
            return ops;
        }

        [Test]
        public void Flush_SendsOneNonAtomicBatch_WithDeletesFirst()
        {
            _session.Reply = (type, body) => Ok();
            _service.SetOwnProp("a", "1");
            _service.SetOwnProp("b", JObject.Parse("{\"x\":2}"), UserPropScope.ModeratorWritable);
            _service.RemoveOwnProp("c");
            Frame();

            Assert.AreEqual(1, _session.Requests.Count);
            Assert.AreEqual(UserStateMessages.Batch, _session.Requests[0].Type);
            Assert.AreEqual(false, (bool)LastRequest["atomic"], "one bad prop must not take the rest with it");
            CollectionAssert.AreEqual(new[] { "delete props.prot.c", "set props.prot.a", "set props.pub.b" }, Ops(LastRequest));
            Assert.AreEqual("1", (string)LastRequest["ops"][1]["value"]);
            Assert.IsNull(LastRequest["scope"], "user state has no scope");
        }

        [Test]
        public void Flush_WaitsForTheRoom()
        {
            _session.Reply = (type, body) => Ok();
            _session.State = SessionState.Joining;
            _service.SetOwnProp("a", "1");
            Frame();
            Assert.AreEqual(0, _session.Requests.Count);

            _session.State = SessionState.Joined;
            Frame();
            Assert.AreEqual(1, _session.Requests.Count);
        }

        [Test]
        public void Flush_RunsAtMostEvery100ms()
        {
            _session.Reply = (type, body) => Ok();
            _service.SetOwnProp("a", "1");
            Frame();
            _service.SetOwnProp("b", "2");
            _now += 0.05f;
            _service.Tick();
            Assert.AreEqual(1, _session.Requests.Count);
            _now += 0.06f;
            _service.Tick();
            Assert.AreEqual(2, _session.Requests.Count);
        }

        [Test]
        public void Outcomes_ReportFailures_ButNotADeleteOfNothing()
        {
            _session.Reply = (type, body) => JObject.Parse(
                "{\"ok\":false,\"error\":\"batch_failed\",\"outcomes\":[{\"ok\":false,\"error\":\"not_found\"},{\"ok\":false,\"error\":\"value_too_large\"}]}");
            _service.RemoveOwnProp("gone");
            _service.SetOwnProp("big", "x");
            Frame();
            CollectionAssert.AreEqual(new[] { "big:value_too_large" }, _failures);
        }

        [Test]
        public void ATopLevelFailureWithoutOutcomes_DropsTheFlush_AsProductionDoes()
        {
            // ApplyFlushOutcomes returns when there are no outcomes, so a rate-limited flush is neither surfaced nor
            // retried (UserStateService.cs:324-326). Mirrored as is.
            _session.Reply = (type, body) => JObject.Parse("{\"ok\":false,\"error\":\"rate_limited\"}");
            _service.SetOwnProp("a", "1");
            Frame();
            Frame();
            Assert.AreEqual(1, _session.Requests.Count);
            CollectionAssert.IsEmpty(_failures);
        }

        [Test]
        public void ATransientError_RequeuesTheFlush()
        {
            _session.Fault = (type, body) => new LocalRelayException("not_connected");
            _service.SetOwnProp("a", "1");
            _service.RemoveOwnProp("b");
            Frame();
            CollectionAssert.IsEmpty(_failures);

            _session.Fault = null;
            _session.Reply = (type, body) => Ok();
            Frame();
            Assert.AreEqual(2, _session.Requests.Count);
            CollectionAssert.AreEqual(new[] { "delete props.prot.b", "set props.prot.a" }, Ops(LastRequest));
        }

        [Test]
        public void ARequeue_LosesToANewerWrite()
        {
            _session.Fault = (type, body) => new LocalRelayException("not_connected");
            _service.SetOwnProp("a", "old");
            _service.Tick();
            // Written while the failed flush was in flight: the newer value wins over the requeue.
            _service.SetOwnProp("a", "new");
            _main.Drain();

            _session.Fault = null;
            _session.Reply = (type, body) => Ok();
            Frame();
            Assert.AreEqual("new", (string)LastRequest["ops"][0]["value"]);
        }

        [Test]
        public void ASocketDrop_ReportsEachSet_WithPacketPartysCode()
        {
            _session.Fault = (type, body) => new LocalRelayException("closed");
            _service.SetOwnProp("a", "1");
            Frame();
            CollectionAssert.AreEqual(new[] { "a:" + PacketPartyErrorCodes.SocketClosed }, _failures);
        }

        [Test]
        public void RoomJoined_RepublishesOwnProps()
        {
            _session.Reply = (type, body) => Ok();
            _service.SetOwnProp("a", "1");
            Frame();

            _session.RaiseRoomJoined();
            Frame();
            Assert.AreEqual(2, _session.Requests.Count);
            CollectionAssert.AreEqual(new[] { "set props.prot.a" }, Ops(LastRequest));
        }

        [Test]
        public void RoomJoined_DoesNotResurrectRemovedProps()
        {
            _session.Reply = (type, body) => Ok();
            _service.SetOwnProp("a", JObject.Parse("{\"x\":1}"));
            _service.SetOwnProp("a.child", "2");
            Frame();

            // The delete sheds the whole subtree from what a rejoin republishes.
            _service.RemoveOwnProp("a");
            _session.RaiseRoomJoined();
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.a" }, Ops(LastRequest));
        }

        [Test]
        public void Snapshot_KeepsOnlyPageProps()
        {
            _session.Emit(UserStateMessages.Snapshot, JObject.Parse(
                "{\"revision\":1,\"users\":{" +
                "\"rsess_2\":{\"props.prot.score\":\"5\",\"attachment_kQx3\":\"v1|1|0\",\"pilot\":\"seat\"}," +
                "\"rsess_3\":{\"avatar\":\"x\"}}}"));

            CollectionAssert.AreEqual(new[] { "rsess_2 score=\"5\" was null" }, _changes);
            Assert.IsTrue(_service.TryGetProp("rsess_2", "score", out var score));
            Assert.AreEqual("5", (string)score);
            CollectionAssert.IsEmpty(new List<KeyValuePair<string, JToken>>(_service.Props("rsess_3")));
            Assert.IsFalse(_service.TryGetProp("rsess_2", "attachment_kQx3", out _), "engine keys never reach the page");
        }

        [Test]
        public void Changes_CarryThePreviousValue_AndSkipEngineKeys()
        {
            _session.Emit(UserStateMessages.Snapshot, JObject.Parse(
                "{\"revision\":1,\"users\":{\"rsess_2\":{\"props.prot.score\":\"5\"}}}"));
            _changes.Clear();

            _session.Emit(UserStateMessages.Changed, JObject.Parse(
                "{\"roomSessionId\":\"rsess_2\",\"revision\":2,\"changes\":[" +
                "{\"path\":\"props.prot.score\",\"value\":\"6\"}," +
                "{\"path\":\"pilot\",\"value\":\"seat\"}," +
                "{\"path\":\"props.pub.flag\",\"value\":true}," +
                "{\"path\":\"props.prot.score\",\"deleted\":true}]}"));

            CollectionAssert.AreEqual(new[]
            {
                "rsess_2 score=\"6\" was \"5\"",
                "rsess_2 flag=true was null",
                "rsess_2 score=null was \"6\""
            }, _changes);
            Assert.IsTrue(_service.TryGetProp("rsess_2", "flag", out _, UserPropScope.ModeratorWritable));
            Assert.IsFalse(_service.TryGetProp("rsess_2", "flag", out _), "scopes are separate subtrees");
        }

        [Test]
        public void OwnReads_ComeFromTheEcho()
        {
            _service.SetOwnProp("a", "1");
            Assert.IsFalse(_service.TryGetProp(_service.OwnRoomSessionId, "a", out _), "not until the relay echoes it");

            _session.Emit(UserStateMessages.Changed, JObject.Parse(
                "{\"roomSessionId\":\"rsess_me\",\"revision\":1,\"changes\":[{\"path\":\"props.prot.a\",\"value\":\"1\"}]}"));
            Assert.IsTrue(_service.TryGetProp(_service.OwnRoomSessionId, "a", out _));
        }

        [Test]
        public void Removed_ForgetsThePlayer()
        {
            _session.Emit(UserStateMessages.Snapshot, JObject.Parse(
                "{\"revision\":1,\"users\":{\"rsess_2\":{\"props.prot.score\":\"5\"}}}"));
            _session.Emit(UserStateMessages.Removed, JObject.Parse("{\"roomSessionId\":\"rsess_2\",\"revision\":2}"));

            CollectionAssert.AreEqual(new[] { "rsess_2" }, _removed);
            Assert.IsFalse(_service.TryGetProp("rsess_2", "score", out _));
        }

        [Test]
        public void ClearAll_ForgetsOwnAndPeerProps()
        {
            _session.Reply = (type, body) => Ok();
            _session.Emit(UserStateMessages.Snapshot, JObject.Parse(
                "{\"revision\":1,\"users\":{\"rsess_2\":{\"props.prot.score\":\"5\"}}}"));
            _service.SetOwnProp("a", "1");
            _service.ClearAll();

            Assert.AreEqual(0, _service.OwnLeaves);
            Assert.IsFalse(_service.TryGetProp("rsess_2", "score", out _));
            Frame();
            Assert.AreEqual(0, _session.Requests.Count, "nothing queued for the old room is sent");
            _session.RaiseRoomJoined();
            Frame();
            Assert.AreEqual(0, _session.Requests.Count, "nor republished in the next one");
        }
    }
}
