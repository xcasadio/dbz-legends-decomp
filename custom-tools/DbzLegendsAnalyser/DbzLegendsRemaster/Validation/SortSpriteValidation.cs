using System;
using DbzLegendsRemaster.SELECT_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: GsSortSprite @ 0x8004820C reaches the same picture by two different routes. When the
// sprite is unrotated at unit scale and carries neither attribute bit 22 nor 23, it emits a SPRT
// rectangle directly. Otherwise it builds a matrix — RotMatrix or the identity image at
// DAT_800653D8, then ScaleMatrix, TransMatrix — and projects four corners through RotTransPers4
// into a POLY_FT4.
//
// Nothing on screen distinguishes the two. A matrix path that collapsed its four corners onto one
// point, which is exactly what happens if DAT_800653D8 is left zero or the GTE offset disagrees
// with DAT_80065394, would draw nothing at all and read as a missing sprite rather than a broken
// projection. That failure mode has already cost this port nine silent defects.
//
// So this bench makes the two routes meet on a case where they must agree. At unit scale with no
// rotation, attribute bit 22 forces the matrix path while only swapping the two V coordinates —
// the geometry is untouched. The four projected corners must then reproduce the rectangle the fast
// path emits, exactly.
internal static class SortSpriteValidation
{
    private static int _failures;

    internal static int Run()
    {
        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateSelectExe();

        FrameBaton.ResetHeadless(120);
        try
        {
            new SELECT_EXE_exe().start();
        }
        catch (GameShutdownException)
        {
        }
        catch (LoadExecTransferException)
        {
        }
        catch (Exception exception)
        {
            Console.WriteLine($"boot: {exception.GetType().Name}: {exception.Message}");
        }

        Console.Write("matrice DAT_800653D8:");
        for (int i = 0; i < 9; i++)
        {
            Console.Write($" {BitConverter.ToInt16(LibGs.DAT_800653d8, i * 2)}");
        }

        Console.WriteLine();
        Console.WriteLine($"decalage 2D: DAT_80065394={LibGs.DAT_80065394} DAT_80065398={LibGs.DAT_80065398}");

        bool identity = BitConverter.ToInt16(LibGs.DAT_800653d8, 0) == 0x1000
            && BitConverter.ToInt16(LibGs.DAT_800653d8, 8) == 0x1000
            && BitConverter.ToInt16(LibGs.DAT_800653d8, 0x10) == 0x1000;
        if (!identity)
        {
            Console.WriteLine("  ECHEC: valiable_init n a pas arme la matrice identite");
            _failures++;
        }

        Case("origine", 0, 0, 64, 32, 0, 0);
        Case("pivot centre", 40, -20, 64, 32, 32, 16);
        Case("pivot coin", 159, 111, 160, 40, 159, 39);
        Case("coordonnees negatives", -84, -108, 176, 40, 0, 0);

        Console.WriteLine(_failures == 0
            ? "=== chemin matriciel: conforme au chemin rapide"
            : $"=== chemin matriciel: {_failures} echec(s)");
        return _failures == 0 ? 0 : 1;
    }

    private static LibGs.GsSPRITE Make(short x, short y, ushort w, ushort h, short mx, short my, uint attribute)
    {
        LibGs.GsSPRITE s = new LibGs.GsSPRITE();
        s.attribute = attribute;
        s.x = x;
        s.y = y;
        s.w = w;
        s.h = h;
        s.mx = mx;
        s.my = my;
        s.scalex = 0x1000;
        s.scaley = 0x1000;
        s.rotate = 0;
        s.u = 10;
        s.v = 20;
        s.tpage = 0x0e;
        s.cx = 0x100;
        s.cy = 0x1f0;
        s.r = 0x80;
        s.g = 0x80;
        s.b = 0x80;
        return s;
    }

    private static void Case(string name, short x, short y, ushort w, ushort h, short mx, short my)
    {
        LibGs.GsOT ot = SELECT_EXE_exe.GsOT_800654c4[0];

        int fastPacket = LibGs.DAT_80059430;
        LibGs.GsSortSprite(Make(x, y, w, h, mx, my, 0), ot, 0);

        // Bit 22 costs the fast path its gate — (attribute & 0xc00000) == 0 — and buys nothing but a
        // swap of the two V coordinates, which the geometry never sees.
        int slowPacket = LibGs.DAT_80059430;
        LibGs.GsSortSprite(Make(x, y, w, h, mx, my, 0x400000), ot, 0);

        if (!LibGpu.RamResolve(fastPacket, out byte[] fastBuf, out int fastOff)
            || !LibGpu.RamResolve(slowPacket, out byte[] slowBuf, out int slowOff))
        {
            Console.WriteLine($"  {name}: ECHEC - paquet non resolu");
            _failures++;
            return;
        }

        int fastCommand = (BitConverter.ToInt32(fastBuf, fastOff + 8) >> 24) & 0xfc;
        int slowCommand = (BitConverter.ToInt32(slowBuf, slowOff + 4) >> 24) & 0xfc;
        if (fastCommand != 0x64 || slowCommand != 0x2c)
        {
            Console.WriteLine(
                $"  {name}: ECHEC - commandes 0x{fastCommand:X2}/0x{slowCommand:X2}, attendu 0x64/0x2C");
            _failures++;
            return;
        }

        int fastXy = BitConverter.ToInt32(fastBuf, fastOff + 0xc);
        int fastWh = BitConverter.ToInt32(fastBuf, fastOff + 0x14);
        int rx = (short)(fastXy & 0xffff);
        int ry = (short)(fastXy >> 16);
        int rw = fastWh & 0xffff;
        int rh = (fastWh >> 16) & 0xffff;

        int[] cornerOffsets = { 8, 0x10, 0x18, 0x20 };
        int[] expectX = { rx, rx + rw, rx, rx + rw };
        int[] expectY = { ry, ry, ry + rh, ry + rh };

        string got = string.Empty;
        bool ok = rw == w && rh == h;
        for (int i = 0; i < 4; i++)
        {
            int packed = BitConverter.ToInt32(slowBuf, slowOff + cornerOffsets[i]);
            int px = (short)(packed & 0xffff);
            int py = (short)(packed >> 16);
            got += $" ({px},{py})";
            if (px != expectX[i] || py != expectY[i])
            {
                ok = false;
            }
        }

        if (ok)
        {
            Console.WriteLine($"  {name}: SPRT ({rx},{ry}) {rw}x{rh} == quad{got}");
            return;
        }

        Console.WriteLine($"  {name}: ECHEC - SPRT ({rx},{ry}) {rw}x{rh}, quad{got}, attendu"
            + $" ({expectX[0]},{expectY[0]}) ({expectX[1]},{expectY[1]})"
            + $" ({expectX[2]},{expectY[2]}) ({expectX[3]},{expectY[3]})");
        _failures++;
    }
}
