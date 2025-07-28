using System.Collections;
using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.Collections.Concurrent;
using Lachee.IO;
using Banter.SDK;
using System.Drawing;
using Banter.Utilities.Async;

[System.Serializable]
public class DirtyRect
{
    public int x;
    public int y;
    public int width;
    public int height;
}
public class DirtyRectImage
{
    public DirtyRect dirty;
    public byte[] image;
}

public class ElectronPipe : BanterPipe
{
    private Thread SenderThread;
    private object _SenderThreadLock = new object();
    private ConcurrentQueue<string> toSendQueue = new ConcurrentQueue<string>();
    private AutoResetEvent sendEvent = new AutoResetEvent(false);
    public string PipeName { get; private set; }
    public int headerSize = 4;
    Thread readThread;
    private NamedPipeClientStream rendererClient;
    Thread readThreadGraphics;
    private NamedPipeClientStream rendererClientGraphics;

    public ElectronPipe(string pipeName)
    {
        PipeName = pipeName;
    }

    static int fullWidth = 1280;
    static  int fullHeight = 720;
    byte[] fullFrameData = new byte[fullWidth * fullHeight * 4]; // Assuming RGBA32 format

    int bytesPerPixel = 4; // for RGBA32

    public Texture2D texture;

    // Assume fullFrameData is the complete byte[] buffer for the texture
    // Assume dirtyRectData is the raw byte[] for the dirty rectangle
    // (x, y) is the bottom-left origin of the dirty rect in the texture

    public void ApplyDirtyRect(byte[] dirtyRectData, int x, int y, int rectWidth, int rectHeight)
    {
        UnityMainThreadTaskScheduler.Default.Enqueue(() =>
        {
            texture.LoadRawTextureData(dirtyRectData);
            texture.Apply();
            // // Create or reuse a temporary texture
            // Texture2D tempTexture = new Texture2D(rectWidth, rectHeight, texture.format, false);

            // // Load raw data into the temporary texture
            // tempTexture.LoadRawTextureData(dirtyRectData);
            // tempTexture.Apply();

            // // Optional: Flip the texture vertically if the source data is top-down

            // // Copy into the destination texture at the specified position
            // UnityEngine.Graphics.CopyTexture(tempTexture, 0, 0, 0, 0, rectWidth, rectHeight, texture, 0, 0, x, y);
            // FlipTextureHorizontally(texture);

            // // Clean up the temporary texture
            // UnityEngine.Object.Destroy(tempTexture);
        });
        
    }

    byte[] FlipTextureHorizontally(byte[] dirtyRectData)
    {
        int width = 1280;
        int height = 720;
        int bytesPerPixel = 4;
        int stride = width * bytesPerPixel; // 1280 * 4 = 5120

        byte[] flippedData = new byte[dirtyRectData.Length];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            for (int x = 0; x < width; x++)
            {
                int srcIndex = rowStart + x * bytesPerPixel;
                int dstIndex = rowStart + (width - 1 - x) * bytesPerPixel;

                // Copy 4 bytes (BGRA) from src to dst
                flippedData[dstIndex] = dirtyRectData[srcIndex];         // B
                flippedData[dstIndex + 1] = dirtyRectData[srcIndex + 1]; // G
                flippedData[dstIndex + 2] = dirtyRectData[srcIndex + 2]; // R
                flippedData[dstIndex + 3] = dirtyRectData[srcIndex + 3]; // A
            }
        }
        return flippedData;
    }

    public override void Start(Action connectedCallback, Action<string> msgCallback)
    {
        texture = new Texture2D(fullWidth, fullHeight, TextureFormat.BGRA32, false);
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
        _ = InitWaitGraphics((success) =>
        {
            if (!success)
            {
                Debug.LogError("Failed to wait for named graphics pipe to connect!");
                throw new Exception("Named graphics pipe failed connect before timeout");
            }
            StartReadGraphicsThread(bytes =>
            {
                Debug.Log("[Banter]: Hi there!" + bytes?.image?.Length);
                Debug.Log("[Banter]: Hi there! rect" + bytes.dirty.x + " " + bytes.dirty.y + " " + bytes.dirty.width + " " + bytes.dirty.height);
            });

        });
    }

    public override void Stop()
    {
        StopReadThread();
        StopSenderThread();
        StopReadGraphicsThread();
        Dispose();
    }

    public void StartReadGraphicsThread(Action<DirtyRectImage> callback)
    {
        readThreadGraphics = new Thread(() => ReadGraphicsThread(callback));
        readThreadGraphics.Start();
    }
    public void StopReadGraphicsThread()
    {
        if (readThreadGraphics != null)
        {
            readThreadGraphics?.Abort();
        }
    }

    public void StartReadThread(Action<string> callback)
    {
        readThread = new Thread(() => ReadThread(callback));
        readThread.Start();
    }
    public void StopReadThread()
    {
        if (readThread != null)
        {
            readThread?.Abort();
        }
    }
    public void StopSenderThread()
    {
        if (SenderThread != null)
        {
            SenderThread?.Abort();
        }
    }

    public async Task InitWaitGraphics(Action<bool> completeCallback)
    {
        bool done = false;

        var ctx = new CancellationTokenSource();
        ctx.CancelAfter(60000);
        _ = Banter.SDK.TaskRunner.Run(() =>
        { // Keep retrying pipe connection once every one seconds for 60 seconds (token timeout)
            while (!done)
            {
                rendererClientGraphics = new NamedPipeClientStream(".", PipeName + "Graphics");
                Thread.Sleep(1000);
                try
                {

                    var code1 = rendererClientGraphics.Connect();
                    if (code1 == 0)
                    {
                        completeCallback(true);
                        done = true;
                    }
                    else
                    {
                        rendererClientGraphics.Dispose();
                        LogLine.Do($"[Banter] Pipe not connected!");
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
                    LogLine.Do("[Banter] Timeout on Pipe connection");
                    completeCallback(false);
                    done = true;
                }
            }
        }, $"{nameof(ElectronPipe)}.{nameof(InitWait)}", ctx.Token);

        // Wait for the done signal from the task
        while (!done)
        {
            await new WaitForSeconds(0.1f);
        }
    }
    public async Task InitWait(Action<bool> completeCallback)
    {
        bool done = false;

        var ctx = new CancellationTokenSource();
        ctx.CancelAfter(60000);
        _ = Banter.SDK.TaskRunner.Run(() =>
        {
            // Keep retrying pipe connection once every one seconds for 60 seconds (token timeout)
            while (!done)
            {
                rendererClient = new NamedPipeClientStream(".", PipeName);
                Thread.Sleep(1000);
                try
                {
                    var code = rendererClient.Connect();
                    if (code == 0)
                    {
                        completeCallback(true);
                        done = true;
                    }
                    else
                    {
                        rendererClient.Dispose();
                        LogLine.Do($"[Banter] Pipe not connected!");
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
                    LogLine.Do("[Banter] Timeout on Pipe connection");
                    completeCallback(false);
                    done = true;
                }
            }
        }, $"{nameof(ElectronPipe)}.{nameof(InitWait)}", ctx.Token);

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
    DirtyRectImage dirtyImage = new DirtyRectImage();
    public void ReadGraphicsThread(Action<DirtyRectImage> callback)
    {
        byte[] result;
        if (rendererClientGraphics == null) return;
        while (rendererClientGraphics != null && rendererClientGraphics.IsConnected)
        {
            if (rendererClientGraphics == null || !rendererClientGraphics.IsConnected)
            {
                break;
            }
            result = new byte[4];
            try
            {
                if (rendererClientGraphics == null)
                {
                    break;
                }

                var read = rendererClientGraphics.Read(result, 0, result.Length);
                if (read > 0)
                {
                    var length = BitConverter.ToInt32(result, 0);
                    result = new byte[length];

                    read = rendererClientGraphics.Read(result, 0, result.Length);
                    if (read > 0)
                    {
                        var resultDirty = new byte[4];
                        read = rendererClientGraphics.Read(resultDirty, 0, resultDirty.Length);
                        length = BitConverter.ToInt32(resultDirty, 0);
                        resultDirty = new byte[length];
                        read = rendererClientGraphics.Read(resultDirty, 0, resultDirty.Length);
                        var strResult = Encoding.Default.GetString(resultDirty);
                        var dirtyRect = JsonUtility.FromJson<DirtyRect>(strResult);
                        dirtyImage.dirty = dirtyRect;
                        dirtyImage.image = result;
                        ApplyDirtyRect(result, dirtyRect.x, dirtyRect.y, dirtyRect.width, dirtyRect.height);
                        callback(dirtyImage);
                    }
                }
            }
            catch (ThreadAbortException)
            {
                // ignore
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
            catch (Exception e)
            {
                Debug.LogError("[Banter] Exception in Pipe Read Thread");
                Debug.LogException(e);
            }
        }
    }
    public void ReadThread(Action<string> callback)
    {
        byte[] result;
        if (rendererClient == null) return;
        while (rendererClient != null && rendererClient.IsConnected)
        {
            if (rendererClient == null || !rendererClient.IsConnected)
            {
                break;
            }
            result = new byte[4];
            try
            {
                if (rendererClient == null)
                {
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
                        IncomingLogger.Add();
                        callback(strResult);
                    }
                }
            }
            catch (ThreadAbortException)
            {
                // ignore
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
            catch (Exception e)
            {
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
                                OutgoingLogger.Add();
                                SendMessage(concat);
                            }
                            else
                            {
                                sendEvent.WaitOne(1);
                            }
                        }
                    }
                    catch (ThreadAbortException)
                    {
                        // ignore
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[Banter] Exception in Pipe Sender Thread");
                        Debug.LogException(ex);

                    }
                    finally
                    {
                        lock (_SenderThreadLock)
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
        if (rendererClient != null)
        {
            rendererClient.Dispose();
            rendererClient = null;
        }
    }

}