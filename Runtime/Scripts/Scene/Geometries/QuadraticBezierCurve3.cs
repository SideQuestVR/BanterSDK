using UnityEngine;

namespace BS
{
    // Ported from three.js QuadraticBezierCurve3.
    public class QuadraticBezierCurve3 : Curve
    {
        public Vector3 v0, v1, v2;

        public QuadraticBezierCurve3(Vector3 v0, Vector3 v1, Vector3 v2)
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
                Interpolations.QuadraticBezier(t, v0.z, v1.z, v2.z));
        }
    }
}
