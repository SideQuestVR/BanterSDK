using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK{
    public class Cone : Geometry
    {

        int index;

        public Cone(float radius = 0.5f, float height = 1, int radialSegments = 8, int heightSegments = 2, bool openEnded = false, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            var cylinder = new Cylinder(0, radius, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength);
            indices = cylinder.indices;
            vertices = cylinder.vertices;
            normals = cylinder.normals;
            uvs = cylinder.uvs;
        }
    }
}
