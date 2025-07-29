using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using System.Collections;

public class AESCTRFileEncryptor
{
    const int BufferSize = 8192;
    const int IVSize = 16;

    public static void EncryptFileWithIV(string inputPath, string outputPath, byte[] key)
    {
        byte[] iv = GenerateRandomIV();

        using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
        using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        using (var aes = new AesManaged { KeySize = 256, BlockSize = 128, Mode = CipherMode.ECB, Padding = PaddingMode.None })
        {
            aes.Key = key;

            output.Write(iv, 0, IVSize); // Write IV at the beginning of the output file

            byte[] buffer = new byte[BufferSize];
            byte[] keystream = new byte[BufferSize];
            byte[] encryptedCounter = new byte[IVSize];
            byte[] counter = (byte[])iv.Clone();
            byte[] counterBlock = new byte[IVSize];

            int bytesRead;
            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i += IVSize)
                {
                    Array.Copy(counter, counterBlock, IVSize);
                    IncrementCounter(counter);

                    using (var encryptor = aes.CreateEncryptor())
                        encryptedCounter = encryptor.TransformFinalBlock(counterBlock, 0, IVSize);

                    for (int j = 0; j < IVSize && i + j < bytesRead; j++)
                        keystream[i + j] = encryptedCounter[j];
                }

                for (int i = 0; i < bytesRead; i++)
                    buffer[i] ^= keystream[i];

                output.Write(buffer, 0, bytesRead);
            }
        }
    }

    public static IEnumerator DecryptFileWithIV(string inputPath, string outputPath, byte[] key, Action onComplete = null)
    {
        using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
        using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        using (var aes = new AesManaged { KeySize = 256, BlockSize = 128, Mode = CipherMode.ECB, Padding = PaddingMode.None })
        {
            aes.Key = key;

            byte[] iv = new byte[IVSize];
            input.Read(iv, 0, IVSize); // Read IV from file

            byte[] buffer = new byte[BufferSize];
            byte[] keystream = new byte[BufferSize];
            byte[] encryptedCounter = new byte[IVSize];
            byte[] counter = (byte[])iv.Clone();
            byte[] counterBlock = new byte[IVSize];

            int bytesRead;
            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i += IVSize)
                {
                    Array.Copy(counter, counterBlock, IVSize);
                    IncrementCounter(counter);

                    using (var encryptor = aes.CreateEncryptor())
                        encryptedCounter = encryptor.TransformFinalBlock(counterBlock, 0, IVSize);

                    for (int j = 0; j < IVSize && i + j < bytesRead; j++)
                        keystream[i + j] = encryptedCounter[j];
                }

                for (int i = 0; i < bytesRead; i++)
                    buffer[i] ^= keystream[i];

                output.Write(buffer, 0, bytesRead);
                yield return null;
            }
        }

        onComplete?.Invoke();
    }

    private static void IncrementCounter(byte[] counter)
    {
        for (int i = counter.Length - 1; i >= 0; i--)
        {
            if (++counter[i] != 0)
                break;
        }
    }

    public static byte[] GenerateRandomKey(int sizeInBits = 256)
    {
        byte[] key = new byte[sizeInBits / 8];
        using (var rng = new RNGCryptoServiceProvider())
            rng.GetBytes(key);
        return key;
    }

    public static byte[] GenerateRandomIV()
    {
        byte[] iv = new byte[IVSize];
        using (var rng = new RNGCryptoServiceProvider())
            rng.GetBytes(iv);
        return iv;
    }
}
