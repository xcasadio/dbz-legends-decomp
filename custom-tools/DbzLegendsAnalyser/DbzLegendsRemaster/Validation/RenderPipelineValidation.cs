using System;
using DbzLegendsRemaster.TITLE_EXE;
using PsxSdkMonogame;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: end-to-end bench for the drawing path TITLE.EXE relies on — ordering table, AddPrim,
// DrawOTag, software rasterizer, VRAM.
//
// It matters because every piece of that chain was inert until now: ClearOTag had no in-memory
// form at all, so RunFrameLoop's table was never linked, and DrawOTag(int) had no behaviour
// without an installed handler. Both were silent: a game could run its whole frame loop, submit
// primitives, and produce a black screen with nothing to show for it.
//
// The bench builds one flat TILE by hand, links it, submits the table, and checks the pixels
// really landed — inside the rectangle and nowhere else.
internal static class RenderPipelineValidation
{
    // A spare stretch of TITLE.EXE's .bss, clear of every buffer this port declares.
    private const int TestPrimitiveAddress = unchecked((int)0x800B0000);

    private const int TileX = 40;
    private const int TileY = 60;
    private const int TileW = 50;
    private const int TileH = 30;

    private static int s_failures;

    internal static int Run()
    {
        s_failures = 0;

        Array.Clear(LibGpu.Vram, 0, LibGpu.Vram.Length);

        // La zone de dessin, comme RunFrameLoop la pose.
        var env = new DRAWENV();
        SetDefDrawEnv(env, 0, 0, 0x140, 0xf0);
        env.isbg = 0;
        PutDrawEnv(env);

        // La table d'affichage de TITLE.EXE, chainee vers l'avant.
        RamRegion(unchecked((int)0x800A6830), FrameLoop.OT_800a6830);
        ClearOTag(FrameLoop.OT_800a6830, 0, 0x800);

        uint firstLink = ReadWord(FrameLoop.OT_800a6830, 0);
        uint lastLink = ReadWord(FrameLoop.OT_800a6830, 0x7ff * 4);
        Check((firstLink & 0x00ffffff) == 0x00A6834,
            $"la premiere entree pointe vers la suivante, lu 0x{firstLink:X8}");
        Check((lastLink & 0x00ffffff) == 0x00ffffff,
            $"la derniere entree termine la chaine, lu 0x{lastLink:X8}");

        // Une TILE construite a la main, a une adresse reelle.
        byte[] primitive = new byte[16];
        RamRegion(TestPrimitiveAddress, primitive);
        SetTile(primitive, 0);
        primitive[4] = 0xf8;  // r0
        primitive[5] = 0x20;  // g0
        primitive[6] = 0x20;  // b0
        WriteI16(primitive, 8, TileX);
        WriteI16(primitive, 10, TileY);
        WriteI16(primitive, 12, TileW);
        WriteI16(primitive, 14, TileH);

        Check(primitive[3] == 3 && primitive[7] == 0x60,
            $"TILE taguee 3 mots / code 0x60, lu {primitive[3]} / 0x{primitive[7]:X2}");

        // Le maillage: la primitive prend la tete du bucket 0.
        AddPrim(FrameLoop.OT_800a6830, 0, primitive, 0);
        uint bucket = ReadWord(FrameLoop.OT_800a6830, 0);
        Check((bucket & 0x00ffffff) == (TestPrimitiveAddress & 0x00ffffff),
            $"le bucket 0 pointe sur la primitive, lu 0x{bucket & 0x00ffffff:X6}");

        uint primNext = ReadWord(primitive, 0);
        Check((primNext >> 24) == 3, $"la primitive garde sa longueur, lu {primNext >> 24}");

        // Et la soumission.
        DrawOTag(unchecked((int)0x800A6830));

        int inside = 0;
        int outside = 0;
        for (int y = 0; y < 240; y++)
        {
            for (int x = 0; x < 320; x++)
            {
                bool nonZero = LibGpu.Vram[(y * 1024) + x] != 0;
                bool within = x >= TileX && x < TileX + TileW && y >= TileY && y < TileY + TileH;
                if (nonZero && within)
                {
                    inside++;
                }
                else if (nonZero)
                {
                    outside++;
                }
            }
        }

        Check(inside == TileW * TileH,
            $"le rectangle couvre ses {TileW * TileH} pixels, lu {inside}");
        Check(outside == 0, $"rien n'est ecrit hors du rectangle, lu {outside}");

        Console.WriteLine($"  rectangle: {inside} pixels dedans, {outside} dehors");

        Console.WriteLine(s_failures == 0
            ? "RENDER: toutes les verifications passent"
            : $"RENDER: {s_failures} echec(s)");
        return s_failures == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            s_failures++;
            Console.WriteLine($"  ECHEC: {label}");
        }
    }

    private static uint ReadWord(byte[] b, int o) =>
        (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    private static void WriteI16(byte[] b, int o, int v)
    {
        b[o] = (byte)v;
        b[o + 1] = (byte)(v >> 8);
    }
}
