using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Every request the page sends gets an answer (issues 8 and 21), except the previous page's leftovers, which get
    /// none: their ids are the old page's and could settle the new page's request with the same id. That each is answered
    /// only once, and only to the document that asked, is the bridge's ledger (Bridge~/dotnet-tests).
    /// The link has no pipe here, so Send buffers into its private `messages` list.
    /// </summary>
    public class BSLinkRequestReplyTests
    {
        GameObject _go;
        BSLink _link;
        List<string> _sent;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("BSLinkRequestReplyTests");
            _go.SetActive(false); // no Awake/Start
            _link = _go.AddComponent<BSLink>();
            _link.scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            _link.scene.link = _link;
            typeof(BSScene).GetField("banterComponents", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_link.scene, new ConcurrentDictionary<int, BSComponent>());
            _sent = (List<string>)typeof(BSLink).GetField("messages", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_link);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        void Handle(string message) =>
            typeof(BSLink).GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_link, new object[] { message });

        static string Request(int id, string command, string data) =>
            APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + id + MessageDelimiters.PRIMARY + command + MessageDelimiters.PRIMARY + data;

        // A request whose handler throws at once: COMPONENT_REMOVED with an id that isn't a number.
        static string Throwing(int id) => Request(id, APICommands.COMPONENT_REMOVED, "abc");

        static string ErrorPrefix(int id) =>
            APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + id + MessageDelimiters.PRIMARY + APICommands.REQUEST_ERROR + MessageDelimiters.PRIMARY;

        [Test]
        public void AfterALoadFailure_ARequestIsRejected()
        {
            _link.scene.state = SceneState.LOAD_FAILED;
            Handle(Request(7, APICommands.YT_INFO, "x"));
            Assert.AreEqual(1, _sent.Count);
            StringAssert.StartsWith(ErrorPrefix(7), _sent[0]);
            StringAssert.Contains("scene not ready", _sent[0]);
        }

        [Test]
        public void ARequestThatThrows_IsRejected()
        {
            _link.scene.state = SceneState.SCENE_START;
            Handle(Throwing(8));
            Assert.AreEqual(1, _sent.Count);
            StringAssert.StartsWith(ErrorPrefix(8) + APICommands.COMPONENT_REMOVED, _sent[0]);
        }

        [Test]
        public void TheReservedNoReplyId_GetsNoReply()
        {
            _link.scene.state = SceneState.LOAD_FAILED;
            Handle(Request(BSLink.NoReplyRequestId, APICommands.DEEP_LINK, "1"));
            _link.scene.state = SceneState.SCENE_START;
            Handle(Throwing(BSLink.NoReplyRequestId));
            Assert.IsEmpty(_sent);
        }

        [Test]
        public void StillLoadingAt30s_IsReady_AndAnswers()
        {
            _link.scene.state = SceneState.NOTHING_20S;
            Assert.IsTrue(_link.scene.isReady);
            Handle(Throwing(9));
            Assert.AreEqual(1, _sent.Count);
            StringAssert.StartsWith(ErrorPrefix(9), _sent[0]);
        }

        [Test]
        public void WhileANewPageLoads_TheOldPagesRequests_GetNoReply()
        {
            _link.scene.state = SceneState.NONE;
            Handle(Throwing(3));
            Assert.IsEmpty(_sent);

            LogAssert.Expect(LogType.Log, new Regex("Dropped 1 request"));
            Handle(APICommands.SCENE_START);
            Assert.AreEqual(SceneState.SCENE_START, _link.scene.state);
            Assert.IsEmpty(_sent);
        }

        [Test]
        public void MessagesThatAreNotReplies_AreUntouched()
        {
            _link.Send(APICommands.UPDATE + "x");
            _link.Send(APICommands.UPDATE + "x");
            Assert.AreEqual(2, _sent.Count);
        }

        [Test]
        public void TheReplacedPagesThirtySecondTimer_DoesNotMakeTheNewLoadReady()
        {
            _link.scene.state = SceneState.NONE;
            Handle(APICommands.NOTHING_20S);
            Assert.AreEqual(SceneState.NONE, _link.scene.state);
        }

        const string S = MessageDelimiters.SECONDARY;

        // Each answers before any await, so Edit Mode can check them. "undefined": the page asked before the
        // component was linked. "5§": no properties. 424242: not a registered component.
        [TestCase("undefined" + S + "1")]
        [TestCase("5" + S)]
        [TestCase("424242" + S + "1")]
        public void QueryComponents_AnswersOnceWithAnError(string msg)
        {
            var task = _link.scene.QueryComponents(msg, 7);
            Assert.IsTrue(task.IsCompleted && !task.IsFaulted);
            Assert.AreEqual(1, _sent.Count);
            StringAssert.StartsWith(ErrorPrefix(7), _sent[0]);
        }

        [Test]
        public void WatchProperties_AnUnlinkedComponent_AnswersOnceWithAnError()
        {
            var task = _link.scene.WatchProperties("undefined" + MessageDelimiters.PRIMARY + "1", 7);
            Assert.IsTrue(task.IsCompleted && !task.IsFaulted);
            Assert.AreEqual(1, _sent.Count);
            StringAssert.StartsWith(ErrorPrefix(7), _sent[0]);
        }
    }
}
