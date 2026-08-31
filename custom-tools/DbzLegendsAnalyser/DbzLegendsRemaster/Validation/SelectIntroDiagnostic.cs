using System;
using DbzLegendsRemaster.SELECT_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: diagnostic for SELECT.EXE's menu screen. It boots the overlay headlessly for a frame
// budget, lists every sprite the frame would actually display, reports the state that decides
// which GsSortSprite path each one takes, and dumps the two draw buffers as BMPs so the frame can
// be looked at rather than inferred from a pixel count.
//
// It is what identified the dragon-ball ripples as accumulation rather than a projection fault:
// the ring of seven 71x70 sprites reads at radius 150 by frame 200, 40 by frame 400 and 190 by
// frame 800, so the balls converge and disperse. They were never stuck; the draw buffer was never
// cleared, which GP0(0x02) now does.
internal static class SelectIntroDiagnostic
{
    internal static int Run(string[] args)
    {
        int budget = 40;
        if (args.Length > 1 && int.TryParse(args[1], out int parsed))
        {
            budget = parsed;
        }

        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateSelectExe();

        FrameBaton.ResetHeadless(budget);
        string stopped = "budget epuise";
        try
        {
            new SELECT_EXE_exe().start();
            stopped = "start() a rendu la main";
        }
        catch (GameShutdownException)
        {
        }
        catch (LoadExecTransferException)
        {
            stopped = "LoadExec";
        }
        catch (Exception exception)
        {
            stopped = $"{exception.GetType().Name}: {exception.Message}";
        }

        Console.WriteLine($"=== budget {budget} frames, arret: {stopped} ===");

        LibGs.GsSPRITE[] sprites = SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec;
        Console.WriteLine($"sprites: {sprites.Length}");

        int badScale = 0;
        int rotated = 0;
        int visible = 0;
        for (int i = 0; i < sprites.Length; i++)
        {
            LibGs.GsSPRITE s = sprites[i];
            if (s.scalex != 0x1000 || s.scaley != 0x1000)
            {
                badScale++;
            }

            if (s.rotate != 0)
            {
                rotated++;
            }

            if ((s.attribute & 0x80000000u) == 0 && s.w != 0 && s.h != 0)
            {
                visible++;
            }
        }

        Console.WriteLine($"echelle != 0x1000: {badScale}   rotate != 0: {rotated}   affichables: {visible}");

        // The band sweep: elements 60..79, armed as 0x3b x 0xf0 upright bands.
        for (int i = 0x3c; i < 0x42; i++)
        {
            LibGs.GsSPRITE s = sprites[i];
            Console.WriteLine(
                $"  [{i:X2}] attr=0x{s.attribute:X8} x={s.x,5} y={s.y,5} w={s.w,4} h={s.h,4} " +
                $"scale={s.scalex:X4}/{s.scaley:X4} rot={s.rotate} tpage=0x{s.tpage:X3} " +
                $"u={s.u} v={s.v} cx=0x{s.cx:X3} cy=0x{s.cy:X3} mx={s.mx} my={s.my}");
        }

        // Which path would each of those take, by the routine's own gate.
        for (int i = 0x3c; i < 0x42; i++)
        {
            LibGs.GsSPRITE s = sprites[i];
            int scaleWord = (ushort)s.scalex | (s.scaley << 0x10);
            bool bit27 = ((s.attribute >> 0x1b) & 1) != 0;
            bool fastByScale = scaleWord == 0x10001000 && s.rotate == 0 && (s.attribute & 0xc00000) == 0;
            Console.WriteLine(
                $"  [{i:X2}] scaleWord=0x{scaleWord:X8} bit27={bit27} fastByScale={fastByScale} " +
                $"-> {(bit27 || fastByScale ? "SPRT rapide" : "MATRICE lente")}");
        }

        Console.WriteLine("=== sprites reellement affiches ===");
        for (int i = 0; i < sprites.Length; i++)
        {
            LibGs.GsSPRITE s = sprites[i];
            if ((s.attribute & 0x80000000u) != 0 || s.w == 0 || s.h == 0)
            {
                continue;
            }

            Console.WriteLine(
                $"  [{i:X2}] attr=0x{s.attribute:X8} x={s.x,5} y={s.y,5} w={s.w,4} h={s.h,4} " +
                $"scale={s.scalex:X4}/{s.scaley:X4} mx={s.mx,4} my={s.my,4} " +
                $"tpage=0x{s.tpage:X3} u={s.u,3} v={s.v,3} cx=0x{s.cx:X3} cy=0x{s.cy:X3}");
        }

        int lit = 0;
        long sum = 0;
        for (int y = 0; y < 240; y++)
        {
            for (int x = 0; x < 320; x++)
            {
                ushort v = LibGpu.Vram[(y * 1024) + x];
                if (v != 0)
                {
                    lit++;
                    sum += (v & 0x1f) + ((v >> 5) & 0x1f) + ((v >> 10) & 0x1f);
                }
            }
        }

        Console.WriteLine($"=== VRAM page0: {lit} pixels allumes, moyenne {(lit == 0 ? 0 : sum / (double)(lit * 3)):F2}/31");
        DumpBmp(0, 0, 320, 240, "select_page0.bmp");
        DumpBmp(0, 240, 320, 240, "select_page1.bmp");
        return 0;
    }

    // JUSTIFICATION: backend MonoGame only
    // RELATION: writes a VRAM window as an uncompressed 24-bit BMP so the frame can be looked at
    // rather than inferred from a count.
    private static void DumpBmp(int vx, int vy, int w, int h, string path)
    {
        int rowBytes = ((w * 3) + 3) & ~3;
        int pixelBytes = rowBytes * h;
        byte[] file = new byte[54 + pixelBytes];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        WriteI32(file, 2, file.Length);
        WriteI32(file, 10, 54);
        WriteI32(file, 14, 40);
        WriteI32(file, 18, w);
        WriteI32(file, 22, h);
        file[26] = 1;
        file[28] = 24;
        WriteI32(file, 34, pixelBytes);

        for (int y = 0; y < h; y++)
        {
            int dst = 54 + ((h - 1 - y) * rowBytes);
            for (int x = 0; x < w; x++)
            {
                ushort v = LibGpu.Vram[((vy + y) * 1024) + vx + x];
                file[dst + (x * 3) + 0] = (byte)(((v >> 10) & 0x1f) << 3);
                file[dst + (x * 3) + 1] = (byte)(((v >> 5) & 0x1f) << 3);
                file[dst + (x * 3) + 2] = (byte)((v & 0x1f) << 3);
            }
        }

        System.IO.File.WriteAllBytes(path, file);
        Console.WriteLine($"  ecrit {path}");
    }

    private static void WriteI32(byte[] b, int o, int v)
    {
        b[o] = (byte)v;
        b[o + 1] = (byte)(v >> 8);
        b[o + 2] = (byte)(v >> 16);
        b[o + 3] = (byte)(v >> 24);
    }
}
