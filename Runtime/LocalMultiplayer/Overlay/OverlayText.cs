using System;
using System.Globalization;

namespace BS.LocalMultiplayer.Overlay
{
    /// <summary>
    /// The wording the in-game overlay and the Local Multiplayer window share. Every member is a pure function
    /// of the values it is given, so the text is the same in both places and can be tested without a session.
    /// Colours are hex strings for rich text (uGUI's &lt;color&gt; tag and IMGUI's rich labels).
    /// </summary>
    public static class OverlayText
    {
        /// <summary>Shown where a value is missing (no session yet, no id yet).</summary>
        public const string None = "\u2014";

        const string Separator = " \u00B7 ";

        /// <summary>
        /// The first <paramref name="length"/> characters of an id, enough to tell players apart. The relay's
        /// ids carry a type prefix ("rsess_", "peer_") that every one of them shares, so it is skipped.
        /// </summary>
        public static string ShortId(string id, int length = 8)
        {
            if (string.IsNullOrEmpty(id))
            {
                return None;
            }
            if (length < 1)
            {
                length = 1;
            }
            var start = 0;
            if (id.StartsWith("rsess_", StringComparison.Ordinal))
            {
                start = "rsess_".Length;
            }
            else if (id.StartsWith("peer_", StringComparison.Ordinal))
            {
                start = "peer_".Length;
            }
            var rest = id.Length - start;
            if (rest <= 0)
            {
                return id;
            }
            return id.Substring(start, Math.Min(rest, length));
        }

        public static string StateLabel(SessionState state)
        {
            switch (state)
            {
                case SessionState.Idle: return "Not joined";
                case SessionState.Connecting: return "Connecting";
                case SessionState.Joining: return "Joining";
                case SessionState.Joined: return "Joined";
                case SessionState.Reconnecting: return "Reconnecting";
                case SessionState.Failed: return "Failed";
                default: return state.ToString();
            }
        }

        public static string StateColor(SessionState state)
        {
            switch (state)
            {
                case SessionState.Joined: return "#5BD75B";
                case SessionState.Connecting:
                case SessionState.Joining:
                case SessionState.Reconnecting: return "#FFD166";
                case SessionState.Failed: return "#FF6B6B";
                default: return "#9AA0A6";
            }
        }

        public static string RoleLabel(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Owner: return "owner";
                case PlayerRole.Moderator: return "moderator";
                default: return "member";
            }
        }

        public static string RoleColor(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Owner: return "#FFC857";
                case PlayerRole.Moderator: return "#7FD1FF";
                default: return "#9AA0A6";
            }
        }

        public static string LevelColor(DiagnosticLevel level)
        {
            switch (level)
            {
                case DiagnosticLevel.Error: return "#FF6B6B";
                case DiagnosticLevel.Warning: return "#FFD166";
                default: return "#B8BEC6";
            }
        }

        /// <summary>"1 player", "3 players".</summary>
        public static string Players(int count)
        {
            return count == 1 ? "1 player" : count.ToString(CultureInfo.InvariantCulture) + " players";
        }

        /// <summary>"1 attachment", "2 attachments".</summary>
        public static string Attachments(int count)
        {
            return count == 1 ? "1 attachment" : count.ToString(CultureInfo.InvariantCulture) + " attachments";
        }

        /// <summary>Everyone in the room, this player included; 0 while not joined (the peer list is empty then).</summary>
        public static int PlayerCount(SessionState state, int peerCount)
        {
            return state == SessionState.Joined ? Math.Max(0, peerCount) + 1 : 0;
        }

        /// <summary>
        /// The one line shown while the panel is hidden: "LocalMP \u00B7 Joined \u00B7 3 players \u00B7 F8". The player count
        /// only appears while joined; the key only when there is one.
        /// </summary>
        public static string Pill(SessionState state, int peerCount, string keyName)
        {
            var text = "LocalMP" + Separator + StateLabel(state);
            if (state == SessionState.Joined)
            {
                text += Separator + Players(PlayerCount(state, peerCount));
            }
            if (!string.IsNullOrEmpty(keyName))
            {
                text += Separator + keyName;
            }
            return text;
        }

        /// <summary>The relay round trip: a dash until one is measured, "&lt;1 ms" on a quiet machine.</summary>
        public static string Rtt(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0)
            {
                return None;
            }
            if (ms < 1)
            {
                return "<1 ms";
            }
            return Math.Round(ms).ToString("0", CultureInfo.InvariantCulture) + " ms";
        }

        /// <summary>A peer's measured pose rate. Negative means not measured yet.</summary>
        public static string PoseRate(float hz)
        {
            if (float.IsNaN(hz) || hz < 0)
            {
                return None;
            }
            return Math.Round(hz).ToString("0", CultureInfo.InvariantCulture) + " Hz";
        }

        /// <summary>What the hub probe found at the page's server port.</summary>
        public static string HubLabel(HubInfo hub)
        {
            if (hub == null)
            {
                return "unknown";
            }
            switch (hub.State)
            {
                case HubState.Unknown: return "not checked";
                case HubState.Waiting: return "waiting for the main editor's relay";
                case HubState.Ready: return "ready";
                case HubState.NoRelay: return "no relay at the page server";
                case HubState.ProjectMismatch: return "another project's relay holds the port";
                case HubState.Unreachable: return "unreachable";
                default: return hub.State.ToString();
            }
        }

        /// <summary>Ends the hub line: the probe runs while the page loads and never again.</summary>
        public const string AtPageLoad = "(at page load)";

        /// <summary>
        /// The hub line, "ready \u00B7 pid 1234 (at page load)", or empty while joined. The hub probe only runs while
        /// the page loads (a Rejoin or a reconnect doesn't run it again), so once this player is in the room the
        /// answer is old news, and after a drop it still describes the page load, not the relay now.
        /// </summary>
        public static string HubLine(HubInfo hub, SessionState state)
        {
            if (state == SessionState.Joined)
            {
                return string.Empty;
            }
            var text = HubLabel(hub);
            if (hub != null && hub.Pid > 0)
            {
                text += Separator + "pid " + hub.Pid.ToString(CultureInfo.InvariantCulture);
            }
            return text + " " + AtPageLoad;
        }

        /// <summary>
        /// Whether the room's space state outlives this play session. The hub's answer when it has one (it can
        /// refuse the folder); otherwise the setting the main editor starts it with.
        /// </summary>
        public static string Persistence(HubInfo hub, bool persistSetting)
        {
            var kept = hub != null && hub.State == HubState.Ready ? hub.PersistenceEnabled : persistSetting;
            return kept ? "space state kept between plays (24 h)" : "space state not kept between plays";
        }

        /// <summary>
        /// What a "Clear room state" request came to. The session answers false for a refusal and for a request
        /// that timed out or lost its socket alike, and logs which it was to the Console, so the text names no
        /// cause (only the world owner is offered the button, and the relay lets the owner clear).
        /// </summary>
        public static string ClearOutcome(bool cleared)
        {
            return cleared
                ? "The room's space state is cleared for everyone."
                : "The relay didn't clear the room: see the Console for why.";
        }

        /// <summary>"Player 2 (clone)" in a Multiplayer Play Mode clone, the slot alone otherwise.</summary>
        public static string SlotLabel(string slot, bool isClone)
        {
            var label = string.IsNullOrEmpty(slot) ? None : slot;
            return isClone ? label + " (clone)" : label;
        }

        /// <summary>The slot, then the display name when it says something the slot doesn't.</summary>
        public static string PlayerName(string slot, string displayName)
        {
            if (string.IsNullOrEmpty(slot))
            {
                return string.IsNullOrEmpty(displayName) ? None : displayName;
            }
            if (string.IsNullOrEmpty(displayName) || string.Equals(slot, displayName, StringComparison.Ordinal))
            {
                return slot;
            }
            return slot + " (" + displayName + ")";
        }

        /// <summary>
        /// "seated on kQx3abcd \u00B7 2 attachments" from a player's engine user state (the pilot seat and the
        /// attachment_&lt;Id&gt; keys); empty when they have neither.
        /// </summary>
        public static string Activity(string seatId, int attachments)
        {
            var seated = !string.IsNullOrEmpty(seatId);
            if (!seated && attachments <= 0)
            {
                return string.Empty;
            }
            if (!seated)
            {
                return Attachments(attachments);
            }
            var text = "seated on " + ShortId(seatId);
            return attachments > 0 ? text + Separator + Attachments(attachments) : text;
        }

        /// <summary>
        /// Text from the relay or another module, made safe for rich text: a '&lt;' could open a tag, so it
        /// becomes a lookalike quote.
        /// </summary>
        public static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.IndexOf('<') < 0 ? text : text.Replace('<', '\u2039');
        }

        /// <summary>Joins non-empty parts with the overlay's middle-dot separator.</summary>
        public static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
            {
                return b ?? string.Empty;
            }
            return string.IsNullOrEmpty(b) ? a : a + Separator + b;
        }
    }
}
