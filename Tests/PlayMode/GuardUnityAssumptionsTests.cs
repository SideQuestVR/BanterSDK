#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using EarlyUpdate = UnityEngine.PlayerLoop.EarlyUpdate;
using UnityObject = UnityEngine.Object;

namespace BS.GuardProbe
{
    /// <summary>
    /// Observes the Unity runtime behaviours that BSUnityEventGuard and Visual Scripting's WorldActivation rest on, with
    /// real asset bundles. A PlayMode suite, run in the editor: <see cref="Setup"/> (IPrebuildSetup) authors the content
    /// and builds the bundles for the active build target in edit mode, before Play; the probes run in Play mode;
    /// <see cref="Cleanup"/> (IPostBuildCleanup) deletes it all after Play. Each probe logs "[GuardProbe] Qn: ..." lines
    /// (the evidence, also when an assertion fails) and a final "[GuardProbe] Qn: RESULT ..." line; its assertions state
    /// the behaviour the guard needs, and every wait has a deadline that fails the test.
    /// </summary>
    /// <remarks>
    /// The guard is always on in Play mode and refuses (scrubs, deactivates, unloads) any bundle scene it was not armed
    /// for, so the raw-Unity probes set BSUnityEventGuard.DisabledForTests for their duration; only Q8's second half runs
    /// with the guard on. The guard's members are internal and this assembly has no InternalsVisibleTo, so they are
    /// reached by reflection (a missing member fails the test that needs it, by name). Greenfield's AfterSceneLoad
    /// bootstraps (MCP host, lip sync, recording...) still start in UTF's empty test scene; nothing here depends on them.
    /// Temporary assets: Assets/__GuardProbeTemp__ and Temp/GuardProbe.
    /// </remarks>
    [Category("GuardUnityAssumptions")]
    public class GuardUnityAssumptionsTests : IPrebuildSetup, IPostBuildCleanup
    {
        // ------------------------------------------------------------------ names and paths

        const string TempFolderName = "__GuardProbeTemp__";
        const string TempFolder = "Assets/" + TempFolderName;
        const string OutputDir = "Temp/GuardProbe";
        const string SceneA = "GuardProbeA", SceneB = "GuardProbeB", SceneHide = "GuardProbeHideFlags", SceneEvent = "GuardProbeEvent";
        const string SceneHideAwake = "GuardProbeHideAwake";

        /// <summary>BSUnityEventGuard.HidingOrPersisting (private const): the bits it clears and its scene-less scan looks for.</summary>
        const HideFlags HidingOrPersisting =
            HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.DontUnloadUnusedAsset;
        const string ScenesBundleName = "guardprobe_scenes", KitBundleName = "guardprobe_kit", ExtraBundleName = "guardprobe_extra";
        const string FlushPrefab = "GuardProbeFlush", ItemPrefab = "GuardProbeItem", ExtraPrefab = "GuardProbeExtra", LegacyPrefab = "GuardProbeLegacyAnim";
        const string LegacyClipA = "GuardProbeLegacyA", LegacyClipB = "GuardProbeLegacyB";
        const string EventsMecanimClip = "GuardProbeEventsMecanim", EventsLegacyClip = "GuardProbeEventsLegacy", EventsController = "GuardProbeEvents";
        const string EditMecanimClip = "GuardProbeEditMecanim", EditLegacyClip = "GuardProbeEditLegacy", EditController = "GuardProbeEdit";
        const float EditWindow = 0.75f; // three loops of a ClipLength clip
        const string ButtonHolder = "Button holder";
        const string StringArgument = "from the bundle";
        const string AuthoredHidden = "HF_Authored_HideInHierarchy";
        const float ClipLength = 0.25f;
        const float Timeout = 15f;

        /// <summary>What <see cref="Setup"/> hands to the Play-mode probes across the domain reload.</summary>
        const string EditLoopKey = "GuardProbe.editModeLoop", BuildKey = "GuardProbe.buildSummary";

        static string ScenePathOf(string name) => TempFolder + "/" + name + ".unity";
        static string PrefabPath(string name) => TempFolder + "/" + name + ".prefab";
        static string ClipPath(string name) => TempFolder + "/" + name + ".anim";
        static string ControllerPath => TempFolder + "/" + EventsController + ".controller";
        static string EditControllerPath => TempFolder + "/" + EditController + ".controller";

        /// <summary>Hide flags Q7 sets at runtime on the bundle scene's objects, by object name.</summary>
        static readonly Dictionary<string, HideFlags> runtimeFlags = new Dictionary<string, HideFlags>(StringComparer.Ordinal)
        {
            { "HF_None", HideFlags.None },
            { "HF_HideInHierarchy", HideFlags.HideInHierarchy },
            { "HF_NotEditable", HideFlags.NotEditable },
            { "HF_DontSaveInEditor", HideFlags.DontSaveInEditor },
            { "HF_DontSaveInBuild", HideFlags.DontSaveInBuild },
            { "HF_DontUnloadUnusedAsset", HideFlags.DontUnloadUnusedAsset },
            { "HF_DontSave", HideFlags.DontSave },
            { "HF_HideAndDontSave", HideFlags.HideAndDontSave },
            { "HF_Child_DontSave", HideFlags.DontSave },
            { "HF_Child_HideAndDontSave", HideFlags.HideAndDontSave },
        };

        // ------------------------------------------------------------------ the guard's internals, by reflection

        const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static readonly Type guardType = typeof(BSAssetBundle).Assembly.GetType("BS.BSUnityEventGuard", false);
        static readonly MethodInfo guardOnSceneLoaded = guardType?.GetMethod("OnSceneLoaded", AnyStatic, null, new[] { typeof(Scene), typeof(LoadSceneMode) }, null);
        static readonly MethodInfo guardMoveToFront = guardType?.GetMethod("MoveToFront", AnyStatic, null, Type.EmptyTypes, null);
        static readonly MethodInfo guardSanitizeWhenLoaded = guardType?.GetMethod("SanitizeWhenLoaded", AnyStatic, null, new[] { typeof(string), typeof(string) }, null);
        static readonly MethodInfo guardRefreshLegacy =
            guardType?.GetMethod("RefreshLegacyAnimations", AnyStatic, null, new[] { typeof(ICollection<AnimationClip>) }, null);
        static readonly FieldInfo guardDisabledField = guardType?.GetField("DisabledForTests", AnyStatic);
        static readonly PropertyInfo guardDisabledProperty = guardType?.GetProperty("DisabledForTests", AnyStatic);
        static readonly Type worldActivationType = Type.GetType("Unity.VisualScripting.WorldActivation, Unity.VisualScripting.Core", false);

        // ------------------------------------------------------------------ Play-mode state (no reload between tests)

        static AssetBundle scenesBundle, kitBundle;
        static readonly List<UnityAction<Scene, LoadSceneMode>> loadedHandlers = new List<UnityAction<Scene, LoadSceneMode>>();
        static readonly List<UnityObject> spawned = new List<UnityObject>();
        static bool guardUnhooked;
        static bool? guardDisabledOriginal;

        // ================================================================== edit mode: before and after Play

        /// <summary>IPrebuildSetup: runs in the editor, after UTF's bootstrap scene exists and before Play mode.</summary>
        public void Setup()
        {
            LogAssert.ignoreFailingMessages = true; // the bundle build may log; the [GuardProbe] lines are the evidence
            try
            {
                BuildContent();
            }
            catch
            {
                DeleteContent();
                throw;
            }
        }

        /// <summary>IPostBuildCleanup: runs in the editor after Play mode, failures included.</summary>
        public void Cleanup()
        {
            LogAssert.ignoreFailingMessages = true;
            if (HasDisabledForTests) GuardDisabledFlag = false; // nothing else resets it, and Play mode exits without a reload
            DeleteContent();
        }

        /// <summary>
        /// Authors the probe content under Assets/__GuardProbeTemp__ and builds three bundles into Temp/GuardProbe. Prefab
        /// sources are made in a throwaway scene and the probe scenes in their own, so UTF's bootstrap scene stays clean.
        /// </summary>
        static void BuildContent()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            SessionState.SetString(EditLoopKey, DescribeLoop(ReadLoop(PlayerLoop.GetCurrentPlayerLoop())));
            RemoveLeftovers();
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", TempFolderName)))
                throw new InvalidOperationException("[GuardProbe] could not create " + TempFolder);

            // Clips: flat curves give them a length; the events clips name every receiver method (Q4).
            var legacyA = MakeClip(LegacyClipA, true, false);
            var legacyB = MakeClip(LegacyClipB, true, false);
            var events = new[]
            {
                new AnimationEvent { time = 0.02f, functionName = GuardProbeAnimationReceiver.PublicInstanceName },
                new AnimationEvent { time = 0.04f, functionName = GuardProbeAnimationReceiver.PrivateInstanceName },
                new AnimationEvent { time = 0.06f, functionName = GuardProbeAnimationReceiver.PublicStaticName },
                new AnimationEvent { time = 0.08f, functionName = GuardProbeAnimationReceiver.PrivateStaticName },
            };
            var mecanim = MakeClip(EventsMecanimClip, false, false, events);
            MakeClip(EventsLegacyClip, true, false, events);
            if (AnimatorController.CreateAnimatorControllerAtPathWithClip(ControllerPath, mecanim) == null)
                throw new InvalidOperationException("[GuardProbe] could not create " + ControllerPath);
            // Q11: looping clips with one event to keep and one to remove at runtime.
            var editEvents = new[]
            {
                new AnimationEvent { time = 0.05f, functionName = GuardProbeAnimationReceiver.PublicInstanceName },
                new AnimationEvent { time = 0.15f, functionName = GuardProbeAnimationReceiver.PrivateInstanceName },
            };
            var editMecanim = MakeClip(EditMecanimClip, false, true, editEvents);
            MakeClip(EditLegacyClip, true, true, editEvents);
            if (AnimatorController.CreateAnimatorControllerAtPathWithClip(EditControllerPath, editMecanim) == null)
                throw new InvalidOperationException("[GuardProbe] could not create " + EditControllerPath);

            WithScratchScene(() =>
            {
                SavePrefab(FlushPrefab, go => new GameObject("child").transform.SetParent(go.transform, false));
                SavePrefab(ItemPrefab, null);
                SavePrefab(ExtraPrefab, null);
                SavePrefab(LegacyPrefab, go =>
                {
                    var animation = go.AddComponent<Animation>();
                    animation.playAutomatically = false;
                    AnimationUtility.SetAnimationClips(animation, new[] { legacyA, legacyB });
                    animation.clip = legacyA;
                });
            }, null);

            WithScratchScene(() =>
            {
                new GameObject("Recorder A").AddComponent<GuardProbeActivationRecorder>();
                new GameObject("Plain A");
            }, SceneA);
            WithScratchScene(() => new GameObject("Recorder B").AddComponent<GuardProbeActivationRecorder>(), SceneB);
            WithScratchScene(() =>
            {
                Transform parent = null;
                foreach (var name in runtimeFlags.Keys)
                {
                    if (name.StartsWith("HF_Child_", StringComparison.Ordinal))
                    {
                        if (parent == null) parent = new GameObject("HF_Parent").transform;
                        new GameObject(name).transform.SetParent(parent, false);
                    }
                    else
                    {
                        new GameObject(name);
                    }
                }
                // Saved in the scene file, so it travels in the bundle data (flags that include DontSave never would).
                new GameObject(AuthoredHidden).hideFlags = HideFlags.HideInHierarchy;
            }, SceneHide);
            WithScratchScene(() => new GameObject("Hider").AddComponent<GuardProbeHideOnAwake>(), SceneHideAwake);
            WithScratchScene(() =>
            {
                var button = new GameObject(ButtonHolder).AddComponent<Button>();
                // Target-less calls naming a static on a test type, as a bundle can carry them (Q8).
                AddStaticCall(button, nameof(GuardProbeStaticTarget.PublicHit), PersistentListenerMode.Void, "");
                AddStaticCall(button, GuardProbeStaticTarget.PrivateHitName, PersistentListenerMode.String, StringArgument);
            }, SceneEvent);
            AssetDatabase.SaveAssets();

            var target = EditorUserBuildSettings.activeBuildTarget;
            if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(OutputDir);
            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = ScenesBundleName,
                    assetNames = new[]
                    {
                        ScenePathOf(SceneA), ScenePathOf(SceneB), ScenePathOf(SceneHide), ScenePathOf(SceneEvent), ScenePathOf(SceneHideAwake),
                    },
                },
                new AssetBundleBuild
                {
                    assetBundleName = KitBundleName,
                    assetNames = new[]
                    {
                        PrefabPath(FlushPrefab), PrefabPath(ItemPrefab), PrefabPath(LegacyPrefab), ClipPath(LegacyClipA), ClipPath(LegacyClipB),
                        ClipPath(EventsMecanimClip), ClipPath(EventsLegacyClip), ControllerPath,
                        ClipPath(EditMecanimClip), ClipPath(EditLegacyClip), EditControllerPath,
                    },
                },
                new AssetBundleBuild { assetBundleName = ExtraBundleName, assetNames = new[] { PrefabPath(ExtraPrefab) } },
            };
            var manifest = BuildPipeline.BuildAssetBundles(OutputDir, builds,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, target);
            if (manifest == null) throw new InvalidOperationException("[GuardProbe] BuildPipeline.BuildAssetBundles failed for " + target);
            var sizes = new List<string>();
            foreach (var name in new[] { ScenesBundleName, KitBundleName, ExtraBundleName })
            {
                var file = Path.Combine(OutputDir, name);
                if (!File.Exists(file)) throw new InvalidOperationException("[GuardProbe] bundle not written: " + file);
                sizes.Add(name + " " + new FileInfo(file).Length + " B");
            }
            var summary = "Built " + string.Join(", ", sizes) + " for " + target + " (ChunkBasedCompression) in " + clock.ElapsedMilliseconds
                          + " ms, in IPrebuildSetup with '" + SceneManager.GetActiveScene().path + "' active.";
            SessionState.SetString(BuildKey, summary);
            Debug.Log("[GuardProbe] SETUP: " + summary);
        }

        static AnimationClip MakeClip(string name, bool legacy, bool loop, params AnimationEvent[] events)
        {
            var clip = new AnimationClip { legacy = legacy, frameRate = 60f };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Constant(0f, ClipLength, 0f));
            if (loop)
            {
                if (legacy)
                {
                    clip.wrapMode = WrapMode.Loop;
                }
                else
                {
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = true;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                }
            }
            if (events.Length > 0) AnimationUtility.SetAnimationEvents(clip, events);
            AssetDatabase.CreateAsset(clip, ClipPath(name));
            return clip;
        }

        static void SavePrefab(string name, Action<GameObject> configure)
        {
            var go = new GameObject(name);
            try
            {
                configure?.Invoke(go);
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath(name), out var saved);
                if (!saved) throw new InvalidOperationException("[GuardProbe] could not save " + PrefabPath(name));
            }
            finally
            {
                UnityObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// A new additive scene, active while <paramref name="populate"/> runs (new GameObjects land there), then saved as
        /// <paramref name="saveAs"/> (or thrown away when null) and closed; the previously active scene is active again.
        /// </summary>
        static void WithScratchScene(Action populate, string saveAs)
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.skybox = null; // keeps the default skybox (and its shader compile) out of the bundle
                populate();
                if (saveAs != null && !EditorSceneManager.SaveScene(scene, ScenePathOf(saveAs)))
                    throw new InvalidOperationException("[GuardProbe] could not save " + saveAs);
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>A persistent call with no target object, naming a static by type string, field by field as a bundle stores it.</summary>
        static void AddStaticCall(Button button, string method, PersistentListenerMode mode, string stringArgument)
        {
            var serialized = new SerializedObject(button);
            var calls = serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            var index = calls.arraySize;
            calls.InsertArrayElementAtIndex(index);
            var call = calls.GetArrayElementAtIndex(index);
            call.FindPropertyRelative("m_Target").objectReferenceValue = null;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = GuardProbeStaticTarget.TypeName;
            call.FindPropertyRelative("m_MethodName").stringValue = method;
            call.FindPropertyRelative("m_Mode").intValue = (int)mode;
            call.FindPropertyRelative("m_Arguments.m_ObjectArgument").objectReferenceValue = null;
            call.FindPropertyRelative("m_Arguments.m_ObjectArgumentAssemblyTypeName").stringValue = "";
            call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue = stringArgument;
            call.FindPropertyRelative("m_CallState").intValue = (int)UnityEventCallState.RuntimeOnly;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// What an aborted or crashed run left behind (Cleanup never ran): the asset folder whether or not the AssetDatabase
        /// knows it yet, a stray folder .meta, and the bundle output.
        /// </summary>
        static void RemoveLeftovers()
        {
            var found = new List<string>();
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                found.Add(TempFolder + " (imported)");
                if (!AssetDatabase.DeleteAsset(TempFolder)) throw new InvalidOperationException("[GuardProbe] could not delete the leftover " + TempFolder);
            }
            if (Directory.Exists(TempFolder))
            {
                found.Add(TempFolder + " (on disk only)");
                Directory.Delete(TempFolder, true);
            }
            if (File.Exists(TempFolder + ".meta"))
            {
                found.Add(TempFolder + ".meta");
                File.Delete(TempFolder + ".meta");
            }
            if (Directory.Exists(OutputDir))
            {
                found.Add(OutputDir);
                Directory.Delete(OutputDir, true);
            }
            if (found.Count > 0) Debug.Log("[GuardProbe] SETUP: removed leftovers of an earlier run: " + string.Join(", ", found));
        }

        static void DeleteContent()
        {
            try
            {
                if (AssetDatabase.IsValidFolder(TempFolder) && !AssetDatabase.DeleteAsset(TempFolder))
                    Debug.LogWarning("[GuardProbe] CLEANUP: could not delete " + TempFolder);
                if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GuardProbe] CLEANUP: " + e.Message);
            }
            SessionState.EraseString(EditLoopKey);
            SessionState.EraseString(BuildKey);
            Debug.Log("[GuardProbe] CLEANUP: " + TempFolder + " exists: " + AssetDatabase.IsValidFolder(TempFolder) + ", " + OutputDir
                      + " exists: " + Directory.Exists(OutputDir) + ", DisabledForTests " + (HasDisabledForTests ? GuardDisabledFlag.ToString() : "absent"));
        }

        // ================================================================== Play mode: per probe and per fixture

        [UnityTearDown]
        public IEnumerator AfterEachProbe()
        {
            LogAssert.ignoreFailingMessages = true;
            ReleaseProbeHooks();
            var unloads = new List<AsyncOperation>();
            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.name.StartsWith("GuardProbe", StringComparison.Ordinal)) continue;
                var unload = SceneManager.UnloadSceneAsync(scene);
                if (unload != null) unloads.Add(unload);
            }
            var deadline = Time.realtimeSinceStartup + Timeout;
            while (unloads.Exists(u => !u.isDone) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(unloads.Exists(u => !u.isDone), "teardown: probe scenes did not unload within " + Timeout + " s");
        }

        [OneTimeTearDown]
        public void ReleaseBundles()
        {
            LogAssert.ignoreFailingMessages = true;
            ReleaseProbeHooks();
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles().ToList())
            {
                if (bundle != null && bundle.name != null && bundle.name.StartsWith("guardprobe_", StringComparison.Ordinal)) bundle.Unload(true);
            }
            scenesBundle = null;
            kitBundle = null;
        }

        /// <summary>Everything a probe may have left on: its sceneLoaded handlers, markers, the guard switch, objects, a graph hold.</summary>
        static void ReleaseProbeHooks()
        {
            foreach (var handler in loadedHandlers) SceneManager.sceneLoaded -= handler;
            loadedHandlers.Clear();
            GuardProbeLoop.Uninstall();
            RestoreGuard();
            foreach (var item in spawned)
            {
                if (item != null) UnityObject.DestroyImmediate(item); // HideAndDontSave objects would outlive Play mode
            }
            spawned.Clear();
            var isDeferring = worldActivationType?.GetProperty("IsDeferring", AnyStatic);
            var cancel = worldActivationType?.GetMethod("Cancel", AnyStatic, null, Type.EmptyTypes, null);
            if (isDeferring != null && cancel != null && (bool)isDeferring.GetValue(null)) cancel.Invoke(null, null);
        }

        // ================================================================== Q10: the guard's own PlayerLoop systems

        [UnityTest, Order(1)]
        public IEnumerator Q10_GuardSystems_BracketUpdatePreloading()
        {
            Begin();
            Assert.IsNotNull(guardType, "BS.BSUnityEventGuard not found in BS.SDK");
            var facts = ReadLoop(PlayerLoop.GetCurrentPlayerLoop());
            Probe("Q10", "Play mode PlayerLoop (Application.isPlaying " + Application.isPlaying + ", frame " + Time.frameCount + "): " + DescribeLoop(facts));
            Probe("Q10", "edit mode, in IPrebuildSetup before Play: " + SessionState.GetString(EditLoopKey, "(not recorded)"));
            Probe("Q10", "guard's sceneLoaded handler: " + DescribeSceneLoadedHandlers()
                         + ". Installed by [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] SubscribeFirst and again by each"
                         + " PendingWorld; InstallFrameHook returns early unless Application.isPlaying");
            Probe("Q10", "the raw-Unity probes (Q1-Q7, Q9, Q8's first half) bypass the guard with: " + GuardBypass
                         + (HasDisabledForTests ? " (value now " + GuardDisabledFlag + ")" : ""));
            var bracketed = facts.Preloading >= 0 && facts.Before == facts.Preloading - 1 && facts.After == facts.Preloading + 1;
            Probe("Q10", "RESULT: in Play mode the guard systems " + (bracketed ? "DO" : "do NOT") + " sit immediately before and after"
                         + " EarlyUpdate.UpdatePreloading; " + facts.GuardSystems + " guard system(s) in the whole loop");
            yield return null;
            Assert.IsTrue(Application.isPlaying, "not in Play mode");
            Assert.GreaterOrEqual(facts.Preloading, 0, "EarlyUpdate.UpdatePreloading not found");
            Assert.AreEqual(facts.Preloading - 1, facts.Before, "GuardBeforeSceneActivation is not immediately before UpdatePreloading");
            Assert.AreEqual(facts.Preloading + 1, facts.After, "GuardAfterSceneActivation is not immediately after UpdatePreloading");
            Assert.AreEqual(2, facts.GuardSystems, "expected exactly two guard systems in the loop");
        }

        // ================================================================== Q6: HideAndDontSave + DontDestroyOnLoad

        [UnityTest, Order(2)]
        public IEnumerator Q06_HideAndDontSave_InDontDestroyOnLoadRoots()
        {
            Begin();
            DisableGuard();
            var anchor = Track(new GameObject("GuardProbe DDOL anchor"));
            UnityObject.DontDestroyOnLoad(anchor);
            var ddol = anchor.scene;
            var cases = new List<(string label, GameObject go, bool persisted)>
            {
                ("plain DontDestroyOnLoad", MakePersistent("plain", HideFlags.None, true, true), true),
                ("HideAndDontSave, then DontDestroyOnLoad", MakePersistent("HADS first", HideFlags.HideAndDontSave, true, true), true),
                ("DontDestroyOnLoad, then HideAndDontSave", MakePersistent("HADS after", HideFlags.HideAndDontSave, false, true), true),
                ("HideInHierarchy, then DontDestroyOnLoad", MakePersistent("HideInHierarchy", HideFlags.HideInHierarchy, true, true), true),
                ("DontSave, then DontDestroyOnLoad", MakePersistent("DontSave", HideFlags.DontSave, true, true), true),
                ("HideAndDontSave, never DontDestroyOnLoad", MakePersistent("HADS only", HideFlags.HideAndDontSave, true, false), false),
            };
            var roots = new HashSet<GameObject>(ddol.GetRootGameObjects());
            var all = new HashSet<GameObject>(Resources.FindObjectsOfTypeAll<GameObject>());
            var findable = new HashSet<GameObject>(FindObjects.All<Transform>(FindObjectsInactive.Include).Select(t => t.gameObject));
            var undiscoverable = new List<string>();
            foreach (var (label, go, persisted) in cases)
            {
                var inRoots = roots.Contains(go);
                var inAll = all.Contains(go);
                var inDdol = go.scene.handle == ddol.handle;
                Probe("Q6", "[" + label + "] scene " + DescribeScene(go.scene, ddol.handle, "the DontDestroyOnLoad scene")
                            + "; in DontDestroyOnLoad GetRootGameObjects: " + YesNo(inRoots)
                            + "; Resources.FindObjectsOfTypeAll: " + YesNo(inAll)
                            + "; FindObjectsByType(Include): " + YesNo(findable.Contains(go))
                            + "; GameObject.Find: " + YesNo(GameObject.Find(go.name) != null)
                            + "; hideFlags now " + go.hideFlags);
                // The guard finds escapees through the root list, plus FindObjectsOfTypeAll filtered by scene.
                if (persisted && !inRoots && !(inAll && inDdol)) undiscoverable.Add(label);
            }
            var guardProbe = all.FirstOrDefault(g => g != null && g.name == "BSUnityEventGuard (DontDestroyOnLoad probe)");
            Probe("Q6", "the guard's own HideInHierarchy DDOL probe: "
                        + (guardProbe == null ? "not created" : "in GetRootGameObjects: " + YesNo(roots.Contains(guardProbe))));
            Probe("Q6", "DontDestroyOnLoad scene '" + ddol.name + "' rootCount " + ddol.rootCount + ", GetRootGameObjects " + roots.Count + ": "
                        + Names(roots.Where(g => g != null).Select(g => g.name), 24));
            Probe("Q6", "RESULT: DontDestroyOnLoad scene's GetRootGameObjects lists a HideAndDontSave object: flags set before the move "
                        + YesNo(roots.Contains(cases[1].go)) + ", flags set after the move " + YesNo(roots.Contains(cases[2].go))
                        + "; DontDestroyOnLoad objects found by neither GetRootGameObjects nor FindObjectsOfTypeAll+scene (the guard's two lookups): "
                        + Names(undiscoverable));
            yield return null;
            Assert.IsEmpty(undiscoverable, "DontDestroyOnLoad objects neither GetRootGameObjects nor FindObjectsOfTypeAll+scene can see");
        }

        static GameObject MakePersistent(string label, HideFlags flags, bool flagsFirst, bool persist)
        {
            var go = Track(new GameObject("GuardProbe DDOL " + label));
            if (flagsFirst) go.hideFlags = flags;
            if (persist) UnityObject.DontDestroyOnLoad(go);
            if (!flagsFirst) go.hideFlags = flags;
            return go;
        }

        // ================================================================== Q9: instance id signs

        [UnityTest, Order(3)]
        public IEnumerator Q09_InstanceIdSigns()
        {
            Begin();
            EnsureBundles();
            DisableGuard();
            var path = ScenePath(SceneA);
            var op = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            Assert.IsNotNull(op, "LoadSceneAsync returned null for " + path);
            var scene = FindLoading(path);
            var deadline = Time.realtimeSinceStartup + Timeout;
            while (!op.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(op.isDone && scene.isLoaded, "the bundle scene did not load within " + Timeout + " s");
            var roots = scene.GetRootGameObjects();
            Assert.IsNotEmpty(roots, "the bundle scene has no roots");
            var prefab = kitBundle.LoadAsset<GameObject>(KitAsset(ItemPrefab + ".prefab"));
            Assert.IsNotNull(prefab, "the item prefab did not load from the kit bundle");

            // (category, what, object). The guard's "positive instance id = loaded from data" check needs each sign.
            const string Activated = "bundle-scene objects after activation", Moved = "moved to DontDestroyOnLoad";
            const string Copies = "Instantiate copies", Fresh = "new GameObject()", Assets = "bundle assets", Other = "other";
            var ids = new List<(string category, string what, UnityObject obj)>();
            foreach (var root in roots)
            {
                ids.Add((Activated, "GameObject '" + root.name + "'", root));
                foreach (var component in root.GetComponents<Component>())
                    ids.Add((Activated, "'" + root.name + "' " + (component == null ? "missing-script component" : component.GetType().Name), component));
            }
            ids.Add((Assets, "prefab asset (LoadAsset)", prefab));
            ids.Add((Assets, "its Transform", prefab.transform));
            ids.Add((Assets, "AnimationClip asset", kitBundle.LoadAsset<AnimationClip>(KitAsset(LegacyClipB + ".anim"))));
            var prefabCopy = Track(UnityObject.Instantiate(prefab));
            ids.Add((Copies, "copy of the bundle prefab", prefabCopy));
            ids.Add((Copies, "its Transform", prefabCopy.transform));
            var sceneObject = roots.FirstOrDefault(r => r.name == "Plain A") ?? roots[0];
            ids.Add((Copies, "copy of bundle-scene object '" + sceneObject.name + "'", Track(UnityObject.Instantiate(sceneObject))));
            var fresh = Track(new GameObject("GuardProbe new GameObject"));
            ids.Add((Fresh, "new GameObject()", fresh));
            ids.Add((Fresh, "its Transform", fresh.transform));
            // The same kinds of object moved to DontDestroyOnLoad: whether the move changes an id, and the sign it keeps.
            var movers = new List<(string what, GameObject go)>
            {
                ("bundle-scene root '" + sceneObject.name + "'", Track(sceneObject)),
                ("Instantiate copy of the bundle prefab", Track(UnityObject.Instantiate(prefab))),
                ("new GameObject()", Track(new GameObject("GuardProbe new GameObject for DDOL"))),
            };
            foreach (var (what, go) in movers)
            {
                var before = go.GetInstanceID();
                UnityObject.DontDestroyOnLoad(go);
                ids.Add((Moved, what + " (id before the move " + before + (before == go.GetInstanceID() ? ", unchanged" : ", CHANGED")
                                + "; now in scene '" + go.scene.name + "')", go));
            }
            ids.Add((Other, "new Mesh()", Track(new Mesh())));
            ids.Add((Other, "AssetDatabase prefab asset (editor, persistent)", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(ItemPrefab))));
            ids.Add((Other, "the scenes AssetBundle object", scenesBundle));
            foreach (var (category, what, obj) in ids) Probe("Q9", "[" + category + "] " + what + ": " + Id(obj));
            Probe("Q9", "RESULT: " + string.Join("; ", ids.GroupBy(p => p.category).Select(g => g.Key + ": " + SignSummary(g.Select(p => p.obj)))));
            Assert.IsTrue(ids.All(p => p.obj == null || p.obj.GetInstanceID() != 0), "an instance id of 0");
        }

        static string SignSummary(IEnumerable<UnityObject> items)
        {
            var list = items.Where(item => item != null).Select(item => item.GetInstanceID()).ToList();
            var negative = list.Count(id => id < 0);
            var positive = list.Count(id => id > 0);
            if (list.Count == 0) return "none";
            if (negative == list.Count) return "all negative (" + negative + ")";
            if (positive == list.Count) return "all positive (" + positive + ")";
            return negative + " negative, " + positive + " positive";
        }

        // ================================================================== Q3: Scene.path of a bundle scene

        [UnityTest, Order(4)]
        public IEnumerator Q03_BundleScenePath_MatchesGetAllScenePaths()
        {
            Begin();
            EnsureBundles();
            var entries = scenesBundle.GetAllScenePaths();
            var entry = ScenePath(SceneA);
            Probe("Q3", "bundle.GetAllScenePaths() = [" + string.Join(", ", entries.Select(e => "'" + e + "'")) + "]; source asset '"
                        + ScenePathOf(SceneA) + "'");
            DisableGuard();
            var loaded = Capture();
            var mismatches = new List<string>();
            foreach (var byName in new[] { true, false })
            {
                loaded.Clear();
                var request = byName ? Path.GetFileNameWithoutExtension(entry) : entry;
                var op = SceneManager.LoadSceneAsync(request, LoadSceneMode.Additive);
                Assert.IsNotNull(op, "LoadSceneAsync returned null for " + request);
                var deadline = Time.realtimeSinceStartup + Timeout;
                while (!op.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(op.isDone, "loading '" + request + "' did not finish within " + Timeout + " s");
                var announced = loaded.LastOrDefault(e => e.SceneName == SceneA);
                var scene = announced != null ? announced.Scene : FindLoading(entry);
                var path = scene.path ?? "";
                var exact = string.Equals(path, entry, StringComparison.Ordinal);
                var ignoringCase = string.Equals(path, entry, StringComparison.OrdinalIgnoreCase);
                Probe("Q3", "[loaded by " + (byName ? "name '" : "path '") + request + "'] Scene.path '" + path + "', Scene.name '" + scene.name
                            + "', buildIndex " + scene.buildIndex + "; equals the GetAllScenePaths entry: ordinal " + YesNo(exact)
                            + ", ignoring case " + YesNo(ignoringCase) + "; extension '.unity' on both: "
                            + YesNo(path.EndsWith(".unity", StringComparison.Ordinal) && entry.EndsWith(".unity", StringComparison.Ordinal))
                            + "; 'Assets/' prefix: Scene.path " + YesNo(path.StartsWith("Assets/", StringComparison.Ordinal)) + ", entry "
                            + YesNo(entry.StartsWith("Assets/", StringComparison.Ordinal)) + "; GetSceneByPath(entry) finds it: "
                            + YesNo(SceneManager.GetSceneByPath(entry).handle == scene.handle) + "; sceneLoaded saw path '" + (announced?.ScenePath ?? "-") + "'");
                if (!ignoringCase || scene.buildIndex >= 0) mismatches.Add(byName ? "by name" : "by path");
                if (scene.IsValid() && scene.isLoaded)
                {
                    var unload = SceneManager.UnloadSceneAsync(scene);
                    deadline = Time.realtimeSinceStartup + Timeout;
                    while (unload != null && !unload.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.IsTrue(unload == null || unload.isDone, "unloading the scene did not finish within " + Timeout + " s");
                }
            }
            Probe("Q3", "RESULT: Scene.path " + (mismatches.Count == 0 ? "matches" : "does NOT match")
                        + " the bundle's GetAllScenePaths entry (case-insensitively, with buildIndex < 0) for loads "
                        + (mismatches.Count == 0 ? "by name and by path" : Names(mismatches)));
            Assert.IsEmpty(mismatches, "PendingWorld.Matches/IsBundleScene compare Scene.path with GetAllScenePaths (OrdinalIgnoreCase) and need buildIndex < 0");
        }

        // ================================================================== Q1: where async scene activation runs

        [UnityTest, Order(5)]
        public IEnumerator Q01_AsyncActivation_RunsInsideUpdatePreloading()
        {
            Begin();
            EnsureBundles();
            var path = ScenePath(SceneA);
            DisableGuard();
            var loaded = Capture();
            GuardProbeLoop.Install();
            yield return null; // one whole frame with the markers in, so the phase reads are live
            GuardProbeLoop.Records.Clear();
            var caller = GuardProbeLoop.Where;
            var requestFrame = Time.frameCount;
            var op = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            Assert.IsNotNull(op, "LoadSceneAsync returned null for " + path);
            var scene = FindLoading(path);
            var loadedAtOnce = scene.isLoaded;
            string firstLoadedAt = null;
            GuardProbeLoop.OnMarker = label =>
            {
                if (firstLoadedAt == null && scene.IsValid() && scene.isLoaded) firstLoadedAt = label + " (frame +" + (Time.frameCount - requestFrame) + ")";
            };
            var deadline = Time.realtimeSinceStartup + Timeout;
            while (!op.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            GuardProbeLoop.OnMarker = null;

            var awake = GuardProbeLoop.Find("Awake", scene.handle);
            var enable = GuardProbeLoop.Find("OnEnable", scene.handle);
            var announced = loaded.FirstOrDefault(e => e.SceneHandle == scene.handle);
            Probe("Q1", "LoadSceneAsync(bundle scene, Additive) requested " + caller + ", frame " + requestFrame + "; isLoaded right after the call: " + YesNo(loadedAtOnce));
            Probe("Q1", "Awake: " + Describe(awake, requestFrame) + " | OnEnable: " + Describe(enable, requestFrame));
            Probe("Q1", "sceneLoaded: " + Describe(announced, requestFrame) + " | scene.isLoaded first seen true at marker: " + (firstLoadedAt ?? "never"));
            Probe("Q1", "probe MonoBehaviour from the test assembly after the bundle round trip: " + RecorderState(scene));
            var inside = announced != null && announced.InPreloading && (awake == null || awake.InPreloading);
            Probe("Q1", "RESULT: async activation (" + (awake != null ? "Awake" : "Awake not observable; sceneLoaded") + ") ran "
                        + (inside ? "INSIDE" : "OUTSIDE") + " the EarlyUpdate.UpdatePreloading bracket");
            Assert.IsTrue(op.isDone, "the bundle scene did not load within " + Timeout + " s");
            Assert.IsNotNull(announced, "sceneLoaded never fired for the bundle scene");
            Assert.IsTrue(announced.InPreloading, "sceneLoaded ran outside EarlyUpdate.UpdatePreloading");
            if (awake != null) Assert.IsTrue(awake.InPreloading, "Awake ran outside EarlyUpdate.UpdatePreloading");
        }

        // ================================================================== Q2: sync loads during a pending activation

        [UnityTest, Order(6)]
        public IEnumerator Q02_SyncLoads_WhileAnActivationIsPending()
        {
            Begin();
            EnsureBundles();
            var pathA = ScenePath(SceneA);
            var pathB = ScenePath(SceneB);
            var flushAsset = KitAsset(FlushPrefab + ".prefab");
            var extraFile = Path.GetFullPath(Path.Combine(OutputDir, ExtraBundleName));
            DisableGuard();
            var loaded = Capture();
            GuardProbeLoop.Install();
            var flushed = new List<string>();
            var timedOut = new List<string>();
            foreach (var kind in new[] { "AssetBundle.LoadAsset", "AssetBundle.LoadFromFile", "SceneManager.LoadScene" })
            {
                yield return null; // nothing pending from the previous trial
                yield return null;
                GuardProbeLoop.Records.Clear();
                loaded.Clear();
                var requestFrame = Time.frameCount;
                var op = SceneManager.LoadSceneAsync(pathA, LoadSceneMode.Additive);
                Assert.IsNotNull(op, "LoadSceneAsync returned null for " + pathA);
                var scene = FindLoading(pathA);
                AssetBundle extra = null;
                UnityObject asset = null;
                var sceneB = default(Scene);
                string callError = null;
                GuardProbeLoop.CallContext = kind;
                try
                {
                    switch (kind)
                    {
                        case "AssetBundle.LoadAsset":
                            asset = kitBundle.LoadAsset(flushAsset);
                            break;
                        case "AssetBundle.LoadFromFile":
                            extra = AssetBundle.LoadFromFile(extraFile);
                            break;
                        default:
                            sceneB = SceneManager.LoadScene(pathB, new LoadSceneParameters(LoadSceneMode.Additive));
                            break;
                    }
                }
                catch (Exception e)
                {
                    callError = "threw " + e.GetType().Name + ": " + e.Message;
                }
                finally
                {
                    GuardProbeLoop.CallContext = null;
                }
                var loadedAfterCall = scene.IsValid() && scene.isLoaded;
                var doneAfterCall = op.isDone;
                var deadline = Time.realtimeSinceStartup + Timeout;
                while ((!op.isDone || (sceneB.IsValid() && !sceneB.isLoaded)) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!op.isDone || (sceneB.IsValid() && !sceneB.isLoaded)) timedOut.Add(kind + " (loading)");

                var awake = GuardProbeLoop.Find("Awake", scene.handle);
                var announced = loaded.FirstOrDefault(e => e.SceneHandle == scene.handle);
                var announcedB = sceneB.IsValid() ? loaded.FirstOrDefault(e => e.SceneHandle == sceneB.handle) : null;
                var inside = (awake != null && awake.CallContext == kind) || (announced != null && announced.CallContext == kind) || loadedAfterCall;
                if (inside) flushed.Add(kind);
                Probe("Q2", "[" + kind + "] the call returned " + (callError ?? (kind == "AssetBundle.LoadAsset" ? Id(asset) : kind == "AssetBundle.LoadFromFile" ? Id(extra) : "scene handle " + sceneB.handle))
                            + "; right after it: S.isLoaded " + YesNo(loadedAfterCall) + ", S's AsyncOperation.isDone " + YesNo(doneAfterCall));
                Probe("Q2", "[" + kind + "] S Awake: " + Describe(awake, requestFrame) + " | S sceneLoaded: " + Describe(announced, requestFrame)
                            + (sceneB.IsValid() ? " | B (the sync LoadScene) sceneLoaded: " + Describe(announcedB, requestFrame) : ""));
                if (inside && awake != null) Probe("Q2", "[" + kind + "] S Awake stack: " + GuardProbeLoop.ShortStack(awake.Stack, 10));
                Probe("Q2", "[" + kind + "] S was activated INSIDE the call: " + YesNo(inside));
                if (extra != null) extra.Unload(true);
                var unloads = new List<AsyncOperation>();
                if (scene.IsValid() && scene.isLoaded) unloads.Add(SceneManager.UnloadSceneAsync(scene));
                if (sceneB.IsValid() && sceneB.isLoaded) unloads.Add(SceneManager.UnloadSceneAsync(sceneB));
                deadline = Time.realtimeSinceStartup + Timeout;
                while (unloads.Exists(u => u != null && !u.isDone) && Time.realtimeSinceStartup < deadline) yield return null;
                if (unloads.Exists(u => u != null && !u.isDone)) timedOut.Add(kind + " (unloading)");
            }
            Probe("Q2", "RESULT: synchronous calls that activated a pending async bundle scene inside themselves (outside the guard's bracket): "
                        + Names(flushed) + (timedOut.Count > 0 ? "; TIMED OUT: " + Names(timedOut) : ""));
            Assert.IsEmpty(timedOut, "trial steps that did not finish within " + Timeout + " s");
            Assert.IsEmpty(flushed, "a synchronous load activated the pending scene outside EarlyUpdate.UpdatePreloading");
        }

        // ================================================================== Q5: legacy Animation on a prefab asset

        [UnityTest, Order(7)]
        public IEnumerator Q05_LegacyAnimation_ClipsOnAPrefabAsset()
        {
            Begin();
            EnsureBundles();
            DisableGuard();
            var bundleAsset = kitBundle.LoadAsset<GameObject>(KitAsset(LegacyPrefab + ".prefab"));
            Assert.IsNotNull(bundleAsset, "the legacy-animation prefab did not load from the kit bundle");
            var editorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(LegacyPrefab));
            var bundleStates = DescribeAnimation("bundle prefab asset (not instantiated)", bundleAsset);
            DescribeAnimation("AssetDatabase prefab asset (editor)", editorAsset);
            var copy = Track(UnityObject.Instantiate(bundleAsset));
            DescribeAnimation("Instantiate copy (control)", copy);
            Probe("Q5", "runtime equivalents of AnimationUtility.GetAnimationClips: enumeration, GetClipCount, this[name], GetClip(name) and .clip;"
                        + " an Animator has runtimeAnimatorController.animationClips");
            Probe("Q5", "RESULT: foreach (AnimationState s in animation) on a bundle prefab ASSET lists " + bundleStates + " of its 2 clips");
            yield return null;
            Assert.AreEqual(2, bundleStates, "the guard's walk enumerates AnimationStates on the asset; it saw fewer than the asset's clips");
        }

        /// <summary>Logs what each Animation API reports on <paramref name="go"/>; returns how many states foreach listed.</summary>
        static int DescribeAnimation(string label, GameObject go)
        {
            if (go == null)
            {
                Probe("Q5", "[" + label + "] not loaded");
                return -1;
            }
            var animation = go.GetComponent<Animation>();
            var states = new List<string>();
            string enumeration;
            try
            {
                foreach (AnimationState state in animation) states.Add(state == null ? "<null>" : state.name + "(" + (state.clip != null ? state.clip.name : "no clip") + ")");
                enumeration = states.Count + " [" + string.Join(", ", states) + "]";
            }
            catch (Exception e)
            {
                enumeration = "threw " + e.GetType().Name + ": " + e.Message;
            }
            Probe("Q5", "[" + label + "] foreach AnimationState: " + enumeration
                        + "; GetClipCount " + Try(() => animation.GetClipCount().ToString())
                        + "; GetClip('" + LegacyClipA + "') " + Try(() => Id(animation.GetClip(LegacyClipA)))
                        + "; GetClip('" + LegacyClipB + "') " + Try(() => Id(animation.GetClip(LegacyClipB)))
                        + "; this['" + LegacyClipB + "'] " + Try(() => animation[LegacyClipB] == null ? "null" : "state (clip " + Id(animation[LegacyClipB].clip) + ")")
                        + "; .clip " + Try(() => Id(animation.clip))
                        + "; AnimationUtility.GetAnimationClips " + Try(() => "[" + string.Join(", ", AnimationUtility.GetAnimationClips(go).Select(c => c == null ? "<null>" : c.name)) + "]"));
            return states.Count;
        }

        // ================================================================== Q4: AnimationEvents and static methods

        [UnityTest, Order(8)]
        public IEnumerator Q04_AnimationEvents_ReachStaticMethods()
        {
            Begin();
            EnsureBundles();
            DisableGuard();
            var controller = kitBundle.LoadAsset<RuntimeAnimatorController>(KitAsset(EventsController + ".controller"));
            var legacyClip = kitBundle.LoadAsset<AnimationClip>(KitAsset(EventsLegacyClip + ".anim"));
            Assert.IsNotNull(controller, "the events controller did not load from the kit bundle");
            Assert.IsNotNull(legacyClip, "the legacy events clip did not load from the kit bundle");
            var noReceiver = new List<string>();
            Application.LogCallback onLog = (message, stack, type) =>
            {
                if (message != null && message.Contains("has no receiver")) noReceiver.Add(message.Split('\n')[0]);
            };
            Application.logMessageReceived += onLog;
            string mecanim, legacy;
            int mecanimStatics, legacyStatics, mecanimControl, legacyControl;
            try
            {
                // Animator: two explicit evaluations cross every event time; then at least 10 real frames and 0.5 s.
                GuardProbeAnimationReceiver.ResetCounts();
                var animated = Track(new GameObject("GuardProbe Animator"));
                animated.AddComponent<GuardProbeAnimationReceiver>();
                var animator = animated.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.runtimeAnimatorController = controller;
                animator.Update(0f);
                animator.Update(0.15f);
                var deadline = Time.realtimeSinceStartup + 1f;
                for (var frames = 0; frames < 10 || Time.realtimeSinceStartup < deadline - 0.5f; frames++)
                {
                    if (Time.realtimeSinceStartup > deadline) break;
                    yield return null;
                }
                mecanim = GuardProbeAnimationReceiver.Counts();
                mecanimStatics = GuardProbeAnimationReceiver.publicStatic + GuardProbeAnimationReceiver.privateStatic;
                mecanimControl = GuardProbeAnimationReceiver.publicInstance;
                UnityObject.DestroyImmediate(animated);

                // Legacy Animation: real frames past the clip's end (3 s at most).
                GuardProbeAnimationReceiver.ResetCounts();
                var legacyObject = Track(new GameObject("GuardProbe legacy Animation"));
                legacyObject.AddComponent<GuardProbeAnimationReceiver>();
                var animation = legacyObject.AddComponent<Animation>();
                animation.cullingType = AnimationCullingType.AlwaysAnimate;
                animation.playAutomatically = false;
                animation.AddClip(legacyClip, "events");
                var played = animation.Play("events");
                var start = Time.time;
                deadline = Time.realtimeSinceStartup + 3f;
                for (var frames = 0; (frames < 10 || Time.time < start + ClipLength + 0.1f) && Time.realtimeSinceStartup < deadline; frames++) yield return null;
                legacy = GuardProbeAnimationReceiver.Counts() + (played ? "" : " (Play returned false)");
                legacyStatics = GuardProbeAnimationReceiver.publicStatic + GuardProbeAnimationReceiver.privateStatic;
                legacyControl = GuardProbeAnimationReceiver.publicInstance;
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
            Probe("Q4", "[Animator, bundle controller] calls: " + mecanim);
            Probe("Q4", "[legacy Animation, bundle clip] calls: " + legacy);
            Probe("Q4", "'has no receiver' errors: " + Names(noReceiver.Distinct(), 8));
            var reached = mecanimStatics + legacyStatics > 0;
            Probe("Q4", "RESULT: an AnimationEvent " + (reached ? "DOES" : "does not") + " reach a static method on a MonoBehaviour of the animated object"
                        + " (Animator: " + mecanimStatics + ", legacy: " + legacyStatics + " static calls; controls fired: Animator " + mecanimControl
                        + ", legacy " + legacyControl + ")" + (reached ? " - SECURITY-RELEVANT" : ""));
            Assert.AreEqual(0, mecanimStatics + legacyStatics, "AnimationEvents called static methods");
            if (mecanimControl == 0 || legacyControl == 0)
                Assert.Inconclusive("control failed: the public instance receiver did not fire (Animator " + mecanimControl + ", legacy " + legacyControl + ")");
        }

        // ================================================================== Q7: hide-flagged objects when their scene unloads

        [UnityTest, Order(9)]
        public IEnumerator Q07_HideFlaggedObjects_WhenTheirBundleSceneUnloads()
        {
            Begin();
            EnsureBundles();
            DisableGuard();
            var path = ScenePath(SceneHide);
            var op = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            Assert.IsNotNull(op, "LoadSceneAsync returned null for " + path);
            var scene = FindLoading(path);
            var deadline = Time.realtimeSinceStartup + Timeout;
            while (!op.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(op.isDone && scene.isLoaded, "the hide-flags scene did not load within " + Timeout + " s");
            var handle = scene.handle;

            var objects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true)) objects[transform.name] = transform.gameObject;
            }
            foreach (var pair in runtimeFlags)
            {
                if (objects.TryGetValue(pair.Key, out var go)) go.hideFlags = pair.Value; // set at runtime, as content could
            }
            var authored = objects.TryGetValue(AuthoredHidden, out var authoredObject) ? authoredObject : null;
            Probe("Q7", "authored HideInHierarchy object after the bundle round trip: " + (authored == null ? "not in the root list" : "hideFlags " + authored.hideFlags));
            var listed = new HashSet<string>(scene.GetRootGameObjects().Select(g => g.name));
            var omitted = runtimeFlags.Keys.Where(n => !n.StartsWith("HF_Child_", StringComparison.Ordinal) && !listed.Contains(n)).ToList();
            // The guard's SceneRoots adds, to the root list, the root of every GameObject FindObjectsOfTypeAll says is in the scene.
            var inSceneByScan = new HashSet<string>(Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(g => g != null && g.scene == scene).Select(g => g.transform.root.name));
            // The guard's third lookup (SceneLessRoots): live roots in no scene that carry hiding or persisting bits.
            var sceneLess = new HashSet<string>(Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(g => g != null && !g.scene.IsValid() && g.transform.parent == null && (g.hideFlags & HidingOrPersisting) != 0
                            && !(g.activeSelf && !g.activeInHierarchy))
                .Select(g => g.name));
            var flaggedRoots = runtimeFlags.Keys.Select(n => n.StartsWith("HF_Child_", StringComparison.Ordinal) ? "HF_Parent" : n).Distinct().ToList();
            var unseenByRootLookups = flaggedRoots.Where(n => !listed.Contains(n) && !inSceneByScan.Contains(n)).ToList();
            var unseen = unseenByRootLookups.Where(n => !sceneLess.Contains(n)).ToList();
            Probe("Q7", "before unload, scene.GetRootGameObjects lists " + listed.Count + " (rootCount " + scene.rootCount + "); omits runtime-flagged roots: "
                        + Names(omitted) + "; of those, FindObjectsOfTypeAll with .scene == scene finds: " + Names(omitted.Where(inSceneByScan.Contains))
                        + "; roots neither of those finds: " + Names(unseenByRootLookups) + "; of those, the guard's scene-less scan finds: "
                        + Names(unseenByRootLookups.Where(sceneLess.Contains)));

            var unload = SceneManager.UnloadSceneAsync(scene);
            Assert.IsNotNull(unload, "UnloadSceneAsync returned null");
            deadline = Time.realtimeSinceStartup + Timeout;
            while (!unload.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(unload.isDone, "unloading the hide-flags scene did not finish within " + Timeout + " s");
            yield return null; // a frame for any deferred destroy

            var anchor = Track(new GameObject("GuardProbe DDOL anchor"));
            UnityObject.DontDestroyOnLoad(anchor);
            var ddolRoots = new HashSet<GameObject>(anchor.scene.GetRootGameObjects());
            var all = new HashSet<GameObject>(Resources.FindObjectsOfTypeAll<GameObject>());
            var survivors = new List<string>();
            var uncovered = new List<string>();
            foreach (var pair in objects)
            {
                var go = pair.Value;
                if (go == null)
                {
                    Probe("Q7", "[" + pair.Key + "] destroyed with its scene");
                    continue;
                }
                Track(go);
                survivors.Add(pair.Key + " (" + go.hideFlags + ")");
                // What the guard now relies on: a survivor is a scene-less root with hiding or persisting bits, which its
                // SceneLessRoots scan judges with the world (Q7b checks that end to end).
                if (go.scene.IsValid() || go.transform.parent != null || (go.hideFlags & HidingOrPersisting) == 0) uncovered.Add(pair.Key);
                Probe("Q7", "[" + pair.Key + ", hideFlags " + go.hideFlags + "] SURVIVED: " + DescribeScene(go.scene, handle, "the unloaded scene")
                            + "; parent " + (go.transform.parent == null ? "none" : "'" + go.transform.parent.name + "'")
                            + "; activeSelf " + go.activeSelf + "; in DontDestroyOnLoad roots " + YesNo(ddolRoots.Contains(go))
                            + "; Resources.FindObjectsOfTypeAll " + YesNo(all.Contains(go)) + "; GameObject.Find " + YesNo(GameObject.Find(pair.Key) != null));
            }
            Probe("Q7", "RESULT (raw Unity, guard off): objects of an unloaded bundle scene that outlived it: " + Names(survivors)
                        + "; survivors that are NOT scene-less roots with hiding/persisting bits (outside the guard's scan): " + Names(uncovered)
                        + "; live hide-flagged roots that no guard lookup (root list, scene scan, scene-less scan) finds: " + Names(unseen));
            Assert.IsEmpty(unseen, "hide-flagged roots of a live bundle scene invisible to all three of the guard's lookups");
            Assert.IsEmpty(uncovered, "objects outlived their unloaded scene in a shape the guard's scene-less scan does not cover");
        }

        // ================================================================== Q8: the static-call hole end to end, and the guard

        [UnityTest, Order(10)]
        public IEnumerator Q08_StaticPersistentCall_WithoutAndWithTheGuard()
        {
            Begin();
            EnsureBundles();
            Assert.IsNotNull(guardSanitizeWhenLoaded, "BSUnityEventGuard.SanitizeWhenLoaded(string, string) not found");
            var path = ScenePath(SceneEvent);
            var name = Path.GetFileNameWithoutExtension(path);

            // Without the guard: a Button from the bundle, invoked as a click would.
            GuardProbeStaticTarget.Reset();
            DisableGuard();
            var op = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            Assert.IsNotNull(op, "LoadSceneAsync returned null for " + path);
            var scene = FindLoading(path);
            var deadline = Time.realtimeSinceStartup + Timeout;
            while (!op.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(op.isDone, "the event scene did not load within " + Timeout + " s");
            var button = FindButton(scene);
            Assert.IsNotNull(button, "no Button in the event scene");
            var callsAsShipped = DescribeCalls(button.onClick);
            var holeError = Try(() =>
            {
                button.onClick.Invoke();
                return null;
            });
            var holeHits = GuardProbeStaticTarget.publicHits + GuardProbeStaticTarget.privateHits;
            Probe("Q8", "[without the guard] persistent calls as loaded: " + callsAsShipped + "; Invoke " + (holeError ?? "ok") + "; static calls made: public "
                        + GuardProbeStaticTarget.publicHits + ", private " + GuardProbeStaticTarget.privateHits + ", argument '" + GuardProbeStaticTarget.lastArgument + "'");
            var unload = SceneManager.UnloadSceneAsync(scene);
            deadline = Time.realtimeSinceStartup + Timeout;
            while (unload != null && !unload.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(unload == null || unload.isDone, "unloading the event scene did not finish within " + Timeout + " s");
            EnableGuard();

            // Through the guard's entry point, as BSAssetBundle.SetupSceneBundle does (Additive here, so UTF's scene stays).
            GuardProbeStaticTarget.Reset();
            var guardLog = new List<string>();
            Application.LogCallback onLog = (message, stack, type) =>
            {
                if (message != null && message.StartsWith("[UnityEventGuard]", StringComparison.Ordinal)) guardLog.Add(type + ": " + message.Replace("\n", " | "));
            };
            Application.logMessageReceived += onLog;
            var passed = false;
            var guarded = default(Scene);
            var guardedDone = false;
            try
            {
                var pending = guardSanitizeWhenLoaded.Invoke(null, new object[] { path, name });
                var complete = pending?.GetType().GetMethod("Complete", AnyInstance, null, Type.EmptyTypes, null);
                Assert.IsNotNull(complete, "PendingWorld.Complete() not found");
                using ((IDisposable)pending)
                {
                    var suppressed = SuppressFailClosedVoid();
                    var guardedOp = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
                    Assert.IsNotNull(guardedOp, "LoadSceneAsync returned null for " + name);
                    guarded = FindLoading(path);
                    deadline = Time.realtimeSinceStartup + Timeout;
                    while (!guardedOp.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                    guardedDone = guardedOp.isDone;
                    passed = (bool)complete.Invoke(pending, null);
                    Probe("Q8", "[with the guard] DisabledForTests " + (HasDisabledForTests ? GuardDisabledFlag.ToString() : "absent")
                                + "; PendingWorld.Complete() = " + passed
                                + (suppressed ? " (a refusal would not load Void: BSAssetBundle.sceneLoadGeneration was bumped)" : ""));
                }
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
            Assert.IsTrue(guardedDone, "the guarded load did not finish within " + Timeout + " s");
            var guardedButton = FindButton(guarded);
            Assert.IsNotNull(guardedButton, "no Button in the guarded scene");
            var callsAfterGuard = DescribeCalls(guardedButton.onClick);
            var blockedError = Try(() =>
            {
                guardedButton.onClick.Invoke();
                return null;
            });
            var blockedHits = GuardProbeStaticTarget.publicHits + GuardProbeStaticTarget.privateHits;
            // A graph can call SetPersistentListenerState; the cleared names must keep the calls dead.
            for (var i = 0; i < guardedButton.onClick.GetPersistentEventCount(); i++)
                guardedButton.onClick.SetPersistentListenerState(i, UnityEventCallState.RuntimeOnly);
            var revivedError = Try(() =>
            {
                guardedButton.onClick.Invoke();
                return null;
            });
            var revivedHits = GuardProbeStaticTarget.publicHits + GuardProbeStaticTarget.privateHits - blockedHits;
            Probe("Q8", "[with the guard] calls after judging: " + callsAfterGuard + "; Invoke " + (blockedError ?? "ok") + " -> " + blockedHits
                        + " static call(s); after switching them back on: Invoke " + (revivedError ?? "ok") + " -> " + revivedHits + " static call(s)");
            Probe("Q8", "[with the guard] guard log: " + Names(guardLog, 6));
            Probe("Q8", "RESULT: hole without the guard " + (holeHits > 0 ? "EXISTS (" + holeHits + " static call(s) from bundle data)" : "did NOT reproduce")
                        + "; through BSUnityEventGuard.SanitizeWhenLoaded the statics were " + (blockedHits + revivedHits == 0 ? "BLOCKED" : "NOT blocked")
                        + " (world " + (passed ? "passed" : "refused") + ")");
            Assert.AreEqual(0, blockedHits + revivedHits, "a static persistent call from a bundle scene ran after the guard judged the scene");
            if (holeHits == 0) Assert.Inconclusive("control failed: the static did not run without the guard either, so 'blocked' proves nothing");
        }

        // ================================================================== Q7b: a root hidden by its own Awake, through the guard

        [UnityTest, Order(12)]
        public IEnumerator Q07b_RootHiddenAtAwake_IsJudgedByTheGuard()
        {
            Begin();
            EnsureBundles();
            Assert.IsNotNull(guardSanitizeWhenLoaded, "BSUnityEventGuard.SanitizeWhenLoaded(string, string) not found");
            var path = ScenePath(SceneHideAwake);
            var name = Path.GetFileNameWithoutExtension(path);

            // Control, guard off: the root its Awake hid leaves its scene and outlives the unload (raw Unity, as in Q7).
            DisableGuard();
            GuardProbeHideOnAwake.Reset();
            var op = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            Assert.IsNotNull(op, "LoadSceneAsync returned null for " + path);
            var rawScene = FindLoading(path);
            var deadline = Time.realtimeSinceStartup + Timeout;
            while (!op.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(op.isDone, "the hide-at-Awake scene did not load within " + Timeout + " s");
            var rawHidden = GuardProbeHideOnAwake.hidden;
            if (GuardProbeHideOnAwake.awakes == 0 || rawHidden == null)
                Assert.Inconclusive("the hiding script's Awake did not run (did it survive the bundle build? " + RecorderState(rawScene) + ")");
            var rawFacts = "scene valid right after the hide " + YesNo(GuardProbeHideOnAwake.sceneValidAfterHide) + ", after the load "
                           + DescribeScene(rawHidden.scene, rawScene.handle, "the world scene");
            var unload = SceneManager.UnloadSceneAsync(rawScene);
            deadline = Time.realtimeSinceStartup + Timeout;
            while (unload != null && !unload.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(unload == null || unload.isDone, "unloading the raw scene did not finish within " + Timeout + " s");
            yield return null;
            var rawSurvived = rawHidden != null;
            Probe("Q7b", "[guard off] Awake gave the root HideAndDontSave: " + rawFacts + "; outlived the unload: " + YesNo(rawSurvived));
            if (rawSurvived) UnityObject.DestroyImmediate(rawHidden); // not this world's business any more

            // Through the guard's pending-world entry point, guard on, as Q8's second half.
            EnableGuard();
            GuardProbeHideOnAwake.Reset();
            var guardLog = new List<string>();
            Application.LogCallback onLog = (message, stack, type) =>
            {
                if (message != null && message.StartsWith("[UnityEventGuard]", StringComparison.Ordinal)) guardLog.Add(type + ": " + message.Replace("\n", " | "));
            };
            Application.logMessageReceived += onLog;
            var passed = false;
            var world = default(Scene);
            var loadDone = false;
            try
            {
                var pending = guardSanitizeWhenLoaded.Invoke(null, new object[] { path, name });
                var complete = pending?.GetType().GetMethod("Complete", AnyInstance, null, Type.EmptyTypes, null);
                Assert.IsNotNull(complete, "PendingWorld.Complete() not found");
                using ((IDisposable)pending)
                {
                    SuppressFailClosedVoid();
                    var guardedOp = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
                    Assert.IsNotNull(guardedOp, "LoadSceneAsync returned null for " + name);
                    world = FindLoading(path);
                    deadline = Time.realtimeSinceStartup + Timeout;
                    while (!guardedOp.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                    loadDone = guardedOp.isDone;
                    passed = (bool)complete.Invoke(pending, null);
                }
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
            Assert.IsTrue(loadDone, "the guarded load did not finish within " + Timeout + " s");
            var hidden = GuardProbeHideOnAwake.hidden;
            Assert.IsNotNull(hidden, "the hiding script's Awake did not run in the guarded load");
            var flagsNow = hidden.hideFlags;
            var cleared = (flagsNow & HidingOrPersisting) == 0;
            var inWorld = world.IsValid() && hidden.scene.handle == world.handle;
            var listed = world.IsValid() && world.isLoaded && world.GetRootGameObjects().Contains(hidden);
            Probe("Q7b", "[guard on] DisabledForTests " + (HasDisabledForTests ? GuardDisabledFlag.ToString() : "absent") + "; world " + (passed ? "passed" : "refused")
                         + "; after the verdict the root has hideFlags " + flagsNow + " (hiding/persisting bits cleared: " + YesNo(cleared) + "), scene "
                         + DescribeScene(hidden.scene, world.handle, "the world scene") + ", in the world's GetRootGameObjects: " + YesNo(listed)
                         + ", activeSelf " + hidden.activeSelf);
            Probe("Q7b", "[guard on] guard log: " + Names(guardLog, 6));
            Probe("Q7b", "ScriptMachine hold until the verdict: not probed (needs a Visual Scripting graph and VS references in this assembly)");

            unload = world.IsValid() && world.isLoaded ? SceneManager.UnloadSceneAsync(world) : null;
            deadline = Time.realtimeSinceStartup + Timeout;
            while (unload != null && !unload.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(unload == null || unload.isDone, "unloading the guarded world did not finish within " + Timeout + " s");
            yield return null; // a frame for the destroy
            var destroyedAtUnload = hidden == null;
            if (!destroyedAtUnload) Track(hidden);
            Probe("Q7b", "RESULT: a bundle-scene root hidden (HideAndDontSave) by its own Awake was, through the guard, "
                         + (cleared && inWorld ? "FOUND: bits cleared and moved back into the world scene" : "NOT moved back with its bits cleared")
                         + ", and " + (destroyedAtUnload ? "destroyed with the world at unload" : "OUTLIVED the world's unload")
                         + "; without the guard it " + (rawSurvived ? "outlived its scene (the hole)" : "did not outlive its scene"));
            Assert.IsTrue((cleared && inWorld) || destroyedAtUnload, "the guard neither brought the hidden root back into its world nor destroyed it at unload");
            Assert.IsTrue(destroyedAtUnload, "a world root hidden by its own Awake outlived the world through the guard");
            if (!rawSurvived) Assert.Inconclusive("control: without the guard the hidden root did not outlive its scene, so the guarded result proves little");
        }

        // ================================================================== Q11: runtime clip.events edits on bundle clips

        [UnityTest, Order(11)]
        public IEnumerator Q11_RuntimeClipEventEdit_SeenByPlayingAnimators()
        {
            Begin();
            EnsureBundles();
            DisableGuard();
            var controller = kitBundle.LoadAsset<RuntimeAnimatorController>(KitAsset(EditController + ".controller"));
            var legacyClip = kitBundle.LoadAsset<AnimationClip>(KitAsset(EditLegacyClip + ".anim"));
            Assert.IsNotNull(controller, "the Q11 controller did not load from the kit bundle");
            Assert.IsNotNull(legacyClip, "the Q11 legacy clip did not load from the kit bundle");
            var mecanimClip = controller.animationClips.FirstOrDefault(c => c != null);
            Assert.IsNotNull(mecanimClip, "the Q11 controller has no clip");

            // Both play, looping, at once; each receiver counts its own calls.
            var animated = Track(new GameObject("GuardProbe Q11 Animator"));
            var animatorReceiver = animated.AddComponent<GuardProbeAnimationReceiver>();
            var animator = animated.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = controller;
            var legacyObject = Track(new GameObject("GuardProbe Q11 legacy Animation"));
            var legacyReceiver = legacyObject.AddComponent<GuardProbeAnimationReceiver>();
            var animation = legacyObject.AddComponent<Animation>();
            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            animation.playAutomatically = false;
            animation.AddClip(legacyClip, "edit");
            animation["edit"].wrapMode = WrapMode.Loop;
            var played = animation.Play("edit");

            // Window A, the control: both events fire on the unedited clips.
            var windowEnd = Time.realtimeSinceStartup + EditWindow;
            var hardDeadline = windowEnd + Timeout;
            for (var frames = 0; frames < 10 || Time.realtimeSinceStartup < windowEnd; frames++)
            {
                if (Time.realtimeSinceStartup > hardDeadline) break;
                yield return null;
            }
            var before = "Animator kept " + animatorReceiver.publicCalls + " / to-remove " + animatorReceiver.privateCalls
                         + "; legacy kept " + legacyReceiver.publicCalls + " / to-remove " + legacyReceiver.privateCalls;
            var controlsFired = animatorReceiver.publicCalls > 0 && animatorReceiver.privateCalls > 0 && legacyReceiver.publicCalls > 0
                                && legacyReceiver.privateCalls > 0;

            // The edit, as BSUnityEventGuard's SetEvents does in Play mode: the runtime setter, on bundle clips, mid-play, and
            // on the legacy state's own clip too if AddClip holds a copy. Code here runs in Update and animation evaluates later
            // in the frame, so every count after a reset comes after the change.
            var stateClip = animation["edit"] != null ? animation["edit"].clip : null;
            var stateClipIsCopy = stateClip != null && !ReferenceEquals(stateClip, legacyClip);
            var changed = new List<AnimationClip> { mecanimClip, legacyClip };
            if (stateClipIsCopy) changed.Add(stateClip);
            var logged = new List<string>();
            var errors = 0;
            Application.LogCallback onLog = (message, stack, type) =>
            {
                logged.Add(type + ": " + (message ?? "").Split('\n')[0]);
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
            };
            string setterThrew = null;
            Application.logMessageReceived += onLog;
            try
            {
                foreach (var clip in changed)
                    clip.events = clip.events.Where(e => e.functionName != GuardProbeAnimationReceiver.PrivateInstanceName).ToArray();
            }
            catch (Exception e)
            {
                setterThrew = e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
            var eventsNow = string.Join("; ", changed.Select(c => "'" + c.name + "' [" + string.Join(", ", c.events.Select(e => e.functionName)) + "]"));
            animatorReceiver.publicCalls = animatorReceiver.privateCalls = 0;
            legacyReceiver.publicCalls = legacyReceiver.privateCalls = 0;

            // Window B, raw Unity: the events changed, nothing refreshed.
            windowEnd = Time.realtimeSinceStartup + EditWindow;
            hardDeadline = windowEnd + Timeout;
            for (var frames = 0; frames < 10 || Time.realtimeSinceStartup < windowEnd; frames++)
            {
                if (Time.realtimeSinceStartup > hardDeadline) break;
                yield return null;
            }
            var rawAnimatorKept = animatorReceiver.publicCalls;
            var rawAnimatorRemoved = animatorReceiver.privateCalls;
            var rawLegacyKept = legacyReceiver.publicCalls;
            var rawLegacyRemoved = legacyReceiver.privateCalls;

            // The guard's fix: RefreshLegacyAnimations re-adds every legacy state on a changed clip, state restored.
            var stateBefore = animation["edit"];
            var playingBefore = animation.isPlaying;
            var timeBefore = stateBefore != null ? stateBefore.time : -1f;
            string refreshThrew = guardRefreshLegacy == null ? "BSUnityEventGuard.RefreshLegacyAnimations(ICollection<AnimationClip>) not found" : null;
            var refreshed = -1;
            try
            {
                if (guardRefreshLegacy != null) refreshed = (int)guardRefreshLegacy.Invoke(null, new object[] { changed });
            }
            catch (Exception e)
            {
                var inner = (e as TargetInvocationException)?.InnerException ?? e;
                refreshThrew = inner.GetType().Name + ": " + inner.Message;
            }
            var stateAfter = animation["edit"];
            var afterRefresh = "isPlaying " + playingBefore + " -> " + animation.isPlaying + ", state time " + timeBefore.ToString("F3") + " -> "
                               + (stateAfter != null ? stateAfter.time.ToString("F3") + ", wrap " + stateAfter.wrapMode + ", enabled " + stateAfter.enabled : "no state")
                               + ", default clip " + Id(animation.clip);
            animatorReceiver.publicCalls = animatorReceiver.privateCalls = 0;
            legacyReceiver.publicCalls = legacyReceiver.privateCalls = 0;

            // Window C: after the refresh, the removed event must not fire on either; the kept one must.
            windowEnd = Time.realtimeSinceStartup + EditWindow;
            hardDeadline = windowEnd + Timeout;
            for (var frames = 0; frames < 10 || Time.realtimeSinceStartup < windowEnd; frames++)
            {
                if (Time.realtimeSinceStartup > hardDeadline) break;
                yield return null;
            }
            var animatorRemoved = rawAnimatorRemoved + animatorReceiver.privateCalls; // an Animator needs no refresh: never, in B or C
            var legacyRemoved = legacyReceiver.privateCalls;
            var keptFired = animatorReceiver.publicCalls > 0 && legacyReceiver.publicCalls > 0;
            Probe("Q11", "before the edit (" + EditWindow + " s, looping " + ClipLength + " s clips; legacy Play " + played + "): " + before);
            Probe("Q11", "clip.events setter on bundle clips in Play mode: " + (setterThrew ?? "no exception") + "; " + errors
                         + " error(s) logged during it; log lines: " + Names(logged, 6) + "; the legacy state's clip is "
                         + (stateClip == null ? "missing" : stateClipIsCopy ? "a COPY (edited too)" : "the bundle clip itself") + "; events now: " + eventsNow);
            Probe("Q11", "WITHOUT a refresh (raw Unity, " + EditWindow + " s): Animator kept " + rawAnimatorKept + " / removed " + rawAnimatorRemoved
                         + "; legacy kept " + rawLegacyKept + " / removed " + rawLegacyRemoved);
            Probe("Q11", "BSUnityEventGuard.RefreshLegacyAnimations(changed clips) refreshed " + refreshed + " state(s)"
                         + (refreshThrew != null ? ", THREW " + refreshThrew : "") + "; " + afterRefresh);
            Probe("Q11", "AFTER the refresh (" + EditWindow + " s): Animator kept " + animatorReceiver.publicCalls + " / removed " + animatorReceiver.privateCalls
                         + "; legacy kept " + legacyReceiver.publicCalls + " / removed " + legacyReceiver.privateCalls);
            Probe("Q11", "RESULT: the runtime clip.events setter on a bundle clip " + (errors == 0 && setterThrew == null ? "logs no error" : "LOGS ERRORS OR THROWS")
                         + "; without a refresh a removed event still fires on a playing legacy Animation " + rawLegacyRemoved + " time(s) (Animator "
                         + rawAnimatorRemoved + "); after RefreshLegacyAnimations the removed event fired " + (animatorRemoved + legacyRemoved == 0
                             ? "NEVER on either"
                             : animatorRemoved + " time(s) on the Animator and " + legacyRemoved + " on the legacy Animation")
                         + ", and the kept one " + (keptFired ? "kept firing on both" : "STOPPED")
                         + (legacyRemoved > 0 ? " - LEGACY STILL FIRES AFTER THE REFRESH: legacy AnimationEvents are a documented limit, covered only by the pre-load checker" : "")
                         + (controlsFired ? "" : " (control: not every event fired before the edit)"));
            Assert.AreEqual(0, animatorRemoved, "an AnimationEvent removed at runtime fired on a playing Animator");
            Assert.AreEqual(0, legacyRemoved, "an AnimationEvent removed at runtime still fired on a legacy Animation after RefreshLegacyAnimations");
            Assert.IsNull(setterThrew, "the clip.events setter threw");
            Assert.IsNull(refreshThrew, "RefreshLegacyAnimations threw");
            Assert.AreEqual(0, errors, "setting clip.events in Play mode logged errors: " + Names(logged, 6));
            if (!controlsFired) Assert.Inconclusive("control: not every event fired before the edit (" + before + ")");
            Assert.IsTrue(keptFired, "the kept event stopped firing after the refresh (Animator " + animatorReceiver.publicCalls + ", legacy " + legacyReceiver.publicCalls + ")");
        }

        /// <summary>
        /// A world the guard refuses unloads to Void (a Single load) while it is the latest world load, which would end the
        /// test run's scene; bumping the generation makes FailClosed treat it as replaced instead.
        /// </summary>
        static bool SuppressFailClosedVoid()
        {
            var field = typeof(BSAssetBundle).GetField("sceneLoadGeneration", AnyStatic);
            if (field == null || field.FieldType != typeof(int) || field.IsLiteral) return false;
            field.SetValue(null, (int)field.GetValue(null) + 1);
            return true;
        }

        static Button FindButton(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Button>(true)).FirstOrDefault();
        }

        static string DescribeCalls(UnityEventBase unityEvent)
        {
            var calls = new List<string>();
            for (var i = 0; i < unityEvent.GetPersistentEventCount(); i++)
            {
                var target = unityEvent.GetPersistentTarget(i);
                calls.Add("#" + i + " target " + (target == null ? "none" : target.name) + ", method '" + unityEvent.GetPersistentMethodName(i) + "', "
                          + unityEvent.GetPersistentListenerState(i));
            }
            return calls.Count == 0 ? "none" : string.Join("; ", calls);
        }

        // ================================================================== guard switch

        static bool HasDisabledForTests =>
            (guardDisabledField != null && guardDisabledField.FieldType == typeof(bool) && !guardDisabledField.IsLiteral && !guardDisabledField.IsInitOnly)
            || (guardDisabledField == null && guardDisabledProperty != null && guardDisabledProperty.PropertyType == typeof(bool) && guardDisabledProperty.CanWrite);

        static bool GuardDisabledFlag
        {
            get => guardDisabledField != null ? (bool)guardDisabledField.GetValue(null) : (bool)guardDisabledProperty.GetValue(null);
            set
            {
                if (guardDisabledField != null) guardDisabledField.SetValue(null, value);
                else guardDisabledProperty.SetValue(null, value);
            }
        }

        static string GuardBypass =>
            HasDisabledForTests
                ? "BSUnityEventGuard.DisabledForTests = true"
                : "the guard's sceneLoaded handler taken off (BSUnityEventGuard.DisabledForTests is not in this build; its per-frame steps stay on)";

        /// <summary>Raw-Unity probes: the guard inert. Undone by <see cref="RestoreGuard"/>.</summary>
        static void DisableGuard()
        {
            if (HasDisabledForTests)
            {
                if (!guardDisabledOriginal.HasValue) guardDisabledOriginal = GuardDisabledFlag;
                GuardDisabledFlag = true;
                return;
            }
            UnhookGuard();
        }

        /// <summary>Q8's guarded half: the guard on, as in production.</summary>
        static void EnableGuard()
        {
            if (HasDisabledForTests)
            {
                if (!guardDisabledOriginal.HasValue) guardDisabledOriginal = GuardDisabledFlag;
                GuardDisabledFlag = false;
            }
            RehookGuard();
        }

        /// <summary>Whatever <see cref="DisableGuard"/>/<see cref="EnableGuard"/> changed, back as it was.</summary>
        static void RestoreGuard()
        {
            if (guardDisabledOriginal.HasValue && HasDisabledForTests) GuardDisabledFlag = guardDisabledOriginal.Value;
            guardDisabledOriginal = null;
            RehookGuard();
        }

        static Delegate[] SceneLoadedHandlers()
        {
            var field = typeof(SceneManager).GetField("sceneLoaded", AnyStatic);
            return (field?.GetValue(null) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
        }

        static bool GuardSubscribed() => guardOnSceneLoaded != null && SceneLoadedHandlers().Any(d => d.Method == guardOnSceneLoaded);

        static UnityAction<Scene, LoadSceneMode> GuardHandler() =>
            (UnityAction<Scene, LoadSceneMode>)Delegate.CreateDelegate(typeof(UnityAction<Scene, LoadSceneMode>), guardOnSceneLoaded);

        /// <summary>Fallback for a build without DisabledForTests: the guard's sceneLoaded handler off.</summary>
        static void UnhookGuard()
        {
            Assert.IsNotNull(guardOnSceneLoaded, "BSUnityEventGuard.OnSceneLoaded(Scene, LoadSceneMode) not found");
            if (!GuardSubscribed()) return;
            SceneManager.sceneLoaded -= GuardHandler();
            guardUnhooked = true;
        }

        /// <summary>Puts the guard's handler back, first in line as the guard keeps it.</summary>
        static void RehookGuard()
        {
            if (!guardUnhooked) return;
            guardUnhooked = false;
            if (GuardSubscribed()) return;
            SceneManager.sceneLoaded += GuardHandler();
            guardMoveToFront?.Invoke(null, null);
        }

        static string DescribeSceneLoadedHandlers()
        {
            var handlers = SceneLoadedHandlers();
            var index = guardOnSceneLoaded == null ? -1 : Array.FindIndex(handlers, d => d.Method == guardOnSceneLoaded);
            return index < 0 ? "NOT subscribed (" + handlers.Length + " handlers)" : "subscribed at index " + index + " of " + handlers.Length;
        }

        // ================================================================== helpers

        static void Begin() => LogAssert.ignoreFailingMessages = true;

        static void Probe(string question, string fact) => Debug.Log("[GuardProbe] " + question + ": " + fact);

        static T Track<T>(T item) where T : UnityObject
        {
            if (item != null) spawned.Add(item);
            return item;
        }

        static void EnsureBundles()
        {
            Assert.IsTrue(Application.isPlaying, "the probes run in Play mode");
            if (scenesBundle == null) scenesBundle = LoadBundle(ScenesBundleName);
            if (kitBundle == null) kitBundle = LoadBundle(KitBundleName);
        }

        static AssetBundle LoadBundle(string name)
        {
            foreach (var loaded in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (loaded != null && loaded.name == name) return loaded;
            }
            var file = Path.GetFullPath(Path.Combine(OutputDir, name));
            Assert.IsTrue(File.Exists(file), "bundle missing (IPrebuildSetup did not build it): " + file);
            var bundle = AssetBundle.LoadFromFile(file);
            Assert.IsNotNull(bundle, "AssetBundle.LoadFromFile failed: " + file);
            return bundle;
        }

        static string ScenePath(string name)
        {
            var path = scenesBundle.GetAllScenePaths()
                .FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(path, "scene " + name + " is not in the scenes bundle");
            return path;
        }

        static string KitAsset(string fileName)
        {
            var asset = kitBundle.GetAllAssetNames()
                .FirstOrDefault(n => n.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(asset, fileName + " is not in the kit bundle (" + string.Join(", ", kitBundle.GetAllAssetNames()) + ")");
            return asset;
        }

        /// <summary>The scene a LoadSceneAsync just added (the scene list holds it, not yet loaded, from the call on).</summary>
        static Scene FindLoading(string path)
        {
            var scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid()) return scene;
            return SceneManager.sceneCount > 0 ? SceneManager.GetSceneAt(SceneManager.sceneCount - 1) : default;
        }

        /// <summary>Records every sceneLoaded from now until the probe ends.</summary>
        static List<GuardProbeLoop.Entry> Capture()
        {
            var entries = new List<GuardProbeLoop.Entry>();
            UnityAction<Scene, LoadSceneMode> handler = (scene, mode) => entries.Add(GuardProbeLoop.Snapshot("sceneLoaded", scene.name, scene));
            SceneManager.sceneLoaded += handler;
            loadedHandlers.Add(handler);
            return entries;
        }

        static string RecorderState(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return "scene not loaded";
            var present = 0;
            var missing = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                present += root.GetComponentsInChildren<GuardProbeActivationRecorder>(true).Length;
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root);
            }
            return present + " present, " + missing + " missing script(s)";
        }

        static string Describe(GuardProbeLoop.Entry entry, int requestFrame) => entry == null ? "not observed" : entry.Describe(requestFrame);

        struct LoopFacts
        {
            public int Preloading, Before, After, GuardSystems, Untyped;
            public string Around;
        }

        static LoopFacts ReadLoop(PlayerLoopSystem loop)
        {
            var facts = new LoopFacts { Preloading = -1, Before = -1, After = -1, Around = "EarlyUpdate not found" };
            var beforeType = guardType?.GetNestedType("GuardBeforeSceneActivation", BindingFlags.NonPublic | BindingFlags.Public);
            var afterType = guardType?.GetNestedType("GuardAfterSceneActivation", BindingFlags.NonPublic | BindingFlags.Public);
            facts.GuardSystems = CountGuardSystems(loop);
            foreach (var phase in loop.subSystemList ?? Array.Empty<PlayerLoopSystem>())
            {
                if (phase.type != typeof(EarlyUpdate)) continue;
                var systems = phase.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                for (var i = 0; i < systems.Length; i++)
                {
                    var type = systems[i].type;
                    if (type == null) facts.Untyped++;
                    else if (type == typeof(EarlyUpdate.UpdatePreloading)) facts.Preloading = i;
                    else if (beforeType != null && type == beforeType) facts.Before = i;
                    else if (afterType != null && type == afterType) facts.After = i;
                }
                var from = Math.Max(0, (facts.Preloading >= 0 ? facts.Preloading : 0) - 2);
                facts.Around = string.Join(", ", systems.Skip(from).Take(5).Select(s => s.type == null ? "<no type>" : s.type.Name));
            }
            return facts;
        }

        static int CountGuardSystems(PlayerLoopSystem system)
        {
            var count = guardType != null && system.type != null && system.type.DeclaringType == guardType ? 1 : 0;
            foreach (var child in system.subSystemList ?? Array.Empty<PlayerLoopSystem>()) count += CountGuardSystems(child);
            return count;
        }

        static string DescribeLoop(LoopFacts facts) =>
            "EarlyUpdate.UpdatePreloading at " + facts.Preloading + ", GuardBeforeSceneActivation at " + facts.Before + ", GuardAfterSceneActivation at "
            + facts.After + ", guard systems in the loop " + facts.GuardSystems + ", EarlyUpdate systems with no type " + facts.Untyped
            + "; around UpdatePreloading: [" + facts.Around + "]";

        static string DescribeScene(Scene scene, int handle, string handleLabel)
        {
            if (!scene.IsValid()) return "no valid scene (handle " + scene.handle + ")";
            return "'" + scene.name + "' (handle " + scene.handle + (scene.handle == handle ? ", " + handleLabel : "") + ", isLoaded " + scene.isLoaded + ")";
        }

        static string Id(UnityObject item) =>
            item == null ? "null" : "'" + item.name + "' id " + item.GetInstanceID() + (item.GetInstanceID() < 0 ? " (negative)" : " (positive)");

        static string YesNo(bool value) => value ? "yes" : "no";

        static string Names(IEnumerable<string> names, int max = 40)
        {
            var list = names.ToList();
            if (list.Count == 0) return "none";
            return string.Join(", ", list.Take(max)) + (list.Count > max ? " ... (" + list.Count + ")" : "");
        }

        static string Try(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                return "threw " + e.GetType().Name + ": " + e.Message.Split('\n')[0];
            }
        }
    }
}
#endif
