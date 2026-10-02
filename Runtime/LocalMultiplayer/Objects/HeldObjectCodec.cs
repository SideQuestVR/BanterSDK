// <mirror source="Packages/com.sidequest.packetparty/Runtime/Components/PacketPartyHeldObject.cs" sha256="a0a25d0b3017d9664e5a1df826eec40b250c2a8dee2a20a4f629481d1ad422e7" mode="verbatim" />
// PacketPartyHeldObject.Encode / TryDecode (:180-214), verbatim apart from being public statics of their own class
// so the edit-mode tests can pin the byte layout.
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// The <c>packetparty.attachment</c> component payload: which hand anchor holds the object and the object's pose
    /// relative to it. Little-endian: [u8 version=1][bool attached][u8 keyLen][utf8 key][3 x f32 position][4 x f32 rotation].
    /// </summary>
    public static class HeldObjectCodec
    {
        public const string ComponentKey = "packetparty.attachment";
        public const byte WireVersion = 1;

        public static byte[] Encode(bool attached, string anchorKey, Vector3 position, Quaternion rotation)
        {
            byte[] key = Encoding.UTF8.GetBytes(anchorKey ?? string.Empty);
            if (key.Length > byte.MaxValue) throw new ArgumentException("anchorKey is too long", nameof(anchorKey));
            using var stream = new MemoryStream(32 + key.Length);
            using var writer = new BinaryWriter(stream);
            writer.Write(WireVersion);
            writer.Write(attached);
            writer.Write((byte)key.Length);
            writer.Write(key);
            writer.Write(position.x); writer.Write(position.y); writer.Write(position.z);
            writer.Write(rotation.x); writer.Write(rotation.y); writer.Write(rotation.z); writer.Write(rotation.w);
            return stream.ToArray();
        }

        public static bool TryDecode(byte[] body, out bool attached, out string anchorKey, out Vector3 position, out Quaternion rotation)
        {
            attached = false; anchorKey = null; position = default; rotation = Quaternion.identity;
            if (body == null || body.Length < 31) return false;
            try
            {
                using var stream = new MemoryStream(body, false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadByte() != WireVersion) return false;
                attached = reader.ReadBoolean();
                int keyLength = reader.ReadByte();
                if (stream.Length - stream.Position < keyLength + 28) return false;
                anchorKey = Encoding.UTF8.GetString(reader.ReadBytes(keyLength));
                position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                return float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z)
                    && float.IsFinite(rotation.x) && float.IsFinite(rotation.y) && float.IsFinite(rotation.z) && float.IsFinite(rotation.w);
            }
            catch (Exception) { return false; }
        }
    }
}
