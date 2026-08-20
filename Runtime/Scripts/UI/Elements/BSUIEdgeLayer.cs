using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BS.UICodeGen;

namespace BS.UI.Elements
{
    /// <summary>
    /// Renders a set of cubic bezier connection curves (node-graph edges) from a single JSON
    /// property, so a whole graph's wiring costs one bus message to update. Coordinates are in
    /// the layer's local space, pre-transform: an ancestor's translate/scale pan-zooms the
    /// curves for free, and transform changes do not re-run generateVisualContent.
    ///
    /// The layer never participates in picking - edge hit-testing is done on the JS side, which
    /// owns the geometry.
    /// </summary>
    [UIElement(typeof(VisualElement), "UIEdgeLayer")]
    public partial class BSUIEdgeLayer : VisualElement
    {
        [Serializable]
        public class EdgeDatum
        {
            public string id;
            public float x1, y1, cx1, cy1, cx2, cy2, x2, y2;
            public string color;
            public float width = 2f;
            public bool dashed;
            public bool selected;
            public bool arrow;

            [NonSerialized] public Color parsedColor;
        }

        [Serializable]
        class EdgeList
        {
            public EdgeDatum[] edges;
        }

        const float DashLength = 6f;
        const float GapLength = 4f;

        static readonly Color FallbackColor = new Color(0.55f, 0.58f, 0.62f);

        List<EdgeDatum> _edges = new List<EdgeDatum>();
        string _edgesJson = "[]";

        public BSUIEdgeLayer()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        [UIProperty(propertyName: "edges")]
        public string Edges
        {
            get => _edgesJson;
            set
            {
                _edgesJson = string.IsNullOrEmpty(value) ? "[]" : value;
                ParseEdges(_edgesJson);
                MarkDirtyRepaint();
            }
        }

        [UIProperty(propertyName: "name")]
        public string ElementName
        {
            get => name;
            set => name = value;
        }

        void ParseEdges(string json)
        {
            _edges.Clear();
            try
            {
                // JsonUtility cannot parse a bare array; wrap it. It handles \uXXXX escapes,
                // which is how the JS side smuggles bus-delimiter characters through.
                var list = JsonUtility.FromJson<EdgeList>("{\"edges\":" + json + "}");
                if (list?.edges == null) return;
                foreach (var edge in list.edges)
                {
                    if (edge == null) continue;
                    if (!ColorUtility.TryParseHtmlString(edge.color, out edge.parsedColor))
                    {
                        edge.parsedColor = FallbackColor;
                    }
                    if (edge.width <= 0f) edge.width = 2f;
                    _edges.Add(edge);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BSUIEdgeLayer] Failed to parse edges JSON: {e.Message}");
            }
        }

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (_edges.Count == 0) return;

            var painter = mgc.painter2D;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            foreach (var edge in _edges)
            {
                if (edge.selected)
                {
                    // Wider translucent underlay reads as a highlight without a second color
                    // channel on the wire.
                    var underlay = edge.parsedColor;
                    underlay.a = 0.35f;
                    StrokeEdge(painter, edge, underlay, edge.width + 4f, dashed: false);
                }

                StrokeEdge(painter, edge, edge.parsedColor, edge.width, edge.dashed);

                if (edge.arrow)
                {
                    DrawArrowhead(painter, edge);
                }
            }
        }

        static void StrokeEdge(Painter2D painter, EdgeDatum edge, Color color, float width, bool dashed)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();

            var p0 = new Vector2(edge.x1, edge.y1);
            var c1 = new Vector2(edge.cx1, edge.cy1);
            var c2 = new Vector2(edge.cx2, edge.cy2);
            var p1 = new Vector2(edge.x2, edge.y2);

            if (!dashed)
            {
                painter.MoveTo(p0);
                painter.BezierCurveTo(c1, c2, p1);
            }
            else
            {
                // Painter2D has no dash API: flatten the cubic and stroke alternating runs as
                // subpaths of one path.
                var approxLength = (c1 - p0).magnitude + (c2 - c1).magnitude + (p1 - c2).magnitude;
                var steps = Mathf.Clamp(Mathf.CeilToInt(approxLength / 6f), 8, 64);
                var previous = p0;
                var distanceIntoPattern = 0f;
                var penDown = false;
                for (var i = 1; i <= steps; i++)
                {
                    var t = (float)i / steps;
                    var point = CubicPoint(p0, c1, c2, p1, t);
                    var segment = (point - previous).magnitude;
                    distanceIntoPattern += segment;
                    var inDash = distanceIntoPattern % (DashLength + GapLength) < DashLength;
                    if (inDash && !penDown)
                    {
                        painter.MoveTo(previous);
                        penDown = true;
                    }
                    if (penDown)
                    {
                        painter.LineTo(point);
                    }
                    if (!inDash)
                    {
                        penDown = false;
                    }
                    previous = point;
                }
            }

            painter.Stroke();
        }

        static void DrawArrowhead(Painter2D painter, EdgeDatum edge)
        {
            var tip = new Vector2(edge.x2, edge.y2);
            var tangent = tip - new Vector2(edge.cx2, edge.cy2);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = tip - new Vector2(edge.x1, edge.y1);
            }
            tangent.Normalize();
            var normal = new Vector2(-tangent.y, tangent.x);
            var size = Mathf.Max(6f, edge.width * 3f);
            var baseCenter = tip - tangent * size;

            painter.fillColor = edge.parsedColor;
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(baseCenter + normal * (size * 0.5f));
            painter.LineTo(baseCenter - normal * (size * 0.5f));
            painter.ClosePath();
            painter.Fill();
        }

        static Vector2 CubicPoint(Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p1, float t)
        {
            var u = 1f - t;
            return u * u * u * p0
                 + 3f * u * u * t * c1
                 + 3f * u * t * t * c2
                 + t * t * t * p1;
        }
    }
}
