using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
using BS;
using System.Threading;
using BS.SDKEditor;
using Unity.EditorCoroutines.Editor;
using System.Collections;
using UnityEditor.UIElements;
using UnityEditor.SceneManagement;
using LongBunnyLabs;
using System.Runtime.InteropServices;
using Unity.VisualScripting;

[RenamedFrom("Banter.SDKEditor.BanterBuilderBundleMode")]
public enum BSBuilderBundleMode
{
    None = 0,
    Scene = 1
}

public enum PoseSelectionType
{
    CenterEye,
    LeftFoot,
    RightFoot
}

public class BuilderWindow : EditorWindow
{
    [SerializeField] private VisualTreeAsset _mainWindowVisualTree = default;
    [SerializeField] private StyleSheet _mainWindowStyleSheet = default;

    public const string SQ_API_CLIENT_ID = "sidequest";

    public const string SQ_API_CLIENT_ID_TEST = "client_85b087d9975cb8ca5bb575a2";

    public bool isTestEnvironment = false;

    public static UnityEvent OnCompileAll = new UnityEvent();
    public static UnityEvent OnClearAll = new UnityEvent();
    public static UnityEvent OnVisualScript = new UnityEvent();
    public static UnityEvent OnCompileInjection = new UnityEvent();
    public static UnityEvent OnCompileAllComponents = new UnityEvent();
    private BuildTarget[] buildTargets = new BuildTarget[] { BuildTarget.Android, BuildTarget.StandaloneWindows };
    private bool[] buildTargetFlags = new bool[] { true, true };
    BSBuilderBundleMode mode = BSBuilderBundleMode.None;
    Label scenePathLabel;
    VisualElement scenePathParent;
    Label sceneStatsLabel;
    VisualElement sceneStatsParent;
    // Label mainTitle;
    string scenePath;
    ListView buildProgress;
    ProgressBar buildProgressBar;
    Label statusBar;

    Label codeText;
    DropdownField worldDropdown;
    Label worldUrlLabel;
    List<SqEditorWorld> worlds = new List<SqEditorWorld>();
    SqEditorWorld selectedWorld;
    Label statusText;
    Button uploadWebOnly;
    Button uploadEverything;

    Toggle autoUpload;

    VisualElement loggedInView;
    VisualElement loggedInViewScene;

    Label confirmBuildMode;
    Label confirmSceneFile;
    Label confirmSpaceCode;
    Label confirmSyncedGraphs;

    Label confirmBuild;
    Button cancelBuild;

    VisualElement buildConfirm;

    GameObject avatarGameObject;
    VisualElement dropAvatarContainer;
   
    string assetBundleRoot = "Assets";
    string assetBundleDirectory = "WebRoot";
    LoginManager loginManager;
    Status status;
    Label buildButton;
    Label buildAvatarButton;
    Action confirmCallback;
    VisualElement linkPage;
    VisualElement buildOptions;
    VisualElement loggedInCTAScene;
    VisualElement dropAreaContainer;
    Label MainTitle;

    PoseSelectionType poseSelectionType = PoseSelectionType.CenterEye;

    VisualElement HeadObjectList;
    Label MissingBones;
    Label CenterEyePoseLabel;
    Button SelectCenterEye;
    Label LeftFootPoseLabel;
    Button SelectLeftFoot;
    Label RightFootPoseLabel;
    Button SelectRightFoot;

    private DropdownField avatarIdDropdown;
    private List<SqEditorAvatar> myAvatars;
    private SqEditorAvatar selectedExistingAvatar;
    private Toggle avatarIsPublicToggle;

    // Pose centerEyePose;
    // Pose leftFootPose;
    // Pose rightFootPose;

    VisualElement AvatarInfoCard;

    Label SelectAvatar;
    Button ShowAvatar;
    
    
    private bool handleEnabled = false;
    private bool handlePosition = false;
    private Vector3 posePosition = Vector3.zero;
    private Quaternion poseRotation = Quaternion.identity;

    Action OnPoseCallback;

    FlexaPose currentFlexaPose;


    [MenuItem("Altspace/Altspace Builder")]
    public static void ShowMainWindow()
    {
        Type inspectorType = Type.GetType("UnityEditor.InspectorWindow,UnityEditor.dll");
        BuilderWindow window = EditorWindow.GetWindow<BuilderWindow>(new Type[] { inspectorType });
        window.minSize = new Vector2(450, 200);
        window.titleContent = new GUIContent("Altspace Builder", Resources.Load<Texture2D>("UI/Images/altspace-window-icon"));
    }


#if GREENFIELD_PROJECT
    [MenuItem("Altspace/Tools/Compile C# Components")]
    public static void CompileAllComponents()
    {
        OnCompileAll.Invoke();
    }
    [MenuItem("Altspace/Tools/Clear C# Components")]
    public static void ClearAllComponents()
    {
        OnClearAll.Invoke();
    }
    [MenuItem("Altspace/Tools/Compile Injection")]
    public static void CompileInjection()
    {
        OnCompileInjection.Invoke();
    }
#else
    [MenuItem("Altspace/Tools/Setup Layers")]
    public static void SetupLayersAndTags()
    {
        InitialiseOnLoad.SetupLayersAndTags();
    }
#endif
    [MenuItem("Altspace/Tools/Toggle Dev Tools")]
    public static void ToggleDevTools()
    {
        BSStarterUpper.ToggleDevTools();
    }

    [MenuItem("Altspace/Tools/Toggle Auto Start (Players + Keyboard Input)")]
    public static void ToggleAutoStart()
    {
        BSStarterUpper.ToggleAutoStart();
    }

#if GREENFIELD_PROJECT
    [MenuItem("Altspace/Tools/Configure Visual Scripting")]
    public static void VisualScript()
    {
        OnVisualScript.Invoke();
    }
#else 
    [MenuItem("Altspace/Tools/Configure Visual Scripting")]
    public static void VisualScript()
    {
        VsNodeGeneration.SetVSTypesAndAssemblies();
    }
#endif 

#if GREENFIELD_PROJECT
    [MenuItem("Altspace/Tools/Domain Reload")]
    public static void DomainReload()
    {
        EditorUtility.RequestScriptReload();
    }
#endif 

    private SqEditorAppApi sq;

    public void OnDisable()
    {
        loginManager?.Dispose();
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnFocus()
    {
        // While signed out, coming back to the window makes sure the code on screen is still
        // redeemable and that we're listening for its approval (see LoginManager.OnWindowFocused).
        loginManager?.OnWindowFocused();
    }

    void ShowWebRoot()
    {
        string path = Path.Join(assetBundleRoot, assetBundleDirectory);

        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath(path, typeof(UnityEngine.Object));

        Selection.activeObject = obj;

        EditorGUIUtility.PingObject(obj);
    }

    public void OnEnable()
    {
        
        SceneView.duringSceneGui += OnSceneGUI;
        VisualElement content = _mainWindowVisualTree.CloneTree();
        content.style.height = new StyleLength(Length.Percent(100));
        rootVisualElement.styleSheets.Add(_mainWindowStyleSheet);
        rootVisualElement.Add(content);
        SqEditorAppApiConfig config = new SqEditorAppApiConfig(isTestEnvironment ? SQ_API_CLIENT_ID_TEST : SQ_API_CLIENT_ID, Application.persistentDataPath, isTestEnvironment);
        sq = new SqEditorAppApi(config);
        SetupUI();
        avatarGameObject = AvatarRef.Instance.avatarGameObject;
        SetupAvatarUI();
        status = new Status(statusBar, buildProgress, buildProgressBar);
        // Restored: this was commented out in "status gone?" (bb1dedf4), which left the Logs field
        // rendering blank rows (makeItem builds a Label, but with no bindItem its text is never set).
        // Guard the index — Rebuild can bind a transiently out-of-range i if the backing list changes
        // between makeItem and bindItem, which is the likely reason it was disabled.
        buildProgress.bindItem = (e, i) =>
        {
            if (e is Label label && i >= 0 && i < status.statusMessages.Count)
                label.text = status.statusMessages[i];
        };
        buildProgress.itemsSource = status.statusMessages;
        buildProgress.Rebuild();
        loginManager = new LoginManager(sq, autoUpload, codeText, linkPage, loggedInView, statusText, buildButton, rootVisualElement.Q<VisualElement>("ExtraUploadButtons"), rootVisualElement.Q<Label>("SignOut"));
        loginManager.SetLoginState();
        loginManager.RefreshView += () => RefreshView(true);
        loginManager.SetBuildButtonText();
        if (sq.User != null)
        {
            loginManager.RefreshUser();
        }
        else
        {
            loginManager.GetCode();
        }
        loginManager.OnLoginCompleted += () =>
        {
            EditorCoroutineUtility.StartCoroutine(RefreshWorlds(), this);
            RefreshView(true);
            avatarGameObject = AvatarRef.Instance.avatarGameObject;
            RefreshAvatarView(true);
            SetupExistingAvatars();
        };
        if (avatarGameObject != null)
        { 
            RefreshAvatarView(); 
        }
        RefreshView(); 
    }

    [MenuItem("Altspace/Tools/Clear All Asset Bundles")]
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
    
    private void OnSceneGUI(SceneView sceneView)
    {
        if (!handleEnabled)
            return;

        EditorGUI.BeginChangeCheck();
        Vector3 newPos = Handles.PositionHandle(posePosition, poseRotation);
        Quaternion newRot = poseRotation;
        if (!handlePosition)
        {
            float handleSize = HandleUtility.GetHandleSize(newPos);
            // newRot = Handles.RotationHandle(poseRotation, newPos);
            Handles.color = Color.white;
            newRot = Handles.FreeRotateHandle(newRot, newPos, handleSize * 0.7f);
            Handles.color = Color.green;
            newRot = Handles.Disc(newRot, newPos, newRot * new Vector3(0, 1, 0), handleSize * 0.7f, false, 1);
            Handles.color = Color.red;
            newRot = Handles.Disc(newRot, newPos, newRot *  new Vector3(1, 0, 0), handleSize * 0.7f, false, 1);
            Handles.color = Color.blue;
            newRot = Handles.Disc(newRot, newPos, newRot *  new Vector3(0, 0, 1), handleSize * 0.7f, false, 1);
        }
        if (EditorGUI.EndChangeCheck())
        {
            posePosition = newPos;
            poseRotation = newRot;
            Repaint(); // update the inspector
        }
        if (!handlePosition)
        {
            // Optional: Draw a wireframe at the pose
            Matrix4x4 matrix = Matrix4x4.TRS(posePosition, poseRotation, Vector3.one);
            using (new Handles.DrawingScope(matrix))
            {
                Handles.color = Color.cyan;
                Handles.ArrowHandleCap(0, Vector3.zero, Quaternion.LookRotation(Vector3.forward), 0.5f, EventType.Repaint);
                Handles.color = Color.magenta;
                Handles.ArrowHandleCap(0, Vector3.zero, Quaternion.LookRotation(Vector3.up), 0.5f, EventType.Repaint);
            }
        }
        OnPoseCallback?.Invoke();
    }
    List<GameObject> headGameObjects = new List<GameObject>();
    void SelectRightFootComplete(FlexaPose avatarPoseMeta)
    {
        OnPoseCallback = null;
        var pos = avatarPoseMeta.rightFootTransform.InverseTransformPoint(posePosition);
        var rot = Quaternion.Inverse(avatarPoseMeta.rightFootTransform.rotation) * poseRotation;
        avatarPoseMeta.rightFoot = new Pose(pos, rot);
        ProjectPrefs.SetString("BanterBuilder_RightFootPosePosition", pos.x + "," + pos.y + "," + pos.z +
            ";" + rot.x + "," + rot.y + "," + rot.z + "," + rot.w);
        RightFootPoseLabel.text = $"Position: {pos}\nRotation: {rot.eulerAngles}";
    }
    void SelectLeftFootComplete(FlexaPose avatarPoseMeta)
    {
        OnPoseCallback = null;
        var pos = avatarPoseMeta.leftFootTransform.InverseTransformPoint(posePosition);
        var rot = Quaternion.Inverse(avatarPoseMeta.leftFootTransform.rotation) * poseRotation;
        avatarPoseMeta.leftFoot = new Pose(pos, rot);
        ProjectPrefs.SetString("BanterBuilder_LeftFootPosePosition", pos.x + "," + pos.y + "," + pos.z +
            ";" + rot.x + "," + rot.y + "," + rot.z + "," + rot.w);
        LeftFootPoseLabel.text = $"Position: {pos}\nRotation: {rot.eulerAngles}";
    }
    void SelectCenterEyeComplete(FlexaPose avatarPoseMeta)
    {
        OnPoseCallback = null;
        var pos = avatarPoseMeta.headTransform.InverseTransformPoint(posePosition);
        var rot = Quaternion.Inverse(avatarPoseMeta.headTransform.rotation) * poseRotation;
        avatarPoseMeta.centerEye = new Pose(pos, rot);
        ProjectPrefs.SetString("BanterBuilder_CenterEyePosePosition", pos.x + "," + pos.y + "," + pos.z +
            ";" + rot.x + "," + rot.y + "," + rot.z + "," + rot.w);
        CenterEyePoseLabel.text = $"Position: {pos}";
    }

    void SetCenterText()
    {
        CenterEyePoseLabel.text = $"Position: {currentFlexaPose.centerEye.position}";
    }
    void SetLeftText()
    {
        LeftFootPoseLabel.text = $"Position: {currentFlexaPose.leftFoot.position}\nRotation: {currentFlexaPose.leftFoot.rotation.eulerAngles}";
    }
    void SetRightText()
    {
        RightFootPoseLabel.text = $"Position: {currentFlexaPose.rightFoot.position}\nRotation: {currentFlexaPose.rightFoot.rotation.eulerAngles}";
    }
    private void SetupAvatarUI()
    {
        currentFlexaPose = GetFlexaPose();
        CenterEyePoseLabel = rootVisualElement.Q<Label>("CenterEyePoseLabel");
        LeftFootPoseLabel = rootVisualElement.Q<Label>("LeftFootPoseLabel");
        RightFootPoseLabel = rootVisualElement.Q<Label>("RightFootPoseLabel");
        HeadObjectList = rootVisualElement.Q<VisualElement>("HeadObjectList");
        DrawReorderableList(headGameObjects, HeadObjectList, true);
        MissingBones = rootVisualElement.Q<Label>("MissingBones");
        AvatarInfoCard = rootVisualElement.Q<VisualElement>("AvatarInfoCard");
        ShowAvatar = rootVisualElement.Q<Button>("ShowAvatar");
        ShowAvatar.RegisterCallback<MouseUpEvent>((e) =>
        {
            if (avatarGameObject == null)
            {
                status.AddStatus("No avatar selected, please select an avatar.");
                return;
            }
            EditorGUIUtility.PingObject(avatarGameObject);

        });
        new DragAndDropStuff().SetupDropArea(rootVisualElement.Q<VisualElement>("dropAvatarArea"), DropGameObject);

        avatarIdDropdown = rootVisualElement.Q<DropdownField>("ddlAvatarId");
        avatarIsPublicToggle = rootVisualElement.Q<Toggle>("togAvatarIsPublic");

        avatarIdDropdown.RegisterCallback<ChangeEvent<string>>(evt =>
        {
            if (avatarIdDropdown.choices.IndexOf(evt.newValue) == 0)
            {
                selectedExistingAvatar = null;
                avatarIsPublicToggle.value = true;
            }
            else
            {
                selectedExistingAvatar = myAvatars[avatarIdDropdown.choices.IndexOf(evt.newValue) - 1];
                avatarIsPublicToggle.value = selectedExistingAvatar.Public;
            }
        });
        
        SetupExistingAvatars();

        buildAvatarButton = rootVisualElement.Q<Label>("buildAvatarButton");
        buildAvatarButton.RegisterCallback<MouseUpEvent>(async (e) =>
        {
            EditorApplication.LockReloadAssemblies();
            AssetDatabase.DisallowAutoRefresh();
            if (await BuildAvatarAssetBundles())
            {
                EditorCoroutineUtility.StartCoroutine(UploadAvatar(() =>
                {
                    status.AddStatus("Avatar build completed successfully.");
                    AssetDatabase.AllowAutoRefresh();
                    EditorApplication.UnlockReloadAssemblies();
                }), this);
            }
            else
            {
                status.AddStatus("Avatar build failed, please check the plugin for errors.");
                AssetDatabase.AllowAutoRefresh();
                EditorApplication.UnlockReloadAssemblies();
            }
        });

        var resetScreen = rootVisualElement.Q<Button>("resetAvatarScreen");
        resetScreen.RegisterCallback<MouseUpEvent>((e) =>
        {
            headGameObjects.Clear();
            var list = (ListView)HeadObjectList.Children().First();
            list.Rebuild();
            avatarGameObject = null;
            RefreshAvatarView(true);
        });

        if (avatarGameObject != null)
        {
            SetCenterText();
            SetLeftText();
            SetRightText();
        }
        GetHeadObjects();
        SelectAvatar = rootVisualElement.Q<Label>("SelectAvatar");
        SelectCenterEye = rootVisualElement.Q<Button>("SelectCenterEye");
        var CenterEyePosReset = rootVisualElement.Q<Button>("CenterEyePosReset");
        var LeftFootPosReset = rootVisualElement.Q<Button>("LeftFootPosReset");
        var LeftFootRotReset = rootVisualElement.Q<Button>("LeftFootRotReset");
        var LeftFootRotWorldReset = rootVisualElement.Q<Button>("LeftFootRotWorldReset");
        var RightFootPosReset = rootVisualElement.Q<Button>("RightFootPosReset");
        var RightFootRotReset = rootVisualElement.Q<Button>("RightFootRotReset");
        var RightFootRotWorldReset = rootVisualElement.Q<Button>("RightFootRotWorldReset");
        var RightFootMirror = rootVisualElement.Q<Button>("RightFootMirror");
        var LeftFootMirror = rootVisualElement.Q<Button>("LeftFootMirror");
        CenterEyePosReset.RegisterCallback<MouseUpEvent>((e) =>
        {
            posePosition = currentFlexaPose.headTransform.position;
            SceneView.RepaintAll();
        });
        SelectCenterEye.RegisterCallback<MouseUpEvent>((e) =>
        {
            currentFlexaPose = GetFlexaPose();
            SetCenterText();
            if (handleEnabled && poseSelectionType != PoseSelectionType.CenterEye)
            {
                if (poseSelectionType == PoseSelectionType.LeftFoot)
                {
                    SelectLeftFootComplete(currentFlexaPose);
                    LeftFootPosReset.style.display = DisplayStyle.None;
                    LeftFootRotReset.style.display = DisplayStyle.None;
                    LeftFootRotWorldReset.style.display = DisplayStyle.None;
                    LeftFootMirror.style.display = DisplayStyle.None;
                }
                else
                {
                    SelectRightFootComplete(currentFlexaPose);
                    RightFootPosReset.style.display = DisplayStyle.None;
                    RightFootRotReset.style.display = DisplayStyle.None;
                    RightFootRotWorldReset.style.display = DisplayStyle.None;
                    RightFootMirror.style.display = DisplayStyle.None;
                }
                handleEnabled = false;
            }
            SelectLeftFoot.text = "Change";
            SelectRightFoot.text = "Change";
            handleEnabled = !handleEnabled;
            handlePosition = handleEnabled;
            SelectCenterEye.text = handleEnabled ? "Done" : "Change";
            poseSelectionType = PoseSelectionType.CenterEye;
            if (!handleEnabled)
            {
                SelectCenterEyeComplete(currentFlexaPose);
                CenterEyePosReset.style.display = DisplayStyle.None;
            }
            else
            {
                CenterEyePosReset.style.display = DisplayStyle.Flex;
                posePosition = currentFlexaPose.headTransform.TransformPoint(currentFlexaPose.centerEye.position);
                poseRotation = currentFlexaPose.headTransform.rotation;
                OnPoseCallback = () =>
                {
                    Handles.DrawLine(currentFlexaPose.headTransform.position, posePosition);
                    var pos = currentFlexaPose.headTransform.InverseTransformPoint(posePosition);
                    var rot = Quaternion.Inverse(currentFlexaPose.headTransform.rotation) * poseRotation;
                    currentFlexaPose.centerEye = new Pose(pos, Quaternion.identity);
                    EditorUtility.SetDirty(currentFlexaPose);
                    CenterEyePoseLabel.text = $"Position: {pos}";
                };

            }
            SceneView.RepaintAll();
        });

        SelectLeftFoot = rootVisualElement.Q<Button>("SelectLeftFoot");

        LeftFootPosReset.RegisterCallback<MouseUpEvent>((e) =>
        {
            posePosition = GetFlexaPose().leftFootTransform.position;
            SceneView.RepaintAll();
        });
        LeftFootRotReset.RegisterCallback<MouseUpEvent>((e) =>
        {
            poseRotation = GetFlexaPose().leftFootTransform?.rotation ?? Quaternion.identity;
            SceneView.RepaintAll();
        });
        LeftFootRotWorldReset.RegisterCallback<MouseUpEvent>((e) =>
        {
            poseRotation = Quaternion.identity;
            SceneView.RepaintAll();
        });
        LeftFootMirror.RegisterCallback<MouseUpEvent>((e) =>
        {
            var offset = currentFlexaPose.rightFoot.InverseTransformDirection(currentFlexaPose.rightFoot.position);
            offset.x *= -1f;
            offset = currentFlexaPose.rightFoot.TransformDirection(offset);
            currentFlexaPose.leftFoot.position = offset;
            posePosition = currentFlexaPose.leftFootTransform.TransformPoint(currentFlexaPose.leftFoot.position);
            SceneView.RepaintAll();
        });
        SelectLeftFoot.RegisterCallback<MouseUpEvent>((e) =>
        {
            currentFlexaPose = GetFlexaPose();
            SetLeftText();

            if (handleEnabled && poseSelectionType != PoseSelectionType.LeftFoot)
            {
                if (poseSelectionType == PoseSelectionType.CenterEye)
                {
                    SelectCenterEyeComplete(currentFlexaPose);
                    CenterEyePosReset.style.display = DisplayStyle.None;
                }
                else
                {
                    SelectRightFootComplete(currentFlexaPose);
                    RightFootPosReset.style.display = DisplayStyle.None;
                    RightFootRotReset.style.display = DisplayStyle.None;
                    RightFootRotWorldReset.style.display = DisplayStyle.None;
                    RightFootMirror.style.display = DisplayStyle.None;
                }
                handleEnabled = false;
            }
            SelectCenterEye.text = "Change";
            SelectRightFoot.text = "Change";
            poseSelectionType = PoseSelectionType.LeftFoot;
            handleEnabled = !handleEnabled;
            handlePosition = false;
            SelectLeftFoot.text = handleEnabled ? "Done" : "Change";
            if (!handleEnabled)
            {
                SelectLeftFootComplete(currentFlexaPose);
                LeftFootPosReset.style.display = DisplayStyle.None;
                LeftFootRotReset.style.display = DisplayStyle.None;
                LeftFootRotWorldReset.style.display = DisplayStyle.None;
                LeftFootMirror.style.display = DisplayStyle.None;
            }
            else
            {
                LeftFootPosReset.style.display = DisplayStyle.Flex;
                LeftFootRotReset.style.display = DisplayStyle.Flex;
                LeftFootRotWorldReset.style.display = DisplayStyle.Flex;
                LeftFootMirror.style.display = DisplayStyle.Flex;
                posePosition = currentFlexaPose.leftFootTransform.TransformPoint(currentFlexaPose.leftFoot.position);
                poseRotation = currentFlexaPose.leftFootTransform.rotation * currentFlexaPose.leftFoot.rotation;
                OnPoseCallback = () =>
                {
                    Handles.DrawLine(currentFlexaPose.leftFootTransform.position, posePosition);
                    var pos = currentFlexaPose.leftFootTransform.InverseTransformPoint(posePosition);
                    var rot = Quaternion.Inverse(currentFlexaPose.leftFootTransform.rotation) * poseRotation;
                    currentFlexaPose.leftFoot = new Pose(pos, rot);
                    EditorUtility.SetDirty(currentFlexaPose);
                    LeftFootPoseLabel.text = $"Position: {pos}\nRotation: {rot.eulerAngles}";
                };

            }
            SceneView.RepaintAll();
        });


        SelectRightFoot = rootVisualElement.Q<Button>("SelectRightFoot");

        RightFootPosReset.RegisterCallback<MouseUpEvent>((e) =>
        {
            posePosition = GetFlexaPose().rightFootTransform.position;
            SceneView.RepaintAll();
        });
        RightFootRotReset.RegisterCallback<MouseUpEvent>((e) =>
        {
            poseRotation = GetFlexaPose().rightFootTransform?.rotation ?? Quaternion.identity;
            SceneView.RepaintAll();
        });
        RightFootRotWorldReset.RegisterCallback<MouseUpEvent>((e) =>
        {
            poseRotation = Quaternion.identity;
            SceneView.RepaintAll();
        });
        
        RightFootMirror.RegisterCallback<MouseUpEvent>((e) =>
        {
            var offset = currentFlexaPose.leftFoot.InverseTransformDirection(currentFlexaPose.leftFoot.position);
            offset.x *= -1f;
            offset = currentFlexaPose.leftFoot.TransformDirection(offset);
            currentFlexaPose.rightFoot.position = offset;
            posePosition = currentFlexaPose.rightFootTransform.TransformPoint(currentFlexaPose.rightFoot.position);
            SceneView.RepaintAll();
        });
        SelectRightFoot.RegisterCallback<MouseUpEvent>((e) =>
        {
            currentFlexaPose = GetFlexaPose();
            SetRightText();

            if (handleEnabled && poseSelectionType != PoseSelectionType.RightFoot)
            {
                if (poseSelectionType == PoseSelectionType.CenterEye)
                {
                    SelectCenterEyeComplete(currentFlexaPose);
                    CenterEyePosReset.style.display = DisplayStyle.None;
                }
                else
                {
                    SelectLeftFootComplete(currentFlexaPose);
                    LeftFootPosReset.style.display = DisplayStyle.None;
                    LeftFootRotReset.style.display = DisplayStyle.None;
                    LeftFootRotWorldReset.style.display = DisplayStyle.None;
                    LeftFootMirror.style.display = DisplayStyle.None;
                }
                handleEnabled = false;
            }
            SelectCenterEye.text = "Change";
            SelectLeftFoot.text = "Change";
            poseSelectionType = PoseSelectionType.RightFoot;
            handleEnabled = !handleEnabled;
            handlePosition = false;
            SelectRightFoot.text = handleEnabled ? "Done" : "Change";
            if (!handleEnabled)
            {
                SelectRightFootComplete(currentFlexaPose);
                RightFootPosReset.style.display = DisplayStyle.None;
                RightFootRotReset.style.display = DisplayStyle.None;
                RightFootRotWorldReset.style.display = DisplayStyle.None;
                RightFootMirror.style.display = DisplayStyle.None;
            }
            else
            {
                RightFootPosReset.style.display = DisplayStyle.Flex;
                RightFootRotReset.style.display = DisplayStyle.Flex;
                RightFootRotWorldReset.style.display = DisplayStyle.Flex;
                RightFootMirror.style.display = DisplayStyle.Flex;
                posePosition = currentFlexaPose.rightFootTransform.TransformPoint(currentFlexaPose.rightFoot.position);
                poseRotation = currentFlexaPose.rightFootTransform.rotation * currentFlexaPose.rightFoot.rotation;
                OnPoseCallback = () =>
                {
                    Handles.DrawLine(currentFlexaPose.rightFootTransform.position, posePosition);
                    var pos = currentFlexaPose.rightFootTransform.InverseTransformPoint(posePosition);
                    var rot = Quaternion.Inverse(currentFlexaPose.rightFootTransform.rotation) * poseRotation;
                    currentFlexaPose.rightFoot = new Pose(pos, rot);
                    EditorUtility.SetDirty(currentFlexaPose);
                    RightFootPoseLabel.text = $"Position: {pos}\nRotation: {rot.eulerAngles}";
                };

            }
            SceneView.RepaintAll();
        });
    }

    private void SetupExistingAvatars()
    {
        avatarIdDropdown.choices = new List<string>();
        avatarIdDropdown.choices.Add("<New Avatar>");
        avatarIdDropdown.SetValueWithoutNotify(avatarIdDropdown.choices[0]);
        
        EditorCoroutineUtility.StartCoroutine(sq.GetAvatars(list =>
        {
            myAvatars = list;
            foreach (SqEditorAvatar av in list)
            {
                avatarIdDropdown.choices.Add($"{av.Name} (ID: {av.AvatarId})");
                if(selectedExistingAvatar?.AvatarId==av.AvatarId)
                    avatarIdDropdown.SetValueWithoutNotify(avatarIdDropdown.choices[^1]);
            }
        }, e =>
        {
            // Not being signed in just means there are no avatars to list yet.
            if (!(e is SqEditorApiAuthException)) Debug.LogException(e);
        }), this);

        avatarIsPublicToggle.value = selectedExistingAvatar?.Public ?? true;
    }

    void GetExistingPose(ref Pose pose, string key, string defaults) {
        var posePositionString = ProjectPrefs.GetString(key, defaults);
        var poseParts = posePositionString.Split(';');
        if (poseParts.Length == 2)
        {
            var positionParts = poseParts[0].Split(',');
            var rotationParts = poseParts[1].Split(',');
            if (positionParts.Length == 3 && rotationParts.Length == 4)
            {
                pose.position = new Vector3(float.Parse(positionParts[0]), float.Parse(positionParts[1]), float.Parse(positionParts[2]));
                pose.rotation = new Quaternion(float.Parse(rotationParts[0]), float.Parse(rotationParts[1]), float.Parse(rotationParts[2]), float.Parse(rotationParts[3]));
            }
        }
    }
    private void SetupUI()
    {
        statusBar = rootVisualElement.Q<Label>("StatusBar");
        new TabsManager(rootVisualElement);
        buildButton = rootVisualElement.Q<Label>("buildButton");
        buildButton.RegisterCallback<MouseUpEvent>((e) => BuildAssetBundles());

        MainTitle = rootVisualElement.Q<Label>("MainTitle");
        var signInButton = rootVisualElement.Q<Button>("OpenLink");
        signInButton.clicked += OpenLinkPage;
        linkPage = signInButton;

        var createSpace = rootVisualElement.Q<Label>("CreateSpace");
        createSpace.RegisterCallback<MouseUpEvent>((e) => OpenCreateWorld());
        var openWebRoot = rootVisualElement.Q<Button>("OpenWebRoot");
        dropAreaContainer = rootVisualElement.Q<VisualElement>("dropAreaContainer");
        dropAvatarContainer = rootVisualElement.Q<VisualElement>("dropAvatarContainer");
        openWebRoot.clicked += () => ShowWebRoot();

        var analyzeBundle = rootVisualElement.Q<Button>("AnalyzeBundle");
        analyzeBundle.clicked += () =>
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogWarning("Bundle Analyzer: the active scene isn't saved to disk yet, so there's no asset path to analyze.");
                return;
            }

            SideQuest.BundleAnalyzer.BundleAnalyzerWindow.OpenAndAnalyze(scene.path);
        };

        var clearLogs = rootVisualElement.Q<Button>("clearLogs");

        clearLogs.clicked += () => status.ClearLogs();

        // Always build both platforms - buildTargetFlags already defaults to { true, true }
        // (Android, Windows) and nothing else needs to change it now that the per-platform
        // toggles are gone, so there's nothing left to wire up here.
        ShowHideBuildButton();

        autoUpload = rootVisualElement.Q<Toggle>("autoUpload");
        autoUpload.value = ProjectPrefs.GetBool("BanterBuilder_AutoUpload", true);

        autoUpload.RegisterCallback<MouseUpEvent>((e) =>
        {
            ProjectPrefs.SetBool("BanterBuilder_AutoUpload", autoUpload.value);
            loginManager.SetBuildButtonText();
        });

        buildOptions = rootVisualElement.Q<VisualElement>("BuildOptions");

        loggedInCTAScene = rootVisualElement.Q<VisualElement>("LoggedInCTAScene");

        var resetScreen = rootVisualElement.Q<Button>("resetScreen");
        resetScreen.RegisterCallback<MouseUpEvent>((e) =>
        {
            mode = BSBuilderBundleMode.None;
            scenePath = "";
            ProjectPrefs.DeleteKey("BanterBuilder_ScenePath");
            status.AddStatus("Scene removed from build.");
            RefreshView();
        });
        confirmBuildMode = rootVisualElement.Q<Label>("ConfirmBuildMode");
        confirmSceneFile = rootVisualElement.Q<Label>("ConfirmSceneFile");
        confirmSpaceCode = rootVisualElement.Q<Label>("ConfirmSpaceCode");
        confirmSyncedGraphs = rootVisualElement.Q<Label>("ConfirmSyncedGraphs");

        buildConfirm = rootVisualElement.Q<VisualElement>("BuildConfirm");

        confirmBuild = rootVisualElement.Q<Label>("ConfirmBuild");
        cancelBuild = rootVisualElement.Q<Button>("CancelBuild");
        cancelBuild.RegisterCallback<MouseUpEvent>((e) =>
        {
            buildConfirm.style.display = DisplayStyle.None;
        });
        confirmBuild.RegisterCallback<MouseUpEvent>((e) =>
        {
            buildConfirm.style.display = DisplayStyle.None;
            confirmCallback?.Invoke();
        });

        codeText = rootVisualElement.Q<Label>("LoginCode");
        worldDropdown = rootVisualElement.Q<DropdownField>("WorldDropdown");
        worldUrlLabel = rootVisualElement.Q<Label>("WorldUrl");
        statusText = rootVisualElement.Q<Label>("SignedInStatus");
        uploadWebOnly = rootVisualElement.Q<Button>("UploadWebOnly");
        uploadEverything = rootVisualElement.Q<Button>("UploadEverything");
        loggedInView = rootVisualElement.Q<VisualElement>("LoggedInView");
        loggedInViewScene = rootVisualElement.Q<VisualElement>("LoggedInViewScene");

        // Selecting a world in the dropdown sets the active world (by index → worlds list) and remembers it.
        worldDropdown.RegisterValueChangedCallback((e) =>
        {
            int idx = worldDropdown.index;
            selectedWorld = (idx >= 0 && idx < worlds.Count) ? worlds[idx] : null;
            if (selectedWorld != null)
            {
                ProjectPrefs.SetString("BanterBuilder_selectedWorldId", selectedWorld.WorldId.ToString());
                // Record the choice on the scene as well as in project prefs: the prefs entry is
                // one value for the whole project, while the runtime-override tooling needs to know
                // which world THIS scene publishes to.
                WorldLinkStamp.StampIfOpen(scenePath, selectedWorld.WorldId, selectedWorld.Slug);
            }
            UpdateWorldUrlLabel();
        });

        // Populate the dropdown from the API if already signed in; otherwise it fills in on login-complete
        // (see loginManager.OnLoginCompleted). RefreshWorlds re-selects the last-used world when present.
        if (sq != null && sq.User != null)
            EditorCoroutineUtility.StartCoroutine(RefreshWorlds(), this);

        uploadWebOnly.clicked += () =>
        {
            if (!HasSelectedWorld)
            {
                status.AddStatus("No world selected, please select or create a world.");
                return;
            }
            ShowBuildConfirm();
            confirmCallback = () =>
            {
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
            if (!HasSelectedWorld)
            {
                status.AddStatus("No world selected, please select or create a world.");
                return;
            }
            ShowBuildConfirm();
            confirmCallback = () =>
            {
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

        // mainTitle = rootVisualElement.Q<Label>("mainTitle");
        scenePathLabel = rootVisualElement.Q<Label>("scenePathLabel");
        scenePathParent = rootVisualElement.Q<VisualElement>("scenePathParent");
        sceneStatsLabel = rootVisualElement.Q<Label>("sceneStatsLabel");
        sceneStatsParent = rootVisualElement.Q<VisualElement>("sceneStatsParent");
        buildProgress = rootVisualElement.Q<ListView>("buildProgress");
        buildProgress.makeItem = () =>
        {
            var label = new Label();
            label.AddToClassList("unity-label-margin");
            return label;
        };
        buildProgress.selectionType = SelectionType.None;
        buildProgressBar = rootVisualElement.Q<ProgressBar>("buildProgressBar");
        new DragAndDropStuff().SetupDropArea(rootVisualElement.Q<VisualElement>("dropArea"), DropFile);
        scenePathLabel.text = scenePath = ProjectPrefs.GetString("BanterBuilder_ScenePath", "");
        if (!string.IsNullOrEmpty(scenePath))
        {
            mode = BSBuilderBundleMode.Scene;
        }

    }

    private IEnumerator UploadAvatar(Action callback)
    {
        Debug.Log("Uploading avatar0...");
        // EditorUtility.DisplayProgressBar("Altspace Upload", "Uploading avatar...", 0.1f);
        var path = "AssetBundles";
        var files = Directory.GetFiles(path, "*.BEE", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            Debug.Log("No avatar files found in " + path);
            status.AddStatus("No avatar files found in " + path);
            EditorUtility.ClearProgressBar();
            callback();
            yield break;
        }
        Debug.Log("Uploading avatar..." + files[0]);
        BeginUploadProgress(2);
        long avatarFileId = -1;
        yield return UploadFile(Path.GetFileName(files[0]), null, fileId => avatarFileId = fileId, files[0], NextUploadStep("Uploading avatar"));
        Debug.Log("Avatar File ID: " + avatarFileId);
        if (avatarFileId == -1)
        {
            status.AddStatus("Failed to upload avatar file.");
            HideProgressBar();
            callback();
            yield break;
        }
        Debug.Log("Taking screenshot...");
        string screenpath = Path.Combine(Application.temporaryCachePath, avatarGameObject.name + ".png");
        ObjectScreenshotter.CaptureGameObject(avatarGameObject, screenpath, 512, true, 1, 31);
        Debug.Log("Uploading screenshot...");
        long screenshotfileid = -1;
        yield return UploadFile(Path.GetFileName(screenpath), null, fileId => screenshotfileid = fileId, screenpath, NextUploadStep("Uploading screenshot"));
        Debug.Log($"Screenshot uploaded as {screenshotfileid}");

        if (selectedExistingAvatar == null)
        {
            // Upload new avatar
            EditorCoroutineUtility.StartCoroutine(sq.PostAvatar((av) =>
                {
                    status.AddStatus("Posted avatar successfully.");
                    Debug.Log("Posted avatar successfully.");
                    callback();
                    selectedExistingAvatar = av;
                    SetupExistingAvatars();
                    EditorCoroutineUtility.StartCoroutine(sq.AttachAvatar((slot) =>
                    {
                        status.AddStatus("Avatar attached successfully.");
                        Debug.Log("Avatar attached successfully.");
                        callback();
                        EditorCoroutineUtility.StartCoroutine(sq.SelectAvatar(() => { }, Debug.LogException, slot.UserAvatarId), this);
                    }, e =>
                    {
                        status.AddStatus("Failed to attach avatar: " + e);
                        Debug.LogError("Failed to attach avatar: " + e);
                        callback();
                    }, av.AvatarId, true), this);
                }, e =>
                {
                    status.AddStatus("Failed to post avatar: " + e);
                    Debug.LogError("Failed to post avatar: " + e);
                    callback();
                }, avatarFileId, avatarFileId, screenshotfileid == -1 ? 0 : screenshotfileid, avatarGameObject.name,
                avatarIsPublicToggle.value), this);
        }
        else
        {
            // Upload to existing avatar
            EditorCoroutineUtility.StartCoroutine(sq.UpdateAvatar((av) =>
                {
                    status.AddStatus("Updated avatar successfully.");
                    Debug.Log("Updated avatar successfully.");
                    callback();
                }, e =>
                {
                    status.AddStatus("Failed to update avatar: " + e);
                    Debug.LogError("Failed to update avatar: " + e);
                    callback();
                }, selectedExistingAvatar.AvatarId, avatarFileId, avatarFileId, screenshotfileid == -1 ? 0 : screenshotfileid, avatarGameObject.name,
                avatarIsPublicToggle.value), this);
        }
        EndUploadProgress("Upload complete");
    }

    private IEnumerator UploadWebOnly(Action callback)
    {
        BeginUploadProgress(3);
        yield return UploadWorldFile("index.html", UploadAssetType.Index, UploadAssetTypePlatform.Any, NextUploadStep("Uploading index.html"));
        yield return UploadWorldFile("script.js", UploadAssetType.Js, UploadAssetTypePlatform.Any, NextUploadStep("Uploading script.js"));
        yield return UploadWorldFile("bullshcript.js", UploadAssetType.Js, UploadAssetTypePlatform.Any, NextUploadStep("Uploading bullshcript.js"));
        callback();
        EndUploadProgress("Upload complete");
    }
    private IEnumerator UploadEverything(Action callback)
    {
        // callback runs in finally so it always fires — even if an upload step throws — restoring
        // button state and releasing the assembly-reload lock the scene build hands off (see
        // BuildAssetBundles). A leaked lock would wedge script recompilation until an editor restart.
        try
        {
            uploadHadFailure = false;
            BeginUploadProgress(4);
            // One platform-agnostic combined bundle (encrypted Basis .bee content) hosted as asset.world.
            // Every platform loads this single file and ranged-GETs its own section; the runtime falls back
            // to legacy per-platform windows.banter / android.banter for spaces that predate it. Missing
            // file is skipped.
            yield return UploadWorldFile("asset.world", UploadAssetType.WorldAsset, UploadAssetTypePlatform.Any, NextUploadStep("Uploading asset.world"));
            yield return UploadWorldFile("index.html", UploadAssetType.Index, UploadAssetTypePlatform.Any, NextUploadStep("Uploading index.html"));
            yield return UploadWorldFile("script.js", UploadAssetType.Js, UploadAssetTypePlatform.Any, NextUploadStep("Uploading script.js"));
            yield return UploadWorldFile("bullshcript.js", UploadAssetType.Js, UploadAssetTypePlatform.Any, NextUploadStep("Uploading bullshcript.js"));

            // Only now that the world itself is up: the runtime overrides we are about to drop are
            // only redundant because the scene that just shipped contains them.
            if (!uploadHadFailure)
                yield return RuntimeOverridePrune.Run(sq, selectedWorld?.WorldId, SelectedWorldSlug,
                                                      msg => status.AddStatus(msg));
            else
                status.AddStatus("Skipping runtime-override cleanup — part of the upload failed.");

            EndUploadProgress("Upload complete");
        }
        finally
        {
            callback?.Invoke();
        }
    }

    /// <summary>Set by UploadWorldFile; read by UploadEverything's post-upload cleanup.</summary>
    private bool uploadHadFailure;

    private IEnumerator UploadFile(string name, byte[] bytes = null, Action<long> callback = null, string path = null, Action<float> onProgress = null)
    {
        var file = path == null ? (Path.Join(Path.Join(assetBundleRoot, assetBundleDirectory), name)) : path;
        if (File.Exists(file) || bytes != null)
        {
            status.AddStatus("Upload started: " + file + "...");
            Debug.Log(  "Upload started: " + file);
        }
        else
        {
            status.AddStatus("File not found, skipping: " + file);
            Debug.Log(  "File not found, skipping: " + file);
            yield break;
        }
        var data = bytes == null ? File.ReadAllBytes(file) : bytes;
        yield return sq.UploadFile(name, data, "", (text) =>
        {
            callback?.Invoke(text.FileId);
            status.AddStatus("Uploaded " + name);
        }, e =>
        {
            status.AddStatus("FAILED UPLOADING " + name);
            Debug.LogException(e);
        }, onProgress);
        Debug.Log("Uploading1: " + file);
    }

    // Uploads a WebRoot file and attaches it to the selected world (asset.world, index.html, script.js…),
    // via /v2/worlds/{worlds_id}/assets/type/{type}/platform/{platform}. Callers pass platform Any (0).
    private IEnumerator UploadWorldFile(string name, UploadAssetType type, UploadAssetTypePlatform platform, Action<float> onProgress = null)
    {
        var file = Path.Join(Path.Join(assetBundleRoot, assetBundleDirectory), name);
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
        string slug = SelectedWorldSlug;
        string baseUrl = string.IsNullOrEmpty(SelectedWorldUrl) ? ("https://" + slug + ".worldspace.host") : SelectedWorldUrl;
        yield return sq.UploadFileToWorld(name, data, selectedWorld?.WorldId, slug, (text) =>
        {
            status.AddStatus("Uploaded " + file + " to " + baseUrl + "/" + name);
        }, e =>
        {
            // Recorded as well as logged: an upload step failing does not throw, so without this
            // flag the post-upload cleanup would run on a half-uploaded world and drop runtime
            // overrides the world does not actually contain yet.
            uploadHadFailure = true;
            status.AddStatus("FAILED UPLOADING " + file + " to " + baseUrl + "/" + name);
            Debug.LogException(e);
        }, type, platform, onProgress);
    }

    public void Remove(VisualElement element)
    {
        element.parent.Remove(element);
    }

    void GetHeadObjects()
    {
        if(avatarGameObject == null)
        {
            return;
        }
        foreach (var t in avatarGameObject.GetComponentsInChildren<Transform>())
        {
            if (t.GetComponent<FlexaHead>())
            {
                if(!headGameObjects.Contains(t.gameObject))
                {
                    headGameObjects.Add(t.gameObject);
                }
            }
        }
        headGameObjects = headGameObjects.Distinct().ToList();
        var list = (ListView)HeadObjectList.Children().First();
        list.Rebuild();
    }
    FlexaPose GetFlexaPose()
    {
        if(avatarGameObject == null)
        {
            return null;
        }
        var avatarPoseMeta = avatarGameObject?.GetComponent<FlexaPose>();
        if (avatarPoseMeta == null)
        {
            avatarPoseMeta = avatarGameObject?.AddComponent<FlexaPose>();
            avatarPoseMeta.rightFoot.rotation = Quaternion.identity;
            avatarPoseMeta.leftFoot.rotation = Quaternion.identity;
            avatarPoseMeta.centerEye.rotation = Quaternion.identity;
            poseRotation = Quaternion.identity;

        }
        return avatarPoseMeta;
    }
    private void DropGameObject(bool isScene, string sceneFile, string[] paths, GameObject gameObject)
    {
        if (!gameObject || PrefabUtility.IsPartOfModelPrefab(gameObject) || (PrefabUtility.IsPartOfPrefabAsset(gameObject) && !PrefabUtility.IsPartOfPrefabInstance(gameObject)))
        {
            status.AddStatus("Add it to the hierarchy first, unpack it too if need be (FBX/GLB).");
            return;
        }
        avatarGameObject = gameObject;
        currentFlexaPose = GetFlexaPose();
        SetCenterText();
        SetLeftText();
        SetRightText();
        RefreshAvatarView();
        GetHeadObjects();
    }
    private void DropFile(bool isScene, string sceneFile, string[] paths, GameObject gameObject)
    {
        if (!isScene)
        {
            // Only scene files are accepted; the old prefab-drop flow no longer exists.
            status.AddStatus("Only .unity scene files can be dropped here.");
            return;
        }
        scenePathLabel.text = scenePath = sceneFile;
        mode = BSBuilderBundleMode.Scene;
        ProjectPrefs.SetString("BanterBuilder_ScenePath", scenePath);
        RefreshView();
    }

    private void RefreshAvatarView(bool ignoreBones = false)
    {
        if (avatarGameObject == null || (!ignoreBones && !ValidateAvatarBones()))
        {
            dropAvatarContainer.style.display = DisplayStyle.Flex;
            AvatarInfoCard.style.display = DisplayStyle.None;
        }
        else
        {
            dropAvatarContainer.style.display = DisplayStyle.None;
            AvatarInfoCard.style.display = DisplayStyle.Flex;
            SelectAvatar.text = avatarGameObject.name;
            currentFlexaPose = GetFlexaPose();

            var humanoidAnim = avatarGameObject.GetComponent<Animator>();
            if (humanoidAnim != null && humanoidAnim.isHuman)
            {
                currentFlexaPose.headTransform = humanoidAnim.GetBoneTransform(HumanBodyBones.Head);
                currentFlexaPose.leftFootTransform = humanoidAnim.GetBoneTransform(HumanBodyBones.LeftFoot);
                currentFlexaPose.rightFootTransform = humanoidAnim.GetBoneTransform(HumanBodyBones.RightFoot);
                foreach (var t in avatarGameObject.GetComponentsInChildren<Transform>())
                {
                    if (t.GetComponent<Renderer>() != null && t.name.ToLower().Contains("head") && headGameObjects.Count == 0)
                    {
                        headGameObjects.Add(t.gameObject);
                        var list = (ListView)HeadObjectList.Children().First();
                        list.Rebuild();
                        break;
                    }
                }
            }
            else
            {
                var bones = AvatarBoneNames.AvatarBoneNamesMapping;
                foreach (var t in avatarGameObject.GetComponentsInChildren<Transform>())
                {
                    if (t.GetComponent<Renderer>() != null && t.name.ToLower().Contains("head") && headGameObjects.Count == 0)
                    {
                        headGameObjects.Add(t.gameObject);
                        var list = (ListView)HeadObjectList.Children().First();
                        list.Rebuild();
                    }
                    if (bones.ContainsKey(t.name))
                    {
                        switch (bones[t.name])
                        {
                            case AvatarBoneName.HEAD:
                                currentFlexaPose.headTransform = t;
                                break;
                            case AvatarBoneName.LEFTLEG_FOOT:
                                currentFlexaPose.leftFootTransform = t;
                                break;
                            case AvatarBoneName.RIGHTLEG_FOOT:
                                currentFlexaPose.rightFootTransform = t;
                                break;

                        }
                    }
                }
            }

        }

        AvatarRef.Instance.SetAvatarGameObject(avatarGameObject);

    }
    private void RefreshView(bool skipLoginRefresh = false)
    {
        scenePathParent.style.display = DisplayStyle.None;
        sceneStatsParent.style.display = DisplayStyle.None;
        dropAreaContainer.style.display = DisplayStyle.None;
        MainTitle.style.display = DisplayStyle.None;
        if (mode == BSBuilderBundleMode.Scene)
        {
            scenePathParent.style.display = DisplayStyle.Flex;
            scenePathLabel.text = "<color=\"white\">Scene:</color> " + scenePath;

            ShowSceneStats(new[] { scenePath });
            loggedInViewScene.style.display = sq.User == null ? DisplayStyle.None : DisplayStyle.Flex;
            buildOptions.style.display = DisplayStyle.Flex;
            loggedInCTAScene.style.display = DisplayStyle.Flex;
            MainTitle.text = "Scene Build";
            MainTitle.style.display = DisplayStyle.Flex;
            // button to open the webroot folder - highlight in unity.
        }
        else
        {
            loggedInViewScene.style.display = DisplayStyle.None;
            buildOptions.style.display = DisplayStyle.None;
            loggedInCTAScene.style.display = DisplayStyle.None;
            dropAreaContainer.style.display = DisplayStyle.Flex;
            MainTitle.style.display = DisplayStyle.None;
        }
        if (!skipLoginRefresh)
        {
            loginManager.ShowUploadToggle();
        }
    }

    // Quick triangle-count / texture-memory readout for whatever was just dropped in - works
    // directly off the asset dependency graph so it doesn't need the scene to be open. Not a
    // full breakdown (see Bundle Analyzer for that), just an at-a-glance sanity check.
    private void ShowSceneStats(IEnumerable<string> assetPaths)
    {
        var paths = assetPaths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (paths.Count == 0)
        {
            sceneStatsParent.style.display = DisplayStyle.None;
            return;
        }

        var stats = SceneQuickStats.Compute(paths);
        sceneStatsLabel.text = SceneQuickStats.FormatSummary(stats);
        sceneStatsParent.style.display = DisplayStyle.Flex;
    }

    public void OpenSpaceCreation()
    {
        Application.OpenURL("https://altvr.app/worlds");
    }
    // Sign-in page for the short code. Must be the same site as the API the window talks to
    // (see isTestEnvironment) — a code minted by one environment won't validate on the other.
    const string SQ_LINK_PAGE_URL = "https://altvr.app/link";

    public void OpenLinkPage()
    {
        if (loginManager == null)
        {
            Application.OpenURL(SQ_LINK_PAGE_URL);
            return;
        }
        // Ask the login manager for a code that is still redeemable (it fetches a fresh one if the
        // current code has expired) and pre-fill it so the user doesn't have to retype it.
        loginManager.PrepareSignIn((loginCode) =>
        {
            var code = loginCode?.Code;
            var url = string.IsNullOrEmpty(code)
                ? SQ_LINK_PAGE_URL
                : SQ_LINK_PAGE_URL + "?code=" + Uri.EscapeDataString(code);
            Application.OpenURL(url);
        });
    }

    // The selected world's slug + full hosting URL (null if none selected), and a readiness guard. These
    // replace the old free-text space-slug field; scene uploads key off the selected world.
    private string SelectedWorldSlug => selectedWorld?.Slug;
    private string SelectedWorldUrl => selectedWorld?.SpaceUrl;
    private bool HasSelectedWorld => selectedWorld != null && !string.IsNullOrEmpty(selectedWorld.Slug);

    // Shows the selected world's hosting URL under the dropdown (blank when nothing's selected).
    private void UpdateWorldUrlLabel()
    {
        if (worldUrlLabel == null)
            return;
        worldUrlLabel.text = SelectedWorldUrl ?? "";
    }

    /// <summary>
    /// (Re)loads the signed-in user's worlds into the dropdown. Preserves/restores the selection: prefers
    /// <paramref name="selectWorldId"/> (e.g. a freshly-created world), else the last-used world from prefs,
    /// else the first world. Safe to call repeatedly.
    /// </summary>
    private IEnumerator RefreshWorlds(string selectWorldId = null)
    {
        if (sq == null || sq.User == null)
            yield break;

        string preferId = !string.IsNullOrEmpty(selectWorldId)
            ? selectWorldId
            : ProjectPrefs.GetString("BanterBuilder_selectedWorldId", "");

        yield return sq.ListWorlds(list =>
        {
            worlds = list ?? new List<SqEditorWorld>();
            worldDropdown.choices = worlds.Select(w => w.Name).ToList();

            int idx = -1;
            if (!string.IsNullOrEmpty(preferId))
                idx = worlds.FindIndex(w => w.WorldId == preferId);
            if (idx < 0 && worlds.Count > 0)
                idx = 0;

            worldDropdown.index = idx;
            // Set selectedWorld directly too — the index setter only fires the value-changed callback when
            // the displayed string actually changes, which isn't guaranteed on a refresh.
            selectedWorld = idx >= 0 ? worlds[idx] : null;
            if (selectedWorld != null)
            {
                ProjectPrefs.SetString("BanterBuilder_selectedWorldId", selectedWorld.WorldId.ToString());
                WorldLinkStamp.StampIfOpen(scenePath, selectedWorld.WorldId, selectedWorld.Slug);
            }
            if (worlds.Count == 0)
                worldDropdown.value = "No worlds — create one";
            UpdateWorldUrlLabel();
        }, e =>
        {
            status.AddStatus("Failed to load worlds: " + e.Message);
            Debug.LogException(e);
        });
    }

    /// <summary>
    /// Opens the "create world" modal, then creates the world, refreshes the list and selects the new one.
    /// </summary>
    private void OpenCreateWorld()
    {
        if (sq == null || sq.User == null)
        {
            status.AddStatus("Sign in before creating a world.");
            return;
        }
        CreateWorldWindow.Open(name =>
            EditorCoroutineUtility.StartCoroutine(CreateWorldRoutine(name), this));
    }

    private IEnumerator CreateWorldRoutine(string name)
    {
        status.AddStatus("Creating world '" + name + "'…");
        SqEditorWorld created = null;
        yield return sq.CreateWorld(name, w => created = w, e =>
        {
            status.AddStatus("Failed to create world: " + e.Message);
            Debug.LogException(e);
        });
        if (created == null)
            yield break;

        status.AddStatus("Created world '" + created.Name + "'.");
        // Reload the list from the API (authoritative) and select the just-created world.
        yield return RefreshWorlds(created.WorldId);
    }
    private void ShowBuildConfirm()
    {
        buildConfirm.style.display = DisplayStyle.Flex;
        confirmBuildMode.text = "<color=\"white\">Build Mode:</color> Scene Bundle";
        confirmSceneFile.text = "<color=\"white\">Scene File:</color> " + scenePath;
        confirmSpaceCode.text = "<color=\"white\">World:</color> " + (string.IsNullOrEmpty(SelectedWorldUrl) ? ("https://" + SelectedWorldSlug + ".worldspace.host") : SelectedWorldUrl);

        // Runtime graph edits that this project has already absorbed. Worth saying out loud here
        // because the upload is what makes removing them from the world safe, and removing them is
        // not obviously part of "upload a world".
        var syncedNote = RuntimeOverridePrune.ConfirmationLine();
        confirmSyncedGraphs.style.display = syncedNote == null ? DisplayStyle.None : DisplayStyle.Flex;
        confirmSyncedGraphs.text = syncedNote ?? "";
    }
   void AddRemoveFlexaHead()
    {
        bool isDirty = false;
        try
        {
            foreach (var t in avatarGameObject?.GetComponentsInChildren<Transform>())
            {
                var flexaHead = t.GetComponent<FlexaHead>();
                if (headGameObjects.Contains(t.gameObject) && flexaHead == null)
                {
                    t.gameObject.AddComponent<FlexaHead>();
                    isDirty = true;
                }
                else if (flexaHead != null && !headGameObjects.Contains(t.gameObject))
                {
                    DestroyImmediate(flexaHead);
                    isDirty = true;
                }
            }
        }catch (Exception e)
        {
            Debug.LogWarning("Error occurred while adding/removing FlexaHead components: " + e);
        }
        if (isDirty)
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
    public void DrawReorderableList(List<GameObject> sourceList, VisualElement rootVisualElement, bool allowSceneObjects = true)
    {
        var list = new ListView(sourceList)
        {
            virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
            showFoldoutHeader = true,
            headerTitle = "  Head Objects",
            showAddRemoveFooter = true,
            reorderMode = ListViewReorderMode.Animated,
            makeItem = () =>
            {
                return new ObjectField
                {
                    objectType = typeof(GameObject),
                    allowSceneObjects = allowSceneObjects
                };
            },
            bindItem = (element, i) =>
            {
                AddRemoveFlexaHead();
                ((ObjectField)element).value = sourceList[i];
                ((ObjectField)element).RegisterValueChangedCallback((value) =>
                {
                    try
                    {
                        // Debug.Log("replace...");
                        sourceList[i] = (GameObject)value.newValue;
                        // Debug.Log("Replaced item at index " + i + " with: " + value.newValue + " (" + sourceList.Count() + ")" + " (" + headGameObjects.Count() + ")");
                        AddRemoveFlexaHead();
                        headGameObjects = sourceList;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Failed to add item to list: " + e);
                    }
                });
            },
            unbindItem = (element, i) => AddRemoveFlexaHead(),
            destroyItem = (element) => AddRemoveFlexaHead()
        };

        rootVisualElement.Add(list);
    }
    
    bool ValidateAvatarBones()
    {

        if (avatarGameObject == null)
        {
            status.AddStatus("No avatar selected, please select an avatar.");
            return false;
        }

        var humanoidAnim = avatarGameObject.GetComponent<Animator>();
        if (humanoidAnim != null && humanoidAnim.isHuman)
        {
            return ValidateHumanoidAvatar(humanoidAnim);
        }

        var bones = AvatarBoneNames.AvatarBoneNamesMapping;
        var hasBones = new Dictionary<AvatarBoneName, Transform>();

        foreach (var bone in avatarGameObject.GetComponentsInChildren<Transform>())
        {
            if (bones.ContainsKey(bone.name))
            {
                try
                {
                    hasBones.Add(bones[bone.name], bone);
                }
                catch
                {
                    // Debug.LogWarning("Duplicate bone name found: " + bone.name + ", " + bones[bone.name] + " skipping.");
                }
            }
        }
        bool hasAllBones = true;
        string missingBones = "";
        int i = 0;
        foreach (var value in Enum.GetValues(typeof(AvatarBoneName)))
        {
            if (!hasBones.ContainsKey((AvatarBoneName)value))
            {
                hasAllBones = false;
                missingBones += AvatarBoneNames.AvatarBoneNamesReverseMapping[(AvatarBoneName)value] + (i % 2 == 0 ? ", " : ", ");

            }
            i++;
        }
        if (!hasAllBones)
        {
            status.AddStatus("Avatar has missing bones!");
            MissingBones.text = "Avatar (" + (avatarGameObject?.name??"<Unknown>") + ") has missing bones:\n\n<color=#AFAFAF>" + missingBones.Substring(0, missingBones.Length - 1) + "</color>";
            return false;
        }
        MissingBones.text = "";
        return true;
    }

    bool ValidateHumanoidAvatar(Animator anim)
    {
        // Required bones (Hips, Spine, arms, legs, Head) are guaranteed when isHuman == true.
        // Just surface any unmapped optional bones as an info note; always pass.
        var optional = new[]
        {
            HumanBodyBones.Neck,
            HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
            HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
            HumanBodyBones.LeftEye, HumanBodyBones.RightEye,
        };
        var missing = optional.Where(b => anim.GetBoneTransform(b) == null).Select(b => b.ToString()).ToList();
        if (missing.Count > 0)
        {
            MissingBones.text = "Humanoid avatar (" + avatarGameObject.name + "). Optional bones not mapped on the Avatar — fingers/eyes/toes/etc. will fall back gracefully:\n\n<color=#AFAFAF>" + string.Join(", ", missing) + "</color>";
        }
        else
        {
            MissingBones.text = "";
        }
        return true;
    }
    bool AnyBoneHasScale()
    {
        var renderers = avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var renderer in renderers)
        {
            if (renderer.bones.Any(b => !IsIdentityScale(b.localScale)))
            {
                status.AddStatus("Avatar has bones with scale applied, this will cause issues in Banter.");
                MissingBones.text = "Avatar has bones with scale applied, this will cause issues in Banter.";
                return true;
            }
        }
        return false;
    }
    bool IsIdentityScale(Vector3 scale, float tolerancePercent = 0.001f)
    {
        return Vector3.Distance(scale, Vector3.one) < tolerancePercent;
    }
    bool IsIdentityRotation(Quaternion q, float toleranceDegrees = 0.001f)
    {
        return Mathf.Abs(Quaternion.Angle(q, Quaternion.identity)) < toleranceDegrees;
    }
    bool RootHasRotation()
    {
        var renderers = avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        List<Transform> aboveRootBones = new List<Transform>();
        foreach (var renderer in renderers)
        {
            aboveRootBones.AddRange(renderer.rootBone.parent.GetComponentsInParent<Transform>());
        }
        return aboveRootBones.Any(b => !IsIdentityRotation(b.rotation));
    }

    void RemoveCameras()
    {
        var cameras = avatarGameObject?.GetComponentsInChildren<Camera>();
        foreach (var camera in cameras)
        {
            DestroyImmediate(camera);
        }
    }

    private async Task<bool> BuildAvatarAssetBundles()
    {
        if (sq.User == null)
        {
            status.AddStatus("You need to be signed in to upload an avatar!");
            MissingBones.text = "You need to be signed in to upload an avatar!";
            return false;
        }

        if (RootHasRotation())
        {
            if (!EditorUtility.DisplayDialog("ROOT ROTATED", "The root gameobject has rotation applied, this might be offset in the bones but will cause rotation problems in Banter.", "Continue", "Cancel"))
            {
                return false;
            }
        }
        if (AnyBoneHasScale())
        {
            if (!EditorUtility.DisplayDialog("BONES SCALED", "The avatar has bones with scale applied, this might be offset in the bones but will cause scaling problems in Banter.", "Continue", "Cancel"))
            {
                return false;
            }
        }
        RemoveCameras();
        var basisProp = avatarGameObject.GetComponent<BasisProp>();
        if (basisProp == null)
        {
            basisProp = avatarGameObject.AddComponent<BasisProp>();
        }

        foreach (var headObj in headGameObjects)
        {
            var flexaHead = headObj.GetComponent<FlexaHead>();
            if (flexaHead == null)
            {
                headObj.AddComponent<FlexaHead>();
            }
        }
        if (!ValidateAvatarBones())
        {
            return false;
        }
        try
        {
            headGameObjects = headGameObjects.Distinct().ToList();
            var list = (ListView)HeadObjectList.Children().First();
            list.Rebuild();
            basisProp.BasisBundleDescription = new BasisBundleDescription
            {
                AssetBundleName = "BasisAvatar"
            };
            List<BuildTarget> buildTargets = new List<BuildTarget>
            {
                BuildTarget.Android,
                BuildTarget.StandaloneWindows,
            };
            // TODO(greenfield): Phase-2 builder rework — replace userId+"42069" with the single greenfield key
            // and drop the SideQuest CDN upload. Signature adapted to upstream Basis (leading Image arg, tuple return);
            // BasisProp kept intentionally (no far-LOD / no glTF fallback in Greenfield).
            await BasisBundleBuild.GameObjectBundleBuild("", basisProp, buildTargets, true, sq.User.UserId + "42069");
            var path = "AssetBundles";
            var files = Directory.GetFiles(path, "*.txt", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                File.Delete(file);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to get avatar bones: " + e);
            return false;
        }
    }
    private void BuildAssetBundles()
    {
        if (mode == BSBuilderBundleMode.None)
        {
            status.AddStatus("Nothing to build...");
            return;
        }
        if (mode == BSBuilderBundleMode.Scene && string.IsNullOrWhiteSpace(scenePath))
        {
            status.AddStatus("No scene selected...");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }
        ShowBuildConfirm();
        confirmCallback = async () => {
            // Basis' scene build switches the active build target, which schedules a domain reload.
            // That reload is deferred until this async method yields — at which point it destroys the
            // continuation and the auto-upload coroutine before they run (build succeeds, nothing
            // uploads, no error). Lock reloads across the scene build + upload and release only once the
            // upload finishes (handed to its completion callback) or here on any early exit, so the
            // reload lands after we're done.
            bool reloadLockHeld = false;
            bool lockHandedToUpload = false;
            try
            {
                if (!ValidateVisualScripting.CheckVsNodes())
                {
                    status.AddStatus("Found disallowed visual scripting nodes, please check the logs for more information.");
                    return;
                }
                else
                {
                    status.AddStatus("Visual Scripting check passed!");
                }
                status.AddStatus("Build started...");

                if (!Directory.Exists(Path.Join(assetBundleRoot, assetBundleDirectory)))
                {
                    Directory.CreateDirectory(Path.Join(assetBundleRoot, assetBundleDirectory));
                }

                List<string> names = new List<string>();

                // Greenfield spaces build to a single platform-agnostic encrypted Basis bundle
                // (asset.world). Every platform loads it and ranged-GETs its own section; the
                // runtime falls back to legacy per-platform windows.banter / android.banter.
                EditorApplication.LockReloadAssemblies();
                reloadLockHeld = true;
                names = await BuildSpaceBeeBundles();
                if (names.Count == 0)
                {
                    status.AddStatus("Build failed. See the console for details.");
                    return;
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
                // The bar is upload-only now; BuildPipeline shows Unity's own
                // popup while building. (This used to poke the bar from a
                // background Task, which touched UI off the main thread.)
                status.AddStatus("Build finished.");

                if (autoUpload.value && sq.User != null)
                {
                    if (!HasSelectedWorld) {
                        status.AddStatus("No world selected, please select or create a world to upload.");
                        return;
                    }
                    uploadWebOnly.SetEnabled(false);
                    uploadEverything.SetEnabled(false);
                    // Hand the reload lock to the upload: it releases in UploadEverything's callback
                    // (which always fires, even on failure), so the deferred reload lands only after
                    // the upload is done rather than mid-flight.
                    bool unlockAfterUpload = reloadLockHeld;
                    lockHandedToUpload = reloadLockHeld;
                    EditorCoroutineUtility.StartCoroutine(UploadEverything(() =>
                    {
                        status.AddStatus("Upload complete.");
                        uploadWebOnly.SetEnabled(true);
                        uploadEverything.SetEnabled(true);
                        if (unlockAfterUpload) EditorApplication.UnlockReloadAssemblies();
                    }), this);
                }
            }
            finally
            {
                // Safety net: if we locked reloads but never handed the unlock to an upload coroutine
                // (early return, no upload, or an exception), release it here so reloads never stay
                // locked (which would otherwise wedge script recompilation until an editor restart).
                if (reloadLockHeld && !lockHandedToUpload)
                    EditorApplication.UnlockReloadAssemblies();
            }
        };
    }
    /// <summary>
    /// Builds the selected scene into a single platform-agnostic encrypted Basis bundle, written to
    /// WebRoot as <c>asset.world</c>. Basis' build pipeline already concatenates every requested platform
    /// into one <c>.BEE</c> (behind a <c>BasisBundleConnector</c> header of per-platform byte ranges), and
    /// the runtime ranged-GETs only the section it needs — so we pass all checked targets to a single
    /// <c>SceneBundleBuild</c> call, exactly as the avatar builder does, rather than one file per platform.
    /// Uses the shared Greenfield key.
    ///
    /// <c>SceneBundleBuild</c> bundles the *open* scene (it derives the scene from a
    /// <see cref="BasisContentBase"/> in it), whereas the builder tracks a scene *asset path* — so we
    /// open the scene, drop in a throwaway <c>BasisProp</c> for it to hang the description off, build into
    /// a scratch dir, and lift just the <c>.bee</c> out. The marker is stripped from the shipped copy by
    /// <see cref="CustomSceneProcessor"/> and removed from the source scene here. Returns the produced
    /// file names (empty on failure).
    /// </summary>
    private async Task<List<string>> BuildSpaceBeeBundles()
    {
        var produced = new List<string>();
        string webRoot = Path.Join(assetBundleRoot, assetBundleDirectory);

        BasisAssetBundleObject settings =
            AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
        if (settings == null)
        {
            status.AddStatus("Basis build settings asset not found. Is Basis installed?");
            return produced;
        }

        // Suppress Basis' post-build "reveal output folder in Explorer" popup. Basis reads
        // OpenFolderOnDisc from whatever asset BasisAssetBundleObject.AssetBundleObject points at, and
        // re-loads it mid-build — so an in-memory flip doesn't stick, and in a consuming project the real
        // asset is a read-only package asset we can't edit. That path is a mutable static string, though,
        // so we point it at a throwaway on-disk clone of the settings with OpenFolderOnDisc off. The clone
        // is a writable Assets asset, so the value survives Basis' mid-build reload. Restored + the clone
        // deleted in finally.
        string origSettingsPath = BasisAssetBundleObject.AssetBundleObject;
        string tempSettingsPath = null;
        if (settings.OpenFolderOnDisc)
        {
            try
            {
                BasisAssetBundleObject quiet = UnityEngine.Object.Instantiate(settings);
                quiet.OpenFolderOnDisc = false;
                string p = "Assets/__GreenfieldSpaceBuildSettings.asset";
                AssetDatabase.DeleteAsset(p); // clear any leftover from an interrupted build
                AssetDatabase.CreateAsset(quiet, p);
                BasisAssetBundleObject.AssetBundleObject = p;
                tempSettingsPath = p;
                settings = quiet; // use the clone's values below — the same ones Basis now reads
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Greenfield] Couldn't suppress Basis output-folder reveal; continuing: " + e.Message);
                BasisAssetBundleObject.AssetBundleObject = origSettingsPath;
                tempSettingsPath = null;
            }
        }

        // Remember what was open so we can restore it. Modified scenes were already saved by the confirm
        // gate (SaveCurrentModifiedScenesIfUserWantsTo) before we got here.
        string previouslyOpen = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        bool reopenNeeded = previouslyOpen != scenePath;
        string descriptionName = string.IsNullOrEmpty(SelectedWorldSlug) ? "GreenfieldSpace" : SelectedWorldSlug;

        // We can't redirect where Basis writes the .bee: BasisSceneBuildName renames the scene asset
        // mid-build, which reloads the Basis settings object (and in a consuming project it's a read-only
        // package asset), so an in-memory AssetBundleDirectory override is discarded. So Basis writes to
        // its own default ({AssetBundleDirectory}/{name}/{guid}.BEE); we read that location and lift the
        // .bee out afterwards. AssetBundleDirectory is typically "./AssetBundles" (relative to the project).
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string basisOutRoot = Path.IsPathRooted(settings.AssetBundleDirectory)
            ? settings.AssetBundleDirectory
            : Path.GetFullPath(Path.Combine(projectRoot, settings.AssetBundleDirectory));

        try
        {
            // One combined build over every selected platform. Basis' BuildBundle loops the targets and
            // CombineFiles concatenates each platform's section into a single .BEE behind a
            // BasisBundleConnector header of byte ranges; the runtime ranged-GETs only the section it needs.
            // So a single SceneBundleBuild call (all targets) yields one platform-agnostic asset.world,
            // rather than one file per platform. (This is exactly how the avatar builder builds.)
            var targets = new List<BuildTarget>();
            for (int i = 0; i < buildTargets.Length; i++)
                if (buildTargetFlags[i]) targets.Add(buildTargets[i]);

            if (targets.Count == 0)
            {
                status.AddStatus("No build targets selected.");
                return produced;
            }

            const string outName = "asset.world";
            status.AddStatus("Building: " + outName + " (" + string.Join(", ", targets) + ")");

            // Open the scene once and drop in the throwaway BasisProp SceneBundleBuild hangs the scene +
            // description off. A single build call sidesteps the old per-platform loop's marker-staleness
            // problem (SceneBundleBuild reloads the scene asset and CustomSceneProcessor strips the marker
            // during the build, so a marker reference couldn't survive into a second call). The marker is
            // read once here, stripped from every shipped section by CustomSceneProcessor, and removed from
            // the source scene asset in the finally block.
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameObject markerGo = GameObject.Find(CustomSceneProcessor.SceneBeeBuildMarkerName)
                                  ?? new GameObject(CustomSceneProcessor.SceneBeeBuildMarkerName);
            BasisProp marker = markerGo.GetComponent<BasisProp>() ?? markerGo.AddComponent<BasisProp>();
            marker.BasisBundleDescription = new BasisBundleDescription { AssetBundleName = descriptionName };

            bool ok;
            string message;
            CustomSceneProcessor.isBuildingSceneBee = true;
            try
            {
                (ok, message) = await BasisBundleBuild.SceneBundleBuild(
                    Image: "",
                    BasisContentBase: marker,
                    Targets: targets,
                    useProvidedPassword: true,
                    OverriddenPassword: GreenfieldBundleCrypto.Password);
            }
            finally
            {
                CustomSceneProcessor.isBuildingSceneBee = false;
            }

            if (!ok)
            {
                status.AddStatus("Build failed (" + outName + "): " + message);
                return produced;
            }

            // Basis writes to {AssetBundleDirectory}/{MakeSafeFolderName(name)}/{guid}.BEE. Lift the newest
            // .bee out of that exact folder into WebRoot as the single combined asset.world.
            string beeFolder = Path.Combine(basisOutRoot, BasisBundleBuild.MakeSafeFolderName(descriptionName));
            string bee = Directory.Exists(beeFolder)
                ? Directory
                    .GetFiles(beeFolder, "*" + settings.BasisEncryptedExtension, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (string.IsNullOrEmpty(bee))
            {
                status.AddStatus("Build produced no " + settings.BasisEncryptedExtension + " in " + beeFolder + ".");
                return produced;
            }

            File.Copy(bee, Path.Combine(webRoot, outName), true);
            // Never leave the plaintext password sidecar Basis drops next to the .bee lying around.
            // (Leave the .bee itself — Basis reveals this folder, so it should show the built bundle.)
            try
            {
                foreach (string sidecar in Directory.GetFiles(beeFolder, settings.ProtectedPasswordFileName + "*.txt"))
                    File.Delete(sidecar);
            }
            catch { /* sidecar tidy-up only */ }
            produced.Add(outName);
        }
        catch (Exception e)
        {
            status.AddStatus("Space build failed: " + e.Message);
            Debug.LogException(e);
            produced.Clear();
        }
        finally
        {
            // Restore the real Basis settings path and drop the temporary quiet-settings clone.
            BasisAssetBundleObject.AssetBundleObject = origSettingsPath;
            if (tempSettingsPath != null)
                AssetDatabase.DeleteAsset(tempSettingsPath);

            // The builds saved the marker into the scene asset — reopen, strip it, and persist a clean
            // scene, then restore whatever the user had open.
            UnityEngine.SceneManagement.Scene cleanup = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameObject leftover = GameObject.Find(CustomSceneProcessor.SceneBeeBuildMarkerName);
            if (leftover != null)
            {
                DestroyImmediate(leftover);
                EditorSceneManager.MarkSceneDirty(cleanup);
                EditorSceneManager.SaveScene(cleanup);
            }
            if (reopenNeeded && !string.IsNullOrEmpty(previouslyOpen))
                EditorSceneManager.OpenScene(previouslyOpen, OpenSceneMode.Single);

            AssetDatabase.Refresh();
        }

        return produced;
    }

    // ---- in-window upload progress -------------------------------------------
    // Building already gets Unity's own popup, so this bar is upload-only.
    // An upload is a series of files; each file owns an equal slice of the bar
    // and its byte-level callback fills that slice, so a large asset bundle
    // still animates instead of sitting on one number.
    int uploadStepIndex;
    int uploadStepTotal;

    void BeginUploadProgress(int totalSteps)
    {
        uploadStepIndex = 0;
        uploadStepTotal = Mathf.Max(1, totalSteps);
    }

    Action<float> NextUploadStep(string label)
    {
        uploadStepIndex++;
        var step = uploadStepIndex;
        var title = $"{label}  ({step}/{uploadStepTotal})";
        status.ShowProgress(title, 100f * (step - 1) / uploadStepTotal);
        return fraction => status.ShowProgress(title,
            100f * ((step - 1) + Mathf.Clamp01(fraction)) / uploadStepTotal);
    }

    void EndUploadProgress(string label)
    {
        status.ShowProgress(label, 100f);
        EditorCoroutineUtility.StartCoroutine(HideProgressBarAfter(3f), this);
    }

    IEnumerator HideProgressBarAfter(float seconds)
    {
        yield return new EditorWaitForSeconds(seconds);
        HideProgressBar();
    }

    void HideProgressBar()
    {
        status.HideProgressBar();
    }
}
