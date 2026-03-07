using Microsoft.Xna.Framework.Graphics;
using PsxTools;
using System;
using System.IO;

namespace DbzLegendsAnalyser.Viewers
{
    /// <summary>
    /// Viewer for CHR_DATA\LOAD.B — loading screen images.
    /// File structure: multiple 20480-byte sections (10 CD sectors each).
    /// Per section: 512 bytes CLUT (256 colors) then LZSS-compressed 8bpp 320×240 image.
    /// </summary>
    public class LOAD_B_View : ImageAnalyserView
    {
        protected override void LoadImages(string filePath)
        {
            byte[] file = File.ReadAllBytes(filePath);

            const int SectionSize = 20480;      // 10 × 2048 bytes
            const int PaletteOffset = 0x000;
            const int CompressedDataOffset = 0x200; // 512 bytes of palette

            int numSections = (file.Length + SectionSize - 1) / SectionSize;

            for (int s = 0; s < numSections; s++)
            {
                int sectionStart = s * SectionSize;

                ushort[] paletteColors = BinaryReaderHelper.ReadUShortArrayFast(
                    file, sectionStart + PaletteOffset, 256);
                var clut256 = new PsxImageDecoder.PsxClut(paletteColors, ColorsPerPalette: 256);

                int compressedStart = sectionStart + CompressedDataOffset;
                int available = file.Length - compressedStart;
                if (available <= 2) continue;

                byte[] compressedData = new byte[available];
                Array.Copy(file, compressedStart, compressedData, 0, available);

                byte[] decompressed;
                try { decompressed = LzssDecompressor.Decompress(compressedData); }
                catch { continue; }

                // 8bpp: VramWidthWords = 160 → 320 pixels wide, height = 240
                try
                {
                    var texture = PsxImageDecoder.DecodeToTexture2D(
                        GraphicsDevice,
                        decompressed,
                        new PsxImageDecoder.PsxImageLayout(160, 240),
                        new PsxImageDecoder.PsxImageFormat(PsxImageDecoder.PsxPixelMode.Bpp8),
                        clut256,
                        paletteIndex: 0);

                    Images.Add(($"Section_{s}", texture));
                }
                catch { continue; }
            }
        }
    }
}
