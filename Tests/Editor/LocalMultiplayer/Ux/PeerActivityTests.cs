using System.Collections.Generic;
using BS.LocalMultiplayer.Overlay;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// What the overlay shows per player from the session's events: the measured pose rate, the seat from the
    /// "pilot" key and the count of "attachment_&lt;Id&gt;" keys (AttachmentNetworkBridge's engine user state).
    /// </summary>
    public class PeerActivityTests
    {
        const string Rsid = "rsess_a";
        const string Other = "rsess_b";

        [Test]
        public void PoseRate_IsFramesPerSecondOverTheWindow()
        {
            var activity = new PeerActivity();
            Assert.IsFalse(activity.Tick(10.0), "the first tick only starts the window");
            for (var i = 0; i < 30; i++)
            {
                activity.OnParticipantFrame(Rsid);
            }
            Assert.IsFalse(activity.Tick(10.5), "half a second is not a window yet");
            Assert.IsTrue(activity.Tick(11.0));
            Assert.IsTrue(activity.TryGet(Rsid, out var entry));
            Assert.AreEqual(30f, entry.PoseHz, 1e-3f);

            // A longer window divides by the time it really took.
            for (var i = 0; i < 30; i++)
            {
                activity.OnParticipantFrame(Rsid);
            }
            Assert.IsTrue(activity.Tick(12.25));
            Assert.AreEqual(24f, entry.PoseHz, 1e-3f);
        }

        [Test]
        public void PoseRate_FallsToZero_WhenFramesStop()
        {
            var activity = new PeerActivity();
            activity.Tick(0.0);
            activity.OnParticipantFrame(Rsid);
            activity.Tick(1.0);
            Assert.IsTrue(activity.Tick(2.0), "1 Hz to 0 Hz is a change");
            Assert.IsTrue(activity.TryGet(Rsid, out var entry));
            Assert.AreEqual(0f, entry.PoseHz);
            Assert.IsFalse(activity.Tick(3.0), "still 0 Hz");
        }

        [Test]
        public void APlayerThatNeverSentAPose_StaysUnmeasured()
        {
            var activity = new PeerActivity();
            activity.Tick(0.0);
            activity.OnEngineState(Rsid, "pilot", "seat1", false);
            activity.Tick(1.0);
            Assert.IsTrue(activity.TryGet(Rsid, out var entry));
            Assert.Less(entry.PoseHz, 0f);
        }

        [Test]
        public void Pilot_GivesTheSeat()
        {
            var activity = new PeerActivity();
            Assert.IsTrue(activity.OnEngineState(Rsid, "pilot", "kQx3|0|0.2|0|0|0|0|1", false));
            Assert.IsTrue(activity.TryGet(Rsid, out var entry));
            Assert.AreEqual("kQx3", entry.SeatId);

            Assert.IsFalse(activity.OnEngineState(Rsid, "pilot", "kQx3|0|0.25|0|0|0|0|1", false), "same seat, new offset");
            Assert.IsTrue(activity.OnEngineState(Rsid, "pilot", "", false), "standing up writes an empty value");
            Assert.AreEqual("", entry.SeatId);

            activity.OnEngineState(Rsid, "pilot", "seat2", false);
            Assert.IsTrue(activity.OnEngineState(Rsid, "pilot", null, true));
            Assert.AreEqual("", entry.SeatId);
        }

        [Test]
        public void StandingUp_ForSomeoneUnknown_AddsNobody()
        {
            var activity = new PeerActivity();
            Assert.IsFalse(activity.OnEngineState(Rsid, "pilot", "", false));
            Assert.IsFalse(activity.OnEngineState(Rsid, "attachment_hat", "", false));
            Assert.AreEqual(0, activity.Count);
        }

        [Test]
        public void Attachments_CountKeysWithAValue()
        {
            var activity = new PeerActivity();
            Assert.IsTrue(activity.OnEngineState(Rsid, "attachment_hat", "v1|1|0|8|0|1|0.1|-0.2|0.3|0|0|0|1", false));
            Assert.IsTrue(activity.OnEngineState(Rsid, "attachment_sword", "v1|0|0|5|0|1|0|0|0|0|0|0|1", false));
            Assert.IsFalse(activity.OnEngineState(Rsid, "attachment_hat", "v1|1|0|8|0|1|0|0|0|0|0|0|1", false), "moved, still one");
            Assert.IsTrue(activity.TryGet(Rsid, out var entry));
            Assert.AreEqual(2, entry.Attachments);

            Assert.IsTrue(activity.OnEngineState(Rsid, "attachment_hat", "", false), "detaching writes an empty value");
            Assert.AreEqual(1, entry.Attachments);
            Assert.IsTrue(activity.OnEngineState(Rsid, "attachment_sword", null, true));
            Assert.AreEqual(0, entry.Attachments);
            Assert.IsFalse(activity.OnEngineState(Rsid, "attachment_sword", null, true));
        }

        [Test]
        public void OtherPaths_AndMissingIds_AreIgnored()
        {
            var activity = new PeerActivity();
            Assert.IsFalse(activity.OnEngineState(Rsid, "someEngineKey", "1", false));
            Assert.IsFalse(activity.OnEngineState(null, "pilot", "seat", false));
            Assert.IsFalse(activity.OnEngineState(Rsid, null, "seat", false));
            activity.OnParticipantFrame(null);
            activity.OnParticipantFrame("");
            Assert.AreEqual(0, activity.Count);
            Assert.IsFalse(activity.TryGet(null, out _));
        }

        [Test]
        public void Snapshot_ReplacesWhatWasKnown()
        {
            var activity = new PeerActivity();
            activity.OnEngineState(Rsid, "pilot", "oldSeat", false);
            activity.OnEngineState(Rsid, "attachment_old", "v1", false);

            activity.OnSnapshot(new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [Rsid] = new Dictionary<string, string> { ["attachment_new"] = "v1" },
                [Other] = new Dictionary<string, string> { ["pilot"] = "seatB|0|0.2|0|0|0|0|1" },
            });

            Assert.IsTrue(activity.TryGet(Rsid, out var mine));
            Assert.AreEqual("", mine.SeatId);
            Assert.AreEqual(1, mine.Attachments);
            Assert.IsTrue(activity.TryGet(Other, out var theirs));
            Assert.AreEqual("seatB", theirs.SeatId);

            activity.OnSnapshot(null);
            Assert.AreEqual("", theirs.SeatId);
            Assert.AreEqual(0, mine.Attachments);
        }

        [Test]
        public void Removed_AndClear_ForgetPlayers()
        {
            var activity = new PeerActivity();
            activity.OnParticipantFrame(Rsid);
            activity.OnParticipantFrame(Other);
            Assert.IsTrue(activity.OnRemoved(Rsid));
            Assert.IsFalse(activity.OnRemoved(Rsid));
            Assert.IsFalse(activity.TryGet(Rsid, out _));
            Assert.IsTrue(activity.TryGet(Other, out _));

            activity.Clear();
            Assert.AreEqual(0, activity.Count);
        }

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("seat", "seat")]
        [TestCase("seat|0|0.2|0|0|0|0|1", "seat")]
        [TestCase("|0|0.2|0", "")]
        public void SeatOf_IsTheFirstField(string pilot, string expected)
        {
            Assert.AreEqual(expected, PeerActivity.SeatOf(pilot));
        }
    }
}
