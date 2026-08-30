using PsxSdkMonogame;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.TITLE_EXE;

// The title screen frame loop. RunFrameLoop @ 0x800587A8 flips a double buffer, samples the pad,
// runs all twenty-one task lists, then submits the ordering table — once per frame, until the
// state word it latched on entry changes.
internal static class FrameLoop
{
    // GHIDRA: DRAWENV_800a67c0 @ 0x800A67C0
    internal static readonly DRAWENV DRAWENV_800a67c0 = new();

    // GHIDRA: DISPENV_800a681c @ 0x800A681C
    private static readonly DISPENV DISPENV_800a681c = new();

    // GHIDRA: OT_800a6830 @ 0x800A6830
    // The 0x800-entry ordering table. It sits at DRAWENV_800a67c0 + 0x70, which is how the original
    // reaches it when submitting: DrawOTag(DAT_800834e0 + 0x70).
    private static readonly OT_TYPE OT_800a6830 = new();

    // GHIDRA: DAT_800834e0 @ 0x800834E0
    // Points at the active DRAWENV. Only ever set to DRAWENV_800a67c0 here.
    private static DRAWENV DAT_800834e0;

    // GHIDRA: DAT_800835b4 @ 0x800835B4
    // Screen state. The loop latches it on entry and runs until it differs, so writing it is how
    // the title screen asks to leave.
    internal static int DAT_800835b4;

    // GHIDRA: DAT_80083504 @ 0x80083504
    // Frames elapsed on this screen. Past 0x960, that is 2400 frames or about 40 seconds, the
    // attract mode fires.
    internal static int DAT_80083504;

    // GHIDRA: DAT_80083474 @ 0x80083474
    private static int DAT_80083474;

    // GHIDRA: DAT_800835a0 @ 0x800835A0
    // Frame cost in VSync units, the two waits either side of the draw.
    private static int DAT_800835a0;

    // GHIDRA: RunFrameLoop @ 0x800587A8
    internal static void RunFrameLoop()
    {
        SetDispMask(1);
        int iVar2 = DAT_800835b4;
        bool bVar1 = false;
        DAT_800834e0 = DRAWENV_800a67c0;

        do
        {
            VSync(3);
            bVar1 = !bVar1;
            int iVar5;
            if (bVar1)
            {
                SetDefDrawEnv(DRAWENV_800a67c0, 0, 0xf0, 0x140, 0xf0);
                iVar5 = 0;
            }
            else
            {
                SetDefDrawEnv(DRAWENV_800a67c0, 0, 0, 0x140, 0xf0);
                iVar5 = 0xf0;
            }

            SetDefDispEnv(DISPENV_800a681c, 0, iVar5, 0x140, 0xf0);
            DRAWENV_800a67c0.dtd = 0;
            DRAWENV_800a67c0.isbg = 1;
            DRAWENV_800a67c0.r0 = (byte)TITLE_EXE_exe.DAT_80083450;
            DRAWENV_800a67c0.g0 = (byte)TITLE_EXE_exe.DAT_8008344c;
            DRAWENV_800a67c0.b0 = (byte)TITLE_EXE_exe.DAT_80083448;
            PutDispEnv(DISPENV_800a681c);
            PutDrawEnv(DRAWENV_800a67c0);
            PadInput.ProcessPadInput(0);

            if ((PadInput.DAT_800834fc[0] & 0x800) != 0 && DAT_800835b4 == 2)
            {
                DAT_80083504 = 0x12c1;
            }

            TaskSystem.ExecuteTaskList(0x14);
            ClearOTag(OT_800a6830, 0x800);
            iVar5 = VSync(1);
            TaskSystem.ExecuteTaskList(0);
            TaskSystem.ExecuteTaskList(1);
            TaskSystem.ExecuteTaskList(2);
            TaskSystem.ExecuteTaskList(3);
            TaskSystem.ExecuteTaskList(4);
            TaskSystem.ExecuteTaskList(5);
            TaskSystem.ExecuteTaskList(6);
            TaskSystem.ExecuteTaskList(7);
            TaskSystem.ExecuteTaskList(8);
            TaskSystem.ExecuteTaskList(9);
            TaskSystem.ExecuteTaskList(10);
            TaskSystem.ExecuteTaskList(0xb);
            TaskSystem.ExecuteTaskList(0xc);
            TaskSystem.ExecuteTaskList(0xd);
            TaskSystem.ExecuteTaskList(0xe);
            TaskSystem.ExecuteTaskList(0xf);
            TaskSystem.ExecuteTaskList(0x10);
            TaskSystem.ExecuteTaskList(0x11);
            TaskSystem.ExecuteTaskList(0x12);
            TaskSystem.ExecuteTaskList(0x13);
            DAT_80083504 = DAT_80083504 + 1;

            if (0x960 < DAT_80083504)
            {
                DisplayMachine.FUN_80038228(3, 0x10);
                int iVar3b = DisplayMachine.FUN_80038228(9, 0);
                if (iVar3b == 0)
                {
                    FUN_80056b30();
                    FUN_80056d00();
                    string exeFileName;
                    if (DAT_80083504 < 0x12c1)
                    {
                        exeFileName = "cdrom:\\MOVIE.EXE;1";
                    }
                    else
                    {
                        DAT_800835b4 = 1;
                        exeFileName = "cdrom:\\TITLE.EXE;1";
                    }

                    ShutdownAndLoadExecutable(exeFileName);
                }
            }

            int iVar3 = VSync(1);
            int iVar4 = VSync(1);

            // The original submits DAT_800834e0 + 0x70, which is exactly OT_800a6830: the ordering
            // table sits right behind the DRAWENV. C# cannot add 0x70 to an object reference, so
            // the table is named directly.
            DrawOTag(OT_800a6830);
            DrawSync(0);
            DAT_80083474 = VSync(1);
            DAT_800835a0 = (iVar3 - iVar5) + (DAT_80083474 - iVar4);
        } while (DAT_800835b4 == iVar2);
    }

    // GHIDRA: ShutdownAndLoadExecutable @ 0x80058158
    // Same role as its counterparts in the two other overlays, with two real differences: ResetGraph
    // runs before PadStop, the reverse of theirs, and CdFlush is not called at all.
    private static void ShutdownAndLoadExecutable(string exeFileName)
    {
        StopRCnt(unchecked((long)0xf2000000));
        StopRCnt(unchecked((long)0xf2000001));
        StopRCnt(unchecked((long)0xf2000002));
        StopRCnt(unchecked((long)0xf2000003));
        ResetGraph(0);
        PadStop();
        StopCallback();
        _96_init();
        LoadExec(exeFileName, DAT_801fff00, 0);
    }

    // GHIDRA: DAT_801fff00 @ 0x801FFF00
    // PARTIAL: only the address of this global reaches LoadExec; its contents are never read here.
    private const int DAT_801fff00 = unchecked((int)0x801FFF00);

    // GHIDRA: _96_init @ 0x80070D84
    private static void _96_init()
    {
        // PARTIAL: compiler overlay runtime initialization is provided by the CLR.
    }

    // GHIDRA: LoadExec @ 0x80070DB4
    // PARTIAL: the Ghidra prototype is void LoadExec(char *, u_long, u_long). The semantics of the
    // two stack arguments are not closed, so they keep raw names. No overlay is wired behind this
    // call site yet: TITLE.EXE hands over to MOVIE.EXE, SELECT.EXE, GAME.EXE and others, none of
    // which is transliterated.
    private static void LoadExec(string exeFileName, int param_2, int param_3)
    {
        WaitDiscLoad(exeFileName);

        // JUSTIFICATION: PSX hardware adaptation only
        // RELATION: A0(0x51) replaces the resident executable and transfers control permanently, so
        // it never returns to its caller.
        throw new LoadExecTransferException();
    }

    // GHIDRA: FUN_80056b30 @ 0x80056B30
    private static void FUN_80056b30()
    {
        // BLOCKED: shuts down the sound sequencer — 24 voices through FUN_800620e8, 6 channels
        // through FUN_80061c88, then FUN_80062334, FUN_80062760, FUN_800627f8, FUN_80062838,
        // FUN_8006268c, FUN_80068a2c and FUN_8006871c. Every one of those lives in the libsnd/libspu
        // range above 0x80059160 and none is closed. The same subsystem is already BLOCKED in
        // SLPS_003.55 (FUN_8002c57c). Nothing is torn down here because nothing was started.
    }

    // GHIDRA: FUN_80056d00 @ 0x80056D00
    private static void FUN_80056d00()
    {
        // PARTIAL: the original silences the CD mix through CdMix with all four volumes at zero,
        // then calls SpuStQuit and five further libsnd routines that are not closed. Only the CD
        // mix is reproducible today, and muting it here would be the sole observable effect.
        // BLOCKED: FUN_80062334, FUN_80064a78, FUN_80068f60, FUN_80067ae8, FUN_80063f6c,
        // FUN_80064010.
    }
}
