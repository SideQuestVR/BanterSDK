using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BS.Security
{
    /// <summary>
    /// The lists the pre-load bundle check judges scripts, calls and AnimationEvents against, and the versions its
    /// verdicts are cached under.
    /// </summary>
    /// <remarks>
    /// <para><b>One source.</b> The world-facing lists, the host deny lists, the by-name dispatchers and the AnimationEvent
    /// sinks are read from <c>BSUnityEventGuard</c> (its static sets) once, then used read-only from any thread. The
    /// checker adds its own extra denials (the rest of the host app and its plugins, and SDK internals no world places):
    /// a script that resolves to neither list is refused when it is a component, reported otherwise. The built-in copies
    /// below are only a fallback when the guard's fields cannot be read (a pin test keeps them in step), and the list
    /// version says which was used.</para>
    /// <para>The denied lists win over the world-facing ones. A denied type also denies its subclasses.</para>
    /// </remarks>
    internal static class BundleCheckPolicy
    {
        /// <summary>Bump when a rule changes what gets refused: every cached verdict is judged again.</summary>
        internal const int CheckerVersion = 2;

        // ------------------------------------------------------------------ built-in copies of the guard's lists (fallback)

        internal static readonly string[] BuiltinContentAssemblies =
        {
            "UnityEngine", "Unity.TextMeshPro", "Unity.Timeline", "Banter.VisualScripting", "BS.SDK",
            "Unity.RenderPipelines.Universal.Runtime", "Unity.RenderPipelines.Core.Runtime",
            "Unity.AI.Navigation", "Unity.Splines", "Unity.Cinemachine",
            "GarethIW.PicaVoxelInfinity",
        };

        internal static readonly string[] BuiltinContentAssemblyPrefixes = { "UnityEngine.", "Unity.VisualScripting.", "BS.SDK." };

        internal static readonly string[] BuiltinContentTypes =
        {
            "UnityEngine.InputSystem.UI.TrackedDeviceRaycaster", "UnityEngine.InputSystem.UI.InputSystemUIInputModule",
        };

        internal static readonly string[] BuiltinDeniedHostAssemblies =
        {
            "SideQuest.GreenfieldMcp", "Greenfield.Mcp", "com.whisper.unity", "SideQuest.LipSync", "Greenfield.LipSync",
            "SideQuest.Recording", "SideQuest.Recording.Banter", "Greenfield.Recording", "SideQuest.HandCamera",
            "Greenfield.HandCamera", "SideQuest.VirtualWebcam", "PacketParty", "SocketIOUnityAssembly", "Oculus.Platform",
            "com.rlabrecque.steamworks.net", "KS.Diagnostics.Process", "Slash.Unity.DataBind", "Greenfield.Databinding",
        };

        internal static readonly string[] BuiltinDeniedHostTypes =
        {
            "SideQuest.GreenfieldMcp.GreenfieldMcpServer", "SideQuest.GreenfieldMcp.McpMainThread",
            "uLipSync.uLipSyncMicrophone", "Whisper.Utils.MicrophoneRecord", "Whisper.WhisperManager",
            "SideQuest.LipSync.AudioSourceLipSyncFeed", "SideQuest.LipSync.LipSyncSystem",
            "UnityEngine.InputSystem.PlayerInput", "UnityEngine.InputSystem.PlayerInputManager",
            "UnityEngine.InputSystem.UI.VirtualMouseInput", "UnityEngine.InputSystem.OnScreen.OnScreenControl",
            "UnityEngine.InputSystem.EnhancedTouch.TouchSimulation",
            "SideQuest.VirtualWebcam.VirtualWebcamSource", "SideQuest.Ora.WebRTC.OraWebRTCPeer", "SideQuest.Ora.WebRTC.OraVideoSender",
            "KS.UnityToolbag.Dispatcher", "UnityThread", "Oculus.Platform.CallbackRunner",
            "Greenfield.App.PlatformIdentity.SteamManager", "GLTFast.DontDestroyOnLoad", "Unity.XR.CoreUtils.XROrigin",
            "BS.DontDestroyOnLoad", "UnityAsyncAwaitUtil.AsyncCoroutineRunner", "BS.BSAssetRegistry",
            "BS.VisualScripting.UI.Helpers.CoroutineRunner",
        };

        internal static readonly string[] BuiltinByNameDispatch =
        {
            "SendMessage", "SendMessageUpwards", "BroadcastMessage", "Invoke", "InvokeRepeating", "StartCoroutine",
            "StartCoroutine_Auto",
        };

        internal static readonly string[] BuiltinAnimationEventSinks =
        {
            "TriggerEvent", "SetText", "Play", "SetDirty", "AddCullingLayer", "SetCullingLayer", "LoadQuestHome",
        };

        // ------------------------------------------------------------------ the checker's own extra denials

        /// <summary>The rest of the host app and its plugins (exact assembly names). A world script with an app class's identity binds to it.</summary>
        internal static readonly string[] ExtraDeniedAssemblies =
        {
            "Assembly-CSharp", "Assembly-CSharp-firstpass",
            "I2.Loc", "MCPForUnity.Runtime", "glTFast", "com.gator-dragon-games.jigglephysics", "Sigtrap.VrTunnellingPro",
            "Unity.WebRTC", "KS.Diagnostics.Process.Example", "Virta", "VirtualCameraPublisher", "ShanesEditor",
            "Unity.Addressables", "Unity.ResourceManager", "UnityEngine.SpatialTracking", "UnityEngine.XR.LegacyInputHelpers",
        };

        /// <summary>Host app and plugin assembly families (prefixes), checked before the world-facing prefixes.</summary>
        internal static readonly string[] ExtraDeniedAssemblyPrefixes =
        {
            "Greenfield.", "SideQuest", "PacketParty.", "Basis", "Slash.", "uLipSync", "glTFast.", "Unity.XR.", "UnityEngine.XR.",
            "MCPForUnity", "I2.",
        };

        /// <summary>SDK internals no world places (the app's page link, loaders, the local rig and its helpers).</summary>
        internal static readonly string[] ExtraDeniedTypes =
        {
            "BS.Security.BundleLayoutProbe", "BS.BSStarterUpper", "BS.BSLink", "BS.Get", "BS.QuestHomeLoader", "BS.UI.Bridge.UIElementBridge",
            "BS.BSDesktopController", "BS.BSDesktopMouseHand", "BS.LoadingBarManager", "BS.FlexaHead",
            "UserData", "AvatarRef", "FlexaPose", "NameGenerator", "LegacyUserAttachmentHelper",
            "SideQuest.FlexaBody.PhysicsHand", "SideQuest.FlexaBody.GrabHand",
            "Siccity.GLTFUtility.CoroutineRunner",
        };

        /// <summary>
        /// ScriptableObject types outside the world-facing lists that worlds may still carry (the live corpus has these
        /// two, from the Input System). Every other unlisted ScriptableObject is refused: Unity runs its OnEnable and
        /// OnAfterDeserialize as the bundle loads, before any runtime hook.
        /// </summary>
        internal static readonly string[] AllowedScriptableObjects =
        {
            "UnityEngine.InputSystem.InputActionAsset", "UnityEngine.InputSystem.InputActionReference",
        };

        // ------------------------------------------------------------------ the effective lists

        /// <summary>One read-only snapshot of every list (safe to use from any thread).</summary>
        internal sealed class Lists
        {
            internal readonly HashSet<string> ContentAssemblies, ContentTypes, DeniedAssemblies, DeniedTypes, ByNameDispatch, AnimationEventSinks;
            internal readonly string[] ContentAssemblyPrefixes, DeniedAssemblyPrefixes;
            /// <summary>"guard" when the guard's own sets were read, else "built-in".</summary>
            internal readonly string Source;
            internal readonly string Version;

            internal Lists(IEnumerable<string> content, IEnumerable<string> contentPrefixes, IEnumerable<string> contentTypes,
                IEnumerable<string> deniedAssemblies, IEnumerable<string> deniedTypes, IEnumerable<string> byName, IEnumerable<string> sinks, string source)
            {
                ContentAssemblies = new HashSet<string>(content, StringComparer.Ordinal);
                ContentAssemblyPrefixes = new List<string>(contentPrefixes).ToArray();
                ContentTypes = new HashSet<string>(contentTypes, StringComparer.Ordinal);
                DeniedAssemblies = new HashSet<string>(deniedAssemblies, StringComparer.Ordinal);
                DeniedAssemblies.UnionWith(ExtraDeniedAssemblies);
                DeniedAssemblyPrefixes = ExtraDeniedAssemblyPrefixes;
                DeniedTypes = new HashSet<string>(deniedTypes, StringComparer.Ordinal);
                DeniedTypes.UnionWith(ExtraDeniedTypes);
                ByNameDispatch = new HashSet<string>(byName, StringComparer.Ordinal);
                ByNameDispatch.UnionWith(BuiltinByNameDispatch);
                AnimationEventSinks = new HashSet<string>(sinks, StringComparer.Ordinal);
                Source = source;
                Version = Hash();
            }

            string Hash()
            {
                var text = new StringBuilder(Source).Append('\u0001');
                void Add(string name, IEnumerable<string> items)
                {
                    text.Append(name).Append('=');
                    var sorted = new List<string>(items);
                    sorted.Sort(StringComparer.Ordinal);
                    foreach (var item in sorted) text.Append(item).Append('\n');
                    text.Append('\u0001');
                }
                Add("content", ContentAssemblies);
                Add("contentPrefixes", ContentAssemblyPrefixes);
                Add("contentTypes", ContentTypes);
                Add("deniedAssemblies", DeniedAssemblies);
                Add("deniedPrefixes", DeniedAssemblyPrefixes);
                Add("deniedTypes", DeniedTypes);
                Add("byName", ByNameDispatch);
                Add("sinks", AnimationEventSinks);
                Add("scriptableObjects", AllowedScriptableObjects);
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                    var hex = new StringBuilder(16);
                    for (var i = 0; i < 8; i++) hex.Append(hash[i].ToString("x2"));
                    return hex.ToString();
                }
            }
        }

        static readonly object gate = new object();
        static Lists current;

        /// <summary>The lists in effect, read from the guard on first use.</summary>
        internal static Lists Current
        {
            get
            {
                var lists = current;
                if (lists != null) return lists;
                lock (gate)
                {
                    return current ?? (current = Read());
                }
            }
        }

        /// <summary>Reads the lists again (tests that change the guard's sets; the main thread at startup).</summary>
        internal static Lists Refresh()
        {
            lock (gate)
            {
                return current = Read();
            }
        }

        static Lists Read()
        {
            Type guard = null;
            try
            {
                guard = typeof(BundleCheckPolicy).Assembly.GetType("BS.BSUnityEventGuard", false);
            }
            catch (Exception)
            {
                guard = null;
            }
            var complete = guard != null;
            string[] Field(string name, string[] fallback)
            {
                try
                {
                    var field = guard?.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (field?.GetValue(null) is IEnumerable values)
                    {
                        var list = new List<string>();
                        foreach (var value in values)
                        {
                            if (value is string text) list.Add(text);
                        }
                        return list.ToArray();
                    }
                }
                catch (Exception)
                {
                    // fall back below
                }
                complete = false;
                return fallback;
            }
            var content = Field("contentAssemblies", BuiltinContentAssemblies);
            var prefixes = Field("contentAssemblyPrefixes", BuiltinContentAssemblyPrefixes);
            var contentTypes = Field("contentTypes", BuiltinContentTypes);
            var deniedAssemblies = Field("deniedHostAssemblies", BuiltinDeniedHostAssemblies);
            var deniedTypes = Field("deniedHostTypes", BuiltinDeniedHostTypes);
            var byName = Field("byNameDispatch", BuiltinByNameDispatch);
            var sinks = Field("animationEventSinks", BuiltinAnimationEventSinks);
            // A partial read would mix sources: use the built-in copies throughout instead.
            if (!complete)
            {
                return new Lists(BuiltinContentAssemblies, BuiltinContentAssemblyPrefixes, BuiltinContentTypes, BuiltinDeniedHostAssemblies,
                    BuiltinDeniedHostTypes, BuiltinByNameDispatch, BuiltinAnimationEventSinks, "built-in");
            }
            return new Lists(content, prefixes, contentTypes, deniedAssemblies, deniedTypes, byName, sinks, "guard");
        }

        // ------------------------------------------------------------------ rules over the lists

        internal enum ScriptClass
        {
            Content,
            Denied,
            Unlisted,
        }

        /// <summary>
        /// Classifies a component type that resolves in the app, by its full name and its assembly's simple name. Denied
        /// types and assemblies win over the world-facing lists.
        /// </summary>
        internal static ScriptClass Classify(string typeFullName, string assemblyName)
        {
            var lists = Current;
            typeFullName = typeFullName ?? "";
            assemblyName = assemblyName ?? "";
            if (lists.DeniedTypes.Contains(typeFullName)) return ScriptClass.Denied;
            if (lists.DeniedAssemblies.Contains(assemblyName)) return ScriptClass.Denied;
            foreach (var prefix in lists.DeniedAssemblyPrefixes)
            {
                if (assemblyName.StartsWith(prefix, StringComparison.Ordinal)) return ScriptClass.Denied;
            }
            if (lists.ContentTypes.Contains(typeFullName)) return ScriptClass.Content;
            if (lists.ContentAssemblies.Contains(assemblyName)) return ScriptClass.Content;
            foreach (var prefix in lists.ContentAssemblyPrefixes)
            {
                if (assemblyName.StartsWith(prefix, StringComparison.Ordinal)) return ScriptClass.Content;
            }
            return ScriptClass.Unlisted;
        }

        /// <summary>Classifies a resolved script type; a denied base type (OnScreenControl...) denies its subclasses.</summary>
        internal static ScriptClass Classify(Type type)
        {
            var denied = Current.DeniedTypes;
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                if (current == typeof(UnityEngine.MonoBehaviour) || current == typeof(UnityEngine.ScriptableObject)) break;
                if (denied.Contains(current.FullName ?? "")) return ScriptClass.Denied;
            }
            return Classify(type.FullName, type.Assembly.GetName().Name);
        }

        /// <summary>The worst classification of any candidate (Denied over Unlisted over Content); Content for none.</summary>
        internal static ScriptClass Worst(Type[] candidates)
        {
            var worst = ScriptClass.Content;
            if (candidates == null) return worst;
            foreach (var type in candidates)
            {
                var verdict = Classify(type);
                if (verdict == ScriptClass.Denied) return verdict;
                if (verdict == ScriptClass.Unlisted) worst = verdict;
            }
            return worst;
        }

        /// <summary>A ScriptableObject type (or a base of it) on <see cref="AllowedScriptableObjects"/>.</summary>
        internal static bool IsAllowedScriptableObject(Type type)
        {
            for (var current = type; current != null && current != typeof(UnityEngine.ScriptableObject); current = current.BaseType)
            {
                if (Array.IndexOf(AllowedScriptableObjects, current.FullName ?? "") >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// The gate and the client's bundle security (BanterStubsAllowed, WorldActivation, BS.BSUnityEventGuard, anything in
        /// BS.Security), matched by name with their nested types: never graph data (the gate's own IsGateType rule).
        /// </summary>
        internal static bool IsGateType(Type type)
        {
            for (var current = type; current != null; current = current.DeclaringType)
            {
                var name = current.FullName ?? "";
                var ns = current.Namespace ?? "";
                if (name == "Unity.VisualScripting.BanterStubsAllowed" || name == "Unity.VisualScripting.WorldActivation"
                    || name == "BS.BSUnityEventGuard" || ns == "BS.Security" || ns.StartsWith("BS.Security.", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static bool IsByNameDispatch(string methodName) => methodName != null && Current.ByNameDispatch.Contains(methodName);

        internal static bool IsAnimationEventSink(string functionName) => functionName != null && Current.AnimationEventSinks.Contains(functionName);

        // ------------------------------------------------------------------ versions

        /// <summary>
        /// A short hash of every list in effect (and where they came from), plus the enforcement flag: a verdict cached
        /// under another list version is judged again.
        /// </summary>
        internal static string ListVersion(bool enforceAllowList) =>
            enforceAllowList ? Current.Version + "+strict" + AllowListSize : Current.Version;

        /// <summary>The Visual Scripting allow-list's size, part of the strict list version (it only matters when enforced).</summary>
        static int AllowListSize =>
#if GREENFIELD_PROJECT
            Unity.VisualScripting.BanterStubsAllowed.members.Length;
#else
            0;
#endif

        static string checkerBuild;

        /// <summary>
        /// The checker assembly's module version id (first 8 hex digits): a rebuilt checker judges every bundle again, even
        /// when nobody remembered to bump <see cref="CheckerVersion"/>.
        /// </summary>
        internal static string CheckerBuild
        {
            get
            {
                if (checkerBuild != null) return checkerBuild;
                string id;
                try
                {
                    id = typeof(BundleCheckPolicy).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 8);
                }
                catch (Exception)
                {
                    id = "unknown";
                }
                return checkerBuild = id;
            }
        }

        /// <summary>"checker+build/lists/options/layouts/environment", the version part of a cache key.</summary>
        internal static string Versions(BundleCheckOptions options, string environment) =>
            Versions(options, environment, options.ScriptLayouts ?? BundleScriptLayouts.Current);

        /// <summary>The same, for a check run with <paramref name="layouts"/> (the script layout table's fingerprint, or "-").</summary>
        internal static string Versions(BundleCheckOptions options, string environment, BundleScriptLayouts.Table layouts) =>
            "c" + CheckerVersion + "." + CheckerBuild + "/l" + ListVersion(options.EnforceAllowList) + "/s" + options.MaxScenes
            + (options.RefuseHiddenAnimationEventNames ? "h" : "") + (options.NarrowPatterns == BundleSeverity.Refuse ? "n" : "")
            + "/t" + (layouts == null ? "-" : layouts.Fingerprint) + "/e" + (environment ?? "");
    }
}
