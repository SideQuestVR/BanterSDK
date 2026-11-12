using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BS.SDKEditor.Tests.Surface
{
    /// <summary>
    /// The public C# surface of the creator SDK as a flat, sorted list of "KEY =&gt; VALUE" lines that a committed
    /// baseline can be diffed against (Bridge 2 plan §7: visual scripting graphs and world bundles bind to these names,
    /// so a rename silently breaks content).
    ///
    /// Plain System.Reflection over <see cref="Type"/> objects and <see cref="CustomAttributeData"/> only, so the same
    /// code runs inside the editor (EditMode test, runtime reflection on Mono) and in the offline generator
    /// (Generator~, MetadataLoadContext, nothing executed). Nothing here may reference UnityEngine.
    ///
    /// Line kinds (KEY =&gt; VALUE):
    /// <list type="bullet">
    /// <item>T type =&gt; visibility, kind, modifiers, base, public interfaces, serializable, selected attributes</item>
    /// <item>C type(params) =&gt; parameter names, ref/out/params, defaults (public constructors)</item>
    /// <item>M type.Name&lt;T&gt;(params) =&gt; static/abstract/virtual, return type, parameter names and defaults</item>
    /// <item>P type.Name[index] =&gt; type, public accessors, static</item>
    /// <item>F type.Name =&gt; type, static/readonly/const value (public fields; enum values are V lines)</item>
    /// <item>E type.Name =&gt; handler type</item>
    /// <item>V enum.Name =&gt; underlying value</item>
    /// <item>S type.name =&gt; a field Unity serializes ("unity"), or a member Visual Scripting serializes in a unit ("vs"),
    /// with [FormerlySerializedAs]/[SerializeAs]/[SerializeReference]</item>
    /// <item>U unit.ci|co|vi|vo.key =&gt; "control" or the value type (ports from Unit.Define; graphs connect by key)</item>
    /// </list>
    /// Public types get T/C/M/P/F/E/V/S/U lines. Non-public types that Unity or Visual Scripting can still serialize by
    /// name (UnityEngine.Object subclasses, [Serializable] types, units) get T and S lines only.
    /// </summary>
    public static class SdkSurface
    {
        public const int Schema = 1;
        public const string Separator = " => ";

        /// <summary>The assemblies the baseline covers, in the order they are reported.</summary>
        public static readonly string[] DefaultAssemblies = { "BS.SDK", "BS.SDK.Splines", "Banter.VisualScripting" };

        public struct Port
        {
            public string Kind;      // ci, co, vi, vo
            public string Key;
            public Type ValueType;   // null for control ports
        }

        // ---------------------------------------------------------------------------------------------------------
        // Collection
        // ---------------------------------------------------------------------------------------------------------

        /// <param name="types">Every type of the covered assemblies (nested types included, as Assembly.GetTypes gives).</param>
        /// <param name="unitPorts">Ports of a unit type, or null when that is unknown here (offline without execution).</param>
        public static List<string> Collect(IEnumerable<Type> types, Func<Type, IEnumerable<Port>> unitPorts = null)
        {
            var lines = new List<string>();
            foreach (var type in types)
            {
                if (type == null || IsCompilerGenerated(type))
                    continue;
                var visible = IsVisible(type);
                var unityObject = DerivesFrom(type, "UnityEngine.Object");
                var serializable = (type.Attributes & TypeAttributes.Serializable) != 0 && !type.IsEnum && !type.IsInterface && !IsDelegate(type);
                var unit = DerivesFrom(type, "Unity.VisualScripting.Unit");
                if (!visible && !unityObject && !serializable && !unit)
                    continue;

                var tn = Name(type);
                lines.Add("T " + tn + Separator + DescribeType(type, visible, serializable));

                if (visible)
                    CollectMembers(type, tn, lines);
                if (unityObject || serializable)
                    CollectUnitySerialized(type, tn, lines);
                if (unit)
                {
                    CollectVsSerialized(type, tn, lines);
                    if (unitPorts != null && !type.IsAbstract && !type.ContainsGenericParameters)
                    {
                        IEnumerable<Port> ports = null;
                        try { ports = unitPorts(type); }
                        catch { ports = null; }
                        if (ports != null)
                            foreach (var p in ports)
                                lines.Add("U " + tn + "." + p.Kind + "." + p.Key + Separator + (p.ValueType == null ? "control" : Name(p.ValueType)));
                    }
                }
            }
            lines.Sort(StringComparer.Ordinal);
            // A type's members can be reported twice only through a bug; keep the list a set.
            return lines.Distinct().ToList();
        }

        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        const BindingFlags DeclaredAll = Declared | BindingFlags.NonPublic;

        static string DescribeType(Type t, bool visible, bool serializable)
        {
            var parts = new List<string>();
            parts.Add(visible ? "public" : (t.IsNested ? "nested-nonpublic" : "internal"));
            string kind;
            if (t.IsEnum) kind = "enum " + Name(Enum_UnderlyingType(t));
            else if (t.IsInterface) kind = "interface";
            else if (IsDelegate(t)) kind = "delegate";
            else if (t.IsValueType) kind = "struct";
            else kind = "class";
            if (kind == "class")
            {
                if (t.IsAbstract && t.IsSealed) kind = "static class";
                else if (t.IsAbstract) kind = "abstract class";
                else if (t.IsSealed) kind = "sealed class";
            }
            parts.Add(kind);
            var bt = t.BaseType;
            if (bt != null && !t.IsEnum && !t.IsValueType && !IsDelegate(t) && bt.FullName != "System.Object")
                parts.Add(": " + Name(bt));
            // Only interfaces this type adds: what a base type brings (System.Enum, MonoBehaviour, Unit) differs between
            // runtimes (Mono vs .NET) and is reported on the base's own line anyway.
            var inherited = new HashSet<string>(bt == null ? Enumerable.Empty<string>() : SafeInterfaces(bt).Select(Name), StringComparer.Ordinal);
            var ifaces = SafeInterfaces(t).Where(IsVisible).Select(Name).Where(n => !inherited.Contains(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (ifaces.Count > 0)
                parts.Add("implements " + string.Join(", ", ifaces));
            if (serializable)
                parts.Add("serializable");
            parts.AddRange(SelectedAttributes(t.GetCustomAttributesData()));
            return string.Join("; ", parts);
        }

        static void CollectMembers(Type type, string tn, List<string> lines)
        {
            if (type.IsEnum)
            {
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (f.IsLiteral)
                        lines.Add("V " + tn + "." + f.Name + Separator + Value(f.GetRawConstantValue()) + Attrs(f));
                return;
            }

            foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                lines.Add("C " + tn + "(" + ParamTypes(c.GetParameters()) + ")" + Separator + "(" + ParamDetails(c.GetParameters()) + ")" + Attrs(c));

            foreach (var m in type.GetMethods(Declared))
            {
                if (m.IsSpecialName && !m.Name.StartsWith("op_", StringComparison.Ordinal))
                    continue; // accessors are reported with their property/event
                if (IsCompilerGenerated(m))
                    continue;
                var generic = m.IsGenericMethodDefinition ? "<" + string.Join(",", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";
                var mods = new List<string>();
                if (m.IsStatic) mods.Add("static");
                if (m.IsAbstract) mods.Add("abstract");
                else if (m.IsVirtual && !m.IsFinal) mods.Add("virtual");
                mods.Add(Name(m.ReturnType));
                lines.Add("M " + tn + "." + m.Name + generic + "(" + ParamTypes(m.GetParameters()) + ")" + Separator
                          + string.Join(" ", mods) + " (" + ParamDetails(m.GetParameters()) + ")" + Attrs(m));
            }

            foreach (var p in type.GetProperties(Declared))
            {
                var get = p.GetGetMethod(false);
                var set = p.GetSetMethod(false);
                if (get == null && set == null)
                    continue;
                var index = p.GetIndexParameters();
                var key = "P " + tn + "." + p.Name + (index.Length > 0 ? "[" + ParamTypes(index) + "]" : "");
                var any = get ?? set;
                lines.Add(key + Separator + (any.IsStatic ? "static " : "") + Name(p.PropertyType) + " {" + (get != null ? " get;" : "") + (set != null ? " set;" : "") + " }" + Attrs(p));
            }

            foreach (var f in type.GetFields(Declared))
            {
                if (f.IsSpecialName || IsCompilerGenerated(f))
                    continue;
                var mods = new List<string>();
                if (f.IsLiteral) mods.Add("const");
                else
                {
                    if (f.IsStatic) mods.Add("static");
                    if (f.IsInitOnly) mods.Add("readonly");
                }
                mods.Add(Name(f.FieldType));
                var v = f.IsLiteral ? " = " + Value(f.GetRawConstantValue()) : "";
                lines.Add("F " + tn + "." + f.Name + Separator + string.Join(" ", mods) + v + Attrs(f));
            }

            foreach (var e in type.GetEvents(Declared))
            {
                var add = e.GetAddMethod(false);
                if (add == null)
                    continue;
                lines.Add("E " + tn + "." + e.Name + Separator + (add.IsStatic ? "static " : "") + Name(e.EventHandlerType) + Attrs(e));
            }
        }

        /// <summary>Fields Unity's serializer writes for a UnityEngine.Object or [Serializable] type (declared here).</summary>
        static void CollectUnitySerialized(Type type, string tn, List<string> lines)
        {
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsStatic || f.IsLiteral || f.IsInitOnly || (f.Attributes & FieldAttributes.NotSerialized) != 0)
                    continue;
                var data = f.GetCustomAttributesData();
                var serializeField = HasAttr(data, "UnityEngine.SerializeField");
                var serializeReference = HasAttr(data, "UnityEngine.SerializeReference");
                if (!f.IsPublic && !serializeField && !serializeReference)
                    continue;
                var extra = new List<string> { "unity " + Name(f.FieldType) };
                if (serializeReference) extra.Add("reference");
                var former = AttrStrings(data, "UnityEngine.Serialization.FormerlySerializedAsAttribute");
                if (former.Count > 0) extra.Add("formerly " + string.Join(", ", former));
                lines.Add("S " + tn + "." + f.Name + Separator + string.Join("; ", extra));
            }
        }

        /// <summary>
        /// Members Visual Scripting's FullSerializer writes for a unit (fsMetaType.CollectProperties with the default
        /// fsConfig: [Serialize]/[SerializeAs]/[SerializeField]/[fsProperty], public fields, public read/write
        /// auto-properties; never statics, delegates, indexers or [DoNotSerialize]/[NonSerialized]/[fsIgnore]).
        /// </summary>
        static void CollectVsSerialized(Type type, string tn, List<string> lines)
        {
            foreach (var m in type.GetMembers(DeclaredAll))
            {
                var f = m as FieldInfo;
                var p = m as PropertyInfo;
                if (f == null && p == null)
                    continue;
                var data = m.GetCustomAttributesData();
                if ((f != null && (f.Attributes & FieldAttributes.NotSerialized) != 0) ||
                    HasAttr(data, "System.NonSerializedAttribute") || HasAttr(data, "Unity.VisualScripting.DoNotSerializeAttribute") ||
                    HasAttr(data, "Unity.VisualScripting.FullSerializer.fsIgnoreAttribute"))
                    continue;
                var forced = HasAttr(data, "UnityEngine.SerializeField") || HasAttr(data, "Unity.VisualScripting.SerializeAttribute") ||
                             HasAttr(data, "Unity.VisualScripting.SerializeAsAttribute") || HasAttr(data, "Unity.VisualScripting.FullSerializer.fsPropertyAttribute");
                Type memberType;
                if (f != null)
                {
                    memberType = f.FieldType;
                    if (IsDelegate(memberType) || IsCompilerGenerated(f) || f.IsStatic)
                        continue;
                    if (!forced && !f.IsPublic)
                        continue;
                }
                else
                {
                    memberType = p.PropertyType;
                    var get = p.GetGetMethod(true);
                    var set = p.GetSetMethod(true);
                    if (IsDelegate(memberType) || p.GetIndexParameters().Length > 0 || (get ?? set).IsStatic)
                        continue;
                    if (!forced)
                    {
                        var publicGet = p.GetGetMethod(false);
                        var auto = get != null && IsCompilerGenerated(get);
                        if (!p.CanRead || !p.CanWrite || publicGet == null || !auto)
                            continue;
                    }
                }
                var key = AttrStrings(data, "Unity.VisualScripting.SerializeAsAttribute").FirstOrDefault() ?? m.Name;
                var value = "vs " + Name(memberType) + (key != m.Name ? "; member " + m.Name : "");
                lines.Add("S " + tn + "." + key + Separator + value);
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Formatting
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>Stable type name: namespace-qualified, nested with '+', generics as Name`n&lt;Args&gt;.</summary>
        public static string Name(Type t)
        {
            if (t == null) return "?";
            if (t.IsGenericParameter) return t.Name;
            if (t.IsByRef) return Name(t.GetElementType()) + "&";
            if (t.IsPointer) return Name(t.GetElementType()) + "*";
            if (t.IsArray)
            {
                var rank = t.GetArrayRank();
                return Name(t.GetElementType()) + "[" + new string(',', rank - 1) + "]";
            }
            // Generic types always print their arguments (a definition prints its parameter names). Runtime reflection
            // hands back the definition itself for a self-reference such as ITween<T> inside ITween<T>, metadata readers
            // a constructed type: both must print the same text.
            if (t.IsGenericType)
            {
                var def = t.IsGenericTypeDefinition ? t : t.GetGenericTypeDefinition();
                return BareName(def) + "<" + string.Join(",", t.GetGenericArguments().Select(Name)) + ">";
            }
            return BareName(t);
        }

        static string BareName(Type t)
        {
            if (t.IsNested)
                return BareName(t.DeclaringType) + "+" + t.Name;
            return string.IsNullOrEmpty(t.Namespace) ? t.Name : t.Namespace + "." + t.Name;
        }

        static string ParamTypes(ParameterInfo[] ps) => string.Join(",", ps.Select(p => Name(p.ParameterType)));

        static string ParamDetails(ParameterInfo[] ps)
        {
            return string.Join(", ", ps.Select(p =>
            {
                var sb = new StringBuilder();
                if (p.ParameterType.IsByRef)
                {
                    var attrs = p.Attributes;
                    sb.Append((attrs & ParameterAttributes.Out) != 0 && (attrs & ParameterAttributes.In) == 0 ? "out " :
                              (attrs & ParameterAttributes.In) != 0 ? "in " : "ref ");
                }
                if (HasAttr(p.GetCustomAttributesData(), "System.ParamArrayAttribute"))
                    sb.Append("params ");
                sb.Append(p.Name);
                if ((p.Attributes & ParameterAttributes.HasDefault) != 0)
                {
                    object raw;
                    try { raw = p.RawDefaultValue; }
                    catch { raw = "?"; }
                    sb.Append(" = ").Append(Value(raw));
                }
                else if ((p.Attributes & ParameterAttributes.Optional) != 0)
                    sb.Append(" = ?");
                return sb.ToString();
            }));
        }

        /// <summary>Culture-invariant literal text, identical on Mono and .NET.</summary>
        public static string Value(object v)
        {
            if (v == null || v is DBNull || v.GetType().FullName == "System.Reflection.Missing") return "null";
            if (v is string s) return Quote(s);
            if (v is bool b) return b ? "true" : "false";
            if (v is char ch) return "'\\u" + ((int)ch).ToString("X4", CultureInfo.InvariantCulture) + "'";
            if (v is float fl) return fl.ToString("G9", CultureInfo.InvariantCulture) + "f";
            if (v is double d) return d.ToString("G17", CultureInfo.InvariantCulture) + "d";
            if (v is decimal m) return m.ToString(CultureInfo.InvariantCulture) + "m";
            if (v is Enum) v = Convert.ChangeType(v, Enum.GetUnderlyingType(v.GetType()), CultureInfo.InvariantCulture);
            if (v is Type ty) return "typeof(" + Name(ty) + ")";
            if (v is IFormattable fm) return fm.ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        static string Attrs(MemberInfo m)
        {
            var sel = SelectedAttributes(m.GetCustomAttributesData());
            return sel.Count == 0 ? "" : "; " + string.Join("; ", sel);
        }

        /// <summary>The attributes that change how content binds: obsolete markers and the rename/move records.</summary>
        static List<string> SelectedAttributes(IList<CustomAttributeData> data)
        {
            var list = new List<string>();
            foreach (var a in data)
            {
                string name;
                try { name = a.AttributeType.FullName; }
                catch { continue; }
                switch (name)
                {
                    case "System.ObsoleteAttribute":
                        var error = a.ConstructorArguments.Count > 1 && a.ConstructorArguments[1].Value is bool e && e;
                        list.Add(error ? "obsolete(error)" : "obsolete");
                        break;
                    case "Unity.VisualScripting.RenamedFromAttribute":
                        list.Add("renamedFrom(" + string.Join(", ", a.ConstructorArguments.Select(x => Value(x.Value))) + ")");
                        break;
                    case "UnityEngine.Scripting.APIUpdating.MovedFromAttribute":
                        list.Add("movedFrom(" + string.Join(", ", a.ConstructorArguments.Select(x => Value(x.Value))) + ")");
                        break;
                    case "Unity.VisualScripting.IncludeInSettingsAttribute":
                        list.Add("includeInSettings(" + string.Join(", ", a.ConstructorArguments.Select(x => Value(x.Value))) + ")");
                        break;
                }
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static bool HasAttr(IList<CustomAttributeData> data, string fullName)
        {
            foreach (var a in data)
            {
                try { if (a.AttributeType.FullName == fullName) return true; }
                catch { }
            }
            return false;
        }

        static List<string> AttrStrings(IList<CustomAttributeData> data, string fullName)
        {
            var list = new List<string>();
            foreach (var a in data)
            {
                try
                {
                    if (a.AttributeType.FullName != fullName) continue;
                    if (a.ConstructorArguments.Count > 0 && a.ConstructorArguments[0].Value is string s) list.Add(s);
                }
                catch { }
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ---------------------------------------------------------------------------------------------------------
        // Type helpers that work on MetadataLoadContext types too (no typeof() comparisons across contexts)
        // ---------------------------------------------------------------------------------------------------------

        public static bool IsVisible(Type t)
        {
            while (t.IsNested)
            {
                if (!t.IsNestedPublic) return false;
                t = t.DeclaringType;
            }
            return t.IsPublic;
        }

        public static bool DerivesFrom(Type t, string baseFullName)
        {
            try
            {
                for (var b = t.BaseType; b != null; b = b.BaseType)
                    if (b.FullName == baseFullName) return true;
            }
            catch { }
            return false;
        }

        static bool IsDelegate(Type t) => t.BaseType != null && t.BaseType.FullName == "System.MulticastDelegate";

        static Type Enum_UnderlyingType(Type t)
        {
            var f = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault();
            return f != null ? f.FieldType : null;
        }

        static IEnumerable<Type> SafeInterfaces(Type t)
        {
            try { return t.GetInterfaces(); }
            catch { return Enumerable.Empty<Type>(); }
        }

        static bool IsCompilerGenerated(MemberInfo m)
        {
            if (m.Name.IndexOf('<') >= 0) return true;
            return HasAttr(m.GetCustomAttributesData(), "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Baseline file (JSON with a sorted "entries" string array; a tiny reader so no JSON package is needed)
        // ---------------------------------------------------------------------------------------------------------

        public static string ToJson(string generatedFrom, IEnumerable<string> assemblies, List<string> entries)
        {
            var counts = Counts(entries);
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append(" \"schema\": ").Append(Schema).Append(",\n");
            sb.Append(" \"about\": ").Append(Quote("Public C# surface of the creator SDK (BS.SDK, BS.SDK.Splines, Banter.VisualScripting): " +
                "types, members, serialized field names and VS unit ports. Removals and changes fail SdkSurfaceTests; additions are reported. " +
                "Regenerate deliberately: copy Temp/BridgeSurface/bs-sdk-surface.current.json over this file after an EditMode run, " +
                "or run Tests/Editor/Surface/Generator~ (see Program.cs).")).Append(",\n");
            sb.Append(" \"generatedFrom\": ").Append(Quote(generatedFrom ?? "")).Append(",\n");
            sb.Append(" \"assemblies\": [").Append(string.Join(", ", assemblies.Select(Quote))).Append("],\n");
            sb.Append(" \"counts\": {").Append(string.Join(", ", counts.Select(kv => Quote(kv.Key) + ": " + kv.Value))).Append("},\n");
            sb.Append(" \"entries\": [\n");
            for (var i = 0; i < entries.Count; i++)
                sb.Append("  ").Append(Quote(entries[i])).Append(i + 1 < entries.Count ? ",\n" : "\n");
            sb.Append(" ]\n}\n");
            return sb.ToString();
        }

        public static SortedDictionary<string, int> Counts(IEnumerable<string> entries)
        {
            var c = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Add(string k) { c[k] = c.TryGetValue(k, out var n) ? n + 1 : 1; }
            var units = new HashSet<string>();
            foreach (var e in entries)
            {
                switch (e[0])
                {
                    case 'T': Add(e.Contains("=> public") ? "publicTypes" : "nonPublicSerializableTypes"); break;
                    case 'C': case 'M': case 'P': case 'F': case 'E': Add("publicMembers"); break;
                    case 'V': Add("enumValues"); break;
                    case 'S': Add(e.Contains("=> vs ") ? "vsSerializedMembers" : "unitySerializedFields"); break;
                    case 'U':
                        Add("unitPorts");
                        var key = Key(e);
                        var dot = key.LastIndexOf('.', key.LastIndexOf('.') - 1);
                        units.Add(key.Substring(2, dot - 2));
                        break;
                }
            }
            c["unitsWithPorts"] = units.Count;
            return c;
        }

        public static string Key(string entry)
        {
            var i = entry.IndexOf(Separator, StringComparison.Ordinal);
            return i < 0 ? entry : entry.Substring(0, i);
        }

        public static string Val(string entry)
        {
            var i = entry.IndexOf(Separator, StringComparison.Ordinal);
            return i < 0 ? "" : entry.Substring(i + Separator.Length);
        }

        /// <summary>Reads a string array property ("entries", "assemblies") or a string property from the baseline.</summary>
        public static List<string> ReadStringArray(string json, string property)
        {
            var list = new List<string>();
            var at = json.IndexOf("\"" + property + "\"", StringComparison.Ordinal);
            if (at < 0) return list;
            var i = json.IndexOf('[', at);
            if (i < 0) return list;
            i++;
            while (i < json.Length)
            {
                var c = json[i];
                if (c == ']') break;
                if (c == '"') { list.Add(ReadString(json, ref i)); continue; }
                i++;
            }
            return list;
        }

        public static string ReadStringProperty(string json, string property)
        {
            var at = json.IndexOf("\"" + property + "\"", StringComparison.Ordinal);
            if (at < 0) return null;
            var i = json.IndexOf('"', json.IndexOf(':', at));
            return i < 0 ? null : ReadString(json, ref i);
        }

        static string ReadString(string json, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < json.Length)
            {
                var c = json[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                var e = json[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(json.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated JSON string");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Comparison
        // ---------------------------------------------------------------------------------------------------------

        public class Diff
        {
            public readonly List<string> Removed = new List<string>();
            public readonly List<string> Changed = new List<string>();
            /// <summary>Changes that keep content working: only parts added (an [Obsolete] marker, an interface, a
            /// FormerlySerializedAs name), or a serialized field renamed with [FormerlySerializedAs] its old name.</summary>
            public readonly List<string> Compatible = new List<string>();
            public readonly List<string> Added = new List<string>();
            public bool Breaking => Removed.Count > 0 || Changed.Count > 0;

            public string Report(int max = 120)
            {
                var sb = new StringBuilder();
                void Section(string title, List<string> items)
                {
                    if (items.Count == 0) return;
                    sb.Append(title).Append(" (").Append(items.Count).Append("):\n");
                    foreach (var x in items.Take(max)) sb.Append("  ").Append(x).Append('\n');
                    if (items.Count > max) sb.Append("  ... and ").Append(items.Count - max).Append(" more\n");
                }
                Section("REMOVED", Removed);
                Section("CHANGED", Changed);
                Section("COMPATIBLE CHANGE (not a failure)", Compatible);
                Section("ADDED (not a failure)", Added);
                return sb.ToString();
            }
        }

        /// <summary>
        /// Entries are keyed by the text before " =&gt; ". A missing key is a removal and a different value a change,
        /// except where the new value only adds parts (split on "; ", interface lists item by item) or a serialized field
        /// was renamed with [FormerlySerializedAs] naming the old one: those are compatible.
        /// </summary>
        public static Diff Compare(IEnumerable<string> baseline, IEnumerable<string> current)
        {
            var b = ToMap(baseline);
            var c = ToMap(current);
            var d = new Diff();
            foreach (var kv in b)
            {
                if (!c.TryGetValue(kv.Key, out var now))
                {
                    var renamedTo = kv.Key.StartsWith("S ", StringComparison.Ordinal) ? FormerlyRenamedTo(kv.Key, c) : null;
                    if (renamedTo != null) d.Compatible.Add(kv.Key + Separator + kv.Value + "   ->   " + renamedTo);
                    else d.Removed.Add(kv.Key + Separator + kv.Value);
                }
                else if (now != kv.Value)
                    (OnlyAddsParts(kv.Value, now) ? d.Compatible : d.Changed).Add(kv.Key + Separator + kv.Value + "   ->   " + now);
            }
            foreach (var kv in c)
                if (!b.ContainsKey(kv.Key)) d.Added.Add(kv.Key + Separator + kv.Value);
            return d;
        }

        static HashSet<string> Parts(string value)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in value.Split(new[] { "; " }, StringSplitOptions.None))
            {
                if (part.StartsWith("implements ", StringComparison.Ordinal))
                    foreach (var i in part.Substring("implements ".Length).Split(new[] { ", " }, StringSplitOptions.None)) set.Add("implements " + i);
                else if (part.StartsWith("formerly ", StringComparison.Ordinal))
                    foreach (var i in part.Substring("formerly ".Length).Split(new[] { ", " }, StringSplitOptions.None)) set.Add("formerly " + i);
                else set.Add(part);
            }
            return set;
        }

        static bool OnlyAddsParts(string before, string after) => Parts(before).IsSubsetOf(Parts(after));

        /// <summary>"S Type.old" gone, but "S Type.new =&gt; ...; formerly old" present: Unity still reads the old data.</summary>
        static string FormerlyRenamedTo(string removedKey, Dictionary<string, string> current)
        {
            var dot = removedKey.LastIndexOf('.');
            var typePrefix = removedKey.Substring(0, dot + 1);
            var oldName = removedKey.Substring(dot + 1);
            foreach (var kv in current)
                if (kv.Key.StartsWith(typePrefix, StringComparison.Ordinal) && kv.Key.IndexOf('.', typePrefix.Length) < 0 &&
                    Parts(kv.Value).Contains("formerly " + oldName))
                    return kv.Key;
            return null;
        }

        static Dictionary<string, string> ToMap(IEnumerable<string> entries)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                var k = Key(e);
                // Duplicate keys (e.g. two overloads that differ only by ref/out) keep every value, sorted.
                map[k] = map.TryGetValue(k, out var prev) ? string.Join(" | ", new[] { prev, Val(e) }.OrderBy(x => x, StringComparer.Ordinal)) : Val(e);
            }
            return map;
        }
    }
}
