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

    public partial class BSPhysicsMaterial : BSComponentBase
    {
        [Tooltip("The dynamic friction of the material, affecting movement when in contact with another surface.")]
        [See(initial = "1")][SerializeField] internal float dynamicFriction = 1;

        [Tooltip("The static friction of the material, determining the resistance to starting movement.")]
        [See(initial = "1")][SerializeField] internal float staticFriction = 1;

        [Tooltip("How bouncy the material is: 0 = no bounce, 1 = bounces back with no loss of energy.")]
        [See(initial = "1")][SerializeField] internal float bounciness = 1;

        [See(initial = "0")][SerializeField] internal PhysicsMaterialCombine frictionCombine = PhysicsMaterialCombine.Average;
        [See(initial = "0")][SerializeField] internal PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Average;

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
                }

                if (changedProperties?.Contains(PropertyName.dynamicFriction) ?? false)
                {
                    _material.dynamicFriction = dynamicFriction;
                }
                if (changedProperties?.Contains(PropertyName.staticFriction) ?? false)
                {
                    _material.staticFriction = staticFriction;
                }
                if (changedProperties?.Contains(PropertyName.bounciness) ?? false)
                {
                    _material.bounciness = bounciness;
                }
                if (changedProperties?.Contains(PropertyName.frictionCombine) ?? false)
                {
                    _material.frictionCombine = frictionCombine;
                }
                if (changedProperties?.Contains(PropertyName.bounceCombine) ?? false)
                {
                    _material.bounceCombine = bounceCombine;
                }
                // sharedMaterial: the material getter can hand back a per-collider copy rather than
                // _material itself.
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