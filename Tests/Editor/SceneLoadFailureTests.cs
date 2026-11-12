using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Every caller drops the task BSScene.LoadUrl returns. A join the world's policy refused, a failed page or a
    /// cancelled load faults it, and that used to reach the error panel much later as a
    /// TaskScheduler.UnobservedTaskException, with no URL. LoadUrl now reads the failure itself: Cancel's failures are
    /// already on the loading screen and are dropped, anything else is logged straight away with the URL.
    /// </summary>
    public class SceneLoadFailureTests
    {
        const string Url = "https://example.worldspace.host";

        [Test]
        public void ARefusedOrCancelledLoad_IsNotAnError()
        {
            var refused = new TaskCompletionSource<bool>();
            var load = BSScene.ObserveLoadFailure(refused.Task, Url);
            refused.SetException(new SceneLoadFailedException("Refused by the world's join policy.", false));

            var cancelled = new TaskCompletionSource<bool>();
            BSScene.ObserveLoadFailure(cancelled.Task, Url);
            cancelled.SetException(new SceneLoadFailedException("Loading Cancelled", true));

            Assert.IsTrue(load.IsFaulted, "a caller that awaits the load still gets the failure");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AnyOtherFailure_IsLoggedAtOnceWithTheUrl()
        {
            var load = new TaskCompletionSource<bool>();
            BSScene.ObserveLoadFailure(load.Task, Url);
            LogAssert.Expect(LogType.Error,
                new Regex(@"^\[LOADING\] Loading https://example\.worldspace\.host failed: System\.InvalidOperationException: bug"));
            load.SetException(new InvalidOperationException("bug"));
        }

        [Test]
        public void ALoadThatFinishesOrIsSuperseded_LogsNothing()
        {
            var finished = new TaskCompletionSource<bool>();
            BSScene.ObserveLoadFailure(finished.Task, Url);
            finished.SetResult(true);

            var superseded = new TaskCompletionSource<bool>();
            BSScene.ObserveLoadFailure(superseded.Task, Url);
            superseded.SetCanceled();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TheMessageStillSaysWhatHappened()
        {
            var refused = new SceneLoadFailedException("Refused by the world's join policy.", false);
            Assert.AreEqual("Load failed: Refused by the world's join policy.", refused.Message);
            Assert.IsFalse(refused.IsUserCancel);

            var cancelled = new SceneLoadFailedException("Loading Cancelled", true);
            Assert.AreEqual("Cancelled: Loading Cancelled", cancelled.Message);
            Assert.IsTrue(cancelled.IsUserCancel);
        }
    }
}
