#if UNITY_EDITOR && GREENFIELD_PROJECT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace BS.Security
{
    /// <summary>
    /// Writes the app's script layout table (<see cref="BundleScriptLayouts"/>) to
    /// Assets/Resources/Generated/BundleScriptLayouts.txt, which the player loads before its first scene.
    /// <para>
    /// How: one uncompressed probe bundle for the build target holds every content script type (a component on its
    /// own inactive GameObject in one prefab, or a ScriptableObject asset) and one value of every class content can hold
    /// in a [SerializeReference] field (<see cref="BundleLayoutProbe"/>). Unity writes each type's properties hash and
    /// tree into it, exactly as for a creator's bundle; the checker's own reader collects them
    /// (<see cref="BundleChecker.CollectLayouts"/>), so the table and the check agree on keys and digests.
    /// </para>
    /// <para>
    /// When: Tools > Bundle Check > Rebuild Script Layout Table, or a build script calling <see cref="Generate"/> before
    /// BuildPlayer (Unity cannot build bundles during a player build, so this cannot run in a preprocess step). The
    /// preprocess check below compares the table's inputs fingerprint (every covered type's serialized fields, by
    /// reflection) with the project's and warns about a missing or stale table, or fails a release build when
    /// <see cref="BundleScriptLayoutsBuildCheck.FailReleaseBuilds"/> is set.
    /// </para>
    /// </summary>
    public static class BundleScriptLayoutsGenerator
    {
        internal const string AssetPath = "Assets/Resources/Generated/BundleScriptLayouts.txt";
        const string ProbeFolderName = "__BundleLayoutProbe__";
        const string ProbeFolder = "Assets/" + ProbeFolderName;
        const string BundleName = "layoutprobe";
        /// <summary>BuildPipeline names its manifest bundle after the output folder, which must not match the bundle's name.</summary>
        const string OutputFolder = "Temp/BundleLayoutProbe/out-probe";
        const int MaxDepth = 10;

        [MenuItem("Tools/Bundle Check/Rebuild Script Layout Table")]
        static void RebuildFromMenu()
        {
            try
            {
                Debug.Log("[BundleCheck] " + Generate(EditorUserBuildSettings.activeBuildTarget));
            }
            catch (Exception e)
            {
                Debug.LogError("[BundleCheck] script layout table not written: " + e);
            }
        }

        /// <summary>
        /// Builds the probe bundle for <paramref name="target"/> and writes the table; returns a summary. Public for the
        /// app's build scripts: call it before BuildPlayer.
        /// </summary>
        public static string Generate(BuildTarget target)
        {
            if (BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Unity cannot build the probe bundle during a player build: generate the table before BuildPlayer");
            var problems = new List<string>();
            var table = BuildTable(target, problems, out var scripts, out var classes);
            var text = BundleScriptLayouts.Format(table);
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath) ?? "Assets/Resources/Generated");
            File.WriteAllText(AssetPath, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            var summary = "script layout table for " + target + ": " + table.Scripts.Count + " of " + scripts + " content script types, "
                          + table.References.Count + " of " + classes + " [SerializeReference] classes, " + text.Length / 1024 + " KiB, fingerprint "
                          + table.Fingerprint + " -> " + AssetPath;
            if (problems.Count > 0)
                Debug.LogWarning("[BundleCheck] " + problems.Count + " type(s) not in the script layout table (their stored trees are taken as they are):\n  "
                                 + string.Join("\n  ", problems.Take(200)));
            return summary;
        }

        /// <summary>The table for <paramref name="target"/>, without writing it (problems: types left out, and why).</summary>
        internal static BundleScriptLayouts.Table BuildTable(BuildTarget target, List<string> problems, out int scriptCount, out int classCount)
        {
            var scripts = ContentScriptTypes();
            var classes = ReferenceClasses(scripts);
            scriptCount = scripts.Count;
            classCount = classes.Count;
            var bundle = BuildProbe(target, scripts, classes, problems);
            var observed = BundleChecker.CollectLayouts(bundle);
            var table = BundleScriptLayouts.Build(observed, Application.unityVersion, target.ToString(), InputsFingerprint(target, scripts, classes), problems);
            foreach (var type in scripts)
            {
                if (!table.Scripts.ContainsKey(BundleScriptLayouts.KeyOf(type))) problems.Add("script " + BundleScriptLayouts.KeyOf(type) + ": not in the probe bundle");
            }
            foreach (var type in classes)
            {
                if (!table.References.ContainsKey(BundleScriptLayouts.KeyOf(type))) problems.Add("reference class " + BundleScriptLayouts.KeyOf(type) + ": not in the probe bundle");
            }
            return table;
        }

        /// <summary>Why the table on disk does not fit a build for <paramref name="target"/>, or null when it does.</summary>
        internal static string Staleness(BuildTarget target)
        {
            if (!File.Exists(AssetPath)) return "missing";
            var table = BundleScriptLayouts.Parse(File.ReadAllText(AssetPath), out var problem);
            if (table == null) return "unreadable (" + problem + ")";
            if (table.UnityVersion != Application.unityVersion) return "generated by Unity " + table.UnityVersion;
            if (table.Target != target.ToString()) return "generated for " + table.Target;
            var scripts = ContentScriptTypes();
            if (table.Inputs != InputsFingerprint(target, scripts, ReferenceClasses(scripts))) return "stale (content script layouts changed since it was generated)";
            return null;
        }

        // ------------------------------------------------------------------ what the table covers

        /// <summary>Player assemblies: no editor-only or test assembly (by compilation pipeline, name or reference).</summary>
        static Func<System.Reflection.Assembly, bool> PlayerAssemblies()
        {
            var editorOnly = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor)) editorOnly.Add(assembly.name);
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)) editorOnly.Remove(assembly.name);
            var verdicts = new Dictionary<System.Reflection.Assembly, bool>();
            return assembly =>
            {
                if (verdicts.TryGetValue(assembly, out var known)) return known;
                var name = assembly.GetName().Name ?? "";
                var player = !editorOnly.Contains(name) && !name.StartsWith("UnityEditor", StringComparison.Ordinal);
                if (player)
                {
                    try
                    {
                        foreach (var reference in assembly.GetReferencedAssemblies())
                        {
                            var referenced = reference.Name ?? "";
                            if (referenced.StartsWith("UnityEditor", StringComparison.Ordinal) || referenced == "nunit.framework") player = false;
                        }
                    }
                    catch (Exception)
                    {
                        player = false;
                    }
                }
                return verdicts[assembly] = player;
            };
        }

        /// <summary>Every concrete MonoBehaviour and ScriptableObject type a world may hold (the content lists, plus the allowed ScriptableObjects).</summary>
        internal static List<Type> ContentScriptTypes()
        {
            var player = PlayerAssemblies();
            var result = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Concat(TypeCache.GetTypesDerivedFrom<ScriptableObject>()))
            {
                if (type.IsAbstract || type.IsGenericType || type.ContainsGenericParameters || !player(type.Assembly)) continue;
                if (BundleCheckPolicy.Classify(type) == BundleCheckPolicy.ScriptClass.Content || BundleCheckPolicy.IsAllowedScriptableObject(type)) result.Add(type);
            }
            result.Sort((a, b) => string.CompareOrdinal(BundleScriptLayouts.KeyOf(a), BundleScriptLayouts.KeyOf(b)));
            return result;
        }

        /// <summary>The fields Unity serializes on a type (its own and its bases', up to MonoBehaviour/ScriptableObject).</summary>
        static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            for (var current = type; current != null && current != typeof(object) && current != typeof(MonoBehaviour)
                                     && current != typeof(ScriptableObject) && current != typeof(UnityEngine.Object); current = current.BaseType)
            {
                FieldInfo[] fields;
                try
                {
                    fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (var field in fields)
                {
                    if (field.IsNotSerialized || field.IsInitOnly || field.IsLiteral) continue;
                    if (field.IsPublic || field.IsDefined(typeof(SerializeField), false) || field.IsDefined(typeof(SerializeReference), false)) yield return field;
                }
            }
        }

        static Type ElementOf(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
            return type;
        }

        static bool IsNested(Type type) =>
            type != null && !type.IsPrimitive && !type.IsEnum && !type.IsPointer && type != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(type)
            && !typeof(Delegate).IsAssignableFrom(type) && (type.IsSerializable || type.IsValueType);

        /// <summary>
        /// Every class content can hold in a [SerializeReference] field: the concrete serializable subclasses of each such
        /// field's declared type, on content scripts, the plain classes and structs they serialize, and those classes
        /// themselves (closure).
        /// </summary>
        internal static List<Type> ReferenceClasses(List<Type> scripts)
        {
            var player = PlayerAssemblies();
            var declared = new HashSet<Type>();
            var visited = new HashSet<Type>();
            void Walk(Type type, int depth)
            {
                if (depth > MaxDepth || type == null || !visited.Add(type)) return;
                foreach (var field in SerializedFields(type))
                {
                    Type fieldType;
                    try
                    {
                        fieldType = ElementOf(field.FieldType);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (field.IsDefined(typeof(SerializeReference), false))
                    {
                        declared.Add(fieldType);
                        continue;
                    }
                    if (IsNested(fieldType)) Walk(fieldType, depth + 1);
                }
            }
            foreach (var script in scripts) Walk(script, 0);
            var classes = new HashSet<Type>();
            var done = new HashSet<Type>();
            var pending = new Queue<Type>(declared);
            while (pending.Count > 0)
            {
                var baseType = pending.Dequeue();
                if (!done.Add(baseType)) continue;
                IEnumerable<Type> candidates = baseType == typeof(object)
                    ? AppDomain.CurrentDomain.GetAssemblies().Where(player).SelectMany(Types)
                    : TypeCache.GetTypesDerivedFrom(baseType).Append(baseType);
                foreach (var candidate in candidates)
                {
                    if (!candidate.IsClass || candidate.IsAbstract || candidate.IsGenericType || candidate.ContainsGenericParameters || !candidate.IsSerializable
                        || typeof(UnityEngine.Object).IsAssignableFrom(candidate) || typeof(Delegate).IsAssignableFrom(candidate) || !player(candidate.Assembly)) continue;
                    if (!classes.Add(candidate)) continue;
                    Walk(candidate, 0);
                    foreach (var nested in declared)
                    {
                        if (!done.Contains(nested)) pending.Enqueue(nested);
                    }
                }
            }
            var result = classes.ToList();
            result.Sort((a, b) => string.CompareOrdinal(BundleScriptLayouts.KeyOf(a), BundleScriptLayouts.KeyOf(b)));
            return result;
        }

        static IEnumerable<Type> Types(System.Reflection.Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }

        /// <summary>
        /// What the table was built from: the Unity version, the target, and every covered type's serialized fields
        /// (name and type, with nested serializable types), by reflection. A change means the layouts may have changed.
        /// </summary>
        internal static string InputsFingerprint(BuildTarget target, List<Type> scripts, List<Type> classes)
        {
            var lines = new List<string>();
            var visited = new HashSet<Type>();
            void Describe(Type type, int depth)
            {
                if (depth > MaxDepth || type == null || !visited.Add(type)) return;
                var line = new StringBuilder(BundleScriptLayouts.KeyOf(type)).Append('=');
                var nested = new List<Type>();
                foreach (var field in SerializedFields(type))
                {
                    Type fieldType;
                    try
                    {
                        fieldType = field.FieldType;
                    }
                    catch (Exception)
                    {
                        line.Append(field.Name).Append(":?;");
                        continue;
                    }
                    line.Append(field.Name).Append(':').Append(fieldType.FullName ?? fieldType.Name);
                    if (field.IsDefined(typeof(SerializeReference), false)) line.Append('&');
                    line.Append(';');
                    var element = ElementOf(fieldType);
                    if (IsNested(element)) nested.Add(element);
                }
                lines.Add(line.ToString());
                foreach (var type2 in nested) Describe(type2, depth + 1);
            }
            foreach (var type in scripts) Describe(type, 0);
            foreach (var type in classes) Describe(type, 0);
            lines.Sort(StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                var text = Application.unityVersion + "\n" + target + "\n" + string.Join("\n", lines);
                return BundleScriptLayouts.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Substring(0, 16);
            }
        }

        // ------------------------------------------------------------------ the probe bundle

        static byte[] BuildProbe(BuildTarget target, List<Type> scripts, List<Type> classes, List<string> problems)
        {
            if (AssetDatabase.IsValidFolder(ProbeFolder)) AssetDatabase.DeleteAsset(ProbeFolder);
            AssetDatabase.CreateFolder("Assets", ProbeFolderName);
            try
            {
                var assets = new List<string>();
                var root = new GameObject("BundleLayoutProbe");
                root.SetActive(false); // no Awake/OnEnable on the probes
                try
                {
                    foreach (var type in scripts.Where(t => typeof(MonoBehaviour).IsAssignableFrom(t)))
                    {
                        var holder = new GameObject(type.FullName);
                        holder.transform.SetParent(root.transform, false);
                        Component added = null;
                        var logging = Debug.unityLogger.logEnabled;
                        try
                        {
                            Debug.unityLogger.logEnabled = false; // AddComponent logs for types without a script file
                            added = holder.AddComponent(type);
                        }
                        catch (Exception e)
                        {
                            problems.Add("script " + BundleScriptLayouts.KeyOf(type) + ": AddComponent threw " + e.GetType().Name);
                        }
                        finally
                        {
                            Debug.unityLogger.logEnabled = logging;
                        }
                        if (added == null)
                        {
                            UnityEngine.Object.DestroyImmediate(holder);
                            problems.Add("script " + BundleScriptLayouts.KeyOf(type) + ": cannot be added as a component");
                        }
                    }
                    var prefab = ProbeFolder + "/Components.prefab";
                    PrefabUtility.SaveAsPrefabAsset(root, prefab, out var saved);
                    if (saved) assets.Add(prefab);
                    else problems.Add("the components prefab could not be saved");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
                var index = 0;
                foreach (var type in scripts.Where(t => typeof(ScriptableObject).IsAssignableFrom(t)))
                {
                    try
                    {
                        var instance = ScriptableObject.CreateInstance(type);
                        if (instance == null || MonoScript.FromScriptableObject(instance) == null)
                        {
                            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                            problems.Add("script " + BundleScriptLayouts.KeyOf(type) + ": no script asset for a ScriptableObject");
                            continue;
                        }
                        var path = ProbeFolder + "/S" + index++ + ".asset";
                        AssetDatabase.CreateAsset(instance, path);
                        assets.Add(path);
                    }
                    catch (Exception e)
                    {
                        problems.Add("script " + BundleScriptLayouts.KeyOf(type) + ": CreateInstance threw " + e.GetType().Name);
                    }
                }
                var probe = ScriptableObject.CreateInstance<BundleLayoutProbe>();
                foreach (var type in classes)
                {
                    object value = null;
                    try
                    {
                        value = Activator.CreateInstance(type, true);
                    }
                    catch (Exception)
                    {
                        try
                        {
                            value = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
                        }
                        catch (Exception e)
                        {
                            problems.Add("reference class " + BundleScriptLayouts.KeyOf(type) + ": cannot be created (" + e.GetType().Name + ")");
                        }
                    }
                    if (value != null) probe.Values.Add(value);
                }
                var references = ProbeFolder + "/References.asset";
                AssetDatabase.CreateAsset(probe, references);
                assets.Add(references);
                AssetDatabase.SaveAssets();

                if (Directory.Exists(OutputFolder)) Directory.Delete(OutputFolder, true);
                Directory.CreateDirectory(OutputFolder);
                var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = assets.ToArray() };
                var manifest = BuildPipeline.BuildAssetBundles(OutputFolder, new[] { build },
                    BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ForceRebuildAssetBundle, target);
                if (manifest == null) throw new InvalidOperationException("BuildPipeline.BuildAssetBundles failed for the probe bundle (" + target + ")");
                return File.ReadAllBytes(Path.Combine(OutputFolder, BundleName));
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(ProbeFolder)) AssetDatabase.DeleteAsset(ProbeFolder);
                if (Directory.Exists(OutputFolder)) Directory.Delete(OutputFolder, true);
            }
        }
    }

    /// <summary>Before a player build: is the script layout table there and current for this target?</summary>
    public sealed class BundleScriptLayoutsBuildCheck : IPreprocessBuildWithReport
    {
        /// <summary>
        /// Fail a release build whose table is missing or stale (development builds only warn). Off until the build
        /// scripts call <see cref="BundleScriptLayoutsGenerator.Generate"/> before BuildPlayer.
        /// </summary>
        public static bool FailReleaseBuilds = false;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var problem = BundleScriptLayoutsGenerator.Staleness(report.summary.platform);
            if (problem == null) return;
            var message = "[BundleCheck] the script layout table (" + BundleScriptLayoutsGenerator.AssetPath + ") is " + problem
                          + ": run Tools > Bundle Check > Rebuild Script Layout Table, or BundleScriptLayoutsGenerator.Generate before BuildPlayer. "
                          + "Without a current table, a world's forged type tree under the app's own script hash is not caught before load.";
            if (FailReleaseBuilds && (report.summary.options & BuildOptions.Development) == 0) throw new BuildFailedException(message);
            Debug.LogWarning(message);
        }
    }
}
#endif
