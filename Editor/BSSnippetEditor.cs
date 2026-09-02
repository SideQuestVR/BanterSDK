using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS.SDKEditor
{
    /*
     * Inspector + scene gizmos for BSSnippet. The attribute fields are generated from the live
     * <bs-snippet> element in index.html (via SnippetHtmlSync), so this is pure-C# UI Toolkit —
     * a static UXML template buys nothing when the field set is dynamic.
     *
     * Two-way sync: field edits go through SnippetHtmlSync.SetAttribute (debounced to disk);
     * SnippetHtmlSync.Changed comes back for external file edits, gizmo drags and other open
     * inspectors. The field currently being typed in is never overwritten from an event.
     */
    [CustomEditor(typeof(BSSnippet))]
    public class BSSnippetEditor : Editor
    {
        static readonly string[] ReservedAttributes = { "name", "title", "description", SnippetHtmlSync.InstanceAttribute };

        VisualElement _root;
        HelpBox _statusBox;
        Label _titleLabel;
        Label _descriptionLabel;
        Label _metaLabel;
        VisualElement _attributesContainer;
        readonly Dictionary<string, Action<string>> _fieldUpdaters = new Dictionary<string, Action<string>>();
        List<string> _shownAttributes = new List<string>();
        string _focusedAttribute;
        List<GizmoDef> _gizmos;
        bool _warnedBadGizmo;

        BSSnippet Script => target as BSSnippet;

        void OnEnable()
        {
            SnippetHtmlSync.Changed += OnHtmlChanged;
            SnippetHtmlSync.AddInspectorRef();
            SnippetReconciler.QueueReconcile();
        }

        void OnDisable()
        {
            SnippetHtmlSync.Changed -= OnHtmlChanged;
            SnippetHtmlSync.RemoveInspectorRef();
            SnippetHtmlSync.FlushNow();
        }

        public override bool UseDefaultMargins() => false;

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();
            _root.style.paddingLeft = 8;
            _root.style.paddingRight = 8;
            _root.style.paddingTop = 4;
            _root.style.paddingBottom = 4;
            var styleSheet = Resources.Load<StyleSheet>("BanterCustomInspector");
            if (styleSheet != null) _root.styleSheets.Add(styleSheet);

            var slugField = new TextField("Slug") { isDelayed = true };
            slugField.BindProperty(serializedObject.FindProperty("slug"));
            // The serialized change alone already fires OnValidate -> reconciler, but queue
            // explicitly too so a re-fetch starts even when OnValidate is swallowed (multi-edit).
            slugField.RegisterValueChangedCallback(_ => SnippetReconciler.QueueReconcile());
            _root.Add(slugField);

            _statusBox = new HelpBox("", HelpBoxMessageType.Info);
            _statusBox.style.display = DisplayStyle.None;
            _root.Add(_statusBox);

            _titleLabel = new Label();
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.marginTop = 6;
            _root.Add(_titleLabel);

            _descriptionLabel = new Label();
            _descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
            _descriptionLabel.style.opacity = 0.85f;
            _root.Add(_descriptionLabel);

            _metaLabel = new Label();
            _metaLabel.style.opacity = 0.5f;
            _metaLabel.style.fontSize = 10;
            _metaLabel.style.marginBottom = 6;
            _root.Add(_metaLabel);

            _attributesContainer = new VisualElement();
            _attributesContainer.RegisterCallback<FocusInEvent>(OnAttributeFocusIn);
            _attributesContainer.RegisterCallback<FocusOutEvent>(_ => _focusedAttribute = null);
            _root.Add(_attributesContainer);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 6;
            var refresh = new Button(OnRefreshClicked) { text = "Refresh from server" };
            var open = new Button(() => EditorUtility.OpenWithDefaultApp(SnippetHtmlSync.AssetPath)) { text = "Open index.html" };
            buttons.Add(refresh);
            buttons.Add(open);
            _root.Add(buttons);

            RebuildContent();
            // Fetch/error status lives outside the Changed event stream, so poll it cheaply.
            _root.schedule.Execute(UpdateStatus).Every(250);
            return _root;
        }

        void OnRefreshClicked()
        {
            if (Script == null || string.IsNullOrEmpty(Script.Slug)) return;
            if (!EditorUtility.DisplayDialog("Refresh snippet?",
                    "Re-fetch this snippet from altvr.app? Local attribute edits in index.html for this instance will be discarded.",
                    "Refresh", "Cancel"))
                return;
            SnippetReconciler.Refetch(Script);
        }

        void OnHtmlChanged(SnippetHtmlSync.ChangeKind kind, string instanceId, string attribute)
        {
            if (Script == null || _root == null) return;
            var mine = Script.InstanceId;
            if (kind == SnippetHtmlSync.ChangeKind.AttributeSet)
            {
                if (instanceId != mine) return;
                UpdateSingleField(attribute);
            }
            else if (kind == SnippetHtmlSync.ChangeKind.Reloaded || instanceId == mine)
            {
                RebuildContent();
            }
            _gizmos = null;
            SceneView.RepaintAll();
        }

        void OnAttributeFocusIn(FocusInEvent evt)
        {
            // FocusIn bubbles from the inner text inputs of compound (vector) fields, so walking
            // up to the row created for the attribute identifies which one is being typed in.
            for (var element = evt.target as VisualElement; element != null && element != _attributesContainer; element = element.parent)
            {
                if (element.userData is string attributeName)
                {
                    _focusedAttribute = attributeName;
                    return;
                }
            }
        }

        void RebuildContent()
        {
            if (_root == null || Script == null) return;
            var element = SnippetHtmlSync.Get(Script.InstanceId);

            _titleLabel.text = (element != null ? (string)element.Attribute("title") : null) ?? Script.CachedTitle ?? "";
            // Description is optional on the snippet but the row is always visible; a dim
            // placeholder keeps the layout honest instead of a silently empty label.
            var description = (element != null ? (string)element.Attribute("description") : null) ?? Script.CachedDescription;
            var hasDescription = !string.IsNullOrEmpty(description);
            _descriptionLabel.text = hasDescription ? description : "(no description)";
            _descriptionLabel.style.unityFontStyleAndWeight = hasDescription ? FontStyle.Normal : FontStyle.Italic;
            _descriptionLabel.style.opacity = hasDescription ? 0.85f : 0.45f;
            _metaLabel.text = element == null ? "" : $"name: {(string)element.Attribute("name")}   instance: {Script.InstanceId}";

            _attributesContainer.Clear();
            _fieldUpdaters.Clear();
            _shownAttributes = new List<string>();
            if (element == null) return;

            foreach (var attribute in element.Attributes())
            {
                var attributeName = attribute.Name.LocalName;
                if (ReservedAttributes.Contains(attributeName)) continue;
                _shownAttributes.Add(attributeName);
                _attributesContainer.Add(CreateAttributeField(attributeName, attribute.Value));
            }
        }

        void UpdateSingleField(string attributeName)
        {
            if (Script == null) return;
            var element = SnippetHtmlSync.Get(Script.InstanceId);
            if (element == null) { RebuildContent(); return; }
            // A brand-new attribute (or one whose value no longer fits its field's type) needs a
            // full rebuild; same-type value changes update in place.
            if (!_fieldUpdaters.TryGetValue(attributeName, out var update)) { RebuildContent(); return; }
            if (attributeName == _focusedAttribute) return; // never clobber what's being typed
            var value = (string)element.Attribute(attributeName);
            if (value == null) { RebuildContent(); return; }
            update(value);
        }

        void UpdateStatus()
        {
            if (Script == null || _statusBox == null) return;
            string message = null;
            var type = HelpBoxMessageType.Info;
            if (SnippetHtmlSync.LoadFailed)
            {
                message = "The snippet section in index.html is unreadable — fix it to re-enable snippet editing.";
                type = HelpBoxMessageType.Error;
            }
            else if (SnippetReconciler.IsFetching(Script))
            {
                message = $"Fetching '{Script.Slug}' from altvr.app…";
            }
            else
            {
                var error = SnippetReconciler.GetLastError(Script);
                if (error != null)
                {
                    message = error;
                    type = HelpBoxMessageType.Error;
                }
                else if (!string.IsNullOrEmpty(Script.Slug) && SnippetHtmlSync.Get(Script.InstanceId) == null)
                {
                    message = "No matching element in index.html yet.";
                    type = HelpBoxMessageType.Warning;
                }
            }

            _statusBox.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (message != null)
            {
                _statusBox.text = message;
                _statusBox.messageType = type;
            }
        }

        // ---- dynamic attribute fields ------------------------------------------------------

        VisualElement CreateAttributeField(string attributeName, string value)
        {
            var label = ObjectNames.NicifyVariableName(attributeName);
            var id = Script.InstanceId;
            void Commit(string newValue) => SnippetHtmlSync.SetAttribute(id, attributeName, newValue);

            VisualElement field;
            var numbers = TryParseNumbers(value);
            if (value == "true" || value == "false")
            {
                var toggle = new Toggle(label) { value = value == "true" };
                toggle.RegisterValueChangedCallback(ev => Commit(ev.newValue ? "true" : "false"));
                _fieldUpdaters[attributeName] = v =>
                {
                    if (v == "true" || v == "false") toggle.SetValueWithoutNotify(v == "true");
                    else RebuildContent();
                };
                field = toggle;
            }
            else if (numbers != null && numbers.Length == 1)
            {
                var floatField = new FloatField(label) { value = numbers[0] };
                floatField.RegisterValueChangedCallback(ev => Commit(Format(ev.newValue)));
                _fieldUpdaters[attributeName] = v =>
                {
                    var n = TryParseNumbers(v);
                    if (n != null && n.Length == 1) floatField.SetValueWithoutNotify(n[0]);
                    else RebuildContent();
                };
                field = floatField;
            }
            else if (numbers != null && numbers.Length == 2)
            {
                var vectorField = new Vector2Field(label) { value = new Vector2(numbers[0], numbers[1]) };
                vectorField.RegisterValueChangedCallback(ev => Commit(Format(ev.newValue.x, ev.newValue.y)));
                _fieldUpdaters[attributeName] = v =>
                {
                    var n = TryParseNumbers(v);
                    if (n != null && n.Length == 2) vectorField.SetValueWithoutNotify(new Vector2(n[0], n[1]));
                    else RebuildContent();
                };
                field = vectorField;
            }
            else if (numbers != null && numbers.Length == 3)
            {
                var vectorField = new Vector3Field(label) { value = new Vector3(numbers[0], numbers[1], numbers[2]) };
                vectorField.RegisterValueChangedCallback(ev => Commit(Format(ev.newValue.x, ev.newValue.y, ev.newValue.z)));
                _fieldUpdaters[attributeName] = v =>
                {
                    var n = TryParseNumbers(v);
                    if (n != null && n.Length == 3) vectorField.SetValueWithoutNotify(new Vector3(n[0], n[1], n[2]));
                    else RebuildContent();
                };
                field = vectorField;
            }
            else if (numbers != null && numbers.Length == 4)
            {
                var vectorField = new Vector4Field(label) { value = new Vector4(numbers[0], numbers[1], numbers[2], numbers[3]) };
                vectorField.RegisterValueChangedCallback(ev => Commit(Format(ev.newValue.x, ev.newValue.y, ev.newValue.z, ev.newValue.w)));
                _fieldUpdaters[attributeName] = v =>
                {
                    var n = TryParseNumbers(v);
                    if (n != null && n.Length == 4) vectorField.SetValueWithoutNotify(new Vector4(n[0], n[1], n[2], n[3]));
                    else RebuildContent();
                };
                field = vectorField;
            }
            else
            {
                var textField = new TextField(label) { value = value };
                textField.RegisterValueChangedCallback(ev => Commit(ev.newValue));
                _fieldUpdaters[attributeName] = v => textField.SetValueWithoutNotify(v);
                field = textField;
            }

            field.userData = attributeName; // consumed by OnAttributeFocusIn
            return field;
        }

        static float[] TryParseNumbers(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var parts = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 4) return null;
            var result = new float[parts.Length];
            for (var i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                    return null;
            return result;
        }

        // Invariant culture, space-separated — the HTML contract. Known wart: "1.0" round-trips
        // as "1".
        static string Format(params float[] values) =>
            string.Join(" ", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));

        // ---- scene gizmos ------------------------------------------------------------------

        class GizmoDef
        {
            public enum Kind { Position, Plane, Box, Sphere }
            public Kind kind;
            public string boundAttribute;
            public Vector3 offset;
            public Vector3 euler;
            public Vector2 planeSize = Vector2.one;
            public Vector3 boxSize = Vector3.one;
            public float radius = 0.5f;
        }

        static readonly Color GizmoOutline = new Color(0f, 0.7f, 1f, 0.9f);
        static readonly Color GizmoFill = new Color(0f, 0.7f, 1f, 0.06f);

        void OnSceneGUI()
        {
            if (Script == null) return;
            var element = SnippetHtmlSync.Get(Script.InstanceId);
            if (element == null) return;
            if (_gizmos == null) ParseGizmos(element);

            var t = Script.transform;
            foreach (var gizmo in _gizmos)
            {
                var local = ResolveLocation(gizmo, element);
                if (gizmo.kind == GizmoDef.Kind.Position)
                {
                    EditorGUI.BeginChangeCheck();
                    var world = Handles.PositionHandle(t.TransformPoint(local), t.rotation);
                    if (EditorGUI.EndChangeCheck())
                        // Same debounced path as inspector edits; the open inspector field
                        // live-updates via Changed(AttributeSet). Deliberately no Undo entry —
                        // index.html is outside the Undo stack (see SnippetReconciler header).
                        SnippetHtmlSync.SetAttribute(Script.InstanceId, gizmo.boundAttribute, Format(
                            t.InverseTransformPoint(world).x, t.InverseTransformPoint(world).y, t.InverseTransformPoint(world).z));
                    continue;
                }

                using (new Handles.DrawingScope(GizmoOutline,
                           t.localToWorldMatrix * Matrix4x4.TRS(local, Quaternion.Euler(gizmo.euler), Vector3.one)))
                {
                    switch (gizmo.kind)
                    {
                        case GizmoDef.Kind.Plane:
                            var half = gizmo.planeSize * 0.5f;
                            Handles.DrawSolidRectangleWithOutline(new[]
                            {
                                new Vector3(-half.x, -half.y, 0),
                                new Vector3(-half.x, half.y, 0),
                                new Vector3(half.x, half.y, 0),
                                new Vector3(half.x, -half.y, 0),
                            }, GizmoFill, GizmoOutline);
                            break;
                        case GizmoDef.Kind.Box:
                            Handles.DrawWireCube(Vector3.zero, gizmo.boxSize);
                            break;
                        case GizmoDef.Kind.Sphere:
                            Handles.DrawWireDisc(Vector3.zero, Vector3.up, gizmo.radius);
                            Handles.DrawWireDisc(Vector3.zero, Vector3.right, gizmo.radius);
                            Handles.DrawWireDisc(Vector3.zero, Vector3.forward, gizmo.radius);
                            break;
                    }
                }
            }
        }

        Vector3 ResolveLocation(GizmoDef gizmo, XElement element)
        {
            if (!string.IsNullOrEmpty(gizmo.boundAttribute))
            {
                var bound = TryParseNumbers((string)element.Attribute(gizmo.boundAttribute));
                if (bound != null && bound.Length == 3) return new Vector3(bound[0], bound[1], bound[2]);
            }
            return gizmo.offset;
        }

        void ParseGizmos(XElement element)
        {
            _gizmos = new List<GizmoDef>();
            _warnedBadGizmo = false;
            foreach (var child in element.Descendants(SnippetHtmlSync.GizmoElementName))
            {
                var typeName = (string)child.Attribute("type");
                var def = new GizmoDef { boundAttribute = (string)child.Attribute("attribute") };
                switch (typeName)
                {
                    case "position": def.kind = GizmoDef.Kind.Position; break;
                    case "plane": def.kind = GizmoDef.Kind.Plane; break;
                    case "box": def.kind = GizmoDef.Kind.Box; break;
                    case "sphere": def.kind = GizmoDef.Kind.Sphere; break;
                    default: WarnBadGizmo($"unknown type '{typeName}'"); continue;
                }
                if (def.kind == GizmoDef.Kind.Position && string.IsNullOrEmpty(def.boundAttribute))
                {
                    WarnBadGizmo("type=\"position\" requires an attribute=\"...\" binding");
                    continue;
                }

                var position = TryParseNumbers((string)child.Attribute("position"));
                if (position != null && position.Length == 3) def.offset = new Vector3(position[0], position[1], position[2]);
                var rotation = TryParseNumbers((string)child.Attribute("rotation"));
                if (rotation != null && rotation.Length == 3) def.euler = new Vector3(rotation[0], rotation[1], rotation[2]);
                var size = TryParseNumbers((string)child.Attribute("size"));
                if (size != null && size.Length == 2) def.planeSize = new Vector2(size[0], size[1]);
                if (size != null && size.Length == 3) def.boxSize = new Vector3(size[0], size[1], size[2]);
                var radius = TryParseNumbers((string)child.Attribute("radius"));
                if (radius != null && radius.Length == 1) def.radius = radius[0];

                _gizmos.Add(def);
            }
        }

        void WarnBadGizmo(string why)
        {
            if (_warnedBadGizmo) return;
            _warnedBadGizmo = true;
            Debug.LogWarning($"[BSSnippet] Skipping a <{SnippetHtmlSync.GizmoElementName}> on '{Script.Slug}': {why}.");
        }
    }
}
