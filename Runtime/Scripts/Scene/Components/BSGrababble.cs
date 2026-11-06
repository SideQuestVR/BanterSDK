
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]

    public partial class BSGrababble : BSComponentBase
    {
        [Tooltip("Defines the type of grab interaction (Point, Cylinder, Ball, Soft).")]
        [See(initial = "0")][SerializeField] internal BSGrabType grabType = BSGrabType.Point;

        [Tooltip("Radius of the grab handle, affecting how objects can be grabbed.")]
        [See(initial = "0.01")][SerializeField] internal float grabRadius = 0.01f;

        [Tooltip("Show the hand hint while a player points at this object. Editor hand previews remain available.")]
        [SerializeField] internal bool showRuntimeVisual = true;

        public bool ShowRuntimeVisual { get => showRuntimeVisual; set => showRuntimeVisual = value; }

        [SerializeField] internal BSPointGrabPose customPointPose = new BSPointGrabPose();
        public BSPointGrabPose CustomPointPose => customPointPose;

        [Tooltip("Sensitivity for detecting held events (higher values make inputs more sensitive).")]
        [See(initial = "0.5")][SerializeField] internal float gunTriggerSensitivity = 0.5f;

        [Tooltip("Rate at which held events are fired (in seconds).")]
        [See(initial = "0.1")][SerializeField] internal float gunTriggerFireRate = 0.1f;

        [Tooltip("Enable automatic triggering of held events without manual input.")]
        [See(initial = "false")][SerializeField] internal bool gunTriggerAutoFire = false;

        [Tooltip("Blocks the left controller's primary button input.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftPrimary = false;

        [Tooltip("Blocks the left controller's secondary button input.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftSecondary = false;

        [Tooltip("Blocks the right controller's primary button input.")]
        [See(initial = "false")][SerializeField] internal bool blockRightPrimary = false;

        [Tooltip("Blocks the right controller's secondary button input.")]
        [See(initial = "false")][SerializeField] internal bool blockRightSecondary = false;

        [Tooltip("Blocks the left controller's thumbstick movement.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftThumbstick = false;

        [Tooltip("Blocks the left controller's thumbstick click.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftThumbstickClick = false;

        [Tooltip("Blocks the right controller's thumbstick movement.")]
        [See(initial = "false")][SerializeField] internal bool blockRightThumbstick = false;

        [Tooltip("Blocks the right controller's thumbstick click.")]
        [See(initial = "false")][SerializeField] internal bool blockRightThumbstickClick = false;

        [Tooltip("Blocks the left controller's trigger input.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftTrigger = false;

        [Tooltip("Blocks the right controller's trigger input.")]
        [See(initial = "false")][SerializeField] internal bool blockRightTrigger = false;

        BSGrabHandle banterGrabHandle;

        bool banterGrabHandleAdded;

        BSWorldObject banterWorldObject;

        bool banterWorldObjectAdded;

        BSHeldEvents banterHeldEvents;
        bool banterHeldEventsAdded;


        internal override void UpdateStuff()
        {

        }
        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has set up the grab handle, world object and held
                // events yet (see valuesApplied). A BSGrabHandle or BSHeldEvents that was already on
                // the object was configured there directly and keeps its settings.
                var properties = new List<PropertyName>();
                if (!GetComponent<BSGrabHandle>())
                {
                    properties.Add(PropertyName.grabType);
                    properties.Add(PropertyName.grabRadius);
                }
                if (!GetComponent<BSHeldEvents>())
                {
                    properties.AddRange(new[] { PropertyName.gunTriggerSensitivity, PropertyName.gunTriggerFireRate,
                        PropertyName.gunTriggerAutoFire, PropertyName.blockLeftPrimary, PropertyName.blockLeftSecondary,
                        PropertyName.blockRightPrimary, PropertyName.blockRightSecondary, PropertyName.blockLeftThumbstick,
                        PropertyName.blockLeftThumbstickClick, PropertyName.blockRightThumbstick,
                        PropertyName.blockRightThumbstickClick, PropertyName.blockLeftTrigger, PropertyName.blockRightTrigger });
                }
                UpdateCallback(properties);
            }
            SetLoadedIfNot();
        }
        internal override void DestroyStuff()
        {
            if (banterGrabHandle && banterGrabHandleAdded)
            {
                Destroy(banterGrabHandle);
            }
            if (banterWorldObject && banterWorldObjectAdded)
            {
                Destroy(banterWorldObject);
            }
            if (banterHeldEvents && banterHeldEventsAdded)
            {
                Destroy(banterHeldEvents);
            }
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            if (!banterGrabHandle)
            {
                banterGrabHandle = gameObject.GetComponent<BSGrabHandle>();
                if (!banterGrabHandle)
                {
                    banterGrabHandleAdded = true;
                    banterGrabHandle = gameObject.AddComponent<BSGrabHandle>();
                    banterGrabHandle.customPointPose = customPointPose;
                }
            }
            if (!banterWorldObject)
            {
                banterWorldObject = gameObject.GetComponent<BSWorldObject>();
                if (!banterWorldObject)
                {
                    banterWorldObjectAdded = true;
                    banterWorldObject = gameObject.AddComponent<BSWorldObject>();
                }
            }
            if (!banterHeldEvents)
            {
                banterHeldEvents = gameObject.GetComponent<BSHeldEvents>();
                if (!banterHeldEvents)
                {
                    banterHeldEventsAdded = true;
                    banterHeldEvents = gameObject.AddComponent<BSHeldEvents>();
                }
            }
            gameObject.layer = 20;
            if (changedProperties.Contains(PropertyName.grabType))
            {
                banterGrabHandle.GrabType = grabType;
            }
            if (changedProperties.Contains(PropertyName.grabRadius))
            {
                banterGrabHandle.GrabRadius = grabRadius;
            }
            if (changedProperties.Contains(PropertyName.gunTriggerSensitivity))
            {
                banterHeldEvents.Sensitivity = gunTriggerSensitivity;
            }
            if (changedProperties.Contains(PropertyName.gunTriggerFireRate))
            {
                banterHeldEvents.FireRate = gunTriggerFireRate;
            }
            if (changedProperties.Contains(PropertyName.gunTriggerAutoFire))
            {
                banterHeldEvents.Auto = gunTriggerAutoFire;
            }
            if (changedProperties.Contains(PropertyName.blockLeftPrimary))
            {
                banterHeldEvents.BlockLeftPrimary = blockLeftPrimary;
            }
            if (changedProperties.Contains(PropertyName.blockLeftSecondary))
            {
                banterHeldEvents.BlockLeftSecondary = blockLeftSecondary;
            }
            if (changedProperties.Contains(PropertyName.blockRightPrimary))
            {
                banterHeldEvents.BlockRightPrimary = blockRightPrimary;
            }
            if (changedProperties.Contains(PropertyName.blockRightSecondary))
            {
                banterHeldEvents.BlockRightSecondary = blockRightSecondary;
            }
            if (changedProperties.Contains(PropertyName.blockLeftThumbstick))
            {
                banterHeldEvents.BlockLeftThumbstick = blockLeftThumbstick;
            }
            if (changedProperties.Contains(PropertyName.blockRightThumbstick))
            {
                banterHeldEvents.BlockRightThumbstick = blockRightThumbstick;
            }
            if (changedProperties.Contains(PropertyName.blockLeftThumbstickClick))
            {
                banterHeldEvents.BlockLeftThumbstickClick = blockLeftThumbstickClick;
            }
            if (changedProperties.Contains(PropertyName.blockRightThumbstickClick))
            {
                banterHeldEvents.BlockRightThumbstickClick = blockRightThumbstickClick;
            }
            if (changedProperties.Contains(PropertyName.blockLeftTrigger))
            {
                banterHeldEvents.BlockLeftTrigger = blockLeftTrigger;
            }
            if (changedProperties.Contains(PropertyName.blockRightTrigger))
            {
                banterHeldEvents.BlockRightTrigger = blockRightTrigger;
            }
        }
    }
}