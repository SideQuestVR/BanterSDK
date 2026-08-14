using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BS;
using SideQuest.Ora;
using Unity.VisualScripting;
[RenamedFrom("Banter.SDK.BanterPipe")]
public class BSPipe
{
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
    public void Send(string msg)
    {

        view?.Send(msg);
    }
    public bool GetIsConnected()
    {
        return manager.connected;
    }
}