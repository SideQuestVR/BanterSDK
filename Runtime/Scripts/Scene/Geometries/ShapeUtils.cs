using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js src/extras/ShapeUtils.js. Keeps upstream's flattened
    // contour + holeIndices calling convention because that is what Earcut expects.
    public static class ShapeUtils
    {
        public static float Area(List<Vector2> contour)
        {
            int n = contour.Count;
            float a = 0f;

            for (int p = n - 1, q = 0; q < n; p = q++)
            {
                a += contour[p].x * contour[q].y - contour[q].x * contour[p].y;
            }

            return a * 0.5f;
        }

        public static bool IsClockWise(List<Vector2> pts)
        {
            return Area(pts) < 0;
        }

        /// <summary>
        /// Triangulates an outline with optional holes. Returns triangles as index triples into
        /// the concatenation of contour followed by each hole, in order.
        /// </summary>
        public static List<int> TriangulateShape(List<Vector2> contour, List<List<Vector2>> holes)
        {
            var vertices = new List<double>();
            var holeIndices = new List<int>();

            RemoveDupEndPts(contour);
            AddContour(vertices, contour);

            int holeIndex = contour.Count;

            if (holes != null)
            {
                foreach (var hole in holes)
                {
                    RemoveDupEndPts(hole);
                    holeIndices.Add(holeIndex);
                    holeIndex += hole.Count;
                    AddContour(vertices, hole);
                }
            }

            return Earcut.Triangulate(vertices, holeIndices);
        }

        static void RemoveDupEndPts(List<Vector2> points)
        {
            int l = points.Count;
            if (l > 2 && points[l - 1] == points[0])
            {
                points.RemoveAt(l - 1);
            }
        }

        static void AddContour(List<double> vertices, List<Vector2> contour)
        {
            foreach (var p in contour)
            {
                vertices.Add(p.x);
                vertices.Add(p.y);
            }
        }
    }
}
