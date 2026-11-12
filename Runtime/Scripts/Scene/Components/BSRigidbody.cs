using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BS;
using UnityEngine;
using PropertyName = BS.PropertyName;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]

    public partial class BSRigidbody : BSComponentBase
    {
        [Tooltip("The mass of the rigidbody, affecting its inertia and interactions with forces.")]
        [See(initial = "1")][SerializeField] internal float mass = 1;

        [Tooltip("The linear drag of the rigidbody, reducing its velocity over time.")]
        [See(initial = "0")][SerializeField] internal float drag = 0;

        [Tooltip("The angular drag of the rigidbody, reducing rotational motion over time.")]
        [See(initial = "0.05")][SerializeField] internal float angularDrag = 0.05f;

        [Tooltip("Determines if the rigidbody is kinematic (not affected by physics but can be moved via code).")]
        [See(initial = "false")][SerializeField] internal bool isKinematic = false;

        [Tooltip("Determines if gravity affects this rigidbody.")]
        [See(initial = "true")][SerializeField] internal bool useGravity = true;

        [Tooltip("Sets the center of mass for the rigidbody, affecting rotation and stability.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 centerOfMass = Vector3.zero;

        [Tooltip("The collision detection mode for the rigidbody, affecting physics precision.")]
        [See(initial = "0")][SerializeField] internal CollisionDetectionMode collisionDetectionMode = CollisionDetectionMode.Discrete;

        [Tooltip("The velocity of the rigidbody, representing its movement in world space.")]
        [Watch(initial = "0,0,0")][SerializeField] internal Vector3 velocity = Vector3.zero;

        [Tooltip("The angular velocity of the rigidbody, representing its rotational movement.")]
        [Watch(initial = "0,0,0")][SerializeField] internal Vector3 angularVelocity = Vector3.zero;

        [Tooltip("Locks movement along the X axis.")]
        [See(initial = "false")][SerializeField] internal bool freezePositionX = false;

        [Tooltip("Locks movement along the Y axis.")]
        [See(initial = "false")][SerializeField] internal bool freezePositionY = false;

        [Tooltip("Locks movement along the Z axis.")]
        [See(initial = "false")][SerializeField] internal bool freezePositionZ = false;

        [Tooltip("Locks rotation around the X axis.")]
        [See(initial = "false")][SerializeField] internal bool freezeRotationX = false;

        [Tooltip("Locks rotation around the Y axis.")]
        [See(initial = "false")][SerializeField] internal bool freezeRotationY = false;

        [Tooltip("Locks rotation around the Z axis.")]
        [See(initial = "false")][SerializeField] internal bool freezeRotationZ = false;

        [Method]
        public void _AddForce(Vector3 force, ForceMode mode)
        {
            Debug.Log("AddForce: " + force + " - " + mode);
            _rigidbody.AddForce(force, mode);
        }
        [Method]
        public void _MovePosition(Vector3 position)
        {
            _rigidbody.MovePosition(position);
        }
        [Method]
        public void _MoveRotation(Quaternion rotation)
        {
            _rigidbody.MoveRotation(rotation);
        }
        [Method(overload = "Values")]
        public void _AddForce(float x, float y, float z, ForceMode mode)
        {
            _rigidbody.AddForce(x, y, z, mode);
        }
        [Method]
        public void _Sleep()
        {
            _rigidbody.Sleep();
        }
        [Method]
        public void _WakeUp()
        {
            _rigidbody.WakeUp();
        }
        [Method]
        public void _AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius, float upwardsModifier, ForceMode mode)
        {
            _rigidbody.AddExplosionForce(explosionForce, explosionPosition, explosionRadius, upwardsModifier, mode);
        }
        [Method]
        public void _AddTorque(Vector3 torque, ForceMode mode)
        {
            _rigidbody.AddTorque(torque, mode);
        }
        [Method(overload = "Values")]
        public void _AddTorque(float x, float y, float z, ForceMode mode)
        {
            _rigidbody.AddTorque(x, y, z, mode);
        }
        [Method]
        public void _AddRelativeForce(Vector3 force, ForceMode mode)
        {
            _rigidbody.AddRelativeForce(force, mode);
        }
        [Method]
        public void _AddRelativeTorque(Vector3 torque, ForceMode mode)
        {
            _rigidbody.AddRelativeTorque(torque, mode);
        }
        [Method]
        public void _AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode)
        {
            _rigidbody.AddForceAtPosition(force, position, mode);
        }
        [Method]
        public void _ResetCenterOfMass()
        {
            _rigidbody.ResetCenterOfMass();
        }
        [Method]
        public void _ResetInertiaTensor()
        {
            _rigidbody.ResetInertiaTensor();
        }
        // Appended last, after the other [Method]s: the Bridge 2 lock lists members in declaration order, new ones at the end.
        // Moves along one axis (0 = X, 1 = Y, 2 = Z) from where the body is now, leaving the other two
        // to physics. A page only ever knows a position a few frames old, so MovePosition would drag the
        // other axes back to it; this lets a page set, say, ride height while physics drives the rest.
        [Method]
        public void _MovePositionSingleAxis(int axis, float value)
        {
            if (axis < 0 || axis > 2) return;
            var position = _rigidbody.position;
            position[axis] = value;
            _rigidbody.MovePosition(position);
        }
        // Sets velocity along one axis (0 = X, 1 = Y, 2 = Z), keeping the current velocity on the others.
        // The velocity property replaces all three with the page's (late) copy.
        [Method]
        public void _SetVelocitySingleAxis(int axis, float value)
        {
            if (axis < 0 || axis > 2) return;
            var velocity = _rigidbody.linearVelocity;
            velocity[axis] = value;
            _rigidbody.linearVelocity = velocity;
        }

        Rigidbody _rigidbody;
        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                ApplyInspectorValues();
            }
            SetupRigidbody();
        }

        // Placed in the Inspector, so nothing has applied the fields yet (see valuesApplied).
        void ApplyInspectorValues()
        {
            _rigidbody = GetComponent<Rigidbody>();
            // mass 0 is a component saved before these fields had defaults: it never applied anything
            // and its other fields are zeros too (no gravity), so it keeps Unity's defaults as before.
            if (_rigidbody == null && mass > 0)
            {
                // This component adds the Rigidbody, so its fields configure it.
                var properties = new List<PropertyName> { PropertyName.mass, PropertyName.drag, PropertyName.angularDrag,
                    PropertyName.isKinematic, PropertyName.useGravity, PropertyName.collisionDetectionMode,
                    PropertyName.freezePositionX, PropertyName.freezePositionY, PropertyName.freezePositionZ,
                    PropertyName.freezeRotationX, PropertyName.freezeRotationY, PropertyName.freezeRotationZ };
                // Setting either explicitly turns off Unity's automatic value, and a velocity on a
                // kinematic body logs a warning, so only the ones actually set.
                if (centerOfMass != Vector3.zero) properties.Add(PropertyName.centerOfMass);
                if (!isKinematic && velocity != Vector3.zero) properties.Add(PropertyName.velocity);
                if (!isKinematic && angularVelocity != Vector3.zero) properties.Add(PropertyName.angularVelocity);
                UpdateCallback(properties);
                return;
            }
            // A Rigidbody already on the object is the creator's own and keeps its settings. Either
            // way the fields are read back from the Rigidbody, so JS sees the values in effect.
            SetupRigidbody();
            mass = _rigidbody.mass;
            drag = _rigidbody.linearDamping;
            angularDrag = _rigidbody.angularDamping;
            isKinematic = _rigidbody.isKinematic;
            useGravity = _rigidbody.useGravity;
            centerOfMass = _rigidbody.centerOfMass;
            collisionDetectionMode = _rigidbody.collisionDetectionMode;
            var constraints = _rigidbody.constraints;
            freezePositionX = (constraints & RigidbodyConstraints.FreezePositionX) != 0;
            freezePositionY = (constraints & RigidbodyConstraints.FreezePositionY) != 0;
            freezePositionZ = (constraints & RigidbodyConstraints.FreezePositionZ) != 0;
            freezeRotationX = (constraints & RigidbodyConstraints.FreezeRotationX) != 0;
            freezeRotationY = (constraints & RigidbodyConstraints.FreezeRotationY) != 0;
            freezeRotationZ = (constraints & RigidbodyConstraints.FreezeRotationZ) != 0;
            valuesApplied = true;
            SyncProperties(true);
        }
        void SetupRigidbody(List<PropertyName> changedProperties = null)
        {
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.GetComponent<Rigidbody>();
            }
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.AddComponent<Rigidbody>();
            }
            if (changedProperties?.Contains(PropertyName.mass) ?? false)
            {
                _rigidbody.mass = mass;
            }
            if (changedProperties?.Contains(PropertyName.drag) ?? false)
            {
                _rigidbody.linearDamping = drag;
            }
            if (changedProperties?.Contains(PropertyName.angularDrag) ?? false)
            {
                _rigidbody.angularDamping = angularDrag;
            }
            if (changedProperties?.Contains(PropertyName.isKinematic) ?? false)
            {
                _rigidbody.isKinematic = isKinematic;
            }
            if (changedProperties?.Contains(PropertyName.useGravity) ?? false)
            {
                _rigidbody.useGravity = useGravity;
            }
            if (changedProperties?.Contains(PropertyName.centerOfMass) ?? false)
            {
                _rigidbody.centerOfMass = centerOfMass;
            }
            if (changedProperties?.Contains(PropertyName.collisionDetectionMode) ?? false)
            {
                _rigidbody.collisionDetectionMode = collisionDetectionMode;
            }
            if (changedProperties?.Contains(PropertyName.velocity) ?? false)
            {
                _rigidbody.linearVelocity = velocity;
            }
            if (changedProperties?.Contains(PropertyName.angularVelocity) ?? false)
            {
                _rigidbody.angularVelocity = angularVelocity;
            }
            if ((changedProperties?.Contains(PropertyName.freezePositionX) ?? false) ||
                (changedProperties?.Contains(PropertyName.freezePositionY) ?? false) ||
                (changedProperties?.Contains(PropertyName.freezePositionZ) ?? false) ||
                (changedProperties?.Contains(PropertyName.freezeRotationX) ?? false) ||
                (changedProperties?.Contains(PropertyName.freezeRotationY) ?? false) ||
                (changedProperties?.Contains(PropertyName.freezeRotationZ) ?? false))
            {
                RigidbodyConstraints constraints = RigidbodyConstraints.None;
                if (freezePositionX)
                {
                    constraints |= RigidbodyConstraints.FreezePositionX;
                }
                if (freezePositionY)
                {
                    constraints |= RigidbodyConstraints.FreezePositionY;
                }
                if (freezePositionZ)
                {
                    constraints |= RigidbodyConstraints.FreezePositionZ;
                }
                if (freezeRotationX)
                {
                    constraints |= RigidbodyConstraints.FreezeRotationX;
                }
                if (freezeRotationY)
                {
                    constraints |= RigidbodyConstraints.FreezeRotationY;
                }
                if (freezeRotationZ)
                {
                    constraints |= RigidbodyConstraints.FreezeRotationZ;
                }
                _rigidbody.constraints = constraints;
            }
            SetLoadedIfNot();
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            SetupRigidbody(changedProperties);
        }

        internal override void DestroyStuff()
        {
            if (_rigidbody != null)
            {
                Destroy(_rigidbody);
            }
        }

        internal override void UpdateStuff()
        {
            // Not set up yet (a JS component with no properties on an inactive object), or removed under us.
            if (_rigidbody == null) return;
            velocity = _rigidbody.linearVelocity;
            angularVelocity = _rigidbody.angularVelocity;
        }

        public override UnityEngine.Object GetReferenceObject()
        {
            return _rigidbody;
        }
    }
}