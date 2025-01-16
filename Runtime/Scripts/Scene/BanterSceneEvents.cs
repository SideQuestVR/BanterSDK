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
    public UnityEvent OnLegacyEnabled = new UnityEvent();
    public UnityEvent<bool> OnEnableDevToolsChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableTeleportChanged = new UnityEvent<bool>();
    public UnityEvent OnLockTeleport = new UnityEvent();
    public UnityEvent OnLockSpiderman = new UnityEvent();
    public UnityEvent<bool> OnEnableForceGrabChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableSpiderManChanged = new UnityEvent<bool>();
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
    public UnityEvent<BanterAttachment> OnAttachObject = new UnityEvent<BanterAttachment>();
    public UnityEvent<BanterAttachment> OnDetachObject = new UnityEvent<BanterAttachment>();
    public UnityEvent<BanterSynced, BanterObjectId> OnSyncedObject = new UnityEvent<BanterSynced, BanterObjectId>();
    public UnityEvent<BanterSynced, BanterObjectId> OnDoIOwn = new UnityEvent<BanterSynced, BanterObjectId>();
    public UnityEvent<BanterSynced, BanterObjectId> OnTakeOwnership = new UnityEvent<BanterSynced, BanterObjectId>();
    public UnityEvent<string, string> OnPublicSpaceStateChanged = new UnityEvent<string, string>();
    public UnityEvent<string, string> OnProtectedSpaceStateChanged = new UnityEvent<string, string>();
    public UnityEvent<string, string> OnDeepLink = new UnityEvent<string, string>();
    public UnityEvent<bool> OnTTsStarted = new UnityEvent<bool>();
    public UnityEvent<string> OnTTsStoped = new UnityEvent<string>();
    public UnityEvent<string, AiImageRatio> OnAiImage = new UnityEvent<string, AiImageRatio>();
    public UnityEvent<string, float, int> OnAiModel = new UnityEvent<string, float, int>();
    public UnityEvent<string, string> OnBase64ToCDN = new UnityEvent<string, string>();
    public UnityEvent<bool> OnPlayerSpeedChanged = new UnityEvent<bool>();
    public UnityEvent<string> OnMenuBrowserMessage = new UnityEvent<string>();
    public UnityEvent OnSceneReset = new UnityEvent();
    public UnityEvent<string> OnLoadUrl = new UnityEvent<string>();
    public UnityEvent<string, string, bool> OnJsCallbackRecieved = new UnityEvent<string, string, bool>();
    public UnityEvent<BanterHeldEvents> OnHeldEvents = new UnityEvent<BanterHeldEvents>();
    public UnityEvent<BanterGrabHandle> OnGrabHandle = new UnityEvent<BanterGrabHandle>();
    public UnityEvent<BanterWorldObject> OnWorldObject = new UnityEvent<BanterWorldObject>();
    public UnityEvent<BanterWorldObject> OnWorldObjectCollectColliders = new UnityEvent<BanterWorldObject>();
    public UnityEvent<string, string> OnAvatarSet = new UnityEvent<string, string>();

    #region Legacy stuff

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
        OnLegacyEnabled.RemoveAllListeners();
        OnEnableDevToolsChanged.RemoveAllListeners();
        OnEnableTeleportChanged.RemoveAllListeners();
        OnEnableForceGrabChanged.RemoveAllListeners();
        OnEnableSpiderManChanged.RemoveAllListeners();
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
        OnAttachObject.RemoveAllListeners();
        OnPublicSpaceStateChanged.RemoveAllListeners();
        OnProtectedSpaceStateChanged.RemoveAllListeners();
        OnDeepLink.RemoveAllListeners();
        OnTTsStarted.RemoveAllListeners();
        OnTTsStoped.RemoveAllListeners();
        OnOneShot.RemoveAllListeners();
        OnAiImage.RemoveAllListeners();
        OnAiModel.RemoveAllListeners();
        OnBase64ToCDN.RemoveAllListeners();
        OnPlayerSpeedChanged.RemoveAllListeners();
        OnMenuBrowserMessage.RemoveAllListeners();
        OnSceneReset.RemoveAllListeners();
        OnLoadUrl.RemoveAllListeners();
        OnJsCallbackRecieved.RemoveAllListeners();
        OnDoIOwn.RemoveAllListeners();
        OnTakeOwnership.RemoveAllListeners();
        OnPlayAvatar.RemoveAllListeners();
        OnSyncedObject.RemoveAllListeners();
        OnAvatarSet.RemoveAllListeners();

        // Legacy stuff
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