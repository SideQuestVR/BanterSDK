using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Ported from three.js CatmullRomCurve3. The practical way to author a smooth 3D path from a
    /// handful of points, which is what TubeGeometry is usually given.
    /// </summary>
    public class CatmullRomCurve3 : Curve
    {
        public List<Vector3> points;
        public bool closed;

        /// <summary>"centripetal", "chordal" or "catmullrom".</summary>
        public string curveType;

        /// <summary>Only used when curveType is "catmullrom".</summary>
        public float tension;

        public CatmullRomCurve3(List<Vector3> points, bool closed = false, string curveType = "centripetal", float tension = 0.5f)
        {
            this.points = points ?? new List<Vector3>();
            this.closed = closed;
            this.curveType = string.IsNullOrEmpty(curveType) ? "centripetal" : curveType;
            this.tension = tension;
        }

        public override Vector3 GetPoint(float t)
        {
            int l = points.Count;
            if (l < 2)
            {
                return l == 1 ? points[0] : Vector3.zero;
            }

            var p = (l - (closed ? 0 : 1)) * t;
            int intPoint = (int)Mathf.Floor(p);
            var weight = p - intPoint;

            if (closed)
            {
                intPoint += intPoint > 0 ? 0 : ((int)Mathf.Floor(Mathf.Abs(intPoint) / (float)l) + 1) * l;
            }
            else if (weight == 0 && intPoint == l - 1)
            {
                intPoint = l - 2;
                weight = 1;
            }

            Vector3 p0, p3;

            if (closed || intPoint > 0)
            {
                p0 = points[(intPoint - 1) % l];
            }
            else
            {
                // extrapolate backwards past the first point
                p0 = points[0] - points[1] + points[0];
            }

            var p1 = points[intPoint % l];
            var p2 = points[(intPoint + 1) % l];

            if (closed || intPoint + 2 < l)
            {
                p3 = points[(intPoint + 2) % l];
            }
            else
            {
                // extrapolate forwards past the last point
                p3 = points[l - 1] - points[l - 2] + points[l - 1];
            }

            if (curveType == "centripetal" || curveType == "chordal")
            {
                // The exponent is what separates the two: 0.5 (centripetal) avoids the cusps and
                // self-intersections that uniform Catmull-Rom produces on unevenly spaced points.
                var pow = curveType == "chordal" ? 0.5f : 0.25f;
                var dt0 = Mathf.Pow((p0 - p1).sqrMagnitude, pow);
                var dt1 = Mathf.Pow((p1 - p2).sqrMagnitude, pow);
                var dt2 = Mathf.Pow((p2 - p3).sqrMagnitude, pow);

                // safety against repeated points
                if (dt1 < 1e-4f) dt1 = 1.0f;
                if (dt0 < 1e-4f) dt0 = dt1;
                if (dt2 < 1e-4f) dt2 = dt1;

                return new Vector3(
                    NonUniform(weight, p0.x, p1.x, p2.x, p3.x, dt0, dt1, dt2),
                    NonUniform(weight, p0.y, p1.y, p2.y, p3.y, dt0, dt1, dt2),
                    NonUniform(weight, p0.z, p1.z, p2.z, p3.z, dt0, dt1, dt2));
            }

            return new Vector3(
                Uniform(weight, p0.x, p1.x, p2.x, p3.x, tension),
                Uniform(weight, p0.y, p1.y, p2.y, p3.y, tension),
                Uniform(weight, p0.z, p1.z, p2.z, p3.z, tension));
        }

        static float NonUniform(float t, float x0, float x1, float x2, float x3, float dt0, float dt1, float dt2)
        {
            // compute tangents when parameterized in [t1,t2]
            var t1 = (x1 - x0) / dt0 - (x2 - x0) / (dt0 + dt1) + (x2 - x1) / dt1;
            var t2 = (x2 - x1) / dt1 - (x3 - x1) / (dt1 + dt2) + (x3 - x2) / dt2;

            // rescale for [0,1] parameterization
            t1 *= dt1;
            t2 *= dt1;

            return Hermite(t, x1, x2, t1, t2);
        }

        static float Uniform(float t, float x0, float x1, float x2, float x3, float tension)
        {
            var t1 = tension * (x2 - x0);
            var t2 = tension * (x3 - x1);
            return Hermite(t, x1, x2, t1, t2);
        }

        static float Hermite(float t, float p0, float p1, float m0, float m1)
        {
            var c0 = p0;
            var c1 = m0;
            var c2 = -3 * p0 + 3 * p1 - 2 * m0 - m1;
            var c3 = 2 * p0 - 2 * p1 + m0 + m1;

            var t2 = t * t;
            var t3 = t2 * t;
            return c0 + c1 * t + c2 * t2 + c3 * t3;
        }
    }
}
