using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class Plane : Geometry
    {
        public Plane(float width = 1, float height = 1, int widthSegments = 1, int heightSegments = 1)
        {

            float width_half = width / 2;
            float height_half = height / 2;

            int gridX1 = widthSegments + 1;
            int gridY1 = heightSegments + 1;

            float segment_width = width / widthSegments;
            float segment_height = height / heightSegments;

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();


            for (int iy = 0; iy < gridY1; iy++)
            {

                var y = iy * segment_height - height_half;

                for (int ix = 0; ix < gridX1; ix++)
                {

                    var x = ix * segment_width - width_half;

                    vertices.Add(new Vector3(x, y, 0));

                    normals.Add(new Vector3(0, 0, 1));

                    uvs.Add(new Vector2(ix / widthSegments, iy / heightSegments));

                }

            }

            // indices

            for (int iy = 0; iy < widthSegments; iy++)
            {

                for (int ix = 0; ix < heightSegments; ix++)
                {

                    int a = ix + gridX1 * iy;
                    int b = ix + gridX1 * (iy + 1);
                    int c = (ix + 1) + gridX1 * (iy + 1);
                    int d = (ix + 1) + gridX1 * iy;
                    //int index = (iy * widthSegments + ix) * 6;
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
