using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BS.Security;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// A minimal in-memory editor for the Unity-behaviour experiments (BundleCheckUnityBehaviourTests,
    /// BundleCheckUnityBehaviour2Tests): parses a UnityFS bundle through the checker's BundleArchive, parses its
    /// format-22 serialized files (type trees, objects, scripts, externals, ref types), lets a test edit them, and
    /// writes everything back as an uncompressed archive in Unity's own layout. No UnityEngine or UnityEditor API, so it
    /// is round-trip tested offline against real bundles too. Edits happen in memory only.
    /// </summary>
    internal static class BundleProbeEditor
    {
        /// <summary>Rewrites the bundle's file holding a <paramref name="behaviourClass"/> behaviour; <paramref name="edit"/> gets the file and that behaviour.</summary>
        internal static byte[] Edit(byte[] bundle, Action<RawFile, RawFile.Located> edit, string behaviourClass = "Button")
        {
            var raw = RawBundle.Parse(bundle);
            var file = raw.Files.First(f => f.Flags == 4 && f.HasBehaviour(behaviourClass));
            edit(file, file.Behaviour(behaviourClass));
            return raw.Write();
        }

        /// <summary>Rewrites the whole archive (node names, extra nodes).</summary>
        internal static byte[] EditBundle(byte[] bundle, Action<RawBundle> edit)
        {
            var raw = RawBundle.Parse(bundle);
            edit(raw);
            return raw.Write();
        }

        /// <summary>The bundle parsed and written back with no edit: what every experiment's rewrite starts from.</summary>
        internal static byte[] RoundTrip(byte[] bundle) => RawBundle.Parse(bundle).Write();

        /// <summary>A UnityFS bundle, its files as bytes (the serialized ones parsed), written back uncompressed.</summary>
        internal sealed class RawBundle
        {
            internal readonly List<RawFile> Files = new List<RawFile>();
            byte[] head;
            int version;
            uint flags;

            internal string Mount => Files.Select(f => f.Name).Where(n => !n.Contains("/")).OrderBy(n => n.Length).First();

            internal static RawBundle Parse(byte[] bytes)
            {
                var stats = new BundleCheckStats();
                var archive = BundleArchive.Open(new ByteArrayBundleSource(bytes), BundleCheckOptions.World(), stats);
                var raw = new RawBundle();
                var p = 8;
                raw.version = BigEndian(bytes, p);
                p += 4;
                while (bytes[p] != 0) p++;
                p++;
                while (bytes[p] != 0) p++;
                p++;
                p += 8 + 4 + 4;
                raw.flags = (uint)BigEndian(bytes, p);
                p += 4;
                raw.head = bytes.Take(p).ToArray();
                foreach (var node in archive.Nodes)
                {
                    var data = new byte[node.Size];
                    archive.Read(node.Offset, data, 0, data.Length);
                    raw.Files.Add((node.Flags & 4) != 0 ? RawFile.Parse(node.Name, data, (int)node.Flags) : new RawFile(node.Name, data, (int)node.Flags));
                }
                return raw;
            }

            static int BigEndian(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

            /// <summary>Largest block written (Unity's own chunk size for LZ4 bundles).</summary>
            const int BlockSize = 128 * 1024;

            /// <summary>
            /// Writes the files back as an uncompressed UnityFS archive laid out as Unity writes one: the header (same
            /// signature, versions and flags, compression bits cleared, "block info at the end" cleared, "padding at
            /// start" 0x200 set), the block info padded to 16 bytes after the header, the data padded to 16 bytes after
            /// the block info (Unity reads it there when 0x200 is set), and the data in 128 KiB stored blocks (flags 0).
            /// </summary>
            internal byte[] Write()
            {
                var files = Files.Select(f => f.Write()).ToList();
                var info = new MemoryStream();
                void BE16(Stream s, int v)
                {
                    s.WriteByte((byte)(v >> 8));
                    s.WriteByte((byte)v);
                }
                void BE32(Stream s, long v)
                {
                    s.WriteByte((byte)(v >> 24));
                    s.WriteByte((byte)(v >> 16));
                    s.WriteByte((byte)(v >> 8));
                    s.WriteByte((byte)v);
                }
                void BE64(Stream s, long v)
                {
                    BE32(s, v >> 32);
                    BE32(s, v & 0xffffffffL);
                }
                var total = files.Sum(f => (long)f.Length);
                var blocks = (int)Math.Max(1, (total + BlockSize - 1) / BlockSize);
                info.Write(new byte[16], 0, 16); // uncompressed data hash: unused
                BE32(info, blocks);
                for (var i = 0; i < blocks; i++)
                {
                    var size = (int)Math.Min(BlockSize, total - (long)i * BlockSize);
                    BE32(info, size);
                    BE32(info, size);
                    BE16(info, 0); // stored, not streamed
                }
                BE32(info, files.Count);
                long offset = 0;
                for (var i = 0; i < files.Count; i++)
                {
                    BE64(info, offset);
                    BE64(info, files[i].Length);
                    BE32(info, Files[i].Flags);
                    var name = Encoding.UTF8.GetBytes(Files[i].Name);
                    info.Write(name, 0, name.Length);
                    info.WriteByte(0);
                    offset += files[i].Length;
                }
                var blocksInfo = info.ToArray();
                var headerEnd = head.Length;
                var infoAt = (headerEnd + 15) / 16 * 16;
                var dataAt = (infoAt + blocksInfo.Length + 15) / 16 * 16;
                var output = new MemoryStream();
                output.Write(head, 0, head.Length - 20);
                BE64(output, dataAt + total);
                BE32(output, blocksInfo.Length);
                BE32(output, blocksInfo.Length);
                BE32(output, (flags & ~0x3Fu & ~0x80u) | 0x40u | 0x200u);
                while (output.Length < infoAt) output.WriteByte(0);
                output.Write(blocksInfo, 0, blocksInfo.Length);
                while (output.Length < dataAt) output.WriteByte(0);
                foreach (var file in files) output.Write(file, 0, file.Length);
                return output.ToArray();
            }
        }

        /// <summary>A format-22 serialized file, parsed and written back (metadata rewritten, objects laid out again).</summary>
        internal sealed class RawFile
        {
            internal sealed class Node
            {
                internal ushort Version;
                internal byte Level, TypeFlags;
                internal uint TypeOffset, NameOffset, Index, MetaFlags;
                internal int Size;
                internal ulong RefHash;

                internal Node Clone() => (Node)MemberwiseClone();
            }

            internal sealed class TypeEntry
            {
                internal int ClassId;
                internal byte Stripped;
                internal short ScriptIndex;
                internal byte[] ScriptHash, Hash = new byte[16];
                internal List<Node> Nodes = new List<Node>();
                internal byte[] Strings = Array.Empty<byte>();
                internal int[] Dependencies = Array.Empty<int>();
                internal string RefClass, RefNamespace, RefAssembly;

                internal void ZeroHashes()
                {
                    Hash = new byte[16];
                    if (ScriptHash != null) ScriptHash = new byte[16];
                }

                static readonly Dictionary<uint, string> common = new Dictionary<uint, string>();

                internal string Text(uint offset)
                {
                    if ((offset & 0x80000000u) != 0) return CommonString(offset & 0x7fffffffu);
                    var end = (int)offset;
                    while (end < Strings.Length && Strings[end] != 0) end++;
                    return Encoding.UTF8.GetString(Strings, (int)offset, end - (int)offset);
                }

                internal string NameOf(int index) => Text(Nodes[index].NameOffset);
                internal string TypeOf(int index) => Text(Nodes[index].TypeOffset);

                internal uint AddString(string text)
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    var offset = (uint)Strings.Length;
                    Strings = Strings.Concat(bytes).Concat(new byte[] { 0 }).ToArray();
                    return offset;
                }

                internal int Find(string name, int level = -1)
                {
                    for (var i = 0; i < Nodes.Count; i++)
                    {
                        if (NameOf(i) == name && (level < 0 || Nodes[i].Level == level)) return i;
                    }
                    throw new InvalidOperationException("no field " + name);
                }

                /// <summary>
                /// The node at a field path from the root ("m_OnClick/m_PersistentCalls/m_Calls/Array/data/m_Mode"): one
                /// child per step, so a same-named field elsewhere (Selectable's m_Navigation.m_Mode) is never taken.
                /// </summary>
                internal int FindPath(string path)
                {
                    var at = 0;
                    foreach (var name in path.Split('/'))
                    {
                        var level = Nodes[at].Level + 1;
                        var end = SubtreeEnd(at);
                        var found = -1;
                        for (var i = at + 1; i < end; i++)
                        {
                            if (Nodes[i].Level == level && NameOf(i) == name)
                            {
                                found = i;
                                break;
                            }
                        }
                        if (found < 0) throw new InvalidOperationException("no field " + name + " in " + path);
                        at = found;
                    }
                    return at;
                }

                internal int SubtreeEnd(int index)
                {
                    var end = index + 1;
                    while (end < Nodes.Count && Nodes[end].Level > Nodes[index].Level) end++;
                    return end;
                }

                internal void InsertPrimitive(int at, int level, string type, string name, int size) =>
                    Nodes.Insert(at, new Node { Version = 1, Level = (byte)level, TypeOffset = AddString(type), NameOffset = AddString(name), Size = size });

                internal void InsertString(int at, int level, string name)
                {
                    Nodes.InsertRange(at, new[]
                    {
                        new Node { Version = 1, Level = (byte)level, TypeOffset = AddString("string"), NameOffset = AddString(name), Size = -1 },
                        new Node { Version = 1, Level = (byte)(level + 1), TypeFlags = 1, TypeOffset = AddString("Array"), NameOffset = AddString("Array"), Size = -1, MetaFlags = 0x4000 },
                        new Node { Version = 1, Level = (byte)(level + 2), TypeOffset = AddString("int"), NameOffset = AddString("size"), Size = 4 },
                        new Node { Version = 1, Level = (byte)(level + 2), TypeOffset = AddString("char"), NameOffset = AddString("data"), Size = 1 },
                    });
                }
            }

            internal sealed class ObjectEntry
            {
                internal long PathId;
                internal int TypeIndex;
                internal byte[] Data;
            }

            internal sealed class External
            {
                internal string Virtual = "", Path = "";
                internal byte[] Guid = new byte[16];
                internal int Type;
            }

            /// <summary>An object with its field offsets (from its own tree).</summary>
            internal sealed class Located
            {
                internal ObjectEntry Object;
                internal List<(string path, int node, int start, int end)> Fields;

                /// <summary>A field's first byte; paths are names joined by '/', array elements "Array/[i]".</summary>
                internal int StartOf(TypeEntry type, string path) => Field(path).start;

                /// <summary>One past a field's last byte (its alignment included).</summary>
                internal int EndOf(TypeEntry type, string path) => Field(path).end;

                (string path, int node, int start, int end) Field(string path)
                {
                    foreach (var field in Fields)
                    {
                        if (field.path == path) return field;
                    }
                    throw new InvalidOperationException("no field at " + path);
                }
            }

            internal string Name;
            internal readonly int Flags;
            readonly byte[] plain;
            internal string UnityVersion = "", UserInfo = "";
            internal int Platform;
            internal bool TypeTrees;
            internal readonly List<TypeEntry> Types = new List<TypeEntry>();
            internal readonly List<ObjectEntry> Objects = new List<ObjectEntry>();
            internal readonly List<(int fileId, long pathId)> Scripts = new List<(int, long)>();
            internal readonly List<External> Externals = new List<External>();
            internal readonly List<TypeEntry> RefTypes = new List<TypeEntry>();

            internal RawFile(string name, byte[] data, int flags)
            {
                Name = name;
                plain = data;
                Flags = flags;
            }

            static string CommonString(uint offset)
            {
                // Unity's shared string table, as AssetsTools ships it (only the names these edits look up matter).
                var table = commonStrings ?? (commonStrings = LoadCommonStrings());
                var end = (int)offset;
                while (end < table.Length && table[end] != 0) end++;
                return offset < table.Length ? Encoding.ASCII.GetString(table, (int)offset, end - (int)offset) : "?";
            }

            static byte[] commonStrings;

            static byte[] LoadCommonStrings()
            {
                // Unity's shared type-tree strings ("int", "m_Script", "Array"...), from the checker's own library
                // (AssetsTools.NET, loaded with BS.SDK) by reflection: the test assembly does not reference it.
                var field = Type.GetType("AssetsTools.NET.TypeTreeType, AssetsTools.NET")?.GetField("COMMON_STRING_TABLE",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return field?.GetValue(null) as byte[] ?? Array.Empty<byte>();
            }

            sealed class Reader
            {
                readonly byte[] b;
                internal int P;
                internal Reader(byte[] bytes, int start)
                {
                    b = bytes;
                    P = start;
                }
                internal byte U8() => b[P++];
                internal ushort U16() { var v = BitConverter.ToUInt16(b, P); P += 2; return v; }
                internal int I32() { var v = BitConverter.ToInt32(b, P); P += 4; return v; }
                internal uint U32() { var v = BitConverter.ToUInt32(b, P); P += 4; return v; }
                internal long I64() { var v = BitConverter.ToInt64(b, P); P += 8; return v; }
                internal ulong U64() { var v = BitConverter.ToUInt64(b, P); P += 8; return v; }
                internal byte[] Bytes(int n) { var v = new byte[n]; Buffer.BlockCopy(b, P, v, 0, n); P += n; return v; }
                internal string CStr() { var s = P; while (b[P] != 0) P++; var v = Encoding.UTF8.GetString(b, s, P - s); P++; return v; }
                internal void Align4() => P = (P + 3) & ~3;
                internal static int BE(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
                internal static long BE64(byte[] b, int at) => ((long)BE(b, at) << 32) | (uint)BE(b, at + 4);
            }

            internal static RawFile Parse(string name, byte[] data, int flags)
            {
                var file = new RawFile(name, data, flags);
                var format = Reader.BE(data, 8);
                if (format != 22) throw new InvalidOperationException("format " + format + " (the probe edits format 22)");
                if (data[16] != 0) throw new InvalidOperationException("big-endian file");
                var dataOffset = Reader.BE64(data, 32);
                var r = new Reader(data, 48);
                file.UnityVersion = r.CStr();
                file.Platform = r.I32();
                file.TypeTrees = r.U8() != 0;
                var typeCount = r.I32();
                for (var i = 0; i < typeCount; i++) file.Types.Add(ReadType(r, false, file.TypeTrees));
                var objectCount = r.I32();
                var spans = new List<(ObjectEntry entry, long offset, int size)>();
                for (var i = 0; i < objectCount; i++)
                {
                    r.Align4();
                    var entry = new ObjectEntry { PathId = r.I64() };
                    var offset = r.I64();
                    var size = (int)r.U32();
                    entry.TypeIndex = r.I32();
                    spans.Add((entry, offset, size));
                }
                var scriptCount = r.I32();
                for (var i = 0; i < scriptCount; i++)
                {
                    var fileId = r.I32();
                    r.Align4();
                    file.Scripts.Add((fileId, r.I64()));
                }
                var externalCount = r.I32();
                for (var i = 0; i < externalCount; i++)
                    file.Externals.Add(new External { Virtual = r.CStr(), Guid = r.Bytes(16), Type = r.I32(), Path = r.CStr() });
                var refCount = r.I32();
                for (var i = 0; i < refCount; i++) file.RefTypes.Add(ReadType(r, true, file.TypeTrees));
                file.UserInfo = r.CStr();
                foreach (var (entry, offset, size) in spans)
                {
                    entry.Data = new byte[size];
                    Buffer.BlockCopy(data, (int)(dataOffset + offset), entry.Data, 0, size);
                    file.Objects.Add(entry);
                }
                return file;
            }

            static TypeEntry ReadType(Reader r, bool isRef, bool trees)
            {
                var type = new TypeEntry { ClassId = r.I32(), Stripped = r.U8(), ScriptIndex = (short)r.U16() };
                if (type.ClassId == 114 || (isRef && type.ScriptIndex != -1)) type.ScriptHash = r.Bytes(16);
                type.Hash = r.Bytes(16);
                if (trees)
                {
                    var nodes = r.I32();
                    var strings = r.I32();
                    for (var i = 0; i < nodes; i++)
                        type.Nodes.Add(new Node
                        {
                            Version = r.U16(), Level = r.U8(), TypeFlags = r.U8(), TypeOffset = r.U32(), NameOffset = r.U32(), Size = r.I32(), Index = r.U32(),
                            MetaFlags = r.U32(), RefHash = r.U64(),
                        });
                    type.Strings = r.Bytes(strings);
                }
                if (isRef)
                {
                    type.RefClass = r.CStr();
                    type.RefNamespace = r.CStr();
                    type.RefAssembly = r.CStr();
                }
                else
                {
                    var count = r.I32();
                    type.Dependencies = new int[count];
                    for (var i = 0; i < count; i++) type.Dependencies[i] = r.I32();
                }
                return type;
            }

            internal byte[] Write()
            {
                if (Types.Count == 0 && Objects.Count == 0) return plain;
                const int headerSize = 48;
                var meta = new MemoryStream();
                var w = new BinaryWriter(meta);
                void Align4()
                {
                    while ((headerSize + meta.Length) % 4 != 0) w.Write((byte)0);
                }
                void CStr(string s)
                {
                    w.Write(Encoding.UTF8.GetBytes(s));
                    w.Write((byte)0);
                }
                void WriteType(TypeEntry type, bool isRef)
                {
                    w.Write(type.ClassId);
                    w.Write(type.Stripped);
                    w.Write((ushort)type.ScriptIndex);
                    if (type.ScriptHash != null) w.Write(type.ScriptHash);
                    w.Write(type.Hash);
                    if (TypeTrees)
                    {
                        w.Write(type.Nodes.Count);
                        w.Write(type.Strings.Length);
                        for (var i = 0; i < type.Nodes.Count; i++)
                        {
                            var n = type.Nodes[i];
                            w.Write(n.Version);
                            w.Write(n.Level);
                            w.Write(n.TypeFlags);
                            w.Write(n.TypeOffset);
                            w.Write(n.NameOffset);
                            w.Write(n.Size);
                            w.Write((uint)i);
                            w.Write(n.MetaFlags);
                            w.Write(n.RefHash);
                        }
                        w.Write(type.Strings);
                    }
                    if (isRef)
                    {
                        CStr(type.RefClass ?? "");
                        CStr(type.RefNamespace ?? "");
                        CStr(type.RefAssembly ?? "");
                    }
                    else
                    {
                        w.Write(type.Dependencies.Length);
                        foreach (var d in type.Dependencies) w.Write(d);
                    }
                }
                CStr(UnityVersion);
                w.Write(Platform);
                w.Write((byte)(TypeTrees ? 1 : 0));
                w.Write(Types.Count);
                foreach (var type in Types) WriteType(type, false);
                var data = new MemoryStream();
                var offsets = new List<long>();
                foreach (var o in Objects)
                {
                    while (data.Length % 8 != 0) data.WriteByte(0);
                    offsets.Add(data.Length);
                    data.Write(o.Data, 0, o.Data.Length);
                }
                w.Write(Objects.Count);
                for (var i = 0; i < Objects.Count; i++)
                {
                    Align4();
                    w.Write(Objects[i].PathId);
                    w.Write(offsets[i]);
                    w.Write((uint)Objects[i].Data.Length);
                    w.Write(Objects[i].TypeIndex);
                }
                w.Write(Scripts.Count);
                foreach (var (fileId, pathId) in Scripts)
                {
                    w.Write(fileId);
                    Align4();
                    w.Write(pathId);
                }
                w.Write(Externals.Count);
                foreach (var e in Externals)
                {
                    CStr(e.Virtual);
                    w.Write(e.Guid);
                    w.Write(e.Type);
                    CStr(e.Path);
                }
                w.Write(RefTypes.Count);
                foreach (var type in RefTypes) WriteType(type, true);
                CStr(UserInfo);
                w.Flush();
                var metadata = meta.ToArray();
                var dataOffset = (headerSize + metadata.Length + 15) / 16 * 16;
                var output = new MemoryStream();
                void BE32(long v)
                {
                    output.WriteByte((byte)(v >> 24));
                    output.WriteByte((byte)(v >> 16));
                    output.WriteByte((byte)(v >> 8));
                    output.WriteByte((byte)v);
                }
                BE32(0);
                BE32(0);
                BE32(22);
                BE32(0);
                BE32(0); // little-endian + reserved
                BE32(metadata.Length);
                BE32((dataOffset + data.Length) >> 32);
                BE32((dataOffset + data.Length) & 0xffffffffL);
                BE32(0);
                BE32(dataOffset);
                BE32(0);
                BE32(0);
                output.Write(metadata, 0, metadata.Length);
                while (output.Length < dataOffset) output.WriteByte(0);
                data.Position = 0;
                data.CopyTo(output);
                return output.ToArray();
            }

            // ---- lookups and edits used by the experiments

            string StringField(ObjectEntry o, string name)
            {
                var located = Locate(o);
                foreach (var field in located.Fields)
                {
                    if (field.path != name) continue;
                    var length = BitConverter.ToInt32(o.Data, field.start);
                    return Encoding.UTF8.GetString(o.Data, field.start + 4, length);
                }
                return null;
            }

            internal ObjectEntry Script(string className) =>
                Objects.First(o => Types[o.TypeIndex].ClassId == 115 && StringField(o, "m_ClassName") == className);

            internal long ScriptPathId(string className) => Script(className).PathId;

            internal bool HasBehaviour(string className)
            {
                if (!Objects.Any(o => Types[o.TypeIndex].ClassId == 115 && StringField(o, "m_ClassName") == className)) return false;
                var script = ScriptPathId(className);
                return Objects.Any(o => Types[o.TypeIndex].ClassId == 114 && BitConverter.ToInt32(o.Data, 16) == 0 && BitConverter.ToInt64(o.Data, 20) == script);
            }

            internal Located Behaviour(string className)
            {
                var script = ScriptPathId(className);
                var o = Objects.First(x => Types[x.TypeIndex].ClassId == 114 && BitConverter.ToInt32(x.Data, 16) == 0 && BitConverter.ToInt64(x.Data, 20) == script);
                return Locate(o);
            }

            /// <summary>Inserts bytes into an object's data (the tree must have been edited to match).</summary>
            internal void InsertData(ObjectEntry o, int at, byte[] bytes) => o.Data = o.Data.Take(at).Concat(bytes).Concat(o.Data.Skip(at)).ToArray();

            internal void SetPPtr(ObjectEntry o, int at, int fileId, long pathId)
            {
                BitConverter.GetBytes(fileId).CopyTo(o.Data, at);
                BitConverter.GetBytes(pathId).CopyTo(o.Data, at + 4);
            }

            /// <summary>Points the object's type entry at a script (its own entry, so other behaviours keep theirs).</summary>
            internal void SetTypeScript(ObjectEntry o, int fileId, long pathId)
            {
                var copy = Types[o.TypeIndex];
                var type = new TypeEntry
                {
                    ClassId = copy.ClassId, Stripped = copy.Stripped, ScriptHash = copy.ScriptHash, Hash = copy.Hash, Nodes = copy.Nodes.Select(n => n.Clone()).ToList(),
                    Strings = copy.Strings, Dependencies = copy.Dependencies, ScriptIndex = (short)Scripts.Count,
                };
                Scripts.Add((fileId, pathId));
                Types.Add(type);
                o.TypeIndex = Types.Count - 1;
            }

            /// <summary>Adds an external file; returns its file id.</summary>
            internal int AddExternal(string path)
            {
                Externals.Add(new External { Path = path });
                return Externals.Count;
            }

            internal void SetFirstString(ObjectEntry o, string value)
            {
                var length = BitConverter.ToInt32(o.Data, 0);
                var bytes = Encoding.UTF8.GetBytes(value);
                if (bytes.Length != length) throw new InvalidOperationException("same-length edits only");
                Buffer.BlockCopy(bytes, 0, o.Data, 4, bytes.Length);
            }

            internal void SetString(ObjectEntry o, string field, string value)
            {
                var located = Locate(o);
                var start = located.StartOf(Types[o.TypeIndex], field);
                var length = BitConverter.ToInt32(o.Data, start);
                var bytes = Encoding.UTF8.GetBytes(value);
                if (bytes.Length != length) throw new InvalidOperationException("same-length edits only");
                Buffer.BlockCopy(bytes, 0, o.Data, start + 4, bytes.Length);
            }

            /// <summary>A [SerializeReference] type with an empty tree (the value carries no fields).</summary>
            internal void AddRefType(string cls, string ns, string asm)
            {
                var type = new TypeEntry { ClassId = 114, ScriptIndex = -1, ScriptHash = new byte[16], RefClass = cls, RefNamespace = ns, RefAssembly = asm };
                type.Nodes.Add(new Node { Version = 1, Level = 0, TypeOffset = type.AddString(cls), NameOffset = type.AddString("Base"), Size = 0 });
                RefTypes.Add(type);
            }

            internal static (int version, int count, long firstRid, int firstEntry) ReadRegistry(byte[] data, int at)
            {
                var version = BitConverter.ToInt32(data, at);
                var count = BitConverter.ToInt32(data, at + 4);
                return (version, count, count > 0 ? BitConverter.ToInt64(data, at + 8) : 0, at + 8);
            }

            /// <summary>Every field's byte range in an object, walked with its own tree (registries end the walk).</summary>
            internal Located Locate(ObjectEntry o)
            {
                var type = Types[o.TypeIndex];
                var fields = new List<(string, int, int, int)>();
                var p = 0;
                Walk(type, 0, o.Data, ref p, "", fields);
                return new Located { Object = o, Fields = fields };
            }

            static void Walk(TypeEntry type, int me, byte[] data, ref int p, string path, List<(string, int, int, int)> fields)
            {
                var node = type.Nodes[me];
                var start = p;
                var children = new List<int>();
                for (var i = me + 1; i < type.Nodes.Count && type.Nodes[i].Level > node.Level; i++)
                {
                    if (type.Nodes[i].Level == node.Level + 1) children.Add(i);
                }
                string Child(int child) => path.Length == 0 ? type.NameOf(child) : path + "/" + type.NameOf(child);
                if (type.TypeOf(me) == "ManagedReferencesRegistry")
                {
                    p = data.Length; // the registry is the object's last field and runs to its end
                    fields.Add((path, me, start, p));
                    return;
                }
                if ((node.TypeFlags & 1) != 0)
                {
                    var count = BitConverter.ToInt32(data, p);
                    p += 4;
                    for (var i = 0; i < count; i++) Walk(type, children[1], data, ref p, path + "/[" + i + "]", fields);
                }
                else if (children.Count == 0)
                {
                    p += Math.Max(0, node.Size);
                }
                else
                {
                    foreach (var child in children) Walk(type, child, data, ref p, Child(child), fields);
                }
                if ((node.MetaFlags & 0x4000) != 0) p = (p + 3) & ~3;
                fields.Add((path, me, start, p));
            }
        }
    }
}
