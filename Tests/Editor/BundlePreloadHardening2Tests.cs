using System.Linq;
using BS.Security;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Round 2 of the pre-load check's hardening (the reviewer's R1-R6 and M1): judged field names in another case,
    /// MonoScripts whose assembly name is empty or wrong, the only node layouts Unity writes, and missing-script call
    /// targets judged as both native classes. Built with the fixture writer in BundlePreloadCheckTests.
    /// </summary>
    public partial class BundlePreloadCheckTests
    {
        static N Renamed(N node, string name)
        {
            node.Name = name;
            return node;
        }

        // ------------------------------------------------------------------ Case variants of judged names

        [Test]
        public void CaseVariantsOfAJudgedGroupAreRefusedWhereTheDataIsLive()
        {
            foreach (var variant in new[] { "m_persistentCalls", "M_PersistentCalls", "m_PERSISTENTCALLS" })
            {
                var tree = MonoBehaviourBase(new N("ButtonClickedEvent", "m_OnClick", -1, false, Renamed(PersistentCalls(), variant)));
                AssertRefused(Check(TreeWorld(tree, w => WriteCalls(w, new[] { staticDelete }))), BundleRule.TypeTreeMismatch);
            }
            // In a script the app does not have, the data is never deserialized: inert.
            var inertTree = MonoBehaviourBase(new N("ButtonClickedEvent", "m_OnClick", -1, false, Renamed(PersistentCalls(), "m_persistentCalls")));
            var inert = Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Panel"),
                Script(2, "Assembly-CSharp.dll", "", "NoSuchAuthorScript4242"),
                Behaviour(3, 2, inertTree, w => WriteCalls(w, new[] { staticDelete })),
            })));
            AssertPassed(Check(inert));
        }

        [Test]
        public void CaseVariantsOfJudgedStringsAreRefused()
        {
            foreach (var variant in new[] { "methodName", "FunctionName", "_JSON" })
            {
                var tree = MonoBehaviourBase(new N("Holder", "m_Holder", -1, false, Str(variant)));
                AssertRefused(Check(TreeWorld(tree, w => w.Str("Destroy"))), BundleRule.TypeTreeMismatch);
            }
        }

        [Test]
        public void CaseVariantsOfBaseAndIdentityFieldsAreRefused()
        {
            var script = MonoBehaviourBase(UnityEvent("m_OnClick"));
            script.Children[2].Name = "m_script";
            AssertRefused(Check(TreeWorld(script, w => WriteCalls(w, new Call[0]))), BundleRule.TypeTreeMismatch);
            var gameObject = MonoBehaviourBase(UnityEvent("m_OnClick"));
            gameObject.Children[0].Name = "M_GameObject";
            AssertRefused(Check(TreeWorld(gameObject, w => WriteCalls(w, new Call[0]))), BundleRule.TypeTreeMismatch);

            var monoScript = new N("MonoScript", "Base", -1, false, Str("m_Name"), Int("m_ExecutionOrder"), Str("m_className"), Str("m_Namespace"), Str("m_AssemblyName"));
            var bundle = Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Panel"),
                new Obj { PathId = 2, ClassId = 115, Tree = monoScript, Data = w => { w.Str("Button"); w.I32(0); w.Str("Button"); w.Str("UnityEngine.UI"); w.Str("UnityEngine.UI.dll"); } },
                Behaviour(3, 2, buttonTree, w => WriteCalls(w, new Call[0])),
            })));
            AssertRefused(Check(bundle), BundleRule.TypeTreeMismatch);

            var registry = MonoBehaviourBase(new N("managedReferencesRegistry", "references", -1, false, Int("version")));
            AssertRefused(Check(TreeWorld(registry, w => { w.I32(2); w.I32(0); })), BundleRule.TypeTreeMismatch);
        }

        // ------------------------------------------------------------------ S2: assembly names that find nothing

        [Test]
        public void ScriptsWhoseAssemblyNameFindsNothingAreLookedUpEverywhere()
        {
            AssertRefused(Check(ButtonWorld("", "BS", "DontDestroyOnLoad")), BundleRule.DeniedScript);
            AssertRefused(Check(ButtonWorld("No.Such.Assembly.dll", "BS", "DontDestroyOnLoad")), BundleRule.DeniedScript);
            // The named assembly exists but holds no such class (Unity could look further).
            AssertRefused(Check(ButtonWorld("UnityEngine.UI.dll", "BS", "DontDestroyOnLoad")), BundleRule.DeniedScript);
            CollectionAssert.IsNotEmpty(BundleTypeResolver.ResolveScriptCandidates("", "UnityEngine.UI", "Button"));
            // A class no assembly has is still a missing script: inert.
            AssertPassed(Check(ButtonWorld("Assembly-CSharp.dll", "", "NoSuchAuthorScript4242", new Call { Target = 0, TypeName = "System.IO.File, mscorlib", Method = "Delete", Mode = 5 })));
        }

        // ------------------------------------------------------------------ S3: the node layouts Unity writes

        const string Cab = "CAB-0123456789abcdef0123456789abcdef";

        [Test]
        public void OnlyUnitysNodeLayoutsAreAccepted()
        {
            var file = SerializedFile(new[] { GameObject(1, "A") });
            var payload = new byte[64];
            AssertPassed(Check(Bundle((Cab, file, 4), (Cab + ".resS", payload, 0), (Cab + ".resource", payload, 0)), BundleCheckOptions.Kit()));
            AssertPassed(Check(Bundle(("BuildPlayer-My Scene_1", file, 4), ("BuildPlayer-My Scene_1.sharedAssets", file, 4), ("BuildPlayer-My Scene_1.resource", payload, 0),
                ("GI/BuildPlayer-My Scene_1/9c/0123456789abcdef0123456789abcdef.caw", payload, 0))));

            void Refused(string why, params (string, byte[], int)[] files)
            {
                var verdict = Check(Bundle(files), BundleCheckOptions.Kit());
                Assert.IsFalse(verdict.Passed, why + ": " + verdict.Describe("test"));
                Assert.IsTrue(verdict.Reasons.Any(r => r.Detail.StartsWith("bundle layout")), why + ": " + verdict.Describe("test"));
            }
            Refused("a serialized file named like neither a CAB nor a scene", ("notes", file, 4));
            Refused("a CAB name that is not hex", ("CAB-xyz", file, 4));
            Refused("two CABs", (Cab, file, 4), ("CAB-fedcba9876543210fedcba9876543210", file, 4));
            Refused("a kit with a sharedAssets file", (Cab, file, 4), (Cab + ".sharedAssets", file, 4));
            Refused("a payload of another name", (Cab, file, 4), (Cab + ".bin", payload, 0));
            Refused("a payload named after another file", (Cab, file, 4), ("CAB-fedcba9876543210fedcba9876543210.resS", payload, 0));
            Refused("GI data of another scene", ("BuildPlayer-A", file, 4), ("BuildPlayer-A.sharedAssets", file, 4), ("GI/BuildPlayer-B/9c/00ff.caw", payload, 0));
            Refused("GI data in a kit", (Cab, file, 4), ("GI/" + Cab + "/9c/00ff.caw", payload, 0));
        }

        // ------------------------------------------------------------------ M1: missing-script targets

        [Test]
        public void AMissingScriptTargetIsJudgedAsBothNativeClasses()
        {
            // The target has no GameObject and a script the app lacks: natively a MonoBehaviour all the same, so
            // StartCoroutine(string) (by-name dispatch) binds on it.
            var bundle = Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Panel"),
                Script(2, "UnityEngine.UI.dll", "UnityEngine.UI", "Button"),
                Script(4, "Assembly-CSharp.dll", "", "NoSuchAuthorScript4242"),
                Behaviour(5, 4, MonoBehaviourBase(), w => { }, gameObject: 0),
                Behaviour(3, 2, buttonTree, w => WriteCalls(w, new[] { new Call { Target = 5, TypeName = "", Method = "StartCoroutine", Mode = 5 } })),
            })));
            AssertRefused(Check(bundle), BundleRule.CallByName);
        }

        // ------------------------------------------------------------------ hide and persistence flags

        static byte[] HiddenWorld(uint flags, string fieldName = "m_ObjectHideFlags") => Bundle(("BuildPlayer-Test", SerializedFile(new[]
        {
            new Obj
            {
                PathId = 1, ClassId = 1, Tree = new N("GameObject", "Base", -1, false, new N("unsigned int", fieldName, 4), Str("m_Name")),
                Data = w => { w.I32((int)flags); w.Str("Hidden"); },
            },
        })));

        [Test]
        public void HidingAndPersistenceFlagsAreRefused()
        {
            foreach (var flags in new uint[] { 1, 4, 16, 32, 52, 61 })
                AssertRefused(Check(HiddenWorld(flags)), BundleRule.HideFlags);
            AssertPassed(Check(HiddenWorld(0)));
            AssertPassed(Check(HiddenWorld(2))); // HideInInspector: no effect in a player
            AssertRefused(Check(HiddenWorld(61, "m_objectHideFlags")), BundleRule.TypeTreeMismatch);
        }

        // ------------------------------------------------------------------ structural refusals

        [Test]
        public void StructuralRefusalsAreMarked()
        {
            Assert.IsTrue(BundleRules.IsStructural(BundleRule.Malformed));
            Assert.IsTrue(BundleRules.IsStructural(BundleRule.TooLarge));
            Assert.IsTrue(BundleRules.IsStructural(BundleRule.TypeTreeShape));
            Assert.IsFalse(BundleRules.IsStructural(BundleRule.TypeTreeMismatch));
            Assert.IsFalse(BundleRules.IsStructural(BundleRule.CallNoTarget));
            // An extra child in a PPtr (the shape that crashed Unity's SafeBinaryRead) is structural...
            var extraChild = MonoBehaviourBase(UnityEvent("m_OnClick"));
            extraChild.Children[0].Children.Add(Int("m_Extra"));
            Assert.IsTrue(Check(TreeWorld(extraChild, w => WriteCalls(w, new Call[0]))).RefusedStructurally);
            // ...a target-less static call is policy.
            var policy = Check(TreeWorld(buttonTree, w => WriteCalls(w, new[] { staticDelete })));
            Assert.IsFalse(policy.Passed);
            Assert.IsFalse(policy.RefusedStructurally);
        }

        // ------------------------------------------------------------------ graph type messages

        [Test]
        public void GraphTypeRefusalsNameTheReason()
        {
            var verdict = BundleGraphTypes.Judge("System.Threading.Thread, mscorlib", false, out var detail);
            Assert.AreEqual(BundleGraphTypes.Verdict.Refused, verdict);
            StringAssert.Contains("deserialization backstop", detail);
        }
    }
}
