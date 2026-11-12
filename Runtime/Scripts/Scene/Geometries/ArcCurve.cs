using UnityEngine;

namespace BS
{
    // Ported from three.js ArcCurve - an EllipseCurve with equal radii.
    public class ArcCurve : EllipseCurve
    {
        public ArcCurve(float aX, float aY, float aRadius, float aStartAngle, float aEndAngle, bool aClockwise)
            : base(aX, aY, aRadius, aRadius, aStartAngle, aEndAngle, aClockwise, 0)
        {
        }
    }
}
