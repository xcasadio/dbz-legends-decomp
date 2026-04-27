using Microsoft.Xna.Framework.Graphics;
using PsxTools;
using System;
using System.Collections.Generic;
using System.IO;

namespace DbzLegendsAnalyser.Viewers
{
    /// <summary>
    /// Viewer for SUB\TITLE.B.
    /// The file starts with two offsets: word0 -> sprite-group table, word1 -> 6-entry image load script.
    /// </summary>
    public class TITLE_B_View : ImageAnalyserView
    {
        private readonly record struct LoadEntry(
            uint Kind,
            uint DataOffset,
            uint VramX,
            uint VramY,
            uint WidthWords,
            uint Height,
            uint IsClutFlag)
        {
            public bool IsCompressed => Kind == 0;
            public bool IsClut => IsClutFlag != 0;
            public int ByteCount => checked((int)(WidthWords * Height * 2));
        }

        protected override void LoadImages(string filePath)
        {
            byte[] file = File.ReadAllBytes(filePath);
            if (file.Length < 8)
                return;

            int loadScriptOffset = checked((int)BitConverter.ToUInt32(file, 0x04));
            if (loadScriptOffset < 0 || loadScriptOffset + 4 > file.Length)
                return;

            int rawEntryCount = checked((int)BitConverter.ToUInt32(file, loadScriptOffset));
            if (rawEntryCount <= 0)
                return;

            int maxEntryCount = (file.Length - loadScriptOffset - 4) / 28;
            int entryCount = Math.Min(rawEntryCount, maxEntryCount);
            if (entryCount <= 0)
                return;

            var entries = new List<LoadEntry>(entryCount);
            for (int i = 0; i < entryCount; i++)
            {
                int entryOffset = loadScriptOffset + 4 + i * 28;
                entries.Add(new LoadEntry(
                    BitConverter.ToUInt32(file, entryOffset + 0),
                    BitConverter.ToUInt32(file, entryOffset + 4),
                    BitConverter.ToUInt32(file, entryOffset + 8),
                    BitConverter.ToUInt32(file, entryOffset + 12),
                    BitConverter.ToUInt32(file, entryOffset + 16),
                    BitConverter.ToUInt32(file, entryOffset + 20),
                    BitConverter.ToUInt32(file, entryOffset + 24)));
            }

            AddIndexedImage(file, entries, 0, 1, "entry0_bg_pal0");
            AddIndexedImage(file, entries, 2, 3, "entry2_logo_ui_pal1");
            AddIndexedImage(file, entries, 4, 5, "entry4_memcard_msgs_pal2");
        }

        private void AddIndexedImage(byte[] file, List<LoadEntry> entries, int imageIndex, int clutIndex, string label)
        {
            if ((uint)imageIndex >= (uint)entries.Count || (uint)clutIndex >= (uint)entries.Count)
                return;

            LoadEntry imageEntry = entries[imageIndex];
            LoadEntry clutEntry = entries[clutIndex];
            if (imageEntry.IsClut || !clutEntry.IsClut)
                return;

            try
            {
                PsxImageDecoder.PsxClut clut = ReadClut(file, clutEntry);
                PsxImageDecoder.PsxPixelMode pixelMode = clut.ColorsPerPalette == 16
                    ? PsxImageDecoder.PsxPixelMode.Bpp4
                    : PsxImageDecoder.PsxPixelMode.Bpp8;

                byte[] imageData = ReadImageData(file, imageEntry);
                var texture = PsxImageDecoder.DecodeToTexture2D(
                    GraphicsDevice,
                    imageData,
                    new PsxImageDecoder.PsxImageLayout((int)imageEntry.WidthWords, (int)imageEntry.Height),
                    new PsxImageDecoder.PsxImageFormat(pixelMode),
                    clut,
                    0);

                Images.Add((label, texture));
            }
            catch { }
        }

        private static PsxImageDecoder.PsxClut ReadClut(byte[] file, LoadEntry entry)
        {
            int colorCount = checked((int)(entry.WidthWords * entry.Height));
            int offset = checked((int)entry.DataOffset);
            int byteCount = checked(colorCount * 2);

            if (offset < 0 || byteCount < 0 || offset > file.Length - byteCount)
                throw new InvalidDataException("TITLE.B CLUT entry is out of range.");

            ushort[] colors = BinaryReaderHelper.ReadUShortArrayFast(file, offset, colorCount);
            int colorsPerPalette = colorCount >= 256 ? 256 : 16;
            return new PsxImageDecoder.PsxClut(colors, colorsPerPalette);
        }

        private static byte[] ReadImageData(byte[] file, LoadEntry entry)
        {
            int offset = checked((int)entry.DataOffset);
            if (offset < 0 || offset >= file.Length)
                throw new InvalidDataException("TITLE.B image entry is out of range.");

            if (entry.IsCompressed)
            {
                byte[] compressed = new byte[file.Length - offset];
                Array.Copy(file, offset, compressed, 0, compressed.Length);
                return LzssDecompressor.Decompress(compressed);
            }

            int byteCount = entry.ByteCount;
            if (byteCount < 0 || offset > file.Length - byteCount)
                throw new InvalidDataException("TITLE.B raw image entry is out of range.");

            byte[] imageData = new byte[byteCount];
            Array.Copy(file, offset, imageData, 0, byteCount);
            return imageData;
        }
    }
}
