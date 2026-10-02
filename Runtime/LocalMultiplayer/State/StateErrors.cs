// <mirror source="Assets/Systems/Networking/State/StateErrors.cs" sha256="125a6c0b3740d37034f8552fbb7de6d73c84fc7691b04003adca8b473c582ac0" mode="verbatim" />
using System;
using System.Collections.Concurrent;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// The server's error strings, plus the client-side codes we raise before spending a
    /// rate-limit token, and an author-facing description for each.
    ///
    /// Content authors see these through three channels (a Unity log, the page's <c>ss_err</c>
    /// sentinel / <c>space-state-error</c> event, and the Visual Scripting error unit), so the
    /// wording has to make sense to someone who has never read the server.
    /// </summary>
    public static class StateErrors
    {
        // --- op engine (packetparty-server/src/state/ops.ts) ---
        public const string InvalidPath = "invalid_path";
        public const string InvalidValue = "invalid_value";
        public const string TypeMismatch = "type_mismatch";
        public const string CasConflict = "cas_conflict";
        public const string ValueTooLarge = "value_too_large";
        public const string TooManyKeys = "too_many_keys";
        public const string NotFound = "not_found";

        // --- service (RoomStateService.ts / UserStateService.ts) ---
        public const string UnknownMessageType = "unknown_message_type";
        public const string InvalidScope = "invalid_scope";
        public const string ProtectedPath = "protected_path";
        public const string AppUnavailable = "app_unavailable";
        public const string InvalidBatch = "invalid_batch";
        public const string BatchFailed = "batch_failed";

        // --- authorization (ActionGuard / the SideQuest webhook) ---
        public const string NotAuthorized = "not_authorized";
        public const string NotSpaceAdmin = "not_space_admin";
        public const string InvalidToken = "invalid_token";
        public const string PolicyDenied = "policy_denied";
        public const string JwtMissing = "jwt_missing";

        // --- dispatcher ---
        public const string RateLimited = "rate_limited";
        public const string SessionNotFound = "session_not_found";
        public const string InternalError = "internal_error";

        // --- client-side only ---
        public const string NotConnected = "not_connected";
        public const string Dropped = "dropped_offline";
        public const string Timeout = "timeout";

        /// <summary>Codes that indicate a bug in this client rather than anything the author did.</summary>
        public static bool IsOurBug(string code) =>
            code == UnknownMessageType || code == InvalidScope || code == InvalidBatch;

        /// <summary>Codes worth retrying rather than surfacing.</summary>
        public static bool IsTransient(string code) =>
            code == RateLimited || code == SessionNotFound || code == InternalError || code == NotConnected;

        public static string Describe(string code)
        {
            switch (code)
            {
                case InvalidPath:
                    return "the property name can't be used (letters, digits, _ - @ and . to nest; at most 64 characters per part)";
                case InvalidValue:
                    return "the value isn't valid JSON";
                case TypeMismatch:
                    return "the stored value isn't the right type for that operation";
                case CasConflict:
                    return "the value changed underneath you";
                case ValueTooLarge:
                    return "the value is over the 16 KB limit";
                case TooManyKeys:
                    return "this space has reached its 2048 property limit";
                case NotFound:
                    return "there was nothing stored at that path";
                case ProtectedPath:
                    return "that property is protected; only the space owner or a moderator can change it";
                case NotAuthorized:
                case NotSpaceAdmin:
                case JwtMissing:
                    return "protected space state can only be changed by the space owner or a moderator";
                case InvalidToken:
                    return "your session was rejected; sign in again";
                case PolicyDenied:
                case AppUnavailable:
                    return "protected space state is not enabled for this space";
                case RateLimited:
                    return "too many state changes at once; they are being retried";
                case SessionNotFound:
                case InternalError:
                    return "the space server had a problem; the change will be retried";
                case NotConnected:
                    return "not connected to the space server";
                case Dropped:
                    return "dropped after 60 seconds offline";
                case Timeout:
                    return "the space server did not answer in time";
                case BatchFailed:
                    return "the change was rejected";
                case UnknownMessageType:
                case InvalidScope:
                case InvalidBatch:
                    return "the client sent a malformed request (this is a bug)";
                default:
                    return code ?? "unknown error";
            }
        }
    }

    /// <summary>
    /// De-duplicates log spam. Concurrent because writes are enqueued from the SDK's dispatch path
    /// while the pump reports from a main-thread post — the previous bridge used a plain HashSet
    /// touched from both.
    /// </summary>
    public sealed class WarnOnce
    {
        private readonly ConcurrentDictionary<string, byte> _seen = new ConcurrentDictionary<string, byte>();

        public void Once(string key, Action action)
        {
            if (key == null) { action(); return; }
            if (_seen.TryAdd(key, 0)) action();
        }

        public void Reset() => _seen.Clear();
    }
}
