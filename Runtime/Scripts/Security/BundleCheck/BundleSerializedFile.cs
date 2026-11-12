using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace BS.Security
{
    /// <summary>A PPtr as stored: file index (0 = this file, n = externals[n-1]) and path id.</summary>
    internal struct BundlePPtr
    {
        internal int FileId;
        internal long PathId;

        internal bool IsNull => FileId == 0 && PathId == 0;
    }

    /// <summary>One serialized file (scene, sharedAssets, CAB) inside a bundle: header, metadata, objects, type trees.</summary>
    internal sealed class BundleSerializedFile
    {
        internal const int ClassGameObject = 1, ClassAnimationClip = 74, ClassMonoBehaviour = 114, ClassMonoScript = 115, ClassAssetBundle = 142;

        /// <summary>
        /// The serialized-file formats the checker has been tested on (the whole live corpus, Unity 2021.3 to 6000.4, is
        /// format 22). Another format may lay out metadata or objects in a way the checker does not read: refused.
        /// </summary>
        /// <remarks>
        /// ON A UNITY UPGRADE: run BundleCheckUnityBehaviour2Tests.M6 (it builds a bundle with the project's own Unity and
        /// must pass). If the format changed, review AssetsTools.NET's support for it (header, metadata, type trees,
        /// ref types) and MetadataBounds below before adding the new number here.
        /// </remarks>
        internal static readonly uint[] SupportedFormats = { 22 };

        /// <summary>
        /// Unity's built-in resource files a bundle may reference (shaders, meshes, fonts, the UI Toolkit scripts): part of
        /// the player itself, never world content. Exact names, as Unity writes them.
        /// </summary>
        internal static readonly string[] BuiltinFiles = { "Resources/unity default resources", "Resources/unity_builtin_extra" };

        /// <summary>Classes only scene files hold (OcclusionCullingSettings, RenderSettings, LightmapSettings, NavMeshSettings).</summary>
        static readonly HashSet<int> sceneClasses = new HashSet<int> { 29, 104, 157, 196 };

        internal enum ExternalKind : byte
        {
            /// <summary>A serialized file of this bundle (archive:/mount/node).</summary>
            Own,
            /// <summary>One of Unity's built-in resource files (<see cref="BuiltinFiles"/>).</summary>
            Builtin,
        }

        internal readonly string Name;
        internal readonly long Start;
        internal readonly long Length;
        internal uint Version;
        internal AssetsFileMetadata Metadata;
        internal readonly Dictionary<long, AssetFileInfo> Objects = new Dictionary<long, AssetFileInfo>();
        internal bool IsScene;
        internal bool HasAssetBundleObject;
        /// <summary>Per external index (0-based): the bundle file it names, or null for a built-in file.</summary>
        internal BundleSerializedFile[] ExternalFiles;
        internal ExternalKind[] ExternalKinds;
        internal string[] ExternalNames;

        readonly BundleArchive archive;
        readonly BundleTypeTree.Node[] compiled;

        BundleSerializedFile(BundleArchive archive, string name, long start, long length, int typeCount)
        {
            this.archive = archive;
            Name = name;
            Start = start;
            Length = length;
            compiled = new BundleTypeTree.Node[typeCount];
        }

        static BundleRefusedException Bad(string file, string why) =>
            new BundleRefusedException(BundleRule.Malformed, "file " + BundleNames.Clean(file, 60) + ": " + why);

        /// <summary>True when the bytes at the start of a file look like a serialized-file header (used for unflagged files).</summary>
        internal static bool LooksSerialized(BundleArchive archive, BundleArchive.Node node)
        {
            if (node.Size < 48) return false;
            var head = new byte[48];
            archive.Read(node.Offset, head, 0, head.Length);
            var version = ReadUInt32BE(head, 8);
            if (version < 9 || version > 64) return false;
            if (version >= 22) return ReadInt64BE(head, 32) <= node.Size && ReadInt64BE(head, 24) <= node.Size;
            return ReadUInt32BE(head, 4) <= node.Size && ReadUInt32BE(head, 0) <= node.Size;
        }

        static uint ReadUInt32BE(byte[] b, int at) => (uint)((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]);

        static long ReadInt64BE(byte[] b, int at) => ((long)ReadUInt32BE(b, at) << 32) | ReadUInt32BE(b, at + 4);

        internal static BundleSerializedFile Load(BundleArchive archive, BundleArchive.Node node, BundleCheckOptions options)
        {
            var name = node.Name;
            if (node.Size < 48) throw Bad(name, "too short");
            var head = new byte[48];
            archive.Read(node.Offset, head, 0, head.Length);
            var header = new AssetsFileHeader();
            try
            {
                header.Read(new AssetsFileReader(new MemoryStream(head, false)));
            }
            catch (Exception e)
            {
                throw Bad(name, "header (" + e.GetType().Name + ")");
            }
            if (Array.IndexOf(SupportedFormats, header.Version) < 0)
                throw new BundleRefusedException(BundleRule.Unsupported, "file " + BundleNames.Clean(name, 60) + ": serialized format " + header.Version + " (the checker reads format 22)");
            if (header.Endianness) throw new BundleRefusedException(BundleRule.Unsupported, "big-endian serialized file");
            var headerSize = header.Version >= 22 ? 48 : 20;
            if (header.MetadataSize <= 0 || header.MetadataSize > options.MaxMetadataBytes || headerSize + header.MetadataSize > node.Size)
                throw Bad(name, "metadata size out of range");
            if (header.FileSize <= 0 || header.FileSize > node.Size) throw Bad(name, "file size out of range");
            if (header.DataOffset < headerSize + header.MetadataSize || header.DataOffset > header.FileSize) throw Bad(name, "data offset out of range");

            var metaEnd = (int)(headerSize + header.MetadataSize);
            var meta = new byte[metaEnd];
            archive.Read(node.Offset, meta, 0, metaEnd);
            // AssetsTools sizes its lists from these counts: walk the same layout with bounds first.
            try
            {
                new MetadataBounds(meta, headerSize, metaEnd, header.Version).Check();
            }
            catch (BundleRefusedException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw Bad(name, "metadata (" + e.GetType().Name + ")");
            }
            var metadata = new AssetsFileMetadata();
            var reader = new AssetsFileReader(new MemoryStream(meta, false)) { BigEndian = false, Position = headerSize };
            try
            {
                metadata.Read(reader, header.Version);
            }
            catch (Exception e)
            {
                throw Bad(name, "metadata (" + e.GetType().Name + ")");
            }
            if (reader.Position > metaEnd) throw Bad(name, "metadata overruns");
            if (!metadata.TypeTreeEnabled) throw new BundleRefusedException(BundleRule.NoTypeTree, "file " + BundleNames.Clean(name, 60) + " has no type trees");
            var file = new BundleSerializedFile(archive, name, node.Offset, Math.Min(node.Size, header.FileSize), metadata.TypeTreeTypes.Count)
            {
                Version = header.Version,
                Metadata = metadata,
            };
            foreach (var info in metadata.AssetInfos)
            {
                if (info.ByteOffset < 0 || info.ByteSize > int.MaxValue) throw Bad(name, "object out of range");
                var at = header.DataOffset + info.ByteOffset;
                if (at < header.DataOffset || at > file.Length - info.ByteSize) throw Bad(name, "object outside the file");
                if ((at & 3) != 0) throw Bad(name, "unaligned object");
                if (info.TypeIdOrIndex < 0 || info.TypeIdOrIndex >= metadata.TypeTreeTypes.Count) throw Bad(name, "object type index out of range");
                if (file.Objects.ContainsKey(info.PathId)) throw Bad(name, "duplicate object id");
                file.Objects.Add(info.PathId, info);
                var classId = metadata.TypeTreeTypes[info.TypeIdOrIndex].TypeId;
                if (sceneClasses.Contains(classId)) file.IsScene = true;
                if (classId == ClassAssetBundle) file.HasAssetBundleObject = true;
            }
            file.dataOffset = header.DataOffset;
            return file;
        }

        long dataOffset;

        /// <summary>The object's class id (114 MonoBehaviour, 1 GameObject...): what Unity instantiates, whatever its tree says.</summary>
        internal int ClassOf(AssetFileInfo info) => Metadata.TypeTreeTypes[info.TypeIdOrIndex].TypeId;

        /// <summary>Unity's name for a class id ("GameObject", "AudioSource"), or null when it is not a known class.</summary>
        internal static string ClassName(int classId)
        {
            if (classId <= 0 || !Enum.IsDefined(typeof(AssetClassID), classId)) return null;
            return ((AssetClassID)classId).ToString();
        }

        /// <summary>
        /// The object's native class name, taken from its class id (never from its type tree's root, which a bundle can name
        /// anything). A tree whose root names another class is refused: the checker would read one class's layout as another.
        /// </summary>
        internal string NativeNameOf(AssetFileInfo info)
        {
            var classId = ClassOf(info);
            var name = ClassName(classId);
            if (name == null) return null;
            var root = Tree(info.TypeIdOrIndex).Type;
            if (!string.Equals(root, name, StringComparison.Ordinal))
                throw new BundleRefusedException(BundleRule.TypeTreeMismatch, "file " + BundleNames.Clean(Name, 60) + ": class " + classId + " (" + name
                                                                              + ") stored with a type tree named " + BundleNames.Clean(root, 40));
            return name;
        }

        /// <summary>The script a MonoBehaviour type entry says it belongs to (metadata ScriptTypeIndex), or null for none.</summary>
        internal bool TryGetTypeScript(AssetFileInfo info, out BundlePPtr script) => TryGetTypeScript(info.TypeIdOrIndex, out script);

        /// <summary>The script a type entry says it belongs to, by type index.</summary>
        internal bool TryGetTypeScript(int typeIndex, out BundlePPtr script)
        {
            script = default;
            var type = Metadata.TypeTreeTypes[typeIndex];
            if (type.ScriptTypeIndex == 0xffff) return false;
            var scripts = Metadata.ScriptTypes;
            if (scripts == null || type.ScriptTypeIndex >= scripts.Count)
                throw new BundleRefusedException(BundleRule.TypeTreeMismatch, "file " + BundleNames.Clean(Name, 60) + ": script type index out of range");
            var entry = scripts[type.ScriptTypeIndex];
            script = new BundlePPtr { FileId = entry.FileId, PathId = entry.PathId };
            return true;
        }

        /// <summary>The compiled, validated type tree for a type index (cached).</summary>
        internal BundleTypeTree.Node Tree(int typeIndex)
        {
            var node = compiled[typeIndex];
            if (node != null) return node;
            var type = Metadata.TypeTreeTypes[typeIndex];
            var blob = type.TypeBlob;
            if (blob == null || !type.TypeBlobIsDefinition || blob.Nodes == null || blob.Nodes.Count == 0)
                throw new BundleRefusedException(BundleRule.NoTypeTree, "file " + BundleNames.Clean(Name, 60) + ": class " + type.TypeId + " has no type tree");
            // The native classes whose data the checker judges (MonoScript identities, AnimationClip events) must hash to
            // their stored value. Script types store the MonoScript's properties hash, and some native classes
            // (AudioMixerController) hash differently, so the rest are taken as they are (see BundleTypeTree.CheckHash).
            if (type.TypeId == ClassMonoScript || type.TypeId == ClassAnimationClip) BundleTypeTree.CheckHash(blob, type.TypeHash, "class " + type.TypeId);
            return compiled[typeIndex] = BundleTypeTree.Compile(blob, "class " + type.TypeId);
        }

        Dictionary<int, byte[]> digests;

        /// <summary>A type entry's stored type hash (for a script type, the MonoScript's properties hash).</summary>
        internal byte[] StoredHash(int typeIndex) => Metadata.TypeTreeTypes[typeIndex].TypeHash.data ?? new byte[16];

        /// <summary>Whether a type entry stores a type tree.</summary>
        internal bool HasTree(int typeIndex)
        {
            var type = Metadata.TypeTreeTypes[typeIndex];
            var blob = type.TypeBlob;
            return blob != null && type.TypeBlobIsDefinition && blob.Nodes != null && blob.Nodes.Count > 0;
        }

        /// <summary>The checker's digest of a type entry's stored tree (<see cref="BundleScriptLayouts.Digest"/>; cached).</summary>
        internal byte[] LayoutDigest(int typeIndex)
        {
            digests = digests ?? new Dictionary<int, byte[]>();
            if (digests.TryGetValue(typeIndex, out var digest)) return digest;
            if (!HasTree(typeIndex))
                throw new BundleRefusedException(BundleRule.NoTypeTree, "file " + BundleNames.Clean(Name, 60) + ": class " + Metadata.TypeTreeTypes[typeIndex].TypeId + " has no type tree");
            return digests[typeIndex] = BundleTypeTree.LayoutDigest(Metadata.TypeTreeTypes[typeIndex].TypeBlob);
        }

        HashSet<int> hideFlagTypes;

        /// <summary>
        /// The type indexes whose tree has a top-level m_ObjectHideFlags field (in any case). Unity's built content never
        /// stores it (it is editor data), so this is normally empty and costs one pass over the raw node names.
        /// </summary>
        internal HashSet<int> TypesWithHideFlags()
        {
            if (hideFlagTypes != null) return hideFlagTypes;
            var set = new HashSet<int>();
            for (var i = 0; i < Metadata.TypeTreeTypes.Count; i++)
            {
                var blob = Metadata.TypeTreeTypes[i].TypeBlob;
                if (blob?.Nodes == null) continue;
                var strings = blob.StringBufferBytes ?? Array.Empty<byte>();
                foreach (var node in blob.Nodes)
                {
                    if (node.Level != 1) continue;
                    if (string.Equals(node.GetNameString(strings), "m_ObjectHideFlags", StringComparison.OrdinalIgnoreCase))
                    {
                        set.Add(i);
                        break;
                    }
                }
            }
            return hideFlagTypes = set;
        }

        readonly Dictionary<string, BundleTypeTree.Node> refTrees = new Dictionary<string, BundleTypeTree.Node>(StringComparer.Ordinal);
        Dictionary<string, byte[]> refDigests;

        /// <summary>
        /// The ref type entry stored for a [SerializeReference] class, or null. Two entries for one class are refused
        /// (Unity writes one per class; which one it would read is unknown).
        /// </summary>
        TypeTreeType RefTypeOf(string className, string ns, string asm)
        {
            TypeTreeType found = null;
            if (Metadata.RefTypes == null) return null;
            foreach (var refType in Metadata.RefTypes)
            {
                var reference = refType.TypeReference;
                if (reference == null || reference.ClassName != className || reference.Namespace != ns || reference.AsmName != asm) continue;
                if (found != null)
                    throw new BundleRefusedException(BundleRule.TypeTreeShape, "file " + BundleNames.Clean(Name, 60) + ": two ref types for one [SerializeReference] class");
                found = refType;
            }
            return found;
        }

        /// <summary>The type tree of a [SerializeReference] type stored in this file's ref types, or null.</summary>
        internal BundleTypeTree.Node RefTree(string className, string ns, string asm)
        {
            var key = asm + "|" + ns + "|" + className;
            if (refTrees.TryGetValue(key, out var found)) return found;
            BundleTypeTree.Node tree = null;
            var refType = RefTypeOf(className, ns, asm);
            var blob = refType?.TypeBlob;
            if (blob != null && refType.TypeBlobIsDefinition && blob.Nodes != null && blob.Nodes.Count > 0) tree = BundleTypeTree.Compile(blob, "reference type");
            // Keys come from the bundle, but only ref types this file stores can be cached here (one entry per lookup
            // that found nothing at most, bounded by the references read).
            if (refTrees.Count < 4096) refTrees[key] = tree;
            return tree;
        }

        /// <summary>The digest of a [SerializeReference] class's stored tree (call after <see cref="RefTree"/> found one).</summary>
        internal byte[] RefLayoutDigest(string className, string ns, string asm)
        {
            var key = asm + "|" + ns + "|" + className;
            refDigests = refDigests ?? new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (refDigests.TryGetValue(key, out var digest)) return digest;
            var blob = RefTypeOf(className, ns, asm)?.TypeBlob;
            if (blob?.Nodes == null || blob.Nodes.Count == 0) throw new BundleRefusedException(BundleRule.NoTypeTree, "a [SerializeReference] value without a type tree");
            digest = BundleTypeTree.LayoutDigest(blob);
            if (refDigests.Count < 4096) refDigests[key] = digest;
            return digest;
        }

        /// <summary>Every ref type with a tree: its identity, stored hash and digest (the layout generator's input).</summary>
        internal IEnumerable<(string className, string ns, string asm, byte[] hash, byte[] digest)> RefTypeLayouts()
        {
            if (Metadata.RefTypes == null) yield break;
            foreach (var refType in Metadata.RefTypes)
            {
                var reference = refType.TypeReference;
                var blob = refType.TypeBlob;
                if (reference == null || blob?.Nodes == null || blob.Nodes.Count == 0 || !refType.TypeBlobIsDefinition) continue;
                yield return (reference.ClassName, reference.Namespace, reference.AsmName, refType.TypeHash.data ?? new byte[16], BundleTypeTree.LayoutDigest(blob));
            }
        }

        /// <summary>A reader limited to one object's bytes, positioned at its start.</summary>
        internal BundleObjectReader Open(AssetFileInfo info, BundleObjectReader reuse)
        {
            var start = Start + dataOffset + info.ByteOffset;
            var reader = reuse ?? new BundleObjectReader(archive);
            reader.Reset(start, start + info.ByteSize, Start);
            return reader;
        }

        /// <summary>
        /// Resolves a PPtr's file: this file, another file of the bundle, or null with <paramref name="outside"/> set (a
        /// built-in file, or an index with no external entry).
        /// </summary>
        internal BundleSerializedFile FileOf(BundlePPtr pointer, out bool outside)
        {
            outside = false;
            if (pointer.FileId == 0) return this;
            var index = pointer.FileId - 1;
            if (ExternalFiles == null || index < 0 || index >= ExternalFiles.Length)
            {
                outside = true;
                return null;
            }
            var file = ExternalFiles[index];
            outside = file == null;
            return file;
        }

        /// <summary>Whether a PPtr names one of Unity's built-in resource files.</summary>
        internal bool IsBuiltin(BundlePPtr pointer)
        {
            var index = pointer.FileId - 1;
            return ExternalKinds != null && index >= 0 && index < ExternalKinds.Length && ExternalKinds[index] == ExternalKind.Builtin;
        }

        internal string ExternalName(int fileId) =>
            ExternalNames != null && fileId > 0 && fileId <= ExternalNames.Length ? ExternalNames[fileId - 1] : "?";

        /// <summary>
        /// Maps each external path to a file of this bundle or a built-in file; anything else is refused. A file of this
        /// bundle is named exactly "archive:/{mount}/{node}" (Unity mounts a bundle's files under one directory, the name
        /// every other node extends: "BuildPlayer-X" for BuildPlayer-X and BuildPlayer-X.sharedAssets, "CAB-h" for a kit).
        /// The directory is not ignored: "archive:/CAB-other/{our node}" is another bundle's file.
        /// </summary>
        internal void LinkExternals(Dictionary<string, BundleSerializedFile> byName, string mount)
        {
            var externals = Metadata.Externals ?? new List<AssetsFileExternal>();
            ExternalFiles = new BundleSerializedFile[externals.Count];
            ExternalKinds = new ExternalKind[externals.Count];
            ExternalNames = new string[externals.Count];
            var prefix = mount == null ? null : "archive:/" + mount + "/";
            for (var i = 0; i < externals.Count; i++)
            {
                var path = externals[i].PathName ?? "";
                ExternalNames[i] = path;
                if (Array.IndexOf(BuiltinFiles, path) >= 0)
                {
                    ExternalKinds[i] = ExternalKind.Builtin;
                    continue;
                }
                if (prefix != null && path.StartsWith(prefix, StringComparison.Ordinal) && byName.TryGetValue(path.Substring(prefix.Length), out var file))
                {
                    ExternalFiles[i] = file;
                    ExternalKinds[i] = ExternalKind.Own;
                    continue;
                }
                throw new BundleRefusedException(BundleRule.ExternalFile, "file " + BundleNames.Clean(Name, 60) + " references " + BundleNames.Clean(path, 100)
                                                                         + ", which is neither a serialized file of this bundle nor a built-in resource file");
            }
        }

        static readonly System.Text.RegularExpressions.Regex cabName =
            new System.Text.RegularExpressions.Regex("^CAB-[0-9a-f]{16,64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        static readonly System.Text.RegularExpressions.Regex sceneName =
            new System.Text.RegularExpressions.Regex("^BuildPlayer-[^/\\\\\\x00-\\x1f]{1,200}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        static readonly System.Text.RegularExpressions.Regex giName =
            new System.Text.RegularExpressions.Regex("^[0-9a-f]{2}/[0-9a-f]{1,64}(\\.[A-Za-z]{1,8}){0,2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// The directory Unity mounts the bundle's files under, from the only node layouts Unity writes (the whole live
        /// corpus has exactly these):
        /// <list type="bullet">
        /// <item>an asset bundle: "CAB-{hex}" (serialized), with "CAB-{hex}.resS" / ".resource" payloads;</item>
        /// <item>a scene bundle: "BuildPlayer-{scene}" and "BuildPlayer-{scene}.sharedAssets" (serialized), with ".resS" /
        /// ".resource" payloads and "GI/BuildPlayer-{scene}/{hh}/{hex}.{ext}" lighting data.</item>
        /// </list>
        /// The mount is the CAB or BuildPlayer name. Anything else returns null with <paramref name="problem"/> set: the
        /// caller refuses the bundle, and no archive reference is then judged as this bundle's own.
        /// </summary>
        internal static string MountOf(IList<BundleArchive.Node> nodes, out string problem)
        {
            problem = null;
            string mount = null;
            foreach (var node in nodes)
            {
                if (!node.IsSerializedFlag || node.Name.EndsWith(".sharedAssets", StringComparison.Ordinal)) continue;
                if (mount != null)
                {
                    problem = "two serialized files that are not one bundle's (" + BundleNames.Clean(mount, 40) + ", " + BundleNames.Clean(node.Name, 40) + ")";
                    return null;
                }
                mount = node.Name;
            }
            if (mount == null)
            {
                problem = "no CAB or BuildPlayer serialized file";
                return null;
            }
            var isScene = sceneName.IsMatch(mount);
            if (!isScene && !cabName.IsMatch(mount))
            {
                problem = "serialized file " + BundleNames.Clean(mount, 60) + " is not named like a CAB or a BuildPlayer scene";
                return null;
            }
            foreach (var node in nodes)
            {
                var name = node.Name;
                if (name == mount) continue;
                bool fits;
                if (node.IsSerializedFlag) fits = isScene && name == mount + ".sharedAssets";
                else if (name.StartsWith("GI/", StringComparison.Ordinal))
                    fits = isScene && name.StartsWith("GI/" + mount + "/", StringComparison.Ordinal) && giName.IsMatch(name.Substring(4 + mount.Length));
                else
                    fits = name == mount + ".resS" || name == mount + ".resource"
                           || (isScene && (name == mount + ".sharedAssets.resS" || name == mount + ".sharedAssets.resource"));
                if (fits) continue;
                problem = "file " + BundleNames.Clean(name, 60) + " is not part of a layout Unity writes";
                return null;
            }
            return mount;
        }

        /// <summary>Bounds-walk of the metadata layout (formats 17-23) before AssetsTools allocates from its counts.</summary>
        sealed class MetadataBounds
        {
            readonly byte[] b;
            readonly int end;
            readonly uint version;
            int p;
            bool typeTrees;

            internal MetadataBounds(byte[] buffer, int start, int end, uint version)
            {
                b = buffer;
                p = start;
                this.end = end;
                this.version = version;
            }

            int Remaining => end - p;

            void Need(int n)
            {
                if (n < 0 || n > Remaining) throw new BundleRefusedException(BundleRule.Malformed, "metadata truncated");
            }

            void Skip(int n)
            {
                Need(n);
                p += n;
            }

            int I32()
            {
                Need(4);
                var v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
                p += 4;
                return v;
            }

            int U16()
            {
                Need(2);
                var v = b[p] | (b[p + 1] << 8);
                p += 2;
                return v;
            }

            byte U8()
            {
                Need(1);
                return b[p++];
            }

            void CString(int max)
            {
                var stop = Math.Min(end, p + max + 1);
                for (var i = p; i < stop; i++)
                {
                    if (b[i] != 0) continue;
                    p = i + 1;
                    return;
                }
                throw new BundleRefusedException(BundleRule.Malformed, "metadata string too long");
            }

            void Align() => p = (p + 3) & ~3;

            int Count(int minEach)
            {
                var n = I32();
                if (n < 0 || (minEach > 0 && n > Remaining / minEach)) throw new BundleRefusedException(BundleRule.Malformed, "metadata count out of range");
                return n;
            }

            internal void Check()
            {
                CString(256);
                Skip(4);
                typeTrees = U8() != 0;
                var types = Count(23);
                for (var i = 0; i < types; i++) Type(false);
                var objects = Count(20);
                for (var i = 0; i < objects; i++)
                {
                    Align();
                    Skip(8 + (version >= 22 ? 8 : 4) + 4 + 4);
                }
                var scripts = Count(12);
                for (var i = 0; i < scripts; i++)
                {
                    Skip(4);
                    Align();
                    Skip(8);
                }
                var externals = Count(22);
                for (var i = 0; i < externals; i++)
                {
                    CString(1024);
                    Skip(16 + 4);
                    CString(1024);
                }
                if (version >= 20)
                {
                    var refs = Count(23);
                    for (var i = 0; i < refs; i++) Type(true);
                }
                CString(4096);
            }

            void Type(bool isRef)
            {
                var classId = I32();
                Skip(1);
                var scriptIndex = U16();
                if (classId == 114 || (isRef && scriptIndex != 0xffff)) Skip(16);
                Skip(16);
                if (!typeTrees) return;
                var inline = true;
                if (version >= 23)
                {
                    Skip(16);
                    inline = I32() != 0;
                }
                if (inline)
                {
                    if (version >= 23) Skip(8);
                    var nodes = Count(version >= 18 ? 32 : 24);
                    var strings = I32();
                    if (strings < 0) throw new BundleRefusedException(BundleRule.Malformed, "metadata string table size");
                    Skip(nodes * (version >= 18 ? 32 : 24));
                    Skip(strings);
                }
                if (version < 21) return;
                if (!isRef)
                {
                    var dependencies = Count(4);
                    Skip(dependencies * 4);
                }
                else
                {
                    CString(1024);
                    CString(1024);
                    CString(1024);
                }
            }
        }
    }

    /// <summary>Little-endian reads over one object's bytes in the bundle, buffered, with hard bounds.</summary>
    internal sealed class BundleObjectReader
    {
        const int Window = 64 * 1024;

        readonly BundleArchive archive;
        readonly byte[] buffer = new byte[Window];
        long bufferStart = -1;
        int bufferLength;
        long position, end, fileStart;

        internal BundleObjectReader(BundleArchive archive)
        {
            this.archive = archive;
        }

        internal void Reset(long start, long limit, long serializedFileStart)
        {
            position = start;
            end = limit;
            fileStart = serializedFileStart;
        }

        internal long Position => position;
        internal long Remaining => end - position;

        static BundleRefusedException Overrun() => new BundleRefusedException(BundleRule.Malformed, "object data overruns its size");

        internal void Skip(long count)
        {
            if (count < 0 || count > end - position) throw Overrun();
            position += count;
        }

        /// <summary>Aligns to 4 bytes relative to the serialized file's start, as Unity does.</summary>
        internal void Align()
        {
            var relative = position - fileStart;
            var pad = (4 - (relative & 3)) & 3;
            if (pad > end - position) throw Overrun();
            position += pad;
        }

        void Ensure(int count)
        {
            if (count > end - position) throw Overrun();
            if (bufferStart >= 0 && position >= bufferStart && position + count <= bufferStart + bufferLength) return;
            var take = (int)Math.Min(Window, end - position);
            if (take < count) throw Overrun();
            archive.Read(position, buffer, 0, take);
            bufferStart = position;
            bufferLength = take;
        }

        internal byte ReadByte()
        {
            Ensure(1);
            var v = buffer[position - bufferStart];
            position++;
            return v;
        }

        internal int ReadInt32()
        {
            Ensure(4);
            var i = (int)(position - bufferStart);
            position += 4;
            return buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16) | (buffer[i + 3] << 24);
        }

        internal long ReadInt64()
        {
            Ensure(8);
            var i = (int)(position - bufferStart);
            position += 8;
            var lo = (uint)(buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16) | (buffer[i + 3] << 24));
            var hi = (uint)(buffer[i + 4] | (buffer[i + 5] << 8) | (buffer[i + 6] << 16) | (buffer[i + 7] << 24));
            return (long)(((ulong)hi << 32) | lo);
        }

        internal long ReadSigned(int size)
        {
            switch (size)
            {
                case 1: return (sbyte)ReadByte();
                case 2:
                    {
                        var lo = ReadByte();
                        return (short)(lo | (ReadByte() << 8));
                    }
                case 4: return ReadInt32();
                case 8: return ReadInt64();
                default: throw Overrun();
            }
        }

        internal byte[] ReadBytes(int count)
        {
            if (count < 0 || count > end - position) throw Overrun();
            var data = new byte[count];
            var done = 0;
            while (done < count)
            {
                var take = Math.Min(Window, count - done);
                if (take >= Window && done == 0 && count > Window)
                {
                    archive.Read(position, data, 0, count);
                    position += count;
                    return data;
                }
                Ensure(take);
                Buffer.BlockCopy(buffer, (int)(position - bufferStart), data, done, take);
                position += take;
                done += take;
            }
            return data;
        }
    }

    /// <summary>
    /// Type trees compiled for walking: validated shapes (primitive sizes, arrays, strings), fixed-size subtrees for
    /// fast skipping, and marks on the nodes the checker must read (UnityEvent calls, by-name callbacks, graph JSON,
    /// AnimationEvents, managed references).
    /// </summary>
    /// <remarks>
    /// <para><b>Strict and canonical.</b> Unity reads a stored tree that differs from its own layout by field NAME: it
    /// skips fields it does not know and converts some primitives. So the checker refuses every tree it could read
    /// differently from Unity, instead of skipping: two sibling fields whose names match even case-insensitively; a PPtr
    /// that is not exactly {int m_FileID, SInt64 m_PathID}; a string that is not exactly {Array {int size, char data}};
    /// a UnityEvent call group, call or argument cache that is not exactly Unity's (field names, order and primitive
    /// types: a float m_Mode is refused, not read as raw bits); a field named like a judged string (MethodName, _json,
    /// functionName) that is not a string.</para>
    /// <para><b>Roles never shadow.</b> A struct can carry several roles (a by-name callback, graph data, an
    /// AnimationEvent: one per judged string child), and the walk still descends into every other child of it, so an
    /// extra string next to m_PersistentCalls cannot hide the calls.</para>
    /// </remarks>
    internal static class BundleTypeTree
    {
        internal enum Kind : byte
        {
            Struct, Primitive, String, Array, Bytes, ManagedReferences,
        }

        [Flags]
        internal enum Role : byte
        {
            None = 0,
            /// <summary>A UnityEvent's m_PersistentCalls (canonical PersistentCallGroup).</summary>
            PersistentCalls = 1,
            /// <summary>A struct with a string MethodName (I2 EventCallback and look-alikes).</summary>
            NamedCallback = 2,
            /// <summary>A struct with a string _json (Visual Scripting serialization data).</summary>
            GraphData = 4,
            /// <summary>A struct with a string functionName (AnimationEvent).</summary>
            AnimationEvent = 8,
        }

        /// <summary>The string children that give a struct a role, by exact name (Unity matches field names exactly).</summary>
        internal const string MethodNameField = "MethodName", JsonField = "_json", FunctionNameField = "functionName";

        internal sealed class Node
        {
            internal string Name;
            internal string Type;
            internal Kind Kind;
            internal int Size;      // primitive size
            internal bool Align;    // align to 4 after this node
            internal Node[] Children = Array.Empty<Node>();
            internal Node Element;  // arrays/strings
            internal int Fixed = -1;
            /// <summary>Fewest bytes a value can take (bounds element counts before iterating).</summary>
            internal int Min;
            internal bool Interesting;
            internal Role Roles;
            /// <summary>Exactly {int m_FileID, SInt64 m_PathID} under a PPtr&lt;...&gt; type.</summary>
            internal bool IsPPtr;
            /// <summary>
            /// The judged name this field's name equals only case-insensitively ("m_persistentCalls"), or null. Unity may or
            /// may not match it; the walk records it and the checker refuses it where the data is live.
            /// </summary>
            internal string Variant;

            /// <summary>The first child with this exact name, or null (names are unique, even case-insensitively).</summary>
            internal Node Child(string name)
            {
                foreach (var child in Children)
                {
                    if (child.Name == name) return child;
                }
                return null;
            }
        }

        static readonly Dictionary<string, int> primitiveSizes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "bool", 1 }, { "char", 1 }, { "SInt8", 1 }, { "UInt8", 1 }, { "unsigned char", 1 },
            { "SInt16", 2 }, { "UInt16", 2 }, { "short", 2 }, { "unsigned short", 2 },
            { "SInt32", 4 }, { "UInt32", 4 }, { "int", 4 }, { "unsigned int", 4 }, { "Type*", 4 }, { "float", 4 },
            { "SInt64", 8 }, { "UInt64", 8 }, { "long", 8 }, { "long long", 8 }, { "unsigned long long", 8 }, { "FileSize", 8 },
            { "double", 8 },
        };

        static BundleRefusedException Mismatch(string where, string why) =>
            new BundleRefusedException(BundleRule.TypeTreeMismatch, where + ": " + why);

        /// <summary>A shape Unity's own reader can desynchronise on (structural: see <see cref="BundleRule.TypeTreeShape"/>).</summary>
        static BundleRefusedException Shape(string where, string why) =>
            new BundleRefusedException(BundleRule.TypeTreeShape, where + ": " + why);

        /// <summary>
        /// A native class's stored type hash is MD4 over each node's type, name, size, flags, version and alignment (true
        /// for MonoScript and AnimationClip in the whole live corpus; not for every class: AudioMixerController differs).
        /// A tree that does not hash to its stored value could be read by Unity with its own layout while the checker
        /// reads another, so it is refused. A tree that does hash to its value but is not Unity's own is read by Unity by
        /// name, like any other: the canonical-shape rules in <see cref="Compile"/> cover that. Script types
        /// (MonoBehaviour, [SerializeReference]) store the script's properties hash instead, which only the editor can
        /// compute: their trees are compared with the app's own through <see cref="BundleScriptLayouts"/> (the table the
        /// editor generates at build time) whenever the stored hash would make Unity read with the app's layout.
        /// </summary>
        internal static void CheckHash(TypeTreeBlob blob, Hash128 stored, string where)
        {
            if (stored.data == null || stored.IsZero()) return; // nothing to compare: Unity has no hash to match either
            var md4 = new MD4();
            var strings = blob.StringBufferBytes ?? Array.Empty<byte>();
            foreach (var node in blob.Nodes)
            {
                md4.Update(Encoding.UTF8.GetBytes(node.GetTypeString(strings)));
                md4.Update(Encoding.UTF8.GetBytes(node.GetNameString(strings)));
                md4.Update(BitConverter.GetBytes(node.ByteSize));
                md4.Update(BitConverter.GetBytes((int)node.TypeFlags));
                md4.Update(BitConverter.GetBytes((int)node.Version));
                md4.Update(BitConverter.GetBytes((int)(node.MetaFlags & 0x4000)));
            }
            var digest = md4.Digest();
            for (var i = 0; i < 16; i++)
            {
                if (digest[i] != stored.data[i]) throw Mismatch(where, "type tree does not match its hash");
            }
        }

        /// <summary>The checker's digest of a stored tree (see <see cref="BundleScriptLayouts.Digest"/>).</summary>
        internal static byte[] LayoutDigest(TypeTreeBlob blob)
        {
            var strings = blob.StringBufferBytes ?? Array.Empty<byte>();
            var nodes = new List<BundleScriptLayouts.LayoutNode>(blob.Nodes.Count);
            foreach (var node in blob.Nodes)
            {
                nodes.Add(new BundleScriptLayouts.LayoutNode
                {
                    Level = node.Level, TypeFlags = (int)node.TypeFlags, Version = node.Version, Size = node.ByteSize,
                    Align = (node.MetaFlags & 0x4000) != 0, Type = node.GetTypeString(strings), Name = node.GetNameString(strings),
                });
            }
            BundleCheckBudget.ChargeCurrent((long)nodes.Count * 64);
            return BundleScriptLayouts.Digest(nodes);
        }

        internal static Node Compile(TypeTreeBlob blob, string where)
        {
            var nodes = blob.Nodes;
            var strings = blob.StringBufferBytes ?? Array.Empty<byte>();
            if (nodes[0].Level != 0) throw Shape(where, "root level");
            BundleCheckBudget.ChargeCurrent((long)nodes.Count * 32);
            var index = 0;
            Node root;
            try
            {
                root = Build(nodes, strings, ref index, where);
            }
            catch (BundleRefusedException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw Shape(where, "unreadable type tree (" + e.GetType().Name + ")");
            }
            if (index != nodes.Count - 1) throw Shape(where, "trailing nodes");
            return root;
        }

        static Node Build(List<TypeTreeNode> nodes, byte[] strings, ref int index, string where)
        {
            var source = nodes[index];
            var node = new Node
            {
                Name = source.GetNameString(strings),
                Type = source.GetTypeString(strings),
                Align = (source.MetaFlags & 0x4000) != 0,
            };
            var children = new List<Node>();
            while (index + 1 < nodes.Count && nodes[index + 1].Level > source.Level)
            {
                if (nodes[index + 1].Level != source.Level + 1) throw Shape(where, "level skips");
                index++;
                children.Add(Build(nodes, strings, ref index, where));
            }
            node.Children = children.ToArray();
            var isArray = ((int)source.TypeFlags & 1) != 0;

            // Unity matches fields by name: two siblings it could take for one another are ambiguous (which one wins is
            // Unity's business, not something to guess).
            if (node.Children.Length > 1)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var child in node.Children)
                {
                    if (!names.Add(child.Name ?? "")) throw Shape(where, "duplicate field " + BundleNames.Clean(child.Name, 40) + " in " + BundleNames.Clean(node.Name, 40));
                }
            }

            if (isArray)
            {
                if (node.Children.Length != 2 || node.Children[0].Kind != Kind.Primitive || node.Children[0].Size != 4)
                    throw Shape(where, "array shape");
                node.Element = node.Children[1];
                node.Kind = node.Element.Kind == Kind.Primitive && node.Element.Size == 1 && !node.Element.Align ? Kind.Bytes : Kind.Array;
            }
            else if (node.Type == "string")
            {
                if (IsCanonicalString(node))
                {
                    node.Kind = Kind.String;
                    node.Align |= node.Children[0].Align;
                }
                else if (node.Children.Length == 1 && node.Children[0].Name == "id" && node.Children[0].Kind == Kind.String)
                {
                    node.Kind = Kind.Struct; // PropertyName: Unity names its type "string" and stores one string, "id"
                }
                else
                {
                    throw Shape(where, "string " + BundleNames.Clean(node.Name, 40) + " is not {Array {int size, char data}}");
                }
            }
            else if (node.Type == "ManagedReferencesRegistry")
            {
                node.Kind = Kind.ManagedReferences;
            }
            else if (string.Equals(node.Type, "ManagedReferencesRegistry", StringComparison.OrdinalIgnoreCase))
            {
                throw Mismatch(where, "type " + BundleNames.Clean(node.Type, 40) + " differs from ManagedReferencesRegistry only by case");
            }
            else if (node.Children.Length == 0)
            {
                if (primitiveSizes.TryGetValue(node.Type, out var size))
                {
                    if (source.ByteSize != size) throw Shape(where, "primitive " + BundleNames.Clean(node.Type, 30) + " of size " + source.ByteSize);
                    node.Kind = Kind.Primitive;
                    node.Size = size;
                }
                else if (source.ByteSize <= 0)
                {
                    node.Kind = Kind.Struct; // an empty class
                }
                else
                {
                    throw Shape(where, "unknown leaf type " + BundleNames.Clean(node.Type, 40));
                }
            }
            else
            {
                node.Kind = Kind.Struct;
            }

            if (node.Type != null && node.Type.StartsWith("PPtr<", StringComparison.Ordinal))
            {
                if (!IsCanonicalPPtr(node)) throw Shape(where, "PPtr " + BundleNames.Clean(node.Name, 40) + " is not {int m_FileID, SInt64 m_PathID}");
                node.IsPPtr = true;
            }

            // A field named like a string the checker judges must be that string, or Unity could read it some other way.
            if ((node.Name == MethodNameField || node.Name == JsonField || node.Name == FunctionNameField) && node.Kind != Kind.String)
                throw Shape(where, "field " + node.Name + " is not a string");

            // Fixed sizes (for skipping whole arrays) only where no alignment can depend on position.
            switch (node.Kind)
            {
                case Kind.Primitive:
                    node.Fixed = node.Align ? -1 : node.Size;
                    break;
                case Kind.Struct:
                    {
                        var total = 0;
                        foreach (var child in node.Children)
                        {
                            if (child.Fixed < 0)
                            {
                                total = -1;
                                break;
                            }
                            total += child.Fixed;
                        }
                        node.Fixed = node.Align ? -1 : total;
                        break;
                    }
                default:
                    node.Fixed = -1;
                    break;
            }
            if (node.Kind == Kind.Primitive) node.Min = node.Size;
            else if (node.Kind == Kind.Struct)
            {
                long min = 0;
                foreach (var child in node.Children) min += child.Min;
                node.Min = (int)Math.Min(min, int.MaxValue);
            }
            else node.Min = 4;

            // Roles and interest.
            node.Variant = VariantOf(node.Name, roleNames);
            if (node.Name == "m_PersistentCalls" || node.Name == "m_PersistentListeners")
            {
                if (!IsCanonicalCallGroup(node))
                    throw Shape(where, BundleNames.Clean(node.Name, 40) + " is not Unity's PersistentCallGroup {m_Calls [PersistentCall]}");
                node.Roles = Role.PersistentCalls;
                node.Interesting = true;
                return node;
            }
            switch (node.Kind)
            {
                case Kind.Struct:
                    if (HasString(node, MethodNameField)) node.Roles |= Role.NamedCallback;
                    if (HasString(node, JsonField)) node.Roles |= Role.GraphData;
                    if (HasString(node, FunctionNameField)) node.Roles |= Role.AnimationEvent;
                    node.Interesting = node.Roles != Role.None;
                    foreach (var child in node.Children) node.Interesting |= child.Interesting;
                    break;
                case Kind.Array:
                    node.Interesting = node.Element.Interesting;
                    break;
                case Kind.ManagedReferences:
                    node.Interesting = true;
                    break;
            }
            node.Interesting |= node.Variant != null;
            return node;
        }

        /// <summary>Names that give a field its role wherever it is (the call group, the judged strings).</summary>
        static readonly string[] roleNames = { "m_PersistentCalls", "m_PersistentListeners", MethodNameField, JsonField, FunctionNameField };

        /// <summary>The judged name <paramref name="name"/> equals case-insensitively but not exactly, or null.</summary>
        internal static string VariantOf(string name, string[] judged)
        {
            if (name == null) return null;
            foreach (var candidate in judged)
            {
                if (!string.Equals(name, candidate, StringComparison.Ordinal) && string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return null;
        }

        static bool HasString(Node node, string name)
        {
            foreach (var child in node.Children)
            {
                if (child.Kind == Kind.String && child.Name == name) return true;
            }
            return false;
        }

        static bool IsInt(Node node) => node.Kind == Kind.Primitive && node.Size == 4 && (node.Type == "int" || node.Type == "SInt32");

        static bool IsCanonicalString(Node node)
        {
            if (node.Children.Length != 1) return false;
            var array = node.Children[0];
            if (array.Name != "Array" || array.Kind != Kind.Bytes || array.Children.Length != 2) return false;
            var size = array.Children[0];
            var data = array.Children[1];
            return size.Name == "size" && IsInt(size) && data.Name == "data" && data.Type == "char" && data.Kind == Kind.Primitive && data.Size == 1;
        }

        static bool IsCanonicalPPtr(Node node)
        {
            if (node.Kind != Kind.Struct || node.Children.Length != 2) return false;
            var file = node.Children[0];
            var path = node.Children[1];
            return file.Name == "m_FileID" && IsInt(file) && !file.Align
                   && path.Name == "m_PathID" && path.Kind == Kind.Primitive && path.Size == 8 && path.Type == "SInt64" && !path.Align;
        }

        static bool Is(Node node, string name, Kind kind) => node.Name == name && node.Kind == kind;

        // Unity's PersistentCallGroup, PersistentCall and ArgumentCache. Unity itself writes trees that leave fields out
        // (live worlds have calls without m_Target and an empty PersistentCallGroup) and reads them by name, a missing
        // field keeping its default. So a field may be absent, in any order, but every field present must be one of
        // Unity's, with exactly Unity's type: nothing extra, nothing converted.

        /// <summary>Unity's PersistentCallGroup: {m_Calls: vector of PersistentCall}, or nothing.</summary>
        static bool IsCanonicalCallGroup(Node node)
        {
            if (node.Kind != Kind.Struct) return false;
            foreach (var calls in node.Children)
            {
                if (!Is(calls, "m_Calls", Kind.Struct) || calls.Children.Length != 1) return false;
                var array = calls.Children[0];
                if (!Is(array, "Array", Kind.Array) || array.Element.Name != "data") return false;
                if (!IsCanonicalCall(array.Element)) return false;
            }
            return true;
        }

        static bool IsCanonicalCall(Node call)
        {
            if (call.Kind != Kind.Struct) return false;
            foreach (var c in call.Children)
            {
                switch (c.Name)
                {
                    case "m_Target":
                        if (!c.IsPPtr) return false;
                        break;
                    case "m_TargetAssemblyTypeName":
                    case "m_MethodName":
                        if (c.Kind != Kind.String) return false;
                        break;
                    case "m_Mode":
                    case "m_CallState":
                        if (!IsInt(c)) return false;
                        break;
                    case "m_Arguments":
                        if (!IsCanonicalArguments(c)) return false;
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }

        static bool IsCanonicalArguments(Node args)
        {
            if (args.Kind != Kind.Struct) return false;
            foreach (var a in args.Children)
            {
                switch (a.Name)
                {
                    case "m_ObjectArgument":
                        if (!a.IsPPtr) return false;
                        break;
                    case "m_ObjectArgumentAssemblyTypeName":
                    case "m_StringArgument":
                        if (a.Kind != Kind.String) return false;
                        break;
                    case "m_IntArgument":
                        if (!IsInt(a)) return false;
                        break;
                    case "m_FloatArgument":
                        if (!(a.Kind == Kind.Primitive && a.Type == "float")) return false;
                        break;
                    case "m_BoolArgument":
                        if (!(a.Kind == Kind.Primitive && a.Size == 1 && (a.Type == "UInt8" || a.Type == "bool"))) return false;
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }
    }
}
