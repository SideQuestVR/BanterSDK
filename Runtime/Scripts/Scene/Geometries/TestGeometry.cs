using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[ExecuteInEditMode]
public class TestGeometry : MonoBehaviour {

    void Start() {
        var circle = new TorusKnot();
        Mesh mesh = new Mesh();
        mesh.vertices = circle.vertices.ToArray();
        mesh.normals = circle.normals.ToArray();
        mesh.triangles = circle.indices.ToArray();
        mesh.uv = circle.uvs.ToArray();

        GetComponent<MeshFilter>().sharedMesh = mesh;
        //GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    void Update() {

    }
}
