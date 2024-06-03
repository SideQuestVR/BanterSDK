using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class Sphere : Geometry
    {
        public Sphere(float radius = 1, int widthSegments = 16, int heightSegments = 16, float phiStart = 0, float phiLength = Mathf.PI * 2f, float thetaStart = 0, float thetaLength = Mathf.PI)
        {

            indices = new List<int>();//[indexLength];
            vertices = new List<Vector3>();//[verticesLength];
            normals = new List<Vector3>();//[verticesLength];
            uvs = new List<Vector2>();//[verticesLength];
            int index = 0;
            var grid = new List<int[]>();

            var thetaEnd = thetaStart + thetaLength;

            for (int iy = 0; iy <= heightSegments; iy++)
            {

                var verticesRow = new int[widthSegments + 1];

                var v = iy / (float)heightSegments;

                for (int ix = 0; ix <= widthSegments; ix++)
                {

                    var u = ix / (float)widthSegments;
                    // vertex
                    Vector3 vertex = new Vector3();
                    vertex.x = -radius * Mathf.Cos(phiStart + u * phiLength) * Mathf.Sin(thetaStart + v * thetaLength);
                    vertex.y = radius * Mathf.Cos(thetaStart + v * thetaLength);
                    vertex.z = -(radius * Mathf.Sin(phiStart + u * phiLength) * Mathf.Sin(thetaStart + v * thetaLength));

                    vertices.Add(vertex);

                    normals.Add(vertex.normalized);

                    // uv

                    uvs.Add(new Vector2(u, 1 - v));

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
                        indices.Add(d);
                        indices.Add(b);
                        indices.Add(a);
                    }

                    if (iy != heightSegments - 1 || thetaEnd < Mathf.PI)
                    {
                        indices.Add(d);
                        indices.Add(c);
                        indices.Add(b);
                    }

                }

            }
        }
    }
}