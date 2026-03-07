using Microsoft.Xna.Framework.Graphics;
using PsxTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DbzLegendsAnalyser.Viewers
{
    /// <summary>
    /// Viewer for STG\STGxTX.B — stage texture files.
    /// STGxTX.B format:
    ///   uint textureCount
    ///   TextureEntry[textureCount]:
    ///     uint compressionType (0=LZSS, 1=raw)
    ///     uint dataOffset
    ///     uint vramX, vramY
    ///     uint width (VRAM words, or color count for CLUT)
    ///     uint height (pixels)
    ///     uint isClut (0=image, 1=palette)
    /// </summary>
    public class STG_TX_View : ImageAnalyserView
    {
        protected override void LoadImages(string filePath)
        {
            byte[] file = File.ReadAllBytes(filePath);
            if (file.Length < 4) return;

            uint textureCount = BitConverter.ToUInt32(file, 0);

            // ── Pass 1: extract CLUTs ──
            var palettes = new Dictionary<int, PsxImageDecoder.PsxClut>();

            for (int i = 0; i < textureCount; i++)
            {
                int e = 4 + i * 28;
                uint dataOffset = BitConverter.ToUInt32(file, e + 4);
                uint width = BitConverter.ToUInt32(file, e + 16);
                uint isClut = BitConverter.ToUInt32(file, e + 24);

                if (isClut != 1) continue;

                int absOff = (int)dataOffset;
                int colorCount = (int)width; // for CLUT, width = number of colors
                if (absOff + colorCount * 2 > file.Length) continue;

                ushort[] colors = BinaryReaderHelper.ReadUShortArrayFast(file, absOff, colorCount);
                int cpp = colorCount >= 256 ? 256 : 16;
                palettes[i] = new PsxImageDecoder.PsxClut(colors, cpp);
            }

            // ── Pass 2: decode images ──
            var fallbackClut = palettes.Values.FirstOrDefault()
                ?? new PsxImageDecoder.PsxClut(new ushort[16], 16);

            for (int i = 0; i < textureCount; i++)
            {
                int e = 4 + i * 28;
                uint compressionType = BitConverter.ToUInt32(file, e + 0);
                uint dataOffset = BitConverter.ToUInt32(file, e + 4);
                uint width = BitConverter.ToUInt32(file, e + 16);
                uint height = BitConverter.ToUInt32(file, e + 20);
                uint isClut = BitConverter.ToUInt32(file, e + 24);

                if (isClut != 0) continue;

                try
                {
                    int absOff = (int)dataOffset;
                    int dataSize;
                    if (i + 1 < textureCount)
                    {
                        uint nextOffset = BitConverter.ToUInt32(file, 4 + (i + 1) * 28 + 4);
                        dataSize = (int)(nextOffset - dataOffset);
                    }
                    else
                    {
                        dataSize = file.Length - absOff;
                    }

                    if (dataSize <= 0 || absOff + dataSize > file.Length) continue;

                    byte[] imageData;
                    if (compressionType == 0)
                    {
                        byte[] src = new byte[dataSize];
                        Array.Copy(file, absOff, src, 0, dataSize);
                        imageData = LzssDecompressor.Decompress(src);
                    }
                    else
                    {
                        imageData = new byte[dataSize];
                        Array.Copy(file, absOff, imageData, 0, dataSize);
                    }

                    // Find nearest preceding palette
                    PsxImageDecoder.PsxClut palette = fallbackClut;
                    int palIdx = -1;
                    for (int p = i - 1; p >= 0; p--)
                    {
                        if (palettes.TryGetValue(p, out var found))
                        {
                            palette = found;
                            palIdx = p;
                            break;
                        }
                    }

                    PsxImageDecoder.PsxPixelMode mode;
                    if (palette.ColorsPerPalette == 256)
                    {
                        mode = PsxImageDecoder.PsxPixelMode.Bpp8;
                    }
                    else
                    {
                        mode = PsxImageDecoder.PsxPixelMode.Bpp4;
                        if (palette.ColorsPerPalette != 16)
                        {
                            ushort[] c16 = new ushort[16];
                            Array.Copy(palette.ColorsBgr555, 0, c16, 0, Math.Min(16, palette.ColorsBgr555.Length));
                            palette = new PsxImageDecoder.PsxClut(c16, 16);
                        }
                    }

                    var texture = PsxImageDecoder.DecodeToTexture2D(
                        GraphicsDevice,
                        imageData,
                        new PsxImageDecoder.PsxImageLayout((int)width, (int)height),
                        new PsxImageDecoder.PsxImageFormat(mode),
                        palette, 0);

                    Images.Add(($"Tex{i}_pal{palIdx}_{mode}", texture));
                }
                catch { continue; }
            }
        }
    }
}
