using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class TorusKnot : Geometry
    {
        public TorusKnot(float radius = 0.5f, float tube = 0.4f, int radialSegments = 8, int tubularSegments = 16, int p = 2, int q = 3)
        {

            indices = new List<int>();//[indexLength];
            vertices = new List<Vector3>();//[verticesLength];
            normals = new List<Vector3>();//[verticesLength];
            uvs = new List<Vector2>();//[verticesLength];
            //var normal = new Vector3();

            var P1 = new Vector3();
            var P2 = new Vector3();

            var B = new Vector3();
            var T = new Vector3();
            var N = new Vector3();

            // generate vertices, normals and uvs

            for (int i = 0; i <= tubularSegments; ++i)
            {

                // the radian "u" is used to calculate the position on the torus curve of the current tubular segement

                var u = i / (float)tubularSegments * p * Mathf.PI * 2;

                // now we calculate two points. P1 is our current position on the curve, P2 is a little farther ahead.
                // these points are used to create a special "coordinate space", which is necessary to calculate the correct vertex positions

                calculatePositionOnCurve(u, p, q, radius, ref P1);
                calculatePositionOnCurve(u + 0.01f, p, q, radius, ref P2);

                // calculate orthonormal basis
                T = P2 - P1;
                N = P2 + P1;
                B = Vector3.Cross(T, N);
                N = Vector3.Cross(B, T);
                //T.subVectors(P2, P1);
                //N.addVectors(P2, P1);
                //B.crossVectors(T, N);
                //N.crossVectors(B, T);

                // normalize B, N. T can be ignored, we don't use it

                B.Normalize();
                N.Normalize();

                for (int j = 0; j <= radialSegments; ++j)
                {

                    // now calculate the vertices. they are nothing more than an extrusion of the torus curve.
                    // because we extrude a shape in the xy-plane, there is no need to calculate a z-value.

                    var v = j / (float)radialSegments * Mathf.PI * 2;
                    var cx = -tube * Mathf.Cos(v);
                    var cy = tube * Mathf.Sin(v);

                    // now calculate the final vertex position.
                    // first we orient the extrusion with our basis vectos, then we add it to the current position on the curve

                    var vertex = new Vector3(P1.x + (cx * N.x + cy * B.x), P1.y + (cx * N.y + cy * B.y), P1.z + (cx * N.z + cy * B.z));

                    vertices.Add(vertex);

                    // normal (P1 is always the center/origin of the extrusion, thus we can use it to calculate the normal)

                    //normal.subVectors(vertex, P1).normalize();
                    Vector3 normal = vertex - P1;
                    normal.Normalize();
                    normals.Add(normal);

                    // uv
                    uvs.Add(new Vector2(i / (float)tubularSegments, j / (float)radialSegments));
                    //uvs.push(i / tubularSegments);
                    //uvs.push(j / radialSegments);

                }

            }

            // generate indices

            for (int j = 1; j <= tubularSegments; j++)
            {

                for (int i = 1; i <= radialSegments; i++)
                {

                    // indices

                    var a = (radialSegments + 1) * (j - 1) + (i - 1);
                    var b = (radialSegments + 1) * j + (i - 1);
                    var c = (radialSegments + 1) * j + i;
                    var d = (radialSegments + 1) * (j - 1) + i;

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

        void calculatePositionOnCurve(float u, int p, int q, float radius, ref Vector3 position)
        {

            var cu = Mathf.Cos(u);
            var su = Mathf.Sin(u);
            var quOverP = (float)q / (float)p * u;
            var cs = Mathf.Cos(quOverP);

            position.x = radius * (2 + cs) * 0.5f * cu;
            position.y = radius * (2 + cs) * su * 0.5f;
            position.z = radius * Mathf.Sin(quOverP) * 0.5f;

        }
    }
}
