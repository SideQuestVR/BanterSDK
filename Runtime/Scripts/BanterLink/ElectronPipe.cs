using System.Collections;
using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.Collections.Concurrent;
using Lachee.IO;
using Banter;

public class ElectronPipe : BanterPipe {
    private CountingLogger incomingLogger = new CountingLogger("ElectronPipe: Web -> Unity");
    private CountingLogger outgoingLogger = new CountingLogger("ElectronPipe: Unity -> Web");
    private Thread SenderThread;
    private object _SenderThreadLock = new object();
    private ConcurrentQueue<string> toSendQueue = new ConcurrentQueue<string>();
    private AutoResetEvent sendEvent = new AutoResetEvent(false);
    public string PipeName { get; private set; }
    public int headerSize = 4;
    Thread readThread;
    private NamedPipeClientStream rendererClient;
    
    public ElectronPipe(string pipeName)
    {
        PipeName = pipeName;
    }


    public override void Start(Action connectedCallback, Action<string> msgCallback) {
        _ = InitWait((success) =>
        {
            if (!success)
            {
                Debug.LogError("Failed to wait for named pipe to connect!");
                throw new Exception("Named pipe failed connect before timeout");
            }
            connectedCallback();
            StartReadThread(msgCallback);
        });
    }

    public override void Stop(){
        StopReadThread();
        StopSenderThread();
        Dispose();
    }

    public void StartReadThread(Action<string> callback) {
        readThread = new Thread(() => ReadThread(callback));
        readThread.Start();
    }
    public void StopReadThread() {
        if(readThread != null) {
            readThread?.Abort();
        }
    }
    public void StopSenderThread() {
        if(SenderThread != null) {
            SenderThread?.Abort();
        }
    }

    public async Task InitWait(Action<bool> completeCallback)
    {        
        bool done = false;

        var ctx = new CancellationTokenSource();
        ctx.CancelAfter(30000);
        _ = Task.Run(() =>
        {
            // Keep retrying pipe connection once every one seconds for 30 seconds (token timeout)
            while (!done)
            {
                rendererClient = new NamedPipeClientStream(".", PipeName);
                Thread.Sleep(1000);
                try
                {
                    var code = rendererClient.Connect();
                    if (code == 0) {
                        completeCallback(true);
                        done = true;
                    } else {
                        rendererClient.Dispose();
                        Debug.LogError($"[Banter] Pipe not connected!");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                    //completeCallback(false);
                    //done = true;
                }
                if (ctx.IsCancellationRequested)
                {
                    Debug.LogError("[Banter] Timeout on Pipe connection");
                    completeCallback(false);
                    done = true;
                }
            }
        }, ctx.Token);

        // Wait for the done signal from the task
        while (!done)
        {
            await new WaitForSeconds(0.1f);
        }
    }

    

    public void PurgeSendQueue()
    {
        if (toSendQueue.Count > 0)
        {
            var ct = toSendQueue.Count;
            toSendQueue.Clear();
        }
    }

    public void ReadThread(Action<string> callback) {
        byte[] result;
        if (rendererClient == null) return;
        while (rendererClient != null && rendererClient.IsConnected){
            if (rendererClient == null || !rendererClient.IsConnected)
            {
                break;
            }
            result = new byte[4];
            try{
                if(rendererClient == null) {
                    break;
                }

                var read = rendererClient.Read(result, 0, result.Length);
                if (read > 0)
                {
                    var length = BitConverter.ToInt32(result, 0);
                    result = new byte[length];

                    read = rendererClient.Read(result, 0, result.Length);
                    if (read > 0)
                    {
                        var strResult = Encoding.Default.GetString(result);
                        // incomingLogger.Add();
                        callback(strResult);
                    }
                }
            }catch(ThreadAbortException){
                // ignore
            }catch (ObjectDisposedException){
                // ignore
            }catch(Exception e){
                Debug.LogError("[Banter] Exception in Pipe Read Thread");
                Debug.LogException(e);
            }
        }
        sendEvent.Set();
    }
    private void QueueSend(string data)
    {
        lock (_SenderThreadLock)
        {
            if (SenderThread == null)
            {
                SenderThread = new Thread(o =>
                {
                    try
                    {
                        while (rendererClient?.IsConnected ?? false)
                        {
                            if (toSendQueue.TryDequeue(out var data))
                            {
                                byte[] b = Encoding.UTF8.GetBytes(data);
                                byte[] blen = BitConverter.GetBytes(b.Length);
                                byte[] concat = new byte[b.Length + blen.Length];
                                Buffer.BlockCopy(blen, 0, concat, 0, 4);
                                Buffer.BlockCopy(b, 0, concat, 4, b.Length);
                                // outgoingLogger.Add();
                                SendMessage(concat);
                            }
                            else
                            {
                                sendEvent.WaitOne(1);
                            }
                        }
                    }catch(ThreadAbortException){
                        // ignore
                    } catch (Exception ex)
                    {
                        Debug.LogError("[Banter] Exception in Pipe Sender Thread");
                        Debug.LogException(ex);

                    } finally
                    {
                        lock(_SenderThreadLock)
                        {
                            SenderThread = null;
                        }
                    }
                });
                SenderThread.Start();
            }
        }
        toSendQueue.Enqueue(data);
        sendEvent.Set();

    }
    private void SendMessage(byte[] missing)
    {
        if (rendererClient != null)
        {
            lock (rendererClient)
            {
                if (rendererClient.IsConnected)
                {
                    rendererClient.WriteAsync(missing);
                }
                else
                {
                    Debug.LogError("[Banter] Message sent to closed Pipe!");
                }
            }
        }
    }

    public override void Send(string json)
    {
        QueueSend(json);
    }

    public override bool GetIsConnected()
    {
        return true;
    }

    public override object GetActivity()
    {
        return null;
    }

    public void Dispose()
    {
         if(rendererClient != null) {
            rendererClient.Dispose();
            rendererClient = null;
        }
    }

}