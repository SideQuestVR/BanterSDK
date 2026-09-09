using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BS;
using SideQuest.Ora;
using Unity.VisualScripting;
using UnityEngine;
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

    /// <summary>Chromium's net error for a navigation that was interrupted by a newer one.</summary>
    public const int ErrAborted = -3;

    [Serializable]
    class LoadFailedPayload
    {
        public string url;
        public int code;
        public string description;
    }

    /// <summary>
    /// True when the browser's loadFailed payload is Chromium's ERR_ABORTED (-3). Ora forwards
    /// did-fail-load for the main frame with ANY error code, and -3 is what you get when a page
    /// that is still loading gets interrupted by the next LoadUrl — a portal fired mid-load, the
    /// user joining another space, or the missing-world fallback navigating away from a hanging
    /// page. That is not a failure of the page we are now loading, so it must not Cancel() the
    /// current load. Non-JSON payloads (older Ora builds sent the bare URL) are not aborts.
    /// </summary>
    public static bool IsAbortedNavigation(string data)
    {
        if (string.IsNullOrEmpty(data)) return false;
        try
        {
            var payload = JsonUtility.FromJson<LoadFailedPayload>(data);
            return payload != null && payload.code == ErrAborted;
        }
        catch
        {
            return false;
        }
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
        view.loadFailed.AddListener((data) =>
        {
            if (IsAbortedNavigation(data))
            {
                LogLine.Do("[LOADING] Ignoring ERR_ABORTED loadFailed (navigation superseded): " + data);
                return;
            }
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