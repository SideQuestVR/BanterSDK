using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The checklist runner: which issues block which builds, and what happens when a check fails.
    /// </summary>
    public class BuildChecklistTests
    {
        // Test checks take constructor arguments, so the real checklist, which only creates checks
        // that have a parameterless constructor, never picks them up.
        sealed class FixedCheck : BuildCheck
        {
            readonly BuildCheckIssue[] _found;
            public FixedCheck(params BuildCheckIssue[] found) { _found = found; }
            public override string Id => "test.fixed";
            public override string Title => "Fixed";
            public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues) => issues.AddRange(_found);
        }

        sealed class ThrowingCheck : BuildCheck
        {
            readonly bool _block;
            public ThrowingCheck(bool block) { _block = block; }
            public override string Id => "test.throws";
            public override string Title => "Throws";
            public override bool BlockOnException => _block;
            public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues) =>
                throw new InvalidOperationException("check exploded");
        }

        Scene _scene;

        [SetUp]
        public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(_scene);

        BuildChecklistResult Run(params BuildCheck[] checks)
        {
            using (var context = BuildCheckContext.ForScene(_scene, new[] { BuildTarget.Android }))
                return BuildChecklist.Run(context, checks);
        }

        static BuildCheckIssue Issue(BuildCheckSeverity severity, bool overridable = true) =>
            new BuildCheckIssue("test", severity, "issue") { Overridable = overridable };

        [Test]
        public void NoIssues_PassesEverything()
        {
            var result = Run(new FixedCheck());
            Assert.AreEqual("All checks passed", result.Summary);
            Assert.IsFalse(result.HasProblems);
            Assert.IsFalse(result.BlocksInteractive);
            Assert.IsFalse(result.BlocksBatch);
        }

        [Test]
        public void Notes_AreNotProblems()
        {
            var result = Run(new FixedCheck(Issue(BuildCheckSeverity.Info)));
            Assert.IsFalse(result.HasProblems);
            Assert.IsFalse(result.BlocksBatch);
        }

        [Test]
        public void Warnings_NeedConfirming_ButPassUnattendedBuilds()
        {
            var result = Run(new FixedCheck(Issue(BuildCheckSeverity.Warning)));
            Assert.IsTrue(result.HasProblems);
            Assert.IsFalse(result.BlocksInteractive);
            Assert.IsFalse(result.BlocksBatch);
        }

        [Test]
        public void OverridableError_BlocksOnlyUnattendedBuilds()
        {
            var result = Run(new FixedCheck(Issue(BuildCheckSeverity.Error)));
            Assert.IsFalse(result.BlocksInteractive);
            Assert.IsTrue(result.BlocksBatch);
        }

        [Test]
        public void NonOverridableError_BlocksEveryBuild()
        {
            var result = Run(new FixedCheck(Issue(BuildCheckSeverity.Error, overridable: false)));
            Assert.IsTrue(result.BlocksInteractive);
            Assert.IsTrue(result.BlocksBatch);
            Assert.AreEqual(1, result.Blockers.Count());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailingCheck_IsReportedAsAnError(bool blockOnException)
        {
            LogAssert.Expect(LogType.Exception, new Regex("check exploded"));
            var result = Run(new ThrowingCheck(blockOnException), new FixedCheck(Issue(BuildCheckSeverity.Warning)));

            // The checks after it still run.
            Assert.AreEqual(2, result.Issues.Count);
            var failure = result.Issues.Single(issue => issue.CheckId == "test.throws");
            Assert.AreEqual(BuildCheckSeverity.Error, failure.Severity);
            Assert.AreEqual(!blockOnException, failure.Overridable);
            Assert.AreEqual(blockOnException, result.BlocksInteractive);
        }

        [Test]
        public void Summary_CountsEachSeverity()
        {
            var result = Run(new FixedCheck(
                Issue(BuildCheckSeverity.Error), Issue(BuildCheckSeverity.Error),
                Issue(BuildCheckSeverity.Warning), Issue(BuildCheckSeverity.Info)));
            Assert.AreEqual("2 errors, 1 warning, 1 note", result.Summary);
        }

        [Test]
        public void RealChecklist_HasUniqueIds_AndNoTestChecks()
        {
            var checks = BuildChecklist.CreateChecks();
            Assert.IsNotEmpty(checks);
            CollectionAssert.AllItemsAreUnique(checks.Select(check => check.Id));
            Assert.IsFalse(checks.Any(check => check.GetType().Assembly == typeof(BuildChecklistTests).Assembly));
        }

        [Test]
        public void Target_ResolvesOnlyInLoadedScenes()
        {
            var go = new GameObject("target");
            SceneManager.MoveGameObjectToScene(go, _scene);
            var target = BuildCheckTarget.ForObject(go, "Assets/NotLoaded.unity");
            Assert.AreEqual("target", target.Label);
            Assert.IsNull(target.Resolve());
        }
    }
}
