using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js IcosahedronGeometry.
    public class Icosahedron : Polyhedron
    {
        static readonly float t = (1 + Mathf.Sqrt(5)) / 2;

        static List<Vector3> Vertices()
        {
            return new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
        }

        static List<int> Indices()
        {
            return new List<int>
            {
                0, 11, 5,   0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,
                1, 5, 9,    5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,
                3, 9, 4,    3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,
                4, 9, 5,    2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1
            };
        }

        public Icosahedron(float radius = 0.5f, float detail = 0)
            : base(Vertices(), Indices(), radius, detail)
        {
        }
    }
}
