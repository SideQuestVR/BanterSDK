using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js TetrahedronGeometry.
    public class Tetrahedron : Polyhedron
    {
        static List<Vector3> Vertices()
        {
            return new List<Vector3>
            {
                new Vector3(1, 1, 1), new Vector3(-1, -1, 1),
                new Vector3(-1, 1, -1), new Vector3(1, -1, -1)
            };
        }

        static List<int> Indices()
        {
            return new List<int>
            {
                2, 1, 0,   0, 3, 2,   1, 3, 0,   2, 3, 1
            };
        }

        public Tetrahedron(float radius = 0.5f, float detail = 0)
            : base(Vertices(), Indices(), radius, detail)
        {
        }
    }
}
