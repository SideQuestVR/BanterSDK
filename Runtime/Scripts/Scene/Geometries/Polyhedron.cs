using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js PolyhedronGeometry. Subdivides each face, projects the result onto a
    // sphere of the given radius, and produces an unindexed triangle soup - so the index list is
    // sequential rather than the caller's face list.
    public class Polyhedron : Geometry
    {
        List<Vector3> vertexBuffer = new List<Vector3>();
        List<Vector2> uvBuffer = new List<Vector2>();

        public Polyhedron(List<Vector3> sourceVertices, List<int> sourceIndices, float radius = 0.5f, float detail = 0)
        {
            Subdivide(sourceVertices, sourceIndices, detail);

            // all vertices should lie on a conceptual sphere with a given radius
            ApplyRadius(radius);

            GenerateUVs();

            vertices = vertexBuffer;
            uvs = uvBuffer;

            // Subdivision produces a triangle soup, so the caller's face indices no longer
            // address it. Every three consecutive vertices are one triangle.
            indices = new List<int>(vertexBuffer.Count);
            for (int i = 0; i < vertexBuffer.Count; i++)
            {
                indices.Add(i);
            }

            normals = new List<Vector3>(vertexBuffer.Count);
            if (detail == 0)
            {
                // flat shading, matching three.js computeVertexNormals on an unindexed buffer
                for (int i = 0; i + 2 < vertexBuffer.Count; i += 3)
                {
                    var n = Vector3.Cross(vertexBuffer[i + 1] - vertexBuffer[i], vertexBuffer[i + 2] - vertexBuffer[i]).normalized;
                    normals.Add(n);
                    normals.Add(n);
                    normals.Add(n);
                }
            }
            else
            {
                // smooth shading - every vertex sits on the sphere, so its position is its normal
                for (int i = 0; i < vertexBuffer.Count; i++)
                {
                    normals.Add(vertexBuffer[i].normalized);
                }
            }

            ConvertToUnityHandedness();
        }

        void Subdivide(List<Vector3> sourceVertices, List<int> sourceIndices, float detail)
        {
            // iterate over all faces and apply a subdivison with the given detail value
            for (int i = 0; i < sourceIndices.Count; i += 3)
            {
                var a = sourceVertices[sourceIndices[i + 0]];
                var b = sourceVertices[sourceIndices[i + 1]];
                var c = sourceVertices[sourceIndices[i + 2]];

                SubdivideFace(a, b, c, detail);
            }
        }

        void SubdivideFace(Vector3 a, Vector3 b, Vector3 c, float detail)
        {
            var cols = Mathf.Max(1, Mathf.RoundToInt(detail) + 1);

            // we use this multidimensional array as a data structure for creating the subdivision
            var v = new List<List<Vector3>>();

            for (int i = 0; i <= cols; i++)
            {
                v.Add(new List<Vector3>());

                var aj = Vector3.Lerp(a, c, i / (float)cols);
                var bj = Vector3.Lerp(b, c, i / (float)cols);

                var rows = cols - i;

                for (int j = 0; j <= rows; j++)
                {
                    if (j == 0 && i == cols)
                    {
                        v[i].Add(aj);
                    }
                    else
                    {
                        v[i].Add(Vector3.Lerp(aj, bj, j / (float)rows));
                    }
                }
            }

            // construct all of the faces
            for (int i = 0; i < cols; i++)
            {
                for (int j = 0; j < 2 * (cols - i) - 1; j++)
                {
                    var k = j / 2;

                    if (j % 2 == 0)
                    {
                        vertexBuffer.Add(v[i][k + 1]);
                        vertexBuffer.Add(v[i + 1][k]);
                        vertexBuffer.Add(v[i][k]);
                    }
                    else
                    {
                        vertexBuffer.Add(v[i][k + 1]);
                        vertexBuffer.Add(v[i + 1][k + 1]);
                        vertexBuffer.Add(v[i + 1][k]);
                    }
                }
            }
        }

        void ApplyRadius(float radius)
        {
            for (int i = 0; i < vertexBuffer.Count; i++)
            {
                vertexBuffer[i] = vertexBuffer[i].normalized * radius;
            }
        }

        void GenerateUVs()
        {
            for (int i = 0; i < vertexBuffer.Count; i++)
            {
                var u = Azimuth(vertexBuffer[i]) / 2 / Mathf.PI + 0.5f;
                var v = Inclination(vertexBuffer[i]) / Mathf.PI + 0.5f;
                uvBuffer.Add(new Vector2(u, 1 - v));
            }

            CorrectUVs();
            CorrectSeam();
        }

        void CorrectSeam()
        {
            // handle case when face straddles the seam, see three.js #3269
            for (int i = 0; i + 2 < uvBuffer.Count; i += 3)
            {
                var x0 = uvBuffer[i + 0].x;
                var x1 = uvBuffer[i + 1].x;
                var x2 = uvBuffer[i + 2].x;

                var max = Mathf.Max(x0, x1, x2);
                var min = Mathf.Min(x0, x1, x2);

                // 0.9 is somewhat arbitrary
                if (max > 0.9f && min < 0.1f)
                {
                    if (x0 < 0.2f) { uvBuffer[i + 0] = new Vector2(x0 + 1, uvBuffer[i + 0].y); }
                    if (x1 < 0.2f) { uvBuffer[i + 1] = new Vector2(x1 + 1, uvBuffer[i + 1].y); }
                    if (x2 < 0.2f) { uvBuffer[i + 2] = new Vector2(x2 + 1, uvBuffer[i + 2].y); }
                }
            }
        }

        void CorrectUVs()
        {
            for (int i = 0; i + 2 < vertexBuffer.Count; i += 3)
            {
                var a = vertexBuffer[i];
                var b = vertexBuffer[i + 1];
                var c = vertexBuffer[i + 2];

                var centroid = (a + b + c) / 3;
                var azi = Azimuth(centroid);

                CorrectUV(i + 0, a, azi);
                CorrectUV(i + 1, b, azi);
                CorrectUV(i + 2, c, azi);
            }
        }

        void CorrectUV(int stride, Vector3 vector, float azimuth)
        {
            var uv = uvBuffer[stride];

            if (azimuth < 0 && uv.x == 1)
            {
                uvBuffer[stride] = new Vector2(uv.x - 1, uv.y);
            }

            if (vector.x == 0 && vector.z == 0)
            {
                uvBuffer[stride] = new Vector2(azimuth / 2 / Mathf.PI + 0.5f, uv.y);
            }
        }

        // Angle around the Y axis, counter-clockwise when looking from above.
        static float Azimuth(Vector3 vector)
        {
            return Mathf.Atan2(vector.z, -vector.x);
        }

        // Angle above the XZ plane.
        static float Inclination(Vector3 vector)
        {
            return Mathf.Atan2(-vector.y, Mathf.Sqrt((vector.x * vector.x) + (vector.z * vector.z)));
        }
    }
}
