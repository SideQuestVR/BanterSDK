using System;
using System.Collections;
using Banter.SDKEditor;
using Unity.EditorCoroutines.Editor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Toggle = UnityEngine.UIElements.Toggle;

public class LoginManager
{
    SqEditorAppApi sq;
    Toggle autoUpload;
    Label buildButton;
    Label codeText;
    VisualElement linkPage;
    VisualElement loggedInView;
    Label statusText;
    int codeCheckCount = 0;
    public event Action OnLoginCompleted;
    public event Action RefreshView;

    VisualElement ExtraUploadButtons;
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

        signOut.RegisterCallback<MouseUpEvent>((e) => LogOut());
        codeCheckCount = 0;
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
        buildButton.text = autoUpload.value && sq.User != null ? "Build & Upload it Now!" : "Build it Now!";
    }

    public void GetCode()
    {
        //TODO LoggedOutVisibleContainer.SetActive(false);
        //call GetLoginCode from the api to retrieve the short code a user should enter
        EditorCoroutineUtility.StartCoroutine(sq.GetLoginCode((code) =>
        {
            //When a code has been retrieved, the Code and the VerificationUrl returned from the API should
            //  be shown to the user
            codeText.text = $"and put in {code.Code}";
            linkPage.style.display = DisplayStyle.Flex;
            //begin polling for completion of the short code login using the interval returned from the API
            StartPolling(code.PollIntervalSeconds);
        }, (error) =>
        {
            //if something goes wrong, details of what should be in the exception
            Debug.LogError("Failed to get code from API!");
            Debug.LogException(error);
            // LoggedOutVisibleContainer.SetActive(true);
        }), this);
    }
    EditorCoroutine waitCoroutine;

    public void StopPolling()
    {
        if (waitCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
        codeCheckCount = 0;
    }

    public void StartPolling(int delaySec)
    {
        waitCoroutine = EditorCoroutineUtility.StartCoroutine(Poller(delaySec), this);
    }

    private IEnumerator Poller(int delaySec)
    {
        //this coroutine loops until the short code login request either fails or succeeds, waiting delaySec between checks
        while (true)
        {
            yield return new WaitForSecondsRealtime(delaySec);
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
            if (ex != null)
            {
                //failures mean the call failed, timed out or something else went wrong.
                //when this happens, stop polling because the situation won't improve.
                Debug.LogError("Exception while checking for login code completion");
                Debug.LogException(ex);
                statusText.text = $"Failed: {ex.Message}";
                // LoggedOutVisibleContainer.SetActive(true);
                StopPolling();
                yield break;
            }
            if (isDone)
            {
                //if the user logged in with the short code, stop the polling coroutine and continue on
                LoginCompleted();
                StopPolling();
                yield break;
            }
            else
            {
                if (codeCheckCount++ < 10)
                {
                    // AddStatus($"Login with short code is not yet complete.  Will check again in {delaySec} seconds");
                }
                else
                {
                    // AddStatus($"Nothing after 10 attempts, stopping polling.");
                    StopPolling();
                    yield break;
                }
            }
        }
    }

    private void LogOut()
    {
        sq.Logout();
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
                // AddStatus("User profile information has been refreshed from the API successfully");
                statusText.text = $"Hi {sq.User.Name}!";
            }, (e) =>
            {
                Debug.LogError("Failed to refresh user");
                Debug.LogException(e);
                LogOut();
            }), this);

        }
    }
}