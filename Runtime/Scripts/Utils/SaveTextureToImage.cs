
using System;
using System.IO;
using UnityEngine;

public class SaveTextureToImage{
 public enum SaveTextureFileFormat
   {
       EXR, JPG, PNG, TGA
    };
    public static byte[] Do(Texture source,
        int width = -1,
        int height = -1,
        SaveTextureFileFormat fileFormat = SaveTextureFileFormat.JPG,
        int jpgQuality = 95){
        if (!(source is Texture2D || source is RenderTexture))
        {
            return null;
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

        UnityEngine.Object.Destroy(tempTex);

        return encoded;
        
    }
}