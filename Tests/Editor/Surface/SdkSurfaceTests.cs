using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.SDKEditor.Tests.Surface
{
    static class SurfacePaths
    {
        const string PackageName = "com.sidequest.creator-sdk";

        public static string PackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SurfacePaths).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                return info.resolvedPath;
            return Path.GetFullPath(Path.Combine("Packages", PackageName));
        }

        public static string Baseline => Path.Combine(PackageRoot(), "Tests", "Editor", "Surface", "bs-sdk-surface.json");

        /// <summary>Every run writes what it saw here, so a deliberate change can be accepted by copying this file over the baseline.</summary>
        public static string Current => Path.GetFullPath(Path.Combine("Temp", "BridgeSurface", "bs-sdk-surface.current.json"));
    }

    /// <summary>
    /// Bridge 2 compatibility guard (plan §7): the public C# surface of the SDK that Visual Scripting graphs and world
    /// bundles bind to by name. Compares the loaded BS.SDK, BS.SDK.Splines and Banter.VisualScripting assemblies with
    /// the committed baseline (bs-sdk-surface.json). Removed or changed types, members, enum values, serialized field
    /// names and unit port keys fail; additions are logged. Renames need [FormerlySerializedAs] / [RenamedFrom] and an
    /// [Obsolete] forwarding member, and the baseline is then regenerated on purpose.
    /// </summary>
    public class SdkSurfaceTests
    {
        static List<string> Current(IList<string> assemblyNames, List<string> problems)
        {
            var types = new List<Type>();
            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var name in assemblyNames)
            {
                var asm = loaded.FirstOrDefault(a => a.GetName().Name == name);
                if (asm == null)
                {
                    problems.Add($"assembly {name} is not loaded");
                    continue;
                }
                try { types.AddRange(asm.GetTypes()); }
                catch (ReflectionTypeLoadException e)
                {
                    types.AddRange(e.Types.Where(t => t != null));
                    problems.Add($"{name}: {e.LoaderExceptions.Length} types failed to load: {e.LoaderExceptions.FirstOrDefault()?.Message}");
                }
            }
            return SdkSurface.Collect(types, UnitPorts);
        }

        /// <summary>Port keys exist only once a unit is defined; graphs connect to them by key.</summary>
        internal static IEnumerable<SdkSurface.Port> UnitPorts(Type type)
        {
            if (!typeof(IUnit).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters ||
                type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) == null)
                return null;
            var unit = (IUnit)Activator.CreateInstance(type, true);
            unit.EnsureDefined();
            if (unit.failedToDefine)
                return null; // its port lines go missing and the comparison reports them
            var ports = new List<SdkSurface.Port>();
            foreach (var p in unit.controlInputs) ports.Add(new SdkSurface.Port { Kind = "ci", Key = p.key });
            foreach (var p in unit.controlOutputs) ports.Add(new SdkSurface.Port { Kind = "co", Key = p.key });
            foreach (var p in unit.valueInputs) ports.Add(new SdkSurface.Port { Kind = "vi", Key = p.key, ValueType = p.type });
            foreach (var p in unit.valueOutputs) ports.Add(new SdkSurface.Port { Kind = "vo", Key = p.key, ValueType = p.type });
            return ports;
        }

        [Test]
        public void PublicSurface_HasNoRemovalsOrChangesAgainstTheBaseline()
        {
            Assert.IsTrue(File.Exists(SurfacePaths.Baseline), "missing baseline " + SurfacePaths.Baseline);
            var json = File.ReadAllText(SurfacePaths.Baseline);
            var baseline = SdkSurface.ReadStringArray(json, "entries");
            var assemblies = SdkSurface.ReadStringArray(json, "assemblies");
            Assert.Greater(baseline.Count, 1000, "the baseline looks empty or unreadable");
            CollectionAssert.IsNotEmpty(assemblies);
            // BS.SDK.Splines compiles only with com.unity.splines (BS_HAS_SPLINES), and the baseline holds its names too.
            if (assemblies.Contains("BS.SDK.Splines") && !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Unity.Splines"))
                Assert.Ignore("com.unity.splines is not installed, so BS.SDK.Splines is not compiled: compare the surface in a project that has it.");

            var problems = new List<string>();
            var current = Current(assemblies, problems);
            Directory.CreateDirectory(Path.GetDirectoryName(SurfacePaths.Current));
            File.WriteAllText(SurfacePaths.Current,
                SdkSurface.ToJson($"EditMode run {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z, Unity {Application.unityVersion}", assemblies, current));
            Assert.IsEmpty(problems, string.Join("\n", problems));

            var diff = SdkSurface.Compare(baseline, current);
            if (diff.Added.Count > 0 || diff.Compatible.Count > 0)
                Debug.Log($"[SdkSurface] {diff.Added.Count} additions and {diff.Compatible.Count} compatible changes (not failures):\n"
                          + string.Join("\n", diff.Compatible.Concat(diff.Added).Take(300)));
            Assert.IsFalse(diff.Breaking,
                "The SDK's public C# surface lost or changed names that graphs and bundles bind to (baseline: "
                + SdkSurface.ReadStringProperty(json, "generatedFrom") + ").\n" + diff.Report()
                + "\nKeep the old name working ([Obsolete] forwarder, [FormerlySerializedAs], [RenamedFrom]). If the change is intended and "
                + "reviewed, copy " + SurfacePaths.Current + " over " + SurfacePaths.Baseline + ".");
        }

        [Test]
        public void Compare_FailsOnRemovalsAndChanges_NotOnAdditionsOrCompatibleChanges()
        {
            var baseline = new List<string>
            {
                "T BS.A => public; class",
                "M BS.A.Run(System.Int32) => System.Void (count)",
                "M BS.A.Old() => System.Void ()",
                "S BS.A.speed => unity System.Single",
                "S BS.A.size => unity System.Single",
                "U BS.VisualScripting.N.vi.Value => System.Single",
                "V BS.E.One => 1",
            };
            var current = new List<string>
            {
                "T BS.A => public; class; implements System.IDisposable",       // compatible: an interface added
                "M BS.A.Run(System.Int32) => System.Boolean (count)",           // changed return type
                "M BS.A.Old() => System.Void (); obsolete",                     // compatible: marked obsolete
                "S BS.A.velocity => unity System.Single; formerly speed",       // compatible rename (old data still read)
                "V BS.E.One => 2",                                              // changed value
                "M BS.A.Stop() => System.Void ()",                              // addition
            };
            var d = SdkSurface.Compare(baseline, current);
            CollectionAssert.AreEquivalent(new[] { "S BS.A.size => unity System.Single", "U BS.VisualScripting.N.vi.Value => System.Single" }, d.Removed);
            Assert.AreEqual(2, d.Changed.Count, d.Report());
            Assert.AreEqual(3, d.Compatible.Count, d.Report());
            Assert.AreEqual(2, d.Added.Count, d.Report());
            Assert.IsTrue(d.Breaking);
            Assert.IsFalse(SdkSurface.Compare(baseline, baseline.Concat(new[] { "T BS.B => public; class" })).Breaking);
        }

        [Test]
        public void Baseline_RoundTripsThroughTheWriterAndReader()
        {
            var lines = new List<string> { "F BS.X.Q => const System.String = \"a\\\"b\\\\c\"", "T BS.X => public; class" };
            var json = SdkSurface.ToJson("tést", new[] { "BS.SDK" }, lines);
            CollectionAssert.AreEqual(lines, SdkSurface.ReadStringArray(json, "entries"));
            Assert.AreEqual("tést", SdkSurface.ReadStringProperty(json, "generatedFrom"));
        }
    }
}
