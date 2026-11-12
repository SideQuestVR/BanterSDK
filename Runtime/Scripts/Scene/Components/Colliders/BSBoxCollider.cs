using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [WatchComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(BSObjectId))]
    public partial class BSBoxCollider : UnityComponentBase
    {
        // Only a script's constructor values reach the Box Collider through these. In a scene the Box Collider's own
        // values are the ones that count, so these stay out of the inspector.
        [See(initial = "false")][SerializeField][HideInInspector] internal bool isTrigger = false;
        [See(initial = "0,0,0")][SerializeField][HideInInspector] internal Vector3 center = Vector3.zero;
        [See(initial = "1,1,1")][SerializeField][HideInInspector] internal Vector3 size = Vector3.one;
    }
}