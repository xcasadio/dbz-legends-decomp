using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PsxTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DbzLegendsAnalyser.Viewers
{
    /// <summary>
    /// Viewer for SUB\USAGI.B.
    /// The file is treated as a set of LZ-compressed VRAM uploads.
    /// Code-backed USAGI.B uploads are decoded from VRAM-space crops, with a mixed 4bpp/8bpp presentation for the validated views.
    /// </summary>
    public sealed class USAGI_B_View : IAnalyserView
    {
        private const int TileWidthWords = 12;
        private const int TileHeight = 48;
        private const int TileCount = 35;
        private const int TilePixelWidth = TileWidthWords * 4;
        private const int TileByteCount = TileWidthWords * TileHeight * 2;
        private const int TileSheetColumns = 7;
        private const int TileSheetRows = 5;

        private readonly record struct ChunkRecord(int Offset, int VramX, int VramY, int WidthWords, int Height)
        {
            public bool HasUploadRect => WidthWords > 0 && Height > 0;
            public int ByteCount => checked(WidthWords * Height * 2);
            public int PixelWidth4bpp => checked(WidthWords * 4);
        }

        private sealed class PaletteBank
        {
            public int ChunkIndex;
            public string Label = string.Empty;
            public ChunkRecord Chunk;
            public byte[] DecodedBytes = Array.Empty<byte>();
            public PsxImageDecoder.PsxClut Clut = null!;
        }

        private readonly record struct PaletteReference(int PaletteChunkIndex, int PaletteIndex, string SourceLabel)
        {
            public int Row => PaletteIndex / 16;
            public int Column => PaletteIndex % 16;
        }

        private readonly record struct ChunkPaletteView(
            int ChunkIndex,
            PsxImageDecoder.PsxPixelMode PixelMode,
            PaletteReference Palette,
            string SourceLabel,
            string Description);

        private readonly record struct SpriteUsage(
            int ChunkIndex,
            int Tpage,
            int U,
            int V,
            int Width,
            int Height,
            PsxImageDecoder.PsxPixelMode PixelMode,
            PaletteReference Palette,
            string SourceLabel,
            string Description);

        private readonly record struct CompositePart(
            int Tpage,
            int U,
            int V,
            int Width,
            int Height,
            int DestinationX,
            int DestinationY);

        private readonly record struct CompositeUsage(
            int ChunkIndex,
            int CanvasWidth,
            int CanvasHeight,
            PsxImageDecoder.PsxPixelMode PixelMode,
            PaletteReference Palette,
            string SourceLabel,
            string Description,
            CompositePart[] Parts);

        private sealed class ViewEntry
        {
            public string Label = string.Empty;
            public Func<Texture2D> CreateTexture = null!;
        }

        private static readonly ChunkRecord[] KnownChunks =
        {
            new(0x000000, 0x000, 0x100, 0x0A0, 0x0F0),
            new(0x011704, 0x0A0, 0x100, 0x0A0, 0x0F0),
            new(0x0173A8, 0x140, 0x100, 0x0A0, 0x0F0),
            new(0x022934, 0x1E0, 0x100, 0x0A0, 0x0F0),
            new(0x029C04, 0x340, 0x178, 0x02C, 0x048),
            new(0x02A7A8, 0x280, 0x000, 0x040, 0x100),
            new(0x02B594, 0x380, 0x000, 0x080, 0x0F0),
            new(0x031B18, 0x2C0, 0x000, 0x040, 0x100),
            new(0x033BCC, 0x340, 0x128, 0x040, 0x050),
            new(0x034558, 0x340, 0x100, 0x040, 0x028),
            new(0x034C7C, 0x300, 0x000, 0x040, 0x100),
            new(0x036084, 0x300, 0x100, 0x040, 0x0D0),
            new(0x0377E0, 0x340, 0x000, 0x040, 0x100),
            new(0x03A9F8, 0x380, 0x100, 0x040, 0x0C0),
            new(0x03BB1C, 0x280, 0x100, 0x078, 0x0C0),
            new(0x040370, 0x000, 0x1F0, 0x100, 0x010),
            new(0x041314, 0x100, 0x1F0, 0x100, 0x010),
            new(0x042148, 0x3C0, 0x180, 0x040, 0x080),
            new(0x043B18, 0x000, 0x000, 0x000, 0x000),
        };

        private static readonly int[] PaletteChunkIndices = { 15, 16 };

        private static readonly int[] Chunk18ScreenOrder =
        {
            0x00, 0x03, 0x08, 0x09, 0x0D, 0x10, 0x15,
            0x14, 0x12, 0x13, 0x11, 0x01, 0x16, 0x0B,
            0x0E, 0x1A, 0x1B, 0x18, 0x19, 0x17, 0x04,
            0x1C, 0x1D, 0x05, 0x07, 0x0A, 0x0F, 0x1E,
            0x1F, 0x02, 0x06, 0x0C, 0x20, 0x21, 0x22,
        };

        private static readonly PaletteReference[] CodeProvenPalettes =
        {
            CreatePaletteReference(15, 0x000, 0x1F0, "BuildOptionsScreen tpage 0x10/0x12"),
            CreatePaletteReference(15, 0x000, 0x1F1, "BuildDemoSaveSlotScreen tpage 0x12/0x13"),
            CreatePaletteReference(15, 0x000, 0x1F2, "FUN_8002a178 tpage 0x15/0x17"),
            CreatePaletteReference(15, 0x000, 0x1F3, "BuildSpSaveSlotScreen tpage 0x17/0x18"),
            CreatePaletteReference(15, 0x000, 0x1F4, "BuildModeMenuScreen tpage 0x1D record 04"),
            CreatePaletteReference(15, 0x000, 0x1F5, "BuildModeMenuScreen tpage 0x1A dynamic row base"),
            CreatePaletteReference(15, 0x000, 0x1FD, "BuildModeMenuScreen tpage 0x1A variant A"),
            CreatePaletteReference(15, 0x000, 0x1FE, "BuildModeMenuScreen tpage 0x1A variant B"),
            CreatePaletteReference(15, 0x000, 0x1FF, "BuildModeMenuScreen tpage 0x1A variant C"),
            CreatePaletteReference(16, 0x100, 0x1F0, "BuildModeMenuScreen tpage 0x0E"),
            CreatePaletteReference(16, 0x100, 0x1F1, "BuildButtonConfigScreen tpage 0x1D"),
            CreatePaletteReference(16, 0x170, 0x1FA, "BuildModeMenuScreen tpage 0x0A"),
            CreatePaletteReference(16, 0x170, 0x1FB, "BuildOptionsScreen/BuildButtonConfigScreen tpage 0x0B"),
            CreatePaletteReference(16, 0x170, 0x1FC, "BuildDemoSaveSlotScreen tpage 0x1D"),
            CreatePaletteReference(16, 0x170, 0x1FD, "FUN_8002a178 tpage 0x0D"),
            CreatePaletteReference(16, 0x170, 0x1FE, "BuildSpSaveSlotScreen tpage 0x1C"),
            CreatePaletteReference(16, 0x170, 0x1F6, "ShowCardMessage tpage 0x1F"),
            CreatePaletteReference(16, 0x170, 0x1F7, "RunSoundTestScreen tpage 0x1E upper set"),
            CreatePaletteReference(16, 0x170, 0x1F8, "RunSoundTestScreen tpage 0x1E strips"),
        };

        private static readonly ChunkPaletteView[] ChunkPaletteViews =
        {
            new(4, PsxImageDecoder.PsxPixelMode.Bpp8, CreatePaletteReference(15, 0x000, 0x1F4, "BuildModeMenuScreen tpage 0x1D record 04"), "BuildModeMenuScreen", "chunk 04 full source sheet with portrait panel and star sprites"),
            new(5, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1FA, "BuildModeMenuScreen tpage 0x0A"), "BuildModeMenuScreen", "chunk 05 full page"),
            new(7, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1FB, "BuildOptionsScreen/BuildButtonConfigScreen tpage 0x0B"), "BuildOptionsScreen", "chunk 07 tilesheet full page"),
            new(9, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1FC, "BuildDemoSaveSlotScreen tpage 0x1D"), "BuildDemoSaveSlotScreen", "chunk 09 full record"),
            new(10, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1FD, "BuildDemoSaveSlotScreen/BuildSpSaveSlotScreen tpage 0x0C"), "BuildDemoSaveSlotScreen", "chunk 10 full page"),
            new(11, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1FE, "BuildSpSaveSlotScreen tpage 0x1C"), "BuildSpSaveSlotScreen", "chunk 11 full page"),
            new(12, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1FD, "FUN_8002a178 tpage 0x0D"), "FUN_8002a178", "chunk 12 full page"),
            new(13, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1F7, "RunSoundTestScreen tpage 0x1E upper set"), "RunSoundTestScreen", "chunk 13 tilesheet palette A"),
            new(13, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1F8, "RunSoundTestScreen tpage 0x1E strips"), "RunSoundTestScreen", "chunk 13 tilesheet palette B"),
            new(17, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1F6, "ShowCardMessage tpage 0x1F"), "ShowCardMessage", "chunk 17 full record palette"),
        };

        private static readonly SpriteUsage[] SpriteUsages =
        {
            new(4, 0x1D, 0x00, 0x78, 0x47, 0x46, PsxImageDecoder.PsxPixelMode.Bpp8, CreatePaletteReference(15, 0x000, 0x1F4, "BuildModeMenuScreen tpage 0x1D record 04"), "BuildModeMenuScreen", "chunk 04 portrait panel"),
            new(6, 0x0E, 0x09, 0x00, 0x3B, 0xF0, PsxImageDecoder.PsxPixelMode.Bpp8, CreatePaletteReference(16, 0x100, 0x1F0, "BuildModeMenuScreen repeated strip"), "BuildModeMenuScreen", "chunk 06 repeated strip source"),
            new(6, 0x0E, 0x48, 0x30, 0x7D, 0x60, PsxImageDecoder.PsxPixelMode.Bpp8, CreatePaletteReference(16, 0x100, 0x1F0, "BuildModeMenuScreen banner sprite"), "BuildModeMenuScreen", "chunk 06 banner crop"),
            new(8, 0x1D, 0x00, 0x28, 0x80, 0x50, PsxImageDecoder.PsxPixelMode.Bpp8, CreatePaletteReference(16, 0x100, 0x1F1, "BuildButtonConfigScreen tpage 0x1D"), "BuildButtonConfigScreen", "chunk 08 animated card source"),
            new(14, 0x1A, 0x00, 0x00, 0x50, 0x30, PsxImageDecoder.PsxPixelMode.Bpp8, CreatePaletteReference(15, 0x000, 0x1F5, "BuildModeMenuScreen selector state 0"), "BuildModeMenuScreen", "chunk 14 selector tile state 0"),
            new(14, 0x1A, 0x00, 0x90, 0x50, 0x30, PsxImageDecoder.PsxPixelMode.Bpp8, CreatePaletteReference(15, 0x000, 0x1FD, "BuildModeMenuScreen tpage 0x1A variant A"), "BuildModeMenuScreen", "chunk 14 selector variant A"),
            new(14, 0x1A, 0x50, 0x90, 0x50, 0x30, PsxImageDecoder.PsxPixelMode.Bpp8, CreatePaletteReference(15, 0x000, 0x1FE, "BuildModeMenuScreen tpage 0x1A variant B"), "BuildModeMenuScreen", "chunk 14 selector variant B"),
            new(14, 0x1A, 0xA0, 0x90, 0x50, 0x30, PsxImageDecoder.PsxPixelMode.Bpp8, CreatePaletteReference(15, 0x000, 0x1FF, "BuildModeMenuScreen tpage 0x1A variant C"), "BuildModeMenuScreen", "chunk 14 selector variant C"),
            new(17, 0x1F, 0x00, 0x80, 0x50, 0x10, PsxImageDecoder.PsxPixelMode.Bpp4, CreatePaletteReference(16, 0x170, 0x1F6, "ShowCardMessage tpage 0x1F"), "ShowCardMessage", "chunk 17 case 5 strip"),
        };

        private static readonly CompositeUsage[] CompositeUsages =
        {
            new(
                0,
                0x200,
                0x0F0,
                PsxImageDecoder.PsxPixelMode.Bpp8,
                CreatePaletteReference(15, 0x000, 0x1F0, "BuildOptionsScreen tpage 0x10/0x12"),
                "BuildOptionsScreen",
                "512x240 background composite; tpage 0x12 spans the tail of chunk 00 and the head of chunk 01",
                new[]
                {
                    new CompositePart(0x10, 0x00, 0x00, 0x100, 0x0F0, 0x000, 0x000),
                    new CompositePart(0x12, 0x00, 0x00, 0x100, 0x0F0, 0x100, 0x000),
                }),
            new(
                1,
                0x140,
                0x0F0,
                PsxImageDecoder.PsxPixelMode.Bpp8,
                CreatePaletteReference(15, 0x000, 0x1F1, "BuildDemoSaveSlotScreen tpage 0x12/0x13"),
                "BuildDemoSaveSlotScreen",
                "320x240 background composite",
                new[]
                {
                    new CompositePart(0x12, 0x40, 0x00, 0x040, 0x0F0, 0x000, 0x000),
                    new CompositePart(0x13, 0x00, 0x00, 0x100, 0x0F0, 0x040, 0x000),
                }),
            new(
                2,
                0x140,
                0x0F0,
                PsxImageDecoder.PsxPixelMode.Bpp8,
                CreatePaletteReference(15, 0x000, 0x1F2, "FUN_8002a178 tpage 0x15/0x17"),
                "FUN_8002a178",
                "320x240 background composite",
                new[]
                {
                    new CompositePart(0x15, 0x00, 0x00, 0x100, 0x0F0, 0x000, 0x000),
                    new CompositePart(0x17, 0x00, 0x00, 0x040, 0x0F0, 0x100, 0x000),
                }),
            new(
                3,
                0x140,
                0x0F0,
                PsxImageDecoder.PsxPixelMode.Bpp8,
                CreatePaletteReference(15, 0x000, 0x1F3, "BuildSpSaveSlotScreen tpage 0x17/0x18"),
                "BuildSpSaveSlotScreen",
                "320x240 background composite",
                new[]
                {
                    new CompositePart(0x17, 0x40, 0x00, 0x040, 0x0F0, 0x000, 0x000),
                    new CompositePart(0x18, 0x00, 0x00, 0x100, 0x0F0, 0x040, 0x000),
                }),
        };

        private readonly ImageViewer _viewer = new ImageViewer();
        private readonly List<ViewEntry> _entries = new();
        private readonly Dictionary<int, byte[]> _decodedChunks = new();
        private readonly Dictionary<int, PaletteBank> _paletteBanks = new();
        private readonly Dictionary<(int PaletteChunkIndex, int Row), PsxImageDecoder.PsxClut> _rowCluts8bpp = new();

        private GraphicsDevice _graphicsDevice = null!;
        private Texture2D _currentTexture;
        private int _selectedIndex = -1;

        public void Initialize(string filePath, GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
            byte[] file = File.ReadAllBytes(filePath);

            DecodeChunks(file);
            BuildPaletteBanks();
            BuildEntries();

            if (_entries.Count > 0)
                OnItemSelected(0);
        }

        public void Update(GameTime gameTime, Rectangle contentBounds)
        {
            _viewer.Bounds = contentBounds;
            _viewer.Update(gameTime);
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle contentBounds)
        {
            _viewer.Bounds = contentBounds;
            _viewer.Draw(spriteBatch);
        }

        public string[] GetListItems()
            => _entries.Select(static entry => entry.Label).ToArray();

        public void OnItemSelected(int index)
        {
            if (index < 0 || index >= _entries.Count || index == _selectedIndex)
                return;

            Texture2D nextTexture = _entries[index].CreateTexture();

            Texture2D previousTexture = _currentTexture;
            _currentTexture = nextTexture;
            _viewer.Texture = nextTexture;
            _selectedIndex = index;

            previousTexture?.Dispose();
        }

        public void Dispose()
        {
            _currentTexture?.Dispose();
            _currentTexture = null;
            _viewer.Texture = null;
            _entries.Clear();
            _decodedChunks.Clear();
            _paletteBanks.Clear();
            _rowCluts8bpp.Clear();
        }

        private static PaletteReference CreatePaletteReference(int paletteChunkIndex, int cx, int cy, string sourceLabel)
        {
            ChunkRecord bank = KnownChunks[paletteChunkIndex];
            int row = cy - bank.VramY;
            int column = (cx - bank.VramX) / 0x10;
            return new PaletteReference(paletteChunkIndex, row * 0x10 + column, sourceLabel);
        }

        private void DecodeChunks(byte[] file)
        {
            _decodedChunks.Clear();

            for (int chunkIndex = 0; chunkIndex < KnownChunks.Length; chunkIndex++)
            {
                int chunkStart = KnownChunks[chunkIndex].Offset;
                int chunkEnd = chunkIndex + 1 < KnownChunks.Length ? KnownChunks[chunkIndex + 1].Offset : file.Length;
                if (chunkStart < 0 || chunkStart >= file.Length || chunkEnd <= chunkStart || chunkEnd > file.Length)
                    continue;

                byte[] compressed = new byte[chunkEnd - chunkStart];
                Array.Copy(file, chunkStart, compressed, 0, compressed.Length);

                try
                {
                    _decodedChunks[chunkIndex] = LzssDecompressor.Decompress(compressed);
                }
                catch
                {
                }
            }
        }

        private void BuildPaletteBanks()
        {
            _paletteBanks.Clear();

            foreach (int chunkIndex in PaletteChunkIndices)
            {
                if (!_decodedChunks.TryGetValue(chunkIndex, out byte[] decoded))
                    continue;

                ChunkRecord chunk = KnownChunks[chunkIndex];
                if (decoded.Length < chunk.ByteCount)
                    continue;

                int colorCount = chunk.WidthWords * chunk.Height;
                ushort[] colors = BinaryReaderHelper.ReadUShortArrayFast(decoded, 0, colorCount);

                _paletteBanks[chunkIndex] = new PaletteBank
                {
                    ChunkIndex = chunkIndex,
                    Label = $"chunk {chunkIndex:00} CLUT bank",
                    Chunk = chunk,
                    DecodedBytes = decoded,
                    Clut = new PsxImageDecoder.PsxClut(colors, ColorsPerPalette: 16),
                };
            }
        }

        private void BuildEntries()
        {
            _entries.Clear();

            foreach (ChunkPaletteView usage in ChunkPaletteViews)
            {
                ChunkPaletteView capturedUsage = usage;
                ChunkRecord chunk = KnownChunks[capturedUsage.ChunkIndex];
                _entries.Add(new ViewEntry
                {
                    Label =
                        $"[SOURCE] chunk {capturedUsage.ChunkIndex:00} " +
                        $"pal {capturedUsage.Palette.Row:00}:{capturedUsage.Palette.Column:00} " +
                        $"{DescribePixelMode(capturedUsage.PixelMode)} {GetPixelWidth(chunk, capturedUsage.PixelMode)}x{chunk.Height} {capturedUsage.SourceLabel} {capturedUsage.Description}",
                    CreateTexture = () => CreateIndexedChunkTexture(capturedUsage.ChunkIndex, capturedUsage.Palette, capturedUsage.PixelMode),
                });
            }

            foreach (SpriteUsage usage in SpriteUsages)
            {
                SpriteUsage capturedUsage = usage;
                _entries.Add(new ViewEntry
                {
                    Label =
                        $"[USAGE] chunk {capturedUsage.ChunkIndex:00} tpage 0x{capturedUsage.Tpage:X2} " +
                        $"pal {capturedUsage.Palette.Row:00}:{capturedUsage.Palette.Column:00} " +
                        $"{DescribePixelMode(capturedUsage.PixelMode)} {capturedUsage.Width}x{capturedUsage.Height} {capturedUsage.SourceLabel} {capturedUsage.Description}",
                    CreateTexture = () => CreateSpriteUsageTexture(capturedUsage),
                });
            }

            foreach (CompositeUsage usage in CompositeUsages)
            {
                CompositeUsage capturedUsage = usage;
                _entries.Add(new ViewEntry
                {
                    Label =
                        $"[COMPOSITE] chunk {capturedUsage.ChunkIndex:00} " +
                        $"{capturedUsage.CanvasWidth}x{capturedUsage.CanvasHeight} " +
                        $"{DescribePixelMode(capturedUsage.PixelMode)} pal {capturedUsage.Palette.Row:00}:{capturedUsage.Palette.Column:00} " +
                        $"{capturedUsage.SourceLabel} {capturedUsage.Description}",
                    CreateTexture = () => CreateCompositeUsageTexture(capturedUsage),
                });
            }

            for (int chunkIndex = 0; chunkIndex < KnownChunks.Length; chunkIndex++)
            {
                if (!_decodedChunks.ContainsKey(chunkIndex))
                    continue;

                if (PaletteChunkIndices.Contains(chunkIndex))
                    continue;

                if (chunkIndex == KnownChunks.Length - 1)
                {
                    BuildChunk18Entries();
                    continue;
                }

                if (!ChunkPaletteViews.Any(usage => usage.ChunkIndex == chunkIndex) &&
                    !SpriteUsages.Any(usage => usage.ChunkIndex == chunkIndex) &&
                    !CompositeUsages.Any(usage => usage.ChunkIndex == chunkIndex))
                    BuildInspectionEntries(chunkIndex);
            }
        }

        private void BuildInspectionEntries(int chunkIndex)
        {
            ChunkRecord chunk = KnownChunks[chunkIndex];
            if (!chunk.HasUploadRect)
                return;

            foreach (PaletteReference palette in CodeProvenPalettes)
            {
                if (!_paletteBanks.ContainsKey(palette.PaletteChunkIndex))
                    continue;

                PaletteReference capturedPalette = palette;
                _entries.Add(new ViewEntry
                {
                    Label =
                        $"[INSPECT] chunk {chunkIndex:00} pal {capturedPalette.Row:00}:{capturedPalette.Column:00} " +
                        $"4bpp {chunk.PixelWidth4bpp}x{chunk.Height} {capturedPalette.SourceLabel}",
                    CreateTexture = () => CreateIndexedChunkTexture(chunkIndex, capturedPalette, PsxImageDecoder.PsxPixelMode.Bpp4),
                });
            }
        }

        private void BuildChunk18Entries()
        {
            if (!_paletteBanks.ContainsKey(16))
                return;

            _entries.Add(new ViewEntry
            {
                Label = "[4bpp] chunk 18 atlas order using FUN_80031e98 per-tile CLUTs",
                CreateTexture = () => CreateChunk18ContactSheet(null),
            });

            _entries.Add(new ViewEntry
            {
                Label = "[4bpp] chunk 18 screen order via g_UsagiChunk18TileIndexMap35",
                CreateTexture = () => CreateChunk18ContactSheet(Chunk18ScreenOrder),
            });
        }

        private Texture2D CreateIndexedChunkTexture(int chunkIndex, PaletteReference palette, PsxImageDecoder.PsxPixelMode pixelMode)
        {
            byte[] decoded = _decodedChunks[chunkIndex];
            ChunkRecord chunk = KnownChunks[chunkIndex];
            PaletteBank bank = _paletteBanks[palette.PaletteChunkIndex];

            if (pixelMode == PsxImageDecoder.PsxPixelMode.Bpp8)
            {
                return PsxImageDecoder.DecodeToTexture2D(
                    _graphicsDevice,
                    decoded,
                    new PsxImageDecoder.PsxImageLayout(chunk.WidthWords, chunk.Height),
                    new PsxImageDecoder.PsxImageFormat(PsxImageDecoder.PsxPixelMode.Bpp8, Index0Transparent: false),
                    Get8bppClut(bank, palette.Row),
                    0);
            }

            return PsxImageDecoder.DecodeToTexture2D(
                _graphicsDevice,
                decoded,
                new PsxImageDecoder.PsxImageLayout(chunk.WidthWords, chunk.Height),
                new PsxImageDecoder.PsxImageFormat(pixelMode, Index0Transparent: false),
                bank.Clut,
                palette.PaletteIndex);
        }

        private Texture2D CreateSpriteUsageTexture(SpriteUsage usage)
        {
            int sourceX = checked(GetTpageBaseX(usage.Tpage) * GetPixelScale(usage.PixelMode) + usage.U);
            int sourceY = checked(GetTpageBaseY(usage.Tpage) + usage.V);
            return CreateVramUsageTexture(usage.PixelMode, usage.Palette, sourceX, sourceY, usage.Width, usage.Height);
        }

        private Texture2D CreateVramUsageTexture(PsxImageDecoder.PsxPixelMode pixelMode, PaletteReference palette, int sourceX, int sourceY, int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new InvalidDataException($"Requested VRAM crop has invalid size {width}x{height}.");

            Color[] destinationPixels = new Color[width * height];
            Array.Fill(destinationPixels, Color.Transparent);

            bool copiedAnyPixels = false;
            foreach ((int chunkIndex, byte[] _) in _decodedChunks.OrderBy(static pair => pair.Key))
            {
                if (PaletteChunkIndices.Contains(chunkIndex))
                    continue;

                ChunkRecord chunk = KnownChunks[chunkIndex];
                if (!chunk.HasUploadRect)
                    continue;

                int chunkLeft = chunk.VramX * GetPixelScale(pixelMode);
                int chunkTop = chunk.VramY;
                int chunkPixelWidth = GetPixelWidth(chunk, pixelMode);
                int chunkRight = chunkLeft + chunkPixelWidth;
                int chunkBottom = chunkTop + chunk.Height;

                int overlapLeft = Math.Max(sourceX, chunkLeft);
                int overlapTop = Math.Max(sourceY, chunkTop);
                int overlapRight = Math.Min(sourceX + width, chunkRight);
                int overlapBottom = Math.Min(sourceY + height, chunkBottom);
                if (overlapLeft >= overlapRight || overlapTop >= overlapBottom)
                    continue;

                Texture2D chunkTexture = CreateIndexedChunkTexture(chunkIndex, palette, pixelMode);
                try
                {
                    Color[] sourcePixels = new Color[chunkPixelWidth * chunk.Height];
                    chunkTexture.GetData(sourcePixels);

                    int copyWidth = overlapRight - overlapLeft;
                    int copyHeight = overlapBottom - overlapTop;
                    int sourceOffsetX = overlapLeft - chunkLeft;
                    int sourceOffsetY = overlapTop - chunkTop;
                    int destinationOffsetX = overlapLeft - sourceX;
                    int destinationOffsetY = overlapTop - sourceY;

                    for (int y = 0; y < copyHeight; y++)
                    {
                        int sourceOffset = (sourceOffsetY + y) * chunkPixelWidth + sourceOffsetX;
                        int destinationOffset = (destinationOffsetY + y) * width + destinationOffsetX;
                        Array.Copy(sourcePixels, sourceOffset, destinationPixels, destinationOffset, copyWidth);
                    }

                    copiedAnyPixels = true;
                }
                finally
                {
                    chunkTexture.Dispose();
                }
            }

            if (!copiedAnyPixels)
                throw new InvalidDataException($"Requested VRAM crop {sourceX},{sourceY} {width}x{height} does not overlap any decoded USAGI.B upload.");

            var croppedTexture = new Texture2D(_graphicsDevice, width, height, false, SurfaceFormat.Color);
            croppedTexture.SetData(destinationPixels);
            return croppedTexture;
        }

        private Texture2D CreateCompositeUsageTexture(CompositeUsage usage)
        {
            Color[] canvas = new Color[usage.CanvasWidth * usage.CanvasHeight];
            Array.Fill(canvas, Color.Transparent);

            foreach (CompositePart part in usage.Parts)
            {
                Texture2D partTexture = CreateVramUsageTexture(
                    usage.PixelMode,
                    usage.Palette,
                    checked(GetTpageBaseX(part.Tpage) * GetPixelScale(usage.PixelMode) + part.U),
                    checked(GetTpageBaseY(part.Tpage) + part.V),
                    part.Width,
                    part.Height);

                try
                {
                    Color[] partPixels = new Color[part.Width * part.Height];
                    partTexture.GetData(partPixels);
                    Blit(partPixels, part.Width, part.Height, canvas, usage.CanvasWidth, usage.CanvasHeight, part.DestinationX, part.DestinationY);
                }
                finally
                {
                    partTexture.Dispose();
                }
            }

            var compositeTexture = new Texture2D(_graphicsDevice, usage.CanvasWidth, usage.CanvasHeight, false, SurfaceFormat.Color);
            compositeTexture.SetData(canvas);
            return compositeTexture;
        }

        private Texture2D CreateChunk18ContactSheet(IReadOnlyList<int> tileOrder)
        {
            byte[] decoded = _decodedChunks[KnownChunks.Length - 1];
            int expectedByteCount = TileCount * TileByteCount;
            if (decoded.Length < expectedByteCount)
                throw new InvalidDataException("USAGI.B chunk 18 is smaller than the expected tile atlas size.");

            PaletteBank bank = _paletteBanks[16];
            IReadOnlyList<int> effectiveOrder = tileOrder ?? Enumerable.Range(0, TileCount).ToArray();
            int sheetWidth = TileSheetColumns * TilePixelWidth;
            int sheetHeight = TileSheetRows * TileHeight;
            Color[] canvas = new Color[sheetWidth * sheetHeight];

            for (int sheetIndex = 0; sheetIndex < TileCount; sheetIndex++)
            {
                int tileIndex = effectiveOrder[sheetIndex];
                byte[] tileData = new byte[TileByteCount];
                Array.Copy(decoded, tileIndex * TileByteCount, tileData, 0, TileByteCount);

                Texture2D tileTexture = PsxImageDecoder.DecodeToTexture2D(
                    _graphicsDevice,
                    tileData,
                    new PsxImageDecoder.PsxImageLayout(TileWidthWords, TileHeight),
                    new PsxImageDecoder.PsxImageFormat(PsxImageDecoder.PsxPixelMode.Bpp4),
                    bank.Clut,
                    GetChunk18PaletteIndex(tileIndex));

                try
                {
                    Color[] tilePixels = new Color[TilePixelWidth * TileHeight];
                    tileTexture.GetData(tilePixels);

                    int destX = (sheetIndex % TileSheetColumns) * TilePixelWidth;
                    int destY = (sheetIndex / TileSheetColumns) * TileHeight;
                    Blit(tilePixels, TilePixelWidth, TileHeight, canvas, sheetWidth, sheetHeight, destX, destY);
                }
                finally
                {
                    tileTexture.Dispose();
                }
            }

            var texture = new Texture2D(_graphicsDevice, sheetWidth, sheetHeight, false, SurfaceFormat.Color);
            texture.SetData(canvas);
            return texture;
        }

        private static int GetChunk18PaletteIndex(int tileIndex)
        {
            int row = tileIndex / TileSheetColumns;
            int column = tileIndex % TileSheetColumns;
            return (0x0B + row) * 0x10 + column;
        }

        private static int GetPixelScale(PsxImageDecoder.PsxPixelMode pixelMode)
            => pixelMode switch
            {
                PsxImageDecoder.PsxPixelMode.Bpp4 => 4,
                PsxImageDecoder.PsxPixelMode.Bpp8 => 2,
                _ => 1,
            };

        private static int GetPixelWidth(ChunkRecord chunk, PsxImageDecoder.PsxPixelMode pixelMode)
            => pixelMode switch
            {
                PsxImageDecoder.PsxPixelMode.Bpp4 => chunk.PixelWidth4bpp,
                PsxImageDecoder.PsxPixelMode.Bpp8 => chunk.WidthWords * 2,
                _ => chunk.WidthWords,
            };

        private static string DescribePixelMode(PsxImageDecoder.PsxPixelMode pixelMode)
            => pixelMode switch
            {
                PsxImageDecoder.PsxPixelMode.Bpp4 => "4bpp",
                PsxImageDecoder.PsxPixelMode.Bpp8 => "8bpp",
                _ => "16bpp",
            };

        private PsxImageDecoder.PsxClut Get8bppClut(PaletteBank bank, int row)
        {
            if (_rowCluts8bpp.TryGetValue((bank.ChunkIndex, row), out PsxImageDecoder.PsxClut clut))
                return clut;

            ushort[] colors = new ushort[0x100];
            Buffer.BlockCopy(bank.DecodedBytes, row * colors.Length * sizeof(ushort), colors, 0, colors.Length * sizeof(ushort));
            clut = new PsxImageDecoder.PsxClut(colors, ColorsPerPalette: 0x100);
            _rowCluts8bpp[(bank.ChunkIndex, row)] = clut;
            return clut;
        }

        private static int GetTpageBaseX(int tpage)
            => (tpage & 0x0F) * 0x40;

        private static int GetTpageBaseY(int tpage)
            => ((tpage >> 4) & 0x01) * 0x100;

        private static void Blit(
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
                if ((uint)targetY >= (uint)destinationHeight)
                    continue;

                int sourceOffset = y * sourceWidth;
                int destinationOffset = targetY * destinationWidth + destX;
                Array.Copy(source, sourceOffset, destination, destinationOffset, sourceWidth);
            }
        }
    }
}