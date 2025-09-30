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
                case ComponentType.BanterAvatarPedestal:
                    return gameObject.AddComponent<BanterAvatarPedestal>();
                case ComponentType.BanterBillboard:
                    return gameObject.AddComponent<BanterBillboard>();
                case ComponentType.BanterBox:
                    return gameObject.AddComponent<BanterBox>();
                case ComponentType.BoxCollider:
                    return gameObject.AddComponent<BanterBoxCollider>();
                case ComponentType.BanterBrowser:
                    return gameObject.AddComponent<BanterBrowser>();
                case ComponentType.CapsuleCollider:
                    return gameObject.AddComponent<BanterCapsuleCollider>();
                case ComponentType.CharacterJoint:
                    return gameObject.AddComponent<BanterCharacterJoint>();
                case ComponentType.BanterCircle:
                    return gameObject.AddComponent<BanterCircle>();
                case ComponentType.BanterColliderEvents:
                    return gameObject.AddComponent<BanterColliderEvents>();
                case ComponentType.BanterCone:
                    return gameObject.AddComponent<BanterCone>();
                case ComponentType.ConfigurableJoint:
                    return gameObject.AddComponent<BanterConfigurableJoint>();
                case ComponentType.BanterCylinder:
                    return gameObject.AddComponent<BanterCylinder>();
                case ComponentType.FixedJoint:
                    return gameObject.AddComponent<BanterFixedJoint>();
                case ComponentType.BanterGeometry:
                    return gameObject.AddComponent<BanterGeometry>();
                case ComponentType.BanterGLTF:
                    return gameObject.AddComponent<BanterGLTF>();
                case ComponentType.BanterGrabHandle:
                    return gameObject.AddComponent<BanterGrabHandle>();
                case ComponentType.BanterHeldEvents:
                    return gameObject.AddComponent<BanterHeldEvents>();
                case ComponentType.HingeJoint:
                    return gameObject.AddComponent<BanterHingeJoint>();
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
                case ComponentType.BanterPlane:
                    return gameObject.AddComponent<BanterPlane>();
                case ComponentType.BanterPortal:
                    return gameObject.AddComponent<BanterPortal>();
                case ComponentType.BanterRigidbody:
                    return gameObject.AddComponent<BanterRigidbody>();
                case ComponentType.BanterRing:
                    return gameObject.AddComponent<BanterRing>();
                case ComponentType.BanterSphere:
                    return gameObject.AddComponent<BanterSphere>();
                case ComponentType.SphereCollider:
                    return gameObject.AddComponent<BanterSphereCollider>();
                case ComponentType.SpringJoint:
                    return gameObject.AddComponent<BanterSpringJoint>();
                case ComponentType.BanterStreetView:
                    return gameObject.AddComponent<BanterStreetView>();
                case ComponentType.BanterSyncedObject:
                    return gameObject.AddComponent<BanterSyncedObject>();
                case ComponentType.BanterText:
                    return gameObject.AddComponent<BanterText>();
                case ComponentType.BanterTorus:
                    return gameObject.AddComponent<BanterTorus>();
                case ComponentType.Transform:
                    return gameObject.AddComponent<BanterTransform>();
                case ComponentType.BanterUIPanel:
                    return gameObject.AddComponent<BanterUIPanel>();
                case ComponentType.BanterVideoPlayer:
                    return gameObject.AddComponent<BanterVideoPlayer>();
                case ComponentType.BanterWorldObject:
                    return gameObject.AddComponent<BanterWorldObject>();
                default:
                    return null;
            }
        }
    }
}
