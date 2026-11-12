using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js CylinderGeometry. Keep this a literal transcription; handedness is
    // handled once by ConvertToUnityHandedness.
    public class Cylinder : Geometry
    {
        int index;

        public Cylinder(float radiusTop = 0.5f, float radiusBottom = 0.5f, float height = 1, int radialSegments = 32, int heightSegments = 1, bool openEnded = false, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            radialSegments = Math.Max(3, radialSegments);
            heightSegments = Math.Max(1, heightSegments);

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();
            index = 0;

            GenerateTorso(radiusTop, radiusBottom, height, heightSegments, radialSegments, thetaStart, thetaLength);

            if (!openEnded)
            {
                if (radiusTop > 0)
                {
                    GenerateCap(radiusTop, radiusBottom, height, radialSegments, thetaStart, thetaLength, true);
                }
                if (radiusBottom > 0)
                {
                    GenerateCap(radiusTop, radiusBottom, height, radialSegments, thetaStart, thetaLength, false);
                }
            }

            ConvertToUnityHandedness();
        }

        void GenerateTorso(float radiusTop, float radiusBottom, float height, int heightSegments, int radialSegments, float thetaStart, float thetaLength)
        {
            var halfHeight = height / 2;
            // this will be used to calculate the normal
            var slope = (radiusBottom - radiusTop) / height;

            // generate vertices, normals and uvs

            var indexArray = new List<int[]>();

            for (int y = 0; y <= heightSegments; y++)
            {
                var indexRow = new int[radialSegments + 1];

                var v = y / (float)heightSegments;

                // calculate the radius of the current row

                var radius = v * (radiusBottom - radiusTop) + radiusTop;

                for (int x = 0; x <= radialSegments; x++)
                {
                    var u = x / (float)radialSegments;

                    var theta = u * thetaLength + thetaStart;

                    var sinTheta = Mathf.Sin(theta);
                    var cosTheta = Mathf.Cos(theta);

                    // vertex
                    var vertex = new Vector3();
                    vertex.x = radius * sinTheta;
                    vertex.y = -v * height + halfHeight;
                    vertex.z = radius * cosTheta;
                    vertices.Add(vertex);

                    // normal

                    var normal = new Vector3(sinTheta, slope, cosTheta);
                    normal.Normalize();
                    normals.Add(normal);

                    // uv

                    uvs.Add(new Vector2(u, 1 - v));

                    // save index of vertex in respective row

                    indexRow[x] = index++;
                }

                // now save vertices of the row in our index array

                indexArray.Add(indexRow);
            }

            // generate indices

            for (int x = 0; x < radialSegments; x++)
            {
                for (int y = 0; y < heightSegments; y++)
                {
                    // we use the index array to access the correct indices

                    var a = indexArray[y][x];
                    var b = indexArray[y + 1][x];
                    var c = indexArray[y + 1][x + 1];
                    var d = indexArray[y][x + 1];

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

        void GenerateCap(float radiusTop, float radiusBottom, float height, int radialSegments, float thetaStart, float thetaLength, bool top)
        {
            var halfHeight = height / 2;

            var radius = (top == true) ? radiusTop : radiusBottom;
            var sign = (top == true) ? 1 : -1;

            // save the index of the first center vertex
            int centerIndexStart = index;

            // first we generate the center vertex data of the cap.
            // because the geometry needs one set of uvs per face,
            // we must generate a center vertex per face/segment

            for (int x = 1; x <= radialSegments; x++)
            {
                vertices.Add(new Vector3(0, halfHeight * sign, 0));

                normals.Add(new Vector3(0, sign, 0));

                uvs.Add(new Vector2(0.5f, 0.5f));

                index++;
            }

            // save the index of the last center vertex

            int centerIndexEnd = index;

            // now we generate the surrounding vertices, normals and uvs

            for (int x = 0; x <= radialSegments; x++)
            {
                var u = x / (float)radialSegments;
                var theta = u * thetaLength + thetaStart;

                var cosTheta = Mathf.Cos(theta);
                var sinTheta = Mathf.Sin(theta);

                // vertex

                var vertex = new Vector3();
                vertex.x = radius * sinTheta;
                vertex.y = halfHeight * sign;
                vertex.z = radius * cosTheta;
                vertices.Add(vertex);

                // normal

                normals.Add(new Vector3(0, sign, 0));

                // uv

                uvs.Add(new Vector2((cosTheta * 0.5f) + 0.5f, (sinTheta * 0.5f * sign) + 0.5f));

                index++;
            }

            // generate indices

            for (int x = 0; x < radialSegments; x++)
            {
                var c = centerIndexStart + x;
                var i = centerIndexEnd + x;

                if (top == true)
                {
                    // face top
                    indices.Add(i);
                    indices.Add(i + 1);
                    indices.Add(c);
                }
                else
                {
                    // face bottom
                    indices.Add(i + 1);
                    indices.Add(i);
                    indices.Add(c);
                }
            }
        }
    }
}
