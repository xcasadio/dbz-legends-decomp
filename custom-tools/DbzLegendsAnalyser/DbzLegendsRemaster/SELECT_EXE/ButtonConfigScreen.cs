using PsxSdkMonogame;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE BUTTON-CONFIGURATION SCREEN — line 2 (操作設定) of the options menu, and the only screen in
// SELECT.EXE that WRITES the cross-overlay pad remap tables instead of only reading them.
//
// FOUR FUNCTIONS, ONE ENTRY POINT:
//     0x80031C44  RunButtonConfigScreen     72 bytes, ONE caller (RunOptionsScreen @ 0x80031A1C,
//                                           line 141: `RunButtonConfigScreen(DAT_80055b10)`),
//                                           FOUR callees in this order
//     0x8002C048  BuildButtonConfigScreen   1908 bytes, the play-in
//     0x80028B38  FUN_80028b38              2892 bytes, the blocking swap loop
//     0x8002C7BC  UnwindButtonConfigScreen  1096 bytes, the play-out
// RunButtonConfigScreen then calls SelectScreen.InitializeSpriteArray on elements 30..41, which is
// the twelve sprites BuildButtonConfigScreen armed — the screen cleans up after itself.
//
// FUN_80028b38 KEEPS ITS RAW NAME. A previous review REFUTED calling 0x80028B38
// "RunButtonConfigScreen"; that name belongs to 0x80031C44, which is what Ghidra carries. Nothing
// closes a business name for 0x80028B38 beyond "the loop the config screen blocks in", so it stays
// FUN_80028b38.
//
// THE PLAYER INDEX. RunOptionsScreen passes DAT_80055B10, the 1P/2P value line 2 toggles, and it
// reaches every one of the three bodies:
//   * BuildButtonConfigScreen indexes OverlayExit.g_PadRemapTablePointers2[param_1] — the int[2] of
//     PSX addresses InitializePadRemapTablePointers @ 0x80034380 published, [0] = 0x801FF020 and
//     [1] = 0x801FF03C — and it picks sprite 0x29's `v` row as `param_1 * 0x10 + 0x60`, i.e. the
//     "1P" / "2P" caption strip;
//   * FUN_80028b38 passes it to PadInput.FUN_80026208 as the PORT to read, and holds
//     `g_PadRemapTablePointers2 + param_1` as the slot it swaps entries inside.
//
// THE REMAP TABLES ARE FOURTEEN u_short EACH, at 0x801FF020 and 0x801FF03C, inside the extent
// SharedHighRam models, so every access below goes through PsxRam at the address the original uses.
// Measured on the console, identical in both tables:
//     0x0020 0x0080 0x0010 0x0040 0x2000 0x8000 0x1000 0x4000 0x0100 0x0800 0x0008 0x0002 0x0004 0x0001
// NOTHING IN SELECT.EXE FILLS THEM. The bootstrap SLPS_003.55 does, through FUN_8002165C; this
// overlay only reads and permutes them. On desktop they therefore read back as whatever
// SharedHighRam holds at the time, which is not this file's contract to fix.
//
// THE ELEVEN-BYTE ORDER TABLE, verbatim from .rodata at 0x8002062C and re-emitted as immediates
// into both functions' stack frames (BuildButtonConfigScreen: `swl v0,0x13(sp)` / `swr v0,0x10(sp)`,
// `swl v1,0x17(sp)` / `swr v1,0x14(sp)`, then `sb` at 0x18/0x19/0x1a(sp)):
//     6, 7, 0, 1, 2, 3, 12, 13, 10, 11, 8
// Those are INDICES INTO THE FOURTEEN-ENTRY REMAP TABLE, one per configurable row, in the order the
// eleven rows are drawn. The three table entries they never name are 4, 5 and 9 — 0x2000, 0x8000
// and 0x0800, which are exactly three of the four masks OverlayExit.PadMaskToButtonIndex has no arm
// for. The two facts close each other: the eleven rows are the eleven buttons that function does
// answer for.
//
// SPRITE MAP, closed from the byte cursors (base 0x800654EC, stride 0x24):
//     0x438 / 0x24 = 30      elements 30..41 — the twelve button rows (0x1E..0x29)
//     0x0D8 / 0x24 =  6      elements  6..18 — the thirteen sprites the options menu owns and this
//                                              screen slides off to the left and back
//     element 0x29 (41)      the "1P" / "2P" caption
//     element 0x2A (42)      the spinning card on tpage 0x1D, u/v 0x00/0x28, w/h 0x80/0x50,
//                            cx/cy 0x100/0x1F1, scale ramped 0x88 -> 0x1000 and rotated
//     elements 1, 2, 3, 5    the menu's own strips, whose y and scalex this screen drives
//     element 4              the header, clamped between -0x60 and +0x10
//
// THE FOUR GsLINE ARE THE LEADER LINES. SelectScreen.g_GsLineArray4 @ 0x80065484 holds four of
// them, 16 bytes apart, and this screen is what actually turns them on — SelectScreen arms all four
// with attribute 0x80000000, which GsSortLine suppresses. FUN_80028b38 clears bit 31 on [0] and [1]
// every frame (the CURSOR's line) and on [2] and [3] only while a row is held (the HELD row's
// line). Each pair draws an elbow: [0] from (-0x80, y) to (-0x10, y), then [1] from (-0x10, y) to
// the endpoint the eleven-word table at 0x80020600 gives for that row.
internal static class ButtonConfigScreen
{
    // GHIDRA: DAT_80020600 @ 0x80020600
    // ELEVEN WORDS of .rodata, 0x80020600..0x8002062B, read out of the image one word at a time.
    // FUN_80028b38 block-copies them onto its stack (`local_88`) and then reads each as a PACKED
    // POINT — low halfword to GsLINE.x1, high halfword to GsLINE.y1 — which is what closes the
    // layout as { short x; short y; }. One entry per configurable row, in row order.
    // The extent is the copy's own: eight words in the do/while (it stops at &DAT_80020620) plus
    // three more after it.
    private static readonly uint[] DAT_80020600 =
    {
        0x00140028, // 00  ( 40,  20)
        0x00240028, // 01  ( 40,  36)
        0x001E0085, // 02  (133,  30)
        0x001E0066, // 03  (102,  30)
        0x00140078, // 04  (120,  20)
        0x00260076, // 05  (118,  38)
        0xFFFB0028, // 06  ( 40,  -5)
        0xFFF60028, // 07  ( 40, -10)
        0xFFFB0078, // 08  (120,  -5)
        0xFFF60078, // 09  (120, -10)
        0x00240044, // 10  ( 68,  36)
    };

    // GHIDRA: __adddf3 @ 0x8004DAEC
    // JUSTIFICATION: C# language bridge only
    // RELATION: libgcc's soft-float double add, eight call sites in BuildButtonConfigScreen and
    // eight in UnwindButtonConfigScreen. PsxSdkMonogame.LibGcc exposes __floatsidf, __muldf3,
    // __divdf3, __subdf3, __fixdfsi and __ltdf2 but NOT __adddf3, and this slice is not allowed to
    // edit that file. It is declared here so the bodies below can call the name Ghidra prints; it
    // BELONGS IN LibGcc.cs and should be hoisted there, then deleted from here.
    private static double __adddf3(double param_1, double param_2)
    {
        return param_1 + param_2;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: shorthand for GsSPRITE_ARRAY_800654ec @ 0x800654EC, which the three bodies below
    // touch around a hundred and fifty times. It returns the same array object, never a copy, so
    // every write through it is a write to that global. Same alias, same reason, as MenuIntro.cs
    // and CharacterSelect.cs.
    private static LibGs.GsSPRITE[] Sprites => SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec;

    // JUSTIFICATION: C# language bridge only
    // RELATION: shorthand for g_GsLineArray4 @ 0x80065484. Ghidra spells every field of elements
    // 1, 2 and 3 as a separate DAT_ symbol because they are just offsets from the array base; the
    // DAT_ name each write carries is kept in a trailing comment so the mapping stays auditable.
    private static LibGs.GsLINE[] Lines => SelectScreen.g_GsLineArray4;

    // GHIDRA: RunButtonConfigScreen @ 0x80031C44
    // Seventy-two bytes, four calls, no branches. Its one caller is RunOptionsScreen @ 0x80031A1C
    // line 141, on the `g_OptionsCursor == 2` arm, and it hands on the player index DAT_80055B10.
    // a0 is untouched between the entry and `jal 0x8002c048` at 0x80031C50 and again before
    // `jal 0x80028b38` at 0x80031C58, which is why both bodies receive param_1 even though Ghidra's
    // stored prototype for BuildButtonConfigScreen still says (void).
    //
    // THE PARAMETER TYPES ARE GHIDRA'S OWN and they differ across the boundary: Ghidra decompiles
    // this entry point as `undefined4 param_1` — the caller's DAT_80055B10, which SELECT_EXE_exe.cs
    // already models as uint — and both callees as `int param_1`. The cast below is that boundary
    // and nothing else; the value is only ever 0 or 1.
    internal static void RunButtonConfigScreen(uint param_1)
    {
        BuildButtonConfigScreen((int)param_1);
        FUN_80028b38((int)param_1);
        UnwindButtonConfigScreen();

        // `InitializeSpriteArray(&GsSPRITE_ARRAY_800654ec[0x1e].attribute, 0xc)` — element 30,
        // twelve entries: exactly the twelve rows BuildButtonConfigScreen armed.
        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 0x1e, 0xc);
    }

    // GHIDRA: BuildButtonConfigScreen @ 0x8002C048
    // 1908 bytes. THE PLAY-IN. Two phases:
    //   1. arm the twelve row sprites 30..41 — tpage 0x0B, x = -0x11, w/h = 0x70/0x10,
    //      mx/my = 0x6F/8, cx/cy = 0x170/0x1FB, colour 0x40 — and set each one's `v` from the pad
    //      mask its row currently maps to: `PadMaskToButtonIndex(table[order[k]]) * 0x10 + 0x20`,
    //      so the row's LABEL follows the assignment;
    //   2. run the transition. iVar10 walks 0 -> 0x5B in steps of 2, one DrawFrame per step. Below
    //      0x28 the thirteen options sprites (elements 6..18) slide left 0x10 a frame and switch
    //      themselves off past x < -0x7F; at and above 0x28 the eleven rows fan out on a sine-free
    //      arc, y = local_38 * (iVar12 / 50.0) + 8 with iVar12 stepping -0x54 by 0x11, and scale
    //      ramps as local_38 * 81.92 (= 4096/50, so 0x1000 at local_38 = 50).
    //
    // PARTIAL: THE TWELFTH ROW READS UNINITIALISED STACK. The prologue writes exactly ELEVEN bytes
    // at sp+0x10..sp+0x1A (two swl/swr pairs plus three sb), and the loop below runs iVar13 = 0..11
    // — twelve iterations — so `local_50[11]` at sp+0x1B is never stored. The original therefore
    // indexes the remap table with whatever the frame happens to hold there. C# cannot read an
    // unassigned local, so the array carries a twelfth element left at 0, which selects table
    // entry 0 (0x0020 -> button index 4 -> v = 0x60). RULE 12: the original's out-of-range read is
    // reproduced, not corrected; the value it lands on is the only part that cannot be.
    internal static void BuildButtonConfigScreen(int param_1)
    {
        sbyte cVar4;
        short sVar5;
        short uVar6;
        short uVar7;
        double uVar8;
        double uVar14;
        double uVar15;
        int iVar10;
        int iVar11;
        int iVar12;
        int iVar13;
        int local_38;
        int local_30;

        // The eleven bytes the prologue stores at sp+0x10..sp+0x1A, plus the twelfth the loop reads
        // and the prologue never writes — see the PARTIAL note above.
        byte[] local_50 = { 0x06, 0x07, 0x00, 0x01, 0x02, 0x03, 0x0C, 0x0D, 0x0A, 0x0B, 0x08, 0x00 };

        iVar13 = 0;
        iVar10 = 0x438;
        Sprites[4].g = 0x80;
        Sprites[4].r = 0x80;
        do
        {
            // 0x438 / 0x24 = element 30, then 31..41.
            int e = iVar10 / 0x24;

            Sprites[e].tpage = 0xb;
            Sprites[e].x = unchecked((short)0xffef);
            int pbVar1 = iVar13;
            iVar13 = iVar13 + 1;

            // `PadMaskToButtonIndex(g_PadRemapTablePointers2[param_1][*pbVar1])` — the pointer array
            // holds PSX addresses, and the table it points at is u_short, so the byte address is
            // base + index * 2.
            cVar4 = (sbyte)OverlayExit.PadMaskToButtonIndex(
                PsxRam.ReadU16(OverlayExit.g_PadRemapTablePointers2[param_1] + local_50[pbVar1] * 2));
            Sprites[e].v = (byte)(cVar4 * 0x10 + 0x20);
            Sprites[e].w = 0x70;
            Sprites[e].h = 0x10;
            Sprites[e].mx = 0x6f;
            Sprites[e].my = 8;
            Sprites[e].cx = 0x170;
            Sprites[e].cy = 0x1fb;
            Sprites[e].b = 0x40;
            Sprites[e].g = 0x40;

            // Possible PsyQ macro: setLineF2()
            Sprites[e].r = 0x40;
            iVar10 = iVar10 + 0x24;
        }
        while (iVar13 < 0xc);

        iVar10 = 0;
        Sprites[0x29].x = 0x34;
        Sprites[0x29].y = -0x20;
        Sprites[0x29].u = 0xc0;

        // `(char)param_1 * '\x10' + '`'` — the 1P / 2P caption row, 0x60 or 0x70.
        Sprites[0x29].v = (byte)((sbyte)param_1 * 0x10 + 0x60);
        Sprites[0x29].w = 0x38;
        Sprites[0x29].b = 0x80;
        Sprites[0x29].g = 0x80;
        Sprites[0x29].r = 0x80;
        Sprites[0x29].attribute = 0x80000000;
        Sprites[0x2a].tpage = 0x1d;
        Sprites[0x2a].y = 0x18;
        Sprites[0x2a].v = 0x28;
        Sprites[0x2a].w = 0x80;
        Sprites[0x2a].cx = 0x100;
        Sprites[0x2a].cy = 0x1f1;
        Sprites[0x2a].mx = 0x40;
        Sprites[0x2a].my = 0x28;
        Sprites[0x2a].scaley = 0x88;
        Sprites[0x2a].scalex = 0x88;
        local_38 = -0x28;
        local_30 = 0;
        Sprites[0x28].u = 0;
        Sprites[0x29].mx = 0;
        Sprites[0x29].my = 0;
        Sprites[0x2a].x = 0x50;
        Sprites[0x2a].h = 0x50;
        Sprites[0x2a].attribute = 0x1000000;
        do
        {
            if (iVar10 < 0x28)
            {
                iVar11 = 0;
                iVar13 = 0xd8;
                do
                {
                    // 0xd8 / 0x24 = element 6, then 7..18.
                    int e = iVar13 / 0x24;

                    sVar5 = (short)(Sprites[e].x + -0x10);
                    Sprites[e].x = sVar5;
                    iVar11 = iVar11 + 1;
                    if (sVar5 < -0x7f)
                    {
                        Sprites[e].attribute = 0x80000000;
                    }

                    iVar13 = iVar13 + 0x24;
                }
                while (iVar11 < 0xd);
            }
            else
            {
                iVar11 = 0;
                uVar14 = LibGcc.__floatsidf(local_38);
                iVar12 = -0x54;
                iVar13 = 0x438;

                // 0x40547AE147AE147B = 81.92 = 4096 / 50.
                uVar15 = LibGcc.__muldf3(uVar14, 81.92);
                uVar6 = (short)LibGcc.__fixdfsi(uVar15);
                do
                {
                    // 0x438 / 0x24 = element 30, then 31..40 — ELEVEN rows, not twelve.
                    int e = iVar13 / 0x24;

                    uVar15 = LibGcc.__floatsidf(iVar12);
                    iVar12 = iVar12 + 0x11;
                    iVar11 = iVar11 + 1;

                    // 0x4049000000000000 = 50.0, 0x4020000000000000 = 8.0.
                    uVar15 = LibGcc.__divdf3(uVar15, 50.0);
                    uVar15 = LibGcc.__muldf3(uVar15, uVar14);
                    uVar15 = __adddf3(uVar15, 8.0);
                    uVar7 = (short)LibGcc.__fixdfsi(uVar15);
                    Sprites[e].y = uVar7;
                    Sprites[e].scaley = uVar6;
                    Sprites[e].scalex = uVar6;
                    Sprites[e].attribute = 0;
                    iVar13 = iVar13 + 0x24;
                }
                while (iVar11 < 0xb);

                uVar14 = LibGcc.__floatsidf(0x5a - iVar10);
                Sprites[0xc].attribute = 0x80000000;
                Sprites[9].attribute = 0x80000000;
                Sprites[6].attribute = 0x80000000;

                // 0x4011EB851EB851EC = 4.48, 0x405E000000000000 = 120.0, 0x4030000000000000 = 16.0.
                uVar14 = LibGcc.__muldf3(uVar14, 4.48);
                uVar14 = LibGcc.__subdf3(120.0, uVar14);
                uVar14 = __adddf3(uVar14, 16.0);
                Sprites[1].y = (short)LibGcc.__fixdfsi(uVar14);
                if (-0x60 < Sprites[4].y)
                {
                    iVar13 = Sprites[4].y + -9;
                    Sprites[4].y = (short)iVar13;

                    // `iVar13 * 0x10000 >> 0x10` is the sign-extended low halfword.
                    if ((short)iVar13 < -0x60)
                    {
                        Sprites[4].y = -0x60;
                    }
                }

                // Ghidra splits this double into the register pair (uVar8, uVar9) and feeds the SAME
                // pair to the three multiplies below; uVar8 is that saved value.
                uVar14 = LibGcc.__floatsidf(0x5a - iVar10);
                uVar8 = uVar14;

                // 0x3FF999999999999A = 1.6, 0xC061000000000000 = -136.0.
                uVar14 = LibGcc.__muldf3(uVar8, 1.6);
                uVar14 = __adddf3(uVar14, -136.0);
                uVar14 = __adddf3(uVar14, 8.0);
                Sprites[2].y = (short)LibGcc.__fixdfsi(uVar14);

                // 0x4001EB851EB851EC = 2.24.
                uVar14 = LibGcc.__muldf3(uVar8, 2.24);
                uVar14 = __adddf3(uVar14, -136.0);
                uVar14 = __adddf3(uVar14, 8.0);
                Sprites[3].y = (short)LibGcc.__fixdfsi(uVar14);

                // 0x400C28F5C28F5C29 = 3.52.
                uVar14 = LibGcc.__muldf3(uVar8, 3.52);
                uVar14 = __adddf3(uVar14, -136.0);
                uVar14 = __adddf3(uVar14, 8.0);
                Sprites[5].y = (short)LibGcc.__fixdfsi(uVar14);
                Sprites[1].scalex = (short)((short)local_38 * -8 + 0x1000);
                Sprites[2].scalex = Sprites[1].scalex;
                Sprites[3].scalex = Sprites[1].scalex;
                Sprites[5].scalex = Sprites[1].scalex;
                if (0x28 < iVar10)
                {
                    local_38 = local_38 + 2;
                    iVar10 = iVar10 + 2;
                    local_30 = local_30 + 0x574e;
                }
            }

            if (Sprites[0x2a].scalex < 0x1000)
            {
                Sprites[0x2a].scalex = (short)(Sprites[0x2a].scaley + 0x7e);
                Sprites[0x2a].scaley = Sprites[0x2a].scalex;
            }

            if (iVar10 < 0x28)
            {
                Sprites[0x2a].rotate = local_30 << 1;
            }
            else
            {
                Sprites[0x2a].rotate = (local_38 / 2 + 0x28) * 0x574e;
            }

            iVar10 = iVar10 + 2;
            FrameStep.DrawFrame();
            local_38 = local_38 + 2;
            local_30 = local_30 + 0x574e;
        }
        while (iVar10 < 0x5b);

        Sprites[0x29].attribute = 0;
        FrameStep.DrawFrame();
    }

    // GHIDRA: FUN_80028b38 @ 0x80028B38
    // 2892 bytes, TWO callees: PadInput.FUN_80026208 and FrameStep.DrawFrame. THE BLOCKING SWAP
    // LOOP the config screen sits in. `while (true)` with no break — the two `return`s below are the
    // only exits, and both are the same case: O (0x40) pressed while nothing is held.
    //
    // THE TWO CURSORS. iVar16 is the row the player is on, 0..10; iVar17 is the row being HELD,
    // -1 when nothing is held. X (0x20) picks a row up when nothing is held, and DROPS it onto the
    // current row when something is. O (0x40) cancels a hold, and quits when there is nothing to
    // cancel. Left (0x1000) and right (0x4000) walk iVar16 and skip over iVar17.
    //
    // WHAT A DROP ACTUALLY DOES, and it is the whole point of this screen:
    //     * fifteen frames of the two row sprites sliding past each other, 0x18 a frame;
    //     * `v` swapped between the two sprites, so the LABELS trade places;
    //     * the two u_short remap-table entries swapped through
    //       `*local_30 + local_99[index + 1]` — that is the real write to 0x801FF020 / 0x801FF03C;
    //     * fifteen more frames sliding back.
    // THE SAME BLOCK IS EMITTED TWICE, once on the edge path (uVar6, the raw pad word) and once on
    // the auto-repeat path (g_PadButtonWord, gated on local_40 so it cannot fire in the same frame
    // as the edge path). Rules 3 and 7: they are NOT merged into one helper here.
    //
    // THE AUTO-REPEAT. local_50 counts frames the pad has been held; past 12 it starts firing, and
    // iVar18 % 5 makes it fire one frame in five. local_48 is the "first press since the pad went
    // empty" latch. Both are reset whenever FUN_80026208 reports an empty pad.
    //
    // REGISTER ALIASING KEPT AS-IS. Ghidra hands iVar16, iVar7, iVar11 and iVar15 back and forth
    // between CURSOR indices (0..10) and SPRITE indices (cursor + 0x1E) inside the swap blocks —
    // e.g. `iVar16 = iVar17 + 0x1e` in the second slide loop, immediately overwritten by
    // `iVar16 = iVar11` (or `iVar16 = iVar7`) after the block. That is the original's own register
    // allocation and it is transliterated literally rather than tidied.
    internal static void FUN_80028b38(int param_1)
    {
        byte uVar1;
        ushort uVar2;
        uint uVar6;
        uint uVar10;
        uint uVar12;
        int iVar7;
        int iVar8;
        int puVar9;
        int iVar11;
        int puVar13;
        int puVar14;
        int iVar15;
        int iVar16;
        int iVar17;
        int iVar18;
        int local_58;
        int local_50;
        int local_48;
        int local_40;

        local_58 = param_1;

        // The eleven bytes of the order table, stored one byte late in this frame: the two swl/swr
        // pairs land at sp-0x98..sp-0x91 and the three sb at sp-0x90..sp-0x8E, while `local_99` is
        // named at sp-0x99. Every read below is `local_99[n + 1]` with n in 0..10, so index 0 is
        // never read — it is the one byte the prologue does not write.
        byte[] local_99 = { 0x00, 0x06, 0x07, 0x00, 0x01, 0x02, 0x03, 0x0C, 0x0D, 0x0A, 0x0B, 0x08 };

        uint[] local_88 = new uint[12];

        // The `if (true) { ... } else { ... }` Ghidra prints here is its alignment analysis of the
        // block copy: both ends are word-aligned, so only the aligned arm is reachable and only it
        // is transliterated. Eight words in the do/while (it stops at &DAT_80020620), three after.
        puVar14 = 0;
        puVar13 = 0;
        do
        {
            uVar6 = DAT_80020600[puVar13 + 1];
            uVar10 = DAT_80020600[puVar13 + 2];
            uVar12 = DAT_80020600[puVar13 + 3];
            local_88[puVar14] = DAT_80020600[puVar13];
            local_88[puVar14 + 1] = uVar6;
            local_88[puVar14 + 2] = uVar10;
            local_88[puVar14 + 3] = uVar12;
            puVar13 = puVar13 + 4;
            puVar14 = puVar14 + 4;
        }
        while (puVar13 != 8);

        uVar6 = DAT_80020600[puVar13];
        uVar10 = DAT_80020600[puVar13 + 1];
        uVar12 = DAT_80020600[puVar13 + 2];
        local_88[puVar14] = uVar6;
        local_88[puVar14 + 1] = uVar10;
        local_88[puVar14 + 2] = uVar12;

        iVar16 = 0;
        iVar17 = -1;
        iVar18 = 0;
        uint[] local_38 = local_88;
        local_50 = 0;
        local_48 = 0;

        // `local_30 = g_PadRemapTablePointers2 + local_58` — a pointer to the SLOT, re-dereferenced
        // at every use. The C# port models the pointer array as int[2] of PSX addresses, so the slot
        // is named by its index and `*local_30` is g_PadRemapTablePointers2[local_30].
        int local_30 = local_58;

        while (true)
        {
            uVar6 = PadInput.FUN_80026208(local_58);
            SELECT_EXE_exe.g_PadButtonWord = (int)(uVar6 & 0xffff);
            if (SELECT_EXE_exe.g_PadButtonWord == 0)
            {
                iVar18 = 0;
                local_48 = 1;
                local_50 = 0;
            }

            local_40 = 0;
            if (((uVar6 & 0x20) != 0) || ((uVar6 & 0x40) != 0))
            {
                local_40 = 1;
                iVar11 = iVar16;
                iVar7 = iVar17;
                if ((uVar6 & 0x20) != 0)
                {
                    iVar7 = iVar16 + 0x1e;
                    Sprites[iVar7].b = 0x80;
                    Sprites[iVar7].g = 0x80;
                    Sprites[iVar7].r = 0x80;
                    if (iVar17 == -1)
                    {
                        Lines[3].attribute = 0;                                  // DAT_800654b4
                        Lines[2].attribute = 0;                                  // DAT_800654a4
                        iVar11 = iVar16 + 1;
                        iVar7 = iVar16;
                        if (10 < iVar16 + 1)
                        {
                            iVar11 = 0;
                        }
                    }
                    else
                    {
                        Lines[2].y0 = (short)((short)iVar16 * 0x11 + -0x44);     // DAT_800654aa
                        iVar15 = 0;
                        Lines[3].x1 = (short)local_38[iVar16];                   // DAT_800654bc
                        Lines[3].y1 = (short)(local_38[iVar16] >> 16);           // DAT_800654be
                        Lines[2].y1 = Lines[2].y0;                               // DAT_800654ae
                        Lines[3].y0 = Lines[2].y0;                               // DAT_800654ba
                        do
                        {
                            Sprites[iVar7].x = (short)(Sprites[iVar7].x + 0x18);
                            Sprites[iVar17 + 0x1e].x = (short)(Sprites[iVar17 + 0x1e].x + -0x18);
                            iVar15 = iVar15 + 1;
                            FrameStep.DrawFrame();
                            iVar8 = iVar16 + 0x1e;
                        }
                        while (iVar15 < 0xf);

                        iVar15 = 0;
                        iVar7 = iVar17 + 0x1e;
                        uVar1 = Sprites[iVar8].v;
                        Sprites[iVar8].v = Sprites[iVar7].v;
                        Sprites[iVar7].v = uVar1;

                        // THE REMAP WRITE. `puVar9 = *local_30 + local_99[iVar16 + 1]` on a
                        // u_short *, so the byte address is base + index * 2.
                        puVar9 = OverlayExit.g_PadRemapTablePointers2[local_30] + local_99[iVar16 + 1] * 2;
                        uVar2 = PsxRam.ReadU16(puVar9);
                        PsxRam.WriteU16(
                            puVar9,
                            PsxRam.ReadU16(OverlayExit.g_PadRemapTablePointers2[local_30] + local_99[iVar17 + 1] * 2));
                        PsxRam.WriteU16(
                            OverlayExit.g_PadRemapTablePointers2[local_30] + local_99[iVar17 + 1] * 2,
                            uVar2);
                        do
                        {
                            Sprites[iVar8].x = (short)(Sprites[iVar8].x + -0x18);
                            Sprites[iVar7].x = (short)(Sprites[iVar7].x + 0x18);
                            iVar15 = iVar15 + 1;
                            FrameStep.DrawFrame();
                            iVar16 = iVar17 + 0x1e;
                        }
                        while (iVar15 < 0xf);

                        iVar7 = -1;
                        Lines[3].attribute = 0x80000000;                         // DAT_800654b4
                        Lines[2].attribute = 0x80000000;                         // DAT_800654a4
                        Sprites[iVar16].b = 0x40;
                        Sprites[iVar16].g = 0x40;

                        // Possible PsyQ macro: setLineF2()
                        Sprites[iVar16].r = 0x40;
                    }
                }

                iVar16 = iVar11;
                iVar17 = iVar7;
                if ((SELECT_EXE_exe.g_PadButtonWord & 0x40) != 0)
                {
                    iVar11 = iVar11 + 0x1e;
                    if (iVar7 == -1)
                    {
                        return;
                    }

                    iVar17 = -1;
                    Lines[3].attribute = 0x80000000;                             // DAT_800654b4
                    Lines[2].attribute = 0x80000000;                             // DAT_800654a4
                    Sprites[iVar11].b = 0x40;
                    Sprites[iVar11].g = 0x40;

                    // Possible PsyQ macro: setLineF2()
                    Sprites[iVar11].r = 0x40;
                    iVar16 = iVar7;
                }
            }

            iVar11 = local_50 + 1;

            // `((0xc < local_50) && (iVar18 = iVar18 + 1, iVar18 == 1)) ||
            //  ((local_50 = iVar11, local_48 != 0 && (g_PadButtonWord != 0)))`
            // — two comma expressions with side effects that C# cannot spell inside an `if`. The
            // order and the short-circuiting are preserved exactly: the increment of iVar18 happens
            // only on the first disjunct, and `local_50 = iVar11` only when the first disjunct is
            // false.
            bool bCond;
            if (0xc < local_50)
            {
                iVar18 = iVar18 + 1;
                bCond = iVar18 == 1;
            }
            else
            {
                bCond = false;
            }

            if (!bCond)
            {
                local_50 = iVar11;
                bCond = (local_48 != 0) && (SELECT_EXE_exe.g_PadButtonWord != 0);
            }

            if (bCond)
            {
                iVar7 = iVar16 + 0x1e;
                if (local_48 != 0)
                {
                    local_48 = 0;
                }

                // Possible PsyQ macro: setLineF2()
                Sprites[iVar7].r = 0x40;
                Sprites[iVar7].g = 0x40;
                Sprites[iVar7].b = 0x40;
                if ((SELECT_EXE_exe.g_PadButtonWord & 0x4000) != 0)
                {
                    iVar16 = iVar16 + 1;
                    if (10 < iVar16)
                    {
                        iVar16 = 0;
                    }

                    if (iVar16 == iVar17)
                    {
                        iVar16 = iVar17 + 1;
                        if (10 < iVar16)
                        {
                            iVar16 = 0;
                        }
                    }
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x1000) != 0)
                {
                    iVar16 = iVar16 + -1;
                    if (iVar16 < 0)
                    {
                        iVar16 = 10;
                    }

                    if (iVar16 == iVar17)
                    {
                        iVar16 = iVar17 + -1;
                        if (iVar16 < 0)
                        {
                            iVar16 = 10;
                        }
                    }
                }

                iVar7 = iVar16;
                iVar15 = iVar17;
                local_50 = iVar11;

                // `((g_PadButtonWord & 0x20) != 0) && (iVar8 = iVar16 + 0x1e, local_40 == 0)` —
                // iVar8 is assigned whenever the mask is set, whether or not local_40 gates the
                // body. The `iVar8 = 0` on the other arm is inert: C# cannot see that bCond2 implies
                // the assignment ran, and the original's register is not read on that path either
                // (the swap blocks always store into iVar8 before reading it).
                bool bCond2;
                if ((SELECT_EXE_exe.g_PadButtonWord & 0x20) != 0)
                {
                    iVar8 = iVar16 + 0x1e;
                    bCond2 = local_40 == 0;
                }
                else
                {
                    iVar8 = 0;
                    bCond2 = false;
                }

                if (bCond2)
                {
                    Sprites[iVar8].b = 0x80;
                    Sprites[iVar8].g = 0x80;
                    Sprites[iVar8].r = 0x80;
                    if (iVar17 == -1)
                    {
                        Lines[3].attribute = 0;                                  // DAT_800654b4
                        Lines[2].attribute = 0;                                  // DAT_800654a4
                        iVar7 = iVar16 + 1;
                        iVar15 = iVar16;
                        if (10 < iVar16 + 1)
                        {
                            iVar7 = 0;
                        }
                    }
                    else
                    {
                        Lines[2].y0 = (short)((short)iVar16 * 0x11 + -0x44);     // DAT_800654aa
                        iVar15 = 0;
                        Lines[3].x1 = (short)local_38[iVar16];                   // DAT_800654bc
                        Lines[3].y1 = (short)(local_38[iVar16] >> 16);           // DAT_800654be
                        Lines[2].y1 = Lines[2].y0;                               // DAT_800654ae
                        Lines[3].y0 = Lines[2].y0;                               // DAT_800654ba
                        do
                        {
                            Sprites[iVar8].x = (short)(Sprites[iVar8].x + 0x18);
                            Sprites[iVar17 + 0x1e].x = (short)(Sprites[iVar17 + 0x1e].x + -0x18);
                            iVar15 = iVar15 + 1;
                            FrameStep.DrawFrame();
                            iVar11 = iVar16 + 0x1e;
                        }
                        while (iVar15 < 0xf);

                        iVar8 = 0;
                        iVar15 = iVar17 + 0x1e;
                        uVar1 = Sprites[iVar11].v;
                        Sprites[iVar11].v = Sprites[iVar15].v;
                        Sprites[iVar15].v = uVar1;

                        // THE REMAP WRITE, second emission — identical to the one on the edge path.
                        puVar9 = OverlayExit.g_PadRemapTablePointers2[local_30] + local_99[iVar16 + 1] * 2;
                        uVar2 = PsxRam.ReadU16(puVar9);
                        PsxRam.WriteU16(
                            puVar9,
                            PsxRam.ReadU16(OverlayExit.g_PadRemapTablePointers2[local_30] + local_99[iVar17 + 1] * 2));
                        PsxRam.WriteU16(
                            OverlayExit.g_PadRemapTablePointers2[local_30] + local_99[iVar17 + 1] * 2,
                            uVar2);
                        do
                        {
                            Sprites[iVar11].x = (short)(Sprites[iVar11].x + -0x18);
                            Sprites[iVar15].x = (short)(Sprites[iVar15].x + 0x18);
                            iVar8 = iVar8 + 1;
                            FrameStep.DrawFrame();
                            iVar16 = iVar17 + 0x1e;
                        }
                        while (iVar8 < 0xf);

                        iVar15 = -1;
                        Lines[3].attribute = 0x80000000;                         // DAT_800654b4
                        Lines[2].attribute = 0x80000000;                         // DAT_800654a4
                        Sprites[iVar16].b = 0x40;
                        Sprites[iVar16].g = 0x40;

                        // Possible PsyQ macro: setLineF2()
                        Sprites[iVar16].r = 0x40;
                    }
                }

                iVar16 = iVar7;
                iVar17 = iVar15;
                if (((SELECT_EXE_exe.g_PadButtonWord & 0x40) != 0) && (local_40 == 0))
                {
                    iVar7 = iVar7 + 0x1e;
                    if (iVar15 == -1)
                    {
                        return;
                    }

                    iVar17 = -1;
                    Lines[3].attribute = 0x80000000;                             // DAT_800654b4
                    Lines[2].attribute = 0x80000000;                             // DAT_800654a4
                    Sprites[iVar7].b = 0x40;
                    Sprites[iVar7].g = 0x40;

                    // Possible PsyQ macro: setLineF2()
                    Sprites[iVar7].r = 0x40;
                    iVar16 = iVar15;
                }
            }

            if (0xc < local_50)
            {
                iVar18 = iVar18 % 5;
            }

            iVar11 = iVar16 + 0x1e;
            Sprites[iVar11].r = 0x80;
            Sprites[iVar11].g = 0x80;
            Sprites[iVar11].b = 0x80;

            // THE CURSOR'S LEADER LINE, rebuilt every frame out of GsLINE[0] and GsLINE[1]: the
            // elbow (-0x80, y) -> (-0x10, y) -> (endpoint x, endpoint y). Its row is iVar17 when
            // something is held, iVar16 otherwise — Ghidra re-emits that select four times because
            // the original recomputes it per store, and it is kept that way.
            Lines[0].x0 = unchecked((short)0xff80);                              // DAT_80065488
            iVar11 = iVar16;
            if (iVar17 != -1)
            {
                iVar11 = iVar17;
            }

            Lines[0].y0 = (short)((short)iVar11 * 0x11 + -0x44);                 // DAT_8006548a
            Lines[1].x0 = unchecked((short)0xfff0);                              // DAT_80065498
            Lines[0].x1 = unchecked((short)0xfff0);                              // DAT_8006548c
            iVar11 = iVar16;
            if (iVar17 != -1)
            {
                iVar11 = iVar17;
            }

            Lines[1].x1 = (short)local_38[iVar11];                               // DAT_8006549c
            iVar11 = iVar16;
            if (iVar17 != -1)
            {
                iVar11 = iVar17;
            }

            Lines[1].y1 = (short)(local_38[iVar11] >> 16);                       // DAT_8006549e
            Lines[1].attribute = 0;                                              // DAT_80065494
            Lines[0].attribute = 0;                                              // g_GsLineArray4
            Lines[0].y1 = Lines[0].y0;                                           // DAT_8006548e
            Lines[1].y0 = Lines[0].y0;                                           // DAT_8006549a
            if (iVar17 != -1)
            {
                // THE HELD ROW'S LEADER LINE, GsLINE[2] and GsLINE[3], same elbow shape for iVar16.
                Lines[2].y0 = (short)((short)iVar16 * 0x11 + -0x44);             // DAT_800654aa
                Lines[2].x0 = unchecked((short)0xff80);                          // DAT_800654a8
                Lines[3].x0 = unchecked((short)0xfff0);                          // DAT_800654b8
                Lines[2].x1 = unchecked((short)0xfff0);                          // DAT_800654ac
                Lines[3].x1 = (short)local_38[iVar16];                           // DAT_800654bc
                Lines[3].y1 = (short)(local_38[iVar16] >> 16);                   // DAT_800654be
                iVar11 = iVar17 + 0x1e;
                Lines[2].y1 = Lines[2].y0;                                       // DAT_800654ae
                Lines[3].y0 = Lines[2].y0;                                       // DAT_800654ba
                Sprites[iVar11].b = 0x80;
                Sprites[iVar11].g = 0x80;
                Sprites[iVar11].r = 0x80;
            }

            FrameStep.DrawFrame();
        }
    }

    // GHIDRA: UnwindButtonConfigScreen @ 0x8002C7BC
    // 1096 bytes. THE PLAY-OUT, and the mirror image of BuildButtonConfigScreen's second phase.
    // iVar10 walks 0x5A down to -1 in steps of 2. While it is still at or above 0x28 the eleven row
    // sprites fold back on the same arc with the scale ramp reversed; below 0x28 (bVar1) the
    // thirteen options sprites slide back in from the left 0x10 a frame and re-enable themselves
    // past x > -0x81, and GsSPRITE[5].scalex is pinned to 0x1000 INSIDE that inner loop — thirteen
    // redundant stores a frame, which is the original's own shape and is not hoisted.
    //
    // bVar1 IS SET FROM THE PREVIOUS ITERATION'S iVar10, not the current one, so the first frame at
    // iVar10 < 0x28 still takes the fold-back arm. Reproduced literally.
    //
    // NOTE THE ONE CONSTANT THAT DIFFERS FROM THE BUILD: GsSPRITE[3].y uses 2.22
    // (0x4001C28F5C28F5C3) here where BuildButtonConfigScreen uses 2.24 (0x4001EB851EB851EC). The
    // play-in and the play-out are therefore NOT exact mirrors, and neither is corrected toward the
    // other — rule 12.
    internal static void UnwindButtonConfigScreen()
    {
        bool bVar1;
        short sVar2;
        short uVar3;
        short uVar4;
        double uVar5;
        int iVar7;
        int iVar8;
        int iVar9;
        int iVar10;
        int iVar11;
        double uVar12;
        double uVar13;

        iVar10 = 0x5a;
        Lines[3].attribute = 0x80000000;                                         // DAT_800654b4
        Lines[2].attribute = 0x80000000;                                         // DAT_800654a4
        Lines[1].attribute = 0x80000000;                                         // DAT_80065494
        Lines[0].attribute = 0x80000000;                                         // g_GsLineArray4
        Sprites[0x29].attribute = 0x80000000;
        bVar1 = false;
        do
        {
            iVar8 = 0;
            if (bVar1)
            {
                iVar7 = 0xd8;
                do
                {
                    // 0xd8 / 0x24 = element 6, then 7..18.
                    int e = iVar7 / 0x24;

                    sVar2 = (short)(Sprites[e].x + 0x10);
                    Sprites[e].x = sVar2;
                    iVar8 = iVar8 + 1;
                    if (-0x81 < sVar2)
                    {
                        Sprites[e].attribute = 0;
                    }

                    iVar7 = iVar7 + 0x24;
                    Sprites[5].scalex = 0x1000;
                }
                while (iVar8 < 0xd);
            }
            else
            {
                iVar11 = iVar10 + -2;
                uVar12 = LibGcc.__floatsidf(iVar10 + -0x2a);
                iVar9 = -0x54;
                iVar7 = 0x438;

                // 0x40547AE147AE147B = 81.92 = 4096 / 50.
                uVar13 = LibGcc.__muldf3(uVar12, 81.92);
                uVar3 = (short)LibGcc.__fixdfsi(uVar13);
                do
                {
                    // 0x438 / 0x24 = element 30, then 31..40.
                    int e = iVar7 / 0x24;

                    uVar13 = LibGcc.__floatsidf(iVar9);
                    iVar9 = iVar9 + 0x11;
                    iVar8 = iVar8 + 1;

                    // 0x4049000000000000 = 50.0, 0x4020000000000000 = 8.0.
                    uVar13 = LibGcc.__divdf3(uVar13, 50.0);
                    uVar13 = LibGcc.__muldf3(uVar13, uVar12);
                    uVar13 = __adddf3(uVar13, 8.0);
                    uVar4 = (short)LibGcc.__fixdfsi(uVar13);
                    Sprites[e].y = uVar4;
                    Sprites[e].scaley = uVar3;
                    Sprites[e].scalex = uVar3;
                    Sprites[e].attribute = 0;
                    iVar7 = iVar7 + 0x24;
                }
                while (iVar8 < 0xb);

                Sprites[0xc].attribute = 0x80000000;
                Sprites[9].attribute = 0x80000000;
                Sprites[6].attribute = 0x80000000;

                // 0x4011EB851EB851EC = 4.48, 0x405E000000000000 = 120.0, 0x4030000000000000 = 16.0.
                uVar12 = LibGcc.__floatsidf(0x5a - iVar11);
                uVar12 = LibGcc.__muldf3(uVar12, 4.48);
                uVar12 = LibGcc.__subdf3(120.0, uVar12);
                uVar12 = __adddf3(uVar12, 16.0);
                Sprites[1].y = (short)LibGcc.__fixdfsi(uVar12);
                if (Sprites[4].y < 0x10)
                {
                    iVar8 = Sprites[4].y + 9;
                    Sprites[4].y = (short)iVar8;

                    // `iVar8 * 0x10000 >> 0x10` is the sign-extended low halfword.
                    if (0x10 < (short)iVar8)
                    {
                        Sprites[4].y = 0x10;
                    }
                }

                // Ghidra splits this double into the register pair (uVar5, uVar6) and feeds the SAME
                // pair to the three multiplies below; uVar5 is that saved value.
                uVar12 = LibGcc.__floatsidf(0x5a - iVar11);
                uVar5 = uVar12;

                // 0x3FF999999999999A = 1.6, 0xC061000000000000 = -136.0.
                uVar12 = LibGcc.__muldf3(uVar5, 1.6);
                uVar12 = __adddf3(uVar12, -136.0);
                uVar12 = __adddf3(uVar12, 8.0);
                Sprites[2].y = (short)LibGcc.__fixdfsi(uVar12);

                // 0x4001C28F5C28F5C3 = 2.22 — the constant that differs from the build's 2.24.
                uVar12 = LibGcc.__muldf3(uVar5, 2.22);
                uVar12 = __adddf3(uVar12, -136.0);
                uVar12 = __adddf3(uVar12, 8.0);
                Sprites[3].y = (short)LibGcc.__fixdfsi(uVar12);

                // 0x400C28F5C28F5C29 = 3.52.
                uVar12 = LibGcc.__muldf3(uVar5, 3.52);
                uVar12 = __adddf3(uVar12, -136.0);
                uVar12 = __adddf3(uVar12, 8.0);
                Sprites[5].y = (short)LibGcc.__fixdfsi(uVar12);
                Sprites[5].scalex = (short)(((short)iVar10 + -0x2a) * -8 + 0x1000);
                iVar10 = iVar11;
            }

            if (0 < Sprites[0x2a].scalex)
            {
                Sprites[0x2a].scalex = (short)(Sprites[0x2a].scaley + -0x7e);
                Sprites[0x2a].scaley = Sprites[0x2a].scalex;
            }

            Sprites[0x2a].rotate = iVar10 << 0xe;
            iVar10 = iVar10 + -2;
            Sprites[1].scalex = Sprites[5].scalex;
            Sprites[2].scalex = Sprites[5].scalex;
            Sprites[3].scalex = Sprites[5].scalex;
            FrameStep.DrawFrame();
            bVar1 = iVar10 < 0x28;
        }
        while (-1 < iVar10);
    }
}
