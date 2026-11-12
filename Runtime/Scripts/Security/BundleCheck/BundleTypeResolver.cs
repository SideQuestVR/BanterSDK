using System;
using System.Collections.Generic;
using System.Reflection;

namespace BS.Security
{
    /// <summary>
    /// Finds the app's real types for names a bundle carries: MonoScript identities, [SerializeReference] types, native
    /// class names, the assembly-qualified strings in persistent calls and the type names in graph JSON. Reflection only
    /// (no UnityEngine API), thread-safe. Every lookup is a SUPERSET of Unity's own: what Unity could bind is among the
    /// candidates returned, so judging every candidate judges what Unity binds.
    /// </summary>
    /// <remarks>
    /// The caches hold names a bundle chose, so each is bounded (<see cref="MaxCacheEntries"/>): when full it is emptied,
    /// never kept growing for the life of the app.
    /// </remarks>
    internal static class BundleTypeResolver
    {
        /// <summary>Most entries any one name cache keeps before it is emptied.</summary>
        internal const int MaxCacheEntries = 4096;

        /// <summary>Longest type name judged (an editor-truncated generic name is a few hundred characters).</summary>
        internal const int MaxTypeNameLength = 16 * 1024;

        static readonly object gate = new object();
        static Dictionary<string, Assembly> assemblies;
        static Dictionary<string, List<Assembly>> assembliesIgnoringCase;
        static int assemblyCount;
        static Dictionary<string, Type> nativeTypes;
        static Dictionary<string, Type[]> fullNames = new Dictionary<string, Type[]>(StringComparer.Ordinal);
        static Dictionary<string, Type[]> scripts = new Dictionary<string, Type[]>(StringComparer.Ordinal);
        static Dictionary<string, Type[]> serialized = new Dictionary<string, Type[]>(StringComparer.Ordinal);
        static Dictionary<string, List<Type>> movedFrom;

        /// <summary>Forgets everything (the app's assembly set changed, or a test).</summary>
        internal static void Reset()
        {
            lock (gate)
            {
                assemblies = null;
                assembliesIgnoringCase = null;
                nativeTypes = null;
                movedFrom = null;
                movedFromByName = null;
                typesByFullName = null;
                fullNames = new Dictionary<string, Type[]>(StringComparer.Ordinal);
                scripts = new Dictionary<string, Type[]>(StringComparer.Ordinal);
                serialized = new Dictionary<string, Type[]>(StringComparer.Ordinal);
            }
        }

        /// <summary>Entries currently cached (tests: the caches stay bounded).</summary>
        internal static int CachedNames
        {
            get
            {
                lock (gate)
                {
                    return fullNames.Count + scripts.Count + serialized.Count;
                }
            }
        }

        static void Remember<T>(Dictionary<string, T> cache, string key, T value)
        {
            if (cache.Count >= MaxCacheEntries) cache.Clear();
            cache[key] = value;
        }

        static Dictionary<string, Assembly> Assemblies()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            if (assemblies != null && loaded.Length == assemblyCount) return assemblies;
            var map = new Dictionary<string, Assembly>(StringComparer.Ordinal);
            var folded = new Dictionary<string, List<Assembly>>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in loaded)
            {
                try
                {
                    if (assembly.IsDynamic) continue;
                    var name = assembly.GetName().Name;
                    if (name == null) continue;
                    if (!map.ContainsKey(name)) map.Add(name, assembly);
                    if (!folded.TryGetValue(name, out var list)) folded[name] = list = new List<Assembly>();
                    if (!list.Contains(assembly)) list.Add(assembly);
                }
                catch (Exception)
                {
                    // an assembly that cannot even name itself contributes no types
                }
            }
            assemblies = map;
            assembliesIgnoringCase = folded;
            assemblyCount = loaded.Length;
            fullNames = new Dictionary<string, Type[]>(StringComparer.Ordinal);
            scripts = new Dictionary<string, Type[]>(StringComparer.Ordinal);
            serialized = new Dictionary<string, Type[]>(StringComparer.Ordinal);
            nativeTypes = null;
            movedFrom = null;
            movedFromByName = null;
            typesByFullName = null;
            return map;
        }

        static Type[] TypesOf(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types ?? Array.Empty<Type>();
            }
            catch (Exception)
            {
                return Array.Empty<Type>();
            }
        }

        /// <summary>
        /// The assembly simple name a MonoScript or reference names: a path's file part, without ".dll"/".exe" (Unity
        /// stores "UnityEngine.UI.dll"; a path-like name is reduced the same way so it cannot slip past the lists).
        /// </summary>
        internal static string AssemblySimpleName(string assemblyName)
        {
            assemblyName = assemblyName ?? "";
            var slash = Math.Max(assemblyName.LastIndexOf('/'), assemblyName.LastIndexOf('\\'));
            if (slash >= 0) assemblyName = assemblyName.Substring(slash + 1);
            if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || assemblyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
            return assemblyName;
        }

        static void AddType(List<Type> list, Type type)
        {
            if (type != null && !list.Contains(type)) list.Add(type);
        }

        /// <summary>
        /// Every type in the named assembly (matched case-insensitively) whose full name matches, exactly or
        /// case-insensitively, with nested classes written either "Outer/Inner" or "Outer+Inner".
        /// </summary>
        static List<Type> Lookup(string assemblyName, string ns, string className)
        {
            var list = new List<Type>();
            var fullName = (string.IsNullOrEmpty(ns) ? className : ns + "." + className).Replace('/', '+');
            Assemblies();
            if (assembliesIgnoringCase.TryGetValue(AssemblySimpleName(assemblyName), out var candidates))
            {
                foreach (var assembly in candidates)
                {
                    try
                    {
                        AddType(list, assembly.GetType(fullName, false, false));
                        AddType(list, assembly.GetType(fullName, false, true));
                    }
                    catch (Exception)
                    {
                        // a name this assembly's loader rejects: no type
                    }
                }
            }
            if (MovedFrom().TryGetValue(AssemblySimpleName(assemblyName) + "|" + fullName, out var moved))
            {
                foreach (var type in moved) AddType(list, type);
            }
            return list;
        }

        /// <summary>
        /// Every loaded type with this full name (exactly or case-insensitively) in ANY assembly, and every type that moved
        /// from it from any assembly: for a MonoScript whose assembly name is empty, wrong or names an assembly without the
        /// class (Unity's lookup may fall back to other assemblies; the checker must not take such a script as missing).
        /// </summary>
        static List<Type> LookupEverywhere(string ns, string className)
        {
            var list = new List<Type>();
            var fullName = (string.IsNullOrEmpty(ns) ? className : ns + "." + className).Replace('/', '+');
            if (TypesByFullName().TryGetValue(fullName, out var named))
            {
                foreach (var type in named) AddType(list, type);
            }
            if (MovedFromByName().TryGetValue(fullName, out var moved))
            {
                foreach (var type in moved) AddType(list, type);
            }
            return list;
        }

        static Dictionary<string, List<Type>> typesByFullName;

        /// <summary>
        /// Every loaded type by full name, case-insensitively: the app's own names (never a bundle's), built once per
        /// assembly set, so a case-insensitive lookup across all assemblies costs one dictionary probe.
        /// </summary>
        static Dictionary<string, List<Type>> TypesByFullName()
        {
            if (typesByFullName != null) return typesByFullName;
            var map = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in Assemblies().Values)
            {
                BundleCheckBudget.CheckCurrent();
                foreach (var type in TypesOf(assembly))
                {
                    var name = type?.FullName;
                    if (name == null) continue;
                    if (!map.TryGetValue(name, out var list)) map[name] = list = new List<Type>(1);
                    list.Add(type);
                }
            }
            return typesByFullName = map;
        }

        static Dictionary<string, List<Type>> movedFromByName;

        /// <summary>The [MovedFrom] table keyed by the old full name alone (any old assembly), case-insensitively.</summary>
        static Dictionary<string, List<Type>> MovedFromByName()
        {
            if (movedFromByName != null) return movedFromByName;
            var map = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in MovedFrom())
            {
                var bar = pair.Key.IndexOf('|');
                var name = bar >= 0 ? pair.Key.Substring(bar + 1) : pair.Key;
                if (!map.TryGetValue(name, out var list)) map[name] = list = new List<Type>();
                foreach (var type in pair.Value)
                {
                    if (!list.Contains(type)) list.Add(type);
                }
            }
            return movedFromByName = map;
        }

        /// <summary>
        /// Every type Unity could bind a MonoScript to (a superset): the class in the named assembly, matched case-insensitively,
        /// nested names either way, path-like or ".dll" assembly names reduced to their simple name, and types that declare
        /// they moved from that identity; when that finds nothing, the class of that name in every loaded assembly. Empty
        /// when the app has no such type anywhere (the script is missing, inert).
        /// </summary>
        internal static Type[] ResolveScriptCandidates(string assemblyName, string ns, string className)
        {
            if (string.IsNullOrEmpty(className)) return Array.Empty<Type>();
            var key = AssemblySimpleName(assemblyName) + "|" + (ns ?? "") + "|" + className;
            lock (gate)
            {
                Assemblies();
                if (scripts.TryGetValue(key, out var found)) return found;
                var list = Lookup(assemblyName, ns, className);
                list.RemoveAll(t => !typeof(UnityEngine.Object).IsAssignableFrom(t));
                if (list.Count == 0)
                {
                    // Nothing in the named assembly (or no such assembly, or none named): every assembly's class of that name.
                    list = LookupEverywhere(ns, className);
                    list.RemoveAll(t => !typeof(UnityEngine.Object).IsAssignableFrom(t));
                }
                found = list.ToArray();
                Remember(scripts, key, found);
                return found;
            }
        }

        /// <summary>The type Unity binds a MonoScript to when the identity matches exactly, else the first candidate; null when missing.</summary>
        internal static Type ResolveScript(string assemblyName, string ns, string className)
        {
            var candidates = ResolveScriptCandidates(assemblyName, ns, className);
            return Best(candidates, ns, className);
        }

        static Type Best(Type[] candidates, string ns, string className)
        {
            if (candidates.Length == 0) return null;
            var exact = (string.IsNullOrEmpty(ns) ? className : ns + "." + className).Replace('/', '+');
            foreach (var type in candidates)
            {
                if (type.FullName == exact) return type;
            }
            return candidates[0];
        }

        /// <summary>
        /// Every type a [SerializeReference] value could be created as (Unity writes nested classes as Outer/Inner): a
        /// superset, as for scripts. Empty when the app has no such type: Unity cannot create the value, so its data is inert.
        /// </summary>
        internal static Type[] ResolveManagedCandidates(string assemblyName, string ns, string className)
        {
            if (string.IsNullOrEmpty(className)) return Array.Empty<Type>();
            var key = "ref|" + AssemblySimpleName(assemblyName) + "|" + (ns ?? "") + "|" + className;
            lock (gate)
            {
                Assemblies();
                if (scripts.TryGetValue(key, out var found)) return found;
                found = Lookup(assemblyName, ns, className).ToArray();
                Remember(scripts, key, found);
                return found;
            }
        }

        internal static Type ResolveManaged(string assemblyName, string ns, string className) =>
            Best(ResolveManagedCandidates(assemblyName, ns, className), ns, className);

        /// <summary>Types carrying [MovedFrom] with a source identity, keyed "assembly|namespace.class" (case-insensitively).</summary>
        static Dictionary<string, List<Type>> MovedFrom()
        {
            if (movedFrom != null) return movedFrom;
            var map = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in assemblies.Values)
            {
                BundleCheckBudget.CheckCurrent();
                foreach (var type in TypesOf(assembly))
                {
                    if (type == null) continue;
                    try
                    {
                        // Cheap filter first: attribute data is only read for the few types that carry the attribute.
                        if (!type.IsDefined(typeof(UnityEngine.Scripting.APIUpdating.MovedFromAttribute), false)) continue;
                        foreach (var data in type.GetCustomAttributesData())
                        {
                            if (data.AttributeType.FullName != "UnityEngine.Scripting.APIUpdating.MovedFromAttribute") continue;
                            string sourceNs = null, sourceAsm = null, sourceClass = null;
                            var args = data.ConstructorArguments;
                            // MovedFrom(bool autoUpdate, string sourceNamespace = null, string sourceAssembly = null, string sourceClassName = null)
                            // or MovedFrom(string sourceNamespace)
                            if (args.Count >= 1 && args[0].ArgumentType == typeof(string)) sourceNs = args[0].Value as string;
                            if (args.Count >= 2 && args[0].ArgumentType == typeof(bool)) sourceNs = args[1].Value as string;
                            if (args.Count >= 3) sourceAsm = args[2].Value as string;
                            if (args.Count >= 4) sourceClass = args[3].Value as string;
                            var ns = sourceNs ?? type.Namespace;
                            var asm = AssemblySimpleName(sourceAsm ?? type.Assembly.GetName().Name);
                            var cls = sourceClass ?? type.Name;
                            var key = asm + "|" + ((string.IsNullOrEmpty(ns) ? cls : ns + "." + cls).Replace('/', '+'));
                            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<Type>();
                            if (!list.Contains(type)) list.Add(type);
                        }
                    }
                    catch (Exception)
                    {
                        // unreadable attributes: no moved-from identity
                    }
                }
            }
            return movedFrom = map;
        }

        /// <summary>The managed type of a native class name ("AudioSource", "Animator", "GameObject"); see <see cref="ResolveNativeClass"/>.</summary>
        internal static Type ResolveNative(string nativeName)
        {
            if (string.IsNullOrEmpty(nativeName)) return null;
            lock (gate)
            {
                Assemblies();
                if (nativeTypes == null)
                {
                    var map = new Dictionary<string, Type>(StringComparer.Ordinal);
                    foreach (var pair in assemblies)
                    {
                        if (!pair.Key.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                        BundleCheckBudget.CheckCurrent();
                        foreach (var type in TypesOf(pair.Value))
                        {
                            if (type == null || !typeof(UnityEngine.Object).IsAssignableFrom(type)) continue;
                            if (map.TryGetValue(type.Name, out var existing) && existing.Namespace == "UnityEngine") continue;
                            map[type.Name] = type;
                        }
                    }
                    nativeTypes = map;
                }
                nativeTypes.TryGetValue(nativeName, out var found);
                return found;
            }
        }

        /// <summary>The managed type of a native class id (the id is what Unity instantiates), or null.</summary>
        internal static Type ResolveNativeClass(int classId) => ResolveNative(BundleSerializedFile.ClassName(classId));

        /// <summary>Whether a name holds control characters (NUL included): native lookups would cut or misread it.</summary>
        internal static bool HasControlCharacters(string value)
        {
            if (value == null) return false;
            foreach (var ch in value)
            {
                if (char.IsControl(ch)) return true;
            }
            return false;
        }

        /// <summary>
        /// The full type name an assembly-qualified string names, without generic arguments or the assembly part
        /// ("Ns.EventMachine`2[[A, a],[B, b]], asm" gives "Ns.EventMachine`2"). Null for syntax this lookup does not
        /// model (arrays, pointers, by-ref, escapes, unbalanced brackets, control characters).
        /// </summary>
        internal static string TopLevelName(string assemblyQualifiedName)
        {
            if (assemblyQualifiedName == null || HasControlCharacters(assemblyQualifiedName)) return null;
            var name = new System.Text.StringBuilder();
            var s = assemblyQualifiedName;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == ',') break;
                if (c == '*' || c == '&' || c == '\\' || c == ']') return null;
                if (c == '[')
                {
                    if (i + 1 >= s.Length || s[i + 1] != '[') return null; // an array suffix
                    var depth = 0;
                    for (; i < s.Length; i++)
                    {
                        if (s[i] == '[') depth++;
                        else if (s[i] == ']' && --depth == 0) break;
                    }
                    // The editor stores names cut at the second comma, so a generic one often ends inside its
                    // arguments ("Ns.EventMachine`2[[Ns.FlowGraph, Asm"): Type.GetType cannot read that, but judging its
                    // definition is a superset of anything a lenient parser could make of it.
                    if (depth != 0) break;
                    // after the generic arguments only the assembly part may follow
                    var rest = s.Substring(i + 1).TrimStart();
                    if (rest.Length > 0 && rest[0] != ',') return null;
                    break;
                }
                name.Append(c);
            }
            var text = name.ToString().Trim();
            return text.Length == 0 ? null : text;
        }

        /// <summary>
        /// Every loaded type a persistent call's type string could name, matched by full name in any assembly (the
        /// assembly part is ignored on purpose: a superset of what Type.GetType finds). A generic instantiation gives
        /// its generic type definition (callers judge it by name and arity). Null when the name uses syntax this lookup
        /// does not model: the caller treats that as unknown.
        /// </summary>
        internal static Type[] ResolveTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName) || assemblyQualifiedName.Trim().Length == 0) return Array.Empty<Type>();
            if (assemblyQualifiedName.Length > MaxTypeNameLength) return null;
            var fullName = TopLevelName(assemblyQualifiedName);
            if (fullName == null) return null;
            lock (gate)
            {
                var map = Assemblies();
                if (fullNames.TryGetValue(fullName, out var found)) return found;
                var list = new List<Type>();
                BundleCheckBudget.CheckCurrent();
                foreach (var assembly in map.Values)
                {
                    try
                    {
                        AddType(list, assembly.GetType(fullName, false));
                    }
                    catch (Exception)
                    {
                        // a malformed name for this assembly's loader: no type
                    }
                }
                found = list.ToArray();
                Remember(fullNames, fullName, found);
                return found;
            }
        }

        /// <summary>
        /// Every type a graph JSON type name ("$type", "formerType", a System.Type value) could deserialize to: the name
        /// looked up in every loaded assembly (as Visual Scripting's RuntimeCodebase does, case-insensitively too), then
        /// RuntimeCodebase itself for its renamed-type table. Empty when nothing has that name (FullSerializer then builds
        /// nothing from it). Null when the name cannot be judged (control characters, too long, or the lookup failed).
        /// </summary>
        internal static Type[] ResolveSerializedType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return Array.Empty<Type>();
            if (typeName.Length > MaxTypeNameLength || HasControlCharacters(typeName)) return null;
            lock (gate)
            {
                var map = Assemblies();
                if (serialized.TryGetValue(typeName, out var found)) return found;
                var list = new List<Type>();
                BundleCheckBudget.CheckCurrent();
                // Case-insensitive matches of plain names come from the index; generic names (with their argument lists)
                // still ask each assembly, as RuntimeCodebase does.
                var generic = typeName.IndexOf('[') >= 0;
                if (!generic && TypesByFullName().TryGetValue(typeName, out var named))
                {
                    foreach (var type in named) AddType(list, type);
                }
                foreach (var assembly in map.Values)
                {
                    try
                    {
                        AddType(list, assembly.GetType(typeName, false, false));
                        if (generic) AddType(list, assembly.GetType(typeName, false, true));
                    }
                    catch (Exception)
                    {
                        // a name this assembly's loader rejects: no type from it
                    }
                }
                if (list.Count == 0)
                {
                    try
                    {
                        AddType(list, Type.GetType(typeName, false));
                    }
                    catch (Exception)
                    {
                        // an assembly-qualified name that does not parse: no type
                    }
                }
                if (list.Count == 0)
                {
                    // Visual Scripting's own lookup, for its renamed types (Bolt/Ludiq names, moved namespaces).
                    try
                    {
                        if (Unity.VisualScripting.RuntimeCodebase.TryDeserializeType(typeName, out var renamed)) AddType(list, renamed);
                    }
                    catch (Exception)
                    {
                        return null; // the lookup Unity runs would fail as well, but how is not ours to guess
                    }
                }
                found = list.ToArray();
                Remember(serialized, typeName, found);
                return found;
            }
        }

        /// <summary>Whether <paramref name="named"/> (maybe a generic definition) is the target's type or one of its bases.</summary>
        internal static bool IsSameOrBase(Type named, Type target)
        {
            if (named == null || target == null) return false;
            if (!named.IsGenericTypeDefinition) return named.IsAssignableFrom(target);
            for (var current = target; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == named) return true;
            }
            foreach (var face in target.GetInterfaces())
            {
                if (face.IsGenericType && face.GetGenericTypeDefinition() == named) return true;
            }
            return false;
        }

        /// <summary>Every MonoBehaviour type in the app (for the AnimationEvent hidden-name rule).</summary>
        internal static List<Type> AllBehaviourTypes()
        {
            var result = new List<Type>();
            lock (gate)
            {
                foreach (var assembly in Assemblies().Values)
                {
                    BundleCheckBudget.CheckCurrent();
                    foreach (var type in TypesOf(assembly))
                    {
                        try
                        {
                            if (type != null && typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type)) result.Add(type);
                        }
                        catch (Exception)
                        {
                            // a type whose hierarchy cannot load cannot be a component either
                        }
                    }
                }
            }
            return result;
        }
    }
}
