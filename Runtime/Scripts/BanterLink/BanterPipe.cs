using System;
using System.Threading.Tasks;
using Banter.SDK;

public abstract class BanterPipe
{
    public CountingLogger IncomingLogger = new CountingLogger("Pipe: Web -> Unity");
    public CountingLogger OutgoingLogger = new CountingLogger("Pipe: Unity -> Web");
    public abstract void Start(Action connectedCallback, Action<string> msgCallback);
    public abstract void Stop();
    public abstract void Send(string msg);
    public abstract bool GetIsConnected();
    public abstract object GetActivity();
}