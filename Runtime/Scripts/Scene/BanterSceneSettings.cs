using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Banter.SDK
{
    public class BanterSceneSettings
    {
        private bool _EnableDevTools = true;
        public bool EnableDevTools { get { return _EnableDevTools; } set { _EnableDevTools = value; scene.events.OnEnableDevToolsChanged.Invoke(value); } }
        public bool EnableDefaultTextures = true;
        private bool _EnableTeleport = true;
        public bool EnableTeleport { get { return _EnableTeleport; } set { _EnableTeleport = value; scene.events.OnEnableTeleportChanged.Invoke(value); } }
        private bool _EnableForceGrab = false;
        public bool EnableForceGrab { get { return _EnableForceGrab; } set { _EnableForceGrab = value; scene.events.OnEnableForceGrabChanged.Invoke(value); } }
        private bool _EnableSpiderMan = false;
        public bool EnableSpiderMan { get { return _EnableSpiderMan; } set { _EnableSpiderMan = value; scene.events.OnEnableSpiderManChanged.Invoke(value); } }
        private bool _EnableHandHold = true;
        public bool EnableHandHold{ get { return _EnableHandHold; } set { _EnableHandHold = value; scene.events.OnEnableHandHoldChanged.Invoke(value); } }
        private bool _EnableRadar = true;
        public bool EnableRadar { get { return _EnableRadar; } set { _EnableRadar = value; scene.events.OnEnableRadarChanged.Invoke(value); } }
        private bool _EnableNametags = true;
        public bool EnableNametags { get { return _EnableNametags; } set { _EnableNametags = value; scene.events.OnEnableNametagsChanged.Invoke(value); } }
        private bool _EnablePortals = true;
        public bool EnablePortals { get { return _EnablePortals; } set { _EnablePortals = value; scene.events.OnEnablePortalsChanged.Invoke(value); } }
        private bool _EnableGuests = true;
        public bool EnableGuests { get { return _EnableGuests; } set { _EnableGuests = value; scene.events.OnEnableGuestsChanged.Invoke(value); } }
        private bool _EnableFriendPositionJoin = true;
        public bool EnableFriendPositionJoin { get { return _EnableFriendPositionJoin; } set { _EnableFriendPositionJoin = value; scene.events.OnEnableFriendPositionJoinChanged.Invoke(value); } }
        private bool _EnableAvatars = true;
        public bool EnableAvatars { get { return _EnableAvatars; } set { _EnableAvatars = value; scene.events.OnEnableAvatarsChanged.Invoke(value); } }
        private int _MaxOccupancy = 20;
        public int MaxOccupancy { get { return _MaxOccupancy; } set { _MaxOccupancy = value; scene.events.OnMaxOccupancyChanged.Invoke(value); } }
        private float _RefreshRate = 72.0f;
        public float RefreshRate { get { return _RefreshRate; } set { _RefreshRate = value; scene.events.OnRefreshRateChanged.Invoke(value); } }
        private Vector2 _ClippingPlane = new Vector2(0.02f, 1500.0f);
        public Vector2 ClippingPlane { get { return _ClippingPlane; } set { _ClippingPlane = value; scene.events.OnClippingPlaneChanged.Invoke(value); } }
        private Vector4 _SpawnPoint = Vector4.zero;
        public Vector4 SpawnPoint { get { return _SpawnPoint; } set { _SpawnPoint = value; scene.events.OnSpawnPointChanged.Invoke(value); } }
        
        // Physics settings
        private float _physicsMoveSpeed = 4f;
        public float PhysicsMoveSpeed { get { return _physicsMoveSpeed; } set { _physicsMoveSpeed = value; scene.events.OnPhysicsMoveSpeedChanged.Invoke(value); } }
        private float _physicsMoveAcceleration = 4.6f;
        public float PhysicsMoveAcceleration { get { return _physicsMoveAcceleration; } set { _physicsMoveAcceleration = value; scene.events.OnPhysicsMoveAccelerationChanged.Invoke(value); } }
        private float _physicsAirControlSpeed = 3.8f;
        public float PhysicsAirControlSpeed { get { return _physicsAirControlSpeed; } set { _physicsAirControlSpeed = value; scene.events.OnPhysicsAirControlSpeedChanged.Invoke(value); } }
        private float _physicsAirControlAcceleration = 6f;
        public float PhysicsAirControlAcceleration { get { return _physicsAirControlAcceleration; } set { _physicsAirControlAcceleration = value; scene.events.OnPhysicsAirControlAccelerationChanged.Invoke(value); } }
        private float _physicsDrag = 0f;
        public float PhysicsDrag { get { return _physicsDrag; } set { _physicsDrag = value; scene.events.OnPhysicsDragChanged.Invoke(value); } } 
        private float _physicsFreeFallAngularDrag = 0f;
        public float PhysicsFreeFallAngularDrag { get { return _physicsFreeFallAngularDrag; } set { _physicsFreeFallAngularDrag = value; scene.events.OnPhysicsFreeFallAngularDragChanged.Invoke(value); } } 
        private float _physicsJumpStrength = 1f;
        public float PhysicsJumpStrength { get { return _physicsJumpStrength; } set { _physicsJumpStrength = value; scene.events.OnPhysicsJumpStrengthChanged.Invoke(value); } } 
        private float _physicsHandPositionStrength = 1f;
        public float PhysicsHandPositionStrength { get { return _physicsHandPositionStrength; } set { _physicsHandPositionStrength = value; scene.events.OnPhysicsHandPositionStrengthChanged.Invoke(value); } } 
        private float _physicsHandRotationStrength = 1f;
        public float PhysicsHandRotationStrength { get { return _physicsHandRotationStrength; } set { _physicsHandRotationStrength = value; scene.events.OnPhysicsHandRotationStrengthChanged.Invoke(value); } } 
        private float _physicsHandSpringiness = 1f;
        public float PhysicsHandSpringiness { get { return _physicsHandSpringiness; } set { _physicsHandSpringiness = value; scene.events.OnPhysicsHandSpringinessChanged.Invoke(value); } } 
        private float _physicsGrappleRange = 512f;
        public float PhysicsGrappleRange { get { return _physicsGrappleRange; } set { _physicsGrappleRange = value; scene.events.OnPhysicsGrappleRangeChanged.Invoke(value); } } 
        private float _physicsGrappleReelSpeed = 1;
        public float PhysicsGrappleReelSpeed { get { return _physicsGrappleReelSpeed; } set { _physicsGrappleReelSpeed = value; scene.events.OnPhysicsGrappleReelSpeedChanged.Invoke(value); } } 
        private float _physicsGrappleStretchiness = 1;
        public float PhysicsGrappleStretchiness { get { return _physicsGrappleStretchiness; } set { _physicsGrappleStretchiness = value; scene.events.OnPhysicsGrappleStretchinessChanged.Invoke(value); } } 
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
        public BanterAssetBundle SceneAssetBundle;
        public List<BanterAssetBundle> KitBundles = new List<BanterAssetBundle>();
        public Dictionary<string, BanterAssetBundle> KitPaths = new Dictionary<string, BanterAssetBundle>();
        // public Dictionary<string, string> CachedFiles = new Dictionary<string, string>();
        public bool isDestroying { get; private set; } = false;
        public DateTime? destroyedAt { get; private set; } = null;
        private BanterScene scene;

        public BanterSceneSettings(string instanceId)
        {
            this.instanceId = instanceId;
            var newParent = new GameObject(instanceId);
            newParent.AddComponent<DontDestroyOnLoad>();
            parentTransform = newParent.transform;
            scene = BanterScene.Instance();
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
            EnableDefaultTextures = true;
            EnableTeleport = true;
            EnableForceGrab = false;
            EnableSpiderMan = false;
            EnableRadar = true;
            EnableNametags = true;
            EnablePortals = true;
            EnableGuests = true;
            EnableFriendPositionJoin = true;
            EnableAvatars = true;
            MaxOccupancy = 20;
            RefreshRate = 72.0f;
            ClippingPlane = new Vector2(0.02f, 1500.0f);
            SpawnPoint = Vector4.zero;
            
            PhysicsMoveSpeed = 4f;
            
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
