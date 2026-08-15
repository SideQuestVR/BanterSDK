using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js LatheGeometry - revolves a 2D profile around the Y axis. Keep this a
    // literal transcription; handedness is handled once by ConvertToUnityHandedness.
    public class Lathe : Geometry
    {
        public Lathe(List<Vector2> points, int segments = 12, float phiStart = 0, float phiLength = Mathf.PI * 2)
        {
            segments = Math.Max(1, segments);

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();

            if (points == null || points.Count < 2)
            {
                return;
            }

            // clamp phiLength so the surface never wraps past a full turn
            phiLength = Mathf.Clamp(phiLength, 0, Mathf.PI * 2);

            var inverseSegments = 1.0f / segments;

            // pre-compute normals for initial "meridian"
            var initNormals = new Vector3[points.Count];
            var normal = Vector3.zero;
            var curNormal = Vector3.zero;
            var prevNormal = Vector3.zero;

            for (int j = 0; j < points.Count; j++)
            {
                if (j == 0)
                {
                    // first vertex
                    normal = new Vector3(points[j + 1].y - points[j].y, -(points[j + 1].x - points[j].x), 0).normalized;
                    curNormal = normal;
                    initNormals[j] = curNormal;
                    prevNormal = normal;
                }
                else if (j == points.Count - 1)
                {
                    // last vertex
                    initNormals[j] = prevNormal;
                }
                else
                {
                    // vertices in between average the two adjacent face normals
                    normal = new Vector3(points[j + 1].y - points[j].y, -(points[j + 1].x - points[j].x), 0).normalized;
                    curNormal = (prevNormal + normal).normalized;
                    initNormals[j] = curNormal;
                    prevNormal = normal;
                }
            }

            for (int i = 0; i <= segments; i++)
            {
                var phi = phiStart + i * inverseSegments * phiLength;

                var sin = Mathf.Sin(phi);
                var cos = Mathf.Cos(phi);

                for (int j = 0; j < points.Count; j++)
                {
                    vertices.Add(new Vector3(points[j].x * sin, points[j].y, points[j].x * cos));

                    uvs.Add(new Vector2(i / (float)segments, j / (float)(points.Count - 1)));

                    var n = initNormals[j];
                    normals.Add(new Vector3(n.x * sin, n.y, n.x * cos).normalized);
                }
            }

            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < points.Count - 1; j++)
                {
                    var baseIndex = j + i * points.Count;

                    var a = baseIndex;
                    var b = baseIndex + points.Count;
                    var c = baseIndex + points.Count + 1;
                    var d = baseIndex + 1;

                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(d);

                    indices.Add(c);
                    indices.Add(d);
                    indices.Add(b);
                }
            }

            ConvertToUnityHandedness();
        }
    }
}
