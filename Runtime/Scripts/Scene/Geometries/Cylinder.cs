using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class Cylinder : Geometry
    {
        int index;

        public Cylinder(float radiusTop = 1, float radiusBottom = 1, float height = 1, int radialSegments = 8, int heightSegments = 2, bool openEnded = false, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            indices = new List<int>();//[indexLength];
            vertices = new List<Vector3>();//[verticesLength];
            normals = new List<Vector3>();//[verticesLength];
            uvs = new List<Vector2>();//[verticesLength];
            index = 0;

            GenerateTorso(radiusTop, radiusBottom, height, heightSegments, radialSegments, thetaStart - Mathf.PI, thetaLength);

            if (!openEnded)
            {
                GenerateCap(radiusTop, radiusBottom, height, heightSegments, radialSegments, thetaStart - Mathf.PI, thetaLength, true);
                GenerateCap(radiusTop, radiusBottom, height, heightSegments, radialSegments, thetaStart - Mathf.PI, thetaLength, false);
            }
        }
        void GenerateTorso(float radiusTop, float radiusBottom, float height, int heightSegments, int radialSegments, float thetaStart, float thetaLength)
        {

            var halfHeight = height / 2;
            // this will be used to calculate the normal
            var slope = (radiusBottom - radiusTop) / height;

            // generate vertices, normals and uvs

            var indexArray = new List<int[]>();

            for (int y = heightSegments; y >= 0; y--)
            {

                var indexRow = new int[radialSegments + 1];

                var v = y / (float)heightSegments;

                // calculate the radius of the current row

                var radius = v * (radiusBottom - radiusTop) + radiusTop;

                for (int x = radialSegments; x >= 0; x--)
                {

                    var u = x / (float)radialSegments;

                    var theta = thetaStart - (u * thetaLength);

                    var sinTheta = Mathf.Sin(theta);
                    var cosTheta = Mathf.Cos(theta);

                    // vertex
                    var vertex = new Vector3();

                    vertex.x = radius * sinTheta;
                    vertex.y = -v * height + halfHeight;
                    vertex.z = radius * cosTheta;
                    vertices.Add(vertex);

                    // normal

                    var normal = new Vector3();
                    normal.x = sinTheta;
                    normal.y = slope;
                    normal.z = cosTheta;
                    normal.Normalize();
                    normals.Add(normal);

                    // uv

                    uvs.Add(new Vector2(1 - u, 1 - v));

                    // save index of vertex in respective row

                    indexRow[x] = index++;

                }

                // now save vertices of the row in our index array

                indexArray.Add(indexRow);

            }

            // generate indices

            for (int x = radialSegments - 1; x >= 0; x--)
            {

                for (int y = heightSegments - 1; y >= 0; y--)
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

        void GenerateCap(float radiusTop, float radiusBottom, float height, int heightSegments, int radialSegments, float thetaStart, float thetaLength, bool top)
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

                // vertex

                vertices.Add(new Vector3(0, halfHeight * sign, 0));

                // normal

                normals.Add(new Vector3(0, sign, 0));

                // uv

                uvs.Add(new Vector2(0.5f, 0.5f));

                // increase index

                index++;

            }

            // save the index of the last center vertex

            int centerIndexEnd = index;

            // now we generate the surrounding vertices, normals and uvs

            for (int x = radialSegments; x >= 0; x--)
            {

                var u = x / (float)radialSegments;
                var theta = thetaStart - (u * thetaLength);

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

                //uv.x = (cosTheta * 0.5) + 0.5;
                //uv.y = (sinTheta * 0.5 * sign) + 0.5;
                uvs.Add(new Vector2(1 - ((cosTheta * 0.5f) + 0.5f), ((sinTheta * 0.5f * sign) + 0.5f)));

                // increase index

                index++;

            }

            // generate indices

            for (int x = radialSegments - 1; x >= 0; x--)
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
