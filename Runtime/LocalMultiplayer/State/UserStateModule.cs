using System;
using System.Threading;
using UnityEngine;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// User state for the local room, as the Greenfield client runs it: a page's <c>SetUserProps</c> (raised as
    /// <c>OnSetUserProps</c> while the host is active) and the JSON user-state API write this player's props under
    /// <c>props.prot.*</c> / <c>props.pub.*</c>, and every player's props reach each page keyed by uid
    /// (<c>upc!</c>, <c>upj!</c>) and Visual Scripting. Engine keys (<c>attachment_*</c>, <c>pilot</c>) are the
    /// session's and never reach the page. Mounts the ports of PacketPartyClient's user-state layer
    /// (<see cref="UserStateClient"/>), <see cref="UserStateService"/> and <see cref="UserStateSdkBridge"/>.
    /// </summary>
    [LocalModule(ModuleOrder.UserState)]
    [DisallowMultipleComponent]
    public sealed class UserStateModule : MonoBehaviour, ILocalModule
    {
        MainThreadQueue _mainThread;
        CancellationTokenSource _lifetime;
        UserStateClient _client;
        UserStateService _service;
        UserStateSdkBridge _bridge;
        bool _installed;

        /// <summary>The user-state service, or null when not installed.</summary>
        public UserStateService Service => _service;

        public void Install(ILocalHost host)
        {
            if (host == null || host.Session == null)
            {
                throw new InvalidOperationException("[LocalMP][UserState] the host has no session to install on.");
            }
            // Set first so a failure part-way through is still undone by Uninstall.
            _installed = true;
            _lifetime = new CancellationTokenSource();
            _mainThread = new MainThreadQueue();
            _client = new UserStateClient(host.Session);
            _client.Attach();
            _service = new UserStateService(_client, _mainThread.Post, _lifetime.Token);
            _bridge = new UserStateSdkBridge(_service);
            // Scene objects have not started yet: SetUserProps from a Start must find the listener.
            _bridge.HookScene(host.Scene ?? BSScene.Current);
            host.Provide(_service);
        }

        public void Uninstall()
        {
            if (!_installed)
            {
                return;
            }
            _installed = false;
            // Cancels requests in flight; their continuations then find the queue closed. The source is not
            // disposed: a late continuation may still read its token.
            if (_lifetime != null)
            {
                try { _lifetime.Cancel(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            _mainThread?.Close();
            _bridge?.Dispose();
            _service?.Dispose();
            _client?.Detach();
            _bridge = null;
            _service = null;
            _client = null;
            _mainThread = null;
            _lifetime = null;
        }

        void Update()
        {
            if (!_installed || _bridge == null)
            {
                return;
            }
            _mainThread.Drain();
            _service.Tick();
            _bridge.Tick();
        }

        void OnDestroy()
        {
            // The host uninstalls first; this only matters when the host object dies without it (script reload).
            Uninstall();
        }
    }
}
