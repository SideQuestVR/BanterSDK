using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js OctahedronGeometry.
    public class Octahedron : Polyhedron
    {
        static List<Vector3> Vertices()
        {
            return new List<Vector3>
            {
                new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0),
                new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1)
            };
        }

        static List<int> Indices()
        {
            return new List<int>
            {
                0, 2, 4,   0, 4, 3,   0, 3, 5,   0, 5, 2,
                1, 2, 5,   1, 5, 3,   1, 3, 4,   1, 4, 2
            };
        }

        public Octahedron(float radius = 0.5f, float detail = 0)
            : base(Vertices(), Indices(), radius, detail)
        {
        }
    }
}
