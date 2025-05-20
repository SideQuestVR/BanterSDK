using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class Post{

    public static async Task<string> Json<T>(string url, T postData, Dictionary<string, string> headers = null)
    {
        UnityWebRequest uwr = UnityWebRequest.Put(url, JsonUtility.ToJson(postData));
        uwr.method = "POST";
        if (headers != null)
        {
            foreach (var header in headers)
            {
                uwr.SetRequestHeader(header.Key, header.Value);
            }
        }
        await uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            throw new System.Exception(uwr.error);
        }
        else
        {
            return uwr.downloadHandler.text;
        }
    }
    public static async Task<string> Text(string url, string postData, Dictionary<string, string> headers = null)
    {
        UnityWebRequest uwr = UnityWebRequest.Put(url, postData);
        uwr.method = "POST";
        if (headers != null)
        {
            foreach (var header in headers)
            {
                uwr.SetRequestHeader(header.Key, header.Value);
            }
        }
        await uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            throw new System.Exception(uwr.error);
        }
        else
        {
            return uwr.downloadHandler.text;
        }
    }
}