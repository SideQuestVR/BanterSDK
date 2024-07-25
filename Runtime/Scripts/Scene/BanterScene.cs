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
        // SpaceState spaceState = new SpaceState();
        public bool loaded = false;
        public bool bundlesLoaded = false;
        public bool loading = false;
        public bool isHome = false;
        public bool isFallbackHome = false;
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
        public UnityMainThreadDispatcher mainThread;
        public string CurrentUrl;
        public bool ClearCacheOnRejoin;
        public Guid InstanceID { get; private set; }
        // AutoResetEvent areChangeEnqueued = new AutoResetEvent(false);
        // AutoResetEvent arePropertyUpdateQueue = new AutoResetEvent(false);
        public string LoadingStatus = "Please wait, loading live space...";
        public void EnqueueChange(string change)
        {
            //this is assuming link.send is always batched in some form and won't block for any significant amount of time
            link.Send(APICommands.UPDATE + change);

            //string msg =          msg = string.Join(MessageDelimiters.TERTIARY, _changes.ToArray());
            //    _changes.Clear();
            //}
            //if (!string.IsNullOrEmpty(msg))
            //{
            //    link.Send(APICommands.UPDATE + msg);
            //}

            //lock (_changes) {
            //    _changes.Add(change);
            //}dd
            //areChangeEnqueued.Set();
        }


        //only to be accessed in main thread!
        private Task activeTask = null;
        private List<BanterComponentPropertyUpdate> _pendingQueue;// = new List<BanterComponentPropertyUpdate>();


        int pendingQLocked = 0;
        int activeTaskLocked = 0;
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
            ClearQueueOnStartup();
        }

        public void Destroy()
        {
            StopThreads();
            _instance = null;
        }

        #region Player Events
        public void Release(GameObject obj, HandSide side = HandSide.LEFT)
        {
            var interaction = obj.GetComponent<PlayerEvents>();
            if (interaction != null)
            {
                interaction.onRelease.Invoke(side);
            }
            link.OnRelease(obj, side);
        }
        public void Grab(GameObject obj, Vector3 point, HandSide side = HandSide.LEFT)
        {
            var interaction = obj.GetComponent<PlayerEvents>();
            if (interaction != null)
            {
                interaction.onGrab.Invoke(point, side);
            }
            link.OnGrab(obj, point, side);
        }
        public void Click(GameObject obj, Vector3 point, Vector3 normal)
        {
            var interaction = obj.GetComponent<PlayerEvents>();
            if (interaction != null)
            {
                interaction.onClick.Invoke(point, normal);
            }
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
            mainThread.Enqueue(() =>
            {
                EventBus.Trigger("OnUserJoined", new CustomEventArgs("OnUserJoined", new object[] { user.name, user.id, user.uid, user.color, user.isLocal }));
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
            mainThread.Enqueue(() =>
            {
                EventBus.Trigger("OnUserLeft", new CustomEventArgs("OnUserLeft", new object[] { user.name, user.id, user.uid, user.color, user.isLocal }));
            });
#endif
        }
        public void OpenPage(string msg, int reqId)
        {
            mainThread?.Enqueue(() => events.OnPageOpened.Invoke(msg));
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.OPEN_PAGE);
        }
        public void StartTTS(string msg, int reqId)
        {
            mainThread?.Enqueue(() => events.OnTTsStarted.Invoke(msg == "1"));
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.START_TTS);
        }
        public void StopTTS(string msg, int reqId)
        {
            mainThread?.Enqueue(() => events.OnTTsStoped.Invoke(msg));
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.STOP_TTS);
        }
        public void Gravity(string msg, int reqId)
        {
            var parts = msg.Split(MessageDelimiters.PRIMARY);
            if (parts.Length < 3)
            {
                Debug.LogError("[Banter] Gravity message is malformed: " + msg);
                return;
            }
            var gravity = new Vector3(Germany.DeGermaniser(parts[0]), Germany.DeGermaniser(parts[1]), Germany.DeGermaniser(parts[2]));
            mainThread?.Enqueue(() => Physics.gravity = gravity);
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.GRAVITY);
        }

        public void TimeScale(string msg, int reqId)
        {
            var timeScale = Germany.DeGermaniser(msg);
            mainThread?.Enqueue(() => Time.timeScale = timeScale);
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.TIME_SCALE);
        }
        public void PlayerSpeed(string msg, int reqId)
        {
            var speed = msg == "1";
            events.OnPlayerSpeedChanged.Invoke(speed);
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.PLAYER_SPEED);
        }

        public void Teleport(string msg, int reqId)
        {
            var parts = msg.Split(MessageDelimiters.PRIMARY);
            var point = new Vector3(Germany.DeGermaniser(parts[0]), Germany.DeGermaniser(parts[1]), Germany.DeGermaniser(parts[2]));
            var rotation = new Vector3(0, Germany.DeGermaniser(parts[3]), 0);
            var stopVelocity = parts[4] == "1";
            var isSpawn = parts[5] == "1";
            if (parts.Length < 5)
            {
                Debug.LogError("[Banter] Teleport message is malformed: " + msg);
                return;
            }
#if BANTER_EDITOR
            mainThread?.Enqueue(() => {
                events.OnTeleport.Invoke(point, rotation, stopVelocity, isSpawn);
            });
#else
            var user = users.FirstOrDefault(x => x.id == parts[5]);
            if (user != null)
            {
                mainThread?.Enqueue(() =>
                {
                    user.transform.position = point;
                    user.transform.eulerAngles = rotation;
                });
            }
#endif
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.TELEPORT);
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
                // Debug.Log(data);
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
        // public void Detach(string data, int reqId) {
        //     var parts = data.Split(MessageDelimiters.PRIMARY);
        //     if(parts.Length < 2) {
        //         Debug.LogError("[Banter] Detach message is malformed: " + data);
        //         return;
        //     }
        //     var obj = GetObject(int.Parse(parts[0]));
        //     if(obj.gameObject != null && obj.banterObject  != null) {
        //         mainThread?.Enqueue(() => {
        //             if(obj.banterObject.previousParent != null) {
        //                 obj.gameObject.transform.SetParent(obj.banterObject.previousParent, false);
        //                 obj.banterObject.previousParent = null;
        //             }
        //         });
        //     }
        //     link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.DETACH);
        // }
        // public void Attach(string data, int reqId) {
        //     var parts = data.Split(MessageDelimiters.PRIMARY);
        //     if(parts.Length < 3) {
        //         Debug.LogError("[Banter] Attach message is malformed: " + data);
        //         return;
        //     }
        //     var user = users.FirstOrDefault(x => x.id == parts[0]);
        //     if(user != null) {
        //         var obj = GetObject(int.Parse(parts[1]));
        //         if(obj.gameObject != null && obj.banterObject  != null) {
        //             var attachmentType = (AttachmentType)int.Parse(parts[2]);
        //             mainThread?.Enqueue(() => {
        //                 user.Attach(obj, attachmentType);
        //             });
        //         }
        //     }
        //     link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.ATTACH);
        // }
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
                mainThread?.Enqueue(() =>
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
            mainThread?.Enqueue(() =>
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
                        paramList.Add(Germany.DeGermaniser(paramParts[1]));
                        break;
                    case PropertyType.Int:
                        paramList.Add(int.Parse(paramParts[1]));
                        break;
                    case PropertyType.Vector2:
                        paramList.Add(new Vector2(Germany.DeGermaniser(paramParts[1]), Germany.DeGermaniser(paramParts[2])));
                        break;
                    case PropertyType.Vector3:
                        paramList.Add(new Vector3(Germany.DeGermaniser(paramParts[1]), Germany.DeGermaniser(paramParts[2]), Germany.DeGermaniser(paramParts[3])));
                        break;
                    case PropertyType.Vector4:
                        paramList.Add(new Vector4(Germany.DeGermaniser(paramParts[1]), Germany.DeGermaniser(paramParts[2]), Germany.DeGermaniser(paramParts[3]), Germany.DeGermaniser(paramParts[4])));
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
            var banterObject = GetGameObject(int.Parse(msgParts[0]));
            if (banterObject != null)
            {
                mainThread?.Enqueue(() =>
                {
                    banterObject.SetActive(int.Parse(msgParts[1]) == 1);
                    SendObjectUpdate(banterObject, reqId);
                });
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
                mainThread?.Enqueue(() =>
                {
                    banterObject.layer = int.Parse(msgParts[1]);
                    SendObjectUpdate(banterObject, reqId);
                });
            }
        }
        public void PhysicsRaycast(string msg, int reqId)
        {
            var msgParts = msg.Split(MessageDelimiters.PRIMARY);
            if (msgParts.Length < 2)
            {
                Debug.LogError("[Banter] Physics Raycast message is malformed: " + msg);
                return;
            }
            var layerMask = int.Parse(msgParts[0]);
            var maxDistance = Germany.DeGermaniser(msgParts[1]);
            var position = new Vector3(Germany.DeGermaniser(msgParts[2]), Germany.DeGermaniser(msgParts[3]), Germany.DeGermaniser(msgParts[4]));
            var direction = new Vector3(Germany.DeGermaniser(msgParts[5]), Germany.DeGermaniser(msgParts[6]), Germany.DeGermaniser(msgParts[7]));
            if (Physics.Raycast(position, direction, out var hit, maxDistance, layerMask))
            {
                link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + APICommands.RAYCAST + MessageDelimiters.PRIMARY + hit.collider.gameObject.GetInstanceID() + MessageDelimiters.PRIMARY + hit.point.x + MessageDelimiters.PRIMARY + hit.point.y + MessageDelimiters.PRIMARY + hit.point.z + MessageDelimiters.PRIMARY + hit.normal.x + MessageDelimiters.PRIMARY + hit.normal.y + MessageDelimiters.PRIMARY + hit.normal.z);
            }
        }
        public void InstantiateJsObject(string msg, int reqId)
        {
            var gameObject = GetGameObject(int.Parse(msg));
            if (gameObject != null)
            {
                mainThread?.Enqueue(async () =>
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
            mainThread?.Enqueue(() => events.OnMenuBrowserMessage.Invoke(msg));
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
            mainThread?.Enqueue(() =>
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
                comp.Init();
                var updates = SetComponentProperties(3, parts, banterComp, msg);

                comp.Deserialise(updates);

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
                            var valFloat = Germany.DeGermaniser(propParts[2]);
                            updates.Add(new BanterFloat() { n = name, x = valFloat });
                            banterComp.UpdateProperty(name, valFloat);
                            break;
                        case PropertyType.Int:
                            var valInt = int.Parse(propParts[2]);
                            updates.Add(new BanterInt() { n = name, x = valInt });
                            banterComp.UpdateProperty(name, valInt);
                            break;
                        case PropertyType.Vector2:
                            var valVector2X = Germany.DeGermaniser(propParts[2]);
                            var valVector2Y = Germany.DeGermaniser(propParts[3]);
                            updates.Add(new BanterVector2() { n = name, x = valVector2X, y = valVector2Y });
                            banterComp.UpdateProperty(name, new Vector2(valVector2X, valVector2Y));
                            break;
                        case PropertyType.Vector3:
                            var valVector3X = Germany.DeGermaniser(propParts[2]);
                            var valVector3Y = Germany.DeGermaniser(propParts[3]);
                            var valVector3Z = Germany.DeGermaniser(propParts[4]);
                            updates.Add(new BanterVector3() { n = name, x = valVector3X, y = valVector3Y, z = valVector3Z });
                            banterComp.UpdateProperty(name, new Vector3(valVector3X, valVector3Y, valVector3Z));
                            break;
                        case PropertyType.Vector4:
                        case PropertyType.Quaternion:
                            var valVector4X = Germany.DeGermaniser(propParts[2]);
                            var valVector4Y = Germany.DeGermaniser(propParts[3]);
                            var valVector4Z = Germany.DeGermaniser(propParts[4]);
                            var valVector4W = Germany.DeGermaniser(propParts[5]);
                            updates.Add(new BanterVector4() { n = name, x = valVector4X, y = valVector4Y, z = valVector4Z, w = valVector4W });
                            banterComp.UpdateProperty(name, new Vector4(valVector4X, valVector4Y, valVector4Z, valVector4W));
                            break;
                    }

                }
                catch (Exception e)
                {
                    Debug.LogError("[Banter] Error setting property: " + e.Message + ": " + msg);
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
                mainThread?.Enqueue(() =>
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
            mainThread?.Enqueue(() => SendObjectUpdate(int.Parse(msg), reqId));
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
            mainThread?.Enqueue(() =>
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
                mainThread?.Enqueue(() =>
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
            mainThread?.Enqueue(async () =>
            {
                try
                {
                    var go = new GameObject(parts[2]);
                    go.transform.parent = settings.parentTransform;
                    go.AddComponent<BanterObjectId>();
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
            loaded = true;
            loading = false;
            externalLoadFailed = true;
            loadUrlTaskCompletionSource?.TrySetException(new Exception((isUserCancel ? "Cancelled: " : "Load failed: ") + message));
            loadUrlTaskCompletionSource?.TrySetCanceled();
            loadingManager?.SetLoadProgress(isUserCancel ? "Loading Cancelled" : "Loading failed", 0, message, true);
            LogLine.Do(isUserCancel ? "Loading Cancelled" : "Loading failed");
            loadingManager?.UpdateCancelText();
            // if(!isHome) {
            // loadUrlTaskCompletionSource?.TrySetException(new Exception((isUserCancel? "Loading cancelled: " : "The URL failed to load: ") + message));
            // // _ = loadingManager.LoadOut();
            // loadingManager?.SetLoadProgress("Failed to load", 0, message, true);
            // loadingManager?.CancelPressed();
            // mainThread?.Enqueue(() => {
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
            mainThread?.Enqueue(() =>
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
                mainThread?.Enqueue(() =>
                {
                    events.OnSceneReset.Invoke();
                });
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
            if (UnityMainThreadDispatcher.Exists())
            {
                mainThread?.Enqueue(async () =>
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
                    try
                    {
                        var space = await Get.SpaceMeta(url);
                        if (space != null && !string.IsNullOrEmpty(space.icon))
                        {
                            loadingTexture = await Get.Texture(space.icon + "?size=2048");
                        }
                    }
                    catch (Exception)
                    {

                    }
                    await ResetScene();
                    await link.LoadUrl(url);
                    await new WaitUntil(() => loaded);
                    LoadingStatus = "Please wait, loading live space...";
                    if (HasLoadFailed())
                    {
                        loading = false;
                        return;
                    }
                    // link.OnUnitySceneLoaded();
                    // state = SceneState.UNITY_READY;
                    loadUrlTaskCompletionSource.SetResult(true);
                    mainThread?.Enqueue(() =>
                    {
                        events.OnUnitySceneLoad.Invoke(url);
                    });
                    await Task.Delay(500);
                    await loadingManager?.LoadOut();
                    loading = false;
                });
                await loadUrlTaskCompletionSource.Task;
            }
        }
        public async Task OnLoad(string instanceId)
        {
            // This is fired when the page loads or realods from the browser end, so it can be fired becuase 
            // we have explicitly naviagted the page, but also becuase someone hut f5 in the debugger. 
            state = SceneState.NONE;
            mainThread?.Enqueue(() =>
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
        public void StartThreads()
        {
            LogLine.Do("Starting Banter thread.");
            // propertyUpdateThread = new Thread(PropertyUpdateThread);
            // propertyUpdateThread.Start();
            //propertySyncThread = new Thread(PropertySyncThread);
            //propertySyncThread.Start();
        }
        public void StopThreads()
        {
            LogLine.Do("Stopping Banter threads.");
            //
            //  if(propertyUpdateThread != null) {
            //     propertyUpdateThread.Abort();
            //      propertyUpdateThread = null;
            //  }
            //if(propertySyncThread != null) {
            //    propertySyncThread.Abort();
            //    propertySyncThread = null;
            //}
        }

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
                //Debug.Log("buffered enqueue on SetFromUnityProperties!");
                int ct = properties.Count;
                for (int i = 0; i < ct; i++)
                {
                    _tickBuffer.Add(properties[i]);
                }
            }
            else
            {
                //Debug.LogWarning($"NON-buffered enqueue on SetFromUnityProperties, only {properties.Count}!");
                EnqueuePropertyUpdates(properties);
            }
        }
        //public void PropertySyncThread()
        //{
        //    while (true)
        //    {
        //        if (dirty && _changes.Count > 0 && link != null)
        //        {
        //            string msg = "";
        //            lock (_changes)
        //            {
        //                msg = string.Join(MessageDelimiters.TERTIARY, _changes.ToArray());
        //                _changes.Clear();
        //            }
        //            if (!string.IsNullOrEmpty(msg))
        //            {
        //                link.Send(APICommands.UPDATE + msg);
        //            }
        //            dirty = false;
        //        }
        //    }
        //}


        private void DoPropertyUpdateQueue(List<BanterComponentPropertyUpdate> toProcess)
        {
            //List<BanterComponentPropertyUpdate> toProcess = _propertyUpdateBatchQueue.Take();
            //lock (_propertyUpdateQueue)
            //{
            //    for (int i = 0; i < _propertyUpdateQueue.Count; i++)
            //    {
            //        toProcess.Add(_propertyUpdateQueue[i]);
            //    }
            //    _propertyUpdateQueue.Clear();
            //}
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
                mainThread?.Enqueue(() =>
                {
                    Debug.LogWarning($"Having to reenqueue {toReenqueue.Count}");
                    EnqueuePropertyUpdates(toReenqueue);
                });
            }
        }

        //public void PropertyUpdateThread()
        //{
        //    while (true)
        //    {
        //        try
        //        {
        //            DoPropertyUpdateQueue();
        //        }
        //        catch (ThreadAbortException)
        //        {
        //            Debug.Log("PropertyUpdateThread aborting");
        //            return;
        //        }
        //        catch (Exception ex)
        //        {
        //            Debug.LogError("Error handling property update queue");
        //            Debug.LogException(ex);
        //        }
        //    }
        //}

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
            mainThread?.Enqueue(() =>
            {
                foreach (var part in settingsParts)
                {
                    var setting = part.Split(MessageDelimiters.SECONDARY);
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
                            settings.ClippingPlane = new Vector2(Germany.DeGermaniser(clippingParts[1]), Germany.DeGermaniser(clippingParts[2]));
                            break;
                        case SettingsMap.SpawnPoint:
                            var spawnParts = setting[1].Split(MessageDelimiters.TERTIARY);
                            settings.SpawnPoint = new Vector4(Germany.DeGermaniser(spawnParts[1]), Germany.DeGermaniser(spawnParts[2]), Germany.DeGermaniser(spawnParts[3]), Germany.DeGermaniser(spawnParts[4]));
                            break;
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
            var msgParts = msg.Split(MessageDelimiters.PRIMARY);
            if (msgParts.Length < 2)
            {
                Debug.LogError("[Banter] LegacySetAttachment message is malformed: " + msg);
                return;
            }
            mainThread?.Enqueue(() =>
            {
                var obj = GetGameObject(int.Parse(msgParts[0]));
                var whoToShow = msgParts[1];
                var part = msgParts[2];
                if (obj != null)
                {

                }
            });
        }
        public void LegacySetChildColor(string msg)
        {
            var msgParts = msg.Split(MessageDelimiters.PRIMARY);
            if (msgParts.Length < 2)
            {
                Debug.LogError("[Banter] LegacySetChildColor message is malformed: " + msg);
                return;
            }
            mainThread?.Enqueue(() =>
            {
                var obj = GetGameObject(int.Parse(msgParts[0]));
                var color = new Color(Germany.DeGermaniser(msgParts[1]), Germany.DeGermaniser(msgParts[2]), Germany.DeGermaniser(msgParts[3]));
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
            mainThread?.Enqueue(() =>
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