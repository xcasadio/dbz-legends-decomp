using System.Collections.Generic;
using System.IO;

namespace PsxTools;

/// <summary>
/// Structural loader for CH_BIN1/2/3 files.
///
/// The loader follows the compact format reference in docs/structure-ch-bin-files.md:
/// - compile-time pointer base 0x801A3800
/// - CHBinMeshEntry = 7 dwords
/// - segment list strides 8 / 16 / 8
/// - AnimStream framed as 0x0000, countdown, words..., 0x0000, next_countdown...
///
/// It intentionally parses only proven structures plus bounded previews.
/// </summary>
public static class ChBinLoader
{
    public const uint CompileTimeBase = 0x801A3800;
    public const uint RuntimeBase = 0x801D2000;

    private const int EntrySize = 0x1C;
    private const int VertexSegmentSize = 0x08;
    private const int MeshSegmentSize = 0x10;
    private const int LightingSegmentSize = 0x08;
    private const int MaxSegmentPreviewCount = 12;
    private const int MaxParsedBatchCount = 256;
    private const int MaxCommandPreviewCount = 12;
    private const int MaxWarningCount = 16;

    public static ChBinFile Load(string filePath)
    {
        string sourceName = Path.GetFileName(filePath);
        return Load(File.ReadAllBytes(filePath), sourceName, filePath);
    }

    public static ChBinFile Load(byte[] data, string sourceName = "buffer", string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 12)
            throw new InvalidDataException("CH_BIN file is too small to contain a header.");

        uint headerWord0 = LE32(data, 0x00);
        ushort relocLoopBound = (ushort)(headerWord0 & 0xFFFF);
        ushort headerFlags = (ushort)(headerWord0 >> 16);

        if (relocLoopBound < 3)
            throw new InvalidDataException($"Invalid reloc_loop_bound: {relocLoopBound}.");

        int headerByteCount = relocLoopBound * 4;
        if (headerByteCount > data.Length)
            throw new InvalidDataException("Header exceeds file length.");

        var warnings = new List<string>();
        var headerSlots = new List<ChBinHeaderSlot>(relocLoopBound);

        for (int slotIndex = 0; slotIndex < relocLoopBound; slotIndex++)
        {
            int slotOffset = slotIndex * 4;
            uint rawValue = LE32(data, slotOffset);
            headerSlots.Add(new ChBinHeaderSlot
            {
                Index = slotIndex,
                ByteOffset = slotOffset,
                RawValue = rawValue,
                IsRuntimeRelocated = slotIndex >= 2 && slotIndex < relocLoopBound,
                FileOffset = GetFileOffsetOrNull(rawValue, data.Length)
            });
        }

        uint entryCountRaw = LE32(data, 0x04);
        uint ptrEntryTable = LE32(data, 0x08);
        if (!TryPointerToFileOffset(ptrEntryTable, data.Length, out int entryTableFileOffset))
            throw new InvalidDataException($"Entry table pointer 0x{ptrEntryTable:X8} is outside the file.");

        int availableEntries = Math.Max(0, (data.Length - entryTableFileOffset) / EntrySize);
        int parsedEntryCount = (int)Math.Min(entryCountRaw, (uint)availableEntries);
        if (parsedEntryCount != entryCountRaw)
        {
            AddWarning(
                warnings,
                $"Entry table truncated: header says {entryCountRaw}, file contains {parsedEntryCount} complete entries.");
        }

        var entries = new List<ChBinEntry>(parsedEntryCount);
        for (int index = 0; index < parsedEntryCount; index++)
        {
            int entryFileOffset = entryTableFileOffset + index * EntrySize;

            uint entryIdPacked = LE32(data, entryFileOffset + 0x00);
            uint primitiveCountPacked = LE32(data, entryFileOffset + 0x04);
            uint unknown0x08 = LE32(data, entryFileOffset + 0x08);
            uint ptrVertexSegmentList = LE32(data, entryFileOffset + 0x0C);
            uint ptrMeshSegmentList = LE32(data, entryFileOffset + 0x10);
            uint ptrLightingSegmentList = LE32(data, entryFileOffset + 0x14);
            uint ptrAnimStream = LE32(data, entryFileOffset + 0x18);

            int? vertexListFileOffset = GetFileOffsetOrNull(ptrVertexSegmentList, data.Length);
            int? meshListFileOffset = GetFileOffsetOrNull(ptrMeshSegmentList, data.Length);
            int? lightingListFileOffset = GetFileOffsetOrNull(ptrLightingSegmentList, data.Length);
            int? animStreamFileOffset = GetFileOffsetOrNull(ptrAnimStream, data.Length);

            if (ptrVertexSegmentList != 0 && !vertexListFileOffset.HasValue)
                AddWarning(warnings, $"Entry E{index:D2}: vertex list pointer 0x{ptrVertexSegmentList:X8} is outside the file.");
            if (ptrMeshSegmentList != 0 && !meshListFileOffset.HasValue)
                AddWarning(warnings, $"Entry E{index:D2}: mesh list pointer 0x{ptrMeshSegmentList:X8} is outside the file.");
            if (ptrLightingSegmentList != 0 && !lightingListFileOffset.HasValue)
                AddWarning(warnings, $"Entry E{index:D2}: lighting list pointer 0x{ptrLightingSegmentList:X8} is outside the file.");
            if (ptrAnimStream != 0 && !animStreamFileOffset.HasValue)
                AddWarning(warnings, $"Entry E{index:D2}: anim stream pointer 0x{ptrAnimStream:X8} is outside the file.");

            SegmentPreviewResult<ChBinVertexSegmentEntry> vertexPreview = vertexListFileOffset.HasValue
                ? ReadVertexSegments(data, vertexListFileOffset.Value)
                : SegmentPreviewResult<ChBinVertexSegmentEntry>.Empty;

            SegmentPreviewResult<ChBinMeshSegmentEntry> meshPreview = meshListFileOffset.HasValue
                ? ReadMeshSegments(data, meshListFileOffset.Value)
                : SegmentPreviewResult<ChBinMeshSegmentEntry>.Empty;

            SegmentPreviewResult<ChBinLightingSegmentEntry> lightingPreview = lightingListFileOffset.HasValue
                ? ReadLightingSegments(data, lightingListFileOffset.Value)
                : SegmentPreviewResult<ChBinLightingSegmentEntry>.Empty;

            AnimPreviewResult animPreview = animStreamFileOffset.HasValue
                ? ReadAnimBatches(data, animStreamFileOffset.Value)
                : AnimPreviewResult.Empty;

            entries.Add(new ChBinEntry
            {
                Index = index,
                FileOffset = entryFileOffset,
                EntryIdPacked = entryIdPacked,
                PrimitiveCountPacked = primitiveCountPacked,
                Unknown0x08 = unknown0x08,
                PtrVertexSegmentList = ptrVertexSegmentList,
                PtrMeshSegmentList = ptrMeshSegmentList,
                PtrLightingSegmentList = ptrLightingSegmentList,
                PtrAnimStream = ptrAnimStream,
                VertexListFileOffset = vertexListFileOffset,
                MeshListFileOffset = meshListFileOffset,
                LightingListFileOffset = lightingListFileOffset,
                AnimStreamFileOffset = animStreamFileOffset,
                VertexSegments = vertexPreview.Items,
                MeshSegments = meshPreview.Items,
                LightingSegments = lightingPreview.Items,
                AnimBatches = animPreview.Batches,
                VertexPreviewTruncated = vertexPreview.Truncated,
                MeshPreviewTruncated = meshPreview.Truncated,
                LightingPreviewTruncated = lightingPreview.Truncated,
                AnimPreviewTruncated = animPreview.Truncated,
                AnimStreamMarkerValid = animPreview.MarkerValid
            });
        }

        return new ChBinFile
        {
            SourceName = sourceName,
            SourcePath = sourcePath,
            Data = data,
            RelocLoopBound = relocLoopBound,
            HeaderFlags = headerFlags,
            HeaderByteCount = headerByteCount,
            EntryCount = entryCountRaw,
            PtrEntryTable = ptrEntryTable,
            EntryTableFileOffset = entryTableFileOffset,
            HeaderSlots = headerSlots,
            Entries = entries,
            Warnings = warnings
        };
    }

    public static bool TryPointerToFileOffset(uint compileTimePointer, int fileLength, out int fileOffset)
    {
        if (compileTimePointer < CompileTimeBase)
        {
            fileOffset = -1;
            return false;
        }

        uint delta = compileTimePointer - CompileTimeBase;
        if (delta >= fileLength)
        {
            fileOffset = -1;
            return false;
        }

        fileOffset = (int)delta;
        return true;
    }

    private static int? GetFileOffsetOrNull(uint compileTimePointer, int fileLength)
        => TryPointerToFileOffset(compileTimePointer, fileLength, out int fileOffset) ? fileOffset : null;

    private static SegmentPreviewResult<ChBinVertexSegmentEntry> ReadVertexSegments(byte[] data, int listFileOffset)
    {
        var items = new List<ChBinVertexSegmentEntry>();
        bool truncated = false;

        for (int index = 0; index < MaxSegmentPreviewCount; index++)
        {
            int offset = listFileOffset + index * VertexSegmentSize;
            if (offset + VertexSegmentSize > data.Length)
                break;

            uint ptrVertices = LE32(data, offset + 0x00);
            uint countsPacked = LE32(data, offset + 0x04);
            ushort countX = (ushort)(countsPacked >> 16);
            ushort countY = (ushort)(countsPacked & 0xFFFF);
            int? dataFileOffset = GetFileOffsetOrNull(ptrVertices, data.Length);

            if (dataFileOffset is null || !IsLikelySegmentCounts(countX, countY))
                break;

            items.Add(new ChBinVertexSegmentEntry
            {
                Index = index,
                FileOffset = offset,
                PtrVertices = ptrVertices,
                DataFileOffset = dataFileOffset,
                CountsPacked = countsPacked
            });
        }

        if (items.Count == MaxSegmentPreviewCount)
        {
            int nextOffset = listFileOffset + MaxSegmentPreviewCount * VertexSegmentSize;
            truncated = nextOffset + VertexSegmentSize <= data.Length
                && TryCreateVertexSegment(data, nextOffset, MaxSegmentPreviewCount, out _);
        }

        return new SegmentPreviewResult<ChBinVertexSegmentEntry>(items, truncated);
    }

    private static bool TryCreateVertexSegment(byte[] data, int offset, int index, out ChBinVertexSegmentEntry segment)
    {
        segment = default!;

        if (offset + VertexSegmentSize > data.Length)
            return false;

        uint ptrVertices = LE32(data, offset + 0x00);
        uint countsPacked = LE32(data, offset + 0x04);
        ushort countX = (ushort)(countsPacked >> 16);
        ushort countY = (ushort)(countsPacked & 0xFFFF);
        int? dataFileOffset = GetFileOffsetOrNull(ptrVertices, data.Length);
        if (dataFileOffset is null || !IsLikelySegmentCounts(countX, countY))
            return false;

        segment = new ChBinVertexSegmentEntry
        {
            Index = index,
            FileOffset = offset,
            PtrVertices = ptrVertices,
            DataFileOffset = dataFileOffset,
            CountsPacked = countsPacked
        };
        return true;
    }

    private static SegmentPreviewResult<ChBinMeshSegmentEntry> ReadMeshSegments(byte[] data, int listFileOffset)
    {
        var items = new List<ChBinMeshSegmentEntry>();
        bool truncated = false;

        for (int index = 0; index < MaxSegmentPreviewCount; index++)
        {
            int offset = listFileOffset + index * MeshSegmentSize;
            if (offset + MeshSegmentSize > data.Length)
                break;

            if (!TryCreateMeshSegment(data, offset, index, out ChBinMeshSegmentEntry segment))
                break;

            items.Add(segment);
        }

        if (items.Count == MaxSegmentPreviewCount)
        {
            int nextOffset = listFileOffset + MaxSegmentPreviewCount * MeshSegmentSize;
            truncated = nextOffset + MeshSegmentSize <= data.Length
                && TryCreateMeshSegment(data, nextOffset, MaxSegmentPreviewCount, out _);
        }

        return new SegmentPreviewResult<ChBinMeshSegmentEntry>(items, truncated);
    }

    private static bool TryCreateMeshSegment(byte[] data, int offset, int index, out ChBinMeshSegmentEntry segment)
    {
        segment = default!;

        if (offset + MeshSegmentSize > data.Length)
            return false;

        uint ptrPrimitiveIndices = LE32(data, offset + 0x00);
        uint ptrUvTable = LE32(data, offset + 0x04);
        uint ptrColorTable = LE32(data, offset + 0x08);
        uint countsPacked = LE32(data, offset + 0x0C);

        int? primitiveIndicesFileOffset = GetFileOffsetOrNull(ptrPrimitiveIndices, data.Length);
        int? uvTableFileOffset = GetFileOffsetOrNull(ptrUvTable, data.Length);
        int? colorTableFileOffset = GetFileOffsetOrNull(ptrColorTable, data.Length);

        ushort countX = (ushort)(countsPacked >> 16);
        ushort countY = (ushort)(countsPacked & 0xFFFF);

        if (primitiveIndicesFileOffset is null
            || uvTableFileOffset is null
            || colorTableFileOffset is null
            || !IsLikelySegmentCounts(countX, countY))
        {
            return false;
        }

        segment = new ChBinMeshSegmentEntry
        {
            Index = index,
            FileOffset = offset,
            PtrPrimitiveIndices = ptrPrimitiveIndices,
            PrimitiveIndicesFileOffset = primitiveIndicesFileOffset,
            PtrUvTable = ptrUvTable,
            UvTableFileOffset = uvTableFileOffset,
            PtrColorTable = ptrColorTable,
            ColorTableFileOffset = colorTableFileOffset,
            CountsPacked = countsPacked
        };
        return true;
    }

    private static SegmentPreviewResult<ChBinLightingSegmentEntry> ReadLightingSegments(byte[] data, int listFileOffset)
    {
        var items = new List<ChBinLightingSegmentEntry>();
        bool truncated = false;

        for (int index = 0; index < MaxSegmentPreviewCount; index++)
        {
            int offset = listFileOffset + index * LightingSegmentSize;
            if (offset + LightingSegmentSize > data.Length)
                break;

            if (!TryCreateLightingSegment(data, offset, index, out ChBinLightingSegmentEntry segment))
                break;

            items.Add(segment);
        }

        if (items.Count == MaxSegmentPreviewCount)
        {
            int nextOffset = listFileOffset + MaxSegmentPreviewCount * LightingSegmentSize;
            truncated = nextOffset + LightingSegmentSize <= data.Length
                && TryCreateLightingSegment(data, nextOffset, MaxSegmentPreviewCount, out _);
        }

        return new SegmentPreviewResult<ChBinLightingSegmentEntry>(items, truncated);
    }

    private static bool TryCreateLightingSegment(byte[] data, int offset, int index, out ChBinLightingSegmentEntry segment)
    {
        segment = default!;

        if (offset + LightingSegmentSize > data.Length)
            return false;

        uint ptrLightingValues = LE32(data, offset + 0x00);
        uint countsPacked = LE32(data, offset + 0x04);
        int? lightingValuesFileOffset = GetFileOffsetOrNull(ptrLightingValues, data.Length);
        ushort countX = (ushort)(countsPacked >> 16);
        ushort countY = (ushort)(countsPacked & 0xFFFF);

        if (lightingValuesFileOffset is null || !IsLikelySegmentCounts(countX, countY))
            return false;

        segment = new ChBinLightingSegmentEntry
        {
            Index = index,
            FileOffset = offset,
            PtrLightingValues = ptrLightingValues,
            LightingValuesFileOffset = lightingValuesFileOffset,
            CountsPacked = countsPacked
        };
        return true;
    }

    private static AnimPreviewResult ReadAnimBatches(byte[] data, int streamFileOffset)
    {
        var batches = new List<ChBinAnimBatch>();
        bool truncated = false;
        bool markerValid = true;
        int cursor = streamFileOffset;

        for (int batchIndex = 0; batchIndex < MaxParsedBatchCount; batchIndex++)
        {
            if (cursor + 4 > data.Length)
            {
                truncated = cursor < data.Length;
                break;
            }

            ushort marker = LE16(data, cursor + 0x00);
            ushort countdown = LE16(data, cursor + 0x02);
            if (marker != 0)
            {
                markerValid = false;
                break;
            }

            int wordsStart = cursor + 4;
            int pos = wordsStart;
            var words = new List<ushort>();
            bool batchEndsStream = false;

            while (pos + 2 <= data.Length)
            {
                ushort word0 = LE16(data, pos);
                if (word0 == 0)
                    break;

                int availableWords = (data.Length - pos) / 2;
                int sizeWords = Math.Clamp(GuessCommandSizeFromData(data, pos), 1, Math.Max(1, availableWords));
                if (sizeWords > availableWords)
                {
                    sizeWords = availableWords;
                    truncated = true;
                }

                for (int wordIndex = 0; wordIndex < sizeWords; wordIndex++)
                    words.Add(LE16(data, pos + wordIndex * 2));

                if ((word0 & 0x00FF) == 0x19)
                {
                    batchEndsStream = true;
                    pos += sizeWords * 2;
                    break;
                }

                pos += sizeWords * 2;

                if (truncated)
                    break;
            }

            bool terminatorFound = pos + 2 <= data.Length && LE16(data, pos) == 0;

            batches.Add(new ChBinAnimBatch
            {
                Index = batchIndex,
                FileOffset = cursor,
                Marker = marker,
                Countdown = countdown,
                Words = words.ToArray(),
                Commands = BuildCommandPreview(words),
                TerminatorFound = terminatorFound
            });

            if (batchEndsStream)
                break;

            if (!terminatorFound)
                break;

            cursor = pos;
        }

        if (batches.Count == MaxParsedBatchCount)
        {
            if (cursor + 4 < data.Length)
                truncated = true;
        }

        return new AnimPreviewResult(batches, truncated, markerValid);
    }

    private static int GuessCommandSizeFromData(byte[] data, int byteOffset)
    {
        int availableWords = Math.Min(8, (data.Length - byteOffset) / 2);
        if (availableWords <= 0)
            return 1;

        ushort[] lookahead = new ushort[availableWords];
        for (int index = 0; index < availableWords; index++)
            lookahead[index] = LE16(data, byteOffset + index * 2);

        return GuessCommandSize(lookahead, 0);
    }

    private static IReadOnlyList<ChBinAnimCommandPreview> BuildCommandPreview(List<ushort> words)
    {
        var commands = new List<ChBinAnimCommandPreview>();
        int cursor = 0;

        while (cursor < words.Count && commands.Count < MaxCommandPreviewCount)
        {
            ushort word0 = words[cursor];
            byte opcode = (byte)(word0 & 0xFF);
            int sizeWords = GuessCommandSize(words, cursor);
            sizeWords = Math.Clamp(sizeWords, 1, Math.Max(1, words.Count - cursor));

            int argCount = Math.Max(0, sizeWords - 1);
            ushort[] args = new ushort[argCount];
            for (int i = 0; i < argCount; i++)
                args[i] = words[cursor + 1 + i];

            commands.Add(new ChBinAnimCommandPreview
            {
                WordIndex = cursor,
                RawWord0 = word0,
                Mnemonic = DescribeOpcode(word0),
                SizeWords = sizeWords,
                Arguments = args
            });

            if (opcode == 0x19)
                break;

            cursor += sizeWords;
        }

        return commands;
    }

    private static int GuessCommandSize(IReadOnlyList<ushort> words, int cursor)
    {
        ushort word0 = words[cursor];
        byte opcode = (byte)(word0 & 0xFF);
        byte high8 = (byte)(word0 >> 8);

        return opcode switch
        {
            0x01 => 1,
            0x02 => 2,
            0x03 => 7,
            0x05 => 4,
            0x06 or 0x07 or 0x08 => cursor + 1 < words.Count ? 2 + CountTransformSpecs(words[cursor + 1]) : 1,
            0x09 => 4,
            0x0F => 4,
            0x0A => 2,
            0x0B => (high8 & 0x80) != 0 ? 7 : 1,
            0x0C => 5,
            0x0D => 4,
            0x0E => 4,
            0x15 or 0x16 => 2,
            0x1D => 4,
            0x1E => 3,
            0x1F => 4,
            0x10 => 3,
            0x11 => 2,
            0x12 => 3,
            0x13 => cursor + 2 < words.Count && (words[cursor + 2] & 0x8000) != 0 ? 5 : 4,
            0x14 => (high8 & 0x03) == 0x02 ? 3 : 2,
            0x17 => GuessBitChkSize(high8),
            0x18 => 3,
            0x19 => 1,
            0x1B => 7,
            0x20 => 6,
            0x21 => (high8 & 0x80) != 0 ? 1 : 3,
            0x23 => (high8 & 0x03) == 0 ? 3 : 1,
            0x25 => cursor + 4 < words.Count ? 5 + CountXySpecs(words[cursor + 2], words[cursor + 3], words[cursor + 4]) : 1,
            0x27 => (high8 & 0x80) != 0 ? 1 : 5,
            0x2C => 1,
            0x2D => 2,
            0x2F => (high8 & 0xC0) == 0x80 ? 1 : 2,
            _ => 1,
        };
    }

    private static int GuessBitChkSize(byte high8)
    {
        return (high8 & 0xC0) switch
        {
            0x40 => 4,
            0x80 => 3,
            0xC0 => 4,
            _ => 2,
        };
    }

    private static int CountTransformSpecs(ushort word1)
    {
        int count = 0;
        if (((word1 >> 0) & 0x0F) != 0x0F)
            count++;
        if (((word1 >> 5) & 0x0F) != 0x0F)
            count++;
        if (((word1 >> 10) & 0x0F) != 0x0F)
            count++;
        return count;
    }

    private static int CountXySpecs(ushort word2, ushort word3, ushort word4)
    {
        int count = 0;
        count += CountPackedSpecs(word2, 3);
        count += CountPackedSpecs(word3, 3);
        count += CountPackedSpecs(word4, 2);
        return count;
    }

    private static int CountPackedSpecs(ushort packed, int specCount)
    {
        int count = 0;
        for (int index = 0; index < specCount; index++)
        {
            int shift = index * 5;
            if (((packed >> shift) & 0x1F) != 0x0F)
                count++;
        }

        return count;
    }

    private static string DescribeOpcode(ushort word0)
    {
        byte opcode = (byte)(word0 & 0xFF);
        return opcode switch
        {
            0x01 => "nop_set",
            0x02 => "table_set",
            0x03 => "load_set",
            0x05 => "render_state",
            0x06 => "trans_set",
            0x07 => "rot_set",
            0x08 => "scl_set",
            0x09 => "cul_set",
            0x0A => "pri_set",
            0x0B => ((word0 & 0x8000) != 0) ? "tex_set_init" : "tex_set_update",
            0x0C => "eye_set",
            0x0D => "tpclut_set",
            0x0E => "rgb_set",
            0x0F => "cmp_set",
            0x10 => "x_add_set",
            0x11 => "parts_link",
            0x12 => "xmax_set",
            0x13 => "rgb2_set",
            0x14 => "utility",
            0x15 => "objint_get",
            0x16 => "objlong_get",
            0x17 => "bit_chk",
            0x18 => "bit_set",
            0x19 => "end_set",
            0x1D => "moveexp_set",
            0x1E => "dist_set",
            0x1F => "move_set",
            0x20 => "uv0123_set",
            0x21 => "eff_set",
            0x1B => "base_culY",
            0x25 => "xy0123_set",
            0x23 => "if_set",
            0x27 => "ch_eff_set",
            0x2C => "cheff_wait",
            0x2D => "chse_call",
            0x2F => "voice_call",
            _ => $"op_{opcode:X2}",
        };
    }

    private static bool IsLikelySegmentCounts(ushort countX, ushort countY)
    {
        if (countX == 0 || countY == 0)
            return false;

        return countX <= 0x400 && countY <= 0x400;
    }

    private static void AddWarning(List<string> warnings, string message)
    {
        if (warnings.Count >= MaxWarningCount)
            return;

        warnings.Add(message);
    }

    private static ushort LE16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);
    private static uint LE32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);

    private readonly record struct SegmentPreviewResult<T>(IReadOnlyList<T> Items, bool Truncated)
    {
        public static SegmentPreviewResult<T> Empty { get; } = new(Array.Empty<T>(), false);
    }

    private readonly record struct AnimPreviewResult(IReadOnlyList<ChBinAnimBatch> Batches, bool Truncated, bool MarkerValid)
    {
        public static AnimPreviewResult Empty { get; } = new(Array.Empty<ChBinAnimBatch>(), false, true);
    }
}

public sealed class ChBinFile
{
    public string SourceName { get; init; } = string.Empty;
    public string? SourcePath { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public ushort RelocLoopBound { get; init; }
    public ushort HeaderFlags { get; init; }
    public int HeaderByteCount { get; init; }
    public uint EntryCount { get; init; }
    public uint PtrEntryTable { get; init; }
    public int EntryTableFileOffset { get; init; }
    public IReadOnlyList<ChBinHeaderSlot> HeaderSlots { get; init; } = Array.Empty<ChBinHeaderSlot>();
    public IReadOnlyList<ChBinEntry> Entries { get; init; } = Array.Empty<ChBinEntry>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class ChBinHeaderSlot
{
    public int Index { get; init; }
    public int ByteOffset { get; init; }
    public uint RawValue { get; init; }
    public bool IsRuntimeRelocated { get; init; }
    public int? FileOffset { get; init; }
}

public sealed class ChBinEntry
{
    public int Index { get; init; }
    public int FileOffset { get; init; }

    public uint EntryIdPacked { get; init; }
    public ushort EntryIdLow => (ushort)(EntryIdPacked & 0xFFFF);
    public ushort EntryIdHigh => (ushort)(EntryIdPacked >> 16);
    public byte PartId => (byte)(EntryIdLow & 0xFF);
    public byte GroupId => (byte)(EntryIdLow >> 8);
    public byte EntryFlags => (byte)(EntryIdHigh & 0xFF);
    public byte EntryExtra => (byte)(EntryIdHigh >> 8);

    public uint PrimitiveCountPacked { get; init; }
    public ushort PrimitiveCountLow => (ushort)(PrimitiveCountPacked & 0xFFFF);
    public ushort PrimitiveCountHigh => (ushort)(PrimitiveCountPacked >> 16);
    public bool IsRenderable => PrimitiveCountLow > 0;
    public bool HasAnimation => PtrAnimStream != 0;

    public uint Unknown0x08 { get; init; }
    public ushort Unknown0x08Low => (ushort)(Unknown0x08 & 0xFFFF);
    public ushort Unknown0x08High => (ushort)(Unknown0x08 >> 16);

    public uint PtrVertexSegmentList { get; init; }
    public int? VertexListFileOffset { get; init; }

    public uint PtrMeshSegmentList { get; init; }
    public int? MeshListFileOffset { get; init; }

    public uint PtrLightingSegmentList { get; init; }
    public int? LightingListFileOffset { get; init; }

    public uint PtrAnimStream { get; init; }
    public int? AnimStreamFileOffset { get; init; }

    public IReadOnlyList<ChBinVertexSegmentEntry> VertexSegments { get; init; } = Array.Empty<ChBinVertexSegmentEntry>();
    public IReadOnlyList<ChBinMeshSegmentEntry> MeshSegments { get; init; } = Array.Empty<ChBinMeshSegmentEntry>();
    public IReadOnlyList<ChBinLightingSegmentEntry> LightingSegments { get; init; } = Array.Empty<ChBinLightingSegmentEntry>();
    public IReadOnlyList<ChBinAnimBatch> AnimBatches { get; init; } = Array.Empty<ChBinAnimBatch>();

    public bool VertexPreviewTruncated { get; init; }
    public bool MeshPreviewTruncated { get; init; }
    public bool LightingPreviewTruncated { get; init; }
    public bool AnimPreviewTruncated { get; init; }
    public bool AnimStreamMarkerValid { get; init; }
}

public sealed class ChBinVertexSegmentEntry
{
    public int Index { get; init; }
    public int FileOffset { get; init; }
    public uint PtrVertices { get; init; }
    public int? DataFileOffset { get; init; }
    public uint CountsPacked { get; init; }
    public ushort CountX => (ushort)(CountsPacked >> 16);
    public ushort CountY => (ushort)(CountsPacked & 0xFFFF);
    public int CellCount => CountX * CountY;
}

public sealed class ChBinMeshSegmentEntry
{
    public int Index { get; init; }
    public int FileOffset { get; init; }
    public uint PtrPrimitiveIndices { get; init; }
    public int? PrimitiveIndicesFileOffset { get; init; }
    public uint PtrUvTable { get; init; }
    public int? UvTableFileOffset { get; init; }
    public uint PtrColorTable { get; init; }
    public int? ColorTableFileOffset { get; init; }
    public uint CountsPacked { get; init; }
    public ushort CountX => (ushort)(CountsPacked >> 16);
    public ushort CountY => (ushort)(CountsPacked & 0xFFFF);
    public int CellCount => CountX * CountY;
}

public sealed class ChBinLightingSegmentEntry
{
    public int Index { get; init; }
    public int FileOffset { get; init; }
    public uint PtrLightingValues { get; init; }
    public int? LightingValuesFileOffset { get; init; }
    public uint CountsPacked { get; init; }
    public ushort CountX => (ushort)(CountsPacked >> 16);
    public ushort CountY => (ushort)(CountsPacked & 0xFFFF);
    public int CellCount => CountX * CountY;
}

public sealed class ChBinAnimBatch
{
    public int Index { get; init; }
    public int FileOffset { get; init; }
    public ushort Marker { get; init; }
    public ushort Countdown { get; init; }
    public ushort[] Words { get; init; } = Array.Empty<ushort>();
    public IReadOnlyList<ChBinAnimCommandPreview> Commands { get; init; } = Array.Empty<ChBinAnimCommandPreview>();
    public bool TerminatorFound { get; init; }
}

public sealed class ChBinAnimCommandPreview
{
    public int WordIndex { get; init; }
    public ushort RawWord0 { get; init; }
    public string Mnemonic { get; init; } = string.Empty;
    public int SizeWords { get; init; }
    public ushort[] Arguments { get; init; } = Array.Empty<ushort>();
}