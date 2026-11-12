using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSPortal : BSComponentBase
    {

        public static string[] defaultPortalLandscapes = new string[]{
            "https://cdn.sidequestvr.com/file/567294/portalplaceholder_1.png",
            "https://cdn.sidequestvr.com/file/567295/portalplaceholder_2.png",
            "https://cdn.sidequestvr.com/file/567296/portalplaceholder_3.png",
            "https://cdn.sidequestvr.com/file/567297/portalplaceholder_4.png",
            "https://cdn.sidequestvr.com/file/567299/portalplaceholder_5.png",
            "https://cdn.sidequestvr.com/file/567300/portalplaceholder_6.png",
            "https://cdn.sidequestvr.com/file/567302/portalplaceholder_7.png",
            "https://cdn.sidequestvr.com/file/567303/portalplaceholder_8.png",
            "https://cdn.sidequestvr.com/file/567304/portalplaceholder_9.png",
            "https://cdn.sidequestvr.com/file/567305/portalplaceholder_10.png",
            "https://cdn.sidequestvr.com/file/567306/portalplaceholder_11.png",
            "https://cdn.sidequestvr.com/file/567307/portalplaceholder_12.png",
        };
        [Tooltip("The URL of the space to link to.")]
        [See(initial = "")][SerializeField] internal string url = "";

        [Tooltip("The instance ID of the space to link to.")]
        [See(initial = "")][SerializeField] internal string instance = "";

        MaterialPropertyBlock block;
        GameObject portal;
        BSSceneEvents sceneEvents;
        internal override void StartStuff()
        {
            sceneEvents = BSScene.Instance().events;
            block = new MaterialPropertyBlock();
            _ = SetupPortal();
        }

        public static async Task SetTexture(string texture, MeshRenderer rend)
        {
            if (PageUrl.Refuse(texture, nameof(BSPortal))) return;
            var tex = await Get.Texture(texture);
            if (tex != null && rend != null)
            {
                rend.material.SetTexture("_MainTex", MipMaps.Do(tex));
            }
        }

        private async Task SetupPortal()
        {
            if (portal != null)
            {
                Destroy(portal);
            }
            var mine = portal = Instantiate(Resources.Load<GameObject>("Prefabs/BSPortal"), transform);
            portal.name = "BSPortal";
            var rend = portal.transform.Find("PortalMesh").GetComponent<MeshRenderer>();
            var title = portal.transform.Find("Info/Titel").GetComponent<TMPro.TextMeshPro>();
            title.text = ""; // The prefab's placeholder text must not show while the metadata loads.
            portal.GetComponentInChildren<Portal>().url = url;
            var space = await Get.SpaceMeta(url);
            var defaultTex = defaultPortalLandscapes[UnityEngine.Random.Range(0, defaultPortalLandscapes.Length)];
            SetLoadedIfNot();
            // A newer run replaced this portal while the metadata loaded, or the component went away.
            if (portal != mine) return;
            // An unknown space can come back as an empty object rather than nothing.
            title.text = string.IsNullOrEmpty(space?.name) ? "Unknown Space" : space.name;
#if UNITY_ANDROID
            const int imageSize = 512;
#else
            const int imageSize = 1024;
#endif
            try
            {
                await SetTexture(Sized(string.IsNullOrEmpty(space?.icon) ? defaultTex : space.icon, imageSize), rend);
            }
            catch (Exception e)
            {
                Debug.Log(e);
                try { await SetTexture(defaultTex, rend); }
                catch (Exception fallback) { Debug.Log(fallback); }
            }
        }

        /// <summary>The url with the SideQuest CDN's resize parameter. Only that CDN understands it, so other hosts get the url as it is.</summary>
        internal static string Sized(string url, int size)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
            if (!uri.Host.Equals("cdn.sidequestvr.com", StringComparison.OrdinalIgnoreCase)) return url;
            if (uri.Query.IndexOf("size=", StringComparison.OrdinalIgnoreCase) >= 0) return url;
            return url + (string.IsNullOrEmpty(uri.Query) ? "?size=" : "&size=") + size;
        }


        internal override void UpdateStuff()
        {

        }
        internal override void DestroyStuff()
        {
            if (portal != null)
            {
                Destroy(portal);
                portal = null;
            }
        }
        internal void UpdateCallback(List<PropertyName> changedProperties = null)
        {
            _ = SetupPortal();
        }
    }
}