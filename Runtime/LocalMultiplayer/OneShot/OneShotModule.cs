// <mirror source="Assets/Systems/SceneEvents/BanterSceneEventHandler.cs" sha256="ee046e8061e439c34909a9015a187b387824455d5cdd6a88e9e1f07941660813" mode="port" />
// The SDK wiring of the one-shot lane: BanterSceneEventHandler.cs:345-365 (page -> room) and :806-847 (room -> page),
// with OneShotService.Instance replaced by this module's OneShotLane, BSScene.Instance() by BSScene.Current, and
// WorldFilesBridge.IsWorldOwner by the settings' world owner (LocalIdentity.WorldOwnerClientId).
using System;
using UnityEngine;
using UnityEngine.Events;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// One-shots for the local room, as the Greenfield client runs them: a page's (or a graph's) <c>OneShot(data)</c>
    /// reaches every other player in the room under the topic <c>"banter"</c>, never the sender, and nothing is
    /// stored for late joiners. A receiver hands it to the page and Visual Scripting with <c>fromId</c> = the
    /// sender's room session id (its <c>UserData.id</c>) and <c>fromAdmin</c> = "the sender is the world owner".
    /// </summary>
    [LocalModule(ModuleOrder.OneShot)]
    [DisallowMultipleComponent]
    public sealed class OneShotModule : MonoBehaviour, ILocalModule
    {
        // The topic every SDK one-shot travels under on Greenfield's room-wide lane.
        internal const string SdkOneShotTopic = "banter";

        ILocalHost _host;
        OneShotLane _lane;
        BSSceneEvents _events;
        BSScene _scene;
        bool _installed;

        // Cached so the listeners are always removed by the same delegate instances.
        UnityAction<string, bool> _onPageOneShot;
        Action<OneShotMessage> _onRoomOneShot;

        /// <summary>The lane, or null when not installed.</summary>
        public OneShotLane Lane => _lane;

        public void Install(ILocalHost host)
        {
            if (host == null || host.Session == null)
            {
                throw new InvalidOperationException("[LocalMP][OneShot] the host has no session to install on.");
            }
            // Set first so a failure part-way through is still undone by Uninstall.
            _installed = true;
            _host = host;
            _onPageOneShot ??= OnPageOneShot;
            _onRoomOneShot ??= OnRoomOneShot;
            _lane = new OneShotLane(host.Session);
            _lane.Attach();
            _lane.Received += _onRoomOneShot;
            // Scene objects have not started yet: listen now so a one-shot sent from a Start meets the lane (it is
            // still refused while the room is not joined, as in production).
            HookScene(host.Scene ?? BSScene.Current);
            host.Provide(_lane);
        }

        public void Uninstall()
        {
            if (!_installed)
            {
                return;
            }
            _installed = false;
            UnhookScene();
            if (_lane != null)
            {
                _lane.Received -= _onRoomOneShot;
                _lane.Detach();
            }
            _lane = null;
            _host = null;
        }

        void Update()
        {
            if (!_installed)
            {
                return;
            }
            EnsureSceneHooked();
        }

        void OnDestroy()
        {
            // The host uninstalls first; this only matters when the host object dies without it (script reload).
            Uninstall();
        }

        /// <summary>
        /// Re-subscribe whenever the SDK swaps its scene or events object, as the Greenfield bridges do
        /// (SpaceStateSdkBridge.cs:64-89): BSScene.Destroy() removes every listener and drops the singleton.
        /// BSScene.Current never constructs a scene, unlike Instance(), so teardown cannot resurrect one.
        /// </summary>
        void EnsureSceneHooked()
        {
            var scene = BSScene.Current;
            if (scene == null || scene.events == null) return;
            if (ReferenceEquals(scene, _scene) && ReferenceEquals(scene.events, _events)) return;
            HookScene(scene);
        }

        void HookScene(BSScene scene)
        {
            if (scene == null || scene.events == null) return;
            if (ReferenceEquals(scene, _scene) && ReferenceEquals(scene.events, _events)) return;
            UnhookScene();
            _scene = scene;
            _events = scene.events;
            _events.OnOneShot.AddListener(_onPageOneShot);
        }

        void UnhookScene()
        {
            if (_events != null)
            {
                _events.OnOneShot.RemoveListener(_onPageOneShot);
            }
            _events = null;
            _scene = null;
        }

        // ------------------------------------------------------------------ page -> room

        // The page's one-shot lane. The topic is fixed: Greenfield's lane is topic-addressed (emoji rides it under
        // 'emoji') while Banter's one-shot is a single untyped broadcast, so every SDK message travels under one topic.
        //
        // allInstances is accepted and ignored: the lane reaches every peer in the room and has no notion of an
        // instance to narrow to.
        void OnPageOneShot(string data, bool allInstances)
        {
            var lane = _lane;
            if (lane == null) return;
            if (!lane.Send(SdkOneShotTopic, data))
            {
                Debug.LogWarning("[LocalMP][SceneEvents] a one-shot was refused by the lane - over 4 KB, "
                               + "or the room is not connected.");
            }
        }

        // ------------------------------------------------------------------ room -> page

        // Relay the room's one-shots into the page.
        //
        // fromAdmin means the world's OWNER, never a moderator (WorldFilesBridge.IsWorldOwner). It is what lets a
        // receiver trust a message: without a real answer here any visitor could, say, rebuild the space for
        // everyone else.
        void OnRoomOneShot(OneShotMessage message)
        {
            if (!string.Equals(message.Topic, SdkOneShotTopic, StringComparison.Ordinal)) return;
            var scene = BSScene.Current;
            if (scene == null || scene.link == null) return;

            var identity = _host != null ? _host.Identity : null;
            var fromAdmin = IsWorldOwner(message.UserId, identity != null ? identity.WorldOwnerClientId : null);

            scene.link.OnOneShot(message.Data, message.RoomSessionId, fromAdmin);
        }

        /// <summary>
        /// Production's <c>WorldFilesBridge.IsWorldOwner(message.UserId)</c>: the sender's stable user id is the world
        /// owner's. Locally that is the relay-stamped <c>fromUserId</c> ("local:" + slot) against the settings' world owner.
        /// </summary>
        internal static bool IsWorldOwner(string fromUserId, string worldOwnerClientId)
        {
            if (string.IsNullOrEmpty(fromUserId)) return false;
            return string.Equals(fromUserId, worldOwnerClientId, StringComparison.Ordinal);
        }
    }
}
