using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    /* 
    #### Transform
    Add a transform to the object. Every object had a transform by default in Unity, this component sets up tracking for the transform properties.

    **Properties**
     - `position` - The position of the object.
     - `localPosition` - The local position of the object.
     - `rotation` - The rotation of the object.
     - `localRotation` - The local rotation of the object.
     - `localScale` - The local scale of the object.
     - `eulerAngles` - The euler angles of the object.
     - `localEulerAngles` - The local euler angles of the object.
     - `up` - The up vector of the object.
     - `forward` - The forward vector of the object.
     - `right` - The right vector of the object.
     - `lerpPosition` - Whether to lerp the position of the object.
     - `lerpRotation` - Whether to lerp the rotation of the object.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("MyTransform");
        const transform = await gameObject.AddComponent(new BS.BanterTransform());
        transform.position = new BS.Vector3(1,1,1);
    ```
    */
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent(typeof(Transform))]
    public class BanterTransform : UnityComponentBase
    {
        [Watch(initial = "0,0,0")][HideInInspector][SerializeField] internal Vector3 position = Vector3.zero;
        [Watch(initial = "0,0,0")][HideInInspector][SerializeField] internal Vector3 localPosition = Vector3.zero;
        [Watch(initial = "0,0,0,1")][HideInInspector][SerializeField] internal Quaternion rotation = Quaternion.identity;
        [Watch(initial = "0,0,0,1")][HideInInspector][SerializeField] internal Quaternion localRotation = Quaternion.identity;
        [Watch(initial = "1,1,1")][HideInInspector][SerializeField] internal Vector3 localScale = Vector3.one;
        [Watch(initial = "0,0,0")][HideInInspector][SerializeField] internal Vector3 eulerAngles = Vector3.zero;
        [Watch(initial = "0,0,0")][HideInInspector][SerializeField] internal Vector3 localEulerAngles = Vector3.zero;
        [Watch(initial = "0,1,0")][HideInInspector][SerializeField] internal Vector3 up = Vector3.up;
        [Watch(initial = "0,0,1")][HideInInspector][SerializeField] internal Vector3 forward = Vector3.forward;
        [Watch(initial = "1,0,0")][HideInInspector][SerializeField] internal Vector3 right = Vector3.right;
        [See(initial = "false")][HideInInspector][SerializeField] internal bool lerpPosition = false;
        [See(initial = "false")][HideInInspector][SerializeField] internal bool lerpRotation = false;
        float _stepPosition = 0.3f;
        Vector3 tempPosition;
        Quaternion tempRotation;
        float _stepRotation = 0.1f;

        void Update()
        {
            if (lerpPosition)
            {
                float distance = Vector3.Distance(transform.localPosition, tempPosition);
                transform.localPosition = Vector3.MoveTowards(transform.localPosition, tempPosition, distance * _stepPosition);
            }
            if (lerpRotation)
            {
                float angle = Quaternion.Angle(transform.localRotation, tempRotation);
                transform.localRotation = Quaternion.RotateTowards(transform.localRotation, tempRotation, angle * _stepRotation);
            }
        }
        // BANTER COMPILED CODE 
        public UnityEngine.Vector3 _position { get { return position; } set { position = value; } }
        public UnityEngine.Vector3 _localPosition { get { return localPosition; } set { localPosition = value; } }
        public UnityEngine.Quaternion _rotation { get { return rotation; } set { rotation = value; } }
        public UnityEngine.Quaternion _localRotation { get { return localRotation; } set { localRotation = value; } }
        public UnityEngine.Vector3 _localScale { get { return localScale; } set { localScale = value; } }
        public UnityEngine.Vector3 _eulerAngles { get { return eulerAngles; } set { eulerAngles = value; } }
        public UnityEngine.Vector3 _localEulerAngles { get { return localEulerAngles; } set { localEulerAngles = value; } }
        public UnityEngine.Vector3 _up { get { return up; } set { up = value; } }
        public UnityEngine.Vector3 _forward { get { return forward; } set { forward = value; } }
        public UnityEngine.Vector3 _right { get { return right; } set { right = value; } }
        public System.Boolean _lerpPosition { get { return lerpPosition; } set { lerpPosition = value; } }
        public System.Boolean _lerpRotation { get { return lerpRotation; } set { lerpRotation = value; } }
        [Header("SYNC TRANSFORM TO JS")]
        public bool sync_position;
        public bool sync_localPosition;
        public bool sync_rotation;
        public bool sync_localRotation;
        public bool sync_localScale;
        public bool sync_eulerAngles;
        public bool sync_localEulerAngles;
        public bool sync_up;
        public bool sync_forward;
        public bool sync_right;
        public Transform _componentType;
        public Transform componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = transform;
                }
                return _componentType;
            }
        }
        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {

        }

        internal override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;


            scene.Tick += Tick;
            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);
            SetLoadedIfNot();
        }

        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);
            scene.Tick -= Tick;

        }

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterVector3)
                {
                    var valposition = (BanterVector3)values[i];
                    if (valposition.n == PropertyName.position)
                    {
                        componentType.position = new Vector3(valposition.x, valposition.y, valposition.z);
                        changedProperties.Add(PropertyName.position);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var vallocalPosition = (BanterVector3)values[i];
                    if (vallocalPosition.n == PropertyName.localPosition)
                    {
                        if (lerpPosition)
                        {
                            tempPosition = new Vector3(vallocalPosition.x, vallocalPosition.y, vallocalPosition.z);
                        }
                        else
                        {
                            componentType.localPosition = new Vector3(vallocalPosition.x, vallocalPosition.y, vallocalPosition.z);
                        }
                        changedProperties.Add(PropertyName.localPosition);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valrotation = (BanterVector4)values[i];
                    if (valrotation.n == PropertyName.rotation)
                    {
                        componentType.rotation = new Quaternion(valrotation.x, valrotation.y, valrotation.z, valrotation.w);
                        changedProperties.Add(PropertyName.rotation);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var vallocalRotation = (BanterVector4)values[i];
                    if (vallocalRotation.n == PropertyName.localRotation)
                    {
                        if (lerpRotation)
                        {
                            tempRotation = new Quaternion(vallocalRotation.x, vallocalRotation.y, vallocalRotation.z, vallocalRotation.w);
                        }
                        else
                        {
                            componentType.localRotation = new Quaternion(vallocalRotation.x, vallocalRotation.y, vallocalRotation.z, vallocalRotation.w);
                        }
                        changedProperties.Add(PropertyName.localRotation);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var vallocalScale = (BanterVector3)values[i];
                    if (vallocalScale.n == PropertyName.localScale)
                    {
                        componentType.localScale = new Vector3(vallocalScale.x, vallocalScale.y, vallocalScale.z);
                        changedProperties.Add(PropertyName.localScale);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valeulerAngles = (BanterVector3)values[i];
                    if (valeulerAngles.n == PropertyName.eulerAngles)
                    {
                        componentType.eulerAngles = new Vector3(valeulerAngles.x, valeulerAngles.y, valeulerAngles.z);
                        changedProperties.Add(PropertyName.eulerAngles);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var vallocalEulerAngles = (BanterVector3)values[i];
                    if (vallocalEulerAngles.n == PropertyName.localEulerAngles)
                    {
                        componentType.localEulerAngles = new Vector3(vallocalEulerAngles.x, vallocalEulerAngles.y, vallocalEulerAngles.z);
                        changedProperties.Add(PropertyName.localEulerAngles);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valup = (BanterVector3)values[i];
                    if (valup.n == PropertyName.up)
                    {
                        componentType.up = new Vector3(valup.x, valup.y, valup.z);
                        changedProperties.Add(PropertyName.up);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valforward = (BanterVector3)values[i];
                    if (valforward.n == PropertyName.forward)
                    {
                        componentType.forward = new Vector3(valforward.x, valforward.y, valforward.z);
                        changedProperties.Add(PropertyName.forward);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valright = (BanterVector3)values[i];
                    if (valright.n == PropertyName.right)
                    {
                        componentType.right = new Vector3(valright.x, valright.y, valright.z);
                        changedProperties.Add(PropertyName.right);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var vallerpPosition = (BanterBool)values[i];
                    if (vallerpPosition.n == PropertyName.lerpPosition)
                    {
                        lerpPosition = vallerpPosition.x;
                        changedProperties.Add(PropertyName.lerpPosition);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var vallerpRotation = (BanterBool)values[i];
                    if (vallerpRotation.n == PropertyName.lerpRotation)
                    {
                        lerpRotation = vallerpRotation.x;
                        changedProperties.Add(PropertyName.lerpRotation);
                    }
                }
            }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if ((sync_position && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.position,
                    type = PropertyType.Vector3,
                    value = componentType.position,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if ((sync_localPosition && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.localPosition,
                    type = PropertyType.Vector3,
                    value = componentType.localPosition,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if ((sync_rotation && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.rotation,
                    type = PropertyType.Quaternion,
                    value = componentType.rotation,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if ((sync_localRotation && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.localRotation,
                    type = PropertyType.Quaternion,
                    value = componentType.localRotation,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if ((sync_localScale && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.localScale,
                    type = PropertyType.Vector3,
                    value = componentType.localScale,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if ((sync_eulerAngles && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.eulerAngles,
                    type = PropertyType.Vector3,
                    value = componentType.eulerAngles,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if ((sync_localEulerAngles && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.localEulerAngles,
                    type = PropertyType.Vector3,
                    value = componentType.localEulerAngles,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if ((sync_up && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.up,
                    type = PropertyType.Vector3,
                    value = componentType.up,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if ((sync_forward && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.forward,
                    type = PropertyType.Vector3,
                    value = componentType.forward,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if ((sync_right && transform.hasChanged) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.right,
                    type = PropertyType.Vector3,
                    value = componentType.right,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.lerpPosition,
                    type = PropertyType.Bool,
                    value = lerpPosition,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.lerpRotation,
                    type = PropertyType.Bool,
                    value = lerpRotation,
                    componentType = ComponentType.Transform,
                    oid = oid,
                    cid = cid
                });
            }
            transform.hasChanged = false;
            scene.SetFromUnityProperties(updates, callback);
        }

        void Tick(object sender, EventArgs e) { SyncProperties(); }

        internal override void WatchProperties(PropertyName[] properties)
        {
            sync_position = false;
            sync_localPosition = false;
            sync_rotation = false;
            sync_localRotation = false;
            sync_localScale = false;
            sync_eulerAngles = false;
            sync_localEulerAngles = false;
            sync_up = false;
            sync_forward = false;
            sync_right = false;
            for (int i = 0; i < properties.Length; i++)
            {
                if (properties[i] == PropertyName.position)
                {
                    sync_position = true;
                }
                if (properties[i] == PropertyName.localPosition)
                {
                    sync_localPosition = true;
                }
                if (properties[i] == PropertyName.rotation)
                {
                    sync_rotation = true;
                }
                if (properties[i] == PropertyName.localRotation)
                {
                    sync_localRotation = true;
                }
                if (properties[i] == PropertyName.localScale)
                {
                    sync_localScale = true;
                }
                if (properties[i] == PropertyName.eulerAngles)
                {
                    sync_eulerAngles = true;
                }
                if (properties[i] == PropertyName.localEulerAngles)
                {
                    sync_localEulerAngles = true;
                }
                if (properties[i] == PropertyName.up)
                {
                    sync_up = true;
                }
                if (properties[i] == PropertyName.forward)
                {
                    sync_forward = true;
                }
                if (properties[i] == PropertyName.right)
                {
                    sync_right = true;
                }
            }
        }
        // END BANTER COMPILED CODE 
    }
}