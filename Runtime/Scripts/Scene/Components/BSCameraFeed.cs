using UnityEngine;

namespace BS
{
    /*
     * Authoring-time description of a live camera feed: "this camera is a feed called X". It does
     * nothing by itself. The client discovers these when a space loads and does the rendering:
     * the camera is taken over (disabled and rendered on a schedule into a texture the client
     * owns, at no more than maxWidth x maxHeight and maxFps, fewer when the device is busy), and
     * the result can be shown on BSFeedMonitor surfaces, cut to program and recorded.
     *
     * Author the camera as a plain Camera that does not render to the screen: never tag it
     * MainCamera, and do not rely on its targetTexture — the client assigns its own. A Cinemachine
     * program camera (CinemachineBrain + CinemachineCamera shots) belongs on a dedicated camera
     * carrying one of these, never on the player's camera.
     *
     * Deliberately NOT a BSComponentBase / [WatchComponent]: that would run the component codegen
     * and append to the frozen JS wire-ordinal registry, and nothing here is scripting-exposed.
     * Pages and graphs refer to a feed by feedId through BS.Newsroom / the "BS\Recording" units.
     * Field names are serialized into published worlds — never rename them.
     */
    [DisallowMultipleComponent]
    [AddComponentMenu("BS/Camera Feed")]
    public class BSCameraFeed : MonoBehaviour
    {
        [Tooltip("The id monitors, cuts and recordings use for this feed. Unique within the space; defaults to the GameObject's name.")]
        public string feedId;

        [Tooltip("The name shown to people choosing a feed (feed pickers such as the recording source list). Falls back to the feed id.")]
        public string displayName;

        [Tooltip("The camera this feed renders from. Defaults to the Camera on this GameObject.")]
        public Camera feedCamera;

        [Tooltip("Higher-priority feeds are kept rendering first when the device cannot afford every feed.")]
        public int priority;

        [Tooltip("Upper bound on the feed's render width in pixels. The client may render smaller (always on Quest).")]
        [Min(16)] public int maxWidth = 1280;

        [Tooltip("Upper bound on the feed's render height in pixels. The client may render smaller (always on Quest).")]
        [Min(16)] public int maxHeight = 720;

        [Tooltip("Upper bound on how many times per second the feed renders. The client may render less often.")]
        [Min(1f)] public float maxFps = 30f;

        [Tooltip("Only offer this feed on desktop clients (for rigs too expensive for standalone headsets).")]
        public bool pcOnly;

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
            if (feedCamera == null) feedCamera = GetComponent<Camera>();
            if (string.IsNullOrEmpty(feedId)) feedId = gameObject.name;
        }
    }
}
