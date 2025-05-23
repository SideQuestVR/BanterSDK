using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;
using UnityEngine.Events;
using Banter.Utilities.Async;
using Banter.Utilities;



#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif

namespace Banter.SDK
{
    public class SpawnPointData
    {
        public Vector3 position;
        public float rotation;
    }
    public class SettingsMap
    {
        public const string EnableDevTools = "EnableDevTools";
        public const string EnableTeleport = "EnableTeleport";
        public const string EnableForceGrab = "EnableForceGrab";
        public const string EnableSpiderMan = "EnableSpiderMan";
        public const string EnableHandHold = "EnableHandHold";
        public const string EnableRadar = "EnableRadar";
        public const string EnableNametags = "EnableNametags";
        public const string EnablePortals = "EnablePortals";
        public const string EnableGuests = "EnableGuests";
        public const string EnableQuaternionPose = "EnableQuaternionPose";
        public const string EnableControllerExtras = "EnableControllerExtras";
        public const string EnableFriendPositionJoin = "EnableFriendPositionJoin";
        public const string EnableDefaultTextures = "EnableDefaultTextures";
        public const string EnableAvatars = "EnableAvatars";
        public const string MaxOccupancy = "MaxOccupancy";
        public const string RefreshRate = "RefreshRate";
        public const string ClippingPlane = "ClippingPlane";
        public const string SpawnPoint = "SpawnPoint";
        
        public const string PhysicsMoveSpeed = "PhysicsMoveSpeed";
        public const string PhysicsMoveAcceleration = "PhysicsMoveAcceleration";
        public const string PhysicsAirControlSpeed = "PhysicsAirControlSpeed";
        public const string PhysicsAirControlAcceleration = "PhysicsAirControlAcceleration";
        public const string PhysicsDrag = "PhysicsDrag";
        public const string PhysicsFreeFallAngularDrag = "PhysicsFreeFallAngularDrag";
        public const string PhysicsJumpStrength = "PhysicsJumpStrength";
        public const string PhysicsHandPositionStrength = "PhysicsHandPositionStrength";
        public const string PhysicsHandRotationStrength = "PhysicsHandRotationStrength";
        public const string PhysicsHandSpringiness = "PhysicsHandSpringiness";
        public const string PhysicsGrappleRange = "PhysicsGrappleRange";
        public const string PhysicsGrappleReelSpeed = "PhysicsGrappleReelSpeed";
        public const string PhysicsGrappleSpringiness = "PhysicsGrappleSpringiness";
        public const string PhysicsGorillaMode = "PhysicsGorillaMode";

        public const string SettingsLocked = "SettingsLocked";
        public const string PhysicsSettingsLocked = "PhysicsSettingsLocked";
    }
    public class BanterScene
    {
        //private static readonly BlockingCollection<List<BanterComponentPropertyUpdate>> _propertyUpdateBatchQueue = new BlockingCollection<List<BanterComponentPropertyUpdate>>();
        //private static readonly List<BanterComponentPropertyUpdate> _propertyUpdateQueue = new List<BanterComponentPropertyUpdate>();
        //private List<string> _changes = new List<string>();
        //private static BlockingCollection<BanterComponentPropertyUpdate> _propertyUpdateQueue = new BlockingCollection<BanterComponentPropertyUpdate>();
        ConcurrentDictionary<int, UnityAndBanterObject> objects = new ConcurrentDictionary<int, UnityAndBanterObject>();
        ConcurrentDictionary<int, BanterComponent> banterComponents = new ConcurrentDictionary<int, BanterComponent>();
        public static string ORIGINAL_HOME_SPACE = "https://sq-lobby.glitch.me/?" + UnityEngine.Random.Range(0, 1000000);
        public static string CUSTOM_HOME_SPACE = "https://banter-winterland.glitch.me";// https://sq-smoke-sdk.glitch.me https://benvr.co.uk/banter/toyhouse/ sq-lobby.glitch.me "https://sq-homepage.glitch.me/home-space.html";// "https://sq-sdk-smokehouse.glitch.me"; //
        public static string KICKED_SPACE = "https://sq-lobby.glitch.me/?" + UnityEngine.Random.Range(0, 1000000);
        public static string ONBOARDING_SPACE = "https://welcome.bant.ing";

        public bool externalLoadFailed;
        public BanterLink link;
        public BanterSceneEvents events;
        public BanterSceneSettings settings;
        static BanterScene _instance;
        public event EventHandler Tick;
        //public bool dirty = true;
        public SpawnPointData spawnPoint;
        public InputActionAsset inputActionAsset;
        public InputActionMap LeftHandActions;
        public InputActionMap RightHandActions;
        public List<UserData> users = new List<UserData>();
        public List<GameObject> kitItems = new List<GameObject>();
        public DataBridge data = new DataBridge();
        // SpaceState spaceState = new SpaceState();
        public bool loaded = false;
        public bool bundlesLoaded = false;
        public bool loading = false;
        public bool isHome = false;
        public bool isFallbackHome = false;
        private float _lookAtMirror;
        public float LookAtMirror
        {
            get
            {
                if (_lookAtMirror == -1)
                {
                    _lookAtMirror = PlayerPrefs.GetFloat("lookedAtMirror", 1);
                }
                return _lookAtMirror;
            }
            set
            {
                _lookAtMirror = value;
                if (_lookAtMirror < 1)
                {
                    _lookAtMirror = 1;
                }
                PlayerPrefs.SetFloat("lookedAtMirror", _lookAtMirror);
            }
        }
        public TaskCompletionSource<bool> loadUrlTaskCompletionSource;
        private LoadingBarManager _loadingManager;
        public LoadingBarManager loadingManager
        {
            get
            {
                if (_loadingManager == null && link != null)
                {
                    _loadingManager = link.GetComponentInChildren<LoadingBarManager>();
                }
                return _loadingManager;
            }
        }
        Texture2D loadingTexture;
        public SceneState state = SceneState.NONE;
        public bool isReady
        {
            get
            {
                return state == SceneState.DOM_READY || state == SceneState.SCENE_READY || state == SceneState.UNITY_READY || state == SceneState.SCENE_START;
            }
        }
        public string CurrentUrl;
        public bool ClearCacheOnRejoin;
        public Guid InstanceID { get; private set; }

        public string LoadingStatus = "Please wait, loading live space...";
        int pendingQLocked = 0;
        int activeTaskLocked = 0;

        //only to be accessed in main thread!
        private Task activeTask = null;
        private List<BanterComponentPropertyUpdate> _pendingQueue;// = new List<BanterComponentPropertyUpdate>();

        public void EnqueueChange(string change)
        {
            link.Send(APICommands.UPDATE + change);
        }
        private void StartQ(bool onlyIfNull = false)
        {
            SpinWait.SpinUntil(() => (Interlocked.CompareExchange(ref activeTaskLocked, 1, 0) == 0));
            try
            {
                if (activeTask != null && onlyIfNull)
                {
                    return;
                }
                activeTask = Task.Run(() =>
                {
                    SpinWait.SpinUntil(() => (Interlocked.CompareExchange(ref pendingQLocked, 1, 0) == 0));
                    var q = _pendingQueue;
                    _pendingQueue = null;
                    if (Interlocked.Exchange(ref pendingQLocked, 0) != 1)
                    {
                        Debug.LogWarning("unlock failed on pendingQLocked in activeTask!");
                    }
                    if (q != null)
                    {
                        DoPropertyUpdateQueue(q);
                    }
                    if (_pendingQueue != null)
                    {
                        StartQ(false);
                    }
                    else
                    {
                        SpinWait.SpinUntil(() => (Interlocked.CompareExchange(ref activeTaskLocked, 1, 0) == 0));
                        activeTask = null;
                        if (Interlocked.Exchange(ref activeTaskLocked, 0) != 1)
                        {
                            Debug.LogWarning("unlock failed on activeTaskLocked in activeTask!");
                        }
                    }
                });
            }
            finally
            {
                if (Interlocked.Exchange(ref activeTaskLocked, 0) != 1)
                {
                    Debug.LogWarning("unlock failed on activeTaskLocked in startQ finally!");
                }
            }
        }

        private void EnqueuePropertyUpdates(List<BanterComponentPropertyUpdate> updates)
        {
            if (updates.Count > 0)
            {
                SpinWait.SpinUntil(() => (Interlocked.CompareExchange(ref pendingQLocked, 1, 0) == 0));
                try
                {
                    if (_pendingQueue == null)
                    {
                        _pendingQueue = new List<BanterComponentPropertyUpdate>();
                    }
                    _pendingQueue.AddRange(updates);
                }
                finally
                {
                    if (Interlocked.Exchange(ref pendingQLocked, 0) != 1)
                    {
                        Debug.LogWarning("unlock failed on pendingQLocked in EnqueuePropertyUpdates!");
                    }
                }
                StartQ(true);
            }
        }

        public BanterScene()
        {
            InstanceID = Guid.NewGuid();
            users = new List<UserData>();
            events = new BanterSceneEvents();
            _instance = this;
            inputActionAsset = Resources.Load<InputActionAsset>("BanterInputActions");
            inputActionAsset.Enable();
            LeftHandActions = inputActionAsset.FindActionMap("LeftHand");
            RightHandActions = inputActionAsset.FindActionMap("RightHand");
            _lookAtMirror = PlayerPrefs.GetFloat("lookedAtMirror", 1);
            events.OnLookedAtMirror.Invoke(LookAtMirror);
        }

        public void Destroy()
        {
            events.RemoveAllListeners();
            _instance = null;
        }

        #region Player Events
        public void Release(GameObject obj, HandSide side = HandSide.LEFT)
        {
            link.OnRelease(obj, side);
        }
        public void Grab(GameObject obj, Vector3 point, HandSide side = HandSide.LEFT)
        {
            link.OnGrab(obj, point, side);
        }
        public void Click(GameObject obj, Vector3 point, Vector3 normal)
        {
            var interaction = obj.GetComponent<BanterPlayerEvents>();
            if (interaction != null)
            {
                interaction.onClick.Invoke(point, normal);
            }
#if BANTER_VISUAL_SCRIPTING
        EventBus.Trigger("OnClick", new CustomEventArgs(obj.GetInstanceID().ToString(), new object[] { point, normal }));
#endif
            link.OnClick(obj, point, normal);
        }
        #endregion

        #region Spawn point
        public void SetSpawnPoint(Vector3 position, float rotation)
        {
            spawnPoint = new SpawnPointData() { position = position, rotation = rotation };
        }
        #endregion

        #region Users
        public void AddUser(UserData user)
        {
            if (!users.Contains(user))
            {
                users.Add(user);
            }
            link?.OnUserJoined(user);
#if BANTER_VISUAL_SCRIPTING
           UnityMainThreadTaskScheduler.Default.Enqueue(async () =>
            {
                await new WaitUntil(() => state == SceneState.UNITY_READY);
                EventBus.Trigger("OnUserJoined", new BanterUser() { name = user.name, id = user.id, uid = user.uid, color = user.color, isLocal = user.isLocal, isSpaceAdmin = user.isSpaceAdmin });
            });
#endif
        }
        public void RemoveUser(UserData user)
        {
            if (users.Contains(user))
            {
                users.Remove(user);
            }
            link.OnUserLeft(user);
#if BANTER_VISUAL_SCRIPTING
           UnityMainThreadTaskScheduler.Default.Enqueue(async () =>
            {
                EventBus.Trigger("OnUserLeft", new BanterUser() { name = user.name, id = user.id, uid = user.uid, color = user.color, isLocal = user.isLocal, isSpaceAdmin = user.isSpaceAdmin });
            });
#endif
        }
        public void LookedAtMirror()
        {
            LookAtMirror = LookAtMirror + 0.001f;
            events.OnLookedAtMirror.Invoke(LookAtMirror);
        }
        public void OpenPage(string msg, int reqId)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() => events.OnPageOpened.Invoke(msg));
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.OPEN_PAGE);
        }
        public void StartTTS(string msg, int reqId)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() => events.OnTTsStarted.Invoke(msg == "1"));
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.START_TTS);
        }
        public void StopTTS(string msg, int reqId)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() => events.OnTTsStoped.Invoke(msg));
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.STOP_TTS);
        }
        public void AiImage(string msg, int reqId)
        {
            try
            {
                var parts = msg.Split(MessageDelimiters.PRIMARY);
                if (parts.Length < 2)
                {
                    Debug.LogError("[Banter] AiImage message is malformed: " + msg);
                    return;
                }
                UnityMainThreadTaskScheduler.Default.Enqueue(() => events.OnAiImage.Invoke(parts[0], (AiImageRatio)int.Parse(parts[1])));
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.AI_IMAGE);
        }
        public void AddPlayerForce(string msg, int reqId)
        {
            try
            {
                var parts = msg.Split(MessageDelimiters.PRIMARY);
                if (parts.Length < 4)
                {
                    Debug.LogError("[Banter] AddPlayerForce message is malformed: " + msg);
                    return;
                }
                var force = new Vector3(NumberFormat.Parse(parts[0]), NumberFormat.Parse(parts[1]), NumberFormat.Parse(parts[2]));
                UnityMainThreadTaskScheduler.Default.Enqueue(() => events.OnAddPlayerForce.Invoke(force, (ForceMode)int.Parse(parts[3])));
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.ADD_PLAYER_FORCE);
        }
        public void AiModel(string msg, int reqId)
        {
            try
            {
                var parts = msg.Split(MessageDelimiters.PRIMARY);
                if (parts.Length < 3)
                {
                    Debug.LogError("[Banter] AiModel message is malformed: " + msg);
                    return;
                }
                UnityMainThreadTaskScheduler.Default.Enqueue(() => events.OnAiModel.Invoke(parts[0], (AiModelSimplify)int.Parse(parts[1]), int.Parse(parts[2])));
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.AI_MODEL);
        }
        public void Base64ToCDN(string msg, int reqId)
        {
            var parts = msg.Split(MessageDelimiters.PRIMARY);
            UnityMainThreadTaskScheduler.Default.Enqueue(() => events.OnBase64ToCDN.Invoke(parts[0], parts[1]));
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.BASE_64_TO_CDN);
        }
        public string GameObjectTextureToBase64(GameObject obj, int materialIndex)
        {
            var renderer = obj.gameObject.GetComponent<Renderer>();
            try
            {
                byte[] bytes = SaveTextureToImage.Do(renderer.sharedMaterials[materialIndex].mainTexture, -1, -1, SaveTextureToImage.SaveTextureFileFormat.PNG);
                // Utils.SaveTextureToFile(_lensCam.targetTexture, Path.Join(dir, $"{DateTime.Now:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}_" + (DateTime.Now - DateTime.UnixEpoch).TotalMilliseconds + ".jpg"));
                // await new WaitUntil(() => isDone);
                if(bytes != null)
                {
                    return Convert.ToBase64String(bytes);
                }else{
                    return null;
                }
                // return Convert.ToBase64String(((Texture2D)renderer.sharedMaterials[materialIndex].mainTexture).EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            return null;
        }
        public void ObjectTextureToBase64(string msg, int reqId)
        {
            var parts = msg.Split(MessageDelimiters.PRIMARY);
            var obj = GetObjectByBid(parts[0]);
            var extra = "null";
            if (obj.gameObject != null)
            {
                var b64 = GameObjectTextureToBase64(obj.gameObject, int.Parse(parts[1]));
                if (b64 != null)
                {
                    extra = b64;
                }
            }
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.OBJECT_TEX_TO_BASE_64 + MessageDelimiters.SECONDARY + extra);
        }
        public void SelectFile(string msg, int reqId)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() => events.OnSelectFile.Invoke((SelectFileType)int.Parse(msg)));
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.SELECT_FILE);
        }
        public void Gravity(string msg, int reqId)
        {
            var parts = msg.Split(MessageDelimiters.PRIMARY);
            if (parts.Length < 3)
            {
                Debug.LogError("[Banter] Gravity message is malformed: " + msg);
                return;
            }
            var gravity = new Vector3(NumberFormat.Parse(parts[0]), NumberFormat.Parse(parts[1]), NumberFormat.Parse(parts[2]));
            UnityMainThreadTaskScheduler.Default.Enqueue(() => Physics.gravity = gravity);
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.GRAVITY);
        }

        public void TimeScale(string msg, int reqId)
        {
            var timeScale = NumberFormat.Parse(msg);
            UnityMainThreadTaskScheduler.Default.Enqueue(() => Time.timeScale = timeScale);
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.TIME_SCALE);
        }
        // public void PlayerSpeed(string msg, int reqId)
        // {
        //     var speed = msg == "1";
        //     events.OnPlayerSpeedChanged?.Invoke(speed);
        //     link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.PLAYER_SPEED);
        // }

        public void LockThing(int reqId, UnityEvent handler, string command)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 handler.Invoke();
             });
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + command);
        }
        public void Teleport(string msg, int reqId)
        {
            var parts = msg.Split(MessageDelimiters.PRIMARY);
            var point = new Vector3(NumberFormat.Parse(parts[0]), NumberFormat.Parse(parts[1]), NumberFormat.Parse(parts[2]));
            var rotation = new Vector3(0, NumberFormat.Parse(parts[3]), 0);
            var stopVelocity = parts[4] == "1";
            var isSpawn = parts[5] == "1";
            if (parts.Length < 5)
            {
                Debug.LogError("[Banter] Teleport message is malformed: " + msg);
                return;
            }
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 events.OnTeleport.Invoke(point, rotation, stopVelocity, isSpawn);
             });
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.TELEPORT);
        }
        public void YtInfo(string youtubeId, int reqId)
        {
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
            };
            UnityMainThreadTaskScheduler.Default.Enqueue(async () =>
             {
                 var videoInfo = await Post.Text(
                     "https://www.youtube.com/youtubei/v1/player?key=AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8",
                     "{\"context\": {\"client\": {\"clientName\": \"ANDROID_TESTSUITE\",\"clientVersion\": \"1.9\",\"hl\": \"en\", \"androidSdkVersion\": 31}},\"videoId\": \"" + youtubeId + "\"}",
                     headers
                 );
                 var responseContext = JsonUtility.FromJson<YtResponseContext>(videoInfo);
                 var cleanJson = JsonUtility.ToJson(responseContext);
                 link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.YT_INFO + MessageDelimiters.TERTIARY + cleanJson); // + MessageDelimiters.TERTIARY + mainFunction + MessageDelimiters.TERTIARY + subFunction 
             });
        }
        #endregion

        #region User/Space properties
        public void SetProps(string propType, string data, string id = null)
        {
            var props = data.Split(MessageDelimiters.SECONDARY);
            if (propType == APICommands.SET_USER_PROPS)
            {
                var user = users.FirstOrDefault(x => id == null ? x.isLocal : x.id == id);
                if (user != null && props.Length > 0)
                {
                    user.SetProps(props);
                    UserPropChanged(props, user.id);
                }
            }
            else if (propType == APICommands.SET_PROTECTED_SPACE_PROPS || propType == APICommands.SET_PUBLIC_SPACE_PROPS)
            {
                foreach (var prop in props)
                {
                    var parts = prop.Split(MessageDelimiters.TERTIARY);
                    if (parts.Length == 2)
                    {
                        if (propType == APICommands.SET_PROTECTED_SPACE_PROPS)
                        {
                            events.OnProtectedSpaceStateChanged.Invoke(parts[0], parts[1]);
                        }
                        else
                        {
                            events.OnPublicSpaceStateChanged.Invoke(parts[0], parts[1]);
                        }
                        // spaceState.items.Add(new SpaceStateItem(){key = parts[0], value = parts[1]});
                        // SaveSpaceState();
                    }
                    else
                    {
                        Debug.LogError("Invalid prop: " + prop);
                    }
                }
                // if(props.Length > 0) {
                //     SpacePropChanged(data);
                // }
            }
        }

        public void UserPropChanged(string[] props, string id)
        {
            link.OnUserStateChanged(id + MessageDelimiters.SECONDARY + string.Join(MessageDelimiters.SECONDARY, props));
        }
        public void SpacePropChanged(string props)
        {
            // link.OnSpaceStateChanged(props);
        }
        void SaveSpaceState()
        {
            // #if !BANTER_EDITOR
            //                 PlayerPrefs.SetString("spaceState", JsonUtility.ToJson(spaceState));
            // #endif
        }
        public void LoadSpaceState()
        {
            // var state = PlayerPrefs.GetString("spaceState");
            // if(state != null) {
            //     spaceState = JsonUtility.FromJson<SpaceState>(state);
            // }
            // SpacePropChanged(string.Join(MessageDelimiters.SECONDARY, spaceState));
        }
        #endregion

        #region Manage Banter Objects
        public void AddBanterObject(GameObject gameObject, BanterObjectId banterObjectId, bool skipChangeFlush = false)
        {
            var oid = gameObject.GetInstanceID();
            if (!objects.ContainsKey(oid))
            {
                var banterObject = new BanterObject() { oid = oid };
                var unityAndBanterObject = new UnityAndBanterObject()
                {
                    gameObject = gameObject,
                    banterObject = banterObject,
                    id = banterObjectId
                };
                banterObject.unityAndBanterObject = unityAndBanterObject;
                objects.TryAdd(oid, unityAndBanterObject);
                if (!skipChangeFlush)
                {
                    FlushObjectToChanges(oid, 0, 0);
                }
            }
        }
        public BanterObject GetBanterObject(int objectId)
        {
            UnityAndBanterObject value;
            if (objects.TryGetValue(objectId, out value))
            {
                return value.banterObject;
            }
            return null;
        }
        public void DestroyBanterObject(int oid)
        {
            var banterObject = GetBanterObject(oid);
            if (banterObject != null)
            {
                banterObject.Destroy();
                objects.TryRemove(oid, out _);
            }
        }
        public BanterComponent AddBanterComponent(int objectId, int componetId, ComponentType type)
        {
            var uBanterObject = GetObject(objectId);
            if (uBanterObject.banterObject != null)
            {
                var component = new BanterComponent()
                {
                    cid = componetId,
                    type = type,
                    banterObject = uBanterObject.banterObject
                };
                uBanterObject.banterObject.AddComponent(componetId, component);
                banterComponents.TryAdd(componetId, component);
                // Components added inside unity asset bundels load at weird times.
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                 {
                     var banterComponent = uBanterObject.id.mainThreadComponentMap.FirstOrDefault(x => x.Key == componetId).Value;
                     if (banterComponent != null && banterComponent._loaded)
                     {
                         component.loaded = banterComponent._loaded;
                     }
                 });
                return component;
            }
            return null;
        }
        public BanterComponent GetBanterComponent(int componetId)
        {
            BanterComponent value;
            if (banterComponents.TryGetValue(componetId, out value))
            {
                return value;
            }
            return null;
        }
        public UnityAndBanterObject GetObjectByBid(string objectId)
        {
            UnityAndBanterObject value = new UnityAndBanterObject();
            foreach (var obj in objects)
            {
                if (obj.Value.id.Id == objectId)
                {
                    return obj.Value;
                }
            }
            return value;
        }
        public UnityAndBanterObject GetObject(int objectId)
        {
            UnityAndBanterObject value;
            objects.TryGetValue(objectId, out value);
            return value;
        }
        public GameObject GetGameObject(int objectId)
        {
            UnityAndBanterObject value;
            if (objects.TryGetValue(objectId, out value))
            {
                return value.gameObject;
            }
            return null;
        }
        public void DestroyBanterComponent(int componetId)
        {
            if (banterComponents.ContainsKey(componetId))
            {
                banterComponents[componetId].Dispose();
                banterComponents.Remove(componetId, out var _);
            }
        }
        public void RegisterBanterMonoscript(int oid, int cid, ComponentType type)
        {
            // TODO: is there a better way to register these components? Maybe not...
            var initials = new List<BanterComponentPropertyUpdate>{
                    new BanterComponentPropertyUpdate(){
                        name = PropertyName.hasUnity,
                        type = PropertyType.Bool,
                        value = true,
                        componentType = type,
                        oid = oid,
                        cid = cid
                    },
                };
            SetFromUnityProperties(initials);
            FlushObjectToChanges(oid, cid, type);
        }

        public void LogMissing()
        {
            if (loaded) return;
            Debug.LogError("Failed to load after 25 seconds.");
            foreach (var x in banterComponents.ToArray())
            {
                Debug.Log("Is loaded? : " + x.Value.cid + " : " + x.Value.banterObject.oid + " :  " + x.Value.banterObject.name + " : " + x.Value.type + " : " + x.Value.loaded + " : " + x.Value.progress);
            }
        }
        public bool HasLoadFailed()
        {
            return (loadUrlTaskCompletionSource?.Task.IsCanceled ?? false) || (loadUrlTaskCompletionSource?.Task.IsFaulted ?? false) || externalLoadFailed;
        }
        public void SetLoaded()
        {
            if (HasLoadFailed())
            {
                return;
            }
            var totalComponents = banterComponents.Count;
            var combinedPercentage = 0f;
            var banterArray = banterComponents.ToArray();
            bundlesLoaded = (state == SceneState.SCENE_READY || state == SceneState.UNITY_READY) && banterArray.Where(x => x.Value.type == ComponentType.BanterAssetBundle && !x.Value.loaded).Count() == 0;
            var loadedComponents = banterArray.Where(x =>
            {
                if (!x.Value.loaded)
                {
                    combinedPercentage += x.Value.progress;
                }
                else
                {
                    combinedPercentage += 1;
                }
                return x.Value.loaded;
            });
            var loadedComponentsCount = loadedComponents.Count();
            loaded = loadedComponentsCount == totalComponents;
            var percentDisplay = (Mathf.Round(combinedPercentage / totalComponents * 10000) / 100).ToString("0.00");
            if (combinedPercentage == 0 || totalComponents == 0)
            {
                loadingManager?.SetLoadProgress("Loading", 0, LoadingStatus, true, loadingTexture);
            }
            else
            {
                loadingManager?.SetLoadProgress("Loading " + $"({loadedComponentsCount}/{totalComponents})", combinedPercentage / totalComponents, percentDisplay + "%...", true, loadingTexture);
            }
            loadingTexture = null;
        }
        public void RegisterComponentOnMainThread(GameObject go, BanterComponentBase comp)
        {
            var obj = go.GetComponent<BanterObjectId>();
            var cid = comp.GetInstanceID();
            BanterComponent banterComp = null;
            if (obj != null)
            {
                obj.mainThreadComponentMap.Add(cid, comp);
                comp.loaded.AddListener((success, message) =>
                {
                    if (banterComp == null)
                    {
                        banterComp = GetBanterComponent(cid);
                    }
                    if (banterComp != null)
                    {
                        banterComp.loaded = true;
                        if (!success)
                        {
                            Cancel(message, false);
                        }
                        // SetLoaded();
                    }
                    else
                    {
                        // This is caught in the AddBanterComponent method instead. 
                        // Debug.LogError("BanterComponent is null: " + go.GetInstanceID() + " : " + cid + " : " + comp.name);
                    }
                    link.Send(APICommands.EVENT + APICommands.LOADED + MessageDelimiters.PRIMARY + cid);
                });
                comp.progress.AddListener((progress) =>
                {
                    if (banterComp == null)
                    {
                        banterComp = GetBanterComponent(cid);
                    }
                    if (banterComp != null)
                    {
                        banterComp.progress = progress;
                        // SetLoaded();
                    }
                });
            }
        }
        public void UnregisterComponentOnMainThread(GameObject go, BanterComponentBase comp)
        {
            var obj = go.GetComponent<BanterObjectId>();
            if (obj != null)
            {
                obj.mainThreadComponentMap.Remove(comp.GetInstanceID());
            }
        }
        #endregion

        #region Javascript Object creation and updates
        public void UpdateJsComponent(string msg, int reqId)
        {
            var sendBackParts = msg.Split(MessageDelimiters.TERTIARY);
            if (sendBackParts.Length < 2)
            {
                Debug.LogError("[Banter] Update Component message sendBackParts is malformed: " + msg);
                return;
            }
            if (!isReady)
            {
                return;
            }

            var sheetFerMainThread = new List<Tuple<BanterComponentBase, List<object>>>();

            for (int i = 1; i < sendBackParts.Length; i++)
            {
                var parts = sendBackParts[i].Split(MessageDelimiters.PRIMARY);
                if (parts.Length < 3)
                {
                    Debug.LogError("[Banter] Update Component message is malformed: " + sendBackParts[i]);
                    return;
                }
                var gameObject = GetObject(int.Parse(parts[0]));
                var componentId = parts[1];
                var sendBack = int.Parse(sendBackParts[0]) == 1;
                try
                {
                    BanterComponentBase unityComp = null;
                    gameObject.id?.mainThreadComponentMap.TryGetValue(int.Parse(componentId), out unityComp);
                    if (unityComp == null)
                    {
                        Debug.LogError("[Banter] Unity component not found: " + msg);
                    }
                    var banterComp = GetBanterComponent(int.Parse(componentId));
                    var objs = SetComponentProperties(2, parts, banterComp, msg);
                    sheetFerMainThread.Add(new Tuple<BanterComponentBase, List<object>>(unityComp, objs));
                    if (banterComp != null && sendBack)
                    {
                        link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.COMPONENT_UPDATED);
                    }
                }
                catch (Exception e)
                {
                    // [Banter] Error updating component offthread: Object reference not set to an instance of an object: True, 0~:~-2765352|-2765670|88~~5~~0~~16.5606751 ~~0
                    Debug.LogError("[Banter] Error updating component offthread: " + e.Message + ": " + (gameObject.id == null) + ", " + msg);
                }
            }
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 for (int i = 0; i < sheetFerMainThread.Count; i++)
                 {
                     var s = sheetFerMainThread[i];
                     try
                     {
                         s.Item1.Deserialise(s.Item2);
                     }
                     catch (Exception e)
                     {
                         Debug.LogError("[Banter] Error updating component: " + e.Message + ": " + msg);
                     }
                 }
             });
        }
        public async void CallMethodOnJsComponent(string msg, int reqId, string full)
        {
            // try
            // {
            var msgParts = msg.Split(MessageDelimiters.PRIMARY, 3);
            if (msgParts.Length < 3)
            {
                Debug.LogError("[Banter] Call Method message is malformed: " + msg);
                return;
            }
            var methodName = msgParts[1];
            if (!int.TryParse(msgParts[0], out var oid))
            {
                Debug.LogWarning($"CallMethodOnJsComponent called method {methodName} with an unset instance ID, it probably hasn't been created yet");
                return;
            }
            var banterComponent = GetBanterComponent(int.Parse(msgParts[0]));

            // var paramsList = "";
            // for(int i = 2; i < msgParts.Length; i++) {
            //     paramsList += msgParts[i];
            //     if(i < msgParts.Length - 1) {
            //         paramsList += MessageDelimiters.PRIMARY;
            //     }
            // }
            var parameters = msgParts[2].Split(MessageDelimiters.SECONDARY, StringSplitOptions.RemoveEmptyEntries);
            var paramList = new List<object>();
            foreach (var param in parameters)
            {
                var paramParts = param.Split(MessageDelimiters.TERTIARY);
                if (paramParts.Length < 1)
                {
                    Debug.LogError("[Banter] Call Method message is malformed: " + msg);
                    return;
                }
                var paramType = (PropertyType)int.Parse(paramParts[0]);
                switch (paramType)
                {
                    case PropertyType.String:
                        paramList.Add(paramParts[1]);
                        break;
                    case PropertyType.Bool:
                        paramList.Add(paramParts[1] == "1");
                        break;
                    case PropertyType.Float:
                        paramList.Add(NumberFormat.Parse(paramParts[1]));
                        break;
                    case PropertyType.Int:
                        paramList.Add(int.Parse(paramParts[1]));
                        break;
                    case PropertyType.Vector2:
                        paramList.Add(new Vector2(NumberFormat.Parse(paramParts[1]), NumberFormat.Parse(paramParts[2])));
                        break;
                    case PropertyType.Vector3:
                        paramList.Add(new Vector3(NumberFormat.Parse(paramParts[1]), NumberFormat.Parse(paramParts[2]), NumberFormat.Parse(paramParts[3])));
                        break;
                    case PropertyType.Vector4:
                        paramList.Add(new Vector4(NumberFormat.Parse(paramParts[1]), NumberFormat.Parse(paramParts[2]), NumberFormat.Parse(paramParts[3]), NumberFormat.Parse(paramParts[4])));
                        break;
                }
            }

            await banterComponent.CallMethod(methodName, paramList, (returnValue) =>
            {
                if (returnValue != null)
                {
                    var strReturnValue = returnValue;
                    if (returnValue is bool)
                    {
                        strReturnValue = PropertyType.Bool + MessageDelimiters.TERTIARY + ((bool)returnValue ? "1" : "0");
                    }
                    else if (returnValue is float)
                    {
                        strReturnValue = PropertyType.Float + MessageDelimiters.TERTIARY + ((float)returnValue).ToString(CultureInfo.InvariantCulture.NumberFormat);
                    }
                    else if (returnValue is int)
                    {
                        strReturnValue = PropertyType.Int + MessageDelimiters.TERTIARY + ((int)returnValue).ToString(CultureInfo.InvariantCulture.NumberFormat);
                    }
                    else if (returnValue is Vector2)
                    {
                        var vec = (Vector2)returnValue;
                        strReturnValue = PropertyType.Vector2 + MessageDelimiters.TERTIARY + vec.x.ToString(CultureInfo.InvariantCulture.NumberFormat) + MessageDelimiters.TERTIARY + vec.y.ToString(CultureInfo.InvariantCulture.NumberFormat);
                    }
                    else if (returnValue is Vector3)
                    {
                        var vec = (Vector3)returnValue;
                        strReturnValue = PropertyType.Vector3 + MessageDelimiters.TERTIARY + vec.x.ToString(CultureInfo.InvariantCulture.NumberFormat) + MessageDelimiters.TERTIARY + vec.y.ToString(CultureInfo.InvariantCulture.NumberFormat) + MessageDelimiters.TERTIARY + vec.z.ToString(CultureInfo.InvariantCulture.NumberFormat);
                    }
                    else if (returnValue is Vector4)
                    {
                        var vec = (Vector4)returnValue;
                        strReturnValue = PropertyType.Vector4 + MessageDelimiters.TERTIARY + vec.x.ToString(CultureInfo.InvariantCulture.NumberFormat) + MessageDelimiters.TERTIARY + vec.y.ToString(CultureInfo.InvariantCulture.NumberFormat) + MessageDelimiters.TERTIARY + vec.z.ToString(CultureInfo.InvariantCulture.NumberFormat) + MessageDelimiters.TERTIARY + vec.w.ToString(CultureInfo.InvariantCulture.NumberFormat);
                    }
                    else if (returnValue is Quaternion)
                    {
                        var vec = (Quaternion)returnValue;
                        strReturnValue = PropertyType.Vector4 + MessageDelimiters.TERTIARY + vec.x.ToString(CultureInfo.InvariantCulture.NumberFormat) + MessageDelimiters.TERTIARY + vec.y.ToString(CultureInfo.InvariantCulture.NumberFormat) + MessageDelimiters.TERTIARY + vec.z.ToString(CultureInfo.InvariantCulture.NumberFormat) + MessageDelimiters.TERTIARY + vec.w.ToString(CultureInfo.InvariantCulture.NumberFormat);
                    }
                    link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.METHOD_RETURN + MessageDelimiters.PRIMARY + strReturnValue);
                }
            });
            // }
            // catch (Exception ex)
            // {
            //     Debug.LogError("CallMethodOnJsComponent threw an exception: "  +full);
            //     Debug.LogException(ex);
            // }
        }
        public void SetJsObjectActive(string msg, int reqId)
        {
            var msgParts = msg.Split(MessageDelimiters.PRIMARY);
            if (msgParts.Length < 2)
            {
                Debug.LogError("[Banter] SetJsObjectActive message is malformed: " + msg);
                return;
            }
            try
            {
                var banterObject = GetGameObject(int.Parse(msgParts[0]));
                if (banterObject != null)
                {
                    UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                    {
                        banterObject.SetActive(int.Parse(msgParts[1]) == 1);
                        SendObjectUpdate(banterObject, reqId);
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("SetJsObjectActive threw an exception: " + msg);
                Debug.LogException(ex);
            }
        }
        public void SetJsObjectLayer(string msg, int reqId)
        {
            var msgParts = msg.Split(MessageDelimiters.PRIMARY);
            if (msgParts.Length < 2)
            {
                Debug.LogError("[Banter] SetJsObjectActive message is malformed: " + msg);
                return;
            }
            var banterObject = GetGameObject(int.Parse(msgParts[0]));
            if (banterObject != null)
            {
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                 {
                     banterObject.layer = int.Parse(msgParts[1]);
                     SendObjectUpdate(banterObject, reqId);
                 });
            }
        }
        public void PhysicsRaycast(string msg, int reqId)
        {
            var msgParts = msg.Split(MessageDelimiters.PRIMARY);
            if (msgParts.Length < 6)
            {
                Debug.LogError("[Banter] Physics Raycast message is malformed: " + msg);
                return;
            }
            var position = new Vector3(NumberFormat.Parse(msgParts[0]), NumberFormat.Parse(msgParts[1]), NumberFormat.Parse(msgParts[2]));
            var direction = new Vector3(NumberFormat.Parse(msgParts[3]), NumberFormat.Parse(msgParts[4]), NumberFormat.Parse(msgParts[5]));
            var maxDistance = msgParts.Length > 6 ? NumberFormat.Parse(msgParts[6]) : -1;
            var layerMask = msgParts.Length > 7 ? int.Parse(msgParts[7]) : -1;
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 RaycastHit hit;
                 bool didHit = false;
                 if (msgParts.Length == 6)
                 {
                     didHit = Physics.Raycast(position, direction, out hit);
                 }
                 else if (msgParts.Length == 7)
                 {
                     didHit = Physics.Raycast(position, direction, out hit, maxDistance);
                 }
                 else
                 {
                     didHit = Physics.Raycast(position, direction, out hit, maxDistance, layerMask);
                 }

                 if (didHit)
                     link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.RAYCAST + MessageDelimiters.PRIMARY + hit.collider.gameObject.GetInstanceID() + MessageDelimiters.PRIMARY + hit.point.x + MessageDelimiters.PRIMARY + hit.point.y + MessageDelimiters.PRIMARY + hit.point.z + MessageDelimiters.PRIMARY + hit.normal.x + MessageDelimiters.PRIMARY + hit.normal.y + MessageDelimiters.PRIMARY + hit.normal.z);
             });
        }
        public void InstantiateJsObject(string msg, int reqId)
        {
            var gameObject = GetGameObject(int.Parse(msg));
            if (gameObject != null)
            {
                UnityMainThreadTaskScheduler.Default.Enqueue(async () =>
                 {
                     var newObject = GameObject.Instantiate(gameObject);
                     var objectId = newObject.GetComponent<BanterObjectId>();
                     objectId.GenerateId(true);
                     newObject.transform.parent = settings.parentTransform;
                     AddBanterObject(newObject, objectId);
                     var banterObject = GetBanterObject(newObject.GetInstanceID());
                     await new WaitForEndOfFrame();
                     foreach (var comp in banterObject.banterComponents)
                     {
                         await comp.Value.GetProperties();
                         foreach (var prop in comp.Value.componentProperties)
                         {
                             string change = Serialise(prop.Value, comp.Value);
                             if (change != null)
                             {
                                 EnqueueChange(change);
                             }
                         }
                     }
                     //dirty = true;
                     await new WaitForEndOfFrame();
                     SendObjectUpdate(newObject, reqId);
                 });
            }
        }
        public void SendBrowserMessage(string msg, int reqId)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() => events.OnMenuBrowserMessage.Invoke(msg));
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.SEND_MENU_BROWSER_MESSAGE);
        }
        public void AddJsComponent(string msg, int reqId)
        {
            var parts = msg.Split(MessageDelimiters.PRIMARY);
            if (parts.Length < 3)
            {
                Debug.LogError("[Banter] Add Component message is malformed: " + msg);
                return;
            }
            var oid = int.Parse(parts[0]);
            var gameObject = GetGameObject(oid);
            if (gameObject == null)
            {
                Debug.LogError("[Banter] Add Component object is null, count:  " + objects.Count + ", " + InstanceID + ", " + msg);
                return;
            }
            else
            {
                // Debug.Log("[Banter] Adding component for" + oid + ", count:  " + objects.Count + ", " + InstanceID + ", " + msg);
            }
            var linkId = parts[1];
            var componentType = (ComponentType)int.Parse(parts[2]);
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 if (!isReady)
                 {
                     return;
                 }
                 var comp = BanterComponentFromType.CreateComponent(gameObject, componentType);
                 if (comp == null)
                 {
                     Debug.LogError("[Banter] Component type not found: " + componentType);
                     return;
                 }
                 var banterComp = AddBanterComponent(gameObject.GetInstanceID(), comp.GetInstanceID(), componentType);
                 if (banterComp != null)
                 {
                     link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY +
                     APICommands.COMPONENT_ADDED + MessageDelimiters.PRIMARY + banterComp.banterObject.oid + MessageDelimiters.PRIMARY + banterComp.cid +
                     MessageDelimiters.PRIMARY + (int)banterComp.type + MessageDelimiters.PRIMARY + linkId);
                 }

                 var constructorProps = SetComponentProperties(3, parts, banterComp, msg);
                 comp.Init(constructorProps);
             });
        }
        private List<object> SetComponentProperties(int startIndex, string[] parts, BanterComponent banterComp, string msg = null)
        {
            var updates = new List<object>();
            for (int i = startIndex; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i]))
                {
                    continue;
                }
                var propParts = parts[i].Split(MessageDelimiters.SECONDARY);
                if (propParts.Length < 3)
                {
                    Debug.LogError("[Banter] wtf: " + banterComp.type + ": " + msg);
                    continue;
                }
                var name = (PropertyName)int.Parse(propParts[0]);
                var type = (PropertyType)int.Parse(propParts[1]);
                try
                {
                    switch (type)
                    {
                        case PropertyType.String:
                            var valString = propParts[2];
                            updates.Add(new BanterString() { n = name, x = valString });
                            banterComp.UpdateProperty(name, valString);
                            break;
                        case PropertyType.Bool:
                            var valBool = propParts[2] == "1";
                            updates.Add(new BanterBool() { n = name, x = valBool });
                            banterComp.UpdateProperty(name, valBool);
                            break;
                        case PropertyType.Float:
                            var valFloat = NumberFormat.Parse(propParts[2]);
                            updates.Add(new BanterFloat() { n = name, x = valFloat });
                            banterComp.UpdateProperty(name, valFloat);
                            break;
                        case PropertyType.Int:
                            if (propParts[2].Equals("null"))
                            {
                                propParts[2] = "0";
                            }
                            var valInt = int.Parse(propParts[2]);
                            updates.Add(new BanterInt() { n = name, x = valInt });
                            banterComp.UpdateProperty(name, valInt);
                            break;
                        case PropertyType.Vector2:
                            var valVector2X = NumberFormat.Parse(propParts[2]);
                            var valVector2Y = NumberFormat.Parse(propParts[3]);
                            updates.Add(new BanterVector2() { n = name, x = valVector2X, y = valVector2Y });
                            banterComp.UpdateProperty(name, new Vector2(valVector2X, valVector2Y));
                            break;
                        case PropertyType.Vector3:
                            var valVector3X = NumberFormat.Parse(propParts[2]);
                            var valVector3Y = NumberFormat.Parse(propParts[3]);
                            var valVector3Z = NumberFormat.Parse(propParts[4]);
                            updates.Add(new BanterVector3() { n = name, x = valVector3X, y = valVector3Y, z = valVector3Z });
                            banterComp.UpdateProperty(name, new Vector3(valVector3X, valVector3Y, valVector3Z));
                            break;
                        case PropertyType.Vector4:
                        case PropertyType.Quaternion:
                            var valVector4X = NumberFormat.Parse(propParts[2]);
                            var valVector4Y = NumberFormat.Parse(propParts[3]);
                            var valVector4Z = NumberFormat.Parse(propParts[4]);
                            var valVector4W = NumberFormat.Parse(propParts[5]);
                            updates.Add(new BanterVector4() { n = name, x = valVector4X, y = valVector4Y, z = valVector4Z, w = valVector4W });
                            banterComp.UpdateProperty(name, new Vector4(valVector4X, valVector4Y, valVector4Z, valVector4W));
                            break;
                    }

                }
                catch (Exception e)
                {
                    Debug.LogError("[Banter] Error setting property: " + name + " " + type + " " + e.Message + ": " + msg);
                }
            }
            return updates;
        }
        public void SetParent(string msg, int reqId)
        {
            var msgParts = msg.Split(MessageDelimiters.PRIMARY);
            if (msgParts.Length < 3)
            {
                Debug.LogError("[Banter] Set Parent message is malformed: " + msg);
                return;
            }
            var parentObject = GetGameObject(int.Parse(msgParts[0]));
            var banterObject = GetGameObject(int.Parse(msgParts[1]));
            if (banterObject != null && parentObject != null)
            {
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                 {
                     banterObject.transform.SetParent(parentObject.transform, int.Parse(msgParts[2]) == 1);
                     SendObjectUpdate(banterObject, reqId);
                 });
            }
        }
        public async Task WaitForEndOfFrame(int reqId)
        {
            await new WaitForEndOfFrame();
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY);
        }
        public void UpdateJsObject(string msg, int reqId)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() => SendObjectUpdate(int.Parse(msg), reqId));
        }
        // TODO: Lets look at what this is doing and why, it could be better to propagate updates back to the object another way
        void SendObjectUpdate(int oid, int reqId)
        {
            var banterObject = GetGameObject(oid);
            SendObjectUpdate(banterObject, reqId);
        }
        // TODO: This too
        void SendObjectUpdate(GameObject banterObject, int reqId)
        {
            if (banterObject != null)
            {
                int parent = 0;
                if (banterObject.activeSelf && banterObject.transform.parent != null && settings.parentTransform != banterObject.transform.parent)
                {
                    parent = banterObject.transform.parent.gameObject.GetInstanceID();
                }
                link.Send(GetObjectUpdateString(banterObject, reqId, parent, null));
            }
        }

        public void DestroyJsObject(int oid, int reqId)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 var gameObject = GetGameObject(oid);
                 if (gameObject != null)
                 {
                     // Should trigger OnDestroy on BanterObjectId to clean the object up.
                     // Calling set active so that OnDestroy is called.
                     gameObject.SetActive(true);
                     GameObject.Destroy(GetGameObject(oid));
                 }
                 else
                 {
                     // GameObject is null, killing the banter object now.
                     BanterScene.Instance().DestroyBanterObject(oid);
                 }

             });
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY);
        }
        public void DestroyJsComponent(int cid, int reqId)
        {
            if (banterComponents.TryGetValue(cid, out var comp))
            {
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                 {
                     var gameObject = GetObject(comp.banterObject.oid);
                     if (gameObject.id != null)
                     {
                         try
                         {
                             if (gameObject.id.mainThreadComponentMap.ContainsKey(cid))
                             {
                                 GameObject.Destroy(gameObject.id.mainThreadComponentMap[cid]);
                             }
                         }
                         catch (Exception e)
                         {
                             Debug.LogError("[Banter] Error destroying component: " + e.Message);
                         }
                     }
                     var component = GetBanterComponent(cid);
                     if (component != null)
                     {
                         DestroyBanterComponent(cid);
                     }
                 });
                link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY);
            }
        }

        string GetObjectUpdateString(GameObject go, int reqId, int parent, string linkId)
        {
            return APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY +
                APICommands.OBJECT_ADDED + MessageDelimiters.PRIMARY + go.GetInstanceID() + MessageDelimiters.PRIMARY + (go.activeSelf ? 1 : 0) +
                MessageDelimiters.PRIMARY + go.name + MessageDelimiters.PRIMARY + go.layer + MessageDelimiters.PRIMARY + parent + MessageDelimiters.PRIMARY + linkId;
        }
        public void AddJsObject(string msg, int reqId)
        {
            var parts = msg.Split(MessageDelimiters.PRIMARY);
            if (parts.Length < 3)
            {
                Debug.LogError("[Banter] Add Object message is malformed: " + msg);
                return;
            }
            UnityMainThreadTaskScheduler.Default.Enqueue(async () =>
             {
                 try
                 {
                     var go = new GameObject(parts[2]);
                     go.transform.parent = settings.parentTransform;
                     AddBanterObject(go, go.AddComponent<BanterObjectId>(), true);
                     link.Send(GetObjectUpdateString(go, reqId, 0, parts[0]));
                     await new WaitForSeconds(2);
                     if (parts[1] == "0")
                     {
                         Debug.Log("Creating object that is not active: " + go.name);
                         go.SetActive(false);
                     }
                 }
                 catch (Exception)
                 {
                     Debug.LogError("[Banter] Add Object after act: " + msg);
                 }
             });
        }
        public void Cancel(string message, bool isUserCancel = false)
        {
            if (!UnityGame.OnMainThread)
            {
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    Cancel(message, isUserCancel);
                });
                return;
            }
            loaded = true;
            loading = false;
            externalLoadFailed = true;
            loadUrlTaskCompletionSource?.TrySetException(new Exception((isUserCancel ? "Cancelled: " : "Load failed: ") + message));
            loadUrlTaskCompletionSource?.TrySetCanceled();
            state = SceneState.LOAD_FAILED;
            loadingManager?.SetLoadProgress(isUserCancel ? "Loading Cancelled" : "Loading failed", 0, message, true);
            LogLine.Do(isUserCancel ? "Loading Cancelled" : "Loading failed");
            loadingManager?.UpdateCancelText();
            // if(!isHome) {
            // loadUrlTaskCompletionSource?.TrySetException(new Exception((isUserCancel? "Loading cancelled: " : "The URL failed to load: ") + message));
            // // _ = loadingManager.LoadOut();
            // loadingManager?.SetLoadProgress("Failed to load", 0, message, true);
            // loadingManager?.CancelPressed();
            //UnityMainThreadTaskScheduler.Default.QueueAction(() => {
            //     events.OnLoadFailed.Invoke(message);
            // });
            // }
        }
        // public async Task LoadLobby() {
        //     await LoadUrl(CUSTOM_HOME_SPACE); // https://sq-homepage.glitch.me/home-space.html
        // }
        public void ResetLoadingProgress()
        {
            loadingManager?.SetLoadProgress("Loading", 0, LoadingStatus, true);
        }
        void ResetSceneAbilitySettings()
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 _ = settings.Reset();
             });
        }
        public async Task ResetScene()
        {
            try
            {
                state = SceneState.NONE;
                objects.Clear();
                banterComponents.Clear();
                ResetSceneAbilitySettings();
                ResetLoadingProgress();
                bundlesLoaded = false;
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                 {
                     events.OnSceneReset.Invoke();
                 });
                // This seems to be a bug in 2022, hard crash without this line.
                GameObject.FindObjectsOfType<Cloth>().ToList().ForEach(x => GameObject.Destroy(x));
                await Resources.UnloadUnusedAssets();
            }
            catch (Exception e)
            {
                Debug.LogError("[Banter] Error resetting scene: " + e.Message);
            }
        }
        public async Task OpenLoadingScreen(string url)
        {
            loadingManager?.Preload();
            await loadingManager?.LoadIn(url);
        }
        public async Task ShowSpaceImage(string url)
        {
            try
            {
                var space = await Get.SpaceMeta(url);
                if (space != null && !string.IsNullOrEmpty(space.icon))
                {
                    loadingTexture = await Get.Texture(space.icon + "?size=1280");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Banter] Error loading space image: " + url + " : " + e.Message);
            }
        }
        public async Task LoadUrl(string url, bool isLoadingOpen = false)
        {
            state = SceneState.NONE;
            loading = true;
            externalLoadFailed = false;
            loadUrlTaskCompletionSource = new TaskCompletionSource<bool>();
            CurrentUrl = url;
            this.isHome = url == CUSTOM_HOME_SPACE;
            this.isFallbackHome = url == ORIGINAL_HOME_SPACE;
            loadingManager?.UpdateCancelText();
            ResetLoadingProgress();
            UnityMainThreadTaskScheduler.Default.Enqueue(async () =>
            {
                if (!isLoadingOpen)
                {
                    await this.OpenLoadingScreen(url);
                }
                // Unity coming out of play mode tries to go to the lobby, just nipping that in the bud.
                if (!Application.isPlaying)
                {
                    return;
                }
                LogLine.Do("[BanterScene] Loading ShowSpaceImage: " + url);
                await ShowSpaceImage(url);
                LogLine.Do("[BanterScene] Loading ResetScene: " + url);
                await ResetScene();
                LogLine.Do("[BanterScene] Loading LoadUrl: " + url);
                await link.LoadUrl(url);
                LogLine.Do("[BanterScene] Loading WaitUntil: " + url);
                await new WaitUntil(() => loaded);
                LogLine.Do("[BanterScene] Loading after WaitUntil: " + url);
                LoadingStatus = "Please wait, loading live space...";
                if (HasLoadFailed())
                {
                    loading = false;
                    return;
                }
                loadUrlTaskCompletionSource.SetResult(true);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    events.OnUnitySceneLoad.Invoke(url);
                });
                LogLine.Do("[BanterScene] Loading Task.Delay(2500): " + url);

                await Task.Delay(2500);
                LogLine.Do("[BanterScene] Loading loadingManager?.LoadOut: " + url);

                await loadingManager?.LoadOut();
                loading = false;
            });
            await loadUrlTaskCompletionSource.Task;
        }
        public async Task OnLoad(string instanceId)
        {
            // This is fired when the page loads or realods from the browser end, so it can be fired becuase 
            // we have explicitly naviagted the page, but also becuase someone hut f5 in the debugger. 
            state = SceneState.NONE;
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 if (settings != null)
                 {
                     _ = settings.Reset();
                     settings.Destroy();
                 }
                 settings = new BanterSceneSettings(instanceId);
                 events.OnLoad.Invoke();
             });
            // TODO: This wont work with 5 ms, but I tested it hundreds of times at 10ms and it never 
            // failed at that level. Maybe it is because the other thread stuff above? I think so. 
            // Maybe it will fail on android? Or other slower computers? Making it 100 for good measure.
            // :thumbsup: :worksonmymachine: :sadcat:
            await Task.Delay(100);
            FlushAllSceneProps();
        }

        void FlushAllSceneProps()
        {
            foreach (var obj in objects.Values)
            {
                var comps = obj.banterObject.banterComponents.Values;
                if (comps.Count == 0)
                {
                    FlushObjectToChanges(obj.banterObject.oid, 0);
                }
                foreach (var comp in comps)
                {
                    var props = comp.componentProperties.Values;
                    if (props.Count == 0)
                    {
                        FlushObjectToChanges(obj.banterObject.oid, comp.cid, comp.type);
                    }
                    foreach (var prop in comp.componentProperties.Values)
                    {
                        string change = Serialise(prop, comp);
                        if (change != null)
                        {
                            EnqueueChange(change);
                        }
                        else
                        {
                            LogLine.Do(Color.red, "[Banter]", "Unknown property type: " + prop.type);
                        }
                    }
                }
            }
        }
        public async Task WatchProperties(string msg, int reqId)
        {
            var msgParts = msg.Split(MessageDelimiters.PRIMARY);
            if (msgParts.Length < 2)
            {
                Debug.LogError("[Banter] Watch Properties message is malformed: " + msg);
                return;
            }
            var banterComponent = GetBanterComponent(int.Parse(msgParts[0]));
            var properties = msgParts[1].Split(MessageDelimiters.SECONDARY);
            var props = new PropertyName[properties.Length];
            for (int i = 0; i < properties.Length; i++)
            {
                props[i] = (PropertyName)int.Parse(properties[i]);
            }
            await banterComponent.WatchProperties(props);
        }
        public async Task QueryComponents(string msg, int reqId)
        {
            List<string> changes = new List<string>();
            foreach (var comp in msg.Split(MessageDelimiters.PRIMARY))
            {
                var comParts = comp.Split(MessageDelimiters.SECONDARY);
                if (comParts.Length < 2)
                {
                    Debug.LogError("[Banter] Query Components message is malformed: " + msg);
                    continue;
                }
                var banterComponent = GetBanterComponent(int.Parse(comParts[0]));
                await banterComponent.GetProperties();
                var properties = comParts[1].Split(MessageDelimiters.TERTIARY);
                foreach (var prop in properties)
                {
                    banterComponent.componentProperties.TryGetValue((PropertyName)int.Parse(prop), out var property);
                    if (property != null)
                    {
                        string change = Serialise(property, banterComponent);
                        if (change != null)
                        {
                            changes.Add(change);
                        }
                    }
                }
            }
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.UPDATE + string.Join(MessageDelimiters.TERTIARY, changes.ToArray()));
        }
        #endregion

        #region Set Banter Properties from the Unity thread and send to JS on another thread


        //when _tickBuffer is not null, the scene's Tick event is executing and all property updates will be buffered to this list
        //  then buffered updates will then be handled all at once.
        //todo: make sure that there aren't too many out-of-Tick things being enqueued
        //private bool bufferUpdates = false;
        private List<BanterComponentPropertyUpdate> _tickBuffer = null;
        public void SetFromUnityProperties(List<BanterComponentPropertyUpdate> properties, Action callback = null, string name = "")
        {
            if (properties.Count > 0)
            {
                properties[properties.Count - 1].callback = callback;
            }
            else
            {
                callback?.Invoke();
            }
            if (_tickBuffer != null)
            {
                int ct = properties.Count;
                for (int i = 0; i < ct; i++)
                {
                    _tickBuffer.Add(properties[i]);
                }
            }
            else
            {
                EnqueuePropertyUpdates(properties);
            }
        }


        private void DoPropertyUpdateQueue(List<BanterComponentPropertyUpdate> toProcess)
        {
            List<BanterComponentPropertyUpdate> toReenqueue = null;
            for (int i = 0; i < toProcess.Count; i++)
            {
                var property = toProcess[i];
                var component = GetBanterComponent(property.cid);
                if (component == null)
                {
                    component = AddBanterComponent(property.oid, property.cid, (ComponentType)property.componentType);
                }
                if (component != null)
                {
                    component.SetProperty(property.name, property.type, property.value, property.callback);
                }
                else
                {
                    // This is here because in some odd situation maybe unity initialises components in a weird order. I have never had this happen, but if a component
                    // tries to set properties before the object exists then this could hit. This will re-queue the property to be set later. This will likely result 
                    // in a memory leak, so for now im removing it and instead logging here.
                    // LogLine.Do(Color.red, LogTag.Banter, "Component not found for property: " + property.name + " (" + property.type + ") on " + property.componentType + " for component " + property.cid + " on object " + property.oid);
                    if (toReenqueue == null)
                    {
                        toReenqueue = new List<BanterComponentPropertyUpdate>();
                    }
                    toReenqueue.Add(property);
                    //ReEnque(property);
                }
            }
            if (toReenqueue?.Count > 0)
            {
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                 {
                     Debug.LogWarning($"Having to reenqueue {toReenqueue.Count}");
                     EnqueuePropertyUpdates(toReenqueue);
                 });
            }
        }

        public string Serialise(BanterComponentProperty prop, BanterComponent comp)
        {
            string returnValue = null;
            if (prop.value is bool)
            {
                var propValue = (BanterBool)(bool)prop.value;
                propValue.n = prop.name;
                returnValue = propValue.Serialise();
            }
            else if (prop.value is float)
            {
                var propValue = (BanterFloat)(float)prop.value;
                propValue.n = prop.name;
                returnValue = propValue.Serialise();
            }
            else if (prop.value is int)
            {
                var propValue = (BanterInt)(int)prop.value;
                propValue.n = prop.name;
                returnValue = propValue.Serialise();
            }
            else if (prop.value is Vector2)
            {
                var propValue = (BanterVector2)(Vector2)prop.value;
                propValue.n = prop.name;
                returnValue = propValue.Serialise();
            }
            else if (prop.value is Vector3)
            {
                var propValue = (BanterVector3)(Vector3)prop.value;
                propValue.n = prop.name;
                returnValue = propValue.Serialise();
            }
            else if (prop.value is Vector4)
            {
                var propValue = (BanterVector4)(Vector4)prop.value;
                propValue.n = prop.name;
                returnValue = propValue.Serialise();
            }
            else if (prop.value is Quaternion)
            {
                var propValue = (BanterVector4)(Quaternion)prop.value;
                propValue.n = prop.name;
                returnValue = propValue.Serialise();
            }
            if (returnValue == null)
            {
                //todo: is this case needed/intended?
                return $"{comp.banterObject.oid}|{comp.cid}|{(int)comp.type}|";

            }
            else
            {
                return $"{comp.banterObject.oid}|{comp.cid}|{(int)comp.type}|{returnValue}";
            }
        }
        #endregion

        #region Utilities and startup methods
        public void FixedUpdate()
        {
            _tickBuffer = new List<BanterComponentPropertyUpdate>();
            Tick?.Invoke(null, null);
            EnqueuePropertyUpdates(_tickBuffer);
            _tickBuffer = null;
            // if(Time.frameCount % 60 == 0) {
            //     LogLine.Do("_changes: " + _changes.Count() + ", _propertyUpdateQueue: " + _propertyUpdateQueue.Count() + ", banterComponents: " + banterComponents.Count() + ", objects: " + objects.Count());
            // }
        }

        public void SetSettings(string msg, int reqId)
        {
            
            var settingsParts = msg.Split(MessageDelimiters.PRIMARY);
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 foreach (var part in settingsParts)
                 {
                     var setting = part.Split(MessageDelimiters.SECONDARY);
                     if (!settings.IsSettingsLocked)
                     {
                         switch (setting[0])
                         {
                             case SettingsMap.EnableDevTools:
                                 settings.EnableDevTools = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableTeleport:
                                 settings.EnableTeleport = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableForceGrab:
                                 settings.EnableForceGrab = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableSpiderMan:
                                 settings.EnableSpiderMan = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableHandHold:
                                 settings.EnableHandHold = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableRadar:
                                 settings.EnableRadar = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableNametags:
                                 settings.EnableNametags = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableDefaultTextures:
                                 settings.EnableDefaultTextures = setting[1] == "1";
                                 break;
                             case SettingsMap.EnablePortals:
                                 settings.EnablePortals = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableGuests:
                                 settings.EnableGuests = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableFriendPositionJoin:
                                 settings.EnableFriendPositionJoin = setting[1] == "1";
                                 break;
                             case SettingsMap.EnableAvatars:
                                 settings.EnableAvatars = setting[1] == "1";
                                 break;
                             case SettingsMap.MaxOccupancy:
                                 settings.MaxOccupancy = int.Parse(setting[1]);
                                 break;
                             case SettingsMap.RefreshRate:
                                 settings.RefreshRate = int.Parse(setting[1]);
                                 break;
                             case SettingsMap.ClippingPlane:
                                 var clippingParts = setting[1].Split(MessageDelimiters.TERTIARY);
                                 settings.ClippingPlane = new Vector2(NumberFormat.Parse(clippingParts[1]),
                                     NumberFormat.Parse(clippingParts[2]));
                                 break;
                             case SettingsMap.SpawnPoint:
                                 var spawnParts = setting[1].Split(MessageDelimiters.TERTIARY);
                                 settings.SpawnPoint = new Vector4(NumberFormat.Parse(spawnParts[1]),
                                     NumberFormat.Parse(spawnParts[2]), NumberFormat.Parse(spawnParts[3]),
                                     NumberFormat.Parse(spawnParts[4]));
                                 break;
                             case SettingsMap.SettingsLocked:
                                 settings.IsSettingsLocked = setting[1] == "1";
                                 break;
                         }
                     }

                     if (!settings.IsPhysicsSettingsLocked)
                     {
                         switch (setting[0])
                         {
                             case SettingsMap.PhysicsMoveSpeed:
                                 settings.PhysicsMoveSpeed = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsMoveAcceleration:
                                 settings.PhysicsMoveAcceleration = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsAirControlSpeed:
                                 settings.PhysicsAirControlSpeed = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsAirControlAcceleration:
                                 settings.PhysicsAirControlAcceleration = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsDrag:
                                 settings.PhysicsDrag = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsFreeFallAngularDrag:
                                 settings.PhysicsFreeFallAngularDrag = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsJumpStrength:
                                 settings.PhysicsJumpStrength = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsHandPositionStrength:
                                 settings.PhysicsHandPositionStrength = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsHandRotationStrength:
                                 settings.PhysicsHandRotationStrength = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsHandSpringiness:
                                 settings.PhysicsHandSpringiness = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsGrappleRange:
                                 settings.PhysicsGrappleRange = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsGrappleReelSpeed:
                                 settings.PhysicsGrappleReelSpeed = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsGrappleSpringiness:
                                 settings.PhysicsGrappleSpringiness = float.Parse(setting[1]);
                                 break;
                             case SettingsMap.PhysicsGorillaMode:
                                 settings.PhysicsGorillaMode = setting[1] == "1";
                                 break;
                             case SettingsMap.PhysicsSettingsLocked:
                                 settings.IsPhysicsSettingsLocked = setting[1] == "1";
                                 break;
                         }
                     }
                 }
             });
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.SCENE_SETTINGS);
        }
        public void FlushObjectToChanges(int oid, int cid = 0, ComponentType ct = 0)
        {
            // An empty update to trigger this object to be sent to JS

            EnqueueChange($"{oid}|{cid}|{(int)ct}|{(int)PropertyName.hasUnity}~~{(int)PropertyType.Bool}~~1");

        }
        public void KillAllKitItemsBeforeLoad()
        {
            foreach (var item in kitItems)
            {
                GameObject.Destroy(item);
            }
            kitItems.Clear();
        }
        void ClearQueueOnStartup()
        {
            //TODO: evaluate/remove
            //while (_propertyUpdateBatchQueue.TryTake(out var tmp)) { }
            //lock (PropertyUpdateThread){
            //    _propertyUpdateQueue.Clear();
            //}
        }
        public static BanterScene Instance()
        {
            if (_instance == null)
            {
                _instance = new BanterScene();
            }
            return _instance;
        }


        #endregion


        #region Legacy stuff
        public void LegacySetAttachment(string msg)
        {
            var parts = msg.Split(MessageDelimiters.PRIMARY, 3);
            var oid = int.Parse(parts[0]);
            var gameObject = GetObject(oid);
            var whoToShow = parts[1];
            var part = (LegacyAttachmentPosition)int.Parse(parts[2]);
            var actualPart = AvatarBoneName.HEAD;
            switch (part)
            {
                case LegacyAttachmentPosition.HEAD:
                    actualPart = AvatarBoneName.HEAD;
                    break;
                case LegacyAttachmentPosition.LEFT_HAND:
                    actualPart = AvatarBoneName.LEFTARM_HAND;
                    break;
                case LegacyAttachmentPosition.RIGHT_HAND:
                    actualPart = AvatarBoneName.RIGHTARM_HAND;
                    break;
                case LegacyAttachmentPosition.BODY:
                    actualPart = AvatarBoneName.HIPS;
                    break;
            }
            var attachment = new BanterAttachment();
            attachment.uid = whoToShow;
            attachment.attachmentPosition = gameObject.gameObject.transform.localPosition;
            attachment.attachmentRotation = gameObject.gameObject.transform.localRotation;
            attachment.attachmentType = AttachmentType.NonPhysics;
            attachment.avatarAttachmentPoint = actualPart;
            attachment.attachedObject = gameObject;
            data.AttachObject(attachment);
        }
        public void LegacySetChildColor(string msg)
        {
            var msgParts = msg.Split(MessageDelimiters.PRIMARY);
            if (msgParts.Length < 2)
            {
                Debug.LogError("[Banter] LegacySetChildColor message is malformed: " + msg);
                return;
            }
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 var obj = GetGameObject(int.Parse(msgParts[0]));
                 var color = new Color(NumberFormat.Parse(msgParts[1]), NumberFormat.Parse(msgParts[2]), NumberFormat.Parse(msgParts[3]));
                 var path = msgParts[4];
                 if (obj != null)
                 {
                     try
                     {
                         // LogLine.Do("Setting child color: " + path + " - " + color);
                         obj.transform.Find(path)
                             .GetComponent<MeshRenderer>().material
                             .SetColor("_Color", color);
                     }
                     catch (Exception e)
                     {
                         Debug.LogError("[Banter] Error setting child color: " + path + " - " + e.Message);
                     }
                 }
             });
        }
        public void LegacySetVideoUrl(GameObject gameObject, string url, string id)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
             {
                 var video = gameObject.GetComponentInChildren<VideoPlayer>();
                 if (video != null)
                 {
                     video.time = 0;
                     video.url = url;
                     video.Play();
                     VideoPlayer.EventHandler callback = null;
                     callback = (VideoPlayer vp) =>
                     {
                         events.OnVideoPrepareCompleted.Invoke(id);
                         video.prepareCompleted -= callback;
                     };
                     video.prepareCompleted += callback;
                 }
             });
        }
        #endregion

    }
}
