using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace Banter.SDK
{
    /* 
    #### Banter Mirror
    Add a mirror to the object.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("MyMirror");
        const mirror = await gameObject.AddComponent(new BS.BanterMirror());
    ```
    */
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterMirror : BanterComponentBase
    {
        [See(initial = "1024")][SerializeField] internal int renderTextureSize = 1024;
        [See(initial = "1")][SerializeField] internal int cameraClear = 1;
        [See(initial = "'#000000'")][SerializeField] internal string backgroundColor = "#000000";

        VRPortalRenderer _renderer;

        [Method]
        public void _SetCullingLayer(int layer)
        {
            _renderer?.SetCullingLayer(layer);
        }

        [Method]
        public void _AddCullingLayer(int layer)
        {
            _renderer?.AddCullingLayer(layer);
        }

        internal override void StartStuff()
        {
            SetupMirror();
        }
        internal override void DestroyStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupMirror(changedProperties);
        }

        void SetupMirror(List<PropertyName> changedProperties = null)
        {
            if (_renderer == null)
            {
                _renderer = gameObject.GetComponentInChildren<VRPortalRenderer>();
            }
            if (_renderer == null)
            {
                var obj = Instantiate(Resources.Load<GameObject>("Prefabs/BanterMirror3D"));
                obj.transform.SetParent(transform, false);
                _renderer = gameObject.GetComponentInChildren<VRPortalRenderer>();
            }
            if (changedProperties?.Contains(PropertyName.renderTextureSize) ?? true)
            {
                _renderer.SetRenderTextureSize(renderTextureSize);
            }
            if (changedProperties?.Contains(PropertyName.cameraClear) ?? true)
            {
                _renderer.SetCameraClear(cameraClear);
            }
            if (changedProperties?.Contains(PropertyName.backgroundColor) ?? true)
            {
                _renderer.SetCameraColor(backgroundColor);
            }
            SetLoadedIfNot();
        }
        // BANTER COMPILED CODE 
        public System.Int32 _renderTextureSize { get { return renderTextureSize; } set { renderTextureSize = value; UpdateCallback(new List<PropertyName> { PropertyName.renderTextureSize }); } }
        public System.Int32 _cameraClear { get { return cameraClear; } set { cameraClear = value; UpdateCallback(new List<PropertyName> { PropertyName.cameraClear }); } }
        public System.String _backgroundColor { get { return backgroundColor; } set { backgroundColor = value; UpdateCallback(new List<PropertyName> { PropertyName.backgroundColor }); } }

        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.renderTextureSize, PropertyName.cameraClear, PropertyName.backgroundColor, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterMirror);


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

            DestroyStuff();
        }

        void SetCullingLayer(Int32 layer)
        {
            _SetCullingLayer(layer);
        }
        void AddCullingLayer(Int32 layer)
        {
            _AddCullingLayer(layer);
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "SetCullingLayer" && parameters.Count == 1 && parameters[0] is Int32)
            {
                var layer = (Int32)parameters[0];
                SetCullingLayer(layer);
                return null;
            }
            else if (methodName == "AddCullingLayer" && parameters.Count == 1 && parameters[0] is Int32)
            {
                var layer = (Int32)parameters[0];
                AddCullingLayer(layer);
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
                if (values[i] is BanterInt)
                {
                    var valrenderTextureSize = (BanterInt)values[i];
                    if (valrenderTextureSize.n == PropertyName.renderTextureSize)
                    {
                        renderTextureSize = valrenderTextureSize.x;
                        changedProperties.Add(PropertyName.renderTextureSize);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valcameraClear = (BanterInt)values[i];
                    if (valcameraClear.n == PropertyName.cameraClear)
                    {
                        cameraClear = valcameraClear.x;
                        changedProperties.Add(PropertyName.cameraClear);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valbackgroundColor = (BanterString)values[i];
                    if (valbackgroundColor.n == PropertyName.backgroundColor)
                    {
                        backgroundColor = valbackgroundColor.x;
                        changedProperties.Add(PropertyName.backgroundColor);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.renderTextureSize,
                    type = PropertyType.Int,
                    value = renderTextureSize,
                    componentType = ComponentType.BanterMirror,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.cameraClear,
                    type = PropertyType.Int,
                    value = cameraClear,
                    componentType = ComponentType.BanterMirror,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.backgroundColor,
                    type = PropertyType.String,
                    value = backgroundColor,
                    componentType = ComponentType.BanterMirror,
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