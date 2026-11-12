using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// The two hot lanes of RELAY.md 6.2, tf.participant (the player pose) and tf.objects (owned synced-object
    /// transforms), written straight from and read straight into structs. They run at 30 Hz per player and per
    /// object, so they skip JObject: the writer appends to a reused StringBuilder on the main thread and the
    /// reader streams the text with a JsonTextReader on the transport's receive thread.
    /// </summary>
    internal static class RelayFrameCodec
    {
        /// <summary>Numbers in tf.participant's ext.o: hip world pose, then head, left and right hand hip-local poses.</summary>
        public const int ExtNumbers = 28;
        /// <summary>The relay drops tf.objects messages over 64 KiB; stay under it with room for the stamps.</summary>
        public const int MaxObjectsMessageChars = 60 * 1024;

        const string ParticipantPrefix = "{\"type\":\"tf.participant\"";
        const string ObjectsPrefix = "{\"type\":\"tf.objects\"";

        /// <summary>
        /// The number arrays a reader fills, kept between frames so the 30 Hz lanes don't allocate them per
        /// frame. A reader only uses an array that the current read filled completely, and copies it into the
        /// frame before the next read, so nothing carries over from one frame to the next. Not thread-safe: one
        /// per receive loop (RelayConnection keeps its own).
        /// </summary>
        internal sealed class ReadBuffers
        {
            /// <summary>ext.o, the 28 numbers of the hip, head and hand poses.</summary>
            internal readonly float[] Ext = new float[ExtNumbers];
            /// <summary>One vector (3) or quaternion (4).</summary>
            internal readonly float[] Numbers = new float[4];
        }

        // ---------------------------------------------------------------- write

        /// <summary>Appends one tf.participant message (key "root", as production's participant transform).</summary>
        public static void WriteParticipant(StringBuilder sb, in ParticipantFrame frame)
        {
            sb.Append(ParticipantPrefix).Append(",\"key\":\"root\",\"seq\":");
            sb.Append(frame.Seq.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"t\":");
            AppendDouble(sb, frame.T);
            sb.Append(",\"vf\":").Append(frame.ValidForMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"d\":").Append(frame.Discontinuity ? "true" : "false");
            sb.Append(",\"p\":");
            AppendVector3(sb, frame.Position);
            sb.Append(",\"r\":");
            AppendQuaternion(sb, frame.Rotation);
            if (frame.HasVelocity)
            {
                sb.Append(",\"v\":");
                AppendVector3(sb, frame.Velocity);
            }
            if (frame.HasExt)
            {
                var ext = frame.Ext;
                sb.Append(",\"ext\":{\"o\":[");
                AppendPose(sb, ext.HipPosition, ext.HipRotation);
                sb.Append(',');
                AppendPose(sb, ext.HeadPosition, ext.HeadRotation);
                sb.Append(',');
                AppendPose(sb, ext.LeftPosition, ext.LeftRotation);
                sb.Append(',');
                AppendPose(sb, ext.RightPosition, ext.RightRotation);
                sb.Append("],\"s\":").Append(ext.Seated ? '1' : '0').Append('}');
            }
            sb.Append('}');
        }

        /// <summary>Opens a tf.objects message; append frames with <see cref="AppendObjectFrame"/>, then close it.</summary>
        public static void BeginObjects(StringBuilder sb)
        {
            sb.Append(ObjectsPrefix).Append(",\"frames\":[");
        }

        public static void AppendObjectFrame(StringBuilder sb, in ObjectFrame frame, bool first)
        {
            if (!first) sb.Append(',');
            sb.Append("{\"objectId\":");
            AppendString(sb, frame.ObjectId ?? "");
            sb.Append(",\"gen\":").Append(frame.Generation.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"epoch\":").Append(frame.Epoch.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"seq\":").Append(frame.Seq.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"t\":");
            AppendDouble(sb, frame.T);
            sb.Append(",\"vf\":").Append(frame.ValidForMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"p\":");
            AppendVector3(sb, frame.Position);
            // RELAY.md 6.2: r only when rotation is synced, v only when the object has a Rigidbody.
            if (frame.HasRotation)
            {
                sb.Append(",\"r\":");
                AppendQuaternion(sb, frame.Rotation);
            }
            if (frame.HasVelocity)
            {
                sb.Append(",\"v\":");
                AppendVector3(sb, frame.Velocity);
            }
            sb.Append('}');
        }

        public static void EndObjects(StringBuilder sb)
        {
            sb.Append("]}");
        }

        /// <summary>One tf.objects message holding <paramref name="frames"/> (tests and one-off sends).</summary>
        public static string WriteObjects(IReadOnlyList<ObjectFrame> frames)
        {
            var sb = new StringBuilder(256);
            BeginObjects(sb);
            for (var i = 0; i < frames.Count; i++)
            {
                AppendObjectFrame(sb, frames[i], i == 0);
            }
            EndObjects(sb);
            return sb.ToString();
        }

        public static string WriteParticipant(in ParticipantFrame frame)
        {
            var sb = new StringBuilder(256);
            WriteParticipant(sb, frame);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- read

        /// <summary>
        /// Cheap pre-check on the raw text: the host writes type first and the relay only adds its stamps, so
        /// a hot frame starts with its type. Anything else still parses, through JObject.
        /// </summary>
        public static bool LooksLikeParticipant(string json)
        {
            return json != null && json.StartsWith(ParticipantPrefix, StringComparison.Ordinal);
        }

        public static bool LooksLikeObjects(string json)
        {
            return json != null && json.StartsWith(ObjectsPrefix, StringComparison.Ordinal);
        }

        /// <summary>Reads a tf.participant message; <paramref name="from"/> is the relay's sender stamp.
        /// <paramref name="buffers"/>: the caller's reused ones (a receive loop), or null for fresh ones.</summary>
        public static bool TryReadParticipant(string json, out string from, out ParticipantFrame frame, ReadBuffers buffers = null)
        {
            from = null;
            frame = default;
            frame.Rotation = Quaternion.identity;
            try
            {
                using (var reader = NewReader(json))
                {
                    return ReadParticipant(reader, ref from, ref frame, buffers ?? new ReadBuffers());
                }
            }
            catch (JsonException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        /// <summary>Reads a tf.objects message into <paramref name="frames"/> (appended).</summary>
        public static bool TryReadObjects(string json, out string from, List<ObjectFrame> frames, ReadBuffers buffers = null)
        {
            from = null;
            try
            {
                using (var reader = NewReader(json))
                {
                    return ReadObjects(reader, ref from, frames, buffers ?? new ReadBuffers());
                }
            }
            catch (JsonException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        /// <summary>The fallback for a hot frame that didn't start with its type: convert the parsed JObject.</summary>
        public static bool TryReadParticipant(JObject json, out string from, out ParticipantFrame frame, ReadBuffers buffers = null)
        {
            from = null;
            frame = default;
            frame.Rotation = Quaternion.identity;
            if (json == null) return false;
            try
            {
                using (var reader = json.CreateReader())
                {
                    return ReadParticipant(reader, ref from, ref frame, buffers ?? new ReadBuffers());
                }
            }
            catch (JsonException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        public static bool TryReadObjects(JObject json, out string from, List<ObjectFrame> frames, ReadBuffers buffers = null)
        {
            from = null;
            if (json == null) return false;
            try
            {
                using (var reader = json.CreateReader())
                {
                    return ReadObjects(reader, ref from, frames, buffers ?? new ReadBuffers());
                }
            }
            catch (JsonException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        static JsonTextReader NewReader(string json)
        {
            // Dates and big floats stay as written: these lanes carry only numbers and ids.
            return new JsonTextReader(new StringReader(json ?? ""))
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
            };
        }

        static bool ReadParticipant(JsonReader reader, ref string from, ref ParticipantFrame frame, ReadBuffers buffers)
        {
            if (!reader.Read() || reader.TokenType != JsonToken.StartObject) return false;
            var isParticipant = false;
            var hasPosition = false;
            var closed = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                {
                    closed = true;
                    break;
                }
                if (reader.TokenType != JsonToken.PropertyName) return false;
                var name = (string)reader.Value;
                if (!reader.Read()) return false;
                switch (name)
                {
                    case "type":
                        isParticipant = reader.TokenType == JsonToken.String &&
                                        string.Equals((string)reader.Value, RelayProtocol.ParticipantFrame, StringComparison.Ordinal);
                        if (!isParticipant) return false;
                        break;
                    case "from":
                        from = reader.TokenType == JsonToken.String ? (string)reader.Value : null;
                        break;
                    case "seq":
                        frame.Seq = (uint)ReadLong(reader);
                        break;
                    case "t":
                        frame.T = ReadDouble(reader);
                        break;
                    case "vf":
                        frame.ValidForMs = (int)ReadLong(reader);
                        break;
                    case "d":
                        frame.Discontinuity = reader.TokenType == JsonToken.Boolean && (bool)reader.Value;
                        break;
                    case "p":
                        if (!ReadVector3(reader, buffers.Numbers, out frame.Position)) return false;
                        hasPosition = true;
                        break;
                    case "r":
                        if (reader.TokenType == JsonToken.Null) break;
                        if (!ReadQuaternion(reader, buffers.Numbers, out frame.Rotation)) return false;
                        break;
                    case "v":
                        if (reader.TokenType == JsonToken.Null) break;
                        if (!ReadVector3(reader, buffers.Numbers, out frame.Velocity)) return false;
                        frame.HasVelocity = true;
                        break;
                    case "ext":
                        if (reader.TokenType == JsonToken.Null) break;
                        if (!ReadExt(reader, ref frame, buffers.Ext)) return false;
                        break;
                    default:
                        // fromPeerId, key and anything a newer relay adds.
                        reader.Skip();
                        break;
                }
            }
            return closed && isParticipant && hasPosition;
        }

        // o: ExtNumbers long; read here, copied into the frame's PoseExt at the end.
        static bool ReadExt(JsonReader reader, ref ParticipantFrame frame, float[] o)
        {
            if (reader.TokenType != JsonToken.StartObject) return false;
            var hasPose = false;
            var seated = false;
            var closed = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                {
                    closed = true;
                    break;
                }
                if (reader.TokenType != JsonToken.PropertyName) return false;
                var name = (string)reader.Value;
                if (!reader.Read()) return false;
                if (name == "o")
                {
                    if (!ReadFloats(reader, o, ExtNumbers)) return false;
                    hasPose = true;
                }
                else if (name == "s")
                {
                    seated = reader.TokenType == JsonToken.Boolean ? (bool)reader.Value : ReadDouble(reader) != 0d;
                }
                else
                {
                    reader.Skip();
                }
            }
            if (!closed || !hasPose) return false;
            frame.HasExt = true;
            frame.Ext = new PoseExt
            {
                HipPosition = new Vector3(o[0], o[1], o[2]),
                HipRotation = new Quaternion(o[3], o[4], o[5], o[6]),
                HeadPosition = new Vector3(o[7], o[8], o[9]),
                HeadRotation = new Quaternion(o[10], o[11], o[12], o[13]),
                LeftPosition = new Vector3(o[14], o[15], o[16]),
                LeftRotation = new Quaternion(o[17], o[18], o[19], o[20]),
                RightPosition = new Vector3(o[21], o[22], o[23]),
                RightRotation = new Quaternion(o[24], o[25], o[26], o[27]),
                Seated = seated,
            };
            return true;
        }

        static bool ReadObjects(JsonReader reader, ref string from, List<ObjectFrame> frames, ReadBuffers buffers)
        {
            if (!reader.Read() || reader.TokenType != JsonToken.StartObject) return false;
            var isObjects = false;
            var hasFrames = false;
            var closed = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                {
                    closed = true;
                    break;
                }
                if (reader.TokenType != JsonToken.PropertyName) return false;
                var name = (string)reader.Value;
                if (!reader.Read()) return false;
                switch (name)
                {
                    case "type":
                        isObjects = reader.TokenType == JsonToken.String &&
                                    string.Equals((string)reader.Value, RelayProtocol.ObjectFrames, StringComparison.Ordinal);
                        if (!isObjects) return false;
                        break;
                    case "from":
                        from = reader.TokenType == JsonToken.String ? (string)reader.Value : null;
                        break;
                    case "frames":
                        if (reader.TokenType != JsonToken.StartArray) return false;
                        var arrayClosed = false;
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonToken.EndArray)
                            {
                                arrayClosed = true;
                                break;
                            }
                            if (!ReadObjectFrame(reader, buffers.Numbers, out var frame)) return false;
                            frames.Add(frame);
                        }
                        if (!arrayClosed) return false;
                        hasFrames = true;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            return closed && isObjects && hasFrames;
        }

        static bool ReadObjectFrame(JsonReader reader, float[] numbers, out ObjectFrame frame)
        {
            frame = default;
            frame.Rotation = Quaternion.identity;
            if (reader.TokenType != JsonToken.StartObject) return false;
            var hasPosition = false;
            var closed = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                {
                    closed = true;
                    break;
                }
                if (reader.TokenType != JsonToken.PropertyName) return false;
                var name = (string)reader.Value;
                if (!reader.Read()) return false;
                switch (name)
                {
                    case "objectId":
                        frame.ObjectId = reader.TokenType == JsonToken.String ? (string)reader.Value : null;
                        break;
                    case "gen":
                        frame.Generation = (uint)ReadLong(reader);
                        break;
                    case "epoch":
                        frame.Epoch = (int)ReadLong(reader);
                        break;
                    case "seq":
                        frame.Seq = (ushort)ReadLong(reader);
                        break;
                    case "t":
                        frame.T = ReadDouble(reader);
                        break;
                    case "vf":
                        frame.ValidForMs = (int)ReadLong(reader);
                        break;
                    case "p":
                        if (!ReadVector3(reader, numbers, out frame.Position)) return false;
                        hasPosition = true;
                        break;
                    case "r":
                        if (reader.TokenType == JsonToken.Null) break;
                        if (!ReadQuaternion(reader, numbers, out frame.Rotation)) return false;
                        frame.HasRotation = true;
                        break;
                    case "v":
                        if (reader.TokenType == JsonToken.Null) break;
                        if (!ReadVector3(reader, numbers, out frame.Velocity)) return false;
                        frame.HasVelocity = true;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            return closed && hasPosition && !string.IsNullOrEmpty(frame.ObjectId);
        }

        // numbers: at least 4 long (ReadBuffers.Numbers); only the first 3 are read and used.
        static bool ReadVector3(JsonReader reader, float[] numbers, out Vector3 value)
        {
            value = default;
            if (!ReadFloats(reader, numbers, 3)) return false;
            value = new Vector3(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        static bool ReadQuaternion(JsonReader reader, float[] numbers, out Quaternion value)
        {
            value = Quaternion.identity;
            if (!ReadFloats(reader, numbers, 4)) return false;
            value = new Quaternion(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        // An array of exactly `count` numbers.
        static bool ReadFloats(JsonReader reader, float[] into, int count)
        {
            if (reader.TokenType != JsonToken.StartArray) return false;
            var index = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndArray) return index == count;
                if (index >= count) return false;
                if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float) return false;
                into[index++] = (float)ReadDouble(reader);
            }
            return false;
        }

        static double ReadDouble(JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                case JsonToken.Float:
                    return Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                default:
                    return 0d;
            }
        }

        static long ReadLong(JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                    return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
                case JsonToken.Float:
                    return (long)Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                default:
                    return 0L;
            }
        }

        // ---------------------------------------------------------------- number and string formatting

        static void AppendPose(StringBuilder sb, Vector3 position, Quaternion rotation)
        {
            AppendFloat(sb, position.x);
            sb.Append(',');
            AppendFloat(sb, position.y);
            sb.Append(',');
            AppendFloat(sb, position.z);
            sb.Append(',');
            AppendFloat(sb, rotation.x);
            sb.Append(',');
            AppendFloat(sb, rotation.y);
            sb.Append(',');
            AppendFloat(sb, rotation.z);
            sb.Append(',');
            AppendFloat(sb, rotation.w);
        }

        static void AppendVector3(StringBuilder sb, Vector3 value)
        {
            sb.Append('[');
            AppendFloat(sb, value.x);
            sb.Append(',');
            AppendFloat(sb, value.y);
            sb.Append(',');
            AppendFloat(sb, value.z);
            sb.Append(']');
        }

        static void AppendQuaternion(StringBuilder sb, Quaternion value)
        {
            sb.Append('[');
            AppendFloat(sb, value.x);
            sb.Append(',');
            AppendFloat(sb, value.y);
            sb.Append(',');
            AppendFloat(sb, value.z);
            sb.Append(',');
            AppendFloat(sb, value.w);
            sb.Append(']');
        }

        // G9 always round-trips a float; JSON has no NaN or infinity, so those go out as 0.
        static void AppendFloat(StringBuilder sb, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
            sb.Append(value.ToString("G9", CultureInfo.InvariantCulture));
        }

        static void AppendDouble(StringBuilder sb, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0d;
            sb.Append(value.ToString("G17", CultureInfo.InvariantCulture));
        }

        static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
