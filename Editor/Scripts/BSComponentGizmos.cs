using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS.SDKEditor
{
    /// <summary>
    /// Scene view outlines for the components that only build their visuals in Play mode (mirror, browser, UI panel,
    /// portal, text, glTF model), so a creator can see how big each will be and which way it faces while placing it.
    /// Editor only, like the snippet gizmos: nothing is added to the object, the scene or the build. The sizes are the
    /// ones the components create at runtime, measured from their prefabs and panel settings.
    /// </summary>
    static class BSComponentGizmos
    {
        // The snippet gizmos' colours.
        static readonly Color Outline = new Color(0f, 0.7f, 1f, 0.9f);
        static readonly Color Fill = new Color(0f, 0.7f, 1f, 0.06f);

        const GizmoType Shown = GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable;

        // BanterMirror3D: Unity's 10 m Plane at a quarter of its size, turned to face -Z.
        static readonly Vector2 MirrorSize = new Vector2(2.5f, 2.5f);

        // BSPortal: its ring is 2.15 m across, 0.8 m above the object, and turns about Y to face the player.
        const float PortalRadius = 1.074f;
        static readonly Vector3 PortalCentre = new Vector3(0f, 0.8f, 0f);

        // World-space panels are their size in pixels over the panel settings' pixels per unit.
        static float s_BrowserPixelsPerUnit;
        static float s_PanelPixelsPerUnit;

        static GUIStyle s_LabelStyle;

        [DrawGizmo(Shown)]
        static void DrawMirror(BSMirror mirror, GizmoType type)
        {
            // A creator's own VRPortalRenderer child is used instead of the prefab, and shows itself.
            if (Application.isPlaying || mirror.GetComponentInChildren<VRPortalRenderer>(true) != null)
                return;
            FlatPanel(mirror.transform, MirrorSize, type, "Mirror");
        }

        [DrawGizmo(Shown)]
        static void DrawBrowser(BSBrowser browser, GizmoType type)
        {
            if (Application.isPlaying)
                return;
            var pixelsPerUnit = PixelsPerUnit(ref s_BrowserPixelsPerUnit, "Prefabs/Browser/Panel Settings", 1300f);
            var size = new Vector2(browser.PageWidth, browser.PageHeight) / pixelsPerUnit;
            FlatPanel(browser.transform, size, type, Join("Browser", browser.Url));
        }

        [DrawGizmo(Shown)]
        static void DrawUIPanel(BSUIPanel panel, GizmoType type)
        {
            // A screen-space panel isn't in the world, and with Mesh Input it draws on this object's own mesh.
            if (Application.isPlaying || panel.ScreenSpace || panel.MeshInput)
                return;
            var size = panel.Resolution / PixelsPerUnit(ref s_PanelPixelsPerUnit, "UI/WorldSpace", 100f);
            FlatPanel(panel.transform, size, type, "UI Panel");
        }

        [DrawGizmo(Shown)]
        static void DrawText(BSText text, GizmoType type)
        {
            if (Application.isPlaying)
                return;
            // The rectangle is where the text wraps, not the text itself, so it gets no fill. The text is always shown,
            // as a preview of what the object says.
            var t = text.transform;
            var size = text.RectTransformSizeDelta;
            Gizmos.matrix = t.localToWorldMatrix;
            Gizmos.color = Outline;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0f));
            FacingArrow(Vector3.zero, Vector3.back, ArrowLength(size));
            Gizmos.matrix = Matrix4x4.identity;
            Label(t.position, string.IsNullOrEmpty(text.Text) ? "(no text)" : text.Text);
            if (Selected(type))
                Label(t.TransformPoint(new Vector3(0f, size.y * 0.5f, 0f)), "Text wraps inside " + WorldSize(t, size));
        }

        [DrawGizmo(Shown)]
        static void DrawPortal(BSPortal portal, GizmoType type)
        {
            if (Application.isPlaying)
                return;
            var t = portal.transform;
            var centre = t.TransformPoint(PortalCentre);
            // Drawn turned towards the Scene view camera, as the portal turns towards the player.
            var toCamera = Camera.current != null ? Camera.current.transform.position - centre : -t.forward;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 1e-6f)
                toCamera = -t.forward;
            Gizmos.matrix = Matrix4x4.TRS(centre, Quaternion.LookRotation(-toCamera.normalized, Vector3.up), t.lossyScale);
            Gizmos.color = Outline;
            const int segments = 48;
            for (var i = 0; i < segments; i++)
            {
                var a = i * Mathf.PI * 2f / segments;
                var b = (i + 1) * Mathf.PI * 2f / segments;
                Gizmos.DrawLine(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * PortalRadius, new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f) * PortalRadius);
            }
            Gizmos.color = Fill;
            Gizmos.DrawCube(Vector3.zero, new Vector3(PortalRadius * 1.4f, PortalRadius * 1.4f, 0f));
            Gizmos.matrix = Matrix4x4.identity;
            if (Selected(type))
                Label(centre + Vector3.up * PortalRadius * t.lossyScale.y, Join("Portal (turns to face the player)", portal.Url));
        }

        [DrawGizmo(Shown)]
        static void DrawGltf(BSGLTF gltf, GizmoType type)
        {
            if (Application.isPlaying)
                return;
            // glTF models face +Z (the blue axis); Legacy Rotate turns them round.
            var t = gltf.transform;
            Gizmos.matrix = t.localToWorldMatrix;
            Gizmos.color = Fill;
            Gizmos.DrawCube(Vector3.zero, Vector3.one * 0.15f);
            Gizmos.color = Outline;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 0.15f);
            FacingArrow(Vector3.zero, gltf.LegacyRotate ? Vector3.back : Vector3.forward, 0.5f);
            Gizmos.matrix = Matrix4x4.identity;
            if (Selected(type))
            {
                var file = string.IsNullOrEmpty(gltf.Url) ? "(no URL)" : System.IO.Path.GetFileName(gltf.Url.Split('?')[0]);
                Label(t.position + Vector3.up * 0.2f, $"glTF model, loads in Play mode: {file}\nThe arrow is the way it faces");
            }
        }

        // -- Helpers ----------------------------------------------------------------------

        // A flat rectangle on the object's XY plane, centred on it and seen from its back (-Z), the way the mirror, the
        // browser, UI panels and text all face: the opposite of the blue axis.
        static void FlatPanel(Transform t, Vector2 size, GizmoType type, string label)
        {
            var flat = new Vector3(size.x, size.y, 0f);
            Gizmos.matrix = t.localToWorldMatrix;
            Gizmos.color = Fill;
            Gizmos.DrawCube(Vector3.zero, flat);
            Gizmos.color = Outline;
            Gizmos.DrawWireCube(Vector3.zero, flat);
            FacingArrow(Vector3.zero, Vector3.back, ArrowLength(size));
            Gizmos.matrix = Matrix4x4.identity;
            if (Selected(type))
                Label(t.TransformPoint(new Vector3(0f, size.y * 0.5f, 0f)), $"{label}\n{WorldSize(t, size)}, seen from the arrow's side");
        }

        static float ArrowLength(Vector2 size) => Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.35f, 0.1f, 1f);

        static void FacingArrow(Vector3 from, Vector3 direction, float length)
        {
            var tip = from + direction * length;
            Gizmos.DrawLine(from, tip);
            var head = length * 0.25f;
            var side = Vector3.Cross(direction, Vector3.up).sqrMagnitude > 0.01f ? Vector3.Cross(direction, Vector3.up).normalized : Vector3.right;
            var up = Vector3.Cross(side, direction).normalized;
            Gizmos.DrawLine(tip, tip - direction * head + side * head);
            Gizmos.DrawLine(tip, tip - direction * head - side * head);
            Gizmos.DrawLine(tip, tip - direction * head + up * head);
            Gizmos.DrawLine(tip, tip - direction * head - up * head);
        }

        static bool Selected(GizmoType type) => (type & GizmoType.InSelectionHierarchy) != 0;

        static string WorldSize(Transform t, Vector2 size)
        {
            var scale = t.lossyScale;
            return $"{Mathf.Abs(size.x * scale.x):0.##} × {Mathf.Abs(size.y * scale.y):0.##} m";
        }

        static string Join(string title, string url) => string.IsNullOrEmpty(url) ? title : title + "\n" + url;

        static void Label(Vector3 position, string text)
        {
            if (s_LabelStyle == null)
            {
                s_LabelStyle = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.LowerCenter, wordWrap = false };
                s_LabelStyle.normal.textColor = Outline;
            }
            Handles.Label(position, text, s_LabelStyle);
        }

        // m_PixelsPerUnit has no public getter. Read once per domain; the fallback is the value the asset ships with.
        static float PixelsPerUnit(ref float cached, string resourcePath, float fallback)
        {
            if (cached > 0f)
                return cached;
            cached = fallback;
            var settings = Resources.Load<PanelSettings>(resourcePath);
            if (settings != null)
            {
                var property = new SerializedObject(settings).FindProperty("m_PixelsPerUnit");
                if (property != null && property.floatValue > 0f)
                    cached = property.floatValue;
            }
            return cached;
        }
    }
}
