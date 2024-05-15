using System;
using System.Collections;
using System.Collections.Generic;
using Banter;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using static UnityEngine.InputSystem.InputAction;
namespace Banter {
    [RequireComponent(typeof(Rigidbody))]
    public class HandGrabber : MonoBehaviour
    {
        public float Radius = .1f;
        public LayerMask GrabLayer;
        public HandSide side;
        BanterScene scene;
        public bool Grabbing;
        public Transform Anchor;
        private readonly Collider[] _colliders = new Collider[100];

        public ConfigurableJoint Joint { get; private set; }
        public Rigidbody GrabbedBody { get; private set; }
        public Collider hit;

        InputAction grab;

        void Awake() {
            scene = BanterScene.Instance();
            grab = (side == HandSide.LEFT ? scene.LeftHandActions : scene.RightHandActions).FindAction("Grip");
        }

        void FixedUpdate()
        {

            var grabValue = grab.ReadValue<float>();

            if (grabValue > 0.2f && !Grabbing)
            {
                var hits = Physics.OverlapSphereNonAlloc(Anchor.position, Radius, _colliders, GrabLayer, QueryTriggerInteraction.Ignore);
                if (hits > 0)
                {
                    Joint = gameObject.AddComponent<ConfigurableJoint>();
                    Joint.xMotion = Joint.yMotion = Joint.zMotion = ConfigurableJointMotion.Locked;
                    Joint.angularXMotion = Joint.angularYMotion = Joint.angularZMotion = ConfigurableJointMotion.Locked;
                    Joint.anchor = transform.InverseTransformPoint(Anchor.position);
                    Joint.autoConfigureConnectedAnchor = false;
                    hit = _colliders[0];

                    if (!hit.attachedRigidbody)
                    {
                        for (var index = 0; index < hits; index++)
                        {
                            var col = _colliders[index];
                            if (!col)
                                break;
                            if (col.attachedRigidbody)
                            {
                                hit = col;
                                break;
                            }
                        }
                    }

                    if (hit.attachedRigidbody) {
                        Joint.connectedBody = hit.attachedRigidbody;
                        Joint.connectedAnchor = hit.attachedRigidbody.transform.InverseTransformPoint(Anchor.position);
                    } else {
                        Joint.connectedAnchor = Anchor.position;
                    }

                    GrabbedBody = hit.attachedRigidbody;
                    hit.enabled = false;
                    hit.enabled = true;
                    Grabbing = true;
                    scene.Grab(hit.gameObject, Anchor.position, side);
                }
            } else if (grabValue < 0.2f  && Grabbing) {
                Grabbing = false;
                if (Joint) {
                    Destroy(Joint);
                }
                if(hit && hit.gameObject) {
                    scene.Release(hit.gameObject, side);
                }
                hit = null;
                GrabbedBody = null;
            }
        }
    }
}
    
