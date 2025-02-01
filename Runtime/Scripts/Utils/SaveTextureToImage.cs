
using System;
using System.IO;
using UnityEngine;

public class SaveTextureToImage{
 public enum SaveTextureFileFormat
   {
       EXR, JPG, PNG, TGA
    };
    public static void Do(Texture source,
        string filePath,
        int width = -1,
        int height = -1,
        SaveTextureFileFormat fileFormat = SaveTextureFileFormat.JPG,
        int jpgQuality = 95,
        bool asynchronous = true,
        bool skipSave = false,
        Action<bool, byte[]> done = null){
        if (!(source is Texture2D || source is RenderTexture))
        {
            done?.Invoke(false, null);
            return;
        }

        if (width < 0 || height < 0)
        {
            width = source.width;
            height = source.height;
        }

        RenderTexture tempRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, tempRT);

        Texture2D tempTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        RenderTexture.active = tempRT;
        tempTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tempTex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(tempRT);

        byte[] encoded;
        switch (fileFormat)
        {
            case SaveTextureFileFormat.JPG:
                encoded = tempTex.EncodeToJPG(jpgQuality);
                break;
            case SaveTextureFileFormat.PNG:
                encoded = tempTex.EncodeToPNG();
                break;
            case SaveTextureFileFormat.TGA:
                encoded = tempTex.EncodeToTGA();
                break;
            default:
                encoded = tempTex.EncodeToEXR();
                break;
        }

        done?.Invoke(true, encoded);
        if (!skipSave)
        {
#if UNITY_ANDROID
            AndroidExtensions.SaveImageToGallery(encoded, Path.GetFileName(filePath), "Banter Photo!");
#else
            File.WriteAllBytes(filePath, encoded);
#endif
        }

        UnityEngine.Object.Destroy(tempTex);
    
        
    }
}