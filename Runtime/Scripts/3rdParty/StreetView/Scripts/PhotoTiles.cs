using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class PhotoTiles : MonoBehaviour
{
    private string TileUrl = "https://streetviewpixels-pa.googleapis.com/v1/tile?cb_client=maps_sv.tactile&nbt=1&fover=2&panoid=";
    private string Panoid;

    public Texture2DArray panorama;

    public void GetParorama(string Panoid, bool IsLarge, bool SmallerRes, Action PanoramaCallback) {
        this.Panoid = Panoid;
        int zoom = 3;
        int cols = 8;
        int rows = 4;
        if (IsLarge) {
            zoom = 4;
            cols = 16;
            rows = 8;
        }
        if (SmallerRes) {
            cols -= 1;
            if (IsLarge) {
                cols -= 3;
                rows -= 1;
            }
        }
        panorama = new Texture2DArray(512, 512, cols * rows, TextureFormat.RGB24, false);
        panorama.wrapMode = TextureWrapMode.Clamp;
        int count = 0;
        for (int y = 0; y < rows; y++) {
            for (int x = 0; x < cols; x++) {
                StartCoroutine(GetParoramaTile(x, y, zoom, (texture, _x, _y) => {
                    Graphics.CopyTexture(texture, 0, 0, panorama, (rows - _y - 1) * cols + _x, 0);
                    count++;
                    if (count == cols * rows) {
                        var mat = GetComponent<MeshRenderer>().material;
                        if(mat != null) {
                            mat.SetTextureScale("_MainTex", new Vector2(SmallerRes ? 0.93f : 1.0019f, SmallerRes ? 0.8125f : 1));
                            mat.SetTextureOffset("_MainTex", new Vector2(SmallerRes ? 0 : 0, SmallerRes ? 0.1875f : 0));
                            mat.SetInt("_Cols", cols);
                            mat.SetInt("_Rows", rows);
                            mat.SetTexture("_MainTex", panorama);
                        }
                        PanoramaCallback();
                    }
                    Destroy(texture);
                }));
            }
        }
    }

    IEnumerator GetParoramaTile(int x, int y, int zoom, Action<Texture2D, int, int> callback) {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(TileUrl + Panoid + "&x=" + x + "&y=" + y + "&zoom=" + zoom)) {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError) {
                Debug.Log("Error getting: " + TileUrl + Panoid + "&x=" + x + "&y=" + y + "&zoom=" + zoom);
                Debug.Log(www.error);
            } else {
                callback(DownloadHandlerTexture.GetContent(www), x, y);
            }
        }
    }
}
