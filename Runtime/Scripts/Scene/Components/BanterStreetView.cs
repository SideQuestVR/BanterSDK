using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace Banter.SDK
{

    /* 
    #### Banter StreetView
    This component will add a streetview dome to the object and set the panoId of the streetview.

    **Properties**
     - `panoId` - The panoId of the streetview.

    **Code Example**
    ```js
        const panoId = "CAoSLEFGM";
        const gameObject = new BS.GameObject("MyStreetView");
        const streetView = await gameObject.AddComponent(new BS.BanterStreetView(panoId));
    ```
    */
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterStreetView : BanterComponentBase
    {
        [Tooltip("The panoId of the Street View location to be displayed.")]
        [See(initial = "")][SerializeField] internal string panoId = "";

        PhotoSphere photoSphere;
        internal override void DestroyStuff() { }
        internal override void StartStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (photoSphere != null)
            {
                Destroy(photoSphere);
            }
            var obj = Instantiate(Resources.Load<GameObject>("StreetViewPrefab"), transform, false);
            photoSphere = obj.GetComponent<PhotoSphere>();
            photoSphere.Panoid = panoId;
            Action photoSphereCallback = null;
            photoSphereCallback = () =>
            {
                photoSphere.LoadCallback -= photoSphereCallback;
                SetLoadedIfNot();
            };
            photoSphere.LoadCallback += photoSphereCallback;
        }
        // BANTER COMPILED CODE 
        public System.String PanoId { get { return panoId; } set { panoId = value; UpdateCallback(new List<PropertyName> { PropertyName.panoId }); } }

        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.panoId, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterStreetView);


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
                if (values[i] is BanterString)
                {
                    var valpanoId = (BanterString)values[i];
                    if (valpanoId.n == PropertyName.panoId)
                    {
                        panoId = valpanoId.x;
                        changedProperties.Add(PropertyName.panoId);
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
                    name = PropertyName.panoId,
                    type = PropertyType.String,
                    value = panoId,
                    componentType = ComponentType.BanterStreetView,
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