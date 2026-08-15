using UnityEngine;

namespace BS
{
    // Ported from three.js LineCurve3.
    public class LineCurve3 : Curve
    {
        public Vector3 v1;
        public Vector3 v2;

        public LineCurve3(Vector3 v1, Vector3 v2)
        {
            this.v1 = v1;
            this.v2 = v2;
        }

        public override Vector3 GetPoint(float t)
        {
            return t >= 1 ? v2 : Vector3.LerpUnclamped(v1, v2, t);
        }

        public override Vector3 GetTangent(float t)
        {
            return (v2 - v1).normalized;
        }
    }
}
