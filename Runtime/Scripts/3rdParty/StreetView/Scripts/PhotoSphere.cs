// using InfinityCode.HugeTexture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;




public class PhotoSphere : MonoBehaviour
{
    public string Panoid; // vSayNK_OObJ0gEptx2UeGg WS13SZ-9S9_zajAt9BiCAw gZ5vtLnBWJxOSp9CE4MDTg
    public bool HighResTexture;
    public Action LoadCallback;

    [HideInInspector]
    public PhotoMeta MetaData;

    [HideInInspector]
    public bool IsLoaded;
    Mesh mesh = null;

    PhotoTiles tiles;


    private bool FirstLoaded;

    void Start() {
        name = Panoid;
        StartCoroutine(GetMetadata(Panoid));
    }
    IEnumerator GetMetadata(string Panoid) {
        string url = "https://www.google.com/maps/photometa/v1?authuser=0&hl=en&gl=uk&pb=!1m4!1smaps_sv.tactile!11m2!2m1!1b1!2m2!1sen!2suk!3m3!1m2!1e2!2s" +
          Panoid +
          "!4m57!1e1!1e2!1e3!1e4!1e5!1e6!1e8!1e12!2m1!1e1!4m1!1i48!5m1!1e1!5m1!1e2!6m1!1e1!6m1!1e2!9m36!1m3!1e2!2b1!3e2!1m3!1e2!2b0!3e3!1m3!1e3!2b1!3e2!1m3!1e3!2b0!3e3!1m3!1e8!2b0!3e3!1m3!1e1!2b0!3e3!1m3!1e4!2b0!3e3!1m3!1e10!2b1!3e2!1m3!1e10!2b0!3e3";
        using (UnityWebRequest www = UnityWebRequest.Get(url)) {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError) {
                UnityEngine.Debug.Log(www.error);
            } else {
                var text = www.downloadHandler.text;
                MetaData = new PhotoMeta(text, (vertices, normals, uvs, tris) => {
                    mesh = GetComponent<MeshFilter>().mesh;
                    mesh.Clear();
                    mesh.vertices = vertices;
                    mesh.normals = normals;
                    mesh.uv = uvs;
                    mesh.triangles = tris.ToArray();
                    GetComponent<MeshFilter>().sharedMesh = mesh;
                    //GetComponent<MeshCollider>().sharedMesh = mesh;
                    if (FirstLoaded) {
                        LoadCallback?.Invoke();
                        IsLoaded = true;
                    } else {
                        FirstLoaded = true;
                    }
                });
                transform.localEulerAngles = MetaData.Rotation;
                tiles = GetComponent<PhotoTiles>();
                tiles.GetParorama(MetaData.Panoid, HighResTexture, MetaData.Resolution != 8192, () => {
                    if (FirstLoaded) {
                        LoadCallback?.Invoke();
                        IsLoaded = true;
                    } else {
                        FirstLoaded = true;
                    }
                });
            }
        }
    }

    void OnDestroy() {
        if(mesh != null) {
            Destroy(mesh);
        }
        if(tiles != null && tiles.panorama != null) {
            Destroy(tiles.panorama);
        }
    }
}