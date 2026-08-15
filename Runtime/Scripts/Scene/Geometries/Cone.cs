using UnityEngine;

namespace BS
{
    // Ported from three.js ConeGeometry, which is a Cylinder with radiusTop forced to 0.
    // The Cylinder it delegates to has already converted handedness, so this must not do it again.
    public class Cone : Geometry
    {
        public Cone(float radius = 0.5f, float height = 1, int radialSegments = 32, int heightSegments = 1, bool openEnded = false, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            var cylinder = new Cylinder(0, radius, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength);
            indices = cylinder.indices;
            vertices = cylinder.vertices;
            normals = cylinder.normals;
            uvs = cylinder.uvs;
        }
    }
}
