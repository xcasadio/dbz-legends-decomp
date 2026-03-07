using Microsoft.Xna.Framework.Graphics;
using PsxTools;
using System;
using System.IO;

namespace DbzLegendsAnalyser.Viewers
{
    /// <summary>
    /// Viewer for CHR_DATA\FACE.B — character face portraits.
    /// Structure: multiple 0x1000-byte sections, each with 16-color CLUT + 3 × 4bpp images (12×48).
    /// </summary>
    public class FACE_B_View : ImageAnalyserView
    {
        protected override void LoadImages(string filePath)
        {
            byte[] file = File.ReadAllBytes(filePath);

            const int SectionSize = 0x1000;
            const int ImageWidth = 0x0C;   // 12 VRAM words → 48 pixels in 4bpp
            const int ImageHeight = 0x30;  // 48 pixels
            const int ImageSize = 0x480;   // 12 × 48 / 2
            int[] imageOffsets = { 0x020, 0x4A0, 0x920 };

            int numSections = (file.Length + SectionSize - 1) / SectionSize;

            for (int s = 0; s < numSections; s++)
            {
                int sectionStart = s * SectionSize;

                ushort[] paletteColors = BinaryReaderHelper.ReadUShortArrayFast(file, sectionStart, 16);
                var clut16 = new PsxImageDecoder.PsxClut(paletteColors, ColorsPerPalette: 16);

                for (int imgIdx = 0; imgIdx < 3; imgIdx++)
                {
                    int offset = sectionStart + imageOffsets[imgIdx];
                    if (offset + ImageSize > file.Length) continue;

                    byte[] imgData = new byte[ImageSize];
                    Array.Copy(file, offset, imgData, 0, ImageSize);

                    try
                    {
                        var texture = PsxImageDecoder.DecodeToTexture2D(
                            GraphicsDevice,
                            imgData,
                            new PsxImageDecoder.PsxImageLayout(ImageWidth, ImageHeight),
                            new PsxImageDecoder.PsxImageFormat(PsxImageDecoder.PsxPixelMode.Bpp4),
                            clut16, 0);

                        Images.Add(($"Sec{s}_img{imgIdx}", texture));
                    }
                    catch { continue; }
                }
            }
        }
    }
}
