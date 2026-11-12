using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BS.SDKEditor.BuildChecks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// BSSpawnerObject copies: what a copy carries over from its spawner and what it must not (the spawner's ids, its
    /// spawner role, a kinematic body, nested synced objects), and the rules every client applies to copies.
    /// </summary>
    public class SpawnerObjectTests
    {
        Scene _scene;

        [SetUp]
        public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(_scene);

        GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else SceneManager.MoveGameObjectToScene(go, _scene);
            return go;
        }

        // A spawner with a handle child (its own id, two colliders) and a lamp child carrying a Synced Object.
        BSSpawnerObject MakeSpawner()
        {
            var root = Make("Cup");
            root.AddComponent<BSObjectId>();
            root.GetComponent<BSObjectId>().Id = "spawner-a";
            root.GetComponent<BSObjectId>().jsId = "js-root";
            root.AddComponent<BoxCollider>();
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var spawner = root.AddComponent<BSSpawnerObject>();
            spawner.jsId = "js-spawner";

            var handle = Make("Handle", root.transform);
            handle.AddComponent<BSObjectId>().Id = "spawner-a-handle";
            handle.AddComponent<BoxCollider>();
            handle.AddComponent<SphereCollider>();

            var lamp = Make("Lamp", root.transform);
            lamp.AddComponent<BSSyncedObject>();
            lamp.GetComponent<BSObjectId>().Id = "spawner-a-lamp";
            return spawner;
        }

        Transform MakeHolder()
        {
            var holder = Make("Holder");
            holder.SetActive(false);
            return holder.transform;
        }

        [Test]
        public void CreateCopy_IsTheWholeObject_WithoutTheSpawnerRole()
        {
            var spawner = MakeSpawner();
            var holder = MakeHolder();
            LogAssert.Expect(LogType.Warning, new Regex("only the copy as a whole is synced"));

            var copy = spawner.CreateCopy(holder, "obj_1");

            Assert.AreEqual(holder, copy.transform.parent, "made under the holder");
            Assert.IsFalse(copy.activeInHierarchy, "nothing on it has woken up");
            Assert.AreEqual("Cup", copy.name);
            Assert.IsNotNull(copy.transform.Find("Handle"), "children come along");
            Assert.IsNull(copy.GetComponent<BSSpawnerObject>(), "a copy doesn't spawn");
            Assert.IsNotNull(spawner.GetComponent<BSSpawnerObject>());
            Assert.IsFalse(copy.GetComponent<Rigidbody>().isKinematic, "a copy is thrown with physics");
            Assert.IsTrue(spawner.GetComponent<Rigidbody>().isKinematic, "the spawner stays put");

            var synced = copy.GetComponent<BSSyncedObject>();
            Assert.IsNotNull(synced, "the copy is synced");
            Assert.IsTrue(synced.TakeOwnershipOnGrab, "grabbing a copy takes it");
            Assert.IsNull(copy.transform.Find("Lamp").GetComponent<BSSyncedObject>(), "only the copy as a whole is synced");
            Assert.IsNotNull(spawner.transform.Find("Lamp").GetComponent<BSSyncedObject>(), "the spawner keeps its own");
        }

        [Test]
        public void CreateCopy_NeverSharesAnIdWithItsSpawner()
        {
            var spawner = MakeSpawner();
            var holder = MakeHolder();
            var first = spawner.CreateCopy(holder, "obj_1");
            var second = spawner.CreateCopy(holder, "obj_2");

            Assert.AreEqual("obj_1", first.GetComponent<BSObjectId>().Id);
            Assert.AreEqual("obj_1/1", first.transform.Find("Handle").GetComponent<BSObjectId>().Id);
            Assert.AreEqual("obj_1/2", first.transform.Find("Lamp").GetComponent<BSObjectId>().Id);
            Assert.AreEqual("spawner-a", spawner.GetComponent<BSObjectId>().Id, "the spawner keeps its authored id");

            var ids = new List<string>();
            foreach (var go in new[] { spawner.gameObject, first, second })
                ids.AddRange(go.GetComponentsInChildren<BSObjectId>(true).Select(objectId => objectId.Id));
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "every object id is unique: " + string.Join(", ", ids));

            Assert.IsTrue(first.GetComponentsInChildren<BSObjectId>(true).All(objectId => string.IsNullOrEmpty(objectId.jsId)),
                "the page links a copy afresh");
            Assert.IsTrue(first.GetComponentsInChildren<BSComponentBase>(true).All(component => string.IsNullOrEmpty(component.jsId)));
            Assert.AreEqual("js-root", spawner.GetComponent<BSObjectId>().jsId);
        }

        [Test]
        public void AssignIds_FollowsTheCopyToItsNetworkId()
        {
            var spawner = MakeSpawner();
            var copy = spawner.CreateCopy(MakeHolder(), SpawnerCopyRules.NewPendingId());
            Assert.IsTrue(SpawnerCopyRules.IsPendingId(copy.GetComponent<BSObjectId>().Id));

            SpawnerCopyRules.AssignIds(copy, "obj_9");

            Assert.AreEqual("obj_9", copy.GetComponent<BSObjectId>().Id);
            Assert.AreEqual("obj_9/1", copy.transform.Find("Handle").GetComponent<BSObjectId>().Id);
        }

        [Test]
        public void CreateCopy_NeedsAnInactiveHolderAndAnId()
        {
            var spawner = MakeSpawner();
            var active = Make("Active").transform;
            Assert.Throws<ArgumentException>(() => spawner.CreateCopy(active, "obj_1"));
            Assert.Throws<ArgumentException>(() => spawner.CreateCopy(MakeHolder(), ""));
        }

        [Test]
        public void Counterpart_IsTheSameColliderOnTheCopy()
        {
            var spawner = MakeSpawner();
            var copy = spawner.CreateCopy(MakeHolder(), "obj_1");
            var sphere = spawner.transform.Find("Handle").GetComponent<SphereCollider>();

            var match = SpawnerCopyRules.Counterpart(spawner.transform, sphere, copy.transform);

            Assert.AreSame(copy.transform.Find("Handle").GetComponent<SphereCollider>(), match);
            Assert.AreSame(copy.GetComponent<BoxCollider>(),
                SpawnerCopyRules.Counterpart(spawner.transform, spawner.GetComponent<BoxCollider>(), copy.transform));
            Assert.IsNull(SpawnerCopyRules.Counterpart(spawner.transform, Make("Elsewhere").AddComponent<BoxCollider>(), copy.transform),
                "a collider outside the spawner has no counterpart");
        }

        static SpawnerCopyRules.CopyInfo Info(string id, long created, bool locked = false) =>
            new SpawnerCopyRules.CopyInfo { ObjectId = id, CreatedAtEpochMs = created, Locked = locked };

        [Test]
        public void PickCapVictims_OnlyAtTheCap()
        {
            var copies = new[] { Info("a", 10), Info("b", 20) };
            CollectionAssert.IsEmpty(SpawnerCopyRules.PickCapVictims(copies, 3), "room for another");
            CollectionAssert.AreEqual(new[] { "a" }, SpawnerCopyRules.PickCapVictims(copies, 2), "the oldest goes");
            CollectionAssert.AreEqual(new[] { "a", "b" }, SpawnerCopyRules.PickCapVictims(copies, 1),
                "an overshoot comes back below the cap");
            CollectionAssert.AreEqual(new[] { "a", "b" }, SpawnerCopyRules.PickCapVictims(copies, 0),
                "at least one copy is always allowed");
        }

        [Test]
        public void PickCapVictims_SkipsHeldCopies()
        {
            CollectionAssert.AreEqual(new[] { "b" },
                SpawnerCopyRules.PickCapVictims(new[] { Info("a", 10, locked: true), Info("b", 20) }, 2));
            Assert.IsNull(SpawnerCopyRules.PickCapVictims(new[] { Info("a", 10, locked: true), Info("b", 20, locked: true) }, 2),
                "every copy is held: nothing spawns");
            CollectionAssert.AreEqual(new[] { "a" }, SpawnerCopyRules.PickCapVictims(new[] { Info("b", 10), Info("a", 10) }, 2),
                "ties by id");
            CollectionAssert.AreEqual(new[] { "b", "c" },
                SpawnerCopyRules.PickCapVictims(new[] { Info("a", 10, locked: true), Info("b", 20), Info("c", 30) }, 2),
                "held copies are passed over");
        }

        [Test]
        public void CreateCopy_DropsASpawnerNestedInside()
        {
            var spawner = MakeSpawner();
            var inner = Make("Refill", spawner.transform);
            inner.AddComponent<BSObjectId>().Id = "spawner-a-refill";
            inner.AddComponent<Rigidbody>();
            inner.AddComponent<BSSpawnerObject>();
            LogAssert.Expect(LogType.Warning, new Regex("is a spawner too"));
            LogAssert.Expect(LogType.Warning, new Regex("only the copy as a whole is synced"));

            var copy = spawner.CreateCopy(MakeHolder(), "obj_1");

            Assert.IsEmpty(copy.GetComponentsInChildren<BSSpawnerObject>(true), "a copy never spawns");
            Assert.IsNotNull(inner.GetComponent<BSSpawnerObject>(), "the spawner keeps its own");
        }
    }

    /// <summary>GameObject > BS > Objects > Spawner Object: a stick that is ready to grab, synced, and clean for the Builder.</summary>
    public class SpawnerObjectMenuTests
    {
        [Test]
        public void Menu_MakesAGrabbableSyncedSpawnerStick()
        {
            var previousSelection = Selection.activeObject;
            var previousActive = SceneManager.GetActiveScene();
            // The menu creates in the active scene. Edit Mode tests normally run in a scratch untitled scene the test
            // runner opens and closes again, so it can create there. With a saved scene active instead, the test makes a
            // scene of its own and activates it for the run, so the scenes already open aren't touched.
            bool ownScene = !string.IsNullOrEmpty(previousActive.path);
            var scene = ownScene ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive) : previousActive;
            if (ownScene)
                SceneManager.SetActiveScene(scene);
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            GameObject parent = null;
            try
            {
                parent = new GameObject("Spawner menu test parent");
                var menu = Type.GetType("CreatorConveniencesMenu, BS.SDKEditor", true);
                menu.GetMethod("CreateSpawnerObject", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { new MenuCommand(parent) });

                var go = Selection.activeGameObject;
                Assert.IsNotNull(go);
                Assert.AreSame(parent.transform, go.transform.parent);
                Assert.AreEqual(20, go.layer, "on the Grabbable layer");
                Assert.AreEqual(new Vector3(0.04f, 0.4f, 0.04f), go.transform.localScale, "a stick");
                Assert.IsNotNull(go.GetComponent<BoxCollider>());
                Assert.IsTrue(go.GetComponent<Rigidbody>().isKinematic);
                Assert.IsNotNull(go.GetComponent<BSWorldObject>());
                Assert.IsNotNull(go.GetComponent<BSSyncedObject>(), "says how the copies sync");
                var grab = new SerializedObject(go.GetComponent<BSGrababble>());
                Assert.AreEqual((int)BSGrabType.Cylinder, grab.FindProperty("grabType").intValue, "held along its length");
                var spawner = new SerializedObject(go.GetComponent<BSSpawnerObject>());
                Assert.AreEqual(10, spawner.FindProperty("maxSpawned").intValue);
                Assert.IsTrue(spawner.FindProperty("forceKinematic").boolValue);
                Assert.IsFalse(string.IsNullOrEmpty(go.GetComponent<BSObjectId>().Id), "an authored id for the copies to name");

                var check = (BuildCheck)Activator.CreateInstance(
                    Type.GetType("BS.SDKEditor.BuildChecks.SpawnerObjectCheck, BS.SDKEditor", true), true);
                using (var context = BuildCheckContext.ForScene(scene, new[] { BuildTarget.Android }))
                    Assert.IsEmpty(BuildChecklist.Run(context, new[] { check }).Issues, "nothing for the Builder to flag");
            }
            finally
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (parent != null)
                    UnityEngine.Object.DestroyImmediate(parent);
                if (ownScene)
                {
                    if (previousActive.IsValid())
                        SceneManager.SetActiveScene(previousActive);
                    EditorSceneManager.CloseScene(scene, true);
                }
                Selection.activeObject = previousSelection;
            }
        }
    }
}
