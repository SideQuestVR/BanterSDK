// Portions of this code are from https://github.com/spatialsys/spatial-unity-sdk
// Retrieved on 2024-06-04
// SPDX-License-Identifier: MIT

#if BANTER_VISUAL_SCRIPTING
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Unity.VisualScripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Specialized;
using UnityEngine.AI;
using Banter.SDK;

namespace Banter.SDKEditor
{
    public static class NodeGeneration
    {
        private const string SETTINGS_ASSET_PATH = "ProjectSettings/VisualScriptingSettings.asset";
        private const string GENERATED_VS_NODES_VERSION_PREFS_KEY = "Banter_GeneratedVSNodesVersion";

        static NodeGeneration()
        {
            // if (PlayerPrefs.GetString(GENERATED_VS_NODES_VERSION_PREFS_KEY) != PackageManagerUtility.currentVersion)
            // {
            //     EditorApplication.update += CheckForNodeRegen;
            // }
        }

        private static void CheckForNodeRegen()
        {
            //in order to do a proper unit rebuild, we need to wait until we know VS has been initialized.
            //waiting until a VS window is opened is the best way I have found to do this
            if (EditorWindow.HasOpenInstances<GraphWindow>())
            {
                //EditorApplication.update -= CheckForNodeRegen;
                UnityEditor.EditorUtility.DisplayDialog("Banter Scripting Initialization", "Hold tight while we make sure your visual scripting settings are just right", "OK");
                SetVSTypesAndAssemblies();
            }
        }

        /// <summary>
        /// We need to set the supported types and assemblies in ProjectSettings/VisualScripting
        /// Ludiq/Unity really does not want us to edit this... so we have to directly edit the json of the file >:(
        /// </summary>
        public static void SetVSTypesAndAssemblies()
        {
            // Initializes internal data structures and editor logic for Visual Scripting.
            VSUsageUtility.isVisualScriptingUsed = true;

            if (!File.Exists(SETTINGS_ASSET_PATH))
            {
                const string MESSAGE = "Visual Scripting is not initialized. Please navigate to the Visual Scripting settings in the Unity project settings to initialize";
                if (!Application.isBatchMode)
                    UnityEditor.EditorUtility.DisplayDialog("Visual Scripting Settings Not Found", MESSAGE, "OK");
                throw new System.Exception(MESSAGE);
            }

            // There's an embedded JSON in this asset file. Manually replace the assembly and type arrays.
            string settingsAssetContents = File.ReadAllText(SETTINGS_ASSET_PATH);

            // Replace assembly options array
            settingsAssetContents = settingsAssetContents.SetJSONArrayValueHelper("assemblyOptions", assemblyAllowList);

            // Replace type options array
            var typesToGenerate = new List<Type>(typeAllowList);
            settingsAssetContents = settingsAssetContents.SetJSONArrayValueHelper("typeOptions", typesToGenerate.Select(type => type.FullName));

            File.WriteAllText(SETTINGS_ASSET_PATH, settingsAssetContents);

            AssetDatabase.Refresh();
            CompilationPipeline.RequestScriptCompilation();
        }

        public static readonly HashSet<string> assemblyAllowList = new HashSet<string>() {
            //"BanterSDK",

            "mscorlib",
            "Assembly-CSharp-firstpass",
            "Assembly-CSharp",

            "UnityEngine",
            "UnityEngine.CoreModule",
            "UnityEngine.PhysicsModule",
            "UnityEngine.AudioModule",
            "UnityEngine.AnimationModule",
            "UnityEngine.VideoModule",
            "UnityEngine.DirectorModule",
            "UnityEngine.Timeline",
            "UnityEngine.ParticleSystemModule",
            "UnityEngine.ParticlesLegacyModule",
            "UnityEngine.WindModule",
            "UnityEngine.ClothModule",
            "UnityEngine.TilemapModule",
            "UnityEngine.SpriteMaskModule",
            "UnityEngine.AIModule",
            "UnityEngine.UIElementsModule",
            "UnityEngine.StyleSheetsModule",
            "UnityEngine.JSONSerializeModule",
            "UnityEngine.UmbraModule",
            "Unity.Timeline",
            "Unity.Timeline.Editor",
            "Cinemachine",
            "com.unity.cinemachine.editor",
            "Unity.TextMeshPro",

            //Note! This assembly is actually forcebly included in the VS assembly list.
            "Unity.VisualScripting.Core",//AotList & AotDictionary
            
            "Unity.VisualScripting.Flow",//contains all the if, for, while, etc nodes
            "Unity.VisualScripting.State",//state graph nodes (enter, exit)
        };

        public static readonly List<Type> typeAllowList = new List<Type>() {
            //Default VS types:
            typeof(object),
            typeof(bool),
            typeof(int),
            typeof(uint),
            typeof(short),
            typeof(ushort),
            typeof(long),
            typeof(ulong),
            typeof(float),
            typeof(double),
            typeof(byte),
            typeof(string),
            typeof(char),
            typeof(TimeSpan),
            typeof(Vector2),
            typeof(Vector3),
            typeof(Vector4),
            typeof(Quaternion),
            typeof(Matrix4x4),
            typeof(Rect),
            typeof(Bounds),
            typeof(Color),
            typeof(AnimationCurve),
            typeof(LayerMask),
            typeof(Ray),
            typeof(Ray2D),
            typeof(RaycastHit),
            typeof(RaycastHit2D),
            typeof(ContactPoint),
            typeof(ContactPoint2D),
            typeof(ParticleCollisionEvent),
            typeof(Mathf),
            typeof(Debug),
            typeof(Exception),
            typeof(Time),
            typeof(DateTime),
            typeof(UnityEngine.Random),
            typeof(Physics),
            typeof(Physics2D),
            typeof(GUI),
            typeof(GUILayout),
            typeof(GUIUtility),
            typeof(AudioMixerGroup),
            typeof(AnimatorStateInfo),
            typeof(BaseEventData),
            typeof(PointerEventData),
            typeof(AxisEventData),
            typeof(IList),
            typeof(IDictionary),
            typeof(IOrderedDictionary),
            typeof(OrderedDictionary),
            typeof(ICollection),
            typeof(AotList),
            typeof(AotDictionary),
            typeof(WheelCollider),
            typeof(WheelFrictionCurve),
            typeof(WheelHit),
            typeof(JointSpring),
            typeof(ArrayList),
            typeof(CombineInstance),

            //Additional types
            typeof(ParticleSystem),

            //AI Classes
            //Subset of AI features that seem the most useful from: https://docs.unity3d.com/ScriptReference/UnityEngine.AIModule.html
            typeof(NavMesh),
            typeof(NavMeshAgent),
            typeof(NavMeshBuilder),
            typeof(NavMeshData),
            typeof(NavMeshObstacle),
            typeof(NavMeshPath),
            typeof(OffMeshLink),
            //AI Structs
            typeof(NavMeshHit),
            typeof(NavMeshLinkData),
            typeof(NavMeshLinkInstance),
            typeof(NavMeshQueryFilter),
            typeof(NavMeshTriangulation),
            //AI Enums
            typeof(NavMeshObstacleShape),
            typeof(NavMeshPathStatus),
            typeof(ObstacleAvoidanceType),
            typeof(OffMeshLinkType),

            // Banter
            // typeof(Banter.SDK.BanterAssetBundle),
            // typeof(Banter.SDK.BanterAudioSource),
            // typeof(Banter.SDK.BanterBillboard),
            // typeof(Banter.SDK.BanterBrowser),
            // typeof(Banter.SDK.BanterConfigurableJoint),
            // typeof(Banter.SDK.BanterGeometry),
            // typeof(Banter.SDK.BanterGLTF),
            // typeof(Banter.SDK.BanterKitItem),
            // typeof(Banter.SDK.BanterMaterial),
            // typeof(Banter.SDK.BanterMirror),
            // typeof(Banter.SDK.BanterObjectId),
            // typeof(Banter.SDK.BanterPhysicMaterial),
            // typeof(Banter.SDK.BanterPortal),
            // typeof(Banter.SDK.BanterRigidbody),
            // typeof(Banter.SDK.BanterStreetView),
            // typeof(Banter.SDK.BanterText),
            // typeof(Banter.SDK.BanterTransform),
            // typeof(Banter.SDK.BanterVideoPlayer),

            // typeof(Banter.SDK.BanterBoxCollider),
            // typeof(Banter.SDK.BanterCapsuleCollider),
            // typeof(Banter.SDK.BanterColliderEvents),
            // typeof(Banter.SDK.BanterInvertedMesh),
            // typeof(Banter.SDK.BanterMeshCollider),
            // typeof(Banter.SDK.BanterSphereCollider),
        };

        private static string SetJSONArrayValueHelper(this string target, string arrayContainerName, IEnumerable<string> arrayContents)
        {
            // Match pattern of "<arrayContainerName>":{"$content":[<any # of characters>]
            Regex reg = new Regex($"(\"{arrayContainerName}\":{{\"\\$content\":\\[)(.*)(\\])");
            string jsonArrayContents = string.Join(',', arrayContents.Select(s => $"\"{s}\""));
            // Only replace the second capture group, since that contains current array contents.
            return reg.Replace(target, $"$1{jsonArrayContents}$3");
        }

// #if !BANTER_EDITOR
//         [InitializeOnLoadMethod]
//         private static void OnScriptsReloaded()
//         {
//             if (PlayerPrefs.GetString(GENERATED_VS_NODES_VERSION_PREFS_KEY) == PackageManagerUtility.currentVersion)
//             {
//                 UnitBase.Rebuild();
//             }
//         }
// #endif
    }
}
#endif
