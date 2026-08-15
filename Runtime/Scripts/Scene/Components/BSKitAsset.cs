using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Instantiates a prefab from the processed low-poly library by its manifest path.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="BSKitItem"/> rather than a mode on it, because the two resolve
    /// through completely different mechanisms and only one of them is about kits in the Banter
    /// sense. BSKitItem loads out of an AssetBundle registered against the space
    /// (<c>scene.settings.KitPaths</c>), which is right for content a creator uploaded with their
    /// space. This one addresses prefabs that ship inside the build, so there is no bundle to wait
    /// for, nothing to download, and no per-space registration — and folding that into BSKitItem
    /// would mean a component whose `path` means two different things depending on state.
    ///
    /// The path is the kit manifest's own `path` field, relative to the package's Assets root:
    /// <c>CartoonCubeWorld/Prefabs/Props/Apple.prefab</c>.
    /// </remarks>
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSKitAsset : BSComponentBase
    {
        [Tooltip("Manifest path of the prefab, relative to the kit package's Assets root, e.g. CartoonCubeWorld/Prefabs/Props/Apple.prefab")]
        [See(initial = "")][SerializeField] internal string path = "";

        /// <summary>Resources root the prefabs are expected under in a build.</summary>
        const string ResourcePrefix = "Kit/";

        /// <summary>Where the prefabs live in the project, for the editor-only fallback.</summary>
        const string PackageAssetRoot = "Packages/com.sidequest.low-poly-assets-processed/Assets/";

        GameObject item;

        void SetupKitAsset()
        {
            if (item != null)
            {
                Destroy(item);
                item = null;
            }

            if (string.IsNullOrEmpty(path))
            {
                SetLoadedIfNot(false, "KitAsset has no path.");
                return;
            }

            var prefab = Resolve(path);
            if (prefab == null)
            {
                SetLoadedIfNot(false, $"KitAsset not found at path: {path}");
                return;
            }

            try
            {
                // Parented with worldPositionStays: false, so the spawned prefab sits exactly on the
                // object the editor created for it and the gizmo moves the two as one.
                item = Instantiate(prefab, transform, false);
                item.transform.localPosition = Vector3.zero;
                item.transform.localRotation = Quaternion.identity;

                scene.kitItems.Add(item);
                SetLoadedIfNot();
            }
            catch (Exception e)
            {
                SetLoadedIfNot(false, e.Message);
            }
        }

        /// <summary>
        /// Find the prefab, preferring the path that also works in a build.
        /// </summary>
        /// <remarks>
        /// Resources first, AssetDatabase second, and the order is the point: Resources is the
        /// shipping path and the fallback exists only so the editor works TODAY, before the kit
        /// pipeline has been re-rooted to emit into a Resources folder. Putting the editor-only
        /// branch first would mean everything works in the editor right up until the first build,
        /// where nothing would.
        /// </remarks>
        static GameObject Resolve(string manifestPath)
        {
            var resourcePath = ResourcePrefix + StripExtension(manifestPath);
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab != null) return prefab;

#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PackageAssetRoot + manifestPath);
            if (prefab != null) return prefab;
#endif

            return null;
        }

        static string StripExtension(string value)
        {
            var dot = value.LastIndexOf('.');
            var slash = value.LastIndexOf('/');
            return dot > slash ? value.Substring(0, dot) : value;
        }

        internal override void DestroyStuff()
        {
            if (item != null && scene.kitItems.Contains(item))
            {
                scene.kitItems.Remove(item);
            }
        }

        internal override void UpdateStuff() { }
        internal override void StartStuff() { }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupKitAsset();
        }

        // BANTER COMPILED CODE
        public System.String Path { get { return path; } set { path = value; UpdateCallback(new List<PropertyName> { PropertyName.path }); } }

        BSScene _scene;
        public BSScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BSScene.Instance();
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
            UpdateCallback(new List<PropertyName>() { PropertyName.path });
        }

        internal override string GetSignature()
        {
            return "KitAsset" + PropertyName.path + path;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.KitAsset);

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
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
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
            var changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BSString)
                {
                    var valpath = (BSString)values[i];
                    if (valpath.n == PropertyName.path)
                    {
                        path = valpath.x;
                        changedProperties.Add(PropertyName.path);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.path,
                    type = PropertyType.String,
                    value = path,
                    componentType = ComponentType.KitAsset,
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
