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

namespace Banter.SDKEditor
{
    public static class VsNodeGeneration
    {
        private const string SETTINGS_ASSET_PATH = "ProjectSettings/VisualScriptingSettings.asset";
        private const string GENERATED_VS_NODES_VERSION_PREFS_KEY = "Banter_GeneratedVSNodesVersion";

        // static NodeGeneration()
        // {
        //     if (PlayerPrefs.GetString(GENERATED_VS_NODES_VERSION_PREFS_KEY) != PackageManagerUtility.currentVersion)
        //     {
        //         EditorApplication.update += CheckForNodeRegen;
        //     }
        // }

#if !BANTER_EDITOR
        [InitializeOnLoadMethod]
        private static void OnScriptsReloaded()
        {
            if (PlayerPrefs.GetString(GENERATED_VS_NODES_VERSION_PREFS_KEY) != PackageManagerUtility.currentVersion)
            {
                VSUsageUtility.isVisualScriptingUsed = true;
                SetVSTypesAndAssemblies();
            }
        }
#endif

        // private static void CheckForNodeRegen()
        // {
        //     //in order to do a proper unit rebuild, we need to wait until we know VS has been initialized.
        //     //waiting until a VS window is opened is the best way I have found to do this
        //     if (EditorWindow.HasOpenInstances<GraphWindow>())
        //     {
        //         EditorApplication.update -= CheckForNodeRegen;
        //         UnityEditor.EditorUtility.DisplayDialog("Banter Scripting Initialization", "Hold tight while we make sure your visual scripting settings are just right", "OK");
                
        //         SetVSTypesAndAssemblies();
        //     }
        // }

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

            UnitBase.Rebuild();

            PlayerPrefs.SetString(GENERATED_VS_NODES_VERSION_PREFS_KEY, PackageManagerUtility.currentVersion);
            PlayerPrefs.Save();
        }

        private static string SetJSONArrayValueHelper(this string target, string arrayContainerName, IEnumerable<string> arrayContents)
        {
            // Match pattern of "<arrayContainerName>":{"$content":[<any # of characters>]
            Regex reg = new Regex($"(\"{arrayContainerName}\":{{\"\\$content\":\\[)(.*)(\\])");
            string jsonArrayContents = string.Join(',', arrayContents.Select(s => $"\"{s}\""));
            // Only replace the second capture group, since that contains current array contents.
            return reg.Replace(target, $"$1{jsonArrayContents}$3");
        }

        public static readonly HashSet<string> assemblyAllowList = new HashSet<string>() {
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
            "Unity.TextMeshPro",

            //Note! This assembly is actually forcebly included in the VS assembly list.
            "Unity.VisualScripting.Core",//AotList & AotDictionary
            
            "Unity.VisualScripting.Flow",//contains all the if, for, while, etc nodes
            "Unity.VisualScripting.State",//state graph nodes (enter, exit)

            "UnityEngine.UI",
            "UnityEngine.UIModule",
            "UnityEngine.UIElementsModule",
            "UnityEngine.UIElements",
            //"UnityEngine.IMGUIModule",

            "Unity.Timeline",
            "UnityEngine.DirectorModule",
            "Cinemachine",

            // Banter
            "Banter.SDK",
            "Banter.VisualScripting",
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

            // UI
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.CanvasScaler.ScaleMode),
            typeof(UnityEngine.UI.CanvasScaler.ScreenMatchMode),
            typeof(UnityEngine.UI.CanvasScaler.Unit),
            typeof(UnityEngine.UI.Button),
            typeof(UnityEngine.UI.Button.ButtonClickedEvent),
            typeof(UnityEngine.UI.Dropdown),
            typeof(UnityEngine.UI.Dropdown.OptionData),
            typeof(UnityEngine.UI.Dropdown.OptionDataList),
            typeof(UnityEngine.UI.Dropdown.DropdownEvent),
            typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.InputField),
            typeof(UnityEngine.UI.InputField.SubmitEvent),
            typeof(UnityEngine.UI.InputField.LineType),
            typeof(UnityEngine.UI.InputField.CharacterValidation),
            typeof(UnityEngine.UI.InputField.InputType),
            typeof(UnityEngine.UI.InputField.ContentType),
            typeof(UnityEngine.UI.Mask),
            typeof(UnityEngine.UI.MaskableGraphic),
            typeof(UnityEngine.UI.RawImage),
            typeof(UnityEngine.UI.Scrollbar),
            typeof(UnityEngine.UI.Scrollbar.ScrollEvent),
            typeof(UnityEngine.UI.ScrollRect),
            typeof(UnityEngine.UI.ScrollRect.ScrollRectEvent),
            typeof(UnityEngine.UI.Selectable),
            typeof(UnityEngine.UI.Slider),
            typeof(UnityEngine.UI.Slider.SliderEvent),
            typeof(UnityEngine.UI.Toggle),
            typeof(UnityEngine.UI.Toggle.ToggleEvent),
            typeof(UnityEngine.UI.ToggleGroup),
            typeof(UnityEngine.UI.VerticalLayoutGroup),
            typeof(UnityEngine.UI.HorizontalLayoutGroup),
            typeof(UnityEngine.UI.GridLayoutGroup),

            // UIElements
            typeof(UnityEngine.UIElements.VisualElement),
            typeof(UnityEngine.UIElements.VisualElementExtensions),
            typeof(UnityEngine.UIElements.UQuery),
            typeof(UnityEngine.UIElements.UQueryExtensions),
            typeof(UnityEngine.UIElements.Label),
            typeof(UnityEngine.UIElements.Button),
            typeof(UnityEngine.UIElements.Slider),
            typeof(UnityEngine.UIElements.RadioButton),
            typeof(UnityEngine.UIElements.RadioButtonGroup),
            typeof(UnityEngine.UIElements.Toggle),
            typeof(UnityEngine.UIElements.TextField),
            typeof(UnityEngine.UIElements.Image),
            typeof(UnityEngine.UIElements.Scroller),
            typeof(UnityEngine.UIElements.ScrollView),
            typeof(UnityEngine.UIElements.ListView),

            // Physics
            typeof(Joint),
            typeof(JointLimits),
            typeof(JointMotor),
            typeof(JointSpring),
            typeof(JointDrive),
            typeof(SoftJointLimit),
            typeof(SoftJointLimitSpring),
            typeof(ConfigurableJoint),
            typeof(ConfigurableJointMotion),
            typeof(FixedJoint),
            typeof(HingeJoint),
            typeof(SpringJoint),
            typeof(CharacterJoint),
            typeof(Collision),

            // Playables
            typeof(UnityEngine.Playables.Playable),
            typeof(UnityEngine.Playables.PlayableDirector),
            typeof(UnityEngine.Playables.PlayableAsset),
            typeof(UnityEngine.Playables.PlayableBinding),
            typeof(UnityEngine.Playables.PlayableGraph),
            typeof(UnityEngine.Playables.PlayableOutput),
            typeof(UnityEngine.Playables.PlayableExtensions),
            typeof(UnityEngine.Playables.PlayState),
            typeof(UnityEngine.Playables.DirectorWrapMode),
            typeof(UnityEngine.Playables.DirectorUpdateMode),
            typeof(UnityEngine.Playables.FrameData),
            typeof(UnityEngine.Playables.AnimationPlayableUtilities),
            typeof(UnityEngine.Playables.ScriptPlayableOutput),
#if BANTER_VS_TIMELINE
            typeof(UnityEngine.Timeline.TimelineAsset),
            typeof(UnityEngine.Timeline.TimelineAsset.DurationMode),
            typeof(UnityEngine.Timeline.TimelinePlayable),
            typeof(UnityEngine.Timeline.TimelineClip),
            typeof(UnityEngine.Timeline.TimelineClipExtensions),
            typeof(UnityEngine.Timeline.TrackAsset),
            typeof(UnityEngine.Timeline.TrackAssetExtensions),
            typeof(UnityEngine.Timeline.ActivationTrack),
            typeof(UnityEngine.Timeline.AnimationTrack),
            typeof(UnityEngine.Timeline.AudioTrack),
            typeof(UnityEngine.Timeline.ControlTrack),
            typeof(UnityEngine.Timeline.GroupTrack),
            typeof(UnityEngine.Timeline.MarkerTrack),
            typeof(UnityEngine.Timeline.SignalTrack),
            typeof(UnityEngine.Timeline.SignalReceiver),
            typeof(UnityEngine.Timeline.SignalAsset),
            typeof(UnityEngine.Timeline.SignalEmitter),
#endif

            typeof(AudioMixerGroup),
            typeof(AnimatorStateInfo),
            typeof(Keyframe),
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
            typeof(UnityEngine.AI.NavMesh),
            typeof(UnityEngine.AI.NavMeshAgent),
            typeof(UnityEngine.AI.NavMeshBuilder),
            typeof(UnityEngine.AI.NavMeshData),
            typeof(UnityEngine.AI.NavMeshObstacle),
            typeof(UnityEngine.AI.NavMeshPath),
            typeof(UnityEngine.AI.OffMeshLink),
            //AI Structs
            typeof(UnityEngine.AI.NavMeshHit),
            typeof(UnityEngine.AI.NavMeshLinkData),
            typeof(UnityEngine.AI.NavMeshLinkInstance),
            typeof(UnityEngine.AI.NavMeshQueryFilter),
            typeof(UnityEngine.AI.NavMeshTriangulation),
            //AI Enums
            typeof(UnityEngine.AI.NavMeshObstacleShape),
            typeof(UnityEngine.AI.NavMeshPathStatus),
            typeof(UnityEngine.AI.ObstacleAvoidanceType),
            typeof(UnityEngine.AI.OffMeshLinkType),

            // Banter
            typeof(Banter.SDK.BanterUser),
            typeof(Banter.SDK.BanterAttachment),
            typeof(Banter.SDK.BanterAttachedObject),
        };
    }
}
#endif
