using System;
using PsxSdkMonogame;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.LibGte;
using static PsxSdkMonogame.Kernel;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE — the versus battle overlay, reached from SELECT.EXE's mode menu by
// LoadExec("cdrom:\\VS.EXE;1"). This file carries its two entry points: the SN Systems crt0 `start`
// and `main`, which is the frame loop.
//
// THE ENGINE IS TITLE.EXE'S, NOT SELECT.EXE'S, and that was measured rather than assumed —
// docs/tasks/VS_EXE_RECON.md carries the evidence. Same 21-list task scheduler, same direct libgpu
// with not one libgs call in the program, same 0x800-entry ordering table reached as env + 0x70.
// TITLE_EXE/FrameLoop.cs's RunFrameLoop @ 0x800587A8 is the same source compiled: its loop body and
// the one below match statement for statement.
//
// That is why this file does not read like SELECT_EXE_exe.cs, which had no scheduler at all.
//
// WHY THERE IS A SEPARATE VS_EXE FOLDER AT ALL, given the instruction not to duplicate what the
// overlays share. The SDK is shared and is not duplicated: every libgpu, libgte, libcd, libetc and
// libapi call below goes to PsxSdkMonogame, and SharedHighRam models the cross-overlay block once.
// But TITLE.EXE and VS.EXE are two separately linked programs. Their globals sit at different
// addresses, and every ported function carries its own `// GHIDRA:` annotation naming its own
// address. Folding the two into one implementation is precisely what rule 3 of the mandate forbids
// — "ne pas fusionner plusieurs fonctions originales dans une API C# plus propre" — and it would
// make both annotations false. So the game code is transliterated per overlay, and the shared
// layers are shared.
internal sealed class VS_EXE_exe
{
    // =====================================================================================
    // .bss — the frame's drawing environments and the ordering table
    // =====================================================================================

    // GHIDRA: DAT_800b0eb8 @ 0x800B0EB8 (VS.EXE)
    // The DRAWENV. main writes its dtd/isbg/background colour by hand every frame at +0x16, +0x18,
    // +0x19, +0x1a and +0x1b — the fields Ghidra spells DAT_800b0ece..DAT_800b0ed3.
    internal static readonly DRAWENV DRAWENV_800b0eb8 = new();

    // GHIDRA: DAT_800b0f14 @ 0x800B0F14 (VS.EXE)
    // The DISPENV. 0x800B0EB8 + 0x5C, immediately behind the DRAWENV, exactly as in TITLE.EXE.
    private static readonly DISPENV DISPENV_800b0f14 = new();

    // GHIDRA: DAT_800b0eb8 @ 0x800B0EB8 (VS.EXE) — its PSX address, not the object.
    // main stores it into DAT_8008d420 and then submits `DrawOTag(DAT_8008d420 + 0x70)`, so the
    // address has to exist as a number.
    private const int Drawenv800b0eb8Address = unchecked((int)0x800B0EB8);

    // GHIDRA: DAT_800b0f28 @ 0x800B0F28 (VS.EXE)
    // THE ORDERING TABLE, 0x800 entries. 0x800B0EB8 + 0x70 = 0x800B0F28, which is how the original
    // reaches it when submitting — it never names the table at the call, only the environment.
    private const int Ot800b0f28Address = unchecked((int)0x800B0F28);

    // A byte[] rather than a uint[]: the ordering table is raw PSX memory the rasterizer walks
    // by address, and TITLE_EXE/FrameLoop.cs already models it that way.
    internal static readonly byte[] OT_800b0f28 = new byte[0x800 * 4];

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: the ordering table is submitted BY ADDRESS (DrawOTag), so the rasterizer has to be
    // able to resolve 0x800B0F28 back to this array. Same treatment as TITLE_EXE/FrameLoop.cs.
    private static void DeclareOrderingTableAddress()
    {
        RamRegion(Ot800b0f28Address, OT_800b0f28);
    }

    // =====================================================================================
    // .sbss / .bss scalars main touches
    // =====================================================================================

    // GHIDRA: DAT_8008d420 @ 0x8008D420 (VS.EXE)
    // Holds the active DRAWENV's ADDRESS, not the object: the original adds 0x70 to it to reach
    // the ordering table.
    private static int DAT_8008d420;

    // GHIDRA: DAT_8008d444 @ 0x8008D444 (VS.EXE)
    // The frame counter, saturating rather than wrapping: main only increments it while it is
    // below 0x7fffffff. Rule 12 — the saturation is the original's and is kept.
    internal static int DAT_8008d444;

    // GHIDRA: DAT_8008d4dc @ 0x8008D4DC (VS.EXE)
    // A CPU-load measure, built from four VSync(1) readings across the frame. Nothing in this
    // slice reads it back.
    private static int DAT_8008d4dc;

    // GHIDRA: DAT_8008d3b4 @ 0x8008D3B4 (VS.EXE)
    private static int DAT_8008d3b4;

    // GHIDRA: DAT_8008d3d8 @ 0x8008D3D8 (VS.EXE)
    // FntOpen's returned stream id. PARTIAL: FntOpen is called once and FntFlush has NO call site
    // anywhere in the program, so the debug font is opened and never drained. Reproduced, not
    // corrected.
    private static int DAT_8008d3d8;

    // GHIDRA: DAT_8008d38c @ 0x8008D38C, DAT_8008d390 @ 0x8008D390, DAT_8008d394 @ 0x8008D394 (VS.EXE)
    // The background colour main copies into the DRAWENV every frame, blue first.
    private static int DAT_8008d38c;

    private static int DAT_8008d390;

    private static int DAT_8008d394;

    // GHIDRA: DAT_8008d334 @ 0x8008D334 (VS.EXE)
    // The CLUT id of the all-white 256-entry palette main builds on the stack and uploads to
    // VRAM (0, 500).
    private static int DAT_8008d334;

    // GHIDRA: DAT_8008d4f0 @ 0x8008D4F0 (VS.EXE)
    private static int DAT_8008d4f0;

    // GHIDRA: DAT_800858bc @ 0x800858BC, DAT_800858c0 @ 0x800858C0 (VS.EXE)
    // crt0's heap base and size, computed from _end.
    //
    // A CORRECTION WORTH KEEPING, because it was got wrong twice. One reconnaissance surface
    // reported that VS.EXE links no heap at all — neither InitHeap nor malloc — and I repeated it.
    // Both readings inferred from ABSENT GHIDRA MARKUP rather than from bytes. FUN_80062F54 is
    // PSYQ's InitHeap: 64 bytes, zero callees, and statement for statement identical to TITLE.EXE's
    // InitHeap @ 0x80059160, with only the global addresses relocated. Its siblings FUN_80062F94
    // and FUN_800631C8 are malloc and free on the same evidence. A missing name is not a missing
    // function.
    private static int DAT_800858bc;

    private static uint DAT_800858c0;

    // GHIDRA: DAT_8008d300 @ 0x8008D300 (VS.EXE)
    private static int DAT_8008d300;

    // GHIDRA: DAT_800c3dd4 @ 0x800C3DD4 (VS.EXE)
    // `_end`, and the boundary the crt0's own BSS clear stops at. It is also what proves Ghidra's
    // second ".text" block is mislabelled: everything past this address is zero fill plus a 16 KB
    // fixed data section, with zero functions in it. See docs/tasks/VS_EXE_RECON.md.
    private const int BssClearLimit = unchecked((int)0x800C3DD4);

    // GHIDRA: DAT_8008d254 @ 0x8008D254 (VS.EXE)
    // The low end of the crt0's BSS clear — the start of .sbss.
    private const int BssClearFirst = unchecked((int)0x8008D254);

    // =====================================================================================
    // start and main
    // =====================================================================================

    // GHIDRA: start @ 0x80072F50 (VS.EXE)
    // Ghidra plates it "Possible SNMAIN.OBJ/__SN_ENTRY_POINT", and it is the same crt0 as
    // TITLE.EXE's @ 0x80068FF4 and SELECT.EXE's @ 0x800347C4: same word-at-a-time zero loop, same
    // heap formula, same trap(1) tail.
    //
    // The tail past trap(1) — the stores through the saved stack pointer and the destructor walk
    // guarded by `if (false)` — is UNREACHABLE. `break 0x1` raises a breakpoint exception and never
    // falls through. It is not transliterated, and not because it is untidy: there is no path to it.
    public void start()
    {
        // The bss clear: 0x8008D254 up to _end at 0x800C3DD4, one word at a time — 0x36B80 bytes.
        //
        // PARTIAL: this port models .sbss/.bss as C# statics and as byte[] regions, all of which
        // the CLR zero-initialises before first use, so the loop has nothing left to zero. The
        // range is spelled out above rather than implied, the same treatment __main and the other
        // overlays' start functions get.
        _ = BssClearFirst;

        DAT_800858c0 = ((DAT_800858e0 - 8U) - DAT_800858dc) - 0xc3dd4;
        DAT_800858bc = BssClearLimit;
        DAT_8008d300 = 0;

        // The decompiler prints this call with ONE argument and main's with TWO, which looked like
        // an open question until the disassembly settled it: at 0x80072FD8 the jal is preceded by
        // `sw a1, ...` and carries `addi a0, a0, 4` in its delay slot, so start passes both — the
        // heap base at _end + 4 and the size it just computed. Two arguments, like main's.
        Heap.FUN_80062f54(unchecked((int)0x800C3DD8), (int)DAT_800858c0);

        main();

        // trap(1) — `break 0x1`. Control never returns from it on the console.
        // PARTIAL: modelled as a plain return, because the desktop host owns thread teardown and
        // main below cannot exit anyway.
    }

    // GHIDRA: DAT_800858e0 @ 0x800858E0, DAT_800858dc @ 0x800858DC (VS.EXE)
    // The two crt0 words the heap formula reads. PARTIAL: nothing in this slice writes them.
    private static uint DAT_800858e0;

    private static uint DAT_800858dc;

    // GHIDRA: main @ 0x80062134 (VS.EXE)
    // 1320 bytes, 42 distinct callees, single caller `start`.
    //
    // It is NOT a state switch like SELECT.EXE's main. It is linear initialisation followed by an
    // INFINITE FRAME LOOP WITH NO EXIT — no break, no return. The only way out of the overlay is
    // FUN_800620B0, the LoadExec path, called from inside a task.
    //
    // The frame order matters and is preserved exactly: pad, then task list 20, then ClearOTag,
    // then lists 0 through 19, then FUN_80062B5C, then the submit. The recon closed what that
    // ordering buys — list 9 is the battle manager, list 10 the six fighters, list 12 the scene and
    // its rendering — so every fighter has moved before anything is drawn, within one frame.
    private static void main()
    {
        __main();

        // GHIDRA: DAT_1f80012c @ 0x1F80012C — a scratchpad word. The scratchpad is reused heavily
        // by this game; this one is cleared here and not read in this slice.
        DAT_1f80012c = 0;

        int iVar7 = 1;

        // FUN_8007A940 / FUN_8007AC10 are EnterCriticalSection / ExitCriticalSection — the recon
        // closed both by their 9 and 10 call sites and their libapi stub form. They bracket the
        // whole of initialisation.
        EnterCriticalSection();
        ResetCallback();
        ResetGraph(0);
        InitGeom();
        SetDispMask(0);
        FileIo.ClearVram();
        PadInit(0);
        CdInit();

        uint uVar5 = 0x10000;
        Heap.FUN_80062f54(0x10000, 0x10000);
        srand(uVar5);
        ExitCriticalSection();

        FntLoad(0x3c0, 0x100);
        DAT_8008d3d8 = FntOpen(0x10, 0x10, 0x100, 200, 0, 0x200);
        DAT_8008d38c = 200;
        DAT_8008d390 = 0;
        DAT_8008d394 = 0;

        FileIo.SetupGeometry(0xa0, 0xef, 0x200, 0, 0, 0, 0x400, 0, 0, 0);
        FUN_80042054(8, 0);
        DAT_8008d4f0 = 1;
        FUN_80062a1c();

        // The all-white 256-entry CLUT: local_220 stays 0 and the other 255 halfwords are 0x8000 —
        // black with the semi-transparency bit set, then white. LoadClut uploads it to VRAM
        // (0, 500).
        byte[] local_220 = new byte[0x200];
        MipsMemory.WriteU16(local_220, 0, 0);
        int puVar4 = 2;
        int iVar1 = 1;
        do
        {
            MipsMemory.WriteU16(local_220, puVar4, 0x8000);
            iVar1 = iVar1 + 1;
            puVar4 = puVar4 + 2;
        }
        while (iVar1 < 0x100);

        DAT_8008d334 = LoadClut(ToWordBuffer(local_220, 0x200), 0, 500);

        bool bVar1 = false;

        // The four boot tasks, in order. The entry arguments are code addresses the original passes
        // as function pointers; two of them are LAB_ labels rather than recognised functions, which
        // is why they keep their raw spelling.
        // The insert points are ELEMENTS of the three tables, not separate globals. The arithmetic
        // settles it and the four calls agree 4/4: 0x80083B3C + i*4 is list i's head, 0x80083B90 +
        // i*4 its tail. main inserts at the head for lists 0 and 0x13, at the tail for 9 and 0x14.
        TaskSystem.CreateTask(Lab8005d1fcAddress, 0x57, 0x14, 0x194, 0, TaskSystem.g_TaskListTail[20]);
        TaskSystem.CreateTask(Fun800411b4Address, 0x58, 0, 0, 0, TaskSystem.g_TaskListHead[0]);
        FUN_800411b4();
        TaskSystem.CreateTask(Lab80027670Address, 0x55, 0x13, 0, 0, TaskSystem.g_TaskListHead[19]);

        PrimitivePools.CreatePrimitivePools(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0);
        FUN_80062684();

        FileIo.ReadFile("\\CHR_DATA\\EFF_AUTO.B;1".ToCharArray(), FileIo.g_cdFileBufferTableAddress, 0);
        FileIo.DecompressAndLoadImage(DAT_801d20a0, 0x280, 0, 0x40, 0x100, 0);
        FileIo.DecompressAndLoadImage(DAT_801d555c, 0x280, 0x100, 0x40, 0x100, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(FileIo.g_cdFileBufferTableAddress, 0, 0x1e0, 0x50, 1, 1);

        FileIo.ReadFile("\\CHR_DATA\\CH_EF_P0.B;1".ToCharArray(), FileIo.g_cdFileBufferTableAddress, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(FileIo.g_cdFileBufferTableAddress, 0, 0x1e3, 0x130, 1, 1);

        uVar5 = (uint)rand();
        FUN_800414ec(uVar5 & 7);

        // The battle context task: 0x3034 bytes of workspace on list 9, id 0x51. Its workspace
        // pointer is read straight back out of the task node at +8 and handed to FUN_800511A8,
        // which is what fills the twelve-slot fighter array at context + 0x1520.
        int iVar7Task = TaskSystem.CreateTask(Lab80055e3cAddress, 0x51, 9, 0x3034, 0, TaskSystem.g_TaskListTail[9]);

        // `uVar6 = *(undefined4 *)(iVar7 + 8)` — main reads the field INLINE. There is no accessor
        // in the original, and inventing one here is exactly what rule 15 forbids. The sibling slice
        // refused to write it, and it was right to.
        int uVar6 = PsxRam.ReadI32(iVar7Task + 8);

        FUN_8005cbe0();
        FUN_80034d98();
        FUN_800511a8(uVar6);
        FUN_80026a68();

        DAT_8008d444 = 0;
        FUN_80042054(2, 4);

        DeclareOrderingTableAddress();
        DAT_8008d420 = Drawenv800b0eb8Address;

        do
        {
            VSync(3);
            bVar1 = !bVar1;
            if (bVar1)
            {
                SetDefDrawEnv(DRAWENV_800b0eb8, 0, 0xf0, 0x140, 0xf0);
                iVar7 = 0;
            }
            else
            {
                SetDefDrawEnv(DRAWENV_800b0eb8, 0, 0, 0x140, 0xf0);
                iVar7 = 0xf0;
            }

            SetDefDispEnv(DISPENV_800b0f14, 0, iVar7, 0x140, 0xf0);

            // DAT_800b0ece..DAT_800b0ed3 are DRAWENV fields at +0x16, +0x18, +0x19, +0x1a, +0x1b.
            DRAWENV_800b0eb8.dtd = 0;
            DRAWENV_800b0eb8.isbg = 1;
            DRAWENV_800b0eb8.r0 = (byte)DAT_8008d394;
            DRAWENV_800b0eb8.g0 = (byte)DAT_8008d390;
            DRAWENV_800b0eb8.b0 = (byte)DAT_8008d38c;

            PutDispEnv(DISPENV_800b0f14);
            PutDrawEnv(DRAWENV_800b0eb8);

            PadInput.ProcessPadInput(0);
            TaskSystem.ExecuteTaskList(0x14);
            ClearOTag(OT_800b0f28, 0, 0x800);
            iVar7 = VSync(1);

            for (int list = 0; list < 0x14; list++)
            {
                TaskSystem.ExecuteTaskList((ushort)list);
            }

            FUN_80062b5c();

            if (DAT_8008d444 < 0x7fffffff)
            {
                DAT_8008d444 = DAT_8008d444 + 1;
            }

            int iVar2 = VSync(1);
            int iVar3 = VSync(1);
            DrawOTag(DAT_8008d420 + 0x70);
            DrawSync(0);
            DAT_8008d3b4 = VSync(1);
            DAT_8008d4dc = (iVar2 - iVar7) + (DAT_8008d3b4 - iVar3);
        }
        while (true);
    }

    // GHIDRA: DAT_1f80012c @ 0x1F80012C (VS.EXE)
    private static int DAT_1f80012c;

    // GHIDRA: __main @ 0x80072FF8 (VS.EXE)
    // PARTIAL: the SN Systems C++ static-constructor walk. Every overlay in this port models it the
    // same way — the CLR runs static initialisers itself, and this game links no C++ constructors.
    private static void __main()
    {
    }

    // =====================================================================================
    // NOT IN THIS SLICE — tranche 0 covers the foundations only
    // =====================================================================================
    // Every stub below is a real function of VS.EXE that main calls and that belongs to a later
    // slice. They are declared here, with their address and what is known of them, rather than
    // silently omitted: main's shape is the deliverable of this slice, and a main that quietly
    // skipped half its calls would not be it.

    // The four task entries are handed to CreateTask as RAW PSX ADDRESSES, and TaskSystem stores
    // them verbatim in the node at +0x04 — so a node built by this port compares byte for byte with
    // one read out of PCSX-Redux. None is registered, because none is transliterated yet: the
    // scheduler skips an address it has no callback for, which means the blocks, the ids, the list
    // membership and the context sizes are already right while the bodies are not.

    // GHIDRA: LAB_8005d1fc @ 0x8005D1FC (VS.EXE)
    // BLOCKED: a task entry point Ghidra never promoted to a function. Task id 0x57, list 0x14,
    // 0x194 bytes of workspace — the list main runs FIRST each frame, before ClearOTag.
    private const int Lab8005d1fcAddress = unchecked((int)0x8005D1FC);

    // GHIDRA: FUN_800411b4 @ 0x800411B4 (VS.EXE)
    // BLOCKED: task id 0x58 on list 0, and main also calls it once directly, immediately after
    // creating it.
    private const int Fun800411b4Address = unchecked((int)0x800411B4);

    private static void FUN_800411b4()
    {
    }

    // GHIDRA: LAB_80027670 @ 0x80027670 (VS.EXE)
    // BLOCKED: task id 0x55, list 0x13.
    private const int Lab80027670Address = unchecked((int)0x80027670);

    // GHIDRA: LAB_80055e3c @ 0x80055E3C (VS.EXE)
    // BLOCKED: THE BATTLE MANAGER. Task id 0x51, list 9, 0x3034 bytes of workspace — the battle
    // context whose layout the recon closed: central gauge at +0x302C bounded to +/-30000, ki gauge
    // at +0x15B4 capped at 16000, per-slot 0x14-byte records at +0x15B0, target index at +0x15C0,
    // and the twelve-slot fighter array at +0x1520.
    private const int Lab80055e3cAddress = unchecked((int)0x80055E3C);

    // GHIDRA: FUN_80042054 @ 0x80042054 (VS.EXE)
    // BLOCKED: called twice by main with (8, 0) then (2, 4).
    private static void FUN_80042054(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_80062a1c @ 0x80062A1C (VS.EXE)
    // BLOCKED: part of graphics bring-up, called once between SetupGeometry and the CLUT upload.
    private static void FUN_80062a1c()
    {
    }

    // GHIDRA: FUN_80062684 @ 0x80062684 (VS.EXE)
    // BLOCKED: the loading screen. It sorts into OT[0x400] at 0x800B1F28.
    private static void FUN_80062684()
    {
    }

    // GHIDRA: FUN_800414ec @ 0x800414EC (VS.EXE)
    // BLOCKED: fed `rand() & 7` — one of eight variants chosen at boot. Which is not established.
    private static void FUN_800414ec(uint param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8005cbe0 @ 0x8005CBE0 (VS.EXE)
    // BLOCKED: THE ROSTER CONSUMER, and the only one. It reads the six character ids SELECT.EXE
    // exports at 0x801FF102-10C — reads at 0x8005CC38 and 0x8005CC68 — and writes them into
    // 0x80083CF0. Character ids run 1..38, closed twice independently by FACE.B's 76 sectors at two
    // per portrait and by the 38-entry AT table.
    private static void FUN_8005cbe0()
    {
    }

    // GHIDRA: FUN_80034d98 @ 0x80034D98 (VS.EXE)
    // BLOCKED.
    private static void FUN_80034d98()
    {
    }

    // GHIDRA: FUN_800511a8 @ 0x800511A8 (VS.EXE)
    // BLOCKED: it fills the twelve-slot fighter array at battleContext + 0x1520. Six fighters are
    // created, in slots 0/1/2 and 6/7/8 — the two teams of a three-on-three.
    private static void FUN_800511a8(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_80026a68 @ 0x80026A68 (VS.EXE)
    // BLOCKED.
    private static void FUN_80026a68()
    {
    }

    // GHIDRA: FUN_80062b5c @ 0x80062B5C (VS.EXE)
    // BLOCKED: run every frame between the last task list and the submit. It sorts into OT[0x7FF]
    // at 0x800B2F24 — the very back of the table, so whatever it draws is behind everything.
    private static void FUN_80062b5c()
    {
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: LoadClut takes a u_long* in the original and main hands it a 256-halfword local by
    // cast. LibGpu.LoadClut takes ulong[], so the bytes are repacked. Each of the three TITLE_EXE
    // files that needs this bridge declares its own; this follows that convention rather than
    // inventing a shared helper.
    private static ulong[] ToWordBuffer(byte[] source, int byteCount)
    {
        ulong[] words = new ulong[(byteCount + 7) / 8];
        for (int i = 0; i < byteCount; i++)
        {
            words[i / 8] |= (ulong)source[i] << ((i % 8) * 8);
        }

        return words;
    }

    // GHIDRA: DAT_801d20a0 @ 0x801D20A0, DAT_801d555c @ 0x801D555C (VS.EXE)
    // PARTIAL: two addresses inside the CD read buffer that main hands to DecompressAndLoadImage
    // after loading EFF_AUTO.B. They are offsets into that buffer, not independent objects.
    private static readonly int DAT_801d20a0 = unchecked((int)0x801D20A0);

    private static readonly int DAT_801d555c = unchecked((int)0x801D555C);
}
