using Microsoft.Xna.Framework.Graphics;
using PsxTools;
using System;
using System.IO;

namespace DbzLegendsAnalyser.Viewers
{
    /// <summary>
    /// Viewer for CHR_DATA\EFF_AUTO.B — special effect sprites.
    /// Structure: 80-color CLUT (5 × 16-color sub-palettes) + 2 LZSS-compressed 4bpp 256×256 images.
    /// </summary>
    public class EFF_AUTO_B_View : ImageAnalyserView
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

            // Image 1 at offset 0xA0
            TryDecodeImage(file, 0xA0, W, H, fmt, palettes, "Img1");

            // Image 2 at offset 0x355C
            TryDecodeImage(file, 0x355C, W, H, fmt, palettes, "Img2");
        }

        private void TryDecodeImage(byte[] file, int offset, int w, int h,
            PsxImageDecoder.PsxImageFormat fmt, PsxImageDecoder.PsxClut[] palettes, string namePrefix)
        {
            try
            {
                byte[] compressed = new byte[file.Length - offset];
                Array.Copy(file, offset, compressed, 0, compressed.Length);
                byte[] decompressed = LzssDecompressor.Decompress(compressed);

                for (int i = 0; i < palettes.Length; i++)
                {
                    var texture = PsxImageDecoder.DecodeToTexture2D(
                        GraphicsDevice, decompressed,
                        new PsxImageDecoder.PsxImageLayout(w, h),
                        fmt, palettes[i], 0);
                    Images.Add(($"{namePrefix}_pal{i}", texture));
                }
            }
            catch { }
        }
    }
}
