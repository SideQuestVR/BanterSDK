using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Banter.SDKEditor
{
    public class InitialiseOnLoad
    {
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
            { 22, "WalkingLegs" },
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
        static AddRequest Request;
        [InitializeOnLoadMethod()]
        static void Go()
        {
            SetupLayers();
            CreateWebRoot();
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

        public static void SetupLayers()
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
                        missingLayers.Add("L" + layer.Key + ": " + layer.Value);
                    }
                }

                if (isMissing && EditorUtility.DisplayDialog("Missing Banter Layers", "Do you want to setup Banter layers automatically?\nThese are required when using Banter specific features:\n" + string.Join(", \n", missingLayers), "Yes", "No"))
                {
                    foreach (var layer in layersToAdd)
                    {
                        var ulayer = layers.GetArrayElementAtIndex(layer.Key);
                        if (ulayer == null || ulayer.stringValue != layer.Value)
                        {
                            AddLayerAt(layers, layer.Key, layer.Value);
                        }
                    }
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                }
            }
        }

        static void AddLayerAt(SerializedProperty layers, int index, string layerName, bool tryOtherIndex = false)
        {
            // Skip if a layer with the name already exists.
            for (int i = 0; i < layers.arraySize; ++i)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                {
                    Debug.Log("Skipping layer '" + layerName + "' because it already exists.");
                    return;
                }
            }

            // Extend layers if necessary
            if (index >= layers.arraySize)
                layers.arraySize = index + 1;

            // set layer name at index
            var element = layers.GetArrayElementAtIndex(index);
            // if (string.IsNullOrEmpty(element.stringValue))
            // {
            element.stringValue = layerName;
            Debug.Log("Added layer '" + layerName + "' at index " + index + ".");
            // }
            // else
            // {
            //     Debug.LogWarning("Could not add layer at index " + index + " because there already is another layer '" + element.stringValue + "'." );

            //     if (tryOtherIndex)
            //     {
            //         // Go up in layer indices and try to find an empty spot.
            //         for (int i = index + 1; i < 32; ++i)
            //         {
            //             // Extend layers if necessary
            //             if (i >= layers.arraySize)
            //                 layers.arraySize = i + 1;

            //             element = layers.GetArrayElementAtIndex(i);
            //             if (string.IsNullOrEmpty(element.stringValue))
            //             {
            //                 element.stringValue = layerName;
            //                 Debug.Log("Added layer '" + layerName + "' at index " + i + " instead of " + index + ".");
            //                 return;
            //             }
            //         }

            //         Debug.LogError("Could not add layer " + layerName + " because there is no space left in the layers array.");
            //     }
            // }
        }
    }
}
