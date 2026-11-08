// <mirror source="Assets/Systems/Networking/LocalUserService.cs" sha256="bbe3bdde63190b3e921ea55e4a76045d35a0154fda94396802bfffb6dde9480c" mode="port" />
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// The local player's UserData, as LocalUserService keeps it in Greenfield: present from the start, with
    /// a uid that never changes (HashUid of the stable user id), and an id that is "local" until the room
    /// is joined and the room session id while in it, set in place so the page never sees this player leave
    /// and rejoin. Here the UserData is the desktop player's, stamped before its Start runs, and announced at
    /// once so "me" resolves for every scene object (LocalUserService.EnsureUser at BanterSceneEventHandler.Start).
    /// Its isSpaceAdmin comes from the settings' world owner and moderators (see RefreshSpaceAdmin).
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
            RefreshSpaceAdmin();
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
            RefreshSpaceAdmin();
        }

        /// <summary>Left the room: revert to the offline session id; the user stays present.</summary>
        public void ClearNetworkSession()
        {
            _networkSessionId = null;
            ApplyIdentity();
            RefreshSpaceAdmin();
        }

        /// <summary>
        /// The SDK's <c>isSpaceAdmin</c> for the local user, as LocalUserService.RefreshSpaceAdmin gives it: the
        /// space's owner or its moderators. Production reads the room join (<c>is_owner</c> / <c>is_space_admin</c>),
        /// the world record and the API-admin check; here the settings' worldOwnerSlot and moderatorSlots decide,
        /// the same offline and in the room.
        /// </summary>
        void RefreshSpaceAdmin()
        {
            if (_user != null) SetSpaceAdmin(_identity != null && _identity.Role != PlayerRole.Member);
        }

        void SetSpaceAdmin(bool admin)
        {
            if (_user == null || _user.isSpaceAdmin == admin) return;
            _user.isSpaceAdmin = admin;
            AnnounceToPage();
        }

        // Stamp name/uid/id from the identity + session state. Safe to call repeatedly.
        void ApplyIdentity()
        {
            if (_user == null) return;
            string previousUid = _user.uid, previousId = _user.id, previousName = _user.name;
            var name = _identity != null ? _identity.DisplayName : null;
            _user.name = !string.IsNullOrEmpty(name) ? name : "Player";
            _user.uid = _identity != null ? _identity.Uid : _user.uid;
            _user.id = _networkSessionId != null ? _networkSessionId : OfflineSessionId;
            AnnounceIfChanged(previousUid, previousId, previousName);
        }

        // LocalUserService.AnnounceIfChanged: the page learns a user's fields only from user-joined (refreshed
        // in place on a repeat join for the same uid), and the scene-ready resync runs before the room join, so
        // a new session id or name is announced again (isSpaceAdmin too, see SetSpaceAdmin). A changed uid isn't
        // (the page would add a second user).
        void AnnounceIfChanged(string previousUid, string previousId, string previousName)
        {
            if (_user.uid != previousUid) return;
            if (_user.id == previousId && _user.name == previousName) return;
            AnnounceToPage();
        }

        void AnnounceToPage()
        {
            var scene = BSScene.Current;
            if (scene == null || scene.link == null || !scene.users.Contains(_user)) return;
            scene.link.OnUserJoined(_user);
        }
    }
}
