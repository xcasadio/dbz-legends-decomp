using PsxSdkMonogame;
using static PsxSdkMonogame.Kernel;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.LibGte;

namespace DbzLegendsRemaster.SELECT_EXE;

// SELECT.EXE — the mode-select overlay. TITLE.EXE hands control here through
// LoadExec("cdrom:\\SELECT.EXE;1"); this overlay hands control on to DEMO.EXE, VS.EXE or SP.EXE
// through OverlayExit.ShutdownAndLoadExecutable. There is no fourth exit: the whole program contains exactly
// three "cdrom:" strings and exactly one LoadExec call site.
//
// THIS OVERLAY IS NOT BUILT LIKE TITLE.EXE, and none of TITLE.EXE's engine transfers:
//   * it draws through Sony's high-level libgs (GsOT / GsSPRITE / GsLINE / GsBOXF). TITLE.EXE
//     links no libgs at all and hand-rolls DRAWENV/DISPENV/OT over libgpu;
//   * it has NO task scheduler. There is no CreateTask / ExecuteTaskList and no indirect dispatch
//     anywhere in the game half of .text (0x800213A8..0x80034FFF);
//   * it has NO frame loop. It has a frame STEP, DrawFrame @ 0x800344A4 with 61 call sites,
//     which the screen bodies call inline from their own blocking do/while loops;
//   * its heap is armed once in start and NEVER USED: SELECT.EXE calls neither malloc nor free.
//     The only allocator that runs is SpuInitMalloc over SPU RAM.
//
// SCOPE OF THIS FILE: start and main. The boot chain around them is in SelectScreen.cs
// (the graphics/CD bring-up, the USAGI.B load, ClearVram, the GsSPRITE initialiser),
// Decompressor.cs, MemoryCard.cs (the card bring-up, probe and boot-time save load main calls on
// its pre-loop path) and OverlayExit.cs; the frame step is in FrameStep.cs and the CD-DA service it
// ends with is in CdAudio.cs. Every screen body and the menu driver are a later slice and are
// BLOCKED stubs here, each naming its address.
internal sealed class SELECT_EXE_exe
{
    // GHIDRA: g_HeapBase @ 0x800692A0
    // The heap base start hands to InitHeap. It is the first word past the end of .bss
    // (0x8006929C) plus one word.
    private const int HeapBaseAddress = unchecked((int)0x800692A0);

    // GHIDRA: DAT_80055a78 @ 0x80055A78
    // First word start's zero loop clears. It is the first byte of .sbss.
    private const int BssClearFirst = unchecked((int)0x80055A78);

    // GHIDRA: g_BssEnd @ 0x8006929C
    // The loop's exclusive limit and the value start publishes into DAT_8004f808. It is one past
    // the last byte of .bss (measured: .bss is 0x80055B88..0x8006929B).
    private const int BssClearLimit = unchecked((int)0x8006929C);

    // GHIDRA: g_OptionsRecord64 @ 0x801FF018
    // The persistent options word in the cross-overlay block, read by main line 46 for bit 1.
    // Modelled by SharedHighRam, which ResolveAddress chains, so it is read here as the raw PSX
    // address the original reads.
    private const int g_OptionsRecord64_Address = unchecked((int)0x801FF018);

    // GHIDRA: DAT_8004f828 @ 0x8004F828
    // .data, image value 0x00008000 — the stack size SNMAIN reserves. Read from the image with
    // read-memory: `00 80 00 00` at 0x8004F828.
    private static uint DAT_8004f828 = 0x00008000;

    // GHIDRA: DAT_8004f82c @ 0x8004F82C
    // .data, image value 0x00800000 — the stack-top offset. Read from the image with read-memory:
    // `00 00 80 00` at 0x8004F82C. SP becomes (0x00800000 - 8) | 0x80000000 = 0x807FFFF8.
    private static uint DAT_8004f82c = 0x00800000;

    // GHIDRA: g_HeapSize @ 0x8004F80C
    // .data, image value 0. start computes the heap SIZE into it.
    private static uint g_HeapSize;

    // GHIDRA: DAT_8004f808 @ 0x8004F808
    // .data. start publishes &g_BssEnd here. PARTIAL: nothing on this slice's path reads it
    // back, so what the SN runtime uses it for is not closed; it is kept because the store is.
    internal static int DAT_8004f808;

    // GHIDRA: DAT_80055b1c @ 0x80055B1C
    // .sbss. start stores its own return address here (`sw a0, -0x7f8(at)` at 0x80034834, with a0
    // holding the value the entry point was reached with).
    // PARTIAL: on desktop there is no caller return address to store. The store is kept and the
    // value is 0, because nothing in SELECT.EXE reads it back on this slice's path.
    internal static int DAT_80055b1c;

    // GHIDRA: g_CurrentMenuState @ 0x80055B50
    // .sbss, SIXTEEN BITS (Ghidra types it undefined2 and main's second store is
    // `(undefined2)iVar2`). main writes 0xFFFF before the memory-card save load and then writes the
    // menu state into it once per outer iteration. MemoryCard.RunSaveLoadFlow reads that 0xFFFF as a
    // SIGNED short — its state 7 tests `g_CurrentMenuState == -1`, which is what makes "no save file on
    // the card" return 0 to main instead of putting a message screen up.
    internal static ushort g_CurrentMenuState;

    // GHIDRA: g_PadButtonWord @ 0x80055B6C
    // .sbss, 32 bits. The recon reads it as the cached pad word; main only zeroes it.
    // PARTIAL: its readers are all in the screen bodies, which are not in this slice.
    internal static int g_PadButtonWord;

    // GHIDRA: DAT_80055b80 @ 0x80055B80
    // .sbss, 32 bits. THE RENDER/RELOAD FLAG WORD. Eleven write sites were measured in the whole
    // overlay; the four bits are:
    //   bit 0 (1)  suppress GsSortClear for this frame
    //   bit 1 (2)  the frame step sorts 0x62 sprites instead of 100
    //   bit 2 (4)  request a full asset reload — main's lines 37..44
    //   bit 3 (8)  the frame step takes its boxfill path
    // main arms bit 2 before entering the loop, so the asset load runs on the FIRST iteration; it
    // is not a pre-loop step.
    internal static int DAT_80055b80;

    // GHIDRA: g_OrderingTableTags0 @ 0x80065350
    // GsOT[0]'s tag array. EIGHT tags of four bytes: main sets GsOT[0].length = 3, and libgs's
    // GsClearOt clears `1 << (length & 0x1f)` entries. The extent is closed independently by the
    // second array's address — 0x80065370 - 0x80065350 = 0x20 = 8 * 4.
    // A LibGpu.RamRegion because GsClearOt hands GsOT.org to LibGpu.ClearOTagR and GsDrawOt hands
    // GsOT.tag to LibGpu.DrawOTag, both of which take PSX addresses and resolve them.
    internal static readonly byte[] g_OrderingTableTags0 = RamRegion(unchecked((int)0x80065350), 32);

    // GHIDRA: g_OrderingTableTags1 @ 0x80065370
    // GsOT[1]'s tag array, same size and same reasoning. 0x80065370 + 0x20 = 0x80065390, four
    // bytes below GsOT[0]'s handle at 0x800654C4 — no named global falls between.
    internal static readonly byte[] g_OrderingTableTags1 = RamRegion(unchecked((int)0x80065370), 32);

    // GHIDRA: g_GsOtArray2 @ 0x800654C4
    // The two libgs ordering-table handles, armed BY HAND in main lines 18..21 — this overlay
    // never calls a libgs routine that would build them. Two elements, because the frame step
    // indexes them as `&g_GsOtArray2 + GsGetActiveBuff() * 5` on an int * and 0x800654D8 -
    // 0x800654C4 = 0x14 = sizeof(GsOT).
    internal static readonly LibGs.GsOT[] GsOT_800654c4 =
    {
        new LibGs.GsOT(),
        new LibGs.GsOT(),
    };

    // GHIDRA: GsSPRITE_ARRAY_800654ec @ 0x800654EC
    // ONE HUNDRED sprites of 36 bytes, 0x800654EC..0x800662FB. The count is main's own argument to
    // InitializeSpriteArray and the frame step's own sort bound; the stride is closed twice — InitializeSpriteArray
    // advances `param_1 + 9` on an undefined4 *, and main's second call passes 0x80065AB0, which is
    // 0x800654EC + 36 * 41.
    // It starts immediately after the two-element GsOT array: 0x800654D8 + 0x14 = 0x800654EC.
    internal static readonly LibGs.GsSPRITE[] GsSPRITE_ARRAY_800654ec = NewSpriteArray(100);

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original's .bss array is 3600 zeroed bytes that the code then treats as
    // GsSPRITE[100]. C# needs each element constructed before it can be written.
    private static LibGs.GsSPRITE[] NewSpriteArray(int count)
    {
        var result = new LibGs.GsSPRITE[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new LibGs.GsSPRITE();
        }

        return result;
    }

    // GHIDRA: start @ 0x800347C4
    // Ghidra plates it "Possible SNMAIN.OBJ/__SN_ENTRY_POINT". TITLE.EXE's start @ 0x80068FF4 is
    // the same source: same zero loop, same heap formula, same trap(1) tail.
    //
    // The tail past trap(1) — the stores through (uVar5 - 0xc)/(uVar5 - 8)/(uVar5 - 4) and the
    // destructor walk over switchdataD_80020000 guarded by `if (false)` — is UNREACHABLE: `break
    // 0x1` at 0x80034868 raises a breakpoint exception and never falls through. It is not
    // transliterated, and it is not skipped for tidiness: there is no path to it.
    public void start()
    {
        // The bss clear: `puVar1 = &DAT_80055a78; do { *puVar1 = 0; puVar1 += 1; }
        // while (puVar1 < &g_BssEnd);` — 0x13824 bytes of .sbss + .bss, one word at a time.
        //
        // PARTIAL: this port models .sbss/.bss as C# statics and as byte[] regions, every one of
        // which the CLR zero-initialises before first use, so the loop has nothing left to zero.
        // The range is spelled out above (BssClearFirst..BssClearLimit) rather than implied. This
        // is the same treatment __main and _96_init get across the ported overlays.

        g_HeapSize = ((DAT_8004f82c - 8U) - DAT_8004f828) - 0x6929c;
        DAT_8004f808 = BssClearLimit;
        DAT_80055b1c = 0;

        // 0x0078E75C bytes at 0x800692A0 — a dev-kit-sized heap on 2 MB retail hardware, exactly
        // as in TITLE.EXE. IT IS NEVER USED: SELECT.EXE calls neither malloc nor free anywhere
        // (measured: the only malloc/free symbols in the program are SpuInitMalloc / SpuMalloc /
        // SpuFree over SPU RAM). The call is kept because start makes it.
        InitHeap(HeapBaseAddress, (int)g_HeapSize);

        main();

        // trap(1) — `break 0x1`. Control never returns from it on the console.
        // PARTIAL: modelled as a plain return, because main here is unreachable-by-exit anyway
        // (see the note on case -1 in main) and the desktop host owns thread teardown.
    }

    // GHIDRA: main @ 0x8003045C
    // 572 bytes, 75 lines, 28 callees. Lines 8..17 are TITLE.EXE's main prologue call for call.
    //
    // CASE -1 IS UNREACHABLE, AND IT IS REPRODUCED ANYWAY — rule 12. The switch value comes from
    // FUN_80030a6c, which tail-calls the menu driver RunModeMenu @ 0x800283A0 and returns
    // g_ModeMenuCursor. find-cross-references on g_ModeMenuCursor reports 14 references and every single
    // one is inside RunModeMenu, which clamps it to [0, itemCount - 1]. Nothing else can write a
    // negative value into it. The real exits are the three LoadExec calls inside the state
    // handlers, so the do/while never terminates and the DrawSync/StopPAD/StopCallback/
    // ResetGraph/exit tail below is dead code on the console too.
    public void main()
    {
        bool bVar1;
        int iVar2;

        __main();
        EnterCriticalSection();
        ResetCallback();
        ResetGraph(0);
        InitGeom();
        SetDispMask(0);
        SelectScreen.ClearVram();
        PadInit(0);
        CdInit();
        ExitCriticalSection();

        // Lines 18..21: the two GsOT handles, armed by hand. Only .org and .length are written;
        // GsClearOt fills .offset, .point and .tag every frame.
        GsOT_800654c4[0].org = RamAddressOf(g_OrderingTableTags0, 0);
        GsOT_800654c4[0].length = 3;
        GsOT_800654c4[1].length = 3;
        GsOT_800654c4[1].org = RamAddressOf(g_OrderingTableTags1, 0);

        SelectScreen.FUN_80030698();
        OverlayExit.InitializePadRemapTablePointers();
        MemoryCard.InitializeMemoryCard();
        SharedHighRam.g_CardProbeResult = MemoryCard.ProbeMemoryCard(0);
        bVar1 = false;
        if (SharedHighRam.g_CardProbeResult == 0)
        {
            g_CurrentMenuState = 0xffff;
            iVar2 = MemoryCard.RunSaveLoadFlow();
            if (iVar2 == 0)
            {
                SharedHighRam.g_CardProbeResult = 2;
            }
        }

        g_PadButtonWord = 0;
        DAT_80055b80 = DAT_80055b80 | 4;
        do
        {
            if ((DAT_80055b80 & 4) != 0)
            {
                DAT_80055b80 = 0;
                SelectScreen.InitializeSpriteArray(GsSPRITE_ARRAY_800654ec, 0, 100);
                SelectScreen.LoadUSAGI_B();
                MenuIntro.BuildModeMenuScreen();

                // 0x80065AB0 = GsSPRITE_ARRAY_800654ec + 36 * 41, i.e. element 41 of the same
                // array, twelve entries.
                SelectScreen.InitializeSpriteArray(GsSPRITE_ARRAY_800654ec, 41, 0xc);
                DAT_80055b80 = DAT_80055b80 & unchecked((int)0xfffffffb);
            }

            iVar2 = ModeBranches.FUN_80030a6c();

            // Bit 1 of the options word gates menu item 2. When it is clear, item 2 is redirected
            // to state 3 instead. The same gate appears inside FUN_80030a6c, where it swaps two
            // sprites' UV and one sprite's attribute.
            // The read goes through the raw PSX address the original reads, which
            // SELECT_EXE_exe.ResolveAddress answers for by chaining SharedHighRam — so it needs
            // PsxSdkBridges.ActivateSelectExe to have installed this overlay's resolver, exactly
            // as every other raw-address read in the ported overlays does.
            // PARTIAL: what the bit means is NOT ESTABLISHED. Its two writers are the memory-card
            // load path — MemoryCard.RunSaveLoadFlow, which is ported now and copies SIXTY-FOUR bytes
            // of the 0x80-byte card record over 0x801FF018..0x801FF057 — and FUN_80031c8c, which is
            // not in this slice. On this port's boot path no card record is read (there is no save
            // file to find), so the bit is the 0 start's .bss clear leaves and item 2 is redirected.
            if (((PsxRam.ReadI32(g_OptionsRecord64_Address) & 2) == 0) && (iVar2 == 2))
            {
                iVar2 = 3;
            }

            FrameStep.DrawFrame();
            g_CurrentMenuState = (ushort)iVar2;
            switch (iVar2)
            {
                case 0:
                    ModeBranches.RunDemoModeScreen();
                    break;
                case 1:
                    ModeBranches.RunVsModeScreen();
                    break;
                case 2:
                    ModeBranches.RunSpModeScreen();
                    break;
                case 3:
                    RunOptionsScreen();
                    break;
                case -1:
                    bVar1 = true;
                    break;
            }
        } while (!bVar1);

        DrawSync(0);
        StopPAD();
        StopCallback();
        ResetGraph(0);
        exit(0);
    }

    // BuildModeMenuScreen @ 0x8002EA8C stood here as a BLOCKED stub. It is now transliterated in
    // MenuIntro.cs — the mode menu's build and entry animation, 6608 bytes.

    // FUN_80030a6c @ 0x80030A6C, RunDemoModeScreen @ 0x80030AF8, RunVsModeScreen @ 0x80030EF8 and
    // RunSpModeScreen @ 0x800310A8 stood here as BLOCKED stubs. All four are now transliterated in
    // ModeBranches.cs, with the menu driver RunModeMenu @ 0x800283A0 in ModeMenu.cs and the shared
    // list cursor RunListSelect @ 0x80033D34 in ListCursor.cs. main's switch calls them above.

    // GHIDRA: g_OptionsCursor @ 0x80055B0C
    // .sbss. The options screen's row cursor, and nothing else in the program touches it — sixteen
    // references, all inside RunOptionsScreen. Four rows: down wraps 3 -> 0, up wraps 0 -> 3.
    internal static int g_OptionsCursor;

    // GHIDRA: DAT_80055b10 @ 0x80055B10
    // .sbss. Row 2 (操作設定): which pad the button-config screen configures. Toggling it to 1 is
    // gated on GetPadStatus(1) reporting a second pad; without one it can only ever be 0.
    internal static uint DAT_80055b10;

    // GHIDRA: DAT_80055b14 @ 0x80055B14
    // .sbss. Row 3 (設定): the save/load toggle. It is a plain flip, ungated.
    internal static uint DAT_80055b14;

    // GHIDRA: DAT_80055a50 @ 0x80055A50, DAT_80055a58 @ 0x80055A58, DAT_80055a60 @ 0x80055A60
    // .sdata. THREE PARALLEL TABLES, one entry per difficulty, that place row 1's value box: x,
    // then u, then w. Ghidra had them as pairs of undefined4 and they were briefly typed RECT here
    // — wrongly. The usage settles it: RunOptionsScreen indexes them as `(&RStack_40.x)[level]`
    // with level in 0..2, so they are short tables, not rectangles. They are typed short[4] now.
    // Values read live off the console, and they match the screen: three boxes of different widths.
    private static readonly short[] DAT_80055a50 = { -40, 28, 93, 0 };

    private static readonly short[] DAT_80055a58 = { 176, 168, 168, 0 };

    private static readonly short[] DAT_80055a60 = { 64, 56, 56, 0 };

    // GHIDRA: RunOptionsScreen @ 0x800315C0
    // main's case 3, reached from caseD_3 @ 0x80030638 and its only caller. 1668 bytes. It does NOT
    // LoadExec: it owns a blocking do/while and returns into main's loop, like every other screen in
    // this overlay.
    //
    // The screen has four rows, confirmed against the console:
    //     0  音楽      stereo / mono          toggles _DAT_801ff01e
    //     1  難易度    three difficulties     cycles DAT_801ff01c over 0..2
    //     2  操作設定  1P / 2P                toggles DAT_80055b10, gated on a second pad
    //     3  設定      save / load            toggles DAT_80055b14
    //
    // Left (0x8000) and right (0x2000) both adjust the current row's value and are NOT symmetric on
    // row 1: right increments and wraps 2 -> 0, left tests for zero before decrementing and rewrites
    // the underflow to 2. Confirm (0x20) enters a sub-screen on rows 2 and 3, and on row 0 only when
    // bit 2 of g_OptionsRecord64 is set — that bit alone is what makes the sound test exist.
    // Cancel (0x40) drops out of the loop.
    //
    // The repeat gate is the same shape the other screens use: a press is taken either on the frame
    // the pad goes from idle to pressed, or once every thirteenth frame while it is held.
    internal static void RunOptionsScreen()
    {
        // The original copies each eight-byte table into its own stack slot before the loop, through
        // the unaligned-store dance the compiler emits for a struct copy. The copy is what matters.
        short[] RStack_40 = { DAT_80055a50[0], DAT_80055a50[1], DAT_80055a50[2], DAT_80055a50[3] };
        short[] RStack_38 = { DAT_80055a58[0], DAT_80055a58[1], DAT_80055a58[2], DAT_80055a58[3] };
        short[] RStack_30 = { DAT_80055a60[0], DAT_80055a60[1], DAT_80055a60[2], DAT_80055a60[3] };

        int iVar19 = 0;
        int iVar20 = 0;
        bool bVar4 = false;
        bool bVar5 = true;

        OptionsScreen.BuildOptionsScreen(g_OptionsCursor, DAT_80055b10, DAT_80055b14);

        do
        {
            uint uVar16 = PadInput.FUN_80026208(3);
            g_PadButtonWord = (ushort)(uVar16 & 0xffff);
            if (g_PadButtonWord == 0)
            {
                bVar4 = true;
                iVar19 = 0;
                iVar20 = 0;
            }

            bool bVar2 = 0xc < iVar20;
            iVar20 = iVar20 + 1;

            // `(bVar2) && (iVar19 = iVar19 + 1, iVar19 == 1)` — C# has no comma operator, so the
            // increment is written out. It still only happens when bVar2 holds, which is what the
            // short-circuit in the original guarantees.
            bool bVar6 = false;
            if (bVar2)
            {
                iVar19 = iVar19 + 1;
                bVar6 = iVar19 == 1;
            }

            if (bVar6 || (bVar4 && g_PadButtonWord != 0))
            {
                if (bVar4)
                {
                    bVar4 = false;
                }

                GsSPRITE_ARRAY_800654ec[g_OptionsCursor + 2].r = 0x40;
                GsSPRITE_ARRAY_800654ec[g_OptionsCursor + 2].g = 0x40;
                GsSPRITE_ARRAY_800654ec[g_OptionsCursor + 2].b = 0x40;

                if ((g_PadButtonWord & 0x4000) != 0)
                {
                    g_OptionsCursor = g_OptionsCursor + 1;
                    if (3 < g_OptionsCursor)
                    {
                        g_OptionsCursor = 0;
                    }
                }

                if ((g_PadButtonWord & 0x1000) != 0)
                {
                    g_OptionsCursor = g_OptionsCursor + -1;
                    if (g_OptionsCursor < 0)
                    {
                        g_OptionsCursor = 3;
                    }
                }

                if ((g_PadButtonWord & 0x8000) != 0)
                {
                    if (g_OptionsCursor == 1)
                    {
                        bVar2 = SharedHighRam.DAT_801ff01c == 0;
                        SharedHighRam.DAT_801ff01c = (ushort)(SharedHighRam.DAT_801ff01c - 1);
                        if (bVar2)
                        {
                            SharedHighRam.DAT_801ff01c = 2;
                        }
                    }
                    else if (g_OptionsCursor < 2)
                    {
                        if (g_OptionsCursor == 0)
                        {
                            SharedHighRam._DAT_801ff01e =
                                (ushort)(SharedHighRam._DAT_801ff01e == 0 ? 1 : 0);
                        }
                    }
                    else if (g_OptionsCursor == 2)
                    {
                        if (DAT_80055b10 == 0)
                        {
                            byte uVar15 = PadInput.GetPadStatus(1);
                            DAT_80055b10 = (uint)(uVar15 == 0 ? 1 : 0);
                        }
                        else
                        {
                            DAT_80055b10 = 0;
                        }
                    }
                    else if (g_OptionsCursor == 3)
                    {
                        DAT_80055b14 = (uint)(DAT_80055b14 == 0 ? 1 : 0);
                    }
                }

                if ((g_PadButtonWord & 0x2000) != 0)
                {
                    if (g_OptionsCursor == 1)
                    {
                        SharedHighRam.DAT_801ff01c = (ushort)(SharedHighRam.DAT_801ff01c + 1);
                        if (2 < SharedHighRam.DAT_801ff01c)
                        {
                            SharedHighRam.DAT_801ff01c = 0;
                        }
                    }
                    else if (g_OptionsCursor < 2)
                    {
                        if (g_OptionsCursor == 0)
                        {
                            SharedHighRam._DAT_801ff01e =
                                (ushort)(SharedHighRam._DAT_801ff01e == 0 ? 1 : 0);
                        }
                    }
                    else if (g_OptionsCursor == 2)
                    {
                        if (DAT_80055b10 == 0)
                        {
                            byte uVar15 = PadInput.GetPadStatus(1);
                            DAT_80055b10 = (uint)(uVar15 == 0 ? 1 : 0);
                        }
                        else
                        {
                            DAT_80055b10 = 0;
                        }
                    }
                    else if (g_OptionsCursor == 3)
                    {
                        DAT_80055b14 = (uint)(DAT_80055b14 == 0 ? 1 : 0);
                    }
                }

                if (((g_PadButtonWord & 0x20) != 0) && (g_OptionsCursor != 1))
                {
                    if (g_OptionsCursor < 2)
                    {
                        if ((g_OptionsCursor == 0) && ((SharedHighRam.g_OptionsRecord64 & 4) != 0))
                        {
                            SoundTestScreen.RunSoundTestScreen();
                        }
                    }
                    else if (g_OptionsCursor == 2)
                    {
                        ButtonConfigScreen.RunButtonConfigScreen(DAT_80055b10);
                        g_PadButtonWord = 0;
                    }
                    else if (g_OptionsCursor == 3)
                    {
                        OptionsScreen.FUN_80031c8c(DAT_80055b14);
                    }
                }

                if ((g_PadButtonWord & 0x40) != 0)
                {
                    bVar5 = false;
                }
            }

            if (0xc < iVar20)
            {
                iVar19 = iVar19 % 5;
            }

            GsSPRITE_ARRAY_800654ec[g_OptionsCursor + 2].r = 0x80;
            GsSPRITE_ARRAY_800654ec[g_OptionsCursor + 2].g = 0x80;
            GsSPRITE_ARRAY_800654ec[g_OptionsCursor + 2].b = 0x80;

            GsSPRITE_ARRAY_800654ec[6].x = -0x20;
            if (SharedHighRam._DAT_801ff01e != 0)
            {
                GsSPRITE_ARRAY_800654ec[6].x = 0x3c;
            }

            GsSPRITE_ARRAY_800654ec[6].v = 0xd0;
            if (SharedHighRam._DAT_801ff01e != 0)
            {
                GsSPRITE_ARRAY_800654ec[6].v = 0xe0;
            }

            uVar16 = SharedHighRam.DAT_801ff01c;
            GsSPRITE_ARRAY_800654ec[9].x = RStack_40[uVar16];
            GsSPRITE_ARRAY_800654ec[9].u = (byte)RStack_38[uVar16];
            GsSPRITE_ARRAY_800654ec[9].v = (byte)((sbyte)SharedHighRam.DAT_801ff01c * 0x10 + 0x30);
            GsSPRITE_ARRAY_800654ec[9].w = (ushort)RStack_30[uVar16];

            GsSPRITE_ARRAY_800654ec[0xd].x = -0x20;
            if (DAT_80055b10 != 0)
            {
                GsSPRITE_ARRAY_800654ec[0xd].x = 0x1c;
            }

            GsSPRITE_ARRAY_800654ec[0x10].x = -0x28;
            GsSPRITE_ARRAY_800654ec[0xd].u = (byte)(DAT_80055b10 << 5);
            if (DAT_80055b14 != 0)
            {
                GsSPRITE_ARRAY_800654ec[0x10].x = 0x14;
            }

            GsSPRITE_ARRAY_800654ec[0x10].v = (byte)(DAT_80055b14 << 4);

            FrameStep.DrawFrame();
        }
        while (bVar5);

        OptionsScreen.UnwindOptionsScreen();
        SelectScreen.InitializeSpriteArray(GsSPRITE_ARRAY_800654ec, 1, 0x13);

        // 0x80031B80-0x80031C3C: sprite 0x3B is copied wholesale onto sprite 0x28, then hidden. The
        // compiler emitted it as a pointer walk in sixteen-byte steps plus a four-byte tail, which
        // is why Ghidra shows the same field names twice; the effect is one thirty-six-byte copy.
        LibGs.GsSPRITE pGVar17 = GsSPRITE_ARRAY_800654ec[0x3b];
        LibGs.GsSPRITE pGVar18 = GsSPRITE_ARRAY_800654ec[0x28];
        pGVar18.attribute = pGVar17.attribute;
        pGVar18.x = pGVar17.x;
        pGVar18.y = pGVar17.y;
        pGVar18.w = pGVar17.w;
        pGVar18.h = pGVar17.h;
        pGVar18.tpage = pGVar17.tpage;
        pGVar18.u = pGVar17.u;
        pGVar18.v = pGVar17.v;
        pGVar18.cx = pGVar17.cx;
        pGVar18.cy = pGVar17.cy;
        pGVar18.r = pGVar17.r;
        pGVar18.g = pGVar17.g;
        pGVar18.b = pGVar17.b;
        pGVar18.mx = pGVar17.mx;
        pGVar18.my = pGVar17.my;
        pGVar18.scalex = pGVar17.scalex;
        pGVar18.scaley = pGVar17.scaley;
        pGVar18.rotate = pGVar17.rotate;

        GsSPRITE_ARRAY_800654ec[0x3b].attribute = 0x80000000;
    }

    // GHIDRA: __main @ 0x8003486C
    private static void __main()
    {
        // PARTIAL: compiler runtime initialization is provided by the CLR.
    }

    // GHIDRA: exit @ 0x8004EAC4
    private static void exit(int param_1)
    {
        // PARTIAL: PsxSdkMonogame has no `exit`, so it is stood in for here rather than open-coded
        // into a game file's control flow. The call is unreachable anyway — see the case -1 note
        // on main — and on the console it would return into start and hit trap(1).
        _ = param_1;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: resolves the PSX ranges this overlay models, in the order the console would: the
    // overlay's own buffers first, then the cross-overlay high RAM at 0x801FF000, then the SNMAIN
    // heap. THE ORDER IS LOAD-BEARING. start arms a heap of 0x0078E75C bytes at 0x800692A0, which
    // on the console spans everything above it including 0x80080000, 0x80090000, 0x800B0000 and
    // 0x801FF000 — the game uses those addresses as raw scratch inside heap space it never
    // allocates from. PsxHeap.Resolve would therefore answer for all of them, so it must come
    // last.
    internal static (byte[] Buffer, int Offset)? ResolveAddress(int address)
    {
        return SelectScreen.Resolve(address)
               ?? SharedHighRam.Resolve(address)
               ?? PsxHeap.Resolve(address);
    }
}
