using NUnit.Framework;
using UnityEditor;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The hub probe's lifetime: its loop runs on a background task, so only the current probe may report,
    /// and it stops with Play instead of polling localhost for the rest of edit mode.
    /// </summary>
    public class HubProbeLifetimeTests
    {
        [TearDown]
        public void TearDown()
        {
            HubProbe.StopProbe();
        }

        [Test]
        public void AReplacedProbe_CannotOverwriteTheNewOnesAnswer()
        {
            var stale = HubProbe.BeginProbe();
            var current = HubProbe.BeginProbe();

            Assert.IsTrue(stale.IsCancellationRequested);
            Assert.IsTrue(HubProbe.Publish(current, new HubInfo { State = HubState.Waiting }));
            // The old loop's request comes back late (Play without a domain reload, the next Play's probe running).
            Assert.IsFalse(HubProbe.Publish(stale, new HubInfo { State = HubState.Ready }));
            Assert.AreEqual(HubState.Waiting, HubProbe.Latest.State);
        }

        [Test]
        public void ANewProbe_StartsWithoutAnAnswer()
        {
            var first = HubProbe.BeginProbe();
            Assert.IsTrue(HubProbe.Publish(first, new HubInfo { State = HubState.Ready }));

            HubProbe.BeginProbe();

            Assert.IsNull(HubProbe.Latest);
        }

        [Test]
        public void AStoppedProbe_ReportsNothing()
        {
            var probe = HubProbe.BeginProbe();

            HubProbe.StopProbe();

            Assert.IsTrue(probe.IsCancellationRequested);
            Assert.IsFalse(HubProbe.Probing);
            Assert.IsFalse(HubProbe.Publish(probe, new HubInfo { State = HubState.Ready }));
            Assert.IsNull(HubProbe.Latest);
        }

        [Test]
        public void ExitingPlayMode_StopsTheProbe()
        {
            var probe = HubProbe.BeginProbe();
            Assert.IsTrue(HubProbe.StopsWithPlay);

            HubProbe.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            Assert.IsTrue(probe.IsCancellationRequested);
            Assert.IsFalse(HubProbe.Probing);
            Assert.IsFalse(HubProbe.StopsWithPlay);
            Assert.IsFalse(HubProbe.Publish(probe, new HubInfo { State = HubState.Ready }));
        }

        [TestCase(PlayModeStateChange.EnteredPlayMode)]
        [TestCase(PlayModeStateChange.ExitingEditMode)]
        [TestCase(PlayModeStateChange.EnteredEditMode)]
        public void OtherPlayModeChanges_KeepTheProbe(PlayModeStateChange change)
        {
            var probe = HubProbe.BeginProbe();

            HubProbe.OnPlayModeStateChanged(change);

            Assert.IsFalse(probe.IsCancellationRequested);
            Assert.IsTrue(HubProbe.Probing);
            Assert.IsTrue(HubProbe.Publish(probe, new HubInfo { State = HubState.Waiting }));
        }

        [Test]
        public void TheProbe_ListensForPlayStopping_OnlyWhileItRuns()
        {
            Assert.IsFalse(HubProbe.StopsWithPlay);

            HubProbe.BeginProbe();
            HubProbe.BeginProbe();
            Assert.IsTrue(HubProbe.StopsWithPlay);

            HubProbe.StopProbe();
            Assert.IsFalse(HubProbe.StopsWithPlay);
        }
    }
}
