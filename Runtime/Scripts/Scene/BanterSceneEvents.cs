using UnityEngine;
using UnityEngine.Events;

public class BanterSceneEvents
{
    public UnityEvent OnLoad = new UnityEvent();
    public UnityEvent OnDomReady = new UnityEvent();
    public UnityEvent OnSceneReady = new UnityEvent();
    public UnityEvent<string> OnUnitySceneLoad = new UnityEvent<string>();
    public UnityEvent<string> OnLoadFailed = new UnityEvent<string>();
    public UnityEvent<Vector3, Vector3, bool, bool> OnTeleport = new UnityEvent<Vector3, Vector3, bool, bool>();
    public UnityEvent<string> OnPortalEnter = new UnityEvent<string>();
    public UnityEvent OnLegacyEnabled = new UnityEvent();
    public UnityEvent<bool> OnEnableDevToolsChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableTeleportChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableForceGrabChanged = new UnityEvent<bool>();
    public UnityEvent<bool> OnEnableSpiderManChanged = new UnityEvent<bool>();
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
    public UnityEvent<string> OnAttachObject = new UnityEvent<string>();
    public UnityEvent<string, string> OnPublicSpaceStateChanged = new UnityEvent<string, string>();
    public UnityEvent<string, string> OnProtectedSpaceStateChanged = new UnityEvent<string, string>();
    public UnityEvent<string, string> OnDeepLink = new UnityEvent<string, string>();
    public UnityEvent<bool> OnTTsStarted = new UnityEvent<bool>();
    public UnityEvent<string> OnTTsStoped = new UnityEvent<string>();
    public UnityEvent<Vector3> OnGravityChanged = new UnityEvent<Vector3>();
    public UnityEvent<float> OnTimeScaleChanged = new UnityEvent<float>();
    public UnityEvent<bool> OnPlayerSpeedChanged = new UnityEvent<bool>();
    public UnityEvent<string> OnMenuBrowserMessage = new UnityEvent<string>();
    public UnityEvent OnSceneReset = new UnityEvent();
    public UnityEvent<string> OnLoadUrl = new UnityEvent<string>();
    public UnityEvent<string, string> OnJsCallbackRecieved = new UnityEvent<string, string>();

    #region Legacy stuff

    public UnityEvent<bool> OnLegacyPlayerLockChanged = new UnityEvent<bool>();
    public UnityEvent<bool, GameObject> OnLegacyPlayerSitChanged = new UnityEvent<bool, GameObject>();
    public UnityEvent<bool> OnLegacyPlayerGorillaChanged = new UnityEvent<bool>();
    public UnityEvent OnLegacyControllerExtrasChanged = new UnityEvent();
    public UnityEvent OnLegacyQuaternionPoseChanged = new UnityEvent();
    public UnityEvent<string> OnVideoPrepareCompleted = new UnityEvent<string>();
    public UnityEvent<string> OnSendAframeEvent = new UnityEvent<string>();
    public UnityEvent<string> OnRequestOwnership = new UnityEvent<string>();
    public UnityEvent<string> OnResetNetworkObject = new UnityEvent<string>();
    public UnityEvent<string> OnDoIOwn = new UnityEvent<string>();
    public UnityEvent<string> OnPlayAvatar = new UnityEvent<string>();



    #endregion
}