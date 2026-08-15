using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js PlaneGeometry. Keep this a literal transcription; handedness is
    // handled once by ConvertToUnityHandedness.
    public class Plane : Geometry
    {
        public Plane(float width = 1, float height = 1, int widthSegments = 1, int heightSegments = 1)
        {
            var widthHalf = width / 2;
            var heightHalf = height / 2;

            var gridX = Math.Max(1, widthSegments);
            var gridY = Math.Max(1, heightSegments);

            var gridX1 = gridX + 1;
            var gridY1 = gridY + 1;

            var segmentWidth = width / gridX;
            var segmentHeight = height / gridY;

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();

            for (int iy = 0; iy < gridY1; iy++)
            {
                var y = iy * segmentHeight - heightHalf;

                for (int ix = 0; ix < gridX1; ix++)
                {
                    var x = ix * segmentWidth - widthHalf;

                    vertices.Add(new Vector3(x, -y, 0));

                    normals.Add(new Vector3(0, 0, 1));

                    uvs.Add(new Vector2(ix / (float)gridX, 1 - iy / (float)gridY));
                }
            }

            for (int iy = 0; iy < gridY; iy++)
            {
                for (int ix = 0; ix < gridX; ix++)
                {
                    var a = ix + gridX1 * iy;
                    var b = ix + gridX1 * (iy + 1);
                    var c = (ix + 1) + gridX1 * (iy + 1);
                    var d = (ix + 1) + gridX1 * iy;

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
