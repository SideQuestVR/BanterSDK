using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Banter.SDK
{

    /* 
    #### Banter Avatar Pedestal
    Add an avatar pedestal to the scene, with a User ID and User Avatar ID.

    **Properties**
    - `userId` - The ID of the user the avatar belongs to.
    - `userAvatarId` - The ID of the avatar "slot".
    - `authorId` - The ID of the avatar's author.

    **Code Example**
    
    */
    [DefaultExecutionOrder(-1)]
    [WatchComponent]
    [RequireComponent(typeof(BanterObjectId))]

    public class BanterAvatarPedestal : BanterComponentBase
    {

        [Tooltip("The ID of the avatar")]
        [See(initial = "0")][SerializeField] internal long avatarId;

#if BASIS_BUNDLE_MANAGEMENT
        BasisLoadableBundle _loadableBundle;
#endif
        private bool _loadStarted;

        internal override void StartStuff()
        {
            LoadAvatar();
        }
        void KillAvatar(GameObject go)
        {
            foreach (Transform child in go.transform.GetComponentsInChildren<Transform>())
            {
                if (child != null && child.gameObject != null && child.gameObject != gameObject)
                {
                    var renderer = child.GetComponent<Renderer>();
                    if (renderer)
                    {
                        if (renderer.sharedMaterial?.HasTexture("_MainTex")??false) {
                            var _MainTex = renderer.sharedMaterial.mainTexture;
                            if (_MainTex != null)
                            {
                                DestroyImmediate(_MainTex, true);
                            } 
                        }
                        DestroyImmediate(renderer.sharedMaterial, true);
                        renderer.sharedMaterial = null;
                    }
                    var filter = child.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        DestroyImmediate(filter.sharedMesh, true);
                        filter.sharedMesh = null;
                    }
                }
            }
        }
        async void LoadAvatar()
        {
            if (_loadStarted)
            {
                return;
            }
            _loaded = false;
            _loadStarted = true;
            try
            {
                GameObject go = null;
                
                try
                {
                    if (transform.childCount > 0)
                    {
                        //KillAvatar(transform.GetChild(0).gameObject);
                        Destroy(transform.GetChild(0).gameObject);
                    }
                    
                    SqAvatar a = await Get.AvatarDetails(avatarId);
                    if (a == null)
                    {
                        LogLine.Do("Invalid uavatarId");
                        return;
                    }
                    _loadableBundle = new BasisLoadableBundle();
                    _loadableBundle.UnlockPassword = a.author_users_id + "42069";
                    _loadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation = $"https://cdn.sidetestvr.com/file/{a.high_avatar_files_id}/high.bee";
                    CancellationToken cancellationToken = new CancellationToken();
#if BASIS_BUNDLE_MANAGEMENT
                    BasisProgressReport BeeProgressReport = new BasisProgressReport();
                    BundledContentHolder.Selector PoliceMode = BundledContentHolder.Selector.Avatar;
                    go = await BasisLoadHandler.LoadGameObjectBundle(_loadableBundle, false, BeeProgressReport, cancellationToken, transform.position, transform.rotation, Vector3.one, false, PoliceMode, transform, false);
#endif
                    var comp = this;
                    if (comp == null || gameObject == null)
                    {
                        LogLine.Do("GameObject/Component was destroyed before avatar was loaded, killing the avatar.");
                        //KillAvatar(go);
                        Destroy(go);
                        return;
                    }
                   
                    SetLoadedIfNot();
                    _loadStarted = false;
                }
                catch (Exception e)
                {
                    SetLoadedIfNot(false, e.Message);
                    Destroy(go);
                    _loadStarted = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                SetLoadedIfNot(false, e.Message);
                _loadStarted = false;
            }
        }

        public void CloneAvatar()
        {
            BanterScene.Instance().data.CloneAvatar?.Invoke(avatarId);
        }
        
        internal override void DestroyStuff()
        {
            //KillAvatar(gameObject);
        }


        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            LoadAvatar();
        }
        // BANTER COMPILED CODE 
        public System.Int64 AvatarId { get { return avatarId; } set { avatarId = value; UpdateCallback(new List<PropertyName> { PropertyName.avatarId }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.avatarId, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterAvatarPedestal);


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
                    var valavatarId = (BanterInt)values[i];
                    if (valavatarId.n == PropertyName.avatarId)
                    {
                        avatarId = valavatarId.x;
                        changedProperties.Add(PropertyName.avatarId);
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
                    name = PropertyName.avatarId,
                    type = PropertyType.Int,
                    value = avatarId,
                    componentType = ComponentType.BanterAvatarPedestal,
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