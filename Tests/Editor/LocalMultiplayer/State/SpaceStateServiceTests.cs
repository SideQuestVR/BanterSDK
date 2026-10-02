using System.Collections.Generic;
using System.Threading;
using BS.LocalMultiplayer.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The ported SpaceStateService against a fake relay session: the local refusals that must never spend a
    /// rate-limit token (SpaceStateService.cs:346-371), the batch shapes (:373-405), and what each relay answer does
    /// to the queue (:407-560).
    /// </summary>
    public class SpaceStateServiceTests
    {
        FakeLocalSession _session;
        MainThreadQueue _main;
        RoomStateClient _client;
        SpaceStateService _service;
        float _now;
        List<string> _failures;
        List<SpaceStateChange> _changes;

        [SetUp]
        public void SetUp()
        {
            _session = new FakeLocalSession();
            _main = new MainThreadQueue();
            _client = new RoomStateClient(_session, _main.Post, CancellationToken.None);
            _client.Attach();
            _now = 100f;
            // The backoff jitter is fixed at 0.05 s so the retry timing below is exact.
            _service = new SpaceStateService(_client, _main.Post, CancellationToken.None, () => _now, () => 0.05f);
            _failures = new List<string>();
            _changes = new List<SpaceStateChange>();
            _service.WriteFailed += (key, code, message) => _failures.Add(key + ":" + code);
            _service.Changed += change => _changes.Add(change);

            _session.RaiseRoomJoined();
            _session.Emit(RoomStateMessages.Snapshot, JObject.Parse(
                "{\"revision\":3," +
                "\"public\":{\"score\":\"1\",\"game.round\":\"2\"}," +
                "\"protected\":{\"admin.title\":\"Welcome\",\"game.locked.rule\":\"no\"}," +
                "\"protectedPrefixes\":[\"admin\",\"game.locked\"]}"));
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
            _client.Detach();
            _main.Close();
        }

        // Past any send interval, then one frame: the module drains posted continuations before it ticks again.
        void Frame()
        {
            _now += 10f;
            _service.Tick();
            _main.Drain();
        }

        static JObject Ok() => JObject.Parse("{\"ok\":true,\"revision\":4}");

        JObject LastRequest => _session.Requests[_session.Requests.Count - 1].Body;

        static List<string> Ops(JObject request)
        {
            var ops = new List<string>();
            foreach (var op in (JArray)request["ops"]) ops.Add((string)op["op"] + " " + (string)op["path"]);
            return ops;
        }

        [Test]
        public void Snapshot_SplitsPublicFromProtected()
        {
            Assert.IsTrue(_service.HasSnapshot);
            Assert.AreEqual(3, _service.Revision);
            Assert.IsTrue(_service.IsKeyPublic("score"));
            Assert.IsFalse(_service.IsKeyPublic("admin.title"));
            Assert.IsFalse(_service.IsKeyPublic("game.locked.rule"));
            CollectionAssert.IsSupersetOf(_service.ProtectedKeys, new[] { "admin", "game.locked", "admin.title" });
            Assert.IsTrue(_service.CanWriteProtected, "every local player counts as signed in");
        }

        [Test]
        public void PublicWrite_ToAProtectedPath_IsRefusedWithoutARequest()
        {
            _service.Set("admin.title", "Hacked", RoomStateScope.Public);
            _service.Set("admin.sub.key", "x", RoomStateScope.Public);
            Frame();

            Assert.AreEqual(0, _session.Requests.Count);
            CollectionAssert.AreEquivalent(new[] { "admin.title:protected_path", "admin.sub.key:protected_path" }, _failures);
            Assert.AreEqual(0, _service.PendingWrites, "a refusal leaves the queue");
        }

        [Test]
        public void PublicDeleteOrReplace_OfAnAncestorOfProtectedState_IsRefused()
        {
            _service.Delete("game", RoomStateScope.Public);
            Frame();
            _service.Replace("game", JObject.Parse("{\"round\":3}"), RoomStateScope.Public);
            Frame();

            Assert.AreEqual(0, _session.Requests.Count);
            CollectionAssert.AreEqual(new[] { "game:protected_path", "game:protected_path" }, _failures);
        }

        [Test]
        public void PublicDelete_OfUnprotectedState_IsSentNonAtomic()
        {
            _session.Reply = (type, body) => Ok();
            _service.Delete("score", RoomStateScope.Public);
            Frame();

            Assert.AreEqual(1, _session.Requests.Count);
            Assert.AreEqual(RoomStateMessages.Batch, _session.Requests[0].Type);
            Assert.AreEqual("public", (string)LastRequest["scope"]);
            Assert.AreEqual(false, (bool)LastRequest["atomic"]);
            CollectionAssert.AreEqual(new[] { "delete score" }, Ops(LastRequest));
            Assert.AreEqual(0, _service.PendingWrites, "acked");
        }

        [Test]
        public void ProtectedWrite_SendsProtectFirst_InAnAtomicBatch()
        {
            _session.Reply = (type, body) => Ok();
            _service.Set("motd", "hi", RoomStateScope.Protected);
            Frame();

            Assert.AreEqual("protected", (string)LastRequest["scope"]);
            Assert.AreEqual(true, (bool)LastRequest["atomic"]);
            CollectionAssert.AreEqual(new[] { "protect motd", "set motd" }, Ops(LastRequest));
            Assert.AreEqual("hi", (string)LastRequest["ops"][1]["value"]);
            CollectionAssert.Contains(_service.ProtectedKeys, "motd", "a confirmed protect is remembered");
        }

        [Test]
        public void ProtectedDelete_DoesNotProtectThePath()
        {
            _session.Reply = (type, body) => Ok();
            _service.Delete("admin.title", RoomStateScope.Protected);
            Frame();
            CollectionAssert.AreEqual(new[] { "delete admin.title" }, Ops(LastRequest));
        }

        [TestCase(StateErrors.NotSpaceAdmin)]
        [TestCase(StateErrors.NotAuthorized)]
        [TestCase(StateErrors.PolicyDenied)]
        [TestCase(StateErrors.AppUnavailable)]
        [TestCase(StateErrors.JwtMissing)]
        public void ADenial_LatchesProtectedWrites_UntilTheNextJoin(string denial)
        {
            _session.Reply = (type, body) => JObject.Parse("{\"ok\":false,\"error\":\"" + denial + "\"}");
            _service.Set("motd", "hi", RoomStateScope.Protected);
            Frame();

            CollectionAssert.AreEqual(new[] { "motd:" + denial }, _failures);
            Assert.IsFalse(_service.CanWriteProtected);

            // Every further protected write is refused locally: no token is spent on a certain denial.
            _service.Set("motd2", "hi", RoomStateScope.Protected);
            Frame();
            Assert.AreEqual(1, _session.Requests.Count);
            Assert.AreEqual("motd2:" + StateErrors.NotAuthorized, _failures[1]);

            _session.RaiseRoomJoined();
            Assert.IsTrue(_service.CanWriteProtected, "a join clears the latch");
        }

        [TestCase(StateErrors.RateLimited)]
        [TestCase(StateErrors.SessionNotFound)]
        [TestCase(StateErrors.InternalError)]
        public void TransientErrors_KeepTheWritePending_AndBackOff(string error)
        {
            _session.Reply = (type, body) => JObject.Parse("{\"ok\":false,\"error\":\"" + error + "\"}");
            _service.Set("score", "5", RoomStateScope.Public);
            Frame();

            Assert.AreEqual(1, _session.Requests.Count);
            Assert.AreEqual(1, _service.PendingWrites, "never surfaced, kept for the retry");
            CollectionAssert.IsEmpty(_failures);

            // The first backoff is 0.25 s plus the jitter (0.05 s here).
            _now += 0.29f;
            _service.Tick();
            Assert.AreEqual(1, _session.Requests.Count, "still backing off");

            _session.Reply = (type, body) => Ok();
            Frame();
            Assert.AreEqual(2, _session.Requests.Count);
            Assert.AreEqual(0, _service.PendingWrites);
        }

        [Test]
        public void BatchFailed_BlamesOnlyTheWriteThatFailed_InAPublicBatch()
        {
            _session.Reply = (type, body) => JObject.Parse(
                "{\"ok\":false,\"error\":\"batch_failed\",\"outcomes\":[{\"ok\":true},{\"ok\":false,\"error\":\"value_too_large\"}]}");
            _service.Set("a", "1", RoomStateScope.Public);
            _service.Set("b", "2", RoomStateScope.Public);
            Frame();

            CollectionAssert.AreEqual(new[] { "b:value_too_large" }, _failures);
            Assert.AreEqual(0, _service.PendingWrites, "the good write of a non-atomic batch was acked");
        }

        [Test]
        public void BatchFailed_InAnAtomicBatch_RetriesTheWritesThatDidNotFail()
        {
            // Protected: protect a, set a, protect b, set b. Nothing persisted, so "a" goes again.
            _session.Reply = (type, body) => JObject.Parse(
                "{\"ok\":false,\"error\":\"batch_failed\",\"outcomes\":[{\"ok\":true},{\"ok\":true},{\"ok\":true},{\"ok\":false,\"error\":\"type_mismatch\"}]}");
            _service.Set("a", "1", RoomStateScope.Protected);
            _service.Set("b", "2", RoomStateScope.Protected);
            Frame();

            CollectionAssert.AreEqual(new[] { "b:type_mismatch" }, _failures);
            Assert.AreEqual(1, _service.PendingWrites);
        }

        [Test]
        public void Replace_DeletesFirst_OnlyWhenASubtreeExists()
        {
            _session.Reply = (type, body) => Ok();
            _service.Replace("score", "9", RoomStateScope.Public);
            _service.Replace("fresh", "1", RoomStateScope.Public);
            Frame();

            CollectionAssert.AreEqual(new[] { "delete score", "set score", "set fresh" }, Ops(LastRequest));
        }

        [Test]
        public void ProtectedPath_DegradesThePublicLaneToSingleOps()
        {
            // The batch reply cannot say which op hit the protection the server knows and we don't.
            _session.Reply = (type, body) => JObject.Parse("{\"ok\":false,\"error\":\"protected_path\"}");
            _service.Set("x", "1", RoomStateScope.Public);
            _service.Set("y", "1", RoomStateScope.Public);
            Frame();
            Assert.AreEqual(2, ((JArray)LastRequest["ops"]).Count);

            Frame();
            Assert.AreEqual(1, ((JArray)LastRequest["ops"]).Count, "one op per message until a batch succeeds");
        }

        [Test]
        public void ProtectedPath_OnASingleWrite_DiscoversTheProtection_AndRetriesProtected()
        {
            _session.Reply = (type, body) => JObject.Parse("{\"ok\":false,\"error\":\"protected_path\"}");
            _service.Set("x", "1", RoomStateScope.Public);
            Frame();

            CollectionAssert.Contains(_service.ProtectedKeys, "x");
            CollectionAssert.IsEmpty(_failures);
            _session.Reply = (type, body) => Ok();

            // The public copy is still queued ahead of the protected retry; it is now refused locally (production
            // reports it to the page as ss_err even though the retry goes on to succeed).
            Frame();
            Assert.AreEqual(1, _session.Requests.Count);
            CollectionAssert.AreEqual(new[] { "x:protected_path" }, _failures);

            Frame();
            Assert.AreEqual(2, _session.Requests.Count);
            Assert.AreEqual("protected", (string)LastRequest["scope"], "re-sent once as protected");
            CollectionAssert.AreEqual(new[] { "protect x", "set x" }, Ops(LastRequest));
        }

        [Test]
        public void SentinelKeys_NeverLeaveTheClient()
        {
            _service.Set(SdkWireCodec.RevisionKey, "99", RoomStateScope.Public);
            _service.Set(SdkWireCodec.ErrorKey, "{}", RoomStateScope.Public);
            Assert.AreEqual(0, _service.PendingWrites);
            CollectionAssert.IsEmpty(_failures);
        }

        [Test]
        public void InvalidKeysAndValues_FailBeforeQueueing()
        {
            _service.Set("a..b", "x", RoomStateScope.Public);
            _service.Set("big", JValue.CreateString(new string('x', SdkWireCodec.MaxValueBytes)), RoomStateScope.Public);
            CollectionAssert.AreEqual(new[] { "a..b:invalid_path", "big:value_too_large" }, _failures);
            Assert.AreEqual(0, _service.PendingWrites);
        }

        [Test]
        public void NothingIsSent_UnlessJoined()
        {
            _session.State = SessionState.Reconnecting;
            _service.Set("score", "5", RoomStateScope.Public);
            Frame();
            Assert.AreEqual(0, _session.Requests.Count);
            Assert.AreEqual(1, _service.PendingWrites, "kept until the room is back (or 60 s pass)");
        }

        [Test]
        public void JsonApi_RunsTheSamePreChecks_WithoutARequest()
        {
            Assert.AreEqual(StateErrors.ProtectedPath,
                _service.ReplaceAsync("admin.title", "x", RoomStateScope.Public).Result.Error);
            Assert.AreEqual(StateErrors.ProtectedPath,
                _service.DeleteAsync("game", RoomStateScope.Public).Result.Error);
            Assert.AreEqual(StateErrors.InvalidPath,
                _service.SetAsync(SdkWireCodec.CanProtectKey, "1", RoomStateScope.Public).Result.Error);

            _session.State = SessionState.Connecting;
            Assert.AreEqual(StateErrors.NotConnected,
                _service.SetAsync("score", "2", RoomStateScope.Public).Result.Error);
            Assert.AreEqual(0, _session.Requests.Count);
        }

        [Test]
        public void JsonApi_ReportsTheFirstFailingOutcome()
        {
            _session.Reply = (type, body) => JObject.Parse(
                "{\"ok\":false,\"error\":\"batch_failed\",\"outcomes\":[{\"ok\":false,\"error\":\"too_many_keys\"}]}");
            var result = _service.SetAsync("score", "2", RoomStateScope.Public).Result;
            Assert.IsFalse(result.Ok);
            Assert.AreEqual(StateErrors.TooManyKeys, result.Error);
            Assert.AreEqual(StateErrors.Describe(StateErrors.TooManyKeys), result.Message);
        }

        [Test]
        public void JsonApi_MapsASocketDropToPacketPartysCode()
        {
            _session.Fault = (type, body) => new LocalRelayException("closed");
            var result = _service.SetAsync("score", "2", RoomStateScope.Public).Result;
            Assert.AreEqual(PacketPartyErrorCodes.SocketClosed, result.Error);
        }

        [Test]
        public void Changes_AreDecoded_WithTheirScopeAndPreviousValue()
        {
            _session.Emit(RoomStateMessages.Changed, JObject.Parse(
                "{\"scope\":\"public\",\"revision\":4,\"changes\":[{\"path\":\"score\",\"value\":\"7\"}," +
                "{\"path\":\":!!!\",\"value\":\"not ours\"}]}"));
            _session.Emit(RoomStateMessages.Changed, JObject.Parse(
                "{\"scope\":\"protected\",\"revision\":5,\"changes\":[{\"path\":\"admin.title\",\"deleted\":true}]}"));

            Assert.AreEqual(5, _service.Revision);
            Assert.AreEqual(2, _changes.Count, "a path we could not have written is ignored");

            Assert.AreEqual("score", _changes[0].Key);
            Assert.AreEqual("7", (string)_changes[0].Value);
            Assert.AreEqual("1", (string)_changes[0].PreviousValue);
            Assert.IsTrue(_changes[0].IsPublic);

            Assert.AreEqual("admin.title", _changes[1].Key);
            Assert.IsTrue(_changes[1].Deleted);
            Assert.IsNull(_changes[1].Value);
            Assert.IsFalse(_changes[1].IsPublic);
            Assert.IsFalse(_service.TryGet("admin.title", out _));
        }

        [Test]
        public void ClearAll_ForgetsTheRoom()
        {
            _service.Set("score", "5", RoomStateScope.Public);
            _service.ClearAll();
            Assert.IsFalse(_service.HasSnapshot);
            Assert.AreEqual(0, _service.Revision);
            Assert.AreEqual(0, _service.PendingWrites);
            CollectionAssert.IsEmpty(_service.ProtectedKeys);
        }
    }
}
