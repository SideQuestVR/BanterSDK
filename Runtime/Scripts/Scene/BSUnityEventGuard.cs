using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.LowLevel;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
#if GREENFIELD_PROJECT
using Unity.VisualScripting;
#endif
using UnityObject = UnityEngine.Object;

namespace BS
{
    /// <summary>
    /// Neutralises what bundle content (world scenes and kit prefabs) could use to run code outside the world:
    /// persistent UnityEvent calls, AnimationEvents, serialized by-name dispatchers, and host-app components.
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> A persistent call is serialized data: a target object, a type name, a method name and maybe an
    /// argument. When the event fires, Unity resolves the method with <see cref="UnityEventBase.GetValidMethodInfo(Type,
    /// string, Type[])"/>, which searches public AND non-public, instance AND static members up the type chain, and
    /// whenever the target is null (never set, or destroyed later) it takes the type from the name string. So a Button,
    /// Toggle, EventTrigger, SignalReceiver or SDK component in a bundle could call System.IO.File.Delete(string) or
    /// any internal SDK method. Worlds ship no scripts, but their scenes can name any MonoBehaviour class in the player.</para>
    /// <para><b>Policy.</b> Always disabled: a call with no target, a target outside the world, an asset target the
    /// content does not itself reach (built-in materials, meshes and fonts are shared with the app), a method that
    /// resolves to a static or non-public member, a by-name dispatcher (SendMessage, BroadcastMessage, Invoke,
    /// StartCoroutine...), the hideFlags setter (it hides an object or keeps it past its scene), Transform re-parenting
    /// (SetParent, parent, DetachChildren: only graphs' is tracked), or one Unity cannot resolve. The inspector only binds public members of the target's own
    /// type; the public statics it offers (AudioListener.volume, Canvas.ForceUpdateCanvases, a few SDK statics) act on
    /// the whole app. A public instance member missing from the Visual Scripting allow-list (<c>BanterStubsAllowed</c>)
    /// is only reported, once per world, until <c>EnforceStrictForBundles</c> flips. A disabled call is switched Off and
    /// its method and type names cleared, so nothing can switch it back on (graphs may call SetPersistentListenerState);
    /// a kept call loses its type name, so a destroyed target cannot make Unity resolve another type's static.
    /// Host-app components that open the microphone, network, capture or input paths are removed now
    /// (<see cref="deniedHostTypes"/>); other host components are reported ("would remove") until
    /// <see cref="EnforceHostComponentRemoval"/> flips. Removal comes after judging, so their OnDisable/OnDestroy can only
    /// fire judged calls.</para>
    /// <para><b>Timing.</b> A world scene is judged from the first <see cref="SceneManager.sceneLoaded"/> handler (after
    /// Awake/OnEnable, before any Start or other handler). Each world load holds its own graphs (WorldActivation) from
    /// just before the load until it has been judged, including those on roots its Awake moved to DontDestroyOnLoad; a
    /// newer load's hold is never closed by an older world. A world that fails, or that the guard never saw load, has its
    /// UnityEvents emptied, is deactivated, its DontDestroyOnLoad escapees destroyed, its graphs dropped, and it is
    /// unloaded to Void unless a newer world load is already replacing it; meanwhile it is not world content
    /// (<see cref="IsRefusedScene"/>). A bundle scene nobody asked for (a world loading its own second scene) is refused
    /// the same way. A kit prefab is judged as its asset loads, before any copy exists, and the copy again after
    /// Instantiate; one that fails is refused. Prefabs, ScriptableObjects and clips the content points at (event
    /// arguments, serialized fields, Visual Scripting graphs and variables, Timeline assets) are judged too. Calls made
    /// from Awake/OnEnable during scene activation still run before the guard: Unity has no hook between
    /// deserialization and Awake.</para>
    /// <para><b>AnimationEvents</b> are dispatched by function name to every MonoBehaviour on the animated object,
    /// non-public methods included. An event is dropped when its name is non-public or static on any MonoBehaviour in
    /// the player (a component added later could receive it), unless it is one of the content sinks in
    /// <see cref="animationEventSinks"/>, public on a behaviour actually there and static on none; when no behaviour on
    /// the object has it; when it names the hideFlags setter; or under the rules above for the behaviours that do. A
    /// clip whose receivers are unknown (the sweep) keeps only names that are neither hidden nor sinks.</para>
    /// <para><b>Hiding and persisting.</b> Every world GameObject and component judged has its HideInHierarchy and
    /// DontSave bits cleared, and roots Scene.GetRootGameObjects might leave out are found through every loaded
    /// GameObject. A passed world's DontDestroyOnLoad escapees are moved back into its scene, so what they hold or let
    /// go of later unloads with it. A scene outside the build that matches no pending world is refused while a world
    /// load is pending or once a scene bundle has been seen (Greenfield loads no other non-build scene), and anything
    /// still in a world's scene after it unloaded is destroyed. A world object a world graph parents outside the world
    /// (a player's rig) is allowed there, recorded through WorldActivation's lending hooks, and destroyed when the world
    /// unloads.</para>
    /// </remarks>
    public static class BSUnityEventGuard
    {
        internal enum Reason
        {
            NoTarget, OutsideWorld, AssetTarget, Static, NonPublic, HiddenName, ByNameDispatch, Unresolved, NoReceiver, NotOnAllowList,
            HideFlagsSetter, Reparents,
        }

        /// <summary>UnityEngine.Object.hideFlags' setter: refused to persistent calls and AnimationEvents under any rule.</summary>
        const string HideFlagsSetterName = "set_hideFlags";

        /// <summary>The bits that hide an object from the hierarchy or keep it (or its asset) past its scene and unloads.</summary>
        const HideFlags HidingOrPersisting =
            HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.DontUnloadUnusedAsset;

        /// <summary>Nesting of serialized classes judged; deeper data fails the content closed. Lists do not count.</summary>
        const int MaxDepth = 32;
        const int MaxLogLines = 60;
        const BindingFlags DeclaredMembers =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>Members of GameObject/Component/MonoBehaviour that call a method named by a string.</summary>
        static readonly HashSet<string> byNameDispatch = new HashSet<string>(StringComparer.Ordinal)
        {
            "SendMessage", "SendMessageUpwards", "BroadcastMessage", "Invoke", "InvokeRepeating", "StartCoroutine",
            "StartCoroutine_Auto",
        };

        /// <summary>Serializable classes that SendMessage a method named by one of their own string fields: that field.</summary>
        static readonly Dictionary<string, string> nameDispatchFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // I2 Localize.LocalizeCallBack: Target.gameObject.SendMessage(MethodName), from Awake/OnEnable and on language change.
            { "I2.Loc.EventCallback", "MethodName" },
        };

        /// <summary>
        /// Host components removed from bundle content now, whatever <see cref="EnforceHostComponentRemoval"/> says: they
        /// run host services, open the microphone, network or capture paths, or drive input. Matched on the component's
        /// type and its base types. Internal so tests can add a probe type.
        /// </summary>
        internal static readonly HashSet<string> deniedHostTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            // MCP
            "SideQuest.GreenfieldMcp.GreenfieldMcpServer", "SideQuest.GreenfieldMcp.McpMainThread",
            // microphone
            "uLipSync.uLipSyncMicrophone", "Whisper.Utils.MicrophoneRecord", "Whisper.WhisperManager",
            "SideQuest.LipSync.AudioSourceLipSyncFeed", "SideQuest.LipSync.LipSyncSystem",
            // input (OnScreenControl is OnScreenButton's and OnScreenStick's base)
            "UnityEngine.InputSystem.PlayerInput", "UnityEngine.InputSystem.PlayerInputManager",
            "UnityEngine.InputSystem.UI.VirtualMouseInput", "UnityEngine.InputSystem.OnScreen.OnScreenControl",
            "UnityEngine.InputSystem.EnhancedTouch.TouchSimulation",
            // capture and networking
            "SideQuest.VirtualWebcam.VirtualWebcamSource", "SideQuest.Ora.WebRTC.OraWebRTCPeer", "SideQuest.Ora.WebRTC.OraVideoSender",
            // others
            "KS.UnityToolbag.Dispatcher", "UnityThread", "Oculus.Platform.CallbackRunner",
            "Greenfield.App.PlatformIdentity.SteamManager", "GLTFast.DontDestroyOnLoad", "Unity.XR.CoreUtils.XROrigin",
            // A world has no business persisting past itself. These move their own GameObject to DontDestroyOnLoad on
            // Awake, which for an inactive object or a kit prefab runs after the verdict; the VS gate refuses them as Type values.
            "BS.DontDestroyOnLoad", "UnityAsyncAwaitUtil.AsyncCoroutineRunner", "BS.BSAssetRegistry",
            "BS.VisualScripting.UI.Helpers.CoroutineRunner",
        };

        /// <summary>Assemblies that are host machinery through and through (capture, networking, services): all removed.</summary>
        static readonly HashSet<string> deniedHostAssemblies = new HashSet<string>(StringComparer.Ordinal)
        {
            "SideQuest.GreenfieldMcp", "Greenfield.Mcp", "com.whisper.unity", "SideQuest.LipSync", "Greenfield.LipSync",
            "SideQuest.Recording", "SideQuest.Recording.Banter", "Greenfield.Recording", "SideQuest.HandCamera",
            "Greenfield.HandCamera", "SideQuest.VirtualWebcam", "PacketParty", "SocketIOUnityAssembly", "Oculus.Platform",
            "com.rlabrecque.steamworks.net", "KS.Diagnostics.Process",
            // Data binding: ContextHolder.Awake creates any type named by a serialized string, and Command invokes methods
            // by name. The host's own setters/synchronizers build on it. Their Awake runs before the guard; removal stops
            // everything after.
            "Slash.Unity.DataBind", "Greenfield.Databinding",
        };

        /// <summary>
        /// AnimationEvent names content components expose on purpose that some MonoBehaviour elsewhere in the player also
        /// has as non-public or static: VisualScriptingEvent.TriggerEvent, TMP SetText, SplineAnimate.Play,
        /// SplineInstantiate.SetDirty, the portal renderer's culling layers, QuestHomeLoader. Exempt from the app-wide rule
        /// only while a behaviour actually on the object has it public. Their partners are pinned by a test
        /// (UnityEventGuardTests): content partners are world-scoped, and host partners (static SetText/Play on menu,
        /// recording and FlexaBody types) are components world content cannot add. Clear is not one: its partners include
        /// app-wide statics (TweenFactory.Clear, Get.Clear), and whether an AnimationEvent can reach a static is unproven.
        /// Unity message, serialization, SDK plumbing and debug-UI names stay dropped.
        /// </summary>
        internal static readonly HashSet<string> animationEventSinks = new HashSet<string>(StringComparer.Ordinal)
        {
            "TriggerEvent", "SetText", "Play", "SetDirty", "AddCullingLayer", "SetCullingLayer", "LoadQuestHome",
        };

        /// <summary>
        /// Components of the singletons the client creates lazily into DontDestroyOnLoad, possibly while a world activates
        /// (its components' Awake/OnEnable can be the first user): the SDK's asset registry, tween root, async and UI
        /// coroutine runners and panel input watchdogs, Visual Scripting's persistent singletons, glTFast's defer agent.
        /// A new DontDestroyOnLoad root holding only these (and Transforms) is the client's, not a world escapee; anything
        /// else in it makes it world content, so a world cannot pass off its own content this way.
        /// </summary>
        static readonly HashSet<string> hostSingletonTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "BS.BSAssetRegistry", "DigitalRuby.Tween.TweenFactory", "UnityAsyncAwaitUtil.AsyncCoroutineRunner",
            "BS.VisualScripting.UI.Helpers.CoroutineRunner", "BS.PanelMeshInputBridge+Watchdog",
            "SideQuest.Greenfield.UI.PanelMeshInputBridge+Watchdog", "Unity.VisualScripting.GlobalMessageListener",
            "Unity.VisualScripting.CoroutineRunner", "Unity.VisualScripting.VariablesSaver",
            "GLTFast.TimeBudgetPerFrameDeferAgent", "GLTFast.DefaultDeferAgent",
        };

        /// <summary>
        /// Client singletons a world can also carry as content, whose Awake moves their own GameObject to
        /// DontDestroyOnLoad, with the static field holding the client's instance. Such a component is the client's only
        /// while it is that instance: every AsyncCoroutineRunner persists (and hides) itself, so a world's copy never is.
        /// </summary>
        internal static readonly Dictionary<string, string> singletonInstanceFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "UnityAsyncAwaitUtil.AsyncCoroutineRunner", "_instance" },
            { "BS.BSAssetRegistry", "_instance" },
            { "BS.VisualScripting.UI.Helpers.CoroutineRunner", "_instance" },
        };

        /// <summary>Report lines already logged for the current world, so a reloaded kit prefab does not repeat them.</summary>
        static readonly HashSet<string> loggedThisWorld = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// While false, components from host-app assemblies in bundle content are only reported ("would remove"); when
        /// true they are removed before any Start. <see cref="deniedHostTypes"/> are removed either way.
        /// </summary>
        internal static bool EnforceHostComponentRemoval = false;

        /// <summary>Test seam: behave as if Unity's UnityEvent internals were missing.</summary>
        internal static bool simulateMissingInternals;

#if UNITY_EDITOR
        /// <summary>
        /// Test switch, under this exact name for the probe suite: while true, scenes get no verdict in sceneLoaded, the
        /// per-frame steps do nothing, WorldActivation holds nothing, and a pending world completes as passed (kit
        /// judgements are unaffected). Nothing resets it, play-mode resets included. Editor only.
        /// </summary>
        internal static bool DisabledForTests;
#else
        /// <summary>A player has no test switch: always false, and nothing can set it.</summary>
        internal const bool DisabledForTests = false;
#endif

        /// <summary><see cref="DisabledForTests"/>, read through a property so a player build's constant is not a dead branch.
        /// A development player can also be switched off to measure the guard (<see cref="OffForMeasurement"/>).</summary>
        static bool GuardOff => DisabledForTests || OffForMeasurement;

#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        static int offForMeasurement = -1;

        /// <summary>
        /// Development players only, to measure what judging costs in a world's activation frame: while the file
        /// files/memdiag/guard-off exists when the app starts, world scenes get no verdict and WorldActivation holds nothing,
        /// as <see cref="DisabledForTests"/> does in the editor. The pre-load bundle check is not affected. A release player
        /// has no such switch.
        /// </summary>
        static bool OffForMeasurement
        {
            get
            {
                if (offForMeasurement >= 0) return offForMeasurement == 1;
                var off = false;
                try
                {
                    var dir = Application.persistentDataPath;
                    if (string.IsNullOrEmpty(dir)) return false; // too early: ask again later
                    off = System.IO.File.Exists(System.IO.Path.Combine(dir, "memdiag", "guard-off"));
                }
                catch (Exception)
                {
                    off = false;
                }
                offForMeasurement = off ? 1 : 0;
                if (off) Debug.LogWarning("[UnityEventGuard] OFF for a measurement (development build, files/memdiag/guard-off): worlds load unjudged.");
                return off;
            }
        }
#else
        const bool OffForMeasurement = false;
#endif

#if !UNITY_EDITOR
        // ---- load timing (players, release too): where a world's activation frame goes. Unity activates scenes inside
        // EarlyUpdate.UpdatePreloading, which BeforeSceneActivation and AfterSceneActivation bracket.
        static double timingStepStart;
        static double timingSceneLoadedAt;
        static string timingScene;
        static double timingJudgeMs = -1;
        static string timingJudgeParts;

        static double TimingNow => Time.realtimeSinceStartupAsDouble;

        static string TimingMs(double ms) => ms.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

        static void TimingStepStarted()
        {
            timingStepStart = TimingNow;
            timingSceneLoadedAt = 0;
            timingJudgeMs = -1;
            timingJudgeParts = null;
        }

        static void TimingSceneLoaded(Scene scene)
        {
            if (timingSceneLoadedAt > 0) return;
            timingSceneLoadedAt = TimingNow;
            timingScene = scene.name;
        }

        static void TimingStepEnded()
        {
            if (timingSceneLoadedAt <= 0) return;
            var unity = (timingSceneLoadedAt - timingStepStart) * 1000.0;
            var total = (TimingNow - timingStepStart) * 1000.0;
            var guard = timingJudgeMs < 0 ? 0 : timingJudgeMs;
            Debug.Log("[LoadTiming] activation of '" + Clean(timingScene) + "' (frame " + Time.frameCount + "): " + TimingMs(total)
                      + " ms in Unity's preloading step = Unity's activation up to sceneLoaded " + TimingMs(unity) + " ms + guard "
                      + (timingJudgeMs < 0 ? (GuardOff ? "off" : "not run") : TimingMs(guard) + " ms (" + timingJudgeParts + ")")
                      + " + other sceneLoaded handlers and the rest of the step " + TimingMs(total - unity - guard) + " ms.");
            if (timingScene != "Void")
            {
                timingMarks.Clear();
                timingMarks.Add(new KeyValuePair<string, double>("rest of EarlyUpdate", TimingNow));
                timingDetail.Clear();
                timingDetailOpen = null;
                timingGcAtStep = GC.CollectionCount(0);
                timingFramesLeft = 2;
            }
            timingSceneLoadedAt = 0;
        }

        // Frame phases after a world's activation step, this frame and the next: where the rest of the long frame goes.
        struct TimingMarkInitialization { }
        struct TimingMarkEarlyUpdate { }
        struct TimingMarkFixedUpdate { }
        struct TimingMarkPreUpdate { }
        struct TimingMarkUpdate { }
        struct TimingMarkPreLateUpdate { }
        struct TimingMarkPostLateUpdate { }
        struct TimingMarkFrameEnd { }

        const string TimingFrameEnd = "between frames";
        static readonly List<KeyValuePair<string, double>> timingMarks = new List<KeyValuePair<string, double>>();
        static int timingFramesLeft;
        static bool timingMarksInstalled;

        static void TimingMark(string segment)
        {
            try
            {
                TimingMarkCore(segment);
            }
            catch (Exception)
            {
                timingFramesLeft = 0; // logging only
            }
        }

        static void TimingMarkCore(string segment)
        {
            if (timingFramesLeft <= 0) return;
            timingMarks.Add(new KeyValuePair<string, double>(segment, TimingNow));
            if (segment != TimingFrameEnd || --timingFramesLeft > 0) return;
            var text = new StringBuilder();
            for (var i = 1; i < timingMarks.Count; i++)
            {
                if (i > 1) text.Append(", ");
                text.Append(timingMarks[i - 1].Key).Append(' ').Append(TimingMs((timingMarks[i].Value - timingMarks[i - 1].Value) * 1000.0));
            }
            Debug.Log("[LoadTiming] after the activation step, this frame and the next (ms per part): " + text);
            timingMarks.Clear();
            TimingDetailMark(null);
            var slow = new StringBuilder();
            foreach (var step in timingDetail)
            {
                if (slow.Length > 0) slow.Append(", ");
                slow.Append(step.Key).Append(' ').Append(TimingMs(step.Value));
            }
            Debug.Log("[LoadTiming] steps of 5 ms or more in EarlyUpdate and PostLateUpdate, same frames (ms): "
                      + (slow.Length > 0 ? slow.ToString() : "none") + "; garbage collections " + (GC.CollectionCount(0) - timingGcAtStep) + ".");
            timingDetail.Clear();
        }

        // Per-step marks in EarlyUpdate and PostLateUpdate (where Unity loads, unloads and renders): which of its own steps
        // holds the frames after a world's activation. Only recorded while the frame marks are (two frames after one).
        static readonly List<KeyValuePair<string, double>> timingDetail = new List<KeyValuePair<string, double>>();
        static string timingDetailOpen;
        static double timingDetailStart;
        static int timingGcAtStep;

        static void TimingDetailMark(string next)
        {
            try
            {
                var now = TimingNow;
                if (timingDetailOpen != null && timingFramesLeft > 0)
                {
                    var ms = (now - timingDetailStart) * 1000.0;
                    if (ms >= 5.0) timingDetail.Add(new KeyValuePair<string, double>(timingDetailOpen, ms));
                }
                timingDetailOpen = timingFramesLeft > 0 ? next : null;
                timingDetailStart = now;
            }
            catch (Exception)
            {
                timingDetailOpen = null; // logging only
            }
        }

        struct TimingDetailMarker { }

        /// <summary>Each step of a phase with a mark before it, and one after the last.</summary>
        static List<PlayerLoopSystem> WithDetailMarks(string phase, IList<PlayerLoopSystem> systems)
        {
            var marked = new List<PlayerLoopSystem>(systems.Count * 2 + 1);
            foreach (var system in systems)
            {
                var name = phase + "." + (system.type != null ? system.type.Name : "?");
                marked.Add(new PlayerLoopSystem { type = typeof(TimingDetailMarker), updateDelegate = () => TimingDetailMark(name) });
                marked.Add(system);
            }
            marked.Add(new PlayerLoopSystem { type = typeof(TimingDetailMarker), updateDelegate = () => TimingDetailMark(null) });
            return marked;
        }

        static PlayerLoopSystem TimingMarkSystem<T>(string segment) =>
            new PlayerLoopSystem { type = typeof(T), updateDelegate = () => TimingMark(segment) };

        static void InstallTimingMarks()
        {
            if (timingMarksInstalled || !Application.isPlaying) return;
            timingMarksInstalled = true;
            try
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                var phases = loop.subSystemList;
                for (var i = 0; i < phases.Length; i++)
                {
                    var phase = phases[i].type;
                    var systems = new List<PlayerLoopSystem>(phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                    if (phase == typeof(UnityEngine.PlayerLoop.Initialization)) systems.Insert(0, TimingMarkSystem<TimingMarkInitialization>("Initialization"));
                    else if (phase == typeof(UnityEngine.PlayerLoop.EarlyUpdate))
                    {
                        systems = WithDetailMarks("EarlyUpdate", systems);
                        systems.Insert(0, TimingMarkSystem<TimingMarkEarlyUpdate>("EarlyUpdate"));
                    }
                    else if (phase == typeof(UnityEngine.PlayerLoop.FixedUpdate)) systems.Insert(0, TimingMarkSystem<TimingMarkFixedUpdate>("FixedUpdate"));
                    else if (phase == typeof(UnityEngine.PlayerLoop.PreUpdate)) systems.Insert(0, TimingMarkSystem<TimingMarkPreUpdate>("PreUpdate"));
                    else if (phase == typeof(UnityEngine.PlayerLoop.Update)) systems.Insert(0, TimingMarkSystem<TimingMarkUpdate>("Update (Start, Update)"));
                    else if (phase == typeof(UnityEngine.PlayerLoop.PreLateUpdate)) systems.Insert(0, TimingMarkSystem<TimingMarkPreLateUpdate>("PreLateUpdate (LateUpdate)"));
                    else if (phase == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    {
                        systems = WithDetailMarks("PostLateUpdate", systems);
                        systems.Insert(0, TimingMarkSystem<TimingMarkPostLateUpdate>("PostLateUpdate (rendering)"));
                        systems.Add(TimingMarkSystem<TimingMarkFrameEnd>(TimingFrameEnd));
                    }
                    else continue;
                    phases[i].subSystemList = systems.ToArray();
                }
                loop.subSystemList = phases;
                PlayerLoop.SetPlayerLoop(loop);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LoadTiming] could not add the frame marks: " + e.Message);
            }
        }
#endif

        /// <summary>
        /// Refuse a scene outside the build that matches nothing while world content may be loaded (R6-3). On in
        /// Greenfield, which loads no such scene of its own; off in a creator project, whose editor can load one any time.
        /// </summary>
        internal static bool RefuseUnmatchedNonBuildScenes =
#if GREENFIELD_PROJECT
            true;
#else
            false;
#endif

        /// <summary>
        /// R6-2, off until the probe run shows how instance ids split: when on, a new DontDestroyOnLoad root counts as a
        /// world escapee (judged with the world, moved back into its scene) only when its instance id is positive, as for
        /// objects loaded from data; a client object created during the activation (negative id) is left alone.
        /// </summary>
        internal static bool EscapeesOnlyFromData = false;

        static bool EnforceAllowList =>
#if GREENFIELD_PROJECT
            BanterStubsAllowed.EnforceStrictForBundles;
#else
            false;
#endif

        // ------------------------------------------------------------------ world scenes

        static readonly List<PendingWorld> pendingWorlds = new List<PendingWorld>();

        /// <summary>Scenes the guard refused that are still loaded (deactivated until Void or a newer world replaces them).</summary>
        static readonly HashSet<int> refusedScenes = new HashSet<int>();

        /// <summary>Whether the guard refused this scene: never world content, whatever else holds.</summary>
        internal static bool IsRefusedScene(Scene scene) => scene.IsValid() && refusedScenes.Contains(scene.handle);

        /// <summary>
        /// Subscribed before any other runtime code (SubsystemRegistration precedes the BeforeSceneLoad subscribers) and
        /// moved to the front again whenever a world is armed, so content is judged before other handlers see it.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void SubscribeFirst()
        {
            pendingWorlds.Clear();
            refusedScenes.Clear();
            escapeesByScene.Clear();
            bundleScenePaths.Clear();
            seenBundles.Clear();
            judgedClips.Clear();
            worldScenes.Clear();
            persistentRootsAtArm.Clear();
            sceneLessAtArm.Clear();
            worldScopeExtensions.Clear(); // the client registers again as it starts (no stale delegate across play sessions)
            lentOutByWorld.Clear();
            lentOutIds.Clear();
            currentWorld = 0;
            frameHookInstalled = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
#if GREENFIELD_PROJECT
            WorldActivation.SetWorldContentProbe(WorldContentPossible);
            WorldActivation.SetLendingHooks(IsWorldScoped, LentOut);
            WorldActivation.SetInertProbe(() => GuardOff);
#endif
            InstallFrameHook();
#if !UNITY_EDITOR
            InstallTimingMarks();
#endif
        }

        /// <summary>
        /// Every scene gets a verdict here. A pending world is judged; a bundle scene nobody asked for (a world loading
        /// its own other scene, or itself again, from a pre-guard sender or a released graph) is refused, and so is any
        /// scene outside the build while world content may be loading or loaded (<see cref="MayBeClientScene"/>): a bundle
        /// scene whose path matched nothing. Anything else is the client's own and its held machines (a scene outside the
        /// build in the editor) start.
        /// </summary>
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
#if !UNITY_EDITOR
            try { TimingSceneLoaded(scene); } catch (Exception) { } // logging only
#endif
            if (GuardOff) return;
            for (var i = 0; i < pendingWorlds.Count; i++)
            {
                var world = pendingWorlds[i];
                if (!world.Matches(scene)) continue;
                pendingWorlds.RemoveAt(i);
                world.Judge(scene);
                return;
            }
            if (IsBundleScene(scene))
            {
                RefuseUnexpected(scene, mode, NewPersistentRoots());
                return;
            }
            if (!MayBeClientScene(scene))
            {
                // A world whose Scene.path differs from its bundle's scene path list lands here too: refused now, and its
                // pending load then fails closed as never seen. The warning shows whether that ever happens.
                Debug.LogWarning("[UnityEventGuard] scene '" + Clean(scene.name) + "' ('" + Clean(scene.path) + "') is outside "
                                 + "the build and matches no pending world or recorded bundle scene path; treating it as one.");
                RefuseUnexpected(scene, mode, NewPersistentRoots());
                return;
            }
#if GREENFIELD_PROJECT
            WorldActivation.ReleaseScene(scene, null);
#endif
        }

        /// <summary>
        /// Whether a scene may be the client's own: one in the build, or one outside it while no world load is pending and
        /// no scene bundle has been seen this session. Greenfield loads no non-build scene of its own (Void loads by build
        /// index; Basis's scene-bundle loader has no callers), so otherwise it is a bundle scene whose path matched nothing.
        /// </summary>
        internal static bool MayBeClientScene(Scene scene, bool playing) =>
            scene.buildIndex >= 0 || !playing || !RefuseUnmatchedNonBuildScenes || !WorldContentPossible();

        static bool MayBeClientScene(Scene scene) => MayBeClientScene(scene, Application.isPlaying);

        /// <summary>A world load is pending, or a scene bundle has been loaded this session (polled while none has been seen).</summary>
        static bool WorldContentPossible() => pendingWorlds.Count > 0 || seenBundles.Count > 0 || PollSceneBundles();

        static void OnSceneUnloaded(Scene scene)
        {
            refusedScenes.Remove(scene.handle);
            // A passed world's DontDestroyOnLoad escapees were moved into its scene; any the move missed go with it here.
            if (escapeesByScene.TryGetValue(scene.handle, out var escapees))
            {
                escapeesByScene.Remove(scene.handle);
                // Every object recorded, not just the roots: a child detached later is a DontDestroyOnLoad root of its own.
                foreach (var gameObject in escapees)
                {
                    if (gameObject == null) continue;
                    gameObject.SetActive(false); // no Update in the next world before the destroy lands
                    DestroyNow(gameObject);
                }
            }
            if (worldScenes.Remove(scene.handle))
            {
                SweepSurvivors(scene);
                ReclaimLentOut(scene.handle);
            }
            if (currentWorld == scene.handle) currentWorld = 0;
#if GREENFIELD_PROJECT
            WorldActivation.ForgetScene(scene);
#endif
        }

        /// <summary>World scenes (handles) judged or refused and not yet unloaded: swept for survivors when they unload.</summary>
        static readonly HashSet<int> worldScenes = new HashSet<int>();

        // ---- world objects a world graph parented outside the world (R6-1)

        /// <summary>The world scene (handle) that passed last and has not unloaded; 0 when none.</summary>
        static int currentWorld;

        /// <summary>The current world's scene handle (tests).</summary>
        internal static int CurrentWorldScene => currentWorld;

        /// <summary>World GameObjects a world graph put outside the world, by the world scene they go with.</summary>
        static readonly Dictionary<int, List<GameObject>> lentOutByWorld = new Dictionary<int, List<GameObject>>();

        /// <summary>The same objects' instance ids: anything inside one of them is still the world's.</summary>
        static readonly HashSet<int> lentOutIds = new HashSet<int>();

        /// <summary>
        /// A transform the current world owns: in its scene or under the world root (<see cref="BSWorldScope"/>), or
        /// inside an object a world graph lent out (what a graph there parents or instantiates is still the world's).
        /// </summary>
        internal static bool IsWorldScoped(Transform transform)
        {
            if (transform == null) return false;
            if (BSWorldScope.Contains(transform.gameObject)) return true;
            if (lentOutIds.Count > 0)
            {
                for (var current = transform; current != null; current = current.parent)
                {
                    if (lentOutIds.Contains(current.gameObject.GetInstanceID())) return true;
                }
            }
            return IsHeldElsewhere(transform);
        }

        static readonly List<Func<Transform, bool>> worldScopeExtensions = new List<Func<Transform, bool>>();

        /// <summary>
        /// For the client (R7-1): world content the client itself holds outside the world, such as a world object a peer
        /// is holding, which AttachmentsSystem parents under the peer's avatar bone. <paramref name="isWorldContentHeldElsewhere"/>
        /// answers true for a transform inside such an object; the guard then counts it as the world's, so graphs on it
        /// are world graphs and what they parent or instantiate outside the world is reported and destroyed with the world.
        /// Called on the main thread for each graph parenting call: keep it cheap. One that throws answers false. Register
        /// as the client starts; play-mode start clears the list. Not for world content: graphs must not reach it.
        /// </summary>
        public static void AddWorldScopeExtension(Func<Transform, bool> isWorldContentHeldElsewhere)
        {
            if (isWorldContentHeldElsewhere != null && !worldScopeExtensions.Contains(isWorldContentHeldElsewhere))
                worldScopeExtensions.Add(isWorldContentHeldElsewhere);
        }

        /// <summary>Removes an extension added with <see cref="AddWorldScopeExtension"/> (call it when its owner is destroyed).</summary>
        public static void RemoveWorldScopeExtension(Func<Transform, bool> isWorldContentHeldElsewhere) =>
            worldScopeExtensions.Remove(isWorldContentHeldElsewhere);

        /// <summary>Whether a client extension says <paramref name="transform"/> is world content it holds outside the world.</summary>
        internal static bool IsHeldElsewhere(Transform transform)
        {
            for (var i = 0; i < worldScopeExtensions.Count; i++)
            {
                try
                {
                    if (worldScopeExtensions[i](transform)) return true;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            return false;
        }

        /// <summary>
        /// A world graph parented this world GameObject outside the world (a player's rig, another client object), or
        /// instantiated it there: allowed, and destroyed when the current world's scene unloads.
        /// </summary>
        internal static void LentOut(GameObject gameObject)
        {
            if (gameObject == null || !lentOutIds.Add(gameObject.GetInstanceID())) return;
            if (!lentOutByWorld.TryGetValue(currentWorld, out var lent)) lentOutByWorld[currentWorld] = lent = new List<GameObject>();
            lent.Add(gameObject);
        }

        /// <summary>
        /// A world's scene unloaded: every object it lent out that still exists is deactivated and destroyed (Unity has no
        /// hook before an unload to move it back), with its children; and those lent while no world had passed. Returns
        /// how many, and logs the count.
        /// </summary>
        internal static int ReclaimLentOut(int worldScene)
        {
            var count = Reclaim(worldScene) + (worldScene != 0 ? Reclaim(0) : 0);
            if (count > 0)
                Debug.LogWarning("[UnityEventGuard] " + count + " world object(s) a graph had parented outside the world (a player's rig "
                                 + "or another client object) destroyed with the world.");
            return count;
        }

        static int Reclaim(int worldScene)
        {
            if (!lentOutByWorld.TryGetValue(worldScene, out var lent)) return 0;
            lentOutByWorld.Remove(worldScene);
            var count = 0;
            foreach (var gameObject in lent)
            {
                lentOutIds.Remove(gameObject.GetInstanceID()); // the id outlives a destroyed object
                if (gameObject == null) continue; // went with the scene, or was destroyed
                count++;
                Deactivate(gameObject, true);
            }
            return count;
        }

        /// <summary>
        /// After a world's scene unloaded: a GameObject still in it outlived the unload (DontSave bits set after the
        /// verdict, if Unity keeps such objects): deactivated and destroyed, and counted in a warning. Only objects that
        /// claim the world's own scene: the client's DontDestroyOnLoad objects (avatars, polaroids, toolbars) are never
        /// touched, which a diff over every object created during the world could not promise.
        /// </summary>
        static void SweepSurvivors(Scene scene)
        {
            if (!Application.isPlaying) return;
            var roots = new HashSet<GameObject>();
            foreach (var gameObject in AllGameObjects())
            {
                if (gameObject != null && gameObject.scene.handle == scene.handle) roots.Add(gameObject.transform.root.gameObject);
            }
            if (roots.Count == 0) return;
            Debug.LogWarning("[UnityEventGuard] " + roots.Count + " root(s) of world '" + Clean(scene.name) + "' outlived its unload; destroying them.");
            foreach (var root in roots) Deactivate(root, true);
        }

        /// <summary>
        /// Every GameObject in a passed world's DontDestroyOnLoad escapee hierarchies, by the world scene's handle: moved
        /// into that scene at the verdict, and destroyed when it unloads in case a move failed.
        /// </summary>
        static readonly Dictionary<int, List<GameObject>> escapeesByScene = new Dictionary<int, List<GameObject>>();

        static void MoveToFront()
        {
            UnityAction<Scene, LoadSceneMode> handler = OnSceneLoaded;
            try
            {
                var field = typeof(SceneManager).GetField("sceneLoaded", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null && field.FieldType == typeof(UnityAction<Scene, LoadSceneMode>))
                {
                    var others = Delegate.Remove((Delegate)field.GetValue(null), handler);
                    field.SetValue(null, Delegate.Combine(handler, others));
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UnityEventGuard] could not move the sceneLoaded handler to the front: " + e.Message);
            }
            SceneManager.sceneLoaded -= handler;
            SceneManager.sceneLoaded += handler;
        }

        /// <summary>Every scene path of every streamed-scene bundle seen this session (a bundle can unload before its scene activates).</summary>
        static readonly HashSet<string> bundleScenePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<int> seenBundles = new HashSet<int>();

        /// <summary>
        /// Records the scene paths (normalised) of streamed-scene bundles loaded now, each bundle read once; true when any
        /// is loaded. GetAllLoadedAssetBundles wraps a native call that returns a new array every time (there is no
        /// non-allocating form), so this runs every frame only while a world is pending, every 15th frame otherwise, and
        /// on demand (an unmatched non-build scene, a held machine's scene before any bundle was seen).
        /// </summary>
        static bool PollSceneBundles()
        {
            var any = false;
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null || !bundle.isStreamedSceneAssetBundle) continue;
                any = true;
                if (!seenBundles.Add(bundle.GetInstanceID())) continue;
                foreach (var path in bundle.GetAllScenePaths()) bundleScenePaths.Add(NormalizePath(path));
            }
            return any;
        }

        /// <summary>A scene from a scene bundle (not one in the build), loaded now or seen this session.</summary>
        static bool IsBundleScene(Scene scene)
        {
            if (!scene.IsValid() || scene.buildIndex >= 0) return false;
            var path = NormalizePath(scene.path);
            if (bundleScenePaths.Contains(path)) return true;
            PollSceneBundles();
            return bundleScenePaths.Contains(path);
        }

        /// <summary>'/' separators; compared case-insensitively. A bundle's scene list and Scene.path may differ in either.</summary>
        static string NormalizePath(string path) => string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/');

        /// <summary>Whether two scene paths name the same scene (normalised, case-insensitive; empty never matches).</summary>
        internal static bool SamePath(string a, string b)
        {
            var left = NormalizePath(a);
            return left.Length > 0 && string.Equals(left, NormalizePath(b), StringComparison.OrdinalIgnoreCase);
        }

        // ---- DontDestroyOnLoad: a world root its own Awake moves there is still world content

        /// <summary>DontDestroyOnLoad roots (instance ids) present just before the scene activation in progress.</summary>
        static readonly HashSet<int> persistentRootsBefore = new HashSet<int>();
        static GameObject persistentProbe;

        static readonly List<GameObject> persistentRootsBuffer = new List<GameObject>();

        static List<GameObject> PersistentRoots()
        {
            persistentRootsBuffer.Clear();
            if (!Application.isPlaying) return persistentRootsBuffer;
            if (persistentProbe == null)
            {
                persistentProbe = new GameObject("BSUnityEventGuard (DontDestroyOnLoad probe)") { hideFlags = HideFlags.HideInHierarchy };
                UnityObject.DontDestroyOnLoad(persistentProbe);
            }
            persistentProbe.scene.GetRootGameObjects(persistentRootsBuffer);
            return persistentRootsBuffer;
        }

        static void RefreshPersistentRoots()
        {
            persistentRootsBefore.Clear();
            foreach (var root in PersistentRoots()) persistentRootsBefore.Add(root.GetInstanceID());
#if GREENFIELD_PROJECT
            WorldActivation.SetPersistentRootsBefore(persistentRootsBefore);
#endif
        }

        /// <summary>
        /// DontDestroyOnLoad roots that appeared since the last refresh, which runs right before scene activation. Roots the
        /// root list may leave out (HideAndDontSave, from data or an Awake like AsyncCoroutineRunner's) are found through
        /// every loaded GameObject; for those, "before" is the full scan taken when the world was armed. A root the client
        /// itself creates meanwhile (a lazily made singleton, a callback of an async load finishing in the same step) is
        /// counted too, and so goes with the world if it fails or unloads: an availability cost, not a way past the guard
        /// (R3-4).
        /// </summary>
        static List<GameObject> NewPersistentRoots(GameObject[] all = null)
        {
            var found = new List<GameObject>();
            var considered = new HashSet<GameObject>();
            foreach (var root in PersistentRoots())
            {
                if (considered.Add(root) && IsNewEscapee(root)) found.Add(root);
            }
            if (persistentProbe != null)
            {
                var objects = all ?? AllGameObjects();
                var persistent = persistentProbe.scene;
                foreach (var gameObject in objects)
                {
                    if (gameObject == null || gameObject.scene != persistent) continue;
                    var root = gameObject.transform.root.gameObject;
                    if (considered.Add(root) && IsNewEscapee(root)) found.Add(root);
                }
                foreach (var root in SceneLessRoots(objects))
                {
                    if (considered.Add(root)) found.Add(root);
                }
            }
            return found;
        }

        /// <summary>Live roots outside every scene (instance ids) a full scan found when a world was armed. Kept for the session.</summary>
        static readonly HashSet<int> sceneLessAtArm = new HashSet<int>();

        /// <summary>
        /// Live roots outside every scene: a root given HideAndDontSave at run time (a pre-guard sender, an Awake) leaves its
        /// scene (handle 0), neither root lookup finds it, and it outlives the unload (probe Q7). One that appeared since the
        /// world was armed, carries hide or DontSave bits (a bundle's assets carry none: the bits do not survive the build,
        /// and the pre-load check refuses them in data), is live (an active prefab asset is not active in a hierarchy) and
        /// is not the client's (<see cref="IsWorldEscapee"/>) is the world's: judged with it, its bits cleared, moved back
        /// into its scene.
        /// </summary>
        internal static List<GameObject> SceneLessRoots(GameObject[] all)
        {
            var found = new List<GameObject>();
            foreach (var gameObject in all)
            {
                if (gameObject == null || gameObject.scene.IsValid() || gameObject.transform.parent != null) continue;
                if ((gameObject.hideFlags & HidingOrPersisting) == 0) continue;
                if (gameObject.activeSelf && !gameObject.activeInHierarchy) continue; // a prefab asset
                var id = gameObject.GetInstanceID();
                if (sceneLessAtArm.Contains(id) || (EscapeesOnlyFromData && id <= 0)) continue;
                if (IsWorldEscapee(gameObject)) found.Add(gameObject);
            }
            return found;
        }

        static bool IsNewEscapee(GameObject root)
        {
            if (root == persistentProbe) return false;
            var id = root.GetInstanceID();
            if (EscapeesOnlyFromData && id <= 0) return false; // created at run time: the client's (R6-2)
            return !persistentRootsBefore.Contains(id) && !persistentRootsAtArm.Contains(id) && IsWorldEscapee(root);
        }

        /// <summary>
        /// DontDestroyOnLoad roots (instance ids) a full scan found when a world was armed, hidden ones included: the
        /// client's, before that load began. Kept for the session.
        /// </summary>
        static readonly HashSet<int> persistentRootsAtArm = new HashSet<int>();

        static void RememberPersistentRoots(GameObject[] all)
        {
            if (persistentProbe == null) return;
            var persistent = persistentProbe.scene;
            foreach (var gameObject in all)
            {
                if (gameObject == null) continue;
                if (gameObject.scene == persistent) persistentRootsAtArm.Add(gameObject.transform.root.gameObject.GetInstanceID());
                else if (!gameObject.scene.IsValid() && gameObject.transform.parent == null && (gameObject.hideFlags & HidingOrPersisting) != 0)
                    sceneLessAtArm.Add(gameObject.GetInstanceID());
            }
        }

        /// <summary>
        /// Every GameObject Unity holds, assets and hidden ones included: Scene.GetRootGameObjects may leave out roots
        /// flagged HideAndDontSave or DontSave (unverified), and those flags can come from a bundle's data.
        /// </summary>
        static GameObject[] AllGameObjects() => Resources.FindObjectsOfTypeAll<GameObject>();

        /// <summary>
        /// The roots of <paramref name="scene"/>: its root list (which throws for a scene that is not loaded) plus the root
        /// of every GameObject in it that the list left out, counted in <paramref name="unlisted"/>. An asset's scene is
        /// never valid, so assets are not among them.
        /// </summary>
        static List<GameObject> SceneRoots(Scene scene, GameObject[] all, out int unlisted)
        {
            unlisted = 0;
            var roots = new List<GameObject>(scene.GetRootGameObjects());
            var seen = new HashSet<GameObject>(roots);
            foreach (var gameObject in all ?? AllGameObjects())
            {
                if (gameObject == null || gameObject.scene != scene) continue;
                var root = gameObject.transform.root.gameObject;
                if (seen.Add(root))
                {
                    roots.Add(root);
                    unlisted++;
                }
            }
            return roots;
        }

        /// <summary>
        /// A new DontDestroyOnLoad root is world content unless every component in it is a Transform or one of the
        /// client's lazily created singletons (<see cref="hostSingletonTypes"/>; for <see cref="singletonInstanceFields"/>
        /// only the client's current instance). Residual: a client object of another kind created during the activation
        /// step is counted as the world's and goes with it (availability, not a bypass).
        /// </summary>
        internal static bool IsWorldEscapee(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) return true; // a missing script came from data
                if (component is Transform) continue;
                if (!IsClientSingleton(component)) return true;
            }
            return false;
        }

        static bool IsClientSingleton(Component component)
        {
            var type = component.GetType();
            var name = type.FullName ?? "";
            if (!hostSingletonTypes.Contains(name)) return false;
            if (!singletonInstanceFields.TryGetValue(name, out var fieldName)) return true;
            // A missing field (pinned by a test) keeps the old rule rather than taking the client's own singleton away.
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return field == null || ReferenceEquals(field.GetValue(null), component);
        }

        /// <summary>Marks the refresh in the player loop: just before EarlyUpdate.UpdatePreloading activates loaded scenes.</summary>
        struct GuardBeforeSceneActivation
        {
        }

        /// <summary>Marks the settling step in the player loop: just after EarlyUpdate.UpdatePreloading.</summary>
        struct GuardAfterSceneActivation
        {
        }

        static bool frameHookInstalled;

        static void InstallFrameHook()
        {
            if (frameHookInstalled || !Application.isPlaying) return;
            frameHookInstalled = true;
            try
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                var phases = loop.subSystemList;
                for (var i = 0; i < phases.Length; i++)
                {
                    if (phases[i].type != typeof(UnityEngine.PlayerLoop.EarlyUpdate)) continue;
                    var systems = new List<PlayerLoopSystem>(phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                    if (systems.Exists(s => s.type == typeof(GuardBeforeSceneActivation))) return;
                    var at = systems.FindIndex(s => s.type == typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdatePreloading));
                    if (at < 0)
                    {
                        Debug.LogWarning("[UnityEventGuard] EarlyUpdate.UpdatePreloading not found; scene activation is not bracketed.");
                        at = 0;
                    }
                    systems.Insert(at + 1,
                        new PlayerLoopSystem { type = typeof(GuardAfterSceneActivation), updateDelegate = AfterSceneActivation });
                    systems.Insert(at,
                        new PlayerLoopSystem { type = typeof(GuardBeforeSceneActivation), updateDelegate = BeforeSceneActivation });
                    phases[i].subSystemList = systems.ToArray();
                    loop.subSystemList = phases;
                    PlayerLoop.SetPlayerLoop(loop);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UnityEventGuard] could not hook the player loop; DontDestroyOnLoad escapees are counted from the load's start: " + e.Message);
            }
        }

        /// <summary>
        /// Each frame right before activation: record scene bundles, and while one is loaded (or a world is pending)
        /// snapshot DontDestroyOnLoad, so a root that appears during the activation is known to come from it, while the
        /// client's own (avatars, toasts) from earlier frames are not.
        /// </summary>
        static void BeforeSceneActivation()
        {
#if !UNITY_EDITOR
            try { TimingStepStarted(); } catch (Exception) { } // logging only
#endif
            if (GuardOff)
            {
#if GREENFIELD_PROJECT
                WorldActivation.SetActivationInProgress(false);
#endif
                return;
            }
            // Bundles every frame a world is pending, otherwise every 15th (each poll allocates an array); IsBundleScene and
            // WorldContentPossible poll on demand too.
            if (pendingWorlds.Count > 0 || Time.frameCount % 15 == 0) PollSceneBundles();
            RefreshPersistentRoots();
#if GREENFIELD_PROJECT
            WorldActivation.SetActivationInProgress(true);
#endif
        }

        /// <summary>
        /// Right after activation: every scene activated in this step had its verdict in sceneLoaded. Settle whatever is
        /// still held without one (machines on client DontDestroyOnLoad roots, a scene no sceneLoaded announced that is not
        /// a bundle scene), so nothing is held forever.
        /// </summary>
        static void AfterSceneActivation()
        {
#if !UNITY_EDITOR
            try { TimingStepEnded(); } catch (Exception) { } // logging only
#endif
#if GREENFIELD_PROJECT
            WorldActivation.SetActivationInProgress(false);
            if (GuardOff) return;
            WorldActivation.SettleUnjudged(scene => MayBeClientScene(scene) && !IsBundleScene(scene));
#endif
        }

        // ---- one world load

        /// <summary>
        /// Arms the guard for the bundle scene <paramref name="scenePath"/>: create it just before the load starts, then
        /// call <see cref="PendingWorld.Complete"/> (or dispose it) once the load has finished.
        /// </summary>
        internal static PendingWorld SanitizeWhenLoaded(string scenePath, string sceneName) => new PendingWorld(scenePath, sceneName);

        /// <summary>One world scene load: judged as it loads; failed closed when it fails or is never seen.</summary>
        internal sealed class PendingWorld : IDisposable
        {
            readonly string scenePath, sceneName;
            /// <summary>This world's scene load (BSAssetBundle counts them): fail-closed only loads Void while it is the latest.</summary>
            readonly int generation;
#if GREENFIELD_PROJECT
            /// <summary>This world's own graph hold: other loads' holds are not touched by it.</summary>
            readonly WorldActivation.Window hold;
#endif
            bool judged, passed, released;

            internal PendingWorld(string scenePath, string sceneName)
            {
                this.scenePath = scenePath;
                this.sceneName = sceneName;
                generation = BSAssetBundle.SceneLoadGeneration;
                PollSceneBundles();
                RememberClips(); // what is loaded now is not this world's
                pendingWorlds.Add(this);
                MoveToFront();
                InstallFrameHook();
#if GREENFIELD_PROJECT
                // The world's graphs stay held (enabled, not listening, no On Enable/On Start) until it has been judged.
                hold = WorldActivation.Open();
#endif
                RefreshPersistentRoots();
                if (persistentProbe != null) RememberPersistentRoots(AllGameObjects()); // hidden client roots: not this world's
            }

            /// <summary>
            /// By the bundle's scene path only (normalised, case-insensitive): another scene with the same file name is not
            /// this world.
            /// </summary>
            internal bool Matches(Scene scene)
            {
                if (SamePath(scene.path, scenePath)) return true;
                if (scene.name == sceneName)
                    Debug.LogWarning("[UnityEventGuard] scene '" + Clean(scene.name) + "' has the world's name but not its path ('"
                                     + Clean(scene.path) + "' vs '" + Clean(scenePath) + "'); not treating it as the world.");
                return false;
            }

            internal void Judge(Scene scene)
            {
#if !UNITY_EDITOR
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double allMs = 0, rootsMs = 0, scanMs = 0;
                var objects = 0;
#endif
                judged = true;
                if (scene.IsValid()) worldScenes.Add(scene.handle);
                List<GameObject> escaped = null;
                string failure = null;
                try
                {
                    var all = Application.isPlaying ? AllGameObjects() : null; // one scan for both
#if !UNITY_EDITOR
                    allMs = clock.Elapsed.TotalMilliseconds;
                    objects = all != null ? all.Length : 0;
#endif
                    escaped = NewPersistentRoots(all);
#if !UNITY_EDITOR
                    rootsMs = clock.Elapsed.TotalMilliseconds;
#endif
                    var report = SanitizeScene(scene, escaped, all);
#if !UNITY_EDITOR
                    scanMs = clock.Elapsed.TotalMilliseconds;
#endif
                    passed = !report.Failed;
                    if (!passed) failure = report.FailureReason;
                }
                catch (Exception e)
                {
                    passed = false;
                    failure = Printable(e.Message);
                }
                escaped = escaped ?? new List<GameObject>();
                if (passed && scene.IsValid()) currentWorld = scene.handle;
                if (passed && escaped.Count > 0 && scene.IsValid())
                {
                    var hierarchy = new List<GameObject>();
                    foreach (var root in escaped)
                    {
                        if (root == null) continue;
                        foreach (var transform in root.GetComponentsInChildren<Transform>(true)) hierarchy.Add(transform.gameObject);
                    }
                    escapeesByScene[scene.handle] = hierarchy;
                    // Back into the world's scene, before its graphs start: what they parent later, or let go of, stays
                    // there and unloads with it (N5-2).
                    foreach (var root in escaped)
                    {
                        try
                        {
                            if (root != null && root.transform.parent == null) SceneManager.MoveGameObjectToScene(root, scene);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[UnityEventGuard] could not move a DontDestroyOnLoad escapee back into world '"
                                             + Clean(scene.name) + "' (it is destroyed when the world unloads): " + Printable(e.Message));
                        }
                    }
                }
                try
                {
                    // A world that failed drops its graphs before it is emptied and deactivated.
                    EndActivationHold(scene, escaped);
                }
                finally
                {
                    if (!passed) FailClosed(scene, escaped, failure, generation);
#if !UNITY_EDITOR
                    try
                    {
                        var endMs = clock.Elapsed.TotalMilliseconds;
                        timingJudgeMs = endMs;
                        timingJudgeParts = "listing all " + objects + " GameObjects " + TimingMs(allMs) + " ms, new persistent roots "
                                           + TimingMs(rootsMs - allMs) + " ms, scan " + TimingMs(scanMs - rootsMs)
                                           + " ms, releasing graphs " + TimingMs(endMs - scanMs) + " ms";
                    }
                    catch (Exception)
                    {
                        // logging only
                    }
#endif
                }
            }

            /// <summary>
            /// Call once the load has finished. True when the world was judged and passed; otherwise it has been
            /// refused (emptied, deactivated, unloading to Void unless a newer world replaces it), and the caller must not
            /// set it up.
            /// </summary>
            internal bool Complete()
            {
                pendingWorlds.Remove(this);
                if (!judged)
                {
                    judged = true;
                    var scene = SceneManager.GetSceneByPath(scenePath);
                    if (!scene.IsValid()) scene = SceneManager.GetSceneByName(sceneName);
                    var none = new List<GameObject>(); // its DontDestroyOnLoad escapees can no longer be told apart
                    if (GuardOff)
                    {
                        // The guard is off for a test: an unjudged world runs as Unity runs it.
                        passed = true;
                        EndActivationHold(scene, none);
                        return passed;
                    }
                    passed = false;
                    try
                    {
                        EndActivationHold(scene, none);
                    }
                    finally
                    {
                        FailClosed(scene, none, "the guard never saw it load", generation);
                    }
                }
                return passed;
            }

            public void Dispose() => Complete();

            /// <summary>Ends this world's graph hold once: a world that passed starts its machines; any other drops them.</summary>
            void EndActivationHold(Scene scene, List<GameObject> escaped)
            {
                if (released) return;
                released = true;
#if GREENFIELD_PROJECT
                WorldActivation.Close(hold, scene, escaped, passed);
#endif
            }
        }

        /// <summary>
        /// Void only while no newer world load has started since this one: a Void queued behind a newer world's Single
        /// load would replace that world. A refused world a newer load is replacing stays refused and deactivated.
        /// </summary>
        internal static bool ShouldLoadVoid(int worldGeneration, int latestGeneration) => worldGeneration == latestGeneration;

        static void FailClosed(Scene scene, List<GameObject> escaped, string why, int generation)
        {
            var loadVoid = ShouldLoadVoid(generation, BSAssetBundle.SceneLoadGeneration);
            Debug.LogError("[UnityEventGuard] world '" + Clean(scene.IsValid() ? scene.name : null) + "' refused (" + why + "); "
                           + (loadVoid ? "unloading it to Void." : "a newer world's load replaces it."));
            Refuse(scene, escaped);
            if (!loadVoid || !Application.isPlaying) return; // worlds only load in Play mode; tests drive this directly
            try
            {
                if (BSAssetBundle.LoadVoid() == null)
                    Debug.LogWarning("[UnityEventGuard] no Void scene to unload to; the refused world stays deactivated.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// A bundle scene nobody asked for: its graphs and its DontDestroyOnLoad escapees' graphs dropped, emptied,
        /// deactivated, the escapees destroyed, and unloaded.
        /// </summary>
        internal static void RefuseUnexpected(Scene scene, LoadSceneMode mode, List<GameObject> escaped)
        {
            escaped = escaped ?? new List<GameObject>();
            Debug.LogError("[UnityEventGuard] bundle scene '" + Clean(scene.name) + "' loaded outside a world load (a world may " +
                           "only bring its own first scene); refusing it.");
#if GREENFIELD_PROJECT
            WorldActivation.Drop(scene, escaped);
#endif
            Refuse(scene, escaped);
            if (!Application.isPlaying) return;
            try
            {
                if (mode == LoadSceneMode.Additive) SceneManager.UnloadSceneAsync(scene);
                else if (pendingWorlds.Count == 0) BSAssetBundle.LoadVoid(); // it replaced the world; a pending world replaces it otherwise
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Takes a refused scene out of play: no longer world content, every UnityEvent in it emptied first (so the
        /// OnDisable/OnDestroy senders deactivation and unloading run have nothing to call), roots deactivated, and its
        /// DontDestroyOnLoad escapees destroyed (they would outlive any unload).
        /// </summary>
        static void Refuse(Scene scene, List<GameObject> escaped)
        {
            var roots = new List<GameObject>();
            if (scene.IsValid())
            {
                refusedScenes.Add(scene.handle);
                worldScenes.Add(scene.handle);
                try
                {
                    roots.AddRange(SceneRoots(scene, null, out _));
                }
                catch (Exception)
                {
                    // Not loaded: nothing of it is running.
                }
            }
            roots.AddRange(escaped);
            try
            {
                Scrub(roots);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            foreach (var root in roots) Deactivate(root, escaped.Contains(root));
        }

        /// <summary>Replaces every UnityEvent with persistent calls under <paramref name="roots"/> by an empty one.</summary>
        static void Scrub(IEnumerable<GameObject> roots)
        {
            var walk = new Walk(_ => true, new Report("refused content"), true);
            foreach (var root in roots) walk.Root(root);
            walk.Finish();
        }

        static void Deactivate(GameObject root, bool destroy)
        {
            try
            {
                if (root == null) return;
                root.SetActive(false); // no Start or Update runs while the unload is pending
                if (destroy) DestroyNow(root);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void DestroyNow(GameObject gameObject)
        {
            if (Application.isPlaying) UnityObject.Destroy(gameObject);
            else UnityObject.DestroyImmediate(gameObject);
        }

        internal static Report SanitizeScene(Scene scene, ICollection<GameObject> escapedRoots = null, GameObject[] all = null)
        {
            loggedThisWorld.Clear(); // a new world: report its findings afresh
            var report = new Report("world '" + Clean(scene.name) + "'");
            if (!scene.IsValid())
            {
                report.Fail("the scene is not valid");
                report.Log();
                return report;
            }
            // GetRootGameObjects throws for a scene that is not loaded; PendingWorld.Judge fails that closed.
            var walk = new Walk(go => go.scene == scene || !go.scene.IsValid() || BSWorldScope.Contains(go)
                                      || (escapedRoots != null && escapedRoots.Contains(go.transform.root.gameObject)), report);
            var roots = SceneRoots(scene, all, out var unlisted);
            if (unlisted > 0)
            {
                report.UnlistedRoots = unlisted;
                report.Add("root(s) Scene.GetRootGameObjects left out, judged anyway: " + unlisted);
            }
            foreach (var root in roots) walk.Root(root);
            if (escapedRoots != null)
            {
                foreach (var root in escapedRoots) walk.Root(root);
            }
            walk.Finish();
            SweepNewClips(report); // clips only a graph, a spawned copy or a Timeline track would reach
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            walk.LogProfile(scene.name);
#endif
            report.Log();
            return report;
        }

        // ------------------------------------------------------------------ kit items

        sealed class PrefabVerdict
        {
            internal bool Passed;
            internal int Rules;
            internal string Why;
        }

        /// <summary>Judged kit prefab assets. An unloaded and reloaded asset is a new object, so it is judged again.</summary>
        static readonly ConditionalWeakTable<GameObject, PrefabVerdict> judgedPrefabs = new ConditionalWeakTable<GameObject, PrefabVerdict>();

        static int Rules => (EnforceAllowList ? 1 : 0) | (EnforceHostComponentRemoval ? 2 : 0);

        static bool IsKitContent(GameObject go) => !go.scene.IsValid() || BSWorldScope.Contains(go);

        /// <summary>Judges a kit prefab asset before it is instantiated; throws when it fails, so the item is refused.</summary>
        internal static void SanitizePrefab(GameObject prefab, string label)
        {
            if (prefab == null) return;
            var rules = Rules;
            if (!judgedPrefabs.TryGetValue(prefab, out var verdict) || verdict.Rules != rules)
            {
                var report = Sanitize(prefab, IsKitContent, "kit item '" + Clean(label) + "'");
                var clips = new Report("kit item '" + Clean(label) + "' clips");
                SweepNewClips(clips); // once per newly judged prefab: clips its spawned copies' legacy Animation could play
                clips.Log();
                verdict = new PrefabVerdict { Passed = !report.Failed, Rules = rules, Why = report.FailureReason };
                judgedPrefabs.Remove(prefab);
                judgedPrefabs.Add(prefab, verdict);
            }
            if (!verdict.Passed) throw new InvalidOperationException("Kit item '" + Clean(label) + "' failed its content check: " + verdict.Why);
        }

        /// <summary>
        /// Whether a page graph may be handed this asset from the world's kit bundles: prefabs (and components' prefabs)
        /// pass <see cref="SanitizePrefab"/>; ScriptableObjects (macros included) pass the walk, and macros the graph gate;
        /// clips and controllers only when every AnimationEvent goes to a graph machine's TriggerAnimationEvent or
        /// TriggerUnityEvent (other receivers are unknown here); plain data passes through: meshes, textures (and render
        /// textures), materials, shaders, sprites, fonts, audio, video and text assets, physics materials, avatar masks.
        /// Every other kind is refused, as is anything that fails or cannot be judged.
        /// </summary>
        internal static bool JudgeKitAsset(UnityObject asset, string label)
        {
            if (asset == null) return false;
            try
            {
                switch (asset)
                {
                    case GameObject prefab:
                        SanitizePrefab(prefab, label);
                        return true;
                    case Component component:
                        SanitizePrefab(component.transform.root.gameObject, label);
                        return true;
                    case AnimationClip clip:
                        return OnlyGraphSinkEvents(clip);
                    case RuntimeAnimatorController controller:
                        foreach (var controllerClip in controller.animationClips)
                        {
                            if (controllerClip != null && !OnlyGraphSinkEvents(controllerClip)) return false;
                        }
                        return true;
                    case ScriptableObject scriptableObject:
                        var report = new Report("kit asset '" + Clean(label) + "'");
                        var walk = new Walk(IsKitContent, report);
                        walk.AssetRoot(scriptableObject);
                        walk.Finish();
                        report.Log();
                        if (report.Failed) return false;
#if GREENFIELD_PROJECT
                        if (scriptableObject is IMacro macro && BanterStubsAllowed.IsBlockedStrictDeep(macro.graph)) return false;
#endif
                        return true;
                    default:
                        return IsPlainData(asset);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UnityEventGuard] kit asset '" + Clean(label) + "' refused: " + Printable(e.Message));
                return false;
            }
        }

        static bool OnlyGraphSinkEvents(AnimationClip clip)
        {
            var events = clip.events;
            if (events == null) return true;
            foreach (var animationEvent in events)
            {
                if (animationEvent.functionName != "TriggerAnimationEvent" && animationEvent.functionName != "TriggerUnityEvent") return false;
            }
            return true;
        }

        /// <summary>Kinds that hold data and nothing that runs or points at what runs.</summary>
        static bool IsPlainData(UnityObject asset) =>
            asset is Mesh || asset is Texture || asset is Material || asset is Shader || asset is Sprite || asset is Font
            || asset is AudioClip || asset is TextAsset || asset is UnityEngine.Video.VideoClip || asset is PhysicsMaterial
            || asset is PhysicsMaterial2D || asset is AvatarMask;

        // ---- AnimationClips nothing walked: reached by a graph's Animation.Play/GetClip, a spawned copy, a Timeline track

        /// <summary>Clips seen by a sweep or judged by a walk (instance ids); a sweep judges only what is new.</summary>
        static readonly HashSet<int> judgedClips = new HashSet<int>();

        static AnimationClip[] LoadedClips() => Application.isPlaying ? Resources.FindObjectsOfTypeAll<AnimationClip>() : Array.Empty<AnimationClip>();

        /// <summary>Marks every clip loaded now as known (before a world load starts: they are not its).</summary>
        internal static void RememberClips()
        {
            foreach (var clip in LoadedClips()) judgedClips.Add(clip.GetInstanceID());
        }

        /// <summary>
        /// Judges the AnimationEvents of every clip loaded since the last sweep, without knowing their receivers: an
        /// event naming a by-name dispatcher, the hideFlags setter, a content sink (exempt only on a known receiver), or a
        /// method that is non-public or static on some MonoBehaviour in the player is dropped; any other name can only reach
        /// a public method. Once per load. The client's own clips carry no AnimationEvents (only a Slash DataBind example
        /// clip, not in the build).
        /// </summary>
        internal static void SweepNewClips(Report report, IEnumerable<AnimationClip> clips = null)
        {
            var changed = new HashSet<AnimationClip>();
            foreach (var clip in clips ?? LoadedClips())
            {
                if (clip == null || !judgedClips.Add(clip.GetInstanceID())) continue;
                var events = clip.events;
                if (events == null || events.Length == 0) continue;
                List<AnimationEvent> kept = null;
                for (var i = 0; i < events.Length; i++)
                {
                    var name = events[i].functionName;
                    var why = SweepVerdict(name);
                    if (why != null)
                    {
                        if (kept == null)
                        {
                            kept = new List<AnimationEvent>(events.Length);
                            for (var j = 0; j < i; j++) kept.Add(events[j]);
                        }
                        report.AnimationEventsDropped++;
                        report.Add("animation event dropped (receiver unknown; " + why + "): " + Clean(name));
                    }
                    else
                    {
                        kept?.Add(events[i]);
                    }
                }
                if (kept != null)
                {
                    if (SetEvents(clip, kept.ToArray())) changed.Add(clip);
                    else report.Fail("could not drop animation events from clip '" + Clean(clip.name) + "' (a project asset, in edit mode)");
                }
            }
            if (changed.Count > 0 && Application.isPlaying) RefreshLegacyAnimations(changed); // wherever they already play
        }

        /// <summary>
        /// Every live legacy Animation with a state on one of <paramref name="changed"/> is refreshed (<see cref="RefreshLegacy"/>).
        /// Returns how many states.
        /// </summary>
        internal static int RefreshLegacyAnimations(ICollection<AnimationClip> changed)
        {
            var count = 0;
            foreach (var animation in Resources.FindObjectsOfTypeAll<Animation>())
            {
                if (animation != null && animation.gameObject.scene.IsValid()) count += RefreshLegacy(animation, changed);
            }
            return count;
        }

        /// <summary>
        /// A legacy Animation that is already playing keeps firing a clip's old AnimationEvents after clip.events changes; an
        /// Animator picks the change up (probe Q11). Each of its states on a changed clip is removed and added again, with
        /// its time, speed, weight, layer, blend and wrap modes and whether it was playing put back, and the default clip
        /// kept. Scene objects only. Returns how many states.
        /// </summary>
        internal static int RefreshLegacy(Animation animation, ICollection<AnimationClip> changed)
        {
            if (animation == null || changed == null || changed.Count == 0 || !animation.gameObject.scene.IsValid()) return 0;
            var states = new List<LegacyState>();
            try
            {
                foreach (AnimationState state in animation)
                {
                    if (state != null && state.clip != null && changed.Contains(state.clip)) states.Add(new LegacyState(state));
                }
            }
            catch (Exception)
            {
                return 0; // no states to read
            }
            if (states.Count == 0) return 0;
            var defaultClip = animation.clip;
            foreach (var saved in states)
            {
                try
                {
                    animation.RemoveClip(saved.Name);
                    animation.AddClip(saved.Clip, saved.Name);
                    var state = animation[saved.Name];
                    if (state != null) saved.Restore(state);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, animation);
                }
            }
            if (animation.clip == null && defaultClip != null) animation.clip = defaultClip;
            return states.Count;
        }

        readonly struct LegacyState
        {
            internal readonly string Name;
            internal readonly AnimationClip Clip;
            readonly float time, speed, weight;
            readonly bool enabled;
            readonly WrapMode wrapMode;
            readonly int layer;
            readonly AnimationBlendMode blendMode;

            internal LegacyState(AnimationState state)
            {
                Name = state.name;
                Clip = state.clip;
                time = state.time;
                speed = state.speed;
                weight = state.weight;
                enabled = state.enabled;
                wrapMode = state.wrapMode;
                layer = state.layer;
                blendMode = state.blendMode;
            }

            internal void Restore(AnimationState state)
            {
                state.wrapMode = wrapMode;
                state.speed = speed;
                state.layer = layer;
                state.blendMode = blendMode;
                state.time = time;
                state.weight = weight;
                state.enabled = enabled;
            }
        }

        /// <summary>Why a swept AnimationEvent (receivers unknown) is dropped; null when it can only reach a public method.</summary>
        static string SweepVerdict(string name)
        {
            if (string.IsNullOrEmpty(name)) return null; // calls nothing
            if (byNameDispatch.Contains(name)) return "calls by name";
            if (name == HideFlagsSetterName) return "sets hideFlags";
            if (animationEventSinks.Contains(name)) return "a content sink with no known receiver";
            if (HiddenNames.ContainsKey(name)) return "non-public or static on a MonoBehaviour in the player";
            return null;
        }

        /// <summary>
        /// Replaces a clip's AnimationEvents in place. In Play mode and in a player (where every caller judges bundle
        /// content) the runtime setter changes the loaded clip in memory; nothing is written to disk. In edit mode (editor
        /// tests) Unity refuses that setter ("Please use Editor.AnimationUtility..."), so the editor API is used, and never
        /// on a project asset: false then, and the caller fails the content closed.
        /// </summary>
        static bool SetEvents(AnimationClip clip, AnimationEvent[] events)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (UnityEditor.AssetDatabase.Contains(clip)) return false;
                UnityEditor.AnimationUtility.SetAnimationEvents(clip, events);
                return true;
            }
#endif
            clip.events = events;
            return true;
        }

        /// <summary>Whether this kit prefab asset passed its judgement under the rules now in force (a page graph may name it).</summary>
        internal static bool IsJudgedKitPrefab(GameObject prefab) =>
            prefab != null && judgedPrefabs.TryGetValue(prefab, out var verdict) && verdict.Passed && verdict.Rules == Rules;

        /// <summary>
        /// Judges a kit item's copy right after Instantiate (legacy Animation states only exist on live objects).
        /// Deactivates and destroys the copy and throws when it fails.
        /// </summary>
        internal static void SanitizeInstance(GameObject instance, string label)
        {
            if (instance == null) return;
            var report = Sanitize(instance, IsKitContent, "kit item '" + Clean(label) + "'");
            if (!report.Failed) return;
            instance.SetActive(false); // nothing of it runs again before the destroy lands
            DestroyNow(instance);
            throw new InvalidOperationException("Kit item '" + Clean(label) + "' failed its content check: " + report.FailureReason);
        }

        /// <summary>
        /// Judges <paramref name="root"/>'s hierarchy, inactive objects included, and the assets it points at.
        /// <paramref name="inScope"/> says which GameObjects count as world content when they are event targets.
        /// </summary>
        internal static Report Sanitize(GameObject root, Func<GameObject, bool> inScope, string label)
        {
            var report = new Report(label);
            if (root == null) return report;
            var walk = new Walk(inScope, report);
            walk.Root(root);
            walk.Finish();
            report.Log();
            return report;
        }

        // ------------------------------------------------------------------ the walk

        /// <summary>What one pass found. The counts cover the pass; the log repeats nothing already logged this world.</summary>
        internal sealed class Report
        {
            public readonly string Label;
            public int Disabled;
            public int Reported;
            public int AnimationEventsDropped;
            public int HostComponents;
            public int Removed;
            /// <summary>World GameObjects and components whose Hide/DontSave bits were cleared.</summary>
            public int HideFlagsCleared;
            /// <summary>Scene roots found only through every loaded GameObject (Scene.GetRootGameObjects left them out).</summary>
            public int UnlistedRoots;
            public bool Failed;
            public string FailureReason;
            readonly Dictionary<string, int> lines = new Dictionary<string, int>(StringComparer.Ordinal);

            internal Report(string label) { Label = label; }

            internal void Add(string line) => lines[line] = lines.TryGetValue(line, out var count) ? count + 1 : 1;

            /// <summary>What this pass found, one line per finding (tests read it).</summary>
            internal IEnumerable<string> Lines => lines.Keys;

            internal void Fail(string why)
            {
                if (!Failed) FailureReason = why;
                Failed = true;
            }

            internal void Log()
            {
                var fresh = new List<string>();
                foreach (var pair in lines)
                {
                    if (loggedThisWorld.Add(pair.Key)) fresh.Add(pair.Value > 1 ? pair.Key + " x" + pair.Value : pair.Key);
                }
                if (fresh.Count == 0 && !Failed) return;
                fresh.Sort(StringComparer.Ordinal);
                var message = new StringBuilder("[UnityEventGuard] ").Append(Label).Append(": ");
                if (Failed) message.Append("FAILED (").Append(FailureReason).Append("); ");
                message.Append(Disabled).Append(" persistent call(s) disabled, ")
                    .Append(AnimationEventsDropped).Append(" animation event(s) dropped, ")
                    .Append(Reported).Append(" call(s) not on the allow-list (kept until it is enforced), ")
                    .Append(HostComponents).Append(" host component(s), ")
                    .Append(Removed).Append(" component(s) removed");
                if (HideFlagsCleared > 0) message.Append(", hideFlags cleared on ").Append(HideFlagsCleared);
                if (UnlistedRoots > 0) message.Append(", ").Append(UnlistedRoots).Append(" root(s) missing from the scene's root list");
                var shown = Math.Min(fresh.Count, MaxLogLines);
                for (var i = 0; i < shown; i++) message.Append("\n  ").Append(fresh[i]);
                if (fresh.Count > shown) message.Append("\n  ... ").Append(fresh.Count - shown).Append(" more");
                if (Failed) Debug.LogError(message.ToString());
                else if (Disabled > 0 || AnimationEventsDropped > 0 || HostComponents > 0 || Removed > 0 || UnlistedRoots > 0)
                    Debug.LogWarning(message.ToString());
                else Debug.Log(message.ToString());
            }
        }

        /// <summary>Where a serialized value lives, so a UnityEvent (or a struct holding one) can be replaced.</summary>
        readonly struct Slot
        {
            readonly object holder;
            readonly FieldInfo field;
            readonly IList list;
            readonly int index;
            readonly Action<object> setter;

            internal Slot(object holder, FieldInfo field)
            {
                this.holder = holder;
                this.field = field;
                list = null;
                index = 0;
                setter = null;
            }

            internal Slot(IList list, int index)
            {
                holder = null;
                field = null;
                this.list = list;
                this.index = index;
                setter = null;
            }

            internal Slot(Action<object> setter)
            {
                holder = null;
                field = null;
                list = null;
                index = 0;
                this.setter = setter;
            }

            internal bool CanSet => field != null || list != null || setter != null;

            internal void Set(object value)
            {
                if (field != null) field.SetValue(holder, value);
                else if (list != null) list[index] = value;
                else setter(value);
            }
        }

        readonly struct AssetTargetCall
        {
            internal readonly UnityEventBase Event;
            internal readonly int Index;
            internal readonly object Call;
            internal readonly string Where;

            internal AssetTargetCall(UnityEventBase unityEvent, int index, object call, string where)
            {
                Event = unityEvent;
                Index = index;
                Call = call;
                Where = where;
            }
        }

        readonly struct Removal
        {
            internal readonly Component Component;
            internal readonly string Name;
            internal readonly string Why;

            internal Removal(Component component, string name, string why)
            {
                Component = component;
                Name = name;
                Why = why;
            }
        }

        sealed class Walk
        {
            readonly Func<GameObject, bool> inScope;
            readonly Report report;
            /// <summary>Refused content: only empty its UnityEvents.</summary>
            readonly bool scrub;
            readonly bool enforce = EnforceAllowList;
#if GREENFIELD_PROJECT
            readonly bool removeHostComponents = EnforceHostComponentRemoval;
#endif
            readonly HashSet<int> visitedAssets = new HashSet<int>();
            readonly HashSet<object> visitedValues = new HashSet<object>(ReferenceComparer.Instance);
            readonly Queue<UnityObject> pending = new Queue<UnityObject>();
            readonly HashSet<Type> behaviourTypes = new HashSet<Type>();
            readonly Dictionary<AnimationClip, HashSet<Type>> clipReceivers = new Dictionary<AnimationClip, HashSet<Type>>();
            readonly HashSet<AnimationClip> clipsWithUnknownReceivers = new HashSet<AnimationClip>();
            readonly List<AssetTargetCall> assetTargetCalls = new List<AssetTargetCall>();
            readonly List<Removal> removals = new List<Removal>();
            /// <summary>Legacy Animations in the content: refreshed after their clips' events change (probe Q11).</summary>
            readonly List<Animation> legacyAnimations = new List<Animation>();
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            // Development players: where the scan's time goes ([LoadTiming] scan profile).
            readonly Dictionary<Type, long> profTypeTicks = new Dictionary<Type, long>();
            readonly Dictionary<Type, int> profTypeCount = new Dictionary<Type, int>();
            long profGraphTicks, profGraphMaxTicks, profDrainTicks, profCallsTicks, profClipsTicks, profRemoveTicks;
            int profGraphCalls, profGraphKept, profComponents, profDrained;
            string profGraphMaxName;

            static double ProfMs(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            internal void LogProfile(string sceneName)
            {
                var types = new List<KeyValuePair<Type, long>>(profTypeTicks);
                types.Sort((a, b) => b.Value.CompareTo(a.Value));
                var top = new StringBuilder();
                long hierarchy = 0;
                foreach (var pair in types) hierarchy += pair.Value;
                for (var i = 0; i < types.Count && i < 8; i++)
                {
                    if (i > 0) top.Append(", ");
                    top.Append(types[i].Key.Name).Append(' ').Append(TimingMs(ProfMs(types[i].Value))).Append(" ms (")
                       .Append(profTypeCount[types[i].Key]).Append(')');
                }
                Debug.Log("[LoadTiming] scan profile of '" + Clean(sceneName) + "': components " + TimingMs(ProfMs(hierarchy)) + " ms over "
                          + profComponents + " (graph serialization inside: " + TimingMs(ProfMs(profGraphTicks)) + " ms in " + profGraphCalls
                          + " calls, " + profGraphKept + " from kept lists, largest " + TimingMs(ProfMs(profGraphMaxTicks)) + " ms on " + (profGraphMaxName ?? "-")
                          + "); queued assets/prefabs " + TimingMs(ProfMs(profDrainTicks)) + " ms (" + profDrained + "); asset-target calls "
                          + TimingMs(ProfMs(profCallsTicks)) + " ms; clips " + TimingMs(ProfMs(profClipsTicks)) + " ms; removals "
                          + TimingMs(ProfMs(profRemoveTicks)) + " ms. Slowest types (incl. their graphs): " + top);
            }
#endif

            internal Walk(Func<GameObject, bool> inScope, Report report, bool scrub = false)
            {
                this.inScope = go => go != null && inScope(go);
                this.report = report;
                this.scrub = scrub;
            }

            /// <summary>A lone asset (ScriptableObject, macro, clip, controller) to judge with what it points at.</summary>
            internal void AssetRoot(UnityObject asset) => Enqueue(asset);

            internal void Root(GameObject root)
            {
                if (root == null) return;
                visitedAssets.Add(root.GetInstanceID());
                Hierarchy(root);
            }

            internal void Finish()
            {
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                var f0 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                Drain();
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                var f1 = System.Diagnostics.Stopwatch.GetTimestamp();
                profDrainTicks += f1 - f0;
#endif
                if (scrub) return;
                // An asset target passes only when the content itself reaches that asset; built-in shared resources do not.
                foreach (var call in assetTargetCalls)
                {
                    try
                    {
                        var target = call.Event.GetPersistentTarget(call.Index);
                        var methodName = call.Event.GetPersistentMethodName(call.Index);
                        if (string.IsNullOrEmpty(methodName)) continue;
                        if (target != null && visitedAssets.Contains(target.GetInstanceID())) Decide(call.Event, call.Index, call.Call, target, methodName, call.Where);
                        else Disable(call.Event, call.Index, call.Call, Reason.AssetTarget, Clean(target != null ? target.GetType().FullName : null) + "." + Clean(methodName), call.Where);
                    }
                    catch (Exception e)
                    {
                        report.Fail("could not judge a persistent call: " + Printable(e.Message));
                    }
                }
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                var f2 = System.Diagnostics.Stopwatch.GetTimestamp();
                profCallsTicks += f2 - f1;
#endif
                Drain();
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                var f3 = System.Diagnostics.Stopwatch.GetTimestamp();
                profDrainTicks += f3 - f2;
#endif
                try
                {
                    Clips();
                }
                catch (Exception e)
                {
                    report.Fail("could not judge animation events: " + Printable(e.Message));
                }
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                var f4 = System.Diagnostics.Stopwatch.GetTimestamp();
                profClipsTicks += f4 - f3;
#endif
                // Second phase: everything is judged, so a removed component's OnDisable/OnDestroy can only fire judged calls.
                foreach (var removal in removals) Remove(removal);
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                profRemoveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - f4;
#endif
            }

            void Remove(Removal removal)
            {
                var component = removal.Component;
                if (component == null) return; // went with an earlier removal
                var asset = !component.gameObject.scene.IsValid();
                try
                {
                    // [RequireComponent] dependents (an XR Origin rig's) go first, or Unity refuses the removal.
                    RemoveDependents(component, new HashSet<Component> { component }, asset);
                    UnityObject.DestroyImmediate(component, asset);
                }
                catch (Exception e)
                {
                    report.Fail("could not remove " + removal.Name + ": " + Printable(e.Message));
                    return;
                }
                if (component != null)
                {
                    report.Fail("could not remove " + removal.Name); // e.g. another component requires it
                    return;
                }
                report.Removed++;
                report.Add("component removed (" + removal.Why + "): " + removal.Name);
            }

            /// <summary>As BSAssetBundle removes a world's EventSystem: whatever [RequireComponent]s the target, recursively.</summary>
            void RemoveDependents(Component target, HashSet<Component> visiting, bool asset)
            {
                foreach (var component in target.GetComponents<Component>())
                {
                    if (component == null || visiting.Contains(component) || !Requires(component.GetType(), target.GetType())) continue;
                    visiting.Add(component);
                    RemoveDependents(component, visiting, asset);
                    var name = Describe(component.GetType());
                    UnityObject.DestroyImmediate(component, asset);
                    report.Removed++;
                    report.Add("component removed (requires a removed host component): " + name);
                }
            }

            static bool Requires(Type dependent, Type required)
            {
                foreach (RequireComponent attribute in dependent.GetCustomAttributes(typeof(RequireComponent), true))
                {
                    if ((attribute.m_Type0 != null && attribute.m_Type0.IsAssignableFrom(required))
                        || (attribute.m_Type1 != null && attribute.m_Type1.IsAssignableFrom(required))
                        || (attribute.m_Type2 != null && attribute.m_Type2.IsAssignableFrom(required)))
                        return true;
                }
                return false;
            }

            void Drain()
            {
                while (pending.Count > 0)
                {
                    var next = pending.Dequeue();
                    if (next == null) continue;
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                    profDrained++;
#endif
                    if (next is GameObject prefab) Hierarchy(prefab);
                    else Asset(next);
                }
            }

            void Asset(UnityObject asset)
            {
                try
                {
                    Fields(asset, asset.GetType(), asset, 0);
                    if (!scrub && asset is Unity.VisualScripting.LudiqScriptableObject) GraphContent(asset);
                }
                catch (Exception e)
                {
                    report.Fail("could not judge a " + Clean(asset.GetType().FullName) + ": " + Printable(e.Message));
                }
            }

            void Hierarchy(GameObject root)
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue; // missing script
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                    var c0 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    try
                    {
                        if (component is Transform) ClearHideFlags(component.gameObject);
                        ClearHideFlags(component);
                        Inspect(component);
                    }
                    catch (Exception e)
                    {
                        // Fail closed: the rest of this component went unjudged.
                        report.Fail("could not judge a " + Clean(component.GetType().FullName) + ": " + Printable(e.Message));
                    }
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                    var type = component.GetType();
                    profTypeTicks.TryGetValue(type, out var ticks);
                    profTypeTicks[type] = ticks + System.Diagnostics.Stopwatch.GetTimestamp() - c0;
                    profTypeCount.TryGetValue(type, out var count);
                    profTypeCount[type] = count + 1;
                    profComponents++;
#endif
                }
            }

            /// <summary>
            /// Hide and DontSave bits come from data (m_ObjectHideFlags) or content code (TMP's sub-text objects and carets,
            /// Timeline's control-track instances): cleared, so no world object hides from the hierarchy or outlives its
            /// scene that way. Counted by type, so the log shows what carries them.
            /// </summary>
            void ClearHideFlags(Component component)
            {
                var flags = component.hideFlags;
                if ((flags & HidingOrPersisting) == 0) return;
                component.hideFlags = flags & ~HidingOrPersisting;
                Cleared(flags, Clean(component.GetType().FullName));
            }

            /// <summary>
            /// Unity applies a GameObject's flags to all its components, so each component's own other bits (NotEditable,
            /// HideInInspector) are put back afterwards, minus the same hiding and persisting bits.
            /// </summary>
            void ClearHideFlags(GameObject gameObject)
            {
                var flags = gameObject.hideFlags;
                if ((flags & HidingOrPersisting) == 0) return;
                var components = gameObject.GetComponents<Component>();
                var before = new HideFlags[components.Length];
                for (var i = 0; i < components.Length; i++) before[i] = components[i] != null ? components[i].hideFlags : HideFlags.None;
                gameObject.hideFlags = flags & ~HidingOrPersisting;
                Cleared(flags, "GameObject");
                for (var i = 0; i < components.Length; i++)
                {
                    var component = components[i];
                    if (component == null) continue;
                    var keep = before[i] & ~HidingOrPersisting;
                    if (component.hideFlags != keep) component.hideFlags = keep;
                    if ((before[i] & HidingOrPersisting) != 0) Cleared(before[i], Clean(component.GetType().FullName));
                }
            }

            void Cleared(HideFlags flags, string what)
            {
                report.HideFlagsCleared++;
                report.Add("hideFlags cleared (" + (flags & HidingOrPersisting) + "): " + what);
            }

            void Inspect(Component component)
            {
                var type = component.GetType();
                if (scrub)
                {
                    Fields(component, type, component, 0);
                    return;
                }
#if GREENFIELD_PROJECT
                if (IsDeniedHostComponent(type))
                {
                    report.HostComponents++;
                    removals.Add(new Removal(component, Describe(type), "host service"));
                }
                else if (IsHostComponentType(type))
                {
                    report.HostComponents++;
                    if (removeHostComponents) removals.Add(new Removal(component, Describe(type), "host component"));
                    else report.Add("host component (would remove): " + Describe(type));
                }
#endif
                if (component is MonoBehaviour) behaviourTypes.Add(type);
                switch (component)
                {
                    case Animator animator:
                        AddClips(animator.runtimeAnimatorController, animator.gameObject, 0);
                        break;
                    case Animation animation:
                        legacyAnimations.Add(animation);
                        AddClip(animation.clip, animation.gameObject);
                        // On a prefab asset the states may not exist yet; the clip sweep after a load covers what this misses.
                        try
                        {
                            foreach (AnimationState state in animation) AddClip(state.clip, animation.gameObject);
                        }
                        catch (Exception)
                        {
                            // Not enumerable on an asset.
                        }
                        break;
                    case PlayableDirector director:
                        Enqueue(director.playableAsset);
                        break;
                    case UnityEngine.InputSystem.PlayerInput playerInput:
                        PlayerInputMessages(playerInput);
                        break;
                }
                Fields(component, type, component, 0);
                if (component is Unity.VisualScripting.LudiqBehaviour) GraphContent(component);
            }

            /// <summary>
            /// Machines, variables and macros clear their Unity-serialized data once deserialized, so what a graph points
            /// at (prefabs it instantiates, clips, other macros) is only visible by serializing it again. Variable values
            /// come from Visual Scripting's own JSON, which can carry a UnityEvent with persistent calls: those are judged
            /// (and scrubbed) in place like any other.
            /// </summary>
            void GraphContent(UnityObject graphOwner)
            {
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                var g0 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                // What the graph points at: the list Unity deserialized it from, which Visual Scripting keeps for this (taken
                // once; every Unity object the graph holds came from it), or else the graph serialized again, the slow way.
                var references = TakeLoadedReferences(graphOwner);
                var kept = references != null;
                if (!kept) references = Unity.VisualScripting.Serialization.Serialize(graphOwner, true).objectReferences;
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                var g = System.Diagnostics.Stopwatch.GetTimestamp() - g0;
                profGraphTicks += g;
                profGraphCalls++;
                if (kept) profGraphKept++;
                if (g > profGraphMaxTicks)
                {
                    profGraphMaxTicks = g;
                    profGraphMaxName = graphOwner.GetType().Name + " '" + Clean(graphOwner.name) + "' (" + (kept ? "kept list" : "serialized") + ")";
                }
#endif
                if (references != null)
                {
                    foreach (var reference in references) Enqueue(reference);
                }
                switch (graphOwner)
                {
                    case Unity.VisualScripting.Variables variables:
                        Declarations(variables.declarations, graphOwner);
                        break;
                    case Unity.VisualScripting.IMachine machine:
                        Declarations((machine.nest?.graph as Unity.VisualScripting.IGraphWithVariables)?.variables, graphOwner);
                        break;
                    case Unity.VisualScripting.IMacro macro:
                        Declarations((macro.graph as Unity.VisualScripting.IGraphWithVariables)?.variables, graphOwner);
                        break;
                }
            }

            /// <summary>
            /// The list a graph's owner was deserialized from (kept by Greenfield's Visual Scripting fork), handed over once;
            /// null otherwise. Players only: a creator project has stock Visual Scripting (no kept list), and in the editor
            /// graphs are edited in memory after they load, so it keeps serializing them.
            /// </summary>
            static UnityObject[] TakeLoadedReferences(UnityObject owner)
            {
#if GREENFIELD_PROJECT && !UNITY_EDITOR
                switch (owner)
                {
                    case Unity.VisualScripting.LudiqBehaviour behaviour:
                        return behaviour.TakeLoadedObjectReferences();
                    case Unity.VisualScripting.LudiqScriptableObject asset:
                        return asset.TakeLoadedObjectReferences();
                }
#endif
                return null;
            }

            void Declarations(Unity.VisualScripting.VariableDeclarations declarations, UnityObject owner)
            {
                if (declarations == null) return;
                foreach (var declaration in declarations)
                {
                    var held = declaration;
                    Value(held.value, new Slot(value => held.value = value), owner, 1);
                }
            }

            /// <summary>PlayerInput in message mode sends "On" + each action name of the content's own action asset.</summary>
            void PlayerInputMessages(UnityEngine.InputSystem.PlayerInput playerInput)
            {
                var behaviour = playerInput.notificationBehavior;
                if (behaviour != UnityEngine.InputSystem.PlayerNotifications.SendMessages
                    && behaviour != UnityEngine.InputSystem.PlayerNotifications.BroadcastMessages) return;
                playerInput.notificationBehavior = UnityEngine.InputSystem.PlayerNotifications.InvokeCSharpEvents;
                report.Disabled++;
                report.Add("disabled (calls by name): UnityEngine.InputSystem.PlayerInput " + behaviour + ", now C# events only");
            }

            bool Fields(object holder, Type type, UnityObject owner, int depth)
            {
                var changed = false;
                foreach (var field in Plans.For(type)) changed |= Value(field.GetValue(holder), new Slot(holder, field), owner, depth + 1);
                return changed;
            }

            /// <summary>True when it replaced something inside a struct, which its own slot then has to receive back.</summary>
            bool Value(object value, Slot slot, UnityObject owner, int depth)
            {
                if (value == null) return false;
                if (depth > MaxDepth)
                {
                    report.Fail("serialized data nested deeper than " + MaxDepth + " levels");
                    return false;
                }
                switch (value)
                {
                    case UnityEventBase unityEvent:
                        return Event(unityEvent, slot, owner);
                    case UnityObject unityObject:
                        Enqueue(unityObject);
                        return false;
                    case string _:
                        return false;
                }
                var type = value.GetType();
                if (type.IsPrimitive || type.IsEnum) return false;
                if (!type.IsValueType && !visitedValues.Add(value)) return false; // [SerializeReference] graphs can cycle
                if (value is IList list)
                {
                    // Elements are replaced in place, and a list level does not count towards the depth limit.
                    for (var i = 0; i < list.Count; i++) Value(list[i], new Slot(list, i), owner, depth);
                    return false;
                }
                if (nameDispatchFields.TryGetValue(type.FullName ?? "", out var nameField)) ClearDispatchName(value, type, nameField, owner);
                if (!Fields(value, type, owner, depth) || !type.IsValueType) return false;
                if (!slot.CanSet)
                {
                    report.Fail("could not write back a changed " + Clean(type.FullName));
                    return false;
                }
                slot.Set(value);
                return true;
            }

            void ClearDispatchName(object value, Type type, string fieldName, UnityObject owner)
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null || field.FieldType != typeof(string) || string.IsNullOrEmpty((string)field.GetValue(value))) return;
                field.SetValue(value, string.Empty);
                report.Disabled++;
                report.Add("disabled (calls by name): " + Clean(type.FullName) + "." + fieldName + " (on " + Clean(owner.GetType().FullName) + ")");
            }

            /// <summary>An asset judged content points at: a prefab, a ScriptableObject, an animation source.</summary>
            void Enqueue(UnityObject target)
            {
                if (target == null) return; // Unity's null: missing and destroyed references too
                switch (target)
                {
                    case GameObject gameObject:
                        EnqueuePrefab(gameObject);
                        return;
                    case Component component:
                        EnqueuePrefab(component.gameObject);
                        return;
                    case ScriptableObject scriptableObject:
                        if (visitedAssets.Add(scriptableObject.GetInstanceID())) pending.Enqueue(scriptableObject);
                        return;
                    case AnimationClip clip:
                        AddClip(clip, null);
                        return;
                    case RuntimeAnimatorController controller:
                        AddClips(controller, null, 0);
                        return;
                }
            }

            void EnqueuePrefab(GameObject gameObject)
            {
                // Scene objects are judged through their scene's roots; another scene's (the host's) are never touched.
                if (gameObject.scene.IsValid()) return;
                var root = gameObject.transform.root.gameObject;
                if (visitedAssets.Add(root.GetInstanceID())) pending.Enqueue(root);
            }

            /// <summary>True when it replaced the event (degraded mode, or refused content).</summary>
            bool Event(UnityEventBase unityEvent, Slot slot, UnityObject owner)
            {
                var count = unityEvent.GetPersistentEventCount();
                if (count == 0) return false;
                var where = " (on " + Clean(owner.GetType().FullName) + ")";
                if (scrub || !Internals.Ready())
                {
                    // Refused content, or no internals to clear names with: no persistent call survives.
                    if (slot.CanSet)
                    {
                        slot.Set(Activator.CreateInstance(unityEvent.GetType(), true));
                        report.Disabled += count;
                        report.Add("disabled (" + (scrub ? "refused content" : "UnityEvent internals unavailable") + "): every persistent call" + where);
                        return true;
                    }
                    if (!scrub)
                    {
                        report.Fail("could not replace a UnityEvent" + where);
                        return false;
                    }
                    for (var i = 0; i < count; i++)
                    {
                        if (Internals.Ready()) Internals.Clear(Internals.Call(unityEvent, i));
                        unityEvent.SetPersistentListenerState(i, UnityEventCallState.Off);
                    }
                    return false;
                }
                for (var i = 0; i < count; i++)
                {
                    var methodName = unityEvent.GetPersistentMethodName(i);
                    if (string.IsNullOrEmpty(methodName)) continue; // never valid, or cleared by an earlier pass
                    var call = Internals.Call(unityEvent, i);
                    if (call == null)
                    {
                        report.Fail("could not read a persistent call" + where);
                        continue;
                    }
                    Enqueue(Internals.ObjectArgument(call)); // e.g. the prefab an ObjectSpawner spawns
                    var target = unityEvent.GetPersistentTarget(i);
                    if (target != null && !(target is GameObject) && !(target is Component))
                    {
                        if (target is ScriptableObject) assetTargetCalls.Add(new AssetTargetCall(unityEvent, i, call, where));
                        else Disable(unityEvent, i, call, Reason.AssetTarget, Clean(target.GetType().FullName) + "." + Clean(methodName), where);
                        continue;
                    }
                    Decide(unityEvent, i, call, target, methodName, where);
                }
                return false;
            }

            void Decide(UnityEventBase unityEvent, int index, object call, UnityObject target, string methodName, string where)
            {
                var reason = Judge(unityEvent, call, target, methodName, out var member);
                if (reason == null || (reason == Reason.NotOnAllowList && !enforce))
                {
                    if (reason != null)
                    {
                        report.Reported++;
                        report.Add("not on the allow-list, kept: " + member + where);
                    }
                    // A kept call resolves on its live target's type. Once that target is destroyed, Unity would take the
                    // bundle's type-name string instead (any type, statics included); without one the call is just invalid.
                    Internals.ClearTypeName(call);
                    return;
                }
                Disable(unityEvent, index, call, reason.Value, member, where);
            }

            void Disable(UnityEventBase unityEvent, int index, object call, Reason reason, string member, string where)
            {
                Internals.Clear(call); // method and type name: a later SetPersistentListenerState cannot revive it
                unityEvent.SetPersistentListenerState(index, UnityEventCallState.Off);
                report.Disabled++;
                report.Add("disabled (" + Describe(reason) + "): " + member + where);
            }

            /// <summary>Null when the call may stay; otherwise why not.</summary>
            Reason? Judge(UnityEventBase unityEvent, object call, UnityObject target, string methodName, out string member)
            {
                if (target == null)
                {
                    member = TypeOnly(Internals.TargetTypeName(call)) + "." + Clean(methodName);
                    return Reason.NoTarget;
                }
                var targetType = target.GetType();
                member = Clean(targetType.FullName) + "." + Clean(methodName);
                var targetObject = target as GameObject ?? (target as Component)?.gameObject;
                if (targetObject != null && !inScope(targetObject)) return Reason.OutsideWorld;

                var method = Internals.FindMethod(unityEvent, call);
                if (method == null) return Reason.Unresolved;
                member = Clean(method.DeclaringType?.FullName) + "." + Clean(method.Name);
                if (method.IsStatic) return Reason.Static;
                if (!method.IsPublic) return Reason.NonPublic;
                if (IsByNameDispatch(method)) return Reason.ByNameDispatch;
                if (IsHideFlagsSetter(method)) return Reason.HideFlagsSetter; // listed as hideFlags, refused under any rule
                if (IsReparenting(method)) return Reason.Reparents; // listed, but only graphs' re-parenting is reported
                if (IsListed(targetType, method)) return null;
                member = Clean(targetType.FullName) + "." + Clean(method.Name);
                return Reason.NotOnAllowList;
            }

            void AddClips(RuntimeAnimatorController controller, GameObject receiver, int depth)
            {
                if (controller == null || depth > 8) return;
                foreach (var clip in controller.animationClips) AddClip(clip, receiver);
                // An override's own list holds the clips in effect; the base's play wherever an override is missing or removed.
                if (controller is AnimatorOverrideController overrides) AddClips(overrides.runtimeAnimatorController, receiver, depth + 1);
            }

            void AddClip(AnimationClip clip, GameObject receiver)
            {
                if (clip == null) return;
                if (!clipReceivers.TryGetValue(clip, out var receivers)) clipReceivers[clip] = receivers = new HashSet<Type>();
                if (receiver == null)
                {
                    clipsWithUnknownReceivers.Add(clip); // a Timeline, graph or stray clip: any behaviour in the content could receive it
                    return;
                }
                foreach (var behaviour in receiver.GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null) receivers.Add(behaviour.GetType());
                }
            }

            void Clips()
            {
                var changed = new HashSet<AnimationClip>();
                foreach (var pair in clipReceivers)
                {
                    var clip = pair.Key;
                    var events = clip.events;
                    if (events == null || events.Length == 0) continue;
                    var receivers = pair.Value;
                    if (clipsWithUnknownReceivers.Contains(clip)) receivers.UnionWith(behaviourTypes);

                    List<AnimationEvent> kept = null;
                    for (var i = 0; i < events.Length; i++)
                    {
                        var reason = JudgeAnimationEvent(events[i].functionName, receivers, out var member);
                        var drop = reason != null && (reason != Reason.NotOnAllowList || enforce);
                        if (reason == Reason.NotOnAllowList && !enforce)
                        {
                            report.Reported++;
                            report.Add("animation event not on the allow-list, kept: " + member);
                        }
                        if (drop)
                        {
                            if (kept == null)
                            {
                                kept = new List<AnimationEvent>(events.Length);
                                for (var j = 0; j < i; j++) kept.Add(events[j]); // everything before the first drop stays
                            }
                            report.AnimationEventsDropped++;
                            report.Add("animation event dropped (" + Describe(reason.Value) + "): " + member);
                        }
                        else
                        {
                            kept?.Add(events[i]);
                        }
                    }
                    if (kept != null)
                    {
                        if (SetEvents(clip, kept.ToArray())) changed.Add(clip);
                        else report.Fail("could not drop animation events from clip '" + Clean(clip.name) + "' (a project asset, in edit mode)");
                    }
                    judgedClips.Add(clip.GetInstanceID());
                }
                if (changed.Count == 0) return;
                foreach (var animation in legacyAnimations) RefreshLegacy(animation, changed);
            }
        }

        // ------------------------------------------------------------------ rules

        /// <summary>Null when every behaviour that has the function exposes it publicly and listed; otherwise why not.</summary>
        static Reason? JudgeAnimationEvent(string functionName, HashSet<Type> receivers, out string member)
        {
            member = "<no receiver>." + Clean(functionName);
            if (string.IsNullOrEmpty(functionName)) return Reason.NoReceiver;
            if (byNameDispatch.Contains(functionName))
            {
                member = Clean(functionName);
                return Reason.ByNameDispatch;
            }
            if (functionName == HideFlagsSetterName)
            {
                member = "UnityEngine.Object." + HideFlagsSetterName;
                return Reason.HideFlagsSetter;
            }
            if (HiddenNames.TryGetValue(functionName, out var hiddenOn)
                && !(IsExemptSink(functionName) && IsPublicOnReceiver(functionName, receivers)))
            {
                member = hiddenOn + "." + Clean(functionName);
                return Reason.HiddenName;
            }
            var found = false;
            string unlisted = null;
            foreach (var receiver in receivers)
            {
                for (var type = receiver; type != null && type != typeof(object); type = type.BaseType)
                {
                    foreach (var info in type.GetMember(functionName, MemberTypes.Method, DeclaredMembers))
                    {
                        var method = (MethodInfo)info;
                        member = Clean(type.FullName) + "." + Clean(method.Name);
                        if (method.IsStatic) return Reason.Static;
                        if (!method.IsPublic) return Reason.NonPublic;
                        found = true;
                        if (unlisted == null && !IsListed(receiver, method)) unlisted = Clean(receiver.FullName) + "." + Clean(method.Name);
                    }
                }
            }
            if (!found) return Reason.NoReceiver;
            if (unlisted != null)
            {
                member = unlisted;
                return Reason.NotOnAllowList;
            }
            return null;
        }

        /// <summary>
        /// A content sink, and static on no MonoBehaviour in the player: until it is shown that AnimationEvents cannot reach
        /// statics, a sink name some behaviour declares static is not exempt.
        /// </summary>
        static bool IsExemptSink(string functionName) => animationEventSinks.Contains(functionName) && !StaticNames.Contains(functionName);

        static bool IsPublicOnReceiver(string functionName, HashSet<Type> receivers)
        {
            foreach (var receiver in receivers)
            {
                foreach (var info in receiver.GetMember(functionName, MemberTypes.Method, BindingFlags.Public | BindingFlags.Instance))
                {
                    if (((MethodInfo)info).IsPublic) return true;
                }
            }
            return false;
        }

        static Dictionary<string, string> hiddenNames;
        static HashSet<string> staticNames;

        /// <summary>
        /// Each method name that is non-public or static on some MonoBehaviour type in the player (or on its Behaviour,
        /// Component and Object bases), with one type that has it. An AnimationEvent by such a name could reach that
        /// method once such a component is added. Editor and test assemblies are left out, so the editor and a build
        /// agree. Built once, on the first clip with events, together with <see cref="StaticNames"/>.
        /// </summary>
        static Dictionary<string, string> HiddenNames
        {
            get
            {
                if (hiddenNames != null) return hiddenNames;
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                var statics = new HashSet<string>(StringComparer.Ordinal);
                void Add(Type type)
                {
                    foreach (var method in type.GetMethods(DeclaredMembers))
                    {
                        if (method.IsStatic) statics.Add(method.Name);
                        if ((method.IsStatic || !method.IsPublic) && !names.ContainsKey(method.Name)) names[method.Name] = Clean(type.FullName);
                    }
                }
                for (var type = typeof(MonoBehaviour).BaseType; type != null; type = type.BaseType) Add(type);
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!IsPlayerAssembly(assembly)) continue;
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    foreach (var type in types)
                    {
                        try
                        {
                            if (type != null && typeof(MonoBehaviour).IsAssignableFrom(type)) Add(type);
                        }
                        catch (Exception)
                        {
                            // A type whose members cannot load cannot be added as a component either.
                        }
                    }
                }
                staticNames = statics;
                return hiddenNames = names;
            }
        }

        /// <summary>Each method name declared static on some MonoBehaviour type in the player, or on its bases.</summary>
        static HashSet<string> StaticNames
        {
            get
            {
                if (staticNames == null) _ = HiddenNames;
                return staticNames;
            }
        }

        /// <summary>
        /// Assemblies a player build has: not dynamic, not editor (by name), not a test assembly (one that references NUnit
        /// or the test runner) or the test frameworks themselves.
        /// </summary>
        internal static bool IsPlayerAssembly(Assembly assembly)
        {
            if (assembly.IsDynamic) return false;
            var name = assembly.GetName().Name ?? "";
            if (name.StartsWith("UnityEditor", StringComparison.Ordinal) || name.EndsWith("Editor", StringComparison.Ordinal)
                || name.Contains(".Editor") || IsTestFramework(name))
                return false;
            try
            {
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    if (IsTestFramework(reference.Name)) return false;
                }
            }
            catch (Exception)
            {
                // Unreadable references: treat it as part of the player.
            }
            return true;
        }

        static bool IsTestFramework(string name) =>
            name == "nunit.framework" || name == "UnityEngine.TestRunner" || name == "UnityEditor.TestRunner";

        /// <summary>
        /// Transform.SetParent, the parent setter and DetachChildren: refused to persistent calls under any rule (R7-2).
        /// Graphs use them (tracked by WorldActivation's lending hooks); none of 12,888 persistent calls in 454 live worlds does.
        /// </summary>
        static bool IsReparenting(MethodInfo method) =>
            (method.Name == "SetParent" || method.Name == "set_parent" || method.Name == "DetachChildren")
            && typeof(Transform).IsAssignableFrom(method.DeclaringType);

        static bool IsHideFlagsSetter(MethodInfo method) =>
            method.Name == HideFlagsSetterName && typeof(UnityObject).IsAssignableFrom(method.DeclaringType);

        static bool IsByNameDispatch(MethodInfo method) =>
            byNameDispatch.Contains(method.Name)
            && (method.DeclaringType == typeof(GameObject) || method.DeclaringType == typeof(Component)
                || method.DeclaringType == typeof(MonoBehaviour));

        /// <summary>
        /// Whether the Visual Scripting allow-list lists the member for the target's type or a base type up to the one that
        /// declares it (ids are "Type.FullName.Member"; a property setter set_X is listed as X). An override is judged
        /// from its declaring type down, so a member listed only under a base type fails closed. Graph machines'
        /// TriggerUnityEvent/TriggerAnimationEvent are the documented UnityEvent/AnimationEvent sinks and always pass.
        /// Outside the Greenfield project there is no allow-list, and only the always-disabled rules apply.
        /// </summary>
        static bool IsListed(Type targetType, MethodInfo method)
        {
#if GREENFIELD_PROJECT
            if (typeof(IEventMachine).IsAssignableFrom(targetType)
                && (method.Name == nameof(IEventMachine.TriggerUnityEvent) || method.Name == nameof(IEventMachine.TriggerAnimationEvent)))
                return true;
            var name = method.Name;
            if (method.IsSpecialName && (name.StartsWith("set_", StringComparison.Ordinal) || name.StartsWith("get_", StringComparison.Ordinal)))
                name = name.Substring(4);
            for (var type = targetType; type != null; type = type.BaseType)
            {
                if (BanterStubsAllowed.IsAllowedIdentifier(type.FullName + "." + name)) return true;
                if (type == method.DeclaringType) break;
            }
            return false;
#else
            return true;
#endif
        }

        /// <summary>
        /// Assemblies whose components bundle content may carry. A world scene binds each MonoBehaviour by class name to
        /// whatever type the player has, host-app services included; components from any other assembly are host
        /// components. Tightened from a scan of live worlds.
        /// </summary>
        static readonly HashSet<string> contentAssemblies = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine", "Unity.TextMeshPro", "Unity.Timeline", "Banter.VisualScripting", "BS.SDK",
            "Unity.RenderPipelines.Universal.Runtime", // UniversalAdditionalCameraData/LightData ride on every URP camera and light
            "Unity.RenderPipelines.Core.Runtime",      // Volume, probe volumes
            "Unity.AI.Navigation", "Unity.Splines", "Unity.Cinemachine", // world-only packages Assets/link.xml preserves for bundles
            "GarethIW.PicaVoxelInfinity",              // voxel worlds; the allow-list trusts its graph units
        };

        /// <summary>Engine modules and UnityEngine.UI; the Visual Scripting runtime; the SDK's own assemblies.</summary>
        static readonly string[] contentAssemblyPrefixes = { "UnityEngine.", "Unity.VisualScripting.", "BS.SDK." };

        /// <summary>Single types from otherwise host assemblies: the UI input pieces creator scenes carry (and the SDK adds).</summary>
        static readonly HashSet<string> contentTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine.InputSystem.UI.TrackedDeviceRaycaster", "UnityEngine.InputSystem.UI.InputSystemUIInputModule",
        };

        static readonly Dictionary<Assembly, bool> hostAssemblies = new Dictionary<Assembly, bool>();

        internal static bool IsHostComponentType(Type type)
        {
            if (contentTypes.Contains(type.FullName ?? "")) return false;
            var assembly = type.Assembly;
            if (hostAssemblies.TryGetValue(assembly, out var host)) return host;
            var name = assembly.GetName().Name ?? "";
            host = !contentAssemblies.Contains(name);
            foreach (var prefix in contentAssemblyPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal)) host = false;
            }
            return hostAssemblies[assembly] = host;
        }

        static readonly Dictionary<Type, bool> deniedTypes = new Dictionary<Type, bool>();

        /// <summary>A host service removed from bundle content now: its type or a base type is denied, or its whole assembly.</summary>
        internal static bool IsDeniedHostComponent(Type type)
        {
            if (deniedTypes.TryGetValue(type, out var denied)) return denied;
            denied = deniedHostAssemblies.Contains(type.Assembly.GetName().Name ?? "");
            for (var current = type; !denied && current != null && current != typeof(MonoBehaviour); current = current.BaseType)
            {
                denied = deniedHostTypes.Contains(current.FullName ?? "");
            }
            return deniedTypes[type] = denied;
        }

        /// <summary>Forget cached deny verdicts (tests change <see cref="deniedHostTypes"/>).</summary>
        internal static void ForgetDeniedTypes() => deniedTypes.Clear();

        static string Describe(Type type) => Clean(type.FullName) + " [" + Clean(type.Assembly.GetName().Name) + "]";

        static string Describe(Reason reason)
        {
            switch (reason)
            {
                case Reason.NoTarget: return "no target object";
                case Reason.OutsideWorld: return "target outside the world";
                case Reason.AssetTarget: return "asset target the content does not reach";
                case Reason.Static: return "static";
                case Reason.NonPublic: return "non-public";
                case Reason.HiddenName: return "non-public or static on a MonoBehaviour in the player";
                case Reason.ByNameDispatch: return "calls by name";
                case Reason.Unresolved: return "unresolved";
                case Reason.NoReceiver: return "no receiver";
                case Reason.HideFlagsSetter: return "sets hideFlags (hides an object or keeps it past its scene)";
                case Reason.Reparents: return "re-parents (only graphs' re-parenting is tracked)";
                default: return "not on the allow-list";
            }
        }

        /// <summary>"System.IO.File, mscorlib" -> "System.IO.File". The string comes from the bundle.</summary>
        static string TypeOnly(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return "<no target>";
            var comma = assemblyQualifiedName.IndexOf(',');
            return Clean(comma > 0 ? assemblyQualifiedName.Substring(0, comma) : assemblyQualifiedName);
        }

        /// <summary>Names from a bundle go into the log: printable identifier characters only, and short.</summary>
        static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "?";
            var text = new StringBuilder(Math.Min(value.Length, 120));
            foreach (var ch in value)
            {
                if (text.Length >= 120)
                {
                    text.Append("...");
                    break;
                }
                text.Append(char.IsLetterOrDigit(ch) || "._+`<>[],".IndexOf(ch) >= 0 ? ch : '?');
            }
            return text.ToString();
        }

        /// <summary>One line of free text for the log: no control characters, and short.</summary>
        static string Printable(string value)
        {
            if (string.IsNullOrEmpty(value)) return "?";
            var text = new StringBuilder(Math.Min(value.Length, 200));
            foreach (var ch in value)
            {
                if (text.Length >= 200)
                {
                    text.Append("...");
                    break;
                }
                text.Append(char.IsControl(ch) ? ' ' : ch);
            }
            return text.ToString();
        }

        /// <summary>
        /// The fields Unity serializes that can lead to a UnityEvent, a by-name dispatcher or an asset worth following,
        /// per type, cached. Field reflection only, so IL2CPP-safe.
        /// </summary>
        static class Plans
        {
            static readonly Dictionary<Type, FieldInfo[]> plans = new Dictionary<Type, FieldInfo[]>();
            static readonly Dictionary<Type, bool> mayHold = new Dictionary<Type, bool>();
            static readonly HashSet<Type> computing = new HashSet<Type>();

            internal static FieldInfo[] For(Type type)
            {
                if (plans.TryGetValue(type, out var plan)) return plan;
                var fields = new List<FieldInfo>();
                foreach (var field in SerializedFields(type))
                {
                    if (field.IsDefined(typeof(SerializeReference), false) || MayHold(field.FieldType)) fields.Add(field);
                }
                return plans[type] = fields.ToArray();
            }

            static IEnumerable<FieldInfo> SerializedFields(Type type)
            {
                for (var current = type; current != null && !IsFrameworkBase(current); current = current.BaseType)
                {
                    foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (field.IsNotSerialized || field.IsInitOnly) continue;
                        if (field.IsPublic || field.IsDefined(typeof(SerializeField), false) || field.IsDefined(typeof(SerializeReference), false))
                            yield return field;
                    }
                }
            }

            static bool IsFrameworkBase(Type type) =>
                type == typeof(object) || type == typeof(ValueType) || type == typeof(UnityObject) || type == typeof(Component)
                || type == typeof(Behaviour) || type == typeof(MonoBehaviour) || type == typeof(ScriptableObject);

            static bool MayHold(Type type)
            {
                if (mayHold.TryGetValue(type, out var known)) return known;
                // A type that (indirectly) contains itself is assumed to: following it costs a little, missing it would not.
                if (!computing.Add(type)) return true;
                try
                {
                    return mayHold[type] = Compute(type);
                }
                finally
                {
                    computing.Remove(type);
                }
            }

            static bool Compute(Type type)
            {
                if (type.IsPrimitive || type.IsEnum || type.IsPointer || type == typeof(string) || type == typeof(decimal)) return false;
                if (typeof(UnityEventBase).IsAssignableFrom(type) || nameDispatchFields.ContainsKey(type.FullName ?? "")) return true;
                if (typeof(UnityObject).IsAssignableFrom(type))
                {
                    return type == typeof(UnityObject) || type == typeof(GameObject) || typeof(Component).IsAssignableFrom(type)
                           || typeof(ScriptableObject).IsAssignableFrom(type) || typeof(AnimationClip).IsAssignableFrom(type)
                           || typeof(RuntimeAnimatorController).IsAssignableFrom(type);
                }
                if (type.IsArray) return type.GetArrayRank() == 1 && MayHold(type.GetElementType());
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return MayHold(type.GetGenericArguments()[0]);
                // Unity serializes no other collection, nor an interface/abstract/object field without [SerializeReference].
                if (type.IsInterface || type.IsAbstract || type == typeof(object) || !type.IsSerializable) return false;
                if (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal)) return false;
                foreach (var field in SerializedFields(type))
                {
                    if (field.IsDefined(typeof(SerializeReference), false) || MayHold(field.FieldType)) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Unity's own resolution, through the serialized PersistentCall (UnityEngine.CoreModule internals, which
        /// Assets/link.xml preserves whole). Without them every UnityEvent with persistent calls is replaced (fail closed).
        /// </summary>
        static class Internals
        {
            const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            static bool initialized;
            static FieldInfo persistentCalls, calls, methodName, targetTypeName, arguments, objectArgument;
            static MethodInfo findMethod;

            internal static bool Ready()
            {
                if (simulateMissingInternals) return false;
                if (initialized) return findMethod != null;
                initialized = true;
                try
                {
                    persistentCalls = typeof(UnityEventBase).GetField("m_PersistentCalls", Fields);
                    calls = persistentCalls?.FieldType.GetField("m_Calls", Fields);
                    var callType = calls?.FieldType.IsGenericType == true ? calls.FieldType.GetGenericArguments()[0] : null;
                    methodName = callType?.GetField("m_MethodName", Fields);
                    targetTypeName = callType?.GetField("m_TargetAssemblyTypeName", Fields);
                    arguments = callType?.GetField("m_Arguments", Fields);
                    objectArgument = arguments?.FieldType.GetField("m_ObjectArgument", Fields);
                    var find = callType == null ? null : typeof(UnityEventBase).GetMethod("FindMethod", Fields, null, new[] { callType }, null);
                    if (methodName != null && targetTypeName != null && objectArgument != null && find != null) findMethod = find;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[UnityEventGuard] " + e.Message);
                }
                if (findMethod == null)
                    Debug.LogError("[UnityEventGuard] UnityEvent internals not found; every persistent call in bundle content will be removed.");
                return findMethod != null;
            }

            internal static object Call(UnityEventBase unityEvent, int index)
            {
                var list = calls.GetValue(persistentCalls.GetValue(unityEvent)) as IList;
                return list != null && index < list.Count ? list[index] : null;
            }

            /// <summary>Exactly the method Unity would bind now; null when it would bind none.</summary>
            internal static MethodInfo FindMethod(UnityEventBase unityEvent, object call)
            {
                try
                {
                    return findMethod.Invoke(unityEvent, new[] { call }) as MethodInfo;
                }
                catch (Exception)
                {
                    return null; // Unity would fail the same way when the event fires; Unresolved disables it.
                }
            }

            internal static string TargetTypeName(object call) => targetTypeName.GetValue(call) as string;

            internal static UnityObject ObjectArgument(object call) => objectArgument.GetValue(arguments.GetValue(call)) as UnityObject;

            /// <summary>
            /// While the target lives, Unity rebuilds an empty type name from it; once it is gone, an empty name fails
            /// PersistentCall.IsValid instead of naming another type.
            /// </summary>
            internal static void ClearTypeName(object call) => targetTypeName.SetValue(call, string.Empty);

            /// <summary>An empty method name fails PersistentCall.IsValid, whatever the call state is set to later.</summary>
            internal static void Clear(object call)
            {
                if (call == null) return;
                methodName.SetValue(call, string.Empty);
                targetTypeName.SetValue(call, string.Empty);
            }
        }

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
