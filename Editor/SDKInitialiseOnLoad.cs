using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using BS;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using LongBunnyLabs;
using Unity.EditorCoroutines.Editor;
using UnityEditor.Build;
using System.Linq;

namespace BS.SDKEditor
{
    [InitializeOnLoad]
    public static class InitialiseOnLoad
    {
        static InitialiseOnLoad()
        {
#if !BANTER_EDITOR
            ImportBasisPackages();
            ImportOraPackage();
            SetupLayersAndTags();
            SetApiCompatibilityLevel();
            CreateWebRoot();
            // CreateUninstaller();
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                if (Object.FindObjectOfType<BSStarterUpper>() == null)
                {
                    Debug.LogWarning("BSStarterUpper not found, adding one.");
                    var go = new GameObject("BSStarterUpper");
                    go.AddComponent<BSStarterUpper>();
                }
            }
        }
        static void AddScriptDefine(string define)
        {
            var buildTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var symbols = PlayerSettings.GetScriptingDefineSymbols(buildTarget);
            symbols = string.Join(";", symbols.Split(";").Where(d => !string.IsNullOrWhiteSpace(d)));
            PlayerSettings.SetScriptingDefineSymbols(buildTarget, symbols + ";" + define);
        }
        static void SetApiCompatibilityLevel()
        {
            // Upstream Basis targets .NET Standard 2.1 (ApiCompatibilityLevel.NET_Standard). The old
            // Banter fork ran on NET_Unity_4_8; forcing that here would break the upstream packages.
            var level = PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup);
            if (level == ApiCompatibilityLevel.NET_Standard)
            {
                return;
            }
            PlayerSettings.SetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup, ApiCompatibilityLevel.NET_Standard);
        }
//         static void CreateUninstaller()
//         {
//             var script = $@"
// using UnityEditor;
// using System.IO;
// using UnityEditor.PackageManager;
// using UnityEditor.PackageManager.Requests;
// namespace BS.SDKEditor
// {{
//     public class UninstallBanter
//     {{
// // #if !BANTER_EDITOR
//         [MenuItem(""Banter/Uninstall SDK"")]
//         static void UninstallBanter()
//         {{
//             RemoveRequest request = Client.Remove(""com.sidequest.banter"");
//             while(!request.IsCompleted)
//             {{
                
//             }}
//             EditUtils.RemoveCompileDefine(""BANTER_ORA"", new BuildTargetGroup[] {{ BuildTargetGroup.Android, BuildTargetGroup.Standalone }});
//             EditUtils.RemoveCompileDefine(""BASIS_BUNDLE_MANAGEMENT"", new BuildTargetGroup[] {{ BuildTargetGroup.Android, BuildTargetGroup.Standalone }});
//             if(Directory.Exists(""Packages/com.basis.bundlemanagement""))
//             {{
//                 Directory.Delete(""Packages/com.basis.bundlemanagement"");
//             }}
//             if(Directory.Exists(""Packages/com.basis.sdk""))
//             {{
//                 Directory.Delete(""Packages/com.basis.sdk"");
//             }}
//             if(Directory.Exists(""Packages/com.basis.odinserializer""))
//             {{
//                 Directory.Delete(""Packages/com.basis.odinserializer"");
//             }}
//             if (Directory.Exists(""Packages/com.sidequest.ora""))
//             {{
//                 Directory.Delete(""Packages/com.sidequest.ora"");
//             }}
//             if (Directory.Exists(""Packages/com.sidequest.banteruninstaller""))
//             {{
//                 Directory.Delete(""Packages/com.sidequest.banteruninstaller"");
//             }}
//         }}
// // #endif
//     }}
// }}
//             ";

//             var packageJson = $@"
//             {{
//                 ""name"": ""com.sidequest.banteruninstaller"",
//                 ""version"": ""0.0.1"",
//                 ""displayName"": ""Banter SDK Uninstaller"",
//                 ""description"": ""Removing the Banter SDK"",
//                 ""unity"": ""2022.3"",
//                 ""unityRelease"": ""39f1"",
//                 ""hideInEditor"": false,
//                 ""documentationUrl"": ""https://bantervr.com/documentation"",
//                 ""dependencies"": {{
//                 }},
//                 ""author"": {{
//                     ""name"": ""SideQuest"",
//                     ""email"": ""banter@sidequestvr.com"",
//                     ""url"": ""https://bantervr.com""
//                 }}
//             }}";

//             if (!Directory.Exists("Packages/com.sidequest.banteruninstaller"))
//             {
//                 Directory.CreateDirectory("Packages/com.sidequest.banteruninstaller");
//             }
//             if (!Directory.Exists("Packages/com.sidequest.banteruninstaller/Editor"))
//             {
//                 Directory.CreateDirectory("Packages/com.sidequest.banteruninstaller/Editor");
//             }
//             if(!File.Exists("Packages/com.sidequest.banteruninstaller/pacakge.json"))
//             {
//                 File.WriteAllText("Packages/com.sidequest.banteruninstaller/pacakge.json", packageJson);
//             }
//             if(!File.Exists("Packages/com.sidequest.banteruninstaller/Editor/UninstallBanter.cs"))
//             {
//                 File.WriteAllText("Packages/com.sidequest.banteruninstaller/Editor/UninstallBanter.cs", script);
//             }
//         }
        static void ImportOraPackage()
        {
            var packageName = "com.sidequest.ora";
            if (Directory.Exists("Packages/" + packageName))
            {
#if !BANTER_ORA
                AddScriptDefine("BANTER_ORA");
#endif
                return;
            }
            if (!EditorUtility.DisplayDialog("Install Ora", "Install the Ora package?  (Required)", "OK", "Cancel"))
            {
                return;
            }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string zipDirectory = Path.Combine(projectRoot, "Packages/com.sidequest.banter/OraPackage");
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
            AddScriptDefine("BANTER_ORA");
        }
        // The Basis packages (and BouncyCastle, which Basis' bundle encryption needs) are shipped as
        // zips under BasisPackages/ and extracted into the consumer's Packages/ folder as embedded
        // (file:) packages. Upstream Basis pulls these via VRChat's VPM, which Greenfield doesn't use,
        // so we vendor and extract them directly. Registry-resolvable deps (glTFast, Animation Rigging,
        // URP, Addressables, Mathematics, ...) are declared in package.json instead and resolve
        // automatically — they don't belong here.
        static readonly string[] BasisPackages = new string[]
        {
            "com.basis.common",
            "com.basis.sdk",
            "com.basis.bundlemanagement",
            "com.sidequest.thirdparty.bouncycastle",
        };

        static void ImportBasisPackages()
        {
            bool allPresent = true;
            foreach (string packageName in BasisPackages)
            {
                if (!Directory.Exists("Packages/" + packageName))
                {
                    allPresent = false;
                    break;
                }
            }
            if (allPresent)
            {
#if !BASIS_BUNDLE_MANAGEMENT
                AddScriptDefine("BASIS_BUNDLE_MANAGEMENT");
#endif
                return;
            }
            // NB: don't touch ProjectPrefs here — it lazily creates/loads an .asset via AssetDatabase,
            // which NREs when this [InitializeOnLoad] static ctor runs during a fresh project's first
            // import (the DB isn't ready yet). The old hasAlreadyAttempted* keys were write-only dead
            // code anyway.
            if (!EditorUtility.DisplayDialog("Install Basis", "Install the Basis packages? (Required)", "OK", "Cancel"))
            {
                return;
            }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string zipDirectory = Path.Combine(projectRoot, "Packages/com.sidequest.banter/BasisPackages");

            string[] packages = BasisPackages;

            foreach (string packageName in packages)
            {
                string zipPath = Path.Combine(zipDirectory, $"{packageName}.zip");
                string extractRoot = Path.Combine(projectRoot, "Packages");
                string extractPath = Path.Combine(extractRoot, packageName);

                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(zipPath, extractRoot);
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
            AddScriptDefine("BASIS_BUNDLE_MANAGEMENT");
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

                if (isMissing)
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
