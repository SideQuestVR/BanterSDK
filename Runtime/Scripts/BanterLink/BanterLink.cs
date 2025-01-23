using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using System.Collections;



#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif
using Banter.Utilities.Async;

namespace Banter.SDK
{
    public class BanterLink : MonoBehaviour
    {
        public BanterPipe pipe;
        public static string pipeName => GeneratePipeName();
        public BanterScene scene;
        public event EventHandler Connected;
        float timeoutDisplay = 0;

        bool _IsVsLoaded;
        public bool IsVsLoaded
        {
            get => _IsVsLoaded;
            set
            {
                if(_IsVsLoaded && vsLoaded != null) {
                    StopCoroutine(vsLoaded);
                }
                _IsVsLoaded = value;
            }
        }
        IEnumerator vsLoaded;
        BatchUpdater batchUpdater;
        // public static string LOCAL_USER_ID;

        void Start()
        {
            scene = BanterScene.Instance();
            SetupPipe();

            scene.events.OnJsCallbackRecieved.AddListener((id, data, isReturn) =>
            {
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
#if BANTER_VISUAL_SCRIPTING
                    EventBus.Trigger("OnJsReturnValue", new CustomEventArgs(id, new object[] { data }));
#endif
                });
            });
        }

        string GetMsgData(string msg, string command)
        {
            return msg.Substring((command + MessageDelimiters.PRIMARY).Length);
        }

        public void StartVSLoadedTimeout() {
            vsLoaded = VisualScriptingReadyTimout();
            StartCoroutine(vsLoaded);
        }
        
        IEnumerator VisualScriptingReadyTimout() {
            yield return new WaitForSeconds(5);
            _IsVsLoaded = true;
        }
        async void ParseCommand(string msg)
        {
            if (msg.StartsWith(APICommands.ONLOAD))
            {
                _ = scene.OnLoad(GetMsgData(msg, APICommands.ONLOAD));
                scene.SetLoaded();
            }
            else if (msg.StartsWith(APICommands.NOTHING_20S))
            {
                scene.state = SceneState.NOTHING_20S;
                LogLine.Do(LogLine.banterColor, LogTag.Banter, "No objects yet after 30 seconds...");
                UnityMainThreadTaskScheduler.Default.Enqueue(() => timeoutDisplay = Time.time);
                scene.loadingManager?.SetLoadProgress("Still Loading... 😅😬", 0, "No objects loaded yet after 20 seconds...", true);
            }
            else if (msg.StartsWith(APICommands.NOTHING))
            {
                scene.state = SceneState.LOAD_FAILED;
                scene.Cancel("No objects yet after 4:20 seconds, failing...");
            }
            else if (msg.StartsWith(APICommands.LOAD_FAILED))
            {
                scene.state = SceneState.LOAD_FAILED;
                scene.Cancel("The web page failed to load!");
            }
            else if (msg.StartsWith(APICommands.ENABLE_LEGACY))
            {
                scene.events.OnLegacyEnabled.Invoke();
                LogLine.Do(LogLine.banterColor, LogTag.Banter, "Running in legacy AFRAME mode...");
            }
            else if (msg.StartsWith(APICommands.SET_PROTECTED_SPACE_PROPS))
            {
                scene.SetProps(APICommands.SET_PROTECTED_SPACE_PROPS, GetMsgData(msg, APICommands.SET_PROTECTED_SPACE_PROPS));
            }
            else if (msg.StartsWith(APICommands.SET_PUBLIC_SPACE_PROPS))
            {
                scene.SetProps(APICommands.SET_PUBLIC_SPACE_PROPS, GetMsgData(msg, APICommands.SET_PUBLIC_SPACE_PROPS));
            }
            else if (msg.StartsWith(APICommands.SET_USER_PROPS))
            {
                var restOfMessage = GetMsgData(msg, APICommands.SET_USER_PROPS);
                var parts = restOfMessage.Split(MessageDelimiters.PRIMARY);
                if (parts.Length == 2)
                {
                    scene.SetProps(APICommands.SET_USER_PROPS, parts[0], parts[1]);
                }
                else
                {
                    scene.SetProps(APICommands.SET_USER_PROPS, restOfMessage);
                }
            }
            else if (msg.StartsWith(APICommands.SCENE_START))
            {
                scene.state = SceneState.SCENE_START;
            }
            else if (msg.StartsWith(APICommands.DOM_READY))
            {
                scene.state = SceneState.DOM_READY;
                scene.events.OnDomReady.Invoke();
                scene.SetLoaded();
            }
            else if (msg.StartsWith(APICommands.SCENE_READY))
            {
                scene.state = SceneState.SCENE_READY;
                scene.events.OnSceneReady.Invoke();
                LogLine.Do(LogLine.banterColor, LogTag.Banter, "Banter Scene Loaded.");
                await new WaitUntil(() =>
                {
                    scene.SetLoaded();
                    return scene.loaded;
                });
                OnUnitySceneLoaded();
                await Task.Delay(25000);
                if (scene.state != SceneState.UNITY_READY)
                {
                    scene.LogMissing();
                }
            }
            else if (msg.StartsWith(APICommands.INJECT_JS_CALLBACK))
            {
                var data = GetMsgData(msg, APICommands.INJECT_JS_CALLBACK).Split(MessageDelimiters.SECONDARY);
                scene.events.OnJsCallbackRecieved.Invoke(data[0], data[1], true);
            }
            else
            {
                LogLine.Do(Color.red, LogTag.Banter, "Unknown parse message: " + msg);
            }
        }
        void ParseRequest(string msg, int id, string full)
        {
            if (!scene.isReady)
            {
                return;
            }
            // try{
            if (msg.StartsWith(APICommands.OBJECT_ADDED))
            {
                scene.AddJsObject(GetMsgData(msg, APICommands.OBJECT_ADDED), id);
            }
            else if (msg.StartsWith(APICommands.INJECT_JS_CALLBACK))
            {
                var data = GetMsgData(msg, APICommands.INJECT_JS_CALLBACK).Split(MessageDelimiters.SECONDARY);
                scene.events.OnJsCallbackRecieved.Invoke(data[0], data[1], false);
            }
            else if (msg.StartsWith(APICommands.SCENE_SETTINGS))
            {
                scene.SetSettings(GetMsgData(msg, APICommands.SCENE_SETTINGS), id);
            }
            else if (msg.StartsWith(APICommands.START_TTS))
            {
                scene.StartTTS(GetMsgData(msg, APICommands.STOP_TTS), id);
            }
            else if (msg.StartsWith(APICommands.WAIT_FOR_END_OF_FRAME))
            {
                _ = scene.WaitForEndOfFrame(id);
            }
            else if (msg.StartsWith(APICommands.STOP_TTS))
            {
                scene.StopTTS(GetMsgData(msg, APICommands.STOP_TTS), id);
            }
            else if (msg.StartsWith(APICommands.AI_IMAGE))
            {
                scene.AiImage(GetMsgData(msg, APICommands.AI_IMAGE), id);
            }
            else if (msg.StartsWith(APICommands.AI_MODEL))
            {
                scene.AiModel(GetMsgData(msg, APICommands.AI_MODEL), id);
            }
            else if (msg.StartsWith(APICommands.BASE_64_TO_CDN))
            {
                scene.Base64ToCDN(GetMsgData(msg, APICommands.BASE_64_TO_CDN), id);
            }
            else if (msg.StartsWith(APICommands.SELECT_FILE))
            {
                scene.SelectFile(GetMsgData(msg, APICommands.SELECT_FILE), id);
            }
            else if (msg.StartsWith(APICommands.GRAVITY))
            {
                scene.Gravity(GetMsgData(msg, APICommands.GRAVITY), id);
            }
            else if (msg.StartsWith(APICommands.TIME_SCALE))
            {
                scene.TimeScale(GetMsgData(msg, APICommands.TIME_SCALE), id);
            }
            else if (msg.StartsWith(APICommands.PLAYER_SPEED))
            {
                scene.PlayerSpeed(GetMsgData(msg, APICommands.PLAYER_SPEED), id);
            }
            else if (msg.StartsWith(APICommands.DEEP_LINK))
            {
                var parts = GetMsgData(msg, APICommands.DEEP_LINK).Split(MessageDelimiters.PRIMARY, 2);
                Debug.Log(parts[0] + " : " + parts[1]);
                scene.events.OnDeepLink.Invoke(parts[0], parts[1]);
            }
            else if (msg.StartsWith(APICommands.ONE_SHOT))
            {
                var parts = GetMsgData(msg, APICommands.ONE_SHOT).Split(MessageDelimiters.PRIMARY, 2);
                scene.events.OnOneShot.Invoke(parts[1], parts[0] == "1");
            }
            else if (msg.StartsWith(APICommands.YT_INFO))
            {
                scene.YtInfo(GetMsgData(msg, APICommands.YT_INFO), id);
            }
            else if (msg.StartsWith(APICommands.OPEN_PAGE))
            {
                scene.OpenPage(GetMsgData(msg, APICommands.OPEN_PAGE), id);
            }
            else if (msg.StartsWith(APICommands.LOCK_SPIDERMAN))
            {
                scene.LockThing(id, scene.events.OnLockSpiderman, APICommands.LOCK_SPIDERMAN);
            }
            else if (msg.StartsWith(APICommands.LOCK_TELEPORT))
            {
                scene.LockThing(id, scene.events.OnLockTeleport, APICommands.LOCK_TELEPORT);
            }
            else if (msg.StartsWith(APICommands.TELEPORT))
            {
                scene.Teleport(GetMsgData(msg, APICommands.TELEPORT), id);
            }
            else if (msg.StartsWith(APICommands.CALL_METHOD))
            {
                scene.CallMethodOnJsComponent(GetMsgData(msg, APICommands.CALL_METHOD), id, full);
            }
            else if (msg.StartsWith(APICommands.INSTANTIATE))
            {
                scene.InstantiateJsObject(GetMsgData(msg, APICommands.INSTANTIATE), id);
            }
            else if (msg.StartsWith(APICommands.SET_LAYER))
            {
                scene.SetJsObjectLayer(GetMsgData(msg, APICommands.SET_LAYER), id);
            }
            else if (msg.StartsWith(APICommands.SET_ACTIVE))
            {
                scene.SetJsObjectActive(GetMsgData(msg, APICommands.SET_ACTIVE), id);
            }
            else if (msg.StartsWith(APICommands.SEND_MENU_BROWSER_MESSAGE))
            {
                scene.SendBrowserMessage(GetMsgData(msg, APICommands.SEND_MENU_BROWSER_MESSAGE), id);
            }
            else if (msg.StartsWith(APICommands.RAYCAST))
            {
                scene.PhysicsRaycast(GetMsgData(msg, APICommands.RAYCAST), id);
            }
            else if (msg.StartsWith(APICommands.OBJECT_UPDATE_REQUEST))
            {
                scene.UpdateJsObject(GetMsgData(msg, APICommands.OBJECT_UPDATE_REQUEST), id);
            }
            else if (msg.StartsWith(APICommands.SET_PARENT))
            {
                scene.SetParent(GetMsgData(msg, APICommands.SET_PARENT), id);
            }
            else if (msg.StartsWith(APICommands.COMPONENT_REMOVED))
            {
                scene.DestroyJsComponent(int.Parse(GetMsgData(msg, APICommands.COMPONENT_REMOVED)), id);
            }
            else if (msg.StartsWith(APICommands.WATCH_PROPERTIES))
            {
                _ = scene.WatchProperties(GetMsgData(msg, APICommands.WATCH_PROPERTIES), id);
            }
            else if (msg.StartsWith(APICommands.OBJECT_REMOVED))
            {
                scene.DestroyJsObject(int.Parse(GetMsgData(msg, APICommands.OBJECT_REMOVED)), id);
            }
            else if (msg.StartsWith(APICommands.COMPONENT_UPDATED))
            {
                scene.UpdateJsComponent(GetMsgData(msg, APICommands.COMPONENT_UPDATED), id);
                // }else if(msg.StartsWith(APICommands.ATTACH)) {
                //     scene.Attach(GetMsgData(msg, APICommands.ATTACH), id);
            }
            else if (msg.StartsWith(APICommands.COMPONENT_ADDED))
            {
                scene.AddJsComponent(GetMsgData(msg, APICommands.COMPONENT_ADDED), id);
            }
            else if (msg.StartsWith(APICommands.QUERY_COMPONENTS))
            {
                _ = scene.QueryComponents(GetMsgData(msg, APICommands.QUERY_COMPONENTS), id);
            }
            else
            {
                Debug.Log("[Banter] Unknown parse request message: " + msg + " id: " + id);
            }
            // }catch(Exception e){
            //     Debug.Log("[Banter] Error parsing request: " + e.Message);
            // }
        }
        void ParseLegacy(string msg)
        {
            // scene.events.OnLegacySendToAframe.Invoke(GetMsgData(msg, APICommands.LEGACY_SEND_TO_AFRAME));
            // if(msg.StartsWith(APICommands.LEGACY_SEND_TO_AFRAME)) {
            // scene.events.OnLegacySendToAframe.Invoke(GetMsgData(msg, APICommands.LEGACY_SEND_TO_AFRAME));
            // }else 
            UnityMainThreadTaskScheduler.Default.Enqueue(() =>
            {
                UnityAndBanterObject lastSitObject = new UnityAndBanterObject();
                if (msg.StartsWith(APICommands.LEGACY_LOCK_PLAYER))
                {
                    scene.events.OnLegacyPlayerLockChanged.Invoke(true);
                }
                else if (msg.StartsWith(APICommands.LEGACY_UNLOCK_PLAYER))
                {
                    scene.events.OnLegacyPlayerLockChanged.Invoke(false);
                }
                else if (msg.StartsWith(APICommands.LEGACY_SIT_PLAYER))
                {
                    lastSitObject = scene.GetObject(int.Parse(GetMsgData(msg, APICommands.LEGACY_SIT_PLAYER)));
                    scene.events.OnLegacyPlayerSitChanged.Invoke(true, lastSitObject);
                }
                else if (msg.StartsWith(APICommands.LEGACY_UNSIT_PLAYER))
                {
                    if (lastSitObject.id)
                    {
                        scene.events.OnLegacyPlayerSitChanged.Invoke(false, lastSitObject);
                    }
                }
                else if (msg.StartsWith(APICommands.LEGACY_ATTACH_OBJECT))
                {
                    scene.LegacySetAttachment(GetMsgData(msg, APICommands.LEGACY_ATTACH_OBJECT));
                }
                else if (msg.StartsWith(APICommands.LEGACY_RESET_NETWORK_OBJECT))
                {
                    // var syncObject = scene.GetObject(int.Parse(GetMsgData(msg, APICommands.LEGACY_RESET_NETWORK_OBJECT)));
                    // FLEXTODO - Synced object
                    // scene.events.OnResetNetworkObject.Invoke(GetMsgData(msg, APICommands.LEGACY_RESET_NETWORK_OBJECT));
                }
                else if (msg.StartsWith(APICommands.LEGACY_DO_I_OWN))
                {
                    // FLEXTODO - Synced object
                    // scene.events.OnDoIOwn.Invoke(GetMsgData(msg, APICommands.LEGACY_DO_I_OWN));
                }
                else if (msg.StartsWith(APICommands.LEGACY_REQUEST_OWNERSHIP))
                {
                    // FLEXTODO - Synced object
                    // scene.events.OnRequestOwnership.Invoke(GetMsgData(msg, APICommands.LEGACY_REQUEST_OWNERSHIP));
                }
                else if (msg.StartsWith(APICommands.LEGACY_PLAY_AVATAR))
                {
                    scene.events.OnLegacyPlayAvatar.Invoke(GetMsgData(msg, APICommands.LEGACY_PLAY_AVATAR));
                }
                else if (msg.StartsWith(APICommands.PLAY_AVATAR))
                {
                    scene.events.OnPlayAvatar.Invoke(GetMsgData(msg, APICommands.PLAY_AVATAR));
                }
                else if (msg.StartsWith(APICommands.LEGACY_SEND_AFRAME_EVENT))
                {
                    scene.events.OnSendAframeEvent.Invoke(GetMsgData(msg, APICommands.LEGACY_SEND_AFRAME_EVENT));
                }
                else if (msg.StartsWith(APICommands.LEGACY_SET_REFRESH_RATE))
                {
                    scene.events.OnRefreshRateChanged.Invoke(float.Parse(GetMsgData(msg, APICommands.LEGACY_SET_REFRESH_RATE)));
                }
                else if (msg.StartsWith(APICommands.LEGACY_GORILLA_PLAYER))
                {
                    scene.events.OnLegacyPlayerGorillaChanged.Invoke(true);
                }
                else if (msg.StartsWith(APICommands.LEGACY_UNGORILLA_PLAYER))
                {
                    scene.events.OnLegacyPlayerGorillaChanged.Invoke(false);
                }
                else if (msg.StartsWith(APICommands.LEGACY_SET_CHILD_COLOR))
                {
                    scene.LegacySetChildColor(GetMsgData(msg, APICommands.LEGACY_SET_CHILD_COLOR));
                }
                else if (msg.StartsWith(APICommands.LEGACY_SET_VIDEO_URL))
                {
                    var parts = GetMsgData(msg, APICommands.LEGACY_SET_VIDEO_URL).Split(MessageDelimiters.PRIMARY, 2);
                    scene.LegacySetVideoUrl(scene.GetGameObject(int.Parse(parts[0])), parts[1], parts[0]);
                }
                else if (msg.StartsWith(APICommands.LEGACY_ENABLE_CONTROLLER_EXTRAS))
                {
                    scene.events.OnLegacyControllerExtrasChanged.Invoke();
                }
                else if (msg.StartsWith(APICommands.LEGACY_ENABLE_QUATERNION_POSE))
                {
                    scene.events.OnLegacyQuaternionPoseChanged.Invoke();
                }
                else
                {
                    Debug.Log("[Banter] Unknown parse legacy message: " + msg);
                }
            });
        }
        void Update()
        {
            if (scene.state == SceneState.NOTHING_20S && Time.frameCount % 10 == 0)
            {
                scene.loadingManager?.SetLoadProgress("Still Loading... 😬", 0, $"No objects loaded yet, {Mathf.Round(260f - (Time.time - timeoutDisplay))} seconds left...", true);
            }
        }
        void HandleMessage(string msg)
        {
            if (msg.StartsWith(APICommands.REQUEST_ID))
            {
                var startLength = (APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID).Length;
                var requestId = msg.Substring(startLength, msg.IndexOf(MessageDelimiters.PRIMARY, startLength) - startLength);
                ParseRequest(msg.Substring(startLength + requestId.Length + 1), int.Parse(requestId), msg);
            }
            else if (msg.StartsWith(APICommands.LEGACY))
            {
                ParseLegacy(msg.Substring(APICommands.LEGACY.Length));
            }
            else
            {
                ParseCommand(msg);
            }
        }
        void SetupPipe()
        {
            LogLine.Do("BanterLink.SetupPipe()");
#if UNITY_ANDROID && !UNITY_EDITOR
            pipe = new AndroidPipe();
#else
            pipe = new ElectronPipe(pipeName);
#endif

            batchUpdater = new BatchUpdater(pipe);
            pipe.Start(() =>
            {
                Connected?.Invoke(this, EventArgs.Empty);
            }, msg =>
            {
                if (msg.StartsWith(MessageDelimiters.PRIMARY + MessageDelimiters.SECONDARY + MessageDelimiters.TERTIARY))
                {
                    var delim = MessageDelimiters.PRIMARY + MessageDelimiters.SECONDARY + MessageDelimiters.TERTIARY;
                    var parts = msg.Substring(delim.Length).Split(delim);
                    foreach (var part in parts)
                    {
                        try
                        {
                            HandleMessage(part);
                        }
                        catch (Exception ex)
                        {
                            //todo: may result in large log messages, in future possibly have toggle flags
                            Debug.LogError($"Exception handling a message in a batch, message: {part}");
                            Debug.LogException(ex);
                        }
                    }
                }
                else
                {
                    try
                    {
                        HandleMessage(msg);
                    }
                    catch (Exception ex)
                    {
                        //todo: may result in large log messages, in future possibly have toggle flags
                        Debug.LogError($"Exception handling a message in a batch, message: {msg}");
                        Debug.LogException(ex);
                    }
                }
                // if(msg.StartsWith(APICommands.REQUEST_ID)) {
                //     var startLength = (APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID).Length;
                //     var requestId = msg.Substring(startLength, msg.IndexOf(MessageDelimiters.PRIMARY, startLength) - startLength);
                //     ParseRequest(msg.Substring(startLength + requestId.Length + 1), int.Parse(requestId), msg);
                // }else if(msg.StartsWith(APICommands.LEGACY)) {
                //     ParseLegacy(msg.Substring(APICommands.LEGACY.Length));
                // }else if(msg.StartsWith(MessageDelimiters.PRIMARY + MessageDelimiters.SECONDARY + MessageDelimiters.TERTIARY)) {
                //     var delim = MessageDelimiters.PRIMARY + MessageDelimiters.SECONDARY + MessageDelimiters.TERTIARY;
                //     var parts = msg.Substring(delim.Length).Split(delim);

                // }else{
                //     ParseCommand(msg);
                // }
            });
        }

        private static string _pipeName;
        private static string GeneratePipeName()
        {
            if (_pipeName == null)
            {
                _pipeName = Guid.NewGuid().ToString().Replace("-", "");
            }
            return $"banterPipe{_pipeName}";
        }

        void OnDestroy()
        {
            if (pipe != null)
            {
                pipe.Stop();
            }
        }

        List<string> messages = new List<string>();
        public void Send(string msg)
        {
            // #if !BANTER_EDITOR
            if (batchUpdater == null)
            {
                messages.Add(msg);
                return;
            }
            else if (messages.Count > 0)
            {
                foreach (var message in messages)
                {
                    batchUpdater.Send(message);
                }
                messages.Clear();
            }
            // #else
            batchUpdater.Send(msg);
            // #endif

        }

        public async Task LoadUrl(string url)
        {
            LogLine.Do(LogLine.banterColor, LogTag.Banter, "Loading URL: " + url);
            pipe.Send(APICommands.LOAD_URL + MessageDelimiters.PRIMARY + url);
            scene.state = SceneState.NONE;
            LogLine.Do(LogLine.banterColor, LogTag.Banter, "Before WaitUntil SCENE_READY");
            await new WaitUntil(() => scene.state >= SceneState.SCENE_READY);
            LogLine.Do(LogLine.banterColor, LogTag.Banter, "After WaitUntil SCENE_READY");
            scene.SetLoaded();
        }

        public void OnSendVersion(string versionData)
        {
            Send(APICommands.EVENT + APICommands.BANTER_VERSION + MessageDelimiters.PRIMARY + versionData);
        }

        // public void OnSendUser(string name, string id, string color, string instance) {
        //     Send(APICommands.EVENT + APICommands.SEND_USER + MessageDelimiters.PRIMARY + name + MessageDelimiters.SECONDARY + id + MessageDelimiters.SECONDARY + color + MessageDelimiters.SECONDARY + instance);
        // }

        public void OnUnitySceneLoaded()
        {
            scene.state = SceneState.UNITY_READY;
            LogLine.Do(LogLine.banterColor, LogTag.Banter, "Unity Scene Loaded.");
            Send(APICommands.EVENT + APICommands.UNITY_LOADED + MessageDelimiters.PRIMARY);
        }

        public void OnVoiceStarted()
        {
            Send(APICommands.EVENT + APICommands.VOICE_STARTED + MessageDelimiters.PRIMARY);
        }
        public void OnAiModel(string glb)
        {
#if BANTER_VISUAL_SCRIPTING
            EventBus.Trigger("OnAiModel", new CustomEventArgs("", new object[] { glb }));
#endif
            Send(APICommands.EVENT + APICommands.AI_IMAGE_RECV + MessageDelimiters.PRIMARY + glb);
        }
        public void OnAiImage(string image)
        {
#if BANTER_VISUAL_SCRIPTING
            EventBus.Trigger("OnAiImage", new CustomEventArgs("", new object[] { image }));
#endif
            Send(APICommands.EVENT + APICommands.AI_MODEL_RECV + MessageDelimiters.PRIMARY + image);
        }
        public void OnBase64ToCDN(long image)
        {
#if BANTER_VISUAL_SCRIPTING
            EventBus.Trigger("OnBase64CDNLink", new CustomEventArgs("", new object[] { image }));
#endif
            Send(APICommands.EVENT + APICommands.BASE_64_TO_CDN_RECV + MessageDelimiters.PRIMARY + image);
        }
        public void OnSelectFile(SelectFileType type, string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if(bytes.Length > 1048576 * 4) {
#if BANTER_VISUAL_SCRIPTING
                EventBus.Trigger("OnSelectFile", new CustomEventArgs("", new object[] { "too-large-over-4mb", type }));
#endif
                Send(APICommands.EVENT + APICommands.SELECT_FILE_RECV + MessageDelimiters.PRIMARY + "too-large-over-4mb" + MessageDelimiters.SECONDARY + (int)type);
                return;
            }
            string file = Convert.ToBase64String(bytes);
#if BANTER_VISUAL_SCRIPTING
            EventBus.Trigger("OnSelectFile", new CustomEventArgs("", new object[] { file, type }));
#endif
            Send(APICommands.EVENT + APICommands.SELECT_FILE_RECV + MessageDelimiters.PRIMARY + file + MessageDelimiters.SECONDARY + (int)type);
        }
        public void OnTranscription(string message, string id)
        {
#if BANTER_VISUAL_SCRIPTING
            EventBus.Trigger("OnSTT", new CustomEventArgs(id, new object[] { message }));
#endif
            Send(APICommands.EVENT + APICommands.SEND_TRANSCRIPTION + MessageDelimiters.PRIMARY + id + MessageDelimiters.SECONDARY + message);
        }

        public void OnFullSpaceState(string json)
        {
            Send(APICommands.EVENT + APICommands.FULL_SPACE_STATE + MessageDelimiters.PRIMARY + json);
        }

        public void OnSpaceStateChanged(string prop, string newValue, string oldValue, bool isPublic)
        {
            Send(APICommands.EVENT + APICommands.SPACE_STATE_CHANGED + MessageDelimiters.PRIMARY + prop + MessageDelimiters.SECONDARY + newValue + MessageDelimiters.SECONDARY + oldValue + MessageDelimiters.SECONDARY + (isPublic ? "1" : "0"));
        }

        public void OnUserStateChanged(string data)
        {
            Send(APICommands.EVENT + APICommands.USER_STATE_CHANGED + MessageDelimiters.PRIMARY + data);
        }

        public void OnOneShot(string data, string fromId, bool fromAdmin)
        {
#if BANTER_VISUAL_SCRIPTING
            EventBus.Trigger("OnOneShot", new CustomEventArgs(fromId, new object[] { data }));
#endif
            Send(APICommands.EVENT + APICommands.ONE_SHOT_RECIEVED + MessageDelimiters.PRIMARY + fromId + MessageDelimiters.SECONDARY + (fromAdmin ? "1" : "0") + MessageDelimiters.SECONDARY + data);
        }

        public void OnUserJoined(UserData user)
        {
            Send(APICommands.EVENT + APICommands.USER_JOINED + MessageDelimiters.PRIMARY + user.uid + MessageDelimiters.SECONDARY + user.name + MessageDelimiters.SECONDARY + (user.isLocal ? "1" : "0") + MessageDelimiters.SECONDARY + user.id + MessageDelimiters.SECONDARY + user.color);
        }

        public void OnUserLeft(UserData user)
        {
            Send(APICommands.EVENT + APICommands.USER_LEFT + MessageDelimiters.PRIMARY + user.uid + MessageDelimiters.SECONDARY + user.name + MessageDelimiters.SECONDARY + (user.isLocal ? "1" : "0") + MessageDelimiters.SECONDARY + user.id + MessageDelimiters.SECONDARY + user.color);
        }

        public void OnClick(GameObject obj, Vector3 point, Vector3 normal)
        {
            Send(APICommands.EVENT + APICommands.CLICKED + MessageDelimiters.PRIMARY + obj.GetInstanceID() + MessageDelimiters.SECONDARY + point.x + MessageDelimiters.SECONDARY + point.y + MessageDelimiters.SECONDARY + point.z + MessageDelimiters.SECONDARY + normal.x + MessageDelimiters.SECONDARY + normal.y + MessageDelimiters.SECONDARY + normal.z);
        }

        public void OnGrab(GameObject obj, Vector3 point, HandSide side)
        {
            Send(APICommands.EVENT + APICommands.GRABBED + MessageDelimiters.PRIMARY + obj.GetInstanceID() + MessageDelimiters.SECONDARY + point.x + MessageDelimiters.SECONDARY + point.y + MessageDelimiters.SECONDARY + point.z + MessageDelimiters.SECONDARY + (int)side);
        }

        public void OnRelease(GameObject obj, HandSide side)
        {
            Send(APICommands.EVENT + APICommands.RELEASED + MessageDelimiters.PRIMARY + obj.GetInstanceID() + MessageDelimiters.SECONDARY + (int)side);
        }

        public void OnButtonPressed(ButtonType button, HandSide side)
        {
            Send(APICommands.EVENT + APICommands.BUTTON_PRESSED + MessageDelimiters.PRIMARY + (int)button + MessageDelimiters.SECONDARY + (int)side);
        }

        public void OnButtonReleased(ButtonType button, HandSide side)
        {
            Send(APICommands.EVENT + APICommands.BUTTON_RELEASED + MessageDelimiters.PRIMARY + (int)button + MessageDelimiters.SECONDARY + (int)side);
        }
        public void OnAframeTrigger(string data)
        {
            Send(APICommands.EVENT + APICommands.AFRAME_TRIGGER + MessageDelimiters.PRIMARY + data);
        }
        public void OnMenuBrowserMessage(string data)
        {
            Send(APICommands.EVENT + APICommands.MENU_BROWSER_MESSAGE + MessageDelimiters.PRIMARY + data);
        }

        public void OnReceiveBrowserMessage(BanterBrowser browser, string message)
        {
            Send(APICommands.EVENT + APICommands.BROWSER_MESSAGE + MessageDelimiters.PRIMARY + browser.gameObject.GetInstanceID() + MessageDelimiters.SECONDARY + message);
        }

        public void OnKeyPress(KeyCode key)
        {
            Send(APICommands.EVENT + APICommands.KEY + MessageDelimiters.PRIMARY + (int)key);
        }

        public void _OnCollisionEnter(GameObject obj, Collision collision)
        {
            ContactPoint[] contact = new ContactPoint[1];
            collision.GetContacts(contact);
            var userData = collision.gameObject.GetComponentInParent<UserData>();
            Send(APICommands.EVENT + APICommands.COLLISION_ENTER + MessageDelimiters.PRIMARY + obj.GetInstanceID() + MessageDelimiters.SECONDARY + collision.gameObject.GetInstanceID() + MessageDelimiters.SECONDARY + contact[0].point.x + MessageDelimiters.SECONDARY + contact[0].point.y + MessageDelimiters.SECONDARY + contact[0].point.z + MessageDelimiters.SECONDARY + contact[0].normal.x + MessageDelimiters.SECONDARY + contact[0].normal.y + MessageDelimiters.SECONDARY + contact[0].normal.z + MessageDelimiters.SECONDARY + collision.gameObject.tag + MessageDelimiters.SECONDARY + collision.gameObject.name + MessageDelimiters.SECONDARY + (userData?.uid ?? "-1"));
        }

        public void _OnCollisionStay(GameObject obj, Collision collision)
        {
            ContactPoint[] contact = new ContactPoint[1];
            collision.GetContacts(contact);
            Send(APICommands.EVENT + APICommands.COLLISION_STAY + MessageDelimiters.PRIMARY + obj.GetInstanceID() + MessageDelimiters.SECONDARY + collision.gameObject.GetInstanceID() + MessageDelimiters.SECONDARY + contact[0].point.x + MessageDelimiters.SECONDARY + contact[0].point.y + MessageDelimiters.SECONDARY + contact[0].point.z + MessageDelimiters.SECONDARY + contact[0].normal.x + MessageDelimiters.SECONDARY + contact[0].normal.y + MessageDelimiters.SECONDARY + contact[0].normal.z + MessageDelimiters.SECONDARY + collision.gameObject.tag + MessageDelimiters.SECONDARY + collision.gameObject.name);
        }

        public void _OnCollisionExit(GameObject obj, Collision collision)
        {
            var userData = collision.gameObject.GetComponentInParent<UserData>();
            Send(APICommands.EVENT + APICommands.COLLISION_EXIT + MessageDelimiters.PRIMARY + obj.GetInstanceID() + MessageDelimiters.SECONDARY + collision.gameObject.GetInstanceID() + MessageDelimiters.SECONDARY + collision.gameObject.tag + MessageDelimiters.SECONDARY + collision.gameObject.name + MessageDelimiters.SECONDARY + collision.gameObject.layer + MessageDelimiters.SECONDARY + (userData?.uid ?? "-1"));
        }

        public void _OnTriggerEnter(GameObject obj, Collider collider)
        {
            var userData = collider.GetComponentInParent<UserData>();
            Send(APICommands.EVENT + APICommands.TRIGGER_ENTER + MessageDelimiters.PRIMARY + obj.GetInstanceID() + MessageDelimiters.SECONDARY + collider.gameObject.GetInstanceID() + MessageDelimiters.SECONDARY + collider.gameObject.tag + MessageDelimiters.SECONDARY + collider.gameObject.name + MessageDelimiters.SECONDARY + (userData?.uid ?? "-1"));
        }

        public void _OnTriggerStay(GameObject obj, Collider collider)
        {
            Send(APICommands.EVENT + APICommands.TRIGGER_STAY + MessageDelimiters.PRIMARY + obj.GetInstanceID() + MessageDelimiters.SECONDARY + collider.gameObject.GetInstanceID() + MessageDelimiters.SECONDARY + collider.gameObject.tag + MessageDelimiters.SECONDARY + collider.gameObject.name);
        }

        public void _OnTriggerExit(GameObject obj, Collider collider)
        {
            var userData = collider.GetComponentInParent<UserData>();
            Send(APICommands.EVENT + APICommands.TRIGGER_EXIT + MessageDelimiters.PRIMARY + obj.GetInstanceID() + MessageDelimiters.SECONDARY + collider.gameObject.GetInstanceID() + MessageDelimiters.SECONDARY + collider.gameObject.tag + MessageDelimiters.SECONDARY + collider.gameObject.name + MessageDelimiters.SECONDARY + (userData?.uid ?? "-1"));
        }

        public void CheckPipe()
        {
            if (!pipe.GetIsConnected())
            {
                SetupPipe();
            }
        }

        #region Legacy stuff
        public void ToggleDevTools()
        {
            pipe.Send(APICommands.TOGGLE_DEV_TOOLS);
        }
        public void HideDevTools()
        {
            pipe.Send(APICommands.HIDE_DEV_TOOLS);
        }
        public void Reload()
        {
            pipe.Send(APICommands.RELOAD);
        }
        public void LegacySendToAframe(string msg)
        {
            Send(APICommands.LEGACY_SEND_TO_AFRAME + MessageDelimiters.PRIMARY + msg);
        }
        #endregion
    }
}