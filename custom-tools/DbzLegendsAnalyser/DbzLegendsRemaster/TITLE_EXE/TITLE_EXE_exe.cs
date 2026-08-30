using PsxSdkMonogame;
using static PsxSdkMonogame.Kernel;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.LibGte;

namespace DbzLegendsRemaster.TITLE_EXE;

internal sealed class TITLE_EXE_exe
{
    private const int TitleBBufferAddress = unchecked((int)0x80110000);
    private const int HeapBaseAddress = 0x00010000;

    // GHIDRA: FUN_80037388 @ 0x80037388
    // Its own PSX address, kept so a task block holds exactly what the console holds.
    private const int FUN_80037388_Address = unchecked((int)0x80037388);

    // GHIDRA: BYTE_ARRAY_801d2000 @ 0x801D2000
    // Its PSX address. The buffer is LoadingScreen.BYTE_ARRAY_801d2000; FUN_80058a9c hands this raw
    // address to ReadFile and reaches two offsets inside it through FUN_80057b08.
    private const int ByteArray801d2000Address = unchecked((int)0x801D2000);

    // GHIDRA: LAB_80027f5c @ 0x80027F5C
    // The callback of FUN_80058a9c's second CreateTask, stored and never called here.
    //
    // BLOCKED: Ghidra has no function boundary at this address, so there is no closed size. Two
    // probes bound the body to [0x10A4, 0x1B90] bytes: a decompiler request at 0x80029000 resolves
    // back to the same undefined function at 0x80027F5C, and the next DEFINED function is
    // FUN_80029aec @ 0x80029AEC. Not registered with TaskSystem.
    private const int LAB_80027f5c_Address = unchecked((int)0x80027F5C);

    // GHIDRA: LAB_800532a4 @ 0x800532A4
    // The callback of FUN_80058a9c's third CreateTask, stored and never called here.
    //
    // BLOCKED, and it must stay blocked: the body is 96 bytes (0x800532A4..0x80053303) that read
    // the task context+0x10E as a short and dispatch 0 -> FUN_80053304 @ 0x80053304 and
    // 1 -> FUN_80053B20 @ 0x80053B20. FUN_80053304 is the libsnd/libspu path, and the C# SDK
    // declares the whole Ss* surface with every body empty while the SpuSt* streaming calls are
    // stubs. Not registered with TaskSystem.
    private const int LAB_800532a4_Address = unchecked((int)0x800532A4);

    // GHIDRA: LAB_8004c010 @ 0x8004C010
    // The callback of FUN_80058a9c's fourth CreateTask, stored and never called here.
    //
    // BLOCKED: the body is 164 bytes (0x8004C010..0x8004C0B3) and is a four-way dispatcher on
    // **(context + 8) read as a ushort — 0 -> FUN_8004C0B4, 1 -> FUN_8004C168, 2 -> FUN_8004DA4C,
    // 3 -> FUN_8004DBAC. The dispatcher itself would be trivial; none of its four arms is
    // transliterated, so registering it would dispatch into nothing. Not registered with TaskSystem.
    private const int LAB_8004c010_Address = unchecked((int)0x8004C010);

    // GHIDRA: DAT_80110000 @ 0x80110000
    // Destination of ReadFile("\\SUB\\TITLE.B;1", ...). The file is 0x25000 bytes, the size
    // docs/TITLE_B_FILE_FORMAT_ANALYSIS.md measured independently.
    internal static readonly byte[] DAT_80110000 = new byte[0x25000];

    // GHIDRA: CdlFILE_800a8860 @ 0x800A8860
    internal static readonly CdlFILE CdlFILE_800a8860 = new();

    // GHIDRA: DAT_80083498 @ 0x80083498
    private static int DAT_80083498;

    // GHIDRA: DAT_8008344c @ 0x8008344C
    internal static int DAT_8008344c;

    // GHIDRA: DAT_80083450 @ 0x80083450
    internal static int DAT_80083450;

    // GHIDRA: DAT_80083448 @ 0x80083448
    internal static int DAT_80083448;

    // GHIDRA: DAT_80083544 @ 0x80083544
    internal static int DAT_80083544;

    // GHIDRA: DAT_800833f0 @ 0x800833F0
    // Sixteen bits: the store at 0x80058C40 is `sh v0,0x23C(gp)`, not a `sw`, and Ghidra types the
    // label undefined2. It holds the CLUT id LoadClut returns. find-cross-references reports exactly
    // ONE reference in the whole overlay, that write — nothing in TITLE.EXE reads it back.
    internal static ushort DAT_800833f0;

    // GHIDRA: DAT_80083454 @ 0x80083454
    // State word of the display/fade machine FUN_80038228 @ 0x80038228, still open. Read here
    // because FUN_80037388 gates its AddPrim on it.
    internal static int DAT_80083454;

    // GHIDRA: main @ 0x800581DC
    public void Main()
    {
        __main();
        EnterCriticalSection();
        ResetCallback();
        ResetGraph(0);
        InitGeom();
        SetDispMask(0);
        ClearVram();
        PadInit(0);
        CdInit();

        CdlFILE pCVar1;
        do
        {
            pCVar1 = CdSearchFile(CdlFILE_800a8860, "\\SELECT.EXE;1".ToCharArray());
        } while (pCVar1 == null);

        ReadFile("\\SUB\\TITLE.B;1".ToCharArray(), TitleBBufferAddress, 0);
        uint uVar2 = 0x10000;
        InitHeap(HeapBaseAddress, 0x10000);
        srand(uVar2);
        ExitCriticalSection();
        FntLoad(0x3c0, 0x100);
        DAT_80083498 = FntOpen(0x10, 0x10, 0x100, 200, 0, 0x200);
        DAT_8008344c = 0;
        DAT_80083450 = 0;
        DAT_80083448 = 0;
        SetupGeometry(0xa8, 0x80, 0x1000, 0, 0, 0, 0x1000, 0, 0, 0);
        TaskSystem.RegisterCallback(FUN_80037388_Address, FUN_80037388);
        TaskSystem.CreateTask(FUN_80037388_Address, 0, 0, 0, 0, TaskSystem.g_TaskListHead[0]);
        FUN_80037388();
        PrimitivePools.FUN_80056dc0(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0);
        DAT_80083544 = 0;
        DisplayMachine.FUN_80038228(8, 0);
        FUN_80058d64();

        do
        {
            if (2 < SharedHighRam.SHORT_ARRAY_801ff000[0x87])
            {
                SharedHighRam.SHORT_ARRAY_801ff000[0x87] = 0;
            }

            GteScratch.DAT_1f80012c = (uint)SharedHighRam.SHORT_ARRAY_801ff000[0x87];
            SharedHighRam.SHORT_ARRAY_801ff000[0x80] = 2;
            SharedHighRam.SHORT_ARRAY_801ff000[0x87] =
                (short)(SharedHighRam.SHORT_ARRAY_801ff000[0x87] + 1);
            DisplayMachine.FUN_80038228(8, 0);
            FrameLoop.DAT_800835b4 = 1;
            TitleImages.FUN_80021dd0();
            FrameLoop.RunFrameLoop();
            FUN_80058a9c();
            DisplayMachine.FUN_80038228(2, 4);
            FrameLoop.DAT_80083504 = 0;
            FrameLoop.RunFrameLoop();
        } while (true);
    }

    // GHIDRA: FUN_80058a9c @ 0x80058A9C
    // The title-to-select handover, and the only reference to it in the whole overlay is the call
    // at 0x800583BC inside main. It frees the six live primitive pools, destroys the first twenty
    // task lists, re-arms the heap from scratch, re-arms the geometry for the select screen
    // (h = 0x200 against the title's 0x1000), rebuilds the task set, loads two CD files plus a
    // synthetic CLUT, and latches DAT_80083544 so the second and later passes skip one task.
    //
    // THE ORDER OF THE FIRST THREE STEPS IS LOAD-BEARING: six FUN_80057030 frees, then twenty
    // FUN_80049a14 list destructions, then InitHeap. Reversing any pair changes what the heap holds.
    //
    // THE FOUR CreateTask SITES ARE TRANSLITERATED IN FULL — same callback addresses, ids, list
    // indices, context sizes and insert points as the console. TaskSystem stores the raw PSX
    // address in the block and skips a callback address that was never registered, so the blocks,
    // the ids, the list membership and the context sizes are all correct even though three of the
    // four bodies are not transliterated. Each unregistered address carries its own BLOCKED note at
    // its constant above. Only FUN_80037388 is registered, and main already did that.
    //
    // PARTIAL, four items, none of them fixable from inside this file:
    //  1. LoadClut @ 0x80074E50 is `return 0;` in LibGpu, so DAT_800833f0 receives 0 instead of a
    //     CLUT id. Nothing in TITLE.EXE reads DAT_800833f0 back, so nothing here observes it.
    //  2. FUN_800376c0 @ 0x800376C0 is not called: see the BLOCKED note at its place below.
    //  3. LoadFACE_B @ 0x80052D68 is not called: see the BLOCKED note at its place below.
    //  4. A SECOND InitHeap leaks a registry row in the SDK. PsxHeap.InitHeap allocates a NEW
    //     byte[] and hands it to LibGpu.RamRegion, which matches on ReferenceEquals and therefore
    //     ADDS a row rather than updating the old one. RamResolve then sees two rows with the same
    //     base 0x00010000 and its tie-break `base[i] > base[best]` is strict, so the FIRST, stale
    //     row wins: AddPrim would splice links into the abandoned buffer while PsxRam reads the new
    //     one. main calls this function once per outer iteration, so a row leaks per pass and the
    //     fixed 64-row registry stops accepting registrations after about sixty. That is SDK
    //     territory (PsxSdkMonogame/PsxHeap.cs and LibGpu.cs), reported rather than worked around.
    private static void FUN_80058a9c()
    {
        int pvVar1;
        int puVar2;
        uint uVar3;
        uint uVar4;
        int iVar5;
        int uVar6;

        // local_210 and local_20e[255] are one contiguous 0x200-byte block on the original's stack
        // frame (sp+0x28 .. sp+0x227), which is what LoadClut is handed the address of. Modelled as
        // the bytes it is, so the halfword stores below land exactly where the `sh` puts them.
        byte[] local_210 = new byte[0x200];

        uVar4 = 0;
        ClearOTag(FrameLoop.DAT_800834e0 + 0x70, 0x800);
        do
        {
            uVar3 = uVar4 & 0xffff;
            uVar4 = uVar4 + 1;

            // DAT_800835f8 is re-loaded from memory on every iteration in the original
            // (lui/lw at 0x80058AC0), not hoisted: FUN_80057030 can change it.
            PrimitivePools.FUN_80057030(PrimitivePools.DAT_800835f8, uVar3);
        } while ((int)uVar4 < 6);

        uVar4 = 0;
        do
        {
            TaskSystem.FUN_80049a14(uVar4 & 0xffff);
            uVar4 = uVar4 + 1;
        } while ((int)uVar4 < 0x14);

        // Lists 0..0x13 only. Index 0x14 is deliberately NOT destroyed, which is why the audio task
        // created below survives and why DAT_80083544 only has to gate its creation.
        InitHeap(HeapBaseAddress, 0x10000);
        SetupGeometry(0xa0, 0xef, 0x200, 0, 0, 0, 0x400, 0, 0, 0);
        DisplayMachine.FUN_80038228(8, 0);

        // Site 1. main creates the SAME callback in the SAME list with id 0; here the id is 0x58.
        // The ids differ in the original, and 0x58 is reproduced.
        TaskSystem.CreateTask(FUN_80037388_Address, 0x58, 0, 0, 0, TaskSystem.g_TaskListHead[0]);
        FUN_80037388();
        PrimitivePools.FUN_80056dc0(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0);
        LoadingScreen.FUN_800583fc();

        // Site 2. Note the asymmetry with site 3 below: this one is handed g_TaskListHead[0x13]
        // (lw v0,-0x6760 at 0x80058BB4, and 0x800798A0 - 0x80079854 = 19 * 4), while site 3 is
        // handed g_TaskListTail[0x14]. It is real, not a transcription slip.
        TaskSystem.CreateTask(LAB_80027f5c_Address, 0x55, 0x13, 0, 0, TaskSystem.g_TaskListHead[0x13]);

        // `ori s0,zero,1` sits in the DELAY SLOT of the branch that skips the audio task
        // (0x80058BD4), so it runs on BOTH paths and must stay outside the `if`.
        iVar5 = 1;
        if (DAT_80083544 == 0)
        {
            // Site 3, the audio task, created on the first pass only.
            TaskSystem.CreateTask(
                LAB_800532a4_Address, 0x57, 0x14, 0x194, 0, TaskSystem.g_TaskListTail[0x14]);
        }

        // Entry 0 stays 0x0000 and entries 1..255 become 0x8000: 255 stores, s0 running 1..0xFF.
        MipsMemory.WriteU16(local_210, 0, 0);
        puVar2 = 2;
        do
        {
            MipsMemory.WriteU16(local_210, puVar2, 0x8000);
            iVar5 = iVar5 + 1;
            puVar2 = puVar2 + 2;
        } while (iVar5 < 0x100);

        DAT_800833f0 = LoadClut(ToWordBuffer(local_210, 0x200), 0, 500);
        ReadFile("\\CHR_DATA\\EFF_AUTO.B;1".ToCharArray(), ByteArray801d2000Address, 0);

        // The two blocks EFF_AUTO.B carries at +0xA0 and +0x355C, each decoding to a 0x40 x 0x100
        // texture. 0x801D20A0 and 0x801D555C are raw addresses inside BYTE_ARRAY_801d2000, which
        // ResolveAddress now answers for.
        TitleImages.FUN_80057b08(unchecked((int)0x801d20a0), 0x280, 0, 0x40, 0x100, '\0');
        TitleImages.FUN_80057b08(unchecked((int)0x801d555c), 0x280, 0x100, 0x40, 0x100, '\0');
        DisplayMachine.LoadImageInVram(
            ToWordBuffer(LoadingScreen.BYTE_ARRAY_801d2000, 0x50 * 1 * 2), 0, 0x1e0, 0x50, 1, '\x01');
        ReadFile("\\CHR_DATA\\CH_EF_P0.B;1".ToCharArray(), ByteArray801d2000Address, 0);
        DisplayMachine.LoadImageInVram(
            ToWordBuffer(LoadingScreen.BYTE_ARRAY_801d2000, 0x130 * 1 * 2), 0, 0x1e3, 0x130, 1,
            '\x01');

        // BLOCKED: FUN_800376c0((uint)(ushort)GteScratch.DAT_1f80012c) @ 0x800376C0.
        // The stage backdrop. Its body is closed — it creates LAB_80037bf0 and LAB_800378d8 in list
        // 1, reads STGxMD_FileNames[DAT_1f80012c] (\STG\STG1TX.B;1 .. \STG\STG8TX.B;1 at
        // 0x800788B8) into 0x801D2000, runs the load-script interpreter over that buffer, copies
        // three ints out of INT_ARRAY_80078948 into DAT_80083450/DAT_8008344c/DAT_80083448, calls
        // FUN_80037104, and fills a 23 x 23 grid of POLY_FT4 at 0x800ACDA0 — but it cannot be
        // written from here. FUN_80057c80 @ 0x80057C80, the interpreter it needs, is hard-wired in
        // TitleImages.cs to `byte[] file = TITLE_EXE_exe.DAT_80110000` and takes only offsets, while
        // this call site walks BYTE_ARRAY_801d2000. Making the buffer a parameter is an edit to
        // TitleImages.cs, which this pass does not own, so it is reported rather than forced.
        // The stage backdrop and its three tasks are therefore absent.

        // Site 4, the only one whose return value is used.
        pvVar1 = TaskSystem.CreateTask(
            LAB_8004c010_Address, 0x51, 9, 0x3034, 0, TaskSystem.g_TaskListTail[9]);

        // `lw s0,8(v0)` at 0x80058D24 is NOT guarded. On the console a CreateTask that returned 0
        // would fault here; PsxRam.ReadI32 on an unresolvable address returns 0 instead. The missing
        // guard is the original's and is kept — rule 12.
        uVar6 = PsxRam.ReadI32(pvVar1 + 8);

        // BLOCKED: LoadFACE_B() @ 0x80052D68.
        // The character portraits. Every game-side callee it needs is already ported
        // (WaitSearchFile, ReadCDData, FUN_80035778, LoadImageInVram) and its data is closed, but
        // its whole first half is a seek loop: it converts \CHR_DATA\FACE.B;1's start position with
        // CdPosToInt and converts `base + (n-1)*2` back with CdIntToPos for each of twelve portrait
        // slots. LibCd.cs has both routines as `{ /* Do nothing */ return default; }`, so every one
        // of the twelve reads would land on the same sector and the portraits would be wrong rather
        // than missing. Closing the two MSF conversions is SDK work — rule 13 forbids open-coding
        // them here — and it also unblocks FUN_800583fc's own lost seek.

        SelectScreenSetup.FUN_80035700();
        SelectScreenSetup.FUN_8004737c(uVar6);
        SelectScreenSetup.FUN_80027354();
        DAT_80083544 = 1;
    }

    // GHIDRA: ClearVram @ 0x80057508
    private static void ClearVram()
    {
        var local_10 = new LibGpu.RECT
        {
            w = 0x400,
            h = 0x200,
            x = 0,
            y = 0,
        };
        ClearImage(local_10, 0, 0, 0);
        DrawSync(0);
    }

    // GHIDRA: SetupGeometry @ 0x80057674
    private static void SetupGeometry(int ofx, int ofy, int h, int param_4, int param_5,
        int param_6, int param_7, short rx, short ry, short rz)
    {
        SetGeomOffset(ofx, ofy);
        SetGeomScreen(h);
        SetFarColor(0x80, 0x80, 0x80);
        SetBackColor(0x80, 0x80, 0x80);
        GteScratch.MATRIX_1f8000e4.m[6] = 0x1000;
        GteScratch.MATRIX_1f8000e4.m[3] = 0x1000;
        GteScratch.MATRIX_1f8000e4.m[0] = 0x1000;
        GteScratch.MATRIX_1f8000e4.m[8] = 0;
        GteScratch.MATRIX_1f8000e4.m[7] = 0;
        GteScratch.MATRIX_1f8000e4.m[5] = 0;
        GteScratch.MATRIX_1f8000e4.m[4] = 0;
        GteScratch.MATRIX_1f8000e4.m[2] = 0;
        GteScratch.MATRIX_1f8000e4.m[1] = 0;
        SetColorMatrix(GteScratch.MATRIX_1f8000e4);
        GteScratch.SVECTOR_1f800104.vx = 0;
        GteScratch.SVECTOR_1f800104.vy = 0;
        GteScratch.SVECTOR_1f800104.vz = 0;
        RotMatrix(GteScratch.SVECTOR_1f800104, GteScratch.MATRIX_1f800000);
        SetLightMatrix(GteScratch.MATRIX_1f800000);
        GteScratch.SVECTOR_1f80007c.vx = rx;
        GteScratch.SVECTOR_1f80007c.vy = ry;
        GteScratch.SVECTOR_1f80007c.vz = rz;
        GteScratch.DAT_1f800084 = rx;
        GteScratch.DAT_1f800086 = ry;
        GteScratch.DAT_1f800088 = rz;
        RotMatrix(GteScratch.SVECTOR_1f80007c, GteScratch.MATRIX_1f800000);
        SetRotMatrix(GteScratch.MATRIX_1f800000);
        GteScratch._DAT_1f8000b4 = param_4;
        GteScratch.DAT_1f8000b8 = param_5;
        GteScratch._DAT_1f8000bc = param_6;
        GteScratch.DAT_1f8000c4 = param_4;
        GteScratch.DAT_1f8000c8 = param_5;
        GteScratch.DAT_1f8000cc = param_6;
        GteScratch.DAT_1f8000d4 = param_4;
        GteScratch.DAT_1f8000d8 = param_5;
        GteScratch.DAT_1f8000dc = param_6;
        GteScratch.DAT_1f800114 = ofx;
        GteScratch.DAT_1f800124 = ofx;
        GteScratch.DAT_1f80011c = ofx;
        GteScratch.DAT_1f800110 = ofy;
        GteScratch.DAT_1f800120 = ofy;
        GteScratch.DAT_1f800118 = ofy;
        GteScratch.DAT_1f8000d0 = param_7;
        GteScratch._DAT_1f8000c0 = param_7;
        GteScratch.DAT_1f8000e0 = param_7;
    }

    // GHIDRA: FUN_80037388 @ 0x80037388
    // Registered as a task by main and then called directly, so it runs once per frame. It swaps
    // the live camera triplets with their pending counterparts, rebuilds the rotation and
    // translation matrices, and derives DAT_1f800128 from the projected depth.
    internal static void FUN_80037388()
    {
        var local_38 = new LibGte.SVECTOR();
        int[] alStack_30 = new int[2];

        GteScratch.DAT_1f80008c = GteScratch.SVECTOR_1f80007c.vx;
        GteScratch.DAT_1f80008e = GteScratch.SVECTOR_1f80007c.vy;
        GteScratch.DAT_1f800090 = GteScratch.SVECTOR_1f80007c.vz;
        GteScratch.SVECTOR_1f80007c.vx = GteScratch.DAT_1f800084;
        GteScratch.SVECTOR_1f80007c.vy = GteScratch.DAT_1f800086;
        GteScratch.SVECTOR_1f80007c.vz = GteScratch.DAT_1f800088;
        GteScratch.DAT_1f8000d4 = GteScratch._DAT_1f8000b4;
        GteScratch.DAT_1f8000d8 = GteScratch.DAT_1f8000b8;
        GteScratch.DAT_1f8000dc = GteScratch._DAT_1f8000bc;
        GteScratch.DAT_1f8000e0 = GteScratch._DAT_1f8000c0;
        GteScratch._DAT_1f8000b4 = GteScratch.DAT_1f8000c4;
        GteScratch.DAT_1f8000b8 = GteScratch.DAT_1f8000c8;
        GteScratch._DAT_1f8000bc = GteScratch.DAT_1f8000cc;
        GteScratch._DAT_1f8000c0 = GteScratch.DAT_1f8000d0;
        GteScratch.DAT_1f80011c = GteScratch.DAT_1f800114;
        GteScratch.DAT_1f800118 = GteScratch.DAT_1f800110;
        GteScratch.DAT_1f800114 = GteScratch.DAT_1f800124;
        GteScratch.DAT_1f800110 = GteScratch.DAT_1f800120;
        RotMatrix(GteScratch.SVECTOR_1f80007c, GteScratch.MATRIX_1f800000);
        GteScratch.MATRIX_1f800000.t[2] = 0;
        GteScratch.MATRIX_1f800000.t[1] = 0;
        GteScratch.MATRIX_1f800000.t[0] = 0;
        SetRotMatrix(GteScratch.MATRIX_1f800000);
        SetTransMatrix(GteScratch.MATRIX_1f800000);
        local_38.vx = 0;
        local_38.vz = 0;
        local_38.vy = (short)GteScratch.DAT_1f8000b8;
        RotTrans(local_38, GteScratch.VECTOR_1f800094, alStack_30);
        GteScratch.VECTOR_1f800094.vz = GteScratch.VECTOR_1f800094.vz + GteScratch._DAT_1f8000c0;
        SetGeomOffset(GteScratch.DAT_1f800114, GteScratch.DAT_1f800110);
        TransMatrix(GteScratch.MATRIX_1f800000, GteScratch.VECTOR_1f800094);
        SetTransMatrix(GteScratch.MATRIX_1f800000);
        SetRotMatrix(GteScratch.MATRIX_1f800000);
        PushMatrix();
        GteScratch.SVECTOR_1f800020.vy = 0;
        GteScratch.SVECTOR_1f800020.vz = 0;
        GteScratch.SVECTOR_1f800020.vx = GteScratch.SVECTOR_1f80007c.vx;
        RotMatrix(GteScratch.SVECTOR_1f800020, GteScratch.MATRIX_1f800000);
        GteScratch.MATRIX_1f800000.t[2] = 0;
        GteScratch.MATRIX_1f800000.t[1] = 0;
        GteScratch.MATRIX_1f800000.t[0] = 0;
        SetTransMatrix(GteScratch.MATRIX_1f800000);
        SetRotMatrix(GteScratch.MATRIX_1f800000);
        GteScratch.SVECTOR_1f800020.vx = 0;
        GteScratch.SVECTOR_1f800020.vy = 0;
        GteScratch.SVECTOR_1f800020.vz = (short)(GteScratch._DAT_1f8000c0 + 0x9d8);
        RotTrans(GteScratch.SVECTOR_1f800020, GteScratch.VECTOR_1f800048, GteScratch.DAT_1f800078);
        int lVar1 = GteScratch.VECTOR_1f800048.vz;
        if (GteScratch.VECTOR_1f800048.vz < 0)
        {
            lVar1 = GteScratch.VECTOR_1f800048.vz + 3;
        }

        GteScratch.DAT_1f800128 = 0x800 - (lVar1 >> 2);
        if (GteScratch.DAT_1f800128 < 0)
        {
            GteScratch.DAT_1f800128 = 0;
        }

        PopMatrix();
        if (1 < DAT_80083454)
        {
            // 0x206c reaches ordering-table bucket 0x7ff: DAT_800834e0 + 0x70 is the table's first
            // entry, so (0x206c - 0x70) / 4 = 0x7ff, the last bucket. Forward-linked, that bucket
            // draws last, which is what puts the fade quad over everything else.
            AddPrim(FrameLoop.DAT_800834e0 + 0x206c, DisplayMachine.POLY_GT4_800b9518);
        }
    }

    // GHIDRA: POLY_FT4_ARRAY_800a8894 @ 0x800A8894
    // Five consecutive POLY_FT4, 0x28 bytes apart on the console, filled by FUN_80058d64. Real
    // memory rather than objects, so the five packets are contiguous exactly as the original walks
    // them and each carries an address a bucket can point at.
    private const int PolyFt4Array800a8894Address = unchecked((int)0x800A8894);

    internal static readonly POLY_FT4Ref POLY_FT4_ARRAY_800a8894 =
        new(RamRegion(PolyFt4Array800a8894Address, POLY_FT4Ref.Size * 5), 0);

    // GHIDRA: DAT_800a897a @ 0x800A897A
    // Cleared by FUN_80058d64. FUN_80038228 @ 0x80038228 returns 1 immediately when its bit 0 is
    // set, so it gates the whole display machine.
    internal static byte DAT_800a897a;

    // GHIDRA: FUN_80058d64 @ 0x80058D64
    // Lays out five identical textured quads: corners (0x50,0x50) to (0x5A,0x5A), UVs (0,0x58) to
    // (0x27,0x67), clut 0x7985, tpage 0x19, flat white. Field order follows the original, which
    // writes them out of sequence through a single roaming byte pointer.
    //
    // NOT called from main yet: it comes after FUN_80038228(8, 0), which is still open, and
    // reordering the two would not be faithful.
    internal static void FUN_80058d64()
    {
        int iVar2 = 0;
        DAT_800a897a = 0;
        do
        {
            POLY_FT4Ref p = POLY_FT4_ARRAY_800a8894[iVar2];
            SetPolyFT4(p);
            SetShadeTex(p, 0);
            SetSemiTrans(p, 0);
            p.clut = 0x7985;
            p.tpage = 0x19;
            p.u3 = 0x27;
            p.u1 = 0x27;
            p.v1 = 0x58;
            p.v0 = 0x58;
            p.r0 = 0x80;
            p.g0 = 0x80;
            p.b0 = 0x80;
            p.x2 = 0x50;
            p.x0 = 0x50;
            p.x3 = 0x5a;
            p.x1 = 0x5a;
            p.y1 = 0x50;
            p.y0 = 0x50;
            p.y3 = 0x5a;
            p.y2 = 0x5a;
            p.u2 = 0;
            p.u0 = 0;
            p.v3 = 0x67;
            p.v2 = 0x67;
            iVar2 = iVar2 + 1;
        } while (iVar2 < 5);
    }

    // GHIDRA: ReadFile @ 0x80057DF4
    private static void ReadFile(char[] fileName, int buffer, ushort mode)
    {
        var cdlFile = new CdlFILE();
        WaitSearchFile(fileName, cdlFile);
        ReadCDData(cdlFile, buffer, (short)mode);
    }

    // GHIDRA: WaitSearchFile @ 0x80057F80
    private static void WaitSearchFile(char[] fileName, CdlFILE cdlFile)
    {
        CdlFILE result;
        do
        {
            result = CdSearchFile(cdlFile, fileName);
        } while (result == null);
    }

    // GHIDRA: ReadCDData @ 0x80057E40
    private static uint ReadCDData(CdlFILE cdlFile, int buffer, short mode)
    {
        uint sectors = (uint)(cdlFile.size + 0x7ff) >> 0xb;
        byte[] result = new byte[8];
        int readBytes;
        int status;

        while (true)
        {
            do
            {
                CdControl(2, cdlFile.pos, result);
                do
                {
                    status = CdSync(0, result);
                } while (status == 0);
            } while (status == 5);

            do
            {
                readBytes = CdRead((int)sectors, buffer, 0x80);
            } while (readBytes != 1);

            if (mode != 0)
            {
                break;
            }

            // The original spells this as `while (readBytes = CdReadSync(0, result), 0 < readBytes)`;
            // C# has no comma operator, so the assignment is lifted out unchanged.
            readBytes = CdReadSync(0, result);
            while (0 < readBytes)
            {
                VSync(0);
                readBytes = CdReadSync(0, result);
            }

            if (readBytes != -1)
            {
                return sectors;
            }
        }

        return 0;
    }

    // GHIDRA: __main @ 0x8006909C
    private static void __main()
    {
        // PARTIAL: compiler runtime initialization is provided by the CLR.
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: resolves the PSX ranges this overlay models — the TITLE.B staging buffer, and the
    // heap armed by InitHeap where every task block lives. Switching to this resolver also makes
    // the overlapping MOVIE.EXE ranges stop answering, matching what LoadExec does to resident RAM.
    internal static (byte[] Buffer, int Offset)? ResolveAddress(int address)
    {
        if (address >= TitleBBufferAddress && address < TitleBBufferAddress + DAT_80110000.Length)
        {
            return (DAT_80110000, address - TitleBBufferAddress);
        }

        // BYTE_ARRAY_801d2000 @ 0x801D2000, the CD staging buffer LoadingScreen declares. Four of
        // FUN_80058a9c's calls address it raw — ReadFile twice and FUN_80057b08 at +0xA0 and
        // +0x355C — and ReadCDData hands its PSX address straight to CdRead, which writes through
        // PsxRam. Without this row those reads and decodes silently go nowhere.
        if (address >= ByteArray801d2000Address
            && address < ByteArray801d2000Address + LoadingScreen.BYTE_ARRAY_801d2000.Length)
        {
            return (LoadingScreen.BYTE_ARRAY_801d2000, address - ByteArray801d2000Address);
        }

        return TitleImages.Resolve(address)
               ?? SelectScreenSetup.Resolve(address)
               ?? SharedHighRam.Resolve(address)
               ?? PsxHeap.Resolve(address);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: LoadClut @ 0x80074E50 and LoadImageInVram @ 0x80057BB4 take the u_long * form, so
    // the source bytes are packed into PSX words for them. Same bridge as the private ones in
    // TitleImages and LoadingScreen.
    private static ulong[] ToWordBuffer(byte[] source, int byteCount)
    {
        if (byteCount <= 0 || byteCount > source.Length)
        {
            byteCount = source.Length;
        }

        int words = (byteCount + 3) / 4;
        ulong[] result = new ulong[words];
        for (int i = 0; i < words; i++)
        {
            int o = i * 4;
            uint word = source[o];
            if (o + 1 < byteCount) word |= (uint)source[o + 1] << 8;
            if (o + 2 < byteCount) word |= (uint)source[o + 2] << 16;
            if (o + 3 < byteCount) word |= (uint)source[o + 3] << 24;
            result[i] = word;
        }

        return result;
    }
}
