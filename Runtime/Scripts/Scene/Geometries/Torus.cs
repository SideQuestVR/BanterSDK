using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK{
    public class Torus : Geometry
    {


        public Torus(float radius = 0.5f, float tube = 0.4f, int radialSegments = 8, int tubularSegments = 16, float arc = Mathf.PI * 2)
        {
            indices = new List<int>();//[indexLength];
            vertices = new List<Vector3>();//[verticesLength];
            normals = new List<Vector3>();//[verticesLength];
            uvs = new List<Vector2>();//[verticesLength];
            var center = new Vector3();
            // generate vertices, normals and uvs

            for (int j = 0; j <= radialSegments; j++)
            {

                for (int i = 0; i <= tubularSegments; i++)
                {

                    var u = i / (float)tubularSegments * arc;
                    var v = j / (float)radialSegments * Mathf.PI * 2;

                    // vertex

                    var vertex = new Vector3((radius + tube * Mathf.Cos(v)) * Mathf.Cos(u), (radius + tube * Mathf.Cos(v)) * Mathf.Sin(u), tube * Mathf.Sin(v));

                    vertices.Add(vertex);

                    // normal

                    center.x = radius * Mathf.Cos(u);
                    center.y = radius * Mathf.Sin(u);
                    Vector3 normal = vertex - center;
                    normal.Normalize();

                    normals.Add(normal);

                    // uv
                    uvs.Add(new Vector2(i / (float)tubularSegments, j / (float)radialSegments));
                }

            }

            // generate indices

            for (int j = 1; j <= radialSegments; j++)
            {

                for (int i = 1; i <= tubularSegments; i++)
                {

                    // indices

                    var a = (tubularSegments + 1) * j + i - 1;
                    var b = (tubularSegments + 1) * (j - 1) + i - 1;
                    var c = (tubularSegments + 1) * (j - 1) + i;
                    var d = (tubularSegments + 1) * j + i;

                    // faces

                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(d);

                    indices.Add(b);
                    indices.Add(c);
                    indices.Add(d);

                }

            }
        }
    }
}