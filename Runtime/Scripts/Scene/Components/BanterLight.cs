using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    /*
    #### Banter Light
    A light component that can be configured as Point, Directional, or Spot light.

    **Properties**
    - `lightType` - The type of light (0 = Point, 1 = Directional, 2 = Spot).
    - `color` - The color of the light.
    - `intensity` - The brightness of the light.
    - `range` - How far the light reaches (Point and Spot only).
    - `spotAngle` - The angle of the light cone in degrees (Spot only).
    - `innerSpotAngle` - The inner angle of the light cone in degrees (Spot only).
    - `shadows` - The type of shadows to cast (0 = None, 1 = Hard, 2 = Soft).

    **Code Example**
    ```js
        // Point Light
        const pointLight = new BS.GameObject("MyPointLight");
        await pointLight.AddComponent(new BS.BanterLight(0, new BS.Color(1, 1, 0, 1), 2, 10, 30, 21.8, 0));

        // Directional Light
        const dirLight = new BS.GameObject("MyDirectionalLight");
        await dirLight.AddComponent(new BS.BanterLight(1, new BS.Color(1, 1, 1, 1), 1, 10, 30, 21.8, 0));

        // Spot Light
        const spotLight = new BS.GameObject("MySpotLight");
        await spotLight.AddComponent(new BS.BanterLight(2, new BS.Color(0, 1, 1, 1), 3, 15, 45, 30, 0));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(Light))]
    [RequireComponent(typeof(Light))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterLight : UnityComponentBase
    {
        [Tooltip("The type of light (0 = Point, 1 = Directional, 2 = Spot).")]
        [See(initial = "0")][SerializeField] internal LightType type = 0;

        [Tooltip("The color of the light.")]
        [See(initial = "1,1,1,1")][SerializeField] internal Vector4 color = new Vector4(1,1,1,1);

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
        // BANTER COMPILED CODE 
        public UnityEngine.LightType Type { get { return type; } set { type = value; } }
        public UnityEngine.Vector4 Color { get { return color; } set { color = value; } }
        public System.Single Intensity { get { return intensity; } set { intensity = value; } }
        public System.Single Range { get { return range; } set { range = value; } }
        public System.Single SpotAngle { get { return spotAngle; } set { spotAngle = value; } }
        public System.Single InnerSpotAngle { get { return innerSpotAngle; } set { innerSpotAngle = value; } }
        public UnityEngine.LightShadows Shadows { get { return shadows; } set { shadows = value; } }
        public Light _componentType;
        public Light componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = GetComponent<Light>();
                }
                return _componentType;
            }
        }
        BanterScene _scene;
        public BanterScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BanterScene.Instance();
                }
                return _scene;
            }
        }
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
            if (alreadyStarted) { return; }
            alreadyStarted = true;



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

            Destroy(componentType);
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
                if (values[i] is BanterInt)
                {
                    var valtype = (BanterInt)values[i];
                    if (valtype.n == PropertyName.type)
                    {
                        componentType.type = (LightType)valtype.x;
                        changedProperties.Add(PropertyName.type);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valcolor = (BanterVector4)values[i];
                    if (valcolor.n == PropertyName.color)
                    {
                        componentType.color = new Vector4(valcolor.x, valcolor.y, valcolor.z, valcolor.w);
                        changedProperties.Add(PropertyName.color);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valintensity = (BanterFloat)values[i];
                    if (valintensity.n == PropertyName.intensity)
                    {
                        componentType.intensity = valintensity.x;
                        changedProperties.Add(PropertyName.intensity);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valrange = (BanterFloat)values[i];
                    if (valrange.n == PropertyName.range)
                    {
                        componentType.range = valrange.x;
                        changedProperties.Add(PropertyName.range);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valspotAngle = (BanterFloat)values[i];
                    if (valspotAngle.n == PropertyName.spotAngle)
                    {
                        componentType.spotAngle = valspotAngle.x;
                        changedProperties.Add(PropertyName.spotAngle);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valinnerSpotAngle = (BanterFloat)values[i];
                    if (valinnerSpotAngle.n == PropertyName.innerSpotAngle)
                    {
                        componentType.innerSpotAngle = valinnerSpotAngle.x;
                        changedProperties.Add(PropertyName.innerSpotAngle);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valshadows = (BanterInt)values[i];
                    if (valshadows.n == PropertyName.shadows)
                    {
                        componentType.shadows = (LightShadows)valshadows.x;
                        changedProperties.Add(PropertyName.shadows);
                    }
                }
            }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.type,
                    type = PropertyType.Int,
                    value = componentType.type,
                    componentType = ComponentType.Light,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.color,
                    type = PropertyType.Vector4,
                    value = componentType.color,
                    componentType = ComponentType.Light,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.intensity,
                    type = PropertyType.Float,
                    value = componentType.intensity,
                    componentType = ComponentType.Light,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.range,
                    type = PropertyType.Float,
                    value = componentType.range,
                    componentType = ComponentType.Light,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.spotAngle,
                    type = PropertyType.Float,
                    value = componentType.spotAngle,
                    componentType = ComponentType.Light,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.innerSpotAngle,
                    type = PropertyType.Float,
                    value = componentType.innerSpotAngle,
                    componentType = ComponentType.Light,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.shadows,
                    type = PropertyType.Int,
                    value = componentType.shadows,
                    componentType = ComponentType.Light,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}