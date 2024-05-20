using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Banter;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

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

    public static UnityEvent OnCompileAll = new UnityEvent();
    public static UnityEvent OnClearAll = new UnityEvent();
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

    public void OnEnable()
    {
        VisualElement content = _mainWindowVisualTree.CloneTree();
        content.style.height = new StyleLength(Length.Percent(100));
        rootVisualElement.styleSheets.Add(_mainWindowStyleSheet);
        rootVisualElement.Add(content);
        SetupUI();
    }

    void SetupUI()
    {
        var buildButton = rootVisualElement.Q<Button>("buildButton");

        buildButton.clicked += () => BuildAssetBundles();

        var buildForAndroid = rootVisualElement.Q<Toggle>("buildForAndroid");
        buildForAndroid.RegisterCallback<MouseUpEvent>((e) =>
        {
            EditorPrefs.SetBool("BanterBuilder_BuildTarget_Android", buildForAndroid.value);
            buildTargetFlags[0] = buildForAndroid.value;
        });

        var buildForWindows = rootVisualElement.Q<Toggle>("buildForWindows");
        buildForWindows.RegisterCallback<MouseUpEvent>((e) =>
        {
            EditorPrefs.SetBool("BanterBuilder_BuildTarget_Windows", buildForWindows.value);
            buildTargetFlags[1] = buildForWindows.value;
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
        if (!string.IsNullOrEmpty(scenePath))
        {
            mode = BanterBuilderBundleMode.Scene;
            RefreshView();
        }

#if BANTER_EDITOR
            rootVisualElement.Q<Button>("allAndInjection").clicked += () =>{
                OnCompileAll.Invoke();
                OnCompileInjection.Invoke();
                // SDKCodeGen.CompileAllComponents();
                // SDKCodeGen.CompileInjection();
            };
            rootVisualElement.Q<Button>("allOnly").clicked += () => OnCompileAll.Invoke();// SDKCodeGen.CompileAllComponents();
            rootVisualElement.Q<Button>("clearAll").clicked += () => OnClearAll.Invoke();// SDKCodeGen.ClearAllComponents();
            rootVisualElement.Q<Button>("compileElectron").clicked += () => OnCompileElectron.Invoke();// SDKCodeGen.CompileElectron();
            rootVisualElement.Q<Button>("compileInjection").clicked += () => OnCompileInjection.Invoke();// SDKCodeGen.CompileInjection();
            rootVisualElement.Q<Button>("kitchenSink").clicked += () => OnCompileAll.Invoke();// SDKCodeGen.CompileAll();
            Remove(rootVisualElement.Q<Button>("setupVisualScripting"));
            Remove(rootVisualElement.Q<Button>("setupLayers"));
#else
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
    void AddStatus(string text)
    {
        statusMessages.Add("<color=\"orange\">" + DateTime.Now.ToString("HH:mm:ss") + ": <color=\"white\">" + text);
        if (statusMessages.Count > 300)
        {
            statusMessages = statusMessages.GetRange(0, 300);
        }
        buildProgress.Rebuild();
    }

    public void Remove(VisualElement element)
    {
        element.parent.Remove(element);
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
            mainTitle.text = "<u>BUILDING A KIT BUNDLE</u>";
            mainTitle.style.display = DisplayStyle.Flex;
            removeSelected.style.display = DisplayStyle.Flex;
            kitListView.style.display = DisplayStyle.Flex;

            kitListView.itemsSource = kitObjectList;
            kitListView.Rebuild();
        }
        else if (mode == BanterBuilderBundleMode.Scene)
        {
            mainTitle.text = "<u>BUILDING A SCENE BUNDLE</u>";
            mainTitle.style.display = DisplayStyle.Flex;
            scenePathLabel.style.display = DisplayStyle.Flex;
            scenePathLabel.text = "SCENE PATH: " + scenePath;
        }
        else
        {
            mainTitle.style.display = DisplayStyle.None;
            mainTitle.text = "";
        }
        ShowRemoveSelected();
    }
    private void BuildAssetBundles()
    {
        // statusMessages.Clear();
        // buildProgressBar.style.display = DisplayStyle.Flex;
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
        // if (EditorUtility.DisplayDialog("Are you sure?", "This will clear any files in the output folder.", "Continue", "Cancel")) {

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
        Debug.Log(Path.Join(assetBundleRoot, assetBundleDirectory) + "/" + assetBundleDirectory + ".manifest");
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
        // }
    }
    void HideProgressBar()
    {
        buildProgressBar.style.display = DisplayStyle.None;
    }

}
