using UnityEngine;

namespace BS
{
    // Ported from three.js QuadraticBezierCurve. 2D, so z stays 0.
    public class QuadraticBezierCurve : Curve
    {
        public Vector2 v0, v1, v2;

        public QuadraticBezierCurve(Vector2 v0, Vector2 v1, Vector2 v2)
        {
            this.v0 = v0;
            this.v1 = v1;
            this.v2 = v2;
        }

        public override Vector3 GetPoint(float t)
        {
            return new Vector3(
                Interpolations.QuadraticBezier(t, v0.x, v1.x, v2.x),
                Interpolations.QuadraticBezier(t, v0.y, v1.y, v2.y),
                0);
        }
    }
}
