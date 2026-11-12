using System;
using System.Collections;
using System.Collections.Generic;
using SideQuest.FlexaBody;
using Pixeye.Unity;
using UnityEngine;
using UnityEngine.Events;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSHeldEvents : BSComponentBase
    {

        [Tooltip("Sensitivity for detecting held events (higher values make inputs more sensitive).")]
        [See(initial = "0.5")][SerializeField] internal float sensitivity = 0.5f;

        [Tooltip("Rate at which held events are fired (in seconds).")]
        [See(initial = "0.1")][SerializeField] internal float fireRate = 0.1f;

        [Tooltip("Enable automatic triggering of held events without manual input.")]
        [See(initial = "false")][SerializeField] internal bool auto = false;

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

        BSPlayerEvents banterPlayerEvents;
        bool banterPlayerEventsAdded;

        ControllerHeldEvents controllerHeldEvents;

        bool controllerHeldEventsAdded;

        Handle_Controller handleController;
        bool handleControllerAdded;

        GrabHandle grabHandle;

        bool grabHandleAdded;

        bool worldObjectAdded = true;




        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff()
        {
            if (banterPlayerEvents && banterPlayerEventsAdded)
            {
                Destroy(banterPlayerEvents);
            }
            if (controllerHeldEvents && controllerHeldEventsAdded)
            {
                Destroy(controllerHeldEvents);
            }
            if (handleController && handleControllerAdded)
            {
                Destroy(handleController);
            }
            if (grabHandle && grabHandleAdded)
            {
                Destroy(grabHandle);
            }
        }


        internal override void StartStuff()
        {
            UpdateCallback(null);
            SetLoadedIfNot();
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            // Take an author-added BSPlayerEvents too, or ControllerHeldEvents would be left
            // without one.
            banterPlayerEvents = GetComponent<BSPlayerEvents>();
            if (!banterPlayerEvents)
            {
                banterPlayerEventsAdded = true;
                banterPlayerEvents = gameObject.AddComponent<BSPlayerEvents>();
            }

            controllerHeldEvents = GetComponent<ControllerHeldEvents>();
            if (controllerHeldEvents == null)
            {
                controllerHeldEventsAdded = true;
                controllerHeldEvents = gameObject.AddComponent<ControllerHeldEvents>();
            }

            controllerHeldEvents.banterEvents = banterPlayerEvents;
            handleController = GetComponent<Handle_Controller>();
            if (handleController == null)
            {
                handleControllerAdded = true;
                handleController = gameObject.AddComponent<Handle_Controller>();
            }

            handleController.Sensitivity = Sensitivity;
            handleController.FireTime = FireRate;
            handleController.AutoFire = Auto;
            handleController.Controllable = controllerHeldEvents;
            handleController.InputBlocks = new InputBlockList[]{
                new InputBlockList(){
                    PrimaryButton = BlockLeftPrimary,
                    SecondaryButton = BlockLeftSecondary,
                    Thumbstick = BlockLeftThumbstick,
                    ThumbstickClick = BlockLeftThumbstickClick,
                    Trigger = BlockLeftTrigger,
                },
                new InputBlockList(){
                    PrimaryButton = BlockRightPrimary,
                    SecondaryButton = BlockRightSecondary,
                    Thumbstick = BlockRightThumbstick,
                    ThumbstickClick = BlockRightThumbstickClick,
                    Trigger = BlockRightTrigger,
                }
            };

            grabHandle = GetComponent<GrabHandle>();
            if (grabHandle == null)
            {
                grabHandleAdded = true;
                grabHandle = gameObject.AddComponent<GrabHandle>();
            }

            grabHandle._handleFunctions = new HandleFunction[] { handleController };
            Rigidbody rb = grabHandle.Col?.attachedRigidbody;
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
    }
}