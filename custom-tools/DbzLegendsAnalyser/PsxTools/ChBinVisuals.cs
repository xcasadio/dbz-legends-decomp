using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PsxTools;

public static class ChBinVisuals
{
    private const int VramWidth = 1024;
    private const int VramHeight = 512;

    public static ChBinVisualDocument Build(ChBinFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        IReadOnlyList<ushort> staticPaletteCandidates = CollectPreferredPaletteCandidates(file);
        IReadOnlyList<ushort> staticTPageCandidates = CollectPreferredTPageCandidates(file);
        IReadOnlyDictionary<ushort, ushort[]> paletteOverrides = LoadPaletteOverrides(file.SourcePath);
        var animator = new ChBinTextureAnimator(file, staticPaletteCandidates, staticTPageCandidates, paletteOverrides);
        IReadOnlyList<ushort> preferredPaletteCandidates = MergePaletteCandidates(
            staticPaletteCandidates,
            animator.DiscoverPaletteCandidates());
        IReadOnlyList<ushort> preferredTPageCandidates = MergeTPageCandidates(
            staticTPageCandidates,
            animator.DiscoverTPageCandidates());
        animator.SetPaletteCandidates(preferredPaletteCandidates);
        animator.SetTPageCandidates(preferredTPageCandidates);
        ushort fallbackCba = preferredPaletteCandidates.Count > 0
            ? preferredPaletteCandidates[0]
            : (ushort)0;

        IReadOnlyList<ChBinMaterialKey> discoveredMaterialKeys = animator.DiscoverVisibleUploadPages()
            .Distinct()
            .ToArray();
        ChBinTexturePage[] discoveredMaterialPages = discoveredMaterialKeys
            .Select(animator.BuildTexturePage)
            .ToArray();
        ChBinMaterialKey? fallbackMaterialKey = discoveredMaterialKeys.Count > 0
            ? discoveredMaterialKeys[0]
            : null;

        var models = new List<ChBinRenderableModel>();
        var materialKeys = new HashSet<ChBinMaterialKey>(discoveredMaterialKeys);
        int primitiveStartIndex = 0;

        foreach (ChBinEntry entry in file.Entries.Where(static entry => entry.IsRenderable))
        {
            ChBinRenderableModel? model = TryBuildModel(file, entry, fallbackMaterialKey, discoveredMaterialKeys, discoveredMaterialPages, primitiveStartIndex);
            if (model is null)
            {
                primitiveStartIndex += entry.PrimitiveCountLow;
                continue;
            }

            models.Add(model);
            foreach (ChBinTexturedPrimitive primitive in model.TexturedPrimitives)
                materialKeys.Add(primitive.BaseMaterialKey);

            primitiveStartIndex += entry.PrimitiveCountLow;
        }

        animator.InitializeModelState(models);

        animator.Reset();

        return new ChBinVisualDocument(file, models, materialKeys.OrderBy(static key => key.TPage).ThenBy(static key => key.Cba).ToArray(), animator);
    }

    private static IReadOnlyList<ushort> CollectPreferredPaletteCandidates(ChBinFile file)
    {
        var counts = new Dictionary<ushort, int>();

        foreach (ChBinEntry entry in file.Entries.Where(static entry => entry.IsRenderable))
        {
            RepeatingStreamCursor? primitiveCursor = RepeatingStreamCursor.FromMeshSegments(entry.MeshSegments, stride: 12);
            if (primitiveCursor is null)
                continue;

            for (int primitiveIndex = 0; primitiveIndex < entry.PrimitiveCountLow; primitiveIndex++)
            {
                if (!primitiveCursor.IsValid)
                    break;

                ChBinMeshSegmentEntry meshSegment = entry.MeshSegments[primitiveCursor.SegmentIndex];
                ChBinPrimitiveRecord primitive = ReadPrimitiveRecord(file.Data, primitiveCursor.CurrentOffset);
                ChBinMaterialKey materialKey = DecodeMaterial(file.Data, meshSegment.ColorTableFileOffset, primitive.ColorIndices);
                if (materialKey != default && materialKey.IsSupported)
                    IncrementCount(counts, materialKey.Cba);

                primitiveCursor.Advance();
            }
        }

        IEnumerable<KeyValuePair<ushort, int>> nonZeroCounts = counts.Where(static pair => pair.Key != 0);
        IEnumerable<KeyValuePair<ushort, int>> orderedCounts = nonZeroCounts.Any() ? nonZeroCounts : counts;
        return orderedCounts
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key)
            .Select(static pair => pair.Key)
            .ToArray();
    }

    private static IReadOnlyList<ushort> CollectPreferredTPageCandidates(ChBinFile file)
    {
        var counts = new Dictionary<ushort, int>();

        foreach (ChBinEntry entry in file.Entries.Where(static entry => entry.IsRenderable))
        {
            RepeatingStreamCursor? primitiveCursor = RepeatingStreamCursor.FromMeshSegments(entry.MeshSegments, stride: 12);
            if (primitiveCursor is null)
                continue;

            for (int primitiveIndex = 0; primitiveIndex < entry.PrimitiveCountLow; primitiveIndex++)
            {
                if (!primitiveCursor.IsValid)
                    break;

                ChBinMeshSegmentEntry meshSegment = entry.MeshSegments[primitiveCursor.SegmentIndex];
                ChBinPrimitiveRecord primitive = ReadPrimitiveRecord(file.Data, primitiveCursor.CurrentOffset);
                ChBinMaterialKey materialKey = DecodeMaterial(file.Data, meshSegment.ColorTableFileOffset, primitive.ColorIndices);
                if (materialKey != default && materialKey.IsSupported)
                    IncrementCount(counts, NormalizeTPageForTextureDecode(materialKey.TPage));

                primitiveCursor.Advance();
            }
        }

        IEnumerable<KeyValuePair<ushort, int>> nonZeroCounts = counts.Where(static pair => pair.Key != 0);
        IEnumerable<KeyValuePair<ushort, int>> orderedCounts = nonZeroCounts.Any() ? nonZeroCounts : counts;
        return orderedCounts
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key)
            .Select(static pair => pair.Key)
            .ToArray();
    }

    private static IReadOnlyList<ushort> MergePaletteCandidates(IReadOnlyList<ushort> preferredCandidates, IReadOnlyList<ushort> discoveredCandidates)
    {
        var merged = new List<ushort>();
        var seen = new HashSet<ushort>();

        foreach (ushort candidate in preferredCandidates.Concat(discoveredCandidates))
        {
            if (seen.Add(candidate))
                merged.Add(candidate);
        }

        return merged;
    }

    private static IReadOnlyList<ushort> MergeTPageCandidates(IReadOnlyList<ushort> preferredCandidates, IReadOnlyList<ushort> discoveredCandidates)
    {
        var merged = new List<ushort>();
        var seen = new HashSet<ushort>();

        foreach (ushort candidate in preferredCandidates.Concat(discoveredCandidates).Select(NormalizeTPageForTextureDecode))
        {
            if (seen.Add(candidate))
                merged.Add(candidate);
        }

        return merged;
    }

    private static ushort NormalizeTPageForTextureDecode(ushort tpage)
        => (ushort)(tpage & 0x019F);

    private static void IncrementCount(Dictionary<ushort, int> counts, ushort value)
    {
        counts.TryGetValue(value, out int currentCount);
        counts[value] = currentCount + 1;
    }

    private static IReadOnlyDictionary<ushort, ushort[]> LoadPaletteOverrides(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return new Dictionary<ushort, ushort[]>();

        string overridePath = sourcePath + ".palettes.json";
        if (!File.Exists(overridePath))
            return new Dictionary<ushort, ushort[]>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(overridePath));
            if (!document.RootElement.TryGetProperty("palettes", out JsonElement palettesElement)
                || palettesElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<ushort, ushort[]>();
            }

            var palettes = new Dictionary<ushort, ushort[]>();
            foreach (JsonProperty property in palettesElement.EnumerateObject())
            {
                if (!TryParseHexUshort(property.Name, out ushort cba)
                    || property.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var colors = new List<ushort>();
                foreach (JsonElement element in property.Value.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String
                        && TryParseHexUshort(element.GetString(), out ushort colorValue))
                    {
                        colors.Add(colorValue);
                        continue;
                    }

                    if (element.ValueKind == JsonValueKind.Number
                        && element.TryGetInt32(out int numericValue)
                        && (uint)numericValue <= ushort.MaxValue)
                    {
                        colors.Add((ushort)numericValue);
                    }
                }

                if (colors.Count > 0)
                    palettes[cba] = colors.ToArray();
            }

            return palettes;
        }
        catch
        {
            return new Dictionary<ushort, ushort[]>();
        }
    }

    private static bool TryParseHexUshort(string? text, out ushort value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string normalized = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? text[2..]
            : text;
        return ushort.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static ChBinRenderableModel? TryBuildModel(ChBinFile file, ChBinEntry entry, ChBinMaterialKey? fallbackMaterialKey, IReadOnlyList<ChBinMaterialKey> fallbackCandidateKeys, IReadOnlyList<ChBinTexturePage> fallbackCandidatePages, int primitiveStartIndex)
    {
        RepeatingStreamCursor? colorCursor = RepeatingStreamCursor.FromVertexSegments(entry.VertexSegments, stride: 4);
        RepeatingStreamCursor? primitiveCursor = RepeatingStreamCursor.FromMeshSegments(entry.MeshSegments, stride: 12);
        RepeatingStreamCursor? uvCursor = RepeatingStreamCursor.FromLightingSegments(entry.LightingSegments, stride: 8);

        if (colorCursor is null || primitiveCursor is null || uvCursor is null)
            return null;

        var wireVerts = new List<VertexPositionColor>(entry.PrimitiveCountLow * 8);
        var solidVerts = new List<VertexPositionColor>(entry.PrimitiveCountLow * 6);
        var texturedPrimitives = new List<ChBinTexturedPrimitive>(entry.PrimitiveCountLow);

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        float maxZ = float.MinValue;

        for (int primitiveIndex = 0; primitiveIndex < entry.PrimitiveCountLow; primitiveIndex++)
        {
            if (!primitiveCursor.IsValid || !uvCursor.IsValid)
                break;

            ChBinMeshSegmentEntry meshSegment = entry.MeshSegments[primitiveCursor.SegmentIndex];
            if (!meshSegment.UvTableFileOffset.HasValue)
                break;

            ChBinPrimitiveRecord primitive = ReadPrimitiveRecord(file.Data, primitiveCursor.CurrentOffset);
            ChBinUvRect uvRect = ReadUvRect(file.Data, uvCursor.CurrentOffset);
            ChBinMaterialKey materialKey = DecodeMaterial(file.Data, meshSegment.ColorTableFileOffset, primitive.ColorIndices);
            if (materialKey == default)
                materialKey = ResolveFallbackMaterialKey(uvRect, fallbackMaterialKey, fallbackCandidateKeys, fallbackCandidatePages);

            Vector3 p0 = ReadCoord(file.Data, meshSegment.UvTableFileOffset.Value, primitive.VertexIndices[0]);
            Vector3 p1 = ReadCoord(file.Data, meshSegment.UvTableFileOffset.Value, primitive.VertexIndices[1]);
            Vector3 p2 = ReadCoord(file.Data, meshSegment.UvTableFileOffset.Value, primitive.VertexIndices[2]);
            Vector3 p3 = ReadCoord(file.Data, meshSegment.UvTableFileOffset.Value, primitive.VertexIndices[3]);

            Color c0 = ReadPackedColor(file.Data, colorCursor.CurrentOffset);
            colorCursor.Advance();
            Color c1 = ReadPackedColor(file.Data, colorCursor.CurrentOffset);
            colorCursor.Advance();
            Color c2 = ReadPackedColor(file.Data, colorCursor.CurrentOffset);
            colorCursor.Advance();
            Color c3 = ReadPackedColor(file.Data, colorCursor.CurrentOffset);
            colorCursor.Advance();

            primitiveCursor.Advance();
            uvCursor.Advance();

            IncludeBounds(p0, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            IncludeBounds(p1, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            IncludeBounds(p2, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            IncludeBounds(p3, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);

            AddWireQuad(wireVerts, p0, p1, p2, p3, primitive.PrimitiveMode);
            AddSolidPrimitive(solidVerts, p0, p1, p2, p3, c0, c1, c2, c3, primitive.PrimitiveMode);
            AddTexturedPrimitive(texturedPrimitives, primitiveStartIndex + primitiveIndex, materialKey, p0, p1, p2, p3, c0, c1, c2, c3, uvRect, primitive.PrimitiveMode);
        }

        if (wireVerts.Count == 0 && solidVerts.Count == 0)
            return null;

        Vector3 center = new(
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f,
            (minZ + maxZ) * 0.5f);
        float extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        float sceneScale = extent > 0.01f ? 400f / extent : 1f;

        return new ChBinRenderableModel
        {
            EntryIndex = entry.Index,
            Label = $"E{entry.Index:D2} P{entry.PartId:X2} G{entry.GroupId:X2}",
            PartId = entry.PartId,
            GroupId = entry.GroupId,
            HasAnimation = entry.HasAnimation,
            PrimitiveStartIndex = primitiveStartIndex,
            PrimitiveCount = entry.PrimitiveCountLow,
            SceneCenter = center,
            SceneScale = sceneScale,
            WireVertices = wireVerts.ToArray(),
            SolidVertices = solidVerts.ToArray(),
            TexturedPrimitives = texturedPrimitives.ToArray(),
        };
    }

    private static ChBinMaterialKey ResolveFallbackMaterialKey(ChBinUvRect uvRect, ChBinMaterialKey? fallbackMaterialKey, IReadOnlyList<ChBinMaterialKey> fallbackCandidateKeys, IReadOnlyList<ChBinTexturePage> fallbackCandidatePages)
    {
        int candidateCount = Math.Min(fallbackCandidateKeys.Count, fallbackCandidatePages.Count);
        int bestScore = 0;
        ChBinMaterialKey? bestKey = null;

        for (int index = 0; index < candidateCount; index++)
        {
            ChBinTexturePage page = fallbackCandidatePages[index];
            if (!page.HasVisiblePixels)
                continue;

            int score = CountVisiblePixelsInUvBounds(page, uvRect);
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestKey = fallbackCandidateKeys[index];
        }

        return bestKey ?? fallbackMaterialKey ?? default;
    }

    private static int CountVisiblePixelsInUvBounds(ChBinTexturePage page, ChBinUvRect uvRect)
    {
        if (page.Width <= 0 || page.Height <= 0 || page.Pixels.Length == 0)
            return 0;

        int minU = Math.Clamp(Math.Min(Math.Min(uvRect.UV0.U, uvRect.UV1.U), Math.Min(uvRect.UV2.U, uvRect.UV3.U)), 0, page.Width - 1);
        int maxU = Math.Clamp(Math.Max(Math.Max(uvRect.UV0.U, uvRect.UV1.U), Math.Max(uvRect.UV2.U, uvRect.UV3.U)), 0, page.Width - 1);
        int minV = Math.Clamp(Math.Min(Math.Min(uvRect.UV0.V, uvRect.UV1.V), Math.Min(uvRect.UV2.V, uvRect.UV3.V)), 0, page.Height - 1);
        int maxV = Math.Clamp(Math.Max(Math.Max(uvRect.UV0.V, uvRect.UV1.V), Math.Max(uvRect.UV2.V, uvRect.UV3.V)), 0, page.Height - 1);

        int visiblePixels = 0;
        Color[] pixels = page.Pixels;
        for (int v = minV; v <= maxV; v++)
        {
            int rowOffset = v * page.Width;
            for (int u = minU; u <= maxU; u++)
            {
                if (pixels[rowOffset + u].A != 0)
                    visiblePixels++;
            }
        }

        return visiblePixels;
    }

    private static ChBinPrimitiveRecord ReadPrimitiveRecord(byte[] data, int offset)
    {
        if (offset < 0 || offset + 12 > data.Length)
            return default;

        return new ChBinPrimitiveRecord(
            new[] { data[offset + 0], data[offset + 1], data[offset + 2], data[offset + 3] },
            new[] { data[offset + 4], data[offset + 5], data[offset + 6], data[offset + 7] },
            data[offset + 8]);
    }

    private static Vector3 ReadCoord(byte[] data, int tableOffset, byte index)
    {
        int offset = tableOffset + index * 6;
        if (offset < 0 || offset + 6 > data.Length)
            return Vector3.Zero;

        short x = (short)BitConverter.ToUInt16(data, offset + 0);
        short y = (short)BitConverter.ToUInt16(data, offset + 2);
        short z = (short)BitConverter.ToUInt16(data, offset + 4);
        return new Vector3(x, y, z);
    }

    private static Color ReadPackedColor(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
            return Color.White;

            return new Color(data[offset + 0], data[offset + 1], data[offset + 2], (byte)255);
    }

    private static ChBinUvRect ReadUvRect(byte[] data, int offset)
    {
        if (offset < 0 || offset + 8 > data.Length)
            return default;

        byte baseU = data[offset + 0];
        byte baseV = data[offset + 2];
        byte deltaU = data[offset + 4];
        byte deltaV = data[offset + 6];

        return new ChBinUvRect(
            new StgUV(baseU, baseV),
            new StgUV((byte)(baseU + deltaU), baseV),
            new StgUV(baseU, (byte)(baseV + deltaV)),
            new StgUV((byte)(baseU + deltaU), (byte)(baseV + deltaV)));
    }

    private static ChBinMaterialKey DecodeMaterial(byte[] data, int? tableOffset, IReadOnlyList<byte> colorIndices)
    {
        if (!tableOffset.HasValue)
            return default;

        foreach (byte colorIndex in colorIndices.Distinct())
        {
            int offset = tableOffset.Value + colorIndex * 6;
            if (offset < 0 || offset + 6 > data.Length)
                continue;

            ushort word1 = BitConverter.ToUInt16(data, offset + 2);
            ushort word2 = BitConverter.ToUInt16(data, offset + 4);
            if (word1 != 0 || word2 != 0)
                return new ChBinMaterialKey(word2, word1);
        }

        return default;
    }

    private static void AddWireQuad(List<VertexPositionColor> vertices, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, byte primitiveMode)
    {
        Color wire = new(76, 154, 255);

        if (primitiveMode == 0)
        {
            vertices.Add(new VertexPositionColor(p0, wire));
            vertices.Add(new VertexPositionColor(p1, wire));
            vertices.Add(new VertexPositionColor(p1, wire));
            vertices.Add(new VertexPositionColor(p3, wire));
            vertices.Add(new VertexPositionColor(p3, wire));
            vertices.Add(new VertexPositionColor(p2, wire));
            vertices.Add(new VertexPositionColor(p2, wire));
            vertices.Add(new VertexPositionColor(p0, wire));
            return;
        }

        Vector3 a = primitiveMode == 1 ? p0 : p0;
        Vector3 b = primitiveMode == 1 ? p1 : p2;
        Vector3 c = primitiveMode == 1 ? p2 : p3;
        vertices.Add(new VertexPositionColor(a, wire));
        vertices.Add(new VertexPositionColor(b, wire));
        vertices.Add(new VertexPositionColor(b, wire));
        vertices.Add(new VertexPositionColor(c, wire));
        vertices.Add(new VertexPositionColor(c, wire));
        vertices.Add(new VertexPositionColor(a, wire));
    }

    private static void AddSolidPrimitive(List<VertexPositionColor> vertices, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color c0, Color c1, Color c2, Color c3, byte primitiveMode)
    {
        if (primitiveMode == 0)
        {
            vertices.Add(new VertexPositionColor(p0, c0));
            vertices.Add(new VertexPositionColor(p1, c1));
            vertices.Add(new VertexPositionColor(p2, c2));
            vertices.Add(new VertexPositionColor(p1, c1));
            vertices.Add(new VertexPositionColor(p3, c3));
            vertices.Add(new VertexPositionColor(p2, c2));
            return;
        }

        if (primitiveMode == 1)
        {
            vertices.Add(new VertexPositionColor(p0, c0));
            vertices.Add(new VertexPositionColor(p1, c1));
            vertices.Add(new VertexPositionColor(p2, c2));
            return;
        }

        vertices.Add(new VertexPositionColor(p0, c0));
        vertices.Add(new VertexPositionColor(p2, c2));
        vertices.Add(new VertexPositionColor(p3, c3));
    }

    private static void AddTexturedPrimitive(List<ChBinTexturedPrimitive> primitives, int globalPrimitiveIndex, ChBinMaterialKey materialKey, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color c0, Color c1, Color c2, Color c3, ChBinUvRect uvRect, byte primitiveMode)
    {
        if (!materialKey.IsSupported)
            return;

        ChBinRawTexturedVertex[] vertices;

        if (primitiveMode == 0)
        {
            vertices =
            [
                new ChBinRawTexturedVertex(p0, c0, uvRect.UV0),
                new ChBinRawTexturedVertex(p1, c1, uvRect.UV1),
                new ChBinRawTexturedVertex(p2, c2, uvRect.UV2),
                new ChBinRawTexturedVertex(p1, c1, uvRect.UV1),
                new ChBinRawTexturedVertex(p3, c3, uvRect.UV3),
                new ChBinRawTexturedVertex(p2, c2, uvRect.UV2),
            ];
            primitives.Add(new ChBinTexturedPrimitive(globalPrimitiveIndex, materialKey, uvRect, primitiveMode, vertices));
            return;
        }

        if (primitiveMode == 1)
        {
            vertices =
            [
                new ChBinRawTexturedVertex(p0, c0, uvRect.UV0),
                new ChBinRawTexturedVertex(p1, c1, uvRect.UV1),
                new ChBinRawTexturedVertex(p2, c2, uvRect.UV2),
            ];
            primitives.Add(new ChBinTexturedPrimitive(globalPrimitiveIndex, materialKey, uvRect, primitiveMode, vertices));
            return;
        }

        vertices =
        [
            new ChBinRawTexturedVertex(p0, c0, uvRect.UV0),
            new ChBinRawTexturedVertex(p2, c2, uvRect.UV2),
            new ChBinRawTexturedVertex(p3, c3, uvRect.UV3),
        ];
        primitives.Add(new ChBinTexturedPrimitive(globalPrimitiveIndex, materialKey, uvRect, primitiveMode, vertices));
    }

    private static void IncludeBounds(Vector3 point, ref float minX, ref float minY, ref float minZ, ref float maxX, ref float maxY, ref float maxZ)
    {
        minX = Math.Min(minX, point.X);
        minY = Math.Min(minY, point.Y);
        minZ = Math.Min(minZ, point.Z);
        maxX = Math.Max(maxX, point.X);
        maxY = Math.Max(maxY, point.Y);
        maxZ = Math.Max(maxZ, point.Z);
    }

    internal sealed class ChBinTextureAnimator
    {
        private static readonly Vector3 IdentityScaleRaw = new(4096f, 4096f, 4096f);

        private readonly ChBinFile _file;
        private readonly StreamState[] _streams;
        private readonly PaletteRequest[] _requests = new PaletteRequest[4];
        private readonly Dictionary<int, byte[]> _slotCache = new();
        private readonly Dictionary<byte, int[]> _entryIndicesByGroup;
        private readonly Dictionary<byte, int[]> _entryIndicesByPart;
        private readonly ModelPoseState[] _modelPoses;
        private readonly TransformVectorState[] _translationSlots = new TransformVectorState[64];
        private readonly TransformVectorState[] _rotationSlots = new TransformVectorState[32];
        private readonly TransformVectorState[] _scaleSlots = new TransformVectorState[16];
        private readonly int[] _entryPrimitiveStarts;
        private readonly int[] _entryPrimitiveCounts;
        private readonly short[] _sharedVars = new short[16];
        private PrimitiveMaterialState[] _basePrimitiveMaterials = Array.Empty<PrimitiveMaterialState>();
        private PrimitiveMaterialState[] _animatedPrimitiveMaterials = Array.Empty<PrimitiveMaterialState>();
        private PrimitiveUvState[] _basePrimitiveUvs = Array.Empty<PrimitiveUvState>();
        private PrimitiveUvState[] _animatedPrimitiveUvs = Array.Empty<PrimitiveUvState>();
        private readonly ushort[] _vram = new ushort[VramWidth * VramHeight];
        private readonly Dictionary<uint, bool> _paletteVisibilityCache = new();
        private readonly IReadOnlyDictionary<ushort, ushort[]> _paletteOverrides;
        private ushort[] _paletteCandidates;
        private ushort[] _tpageCandidates;
        private Dictionary<ushort, int> _paletteCandidateRanks;

        public ChBinTextureAnimator(ChBinFile file, IReadOnlyList<ushort>? paletteCandidates = null, IReadOnlyList<ushort>? tpageCandidates = null, IReadOnlyDictionary<ushort, ushort[]>? paletteOverrides = null)
        {
            _file = file;
            _paletteOverrides = paletteOverrides ?? new Dictionary<ushort, ushort[]>();
            _paletteCandidates = paletteCandidates?.Distinct().ToArray() ?? Array.Empty<ushort>();
            _tpageCandidates = tpageCandidates?.Select(NormalizeTPageForTextureDecode).Distinct().ToArray() ?? Array.Empty<ushort>();
            _paletteCandidateRanks = BuildPaletteCandidateRanks(_paletteCandidates);
            _streams = file.Entries
                .Where(static entry => entry.AnimBatches.Count > 0)
                .Select(static entry => new StreamState(entry.Index, entry.AnimBatches.ToArray()))
                .ToArray();
            _modelPoses = new ModelPoseState[file.Entries.Count];
            _entryPrimitiveStarts = Enumerable.Repeat(-1, file.Entries.Count).ToArray();
            _entryPrimitiveCounts = new int[file.Entries.Count];
            _entryIndicesByGroup = file.Entries
                .Where(static entry => entry.IsRenderable)
                .GroupBy(static entry => (byte)(entry.GroupId & 0x0F))
                .ToDictionary(static group => group.Key, static group => group.Select(static entry => entry.Index).ToArray());
            _entryIndicesByPart = file.Entries
                .Where(static entry => entry.IsRenderable)
                .GroupBy(static entry => entry.PartId)
                .ToDictionary(static group => group.Key, static group => group.Select(static entry => entry.Index).ToArray());

            int primitiveCursor = 0;
            foreach (ChBinEntry entry in file.Entries.Where(static entry => entry.IsRenderable))
            {
                _entryPrimitiveStarts[entry.Index] = primitiveCursor;
                _entryPrimitiveCounts[entry.Index] = entry.PrimitiveCountLow;
                primitiveCursor += entry.PrimitiveCountLow;
            }
        }

        public bool HasStreams => _streams.Length > 0;

        public void SetPaletteCandidates(IReadOnlyList<ushort> candidates)
        {
            _paletteCandidates = candidates.Distinct().ToArray();
            _paletteCandidateRanks = BuildPaletteCandidateRanks(_paletteCandidates);
            _paletteVisibilityCache.Clear();
        }

        public void SetTPageCandidates(IReadOnlyList<ushort> candidates)
            => _tpageCandidates = candidates.Select(NormalizeTPageForTextureDecode).Distinct().ToArray();

        public void Reset()
        {
            Array.Clear(_vram, 0, _vram.Length);
            _paletteVisibilityCache.Clear();
            Array.Clear(_requests, 0, _requests.Length);
            Array.Clear(_translationSlots, 0, _translationSlots.Length);
            Array.Clear(_rotationSlots, 0, _rotationSlots.Length);
            Array.Clear(_scaleSlots, 0, _scaleSlots.Length);
            Array.Clear(_sharedVars, 0, _sharedVars.Length);
            if (_basePrimitiveMaterials.Length > 0)
                Array.Copy(_basePrimitiveMaterials, _animatedPrimitiveMaterials, _basePrimitiveMaterials.Length);
            if (_basePrimitiveUvs.Length > 0)
                Array.Copy(_basePrimitiveUvs, _animatedPrimitiveUvs, _basePrimitiveUvs.Length);

            for (int index = 0; index < _modelPoses.Length; index++)
                _modelPoses[index] = default;

            foreach (StreamState stream in _streams)
            {
                stream.Reset();
                if (stream.Batches.Length == 0)
                    continue;

                ExecuteBatch(stream, stream.Batches[0], out _, out _);
                stream.FramesUntilNextBatch = NormalizeCountdown(stream.Batches[0].Countdown);
            }
        }

        public void InitializeModelState(IReadOnlyList<ChBinRenderableModel> models)
        {
            int primitiveCount = models.Count == 0
                ? 0
                : models.Max(static model => model.PrimitiveStartIndex + model.PrimitiveCount);
            _basePrimitiveMaterials = new PrimitiveMaterialState[primitiveCount];
            _animatedPrimitiveMaterials = new PrimitiveMaterialState[primitiveCount];
            _basePrimitiveUvs = new PrimitiveUvState[primitiveCount];
            _animatedPrimitiveUvs = new PrimitiveUvState[primitiveCount];

            foreach (ChBinRenderableModel model in models)
            {
                foreach (ChBinTexturedPrimitive primitive in model.TexturedPrimitives)
                {
                    if ((uint)primitive.GlobalPrimitiveIndex >= (uint)_basePrimitiveMaterials.Length)
                        continue;

                    PrimitiveMaterialState state = new(primitive.BaseMaterialKey, true);
                    _basePrimitiveMaterials[primitive.GlobalPrimitiveIndex] = state;
                    _animatedPrimitiveMaterials[primitive.GlobalPrimitiveIndex] = state;

                    PrimitiveUvState uvState = new(primitive.BaseUvRect, true);
                    _basePrimitiveUvs[primitive.GlobalPrimitiveIndex] = uvState;
                    _animatedPrimitiveUvs[primitive.GlobalPrimitiveIndex] = uvState;
                }
            }
        }

        public IReadOnlyList<ushort> DiscoverPaletteCandidates()
        {
            var counts = new Dictionary<ushort, int>();

            foreach (StreamState stream in _streams)
            {
                foreach (ChBinAnimBatch batch in stream.Batches)
                {
                    ushort[] words = batch.Words;
                    int cursor = 0;

                    while (cursor < words.Length)
                    {
                        ushort word0 = words[cursor];
                        byte opcode = (byte)(word0 & 0xFF);
                        int size = GuessCommandSize(words, cursor);
                        size = Math.Clamp(size, 1, words.Length - cursor);

                        switch (opcode)
                        {
                            case 0x03 when size >= 7:
                                if (words[cursor + 3] == 0x0010 && words[cursor + 4] == 0x0001)
                                {
                                    ushort cba = EncodeCba(words[cursor + 1], words[cursor + 2]);
                                    IncrementCount(counts, cba);
                                }

                                break;
                            case 0x0B when size >= 7 && (word0 & 0x8000) != 0:
                                IncrementCount(counts, EncodeCba((short)words[cursor + 3], (short)words[cursor + 4]));
                                break;
                            case 0x0D when size >= 4:
                                byte flags = (byte)(word0 >> 8);
                                if ((flags & 0x0F) == 0 && (flags & 0x40) == 0)
                                    IncrementCount(counts, words[cursor + 2]);
                                break;
                        }

                        if (opcode == 0x19)
                            break;

                        cursor += size;
                    }
                }
            }

            IEnumerable<KeyValuePair<ushort, int>> nonZeroCounts = counts.Where(static pair => pair.Key != 0);
            IEnumerable<KeyValuePair<ushort, int>> orderedCounts = nonZeroCounts.Any() ? nonZeroCounts : counts;
            return orderedCounts
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key)
                .Select(static pair => pair.Key)
                .ToArray();
        }

        public IReadOnlyList<ushort> DiscoverTPageCandidates()
        {
            var counts = new Dictionary<ushort, int>();

            foreach (StreamState stream in _streams)
            {
                foreach (ChBinAnimBatch batch in stream.Batches)
                {
                    ushort[] words = batch.Words;
                    int cursor = 0;

                    while (cursor < words.Length)
                    {
                        ushort word0 = words[cursor];
                        byte opcode = (byte)(word0 & 0xFF);
                        int size = GuessCommandSize(words, cursor);
                        size = Math.Clamp(size, 1, words.Length - cursor);

                        switch (opcode)
                        {
                            case 0x0D when size >= 4:
                                byte flags = (byte)(word0 >> 8);
                                if ((flags & 0x0F) == 0 && (flags & 0x80) == 0)
                                    IncrementCount(counts, NormalizeTPageForTextureDecode(words[cursor + 3]));
                                break;
                        }

                        if (opcode == 0x19)
                            break;

                        cursor += size;
                    }
                }
            }

            IEnumerable<KeyValuePair<ushort, int>> nonZeroCounts = counts.Where(static pair => pair.Key != 0);
            IEnumerable<KeyValuePair<ushort, int>> orderedCounts = nonZeroCounts.Any() ? nonZeroCounts : counts;
            return orderedCounts
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key)
                .Select(static pair => pair.Key)
                .ToArray();
        }

        public IReadOnlyList<ChBinMaterialKey> DiscoverVisibleUploadPages(int sampleFrames = 12)
        {
            ChBinMaterialKey[] candidates = EnumerateUploadPageCandidates().ToArray();
            if (candidates.Length == 0)
                return Array.Empty<ChBinMaterialKey>();

            var scores = new Dictionary<ChBinMaterialKey, int>();

            Reset();
            for (int frame = 0; frame <= sampleFrames; frame++)
            {
                foreach (ChBinMaterialKey key in candidates)
                {
                    ChBinTexturePage page = BuildTexturePage(key);
                    if (!page.HasVisiblePixels)
                        continue;

                    int score = CountVisiblePixels(page);
                    if (!scores.TryGetValue(key, out int bestScore) || score > bestScore)
                        scores[key] = score;
                }

                if (frame < sampleFrames)
                    AdvanceFrame(out _);
            }

            Reset();
            return scores
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key.Cba == 0 ? 1 : 0)
                .ThenBy(pair => GetPaletteCandidateRank(pair.Key.Cba))
                .ThenBy(static pair => pair.Key.ColorMode)
                .ThenBy(static pair => pair.Key.TPage)
                .ThenBy(static pair => pair.Key.Cba)
                .Select(static pair => pair.Key)
                .ToArray();
        }

        public bool AdvanceFrame(out bool textureDirty)
        {
            bool poseDirty = false;
            textureDirty = false;

            foreach (StreamState stream in _streams)
            {
                if (stream.IsFinished || stream.Batches.Length == 0)
                    continue;

                ChBinAnimBatch batch = stream.Batches[stream.BatchIndex];
                if (ExecuteBatch(stream, batch, out bool batchTextureDirty, out bool batchPoseDirty))
                {
                    textureDirty |= batchTextureDirty;
                    poseDirty |= batchPoseDirty;
                }

                if (stream.IsFinished)
                    continue;

                stream.FramesUntilNextBatch--;
                if (stream.FramesUntilNextBatch > 0)
                    continue;

                int nextBatchIndex = stream.BatchIndex + 1;
                if (nextBatchIndex >= stream.Batches.Length)
                {
                    stream.IsFinished = true;
                    continue;
                }

                stream.BatchIndex = nextBatchIndex;
                stream.FramesUntilNextBatch = NormalizeCountdown(stream.Batches[nextBatchIndex].Countdown);
            }

            return textureDirty || poseDirty;
        }

        private static int NormalizeCountdown(ushort countdown)
            => countdown == 0 ? 1 : countdown;

        public Matrix GetModelAnimationMatrix(int entryIndex)
        {
            if ((uint)entryIndex >= (uint)_modelPoses.Length)
                return Matrix.Identity;

            ModelPoseState pose = _modelPoses[entryIndex];
            if (!pose.IsAssigned)
                return Matrix.Identity;

            Vector3 rotation = pose.Rotation * (MathHelper.TwoPi / 4096f);
            Vector3 scale = new(
                pose.Scale.X / 4096f,
                pose.Scale.Y / 4096f,
                pose.Scale.Z / 4096f);

            return Matrix.CreateScale(scale)
                * Matrix.CreateRotationX(rotation.X)
                * Matrix.CreateRotationY(rotation.Y)
                * Matrix.CreateRotationZ(rotation.Z)
                * Matrix.CreateTranslation(pose.Translation);
        }

        public ChBinMaterialKey GetAnimatedMaterialKey(int primitiveIndex, ChBinMaterialKey fallbackMaterialKey)
        {
            if ((uint)primitiveIndex >= (uint)_animatedPrimitiveMaterials.Length)
                return fallbackMaterialKey;

            PrimitiveMaterialState state = _animatedPrimitiveMaterials[primitiveIndex];
            return state.IsAssigned ? state.MaterialKey : fallbackMaterialKey;
        }

        public ChBinUvRect GetAnimatedUvRect(int primitiveIndex, ChBinUvRect fallbackUvRect)
        {
            if ((uint)primitiveIndex >= (uint)_animatedPrimitiveUvs.Length)
                return fallbackUvRect;

            PrimitiveUvState state = _animatedPrimitiveUvs[primitiveIndex];
            return state.IsAssigned ? state.UvRect : fallbackUvRect;
        }

        private IReadOnlyList<ChBinMaterialKey> EnumerateUploadPageCandidates()
        {
            var keys = new HashSet<ChBinMaterialKey>();

            foreach (StreamState stream in _streams)
            {
                foreach (ChBinAnimBatch batch in stream.Batches)
                {
                    ushort[] words = batch.Words;
                    int cursor = 0;

                    while (cursor < words.Length)
                    {
                        ushort word0 = words[cursor];
                        byte opcode = (byte)(word0 & 0xFF);
                        int size = GuessCommandSize(words, cursor);
                        size = Math.Clamp(size, 1, words.Length - cursor);

                        if (opcode == 0x03 && size >= 7)
                        {
                            int x = words[cursor + 1];
                            int y = words[cursor + 2];
                            int widthWords = Math.Max(1, (int)words[cursor + 3]);
                            int height = Math.Max(1, (int)words[cursor + 4]);

                            int firstPageX = Math.Clamp(x / 64, 0, 15);
                            int lastPageX = Math.Clamp((x + widthWords - 1) / 64, 0, 15);
                            int firstPageY = Math.Clamp(y / 256, 0, 1);
                            int lastPageY = Math.Clamp((y + height - 1) / 256, 0, 1);

                            for (int pageY = firstPageY; pageY <= lastPageY; pageY++)
                            {
                                for (int pageX = firstPageX; pageX <= lastPageX; pageX++)
                                {
                                    if (_tpageCandidates.Length > 0)
                                    {
                                        foreach (ushort tpage in _tpageCandidates)
                                        {
                                            var candidateBaseKey = new ChBinMaterialKey(tpage, 0);
                                            if (candidateBaseKey.TPageX != pageX || candidateBaseKey.TPageY != pageY)
                                                continue;

                                            AddCandidateKeys(keys, tpage);
                                        }

                                        continue;
                                    }

                                    for (int colorMode = 0; colorMode <= 2; colorMode++)
                                    {
                                        ushort tpage = (ushort)(pageX | (pageY << 4) | (colorMode << 7));
                                        AddCandidateKeys(keys, tpage);
                                    }
                                }
                            }
                        }

                        if (opcode == 0x19)
                            break;

                        cursor += size;
                    }
                }
            }

            return keys.ToArray();
        }

        private void AddCandidateKeys(HashSet<ChBinMaterialKey> keys, ushort tpage)
        {
            ushort normalizedTPage = NormalizeTPageForTextureDecode(tpage);
            var baseKey = new ChBinMaterialKey(normalizedTPage, 0);
            if (baseKey.ColorMode == 2)
            {
                keys.Add(baseKey);
                return;
            }

            if (_paletteCandidates.Length == 0)
            {
                keys.Add(baseKey);
                return;
            }

            foreach (ushort cba in _paletteCandidates)
                keys.Add(new ChBinMaterialKey(normalizedTPage, cba));
        }

        private static int CountVisiblePixels(ChBinTexturePage page)
        {
            int count = 0;
            foreach (Color pixel in page.Pixels)
            {
                if (pixel.A != 0)
                    count++;
            }

            return count;
        }

        private int GetPaletteCandidateRank(ushort cba)
            => _paletteCandidateRanks.TryGetValue(cba, out int rank) ? rank : int.MaxValue;

        private static Dictionary<ushort, int> BuildPaletteCandidateRanks(IReadOnlyList<ushort> candidates)
        {
            var ranks = new Dictionary<ushort, int>();
            for (int index = 0; index < candidates.Count; index++)
            {
                ushort cba = candidates[index];
                if (!ranks.ContainsKey(cba))
                    ranks[cba] = index;
            }

            return ranks;
        }

        public ChBinTexturePage BuildTexturePage(ChBinMaterialKey key)
        {
            if (!key.IsSupported)
                return ChBinTexturePage.Empty;

            int width = key.PageWidth;
            int height = key.PageHeight;
            var pixels = new Color[width * height];
            bool hasVisiblePixels = false;

            int pageBaseX = key.TPageX * 64;
            int pageBaseY = key.TPageY * 256;

            for (int y = 0; y < height; y++)
            {
                int vramY = pageBaseY + y;
                if ((uint)vramY >= VramHeight)
                    break;

                for (int x = 0; x < width; x++)
                {
                    Color color = key.ColorMode switch
                    {
                        0 => Sample4Bpp(pageBaseX, vramY, x, key.Cba),
                        1 => Sample8Bpp(pageBaseX, vramY, x, key.Cba),
                        2 => Sample16Bpp(pageBaseX, vramY, x),
                        _ => Color.Transparent,
                    };

                    pixels[y * width + x] = color;
                    if (color.A != 0)
                        hasVisiblePixels = true;
                }
            }

            return new ChBinTexturePage(width, height, pixels, hasVisiblePixels);
        }

        private bool ExecuteBatch(StreamState stream, ChBinAnimBatch batch, out bool textureDirty, out bool poseDirty)
        {
            textureDirty = false;
            poseDirty = false;
            ushort[] words = batch.Words;
            int cursor = 0;

            while (cursor < words.Length)
            {
                ushort word0 = words[cursor];
                byte opcode = (byte)(word0 & 0xFF);
                if (opcode == 0x19)
                {
                    stream.IsFinished = true;
                    break;
                }

                int size = GuessCommandSize(words, cursor);
                size = Math.Clamp(size, 1, words.Length - cursor);

                switch (opcode)
                {
                    case 0x03 when size >= 7:
                        textureDirty |= ApplyLoadSet(words, cursor);
                        break;
                    case 0x06 when size >= 2:
                        poseDirty |= ApplyTransformSet(words, cursor, _translationSlots, (words[cursor] >> 8) & 0x3F, 0x3F, Vector3.Zero);
                        break;
                    case 0x07 when size >= 2:
                        poseDirty |= ApplyTransformSet(words, cursor, _rotationSlots, (words[cursor] >> 8) & 0x1F, 0x1F, Vector3.Zero);
                        break;
                    case 0x08 when size >= 2:
                        poseDirty |= ApplyTransformSet(words, cursor, _scaleSlots, (words[cursor] >> 8) & 0x0F, 0x0F, IdentityScaleRaw);
                        break;
                    case 0x09 when size >= 4:
                        poseDirty |= ApplySetMeshTransform(words, cursor);
                        break;
                    case 0x0D when size >= 4:
                        textureDirty |= ApplyTexturePageOrClutSet(words, cursor);
                        break;
                    case 0x20 when size >= 6:
                        poseDirty |= ApplyUv0123Set(words, cursor);
                        break;
                    case 0x0B when size >= 1:
                        textureDirty |= ApplyTexSet(words, cursor, size);
                        break;
                }

                cursor += size;
            }

            return textureDirty || poseDirty;
        }

        private bool ApplyTransformSet(ushort[] words, int cursor, TransformVectorState[] slots, int targetIndex, int sourceMask, Vector3 defaultValue)
        {
            if ((uint)targetIndex >= (uint)slots.Length || cursor + 1 >= words.Length)
                return false;

            TransformVectorState state = slots[targetIndex];
            Vector3 nextValue = state.IsAssigned ? state.Value : defaultValue;
            bool dirty = !state.IsAssigned;
            int operandCursor = cursor + 2;
            ushort packedSpecs = words[cursor + 1];

            for (int componentIndex = 0; componentIndex < 3; componentIndex++)
            {
                int packedSpec = (packedSpecs >> (componentIndex * 5)) & 0x1F;
                int opMode = packedSpec & 0x0F;
                if (opMode == 0x0F)
                    continue;

                short operand = operandCursor < words.Length
                    ? (short)words[operandCursor++]
                    : (short)0;
                if ((packedSpec & 0x10) != 0)
                    operand = _sharedVars[operand & 0x0F];

                short currentValue = GetVectorComponent(nextValue, componentIndex);
                short updatedValue = opMode == 0x08
                    ? GetTransformCopyValue(slots, operand, componentIndex, sourceMask, defaultValue)
                    : ApplyMathOp(currentValue, opMode, operand);

                if (updatedValue != currentValue)
                    dirty = true;

                SetVectorComponent(ref nextValue, componentIndex, updatedValue);
            }

            slots[targetIndex] = new TransformVectorState(nextValue, true);
            return dirty;
        }

        private bool ApplyTexturePageOrClutSet(ushort[] words, int cursor)
        {
            byte flags = (byte)(words[cursor] >> 8);
            int applyMathMode = flags & 0x0F;
            int rangeMode = flags & 0x30;
            short clutOperand = (short)words[cursor + 2];
            short tpageOperand = (short)words[cursor + 3];
            if ((flags & 0x40) != 0)
                clutOperand = _sharedVars[clutOperand & 0x0F];
            if ((flags & 0x80) != 0)
                tpageOperand = _sharedVars[tpageOperand & 0x0F];

            ushort word1 = words[cursor + 1];
            int startOrGroup = word1 & 0xFF;
            int count = word1 >> 8;
            if (count <= 0)
                return false;

            return rangeMode switch
            {
                0x00 => ApplyMaterialOpToPrimitiveRange(startOrGroup, count, applyMathMode, clutOperand, tpageOperand),
                0x10 => ApplyMaterialOpToEntryRange(startOrGroup, count, applyMathMode, clutOperand, tpageOperand),
                0x20 => ApplyMaterialOpToGroup(startOrGroup, count, applyMathMode, clutOperand, tpageOperand),
                _ => false,
            };
        }

        private bool ApplyMaterialOpToEntryRange(int startEntryIndex, int entryCount, int applyMathMode, short clutOperand, short tpageOperand)
        {
            bool dirty = false;

            for (int entryOffset = 0; entryOffset < entryCount; entryOffset++)
                dirty |= ApplyMaterialOpToEntry(startEntryIndex + entryOffset, applyMathMode, clutOperand, tpageOperand);

            return dirty;
        }

        private bool ApplyMaterialOpToGroup(int groupId, int matchCount, int applyMathMode, short clutOperand, short tpageOperand)
        {
            if (!_entryIndicesByGroup.TryGetValue((byte)groupId, out int[]? entryIndices))
                return false;

            bool dirty = false;
            int remaining = matchCount;
            foreach (int entryIndex in entryIndices)
            {
                dirty |= ApplyMaterialOpToEntry(entryIndex, applyMathMode, clutOperand, tpageOperand);
                remaining--;
                if (remaining <= 0)
                    break;
            }

            return dirty;
        }

        private bool ApplyMaterialOpToEntry(int entryIndex, int applyMathMode, short clutOperand, short tpageOperand)
        {
            if ((uint)entryIndex >= (uint)_entryPrimitiveStarts.Length)
                return false;

            int start = _entryPrimitiveStarts[entryIndex];
            int count = _entryPrimitiveCounts[entryIndex];
            if (start < 0 || count <= 0)
                return false;

            return ApplyMaterialOpToPrimitiveRange(start, count, applyMathMode, clutOperand, tpageOperand);
        }

        private bool ApplyMaterialOpToPrimitiveRange(int primitiveStartIndex, int primitiveCount, int applyMathMode, short clutOperand, short tpageOperand)
        {
            bool dirty = false;

            int rangeStart = Math.Max(0, primitiveStartIndex);
            int rangeEnd = Math.Min(_animatedPrimitiveMaterials.Length, primitiveStartIndex + primitiveCount);
            for (int primitiveIndex = rangeStart; primitiveIndex < rangeEnd; primitiveIndex++)
            {
                PrimitiveMaterialState state = _animatedPrimitiveMaterials[primitiveIndex];
                if (!state.IsAssigned)
                    continue;

                ushort nextCba = ApplyMaterialMath(state.MaterialKey.Cba, applyMathMode, clutOperand);
                ushort nextTPage = ApplyMaterialMath(state.MaterialKey.TPage, applyMathMode, tpageOperand);
                ChBinMaterialKey nextKey = new(nextTPage, nextCba);
                if (nextKey == state.MaterialKey)
                    continue;

                _animatedPrimitiveMaterials[primitiveIndex] = new PrimitiveMaterialState(nextKey, true);
                dirty = true;
            }

            return dirty;
        }

        private ushort ApplyMaterialMath(ushort currentValue, int applyMathMode, short operand)
        {
            short updatedValue = ApplyMathOp((short)currentValue, applyMathMode, operand);
            if (updatedValue < 0)
                return 0;

            return (ushort)updatedValue;
        }

        private bool ApplyUv0123Set(ushort[] words, int cursor)
        {
            byte flags = (byte)(words[cursor] >> 8);
            int rangeMode = flags & 0x30;
            ushort word1 = words[cursor + 1];
            int startOrPart = word1 & 0xFF;
            int count = word1 >> 8;
            if (count <= 0)
                return false;

            ChBinUvRect uvRect = new(
                DecodePackedUv(words[cursor + 2]),
                DecodePackedUv(words[cursor + 3]),
                DecodePackedUv(words[cursor + 4]),
                DecodePackedUv(words[cursor + 5]));

            return rangeMode switch
            {
                0x00 => ApplyUvToPrimitiveRange(startOrPart, count, uvRect),
                0x10 => ApplyUvToEntryRange(startOrPart, count, uvRect),
                0x20 => ApplyUvToPart(startOrPart, count, uvRect),
                _ => false,
            };
        }

        private static StgUV DecodePackedUv(ushort packed)
            => new((byte)(packed & 0xFF), (byte)(packed >> 8));

        private bool ApplyUvToEntryRange(int startEntryIndex, int entryCount, ChBinUvRect uvRect)
        {
            bool dirty = false;

            for (int entryOffset = 0; entryOffset < entryCount; entryOffset++)
                dirty |= ApplyUvToEntry(startEntryIndex + entryOffset, uvRect);

            return dirty;
        }

        private bool ApplyUvToPart(int partId, int matchCount, ChBinUvRect uvRect)
        {
            if (!_entryIndicesByPart.TryGetValue((byte)partId, out int[]? entryIndices))
                return false;

            bool dirty = false;
            int remaining = matchCount;
            foreach (int entryIndex in entryIndices)
            {
                dirty |= ApplyUvToEntry(entryIndex, uvRect);
                remaining--;
                if (remaining <= 0)
                    break;
            }

            return dirty;
        }

        private bool ApplyUvToEntry(int entryIndex, ChBinUvRect uvRect)
        {
            if ((uint)entryIndex >= (uint)_entryPrimitiveStarts.Length)
                return false;

            int start = _entryPrimitiveStarts[entryIndex];
            int count = _entryPrimitiveCounts[entryIndex];
            if (start < 0 || count <= 0)
                return false;

            return ApplyUvToPrimitiveRange(start, count, uvRect);
        }

        private bool ApplyUvToPrimitiveRange(int primitiveStartIndex, int primitiveCount, ChBinUvRect uvRect)
        {
            bool dirty = false;

            int rangeStart = Math.Max(0, primitiveStartIndex);
            int rangeEnd = Math.Min(_animatedPrimitiveUvs.Length, primitiveStartIndex + primitiveCount);
            for (int primitiveIndex = rangeStart; primitiveIndex < rangeEnd; primitiveIndex++)
            {
                PrimitiveUvState state = _animatedPrimitiveUvs[primitiveIndex];
                if (!state.IsAssigned || state.UvRect == uvRect)
                    continue;

                _animatedPrimitiveUvs[primitiveIndex] = new PrimitiveUvState(uvRect, true);
                dirty = true;
            }

            return dirty;
        }

        private bool ApplySetMeshTransform(ushort[] words, int cursor)
        {
            byte flags = (byte)(words[cursor] >> 8);
            byte groupId = (byte)(flags & 0x0F);
            ushort rangeCount = (flags & 0x10) == 0
                ? words[cursor + 1]
                : (ushort)_sharedVars[words[cursor + 1] & 0x0F];
            if (rangeCount == 0)
                return false;

            ushort packedTargets = words[cursor + 3];
            Vector3 translation = ResolveTransformValue(_translationSlots, packedTargets & 0x3F, Vector3.Zero);
            Vector3 rotation = ResolveTransformValue(_rotationSlots, (packedTargets >> 6) & 0x1F, Vector3.Zero);
            Vector3 scale = ResolveTransformValue(_scaleSlots, (packedTargets >> 11) & 0x0F, IdentityScaleRaw);

            if (!_entryIndicesByGroup.TryGetValue(groupId, out int[]? entryIndices))
                return false;

            bool dirty = false;
            ModelPoseState nextPose = new(translation, rotation, scale, true);
            foreach (int entryIndex in entryIndices)
            {
                if (_modelPoses[entryIndex].Equals(nextPose))
                    continue;

                _modelPoses[entryIndex] = nextPose;
                dirty = true;
            }

            return dirty;
        }

        private Vector3 ResolveTransformValue(TransformVectorState[] slots, int index, Vector3 defaultValue)
        {
            if ((uint)index >= (uint)slots.Length)
                return defaultValue;

            TransformVectorState state = slots[index];
            return state.IsAssigned ? state.Value : defaultValue;
        }

        private short GetTransformCopyValue(TransformVectorState[] slots, short operand, int componentIndex, int sourceMask, Vector3 defaultValue)
        {
            int sourceIndex = operand & sourceMask;
            if ((uint)sourceIndex >= (uint)slots.Length)
                return GetVectorComponent(defaultValue, componentIndex);

            TransformVectorState sourceState = slots[sourceIndex];
            return GetVectorComponent(sourceState.IsAssigned ? sourceState.Value : defaultValue, componentIndex);
        }

        private short ApplyMathOp(short currentValue, int opMode, short operand)
        {
            return opMode switch
            {
                0 => operand,
                1 => (short)(currentValue + operand),
                2 => (short)(currentValue - operand),
                3 => (short)(currentValue | operand),
                4 => (short)(currentValue & operand),
                5 => (short)(currentValue ^ operand),
                6 => (short)((currentValue * operand) / 4096),
                7 => operand == 0 ? currentValue : (short)(currentValue / operand),
                9 => (short)(operand - currentValue),
                10 => ApplySharedVarWrite(currentValue, operand),
                11 => (short)(currentValue + (operand & Random.Shared.Next(short.MinValue, short.MaxValue))),
                12 => operand == 0 ? currentValue : (short)(currentValue % operand),
                _ => currentValue,
            };
        }

        private short ApplySharedVarWrite(short currentValue, short operand)
        {
            _sharedVars[(operand >> 1) & 0x0F] = currentValue;
            return currentValue;
        }

        private static short GetVectorComponent(Vector3 value, int componentIndex)
        {
            return componentIndex switch
            {
                0 => (short)value.X,
                1 => (short)value.Y,
                _ => (short)value.Z,
            };
        }

        private static void SetVectorComponent(ref Vector3 value, int componentIndex, short componentValue)
        {
            switch (componentIndex)
            {
                case 0:
                    value.X = componentValue;
                    break;
                case 1:
                    value.Y = componentValue;
                    break;
                default:
                    value.Z = componentValue;
                    break;
            }
        }

        private static ushort EncodeCba(int x, int y)
        {
            int clampedX = Math.Clamp(x, 0, VramWidth - 16);
            int clampedY = Math.Clamp(y, 0, VramHeight - 1);
            return (ushort)(((clampedX / 16) & 0x3F) | ((clampedY & 0x1FF) << 6));
        }

        private bool ApplyLoadSet(ushort[] words, int cursor)
        {
            ushort word0 = words[cursor + 0];
            int x = words[cursor + 1];
            int y = words[cursor + 2];
            int widthWords = words[cursor + 3];
            int height = words[cursor + 4];
            int slotIndex = words[cursor + 5];

            byte[] payload = GetSlotPayload(slotIndex);
            if (payload.Length == 0 || widthWords <= 0 || height <= 0)
                return false;

            byte[] src = (word0 & 0x0100) != 0
                ? LzssDecompressor.Decompress(payload)
                : payload;

            return UploadWords(src, x, y, widthWords, height);
        }

        private bool ApplyTexSet(ushort[] words, int cursor, int size)
        {
            ushort word0 = words[cursor];
            int requestIndex = (word0 >> 8) & 0x03;
            if ((uint)requestIndex >= (uint)_requests.Length)
                return false;

            if ((word0 & 0x8000) != 0)
            {
                if (size < 7)
                    return false;

                _requests[requestIndex] = new PaletteRequest
                {
                    SourceSlot = words[cursor + 1],
                    X = (short)words[cursor + 3],
                    Y = (short)words[cursor + 4],
                    CycleOffset = (byte)(words[cursor + 5] & 0xFF),
                    FirstIndex = (byte)(words[cursor + 5] >> 8),
                    LastIndex = (byte)(words[cursor + 6] & 0xFF),
                    Flags = (byte)(words[cursor + 6] >> 8),
                    ReloadState = 0,
                    IsValid = true,
                };

                return UploadClutRow(_requests[requestIndex]);
            }

            if (!_requests[requestIndex].IsValid)
                return false;

            PaletteRequest request = _requests[requestIndex];
            int reloadMask = word0 & 0x7000;
            bool dirty = false;

            if (reloadMask != 0 && request.ReloadState == 0)
            {
                request.ReloadState = (byte)((word0 >> 8) & 0x70);
                request.CycleOffset--;
                dirty = UploadClutRow(request);
            }

            if (request.ReloadState >= 0x10)
                request.ReloadState = (byte)(request.ReloadState - 0x10);
            else
                request.ReloadState = 0;

            _requests[requestIndex] = request;
            return dirty;
        }

        private bool UploadClutRow(PaletteRequest request)
        {
            byte[] payload = GetSlotPayload(request.SourceSlot);
            if (payload.Length < 0x20)
                return false;

            ushort[] temp = BinaryReaderHelper.ReadUShortArrayFast(payload, 0, 16);

            int first = Math.Clamp((int)request.FirstIndex, 0, 15);
            int last = Math.Clamp((int)request.LastIndex, first, 15);
            int count = last - first + 1;
            if (count <= 0)
                return false;

            var rotated = new ushort[16];
            Array.Copy(temp, rotated, 16);

            for (int index = 0; index < count; index++)
            {
                int srcIndex = first + ((request.CycleOffset % count) + index + count) % count;
                ushort color = temp[srcIndex];
                if ((request.Flags & 0x80) != 0)
                    color |= 0x8000;
                if ((request.Flags & 0x01) != 0)
                    color &= 0x7FFF;
                rotated[first + index] = color;
            }

            for (int i = 0; i < 16; i++)
            {
                int x = request.X + i;
                int y = request.Y;
                if ((uint)x >= VramWidth || (uint)y >= VramHeight)
                    continue;

                _vram[y * VramWidth + x] = rotated[i];
            }

            _paletteVisibilityCache.Clear();

            return true;
        }

        private bool UploadWords(byte[] src, int x, int y, int widthWords, int height)
        {
            int requiredBytes = widthWords * height * 2;
            int copyBytes = Math.Min(requiredBytes, src.Length);
            if (copyBytes <= 0)
                return false;

            bool dirty = false;
            int srcPos = 0;
            _paletteVisibilityCache.Clear();

            for (int row = 0; row < height && srcPos + 1 < copyBytes; row++)
            {
                int dstY = y + row;
                if ((uint)dstY >= VramHeight)
                {
                    srcPos += widthWords * 2;
                    continue;
                }

                for (int col = 0; col < widthWords && srcPos + 1 < copyBytes; col++)
                {
                    int dstX = x + col;
                    ushort value = (ushort)(src[srcPos] | (src[srcPos + 1] << 8));
                    srcPos += 2;

                    if ((uint)dstX >= VramWidth)
                        continue;

                    _vram[dstY * VramWidth + dstX] = value;
                    dirty = true;
                }
            }

            return dirty;
        }

        private byte[] GetSlotPayload(int slotIndex)
        {
            if (_slotCache.TryGetValue(slotIndex, out byte[]? cached))
                return cached;

            ChBinHeaderSlot? slot = _file.HeaderSlots.FirstOrDefault(candidate => candidate.Index == slotIndex);
            if (slot is null || !slot.FileOffset.HasValue)
                return _slotCache[slotIndex] = Array.Empty<byte>();

            int start = slot.FileOffset.Value;
            int end = _file.Data.Length;

            foreach (ChBinHeaderSlot other in _file.HeaderSlots)
            {
                if (!other.FileOffset.HasValue || other.FileOffset.Value <= start)
                    continue;

                end = Math.Min(end, other.FileOffset.Value);
            }

            if (end <= start)
                return _slotCache[slotIndex] = Array.Empty<byte>();

            byte[] payload = new byte[end - start];
            Buffer.BlockCopy(_file.Data, start, payload, 0, payload.Length);
            _slotCache[slotIndex] = payload;
            return payload;
        }

        private Color Sample4Bpp(int pageBaseX, int vramY, int u, ushort cba)
        {
            int wordX = pageBaseX + (u >> 2);
            if ((uint)wordX >= VramWidth)
                return Color.Transparent;

            ushort packed = _vram[vramY * VramWidth + wordX];
            int shift = (u & 3) * 4;
            int index = (packed >> shift) & 0x0F;
            if (index == 0)
                return Color.Transparent;

            int paletteX = (cba & 0x3F) * 16;
            int paletteY = (cba >> 6) & 0x1FF;
            if ((uint)paletteY >= VramHeight || !HasVisiblePaletteEntries(cba, 16))
            {
                if (TrySamplePaletteOverride(cba, index, out Color paletteColor))
                    return paletteColor;

                return IndexedDebugColor(index, 0x0F);
            }

            if ((uint)paletteX + index >= VramWidth)
                return Color.Transparent;

            ushort color = _vram[paletteY * VramWidth + paletteX + index];
            return Bgr555ToColor(color, treatZeroAsTransparent: true);
        }

        private Color Sample8Bpp(int pageBaseX, int vramY, int u, ushort cba)
        {
            int wordX = pageBaseX + (u >> 1);
            if ((uint)wordX >= VramWidth)
                return Color.Transparent;

            ushort packed = _vram[vramY * VramWidth + wordX];
            int index = (u & 1) == 0 ? (packed & 0xFF) : (packed >> 8);
            if (index == 0)
                return Color.Transparent;

            int paletteX = (cba & 0x3F) * 16;
            int paletteY = (cba >> 6) & 0x1FF;
            if ((uint)paletteY >= VramHeight || !HasVisiblePaletteEntries(cba, 256))
            {
                if (TrySamplePaletteOverride(cba, index, out Color paletteColor))
                    return paletteColor;

                return IndexedDebugColor(index, 0xFF);
            }

            int sampleX = paletteX + index;
            if ((uint)sampleX >= VramWidth)
                return Color.Transparent;

            ushort color = _vram[paletteY * VramWidth + sampleX];
            return Bgr555ToColor(color, treatZeroAsTransparent: true);
        }

        private Color Sample16Bpp(int pageBaseX, int vramY, int u)
        {
            int pixelX = pageBaseX + u;
            if ((uint)pixelX >= VramWidth)
                return Color.Transparent;

            ushort color = _vram[vramY * VramWidth + pixelX];
            return Bgr555ToColor(color, treatZeroAsTransparent: true);
        }

        private static Color Bgr555ToColor(ushort color, bool treatZeroAsTransparent)
        {
            if (treatZeroAsTransparent && color == 0)
                return Color.Transparent;

            int r = ((color >> 0) & 0x1F) * 255 / 31;
            int g = ((color >> 5) & 0x1F) * 255 / 31;
            int b = ((color >> 10) & 0x1F) * 255 / 31;
            return new Color(r, g, b, 255);
        }

        private bool TrySamplePaletteOverride(ushort cba, int index, out Color color)
        {
            if (_paletteOverrides.TryGetValue(cba, out ushort[]? palette)
                && (uint)index < (uint)palette.Length)
            {
                color = Bgr555ToColor(palette[index], treatZeroAsTransparent: true);
                return true;
            }

            color = default;
            return false;
        }

        private bool HasVisiblePaletteEntries(ushort cba, int colorCount)
        {
            uint cacheKey = ((uint)colorCount << 16) | cba;
            if (_paletteVisibilityCache.TryGetValue(cacheKey, out bool hasVisibleEntries))
                return hasVisibleEntries;

            int paletteX = (cba & 0x3F) * 16;
            int paletteY = (cba >> 6) & 0x1FF;
            if ((uint)paletteY >= VramHeight || paletteX < 0 || paletteX >= VramWidth)
            {
                _paletteVisibilityCache[cacheKey] = false;
                return false;
            }

            int maxCount = Math.Min(colorCount, VramWidth - paletteX);
            int rowOffset = paletteY * VramWidth + paletteX;
            hasVisibleEntries = false;
            for (int index = 0; index < maxCount; index++)
            {
                if (_vram[rowOffset + index] != 0)
                {
                    hasVisibleEntries = true;
                    break;
                }
            }

            _paletteVisibilityCache[cacheKey] = hasVisibleEntries;
            return hasVisibleEntries;
        }

        private static Color IndexedDebugColor(int index, int maxIndex)
        {
            int value = Math.Clamp(index * 255 / Math.Max(1, maxIndex), 24, 255);
            return new Color(value, value, value, 255);
        }

        private static int GuessCommandSize(IReadOnlyList<ushort> words, int cursor)
        {
            ushort word0 = words[cursor];
            byte opcode = (byte)(word0 & 0xFF);
            byte high8 = (byte)(word0 >> 8);

            return opcode switch
            {
                0x02 => 2,
                0x03 => 7,
                0x05 => 4,
                0x06 or 0x07 or 0x08 => cursor + 1 < words.Count ? 2 + CountTransformSpecs(words[cursor + 1]) : 1,
                0x09 => 4,
                0x0A => 2,
                0x0B => (high8 & 0x80) != 0 ? 7 : 1,
                0x0C => 5,
                0x0D => 4,
                0x0E => 4,
                0x0F => 4,
                0x10 => 3,
                0x11 => 2,
                0x12 => 3,
                0x13 => cursor + 2 < words.Count && (words[cursor + 2] & 0x8000) != 0 ? 5 : 4,
                0x14 => (high8 & 0x03) == 0x02 ? 3 : 2,
                0x15 or 0x16 => 2,
                0x17 => GuessBitChkSize(high8),
                0x18 => 3,
                0x19 => 1,
                0x1B => 7,
                0x20 => 6,
                0x21 => (high8 & 0x80) != 0 ? 1 : 3,
                0x23 => (high8 & 0x03) == 0 ? 3 : 1,
                0x25 => cursor + 4 < words.Count ? 5 + CountXySpecs(words[cursor + 2], words[cursor + 3], words[cursor + 4]) : 1,
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
            return CountPackedSpecs(word2, 3) + CountPackedSpecs(word3, 3) + CountPackedSpecs(word4, 2);
        }

        private static int CountPackedSpecs(ushort packed, int specCount)
        {
            int count = 0;
            for (int index = 0; index < specCount; index++)
            {
                if (((packed >> (index * 5)) & 0x1F) != 0x0F)
                    count++;
            }

            return count;
        }

        private readonly record struct TransformVectorState(Vector3 Value, bool IsAssigned);

        private readonly record struct ModelPoseState(Vector3 Translation, Vector3 Rotation, Vector3 Scale, bool IsAssigned);

        private readonly record struct PrimitiveMaterialState(ChBinMaterialKey MaterialKey, bool IsAssigned);

        private readonly record struct PrimitiveUvState(ChBinUvRect UvRect, bool IsAssigned);
    }

    private sealed class StreamState
    {
        public StreamState(int entryIndex, ChBinAnimBatch[] batches)
        {
            EntryIndex = entryIndex;
            Batches = batches;
        }

        public int EntryIndex { get; }
        public ChBinAnimBatch[] Batches { get; }
        public int BatchIndex { get; set; }
        public int FramesUntilNextBatch { get; set; }
        public bool IsFinished { get; set; }

        public void Reset()
        {
            BatchIndex = 0;
            FramesUntilNextBatch = 0;
            IsFinished = false;
        }
    }

    private struct PaletteRequest
    {
        public bool IsValid;
        public int SourceSlot;
        public short X;
        public short Y;
        public byte CycleOffset;
        public byte FirstIndex;
        public byte LastIndex;
        public byte Flags;
        public byte ReloadState;
    }

    private sealed class RepeatingStreamCursor
    {
        private readonly SegmentState[] _segments;
        private readonly int _stride;
        private int _segmentIndex;
        private int _positionInRow;
        private int _rowIndex;

        private RepeatingStreamCursor(SegmentState[] segments, int stride)
        {
            _segments = segments;
            _stride = stride;
            _segmentIndex = 0;
            _positionInRow = 0;
            _rowIndex = 0;
        }

        public bool IsValid => _segmentIndex < _segments.Length;
        public int SegmentIndex => _segmentIndex;
        public int CurrentOffset
        {
            get
            {
                SegmentState segment = _segments[_segmentIndex];
                int linearIndex = (_rowIndex * segment.CountX) + _positionInRow;
                return segment.DataOffset + linearIndex * _stride;
            }
        }

        public static RepeatingStreamCursor? FromVertexSegments(IReadOnlyList<ChBinVertexSegmentEntry> segments, int stride)
        {
            SegmentState[] values = segments
                .Where(static segment => segment.DataFileOffset.HasValue)
                .Select(static segment => new SegmentState(segment.DataFileOffset!.Value, segment.CountX, segment.CountY))
                .Where(static segment => segment.CountX > 0 && segment.CountY > 0)
                .ToArray();
            return values.Length == 0 ? null : new RepeatingStreamCursor(values, stride);
        }

        public static RepeatingStreamCursor? FromMeshSegments(IReadOnlyList<ChBinMeshSegmentEntry> segments, int stride)
        {
            SegmentState[] values = segments
                .Where(static segment => segment.PrimitiveIndicesFileOffset.HasValue)
                .Select(static segment => new SegmentState(segment.PrimitiveIndicesFileOffset!.Value, segment.CountX, segment.CountY))
                .Where(static segment => segment.CountX > 0 && segment.CountY > 0)
                .ToArray();
            return values.Length == 0 ? null : new RepeatingStreamCursor(values, stride);
        }

        public static RepeatingStreamCursor? FromLightingSegments(IReadOnlyList<ChBinLightingSegmentEntry> segments, int stride)
        {
            SegmentState[] values = segments
                .Where(static segment => segment.LightingValuesFileOffset.HasValue)
                .Select(static segment => new SegmentState(segment.LightingValuesFileOffset!.Value, segment.CountX, segment.CountY))
                .Where(static segment => segment.CountX > 0 && segment.CountY > 0)
                .ToArray();
            return values.Length == 0 ? null : new RepeatingStreamCursor(values, stride);
        }

        public void Advance()
        {
            if (!IsValid)
                return;

            _positionInRow++;
            if (_positionInRow < _segments[_segmentIndex].CountX)
                return;

            _positionInRow = 0;
            _rowIndex++;
            if (_rowIndex < _segments[_segmentIndex].CountY)
                return;

            _segmentIndex++;
            if (!IsValid)
                return;

            _rowIndex = 0;
        }

        private readonly record struct SegmentState(int DataOffset, int CountX, int CountY);
    }

    private readonly record struct ChBinPrimitiveRecord(IReadOnlyList<byte> VertexIndices, IReadOnlyList<byte> ColorIndices, byte PrimitiveMode);
}

public sealed class ChBinVisualDocument
{
    private readonly ChBinVisuals.ChBinTextureAnimator _animator;

    internal ChBinVisualDocument(ChBinFile file, IReadOnlyList<ChBinRenderableModel> models, IReadOnlyList<ChBinMaterialKey> materialKeys, ChBinVisuals.ChBinTextureAnimator animator)
    {
        File = file;
        Models = models;
        MaterialKeys = materialKeys;
        _animator = animator;
    }

    public ChBinFile File { get; }
    public IReadOnlyList<ChBinRenderableModel> Models { get; }
    public IReadOnlyList<ChBinMaterialKey> MaterialKeys { get; }
    public int TextureVersion { get; private set; } = 1;
    public bool HasAnimations => _animator.HasStreams;
    public bool HasAnimatedTextures => _animator.HasStreams;

    public void ResetAnimation()
    {
        _animator.Reset();
        TextureVersion++;
    }

    public bool AdvanceFrame()
        => AdvanceFrame(out _);

    public bool AdvanceFrame(out bool textureDirty)
    {
        if (!_animator.AdvanceFrame(out textureDirty))
            return false;

        if (textureDirty)
            TextureVersion++;

        return true;
    }

    public ChBinTexturePage BuildTexturePage(ChBinMaterialKey key)
        => _animator.BuildTexturePage(key);

    public Matrix GetModelAnimationMatrix(int entryIndex)
        => _animator.GetModelAnimationMatrix(entryIndex);

    public ChBinMaterialKey GetAnimatedMaterialKey(int primitiveIndex, ChBinMaterialKey fallbackMaterialKey)
        => _animator.GetAnimatedMaterialKey(primitiveIndex, fallbackMaterialKey);

    public ChBinUvRect GetAnimatedUvRect(int primitiveIndex, ChBinUvRect fallbackUvRect)
        => _animator.GetAnimatedUvRect(primitiveIndex, fallbackUvRect);
}

public sealed class ChBinRenderableModel
{
    public int EntryIndex { get; init; }
    public string Label { get; init; } = string.Empty;
    public byte PartId { get; init; }
    public byte GroupId { get; init; }
    public bool HasAnimation { get; init; }
    public int PrimitiveStartIndex { get; init; }
    public ushort PrimitiveCount { get; init; }
    public Vector3 SceneCenter { get; init; }
    public float SceneScale { get; init; }
    public VertexPositionColor[] WireVertices { get; init; } = Array.Empty<VertexPositionColor>();
    public VertexPositionColor[] SolidVertices { get; init; } = Array.Empty<VertexPositionColor>();
    public IReadOnlyList<ChBinTexturedPrimitive> TexturedPrimitives { get; init; } = Array.Empty<ChBinTexturedPrimitive>();
}

public sealed class ChBinTexturedPrimitive
{
    private readonly ChBinRawTexturedVertex[] _rawVertices;
    private readonly byte _primitiveMode;
    private VertexPositionColorTexture[]? _vertices4Bpp;
    private VertexPositionColorTexture[]? _vertices8Bpp;
    private VertexPositionColorTexture[]? _vertices16Bpp;

    public ChBinTexturedPrimitive(int globalPrimitiveIndex, ChBinMaterialKey baseMaterialKey, ChBinUvRect baseUvRect, byte primitiveMode, ChBinRawTexturedVertex[] rawVertices)
    {
        GlobalPrimitiveIndex = globalPrimitiveIndex;
        BaseMaterialKey = baseMaterialKey;
        BaseUvRect = baseUvRect;
        _primitiveMode = primitiveMode;
        _rawVertices = rawVertices;
        Vertices = BuildVertices(baseMaterialKey, GetMappedUvs(baseUvRect));

        CacheVertices(baseMaterialKey.ColorMode, Vertices);
    }

    public int GlobalPrimitiveIndex { get; }
    public ChBinMaterialKey BaseMaterialKey { get; }
    public ChBinUvRect BaseUvRect { get; }
    public VertexPositionColorTexture[] Vertices { get; }

    public VertexPositionColorTexture[] GetVertices(ChBinMaterialKey materialKey)
        => GetVertices(materialKey, BaseUvRect);

    public VertexPositionColorTexture[] GetVertices(ChBinMaterialKey materialKey, ChBinUvRect uvRect)
    {
        if (uvRect == BaseUvRect)
        {
            return materialKey.ColorMode switch
            {
                0 => _vertices4Bpp ??= BuildVertices(materialKey, GetMappedUvs(BaseUvRect)),
                1 => _vertices8Bpp ??= BuildVertices(materialKey, GetMappedUvs(BaseUvRect)),
                2 => _vertices16Bpp ??= BuildVertices(materialKey, GetMappedUvs(BaseUvRect)),
                _ => Vertices,
            };
        }

        return BuildVertices(materialKey, GetMappedUvs(uvRect));
    }

    private VertexPositionColorTexture[] BuildVertices(ChBinMaterialKey materialKey, IReadOnlyList<StgUV> mappedUvs)
    {
        var vertices = new VertexPositionColorTexture[_rawVertices.Length];
        for (int index = 0; index < _rawVertices.Length; index++)
        {
            ChBinRawTexturedVertex rawVertex = _rawVertices[index];
            StgUV uv = index < mappedUvs.Count ? mappedUvs[index] : rawVertex.Uv;
            vertices[index] = new VertexPositionColorTexture(rawVertex.Position, rawVertex.Color, materialKey.Normalize(uv));
        }

        return vertices;
    }

    private IReadOnlyList<StgUV> GetMappedUvs(ChBinUvRect uvRect)
    {
        if (_primitiveMode == 0 && _rawVertices.Length == 6)
        {
            return
            [
                uvRect.UV0,
                uvRect.UV1,
                uvRect.UV2,
                uvRect.UV1,
                uvRect.UV3,
                uvRect.UV2,
            ];
        }

        if (_primitiveMode == 1 && _rawVertices.Length == 3)
        {
            return
            [
                uvRect.UV0,
                uvRect.UV1,
                uvRect.UV2,
            ];
        }

        if (_rawVertices.Length == 3)
        {
            return
            [
                uvRect.UV0,
                uvRect.UV2,
                uvRect.UV3,
            ];
        }

        return _rawVertices.Select(static vertex => vertex.Uv).ToArray();
    }

    private void CacheVertices(int colorMode, VertexPositionColorTexture[] vertices)
    {
        switch (colorMode)
        {
            case 0:
                _vertices4Bpp = vertices;
                break;
            case 1:
                _vertices8Bpp = vertices;
                break;
            case 2:
                _vertices16Bpp = vertices;
                break;
        }
    }
}

public readonly record struct ChBinRawTexturedVertex(Vector3 Position, Color Color, StgUV Uv);

public readonly record struct ChBinMaterialKey(ushort TPage, ushort Cba)
{
    public const int TexturePageTexelWidth = 256;

    public int TPageX => TPage & 0x0F;
    public int TPageY => (TPage >> 4) & 0x01;
    public int ColorMode => (TPage >> 7) & 0x03;
    public bool IsSupported => ColorMode is 0 or 1 or 2;
    public int PageWidth => TexturePageTexelWidth;
    public int PageHeight => 256;

    public Vector2 Normalize(StgUV uv)
        => new(
            MathHelper.Clamp(uv.U / (float)(TexturePageTexelWidth - 1), 0f, 1f),
            MathHelper.Clamp(uv.V / 255f, 0f, 1f));

    public string Label => $"TP {TPage:X4} / CBA {Cba:X4}";
}

public readonly record struct ChBinTexturePage(int Width, int Height, Color[] Pixels, bool HasVisiblePixels)
{
    public static ChBinTexturePage Empty { get; } = new(1, 1, new[] { Color.Transparent }, false);
}

public readonly record struct ChBinUvRect(StgUV UV0, StgUV UV1, StgUV UV2, StgUV UV3);