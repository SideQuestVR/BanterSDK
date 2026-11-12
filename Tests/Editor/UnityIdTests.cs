using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// <see cref="BSScene.UnityId"/>: the number the page knows an object or component by (unityId, oid, cid), which
    /// the SDK hands out itself now that Unity's EntityId has replaced the instance id. One per object, never 0, never
    /// another object's (even when Unity reuses an EntityId), kept through a destroy, and the key of the scene's object
    /// map in both directions.
    /// </summary>
    public class UnityIdTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
                if (obj != null)
                    Object.DestroyImmediate(obj);
            _created.Clear();
        }

        GameObject Create(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            _created.Add(go);
            return go;
        }

        // ---- assigning ----

        [Test]
        public void Null_IsZero()
        {
            Assert.AreEqual(0, BSScene.UnityId(null));
        }

        [Test]
        public void AnObject_KeepsOnePositiveId()
        {
            var go = Create("a");
            var id = BSScene.UnityId(go);
            Assert.Greater(id, 0, "the page reads 0 as no parent");
            Assert.AreEqual(id, BSScene.UnityId(go));
        }

        [Test]
        public void ObjectsAndComponents_EachGetTheirOwn()
        {
            var a = Create("a");
            var b = Create("b");
            var ids = new[] { BSScene.UnityId(a), BSScene.UnityId(b), BSScene.UnityId(a.transform), BSScene.UnityId(b.transform) };
            CollectionAssert.AllItemsAreUnique(ids);
        }

        [Test]
        public void OneObject_GetsOneId_FromManyThreads()
        {
            // The link's thread and the main thread both ask; the answer must not depend on who asked first.
            var go = Create("shared");
            var ids = new int[32];
            Parallel.For(0, ids.Length, i => ids[i] = BSScene.UnityId(go));
            Assert.AreEqual(1, ids.Distinct().Count());
            Assert.AreEqual(ids[0], BSScene.UnityId(go));
        }

        // ---- destroyed objects and reuse ----

        [Test]
        public void ADestroyedObject_KeepsItsId()
        {
            // A held object destroyed mid-grab is still reported released by the id the page knows it by.
            var go = Create("held");
            var id = BSScene.UnityId(go);
            Object.DestroyImmediate(go);
            Assert.IsTrue(go == null, "destroyed");
            Assert.AreEqual(id, BSScene.UnityId(go));
        }

        [Test]
        public void IdsAreNeverHandedOutTwice_WhenObjectsComeAndGo()
        {
            // Unity may give a destroyed object's EntityId to the next object it creates; the page's ids must not
            // follow it, or a new object would answer to a destroyed one's id.
            var ids = new HashSet<int>();
            var entityIds = new HashSet<EntityId>();
            var reusedEntityIds = 0;
            for (var i = 0; i < 500; i++)
            {
                var go = new GameObject("churn") { hideFlags = HideFlags.HideAndDontSave };
                if (!entityIds.Add(go.GetEntityId()))
                    reusedEntityIds++;
                Assert.IsTrue(ids.Add(BSScene.UnityId(go)), "an id was handed out twice");
                Object.DestroyImmediate(go);
            }
            TestContext.WriteLine($"EntityIds this Unity reused: {reusedEntityIds}");
        }

        // ---- the scene's object map ----

        [Test]
        public void TheObjectMap_LooksUpBothWays()
        {
            WithBareScene(scene =>
            {
                var go = Create("registered");
                scene.AddBanterObject(go, null, skipChangeFlush: true);
                var oid = BSScene.UnityId(go);
                Assert.AreSame(go, scene.GetGameObject(oid));
                Assert.AreEqual(oid, scene.GetBanterObject(oid).oid);
                Assert.AreSame(go, scene.GetObject(oid).gameObject);
            });
        }

        [Test]
        public void ADestroyedObjectsId_NeverFindsTheNextObject()
        {
            WithBareScene(scene =>
            {
                var first = Create("first");
                scene.AddBanterObject(first, null, skipChangeFlush: true);
                var firstId = BSScene.UnityId(first);
                scene.DestroyBanterObject(firstId);
                Object.DestroyImmediate(first);

                var second = Create("second");
                scene.AddBanterObject(second, null, skipChangeFlush: true);
                var secondId = BSScene.UnityId(second);

                Assert.AreNotEqual(firstId, secondId);
                Assert.IsNull(scene.GetGameObject(firstId), "the destroyed object's id resolves to nothing");
                Assert.AreSame(second, scene.GetGameObject(secondId));
                Assert.AreEqual(1, scene.RegisteredObjectCount);
            });
        }

        // BSScene's constructor has side effects (input actions, a static instance), so the maps are given to a bare
        // one, which stands in as BSScene.Instance() (BSObject's constructor asks for it) until the test ends.
        static void WithBareScene(System.Action<BSScene> test)
        {
            var instance = typeof(BSScene).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            var objects = typeof(BSScene).GetField("objects", BindingFlags.NonPublic | BindingFlags.Instance);
            var components = typeof(BSScene).GetField("banterComponents", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(instance, "BSScene._instance");
            Assert.IsNotNull(objects, "BSScene.objects");
            Assert.IsNotNull(components, "BSScene.banterComponents");

            var scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            objects.SetValue(scene, new ConcurrentDictionary<int, UnityAndBanterObject>());
            components.SetValue(scene, new ConcurrentDictionary<int, BSComponent>());
            var previous = instance.GetValue(null);
            instance.SetValue(null, scene);
            try
            {
                test(scene);
            }
            finally
            {
                instance.SetValue(null, previous);
            }
        }
    }
}
