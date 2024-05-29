using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK{
    public class Box : Geometry
    {

        int numberOfVertices = 0;

        public Box(float width = 1, float height = 1, float depth = 1, int widthSegments = 1, int heightSegments = 1, int depthSegments = 1)
        {
            widthSegments = Math.Clamp(widthSegments, 1, int.MaxValue);
            heightSegments = Math.Clamp(heightSegments, 1, int.MaxValue);
            depthSegments = Math.Clamp(depthSegments, 1, int.MaxValue);

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();

            // build each side of the box geometry

            BuildPlane("z", "y", "x", -1, -1, depth, height, width, depthSegments, heightSegments, 0); // px
            BuildPlane("z", "y", "x", 1, -1, depth, height, -width, depthSegments, heightSegments, 1); // nx
            BuildPlane("x", "z", "y", 1, 1, width, depth, height, widthSegments, depthSegments, 2); // py
            BuildPlane("x", "z", "y", 1, -1, width, depth, -height, widthSegments, depthSegments, 3); // ny
            BuildPlane("x", "y", "z", 1, -1, width, height, depth, widthSegments, heightSegments, 4); // pz
            BuildPlane("x", "y", "z", -1, -1, width, height, -depth, widthSegments, heightSegments, 5); // nz

        }
        void BuildPlane(string u, string v, string w, int udir, int vdir, float width, float height, float depth, int gridX, int gridY, int side)
        {
            float segmentWidth = width / gridX;
            float segmentHeight = height / gridY;

            var widthHalf = width / 2f;
            var heightHalf = height / 2f;
            var depthHalf = depth / 2f;

            var gridX1 = gridX + 1;
            var gridY1 = gridY + 1;
            int vertexCounter = 0;

            for (int iy = 0; iy < gridY1; iy++)
            {

                float y = iy * segmentHeight - heightHalf;

                for (int ix = 0; ix < gridX1; ix++)
                {

                    float x = ix * segmentWidth - widthHalf;

                    vertices.Add(MakePlaneVector(u, v, w, (float)x * udir, (float)y * vdir, depthHalf));

                    normals.Add(MakePlaneVector(u, v, w, 0, 0, depth > 0 ? 1 : -1));

                    uvs.Add(new Vector2(1 - (ix / gridX), 1 - (iy / gridY)));

                    vertexCounter++;
                }

            }

            for (int iy = 0; iy < gridY; iy++)
            {

                for (int ix = 0; ix < gridX; ix++)
                {

                    var a = numberOfVertices + ix + gridX1 * iy;
                    var b = numberOfVertices + ix + gridX1 * (iy + 1);
                    var c = numberOfVertices + (ix + 1) + gridX1 * (iy + 1);
                    var d = numberOfVertices + (ix + 1) + gridX1 * iy;

                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(d);
                    indices.Add(b);
                    indices.Add(c);
                    indices.Add(d);

                }

            }
            numberOfVertices += vertexCounter;
        }
        Vector3 MakePlaneVector(string u, string v, string w, float x, float y, float z)
        {
            Vector3 vector = new Vector3();

            switch (u)
            {
                case "x":
                    vector.x = x;
                    break;
                case "y":
                    vector.y = x;
                    break;
                case "z":
                    vector.z = x;
                    break;
            }

            switch (v)
            {
                case "x":
                    vector.x = y;
                    break;
                case "y":
                    vector.y = y;
                    break;
                case "z":
                    vector.z = y;
                    break;
            }

            switch (w)
            {
                case "x":
                    vector.x = z;
                    break;
                case "y":
                    vector.y = z;
                    break;
                case "z":
                    vector.z = z;
                    break;
            }
            return vector;
        }
    }
}
