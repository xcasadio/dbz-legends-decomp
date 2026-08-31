using PsxSdkMonogame;
using static PsxSdkMonogame.LibGcc;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE SCREEN-DECORATION MODULE — the run of .text from 0x80029684 to 0x8002EA8B, which is the
// artwork half of the three mode branches in ModeBranches.cs. Six of its seven functions come in
// pairs: a BUILD that arms the sprites for a screen and plays it in, and an UNWIND that plays the
// same screen out when the player cancels. The seventh, FUN_8002cc04, is the hand-off transition
// every branch runs once the choice is made.
//     0x80029684  BuildDemoSaveSlotScreen   build   the DEMO save-slot picker      (unwind UnwindDemoSaveSlotScreen)
//     0x80029F9C  UnwindDemoSaveSlotScreen   unwind
//     0x8002A178  FUN_8002a178   build   the VS sub-menu                (unwind FUN_8002a6f8)
//     0x8002A6F8  FUN_8002a6f8   unwind
//     0x8002A7F4  BuildSpSaveSlotScreen   build   the SP save-slot picker        (unwind UnwindSpSaveSlotScreen)
//     0x8002B174  UnwindSpSaveSlotScreen   unwind
//     0x8002CC04  FUN_8002cc04   the hand-off transition, plus its satellite pass FUN_8002dec0
//                                @ 0x8002DEC0
//
// NOTHING HERE TOUCHES libsnd. Every callee of all seven is accounted for: FrameStep.DrawFrame,
// SelectScreen.InitializeSpriteArray, LibGpu.MoveImage, LibGpu.DrawSync, the four LibGcc soft-float entry
// points and FUN_8002dec0 below. FUN_8002cc04's callee list in Ghidra is exactly
// { FUN_8002dec0, DrawSync, __floatsidf, __muldf3, __divdf3, DrawFrame, InitializeSpriteArray,
//   __fixdfsi, MoveImage } — no SsUtil, no SpuSet, no Vab anything. So none of this slice is
// blocked on LibSnd.cs.
//
// THE SPRITE INDICES ARE ARITHMETIC, NOT GUESSES, and the table is the one ModeBranches.cs states:
//     &GsSPRITE_ARRAY_800654ec = 0x800654EC, stride 36 (0x24)
//     &DAT_80065480 + n  ->  element (n - 0x6C) / 36, field +0x00 (attribute)
//     &g_GsLineArray4 + n  ->  ... +0x04 (x)      &g_GsLineArray4 + n + 2 -> +0x06 (y)
//     &DAT_80065488 + n  ->  ... +0x08 (w)      &DAT_8006548a + n     -> +0x0A (h)
//     &DAT_8006548c + n  ->  ... +0x0C (tpage)  &DAT_8006548e + n     -> +0x0E (u), +1 -> v
//     &DAT_80065490 + n  ->  ... +0x10 (cx)     &DAT_80065492 + n     -> +0x12 (cy)
//     &DAT_80065494 + n  ->  ... +0x14 (r)      &DAT_80065498 + n     -> +0x18 (mx)
//     &DAT_8006549a + n  ->  ... +0x1A (my)
//     (&GsSPRITE_ARRAY_800654ec[0].<field>)[n] -> element n / 36, that field
// With the loops' own cursors that is: the SHIFTED bases at n = 0x168/0x18C/0x1B0 name elements
// 7, 8, 9, and the ARRAY base at the same n names elements 10, 11, 12 — the two rows the pickers
// light up together. n = 0xFC/0x120/0x144 through the array base is elements 7, 8, 9 again.
//
// SPRITES 0x62 AND 99 ARE THE BACKGROUND, and each build stitches it out of two non-contiguous 4bpp
// bands of the USAGI.B sheet. Their `r` is ramped 0 -> 0x80 across the fade-in (and 0x80 -> 0 on
// the way out) with `g` and `b` copied from it, which is how the whole picture dims.
internal static class ScreenDecoration
{
    // GHIDRA: g_SpBuildDigitCellRect @ 0x80055A10
    // .sdata, undefined4, image value 0x00DD0000 (the bytes at 0x80055A10 are 00 00 DD 00, read with
    // read-memory). The FIRST half of the RECT constant BuildSpSaveSlotScreen copies into its stack frame:
    // x = 0x0000, y = 0x00DD. It is the same four-by-sixteen digit cell as ModeBranches.g_SpBranchDigitCellRect,
    // which is the copy RunSpModeScreen makes of the same source RECT.
    private static readonly uint g_SpBuildDigitCellRect = 0x00DD0000;

    // GHIDRA: DAT_80055a14 @ 0x80055A14
    // .sdata, undefined4, image value 0x00100004 (bytes 04 00 10 00). The SECOND half: w = 0x0004,
    // h = 0x0010.
    private static readonly uint DAT_80055a14 = 0x00100004;

    // GHIDRA: g_FullFrameRect320x240 @ 0x80055A30
    // .sdata, undefined4, image value 0x00000000 (bytes 00 00 00 00). First half of the RECT
    // FUN_8002cc04 hands MoveImage: x = 0, y = 0.
    private static readonly uint g_FullFrameRect320x240 = 0x00000000;

    // GHIDRA: DAT_80055a34 @ 0x80055A34
    // .sdata, undefined4, image value 0x00F00140 (bytes 40 01 F0 00). Second half: w = 0x0140,
    // h = 0x00F0 — the whole 320x240 frame, which FUN_8002cc04 copies to VRAM (0x280, 0).
    private static readonly uint DAT_80055a34 = 0x00F00140;

    // GHIDRA: g_ChainLastIndexTable8 @ 0x80055A38
    // .sdata, undefined4, image bytes FF 01 04 08.
    // GHIDRA: DAT_80055a3c @ 0x80055A3C
    // .sdata, undefined4, image bytes 0D 13 1A 22.
    // TOGETHER THEY ARE ONE EIGHT-BYTE TABLE, and FUN_8002cc04 reads it back a BYTE at a time
    // (`(byte)auStack_30[iVar14]`), which is why it is kept as bytes here rather than as two words.
    // WHAT THE EIGHT VALUES ARE: the LAST index, relative to GsSPRITE element 60, of each of the
    // seven chains FUN_80030698 built. Row i holds one leader plus i + 1 satellites, so the running
    // totals are 2, 5, 9, 14, 20, 27, 35 and the last indices are 1, 4, 8, 13, 19, 26, 34 — which is
    // 01 04 08 0D 13 1A 22 exactly. The leading 0xFF is the "one before the first" sentinel; the
    // only loop that indexes this table starts at 1, so 0xFF is never read there.
    private static readonly byte[] g_ChainLastIndexTable8 =
    {
        0xFF, 0x01, 0x04, 0x08, 0x0D, 0x13, 0x1A, 0x22,
    };

    // GHIDRA: BuildDemoSaveSlotScreen @ 0x80029684
    // 2328 bytes. THE DEMO SAVE-SLOT PICKER'S BUILD, called by ModeBranches.RunDemoModeScreen with the
    // preselected cursor and the base of the three eight-byte records at 0x801FF200.
    //
    // Shape: arm the two background sprites, re-initialise elements 1..19, arm the panel (1), the
    // two caption strips (5, 6), the three row icons (7, 8, 9) out of the records, the three row
    // labels (10, 11, 12) and the four selectable rows (16..19); brighten the preselected row; then
    // play the whole thing in over 32 frames, scaling from 0x2000 down to 0x1000 while the
    // background brightens 0 -> 0x7C and the panel spins through eight 30-degree steps.
    internal static void BuildDemoSaveSlotScreen(int param_1, int param_2)
    {
        ushort uVar1;
        bool bVar2;
        int iVar3;
        int iVar4;
        int iVar5;
        short sVar6;
        short sVar7;
        int iVar8;
        int iVar9;

        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].tpage = 0x12;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].x = -0xa0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].u = (byte)'@';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].w = 0x40;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].tpage = 0x13;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].x = -0x60;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].cy = 0x1f1;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].cy = 0x1f1;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].cx = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].attribute = 0x1000000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].cx = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].attribute = 0x1000000;

        // `InitializeSpriteArray(0x80065510, 0x13)` — 0x80065510 is 0x800654EC + 0x24, i.e. element 1.
        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 1, 0x13);
        iVar3 = 1;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar3].tpage = 0xc;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar3].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar3].cy = 0x1fd;
            bVar2 = iVar3 < 0x13;
            iVar3 = iVar3 + 1;
        }
        while (bVar2);

        iVar9 = 0;
        iVar5 = 0x168;
        iVar8 = 10;
        sVar7 = 6;
        sVar6 = 0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].tpage = 0x1d;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x = 0xf;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].y = -0x54;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].w = 0xff;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].h = 0x28;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].cy = 0x1fc;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].mx = 0x7f;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].my = 0x14;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].x = -4;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].v = 0x94;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].w = 0xe8;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].h = 0xc;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].mx = 0x74;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].my = 6;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].v = 0x81;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].h = 0x12;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].mx = unchecked((short)0x80);
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].attribute = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].y = -0x2f;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].attribute = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].x = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].y = -0x2f;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].my = 9;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].attribute = 0;
        iVar3 = 0xfc;
        do
        {
            // iVar3 / 0x24 is element 7, 8, 9 (the row's icon); iVar5 / 0x24 is element 10, 11, 12
            // (the row label). See the table at the top of this file.
            int e7 = iVar3 / 0x24;
            int e10 = iVar5 / 0x24;

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].x = sVar6;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].y = sVar7;

            // The halfword at +2 of each eight-byte record at param_2 = 0x801FF200.
            uVar1 = PsxRam.ReadU16(param_2 + 2);
            param_2 = param_2 + 8;
            sVar7 = (short)(sVar7 + 0x18);
            sVar6 = (short)(sVar6 + 8);
            iVar9 = iVar9 + 1;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].h = 0xd;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].mx = unchecked((short)0xffde);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].v = 0xdd;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].w = 0x10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].my = 5;

            // `(char)uVar1 << 4` stored back into a char keeps the low eight bits.
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].u = (byte)(uVar1 << 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].b = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].g = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].r = 0x40;
            iVar4 = iVar8 + 3;
            iVar8 = iVar8 + 1;

            // `&DAT_80055b78 + iVar4` with iVar4 = 13, 14, 15 is 0x80055B85/86/87 — the availability
            // bytes at index iVar4 - 0xC from 0x80055B84. An absent row gets attribute bit 31, which
            // is how GsSortSprite drops it.
            iVar4 = (int)((uint)(ListCursor.AvailabilityByte(iVar4 - 0xc) == 0 ? 1 : 0) << 0x1f);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].attribute = (uint)iVar4;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].attribute = (uint)iVar4;
            iVar5 = iVar5 + 0x24;
            iVar3 = iVar3 + 0x24;
        }
        while (iVar9 < 3);

        iVar5 = 0;
        sVar7 = 6;
        sVar6 = 0x38;
        iVar3 = 0x168;
        do
        {
            // Array base this time, so iVar3 / 0x24 is element 10, 11, 12.
            int e = iVar3 / 0x24;

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].y = sVar7;
            sVar7 = (short)(sVar7 + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].x = sVar6;
            sVar6 = (short)(sVar6 + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].v = 0xb3;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].w = 0x68;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].h = 0x15;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].mx = 0x34;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].my = 10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].b = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].g = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].r = 0x40;
            iVar5 = iVar5 + 1;
            iVar3 = iVar3 + 0x24;
        }
        while (iVar5 < 3);

        iVar3 = 0;
        if (param_1 != 0)
        {
            // The preselected row, brightened on both of its two sprites. param_1 is 1..3, so the
            // pair is (10, 7), (11, 8) or (12, 9).
            iVar5 = param_1 + 9;
            iVar8 = param_1 + 6;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar5].b = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar5].g = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar5].r = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar8].b = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar8].g = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar8].r = 0x80;
        }

        sVar7 = -0xe;
        sVar6 = -0x18;
        iVar5 = 0x240;
        do
        {
            // 0x240 / 0x24 = element 16 — the first of the four selectable rows.
            int e = iVar5 / 0x24;

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].y = sVar7;
            sVar7 = (short)(sVar7 + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].x = sVar6;
            sVar6 = (short)(sVar6 + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].w = 0xe0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].h = 0x15;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].b = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].g = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].r = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].mx = 0x60;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].my = 10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].attribute = 0;
            iVar3 = iVar3 + 1;
            iVar5 = iVar5 + 0x24;
        }
        while (iVar3 < 4);

        iVar3 = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].v = (byte)'W';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].w = 0xc0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].v = 0x18;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].v = (byte)'-';
        param_1 = param_1 + 0x10;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].v = (byte)'B';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1].r = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1].g = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1].b = 0x80;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r = (byte)iVar3;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x + 4);
            iVar5 = iVar3;
            if (iVar3 < 0)
            {
                iVar5 = iVar3 + 3;
            }

            iVar5 = iVar5 >> 2;
            iVar8 = iVar5;
            if (iVar5 < 0)
            {
                iVar8 = iVar5 + 7;
            }

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley = (short)((short)iVar3 * 0x20);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex =
                (short)(((short)iVar3 * -0x20) + 0x2000);

            // `iVar5 + (iVar8 >> 3) * -8` is iVar5 % 8 for the non-negative iVar5 this loop makes:
            // the eight 30-degree steps the panel spins through. 0x16800 is 360 degrees / 8 in the
            // 1/4096-turn units GsSortSprite's rotate field takes.
            iVar5 = iVar5 + ((iVar8 >> 3) * -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].rotate = (7 - iVar5) * 0x16800;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate = iVar5 * 0x16800;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].r =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            FrameStep.DrawFrame();
            iVar3 = iVar3 + 4;
        }
        while (iVar3 < 0x80);

        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].rotate = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].scalex = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley = 0x1000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex = 0x1000;
        FrameStep.DrawFrame();
    }

    // GHIDRA: UnwindDemoSaveSlotScreen @ 0x80029F9C
    // 476 bytes. THE DEMO PICKER'S CANCEL UNWIND — the exact reverse of BuildDemoSaveSlotScreen's closing
    // animation: 32 frames counting 0x80 down to 4, the same eight-step spin, the same sprite
    // offsets negated, and the background dimmed back to 0. It ends by re-initialising elements
    // 1..19, which is what leaves the mode menu a clean array to rebuild into.
    // The scale here is `iVar3 << 5` on both axes rather than BuildDemoSaveSlotScreen's asymmetric pair, so the
    // panel shrinks to nothing instead of settling at 0x1000.
    internal static void UnwindDemoSaveSlotScreen()
    {
        int iVar1;
        int iVar2;
        int iVar3;

        iVar3 = 0x80;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r = (byte)iVar3;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x + -4);
            iVar1 = iVar3;
            if (iVar3 < 0)
            {
                iVar1 = iVar3 + 3;
            }

            iVar1 = iVar1 >> 2;
            iVar2 = iVar1;
            if (iVar1 < 0)
            {
                iVar2 = iVar1 + 7;
            }

            iVar1 = iVar1 + ((iVar2 >> 3) * -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].rotate = (7 - iVar1) * 0x16800;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate = iVar1 * 0x16800;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex = (short)(iVar3 << 5);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].scaley =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].rotate =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].rotate;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].r =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            FrameStep.DrawFrame();
            iVar3 = iVar3 + -4;
        }
        while (0 < iVar3);

        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 1, 0x13);
    }

    // GHIDRA: FUN_8002a178 @ 0x8002A178
    // 1408 bytes. THE VS SUB-MENU'S BUILD.
    //
    // NOTE ON param_1 — THE ORIGINAL IGNORES IT. ModeBranches.RunVsModeScreen passes g_VsSubMenuCursor in
    // a0, but the first thing this function does is `jal InitializeSpriteArray` with a0 = 0x80065510, and
    // Ghidra recovers the signature as `void FUN_8002a178(void)` with no `unaff_` register read.
    // The parameter is kept on this side because the CALL SITE passes it; it is not read, and that
    // is the original's behaviour, not a simplification.
    //
    // Shape: re-initialise elements 1..19, arm the two background bands and the panel (1), arm the
    // three sub-menu items (2, 3, 4), then play in over 32 frames — the panel widens by 0x200 a
    // frame for the first 24 and then narrows by 0x400 for the last 8, which is the overshoot-and-
    // settle. Afterwards elements 5..18 get their tpage/CLUT and the 4-by-2 portrait grid (9..16)
    // is laid out.
    internal static void FUN_8002a178(int param_1)
    {
        int iVar1;
        int iVar2;
        short sVar3;
        byte uVar4;
        int iVar5;

        _ = param_1;

        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 1, 0x13);
        iVar5 = 0;
        uVar4 = 0xb8;
        sVar3 = -0x14;
        iVar2 = 0x48;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].tpage = 0x15;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].x = -0xa0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].tpage = 0x17;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].x = 0x60;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].w = 0x40;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].tpage = 0xd;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x = 8;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].y = -0x4c;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].w = 0xe0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].h = 0x28;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].cx = 0x170;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].cy = 0x1fd;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].mx = 0x70;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].u = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].v = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].cx = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].cy = 0x1f2;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].attribute = 0x1000000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].u = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].v = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].cx = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].cy = 0x1f2;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].attribute = 0x1000000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].my = 0x14;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].attribute = 0;
        do
        {
            // 0x48 / 0x24 = element 2, then 3 and 4 — the three sub-menu items.
            int e = iVar2 / 0x24;

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].v = uVar4;
            uVar4 = (byte)(uVar4 + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].y = sVar3;
            sVar3 = (short)(sVar3 + 0x1c);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].cy = 0x1fd;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].mx = 0x4c;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].tpage = 0xd;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].x = 4;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].w = 0x98;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].h = 0x18;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].my = 0xc;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].r = (byte)'<';
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].g = (byte)'<';
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].b = (byte)'<';
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].attribute = 0;
            iVar5 = iVar5 + 1;
            iVar2 = iVar2 + 0x24;
        }
        while (iVar5 < 3);

        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex = 0;
        iVar2 = 0;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x + 8);
            if (iVar2 < 0x60)
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex =
                    (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex + 0x200);
            }
            else
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex =
                    (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex + -0x400);
            }

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r = (byte)iVar2;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].r =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            FrameStep.DrawFrame();
            iVar2 = iVar2 + 4;
        }
        while (iVar2 < 0x80);

        iVar5 = 0;
        iVar2 = 0xb4;
        do
        {
            // 0xB4 / 0x24 = element 5, fourteen of them, so elements 5..18.
            int e = iVar2 / 0x24;

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].tpage = 0xd;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].cy = 0x1f9;
            iVar5 = iVar5 + 1;
            iVar2 = iVar2 + 0x24;
        }
        while (iVar5 < 0xe);

        iVar5 = 0;
        iVar2 = 0x144;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].y = -0x30;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].mx = unchecked((short)0x80);
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].mx = unchecked((short)0x80);
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].h = 0x30;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].h = 0x30;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x = 8;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x = 8;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].y = 8;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].y = -0x28;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].v = 0x88;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].v = 0x88;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].x = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].w = 0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].h = 0x20;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].x = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].y = 0x10;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].w = 0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].h = 0x20;
        do
        {
            // 0x144 / 0x24 = element 9, eight of them: elements 9..16 laid out as a 4-by-2 grid,
            // x = (iVar5 % 4) * 0x30 - 0x40 and y = (iVar5 / 4) * 0x38 - 0x18. The `iVar5 + 3` and
            // the arithmetic shift are the compiler's signed division by four; iVar5 is never
            // negative here, and the form is kept as emitted.
            int e = iVar2 / 0x24;

            iVar1 = iVar5;
            if (iVar5 < 0)
            {
                iVar1 = iVar5 + 3;
            }

            sVar3 = (short)(iVar1 >> 2);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].x =
                (short)(((((short)iVar5) + (sVar3 * -4)) * 0x30) + -0x40);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].y = (short)((sVar3 * 0x38) + -0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].w = 0x30;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].h = 0x30;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].my = 0x18;
            iVar5 = iVar5 + 1;
            iVar2 = iVar2 + 0x24;
        }
        while (iVar5 < 8);

        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].v = (byte)'X';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].w = 0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].v = (byte)'X';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].w = 0x38;
    }

    // GHIDRA: FUN_8002a6f8 @ 0x8002A6F8
    // 252 bytes. THE VS SUB-MENU'S CANCEL UNWIND — 32 frames counting 0x80 down to 4, the panel
    // narrowing by 0x80 a frame and the background dimmed to 0, then elements 1..19 re-initialised.
    internal static void FUN_8002a6f8()
    {
        int iVar1;

        iVar1 = 0x80;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r = (byte)iVar1;
            iVar1 = iVar1 + -4;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex + -0x80);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].r =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            FrameStep.DrawFrame();
        }
        while (0 < iVar1);

        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 1, 0x13);
    }

    // GHIDRA: BuildSpSaveSlotScreen @ 0x8002A7F4
    // 2432 bytes. THE SP SAVE-SLOT PICKER'S BUILD, called by ModeBranches.RunSpModeScreen with the
    // preselected cursor and the base of the three sixteen-byte records at 0x801FF218.
    //
    // It opens with the SAME digit blit ModeBranches.RunSpModeScreen does on every rebuild: decode the
    // halfword at +2 of each record as a decimal number and MoveImage its two digits out of the
    // 4-by-16 strip at (0x300 + digit * 4, 0xDD) to (0x3E4 + row * 8, 0x100) and
    // (0x3E8 + row * 8, 0x100). The RECT constant is g_SpBuildDigitCellRect/DAT_80055a14, which is the same
    // pair of words as ModeBranches.g_SpBranchDigitCellRect/DAT_80055a4c.
    //
    // The rest is the picker's own layout — six panel strips (1..6) sliding in from off-screen, the
    // three row icons (7, 8, 9), the three row labels (10, 11, 12), the two caption strips (14, 15)
    // and the four selectable rows (16..19) — played in over 32 frames while everything slides to
    // its resting x.
    internal static void BuildSpSaveSlotScreen(int param_1, int param_2)
    {
        bool bVar3;
        uint uVar4;
        short sVar5;
        short sVar6;
        int iVar7;
        int iVar8;
        int iVar9;
        sbyte cVar10;
        int iVar11;
        int iVar12;
        ushort puVar2;
        RECT local_58 = new RECT();

        // `local_58._0_4_ = g_SpBuildDigitCellRect; local_58._4_4_ = DAT_80055a14;` — Ghidra renders each word
        // twice, once as the unaligned SWL/SWR pair the compiler emitted for the struct copy and
        // once as the aligned store. Both write the same four bytes.
        local_58.x = (short)(g_SpBuildDigitCellRect & 0xffff);
        local_58.y = (short)(g_SpBuildDigitCellRect >> 16);
        local_58.w = (short)(DAT_80055a14 & 0xffff);
        local_58.h = (short)(DAT_80055a14 >> 16);

        iVar11 = 0;
        iVar12 = 0x3e4;
        iVar9 = 1000;
        do
        {
            uVar4 = (uint)((PsxRam.ReadU16(param_2 + 2) + 1) / 10);
            iVar7 = (int)uVar4 - 1;
            if (uVar4 == 0)
            {
                iVar7 = 9;
            }

            // `(short)((iVar7 << 0x10) >> 0xe)` is iVar7 * 4 taken through the low halfword.
            local_58.x = (short)((short)((iVar7 << 0x10) >> 0xe) + 0x300);
            MoveImage(local_58, iVar12, 0x100);
            puVar2 = PsxRam.ReadU16(param_2 + 2);
            param_2 = param_2 + 0x10;
            iVar12 = iVar12 + 8;
            iVar11 = iVar11 + 1;
            local_58.x = (short)(((puVar2 % 10) * 4) + 0x300);
            MoveImage(local_58, iVar9, 0x100);
            iVar9 = iVar9 + 8;
        }
        while (iVar11 < 3);

        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].tpage = 0x17;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].x = -0xa0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].u = (byte)'@';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].w = 0x40;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].tpage = 0x18;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].x = -0x60;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].cx = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].cy = 499;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].attribute = 0x1000000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].cx = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].cy = 499;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].attribute = 0x1000000;
        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 1, 0x13);
        iVar9 = 1;
        iVar11 = 0;
        do
        {
            iVar12 = iVar9;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].tpage = 0x1c;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].y = (short)((iVar11 * -0x28) + 0x48);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].w = 0xd8;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].h = 0x28;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].cy = 0x1fe;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].attribute = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].y =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].y;
            iVar9 = iVar12 + 1;
            iVar11 = iVar12;
        }
        while (iVar12 < 6);

        iVar7 = 0;
        iVar9 = 0x168;
        iVar12 = 10;
        cVar10 = -0x70;
        sVar5 = -0x2b;
        iVar11 = 0xfc;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].y = 0x20;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x = 0xa0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].x = -0x178;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].x = 0xb0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].x = -0x188;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].x = 0xc0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].x = -0x198;
        do
        {
            int e7 = iVar11 / 0x24;
            int e10 = iVar9 / 0x24;

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].tpage = 0x1f;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].y = sVar5;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].u = (byte)cVar10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].w = 0x20;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].h = 0x10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].b = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].g = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].r = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].cy = 0x1fd;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].cy = 0x1fd;
            iVar8 = iVar12 + 3;
            iVar12 = iVar12 + 1;
            cVar10 = (sbyte)(cVar10 + 0x20);
            sVar5 = (short)(sVar5 + 0x18);
            iVar11 = iVar11 + 0x24;
            iVar7 = iVar7 + 1;
            iVar8 = (int)((uint)(ListCursor.AvailabilityByte(iVar8 - 0xc) == 0 ? 1 : 0) << 0x1f);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].attribute = (uint)iVar8;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].attribute = (uint)iVar8;
            iVar9 = iVar9 + 0x24;
        }
        while (iVar7 < 3);

        iVar11 = 0;
        sVar6 = -0x30;
        sVar5 = 0x14;
        iVar9 = 0x168;
        do
        {
            int e = iVar9 / 0x24;

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].y = sVar6;
            sVar6 = (short)(sVar6 + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].x = sVar5;
            sVar5 = (short)(sVar5 + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].tpage = 0xc;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].v = 200;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].w = 0x68;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].h = 0x15;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].b = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].g = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].r = 0x40;
            iVar11 = iVar11 + 1;
            iVar9 = iVar9 + 0x24;
        }
        while (iVar11 < 3);

        iVar9 = 0;
        if (param_1 != 0)
        {
            iVar11 = param_1 + 9;
            iVar12 = param_1 + 6;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar11].b = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar11].g = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar11].r = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].b = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].g = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar12].r = 0x80;
        }

        iVar11 = 0x1f8;
        do
        {
            // 0x1F8 / 0x24 = element 14, six of them: 14..19. `bVar3` is computed BEFORE iVar9 is
            // incremented, so the r/g/b arm is skipped for the first two (14 and 15) and taken for
            // the four selectable rows (16..19).
            int e = iVar11 / 0x24;

            bVar3 = 1 < iVar9;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].tpage = 0xc;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].cy = 0x1fd;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].attribute = 0;
            iVar9 = iVar9 + 1;
            if (bVar3)
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].r = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].g = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].b = 0x40;
            }

            iVar11 = iVar11 + 0x24;
        }
        while (iVar9 < 6);

        iVar9 = 0;
        param_1 = param_1 + 0x10;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1].r = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1].g = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1].b = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].x = -0x288;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].y = -100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].v = 0x94;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].w = 0xe8;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].h = 0xc;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].x = -0x290;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].y = -0x68;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].v = 0x81;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].h = 0x12;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].x = 0x15c;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].y = -0x48;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].v = (byte)'W';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].w = 0xc0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].y = -0x30;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].v = 0x18;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x = 0x120;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].y = -0x18;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].v = (byte)'-';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].h = 0x15;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x = -0x228;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].w = 0xe0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].h = 0x15;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].w = 0xe0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].h = 0x15;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x = -0x228;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].y = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].v = (byte)'B';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].w = 0xe0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].h = 0x15;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x + -8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x + 4);
            bVar3 = SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x < -0x83;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x + -0x18);
            if (bVar3)
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x = -0x90;
            }

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].x + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].x + -0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].x + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].x + -0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].x + 0x18);
            if (SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].x < -0x78)
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].x =
                    (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].x + 0x18);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].x =
                    (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].x + 0x18);
            }

            sVar5 = -0x40;
            if (-0x34 < SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].x)
            {
                sVar5 = (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].x + -0x18);
            }

            if (SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x < -0x60)
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].x =
                    (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x + 0xd2);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].x =
                    (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x + 0x8c);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x =
                    (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x + 0x18);
            }

            sVar6 = -0x6a;
            if (-0x50 < SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x)
            {
                sVar6 = (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x + -0x18);
            }

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].x = (short)(sVar6 + 0xba);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].x = (short)(sVar6 + 0x74);
            bVar3 = -0x7d < SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x + 0x18);
            if (bVar3)
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x = -0x70;
            }

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r = (byte)iVar9;
            iVar9 = iVar9 + 4;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x + 0xba);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x + 0x74);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].x = sVar5;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x = sVar6;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].r =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            FrameStep.DrawFrame();
        }
        while (iVar9 < 0x80);
    }

    // GHIDRA: UnwindSpSaveSlotScreen @ 0x8002B174
    // 360 bytes. THE SP PICKER'S CANCEL UNWIND — 32 frames counting 0x80 down to 4, every panel
    // strip sliding back off the way it came, then elements 1..19 re-initialised.
    // NOTE THE THREE OFFSET PAIRS, which are NOT the build's: sprites 7/10 track 0x11 by +0xE2/+0x8C
    // (the build used +0xD2/+0x8C), 8/0xB track 0x12 by +0xB2/+0x5C (the build used +0xBA/+0x74) and
    // 9/0xC track 0x13 by +0xE2/+0x8C (the build used +0xBA/+0x74). Reproduced, not reconciled.
    internal static void UnwindSpSaveSlotScreen()
    {
        int iVar1;

        iVar1 = 0x80;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x14].y + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x16].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x15].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].x + 8);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x + -0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x + -4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].x + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].x + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].x + -0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x + 0xe2);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x + 0x8c);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x + 0xb2);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x + 0x5c);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x + 0xe2);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x + 0x8c);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r = (byte)iVar1;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x + -0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x13].x + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].r =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].g =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].b =
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r;
            FrameStep.DrawFrame();
            iVar1 = iVar1 + -4;
        }
        while (0 < iVar1);

        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 1, 0x13);
    }

    // GHIDRA: FUN_8002cc04 @ 0x8002CC04
    // 1836 bytes. THE HAND-OFF TRANSITION. All three branches call it immediately before
    // ModeBranches.StopCdAudio and the LoadExec, so it is the last thing SELECT.EXE draws.
    //
    // NO ARGUMENT IS READ. The two card pickers write a0 (0 and 1) and RunVsModeScreen leaves whatever
    // was in the register, but Ghidra recovers `void FUN_8002cc04(void)` — the first instruction
    // that touches a0 is the `jal InitializeSpriteArray` at the top, and there is no `unaff_` read anywhere
    // in the body. THAT CLOSES THE OPEN POINT ModeBranches.cs RECORDED at its FUN_8002cc04 call in
    // RunVsModeScreen: the leaked register cannot reach anything, because nothing reads it.
    // The C# signature keeps param_1 because two of the three call sites pass it.
    //
    // WHAT IT DOES, in two phases over the same 35 sprites (elements 60..94) the mode-menu orbit
    // uses — FUN_80030698 @ 0x80030698 built their address table at 0x800593B8, and
    // SelectScreen.cs already carries it:
    //   SETUP  present one frame, MoveImage the whole 320x240 frame to VRAM (0x280, 0), DrawSync,
    //          re-initialise elements 0..59, re-aim the background at tpage 0x0A / 0x0B with
    //          attribute 0x2000000 (a different blend mode from the builds' 0x1000000), set
    //          DAT_80055b80 bit 2 so main reloads the assets after the hand-off, and set all 35
    //          chain sprites to scale 0x3000.
    //   PHASE 1 (30 frames) the seven chain leaders are placed on an ellipse of radius iVar14,
    //          which shrinks 0x104 -> 0x14 by 8 a frame, while each leader's angle advances 15
    //          degrees a frame and every chain sprite shrinks by 0x15E. The chains collapse to the
    //          centre. Then all 37 of elements 60..96 are hidden, DAT_80055b80 gains bit 0
    //          (suppress the background clear) and bit 1 (sort 0x62 sprites instead of 100), and
    //          elements 0..59 are re-initialised again.
    //   PHASE 2 (about 98 frames) the same seven chains are re-released one every eight frames
    //          from angle 0/60/120/180/240/300 with a radius that starts at phase 1's final 0x14
    //          and grows by 0xC a frame; each released chain is un-hidden and grows by 400 a frame.
    //          The ONLY exit is `if (21999 < element 60's scalex) return;` at the top of the loop.
    //
    // IT TERMINATES, and that is arithmetic rather than hope: element 60's scalex leaves phase 1 at
    // 0x3000 - 30 * 0x15E = 1788, and from the frame where the counter passes 0x2F it is rewritten
    // every frame as element 61's scaley + 400 with element 61 then copied from it, i.e. +400 a
    // frame. (21999 - 1788) / 400 = 51 more frames. Nothing else writes element 60's scale: the
    // per-chain growth loop starts at 0x8B8, which is element 62.
    internal static void FUN_8002cc04(int param_1)
    {
        byte bVar2;
        short uVar4;
        short sVar5;
        uint uVar9;
        int iVar10;
        int iVar11;
        int piVar12;
        int iVar13;
        int iVar14;
        double uVar15;
        double uVar16;

        _ = param_1;

        // THE STACK FRAME, and why it is four objects here. Ghidra names nine locals
        // (local_58, local_54, local_50, local_4c, asStack_48, local_3c, auStack_38, auStack_30,
        // uStack_2c) but the code addresses them as FOUR contiguous runs:
        //   -0x58 .. -0x4B   seven ushorts — the per-chain ANGLE, read as
        //                    `*(ushort *)(iVar10 + (int)puVar7)` for iVar10 = 0, 2, ... 12
        //   -0x48 .. -0x3B   seven shorts  — the per-chain RADIUS, read as
        //                    `*(short *)((int)asStack_48 + iVar13)` for iVar13 = 2, 4, ... 12
        //   -0x38 .. -0x31   the RECT handed to MoveImage
        //   -0x30 .. -0x29   eight bytes  — the chain boundary table, indexed a byte at a time
        // The initial words are the .sdata constants named at the top of this file, decomposed the
        // way the code reads them back: 0x00320000 little-endian is { 0x0000, 0x0032 }, 0x00960064
        // is { 0x0064, 0x0096 } and 0x00FA00C8 is { 0x00C8, 0x00FA }, so the seven angles start at
        // 0, 50, 100, 150, 200, 250 and 300 degrees.
        ushort[] local_58 = { 0x0000, 0x0032, 0x0064, 0x0096, 0x00c8, 0x00fa, 300 };
        short[] asStack_48 = new short[7];
        RECT auStack_38 = new RECT();
        byte[] auStack_30 = (byte[])g_ChainLastIndexTable8.Clone();

        auStack_38.x = (short)(g_FullFrameRect320x240 & 0xffff);
        auStack_38.y = (short)(g_FullFrameRect320x240 >> 16);
        auStack_38.w = (short)(DAT_80055a34 & 0xffff);
        auStack_38.h = (short)(DAT_80055a34 >> 16);

        iVar14 = 0x104;
        uVar4 = 0x104;
        FrameStep.DrawFrame();
        MoveImage(auStack_38, 0x280, 0);
        DrawSync(0);
        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 0, 0x3c);
        iVar13 = 0;
        iVar10 = 0x870;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].tpage = 10;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].x = -0xa0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].w = 0x40;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].tpage = 0xb;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].x = -0x60;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].u = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].attribute = 0x2000000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].u = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].attribute = 0x2000000;
        SELECT_EXE_exe.DAT_80055b80 = SELECT_EXE_exe.DAT_80055b80 | 4;
        do
        {
            // 0x870 / 0x24 = element 60, 0x23 = 35 of them: elements 60..94.
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar10 / 0x24].scaley = 0x3000;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar10 / 0x24].scalex = 0x3000;
            iVar13 = iVar13 + 1;
            iVar10 = iVar10 + 0x24;
        }
        while (iVar13 < 0x23);

        // Ghidra wraps the loop below in `if (true) { ... }`, which is unconditional entry.
        do
        {
            iVar13 = 0;
            uVar15 = __floatsidf((short)iVar14);
            piVar12 = 0;
            iVar10 = 0;
            do
            {
                // -sin(angle) * radius / 4096 -> leader.x, cos(angle) * radius / 4096 -> leader.y.
                // &g_CosineTableBase is ModeMenu.g_SineTable451 ninety entries in, i.e. cos.
                // 0x40B00000_00000000 is 4096.0.
                uVar16 = __floatsidf(-ModeMenu.g_SineTable451[local_58[iVar10 >> 1]]);
                uVar16 = __muldf3(uVar16, uVar15);
                uVar16 = __divdf3(uVar16, 4096.0);
                uVar4 = (short)__fixdfsi(uVar16);
                iVar13 = iVar13 + 1;
                ModeMenu.SpriteAtAddress(
                    MipsMemory.ReadI32(SelectScreen.g_SpriteChainTable7, piVar12)).x = uVar4;
                iVar11 = MipsMemory.ReadI32(SelectScreen.g_SpriteChainTable7, piVar12);
                piVar12 = piVar12 + 0xc;
                uVar16 = __floatsidf(ModeMenu.g_SineTable451[90 + local_58[iVar10 >> 1]]);
                uVar16 = __muldf3(uVar16, uVar15);
                uVar16 = __divdf3(uVar16, 4096.0);
                uVar4 = (short)__fixdfsi(uVar16);
                ModeMenu.SpriteAtAddress(iVar11).y = uVar4;
                iVar10 = iVar13 * 2;
            }
            while (iVar13 < 7);

            iVar10 = 0;
            int puVar7 = 0;
            do
            {
                local_58[puVar7] = (ushort)(local_58[puVar7] + 0xf);
                iVar10 = iVar10 + 1;
                if (0x167 < local_58[puVar7])
                {
                    local_58[puVar7] = 0;
                }

                puVar7 = puVar7 + 1;
            }
            while (iVar10 < 7);

            iVar14 = iVar14 + -8;
            uVar4 = (short)iVar14;
            FUN_8002dec0();
            iVar13 = 0;
            iVar10 = 0x870;
            do
            {
                iVar13 = iVar13 + 1;
                sVar5 = (short)(
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar10 / 0x24].scaley + -0x15e);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar10 / 0x24].scaley = sVar5;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar10 / 0x24].scalex = sVar5;
                iVar10 = iVar10 + 0x24;
            }
            while (iVar13 < 0x23);

            FrameStep.DrawFrame();
        }
        while (0x14 < (short)iVar14);

        iVar13 = 0x24;
        iVar10 = 0xd80;
        do
        {
            // 0xD80 / 0x24 = element 96, walking DOWN to element 60 — 37 of them.
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar10 / 0x24].attribute = 0x80000000;
            iVar13 = iVar13 + -1;
            iVar10 = iVar10 + -0x24;
        }
        while (-1 < iVar13);

        iVar10 = 6;
        FrameStep.DrawFrame();
        FrameStep.DrawFrame();
        SELECT_EXE_exe.DAT_80055b80 = SELECT_EXE_exe.DAT_80055b80 | 1;
        FrameStep.DrawFrame();
        SELECT_EXE_exe.DAT_80055b80 = SELECT_EXE_exe.DAT_80055b80 | 2;
        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 0, 0x3c);

        // `local_54 = 0x78003c; local_50 = 0xf000b4; local_58 &= 0xffff; local_4c[0] = 300;` — the
        // angle array re-seeded to { unchanged, 0, 60, 120, 180, 240, 300 }. The `& 0xffff` keeps
        // entry 0 and clears entry 1, which is why entry 0 is the only one phase 1 leaves behind.
        local_58[2] = 0x003c;
        local_58[3] = 0x0078;
        local_58[4] = 0x00b4;
        local_58[5] = 0x00f0;
        local_58[1] = 0;
        local_58[6] = 300;

        // `puVar8 = local_4c; do { puVar8[8] = uVar4; puVar8 -= 1; } while (-1 < --iVar10);` with
        // iVar10 = 6 — puVar8 walks BACKWARD through the angle array while writing at +16, which
        // lands on radius[6] down to radius[0]. uVar4 is phase 1's last radius, 0x14.
        int puVar8 = 6;
        do
        {
            asStack_48[puVar8] = uVar4;
            iVar10 = iVar10 + -1;
            puVar8 = puVar8 - 1;
        }
        while (-1 < iVar10);

        iVar10 = 0;
        do
        {
            if (21999 < SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3c].scalex)
            {
                return;
            }

            iVar14 = 1;

            // `piVar12 = &DAT_800593c4` — record 1 of the seven twelve-byte records at 0x800593B8.
            piVar12 = 0xc;
            iVar13 = 2;
            do
            {
                // Chain iVar14 is not released until frame (iVar14 - 1) * 8.
                if ((int)(short)iVar10 < (iVar14 + -1) * 8)
                {
                    break;
                }

                uVar15 = __floatsidf(-ModeMenu.g_SineTable451[local_58[iVar13 >> 1]]);
                uVar16 = __floatsidf(asStack_48[iVar13 >> 1]);
                uVar15 = __muldf3(uVar15, uVar16);
                uVar15 = __divdf3(uVar15, 4096.0);
                uVar4 = (short)__fixdfsi(uVar15);
                ModeMenu.SpriteAtAddress(
                    MipsMemory.ReadI32(SelectScreen.g_SpriteChainTable7, piVar12)).x = uVar4;
                uVar15 = __floatsidf(ModeMenu.g_SineTable451[90 + local_58[iVar13 >> 1]]);
                uVar16 = __floatsidf(asStack_48[iVar13 >> 1]);
                uVar15 = __muldf3(uVar15, uVar16);
                uVar15 = __divdf3(uVar15, 4096.0);
                uVar4 = (short)__fixdfsi(uVar15);
                ModeMenu.SpriteAtAddress(
                    MipsMemory.ReadI32(SelectScreen.g_SpriteChainTable7, piVar12)).y = uVar4;

                // Un-hide chain iVar14's sprites: elements 60 + auStack_30[iVar14] + 1 through
                // 60 + auStack_30[iVar14 + 1].
                uVar9 = (uint)auStack_30[iVar14] + 1;
                if (uVar9 <= auStack_30[iVar14 + 1])
                {
                    iVar11 = (int)(uVar9 * 0x24) + 0x870;
                    do
                    {
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar11 / 0x24].attribute = 0x1000000;
                        uVar9 = uVar9 + 1;
                        iVar11 = iVar11 + 0x24;
                    }
                    while ((int)uVar9 <= (int)(uint)auStack_30[iVar14 + 1]);
                }

                iVar14 = iVar14 + 1;
                asStack_48[iVar13 >> 1] = (short)(asStack_48[iVar13 >> 1] + 0xc);
                piVar12 = piVar12 + 0xc;
                iVar13 = iVar13 + 2;
            }
            while (iVar14 < 7);

            FUN_8002dec0();
            bVar2 = auStack_30[iVar14];
            iVar13 = 2;
            if (1 < bVar2)
            {
                // 0x8B8 / 0x24 = element 62 — every chain sprite released so far grows by 400.
                iVar14 = 0x8b8;
                do
                {
                    iVar13 = iVar13 + 1;
                    sVar5 = (short)(
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar14 / 0x24].scaley + 400);
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar14 / 0x24].scaley = sVar5;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar14 / 0x24].scalex = sVar5;
                    iVar14 = iVar14 + 0x24;
                }
                while (iVar13 <= (int)(uint)bVar2);
            }

            iVar10 = iVar10 + 1;
            if (0x2f < (short)iVar10)
            {
                // `*(undefined2 *)(g_SpriteChainTable7 + 4)` is chain 0's LEADER, dereferenced through the
                // pointer stored at 0x800593B8 — element 60. It is pinned at the centre and its own
                // pair (60, 61) is what the loop's exit test watches.
                int leader0 = MipsMemory.ReadI32(SelectScreen.g_SpriteChainTable7, 0);
                ModeMenu.SpriteAtAddress(leader0).x = 0;
                ModeMenu.SpriteAtAddress(leader0).y = 0;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3d].attribute = 0x1000000;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3c].attribute = 0x1000000;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3c].scalex =
                    (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3d].scaley + 400);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3c].scaley =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3c].scalex;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3d].scalex =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3c].scalex;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3d].scaley =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3c].scalex;
            }

            FrameStep.DrawFrame();
        }
        while (true);
    }

    // GHIDRA: FUN_8002dec0 @ 0x8002DEC0
    // 3020 bytes, no loop of any kind — 28 fully unrolled satellite updates, one per entry of the
    // triangular table FUN_80030698 built. FUN_8002cc04 calls it twice, once per phase.
    //
    // IT IS THE SIBLING OF ModeMenu.FUN_80033630 @ 0x80033630 AND NOT THE SAME FUNCTION. Both copy
    // each leader's x and y onto that leader's satellites; where FUN_80033630 adds a hard-coded
    // delta to the COPIED POSITION, this one copies the position unchanged and writes the delta into
    // the satellite's mx (+0x18) and my (+0x1A) instead — the sprite's rotation/scale origin. That
    // is what makes the chains spin about offset centres while collapsing.
    //
    // FOUR STORES ARE MISSING OR WRONG IN THE ORIGINAL, AND ALL FOUR ARE REPRODUCED (rule 12):
    //   row 0's single satellite gets no mx/my at all;
    //   row 1's two satellites get mx but no my;
    //   row 2's third satellite gets my but no mx;
    //   row 5's third satellite (DAT_800593f8[2]) has its mx written TWICE, 0xFFF8 then 0xFFF5, and
    //   never gets an my. The second store is at +0x18 in the image, not +0x1A.
    private static void FUN_8002dec0()
    {
        // row 0 — *DAT_800593bc from g_SpriteChainTable7
        ModeMenu.SatelliteSprite(0, 0).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(0)).x;
        ModeMenu.SatelliteSprite(0, 0).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(0)).y;

        // row 1 — DAT_800593c8[0..1] from DAT_800593c4
        ModeMenu.SatelliteSprite(1, 0).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(1)).x;
        ModeMenu.SatelliteSprite(1, 0).mx = 0xe;
        ModeMenu.SatelliteSprite(1, 0).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(1)).y;
        ModeMenu.SatelliteSprite(1, 1).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(1)).x;
        ModeMenu.SatelliteSprite(1, 1).mx = unchecked((short)0xfffa);
        ModeMenu.SatelliteSprite(1, 1).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(1)).y;

        // row 2 — DAT_800593d4[0..2] from DAT_800593d0
        ModeMenu.SatelliteSprite(2, 0).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(2)).x;
        ModeMenu.SatelliteSprite(2, 0).mx = 0xe;
        ModeMenu.SatelliteSprite(2, 0).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(2)).y;
        ModeMenu.SatelliteSprite(2, 0).my = unchecked((short)0xfffa);
        ModeMenu.SatelliteSprite(2, 1).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(2)).x;
        ModeMenu.SatelliteSprite(2, 1).mx = unchecked((short)0xfffa);
        ModeMenu.SatelliteSprite(2, 1).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(2)).y;
        ModeMenu.SatelliteSprite(2, 1).my = unchecked((short)0xfffa);
        ModeMenu.SatelliteSprite(2, 2).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(2)).x;
        ModeMenu.SatelliteSprite(2, 2).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(2)).y;
        ModeMenu.SatelliteSprite(2, 2).my = 0xe;

        // row 3 — DAT_800593e0[0..3] from DAT_800593dc
        ModeMenu.SatelliteSprite(3, 0).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(3)).x;
        ModeMenu.SatelliteSprite(3, 0).mx = 0x12;
        ModeMenu.SatelliteSprite(3, 0).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(3)).y;
        ModeMenu.SatelliteSprite(3, 0).my = 0xc;
        ModeMenu.SatelliteSprite(3, 1).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(3)).x;
        ModeMenu.SatelliteSprite(3, 1).mx = unchecked((short)0xfffb);
        ModeMenu.SatelliteSprite(3, 1).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(3)).y;
        ModeMenu.SatelliteSprite(3, 1).my = 0xd;
        ModeMenu.SatelliteSprite(3, 2).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(3)).x;
        ModeMenu.SatelliteSprite(3, 2).mx = 0x13;
        ModeMenu.SatelliteSprite(3, 2).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(3)).y;
        ModeMenu.SatelliteSprite(3, 2).my = unchecked((short)0xfff8);
        ModeMenu.SatelliteSprite(3, 3).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(3)).x;
        ModeMenu.SatelliteSprite(3, 3).mx = unchecked((short)0xfffa);
        ModeMenu.SatelliteSprite(3, 3).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(3)).y;
        ModeMenu.SatelliteSprite(3, 3).my = unchecked((short)0xfff8);

        // row 4 — DAT_800593ec[0..4] from DAT_800593e8
        ModeMenu.SatelliteSprite(4, 0).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).x;
        ModeMenu.SatelliteSprite(4, 0).mx = 6;
        ModeMenu.SatelliteSprite(4, 0).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).y;
        ModeMenu.SatelliteSprite(4, 0).my = 0x12;
        ModeMenu.SatelliteSprite(4, 1).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).x;
        ModeMenu.SatelliteSprite(4, 1).mx = unchecked((short)0xfff5);
        ModeMenu.SatelliteSprite(4, 1).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).y;
        ModeMenu.SatelliteSprite(4, 1).my = 8;
        ModeMenu.SatelliteSprite(4, 2).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).x;
        ModeMenu.SatelliteSprite(4, 2).mx = 0x10;
        ModeMenu.SatelliteSprite(4, 2).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).y;
        ModeMenu.SatelliteSprite(4, 2).my = unchecked((short)0xfff5);
        ModeMenu.SatelliteSprite(4, 3).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).x;
        ModeMenu.SatelliteSprite(4, 3).mx = unchecked((short)0xfffb);
        ModeMenu.SatelliteSprite(4, 3).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).y;
        ModeMenu.SatelliteSprite(4, 3).my = unchecked((short)0xfff6);
        ModeMenu.SatelliteSprite(4, 4).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).x;
        ModeMenu.SatelliteSprite(4, 4).mx = 0x16;
        ModeMenu.SatelliteSprite(4, 4).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(4)).y;
        ModeMenu.SatelliteSprite(4, 4).my = 6;

        // row 5 — DAT_800593f8[0..5] from DAT_800593f4
        ModeMenu.SatelliteSprite(5, 0).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).x;
        ModeMenu.SatelliteSprite(5, 0).mx = 6;
        ModeMenu.SatelliteSprite(5, 0).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).y;
        ModeMenu.SatelliteSprite(5, 0).my = 0x12;
        ModeMenu.SatelliteSprite(5, 1).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).x;
        ModeMenu.SatelliteSprite(5, 1).mx = unchecked((short)0xfff5);
        ModeMenu.SatelliteSprite(5, 1).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).y;
        ModeMenu.SatelliteSprite(5, 1).my = 8;
        ModeMenu.SatelliteSprite(5, 2).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).x;
        ModeMenu.SatelliteSprite(5, 2).mx = unchecked((short)0xfff8);
        ModeMenu.SatelliteSprite(5, 2).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).y;

        // The image writes +0x18 a SECOND time here, not +0x1A. Reproduced.
        ModeMenu.SatelliteSprite(5, 2).mx = unchecked((short)0xfff5);
        ModeMenu.SatelliteSprite(5, 3).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).x;
        ModeMenu.SatelliteSprite(5, 3).mx = unchecked((short)0xfffb);
        ModeMenu.SatelliteSprite(5, 3).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).y;
        ModeMenu.SatelliteSprite(5, 3).my = unchecked((short)0xfff6);
        ModeMenu.SatelliteSprite(5, 4).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).x;
        ModeMenu.SatelliteSprite(5, 4).mx = 6;
        ModeMenu.SatelliteSprite(5, 4).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).y;
        ModeMenu.SatelliteSprite(5, 4).my = 3;
        ModeMenu.SatelliteSprite(5, 5).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).x;
        ModeMenu.SatelliteSprite(5, 5).mx = 0x16;
        ModeMenu.SatelliteSprite(5, 5).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(5)).y;
        ModeMenu.SatelliteSprite(5, 5).my = 6;

        // row 6 — DAT_80059404[0..6] from DAT_80059400
        ModeMenu.SatelliteSprite(6, 0).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).x;
        ModeMenu.SatelliteSprite(6, 0).mx = 6;
        ModeMenu.SatelliteSprite(6, 0).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).y;
        ModeMenu.SatelliteSprite(6, 0).my = 0x14;
        ModeMenu.SatelliteSprite(6, 1).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).x;
        ModeMenu.SatelliteSprite(6, 1).mx = unchecked((short)0xfff6);
        ModeMenu.SatelliteSprite(6, 1).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).y;
        ModeMenu.SatelliteSprite(6, 1).my = 0xc;
        ModeMenu.SatelliteSprite(6, 2).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).x;
        ModeMenu.SatelliteSprite(6, 2).mx = 0x15;
        ModeMenu.SatelliteSprite(6, 2).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).y;
        ModeMenu.SatelliteSprite(6, 2).my = unchecked((short)0xfffa);
        ModeMenu.SatelliteSprite(6, 3).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).x;
        ModeMenu.SatelliteSprite(6, 3).mx = unchecked((short)0xfff6);
        ModeMenu.SatelliteSprite(6, 3).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).y;
        ModeMenu.SatelliteSprite(6, 3).my = unchecked((short)0xfffa);
        ModeMenu.SatelliteSprite(6, 4).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).x;
        ModeMenu.SatelliteSprite(6, 4).mx = 6;
        ModeMenu.SatelliteSprite(6, 4).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).y;
        ModeMenu.SatelliteSprite(6, 4).my = 3;
        ModeMenu.SatelliteSprite(6, 5).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).x;
        ModeMenu.SatelliteSprite(6, 5).mx = 0x15;
        ModeMenu.SatelliteSprite(6, 5).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).y;
        ModeMenu.SatelliteSprite(6, 5).my = 10;
        ModeMenu.SatelliteSprite(6, 6).x = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).x;
        ModeMenu.SatelliteSprite(6, 6).mx = 5;
        ModeMenu.SatelliteSprite(6, 6).y = ModeMenu.SpriteAtAddress(ModeMenu.LeaderAddress(6)).y;
        ModeMenu.SatelliteSprite(6, 6).my = unchecked((short)0xfff2);
    }
}
