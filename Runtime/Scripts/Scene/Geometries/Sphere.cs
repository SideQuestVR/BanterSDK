using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js SphereGeometry. Keep this a literal transcription; handedness is
    // handled once by ConvertToUnityHandedness.
    public class Sphere : Geometry
    {
        public Sphere(float radius = 0.5f, int widthSegments = 32, int heightSegments = 16, float phiStart = 0, float phiLength = Mathf.PI * 2f, float thetaStart = 0, float thetaLength = Mathf.PI)
        {
            widthSegments = Math.Max(3, widthSegments);
            heightSegments = Math.Max(2, heightSegments);

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();
            int index = 0;
            var grid = new List<int[]>();

            // Clamping here is what lets the pole guards below ever fire. Without it a caller
            // passing the shared thetaLength default of 2*PI sweeps the sphere twice and every
            // guard fails, emitting zero triangles.
            var thetaEnd = Mathf.Min(thetaStart + thetaLength, Mathf.PI);

            for (int iy = 0; iy <= heightSegments; iy++)
            {
                var verticesRow = new int[widthSegments + 1];

                var v = iy / (float)heightSegments;

                // special case for the poles
                float uOffset = 0;
                if (iy == 0 && thetaStart == 0)
                {
                    uOffset = 0.5f / widthSegments;
                }
                else if (iy == heightSegments && thetaEnd >= Mathf.PI)
                {
                    uOffset = -0.5f / widthSegments;
                }

                for (int ix = 0; ix <= widthSegments; ix++)
                {
                    var u = ix / (float)widthSegments;

                    // vertex
                    Vector3 vertex = new Vector3();
                    vertex.x = -radius * Mathf.Cos(phiStart + u * phiLength) * Mathf.Sin(thetaStart + v * thetaLength);
                    vertex.y = radius * Mathf.Cos(thetaStart + v * thetaLength);
                    vertex.z = radius * Mathf.Sin(phiStart + u * phiLength) * Mathf.Sin(thetaStart + v * thetaLength);

                    vertices.Add(vertex);

                    normals.Add(vertex.normalized);

                    uvs.Add(new Vector2(u + uOffset, 1 - v));

                    verticesRow[ix] = index++;
                }

                grid.Add(verticesRow);
            }

            // indices

            for (int iy = 0; iy < heightSegments; iy++)
            {
                for (int ix = 0; ix < widthSegments; ix++)
                {
                    var a = grid[iy][ix + 1];
                    var b = grid[iy][ix];
                    var c = grid[iy + 1][ix];
                    var d = grid[iy + 1][ix + 1];

                    if (iy != 0 || thetaStart > 0)
                    {
                        indices.Add(a);
                        indices.Add(b);
                        indices.Add(d);
                    }

                    if (iy != heightSegments - 1 || thetaEnd < Mathf.PI)
                    {
                        indices.Add(b);
                        indices.Add(c);
                        indices.Add(d);
                    }
                }
            }

            ConvertToUnityHandedness();
        }
    }
}
