using UnityEngine;

namespace BS
{
    // Ported from three.js LineCurve. 2D, so z stays 0.
    public class LineCurve : Curve
    {
        public Vector2 v1;
        public Vector2 v2;

        public LineCurve(Vector2 v1, Vector2 v2)
        {
            this.v1 = v1;
            this.v2 = v2;
        }

        public override Vector3 GetPoint(float t)
        {
            var p = t >= 1 ? v2 : Vector2.LerpUnclamped(v1, v2, t);
            return new Vector3(p.x, p.y, 0);
        }

        public override Vector3 GetTangent(float t)
        {
            var d = (v2 - v1).normalized;
            return new Vector3(d.x, d.y, 0);
        }
    }
}
