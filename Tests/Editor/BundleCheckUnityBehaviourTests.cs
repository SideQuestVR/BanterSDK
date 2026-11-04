using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BS.Security;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using static BS.SDKEditor.Tests.BundleProbeEditor;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Unity-behaviour experiments for the pre-load bundle check's hardening round: what Unity ACTUALLY reads from the
    /// non-canonical type trees the checker now refuses (the reviewer's A1-A15 forms), so the refusals rest on evidence.
    /// Each test builds small real bundles (BuildPipeline, uncompressed, into Temp/BundleProbe), rewrites them in memory
    /// with the minimal format-22 editor below, loads them with AssetBundle.LoadFromMemory and logs
    /// "[BundleProbe] Un: ..." facts. They assert only that the unmodified control loads; every finding is a log line.
    /// </summary>
    /// <remarks>
    /// <para>The guard and the pre-load check stand aside during the loads (BSUnityEventGuard.DisabledForTests, restored
    /// in TearDown), so the bytes reach Unity as written. The temp prefabs (Assets/__BundleProbeTemp__) and built bundles
    /// are deleted in TearDown; nothing modified ever touches disk. Bundles are built into Temp/BundleProbe/out-sources:
    /// BuildPipeline names its manifest bundle after the output folder, which must not match a bundle name.</para>
    /// <para>The editor (BundleProbeEditor) is round-trip tested on its own: U0 rewrites a built bundle with no edit and
    /// requires Unity to load it exactly like the original. Every experiment checks the same first and is Inconclusive
    /// (not a false finding) when the rewriter's output does not load. Unity's error logs during the experiments' loads
    /// are recorded, not failures (LogAssert.ignoreFailingMessages, per test).</para>
    /// <para>Editor evidence only: a player can take other paths (IL2CPP reflection, the type-tree fast path), so a fact that
    /// matters should be confirmed in a development player before it is relied on. The older layout-hash probe is
    /// BundleTypeTreeProbeTests (same question as U2, on a LayoutElement).</para>
    /// </remarks>
    [Category("BundleCheckUnityBehaviour")]
    public class BundleCheckUnityBehaviourTests
    {
        const string TempFolderName = "__BundleProbeTemp__";
        const string TempFolder = "Assets/" + TempFolderName;
        const string OutputDir = "Temp/BundleProbe";

        static void Probe(string id, string fact) => Debug.Log("[BundleProbe] " + id + ": " + fact);

        bool guardWas;
        readonly List<AssetBundle> loaded = new List<AssetBundle>();

        [SetUp]
        public void SetUp()
        {
            guardWas = BSUnityEventGuard.DisabledForTests;
            BSUnityEventGuard.DisabledForTests = true;
            // The experiments load bundles Unity may refuse with an error log: that is a result to record, not a test
            // failure. Scoped to this test (reset in TearDown); each test asserts its own controls explicitly.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var bundle in loaded)
            {
                try
                {
                    if (bundle != null) bundle.Unload(true);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[BundleProbe] unload: " + e.Message);
                }
            }
            loaded.Clear();
            BSUnityEventGuard.DisabledForTests = guardWas;
            LogAssert.ignoreFailingMessages = false;
            if (AssetDatabase.IsValidFolder(TempFolder) && !AssetDatabase.DeleteAsset(TempFolder))
                Debug.LogError("[BundleProbe] could not delete " + TempFolder);
            if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
        }

        // ------------------------------------------------------------------ building the sources

        /// <summary>
        /// Builds the probe bundles: "probea" (a Button whose onClick calls GameObject.SetActive(true), plus an Image, an
        /// AudioSource and a Toggle sibling object), "probeb" (a Toggle, for the cross-bundle script), and when Unity.Splines
        /// is present "probec" (a SplineExtrude with its [SerializeReference] shape).
        /// </summary>
        static Dictionary<string, byte[]> BuildSources()
        {
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.CreateFolder("Assets", TempFolderName);
            var builds = new List<AssetBundleBuild>();

            var a = new GameObject("ProbeButton", typeof(RectTransform));
            try
            {
                a.AddComponent<Image>();
                var button = a.AddComponent<Button>();
                UnityEventTools.AddBoolPersistentListener(button.onClick, a.SetActive, true);
                a.AddComponent<AudioSource>();
                var toggleChild = new GameObject("ToggleChild", typeof(RectTransform));
                toggleChild.transform.SetParent(a.transform, false);
                toggleChild.AddComponent<Toggle>();
                PrefabUtility.SaveAsPrefabAsset(a, TempFolder + "/ProbeButton.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);
            }
            builds.Add(new AssetBundleBuild { assetBundleName = "probea", assetNames = new[] { TempFolder + "/ProbeButton.prefab" } });

            var b = new GameObject("ProbeToggle", typeof(RectTransform));
            try
            {
                b.AddComponent<Toggle>();
                PrefabUtility.SaveAsPrefabAsset(b, TempFolder + "/ProbeToggle.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(b);
            }
            builds.Add(new AssetBundleBuild { assetBundleName = "probeb", assetNames = new[] { TempFolder + "/ProbeToggle.prefab" } });

            var extrudeType = Type.GetType("UnityEngine.Splines.SplineExtrude, Unity.Splines");
            if (extrudeType != null)
            {
                var c = new GameObject("ProbeSpline");
                try
                {
                    c.AddComponent(Type.GetType("UnityEngine.Splines.SplineContainer, Unity.Splines"));
                    c.AddComponent<MeshFilter>();
                    c.AddComponent<MeshRenderer>();
                    c.AddComponent(extrudeType);
                    PrefabUtility.SaveAsPrefabAsset(c, TempFolder + "/ProbeSpline.prefab");
                    builds.Add(new AssetBundleBuild { assetBundleName = "probec", assetNames = new[] { TempFolder + "/ProbeSpline.prefab" } });
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(c);
                }
            }

            // The manifest bundle is named after the output folder: "out-sources" matches no bundle name.
            var output = Path.Combine(OutputDir, "out-sources");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            var target = EditorUserBuildSettings.activeBuildTarget;
            var manifest = BuildPipeline.BuildAssetBundles(output, builds.ToArray(),
                BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ForceRebuildAssetBundle, target);
            Assert.IsNotNull(manifest, "BuildPipeline.BuildAssetBundles failed for " + target);
            var result = new Dictionary<string, byte[]>();
            foreach (var build in builds) result[build.assetBundleName] = File.ReadAllBytes(Path.Combine(output, build.assetBundleName));
            return result;
        }

        // ---- shapes that can crash Unity itself. A fatal error aborts the whole batch (every later test), so these load
        // only in a session started for them: BUNDLEPROBE_RISKY=1 in the environment. Otherwise they are logged, not loaded.

        const string KnownCrash =
            "NOT LOADED (known: crashed Unity 6000.3.21f1, run r4: 'Size overflow in allocator' in SafeBinaryRead::TransferSTLStyleArray<string> "
            + "from MonoBehaviour::Transfer in LoadAllAssets. Unity reads a PPtr as exactly {m_FileID, m_PathID} whatever children the tree "
            + "lists, so the extra bytes shift every later field and a shifted string length overflows the allocator)";

        const string SameShapeAsTheCrash =
            "NOT LOADED (an extra child inside a PPtr, the shape that crashed Unity in A3; set BUNDLEPROBE_RISKY=1 to load it in a session of its own)";

        const string KnownRetypeCrash =
            "NOT LOADED (known: crashed Unity 6000.3.21f1, run r5: 'The file archive:/CAB-... is corrupted' then CachedReader::OutOfBoundsError "
            + "in Transfer_Blittable_SingleValueField<StreamedBinaryRead> from SerializeTraits<ReferencedObjectData> / ManagedReferencesTransferState / "
            + "TransferScriptingObjectWithSerializedRefSupport<StreamedBinaryRead>. The host's stored hash is the app's, so Unity streams the "
            + "registry with the app's layouts and ignores the stored ref type tree: a value of a class its data was not written for reads out of bounds)";

        const string UntestedPPtrShape =
            "NOT LOADED (a duplicate PPtr field, never loaded yet; set BUNDLEPROBE_RISKY=1 to load it in a session of its own)";

        static bool RiskyShapes => Environment.GetEnvironmentVariable("BUNDLEPROBE_RISKY") == "1";

        /// <summary>Runs an experiment that may crash Unity only when BUNDLEPROBE_RISKY=1; else logs why it was not loaded.</summary>
        static void Risky(string id, string label, string notLoaded, Func<string> run) => Probe(id, label + (RiskyShapes ? run() : notLoaded));

        /// <summary>
        /// Whether the editor's unmodified rewrite of <paramref name="bundle"/> loads exactly like the original; when it does
        /// not, the experiment is Inconclusive (the rewriter is broken, see U0), never a finding about Unity.
        /// </summary>
        void RequireRewritable(byte[] bundle)
        {
            var original = Describe(bundle);
            var rewritten = Describe(RoundTrip(bundle));
            if (rewritten != original)
                Assert.Inconclusive("the rewriter's unmodified output does not load like the original (see U0): original " + original + "; rewritten " + rewritten);
        }

        // ------------------------------------------------------------------ loading

        /// <summary>Loads bundle bytes (kept loaded until TearDown or <see cref="Unload"/>); null when Unity refuses them.</summary>
        AssetBundle Load(byte[] bytes, out string note)
        {
            note = "";
            AssetBundle bundle;
            try
            {
                bundle = AssetBundle.LoadFromMemory(bytes);
            }
            catch (Exception e)
            {
                note = " (LoadFromMemory threw " + e.GetType().Name + ")";
                return null;
            }
            if (bundle == null)
            {
                note = " (LoadFromMemory refused the bundle)";
                return null;
            }
            loaded.Add(bundle);
            return bundle;
        }

        void Unload(AssetBundle bundle)
        {
            if (bundle == null) return;
            loaded.Remove(bundle);
            bundle.Unload(true);
        }

        /// <summary>Loads bundle A (unloading it again) and describes what the probe prefab's components became.</summary>
        string Describe(byte[] bytes)
        {
            var bundle = Load(bytes, out var note);
            if (bundle == null) return "not loaded" + note;
            try
            {
                var prefabs = bundle.LoadAllAssets<GameObject>();
                var root = prefabs.FirstOrDefault(p => p.name == "ProbeButton");
                if (root == null) return "no ProbeButton prefab (" + prefabs.Length + " prefabs)";
                var text = new StringBuilder("components [");
                foreach (var component in root.GetComponents<Component>())
                    text.Append(component == null ? "<missing script>" : component.GetType().FullName).Append("; ");
                text.Append("]");
                foreach (var behaviour in root.GetComponents<MonoBehaviour>())
                {
                    if (behaviour is Selectable selectable && !(behaviour is Toggle))
                    {
                        var events = EventsOf(behaviour);
                        text.Append(" ").Append(behaviour.GetType().Name).Append(events);
                    }
                }
                return text.ToString();
            }
            finally
            {
                Unload(bundle);
            }
        }

        /// <summary>A Button's onClick as Unity loaded it: call count, method names, targets and modes (modes by reflection).</summary>
        static string EventsOf(MonoBehaviour behaviour)
        {
            if (!(behaviour is Button button)) return "";
            var onClick = button.onClick;
            var count = onClick.GetPersistentEventCount();
            var text = new StringBuilder(".onClick: " + count + " call(s)");
            for (var i = 0; i < count; i++)
            {
                var target = onClick.GetPersistentTarget(i);
                text.Append(" [").Append(onClick.GetPersistentMethodName(i)).Append(" on ").Append(target == null ? "null" : target.GetType().Name)
                    .Append(", mode ").Append(ModeOf(onClick, i)).Append("]");
            }
            return text.ToString();
        }

        static string ModeOf(UnityEngine.Events.UnityEventBase unityEvent, int index)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var group = typeof(UnityEngine.Events.UnityEventBase).GetField("m_PersistentCalls", flags)?.GetValue(unityEvent);
                var calls = group?.GetType().GetField("m_Calls", flags)?.GetValue(group) as IList;
                var call = calls?[index];
                return call?.GetType().GetField("m_Mode", flags)?.GetValue(call)?.ToString() ?? "?";
            }
            catch (Exception e)
            {
                return "? (" + e.GetType().Name + ")";
            }
        }

        // ------------------------------------------------------------------ the experiments

        /// <summary>The rewriter itself: a bundle written back with no edit must load exactly like the original.</summary>
        [Test]
        public void U0_RewriterRoundTripLoadsLikeTheOriginal()
        {
            var a = BuildSources()["probea"];
            var original = Describe(a);
            StringAssert.Contains("SetActive", original, "the unmodified bundle must load its call");
            var rewritten = RoundTrip(a);
            var described = Describe(rewritten);
            Probe("U0", "round trip (" + a.Length + " -> " + rewritten.Length + " bytes): original " + original + " | rewritten " + described);
            Assert.AreEqual(original, described, "the rewriter's unmodified output must load exactly like the original");
            var checkedRewrite = BundleChecker.Check(rewritten, BundleCheckOptions.Kit(), "u0");
            Assert.IsTrue(checkedRewrite.Passed, "the unmodified rewrite must pass the checker too: " + checkedRewrite.Describe("u0"));
        }

        [Test]
        public void U1_NonCanonicalTreesAsUnityReadsThem()
        {
            var sources = BuildSources();
            var a = sources["probea"];
            var control = Describe(a);
            Probe("U1", "control (Unity " + Application.unityVersion + ", " + EditorUserBuildSettings.activeBuildTarget + "): " + control);
            StringAssert.Contains("SetActive", control, "the unmodified bundle must load its call");
            RequireRewritable(a);

            foreach (var zeroHash in new[] { false, true })
            {
                var suffix = zeroHash ? " [type hash zeroed]" : " [type hash kept]";
                foreach (var marker in new[] { "functionName", "MethodName", "_json" })
                {
                    Probe("U1", "A1 extra string '" + marker + "' at the end of m_OnClick" + suffix + ": " + Describe(Edit(a, (file, button) =>
                    {
                        var type = file.Types[button.Object.TypeIndex];
                        var onClick = type.Find("m_OnClick");
                        var end = type.SubtreeEnd(onClick);
                        type.InsertString(end, type.Nodes[onClick].Level + 1, marker);
                        file.InsertData(button.Object, button.EndOf(type, "m_OnClick"), new byte[4]);
                        if (zeroHash) type.ZeroHashes();
                    })));
                }
                Probe("U1", "A2 m_Script with an extra child (int m_Extra)" + suffix + ": " + Describe(Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    var script = type.Find("m_Script", 1);
                    type.InsertPrimitive(script + 3, 2, "int", "m_Extra", 4);
                    file.InsertData(button.Object, 28, new byte[4]);
                    if (zeroHash) type.ZeroHashes();
                })));
                Probe("U1", "A2b m_Script with m_PathID before m_FileID" + suffix + ": " + Describe(Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    var script = type.Find("m_Script", 1);
                    var fileId = type.Nodes[script + 1];
                    type.Nodes[script + 1] = type.Nodes[script + 2];
                    type.Nodes[script + 2] = fileId;
                    var data = button.Object.Data;
                    var swapped = new byte[12];
                    Buffer.BlockCopy(data, 20, swapped, 0, 8);
                    Buffer.BlockCopy(data, 16, swapped, 8, 4);
                    Buffer.BlockCopy(swapped, 0, data, 16, 12);
                    if (zeroHash) type.ZeroHashes();
                })));
                Risky("U1", "A3 m_GameObject with an extra child" + suffix + ": ", KnownCrash, () => Describe(Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    var gameObject = type.Find("m_GameObject", 1);
                    type.InsertPrimitive(gameObject + 3, 2, "int", "m_Extra", 4);
                    file.InsertData(button.Object, 12, new byte[4]);
                    if (zeroHash) type.ZeroHashes();
                })));
                Risky("U1", "A4 second m_Script naming the Image script" + suffix + ": ", UntestedPPtrShape, () => Describe(Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    var script = type.Find("m_Script", 1);
                    type.Nodes.InsertRange(script + 3, type.Nodes.Skip(script).Take(3).Select(n => n.Clone()).ToList());
                    var image = file.ScriptPathId("Image");
                    var copy = new byte[12];
                    BitConverter.GetBytes(0).CopyTo(copy, 0);
                    BitConverter.GetBytes(image).CopyTo(copy, 4);
                    file.InsertData(button.Object, 28, copy);
                    if (zeroHash) type.ZeroHashes();
                })));
                Risky("U1", "A12 m_Target with an extra child" + suffix + ": ", SameShapeAsTheCrash, () => Describe(Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    var target = type.FindPath("m_OnClick/m_PersistentCalls/m_Calls/Array/data/m_Target");
                    type.InsertPrimitive(target + 3, type.Nodes[target].Level + 1, "int", "m_Extra", 4);
                    file.InsertData(button.Object, button.EndOf(type, "m_OnClick/m_PersistentCalls/m_Calls/Array/[0]/m_Target"), new byte[4]);
                    if (zeroHash) type.ZeroHashes();
                })));
            }
            foreach (var zeroHash in new[] { false, true })
            {
                // Both identity fields present: the first named m_ClassName says Toggle, the second (the real one) Button.
                Probe("U1", "A15 MonoScript with two m_ClassName fields (first Toggle, second Button)" + (zeroHash ? " [type hash zeroed]" : " [type hash kept]") + ": "
                            + Describe(Edit(a, (file, button) =>
                            {
                                var script = file.Script("Button");
                                var type = file.Types[script.TypeIndex];
                                type.Nodes[type.Find("m_Name", 1)].NameOffset = type.AddString("m_ClassName");
                                file.SetFirstString(script, "Toggle");
                                if (zeroHash) type.ZeroHashes();
                            })));
            }
        }

        [Test]
        public void U2_TypeTreeFastPathWithAKeptScriptHash()
        {
            var a = BuildSources()["probea"];
            RequireRewritable(a);
            foreach (var zeroHash in new[] { false, true })
            {
                Probe("U2", "m_PersistentCalls renamed m_PersistentCallz" + (zeroHash ? ", type hash zeroed" : ", script and type hash kept") + ": " + Describe(Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    type.Nodes[type.Find("m_PersistentCalls")].NameOffset = type.AddString("m_PersistentCallz");
                    if (zeroHash) type.ZeroHashes();
                })) + " -> 1 call: Unity read by its own layout (trusted the hash); 0 calls: it read by the bundle's tree");
            }
        }

        [Test]
        public void U3_FloatModeConversion()
        {
            var a = BuildSources()["probea"];
            RequireRewritable(a);
            foreach (var zeroHash in new[] { false, true })
            {
                Probe("U3", "m_Mode typed float holding 6.0" + (zeroHash ? " [type hash zeroed]" : " [type hash kept]") + ": " + Describe(Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    var mode = type.FindPath("m_OnClick/m_PersistentCalls/m_Calls/Array/data/m_Mode");
                    type.Nodes[mode].TypeOffset = type.AddString("float");
                    var at = button.StartOf(type, "m_OnClick/m_PersistentCalls/m_Calls/Array/[0]/m_Mode");
                    var value = BitConverter.ToInt32(button.Object.Data, at);
                    BitConverter.GetBytes((float)value).CopyTo(button.Object.Data, at);
                    if (zeroHash) type.ZeroHashes();
                })) + " (built with mode Bool = 6)");
            }
        }

        [Test]
        public void U4_NulInAMonoScriptIdentity()
        {
            var a = BuildSources()["probea"];
            RequireRewritable(a);
            Probe("U4", "Button's MonoScript m_ClassName 'Text\\0\\0' (same length): " + Describe(Edit(a, (file, button) =>
            {
                var script = file.Script("Button");
                file.SetString(script, "m_ClassName", "Text\0\0");
            })) + " -> a Text component means Unity cut the name at NUL");
        }

        [Test]
        public void U5_ScriptsInOtherFiles()
        {
            var sources = BuildSources();
            var a = sources["probea"];
            RequireRewritable(a);
            Probe("U5", "m_Script -> globalgamemanagers.assets #1: " + Describe(Edit(a, (file, button) =>
            {
                var index = file.AddExternal("globalgamemanagers.assets");
                file.SetPPtr(button.Object, 16, index, 1);
                file.SetTypeScript(button.Object, index, 1);
            })));
            var b = sources["probeb"];
            var bRaw = RawBundle.Parse(b);
            var bFile = bRaw.Files.First(f => f.Flags == 4);
            var toggle = bFile.ScriptPathId("Toggle");
            var mount = bRaw.Mount;
            var bundleB = Load(b, out var noteB);
            if (bundleB == null)
            {
                Assert.Inconclusive("bundle B did not load" + noteB);
                return;
            }
            try
            {
                Probe("U5", "m_Script -> archive:/" + mount + "/" + bFile.Name + " #" + toggle + " (B's Toggle script, B loaded): " + Describe(Edit(a, (file, button) =>
                {
                    var index = file.AddExternal("archive:/" + mount + "/" + bFile.Name);
                    file.SetPPtr(button.Object, 16, index, toggle);
                    file.SetTypeScript(button.Object, index, toggle);
                })));
                Probe("U5", "m_Script -> archive:/" + mount + "/<A's own file name> #" + toggle + " (the leaf trick, B loaded): " + Describe(Edit(a, (file, button) =>
                {
                    var index = file.AddExternal("archive:/" + mount + "/" + file.Name);
                    file.SetPPtr(button.Object, 16, index, toggle);
                    file.SetTypeScript(button.Object, index, toggle);
                })));
            }
            finally
            {
                Unload(bundleB);
            }
        }

        [Test]
        public void U6_AComponentWhoseGameObjectIsNull()
        {
            var a = BuildSources()["probea"];
            RequireRewritable(a);
            Probe("U6", "Button's m_GameObject set to null (the GameObject still lists it): " + Describe(Edit(a, (file, button) => file.SetPPtr(button.Object, 0, 0, 0)))
                        + " -> Button still listed: Unity attaches by the GameObject's list, not m_GameObject");
        }

        [Test]
        public void U7_ASpoofedRootName()
        {
            var a = BuildSources()["probea"];
            RequireRewritable(a);
            Probe("U7", "AudioSource's tree root named 'GameObject': " + Describe(Edit(a, (file, button) =>
            {
                var source = file.Objects.First(o => file.Types[o.TypeIndex].ClassId == 82);
                var type = file.Types[source.TypeIndex];
                type.Nodes[0].TypeOffset = type.AddString("GameObject");
            })) + " -> AudioSource still listed: the class id decides");
            Probe("U7", "AssetBundle object's tree root named 'GameObject': " + Describe(Edit(a, (file, button) =>
            {
                var bundle = file.Objects.First(o => file.Types[o.TypeIndex].ClassId == 142);
                var type = file.Types[bundle.TypeIndex];
                type.Nodes[0].TypeOffset = type.AddString("GameObject");
            })));
        }

        [Test]
        public void U10_SerializeReferenceValuesNoFieldCanHold()
        {
            var sources = BuildSources();
            if (!sources.TryGetValue("probec", out var c))
            {
                Assert.Inconclusive("Unity.Splines is not in this project");
                return;
            }
            var utility = Type.GetType("UnityEngine.Serialization.ManagedReferenceUtility, UnityEngine.CoreModule");
            var get = utility?.GetMethod("GetManagedReference", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(UnityEngine.Object), typeof(long) }, null);
            string Inspect(byte[] bytes, long rid)
            {
                var bundle = Load(bytes, out var note);
                if (bundle == null) return "not loaded" + note;
                try
                {
                    var prefab = bundle.LoadAllAssets<GameObject>().FirstOrDefault(p => p.name == "ProbeSpline");
                    var extrude = prefab == null ? null : prefab.GetComponent(Type.GetType("UnityEngine.Splines.SplineExtrude, Unity.Splines"));
                    if (extrude == null) return "no SplineExtrude";
                    var shape = extrude.GetType().GetProperty("Shape")?.GetValue(extrude);
                    object byId = null;
                    try
                    {
                        byId = get?.Invoke(null, new object[] { extrude, rid });
                    }
                    catch (Exception e)
                    {
                        byId = "(" + (e.InnerException ?? e).GetType().Name + ")";
                    }
                    return "Shape = " + (shape == null ? "null" : shape.GetType().FullName) + ", reference " + rid + " = " + (byId == null ? "null" : byId is string s ? s : byId.GetType().FullName);
                }
                finally
                {
                    Unload(bundle);
                }
            }
            var roundTrip = Inspect(RoundTrip(c), 0);
            if (roundTrip != Inspect(c, 0)) Assert.Inconclusive("the rewriter's unmodified output does not load like the original (see U0): " + roundTrip);
            var raw = RawBundle.Parse(c);
            var file = raw.Files.First(f => f.Flags == 4);
            var extrudeObject = file.Behaviour("SplineExtrude");
            var registry = extrudeObject.StartOf(file.Types[extrudeObject.Object.TypeIndex], "references");
            var (version, count, firstRid, firstEntry) = RawFile.ReadRegistry(extrudeObject.Object.Data, registry);
            Probe("U10", "control: registry version " + version + ", " + count + " value(s); " + Inspect(c, firstRid));
            Probe("U10", "an extra registry value (rid 4242, a copy of the first) that no field references: " + Inspect(Edit(c, (f, _) =>
            {
                var behaviour = f.Behaviour("SplineExtrude");
                var data = behaviour.Object.Data;
                var entry = data.Skip(firstEntry).ToArray();
                BitConverter.GetBytes(4242L).CopyTo(entry, 0);
                f.InsertData(behaviour.Object, data.Length, entry);
                BitConverter.GetBytes(count + 1).CopyTo(behaviour.Object.Data, registry + 4);
            }, "SplineExtrude"), 4242) + " -> a non-null reference 4242: Unity creates registry values no field holds");
            Risky("U10", "the shape's value retyped to UnityEngine.Splines.Spline (not an IExtrudeShape): ", KnownRetypeCrash,
                () => Inspect(RetypeShapeToSpline(c), firstRid) + " -> Shape null but the reference set: created, then not assigned");
        }

        /// <summary>
        /// The probe's SplineExtrude with its one registry value retyped to UnityEngine.Splines.Spline with no data, and a
        /// root-only ref type for it (the r5 crash shape: Unity streams it with Spline's real layout and reads out of bounds).
        /// </summary>
        static byte[] RetypeShapeToSpline(byte[] c) => Edit(c, (f, _) =>
        {
            var behaviour = f.Behaviour("SplineExtrude");
            var registry = behaviour.StartOf(f.Types[behaviour.Object.TypeIndex], "references");
            var (_, _, firstRid, firstEntry) = RawFile.ReadRegistry(behaviour.Object.Data, registry);
            var data = behaviour.Object.Data;
            var tail = new List<byte>();
            tail.AddRange(BitConverter.GetBytes(firstRid));
            foreach (var part in new[] { "Spline", "UnityEngine.Splines", "Unity.Splines" })
            {
                var bytes = Encoding.UTF8.GetBytes(part);
                tail.AddRange(BitConverter.GetBytes(bytes.Length));
                tail.AddRange(bytes);
                while (tail.Count % 4 != 0) tail.Add(0);
            }
            var head = data.Take(firstEntry).ToList();
            head.AddRange(tail);
            behaviour.Object.Data = head.ToArray();
            BitConverter.GetBytes(1).CopyTo(behaviour.Object.Data, registry + 4);
            f.AddRefType("Spline", "UnityEngine.Splines", "Unity.Splines");
        }, "SplineExtrude");

        /// <summary>
        /// The app layout table (BundleScriptLayouts) against Unity's real output, with nothing loaded: a table collected
        /// from the probe bundles themselves (the hashes and trees this editor writes for Button, Image, Toggle,
        /// SplineExtrude and its shape classes) must let the originals pass and refuse, as TypeTreeShape, a forged tree
        /// under the app's hash (U2's rename), the same under a zeroed hash, and the U10 crash shape. Also logs whether the
        /// editor's own hash APIs reproduce the stored hash (if one does, the generator could skip building bundles).
        /// </summary>
        [Test]
        public void U14_TheAppLayoutTableAgainstUnitysOwnOutput()
        {
            var sources = BuildSources();
            var observed = new List<BundleScriptLayouts.Observed>();
            foreach (var bundle in sources.Values) observed.AddRange(BundleChecker.CollectLayouts(bundle));
            var problems = new List<string>();
            var table = BundleScriptLayouts.Build(observed, Application.unityVersion, EditorUserBuildSettings.activeBuildTarget.ToString(), "U14", problems);
            Probe("U14", "table from the probe bundles: " + table.Scripts.Count + " script(s), " + table.References.Count + " reference class(es), "
                         + problems.Count + " conflict(s) " + string.Join("; ", problems));
            Assert.IsTrue(table.Scripts.ContainsKey(BundleScriptLayouts.KeyOf(typeof(Button))), "the probe's Button is in the table");
            var options = BundleCheckOptions.World();
            options.ScriptLayouts = table;
            string Verdict(byte[] bytes)
            {
                var verdict = BundleChecker.Check(bytes, options);
                return verdict.Kind + (verdict.Reasons.Count > 0 ? " (" + verdict.Reasons[0].Rule + ": " + verdict.Reasons[0].Detail + ")" : "");
            }
            var a = sources["probea"];
            var original = BundleChecker.Check(a, options);
            Probe("U14", "probea with the table: " + Verdict(a));
            Assert.IsFalse(original.Reasons.Any(r => r.Rule == BundleRule.TypeTreeShape), "the unmodified probe bundle keeps passing the layout comparison");
            foreach (var zeroHash in new[] { false, true })
            {
                var forged = Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    type.Nodes[type.Find("m_PersistentCalls")].NameOffset = type.AddString("m_PersistentCallz");
                    if (zeroHash) type.ZeroHashes();
                });
                var verdict = BundleChecker.Check(forged, options);
                Probe("U14", "U2's rename (" + (zeroHash ? "hash zeroed" : "the app's hash kept") + ") with the table: " + Verdict(forged));
                Assert.IsTrue(verdict.Reasons.Any(r => r.Rule == BundleRule.TypeTreeShape), "a forged tree under the app's (or a zero) hash is refused");
            }
            if (sources.TryGetValue("probec", out var c))
            {
                Probe("U14", "probec with the table: " + Verdict(c));
                Assert.IsFalse(BundleChecker.Check(c, options).Reasons.Any(r => r.Rule == BundleRule.TypeTreeShape), "the unmodified SplineExtrude keeps passing");
                var retyped = RetypeShapeToSpline(c);
                var verdict = BundleChecker.Check(retyped, options);
                Probe("U14", "U10's crash shape with the table: " + Verdict(retyped));
                Assert.IsTrue(verdict.Reasons.Any(r => r.Rule == BundleRule.TypeTreeShape), "the U10 crash shape is refused as structural");
                Probe("U14", "U10's crash shape without a table: " + Verdict(RetypeShapeToSpline(c)) + " (the residual the table closes)");
            }
            // Can the editor compute the stored hash without building a bundle?
            var stored = observed.FirstOrDefault(o => o.Kind == 'S' && o.Key == BundleScriptLayouts.KeyOf(typeof(Button)));
            var script = MonoImporter.GetAllRuntimeMonoScripts().FirstOrDefault(m => m != null && m.GetClass() == typeof(Button));
            var propertiesHash = typeof(MonoScript).GetMethod("GetPropertiesHashString", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            string Try(Func<object> get)
            {
                try
                {
                    return get()?.ToString() ?? "null";
                }
                catch (Exception e)
                {
                    return "(" + (e.InnerException ?? e).GetType().Name + ")";
                }
            }
            var calculate = Type.GetType("UnityEditor.Build.Content.ContentBuildInterface, UnityEditor.CoreModule")?.GetMethod("CalculatePlayerSerializationHashForType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Probe("U14", "Button's stored hash " + BundleScriptLayouts.Hex(stored.Hash)
                         + "; MonoScript.GetPropertiesHashString(false) " + (propertiesHash == null || script == null ? "n/a" : Try(() => propertiesHash.Invoke(script, new object[] { false })))
                         + ", (true) " + (propertiesHash == null || script == null ? "n/a" : Try(() => propertiesHash.Invoke(script, new object[] { true })))
                         + "; ContentBuildInterface.CalculatePlayerSerializationHashForType " + (calculate == null ? "n/a" : Try(() => calculate.Invoke(null, new object[] { typeof(Button) }))));
        }

        // ------------------------------------------------------------------ editing
    }
}
