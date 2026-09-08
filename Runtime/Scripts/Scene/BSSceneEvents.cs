using System;
using BS;
using UnityEngine;
using UnityEngine.Events;
using Unity.VisualScripting;

[RenamedFrom("Banter.SDK.BanterSceneEvents")]
public class BSSceneEvents
{
    public UnityEvent<string> KeyboardFocus = new UnityEvent<string>();
    public UnityEvent OnLoad = new UnityEvent();
    public UnityEvent OnDomReady = new UnityEvent();
    public UnityEvent OnSceneReady = new UnityEvent();
    public UnityEvent<float> OnLookedAtMirror = new UnityEvent<float>();
    public UnityEvent<string> OnUnitySceneLoad = new UnityEvent<string>();
    public UnityEvent OnSpaceChanged = new UnityEvent();
    public UnityEvent<int> OnAndroidMemoryChanged = new UnityEvent<int>();
    public UnityEvent<Vector3, Vector3, bool, bool> OnTeleport = new UnityEvent<Vector3, Vector3, bool, bool>();
    public UnityEvent<string> OnPortalEnter = new UnityEvent<string>();
    public UnityEvent<bool> OnEnableDevToolsChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableTeleportChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableForceGrabChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableSpiderManChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableHandHoldChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableRadarChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableNametagsChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnablePortalsChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableGuestsChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableFriendPositionJoinChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableAvatarsChanged = new UnityEvent<bool>();
    public UnityEvent<int> OnMaxOccupancyChanged = new UnityEvent<int>();
    public UnityEvent<Vector4> OnSpawnPointChanged = new UnityEvent<Vector4>();
    public UnityEvent<float> OnRefreshRateChanged = new UnityEvent<float>();
    public UnityEvent<Vector2> OnClippingPlaneChanged = new UnityEvent<Vector2>();
    public UnityEvent<string> OnPageOpened = new UnityEvent<string>();
    public UnityEvent<string, bool> OnOneShot = new UnityEvent<string, bool>();
    public UnityEvent<BSSynced, BSSyncedObject> OnSyncedObject = new UnityEvent<BSSynced, BSSyncedObject>();
    public UnityEvent<BSSynced, BSSyncedObject> OnTakeOwnership = new UnityEvent<BSSynced, BSSyncedObject>();
    public UnityEvent<string, string> OnPublicSpaceStateChanged = new UnityEvent<string, string>();
    public UnityEvent<string, string> OnProtectedSpaceStateChanged = new UnityEvent<string, string>();
    public UnityEvent<string, string> OnDeepLink = new UnityEvent<string, string>();
    public UnityEvent<bool> OnTTsStarted = new UnityEvent<bool>();
    public UnityEvent<string> OnTTsStoped = new UnityEvent<string>();
    public UnityEvent<string, AiImageRatio> OnAiImage = new UnityEvent<string, AiImageRatio>();
    public UnityEvent<string, AiModelSimplify, int> OnAiModel = new UnityEvent<string, AiModelSimplify, int>();
    public UnityEvent<Vector3, ForceMode> OnAddPlayerForce = new UnityEvent<Vector3, ForceMode>();
    public UnityEvent<string, string> OnBase64ToCDN = new UnityEvent<string, string>();
    public UnityEvent<SelectFileType> OnSelectFile = new UnityEvent<SelectFileType>();
    //public UnityEvent<bool> OnPlayerSpeedChanged = new UnityEvent<bool>();
    public UnityEvent<string> OnMenuBrowserMessage = new UnityEvent<string>();
    public UnityEvent OnSceneReset = new UnityEvent();
    public UnityEvent<string> OnLoadUrl = new UnityEvent<string>();
    public UnityEvent<string, string, bool> OnJsCallbackRecieved = new UnityEvent<string, string, bool>();
    public UnityEvent<string, string> OnAvatarSet = new UnityEvent<string, string>();
    public UnityEvent<string, string> OnGuestAvatarSet = new UnityEvent<string, string>();
    public UnityEvent<string, int, int, Color> OnToast = new UnityEvent<string, int, int, Color>();

    #region Physics Settings
    public UnityEvent<float> OnPhysicsMoveSpeedChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsMoveAccelerationChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsAirControlSpeedChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsAirControlAccelerationChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsDragChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsFreeFallAngularDragChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsJumpStrengthChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsHandPositionStrengthChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsHandRotationStrengthChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsHandSpringinessChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsGrappleRangeChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsGrappleReelSpeedChanged = new UnityEvent<float>();
    public UnityEvent<float> OnPhysicsGrappleSpringinessChanged = new UnityEvent<float>();
    public UnityEvent<bool> OnPhysicsGorillaModeChanged = new UnityEvent<bool>();
    #endregion
    
    public UnityEvent<string, float, string, bool> OnLeaderBoardScore = new UnityEvent<string, float, string, bool>();
    public UnityEvent<string> OnLeaderBoardClear = new UnityEvent<string>();
    public UnityEvent OnGetLeaderBoard = new UnityEvent();
    public UnityEvent<string, string> OnGetUserState = new UnityEvent<string, string>();
    public UnityEvent<string, string, string> OnSetUserState = new UnityEvent<string, string, string>();
    public UnityEvent<string, string> OnRemoveUserState = new UnityEvent<string, string>();

    /// <summary>
    /// A page's SetUserProps, as (targetUserId, "key|value" props). Raised INSTEAD of the local
    /// UserData echo under GREENFIELD_PROJECT so the host app can network the write and enforce
    /// ownership: the local path would otherwise echo a foreign-user write back to the page as if
    /// it had succeeded. Distinct from OnSetUserState above, which is the SideQuest-backend
    /// "saved value" feature.
    /// </summary>
    public UnityEvent<string, string[]> OnSetUserProps = new UnityEvent<string, string[]>();

    /// <summary>
    /// A JSON space/user state operation from a page or a Visual Scripting unit. The host app
    /// handles it, sets <c>Handled</c> synchronously, and calls <c>Respond</c> when the backend
    /// answers. See <see cref="BSStateRequest"/>.
    /// </summary>
    public UnityEvent<BSStateRequest> OnStateRequest = new UnityEvent<BSStateRequest>();
    /// <summary>
    /// A progression op from a page (fire a world-bound XP hook, read my progression). Same
    /// op-object contract as OnStateRequest: the handler sets Handled synchronously and replies.
    /// </summary>
    public UnityEvent<BSHostRequest> OnProgressionRequest = new UnityEvent<BSHostRequest>();

    public UnityEvent OnBanterUiPanelActiveChanged = new UnityEvent();

    /// <summary>A stored lighting payload arrived from the page (space load) for the
    /// app's lighting system to apply.</summary>
    public UnityEvent<string> OnLightingData = new UnityEvent<string>();

    #region Callback Functions
    public Func<string> GetUserLanguage = new Func<string>(() => { return ""; });
    public Func<string> GetPlatform = new Func<string>(() => { return ""; });
    /// <summary>The app's current baked lighting data as a persistable string, for
    /// the page to store with its saved scene. Empty = nothing baked yet.</summary>
    public Func<string> GetLightingData = new Func<string>(() => { return ""; });

    /// <summary>
    /// Synchronous read of a space-state key from the host app's local mirror, for Visual Scripting
    /// value nodes (which have no control flow to await on). Returns a compact JSON envelope:
    /// <c>{"exists":bool,"value":"string form","json":"json form","isPublic":bool}</c>.
    /// </summary>
    public Func<string, string> GetSpaceStateValue = new Func<string, string>(key => "{\"exists\":false}");

    /// <summary>
    /// Synchronous read of a user prop. Arguments are (userIdOrMe, key); the envelope is
    /// <c>{"exists":bool,"value":"string form","json":"json form"}</c>.
    /// </summary>
    public Func<string, string, string> GetUserStateValue =
        new Func<string, string, string>((userId, key) => "{\"exists\":false}");

    #endregion

    #region Legacy stuff

    public UnityEvent OnLegacyEnabled = new UnityEvent();
    public UnityEvent<bool> OnLegacyPlayerLockChanged = new UnityEvent<bool>();
    public UnityEvent<bool, UnityAndBanterObject> OnLegacyPlayerSitChanged = new UnityEvent<bool, UnityAndBanterObject>();
    public UnityEvent<bool> OnLegacyPlayerGorillaChanged = new UnityEvent<bool>();
    public UnityEvent OnLegacyControllerExtrasChanged = new UnityEvent();
    public UnityEvent OnLegacyQuaternionPoseChanged = new UnityEvent();
    public UnityEvent<string> OnVideoPrepareCompleted = new UnityEvent<string>();
    public UnityEvent<string> OnSendAframeEvent = new UnityEvent<string>();
    public UnityEvent<string> OnPlayAvatar = new UnityEvent<string>();
    public UnityEvent<string> OnLegacyPlayAvatar = new UnityEvent<string>();

    #endregion

    public void RemoveAllListeners()
    {
        // Stop Event Listeners
        OnStateRequest.RemoveAllListeners();
        OnProgressionRequest.RemoveAllListeners();
        OnSetUserProps.RemoveAllListeners();
        OnLoad.RemoveAllListeners();
        OnDomReady.RemoveAllListeners();
        OnSceneReady.RemoveAllListeners();
        OnUnitySceneLoad.RemoveAllListeners();
        OnTeleport.RemoveAllListeners();
        OnPortalEnter.RemoveAllListeners();
        OnEnableDevToolsChanged.RemoveAllListeners();
        OnEnableTeleportChanged.RemoveAllListeners();
        OnEnableForceGrabChanged.RemoveAllListeners();
        OnEnableSpiderManChanged.RemoveAllListeners();
        OnEnableHandHoldChanged.RemoveAllListeners();
        OnEnableRadarChanged.RemoveAllListeners();
        OnEnableNametagsChanged.RemoveAllListeners();
        OnEnablePortalsChanged.RemoveAllListeners();
        OnEnableGuestsChanged.RemoveAllListeners();
        OnEnableFriendPositionJoinChanged.RemoveAllListeners();
        OnEnableAvatarsChanged.RemoveAllListeners();
        OnMaxOccupancyChanged.RemoveAllListeners();
        OnSpawnPointChanged.RemoveAllListeners();
        OnRefreshRateChanged.RemoveAllListeners();
        OnClippingPlaneChanged.RemoveAllListeners();
        OnPageOpened.RemoveAllListeners();
        OnOneShot.RemoveAllListeners();
        OnPublicSpaceStateChanged.RemoveAllListeners();
        OnProtectedSpaceStateChanged.RemoveAllListeners();
        OnDeepLink.RemoveAllListeners();
        OnTTsStarted.RemoveAllListeners();
        OnTTsStoped.RemoveAllListeners();
        OnOneShot.RemoveAllListeners();
        OnAiImage.RemoveAllListeners();
        OnAiModel.RemoveAllListeners();
        OnBase64ToCDN.RemoveAllListeners();
        //OnPlayerSpeedChanged.RemoveAllListeners();
        OnMenuBrowserMessage.RemoveAllListeners();
        OnSceneReset.RemoveAllListeners();
        OnLoadUrl.RemoveAllListeners();
        OnJsCallbackRecieved.RemoveAllListeners();
        OnLightingData.RemoveAllListeners();
        OnTakeOwnership.RemoveAllListeners();
        OnPlayAvatar.RemoveAllListeners();
        OnSyncedObject.RemoveAllListeners();
        OnAvatarSet.RemoveAllListeners();

        // Physics
        OnPhysicsMoveSpeedChanged.RemoveAllListeners();
        OnPhysicsMoveAccelerationChanged.RemoveAllListeners();
        OnPhysicsAirControlSpeedChanged.RemoveAllListeners();
        OnPhysicsAirControlAccelerationChanged.RemoveAllListeners();
        OnPhysicsDragChanged.RemoveAllListeners();
        OnPhysicsFreeFallAngularDragChanged.RemoveAllListeners();
        OnPhysicsJumpStrengthChanged.RemoveAllListeners();
        OnPhysicsHandPositionStrengthChanged.RemoveAllListeners();
        OnPhysicsHandRotationStrengthChanged.RemoveAllListeners();
        OnPhysicsHandSpringinessChanged.RemoveAllListeners();
        OnPhysicsGrappleRangeChanged.RemoveAllListeners();
        OnPhysicsGrappleReelSpeedChanged.RemoveAllListeners();
        OnPhysicsGrappleSpringinessChanged.RemoveAllListeners();
        OnPhysicsGorillaModeChanged.RemoveAllListeners();
            
        // Legacy stuff
        OnLegacyEnabled.RemoveAllListeners();
        OnLegacyPlayerLockChanged.RemoveAllListeners();
        OnLegacyPlayerSitChanged.RemoveAllListeners();
        OnLegacyPlayerGorillaChanged.RemoveAllListeners();
        OnLegacyControllerExtrasChanged.RemoveAllListeners();
        OnLegacyQuaternionPoseChanged.RemoveAllListeners();
        OnVideoPrepareCompleted.RemoveAllListeners();
        OnSendAframeEvent.RemoveAllListeners();
        OnLegacyPlayAvatar.RemoveAllListeners();
    }
}