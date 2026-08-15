using UnityEngine;

namespace BS
{
    // Ported from three.js EllipseCurve. Backs arc, absarc, ellipse and absellipse on Path.
    public class EllipseCurve : Curve
    {
        public float aX, aY;
        public float xRadius, yRadius;
        public float aStartAngle, aEndAngle;
        public bool aClockwise;
        public float aRotation;

        public EllipseCurve(float aX = 0, float aY = 0, float xRadius = 1, float yRadius = 1,
            float aStartAngle = 0, float aEndAngle = Mathf.PI * 2, bool aClockwise = false, float aRotation = 0)
        {
            this.aX = aX;
            this.aY = aY;
            this.xRadius = xRadius;
            this.yRadius = yRadius;
            this.aStartAngle = aStartAngle;
            this.aEndAngle = aEndAngle;
            this.aClockwise = aClockwise;
            this.aRotation = aRotation;
        }

        public override Vector3 GetPoint(float t)
        {
            var twoPi = Mathf.PI * 2;
            var deltaAngle = aEndAngle - aStartAngle;
            var samePoints = Mathf.Abs(deltaAngle) < Mathf.Epsilon;

            // ensure deltaAngle is 0 .. 2*PI
            while (deltaAngle < 0) deltaAngle += twoPi;
            while (deltaAngle > twoPi) deltaAngle -= twoPi;

            if (deltaAngle < Mathf.Epsilon)
            {
                deltaAngle = samePoints ? 0 : twoPi;
            }

            if (aClockwise && !samePoints)
            {
                deltaAngle = deltaAngle == twoPi ? -twoPi : deltaAngle - twoPi;
            }

            var angle = aStartAngle + t * deltaAngle;
            var x = aX + xRadius * Mathf.Cos(angle);
            var y = aY + yRadius * Mathf.Sin(angle);

            if (aRotation != 0)
            {
                var cos = Mathf.Cos(aRotation);
                var sin = Mathf.Sin(aRotation);

                var tx = x - aX;
                var ty = y - aY;

                x = tx * cos - ty * sin + aX;
                y = tx * sin + ty * cos + aY;
            }

            return new Vector3(x, y, 0);
        }
    }
}
