using System;
using System.Threading.Tasks;

public abstract class BanterPipe{
    public abstract void Start(Action connectedCallback, Action<string> msgCallback);
    public abstract void Stop();
    public abstract void Send(string msg);
    public abstract bool GetIsConnected();
    public abstract object GetActivity();
}