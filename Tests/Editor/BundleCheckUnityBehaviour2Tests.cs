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
    /// Round-2 Unity-behaviour experiments for the pre-load bundle check (same approach as BundleCheckUnityBehaviourTests:
    /// real bundles built with BuildPipeline into Temp/BundleProbe2, rewritten in memory, loaded with LoadFromMemory, facts
    /// logged as "[BundleProbe] Un: ..."):
    /// <list type="bullet">
    /// <item>U11: does Unity match case variants of judged field names (m_PersistentCalls, m_Script, m_ClassName)?</item>
    /// <item>U12: does Unity look in other assemblies when a MonoScript's assembly name is empty or wrong?</item>
    /// <item>U13: how does Unity mount a bundle's files (odd node names, references through another directory)?</item>
    /// <item>M6: a bundle built by this project's own Unity must PASS the checker (catches a serialized-format bump on a
    /// Unity upgrade; see BundleSerializedFile.SupportedFormats).</item>
    /// </list>
    /// Nothing here waits on a task: builds and loads are synchronous on the main thread. The guard and the pre-load check
    /// stand aside during loads (BSUnityEventGuard.DisabledForTests, restored in TearDown); temp prefabs
    /// (Assets/__BundleProbe2Temp__) and built bundles are deleted in TearDown. Editor evidence only.
    /// </summary>
    [Category("BundleCheckUnityBehaviour")]
    public class BundleCheckUnityBehaviour2Tests
    {
        const string TempFolderName = "__BundleProbe2Temp__";
        const string TempFolder = "Assets/" + TempFolderName;
        const string OutputDir = "Temp/BundleProbe2";

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

        /// <summary>
        /// One prefab bundle per option set: "ProbeButton" (an Image, and a Button whose onClick calls
        /// GameObject.SetActive(true)).
        /// </summary>
        static byte[] BuildProbe(BuildAssetBundleOptions compression, string bundleName)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.CreateFolder("Assets", TempFolderName);
            var prefabPath = TempFolder + "/ProbeButton.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                var a = new GameObject("ProbeButton", typeof(RectTransform));
                try
                {
                    a.AddComponent<Image>();
                    var button = a.AddComponent<Button>();
                    UnityEventTools.AddBoolPersistentListener(button.onClick, a.SetActive, true);
                    PrefabUtility.SaveAsPrefabAsset(a, prefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(a);
                }
            }
            // The manifest bundle is named after the output folder: "out-..." matches no bundle name.
            var output = Path.Combine(OutputDir, "out-" + bundleName);
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            var target = EditorUserBuildSettings.activeBuildTarget;
            var manifest = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = bundleName, assetNames = new[] { prefabPath } } },
                compression | BuildAssetBundleOptions.ForceRebuildAssetBundle, target);
            Assert.IsNotNull(manifest, "BuildPipeline.BuildAssetBundles failed for " + target);
            return File.ReadAllBytes(Path.Combine(output, bundleName));
        }

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

        /// <summary>Loads the probe bundle (unloading it again) and describes the prefab's components and the Button's calls.</summary>
        string Describe(byte[] bytes)
        {
            var bundle = Load(bytes, out var note);
            if (bundle == null) return "not loaded" + note;
            try
            {
                var prefabs = bundle.LoadAllAssets<GameObject>();
                var root = prefabs.FirstOrDefault(p => p.name == "ProbeButton");
                if (root == null) return "loaded; no ProbeButton prefab (" + prefabs.Length + " prefabs, assets: " + string.Join(",", bundle.GetAllAssetNames()) + ")";
                var text = new StringBuilder("components [");
                foreach (var component in root.GetComponents<Component>())
                    text.Append(component == null ? "<missing script>" : component.GetType().FullName).Append("; ");
                text.Append("]");
                var button = root.GetComponent<Button>();
                if (button != null)
                {
                    var count = button.onClick.GetPersistentEventCount();
                    text.Append(" Button.onClick: ").Append(count).Append(" call(s)");
                    for (var i = 0; i < count; i++) text.Append(" [").Append(button.onClick.GetPersistentMethodName(i)).Append("]");
                }
                return text.ToString();
            }
            finally
            {
                Unload(bundle);
            }
        }

        /// <summary>Inconclusive (never a finding about Unity) when the rewriter's unmodified output does not load like the original.</summary>
        void RequireRewritable(byte[] bundle)
        {
            var original = Describe(bundle);
            var rewritten = Describe(RoundTrip(bundle));
            if (rewritten != original)
                Assert.Inconclusive("the rewriter's unmodified output does not load like the original (see BundleCheckUnityBehaviourTests.U0): original "
                                    + original + "; rewritten " + rewritten);
        }

        /// <summary>Replaces a string field's value, any length (the object's later bytes move with it).</summary>
        static void ReplaceString(RawFile file, RawFile.ObjectEntry o, string field, string value)
        {
            var located = file.Locate(o);
            var start = located.StartOf(file.Types[o.TypeIndex], field);
            var end = located.EndOf(file.Types[o.TypeIndex], field);
            var bytes = Encoding.UTF8.GetBytes(value);
            var replacement = new List<byte>(BitConverter.GetBytes(bytes.Length));
            replacement.AddRange(bytes);
            while ((start + replacement.Count) % 4 != 0) replacement.Add(0);
            o.Data = o.Data.Take(start).Concat(replacement).Concat(o.Data.Skip(end)).ToArray();
        }

        // ------------------------------------------------------------------ the experiments

        [Test]
        public void U11_CaseVariantsOfJudgedFieldNames()
        {
            var a = BuildProbe(BuildAssetBundleOptions.UncompressedAssetBundle, "probe11");
            var control = Describe(a);
            Probe("U11", "control (Unity " + Application.unityVersion + ", " + EditorUserBuildSettings.activeBuildTarget + "): " + control);
            StringAssert.Contains("SetActive", control, "the unmodified bundle must load its call");
            RequireRewritable(a);
            foreach (var zeroHash in new[] { false, true })
            {
                var suffix = zeroHash ? " [type hash zeroed]" : " [type hash kept]";
                Probe("U11", "Button field m_PersistentCalls renamed m_persistentCalls" + suffix + ": " + Describe(Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    type.Nodes[type.Find("m_PersistentCalls")].NameOffset = type.AddString("m_persistentCalls");
                    if (zeroHash) type.ZeroHashes();
                })) + " -> 1 call: Unity matched the name ignoring case (or by its own layout); 0: exact names only");
                Probe("U11", "Button field m_Script renamed m_script" + suffix + ": " + Describe(Edit(a, (file, button) =>
                {
                    var type = file.Types[button.Object.TypeIndex];
                    type.Nodes[type.Find("m_Script", 1)].NameOffset = type.AddString("m_script");
                    if (zeroHash) type.ZeroHashes();
                })) + " -> Button listed: matched ignoring case; missing script: exact names only");
                Probe("U11", "MonoScript field m_ClassName renamed m_className" + suffix + ": " + Describe(Edit(a, (file, button) =>
                {
                    var script = file.Script("Button");
                    var type = file.Types[script.TypeIndex];
                    type.Nodes[type.Find("m_ClassName", 1)].NameOffset = type.AddString("m_className");
                    if (zeroHash) type.ZeroHashes();
                })) + " -> Button listed: matched ignoring case (or hash trusted); missing script: exact names only");
            }
        }

        [Test]
        public void U12_MonoScriptAssemblyFallback()
        {
            var a = BuildProbe(BuildAssetBundleOptions.UncompressedAssetBundle, "probe12");
            RequireRewritable(a);
            foreach (var assembly in new[] { "", "UnityEngine.Nope.dll", "Assembly-CSharp.dll", "unityengine.ui.dll", "Library/ScriptAssemblies/UnityEngine.UI.dll" })
            {
                Probe("U12", "Button's MonoScript m_AssemblyName '" + assembly + "': " + Describe(Edit(a, (file, button) =>
                {
                    ReplaceString(file, file.Script("Button"), "m_AssemblyName", assembly);
                })) + " -> Button listed: Unity found the class without (or despite) the assembly name");
            }
        }

        [Test]
        public void U13_HowUnityMountsABundlesFiles()
        {
            var a = BuildProbe(BuildAssetBundleOptions.UncompressedAssetBundle, "probe13");
            RequireRewritable(a);
            var raw = RawBundle.Parse(a);
            var main = raw.Files.First(f => f.Flags == 4);
            var own = main.Name;
            Probe("U13", "control, nodes " + string.Join(", ", raw.Files.Select(f => f.Name + "(" + f.Flags + ")")) + ": " + Describe(a));
            Probe("U13", "serialized node renamed '" + "ZZZ" + own.Substring(3) + "' (same length, not a CAB name): " + Describe(EditBundle(a, b =>
            {
                b.Files.First(f => f.Flags == 4).Name = "ZZZ" + own.Substring(3);
            })));
            Probe("U13", "an extra unflagged node 'notes.txt': " + Describe(EditBundle(a, b => b.Files.Add(new RawFile("notes.txt", new byte[64], 0)))));
            foreach (var directory in new[] { own, "CAB-00000000000000000000000000000000", "elsewhere" })
            {
                Probe("U13", "Button's m_Script through 'archive:/" + directory + "/" + own + "' (its own Button script): " + Describe(Edit(a, (file, button) =>
                {
                    var script = file.ScriptPathId("Button");
                    var index = file.AddExternal("archive:/" + directory + "/" + own);
                    file.SetPPtr(button.Object, 16, index, script);
                    file.SetTypeScript(button.Object, index, script);
                })) + " -> Button listed: Unity resolved the reference to this bundle's own file through that directory");
            }
        }

        [Test]
        public void M6_ABundleFromThisUnityPassesTheChecker()
        {
            foreach (var (options, label) in new[] { (BuildAssetBundleOptions.UncompressedAssetBundle, "uncompressed"), (BuildAssetBundleOptions.ChunkBasedCompression, "LZ4") })
            {
                var bytes = BuildProbe(options, "probem6" + label.ToLowerInvariant());
                var verdict = BundleChecker.Check(bytes, BundleCheckOptions.Kit(), "m6");
                Probe("M6", label + " bundle from Unity " + Application.unityVersion + ": " + verdict.Kind + ", serialized format " + verdict.Stats.SerializedFormat
                            + ", " + verdict.Stats.Compression);
                Assert.IsTrue(verdict.Passed, "a bundle built by this project's own Unity must pass the pre-load check (a serialized-format change on a "
                                              + "Unity upgrade? see BundleSerializedFile.SupportedFormats): " + verdict.Describe(label));
            }
        }
    }
}
