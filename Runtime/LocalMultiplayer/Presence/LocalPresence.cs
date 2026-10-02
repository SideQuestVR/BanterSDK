// <mirror source="Assets/Systems/Networking/NetworkService.cs" sha256="cab93afa037c3e7a7ef1181ed552a18541ae46c49b93872cbae9b18a5f804c80" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/PacketPartyPeer.cs" sha256="3eeeb45fe59f358a92ffaa4494b46710cd26620ebf12e790b39ba64e025eebee" mode="port" />
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// The remote players, as NetworkService builds them (CreateRemotePlayer, RegisterSdkUser and
    /// HandlePeerLeft, NetworkService.cs:1182-1296): one root per peer under the host's RemotesRoot, with
    /// the avatar module's orb under it, and a UserData the scene learns about in the same call, so the page
    /// and Visual Scripting see a peer join the moment it does. A peer that comes back under a new session
    /// replaces its old avatar (keyed by its stable user id) instead of stacking a second one, and because
    /// the new UserData holds the same uid when the old one is destroyed, the page sees no leave. A leaving
    /// peer's root is destroyed, and UserData.OnDestroy removes the user; nothing here calls RemoveUser,
    /// which would fire the leave twice.
    /// </summary>
    [LocalModule(ModuleOrder.Presence)]
    [AddComponentMenu("")]
    public sealed class LocalPresence : MonoBehaviour, ILocalModule
    {
        ILocalHost _host;
        ILocalSession _session;
        // Remote avatars keyed by STABLE per-user identity (not peerId), so a peer that reconnects under a new
        // session replaces its old avatar instead of stacking a duplicate.
        readonly Dictionary<string, LocalPeer> _avatarsByIdentity = new Dictionary<string, LocalPeer>(StringComparer.Ordinal);
        // Remote players keyed by roomSessionId, for lookups by the same identity as player state.
        readonly Dictionary<string, LocalPeer> _remotePlayersByRoomSession = new Dictionary<string, LocalPeer>(StringComparer.Ordinal);
        readonly List<LocalPeer> _scratch = new List<LocalPeer>();
        [NonSerialized] bool _live;

        public void Install(ILocalHost host)
        {
            _host = host;
            _session = host.Session;
            _session.PeerJoined += CreateRemotePlayer;
            _session.PeerLeft += HandlePeerLeft;
            _session.RoomLeft += DisposeRemotePlayers;
            _live = true;
        }

        public void Uninstall()
        {
            _live = false;
            if (_session != null)
            {
                _session.PeerJoined -= CreateRemotePlayer;
                _session.PeerLeft -= HandlePeerLeft;
                _session.RoomLeft -= DisposeRemotePlayers;
            }
            // Play is ending (or scripts are reloading) while the browser link still exists: destroy the remote
            // users now, so their leaves reach the page and nothing outlives the session.
            DestroyAll(immediate: true);
        }

        void CreateRemotePlayer(LocalPeer peer)
        {
            if (!_live || peer == null || peer.Root != null) return;
            // If this user already has an avatar under the same stable identity (they came back under a new
            // session after a blip), dispose the stale one so we don't stack a duplicate.
            string identity = StableIdentity(peer);
            if (_avatarsByIdentity.TryGetValue(identity, out var existing) && existing != null && existing != peer)
            {
                Debug.Log($"[LocalMP] Replacing stale avatar for '{identity}' (peer returned as {peer.PeerId}).");
                ForgetRemotePlayer(existing.RoomSessionId, existing);
                DestroyRoot(existing, immediate: false);
            }

            var root = new GameObject(PlayerObjectName(false, Label(peer), "", peer.RoomSessionId));
            root.transform.SetParent(_host.RemotesRoot, false);
            peer.Root = root;
            peer.Head = BuildAvatar(peer, root.transform);
            _avatarsByIdentity[identity] = peer;
            if (!string.IsNullOrEmpty(peer.RoomSessionId))
            {
                _remotePlayersByRoomSession[peer.RoomSessionId] = peer;
            }
            RegisterSdkUser(root, peer);
        }

        // The avatar module (IRemoteAvatarFactory) builds the orb under the root and says where its head is;
        // without one the root stands in for the head, as it does in production until the model loads.
        Transform BuildAvatar(LocalPeer peer, Transform root)
        {
            var factory = _host.Get<IRemoteAvatarFactory>();
            if (factory is UnityEngine.Object unityFactory && unityFactory == null) factory = null;
            Transform head = null;
            if (factory != null)
            {
                try
                {
                    head = factory.BuildAvatar(peer, root);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            return head != null ? head : root;
        }

        // Represent the remote peer to the scene as a UserData, so scene scripts, the browser link and visual
        // scripting see them join. UserData.OnDestroy calls scene.RemoveUser when the avatar is destroyed
        // (peer leave, reconnect-replace, room leave), so we only AddUser here — never RemoveUser (it isn't
        // idempotent and would double-fire OnUserLeft).
        void RegisterSdkUser(GameObject avatar, LocalPeer peer)
        {
            var scene = _host.Scene != null ? _host.Scene : BSScene.Current;
            if (scene == null) return;

            var userData = avatar.AddComponent<UserData>();
            userData.isLocal = false;
            // uid = the broadcast stable USER identity (the clientId they joined with), hashed for parity with
            // Banter. id = the per-session identity (roomSessionId), which all session-scoped routing and
            // ownership keys off.
            string rawUid = !string.IsNullOrEmpty(peer.ClientId) ? peer.ClientId
                          : !string.IsNullOrEmpty(peer.RoomSessionId) ? peer.RoomSessionId
                          : peer.PeerId;
            userData.uid = LocalIdentityFactory.HashUid(rawUid);
            userData.id = !string.IsNullOrEmpty(peer.RoomSessionId) ? peer.RoomSessionId : peer.PeerId;
            userData.name = Label(peer);
            userData.color = LocalIdentityFactory.RemoteColor;
            userData.isSpaceAdmin = false;
            userData.Head = peer.Head;
            peer.User = userData;
            scene.AddUser(userData);
            avatar.name = PlayerObjectName(false, userData.name, userData.uid, userData.id);
        }

        void HandlePeerLeft(LocalPeer peer)
        {
            if (!_live || peer == null) return;
            Debug.Log($"[LocalMP] Peer left: {Label(peer)} ({peer.PeerId})");
            string identity = StableIdentity(peer);
            // Forget the mapping only if it still points at THIS peer's avatar — never drop a newer replacement
            // that a reconnect already installed under the same identity.
            if (_avatarsByIdentity.TryGetValue(identity, out var avatar) &&
                (avatar == null || avatar.PeerId == peer.PeerId))
            {
                _avatarsByIdentity.Remove(identity);
            }
            ForgetRemotePlayer(peer.RoomSessionId, peer);
            // The view of exactly this session goes (the scene binder's DisposeView).
            DestroyRoot(peer, immediate: false);
        }

        // The room is gone (this player left, or its session was lost): every remote body goes with it.
        void DisposeRemotePlayers()
        {
            DestroyAll(immediate: false);
        }

        void DestroyAll(bool immediate)
        {
            _scratch.Clear();
            _scratch.AddRange(_remotePlayersByRoomSession.Values);
            foreach (var peer in _avatarsByIdentity.Values)
            {
                if (!_scratch.Contains(peer)) _scratch.Add(peer);
            }
            _avatarsByIdentity.Clear();
            _remotePlayersByRoomSession.Clear();
            foreach (var peer in _scratch)
            {
                DestroyRoot(peer, immediate);
            }
            _scratch.Clear();
        }

        // Drop a roomSessionId → peer mapping if it still points at this peer.
        void ForgetRemotePlayer(string roomSessionId, LocalPeer peer)
        {
            if (string.IsNullOrEmpty(roomSessionId)) return;
            if (_remotePlayersByRoomSession.TryGetValue(roomSessionId, out var mapped) && mapped == peer)
            {
                _remotePlayersByRoomSession.Remove(roomSessionId);
            }
        }

        static void DestroyRoot(LocalPeer peer, bool immediate)
        {
            if (peer == null) return;
            var root = peer.Root;
            peer.Root = null;
            peer.Head = null;
            peer.User = null;
            if (root == null) return;
            if (immediate)
            {
                DestroyImmediate(root);
            }
            else
            {
                Destroy(root);
            }
        }

        // Stable per-user key that survives a reconnect: userId (the fixed clientId) is preferred;
        // roomSessionId and peerId can change across a full re-join, so they're fallbacks.
        static string StableIdentity(LocalPeer peer)
        {
            if (peer == null) return string.Empty;
            if (!string.IsNullOrEmpty(peer.ClientId)) return "u:" + peer.ClientId;
            if (!string.IsNullOrEmpty(peer.RoomSessionId)) return "s:" + peer.RoomSessionId;
            return "p:" + peer.PeerId;
        }

        // PacketPartyPeer.Label: a stable readable label that never invents a random identity.
        static string Label(LocalPeer peer)
        {
            if (!string.IsNullOrEmpty(peer.DisplayName)) return peer.DisplayName;
            if (!string.IsNullOrEmpty(peer.ClientId)) return peer.ClientId;
            if (string.IsNullOrEmpty(peer.PeerId)) return "?";
            return peer.PeerId.Length > 8 ? peer.PeerId.Substring(0, 8) : peer.PeerId;
        }

        // Consistent parent-GameObject name for player objects: [Local/Remote][display name][uid][id].
        static string PlayerObjectName(bool isLocal, string displayName, string uid, string id)
            => $"[{(isLocal ? "Local" : "Remote")}][{displayName}][{uid}][{id}]";
    }
}
