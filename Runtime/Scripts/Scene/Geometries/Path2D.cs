using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Ported from three.js src/extras/core/Path.js - a CurvePath with a 2D pen API.
    ///
    /// Named Path2D rather than Path, which is upstream's name: most of this namespace also uses
    /// System.IO, and a BS.Path shadows System.IO.Path for every file in namespace BS. That broke
    /// four unrelated files the moment it existed, and would have kept catching new ones.
    /// </summary>
    public class Path2D : CurvePath
    {
        public Vector2 currentPoint;

        public Path2D(List<Vector2> points = null)
        {
            if (points != null && points.Count > 0)
            {
                SetFromPoints(points);
            }
        }

        public Path2D SetFromPoints(List<Vector2> points)
        {
            MoveTo(points[0].x, points[0].y);
            for (int i = 1; i < points.Count; i++)
            {
                LineTo(points[i].x, points[i].y);
            }
            return this;
        }

        public Path2D MoveTo(float x, float y)
        {
            currentPoint = new Vector2(x, y);
            return this;
        }

        public Path2D LineTo(float x, float y)
        {
            Add(new LineCurve(currentPoint, new Vector2(x, y)));
            currentPoint = new Vector2(x, y);
            return this;
        }

        public Path2D QuadraticCurveTo(float aCPx, float aCPy, float aX, float aY)
        {
            Add(new QuadraticBezierCurve(currentPoint, new Vector2(aCPx, aCPy), new Vector2(aX, aY)));
            currentPoint = new Vector2(aX, aY);
            return this;
        }

        public Path2D BezierCurveTo(float aCP1x, float aCP1y, float aCP2x, float aCP2y, float aX, float aY)
        {
            Add(new CubicBezierCurve(currentPoint, new Vector2(aCP1x, aCP1y), new Vector2(aCP2x, aCP2y), new Vector2(aX, aY)));
            currentPoint = new Vector2(aX, aY);
            return this;
        }

        public Path2D SplineThru(List<Vector2> pts)
        {
            var npts = new List<Vector2> { currentPoint };
            npts.AddRange(pts);
            Add(new SplineCurve(npts));
            currentPoint = pts[pts.Count - 1];
            return this;
        }

        /// <summary>Arc relative to the current point.</summary>
        public Path2D Arc(float aX, float aY, float aRadius, float aStartAngle, float aEndAngle, bool aClockwise)
        {
            return AbsArc(aX + currentPoint.x, aY + currentPoint.y, aRadius, aStartAngle, aEndAngle, aClockwise);
        }

        public Path2D AbsArc(float aX, float aY, float aRadius, float aStartAngle, float aEndAngle, bool aClockwise)
        {
            return AbsEllipse(aX, aY, aRadius, aRadius, aStartAngle, aEndAngle, aClockwise, 0);
        }

        public Path2D Ellipse(float aX, float aY, float xRadius, float yRadius, float aStartAngle, float aEndAngle, bool aClockwise, float aRotation)
        {
            return AbsEllipse(aX + currentPoint.x, aY + currentPoint.y, xRadius, yRadius, aStartAngle, aEndAngle, aClockwise, aRotation);
        }

        public Path2D AbsEllipse(float aX, float aY, float xRadius, float yRadius, float aStartAngle, float aEndAngle, bool aClockwise, float aRotation)
        {
            var curve = new EllipseCurve(aX, aY, xRadius, yRadius, aStartAngle, aEndAngle, aClockwise, aRotation);

            if (curves.Count > 0)
            {
                // ensure the arc is connected to whatever came before it
                var firstPoint = curve.GetPoint(0);
                if (firstPoint != (Vector3)currentPoint)
                {
                    LineTo(firstPoint.x, firstPoint.y);
                }
            }

            Add(curve);

            var lastPoint = curve.GetPoint(1);
            currentPoint = new Vector2(lastPoint.x, lastPoint.y);

            return this;
        }

        public Path2D ClosePath()
        {
            if (curves.Count > 0)
            {
                var startPoint = curves[0].GetPoint(0);
                if (new Vector2(startPoint.x, startPoint.y) != currentPoint)
                {
                    LineTo(startPoint.x, startPoint.y);
                }
            }
            return this;
        }

        /// <summary>Flattens the path to a 2D polyline at the given resolution per curve.</summary>
        public List<Vector2> GetPoints2D(int divisions = 12)
        {
            var points = new List<Vector2>();
            Vector2? last = null;

            foreach (var curve in curves)
            {
                // Straight segments need no subdivision; arcs are given proportionally more
                // samples so a quarter turn does not get the same budget as a full circle.
                int resolution = divisions;
                if (curve is LineCurve)
                {
                    resolution = 1;
                }
                else if (curve is EllipseCurve ellipse)
                {
                    resolution = divisions * 2;
                }
                else if (curve is SplineCurve spline)
                {
                    resolution = divisions * (spline.points.Count - 1);
                }

                var pts = curve.GetPoints(resolution);

                foreach (var p in pts)
                {
                    var p2 = new Vector2(p.x, p.y);
                    if (last.HasValue && last.Value == p2)
                    {
                        continue; // ensure no consecutive duplicates
                    }
                    points.Add(p2);
                    last = p2;
                }
            }

            if (points.Count > 1 && points[points.Count - 1] == points[0])
            {
                points.RemoveAt(points.Count - 1);
            }

            return points;
        }
    }
}
