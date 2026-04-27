using Microsoft.Xna.Framework;
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
        private const int FinalScreenWidth = 320;
        private const int FinalScreenHeight = 240;

        private static readonly string[] KnownGroupNames =
        {
            "background left slice",
            "background right slice",
            "logo top",
            "copyright bottom",
            "press start"
        };

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

        private readonly record struct TitleSprite(
            byte U,
            byte V,
            byte LocalX,
            byte LocalY,
            ushort ClutId,
            ushort PackedTPage,
            ushort Width,
            ushort Height,
            ushort RotZ,
            ushort Aux,
            ushort ScaleX,
            ushort ScaleY)
        {
            public int TPage => PackedTPage & 0x1FF;
            public int TPageX => TPage & 0xF;
            public int TPageY => (TPage >> 4) & 1;
            public int ColorMode => (TPage >> 7) & 3;
            public int ClutVramX => (ClutId & 0x3F) * 16;
            public int ClutVramY => (ClutId >> 6) & 0x1FF;
        }

        private sealed class DecodedResource
        {
            public string Label = string.Empty;
            public Texture2D Texture = null!;
            public Color[] Pixels = Array.Empty<Color>();
            public PsxImageDecoder.PsxPixelMode PixelMode;
            public int VramX16;
            public int VramY;
            public int ClutVramX;
            public int ClutVramY;

            public int PixelX => PixelMode switch
            {
                PsxImageDecoder.PsxPixelMode.Bpp4 => VramX16 * 4,
                PsxImageDecoder.PsxPixelMode.Bpp8 => VramX16 * 2,
                _ => VramX16
            };
        }

        private readonly record struct ResolvedSprite(TitleSprite Sprite, Color[] Pixels);

        private readonly record struct PlacedSprite(
            string Label,
            Color[] Pixels,
            int Width,
            int Height,
            int X,
            int Y);

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

            var resources = new List<DecodedResource>(3);
            AddIndexedImage(file, entries, 0, 1, "source background atlas 320x240 8bpp", resources);
            AddIndexedImage(file, entries, 2, 3, "source logo and ui atlas 256x256 8bpp", resources);
            AddIndexedImage(file, entries, 4, 5, "source memcard status messages atlas 256x128 4bpp lzss", resources);
            AddSpriteImages(file, entries, resources);
        }

        private void AddIndexedImage(
            byte[] file,
            List<LoadEntry> entries,
            int imageIndex,
            int clutIndex,
            string label,
            List<DecodedResource> resources)
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

                var pixels = new Color[texture.Width * texture.Height];
                texture.GetData(pixels);
                resources.Add(new DecodedResource
                {
                    Label = label,
                    Texture = texture,
                    Pixels = pixels,
                    PixelMode = pixelMode,
                    VramX16 = checked((int)imageEntry.VramX),
                    VramY = checked((int)imageEntry.VramY),
                    ClutVramX = checked((int)clutEntry.VramX),
                    ClutVramY = checked((int)clutEntry.VramY)
                });
            }
            catch { }
        }

        private void AddSpriteImages(byte[] file, List<LoadEntry> entries, List<DecodedResource> resources)
        {
            if (resources.Count == 0 || file.Length < 8)
                return;

            int groupTableOffset = checked((int)BitConverter.ToUInt32(file, 0x00));
            if (groupTableOffset <= 0 || groupTableOffset + 4 > file.Length)
                return;

            int minDataOffset = file.Length;
            foreach (LoadEntry entry in entries)
            {
                int dataOffset = checked((int)entry.DataOffset);
                if (dataOffset > groupTableOffset && dataOffset < minDataOffset)
                    minDataOffset = dataOffset;
            }

            int maxGroupCount = Math.Max(0, (minDataOffset - groupTableOffset) / 4);
            if (maxGroupCount <= 0)
                return;

            var groupOffsets = new List<int>(maxGroupCount);
            for (int i = 0; i < maxGroupCount; i++)
            {
                int offset = checked((int)BitConverter.ToUInt32(file, groupTableOffset + i * 4));
                if (offset < 0 || offset + 4 > groupTableOffset)
                    break;

                groupOffsets.Add(offset);
            }

            var finalSprites = new List<PlacedSprite>();

            for (int groupIndex = 0; groupIndex < groupOffsets.Count; groupIndex++)
            {
                int groupOffset = groupOffsets[groupIndex];
                int groupBoundary = groupIndex + 1 < groupOffsets.Count ? groupOffsets[groupIndex + 1] : groupTableOffset;
                if (groupBoundary <= groupOffset + 4)
                    continue;

                int rawSpriteCount = checked((int)BitConverter.ToUInt32(file, groupOffset));
                int maxSpriteCount = Math.Max(0, (groupBoundary - groupOffset - 4) / 20);
                int spriteCount = Math.Min(rawSpriteCount, maxSpriteCount);
                if (spriteCount <= 0)
                    continue;

                string groupName = groupIndex < KnownGroupNames.Length
                    ? KnownGroupNames[groupIndex]
                    : $"group {groupIndex}";

                var groupSprites = new List<PlacedSprite>(spriteCount);

                TitleSprite[] rawSprites = new TitleSprite[spriteCount];
                for (int spriteIndex = 0; spriteIndex < spriteCount; spriteIndex++)
                {
                    int spriteOffset = groupOffset + 4 + spriteIndex * 20;
                    rawSprites[spriteIndex] = ReadSprite(file, spriteOffset);
                }

                foreach (int spriteIndex in GetSpriteDrawOrder(groupIndex, spriteCount))
                {
                    TitleSprite sprite = rawSprites[spriteIndex];

                    if (!TryResolveSprite(sprite, resources, out ResolvedSprite resolved))
                        continue;

                    (int finalX, int finalY) = GetFinalScreenPosition(groupIndex, resolved.Sprite);
                    string spriteLabel =
                        $"group {groupIndex} {groupName} sprite {spriteIndex} {resolved.Sprite.Width}x{resolved.Sprite.Height} at {finalX},{finalY}";

                    var placed = new PlacedSprite(
                        spriteLabel,
                        resolved.Pixels,
                        resolved.Sprite.Width,
                        resolved.Sprite.Height,
                        finalX,
                        finalY);

                    groupSprites.Add(placed);
                    finalSprites.Add(placed);
                    Images.Add((spriteLabel, CreateTexture(placed.Pixels, placed.Width, placed.Height)));
                }

                if (groupSprites.Count == 0)
                    continue;

                string compositeLabel = $"group {groupIndex} {groupName} composite";
                Images.Add((compositeLabel, CreateCroppedCompositeTexture(groupSprites)));
            }

            if (finalSprites.Count > 0)
            {
                Texture2D finalComposite = CreateCanvasTexture(finalSprites, FinalScreenWidth, FinalScreenHeight);
                Images.Insert(0, ($"final title screen composite {FinalScreenWidth}x{FinalScreenHeight}", finalComposite));
            }
        }

        private static TitleSprite ReadSprite(byte[] file, int offset)
        {
            return new TitleSprite(
                file[offset + 0],
                file[offset + 1],
                file[offset + 2],
                file[offset + 3],
                BitConverter.ToUInt16(file, offset + 4),
                BitConverter.ToUInt16(file, offset + 6),
                BitConverter.ToUInt16(file, offset + 8),
                BitConverter.ToUInt16(file, offset + 10),
                BitConverter.ToUInt16(file, offset + 12),
                BitConverter.ToUInt16(file, offset + 14),
                BitConverter.ToUInt16(file, offset + 16),
                BitConverter.ToUInt16(file, offset + 18));
        }

        private static bool TryResolveSprite(TitleSprite sprite, List<DecodedResource> resources, out ResolvedSprite resolved)
        {
            resolved = default;

            PsxImageDecoder.PsxPixelMode? pixelMode = sprite.ColorMode switch
            {
                0 => PsxImageDecoder.PsxPixelMode.Bpp4,
                1 => PsxImageDecoder.PsxPixelMode.Bpp8,
                _ => null
            };

            if (pixelMode is null || sprite.Width == 0 || sprite.Height == 0)
                return false;

            int pagePixelWidth = pixelMode == PsxImageDecoder.PsxPixelMode.Bpp8 ? 128 : 256;

            foreach (DecodedResource resource in resources)
            {
                if (resource.PixelMode != pixelMode.Value)
                    continue;

                if (resource.ClutVramX != sprite.ClutVramX || resource.ClutVramY != sprite.ClutVramY)
                    continue;

                int sourceX = sprite.TPageX * pagePixelWidth + sprite.U - resource.PixelX;
                int sourceY = sprite.TPageY * 256 + sprite.V - resource.VramY;
                if (sourceX < 0 || sourceY < 0)
                    continue;

                if (sourceX >= resource.Texture.Width || sourceY >= resource.Texture.Height)
                    continue;

                Color[] pixels = ExtractPixels(resource, sourceX, sourceY, sprite.Width, sprite.Height);
                resolved = new ResolvedSprite(sprite, pixels);
                return true;
            }

            return false;
        }

        private static Color[] ExtractPixels(DecodedResource resource, int sourceX, int sourceY, int width, int height)
        {
            var pixels = new Color[width * height];
            int copyWidth = Math.Max(0, Math.Min(width, resource.Texture.Width - sourceX));
            int copyHeight = Math.Max(0, Math.Min(height, resource.Texture.Height - sourceY));

            for (int y = 0; y < copyHeight; y++)
            {
                int sourceIndex = (sourceY + y) * resource.Texture.Width + sourceX;
                int destIndex = y * width;
                Array.Copy(resource.Pixels, sourceIndex, pixels, destIndex, copyWidth);
            }

            return pixels;
        }

        private static (int X, int Y) GetFinalScreenPosition(int groupIndex, TitleSprite sprite)
        {
            return groupIndex switch
            {
                1 => (255 + sprite.LocalX, sprite.LocalY),
                3 when sprite.LocalX > 0 => (sprite.LocalX - 1, sprite.LocalY),
                _ => (sprite.LocalX, sprite.LocalY)
            };
        }

        private static IEnumerable<int> GetSpriteDrawOrder(int groupIndex, int spriteCount)
        {
            if (groupIndex == 2 && spriteCount == 4)
            {
                yield return 1;
                yield return 0;
                yield return 2;
                yield return 3;
                yield break;
            }

            for (int i = 0; i < spriteCount; i++)
                yield return i;
        }

        private Texture2D CreateCroppedCompositeTexture(List<PlacedSprite> sprites)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            foreach (PlacedSprite sprite in sprites)
            {
                minX = Math.Min(minX, sprite.X);
                minY = Math.Min(minY, sprite.Y);
                maxX = Math.Max(maxX, sprite.X + sprite.Width);
                maxY = Math.Max(maxY, sprite.Y + sprite.Height);
            }

            int width = Math.Max(1, maxX - minX);
            int height = Math.Max(1, maxY - minY);
            var pixels = new Color[width * height];

            foreach (PlacedSprite sprite in sprites)
            {
                int destX = sprite.X - minX;
                int destY = sprite.Y - minY;
                BlitNonTransparent(
                    sprite.Pixels,
                    sprite.Width,
                    sprite.Height,
                    pixels,
                    width,
                    height,
                    destX,
                    destY);
            }

            return CreateTexture(pixels, width, height);
        }

        private Texture2D CreateCanvasTexture(List<PlacedSprite> sprites, int width, int height)
        {
            var pixels = new Color[width * height];

            foreach (PlacedSprite sprite in sprites)
            {
                BlitNonTransparent(
                    sprite.Pixels,
                    sprite.Width,
                    sprite.Height,
                    pixels,
                    width,
                    height,
                    sprite.X,
                    sprite.Y);
            }

            return CreateTexture(pixels, width, height);
        }

        private Texture2D CreateTexture(Color[] pixels, int width, int height)
        {
            var texture = new Texture2D(GraphicsDevice, width, height, false, SurfaceFormat.Color);
            texture.SetData(pixels);
            return texture;
        }

        private static void BlitNonTransparent(
            Color[] source,
            int sourceWidth,
            int sourceHeight,
            Color[] destination,
            int destinationWidth,
            int destinationHeight,
            int destX,
            int destY)
        {
            for (int y = 0; y < sourceHeight; y++)
            {
                int targetY = destY + y;
                if (targetY < 0 || targetY >= destinationHeight)
                    continue;

                int sourceRow = y * sourceWidth;
                int destinationRow = targetY * destinationWidth;
                for (int x = 0; x < sourceWidth; x++)
                {
                    int targetX = destX + x;
                    if (targetX < 0 || targetX >= destinationWidth)
                        continue;

                    Color pixel = source[sourceRow + x];
                    if (pixel.A == 0)
                        continue;

                    destination[destinationRow + targetX] = pixel;
                }
            }
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
