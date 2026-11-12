using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Attachments
{
    /// <summary>
    /// Attachments and seats, as the Greenfield client runs them: <see cref="LocalAttachmentsSystem"/> (its
    /// AttachmentsSystem) takes over the scene's AttachObject/DetachObject from the SDK's SdkAttachments, and
    /// <see cref="LocalAttachmentBridge"/> (its AttachmentNetworkBridge) broadcasts this player's autoSync attachments
    /// and seat as user state and reproduces everyone else's on their orb avatars.
    /// </summary>
    /// <remarks>
    /// Needs the avatar module's <see cref="ILocalRig"/> (the desktop player's hands and torso) and
    /// <see cref="IRemoteBoneSource"/> (the remote orbs' bones and seat glue). When this player's session ends (Leave,
    /// a Rejoin or page load, which close it, or a resume the relay refused), objects reproduced on remote avatars go
    /// back where they were first (<see cref="LocalAttachmentsSystem.RestoreRemoteReproductions"/>): a deliberate
    /// deviation, since production only leaves by unloading the whole space.
    /// </remarks>
    [LocalModule(ModuleOrder.Attachments)]
    [DisallowMultipleComponent]
    public sealed class AttachmentModule : MonoBehaviour, ILocalModule
    {
        ILocalHost _host;
        ILocalSession _session;
        AttachmentDiagnostics _diag;
        LocalAttachmentsSystem _system;
        LocalAttachmentBridge _bridge;
        bool _installed;

        // Cached so every unsubscribe removes the delegate that was added.
        Action _onRoomLeft;
        Action _onQuitting;

        /// <summary>The seat this player broadcasts as riding, or null.</summary>
        public string LocalPilotSeatId => _bridge != null ? _bridge.LocalPilotSeatId : null;
        /// <summary>Objects attached to this player.</summary>
        public int LocalAttachmentCount => _system != null ? _system.LocalAttachmentCount : 0;
        /// <summary>Objects shown on remote avatars.</summary>
        public int RemoteReproductionCount => _system != null ? _system.RemoteReproductionCount : 0;
        /// <summary>Peers' attachments waiting for their object or avatar.</summary>
        public int PendingAttachmentCount => _bridge != null ? _bridge.PendingAttachmentCount : 0;
        /// <summary>Peers' seats waiting for the seat object or avatar.</summary>
        public int PendingPilotCount => _bridge != null ? _bridge.PendingPilotCount : 0;

        public void Install(ILocalHost host)
        {
            if (_installed)
            {
                return;
            }
            _host = host;
            _session = host.Session;
            _diag = new AttachmentDiagnostics(host.Diagnostics);
            _system = new LocalAttachmentsSystem(host, _diag);
            _bridge = new LocalAttachmentBridge(host, _system, _diag);
            _onRoomLeft ??= OnRoomLeft;
            _onQuitting ??= OnQuitting;

            var scene = host.Scene ?? BSScene.Current;
            if (scene == null)
            {
                Debug.LogWarning("[LocalMP][Attachments] No scene to take the attach delegates from; attachments stay the SDK's.");
            }
            // Scene objects have not started yet: take the delegates now, so the first autoAttach and seat come here.
            _system.Install(scene);
            _bridge.Start(scene, _session);

            if (_session != null)
            {
                _session.RoomLeft += _onRoomLeft;
            }
            // At play exit the scene's objects are destroyed after this: their Attachment components must not
            // detach, unseat or reach a scene that is going away.
            Application.quitting += _onQuitting;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
            _installed = true;
            host.Provide(this);
        }

        public void Uninstall()
        {
            if (!_installed)
            {
                return;
            }
            _installed = false;
            Application.quitting -= _onQuitting;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            if (_session != null)
            {
                _session.RoomLeft -= _onRoomLeft;
            }
            try
            {
                _bridge.Stop();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            try
            {
                // Puts reproductions back, removes what was added and hands the delegates back to SdkAttachments.
                _system.TearDown();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            _diag.ClearAll();
            _session = null;
            _host = null;
        }

        void Update()
        {
            if (!_installed)
            {
                return;
            }
            _bridge.Update();
        }

        void OnDestroy()
        {
            // The host uninstalls first; this only matters when the host object dies without it.
            Uninstall();
        }

        // This player's session is over: Leave, the close a page load or Rejoin causes (the lifecycle disconnects on
        // OnLoad), or a resume the relay no longer accepted. Presence (installed earlier, so called earlier) has just
        // queued the remote avatars' Destroy; it runs at the end of the frame, so the reproductions can still be
        // lifted off them. A dropped socket that resumes keeps its peers and raises nothing, as in production.
        void OnRoomLeft()
        {
            if (!_installed)
            {
                return;
            }
            try
            {
                var restored = _system.RestoreRemoteReproductions();
                if (restored > 0)
                {
                    Debug.Log($"[LocalMP][Attachments] Left the room: put {restored} object(s) worn by other players back "
                        + "where the scene had them. The room's snapshot puts them back on when you join again.");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void OnQuitting()
        {
            _system?.MarkSceneDying();
        }

#if UNITY_EDITOR
        void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                _system?.MarkSceneDying();
            }
        }
#endif
    }
}
