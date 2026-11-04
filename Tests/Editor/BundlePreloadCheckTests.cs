using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BS.Security;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The pre-load bundle check (Runtime/Scripts/Security/BundleCheck): rules on tiny bundles built byte by byte here
    /// (UnityFS + one serialized file with type trees), the FullSerializer-lenient graph JSON reader, the lists and their
    /// alignment with BSUnityEventGuard, Unity's own method binding, and the verdict cache. Real-world and mutated
    /// real bundles are covered offline (Phase A report), not here.
    /// </summary>
    public partial class BundlePreloadCheckTests
    {
        // ------------------------------------------------------------------ a minimal bundle writer

        /// <summary>A type tree node: primitives carry their size; arrays/strings/vectors are built by the helpers.</summary>
        sealed class N
        {
            internal string Type, Name;
            internal int Size = -1;
            internal bool IsArray, Align;
            internal readonly List<N> Children = new List<N>();

            internal N(string type, string name, int size = -1, bool align = false, params N[] children)
            {
                Type = type;
                Name = name;
                Size = size;
                Align = align;
                Children.AddRange(children);
            }
        }

        static N Int(string name) => new N("int", name, 4);
        static N Long(string name) => new N("SInt64", name, 8);
        static N Byte(string name) => new N("UInt8", name, 1, true);
        static N Float(string name) => new N("float", name, 4);
        static N Bool(string name) => new N("bool", name, 1, true);
        static N Str(string name) => new N("string", name, -1, false, new N("Array", "Array", -1, true, Int("size"), new N("char", "data", 1)) { IsArray = true });
        static N PPtr(string name, string of = "Object") => new N("PPtr<" + of + ">", name, 12, false, Int("m_FileID"), Long("m_PathID"));
        static N Vector(string name, N element) => new N("vector", name, -1, false, new N("Array", "Array", -1, true, Int("size"), element) { IsArray = true });

        static N MonoBehaviourBase(params N[] fields)
        {
            var root = new N("MonoBehaviour", "Base", -1, false, PPtr("m_GameObject", "GameObject"), Byte("m_Enabled"), PPtr("m_Script", "MonoScript"), Str("m_Name"));
            root.Children.AddRange(fields);
            return root;
        }

        static N PersistentCalls() =>
            new N("PersistentCallGroup", "m_PersistentCalls", -1, false, Vector("m_Calls", new N("PersistentCall", "data", -1, false,
                PPtr("m_Target"), Str("m_TargetAssemblyTypeName"), Str("m_MethodName"), Int("m_Mode"),
                new N("ArgumentCache", "m_Arguments", -1, false, PPtr("m_ObjectArgument"), Str("m_ObjectArgumentAssemblyTypeName"), Int("m_IntArgument"),
                    Float("m_FloatArgument"), Str("m_StringArgument"), Bool("m_BoolArgument")),
                Int("m_CallState"))));

        static readonly N monoScriptTree = new N("MonoScript", "Base", -1, false, Str("m_Name"), Int("m_ExecutionOrder"),
            Str("m_ClassName"), Str("m_Namespace"), Str("m_AssemblyName"));

        static readonly N gameObjectTree = new N("GameObject", "Base", -1, false, Str("m_Name"));

        sealed class Writer
        {
            readonly MemoryStream stream = new MemoryStream();
            internal int Position => (int)stream.Position;
            internal byte[] ToArray() => stream.ToArray();
            internal void U8(int v) => stream.WriteByte((byte)v);
            internal void I16(int v) { U8(v); U8(v >> 8); }
            internal void I32(int v) { U8(v); U8(v >> 8); U8(v >> 16); U8(v >> 24); }
            internal void I64(long v) { I32((int)v); I32((int)(v >> 32)); }
            internal void I32BE(int v) { U8(v >> 24); U8(v >> 16); U8(v >> 8); U8(v); }
            internal void I64BE(long v) { I32BE((int)(v >> 32)); I32BE((int)v); }
            internal void Bytes(byte[] b) => stream.Write(b, 0, b.Length);
            internal void CStr(string s) { Bytes(Encoding.UTF8.GetBytes(s)); U8(0); }
            internal void Align(int n) { while (stream.Position % n != 0) U8(0); }
            internal void Str(string s) { var b = Encoding.UTF8.GetBytes(s ?? ""); I32(b.Length); Bytes(b); Align(4); }
        }

        /// <summary>One object: its class, type tree, script (for 114) and serialized bytes.</summary>
        sealed class Obj
        {
            internal long PathId;
            internal int ClassId;
            internal N Tree;
            internal Action<Writer> Data;
            /// <summary>For 114: the path id of its MonoScript in this file (0: none), recorded in the type entry's script index.</summary>
            internal long Script;
            /// <summary>For 114: the script index written into its type entry instead of the real one (-1: the real one).</summary>
            internal int ScriptIndexOverride = -1;
            /// <summary>The type entry's stored type hash (16 bytes; null: zeroes).</summary>
            internal byte[] TypeHash;
        }

        /// <summary>Extra metadata for <see cref="SerializedFile(IList{Obj}, FileOptions)"/>.</summary>
        sealed class FileOptions
        {
            internal bool TypeTrees = true;
            internal uint Format = 22;
            /// <summary>External file paths (file id n = Externals[n-1]).</summary>
            internal readonly List<string> Externals = new List<string>();
            /// <summary>[SerializeReference] types: (tree, class, namespace, assembly).</summary>
            internal readonly List<(N tree, string cls, string ns, string asm)> RefTypes = new List<(N, string, string, string)>();
            /// <summary>Adds an AssetBundle object (every built bundle has one); off to test the refusal.</summary>
            internal bool AssetBundleObject = true;
            /// <summary>Stored type hashes of ref types by class name (missing: zeroes).</summary>
            internal readonly Dictionary<string, byte[]> RefTypeHashes = new Dictionary<string, byte[]>();
        }

        static readonly N assetBundleTree = new N("AssetBundle", "Base", -1, false, Str("m_Name"));

        static void Flatten(N node, int level, List<(N node, int level)> output)
        {
            output.Add((node, level));
            foreach (var child in node.Children) Flatten(child, level + 1, output);
        }

        static void WriteTree(Writer meta, N tree)
        {
            var nodes = new List<(N node, int level)>();
            Flatten(tree, 0, nodes);
            var strings = new List<byte>();
            var offsets = new Dictionary<string, int>();
            int Offset(string s)
            {
                if (offsets.TryGetValue(s, out var at)) return at;
                offsets[s] = strings.Count;
                strings.AddRange(Encoding.UTF8.GetBytes(s));
                strings.Add(0);
                return offsets[s];
            }
            foreach (var (node, _) in nodes)
            {
                Offset(node.Type);
                Offset(node.Name);
            }
            meta.I32(nodes.Count);
            meta.I32(strings.Count);
            var index = 0;
            foreach (var (node, level) in nodes)
            {
                meta.I16(1);
                meta.U8(level);
                meta.U8(node.IsArray ? 1 : 0);
                meta.I32(Offset(node.Type));
                meta.I32(Offset(node.Name));
                meta.I32(node.Size);
                meta.I32(index++);
                meta.I32(node.Align ? 0x4000 : 0);
                meta.I64(0);
            }
            meta.Bytes(strings.ToArray());
        }

        static byte[] SerializedFile(IList<Obj> objects, bool typeTrees = true) => SerializedFile(objects, new FileOptions { TypeTrees = typeTrees });

        /// <summary>A serialized file with type trees (zero stored hashes: nothing for Unity to match).</summary>
        static byte[] SerializedFile(IList<Obj> objects, FileOptions options)
        {
            objects = new List<Obj>(objects);
            if (options.AssetBundleObject && !objects.Any(o => o.ClassId == 142))
                objects.Add(new Obj { PathId = 1000001, ClassId = 142, Tree = assetBundleTree, Data = w => w.Str("test") });
            var scriptTable = new List<long>();
            foreach (var o in objects)
            {
                if (o.ClassId == 114 && o.Script != 0 && !scriptTable.Contains(o.Script)) scriptTable.Add(o.Script);
            }
            int ScriptIndex(Obj o) => o.ScriptIndexOverride >= 0 ? o.ScriptIndexOverride : o.Script == 0 ? 0xffff : scriptTable.IndexOf(o.Script);
            var types = new List<(int classId, N tree, int scriptIndex, byte[] hash)>();
            int TypeOf(Obj o) => types.FindIndex(t => t.classId == o.ClassId && t.tree == o.Tree && t.scriptIndex == (o.ClassId == 114 ? ScriptIndex(o) : 0xffff) && t.hash == o.TypeHash);
            foreach (var o in objects)
            {
                if (TypeOf(o) < 0) types.Add((o.ClassId, o.Tree, o.ClassId == 114 ? ScriptIndex(o) : 0xffff, o.TypeHash));
            }
            const int headerSize = 48;
            var meta = new Writer();
            void Align4()
            {
                while ((headerSize + meta.Position) % 4 != 0) meta.U8(0);
            }
            meta.CStr("6000.3.21f1");
            meta.I32(19);
            meta.U8(options.TypeTrees ? 1 : 0);
            meta.I32(types.Count);
            foreach (var (classId, tree, scriptIndex, hash) in types)
            {
                meta.I32(classId);
                meta.U8(0);
                meta.I16(scriptIndex);
                if (classId == 114) meta.Bytes(new byte[16]);
                meta.Bytes(hash ?? new byte[16]);
                if (!options.TypeTrees) continue;
                WriteTree(meta, tree);
                meta.I32(0); // type dependencies
            }
            // Object data first (to know offsets), each 8-aligned relative to the data start.
            var data = new Writer();
            var placed = new List<(Obj obj, int offset, int size)>();
            foreach (var o in objects)
            {
                data.Align(8);
                var start = data.Position;
                o.Data(data);
                placed.Add((o, start, data.Position - start));
            }
            meta.I32(objects.Count);
            Align4();
            foreach (var (obj, offset, size) in placed)
            {
                Align4();
                meta.I64(obj.PathId);
                meta.I64(offset);
                meta.I32(size);
                meta.I32(TypeOf(obj));
            }
            meta.I32(scriptTable.Count);
            foreach (var script in scriptTable)
            {
                meta.I32(0);
                Align4();
                meta.I64(script);
            }
            meta.I32(options.Externals.Count);
            foreach (var path in options.Externals)
            {
                meta.CStr("");
                meta.Bytes(new byte[16]);
                meta.I32(0);
                meta.CStr(path);
            }
            meta.I32(options.RefTypes.Count);
            foreach (var (tree, cls, ns, asm) in options.RefTypes)
            {
                meta.I32(114);
                meta.U8(0);
                meta.I16(0xffff);
                meta.Bytes(new byte[16]);
                meta.Bytes(options.RefTypeHashes.TryGetValue(cls, out var refHash) ? refHash : new byte[16]);
                if (options.TypeTrees) WriteTree(meta, tree);
                meta.CStr(cls);
                meta.CStr(ns);
                meta.CStr(asm);
            }
            meta.CStr("");
            var metadata = meta.ToArray();
            var dataOffset = (headerSize + metadata.Length + 15) / 16 * 16;
            var file = new Writer();
            file.I32BE(0);
            file.I32BE(0);
            file.I32BE((int)options.Format);
            file.I32BE(0);
            file.U8(0);
            file.U8(0);
            file.U8(0);
            file.U8(0);
            file.I32BE(metadata.Length);
            file.I64BE(dataOffset + data.Position);
            file.I64BE(dataOffset);
            file.I64BE(0);
            file.Bytes(metadata);
            while (file.Position < dataOffset) file.U8(0);
            file.Bytes(data.ToArray());
            return file.ToArray();
        }

        /// <summary>An uncompressed UnityFS bundle holding the given serialized files.</summary>
        static byte[] Bundle(params (string name, byte[] bytes)[] files) => Bundle(files.Select(f => (f.name, f.bytes, 4)).ToArray());

        /// <summary>An uncompressed UnityFS bundle; each file with its directory flags (4 = serialized file).</summary>
        static byte[] Bundle(params (string name, byte[] bytes, int flags)[] files)
        {
            var info = new Writer();
            info.Bytes(new byte[16]);
            var total = files.Sum(f => f.bytes.Length);
            info.I32BE(1);
            info.I32BE(total);
            info.I32BE(total);
            info.U8(0);
            info.U8(0x40);
            info.I32BE(files.Length);
            long offset = 0;
            foreach (var (name, bytes, flags) in files)
            {
                info.I64BE(offset);
                info.I64BE(bytes.Length);
                info.I32BE(flags);
                info.CStr(name);
                offset += bytes.Length;
            }
            var blocksInfo = info.ToArray();
            var bundle = new Writer();
            bundle.CStr("UnityFS");
            bundle.I32BE(8);
            bundle.CStr("5.x.x");
            bundle.CStr("6000.3.21f1");
            const int headerEnd = 64; // 50 header bytes aligned to 16
            bundle.I64BE(headerEnd + blocksInfo.Length + total);
            bundle.I32BE(blocksInfo.Length);
            bundle.I32BE(blocksInfo.Length);
            bundle.I32BE(0x40);
            while (bundle.Position < headerEnd) bundle.U8(0);
            bundle.Bytes(blocksInfo);
            foreach (var (_, bytes, _) in files) bundle.Bytes(bytes);
            return bundle.ToArray();
        }

        // ---- a world: one GameObject with one component (a UnityEvent holder) whose MonoScript names a type.

        sealed class Call
        {
            internal long Target = 1;
            internal int TargetFile;
            internal string TypeName = "UnityEngine.GameObject, UnityEngine";
            internal string Method = "SetActive";
            internal int Mode = 6;
            internal string ArgumentType = "";
        }

        static Obj Script(long pathId, string assembly, string ns, string cls) => new Obj
        {
            PathId = pathId, ClassId = 115, Tree = monoScriptTree,
            Data = w =>
            {
                w.Str(cls);
                w.I32(0);
                w.Str(cls);
                w.Str(ns);
                w.Str(assembly);
            },
        };

        static Obj GameObject(long pathId, string name) => new Obj { PathId = pathId, ClassId = 1, Tree = gameObjectTree, Data = w => w.Str(name) };

        static readonly N buttonTree = MonoBehaviourBase(new N("ButtonClickedEvent", "m_OnClick", -1, false, PersistentCalls()));

        static Obj Behaviour(long pathId, long script, N tree, Action<Writer> fields, long gameObject = 1) => new Obj
        {
            PathId = pathId, ClassId = 114, Tree = tree, Script = script,
            Data = w =>
            {
                w.I32(0); w.I64(gameObject);
                w.U8(1); w.Align(4);
                w.I32(0); w.I64(script);
                w.Str("");
                fields(w);
            },
        };

        static void WriteCalls(Writer w, IList<Call> calls)
        {
            w.I32(calls.Count);
            foreach (var call in calls)
            {
                w.I32(call.TargetFile); w.I64(call.Target);
                w.Str(call.TypeName);
                w.Str(call.Method);
                w.I32(call.Mode);
                w.I32(0); w.I64(0);
                w.Str(call.ArgumentType);
                w.I32(0);
                w.I32(0);
                w.Str("");
                w.U8(1); w.Align(4);
                w.I32(2);
            }
        }

        /// <summary>A scene file ("BuildPlayer-Test") with a Button-scripted component holding <paramref name="calls"/>.</summary>
        static byte[] ButtonWorld(params Call[] calls) => ButtonWorld("UnityEngine.UI.dll", "UnityEngine.UI", "Button", calls);

        static byte[] ButtonWorld(string assembly, string ns, string cls, params Call[] calls) => Bundle(("BuildPlayer-Test", SerializedFile(new[]
        {
            GameObject(1, "Panel"),
            Script(2, assembly, ns, cls),
            Behaviour(3, 2, buttonTree, w => WriteCalls(w, calls)),
        })));

        static BundleVerdict Check(byte[] bundle, BundleCheckOptions options = null) => BundleChecker.Check(bundle, options ?? BundleCheckOptions.World(), "test");

        static void AssertRefused(BundleVerdict verdict, BundleRule rule)
        {
            Assert.IsFalse(verdict.Passed, "expected a refusal for " + rule + "; " + verdict.Describe("test"));
            Assert.IsTrue(verdict.Reasons.Any(r => r.Rule == rule), "expected " + rule + "; " + verdict.Describe("test"));
        }

        static void AssertPassed(BundleVerdict verdict) => Assert.IsTrue(verdict.Passed, verdict.Describe("test"));

        // ------------------------------------------------------------------ container and structure

        [Test]
        public void PlainWorldPasses()
        {
            var verdict = Check(ButtonWorld(new Call()));
            AssertPassed(verdict);
            Assert.AreEqual(1, verdict.Stats.PersistentCalls);
            Assert.AreEqual(1, verdict.Stats.Scenes);
        }

        [Test]
        public void NotABundleAndTruncatedBundlesAreRefused()
        {
            AssertRefused(Check(Encoding.ASCII.GetBytes(new string('x', 500))), BundleRule.Unsupported);
            var bundle = ButtonWorld(new Call());
            AssertRefused(Check(bundle.Take(bundle.Length - 40).ToArray()), BundleRule.Malformed);
            AssertRefused(Check(bundle.Take(70).ToArray()), BundleRule.Malformed);
        }

        [Test]
        public void FilesWithoutTypeTreesAreRefused()
        {
            var file = SerializedFile(new[] { GameObject(1, "A") }, typeTrees: false);
            AssertRefused(Check(Bundle(("BuildPlayer-Test", file))), BundleRule.NoTypeTree);
        }

        [Test]
        public void TwoScenesOrAKitWithASceneAreRefused()
        {
            var scene = SerializedFile(new[] { GameObject(1, "A") });
            AssertRefused(Check(Bundle(("BuildPlayer-One", scene), ("BuildPlayer-Two", scene))), BundleRule.TooManyScenes);
            AssertRefused(Check(Bundle(("BuildPlayer-One", scene)), BundleCheckOptions.Kit()), BundleRule.TooManyScenes);
            AssertPassed(Check(Bundle(("CAB-0123456789abcdef", scene)), BundleCheckOptions.Kit()));
        }

        // ------------------------------------------------------------------ persistent calls

        [Test]
        public void TargetlessCallReachingAStaticIsRefused()
        {
            var call = new Call { Target = 0, TypeName = "System.IO.File, mscorlib", Method = "Delete", Mode = 5 };
            AssertRefused(Check(ButtonWorld(call)), BundleRule.CallNoTarget);
        }

        [Test]
        public void TargetlessCallToAnInstanceMethodIsOnlyReported()
        {
            var verdict = Check(ButtonWorld(new Call { Target = 0 }));
            AssertPassed(verdict);
            Assert.IsTrue(verdict.Reports.Any(r => r.Rule == BundleRule.CallDead), verdict.Describe("test"));
        }

        [Test]
        public void StaticNonPublicAndByNameCallsAreRefused()
        {
            AssertRefused(Check(ButtonWorld(new Call { Method = "Destroy", Mode = 2, ArgumentType = "UnityEngine.Object, UnityEngine" })), BundleRule.CallStatic);
            // A Button's click can be fired during activation (by a listed setter), so by-name stays refused here.
            AssertRefused(Check(ButtonWorld(new Call { Method = "SendMessage", Mode = 5 })), BundleRule.CallByName);
            var hidden = typeof(UnityEngine.GameObject).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.GetParameters().Length == 0 && !m.IsGenericMethod);
            if (hidden != null) AssertRefused(Check(ButtonWorld(new Call { Method = hidden.Name, Mode = 1 })), BundleRule.CallNonPublic);
        }

        static byte[] InputFieldWorld(string eventField, params Call[] calls) => Bundle(("BuildPlayer-Test", SerializedFile(new[]
        {
            GameObject(1, "Field"),
            Script(2, "UnityEngine.UI.dll", "UnityEngine.UI", "InputField"),
            Behaviour(3, 2, MonoBehaviourBase(new N("SubmitEvent", eventField, -1, false, PersistentCalls())), w => WriteCalls(w, calls)),
        })));

        /// <summary>roll20's pattern: a by-name call on an InputField's submit (user input only) passes with a warning by default.</summary>
        [Test]
        public void ByNameCallOnInputFieldSubmitIsANarrowWarning()
        {
            if (BundleTypeResolver.ResolveScript("UnityEngine.UI.dll", "UnityEngine.UI", "InputField") == null) Assert.Ignore("UGUI not loaded");
            var call = new Call { Method = "SendMessage", Mode = 0 };
            var verdict = Check(InputFieldWorld("m_OnSubmit", call));
            AssertPassed(verdict);
            Assert.IsTrue(verdict.Reports.Any(r => r.Rule == BundleRule.CallByName && r.Detail.StartsWith("warning: ")), verdict.Describe("test"));
            AssertRefused(Check(InputFieldWorld("m_OnSubmit", call), new BundleCheckOptions { NarrowPatterns = BundleSeverity.Refuse }), BundleRule.CallByName);
            // onValueChanged can fire during activation: refused whatever the setting.
            AssertRefused(Check(InputFieldWorld("m_OnValueChanged", call)), BundleRule.CallByName);
        }

        [Test]
        public void StoredTypeNameReachingAStaticIsRefused()
        {
            var call = new Call { TypeName = "UnityEngine.AudioListener, UnityEngine.AudioModule", Method = "set_volume", Mode = 4 };
            AssertRefused(Check(ButtonWorld(call)), BundleRule.CallTypeName);
        }

        [Test]
        public void EditorTruncatedGenericTypeNamesAreJudgedByTheirDefinition()
        {
            Assert.AreEqual("Unity.VisualScripting.EventMachine`2",
                BundleTypeResolver.TopLevelName("Unity.VisualScripting.EventMachine`2[[Unity.VisualScripting.FlowGraph, Unity.VisualScripting.Flow"));
            Assert.IsNull(BundleTypeResolver.TopLevelName("System.String[], mscorlib"));
            var call = new Call { Target = 0, TypeName = "System.Collections.Generic.Comparer`1[[System.String, mscorlib", Method = "get_Default", Mode = 1 };
            AssertRefused(Check(ButtonWorld(call)), BundleRule.CallNoTarget);
        }

        [Test]
        public void TargetOutsideTheBundleIsRefusedAndADanglingOneIsDead()
        {
            AssertRefused(Check(ButtonWorld(new Call { TargetFile = 1 })), BundleRule.CallOutsideTarget);
            // A path id with no object here loads as null: judged like a target-less call (dead here).
            AssertPassed(Check(ButtonWorld(new Call { Target = 999 })));
        }

        // ------------------------------------------------------------------ scripts

        [Test]
        public void HostScriptsAreRefusedAndMissingScriptsAreInert()
        {
            AssertRefused(Check(ButtonWorld("Slash.Unity.DataBind.dll", "Slash.Unity.DataBind.Core.Presentation", "ContextHolder")), BundleRule.DeniedScript);
            AssertRefused(Check(ButtonWorld("BS.SDK.dll", "BS", "DontDestroyOnLoad")), BundleRule.DeniedScript);
            var verdict = Check(ButtonWorld("Assembly-CSharp.dll", "", "NoSuchAuthorScript4242", new Call { Target = 0, TypeName = "System.IO.File, mscorlib", Method = "Delete", Mode = 5 }));
            AssertPassed(verdict); // never deserialized: its calls cannot run
            Assert.IsTrue(verdict.Reports.Any(r => r.Rule == BundleRule.CallInert), verdict.Describe("test"));
        }

        [Test]
        public void PolicyClassifiesAssembliesAndTypes()
        {
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Content, BundleCheckPolicy.Classify("UnityEngine.UI.Button", "UnityEngine.UI"));
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Content, BundleCheckPolicy.Classify("BS.BSAudioSource", "BS.SDK"));
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Content, BundleCheckPolicy.Classify("Unity.Cinemachine.CinemachineCamera", "Unity.Cinemachine"));
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Content, BundleCheckPolicy.Classify("UnityEngine.InputSystem.UI.InputSystemUIInputModule", "Unity.InputSystem"));
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Denied, BundleCheckPolicy.Classify("SideQuest.GreenfieldMcp.GreenfieldMcpServer", "SideQuest.GreenfieldMcp"));
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Denied, BundleCheckPolicy.Classify("Anything", "Slash.Unity.DataBind"));
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Denied, BundleCheckPolicy.Classify("Anything", "Assembly-CSharp"));
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Denied, BundleCheckPolicy.Classify("BS.DontDestroyOnLoad", "BS.SDK"));
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Denied, BundleCheckPolicy.Classify("UnityEngine.InputSystem.PlayerInput", "Unity.InputSystem"));
            Assert.AreEqual(BundleCheckPolicy.ScriptClass.Unlisted, BundleCheckPolicy.Classify("UnityEngine.InputSystem.InputActionAsset", "Unity.InputSystem"));
        }

        /// <summary>
        /// The checker reads the guard's own lists at runtime (one source); its built-in fallback copies must match them,
        /// and the lists in effect must come from the guard.
        /// </summary>
        [Test]
        public void PreloadCheck_MirrorsTheUnityEventGuardsLists()
        {
            var guard = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "BS.SDK")
                .Select(a => a.GetType("BS.BSUnityEventGuard", false)).FirstOrDefault(t => t != null);
            Assert.IsNotNull(guard, "BSUnityEventGuard not found");
            IEnumerable<string> Field(string name)
            {
                var field = guard.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(field, "BSUnityEventGuard." + name + " moved: re-align BundleCheckPolicy");
                return ((IEnumerable<string>)field.GetValue(null)).ToList();
            }
            CollectionAssert.AreEquivalent(Field("contentAssemblies"), BundleCheckPolicy.BuiltinContentAssemblies);
            CollectionAssert.AreEquivalent(Field("contentAssemblyPrefixes"), BundleCheckPolicy.BuiltinContentAssemblyPrefixes);
            CollectionAssert.AreEquivalent(Field("contentTypes"), BundleCheckPolicy.BuiltinContentTypes);
            CollectionAssert.AreEquivalent(Field("deniedHostAssemblies"), BundleCheckPolicy.BuiltinDeniedHostAssemblies);
            CollectionAssert.AreEquivalent(Field("deniedHostTypes").Where(t => !t.StartsWith("BS.Tests", StringComparison.Ordinal)), BundleCheckPolicy.BuiltinDeniedHostTypes);
            CollectionAssert.AreEquivalent(Field("byNameDispatch"), BundleCheckPolicy.BuiltinByNameDispatch);
            CollectionAssert.AreEquivalent(Field("animationEventSinks"), BundleCheckPolicy.BuiltinAnimationEventSinks);
            var lists = BundleCheckPolicy.Refresh();
            Assert.AreEqual("guard", lists.Source, "the checker must read the guard's lists, not its fallback copies");
            CollectionAssert.IsSubsetOf(Field("deniedHostTypes"), lists.DeniedTypes);
            CollectionAssert.IsSubsetOf(Field("deniedHostAssemblies"), lists.DeniedAssemblies);
            CollectionAssert.AreEquivalent(Field("contentAssemblies"), lists.ContentAssemblies);
        }

        // ------------------------------------------------------------------ other by-name sinks

        [Test]
        public void NamedCallbacksAreRefused()
        {
            var tree = MonoBehaviourBase(new N("EventCallback", "LocalizeCallBack", -1, false, PPtr("Target", "MonoBehaviour"), Str("MethodName")));
            byte[] World(string method) => Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Label"), Script(2, "UnityEngine.UI.dll", "UnityEngine.UI", "Text"),
                Behaviour(3, 2, tree, w => { w.I32(0); w.I64(3); w.Str(method); }),
            })));
            AssertRefused(Check(World("OnDestroy")), BundleRule.NamedCallback);
            AssertPassed(Check(World("")));
        }

        [Test]
        public void PlayerInputInMessageModeIsANarrowWarning()
        {
            var tree = MonoBehaviourBase(Int("m_NotificationBehavior"));
            var bundle = Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Player"), Script(2, "Unity.InputSystem.dll", "UnityEngine.InputSystem", "PlayerInput"),
                Behaviour(3, 2, tree, w => w.I32(0)),
            })));
            if (BundleTypeResolver.ResolveScript("Unity.InputSystem.dll", "UnityEngine.InputSystem", "PlayerInput") == null) Assert.Ignore("Input System not loaded");
            // ernst2's pattern: by default a warning (the guard forces C# events and removes PlayerInput at sceneLoaded)...
            var verdict = Check(bundle);
            AssertPassed(verdict);
            Assert.IsTrue(verdict.Reports.Any(r => r.Rule == BundleRule.PlayerInputMessages && r.Detail.StartsWith("warning: ")), verdict.Describe("test"));
            // ...and refused when the narrow patterns are set to Refuse.
            var strict = Check(bundle, new BundleCheckOptions { NarrowPatterns = BundleSeverity.Refuse });
            AssertRefused(strict, BundleRule.PlayerInputMessages);
            AssertRefused(strict, BundleRule.DeniedScript);
        }

        [Test]
        public void GraphDataWithEventValuesIsRefusedAndLenientJsonPasses()
        {
            var tree = MonoBehaviourBase(new N("SerializationData", "_data", -1, false, Str("_json"), Vector("_objectReferences", PPtr("data"))));
            byte[] World(string json) => Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Graph"), Script(2, "Unity.VisualScripting.Core.dll", "Unity.VisualScripting", "Variables"),
                Behaviour(3, 2, tree, w => { w.Str(json); w.I32(0); }),
            })));
            AssertPassed(Check(World("{\"declarations\":{\"Kind\":\"Object\",\"collection\":{\"$content\":[],\"$version\":\"A\"},\"$version\":\"A\"}}")));
            AssertRefused(Check(World("{\"v\":{\"$type\":\"UnityEngine.Events.UnityEvent, UnityEngine.CoreModule\",\"m_PersistentCalls\":{\"m_Calls\":[{\"m_MethodName\":\"Delete\"}]}}}")),
                BundleRule.GraphUnityEvent);
            AssertRefused(Check(World("{\"v\":{\"$type\":\"UnityEngine.AnimationEvent, UnityEngine.AnimationModule\"}}")), BundleRule.GraphAnimationEvent);
            AssertRefused(Check(World("{\"v\":\"unterminated")), BundleRule.GraphUnreadable);
        }

        [Test]
        public void GraphJsonReaderFollowsFullSerializer()
        {
            Assert.IsFalse(BundleGraphJson.Scan("/* c */ {\"a\":\"x\\0y\" \"b\":[1 2,3,] // tail\n}").Unreadable, "comments, \\0, missing and trailing commas");
            Assert.IsFalse(BundleGraphJson.Scan("{\"a\":\"raw\u0001control\"} trailing ignored").Unreadable);
            Assert.IsTrue(BundleGraphJson.Scan("{\"a\":1,\"a\":2}").Unreadable, "duplicate keys throw in FullSerializer");
            Assert.IsTrue(BundleGraphJson.Scan("{\"a\":\"\\q\"}").Unreadable, "unknown escape");
            Assert.IsTrue(BundleGraphJson.Scan("{\"a\":[").Unreadable);
            var calls = BundleGraphJson.Scan("{\"m_persistentcalls\":{\"M_CALLS\":[{\"m_MethodName\":\"Quit\"},{\"methodName\":\"\"}]}}");
            CollectionAssert.AreEqual(new[] { "Quit" }, calls.PersistentCalls, "keys match case-insensitively; empty names are never valid");
            Assert.AreEqual(1, BundleGraphJson.Scan("{\"e\":{\"functionName\":\"Destroy\"}}").AnimationEvents.Count);
            Assert.IsFalse(BundleGraphJson.Scan("").Unreadable);
        }

        // ------------------------------------------------------------------ Unity's own binding

        [Test]
        public void MethodBindingFollowsUnity()
        {
            var bound = BundleEventRules.PossibleMethods(typeof(UnityEngine.GameObject), "SetActive", 6, null, null, out var approximate);
            Assert.IsFalse(approximate);
            Assert.AreEqual(BundleEventRules.Verdict.Ok, BundleEventRules.JudgeMethod(bound.Single()));
            var destroy = BundleEventRules.PossibleMethods(typeof(UnityEngine.GameObject), "Destroy", 2, "UnityEngine.Object, UnityEngine", null, out _);
            Assert.IsTrue(destroy.Any(m => BundleEventRules.JudgeMethod(m) == BundleEventRules.Verdict.Static));
            var file = BundleTypeResolver.ResolveTypeName("System.IO.File, mscorlib");
            Assert.IsTrue(file.Any(t => BundleEventRules.PossibleMethods(t, "Delete", 5, null, null, out _).Any(m => m.IsStatic)));
            var button = BundleTypeResolver.ResolveScript("UnityEngine.UI.dll", "UnityEngine.UI", "Button");
            if (button != null) CollectionAssert.IsEmpty(BundleEventRules.EventArguments(button, new[] { "m_OnClick" }));
        }

        [Test]
        public void AnimationEventsUseTheGuardRules()
        {
            var receivers = new List<Type> { typeof(UnityEngine.MonoBehaviour) };
            Assert.AreEqual(BundleEventRules.Verdict.ByName, BundleEventRules.JudgeAnimationEvent("SendMessage", receivers, out _));
            Assert.AreNotEqual(BundleEventRules.Verdict.Ok, BundleEventRules.JudgeAnimationEvent("Destroy", receivers, out _));
            Assert.AreEqual(BundleEventRules.Verdict.NoReceiver, BundleEventRules.JudgeAnimationEvent("", receivers, out _));
        }

        // ------------------------------------------------------------------ verdict cache

        [Test]
        public void VerdictCacheRoundTripsBoundsAndSurvivesCorruption()
        {
            var path = Path.Combine(Path.GetTempPath(), "bundle-verdicts-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var cache = new BundleVerdictCache(path, 3);
                string Key(int i) => BundleVerdictCache.Key(Enumerable.Repeat((byte)i, 32).ToArray(), "c1/labc/s1h/etest");
                cache.Put(Key(1), true, "");
                cache.Put(Key(2), false, "CallStatic: X.Y");
                Assert.IsTrue(new BundleVerdictCache(path).TryGet(Key(2), out var passed, out var reason));
                Assert.IsFalse(passed);
                Assert.AreEqual("CallStatic: X.Y", reason);
                cache.TryGet(Key(1), out _, out _); // 1 is now newer than 2
                cache.Put(Key(3), true, "");
                cache.Put(Key(4), true, "");
                var reopened = new BundleVerdictCache(path, 3);
                Assert.AreEqual(3, reopened.Count);
                Assert.IsFalse(reopened.TryGet(Key(2), out _, out _), "least recently used goes first");
                Assert.IsTrue(reopened.TryGet(Key(1), out _, out _));

                File.WriteAllText(path, "{\"format\":1,\"entries\":[\n{\"k\":\"zz\" broken\n");
                Assert.AreEqual(0, new BundleVerdictCache(path).Count, "a corrupt file reads as empty");
                Assert.IsFalse(new BundleVerdictCache(path).TryGet("not|safe\"key", out _, out _));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ServiceAnswersFromTheCacheForTheSameBytesAndRules()
        {
            var path = Path.Combine(Path.GetTempPath(), "bundle-verdicts-test-" + Guid.NewGuid().ToString("N") + ".json");
            var hashing = BundleCheckService.Hashing;
            try
            {
                BundleCheckService.Configure(path, "test-build");
                BundleCheckService.Hashing = BundleCheckService.HashPolicy.Always;
                var bundle = ButtonWorld(new Call { Target = 0, TypeName = "System.IO.File, mscorlib", Method = "Delete", Mode = 5 });
                var first = BundleCheckService.Check(bundle, 0, bundle.Length, BundleCheckOptions.World());
                var second = BundleCheckService.Check(bundle, 0, bundle.Length, BundleCheckOptions.World());
                Assert.IsFalse(first.Passed);
                Assert.IsFalse(first.FromCache);
                Assert.IsTrue(second.FromCache);
                Assert.IsFalse(second.Passed);
                var asKit = BundleCheckService.Check(bundle, 0, bundle.Length, BundleCheckOptions.Kit());
                Assert.IsFalse(asKit.FromCache, "other rules, other key");
                Assert.AreNotEqual(BundleCheckPolicy.Versions(BundleCheckOptions.World(), "a"), BundleCheckPolicy.Versions(BundleCheckOptions.World(), "b"));
                // Auto: a bundle without LZMA is checked every time (cheaper than hashing it), never cached.
                BundleCheckService.Hashing = BundleCheckService.HashPolicy.Auto;
                var plain = ButtonWorld(new Call());
                BundleCheckService.Check(plain, 0, plain.Length, BundleCheckOptions.World());
                Assert.IsFalse(BundleCheckService.Check(plain, 0, plain.Length, BundleCheckOptions.World()).FromCache);
            }
            finally
            {
                BundleCheckService.Hashing = hashing;
                RestoreCheckConfiguration();
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
