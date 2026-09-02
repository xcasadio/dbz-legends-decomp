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
    // JUSTIFICATION: backend MonoGame only
    // RELATION: drives SELECT.EXE's options screen — main's case 3 — on its own frame budget, so the
    // screen it draws can be looked at rather than inferred from the fact that it compiles. This
    // port has nine defects on record that were correct code producing nothing, and every one of
    // them was found by looking at the frame.
    //
    // It boots the overlay first, because BuildOptionsScreen draws against the sprite array, the
    // ordering tables and the VRAM the boot arms. Then it re-arms the headless baton and calls
    // RunOptionsScreen directly: the screen owns a blocking do/while and never returns on its own
    // without a cancel press, so the budget is what ends it.
    // JUSTIFICATION: backend MonoGame only
    // RELATION: drives the save and the load arms of the options screen's row 3 directly, because
    // the question they answer cannot be settled by reading: does the ported card path actually
    // COMPLETE, or does it enter the flow and stall on a stubbed BIOS handshake?
    //
    // It calls OptionsScreen.FUN_80031c8c, which is the row-3 confirm: 0 selects mode 2, the write
    // side, and any other value selects mode 3, the read side. Each arm gets its own frame budget,
    // because both own blocking do/while loops with VSync waits and neither returns on a desktop
    // without one.
    internal static int RunSaveLoad(string[] args)
    {
        int budget = 400;
        if (args.Length > 1 && int.TryParse(args[1], out int parsed))
        {
            budget = parsed;
        }

        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateSelectExe();

        FrameBaton.ResetHeadless(240);
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

        Console.WriteLine("=== boot termine");
        Console.WriteLine($"carte presente port 0: {LibMcrd.CardIsPresent(0)}");
        Console.WriteLine($"g_CardProbeResult = {SharedHighRam.g_CardProbeResult}");

        Drive("SAUVEGARDE  FUN_80031c8c(0) -> mode 2", 0, budget);
        Drive("CHARGEMENT  FUN_80031c8c(1) -> mode 3", 1, budget);
        return 0;
    }

    // JUSTIFICATION: backend MonoGame only
    private static void Drive(string label, uint arg, int budget)
    {
        Console.WriteLine();
        Console.WriteLine($"########## {label}");
        MemoryCard.g_CardOperationState = 0;
        MemoryCard.g_CardReprobeRequest = 0;

        FrameBaton.ResetHeadless(budget);
        string stopped = "budget epuise";
        try
        {
            OptionsScreen.FUN_80031c8c(arg);
            stopped = "REVENU NORMALEMENT";
        }
        catch (GameShutdownException)
        {
        }
        catch (Exception exception)
        {
            stopped = $"{exception.GetType().Name}: {exception.Message}";
        }

        Console.WriteLine($"  arret            : {stopped}");
        Console.WriteLine($"  g_CardOperationState = {MemoryCard.g_CardOperationState}");
        Console.WriteLine($"  g_CardReprobeRequest = {MemoryCard.g_CardReprobeRequest}");
        Console.WriteLine($"  g_CardProbeResult    = {SharedHighRam.g_CardProbeResult}");

        // The six save records the load side fills, at 0x801FF200 and 0x801FF218. All zero means
        // the read found nothing; non-zero means a record came back off the card.
        string records = string.Empty;
        for (int i = 0; i < 6; i++)
        {
            records += $" {SharedHighRam.INT_ARRAY_801ff200[i]:X8}";
        }

        Console.WriteLine($"  enregistrements      :{records}");

        // JUSTIFICATION: backend MonoGame only
        // RELATION: this row-3 confirm reaches CardRecords.FUN_800276d8, whose mode 2/3 arms are the
        // two call sites the mandate names for ShowCardMessage(5) / ShowCardMessage(1). ShowCardMessage
        // draws exactly two frames with the dim boxfill armed, then restores the five sprites and
        // draws a third with them back to normal — double buffering means the message frame's content
        // survives in whichever of the two VRAM pages the restore frame did NOT just overwrite, so
        // dumping both after Drive() returns is enough to see it without editing MemoryCard.cs to add
        // a capture hook.
        LibGs.GsBOXF boxf0 = FrameStep.GsBOXF_ARRAY_80067b68[0];
        Console.WriteLine(
            $"  boxfill[0] apres l'appel : attr=0x{boxf0.attribute:X8} x={boxf0.x} y={boxf0.y} " +
            $"w={boxf0.w} h={boxf0.h} rgb=({boxf0.r},{boxf0.g},{boxf0.b})");
        for (int page = 0; page < 2; page++)
        {
            int vy = page * 240;
            int lit = 0;
            for (int y = 0; y < 240; y++)
            {
                for (int x = 0; x < 320; x++)
                {
                    if (LibGpu.Vram[((vy + y) * 1024) + x] != 0)
                    {
                        lit++;
                    }
                }
            }

            Console.WriteLine($"  VRAM page{page}: {lit} pixels allumes");
            DumpBmp(0, vy, 320, 240, $"saveload_{arg}_page{page}.bmp");
        }
    }

    internal static int RunOptions(string[] args)
    {
        int budget = 120;
        if (args.Length > 1 && int.TryParse(args[1], out int parsed))
        {
            budget = parsed;
        }

        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateSelectExe();

        FrameBaton.ResetHeadless(240);
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

        Console.WriteLine("=== boot termine, entree dans RunOptionsScreen ===");

        FrameBaton.ResetHeadless(budget);
        string stopped = "budget epuise";
        try
        {
            SELECT_EXE_exe.RunOptionsScreen();
            stopped = "RunOptionsScreen a rendu la main";
        }
        catch (GameShutdownException)
        {
        }
        catch (Exception exception)
        {
            stopped = $"{exception.GetType().Name}: {exception.Message}";
        }

        Console.WriteLine($"=== {budget} frames, arret: {stopped}");
        Console.WriteLine($"curseur de ligne g_OptionsCursor = {SELECT_EXE_exe.g_OptionsCursor}");
        Console.WriteLine($"difficulte DAT_801ff01c = {SharedHighRam.DAT_801ff01c}   "
            + $"mono _DAT_801ff01e = {SharedHighRam._DAT_801ff01e}");

        LibGs.GsSPRITE[] sprites = SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec;
        int visible = 0;
        for (int i = 0; i < sprites.Length; i++)
        {
            if ((sprites[i].attribute & 0x80000000u) == 0 && sprites[i].w != 0 && sprites[i].h != 0)
            {
                visible++;
            }
        }

        Console.WriteLine($"sprites affichables: {visible}");

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
        DumpBmp(0, 0, 320, 240, "options_page0.bmp");
        DumpBmp(0, 240, 320, 240, "options_page1.bmp");
        return 0;
    }

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
