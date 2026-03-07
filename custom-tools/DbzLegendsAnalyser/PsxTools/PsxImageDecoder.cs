using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PsxTools;

/// <summary>
/// PSX raw image decoder — supports 4bpp/8bpp/16bpp with BGR555 CLUTs.
/// Returns MonoGame Texture2D instead of System.Drawing.Bitmap.
/// Equivalent of PsxTools2/PsxImageLoader.cs.
/// </summary>
public static class PsxImageDecoder
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Color Bgr555ToColor(ushort c, bool treatZeroAsTransparent)
    {
        if (treatZeroAsTransparent && c == 0)
            return Color.Transparent;

        int r5 = (c & 0x1F);
        int g5 = (c >> 5) & 0x1F;
        int b5 = (c >> 10) & 0x1F;

        int r8 = (r5 * 255) / 31;
        int g8 = (g5 * 255) / 31;
        int b8 = (b5 * 255) / 31;

        return new Color(r8, g8, b8, 255);
    }

    private static Color[] BuildPalette(PsxClut clut, int paletteIndex, int neededColors)
    {
        if (clut.ColorsPerPalette != neededColors)
            throw new ArgumentException($"CLUT ColorsPerPalette must be {neededColors} for this mode.");

        var pal555 = clut.GetPalette(paletteIndex);
        Color[] pal = new Color[neededColors];

        for (int i = 0; i < neededColors; i++)
            pal[i] = Bgr555ToColor(pal555[i], treatZeroAsTransparent: false);

        return pal;
    }

    // ── Main decode ──────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes raw PSX image data into a MonoGame Texture2D.
    /// </summary>
    public static Texture2D DecodeToTexture2D(
        GraphicsDevice graphicsDevice,
        ReadOnlySpan<byte> raw,
        PsxImageLayout layout,
        PsxImageFormat fmt,
        PsxClut? clut = null,
        int paletteIndex = 0)
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

        Color[]? pal = null;
        if (fmt.PixelMode == PsxPixelMode.Bpp4)
        {
            if (clut is null) throw new ArgumentNullException(nameof(clut));
            pal = BuildPalette(clut, paletteIndex, neededColors: 16);
        }
        else if (fmt.PixelMode == PsxPixelMode.Bpp8)
        {
            if (clut is null) throw new ArgumentNullException(nameof(clut));
            pal = BuildPalette(clut, paletteIndex, neededColors: 256);
        }

        Color[] pixels = new Color[widthPixels * h];
        int srcPos = 0;

        if (fmt.PixelMode == PsxPixelMode.Bpp16)
        {
            for (int y = 0; y < h; y++)
            {
                int dstOff = y * widthPixels;
                for (int x = 0; x < widthPixels; x++)
                {
                    ushort c = (ushort)(raw[srcPos] | (raw[srcPos + 1] << 8));
                    srcPos += 2;
                    pixels[dstOff + x] = Bgr555ToColor(c, fmt.Color0TransparentFor16bpp);
                }
            }
        }
        else if (fmt.PixelMode == PsxPixelMode.Bpp8)
        {
            for (int y = 0; y < h; y++)
            {
                int dstOff = y * widthPixels;
                for (int x = 0; x < widthPixels; x++)
                {
                    byte idx = raw[srcPos++];
                    if (fmt.Index0Transparent && idx == 0)
                        pixels[dstOff + x] = Color.Transparent;
                    else
                        pixels[dstOff + x] = pal![idx];
                }
            }
        }
        else // 4bpp
        {
            int rowBytes = wWords * 2;
            for (int y = 0; y < h; y++)
            {
                int dstOff = y * widthPixels;
                int x = 0;
                for (int bx = 0; bx < rowBytes; bx++)
                {
                    byte b = raw[srcPos++];
                    int i0 = b & 0x0F;
                    int i1 = (b >> 4) & 0x0F;

                    pixels[dstOff + x] = (fmt.Index0Transparent && i0 == 0) ? Color.Transparent : pal![i0];
                    pixels[dstOff + x + 1] = (fmt.Index0Transparent && i1 == 0) ? Color.Transparent : pal![i1];
                    x += 2;
                }
            }
        }

        var texture = new Texture2D(graphicsDevice, widthPixels, h, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        return texture;
    }
}
