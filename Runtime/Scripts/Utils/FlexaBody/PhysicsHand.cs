// SDK port of the parts of FlexaBody's PhysicsHand that grabbing uses, compiled when the FlexaBody
// package is not available. The body joint / arm drives are not ported: the SDK desktop controller
// drives its hand Rigidbody directly.

#if !BANTER_FLEX

using UnityEngine;

namespace SideQuest.FlexaBody
{
    /// <summary> The Rigidbody a GrabHand grabs with </summary>
    [AddComponentMenu("")]
    public class PhysicsHand : MonoBehaviour
    {
        public HandID HandId { get { return _handId; } }
        [SerializeField] HandID _handId;
        public Rigidbody RB { get { return _rb; } }
        [SerializeField] Rigidbody _rb;
        public Collider Col { get { return _collider; } }
        [SerializeField] Collider _collider;

        public void Setup(HandID handId, Rigidbody rb, Collider collider = null)
        {
            _handId = handId;
            _rb = rb;
            _collider = collider;
        }

        public Vector3 GetPalmDir()
        {
            if (_handId == HandID.Left)
                return _rb.Right();
            else
                return _rb.Left();
        }

        public Vector3 GetPalmDirLocal()
        {
            if (_handId == HandID.Left)
                return Vector3.right;
            else
                return Vector3.left;
        }

        // Helpers

        // Ignore GrabHandle (Checks whether to use WorldObject or not)
        public void IgnoreCollision(GrabHandle grabHandle, bool ignore = true)
        {
            if (grabHandle.WorldObj)
                IgnoreCollision(grabHandle.WorldObj, ignore);
            else
                IgnoreCollision(grabHandle.Col, ignore);
        }

        // Ignore WorldObject
        public void IgnoreCollision(WorldObject worldObject, bool ignore = true)
        {
            if (worldObject.Colliders == null)
                return;

            for (int i = 0; i < worldObject.Colliders.Length; i++)
            {
                IgnoreCollision(worldObject.Colliders[i], ignore);
            }
        }

        // Ignore Collider
        public void IgnoreCollision(Collider collider, bool ignore = true)
        {
            // A hand without a collider (the desktop mouse hand) has nothing to ignore.
            if (_collider == null || collider == null)
                return;

            try
            {
                Physics.IgnoreCollision(_collider, collider, ignore);
            }
            catch (System.Exception e)
            {
                Debug.Log("PhysicsHand Ignore Collision Failed: " + e);
            }
        }
    }
}

#endif
