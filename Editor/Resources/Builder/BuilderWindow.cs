using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
using Banter.SDK;
using System.Threading;
using Banter.SDKEditor;
using Unity.EditorCoroutines.Editor;
using System.Collections;

public enum BanterBuilderBundleMode
{
    None = 0,
    Scene = 1,
    Kit = 2
}
public class KitObjectAndPath
{
    public UnityEngine.Object obj;
    public string path;
    public static List<Type> ALLOWED_KIT_TYPES = new List<Type>()
    {
        typeof(GameObject),
        typeof(Material),
        typeof(Shader)
    };
}
public class BuilderWindow : EditorWindow
{
    [SerializeField] private VisualTreeAsset _mainWindowVisualTree = default;
    [SerializeField] private StyleSheet _mainWindowStyleSheet = default;

    private const string SQ_API_CLIENT_ID = "client_0e4c67f9a6bbe12143870312";

    public static UnityEvent OnCompileAll = new UnityEvent();
    public static UnityEvent OnClearAll = new UnityEvent();
    public static UnityEvent OnVisualScript = new UnityEvent();
    public static UnityEvent OnCompileInjection = new UnityEvent();
    public static UnityEvent OnCompileElectron = new UnityEvent();
    public static UnityEvent OnCompileAllComponents = new UnityEvent();
    private BuildTarget[] buildTargets = new BuildTarget[] { BuildTarget.Android, BuildTarget.StandaloneWindows };
    private bool[] buildTargetFlags = new bool[] { true, true };
    BanterBuilderBundleMode mode = BanterBuilderBundleMode.None;
    Label scenePathLabel;
    Label mainTitle;
    string scenePath;
    ListView kitListView;
    List<KitObjectAndPath> kitObjectList = new List<KitObjectAndPath>();
    ListView buildProgress;
    ProgressBar buildProgressBar;
    Button removeSelected;
    Label statusBar;

    Label codeText;
    TextField spaceSlug;
    Label statusText;
    Label signOut;
    Button uploadWebOnly;
    Label uploadEverything;

    Toggle autoUpload;

    VisualElement loggedInView;


    string assetBundleRoot = "Assets";
    string assetBundleDirectory = "WebRoot";
    List<string> statusMessages = new List<string>();

    [MenuItem("Banter/Bundle Builder")]
    public static void ShowMainWindow()
    {
        BuilderWindow window = GetWindow<BuilderWindow>();
        window.minSize = new Vector2(450, 200);
        window.titleContent = new GUIContent("Banter Bundle Builder");
    }

    private SqEditorAppApi sq;

    private int codeCheckCount;

    public void OnDisable()
    {
        StopPolling();
        codeCheckCount = 0;
    }

    void ShowWebRoot()
    {
        Debug.Log("SelectPath");

        string path = Path.Join(assetBundleRoot, assetBundleDirectory);

        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath(path, typeof(UnityEngine.Object));

        Selection.activeObject = obj;

        EditorGUIUtility.PingObject(obj);
    }

    public void OnEnable()
    {
        codeCheckCount = 0;
        VisualElement content = _mainWindowVisualTree.CloneTree();
        content.style.height = new StyleLength(Length.Percent(100));
        rootVisualElement.styleSheets.Add(_mainWindowStyleSheet);
        rootVisualElement.Add(content);
        SqEditorAppApiConfig config = new SqEditorAppApiConfig(SQ_API_CLIENT_ID, Application.persistentDataPath, false);
        sq = new SqEditorAppApi(config);
        SetupUI();
        SetLoginState();
        if (sq.User != null)
        {
            AddStatus("User is logged in at startup, refreshing the user's profile");
            RefreshUser();
        }
        else
        {
            GetCode();
        }

    }
    private void ShowUploadToggle()
    {
        if (sq.User != null && mode == BanterBuilderBundleMode.Scene)
        {
            autoUpload.style.display = DisplayStyle.Flex;
        }
        else
        {
            autoUpload.style.display = DisplayStyle.None;
        }
        SetBuildButtonText();
    }
    private void SetLoginState()
    {
        if (sq.User != null)
        {
            LoginCompleted();
        }
        else
        {
            codeText.style.display = DisplayStyle.Flex;
            loggedInView.style.display = DisplayStyle.None;
            SetBuildButtonText();
        }
        ShowUploadToggle();
    }

    private void GetCode()
    {
        //TODO LoggedOutVisibleContainer.SetActive(false);
        //call GetLoginCode from the api to retrieve the short code a user should enter
        EditorCoroutineUtility.StartCoroutine(sq.GetLoginCode((code) =>
        {
            AddStatus("Successfully got login short code from API");
            //When a code has been retrieved, the Code and the VerificationUrl returned from the API should
            //  be shown to the user
            codeText.text = $"Go to {code.VerificationUrl}\nput in {code.Code}";
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

    private void StopPolling()
    {
        AddStatus("Stopping polling for completion of short code login");
        if (waitCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
    }

    private void StartPolling(int delaySec)
    {
        AddStatus("Beginning polling for completion of short code login");
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
                AddStatus("Login with short code has completed");
                //if the user logged in with the short code, stop the polling coroutine and continue on
                LoginCompleted();
                StopPolling();
                yield break;
            }
            else
            {
                if (codeCheckCount++ < 10)
                {
                    AddStatus($"Login with short code is not yet complete.  Will check again in {delaySec} seconds");
                }
                else
                {
                    AddStatus($"Nothing after 10 attempts, stopping polling.");
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
        statusText.text = $"Logged in as: {sq.User.Name}";
        autoUpload.style.display = DisplayStyle.Flex;
        SetBuildButtonText();
    }

    [MenuItem("Banter/Tools/Clear All Asset Bundles")]
    public static void ClearAllAssetBundles()
    {
        // Fetch all asset paths in the project
        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();

        // Iterate through all asset paths and clear asset bundle names
        foreach (string path in allAssetPaths)
        {
            AssetImporter assetImporter = AssetImporter.GetAtPath(path);
            if (assetImporter != null && !string.IsNullOrEmpty(assetImporter.assetBundleName))
            {
                assetImporter.assetBundleName = string.Empty;
            }
        }

        // Clear the AssetBundle cache
        if (!Caching.ClearCache())
        {
            Debug.LogError("Failed to clear the AssetBundle cache.");
        }

        // Refresh and update the asset database
        AssetDatabase.Refresh();

        // Remove unused asset bundle names
        AssetDatabase.RemoveUnusedAssetBundleNames();
        AssetDatabase.Refresh();

        Debug.Log("Cleared all asset bundles.");
    }

    public void RefreshUser()
    {
        if (sq.User != null)
        {
            //refreshes a user's data from the API.
            //This should be called periodically (e.g. on app start) to update the user's profile information.
            EditorCoroutineUtility.StartCoroutine(sq.RefreshUserProfile((u) =>
            {
                AddStatus("User profile information has been refreshed from the API successfully");
                statusText.text = $"Logged in as: {sq.User.Name}";
            }, (e) =>
            {
                Debug.LogError("Failed to refresh user");
                Debug.LogException(e);
            }), this);

        }
    }
    enum ActiveTab
    {
        Build,
        Upload,
        Tools,
        Logs
    }

    ActiveTab activeTabName = ActiveTab.Build;
    float activeTabPosition = 0;
    private void SetupTabs()
    {

        var activeTab = rootVisualElement.Q<VisualElement>("ActiveTab");

        var tabSections = rootVisualElement.Q<VisualElement>("TabSections");

        var buildTab = rootVisualElement.Q<Label>("BuildTab");
        var uploadTab = rootVisualElement.Q<Label>("UploadTab");
        var toolsTab = rootVisualElement.Q<Label>("ToolsTab");
        var logsTab = rootVisualElement.Q<Label>("LogsTab");

        var buildTabSection = rootVisualElement.Q<VisualElement>("BuildSection");
        var uploadSection = rootVisualElement.Q<VisualElement>("UploadSection");
        var toolsSection = rootVisualElement.Q<VisualElement>("ToolsSection");
        var logsSection = rootVisualElement.Q<VisualElement>("LogsSection");

        buildTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Build;
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });

        uploadTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Upload;
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });

        toolsTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Tools;
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });

        logsTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Logs;
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });

        rootVisualElement.RegisterCallback<GeometryChangedEvent>((e) =>
        {
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });
    }

    void MoveTabSections(VisualElement tabSections)
    {

        switch (activeTabName)
        {
            case ActiveTab.Build:
                tabSections.style.left = 0;
                break;
            case ActiveTab.Upload:
                tabSections.style.left = -rootVisualElement.resolvedStyle.width;
                break;
            case ActiveTab.Tools:
                tabSections.style.left = -rootVisualElement.resolvedStyle.width * 2;
                break;
            case ActiveTab.Logs:
                tabSections.style.left = -rootVisualElement.resolvedStyle.width * 3;
                break;
        }
    }

    void SetActivePosition()
    {
        switch (activeTabName)
        {
            case ActiveTab.Build:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 - (rootVisualElement.resolvedStyle.width * 0.3375f) - 45;
                break;
            case ActiveTab.Upload:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 - (rootVisualElement.resolvedStyle.width * 0.1125f) - 45;
                break;
            case ActiveTab.Tools:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 + (rootVisualElement.resolvedStyle.width * 0.1125f) - 45;
                break;
            case ActiveTab.Logs:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 + (rootVisualElement.resolvedStyle.width * 0.3375f) - 45;
                break;
        }
    }

    void ShowHideBuildButton()
    {
        if (!buildTargetFlags[0] && !buildTargetFlags[1])
        {
            buildButton.SetEnabled(false);
        }
        else
        {
            buildButton.SetEnabled(true);
        }
    }

    void SetBuildButtonText()
    {
        buildButton.text = autoUpload.value && sq.User != null && mode == BanterBuilderBundleMode.Scene ? "Build & Upload it Now!" : "Build it Now!";
    }
    Label buildButton;
    private void SetupUI()
    {
        statusBar = rootVisualElement.Q<Label>("StatusBar");
        SetupTabs();
        buildButton = rootVisualElement.Q<Label>("buildButton");

        buildButton.RegisterCallback<MouseUpEvent>((e) => BuildAssetBundles());

        var createSpace = rootVisualElement.Q<Label>("CreateSpace");
        createSpace.RegisterCallback<MouseUpEvent>((e) => OpenSpaceCreation());
        var openWebRoot = rootVisualElement.Q<Button>("OpenWebRoot");

        openWebRoot.clicked += () => ShowWebRoot();

        var clearLogs = rootVisualElement.Q<Button>("clearLogs");

        clearLogs.clicked += () => ClearLogs();

        var buildForAndroid = rootVisualElement.Q<Toggle>("buildForAndroid");
        buildTargetFlags[0] = buildForAndroid.value = EditorPrefs.GetBool("BanterBuilder_BuildTarget_Android", true);
        buildForAndroid.RegisterCallback<MouseUpEvent>((e) =>
        {
            EditorPrefs.SetBool("BanterBuilder_BuildTarget_Android", buildForAndroid.value);
            buildTargetFlags[0] = buildForAndroid.value;
            ShowHideBuildButton();
        });

        var buildForWindows = rootVisualElement.Q<Toggle>("buildForWindows");
        buildTargetFlags[1] = buildForWindows.value = EditorPrefs.GetBool("BanterBuilder_BuildTarget_Windows", true);
        buildForWindows.RegisterCallback<MouseUpEvent>((e) =>
        {
            EditorPrefs.SetBool("BanterBuilder_BuildTarget_Windows", buildForWindows.value);
            buildTargetFlags[1] = buildForWindows.value;
            ShowHideBuildButton();
        });

        ShowHideBuildButton();

        autoUpload = rootVisualElement.Q<Toggle>("autoUpload");
        autoUpload.value = EditorPrefs.GetBool("BanterBuilder_AutoUpload", false);
        SetBuildButtonText();
        autoUpload.RegisterCallback<MouseUpEvent>((e) =>
        {
            EditorPrefs.SetBool("BanterBuilder_AutoUpload", autoUpload.value);
            SetBuildButtonText();
        });
        var spaceSlugPlaceholder = rootVisualElement.Q<Label>("SpaceSlugPlaceholder");
        codeText = rootVisualElement.Q<Label>("LoginCode");
        spaceSlug = rootVisualElement.Q<TextField>("SpaceSlug");
        statusText = rootVisualElement.Q<Label>("SignedInStatus");
        signOut = rootVisualElement.Q<Label>("SignOut");
        uploadWebOnly = rootVisualElement.Q<Button>("UploadWebOnly");
        uploadEverything = rootVisualElement.Q<Label>("UploadEverything");
        loggedInView = rootVisualElement.Q<VisualElement>("LoggedInView");
        signOut.RegisterCallback<MouseUpEvent>((e) => LogOut());

        spaceSlug.RegisterValueChangedCallback((e) =>
        {
            ShowSpaceSlugPlaceholder(spaceSlugPlaceholder, e.newValue);
            EditorPrefs.SetString("BanterBuilder_spaceSlug", e.newValue);
        });

        spaceSlug.value = EditorPrefs.GetString("BanterBuilder_spaceSlug", "");
        ShowSpaceSlugPlaceholder(spaceSlugPlaceholder, spaceSlug.value);
        uploadWebOnly.clicked += () =>
        {
            uploadWebOnly.SetEnabled(false);
            uploadEverything.SetEnabled(false);
            EditorCoroutineUtility.StartCoroutine(UploadWebOnly(() =>
            {
                AddStatus("Upload complete.");
                uploadWebOnly.SetEnabled(true);
                uploadEverything.SetEnabled(true);
            }), this);
        };

        uploadEverything.RegisterCallback<MouseUpEvent>((e) =>
        {
            uploadWebOnly.SetEnabled(false);
            uploadEverything.SetEnabled(false);
            EditorCoroutineUtility.StartCoroutine(UploadEverything(() =>
            {
                AddStatus("Upload complete.");
                uploadWebOnly.SetEnabled(true);
                uploadEverything.SetEnabled(true);
            }), this);
        });


        mainTitle = rootVisualElement.Q<Label>("mainTitle");
        scenePathLabel = rootVisualElement.Q<Label>("scenePathLabel");
        buildProgress = rootVisualElement.Q<ListView>("buildProgress");
        buildProgress.makeItem = () =>
        {
            var label = new Label();
            label.AddToClassList("unity-label-margin");
            return label;
        };
        buildProgress.bindItem = (e, i) =>
        {
            (e as Label).text = statusMessages[i].ToLower();
        };
        buildProgress.itemsSource = statusMessages;
        buildProgress.Rebuild();
        buildProgress.selectionType = SelectionType.None;
        buildProgressBar = rootVisualElement.Q<ProgressBar>("buildProgressBar");
        removeSelected = rootVisualElement.Q<Button>("removeSelected");
        removeSelected.clicked += () => RemoveSelectedObjects();
        kitListView = rootVisualElement.Q<ListView>("kitItemList");
        kitListView.onSelectionChange += (e) => ShowRemoveSelected();
        kitListView.makeItem = () =>
        {
            var ele = new VisualElement();
            ele.style.flexDirection = FlexDirection.RowReverse;
            ele.style.justifyContent = Justify.SpaceBetween;
            var label = new Label
            {
                name = "kitItemName"
            };
            var button = new Button
            {
                name = "kitItemCopy"
            };
            label.style.color = new Color(0.560f, 0.560f, 0.560f);
            label.style.textOverflow = TextOverflow.Ellipsis;
            button.style.paddingBottom = button.style.paddingTop = 0;
            button.style.paddingLeft = button.style.paddingRight = 2;
            button.style.borderTopRightRadius = button.style.borderBottomRightRadius = button.style.borderTopLeftRadius = button.style.borderBottomLeftRadius = 8;
            button.style.marginTop = button.style.marginLeft = button.style.marginRight = 0;
            button.style.marginBottom = 2;
            ele.Add(button);
            ele.Add(label);
            ele.AddToClassList("unity-label-margin");
            return ele;
        };
        kitListView.bindItem = (e, i) =>
        {
            var name = kitObjectList[i].path.ToLower();
            e.Q<Label>("kitItemName").text = i + 1 + ". " + name;
            var button = e.Q<Button>("kitItemCopy");
            button.text = "copy";
            button.clicked += () =>
            {
                AddStatus("Copied path to clipboard: " + name);
                GUIUtility.systemCopyBuffer = name;
            };
        };
        kitListView.selectionType = SelectionType.Multiple;
        kitListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
        kitListView.reorderMode = ListViewReorderMode.Simple;
        DragAndDropStuff.SetupDropArea(rootVisualElement.Q<VisualElement>("dropArea"), DropFile);
        scenePathLabel.text = scenePath = EditorPrefs.GetString("BanterBuilder_ScenePath", "");
        LoadKitList();
        if (!string.IsNullOrEmpty(scenePath))
        {
            mode = BanterBuilderBundleMode.Scene;
        }
        else
        {
            if (kitObjectList.Count > 0)
            {
                mode = BanterBuilderBundleMode.Kit;
            }
        }
        RefreshView();

#if BANTER_EDITOR
            rootVisualElement.Q<Button>("allAndInjection").clicked += () =>{
                OnCompileAll.Invoke();
                OnCompileInjection.Invoke();
            };
            rootVisualElement.Q<Button>("visualScript").clicked += () => OnVisualScript.Invoke();// SDKCodeGen.CompileAllComponents();
            rootVisualElement.Q<Button>("allOnly").clicked += () => OnCompileAll.Invoke();// SDKCodeGen.CompileAllComponents();
            rootVisualElement.Q<Button>("clearAll").clicked += () => OnClearAll.Invoke();// SDKCodeGen.ClearAllComponents();
            rootVisualElement.Q<Button>("compileElectron").clicked += () => OnCompileElectron.Invoke();// SDKCodeGen.CompileElectron();
            rootVisualElement.Q<Button>("compileInjection").clicked += () => OnCompileInjection.Invoke();// SDKCodeGen.CompileInjection();
            rootVisualElement.Q<Button>("kitchenSink").clicked += () => OnCompileAll.Invoke();// SDKCodeGen.CompileAll();
            Remove(rootVisualElement.Q<Button>("setupVisualScripting"));
            Remove(rootVisualElement.Q<Button>("setupLayers"));
#else
        Remove(rootVisualElement.Q<Button>("visualScript"));
        Remove(rootVisualElement.Q<Button>("allAndInjection"));
        Remove(rootVisualElement.Q<Button>("allOnly"));
        Remove(rootVisualElement.Q<Button>("clearAll"));
        Remove(rootVisualElement.Q<Button>("compileElectron"));
        Remove(rootVisualElement.Q<Button>("compileInjection"));
        Remove(rootVisualElement.Q<Button>("kitchenSink"));
        rootVisualElement.Q<Button>("setupVisualScripting").clicked += () => _ = InitialiseOnLoad.InstallVisualScripting();
        rootVisualElement.Q<Button>("setupLayers").clicked += () => InitialiseOnLoad.SetupLayers();
#endif
        rootVisualElement.Q<Button>("openDevTools").clicked += () => BanterStarterUpper.ToggleDevTools();

    }

    private void ShowSpaceSlugPlaceholder(Label spaceSlugPlaceholder, string newValue)
    {
        if (!string.IsNullOrEmpty(newValue))
        {
            spaceSlugPlaceholder.style.display = DisplayStyle.None;
        }
        else
        {
            spaceSlugPlaceholder.style.display = DisplayStyle.Flex;
        }
    }

    private IEnumerator UploadWebOnly(Action callback)
    {
        yield return UploadFile("index.html", UploadAssetType.Index, UploadAssetTypePlatform.Any);
        yield return UploadFile("script.js", UploadAssetType.Js, UploadAssetTypePlatform.Any);
        yield return UploadFile("bullshcript.js", UploadAssetType.Js, UploadAssetTypePlatform.Any);
        callback();
    }
    private IEnumerator UploadEverything(Action callback)
    {
        yield return UploadFile("windows.banter", UploadAssetType.AssetBundle, UploadAssetTypePlatform.Windows);
        yield return UploadFile("android.banter", UploadAssetType.AssetBundle, UploadAssetTypePlatform.Android);
        yield return UploadFile("index.html", UploadAssetType.Index, UploadAssetTypePlatform.Any);
        yield return UploadFile("script.js", UploadAssetType.Js, UploadAssetTypePlatform.Any);
        yield return UploadFile("bullshcript.js", UploadAssetType.Js, UploadAssetTypePlatform.Any);
        callback();
    }

    private IEnumerator UploadFile(string name, UploadAssetType type, UploadAssetTypePlatform platform)
    {
        var file = Path.Join(assetBundleRoot, assetBundleDirectory) + "\\" + name;
        if (File.Exists(file))
        {
            AddStatus("Upload started: " + file + "...");
        }
        else
        {
            AddStatus("File not found, skipping: " + file);
            yield break;
        }
        var data = File.ReadAllBytes(file);
        yield return sq.UploadFile(name, data, spaceSlug.text, (text) =>
        {
            AddStatus("Uploaded " + file + " to https://" + spaceSlug.text + ".bant.ing/" + name);
        }, e =>
        {
            AddStatus("FAILED UPLOADING " + file + " to https://" + spaceSlug.text + ".bant.ing/" + name);
            Debug.LogException(e);
        }, type, platform);
    }
    CancellationTokenSource resetDebounce;
    private async Task ResetStatus(CancellationTokenSource cts)
    {
        await Task.Delay(3000);
        if (cts.Token.IsCancellationRequested)
        {
            return;
        }
        statusBar.text = "STATUS: Idle";
    }
    void ClearLogs()
    {
        statusMessages.Clear();
        buildProgress.Rebuild();
    }
    void AddStatus(string text)
    {
        var val = "<color=\"orange\">" + DateTime.Now.ToString("HH:mm:ss") + ": <color=\"white\">" + text;
        statusMessages.Insert(0, val);
        statusBar.text = "STATUS: " + val;
        if (statusMessages.Count > 300)
        {
            statusMessages = statusMessages.GetRange(0, 300);
        }

        buildProgress.Rebuild();
        if (resetDebounce != null && !resetDebounce.Token.IsCancellationRequested)
        {
            resetDebounce.Cancel();
        }
        resetDebounce = new CancellationTokenSource();
        _ = ResetStatus(resetDebounce);
    }

    public void Remove(VisualElement element)
    {
        element.parent.Remove(element);
    }

    private void SaveKitList()
    {
        EditorPrefs.SetString("BanterBuilder_SelectedKitObjects", String.Join(",", kitObjectList.Select(ko => ko.path).ToArray()));
    }

    private void LoadKitList()
    {
        var paths = EditorPrefs.GetString("BanterBuilder_SelectedKitObjects", "").Split(',');
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }
            var obj = GetKitObject(path);
            if (obj == null)
            {
                continue;
            }
            if (!kitObjectList.Any(x => x.path == path))
            {
                kitObjectList.Add(new KitObjectAndPath() { obj = obj, path = path });
            }
        }
    }

    private void DropFile(bool isScene, string sceneFile, string[] paths)
    {
        if (isScene)
        {
            scenePathLabel.text = scenePath = sceneFile;
            mode = BanterBuilderBundleMode.Scene;
        }
        else
        {
            scenePathLabel.text = scenePath = "";
            foreach (var dropped in paths)
            {
                var obj = GetKitObject(dropped);
                if (obj == null)
                {
                    continue;
                }
                if (!kitObjectList.Any(x => x.path == dropped))
                {
                    kitObjectList.Add(new KitObjectAndPath() { obj = obj, path = dropped });
                    SaveKitList();
                }
            }
            if (kitObjectList.Count > 0)
            {
                mode = BanterBuilderBundleMode.Kit;
            }
        }
        EditorPrefs.SetString("BanterBuilder_SelectedKitObjects", String.Join(",", kitObjectList.Select(ko => ko.path).ToArray()));
        EditorPrefs.SetString("BanterBuilder_ScenePath", scenePath);
        RefreshView();
    }

    private UnityEngine.Object GetKitObject(string path)
    {
        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (obj == null)
        {
            Debug.LogWarning("Couldn't load asset at path " + path);
            return null;
        }
        if (!KitObjectAndPath.ALLOWED_KIT_TYPES.Contains(obj.GetType()))
        {
            Debug.LogWarning($"Asset at path {path} isn't a valid kit bundle object type, it is {obj.GetType().Name}.  Allowed types are: {string.Join(", ", KitObjectAndPath.ALLOWED_KIT_TYPES.Select(x => x.Name))}");
            return null;
        }
        return obj;
    }
    void RemoveSelectedObjects()
    {
        foreach (var sel in kitListView.selectedItems.Cast<KitObjectAndPath>())
        {
            kitObjectList.Remove(sel);
            SaveKitList();
        }
        kitListView.ClearSelection();
        RefreshView();
    }
    private void ShowRemoveSelected()
    {
        removeSelected.style.display = kitListView.selectedIndices.Count() > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }
    private void RefreshView()
    {
        scenePathLabel.style.display = DisplayStyle.None;
        kitListView.style.display = DisplayStyle.None;
        removeSelected.style.display = DisplayStyle.None;
        mainTitle.style.display = DisplayStyle.None;
        if (mode == BanterBuilderBundleMode.Kit && kitObjectList.Count > 0)
        {
            mainTitle.text = "<color=\"white\">Build Mode:</color> Prefab Asset Bundle (Kit)";
            mainTitle.style.display = DisplayStyle.Flex;
            removeSelected.style.display = DisplayStyle.Flex;
            kitListView.style.display = DisplayStyle.Flex;

            kitListView.itemsSource = kitObjectList;
            kitListView.Rebuild();
        }
        else if (mode == BanterBuilderBundleMode.Scene)
        {
            mainTitle.text = "<color=\"white\">Build Mode:</color> Scene Asset Bundle";
            mainTitle.style.display = DisplayStyle.Flex;
            scenePathLabel.style.display = DisplayStyle.Flex;
            scenePathLabel.text = "<color=\"white\">Scene Path:</color> " + scenePath;
            // button to open the webroot folder - highlight in unity.
        }
        else
        {
            mainTitle.style.display = DisplayStyle.None;
            mainTitle.text = "";
        }
        ShowRemoveSelected();
        ShowUploadToggle();
    }

    public void OpenSpaceCreation() {
        Application.OpenURL("https://sidequestvr.com/account/create-space");
    }
    private void BuildAssetBundles()
    {
        if (mode == BanterBuilderBundleMode.None)
        {
            AddStatus("Nothing to build...");
            return;
        }
        if (mode == BanterBuilderBundleMode.Scene && string.IsNullOrEmpty(scenePath))
        {
            AddStatus("No scene selected...");
            return;
        }
        if (mode == BanterBuilderBundleMode.Kit && kitObjectList.Count < 1)
        {
            AddStatus("No objects selected...");
            return;
        }
        AddStatus("Build started...");

        if (!Directory.Exists(Path.Join(assetBundleRoot, assetBundleDirectory)))
        {
            Directory.CreateDirectory(Path.Join(assetBundleRoot, assetBundleDirectory));
        }


        if (mode == BanterBuilderBundleMode.None)
        {
            throw new Exception("Nothing to build!");
        }
        else if (mode == BanterBuilderBundleMode.Scene && string.IsNullOrWhiteSpace(scenePath))
        {
            throw new Exception("No scene to build!");
        }
        else if (mode == BanterBuilderBundleMode.Kit && kitObjectList.Count < 1)
        {
            throw new Exception("No kit objects to build!");
        }
        buildProgressBar.value = 25;
        List<string> names = new List<string>();
        for (int i = 0; i < buildTargets.Length; i++)
        {
            buildProgressBar.value += 25;
            if (buildTargetFlags[i])
            {
                string newAssetBundleName = "bundle";
                string platform = buildTargets[i].ToString().ToLower();
                AssetBundleBuild abb = new AssetBundleBuild();

                if (mode == BanterBuilderBundleMode.Scene)
                {
                    string[] parts = scenePath.Split("/");// parts[parts.Length - 1].Split(".")[0].ToLower() + "_" +
                    newAssetBundleName = (platform == "standalonewindows" ? "windows" : "android") + ".banter"; // + "_" + DateTime.Now.ToString("dd-MM-yyyy_hh-mm-ss")
                    AddStatus("Building: " + newAssetBundleName);

                    AssetImporter.GetAtPath(scenePath).SetAssetBundleNameAndVariant(newAssetBundleName, string.Empty);
                    abb.assetNames = new[] { scenePath };
                }
                else if (mode == BanterBuilderBundleMode.Kit)
                {
                    newAssetBundleName = "kitbundle_" + platform + ".banter";
                    AddStatus("Building: " + newAssetBundleName);
                    abb.assetNames = kitObjectList.Select(x => x.path).ToArray();
                }
                else
                {
                    continue;
                }
                abb.assetBundleName = newAssetBundleName;
                CustomSceneProcessor.isBuildingAssetBundles = true;
                BuildPipeline.BuildAssetBundles(Path.Join(assetBundleRoot, assetBundleDirectory), new[] { abb }, BuildAssetBundleOptions.None, buildTargets[i]);
                CustomSceneProcessor.isBuildingAssetBundles = false;
                names.Add(newAssetBundleName);
                if (File.Exists(Path.Join(assetBundleRoot, assetBundleDirectory) + "/" + newAssetBundleName + ".manifest"))
                {
                    File.Delete(Path.Join(assetBundleRoot, assetBundleDirectory) + "/" + newAssetBundleName + ".manifest");
                }

            }
        }

        if (File.Exists(Path.Join(assetBundleRoot, assetBundleDirectory) + "/" + assetBundleDirectory + ".manifest"))
        {
            File.Delete(Path.Join(assetBundleRoot, assetBundleDirectory) + "/" + assetBundleDirectory + ".manifest");
        }
        if (File.Exists(Path.Join(assetBundleRoot, assetBundleDirectory) + "/" + assetBundleDirectory))
        {
            File.Delete(Path.Join(assetBundleRoot, assetBundleDirectory) + "/" + assetBundleDirectory);
        }
        if (names.Count > 0)
        {
            EditorUtility.RevealInFinder(Path.Join(assetBundleRoot, assetBundleDirectory) + "/" + names[0]);
        }
        buildProgressBar.value = 100;
        var task = new Task(async () =>
        {
            await Task.Delay(5000);
            HideProgressBar();
        });
        task.Start();
        if (mode == BanterBuilderBundleMode.Kit)
        {
            AddStatus("Writing kit items to " + Path.Join(assetBundleRoot, assetBundleDirectory) + "/kit_items.txt.");
            File.WriteAllText(Path.Join(assetBundleRoot, assetBundleDirectory) + "/kit_items.txt", String.Join("\n", kitObjectList.Select(x => x.path.ToLower()).ToArray()));
        }
        AddStatus("Build finished.");
        if (autoUpload.value && sq.User != null && mode == BanterBuilderBundleMode.Scene)
        {
            if (string.IsNullOrEmpty(spaceSlug.text))
            {
                AddStatus("No space subdomain specified, skipping upload.");
                return;
            }
            uploadWebOnly.SetEnabled(false);
            uploadEverything.SetEnabled(false);
            EditorCoroutineUtility.StartCoroutine(UploadEverything(() =>
            {
                AddStatus("Upload complete.");
                uploadWebOnly.SetEnabled(true);
                uploadEverything.SetEnabled(true);
            }), this);
        }
    }
    void HideProgressBar()
    {
        buildProgressBar.style.display = DisplayStyle.None;
    }

}
