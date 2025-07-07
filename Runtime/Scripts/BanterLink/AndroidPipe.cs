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
            // AndroidJNI.AttachCurrentThread();
            activity = new AndroidJavaObject("quest.side.wtf.MainActivity");
            var aframe = new AframeCallback();
            aframe.SetCallback(str =>
            {
                IncomingLogger.Add();

                if (str == "A")
                {
                    fromAndroid.Add();
                }
                else
                {

                    msgCallback(str);

                }
            });

            string jstxt = Resources.Load<TextAsset>("inject").text;
            try
            {
                activity.Call("setInjectionJs", jstxt);
            }
            catch (Exception)
            {
                UnityEngine.Debug.Log("No injection js found: " + jstxt);
            }
            //activity.Call("setCallBackListener", aframe);

            int port = -2;
            try
            {
                port = activity.Call<int>("startSocketServer");
                UnityEngine.Debug.Log("BanterSocketClient gonna connect to port " + port);
                if (port <= 0)
                {
                    throw new Exception("Failed to get port from android socket server");
                }
                socketClient = new BanterSocketClient();
                socketClient.ConnectAsync("localhost", port, (msg) =>
                {
                    fromAndroid.Add();
                    if (msg != "A")
                    {
                        msgCallback(msg);
                    }
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

            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("Failed to do socket server stuff!");
                UnityEngine.Debug.LogException(ex);
                throw;
            }

            //            connectedCallback();
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
                activity.Call("sendMessage", msg);
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