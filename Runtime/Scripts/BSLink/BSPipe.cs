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

    // The space view starts on about:blank. On Windows, Ora's Electron injects the SDK into that document too, but it
    // only attaches its LoadStarted/DomReady listeners at the window's first paint: that document never gets a
    // LoadStarted, so BSScene.OnLoad (which creates BSScene.settings) never runs for it. The injection starts a Scene by
    // itself, so the blank document sends SCENE_START, object update requests for every scene object (a parented one
    // then threw on the null settings: "Error updating object") and, if the real page is held for 3 s, a SCENE_READY
    // for a page that is not a space. Until a page has started loading, nothing the view sends is about a space. On
    // Windows a real page's LoadStarted always comes first (OraView.LoadUrl waits for the window, and the listeners come
    // with it). Ora's Android compat channel can deliver a page's first messages ahead of its LoadStarted; OnLoad's state
    // reset already discarded those before this gate existed.
    bool pageLoadStarted;
    bool loggedIgnoring;
    int ignoredBeforeLoad;

    /// <summary>Whether a page has started loading in the view, which opens the pipe to its messages.</summary>
    internal bool PageLoadStarted => pageLoadStarted;

    // A LoadStarted for the start-up document itself (Ora attaching its listeners early, as in openBrowser mode) is not
    // a page load: it must not open the gate.
    static bool IsStartupUrl(string url) => string.IsNullOrEmpty(url) || url == "about:blank";

    public void Start(Action connectedCallback, Action<string> msgCallback)
    {
        manager?.browserConnected.AddListener(() => connectedCallback());
        if (manager != null && manager.connected)
            connectedCallback();
        view.browserMessage.AddListener((reqId, command, data) =>
        {
            if (!pageLoadStarted)
            {
                ignoredBeforeLoad++;
                if (!loggedIgnoring)
                {
                    loggedIgnoring = true;
                    LogLine.Do("Ignoring messages from the space view's start-up page (about:blank) until a page starts loading.");
                }
                return;
            }
            msgCallback(data);
        });

        view.loadStarted.AddListener((url) =>
        {
            // Before OnLoad, which queues the task that creates the settings: every message handled after this
            // queues its work behind that task.
            if (!pageLoadStarted && !IsStartupUrl(url))
            {
                pageLoadStarted = true;
                if (ignoredBeforeLoad > 0)
                    LogLine.Do("Ignored " + ignoredBeforeLoad + " message(s) from the space view's start-up page.");
            }
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
            if (!pageLoadStarted) return;
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