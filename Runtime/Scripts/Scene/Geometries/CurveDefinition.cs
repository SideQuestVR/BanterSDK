using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// One segment of a hand-built curve path. JsonUtility cannot deserialize polymorphic types,
    /// so every variant shares one flat record and is discriminated by <see cref="type"/>.
    /// Unused control points stay at zero and cost nothing on the wire.
    /// </summary>
    [Serializable]
    public class CurveSegment
    {
        /// <summary>Line | Quadratic | Cubic</summary>
        public string type;
        public Vector3 v0;
        public Vector3 v1;
        public Vector3 v2;
        public Vector3 v3;
    }

    /// <summary>
    /// The wire format for a 3D curve, used by TubeGeometry and as ExtrudeGeometry's extrude path.
    ///
    /// Travels as a JSON string because the code generator only supports scalar and fixed-struct
    /// properties - arrays and custom types are rejected outright - which is the same reason
    /// parametricPoints is a string. Keep every field ASCII: the JS-Unity transport joins on
    /// the delimiters in MessageDelimiters, and a payload containing one would break framing.
    /// </summary>
    [Serializable]
    public class CurveDefinition
    {
        /// <summary>CatmullRom | Line | Path</summary>
        public string type;
        public bool closed;
        /// <summary>centripetal | chordal | catmullrom</summary>
        public string curveType;
        public float tension;
        public Vector3[] points;
        public CurveSegment[] segments;

        /// <summary>
        /// Returns null and logs once on malformed input rather than throwing, so a world
        /// author's typo cannot take out a component's Start.
        /// </summary>
        public static Curve Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            CurveDefinition def;
            try
            {
                def = JsonUtility.FromJson<CurveDefinition>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BSGeometry] Could not parse curvePoints: " + e.Message);
                return null;
            }

            if (def == null)
            {
                return null;
            }

            return def.ToCurve();
        }

        public Curve ToCurve()
        {
            var kind = string.IsNullOrEmpty(type) ? "CatmullRom" : type;

            if (kind == "Path")
            {
                if (segments == null || segments.Length == 0)
                {
                    Debug.LogWarning("[BSGeometry] Curve type 'Path' needs at least one segment.");
                    return null;
                }

                var path = new CurvePath();
                foreach (var s in segments)
                {
                    switch (string.IsNullOrEmpty(s.type) ? "Line" : s.type)
                    {
                        case "Cubic":
                            path.Add(new CubicBezierCurve3(s.v0, s.v1, s.v2, s.v3));
                            break;
                        case "Quadratic":
                            path.Add(new QuadraticBezierCurve3(s.v0, s.v1, s.v2));
                            break;
                        default:
                            path.Add(new LineCurve3(s.v0, s.v1));
                            break;
                    }
                }
                return path;
            }

            if (points == null || points.Length < 2)
            {
                Debug.LogWarning("[BSGeometry] A curve needs at least two points.");
                return null;
            }

            if (kind == "Line")
            {
                var path = new CurvePath();
                for (int i = 0; i < points.Length - 1; i++)
                {
                    path.Add(new LineCurve3(points[i], points[i + 1]));
                }
                if (closed)
                {
                    path.Add(new LineCurve3(points[points.Length - 1], points[0]));
                }
                return path;
            }

            return new CatmullRomCurve3(new List<Vector3>(points), closed,
                string.IsNullOrEmpty(curveType) ? "centripetal" : curveType,
                tension <= 0 ? 0.5f : tension);
        }
    }
}
