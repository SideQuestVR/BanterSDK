using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The Visual Scripting unit "Set Avatar" (<see cref="BS.VisualScripting.SetAvatar"/>) raises
    /// <c>BSSceneEvents.OnAvatarSet(remoteUrl, localUrl)</c> with URLs a world's graph chooses, and nothing listens today.
    /// Avatars load through Basis unchecked (<c>BasisPreLoadCheck.Kind.Unchecked</c>: a peer's avatar resolves only to this
    /// client's own catalog, and a URL avatar only loads for the user who chose it). A listener that loaded those URLs as
    /// the user's avatar would let any world put a bundle on the user that the pre-load content check never saw. This test
    /// fails as soon as runtime code touches the event besides its declaration and the unit: such a listener must first load
    /// through the content check (a checked kind), or not take URLs from a world at all.
    /// </summary>
    /// <remarks>
    /// Scans the IL of every loaded runtime assembly that references BS.SDK (editor and test assemblies excluded) for loads
    /// of the field. A listener reached by reflection on the name would not show here; none exists.
    /// </remarks>
    [Category("UnityRuntime")]
    public class AvatarSetGuardTests
    {
        static readonly Dictionary<short, OpCode> opCodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.GetValue(null))
            .OfType<OpCode>()
            .ToDictionary(o => o.Value);

        [Test]
        public void NothingListensToTheWorldsSetAvatarEvent()
        {
            var field = typeof(BSSceneEvents).GetField("OnAvatarSet", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "BSSceneEvents.OnAvatarSet moved: update this guard");
            var allowed = new HashSet<Type> { typeof(BSSceneEvents), typeof(BS.VisualScripting.SetAvatar) };
            var offenders = new List<string>();
            var scanned = 0;
            foreach (var assembly in RuntimeAssemblies())
            {
                scanned++;
                foreach (var type in TypesOf(assembly))
                {
                    if (IsWithin(type, allowed)) continue;
                    const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                    IEnumerable<MethodBase> methods;
                    try
                    {
                        methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)).ToList();
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    foreach (var method in methods)
                    {
                        if (LoadsField(method, field)) offenders.Add(assembly.GetName().Name + ": " + type.FullName + "." + method.Name);
                    }
                }
            }
            Assert.Greater(scanned, 1, "no runtime assemblies were scanned");
            Assert.IsEmpty(offenders,
                "Runtime code uses BSSceneEvents.OnAvatarSet, whose URLs come from a world's graph; avatars load unchecked "
                + "(BasisPreLoadCheck.Kind.Unchecked), so such a URL must go through the content check before it becomes an avatar:\n"
                + string.Join("\n", offenders));
        }

        static IEnumerable<Assembly> RuntimeAssemblies()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                var name = assembly.GetName().Name;
                if (name.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                bool usesSdk;
                try
                {
                    usesSdk = name == "BS.SDK" || assembly.GetReferencedAssemblies().Any(r => r.Name == "BS.SDK");
                }
                catch (Exception)
                {
                    continue;
                }
                if (usesSdk) yield return assembly;
            }
        }

        static IEnumerable<Type> TypesOf(Assembly assembly)
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

        static bool IsWithin(Type type, HashSet<Type> allowed)
        {
            for (var t = type; t != null; t = t.DeclaringType)
            {
                if (allowed.Contains(t)) return true;
            }
            return false;
        }

        /// <summary>Whether the method's IL loads (or takes the address of, or stores) <paramref name="field"/>.</summary>
        static bool LoadsField(MethodBase method, FieldInfo field)
        {
            byte[] il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception)
            {
                return false;
            }
            if (il == null) return false;
            var at = 0;
            while (at < il.Length)
            {
                short value = il[at++];
                if (value == 0xFE && at < il.Length) value = (short)(0xFE00 | il[at++]);
                if (!opCodes.TryGetValue(value, out var code)) return false; // not IL we can follow: nothing found
                var size = OperandSize(code.OperandType, il, at);
                if (code.OperandType == OperandType.InlineField && at + 4 <= il.Length)
                {
                    var token = BitConverter.ToInt32(il, at);
                    try
                    {
                        var resolved = method.Module.ResolveField(token,
                            method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null,
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        if (resolved != null && resolved.Module == field.Module && resolved.MetadataToken == field.MetadataToken) return true;
                    }
                    catch (Exception)
                    {
                        // a field in an assembly that is not loaded: not this one
                    }
                }
                at += size;
            }
            return false;
        }

        static int OperandSize(OperandType type, byte[] il, int at)
        {
            switch (type)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                case OperandType.InlineSwitch: return at + 4 <= il.Length ? 4 + 4 * BitConverter.ToInt32(il, at) : il.Length;
                default: return 4;
            }
        }
    }
}
