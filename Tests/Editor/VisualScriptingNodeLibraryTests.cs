using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The Visual Scripting node library the SDK configures: every BS type the client runs has to come from an
    /// assembly (or type) on it, or the fuzzy finder never offers its nodes. In August 2026 the Banter.* -> BS.*
    /// rename turned the assembly names "Banter.SDK" and "Banter.VisualScripting" on this list into "BS" and
    /// "BS.VisualScripting", which match no assembly, and every BS component lost its nodes.
    /// </summary>
    public class VisualScriptingNodeLibraryTests
    {
        static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(fullName)).FirstOrDefault(type => type != null);

        // Visual Scripting's own rule (Codebase.UpdateSettings): only types from a listed assembly, and of those, the
        // ones on the type list, enums and UnityEngine.Objects (unless [IncludeInSettings(false)]), and anything with
        // [IncludeInSettings(true)]. A plain class like BSUser needs to be on the type list.
        static bool OnTheNodeLibrary(Type type)
        {
            if (!VsNodeGeneration.assemblyAllowList.Contains(type.Assembly.GetName().Name))
                return false;
            if (VsNodeGeneration.typeAllowList.Contains(type))
                return true;
            var attribute = (Unity.VisualScripting.IncludeInSettingsAttribute)Attribute.GetCustomAttribute(type,
                typeof(Unity.VisualScripting.IncludeInSettingsAttribute));
            if (type.IsEnum || typeof(UnityEngine.Object).IsAssignableFrom(type))
                return attribute == null || attribute.include;
            return attribute != null && attribute.include;
        }

        [Test]
        public void TheSdkAssemblies_AreOnTheList()
        {
            CollectionAssert.Contains(VsNodeGeneration.assemblyAllowList, typeof(BSText).Assembly.GetName().Name);
            var sdkNodes = FindType("BS.VisualScripting.SetSpaceStateValue");
            Assert.IsNotNull(sdkNodes, "the SDK's Visual Scripting assembly isn't loaded");
            CollectionAssert.Contains(VsNodeGeneration.assemblyAllowList, sdkNodes.Assembly.GetName().Name);
        }

        [Test]
        public void EverySdkAssemblyNameOnTheList_IsARealAssembly()
        {
            var loaded = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name));
            var sdkNames = VsNodeGeneration.assemblyAllowList.Where(name => name.StartsWith("BS", StringComparison.Ordinal) ||
                                                                            name.StartsWith("Banter", StringComparison.Ordinal));
            foreach (var name in sdkNames)
                Assert.IsTrue(loaded.Contains(name), $"\"{name}\" is on the node library list, but no assembly has that name");
        }

        [Test]
        public void EveryBsTypeTheClientRuns_HasNodes()
        {
            // VsStubsAllowed lists the members the client runs: "BS.BSText.text", "BS.BSUser..ctor". It also lists
            // each one under its old name ("BS.BanterText.text") for graphs made before the rename; those types are
            // Banter.SDK's legacy stubs, so the names never resolve here.
            var typeNames = VsStubsAllowed.members
                .Where(member => member.StartsWith("BS.", StringComparison.Ordinal) && !member.StartsWith("BS.Banter", StringComparison.Ordinal))
                .Select(member => member.EndsWith("..ctor", StringComparison.Ordinal)
                    ? member.Substring(0, member.Length - "..ctor".Length)
                    : member.Substring(0, member.LastIndexOf('.')))
                .Distinct()
                .ToList();
            Assert.Greater(typeNames.Count, 50, "the client's member list looks empty");

            var missing = new List<string>();
            var unknown = new List<string>();
            foreach (var name in typeNames)
            {
                var type = FindType(name);
                if (type == null)
                    unknown.Add(name);
                else if (!OnTheNodeLibrary(type))
                    missing.Add(name);
            }
            if (unknown.Count > 0)
                Debug.Log("[VS node library test] Types on the client's list that the SDK no longer has: " + string.Join(", ", unknown));
            Assert.IsEmpty(missing, "BS types the client runs that the node library leaves out: " + string.Join(", ", missing));
            Assert.Less(unknown.Count, typeNames.Count / 2, "most of the client's BS types don't resolve; is BS.SDK loaded?");
        }
    }
}
