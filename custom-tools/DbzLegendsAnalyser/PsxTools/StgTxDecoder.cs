using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PsxTools;

/// <summary>
/// Loader for DBZ Legends STGxTX.B texture files.
/// Returns MonoGame Texture2D instead of System.Drawing.Bitmap.
/// </summary>
public static class StgTxDecoder
{
    public static List<StgTexture> LoadStgTxFile(GraphicsDevice graphicsDevice, string filePath)
        => LoadStgTxFile(graphicsDevice, File.ReadAllBytes(filePath));

    public static List<StgTexture> LoadStgTxFile(GraphicsDevice graphicsDevice, byte[] fileBytes)
    {
        using var ms = new MemoryStream(fileBytes, writable: false);
        using var br = new BinaryReader(ms);

        uint textureCount = br.ReadUInt32();
        uint texturesOffset = br.ReadUInt32();
        uint textureDataOffset = br.ReadUInt32();

        var textures = new List<StgTexture>((int)textureCount);

        ms.Seek(texturesOffset, SeekOrigin.Begin);

        for (int i = 0; i < textureCount; i++)
        {
            uint compressionType = br.ReadUInt32();
            uint dataOffset = br.ReadUInt32();
            uint vramX = br.ReadUInt32();
            uint vramY = br.ReadUInt32();
            uint width = br.ReadUInt32();
            uint height = br.ReadUInt32();
            uint isClut = br.ReadUInt32();

            long currentPos = ms.Position;
            ms.Seek(textureDataOffset + dataOffset, SeekOrigin.Begin);

            byte[] textureData;

            if (compressionType == 0)
            {
                int compressedSize = Math.Min(fileBytes.Length - (int)(textureDataOffset + dataOffset), 0x10000);
                byte[] compressedData = new byte[compressedSize];
                ms.Read(compressedData, 0, compressedSize);
                textureData = LzssDecompressor.Decompress(compressedData);
            }
            else if (compressionType == 1)
            {
                int dataSize = (int)(width * height * 2);
                textureData = new byte[dataSize];
                ms.Read(textureData, 0, dataSize);
            }
            else
            {
                throw new NotSupportedException($"Unknown compression type: {compressionType}");
            }

            var texture = new StgTexture
            {
                Index = i,
                CompressionType = compressionType,
                VramX = (ushort)vramX,
                VramY = (ushort)vramY,
                Width = (ushort)width,
                Height = (ushort)height,
                IsClut = isClut != 0,
                RawData = textureData
            };

            if (!texture.IsClut && textureData.Length > 0)
            {
                texture.Texture = ConvertToTexture2D(graphicsDevice, textureData, (int)width, (int)height);
            }

            textures.Add(texture);
            ms.Seek(currentPos, SeekOrigin.Begin);
        }

        return textures;
    }

    private static Texture2D ConvertToTexture2D(GraphicsDevice graphicsDevice, byte[] data, int width, int height)
    {
        var texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 2;
                if (offset + 1 < data.Length)
                {
                    ushort pixel = (ushort)(data[offset] | (data[offset + 1] << 8));
                    pixels[y * width + x] = Bgr555ToColor(pixel);
                }
            }
        }

        texture.SetData(pixels);
        return texture;
    }

    private static Color Bgr555ToColor(ushort bgr555)
    {
        int r5 = (bgr555 >> 0) & 0x1F;
        int g5 = (bgr555 >> 5) & 0x1F;
        int b5 = (bgr555 >> 10) & 0x1F;

        int r8 = (r5 << 3) | (r5 >> 2);
        int g8 = (g5 << 3) | (g5 >> 2);
        int b8 = (b5 << 3) | (b5 >> 2);
        int a8 = (bgr555 == 0) ? 0 : 255;

        return new Color(r8, g8, b8, a8);
    }
}

public class StgTexture
{
    public int Index { get; set; }
    public uint CompressionType { get; set; }
    public ushort VramX { get; set; }
    public ushort VramY { get; set; }
    public ushort Width { get; set; }
    public ushort Height { get; set; }
    public bool IsClut { get; set; }
    public byte[] RawData { get; set; } = Array.Empty<byte>();
    public Texture2D? Texture { get; set; }

    public string Description => IsClut
        ? $"CLUT Palette [{Width}x{Height}] @ VRAM({VramX},{VramY})"
        : $"Texture [{Width}x{Height}] @ VRAM({VramX},{VramY}) - {(CompressionType == 0 ? "LZSS" : "Uncompressed")}";
}
