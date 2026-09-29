// Banter has its own copy of this bridge, wired to its pointer module (com.sidequest.greenfield.ui).
#if !GREENFIELD_PROJECT

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS
{
    /// <summary>
    /// Gives pointer input to UI Toolkit panels that are drawn onto their own mesh
    /// (<see cref="BSUIPanel.UsesMeshInput"/>), by raycasting that mesh and handing UI Toolkit the
    /// texture UV at the hit as a panel-space position.
    /// </summary>
    /// <remarks>
    /// UI Toolkit picks against a plane, so on any surface that is not a flat quad a click lands
    /// nowhere near where it appears. The UV is the one coordinate that survives an arbitrary
    /// shape: whatever the surface does in space, the texel under the pointer is the pixel the user
    /// is looking at. Feeding that back through
    /// <c>PanelSettings.SetScreenToPanelSpaceFunction</c> means the rest of the stack —
    /// PanelRaycaster, PanelEventHandler, hover, focus, drag — keeps working unchanged, because all
    /// of it consumes panel coordinates.
    ///
    /// The SDK port of the client's bridge: the ray comes from the main camera through the screen
    /// point UI Toolkit asks about, which in the SDK is the mouse. Panels self-register through
    /// <see cref="AddPanelStuff.PanelReady"/>; nothing needs adding to a scene.
    /// </remarks>
    public static class PanelMeshInputBridge
    {
        /// <summary>How far along the pointer ray to look for the panel surface.</summary>
        public static float MaxDistance = 30f;

        /// <summary>
        /// "The pointer is not on this panel." Deliberately a finite point outside the panel rather
        /// than NaN: the raycaster rejects both identically, but PanelEventHandler converts with
        /// allowOutside and does *not* reject NaN — it would land in event positions and deltas and
        /// poison the stored pointer position for every panel in the scene.
        /// </summary>
        static readonly Vector2 Invalid = new Vector2(-1f, -1f);

        static readonly Dictionary<GameObject, PanelEntry> _entries = new();
        static readonly List<PanelEntry> _watched = new();

        /// <summary>True when this GameObject is a panel taking input from its mesh's UVs.</summary>
        public static bool IsMeshPanel(GameObject host) =>
            host != null && _entries.TryGetValue(host, out var e) && e.Installed;

        /// <summary>
        /// The surface hit for a ray this frame, raycasting if it has not been resolved yet. Lets the
        /// desktop controller give an otherwise depth-less panel hit a real distance.
        /// </summary>
        public static bool TryResolveHit(GameObject host, Ray ray, out RaycastHit hit)
        {
            hit = default;
            return host != null
                && _entries.TryGetValue(host, out var entry)
                && entry.Installed
                && entry.TryResolveHit(ray, out hit);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            _entries.Clear();
            _watched.Clear();

            // Unsubscribe first: with domain reload disabled, statics survive leaving play mode and
            // this would otherwise stack a new handler on every run.
            AddPanelStuff.PanelReady -= OnPanelReady;
            AddPanelStuff.PanelReady += OnPanelReady;

            Watchdog.Ensure();
        }

        static void OnPanelReady(IPanel panel)
        {
            var host = (panel as IRuntimePanel)?.selectableGameObject;
            if (host == null) return;

            var uiPanel = host.GetComponent<BSUIPanel>();
            if (uiPanel == null || _entries.ContainsKey(host)) return;

            var entry = new PanelEntry(host, uiPanel);
            _entries[host] = entry;
            _watched.Add(entry);
        }

        /// <summary>
        /// Mesh mode can be switched on after the panel is ready, and the mesh itself arrives from
        /// JS later still, so installation is re-checked rather than done once.
        /// </summary>
        static void Tick()
        {
            for (var i = _watched.Count - 1; i >= 0; i--)
            {
                var entry = _watched[i];
                if (entry.IsDead)
                {
                    // Uninstall on the way out: the PanelSettings can outlive the panel, and a
                    // closure left on it would answer for a panel that no longer exists.
                    entry.Uninstall();
                    _entries.Remove(entry.Host);
                    _watched.RemoveAt(i);
                    continue;
                }
                entry.Sync();
            }
        }

        sealed class PanelEntry
        {
            public readonly GameObject Host;
            public bool Installed { get; private set; }
            public bool IsDead => Host == null || _panel == null;

            readonly BSUIPanel _panel;

            PanelSettings _settings;
            UIDocument _document;
            Vector2? _lastGood;

            int _sampleFrame = -1;
            Ray _sampleRay;
            bool _sampleHit;
            RaycastHit _sample;

            public PanelEntry(GameObject host, BSUIPanel panel)
            {
                Host = host;
                _panel = panel;
            }

            public void Sync()
            {
                // Not installed until the collider exists, so the mapping is never the reason a
                // panel is unclickable, and IsMeshPanel never claims a panel that cannot be hit.
                var wanted = _panel.UsesMeshInput && _panel.MeshInputCollider != null;
                if (wanted == Installed) return;

                if (!wanted)
                {
                    Uninstall();
                    return;
                }

                // BSUIPanel instantiates its own PanelSettings, so mutating this one is local to
                // this panel.
                var document = Host.GetComponent<UIDocument>();
                var settings = document != null ? document.panelSettings : null;
                if (settings == null) return;

                _document = document;
                _settings = settings;
                settings.SetScreenToPanelSpaceFunction(Resolve);
                Installed = true;
            }

            public void Uninstall()
            {
                if (!Installed) return;

                // Explicit null test, not `?.`: a destroyed UnityEngine.Object is not a real null
                // reference, so `?.` would sail past it and throw on the call.
                //
                // Cleared through the 3D overload: the 2D one has no null check and installs a
                // wrapper that throws on the next pointer event.
                if (_settings != null)
                    _settings.SetScreenToPanelSpaceFunction3D(null);

                _settings = null;
                _document = null;
                _lastGood = null;
                _sampleFrame = -1;
                Installed = false;
            }

            /// <param name="screenPosition">Screen point with a top-left origin, as UI Toolkit (and
            /// uGUI's PanelRaycaster / PanelEventHandler, which flip y before asking) pass it.</param>
            Vector2 Resolve(Vector2 screenPosition)
            {
                var meshCollider = _panel != null ? _panel.MeshInputCollider : null;
                var texture = _panel != null ? _panel.renderTexture : null;
                if (meshCollider == null || texture == null) return Invalid;

                // A locked cursor (the desktop controller flying) points at nothing.
                var camera = Camera.main;
                if (camera == null || UnityEngine.Cursor.lockState == CursorLockMode.Locked) return Invalid;

                var ray = camera.ScreenPointToRay(new Vector3(screenPosition.x, Screen.height - screenPosition.y, 0f));
                if (TryResolveHit(ray, out var hit))
                {
                    var uv = hit.textureCoord;
                    var point = ClampIntoPanel(
                        new Vector2(uv.x * texture.width, (1f - uv.y) * texture.height), texture);
                    _lastGood = point;
                    return point;
                }

                // While THIS panel holds the pointer capture UI Toolkit keeps delivering its events
                // even after the ray has left the surface. Reporting "nowhere" for those would jump
                // whatever is being dragged to the edge of the panel and back; holding the last good
                // spot keeps a drag that wanders off the mesh behaving like one that stays on it.
                // Gated on this panel actually capturing, not on the pointer merely being pressed,
                // or a press anywhere else would read as a hit on every hovered mesh panel.
                if (HoldsCapture() && _lastGood.HasValue)
                    return ClampIntoPanel(_lastGood.Value, texture);

                return Invalid;
            }

            /// <summary>
            /// UI Toolkit tests containment with Rect.Contains, which is half-open — exactly
            /// <c>width</c> counts as outside. A hit right on the mesh's seam or bottom edge lands
            /// on exactly that value, so it needs nudging inside or it reads as a miss.
            /// </summary>
            static Vector2 ClampIntoPanel(Vector2 point, RenderTexture texture)
            {
                const float edge = 0.001f;
                return new Vector2(
                    Mathf.Clamp(point.x, 0f, texture.width - edge),
                    Mathf.Clamp(point.y, 0f, texture.height - edge));
            }

            /// <summary>
            /// One physics query per ray per frame. UI Toolkit asks for this conversion many times a
            /// frame — once per raycaster pass and again for each pointer event.
            /// </summary>
            public bool TryResolveHit(Ray ray, out RaycastHit hit)
            {
                if (_sampleFrame == Time.frameCount && _sampleRay.origin == ray.origin && _sampleRay.direction == ray.direction)
                {
                    hit = _sample;
                    return _sampleHit;
                }

                var meshCollider = _panel != null ? _panel.MeshInputCollider : null;
                if (meshCollider == null)
                {
                    hit = default;
                    return false;
                }

                // Scoped to this panel's own collider: a scene-wide raycast could return any other
                // surface, whose texture coordinate would be meaningless here.
                var didHit = meshCollider.Raycast(ray, out hit, MaxDistance);

                _sampleFrame = Time.frameCount;
                _sampleRay = ray;
                _sampleHit = didHit;
                _sample = hit;
                return didHit;
            }

            /// <summary>
            /// True when a VisualElement in THIS panel currently holds the mouse pointer capture — a
            /// slider thumb, scroller or text selection that grabbed it on press.
            /// </summary>
            bool HoldsCapture()
            {
                var panel = _document != null && _document.rootVisualElement != null
                    ? _document.rootVisualElement.panel
                    : null;
                return panel != null && panel.GetCapturingElement(PointerId.mousePointerId) != null;
            }
        }

        /// <summary>Drives <see cref="Tick"/> without needing anything placed in a scene.</summary>
        sealed class Watchdog : MonoBehaviour
        {
            static Watchdog _instance;

            public static void Ensure()
            {
                if (_instance != null) return;
                // Left visible in the hierarchy on purpose — when mesh input misbehaves, the first
                // question is whether this is running at all.
                var go = new GameObject("PanelMeshInputBridge");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<Watchdog>();
            }

            void Update() => Tick();
        }
    }
}

#endif
