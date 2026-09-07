using System;
using System.IO;
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

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: THE OVERLAY'S ADDRESS RESOLVER, and without it none of this port does anything.
    //
    // PsxRam holds ONE installed resolver, swapped per overlay by PsxSdkBridges, and every
    // PsxRam.Read/Write in VS_EXE goes through it. Until this method existed there was no VS.EXE
    // line in that bridge at all: SELECT.EXE's resolver stayed installed, VS.EXE's addresses matched
    // nothing, and every read returned zero while every write was dropped — silently. Ten thousand
    // lines across three tranches were correct and inert, which is the ninth defect of this port's
    // running tally at the scale of a whole overlay.
    //
    // The order matters. LibGpu's own region table is consulted FIRST because every RamRegion this
    // overlay declares registers there — AnimVm's 0x8C48 workspace, the ordering table, the two
    // scratch locals the anim VM models for stack arguments, FighterSetup's six 0x1E58 slots — so
    // one lookup covers them all and no file has to be listed by hand. The explicit chains follow
    // for the spans that are not RamRegion-backed, then the cross-overlay block, then the heap
    // last, exactly as SELECT_EXE_exe.ResolveAddress orders its own.
    internal static (byte[] Buffer, int Offset)? ResolveAddress(int address)
    {
        if (RamResolve(address, out byte[] buffer, out int offset))
        {
            return (buffer, offset);
        }

        return FileIo.Resolve(address)
               ?? FighterSetup.Resolve(address)
               ?? AnimVm.Resolve(address)
               ?? SharedHighRam.Resolve(address)
               ?? PsxHeap.Resolve(address)
        // THE IMAGE, LAST. Answers only for an address nothing above claims: a table in .data or
        // .rodata that the original reads straight out of its own executable. Chained after the
        // heap by convention — the heap is a span the program armed on purpose, so it outranks
        // bytes it merely inherited — and never declared as a RamRegion because RamResolve would
        // then have shadowed every link above that lies inside the image extent. See PsxExeImage.
               ?? PsxExeImage.Resolve(address);
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

    // JUSTIFICATION: backend MonoGame only
    // RELATION: read-only diagnostic accessors for Validation/VsBootDiagnostic.cs. The three
    // globals above stay private -- the original's are file-local and nothing outside writes them.
    // These exist because the DRAWENV background colour is the one thing about this overlay that is
    // visible without any rendering at all, which makes it a cheap, machine-checkable proof of how
    // far boot got: main seeds (0, 0, 200), the hardcoded dark blue, and FUN_800414ec then
    // overwrites all three from VariantBackgroundColorTable @ 0x80082C30 with one of eight
    // variants. Seeing a variant colour means that function ran; seeing 0/0/200 means it did not.
    internal static int DiagBackgroundR => DAT_8008d394;

    internal static int DiagBackgroundG => DAT_8008d390;

    internal static int DiagBackgroundB => DAT_8008d38c;

    // GHIDRA: DAT_8008d334 @ 0x8008D334 (VS.EXE)
    // The CLUT id of the all-white 256-entry palette main builds on the stack and uploads to
    // VRAM (0, 500).
    private static int DAT_8008d334;

    // GHIDRA: DAT_8008d4f0 @ 0x8008D4F0 (VS.EXE)
    internal static int DAT_8008d4f0;

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
    // The two crt0 words the heap formula reads. They are .data, initialised by the image:
    //     0x800858DC = 00 80 00 00  ->  0x00008000   the stack size
    //     0x800858E0 = 00 00 80 00  ->  0x00800000   the stack-top offset
    // the same two values SELECT.EXE transcribes for its own pair (DAT_8004f828 / DAT_8004f82c),
    // and the same crt0. The PARTIAL that stood here — "nothing in this slice writes them" — was
    // true and misleading: nothing writes them because the loader does, and at zero the formula
    // below went negative, which sent InitHeap down its disarm path instead of arming the
    // 0x734224-byte heap the console arms at 0x800C3DD8. That disarm is where the SELECT heap was
    // left registered as a zombie (see PsxHeap.InitHeap). Two fixes for one defect, each right on
    // its own: the SDK releases on disarm, and this overlay no longer disarms.
    private static uint DAT_800858e0 = 0x00800000;

    private static uint DAT_800858dc = 0x00008000;

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
        BattleScene.FUN_80042054(8, 0);
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

        // JUSTIFICATION: C# language bridge only
        // RELATION: CreateTask stores Fun800411b4Address raw in the node at +0x04, and the
        // scheduler's per-list dispatch (TaskSystem.InvokeCallback) only reaches a body it has a
        // registered delegate for -- see BattleManager.RegisterBattleManagerTask's identical shape.
        // Without this line FUN_800411b4 would run once at boot (the direct call below) and then
        // silently do nothing on every subsequent frame despite being on list 0's schedule.
        TaskSystem.RegisterCallback(Fun800411b4Address, FUN_800411b4);
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
        // JUSTIFICATION: C# language bridge only
        // RELATION: CreateTask range 0x80055E3C brut dans le noeud, et le repartiteur de TaskSystem
        // a besoin de savoir a quel corps porte cette adresse correspond. Sans cette ligne, la
        // liste 9 parcourt un noeud vivant et ne distribue rien — le gestionnaire de combat entier
        // etait compile et inatteignable, exactement comme le corps des combattants l'etait avant
        // que FighterSetup n'appelle FighterTask.RegisterFighterTask.
        //
        // BattleManager.cs expose l'enregistrement sans le faire, et le dit, parce que le createur
        // est `main` et que `main` n'est pas son fichier. C'est ici que la couture se ferme.
        BattleManager.RegisterBattleManagerTask();

        int iVar7Task = TaskSystem.CreateTask(Lab80055e3cAddress, 0x51, 9, 0x3034, 0, TaskSystem.g_TaskListTail[9]);

        // `uVar6 = *(undefined4 *)(iVar7 + 8)` — main reads the field INLINE. There is no accessor
        // in the original, and inventing one here is exactly what rule 15 forbids. The sibling slice
        // refused to write it, and it was right to.
        int uVar6 = PsxRam.ReadI32(iVar7Task + 8);

        Roster.FUN_8005cbe0();
        FUN_80034d98();
        FighterSetup.FUN_800511a8(uVar6);
        FUN_80026a68();

        DAT_8008d444 = 0;
        BattleScene.FUN_80042054(2, 4);

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

    // ---- FUN_800411b4's own scratchpad workspace, declared HERE rather than in Scratchpad.cs.
    // These addresses are not proven to carry the SAME values TITLE.EXE keeps at the matching
    // offsets in TITLE_EXE/GteScratch.cs -- only that VS.EXE's own compiled copy of this function
    // (see FUN_800411b4's own comment: the same 824-byte source as TITLE's FUN_80037388, byte for
    // byte) touches the same offsets. Scratchpad.cs's own header explains why a byte match alone is
    // not enough to merge -- "two overlays storing different things at one address is entirely
    // legitimate on the console" -- so these stay local to VS.EXE, the same treatment DAT_1f80012c
    // above already gets and FileIo.cs already gives 0x1F8000D0-E0/0x1F800120.

    // GHIDRA: DAT_1f80008c @ 0x1F80008C, DAT_1f80008e @ 0x1F80008E, DAT_1f800090 @ 0x1F800090 (VS.EXE)
    // The one-frame-old camera rotation triplet: FUN_800411b4 saves SVECTOR_1f80007c's current
    // vx/vy/vz here before overwriting it from DAT_1f800084/86/88 -- the same "history slot ahead of
    // the swap" shape TITLE.EXE's FUN_80037388 uses at the identical offsets.
    private static short DAT_1f80008c;

    private static short DAT_1f80008e;

    private static short DAT_1f800090;

    // GHIDRA: SVECTOR_1f800020 @ 0x1F800020 (VS.EXE)
    // A scratch SVECTOR FUN_800411b4 reuses for two unrelated RotMatrix/RotTrans calls in the same
    // function. TITLE.EXE keeps the first of its four sprite-quad corners at this same offset
    // (TITLE_EXE/GteScratch.cs), but nothing in VS.EXE's ported tree shows that reading applies
    // here, so it keeps a raw name rather than borrowing TITLE's.
    private static readonly SVECTOR SVECTOR_1f800020 = new();

    // GHIDRA: VECTOR_1f800048 @ 0x1F800048 (VS.EXE)
    // The second RotTrans's result vector; only .vz is read back, by FUN_800411b4 itself, for the
    // DAT_1f800128 depth projection below.
    private static readonly VECTOR VECTOR_1f800048 = new();

    // GHIDRA: DAT_1f800078 @ 0x1F800078 (VS.EXE)
    // The second RotTrans's (long *) flag output. PARTIAL, same as LibGte.RotTrans's own comment:
    // the GTE FLAG register is not modelled in this port, so this is a write-only sink nothing in
    // this function reads back.
    private static readonly int[] DAT_1f800078 = new int[1];

    // GHIDRA: DAT_1f800094 @ 0x1F800094, DAT_1f800098 @ 0x1F800098 (VS.EXE)
    // The vx/vy of the FIRST RotTrans's result vector at 0x1F800094. Its vz is 0x1F80009C --
    // ALREADY declared, by BattleScene.cs, as its own DAT_1f80009c ("a GTE scratchpad word the
    // dispatcher multiplies the sine by"). Scratchpad.cs's header already records 0x1F80009C as a
    // known aliasing point: TITLE keeps a 16-byte VECTOR there while "VS declares a separate int
    // DAT_1f80009c over the same byte and READS it while nothing anywhere writes it." FUN_800411b4
    // below is that missing writer. It is wired straight to BattleScene.DAT_1f80009c rather than a
    // second declaration over the same address, per this project's own duplicate-symbol rule --
    // compare addresses, not names, and one PSX byte gets one C# storage cell within an overlay.
    // Only vx/vy are new here; vz reuses the existing field.
    private static int DAT_1f800094;

    private static int DAT_1f800098;

    // GHIDRA: DAT_1f800128 @ 0x1F800128 (VS.EXE)
    // The depth-projected table offset FUN_800411b4 computes every frame; nothing else in this
    // slice reads it back yet.
    private static int DAT_1f800128;

    // GHIDRA: DAT_8008d398 @ 0x8008D398 (VS.EXE)
    // OWNERSHIP CAVEAT: this is BattleScene.FUN_80042054's own state word -- that function's real
    // body (a many-case switch that reads and writes it repeatedly, per its own cross-references) is
    // still a BLOCKED stub in BattleScene.cs, so nothing currently writes this field and the guard
    // below never fires in this port's present state. Declared here, the first VS.EXE code (in this
    // port) to reach it, rather than left implicit; when FUN_80042054 is transliterated it must use
    // THIS field rather than a second one over the same address.
    // Read via an UNSIGNED halfword (raw MIPS decode at 0x80041490: `lhu v0,0x29c(gp)`, gp-relative
    // to VS.EXE's own 0x8008D0FC per the project's PCSX-Redux evidence channel: 0x8008D0FC + 0x29C
    // = 0x8008D398) and compared unsigned (`sltiu`), hence the unsigned type.
    private static ushort DAT_8008d398;

    // GHIDRA: DAT_800c3bfc @ 0x800C3BFC (VS.EXE)
    // BLOCKED: only ever address-taken by this function, never dereferenced, so its shape (which
    // primitive, what size) is not established here. AddPrim's (int, int) overload takes the raw
    // address, matching the original's `&DAT_800c3bfc` exactly without inventing a packet layout.
    private const int Dat800c3bfcAddress = unchecked((int)0x800C3BFC);

    // GHIDRA: FUN_800411b4 @ 0x800411B4 (VS.EXE)
    // Task id 0x58 on list 0, and main also calls it once directly, immediately after creating it.
    private const int Fun800411b4Address = unchecked((int)0x800411B4);

    //
    // THE SAME SOURCE AS TITLE.EXE's FUN_80037388 @ 0x80037388, RECOMPILED: 824 bytes on both sides,
    // and the two decompilations agree statement for statement (TITLE_EXE_exe.cs carries that
    // function in full, with its own address annotations). The camera/geometry-offset task: swaps
    // the live rotation/geometry-offset triplets with their pending counterparts (the pending side
    // is primed elsewhere -- FileIo.SetupGeometry, at boot), rebuilds the rotation and translation
    // matrices from them, and derives DAT_1f800128 from the projected depth of a second, unrelated
    // RotTrans.
    //
    // SCRATCHPAD READING, address for address, following TITLE's own resolved layout
    // (TITLE_EXE/GteScratch.cs):
    //   0x1F80007C SVECTOR (Scratchpad.SVECTOR_1f80007c)      the live camera rotation
    //   0x1F800084/86/88   (Scratchpad.DAT_1f800084/86/88)    the pending rotation
    //   0x1F80008C/8E/90   (this file, above)                 one-frame-old rotation, saved here
    //   0x1F8000B4/B8/BC/C0 (Scratchpad._DAT_1f8000b4 etc.)   the live geometry-offset triplet+extra
    //   0x1F8000C4/C8/CC   (Scratchpad.DAT_1f8000c4 etc.)     its pending counterpart
    //   0x1F8000D0/D4/D8/DC/E0 (FileIo.DAT_1f8000d0 etc.)     a second pending/live pair FileIo.cs
    //                                                         already declares -- SetupGeometry
    //                                                         primes D0/D4 from its own param_7 and
    //                                                         param_4; this is that pair's first
    //                                                         VS.EXE reader
    //   0x1F800110/114/118/11C/124/120 (Scratchpad.* / FileIo.DAT_1f800120) the geometry-offset
    //                                                         ring, same swap shape
    //   0x1F800000 MATRIX (Scratchpad.MATRIX_1f800000)        the working rotation/translation matrix
    //   0x1F800020 SVECTOR (this file, above)                 scratch, reused twice, unrelated to
    //                                                         the four-corner array TITLE keeps there
    //   0x1F800048 VECTOR, 0x1F800078 (this file, above)      second RotTrans's result and flag sink
    //   0x1F800094/98/9C VECTOR (this file, above; vz is
    //     BattleScene.DAT_1f80009c)                           first RotTrans's result
    //   0x1F800128 (this file, above)                         the projected-depth table offset
    //
    // THE ONE NARROW READ: `DAT_1f800024 = DAT_1f8000c0 + 0x9d8` (Ghidra's own printed C, no
    // underscore -- unlike every OTHER touch of 0x1F8000C0 in this function, which reads/writes the
    // full 32 bits and so prints as `_DAT_1f8000c0`). Raw MIPS decode at 0x8004142c settles it:
    // `lhu v0,0xc0(v0)`, an UNSIGNED HALFWORD load, not a word load. Reusing Scratchpad._DAT_1f8000c0
    // (the existing 32-bit field) here anyway, rather than adding a second, narrower declaration
    // over the same address, is not a guess: for a value later truncated to 16 bits by the SH that
    // follows, `(X + 0x9d8) & 0xffff` and `((X & 0xffff) + 0x9d8) & 0xffff` are the same integer for
    // every X, so the wider field reads back identically regardless of its high 16 bits. The
    // compiler's choice to narrow the load here is an optimisation on its side, not a different
    // source value -- the C source both sites compile from is the one already on Scratchpad.cs.
    private static void FUN_800411b4()
    {
        DAT_1f80008c = Scratchpad.SVECTOR_1f80007c.vx;
        DAT_1f80008e = Scratchpad.SVECTOR_1f80007c.vy;
        DAT_1f800090 = Scratchpad.SVECTOR_1f80007c.vz;
        Scratchpad.SVECTOR_1f80007c.vx = Scratchpad.DAT_1f800084;
        Scratchpad.SVECTOR_1f80007c.vy = Scratchpad.DAT_1f800086;
        Scratchpad.SVECTOR_1f80007c.vz = Scratchpad.DAT_1f800088;
        FileIo.DAT_1f8000d4 = Scratchpad._DAT_1f8000b4;
        FileIo.DAT_1f8000d8 = Scratchpad.DAT_1f8000b8;
        FileIo.DAT_1f8000dc = Scratchpad._DAT_1f8000bc;
        FileIo.DAT_1f8000e0 = Scratchpad._DAT_1f8000c0;
        Scratchpad._DAT_1f8000b4 = Scratchpad.DAT_1f8000c4;
        Scratchpad.DAT_1f8000b8 = Scratchpad.DAT_1f8000c8;
        Scratchpad._DAT_1f8000bc = Scratchpad.DAT_1f8000cc;
        Scratchpad._DAT_1f8000c0 = FileIo.DAT_1f8000d0;
        Scratchpad.DAT_1f80011c = Scratchpad.DAT_1f800114;
        Scratchpad.DAT_1f800118 = Scratchpad.DAT_1f800110;
        Scratchpad.DAT_1f800114 = Scratchpad.DAT_1f800124;
        Scratchpad.DAT_1f800110 = FileIo.DAT_1f800120;

        RotMatrix(Scratchpad.SVECTOR_1f80007c, Scratchpad.MATRIX_1f800000);
        Scratchpad.MATRIX_1f800000.t[2] = 0;
        Scratchpad.MATRIX_1f800000.t[1] = 0;
        Scratchpad.MATRIX_1f800000.t[0] = 0;
        SetRotMatrix(Scratchpad.MATRIX_1f800000);
        SetTransMatrix(Scratchpad.MATRIX_1f800000);

        SVECTOR local_38 = new();
        local_38.vx = 0;
        local_38.vz = 0;
        local_38.vy = (short)Scratchpad.DAT_1f8000b8;

        // JUSTIFICATION: C# language bridge only
        // RELATION: RotTrans needs a real VECTOR object to write its three fields into, but the true
        // backing storage for those three fields is split across DAT_1f800094/DAT_1f800098 (above)
        // and BattleScene.DAT_1f80009c (the aliasing wiring explained above), so a throwaway object
        // receives RotTrans's output here and is copied out field by field -- the same role local_38
        // just above plays for a genuine stack local the original has.
        VECTOR vector1f800094 = new();
        int[] alStack_30 = new int[2];
        RotTrans(local_38, vector1f800094, alStack_30);
        DAT_1f800094 = vector1f800094.vx;
        DAT_1f800098 = vector1f800094.vy;
        BattleScene.DAT_1f80009c = vector1f800094.vz;

        BattleScene.DAT_1f80009c = BattleScene.DAT_1f80009c + Scratchpad._DAT_1f8000c0;
        SetGeomOffset(Scratchpad.DAT_1f800114, Scratchpad.DAT_1f800110);

        // JUSTIFICATION: C# language bridge only
        // RELATION: TransMatrix's int[3] overload (LibGte.cs) exists for exactly this shape --
        // "call sites that hold a real VECTOR reach the other overload instead of unpacking the
        // vector at every call" -- and here the three components are NOT held in one VECTOR object
        // (see the aliasing note above), so the int[] overload is the one that fits without building
        // a second throwaway VECTOR.
        TransMatrix(Scratchpad.MATRIX_1f800000,
            new[] { DAT_1f800094, DAT_1f800098, BattleScene.DAT_1f80009c });
        SetTransMatrix(Scratchpad.MATRIX_1f800000);
        SetRotMatrix(Scratchpad.MATRIX_1f800000);
        PushMatrix();

        SVECTOR_1f800020.vy = 0;
        SVECTOR_1f800020.vz = 0;
        SVECTOR_1f800020.vx = Scratchpad.SVECTOR_1f80007c.vx;
        RotMatrix(SVECTOR_1f800020, Scratchpad.MATRIX_1f800000);
        Scratchpad.MATRIX_1f800000.t[2] = 0;
        Scratchpad.MATRIX_1f800000.t[1] = 0;
        Scratchpad.MATRIX_1f800000.t[0] = 0;
        SetTransMatrix(Scratchpad.MATRIX_1f800000);
        SetRotMatrix(Scratchpad.MATRIX_1f800000);

        SVECTOR_1f800020.vx = 0;
        SVECTOR_1f800020.vy = 0;
        SVECTOR_1f800020.vz = (short)(Scratchpad._DAT_1f8000c0 + 0x9d8);
        RotTrans(SVECTOR_1f800020, VECTOR_1f800048, DAT_1f800078);

        int iVar1 = VECTOR_1f800048.vz;
        if (VECTOR_1f800048.vz < 0)
        {
            iVar1 = VECTOR_1f800048.vz + 3;
        }

        DAT_1f800128 = 0x800 - (iVar1 >> 2);
        if (DAT_1f800128 < 0)
        {
            DAT_1f800128 = 0;
        }

        PopMatrix();
        if (DAT_8008d398 > 1)
        {
            AddPrim(DAT_8008d420 + 0x206c, Dat800c3bfcAddress);
        }
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

    // GHIDRA: DAT_800b2f74 @ 0x800B2F74 (VS.EXE)
    // Five POLY_FT4 packets, 0xC8 bytes (5 * 0x28) run in .bss. CLOSED as POLY_FT4 by FUN_80062a1c
    // below, which calls SetPolyFT4/SetSemiTrans/SetShadeTex on every one of the five and then
    // stamps all twenty fields (r0/g0/b0/x0-3/y0-3/u0-3/v0-3/clut/tpage) by hand — the same shape
    // TITLE_EXE/LoadingScreen.cs's two-packet POLY_FT4_800b9dd4 array already carries in this
    // project, ported the same way for the same reason.
    //
    // OWNERSHIP CAVEAT, in the shape VS_EXE/FighterSetup.cs already uses for DAT_8008da48. Also
    // read and resubmitted every frame by FUN_80062b5c below (both functions live in this file, so
    // there is exactly one declaration to keep straight).
    private const int PolyFt4800b2f74Address = unchecked((int)0x800B2F74);

    private static readonly POLY_FT4Ref POLY_FT4_800b2f74 =
        new(RamRegion(PolyFt4800b2f74Address, POLY_FT4Ref.Size * 5), 0);

    // GHIDRA: FUN_80062a1c @ 0x80062A1C (VS.EXE)
    // CLOSED. Builds the five POLY_FT4_800b2f74 packets FUN_80062b5c (below) submits every frame
    // when AnimVm.DAT_800b305a's bit 2 is up — that function's own comment records what closes the
    // reading of that flag. `AnimVm.DAT_800b305a` is the one declaration for that global; it is
    // NOT redeclared here.
    //
    // Two cursors walk the same five packets in lockstep, exactly as in TITLE_EXE/LoadingScreen.cs:
    // `p`, a POLY_FT4 *, and a raw byte offset (`puVar1` in the original) that the disassembly
    // resolves against `p`'s own base (&DAT_800b2f74 + 0x1D, i.e. offset 29 = v2) rather than
    // walking a second pointer — every `puVar1 + n` in the decompilation lands on a named POLY_FT4
    // field once that base is applied, checked field by field below. Ported directly onto the
    // named fields, as this project's POLY_FT4Ref idiom is built for.
    //
    // The three values that stay CONSTANT across all five packets (y0/y1 = 0x6c, y2/y3 = 0x84,
    // v0/v1 = 0x68, v2/v3 = 0x80) are the original's own — every packet gets the identical vertical
    // strip, only the horizontal placement (x0-3) and the horizontal texel window (u0-3) step by
    // 0x10 per packet, which is what draws five ADJACENT tiles rather than five copies of one.
    private static void FUN_80062a1c()
    {
        POLY_FT4Ref p = POLY_FT4_800b2f74;
        int iVar6 = 0;
        byte cVar5 = 0x38;
        byte cVar4 = 0x28;
        short sVar3 = 0x88;
        short sVar2 = 0x78;
        AnimVm.DAT_800b305a = 0;
        do
        {
            SetPolyFT4(p);
            SetShadeTex(p, 0);
            SetSemiTrans(p, 0);

            // `*(undefined2 *)(puVar1 + -0xf) = 0x7986` and `*(undefined2 *)(puVar1 + -7) = 0x19`,
            // with puVar1 at base offset 0x1D (v2): 0x1D - 0xF = 0x0E (clut), 0x1D - 7 = 0x16
            // (tpage).
            p.WriteHalf(0x0e, 0x7986);
            p.WriteHalf(0x16, 0x19);
            p.x2 = sVar2;
            p.x0 = sVar2;
            p.x3 = sVar3;
            p.x1 = sVar3;
            p.y1 = 0x6c;
            p.y0 = 0x6c;
            p.y3 = 0x84;
            p.y2 = 0x84;
            p.u2 = cVar4;
            p.u0 = cVar4;
            p.u3 = cVar5;
            p.u1 = cVar5;
            p.r0 = 0x80;
            p.g0 = 0x80;
            p.b0 = 0x80;
            p.v1 = 0x68;
            p.v0 = 0x68;
            p.v3 = 0x80;
            p.v2 = 0x80;

            p = p[1];
            cVar5 = (byte)(cVar5 + 0x10);
            cVar4 = (byte)(cVar4 + 0x10);
            sVar3 = (short)(sVar3 + 0x10);
            iVar6 = iVar6 + 1;
            sVar2 = (short)(sVar2 + 0x10);
        } while (iVar6 < 5);
    }

    // GHIDRA: DAT_80110000 @ 0x80110000 (VS.EXE)
    // Its PSX address, which CdRead below is handed directly.
    //
    // ENLARGED FROM 0x5800 TO 0x28000, exactly as this comment's earlier version predicted would be
    // needed. It was sized for what FUN_80062684 alone touches -- CdRead(0xb, ...) is 11 sectors,
    // 0xb * 0x800 = 0x5800 on this port's 2048-byte-per-sector model -- and warned that
    // FUN_8005d25c, FUN_80026ac0 and the FUN_80029xxx family reach the same address and would
    // force an enlargement when transliterated. FUN_80026ac0 landed, and the prediction held: it
    // reads a whole character archive here, and the region's own size is what stopped it.
    //
    // HOW IT FAILED IS WORTH RECORDING, because the symptom pointed at the CD layer and the cause
    // was here. LibDs.ReadDataSectors writes sector by sector and BREAKS OUT of its loop the first
    // time PsxRam.WriteBytes refuses -- which is what a write past the end of a region does. With
    // 0x5800 backing an 0x11000 read, it delivered 11 sectors of 34, CdRead returned 0 instead of
    // 1, and FileIo.ReadCDData threw "CdRead delivered fewer than 34 sector(s)". The message named
    // the read, the position and the destination, and every one of those was correct; the region
    // behind the destination was not.
    //
    // THE NEW SIZE IS MEASURED, not chosen. FUN_80026d08 opens one of the character archives in
    // data/AT1 and data/AT2 and FUN_80026ac0 reads it whole into this address. The largest of those
    // thirty-six files is AT1/BU.B at 163840 bytes = 80 sectors = 0x28000, so that is the size.
    // 0x80110000 + 0x28000 = 0x80138000, far below the next region this port declares
    // (DAT_801C1000), so nothing is overlapped.
    //
    // STILL PARTIAL, and the same rule applies to the next reader as applied to this one: enlarge,
    // never shrink -- RamRegion updates the row rather than adding a second one -- and size it on
    // what that reader demonstrably reads. FUN_8005d25c and the FUN_80029xxx family are still
    // untransliterated and still reach this address.
    private const int Dat80110000Address = unchecked((int)0x80110000);

    private static readonly byte[] DAT_80110000 = RamRegion(Dat80110000Address, 0x28000);

    // GHIDRA: DAT_800c3cb8 @ 0x800C3CB8 (VS.EXE)
    // Two POLY_FT4 packets, contiguous (0x800C3CB8 and 0x800C3CB8 + 0x28 = 0x800C3CE0), CLOSED the
    // same way as POLY_FT4_800b2f74 above -- SetPolyFT4/SetSemiTrans/SetShadeTex on each, then every
    // field stamped by hand. Same two-band-of-the-loading-picture shape as
    // TITLE_EXE/LoadingScreen.cs's POLY_FT4_800b9dd4 / POLY_FT4_800b9dfc pair, which this function
    // is the relinked twin of.
    private const int PolyFt4800c3cb8Address = unchecked((int)0x800C3CB8);

    private static readonly POLY_FT4Ref POLY_FT4_800c3cb8 =
        new(RamRegion(PolyFt4800c3cb8Address, POLY_FT4Ref.Size * 2), 0);

    private static readonly POLY_FT4Ref POLY_FT4_800c3ce0 =
        new(POLY_FT4_800c3cb8.Buf, POLY_FT4Ref.Size);

    // GHIDRA: DAT_800b1f28 @ 0x800B1F28 (VS.EXE)
    // NOT a second ordering table -- bucket 0x400 of OT_800b0f28, exactly as TITLE.EXE's own
    // DAT_800a7830 is bucket 0x400 of OT_800a6830 in TITLE_EXE/LoadingScreen.cs (the file this
    // function is the relinked twin of): 0x800B1F28 - 0x800B0F28 = 0x1000 = 0x400 * 4, and this
    // function both clears and draws OT_800b0f28 itself around the two AddPrim calls that use this
    // address.
    private const int Dat800b1f28Address = unchecked((int)0x800B1F28);

    // GHIDRA: FUN_80062684 @ 0x80062684 (VS.EXE)
    // THE LOADING SCREEN. Byte-for-byte the same source as TITLE.EXE's ShowLoadingScreen
    // @ 0x800583FC, recompiled at VS.EXE's own addresses -- TITLE_EXE/LoadingScreen.cs already
    // carries that file's own reasoning in full; this comment records only where the two diverge.
    //
    // DIFFERENCES FROM THE TITLE.EXE TWIN, closed by this function's own decompilation:
    //   * CStack_38.size is 0xb (11 sectors) here, not TITLE.EXE's 0xa (10) -- VS.EXE's own LOAD.B
    //     is one sector bigger.
    //   * The seek offset is CdPosToInt(pos) + 0x50 flat. TITLE.EXE's own copy instead picks one of
    //     three loading pictures through SHORT_ARRAY_801ff000[0x87] * 10 -- VS.EXE has one.
    //   * The two packets live at 0x800C3CB8, not 0x800B9DD4; the bucket is this overlay's own
    //     0x800B1F28 (bucket 0x400 of OT_800b0f28); the draw/display environments are this file's
    //     DRAWENV_800b0eb8 / DISPENV_800b0f14; the CLUT upload is VS_EXE's own
    //     LoadImage_ReturnTPageOrClutId rather than TitleImages' DisplayMachine wrapper.
    //
    // DEVIATIONS FROM THE ORIGINAL'S OWN CONTROL FLOW, all inherited from the TITLE.EXE twin's own
    // recorded reasoning (TITLE_EXE/LoadingScreen.cs and FileIo.WaitSearchFile / FileIo.ReadCDData
    // carry the evidence in full):
    //   * `do { p = CdSearchFile(...); } while (p == NULL)` becomes one search plus a thrown
    //     FileNotFoundException -- the desktop CdSearchFile answers from a File.Exists probe a
    //     second call cannot answer differently, so the retry either exits immediately or freezes
    //     the host.
    //   * The nested `while (CdSync(...) == 0)` / `while (status == 5)` spin cannot iterate --
    //     CdSync is the constant CdlComplete (2) on this port (LibCd.cs) -- so it is one call.
    //   * `while (CdReadSync(...) != 0)` cannot iterate either -- CdReadSync is the constant 0.
    //   * CdRead's own return is NOT guarded here, matching the original: unlike ReadCDData (which
    //     retries a failed CdRead), this call site discards the result, and that is reproduced.
    private static void FUN_80062684()
    {
        // JUSTIFICATION: PSX hardware adaptation only
        // RELATION: main does not register OT_800b0f28 with RamRegion until DeclareOrderingTableAddress,
        // called near the very end of main just before its own frame loop -- and this function runs
        // earlier, from the boot sequence, so the two AddPrim calls below would resolve nothing
        // without this. Re-registering the same array updates its base rather than adding a second
        // row (see DeclareOrderingTableAddress's own comment), so calling it again here is safe.
        // TITLE_EXE/LoadingScreen.cs takes the identical precaution for the identical reason.
        RamRegion(Ot800b0f28Address, OT_800b0f28);

        CdlFILE CStack_38 = new();
        byte[] local_20 = new byte[8];

        local_20[0] = 0x80;
        CdControlB(0x0e, local_20, null);

        if (CdSearchFile(CStack_38, "\\CHR_DATA\\LOAD.B;1".ToCharArray()) == null)
        {
            throw new FileNotFoundException(
                "CdSearchFile could not resolve \\CHR_DATA\\LOAD.B;1 -- no file at " +
                LibDs.DescribeDiscPath("\\CHR_DATA\\LOAD.B;1"),
                "\\CHR_DATA\\LOAD.B;1");
        }

        CStack_38.size = 0xb;
        int iVar2 = CdPosToInt(CStack_38.pos);
        CdIntToPos(iVar2 + 0x50, CStack_38.pos);
        CdControl(2, CStack_38.pos, local_20);

        CdSync(1, local_20);

        CdRead(CStack_38.size, Dat80110000Address, 0x80);

        CdReadSync(1, local_20);

        FileIo.DecompressLZSS(DAT_80110000, 0x200, FileIo.g_cdFileBufferTable, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(FileIo.g_cdFileBufferTableAddress, 0x140, 0, 0xa0, 0xf0, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(Dat80110000Address, 0, 0x1e0, 0x100, 1, 1);
        SetDispMask(1);
        SetDefDrawEnv(DRAWENV_800b0eb8, 0, 0, 0x140, 0xf0);
        SetDefDispEnv(DISPENV_800b0f14, 0, 0, 0x140, 0xf0);

        // DAT_800b0ecc: DRAWENV + 0x14, i.e. DRAWENV.tpage. Written before the table is cleared,
        // exactly where the store sits in the image.
        DRAWENV_800b0eb8.tpage = 0x85;
        ClearOTag(OT_800b0f28, 0, 0x800);

        int iVar4 = 0;
        short sVar3 = 0x85;
        POLY_FT4Ref p = POLY_FT4_800c3cb8;
        iVar2 = 0;
        do
        {
            SetPolyFT4(p);
            SetSemiTrans(p, 0);
            SetShadeTex(p, 1);

            // Same two-cursor shape as FUN_80062a1c above and as TITLE_EXE/LoadingScreen.cs's own
            // loop: `p` walks whole packets, `iVar2` a byte offset off the fixed base that lands on
            // tpage (+0x16) and clut (+0x0e).
            POLY_FT4_800c3cb8.WriteHalf(0x16 + iVar2, sVar3);
            sVar3 = (short)(sVar3 + 2);
            POLY_FT4_800c3cb8.WriteHalf(0x0e + iVar2, 0x7800);
            p.r0 = 0x80;
            p.g0 = 0x80;
            p.b0 = 0x80;
            p = p[1];
            iVar4 = iVar4 + 1;
            iVar2 = iVar2 + 0x28;
        } while (iVar4 < 2);

        // The remaining stores are absolute in the original, one field of one of the two packets
        // each, in the machine's own order -- not a regrouping. See TITLE_EXE/LoadingScreen.cs's
        // identical block for the field-by-field derivation this one repeats with VS.EXE's own
        // addresses.
        POLY_FT4_800c3cb8.x2 = 0;
        POLY_FT4_800c3cb8.x0 = 0;
        POLY_FT4_800c3cb8.x3 = 0x100;
        POLY_FT4_800c3cb8.x1 = 0x100;
        POLY_FT4_800c3ce0.x2 = 0x100;
        POLY_FT4_800c3ce0.x0 = 0x100;
        POLY_FT4_800c3ce0.x3 = 0x140;
        POLY_FT4_800c3ce0.x1 = 0x140;
        POLY_FT4_800c3cb8.y3 = 0xf0;
        POLY_FT4_800c3cb8.y2 = 0xf0;
        POLY_FT4_800c3ce0.y3 = 0xf0;
        POLY_FT4_800c3ce0.y2 = 0xf0;
        POLY_FT4_800c3cb8.u3 = 0xff;
        POLY_FT4_800c3cb8.u1 = 0xff;
        POLY_FT4_800c3cb8.y1 = 0;
        POLY_FT4_800c3cb8.y0 = 0;
        POLY_FT4_800c3ce0.y1 = 0;
        POLY_FT4_800c3ce0.y0 = 0;
        POLY_FT4_800c3cb8.u2 = 0;
        POLY_FT4_800c3cb8.u0 = 0;
        POLY_FT4_800c3cb8.v1 = 0;
        POLY_FT4_800c3cb8.v0 = 0;
        POLY_FT4_800c3cb8.v3 = 0xef;
        POLY_FT4_800c3cb8.v2 = 0xef;
        POLY_FT4_800c3ce0.u2 = 0;
        POLY_FT4_800c3ce0.u0 = 0;
        POLY_FT4_800c3ce0.u3 = 0x41;
        POLY_FT4_800c3ce0.u1 = 0x41;
        POLY_FT4_800c3ce0.v1 = 0;
        POLY_FT4_800c3ce0.v0 = 0;
        POLY_FT4_800c3ce0.v3 = 0xef;
        POLY_FT4_800c3ce0.v2 = 0xef;
        AddPrim(Dat800b1f28Address, POLY_FT4_800c3cb8);
        AddPrim(Dat800b1f28Address, POLY_FT4_800c3ce0);

        DRAWENV_800b0eb8.dtd = 0;
        DRAWENV_800b0eb8.isbg = 1;
        DRAWENV_800b0eb8.r0 = 0;
        DRAWENV_800b0eb8.g0 = 0;
        DRAWENV_800b0eb8.b0 = 0;
        PutDispEnv(DISPENV_800b0f14);
        PutDrawEnv(DRAWENV_800b0eb8);
        DrawOTag(Ot800b0f28Address);
        DrawSync(0);
    }

    // GHIDRA: VariantFileNameTable @ 0x80082BA0 (VS.EXE)
    // Eight fixed-stride (0x12 = 18-byte) records, each a null-padded CD file name. Bytes read
    // verbatim from the image with read-memory: "\STG\STG1TX.B;1" through "\STG\STG8TX.B;1", each
    // 15 characters plus three trailing 0x00 pad bytes. find-cross-references shows this address
    // is constructed nowhere but FUN_800414ec (the lui/addiu/addu sequence at 0x80041568..0x8004157c
    // building `&VariantFileNameTable + (param_1 & 0xffff) * 0x12`), and the very next byte after
    // the eighth record (0x80082BA0 + 8*0x12 = 0x80082C30) is VariantBackgroundColorTable's first
    // byte, confirming the entry count and stride independently of the caller's index range.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: FileIo.ReadFile already takes its fileName as a C# char[] at every other call site
    // in this file (see e.g. the EFF_AUTO.B;1 / CH_EF_P0.B;1 calls above), not a raw PSX pointer
    // resolved through PsxRam -- so the table is modelled the same way, as literal strings turned
    // into char[] at the call, rather than as a second byte-for-byte .bss array nothing else reads.
    private static readonly string[] VariantFileNameTable =
    {
        "\\STG\\STG1TX.B;1",
        "\\STG\\STG2TX.B;1",
        "\\STG\\STG3TX.B;1",
        "\\STG\\STG4TX.B;1",
        "\\STG\\STG5TX.B;1",
        "\\STG\\STG6TX.B;1",
        "\\STG\\STG7TX.B;1",
        "\\STG\\STG8TX.B;1",
    };

    // GHIDRA: VariantBackgroundColorTable @ 0x80082C30 (VS.EXE)
    // Eight fixed-stride (0xc = 12-byte) records of three full 32-bit words apiece, read verbatim
    // from the image with read-memory. Every word's upper three bytes are 0x00 in the image (each
    // value fits a byte), but the original reads them as `*(uint *)`, not `*(byte *)` -- see
    // FUN_800414ec's own `UINT_8008d394 = *(uint *)(&VariantBackgroundColorTable + iVar2)` and the
    // two loads right after it -- so the full word is kept rather than narrowed. The three columns
    // land in DAT_8008d394, DAT_8008d390, DAT_8008d38c in that order, which that trio's own header
    // comment already identifies as "the background colour main copies into the DRAWENV every
    // frame" (r0, g0, b0 respectively). find-cross-references shows FUN_800414ec is this table's
    // only reader anywhere in the overlay.
    private static readonly uint[] VariantBackgroundColorTable =
    {
        0xA0, 0xD0, 0xF8,
        0x70, 0xC8, 0x80,
        0x20, 0x28, 0x18,
        0xC0, 0xC0, 0xF8,
        0xA8, 0xD0, 0xF8,
        0x98, 0xE0, 0xF0,
        0xB0, 0xB8, 0xF8,
        0xC0, 0xA0, 0xF0,
    };

    // GHIDRA: LAB_80041a1c @ 0x80041A1C (VS.EXE)
    // BLOCKED: a task entry point Ghidra never promoted to a function -- id 0x100, list 1, 4 bytes
    // of workspace, tail-inserted on list 1. Only referenced by FUN_800414ec's own CreateTask call
    // (find-cross-references: one PARAM reference, from 0x800414fc). Same raw-address treatment as
    // Lab80040f78Address and neighbours.
    private const int Lab80041a1cAddress = unchecked((int)0x80041A1C);

    // GHIDRA: LAB_80041704 @ 0x80041704 (VS.EXE)
    // BLOCKED: a second task entry point Ghidra never promoted -- id 0x54, also list 1, no per-node
    // workspace, also tail-inserted on list 1. Only referenced by FUN_800414ec's own CreateTask
    // call (find-cross-references: one PARAM reference, from 0x80041540). Its return value (v0,
    // non-zero on success) gates the rest of FUN_800414ec, exactly as Ghidra's decompilation shows.
    private const int Lab80041704Address = unchecked((int)0x80041704);

    // GHIDRA: DAT_800b7484 @ 0x800B7484 (VS.EXE)
    // The base of the 23x23 grid of 0x48-byte packets FUN_80061204's own header comment already
    // named (PrimitivePools.cs): "walking a 24x24 [sic, the loop bound 0x17 is 23 decimal] grid of
    // packets in .bss from &DAT_800B7484 with a 0x48-byte stride, so the POLY_FT4 is embedded in a
    // larger record there." Declared here as a raw address constant, not a backing byte[] -- every
    // access below goes through PsxRam / the InitializePolyFt4 resolver, exactly as this file's own
    // FUN_80061bd8 treats other .bss regions it does not own a C# array for.
    private const int Dat800b7484Address = unchecked((int)0x800B7484);

    // GHIDRA: FUN_800414ec @ 0x800414EC (VS.EXE)
    // Fed `rand() & 7` by main -- one of eight stage variants, chosen once at boot. Registers two
    // still-unpromoted list-1 tasks (LAB_80041a1c, LAB_80041704); only if the second CreateTask
    // returns non-zero does it read the variant's background texture (VariantFileNameTable) into
    // g_cdFileBufferTable, upload the table-driven records inside it through FUN_80061bd8, latch
    // the variant's background colour triplet (VariantBackgroundColorTable) into DAT_8008d394 /
    // DAT_8008d390 / DAT_8008d38c, register FUN_80040f30 as a list-0xd task with the same variant
    // index, and finally stamp a 23x23 grid of POLY_FT4 packets at DAT_800b7484: InitializePolyFt4
    // fills the fixed fields (tpage 0xb, clut 0x7880, u/v tile extent 0x1f) and this loop fills the
    // four x/y vertex halfwords per cell afterward, exactly as InitializePolyFt4's own header notes
    // ("The four x/y vertices are NOT written here; the caller fills them itself right after.").
    //
    // Every offset and register below was read off the disassembly (0x800415b0..0x800416dc), not
    // off Ghidra's decompilation, which reuses the pseudo-C names iVar2/iVar3/sVar1 for two
    // different live values apiece inside the loop and is not followed literally here. `iVar2` is
    // the outer (row) counter and `iVar6` the inner (column) counter throughout, matching the
    // decompiled InitializePolyFt4 call itself: `(iVar6 % 2) * 0x20` is param_5, `(iVar2 % 2) * 0x20`
    // is param_6.
    //
    // CreateTask arguments confirmed against TaskSystem.CreateTask's own signature (callback, id,
    // listIndex, contextSize, param_5, insertPoint); DAT_80083b94 is g_TaskListTail[1]
    // (0x80083B90 + 1*4). FUN_80040f30's own definition takes a short parameter but Ghidra's
    // decompilation of THIS call site drops the argument (`FUN_80040f30();`); tracing the a0
    // register back from the jal at 0x80041608 shows it was never touched since `addu a0,s0,zero`
    // at 0x800415a4, i.e. it still holds `param_1 & 0xffff` -- the same variant index used for both
    // table lookups just above -- so that is what is passed here.
    private static void FUN_800414ec(uint param_1)
    {
        TaskSystem.CreateTask(Lab80041a1cAddress, 0x100, 1, 4, 0, TaskSystem.g_TaskListTail[1]);
        int iVar2 = TaskSystem.CreateTask(Lab80041704Address, 0x54, 1, 0, 0, TaskSystem.g_TaskListTail[1]);
        if (iVar2 != 0)
        {
            int variantIndex = (int)(param_1 & 0xffff);

            FileIo.ReadFile(VariantFileNameTable[variantIndex].ToCharArray(), FileIo.g_cdFileBufferTableAddress, 0);
            FUN_80061bd8(FileIo.g_cdFileBufferTableAddress, FileIo.g_cdFileBufferTableAddress);

            int colorBase = variantIndex * 3;
            DAT_8008d394 = (int)VariantBackgroundColorTable[colorBase];
            DAT_8008d390 = (int)VariantBackgroundColorTable[colorBase + 1];
            DAT_8008d38c = (int)VariantBackgroundColorTable[colorBase + 2];

            FUN_80040f30((short)variantIndex);

            short sVar8 = 0x100;
            int puVar7 = Dat800b7484Address;
            iVar2 = 0;
            do
            {
                int psVar5 = puVar7 + 0x3c;
                byte rowPhase = (byte)((iVar2 % 2) * 0x20);
                int iVar6 = 0;
                do
                {
                    byte colPhase = (byte)((iVar6 % 2) * 0x20);
                    PrimitivePools.InitializePolyFt4(puVar7, 1, 0xb, 0x7880, colPhase, rowPhase, 0x1f, 0x1f);

                    short sVar1 = (short)(iVar6 << 8);
                    iVar6 = iVar6 + 1;
                    short sVar4 = (short)(iVar6 << 8);
                    short sVar3 = (short)(iVar2 << 8);

                    PsxRam.WriteU16(psVar5 - 4, unchecked((ushort)sVar1));
                    PsxRam.WriteU16(psVar5 - 20, unchecked((ushort)sVar1));
                    PsxRam.WriteU16(psVar5 + 6, 0);
                    PsxRam.WriteU16(psVar5 - 2, 0);
                    PsxRam.WriteU16(psVar5 - 10, 0);
                    PsxRam.WriteU16(psVar5 - 18, 0);
                    PsxRam.WriteU16(psVar5 + 4, unchecked((ushort)sVar4));
                    PsxRam.WriteU16(psVar5 - 12, unchecked((ushort)sVar4));
                    PsxRam.WriteU16(psVar5 - 8, unchecked((ushort)sVar3));
                    PsxRam.WriteU16(psVar5 - 16, unchecked((ushort)sVar3));
                    PsxRam.WriteU16(psVar5 + 8, unchecked((ushort)sVar8));
                    PsxRam.WriteU16(psVar5, unchecked((ushort)sVar8));

                    psVar5 = psVar5 + 0x48;
                    puVar7 = puVar7 + 0x48;
                } while (iVar6 < 0x17);

                sVar8 = (short)(sVar8 + 0x100);
                iVar2 = iVar2 + 1;
            } while (iVar2 < 0x17);
        }
    }

    // GHIDRA: DAT_8008d3d0 @ 0x8008D3D0 (VS.EXE)
    // Its only reference anywhere in the overlay: written here, never read.
    private static short DAT_8008d3d0;

    // GHIDRA: DAT_8008d39c @ 0x8008D39C (VS.EXE)
    // Written here from param_1; also read at 0x800410A0, inside LAB_80040f78's own unpromoted body
    // -- see that task entry's own const just below -- so the read site stays undescribed here.
    private static short DAT_8008d39c;

    // GHIDRA: LAB_80040f78 @ 0x80040F78 (VS.EXE)
    // BLOCKED: a task entry point Ghidra never promoted to a function -- task id 0, list 0xd, 0xc
    // bytes of workspace, tail-inserted on list 0xd. Same raw-address treatment as the other task
    // entries in this file (Lab8005d1fcAddress and neighbours).
    private const int Lab80040f78Address = unchecked((int)0x80040F78);

    // GHIDRA: FUN_80040f30 @ 0x80040F30 (VS.EXE)
    // 72 bytes, one callee (CreateTask, i.e. TaskSystem.CreateTask -- see TaskSystem.cs's own header
    // for how FUN_80053330 was closed). DAT_80083bc4, the raw insert point Ghidra prints, is
    // g_TaskListTail[0xd]: main's own comment on the task table arithmetic gives tail[i] =
    // 0x80083B90 + i*4, and 0x80083B90 + 0xd*4 = 0x80083BC4 exactly.
    //
    // Its only caller is FUN_800414ec above, now transliterated: it passes the variant index
    // (`param_1 & 0xffff`, the same value both table lookups there use) straight through, per the
    // a0-register trace recorded on that call site.
    private static void FUN_80040f30(short param_1)
    {
        DAT_8008d3d0 = 0;
        DAT_8008d39c = param_1;
        TaskSystem.CreateTask(Lab80040f78Address, 0, 0xd, 0xc, 0, TaskSystem.g_TaskListTail[0xd]);
    }

    // GHIDRA: FUN_80061bd8 @ 0x80061BD8 (VS.EXE)
    // A table-driven image loader. param_1[0] is a record count (uVar7); records run from
    // param_1[1], seven words (0x1C bytes) apart: tag, a byte offset into param_2, x, y, w, h,
    // isClut. Tag 0 LZSS-decompresses the block at param_2+offset into FileIo.DAT_800a0d58 and
    // uploads it through FileIo.LoadImage_ReturnTPageOrClutId; tag 1 uploads the block at
    // param_2+offset directly through LoadImage, using x/y/w/h as a RECT and never touching the
    // isClut field. Any OTHER tag value is not consumed as a record at all -- the record cursor
    // only advances by one word instead of seven in that case, exactly as Ghidra's own decompilation
    // shows (the reassignment to +7 words happens only inside the two matched branches); not
    // corrected, per rule 12.
    //
    // Two callers: FUN_800414ec above (now transliterated) reaches it as
    // `FUN_80061bd8(&g_cdFileBufferTable,&g_cdFileBufferTable)` -- the same buffer as both the table
    // and the data region -- and one call from 0x80029700, outside this slice.
    //
    // PARTIAL: the tag-0 branch's DecompressLZSS source needs the shared PsxRam resolver, exactly as
    // FileIo.DecompressAndLoadImage's own comment explains for the identical shape; when the
    // resolver has no row for param_2+offset, this port skips that record's decompress/upload/
    // DrawSync rather than dereferencing garbage, the same fallback DecompressAndLoadImage uses.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: tag 1 writes its RECT fields into a LOCAL scratch rect rather than FileIo's own
    // private RECT_8008d48c (the original's real target, `(RECT *)&DAT_8008d48c`): that field is
    // fully overwritten before every read and never carried across calls, so a local produces the
    // identical LoadImage argument without a second declaration over FileIo's private field, which
    // this file cannot reach.
    private static void FUN_80061bd8(int param_1, int param_2)
    {
        uint uVar7 = (uint)PsxRam.ReadI32(param_1);
        uint uVar11 = 0;
        if (uVar7 != 0)
        {
            int puVar9 = param_1 + 4;
            do
            {
                int puVar10 = puVar9 + 4;
                if (PsxRam.ReadI32(puVar9) == 0)
                {
                    uint uVar1 = (uint)PsxRam.ReadI32(puVar9 + 8);
                    uint uVar2 = (uint)PsxRam.ReadI32(puVar9 + 12);
                    uint uVar3 = (uint)PsxRam.ReadI32(puVar9 + 16);
                    uint uVar4 = (uint)PsxRam.ReadI32(puVar9 + 20);
                    uint uVar5 = (uint)PsxRam.ReadI32(puVar9 + 24);
                    uint uVar6 = (uint)PsxRam.ReadI32(puVar10);
                    puVar10 = puVar9 + 28;

                    var resolved = PsxRam.AddressResolver?.Invoke(param_2 + (int)uVar6);
                    if (resolved != null)
                    {
                        (byte[] src, int srcOffset) = resolved.Value;
                        FileIo.DecompressLZSS(src, srcOffset, FileIo.DAT_800a0d58, 0);
                        FileIo.LoadImage_ReturnTPageOrClutId(Dat800a0d58Address, (ushort)uVar1,
                            (ushort)uVar2, (short)uVar3, (short)uVar4, (byte)uVar5);
                        DrawSync(0);
                    }
                }
                else if (PsxRam.ReadI32(puVar9) == 1)
                {
                    RECT rect = new();
                    rect.x = (short)PsxRam.ReadI32(puVar9 + 8);
                    rect.y = (short)PsxRam.ReadI32(puVar9 + 12);
                    rect.w = (short)PsxRam.ReadI32(puVar9 + 16);
                    rect.h = (short)PsxRam.ReadI32(puVar9 + 20);
                    LoadImage(rect, param_2 + PsxRam.ReadI32(puVar10));
                    puVar10 = puVar9 + 28;
                }

                uVar11 = uVar11 + 1;
                puVar9 = puVar10;
            } while (uVar11 < uVar7);
        }
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the same numeric address as FileIo.cs's own private Dat800a0d58Address, needed here
    // because that constant's accessibility does not reach this file. It names a fixed PSX address,
    // not mutable state, so a second const spelling it out carries none of the duplicate-symbol risk
    // a second byte[] or scratchpad object would.
    private const int Dat800a0d58Address = unchecked((int)0x800A0D58);

    // GHIDRA: DAT_80081828 @ 0x80081828, PTR_DAT_80081910 @ 0x80081910 (VS.EXE)
    // Two .data addresses FUN_80034d98 hands over as raw PSX pointers -- `&DAT_80081828` and, cast
    // through a pointer variable, `&PTR_DAT_80081910`. Ghidra types the second as a pointer
    // (currently holding 0x80000000, itself image data) because SOMETHING in the overlay reads it
    // that way elsewhere; this call site does not dereference it, it hands over the pointer
    // VARIABLE'S OWN address, exactly as `&DAT_80081828` hands over the byte's. Neither symbol is
    // interpreted further here. FileIo.DecompressAndLoadImage's own comment already anticipated the
    // first of the two: "FUN_80034d98 @ 0x80034D98 once (&DAT_80081828, 0x10 x 0x40)".
    private const int Dat80081828Address = unchecked((int)0x80081828);

    private const int Dat80081910Address = unchecked((int)0x80081910);

    // GHIDRA: FUN_80034d98 @ 0x80034D98 (VS.EXE)
    // CLOSED. memset(&DAT_8008da48, 0, 0xb610) then two uploads. The memset target is
    // FighterSetup.DAT_8008da48 -- that file's own OWNERSHIP CAVEAT asked this exact function to
    // reuse it rather than declare a second array over the same address, and this does.
    private static void FUN_80034d98()
    {
        memset(FighterSetup.DAT_8008da48, 0, 0, 0xb610);
        FileIo.DecompressAndLoadImage(Dat80081828Address, 0x380, 0x180, 0x10, 0x40, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(Dat80081910Address, 0, 0x1ea, 0xa0, 1, 0);
    }

    // FUN_800511a8 @ 0x800511A8 stood here as a BLOCKED stub. It is transliterated in
    // VS_EXE/FighterSetup.cs now, and main calls THAT one — see the call above.
    //
    // The stub had to go rather than merely be left unused: while it existed, two functions in this
    // port carried the same `// GHIDRA: FUN_800511a8 @ 0x800511A8` annotation, and main called the
    // empty one. That is tranche 1's duplicate-shared-state defect in its function form — everything
    // compiles, the real code is dead, and nothing says so. The slice that wrote FighterSetup
    // spotted it and reported it rather than editing this file, which is what the ownership rule
    // asks for.

    // GHIDRA: DAT_8008d610 @ 0x8008D610 (VS.EXE)
    // 0x438 bytes, memset here. OWNERSHIP CAVEAT, in the shape VS_EXE/FighterSetup.cs already uses
    // for DAT_8008da48: this file is the first VS.EXE code to reach the block, so it is declared
    // here rather than left implicit, and any later slice that transliterates LAB_80026888 (the
    // block's one reader -- see that task entry's own const below) must use THIS array rather than
    // declare a second one over the same address.
    //
    // The extent is closed by two facts, not one: FUN_80026a68 memsets exactly this span, and
    // 0x8008D610 + 0x438 = 0x8008DA48 -- FighterSetup.DAT_8008da48's own address, exactly. That
    // matches TITLE.EXE's identical pair (FUN_80027354's 0x438 at DAT_800836D4, immediately
    // followed by FUN_80035700's 0xB610 at DAT_80083B0C, see TITLE_EXE/SecondScreenSetup.cs), which
    // is the same relationship at different addresses, not a coincidence of size.
    //
    // PARTIAL: what the thirty 0x24-byte records it implies (0x438 / 0x24 = 0x1e -- TITLE.EXE's own
    // comment says thirty, which is 0x1e; the count is not re-derived here) hold is not established
    // by this function, which only clears them.
    private const int Dat8008d610Address = unchecked((int)0x8008D610);

    private static readonly byte[] DAT_8008d610 = RamRegion(Dat8008d610Address, 0x438);

    // GHIDRA: LAB_80026888 @ 0x80026888 (VS.EXE)
    // BLOCKED: a task entry point Ghidra never promoted to a function -- the fifth of this
    // overlay's boot tasks, task id 0, list 0xb, no per-node workspace (contextSize 0), inserted at
    // g_TaskListTail[0xb]. Not registered with TaskSystem for the same reason the other raw task
    // addresses in this file are not.
    private const int Lab80026888Address = unchecked((int)0x80026888);

    // GHIDRA: FUN_80026a68 @ 0x80026A68 (VS.EXE)
    // CLOSED. A memset then a CreateTask; FUN_80053330 is TaskSystem.CreateTask (see
    // TaskSystem.cs's own header comment), and this call's six arguments match that signature's
    // shape exactly: callback, id 0, list 0xb, contextSize 0, param_5 1, insertPoint
    // g_TaskListTail[0xb].
    private static void FUN_80026a68()
    {
        memset(DAT_8008d610, 0, 0, 0x438);
        TaskSystem.CreateTask(Lab80026888Address, 0, 0xb, 0, 1, TaskSystem.g_TaskListTail[0xb]);
    }

    // GHIDRA: DAT_800b2f24 @ 0x800B2F24 (VS.EXE)
    // NOT a third ordering table -- bucket 0x7FF of OT_800b0f28, the very LAST bucket: 0x800B2F24 -
    // 0x800B0F28 = 0x1FFC = 0x7FF * 4. AddPrim below never has to register it separately because
    // OT_800b0f28 itself is already registered by the time this runs every frame -- main's own
    // DeclareOrderingTableAddress, called once before the frame loop starts.
    private const int Dat800b2f24Address = unchecked((int)0x800B2F24);

    // GHIDRA: FUN_80062b5c @ 0x80062B5C (VS.EXE)
    // Run every frame between the last task list and the submit; whatever it draws lands in
    // OT_800b0f28's very last bucket, so it is behind everything else in the frame.
    //
    // PARTIAL: AnimCmdEffects.cs's own reading of this function, carried over into
    // AnimVm.DAT_800b305a's comment, is the closest thing to an interpretation this port has --
    // "toggles bit 2 off a pad test, sets bit 0 from it, latches bit 1, forces the word to 0 when
    // DAT_8008d4f0 != 1, and — when bit 2 is up — submits five primitives from 0x800B2F74 through
    // AddPrim, which reads as the freeze the pause overlay drives." That reading is reproduced
    // here; "pause" stays a reading, not a closed symbol, and nothing below decides it further.
    //
    // The control flow is NOT restructured into if/else: LAB_80062bd0 is a label Ghidra places
    // INSIDE the first branch's body that the second branch also jumps INTO (`goto LAB_80062bd0`
    // from inside the else-arm, re-entering the first arm's own code with the just-updated flag
    // word). C# will not let a goto jump into the middle of an `if` block from outside it, so the
    // shape below is flattened to the same labels at the same nesting Ghidra itself would reach if
    // asked for raw control flow rather than its structured approximation -- every branch, both
    // gotos and the fallthrough after each, is kept; only the block nesting changes to something
    // the language accepts.
    private static void FUN_80062b5c()
    {
        ushort uVar1;

        if (((PadInput.g_PadNewlyPressed[0] & 0x800) == 0) &&
            ((SharedHighRam.SHORT_ARRAY_801ff000[0x80] != 0) ||
             ((PadInput.g_PadNewlyPressed[1] & 0x800) == 0)))
        {
            goto LAB_80062bd0;
        }

        AnimVm.DAT_800b305a = (ushort)(AnimVm.DAT_800b305a ^ 4);
        if ((AnimVm.DAT_800b305a & 4) == 0)
        {
            AnimVm.DAT_800b305a = (ushort)(AnimVm.DAT_800b305a & 0xfffc);
            goto LAB_80062bd0;
        }

        goto AfterGate;

    LAB_80062bd0:
        uVar1 = AnimVm.DAT_800b305a;
        if ((AnimVm.DAT_800b305a & 4) == 0)
        {
            goto LAB_80062c1c;
        }

    AfterGate:
        uVar1 = (ushort)(AnimVm.DAT_800b305a | 1);
        if ((AnimVm.DAT_800b305a & 2) != 0)
        {
            uVar1 = (ushort)(AnimVm.DAT_800b305a ^ 1);
            if ((uVar1 & 1) != 0)
            {
                uVar1 = (ushort)(uVar1 & 0xfffd);
            }
        }

    LAB_80062c1c:
        AnimVm.DAT_800b305a = uVar1;

        if (DAT_8008d4f0 != 1)
        {
            AnimVm.DAT_800b305a = 0;
        }

        if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 0x88000008) != 0)
        {
            AnimVm.DAT_800b305a = 0;
        }

        if ((AnimVm.DAT_800b305a & 4) != 0)
        {
            int iVar2 = 0;
            POLY_FT4Ref p = POLY_FT4_800b2f74;
            do
            {
                AddPrim(Dat800b2f24Address, p);
                iVar2 = iVar2 + 1;
                p = p[1];
            } while (iVar2 < 5);
        }
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
