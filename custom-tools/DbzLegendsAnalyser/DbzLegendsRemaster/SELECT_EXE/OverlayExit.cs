using PsxSdkMonogame;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.SELECT_EXE;

// The last emission block of SELECT.EXE's game code, 0x80034380..0x800347C3: FUN_80034380,
// FUN_800343a4, the frame step DrawFrame and the shutdown/LoadExec path ShutdownAndLoadExecutable, with
// SNMAIN's start immediately behind it at 0x800347C4.
//
// This file carries the two of them that are on the boot chain. The frame step is in FrameStep.cs;
// FUN_800343a4 @ 0x800343A4 (a bit-to-index helper the button-remap path feeds) has no caller on
// this slice's path and is not modelled.
internal static class OverlayExit
{
    // GHIDRA: g_PadRemapTable0 @ 0x801FF020
    // BUTTON REMAP TABLE A, inside the cross-overlay block. 0x801FF000 + 0x10 * 2 — the "index
    // 0x10" the bootstrap SLPS_003.55 fills through FUN_8002165c and TITLE.EXE reads back in
    // ProcessPadInput. SELECT.EXE reaches the same table.
    private const int g_PadRemapTable0_Address = unchecked((int)0x801FF020);

    // GHIDRA: g_PadRemapTable1 @ 0x801FF03C
    // BUTTON REMAP TABLE B. 0x801FF000 + 0x1E * 2 — the bootstrap's "index 0x1E".
    private const int g_PadRemapTable1_Address = unchecked((int)0x801FF03C);

    // GHIDRA: DAT_801fff00 @ 0x801FFF00
    // The EXEC header scratch LoadExec is given. Ghidra's decompilation of ShutdownAndLoadExecutable names it
    // &DAT_801fff00; TITLE.EXE's ShutdownAndLoadExecutable passes the same address.
    // PARTIAL: only the address reaches LoadExec; nothing in SELECT.EXE reads the contents.
    private const int DAT_801fff00 = unchecked((int)0x801FFF00);

    // GHIDRA: g_PadRemapTablePointers2 @ 0x80055B44
    // .sbss. Holds &g_PadRemapTable0. Its consumer is FUN_8002c048 @ 0x8002C048, which reads the pair
    // below as a two-entry array selected by player index — `(&g_PadRemapTablePointers2)[param_1]` — and
    // indexes the table it points at by a pad byte.
    internal static int g_PadRemapTablePointers2;

    // GHIDRA: DAT_80055b48 @ 0x80055B48
    // .sbss. Holds &g_PadRemapTable1, the second entry of that pair.
    internal static int DAT_80055b48;

    // GHIDRA: FUN_80034380 @ 0x80034380
    // Thirty-six bytes, two stores, no callees. It publishes the two pointers into the
    // cross-overlay save block that the pad-remap path later reads. main calls it once, between
    // the graphics bring-up and the memory-card bring-up.
    internal static void FUN_80034380()
    {
        g_PadRemapTablePointers2 = g_PadRemapTable0_Address;
        DAT_80055b48 = g_PadRemapTable1_Address;
    }

    // GHIDRA: ShutdownAndLoadExecutable @ 0x8003472C
    // THE EXIT PATH. One hundred and fifty-two bytes, and the ONLY LoadExec call site in the whole
    // program (find-cross-references on LoadExec @ 0x8004ED74 reports exactly one caller).
    //
    // Its three call sites, and the three overlays SELECT.EXE can hand control to — these are also
    // the only three "cdrom:" strings in the image:
    //     RunDemoModeScreen @ 0x80030AF8 (state 0)  "cdrom:\\DEMO.EXE;1"  @ 0x80020674
    //     RunVsModeScreen @ 0x80030EF8 (state 1)  "cdrom:\\VS.EXE;1"    @ 0x80020688
    //     RunSpModeScreen @ 0x800310A8 (state 2)  "cdrom:\\SP.EXE;1"    @ 0x80020698
    // There is no "cdrom:\\TITLE.EXE;1" in SELECT.EXE: this overlay cannot go back.
    //
    // Against TITLE.EXE's ShutdownAndLoadExecutable @ 0x80058158, same source with three
    // additions: the memory-card teardown ShutdownMemoryCard, VSync(0) and CdFlush, and ResetGraph runs
    // before PadStop rather than after.
    internal static void ShutdownAndLoadExecutable(string param_1)
    {
        MemoryCard.ShutdownMemoryCard();
        VSync(0);
        StopRCnt(unchecked((long)0xf2000000));
        StopRCnt(unchecked((long)0xf2000001));
        StopRCnt(unchecked((long)0xf2000002));
        StopRCnt(unchecked((long)0xf2000003));
        PadStop();
        ResetGraph(0);
        CdFlush();
        StopCallback();
        _96_init();
        LoadExec(param_1, DAT_801fff00, 0);
    }

    // GHIDRA: LoadExec @ 0x8004ED74
    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: A0(0x51) replaces the resident executable and transfers control permanently, so it
    // never returns to its caller — every original call site is followed by unreachable code.
    // LibApi.LoadExec is a no-op, so the port models the transfer at the game layer, exactly as
    // SLPS_003_55, MOVIE_EXE and TITLE_EXE already do.
    //
    // PARTIAL: none of the three targets is transliterated, so there is nothing to hand over to.
    // DEMO.EXE, VS.EXE and SP.EXE are all present under data/ (159744, 942080 and 942080 bytes),
    // and WaitDiscLoad below reproduces the drive time each would take, so when one of them is
    // ported the dispatch goes here and nowhere else.
    private static void LoadExec(string exeFileName, int param_2, int param_3)
    {
        _ = param_2;
        _ = param_3;

        // JUSTIFICATION: PSX hardware adaptation only
        // RELATION: see LibCd.WaitDiscLoad — the drive spends real time fetching the overlay, and
        // without it a held button carries straight into the next screen.
        WaitDiscLoad(exeFileName);

        throw new LoadExecTransferException();
    }
}
