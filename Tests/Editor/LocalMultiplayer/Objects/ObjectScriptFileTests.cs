using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>
    /// Unity knows a MonoBehaviour by the script file named after its class. One that shares another class's file has
    /// no MonoScript of its own, and adding it at runtime depends on a lookup the editor may refuse.
    /// </summary>
    public class ObjectScriptFileTests
    {
        static readonly string[] Folders =
        {
            "Packages/com.sidequest.creator-sdk/Runtime/LocalMultiplayer/Objects",
            "Packages/com.sidequest.creator-sdk/Runtime/LocalMultiplayer/Diagnostics"
        };

        [Test]
        public void EveryMonoBehaviour_HasAScriptFileNamedAfterIt()
        {
            foreach (var folder in Folders) Assert.IsTrue(AssetDatabase.IsValidFolder(folder), folder + " not found");
            var scripted = new HashSet<Type>();
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", Folders))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                var type = script != null ? script.GetClass() : null;
                if (type != null) scripted.Add(type);
            }

            var missing = new List<string>();
            foreach (var type in typeof(SyncedObjectsModule).Assembly.GetTypes())
            {
                if (type.Namespace != typeof(SyncedObjectsModule).Namespace) continue;
                if (type.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                if (!scripted.Contains(type)) missing.Add(type.Name);
            }
            CollectionAssert.IsEmpty(missing, "MonoBehaviours without a script file named after them");
            Assert.IsTrue(scripted.Contains(typeof(HandoffTriggerForwarder)));
        }
    }
}
