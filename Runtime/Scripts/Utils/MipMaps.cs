using Unity.Collections;
using UnityEditor;
using UnityEngine;

public class MipMaps{
    public static Texture2D Do(Texture2D texture) {
        Texture2D texture2 = null;
        try{
            texture2 = new Texture2D(texture.width, texture.height, texture.format, true, false);
            var src = texture.GetRawTextureData<byte>();
            var dest = texture2.GetRawTextureData<byte>();
            NativeArray<byte>.Copy(src, dest, src.Length);
            texture2.LoadRawTextureData(dest);
            texture2.Apply(true, true);
            GameObject.Destroy(texture);
        }catch{
            Debug.Log("RuntimeMipMaps failed");
            texture2 = texture;
        }
        return texture2;
    }
}