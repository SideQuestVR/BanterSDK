using System;
using System.Threading;

namespace BS
{
    /// <summary>
    /// A generic request/response op from a page to whatever the host app provides, for bridges
    /// that are not space/user state (progression today). The SDK knows nothing about the
    /// backend: it raises this on the matching <c>BSSceneEvents</c> UnityEvent and waits for
    /// <see cref="Respond"/>. Same contract as <see cref="BSStateRequest"/>: one event carrying
    /// an op object, so adding an operation is a data change rather than an API change.
    /// </summary>
    public class BSHostRequest
    {
        /// <summary>The API command token this arrived on (e.g. APICommands.PROGRESSION_OP).</summary>
        public string Command;

        /// <summary>The sub-command, e.g. "fire" | "me".</summary>
        public string Op;

        /// <summary>The decoded JSON body.</summary>
        public string Json;

        /// <summary>Visual Scripting correlation id; null for page-originated ops.</summary>
        public string RequestId;

        /// <summary>
        /// The handler MUST set this synchronously before going async. <c>UnityEvent.Invoke</c> is
        /// synchronous, so an unset flag after it returns means nothing is listening, which is how
        /// a page promise still settles in a build with no host bridge.
        /// </summary>
        public bool Handled;

        private Action<string> _respond;
        private int _responded;

        public BSHostRequest(string command, Action<string> respond)
        {
            Command = command;
            _respond = respond;
        }

        /// <summary>
        /// Reply with a compact JSON envelope: <c>{"ok":true,...}</c> or
        /// <c>{"ok":false,"error":"code"}</c>. Safe from any thread and idempotent.
        /// </summary>
        public void Respond(string json)
        {
            if (Interlocked.Exchange(ref _responded, 1) != 0) return;
            _respond?.Invoke(json);
        }
    }
}
