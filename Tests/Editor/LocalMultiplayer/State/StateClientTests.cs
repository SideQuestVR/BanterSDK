using System.Collections.Generic;
using System.Threading;
using BS.LocalMultiplayer.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The ported PacketPartyClient state layer (PacketPartyClient.cs:3032-3396): room-state changes reach the
    /// services in strict revision order, a skipped revision is repaired with a path-less get, and stale user-state
    /// events are dropped.
    /// </summary>
    public class StateClientTests
    {
        FakeLocalSession _session;
        MainThreadQueue _main;
        RoomStateClient _room;
        List<long> _snapshots;
        List<long> _changes;

        [SetUp]
        public void SetUp()
        {
            _session = new FakeLocalSession();
            _main = new MainThreadQueue();
            _room = new RoomStateClient(_session, _main.Post, CancellationToken.None);
            _room.Attach();
            _snapshots = new List<long>();
            _changes = new List<long>();
            _room.OnRoomStateSnapshot += snapshot => _snapshots.Add(snapshot.Revision);
            _room.OnRoomStateChanged += changed => _changes.Add(changed.Revision);
        }

        [TearDown]
        public void TearDown()
        {
            _room.Detach();
            _main.Close();
        }

        void Snapshot(long revision) =>
            _session.Emit(RoomStateMessages.Snapshot, JObject.Parse(
                "{\"revision\":" + revision + ",\"public\":{},\"protected\":{},\"protectedPrefixes\":[]}"));

        void Change(long revision) =>
            _session.Emit(RoomStateMessages.Changed, JObject.Parse(
                "{\"scope\":\"public\",\"revision\":" + revision + ",\"changes\":[{\"path\":\"k\",\"value\":" + revision + "}]}"));

        [Test]
        public void Attach_ListensForTheRelaysStateMessages_AndDetachStops()
        {
            Assert.AreEqual(1, _session.HandlerCount(RoomStateMessages.Snapshot));
            Assert.AreEqual(1, _session.HandlerCount(RoomStateMessages.Changed));
            _room.Detach();
            Assert.AreEqual(0, _session.HandlerCount(RoomStateMessages.Snapshot));
            Assert.AreEqual(0, _session.HandlerCount(RoomStateMessages.Changed));
        }

        [Test]
        public void InOrderChanges_PassThrough_AndStaleOnesAreDropped()
        {
            Snapshot(5);
            Change(6);
            Change(6);
            Change(4);
            Change(7);
            CollectionAssert.AreEqual(new[] { 5L }, _snapshots);
            CollectionAssert.AreEqual(new[] { 6L, 7L }, _changes);
            Assert.AreEqual(7, _room.Revision);
        }

        [Test]
        public void AnOlderSnapshot_IsDropped()
        {
            Snapshot(5);
            Snapshot(4);
            Snapshot(5);
            CollectionAssert.AreEqual(new[] { 5L, 5L }, _snapshots, "an equal revision is a fresh authority and passes");
        }

        [Test]
        public void ASkippedRevision_IsHeldBack_AndRequestsAPathlessGet()
        {
            Snapshot(5);
            Change(7);
            CollectionAssert.IsEmpty(_changes, "7 waits for 6");
            Assert.AreEqual(1, _session.Requests.Count);
            Assert.AreEqual(RoomStateMessages.Get, _session.Requests[0].Type);
            Assert.AreEqual("public", (string)_session.Requests[0].Body["scope"]);
            Assert.IsNull(_session.Requests[0].Body["path"], "path-less: the whole state");

            Change(6);
            CollectionAssert.AreEqual(new[] { 6L, 7L }, _changes, "applied in order once the gap closes");
        }

        [Test]
        public void TheRepairSnapshot_SplitsByProtectedPrefix_AndDiscardsWhatItCovers()
        {
            RoomStateSnapshotEvent repaired = null;
            _room.OnRoomStateSnapshot += snapshot => repaired = snapshot;
            _session.Reply = (type, body) => JObject.Parse(
                "{\"ok\":true,\"revision\":8,\"state\":{\"a\":1,\"admin.title\":\"x\"},\"protectedPrefixes\":[\"admin\"]}");

            Snapshot(5);
            Change(7);
            _main.Drain();

            CollectionAssert.AreEqual(new[] { 5L, 8L }, _snapshots);
            CollectionAssert.IsEmpty(_changes, "revision 7 is covered by the repaired snapshot");
            Assert.AreEqual(8, _room.Revision);
            CollectionAssert.AreEquivalent(new[] { "a" }, repaired.Public.Keys);
            CollectionAssert.AreEquivalent(new[] { "admin.title" }, repaired.Protected.Keys);
            CollectionAssert.AreEqual(new[] { "admin" }, repaired.ProtectedPrefixes);
        }

        [Test]
        public void ALeftRoom_ResetsTheRevision_SoTheNextSessionsSnapshotPasses()
        {
            Snapshot(10);
            _session.RaiseRoomLeft();
            Snapshot(3);
            CollectionAssert.AreEqual(new[] { 10L, 3L }, _snapshots);
        }

        [Test]
        public void BatchRequests_CarryOpsAtomicAndScope()
        {
            _session.Reply = (type, body) => JObject.Parse("{\"ok\":true,\"revision\":2}");
            var ops = new JArray { new JObject { ["op"] = "set", ["path"] = "a", ["value"] = 1 } };
            var result = _room.BatchRoomStateAsync(ops, RoomStateScope.Protected, true).Result;

            Assert.IsTrue(result.Ok);
            Assert.AreEqual(2, result.Revision);
            var request = _session.Requests[0];
            Assert.AreEqual(RoomStateMessages.Batch, request.Type);
            Assert.AreEqual("protected", (string)request.Body["scope"]);
            Assert.AreEqual(true, (bool)request.Body["atomic"]);
            Assert.AreEqual("set", (string)request.Body["ops"][0]["op"]);
            Assert.AreEqual("a", (string)request.Body["ops"][0]["path"]);
        }

        [Test]
        public void AResultWithNullValue_ParsesAsNoValue()
        {
            // PacketParty's JsonSettings ignore nulls, so "value":null is not passed on to the page.
            var parsed = StateJson.Parse<RoomStateResult>(JObject.Parse("{\"ok\":true,\"revision\":1,\"value\":null}"));
            Assert.IsNull(parsed.Value);
        }

        [Test]
        public void UserState_DropsEventsOlderThanTheLastRevision()
        {
            var users = new UserStateClient(_session);
            users.Attach();
            var seen = new List<string>();
            users.OnUserStateSnapshot += snapshot => seen.Add("snapshot " + snapshot.Revision);
            users.OnUserStateChanged += changed => seen.Add("changed " + changed.Revision);
            users.OnUserStateRemoved += removed => seen.Add("removed " + removed.Revision);

            _session.Emit(UserStateMessages.Snapshot, JObject.Parse("{\"revision\":4,\"users\":{}}"));
            _session.Emit(UserStateMessages.Changed, JObject.Parse("{\"roomSessionId\":\"r1\",\"revision\":3,\"changes\":[]}"));
            _session.Emit(UserStateMessages.Changed, JObject.Parse("{\"roomSessionId\":\"r1\",\"revision\":4,\"changes\":[]}"));
            _session.Emit(UserStateMessages.Changed, JObject.Parse("{\"revision\":9,\"changes\":[]}"));
            _session.Emit(UserStateMessages.Removed, JObject.Parse("{\"roomSessionId\":\"r1\",\"revision\":2}"));
            _session.Emit(UserStateMessages.Removed, JObject.Parse("{\"roomSessionId\":\"r1\",\"revision\":5}"));

            // User state's guard is "older than", so an equal revision passes (PacketPartyClient.cs:3054).
            CollectionAssert.AreEqual(new[] { "snapshot 4", "changed 4", "removed 5" }, seen);
            users.Detach();
        }
    }
}
