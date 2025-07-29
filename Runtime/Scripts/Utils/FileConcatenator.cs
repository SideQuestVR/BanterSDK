using System;
using System.IO;

public static class FileConcatenator
{
    private const int HeaderSize = 32;

    public static void Concatenate(string file1Path, string file2Path, string outputPath)
    {
        byte[] file1Data = File.ReadAllBytes(file1Path);
        byte[] file2Data = File.ReadAllBytes(file2Path);

        long file1Offset = HeaderSize;
        long file1Length = file1Data.Length;
        long file2Offset = file1Offset + file1Length;
        long file2Length = file2Data.Length;

        using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            // Write header
            writer.Write(file1Offset);
            writer.Write(file1Length);
            writer.Write(file2Offset);
            writer.Write(file2Length);

            // Write files
            writer.Write(file1Data);
            writer.Write(file2Data);
        }
    }

    // public static void ExtractFiles(string combinedPath, string outFile1, string outFile2)
    // {
    //     using (var stream = new FileStream(combinedPath, FileMode.Open, FileAccess.Read))
    //     using (var reader = new BinaryReader(stream))
    //     {
    //         long file1Offset = reader.ReadInt64();
    //         long file1Length = reader.ReadInt64();
    //         long file2Offset = reader.ReadInt64();
    //         long file2Length = reader.ReadInt64();

    //         stream.Seek(file1Offset, SeekOrigin.Begin);
    //         byte[] file1Data = reader.ReadBytes((int)file1Length);

    //         stream.Seek(file2Offset, SeekOrigin.Begin);
    //         byte[] file2Data = reader.ReadBytes((int)file2Length);

    //         File.WriteAllBytes(outFile1, file1Data);
    //         File.WriteAllBytes(outFile2, file2Data);
    //     }
    // }
}