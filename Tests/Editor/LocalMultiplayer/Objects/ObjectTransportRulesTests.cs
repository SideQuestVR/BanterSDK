// <mirror source="Packages/com.sidequest.packetparty/Tests/Editor/ObjectTransportTests.cs" sha256="c0e8e9920b1fd1e87c51140a1b69c1cf21f622a9449b77482c9933061b1419e7" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Tests/Editor/TransformFrameCodecTests.cs" sha256="8b5f871dea3987c2b0e63a8d083dcba734d4536a944f5829458869f91e710ba8" mode="port" />
// ReceiveGuard_BindsSenderGenerationAndSequence (ObjectTransportTests.cs:44-56) and SequenceComparisonHandlesWrap
// (TransformFrameCodecTests.cs:48-54) with the envelope's fields passed directly, plus the frame guard.
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>Which object payloads and transform frames a receiver accepts: owner, generation, newer sequence.</summary>
    public class ObjectTransportRulesTests
    {
        [Test]
        public void ReceiveGuard_BindsSenderGenerationAndSequence()
        {
            Assert.IsTrue(ObjectTransportRules.ShouldAcceptEnvelope("owner", 7, 7, 11, "owner", 10));
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptEnvelope("owner", 7, 7, 11, "attacker", 10));
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptEnvelope("owner", 6, 7, 11, "owner", 10));
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptEnvelope("owner", 7, 7, 11, "owner", 11));
            Assert.IsTrue(ObjectTransportRules.ShouldAcceptEnvelope("owner", 7, 7, 2, "owner", 65535));
        }

        [Test]
        public void ReceiveGuard_AcceptsTheFirstPayloadOfAStream()
        {
            Assert.IsTrue(ObjectTransportRules.ShouldAcceptEnvelope("owner", 3, 3, 1, "owner", null));
            Assert.IsTrue(ObjectTransportRules.ShouldAcceptEnvelope("owner", 3, 3, 0, "owner", null));
        }

        [Test]
        public void ReceiveGuard_RejectsAMissingSender()
        {
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptEnvelope("owner", 3, 3, 1, null, null));
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptEnvelope("owner", 3, 3, 1, "", null));
        }

        [Test]
        public void SequenceComparisonHandlesWrap()
        {
            Assert.That(ObjectTransportRules.IsNewerSequence(0, ushort.MaxValue), Is.True);
            Assert.That(ObjectTransportRules.IsNewerSequence(ushort.MaxValue, 0), Is.False);
            Assert.That(ObjectTransportRules.IsNewerSequence(4, 4), Is.False);
        }

        [Test]
        public void Sequences_AreNewerWithinHalfTheWindow()
        {
            Assert.IsTrue(ObjectTransportRules.IsNewerSequence(0x7FFF, 0));
            Assert.IsFalse(ObjectTransportRules.IsNewerSequence(0x8000, 0));
            Assert.IsTrue(ObjectTransportRules.IsSeqNewer(0x7FFF, 0));
            Assert.IsFalse(ObjectTransportRules.IsSeqNewer(0x8000, 0));
            Assert.IsTrue(ObjectTransportRules.IsSeqNewer(1, 0xFFFF));
            Assert.IsFalse(ObjectTransportRules.IsSeqNewer(5, 5));
        }

        [Test]
        public void SequenceComparisons_Agree()
        {
            for (int last = 0; last <= 0xFFFF; last += 4099)
            {
                for (int step = 0; step <= 0xFFFF; step += 997)
                {
                    int candidate = (last + step) & 0xFFFF;
                    Assert.AreEqual(
                        ObjectTransportRules.IsNewerSequence((ushort)candidate, (ushort)last),
                        ObjectTransportRules.IsSeqNewer(candidate, last),
                        $"{candidate} vs {last}");
                }
            }
        }

        [Test]
        public void FrameGuard_AcceptsOnlyTheOwnerAtTheRecordGeneration()
        {
            Assert.IsTrue(ObjectTransportRules.ShouldAcceptFrame("rsess_a", 2, "rsess_a", 2));
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptFrame("rsess_a", 2, "rsess_b", 2));
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptFrame("rsess_a", 2, "rsess_a", 1));
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptFrame("rsess_a", 2, "rsess_a", 3));
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptFrame("rsess_a", 2, null, 2));
            Assert.IsFalse(ObjectTransportRules.ShouldAcceptFrame(null, 2, null, 2));
        }

        [Test]
        public void PayloadKinds_AreProductions()
        {
            Assert.AreEqual(1, ObjectTransportRules.KindOpaqueBinary);
            Assert.AreEqual(2, ObjectTransportRules.KindOpaqueJson);
            Assert.AreEqual(3, ObjectTransportRules.KindBatch);
        }
    }
}
