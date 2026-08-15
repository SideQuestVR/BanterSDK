using UnityEngine;

namespace BS
{
    // Ported from three.js CubicBezierCurve3.
    public class CubicBezierCurve3 : Curve
    {
        public Vector3 v0, v1, v2, v3;

        public CubicBezierCurve3(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
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
                Interpolations.CubicBezier(t, v0.z, v1.z, v2.z, v3.z));
        }
    }
}
