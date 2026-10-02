using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using BS.LocalMultiplayer.Overlay;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The overlay's and the window's wording: short ids, the hidden-panel pill, rates, labels, the hub line and
    /// what a room clear came to.
    /// </summary>
    public class OverlayTextTests
    {
        const string Dash = "\u2014";
        const string Dot = " \u00B7 ";

        [TestCase(null, Dash)]
        [TestCase("", Dash)]
        [TestCase("rsess_1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d", "1a2b3c4d")]
        [TestCase("peer_abcdef12-3456", "abcdef12")]
        [TestCase("0123456789abcdef0123456789abcdef", "01234567")]
        [TestCase("abc", "abc")]
        [TestCase("rsess_ab", "ab")]
        [TestCase("rsess_", "rsess_")]
        public void ShortId_KeepsTheTellingPart(string id, string expected)
        {
            Assert.AreEqual(expected, OverlayText.ShortId(id));
        }

        [Test]
        public void ShortId_TakesTheRequestedLength()
        {
            Assert.AreEqual("1a2b", OverlayText.ShortId("rsess_1a2b3c4d", 4));
            Assert.AreEqual("1", OverlayText.ShortId("rsess_1a2b3c4d", 0));
        }

        // " | " stands for the middle-dot separator.
        [TestCase(SessionState.Joined, 2, "F8", "LocalMP | Joined | 3 players | F8")]
        [TestCase(SessionState.Joined, 0, "F8", "LocalMP | Joined | 1 player | F8")]
        [TestCase(SessionState.Connecting, 2, "F8", "LocalMP | Connecting | F8")]
        [TestCase(SessionState.Reconnecting, 1, "F9", "LocalMP | Reconnecting | F9")]
        [TestCase(SessionState.Joined, 3, null, "LocalMP | Joined | 4 players")]
        [TestCase(SessionState.Idle, 0, "F8", "LocalMP | Not joined | F8")]
        public void Pill_IsOneLine(SessionState state, int peers, string key, string expected)
        {
            Assert.AreEqual(expected.Replace(" | ", Dot), OverlayText.Pill(state, peers, key));
        }

        [Test]
        public void PlayerCount_IncludesYou_OnlyWhileJoined()
        {
            Assert.AreEqual(1, OverlayText.PlayerCount(SessionState.Joined, 0));
            Assert.AreEqual(4, OverlayText.PlayerCount(SessionState.Joined, 3));
            Assert.AreEqual(1, OverlayText.PlayerCount(SessionState.Joined, -2));
            foreach (SessionState state in Enum.GetValues(typeof(SessionState)))
            {
                if (state != SessionState.Joined)
                {
                    Assert.AreEqual(0, OverlayText.PlayerCount(state, 3), state.ToString());
                }
            }
        }

        [TestCase(0, "0 players")]
        [TestCase(1, "1 player")]
        [TestCase(4, "4 players")]
        public void Players_Counts(int count, string expected)
        {
            Assert.AreEqual(expected, OverlayText.Players(count));
        }

        [TestCase(1, "1 attachment")]
        [TestCase(2, "2 attachments")]
        public void Attachments_Counts(int count, string expected)
        {
            Assert.AreEqual(expected, OverlayText.Attachments(count));
        }

        [TestCase(double.NaN, Dash)]
        [TestCase(double.PositiveInfinity, Dash)]
        [TestCase(0.0, Dash)]
        [TestCase(-5.0, Dash)]
        [TestCase(0.4, "<1 ms")]
        [TestCase(3.4, "3 ms")]
        [TestCase(12.6, "13 ms")]
        public void Rtt_RoundsToWholeMilliseconds(double ms, string expected)
        {
            Assert.AreEqual(expected, OverlayText.Rtt(ms));
        }

        [TestCase(-1f, Dash)]
        [TestCase(float.NaN, Dash)]
        [TestCase(0f, "0 Hz")]
        [TestCase(29.6f, "30 Hz")]
        public void PoseRate_RoundsToWholeHertz(float hz, string expected)
        {
            Assert.AreEqual(expected, OverlayText.PoseRate(hz));
        }

        [Test]
        public void Numbers_IgnoreTheCurrentCulture()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual("1234 ms", OverlayText.Rtt(1234.4));
                Assert.AreEqual("1500 Hz", OverlayText.PoseRate(1500f));
                Assert.AreEqual("1000 players", OverlayText.Players(1000));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void EveryState_HasItsOwnLabel()
        {
            var seen = new HashSet<string>();
            foreach (SessionState state in Enum.GetValues(typeof(SessionState)))
            {
                var label = OverlayText.StateLabel(state);
                Assert.IsFalse(string.IsNullOrEmpty(label), state.ToString());
                Assert.IsTrue(seen.Add(label), state.ToString());
            }
        }

        [Test]
        public void StateColours_SayGoodBusyOrBad()
        {
            var busy = OverlayText.StateColor(SessionState.Connecting);
            Assert.AreEqual(busy, OverlayText.StateColor(SessionState.Joining));
            Assert.AreEqual(busy, OverlayText.StateColor(SessionState.Reconnecting));
            Assert.AreNotEqual(busy, OverlayText.StateColor(SessionState.Joined));
            Assert.AreNotEqual(busy, OverlayText.StateColor(SessionState.Failed));
            Assert.AreNotEqual(OverlayText.StateColor(SessionState.Joined), OverlayText.StateColor(SessionState.Failed));
        }

        [Test]
        public void Roles_HaveTheirOwnLabelsAndColours()
        {
            Assert.AreEqual("owner", OverlayText.RoleLabel(PlayerRole.Owner));
            Assert.AreEqual("moderator", OverlayText.RoleLabel(PlayerRole.Moderator));
            Assert.AreEqual("member", OverlayText.RoleLabel(PlayerRole.Member));
            Assert.AreNotEqual(OverlayText.RoleColor(PlayerRole.Owner), OverlayText.RoleColor(PlayerRole.Moderator));
            Assert.AreNotEqual(OverlayText.RoleColor(PlayerRole.Moderator), OverlayText.RoleColor(PlayerRole.Member));
        }

        [Test]
        public void DiagnosticLevels_HaveTheirOwnColours()
        {
            var colours = new HashSet<string>();
            foreach (DiagnosticLevel level in Enum.GetValues(typeof(DiagnosticLevel)))
            {
                Assert.IsTrue(colours.Add(OverlayText.LevelColor(level)), level.ToString());
            }
        }

        [Test]
        public void EveryHubState_HasItsOwnLabel()
        {
            Assert.AreEqual("unknown", OverlayText.HubLabel(null));
            var seen = new HashSet<string>();
            foreach (HubState state in Enum.GetValues(typeof(HubState)))
            {
                var label = OverlayText.HubLabel(new HubInfo { State = state });
                Assert.IsFalse(string.IsNullOrEmpty(label), state.ToString());
                Assert.IsTrue(seen.Add(label), state.ToString());
            }
        }

        [Test]
        public void HubLine_IsHidden_WhileJoined()
        {
            // The probe only runs while the page loads: a Rejoin or a reconnect would leave "ready" behind.
            Assert.AreEqual("", OverlayText.HubLine(null, SessionState.Joined));
            Assert.AreEqual("", OverlayText.HubLine(new HubInfo { State = HubState.Ready, Pid = 4242 }, SessionState.Joined));
            Assert.AreEqual("", OverlayText.HubLine(new HubInfo { State = HubState.Unreachable }, SessionState.Joined));
        }

        [Test]
        public void HubLine_SaysItIsFromThePageLoad_WhileNotJoined()
        {
            var ready = new HubInfo { State = HubState.Ready, Pid = 4242 };
            foreach (SessionState state in Enum.GetValues(typeof(SessionState)))
            {
                if (state == SessionState.Joined)
                {
                    continue;
                }
                Assert.AreEqual("ready" + Dot + "pid 4242 (at page load)", OverlayText.HubLine(ready, state), state.ToString());
            }
            Assert.AreEqual("waiting for the main editor's relay (at page load)",
                OverlayText.HubLine(new HubInfo { State = HubState.Waiting }, SessionState.Idle));
            Assert.AreEqual("unknown (at page load)", OverlayText.HubLine(null, SessionState.Failed));
        }

        [Test]
        public void HubLine_LabelsEveryHubState()
        {
            foreach (HubState state in Enum.GetValues(typeof(HubState)))
            {
                var hub = new HubInfo { State = state };
                var line = OverlayText.HubLine(hub, SessionState.Reconnecting);
                StringAssert.StartsWith(OverlayText.HubLabel(hub), line, state.ToString());
                StringAssert.EndsWith(OverlayText.AtPageLoad, line, state.ToString());
            }
        }

        [Test]
        public void ClearOutcome_BlamesNoOne_WhenTheRelayDidNotClear()
        {
            var failed = OverlayText.ClearOutcome(false);
            // False is also a timeout or a closed socket, and only the world owner is offered the button.
            StringAssert.DoesNotContain("owner", failed);
            StringAssert.DoesNotContain("refused", failed);
            StringAssert.Contains("Console", failed);
            Assert.AreEqual("The room's space state is cleared for everyone.", OverlayText.ClearOutcome(true));
            Assert.AreNotEqual(OverlayText.ClearOutcome(true), failed);
        }

        [Test]
        public void Persistence_TrustsAReadyHub_OverTheSetting()
        {
            var kept = OverlayText.Persistence(null, true);
            var notKept = OverlayText.Persistence(null, false);
            Assert.AreNotEqual(kept, notKept);

            // A ready hub reports what it really does (it can refuse a folder under the web root).
            Assert.AreEqual(notKept, OverlayText.Persistence(new HubInfo { State = HubState.Ready, PersistenceEnabled = false }, true));
            Assert.AreEqual(kept, OverlayText.Persistence(new HubInfo { State = HubState.Ready, PersistenceEnabled = true }, false));

            // Until then, the setting the main editor starts it with.
            Assert.AreEqual(notKept, OverlayText.Persistence(new HubInfo { State = HubState.Waiting, PersistenceEnabled = true }, false));
            Assert.AreEqual(kept, OverlayText.Persistence(new HubInfo { State = HubState.Unknown }, true));
        }

        [TestCase("Player 2", true, "Player 2 (clone)")]
        [TestCase("Main Editor", false, "Main Editor")]
        [TestCase(null, false, Dash)]
        public void SlotLabel_MarksClones(string slot, bool isClone, string expected)
        {
            Assert.AreEqual(expected, OverlayText.SlotLabel(slot, isClone));
        }

        [TestCase("Player 2", "Player 2", "Player 2")]
        [TestCase("Player 2", "Shane", "Player 2 (Shane)")]
        [TestCase("Player 2", null, "Player 2")]
        [TestCase(null, "Shane", "Shane")]
        [TestCase(null, null, Dash)]
        public void PlayerName_AddsADifferentDisplayName(string slot, string displayName, string expected)
        {
            Assert.AreEqual(expected, OverlayText.PlayerName(slot, displayName));
        }

        [Test]
        public void Activity_DescribesSeatAndAttachments()
        {
            Assert.AreEqual("", OverlayText.Activity(null, 0));
            Assert.AreEqual("", OverlayText.Activity("", 0));
            Assert.AreEqual("2 attachments", OverlayText.Activity(null, 2));
            Assert.AreEqual("seated on kQx3Abcd", OverlayText.Activity("kQx3AbcdEfGh", 0));
            Assert.AreEqual("seated on kQx3" + Dot + "1 attachment", OverlayText.Activity("kQx3", 1));
        }

        [Test]
        public void Plain_DisarmsRichTextTags()
        {
            Assert.AreEqual("", OverlayText.Plain(null));
            Assert.AreEqual("no tags here", OverlayText.Plain("no tags here"));
            var text = OverlayText.Plain("Player 2 lacks <b>3</b> objects");
            Assert.AreEqual(-1, text.IndexOf('<'));
            Assert.AreEqual("Player 2 lacks \u2039b>3\u2039/b> objects", text);
        }

        [Test]
        public void Join_SkipsEmptyParts()
        {
            Assert.AreEqual("a" + Dot + "b", OverlayText.Join("a", "b"));
            Assert.AreEqual("b", OverlayText.Join("", "b"));
            Assert.AreEqual("a", OverlayText.Join("a", ""));
            Assert.AreEqual("", OverlayText.Join(null, null));
        }
    }
}
