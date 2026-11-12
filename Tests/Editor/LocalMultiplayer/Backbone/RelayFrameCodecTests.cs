using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>The two hot lanes of RELAY.md 6.2, written from and read into structs without a JObject.</summary>
    public class RelayFrameCodecTests
    {
        static ParticipantFrame Pose(bool withVelocity = true, bool withExt = true)
        {
            return new ParticipantFrame
            {
                Seq = 1234,
                T = 98765432.125,
                ValidForMs = 33,
                Discontinuity = false,
                Position = new Vector3(1.5f, -2.25f, 3.1f),
                Rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.927362f),
                HasVelocity = withVelocity,
                Velocity = withVelocity ? new Vector3(0.01f, -9.81f, 1e-7f) : default,
                HasExt = withExt,
                Ext = withExt
                    ? new PoseExt
                    {
                        HipPosition = new Vector3(10f, 0.95f, -4f),
                        HipRotation = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f),
                        HeadPosition = new Vector3(0f, 0.65f, 0.02f),
                        HeadRotation = new Quaternion(0.05f, -0.1f, 0.01f, 0.9937f),
                        LeftPosition = new Vector3(-0.3f, 0.1f, 0.25f),
                        LeftRotation = new Quaternion(0.5f, 0.5f, -0.5f, 0.5f),
                        RightPosition = new Vector3(0.31f, 0.12f, 0.33f),
                        RightRotation = new Quaternion(-0.25f, 0.4f, 0.1f, 0.8746f),
                        Seated = true,
                    }
                    : default,
            };
        }

        // Exact: the writer prints floats round-trippably, so nothing may move.
        static void AssertSame(ParticipantFrame expected, ParticipantFrame actual)
        {
            Assert.AreEqual(expected.Seq, actual.Seq);
            Assert.AreEqual(expected.T, actual.T);
            Assert.AreEqual(expected.ValidForMs, actual.ValidForMs);
            Assert.AreEqual(expected.Discontinuity, actual.Discontinuity);
            AssertExact(expected.Position, actual.Position);
            AssertExact(expected.Rotation, actual.Rotation);
            Assert.AreEqual(expected.HasVelocity, actual.HasVelocity);
            if (expected.HasVelocity) AssertExact(expected.Velocity, actual.Velocity);
            Assert.AreEqual(expected.HasExt, actual.HasExt);
            if (!expected.HasExt) return;
            AssertExact(expected.Ext.HipPosition, actual.Ext.HipPosition);
            AssertExact(expected.Ext.HipRotation, actual.Ext.HipRotation);
            AssertExact(expected.Ext.HeadPosition, actual.Ext.HeadPosition);
            AssertExact(expected.Ext.HeadRotation, actual.Ext.HeadRotation);
            AssertExact(expected.Ext.LeftPosition, actual.Ext.LeftPosition);
            AssertExact(expected.Ext.LeftRotation, actual.Ext.LeftRotation);
            AssertExact(expected.Ext.RightPosition, actual.Ext.RightPosition);
            AssertExact(expected.Ext.RightRotation, actual.Ext.RightRotation);
            Assert.AreEqual(expected.Ext.Seated, actual.Ext.Seated);
        }

        static void AssertExact(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x);
            Assert.AreEqual(expected.y, actual.y);
            Assert.AreEqual(expected.z, actual.z);
        }

        static void AssertExact(Quaternion expected, Quaternion actual)
        {
            Assert.AreEqual(expected.x, actual.x);
            Assert.AreEqual(expected.y, actual.y);
            Assert.AreEqual(expected.z, actual.z);
            Assert.AreEqual(expected.w, actual.w);
        }

        // ---------------------------------------------------------------- tf.participant

        [Test]
        public void Participant_RoundTrips_WithTheWholeExt()
        {
            var frame = Pose();
            var json = RelayFrameCodec.WriteParticipant(frame);

            Assert.IsTrue(RelayFrameCodec.TryReadParticipant(json, out var from, out var read));
            Assert.IsNull(from);
            AssertSame(frame, read);
        }

        [Test]
        public void Participant_HasTheFieldsOfRelayMd_AndAnExtOf28Numbers()
        {
            var json = JObject.Parse(RelayFrameCodec.WriteParticipant(Pose()));

            Assert.AreEqual("tf.participant", (string)json["type"]);
            Assert.AreEqual("root", (string)json["key"]);
            Assert.AreEqual(1234, (long)json["seq"]);
            Assert.AreEqual(98765432.125, (double)json["t"]);
            Assert.AreEqual(33, (int)json["vf"]);
            Assert.AreEqual(false, (bool)json["d"]);
            Assert.AreEqual(3, ((JArray)json["p"]).Count);
            Assert.AreEqual(4, ((JArray)json["r"]).Count);
            Assert.AreEqual(3, ((JArray)json["v"]).Count);
            var o = (JArray)json["ext"]["o"];
            Assert.AreEqual(RelayFrameCodec.ExtNumbers, o.Count);
            Assert.AreEqual(28, o.Count);
            // Hip world pose first, then head, left and right, each position then rotation.
            Assert.AreEqual(10f, (float)o[0]);
            Assert.AreEqual(0.7071068f, (float)o[4]);
            Assert.AreEqual(0.65f, (float)o[8]);
            Assert.AreEqual(-0.3f, (float)o[14]);
            Assert.AreEqual(0.8746f, (float)o[27]);
            Assert.AreEqual(1, (int)json["ext"]["s"]);
            // The type is written first, so the receive thread can tell a hot frame without parsing it.
            StringAssert.StartsWith("{\"type\":\"tf.participant\"", RelayFrameCodec.WriteParticipant(Pose()));
        }

        [Test]
        public void Participant_WithoutVelocityOrExt_LeavesThemOut()
        {
            var frame = Pose(withVelocity: false, withExt: false);
            var text = RelayFrameCodec.WriteParticipant(frame);
            var json = JObject.Parse(text);

            Assert.IsNull(json["v"]);
            Assert.IsNull(json["ext"]);
            Assert.IsTrue(RelayFrameCodec.TryReadParticipant(text, out _, out var read));
            AssertSame(frame, read);
        }

        [Test]
        public void Participant_StandingLegPose_IsZero()
        {
            var frame = Pose();
            frame.Ext.Seated = false;
            var json = JObject.Parse(RelayFrameCodec.WriteParticipant(frame));

            Assert.AreEqual(0, (int)json["ext"]["s"]);
        }

        [Test]
        public void Participant_ReadsTheRelaysSenderStamp()
        {
            var json = JObject.Parse(RelayFrameCodec.WriteParticipant(Pose()));
            json["from"] = "rsess_1";
            json["fromPeerId"] = "peer_1";
            var text = json.ToString(Newtonsoft.Json.Formatting.None);

            Assert.IsTrue(RelayFrameCodec.LooksLikeParticipant(text));
            Assert.IsTrue(RelayFrameCodec.TryReadParticipant(text, out var from, out var read));
            Assert.AreEqual("rsess_1", from);
            AssertSame(Pose(), read);
        }

        [Test]
        public void Participant_ReadsAnyFieldOrder_AndBooleansForSeated()
        {
            const string text = "{\"from\":\"rsess_9\",\"ext\":{\"s\":true,\"o\":[0,1,2,0,0,0,1,0,0,0,0,0,0,1,0,0,0,0,0,0,1,0,0,0,0,0,0,1]}," +
                                "\"p\":[1,2,3],\"seq\":7,\"type\":\"tf.participant\",\"unknown\":{\"a\":[1,{\"b\":2}]},\"t\":5,\"vf\":40,\"d\":true}";

            Assert.IsFalse(RelayFrameCodec.LooksLikeParticipant(text));
            Assert.IsTrue(RelayFrameCodec.TryReadParticipant(JObject.Parse(text), out var from, out var read));
            Assert.AreEqual("rsess_9", from);
            Assert.AreEqual(7u, read.Seq);
            Assert.AreEqual(5d, read.T);
            Assert.AreEqual(40, read.ValidForMs);
            Assert.IsTrue(read.Discontinuity);
            AssertExact(new Vector3(1, 2, 3), read.Position);
            AssertExact(Quaternion.identity, read.Rotation);
            Assert.IsFalse(read.HasVelocity);
            Assert.IsTrue(read.HasExt);
            Assert.IsTrue(read.Ext.Seated);
            AssertExact(new Vector3(0, 1, 2), read.Ext.HipPosition);
        }

        [TestCase("{\"type\":\"tf.participant\",\"seq\":1}")]
        [TestCase("{\"type\":\"tf.participant\",\"p\":[1,2]}")]
        [TestCase("{\"type\":\"tf.participant\",\"p\":[1,2,3],\"ext\":{\"o\":[1,2,3]}}")]
        [TestCase("{\"type\":\"tf.objects\",\"p\":[1,2,3]}")]
        [TestCase("{\"type\":\"tf.participant\",\"p\":[1,2,3]")]
        [TestCase("not json")]
        public void Participant_RejectsBrokenFrames(string text)
        {
            Assert.IsFalse(RelayFrameCodec.TryReadParticipant(text, out _, out _));
        }

        [Test]
        public void NonFiniteNumbers_GoOutAsZero()
        {
            var frame = Pose(withExt: false);
            frame.Position = new Vector3(float.NaN, float.PositiveInfinity, 1f);
            frame.T = double.NaN;

            var text = RelayFrameCodec.WriteParticipant(frame);

            Assert.DoesNotThrow(() => JObject.Parse(text));
            Assert.IsTrue(RelayFrameCodec.TryReadParticipant(text, out _, out var read));
            AssertExact(new Vector3(0f, 0f, 1f), read.Position);
            Assert.AreEqual(0d, read.T);
        }

        // ---------------------------------------------------------------- tf.objects

        static List<ObjectFrame> Objects()
        {
            return new List<ObjectFrame>
            {
                new ObjectFrame
                {
                    ObjectId = "kQx3",
                    Generation = 2,
                    Epoch = 1,
                    Seq = 513,
                    T = 98765432.125,
                    ValidForMs = 33,
                    Position = new Vector3(4f, 5.5f, -6.25f),
                    HasRotation = true,
                    Rotation = new Quaternion(0f, 0f, 0.3826834f, 0.9238795f),
                    HasVelocity = true,
                    Velocity = new Vector3(0f, -1.5f, 0.125f),
                },
                new ObjectFrame
                {
                    ObjectId = "with \"quotes\" and \\ slash",
                    Generation = 4000000000,
                    Epoch = 7,
                    Seq = 65535,
                    T = 1.5,
                    ValidForMs = 100,
                    Position = new Vector3(-1f, 0f, 1e-3f),
                    Rotation = Quaternion.identity,
                },
            };
        }

        [Test]
        public void Objects_RoundTrip()
        {
            var frames = Objects();
            var text = RelayFrameCodec.WriteObjects(frames);

            Assert.IsTrue(RelayFrameCodec.LooksLikeObjects(text));
            var read = new List<ObjectFrame>();
            Assert.IsTrue(RelayFrameCodec.TryReadObjects(text, out var from, read));
            Assert.IsNull(from);
            Assert.AreEqual(frames.Count, read.Count);
            for (var i = 0; i < frames.Count; i++)
            {
                Assert.AreEqual(frames[i].ObjectId, read[i].ObjectId);
                Assert.AreEqual(frames[i].Generation, read[i].Generation);
                Assert.AreEqual(frames[i].Epoch, read[i].Epoch);
                Assert.AreEqual(frames[i].Seq, read[i].Seq);
                Assert.AreEqual(frames[i].T, read[i].T);
                Assert.AreEqual(frames[i].ValidForMs, read[i].ValidForMs);
                AssertExact(frames[i].Position, read[i].Position);
                Assert.AreEqual(frames[i].HasRotation, read[i].HasRotation);
                AssertExact(frames[i].HasRotation ? frames[i].Rotation : Quaternion.identity, read[i].Rotation);
                Assert.AreEqual(frames[i].HasVelocity, read[i].HasVelocity);
                if (frames[i].HasVelocity) AssertExact(frames[i].Velocity, read[i].Velocity);
            }
        }

        [Test]
        public void Objects_HaveTheFieldsOfRelayMd()
        {
            var json = JObject.Parse(RelayFrameCodec.WriteObjects(Objects()));
            var frames = (JArray)json["frames"];

            Assert.AreEqual("tf.objects", (string)json["type"]);
            Assert.AreEqual(2, frames.Count);
            CollectionAssert.AreEqual(new[] { "objectId", "gen", "epoch", "seq", "t", "vf", "p", "r", "v" },
                ((JObject)frames[0]).Properties().Select(p => p.Name).ToArray());
            // r only when rotation is synced, v only with a Rigidbody.
            CollectionAssert.AreEqual(new[] { "objectId", "gen", "epoch", "seq", "t", "vf", "p" },
                ((JObject)frames[1]).Properties().Select(p => p.Name).ToArray());
            Assert.AreEqual(4000000000L, (long)frames[1]["gen"]);
            Assert.AreEqual(65535, (int)frames[1]["seq"]);
        }

        [Test]
        public void Objects_ReadTheRelaysSenderStamp()
        {
            var json = JObject.Parse(RelayFrameCodec.WriteObjects(Objects()));
            json["from"] = "rsess_owner";
            var read = new List<ObjectFrame>();

            Assert.IsTrue(RelayFrameCodec.TryReadObjects(json.ToString(Newtonsoft.Json.Formatting.None), out var from, read));
            Assert.AreEqual("rsess_owner", from);
            Assert.AreEqual(2, read.Count);
        }

        [TestCase("{\"type\":\"tf.objects\"}")]
        [TestCase("{\"type\":\"tf.objects\",\"frames\":[{\"gen\":1,\"p\":[1,2,3]}]}")]
        [TestCase("{\"type\":\"tf.objects\",\"frames\":[{\"objectId\":\"a\",\"p\":[1,2,3,4]}]}")]
        [TestCase("{\"type\":\"tf.participant\",\"frames\":[]}")]
        public void Objects_RejectBrokenMessages(string text)
        {
            Assert.IsFalse(RelayFrameCodec.TryReadObjects(text, out _, new List<ObjectFrame>()));
        }

        // ---------------------------------------------------------------- one receive loop's reused buffers

        static void AssertSame(ObjectFrame expected, ObjectFrame actual)
        {
            Assert.AreEqual(expected.ObjectId, actual.ObjectId);
            Assert.AreEqual(expected.Generation, actual.Generation);
            Assert.AreEqual(expected.Epoch, actual.Epoch);
            Assert.AreEqual(expected.Seq, actual.Seq);
            Assert.AreEqual(expected.T, actual.T);
            Assert.AreEqual(expected.ValidForMs, actual.ValidForMs);
            AssertExact(expected.Position, actual.Position);
            Assert.AreEqual(expected.HasRotation, actual.HasRotation);
            AssertExact(expected.Rotation, actual.Rotation);
            Assert.AreEqual(expected.HasVelocity, actual.HasVelocity);
            AssertExact(expected.Velocity, actual.Velocity);
        }

        [Test]
        public void SharedBuffers_ReadEveryParticipantAsFreshOnesDo()
        {
            var buffers = new RelayFrameCodec.ReadBuffers();
            var full = Pose();
            var bare = Pose(withVelocity: false, withExt: false);
            bare.Position = new Vector3(-7f, 8f, 9f);

            // A frame that breaks off half way leaves numbers in both buffers...
            Assert.IsFalse(RelayFrameCodec.TryReadParticipant(
                "{\"type\":\"tf.participant\",\"p\":[1,2,3],\"ext\":{\"o\":[6,6,6]}}", out _, out _, buffers));

            // ...and no later frame may pick them up, nor anything an earlier good frame left.
            Assert.IsTrue(RelayFrameCodec.TryReadParticipant(RelayFrameCodec.WriteParticipant(full), out _, out var first, buffers));
            AssertSame(full, first);
            Assert.IsTrue(RelayFrameCodec.TryReadParticipant(RelayFrameCodec.WriteParticipant(bare), out _, out var second, buffers));
            AssertSame(bare, second);
            Assert.IsTrue(RelayFrameCodec.TryReadParticipant("{\"type\":\"tf.participant\",\"p\":[4,5,6]}", out _, out var noRotation, buffers));
            AssertExact(new Vector3(4f, 5f, 6f), noRotation.Position);
            AssertExact(Quaternion.identity, noRotation.Rotation);
            Assert.IsFalse(noRotation.HasVelocity);
            Assert.IsFalse(noRotation.HasExt);
            // The JObject fallback shares them too.
            Assert.IsTrue(RelayFrameCodec.TryReadParticipant(JObject.Parse(RelayFrameCodec.WriteParticipant(full)), out _, out var third, buffers));
            AssertSame(full, third);
        }

        [Test]
        public void SharedBuffers_ReadEveryObjectFrameAsFreshOnesDo()
        {
            var buffers = new RelayFrameCodec.ReadBuffers();
            var text = RelayFrameCodec.WriteObjects(Objects());
            var fresh = new List<ObjectFrame>();
            Assert.IsTrue(RelayFrameCodec.TryReadObjects(text, out _, fresh));

            Assert.IsFalse(RelayFrameCodec.TryReadObjects(
                "{\"type\":\"tf.objects\",\"frames\":[{\"objectId\":\"a\",\"p\":[1,2,3],\"r\":[9,9,9]}]}", out _, new List<ObjectFrame>(), buffers));
            // The second frame has neither r nor v: it must not inherit the first one's from the buffer.
            for (var round = 0; round < 2; round++)
            {
                var shared = new List<ObjectFrame>();
                Assert.IsTrue(RelayFrameCodec.TryReadObjects(text, out _, shared, buffers));
                Assert.AreEqual(fresh.Count, shared.Count);
                for (var i = 0; i < fresh.Count; i++) AssertSame(fresh[i], shared[i]);
            }
            var fromJObject = new List<ObjectFrame>();
            Assert.IsTrue(RelayFrameCodec.TryReadObjects(JObject.Parse(text), out _, fromJObject, buffers));
            Assert.AreEqual(fresh.Count, fromJObject.Count);
            for (var i = 0; i < fresh.Count; i++) AssertSame(fresh[i], fromJObject[i]);
        }
    }
}
