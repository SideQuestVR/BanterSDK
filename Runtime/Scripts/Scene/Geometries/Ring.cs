using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js RingGeometry. Keep this a literal transcription; handedness is
    // handled once by ConvertToUnityHandedness.
    public class Ring : Geometry
    {
        public Ring(float innerRadius = 0.15f, float outerRadius = 0.5f, int thetaSegments = 32, int phiSegments = 1, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            thetaSegments = Math.Max(3, thetaSegments);
            phiSegments = Math.Max(1, phiSegments);

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();

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

                    var vertex = new Vector3(radius * Mathf.Cos(segment), radius * Mathf.Sin(segment), 0);

                    vertices.Add(vertex);

                    normals.Add(new Vector3(0, 0, 1));

                    uvs.Add(new Vector2((vertex.x / outerRadius + 1) / 2, (vertex.y / outerRadius + 1) / 2));
                }

                // increase the radius for next row of vertices

                radius += radiusStep;
            }

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

                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(d);

                    indices.Add(b);
                    indices.Add(c);
                    indices.Add(d);
                }
            }

            ConvertToUnityHandedness();
        }
    }
}
