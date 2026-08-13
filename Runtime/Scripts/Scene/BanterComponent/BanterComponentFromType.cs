using UnityEngine;
namespace Banter.SDK
{
    public class BanterComponentFromType
    {
        public static BanterComponentBase CreateComponent(GameObject gameObject, ComponentType componentType)
        {
            switch (componentType)
            {
                case ComponentType.BSAOBaking:
                    return gameObject.AddComponent<BSAOBaking>();
                case ComponentType.BSApple:
                    return gameObject.AddComponent<BSApple>();
                case ComponentType.BSAssetBundle:
                    return gameObject.AddComponent<BSAssetBundle>();
                case ComponentType.BSAttachedObject:
                    return gameObject.AddComponent<BSAttachedObject>();
                case ComponentType.BSAudioSource:
                    return gameObject.AddComponent<BSAudioSource>();
                // BanterAvatarPedestal disabled for Greenfield Basis migration (component tilde'd out).
                // Enum member kept for ComponentType ordinal/protocol stability.
                // case ComponentType.BanterAvatarPedestal:
                //     return gameObject.AddComponent<BanterAvatarPedestal>();
                case ComponentType.BSBillboard:
                    return gameObject.AddComponent<BSBillboard>();
                case ComponentType.BSBox:
                    return gameObject.AddComponent<BSBox>();
                case ComponentType.BoxCollider:
                    return gameObject.AddComponent<BSBoxCollider>();
                case ComponentType.BSBrowser:
                    return gameObject.AddComponent<BSBrowser>();
                case ComponentType.CapsuleCollider:
                    return gameObject.AddComponent<BSCapsuleCollider>();
                case ComponentType.BSCatenoid:
                    return gameObject.AddComponent<BSCatenoid>();
                case ComponentType.CharacterJoint:
                    return gameObject.AddComponent<BSCharacterJoint>();
                case ComponentType.BSCircle:
                    return gameObject.AddComponent<BSCircle>();
                case ComponentType.BSColliderEvents:
                    return gameObject.AddComponent<BSColliderEvents>();
                case ComponentType.BSCone:
                    return gameObject.AddComponent<BSCone>();
                case ComponentType.ConfigurableJoint:
                    return gameObject.AddComponent<BSConfigurableJoint>();
                case ComponentType.BSCylinder:
                    return gameObject.AddComponent<BSCylinder>();
                case ComponentType.BSFermet:
                    return gameObject.AddComponent<BSFermet>();
                case ComponentType.FixedJoint:
                    return gameObject.AddComponent<BSFixedJoint>();
                case ComponentType.BSGeometry:
                    return gameObject.AddComponent<BSGeometry>();
                case ComponentType.BSGLTF:
                    return gameObject.AddComponent<BSGLTF>();
                case ComponentType.BSGrababble:
                    return gameObject.AddComponent<BSGrababble>();
                case ComponentType.BSGrabHandle:
                    return gameObject.AddComponent<BSGrabHandle>();
                case ComponentType.BSHeldEvents:
                    return gameObject.AddComponent<BSHeldEvents>();
                case ComponentType.BSHelicoid:
                    return gameObject.AddComponent<BSHelicoid>();
                case ComponentType.HingeJoint:
                    return gameObject.AddComponent<BSHingeJoint>();
                case ComponentType.BSHorn:
                    return gameObject.AddComponent<BSHorn>();
                case ComponentType.BSInvertedMesh:
                    return gameObject.AddComponent<BSInvertedMesh>();
                case ComponentType.BSKitItem:
                    return gameObject.AddComponent<BSKitItem>();
                case ComponentType.BSKlein:
                    return gameObject.AddComponent<BSKlein>();
                case ComponentType.Light:
                    return gameObject.AddComponent<BSLight>();
                case ComponentType.BSMaterial:
                    return gameObject.AddComponent<BSMaterial>();
                case ComponentType.MeshCollider:
                    return gameObject.AddComponent<BSMeshCollider>();
                case ComponentType.BSMirror:
                    return gameObject.AddComponent<BSMirror>();
                case ComponentType.BSMobius:
                    return gameObject.AddComponent<BSMobius>();
                case ComponentType.BSMobius3d:
                    return gameObject.AddComponent<BSMobius3d>();
                case ComponentType.BSMonoBehaviour:
                    return gameObject.AddComponent<BSMonoBehaviour>();
                case ComponentType.BSNatica:
                    return gameObject.AddComponent<BSNatica>();
                case ComponentType.BSPhysicMaterial:
                    return gameObject.AddComponent<BSPhysicMaterial>();
                case ComponentType.BSPhysicsMaterial:
                    return gameObject.AddComponent<BSPhysicsMaterial>();
                case ComponentType.BSPillow:
                    return gameObject.AddComponent<BSPillow>();
                case ComponentType.BSPlane:
                    return gameObject.AddComponent<BSPlane>();
                case ComponentType.BSPortal:
                    return gameObject.AddComponent<BSPortal>();
                case ComponentType.BSQuestHome:
                    return gameObject.AddComponent<BSQuestHome>();
                case ComponentType.BSRigidbody:
                    return gameObject.AddComponent<BSRigidbody>();
                case ComponentType.BSRing:
                    return gameObject.AddComponent<BSRing>();
                case ComponentType.BSScherk:
                    return gameObject.AddComponent<BSScherk>();
                case ComponentType.BSSkinnedMeshRenderer:
                    return gameObject.AddComponent<BSSkinnedMeshRenderer>();
                case ComponentType.BSSnail:
                    return gameObject.AddComponent<BSSnail>();
                case ComponentType.BSSphere:
                    return gameObject.AddComponent<BSSphere>();
                case ComponentType.SphereCollider:
                    return gameObject.AddComponent<BSSphereCollider>();
                case ComponentType.BSSpiral:
                    return gameObject.AddComponent<BSSpiral>();
                case ComponentType.BSSpring:
                    return gameObject.AddComponent<BSSpring>();
                case ComponentType.SpringJoint:
                    return gameObject.AddComponent<BSSpringJoint>();
                case ComponentType.BSStreetView:
                    return gameObject.AddComponent<BSStreetView>();
                case ComponentType.BSSyncedObject:
                    return gameObject.AddComponent<BSSyncedObject>();
                case ComponentType.BSText:
                    return gameObject.AddComponent<BSText>();
                case ComponentType.BSTorus:
                    return gameObject.AddComponent<BSTorus>();
                case ComponentType.BSTorusKnot:
                    return gameObject.AddComponent<BSTorusKnot>();
                case ComponentType.BSUIPanel:
                    return gameObject.AddComponent<BSUIPanel>();
                case ComponentType.BSVideoPlayer:
                    return gameObject.AddComponent<BSVideoPlayer>();
                case ComponentType.BSWorldObject:
                    return gameObject.AddComponent<BSWorldObject>();
                default:
                    return null;
            }
        }
    }
}
