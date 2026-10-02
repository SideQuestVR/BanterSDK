using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SideQuest.Ora;
using UnityEngine;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// BSPipe ignores what the space view sends until a page has started loading. The view starts on about:blank, Ora
    /// injects the SDK there without ever reporting a LoadStarted for it, and that document's requests used to reach
    /// BSScene before its settings existed ("Error updating object" for a seat's SitPoint at every Play).
    /// The pipe here has no link, so the load-started listener throws at link.scene: after it has decided about the
    /// gate and before BSScene.OnLoad (which needs Play mode) could run. That also pins the order.
    /// </summary>
    public class BSPipeGateTests
    {
        GameObject _viewObject;
        OraView _view;
        BSPipe _pipe;
        readonly List<string> _received = new List<string>();

        [SetUp]
        public void SetUp()
        {
            // Inactive, so OraView.Awake never runs and no browser window is created.
            _viewObject = new GameObject("BSPipeGateTests view");
            _viewObject.SetActive(false);
            _view = _viewObject.AddComponent<OraView>();
            _pipe = new BSPipe(null, _view, null);
            _received.Clear();
            _pipe.Start(() => { }, _received.Add);
        }

        [TearDown]
        public void TearDown()
        {
            if (_viewObject != null) UnityEngine.Object.DestroyImmediate(_viewObject);
        }

        void LoadStarted(string url)
        {
            Assert.Throws<System.NullReferenceException>(() => _view.loadStarted.Invoke(url), "the listener reaches link.scene");
        }

        void Message() => _view.browserMessage.Invoke("0", "default-message", APICommands.SCENE_START);

        [Test]
        public void MessagesBeforeTheFirstPageLoad_AreIgnored_WithOneLogLine()
        {
            LogAssert.Expect(LogType.Log, new Regex("Ignoring messages from the space view's start-up page"));
            Message();
            Message();
            Assert.IsEmpty(_received);
            Assert.IsFalse(_pipe.PageLoadStarted);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ADomReadyBeforeTheFirstPageLoad_IsIgnored()
        {
            // Handling it would touch link.scene, and there is no link here.
            Assert.DoesNotThrow(() => _view.domReady.Invoke("about:blank"));
        }

        [Test]
        public void APageLoadStarting_OpensThePipe()
        {
            LoadStarted("http://localhost:42068/");
            Assert.IsTrue(_pipe.PageLoadStarted);
            Message();
            CollectionAssert.AreEqual(new[] { APICommands.SCENE_START }, _received);
        }

        [Test]
        public void TheStartUpPageLoading_KeepsThePipeClosed()
        {
            LoadStarted("about:blank");
            Assert.IsFalse(_pipe.PageLoadStarted);
            Message();
            Assert.IsEmpty(_received);
        }

        [Test]
        public void OnceAPageStartedLoading_DomReadyIsHandled()
        {
            LoadStarted("http://localhost:42068/");
            Assert.Throws<System.NullReferenceException>(() => _view.domReady.Invoke("http://localhost:42068/"), "the listener reaches link.scene");
        }

        [Test]
        public void TheIgnoredMessages_AreCountedWhenThePipeOpens()
        {
            LogAssert.Expect(LogType.Log, new Regex("Ignoring messages from the space view's start-up page"));
            Message();
            Message();
            LogAssert.Expect(LogType.Log, new Regex("Ignored 2 message\\(s\\) from the space view's start-up page"));
            LoadStarted("http://localhost:42068/");
        }

        // BSScene and BSSceneSettings constructors have side effects (input actions, a root GameObject): build them bare.
        static BSScene BareScene() => (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));

        [Test]
        public void WhenSettingsReady_CompletesAtOnce_WhenTheSettingsExist()
        {
            var scene = BareScene();
            scene.settings = (BSSceneSettings)FormatterServices.GetUninitializedObject(typeof(BSSceneSettings));
            var ready = scene.WhenSettingsReady("test");
            Assert.IsTrue(ready.IsCompleted);
            Assert.IsTrue(ready.Result);
        }

        [Test]
        public void WhenSettingsReady_IsFalse_OutsidePlay_WhenThereAreNoSettings()
        {
            var ready = BareScene().WhenSettingsReady("test");
            Assert.IsTrue(ready.IsCompleted, "no page load is coming outside Play");
            Assert.IsFalse(ready.Result);
        }
    }
}
