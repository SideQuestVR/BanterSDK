using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Dodecahedron
{
    Vector3[] vertices;
    int[] indices;
    float radius;
    float detail;
    public Dodecahedron(float radius = 1, float detail = 0)
    {
        var t = (1 + Mathf.Sqrt(5)) / 2;
        var r = 1 / t;
        this.radius = radius;
        this.detail = detail;
        this.vertices = new Vector3[20]{
            new Vector3(- 1, - 1, - 1),
            new Vector3(- 1, - 1, 1),
            new Vector3(- 1, 1, - 1),
            new Vector3(- 1, 1, 1),
            new Vector3(1, - 1, - 1),
            new Vector3(1, - 1, 1),
            new Vector3(1, 1, - 1),
            new Vector3(1, 1, 1),
            new Vector3(0, - r, - t),
            new Vector3(0, - r, t),
            new Vector3(0, r, - t),
            new Vector3(0, r, t),
            new Vector3(- r, - t, 0),
            new Vector3(- r, t, 0),
            new Vector3(r, - t, 0),
            new Vector3(r, t, 0),
            new Vector3(- t, 0, - r),
            new Vector3(t, 0, - r),
            new Vector3(- t, 0, r),
            new Vector3(t, 0, r),
			// // (±1, ±1, ±1)
			// - 1, - 1, - 1,	- 1, - 1, 1,
			// - 1, 1, - 1, - 1, 1, 1,
			// 1, - 1, - 1, 1, - 1, 1,
			// 1, 1, - 1, 1, 1, 1,

			// (0, ±1/φ, ±φ)
			// 0, - r, - t, 0, - r, t,
			// 0, r, - t, 0, r, t,

			// (±1/φ, ±φ, 0)
			// - r, - t, 0, - r, t, 0,
			// r, - t, 0, r, t, 0,

			// (±φ, 0, ±1/φ)
			// - t, 0, - r, t, 0, - r,
			// - t, 0, r, t, 0, r
        };

        this.indices = new int[]{
            3, 11, 7,   3, 7, 15,   3, 15, 13,
            7, 19, 17,  7, 17, 6,   7, 6, 15,
            17, 4, 8,   17, 8, 10,  17, 10, 6,
            8, 0, 16,   8, 16, 2,   8, 2, 10,
            0, 12, 1,   0, 1, 18,   0, 18, 16,
            6, 10, 2,   6, 2, 13,   6, 13, 15,
            2, 16, 18,  2, 18, 3,   2, 3, 13,
            18, 1, 9,   18, 9, 11,  18, 11, 3,
            4, 14, 12,  4, 12, 0,   4, 0, 8,
            11, 9, 5,   11, 5, 19,  11, 19, 7,
            19, 5, 14,  19, 14, 4,  19, 4, 17,
            1, 12, 14,  1, 14, 5,   1, 5, 9
        };


    }

    public Mesh generate()
    {
        return new Polyhedron(new List<Vector3>(vertices), new List<int>(indices), radius, detail).generate();
    }

}
