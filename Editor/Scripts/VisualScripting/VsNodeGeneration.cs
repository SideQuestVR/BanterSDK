// Portions of this code are from https://github.com/spatialsys/spatial-unity-sdk
// Retrieved on 2024-06-04
// SPDX-License-Identifier: MIT

using UnityEditor;
using UnityEngine;
using Unity.VisualScripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Specialized;

#if BANTER_PICAVOXEL
using PicaVoxel;
#endif

namespace BS.SDKEditor
{
    /// <summary>
    /// The Visual Scripting node library for spaces: which assemblies and types the fuzzy finder offers nodes
    /// for (the SDK's components and the Unity APIs the client runs), and building the node database from
    /// them. Run from the Welcome window's setup checklist, Altspace/Tools/Configure Visual Scripting and,
    /// in Greenfield, its own code generation. It never shows a dialog, so it also runs in batch mode.
    /// </summary>
    public static class VsNodeGeneration
    {
        /// <summary>Per user and project: the SDK version and option lists the node database was last built for.</summary>
        const string NodesStampKey = "BS.SDK.VisualScriptingNodes";

        // The SDK's own assemblies, named from their types so an asmdef rename can't drop them unnoticed. The
        // August 2026 Banter.* -> BS.* namespace move rewrote "Banter.SDK" and "Banter.VisualScripting" here to
        // "BS" and "BS.VisualScripting", which match no assembly: no BS component got any nodes after that.
        // Declared before the allow-list, whose initializer reads them.
        /// <summary>BS.SDK: the components (BSText, BSPortal...) and the types they use.</summary>
        public static readonly string SdkAssemblyName = typeof(BSText).Assembly.GetName().Name;
        /// <summary>Banter.VisualScripting: the SDK's custom nodes and their event arguments.</summary>
        public static readonly string SdkNodesAssemblyName = typeof(BS.VisualScripting.SetSpaceStateValue).Assembly.GetName().Name;

        /// <summary>
        /// Sets the project's Visual Scripting node library to the SDK's assemblies and types, then rebuilds
        /// the node database. Uses Visual Scripting's own settings API, as its Project Settings page does:
        /// the database is built from the settings in memory, so a rebuild after editing the settings file on
        /// disk would still use the old ones until the next domain reload.
        /// </summary>
        public static void SetVSTypesAndAssemblies()
        {
            // The first time, this initialises Visual Scripting, which creates its settings and builds the
            // node database with its defaults, as opening a graph would.
            if (!VSUsageUtility.isVisualScriptingUsed)
                VSUsageUtility.isVisualScriptingUsed = true;

            var configuration = BoltCore.Configuration;
            if (configuration == null)
            {
                Debug.LogError("[Creator SDK] Visual Scripting didn't initialise, so its node library wasn't set up. " +
                               "Open Edit > Project Settings > Visual Scripting once, then try again.");
                return;
            }

            // Replaced, not merged: a space can only use what the client runs.
            configuration.assemblyOptions.Clear();
            configuration.assemblyOptions.AddRange(assemblyAllowList.Select(name => (LooseAssemblyName)name));
            configuration.typeOptions.Clear();
            configuration.typeOptions.AddRange(typeAllowList.Distinct());
            configuration.GetMetadata(nameof(configuration.assemblyOptions)).Save();
            configuration.GetMetadata(nameof(configuration.typeOptions)).Save();
            WriteSettingsNow(configuration);

            Codebase.UpdateSettings();
            UnitBase.Rebuild();

            EditorUserSettings.SetConfigValue(NodesStampKey, CurrentStamp);
            Debug.Log($"[Creator SDK] Visual Scripting node library set up for SDK {PackageManagerUtility.currentVersion ?? "(unknown version)"}.");
        }

        // SaveProjectSettingsAsset only queues the write for EditorApplication.delayCall, which an editor in the
        // background doesn't reach, and a batch-mode run that quits straight after never does. Visual Scripting's
        // own (private) serializer writes it now; the queued write still happens as well, and covers a version
        // without that method.
        static void WriteSettingsNow(PluginConfiguration configuration)
        {
            configuration.SaveProjectSettingsAsset(true);
            try
            {
                typeof(PluginConfiguration)
                    .GetMethod("SerializeProjectSettingsAssetToDisk", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(configuration, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Creator SDK] Visual Scripting's settings will be saved on the editor's next update: " + e.Message);
            }
        }

        /// <summary>
        /// The SDK assemblies and types this project's Visual Scripting settings don't include. False when
        /// Visual Scripting hasn't been initialised in this project, so there are no settings to read.
        /// </summary>
        public static bool TryGetMissingOptions(out List<string> assemblies, out List<Type> types)
        {
            assemblies = new List<string>();
            types = new List<Type>();
            var configuration = VSUsageUtility.isVisualScriptingUsed ? BoltCore.Configuration : null;
            if (configuration == null)
                return false;
            var configuredAssemblies = new HashSet<string>(configuration.assemblyOptions.Select(option => option.name));
            assemblies.AddRange(assemblyAllowList.Where(name => !configuredAssemblies.Contains(name)));
            var configuredTypes = new HashSet<Type>(configuration.typeOptions.Where(type => type != null));
            types.AddRange(typeAllowList.Distinct().Where(type => !configuredTypes.Contains(type)));
            return true;
        }

        /// <summary>Whether the node database the fuzzy finder reads exists.</summary>
        public static bool NodeDatabaseExists
        {
            get
            {
                var path = VSUsageUtility.isVisualScriptingUsed ? BoltFlow.Paths?.unitOptions : null;
                return !string.IsNullOrEmpty(path) && File.Exists(path);
            }
        }

        /// <summary>Whether the node database was last built here by this SDK version, with these lists.</summary>
        public static bool NodesBuiltForThisVersion => EditorUserSettings.GetConfigValue(NodesStampKey) == CurrentStamp;

        /// <summary>The SDK version the node database was last built for here, or null.</summary>
        public static string NodesBuiltForVersion
        {
            get
            {
                var stamp = EditorUserSettings.GetConfigValue(NodesStampKey);
                return string.IsNullOrEmpty(stamp) ? null : stamp.Split('|')[0];
            }
        }

        static string CurrentStamp => (PackageManagerUtility.currentVersion ?? "unknown") + "|" + OptionsHash();

        // FNV-1a over the sorted lists: a changed list means a rebuild, even within one SDK version.
        static string OptionsHash()
        {
            var text = string.Join("\n", assemblyAllowList.OrderBy(name => name, StringComparer.Ordinal)) + "\n--\n" +
                       string.Join("\n", typeAllowList.Select(type => type.FullName).Distinct().OrderBy(name => name, StringComparer.Ordinal));
            uint hash = 2166136261;
            foreach (var c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("x8");
        }

        public static readonly HashSet<string> assemblyAllowList = new HashSet<string>() {
            "mscorlib",
            "Assembly-CSharp-firstpass",
            "Assembly-CSharp",

            "UnityEngine",
            "UnityEngine.CoreModule",
            "UnityEngine.PhysicsModule",
            "UnityEngine.Physics2DModule",
            "UnityEngine.VehiclesModule",
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
            "Cinemachine",          // Cinemachine 2.x
            "Unity.Cinemachine",    // Cinemachine 3.x (com.unity.cinemachine 3); also in AotPreBuilder._allowedNamespaces

            // The SDK: components and custom nodes
            SdkAssemblyName,
            SdkNodesAssemblyName,

            // Picavoxel
            "GarethIW.PicaVoxelInfinity",

            // Colombia
            "com.sidequest.columbia.VisualScripting"
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
            typeof(TimeSpan),
            typeof(UnityEngine.Random),

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
            
            typeof(Physics),
            typeof(Physics2D),
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

            // Particles

            typeof(ParticleSystem),
            typeof(ParticleSystem.MainModule),
            typeof(ParticleSystem.EmissionModule),
            typeof(ParticleSystem.ShapeModule),
            typeof(ParticleSystem.VelocityOverLifetimeModule),
            typeof(ParticleSystem.LimitVelocityOverLifetimeModule),
            typeof(ParticleSystem.InheritVelocityModule),
            typeof(ParticleSystem.ForceOverLifetimeModule),
            typeof(ParticleSystem.ColorOverLifetimeModule),
            typeof(ParticleSystem.ColorBySpeedModule),
            typeof(ParticleSystem.SizeOverLifetimeModule),
            typeof(ParticleSystem.SizeBySpeedModule),
            typeof(ParticleSystem.RotationOverLifetimeModule),
            typeof(ParticleSystem.RotationBySpeedModule),
            typeof(ParticleSystem.ExternalForcesModule),
            typeof(ParticleSystem.NoiseModule),
            typeof(ParticleSystem.CollisionModule),
            typeof(ParticleSystem.TriggerModule),
            typeof(ParticleSystem.SubEmittersModule),
            typeof(ParticleSystem.TextureSheetAnimationModule),
            typeof(ParticleSystem.LightsModule),
            typeof(ParticleSystem.TrailModule),
            typeof(ParticleSystem.CustomDataModule),
            typeof(ParticleSystem.MinMaxCurve),
            typeof(ParticleSystem.MinMaxGradient),
            typeof(ParticleSystemRenderer),


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
#if BANTER_VS_CINEMACHINE
            // Cinemachine 3: a world's program camera (brain + shots). Types a graph may hold in a
            // variable; their members come from the Unity.Cinemachine assembly entry above.
            typeof(Unity.Cinemachine.CinemachineBrain),
            typeof(Unity.Cinemachine.CinemachineCamera),
            typeof(Unity.Cinemachine.CinemachineVirtualCameraBase),
            typeof(Unity.Cinemachine.CinemachineSplineDolly),
            typeof(Unity.Cinemachine.CinemachineSplineCart),
            typeof(Unity.Cinemachine.CinemachineTargetGroup),
            typeof(Unity.Cinemachine.CinemachineBasicMultiChannelPerlin),
            typeof(Unity.Cinemachine.CinemachineImpulseSource),
            typeof(Unity.Cinemachine.CinemachineClearShot),
            typeof(Unity.Cinemachine.CinemachineSequencerCamera),
            typeof(Unity.Cinemachine.CinemachineMixingCamera),
            typeof(Unity.Cinemachine.CinemachineCore),
            typeof(Unity.Cinemachine.CinemachineBlendDefinition),
            typeof(Unity.Cinemachine.LensSettings),
            typeof(Unity.Cinemachine.PrioritySettings),
            typeof(Unity.Cinemachine.CameraTarget),
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

            // Banter classes that aren't MonoBehaviours
            // See AotPreBuilder._allowedBanterTypes for the MBs
            typeof(BS.BSUser),
            typeof(BS.BSAttachment),

            typeof(BS.Score),
#if BANTER_PICAVOXEL
            typeof(PicaVoxel.VoxelEditEventArgs),
            typeof(PicaVoxel.VoxelDetectorEventArgs),
            typeof(PicaVoxel.ChunkChangesEventArgs),
#endif
        };
    }
}
