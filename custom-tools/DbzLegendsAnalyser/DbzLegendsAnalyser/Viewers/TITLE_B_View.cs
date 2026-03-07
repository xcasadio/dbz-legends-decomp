using Microsoft.Xna.Framework.Graphics;
using PsxTools;
using System;
using System.IO;

namespace DbzLegendsAnalyser.Viewers
{
    /// <summary>
    /// Viewer for SUB\TITLE.B — title screen images.
    /// Structure: 80-color CLUT (5 × 16 sub-palettes) + LZSS-compressed 4bpp 256×256 image.
    /// </summary>
    public class TITLE_B_View : ImageAnalyserView
    {
        protected override void LoadImages(string filePath)
        {
            byte[] file = File.ReadAllBytes(filePath);

            // 80 palette colors = 5 × 16-color sub-palettes
            ushort[] paletteColors = BinaryReaderHelper.ReadUShortArrayFast(file, 0x00, 80);
            var palettes = new PsxImageDecoder.PsxClut[5];
            for (int i = 0; i < 5; i++)
            {
                ushort[] p16 = new ushort[16];
                Array.Copy(paletteColors, i * 16, p16, 0, 16);
                palettes[i] = new PsxImageDecoder.PsxClut(p16, ColorsPerPalette: 16);
            }

            // 4bpp, 256×256 pixels → VramWidthWords=0x40, Height=0x100
            const int W = 0x40, H = 0x100;
            var fmt = new PsxImageDecoder.PsxImageFormat(PsxImageDecoder.PsxPixelMode.Bpp4);

            try
            {
                byte[] compressed = new byte[file.Length - 0xA0];
                Array.Copy(file, 0xA0, compressed, 0, compressed.Length);
                byte[] decompressed = LzssDecompressor.Decompress(compressed);

                for (int i = 0; i < palettes.Length; i++)
                {
                    var texture = PsxImageDecoder.DecodeToTexture2D(
                        GraphicsDevice, decompressed,
                        new PsxImageDecoder.PsxImageLayout(W, H),
                        fmt, palettes[i], 0);
                    Images.Add(($"Title_pal{i}", texture));
                }
            }
            catch { }
        }
    }
}
