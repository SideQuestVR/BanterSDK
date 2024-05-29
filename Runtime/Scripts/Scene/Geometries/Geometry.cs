using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace Banter.SDK{
    [System.Serializable]
    public class Geometry
    {
        public List<int> indices;
        public List<Vector3> vertices;
        public List<Vector3> normals;
        public List<Vector2> uvs;


        public Mesh generate()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.triangles = indices.ToArray();
            mesh.uv = uvs.ToArray();
            return mesh;
        }
    }
}
