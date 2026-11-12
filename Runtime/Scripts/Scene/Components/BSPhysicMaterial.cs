using System;
using System.Collections;
using System.Collections.Generic;
using BS;
using UnityEngine;
using UnityEngine.Video;
using PropertyName = BS.PropertyName;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]

    public partial class BSPhysicMaterial : BSComponentBase
    {
        [Tooltip("The dynamic friction of the material, affecting movement when in contact with another surface.")]
        [See(initial = "1")][SerializeField] internal float dynamicFriction = 1;

        [Tooltip("The static friction of the material, determining the resistance to starting movement.")]
        [See(initial = "1")][SerializeField] internal float staticFriction = 1;

        PhysicsMaterial _material;
        Collider _collider;
        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has applied the fields yet (see valuesApplied).
                ReSetup();
            }
            SetupPhysicMaterial(null);
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            SetupPhysicMaterial(changedProperties);
        }

        internal override void UpdateStuff()
        {

        }
        void SetupPhysicMaterial(List<PropertyName> changedProperties = null)
        {
            if (_collider == null)
            {
                _collider = GetComponent<Collider>();
            }
            if (_collider == null && GetComponent<MeshFilter>())
            {
                // Nothing to put the material on yet: give the mesh a collider.
                var meshCollider = gameObject.AddComponent<MeshCollider>();
                meshCollider.convex = true;
                _collider = meshCollider;
            }
            if (_collider != null)
            {
                if (_material == null)
                {
                    _material = new PhysicsMaterial();
                    // This component's long-standing behaviour: no bounce, and the lower friction of
                    // the two surfaces wins, so a low value here makes the object slippery against
                    // anything. BSPhysicsMaterial is the one with bounce and combine modes to set.
                    _material.bounciness = 0;
                    _material.frictionCombine = PhysicsMaterialCombine.Minimum;
                    _material.bounceCombine = PhysicsMaterialCombine.Minimum;
                }

                if (changedProperties?.Contains(PropertyName.dynamicFriction) ?? false)
                {
                    _material.dynamicFriction = dynamicFriction;
                }
                if (changedProperties?.Contains(PropertyName.staticFriction) ?? false)
                {
                    _material.staticFriction = staticFriction;
                }
                // sharedMaterial: the material getter can hand back a per-collider copy, and writing
                // through that copy detached the collider from later changes to _material.
                if (_collider.sharedMaterial != _material)
                {
                    _collider.sharedMaterial = _material;
                }
            }
            SetLoadedIfNot();
        }

        internal override void DestroyStuff()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}