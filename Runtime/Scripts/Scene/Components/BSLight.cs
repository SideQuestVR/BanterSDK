using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(Light))]
    [RequireComponent(typeof(Light))]
    [RequireComponent(typeof(BSObjectId))]
    public partial class BSLight : UnityComponentBase
    {
        [Tooltip("The type of light (0 = Spot, 1 = Directional, 2 = Point).")]
        [See(initial = "0")][SerializeField] internal LightType type = 0;

        [Tooltip("The color of the light.")]
        [See(initial = "1,1,1,1")][SerializeField] internal Vector4 color = new Vector4(1, 1, 1, 1);

        [Tooltip("The brightness of the light.")]
        [See(initial = "1")][SerializeField] internal float intensity = 1f;

        [Tooltip("How far the light reaches (Point and Spot only).")]
        [See(initial = "10")][SerializeField] internal float range = 10f;

        [Tooltip("The angle of the light cone in degrees (Spot only).")]
        [See(initial = "30")][SerializeField] internal float spotAngle = 30f;

        [Tooltip("The inner angle of the light cone in degrees (Spot only).")]
        [See(initial = "21.8")][SerializeField] internal float innerSpotAngle = 21.8f;

        [Tooltip("The type of shadows to cast (0 = None, 1 = Hard, 2 = Soft).")]
        [See(initial = "0")][SerializeField] internal LightShadows shadows = 0;
    }
}