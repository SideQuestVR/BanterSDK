using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>
    /// Struct containing parsed KTX texture header information
    /// </summary>
    public struct KTXHeader
    {
        public uint width;
        public uint height;
        public uint depth;
        public uint glInternalFormat;
        public uint dataOffset;
        public uint dataSize;
    }

    /// <summary>
    /// Parser for KTX texture files, specifically for ASTC-compressed Quest Home textures
    /// KTX format specification: https://www.khronos.org/opengles/sdk/tools/KTX/file_format_spec/
    /// </summary>
    public static class KTXParser
    {
        // KTX file identifier
        private static readonly byte[] KTX_IDENTIFIER = new byte[]
        {
            0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A
        };

        /// <summary>
        /// Parse KTX file header to extract texture metadata
        /// </summary>
        /// <param name="ktxData">Raw KTX file data</param>
        /// <returns>Parsed KTX header information</returns>
        public static KTXHeader ParseHeader(byte[] ktxData)
        {
            if (ktxData == null || ktxData.Length < 64)
            {
                throw new ArgumentException("Invalid KTX data: file too small");
            }

            // Verify KTX identifier
            for (int i = 0; i < KTX_IDENTIFIER.Length; i++)
            {
                if (ktxData[i] != KTX_IDENTIFIER[i])
                {
                    throw new FormatException("Invalid KTX file identifier");
                }
            }

            using (var stream = new MemoryStream(ktxData))
            using (var reader = new BinaryReader(stream))
            {
                // Skip identifier (12 bytes)
                stream.Position = 12;

                // Read endianness (4 bytes) - 0x04030201 for little-endian
                uint endianness = reader.ReadUInt32();
                bool isLittleEndian = (endianness == 0x04030201);

                if (!isLittleEndian)
                {
                    throw new NotSupportedException("Big-endian KTX files are not supported");
                }

                // Read header fields
                uint glType = reader.ReadUInt32();                // 16
                uint glTypeSize = reader.ReadUInt32();            // 20
                uint glFormat = reader.ReadUInt32();              // 24
                uint glInternalFormat = reader.ReadUInt32();      // 28
                uint glBaseInternalFormat = reader.ReadUInt32();  // 32
                uint pixelWidth = reader.ReadUInt32();            // 36
                uint pixelHeight = reader.ReadUInt32();           // 40
                uint pixelDepth = reader.ReadUInt32();            // 44
                uint numberOfArrayElements = reader.ReadUInt32(); // 48
                uint numberOfFaces = reader.ReadUInt32();         // 52
                uint numberOfMipmapLevels = reader.ReadUInt32();  // 56
                uint bytesOfKeyValueData = reader.ReadUInt32();   // 60

                // Skip key-value data
                stream.Position += bytesOfKeyValueData;

                // Read image size
                uint imageSize = reader.ReadUInt32();

                return new KTXHeader
                {
                    width = pixelWidth,
                    height = pixelHeight,
                    depth = pixelDepth,
                    glInternalFormat = glInternalFormat,
                    dataOffset = (uint)stream.Position,
                    dataSize = imageSize
                };
            }
        }

        /// <summary>
        /// Convert OpenGL internal format constant to Unity TextureFormat
        /// ASTC format constants from: https://www.khronos.org/registry/OpenGL/extensions/KHR/KHR_texture_compression_astc_hdr.txt
        /// Note: Unity only supports square and certain rectangular ASTC block sizes
        /// </summary>
        /// <param name="glInternalFormat">OpenGL internal format constant</param>
        /// <returns>Corresponding Unity TextureFormat</returns>
        public static TextureFormat GetTextureFormat(uint glInternalFormat)
        {
            switch (glInternalFormat)
            {
                // ASTC formats supported by Unity
                case 0x93B0: return TextureFormat.ASTC_4x4;
                case 0x93B2: return TextureFormat.ASTC_5x5;
                case 0x93B4: return TextureFormat.ASTC_6x6;
                case 0x93B7: return TextureFormat.ASTC_8x8;
                case 0x93BB: return TextureFormat.ASTC_10x10;
                case 0x93BD: return TextureFormat.ASTC_12x12;

                // ASTC formats NOT supported by Unity (throw informative error)
                case 0x93B1: throw new NotSupportedException($"ASTC 5x4 format (0x93B1) is not supported by Unity. Supported formats: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12");
                case 0x93B3: throw new NotSupportedException($"ASTC 6x5 format (0x93B3) is not supported by Unity. Supported formats: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12");
                case 0x93B5: throw new NotSupportedException($"ASTC 8x5 format (0x93B5) is not supported by Unity. Supported formats: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12");
                case 0x93B6: throw new NotSupportedException($"ASTC 8x6 format (0x93B6) is not supported by Unity. Supported formats: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12");
                case 0x93B8: throw new NotSupportedException($"ASTC 10x5 format (0x93B8) is not supported by Unity. Supported formats: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12");
                case 0x93B9: throw new NotSupportedException($"ASTC 10x6 format (0x93B9) is not supported by Unity. Supported formats: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12");
                case 0x93BA: throw new NotSupportedException($"ASTC 10x8 format (0x93BA) is not supported by Unity. Supported formats: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12");
                case 0x93BC: throw new NotSupportedException($"ASTC 12x10 format (0x93BC) is not supported by Unity. Supported formats: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12");

                default:
                    throw new NotSupportedException($"Unsupported GL internal format: 0x{glInternalFormat:X}. Only ASTC formats are supported, and Unity only supports: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12");
            }
        }

        /// <summary>
        /// Extract compressed texture data from KTX file
        /// </summary>
        /// <param name="ktxData">Raw KTX file data</param>
        /// <param name="header">Parsed KTX header</param>
        /// <returns>Raw compressed texture data</returns>
        public static byte[] ExtractTextureData(byte[] ktxData, KTXHeader header)
        {
            if (header.dataOffset + header.dataSize > ktxData.Length)
            {
                throw new ArgumentException("Invalid KTX data: texture data extends beyond file size");
            }

            byte[] textureData = new byte[header.dataSize];
            Array.Copy(ktxData, header.dataOffset, textureData, 0, header.dataSize);
            return textureData;
        }

        /// <summary>
        /// Load ASTC texture from KTX file data
        /// </summary>
        /// <param name="ktxData">Raw KTX file data</param>
        /// <param name="textureName">Name for the created texture</param>
        /// <returns>Loaded Texture2D with ASTC compression</returns>
        public static Texture2D LoadASTCTexture(byte[] ktxData, string textureName = "ASTCTexture")
        {
            try
            {
                var header = ParseHeader(ktxData);
                TextureFormat format = GetTextureFormat(header.glInternalFormat);
                byte[] textureData = ExtractTextureData(ktxData, header);

                // Create texture with ASTC format in sRGB color space
                // linear=false tells Unity the data is sRGB, so it will convert to linear when sampling
                Texture2D texture = new Texture2D(
                    (int)header.width,
                    (int)header.height,
                    format,
                    false,  // mipChain = false (no mipmaps)
                    false   // linear = false (sRGB color space - critical for correct colors!)
                );

                texture.name = textureName;

                // Load compressed data directly (Unity will decompress on GPU)
                texture.LoadRawTextureData(textureData);
                texture.Apply(false, true); // uploadToGPU=false, makeNoLongerReadable=true

                return texture;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load ASTC texture from KTX data: {ex.Message}");
                throw;
            }
        }
    }
}
