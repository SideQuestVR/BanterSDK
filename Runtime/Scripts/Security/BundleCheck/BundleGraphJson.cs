using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#if GREENFIELD_PROJECT
using Unity.VisualScripting;
#endif

namespace BS.Security
{
    /// <summary>
    /// Reads Visual Scripting's serialized JSON (FullSerializer) the way <c>fsJsonParser</c> and <c>fsSerializer</c> do:
    /// whitespace by <see cref="char.IsWhiteSpace(char)"/>, // and /* */ comments, optional commas, its escape set
    /// (including \0 and \a), raw control characters inside strings, and trailing content ignored. It collects what the
    /// checker judges anywhere in the data: UnityEvent persistent calls, AnimationEvent values, every type FullSerializer
    /// would build ("$type", a MissingType unit's "formerType") and every boxed System.Type value. Anything it cannot read
    /// is a failure (the caller refuses): never skipped.
    /// </summary>
    /// <remarks>
    /// FullSerializer's metadata is followed, not skipped: a "$content" wrapper is transparent (fsSerializer unwraps any
    /// object that has one, so a call list hidden in {"$content":[...]} is still a call list); a "$ref" stands for an
    /// object defined elsewhere in the same data, which was judged where it was defined, except that a "$ref" where a
    /// UnityEvent's calls belong is itself reported as a call (no legitimate graph carries persistent calls); a
    /// MissingType's "formerValue" is JSON that FullSerializer parses again, so it is read again here (nested at most
    /// <see cref="MaxFormerValueNesting"/> deep, refused deeper).
    /// </remarks>
    internal static class BundleGraphJson
    {
        internal sealed class Result
        {
            internal bool Unreadable;
            internal string Error;
            /// <summary>Persistent calls with a method name, found in UnityEvent-shaped values (method names only).</summary>
            internal readonly List<string> PersistentCalls = new List<string>();
            /// <summary>AnimationEvent values (by $type or shape), with their function names.</summary>
            internal readonly List<string> AnimationEvents = new List<string>();
            /// <summary>Distinct type names FullSerializer would build: every "$type" and "formerType" string.</summary>
            internal readonly HashSet<string> Types = new HashSet<string>(StringComparer.Ordinal);
            /// <summary>Distinct (type name, string content) pairs of objects with both: a boxed System.Type value when the type is a Type.</summary>
            internal readonly HashSet<KeyValuePair<string, string>> TypedContents = new HashSet<KeyValuePair<string, string>>();
        }

        /// <summary>
        /// fsJsonParser.MaxDepth: containers nested deeper fail to parse, and fsSerializer caps a whole deserialization at
        /// the same depth, MissingType formerValue re-parses included. Deeper input is refused here too.
        /// </summary>
        internal const int MaxDepth = 128;

        /// <summary>formerValue JSON inside formerValue JSON... deeper than this is refused.</summary>
        internal const int MaxFormerValueNesting = 4;

        /// <summary>Distinct type names and typed contents one blob may carry (a world's graphs use a few hundred).</summary>
        internal const int MaxDistinctTypes = 20000;

        enum Expect : byte
        {
            None,
            CallGroup, // the value of m_PersistentCalls
            Calls,     // the value of m_Calls inside a call group
            Call,      // an element of m_Calls
        }

        sealed class Frame
        {
            internal bool IsObject;
            internal Expect Role;          // what this container is
            internal Expect PendingValue;  // role for the value of the key just read
            internal string PendingKey;
            internal HashSet<string> Keys; // duplicate keys make FullSerializer throw
            internal string TypeName;      // this object's "$type" string
            internal string ContentText;   // this object's "$content" when it is a string
        }

        static bool Is(string key, string a) => string.Equals(key, a, StringComparison.OrdinalIgnoreCase);

        internal static Result Scan(string json) => Scan(json, 0, 0);

        /// <summary><paramref name="baseDepth"/>: containers already open around this JSON (a formerValue replaces its unit in place).</summary>
        static Result Scan(string json, int nesting, int baseDepth)
        {
            var result = new Result();
            if (string.IsNullOrEmpty(json)) return result;
            // The scan loops over captured data: charge it to the check's budget (bytes and time).
            BundleCheckBudget.ChargeCurrent(json.Length);
            try
            {
                new Reader(json, result, nesting, baseDepth).Run();
            }
            catch (FormatException e)
            {
                result.Unreadable = true;
                result.Error = e.Message;
            }
            return result;
        }

        sealed class Reader
        {
            readonly string s;
            readonly Result result;
            readonly int nesting;
            readonly int baseDepth;
            int p;
            int checkedAt;
            readonly List<Frame> stack = new List<Frame>();

            /// <summary>Every 64 KiB of input: let the check's budget stop a long scan (the watchdog cannot stop a thread).</summary>
            void Tick()
            {
                if (p - checkedAt < 65536) return;
                checkedAt = p;
                BundleCheckBudget.CheckCurrent();
            }

            internal Reader(string json, Result result, int nesting, int baseDepth)
            {
                this.baseDepth = baseDepth;
                s = json;
                this.result = result;
                this.nesting = nesting;
            }

            static FormatException Fail(string why) => new FormatException(why);

            bool Has(int offset = 0) => p + offset < s.Length;

            void SkipSpace()
            {
                while (Has())
                {
                    Tick();
                    var c = s[p];
                    if (char.IsWhiteSpace(c))
                    {
                        p++;
                        continue;
                    }
                    if (c != '/') break;
                    // fsJsonParser: "//" and "/*" start comments; any other '/' is not space and fails the value that
                    // follows (it used to loop here forever, and so did fsJsonParser before its own fix).
                    if (!Has(1)) break; // a last '/': the caller fails on it (or ignores it after the root value, as fsJsonParser does)
                    if (s[p + 1] == '/')
                    {
                        while (Has() && Environment.NewLine.IndexOf(s[p]) < 0)
                        {
                            p++;
                            Tick();
                        }
                        continue;
                    }
                    if (s[p + 1] == '*')
                    {
                        p += 2;
                        while (Has(1))
                        {
                            if (s[p] == '*' && s[p + 1] == '/')
                            {
                                p += 3; // fsJsonParser steps one past the closing slash
                                break;
                            }
                            p++;
                            Tick();
                        }
                        // Unterminated: like fsJsonParser, the last character is left to be read as a token.
                        continue;
                    }
                    throw Fail("stray '/'");
                }
            }

            internal void Run()
            {
                SkipSpace();
                if (!Has()) throw Fail("empty");
                Value(Expect.None);
                // fsJsonParser ignores whatever follows the root value.
            }

            /// <summary>Parses one value iteratively (containers push frames), carrying the role the parent gave it.</summary>
            void Value(Expect role)
            {
                var depthAtStart = stack.Count;
                ParseOne(role);
                while (stack.Count > depthAtStart)
                {
                    Tick();
                    var frame = stack[stack.Count - 1];
                    SkipSpace();
                    if (!Has()) throw Fail("unterminated " + (frame.IsObject ? "object" : "array"));
                    var close = frame.IsObject ? '}' : ']';
                    if (s[p] == close)
                    {
                        p++;
                        stack.RemoveAt(stack.Count - 1);
                        if (frame.IsObject) Close(frame);
                        AfterValue();
                        continue;
                    }
                    if (frame.IsObject)
                    {
                        var key = ReadString();
                        if (frame.Keys == null) frame.Keys = new HashSet<string>(StringComparer.Ordinal);
                        if (!frame.Keys.Add(key)) throw Fail("duplicate key");
                        SkipSpace();
                        if (!Has() || s[p] != ':') throw Fail("expected ':'");
                        p++;
                        SkipSpace();
                        frame.PendingKey = key;
                        frame.PendingValue = RoleForKey(frame, key);
                        if (!Has()) throw Fail("missing value");
                        if (Is(key, "$ref") && frame.Role != Expect.None)
                        {
                            // An object defined elsewhere standing where a UnityEvent's calls belong.
                            result.PersistentCalls.Add("$ref in a UnityEvent");
                        }
                        if (s[p] == '"' && IsJudgedString(frame, key))
                        {
                            var text = ReadString();
                            Judge(frame, key, text);
                            AfterValue();
                            continue;
                        }
                        ParseOne(frame.PendingValue);
                    }
                    else
                    {
                        ParseOne(frame.Role == Expect.Calls ? Expect.Call : Expect.None);
                    }
                }
            }

            static bool IsJudgedString(Frame frame, string key) =>
                Is(key, "$type") || Is(key, "$content") || Is(key, "functionName") || Is(key, "m_FunctionName")
                || Is(key, "formerType") || Is(key, "formerValue")
                || (frame.Role == Expect.Call && (Is(key, "m_MethodName") || Is(key, "methodName")));

            static Expect RoleForKey(Frame frame, string key)
            {
                if (Is(key, "m_PersistentCalls") || Is(key, "m_PersistentListeners")) return Expect.CallGroup;
                if (frame.Role == Expect.CallGroup && (Is(key, "m_Calls") || Is(key, "m_Listeners"))) return Expect.Calls;
                // fsSerializer unwraps any object with "$content": the wrapper's role is its content's.
                if (Is(key, "$content")) return frame.Role;
                return Expect.None;
            }

            void AddType(string name)
            {
                if (result.Types.Count >= MaxDistinctTypes && !result.Types.Contains(name)) throw Fail("too many distinct types");
                result.Types.Add(name);
            }

            /// <summary>On an object's close: a "$type" with a string "$content" may be a boxed System.Type value.</summary>
            void Close(Frame frame)
            {
                if (frame.TypeName == null || frame.ContentText == null) return;
                var pair = new KeyValuePair<string, string>(frame.TypeName, frame.ContentText);
                if (result.TypedContents.Count >= MaxDistinctTypes && !result.TypedContents.Contains(pair)) throw Fail("too many distinct type values");
                result.TypedContents.Add(pair);
            }

            void Judge(Frame frame, string key, string text)
            {
                if (Is(key, "$type"))
                {
                    frame.TypeName = text;
                    AddType(text);
                    var comma = text.IndexOf(',');
                    var typeName = (comma >= 0 ? text.Substring(0, comma) : text).Trim();
                    if (Is(typeName, "UnityEngine.AnimationEvent")) result.AnimationEvents.Add("$type AnimationEvent");
                    else if (Is(typeName, "UnityEngine.Events.PersistentCall") || Is(typeName, "UnityEngine.Events.PersistentCallGroup")
                             || Is(typeName, "UnityEngine.Events.ArgumentCache"))
                        result.PersistentCalls.Add("$type " + typeName);
                    return;
                }
                if (Is(key, "$content"))
                {
                    frame.ContentText = text;
                    return;
                }
                if (Is(key, "formerType"))
                {
                    AddType(text);
                    return;
                }
                if (Is(key, "formerValue"))
                {
                    // FullSerializer parses a MissingType's former value again when its type is back: read it as JSON too.
                    if (text.Length == 0) return;
                    if (nesting + 1 > MaxFormerValueNesting) throw Fail("formerValue nested too deeply");
                    var inner = Scan(text, nesting + 1, baseDepth + stack.Count - 1);
                    if (inner.Unreadable) throw Fail("formerValue: " + inner.Error);
                    result.PersistentCalls.AddRange(inner.PersistentCalls);
                    result.AnimationEvents.AddRange(inner.AnimationEvents);
                    foreach (var name in inner.Types) AddType(name);
                    foreach (var pair in inner.TypedContents)
                    {
                        if (result.TypedContents.Count >= MaxDistinctTypes && !result.TypedContents.Contains(pair)) throw Fail("too many distinct type values");
                        result.TypedContents.Add(pair);
                    }
                    return;
                }
                if (frame.Role == Expect.Call && (Is(key, "m_MethodName") || Is(key, "methodName")))
                {
                    if (text.Length > 0) result.PersistentCalls.Add(text);
                    return;
                }
                if (text.Length > 0) result.AnimationEvents.Add(text); // functionName / m_FunctionName
            }

            /// <summary>After a value inside a container: an optional comma.</summary>
            void AfterValue()
            {
                SkipSpace();
                if (Has() && s[p] == ',') p++;
            }

            void ParseOne(Expect role)
            {
                SkipSpace();
                if (!Has()) throw Fail("unexpected end");
                var c = s[p];
                switch (c)
                {
                    case '{':
                    case '[':
                        if (baseDepth + stack.Count >= MaxDepth) throw Fail("nested deeper than " + MaxDepth + " levels");
                        p++;
                        stack.Add(new Frame { IsObject = c == '{', Role = role });
                        return;
                    case '"':
                        ReadString();
                        break;
                    case 't':
                        Exact("true");
                        break;
                    case 'f':
                        Exact("false");
                        break;
                    case 'n':
                        Exact("null");
                        break;
                    case 'I':
                    case 'N':
                    case '.':
                    case '+':
                    case '-':
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        Number();
                        break;
                    default:
                        throw Fail("invalid token");
                }
                if (stack.Count > 0) AfterValue();
            }

            void Exact(string word)
            {
                if (string.CompareOrdinal(s, p, word, 0, word.Length) != 0) throw Fail("expected " + word);
                p += word.Length;
                // fsJsonParser fails when the word ends the input (its TryMoveNext returns false).
                if (!Has()) throw Fail("unexpected end after " + word);
            }

            static bool IsSeparator(char c) => char.IsWhiteSpace(c) || c == ',' || c == '}' || c == ']';

            void Number()
            {
                var start = p;
                p++;
                while (Has() && !IsSeparator(s[p]))
                {
                    p++;
                    Tick();
                }
                var text = s.Substring(start, p - start);
                if (text.Contains(".") || text.Contains("e") || text.Contains("E") || text == "Infinity" || text == "-Infinity" || text == "NaN")
                {
                    if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) throw Fail("bad number");
                }
                else if (!long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    throw Fail("bad number");
                }
            }

            readonly StringBuilder text = new StringBuilder();

            string ReadString()
            {
                if (!Has() || s[p] != '"') throw Fail("expected string");
                p++;
                text.Length = 0;
                while (Has() && s[p] != '"')
                {
                    Tick();
                    var c = s[p];
                    if (c != '\\')
                    {
                        text.Append(c);
                        p++;
                        continue;
                    }
                    p++;
                    if (!Has()) throw Fail("unterminated escape");
                    var e = s[p];
                    switch (e)
                    {
                        case '\\': text.Append('\\'); p++; break;
                        case '/': text.Append('/'); p++; break;
                        case '"': text.Append('"'); p++; break;
                        case 'a': text.Append('\a'); p++; break;
                        case 'b': text.Append('\b'); p++; break;
                        case 'f': text.Append('\f'); p++; break;
                        case 'n': text.Append('\n'); p++; break;
                        case 'r': text.Append('\r'); p++; break;
                        case 't': text.Append('\t'); p++; break;
                        case '0': text.Append('\0'); p++; break;
                        case 'u':
                            p++;
                            if (!Has(3) || !IsHex(s[p]) || !IsHex(s[p + 1]) || !IsHex(s[p + 2]) || !IsHex(s[p + 3])) throw Fail("bad \\u escape");
                            text.Append((char)int.Parse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            p += 4;
                            break;
                        default:
                            throw Fail("bad escape");
                    }
                }
                if (!Has()) throw Fail("unterminated string");
                p++;
                return text.ToString();
            }

            static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }

    /// <summary>
    /// What graph JSON may name for FullSerializer to build or resolve, before any runtime hook: bundle graphs deserialize
    /// in LudiqBehaviour.OnAfterDeserialize, on the loading thread, WITHOUT the page path's type filter, and
    /// FullSerializer constructs whatever a "$type" names.
    /// <list type="bullet">
    /// <item>"$type" and "formerType": the runtime's own deserialization backstop (BanterStubsAllowed.IsBackstopAllowedType,
    /// installed in players as fsSerializer.backstopTypeFilter), so the checker and the runtime agree, plus the checker's
    /// lists on top: denied host types (BundleCheckPolicy), gate and security types, host UnityEngine.Object types,
    /// native pointers and event plumbing (PersistentCall, ArgumentCache, AnimationEvent), also as generic arguments or
    /// element types.</item>
    /// <item>Boxed System.Type values: plain types, world-facing content types and allow-listed types pass; host, denied and
    /// gate types are refused (the page path's IsAllowedTypeValue, widened to content: bundle graphs keep the legacy
    /// rule for now).</item>
    /// </list>
    /// Outside the Greenfield project (no fork gate) the checker's own equivalent applies: plain values, collections and
    /// content types.
    /// </summary>
    internal static class BundleGraphTypes
    {
        internal enum Verdict
        {
            /// <summary>Every type the name could mean is allowed.</summary>
            Allowed,
            /// <summary>No loaded type has the name: FullSerializer builds nothing from it.</summary>
            Unresolved,
            /// <summary>A type the name could mean is a host, denied or gate type, or event plumbing.</summary>
            Refused,
            /// <summary>The name cannot be judged (control characters, too long, the lookup failed).</summary>
            Unjudgeable,
        }

        /// <summary>
        /// Judges a "$type"/"formerType" name (<paramref name="asTypeValue"/> false) or the type a boxed System.Type value
        /// names (true), over every type the name could resolve to.
        /// </summary>
        internal static Verdict Judge(string typeName, bool asTypeValue, out string detail)
        {
            detail = "";
            var candidates = BundleTypeResolver.ResolveSerializedType(typeName);
            if (candidates == null)
            {
                detail = "not judgeable";
                return Verdict.Unjudgeable;
            }
            if (candidates.Length == 0) return Verdict.Unresolved;
            foreach (var type in candidates)
            {
                string why;
                var allowed = asTypeValue ? IsAllowedTypeValue(type, out why) : IsAllowedSerializedType(type, out why);
                if (allowed) continue;
                detail = BundleNames.Clean(type.FullName ?? type.Name, 100) + ": " + why;
                return Verdict.Refused;
            }
            return Verdict.Allowed;
        }

        /// <summary>Whether a resolved type is a System.Type (a boxed value of it names another type).</summary>
        internal static bool IsTypeOfType(string typeName)
        {
            var candidates = BundleTypeResolver.ResolveSerializedType(typeName);
            if (candidates == null) return true; // unjudgeable: judge its content as a type value too
            foreach (var type in candidates)
            {
                if (typeof(Type).IsAssignableFrom(type)) return true;
            }
            return false;
        }

        /// <summary>The type itself, element types of arrays/pointers, and every generic argument, recursively.</summary>
        static IEnumerable<Type> Components(Type type, int depth = 0)
        {
            if (type == null || depth > 16) yield break;
            if (type.HasElementType)
            {
                yield return type;
                foreach (var inner in Components(type.GetElementType(), depth + 1)) yield return inner;
                yield break;
            }
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                yield return type.GetGenericTypeDefinition();
                foreach (var argument in type.GetGenericArguments())
                {
                    foreach (var inner in Components(argument, depth + 1)) yield return inner;
                }
                yield break;
            }
            yield return type;
        }

        static readonly Type runtimeType = typeof(Type).GetType();

        static bool IsPlainData(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(object) || type == typeof(Guid);

        static bool IsNativePointer(Type type) => type.IsPointer || type == typeof(IntPtr) || type == typeof(UIntPtr) || type.IsByRef;

        /// <summary>Event plumbing as data: persistent calls and AnimationEvents are refused in bundle graphs under both rules.</summary>
        static bool IsEventPlumbing(Type type)
        {
            var name = type.FullName ?? "";
            return name == "UnityEngine.Events.PersistentCall" || name == "UnityEngine.Events.PersistentCallGroup"
                   || name == "UnityEngine.Events.ArgumentCache" || name.StartsWith("UnityEngine.Events.InvokableCall", StringComparison.Ordinal)
                   || name == "UnityEngine.AnimationEvent";
        }

        /// <summary>Refused wherever it appears: gate/security types, denied host types and assemblies, host UnityEngine.Object types.</summary>
        static bool IsHostOrDenied(Type type, out string why)
        {
            why = null;
            if (IsNativePointer(type)) why = "a native pointer";
            else if (BundleCheckPolicy.IsGateType(type)) why = "the gate or the client's bundle security";
            else if (IsEventPlumbing(type)) why = "event data";
            else if (type.IsGenericParameter) why = "an open generic parameter";
            else if (!IsPlainData(type) && !IsSystemCollection(type))
            {
                var verdict = BundleCheckPolicy.Classify(type);
                if (verdict == BundleCheckPolicy.ScriptClass.Denied) why = "a denied host type";
                else if (verdict != BundleCheckPolicy.ScriptClass.Content && typeof(UnityEngine.Object).IsAssignableFrom(type)) why = "an app object type outside the world-facing lists";
            }
            return why != null;
        }

        /// <summary>
        /// Plain containers: System.Collections.Generic's definitions, Nullable, and the old non-generic lists (a live world,
        /// techsmansion, keeps an ArrayList in a graph). Their elements carry their own "$type"s, judged on their own.
        /// </summary>
        static bool IsSystemCollection(Type type) =>
            (type.IsGenericTypeDefinition && ((type.Namespace ?? "") == "System.Collections.Generic" || type == typeof(Nullable<>)))
            || type == typeof(System.Collections.ArrayList) || type == typeof(System.Collections.Hashtable)
            || type == typeof(System.Collections.Queue) || type == typeof(System.Collections.Stack);

        /// <summary>A "$type" FullSerializer may construct from bundle graph data.</summary>
        internal static bool IsAllowedSerializedType(Type type, out string why)
        {
            foreach (var component in Components(type))
            {
                if (IsHostOrDenied(component, out why)) return false;
            }
#if GREENFIELD_PROJECT
            // The runtime's own deserialization backstop (fsSerializer.backstopTypeFilter in players): the same predicate,
            // so the checker refuses before load exactly what the runtime would refuse at load, and a bit more above.
            why = BackstopAllows(type) ? null : "refused by the deserialization backstop (a host, core-library, engine or SDK class, a delegate, or a type that holds an address or runtime handle)";
            return why == null;
#else
            foreach (var component in Components(type))
            {
                // System.Type itself (and the runtime's type class) is resolved, never constructed; its value is judged apart.
                if (component.HasElementType || IsPlainData(component) || component == typeof(Type) || component == runtimeType || IsSystemCollection(component)) continue;
                if (BundleCheckPolicy.Classify(component) == BundleCheckPolicy.ScriptClass.Content) continue;
                why = BundleNames.Clean(component.FullName ?? component.Name, 80) + " is neither content nor a plain value or collection";
                return false;
            }
            why = null;
            return true;
#endif
        }

#if GREENFIELD_PROJECT
#if BUNDLECHECK_OFFLINE
        // Offline test harnesses compile these sources into an assembly that is not BS.SDK, so the fork's internal
        // predicate is reached by reflection there. Unity builds never define BUNDLECHECK_OFFLINE.
        static readonly System.Reflection.MethodInfo backstop = typeof(BanterStubsAllowed).GetMethod("IsBackstopAllowedType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        static bool BackstopAllows(Type type) => (bool)backstop.Invoke(null, new object[] { type });
#else
        static bool BackstopAllows(Type type) => BanterStubsAllowed.IsBackstopAllowedType(type);
#endif
#endif

        /// <summary>The type a boxed System.Type value names (what GetComponent/AddComponent(Type) would receive).</summary>
        internal static bool IsAllowedTypeValue(Type type, out string why)
        {
            why = null;
            while (type.HasElementType && !type.IsPointer && !type.IsByRef) type = type.GetElementType();
            if (IsNativePointer(type))
            {
                why = "a native pointer";
                return false;
            }
            if (IsPlainData(type)) return true;
            foreach (var component in Components(type))
            {
                if (IsHostOrDenied(component, out why)) return false;
            }
            if (!type.IsGenericType && BundleCheckPolicy.Classify(type) == BundleCheckPolicy.ScriptClass.Content) return true;
#if GREENFIELD_PROJECT
            if (BanterStubsAllowed.IsAllowedType(type)) return true;
#endif
            why = "a type outside the world-facing lists and the allow-list";
            return false;
        }
    }
}
