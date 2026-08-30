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
// through OverlayExit.FUN_8003472c. There is no fourth exit: the whole program contains exactly
// three "cdrom:" strings and exactly one LoadExec call site.
//
// THIS OVERLAY IS NOT BUILT LIKE TITLE.EXE, and none of TITLE.EXE's engine transfers:
//   * it draws through Sony's high-level libgs (GsOT / GsSPRITE / GsLINE / GsBOXF). TITLE.EXE
//     links no libgs at all and hand-rolls DRAWENV/DISPENV/OT over libgpu;
//   * it has NO task scheduler. There is no CreateTask / ExecuteTaskList and no indirect dispatch
//     anywhere in the game half of .text (0x800213A8..0x80034FFF);
//   * it has NO frame loop. It has a frame STEP, FUN_800344a4 @ 0x800344A4 with 61 call sites,
//     which the screen bodies call inline from their own blocking do/while loops;
//   * its heap is armed once in start and NEVER USED: SELECT.EXE calls neither malloc nor free.
//     The only allocator that runs is SpuInitMalloc over SPU RAM.
//
// SCOPE OF THIS FILE: start and main. The boot chain around them is in SelectScreen.cs
// (the graphics/CD bring-up, the USAGI.B load, ClearVram, the GsSPRITE initialiser),
// Decompressor.cs and OverlayExit.cs; the frame step is in FrameStep.cs and the CD-DA service it
// ends with is in CdAudio.cs. Every screen body and the menu driver are a later slice and are
// BLOCKED stubs here, each naming its address.
internal sealed class SELECT_EXE_exe
{
    // GHIDRA: DAT_800692a0 @ 0x800692A0
    // The heap base start hands to InitHeap. It is the first word past the end of .bss
    // (0x8006929C) plus one word.
    private const int HeapBaseAddress = unchecked((int)0x800692A0);

    // GHIDRA: DAT_80055a78 @ 0x80055A78
    // First word start's zero loop clears. It is the first byte of .sbss.
    private const int BssClearFirst = unchecked((int)0x80055A78);

    // GHIDRA: DAT_8006929c @ 0x8006929C
    // The loop's exclusive limit and the value start publishes into DAT_8004f808. It is one past
    // the last byte of .bss (measured: .bss is 0x80055B88..0x8006929B).
    private const int BssClearLimit = unchecked((int)0x8006929C);

    // GHIDRA: DAT_801ff018 @ 0x801FF018
    // The persistent options word in the cross-overlay block, read by main line 46 for bit 1.
    // Modelled by SharedHighRam, which ResolveAddress chains, so it is read here as the raw PSX
    // address the original reads.
    private const int DAT_801ff018_Address = unchecked((int)0x801FF018);

    // GHIDRA: DAT_8004f828 @ 0x8004F828
    // .data, image value 0x00008000 — the stack size SNMAIN reserves. Read from the image with
    // read-memory: `00 80 00 00` at 0x8004F828.
    private static uint DAT_8004f828 = 0x00008000;

    // GHIDRA: DAT_8004f82c @ 0x8004F82C
    // .data, image value 0x00800000 — the stack-top offset. Read from the image with read-memory:
    // `00 00 80 00` at 0x8004F82C. SP becomes (0x00800000 - 8) | 0x80000000 = 0x807FFFF8.
    private static uint DAT_8004f82c = 0x00800000;

    // GHIDRA: DAT_8004f80c @ 0x8004F80C
    // .data, image value 0. start computes the heap SIZE into it.
    private static uint DAT_8004f80c;

    // GHIDRA: DAT_8004f808 @ 0x8004F808
    // .data. start publishes &DAT_8006929c here. PARTIAL: nothing on this slice's path reads it
    // back, so what the SN runtime uses it for is not closed; it is kept because the store is.
    internal static int DAT_8004f808;

    // GHIDRA: DAT_80055b1c @ 0x80055B1C
    // .sbss. start stores its own return address here (`sw a0, -0x7f8(at)` at 0x80034834, with a0
    // holding the value the entry point was reached with).
    // PARTIAL: on desktop there is no caller return address to store. The store is kept and the
    // value is 0, because nothing in SELECT.EXE reads it back on this slice's path.
    internal static int DAT_80055b1c;

    // GHIDRA: DAT_80055b50 @ 0x80055B50
    // .sbss, SIXTEEN BITS (Ghidra types it undefined2 and main's second store is
    // `(undefined2)iVar2`). main writes 0xFFFF before the memory-card browser and then writes the
    // menu state into it once per outer iteration.
    internal static ushort DAT_80055b50;

    // GHIDRA: DAT_80055b6c @ 0x80055B6C
    // .sbss, 32 bits. The recon reads it as the cached pad word; main only zeroes it.
    // PARTIAL: its readers are all in the screen bodies, which are not in this slice.
    internal static int DAT_80055b6c;

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

    // GHIDRA: DAT_80065350 @ 0x80065350
    // GsOT[0]'s tag array. EIGHT tags of four bytes: main sets GsOT[0].length = 3, and libgs's
    // GsClearOt clears `1 << (length & 0x1f)` entries. The extent is closed independently by the
    // second array's address — 0x80065370 - 0x80065350 = 0x20 = 8 * 4.
    // A LibGpu.RamRegion because GsClearOt hands GsOT.org to LibGpu.ClearOTagR and GsDrawOt hands
    // GsOT.tag to LibGpu.DrawOTag, both of which take PSX addresses and resolve them.
    internal static readonly byte[] DAT_80065350 = RamRegion(unchecked((int)0x80065350), 32);

    // GHIDRA: DAT_80065370 @ 0x80065370
    // GsOT[1]'s tag array, same size and same reasoning. 0x80065370 + 0x20 = 0x80065390, four
    // bytes below GsOT[0]'s handle at 0x800654C4 — no named global falls between.
    internal static readonly byte[] DAT_80065370 = RamRegion(unchecked((int)0x80065370), 32);

    // GHIDRA: DAT_800654c4 @ 0x800654C4
    // The two libgs ordering-table handles, armed BY HAND in main lines 18..21 — this overlay
    // never calls a libgs routine that would build them. Two elements, because the frame step
    // indexes them as `&DAT_800654c4 + GsGetActiveBuff() * 5` on an int * and 0x800654D8 -
    // 0x800654C4 = 0x14 = sizeof(GsOT).
    internal static readonly LibGs.GsOT[] GsOT_800654c4 =
    {
        new LibGs.GsOT(),
        new LibGs.GsOT(),
    };

    // GHIDRA: GsSPRITE_ARRAY_800654ec @ 0x800654EC
    // ONE HUNDRED sprites of 36 bytes, 0x800654EC..0x800662FB. The count is main's own argument to
    // FUN_80030848 and the frame step's own sort bound; the stride is closed twice — FUN_80030848
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
        // while (puVar1 < &DAT_8006929c);` — 0x13824 bytes of .sbss + .bss, one word at a time.
        //
        // PARTIAL: this port models .sbss/.bss as C# statics and as byte[] regions, every one of
        // which the CLR zero-initialises before first use, so the loop has nothing left to zero.
        // The range is spelled out above (BssClearFirst..BssClearLimit) rather than implied. This
        // is the same treatment __main and _96_init get across the ported overlays.

        DAT_8004f80c = ((DAT_8004f82c - 8U) - DAT_8004f828) - 0x6929c;
        DAT_8004f808 = BssClearLimit;
        DAT_80055b1c = 0;

        // 0x0078E75C bytes at 0x800692A0 — a dev-kit-sized heap on 2 MB retail hardware, exactly
        // as in TITLE.EXE. IT IS NEVER USED: SELECT.EXE calls neither malloc nor free anywhere
        // (measured: the only malloc/free symbols in the program are SpuInitMalloc / SpuMalloc /
        // SpuFree over SPU RAM). The call is kept because start makes it.
        InitHeap(HeapBaseAddress, (int)DAT_8004f80c);

        main();

        // trap(1) — `break 0x1`. Control never returns from it on the console.
        // PARTIAL: modelled as a plain return, because main here is unreachable-by-exit anyway
        // (see the note on case -1 in main) and the desktop host owns thread teardown.
    }

    // GHIDRA: main @ 0x8003045C
    // 572 bytes, 75 lines, 28 callees. Lines 8..17 are TITLE.EXE's main prologue call for call.
    //
    // CASE -1 IS UNREACHABLE, AND IT IS REPRODUCED ANYWAY — rule 12. The switch value comes from
    // FUN_80030a6c, which tail-calls the menu driver FUN_800283a0 @ 0x800283A0 and returns
    // DAT_80055a0c. find-cross-references on DAT_80055a0c reports 14 references and every single
    // one is inside FUN_800283a0, which clamps it to [0, itemCount - 1]. Nothing else can write a
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
        SelectScreen.FUN_800308bc();
        PadInit(0);
        CdInit();
        ExitCriticalSection();

        // Lines 18..21: the two GsOT handles, armed by hand. Only .org and .length are written;
        // GsClearOt fills .offset, .point and .tag every frame.
        GsOT_800654c4[0].org = RamAddressOf(DAT_80065350, 0);
        GsOT_800654c4[0].length = 3;
        GsOT_800654c4[1].length = 3;
        GsOT_800654c4[1].org = RamAddressOf(DAT_80065370, 0);

        SelectScreen.FUN_80030698();
        OverlayExit.FUN_80034380();
        FUN_80021ce4();
        SharedHighRam.DAT_801ff068 = FUN_80021e34(0);
        bVar1 = false;
        if (SharedHighRam.DAT_801ff068 == 0)
        {
            DAT_80055b50 = 0xffff;
            iVar2 = FUN_80021618();
            if (iVar2 == 0)
            {
                SharedHighRam.DAT_801ff068 = 2;
            }
        }

        DAT_80055b6c = 0;
        DAT_80055b80 = DAT_80055b80 | 4;
        do
        {
            if ((DAT_80055b80 & 4) != 0)
            {
                DAT_80055b80 = 0;
                SelectScreen.FUN_80030848(GsSPRITE_ARRAY_800654ec, 0, 100);
                SelectScreen.FUN_80030908();
                FUN_8002ea8c();

                // 0x80065AB0 = GsSPRITE_ARRAY_800654ec + 36 * 41, i.e. element 41 of the same
                // array, twelve entries.
                SelectScreen.FUN_80030848(GsSPRITE_ARRAY_800654ec, 41, 0xc);
                DAT_80055b80 = DAT_80055b80 & unchecked((int)0xfffffffb);
            }

            iVar2 = FUN_80030a6c();

            // Bit 1 of the options word gates menu item 2. When it is clear, item 2 is redirected
            // to state 3 instead. The same gate appears inside FUN_80030a6c, where it swaps two
            // sprites' UV and one sprite's attribute.
            // The read goes through the raw PSX address the original reads, which
            // SELECT_EXE_exe.ResolveAddress answers for by chaining SharedHighRam — so it needs
            // PsxSdkBridges.ActivateSelectExe to have installed this overlay's resolver, exactly
            // as every other raw-address read in the ported overlays does.
            // PARTIAL: what the bit means is NOT ESTABLISHED. It is written by the memory-card
            // load path (FUN_80021618 copies a 0x80-byte record over 0x801FF018) and by
            // FUN_80031c8c; neither is in this slice.
            if (((PsxRam.ReadI32(DAT_801ff018_Address) & 2) == 0) && (iVar2 == 2))
            {
                iVar2 = 3;
            }

            FrameStep.FUN_800344a4();
            DAT_80055b50 = (ushort)iVar2;
            switch (iVar2)
            {
                case 0:
                    FUN_80030af8();
                    break;
                case 1:
                    FUN_80030ef8();
                    break;
                case 2:
                    FUN_800310a8();
                    break;
                case 3:
                    FUN_800315c0();
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

    // GHIDRA: FUN_80021ce4 @ 0x80021CE4
    private static void FUN_80021ce4()
    {
        // BLOCKED: the memory-card bring-up. Body is InitCARD(1); StartCARD(); _bu_init();
        // FUN_80022348(); _card_auto(0); ChangeClearPAD(0); FUN_800213a8() — the same seven calls
        // in the same order as TITLE.EXE's InitializeMemoryCard @ 0x80022630, which is BLOCKED in
        // TITLE_EXE/TitleScreenTask.cs for the same reason.
        // FUN_80022348 @ 0x80022348 opens eight kernel events (0xF4000001 and 0xF0000011, specs
        // 0x0004/0x8000/0x0100/0x2000) and the module then spins in FUN_800221d0 @ 0x800221D0
        // until one of four TestEvent calls returns 1.
        //
        // THAT IS NO LONGER WHAT BLOCKS IT. LibApi now carries a real kernel event table:
        // TestEvent returns 1 when a matching DeliverEvent has landed on an open descriptor, and
        // _card_info, _card_clear and _card_load deliver synchronously - so the poll terminates.
        // What is missing is only the transliteration itself: this module is a later slice.
        // Nothing is started here, which is why FUN_80021d34's teardown in OverlayExit tears
        // nothing down.
    }

    // GHIDRA: FUN_80021e34 @ 0x80021E34
    private static int FUN_80021e34(int param_1)
    {
        // BLOCKED: the memory-card probe — FUN_800222b8 (four TestEvent), _card_info(chan),
        // FUN_800221d0, then optionally _card_clear and a _card_load pass. Same TestEvent blocker
        // as FUN_80021ce4 above. Its result is the card status code main stores at 0x801FF068.
        // Returning 0 here is NOT a chosen "no card" answer — it is C#'s default for an
        // untransliterated body, and main's `if (result == 0)` arm then runs FUN_80021618, which
        // is BLOCKED too. Stated so the value is not read as evidence.
        _ = param_1;
        return default;
    }

    // GHIDRA: FUN_80021618 @ 0x80021618
    private static int FUN_80021618()
    {
        // BLOCKED: the card save-slot browser, 700 bytes. It drives the frame step FUN_800344a4
        // and VSync directly and ends by copying the 0x80-byte record FUN_80022810 read off the
        // card into 0x801FF018. Same memory-card module, same TestEvent blocker.
        return default;
    }

    // GHIDRA: FUN_8002ea8c @ 0x8002EA8C
    private static void FUN_8002ea8c()
    {
        // BLOCKED: the select-screen intro animation, 6608 bytes / 916 lines, ending at
        // 0x8003045B immediately before main. It calls the frame step FUN_800344a4 fourteen times
        // inline and uses libgcc softfloat (__floatsidf / __subdf3 / __fixdfsi) for its easing.
        // A later slice owns it.
    }

    // GHIDRA: FUN_80030a6c @ 0x80030A6C
    private static int FUN_80030a6c()
    {
        // BLOCKED: 140 bytes. It patches GsSPRITE_ARRAY_800654ec[0x17].v, [0x18].attribute,
        // [0x1b].u and [0x1b].cy according to bit 1 of 0x801FF018, then tail-calls the menu driver
        // FUN_800283a0 @ 0x800283A0 (1944 bytes) and returns DAT_80055a0c @ 0x80055A0C.
        // FUN_800283a0 is the mode menu, which this slice is told not to port; it also needs
        // bit-exact libgcc softfloat for its easing curves, which is an open decision.
        // Returning 0 is C#'s default for an untransliterated body, not a chosen menu state.
        //
        // CONSEQUENCE, NOW THAT THE FRAME STEP IS REAL: main's do/while presents a frame per pass
        // (FrameStep.FUN_800344a4 ends with DrawSync/VSync/GsSwapDispBuff/GsDrawOt), then takes
        // case 0 every time, whose body FUN_80030af8 is still BLOCKED. So the loop no longer spins
        // free — it runs at the VSync rate — but it still has no exit, because the three real exits
        // are the LoadExec calls inside the BLOCKED screen bodies.
        return default;
    }

    // GHIDRA: FUN_80030af8 @ 0x80030AF8
    private static void FUN_80030af8()
    {
        // BLOCKED: state 0, the DEMO screen body, 1024 bytes. Reads the three 8-byte save records
        // at 0x801FF200/208/210, drives a 3-slot chooser through FUN_80033d34 @ 0x80033D34, writes
        // the launch parameters to 0x801FF000..0x801FF006 and exits through
        // OverlayExit.FUN_8003472c("cdrom:\\DEMO.EXE;1").
    }

    // GHIDRA: FUN_80030ef8 @ 0x80030EF8
    private static void FUN_80030ef8()
    {
        // BLOCKED: state 1, the VS screen body, 432 bytes. Drives FUN_80031e98 @ 0x80031E98 (the
        // 48x48 4bpp character grid — explicitly out of scope for this slice), writes the seven
        // u16 VS roster at 0x801FF100..0x801FF10D and exits through
        // OverlayExit.FUN_8003472c("cdrom:\\VS.EXE;1").
    }

    // GHIDRA: FUN_800310a8 @ 0x800310A8
    private static void FUN_800310a8()
    {
        // BLOCKED: state 2, the SP screen body, 1304 bytes. Same shape as FUN_80030af8 against the
        // three 16-byte records at 0x801FF218/228/238, uses MoveImage twice, writes the launch
        // parameters to 0x801FF008..0x801FF017 and exits through
        // OverlayExit.FUN_8003472c("cdrom:\\SP.EXE;1").
    }

    // GHIDRA: FUN_800315c0 @ 0x800315C0
    private static void FUN_800315c0()
    {
        // BLOCKED: state 3, the in-place options sub-screen, 1668 bytes. It does NOT LoadExec — it
        // returns to main's loop. It is the only path that reaches the sound-bank loader chain
        // FUN_80026420 -> FUN_80022994, i.e. the five \SOUND\*.B files, and PsxSdkMonogame's
        // LibSnd is entirely unimplemented.
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
