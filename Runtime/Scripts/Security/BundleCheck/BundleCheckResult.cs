using System;
using System.Collections.Generic;
using System.Text;

namespace BS.Security
{
    internal enum BundleVerdictKind
    {
        Pass,
        Refuse,
    }

    /// <summary>Why a bundle was refused, or what was only reported. Stable names: they go into logs and the verdict cache.</summary>
    internal enum BundleRule
    {
        // ---- container (always refuse)
        /// <summary>Not a UnityFS bundle, or its container, blocks, files or objects are malformed or out of bounds.</summary>
        Malformed,
        /// <summary>A feature the checker does not read (encryption flag, big-endian files, mixed compression, unknown formats).</summary>
        Unsupported,
        /// <summary>The bundle is larger than the checker's limits (would cost too much memory to judge).</summary>
        TooLarge,
        /// <summary>A serialized file without type trees: Unity would read it with the app's own layouts, unseen.</summary>
        NoTypeTree,
        /// <summary>
        /// A type tree Unity could read otherwise than the checker: its stored hash does not match its nodes, its root
        /// names another class than its class id, a type entry names another script than the object's m_Script, or a
        /// judged field name differs only by case (Unity matches some names ignoring case: U11).
        /// </summary>
        TypeTreeMismatch,
        /// <summary>
        /// STRUCTURAL: a type tree shape Unity's own reader can desynchronise on: a PPtr that is not exactly {int m_FileID,
        /// SInt64 m_PathID} (Unity reads a PPtr as those 12 bytes whatever the tree lists: an extra child shifts every
        /// later field, and a shifted string length crashed Unity's SafeBinaryRead with "Size overflow in allocator",
        /// BundleCheckUnityBehaviourTests U1 A3), a malformed string or array, a primitive of the wrong size, duplicate
        /// fields, a call group that is not Unity's, broken tree levels.
        /// </summary>
        TypeTreeShape,
        /// <summary>More scenes than the bundle kind allows.</summary>
        TooManyScenes,
        /// <summary>
        /// A reference to a file that is neither a serialized file of this bundle ("archive:/{mount}/{node}") nor one of
        /// Unity's built-in resource files: another bundle's file or the player's own (globalgamemanagers...).
        /// </summary>
        ExternalFile,
        /// <summary>
        /// A name the checker must judge (MonoScript identity, persistent-call type or method name, AnimationEvent function,
        /// graph type) that holds control characters (NUL included) or is longer than its limit.
        /// </summary>
        BadName,

        // ---- components
        /// <summary>A MonoScript naming a component type from a deny-listed host assembly or a deny-listed type.</summary>
        DeniedScript,
        /// <summary>A component (on a GameObject) whose script resolves to an app type outside the world-facing lists.</summary>
        HostComponent,
        /// <summary>
        /// A ScriptableObject whose script resolves to an app type outside the world-facing lists (it runs OnEnable and
        /// OnAfterDeserialize at load); InputActionAsset and InputActionReference are allowed (reported).
        /// </summary>
        UnlistedScript,
        /// <summary>Report: a behaviour's script is in one of Unity's built-in resource files (UI Toolkit's); its data is judged as live.</summary>
        ExternalScript,
        /// <summary>A [SerializeReference] value whose type resolves to a denied or unlisted app type, or cannot be judged.</summary>
        HostReference,

        // ---- persistent UnityEvent calls
        /// <summary>A call with no target whose type name reaches a static method.</summary>
        CallNoTarget,
        /// <summary>A call that resolves to a static method.</summary>
        CallStatic,
        /// <summary>A call that resolves to a non-public method.</summary>
        CallNonPublic,
        /// <summary>A call to SendMessage/BroadcastMessage/Invoke/StartCoroutine... (calls methods by name).</summary>
        CallByName,
        /// <summary>A call whose target is outside the bundle (another bundle, built-in or player resources).</summary>
        CallOutsideTarget,
        /// <summary>A call whose stored type name, once its target is gone, reaches a static method.</summary>
        CallTypeName,
        /// <summary>A call whose target type cannot be resolved, so its method cannot be judged.</summary>
        CallUnresolvedTarget,
        /// <summary>Report: no target and nothing static reachable; Unity binds nothing.</summary>
        CallDead,
        /// <summary>Report: the method does not resolve on the target; Unity binds nothing.</summary>
        CallUnresolved,
        /// <summary>Report: the stored type name differs from the target's type (nothing static reachable through it).</summary>
        CallTypeNameDiffers,
        /// <summary>Report (refuse when the allow-list is enforced): a public instance member not on the Visual Scripting allow-list.</summary>
        CallNotOnAllowList,
        /// <summary>Report: a call inside a component whose script is missing in the app (never deserialized).</summary>
        CallInert,

        // ---- other by-name sinks
        /// <summary>An AnimationEvent naming SendMessage-style dispatch, or a static/non-public method on a behaviour in the bundle.</summary>
        AnimationEvent,
        /// <summary>Report: an AnimationEvent the runtime guard drops or reports (no receiver in the bundle, or not on the allow-list).</summary>
        AnimationEventReported,
        /// <summary>A serialized callback that SendMessages a method by name (I2 EventCallback.MethodName).</summary>
        NamedCallback,
        /// <summary>PlayerInput in SendMessages/BroadcastMessages mode.</summary>
        PlayerInputMessages,
        /// <summary>
        /// An object stored with hide or persistence flags (m_ObjectHideFlags with HideInHierarchy, DontSaveInEditor,
        /// DontSaveInBuild or DontUnloadUnusedAsset): Unity never writes them into built content, and a hidden
        /// (HideAndDontSave) scene root is invisible to the runtime guard (PlayMode probe Q7).
        /// </summary>
        HideFlags,

        // ---- Visual Scripting graph data
        /// <summary>A graph/variables JSON blob carrying UnityEvent persistent calls.</summary>
        GraphUnityEvent,
        /// <summary>A graph/variables JSON blob carrying AnimationEvent values.</summary>
        GraphAnimationEvent,
        /// <summary>A graph/variables JSON blob the checker could not read.</summary>
        GraphUnreadable,
        /// <summary>
        /// A graph/variables JSON blob naming a type FullSerializer would build ("$type", a MissingType's "formerType") or a
        /// System.Type value that is a host, denied or gate type, or that the checker cannot judge.
        /// </summary>
        GraphType,

        // ---- references
        /// <summary>Report: an object reference (event argument) to something outside the bundle.</summary>
        ExternalReference,

        /// <summary>The verdict cache holds a refusal for these exact bytes and rules.</summary>
        RefusedBefore,
    }

    /// <summary>The two kinds of refusal, so enforcement can be staged.</summary>
    internal static class BundleRules
    {
        /// <summary>
        /// STRUCTURAL refusals: bytes the native loader itself could misread or crash on (out-of-bounds sizes and counts,
        /// truncated or malformed containers and objects, decompression bombs, non-canonical type-tree shapes). They hold
        /// no policy judgement, so they can be enforced before the security-policy rules: Malformed, TooLarge and
        /// TypeTreeShape. Everything else is policy (what world content may contain).
        /// </summary>
        internal static bool IsStructural(BundleRule rule) =>
            rule == BundleRule.Malformed || rule == BundleRule.TooLarge || rule == BundleRule.TypeTreeShape;
    }

    /// <summary>One finding: the rule, where (sanitized names from the bundle), and a short detail. Never argument values.</summary>
    internal sealed class BundleFinding
    {
        internal readonly BundleRule Rule;
        internal readonly string Where;
        internal readonly string Detail;

        internal BundleFinding(BundleRule rule, string where, string detail)
        {
            Rule = rule;
            Where = where ?? "";
            Detail = detail ?? "";
        }

        public override string ToString() => Rule + ": " + Detail + (Where.Length > 0 ? " @ " + Where : "");
    }

    /// <summary>What the check cost and saw.</summary>
    internal sealed class BundleCheckStats
    {
        internal long BundleBytes;
        internal long UncompressedBytes;
        internal string Compression = "";
        internal string EngineVersion = "";
        internal int SerializedFiles;
        internal int SerializedFormat;
        internal int Scenes;
        internal int Objects;
        internal int ObjectsRead;
        internal int MonoBehaviours;
        internal int MonoScripts;
        internal int PersistentCalls;
        internal int AnimationEvents;
        internal int GraphBlobs;
        internal long GraphJsonBytes;
        internal int BlocksDecompressed;
        internal long BytesDecompressed;
        internal double ParseMilliseconds;
        internal double JudgeMilliseconds;
        internal double HashMilliseconds;

        public override string ToString() =>
            "files " + SerializedFiles + " (format " + SerializedFormat + "), scenes " + Scenes + ", objects " + ObjectsRead + "/" + Objects
            + ", behaviours " + MonoBehaviours + ", scripts " + MonoScripts + ", calls " + PersistentCalls + ", animation events " + AnimationEvents
            + ", graph blobs " + GraphBlobs + " (" + GraphJsonBytes / 1024 + " KiB), decompressed " + BlocksDecompressed + " blocks / "
            + BytesDecompressed / (1024 * 1024) + " MiB of " + UncompressedBytes / (1024 * 1024) + " MiB (" + Compression + "), hash "
            + HashMilliseconds.ToString("0.0") + " ms, parse " + ParseMilliseconds.ToString("0.0") + " ms, judge " + JudgeMilliseconds.ToString("0.0") + " ms";
    }

    /// <summary>The checker's answer for one bundle.</summary>
    internal sealed class BundleVerdict
    {
        internal readonly BundleVerdictKind Kind;
        /// <summary>Why it was refused (empty on Pass).</summary>
        internal readonly IReadOnlyList<BundleFinding> Reasons;
        /// <summary>Report-only findings (capped).</summary>
        internal readonly IReadOnlyList<BundleFinding> Reports;
        internal readonly BundleCheckStats Stats;
        /// <summary>Checker and list versions this verdict was made with.</summary>
        internal readonly string Versions;
        /// <summary>True when this came from the verdict cache rather than a fresh check.</summary>
        internal readonly bool FromCache;

        internal BundleVerdict(BundleVerdictKind kind, IReadOnlyList<BundleFinding> reasons, IReadOnlyList<BundleFinding> reports,
            BundleCheckStats stats, string versions, bool fromCache = false)
        {
            Kind = kind;
            Reasons = reasons ?? Array.Empty<BundleFinding>();
            Reports = reports ?? Array.Empty<BundleFinding>();
            Stats = stats ?? new BundleCheckStats();
            Versions = versions ?? "";
            FromCache = fromCache;
        }

        internal bool Passed => Kind == BundleVerdictKind.Pass;

        /// <summary>Refused for a structural reason (<see cref="BundleRules.IsStructural"/>): bytes the native loader could crash on.</summary>
        internal bool RefusedStructurally
        {
            get
            {
                foreach (var reason in Reasons)
                {
                    if (BundleRules.IsStructural(reason.Rule)) return true;
                }
                return false;
            }
        }

        /// <summary>The first refusal reason as one line, for a user-facing message ("" on Pass).</summary>
        internal string FirstReason => Reasons.Count > 0 ? Reasons[0].Rule + ": " + Reasons[0].Detail : "";

        /// <summary>A log block: verdict, reasons and reports (capped), stats.</summary>
        internal string Describe(string label, int maxLines = 40)
        {
            var text = new StringBuilder("[BundleCheck] ").Append(BundleNames.Clean(label)).Append(": ")
                .Append(Kind == BundleVerdictKind.Pass ? "PASS" : "REFUSED").Append(FromCache ? " (cached)" : "");
            var shown = 0;
            foreach (var reason in Reasons)
            {
                if (shown++ >= maxLines) break;
                text.Append("\n  refuse ").Append(reason);
            }
            if (Reasons.Count > maxLines) text.Append("\n  ... ").Append(Reasons.Count - maxLines).Append(" more reasons");
            var reports = 0;
            foreach (var report in Reports)
            {
                if (shown++ >= maxLines) break;
                reports++;
                text.Append("\n  report ").Append(report);
            }
            if (Reports.Count > reports) text.Append("\n  ... ").Append(Reports.Count - reports).Append(" more reports");
            text.Append("\n  ").Append(Stats);
            return text.ToString();
        }
    }

    internal enum BundleSeverity
    {
        /// <summary>Pass, with a warning in the reports: the runtime guard neutralises it before it can fire.</summary>
        Warn,
        Refuse,
    }

    /// <summary>Knobs for one check. Defaults are the world-bundle policy.</summary>
    internal sealed class BundleCheckOptions
    {
        /// <summary>Scenes a bundle may hold: 1 for a world, 0 for a kit.</summary>
        internal int MaxScenes = 1;
        /// <summary>Refuse public calls/animation events missing from the Visual Scripting allow-list (else report them).</summary>
        internal bool EnforceAllowList;
        /// <summary>
        /// Refuse AnimationEvents whose name is non-public or static on some app MonoBehaviour (the guard's hidden-name
        /// rule, which the guard enforces by dropping the event); else report them.
        /// </summary>
        internal bool RefuseHiddenAnimationEventNames = true;
        /// <summary>
        /// The two narrow patterns live worlds carry that cannot fire before the runtime guard runs (see
        /// BundleChecker.NarrowPatterns): Warn for worlds (Shane, 2026-11-04: roll20's InputField call and ernst2's
        /// PlayerInput keep loading, with a warning), Refuse for kits (Kit()), where no sceneLoaded follows.
        /// </summary>
        internal BundleSeverity NarrowPatterns = BundleSeverity.Warn;
        /// <summary>
        /// Most LZMA data the checker decodes (LZMA bundles are one block, decoded from the start): 512 MiB on Android,
        /// 1 GiB elsewhere. A world over it needs re-publishing (LZ4).
        /// </summary>
        internal long MaxLzmaBlockBytes =
#if UNITY_ANDROID && !UNITY_EDITOR
            512L * 1024 * 1024;
#else
            1024L * 1024 * 1024;
#endif
        /// <summary>Largest LZ4 block (Unity writes 128 KiB).</summary>
        internal int MaxLz4BlockBytes = 16 * 1024 * 1024;
        /// <summary>Largest serialized-file metadata (type trees, object table) read into memory.</summary>
        internal int MaxMetadataBytes = 64 * 1024 * 1024;
        /// <summary>Decompressed blocks kept (LRU) while reading.</summary>
        internal int BlockCacheSize = 32;
        /// <summary>Report-only findings kept in the verdict.</summary>
        internal int MaxReports = 200;
        /// <summary>The app's script layouts to compare stored trees with (tests); null: the installed table, if any.</summary>
        internal BundleScriptLayouts.Table ScriptLayouts;

        internal static BundleCheckOptions World() => new BundleCheckOptions { MaxScenes = 1 };
        /// <summary>
        /// Kits refuse the narrow patterns too: a kit prefab is instantiated later, without the sceneLoaded pass that
        /// neutralises them in a world, and no kit in the corpus needs them.
        /// </summary>
        internal static BundleCheckOptions Kit() => new BundleCheckOptions { MaxScenes = 0, NarrowPatterns = BundleSeverity.Refuse };
    }

    /// <summary>Thrown inside the checker when the bundle must be refused for a container-level reason.</summary>
    internal sealed class BundleRefusedException : Exception
    {
        internal readonly BundleRule Rule;

        internal BundleRefusedException(BundleRule rule, string detail) : base(detail)
        {
            Rule = rule;
        }
    }

    /// <summary>Names from a bundle go into logs: printable identifier characters only, and short.</summary>
    internal static class BundleNames
    {
        internal static string Clean(string value, int max = 120)
        {
            if (string.IsNullOrEmpty(value)) return "?";
            var text = new StringBuilder(Math.Min(value.Length, max));
            foreach (var ch in value)
            {
                if (text.Length >= max)
                {
                    text.Append("...");
                    break;
                }
                text.Append(char.IsLetterOrDigit(ch) || "._+`<>[],-/ ".IndexOf(ch) >= 0 ? ch : '?');
            }
            return text.ToString();
        }
    }
}
