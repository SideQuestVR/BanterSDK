using System;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>
    /// The packetparty.attachment payload must stay byte-identical to PacketPartyHeldObject.Encode, little-endian:
    /// [u8 1][bool attached][u8 keyLen][utf8 key][3 x f32 position][4 x f32 rotation].
    /// </summary>
    public class HeldObjectCodecTests
    {
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        static readonly Quaternion QuarterTurnY = new Quaternion(0f, 0.70710677f, 0f, 0.70710677f);

        [Test]
        public void Encode_MatchesTheGoldenBytes()
        {
            byte[] payload = HeldObjectCodec.Encode(true, "right", new Vector3(1f, -2f, 0.5f), QuarterTurnY);
            Assert.AreEqual(
                "010105" + "7269676874" +
                "0000803f" + "000000c0" + "0000003f" +
                "00000000" + "f304353f" + "00000000" + "f304353f",
                Hex(payload));
        }

        [Test]
        public void Encode_DetachedWithoutKey_IsThirtyOneBytes()
        {
            byte[] payload = HeldObjectCodec.Encode(false, "", Vector3.zero, Quaternion.identity);
            Assert.AreEqual("010000" + "000000000000000000000000" + "000000000000000000000000" + "0000803f", Hex(payload));
            Assert.AreEqual(31, payload.Length);
        }

        [TestCase("left")]
        [TestCase("right")]
        [TestCase("head")]
        [TestCase("ünïcode")]
        public void Encode_LengthIsThirtyOnePlusTheUtf8Key(string key)
        {
            byte[] payload = HeldObjectCodec.Encode(true, key, Vector3.one, Quaternion.identity);
            Assert.AreEqual(31 + System.Text.Encoding.UTF8.GetByteCount(key), payload.Length);
            Assert.AreEqual(HeldObjectCodec.WireVersion, payload[0]);
            Assert.AreEqual(1, payload[1]);
            Assert.AreEqual(System.Text.Encoding.UTF8.GetByteCount(key), payload[2]);
        }

        [Test]
        public void RoundTrip_KeepsEveryField()
        {
            var position = new Vector3(0.25f, 1.5f, -3f);
            byte[] payload = HeldObjectCodec.Encode(true, "left", position, QuarterTurnY);
            Assert.IsTrue(HeldObjectCodec.TryDecode(payload, out bool attached, out string key, out Vector3 decodedPosition, out Quaternion rotation));
            Assert.IsTrue(attached);
            Assert.AreEqual("left", key);
            Assert.AreEqual(position, decodedPosition);
            Assert.AreEqual(QuarterTurnY.x, rotation.x);
            Assert.AreEqual(QuarterTurnY.y, rotation.y);
            Assert.AreEqual(QuarterTurnY.z, rotation.z);
            Assert.AreEqual(QuarterTurnY.w, rotation.w);
        }

        [Test]
        public void Decode_RejectsAnotherVersion()
        {
            byte[] payload = HeldObjectCodec.Encode(true, "right", Vector3.zero, Quaternion.identity);
            payload[0] = 2;
            Assert.IsFalse(HeldObjectCodec.TryDecode(payload, out _, out _, out _, out _));
        }

        [Test]
        public void Decode_RejectsTruncatedPayloads()
        {
            Assert.IsFalse(HeldObjectCodec.TryDecode(null, out _, out _, out _, out _));
            Assert.IsFalse(HeldObjectCodec.TryDecode(new byte[30], out _, out _, out _, out _));
            byte[] payload = HeldObjectCodec.Encode(true, "right", Vector3.zero, Quaternion.identity);
            Array.Resize(ref payload, payload.Length - 1);
            Assert.IsFalse(HeldObjectCodec.TryDecode(payload, out _, out _, out _, out _));
        }

        [Test]
        public void Decode_RejectsAKeyLongerThanThePayload()
        {
            byte[] payload = HeldObjectCodec.Encode(true, "right", Vector3.zero, Quaternion.identity);
            payload[2] = 6;
            Assert.IsFalse(HeldObjectCodec.TryDecode(payload, out _, out _, out _, out _));
        }

        [Test]
        public void Decode_RejectsNonFiniteValues()
        {
            Assert.IsFalse(HeldObjectCodec.TryDecode(
                HeldObjectCodec.Encode(true, "right", new Vector3(float.NaN, 0f, 0f), Quaternion.identity), out _, out _, out _, out _));
            Assert.IsFalse(HeldObjectCodec.TryDecode(
                HeldObjectCodec.Encode(true, "right", Vector3.zero, new Quaternion(0f, 0f, 0f, float.PositiveInfinity)), out _, out _, out _, out _));
        }

        [Test]
        public void Encode_RejectsAKeyLongerThan255Bytes()
        {
            Assert.Throws<ArgumentException>(() => HeldObjectCodec.Encode(true, new string('k', 256), Vector3.zero, Quaternion.identity));
        }
    }
}
