// <mirror source="Assets/Systems/Networking/LocalUserService.cs" sha256="0e8f996c03e79f899e008ac0aa4e10d69e1ea5e9f41a55a73c7b5f8e306bf1dc" mode="port" />
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// The local player's UserData, as LocalUserService keeps it in Greenfield: present from the start, with
    /// a uid that never changes (HashUid of the stable user id), and an id that is "local" until the room
    /// is joined and the room session id while in it, set in place so the page never sees this player leave
    /// and rejoin. Here the UserData is the desktop player's, stamped before its Start runs, and announced at
    /// once so "me" resolves for every scene object (LocalUserService.EnsureUser at BanterSceneEventHandler.Start).
    /// </summary>
    internal sealed class LocalUserIdentity
    {
        // Matches the local color NetworkService used to stamp, so nothing downstream sees a change.
        const string LocalColor = LocalIdentityFactory.LocalColor;
        // Session id while not in a room. Content that reads the local user offline gets a stable,
        // non-empty id; a real roomSessionId replaces it on join (see SetNetworkSession).
        const string OfflineSessionId = "local";

        readonly LocalIdentity _identity;
        UserData _user;
        // The live room session id while in a room; null when offline.
        string _networkSessionId;

        public LocalUserIdentity(LocalIdentity identity)
        {
            _identity = identity;
        }

        /// <summary>The local UserData, or null before <see cref="EnsureUser"/>.</summary>
        public UserData Local => _user;

        /// <summary>
        /// Stamps <paramref name="user"/> as the local user and registers it with the scene now. If the scene
        /// already announced it with another identity, it is announced again (a page only refreshes a user's
        /// fields on a repeat join), never edited under the page.
        /// </summary>
        public void EnsureUser(BSScene scene, UserData user)
        {
            if (_user != null) return;
            if (scene == null || user == null) return;

            if (scene.users.Contains(user))
            {
                scene.RemoveUser(user);
            }
            _user = user;
            _user.isLocal = true;
            _user.color = LocalColor;
            ApplyIdentity();
            scene.AddUser(_user);
            Debug.Log($"[LocalMP] Registered local user uid={_user.uid} id={_user.id} " +
                      $"(session={(_networkSessionId != null ? _networkSessionId : "offline")}).");
        }

        /// <summary>
        /// Joined a room: adopt the live <paramref name="roomSessionId"/>. The uid is left as-is, so the page's
        /// user keying is stable across the offline→online transition and no user left/joined fires for us.
        /// </summary>
        public void SetNetworkSession(string roomSessionId)
        {
            _networkSessionId = string.IsNullOrEmpty(roomSessionId) ? null : roomSessionId;
            ApplyIdentity();
        }

        /// <summary>Left the room: revert to the offline session id; the user stays present.</summary>
        public void ClearNetworkSession()
        {
            _networkSessionId = null;
            ApplyIdentity();
        }

        // Stamp name/uid/id from the identity + session state. Safe to call repeatedly.
        void ApplyIdentity()
        {
            if (_user == null) return;
            var name = _identity != null ? _identity.DisplayName : null;
            _user.name = !string.IsNullOrEmpty(name) ? name : "Player";
            _user.uid = _identity != null ? _identity.Uid : _user.uid;
            _user.id = _networkSessionId != null ? _networkSessionId : OfflineSessionId;
        }
    }
}
