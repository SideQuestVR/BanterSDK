using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PlasticGui;
using UnityEngine;
using Siccity.GLTFUtility;

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
        [Tooltip("The The ID of the user the avatar belongs to.")]
        [See(initial = "0")][SerializeField] internal long userId;

        [Tooltip("The ID of the avatar \"slot\".")]
        [See(initial = "0")][SerializeField] internal long userAvatarId;
        
        [Tooltip("The ID of the avatar's author.")]
        [See(initial = "0")][SerializeField] internal long authorId;

        BasisLoadableBundle _loadableBundle;
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
                    if (renderer != null)
                    {
                        if (renderer.sharedMaterial.HasTexture("_MainTex")) {
                            var _MainTex = renderer.sharedMaterial.mainTexture;
                            if (_MainTex != null)
                            {
                                DestroyImmediate(_MainTex);
                            } 
                        }
                        DestroyImmediate(renderer.sharedMaterial);
                        renderer.sharedMaterial = null;
                    }
                    var filter = child.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        DestroyImmediate(filter.sharedMesh);
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
                    UserAvatar a = await Get.UserAvatar(userId, userAvatarId);
                    if (a == null)
                    {
                        LogLine.Do("Invalid userId or avatarUserId");
                        return;
                    }
                    _loadableBundle = new BasisLoadableBundle();
                    _loadableBundle.UnlockPassword = authorId + "42069";
                    _loadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation = $"https://cdn.sidetestvr.com/file/{a.high_avatar_files_id}/high.bee";
                    CancellationToken cancellationToken = new CancellationToken();
                    BasisProgressReport BeeProgressReport = new BasisProgressReport();
                    BundledContentHolder.Selector PoliceMode = BundledContentHolder.Selector.Avatar;
                    go = await BasisLoadHandler.LoadGameObjectBundle(_loadableBundle, false, BeeProgressReport, cancellationToken, Vector3.zero, Quaternion.identity, Vector3.one, false, PoliceMode, transform, false);

                    var comp = this;
                    if (comp == null || gameObject == null)
                    {
                        LogLine.Do("GameObject/Component was destroyed before avatar was loaded, killing the avatar.");
                        KillAvatar(go);
                        Destroy(go);
                        return;
                    }
                    if (transform.childCount > 0)
                    {
                        KillAvatar(transform.GetChild(0).gameObject);
                        Destroy(transform.GetChild(0).gameObject);
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
        internal override void DestroyStuff()
        {
            KillAvatar(gameObject);
        }
        

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            LoadAvatar();
        }
        
        // BANTER COMPILED CODE 
        public System.Int64 UserId { get { return userId; } set { userId = value; UpdateCallback(new List<PropertyName> { PropertyName.userId }); } }
        public System.Int64 UserAvatarId { get { return userAvatarId; } set { userAvatarId = value; UpdateCallback(new List<PropertyName> { PropertyName.userAvatarId }); } }
        public System.Int64 AuthorId { get { return authorId; } set { authorId = value; UpdateCallback(new List<PropertyName> { PropertyName.authorId }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.userId, PropertyName.userAvatarId, PropertyName.authorId, };
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
                    var valuserId = (BanterInt)values[i];
                    if (valuserId.n == PropertyName.userId)
                    {
                        userId = valuserId.x;
                        changedProperties.Add(PropertyName.userId);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valuserAvatarId = (BanterInt)values[i];
                    if (valuserAvatarId.n == PropertyName.userAvatarId)
                    {
                        userAvatarId = valuserAvatarId.x;
                        changedProperties.Add(PropertyName.userAvatarId);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valauthorId = (BanterInt)values[i];
                    if (valauthorId.n == PropertyName.authorId)
                    {
                        authorId = valauthorId.x;
                        changedProperties.Add(PropertyName.authorId);
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
                    name = PropertyName.userId,
                    type = PropertyType.Int,
                    value = userId,
                    componentType = ComponentType.BanterAvatarPedestal,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.userAvatarId,
                    type = PropertyType.Int,
                    value = userAvatarId,
                    componentType = ComponentType.BanterAvatarPedestal,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.authorId,
                    type = PropertyType.Int,
                    value = authorId,
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