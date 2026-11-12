using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using BS.Security;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The hardening round of the pre-load check (Hex/Beacon review, 2026-11-04): every confirmed bypass in its smallest
    /// form, built byte by byte with the fixture writer in BundlePreloadCheckTests. The checker must refuse anything it
    /// cannot judge exactly as Unity will read it: non-canonical or duplicate fields, roles hiding roles, long or
    /// control-character names, script/type-entry disagreements, spoofed class names, references outside the bundle,
    /// unlisted ScriptableObjects and [SerializeReference] types, and graph JSON hiding calls or naming host types.
    /// </summary>
    public partial class BundlePreloadCheckTests
    {
        static readonly Call staticDelete = new Call { Target = 0, TypeName = "System.IO.File, mscorlib", Method = "Delete", Mode = 5 };

        /// <summary>A Button-scripted world whose behaviour uses <paramref name="tree"/>, holding one call in m_OnClick.</summary>
        static byte[] TreeWorld(N tree, Action<Writer> fields, FileOptions options = null, long script = 2) =>
            Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Panel"),
                Script(2, "UnityEngine.UI.dll", "UnityEngine.UI", "Button"),
                Behaviour(3, script, tree, fields),
            }, options ?? new FileOptions())));

        static N UnityEvent(string name, params N[] extra)
        {
            var node = new N("ButtonClickedEvent", name, -1, false, PersistentCalls());
            node.Children.AddRange(extra);
            return node;
        }

        static N Base(Func<N, N> editBase, params N[] fields)
        {
            var root = MonoBehaviourBase(fields);
            return editBase(root);
        }

        // ------------------------------------------------------------------ B1: roles never shadow

        [Test]
        public void RoleStringsNextToPersistentCallsDoNotHideThem()
        {
            foreach (var marker in new[] { "functionName", "MethodName", "_json" })
            {
                var tree = MonoBehaviourBase(UnityEvent("m_OnClick", Str(marker)));
                var bundle = TreeWorld(tree, w =>
                {
                    WriteCalls(w, new[] { staticDelete });
                    w.Str("");
                });
                AssertRefused(Check(bundle), BundleRule.CallNoTarget);
            }
        }

        // ------------------------------------------------------------------ B1: canonical shapes

        [Test]
        public void NonCanonicalOrDuplicateBaseFieldsAreRefused()
        {
            // m_Script with an extra child (Unity reads it by name; the checker used to see no script at all).
            var extraChild = MonoBehaviourBase(UnityEvent("m_OnClick"));
            extraChild.Children[2].Children.Add(Int("m_Extra"));
            AssertRefused(Check(TreeWorld(extraChild, w => WriteCalls(w, new[] { staticDelete }))), BundleRule.TypeTreeShape);

            // m_PathID before m_FileID.
            var swapped = MonoBehaviourBase(UnityEvent("m_OnClick"));
            swapped.Children[2].Children.Reverse();
            AssertRefused(Check(TreeWorld(swapped, w => WriteCalls(w, new[] { staticDelete }))), BundleRule.TypeTreeShape);

            // A second m_Script, and one that differs only in case.
            var duplicate = MonoBehaviourBase(PPtr("m_Script", "MonoScript"), UnityEvent("m_OnClick"));
            AssertRefused(Check(TreeWorld(duplicate, w => { w.I32(0); w.I64(0); WriteCalls(w, new[] { staticDelete }); })), BundleRule.TypeTreeShape);
            var folded = MonoBehaviourBase(PPtr("M_SCRIPT", "MonoScript"), UnityEvent("m_OnClick"));
            AssertRefused(Check(TreeWorld(folded, w => { w.I32(0); w.I64(0); WriteCalls(w, new[] { staticDelete }); })), BundleRule.TypeTreeShape);

            // m_GameObject that is not a PPtr (the component used to be taken for a ScriptableObject).
            var gameObject = MonoBehaviourBase(UnityEvent("m_OnClick"));
            gameObject.Children[0] = new N("Foo", "m_GameObject", 12, false, Int("m_FileID"), Long("m_PathID"));
            AssertRefused(Check(TreeWorld(gameObject, w => WriteCalls(w, new[] { staticDelete }))), BundleRule.TypeTreeShape);
        }

        [Test]
        public void PersistentCallFieldsMustBeUnitysOwn()
        {
            // m_Mode typed float: Unity's reader converts it, the checker used to read its raw bits.
            var tree = MonoBehaviourBase(UnityEvent("m_OnClick"));
            var call = tree.Children[4].Children[0].Children[0].Children[0].Children[1];
            Assert.AreEqual("m_Mode", call.Children[3].Name);
            call.Children[3] = Float("m_Mode");
            AssertRefused(Check(TreeWorld(tree, w => WriteCalls(w, new[] { staticDelete }))), BundleRule.TypeTreeShape);

            // An extra field in the call, and a non-canonical m_Target.
            var extra = MonoBehaviourBase(UnityEvent("m_OnClick"));
            extra.Children[4].Children[0].Children[0].Children[0].Children[1].Children.Add(Int("m_Extra"));
            AssertRefused(Check(TreeWorld(extra, w => WriteCalls(w, new[] { staticDelete }))), BundleRule.TypeTreeShape);
            var target = MonoBehaviourBase(UnityEvent("m_OnClick"));
            target.Children[4].Children[0].Children[0].Children[0].Children[1].Children[0].Children.Add(Int("m_Extra"));
            AssertRefused(Check(TreeWorld(target, w => WriteCalls(w, new[] { staticDelete }))), BundleRule.TypeTreeShape);
        }

        [Test]
        public void JudgedStringFieldsMustBeStrings()
        {
            var tree = MonoBehaviourBase(new N("EventCallback", "LocalizeCallBack", -1, false, Int("MethodName")));
            AssertRefused(Check(TreeWorld(tree, w => w.I32(1))), BundleRule.TypeTreeShape);
        }

        // ------------------------------------------------------------------ B1: MonoScript identities

        [Test]
        public void ScriptIdentitiesWithControlCharactersOrOverOneKilobyteAreRefused()
        {
            AssertRefused(Check(ButtonWorld("UnityEngine.UI.dll", "UnityEngine.UI", "Button\0Evil", new Call())), BundleRule.BadName);
            AssertRefused(Check(ButtonWorld("UnityEngine.UI.dll", "UnityEngine.UI\n", "Button", new Call())), BundleRule.BadName);
            AssertRefused(Check(ButtonWorld("UnityEngine.UI.dll", "UnityEngine.UI" + new string('x', 1100), "Button", new Call())), BundleRule.BadName);
        }

        [Test]
        public void DuplicateMonoScriptIdentityFieldsAreRefused()
        {
            var tree = new N("MonoScript", "Base", -1, false, Str("m_Name"), Int("m_ExecutionOrder"), Str("m_ClassName"), Str("m_Namespace"),
                Str("m_AssemblyName"), Str("m_ClassName"));
            var bundle = Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Panel"),
                new Obj { PathId = 2, ClassId = 115, Tree = tree, Data = w => { w.Str("Button"); w.I32(0); w.Str("RigBuilder"); w.Str("UnityEngine.UI"); w.Str("UnityEngine.UI.dll"); w.Str("Button"); } },
                Behaviour(3, 2, buttonTree, w => WriteCalls(w, new Call[0])),
            })));
            AssertRefused(Check(bundle), BundleRule.TypeTreeShape);
        }

        [Test]
        public void TypeEntryScriptMustBeTheObjectsScript()
        {
            var bundle = Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Panel"),
                Script(2, "UnityEngine.UI.dll", "UnityEngine.UI", "Button"),
                Script(4, "UnityEngine.UI.dll", "UnityEngine.UI", "Toggle"),
                new Obj { PathId = 5, ClassId = 114, Tree = MonoBehaviourBase(), Script = 4, Data = w => { w.I32(0); w.I64(1); w.U8(1); w.Align(4); w.I32(0); w.I64(4); w.Str(""); } },
                // The script table is [4, 2] (in object order); this type entry says index 0, Toggle's 4, while m_Script is 2.
                new Obj { PathId = 3, ClassId = 114, Tree = buttonTree, Script = 2, ScriptIndexOverride = 0, Data = w =>
                {
                    w.I32(0); w.I64(1); w.U8(1); w.Align(4); w.I32(0); w.I64(2); w.Str(""); WriteCalls(w, new Call[0]);
                } },
            })));
            AssertRefused(Check(bundle), BundleRule.TypeTreeMismatch);
        }

        // ------------------------------------------------------------------ B1: class ids, not root names

        [Test]
        public void ACallTargetWhoseTreeNamesAnotherClassIsRefused()
        {
            var spoofed = new N("GameObject", "Base", -1, false, Str("m_Name"));
            var bundle = Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Panel"),
                Script(2, "UnityEngine.UI.dll", "UnityEngine.UI", "Button"),
                Behaviour(3, 2, buttonTree, w => WriteCalls(w, new[] { new Call { Target = 9, TypeName = "", Method = "LoadFromFile", Mode = 5 } })),
                new Obj { PathId = 9, ClassId = 142, Tree = spoofed, Data = w => w.Str("x") },
            })));
            AssertRefused(Check(bundle), BundleRule.TypeTreeMismatch);
            // Named honestly, the same target is the AssetBundle class, and LoadFromFile is static.
            var honest = Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Panel"),
                Script(2, "UnityEngine.UI.dll", "UnityEngine.UI", "Button"),
                Behaviour(3, 2, buttonTree, w => WriteCalls(w, new[] { new Call { Target = 9, TypeName = "", Method = "LoadFromFile", Mode = 5 } })),
                new Obj { PathId = 9, ClassId = 142, Tree = assetBundleTree, Data = w => w.Str("x") },
            })));
            AssertRefused(Check(honest), BundleRule.CallStatic);
        }

        // ------------------------------------------------------------------ B2: external files

        static byte[] ExternalScriptWorld(string external, string node = "BuildPlayer-Test", params (string, byte[], int)[] more)
        {
            var options = new FileOptions();
            options.Externals.Add(external);
            var behaviour = new Obj
            {
                PathId = 3, ClassId = 114, Tree = buttonTree, Script = 0, Data = w =>
                {
                    w.I32(0); w.I64(1); w.U8(1); w.Align(4);
                    w.I32(1); w.I64(4242); // m_Script in external file 1
                    w.Str("");
                    WriteCalls(w, new[] { staticDelete });
                },
            };
            behaviour.ScriptIndexOverride = 0;
            var file = SerializedFileWithScriptTable(new[] { GameObject(1, "Panel"), behaviour }, options, (1, 4242));
            var files = new List<(string, byte[], int)> { (node, file, 4) };
            files.AddRange(more);
            return Bundle(files.ToArray());
        }

        /// <summary>A file whose script table holds one (file id, path id) entry the behaviours index.</summary>
        static byte[] SerializedFileWithScriptTable(IList<Obj> objects, FileOptions options, (int fileId, long pathId) entry)
        {
            // The writer's table holds this file's scripts only; patch its single entry's file id in place.
            var bytes = SerializedFile(objects.Select(o => o.ClassId == 114 ? new Obj
            {
                PathId = o.PathId, ClassId = 114, Tree = o.Tree, Data = o.Data, Script = entry.pathId,
            } : o).ToList(), options);
            var marker = BitConverter.GetBytes(entry.pathId);
            for (var i = 48; i + 12 <= bytes.Length; i++)
            {
                if (bytes.Skip(i + 4).Take(8).SequenceEqual(marker) && BitConverter.ToInt32(bytes, i) == 0 && BitConverter.ToInt32(bytes, i - 4) == 1)
                {
                    BitConverter.GetBytes(entry.fileId).CopyTo(bytes, i);
                    return bytes;
                }
            }
            throw new InvalidOperationException("script table entry not found");
        }

        [Test]
        public void ReferencesToFilesOutsideTheBundleAreRefused()
        {
            AssertRefused(Check(ExternalScriptWorld("globalgamemanagers.assets")), BundleRule.ExternalFile);
            AssertRefused(Check(ExternalScriptWorld("archive:/CAB-00000000000000000000000000000000/CAB-00000000000000000000000000000000")), BundleRule.ExternalFile);
            // Our own file's name under another bundle's directory is that bundle's file.
            AssertRefused(Check(ExternalScriptWorld("archive:/CAB-attacker/BuildPlayer-Test")), BundleRule.ExternalFile);
        }

        [Test]
        public void BuiltinResourceScriptsAreJudgedAsLive()
        {
            // A script in Unity's built-in resources (UI Toolkit's live there): its calls are judged, not taken as inert.
            var verdict = Check(ExternalScriptWorld("Resources/unity default resources"));
            AssertRefused(verdict, BundleRule.CallNoTarget);
            Assert.IsTrue(verdict.Reports.Any(r => r.Rule == BundleRule.ExternalScript), verdict.Describe("test"));
        }

        [Test]
        public void OwnArchivePathsResolveOnlyUnderTheMount()
        {
            var options = new FileOptions();
            options.Externals.Add("archive:/BuildPlayer-Test/BuildPlayer-Test.sharedAssets");
            var scene = SerializedFile(new[] { GameObject(1, "Panel") }, options);
            var shared = SerializedFile(new[] { GameObject(1, "Shared") });
            AssertPassed(Check(Bundle(("BuildPlayer-Test", scene), ("BuildPlayer-Test.sharedAssets", shared))));
            var wrongCase = new FileOptions();
            wrongCase.Externals.Add("archive:/buildplayer-test/BuildPlayer-Test.sharedAssets");
            AssertRefused(Check(Bundle(("BuildPlayer-Test", SerializedFile(new[] { GameObject(1, "Panel") }, wrongCase)), ("BuildPlayer-Test.sharedAssets", shared))),
                BundleRule.ExternalFile);
        }

        // ------------------------------------------------------------------ tightening

        [Test]
        public void BundlesWithoutFilesOrAnAssetBundleObjectAreRefused()
        {
            var noAssetBundle = SerializedFile(new[] { GameObject(1, "A") }, new FileOptions { AssetBundleObject = false });
            AssertRefused(Check(Bundle(("BuildPlayer-Test", noAssetBundle))), BundleRule.Unsupported);
            AssertRefused(Check(Bundle(("GI/BuildPlayer-Test/aa/aa", new byte[64], 0))), BundleRule.Unsupported);
        }

        [Test]
        public void OnlyTestedSerializedFormatsAreRead()
        {
            AssertRefused(Check(Bundle(("BuildPlayer-Test", SerializedFile(new[] { GameObject(1, "A") }, new FileOptions { Format = 21 })))), BundleRule.Unsupported);
            AssertRefused(Check(Bundle(("BuildPlayer-Test", SerializedFile(new[] { GameObject(1, "A") }, new FileOptions { Format = 23 })))), BundleRule.Unsupported);
        }

        [Test]
        public void UnflaggedFilesMustBeResourcesOrGIData()
        {
            var scene = SerializedFile(new[] { GameObject(1, "A") });
            AssertPassed(Check(Bundle(("BuildPlayer-Test", scene, 4), ("BuildPlayer-Test.resource", new byte[64], 0), ("GI/BuildPlayer-Test/aa/aa", new byte[64], 0))));
            AssertRefused(Check(Bundle(("BuildPlayer-Test", scene, 4), ("BuildPlayer-Test.notes", new byte[64], 0))), BundleRule.Unsupported);
        }

        [Test]
        public void VersionsCarryTheCheckerBuild()
        {
            var versions = BundleCheckPolicy.Versions(BundleCheckOptions.World(), "e");
            StringAssert.StartsWith("c" + BundleCheckPolicy.CheckerVersion + "." + BundleCheckPolicy.CheckerBuild + "/", versions);
            Assert.AreEqual(8, BundleCheckPolicy.CheckerBuild.Length);
        }

        [Test]
        public void KitsRefuseTheNarrowPatterns()
        {
            Assert.AreEqual(BundleSeverity.Refuse, BundleCheckOptions.Kit().NarrowPatterns);
            Assert.AreEqual(BundleSeverity.Warn, BundleCheckOptions.World().NarrowPatterns);
        }

        // ------------------------------------------------------------------ should-fix: ScriptableObjects and references

        static byte[] ScriptableObjectWorld(string assembly, string ns, string cls) => Bundle(("CAB-0123456789abcdef", SerializedFile(new[]
        {
            Script(2, assembly, ns, cls),
            Behaviour(3, 2, MonoBehaviourBase(), w => { }, gameObject: 0),
        })));

        [Test]
        public void UnlistedScriptableObjectsAreRefusedExceptTheInputSystemAssets()
        {
            if (BundleTypeResolver.ResolveScript("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputActionAsset") == null) Assert.Ignore("Input System not loaded");
            AssertPassed(Check(ScriptableObjectWorld("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputActionAsset"), BundleCheckOptions.Kit()));
            AssertPassed(Check(ScriptableObjectWorld("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputActionReference"), BundleCheckOptions.Kit()));
            var settings = BundleTypeResolver.ResolveScript("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputSettings");
            if (settings != null)
                AssertRefused(Check(ScriptableObjectWorld("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputSettings"), BundleCheckOptions.Kit()), BundleRule.UnlistedScript);
        }

        [Test]
        public void ScriptLookupIsASupersetOfUnitys()
        {
            var button = BundleTypeResolver.ResolveScript("UnityEngine.UI.dll", "UnityEngine.UI", "Button");
            if (button == null) Assert.Ignore("UGUI not loaded");
            Assert.AreEqual(button, BundleTypeResolver.ResolveScript("Library/ScriptAssemblies/UnityEngine.UI.dll", "UnityEngine.UI", "Button"), "path-like assembly");
            Assert.AreEqual(button, BundleTypeResolver.ResolveScript("unityengine.ui.DLL", "UnityEngine.UI", "Button"), "assembly case");
            CollectionAssert.Contains(BundleTypeResolver.ResolveScriptCandidates("UnityEngine.UI.dll", "unityengine.ui", "BUTTON"), button, "type case");
            Assert.AreEqual("UnityEngine.UI", BundleTypeResolver.AssemblySimpleName("C:\\x\\UnityEngine.UI.dll"));
        }

        [Test]
        public void SerializeReferenceTypesMustBeContent()
        {
            var valueTree = new N("Probe", "Base", -1, false, Int("m_Value"));
            byte[] World(string cls, string ns, string asm)
            {
                var options = new FileOptions();
                options.RefTypes.Add((valueTree, cls, ns, asm));
                var tree = MonoBehaviourBase(new N("ManagedReferencesRegistry", "references", -1, false, Int("version")));
                return TreeWorld(tree, w =>
                {
                    w.I32(2); // registry version
                    w.I32(1); // one value
                    w.I64(1); // rid
                    w.Str(cls);
                    w.Str(ns);
                    w.Str(asm);
                    w.I32(7);
                }, options);
            }
            AssertPassed(Check(World("NoSuchType4242", "Nowhere", "Assembly-CSharp"))); // Unity cannot create it: inert
            AssertRefused(Check(World("BSLink", "BS", "BS.SDK")), BundleRule.HostReference);
            if (BundleTypeResolver.ResolveManaged("Unity.InputSystem", "UnityEngine.InputSystem", "InputActionMap") != null)
                AssertRefused(Check(World("InputActionMap", "UnityEngine.InputSystem", "Unity.InputSystem")), BundleRule.HostReference);
            AssertRefused(Check(World("Foo`1[[System.Int32, mscorlib]]", "Nowhere", "Assembly-CSharp")), BundleRule.HostReference);
        }

        // ------------------------------------------------------------------ B3: graph JSON

        static byte[] GraphBundle(string json) => Bundle(("BuildPlayer-Test", SerializedFile(new[]
        {
            GameObject(1, "Graph"), Script(2, "Unity.VisualScripting.Core.dll", "Unity.VisualScripting", "Variables"),
            Behaviour(3, 2, MonoBehaviourBase(new N("SerializationData", "_data", -1, false, Str("_json"), Vector("_objectReferences", PPtr("data")))),
                w => { w.Str(json); w.I32(0); }),
        })));

        const string deleteCall = "{\"m_Target\":null,\"m_TargetAssemblyTypeName\":\"System.IO.File, mscorlib\",\"m_MethodName\":\"Delete\",\"m_Mode\":5}";

        [Test]
        public void ContentWrappersAndReferencesDoNotHideCalls()
        {
            AssertRefused(Check(GraphBundle("{\"v\":{\"m_PersistentCalls\":{\"m_Calls\":{\"$content\":[" + deleteCall + "]}}}}")), BundleRule.GraphUnityEvent);
            AssertRefused(Check(GraphBundle("{\"v\":{\"m_PersistentCalls\":{\"m_Calls\":{\"$id\":\"7\",\"$content\":[" + deleteCall + "]}}}}")), BundleRule.GraphUnityEvent);
            AssertRefused(Check(GraphBundle("{\"v\":{\"m_PersistentCalls\":{\"$content\":{\"m_Calls\":[" + deleteCall + "]}}}}")), BundleRule.GraphUnityEvent);
            AssertRefused(Check(GraphBundle("{\"a\":{\"$id\":\"3\",\"$content\":[" + deleteCall + "]},\"v\":{\"m_PersistentCalls\":{\"m_Calls\":{\"$ref\":\"3\"}}}}")), BundleRule.GraphUnityEvent);
            AssertRefused(Check(GraphBundle("{\"v\":{\"$type\":\"System.Collections.Generic.List`1[[UnityEngine.Events.PersistentCall, UnityEngine.CoreModule]], mscorlib\",\"$content\":["
                                            + deleteCall + "]}}")), BundleRule.GraphType);
        }

        [Test]
        public void GraphTypesAreJudged()
        {
            if (BundleTypeResolver.ResolveSerializedType("Slash.Unity.DataBind.Core.Presentation.ContextHolder, Slash.Unity.DataBind")?.Length > 0)
                AssertRefused(Check(GraphBundle("{\"v\":{\"$type\":\"Slash.Unity.DataBind.Core.Presentation.ContextHolder, Slash.Unity.DataBind\",\"x\":1}}")), BundleRule.GraphType);
            AssertRefused(Check(GraphBundle("{\"v\":{\"$type\":\"BS.Security.BundleChecker\"}}")), BundleRule.GraphType);
            AssertRefused(Check(GraphBundle("{\"v\":{\"$content\":\"BS.BSLink\",\"$type\":\"System.RuntimeType\"}}")), BundleRule.GraphType);
            AssertRefused(Check(GraphBundle("{\"v\":{\"$type\":\"Unity.VisualScripting.MissingType\",\"formerType\":\"BS.Security.BundleChecker\",\"formerValue\":\"\"}}")),
                BundleRule.GraphType);
            AssertRefused(Check(GraphBundle("{\"v\":{\"$type\":\"Unity.VisualScripting.MissingType\",\"formerType\":\"X\",\"formerValue\":\"{\\\"m_PersistentCalls\\\":{\\\"m_Calls\\\":["
                                            + deleteCall.Replace("\"", "\\\"") + "]}}\"}}")), BundleRule.GraphUnityEvent);
            AssertRefused(Check(GraphBundle("{\"v\":{\"$type\":\"System.Type\\u0000x\"}}")), BundleRule.GraphType);
            // Plain data, collections, engine values and VS's own types pass; names nothing has are inert.
            AssertPassed(Check(GraphBundle("{\"a\":{\"$type\":\"System.Int32\",\"$content\":3},\"b\":{\"$type\":\"UnityEngine.Vector3\",\"x\":1},"
                                           + "\"c\":{\"$type\":\"System.Collections.Generic.List`1[[System.String, mscorlib]]\",\"$content\":[]},"
                                           + "\"d\":{\"$content\":\"UnityEngine.Transform\",\"$type\":\"System.RuntimeType\"},\"e\":{\"$type\":\"No.Such.Type4242\"}}")));
        }

        [Test]
        public void GraphJsonScannerAlwaysTerminatesAndMatchesTheParsersLimits()
        {
            foreach (var json in new[] { "{\"a\":1 /x }", "{\"a\":1 /}", "/", "{\"a\":/" })
            {
                BundleGraphJson.Result result = null;
                var thread = new Thread(() => result = BundleGraphJson.Scan(json)) { IsBackground = true };
                thread.Start();
                Assert.IsTrue(thread.Join(5000), "scan of " + json + " did not finish");
                Assert.IsTrue(result.Unreadable, json);
            }
            Assert.IsFalse(BundleGraphJson.Scan(new string('[', 128) + new string(']', 128) + " ").Unreadable, "128 levels parse");
            Assert.IsTrue(BundleGraphJson.Scan(new string('[', 129) + new string(']', 129) + " ").Unreadable, "129 do not (fsJsonParser.MaxDepth)");
            Assert.IsFalse(BundleGraphJson.Scan("{\"a\":1} /* trailing, unterminated").Unreadable, "after the root value, as fsJsonParser");
        }

        // ------------------------------------------------------------------ caps

        [Test]
        public void ResolverCachesStayBounded()
        {
            for (var i = 0; i < BundleTypeResolver.MaxCacheEntries + 50; i++) BundleTypeResolver.ResolveSerializedType("No.Such.Type" + i);
            Assert.LessOrEqual(BundleTypeResolver.CachedNames, 3 * BundleTypeResolver.MaxCacheEntries);
        }

        [Test]
        public void LongPersistentCallNamesAreRefused()
        {
            AssertRefused(Check(ButtonWorld(new Call { Method = new string('a', BundleChecker.MaxMethodNameBytes + 1) })), BundleRule.BadName);
            AssertRefused(Check(ButtonWorld(new Call { Method = "SetActive\0Destroy", Mode = 6 })), BundleRule.BadName);
        }
    }
}
