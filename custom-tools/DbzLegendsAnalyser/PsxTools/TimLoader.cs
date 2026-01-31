using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PsxTools;

public static class TimLoader
{
    public static Texture2D LoadTim(GraphicsDevice graphicsDevice, string path, int paletteIndex = 0, Color? transparentKey = null, int tolerance = 0)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        return LoadTim(graphicsDevice, br, paletteIndex, transparentKey, tolerance);
    }

    public static Texture2D LoadTim(GraphicsDevice graphicsDevice, BinaryReader br, int paletteIndex = 0, Color? transparentKey = null, int tolerance = 0)
    {
        uint magic = br.ReadUInt32(); // 0x10 00 00 00
        if (magic != 0x10)
        {
            throw new InvalidDataException("Not a TIM file (bad magic).");
        }

        uint flags = br.ReadUInt32();
        bool hasClut = (flags & 0x08) != 0;
        int bppCode = (int)(flags & 0x07);
        int bpp = bppCode switch { 0 => 4, 1 => 8, 2 => 16, 3 => 24, _ => throw new NotSupportedException("Unknown bpp") };
        if (bpp == 24)
        {
            throw new NotSupportedException("24bpp TIM not handled here.");
        }

        // --- Read CLUT (palette) if present ---
        Color[][] palettes = null;
        if (hasClut)
        {
            uint clutLen = br.ReadUInt32(); // includes header below (x,y,w,h)
            ushort clutX = br.ReadUInt16(); // VRAM coords, not needed for decoding
            ushort clutY = br.ReadUInt16();
            ushort clutW = br.ReadUInt16(); // colors per CLUT (16 or 256)
            ushort clutH = br.ReadUInt16(); // number of CLUTs
            int totalColors = clutW * clutH;

            palettes = new Color[clutH][];
            for (int p = 0; p < clutH; p++)
                palettes[p] = new Color[clutW];

            var key = transparentKey ?? new Color(0, 0, 0, 0);

            for (int i = 0; i < clutW * clutH; i++)
            {
                ushort c = br.ReadUInt16();              // BGR555 (+bit semi-trans)
                int r = ((c & 0x1F) << 3) | ((c & 0x1F) >> 2);
                int g = (((c >> 5) & 31) << 3) | (((c >> 5) & 31) >> 2);
                int b = (((c >> 10) & 31) << 3) | (((c >> 10) & 31) >> 2);

                bool makeTransparent = false;
                if (key.A != 0)
                {
                    makeTransparent =
                        Math.Abs(r - key.R) <= tolerance &&
                        Math.Abs(g - key.G) <= tolerance &&
                        Math.Abs(b - key.B) <= tolerance;
                }
                else
                {
                    makeTransparent = (c == 0);
                }

                int a = makeTransparent ? 0 : 255;

                int clutIdx = i / clutW;
                int colorIdx = i % clutW;
                palettes[clutIdx][colorIdx] = new Color(r, g, b, a);
            }
        }

        // --- Read image block ---
        uint imgLen = br.ReadUInt32(); // includes header below
        ushort imgX = br.ReadUInt16(); // VRAM coords, can be ignored
        ushort imgY = br.ReadUInt16();
        ushort imgWWords = br.ReadUInt16(); // width in 16-bit words
        ushort imgH = br.ReadUInt16();

        int width = bpp switch
        {
            4 => imgWWords * 4,
            8 => imgWWords * 2,
            16 => imgWWords * 1,
            _ => throw new NotSupportedException()
        };
        int height = imgH;

        int dataBytes = checked((int)imgLen - 12); // remove x,y,w,h (8 bytes) + length field was separate
        byte[] imgData = br.ReadBytes(dataBytes);

        // --- Decode to 32-bit ARGB ---
        return DecodeBuffer(graphicsDevice, paletteIndex, width, height, bpp, palettes, imgWWords, imgData);
    }

    public static Texture2D DecodeBuffer(GraphicsDevice graphicsDevice, int paletteIndex, int width, int height, int bpp, Color[][]? palettes,
        ushort imgWWords, byte[] imgData)
    {
        var texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
        Color[] pixels = new Color[width * height];

        if (bpp == 4)
        {
            if (palettes == null)
            {
                throw new InvalidDataException("4bpp image without CLUT.");
            }

            var pal = palettes[Math.Clamp(paletteIndex, 0, palettes.Length - 1)];
            int bytesPerLine = imgWWords * 2;
            for (int y = 0; y < height; y++)
            {
                int srcOff = y * bytesPerLine;
                int dstOff = y * width;
                for (int bx = 0; bx < bytesPerLine; bx++)
                {
                    byte b = imgData[srcOff + bx];
                    int x0 = bx * 2;

                    pixels[dstOff + x0] = pal[b & 0x0F];
                    pixels[dstOff + x0 + 1] = pal[(b >> 4) & 0x0F];
                }
            }
        }
        else if (bpp == 8)
        {
            if (palettes == null)
            {
                throw new InvalidDataException("8bpp image without CLUT.");
            }

            var pal = palettes[Math.Clamp(paletteIndex, 0, palettes.Length - 1)];
            int bytesPerLine = imgWWords * 2;
            for (int y = 0; y < height; y++)
            {
                int srcOff = y * bytesPerLine;
                int dstOff = y * width;
                for (int x = 0; x < bytesPerLine; x++)
                {
                    byte idx = imgData[srcOff + x];
                    pixels[dstOff + x] = pal[idx];
                }
            }
        }
        else if (bpp == 16)
        {
            int bytesPerLine = imgWWords * 2;
            for (int y = 0; y < height; y++)
            {
                int srcOff = y * bytesPerLine;
                int dstOff = y * width;
                for (int x = 0; x < width; x++)
                {
                    ushort c = BitConverter.ToUInt16(imgData, srcOff + x * 2);
                    int r5 = (c & 0x1F);
                    int g5 = (c >> 5) & 0x1F;
                    int b5 = (c >> 10) & 0x1F;
                    int r = (r5 << 3) | (r5 >> 2);
                    int g = (g5 << 3) | (g5 >> 2);
                    int b = (b5 << 3) | (b5 >> 2);
                    int a = (c == 0) ? 0 : 255;
                    pixels[dstOff + x] = new Color(r, g, b, a);
                }
            }
        }

        texture.SetData(pixels);
        return texture;
    }
}