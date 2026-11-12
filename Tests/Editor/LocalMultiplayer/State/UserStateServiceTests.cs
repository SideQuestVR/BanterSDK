using System.Collections.Generic;
using System.Threading;
using BS.LocalMultiplayer.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The ported UserStateService against a fake relay session: the 0.1 s non-atomic flush with deletes first,
    /// replacing sets, the verdict an awaited write gets, per-op failures, requeue on transient errors, the rejoin
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
        public void ATopLevelRateLimit_RetriesTheWrite_ThenAnswersIt()
        {
            int replies = 0;
            _session.Reply = (type, body) => ++replies == 1 ? JObject.Parse("{\"ok\":false,\"error\":\"rate_limited\"}") : Ok();
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, false);
            Frame();
            Assert.IsFalse(write.IsCompleted, "still being retried");
            CollectionAssert.IsEmpty(_failures);

            Frame();
            Assert.AreEqual(2, _session.Requests.Count);
            CollectionAssert.AreEqual(new[] { "set props.prot.a" }, Ops(LastRequest));
            Assert.IsTrue(write.IsCompleted);
            Assert.IsNull(Done(write));
        }

        [Test]
        public void TopLevelTransientFailures_BackOff_AndASuccessResetsIt()
        {
            _session.Reply = (type, body) => JObject.Parse("{\"ok\":false,\"error\":\"internal_error\"}");
            _service.SetOwnProp("a", "1");
            _service.Tick();
            _main.Drain();
            Assert.AreEqual(1, _session.Requests.Count);

            _now += 0.2f;
            _service.Tick();
            Assert.AreEqual(1, _session.Requests.Count, "the first retry waits 0.25 s");
            _now += 0.1f;
            _service.Tick();
            _main.Drain();
            Assert.AreEqual(2, _session.Requests.Count);

            _now += 0.4f;
            _service.Tick();
            Assert.AreEqual(2, _session.Requests.Count, "the second waits 0.5 s");
            _now += 0.2f;
            _session.Reply = (type, body) => Ok();
            _service.Tick();
            _main.Drain();
            Assert.AreEqual(3, _session.Requests.Count);

            _service.SetOwnProp("b", "2");
            _now += 0.11f;
            _service.Tick();
            Assert.AreEqual(4, _session.Requests.Count, "back to the plain flush interval");
        }

        [Test]
        public void ATopLevelRefusal_IsSurfaced_AndRefusesTheWrite()
        {
            _session.Reply = (type, body) => JObject.Parse("{\"ok\":false,\"error\":\"invalid_batch\"}");
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, false);
            Frame();
            Assert.AreEqual("invalid_batch", Done(write));
            CollectionAssert.AreEqual(new[] { "a:invalid_batch" }, _failures);
            Frame();
            Assert.AreEqual(1, _session.Requests.Count, "not retried");
        }

        // ------------------------------------------------------------------ replacing sets

        void Replace(string key, string json) =>
            _service.TrySetOwnProp(key, JToken.Parse(json), UserPropScope.OwnerOnly, true, out _);

        // Result would block for good on a write that was never answered, and take the test run with it.
        static string Done(System.Threading.Tasks.Task<string> write)
        {
            Assert.IsTrue(write.IsCompleted, "the write was never answered");
            return write.Result;
        }

        [Test]
        public void AFirstReplace_SendsNoDelete()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1}");
            Frame();
            CollectionAssert.AreEqual(new[] { "set props.prot.pos" }, Ops(LastRequest));
        }

        [Test]
        public void ReplacingAnObjectWithItself_SendsNoDelete()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1,\"y\":2}");
            Frame();
            Replace("pos", "{\"x\":1,\"y\":2}");
            Frame();
            CollectionAssert.AreEqual(new[] { "set props.prot.pos" }, Ops(LastRequest));
        }

        [Test]
        public void AReplaceThatOnlyAddsOrChangesLeaves_SendsNoDelete()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1}");
            Frame();
            Replace("pos", "{\"x\":2,\"y\":3}");
            Frame();
            CollectionAssert.AreEqual(new[] { "set props.prot.pos" }, Ops(LastRequest));
        }

        [TestCase("{\"x\":1,\"y\":2}", "{\"x\":3}", TestName = "ALeafDropped")]
        [TestCase("{\"x\":1}", "{\"x\":{\"y\":2}}", TestName = "ALeafBecomesAnObject")]
        [TestCase("{\"x\":{\"y\":2}}", "{\"x\":1}", TestName = "AnObjectBecomesALeaf")]
        [TestCase("5", "{\"x\":1}", TestName = "APlainValueBecomesAnObject")]
        [TestCase("{}", "{\"x\":1}", TestName = "AnEmptyObjectFills")]
        public void AReplaceThatWouldLeaveAnOldLeaf_DeletesThePathFirst(string before, string after)
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", before);
            Frame();
            Replace("pos", after);
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.pos", "set props.prot.pos" }, Ops(LastRequest));
        }

        [Test]
        public void AReplaceInsideAnObjectHeldFurtherUp_DeletesThePathFirst()
        {
            _session.Reply = (type, body) => Ok();
            Replace("a", "{\"b\":1}");
            Frame();
            Replace("a.b", "{\"c\":2}");
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.a.b", "set props.prot.a.b" }, Ops(LastRequest));
        }

        [Test]
        public void AReplaceAfterAMerge_DeletesThePathFirst()
        {
            _session.Reply = (type, body) => Ok();
            _service.SetOwnProp("pos", JObject.Parse("{\"x\":1}"));
            Frame();
            _service.SetOwnProp("pos", JObject.Parse("{\"y\":2}"));
            Frame();
            Replace("pos", "{\"y\":2}");
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.pos", "set props.prot.pos" }, Ops(LastRequest));
        }

        [TestCase("{\"x\":1}", "{\"x\":1}", false)]
        [TestCase("{\"x\":1}", "{\"x\":2,\"y\":3}", false)]
        [TestCase("{\"a\":{\"b\":1}}", "{\"a\":{\"b\":2}}", false)]
        [TestCase("[1,2]", "[3]", false)]
        [TestCase("5", "6", false)]
        [TestCase("{\"x\":1,\"y\":2}", "{\"x\":1}", true)]
        [TestCase("{\"a\":{\"b\":1}}", "{\"a\":1}", true)]
        [TestCase("{\"x\":1}", "5", true)]
        public void LeavesOldLeaf_ComparesFlattenedPaths(string before, string after, bool leaves)
        {
            var held = new[] { new KeyValuePair<string, JToken>("p", JToken.Parse(before)) };
            Assert.AreEqual(leaves, UserStateService.LeavesOldLeaf("p", JToken.Parse(after), held));
        }

        [Test]
        public void LeavesOldLeaf_CountsKeysHeldUnderThePath()
        {
            var held = new[] { new KeyValuePair<string, JToken>("p.b", JToken.Parse("1")) };
            Assert.IsFalse(UserStateService.LeavesOldLeaf("p", JToken.Parse("{\"b\":2}"), held));
            Assert.IsTrue(UserStateService.LeavesOldLeaf("p", JToken.Parse("{\"c\":2}"), held));
        }

        [Test]
        public void AReplaceOfAnObject_WithAPlainValue_DeletesThePathFirst()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1}");
            Frame();
            Replace("pos", "5");
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.pos", "set props.prot.pos" }, Ops(LastRequest));
        }

        [Test]
        public void AReplaceOverChildKeys_DeletesThePathFirst()
        {
            _session.Reply = (type, body) => Ok();
            _service.SetOwnProp("a.b", "1");
            Frame();
            Replace("a", "5");
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.a", "set props.prot.a" }, Ops(LastRequest));
        }

        [Test]
        public void APlainValueOverAPlainValue_StaysOneOp()
        {
            _session.Reply = (type, body) => Ok();
            Replace("n", "1");
            Frame();
            Replace("n", "2");
            Frame();
            CollectionAssert.AreEqual(new[] { "set props.prot.n" }, Ops(LastRequest));
        }

        [Test]
        public void AMergingSet_SendsNoDelete()
        {
            _session.Reply = (type, body) => Ok();
            _service.SetOwnProp("pos", JObject.Parse("{\"x\":1}"));
            Frame();
            _service.SetOwnProp("pos", JObject.Parse("{\"y\":2}"));
            Frame();
            CollectionAssert.AreEqual(new[] { "set props.prot.pos" }, Ops(LastRequest));
        }

        [Test]
        public void ALaterPlainWrite_KeepsAPendingReplacesDelete()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1}");
            Frame();
            Replace("pos", "{\"x\":2}");
            Replace("pos", "5");
            Replace("pos", "6");
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.pos", "set props.prot.pos" }, Ops(LastRequest));
            Assert.AreEqual(6, (int)LastRequest["ops"][1]["value"]);
        }

        [Test]
        public void ADeleteThenASet_KeepsTheDeleteInFront()
        {
            _session.Reply = (type, body) => Ok();
            _service.SetOwnProp("pos", JObject.Parse("{\"x\":1}"));
            Frame();
            _service.RemoveOwnProp("pos");
            _service.SetOwnProp("pos", JObject.Parse("{\"y\":2}"));
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.pos", "set props.prot.pos" }, Ops(LastRequest));
        }

        [Test]
        public void AReplace_ShedsChildKeys_FromTheRejoinRepublish()
        {
            _session.Reply = (type, body) => Ok();
            _service.SetOwnProp("a.b", "1");
            Frame();
            Replace("a", "{\"c\":2}");
            Frame();

            _session.RaiseRoomJoined();
            Frame();
            CollectionAssert.AreEqual(new[] { "set props.prot.a" }, Ops(LastRequest));
        }

        [Test]
        public void AReplace_CountsTheLeavesItFrees()
        {
            _session.Reply = (type, body) => Ok();
            var full = new JObject();
            for (int i = 0; i < UserStateService.MaxPropLeaves; i++) full["k" + i] = i;
            _service.SetOwnProp("a.child", full);
            Assert.AreEqual(UserStateService.MaxPropLeaves, _service.OwnLeaves);

            Assert.IsTrue(_service.TrySetOwnProp("a", full, UserPropScope.OwnerOnly, true, out string error), error);
            Assert.AreEqual(UserStateService.MaxPropLeaves, _service.OwnLeaves);
        }

        [TestCase(70, 1, TestName = "TooManySegments")]
        [TestCase(1, 300, TestName = "TooLongAPath")]
        public void AValuePastThePathLimits_IsRefusedBeforeAnythingIsSent(int depth, int width)
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1}");
            Frame();
            int sent = _session.Requests.Count;

            JToken deep = 1;
            for (int i = 0; i < depth; i++)
            {
                var wrap = new JObject();
                string name = width > 60 ? new string('a', 60) + i : "a";
                wrap[name] = deep;
                deep = wrap;
            }
            if (width > 60)
                for (int i = 0; i < 5; i++) deep = new JObject { [new string('b', 60)] = deep };

            Assert.IsFalse(_service.TrySetOwnProp("pos", deep, UserPropScope.OwnerOnly, true, out string error));
            Assert.AreEqual(StateErrors.InvalidPath, error);
            Frame();
            Assert.AreEqual(sent, _session.Requests.Count, "a replace must not send its delete for a value that can't follow");
        }

        [Test]
        public void AValueWithAnUnusableMemberName_IsRefusedBeforeAnythingIsSent()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1}");
            Frame();
            int sent = _session.Requests.Count;

            var bad = new JObject { [new string('a', 200)] = 1 };
            Assert.IsFalse(_service.TrySetOwnProp("pos", bad, UserPropScope.OwnerOnly, true, out string error));
            Assert.AreEqual(StateErrors.InvalidPath, error);
            Frame();
            Assert.AreEqual(sent, _session.Requests.Count, "a replace must not send its delete for a value that can't follow");
        }

        [Test]
        public void MemberNamesAreEncoded_LikeSpaceState()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"a b\":1}");
            Frame();
            var value = (JObject)LastRequest["ops"][0]["value"];
            Assert.IsNull(value["a b"]);
            StatePathCodec.TryEncodeSegment("a b", out string segment, out _);
            Assert.AreEqual(1, (int)value[segment]);
        }

        [Test]
        public void ARequeuedReplace_StillSendsItsDelete()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1,\"y\":2}");
            Frame();

            _session.Fault = (type, body) => new LocalRelayException("not_connected");
            var write = _service.SetOwnPropAsync("pos", JObject.Parse("{\"x\":1}"), UserPropScope.OwnerOnly, true);
            Frame();
            Assert.IsFalse(write.IsCompleted);

            _session.Fault = null;
            _session.Reply = (type, body) => Ok();
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.pos", "set props.prot.pos" }, Ops(LastRequest));
            Assert.IsNull(Done(write));
        }

        [Test]
        public void ARequeuedReplace_KeepsItsDelete_UnderANewerMerge()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1,\"z\":3}");
            Frame();

            _session.Fault = (type, body) => new LocalRelayException("not_connected");
            _service.TrySetOwnProp("pos", JObject.Parse("{\"x\":1}"), UserPropScope.OwnerOnly, true, out _);
            _service.Tick();
            _service.SetOwnProp("pos", JObject.Parse("{\"y\":2}"));
            _main.Drain();

            _session.Fault = null;
            _session.Reply = (type, body) => Ok();
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.pos", "set props.prot.pos" }, Ops(LastRequest));
            Assert.AreEqual(2, (int)LastRequest["ops"][1]["value"]["y"]);
        }

        // ------------------------------------------------------------------ requeues

        [Test]
        public void ARequeue_DoesNotBringBackAChildAReplaceTookAway()
        {
            _session.Fault = (type, body) => new LocalRelayException("not_connected");
            _service.SetOwnProp("a.b", "old");
            _service.Tick();
            Replace("a", "{\"c\":1}");
            _main.Drain();

            _session.Fault = null;
            _session.Reply = (type, body) => Ok();
            Frame();
            CollectionAssert.DoesNotContain(Ops(LastRequest), "set props.prot.a.b");
            CollectionAssert.Contains(Ops(LastRequest), "set props.prot.a");
        }

        [Test]
        public void ARequeueFromBeforeClearAll_PublishesNothingInTheNextWorld()
        {
            _session.Fault = (type, body) => new LocalRelayException("not_connected");
            var write = _service.SetOwnPropAsync("a", "old-world", UserPropScope.OwnerOnly, true);
            _service.RemoveOwnProp("gone");
            _service.Tick();
            _service.ClearAll();
            _main.Drain();
            Assert.IsTrue(write.IsCompleted, "its world has gone, so nobody is left waiting");

            _session.Fault = null;
            _session.Reply = (type, body) => Ok();
            int sent = _session.Requests.Count;
            Frame();
            Assert.AreEqual(sent, _session.Requests.Count, "neither its set nor its delete reaches the next world");
        }

        [Test]
        public void ARequeue_KeepsAWriteOfTheSameKeyMadeInTheNextWorld()
        {
            _session.Fault = (type, body) => new LocalRelayException("not_connected");
            _service.SetOwnProp("a", "old-world");
            _service.Tick();
            _service.ClearAll();
            _service.SetOwnProp("a", "new-world");
            _main.Drain();

            _session.Fault = null;
            _session.Reply = (type, body) => Ok();
            Frame();
            Assert.AreEqual("new-world", (string)LastRequest["ops"][0]["value"]);
        }

        [TestCase("closed", TestName = "ASocketDrop")]
        [TestCase("timeout", TestName = "ASignalTimeout")]
        public void ALostReply_RequeuesAReplace_WithItsDelete(string relayCode)
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"x\":1,\"y\":2}");
            Frame();

            _session.Fault = (type, body) => new LocalRelayException(relayCode);
            Replace("pos", "{\"x\":3}");
            Frame();
            CollectionAssert.IsEmpty(_failures, "the write isn't lost, so nothing has failed");

            _session.Fault = null;
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.pos", "set props.prot.pos" }, Ops(LastRequest));
        }

        [Test]
        public void APersistentTransientReply_AnswersTheCaller_AndKeepsTrying()
        {
            _session.Reply = (type, body) => JObject.Parse("{\"ok\":false,\"error\":\"internal_error\"}");
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            for (int i = 0; i < 4; i++)
            {
                _now += 5f;
                _service.Tick();
                _main.Drain();
                Assert.IsFalse(write.IsCompleted, $"try {i + 1}");
            }
            _now += 5f;
            _service.Tick();
            _main.Drain();
            Assert.AreEqual(StateErrors.InternalError, Done(write));

            int sent = _session.Requests.Count;
            _now += 5f;
            _service.Tick();
            Assert.AreEqual(sent + 1, _session.Requests.Count, "the write itself is still queued");
        }

        // ------------------------------------------------------------------ batches

        [Test]
        public void Ops_SendEveryDeleteFirst_AndSetsParentBeforeChild()
        {
            _session.Reply = (type, body) => Ok();
            Replace("b", "{\"x\":1,\"y\":2}");
            Replace("a", "{\"x\":1,\"y\":2}");
            Frame();

            _service.SetOwnProp("a.c", "1");
            Replace("b", "{\"x\":1}");
            Replace("a", "{\"x\":1}");
            _service.RemoveOwnProp("z");
            Frame();
            CollectionAssert.AreEqual(new[]
            {
                "delete props.prot.z", "delete props.prot.a", "delete props.prot.b",
                "set props.prot.a", "set props.prot.b"
            }, Ops(LastRequest));
        }

        [Test]
        public void ABigFlush_SplitsAtTheBatchLimit_KeepingEachDeleteWithItsSet()
        {
            _session.Reply = (type, body) => Ok();
            for (int i = 0; i < 40; i++) Replace("k" + i.ToString("00"), "{\"x\":1}");
            Frame();
            for (int i = 0; i < 40; i++) Replace("k" + i.ToString("00"), "{\"y\":1}");

            int before = _session.Requests.Count;
            Frame();
            Assert.AreEqual(SpaceStateWriteQueue.MaxOpsPerBatch, Ops(LastRequest).Count);
            var first = Ops(LastRequest);
            Frame();
            Assert.AreEqual(before + 2, _session.Requests.Count);
            var second = Ops(LastRequest);
            Assert.AreEqual(80, first.Count + second.Count);
            foreach (var ops in new[] { first, second })
                foreach (string op in ops)
                    if (op.StartsWith("set ")) CollectionAssert.Contains(ops, "delete " + op.Substring(4));
        }

        [Test]
        public void ManyDeletes_SplitToo_AndGoBeforeAnySet()
        {
            _session.Reply = (type, body) => Ok();
            for (int i = 0; i < 100; i++) _service.RemoveOwnProp("d" + i);
            _service.SetOwnProp("a", "1");
            Frame();
            Assert.AreEqual(SpaceStateWriteQueue.MaxOpsPerBatch, Ops(LastRequest).Count);
            CollectionAssert.DoesNotContain(Ops(LastRequest), "set props.prot.a");
            Frame();
            CollectionAssert.Contains(Ops(LastRequest), "set props.prot.a");
        }

        [Test]
        public void AThrowingWriteFailedListener_DoesNotLeaveWritesUnanswered()
        {
            _service.WriteFailed += (key, code, message) => throw new System.InvalidOperationException("listener");
            _session.Reply = (type, body) => JObject.Parse(
                "{\"ok\":false,\"error\":\"batch_failed\",\"outcomes\":[{\"ok\":false,\"error\":\"value_too_large\"},{\"ok\":true}]}");
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("listener"));
            var bad = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            var good = _service.SetOwnPropAsync("b", "1", UserPropScope.OwnerOnly, true);
            Frame();
            Assert.AreEqual("value_too_large", Done(bad));
            Assert.IsNull(Done(good));
        }

        // ------------------------------------------------------------------ awaited writes

        [Test]
        public void AnAwaitedWrite_CompletesWithItsOwnOutcome()
        {
            _session.Reply = (type, body) => Ok();
            Replace("big", "{\"y\":1}");
            Frame();
            _session.Reply = (type, body) => JObject.Parse(
                "{\"ok\":false,\"error\":\"batch_failed\",\"outcomes\":[{\"ok\":true},{\"ok\":false,\"error\":\"value_too_large\"},{\"ok\":true}]}");
            var big = _service.SetOwnPropAsync("big", JObject.Parse("{\"x\":1}"), UserPropScope.OwnerOnly, true);
            var fine = _service.SetOwnPropAsync("fine", "1", UserPropScope.OwnerOnly, false);
            Assert.IsFalse(big.IsCompleted, "nothing has been sent yet");

            Frame();
            Assert.AreEqual("value_too_large", Done(big), "the outcome of the set, past its delete");
            Assert.IsNull(Done(fine));
            CollectionAssert.AreEqual(new[] { "big:value_too_large" }, _failures);
        }

        [Test]
        public void AnAwaitedWrite_NotFoundOnItsDelete_IsNotAFailure()
        {
            _session.Reply = (type, body) => Ok();
            Replace("pos", "{\"y\":1}");
            Frame();
            _session.Reply = (type, body) => JObject.Parse(
                "{\"ok\":false,\"error\":\"batch_failed\",\"outcomes\":[{\"ok\":false,\"error\":\"not_found\"},{\"ok\":true}]}");
            var write = _service.SetOwnPropAsync("pos", JObject.Parse("{\"x\":1}"), UserPropScope.OwnerOnly, true);
            Frame();
            Assert.IsNull(Done(write));
            CollectionAssert.IsEmpty(_failures);
        }

        [Test]
        public void AClientSideRefusal_CompletesAtOnce()
        {
            var write = _service.SetOwnPropAsync("", "1", UserPropScope.OwnerOnly, true);
            Assert.AreEqual(StateErrors.InvalidPath, Done(write));
        }

        [Test]
        public void AWriteOutsideARoom_CompletesAtOnce_AndStaysQueued()
        {
            _session.Reply = (type, body) => Ok();
            _session.State = SessionState.Joining;
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            Assert.IsTrue(write.IsCompleted);
            Assert.IsNull(Done(write));

            _session.State = SessionState.Joined;
            Frame();
            CollectionAssert.AreEqual(new[] { "set props.prot.a" }, Ops(LastRequest));
        }

        [Test]
        public void TwoWritesToOnePath_InOneWindow_ShareTheVerdict()
        {
            _session.Reply = (type, body) => Ok();
            var first = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            var second = _service.SetOwnPropAsync("a", "2", UserPropScope.OwnerOnly, true);
            Frame();
            Assert.AreEqual(1, _session.Requests.Count);
            Assert.IsTrue(first.IsCompleted);
            Assert.IsTrue(second.IsCompleted);
        }

        [Test]
        public void AWriteMadeDuringAFlush_WaitsForItsOwnFlush()
        {
            _session.Reply = (type, body) => Ok();
            var first = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            _service.Tick();
            var second = _service.SetOwnPropAsync("a", "2", UserPropScope.OwnerOnly, true);
            _main.Drain();
            Assert.IsTrue(first.IsCompleted);
            Assert.IsFalse(second.IsCompleted, "the reply was for the older value");

            Frame();
            Assert.IsTrue(second.IsCompleted);
        }

        [Test]
        public void AReplaceOfTheParent_TakesOverTheChildsWaiter()
        {
            _session.Reply = (type, body) => Ok();
            var child = _service.SetOwnPropAsync("a.b", "1", UserPropScope.OwnerOnly, true);
            var parent = _service.SetOwnPropAsync("a", "{}", UserPropScope.OwnerOnly, true);
            Frame();
            CollectionAssert.AreEqual(new[] { "delete props.prot.a", "set props.prot.a" }, Ops(LastRequest));
            Assert.IsTrue(child.IsCompleted);
            Assert.IsTrue(parent.IsCompleted);
        }

        [Test]
        public void ADelete_ReleasesAWriteItCancelled()
        {
            _session.Reply = (type, body) => Ok();
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            _service.RemoveOwnProp("a");
            Assert.IsTrue(write.IsCompleted);
            Assert.IsNull(Done(write));
        }

        [Test]
        public void ADeleteDuringARequeuedFlush_ReleasesTheWrite()
        {
            _session.Fault = (type, body) => new LocalRelayException("not_connected");
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            _service.Tick();
            _service.RemoveOwnProp("a");
            _main.Drain();
            Assert.IsTrue(write.IsCompleted);
        }

        [Test]
        public void ASocketDrop_HoldsTheWrite_ForTheNextJoin()
        {
            _session.Fault = (type, body) => new LocalRelayException("closed");
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            Frame();
            Assert.IsFalse(write.IsCompleted);

            _session.State = SessionState.Reconnecting;
            Frame();
            Assert.IsTrue(write.IsCompleted, "held for the join, as a write made offline is");
            Assert.IsNull(Done(write));
        }

        [Test]
        public void ARefusal_RefusesAnAwaitedWrite()
        {
            _session.Fault = (type, body) => new LocalRelayException("invalid_token");
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            Frame();
            Assert.AreEqual("invalid_token", Done(write));
        }

        [Test]
        public void AnUnexpectedException_RefusesAnAwaitedWrite()
        {
            _session.Fault = (type, body) => new System.InvalidOperationException("boom");
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("Flush failed"));
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            Frame();
            Assert.AreEqual(StateErrors.InternalError, Done(write));
        }

        [Test]
        public void LeavingTheRoom_ReleasesAWaitingWrite_AndKeepsItQueued()
        {
            _session.Reply = (type, body) => Ok();
            var write = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            _session.State = SessionState.Reconnecting;
            Frame();
            Assert.IsTrue(write.IsCompleted, "a write held for the next join is answered as one made offline is");
            Assert.IsNull(Done(write));
            Assert.AreEqual(0, _session.Requests.Count);

            _session.State = SessionState.Joined;
            Frame();
            Assert.AreEqual(1, _session.Requests.Count);
        }

        [Test]
        public void ClearAllAndDispose_ReleaseWaitingWrites()
        {
            var cleared = _service.SetOwnPropAsync("a", "1", UserPropScope.OwnerOnly, true);
            _service.ClearAll();
            Assert.AreEqual(StateErrors.NotConnected, Done(cleared));

            var disposed = _service.SetOwnPropAsync("b", "1", UserPropScope.OwnerOnly, true);
            _service.Dispose();
            Assert.AreEqual(StateErrors.NotConnected, Done(disposed));
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
        public void ARefusedFlush_ReportsEachSet()
        {
            _session.Fault = (type, body) => new LocalRelayException("invalid_token");
            _service.SetOwnProp("a", "1");
            Frame();
            CollectionAssert.AreEqual(new[] { "a:invalid_token" }, _failures);
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
