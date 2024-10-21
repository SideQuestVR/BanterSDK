using UnityEngine;
namespace Banter.SDK
{
    public class BanterComponentFromType
    {
        public static BanterComponentBase CreateComponent(GameObject gameObject, ComponentType componentType)
        {
            switch (componentType)
            {
                case ComponentType.BanterAssetBundle:
                    return gameObject.AddComponent<BanterAssetBundle>();
                case ComponentType.BanterAttachedObject:
                    return gameObject.AddComponent<BanterAttachedObject>();
                case ComponentType.BanterAudioSource:
                    return gameObject.AddComponent<BanterAudioSource>();
                case ComponentType.BanterBillboard:
                    return gameObject.AddComponent<BanterBillboard>();
                case ComponentType.BoxCollider:
                    return gameObject.AddComponent<BanterBoxCollider>();
                case ComponentType.BanterBrowser:
                    return gameObject.AddComponent<BanterBrowser>();
                case ComponentType.CapsuleCollider:
                    return gameObject.AddComponent<BanterCapsuleCollider>();
                case ComponentType.BanterColliderEvents:
                    return gameObject.AddComponent<BanterColliderEvents>();
                case ComponentType.ConfigurableJoint:
                    return gameObject.AddComponent<BanterConfigurableJoint>();
                case ComponentType.BanterGeometry:
                    return gameObject.AddComponent<BanterGeometry>();
                case ComponentType.BanterGLTF:
                    return gameObject.AddComponent<BanterGLTF>();
                case ComponentType.BanterInvertedMesh:
                    return gameObject.AddComponent<BanterInvertedMesh>();
                case ComponentType.BanterKitItem:
                    return gameObject.AddComponent<BanterKitItem>();
                case ComponentType.BanterMaterial:
                    return gameObject.AddComponent<BanterMaterial>();
                case ComponentType.MeshCollider:
                    return gameObject.AddComponent<BanterMeshCollider>();
                case ComponentType.BanterMirror:
                    return gameObject.AddComponent<BanterMirror>();
                case ComponentType.BanterPhysicMaterial:
                    return gameObject.AddComponent<BanterPhysicMaterial>();
                case ComponentType.BanterPortal:
                    return gameObject.AddComponent<BanterPortal>();
                case ComponentType.BanterRigidbody:
                    return gameObject.AddComponent<BanterRigidbody>();
                case ComponentType.SphereCollider:
                    return gameObject.AddComponent<BanterSphereCollider>();
                case ComponentType.BanterStreetView:
                    return gameObject.AddComponent<BanterStreetView>();
                case ComponentType.BanterSyncedObject:
                    return gameObject.AddComponent<BanterSyncedObject>();
                case ComponentType.BanterText:
                    return gameObject.AddComponent<BanterText>();
                case ComponentType.Transform:
                    return gameObject.AddComponent<BanterTransform>();
                case ComponentType.BanterVideoPlayer:
                    return gameObject.AddComponent<BanterVideoPlayer>();
                default:
                    return null;
            }
        }
    }
}
