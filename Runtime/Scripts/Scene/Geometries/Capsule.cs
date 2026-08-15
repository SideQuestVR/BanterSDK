using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // A capsule as three.js builds it: a surface of revolution over a profile made of two
    // hemispherical caps joined by a cylindrical section. Written directly rather than via
    // LatheGeometry so it does not depend on the curve infrastructure.
    //
    // `length` is the cylindrical section only, so the total height is length + 2 * radius.
    // The defaults give 0.5 x 1 x 0.5.
    public class Capsule : Geometry
    {
        public Capsule(float radius = 0.25f, float length = 0.5f, int capSegments = 8, int radialSegments = 32)
        {
            capSegments = Math.Max(1, capSegments);
            radialSegments = Math.Max(3, radialSegments);

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();

            var halfLength = length / 2;

            // Profile rows, pole to pole. The equator is emitted twice - once at the bottom cap's
            // centre and once at the top's - and the quad between those two rows is the cylinder.
            var rowCentres = new List<float>();
            var rowAngles = new List<float>();

            for (int i = 0; i <= capSegments; i++)
            {
                rowAngles.Add(-Mathf.PI / 2 + (Mathf.PI / 2) * (i / (float)capSegments));
                rowCentres.Add(-halfLength);
            }
            for (int i = 0; i <= capSegments; i++)
            {
                rowAngles.Add((Mathf.PI / 2) * (i / (float)capSegments));
                rowCentres.Add(halfLength);
            }

            int rows = rowAngles.Count;

            for (int iy = 0; iy < rows; iy++)
            {
                var theta = rowAngles[iy];
                var centreY = rowCentres[iy];
                var ringRadius = radius * Mathf.Cos(theta);
                var offsetY = radius * Mathf.Sin(theta);

                var v = iy / (float)(rows - 1);

                for (int ix = 0; ix <= radialSegments; ix++)
                {
                    var u = ix / (float)radialSegments;
                    var phi = u * Mathf.PI * 2;

                    var x = ringRadius * Mathf.Sin(phi);
                    var z = ringRadius * Mathf.Cos(phi);

                    vertices.Add(new Vector3(x, centreY + offsetY, z));

                    // Normal is measured from the cap centre, which is what keeps the two
                    // hemispheres smooth and the cylinder wall perpendicular.
                    normals.Add(new Vector3(x, offsetY, z).normalized);

                    uvs.Add(new Vector2(u, v));
                }
            }

            int rowStride = radialSegments + 1;

            for (int iy = 0; iy < rows - 1; iy++)
            {
                for (int ix = 0; ix < radialSegments; ix++)
                {
                    var a = iy * rowStride + ix;
                    var b = (iy + 1) * rowStride + ix;
                    var c = (iy + 1) * rowStride + ix + 1;
                    var d = iy * rowStride + ix + 1;

                    // Rows run bottom to top while the ring runs clockwise in xz, which is the
                    // opposite parameter handedness to the three.js grid builders, so the winding
                    // is mirrored relative to theirs.
                    indices.Add(a);
                    indices.Add(d);
                    indices.Add(b);

                    indices.Add(b);
                    indices.Add(d);
                    indices.Add(c);
                }
            }

            ConvertToUnityHandedness();
        }
    }
}
