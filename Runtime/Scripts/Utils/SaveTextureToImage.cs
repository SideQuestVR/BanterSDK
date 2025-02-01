
using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

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
        // check that the input we're getting is something we can handle:
        if (!(source is Texture2D || source is RenderTexture))
        {
            done?.Invoke(false, null);
            return;
        }
 
        // use the original texture size in case the input is negative:
        if (width < 0 || height < 0)
        {
            width = source.width;
            height = source.height;
        }
        Texture resizeRT = null;
        // resize the original image:
        if(width != source.width || height != source.height)
        {
            resizeRT = RenderTexture.GetTemporary(width, height, 0);
            Graphics.Blit(source, (RenderTexture)resizeRT);
        }else{
            resizeRT = source;
        }
        // create a native array to receive data from the GPU:
        var narray = new NativeArray<byte>(width * height * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        // request the texture data back from the GPU:
        var request = AsyncGPUReadback.RequestIntoNativeArray (ref narray, resizeRT, 0, (AsyncGPUReadbackRequest request) =>
        {
                // if the readback was successful, encode and write the results to disk
            if (!request.hasError)
            {
                NativeArray<byte> encoded;

                switch (fileFormat)
                {
                    case SaveTextureFileFormat.EXR:
                        encoded = ImageConversion.EncodeNativeArrayToEXR(narray, resizeRT.graphicsFormat, (uint)width, (uint)height);
                        break;
                    case SaveTextureFileFormat.JPG:
                        encoded = ImageConversion.EncodeNativeArrayToJPG(narray, resizeRT.graphicsFormat, (uint)width, (uint)height, 0, jpgQuality);
                        break;
                    case SaveTextureFileFormat.TGA:
                        encoded = ImageConversion.EncodeNativeArrayToTGA(narray, resizeRT.graphicsFormat, (uint)width, (uint)height);
                        break;
                    default:
                        encoded = ImageConversion.EncodeNativeArrayToPNG(narray, resizeRT.graphicsFormat, (uint)width, (uint)height);
                        break;
                }

                // notify the user that the operation is done, and its outcome.
                done?.Invoke(!request.hasError, encoded.ToArray());
                if(!skipSave) {
#if UNITY_ANDROID
                AndroidExtensions.SaveImageToGallery(encoded.ToArray(), Path.GetFileName(filePath), "Banter Photo!");
#else
                System.IO.File.WriteAllBytes(filePath, encoded.ToArray());
#endif
                
                }
                encoded.Dispose();
            }else{
                // notify the user that the operation is done, and its outcome.
                done?.Invoke(!request.hasError, null);
            }

            narray.Dispose();
            
        });


        if (!asynchronous)
            request.WaitForCompletion();

        
    }
}