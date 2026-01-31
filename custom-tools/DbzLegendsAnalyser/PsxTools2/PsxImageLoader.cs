using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PsxTools2;

public static class PsxImageLoader
{
    public enum PsxPixelMode : byte { Bpp4 = 0, Bpp8 = 1, Bpp16 = 2 }

    public sealed record PsxClut(ushort[] ColorsBgr555, int ColorsPerPalette)
    {
        public int PaletteCount => ColorsBgr555.Length / ColorsPerPalette;

        public ReadOnlySpan<ushort> GetPalette(int paletteIndex)
        {
            if ((uint)paletteIndex >= (uint)PaletteCount)
                throw new ArgumentOutOfRangeException(nameof(paletteIndex));

            return ColorsBgr555.AsSpan(paletteIndex * ColorsPerPalette, ColorsPerPalette);
        }
    }

    public readonly record struct PsxImageLayout(int VramWidthWords, int Height);

    public readonly record struct PsxImageFormat(
        PsxPixelMode PixelMode,
        bool Index0Transparent = true,
        bool Color0TransparentFor16bpp = false
    );

    // ---- Helpers ----
    private static uint Bgr555ToArgb32(ushort c, bool treatZeroAsTransparent)
    {
        if (treatZeroAsTransparent && c == 0)
            return 0x00000000u;

        int r5 = (c & 0x1F);
        int g5 = (c >> 5) & 0x1F;
        int b5 = (c >> 10) & 0x1F;

        uint r8 = (uint)((r5 * 255) / 31);
        uint g8 = (uint)((g5 * 255) / 31);
        uint b8 = (uint)((b5 * 255) / 31);

        return 0xFF000000u | (r8 << 16) | (g8 << 8) | b8;
    }

    private static uint[] BuildPaletteArgb(PsxClut clut, int paletteIndex, int neededColors)
    {
        if (clut.ColorsPerPalette != neededColors)
            throw new ArgumentException($"CLUT ColorsPerPalette must be {neededColors} for this mode.");

        var pal555 = clut.GetPalette(paletteIndex);
        uint[] pal = new uint[neededColors];

        for (int i = 0; i < neededColors; i++)
            pal[i] = Bgr555ToArgb32(pal555[i], treatZeroAsTransparent: false);

        return pal;
    }

    // ---- Main decode ----
    public static Bitmap DecodeToBitmap(
        ReadOnlySpan<byte> raw,
        PsxImageLayout layout,
        PsxImageFormat fmt,
        PsxClut? clut = null,
        int paletteIndex = 0
    )
    {
        int wWords = layout.VramWidthWords;
        int h = layout.Height;
        int bytesExpected = wWords * h * 2;

        if (raw.Length < bytesExpected)
            throw new ArgumentException($"Raw too small: need {bytesExpected} bytes, got {raw.Length}");

        int widthPixels = fmt.PixelMode switch
        {
            PsxPixelMode.Bpp4 => wWords * 4,
            PsxPixelMode.Bpp8 => wWords * 2,
            PsxPixelMode.Bpp16 => wWords,
            _ => throw new ArgumentOutOfRangeException()
        };

        uint[]? pal = null;
        if (fmt.PixelMode == PsxPixelMode.Bpp4)
        {
            if (clut is null) throw new ArgumentNullException(nameof(clut));
            pal = BuildPaletteArgb(clut, paletteIndex, neededColors: 16);
        }
        else if (fmt.PixelMode == PsxPixelMode.Bpp8)
        {
            if (clut is null) throw new ArgumentNullException(nameof(clut));
            pal = BuildPaletteArgb(clut, paletteIndex, neededColors: 256);
        }

        // Create destination bitmap
        Bitmap bmp = new Bitmap(widthPixels, h, PixelFormat.Format32bppArgb);
        Rectangle rect = new Rectangle(0, 0, widthPixels, h);
        BitmapData bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = bd.Stride; // bytes per row in destination
            int totalBytes = stride * h;
            byte[] dst = new byte[totalBytes]; // managed buffer then copy (safe + fast enough)

            // Write row-by-row
            int srcPos = 0;

            if (fmt.PixelMode == PsxPixelMode.Bpp16)
            {
                for (int y = 0; y < h; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < widthPixels; x++)
                    {
                        ushort c = (ushort)(raw[srcPos] | (raw[srcPos + 1] << 8));
                        srcPos += 2;

                        uint argb = Bgr555ToArgb32(c, fmt.Color0TransparentFor16bpp);
                        int o = rowStart + (x * 4);
                        dst[o + 0] = (byte)(argb & 0xFF);         // B
                        dst[o + 1] = (byte)((argb >> 8) & 0xFF);  // G
                        dst[o + 2] = (byte)((argb >> 16) & 0xFF); // R
                        dst[o + 3] = (byte)((argb >> 24) & 0xFF); // A
                    }
                }
            }
            else if (fmt.PixelMode == PsxPixelMode.Bpp8)
            {
                // widthPixels == wWords*2 == row bytes in source
                for (int y = 0; y < h; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < widthPixels; x++)
                    {
                        byte idx = raw[srcPos++];
                        uint argb;

                        if (fmt.Index0Transparent && idx == 0)
                            argb = 0x00000000u;
                        else
                            argb = pal![idx];

                        int o = rowStart + (x * 4);
                        dst[o + 0] = (byte)(argb & 0xFF);
                        dst[o + 1] = (byte)((argb >> 8) & 0xFF);
                        dst[o + 2] = (byte)((argb >> 16) & 0xFF);
                        dst[o + 3] = (byte)((argb >> 24) & 0xFF);
                    }
                }
            }
            else // 4bpp
            {
                int rowBytes = wWords * 2;
                // each byte -> 2 pixels
                for (int y = 0; y < h; y++)
                {
                    int rowStart = y * stride;
                    int x = 0;
                    for (int bx = 0; bx < rowBytes; bx++)
                    {
                        byte b = raw[srcPos++];
                        int i0 = b & 0x0F;
                        int i1 = (b >> 4) & 0x0F;

                        uint a0 = (fmt.Index0Transparent && i0 == 0) ? 0x00000000u : pal![i0];
                        uint a1 = (fmt.Index0Transparent && i1 == 0) ? 0x00000000u : pal![i1];

                        int o0 = rowStart + (x * 4);
                        dst[o0 + 0] = (byte)(a0 & 0xFF);
                        dst[o0 + 1] = (byte)((a0 >> 8) & 0xFF);
                        dst[o0 + 2] = (byte)((a0 >> 16) & 0xFF);
                        dst[o0 + 3] = (byte)((a0 >> 24) & 0xFF);

                        int o1 = o0 + 4;
                        dst[o1 + 0] = (byte)(a1 & 0xFF);
                        dst[o1 + 1] = (byte)((a1 >> 8) & 0xFF);
                        dst[o1 + 2] = (byte)((a1 >> 16) & 0xFF);
                        dst[o1 + 3] = (byte)((a1 >> 24) & 0xFF);

                        x += 2;
                    }
                }
            }

            Marshal.Copy(dst, 0, bd.Scan0, dst.Length);
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        return bmp;
    }
}