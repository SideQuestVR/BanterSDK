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
using UnityEngine.Networking;
using UnityEditor.UIElements;
using System.Text.RegularExpressions;

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
    public Texture2D texture;
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

    public const string SQ_API_CLIENT_ID = "client_0e4c67f9a6bbe12143870312";

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
    Button uploadWebOnly;
    Label uploadEverything;

    Toggle autoUpload;

    VisualElement loggedInView;
    VisualElement loggedInViewScene;
    VisualElement loggedInViewPrefab;

    DropdownField existingDropDown;
    DropdownField kitCategoryDropDown;
    Label numberOfItems;

    KitCategory[] kitCategories;

    TextField kitName;
    TextField kitDescription;

    ObjectField markitCoverImage;
    Label uploadEverythingKit;
    Button uploadWebOnlyKit;
    Button deleteKit;
    Label confirmBuildMode;
    Label confirmSceneFile;
    Label confirmSpaceCode;
    Label confirmKitBundle;
    Label confirmKitBundleID;
    Label confirmKitNumber;

    Label confirmBuild;
    Button cancelBuild;

    VisualElement buildConfirm;

    VisualElement deleteConfirm;
    Label confirmDelete;
    Button cancelDelete;

   
    Kit[] myKits; 
    string selectedKitId;
    string assetBundleRoot = "Assets";
    string assetBundleDirectory = "WebRoot";
    List<string> statusMessages = new List<string>();

    [MenuItem("Banter/Build Space")]
    public static void ShowMainWindow()
    {
        BuilderWindow window = GetWindow<BuilderWindow>();
        window.minSize = new Vector2(450, 200);
        window.titleContent = new GUIContent("Banter Space");
    }

    [MenuItem("Banter/Build Avatar(Select Avatar GameObject)")]
    public static void SetupAvatar()
    {
        BanterAvatar avatar = Selection.activeGameObject?.GetComponent<BanterAvatar>();
        if (avatar == null)
        {
            Selection.activeGameObject?.AddComponent<BanterAvatar>();
        }
    }

    private SqEditorAppApi sq;

    public void OnDisable()
    {
        loginManager?.StopPolling();
    }

    void ShowWebRoot()
    {
        string path = Path.Join(assetBundleRoot, assetBundleDirectory);

        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath(path, typeof(UnityEngine.Object));

        Selection.activeObject = obj;

        EditorGUIUtility.PingObject(obj);
    }

    LoginManager loginManager;

    Status status;

    public void OnEnable()
    {
        VisualElement content = _mainWindowVisualTree.CloneTree();
        content.style.height = new StyleLength(Length.Percent(100));
        rootVisualElement.styleSheets.Add(_mainWindowStyleSheet);
        rootVisualElement.Add(content);
        SqEditorAppApiConfig config = new SqEditorAppApiConfig(SQ_API_CLIENT_ID, Application.persistentDataPath, false);
        sq = new SqEditorAppApi(config);
        SetupUI();
        status = new Status(statusBar, buildProgress, buildProgressBar);
        loginManager = new LoginManager(sq, autoUpload, codeText, loggedInView, statusText, buildButton, rootVisualElement.Q<Label>("SignOut"));
        loginManager.SetLoginState();
        loginManager.SetBuildButtonText();
        if (sq.User != null)
        {
            loginManager.RefreshUser();
        }
        else
        {
            loginManager.GetCode();
        }
        RefreshView();
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

    // void SetBuildButtonText()
    // {
    //     buildButton.text = autoUpload.value && sq.User != null ? "Build & Upload it Now!" : "Build it Now!";
    // }

     public IEnumerator Texture(string url, Action<Texture2D> callback)
        {
            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(url))
            {
                yield return uwr.SendWebRequest();
                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    throw new System.Exception(uwr.error);
                }
                else
                {
                    callback(DownloadHandlerTexture.GetContent(uwr));
                }
            }
        }

    void SelectKit(int selectedIndex) {
        if(myKits == null || myKits.Length == 0 || selectedIndex == myKits.Length) {
            kitName.value = "";
            kitDescription.value = "";
            selectedKitId = "";
            kitCategoryDropDown.index = -1;
            markitCoverImage.value = null;
            uploadWebOnlyKit.style.display = DisplayStyle.None;
            deleteKit.style.display = DisplayStyle.None;
            return;
        }
        uploadWebOnlyKit.style.display = DisplayStyle.Flex;
        deleteKit.style.display = DisplayStyle.Flex;
        selectedKitId = myKits[selectedIndex].id;
        kitName.value = myKits[selectedIndex].name;
        kitDescription.value = myKits[selectedIndex].description;
        kitCategoryDropDown.index = -1;
        kitCategoryDropDown.index = kitCategories.ToList().IndexOf(kitCategories.First(k => k.id == myKits[selectedIndex].kit_categories_id));
        EditorCoroutineUtility.StartCoroutine(Texture(myKits[selectedIndex].picture, tex => {
            markitCoverImage.value = CopyIt(tex);
        }), this);
    }
    Label buildButton;
    Action confirmCallback;
    Action deleteCallback;
    private void SetupUI()
    {
        statusBar = rootVisualElement.Q<Label>("StatusBar");
        new TabsManager(rootVisualElement);
        buildButton = rootVisualElement.Q<Label>("buildButton");

        buildButton.RegisterCallback<MouseUpEvent>((e) => BuildAssetBundles());

        var createSpace = rootVisualElement.Q<Label>("CreateSpace");
        createSpace.RegisterCallback<MouseUpEvent>((e) => OpenSpaceCreation());
        var openWebRoot = rootVisualElement.Q<Button>("OpenWebRoot");

        openWebRoot.clicked += () => ShowWebRoot();

        var clearLogs = rootVisualElement.Q<Button>("clearLogs");

        clearLogs.clicked += () => status.ClearLogs();

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
        
        autoUpload.RegisterCallback<MouseUpEvent>((e) =>
        {
            EditorPrefs.SetBool("BanterBuilder_AutoUpload", autoUpload.value);
            loginManager.SetBuildButtonText();
        });

        markitCoverImage = rootVisualElement.Q<ObjectField>("MarkitCoverImage");
        existingDropDown = rootVisualElement.Q<DropdownField>("ExistingDropDown");
        var kitSelectPlaceholder = rootVisualElement.Q<Label>("KitSelectPlaceholder");
        existingDropDown.RegisterValueChangedCallback((e) =>
        {
           ShowSpaceSlugPlaceholder(kitSelectPlaceholder, e.newValue);
           SelectKit(existingDropDown.index);

        });

        numberOfItems = rootVisualElement.Q<Label>("NumberOfItems");
        kitCategoryDropDown = rootVisualElement.Q<DropdownField>("KitCategoryDropDown");
        var kitCategoryPlaceholder = rootVisualElement.Q<Label>("KitCategoryPlaceholder");
        kitCategoryDropDown.RegisterValueChangedCallback((e) =>
        {
            ShowSpaceSlugPlaceholder(kitCategoryPlaceholder, e.newValue);
        });
        EditorCoroutineUtility.StartCoroutine(Json<KitCategoryRows>("https://screen.sdq.st:2096/kit/categories", categories => {
            kitCategories = categories.rows;
            kitCategoryDropDown.choices = categories.rows.Select(k => k.name).ToList();
        }), this);
        var spaceSlugPlaceholder = rootVisualElement.Q<Label>("SpaceSlugPlaceholder");
        
        kitName = rootVisualElement.Q<TextField>("KitName");
        var kitNamePlaceholder = rootVisualElement.Q<Label>("KitNamePlaceholder");
        kitName.RegisterValueChangedCallback((e) =>
        {
            ShowSpaceSlugPlaceholder(kitNamePlaceholder, e.newValue);
        });
        kitDescription = rootVisualElement.Q<TextField>("KitDescription");
        var kitDescPlaceholder = rootVisualElement.Q<Label>("KitDescPlaceholder");
        kitDescription.RegisterValueChangedCallback((e) =>
        {
            ShowSpaceSlugPlaceholder(kitDescPlaceholder, e.newValue);
        });
        confirmBuildMode = rootVisualElement.Q<Label>("ConfirmBuildMode");
        confirmSceneFile = rootVisualElement.Q<Label>("ConfirmSceneFile");
        confirmSpaceCode = rootVisualElement.Q<Label>("ConfirmSpaceCode");
        confirmKitBundle = rootVisualElement.Q<Label>("ConfirmKitBundle");
        confirmKitBundleID = rootVisualElement.Q<Label>("ConfirmKitBundleID");
        confirmKitNumber = rootVisualElement.Q<Label>("ConfirmKitNumber");


        deleteConfirm = rootVisualElement.Q<VisualElement>("DeleteConfirm");

        confirmDelete = rootVisualElement.Q<Label>("ConfirmDelete");
        cancelDelete = rootVisualElement.Q<Button>("CancelDelete");
        cancelDelete.RegisterCallback<MouseUpEvent>((e) => {
            deleteConfirm.style.display = DisplayStyle.None;
        });
        confirmDelete.RegisterCallback<MouseUpEvent>((e) => {
            deleteConfirm.style.display = DisplayStyle.None;
            deleteCallback?.Invoke();
        });
        buildConfirm = rootVisualElement.Q<VisualElement>("BuildConfirm");


        confirmBuild = rootVisualElement.Q<Label>("ConfirmBuild");
        cancelBuild = rootVisualElement.Q<Button>("CancelBuild");
        cancelBuild.RegisterCallback<MouseUpEvent>((e) => {
            buildConfirm.style.display = DisplayStyle.None;
        });
        confirmBuild.RegisterCallback<MouseUpEvent>((e) => {
            buildConfirm.style.display = DisplayStyle.None;
            confirmCallback?.Invoke();
        });

        
        EditorCoroutineUtility.StartCoroutine(PopulateExistingKits(), this);

        codeText = rootVisualElement.Q<Label>("LoginCode");
        spaceSlug = rootVisualElement.Q<TextField>("SpaceSlug");
        statusText = rootVisualElement.Q<Label>("SignedInStatus");
        uploadWebOnly = rootVisualElement.Q<Button>("UploadWebOnly");
        uploadWebOnlyKit = rootVisualElement.Q<Button>("UploadWebOnlyKit");
        deleteKit = rootVisualElement.Q<Button>("DeleteKit");
        uploadEverything = rootVisualElement.Q<Label>("UploadEverything");
        uploadEverythingKit = rootVisualElement.Q<Label>("UploadEverythingKit");
        loggedInView = rootVisualElement.Q<VisualElement>("LoggedInView");
        loggedInViewScene = rootVisualElement.Q<VisualElement>("LoggedInViewScene");
        loggedInViewPrefab = rootVisualElement.Q<VisualElement>("LoggedInViewPrefab");

        spaceSlug.RegisterValueChangedCallback((e) =>
        {
            ShowSpaceSlugPlaceholder(spaceSlugPlaceholder, e.newValue);
            EditorPrefs.SetString("BanterBuilder_spaceSlug", e.newValue);
        });

        spaceSlug.value = EditorPrefs.GetString("BanterBuilder_spaceSlug", "");
        ShowSpaceSlugPlaceholder(spaceSlugPlaceholder, spaceSlug.value);
        uploadWebOnly.clicked += () =>
        {
            if (string.IsNullOrEmpty(spaceSlug.text)){
                status.AddStatus("No space slug provided, please enter a slug.");
                return;
            }
            ShowBuildConfirm();
            confirmCallback = () => {
                uploadWebOnly.SetEnabled(false);
                uploadEverything.SetEnabled(false);
                EditorCoroutineUtility.StartCoroutine(UploadWebOnly(() =>
                {
                    status.AddStatus("Upload complete.");
                    uploadWebOnly.SetEnabled(true);
                    uploadEverything.SetEnabled(true);
                }), this);
            };
        };

        uploadEverything.RegisterCallback<MouseUpEvent>((e) =>
        {
            if (string.IsNullOrEmpty(spaceSlug.text)){
                status.AddStatus("No space slug provided, please enter a slug.");
                return;
            }
            ShowBuildConfirm();
            confirmCallback = () => {
                confirmCallback = null;
                uploadWebOnly.SetEnabled(false);
                uploadEverything.SetEnabled(false);
                EditorCoroutineUtility.StartCoroutine(UploadEverything(() =>
                {
                    status.AddStatus("Upload complete.");
                    uploadWebOnly.SetEnabled(true);
                    uploadEverything.SetEnabled(true);
                }), this);
            };
        });

        uploadEverythingKit.RegisterCallback<MouseUpEvent>((e) => {
            autoUpload.value = true;
            BuildAssetBundles();
        });
        
        uploadWebOnlyKit.RegisterCallback<MouseUpEvent>((e) => {
            autoUpload.value = true;
            BuildAssetBundles(true);
        });

        deleteKit.RegisterCallback<MouseUpEvent>((e) => {
            deleteConfirm.style.display = DisplayStyle.Flex;
            deleteCallback = () => {
                if (string.IsNullOrEmpty(selectedKitId)){
                    status.AddStatus("No kit selected, please select a kit.");
                    return;
                }
                deleteKit.SetEnabled(false);
                EditorCoroutineUtility.StartCoroutine(DeleteKit(() =>
                {
                    status.AddStatus("Deleted kit.");
                    deleteKit.SetEnabled(true);
                }), this);
            };
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
            ele.style.flexDirection = FlexDirection.Row;
            ele.style.justifyContent = Justify.SpaceBetween;
            var label = new Label
            {
                name = "kitItemName"
            };
            var button = new Button
            {
                name = "kitItemCopy"
            };
            var image = new VisualElement
            {
                name = "kitItemImage"
            };
            image.style.width = 50;
            image.style.height = 50;
            image.style.flexShrink = 0;
            label.style.color = new Color(0.560f, 0.560f, 0.560f);
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.fontSize = 16;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            
            label.style.width = new StyleLength(Length.Percent(100));
            label.style.flexShrink = 1;
            button.style.paddingBottom = button.style.paddingTop = 0;
            button.style.paddingLeft = button.style.paddingRight = 2;
            button.style.borderTopRightRadius = button.style.borderBottomRightRadius = button.style.borderTopLeftRadius = button.style.borderBottomLeftRadius = 8;
            button.style.marginTop = button.style.marginLeft = button.style.marginRight = 0;
            button.style.marginBottom = 2;
            button.style.flexShrink = 0;
            ele.Add(image);
            ele.Add(label);
            ele.Add(button);
            ele.AddToClassList("unity-label-margin");
            return ele;
        };

        SnapshotCamera snapshotCamera = FindAnyObjectByType<SnapshotCamera>();
        
        kitListView.bindItem = (e, i) =>
        {
            var name = kitObjectList[i].path.ToLower();
            if(kitObjectList[i].texture == null) {
                
                if(snapshotCamera == null) {
                    snapshotCamera = SnapshotCamera.MakeSnapshotCamera(0);
                }
                kitObjectList[i].texture = snapshotCamera.TakePrefabSnapshot((GameObject)kitObjectList[i].obj);
            }
            var text = e.Q<Label>("kitItemName");
            text.text = i + 1 + ". " + name;
            var image = e.Q<VisualElement>("kitItemImage");
            image.style.backgroundImage = new StyleBackground(kitObjectList[i].texture);
            image.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            var button = e.Q<Button>("kitItemCopy");
            button.text = "copy";
            button.clicked += () =>
            {
                status.AddStatus("Copied path to clipboard: " + name);
                GUIUtility.systemCopyBuffer = name;
            };
        };
        kitListView.selectionType = SelectionType.Multiple;
        kitListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
        kitListView.reorderMode = ListViewReorderMode.Simple;
        new DragAndDropStuff().SetupDropArea(rootVisualElement.Q<VisualElement>("dropArea"), DropFile);
        new DragAndDropStuff().SetupDropArea(rootVisualElement.Q<VisualElement>("dropRecordingArea"), DropRecordingFile);
        scenePathLabel.text = scenePath = EditorPrefs.GetString("BanterBuilder_ScenePath", "");
        LoadKitList();
        if (!string.IsNullOrEmpty(scenePath))
        {
            mode = BanterBuilderBundleMode.Scene;
            loggedInViewPrefab.style.display = DisplayStyle.None;
            loggedInViewScene.style.display = DisplayStyle.Flex;
        }
        else
        {
            if (kitObjectList.Count > 0)
            {
                mode = BanterBuilderBundleMode.Kit;
                loggedInViewPrefab.style.display = DisplayStyle.Flex;
                loggedInViewScene.style.display = DisplayStyle.None;
            }
        }

#if BANTER_EDITOR
            rootVisualElement.Q<Button>("allAndInjection").clicked += () =>{
                OnCompileAll.Invoke();
                OnCompileInjection.Invoke();
            };
            rootVisualElement.Q<Button>("allOnly").clicked += () => OnCompileAll.Invoke();// SDKCodeGen.CompileAllComponents();
            rootVisualElement.Q<Button>("clearAll").clicked += () => OnClearAll.Invoke();// SDKCodeGen.ClearAllComponents();
            rootVisualElement.Q<Button>("compileElectron").clicked += () => OnCompileElectron.Invoke();// SDKCodeGen.CompileElectron();
            rootVisualElement.Q<Button>("compileInjection").clicked += () => OnCompileInjection.Invoke();// SDKCodeGen.CompileInjection();
            rootVisualElement.Q<Button>("kitchenSink").clicked += () => OnCompileAll.Invoke();// SDKCodeGen.CompileAll();
            Remove(rootVisualElement.Q<Button>("setupLayers"));

#else
        Remove(rootVisualElement.Q<Button>("allAndInjection"));
        Remove(rootVisualElement.Q<Button>("allOnly"));
        Remove(rootVisualElement.Q<Button>("clearAll"));
        Remove(rootVisualElement.Q<Button>("compileElectron"));
        Remove(rootVisualElement.Q<Button>("compileInjection"));
        Remove(rootVisualElement.Q<Button>("kitchenSink"));

        rootVisualElement.Q<Button>("setupLayers").clicked += () => InitialiseOnLoad.SetupLayersAndTags();
#endif

#if BANTER_VISUAL_SCRIPTING

#if BANTER_EDITOR
        rootVisualElement.Q<Button>("visualScript").clicked += () => OnVisualScript.Invoke();// SDKCodeGen.CompileAllComponents();
#else // BANTER_EDITOR
        rootVisualElement.Q<Button>("visualScript").clicked += () => VsNodeGeneration.SetVSTypesAndAssemblies();
#endif // BANTER_EDITOR

#else // BANTER_VISUAL_SCRIPTING
        Remove(rootVisualElement.Q<Button>("visualScript"));
#endif // BANTER_VISUAL_SCRIPTING
        rootVisualElement.Q<Button>("openDevTools").clicked += () => BanterStarterUpper.ToggleDevTools();
    }
    public IEnumerator Json<T>(string url, Action<T> callback)
    {
        UnityWebRequest uwr = UnityWebRequest.Get(url);
        yield return uwr.SendWebRequest();
        if (uwr.result != UnityWebRequest.Result.Success)
        {
            throw new System.Exception(uwr.error);
        }
        else
        {
            callback(JsonUtility.FromJson<T>(uwr.downloadHandler.text));
        }
    }
    public IEnumerator Json<T>(string url, T postData, Action<string> callback, Dictionary<string, string> headers = null)
    {
        UnityWebRequest uwr = UnityWebRequest.Put(url, JsonUtility.ToJson(postData));
        uwr.method = "POST";
        if (headers != null)
        {
            foreach (var header in headers)
            {
                uwr.SetRequestHeader(header.Key, header.Value);
            }
        }
        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError(url + ":" + JsonUtility.ToJson(postData));
            throw new System.Exception(uwr.error);
        }
        else
        {
            callback(uwr.downloadHandler.text);
        }
    }
    private IEnumerator PopulateExistingKits(Action callback = null) {
        if(sq.User == null) {
            yield break;
        }
        yield return Json<KitRows>("https://screen.sdq.st:2096/kits/user/" + sq.User.UserId, kit => {
            myKits = kit.rows;
            if(kit.rows.Length != 0) {
                existingDropDown.choices = kit.rows.Select(k => k.id + ": " + k.name).ToList().Concat(new List<string>{"Create New..."}).ToList();
            }else{
                existingDropDown.choices = new List<string>{"Create New..."};
            }
            callback?.Invoke();
        });
    }
    bool KitUserCreated = false;
    private IEnumerator CreateKitUser() {
        var headers = new Dictionary<string, string>{
            { "Content-Type", "application/json" },
        };  
        var kitUser = new KitUser{
            ext_id = sq.User.UserId.ToString(),
            name = sq.User.Name,
            bio = sq.User.TagLine,
            profile_pic = "https://cdn.sidequestvr.com/" + sq.User.PreviewImageUrl
        };
        yield return Json("https://screen.sdq.st:2096/user", kitUser, resp => {
            var kitUserResponse = JsonUtility.FromJson<KitUserRows>(resp);
            if(kitUserResponse.rows.Length == 0) {
                status.AddStatus("Failed to create kit user, are you online?");
                return;
            }
            KitUserCreated = true;
        }, headers);
    }
    private IEnumerator CheckKitUserExists() {
        if(sq.User == null || KitUserCreated) {
            yield break;
        }
        yield return Json<KitUserRows>("https://screen.sdq.st:2096/user/" + sq.User.UserId, user => {
            if(user.rows.Length == 0) {
                EditorCoroutineUtility.StartCoroutine(CreateKitUser(), this);
            }else{
                KitUserCreated = true;
            }
        });
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
        EditorUtility.DisplayProgressBar("Banter Upload", "Uploading web files...", 0.1f);
        yield return UploadFileToCommunity("index.html", UploadAssetType.Index, UploadAssetTypePlatform.Any);
        yield return UploadFileToCommunity("script.js", UploadAssetType.Js, UploadAssetTypePlatform.Any);
        yield return UploadFileToCommunity("bullshcript.js", UploadAssetType.Js, UploadAssetTypePlatform.Any);
        EditorUtility.DisplayProgressBar("Banter Upload", "Uploaded", 0.99f);
        callback();
        EditorUtility.ClearProgressBar();
    }
    private Texture2D CopyIt(Texture2D source) {
        RenderTexture renderTex = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.Default,
                    source.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
        
        Graphics.Blit(source, renderTex);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTex;
        Texture2D readableText = new Texture2D(source.width, source.height);
        readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
        readableText.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTex);
        return readableText;
    }

    private string GetKitName() {
        return Regex.Replace(kitName.text, "[^A-Za-z0-9-]", "");
    }

    private IEnumerator UploadKit(Action callback, bool skipUpload = false) {
        EditorUtility.DisplayProgressBar("Banter Upload", skipUpload ? "Updating Kit details..." : "Uploading kitbundle_standalonewindows_" + GetKitName() + ".banter...", 0.1f);
        long androidFileId = 0;
        long windowsFileId = 0;
        long coverFileId = 0;
        long[] imageIds = new long[kitObjectList.Count];
        if(!skipUpload) {
            yield return UploadFile("kitbundle_standalonewindows_" + GetKitName() + ".banter", null, fileId => windowsFileId = fileId);
            EditorUtility.DisplayProgressBar("Banter Upload", "Uploading kitbundle_android_" + GetKitName() + ".banter...", 0.5f);
            yield return UploadFile("kitbundle_android_" + GetKitName() + ".banter", null, fileId => androidFileId = fileId);
            EditorUtility.DisplayProgressBar("Banter Upload", "Uploaded", 0.99f);
        }

        yield return UploadFile("cover_image.png", CopyIt((Texture2D)markitCoverImage.value).EncodeToPNG(), fileId => coverFileId = fileId);

        for(int i = 0; i < kitObjectList.Count; i++) {
            // TODO this sucks - Replace with something bespoke like this: https://gist.github.com/mickdekkers/5c3c62539c057010d4497f9865060e20
            yield return UploadFile("prefab_image.png", kitObjectList[i].texture.EncodeToPNG(), fileId => imageIds[i] = fileId);
        }

        string createdKitId = null;
        var headers = new Dictionary<string, string>{
            { "Content-Type", "application/json" },
        };  
        yield return Json("https://screen.sdq.st:2096/kit", new Kit{
            name = kitName.value,
            description = kitDescription.value,
            kit_categories_id = kitCategories[kitCategoryDropDown.index].id,
            users_id = sq.User.UserId.ToString(),
            id = selectedKitId,
            access_token = sq.Data.Token.AccessToken,
            picture = "https://cdn.sidequestvr.com/file/" + coverFileId.ToString() + "/kitbundle_cover_image.png",
            windows = skipUpload ? myKits[existingDropDown.index].windows : "https://cdn.sidequestvr.com/file/" + windowsFileId.ToString() + "/kitbundle_standalonewindows_" + GetKitName() + ".banter",
            android = skipUpload ? myKits[existingDropDown.index].android : "https://cdn.sidequestvr.com/file/" + androidFileId.ToString() + "/kitbundle_android_" + GetKitName() + ".banter",
            items = kitObjectList.Select(ko => new KitItem{
                name = ko.obj.name,
                picture = "https://cdn.sidequestvr.com/file/" + imageIds[kitObjectList.IndexOf(ko)].ToString() + "/kitbundle_prefab_image.png",
                path = ko.path,
            }).ToArray(),
        }, resp => {
            var kitResponse = JsonUtility.FromJson<KitRows>(resp);
            createdKitId = kitResponse.rows[0].id;
        }, headers);

        status.AddStatus("Uploaded kit to Banter Markit");
        EditorCoroutineUtility.StartCoroutine(PopulateExistingKits(), this);
        callback();
        EditorUtility.ClearProgressBar();

    }
    private IEnumerator DeleteKit(Action callback) {
        if(string.IsNullOrEmpty(selectedKitId)) {
            status.AddStatus("No kit selected, please select a kit.");
            yield break;
        }
        var headers = new Dictionary<string, string>{
            { "Content-Type", "application/json" },
        };  
        var kit = new Kit();
        kit.users_id = sq.User.UserId.ToString();
        kit.access_token = sq.Data.Token.AccessToken;
        yield return Json("https://screen.sdq.st:2096/kit/delete/" + selectedKitId, kit, resp => {
            EditorCoroutineUtility.StartCoroutine(PopulateExistingKits(()=>{
                SelectKit(myKits.Length);
                try{
                    existingDropDown.index = myKits.Length;
                }catch{}
                uploadWebOnlyKit.style.display = DisplayStyle.None;
                deleteKit.style.display = DisplayStyle.None;
                status.AddStatus("Deleted kit from Banter Markit");
                callback();
            }), this);
            
        }, headers);
    }
    private IEnumerator UploadEverything(Action callback)
    {
        EditorUtility.DisplayProgressBar("Banter Upload", "Uploading everything...", 0.1f);
        yield return UploadFileToCommunity("windows.banter", UploadAssetType.AssetBundle, UploadAssetTypePlatform.Windows);
        EditorUtility.DisplayProgressBar("Banter Upload", "Uploaded windows.banter...", 0.5f);
        yield return UploadFileToCommunity("android.banter", UploadAssetType.AssetBundle, UploadAssetTypePlatform.Android);
        EditorUtility.DisplayProgressBar("Banter Upload", "Uploaded android.banter...", 0.9f);
        yield return UploadFileToCommunity("index.html", UploadAssetType.Index, UploadAssetTypePlatform.Any);
        EditorUtility.DisplayProgressBar("Banter Upload", "Uploaded index.html...", 0.92f);
        yield return UploadFileToCommunity("script.js", UploadAssetType.Js, UploadAssetTypePlatform.Any);
        EditorUtility.DisplayProgressBar("Banter Upload", "Uploaded script.js...", 0.95f);
        yield return UploadFileToCommunity("bullshcript.js", UploadAssetType.Js, UploadAssetTypePlatform.Any);
        EditorUtility.DisplayProgressBar("Banter Upload", "Uploaded bullshcript.js...", 0.99f);
        callback();
        EditorUtility.ClearProgressBar();
    }

    private IEnumerator UploadFile(string name, byte[] bytes = null, Action<long> callback = null)
    {
        var file = Path.Join(assetBundleRoot, assetBundleDirectory) + "\\" + name;
        if (File.Exists(file) || bytes != null)
        {
            status.AddStatus("Upload started: " + file + "...");
        }
        else
        {
            status.AddStatus("File not found, skipping: " + file);
            yield break;
        }
        var data = bytes == null ? File.ReadAllBytes(file) : bytes;
        yield return sq.UploadFile(name, data, "", (text) =>
        {
            callback?.Invoke(text.FileId);
            status.AddStatus("Uploaded " + name + " to Banter Markit");
        }, e =>
        {
            status.AddStatus("FAILED UPLOADING " + name + " to Banter Markit");
            Debug.LogException(e);
        });
    }

    private IEnumerator UploadFileToCommunity(string name, UploadAssetType type, UploadAssetTypePlatform platform)
    {
        var file = Path.Join(assetBundleRoot, assetBundleDirectory) + "\\" + name;
        if (File.Exists(file))
        {
            status.AddStatus("Upload started: " + file + "...");
        }
        else
        {
            status.AddStatus("File not found, skipping: " + file);
            yield break;
        }
        var data = File.ReadAllBytes(file);
        yield return sq.UploadFileToCommunity(name, data, spaceSlug.text, (text) =>
        {
            status.AddStatus("Uploaded " + file + " to https://" + spaceSlug.text + ".bant.ing/" + name);
        }, e =>
        {
            status.AddStatus("FAILED UPLOADING " + file + " to https://" + spaceSlug.text + ".bant.ing/" + name);
            Debug.LogException(e);
        }, type, platform);
    }
    // void ClearLogs()
    // {
    //     statusMessages.Clear();
    //     buildProgress.Rebuild();
    // }
    // void AddStatus(string text)
    // {
    //     var val = "<color=\"orange\">" + DateTime.Now.ToString("HH:mm:ss") + ": <color=\"white\">" + text;
    //     statusMessages.Insert(0, val);
    //     statusBar.text = "STATUS: " + val;
    //     if (statusMessages.Count > 300)
    //     {
    //         statusMessages = statusMessages.GetRange(0, 300);
    //     }

    //     buildProgress.Rebuild();
    // }

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
        numberOfItems.text = "Number of items: " + kitObjectList.Count;
    }

    private void DropRecordingFile(bool isScene, string sceneFile, string[] paths)
    {
        string trackingData = null;
        string prefab = null;
        try
        {
            trackingData = paths.First(x => x.EndsWith(".trackingdata"));
            prefab = paths.First(x => x.EndsWith(".prefab"));
        }
        catch
        {
            status.AddStatus("Tracking or prefab files not found in dropped files.");
            return;
        }
        var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
        var bytes = File.ReadAllBytes(trackingData);
        AvatarUtilities.ParseAnimationCurves(bytes);
        AvatarUtilities.SetBonePaths(avatar, (t) => { });
        AvatarUtilities.SetAnimationCurves();
        AssetDatabase.CreateAsset(AvatarUtilities.clip, trackingData.Replace(".trackingdata", ".anim"));
        status.AddStatus("Animation file generated at " + trackingData.Replace(".trackingdata", ".anim") + ".");
    }

    private void DropFile(bool isScene, string sceneFile, string[] paths)
    {
        if (isScene)
        {
            scenePathLabel.text = scenePath = sceneFile;
            mode = BanterBuilderBundleMode.Scene;
            loggedInViewPrefab.style.display = DisplayStyle.None;
            loggedInViewScene.style.display = DisplayStyle.Flex;
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
                loggedInViewPrefab.style.display = DisplayStyle.Flex;
                loggedInViewScene.style.display = DisplayStyle.None;
            }
            numberOfItems.text = "Number of items: " + kitObjectList.Count;
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
            loggedInViewPrefab.style.display = DisplayStyle.Flex;
            loggedInViewScene.style.display = DisplayStyle.None;
        }
        else if (mode == BanterBuilderBundleMode.Scene)
        {
            mainTitle.text = "<color=\"white\">Build Mode:</color> Scene Asset Bundle";
            mainTitle.style.display = DisplayStyle.Flex;
            scenePathLabel.style.display = DisplayStyle.Flex;
            scenePathLabel.text = "<color=\"white\">Scene Path:</color> " + scenePath;
            loggedInViewPrefab.style.display = DisplayStyle.None;
            loggedInViewScene.style.display = DisplayStyle.Flex;
            // button to open the webroot folder - highlight in unity.
        }
        else
        {
            mainTitle.style.display = DisplayStyle.None;
            mainTitle.text = "";
            loggedInViewPrefab.style.display = DisplayStyle.None;
            loggedInViewScene.style.display = DisplayStyle.Flex;
        }
        ShowRemoveSelected();
        loginManager.ShowUploadToggle();
    }

    public void OpenSpaceCreation()
    {
        Application.OpenURL("https://sidequestvr.com/account/create-space");
    }
    private void ShowBuildConfirm() {
        buildConfirm.style.display = DisplayStyle.Flex;
        confirmBuildMode.text = "<color=\"white\">Build Mode:</color> " + (mode == BanterBuilderBundleMode.Scene ? "Scene Bundle" : "Kit Bundle");
        confirmKitBundle.style.display = mode == BanterBuilderBundleMode.Kit ? DisplayStyle.Flex : DisplayStyle.None;
        confirmKitBundle.text = "<color=\"white\">Kit Name:</color> " + kitName.value;
        confirmKitBundleID.style.display = mode == BanterBuilderBundleMode.Kit && !string.IsNullOrEmpty(selectedKitId) ? DisplayStyle.Flex : DisplayStyle.None;
        confirmKitBundleID.text = "<color=\"white\">Kit Bundle ID:</color> " + selectedKitId;
        confirmSceneFile.style.display = mode == BanterBuilderBundleMode.Scene ? DisplayStyle.Flex : DisplayStyle.None;
        confirmSceneFile.text = "<color=\"white\">Scene File:</color> " + scenePath;
        confirmSpaceCode.style.display = mode == BanterBuilderBundleMode.Scene ? DisplayStyle.Flex : DisplayStyle.None;
        confirmSpaceCode.text = "<color=\"white\">Space:</color> https://" + spaceSlug.text + ".bant.ing";
        confirmKitNumber.style.display = mode == BanterBuilderBundleMode.Kit ? DisplayStyle.Flex : DisplayStyle.None;
        confirmKitNumber.text =  "<color=\"white\">Number of Items:</color> " + kitObjectList.Count.ToString();
    }
    private void BuildAssetBundles(bool skipUpload = false)
    {
        if (mode == BanterBuilderBundleMode.None)
        {
            status.AddStatus("Nothing to build...");
            return;
        }
        if (mode == BanterBuilderBundleMode.Scene && string.IsNullOrWhiteSpace(scenePath))
        {
            status.AddStatus("No scene selected...");
            return;
        }
        if (mode == BanterBuilderBundleMode.Kit && kitObjectList.Count < 1)
        {
            status.AddStatus("No objects selected...");
            return;
        }

        ShowBuildConfirm();
        confirmCallback = () => {
#if BANTER_VISUAL_SCRIPTING
            if (!ValidateVisualScripting.CheckVsNodes())
            {
                status.AddStatus("Found disallowed visual scripting nodes, please check the logs for more information.");
                return;
            }
            else
            {
                status.AddStatus("Visual Scripting check passed!");
            }
#endif
            if(!skipUpload) {
                status.AddStatus("Build started...");

                if (!Directory.Exists(Path.Join(assetBundleRoot, assetBundleDirectory)))
                {
                    Directory.CreateDirectory(Path.Join(assetBundleRoot, assetBundleDirectory));
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
                            status.AddStatus("Building: " + newAssetBundleName);

                            AssetImporter.GetAtPath(scenePath).SetAssetBundleNameAndVariant(newAssetBundleName, string.Empty);
                            abb.assetNames = new[] { scenePath };
                        }
                        else if (mode == BanterBuilderBundleMode.Kit)
                        {
                            newAssetBundleName = "kitbundle_" + platform + "_" + GetKitName() + ".banter";
                            status.AddStatus("Building: " + newAssetBundleName);
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
                if (names.Count > 0 && !autoUpload.value)
                {
                    EditorUtility.RevealInFinder(Path.Join(assetBundleRoot, assetBundleDirectory) + "/" + names[0]);
                }
                buildProgressBar.value = 100;
                var task = new Task(async () =>
                {
                    await new WaitForSeconds(5);
                    HideProgressBar();
                });
                task.Start();
                if (mode == BanterBuilderBundleMode.Kit)
                {
                    status.AddStatus("Writing kit items to " + Path.Join(assetBundleRoot, assetBundleDirectory) + "/kit_items.txt.");
                    File.WriteAllText(Path.Join(assetBundleRoot, assetBundleDirectory) + "/kit_items.txt", String.Join("\n", kitObjectList.Select(x => x.path.ToLower()).ToArray()));
                }
                status.AddStatus("Build finished.");
            }
            
            if (autoUpload.value && sq.User != null)
            {
                if(mode == BanterBuilderBundleMode.Scene) {
                    if (string.IsNullOrEmpty(spaceSlug.text)){
                        status.AddStatus("No space slug provided, please enter a slug to upload.");
                        return;
                    }
                    uploadWebOnly.SetEnabled(false);
                    uploadEverything.SetEnabled(false);
                    EditorCoroutineUtility.StartCoroutine(UploadEverything(() =>
                    {
                        status.AddStatus("Upload complete.");
                        uploadWebOnly.SetEnabled(true);
                        uploadEverything.SetEnabled(true);
                    }), this);
                }else{
                    if (string.IsNullOrEmpty(kitName.text) || string.IsNullOrEmpty(kitDescription.text) || markitCoverImage.value == null || kitCategoryDropDown.index == -1){
                        status.AddStatus("No kit name, description, category or cover image provided, please enter a name, description, category and select a texture.");
                        return;
                    }
                    uploadEverythingKit.SetEnabled(false);
                    EditorCoroutineUtility.StartCoroutine(UploadKit(() =>
                    {
                        status.AddStatus("Upload complete.");
                        uploadEverythingKit.SetEnabled(true);
                    }, skipUpload), this);
                }
            }
        };
    }
    void HideProgressBar()
    {
        buildProgressBar.style.display = DisplayStyle.None;
    }

}
