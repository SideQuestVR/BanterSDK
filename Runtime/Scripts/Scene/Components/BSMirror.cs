using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace BS
{
    /* 
    #### Banter Mirror
    Add a mirror to the object.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("MyMirror");
        const mirror = await gameObject.AddComponent(new BS.BSMirror());
    ```
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSMirror : BSComponentBase
    {
        [Tooltip("The resolution of the mirror's render texture. Higher values improve quality but use more memory.")]
        [See(initial = "1024")][SerializeField] internal int renderTextureSize = 1024;

        [Tooltip("Determines how the mirror camera clears the background. 1 = Skybox, 2 = Solid Color, 3 = Depth Only, 4 = Nothing.")]
        [See(initial = "1")][SerializeField] internal int cameraClear = 1;

        [Tooltip("The background color of the mirror when using solid color clearing (Hex format, e.g., #000000 for black).")]
        [See(initial = "'#000000'")][SerializeField] internal string backgroundColor = "#000000";


        VRPortalRenderer _renderer;
        GameObject _mirrorObject;

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

        internal override void UpdateStuff()
        {

        }
        internal override void DestroyStuff()
        {
            if (_mirrorObject != null)
            {
                Destroy(_mirrorObject);
                _mirrorObject = null;
            }
            _renderer = null;
        }
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
                _mirrorObject = Instantiate(Resources.Load<GameObject>("Prefabs/BanterMirror3D"));
                _mirrorObject.transform.SetParent(transform, false);
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
    }
}