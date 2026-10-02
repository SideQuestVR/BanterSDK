using System;
using System.Collections;
using SideQuest.Ora;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// The seam a host that networks this play session hooks into: the in-editor local multiplayer
    /// (assembly BS.SDK.LocalMultiplayer). While <see cref="Active"/>, the SDK's offline stand-ins step
    /// aside because the host provides the real thing: a page's user props go to
    /// <see cref="BSSceneEvents.OnSetUserProps"/>, and the Visual Scripting space-prop loopback and the
    /// once-per-page user replay are skipped. Nothing sets it in builds or in the Greenfield client.
    /// </summary>
    public static class BSNetworkHost
    {
        /// <summary>A host is installed and networking this play session.</summary>
        public static bool Active { get; set; }

        /// <summary>
        /// Raised at the end of <see cref="BSStarterUpper"/>'s Awake: the browser link, the desktop player
        /// and the SDK's attachment emulation exist, and no scene object has started yet.
        /// </summary>
        public static event Action<BSScene> SceneLinked;

        /// <summary>
        /// When set, the SDK yields this (with the view and the web server port) before it loads the
        /// space page, instead of its fixed two second wait. The host uses it to wait for its hub.
        /// </summary>
        public static Func<OraView, int, IEnumerator> PageLoadGate;

        internal static void RaiseSceneLinked(BSScene scene)
        {
            var handlers = SceneLinked;
            if (handlers == null)
            {
                return;
            }
            foreach (Action<BSScene> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(scene);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        // Play mode without a domain reload keeps statics; start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Active = false;
            SceneLinked = null;
            PageLoadGate = null;
        }
    }
}
