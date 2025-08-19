// using System;
// using System.Collections.Generic;
// using System.IO;
// using UnityEngine;
// using UnityEngine.Events;

// namespace TLab.WebView
// {
//     // class BrowserCommands {
//     //     public const string CREATE_WINDOW = "CREATE_WINDOW";
//     //     public const string KILL_WINDOW = "KILL_WINDOW";
//     //     public const string RELOAD = "RELOAD";
//     //     public const string TOGGLE_DEV_TOOLS = "TOGGLE_DEV_TOOLS";
//     //     public const string HIDE_DEV_TOOLS = "HIDE_DEV_TOOLS";
//     //     public const string LOAD_URL = "LOAD_URL";
//     //     public const string INJECT_JS = "INJECT_JS";
//     //     public const string INJECT_JS_CALLBACK = "INJECT_JS_CALLBACK";
//     // }
//     // class MessageDelims{
//     //     public const string PRIMARY = "¶";
//     //     public const string SECONDARY = "§";
//     // }
//     public class BrowserManager : MonoBehaviour
//     {
//         public UnityEvent OnBrowserStarted = new UnityEvent();
//         static BrowserManager _instance;
//         public static string WEB_ROOT = "WebRoot";
//         public bool IsElectronRunning = false;
//         private static string _pipeName;
//         public ElectronPipe electronPipe;
//         public static BrowserManager Instance
//         {
//             get
//             {
//                 if (_instance == null)
//                 {
//                     _instance = new GameObject("BrowserManager").AddComponent<BrowserManager>();
//                 }
//                 return _instance;
//             }
//         }

//         void Awake()
//         {
//             if (_instance == null)
//             {
//                 _instance = this;
//             }
//             else
//             {
//                 return;
//             }
// #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
//             StartBrowser();
// #endif
//         }

//         void OnDestroy()
//         {
//             electronPipe?.Stop();
//             ProcessManager.Kill();
//         }

//         private void StartBrowser()
//         {
//             GeneratePipeName();
// #if !BANTER_EDITOR
//             var isProd = false;
// #else
//             var isProd = true;
// #endif
// #if UNITY_EDITOR
            
//             var Eargs = (isProd ? "--prod true " : "") + "--bebug --pipename " + _pipeName + " --root " + "\"" + Path.Join(Application.dataPath, WEB_ROOT) + "\"";
//             ProcessManager.Start(Path.GetFullPath("Packages\\com.sidequest.banter-webview\\Runtime\\Electron~~\\build\\win-unpacked\\"),
//                 Path.GetFullPath("Packages\\com.sidequest.banter-webview\\Runtime\\Electron~~\\build\\win-unpacked\\electron-link.exe"),
//                 Eargs, (str, isError) =>
//                 {
//                      Debug.Log(str);
//                 });
// #else
            
//             ProcessManager.Start(Directory.GetCurrentDirectory() + "\\electron-link",
//                 Directory.GetCurrentDirectory() + "\\electron-link\\electron-link.exe",
//                 "--bebug --prod true --pipename " + _pipeName);
// #endif
//             electronPipe = new ElectronPipe(_pipeName);
//             electronPipe.Start(() =>
//             {
//                 Debug.Log("Electron pipe started successfully.");
//                 IsElectronRunning = true;
//                 OnBrowserStarted.Invoke();
//             }, (msg) =>
//             {
//                 ParseMessage(msg);
//             });
//         }
//         Dictionary<int, Action<string>> messageHandlers = new Dictionary<int, Action<string>>();
//         int msgCount;
//         private void ParseMessage(string msg)
//         {
//             var parts = msg.Split(MessageDelims.PRIMARY, 2);
//             if (messageHandlers.TryGetValue(int.Parse(parts[0]), out var handler))
//             {
//                 handler?.Invoke(parts[1]);
//             }
//         }

//         public void Send(string data, Action<string> callback = null)
//         {
//             var id = ++msgCount;
//             if(msgCount > 999999)
//             {
//                 msgCount = 0; // Reset to avoid overflow
//             }
//             var message = $"{id}{MessageDelims.PRIMARY}{data}";
//             messageHandlers[id] = callback;
//             electronPipe.Send(message);
//         }

//         private string GeneratePipeName()
//         {
//             if (_pipeName == null)
//             {
//                 _pipeName = Guid.NewGuid().ToString().Replace("-", "");
//             }
//             return $"banterPipe{_pipeName}";
//         }
//         private void Update()
//         {
//             FragmentCapture.GarbageCollect();
//         }
//     }
// }
