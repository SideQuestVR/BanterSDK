// SDK port of FlexaBody's WorldObject, compiled when the FlexaBody package is not available.

#if !BANTER_FLEX

using UnityEngine;

namespace SideQuest.FlexaBody
{
    /// <summary> Interactable Physics Object </summary>
    [AddComponentMenu("")]
    public class WorldObject : MonoBehaviour
    {
        public Rigidbody RB;
        public Collider[] Colliders;
        [SerializeField] Transform _com;            // Center Of Mass

        bool _firstGrab = true;

        private void Start()
        {
            if (_com)
                RB.centerOfMass = _com.localPosition;
        }

        public void IgnoreCollision(Collider col, bool ignore)
        {
            for (int i = 0; i < Colliders.Length; i++)
            {
                try
                {
                    Physics.IgnoreCollision(col, Colliders[i], ignore);
                }
                catch (System.Exception e)
                {
                    Debug.Log("PhysicsHand Ignore Collision Failed: " + e);
                }
            }
        }

        public void OnGrab(HandData handData)
        {
            if (_firstGrab)
            {
                if (Colliders == null || Colliders.Length == 0)
                    Colliders = gameObject.GetComponentsInChildren<Collider>();

                _firstGrab = false;
            }
        }

        public void OnRelease(HandData handData)
        {
        }
    }
}

#endif
