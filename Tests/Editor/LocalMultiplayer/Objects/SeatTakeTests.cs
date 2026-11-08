using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>
    /// Sitting on a seat of a synced object takes the object (the client's SyncedObjectSystem.HoldWhileRiding), aimed at
    /// whoever owns it, but never while another player rides a seat on it: a passenger doesn't take the vehicle from its
    /// driver, also when the vehicle was already bound and the take didn't have to wait.
    /// </summary>
    public class SeatTakeTests
    {
        const string Me = "rsess_me";
        const string Other = "rsess_other";

        LocalObjectBinderTests.FakeSession _session;
        LocalObjectBinderTests.FakeHost _host;
        SyncedObjectsModule _module;
        SynchronizationContext _savedContext;
        Func<BSSeat, bool> _savedResolver;
        readonly List<GameObject> _created = new List<GameObject>();
        // Seats another player sits on (what the attachment bridge answers from the pilots it received).
        readonly HashSet<BSSeat> _riddenByOther = new HashSet<BSSeat>();
        long _revision = 10;

        [SetUp]
        public void SetUp()
        {
            _riddenByOther.Clear();
            _revision = 10;
            // Replies complete inline, as they would once the session drains them on the main thread.
            _savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            _savedResolver = BSSeat.OccupiedByOtherResolver;
            BSSeat.OccupiedByOtherResolver = seat => _riddenByOther.Contains(seat);
            _session = new LocalObjectBinderTests.FakeSession { Reply = Reply };
            _host = new LocalObjectBinderTests.FakeHost(_session);
            var moduleObject = new GameObject("synced objects");
            _created.Add(moduleObject);
            _module = moduleObject.AddComponent<SyncedObjectsModule>();
            _module.Install(_host);
            _session.Deliver(ObjectMessages.Snapshot, new JObject { ["revision"] = 5, ["objects"] = new JArray() });
            _session.JoinRoom(Me);
        }

        [TearDown]
        public void TearDown()
        {
            if (_module != null) _module.Uninstall();
            foreach (var go in _created)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _created.Clear();
            BSSeat.OccupiedByOtherResolver = _savedResolver;
            SynchronizationContext.SetSynchronizationContext(_savedContext);
        }

        // The relay: every object was declared first by the other player, who owns it; an acquire is granted.
        Task<JObject> Reply(string type, JObject body)
        {
            string id = (string)body["objectId"];
            switch (type)
            {
                case ObjectMessages.Create:
                    return Task.FromResult(Ok(id, Other, 1));
                case ObjectMessages.Acquire:
                    return Task.FromResult(Ok(id, Me, 2));
                default:
                    return Task.FromResult(new JObject { ["ok"] = false, ["error"] = "unsupported" });
            }
        }

        JObject Ok(string id, string owner, uint generation)
        {
            long revision = ++_revision;
            return new JObject
            {
                ["ok"] = true,
                ["revision"] = revision,
                ["object"] = LocalObjectBinderTests.Record(id, owner, generation, revision)
            };
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

        // A synced vehicle with two seats on children, declared and bound: the room has it, owned by the other player.
        (BSSyncedObject Synced, LocalNetworkObject Network, BSSeat Driver, BSSeat Passenger) AddVehicle(string id)
        {
            var go = new GameObject(id);
            _created.Add(go);
            go.AddComponent<BSObjectId>().Id = id;
            var synced = go.AddComponent<BSSyncedObject>();
            var driver = AddSeat(go.transform, id + "-driver");
            var passenger = AddSeat(go.transform, id + "-passenger");
            var networkObject = go.AddComponent<LocalNetworkObject>();
            networkObject.ConfigureSceneDeclare(_module.Binder, "object", id);
            _module.Binder.Register(networkObject);
            Assert.IsTrue(networkObject.IsBound, $"'{id}' is declared");
            Assert.IsTrue(networkObject.IsRemoteOwned, "the other player owns it");
            return (synced, networkObject, driver, passenger);
        }

        static BSSeat AddSeat(Transform parent, string id)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            var seat = go.AddComponent<BSSeat>();
            go.GetComponent<BSObjectId>().Id = id;
            return seat;
        }

        [Test]
        public void SittingOnAFreeVehicle_TakesItFromItsOwner()
        {
            var vehicle = AddVehicle("cart");
            _module.TakeForSeat(vehicle.Synced);
            Assert.AreEqual(1, Count(ObjectMessages.Acquire, "cart"), "the take is aimed at the owner");
            Assert.IsTrue(vehicle.Network.IsOwned, "the rider drives it");
        }

        [Test]
        public void SittingWhereAnotherPlayerRides_LeavesThemTheVehicle()
        {
            var vehicle = AddVehicle("cart");
            _riddenByOther.Add(vehicle.Driver);
            _module.TakeForSeat(vehicle.Synced);
            Assert.AreEqual(0, Count(ObjectMessages.Acquire, "cart"), "a passenger doesn't take the vehicle");
            Assert.IsTrue(vehicle.Network.IsRemoteOwned, "its driver keeps it");
        }

        [Test]
        public void GettingOffAndOnAgain_OnceTheDriverLeft_TakesIt()
        {
            var vehicle = AddVehicle("cart");
            _riddenByOther.Add(vehicle.Driver);
            _module.TakeForSeat(vehicle.Synced);
            _module.ReleaseAfterRiding(vehicle.Synced);
            _riddenByOther.Clear();
            _module.TakeForSeat(vehicle.Synced);
            Assert.AreEqual(1, Count(ObjectMessages.Acquire, "cart"));
            Assert.IsTrue(vehicle.Network.IsOwned);
        }
    }
}
