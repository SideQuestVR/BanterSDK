using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Banter.SDKEditor;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static BuilderWindow;

namespace Banter.SDK
{
    [CustomEditor(typeof(BanterAvatar))]
    public class BanterAvatarEditor : Editor
    {
        public VisualTreeAsset visualTree;

        Label mainTitle;
        ListView buildProgress;
        ProgressBar buildProgressBar;
        Label statusBar;

        Label codeText;
        Label statusText;
        Label uploadEverything;

        Toggle autoUpload;

        VisualElement loggedInView;


        Label confirmSlot;

        Label confirmBuild;
        Button cancelBuild;

        VisualElement buildConfirm;

        VisualElement deleteConfirm;
        Label confirmDelete;
        Button cancelDelete;

        Label buildButton;
        Action confirmCallback;
        Action deleteCallback;

        private bool[] buildTargetFlags = new bool[] { true, true };
        VisualElement rootVisualElement;
        public BanterAvatar BanterAvatar;

        private SqEditorAppApi sq;

        Status status;

        public void OnEnable()
        {
            Debug.Log(File.Exists("Packages/com.sidequest.banter/Editor/Resources/Builder/BanterAvatarEditor.uxml"));
            visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.sidequest.banter/Editor/Resources/Builder/BanterAvatarEditor.uxml");
            BanterAvatar = (BanterAvatar)target;
        }

        LoginManager loginManager;

        public override VisualElement CreateInspectorGUI()
        {
            BanterAvatar = (BanterAvatar)target;
            var rootElement = new VisualElement();
            rootElement.style.marginLeft = -15;
            rootElement.style.marginTop = -3;
            rootElement.style.marginRight = -7;
            rootElement.style.marginBottom = -7;
            // Draw default inspector elements first
            // InspectorElement.FillDefaultInspector(rootElement, serializedObject, this);

            if (visualTree != null)
            {
                rootVisualElement = visualTree.CloneTree();
                rootElement.Add(rootVisualElement);
                new TabsManager(rootVisualElement);
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


                // SetupTabs();
                // BasisSDKCommonInspector.CreateBuildTargetOptions(uiElementsRoot);
                // BasisSDKCommonInspector.CreateBuildOptionsDropdown(uiElementsRoot);

                // BasisAssetBundleObject assetBundleObject = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
                // Button BuildButton = BasisHelpersGizmo.Button(uiElementsRoot, BasisSDKConstants.BuildButton);
                // BuildButton.clicked += () => Build(BuildButton, assetBundleObject.selectedTargets);
            }
            else
            {
                Debug.LogError("VisualTree is null. Make sure the UXML file is assigned correctly.");
            }

            return rootElement;
        }

        private void SetupUI()
        {
            statusBar = rootVisualElement.Q<Label>("StatusBar");
            new TabsManager(rootVisualElement);
            buildButton = rootVisualElement.Q<Label>("buildButton");

            buildButton.RegisterCallback<MouseUpEvent>((e) => { });

            // var createSpace = rootVisualElement.Q<Label>("CreateSpace");
            // createSpace.RegisterCallback<MouseUpEvent>((e) => { });
            // var openWebRoot = rootVisualElement.Q<Button>("OpenWebRoot");

            // openWebRoot.clicked += () => ShowWebRoot();

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
                EditorPrefs.SetBool("BanterAvatar_AutoUpload", autoUpload.value);
                loginManager.SetBuildButtonText();
            });

            confirmSlot = rootVisualElement.Q<Label>("ConfirmSlot");


            deleteConfirm = rootVisualElement.Q<VisualElement>("DeleteConfirm");

            confirmDelete = rootVisualElement.Q<Label>("ConfirmDelete");
            cancelDelete = rootVisualElement.Q<Button>("CancelDelete");
            cancelDelete.RegisterCallback<MouseUpEvent>((e) =>
            {
                deleteConfirm.style.display = DisplayStyle.None;
            });
            confirmDelete.RegisterCallback<MouseUpEvent>((e) =>
            {
                deleteConfirm.style.display = DisplayStyle.None;
                deleteCallback?.Invoke();
            });
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
            statusText = rootVisualElement.Q<Label>("SignedInStatus");
            // uploadEverything = rootVisualElement.Q<Label>("UploadEverything");
            loggedInView = rootVisualElement.Q<VisualElement>("LoggedInView");


            // uploadEverything.RegisterCallback<MouseUpEvent>((e) =>
            // {
                
            //     // ShowBuildConfirm();
            //     confirmCallback = () =>
            //     {
            //         confirmCallback = null;
            //         uploadEverything.SetEnabled(false);
            //         // EditorCoroutineUtility.StartCoroutine(UploadEverything(() =>
            //         // {
            //         //     status.AddStatus("Upload complete.");
            //         //     uploadEverything.SetEnabled(true);
            //         // }), this);
            //     };
            // });


            mainTitle = rootVisualElement.Q<Label>("mainTitle");
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
    }
}
