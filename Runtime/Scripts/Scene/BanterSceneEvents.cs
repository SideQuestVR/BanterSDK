using System;
using Banter.SDK;
using UnityEngine;
using UnityEngine.Events;

public class BanterSceneEvents
{
    public UnityEvent OnLoad = new UnityEvent();
    public UnityEvent OnDomReady = new UnityEvent();
    public UnityEvent OnSceneReady = new UnityEvent();
    public UnityEvent<float> OnLookedAtMirror = new UnityEvent<float>();
    public UnityEvent<string> OnUnitySceneLoad = new UnityEvent<string>();
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
    public UnityEvent<BanterSynced, BanterSyncedObject> OnSyncedObject = new UnityEvent<BanterSynced, BanterSyncedObject>();
    public UnityEvent<BanterSynced, BanterSyncedObject> OnTakeOwnership = new UnityEvent<BanterSynced, BanterSyncedObject>();
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
    public UnityEvent<BanterHeldEvents> OnHeldEvents = new UnityEvent<BanterHeldEvents>();
    public UnityEvent<BanterGrabHandle> OnGrabHandle = new UnityEvent<BanterGrabHandle>();
    public UnityEvent<BanterWorldObject> OnWorldObject = new UnityEvent<BanterWorldObject>();
    public UnityEvent<BanterWorldObject> OnWorldObjectCollectColliders = new UnityEvent<BanterWorldObject>();
    public UnityEvent<string, string> OnAvatarSet = new UnityEvent<string, string>();
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

    #region Callback Functions
    public Func<string> GetUserLanguage = new Func<string>(() => { return ""; });

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