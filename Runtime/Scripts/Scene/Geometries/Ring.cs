using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class Ring : Geometry
    {
        public Ring(float innerRadius = 0.3f, float outerRadius = 1f, int thetaSegments = 24, int phiSegments = 8, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            indices = new List<int>();//[indexLength];
            vertices = new List<Vector3>();//[verticesLength];
            normals = new List<Vector3>();//[verticesLength];
            uvs = new List<Vector2>();//[verticesLength];

            float segment;
            int segmentIndex;
            float radius = innerRadius;
            float radiusStep = ((outerRadius - innerRadius) / phiSegments);

            for (int j = 0; j <= phiSegments; j++)
            {

                for (int i = 0; i <= thetaSegments; i++)
                {

                    // values are generate from the inside of the ring to the outside

                    segment = thetaStart + i / (float)thetaSegments * thetaLength;

                    // vertex
                    var vertex = new Vector3(radius * Mathf.Cos(segment), radius * Mathf.Sin(segment), 0);

                    vertices.Add(vertex);

                    // normal

                    normals.Add(new Vector3(0, 0, 1));

                    // uv

                    uvs.Add(new Vector2((vertex.x / outerRadius + 1) / 2, (vertex.y / outerRadius + 1) / 2));

                }

                // increase the radius for next row of vertices

                radius += radiusStep;

            }

            // indices

            for (int j = 0; j < phiSegments; j++)
            {

                int thetaSegmentLevel = j * (thetaSegments + 1);

                for (int i = 0; i < thetaSegments; i++)
                {

                    segmentIndex = i + thetaSegmentLevel;

                    var a = segmentIndex;
                    var b = segmentIndex + thetaSegments + 1;
                    var c = segmentIndex + thetaSegments + 2;
                    var d = segmentIndex + 1;

                    // faces
                    indices.Add(b);
                    indices.Add(a);
                    indices.Add(d);

                    indices.Add(c);
                    indices.Add(b);
                    indices.Add(d);

                }

            }
        }
    }
}
