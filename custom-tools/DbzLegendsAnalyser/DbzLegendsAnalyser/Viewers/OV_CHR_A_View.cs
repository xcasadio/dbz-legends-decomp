using Microsoft.Xna.Framework.Graphics;
using PsxTools;
using System;
using System.Collections.Generic;
using System.IO;

namespace DbzLegendsAnalyser.Viewers
{
    /// <summary>
    /// Viewer for CHR_DATA\OV_CHR_A.B — character sprite sheets.
    /// Decodes 4 image regions with multiple palette variations.
    /// </summary>
    public class OV_CHR_A_View : ImageAnalyserView
    {
        protected override void LoadImages(string filePath)
        {
            byte[] raw = File.ReadAllBytes(filePath);
            byte[] decomp = LzssDecompressor.Decompress(raw);

            if (decomp.Length < 0x7400)
                throw new InvalidDataException(
                    $"Decompressed data too small: 0x{decomp.Length:X} (expected >= 0x7400)");

            // Parse CLUTs
            ushort[] clut128Colors = BinaryReaderHelper.ReadUShortArrayFast(decomp, 0x0000, 128);
            ushort[] clut256Colors = BinaryReaderHelper.ReadUShortArrayFast(decomp, 0x0100, 256);
            var clut128 = new PsxImageDecoder.PsxClut(clut128Colors, ColorsPerPalette: 16);
            var clut256 = new PsxImageDecoder.PsxClut(clut256Colors, ColorsPerPalette: 256);

            var regions = new[]
            {
                (Offset: 0x0300, Length: 0x2000, WidthWords: 0x20, Height: 0x80,
                 Mode: PsxImageDecoder.PsxPixelMode.Bpp4, NumPalettes: 8, Clut: clut128),
                (Offset: 0x2300, Length: 0x2300, WidthWords: 0x28, Height: 0x70,
                 Mode: PsxImageDecoder.PsxPixelMode.Bpp4, NumPalettes: 8, Clut: clut128),
                (Offset: 0x4600, Length: 0x1E00, WidthWords: 0x28, Height: 0x60,
                 Mode: PsxImageDecoder.PsxPixelMode.Bpp8, NumPalettes: 1, Clut: clut256),
                (Offset: 0x6400, Length: 0x1000, WidthWords: 0x40, Height: 0x20,
                 Mode: PsxImageDecoder.PsxPixelMode.Bpp4, NumPalettes: 8, Clut: clut128)
            };

            foreach (var region in regions)
            {
                byte[] regionData = new byte[region.Length];
                Array.Copy(decomp, region.Offset, regionData, 0, regionData.Length);

                for (int i = 0; i < region.NumPalettes; i++)
                {
                    var texture = PsxImageDecoder.DecodeToTexture2D(
                        GraphicsDevice,
                        regionData,
                        new PsxImageDecoder.PsxImageLayout(region.WidthWords, region.Height),
                        new PsxImageDecoder.PsxImageFormat(region.Mode),
                        region.Clut,
                        i);

                    Images.Add(($"0x{region.Offset:X4} pal{i}", texture));
                }
            }
        }
    }
}
