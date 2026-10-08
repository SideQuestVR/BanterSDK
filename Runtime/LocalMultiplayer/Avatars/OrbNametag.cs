// <mirror source="Assets/Systems/Avatar/Nametags/RemoteNametag.cs" sha256="0f0538ea918abad9e64d46d08120144c9b84b582d0344a46c109cbd9bdbdc7a2" mode="port" />
// <mirror source="Assets/Systems/Avatar/Nametags/NametagService.cs" sha256="ed4b08022c5ca1479d56c7f67df3a22003d46161aec781efba4c0d6777766199" mode="port" />
// RemoteNametag's placement and visibility (:56-88) with the "Always" display mode, hidden while the space turns
// nametags off (NametagService.SceneAllowsNametags, :372-379), at NametagService's size and height (:31, :34) and
// parented under the player for its lifetime (:179-182). The baked pill texture is replaced by a
// world-space uGUI label (the Creator SDK has no NametagGenerator, and TextMeshPro isn't referenced), built like
// PacketParty's PeerAvatar label with the built-in LegacyRuntime font.
using UnityEngine;
using UnityEngine.UI;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// A remote player's name above their orb: 2.1 m over the player root, kept upright and turned to face the
    /// camera, and shown only while the player's body is (once it has been placed and posed).
    /// </summary>
    [AddComponentMenu("")]
    public sealed class OrbNametag : MonoBehaviour
    {
        /// <summary>Height above the player root, in metres (NametagService._height).</summary>
        public const float Height = 2.1f;
        /// <summary>The label's world size, in metres (NametagService._worldSize).</summary>
        public static readonly Vector2 WorldSize = new Vector2(1.125f, 0.28125f);

        // 400 canvas units per metre: the 1.125 m x 0.28125 m label is 450 x 112.5 units.
        const float UnitsPerMetre = 400f;

        [System.NonSerialized] bool _live;
        Transform _anchor;   // the player root; the tag sits Height above it
        Canvas _canvas;
        RemoteOrbAvatar _view;
        Transform _cam;

        /// <summary>The label's text.</summary>
        public string Text { get; private set; }

        /// <summary>Builds the label as a child of <paramref name="anchor"/>, hidden until the body shows.</summary>
        internal static OrbNametag Create(Transform anchor, string text, Color tint, RemoteOrbAvatar view)
        {
            var go = new GameObject("Nametag");
            go.transform.SetParent(anchor, false); // parent for lifecycle; pose is world-driven
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = WorldSize * UnitsPerMetre;
            rect.localScale = Vector3.one * (1f / UnitsPerMetre);
            rect.localPosition = Vector3.up * Height;

            var background = AddImage(go.transform, "Background", new Color(0f, 0f, 0f, 0.55f));
            Stretch(background.rectTransform, 0f, 0f);
            // A bar in the player's colour down the left edge.
            var bar = AddImage(go.transform, "Tint", new Color(tint.r, tint.g, tint.b, 1f));
            bar.rectTransform.anchorMin = new Vector2(0f, 0f);
            bar.rectTransform.anchorMax = new Vector2(0f, 1f);
            bar.rectTransform.pivot = new Vector2(0f, 0.5f);
            bar.rectTransform.sizeDelta = new Vector2(12f, 0f);
            bar.rectTransform.anchoredPosition = Vector2.zero;

            var labelGo = new GameObject("Name", typeof(RectTransform));
            labelGo.layer = go.layer;
            labelGo.transform.SetParent(go.transform, false);
            var label = labelGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text;
            label.fontSize = 56;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 20;
            label.resizeTextMaxSize = 64;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.color = Color.white;
            label.raycastTarget = false;
            Stretch(label.rectTransform, 24f, 8f);

            var tag = go.AddComponent<OrbNametag>();
            tag._anchor = anchor;
            tag._canvas = canvas;
            tag._view = view;
            tag.Text = text;
            canvas.enabled = false;
            tag._live = true;
            return tag;
        }

        /// <summary>Hides the label and stops following the player (the player is gone).</summary>
        internal void Shutdown()
        {
            _live = false;
            if (_canvas != null) _canvas.enabled = false;
        }

        static Image AddImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void Stretch(RectTransform rect, float horizontalInset, float verticalInset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(horizontalInset, verticalInset);
            rect.offsetMax = new Vector2(-horizontalInset, -verticalInset);
        }

        void LateUpdate()
        {
            if (!_live) return;

            // Hide until the player's body is on screen (placed and posed), and while the space hides nametags
            // (SceneSettings.EnableNametags, as NametagService.SceneAllowsNametags).
            if (_canvas != null)
            {
                var scene = BSScene.Current;
                bool sceneAllows = scene == null || scene.settings == null || scene.settings.EnableNametags;
                bool visible = sceneAllows && (_view == null || _view.IsBodyVisible);
                if (_canvas.enabled != visible) _canvas.enabled = visible;
            }

            if (_anchor == null) return;
            if (_cam == null)
            {
                var main = Camera.main;
                if (main == null) return;
                _cam = main.transform;
            }

            // Drive world pose directly (not via parenting) so the avatar's own rotation doesn't tilt
            // the tag; keep it upright and facing the camera.
            transform.position = _anchor.position + Vector3.up * Height;
            Vector3 toCam = transform.position - _cam.position;
            toCam.y = 0f; // stay level; only yaw toward the viewer
            if (toCam.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
        }
    }
}
