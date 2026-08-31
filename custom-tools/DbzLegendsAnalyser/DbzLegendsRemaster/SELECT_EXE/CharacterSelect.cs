using PsxSdkMonogame;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE 3-ON-3 CHARACTER SELECT — RunVsTeamSelect @ 0x80031E98, 6040 bytes, the largest single function
// in SELECT.EXE and the last thing between the VS branch and LoadExec("cdrom:\\VS.EXE;1").
//
// ONE CALLER, ONE EXIT CONTRACT. RunVsModeScreen @ 0x80030EF8 (ModeBranches) calls it from inside its
// sub-menu loop and then tests bit 2 of DAT_80055B80. This function writes that bit on its confirm
// path, at 0x80033488. It is NOT the only writer: 0x80055B80 has 27 references image-wide and THREE
// of them set bit 2 - main @ 0x80030550, FUN_8002cc04 @ 0x8002CDA8 and this one. All three are
// transliterated. What makes the VS branch depend on this one is that main CLEARS the word on every
// outer iteration before dispatching, so the bit the branch sees can only have come from inside the
// sub-menu.
//   (An earlier note here claimed exclusivity and a single instruction site. That was wrong, and it
//    came from a reconnaissance summary rather than from the cross references.)
// It returns 0 on confirm and 0xFFFFFFFF on every cancel path; the caller DISCARDS the return value
// and reads the bit instead.
//
// THE SHAPE: a blocking screen body, like every other screen in this overlay. No task, no
// scheduler. Four fixed animation loops run first, each calling the frame step DrawFrame
// @ 0x800344A4 once per frame, then one `do { ... } while (true)` that reads both pads, moves the
// cursors, repaints, calls the frame step, and only ever leaves through a `return`.
//
// SIX SLOTS OVER 35 PORTRAITS. The slots are the six ints at 0x8004F7EC..0x8004F803 — row 0 is
// slots 0..2, row 1 is slots 3..5 — each holding an index into two 35-entry byte tables, or -1 for
// "empty". The two tables are NOT interchangeable:
//   g_UsagiChunk18TileIndexMap35  @ 0x800206A8   picks the PORTRAIT TILE inside USAGI.B record 18
//   g_UsagiSelectionValueMap35    @ 0x800206CC   is the id EXPORTED to the VS roster block
// so a slot's artwork and a slot's exported character id come from different permutations of the
// same 0..34 index. Both tables are copied onto the stack on entry and read from the copy.
//
// HOW RECORD 18 REACHES THE SCREEN, which is what the recon left open. Record 18 is the one
// LoadUSAGI_B decodes to 0x80080000 and never uploads (SelectScreen.g_UsagiChunk18DecodedTiles).
// THIS FUNCTION IS THE UPLOADER. Every time a slot's value changes it runs, for that one slot,
//     LoadImage(&rect, 0x80080000 | tileIndex * 0x480); DrawSync(0);
// with rect = { x = 0x3C0 + column * 0xC, y = 0x100 + row * 0x30, w = 0xC, h = 0x30 } — twelve VRAM
// halfwords by forty-eight rows, i.e. one 48x48 4bpp tile, staged into the texture page the sprite
// then samples. The numbers agree three ways: the sprite's tpage 0x1F resolves to VRAM (960, 256)
// and 0x3C0 = 960, 0x100 = 256; the sprite's u = column * 0x30 and v = row * 0x30 are the 48-pixel
// steps that match twelve halfwords of 4bpp; and 35 tiles * 0x480 bytes = 40320, exactly the extent
// Ghidra types the symbol (ushort[20160]). The CLUT is picked per tile out of record 16's rows:
// cx = 0x100 + (tile % 7) * 0x10, cy = 0x1FB + tile / 7.
//
// BOTH PADS IN ONE FRAME, AND NOT THROUGH THE SAME READER. The loop opens with
// FUN_80026208(0) and FUN_80026208(1) — the debounced reader in PadInput. Then, in TWO-PLAYER mode
// only (param_1 == 0) and only while at least one player has not confirmed, pad 2's word is
// THROWN AWAY and re-read RAW through ReadPadButtons(1), which does no auto-repeat masking at all.
// Verified from the instruction stream, because two Ghidra renderings of this line disagreed:
//     0x800325d0  addu s5,v0,zero    s5 = FUN_80026208(0)   -> uVar8
//     0x800325e0  addu t0,v0,zero    t0 = FUN_80026208(1)   -> uVar9
//     0x8003260c  jal 0x800263e4
//     0x80032614  addu t0,v0,zero    t0 = ReadPadButtons(1)   -> uVar9, NOT uVar8
// and 0x80032618 (`andi v0,s5,0xffff`) confirms the uVar8 test still reads s5. Pad 1 therefore
// keeps its debounced word and pad 2 gets the raw one. Each pad then runs its own repeat cadence
// out of the three counters below it. Reproduced as measured — rule 12.
//
// WHICH SPRITES THIS FUNCTION TOUCHES. Only the first two rows are CLOSED — they are the elements
// the cursor arithmetic itself computes (`iVar28 * 4 + iVar24 + 9` and `iVar25 + 0xD`):
//   9, 10, 11   row 0's three portraits          12   row 0's confirm cell (column 3)
//   13, 14, 15  row 1's three portraits          16   row 1's confirm cell (column 3)
// PARTIAL - the rest are read off the animations they take part in rather than off the builder.
// FUN_8002a178 @ 0x8002A178, which arms their geometry, IS transliterated now, in
// ScreenDecoration.cs; these roles were inferred before that landed and have not been re-derived
// against it. Roles are given here as an inference and are deliberately NOT carried into any
// identifier:
//   7, 8        one per row, faded up with the row and dimmed when that row confirms
//   17, 18      one per row (0x11, 0x12), scaled in before the loop and dimmed on confirm
//   2, 3, 4     scaled from 0xE61 to 0 on entry
//   5           armed from scratch and scaled in once, on the confirm path only
// In two-player mode pad 1 is locked to row 0 and pad 2 to row 1. In one-player mode (param_1 != 0)
// pad 1 walks row 0 first and then row 1.
internal static class CharacterSelect
{
    // GHIDRA: g_UsagiChunk18TileIndexMap35 @ 0x800206A8
    // .rodata, byte[35]. Read straight out of the image. Slot value -> tile index inside record 18.
    private static readonly byte[] g_UsagiChunk18TileIndexMap35 =
    {
        0x00, 0x03, 0x08, 0x09, 0x0d, 0x10, 0x15, 0x14, 0x12, 0x13,
        0x11, 0x01, 0x16, 0x0b, 0x0e, 0x1a, 0x1b, 0x18, 0x19, 0x17,
        0x04, 0x1c, 0x1d, 0x05, 0x07, 0x0a, 0x0f, 0x1e, 0x1f, 0x02,
        0x06, 0x0c, 0x20, 0x22, 0x21,
    };

    // GHIDRA: g_UsagiSelectionValueMap35 @ 0x800206CC
    // .rodata, byte[35]. Slot value -> the halfword exported to 0x801FF102..0x801FF10C.
    private static readonly byte[] g_UsagiSelectionValueMap35 =
    {
        0x01, 0x04, 0x0e, 0x0f, 0x09, 0x14, 0x19, 0x18, 0x16, 0x17,
        0x15, 0x02, 0x1a, 0x0d, 0x0a, 0x1e, 0x1f, 0x1c, 0x1d, 0x1b,
        0x05, 0x23, 0x24, 0x06, 0x08, 0x0c, 0x25, 0x20, 0x12, 0x03,
        0x07, 0x10, 0x21, 0x11, 0x22,
    };

    // GHIDRA: g_UsagiChunk18TileRect12x48 @ 0x80055A70
    // .data, RECT { x = 0, y = 0, w = 0x0C, h = 0x30 }. Read from the image as `00 00 00 00 0C 00
    // 30 00`. Only w and h survive — every LoadImage below overwrites x and y first.
    private static readonly RECT g_UsagiChunk18TileRect12x48 = new RECT { x = 0, y = 0, w = 0x0c, h = 0x30 };

    // GHIDRA: g_MaxSelectionIndexTable8 @ 0x80055A68
    // .data, image value 0x130C0A05 — the bytes 05 0A 0C 13.
    private static readonly uint g_MaxSelectionIndexTable8 = 0x130c0a05;

    // GHIDRA: DAT_80055a6c @ 0x80055A6C
    // .data, image value 0x22201C16 — the bytes 16 1C 20 22.
    //
    // The two words are ADJACENT and this function copies them onto adjacent stack slots
    // (auStack_e8[4] at fp-0xE8, auStack_e4[4] at fp-0xE4) and then indexes ACROSS the pair with
    // `auStack_e8[DAT_801ff002]`, DAT_801FF002 running 0..7. So the pair is one eight-byte table
    //     5, 10, 12, 19, 22, 28, 32, 34
    // and the entry it selects is the HIGHEST SLOT VALUE the roster may hold at that unlock tier —
    // it is the bound both the increment (`> bound` wraps to -1) and the decrement (`< -1` wraps to
    // bound) test against. 34 is the last index of the 35-entry tables, so tier 7 is the full
    // roster. What advances DAT_801FF002 is RunDemoModeScreen, outside this slice.
    private static readonly uint DAT_80055a6c = 0x22201c16;

    // GHIDRA: DAT_80055b18 @ 0x80055B18
    // .sbss, undefined4. The unlock tier THIS SCREEN LAST RAN AT. Two references in the whole
    // image, both here: read at 0x80032060, written at 0x800320E0. If the tier has gone DOWN since
    // the last visit the six slots are wiped back to -1, otherwise the previous picks survive
    // across visits. Starts at 0 from start's .bss clear, so the first visit never wipes.
    private static uint DAT_80055b18;

    // GHIDRA: g_SelectionIndexSlots6 @ 0x8004F7EC .. DAT_8004f800 @ 0x8004F800
    // .data, SIX 32-bit slots, 0x8004F7EC..0x8004F803, every one of them 0xFFFFFFFF in the image
    // (read-memory: 24 bytes of FF, then zeros at 0x8004F804 — the extent closes itself).
    //   [0] g_SelectionIndexSlots6  [1] DAT_8004f7f0  [2] DAT_8004f7f4   row 0 / pad 1
    //   [3] DAT_8004f7f8  [4] DAT_8004f7fc  [5] DAT_8004f800   row 1 / pad 2
    // ONE array rather than six globals because the original addresses it as one: it walks
    // `&g_SelectionIndexSlots6 + iVar28 * 3 + iVar24` and `&DAT_8004f7f8 + iVar25` on an `int *`, and the
    // duplicate scan below sweeps all six through `piVar17 = piVar17 + 3`. All fourteen references
    // in the image are inside this function.
    private static readonly int[] g_SelectionIndexSlots6 = { -1, -1, -1, -1, -1, -1 };

    // JUSTIFICATION: C# language bridge only
    // RELATION: shorthand for GsSPRITE_ARRAY_800654ec @ 0x800654EC, which this function touches
    // around two hundred times. Same alias, same reason, as MenuIntro.cs.
    private static LibGs.GsSPRITE[] Sprites => SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec;

    // GHIDRA: RunVsTeamSelect @ 0x80031E98
    // 6040 bytes. Returns 0 on confirm (with bit 2 of DAT_80055B80 set) and 0xFFFFFFFF on cancel.
    //
    // param_1 is the VS sub-mode RunVsModeScreen picked, 0..2, and it is stored in local_88:
    //   0  TWO PLAYERS  — pad 1 owns row 0, pad 2 owns row 1, both must confirm
    //   1  and 2        — ONE player, walking row 0 then row 1 alone; they differ only in the u/v
    //                     the two name plates (sprites 7 and 8) are set to.
    internal static uint RunVsTeamSelect(int param_1)
    {
        byte uVar2;
        byte uVar3;
        byte bVar4;
        bool bVar5;
        ushort uVar8;
        ushort uVar9;
        int iVar10;
        uint uVar11;
        int iVar12;
        int piVar13;
        int puVar14;
        uint uVar15;
        int pbVar16;
        int piVar17;
        int puVar18;
        int puVar19;
        byte uVar20;
        short sVar21;
        int iVar22;
        int iVar23;
        int iVar24;
        int iVar25;
        int piVar26;
        uint uVar27;
        int iVar28;

        // The stack frame, spelled with the original's own names. The pointer-typed locals above
        // (piVar13/17/26, puVar14/18/19, pbVar16) are the original's roaming cursors; in C# they
        // carry an ELEMENT INDEX into the array they walked, and every increment below is the same
        // increment the original made, converted once by the element size.
        byte[] auStack_e8 = new byte[8];
        byte[] local_e0 = new byte[40];
        byte[] local_b8 = new byte[40];
        RECT local_90 = new RECT();
        int local_88;
        int local_80;
        int local_78;
        int local_70;
        int local_68;
        int local_60;
        int local_58;
        int local_50;
        int local_48;
        sbyte local_40;
        sbyte local_38;
        int local_30;

        local_88 = param_1;

        // Ghidra prints the two eight-byte copies below as an `swl`/`swr` pair followed by the
        // aligned `sw` — the compiler's unaligned-store idiom. The stack slot is word aligned, so
        // the partial store writes the same bytes the aligned one does and the net effect is the
        // plain word copy. Same for the three block copies that follow, where Ghidra prints the
        // alignment test already resolved as `if (true) { ... } else { ... }`: the `true` arm is
        // the aligned copy and the `else` arm is unreachable, so only the taken arm is written.
        MipsMemory.WriteU32(auStack_e8, 0, g_MaxSelectionIndexTable8);
        MipsMemory.WriteU32(auStack_e8, 4, DAT_80055a6c);

        // g_UsagiChunk18TileIndexMap35 -> local_e0, thirty-five bytes: two sixteen-byte passes
        // (`while (pbVar16 != base + 0x20)`) and then a three-byte tail.
        puVar19 = 0;
        pbVar16 = 0;
        do
        {
            uVar27 = MipsMemory.ReadU32(g_UsagiChunk18TileIndexMap35, pbVar16 + 4);
            uVar11 = MipsMemory.ReadU32(g_UsagiChunk18TileIndexMap35, pbVar16 + 8);
            uVar15 = MipsMemory.ReadU32(g_UsagiChunk18TileIndexMap35, pbVar16 + 0xc);
            MipsMemory.WriteU32(local_e0, puVar19, MipsMemory.ReadU32(g_UsagiChunk18TileIndexMap35, pbVar16));
            MipsMemory.WriteU32(local_e0, puVar19 + 4, uVar27);
            MipsMemory.WriteU32(local_e0, puVar19 + 8, uVar11);
            MipsMemory.WriteU32(local_e0, puVar19 + 0xc, uVar15);
            pbVar16 = pbVar16 + 0x10;
            puVar19 = puVar19 + 0x10;
        }
        while (pbVar16 != 0x20);

        uVar2 = g_UsagiChunk18TileIndexMap35[pbVar16 + 1];
        uVar3 = g_UsagiChunk18TileIndexMap35[pbVar16 + 2];
        local_e0[puVar19] = g_UsagiChunk18TileIndexMap35[pbVar16];
        local_e0[puVar19 + 1] = uVar2;
        local_e0[puVar19 + 2] = uVar3;

        // g_UsagiSelectionValueMap35 -> local_b8, the same shape.
        puVar19 = 0;
        pbVar16 = 0;
        do
        {
            uVar27 = MipsMemory.ReadU32(g_UsagiSelectionValueMap35, pbVar16 + 4);
            uVar11 = MipsMemory.ReadU32(g_UsagiSelectionValueMap35, pbVar16 + 8);
            uVar15 = MipsMemory.ReadU32(g_UsagiSelectionValueMap35, pbVar16 + 0xc);
            MipsMemory.WriteU32(local_b8, puVar19, MipsMemory.ReadU32(g_UsagiSelectionValueMap35, pbVar16));
            MipsMemory.WriteU32(local_b8, puVar19 + 4, uVar27);
            MipsMemory.WriteU32(local_b8, puVar19 + 8, uVar11);
            MipsMemory.WriteU32(local_b8, puVar19 + 0xc, uVar15);
            pbVar16 = pbVar16 + 0x10;
            puVar19 = puVar19 + 0x10;
        }
        while (pbVar16 != 0x20);

        uVar2 = g_UsagiSelectionValueMap35[pbVar16 + 1];
        uVar3 = g_UsagiSelectionValueMap35[pbVar16 + 2];
        local_b8[puVar19] = g_UsagiSelectionValueMap35[pbVar16];
        local_b8[puVar19 + 1] = uVar2;
        local_b8[puVar19 + 2] = uVar3;

        // `local_90._0_4_ = g_UsagiChunk18TileRect12x48._0_4_` and `._4_4_ = ._4_4_` — the eight
        // bytes of the RECT, which C# spells field by field because RECT is a class here.
        local_90.x = g_UsagiChunk18TileRect12x48.x;
        local_90.y = g_UsagiChunk18TileRect12x48.y;
        local_90.w = g_UsagiChunk18TileRect12x48.w;
        local_90.h = g_UsagiChunk18TileRect12x48.h;

        iVar24 = 9;
        if ((int)(uint)SharedHighRam.DAT_801ff002 < (int)DAT_80055b18)
        {
            // The six slots, wiped in DESCENDING address order, exactly as the original stores them.
            g_SelectionIndexSlots6[5] = -1;
            g_SelectionIndexSlots6[4] = -1;
            g_SelectionIndexSlots6[3] = -1;
            g_SelectionIndexSlots6[2] = -1;
            g_SelectionIndexSlots6[1] = -1;
            g_SelectionIndexSlots6[0] = -1;
        }

        DAT_80055b18 = SharedHighRam.DAT_801ff002;

        // ANIMATION 1 — the banner shrinking in. Ten frames, scalex 0xE61 down to 0 by 0x199.
        sVar21 = 0xe61;
        do
        {
            Sprites[2].scalex = sVar21;
            Sprites[3].scalex = sVar21;
            Sprites[4].scalex = sVar21;
            FrameStep.DrawFrame();
            iVar24 = iVar24 + -1;
            sVar21 = (short)(sVar21 - 0x199);
        }
        while (-1 < iVar24);

        if (local_88 == 0)
        {
            Sprites[0x12].r = 0x80;
        }
        else
        {
            Sprites[0x12].r = 0x40;
        }

        // ANIMATION 2 — the two side panels growing out. Ten frames, scalex 0 up to 0xE61.
        iVar24 = 0;
        sVar21 = 0;
        Sprites[0x12].g = Sprites[0x12].r;
        Sprites[0x12].b = Sprites[0x12].r;
        do
        {
            Sprites[0x12].attribute = 0;
            Sprites[0x11].attribute = 0;
            Sprites[0x11].scalex = sVar21;
            Sprites[0x12].scalex = sVar21;
            FrameStep.DrawFrame();
            iVar24 = iVar24 + 1;
            sVar21 = (short)(sVar21 + 0x199);
        }
        while (iVar24 < 10);

        Sprites[0x12].scalex = 0x1000;
        Sprites[0x11].scalex = 0x1000;

        // The two name plates. Which glyphs they show is the only thing the three sub-modes differ
        // in before the loop.
        if (local_88 == 1)
        {
            Sprites[7].u = 0x98;
            Sprites[7].v = 0xb8;
            Sprites[8].u = 0x69;
            Sprites[8].v = 0x68;
        }
        else if (local_88 < 2)
        {
            if (local_88 == 0)
            {
                Sprites[7].u = 0x98;
                Sprites[8].u = 0x98;
                Sprites[7].v = 0xb8;
                Sprites[8].v = 0xd8;
            }
        }
        else if (local_88 == 2)
        {
            Sprites[8].v = 0x68;
            Sprites[7].u = 0x69;
            Sprites[7].v = 0x68;
            Sprites[8].u = 0x69;
        }

        // ANIMATION 3 — the name plates fading up. Sixteen frames, brightness 0 to 0x78 by 8.
        // In one-player mode plate 8 is held at HALF brightness (`(iVar24 - (iVar24 >> 0x1f)) >> 1`,
        // the compiler's signed divide by two), because only plate 7 is live.
        iVar24 = 0;
        do
        {
            Sprites[8].attribute = 0;
            Sprites[7].attribute = 0;
            Sprites[7].r = (byte)iVar24;
            Sprites[8].r = Sprites[7].r;
            if (local_88 != 0)
            {
                Sprites[8].r = (byte)((uint)(iVar24 - (iVar24 >> 0x1f)) >> 1);
            }

            Sprites[7].g = Sprites[7].r;
            Sprites[7].b = Sprites[7].r;
            Sprites[8].g = Sprites[8].r;
            Sprites[8].b = Sprites[8].r;
            FrameStep.DrawFrame();
            iVar24 = iVar24 + 8;
        }
        while (iVar24 < 0x80);

        // THE INITIAL PORTRAIT UPLOAD. Two rows of three slots. Every slot that is not -1 has its
        // tile pushed into VRAM here, so a roster carried over from a previous visit repaints
        // before the first frame of the loop. A slot that IS -1 gets the placeholder u/v/cx/cy —
        // and note it does NOT get a tpage, unlike the same placeholder inside the loop below,
        // which sets tpage 0xD. That asymmetry is the original's; it is reproduced, not corrected.
        iVar24 = 0;
        iVar25 = 0;
        local_30 = 0x100;
        iVar28 = 0;
        piVar26 = 0;
        do
        {
            iVar22 = 0;
            sVar21 = 0x3c0;
            uVar20 = 0;
            local_80 = iVar28;
            local_78 = local_30;
            piVar17 = piVar26;
            do
            {
                if (g_SelectionIndexSlots6[piVar17] == -1)
                {
                    iVar10 = iVar25 + iVar22 + 9;
                    Sprites[iVar10].u = 0x38;
                    Sprites[iVar10].v = 0x58;
                    Sprites[iVar10].cx = 0x170;
                    Sprites[iVar10].cy = 0x1f9;
                }
                else
                {
                    iVar10 = iVar25 + iVar22 + 9;
                    Sprites[iVar10].tpage = 0x1f;
                    Sprites[iVar10].u = uVar20;
                    Sprites[iVar10].v = (byte)local_80;

                    // cx / cy pick the tile's 16-colour CLUT row. Ghidra prints the divide by seven
                    // twice over: `bVar4 / 7` is the `mult`/`mfhi` against the magic 0x24924925, and
                    // `(q + ((n - q) >> 1)) >> 2` is the correction sequence that follows it. For
                    // n = 7k + r with r < 7 the composite is k + (r >> 1) / 4 = k, so both halves
                    // are floor(n / 7) and the pair yields (tile % 7, tile / 7). Written the way it
                    // is printed rather than folded, because the fold is a proof, not a reading.
                    bVar4 = local_e0[g_SelectionIndexSlots6[piVar17]];
                    uVar27 = bVar4 / 7u;
                    Sprites[iVar10].cx = (short)(((((ushort)bVar4) + ((short)((uVar27 + ((bVar4 - uVar27) >> 1)) >> 2) * -7)) & 0xff) * 0x10 + 0x100);
                    uVar27 = local_e0[g_SelectionIndexSlots6[piVar17]] / 7u;
                    Sprites[iVar10].cy = (short)((((ushort)((uVar27 + ((local_e0[g_SelectionIndexSlots6[piVar17]] - uVar27) >> 1)) >> 2)) & 0xff) + 0x1fb);

                    // `local_90._2_2_` is RECT.y and `local_90._0_2_` is RECT.x; w and h are still
                    // the 12 x 48 the rect was copied with.
                    local_90.y = (short)local_78;
                    local_90.x = sVar21;
                    LoadImage(local_90, unchecked((int)((uint)local_e0[g_SelectionIndexSlots6[piVar17]] * 0x480 | 0x80080000u)));
                    DrawSync(0);
                }

                piVar17 = piVar17 + 1;
                sVar21 = (short)(sVar21 + 0xc);
                iVar22 = iVar22 + 1;
                uVar20 = (byte)(uVar20 + 0x30);
            }
            while (iVar22 < 3);

            iVar25 = iVar25 + 4;
            iVar28 = iVar28 + 0x30;
            piVar26 = piVar26 + 3;
            iVar24 = iVar24 + 1;
            local_30 = local_30 + 0x30;
        }
        while (iVar24 < 2);

        // ANIMATION 4 — the six portraits fading up, twelve frames, brightness 0/0x10/0x20/0x30.
        //
        // The original addresses these as BYTE OFFSETS from element 0 and Ghidra spells the same
        // two addresses four different ways:
        //     (&GsSPRITE_ARRAY_800654ec[0].b)[iVar25]                    element iVar25 / 0x24, .b
        //     *(uchar *)(iVar25 + -0x7ff9ab90)   = 0x80065470 + iVar25   element iVar22 / 0x24, .r
        //     *(undefined4 *)((int)&...[0].attribute + iVar25)           element iVar25 / 0x24
        //     *(undefined4 *)(iVar25 + -0x7ff9aba4) = 0x8006545C + ...   element iVar22 / 0x24
        // 0x80065470 and 0x8006545C are 0x90 = 4 * 36 BELOW the two array bases, and iVar25 - iVar22
        // is 0x90 as well, so both pairs land on the same element. Both cursors start on an exact
        // element boundary — 0x1D4 = 13 * 36 and 0x144 = 9 * 36 — and step one whole element.
        iVar24 = 0;
        do
        {
            iVar28 = 0;
            iVar25 = 0x1d4;
            iVar22 = 0x144;
            do
            {
                uVar20 = (byte)(iVar24 << 4);
                Sprites[iVar25 / 0x24].b = uVar20;
                Sprites[iVar25 / 0x24].g = uVar20;
                Sprites[iVar25 / 0x24].r = uVar20;
                Sprites[iVar22 / 0x24].b = uVar20;
                Sprites[iVar22 / 0x24].g = uVar20;
                Sprites[iVar22 / 0x24].r = uVar20;
                Sprites[iVar25 / 0x24].attribute = 0;
                Sprites[iVar22 / 0x24].attribute = 0;
                iVar25 = iVar25 + 0x24;
                FrameStep.DrawFrame();
                iVar28 = iVar28 + 1;
                iVar22 = iVar22 + 0x24;
            }
            while (iVar28 < 3);

            iVar24 = iVar24 + 1;

            // NOT a dead store: iVar25 is the NEXT loop's counter and the compiler hoisted its
            // initialisation up here. It is written the way it is emitted.
            iVar25 = 0;
        }
        while (iVar24 < 4);

        // The two OK buttons fading up, four frames.
        iVar24 = 0;
        do
        {
            Sprites[0xc].r = (byte)iVar24;
            Sprites[0x10].attribute = 0;
            Sprites[0xc].attribute = 0;
            Sprites[0xc].g = Sprites[0xc].r;
            Sprites[0xc].b = Sprites[0xc].r;
            Sprites[0x10].r = Sprites[0xc].r;
            Sprites[0x10].g = Sprites[0xc].r;
            Sprites[0x10].b = Sprites[0xc].r;
            FrameStep.DrawFrame();
            iVar25 = iVar25 + 1;
            iVar24 = iVar25 * 0x10;
        }
        while (iVar25 < 4);

        // THE LOOP STATE.
        //   iVar24  pad 1's column, 0..3, where 3 is the OK button
        //   iVar28  pad 1's row, 0 or 1 (never leaves 0 in two-player mode)
        //   iVar25  pad 2's column, 0..3
        //   uVar27  which players are still LIVE: bit 0 pad 1, bit 1 pad 2. Confirming clears the
        //           bit; cancelling puts it back. Reaching 0 is the exit condition.
        //   local_40 / local_38  pad 1's / pad 2's confirm state
        //   local_68 / local_50  how many frames the pad has been held
        //   local_70 / local_58  the repeat phase, taken modulo 5 once past frame 12
        //   local_60 / local_48  "the pad was released, take the next press immediately"
        iVar24 = 0;
        iVar28 = 0;
        iVar25 = 0;
        uVar27 = 3;
        local_40 = 0;
        local_38 = 0;
        local_58 = 0;
        local_70 = 0;
        local_50 = 0;
        local_68 = 0;
        local_48 = 0;
        local_60 = 0;
        do
        {
            uVar8 = PadInput.FUN_80026208(0);
            uVar9 = PadInput.FUN_80026208(1);

            // JUSTIFICATION: C# language bridge only
            // RELATION: Ghidra prints LAB_80032618 INSIDE the `else` arm and reaches it with a
            // `goto` out of the `then` arm; the only path that SKIPS the body is the
            // `j 0x800333dc` at 0x80032604, whose delay slot clears uVar27. C# forbids a goto into
            // a block, so "does the body run this pass" is carried in a local. The order of the
            // tests, the re-read of pad 2 and the clearing of uVar27 are unchanged.
            bool bBody = true;
            if (local_88 == 0)
            {
                if ((local_40 == 0) || (local_38 == 0))
                {
                    uVar9 = PadInput.ReadPadButtons(1);
                }
                else
                {
                    uVar27 = 0;
                    bBody = false;
                }
            }

            if (bBody)
            {
                // LAB_80032618
                if (uVar8 == 0)
                {
                    local_60 = 1;
                    local_70 = 0;
                    local_68 = 0;
                }

                if (uVar9 == 0)
                {
                    local_48 = 2;
                    local_58 = 0;
                    local_50 = 0;
                }

                uVar11 = uVar27 & 2;
                if (iVar28 == 0)
                {
                    uVar11 = uVar27 & 1;
                }

                // JUSTIFICATION: C# language bridge only
                // RELATION: Ghidra prints this gate as a chain of comma expressions —
                //     (((bVar5 = 0xc < local_68, local_68 = local_68 + 1, bVar5 &&
                //       (local_70 = local_70 + 1, local_70 == 1)) || ((local_60 != 0 && (uVar8 != 0)))))
                // C# has no comma operator. The temporaries are named and the side effects kept in
                // place: local_68 advances whenever the outer test passed, local_70 advances only
                // once local_68 is past 12, and the released-then-pressed arm is still evaluated
                // only when the first one failed.
                bool bStep;
                if ((uVar11 != 0) || ((uVar8 & 0x40) != 0))
                {
                    bVar5 = 0xc < local_68;
                    local_68 = local_68 + 1;
                    bStep = false;
                    if (bVar5)
                    {
                        local_70 = local_70 + 1;
                        bStep = local_70 == 1;
                    }

                    if (!bStep)
                    {
                        bStep = (local_60 != 0) && (uVar8 != 0);
                    }
                }
                else
                {
                    bStep = false;
                }

                if (bStep)
                {
                    if (local_60 != 0)
                    {
                        local_60 = 0;
                    }

                    iVar22 = iVar24 + 9 + iVar28 * 4;
                    Sprites[iVar22].r = 0x40;
                    Sprites[iVar22].g = 0x40;
                    Sprites[iVar22].b = 0x40;

                    // LEFT (0x8000). Ghidra: `if (((uVar8 & 0x8000) == 0) || (iVar22 = iVar24 + -1,
                    // iVar24 + -1 < 0)) { iVar22 = iVar24; }` — the decrement happens before the
                    // clamp test and is undone by the body when it underflows.
                    bool bClampLeft;
                    if ((uVar8 & 0x8000) == 0)
                    {
                        bClampLeft = true;
                    }
                    else
                    {
                        iVar22 = iVar24 + -1;
                        bClampLeft = iVar24 + -1 < 0;
                    }

                    if (bClampLeft)
                    {
                        iVar22 = iVar24;
                    }

                    iVar24 = iVar22;

                    // RIGHT (0x2000), the same shape the other way.
                    if ((uVar8 & 0x2000) != 0)
                    {
                        iVar24 = iVar22 + 1;
                        if (3 < iVar22 + 1)
                        {
                            iVar24 = iVar22;
                        }
                    }

                    // DOWN (0x4000) — step this slot's value FORWARD, and keep stepping while the
                    // value collides with another slot. The scan counts every one of the six slots
                    // that is not -1 and matches, which includes the slot itself, so "1 < iVar10"
                    // means "somebody else has it too". Past the tier bound the value wraps to -1,
                    // which the scan can never match, so the loop always terminates.
                    if (((uVar8 & 0x4000) != 0) && (iVar24 != 3))
                    {
                        piVar26 = iVar28 * 3 + iVar24;
                        do
                        {
                            iVar22 = g_SelectionIndexSlots6[piVar26];
                            g_SelectionIndexSlots6[piVar26] = iVar22 + 1;
                            iVar10 = 0;
                            if ((int)(uint)auStack_e8[SharedHighRam.DAT_801ff002] < iVar22 + 1)
                            {
                                g_SelectionIndexSlots6[piVar26] = -1;
                            }

                            iVar22 = 0;
                            piVar17 = 0;
                            do
                            {
                                iVar23 = 0;
                                piVar13 = piVar17;
                                do
                                {
                                    iVar12 = g_SelectionIndexSlots6[piVar13];
                                    piVar13 = piVar13 + 1;
                                    if ((iVar12 != -1) && (iVar12 == g_SelectionIndexSlots6[piVar26]))
                                    {
                                        iVar10 = iVar10 + 1;
                                    }

                                    iVar23 = iVar23 + 1;
                                }
                                while (iVar23 < 3);

                                iVar22 = iVar22 + 1;
                                piVar17 = piVar17 + 3;
                            }
                            while (iVar22 < 2);
                        }
                        while (1 < iVar10);
                    }

                    // UP (0x1000). The same walk backwards, and the original really does read the
                    // slots as UNSIGNED here (`uVar11 != 0xffffffff`) where the DOWN arm read them
                    // as signed. Same values, different spelling; both are kept.
                    if (((uVar8 & 0x1000) != 0) && (iVar24 != 3))
                    {
                        puVar19 = iVar28 * 3 + iVar24;
                        do
                        {
                            uVar11 = (uint)g_SelectionIndexSlots6[puVar19];
                            g_SelectionIndexSlots6[puVar19] = (int)(uVar11 - 1);
                            iVar22 = 0;
                            if ((int)(uVar11 - 1) < -1)
                            {
                                g_SelectionIndexSlots6[puVar19] = (int)(uint)auStack_e8[SharedHighRam.DAT_801ff002];
                            }

                            iVar10 = 0;
                            puVar18 = 0;
                            do
                            {
                                iVar23 = 0;
                                puVar14 = puVar18;
                                do
                                {
                                    uVar11 = (uint)g_SelectionIndexSlots6[puVar14];
                                    puVar14 = puVar14 + 1;
                                    if ((uVar11 != 0xffffffff) && (uVar11 == (uint)g_SelectionIndexSlots6[puVar19]))
                                    {
                                        iVar22 = iVar22 + 1;
                                    }

                                    iVar23 = iVar23 + 1;
                                }
                                while (iVar23 < 3);

                                iVar10 = iVar10 + 1;
                                puVar18 = puVar18 + 3;
                            }
                            while (iVar10 < 2);
                        }
                        while (1 < iVar22);
                    }

                    // CIRCLE (0x20) on the OK button — CONFIRM. The guard
                    // `-3 < slot0 + slot1 + slot2` rejects a row whose three slots are all -1;
                    // any one filled slot makes the sum greater than -3.
                    if ((((uVar8 & 0x20) != 0) && (iVar24 == 3)) &&
                        (-3 < g_SelectionIndexSlots6[iVar28 * 3] + g_SelectionIndexSlots6[1 + iVar28 * 3] + g_SelectionIndexSlots6[2 + iVar28 * 3]))
                    {
                        if (iVar28 == 0)
                        {
                            if (local_88 == 0)
                            {
                                local_40 = 1;
                                Sprites[0x11].b = 0x40;
                                Sprites[0x11].g = 0x40;
                                Sprites[0x11].r = 0x40;
                                Sprites[0xc].b = 0x40;
                                Sprites[0xc].g = 0x40;
                                Sprites[0xc].r = 0x40;
                                Sprites[7].b = 0x40;
                                Sprites[7].g = 0x40;
                                Sprites[7].r = 0x40;
                                uVar27 = uVar27 - 1;
                            }
                            else
                            {
                                // One player: row 0 is done, move the same pad down to row 1 and
                                // light row 1's panel.
                                iVar28 = 1;
                                iVar24 = 0;
                                local_40 = 1;
                                Sprites[0x11].b = 0x40;
                                Sprites[0x11].g = 0x40;
                                Sprites[0x11].r = 0x40;
                                Sprites[0xc].b = 0x40;
                                Sprites[0xc].g = 0x40;
                                Sprites[0xc].r = 0x40;
                                Sprites[7].b = 0x40;
                                Sprites[7].g = 0x40;
                                Sprites[7].r = 0x40;
                                Sprites[0x12].b = 0x80;
                                Sprites[0x12].g = 0x80;
                                Sprites[0x12].r = 0x80;
                                Sprites[8].b = 0x80;
                                Sprites[8].g = 0x80;
                                Sprites[8].r = 0x80;
                                uVar27 = uVar27 - 1;
                            }
                        }
                        else
                        {
                            local_40 = 2;
                            Sprites[0x12].b = 0x40;
                            Sprites[0x12].g = 0x40;
                            Sprites[0x12].r = 0x40;
                            Sprites[0x10].b = 0x40;
                            Sprites[0x10].g = 0x40;
                            Sprites[0x10].r = 0x40;
                            Sprites[8].b = 0x40;
                            Sprites[8].g = 0x40;
                            Sprites[8].r = 0x40;
                            uVar27 = uVar27 - 2;
                        }
                    }

                    // CROSS (0x40) — CANCEL. From a fully live screen it leaves the function; from
                    // a partly confirmed one it walks the confirmation back one step.
                    if ((uVar8 & 0x40) != 0)
                    {
                        local_40 = 0;
                        if (local_88 == 0)
                        {
                            if (uVar27 == 3)
                            {
                                return 0xffffffff;
                            }

                            if (uVar27 == 2)
                            {
                                uVar27 = 3;
                                Sprites[0x11].b = 0x80;
                                Sprites[0x11].g = 0x80;
                                Sprites[0x11].r = 0x80;
                                Sprites[7].b = 0x80;
                                Sprites[7].g = 0x80;
                                Sprites[7].r = 0x80;
                            }
                        }
                        else
                        {
                            iVar24 = 3;
                            if (iVar28 == 0)
                            {
                                return 0xffffffff;
                            }

                            iVar28 = 0;
                            uVar27 = uVar27 + 1;
                            Sprites[0x11].b = 0x80;
                            Sprites[0x11].g = 0x80;
                            Sprites[0x11].r = 0x80;
                            Sprites[7].b = 0x80;
                            Sprites[7].g = 0x80;
                            Sprites[7].r = 0x80;
                            Sprites[0x12].b = 0x40;
                            Sprites[0x12].g = 0x40;
                            Sprites[0x12].r = 0x40;
                            Sprites[0x10].b = 0x40;
                            Sprites[0x10].g = 0x40;
                            Sprites[0x10].r = 0x40;
                            Sprites[8].b = 0x40;
                            Sprites[8].g = 0x40;
                            Sprites[8].r = 0x40;
                        }
                    }
                }

                if (0xc < local_68)
                {
                    local_70 = local_70 % 5;
                }

                // PAD 2's HALF, TWO-PLAYER MODE ONLY. Same gate, same three counters, same
                // duplicate-skipping walks — but its slots are [3..5] and its sprites are 13..16,
                // and it has no row to move to.
                bool bStep2;
                if ((local_88 == 0) && (((uVar27 & 2) != 0) || ((uVar9 & 0x40) != 0)))
                {
                    bVar5 = 0xc < local_50;
                    local_50 = local_50 + 1;
                    bStep2 = false;
                    if (bVar5)
                    {
                        local_58 = local_58 + 1;
                        bStep2 = local_58 == 1;
                    }

                    if (!bStep2)
                    {
                        bStep2 = (local_48 != 0) && (uVar9 != 0);
                    }
                }
                else
                {
                    bStep2 = false;
                }

                if (bStep2)
                {
                    if (local_48 != 0)
                    {
                        local_48 = 0;
                    }

                    iVar22 = iVar25 + 0xd;
                    Sprites[iVar22].r = 0x40;
                    Sprites[iVar22].g = 0x40;
                    Sprites[iVar22].b = 0x40;

                    bool bClampLeft2;
                    if ((uVar9 & 0x8000) == 0)
                    {
                        bClampLeft2 = true;
                    }
                    else
                    {
                        iVar22 = iVar25 + -1;
                        bClampLeft2 = iVar25 + -1 < 0;
                    }

                    if (bClampLeft2)
                    {
                        iVar22 = iVar25;
                    }

                    iVar25 = iVar22;

                    if ((uVar9 & 0x2000) != 0)
                    {
                        iVar25 = iVar22 + 1;
                        if (3 < iVar22 + 1)
                        {
                            iVar25 = iVar22;
                        }
                    }

                    if (((uVar9 & 0x4000) != 0) && (iVar25 != 3))
                    {
                        piVar26 = 3 + iVar25;
                        do
                        {
                            iVar22 = g_SelectionIndexSlots6[piVar26];
                            g_SelectionIndexSlots6[piVar26] = iVar22 + 1;
                            iVar10 = 0;
                            if ((int)(uint)auStack_e8[SharedHighRam.DAT_801ff002] < iVar22 + 1)
                            {
                                g_SelectionIndexSlots6[piVar26] = -1;
                            }

                            iVar22 = 0;
                            piVar17 = 0;
                            do
                            {
                                iVar23 = 0;
                                piVar13 = piVar17;
                                do
                                {
                                    iVar12 = g_SelectionIndexSlots6[piVar13];
                                    piVar13 = piVar13 + 1;
                                    if ((iVar12 != -1) && (iVar12 == g_SelectionIndexSlots6[piVar26]))
                                    {
                                        iVar10 = iVar10 + 1;
                                    }

                                    iVar23 = iVar23 + 1;
                                }
                                while (iVar23 < 3);

                                iVar22 = iVar22 + 1;
                                piVar17 = piVar17 + 3;
                            }
                            while (iVar22 < 2);
                        }
                        while (1 < iVar10);
                    }

                    if (((uVar9 & 0x1000) != 0) && (iVar25 != 3))
                    {
                        puVar19 = 3 + iVar25;
                        do
                        {
                            uVar11 = (uint)g_SelectionIndexSlots6[puVar19];
                            g_SelectionIndexSlots6[puVar19] = (int)(uVar11 - 1);
                            iVar22 = 0;
                            if ((int)(uVar11 - 1) < -1)
                            {
                                g_SelectionIndexSlots6[puVar19] = (int)(uint)auStack_e8[SharedHighRam.DAT_801ff002];
                            }

                            iVar10 = 0;
                            puVar18 = 0;
                            do
                            {
                                iVar23 = 0;
                                puVar14 = puVar18;
                                do
                                {
                                    uVar11 = (uint)g_SelectionIndexSlots6[puVar14];
                                    puVar14 = puVar14 + 1;
                                    if ((uVar11 != 0xffffffff) && (uVar11 == (uint)g_SelectionIndexSlots6[puVar19]))
                                    {
                                        iVar22 = iVar22 + 1;
                                    }

                                    iVar23 = iVar23 + 1;
                                }
                                while (iVar23 < 3);

                                iVar10 = iVar10 + 1;
                                puVar18 = puVar18 + 3;
                            }
                            while (iVar10 < 2);
                        }
                        while (1 < iVar22);
                    }

                    if ((((uVar9 & 0x20) != 0) && (iVar25 == 3)) &&
                        (-3 < g_SelectionIndexSlots6[3] + g_SelectionIndexSlots6[4] + g_SelectionIndexSlots6[5]))
                    {
                        uVar27 = uVar27 - 2;
                        local_38 = 1;
                        Sprites[0x12].b = 0x40;
                        Sprites[0x12].g = 0x40;
                        Sprites[0x12].r = 0x40;
                        Sprites[0x10].b = 0x40;
                        Sprites[0x10].g = 0x40;
                        Sprites[0x10].r = 0x40;
                        Sprites[8].b = 0x40;
                        Sprites[8].g = 0x40;
                        Sprites[8].r = 0x40;
                    }

                    if ((uVar9 & 0x40) != 0)
                    {
                        local_38 = 0;
                        Sprites[0x12].b = 0x80;
                        Sprites[0x12].g = 0x80;
                        Sprites[0x12].r = 0x80;
                        Sprites[8].b = 0x80;
                        Sprites[8].g = 0x80;
                        Sprites[8].r = 0x80;
                        if (uVar27 == 3)
                        {
                            // The same six bytes written a second time before returning. Redundant
                            // and reproduced — rule 12.
                            Sprites[8].r = 0x80;
                            Sprites[8].g = 0x80;
                            Sprites[8].b = 0x80;
                            Sprites[0x12].r = 0x80;
                            Sprites[0x12].g = 0x80;
                            Sprites[0x12].b = 0x80;
                            return 0xffffffff;
                        }

                        if (uVar27 == 1)
                        {
                            uVar27 = 3;
                        }
                    }
                }

                if (0xc < local_50)
                {
                    local_58 = local_58 % 5;
                }

                // REPAINT — pad 1's slot. When the slot went to -1 the placeholder is armed
                // (tpage 0xD this time, unlike the pre-loop pass), otherwise the tile is uploaded
                // and the CLUT recomputed.
                if (iVar24 != 3)
                {
                    piVar26 = iVar28 * 3 + iVar24;
                    if (g_SelectionIndexSlots6[piVar26] == -1)
                    {
                        iVar22 = iVar28 * 4 + iVar24 + 9;
                        Sprites[iVar22].tpage = 0xd;
                        Sprites[iVar22].u = 0x38;
                        Sprites[iVar22].v = 0x58;
                        Sprites[iVar22].cx = 0x170;
                        Sprites[iVar22].cy = 0x1f9;
                    }
                    else
                    {
                        iVar22 = iVar28 * 4 + iVar24 + 9;
                        Sprites[iVar22].tpage = 0x1f;
                        Sprites[iVar22].u = (byte)((sbyte)iVar24 * 0x30);
                        Sprites[iVar22].v = (byte)(iVar28 * 0x30);
                        bVar4 = local_e0[g_SelectionIndexSlots6[piVar26]];
                        uVar11 = bVar4 / 7u;
                        Sprites[iVar22].cx = (short)(((((ushort)bVar4) + ((short)((uVar11 + ((bVar4 - uVar11) >> 1)) >> 2) * -7)) & 0xff) * 0x10 + 0x100);
                        uVar11 = local_e0[g_SelectionIndexSlots6[piVar26]] / 7u;
                        Sprites[iVar22].cy = (short)((((ushort)((uVar11 + ((local_e0[g_SelectionIndexSlots6[piVar26]] - uVar11) >> 1)) >> 2)) & 0xff) + 0x1fb);
                        local_90.y = (short)((short)(iVar28 * 0x30) + 0x100);
                        local_90.x = (short)((short)iVar24 * 0xc + 0x3c0);
                        LoadImage(local_90, unchecked((int)((uint)local_e0[g_SelectionIndexSlots6[piVar26]] * 0x480 | 0x80080000u)));
                        DrawSync(0);
                    }
                }

                // JUSTIFICATION: C# language bridge only
                // RELATION: Ghidra emits the tail of the body as two labels sitting in DIFFERENT
                // arms of one if/else — LAB_800332b8 inside the `iVar25 == 3` arm and LAB_8003337c
                // inside the `else` — with a goto crossing between them each way. C# cannot jump
                // into a block, so which label was reached is carried in two locals. The four
                // reachable combinations are exactly the original's:
                //     iVar25 == 3, local_88 == 0   LAB_800332b8, then its local_88 == 0 arm
                //     iVar25 == 3, local_88 != 0   LAB_800332b8 -> LAB_8003337c
                //     iVar25 != 3, local_88 == 0   pad 2's repaint, then LAB_800332b8's arm
                //     iVar25 != 3, local_88 != 0   fall through to LAB_8003337c
                bool bAt800332b8;
                bool bAt8003337c = false;
                if (iVar25 == 3)
                {
                    bAt800332b8 = true;
                }
                else
                {
                    if (local_88 == 0)
                    {
                        // REPAINT — pad 2's slot. Its v is FIXED at 0x30: pad 2 is always row 1.
                        piVar26 = 3 + iVar25;
                        if (g_SelectionIndexSlots6[piVar26] == -1)
                        {
                            iVar22 = iVar25 + 0xd;
                            Sprites[iVar22].tpage = 0xd;
                            Sprites[iVar22].u = 0x38;
                            Sprites[iVar22].v = 0x58;
                            Sprites[iVar22].cx = 0x170;
                            Sprites[iVar22].cy = 0x1f9;
                        }
                        else
                        {
                            iVar22 = iVar25 + 0xd;
                            Sprites[iVar22].tpage = 0x1f;
                            Sprites[iVar22].u = (byte)((sbyte)iVar25 * 0x30);
                            Sprites[iVar22].v = 0x30;
                            bVar4 = local_e0[g_SelectionIndexSlots6[piVar26]];
                            uVar11 = bVar4 / 7u;
                            Sprites[iVar22].cx = (short)(((((ushort)bVar4) + ((short)((uVar11 + ((bVar4 - uVar11) >> 1)) >> 2) * -7)) & 0xff) * 0x10 + 0x100);
                            uVar11 = local_e0[g_SelectionIndexSlots6[piVar26]] / 7u;
                            Sprites[iVar22].cy = (short)((((ushort)((uVar11 + ((local_e0[g_SelectionIndexSlots6[piVar26]] - uVar11) >> 1)) >> 2)) & 0xff) + 0x1fb);
                            local_90.y = 0x130;
                            local_90.x = (short)((short)iVar25 * 0xc + 0x3c0);
                            LoadImage(local_90, unchecked((int)((uint)local_e0[g_SelectionIndexSlots6[piVar26]] * 0x480 | 0x80080000u)));
                            DrawSync(0);
                        }

                        bAt800332b8 = true;
                    }
                    else
                    {
                        bAt800332b8 = false;
                        bAt8003337c = true;
                    }
                }

                if (bAt800332b8)
                {
                    // LAB_800332b8
                    if (local_88 != 0)
                    {
                        bAt8003337c = true;
                    }
                    else
                    {
                        if (local_40 == 0)
                        {
                            iVar22 = iVar24 + 9 + iVar28 * 4;
                            Sprites[iVar22].r = 0x80;
                            Sprites[iVar22].g = 0x80;
                            Sprites[iVar22].b = 0x80;
                        }

                        if (local_38 == 0)
                        {
                            iVar22 = iVar25 + 0xd;
                            Sprites[iVar22].r = 0x80;
                            Sprites[iVar22].g = 0x80;
                            Sprites[iVar22].b = 0x80;
                        }
                    }
                }

                if (bAt8003337c)
                {
                    // LAB_8003337c
                    if (local_40 != 2)
                    {
                        iVar22 = iVar24 + 9 + iVar28 * 4;
                        Sprites[iVar22].r = 0x80;
                        Sprites[iVar22].g = 0x80;
                        Sprites[iVar22].b = 0x80;
                    }
                }

                FrameStep.DrawFrame();
            }

            if (uVar27 == 0)
            {
                // THE EXIT. Sprite 5 — the plate that flies in over the finished roster — is armed
                // from scratch and its scalex ramped over twenty-one frames, then one more frame at
                // full size, and only then is the roster published.
                iVar24 = 0;
                Sprites[5].y = -0x18;
                Sprites[5].v = 0x28;
                Sprites[5].w = 0x100;
                Sprites[5].h = 0x30;
                Sprites[5].mx = 0x80;
                Sprites[5].x = 0;
                Sprites[5].cy = 0x1ff;
                Sprites[5].attribute = 0;
                Sprites[5].scalex = 0;
                do
                {
                    sVar21 = (short)(Sprites[5].scalex + 0xcc);
                    FrameStep.DrawFrame();
                    iVar24 = iVar24 + 1;
                    Sprites[5].scalex = sVar21;
                }
                while (iVar24 < 0x15);

                Sprites[5].scalex = 0x1000;
                FrameStep.DrawFrame();

                // THE VS ROSTER BLOCK, 0x801FF100..0x801FF10D, fourteen bytes inside the shared
                // high-RAM span SharedHighRam models (base 0x801FF000). Short index 0x80 is
                // 0x801FF100, so the six ids are indices 0x81..0x86 — the same spelling
                // TITLE_EXE/SecondScreenSetup.cs already uses for 0x801FF100.
                //
                // The exported value is the SELECTION map (local_b8), not the tile map, and an
                // empty slot exports 0 rather than -1.
                if (g_SelectionIndexSlots6[0] == -1)
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x81] = 0;
                }
                else
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x81] = (short)(ushort)local_b8[g_SelectionIndexSlots6[0]];
                }

                if (g_SelectionIndexSlots6[1] == -1)
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x82] = 0;
                }
                else
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x82] = (short)(ushort)local_b8[g_SelectionIndexSlots6[1]];
                }

                if (g_SelectionIndexSlots6[2] == -1)
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x83] = 0;
                }
                else
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x83] = (short)(ushort)local_b8[g_SelectionIndexSlots6[2]];
                }

                if (g_SelectionIndexSlots6[3] == -1)
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x84] = 0;
                }
                else
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x84] = (short)(ushort)local_b8[g_SelectionIndexSlots6[3]];
                }

                if (g_SelectionIndexSlots6[4] == -1)
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x85] = 0;
                }
                else
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x85] = (short)(ushort)local_b8[g_SelectionIndexSlots6[4]];
                }

                if (g_SelectionIndexSlots6[5] == -1)
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x86] = 0;
                }
                else
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[0x86] = (short)(ushort)local_b8[g_SelectionIndexSlots6[5]];
                }

                // DAT_801ff100 — the VS sub-mode, straight from param_1.
                SharedHighRam.SHORT_ARRAY_801ff000[0x80] = (short)local_88;

                // AND THE BIT. 0x80033488, the only `ori` of 4 into DAT_80055B80 in the image, and
                // the one thing RunVsModeScreen @ 0x80030EF8 breaks its sub-menu loop on.
                SELECT_EXE_exe.DAT_80055b80 = SELECT_EXE_exe.DAT_80055b80 | 4;
                return 0;
            }
        }
        while (true);
    }
}
