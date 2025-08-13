using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Banter.Utilities.Async;
namespace Banter.SDK
{
    public class AndroidPipe : BanterPipe
    {
        AndroidJavaObject activity;

        private CountingLogger fromAndroid = new CountingLogger("AndroidPipe: from android -> unity");
        private BanterSocketClient socketClient;
        public override void Start(Action connectedCallback, Action<string> msgCallback)
        {
            activity = new AndroidJavaObject("quest.side.wtf.MainActivity");

            try
            {
                _ = BanterStarterUpper.SetMainWindowPort(port =>
                { 
                    UnityEngine.Debug.Log("BanterSocketClient gonna connect to port " + port);
                    if (port <= 0)
                    {
                        throw new Exception("Failed to get port from android socket server");
                    }
                    socketClient = new BanterSocketClient();
                    socketClient.ConnectAsync("localhost", port, (msg) =>
                    {
                        msgCallback(msg);
                    }).ContinueWith(async x =>
                    {
                        try
                        {
                            await x;
                            UnityEngine.Debug.Log("BanterSocketClient connected");
                            UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => { connectedCallback(); }, $"{nameof(AndroidPipe)}.{nameof(Start)}"));
                        }
                        catch (Exception e)
                        {
                            UnityEngine.Debug.LogError("Failed to do cnnect stuff!");
                            UnityEngine.Debug.LogException(e);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("Failed to do socket server stuff!");
                UnityEngine.Debug.LogException(ex);
                throw;
            }
        }

        public override void Stop()
        {
            if (activity != null)
            {
                activity.Call("setCallBackListener", null);
            }
        }

        public override void Send(string msg)
        {
            AndroidJNI.AttachCurrentThread();
            if (activity != null)
            {
                OutgoingLogger.Add();
                BanterStarterUpper.browser?.SendMsg(msg);
                // activity.Call("sendMessage", msg);
            }
        }

        public override bool GetIsConnected()
        {
            return socketClient.IsConnected;
        }

        public override object GetActivity()
        {
            return activity;
        }
    }

}