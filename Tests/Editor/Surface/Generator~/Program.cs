// Offline generator for Tests/Editor/Surface/bs-sdk-surface.json, for when the Unity editor holds the project lock.
//
// The surface (types, members, serialized fields) is read with MetadataLoadContext: metadata only, nothing executed.
// Visual Scripting port keys only exist after Unit.Define() runs, so the units alone are instantiated and defined in a
// separate AssemblyLoadContext over the editor's implementation assemblies. A unit that cannot be defined
// outside Unity gets no U lines here (the EditMode test then reports its ports as additions, never as failures).
//
// Usage, from the Greenfield repo root (the assemblies must be compiled: Library/ScriptAssemblies or an rsp compile):
//   dotnet run --project Packages/com.sidequest.creator-sdk/Tests/Editor/Surface/Generator~ -- [options]
//     --out <file>        write the JSON here (default: print counts only)
//     --bin <dir>         where BS.SDK.dll, BS.SDK.Splines.dll and Banter.VisualScripting.dll are (default Library/ScriptAssemblies)
//     --dag <dir>         Bee dag holding <Assembly>.rsp, for the reference list (default: newest Library/Bee/artifacts/*E.dag)
//     --label <text>      the "generatedFrom" text
//     --no-ports          skip the Unit.Define() pass
//     --runtime-reflection  read members from the runtime-loaded assemblies instead of MetadataLoadContext (cross-check)
//   dotnet run --project ... -- --diff <old.json> <new.json>     print removed / changed / added lines
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BS.SDKEditor.Tests.Surface;

static class Program
{
    static int Main(string[] args)
    {
        string Opt(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        if (args.Contains("--diff"))
        {
            var i = Array.IndexOf(args, "--diff");
            var a = SdkSurface.ReadStringArray(File.ReadAllText(args[i + 1]), "entries");
            var b = SdkSurface.ReadStringArray(File.ReadAllText(args[i + 2]), "entries");
            var d = SdkSurface.Compare(a, b);
            Console.WriteLine($"removed {d.Removed.Count}, changed {d.Changed.Count}, compatible {d.Compatible.Count}, added {d.Added.Count}");
            Console.Write(d.Report(int.MaxValue));
            return 0;
        }

        var root = Directory.GetCurrentDirectory();
        var bin = Path.GetFullPath(Opt("--bin") ?? Path.Combine(root, "Library", "ScriptAssemblies"));
        var dag = Opt("--dag") ?? Directory.GetDirectories(Path.Combine(root, "Library", "Bee", "artifacts"), "*E.dag")
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "BS.SDK.rsp"))).First();
        var names = SdkSurface.DefaultAssemblies;

        // Reference list = the union of the covered assemblies' rsp -r: lines, project assemblies swapped for --bin copies.
        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
            refs[n] = Path.Combine(bin, n + ".dll");
        foreach (var n in names)
        {
            var rsp = Path.Combine(dag, n + ".rsp");
            if (!File.Exists(rsp)) continue;
            foreach (var line in File.ReadLines(rsp))
            {
                if (!line.StartsWith("-r:", StringComparison.Ordinal)) continue;
                var p = line.Substring(3).Trim('"');
                if (!Path.IsPathRooted(p)) p = Path.Combine(root, p);
                var simple = Path.GetFileNameWithoutExtension(p);
                if (simple.EndsWith(".ref", StringComparison.OrdinalIgnoreCase)) simple = simple.Substring(0, simple.Length - 4);
                if (!refs.ContainsKey(simple) && File.Exists(p)) refs[simple] = Path.GetFullPath(p);
            }
        }
        var core = refs.ContainsKey("netstandard") ? "netstandard" : "mscorlib";

        // Keyed by assembly name, not file name: PathAssemblyResolver indexes by file name, so every Bee *.ref.dll
        // ("X.ref.dll" holds assembly "X") would be unresolvable through it.
        using var mlc = new MetadataLoadContext(new NameResolver(refs), core);
        var types = new List<Type>();
        foreach (var n in names)
        {
            var path = refs[n];
            if (!File.Exists(path)) { Console.Error.WriteLine($"missing {path}"); return 2; }
            var asm = mlc.LoadFromAssemblyPath(path);
            try { types.AddRange(asm.GetTypes()); }
            catch (ReflectionTypeLoadException e)
            {
                types.AddRange(e.Types.Where(t => t != null));
                Console.Error.WriteLine($"{n}: {e.LoaderExceptions.Length} types failed to load (first: {e.LoaderExceptions.FirstOrDefault()?.Message})");
            }
        }

        Dictionary<string, List<SdkSurface.Port>> ports = null;
        List<Type> runtimeTypes = null;
        if (!args.Contains("--no-ports"))
            ports = DefineUnits(names, bin, refs, out runtimeTypes);

        Func<Type, IEnumerable<SdkSurface.Port>> provider = null;
        if (ports != null)
            provider = t => ports.TryGetValue(SdkSurface.Name(t), out var l) ? l : null;
        // --runtime-reflection: read the surface from the executed (runtime-loaded) assemblies instead of metadata, the
        // way the EditMode test does inside Unity. A cross-check that both readers produce the same lines.
        var entries = SdkSurface.Collect(args.Contains("--runtime-reflection") && runtimeTypes != null ? runtimeTypes : types, provider);
        var json = SdkSurface.ToJson(Opt("--label") ?? "offline generator", names, entries);
        foreach (var kv in SdkSurface.Counts(entries)) Console.WriteLine($"{kv.Key}: {kv.Value}");
        var outPath = Opt("--out");
        if (outPath != null)
        {
            File.WriteAllText(outPath, json, new System.Text.UTF8Encoding(false));
            Console.WriteLine("wrote " + outPath);
        }
        return 0;
    }

    /// <summary>Instantiates and defines every unit in an isolated load context; returns ports keyed by type name.</summary>
    static Dictionary<string, List<SdkSurface.Port>> DefineUnits(string[] names, string bin, Dictionary<string, string> refs, out List<Type> loadedTypes)
    {
        loadedTypes = new List<Type>();
        // Execution needs implementation assemblies: the editor's ScriptAssemblies instead of Bee ref.dlls, and the
        // .NET runtime's own BCL instead of Unity's netstandard reference facades.
        var impl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in refs)
        {
            var p = kv.Value;
            if (p.Contains(Path.DirectorySeparatorChar + "NetStandard" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                p.Contains("/NetStandard/", StringComparison.OrdinalIgnoreCase) || kv.Key == "netstandard" || kv.Key == "mscorlib")
                continue;
            if (p.EndsWith(".ref.dll", StringComparison.OrdinalIgnoreCase))
            {
                var candidates = new[] { Path.Combine(bin, kv.Key + ".dll"), Path.Combine(Directory.GetCurrentDirectory(), "Library", "ScriptAssemblies", kv.Key + ".dll") };
                p = candidates.FirstOrDefault(File.Exists);
                if (p == null) continue;
            }
            impl[kv.Key] = p;
        }
        var ctx = new UnitContext(impl);
        var result = new Dictionary<string, List<SdkSurface.Port>>(StringComparer.Ordinal);
        int defined = 0, failed = 0;
        var failures = new List<string>();
        foreach (var n in names)
        {
            Assembly asm;
            try { asm = ctx.LoadFromAssemblyPath(impl[n]); }
            catch (Exception e) { failures.Add($"{n}: {e.Message}"); continue; }
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            loadedTypes.AddRange(types);
            foreach (var t in types)
            {
                if (t.IsAbstract || t.ContainsGenericParameters || !SdkSurface.DerivesFrom(t, "Unity.VisualScripting.Unit"))
                    continue;
                try
                {
                    var unit = Activator.CreateInstance(t, nonPublic: true);
                    var iunit = t.GetInterface("Unity.VisualScripting.IUnit");
                    iunit.GetMethod("EnsureDefined").Invoke(unit, null);
                    if ((bool)iunit.GetProperty("failedToDefine").GetValue(unit))
                    {
                        var ex = iunit.GetProperty("definitionException").GetValue(unit) as Exception;
                        failed++;
                        failures.Add($"{t.FullName}: {ex?.GetType().Name} {ex?.Message}");
                        continue;
                    }
                    var list = new List<SdkSurface.Port>();
                    void Read(string collection, string kind, bool value)
                    {
                        var ports = (System.Collections.IEnumerable)iunit.GetProperty(collection).GetValue(unit);
                        foreach (var p in ports)
                        {
                            var pt = p.GetType();
                            list.Add(new SdkSurface.Port
                            {
                                Kind = kind,
                                Key = (string)pt.GetProperty("key").GetValue(p),
                                ValueType = value ? (Type)pt.GetProperty("type").GetValue(p) : null,
                            });
                        }
                    }
                    Read("controlInputs", "ci", false);
                    Read("controlOutputs", "co", false);
                    Read("valueInputs", "vi", true);
                    Read("valueOutputs", "vo", true);
                    result[SdkSurface.Name(t)] = list;
                    defined++;
                }
                catch (Exception e)
                {
                    failed++;
                    var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                    failures.Add($"{t.FullName}: {inner.GetType().Name} {inner.Message}");
                }
            }
        }
        Console.WriteLine($"units defined offline: {defined}, not definable offline: {failed}");
        foreach (var f in failures.Take(40)) Console.WriteLine("  " + f.Split('\n')[0]);
        return result;
    }

    sealed class NameResolver : MetadataAssemblyResolver
    {
        readonly Dictionary<string, string> map;
        public NameResolver(Dictionary<string, string> map) { this.map = map; }
        public override Assembly Resolve(MetadataLoadContext context, AssemblyName name) =>
            map.TryGetValue(name.Name, out var p) ? context.LoadFromAssemblyPath(p) : null;
    }

    sealed class UnitContext : AssemblyLoadContext
    {
        readonly Dictionary<string, string> map;
        public UnitContext(Dictionary<string, string> map) : base("bs-sdk-surface-units", isCollectible: false) { this.map = map; }
        protected override Assembly Load(AssemblyName name) =>
            map.TryGetValue(name.Name, out var p) && File.Exists(p) ? LoadFromAssemblyPath(p) : null;
    }
}
