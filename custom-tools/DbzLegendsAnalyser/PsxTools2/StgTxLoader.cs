using System.Drawing;
using System.Drawing.Imaging;

namespace PsxTools2;

/// <summary>
/// Loader for DBZ Legends STGxTX.B texture files.
/// These files contain multiple textures with LZSS compression.
/// </summary>
public static class StgTxLoader
{
    /// <summary>
    /// Loads an STGxTX.B file and returns all textures as bitmaps.
    /// </summary>
    public static List<StgTexture> LoadStgTxFile(string filePath)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        return LoadStgTxFile(fileBytes);
    }

    /// <summary>
    /// Loads an STGxTX.B file from bytes and returns all textures as bitmaps.
    /// </summary>
    public static List<StgTexture> LoadStgTxFile(byte[] fileBytes)
    {
        using var ms = new MemoryStream(fileBytes, writable: false);
        using var br = new BinaryReader(ms);

        // Read header
        uint textureCount = br.ReadUInt32();
        uint texturesOffset = br.ReadUInt32();
        uint textureDataOffset = br.ReadUInt32();

        var textures = new List<StgTexture>((int)textureCount);

        // Read texture entries
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

            // Read texture data
            long currentPos = ms.Position;
            ms.Seek(textureDataOffset + dataOffset, SeekOrigin.Begin);

            byte[] textureData;
            
            // Decompress if needed
            if (compressionType == 0)
            {
                // LZSS compressed
                int compressedSize = EstimateCompressedSize(fileBytes, (int)(textureDataOffset + dataOffset));
                byte[] compressedData = new byte[compressedSize];
                ms.Read(compressedData, 0, compressedSize);
                textureData = LzssDecompressor.Decompress(compressedData);
            }
            else if (compressionType == 1)
            {
                // Uncompressed
                int dataSize = (int)(width * height * 2); // 16bpp
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

            // Convert to bitmap if it's an image (not a CLUT)
            if (!texture.IsClut && textureData.Length > 0)
            {
                texture.Bitmap = ConvertToBitmap(textureData, (int)width, (int)height);
            }

            textures.Add(texture);
            
            // Restore position to read next entry
            ms.Seek(currentPos, SeekOrigin.Begin);
        }

        return textures;
    }

    private static int EstimateCompressedSize(byte[] fileBytes, int offset)
    {
        // Simple heuristic: read until we see unlikely LZSS patterns
        // or reach a reasonable maximum
        int maxSize = Math.Min(fileBytes.Length - offset, 0x10000);
        return maxSize;
    }

    private static Bitmap ConvertToBitmap(byte[] data, int width, int height)
    {
        // PSX textures are 16-bit (BGR555 format)
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        BitmapData bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                uint* pDst = (uint*)bd.Scan0;
                int stride = bd.Stride / 4;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int offset = (y * width + x) * 2;
                        if (offset + 1 < data.Length)
                        {
                            ushort pixel = (ushort)(data[offset] | (data[offset + 1] << 8));
                            uint argb = Bgr555ToArgb(pixel);
                            pDst[y * stride + x] = argb;
                        }
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        return bmp;
    }

    private static uint Bgr555ToArgb(ushort bgr555)
    {
        int r5 = (bgr555 >> 0) & 0x1F;
        int g5 = (bgr555 >> 5) & 0x1F;
        int b5 = (bgr555 >> 10) & 0x1F;
        bool stp = (bgr555 & 0x8000) != 0;

        // Expand 5-bit to 8-bit
        int r8 = (r5 << 3) | (r5 >> 2);
        int g8 = (g5 << 3) | (g5 >> 2);
        int b8 = (b5 << 3) | (b5 >> 2);

        // Handle semi-transparency bit
        int a8 = (bgr555 == 0) ? 0 : (stp ? 255 : 255);

        return (uint)((a8 << 24) | (r8 << 16) | (g8 << 8) | b8);
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
    public Bitmap? Bitmap { get; set; }

    public string Description => IsClut 
        ? $"CLUT Palette [{Width}x{Height}] @ VRAM({VramX},{VramY})"
        : $"Texture [{Width}x{Height}] @ VRAM({VramX},{VramY}) - {(CompressionType == 0 ? "LZSS" : "Uncompressed")}";
}
