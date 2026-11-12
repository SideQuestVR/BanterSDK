using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The pre-load check reads a bundle's objects through the bundle's own type trees. This experiment asks whether Unity
    /// does too, or trusts a matching type hash and reads by its own layout (then a forged tree could show the checker
    /// other fields than Unity loads). A prefab with a LayoutElement (m_LayoutPriority = 7) is built into an uncompressed
    /// bundle; the field's name in the bundle's type tree is renamed in memory (same length, the type hash left as built),
    /// and the bundle is loaded with LoadFromMemory. Reading by the tree, Unity cannot match the renamed field and keeps
    /// the default (1); trusting the hash, it reads 7. Logs "[GuardProbe] TypeTree: ...".
    /// </summary>
    /// <remarks>
    /// Editor evidence only: a player can take another path, so run it in a development player too before relying on
    /// it. The modified bytes exist only in memory; the prefab (Assets/__TypeTreeProbeTemp__) and the built bundle
    /// (Temp/TypeTreeProbe) are deleted in TearDown.
    /// </remarks>
    [Category("GuardUnityAssumptions")]
    [Category("BundleCheckUnityBehaviour")]
    public class BundleTypeTreeProbeTests
    {
        const string TempFolderName = "__TypeTreeProbeTemp__";
        const string TempFolder = "Assets/" + TempFolderName;
        const string OutputDir = "Temp/TypeTreeProbe";
        const string PrefabName = "TypeTreeProbe";
        const string BundleName = "typetreeprobe";
        const string Field = "m_LayoutPriority";
        const int Authored = 7;
        const int FieldDefault = 1;

        static void Probe(string fact) => Debug.Log("[GuardProbe] TypeTree: " + fact);

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolder) && !AssetDatabase.DeleteAsset(TempFolder))
                Debug.LogError("[GuardProbe] TypeTree: could not delete " + TempFolder);
            if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void DoesUnityReadAScriptByTheBundlesTypeTreeOrTrustItsHash()
        {
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder); // left by an aborted run
            AssetDatabase.CreateFolder("Assets", TempFolderName);
            var prefabPath = TempFolder + "/" + PrefabName + ".prefab";
            var source = new GameObject(PrefabName, typeof(RectTransform));
            try
            {
                source.AddComponent<LayoutElement>().layoutPriority = Authored;
                PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            // BuildPipeline names its manifest bundle after the output folder; "out-layout" matches no bundle name
            // ("Temp/TypeTreeProbe" did: bundle names are lower-cased, so it clashed with "typetreeprobe").
            var output = Path.Combine(OutputDir, "out-layout");
            if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(output);
            var target = EditorUserBuildSettings.activeBuildTarget;
            var manifest = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { prefabPath } } },
                BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ForceRebuildAssetBundle, target);
            Assert.IsNotNull(manifest, "BuildPipeline.BuildAssetBundles failed for " + target);
            var built = File.ReadAllBytes(Path.Combine(output, BundleName));

            var control = LoadPriority(built, out var controlNote);
            Probe("control, the bundle as built (" + built.Length + " B, " + target + ", Unity " + Application.unityVersion + "): layoutPriority "
                  + control + controlNote);
            Assert.AreEqual(Authored, control, "the unmodified bundle must load the authored value");

            var hits = IndexesOf(built, Encoding.ASCII.GetBytes(Field + "\0"));
            Probe("'" + Field + "' occurs " + hits.Count + " time(s) in the uncompressed bundle");
            if (hits.Count != 1) Assert.Inconclusive("expected the field name once, in the type tree's string buffer");
            // Unity may refuse or log errors loading the forged bytes: that is the result to record, not a failure.
            LogAssert.ignoreFailingMessages = true;
            var forged = (byte[])built.Clone();
            forged[hits[0] + Field.Length - 1] = (byte)'z'; // m_LayoutPrioritz: same length, type hash untouched
            var read = LoadPriority(forged, out var note);
            string meaning;
            if (read == Authored)
                meaning = "Unity TRUSTED the matching type hash and read the data by its own layout, ignoring the bundle's tree: "
                          + "the checker must not judge script data by an unverified bundle tree (needs the build-time table)";
            else if (read == FieldDefault)
                meaning = "Unity read by the bundle's type tree (the renamed field matched nothing; the default stayed): "
                          + "the checker's view of the data is Unity's";
            else
                meaning = "unexpected; inspect the load";
            Probe("RESULT forged tree, hash kept: layoutPriority " + read + note + " -> " + meaning);
            Assert.That(read, Is.EqualTo(Authored).Or.EqualTo(FieldDefault), "neither path: " + read + note);
        }

        /// <summary>The loaded prefab's LayoutElement.layoutPriority (negative when it did not load); the bundle is unloaded again.</summary>
        static int LoadPriority(byte[] bytes, out string note)
        {
            note = "";
            var bundle = AssetBundle.LoadFromMemory(bytes);
            if (bundle == null)
            {
                note = " (LoadFromMemory refused the bundle)";
                return -1;
            }
            try
            {
                var prefabs = bundle.LoadAllAssets<GameObject>();
                var element = prefabs.Length > 0 ? prefabs[0].GetComponent<LayoutElement>() : null;
                if (element == null)
                {
                    note = " (no LayoutElement on the loaded prefab)";
                    return -2;
                }
                return element.layoutPriority;
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        static List<int> IndexesOf(byte[] data, byte[] pattern)
        {
            var found = new List<int>();
            for (var i = 0; i <= data.Length - pattern.Length; i++)
            {
                var j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) found.Add(i);
            }
            return found;
        }
    }
}
