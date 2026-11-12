using System;
using System.Collections.Generic;
using System.Globalization;
using BS.LocalMultiplayer.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Which networked-object Ids the Local Multiplayer window flags: empty ones, ones another object shares, and
    /// numeric ids (what BSObjectId writes for itself, different in every editor process). Then which scenes
    /// "Assign Stable Ids" changed, the only ones it offers to save.
    /// </summary>
    public class ObjectIdSweepTests
    {
        [TestCase(null)]
        [TestCase("")]
        public void NoId_IsEmpty(string id)
        {
            Assert.AreEqual(IdProblem.Empty, ObjectIdSweep.Classify(id, 0));
        }

        [Test]
        public void Empty_ComesBeforeDuplicate()
        {
            Assert.AreEqual(IdProblem.Empty, ObjectIdSweep.Classify("", 3));
        }

        [TestCase("kQx3AbcDefGhIjKlMnOpQr", 2)]
        [TestCase("-25810", 2)]
        [TestCase("seat", 5)]
        public void SharedId_IsDuplicate(string id, int occurrences)
        {
            // A duplicate is the stronger finding even when the Id is also an instance id.
            Assert.AreEqual(IdProblem.Duplicate, ObjectIdSweep.Classify(id, occurrences));
        }

        [TestCase("-25810")]
        [TestCase("12345")]
        [TestCase("0")]
        [TestCase("-2147483648")]
        public void InstanceIdShaped_IsInstanceIdStyle(string id)
        {
            Assert.AreEqual(IdProblem.InstanceIdStyle, ObjectIdSweep.Classify(id, 1));
        }

        [TestCase("kQx3AbcDefGhIjKlMnOpQr")]
        [TestCase("seat-1")]
        [TestCase("12a")]
        [TestCase("-")]
        [TestCase("--5")]
        [TestCase("+5")]
        [TestCase("1-2")]
        [TestCase(" 5")]
        [TestCase("5 ")]
        [TestCase("\u0663")]
        public void OtherIds_AreStable(string id)
        {
            Assert.AreEqual(IdProblem.None, ObjectIdSweep.Classify(id, 1));
        }

        [Test]
        public void EveryInstanceIdString_IsInstanceIdStyle()
        {
            foreach (var value in new[] { int.MinValue, -123456, -1, 0, 1, 987654, int.MaxValue })
            {
                // BSScene.UnityId(...).ToString(), or an old SDK's instance id, under any culture: Int32 never groups
                // digits or localises them.
                Assert.IsTrue(ObjectIdSweep.IsInstanceIdStyle(value.ToString(CultureInfo.InvariantCulture)), value.ToString());
            }
        }

        [Test]
        public void ForceGeneratedIds_AreStable()
        {
            // The shape BSObjectId.ForceGenerateId writes: a GUID in URL-safe base64 without padding.
            for (var i = 0; i < 1000; i++)
            {
                var id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                Assert.AreEqual(IdProblem.None, ObjectIdSweep.Classify(id, 1), id);
            }
        }

        [Test]
        public void EveryProblem_HasItsOwnDescription()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (IdProblem problem in Enum.GetValues(typeof(IdProblem)))
            {
                var text = ObjectIdSweep.Describe(problem);
                Assert.IsFalse(string.IsNullOrEmpty(text), problem.ToString());
                Assert.IsTrue(seen.Add(text), problem.ToString());
            }
        }

        [Test]
        public void Assign_ReportsEachSceneItChanged_Once()
        {
            // Preview scenes, as the SDK's scene check tests use: nothing the creator has open is touched.
            var changedScene = EditorSceneManager.NewPreviewScene();
            var otherScene = EditorSceneManager.NewPreviewScene();
            var created = new List<BSObjectId>();
            try
            {
                var first = CreateId(changedScene, "first", created);
                var second = CreateId(changedScene, "second", created);
                var elsewhere = CreateId(otherScene, "elsewhere", created);
                elsewhere.Id = "kept";

                var touched = new List<Scene>();
                var changed = ObjectIdSweep.Assign(new[]
                {
                    new ObjectIdSweep.Finding { Component = first, Problem = IdProblem.Empty },
                    new ObjectIdSweep.Finding { Component = second, Problem = IdProblem.Duplicate },
                }, touched);

                Assert.AreEqual(2, changed);
                // Two Ids changed in one scene: it is listed once. The other scene changed nowhere, so "Save the
                // changed scenes" leaves whatever unsaved edits it has alone.
                Assert.AreEqual(1, touched.Count);
                Assert.IsTrue(touched[0] == changedScene);
                Assert.IsFalse(touched.Contains(otherScene));
                Assert.AreEqual(IdProblem.None, ObjectIdSweep.Classify(first.Id, 1), first.Id);
                Assert.AreEqual(IdProblem.None, ObjectIdSweep.Classify(second.Id, 1), second.Id);
                Assert.AreNotEqual(first.Id, second.Id);
                Assert.AreEqual("kept", elsewhere.Id);
            }
            finally
            {
                // The new Ids were recorded for Undo; the objects die with the preview scenes.
                foreach (var id in created)
                {
                    if (id != null)
                    {
                        Undo.ClearUndo(id);
                    }
                }
                EditorSceneManager.ClosePreviewScene(changedScene);
                EditorSceneManager.ClosePreviewScene(otherScene);
            }
        }

        [Test]
        public void Assign_WithoutFindings_TouchesNothing()
        {
            var touched = new List<Scene>();
            Assert.AreEqual(0, ObjectIdSweep.Assign(new ObjectIdSweep.Finding[0], touched));
            Assert.AreEqual(0, ObjectIdSweep.Assign(null, touched));
            Assert.AreEqual(0, touched.Count);
        }

        [Test]
        public void ScenesToSave_IsEmpty_WhenNothingWasTouched()
        {
            // Whatever the creator has open and unsaved stays unsaved when the sweep changed no scene.
            Assert.AreEqual(0, ObjectIdSweep.ScenesToSave(new List<Scene>()).Count);
            Assert.AreEqual(0, ObjectIdSweep.ScenesToSave(null).Count);
        }

        static BSObjectId CreateId(Scene scene, string name, List<BSObjectId> created)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            var id = go.AddComponent<BSObjectId>();
            created.Add(id);
            return id;
        }
    }
}
