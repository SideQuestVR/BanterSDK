using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// One pen command. A shape is contours-of-points, which JsonUtility cannot express as a
    /// nested array, so it travels as a flat command tape instead - "M" implicitly starts a new
    /// contour, which removes the nesting entirely. Letters follow SVG so an SVG path importer
    /// would be a JS-side change only.
    /// </summary>
    [Serializable]
    public class ShapeCommand
    {
        /// <summary>M | L | C | Q | S | A | Z | H</summary>
        public string type;
        public float x, y;
        public float x1, y1, x2, y2;
        public float radiusX, radiusY, startAngle, endAngle, rotation;
        public bool clockwise;
    }

    /// <summary>
    /// The wire format for a 2D shape, used by ShapeGeometry, ExtrudeGeometry and Lathe.
    ///
    /// Keep every field ASCII: the JS-Unity transport joins on the delimiters in
    /// MessageDelimiters, and a payload containing one would break framing. There are no
    /// user-supplied strings in this schema, which is what makes that safe.
    /// </summary>
    [Serializable]
    public class ShapeCommands
    {
        public ShapeCommand[] commands;

        /// <summary>
        /// Returns null and logs once on malformed input rather than throwing, so a world
        /// author's typo cannot take out a component's Start.
        /// </summary>
        public static Shape Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            ShapeCommands parsed;
            try
            {
                parsed = JsonUtility.FromJson<ShapeCommands>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BSGeometry] Could not parse shapePoints: " + e.Message);
                return null;
            }

            if (parsed?.commands == null || parsed.commands.Length == 0)
            {
                return null;
            }

            return parsed.Replay();
        }

        public Shape Replay()
        {
            var shape = new Shape();
            Path2D active = shape;
            bool inHoles = false;

            // A run of consecutive S commands is one splineThru, so they accumulate here and
            // flush when anything else turns up.
            var splineRun = new List<Vector2>();

            void FlushSpline()
            {
                if (splineRun.Count > 0)
                {
                    active.SplineThru(new List<Vector2>(splineRun));
                    splineRun.Clear();
                }
            }

            foreach (var c in commands)
            {
                var type = string.IsNullOrEmpty(c.type) ? "" : c.type.ToUpperInvariant();

                if (type != "S")
                {
                    FlushSpline();
                }

                switch (type)
                {
                    case "M":
                        if (inHoles)
                        {
                            active = new Path2D();
                            shape.holes.Add(active);
                        }
                        active.MoveTo(c.x, c.y);
                        break;
                    case "L":
                        active.LineTo(c.x, c.y);
                        break;
                    case "C":
                        active.BezierCurveTo(c.x1, c.y1, c.x2, c.y2, c.x, c.y);
                        break;
                    case "Q":
                        active.QuadraticCurveTo(c.x1, c.y1, c.x, c.y);
                        break;
                    case "S":
                        splineRun.Add(new Vector2(c.x, c.y));
                        break;
                    case "A":
                        active.AbsEllipse(c.x, c.y, c.radiusX, c.radiusY, c.startAngle,
                            c.endAngle == 0 ? Mathf.PI * 2 : c.endAngle, c.clockwise, c.rotation);
                        break;
                    case "Z":
                        active.ClosePath();
                        break;
                    case "H":
                        inHoles = true;
                        break;
                    default:
                        Debug.LogWarning("[BSGeometry] Unknown shape command '" + c.type + "'");
                        break;
                }
            }

            FlushSpline();

            return shape;
        }
    }
}
