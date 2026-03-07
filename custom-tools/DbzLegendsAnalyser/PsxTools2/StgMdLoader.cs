namespace PsxTools2;

// ─────────────────────────────────────────────────────────────────────────────
//  STGxMD.B — fully-understood format (March 2026)
//  See docs/STG_MD_FILE_FORMAT_ANALYSIS.md for complete documentation.
//
//  File layout:
//    [0x00] u32 meshTableOffset  (= 0x08)
//    [0x04] u32 particleListOffset
//    [0x08] MeshTableEntry[16]   (16 × 8 bytes)
//    [particleListOffset] ParticleNodeList
//    [meshDataOffset - 4] u32 renderTableRelOffset   ← per mesh
//    [meshDataOffset .. +renderTableRelOffset-4] raw SVECTOR vertex data
//    [meshDataOffset + renderTableRelOffset - 4] RenderTable + MeshPart(s)
// ─────────────────────────────────────────────────────────────────────────────

public static class StgMdLoader
{
    // Byte size of one primitive entry for each type 0-7
    private static readonly int[] PrimitiveSizes = { 44, 52, 60, 76, 36, 44, 60, 80 };

    public static StgModelFile Load(string filePath)
        => Load(File.ReadAllBytes(filePath));

    public static StgModelFile Load(byte[] data)
    {
        var model = new StgModelFile();
        if (data.Length < 8) return model;

        // ── Header ──────────────────────────────────────────────────────────
        uint meshTableOffset    = LE32(data, 0);    // always 0x08
        uint particleListOffset = LE32(data, 4);

        // ── Mesh table (16 entries × 8 bytes) ───────────────────────────────
        int tableBase = (int)meshTableOffset;
        for (int i = 0; i < 16; i++)
        {
            int entryOff = tableBase + i * 8;
            if (entryOff + 8 > data.Length) break;

            uint meshDataOffset = LE32(data, entryOff);
            uint meshType       = LE32(data, entryOff + 4);

            model.MeshEntries.Add(new StgMeshEntry
            {
                Index      = i,
                FileOffset = (int)meshDataOffset,
                Type       = (int)meshType,
                Parts      = meshDataOffset > 4 && meshDataOffset < data.Length
                             ? ReadMeshParts(data, (int)meshDataOffset)
                             : []
            });
        }

        // ── Particle list ────────────────────────────────────────────────────
        if (particleListOffset + 2 <= data.Length)
        {
            int plOff = (int)particleListOffset;
            ushort count = LE16(data, plOff);
            plOff += 2;

            for (int i = 0; i < count; i++)
            {
                if (plOff + 6 > data.Length) break;
                short meshIndex = (short)LE16(data, plOff);
                short posX      = (short)LE16(data, plOff + 2);
                short posZ      = (short)LE16(data, plOff + 4);
                plOff += 6;

                if (meshIndex >= 0 && meshIndex < model.MeshEntries.Count)
                {
                    model.Particles.Add(new StgParticle
                    {
                        MeshIndex = meshIndex,
                        WorldX    = posX,
                        WorldY    = 0,
                        WorldZ    = posZ,
                        Mesh      = model.MeshEntries[meshIndex]
                    });
                }
            }
        }

        return model;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Read all MeshParts from a mesh blob.
    //
    //  Layout (file-relative addresses):
    //    (fileOffset - 4)                         → u32 renderTableRelOffset
    //    (fileOffset + renderTableRelOffset - 4)  → RenderTable { partCount, offsets[] }
    //    (RenderTable base + offsets[i])          → MeshPart (passed to RenderMesh)
    // ─────────────────────────────────────────────────────────────────────────
    private static List<StgMeshPart> ReadMeshParts(byte[] data, int fileOffset)
    {
        var parts = new List<StgMeshPart>();

        // renderTableRelOffset lives at (fileOffset - 4)
        int rendOffAddr = fileOffset - 4;
        if (rendOffAddr < 0 || rendOffAddr + 4 > data.Length) return parts;

        uint renderTableRelOffset = LE32(data, rendOffAddr);
        if (renderTableRelOffset == 0 || renderTableRelOffset > 0x100000) return parts;

        // renderTableBase = meshDataOffset + renderTableRelOffset - 4
        // In the decompilation: meshDataPtr = modelDataPtr - 4,  modelDataPtr = meshDataOffset + renderTableRelOffset
        int renderTableBase = fileOffset + (int)renderTableRelOffset - 4;
        if (renderTableBase < 0 || renderTableBase + 8 > data.Length) return parts;

        uint partCount = LE32(data, renderTableBase);
        if (partCount == 0 || partCount > 64) return parts;

        for (int p = 0; p < (int)partCount; p++)
        {
            int offsetSlot = renderTableBase + 4 + p * 4;
            if (offsetSlot + 4 > data.Length) break;

            // RenderMesh ptr = (int)meshDataPtr + meshDataPtr[1 + p]
            //                = renderTableBase + offsets[p]
            uint partByteOffset = LE32(data, offsetSlot);
            int  partAddr       = renderTableBase + (int)partByteOffset;
            if (partAddr < 0 || partAddr + 8 > data.Length) continue;

            var part = ReadSinglePart(data, partAddr);
            if (part != null) parts.Add(part);
        }

        return parts;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Read one MeshPart.
    //  MeshPart: { u32 numSections, Section[numSections] }
    //  Section:  { u16 count, u16 typeFlags, count × primitiveData }
    // ─────────────────────────────────────────────────────────────────────────
    private static StgMeshPart? ReadSinglePart(byte[] data, int partAddr)
    {
        uint numSections = LE32(data, partAddr);
        if (numSections == 0 || numSections > 32) return null;

        var part   = new StgMeshPart { FileOffset = partAddr };
        int cursor = partAddr + 4;

        for (int s = 0; s < (int)numSections; s++)
        {
            if (cursor + 4 > data.Length) break;

            ushort primCount = LE16(data, cursor);
            ushort typeFlags = LE16(data, cursor + 2);
            int    primType  = typeFlags & 7;
            cursor += 4;

            if (primCount == 0 || primCount > 2000) break;
            int primSize     = PrimitiveSizes[primType];
            int sectionBytes = primCount * primSize;
            if (cursor + sectionBytes > data.Length) break;

            var section = new StgMeshSection
            {
                PrimitiveType  = (StgPrimitiveType)primType,
                PrimitiveCount = primCount,
                FileOffset     = cursor,
                Triangles      = ExtractTriangles(data, cursor, primCount, primType)
            };
            part.Sections.Add(section);
            cursor += sectionBytes;
        }

        return part.Sections.Count > 0 ? part : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Extract triangles (quads split into 2 tris) from a section's raw data.
    //
    //  SVECTOR = { short vx, vy, vz, pad }  (8 bytes)
    //  Vertex positions always first:
    //    Tri types  (0,2,4,6): v0@+0,  v1@+8,  v2@+16
    //    Quad types (1,3,5,7): v0@+0,  v1@+8,  v2@+16, v3@+24
    //  Color fields (non-textured types):
    //    F3  (4): single BGRA at +32
    //    F4  (5): single BGRA at +40
    //    G3  (6): BGRA at +48, +52, +56
    //    G4  (7): BGRA at +64, +68, +72, +76
    // ─────────────────────────────────────────────────────────────────────────
    private static List<StgTriangle> ExtractTriangles(byte[] data, int sectionStart, int count, int primType)
    {
        bool isQuad  = IsQuad(primType);
        int  primSize = PrimitiveSizes[primType];
        var  tris    = new List<StgTriangle>(count * (isQuad ? 2 : 1));

        for (int p = 0; p < count; p++)
        {
            int off = sectionStart + p * primSize;
            if (off + primSize > data.Length) break;

            var v0 = ReadVec3(data, off + 0);
            var v1 = ReadVec3(data, off + 8);
            var v2 = ReadVec3(data, off + 16);
            var v3 = isQuad ? ReadVec3(data, off + 24) : default;

            var (c0, c1, c2, c3) = ReadColors(data, off, primType);

            tris.Add(new StgTriangle(v0, v1, v2, c0, c1, c2));

            if (isQuad)
                // PSX quad winding: v0,v1,v2,v3 → tri(v0,v1,v2) + tri(v1,v3,v2)
                tris.Add(new StgTriangle(v1, v3, v2, c1, c3, c2));
        }

        return tris;
    }

    private static (Color c0, Color c1, Color c2, Color c3) ReadColors(byte[] data, int off, int primType)
    {
        switch (primType)
        {
            case 4: { var c = Clr(data, off + 32); return (c, c, c, c); }   // POLY_F3
            case 5: { var c = Clr(data, off + 40); return (c, c, c, c); }   // POLY_F4
            case 6: return (Clr(data, off + 48), Clr(data, off + 52),        // POLY_G3
                            Clr(data, off + 56), Color.White);
            case 7: return (Clr(data, off + 64), Clr(data, off + 68),        // POLY_G4
                            Clr(data, off + 72), Clr(data, off + 76));
            default: return (Color.White, Color.White, Color.White, Color.White);
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    static bool IsQuad(int t) => t == 1 || t == 3 || t == 5 || t == 7;

    static Vec3 ReadVec3(byte[] data, int offset)
    {
        if (offset + 6 > data.Length) return default;
        return new Vec3(
            (short)LE16(data, offset),
            (short)LE16(data, offset + 2),
            (short)LE16(data, offset + 4));
    }

    static Color Clr(byte[] data, int offset)
    {
        if (offset + 3 > data.Length) return Color.White;
        // PSX colors are 0-128 range; double to display as 0-255
        return Color.FromArgb(
            Math.Min(255, data[offset]     * 2),
            Math.Min(255, data[offset + 1] * 2),
            Math.Min(255, data[offset + 2] * 2));
    }

    static uint   LE32(byte[] d, int o) => BitConverter.ToUInt32(d, o);
    static ushort LE16(byte[] d, int o) => BitConverter.ToUInt16(d, o);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Data model
// ─────────────────────────────────────────────────────────────────────────────

public class StgModelFile
{
    public List<StgMeshEntry> MeshEntries { get; } = new();
    public List<StgParticle>  Particles   { get; } = new();

    /// <summary>All triangles in world space (particle offset applied).</summary>
    public IEnumerable<StgTriangle> GetWorldTriangles()
    {
        foreach (var p in Particles)
        foreach (var part in p.Mesh.Parts)
        foreach (var section in part.Sections)
        foreach (var tri in section.Triangles)
            yield return tri.Translate(p.WorldX, p.WorldY, p.WorldZ);
    }
}

public class StgMeshEntry
{
    public int   Index      { get; init; }
    public int   FileOffset { get; init; }
    public int   Type       { get; init; }   // 1 = static, 2 = animated
    public List<StgMeshPart> Parts { get; init; } = new();

    public int TotalTriangles => Parts.Sum(p => p.Sections.Sum(s => s.Triangles.Count));
}

public class StgParticle
{
    public int          MeshIndex { get; init; }
    public short        WorldX    { get; init; }
    public short        WorldY    { get; init; }
    public short        WorldZ    { get; init; }
    public StgMeshEntry Mesh      { get; init; } = null!;
}

public class StgMeshPart
{
    public int FileOffset { get; init; }
    public List<StgMeshSection> Sections { get; } = new();
}

public class StgMeshSection
{
    public StgPrimitiveType  PrimitiveType  { get; init; }
    public int               PrimitiveCount { get; init; }
    public int               FileOffset     { get; init; }
    public List<StgTriangle> Triangles      { get; init; } = new();
}

/// <summary>A single triangle in local mesh space.</summary>
public record struct StgTriangle(Vec3 V0, Vec3 V1, Vec3 V2, Color C0, Color C1, Color C2)
{
    public StgTriangle Translate(float dx, float dy, float dz) => this with
    {
        V0 = V0.Add(dx, dy, dz),
        V1 = V1.Add(dx, dy, dz),
        V2 = V2.Add(dx, dy, dz)
    };

    public Color AverageColor => Color.FromArgb(
        (C0.R + C1.R + C2.R) / 3,
        (C0.G + C1.G + C2.G) / 3,
        (C0.B + C1.B + C2.B) / 3);
}

/// <summary>3D vector (PSX int16 units cast to float for math).</summary>
public record struct Vec3(float X, float Y, float Z)
{
    public Vec3 Add(float dx, float dy, float dz) => new(X + dx, Y + dy, Z + dz);
}

public enum StgPrimitiveType
{
    POLY_FT3 = 0,  // Flat textured tri
    POLY_FT4 = 1,  // Flat textured quad
    POLY_GT3 = 2,  // Gouraud textured tri
    POLY_GT4 = 3,  // Gouraud textured quad
    POLY_F3  = 4,  // Flat colored tri
    POLY_F4  = 5,  // Flat colored quad
    POLY_G3  = 6,  // Gouraud colored tri
    POLY_G4  = 7,  // Gouraud colored quad
}
