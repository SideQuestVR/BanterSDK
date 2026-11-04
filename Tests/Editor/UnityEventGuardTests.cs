using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Unity.VisualScripting;
using UnityObject = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// BSUnityEventGuard. A persistent UnityEvent call is serialized "target + type name + method name (+ argument)" data
    /// that Unity resolves with GetValidMethodInfo (public and non-public, instance and static), so bundle content
    /// could call any static method in the app. These tests write that data through Unity's serializer the way a bundle
    /// carries it, then check what the guard keeps. Every call target is a probe in this file. The scene path's
    /// sceneLoaded hook and its fail-closed unload need Play mode; these tests drive the judging functions directly.
    /// </summary>
    public class UnityEventGuardTests
    {
        static int staticHits;
        static string lastArgument;

        // Private statics: what a call with no target object can reach through its type-name string.
        static void StaticHit() => staticHits++;

        static void StaticHitWithArgument(string value)
        {
            staticHits++;
            lastArgument = value;
        }

        /// <summary>A static with the same name and argument mode as <see cref="Probe.Hit"/>.</summary>
        public static class StaticTwin
        {
            public static int hits;

            static void Hit() => hits++;
        }

        public class Probe : MonoBehaviour
        {
            public int hits;
            public ScriptableObject data;

            void PrivateHit() => hits++;

            public void PublicHit() => hits++;

            public void Hit() => hits++;

            // Private on UGUI's Button: a Button added later could receive an AnimationEvent by this name.
            public void Press() => hits++;

            // A content sink (TMP's SetText), also private on TMP_InputField; some client behaviours declare it static.
            public void SetText(string value) => hits++;

            // A content sink (UGUI's layout SetDirty), protected on AspectRatioFitter and static on no behaviour.
            public void SetDirty() => hits++;

            // Static on UGUI's Selectable: a sink by this name would not be exempt.
            public void AllSelectablesNoAlloc() => hits++;
        }

        /// <summary>A denied host service in the R3-5 test.</summary>
        public class DeniedService : MonoBehaviour
        {
        }

        /// <summary>Requires the denied service, as an XR Origin rig's components require XROrigin.</summary>
        [RequireComponent(typeof(DeniedService))]
        public class NeedsDeniedService : MonoBehaviour
        {
        }

        /// <summary>A host-style component whose OnDestroy fires a UnityEvent (as Toggle.OnDestroy does via its group).</summary>
        [ExecuteAlways]
        public class DestroySender : MonoBehaviour
        {
            public UnityEvent destroyed = new UnityEvent();

            void OnDestroy() => destroyed.Invoke();
        }

        public class EventHolder : ScriptableObject
        {
            public UnityEvent fired = new UnityEvent();

            public void Ping() => staticHits++;
        }

        [Serializable]
        public class Node
        {
            [SerializeReference] public Node next;
            public UnityEvent fired = new UnityEvent();
        }

        public class DeepChain : MonoBehaviour
        {
            [SerializeReference] public Node head;
        }

        Scene scene;
        GameObject root;
        bool removeHostBefore;
#if GREENFIELD_PROJECT
        bool enforceBefore;
#endif

        [SetUp]
        public void SetUp()
        {
            staticHits = 0;
            lastArgument = null;
            StaticTwin.hits = 0;
            scene = EditorSceneManager.NewPreviewScene();
            root = Make("world root");
            removeHostBefore = BSUnityEventGuard.EnforceHostComponentRemoval;
            BSUnityEventGuard.EnforceHostComponentRemoval = false;
            BSUnityEventGuard.simulateMissingInternals = false;
#if GREENFIELD_PROJECT
            enforceBefore = BanterStubsAllowed.EnforceStrictForBundles;
            BanterStubsAllowed.EnforceStrictForBundles = false;
#endif
        }

        [TearDown]
        public void TearDown()
        {
            BSUnityEventGuard.EnforceHostComponentRemoval = removeHostBefore;
            BSUnityEventGuard.simulateMissingInternals = false;
#if GREENFIELD_PROJECT
            BanterStubsAllowed.EnforceStrictForBundles = enforceBefore;
            WorldActivation.Cancel(); // no test leaves a hold open
#endif
            EditorSceneManager.ClosePreviewScene(scene);
        }

        GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        static string TypeName(Type type) => type.FullName + ", " + type.Assembly.GetName().Name;

        /// <summary>Appends a persistent call to the UnityEvent at <paramref name="eventPath"/>, field by field as a bundle stores it.</summary>
        static void AddSerializedCall(UnityObject owner, string eventPath, UnityObject target, string typeName, string method,
            PersistentListenerMode mode, string stringArgument = "", bool boolArgument = false,
            UnityEventCallState state = UnityEventCallState.EditorAndRuntime, // EditorAndRuntime fires outside Play mode too
            string objectArgumentType = "")
        {
            var serialized = new SerializedObject(owner);
            var calls = serialized.FindProperty(eventPath + ".m_PersistentCalls.m_Calls");
            var index = calls.arraySize;
            calls.InsertArrayElementAtIndex(index);
            var call = calls.GetArrayElementAtIndex(index);
            call.FindPropertyRelative("m_Target").objectReferenceValue = target;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = typeName;
            call.FindPropertyRelative("m_MethodName").stringValue = method;
            call.FindPropertyRelative("m_Mode").intValue = (int)mode;
            call.FindPropertyRelative("m_Arguments.m_ObjectArgument").objectReferenceValue = null;
            call.FindPropertyRelative("m_Arguments.m_ObjectArgumentAssemblyTypeName").stringValue = objectArgumentType;
            call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue = stringArgument;
            call.FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue = boolArgument;
            call.FindPropertyRelative("m_CallState").intValue = (int)state;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        BSUnityEventGuard.Report Sanitize(Func<GameObject, bool> inScope = null) =>
            BSUnityEventGuard.Sanitize(root, inScope ?? (_ => true), "UnityEventGuardTests");

        static bool Found(BSUnityEventGuard.Report report, string fragment)
        {
            foreach (var line in report.Lines)
            {
                if (line.Contains(fragment)) return true;
            }
            return false;
        }

        [Test]
        public void StaticCallsWithNoTarget_RunUntilJudged_ThenCannotBeSwitchedBackOn()
        {
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", null, TypeName(typeof(UnityEventGuardTests)), nameof(StaticHit), PersistentListenerMode.Void);
            AddSerializedCall(button, "m_OnClick", null, TypeName(typeof(UnityEventGuardTests)), nameof(StaticHitWithArgument),
                PersistentListenerMode.String, "from the bundle");

            button.onClick.Invoke();
            Assert.AreEqual(2, staticHits, "the hole: serialized calls ran private static methods with no target object");
            Assert.AreEqual("from the bundle", lastArgument);

            var report = Sanitize();
            Assert.AreEqual(2, report.Disabled);
            Assert.AreEqual(UnityEventCallState.Off, button.onClick.GetPersistentListenerState(0));
            Assert.AreEqual("", button.onClick.GetPersistentMethodName(0), "the method name is cleared");
            button.onClick.Invoke();
            Assert.AreEqual(2, staticHits);

            // Graphs may call SetPersistentListenerState on UI events: the cleared name keeps the call dead.
            button.onClick.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            button.onClick.SetPersistentListenerState(1, UnityEventCallState.EditorAndRuntime);
            button.onClick.Invoke();
            Assert.AreEqual(2, staticHits);
        }

        [Test]
        public void KeptCall_WhoseTargetDies_DoesNotFallBackToTheTypeNameString()
        {
            var probe = root.AddComponent<Probe>();
            var button = root.AddComponent<Button>();
            // Judged (and kept) on the live target's public Probe.Hit; the type string names a static twin.
            AddSerializedCall(button, "m_OnClick", probe, TypeName(typeof(StaticTwin)), nameof(Probe.Hit), PersistentListenerMode.Void);

            Assert.AreEqual(0, Sanitize().Disabled);
            UnityObject.DestroyImmediate(probe);
            button.onClick.Invoke();
            Assert.AreEqual(0, StaticTwin.hits, "with its target gone, Unity resolved the bundle's type-name string");
        }

        [Test]
        public void KeptCall_StillRunsOnItsLiveTarget()
        {
            var probe = root.AddComponent<Probe>();
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", probe, TypeName(typeof(StaticTwin)), nameof(Probe.Hit), PersistentListenerMode.Void);

            Sanitize();
            button.onClick.Invoke();
            Assert.AreEqual(1, probe.hits, "the type name is rebuilt from the live target");
            Assert.AreEqual(0, StaticTwin.hits);
        }

        [Test]
        public void CallShippedSwitchedOff_IsStillCleared()
        {
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", null, TypeName(typeof(UnityEventGuardTests)), nameof(StaticHit),
                PersistentListenerMode.Void, state: UnityEventCallState.Off);

            Assert.AreEqual(1, Sanitize().Disabled);
            button.onClick.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            button.onClick.Invoke();
            Assert.AreEqual(0, staticHits);
        }

        [Test]
        public void NonPublicInstanceMethod_IsDisabled()
        {
            var probe = root.AddComponent<Probe>();
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", probe, TypeName(typeof(Probe)), "PrivateHit", PersistentListenerMode.Void);
            button.onClick.Invoke();
            Assert.AreEqual(1, probe.hits, "Unity binds non-public instance methods too");

            Assert.AreEqual(1, Sanitize().Disabled);
            button.onClick.Invoke();
            Assert.AreEqual(1, probe.hits);
        }

        [Test]
        public void SendMessage_IsDisabled()
        {
            var probe = root.AddComponent<Probe>();
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", root, TypeName(typeof(GameObject)), nameof(GameObject.SendMessage),
                PersistentListenerMode.String, "PrivateHit");

            Assert.AreEqual(1, Sanitize().Disabled);
            Assert.AreEqual(UnityEventCallState.Off, button.onClick.GetPersistentListenerState(0));
            button.onClick.Invoke();
            Assert.AreEqual(0, probe.hits);
        }

        [Test]
        public void ListedPublicMember_IsKept()
        {
            var lamp = Make("lamp", root.transform);
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", lamp, TypeName(typeof(GameObject)), nameof(GameObject.SetActive),
                PersistentListenerMode.Bool, boolArgument: false);

            var report = Sanitize();
            Assert.AreEqual(0, report.Disabled);
            Assert.AreEqual(0, report.Reported, "GameObject.SetActive is on the allow-list");
            button.onClick.Invoke();
            Assert.IsFalse(lamp.activeSelf, "the kept call still runs");
        }

        [Test]
        public void TargetOutsideTheWorld_IsDisabled()
        {
            var host = Make("host object");
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", host, TypeName(typeof(GameObject)), nameof(GameObject.SetActive),
                PersistentListenerMode.Bool, boolArgument: false);

            Assert.AreEqual(1, Sanitize(go => go != host).Disabled);
            button.onClick.Invoke();
            Assert.IsTrue(host.activeSelf);
        }

        [Test]
        public void AssetTarget_IsDisabled_UnlessTheContentReachesIt()
        {
            var mesh = new Mesh(); // stands in for a built-in shared resource (Default-Material, the default font...)
            var holder = ScriptableObject.CreateInstance<EventHolder>();
            try
            {
                var button = root.AddComponent<Button>();
                AddSerializedCall(button, "m_OnClick", mesh, TypeName(typeof(Mesh)), nameof(Mesh.Clear), PersistentListenerMode.Void);
                AddSerializedCall(button, "m_OnClick", holder, TypeName(typeof(EventHolder)), nameof(EventHolder.Ping), PersistentListenerMode.Void);

                Assert.AreEqual(2, Sanitize().Disabled, "a mesh is never content-reached; the ScriptableObject is not reached here");

                var reached = Make("reached", root.transform).AddComponent<Button>();
                root.AddComponent<Probe>().data = holder;
                AddSerializedCall(reached, "m_OnClick", holder, TypeName(typeof(EventHolder)), nameof(EventHolder.Ping), PersistentListenerMode.Void);
                Sanitize();
                Assert.AreEqual(UnityEventCallState.EditorAndRuntime, reached.onClick.GetPersistentListenerState(0),
                    "a ScriptableObject the content itself references stays a valid target");
            }
            finally
            {
                UnityObject.DestroyImmediate(mesh);
                UnityObject.DestroyImmediate(holder);
            }
        }

#if GREENFIELD_PROJECT
        [Test]
        public void UnlistedPublicMember_IsReported_ThenDisabledWhenEnforced()
        {
            var probe = root.AddComponent<Probe>();
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", probe, TypeName(typeof(Probe)), nameof(Probe.PublicHit), PersistentListenerMode.Void);

            var report = Sanitize();
            Assert.AreEqual(0, report.Disabled);
            Assert.AreEqual(1, report.Reported);
            button.onClick.Invoke();
            Assert.AreEqual(1, probe.hits, "report-only: kept");

            BanterStubsAllowed.EnforceStrictForBundles = true;
            report = Sanitize();
            Assert.AreEqual(1, report.Disabled);
            button.onClick.Invoke();
            Assert.AreEqual(1, probe.hits);
        }

        [Test]
        public void ScriptMachineTriggerUnityEvent_IsKept()
        {
            var machine = root.AddComponent<ScriptMachine>();
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", machine, TypeName(typeof(ScriptMachine)), nameof(ScriptMachine.TriggerUnityEvent),
                PersistentListenerMode.String, "clicked");

            var report = Sanitize();
            Assert.AreEqual(0, report.Disabled);
            Assert.AreEqual(0, report.Reported, "the documented UnityEvent sink for graphs");
        }

        [Test]
        public void HostComponents_AreReported_ThenRemovedWhenEnforced()
        {
            Assert.IsFalse(BSUnityEventGuard.IsHostComponentType(typeof(Button)));
            Assert.IsFalse(BSUnityEventGuard.IsHostComponentType(typeof(BSObjectId)));
            Assert.IsFalse(BSUnityEventGuard.IsHostComponentType(typeof(ScriptMachine)));
            Assert.IsFalse(BSUnityEventGuard.IsHostComponentType(typeof(UnityEngine.InputSystem.UI.TrackedDeviceRaycaster)));
            Assert.IsTrue(BSUnityEventGuard.IsHostComponentType(typeof(UnityEngine.InputSystem.PlayerInput)));
            Assert.IsTrue(BSUnityEventGuard.IsHostComponentType(typeof(Probe)), "this test assembly is not a content assembly");

            root.AddComponent<Button>();
            root.AddComponent<Probe>();
            var report = Sanitize();
            Assert.AreEqual(1, report.HostComponents);
            Assert.IsNotNull(root.GetComponent<Probe>(), "report-only: kept");

            BSUnityEventGuard.EnforceHostComponentRemoval = true;
            report = Sanitize();
            Assert.IsFalse(report.Failed);
            Assert.IsNull(root.GetComponent<Probe>(), "removed before any Start");
            Assert.IsNotNull(root.GetComponent<Button>());
        }
#endif

        [Test]
        public void EventTriggerEntries_AreJudged()
        {
            var trigger = root.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            trigger.triggers.Add(entry);
            UnityEventTools.AddVoidPersistentListener(entry.callback, StaticHit);
            entry.callback.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            entry.callback.Invoke(null);
            Assert.AreEqual(1, staticHits);

            Assert.AreEqual(1, Sanitize().Disabled, "an event inside a list of serializable entries");
            entry.callback.Invoke(null);
            Assert.AreEqual(1, staticHits);
        }

        [Test]
        public void InactiveChildren_AreJudged()
        {
            var child = Make("inactive", root.transform);
            child.SetActive(false);
            var button = child.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", null, TypeName(typeof(UnityEventGuardTests)), nameof(StaticHit), PersistentListenerMode.Void);

            Assert.AreEqual(1, Sanitize().Disabled);
            button.onClick.Invoke();
            Assert.AreEqual(0, staticHits);
        }

        [Test]
        public void SceneRoots_AreJudged()
        {
            var button = Make("second root").AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", null, TypeName(typeof(UnityEventGuardTests)), nameof(StaticHit), PersistentListenerMode.Void);

            var report = BSUnityEventGuard.SanitizeScene(scene);
            Assert.IsFalse(report.Failed);
            Assert.AreEqual(1, report.Disabled);
        }

        [Test]
        public void SceneRoots_HiddenOnesAreJudgedToo()
        {
            var hidden = Make("hidden root");
            var button = hidden.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", null, TypeName(typeof(UnityEventGuardTests)), nameof(StaticHit), PersistentListenerMode.Void);
            hidden.hideFlags = HideFlags.HideAndDontSave; // as a bundle's m_ObjectHideFlags can set it
            try
            {
                var report = BSUnityEventGuard.SanitizeScene(scene);
                Assert.IsFalse(report.Failed);
                Assert.AreEqual(1, report.Disabled, "judged whether or not the scene's root list shows it");
                Assert.AreEqual(HideFlags.NotEditable, hidden.hideFlags, "hide and DontSave bits cleared");
            }
            finally
            {
                UnityObject.DestroyImmediate(hidden);
            }
        }

        [Test]
        public void HideFlags_AreClearedOnWorldObjectsAndComponents()
        {
            var child = Make("hidden child", root.transform);
            child.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            var filter = child.AddComponent<MeshFilter>();
            filter.hideFlags = HideFlags.HideAndDontSave;

            var report = Sanitize();
            Assert.IsFalse(report.Failed);
            Assert.AreEqual(HideFlags.None, child.hideFlags);
            Assert.AreEqual(HideFlags.NotEditable, filter.hideFlags, "only the hiding and persisting bits");
            Assert.GreaterOrEqual(report.HideFlagsCleared, 2, "the GameObject and the MeshFilter (Unity may flag the Transform too)");
        }

        [Test]
        public void PersistentCalls_ToTheHideFlagsSetter_AreDisabledUnderAnyRule()
        {
            // Object mode with the enum as the argument type resolves to Object.set_hideFlags (checked on 6000.3.21).
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", root, TypeName(typeof(GameObject)), "set_hideFlags", PersistentListenerMode.Object,
                objectArgumentType: TypeName(typeof(HideFlags)));
            AddSerializedCall(button, "m_OnClick", root, TypeName(typeof(GameObject)), "set_name", PersistentListenerMode.String, "renamed");

            var report = Sanitize();
            Assert.AreEqual(1, report.Disabled, "the name setter stays (at most reported); the hideFlags setter does not");
            Assert.IsTrue(Found(report, "sets hideFlags"), string.Join("\n", report.Lines));
            Assert.AreEqual("", button.onClick.GetPersistentMethodName(0));
            Assert.AreEqual("set_name", button.onClick.GetPersistentMethodName(1));
        }

        [Test]
        public void PersistentCalls_ThatReparent_AreDisabledUnderAnyRule()
        {
            // Listed for graphs (whose re-parenting is reported); no live world's persistent calls use them.
            var button = root.AddComponent<Button>();
            var held = Make("held", root.transform);
            AddSerializedCall(button, "m_OnClick", held.transform, TypeName(typeof(Transform)), nameof(Transform.SetParent),
                PersistentListenerMode.Object, objectArgumentType: TypeName(typeof(Transform)));
            AddSerializedCall(button, "m_OnClick", held.transform, TypeName(typeof(Transform)), "set_parent",
                PersistentListenerMode.Object, objectArgumentType: TypeName(typeof(Transform)));
            AddSerializedCall(button, "m_OnClick", root.transform, TypeName(typeof(Transform)), nameof(Transform.DetachChildren),
                PersistentListenerMode.Void);
            AddSerializedCall(button, "m_OnClick", held, TypeName(typeof(GameObject)), nameof(GameObject.SetActive),
                PersistentListenerMode.Bool, boolArgument: true);

            var report = Sanitize();
            Assert.AreEqual(3, report.Disabled, string.Join("\n", report.Lines));
            Assert.IsTrue(Found(report, "re-parents"), string.Join("\n", report.Lines));
            Assert.AreEqual(nameof(GameObject.SetActive), button.onClick.GetPersistentMethodName(3));
        }

        [Test]
        public void WorldScopeExtensions_AreConsulted_AndOneThatThrowsAnswersFalse()
        {
            var held = Make("held by a peer");
            var part = Make("part", held.transform);
            Func<Transform, bool> holds = t => t.IsChildOf(held.transform);
            Func<Transform, bool> broken = t => throw new InvalidOperationException("broken extension");
            BSUnityEventGuard.AddWorldScopeExtension(broken);
            BSUnityEventGuard.AddWorldScopeExtension(holds);
            BSUnityEventGuard.AddWorldScopeExtension(holds); // once
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("broken extension"));
                Assert.IsTrue(BSUnityEventGuard.IsHeldElsewhere(part.transform));
                LogAssert.Expect(LogType.Exception, new Regex("broken extension"));
                Assert.IsFalse(BSUnityEventGuard.IsHeldElsewhere(root.transform));
            }
            finally
            {
                BSUnityEventGuard.RemoveWorldScopeExtension(broken);
                BSUnityEventGuard.RemoveWorldScopeExtension(holds);
            }
            Assert.IsFalse(BSUnityEventGuard.IsHeldElsewhere(part.transform), "removed");
        }

        [Test]
        public void LegacyAnimationStates_OnAChangedClip_AreAddedAgain_WithTheirPlayback()
        {
            var clip = new AnimationClip { legacy = true, name = "UnityEventGuardTests refresh" };
            var other = new AnimationClip { legacy = true, name = "UnityEventGuardTests other" };
            try
            {
                var animation = root.AddComponent<Animation>();
                animation.AddClip(clip, "walk");
                animation.AddClip(other, "idle");
                animation.clip = clip;
                var walk = animation["walk"];
                walk.wrapMode = WrapMode.Loop;
                walk.speed = 2f;
                walk.layer = 3;
                walk.weight = 0.5f;
                walk.enabled = true;
                var states = 0;
                foreach (AnimationState unused in animation) states++;
                Assume.That(states == 2, "legacy states are enumerable in edit mode");
                // Added under another name, a state holds a copy of the clip (probe Q11): the guard judges and changes
                // the clip the state holds, so that is the one that changed.
                var held = walk.clip;

                Assert.AreEqual(1, BSUnityEventGuard.RefreshLegacy(animation, new[] { held }), "only the state on the changed clip");
                var again = animation["walk"];
                Assert.IsNotNull(again);
                Assert.IsNotNull(again.clip);
                Assert.AreEqual(WrapMode.Loop, again.wrapMode);
                Assert.AreEqual(2f, again.speed);
                Assert.AreEqual(3, again.layer);
                Assert.AreEqual(0.5f, again.weight);
                Assert.IsTrue(again.enabled, "still playing");
                Assert.IsNotNull(animation["idle"]);
                Assert.AreSame(clip, animation.clip, "the default clip is kept");
            }
            finally
            {
                UnityObject.DestroyImmediate(clip);
                UnityObject.DestroyImmediate(other);
            }
        }

        [Test]
        public void SceneLessRoots_LiveHideFlaggedRootsOutsideEveryScene_AreTheWorlds()
        {
            var hidden = EditorUtility.CreateGameObjectWithHideFlags("UnityEventGuardTests scene-less", HideFlags.HideAndDontSave, typeof(MeshFilter));
            try
            {
                Assume.That(!hidden.scene.IsValid(), "a HideAndDontSave object is outside every scene here");
                var found = BSUnityEventGuard.SceneLessRoots(new[] { hidden, root });
                CollectionAssert.AreEqual(new[] { hidden }, found, "the scene's own root is not one");
            }
            finally
            {
                UnityObject.DestroyImmediate(hidden);
            }
        }

        [Test]
        public void ScenePaths_CompareWithoutCaseOrSeparatorDifferences()
        {
            Assert.IsTrue(BSUnityEventGuard.SamePath("Assets/Worlds/My World.unity", @"assets\worlds\my world.UNITY"));
            Assert.IsFalse(BSUnityEventGuard.SamePath("Assets/Worlds/A.unity", "Assets/Worlds/B.unity"));
            Assert.IsFalse(BSUnityEventGuard.SamePath("", ""), "an empty path names no scene");
        }

        [Test]
        public void GraphVariables_LeadToTheAssetsTheyHold()
        {
            // Variables (like machines and macros) clear their Unity-serialized data once loaded: the guard serializes them again.
            var holder = ScriptableObject.CreateInstance<EventHolder>();
            try
            {
                UnityEventTools.AddVoidPersistentListener(holder.fired, StaticHit);
                holder.fired.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
                root.AddComponent<Variables>().declarations.Set("held", holder);

                Assert.AreEqual(1, Sanitize().Disabled);
                holder.fired.Invoke();
                Assert.AreEqual(0, staticHits);
            }
            finally
            {
                UnityObject.DestroyImmediate(holder);
            }
        }

        [Test]
        public void KitPrefab_CopiesInheritTheJudgement()
        {
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", null, TypeName(typeof(UnityEventGuardTests)), nameof(StaticHit), PersistentListenerMode.Void);

            BSUnityEventGuard.SanitizePrefab(root, "kit/test.prefab");
            Assert.IsTrue(BSUnityEventGuard.IsJudgedKitPrefab(root));
            Assert.AreEqual(UnityEventCallState.Off, button.onClick.GetPersistentListenerState(0));
            var copy = UnityObject.Instantiate(root, Make("holder").transform);
            BSUnityEventGuard.SanitizeInstance(copy, "kit/test.prefab");
            var copied = copy.GetComponent<Button>().onClick;
            Assert.AreEqual(UnityEventCallState.Off, copied.GetPersistentListenerState(0));
            copied.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            copied.Invoke();
            Assert.AreEqual(0, staticHits);
        }

        [Test]
        public void ContentNestedTooDeep_FailsClosed()
        {
            var chain = root.AddComponent<DeepChain>();
            chain.head = new Node();
            var node = chain.head;
            for (var i = 0; i < 40; i++)
            {
                node.next = new Node();
                node = node.next;
            }

            // Each refused pass logs one error.
            for (var i = 0; i < 3; i++) LogAssert.Expect(LogType.Error, new Regex(@"^\[UnityEventGuard\] .*FAILED \(serialized data nested deeper"));
            Assert.IsTrue(Sanitize().Failed);
            Assert.Throws<InvalidOperationException>(() => BSUnityEventGuard.SanitizePrefab(root, "kit/deep.prefab"), "a kit item is refused");
            Assert.IsFalse(BSUnityEventGuard.IsJudgedKitPrefab(root));
            var copy = UnityObject.Instantiate(root, Make("holder").transform);
            Assert.Throws<InvalidOperationException>(() => BSUnityEventGuard.SanitizeInstance(copy, "kit/deep.prefab"));
        }

        [Test]
        public void WithoutUnityInternals_EveryEventWithPersistentCallsIsReplaced()
        {
            var lamp = Make("lamp", root.transform);
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", lamp, TypeName(typeof(GameObject)), nameof(GameObject.SetActive),
                PersistentListenerMode.Bool, boolArgument: false);
            BSUnityEventGuard.simulateMissingInternals = true;

            var report = Sanitize();
            Assert.IsFalse(report.Failed);
            Assert.AreEqual(1, report.Disabled);
            Assert.AreEqual(0, button.onClick.GetPersistentEventCount(), "a fresh, empty event: nothing left to resolve");
            button.onClick.Invoke();
            Assert.IsTrue(lamp.activeSelf);
        }

        [Test]
        public void GraphVariableHoldingAUnityEvent_IsScrubbed()
        {
            // Visual Scripting's own JSON (not Unity's serializer) fills variable values, UnityEvents included.
            var held = new UnityEvent();
            UnityEventTools.AddVoidPersistentListener(held, StaticHit);
            held.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            root.AddComponent<Variables>().declarations.Set("evt", held);

            Assert.AreEqual(1, Sanitize().Disabled);
            held.Invoke();
            Assert.AreEqual(0, staticHits);
        }

#if GREENFIELD_PROJECT
        /// <summary>Stands in for a machine WorldActivation held during scene activation.</summary>
        sealed class HeldMachine : WorldActivation.IDeferred
        {
            public bool started;
            public bool dropped;
            public UnityObject on;

            public void ReleaseActivation() => started = true;

            public void DropActivation() => dropped = true;

            public UnityObject context => on;
        }

        void BuildDeepChain()
        {
            var chain = root.AddComponent<DeepChain>();
            chain.head = new Node();
            var node = chain.head;
            for (var i = 0; i < 40; i++)
            {
                node.next = new Node();
                node = node.next;
            }
        }

        [Test]
        public void WorldHold_StartsHeldGraphs_OnceTheWorldPasses()
        {
            var machine = new HeldMachine();
            using (var world = BSUnityEventGuard.SanitizeWhenLoaded("Assets/UnityEventGuardTests.unity", "UnityEventGuardTests"))
            {
                Assert.IsTrue(WorldActivation.IsDeferring, "graphs are held from before the load");
                WorldActivation.Hold(machine);
                world.Judge(scene);
                Assert.IsTrue(world.Complete());
            }
            Assert.IsTrue(machine.started);
            Assert.IsFalse(machine.dropped);
            Assert.IsFalse(WorldActivation.IsDeferring);
        }

        [Test]
        public void WorldHold_DropsHeldGraphs_WhenTheWorldFails()
        {
            BuildDeepChain();
            var lamp = Make("lamp", root.transform);
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", lamp, TypeName(typeof(GameObject)), nameof(GameObject.SetActive),
                PersistentListenerMode.Bool, boolArgument: false);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[UnityEventGuard\] world .*FAILED \(serialized data nested deeper"));
            LogAssert.Expect(LogType.Error, new Regex(@"^\[UnityEventGuard\] world .* refused \(serialized data nested deeper"));
            var machine = new HeldMachine();
            using (var world = BSUnityEventGuard.SanitizeWhenLoaded("Assets/UnityEventGuardTests.unity", "UnityEventGuardTests"))
            {
                WorldActivation.Hold(machine);
                world.Judge(scene);
                Assert.IsFalse(world.Complete());
            }
            Assert.IsTrue(machine.dropped, "held graphs never start");
            Assert.IsFalse(machine.started);
            Assert.IsFalse(root.activeSelf, "deactivated, so no Start runs before the unload");
            Assert.AreEqual(0, button.onClick.GetPersistentEventCount(), "emptied first: OnDisable/OnDestroy senders have nothing to call");
            Assert.IsTrue(BSUnityEventGuard.IsRefusedScene(scene), "never world content again");
            Assert.IsFalse(WorldActivation.IsDeferring);
        }

        [Test]
        public void WorldHold_DropsHeldGraphs_WhenTheGuardNeverSawTheWorld()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"refused \(the guard never saw it load\)"));
            var machine = new HeldMachine();
            using (var world = BSUnityEventGuard.SanitizeWhenLoaded("Assets/UnityEventGuardTests.unity", "UnityEventGuardTests"))
            {
                WorldActivation.Hold(machine);
                Assert.IsFalse(world.Complete());
            }
            Assert.IsTrue(machine.dropped);
            Assert.IsFalse(machine.started);
        }
#endif

        [Test]
        public void FailClosed_LoadsVoid_OnlyWhileTheRefusedWorldIsTheLatestLoad()
        {
            Assert.IsTrue(BSUnityEventGuard.ShouldLoadVoid(3, 3));
            Assert.IsFalse(BSUnityEventGuard.ShouldLoadVoid(3, 4), "a Void queued behind a newer world's Single load would replace that world");
        }

        [Test]
        public void UnexpectedBundleScene_IsEmptiedDeactivatedAndRefused()
        {
            var lamp = Make("lamp", root.transform);
            var button = root.AddComponent<Button>();
            AddSerializedCall(button, "m_OnClick", lamp, TypeName(typeof(GameObject)), nameof(GameObject.SetActive),
                PersistentListenerMode.Bool, boolArgument: false);
            LogAssert.Expect(LogType.Error, new Regex(@"loaded outside a world load .*refusing it"));

            BSUnityEventGuard.RefuseUnexpected(scene, LoadSceneMode.Additive, null);
            Assert.AreEqual(0, button.onClick.GetPersistentEventCount());
            Assert.IsFalse(root.activeSelf);
            Assert.IsTrue(BSUnityEventGuard.IsRefusedScene(scene));
        }

        [Test]
        public void Cinemachine_IsContent()
        {
            var type = Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
            if (type == null) Assert.Ignore("Cinemachine is not in this project");
            Assert.IsFalse(BSUnityEventGuard.IsHostComponentType(type), "Assets/link.xml preserves it for world bundles");
        }

#if GREENFIELD_PROJECT
        [Test]
        public void WorldHolds_AreSettledPerWorld()
        {
            // An older world finishing must not close (or start) a newer world's hold.
            var otherScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var other = new GameObject("other world");
                SceneManager.MoveGameObjectToScene(other, otherScene);
                var older = new HeldMachine { on = root };
                var newer = new HeldMachine { on = other };
                var olderHold = WorldActivation.Open(Array.Empty<int>(), 60);
                var newerHold = WorldActivation.Open(Array.Empty<int>(), 60);
                WorldActivation.Hold(older);
                WorldActivation.Hold(newer);

                WorldActivation.Close(olderHold, scene, null, true);
                Assert.IsTrue(older.started);
                Assert.IsFalse(newer.started || newer.dropped, "the newer world is still held");
                Assert.IsTrue(WorldActivation.IsDeferring);

                WorldActivation.Close(newerHold, otherScene, null, false);
                Assert.IsTrue(newer.dropped);
                Assert.IsFalse(WorldActivation.IsDeferring);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(otherScene);
            }
        }

        [Test]
        public void ClientHold_OnlyWarnsAfterItsDeadline()
        {
            var machine = new HeldMachine();
            var hold = WorldActivation.Open(Array.Empty<int>(), 0);
            WorldActivation.Hold(machine);
            System.Threading.Thread.Sleep(5);
            LogAssert.Expect(LogType.Warning, new Regex("taking longer than expected"));
            Assert.IsTrue(WorldActivation.IsDeferring, "a slow world keeps its machines held");
            Assert.IsFalse(machine.dropped);
            WorldActivation.Close(hold, default, null, true);
            Assert.IsTrue(machine.started);
        }

        [Test]
        public void HostComponentRemoval_ComesAfterJudging()
        {
            var name = typeof(DestroySender).FullName;
            BSUnityEventGuard.deniedHostTypes.Add(name);
            BSUnityEventGuard.ForgetDeniedTypes();
            try
            {
                var sender = root.AddComponent<DestroySender>();
                UnityEventTools.AddVoidPersistentListener(sender.destroyed, StaticHit);
                sender.destroyed.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);

                var report = Sanitize();
                Assert.AreEqual(1, report.Removed, "a denied host service goes even while host removal is report-only");
                Assert.IsNull(root.GetComponent<DestroySender>());
                Assert.AreEqual(0, staticHits, "its OnDestroy fired only the already-judged (disabled) call");
            }
            finally
            {
                BSUnityEventGuard.deniedHostTypes.Remove(name);
                BSUnityEventGuard.ForgetDeniedTypes();
            }
        }

        [Test]
        public void DeniedHostServices_MatchTheirTypeAndBaseTypes()
        {
            Assert.IsTrue(BSUnityEventGuard.IsDeniedHostComponent(typeof(UnityEngine.InputSystem.PlayerInput)));
            Assert.IsTrue(BSUnityEventGuard.IsDeniedHostComponent(typeof(UnityEngine.InputSystem.OnScreen.OnScreenButton)), "via OnScreenControl");
            Assert.IsFalse(BSUnityEventGuard.IsDeniedHostComponent(typeof(UnityEngine.InputSystem.UI.TrackedDeviceRaycaster)));
            Assert.IsFalse(BSUnityEventGuard.IsDeniedHostComponent(typeof(Button)));
        }
#endif

        [Test]
        public void KitAssets_AreJudgedBeforeAPageGraphGetsThem()
        {
            var holder = ScriptableObject.CreateInstance<EventHolder>();
            var plain = new AnimationClip { name = "plain" };
            var withEvents = new AnimationClip { name = "with events" };
            var mesh = new Mesh();
            try
            {
                UnityEventTools.AddVoidPersistentListener(holder.fired, StaticHit);
                holder.fired.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
                AnimationUtility.SetAnimationEvents(withEvents, new[] { new AnimationEvent { functionName = "Anything" } });

                Assert.IsTrue(BSUnityEventGuard.JudgeKitAsset(holder, "kit/holder.asset"), "a ScriptableObject is walked");
                holder.fired.Invoke();
                Assert.AreEqual(0, staticHits, "and its calls judged before it is handed out");
                Assert.IsTrue(BSUnityEventGuard.JudgeKitAsset(plain, "kit/plain.anim"));
                Assert.IsFalse(BSUnityEventGuard.JudgeKitAsset(withEvents, "kit/events.anim"), "AnimationEvents with unknown receivers cannot be judged");
                Assert.IsTrue(BSUnityEventGuard.JudgeKitAsset(mesh, "kit/mesh.asset"), "plain data");
                var footsteps = new AnimationClip { name = "footsteps" };
                var lighting = new LightingSettings();
                try
                {
                    AnimationUtility.SetAnimationEvents(footsteps, new[] { new AnimationEvent { functionName = "TriggerAnimationEvent", stringParameter = "step" } });
                    Assert.IsTrue(BSUnityEventGuard.JudgeKitAsset(footsteps, "kit/footsteps.anim"), "events only for graph machines");
                    Assert.IsFalse(BSUnityEventGuard.JudgeKitAsset(lighting, "kit/lighting.asset"), "a kind that is not listed as plain data");
                }
                finally
                {
                    UnityObject.DestroyImmediate(footsteps);
                    UnityObject.DestroyImmediate(lighting);
                }
                Assert.IsTrue(BSUnityEventGuard.JudgeKitAsset(root, "kit/root.prefab"), "a prefab passes SanitizePrefab");
                Assert.IsFalse(BSUnityEventGuard.JudgeKitAsset(null, "kit/missing"));
            }
            finally
            {
                UnityObject.DestroyImmediate(holder);
                UnityObject.DestroyImmediate(plain);
                UnityObject.DestroyImmediate(withEvents);
                UnityObject.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TestAssemblies_AreToldApartByTheirReferences()
        {
            Assert.IsFalse(BSUnityEventGuard.IsPlayerAssembly(typeof(UnityEventGuardTests).Assembly), "references NUnit");
            Assert.IsTrue(BSUnityEventGuard.IsPlayerAssembly(typeof(Button).Assembly));
            Assert.IsTrue(BSUnityEventGuard.IsPlayerAssembly(typeof(BSObjectId).Assembly));
        }

#if GREENFIELD_PROJECT
        [Test]
        public void SceneVerdicts_ReleaseRefuseAndSettle()
        {
            var otherScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var other = new GameObject("refused world");
                SceneManager.MoveGameObjectToScene(other, otherScene);
                var released = new HeldMachine { on = root };
                var dropped = new HeldMachine { on = other };
                WorldActivation.Hold(released);
                WorldActivation.Hold(dropped);

                Assert.AreEqual(1, WorldActivation.Drop(otherScene, null));
                Assert.IsTrue(dropped.dropped);
                Assert.IsTrue(WorldActivation.ShouldDefer(other), "a refused scene's machines never start, later ones included");

                Assert.AreEqual(0, WorldActivation.SettleUnjudged(_ => false), "a scene without a verdict waits");
                Assert.AreEqual(1, WorldActivation.SettleUnjudged(_ => true), "unless it is not a bundle scene");
                Assert.IsTrue(released.started);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(otherScene);
            }
        }

        [Test]
        public void DeniedComponent_GoesWithWhatRequiresIt()
        {
            var name = typeof(DeniedService).FullName;
            BSUnityEventGuard.deniedHostTypes.Add(name);
            BSUnityEventGuard.ForgetDeniedTypes();
            try
            {
                // A nested test type has no MonoScript, so Unity does not add what its [RequireComponent] names; the guard
                // reads the attribute itself, so the dependent still goes first.
                root.AddComponent<DeniedService>();
                root.AddComponent<NeedsDeniedService>();
                Assert.IsNotNull(root.GetComponent<DeniedService>(), "setup");
                var report = Sanitize();
                Assert.IsFalse(report.Failed, "the dependent is removed first, so the removal succeeds");
                Assert.AreEqual(2, report.Removed);
                Assert.IsNull(root.GetComponent<DeniedService>());
                Assert.IsNull(root.GetComponent<NeedsDeniedService>());
            }
            finally
            {
                BSUnityEventGuard.deniedHostTypes.Remove(name);
                BSUnityEventGuard.ForgetDeniedTypes();
            }
        }

        /// <summary>
        /// The MonoBehaviours in the player that have each AnimationEvent sink name as non-public or static (the guard
        /// exempts the sinks from that rule). A new partner trips this: decide whether the sink stays.
        /// </summary>
        [Test]
        public void AnimationEventSinkPartners_ArePinned()
        {
            var expected = new System.Collections.Generic.Dictionary<string, string[]>
            {
                { "TriggerEvent", new[] { "Unity.VisualScripting.EventMachine`2" } },
                { "SetText", new[] { "Greenfield.Menu.Explore.ExploreSectionView", "Greenfield.Menu.FeedSection", "Greenfield.Menu.ProfileSection",
                    "Greenfield.Recording.UI.MediaPreview", "Greenfield.Recording.UI.RecordingsGallerySection", "TMPro.TMP_InputField",
                    "TMPro.TMP_Text", "UnityEngine.UI.InputField" } },
                { "Play", new[] { "BS.BSAudioSource", "Greenfield.Emoji.EmojiReactionService", "Greenfield.HandCamera.CameraSoundEffects",
                    "SideQuest.FlexaBody.BirdFlightSound", "Whisper.Utils.PlayAudioAndDestroy" } },
                { "SetDirty", new[] { "UnityEngine.UI.AspectRatioFitter", "UnityEngine.UI.ContentSizeFitter", "UnityEngine.UI.LayoutElement",
                    "UnityEngine.UI.LayoutGroup", "UnityEngine.UI.ScrollRect" } },
                { "AddCullingLayer", new[] { "BS.BSMirror" } },
                { "SetCullingLayer", new[] { "BS.BSMirror" } },
                { "LoadQuestHome", new[] { "BS.BSQuestHome" } },
            };
            CollectionAssert.AreEquivalent(expected.Keys, BSUnityEventGuard.animationEventSinks, "every sink is pinned");
            const System.Reflection.BindingFlags declared = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
            var behaviours = new System.Collections.Generic.List<Type>();
            for (var type = typeof(MonoBehaviour).BaseType; type != null; type = type.BaseType) behaviours.Add(type);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!BSUnityEventGuard.IsPlayerAssembly(assembly)) continue;
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                foreach (var type in types)
                {
                    if (type != null && typeof(MonoBehaviour).IsAssignableFrom(type)) behaviours.Add(type);
                }
            }
            foreach (var pair in expected)
            {
                var partners = new System.Collections.Generic.SortedSet<string>();
                foreach (var type in behaviours)
                {
                    foreach (var method in type.GetMethods(declared))
                    {
                        if (method.Name == pair.Key && (method.IsStatic || !method.IsPublic)) partners.Add(type.FullName);
                    }
                }
                CollectionAssert.AreEquivalent(pair.Value, partners, "partners of " + pair.Key);
            }
        }
#endif

        [Test]
        public void ClipSweep_JudgesEventsWithoutKnowingReceivers()
        {
            var clip = new AnimationClip { name = "swept" };
            try
            {
                AnimationUtility.SetAnimationEvents(clip, new[]
                {
                    new AnimationEvent { functionName = "Press" },          // private on UGUI's Button
                    new AnimationEvent { functionName = "SetDirty" },       // a content sink: exempt only on a known receiver
                    new AnimationEvent { functionName = "SomethingPublic" }, // can only reach a public method
                    new AnimationEvent { functionName = nameof(GameObject.SendMessage) },
                    new AnimationEvent { functionName = "set_hideFlags", intParameter = 61 },
                });
                var report = new BSUnityEventGuard.Report("sweep test");
                BSUnityEventGuard.SweepNewClips(report, new[] { clip });
                Assert.AreEqual(4, report.AnimationEventsDropped);
                CollectionAssert.AreEqual(new[] { "SomethingPublic" }, Array.ConvertAll(clip.events, e => e.functionName));

                var again = new BSUnityEventGuard.Report("sweep test");
                BSUnityEventGuard.SweepNewClips(again, new[] { clip });
                Assert.AreEqual(0, again.AnimationEventsDropped, "once per clip");
            }
            finally
            {
                UnityObject.DestroyImmediate(clip);
            }
        }

#if GREENFIELD_PROJECT
        [Test]
        public void WorldDontDestroyOnLoad_IsRemoved()
        {
            root.AddComponent<DontDestroyOnLoad>();
            var report = Sanitize();
            Assert.IsFalse(report.Failed);
            Assert.IsNull(root.GetComponent<DontDestroyOnLoad>(), "a world has no business persisting past itself");
        }

        [Test]
        public void NewPersistentRoots_ClientSingletonsAreNotWorldContent()
        {
            var tween = Make("DigitalRubyTween");
            tween.AddComponent<DigitalRuby.Tween.TweenFactory>();
            var empty = Make("jiggle dummy");
            var content = Make("world root");
            Make("child", content.transform).AddComponent<MeshFilter>();
            var mixed = Make("tween root with world content");
            mixed.AddComponent<DigitalRuby.Tween.TweenFactory>();
            mixed.AddComponent<MeshFilter>();
            var runner = Make("AsyncCoroutineRunner");
            runner.AddComponent<UnityAsyncAwaitUtil.AsyncCoroutineRunner>();

            Assert.IsFalse(BSUnityEventGuard.IsWorldEscapee(tween));
            Assert.IsFalse(BSUnityEventGuard.IsWorldEscapee(empty));
            Assert.IsTrue(BSUnityEventGuard.IsWorldEscapee(content), "a component in a child is enough");
            Assert.IsTrue(BSUnityEventGuard.IsWorldEscapee(mixed), "a singleton type does not hide world content");
            Assert.IsTrue(BSUnityEventGuard.IsWorldEscapee(runner), "every AsyncCoroutineRunner persists itself; this one is not the client's");

            var registry = Make("BSAssetRegistry");
            var component = registry.AddComponent<BSAssetRegistry>();
            var instance = typeof(BSAssetRegistry).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var previous = instance.GetValue(null);
            try
            {
                Assert.IsTrue(BSUnityEventGuard.IsWorldEscapee(registry), "a registry the client does not use");
                instance.SetValue(null, component);
                Assert.IsFalse(BSUnityEventGuard.IsWorldEscapee(registry), "the client's registry");
            }
            finally
            {
                instance.SetValue(null, previous);
            }
        }

        [Test]
        public void ClientSingletonInstanceFields_Exist()
        {
            foreach (var pair in BSUnityEventGuard.singletonInstanceFields)
            {
                Type type = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(pair.Key);
                    if (type != null) break;
                }
                Assert.IsNotNull(type, pair.Key);
                var field = type.GetField(pair.Value, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.IsNotNull(field, pair.Key + "." + pair.Value);
                Assert.IsTrue(field.FieldType.IsAssignableFrom(type), pair.Key + "." + pair.Value + " holds the instance");
            }
        }
#endif

        [Test]
        public void AnimationEventSinks_StaticOnSomeBehaviour_AreNotExempt()
        {
            root.AddComponent<Probe>();
            var clip = new AnimationClip { legacy = true, name = "UnityEventGuardTests static sink" };
            BSUnityEventGuard.animationEventSinks.Add(nameof(Probe.AllSelectablesNoAlloc));
            try
            {
                AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { functionName = nameof(Probe.AllSelectablesNoAlloc) } });
                root.AddComponent<Animation>().clip = clip;
                var report = Sanitize();
                Assert.AreEqual(1, report.AnimationEventsDropped, "UGUI's Selectable declares it static");
                Assert.AreEqual(0, clip.events.Length);
            }
            finally
            {
                BSUnityEventGuard.animationEventSinks.Remove(nameof(Probe.AllSelectablesNoAlloc));
                UnityObject.DestroyImmediate(clip);
            }
        }

#if GREENFIELD_PROJECT
        [Test]
        public void ScenesOutsideTheBuild_AreNotTheClientsWhileAWorldIsPending()
        {
            Assume.That(scene.buildIndex < 0, "a preview scene is outside the build");
            Assert.IsTrue(BSUnityEventGuard.MayBeClientScene(scene, false), "edit mode: no world loads");
            var world = BSUnityEventGuard.SanitizeWhenLoaded("Assets/UnityEventGuardTests/never.unity", "UnityEventGuardTests never");
            try
            {
                Assert.IsFalse(BSUnityEventGuard.MayBeClientScene(scene, true), "a world is pending: a scene that matched nothing is refused");
            }
            finally
            {
                LogAssert.Expect(LogType.Error, new Regex(@"refused \(the guard never saw it load\)"));
                world.Complete();
            }
        }

        [Test]
        public void Parenting_IsKnownForTheMembersThatMoveObjects()
        {
            Assert.AreEqual(WorldActivation.Parenting.Target,
                WorldActivation.ParentingOf(new Member(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) })));
            Assert.AreEqual(WorldActivation.Parenting.Target, WorldActivation.ParentingOf(new Member(typeof(RectTransform), nameof(Transform.parent))));
            Assert.AreEqual(WorldActivation.Parenting.Children,
                WorldActivation.ParentingOf(new Member(typeof(Transform), nameof(Transform.DetachChildren), Type.EmptyTypes)));
            Assert.AreEqual(WorldActivation.Parenting.Result,
                WorldActivation.ParentingOf(new Member(typeof(GameObject), nameof(UnityObject.Instantiate), new[] { typeof(UnityObject), typeof(Scene) })));
            Assert.AreEqual(WorldActivation.Parenting.Result,
                WorldActivation.ParentingOf(new Member(typeof(UnityObject), nameof(UnityObject.Instantiate), new[] { typeof(UnityObject) })));
            Assert.AreEqual(WorldActivation.Parenting.None,
                WorldActivation.ParentingOf(new Member(typeof(Transform), nameof(Transform.SetAsFirstSibling), Type.EmptyTypes)));
            Assert.AreEqual(WorldActivation.Parenting.None, WorldActivation.ParentingOf(new Member(typeof(Transform), nameof(Transform.position))));
        }

        [Test]
        public void Lending_ReportsOnlyWorldObjectsAWorldGraphPutsOutside()
        {
            // Stand-in scope: the world is everything under the fixture's "world root"; the client is "client rig".
            var lent = new List<GameObject>();
            WorldActivation.SetLendingHooks(t => t.root == root.transform, lent.Add);
            try
            {
                var rig = Make("client rig");
                var clientGraph = Make("client graph", rig.transform);
                var worldGraph = Make("world graph", root.transform);
                var hat = Make("hat", root.transform);
                var badge = Make("badge", rig.transform);
                var crate = Make("crate", root.transform);
                Make("plank", crate.transform);

                // A world graph puts a world object on the player: reported.
                var watched = WorldActivation.BeforeParenting(WorldActivation.Parenting.Target, worldGraph, hat.transform);
                hat.transform.SetParent(rig.transform);
                WorldActivation.AfterParenting(watched, WorldActivation.Parenting.Target, null);
                CollectionAssert.AreEqual(new[] { hat }, lent);

                // The client's own graph, or a client object a world graph moves about the client: never reported.
                Assert.IsNull(WorldActivation.BeforeParenting(WorldActivation.Parenting.Target, clientGraph, badge.transform));
                Assert.IsNull(WorldActivation.BeforeParenting(WorldActivation.Parenting.Target, worldGraph, badge.transform));

                // A world graph instantiates under the player: the copy is reported.
                watched = WorldActivation.BeforeParenting(WorldActivation.Parenting.Result, worldGraph, null);
                var copy = UnityObject.Instantiate(crate, rig.transform);
                WorldActivation.AfterParenting(watched, WorldActivation.Parenting.Result, copy);
                Assert.AreEqual(copy, lent[1]);

                // DetachChildren makes world children roots of their own: each is reported.
                watched = WorldActivation.BeforeParenting(WorldActivation.Parenting.Children, worldGraph, crate.transform);
                var plank = crate.transform.GetChild(0).gameObject;
                crate.transform.DetachChildren();
                WorldActivation.AfterParenting(watched, WorldActivation.Parenting.Children, null);
                Assert.AreEqual(plank, lent[2]);
                Assert.AreEqual(3, lent.Count);
            }
            finally
            {
                WorldActivation.SetLendingHooks(BSUnityEventGuard.IsWorldScoped, BSUnityEventGuard.LentOut);
            }
        }

        [Test]
        public void HostTypeArguments_CachedPerPort_JudgeLikeTheReflectingForm()
        {
            var cases = new (object value, Type parameter)[]
            {
                (new[] { typeof(Rigidbody), typeof(System.IO.File) }, typeof(Type[])),
                (new List<object> { 1, typeof(System.IO.File) }, typeof(object)),
                (new List<Vector3> { Vector3.zero }, typeof(List<Vector3>)),
                (typeof(System.IO.File), typeof(Type)),
                (new[] { typeof(Rigidbody) }, typeof(Type[])),
            };
            foreach (var (value, parameter) in cases)
            {
                Assert.AreEqual(BanterStubsAllowed.HostTypeArgument(value, parameter),
                    BanterStubsAllowed.HostTypeArgument(value, BanterStubsAllowed.MayHoldTypes(parameter)), parameter.Name);
            }
        }

        [Test]
        public void DisabledForTests_HoldsNothing_AndLetsAnUnjudgedWorldThrough()
        {
            WorldActivation.SetInertProbe(() => BSUnityEventGuard.DisabledForTests);
            BSUnityEventGuard.DisabledForTests = true;
            try
            {
                using (var world = BSUnityEventGuard.SanitizeWhenLoaded("Assets/UnityEventGuardTests/off.unity", "UnityEventGuardTests off"))
                {
                    Assert.IsFalse(WorldActivation.ShouldDefer(root), "no holds while the guard is off");
                    Assert.IsTrue(world.Complete(), "never judged, and not refused for it");
                }
            }
            finally
            {
                BSUnityEventGuard.DisabledForTests = false;
            }
        }
#endif

        [Test]
        public void LentOutObjects_GoWithTheirWorld()
        {
            var held = Make("held item");
            var brim = Make("brim", held.transform);
            BSUnityEventGuard.LentOut(held);
            BSUnityEventGuard.LentOut(held); // once
            Assert.AreEqual(1, BSUnityEventGuard.ReclaimLentOut(BSUnityEventGuard.CurrentWorldScene));
            Assert.IsTrue(held == null && brim == null, "destroyed, children included");
            Assert.AreEqual(0, BSUnityEventGuard.ReclaimLentOut(BSUnityEventGuard.CurrentWorldScene));
        }

        [Test]
        public void AnimationEvents_ThatReachNonPublicDispatchHiddenOrNoReceiver_AreDropped()
        {
            root.AddComponent<Probe>();
            var clip = new AnimationClip { legacy = true, name = "UnityEventGuardTests" };
            try
            {
                AnimationUtility.SetAnimationEvents(clip, new[]
                {
                    new AnimationEvent { functionName = nameof(Probe.PublicHit) },
                    new AnimationEvent { functionName = "PrivateHit" },
                    new AnimationEvent { functionName = nameof(GameObject.SendMessage), stringParameter = "PrivateHit" },
                    new AnimationEvent { functionName = "NoSuchMethod" },
                    // Public on this receiver, but private on UGUI's Button: a Button added later could receive it.
                    new AnimationEvent { functionName = nameof(Probe.Press) },
                    // Protected on AspectRatioFitter too, but a content sink that is public here and static nowhere.
                    new AnimationEvent { functionName = nameof(Probe.SetDirty) },
                    // Object.hideFlags' setter: refused under any rule.
                    new AnimationEvent { functionName = "set_hideFlags", intParameter = 61 },
                });
                root.AddComponent<Animation>().clip = clip;

                var report = Sanitize();
                Assert.AreEqual(5, report.AnimationEventsDropped);
                var kept = clip.events;
                Assert.AreEqual(2, kept.Length);
                Assert.AreEqual(nameof(Probe.PublicHit), kept[0].functionName);
                Assert.AreEqual(nameof(Probe.SetDirty), kept[1].functionName);
            }
            finally
            {
                UnityObject.DestroyImmediate(clip);
            }
        }
    }
}
