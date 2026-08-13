using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Banter.SDK;
#if BANTER_ORA
using SideQuest.Ora;
using Unity.VisualScripting;
#endif
[RenamedFrom("Banter.SDK.BanterPipe")]
public class BSPipe
{
#if BANTER_ORA
    public OraView view;
    OraManager manager;
    BSLink link;
    public BSPipe(BSLink link, OraView view, OraManager manager)
    {
        this.manager = manager;
        this.view = view;
        this.link = link;
    }
    public CountingLogger IncomingLogger = new CountingLogger("Pipe: Web -> Unity");
    public CountingLogger OutgoingLogger = new CountingLogger("Pipe: Unity -> Web");
    public void Start(Action connectedCallback, Action<string> msgCallback)
    {
        manager?.browserConnected.AddListener(() => connectedCallback());
        if (manager != null && manager.connected)
            connectedCallback();
        view.browserMessage.AddListener((reqId, command, data) =>
        {
            msgCallback(data);
        });
        
        view.loadStarted.AddListener((url) =>
        {
            _ = link.scene.OnLoad(Guid.NewGuid().ToString());
            link.scene.SetLoaded();
        });
        view.loadFailed.AddListener((url) =>
        {
            link.scene.state = SceneState.LOAD_FAILED;
            link.scene.Cancel("The web page failed to load!");
        });
        view.domReady.AddListener((url) =>
        {
            link.scene.state = SceneState.DOM_READY;
            link.scene.events.OnDomReady.Invoke();
            link.scene.SetLoaded();
        });
    }
#endif
    public void Send(string msg)
    {

#if BANTER_ORA
        view?.Send(msg);
#endif
    }
    public bool GetIsConnected()
    {
#if BANTER_ORA
        return manager.connected;
#else
        return false;
#endif
    }
}