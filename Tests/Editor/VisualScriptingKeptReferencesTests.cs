using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Greenfield's Visual Scripting fork keeps the object references a graph was deserialized from, so the content guard
    /// in a player can take them instead of serializing the graph again (seconds on a Quest for a world with big graphs).
    /// The list must come only from Unity's deserialization, be handed over once, and be dropped when Unity serializes the
    /// object again (its content may have changed since it loaded), so the guard falls back to serializing.
    /// </summary>
    public class VisualScriptingKeptReferencesTests
    {
        [Test]
        public void KeptReferences_ComeFromDeserialization_AreTakenOnce_AndGoWhenSerializedAgain()
        {
            var held = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            var macro = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            ScriptGraphAsset copy = null, again = null, copyOfAgain = null;
            try
            {
                macro.graph.variables.Set("held", held);
                Assert.IsNull(macro.TakeLoadedObjectReferences(), "never deserialized: nothing kept");

                copy = Object.Instantiate(macro); // Unity serializes the source and deserializes the copy
                var kept = copy.TakeLoadedObjectReferences();
                Assert.IsNotNull(kept, "the copy was deserialized from data that points at the held asset");
                CollectionAssert.Contains(kept, held);
                Assert.IsNull(copy.TakeLoadedObjectReferences(), "handed over once");

                again = Object.Instantiate(macro);
                copyOfAgain = Object.Instantiate(again); // Unity serializes 'again' as the source
                Assert.IsNull(again.TakeLoadedObjectReferences(), "serialized again: its kept list no longer stands for it");
                var fresh = copyOfAgain.TakeLoadedObjectReferences();
                Assert.IsNotNull(fresh, "a copy made from it is deserialized afresh");
                CollectionAssert.Contains(fresh, held);
            }
            finally
            {
                foreach (var asset in new Object[] { held, macro, copy, again, copyOfAgain })
                {
                    if (asset != null) Object.DestroyImmediate(asset);
                }
            }
        }

        [Test]
        public void NoGraphData_KeepsNothing()
        {
            var behaviourHost = new GameObject("kept references test");
            try
            {
                var variables = behaviourHost.AddComponent<Variables>(); // added, never deserialized
                Assert.IsNull(variables.TakeLoadedObjectReferences());
            }
            finally
            {
                Object.DestroyImmediate(behaviourHost);
            }
        }
    }
}
