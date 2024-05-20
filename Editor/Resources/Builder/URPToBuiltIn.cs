using UnityEngine;
using UnityEditor;
using System.Linq;
using UnityEngine.UIElements;

public class URPToBuiltIn : EditorWindow
{
    // Options to select shader
    private string[] options = new string[] { "Standard", "Mobile/Diffuse" };
    private int selectedIndex = 0;

    [MenuItem("Banter/Tools/URP/URPToBuiltIn")]
    public static void OpenWindow()
    {
        URPToBuiltIn window = GetWindow<URPToBuiltIn>();
        window.titleContent = new GUIContent("Convery URP materials to Built-In");
    }

    public void OnEnable()
    {
        rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/Builder/dnd.uss"));
        var container = new VisualElement();
        // var toggleContainer = new VisualElement();

        UnityEngine.UIElements.Label mainTitle = new UnityEngine.UIElements.Label();
        mainTitle.text = "<b>Warning:</b> This will convert all materials in the project from URP to Built-In, this\n cannot be undone, please make sure you have a backup of your\n project before proceeding.";
        mainTitle.AddToClassList("scene-title");
        rootVisualElement.Add(mainTitle);

        // UnityEngine.UIElements.Label sceneName = new UnityEngine.UIElements.Label();
        // sceneName.AddToClassList("scene-name");

        // rootVisualElement.Add(sceneName);

        // UnityEngine.UIElements.Toggle includeAndroid = new UnityEngine.UIElements.Toggle();
        // includeAndroid.text = "Build for Android";
        // includeAndroid.AddToClassList("toggle-box");
        // includeAndroid.value = buildTargetFlags[0];

        // UnityEngine.UIElements.Toggle includeWindows = new UnityEngine.UIElements.Toggle();
        // includeWindows.text = "Build for Windows";
        // includeWindows.AddToClassList("toggle-box");
        // includeWindows.value = buildTargetFlags[1];

        // includeAndroid.RegisterCallback<MouseUpEvent>(IncludeAndroid);
        // includeWindows.RegisterCallback<MouseUpEvent>(IncludeWindows);

        // toggleContainer.Add(includeAndroid);
        // toggleContainer.Add(includeWindows);

        // container.Add(toggleContainer);
        UnityEngine.UIElements.Button buildButton = new UnityEngine.UIElements.Button();
        buildButton.AddToClassList("build-button");
        container.Add(buildButton);
        buildButton.text = "Convert Materials from\nURP to Built-In (Standard)";
        buildButton.RegisterCallback<MouseUpEvent>((MouseUpEvent _) =>
        {
            selectedIndex = 0;
            ConvertMaterials();
        });

        UnityEngine.UIElements.Button buildButton2 = new UnityEngine.UIElements.Button();
        buildButton2.AddToClassList("build-button");
        container.Add(buildButton2);
        buildButton2.text = "Convert Materials from\nURP to Built-In (Mobile/Diffuse)";
        buildButton2.RegisterCallback<MouseUpEvent>((MouseUpEvent _) =>
        {
            selectedIndex = 1;
            ConvertMaterials();
        });


        rootVisualElement.Add(container);
    }
    private void ConvertShaders(MouseUpEvent _)
    {

    }
    // private void OnGUI()
    // {
    //     selectedIndex = EditorGUILayout.Popup(selectedIndex, options);

    //     if (GUILayout.Button("Convert Materials"))
    //     {
    //         ConvertMaterials();
    //     }
    // }

    private void ConvertMaterials()
    {
        // Get all the material asset paths in the project
        string[] materialPaths = AssetDatabase.GetAllAssetPaths().Where(path => path.EndsWith(".mat")).ToArray();

        // Iterate through all the material paths
        foreach (string materialPath in materialPaths)
        {
            // Load the material
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            // Skip this material if it's not using a URP shader or Error shader
            if (!material.shader.name.StartsWith("Universal Render Pipeline") &&
                !material.shader.name.Equals("Hidden/InternalErrorShader"))
            {
                continue;
            }

            // Save properties with defaults
            var color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            var mainTexture = material.mainTexture;
            var metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
            var smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0f;

            // Check if it's a transparent material
            bool isTransparent = material.HasProperty("_Surface") && material.GetFloat("_Surface") == 1.0f;

            // Change the shader of the material based on the selected option
            switch (selectedIndex)
            {
                case 0:
                    material.shader = Shader.Find("Standard");
                    break;
                case 1:
                    material.shader = Shader.Find("Mobile/Diffuse");
                    break;
            }

            // Restore properties
            material.SetColor("_Color", color);
            material.SetTexture("_MainTex", mainTexture);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);

            // If it was a transparent material, set the rendering mode to transparent
            if (isTransparent && selectedIndex == 0) // Only applies to Standard shader
            {
                material.SetFloat("_Mode", 2.0f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            // Save the changes to this material
            EditorUtility.SetDirty(material);
        }

        // Save the changes to all materials
        AssetDatabase.SaveAssets();
    }
}