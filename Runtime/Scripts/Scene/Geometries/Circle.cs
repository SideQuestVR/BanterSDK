using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js CircleGeometry. Keep this a literal transcription; handedness is
    // handled once by ConvertToUnityHandedness.
    public class Circle : Geometry
    {
        public Circle(float radius = 0.5f, int segments = 32, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            segments = Math.Max(3, segments);

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();

            // center point
            vertices.Add(new Vector3());
            normals.Add(new Vector3(0, 0, 1));
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int s = 0; s <= segments; s++)
            {
                float segment = thetaStart + s / (float)segments * thetaLength;

                Vector3 vertex = new Vector3(radius * Mathf.Cos(segment), radius * Mathf.Sin(segment), 0);

                vertices.Add(vertex);

                normals.Add(new Vector3(0, 0, 1));

                uvs.Add(new Vector2(
                    (vertex.x / radius + 1) / 2,
                    (vertex.y / radius + 1) / 2
                ));
            }

            for (int i = 1; i <= segments; i++)
            {
                indices.Add(i);
                indices.Add(i + 1);
                indices.Add(0);
            }

            ConvertToUnityHandedness();
        }
    }
}
