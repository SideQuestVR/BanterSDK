using UnityEngine;

namespace BS
{
    /*
     * Authoring-time description of a screen that shows a live feed: "draw feed X on this
     * renderer". It does nothing by itself. The client discovers these when a space loads and
     * puts the feed's texture into textureProperty of the chosen material slot through a
     * MaterialPropertyBlock, so the material asset is never modified and every monitor showing a
     * feed shares one texture.
     *
     * feedId names a BSCameraFeed, or one of the two newsroom buses: "program" (what is on air /
     * being recorded) and "preview" (what is lined up to be cut to next). A monitor whose feed is
     * missing keeps its material's own texture.
     *
     * Deliberately NOT a BSComponentBase / [WatchComponent] (see BSCameraFeed). Field names are
     * serialized into published worlds — never rename them.
     */
    [AddComponentMenu("BS/Feed Monitor")]
    public class BSFeedMonitor : MonoBehaviour
    {
        /// <summary>Client hook: raised as each one is enabled, so the client takes up monitors as they arrive instead
        /// of searching the scene for them.</summary>
        public static event System.Action<BSFeedMonitor> Enabled;

        void OnEnable() => Enabled?.Invoke(this);

        [Tooltip("The BSCameraFeed id to show, or \"program\" / \"preview\" for the newsroom buses.")]
        public string feedId = "program";

        [Tooltip("The renderer to draw the feed on. Defaults to the Renderer on this GameObject.")]
        public Renderer targetRenderer;

        [Tooltip("Which of the renderer's materials receives the feed.")]
        [Min(0)] public int materialIndex;

        [Tooltip("The material's texture property to set: _BaseMap for URP Lit/Unlit, _MainTex for most others.")]
        public string textureProperty = "_BaseMap";

        void Reset()
        {
            ApplyDefaults();
        }

        void OnValidate()
        {
            ApplyDefaults();
        }

        void ApplyDefaults()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            if (string.IsNullOrEmpty(textureProperty)) textureProperty = "_BaseMap";
        }
    }
}
