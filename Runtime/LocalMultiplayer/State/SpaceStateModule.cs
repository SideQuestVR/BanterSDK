using System;
using System.Threading;
using UnityEngine;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// Space state for the local room, as the Greenfield client runs it: a page's (or a graph's) public and
    /// protected space props and the JSON space-state API go to the relay's room state, and every change, the
    /// writer's own echo included, comes back to the page (<c>spc!</c>, <c>spj!</c>, <c>fss!</c>, <c>fsj!</c>) and
    /// to Visual Scripting. Mounts the ports of PacketPartyClient's room-state layer (<see cref="RoomStateClient"/>),
    /// <see cref="SpaceStateService"/> and <see cref="SpaceStateSdkBridge"/>, ticked in that order each frame after
    /// the posted request continuations, as production's components run after PacketParty's main-thread queue.
    /// </summary>
    [LocalModule(ModuleOrder.SpaceState)]
    [DisallowMultipleComponent]
    public sealed class SpaceStateModule : MonoBehaviour, ILocalModule
    {
        MainThreadQueue _mainThread;
        CancellationTokenSource _lifetime;
        RoomStateClient _client;
        SpaceStateService _service;
        SpaceStateSdkBridge _bridge;
        bool _installed;

        /// <summary>The space-state service, or null when not installed.</summary>
        public SpaceStateService Service => _service;

        public void Install(ILocalHost host)
        {
            if (host == null || host.Session == null)
            {
                throw new InvalidOperationException("[LocalMP][SpaceState] the host has no session to install on.");
            }
            // Set first so a failure part-way through is still undone by Uninstall.
            _installed = true;
            _lifetime = new CancellationTokenSource();
            _mainThread = new MainThreadQueue();
            _client = new RoomStateClient(host.Session, _mainThread.Post, _lifetime.Token);
            _client.Attach();
            _service = new SpaceStateService(_client, _mainThread.Post, _lifetime.Token);
            _bridge = new SpaceStateSdkBridge(_service);
            // Scene objects have not started yet: writes made from a Start must find the listener.
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
