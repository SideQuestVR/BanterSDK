using UnityEngine;

namespace BS
{
    // Ported from three.js CubicBezierCurve. 2D, so z stays 0.
    public class CubicBezierCurve : Curve
    {
        public Vector2 v0, v1, v2, v3;

        public CubicBezierCurve(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            this.v0 = v0;
            this.v1 = v1;
            this.v2 = v2;
            this.v3 = v3;
        }

        public override Vector3 GetPoint(float t)
        {
            return new Vector3(
                Interpolations.CubicBezier(t, v0.x, v1.x, v2.x, v3.x),
                Interpolations.CubicBezier(t, v0.y, v1.y, v2.y, v3.y),
                0);
        }
    }
}
