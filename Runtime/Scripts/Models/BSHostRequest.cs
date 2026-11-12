using System;
using System.Threading;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// A generic request/response op from a page to whatever the host app provides, for bridges
    /// that are not space/user state (progression, and every <see cref="BSHostExtensions"/>
    /// extension). The SDK knows nothing about the backend: it hands this to the app and waits for
    /// <see cref="Respond"/>. Same contract as <see cref="BSStateRequest"/>: one event carrying
    /// an op object, so adding an operation is a data change rather than an API change.
    /// </summary>
    public class BSHostRequest
    {
        /// <summary>
        /// What this request is addressed to. For progression it is the API command token it
        /// arrived on (APICommands.PROGRESSION_OP); for a host extension it is the extension's
        /// registered name (e.g. "recording").
        /// </summary>
        public string Command;

        /// <summary>The sub-command, e.g. "fire" | "me" | "start".</summary>
        public string Op;

        /// <summary>The decoded JSON body.</summary>
        public string Json;

        /// <summary>Visual Scripting correlation id; null for page-originated ops.</summary>
        public string RequestId;

        /// <summary>
        /// A GameObject handed over directly by an in-process caller (a Visual Scripting unit),
        /// which cannot travel in <see cref="Json"/>. Always null for page-originated ops: a page
        /// names objects by unityId (BSScene.UnityId) in its JSON instead, resolved with
        /// <see cref="BSHostExtensions.ResolveObject"/>.
        /// </summary>
        public GameObject Target;

        /// <summary>
        /// The handler MUST set this synchronously before going async. Dispatch is synchronous, so
        /// an unset flag after it returns means nothing took the request, which is how a page
        /// promise still settles in a build with no host bridge.
        /// </summary>
        public bool Handled;

        private Action<string> _respond;
        private int _responded;

        public BSHostRequest(string command, Action<string> respond)
        {
            Command = command;
            _respond = respond;
        }

        /// <summary>True once <see cref="Respond"/> has been called.</summary>
        public bool Responded => Volatile.Read(ref _responded) != 0;

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
