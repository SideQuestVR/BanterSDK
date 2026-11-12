using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
#if GREENFIELD_PROJECT
using Unity.VisualScripting;
#endif

namespace BS.Security
{
    /// <summary>
    /// How Unity binds a persistent call or dispatches an AnimationEvent, judged against the app's real types. Pure
    /// reflection plus Unity's own <see cref="UnityEventBase.GetValidMethodInfo(Type, string, Type[])"/> (managed code,
    /// no engine calls), so it runs off the main thread. Thread-safe; meant to be the one copy of these rules (the
    /// pre-load check uses it, and BSUnityEventGuard can).
    /// </summary>
    internal static class BundleEventRules
    {
        internal enum Verdict
        {
            Ok,
            Static,
            NonPublic,
            ByName,
            Unresolved,
            NotOnAllowList,
            NoReceiver,
            HiddenName,
        }

        const BindingFlags DeclaredMembers =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        static readonly Type[] eventDefinitions = { typeof(UnityEvent<>), typeof(UnityEvent<,>), typeof(UnityEvent<,,>), typeof(UnityEvent<,,,>) };

        // ------------------------------------------------------------------ persistent calls

        /// <summary>
        /// The methods Unity could bind for a call on <paramref name="targetType"/> (UnityEventBase.FindMethod): one in
        /// the usual case, several when the object-argument type name could mean several types, none when Unity binds
        /// nothing. <paramref name="approximate"/> is set when the exact argument types are unknown (an EventDefined call
        /// whose event type could not be found, or an ambiguous overload); the result is then every same-named method of
        /// a possible arity on the type chain, a superset.
        /// </summary>
        internal static List<MethodInfo> PossibleMethods(Type targetType, string methodName, int mode, string objectArgumentTypeName,
            Type[] eventArguments, out bool approximate)
        {
            approximate = false;
            var result = new List<MethodInfo>();
            if (targetType == null || string.IsNullOrEmpty(methodName)) return result;
            if (targetType.ContainsGenericParameters)
            {
                // A generic definition named by a type string: judge every same-named method of a possible arity.
                approximate = true;
                return mode == 0 ? Candidates(targetType, methodName, 0, 4)
                    : mode == 1 ? Candidates(targetType, methodName, 0, 0)
                    : mode >= 2 && mode <= 6 ? Candidates(targetType, methodName, 1, 1) : result;
            }
            var argumentSets = new List<Type[]>();
            switch (mode)
            {
                case 0: // EventDefined
                    if (eventArguments == null)
                    {
                        approximate = true;
                        return Candidates(targetType, methodName, 0, 4);
                    }
                    argumentSets.Add(eventArguments);
                    break;
                case 1:
                    argumentSets.Add(Type.EmptyTypes);
                    break;
                case 2: // Object: Type.GetType(argument type name) ?? Object
                    {
                        argumentSets.Add(new[] { typeof(UnityEngine.Object) });
                        if (!string.IsNullOrEmpty(objectArgumentTypeName))
                        {
                            var named = BundleTypeResolver.ResolveTypeName(objectArgumentTypeName);
                            if (named == null)
                            {
                                approximate = true;
                                return Candidates(targetType, methodName, 1, 1);
                            }
                            foreach (var type in named) argumentSets.Add(new[] { type });
                        }
                        break;
                    }
                case 3:
                    argumentSets.Add(new[] { typeof(int) });
                    break;
                case 4:
                    argumentSets.Add(new[] { typeof(float) });
                    break;
                case 5:
                    argumentSets.Add(new[] { typeof(string) });
                    break;
                case 6:
                    argumentSets.Add(new[] { typeof(bool) });
                    break;
                default:
                    return result; // Unity's FindMethod returns null for any other mode
            }
            foreach (var arguments in argumentSets)
            {
                MethodInfo method;
                try
                {
                    method = UnityEventBase.GetValidMethodInfo(targetType, methodName, arguments);
                }
                catch (AmbiguousMatchException)
                {
                    approximate = true;
                    return Candidates(targetType, methodName, arguments.Length, arguments.Length);
                }
                catch (Exception)
                {
                    method = null; // Unity's own lookup throws the same way when the event fires: nothing binds
                }
                if (method != null && !result.Contains(method)) result.Add(method);
            }
            return result;
        }

        /// <summary>Every method named <paramref name="name"/> with an arity in range, declared anywhere on the type chain.</summary>
        internal static List<MethodInfo> Candidates(Type type, string name, int minArity, int maxArity)
        {
            var result = new List<MethodInfo>();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                BundleCheckBudget.CheckCurrent();
                MemberInfo[] members;
                try
                {
                    members = current.GetMember(name, MemberTypes.Method, DeclaredMembers);
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (var member in members)
                {
                    var method = (MethodInfo)member;
                    int arity;
                    try
                    {
                        arity = method.GetParameters().Length;
                    }
                    catch (Exception)
                    {
                        arity = minArity;
                    }
                    if (arity >= minArity && arity <= maxArity) result.Add(method);
                }
            }
            return result;
        }

        /// <summary>The worst thing about one bindable method (allow-list aside).</summary>
        internal static Verdict JudgeMethod(MethodInfo method)
        {
            if (method.IsStatic) return Verdict.Static;
            if (!method.IsPublic) return Verdict.NonPublic;
            if (IsByNameDispatch(method)) return Verdict.ByName;
            return Verdict.Ok;
        }

        internal static bool IsByNameDispatch(MethodInfo method) =>
            BundleCheckPolicy.IsByNameDispatch(method.Name)
            && (method.DeclaringType == typeof(GameObject) || method.DeclaringType == typeof(Component) || method.DeclaringType == typeof(MonoBehaviour));

        /// <summary>
        /// The argument types of the UnityEvent stored at <paramref name="path"/> inside <paramref name="ownerType"/>'s
        /// serialized data (field names, "[]" for an array/list element; former names honoured), or null when the path or
        /// the event type cannot be followed.
        /// </summary>
        internal static Type[] EventArguments(Type ownerType, IList<string> path)
        {
            var type = ownerType;
            for (var i = 0; type != null && i < path.Count; i++)
            {
                if (path[i] == "[]")
                {
                    if (type.IsArray) type = type.GetElementType();
                    else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) type = type.GetGenericArguments()[0];
                    else return null;
                    continue;
                }
                type = SerializedField(type, path[i])?.FieldType;
            }
            if (type == null || !typeof(UnityEventBase).IsAssignableFrom(type)) return null;
            for (var current = type; current != null && current != typeof(UnityEventBase); current = current.BaseType)
            {
                if (current == typeof(UnityEvent)) return Type.EmptyTypes;
                if (!current.IsGenericType) continue;
                var definition = current.GetGenericTypeDefinition();
                foreach (var known in eventDefinitions)
                {
                    if (definition == known) return current.GetGenericArguments();
                }
            }
            return null; // a custom UnityEventBase: its own FindMethod_Impl
        }

        static FieldInfo SerializedField(Type type, string name)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo[] fields;
                try
                {
                    fields = current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                }
                catch (Exception)
                {
                    return null;
                }
                foreach (var field in fields)
                {
                    if (field.Name == name) return field;
                }
                foreach (var field in fields)
                {
                    object[] formerNames;
                    try
                    {
                        formerNames = field.GetCustomAttributes(typeof(UnityEngine.Serialization.FormerlySerializedAsAttribute), false);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    foreach (UnityEngine.Serialization.FormerlySerializedAsAttribute former in formerNames)
                    {
                        if (former.oldName == name) return field;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Whether the Visual Scripting allow-list lists the member for the target's type or a base type up to the one that
        /// declares it (ids are "Type.FullName.Member"; set_X/get_X are listed as X). Event machines'
        /// TriggerUnityEvent/TriggerAnimationEvent are the documented sinks. Without the allow-list (outside the Greenfield
        /// project) everything public passes. Same rule as BSUnityEventGuard.IsListed.
        /// </summary>
        internal static bool IsListed(Type targetType, MethodInfo method)
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

        // ------------------------------------------------------------------ AnimationEvents

        static readonly object hiddenGate = new object();
        static Dictionary<string, string> hiddenNames;

        /// <summary>
        /// An AnimationEvent dispatches by function name to every MonoBehaviour on the animated object (non-public and
        /// static methods included). Judged like BSUnityEventGuard.JudgeAnimationEvent: by-name dispatch; a name that is
        /// non-public or static on any MonoBehaviour in the player (a component added later could receive it) unless it
        /// is a known sink a receiver has public; static or non-public on a receiver; no receiver; not on the allow-list.
        /// </summary>
        internal static Verdict JudgeAnimationEvent(string functionName, ICollection<Type> receivers, out string member)
        {
            member = "<no receiver>." + BundleNames.Clean(functionName);
            if (string.IsNullOrEmpty(functionName)) return Verdict.NoReceiver;
            if (BundleCheckPolicy.IsByNameDispatch(functionName))
            {
                member = BundleNames.Clean(functionName);
                return Verdict.ByName;
            }
            if (HiddenNames().TryGetValue(functionName, out var hiddenOn)
                && !(BundleCheckPolicy.IsAnimationEventSink(functionName) && IsPublicOnReceiver(functionName, receivers)))
            {
                member = hiddenOn + "." + BundleNames.Clean(functionName);
                return Verdict.HiddenName;
            }
            var found = false;
            string unlisted = null;
            foreach (var receiver in receivers)
            {
                BundleCheckBudget.CheckCurrent();
                for (var type = receiver; type != null && type != typeof(object); type = type.BaseType)
                {
                    foreach (var info in type.GetMember(functionName, MemberTypes.Method, DeclaredMembers))
                    {
                        var method = (MethodInfo)info;
                        member = BundleNames.Clean(type.FullName) + "." + BundleNames.Clean(method.Name);
                        if (method.IsStatic) return Verdict.Static;
                        if (!method.IsPublic) return Verdict.NonPublic;
                        found = true;
                        if (unlisted == null && !IsListed(receiver, method)) unlisted = BundleNames.Clean(receiver.FullName) + "." + BundleNames.Clean(method.Name);
                    }
                }
            }
            if (!found) return Verdict.NoReceiver;
            if (unlisted != null)
            {
                member = unlisted;
                return Verdict.NotOnAllowList;
            }
            return Verdict.Ok;
        }

        static bool IsPublicOnReceiver(string functionName, ICollection<Type> receivers)
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

        /// <summary>
        /// Each method name that is non-public or static on some MonoBehaviour type in the player (or its Behaviour,
        /// Component and Object bases), with one type that has it. Built once, on the first clip with events; editor and
        /// test assemblies are left out (BSUnityEventGuard.IsPlayerAssembly), so the editor and a build agree.
        /// </summary>
        internal static Dictionary<string, string> HiddenNames()
        {
            lock (hiddenGate)
            {
                if (hiddenNames != null) return hiddenNames;
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                void Add(Type type)
                {
                    foreach (var method in type.GetMethods(DeclaredMembers))
                    {
                        if ((method.IsStatic || !method.IsPublic) && !names.ContainsKey(method.Name)) names[method.Name] = BundleNames.Clean(type.FullName);
                    }
                }
                for (var type = typeof(MonoBehaviour).BaseType; type != null; type = type.BaseType) Add(type);
                foreach (var type in BundleTypeResolver.AllBehaviourTypes())
                {
                    BundleCheckBudget.CheckCurrent();
                    try
                    {
                        if (IsPlayerAssembly(type.Assembly)) Add(type);
                    }
                    catch (Exception)
                    {
                        // a type whose members cannot load cannot be added as a component either
                    }
                }
                return hiddenNames = names;
            }
        }

        /// <summary>Not dynamic, not editor (by name), not a test assembly or framework (same test as the guard's).</summary>
        static bool IsPlayerAssembly(Assembly assembly)
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
                // unreadable references: part of the player
            }
            return true;
        }

        static bool IsTestFramework(string name) =>
            name == "nunit.framework" || name == "UnityEngine.TestRunner" || name == "UnityEditor.TestRunner";
    }
}
