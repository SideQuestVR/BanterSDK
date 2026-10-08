using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using Unity.VisualScripting;

namespace BS
{
    [RenamedFrom("Banter.SDK.BanterSceneSettings")]
    public class BSSceneSettings
    {
        // TODO(not wired up in the app yet): whether players may open the page's developer tools from inside the
        // app (Banter's in-space debugger). Nothing in the client reads it; SDK Play Mode has its own dev tools button.
        private bool _EnableDevTools = true;
        public bool EnableDevTools { get { return _EnableDevTools; } set { _EnableDevTools = value; scene.events.OnEnableDevToolsChanged.Invoke(value); } }
        private bool _EnableTeleport = true;
        public bool EnableTeleport { get { return _EnableTeleport; } set { _EnableTeleport = value; scene.events.OnEnableTeleportChanged.Invoke(value); } }
        private bool _EnableForceGrab = false;
        public bool EnableForceGrab { get { return _EnableForceGrab; } set { _EnableForceGrab = value; scene.events.OnEnableForceGrabChanged.Invoke(value); } }
        private bool _EnableSpiderMan = false;
        public bool EnableSpiderMan { get { return _EnableSpiderMan; } set { _EnableSpiderMan = value; scene.events.OnEnableSpiderManChanged.Invoke(value); } }
        // TODO(not wired up in the app yet): let players grab and hold other players' hands (remote players' hands get
        // grabbable colliders, also gated by the player's own Hand Holding comfort option). Remote players have no hand
        // colliders in the client yet.
        private bool _EnableHandHold = true;
        public bool EnableHandHold { get { return _EnableHandHold; } set { _EnableHandHold = value; scene.events.OnEnableHandHoldChanged.Invoke(value); } }
        // TODO(not wired up in the app yet): show a radar marker over other players so they can be found through walls
        // (with the player's own Radar option). The client has no radar marker.
        private bool _EnableRadar = true;
        public bool EnableRadar { get { return _EnableRadar; } set { _EnableRadar = value; scene.events.OnEnableRadarChanged.Invoke(value); } }
        private bool _EnableNametags = true;
        public bool EnableNametags { get { return _EnableNametags; } set { _EnableNametags = value; scene.events.OnEnableNametagsChanged.Invoke(value); } }
        // TODO(not wired up in the app yet): let players drop portals to other spaces from the menu (Banter's Drop Portal
        // button). The client's menu has no drop-portal action; portals placed in the scene work regardless.
        private bool _EnablePortals = true;
        public bool EnablePortals { get { return _EnablePortals; } set { _EnablePortals = value; scene.events.OnEnablePortalsChanged.Invoke(value); } }
        private bool _EnableGuests = true;
        public bool EnableGuests { get { return _EnableGuests; } set { _EnableGuests = value; scene.events.OnEnableGuestsChanged.Invoke(value); } }
        private bool _EnableAvatars = true;
        public bool EnableAvatars { get { return _EnableAvatars; } set { _EnableAvatars = value; scene.events.OnEnableAvatarsChanged.Invoke(value); } }
        // Not acted on, by design: how many players share an instance is decided by the server when they join (the
        // world's Capacity on altvr.app, else the page's sq-maxoccupancy meta tag, else 20), before the page runs.
        private int _MaxOccupancy = 20;
        public int MaxOccupancy { get { return _MaxOccupancy; } set { _MaxOccupancy = value; scene.events.OnMaxOccupancyChanged.Invoke(value); } }
        private float _RefreshRate = 72.0f;
        public float RefreshRate { get { return _RefreshRate; } set { _RefreshRate = value; scene.events.OnRefreshRateChanged.Invoke(value); } }
        private Vector2 _ClippingPlane = new Vector2(0.02f, 1500.0f);
        public Vector2 ClippingPlane { get { return _ClippingPlane; } set { _ClippingPlane = value; scene.events.OnClippingPlaneChanged.Invoke(value); } }
        private Vector4 _SpawnPoint = Vector4.zero;
        public Vector4 SpawnPoint { get { return _SpawnPoint; } set { _SpawnPoint = value; scene.events.OnSpawnPointChanged.Invoke(value); } }

        /// <summary>
        /// The physics settings every space starts from. They are the Greenfield client's own FlexaBody tuning, so a
        /// space that never sets them (or a SetSettings that resends them untouched) changes nothing. scene.ts
        /// SceneSettings and the BS Settings component repeat these values and must stay equal; Greenfield's
        /// ScenePhysicsDefaultsTests checks all three against the FlexaBody assets.
        /// </summary>
        public static class Defaults
        {
            public const float PhysicsMoveSpeed = 3.25f;
            public const float PhysicsMoveAcceleration = 4.6f;
            public const float PhysicsAirControlSpeed = 2.4f;
            public const float PhysicsAirControlAcceleration = 6f;
            public const float PhysicsDrag = 0f;
            public const float PhysicsFreeFallAngularDrag = 6f;
            public const float PhysicsJumpStrength = 1f;
            public const float PhysicsHandPositionStrength = 1f;
            public const float PhysicsHandRotationStrength = 1f;
            public const float PhysicsHandSpringiness = 10f;
            public const float PhysicsGrappleRange = 512f;
            public const float PhysicsGrappleReelSpeed = 1f;
            public const float PhysicsGrappleSpringiness = 10f;
        }

        // Physics settings
        private float _physicsMoveSpeed = Defaults.PhysicsMoveSpeed;
        public float PhysicsMoveSpeed { get { return _physicsMoveSpeed; } set { _physicsMoveSpeed = value; scene.events.OnPhysicsMoveSpeedChanged.Invoke(value); } }
        private float _physicsMoveAcceleration = Defaults.PhysicsMoveAcceleration;
        public float PhysicsMoveAcceleration { get { return _physicsMoveAcceleration; } set { _physicsMoveAcceleration = value; scene.events.OnPhysicsMoveAccelerationChanged.Invoke(value); } }
        private float _physicsAirControlSpeed = Defaults.PhysicsAirControlSpeed;
        public float PhysicsAirControlSpeed { get { return _physicsAirControlSpeed; } set { _physicsAirControlSpeed = value; scene.events.OnPhysicsAirControlSpeedChanged.Invoke(value); } }
        private float _physicsAirControlAcceleration = Defaults.PhysicsAirControlAcceleration;
        public float PhysicsAirControlAcceleration { get { return _physicsAirControlAcceleration; } set { _physicsAirControlAcceleration = value; scene.events.OnPhysicsAirControlAccelerationChanged.Invoke(value); } }
        private float _physicsDrag = Defaults.PhysicsDrag;
        public float PhysicsDrag { get { return _physicsDrag; } set { _physicsDrag = value; scene.events.OnPhysicsDragChanged.Invoke(value); } }
        // TODO(not wired up in the app yet): angular drag while the player's body tumbles freely (zero gravity, ragdoll
        // falls). The client applies it to the torso (ScenePhysicsApplier), but the torso's rotation is always frozen,
        // so it has no effect until free rotation is used.
        private float _physicsFreeFallAngularDrag = Defaults.PhysicsFreeFallAngularDrag;
        public float PhysicsFreeFallAngularDrag { get { return _physicsFreeFallAngularDrag; } set { _physicsFreeFallAngularDrag = value; scene.events.OnPhysicsFreeFallAngularDragChanged.Invoke(value); } }
        private float _physicsJumpStrength = Defaults.PhysicsJumpStrength;
        public float PhysicsJumpStrength { get { return _physicsJumpStrength; } set { _physicsJumpStrength = value; scene.events.OnPhysicsJumpStrengthChanged.Invoke(value); } }
        private float _physicsHandPositionStrength = Defaults.PhysicsHandPositionStrength;
        public float PhysicsHandPositionStrength { get { return _physicsHandPositionStrength; } set { _physicsHandPositionStrength = value; scene.events.OnPhysicsHandPositionStrengthChanged.Invoke(value); } }
        private float _physicsHandRotationStrength = Defaults.PhysicsHandRotationStrength;
        public float PhysicsHandRotationStrength { get { return _physicsHandRotationStrength; } set { _physicsHandRotationStrength = value; scene.events.OnPhysicsHandRotationStrengthChanged.Invoke(value); } }
        private float _physicsHandSpringiness = Defaults.PhysicsHandSpringiness;
        public float PhysicsHandSpringiness { get { return _physicsHandSpringiness; } set { _physicsHandSpringiness = value; scene.events.OnPhysicsHandSpringinessChanged.Invoke(value); } }
        private float _physicsGrappleRange = Defaults.PhysicsGrappleRange;
        public float PhysicsGrappleRange { get { return _physicsGrappleRange; } set { _physicsGrappleRange = value; scene.events.OnPhysicsGrappleRangeChanged.Invoke(value); } }
        private float _physicsGrappleReelSpeed = Defaults.PhysicsGrappleReelSpeed;
        public float PhysicsGrappleReelSpeed { get { return _physicsGrappleReelSpeed; } set { _physicsGrappleReelSpeed = value; scene.events.OnPhysicsGrappleReelSpeedChanged.Invoke(value); } }
        private float _physicsGrappleSpringiness = Defaults.PhysicsGrappleSpringiness;
        public float PhysicsGrappleSpringiness { get { return _physicsGrappleSpringiness; } set { _physicsGrappleSpringiness = value; scene.events.OnPhysicsGrappleSpringinessChanged.Invoke(value); } }
        private bool _physicsGorillaMode = false;
        public bool PhysicsGorillaMode { get { return _physicsGorillaMode; } set { _physicsGorillaMode = value; scene.events.OnPhysicsGorillaModeChanged.Invoke(value); } }


        public bool IsSettingsLocked = false;
        public bool IsPhysicsSettingsLocked = false;

        public Transform LeftHand = null;
        public Transform RightHand = null;
        public Transform Head = null;
        public Transform Body = null;
        public Transform Cockpit = null;
        public Transform parentTransform = null;
        public AudioMixerGroup SpaceAudioGroup;

        // Optional per-source override for routing a loaded space's AudioSources. The app sets this
        // (see Greenfield's AudioMixerApplier) to apply its own policy — e.g. music vs. gameplay
        // buses — while the SDK stays app-agnostic. When null, or when it returns null for a given
        // source, BSAssetBundle falls back to SpaceAudioGroup. Set on each space load, since these
        // settings are rebuilt per load.
        public System.Func<UnityEngine.AudioSource, AudioMixerGroup> AudioGroupSelector;
        public BSAssetBundle SceneAssetBundle;
        public List<BSAssetBundle> KitBundles = new List<BSAssetBundle>();
        public Dictionary<string, BSAssetBundle> KitPaths = new Dictionary<string, BSAssetBundle>();
        // public Dictionary<string, string> CachedFiles = new Dictionary<string, string>();
        public bool isDestroying { get; private set; } = false;
        public DateTime? destroyedAt { get; private set; } = null;
        private BSScene scene;

        public BSSceneSettings(string instanceId)
        {
            this.instanceId = instanceId;
            var newParent = new GameObject(instanceId);
            newParent.AddComponent<DontDestroyOnLoad>();
            parentTransform = newParent.transform;
            scene = BSScene.Instance();
            LogLine.Do(LogLine.banterColor, LogTag.Banter, "Creating instance: " + instanceId);
        }
        public string instanceId { get; private set; }
        public void Destroy()
        {
            LogLine.Do(LogLine.banterColor, LogTag.Banter, "Destroying instance: " + instanceId);
            if (parentTransform == null)
            {
                return;
            }
            parentTransform.gameObject.name = "[Destroying] " + parentTransform.gameObject.name;
            GameObject.Destroy(parentTransform.gameObject);
            if (LeftHand != null)
            {
                GameObject.Destroy(LeftHand.gameObject);
            }
            if (RightHand != null)
            {
                GameObject.Destroy(RightHand.gameObject);
            }
            if (Head != null)
            {
                GameObject.Destroy(Head.gameObject);
            }
            if (Body != null)
            {
                GameObject.Destroy(Body.gameObject);
            }
            if (Cockpit != null)
            {
                GameObject.Destroy(Cockpit.gameObject);
            }

        }
        public async Task Reset()
        {
            isDestroying = true;
            destroyedAt = DateTime.Now;

            IsSettingsLocked = false;
            IsPhysicsSettingsLocked = false;

            EnableDevTools = true;
            EnableTeleport = true;
            EnableForceGrab = false;
            EnableSpiderMan = false;
            EnableHandHold = true;
            EnableRadar = true;
            EnableNametags = true;
            EnablePortals = true;
            EnableGuests = true;
            EnableAvatars = true;
            MaxOccupancy = 20;
            RefreshRate = 72.0f;
            ClippingPlane = new Vector2(0.02f, 1500.0f);
            SpawnPoint = Vector4.zero;

            PhysicsMoveSpeed = Defaults.PhysicsMoveSpeed;
            PhysicsMoveAcceleration = Defaults.PhysicsMoveAcceleration;
            PhysicsAirControlSpeed = Defaults.PhysicsAirControlSpeed;
            PhysicsAirControlAcceleration = Defaults.PhysicsAirControlAcceleration;
            PhysicsDrag = Defaults.PhysicsDrag;
            PhysicsFreeFallAngularDrag = Defaults.PhysicsFreeFallAngularDrag;
            PhysicsJumpStrength = Defaults.PhysicsJumpStrength;
            PhysicsHandPositionStrength = Defaults.PhysicsHandPositionStrength;
            PhysicsHandRotationStrength = Defaults.PhysicsHandRotationStrength;
            PhysicsHandSpringiness = Defaults.PhysicsHandSpringiness;
            PhysicsGrappleRange = Defaults.PhysicsGrappleRange;
            PhysicsGrappleReelSpeed = Defaults.PhysicsGrappleReelSpeed;
            PhysicsGrappleSpringiness = Defaults.PhysicsGrappleSpringiness;
            PhysicsGorillaMode = false;

            if (SceneAssetBundle != null)
            {
                await SceneAssetBundle.Unload();
                SceneAssetBundle = null;
            }
            foreach (var bundle in KitBundles.ToArray())
            {
                await bundle.Unload();
            }
            KitBundles.Clear();
            KitPaths.Clear();

            isDestroying = false;
        }
        // public void Destroy()
        // {
        // isDestroying = true;
        // destroyedAt = DateTime.Now;
        // LogLine.Do(LogLine.banterColor, LogTag.Banter, "Destroying instance: " + instanceId);
        // if(parentTransform == null) {
        //     return;
        // }
        // parentTransform.gameObject.name = "[Destroying] " + parentTransform.gameObject.name;
        // GameObject.Destroy(parentTransform.gameObject);
        // if(LeftHand != null) {
        //     GameObject.Destroy(LeftHand);
        // }
        // if(RightHand != null) {
        //     GameObject.Destroy(RightHand);
        // }
        // if(Head != null) {
        //     GameObject.Destroy(Head);
        // }
        // if(Body != null) {
        //     GameObject.Destroy(Body);
        // }
        // if(Cockpit != null) {
        //     GameObject.Destroy(Cockpit);
        // }
        // KitPaths.Clear();
        // allMats.Clear();
        // allShaders.Clear();

        // if (createdShaderMaterials.Count > 0)
        // {
        //     try
        //     {
        //         foreach (var mat in createdShaderMaterials)
        //         {
        //             try
        //             {
        //                 GameObject.Destroy(mat.Value);
        //             }
        //             catch (Exception ex)
        //             {
        //                 Debug.LogError("Error destroying created material, may leak!");
        //                 Debug.LogException(ex);
        //             }
        //         }
        //         createdShaderMaterials.Clear();
        //     } catch (Exception ex)
        //     {
        //         Debug.LogError("Error cleaning up created materials!");
        //         Debug.LogException(ex);
        //     }
        // }

        // }

        // public Dictionary<string, Material> allMats { get; } = new Dictionary<string, Material>();
        // public Dictionary<string, Shader> allShaders { get; } = new Dictionary<string, Shader>();

        //this is keyed off of a string which is a combination of the shader name, and (if there was a source material name) a plus, and the source material name
        //this is in case there are multiple source materials using the same shader, we want to keep an instance of each combination
        // public Dictionary<string, Material> createdShaderMaterials { get; } = new Dictionary<string, Material>();
    }
}
