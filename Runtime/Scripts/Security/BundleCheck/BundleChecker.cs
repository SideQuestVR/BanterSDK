using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using AssetsTools.NET;

namespace BS.Security
{
    /// <summary>
    /// Judges a world or kit AssetBundle from its bytes, before Unity loads anything from it: every serialized file
    /// (scene, sharedAssets, CABs) is read through its type trees, and the bundle is refused when it carries something
    /// that would run app code with world-chosen data during scene activation (where no runtime hook can see it):
    /// host-app components and ScriptableObjects, persistent calls that reach statics/non-public/by-name methods or
    /// targets outside the bundle, by-name callbacks, AnimationEvents and graph data that do the same, graph data naming
    /// types FullSerializer would build from host code, references into other bundles or the player's files, or anything
    /// the checker cannot read exactly as Unity will.
    /// </summary>
    /// <remarks>
    /// <para>Pure C# over the bytes (AssetsTools.NET for the container and type trees) plus reflection on the app's types: no
    /// UnityEngine API is called, so it runs off the main thread. The runtime guard (BSUnityEventGuard) stays as defence
    /// in depth. Rules: see <see cref="BundleRule"/>; lists: <see cref="BundleCheckPolicy"/>; tree shapes:
    /// <see cref="BundleTypeTree"/>.</para>
    /// <para><b>Refuse what cannot be judged exactly.</b> Unity's reader matches fields by name and skips what it does
    /// not know, so the checker never skips where it must judge: non-canonical trees are refused at compile, MonoBehaviour
    /// base fields must be canonical, a type entry's script must be the object's m_Script, names it judges must be short
    /// valid text without control characters, and every name lookup is a superset of Unity's (case-insensitive, nested
    /// '/', moved-from types, every candidate judged).</para>
    /// <para><b>Budgets.</b> Bytes and wall-clock time are the service's <see cref="BundleCheckBudget"/> (the archive
    /// charges every byte it serves; graph scans and tree compiles charge what they loop over). The counts below bound
    /// what one check may record. TODO: move them into the budget object when it grows count limits.</para>
    /// </remarks>
    internal static class BundleChecker
    {
        internal const int MaxCalls = 100000;
        internal const int MaxCallbacks = 100000;
        internal const int MaxGraphBlobs = 50000;
        internal const int MaxAnimationEvents = 100000;
        internal const int MaxReferences = 200000;
        /// <summary>Largest graph JSON blob read (the largest live world's graphs total under 2 MiB).</summary>
        internal const int MaxGraphJsonBytes = 32 * 1024 * 1024;
        /// <summary>Graph JSON read per bundle, all blobs together.</summary>
        internal const long MaxGraphJsonTotal = 128L * 1024 * 1024;
        /// <summary>MonoScript class, namespace and assembly names (Unity's own are far shorter).</summary>
        internal const int MaxIdentityBytes = 1024;
        internal const int MaxMethodNameBytes = 1024;
        internal const int MaxTypeNameBytes = 16 * 1024;
        internal const int MaxFunctionNameBytes = 1024;
        /// <summary>Distinct findings remembered for de-duplication; past it findings are still counted, never kept growing.</summary>
        const int MaxSeen = 8192;

        static readonly UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);

        internal static BundleVerdict Check(byte[] bundle, BundleCheckOptions options = null, string environment = null) =>
            Check(new ByteArrayBundleSource(bundle), options, environment);

        internal static BundleVerdict Check(BundleSource source, BundleCheckOptions options, string environment)
        {
            options = options ?? BundleCheckOptions.World();
            var stats = new BundleCheckStats { BundleBytes = source.Length };
            var layouts = options.ScriptLayouts ?? BundleScriptLayouts.Current;
            var run = new Run(options, stats, layouts);
            try
            {
                run.Execute(source);
            }
            catch (BundleRefusedException e)
            {
                run.Refuse(e.Rule, "", e.Message);
            }
            catch (OperationCanceledException)
            {
                throw; // the caller stopped waiting: no verdict at all
            }
            catch (Exception e)
            {
                // Fail closed: whatever the checker could not finish judging is refused.
                run.Refuse(BundleRule.Malformed, "", "checker could not read the bundle (" + e.GetType().Name + ")");
            }
            return run.Verdict(BundleCheckPolicy.Versions(options, environment, layouts));
        }

        /// <summary>
        /// The script type entries, ref types and native type entries a bundle stores (key, stored hash, tree digest), for
        /// BundleScriptLayoutsGenerator: read with the checker's own reader, so the table and the check agree on keys and
        /// digests. Throws <see cref="BundleRefusedException"/> when the bundle cannot be read that far.
        /// </summary>
        internal static List<BundleScriptLayouts.Observed> CollectLayouts(byte[] bundle)
        {
            var run = new Run(BundleCheckOptions.World(), new BundleCheckStats { BundleBytes = bundle.Length }, null);
            run.Collected = new List<BundleScriptLayouts.Observed>();
            run.Execute(new ByteArrayBundleSource(bundle));
            return run.Collected;
        }

#if BUNDLECHECK_OFFLINE
        /// <summary>Offline harness only: called with every checked bundle's stored layouts (corpus consistency runs).</summary>
        internal static Action<List<BundleScriptLayouts.Observed>> LayoutObserver;
#endif

        // ------------------------------------------------------------------ what the walk records

        enum OwnerKind : byte
        {
            Behaviour,
            Clip,
        }

        /// <summary>Whether data in an object is ever deserialized into app code, and as what.</summary>
        enum OwnerState : byte
        {
            /// <summary>Never deserialized: no script, a missing script, an unresolved [SerializeReference] type, a clip's extra data.</summary>
            Inert,
            /// <summary>Deserialized into a known app type.</summary>
            Live,
            /// <summary>Deserialized, but into a type the checker cannot see (a script in Unity's built-in resources): judged as live.</summary>
            LiveUnknown,
        }

        /// <summary>Who holds what is being walked: an object, or a [SerializeReference] value inside one.</summary>
        sealed class Owner
        {
            internal BundleSerializedFile File;
            internal long PathId;
            internal OwnerKind Kind;
            /// <summary>Set for [SerializeReference] data: the managed type's identity and whether it resolves.</summary>
            internal bool IsReference;
            internal Type ReferenceType;
            internal bool ReferenceAmbiguous;
            internal string ReferenceName;
            /// <summary>Unity reads this object with the app's compiled layout (its stored hash is the app's; see BundleScriptLayouts).</summary>
            internal bool AppLayout;
        }

        sealed class Behaviour
        {
            internal BundleSerializedFile File;
            internal long PathId;
            internal BundlePPtr GameObject;
            internal BundlePPtr Script;
            internal string Name;
            internal long? NotificationBehavior;
        }

        sealed class Script
        {
            internal string Assembly = "", Namespace = "", Class = "";
            /// <summary>The type Unity binds when the identity matches exactly, else the first candidate; null = a missing script.</summary>
            internal Type Type;
            /// <summary>Every type Unity could bind (a superset); judged all.</summary>
            internal Type[] Candidates = Array.Empty<Type>();
            internal BundleCheckPolicy.ScriptClass Class2;
            internal string Identity => BundleNames.Clean((Namespace.Length > 0 ? Namespace + "." : "") + Class) + " [" + BundleNames.Clean(Assembly, 60) + "]";
        }

        sealed class Call
        {
            internal Owner Owner;
            internal string[] Path;
            internal int Index;
            internal BundlePPtr Target;
            internal string TypeName, Method, ArgumentTypeName;
            internal int Mode;
            internal BundlePPtr Argument;
        }

        sealed class Named
        {
            internal Owner Owner;
            internal string[] Path;
            internal string Value;
            internal BundleGraphJson.Result Graph;
        }

        sealed class Reference
        {
            internal Owner Holder;
            internal string Name;
            internal Type[] Candidates;
            internal bool Unjudgeable;
        }

        // ------------------------------------------------------------------ one check

        sealed class Run
        {
            readonly BundleCheckOptions options;
            readonly BundleCheckStats stats;
            readonly List<BundleFinding> reasons = new List<BundleFinding>();
            readonly List<BundleFinding> reports = new List<BundleFinding>();
            readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            bool refused;
            int reportsDropped;
            long graphJsonBytes;

            readonly List<BundleSerializedFile> files = new List<BundleSerializedFile>();
            readonly Dictionary<BundleSerializedFile, Dictionary<long, Script>> scripts = new Dictionary<BundleSerializedFile, Dictionary<long, Script>>();
            readonly Dictionary<BundleSerializedFile, Dictionary<long, Behaviour>> behaviours = new Dictionary<BundleSerializedFile, Dictionary<long, Behaviour>>();
            readonly List<Call> calls = new List<Call>();
            readonly List<Named> callbacks = new List<Named>();
            readonly List<Named> graphs = new List<Named>();
            readonly List<Named> animationEvents = new List<Named>();
            readonly List<Reference> references = new List<Reference>();
            /// <summary>Fields named like a judged name in another case ("m_persistentCalls"), refused where their data is live.</summary>
            readonly List<Named> variants = new List<Named>();
            readonly Dictionary<string, KeyValuePair<BundleGraphTypes.Verdict, string>> graphTypeVerdicts =
                new Dictionary<string, KeyValuePair<BundleGraphTypes.Verdict, string>>(StringComparer.Ordinal);
            readonly List<string> path = new List<string>();
            BundleObjectReader reader;
            Owner owner;
            /// <summary>The app's script layouts (null: none installed, stored trees taken as they are).</summary>
            readonly BundleScriptLayouts.Table layouts;
            /// <summary>Set by <see cref="CollectLayouts"/>: the run stops after the MonoScripts, filling this.</summary>
            internal List<BundleScriptLayouts.Observed> Collected;

            internal Run(BundleCheckOptions options, BundleCheckStats stats, BundleScriptLayouts.Table layouts)
            {
                this.options = options;
                this.stats = stats;
                this.layouts = layouts;
            }

            /// <summary>True the first time a finding is seen (de-duplication stops growing at <see cref="MaxSeen"/>).</summary>
            bool First(string key) => seen.Count >= MaxSeen ? !seen.Contains(key) : seen.Add(key);

            internal void Refuse(BundleRule rule, string where, string detail)
            {
                refused = true;
                if (reasons.Count >= 200) return;
                var finding = new BundleFinding(rule, where, detail);
                if (!First("R" + finding)) return;
                reasons.Add(finding);
            }

            void Report(BundleRule rule, string where, string detail)
            {
                if (reports.Count >= options.MaxReports)
                {
                    reportsDropped++;
                    return;
                }
                var finding = new BundleFinding(rule, where, detail);
                if (!First("P" + finding)) return;
                reports.Add(finding);
            }

            internal BundleVerdict Verdict(string versions) =>
                new BundleVerdict(refused || reasons.Count > 0 ? BundleVerdictKind.Refuse : BundleVerdictKind.Pass, reasons, reports, stats, versions);

            static BundleRefusedException Mismatch(string why) => new BundleRefusedException(BundleRule.TypeTreeMismatch, why);

            /// <summary>A shape Unity's own reader can desynchronise on (structural).</summary>
            static BundleRefusedException Shape(string why) => new BundleRefusedException(BundleRule.TypeTreeShape, why);

            // ---------------------------------------------------------- phase 1: container and files

            internal void Execute(BundleSource source)
            {
                var clock = Stopwatch.StartNew();
                var archive = BundleArchive.Open(source, options, stats);
                // Only the node layouts Unity writes. Otherwise refused, and no archive reference counts as the bundle's own.
                var mount = BundleSerializedFile.MountOf(archive.Nodes, out var layoutProblem);
                if (layoutProblem != null) Refuse(BundleRule.Unsupported, "", "bundle layout: " + layoutProblem);
                var byName = new Dictionary<string, BundleSerializedFile>(StringComparer.Ordinal);
                foreach (var node in archive.Nodes)
                {
                    if ((node.Flags & 3) != 0) throw new BundleRefusedException(BundleRule.Unsupported, "directory or deleted entry in the bundle");
                    if (!node.IsSerializedFlag)
                    {
                        // Resource payloads are read by Unity through their objects' stream paths only; anything else
                        // unflagged must not be a serialized file in disguise.
                        if (BundleArchive.IsResourceName(node.Name)) continue;
                        if (!BundleSerializedFile.LooksSerialized(archive, node))
                        {
                            // Unity 6 puts GI data under "GI/...", read by path by the engine. Nothing else is expected.
                            if (node.Name.StartsWith("GI/", StringComparison.Ordinal))
                            {
                                Report(BundleRule.Unsupported, "", "file " + BundleNames.Clean(node.Name, 60) + " is not a serialized file (not read)");
                                continue;
                            }
                            throw new BundleRefusedException(BundleRule.Unsupported, "file " + BundleNames.Clean(node.Name, 60)
                                                                                     + " is neither a serialized file, a resource payload nor GI data");
                        }
                    }
                    var file = BundleSerializedFile.Load(archive, node, options);
                    files.Add(file);
                    byName[node.Name] = file;
                    stats.Objects += file.Objects.Count;
                    stats.SerializedFormat = Math.Max(stats.SerializedFormat, (int)file.Version);
                }
                stats.SerializedFiles = files.Count;
                if (files.Count == 0) throw new BundleRefusedException(BundleRule.Unsupported, "no serialized file in the bundle");
                var hasAssetBundle = false;
                foreach (var file in files) hasAssetBundle |= file.HasAssetBundleObject;
                if (!hasAssetBundle) throw new BundleRefusedException(BundleRule.Unsupported, "no AssetBundle object in the bundle");

                // Externals: only this bundle's own serialized files (under its exact mount) and Unity's built-in files.
                var scenes = 0;
                foreach (var file in files)
                {
                    file.LinkExternals(byName, mount);
                    if (file.IsScene || (file.Name.StartsWith("BuildPlayer-", StringComparison.OrdinalIgnoreCase) && file.Name.IndexOf('.') < 0)) scenes++;
                }
                stats.Scenes = scenes;
                if (scenes > options.MaxScenes)
                    Refuse(BundleRule.TooManyScenes, "", scenes + " scenes (" + (options.MaxScenes == 0 ? "a kit bundle may hold none" : "at most " + options.MaxScenes) + ")");

                reader = new BundleObjectReader(archive);
                foreach (var file in files)
                {
                    foreach (var info in file.Objects.Values)
                    {
                        if (file.ClassOf(info) != BundleSerializedFile.ClassMonoScript) continue;
                        BundleCheckBudget.CheckCurrent();
                        try
                        {
                            ReadScript(file, info);
                        }
                        catch (BundleRefusedException e)
                        {
                            throw new BundleRefusedException(e.Rule, e.Message + " (in MonoScript " + BundleNames.Clean(file.Name, 48) + "#" + info.PathId + ")");
                        }
                    }
                }
                if (Collected != null)
                {
                    CollectInto(Collected);
                    return;
                }
#if BUNDLECHECK_OFFLINE
                if (LayoutObserver != null)
                {
                    var observed = new List<BundleScriptLayouts.Observed>();
                    CollectInto(observed);
                    LayoutObserver(observed);
                }
#endif
                foreach (var file in files)
                {
                    foreach (var info in file.Objects.Values)
                    {
                        var classId = file.ClassOf(info);
                        if (classId != BundleSerializedFile.ClassMonoBehaviour && classId != BundleSerializedFile.ClassAnimationClip) continue;
                        BundleCheckBudget.CheckCurrent();
                        try
                        {
                            WalkObject(file, info, classId == BundleSerializedFile.ClassMonoBehaviour ? OwnerKind.Behaviour : OwnerKind.Clip);
                        }
                        catch (BundleRefusedException e)
                        {
                            // Name the object (file, path id, script) so a refusal can be traced.
                            var script = classId == BundleSerializedFile.ClassMonoBehaviour ? ScriptAt(file, BehaviourAt(file, info.PathId)?.Script ?? default) : null;
                            throw new BundleRefusedException(e.Rule, e.Message + " (in " + BundleNames.Clean(file.Name, 48) + "#" + info.PathId
                                                                       + (script != null ? ", " + script.Identity : "") + ")");
                        }
                    }
                }
                // Hide and persistence flags, on any object whose stored tree has them (built content never does).
                foreach (var file in files)
                {
                    var withFlags = file.TypesWithHideFlags();
                    if (withFlags.Count == 0) continue;
                    foreach (var info in file.Objects.Values)
                    {
                        if (!withFlags.Contains(info.TypeIdOrIndex)) continue;
                        BundleCheckBudget.CheckCurrent();
                        try
                        {
                            ReadHideFlags(file, info);
                        }
                        catch (BundleRefusedException e)
                        {
                            throw new BundleRefusedException(e.Rule, e.Message + " (in " + BundleNames.Clean(file.Name, 48) + "#" + info.PathId + ")");
                        }
                    }
                }
                stats.ParseMilliseconds = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                Judge();
                stats.JudgeMilliseconds = clock.Elapsed.TotalMilliseconds;
            }

            /// <summary>HideFlags that hide an object or keep it past its scene: HideInHierarchy, DontSaveInEditor, DontSaveInBuild, DontUnloadUnusedAsset.</summary>
            const uint HidingFlags = 1 | 4 | 16 | 32;

            /// <summary>Reads an object's m_ObjectHideFlags (its stored tree has one) and refuses hiding or persistence flags.</summary>
            void ReadHideFlags(BundleSerializedFile file, AssetFileInfo info)
            {
                var tree = file.Tree(info.TypeIdOrIndex);
                var className = BundleSerializedFile.ClassName(file.ClassOf(info)) ?? "class " + file.ClassOf(info);
                file.Open(info, reader);
                foreach (var child in tree.Children)
                {
                    if (!string.Equals(child.Name, "m_ObjectHideFlags", StringComparison.OrdinalIgnoreCase))
                    {
                        Skip(child);
                        continue;
                    }
                    if (child.Name != "m_ObjectHideFlags") throw Mismatch(className + " field " + BundleNames.Clean(child.Name, 40) + " differs from m_ObjectHideFlags only by case");
                    if (child.Kind != BundleTypeTree.Kind.Primitive || child.Size != 4) throw Shape(className + " m_ObjectHideFlags is not a 4-byte integer");
                    var flags = (uint)reader.ReadInt32();
                    if ((flags & HidingFlags) != 0)
                        Refuse(BundleRule.HideFlags, BundleNames.Clean(file.Name, 48) + "#" + info.PathId,
                            className + " stored with HideFlags 0x" + flags.ToString("x") + " (hidden or kept past its scene)");
                    else
                        Report(BundleRule.HideFlags, "", className + " stores m_ObjectHideFlags 0x" + flags.ToString("x") + " (no hiding or persistence flag)");
                    return;
                }
            }

            /// <summary>A judged name: valid UTF-8, no control characters (NUL included), at most <paramref name="max"/> bytes.</summary>
            string ReadName(BundleTypeTree.Node node, int max, string what)
            {
                var length = reader.ReadInt32();
                if (length < 0) throw new BundleRefusedException(BundleRule.Malformed, "negative string length");
                if (length > max) throw new BundleRefusedException(BundleRule.BadName, what + " over " + max + " bytes");
                var bytes = reader.ReadBytes(length);
                if (node.Align) reader.Align();
                string text;
                try
                {
                    text = strictUtf8.GetString(bytes);
                }
                catch (ArgumentException)
                {
                    throw new BundleRefusedException(BundleRule.BadName, what + " that is not valid UTF-8");
                }
                if (BundleTypeResolver.HasControlCharacters(text)) throw new BundleRefusedException(BundleRule.BadName, what + " with control characters");
                return text;
            }

            /// <summary>A string kept only for messages: its first bytes, lenient; the rest skipped.</summary>
            string ReadLabel(BundleTypeTree.Node node, int keep)
            {
                var length = reader.ReadInt32();
                if (length < 0) throw new BundleRefusedException(BundleRule.Malformed, "negative string length");
                var take = Math.Min(length, keep);
                var bytes = reader.ReadBytes(take);
                reader.Skip(length - take);
                if (node.Align) reader.Align();
                return Encoding.UTF8.GetString(bytes);
            }

            static readonly string[] identityFields = { "m_ClassName", "m_Namespace", "m_AssemblyName" };

            /// <summary>The MonoBehaviour base fields the checker reads by name.</summary>
            static readonly string[] baseFields = { "m_Script", "m_GameObject", "m_NotificationBehavior" };

            void ReadScript(BundleSerializedFile file, AssetFileInfo info)
            {
                var tree = file.Tree(info.TypeIdOrIndex);
                file.NativeNameOf(info);
                file.Open(info, reader);
                var script = new Script();
                foreach (var child in tree.Children)
                {
                    var variant = BundleTypeTree.VariantOf(child.Name, identityFields);
                    if (variant != null) throw Mismatch("MonoScript field " + BundleNames.Clean(child.Name, 40) + " differs from " + variant + " only by case");
                    if (Array.IndexOf(identityFields, child.Name) < 0)
                    {
                        Skip(child);
                        continue;
                    }
                    // Unity reads these by name; anything but a short plain string is refused, never read as "".
                    if (child.Kind != BundleTypeTree.Kind.String) throw Shape("MonoScript " + child.Name + " is not a string");
                    var text = ReadName(child, MaxIdentityBytes, "MonoScript " + child.Name);
                    if (child.Name == "m_ClassName") script.Class = text;
                    else if (child.Name == "m_Namespace") script.Namespace = text;
                    else script.Assembly = text;
                }
                stats.ObjectsRead++;
                stats.MonoScripts++;
                script.Candidates = BundleTypeResolver.ResolveScriptCandidates(script.Assembly, script.Namespace, script.Class);
                script.Type = BundleTypeResolver.ResolveScript(script.Assembly, script.Namespace, script.Class);
                if (!scripts.TryGetValue(file, out var map)) scripts[file] = map = new Dictionary<long, Script>();
                map[info.PathId] = script;
            }

            // ---------------------------------------------------------- phase 2: the walk

            void WalkObject(BundleSerializedFile file, AssetFileInfo info, OwnerKind kind)
            {
                var tree = file.Tree(info.TypeIdOrIndex);
                if (tree.Kind != BundleTypeTree.Kind.Struct) throw Shape("object root is not a struct");
                file.NativeNameOf(info); // the class id's own tree name, or refused
                file.Open(info, reader);
                owner = new Owner { File = file, PathId = info.PathId, Kind = kind };
                path.Clear();
                Behaviour behaviour = null;
                if (kind == OwnerKind.Behaviour)
                {
                    owner.AppLayout = JudgeLayout(file, info);
                    behaviour = new Behaviour { File = file, PathId = info.PathId };
                    if (!behaviours.TryGetValue(file, out var map)) behaviours[file] = map = new Dictionary<long, Behaviour>();
                    map[info.PathId] = behaviour;
                    stats.MonoBehaviours++;
                }
                WalkFields(tree, behaviour);
                if (behaviour != null)
                {
                    // The type entry Unity reads this object with belongs to one script: it must be the object's own.
                    if (file.TryGetTypeScript(info, out var typeScript))
                    {
                        if (typeScript.FileId != behaviour.Script.FileId || typeScript.PathId != behaviour.Script.PathId)
                            throw Mismatch("behaviour whose type entry names another script than its m_Script");
                    }
                    else if (!behaviour.Script.IsNull)
                    {
                        throw Mismatch("behaviour with an m_Script whose type entry names no script");
                    }
                }
                stats.ObjectsRead++;
            }

            static bool IsInt(BundleTypeTree.Node node) =>
                node.Kind == BundleTypeTree.Kind.Primitive && node.Size == 4 && (node.Type == "int" || node.Type == "SInt32");

            /// <summary>
            /// A struct's fields (the object root when <paramref name="behaviour"/> is set): its role strings captured, every
            /// other interesting child walked whatever roles the struct has, the rest skipped; then its roles dispatched.
            /// </summary>
            void WalkFields(BundleTypeTree.Node node, Behaviour behaviour, int childCount = -1)
            {
                var roles = node.Roles;
                var methodName = "";
                string json = null, functionName = null;
                var children = childCount < 0 ? node.Children.Length : childCount;
                for (var index = 0; index < children; index++)
                {
                    var child = node.Children[index];
                    if (child.Variant != null) variants.Add(new Named { Owner = owner, Path = path.ToArray(), Value = child.Name + " (" + child.Variant + ")" });
                    if (behaviour != null)
                    {
                        var variant = BundleTypeTree.VariantOf(child.Name, baseFields);
                        if (variant != null) throw Mismatch("behaviour field " + BundleNames.Clean(child.Name, 40) + " differs from " + variant + " only by case");
                        switch (child.Name)
                        {
                            case "m_GameObject":
                                if (!child.IsPPtr) throw Shape("m_GameObject is not a PPtr");
                                behaviour.GameObject = ReadPPtr(child);
                                continue;
                            case "m_Script":
                                if (!child.IsPPtr) throw Shape("m_Script is not a PPtr");
                                behaviour.Script = ReadPPtr(child);
                                continue;
                            case "m_Name":
                                if (child.Kind != BundleTypeTree.Kind.String) break;
                                behaviour.Name = ReadLabel(child, 256);
                                continue;
                            case "m_NotificationBehavior":
                                if (!IsInt(child)) throw Shape("m_NotificationBehavior is not an int");
                                behaviour.NotificationBehavior = reader.ReadInt32();
                                if (child.Align) reader.Align();
                                continue;
                        }
                    }
                    if (child.Kind == BundleTypeTree.Kind.String && roles != BundleTypeTree.Role.None)
                    {
                        if (child.Name == BundleTypeTree.MethodNameField && (roles & BundleTypeTree.Role.NamedCallback) != 0)
                        {
                            methodName = ReadLabel(child, 120);
                            continue;
                        }
                        if (child.Name == BundleTypeTree.JsonField && (roles & BundleTypeTree.Role.GraphData) != 0)
                        {
                            json = ReadJson(child);
                            continue;
                        }
                        if (child.Name == BundleTypeTree.FunctionNameField && (roles & BundleTypeTree.Role.AnimationEvent) != 0 && owner.Kind == OwnerKind.Clip)
                        {
                            functionName = ReadName(child, MaxFunctionNameBytes, "AnimationEvent function name");
                            continue;
                        }
                    }
                    if (child.Interesting) Walk(child, false);
                    else Skip(child);
                }
                if ((roles & BundleTypeTree.Role.NamedCallback) != 0)
                {
                    if (callbacks.Count >= MaxCallbacks) throw new BundleRefusedException(BundleRule.TooLarge, "more than " + MaxCallbacks + " named callbacks");
                    callbacks.Add(new Named { Owner = owner, Path = path.ToArray(), Value = methodName });
                }
                if ((roles & BundleTypeTree.Role.GraphData) != 0)
                {
                    if (graphs.Count >= MaxGraphBlobs) throw new BundleRefusedException(BundleRule.TooLarge, "more than " + MaxGraphBlobs + " graph blobs");
                    stats.GraphBlobs++;
                    stats.GraphJsonBytes += json?.Length ?? 0;
                    graphs.Add(new Named { Owner = owner, Path = path.ToArray(), Graph = BundleGraphJson.Scan(json ?? "") });
                }
                if ((roles & BundleTypeTree.Role.AnimationEvent) != 0 && owner.Kind == OwnerKind.Clip)
                {
                    // A functionName struct inside a behaviour is plain data: Unity dispatches AnimationEvents from clips only.
                    if (animationEvents.Count >= MaxAnimationEvents) throw new BundleRefusedException(BundleRule.TooLarge, "more than " + MaxAnimationEvents + " AnimationEvents");
                    animationEvents.Add(new Named { Owner = owner, Path = path.ToArray(), Value = functionName ?? "" });
                    stats.AnimationEvents++;
                }
            }

            string ReadJson(BundleTypeTree.Node node)
            {
                var length = reader.ReadInt32();
                if (length < 0) throw new BundleRefusedException(BundleRule.Malformed, "negative string length");
                if (length > MaxGraphJsonBytes) throw new BundleRefusedException(BundleRule.TooLarge, "graph data over " + MaxGraphJsonBytes / (1024 * 1024) + " MiB");
                graphJsonBytes += length;
                if (graphJsonBytes > MaxGraphJsonTotal) throw new BundleRefusedException(BundleRule.TooLarge, "graph data over " + MaxGraphJsonTotal / (1024 * 1024) + " MiB in all");
                var bytes = reader.ReadBytes(length);
                if (node.Align) reader.Align();
                return Encoding.UTF8.GetString(bytes);
            }

            void Walk(BundleTypeTree.Node node, bool isElement)
            {
                switch (node.Kind)
                {
                    case BundleTypeTree.Kind.Struct:
                        if ((node.Roles & BundleTypeTree.Role.PersistentCalls) != 0)
                        {
                            // The path stays at the UnityEvent's field: the event type is looked up from it.
                            ReadCallGroup(node);
                            break;
                        }
                        if (!isElement) path.Add(node.Name);
                        WalkFields(node, null);
                        if (!isElement) path.RemoveAt(path.Count - 1);
                        break;
                    case BundleTypeTree.Kind.Array:
                        {
                            var named = !isElement && node.Name != "Array";
                            if (named) path.Add(node.Name);
                            var count = Count(node);
                            path.Add("[]");
                            for (var i = 0; i < count; i++)
                            {
                                if ((i & 1023) == 1023) BundleCheckBudget.CheckCurrent();
                                if (node.Element.Interesting) Walk(node.Element, true);
                                else Skip(node.Element);
                            }
                            path.RemoveAt(path.Count - 1);
                            if (named) path.RemoveAt(path.Count - 1);
                            break;
                        }
                    case BundleTypeTree.Kind.ManagedReferences:
                        References();
                        break;
                    default:
                        Skip(node);
                        return;
                }
                if (node.Align) reader.Align();
            }

            int Count(BundleTypeTree.Node array)
            {
                var count = reader.ReadInt32();
                var min = array.Element.Min;
                if (count < 0 || (min > 0 && count > reader.Remaining / min) || (min == 0 && count > (1 << 20)))
                    throw new BundleRefusedException(BundleRule.Malformed, "array length out of range");
                return count;
            }

            void Skip(BundleTypeTree.Node node)
            {
                switch (node.Kind)
                {
                    case BundleTypeTree.Kind.Primitive:
                        reader.Skip(node.Size);
                        break;
                    case BundleTypeTree.Kind.String:
                    case BundleTypeTree.Kind.Bytes:
                        {
                            var length = reader.ReadInt32();
                            if (length < 0) throw new BundleRefusedException(BundleRule.Malformed, "negative length");
                            reader.Skip(length);
                            break;
                        }
                    case BundleTypeTree.Kind.Array:
                        {
                            var count = Count(node);
                            if (node.Element.Fixed >= 0) reader.Skip((long)count * node.Element.Fixed);
                            else
                            {
                                for (var i = 0; i < count; i++)
                                {
                                    if ((i & 1023) == 1023) BundleCheckBudget.CheckCurrent();
                                    Skip(node.Element);
                                }
                            }
                            break;
                        }
                    case BundleTypeTree.Kind.Struct:
                        foreach (var child in node.Children) Skip(child);
                        break;
                    case BundleTypeTree.Kind.ManagedReferences:
                        References();
                        break;
                }
                if (node.Align) reader.Align();
            }

            BundlePPtr ReadPPtr(BundleTypeTree.Node node)
            {
                var pointer = new BundlePPtr
                {
                    FileId = reader.ReadInt32(),
                    PathId = reader.ReadInt64(),
                };
                if (node.Align) reader.Align();
                return pointer;
            }

            int ReadInt(BundleTypeTree.Node node)
            {
                var value = reader.ReadInt32();
                if (node.Align) reader.Align();
                return value;
            }

            /// <summary>
            /// A canonical PersistentCallGroup (validated at compile): every call read field by field, by name as Unity
            /// does; a field the tree leaves out keeps Unity's default (no target, empty names, EventDefined).
            /// </summary>
            void ReadCallGroup(BundleTypeTree.Node group)
            {
                var where = path.ToArray();
                foreach (var list in group.Children)
                {
                    var array = list.Children[0];
                    var count = Count(array);
                    for (var i = 0; i < count; i++)
                    {
                        if ((i & 1023) == 1023) BundleCheckBudget.CheckCurrent();
                        ReadCall(array.Element, where, i);
                    }
                    if (array.Align) reader.Align();
                    if (list.Align) reader.Align();
                }
            }

            void ReadCall(BundleTypeTree.Node element, string[] where, int index)
            {
                BundlePPtr target = default, argument = default;
                string typeName = "", method = "", argumentType = "";
                var mode = 0; // PersistentListenerMode.EventDefined, PersistentCall's default
                foreach (var c in element.Children)
                {
                    switch (c.Name)
                    {
                        case "m_Target":
                            target = ReadPPtr(c);
                            break;
                        case "m_TargetAssemblyTypeName":
                            typeName = ReadName(c, MaxTypeNameBytes, "persistent call type name");
                            break;
                        case "m_MethodName":
                            method = ReadName(c, MaxMethodNameBytes, "persistent call method name");
                            break;
                        case "m_Mode":
                            mode = ReadInt(c);
                            break;
                        case "m_Arguments":
                            foreach (var a in c.Children)
                            {
                                switch (a.Name)
                                {
                                    case "m_ObjectArgument":
                                        argument = ReadPPtr(a);
                                        break;
                                    case "m_ObjectArgumentAssemblyTypeName":
                                        argumentType = ReadName(a, MaxTypeNameBytes, "persistent call argument type name");
                                        break;
                                    default:
                                        Skip(a);
                                        break;
                                }
                            }
                            if (c.Align) reader.Align();
                            break;
                        default:
                            Skip(c);
                            break;
                    }
                }
                if (element.Align) reader.Align();
                if (calls.Count >= MaxCalls) throw new BundleRefusedException(BundleRule.TooLarge, "more than " + MaxCalls + " persistent calls");
                calls.Add(new Call
                {
                    Owner = owner, Path = where, Index = index, Target = target, TypeName = typeName, Method = method, Mode = mode,
                    ArgumentTypeName = argumentType, Argument = argument,
                });
                stats.PersistentCalls++;
            }

            /// <summary>
            /// A ManagedReferencesRegistry ([SerializeReference] values): each value is read with its own stored type
            /// tree and walked like the object, owned by its managed type (inert when that type is not in the app). Every
            /// value's type is judged, referenced from a field or not.
            /// </summary>
            void References()
            {
                var file = owner.File;
                var outer = owner;
                var outerPath = new List<string>(path);
                var version = reader.ReadInt32();
                int count;
                if (version == 2)
                {
                    count = reader.ReadInt32();
                    if (count < 0 || count > reader.Remaining / 20) throw new BundleRefusedException(BundleRule.Malformed, "reference count out of range");
                }
                else if (version == 1)
                {
                    count = int.MaxValue;
                }
                else
                {
                    throw new BundleRefusedException(BundleRule.Unsupported, "managed references version " + version);
                }
                for (var i = 0; i < count; i++)
                {
                    BundleCheckBudget.CheckCurrent();
                    if (version == 2) reader.Skip(8); // rid
                    var className = ReferenceString();
                    var ns = ReferenceString();
                    var asm = ReferenceString();
                    if (version == 1 && className == "Terminus" && ns == "UnityEngine.DMAT" && asm == "FAKE_ASM") break;
                    if (className.Length == 0 && ns.Length == 0 && asm.Length == 0) continue; // a null reference: no data
                    var tree = file.RefTree(className, ns, asm);
                    if (tree == null) throw new BundleRefusedException(BundleRule.NoTypeTree, "a [SerializeReference] value without a type tree");
                    var candidates = BundleTypeResolver.ResolveManagedCandidates(asm, ns, className);
                    var name = BundleNames.Clean((ns.Length > 0 ? ns + "." : "") + className);
                    if (layouts != null) JudgeReferenceLayout(file, outer.AppLayout, className, ns, asm, candidates, name);
                    if (references.Count >= MaxReferences) throw new BundleRefusedException(BundleRule.TooLarge, "more than " + MaxReferences + " [SerializeReference] values");
                    references.Add(new Reference
                    {
                        Holder = outer, Name = name, Candidates = candidates,
                        // Generic or otherwise unusual names Unity might still resolve: a type the checker cannot find there cannot be judged.
                        Unjudgeable = candidates.Length == 0 && (className.IndexOfAny(new[] { '[', '`', ',' }) >= 0 || ns.IndexOfAny(new[] { '[', '`', ',' }) >= 0),
                    });
                    owner = new Owner
                    {
                        File = file, PathId = outer.PathId, Kind = outer.Kind, IsReference = true,
                        ReferenceType = BundleTypeResolver.ResolveManaged(asm, ns, className), ReferenceAmbiguous = candidates.Length > 1, ReferenceName = name,
                    };
                    path.Clear();
                    // A referenced type's tree ends with its own registry when it has [SerializeReference] fields, but
                    // only the root object stores one (AssetsTools drops it the same way).
                    var fields = tree.Children.Length;
                    if (fields > 0 && tree.Children[fields - 1].Kind == BundleTypeTree.Kind.ManagedReferences) fields--;
                    WalkFields(tree, null, fields);
                    if (tree.Align) reader.Align();
                }
                owner = outer;
                path.Clear();
                path.AddRange(outerPath);
            }

            string ReferenceString()
            {
                var length = reader.ReadInt32();
                if (length < 0 || length > 4096) throw new BundleRefusedException(BundleRule.Malformed, "reference type name length");
                string text;
                try
                {
                    text = strictUtf8.GetString(reader.ReadBytes(length));
                }
                catch (ArgumentException)
                {
                    throw new BundleRefusedException(BundleRule.BadName, "[SerializeReference] type name that is not valid UTF-8");
                }
                reader.Align();
                if (BundleTypeResolver.HasControlCharacters(text)) throw new BundleRefusedException(BundleRule.BadName, "[SerializeReference] type name with control characters");
                return text;
            }

            // ---------------------------------------------------------- phase 3: judging

            /// <summary>The table keys of the script a type entry names: the engine's own, or every app type Unity could bind.</summary>
            List<string> ScriptKeys(BundleSerializedFile file, BundlePPtr pointer)
            {
                var keys = new List<string>();
                if (pointer.IsNull) return keys;
                if (file.IsBuiltin(pointer))
                {
                    keys.Add(BundleScriptLayouts.BuiltinKey(file.ExternalName(pointer.FileId), pointer.PathId));
                    return keys;
                }
                var script = ScriptAt(file, pointer);
                if (script == null) return keys;
                foreach (var type in script.Candidates) keys.Add(BundleScriptLayouts.KeyOf(type));
                return keys;
            }

            /// <summary>
            /// Whether Unity reads this behaviour with the app's own compiled layout: its type entry's stored hash is the
            /// app's for its script (or zero, never stored for a real script and treated the same). The stored tree, which
            /// the checker reads, must then be the app's own: refused as TypeTreeShape otherwise (a forged tree with a
            /// copied hash, U2). A script the table does not hold is taken as it is.
            /// </summary>
            bool JudgeLayout(BundleSerializedFile file, AssetFileInfo info)
            {
                if (layouts == null || !file.TryGetTypeScript(info, out var pointer) || pointer.IsNull) return false;
                var stored = file.StoredHash(info.TypeIdOrIndex);
                var appLayout = false;
                foreach (var key in ScriptKeys(file, pointer))
                {
                    if (!layouts.Scripts.TryGetValue(key, out var entry)) continue;
                    if (!BundleScriptLayouts.Same(entry.Hash, stored) && !BundleScriptLayouts.IsZero(stored)) continue;
                    appLayout = true;
                    if (!BundleScriptLayouts.Same(entry.Digest, file.LayoutDigest(info.TypeIdOrIndex)))
                        throw Shape("type tree differs from the app's own layout of " + BundleNames.Clean(key, 120)
                                    + ", which Unity reads this object with (its stored hash is the app's)");
                }
                return appLayout;
            }

            /// <summary>
            /// A [SerializeReference] value inside an object Unity reads with the app's layout: Unity streams the registry
            /// with the app's layout of each value's class too (the r5 crash in U10: a value of a class its data was not
            /// written for, StreamedBinaryRead out of bounds). Its stored tree must then be the app's own, and an app class
            /// the table does not hold cannot be compared: both refused as TypeTreeShape (structural). The host decides:
            /// Unity stores every ref type with a zero hash (all of the live corpus's), so no per-class hash can. A class
            /// the app does not have is inert, read by its stored tree.
            /// </summary>
            void JudgeReferenceLayout(BundleSerializedFile file, bool appLayout, string className, string ns, string asm, Type[] candidates, string name)
            {
                if (!appLayout || candidates.Length == 0) return;
                byte[] digest = null;
                foreach (var type in candidates)
                {
                    var key = BundleScriptLayouts.KeyOf(type);
                    if (!layouts.References.TryGetValue(key, out var entry))
                        throw Shape("[SerializeReference] value of " + name + ", which Unity reads with the app's layout of "
                                    + BundleNames.Clean(key, 120) + ", a class the app's layout table does not hold");
                    digest = digest ?? file.RefLayoutDigest(className, ns, asm);
                    if (!BundleScriptLayouts.Same(entry.Digest, digest))
                        throw Shape("[SerializeReference] value of " + name + " stored with a type tree that differs from the app's own layout of "
                                    + BundleNames.Clean(key, 120) + ", which Unity reads it with");
                }
            }

            /// <summary>The bundle's script type entries ('S', keyed by the exactly bound type), ref types ('R') and native type entries ('N').</summary>
            void CollectInto(List<BundleScriptLayouts.Observed> into)
            {
                foreach (var file in files)
                {
                    var types = file.Metadata.TypeTreeTypes;
                    for (var index = 0; index < types.Count; index++)
                    {
                        BundleCheckBudget.CheckCurrent();
                        if (!file.HasTree(index)) continue;
                        var classId = types[index].TypeId;
                        if (classId != BundleSerializedFile.ClassMonoBehaviour)
                        {
                            into.Add(new BundleScriptLayouts.Observed { Kind = 'N', Key = "class " + classId, Hash = file.StoredHash(index), Digest = file.LayoutDigest(index) });
                            continue;
                        }
                        if (!file.TryGetTypeScript(index, out var pointer) || pointer.IsNull) continue;
                        string key = null;
                        if (file.IsBuiltin(pointer)) key = BundleScriptLayouts.BuiltinKey(file.ExternalName(pointer.FileId), pointer.PathId);
                        else
                        {
                            var script = ScriptAt(file, pointer);
                            if (script?.Type != null) key = BundleScriptLayouts.KeyOf(script.Type);
                        }
                        if (key != null) into.Add(new BundleScriptLayouts.Observed { Kind = 'S', Key = key, Hash = file.StoredHash(index), Digest = file.LayoutDigest(index) });
                    }
                    foreach (var (className, ns, asm, hash, digest) in file.RefTypeLayouts())
                    {
                        BundleCheckBudget.CheckCurrent();
                        var type = BundleTypeResolver.ResolveManaged(asm, ns, className);
                        if (type != null) into.Add(new BundleScriptLayouts.Observed { Kind = 'R', Key = BundleScriptLayouts.KeyOf(type), Hash = hash, Digest = digest });
                    }
                }
            }

            Script ScriptAt(BundleSerializedFile file, BundlePPtr pointer)
            {
                if (pointer.IsNull) return null;
                var target = file.FileOf(pointer, out _);
                if (target == null) return null;
                return scripts.TryGetValue(target, out var map) && map.TryGetValue(pointer.PathId, out var script) ? script : null;
            }

            Behaviour BehaviourAt(BundleSerializedFile file, long pathId) =>
                behaviours.TryGetValue(file, out var map) && map.TryGetValue(pathId, out var behaviour) ? behaviour : null;

            /// <summary>A behaviour's own state: no script or a missing one is inert; a built-in script is live but unknown.</summary>
            OwnerState HolderState(Behaviour behaviour, out Type type, out bool ambiguous)
            {
                type = null;
                ambiguous = false;
                if (behaviour == null || behaviour.Script.IsNull) return OwnerState.Inert;
                if (behaviour.File.IsBuiltin(behaviour.Script)) return OwnerState.LiveUnknown;
                var script = ScriptAt(behaviour.File, behaviour.Script);
                if (script?.Type == null) return OwnerState.Inert;
                type = script.Type;
                ambiguous = script.Candidates.Length > 1;
                return OwnerState.Live;
            }

            /// <summary>Whether data under <paramref name="who"/> is deserialized, and into which type (null when unknown).</summary>
            OwnerState StateOf(Owner who, out Type type, out bool ambiguous)
            {
                type = null;
                ambiguous = false;
                if (who.Kind != OwnerKind.Behaviour) return OwnerState.Inert; // a clip's data besides its events is native: never app code
                var state = HolderState(BehaviourAt(who.File, who.PathId), out type, out ambiguous);
                if (!who.IsReference || state == OwnerState.Inert) return state;
                type = who.ReferenceType;
                ambiguous = who.ReferenceAmbiguous;
                return type == null ? OwnerState.Inert : OwnerState.Live;
            }

            readonly Dictionary<string, string> objectNames = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>"file:GameObjectName/Component#pathId", names cleaned. Never values.</summary>
            string Where(Owner who)
            {
                var file = who.File;
                var behaviour = who.Kind == OwnerKind.Behaviour ? BehaviourAt(file, who.PathId) : null;
                string objectName = null, typeName;
                if (behaviour != null)
                {
                    var script = ScriptAt(file, behaviour.Script);
                    typeName = script != null ? BundleNames.Clean(script.Class) : file.IsBuiltin(behaviour.Script) ? "built-in script" : "MonoBehaviour";
                    objectName = !behaviour.GameObject.IsNull ? NameOf(file, behaviour.GameObject) : BundleNames.Clean(behaviour.Name);
                }
                else
                {
                    typeName = "AnimationClip";
                    objectName = NameOf(file, new BundlePPtr { FileId = 0, PathId = who.PathId });
                }
                var text = BundleNames.Clean(file.Name, 48) + ":" + (objectName ?? "?") + "/" + typeName + "#" + who.PathId;
                return who.IsReference ? text + " ref " + who.ReferenceName : text;
            }

            string NameOf(BundleSerializedFile file, BundlePPtr pointer)
            {
                var target = file.FileOf(pointer, out _);
                if (target == null || !target.Objects.TryGetValue(pointer.PathId, out var info)) return "?";
                var key = target.Name + "#" + pointer.PathId;
                if (objectNames.TryGetValue(key, out var cached)) return cached;
                var name = "?";
                try
                {
                    var tree = target.Tree(info.TypeIdOrIndex);
                    target.Open(info, reader);
                    foreach (var child in tree.Children)
                    {
                        if (child.Kind == BundleTypeTree.Kind.String && child.Name == "m_Name")
                        {
                            name = BundleNames.Clean(ReadLabel(child, 120), 60);
                            break;
                        }
                        Skip(child);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (BundleRefusedException e) when (e.Rule == BundleRule.TooLarge)
                {
                    throw; // the budget ran out: not a naming problem
                }
                catch (Exception)
                {
                    name = "?";
                }
                if (objectNames.Count < 4096) objectNames[key] = name;
                return name;
            }

            static string PathText(string[] fieldPath, int index)
            {
                var text = new StringBuilder();
                foreach (var part in fieldPath)
                {
                    if (part == "[]") text.Append("[]");
                    else text.Append(text.Length > 0 ? "." : "").Append(BundleNames.Clean(part, 40));
                }
                return text.Append('[').Append(index).Append(']').ToString();
            }

            /// <summary>
            /// Behaves like a component (receives callbacks while the scene runs): a MonoBehaviour or StateMachineBehaviour
            /// type, anything that is not a ScriptableObject, or any object that names a GameObject.
            /// </summary>
            static bool IsComponentLike(Type type, Behaviour behaviour) =>
                !behaviour.GameObject.IsNull || typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type)
                || typeof(UnityEngine.StateMachineBehaviour).IsAssignableFrom(type) || !typeof(UnityEngine.ScriptableObject).IsAssignableFrom(type);

            void Judge()
            {
                // Scripts: denied identities are refused wherever they appear (another bundle could use them).
                foreach (var map in scripts.Values)
                {
                    foreach (var script in map.Values)
                    {
                        BundleCheckBudget.CheckCurrent();
                        if (script.Candidates.Length == 0) continue; // a missing script: inert
                        script.Class2 = BundleCheckPolicy.Worst(script.Candidates);
                        if (script.Class2 != BundleCheckPolicy.ScriptClass.Denied) continue;
                        var playerInput = true;
                        foreach (var type in script.Candidates) playerInput &= IsPlayerInput(type);
                        if (playerInput && options.NarrowPatterns == BundleSeverity.Warn)
                            Report(BundleRule.DeniedScript, "", "warning: " + script.Identity + " (PlayerInput, which the runtime guard removes at "
                                                              + "sceneLoaded; before that it only pairs devices and sends fixed-name messages to its own GameObject)");
                        else
                            Refuse(BundleRule.DeniedScript, "", script.Identity + " resolves to the app's " + BundleNames.Clean(script.Type?.FullName));
                    }
                }

                // Behaviours: host components and ScriptableObjects, PlayerInput by name, AnimationEvent receivers.
                var receivers = new HashSet<Type>();
                var playerInputs = BundleTypeResolver.ResolveTypeName("UnityEngine.InputSystem.PlayerInput");
                foreach (var map in behaviours.Values)
                {
                    foreach (var behaviour in map.Values)
                    {
                        BundleCheckBudget.CheckCurrent();
                        var who = new Owner { File = behaviour.File, PathId = behaviour.PathId, Kind = OwnerKind.Behaviour };
                        if (behaviour.Script.IsNull) continue; // no script at all: nothing binds
                        if (behaviour.File.IsBuiltin(behaviour.Script))
                        {
                            // One of the engine's own scripts (UI Toolkit's UIDocument, VisualTreeAsset...): its data is judged as live.
                            Report(BundleRule.ExternalScript, Where(who), "script in " + BundleNames.Clean(behaviour.File.ExternalName(behaviour.Script.FileId), 80));
                            continue;
                        }
                        var script = ScriptAt(behaviour.File, behaviour.Script);
                        if (script == null || script.Candidates.Length == 0) continue;
                        var componentLike = false;
                        foreach (var type in script.Candidates) componentLike |= IsComponentLike(type, behaviour);
                        if (componentLike)
                        {
                            foreach (var type in script.Candidates)
                            {
                                if (typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type)) receivers.Add(type);
                            }
                            if (script.Class2 == BundleCheckPolicy.ScriptClass.Unlisted)
                                Refuse(BundleRule.HostComponent, Where(who), script.Identity + " is an app component outside the world-facing lists");
                        }
                        else if (script.Class2 == BundleCheckPolicy.ScriptClass.Unlisted)
                        {
                            var allowed = true;
                            foreach (var type in script.Candidates) allowed &= BundleCheckPolicy.IsAllowedScriptableObject(type);
                            if (allowed) Report(BundleRule.UnlistedScript, "", script.Identity + " (ScriptableObject on the allowed list)");
                            else Refuse(BundleRule.UnlistedScript, Where(who), script.Identity + " is an app ScriptableObject outside the world-facing lists (it runs at load)");
                        }
                        if (playerInputs != null && playerInputs.Length > 0 && script.Type != null && IsAny(script.Type, playerInputs))
                        {
                            // A missing field means Unity's default, SendMessages.
                            var mode = behaviour.NotificationBehavior ?? 0;
                            if (mode == 0 || mode == 1)
                            {
                                var text = "PlayerInput in " + (mode == 0 ? "SendMessages" : "BroadcastMessages") + " mode";
                                if (options.NarrowPatterns == BundleSeverity.Warn)
                                    Report(BundleRule.PlayerInputMessages, Where(who), "warning: " + text + "; the runtime guard switches it to C# events at sceneLoaded "
                                                                                       + "(earlier messages are fixed names to its own GameObject; action messages need input)");
                                else
                                    Refuse(BundleRule.PlayerInputMessages, Where(who), text);
                            }
                        }
                    }
                }

                // [SerializeReference] values: their types must be content (Unity creates every value in the registry).
                foreach (var reference in references)
                {
                    BundleCheckBudget.CheckCurrent();
                    if (HolderState(BehaviourAt(reference.Holder.File, reference.Holder.PathId), out _, out _) == OwnerState.Inert) continue;
                    if (reference.Candidates.Length == 0)
                    {
                        if (reference.Unjudgeable) Refuse(BundleRule.HostReference, Where(reference.Holder), "[SerializeReference] type " + reference.Name + " cannot be judged");
                        continue;
                    }
                    var verdict = BundleCheckPolicy.Worst(reference.Candidates);
                    if (verdict != BundleCheckPolicy.ScriptClass.Content)
                        Refuse(BundleRule.HostReference, Where(reference.Holder), "[SerializeReference] type " + reference.Name + " is "
                                                                                  + (verdict == BundleCheckPolicy.ScriptClass.Denied ? "a denied host type" : "outside the world-facing lists"));
                }

                foreach (var call in calls)
                {
                    BundleCheckBudget.CheckCurrent();
                    JudgeCall(call);
                }

                foreach (var variant in variants)
                {
                    BundleCheckBudget.CheckCurrent();
                    // Clips are native: Unity reads every field of theirs. Behaviours only when their data is deserialized.
                    if (variant.Owner.Kind == OwnerKind.Behaviour && StateOf(variant.Owner, out _, out _) == OwnerState.Inert) continue;
                    Refuse(BundleRule.TypeTreeMismatch, Where(variant.Owner), "field " + BundleNames.Clean(variant.Value, 80) + " differs from a judged name only by case");
                }

                foreach (var callback in callbacks)
                {
                    BundleCheckBudget.CheckCurrent();
                    if (callback.Value.Length == 0) continue;
                    if (StateOf(callback.Owner, out _, out _) == OwnerState.Inert) continue; // the holder is never deserialized
                    Refuse(BundleRule.NamedCallback, Where(callback.Owner), PathText(callback.Path, 0) + ".MethodName set (called by name)");
                }

                foreach (var graph in graphs)
                {
                    BundleCheckBudget.CheckCurrent();
                    if (StateOf(graph.Owner, out _, out _) == OwnerState.Inert) continue;
                    var where = Where(graph.Owner);
                    if (graph.Graph.Unreadable) Refuse(BundleRule.GraphUnreadable, where, "graph data unreadable (" + BundleNames.Clean(graph.Graph.Error, 60) + ")");
                    foreach (var method in graph.Graph.PersistentCalls) Refuse(BundleRule.GraphUnityEvent, where, "UnityEvent persistent call to " + BundleNames.Clean(method, 80));
                    foreach (var function in graph.Graph.AnimationEvents) Refuse(BundleRule.GraphAnimationEvent, where, "AnimationEvent value " + BundleNames.Clean(function, 80));
                    foreach (var typeName in graph.Graph.Types)
                    {
                        BundleCheckBudget.CheckCurrent();
                        JudgeGraphType(where, typeName, false);
                    }
                    foreach (var pair in graph.Graph.TypedContents)
                    {
                        BundleCheckBudget.CheckCurrent();
                        if (BundleGraphTypes.IsTypeOfType(pair.Key)) JudgeGraphType(where, pair.Value, true);
                    }
                }

                foreach (var animationEvent in animationEvents)
                {
                    BundleCheckBudget.CheckCurrent();
                    var verdict = BundleEventRules.JudgeAnimationEvent(animationEvent.Value, receivers, out var member);
                    var where = Where(animationEvent.Owner);
                    switch (verdict)
                    {
                        case BundleEventRules.Verdict.ByName:
                        case BundleEventRules.Verdict.Static:
                        case BundleEventRules.Verdict.NonPublic:
                            Refuse(BundleRule.AnimationEvent, where, member + " (" + verdict + ")");
                            break;
                        case BundleEventRules.Verdict.HiddenName:
                            if (options.RefuseHiddenAnimationEventNames) Refuse(BundleRule.AnimationEvent, where, member + " (non-public or static on an app behaviour)");
                            else Report(BundleRule.AnimationEventReported, where, member + " (non-public or static on an app behaviour; the runtime guard drops it)");
                            break;
                        case BundleEventRules.Verdict.NotOnAllowList:
                            if (options.EnforceAllowList) Refuse(BundleRule.AnimationEvent, where, member + " (not on the allow-list)");
                            else Report(BundleRule.AnimationEventReported, where, member + " (not on the allow-list)");
                            break;
                        case BundleEventRules.Verdict.NoReceiver:
                            Report(BundleRule.AnimationEventReported, where, member + " (no receiver in the bundle)");
                            break;
                    }
                }
            }

            void JudgeGraphType(string where, string typeName, bool asTypeValue)
            {
                var key = (asTypeValue ? "v|" : "t|") + typeName;
                if (!graphTypeVerdicts.TryGetValue(key, out var known))
                {
                    var verdict = BundleGraphTypes.Judge(typeName, asTypeValue, out var detail);
                    known = new KeyValuePair<BundleGraphTypes.Verdict, string>(verdict, detail);
                    if (graphTypeVerdicts.Count < 65536) graphTypeVerdicts[key] = known; // a memo, bounded: past it names are judged again
                }
                if (known.Key != BundleGraphTypes.Verdict.Refused && known.Key != BundleGraphTypes.Verdict.Unjudgeable) return;
                Refuse(BundleRule.GraphType, where, (asTypeValue ? "System.Type value " : "type ") + BundleNames.Clean(typeName, 100) + " (" + known.Value + ")");
            }

            // ---------------------------------------------------------- the two narrow patterns (NarrowPatterns)
            //
            // Both appear in live worlds as authoring choices, and neither can fire before the runtime guard runs at
            // sceneLoaded (which neutralises both), so by default world bundles pass with a warning (kits refuse them):
            // - a by-name persistent call (SendMessage...) on an InputField/TMP_InputField submit or end-edit event: those
            //   fire only from the user's input, or from deactivating a field the user focused, all after activation
            //   (roll20: InputField.onSubmit -> SendMessage). Any other sender, TMP_InputField's onValueChanged included
            //   (its line-limit setter can fire it during activation), is refused;
            // - PlayerInput (the guard's deny list, and its SendMessages/BroadcastMessages mode): the guard forces C# events
            //   and removes it at sceneLoaded; before that it only sends the fixed-name OnControlsChanged/OnDeviceLost/
            //   OnDeviceRegained to its own GameObject, while action messages need input (ernst2).
            // Host MonoScripts are refused, so in both cases the only receivers are content components.

            static readonly string[] userOnlyInputFieldEvents = { "m_OnSubmit", "m_OnDidEndEdit", "m_OnEndEdit", "onSubmit", "m_EndEdit" };

            static bool IsUserOnlyInputFieldEvent(Type ownerType, string[] fieldPath)
            {
                if (ownerType == null || fieldPath.Length != 1 || Array.IndexOf(userOnlyInputFieldEvents, fieldPath[0]) < 0) return false;
                for (var type = ownerType; type != null; type = type.BaseType)
                {
                    if (type.FullName == "UnityEngine.UI.InputField" || type.FullName == "TMPro.TMP_InputField") return true;
                }
                return false;
            }

            static bool IsPlayerInput(Type type)
            {
                for (var current = type; current != null; current = current.BaseType)
                {
                    if (current.FullName == "UnityEngine.InputSystem.PlayerInput") return true;
                }
                return false;
            }

            static bool IsAny(Type type, Type[] bases)
            {
                foreach (var candidate in bases)
                {
                    if (candidate.IsAssignableFrom(type)) return true;
                }
                return false;
            }

            void JudgeCall(Call call)
            {
                if (string.IsNullOrEmpty(call.Method)) return; // never valid (PersistentCall.IsValid)
                var state = StateOf(call.Owner, out var ownerType, out var ambiguousOwner);
                var site = PathText(call.Path, call.Index);
                if (state == OwnerState.Inert)
                {
                    Report(BundleRule.CallInert, Where(call.Owner), site + " (its component's script is not in the app)");
                    return;
                }
                var label = (ownerType != null ? BundleNames.Clean(ownerType.FullName) : "a built-in script") + "." + site;
                var method = BundleNames.Clean(call.Method, 80);
                // Unknown or ambiguous owners: the event's argument types are unknown, so every arity is judged.
                var eventArguments = ownerType == null || ambiguousOwner ? null : BundleEventRules.EventArguments(ownerType, call.Path);

                if (!call.Argument.IsNull)
                {
                    call.Owner.File.FileOf(call.Argument, out var argumentOutside);
                    if (argumentOutside)
                        Report(BundleRule.ExternalReference, Where(call.Owner), label + " argument in " + BundleNames.Clean(call.Owner.File.ExternalName(call.Argument.FileId), 80));
                }

                var targetTypes = TargetTypes(call, out var targetState, out var outsideName);
                if (targetState == TargetState.Outside)
                {
                    Refuse(BundleRule.CallOutsideTarget, Where(call.Owner), label + " -> ." + method + " on an object in " + BundleNames.Clean(outsideName, 80));
                    return;
                }
                if (targetState == TargetState.Unresolved)
                {
                    Refuse(BundleRule.CallUnresolvedTarget, Where(call.Owner), label + " -> ." + method + " on a target whose type is not in the app");
                    return;
                }
                if (targetState == TargetState.None)
                {
                    // Unity takes the type from the stored name; only a static can then be called.
                    var named = BundleTypeResolver.ResolveTypeName(call.TypeName);
                    if (named == null)
                    {
                        Refuse(BundleRule.CallNoTarget, Where(call.Owner), label + " -> ." + method + " with no target and a type name the checker cannot follow");
                        return;
                    }
                    foreach (var type in named)
                    {
                        foreach (var candidate in BundleEventRules.PossibleMethods(type, call.Method, call.Mode, call.ArgumentTypeName, eventArguments, out _))
                        {
                            if (candidate.IsStatic)
                            {
                                Refuse(BundleRule.CallNoTarget, Where(call.Owner), label + " -> " + Member(candidate) + " (no target; static)");
                                return;
                            }
                        }
                    }
                    Report(BundleRule.CallDead, Where(call.Owner), label + " -> ." + method + " (no target; Unity binds nothing)");
                    return;
                }

                foreach (var targetType in targetTypes)
                {
                    var methods = BundleEventRules.PossibleMethods(targetType, call.Method, call.Mode, call.ArgumentTypeName, eventArguments, out var approximate);
                    if (methods.Count == 0)
                    {
                        Report(BundleRule.CallUnresolved, Where(call.Owner), label + " -> " + BundleNames.Clean(targetType.FullName) + "." + method + " (does not resolve; Unity binds nothing)");
                    }
                    foreach (var candidate in methods)
                    {
                        var verdict = BundleEventRules.JudgeMethod(candidate);
                        var text = label + " -> " + Member(candidate) + (approximate ? " (by name, arguments unknown)" : "");
                        switch (verdict)
                        {
                            case BundleEventRules.Verdict.Static:
                                Refuse(BundleRule.CallStatic, Where(call.Owner), text);
                                break;
                            case BundleEventRules.Verdict.NonPublic:
                                Refuse(BundleRule.CallNonPublic, Where(call.Owner), text);
                                break;
                            case BundleEventRules.Verdict.ByName:
                                if (IsUserOnlyInputFieldEvent(ownerType, call.Path) && options.NarrowPatterns == BundleSeverity.Warn)
                                    Report(BundleRule.CallByName, Where(call.Owner), "warning: " + text + " (calls methods by name, but only on the user's submit/end-edit, "
                                                                                     + "after activation; the runtime guard disables it at sceneLoaded)");
                                else
                                    Refuse(BundleRule.CallByName, Where(call.Owner), text + " (calls methods by name)");
                                break;
                            default:
                                if (!BundleEventRules.IsListed(targetType, candidate))
                                {
                                    var unlisted = label + " -> " + BundleNames.Clean(targetType.FullName) + "." + BundleNames.Clean(candidate.Name) + " (not on the allow-list)";
                                    if (options.EnforceAllowList) Refuse(BundleRule.CallNotOnAllowList, Where(call.Owner), unlisted);
                                    else Report(BundleRule.CallNotOnAllowList, Where(call.Owner), unlisted);
                                }
                                break;
                        }
                    }
                }

                // Once the target is destroyed Unity resolves the stored type name instead.
                if (string.IsNullOrEmpty(call.TypeName)) return;
                var namedTypes = BundleTypeResolver.ResolveTypeName(call.TypeName);
                if (namedTypes == null)
                {
                    Refuse(BundleRule.CallTypeName, Where(call.Owner), label + " -> ." + method + ": stored type name the checker cannot follow ("
                                                                       + BundleNames.Clean(TypePart(call.TypeName), 80) + ")");
                    return;
                }
                var differs = false;
                foreach (var targetType in targetTypes)
                {
                    if (namedTypes.Length == 0 && !SameIdentity(call.TypeName, targetType)) differs = true;
                    foreach (var type in namedTypes)
                    {
                        if (!BundleTypeResolver.IsSameOrBase(type, targetType)) differs = true;
                    }
                }
                foreach (var type in namedTypes)
                {
                    foreach (var candidate in BundleEventRules.PossibleMethods(type, call.Method, call.Mode, call.ArgumentTypeName, eventArguments, out _))
                    {
                        if (!candidate.IsStatic) continue;
                        Refuse(BundleRule.CallTypeName, Where(call.Owner), label + " -> " + Member(candidate) + " (static through the stored type name)");
                        return;
                    }
                }
                if (differs)
                    Report(BundleRule.CallTypeNameDiffers, Where(call.Owner), label + ": stored type " + BundleNames.Clean(TypePart(call.TypeName)) + " vs target "
                                                                             + BundleNames.Clean(targetTypes[0].FullName));
            }

            static string TypePart(string assemblyQualified) => BundleTypeResolver.TopLevelName(assemblyQualified) ?? "?";

            static bool SameIdentity(string typeName, Type type) => TypePart(typeName) == type.FullName;

            static string Member(MethodInfo method) => BundleNames.Clean(method.DeclaringType?.FullName) + "." + BundleNames.Clean(method.Name);

            enum TargetState
            {
                Found,
                None,
                Outside,
                Unresolved,
            }

            /// <summary>
            /// The target's possible runtime types: a behaviour's script candidates (a missing script binds as the bare base),
            /// or the native class of its class id (never its tree's root name).
            /// </summary>
            Type[] TargetTypes(Call call, out TargetState state, out string outsideName)
            {
                outsideName = null;
                if (call.Target.IsNull)
                {
                    state = TargetState.None;
                    return Array.Empty<Type>();
                }
                var file = call.Owner.File.FileOf(call.Target, out var outside);
                if (outside)
                {
                    state = TargetState.Outside;
                    outsideName = call.Owner.File.ExternalName(call.Target.FileId);
                    return Array.Empty<Type>();
                }
                if (!file.Objects.TryGetValue(call.Target.PathId, out var info))
                {
                    state = TargetState.None; // a dangling reference loads as null
                    return Array.Empty<Type>();
                }
                state = TargetState.Found;
                if (file.ClassOf(info) == BundleSerializedFile.ClassMonoBehaviour)
                {
                    var behaviour = BehaviourAt(file, info.PathId);
                    switch (HolderState(behaviour, out _, out _))
                    {
                        case OwnerState.Live:
                            return ScriptAt(file, behaviour.Script).Candidates;
                        case OwnerState.LiveUnknown:
                            state = TargetState.Unresolved; // a built-in script's methods cannot be judged
                            return Array.Empty<Type>();
                        default:
                            // A missing script binds as the bare native class: MonoBehaviour, whatever m_GameObject says
                            // (Unity's class 114 is both); judged as either.
                            return new[] { typeof(UnityEngine.MonoBehaviour), typeof(UnityEngine.ScriptableObject) };
                    }
                }
                var type = BundleTypeResolver.ResolveNative(file.NativeNameOf(info));
                if (type == null)
                {
                    state = TargetState.Unresolved;
                    return Array.Empty<Type>();
                }
                return new[] { type };
            }
        }
    }
}
