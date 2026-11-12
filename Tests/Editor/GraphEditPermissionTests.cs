using System;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Who may change a world's script machines from its page: the page's own machines always, the world's own only when
    /// the host says so.
    /// </summary>
    public class GraphEditPermissionTests
    {
        GameObject _root;
        Func<bool> _saved;

        [SetUp]
        public void SetUp()
        {
            _saved = GraphEditPermission.CanEditWorldGraphs;
            GraphEditPermission.CanEditWorldGraphs = null;
            _root = new GameObject("root");
        }

        [TearDown]
        public void TearDown()
        {
            GraphEditPermission.CanEditWorldGraphs = _saved;
            UnityEngine.Object.DestroyImmediate(_root);
        }

        ScriptMachine Machine(GameObject on = null) => (on ?? _root).AddComponent<ScriptMachine>();

        [Test]
        public void TheWorldsOwnMachine_IsNotThePagesToChange_WithoutTheHost()
        {
            Assert.IsFalse(GraphEditPermission.MayChange(Machine()), "unset is no");
            GraphEditPermission.CanEditWorldGraphs = () => false;
            Assert.IsFalse(GraphEditPermission.MayChange(Machine()));
        }

        [Test]
        public void TheWorldsOwnMachine_IsTheOwnersToChange()
        {
            GraphEditPermission.CanEditWorldGraphs = () => true;
            Assert.IsTrue(GraphEditPermission.MayChange(Machine()));
        }

        [Test]
        public void AMachineThePageMade_IsAlwaysItsToChange()
        {
            var made = Machine();
            GraphEditPermission.NotePageMade(made);
            Assert.IsTrue(GraphEditPermission.IsPageMade(made));
            Assert.IsTrue(GraphEditPermission.MayChange(made));
            Assert.IsFalse(GraphEditPermission.IsPageMade(Machine()), "only the one it made");
        }

        [Test]
        public void AHostThatThrows_IsANo()
        {
            GraphEditPermission.CanEditWorldGraphs = () => throw new InvalidOperationException("test");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test");
            Assert.IsFalse(GraphEditPermission.MayChange(Machine()));
        }

        [Test]
        public void NoMachine_IsNeverChangeable()
        {
            GraphEditPermission.CanEditWorldGraphs = () => true;
            Assert.IsFalse(GraphEditPermission.MayChange(null));
            GraphEditPermission.NotePageMade(null);
        }

        [Test]
        public void APageCall_EndsOnceEvenIfDisposedTwice()
        {
            var outer = GraphEditPermission.PageCall();
            var inner = GraphEditPermission.PageCall();
            inner.Dispose();
            inner.Dispose();
            Assert.IsTrue(GraphEditPermission.InPageCall);
            outer.Dispose();
            Assert.IsFalse(GraphEditPermission.InPageCall);
        }
    }
}
