using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PsxTools2;

public static class PsxTimLoader
{
    public static Bitmap LoadPsxTimAsBitmap(string filePath, int paletteRow = 0)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);

        if (LooksLikeTim(fileBytes))
        {
            return DecodeTimToBitmap(fileBytes, paletteRow);
        }

        byte[] decompressed = LzssDecompressor.Decompress(fileBytes);

        if (LooksLikeTim(decompressed))
        {
            return DecodeTimToBitmap(decompressed, paletteRow);
        }

        throw new NotSupportedException("Unknown format : not TIM direct, not TIM after decompression.");
    }

    public static Bitmap DecodeTimToBitmap(byte[] timBytes, int paletteRow = 0)
    {
        using var ms = new MemoryStream(timBytes, writable: false);
        using var br = new BinaryReader(ms);

        uint magic = br.ReadUInt32(); // LE
        if (magic != 0x00000010)
        {
            throw new InvalidDataException("TIM magic invalide.");
        }

        uint flags = br.ReadUInt32();
        int bppMode = (int)(flags & 0x7);      // 0=4bpp,1=8bpp,2=16bpp,3=24bpp
        bool hasClut = (flags & 0x8) != 0;

        ushort[] clut = Array.Empty<ushort>();
        int clutW = 0, clutH = 0;

        if (hasClut)
        {
            _ = br.ReadUInt32(); // clutBlockSize
            _ = br.ReadUInt16(); // clutX
            _ = br.ReadUInt16(); // clutY
            clutW = br.ReadUInt16();
            clutH = br.ReadUInt16();

            int colors = checked(clutW * clutH);
            clut = new ushort[colors];
            for (int i = 0; i < colors; i++)
            {
                clut[i] = br.ReadUInt16();
            }
        }

        _ = br.ReadUInt32(); // imgBlockSize
        _ = br.ReadUInt16(); // imgX
        _ = br.ReadUInt16(); // imgY
        int imgWWords = br.ReadUInt16();
        int imgH = br.ReadUInt16();

        int pixelW = bppMode switch
        {
            0 => imgWWords * 4,                 // 4bpp
            1 => imgWWords * 2,                 // 8bpp
            2 => imgWWords,                     // 16bpp
            3 => (imgWWords * 2) / 3,           // 24bpp
            _ => throw new NotSupportedException($"Unknown TIM bppMode: {bppMode}")
        };

        var bmp = new Bitmap(pixelW, imgH, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, pixelW, imgH);
        BitmapData bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = bd.Stride;
            byte[] dst = new byte[checked(stride * imgH)];

            if (bppMode == 2)
            {
                // 16bpp BGR555 direct
                for (int y = 0; y < imgH; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < pixelW; x++)
                    {
                        ushort psx = br.ReadUInt16();
                        uint argb = PsxBgr555ToArgb(psx, treatZeroAsTransparent: false);
                        WriteBgra(dst, row + x * 4, argb);
                    }
                }
            }
            else if (bppMode == 3)
            {
                // 24bpp packed B,G,R bytes
                int bytesPerLine = imgWWords * 2;
                byte[] line = new byte[bytesPerLine];

                for (int y = 0; y < imgH; y++)
                {
                    int read = br.Read(line, 0, bytesPerLine);
                    if (read != bytesPerLine)
                    {
                        throw new InvalidDataException("EOF in 24bpp data.");
                    }

                    int row = y * stride;
                    for (int x = 0; x < pixelW; x++)
                    {
                        int i = x * 3;
                        byte b = line[i + 0];
                        byte g = line[i + 1];
                        byte r = line[i + 2];
                        uint argb = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                        WriteBgra(dst, row + x * 4, argb);
                    }
                }
            }
            else
            {
                // 4bpp / 8bpp paletté
                if (!hasClut)
                {
                    throw new InvalidDataException("Paletted TIM without CLUT.");
                }

                int paletteCount = clutH;
                if (paletteRow < 0 || paletteRow >= paletteCount)
                {
                    paletteRow = 0;
                }

                int paletteOffset = paletteRow * clutW;

                if (bppMode == 0)
                {
                    // 4bpp : 1 word = 4 nibbles
                    for (int y = 0; y < imgH; y++)
                    {
                        int row = y * stride;
                        int x = 0;

                        for (int w = 0; w < imgWWords; w++)
                        {
                            ushort word = br.ReadUInt16();
                            int i0 = (word >> 0) & 0xF;
                            int i1 = (word >> 4) & 0xF;
                            int i2 = (word >> 8) & 0xF;
                            int i3 = (word >> 12) & 0xF;

                            WriteBgra(dst, row + (x++ * 4), PsxBgr555ToArgb(clut[paletteOffset + i0], true));
                            WriteBgra(dst, row + (x++ * 4), PsxBgr555ToArgb(clut[paletteOffset + i1], true));
                            WriteBgra(dst, row + (x++ * 4), PsxBgr555ToArgb(clut[paletteOffset + i2], true));
                            WriteBgra(dst, row + (x++ * 4), PsxBgr555ToArgb(clut[paletteOffset + i3], true));
                        }
                    }
                }
                else if (bppMode == 1)
                {
                    // 8bpp : 1 word = 2 bytes indices
                    for (int y = 0; y < imgH; y++)
                    {
                        int row = y * stride;
                        int x = 0;

                        for (int w = 0; w < imgWWords; w++)
                        {
                            ushort word = br.ReadUInt16();
                            int i0 = word & 0xFF;
                            int i1 = (word >> 8) & 0xFF;

                            WriteBgra(dst, row + (x++ * 4), PsxBgr555ToArgb(clut[paletteOffset + i0], true));
                            WriteBgra(dst, row + (x++ * 4), PsxBgr555ToArgb(clut[paletteOffset + i1], true));
                        }
                    }
                }
                else
                {
                    throw new NotSupportedException($"Unsupported paletted TIM bppMode: {bppMode}");
                }
            }

            Marshal.Copy(dst, 0, bd.Scan0, dst.Length);
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
    }

    private static bool LooksLikeTim(byte[] data)
        => data.Length >= 8 && data[0] == 0x10 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x00;

    private static void WriteBgra(byte[] dst, int offset, uint argb)
    {
        // Format32bppArgb en mémoire = BGRA (little endian)
        dst[offset + 0] = (byte)(argb & 0xFF);         // B
        dst[offset + 1] = (byte)((argb >> 8) & 0xFF);  // G
        dst[offset + 2] = (byte)((argb >> 16) & 0xFF); // R
        dst[offset + 3] = (byte)((argb >> 24) & 0xFF); // A
    }

    private static uint PsxBgr555ToArgb(ushort psx, bool treatZeroAsTransparent)
    {
        if (treatZeroAsTransparent && psx == 0)
        {
            return 0x00000000u;
        }

        int b5 = (psx >> 0) & 0x1F;
        int g5 = (psx >> 5) & 0x1F;
        int r5 = (psx >> 10) & 0x1F;

        byte b = (byte)((b5 << 3) | (b5 >> 2));
        byte g = (byte)((g5 << 3) | (g5 >> 2));
        byte r = (byte)((r5 << 3) | (r5 >> 2));

        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }
}