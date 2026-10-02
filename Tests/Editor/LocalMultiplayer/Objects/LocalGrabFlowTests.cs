using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SideQuest.FlexaBody;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>
    /// The desktop grab of a synced object against a fake relay: a grab acquires and locks, a release commits the pose
    /// and unlocks while keeping authority (PacketPartyGrabbable). A desktop click can end the grab before the acquire
    /// lands, which a VR grip never does, and the object must still end up unlocked. A lost race lets go only of the
    /// object it lost, and ends the press, so the hand's grip window doesn't grab that object again.
    /// </summary>
    public class LocalGrabFlowTests
    {
        const string Me = "rsess_me";
        const string Other = "rsess_other";

        sealed class RelayRecord
        {
            public string Owner = Other;
            public uint Generation;
            public bool Locked;
            public long Revision;
        }

        sealed class HeldAcquire
        {
            public string ObjectId;
            public bool Lock;
            public TaskCompletionSource<JObject> Reply;
        }

        LocalObjectBinderTests.FakeSession _session;
        LocalObjectBinderTests.FakeHost _host;
        LocalObjectBinder _binder;
        LocalGrabNetworkBridge _bridge;
        SynchronizationContext _savedContext;
        readonly List<GameObject> _created = new List<GameObject>();

        // The relay's registry (every object exists, owned by the other player), and the acquires it hasn't answered.
        readonly Dictionary<string, RelayRecord> _relay = new Dictionary<string, RelayRecord>(StringComparer.Ordinal);
        readonly List<HeldAcquire> _heldAcquires = new List<HeldAcquire>();
        bool _holdAcquires;
        long _revision = 10;

        [SetUp]
        public void SetUp()
        {
            // NUnit runs every test on one fixture instance: start each with an empty relay.
            _relay.Clear();
            _heldAcquires.Clear();
            _holdAcquires = false;
            _revision = 10;
            // Replies complete inline, as they would once the session drains them on the main thread.
            _savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            _session = new LocalObjectBinderTests.FakeSession { Reply = Reply };
            _host = new LocalObjectBinderTests.FakeHost(_session);
            _binder = new LocalObjectBinder(_host);
            _session.Deliver(ObjectMessages.Snapshot, new JObject { ["revision"] = 5, ["objects"] = new JArray() });
            _session.JoinRoom(Me);
            var bridgeObject = new GameObject("bridge");
            _created.Add(bridgeObject);
            _bridge = bridgeObject.AddComponent<LocalGrabNetworkBridge>();
            _bridge.Configure(_host);
        }

        [TearDown]
        public void TearDown()
        {
            if (_bridge != null) _bridge.Teardown();
            _binder?.Dispose();
            foreach (var go in _created)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _created.Clear();
            SynchronizationContext.SetSynchronizationContext(_savedContext);
        }

        // ─── The fake relay ─────────────────────────────────────────────────────

        Task<JObject> Reply(string type, JObject body)
        {
            string id = (string)body["objectId"];
            switch (type)
            {
                case ObjectMessages.Create:
                    // Scene creates are idempotent.
                    if (!_relay.TryGetValue(id, out var existing)) _relay[id] = existing = new RelayRecord { Revision = ++_revision };
                    return Task.FromResult(Ok(id, existing));
                case ObjectMessages.Acquire:
                    bool locked = (bool)body["lock"];
                    if (!_holdAcquires) return Task.FromResult(Acquire(id, locked));
                    var held = new HeldAcquire { ObjectId = id, Lock = locked, Reply = new TaskCompletionSource<JObject>() };
                    _heldAcquires.Add(held);
                    return held.Reply.Task;
                case ObjectMessages.CommitState:
                    _relay[id].Revision = ++_revision;
                    return Task.FromResult(Ok(id, _relay[id]));
                case ObjectMessages.Unlock:
                    _relay[id].Locked = false;
                    _relay[id].Revision = ++_revision;
                    return Task.FromResult(Ok(id, _relay[id]));
                default:
                    return Task.FromResult(new JObject { ["ok"] = false, ["error"] = "unsupported" });
            }
        }

        JObject Acquire(string id, bool locked)
        {
            var record = _relay[id];
            record.Owner = Me;
            record.Generation++;
            record.Locked = locked;
            record.Revision = ++_revision;
            return Ok(id, record);
        }

        static JObject RecordOf(string id, RelayRecord record)
            => LocalObjectBinderTests.Record(id, record.Owner, record.Generation, record.Revision, record.Locked);

        static JObject Ok(string id, RelayRecord record)
            => new JObject { ["ok"] = true, ["revision"] = record.Revision, ["object"] = RecordOf(id, record) };

        // The relay answers an acquire it was holding back: granted ...
        void LandAcquire(string id)
        {
            var held = TakeHeldAcquire(id);
            held.Reply.SetResult(Acquire(id, held.Lock));
        }

        // ... or denied: the other player got there first and holds the object.
        void DenyAcquire(string id)
        {
            var held = TakeHeldAcquire(id);
            var record = _relay[id];
            record.Locked = true;
            record.Revision = ++_revision;
            held.Reply.SetResult(new JObject
            {
                ["ok"] = false, ["error"] = "object_locked", ["revision"] = record.Revision, ["object"] = RecordOf(id, record)
            });
        }

        HeldAcquire TakeHeldAcquire(string id)
        {
            int index = _heldAcquires.FindIndex(held => held.ObjectId == id);
            Assert.GreaterOrEqual(index, 0, $"no acquire of '{id}' is waiting");
            var found = _heldAcquires[index];
            _heldAcquires.RemoveAt(index);
            return found;
        }

        int Count(string type, string id)
        {
            int count = 0;
            foreach (var request in _session.Requests)
            {
                if (request.Type == type && (string)request.Body["objectId"] == id) count++;
            }
            return count;
        }

        JObject FindRequest(string type, string id)
        {
            foreach (var request in _session.Requests)
            {
                if (request.Type == type && (string)request.Body["objectId"] == id) return request.Body;
            }
            Assert.Fail($"no {type} was sent for '{id}'");
            return null;
        }

        // ─── Scene objects ──────────────────────────────────────────────────────

        // A synced object that opted into grab ownership, declared and bound; the room has it, owned by the other player.
        LocalNetworkObject AddGrabbable(string id)
        {
            var go = new GameObject(id);
            _created.Add(go);
            go.AddComponent<BoxCollider>();
            var networkObject = go.AddComponent<LocalNetworkObject>();
            networkObject.ConfigureSceneDeclare(_binder, "object", id);
            go.AddComponent<LocalGrabbable>().Configure();
            _binder.Register(networkObject);
            Assert.IsTrue(networkObject.IsBound, $"'{id}' is declared");
            Assert.IsFalse(networkObject.IsOwned);
            return networkObject;
        }

        // The desktop player's mouse hand without its camera and scene: GrabHand on a kinematic body, and the
        // BSDesktopMouseHand that owns the press. Edit mode runs no Start, so the hand's wiring is set directly.
        GrabHand CreateMouseHand(out BSDesktopMouseHand mouseHand, out HandData data)
        {
            var go = new GameObject("mouse hand");
            _created.Add(go);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var physicsHand = go.AddComponent<PhysicsHand>();
            physicsHand.Setup(HandID.Right, body);
            var hand = go.AddComponent<GrabHand>();
            hand.Setup(HandID.Right, physicsHand, Vector3.zero);
            data = new HandData();
            hand.PlayerStart(data);
            mouseHand = go.AddComponent<BSDesktopMouseHand>();
            SetPrivate(mouseHand, "_grabHand", hand);
            SetPrivate(mouseHand, "_gripHeld", true); // the button is down
            return hand;
        }

        // What GrabHand.StartGrab leaves in the hand's data for a grabbed collider without a Rigidbody.
        static void Hold(HandData data, GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (!target.TryGetComponent(out GrabHandle handle)) handle = target.AddComponent<GrabHandle>();
            handle.Col = collider;
            data.Grabbing = true;
            data.HeldHandle = handle;
            data.HeldCollider = collider;
        }

        static void SetPrivate(object target, string field, object value)
        {
            var info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{field} is gone: update this test");
            info.SetValue(target, value);
        }

        // ─── Grab and release ───────────────────────────────────────────────────

        [Test]
        public void AGrabStillHeldWhenTheAcquireLands_StaysLocked_UntilItsRelease()
        {
            var a = AddGrabbable("a");
            _holdAcquires = true;
            var grab = _bridge.HandleGrab(a.gameObject, Vector3.zero);
            LandAcquire("a");
            Assert.IsTrue(grab.IsCompleted);
            Assert.IsTrue(a.IsOwned && a.IsLocked, "held: owned and locked");
            Assert.AreEqual(0, Count(ObjectMessages.Unlock, "a"));

            var release = _bridge.HandleRelease(a.gameObject);
            Assert.IsTrue(release.IsCompleted);
            Assert.AreEqual(1, Count(ObjectMessages.CommitState, "a"));
            Assert.AreEqual(1, Count(ObjectMessages.Unlock, "a"));
            Assert.IsTrue(a.IsOwned, "the drop keeps authority");
            Assert.IsFalse(a.IsLocked);
        }

        [Test]
        public void AClickReleasedBeforeTheAcquireLands_DropsTheObjectWhenItLands()
        {
            var a = AddGrabbable("a");
            _holdAcquires = true;
            var grab = _bridge.HandleGrab(a.gameObject, Vector3.zero);
            var release = _bridge.HandleRelease(a.gameObject);
            Assert.IsTrue(release.IsCompleted, "the release doesn't wait for the acquire");
            Assert.IsFalse(grab.IsCompleted, "the acquire is on its way");
            Assert.AreEqual(0, Count(ObjectMessages.CommitState, "a") + Count(ObjectMessages.Unlock, "a"), "nothing to drop yet");

            LandAcquire("a");
            Assert.IsTrue(grab.IsCompleted);
            Assert.AreEqual(1, Count(ObjectMessages.CommitState, "a"), "dropped once: the pose is committed");
            Assert.AreEqual(1, Count(ObjectMessages.Unlock, "a"), "and the object unlocked");
            Assert.IsTrue(a.IsOwned, "the drop keeps authority");
            Assert.IsFalse(a.IsLocked, "nobody else is locked out");
        }

        [Test]
        public void AnAcquireLandingAfterTheHandMovedOn_DropsThatObject()
        {
            var a = AddGrabbable("a");
            var b = AddGrabbable("b");
            _holdAcquires = true;
            var grabA = _bridge.HandleGrab(a.gameObject, Vector3.zero);
            // 'a' was let go without the bridge hearing of it (a rebuilt desktop player), and the hand took 'b'.
            var grabB = _bridge.HandleGrab(b.gameObject, Vector3.zero);

            LandAcquire("a");
            Assert.IsTrue(grabA.IsCompleted);
            Assert.AreEqual(1, Count(ObjectMessages.Unlock, "a"));
            Assert.IsTrue(a.IsOwned);
            Assert.IsFalse(a.IsLocked);

            LandAcquire("b");
            Assert.IsTrue(grabB.IsCompleted);
            Assert.AreEqual(0, Count(ObjectMessages.Unlock, "b"), "'b' is still in the hand");
            Assert.IsTrue(b.IsOwned && b.IsLocked);
        }

        // ─── LocalGrabbable ─────────────────────────────────────────────────────

        [Test]
        public void ReleaseGrab_WhileTheAcquireIsOnItsWay_IsCarriedOutWhenItLands()
        {
            var a = AddGrabbable("a");
            var grabbable = a.GetComponent<LocalGrabbable>();
            int grabbed = 0;
            int released = 0;
            grabbable.OnGrabbed += () => grabbed++;
            grabbable.OnReleased += () => released++;
            _holdAcquires = true;
            var grab = grabbable.TryGrabAsync();
            Assert.IsTrue(grabbable.ReleaseGrabAsync().IsCompleted, "kept for later, not awaited");

            LandAcquire("a");
            Assert.IsTrue(grab.IsCompleted);
            Assert.IsTrue(grab.Result, "the acquire made the object ours");
            Assert.AreEqual(1, grabbed);
            Assert.AreEqual(1, released);
            Assert.IsFalse(grabbable.IsHeld);
            Assert.IsTrue(a.IsOwned);
            Assert.AreEqual(1, Count(ObjectMessages.Unlock, "a"));
            Assert.AreEqual(a.Record.OwnershipGeneration, (uint)(long)FindRequest(ObjectMessages.CommitState, "a")["expectedOwnershipGeneration"],
                "the pose is committed under the generation the acquire gave");
        }

        [Test]
        public void ADeniedAcquire_ForgetsTheReleaseAskedDuringIt()
        {
            var a = AddGrabbable("a");
            var grabbable = a.GetComponent<LocalGrabbable>();
            _holdAcquires = true;
            var grab = grabbable.TryGrabAsync();
            _ = grabbable.ReleaseGrabAsync();
            DenyAcquire("a");
            Assert.IsTrue(grab.IsCompleted);
            Assert.IsFalse(grab.Result);
            Assert.AreEqual(0, Count(ObjectMessages.CommitState, "a") + Count(ObjectMessages.Unlock, "a"));

            // The next grab holds the object as usual: the old release went with the denied acquire.
            _holdAcquires = false;
            var regrab = grabbable.TryGrabAsync();
            Assert.IsTrue(regrab.IsCompleted && regrab.Result);
            Assert.IsTrue(grabbable.IsHeld);
            Assert.AreEqual(0, Count(ObjectMessages.Unlock, "a"));
        }

        // ─── A lost race ────────────────────────────────────────────────────────

        [Test]
        public void ALostRace_LetsGoOnlyOfTheObjectItLost_AndEndsThePress()
        {
            var a = AddGrabbable("a");
            var b = AddGrabbable("b");
            var hand = CreateMouseHand(out var mouseHand, out var data);
            GameObject released = null;
            hand.Released += go => released = go;

            // The hand has moved on to 'b' and the button is down for it: the race lost for 'a' leaves both alone.
            Hold(data, b.gameObject);
            Assert.IsFalse(LocalGrabNetworkBridge.LetGoAfterLostRace(hand, mouseHand, a));
            Assert.IsTrue(hand.IsGrabbing);
            Assert.IsTrue(mouseHand.IsActive, "the press on 'b' goes on");
            Assert.IsNull(released);
            hand.ReleaseGrab();
            released = null;

            // Still holding 'a' with the button down: let go and end the press, or GrabHand's grip window, which takes
            // any grabbable collider in reach, would grab it again on the next physics step.
            Hold(data, a.gameObject);
            SetPrivate(mouseHand, "_gripHeld", true);
            hand.PreferredCollider = a.GetComponent<Collider>();
            Assert.IsTrue(LocalGrabNetworkBridge.LetGoAfterLostRace(hand, mouseHand, a));
            Assert.IsFalse(hand.IsGrabbing);
            Assert.AreSame(a.gameObject, released);
            Assert.IsFalse(mouseHand.IsActive, "the press is over");
            Assert.IsNull(hand.PreferredCollider);

            // Nothing in the hand: nothing to let go of.
            Assert.IsFalse(LocalGrabNetworkBridge.LetGoAfterLostRace(hand, mouseHand, a));
        }

        [Test]
        public void HoldsNetworkObject_ResolvesWhatTheHandHolds()
        {
            var a = AddGrabbable("a");
            var b = AddGrabbable("b");
            var hand = CreateMouseHand(out _, out var data);
            Assert.IsFalse(LocalGrabNetworkBridge.HoldsNetworkObject(hand, a), "an empty hand");
            Hold(data, a.gameObject);
            Assert.IsTrue(LocalGrabNetworkBridge.HoldsNetworkObject(hand, a));
            Assert.IsFalse(LocalGrabNetworkBridge.HoldsNetworkObject(hand, b));
            Assert.IsFalse(LocalGrabNetworkBridge.HoldsNetworkObject(null, a));
            Assert.IsFalse(LocalGrabNetworkBridge.HoldsNetworkObject(hand, null));
        }
    }
}
