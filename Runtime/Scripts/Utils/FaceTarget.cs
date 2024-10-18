
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Banter.SDK
{
    public class FaceTarget : MonoBehaviour
    {
        [Tooltip("Manually set the target GameObject. If left null, the script will search for the GameObject tagged 'PlayerHead'.")]
        public GameObject targetObject;

        [Header("Smoothing Settings")]
        [Tooltip("0 for hard look at, larger values for more smoothing")]
        public float smoothing = 0.0f;

        [Header("Axis Constraints")]
        [Tooltip("Enable or disable rotation around the X axis")]
        public bool enableXAxis = true;
        [Tooltip("Enable or disable rotation around the Y axis")]
        public bool enableYAxis = true;
        [Tooltip("Enable or disable rotation around the Z axis")]
        public bool enableZAxis = true;

        [Header("Billboarding")]
        [Tooltip("Follow the forward direction of the head")]
        public bool isBillboard = false;

        // [Header("Sync Options")]
        // public UniqueObjectId uniqueObjectId;

        private Transform playerTransform; // The Transform component of the player
        // public TriggerEvent triggerEvent;

        public void SetTarget()
        {
            // targetObject = triggerEvent == null ? uniqueObjectId.lastObject.gameObject : triggerEvent.uniqueObjectId.lastObject.gameObject;
            playerTransform = targetObject.transform;
        }

        public void ResetTarget()
        {
            targetObject = null;
        }

        public void SetSmoothing(float newSmoothing)
        {
            smoothing = newSmoothing;
        }

        public void SetEnableXAxis(bool isEnabled)
        {
            enableXAxis = isEnabled;
        }

        public void SetEnableYAxis(bool isEnabled)
        {
            enableYAxis = isEnabled;
        }

        public void SetEnableZAxis(bool isEnabled)
        {
            enableZAxis = isEnabled;
        }


        void Start()
        {
            // if(triggerEvent == null) {
            //     triggerEvent = GetComponent<TriggerEvent>();
            // }
            if (targetObject != null)
            {
                playerTransform = targetObject.transform; // Set the player's transform if the target GameObject is manually set
            }
            else
            {
                playerTransform = Camera.main.transform;
            }
        }


        void Update()
        {
            if (playerTransform != null)
            {
                // Calculate the rotation needed to look at the player
                Quaternion targetRotation = isBillboard ? Quaternion.LookRotation(transform.position - playerTransform.position) : Quaternion.LookRotation(playerTransform.position - transform.position);

                if (smoothing == 0.0f)
                {
                    // Apply immediate rotation without smoothing
                    ApplyRotation(targetRotation);
                }
                else
                {
                    // Calculate the smooth factor (inverted logic)
                    float smoothFactor = 1 / smoothing;

                    // Interpolate smoothly towards the target rotation
                    Quaternion interpolatedRotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * smoothFactor);
                    ApplyRotation(interpolatedRotation);
                }
            }
        }


        void ApplyRotation(Quaternion rotation)
        {
            Vector3 currentRotation = transform.eulerAngles;
            Vector3 targetEulerRotation = rotation.eulerAngles;

            // Apply the target rotation only to the enabled axes
            float newX = enableXAxis ? targetEulerRotation.x : currentRotation.x;
            float newY = enableYAxis ? targetEulerRotation.y : currentRotation.y;
            float newZ = enableZAxis ? targetEulerRotation.z : currentRotation.z;

            // Construct the final rotation
            Quaternion finalRotation = Quaternion.Euler(newX, newY, newZ);

            transform.rotation = finalRotation;
        }


    }
}
