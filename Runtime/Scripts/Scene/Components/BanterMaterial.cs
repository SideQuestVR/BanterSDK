using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Banter.SDK
{
    public enum ShaderType
    {
        Mobile,
        Custom
    }
    public enum MaterialSide
    {
        Front,
        Back,
        Double
    }
    /* 
    #### Banter Material
    Add a material to the object. This component will add a material to the object and set the shader, texture, color and side of the material.

    **Properties**
     - `texture` - The texture to use for the material.
     - `color` - The color of the material.
     - `shaderName` - The name of the shader to use.
     - `side` - The side of the material to render.
     - `generateMipMaps` - Whether to generate mipmaps for the texture.

    **Code Example**
    ```js
        const texture = "https://cdn.glitch.global/7bdd46d4-73c4-47a1-b156-10440ceb99fb/GridBox_Default.png?v=1708022523716";
        const color = new BS.Vector4(1,1,1,1);
        const shaderName = "Unlit/Diffuse";
        const side = BS.MaterialSide.Front;
        const generateMipMaps = false;

        const gameObject = new BS.GameObject("MyMaterial"); 
        const material = await gameObject.AddComponent(new BS.BanterMaterial(shaderName, texture, color, side, generateMipMaps));

    ```

    */
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterMaterial : BanterComponentBase
    {
        public ShaderType shaderType = ShaderType.Custom;
        [See] public string shaderName = "Unlit/Diffuse";
        MeshRenderer _renderer;
        [See] public string texture = "";// "https://cdn.glitch.global/7bdd46d4-73c4-47a1-b156-10440ceb99fb/GridBox_Default.png?v=1708022523716";
        [See] public Vector4 color;
        [See] public MaterialSide side;
        [See(initial = "false")] public bool generateMipMaps = false;
        Texture2D defaultTexture;
        Texture2D mainTex;
        public override void StartStuff()
        {
            _ = SetupMaterial();
        }
        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            _ = SetupMaterial(changedProperties);
        }
        async Task SetupMaterial(List<PropertyName> changedProperties = null)
        {
            if (defaultTexture == null)
            {
                defaultTexture = Resources.Load<Texture2D>("Images/GridBox_Default");
            }
            SetLoadedIfNot();
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<MeshRenderer>();
            }
            if (changedProperties?.Contains(PropertyName.shaderName) ?? false)
            {
                var material = new Material(shaderType == ShaderType.Custom ? Shader.Find(shaderName) : Shader.Find("Unlit/Diffuse"));
                _renderer.sharedMaterial = material;
            }
            if (changedProperties?.Contains(PropertyName.texture) ?? false)
            {
                await SetTexture(texture);
            }
            if (changedProperties?.Contains(PropertyName.color) ?? false)
            {
                SetColor(new Color(color.x, color.y, color.z, color.w));
            }
            if (changedProperties?.Contains(PropertyName.side) ?? false)
            {
                _renderer.sharedMaterial.SetFloat("_Cull", 2 - (int)side);
            }
        }

        public void SetColor(Color color)
        {
            _renderer.sharedMaterial.SetColor("_Color", color);
        }

        public async Task SetTexture(string texture)
        {
            try
            {
                if (!scene.settings.EnableDefaultTextures && string.IsNullOrEmpty(texture))
                {
                    return;
                }
                mainTex = defaultTexture;
                if (!string.IsNullOrEmpty(texture))
                {
                    mainTex = generateMipMaps ? MipMaps.Do(await Get.Texture(texture)) : await Get.Texture(texture);
                }
                if (_renderer != null)
                {
                    _renderer.sharedMaterial.mainTexture = mainTex;
                }
            }
            catch (Exception e)
            {
                Debug.Log("Could not get texture: " + texture);
                Debug.LogError(e);
            }
        }

        public override void DestroyStuff()
        {
            if (_renderer != null)
            {
                Destroy(_renderer);
            }
        }
        // BANTER COMPILED CODE 
        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        public override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.shaderName, PropertyName.texture, PropertyName.color, PropertyName.side, PropertyName.generateMipMaps, };
            UpdateCallback(changedProperties);
        }

        public override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterMaterial);


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

        public override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        public override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterString)
                {
                    var valshaderName = (BanterString)values[i];
                    if (valshaderName.n == PropertyName.shaderName)
                    {
                        shaderName = valshaderName.x;
                        changedProperties.Add(PropertyName.shaderName);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valtexture = (BanterString)values[i];
                    if (valtexture.n == PropertyName.texture)
                    {
                        texture = valtexture.x;
                        changedProperties.Add(PropertyName.texture);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valcolor = (BanterVector4)values[i];
                    if (valcolor.n == PropertyName.color)
                    {
                        color = new Vector4(valcolor.x, valcolor.y, valcolor.z, valcolor.w);
                        changedProperties.Add(PropertyName.color);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valside = (BanterInt)values[i];
                    if (valside.n == PropertyName.side)
                    {
                        side = (MaterialSide)valside.x;
                        changedProperties.Add(PropertyName.side);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valgenerateMipMaps = (BanterBool)values[i];
                    if (valgenerateMipMaps.n == PropertyName.generateMipMaps)
                    {
                        generateMipMaps = valgenerateMipMaps.x;
                        changedProperties.Add(PropertyName.generateMipMaps);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        public override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.shaderName,
                    type = PropertyType.String,
                    value = shaderName,
                    componentType = ComponentType.BanterMaterial,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.texture,
                    type = PropertyType.String,
                    value = texture,
                    componentType = ComponentType.BanterMaterial,
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
                    value = color,
                    componentType = ComponentType.BanterMaterial,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.side,
                    type = PropertyType.Int,
                    value = side,
                    componentType = ComponentType.BanterMaterial,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.generateMipMaps,
                    type = PropertyType.Bool,
                    value = generateMipMaps,
                    componentType = ComponentType.BanterMaterial,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        public override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}