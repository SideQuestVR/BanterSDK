using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class Circle : Geometry
{


    public Circle(float radius = 1, int segments = 24, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
    {

        indices = new List<int>();
        vertices = new List<Vector3>();
        normals = new List<Vector3>();
        uvs = new List<Vector2>();

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
            //int index = (i - 1) * 3;

            indices.Add(0);
            indices.Add(i + 1);
            indices.Add(i);
        }

    }
}