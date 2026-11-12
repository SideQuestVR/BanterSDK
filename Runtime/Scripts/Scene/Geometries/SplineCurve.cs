using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js SplineCurve - a uniform Catmull-Rom through 2D points.
    public class SplineCurve : Curve
    {
        public List<Vector2> points;

        public SplineCurve(List<Vector2> points)
        {
            this.points = points ?? new List<Vector2>();
        }

        public override Vector3 GetPoint(float t)
        {
            if (points.Count == 0) return Vector3.zero;
            if (points.Count == 1) return new Vector3(points[0].x, points[0].y, 0);

            var p = (points.Count - 1) * t;
            var intPoint = Mathf.FloorToInt(p);
            var weight = p - intPoint;

            var p0 = points[intPoint == 0 ? intPoint : intPoint - 1];
            var p1 = points[intPoint];
            var p2 = points[intPoint > points.Count - 2 ? points.Count - 1 : intPoint + 1];
            var p3 = points[intPoint > points.Count - 3 ? points.Count - 1 : intPoint + 2];

            return new Vector3(
                Interpolations.CatmullRom(weight, p0.x, p1.x, p2.x, p3.x),
                Interpolations.CatmullRom(weight, p0.y, p1.y, p2.y, p3.y),
                0);
        }
    }
}
