using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Banter.SDK;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using LongBunnyLabs;
using Unity.EditorCoroutines.Editor;
using UnityEditor.Build;

namespace Banter.SDKEditor
{
    [InitializeOnLoad]
    public static class InitialiseOnLoad
    {
        static InitialiseOnLoad()
        {
            var renderPipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            if( renderPipeline != null)
            {
                if (!EditorUtility.DisplayDialog("WRONG RENDER PIPELINE", "This project was created with URP but needs to use BiRP. Please select 3D(Built in Render Pipeline) in a new project and discard this one.", "OK", "Close Unity"))
                {
                    EditorApplication.Exit(0);
                    return;
                }
                else
                {
                    return;
                }
            }
#if !BANTER_EDITOR
#if !UNITY_2022
            ImportBasisPackages();
#endif
            ImportOraPackage();
            SetupLayersAndTags();
            SetApiCompatibilityLevel();
            CreateWebRoot();
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                if (Object.FindObjectOfType<BanterStarterUpper>() == null)
                {
                    Debug.LogWarning("BanterStarterUpper not found, adding one.");
                    var go = new GameObject("BanterStarterUpper");
                    go.AddComponent<BanterStarterUpper>();
                }
            }
        }
        static void SetApiCompatibilityLevel()
        {
            var level = PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup);
            if (level == ApiCompatibilityLevel.NET_Unity_4_8)
            {
                return;
            }
            PlayerSettings.SetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup, ApiCompatibilityLevel.NET_Unity_4_8);
        }
        static void ImportOraPackage()
        {
            var packageName = "com.sidequest.ora";
            if (Directory.Exists("Packages/" + packageName))
            {
#if !BANTER_ORA
                PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, "BANTER_ORA");
#endif
                return;
            }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string zipDirectory = Path.Combine(projectRoot, "Packages/" + packageName + "/OraPackage");
            string zipPath = Path.Combine(zipDirectory, $"com.sidequest.ora.zip");
            string extractRoot = Path.Combine(projectRoot, "Packages");
            string extractPath = Path.Combine(extractRoot, packageName);

            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);

            ZipFile.ExtractToDirectory(zipPath, extractRoot);            
            
            // Modify manifest.json
            string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            string json = File.ReadAllText(manifestPath);

            var jObject = JObject.Parse(json);
            var dependencies = (JObject)jObject["dependencies"];

            dependencies[packageName] = $"file:{packageName}";

            File.WriteAllText(manifestPath, jObject.ToString());
            AssetDatabase.Refresh();
            Debug.Log("All Ora packages installed and manifest.json updated.");    
            PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, "BANTER_ORA");
        }
        static void ImportBasisPackages()
        {
            if (Directory.Exists("Packages/com.basis.bundlemanagement") &&
               Directory.Exists("Packages/com.basis.sdk") &&
               Directory.Exists("Packages/com.basis.odinserializer"))
            {
#if !BASIS_BUNDLE_MANAGEMENT
                PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, "BASIS_BUNDLE_MANAGEMENT");
#endif
                return;
            }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string zipDirectory = Path.Combine(projectRoot, "Packages/com.sidequest.banter/BasisPackages");

            string[] packages = new string[]
            {
                "com.basis.bundlemanagement",
                "com.basis.sdk",
                "com.basis.odinserializer"
            };

            foreach (string packageName in packages)
            {
                string zipPath = Path.Combine(zipDirectory, $"{packageName}.zip");
                string extractRoot = Path.Combine(projectRoot, "Packages");
                string extractPath = Path.Combine(extractRoot, packageName);

                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(zipPath, extractRoot);
                Debug.Log($"Extracted {packageName} to {extractPath}");
            }

            // Modify manifest.json
            string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            string json = File.ReadAllText(manifestPath);

            var jObject = JObject.Parse(json);
            var dependencies = (JObject)jObject["dependencies"];

            foreach (string packageName in packages)
            {
                dependencies[packageName] = $"file:{packageName}";
            }

            File.WriteAllText(manifestPath, jObject.ToString());
            AssetDatabase.Refresh();
            Debug.Log("All Basis packages installed and manifest.json updated.");
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), "BASIS_BUNDLE_MANAGEMENT");
        }
        static void CreateWebRoot()
        {
            // TODO: Add more into the boilerplate like examples, meta tags for stuff thats global, etc
#if !BANTER_EDITOR
            var webRoot = Application.dataPath + "/WebRoot";
            if (Directory.Exists(webRoot))
                return;
            Directory.CreateDirectory(webRoot);
            File.WriteAllText(webRoot + "/index.html", "<html android-bundle windows-bundle><head>");
#endif
        }

        public static void SetupLayersAndTags()
        {
            Object[] asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset != null && asset.Length > 0)
            {
                SerializedObject serializedObject = new SerializedObject(asset[0]);


                SerializedProperty layers = serializedObject.FindProperty("layers");
                bool isMissing = false;
                List<string> missingLayers = new List<string>();
                foreach (var layer in layersToAdd)
                {
                    var ulayer = layers.GetArrayElementAtIndex(layer.Key);
                    if (ulayer == null || ulayer.stringValue != layer.Value)
                    {
                        isMissing = true;
                        missingLayers.Add(layer.Value);
                    }
                }

                SerializedProperty tags = serializedObject.FindProperty("tags");
                List<string> missingTags = new List<string>();
                foreach (var tag in tagsToAdd)
                {
                    var utag = tags.GetArrayElementAtIndex(tag.Key);
                    if (utag == null || utag.stringValue != tag.Value)
                    {
                        isMissing = true;
                        missingTags.Add(tag.Value);
                    }
                }

                if (isMissing && EditorUtility.DisplayDialog("Missing Banter Layers/Tags", "Do you want to setup Banter layers and tags automatically?\nThese are required when using Banter specific features.\n Please back up your project first!" + (missingLayers.Count > 0 ? "\n\nLayers:\n" : "") + string.Join(", ", missingLayers) + (missingTags.Count > 0 ? "\n\nTags:\n" : "") + string.Join(", ", missingTags), "Yes", "No"))
                {
                    foreach (var layer in layersToAdd)
                    {
                        var ulayer = layers.GetArrayElementAtIndex(layer.Key);
                        if (ulayer == null || ulayer.stringValue != layer.Value)
                        {
                            AddTagManagerObjectAt(layers, "layer", layer.Key, layer.Value);
                        }
                    }

                    foreach (var tag in tagsToAdd)
                    {
                        var utag = tags.GetArrayElementAtIndex(tag.Key);
                        if (utag == null || utag.stringValue != tag.Value)
                        {
                            AddTagManagerObjectAt(tags, "tag", tag.Key, tag.Value);
                        }
                    }

                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                }
            }
        }

        static void AddTagManagerObjectAt(SerializedProperty prop, string semantic, int index, string name, bool tryOtherIndex = false)
        {
            // Skip if an object with the name already exists.
            for (int i = 0; i < prop.arraySize; ++i)
            {
                if (prop.GetArrayElementAtIndex(i).stringValue == name)
                {
                    Debug.Log($"Skipping {semantic} '{name}' because it already exists.");
                    return;
                }
            }

            // Extend layers if necessary
            if (index >= prop.arraySize)
                prop.arraySize = index + 1;

            // set layer name at index
            var element = prop.GetArrayElementAtIndex(index);

            element.stringValue = name;
            Debug.Log($"Added {semantic} '{name}' at index {index}.");
        }

        public static Dictionary<int, string> layersToAdd = new Dictionary<int, string> {
            { 3, "UserLayer1" },
            { 6, "UserLayer2" },
            { 7, "UserLayer3" },
            { 8, "UserLayer4" },
            { 9, "UserLayer5" },
            { 10, "UserLayer6" },
            { 11, "UserLayer7" },
            { 12, "UserLayer8" },
            { 13, "UserLayer9" },
            { 14, "UserLayer10" },
            { 15, "UserLayer11" },
            { 16, "UserLayer12" },
            { 17, "NetworkPlayer" },
            { 18, "RPMAvatarHead" },
            { 19, "RPMAvatarBody" },
            { 20, "Grabbable" },
            { 21, "HandColliders" },
            { 22, "Menu" },
            { 23, "PhysicsPlayer" },
            { 24, "BanterInternal1_DONTUSE" },
            { 25, "BanterInternal2_DONTUSE" },
            { 26, "BanterInternal3_DONTUSE" },
            { 27, "BanterInternal4_DONTUSE" },
            { 28, "BanterInternal5_DONTUSE" },
            { 29, "BanterInternal6_DONTUSE" },
            { 30, "BanterInternal7_DONTUSE" },
            { 31, "BanterInternal8_DONTUSE" }
        };

        public static Dictionary<int, string> tagsToAdd = new Dictionary<int, string> {
            { 0,  "__BA_NameTag" },
            { 1,  "__BA_NameTagMenu" },
            { 2,  "__BA_FootRig" },
            { 3,  "__BA_PlayerHead" },
            { 4,  "__BA_UNUSED0" },
            { 5,  "__BA_UNUSED1" },
            { 6,  "__BA_TriggerIndex" },
            { 7,  "__BA_PlayerTorso" },
            { 8,  "__BA_PlayerLegs" },
            { 9,  "__BA_LocalPlayer" },
            { 10, "__BA_PlayerLeftHand" },
            { 11, "__BA_PlayerRightHand" },
            { 12, "__BA_LocalPlayerFeet" },
            { 13, "__BA_UserTag0" },
            { 14, "__BA_UserTag1" },
            { 15, "__BA_UserTag2" },
            { 16, "__BA_UserTag3" },
            { 17, "__BA_UserTag4" },
            { 18, "__BA_UserTag5" },
            { 19, "__BA_UserTag6" },
            { 20, "__BA_UserTag7" },
            { 21, "__BA_UserTag8" },
            { 22, "__BA_UserTag9" },
            { 23, "__BA_UserTag10" },
            { 24, "__BA_UserTag11" },
            { 25, "__BA_UserTag12" },
            { 26, "__BA_UserTag13" },
            { 27, "__BA_UserTag14" },
            { 28, "MenuWorldSpace" },
            { 29, "VRPlayerContextMenu" },
            { 30, "PortalBall" },
        };
    }
}
