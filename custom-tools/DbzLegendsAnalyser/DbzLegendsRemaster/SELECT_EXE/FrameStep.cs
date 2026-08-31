using PsxSdkMonogame;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE FRAME STEP of SELECT.EXE, and the GsBOXF array only it and FUN_80027a58 touch.
//
// DrawFrame @ 0x800344A4 is the ONLY place SELECT.EXE draws. Sixty-one call sites, and NOT ONE
// of them is a frame loop: this overlay has no scheduler and no dispatcher, so every screen body
// sits in its own blocking do/while and calls this once per frame to present. main @ 0x8003045C
// calls it once per outer iteration; FUN_8002ea8c @ 0x8002EA8C (the intro animation) calls it
// fourteen times inline; the menu driver RunModeMenu @ 0x800283A0 twice inside its loop.
//
// It sits in SELECT.EXE's frame/shutdown module, the four functions emitted at 0x800344A4
// (this one), 0x8003472C (OverlayExit.ShutdownAndLoadExecutable), 0x800347C4 (start) and 0x8003486C (__main).
//
// WHAT IT DOES, in order. Twelve calls: nine are libgs, in PsxSdkMonogame/LibGs.cs,
// which was transliterated from this overlay for exactly this function:
//   GsGetActiveBuff -> GsSetWorkBase(active * 24000 + 0x800597CC) -> GsClearOt -> four GsSortLine
//   -> the sprite pass OR the boxfill pass -> DrawSync(0) -> VSync(0) -> ResetGraph(1)
//   -> GsSwapDispBuff -> GsSortClear (unless bit 0) -> GsDrawOt -> UpdateCdAudio.
//
// TWO THINGS ABOUT THE COORDINATES THAT ARE SET UP ELSEWHERE AND APPLY TO EVERY SPRITE SORTED HERE:
//   * FUN_80030698 @ 0x80030698 calls GsInit3D, which sets libgs's sort origin to the screen centre
//     (LibGs.DAT_800593b0 = width / 2 = 160, DAT_800593b2 = height / 2 = 120). A GsSPRITE's x and y
//     are therefore OFFSETS FROM (160,120), plus the target buffer's VRAM origin. That is why
//     FUN_80027a58 @ 0x80027A58 arms the full-screen boxfill below at x = -160, y = -120, w = 320,
//     h = 240: those four numbers are the whole 320x240 screen expressed from its centre.
//   * LibGs.GsSetDrawBuffOffset publishes that origin from the OPPOSITE buffer index to the one
//     GsSetDrawBuffClip uses. That is deliberate and it is documented at the top of LibGs.cs: the
//     offset it publishes is consumed by the NEXT frame's sort, and by then the two agree.
internal static class FrameStep
{
    // GHIDRA: g_GsBoxfArray5 @ 0x80067B68
    // FIVE GsBOXF of sixteen bytes, 0x80067B68..0x80067BB7. The count and the stride are this
    // function's own — the boxfill pass below walks `bp = (GsBOXF *)&g_GsBoxfArray5` five times with
    // `bp = bp + 1`. Both ends are closed: 0x80067B68 is the address the pass starts from, and
    // 0x80067B68 + 5 * 16 = 0x80067BB8 is DAT_80067bb8, a libsnd global written by FUN_80039ee4
    // @ 0x80039EE4 and FUN_8003a250 @ 0x8003A250 and read by FUN_8003b05c @ 0x8003B05C.
    //
    // ONLY ELEMENT 0 IS EVER WRITTEN, and only by FUN_80027a58 @ 0x80027A58 lines 231..242:
    //     DAT_80067b70 = 0x140;      // [0].w  = 320
    //     DAT_80067b6c = 0xff60;     // [0].x  = -160
    //     DAT_80067b6e = 0xff88;     // [0].y  = -120
    //     DAT_80067b72 = 0xf0;       // [0].h  = 240
    //     DAT_80067b74/75/76 = 0,0,1 // [0].r/g/b
    //     g_GsBoxfArray5 = 0x40000000; // [0].attribute, then 0x80000000 when sprite[0].attribute != 0
    // Elements 1..4 have NO writer anywhere in the program, so they stay zero — and zero is a
    // POSITIVE attribute, which LibGs.GsSortBoxFill does not reject (unlike GsSortSprite it has no
    // w == 0 early-out). Four zero-area tiles are therefore spliced into the ordering table every
    // time the boxfill path runs. That is the original's own behaviour and it is reproduced, not
    // corrected — rule 12.
    //
    // NOT REGISTERED AS A LibGpu.RamRegion, and neither are the GsSPRITE / GsLINE / GsOT arrays —
    // see the note on the sprite pass below.
    internal static readonly LibGs.GsBOXF[] GsBOXF_ARRAY_80067b68 =
    {
        new LibGs.GsBOXF(),
        new LibGs.GsBOXF(),
        new LibGs.GsBOXF(),
        new LibGs.GsBOXF(),
        new LibGs.GsBOXF(),
    };

    // GHIDRA: DrawFrame @ 0x800344A4
    // 648 bytes, 61 call sites, no loop of its own.
    //
    // ON THE MEMORY MODEL, because it is the thing that decides whether anything reaches the screen:
    // what gets spliced into the ordering table is never a GsSPRITE, a GsLINE, a GsBOXF or a GsOT.
    // It is the libgpu PACKET that LibGs.GsSortSprite / GsSortLine / GsSortBoxFill build in the work
    // area at 0x800597CC, and LibGs._make_packet resolves exactly two raw addresses: the work-area
    // cursor DAT_80059430 and the ordering-table bucket GsOT.org + index * 4. Both of those are
    // already LibGpu.RamRegion declarations —
    //     the 48000-byte work area          LibGs.DAT_800597cc  @ 0x800597CC
    //     GsOT[0]'s tag array               SELECT_EXE_exe.g_OrderingTableTags0 @ 0x80065350
    //     GsOT[1]'s tag array               SELECT_EXE_exe.g_OrderingTableTags1 @ 0x80065370
    // and the two block-fill packets GsSortClear links are LibGs.DAT_80058c90 / DAT_80058ca0.
    // The four sprite/line/box/OT structures are passed BY REFERENCE to routines that read their
    // fields; nothing in the frame step, in libgs or in the rasterizer ever turns 0x800654EC,
    // 0x80065484, 0x800654C4 or 0x80067B68 back into memory. Declaring byte[] regions for them
    // would create a second, disjoint copy of the same PSX memory that no reader consumes, so they
    // are not declared. PARTIAL, and it is a real gap for a LATER slice, not for this one:
    // FUN_80030698 @ 0x80030698 writes thirty-five GsSPRITE ADDRESSES (0x80065D5C stepping by 0x24,
    // i.e. elements 60..94) into the table at 0x80058E08, and whichever screen body reads that
    // table back will need an address-to-element bridge that does not exist yet.
    internal static void DrawFrame()
    {
        int iVar1 = LibGs.GsGetActiveBuff();

        // 0x800597CC, which Ghidra renders as the negative displacement -0x7ffa6834. Two 24000-byte
        // packet areas, one per display buffer. LibGs registers the whole 48000 bytes.
        LibGs.GsSetWorkBase(iVar1 * 24000 + unchecked((int)0x800597CC));

        // `otp = (GsOT *)(&g_GsOtArray2 + iVar1 * 5)` — five words is sizeof(GsOT), so this is
        // GsOT[activeBuf] of the two-element array main @ 0x8003045C armed by hand.
        LibGs.GsOT otp = SELECT_EXE_exe.GsOT_800654c4[iVar1];

        LibGs.GsClearOt(0, 0, otp);
        LibGs.GsSortLine(SelectScreen.g_GsLineArray4[0], otp, 1);   // &g_GsLineArray4
        LibGs.GsSortLine(SelectScreen.g_GsLineArray4[1], otp, 1);   // &DAT_80065494
        LibGs.GsSortLine(SelectScreen.g_GsLineArray4[2], otp, 1);   // &DAT_800654a4
        LibGs.GsSortLine(SelectScreen.g_GsLineArray4[3], otp, 1);   // &DAT_800654b4

        // FUN_80030698 arms all four GsLINE with attribute 0x80000000, and LibGs.GsSortLine gates
        // its whole body on `if (-1 < (int)attribute)`. As armed, all four are SUPPRESSED every
        // frame. Reproduced, not corrected — rule 12.

        int iVar2;
        if ((SELECT_EXE_exe.DAT_80055b80 & 8) == 0)
        {
            // THE SPRITE PASS. The bound is re-read from DAT_80055b80 EVERY ITERATION, which is why
            // this is written as the original's four branches rather than as a for loop: bit 1 set
            // means 0x62 sprites, bit 1 clear means 100, and a write to the flag word mid-pass would
            // change the bound mid-pass. The four tests below are, instruction for instruction:
            //     0x80034578  andi v0,v1,0x2   / 0x8003457C  bne v0,zero,0x80034590
            //     0x80034584  slti v0,s0,0x64  / 0x80034588  beq v0,zero,0x8003468C
            //     0x800345D0  andi v0,v0,0x2   / 0x800345D4  beq v0,zero,0x80034584
            //     0x800345DC  slti v0,s0,0x62  / 0x800345E0  bne v0,zero,0x80034594
            // Ghidra renders the same shape as a while(iVar2 < 100) wrapping a while(true) whose
            // body carries the label LAB_80034594; C# forbids a goto INTO a block, so the branches
            // are spelled out flat here. Neither the order nor the number of tests changes.
            iVar2 = 0;
            if ((SELECT_EXE_exe.DAT_80055b80 & 2) != 0)
            {
                goto LAB_80034594;
            }

        LAB_80034584:
            if (100 <= iVar2)
            {
                goto LAB_8003468c;
            }

        LAB_80034594:
            LibGs.GsSortSprite(
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar2],
                SELECT_EXE_exe.GsOT_800654c4[iVar1],
                1);
            iVar2 = iVar2 + 1;
            if ((SELECT_EXE_exe.DAT_80055b80 & 2) == 0)
            {
                goto LAB_80034584;
            }

            if (iVar2 < 0x62)
            {
                goto LAB_80034594;
            }

            goto LAB_8003468c;
        }
        else
        {
            // THE BOXFILL PASS, taken when bit 3 of DAT_80055b80 is set. FUN_80027a58 @ 0x80027A58
            // is the only writer of that bit — line 251 sets bits 0 and 3 together (`| 9`), calls
            // the frame step twice, and line 256 clears them again (`& 0xFFFFFFF6`). Four sprites,
            // five boxfills, then sprite 4 on top.
            // JUSTIFICATION: C# language bridge only
            // RELATION: `_29` and `bp` are the original's own pointer cursors, walked with
            // `_29 = _29 + 1` and `bp = bp + 1`. They become element indices into the same two
            // arrays; the walk, its start and its two bounds are unchanged.
            iVar2 = 0;
            int _29 = 0;
            do
            {
                LibGs.GsSortSprite(
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[_29],
                    SELECT_EXE_exe.GsOT_800654c4[iVar1],
                    1);
                iVar2 = iVar2 + 1;
                _29 = _29 + 1;
            } while (iVar2 < 4);

            iVar2 = 0;
            int bp = 0;
            do
            {
                LibGs.GsSortBoxFill(
                    GsBOXF_ARRAY_80067b68[bp],
                    SELECT_EXE_exe.GsOT_800654c4[iVar1],
                    1);
                iVar2 = iVar2 + 1;
                bp = bp + 1;
            } while (iVar2 < 5);

            LibGs.GsSortSprite(
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4],
                SELECT_EXE_exe.GsOT_800654c4[iVar1],
                1);
        }

    LAB_8003468c:
        DrawSync(0);
        VSync(0);
        ResetGraph(1);
        LibGs.GsSwapDispBuff();

        // Bit 0 of DAT_80055b80 suppresses the background clear for this frame. FUN_8002cc04
        // @ 0x8002CC04 line 159 and FUN_8002ea8c @ 0x8002EA8C line 408 set it; FUN_8002ea8c line
        // 780 clears it.
        if ((SELECT_EXE_exe.DAT_80055b80 & 1) == 0)
        {
            LibGs.GsSortClear(0, 0, 0, SELECT_EXE_exe.GsOT_800654c4[iVar1]);
        }

        LibGs.GsDrawOt(SELECT_EXE_exe.GsOT_800654c4[iVar1]);
        CdAudio.UpdateCdAudio();
    }
}
