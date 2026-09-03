using System;
using System.Collections;
using BS.SDKEditor;
using Unity.EditorCoroutines.Editor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Toggle = UnityEngine.UIElements.Toggle;

public class LoginManager
{
    // How long a signed-out window keeps minting fresh codes and polling for approval on its own before
    // it parks and waits for the user to click SIGN IN (or click back into the window). Bounds the API
    // load of an abandoned window to roughly 4 getshortcode and 360 checkshortcode calls per hour.
    const int MaxAutoRenewMinutes = 60;
    // Never poll faster than this, whatever interval the server hands back.
    const int MinPollSeconds = 5;
    // Backoff after transient failures: 5 s, 10 s, ... capped at 60 s.
    const int MinRetrySeconds = 5;
    const int MaxRetrySeconds = 60;

    SqEditorAppApi sq;
    Toggle autoUpload;
    Label buildButton;
    Label codeText;
    VisualElement linkPage;
    VisualElement loggedInView;
    Label statusText;
    public event Action OnLoginCompleted;
    public event Action RefreshView;

    VisualElement ExtraUploadButtons;

    EditorCoroutine waitCoroutine;
    EditorCoroutine retryCoroutine;
    bool isFetchingCode;
    int consecutiveErrors;
    DateTime pollingDeadline = DateTime.MinValue;
    // A SIGN IN click that arrived while no usable code existed; fired once a code is available.
    Action<SqEditorLoginCode> pendingSignIn;

    bool IsCeilingReached => DateTime.Now > pollingDeadline;

    public LoginManager(SqEditorAppApi sq, Toggle autoUpload, Label codeText, VisualElement linkPage, VisualElement loggedInView, Label statusText, Label buildButton, VisualElement ExtraUploadButtons, Label signOut)
    {
        this.autoUpload = autoUpload;
        this.codeText = codeText;
        this.linkPage = linkPage;
        this.loggedInView = loggedInView;
        this.statusText = statusText;
        this.sq = sq;
        this.buildButton = buildButton;
        this.ExtraUploadButtons = ExtraUploadButtons;

        // Every path that clears the session (the Sign out label, a refresh token the server rejects, a
        // 401 a refresh could not fix) ends up here, so the window flips to the code view exactly once.
        sq.LoggedOut += HandleLoggedOut;
        signOut.RegisterCallback<MouseUpEvent>((e) => LogOut());
    }

    /// <summary>
    /// Stops all polling and detaches from the api. Call from the window's OnDisable.
    /// </summary>
    public void Dispose()
    {
        StopAll();
        sq.LoggedOut -= HandleLoggedOut;
    }

    public void ShowUploadToggle()
    {
        if (sq.User != null)
        {
            ExtraUploadButtons.style.display = DisplayStyle.Flex;
            autoUpload.style.display = DisplayStyle.Flex;
        }
        else
        {
            ExtraUploadButtons.style.display = DisplayStyle.None;
            autoUpload.style.display = DisplayStyle.None;
        }
        SetBuildButtonText();
        RefreshView?.Invoke();
    }
    public void SetLoginState()
    {
        if (sq.User != null)
        {
            LoginCompleted();
        }
        else
        {
            codeText.style.display = DisplayStyle.Flex;
            linkPage.style.display = DisplayStyle.Flex;
            loggedInView.style.display = DisplayStyle.None;
            SetBuildButtonText();
        }
        ShowUploadToggle();
    }
    public void SetBuildButtonText()
    {
        buildButton.text = autoUpload.value && sq.User != null ? "BUILD & UPLOAD" : "BUILD";
    }

    /// <summary>
    /// Requests a fresh short code and starts polling for its approval. Safe to call repeatedly: a
    /// request already in flight is reused rather than duplicated.
    /// </summary>
    /// <param name="onReady">Invoked once with the new code (used by SIGN IN to open the browser only when a usable code exists)</param>
    /// <param name="resetDeadline">True for user-initiated calls; false for automatic continuations so they do not extend the auto-renew window</param>
    public void GetCode(Action<SqEditorLoginCode> onReady = null, bool resetDeadline = true)
    {
        if (resetDeadline)
        {
            pollingDeadline = DateTime.Now.AddMinutes(MaxAutoRenewMinutes);
        }
        if (onReady != null)
        {
            pendingSignIn = onReady;
        }
        if (retryCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(retryCoroutine);
            retryCoroutine = null;
        }
        if (isFetchingCode)
        {
            return;
        }
        isFetchingCode = true;
        if (sq.CurrentLoginCode == null)
        {
            codeText.text = "Fetching code...";
        }
        else if (sq.IsLoginCodeExpired)
        {
            codeText.text = "Code expired, fetching a new one...";
        }
        codeText.style.display = DisplayStyle.Flex;
        linkPage.style.display = DisplayStyle.Flex;

        //call GetLoginCode from the api to retrieve the short code a user should enter
        EditorCoroutineUtility.StartCoroutine(sq.GetLoginCode((code) =>
        {
            isFetchingCode = false;
            consecutiveErrors = 0;
            //When a code has been retrieved, the Code and the VerificationUrl returned from the API should
            //  be shown to the user
            codeText.text = $"Code: {code.Code}";
            //begin polling for completion of the short code login using the interval returned from the API
            StartPolling(PollInterval(code));
            var signIn = pendingSignIn;
            pendingSignIn = null;
            signIn?.Invoke(code);
        }, (error) =>
        {
            isFetchingCode = false;
            pendingSignIn = null;
            //if something goes wrong, details of what should be in the exception
            Debug.LogError("Failed to get code from API!");
            Debug.LogException(error);
            if (IsCeilingReached)
            {
                codeText.text = "Code expired. Click SIGN IN for a new one.";
                return;
            }
            codeText.text = "Can't reach SideQuest, retrying...";
            consecutiveErrors++;
            retryCoroutine = EditorCoroutineUtility.StartCoroutine(RetryGetCodeAfter(RetryDelay()), this);
        }), this);
    }

    private IEnumerator RetryGetCodeAfter(int delaySec)
    {
        yield return new WaitForSecondsRealtime(delaySec);
        retryCoroutine = null;
        GetCode(resetDeadline: false);
    }

    private static int PollInterval(SqEditorLoginCode code)
    {
        return Math.Max(code.PollIntervalSeconds, MinPollSeconds);
    }

    private int RetryDelay()
    {
        return Math.Min(MinRetrySeconds * consecutiveErrors, MaxRetrySeconds);
    }

    public void StopPolling()
    {
        if (waitCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
    }

    /// <summary>
    /// Stops the approval poller and any pending retry of the code request.
    /// </summary>
    public void StopAll()
    {
        StopPolling();
        if (retryCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(retryCoroutine);
            retryCoroutine = null;
        }
    }

    public void StartPolling(int delaySec)
    {
        // Never run two pollers at once.
        StopPolling();
        waitCoroutine = EditorCoroutineUtility.StartCoroutine(Poller(delaySec), this);
    }

    private IEnumerator Poller(int delaySec)
    {
        //this coroutine loops until the short code login is approved, waiting delaySec between checks.
        //An expired code is replaced with a fresh one; transient failures back off and keep polling.
        while (true)
        {
            yield return new WaitForSecondsRealtime(delaySec);

            if (IsCeilingReached)
            {
                // Nobody has signed in for a long time; stop hitting the API until the user comes back.
                waitCoroutine = null;
                codeText.text = "Code expired. Click SIGN IN for a new one.";
                yield break;
            }

            SqEditorUser user = null;
            bool isDone = false;
            Exception ex = null;

            //Call to check if the short code has been completed
            yield return sq.CheckLoginCodeComplete((done, usr) =>
            {
                //The function is invoked with two parameters:
                // the first (done) is a boolean indicating if the short code request has been completed by the user
                // the second (usr) is the user profile object, and will be null until (done) is true
                isDone = done;
                user = usr;
            }, (e) =>
            {
                ex = e;
            });
            if (ex is SqEditorLoginCodeExpiredException)
            {
                // Clear our own handle first so the new poller's StartPolling doesn't try to stop the
                // coroutine that is currently running.
                waitCoroutine = null;
                GetCode(resetDeadline: false);
                yield break;
            }
            if (ex != null)
            {
                //network trouble or a server error: keep polling, a little slower each time.
                Debug.LogWarning("Exception while checking for login code completion, will retry");
                Debug.LogException(ex);
                codeText.text = "Can't reach SideQuest, retrying...";
                consecutiveErrors++;
                delaySec = RetryDelay();
                continue;
            }
            consecutiveErrors = 0;
            if (isDone)
            {
                //if the user logged in with the short code, stop the polling coroutine and continue on
                LoginCompleted();
                StopPolling();
                yield break;
            }
            if (sq.CurrentLoginCode != null)
            {
                // Clear any "retrying" text from an earlier blip.
                codeText.text = $"Code: {sq.CurrentLoginCode.Code}";
                delaySec = PollInterval(sq.CurrentLoginCode);
            }
        }
    }

    /// <summary>
    /// The window regained focus. While signed out, make sure a live code is showing and being polled;
    /// clicking into the window counts as the user coming back, so the auto-renew window restarts.
    /// </summary>
    public void OnWindowFocused()
    {
        if (sq.User != null)
        {
            return;
        }
        pollingDeadline = DateTime.Now.AddMinutes(MaxAutoRenewMinutes);
        if (sq.CurrentLoginCode == null || sq.IsLoginCodeExpired)
        {
            GetCode(resetDeadline: false);
        }
        else if (waitCoroutine == null && !isFetchingCode)
        {
            StartPolling(PollInterval(sq.CurrentLoginCode));
        }
    }

    /// <summary>
    /// SIGN IN was clicked. Invokes <paramref name="onReady"/> with a code that is still redeemable,
    /// fetching a fresh one first if the current code is missing or expired, and makes sure approval
    /// polling is running.
    /// </summary>
    public void PrepareSignIn(Action<SqEditorLoginCode> onReady)
    {
        if (sq.User != null)
        {
            return;
        }
        pollingDeadline = DateTime.Now.AddMinutes(MaxAutoRenewMinutes);
        if (sq.CurrentLoginCode != null && !sq.IsLoginCodeExpired && !isFetchingCode)
        {
            if (waitCoroutine == null)
            {
                StartPolling(PollInterval(sq.CurrentLoginCode));
            }
            onReady?.Invoke(sq.CurrentLoginCode);
            return;
        }
        GetCode(onReady, resetDeadline: false);
    }

    private void LogOut()
    {
        // The rest happens in HandleLoggedOut via sq.LoggedOut.
        sq.Logout();
    }

    private void HandleLoggedOut()
    {
        StopAll();
        SetLoginState();
        GetCode();
    }

    private void LoginCompleted()
    {
        loggedInView.style.display = DisplayStyle.Flex;
        codeText.style.display = DisplayStyle.None;
        linkPage.style.display = DisplayStyle.None;
        statusText.text = $"Hi {sq.User.Name}!";
        autoUpload.style.display = DisplayStyle.Flex;
        SetBuildButtonText();
        OnLoginCompleted?.Invoke();
        // EditorCoroutineUtility.StartCoroutine(CheckKitUserExists(), this);
    }

    public void RefreshUser()
    {
        if (sq.User != null)
        {
            //refreshes a user's data from the API.
            //This should be called periodically (e.g. on app start) to update the user's profile information.
            EditorCoroutineUtility.StartCoroutine(sq.RefreshUserProfile((u) =>
            {
                statusText.text = $"Hi {sq.User.Name}!";
            }, (e) =>
            {
                if (sq.User == null)
                {
                    // The API already cleared the session; its LoggedOut event has shown the code view.
                    Debug.LogWarning("SideQuest session is no longer valid: " + e.Message);
                    return;
                }
                // Every non-2xx status arrives as SqEditorApiAuthException, so look at the code: only an
                // outright rejection signs the user out.
                var httpCode = (e as SqEditorApiException)?.HttpCode;
                if (httpCode == 401 || httpCode == 403)
                {
                    Debug.LogError("SideQuest rejected the session, signing out");
                    Debug.LogException(e);
                    sq.Logout();
                    return;
                }
                // Offline, a server error, or a profile hiccup: keep the session, the next call will retry.
                Debug.LogWarning("Failed to refresh user profile, staying signed in: " + e.Message);
                statusText.text = $"Hi {sq.User.Name}! (offline)";
            }), this);

        }
    }
}
