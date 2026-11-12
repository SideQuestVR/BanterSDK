using System;
using System.Collections;
using System.Collections.Generic;
using SideQuest.FlexaBody;
using UnityEngine;
using Unity.VisualScripting;
[RenamedFrom("Banter.SDK.BanterGrabType")]
public enum BSGrabType
{
    Point,
    Cylinder,
    Ball,
    Soft
}

[RenamedFrom("Banter.SDK.BanterGrabHand")]
public enum BSGrabHand
{
    Either,
    LeftOnly,
    RightOnly
}
namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSGrabHandle : BSComponentBase
    {
        GrabHandle grabHandle;

        bool grabHandleAdded;
        bool worldObjectAdded;

        [Tooltip("Defines the type of grab interaction (Point, Cylinder, Ball, Soft).")]
        [See(initial = "0")][SerializeField] internal BSGrabType grabType = BSGrabType.Point;

        [Tooltip("Radius of the grab handle, affecting how objects can be grabbed.")]
        [See(initial = "0.01")][SerializeField] internal float grabRadius = 0.01f;

        [Tooltip("Restrict which hand can grab this handle. Not networked — editor/local only.")]
        [SerializeField] internal BSGrabHand grabHand = BSGrabHand.Either;

        [Tooltip("Show the hand hint while a player points at this handle. Editor hand previews remain available.")]
        [SerializeField] internal bool showRuntimeVisual = true;

        public bool ShowRuntimeVisual { get => showRuntimeVisual; set => showRuntimeVisual = value; }

        [SerializeField] internal BSPointGrabPose customPointPose = new BSPointGrabPose();
        public BSPointGrabPose CustomPointPose => customPointPose;

        public void ApplyCustomPointPose()
        {
            if (grabHandle != null) customPointPose?.Apply(grabHandle);
        }

        void OnValidate() => ApplyCustomPointPose();

        internal override void DestroyStuff()
        {
            if (worldObjectAdded && grabHandle && grabHandle.WorldObj)
            {
                Destroy(grabHandle.WorldObj);
            }
            if (grabHandle && grabHandleAdded)
            {
                Destroy(grabHandle);
            }
        }


        internal override void UpdateStuff()
        {

        }
        internal override void StartStuff()
        {
            SetLoadedIfNot();
            // Configures FlexaBody's GrabHandle in Banter, and the SDK's port of it otherwise
            // (Utils/FlexaBody), which the desktop controller grabs with.
            grabHandle = GetComponent<GrabHandle>();
            if (grabHandle == null)
            {
                grabHandleAdded = true;
                grabHandle = gameObject.AddComponent<GrabHandle>();
            }

            grabHandle.Col = GetComponent<Collider>();
            grabHandle.GrabType = (GrabType)GrabType;
            grabHandle._grabRadius = GrabRadius;
            ApplyCustomPointPose();
            Rigidbody rb = grabHandle.Col ? grabHandle.Col.attachedRigidbody : null;
            if (rb)
            {
                grabHandle.WorldObj = rb.GetComponentInParent<WorldObject>();
                if (!grabHandle.WorldObj)
                {
                    worldObjectAdded = true;
                    grabHandle.WorldObj = rb.gameObject.AddComponent<WorldObject>();
                }
            }
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (changedProperties.Contains(PropertyName.grabType) && grabHandle)
            {
                grabHandle.GrabType = (GrabType)grabType;
            }
        }
    }
}