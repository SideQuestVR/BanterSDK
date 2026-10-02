using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BS.SDKEditor.Setup;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The Setup panel's checklist: what Fix All runs and skips, and the layer and tag plan behind the
    /// "SDK layers and tags" item.
    /// </summary>
    public class ProjectSetupTests
    {
        // Test checks take constructor arguments, so the real checklist, which only lists checks that have a
        // parameterless constructor, never picks them up.
        sealed class FakeCheck : SetupCheck
        {
            readonly string _id;
            readonly SetupImportance _importance;
            readonly bool _fixWorks;
            readonly bool _throws;
            SetupState _state;
            public int Fixes;

            public FakeCheck(string id, SetupImportance importance, SetupState state, bool fixWorks = true, bool throws = false)
            {
                _id = id;
                _importance = importance;
                _state = state;
                _fixWorks = fixWorks;
                _throws = throws;
            }

            public override string Id => _id;
            public override string Title => _id;
            public override string Why => "why";
            public override string WithoutIt => "without it";
            public override SetupImportance Importance => _importance;

            public override SetupStatus Evaluate()
            {
                if (_throws)
                    throw new InvalidOperationException("check exploded");
                switch (_state)
                {
                    case SetupState.Done: return SetupStatus.Done("done");
                    case SetupState.NeedsManualFix: return SetupStatus.NeedsManualFix("by hand");
                    default: return SetupStatus.NeedsFix("broken");
                }
            }

            public override bool Fix()
            {
                Fixes++;
                if (_fixWorks)
                    _state = SetupState.Done;
                return _fixWorks;
            }
        }

        [Test]
        public void FixAll_RunsTheRequiredAndRecommendedFixes()
        {
            var required = new FakeCheck("required", SetupImportance.Required, SetupState.NeedsFix);
            var recommended = new FakeCheck("recommended", SetupImportance.Recommended, SetupState.NeedsFix);
            var result = ProjectSetup.FixAll(new SetupCheck[] { required, recommended });
            Assert.AreEqual(1, required.Fixes);
            Assert.AreEqual(1, recommended.Fixes);
            CollectionAssert.AreEqual(new[] { required, recommended }, result.Fixed);
            Assert.IsEmpty(result.Failed);
        }

        [Test]
        public void FixAll_LeavesOptionalManualAndFinishedItemsAlone()
        {
            var optional = new FakeCheck("optional", SetupImportance.Optional, SetupState.NeedsFix);
            var manual = new FakeCheck("manual", SetupImportance.Required, SetupState.NeedsManualFix);
            var done = new FakeCheck("done", SetupImportance.Required, SetupState.Done);
            var result = ProjectSetup.FixAll(new SetupCheck[] { optional, manual, done });
            Assert.AreEqual(0, optional.Fixes + manual.Fixes + done.Fixes);
            Assert.IsEmpty(result.Fixed);
        }

        [Test]
        public void FixAll_ReportsAFixThatDidntTake()
        {
            var stuck = new FakeCheck("stuck", SetupImportance.Required, SetupState.NeedsFix, fixWorks: false);
            var result = ProjectSetup.FixAll(new SetupCheck[] { stuck });
            CollectionAssert.AreEqual(new[] { stuck }, result.Failed);
        }

        [Test]
        public void ACheckThatThrows_BecomesAManualItem()
        {
            LogAssert.Expect(LogType.Exception, new Regex("check exploded"));
            var status = ProjectSetup.Evaluate(new FakeCheck("throws", SetupImportance.Required, SetupState.NeedsFix, throws: true));
            Assert.AreEqual(SetupState.NeedsManualFix, status.State);
            StringAssert.Contains("check exploded", status.Summary);
        }

        [Test]
        public void TheRealChecklist_ExplainsEveryItem()
        {
            var checks = ProjectSetup.CreateChecks();
            Assert.IsNotEmpty(checks);
            CollectionAssert.AllItemsAreUnique(checks.Select(check => check.Id));
            foreach (var check in checks)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(check.Title), check.Id + " has no title");
                Assert.IsFalse(string.IsNullOrWhiteSpace(check.Why), check.Id + " doesn't say why it's needed");
                Assert.IsFalse(string.IsNullOrWhiteSpace(check.WithoutIt), check.Id + " doesn't say what goes wrong without it");
            }
            CollectionAssert.IsSubsetOf(new[] { "sdk.layers-tags", "sdk.visual-scripting", "sdk.api-compatibility", "sdk.webroot", "sdk.input-handling",
                    "sdk.textmeshpro", "sdk.urp", "sdk.graphics-apis", "sdk.color-space" },
                checks.Select(check => check.Id).ToList());
        }

        [Test]
        public void TheRealChecklist_ChecksWithoutThrowing()
        {
            foreach (var check in ProjectSetup.CreateChecks())
                Assert.DoesNotThrow(() => check.Evaluate(), check.Id);
        }

        // -- Layers and tags -------------------------------------------------------

        // A new project's TagManager: Unity's built-in layers and no tags.
        static string[] NewProjectLayers()
        {
            var layers = new string[32];
            for (var i = 0; i < layers.Length; i++)
                layers[i] = "";
            layers[0] = "Default";
            layers[1] = "TransparentFX";
            layers[2] = "Ignore Raycast";
            layers[4] = "Water";
            layers[5] = "UI";
            return layers;
        }

        static string[] SdkLayers()
        {
            var layers = NewProjectLayers();
            foreach (var layer in InitialiseOnLoad.layersToAdd)
                layers[layer.Key] = layer.Value;
            return layers;
        }

        static string[] SdkTags() => InitialiseOnLoad.tagsToAdd.OrderBy(tag => tag.Key).Select(tag => tag.Value).ToArray();

        [Test]
        public void ANewProject_GetsEverySdkLayerAndTag_WithNothingRenamed()
        {
            var plan = InitialiseOnLoad.PlanLayersAndTags(NewProjectLayers(), new string[0]);
            Assert.AreEqual(InitialiseOnLoad.layersToAdd.Count, plan.Layers.Count);
            Assert.IsEmpty(plan.Renames);
            CollectionAssert.AreEqual(SdkTags(), plan.MissingTags);
        }

        [Test]
        public void AProjectThatIsSetUp_HasNothingToDo()
        {
            Assert.IsTrue(InitialiseOnLoad.PlanLayersAndTags(SdkLayers(), SdkTags()).IsEmpty);
        }

        [Test]
        public void TagsCount_ByName_InAnyOrder_AndTheProjectsOwnAreKept()
        {
            // Tags are stored on GameObjects as text, so their order doesn't matter and nothing replaces "Enemy".
            var tags = new[] { "Enemy" }.Concat(SdkTags().Reverse()).ToArray();
            Assert.IsEmpty(InitialiseOnLoad.PlanLayersAndTags(SdkLayers(), tags).MissingTags);
        }

        [Test]
        public void ALayerSlotWithAnotherName_IsReportedAsARename()
        {
            var layers = SdkLayers();
            layers[8] = "Water2";
            var plan = InitialiseOnLoad.PlanLayersAndTags(layers, SdkTags());
            var rename = plan.Renames.Single();
            Assert.AreEqual(8, rename.Index);
            Assert.AreEqual("Water2", rename.Current);
            Assert.AreEqual(InitialiseOnLoad.layersToAdd[8], rename.Wanted);
        }

        [Test]
        public void AnSdkLayerInTheWrongSlot_IsMovedToItsOwn()
        {
            // Scenes store layer numbers: "Grabbable" only works as layer 20.
            var layers = NewProjectLayers();
            layers[9] = "Grabbable";
            var plan = InitialiseOnLoad.PlanLayersAndTags(layers, SdkTags());
            Assert.IsTrue(plan.Layers.Any(change => change.Index == 20 && change.Wanted == "Grabbable"));
            Assert.IsTrue(plan.Renames.Any(change => change.Index == 9 && change.Current == "Grabbable"));
        }
    }
}
