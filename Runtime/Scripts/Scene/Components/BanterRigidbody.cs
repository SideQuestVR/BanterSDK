using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Banter.SDK;
using UnityEngine;
using PropertyName = Banter.SDK.PropertyName;

namespace Banter.SDK
{
    /* 
    #### Banter Rigidbody
    This component will add a rigidbody to the object and set the mass, drag, angular drag, is kinematic, use gravity, center of mass, collision detection mode, velocity, angular velocity, freeze position and freeze rotation of the rigidbody.

    **Properties**
     - `mass` - The mass of the rigidbody.
     - `drag` - The drag of the rigidbody.
     - `angularDrag` - The angular drag of the rigidbody.
     - `isKinematic` - Whether the rigidbody is kinematic.
     - `useGravity` - Whether the rigidbody uses gravity.
     - `centerOfMass` - The center of mass of the rigidbody.
     - `collisionDetectionMode` - The collision detection mode of the rigidbody.
     - `velocity` - The velocity of the rigidbody.
     - `angularVelocity` - The angular velocity of the rigidbody.
     - `freezePositionX` - Whether to freeze the position on the x axis.
     - `freezePositionY` - Whether to freeze the position on the y axis.
     - `freezePositionZ` - Whether to freeze the position on the z axis.
     - `freezeRotationX` - Whether to freeze the rotation on the x axis.
     - `freezeRotationY` - Whether to freeze the rotation on the y axis.
     - `freezeRotationZ` - Whether to freeze the rotation on the z axis.

     **Methods**
     ```js
    // AddForce - Add a force to the rigidbody.
    rigidBody.AddForce(force: BS.Vector3, mode: BS.ForceMode);
    ```
    ```js
    // MovePosition - Move the position of the rigidbody.
    rigidBody.MovePosition(position: BS.Vector3);
    ```
    ```js
    // MoveRotation - Move the rotation of the rigidbody.
    rigidBody.MoveRotation(rotation: BS.Quaternion);
    ```
    ```js
    // AddForceValues - Add a force to the rigidbody.
    rigidBody.AddForceValues(x: float, y: float, z: float, mode: BS.ForceMode);
    ```
    ```js
    // Sleep - Put the rigidbody to sleep.
    rigidBody.Sleep();
    ```
    ```js
    // AddExplosionForce - Add an explosion force to the rigidbody.
    rigidBody.AddExplosionForce(explosionForce: float, explosionPosition: BS.Vector3, explosionRadius: float, upwardsModifier: float, mode: BS.ForceMode);
    ```


    **Code Example**
    ```js
        const mass = 1;
        const drag = 0;
        const angularDrag = 0.05;
        const isKinematic = false;
        const useGravity = true;
        const centerOfMass = new BS.Vector3(0,0,0);
        const collisionDetectionMode = BS.CollisionDetectionMode.Continuous;
        const velocity = new BS.Vector3(0,0,0);
        const angularVelocity = new BS.Vector3(0,0,0);
        const freezePositionX = false;
        const freezePositionY = false;
        const freezePositionZ = false;
        const freezeRotationX = false;
        const freezeRotationY = false;
        const freezeRotationZ = false;
        const gameObject = new BS.GameObject("MyRigidbody");
        const rigidBody = await gameObject.AddComponent(new BS.BanterRigidbody(mass, drag, angularDrag, isKinematic, useGravity, centerOfMass, collisionDetectionMode, velocity, angularVelocity, freezePositionX, freezePositionY, freezePositionZ, freezeRotationX, freezeRotationY, freezeRotationZ));
    ```
    */
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]

    public class BanterRigidbody : BanterComponentBase
    {
        [See(initial = "1")] public float mass;
        [See(initial = "0")] public float drag;
        [See(initial = "0.05")] public float angularDrag;
        [See(initial = "false")] public bool isKinematic;
        [See(initial = "true")] public bool useGravity;
        [See(initial = "0,0,0")] public Vector3 centerOfMass;
        [See(initial = "0")] public CollisionDetectionMode collisionDetectionMode;
        [Watch(initial = "0,0,0")] public Vector3 velocity;
        [Watch(initial = "0,0,0")] public Vector3 angularVelocity;
        [See(initial = "false")] public bool freezePositionX;
        [See(initial = "false")] public bool freezePositionY;
        [See(initial = "false")] public bool freezePositionZ;
        [See(initial = "false")] public bool freezeRotationX;
        [See(initial = "false")] public bool freezeRotationY;
        [See(initial = "false")] public bool freezeRotationZ;
        [Method]
        public void _AddForce(Vector3 force, ForceMode mode)
        {
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
        public void _AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius, float upwardsModifier, ForceMode mode)
        {
            _rigidbody.AddExplosionForce(explosionForce, explosionPosition, explosionRadius, upwardsModifier, mode);
        }

        Rigidbody _rigidbody;
        internal override void StartStuff()
        {
            SetupRigidbody();
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
                _rigidbody.drag = drag;
            }
            if (changedProperties?.Contains(PropertyName.angularDrag) ?? false)
            {
                _rigidbody.angularDrag = angularDrag;
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
                _rigidbody.velocity = velocity;
            }
            if (changedProperties?.Contains(PropertyName.velocity) ?? false)
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

        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupRigidbody(changedProperties);
        }

        internal override void DestroyStuff()
        {
            if (_rigidbody != null)
            {
                Destroy(_rigidbody);
            }
        }
        // BANTER COMPILED CODE 
        [Header("SYNC BANTERRIGIDBODY TO JS")]
        public bool _velocity;
        public bool _angularVelocity;

        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.velocity, PropertyName.angularVelocity, PropertyName.mass, PropertyName.drag, PropertyName.angularDrag, PropertyName.isKinematic, PropertyName.useGravity, PropertyName.centerOfMass, PropertyName.collisionDetectionMode, PropertyName.freezePositionX, PropertyName.freezePositionY, PropertyName.freezePositionZ, PropertyName.freezeRotationX, PropertyName.freezeRotationY, PropertyName.freezeRotationZ, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterRigidbody);

            scene.Tick += Tick;
            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);
            scene.Tick -= Tick;
            DestroyStuff();
        }

        void AddForce(Vector3 force, ForceMode mode)
        {
            _AddForce(force, mode);
        }
        void MovePosition(Vector3 position)
        {
            _MovePosition(position);
        }
        void MoveRotation(Quaternion rotation)
        {
            _MoveRotation(rotation);
        }
        void AddForce(float x, float y, float z, ForceMode mode)
        {
            _AddForce(x, y, z, mode);
        }
        void Sleep()
        {
            _Sleep();
        }
        void AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius, float upwardsModifier, ForceMode mode)
        {
            _AddExplosionForce(explosionForce, explosionPosition, explosionRadius, upwardsModifier, mode);
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "AddForce" && parameters.Count == 2 && parameters[0] is Vector3 && parameters[1] is int)
            {
                var force = (Vector3)parameters[0];
                var mode = (ForceMode)parameters[1];
                AddForce(force, mode);
                return null;
            }
            else if (methodName == "MovePosition" && parameters.Count == 1 && parameters[0] is Vector3)
            {
                var position = (Vector3)parameters[0];
                MovePosition(position);
                return null;
            }
            else if (methodName == "MoveRotation" && parameters.Count == 1 && parameters[0] is Quaternion)
            {
                var rotation = (Quaternion)parameters[0];
                MoveRotation(rotation);
                return null;
            }
            else if (methodName == "AddForce" && parameters.Count == 4 && parameters[0] is float && parameters[1] is float && parameters[2] is float && parameters[3] is int)
            {
                var x = (float)parameters[0];
                var y = (float)parameters[1];
                var z = (float)parameters[2];
                var mode = (ForceMode)parameters[3];
                AddForce(x, y, z, mode);
                return null;
            }
            else if (methodName == "Sleep" && parameters.Count == 0)
            {
                Sleep();
                return null;
            }
            else if (methodName == "AddExplosionForce" && parameters.Count == 5 && parameters[0] is float && parameters[1] is Vector3 && parameters[2] is float && parameters[3] is float && parameters[4] is int)
            {
                var explosionForce = (float)parameters[0];
                var explosionPosition = (Vector3)parameters[1];
                var explosionRadius = (float)parameters[2];
                var upwardsModifier = (float)parameters[3];
                var mode = (ForceMode)parameters[4];
                AddExplosionForce(explosionForce, explosionPosition, explosionRadius, upwardsModifier, mode);
                return null;
            }
            else
            {
                return null;
            }
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterVector3)
                {
                    var valvelocity = (BanterVector3)values[i];
                    if (valvelocity.n == PropertyName.velocity)
                    {
                        velocity = new Vector3(valvelocity.x, valvelocity.y, valvelocity.z);
                        changedProperties.Add(PropertyName.velocity);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valangularVelocity = (BanterVector3)values[i];
                    if (valangularVelocity.n == PropertyName.angularVelocity)
                    {
                        angularVelocity = new Vector3(valangularVelocity.x, valangularVelocity.y, valangularVelocity.z);
                        changedProperties.Add(PropertyName.angularVelocity);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valmass = (BanterFloat)values[i];
                    if (valmass.n == PropertyName.mass)
                    {
                        mass = valmass.x;
                        changedProperties.Add(PropertyName.mass);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valdrag = (BanterFloat)values[i];
                    if (valdrag.n == PropertyName.drag)
                    {
                        drag = valdrag.x;
                        changedProperties.Add(PropertyName.drag);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valangularDrag = (BanterFloat)values[i];
                    if (valangularDrag.n == PropertyName.angularDrag)
                    {
                        angularDrag = valangularDrag.x;
                        changedProperties.Add(PropertyName.angularDrag);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valisKinematic = (BanterBool)values[i];
                    if (valisKinematic.n == PropertyName.isKinematic)
                    {
                        isKinematic = valisKinematic.x;
                        changedProperties.Add(PropertyName.isKinematic);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valuseGravity = (BanterBool)values[i];
                    if (valuseGravity.n == PropertyName.useGravity)
                    {
                        useGravity = valuseGravity.x;
                        changedProperties.Add(PropertyName.useGravity);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valcenterOfMass = (BanterVector3)values[i];
                    if (valcenterOfMass.n == PropertyName.centerOfMass)
                    {
                        centerOfMass = new Vector3(valcenterOfMass.x, valcenterOfMass.y, valcenterOfMass.z);
                        changedProperties.Add(PropertyName.centerOfMass);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valcollisionDetectionMode = (BanterInt)values[i];
                    if (valcollisionDetectionMode.n == PropertyName.collisionDetectionMode)
                    {
                        collisionDetectionMode = (CollisionDetectionMode)valcollisionDetectionMode.x;
                        changedProperties.Add(PropertyName.collisionDetectionMode);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valfreezePositionX = (BanterBool)values[i];
                    if (valfreezePositionX.n == PropertyName.freezePositionX)
                    {
                        freezePositionX = valfreezePositionX.x;
                        changedProperties.Add(PropertyName.freezePositionX);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valfreezePositionY = (BanterBool)values[i];
                    if (valfreezePositionY.n == PropertyName.freezePositionY)
                    {
                        freezePositionY = valfreezePositionY.x;
                        changedProperties.Add(PropertyName.freezePositionY);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valfreezePositionZ = (BanterBool)values[i];
                    if (valfreezePositionZ.n == PropertyName.freezePositionZ)
                    {
                        freezePositionZ = valfreezePositionZ.x;
                        changedProperties.Add(PropertyName.freezePositionZ);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valfreezeRotationX = (BanterBool)values[i];
                    if (valfreezeRotationX.n == PropertyName.freezeRotationX)
                    {
                        freezeRotationX = valfreezeRotationX.x;
                        changedProperties.Add(PropertyName.freezeRotationX);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valfreezeRotationY = (BanterBool)values[i];
                    if (valfreezeRotationY.n == PropertyName.freezeRotationY)
                    {
                        freezeRotationY = valfreezeRotationY.x;
                        changedProperties.Add(PropertyName.freezeRotationY);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valfreezeRotationZ = (BanterBool)values[i];
                    if (valfreezeRotationZ.n == PropertyName.freezeRotationZ)
                    {
                        freezeRotationZ = valfreezeRotationZ.x;
                        changedProperties.Add(PropertyName.freezeRotationZ);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if ((_velocity) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.velocity,
                    type = PropertyType.Vector3,
                    value = velocity,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if ((_angularVelocity) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.angularVelocity,
                    type = PropertyType.Vector3,
                    value = angularVelocity,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.mass,
                    type = PropertyType.Float,
                    value = mass,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.drag,
                    type = PropertyType.Float,
                    value = drag,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.angularDrag,
                    type = PropertyType.Float,
                    value = angularDrag,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isKinematic,
                    type = PropertyType.Bool,
                    value = isKinematic,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.useGravity,
                    type = PropertyType.Bool,
                    value = useGravity,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.centerOfMass,
                    type = PropertyType.Vector3,
                    value = centerOfMass,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.collisionDetectionMode,
                    type = PropertyType.Int,
                    value = collisionDetectionMode,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.freezePositionX,
                    type = PropertyType.Bool,
                    value = freezePositionX,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.freezePositionY,
                    type = PropertyType.Bool,
                    value = freezePositionY,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.freezePositionZ,
                    type = PropertyType.Bool,
                    value = freezePositionZ,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.freezeRotationX,
                    type = PropertyType.Bool,
                    value = freezeRotationX,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.freezeRotationY,
                    type = PropertyType.Bool,
                    value = freezeRotationY,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.freezeRotationZ,
                    type = PropertyType.Bool,
                    value = freezeRotationZ,
                    componentType = ComponentType.BanterRigidbody,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        void Tick(object sender, EventArgs e) { SyncProperties(); }

        internal override void WatchProperties(PropertyName[] properties)
        {
            _velocity = false;
            _angularVelocity = false;
            for (int i = 0; i < properties.Length; i++)
            {
                if (properties[i] == PropertyName.velocity)
                {
                    _velocity = true;
                }
                if (properties[i] == PropertyName.angularVelocity)
                {
                    _angularVelocity = true;
                }
            }
        }
        // END BANTER COMPILED CODE 
    }
}