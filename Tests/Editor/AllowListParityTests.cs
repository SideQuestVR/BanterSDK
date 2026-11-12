using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.VisualScripting;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The two Visual Scripting allow-lists are one list in two places: VsStubsAllowed (this SDK's editor side: what the
    /// node finder and the build check offer) and BanterStubsAllowed (com.unity.visualscripting's runtime side: what a
    /// graph may actually call in a world). The generator writes both from the same identifiers; a hand edit to one of
    /// them alone leaves nodes that can be placed and then are blocked in-world (seen 2026-10-22: 33 LightmapSettings /
    /// LightProbes ids were in the editor list only).
    /// </summary>
    public class AllowListParityTests
    {
        [Test]
        public void TheEditorAndRuntimeAllowListsAreTheSame()
        {
            var editor = new HashSet<string>(VsStubsAllowed.members);
            var runtime = new HashSet<string>(BanterStubsAllowed.members);
            var editorOnly = editor.Except(runtime).OrderBy(id => id).ToList();
            var runtimeOnly = runtime.Except(editor).OrderBy(id => id).ToList();
            Assert.IsEmpty(editorOnly, "offered in the editor but blocked in-world: " + string.Join(", ", editorOnly.Take(20)));
            Assert.IsEmpty(runtimeOnly, "allowed in-world but missing from the editor list: " + string.Join(", ", runtimeOnly.Take(20)));
        }

        [TestCase("UnityEngine.LightmapSettings.lightProbes")]
        [TestCase("UnityEngine.LightProbes.bakedProbes")]
        [TestCase("UnityEngine.LightProbes.Tetrahedralize")]
        public void LightProbeMembers_AreAllowedInWorld(string id)
        {
            Assert.IsTrue(BanterStubsAllowed.IsAllowedIdentifier(id), id);
        }
    }
}
