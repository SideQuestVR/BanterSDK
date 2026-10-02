using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>
    /// Production's three kinematic states of a synced body (PacketPartySynchronizedTransform.cs:965-999):
    /// never bound → left as authored (no freeze before the join); bound and owned → the authored setting;
    /// bound (or previously bound) and not owned → kinematic. Transform-bound objects are never touched.
    /// </summary>
    public class SyncedKinematicsTests
    {
        // (everBound, owned, rigidbodyBinding, authoredKinematic) → desired isKinematic, or null = untouched.
        [TestCase(false, false, true, false, null, TestName = "NeverBound_Dynamic_IsUntouched")]
        [TestCase(false, false, true, true, null, TestName = "NeverBound_Kinematic_IsUntouched")]
        [TestCase(true, true, true, false, false, TestName = "BoundOwner_GetsAuthoredDynamic")]
        [TestCase(true, true, true, true, true, TestName = "BoundOwner_GetsAuthoredKinematic")]
        [TestCase(true, false, true, false, true, TestName = "BoundNotOwner_IsKinematic")]
        [TestCase(true, false, true, true, true, TestName = "BoundNotOwner_StaysKinematic")]
        [TestCase(true, false, false, false, null, TestName = "TransformBinding_NotOwner_IsUntouched")]
        [TestCase(true, true, false, false, null, TestName = "TransformBinding_Owner_IsUntouched")]
        public void DesiredKinematic_FollowsTheStateTable(bool everBound, bool owned, bool rigidbodyBinding, bool authoredKinematic, bool? expected)
        {
            Assert.AreEqual(expected, LocalSyncedTransform.DesiredKinematic(everBound, owned, rigidbodyBinding, authoredKinematic));
        }

        [Test]
        public void PreviouslyBound_WithoutARecord_IsKinematic()
        {
            // Disconnected (the record mirror is cleared, so nobody is "owner"): a once-bound body freezes, as in
            // production, where the object keeps its id across the disconnect.
            Assert.AreEqual(true, LocalSyncedTransform.DesiredKinematic(everBound: true, owned: false, rigidbodyBinding: true, authoredKinematic: false));
        }

        [Test]
        public void FallThreshold_IsTheSerializedProductionValue()
        {
            Assert.AreEqual(-250f, SyncedObjectsModule.FallThreshold);
        }
    }
}
