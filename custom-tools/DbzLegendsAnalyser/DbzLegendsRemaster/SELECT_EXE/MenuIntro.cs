using PsxSdkMonogame;
using static PsxSdkMonogame.LibGcc;
using static PsxSdkMonogame.MipsMemory;

namespace DbzLegendsRemaster.SELECT_EXE;

// FUN_8002ea8c @ 0x8002EA8C — 6608 bytes, 916 lines, the FIRST function of SELECT.EXE's "select.c"
// emission block and the largest one in it. It ends at 0x8003045B, immediately before main.
//
// WHAT IT IS: the mode menu's BUILD plus its entry animation. main calls it once per rebuild, in
// the same block that resets the hundred sprites and reloads USAGI.B, so every sprite the menu
// driver ModeMenu.FUN_800283a0 later moves is armed here. It owns no loop of its own beyond the
// animation; it calls the frame step FrameStep.FUN_800344a4 inline (twenty call sites, four of them
// inside the outer animation loop) and returns.
//
// THE THREE PIECES, in order:
//   1. the vertical band sweep — sprites 60..79 on tpage 0x0E, alternating in from y = -360 and
//      y = +120 until they meet at y = -120 (lines 86..134);
//   2. the static build — the five option plates (20..24), the fifteen label tiles (40..54), the
//      four menu rows (0x15..0x18) with their three drop shadows (0x28..0x2A), the four selector
//      tiles (0x19..0x1C), the banner (0x1D) with its three shadows (0x37..0x39) and the zooming
//      logo (10) (lines 135..408);
//   3. the animation — four frame steps per pass, the bands rotating leftwards while the logo
//      shrinks from scale 0x4000 towards 0x1000 and the rows slide in, until the logo has finished
//      shrinking (lines 409..777); then the fade-in on sprite 0 and the final arming of the seven
//      orbit chains (lines 779..913).
//
// THE 0x80020648 BLOCK IS NOW CLOSED. The data pass listed the 28 bytes at 0x80020648..0x80020663
// as "CONSUMER NOT IDENTIFIED". They are this function's own: the compiler materialised them as
// seven word immediates into a stack array (Ghidra's local_a8), and the last loop hands them out
// one per orbit satellite as the sprite's v texture row. Byte for byte, the immediates
// 0x9b8b8b8b / 0x7b7b9bab / 0x8b7b9b9b / 0x7bab9b9b / 0x8b9b9b8b / 0x9b8b7bab / 0x9bab8b9b are
// little-endian 8B 8B 8B 9B AB 9B 7B 7B 9B 9B 7B 8B 9B 9B AB 7B 8B 9B 9B 8B AB 7B 8B 9B 9B 8B AB 9B,
// which is exactly what read-memory returns for that .rdata block. Twenty-eight bytes, and the
// triangular table holds exactly twenty-eight satellites.
internal static class MenuIntro
{
    // GHIDRA: DAT_80020648 @ 0x80020648
    // .rdata, 28 bytes, extent closed at both ends: 0x80020638 is the eight-halfword angle block
    // ModeMenu.DAT_800205e4 duplicates and 0x80020664 is the "\\SUB\\USAGI.B;1" string.
    // Named here rather than left as immediates because the identification above is the point.
    private static readonly byte[] DAT_80020648 =
    {
        0x8b, 0x8b, 0x8b, 0x9b, 0xab, 0x9b, 0x7b, 0x7b, 0x9b, 0x9b, 0x7b, 0x8b, 0x9b, 0x9b,
        0xab, 0x7b, 0x8b, 0x9b, 0x9b, 0x8b, 0xab, 0x7b, 0x8b, 0x9b, 0x9b, 0x8b, 0xab, 0x9b,
    };

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original's `*dst = *src` on a GsSPRITE. GCC emitted the 36-byte struct
    // assignment as a two-pass unrolled 16-byte copy plus a four-byte tail, which is why Ghidra
    // prints it as a do/while over `&p->cx` with a cx/cy epilogue. LibGs.GsSPRITE is a CLASS in this
    // port, so `dst = src` would alias one object instead of copying it — every field has to move.
    // The one byte the original copies that has no field here is the +0x17 pad, which nothing reads.
    private static void CopySprite(LibGs.GsSPRITE dst, LibGs.GsSPRITE src)
    {
        dst.attribute = src.attribute;
        dst.x = src.x;
        dst.y = src.y;
        dst.w = src.w;
        dst.h = src.h;
        dst.tpage = src.tpage;
        dst.u = src.u;
        dst.v = src.v;
        dst.cx = src.cx;
        dst.cy = src.cy;
        dst.r = src.r;
        dst.g = src.g;
        dst.b = src.b;
        dst.mx = src.mx;
        dst.my = src.my;
        dst.scalex = src.scalex;
        dst.scaley = src.scaley;
        dst.rotate = src.rotate;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: shorthand for GsSPRITE_ARRAY_800654ec @ 0x800654EC, which around two hundred and
    // fifty lines below touch by name. It returns the same array object, never a copy, so every
    // write through it is a write to that global. Nothing else in this file is renamed.
    private static LibGs.GsSPRITE[] Sprites => SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec;

    // GHIDRA: FUN_8002ea8c @ 0x8002EA8C
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: three shapes could not be spelled literally.
    //   (1) Ghidra addresses most of the sprite writes as byte offsets from GsSPRITE_ARRAY_800654ec
    //       (`*(undefined2 *)((int)&GsSPRITE_ARRAY_800654ec[0].tpage + iVar11)`). The offset cursors
    //       (iVar11, iVar23) are kept and divided by the array's own stride, 0x24, to name the
    //       element — the same treatment ModeBranches.cs uses.
    //   (2) The struct assignments become CopySprite above.
    //   (3) The libgcc soft-float calls are PsxSdkMonogame/LibGcc.cs's; the (lo, hi) register pairs Ghidra
    //       prints become one double.
    internal static void FUN_8002ea8c()
    {
        short sVar2;
        bool bVar3;
        sbyte cVar4;
        short sVar12;
        int iVar11;
        int iVar14;
        int iVar21;
        int iVar23;
        int iVar24;
        uint uVar22;
        double uVar26;

        // The 28-byte stack block. The original stores seven word immediates into it; the bytes are
        // the .rdata block above, and the copy is spelled out as the seven words the compiler
        // emitted (each rendered by Ghidra as an unaligned SWL/SWR pair plus an aligned store of the
        // same word).
        byte[] local_a8 = new byte[28];
        for (int i = 0; i < 28; i++)
        {
            local_a8[i] = DAT_80020648[i];
        }

        // ---------------------------------------------------------------------------------------
        // 1. THE BAND SWEEP. 0x870 / 0x24 = element 60, and the loop runs to element 79.
        // ---------------------------------------------------------------------------------------
        uVar22 = 0x3c;
        iVar11 = 0x870;
        sVar12 = -0xb3;
        do
        {
            Sprites[iVar11 / 0x24].tpage = 0xe;
            Sprites[iVar11 / 0x24].x = sVar12;
            if ((uVar22 & 1) == 0)
            {
                Sprites[iVar11 / 0x24].y = unchecked((short)0xfe98);
            }
            else
            {
                Sprites[iVar11 / 0x24].y = 0x78;
            }

            Sprites[iVar11 / 0x24].h = 0xf0;
            Sprites[iVar11 / 0x24].cx = 0x100;
            Sprites[iVar11 / 0x24].cy = 0x1f0;
            Sprites[iVar11 / 0x24].u = 0x09;
            Sprites[iVar11 / 0x24].v = 0x00;
            Sprites[iVar11 / 0x24].w = 0x3b;
            Sprites[iVar11 / 0x24].attribute = 0x1000000;
            iVar11 = iVar11 + 0x24;
            uVar22 = uVar22 + 1;
            sVar12 = (short)(sVar12 + 0x3b);
        }
        while ((int)uVar22 < 0x50);

        while (true)
        {
            FrameStep.FUN_800344a4();
            uVar22 = 0x3c;
            if (Sprites[0x3c].y == -0x78)
            {
                break;
            }

            // `psVar13 = &GsSPRITE_ARRAY_800654ec[0x3c].y` stepped by 0x12 shorts, i.e. 36 bytes —
            // one element per pass. It covers 60..65 only, not the whole run armed above.
            int psVar13 = 0x3c;
            do
            {
                sVar12 = -8;
                if ((uVar22 & 1) == 0)
                {
                    sVar12 = 8;
                }

                Sprites[psVar13].y = (short)(Sprites[psVar13].y + sVar12);
                uVar22 = uVar22 + 1;
                psVar13 = psVar13 + 1;
            }
            while ((int)uVar22 < 0x42);
        }

        Sprites[0x3c].x = -0x78;
        Sprites[0x3d].x = -0x3d;
        Sprites[0x3e].x = -2;
        Sprites[0x3f].x = 0x39;
        Sprites[0x40].x = 0x74;
        Sprites[0x41].x = 0xaf;
        Sprites[0x47].x = -0xb3;
        Sprites[0x48].x = -0xee;
        Sprites[0x47].y = -0x78;
        Sprites[0x48].y = -0x78;
        FrameStep.FUN_800344a4();

        // ---------------------------------------------------------------------------------------
        // 2. THE STATIC BUILD. 0x2D0 / 0x24 = element 20; five plates, 20..24.
        // ---------------------------------------------------------------------------------------
        iVar23 = 0;
        iVar11 = 0x2d0;
        do
        {
            Sprites[iVar11 / 0x24].tpage = 10;
            Sprites[iVar11 / 0x24].x = 0xa0;
            Sprites[iVar11 / 0x24].w = 0x90;
            Sprites[iVar11 / 0x24].h = 0x18;
            Sprites[iVar11 / 0x24].b = 0x40;
            Sprites[iVar11 / 0x24].g = 0x40;
            Sprites[iVar11 / 0x24].r = 0x40;
            Sprites[iVar11 / 0x24].cx = 0x170;
            Sprites[iVar11 / 0x24].cy = 0x1fa;
            Sprites[iVar11 / 0x24].attribute = 0;
            iVar23 = iVar23 + 1;
            iVar11 = iVar11 + 0x24;
        }
        while (iVar23 < 5);

        // 0x5A0 / 0x24 = element 40; fifteen label tiles, 40..54, laid out three across and five
        // down: x = (2 - (n % 3)) * -60 + 16, y = (n / 3) * 32 - 80.
        iVar23 = 0;
        iVar11 = 0x5a0;
        do
        {
            Sprites[iVar11 / 0x24].v = 0xd0;
            Sprites[iVar11 / 0x24].w = 0x90;
            Sprites[iVar11 / 0x24].h = 0x18;
            Sprites[iVar11 / 0x24].tpage = 10;
            Sprites[iVar11 / 0x24].u = 0x00;

            // Ghidra recovered the divisor from the compiler's magic multiply. iVar23 is 0..14, so
            // the truncating C# division agrees on every value it can take.
            sVar12 = (short)(iVar23 / 3);
            Sprites[iVar11 / 0x24].x = (short)(((2 - ((short)iVar23 + (sVar12 * -3))) * -0x3c) + 0x10);
            Sprites[iVar11 / 0x24].y = (short)((sVar12 * 0x20) + -0x50);
            Sprites[iVar11 / 0x24].b = 0x40;
            Sprites[iVar11 / 0x24].g = 0x40;
            Sprites[iVar11 / 0x24].cx = 0x170;
            Sprites[iVar11 / 0x24].cy = 0x1fa;
            Sprites[iVar11 / 0x24].r = 0x40;
            Sprites[iVar11 / 0x24].attribute = 0x80000000;
            iVar23 = iVar23 + 1;
            iVar11 = iVar11 + 0x24;
        }
        while (iVar23 < 0xf);

        // Sprite 0x14 is the row template; 0x2A, 0x29 and 0x28 are its three trailing shadows, each
        // copied from the one before it and then offset.
        Sprites[0x14].x = -0x150;
        Sprites[0x14].y = -0x6c;
        Sprites[0x14].w = 0xb0;
        Sprites[0x14].h = 0x28;
        Sprites[0x14].r = 0x80;
        Sprites[0x14].g = 0x80;
        Sprites[0x14].b = 0x80;
        CopySprite(Sprites[0x2a], Sprites[0x14]);
        CopySprite(Sprites[0x29], Sprites[0x2a]);
        CopySprite(Sprites[0x28], Sprites[0x29]);
        Sprites[0x28].x = -0x6e;
        Sprites[0x29].x = -0x87;
        Sprites[0x2a].x = -0xa0;
        Sprites[0x15].y = -0x30;
        Sprites[0x15].v = 0x28;
        Sprites[0x16].y = -0x10;
        Sprites[0x16].v = 0x40;
        Sprites[0x17].y = 0x10;
        Sprites[0x17].v = 0x58;
        Sprites[0x18].y = 0x30;
        Sprites[0x2a].attribute = 0x80000000;
        Sprites[0x29].attribute = 0x80000000;
        Sprites[0x28].attribute = 0x80000000;
        Sprites[0x18].v = 0x70;
        iVar11 = 0;

        // The same options-word gate FUN_80030a6c and ModeMenu.FUN_800283a0 apply: with bit 1 clear
        // the fourth row is hidden and the third takes its artwork.
        if ((PsxRam.ReadI32(unchecked((int)0x801FF018)) & 2) == 0)
        {
            Sprites[0x17].v = 0x70;
            Sprites[0x18].attribute = 0x80000000;
        }

        // 900 / 0x24 = element 25; the four selector tiles, 0x19..0x1C.
        iVar23 = 900;
        do
        {
            Sprites[iVar23 / 0x24].tpage = 0x1a;
            Sprites[iVar23 / 0x24].x = 0x40;
            Sprites[iVar23 / 0x24].y = unchecked((short)0xff40);
            Sprites[iVar23 / 0x24].w = 0x50;
            Sprites[iVar23 / 0x24].h = 0x30;
            Sprites[iVar23 / 0x24].cx = 0;
            Sprites[iVar23 / 0x24].cy = 0x1f5;
            iVar11 = iVar11 + 1;
            iVar23 = iVar23 + 0x24;
        }
        while (iVar11 < 4);

        Sprites[0x1a].cy = 0x1fd;
        Sprites[0x1b].u = 0xa0;
        Sprites[0x1a].v = 0x90;
        Sprites[0x1b].v = 0x90;
        Sprites[0x1b].cy = 0x1ff;
        Sprites[0x1c].u = 0x50;
        Sprites[0x1c].v = 0x90;
        Sprites[0x1c].cy = 0x1fe;

        // 0x801FF002 is the DEMO launch block's second halfword, which FUN_80030af8 wrote before the
        // previous overlay hand-off. Sprite 0x19 picks its tile out of a 3-wide sheet with it.
        Sprites[0x19].cy = (short)(SharedHighRam.DAT_801ff002 + 0x1f5);
        cVar4 = (sbyte)(SharedHighRam.DAT_801ff002 / 3);
        Sprites[0x19].u = (byte)(((sbyte)SharedHighRam.DAT_801ff002 + (cVar4 * -3)) * 0x50);
        Sprites[0x19].v = (byte)(cVar4 * 0x30);
        if ((PsxRam.ReadI32(unchecked((int)0x801FF018)) & 2) == 0)
        {
            Sprites[0x1b].u = 0x50;
            Sprites[0x1b].cy = 0x1fe;
        }

        // Sprite 0x1D is the banner; 0x39, 0x38 and 0x37 are its three trailing shadows.
        Sprites[0x1d].tpage = 0xe;
        Sprites[0x1d].x = 0x28;
        Sprites[0x1d].y = -0xd8;
        Sprites[0x1d].u = 0x48;
        Sprites[0x1d].v = 0x30;
        Sprites[0x1d].w = 0x7d;
        Sprites[0x1d].h = 0x60;
        Sprites[0x1d].cx = 0x100;
        Sprites[0x19].attribute = 0x1000000;
        Sprites[0x1d].cy = 0x1f0;
        Sprites[0x1d].attribute = 0x1000000;
        CopySprite(Sprites[0x39], Sprites[0x1d]);
        CopySprite(Sprites[0x38], Sprites[0x39]);
        CopySprite(Sprites[0x37], Sprites[0x38]);
        iVar11 = 0x144;
        Sprites[0x39].attribute = 0x80000000;
        Sprites[0x38].attribute = 0x80000000;
        Sprites[0x37].attribute = 0x80000000;
        Sprites[0x37].y = -0x48;
        Sprites[0x38].y = -0x60;
        Sprites[0x39].y = -0x78;

        // Sprite 10 is the zooming logo. It starts at scale 0x4000 and the animation below shrinks
        // it towards 0x1000.
        Sprites[10].tpage = 0xe;
        Sprites[10].x = -0x29;
        Sprites[10].y = -0x59;
        Sprites[10].u = 0x48;
        Sprites[10].v = 0x07;
        Sprites[10].w = 0xa0;
        Sprites[10].h = 0x28;
        Sprites[10].cx = 0x100;
        Sprites[10].cy = 0x1f0;
        Sprites[10].mx = 0x9f;
        Sprites[10].my = 0x27;
        Sprites[10].scalex = 0x4000;
        Sprites[10].scaley = 0x4000;

        // Bit 0 of the flag word suppresses GsSortClear for the whole animation — the bands are
        // meant to smear. It is cleared again at the end (line 780).
        SELECT_EXE_exe.DAT_80055b80 = SELECT_EXE_exe.DAT_80055b80 | 1;

        // 0x144 / 0x24 = element 9, counting DOWN to element 0.
        do
        {
            Sprites[iVar11 / 0x24].attribute = 0x80000000;
            iVar11 = iVar11 + -0x24;
        }
        while (-1 < iVar11);

        // ---------------------------------------------------------------------------------------
        // 3. THE ANIMATION. Four frame steps per outer pass; the guard and the loop condition are
        // both on the logo's scale.
        // ---------------------------------------------------------------------------------------
        if (0x1000 < Sprites[10].scalex)
        {
            // `pGVar25 = GsSPRITE_ARRAY_800654ec + 1` — the source base of the ten-element shift
            // below, set once and reused by all four sub-blocks.
            int pGVar25 = 1;
            do
            {
                // -- sub-block 1 --------------------------------------------------------------
                Sprites[0x3d].x = (short)(Sprites[0x3d].x + -0xf);
                Sprites[0x3e].x = (short)(Sprites[0x3e].x + -0xf);
                Sprites[0x3f].x = (short)(Sprites[0x3f].x + -0xf);
                Sprites[0x40].x = (short)(Sprites[0x40].x + -0xf);
                Sprites[0x41].x = (short)(Sprites[0x41].x + -0xf);
                Sprites[0x47].x = (short)(Sprites[0x47].x + 0xf);
                Sprites[0x48].x = (short)(Sprites[0x48].x + 0xf);
                if (Sprites[0x18].x < -0x87)
                {
                    iVar11 = 0;
                    if ((Sprites[0].x < 0x9f) || (Sprites[10].x < 0x9f))
                    {
                        Sprites[10].attribute = 0x1000000;

                        // sprite[k] = sprite[k + 1] for k = 0..9 — the logo trail shifts down one
                        // slot per frame. Copying forward is safe: each read is of a slot the loop
                        // has not written yet.
                        int pGVar18 = pGVar25;
                        int pGVar15 = 0;
                        do
                        {
                            CopySprite(Sprites[pGVar15], Sprites[pGVar18]);
                            pGVar18 = pGVar18 + 1;
                            iVar11 = iVar11 + 1;
                            pGVar15 = pGVar15 + 1;
                        }
                        while (iVar11 < 10);

                        if (Sprites[10].x < 0x9f)
                        {
                            Sprites[10].y = (short)(Sprites[10].y + 0x10);
                            Sprites[10].x = (short)(Sprites[10].x + 0x10);
                        }

                        if (0x1000 < Sprites[10].scalex)
                        {
                            // `__floatsidf()` prints with no argument, but the register is set:
                            // `lh a0,0x18(s3)` at 0x8002F7E4 with s3 = &GsSPRITE_ARRAY_800654ec[10].x
                            // reads +0x1C, which is scalex, and 0x8002F7EC's `slti v0,a0,0x1001` is
                            // the very guard above reusing the same register.
                            // 0x407482E1_47AE147B = 328.18
                            uVar26 = __floatsidf(Sprites[10].scalex);
                            uVar26 = __subdf3(uVar26, 328.18);
                            Sprites[10].scalex = (short)__fixdfsi(uVar26);
                            uVar26 = __floatsidf(Sprites[10].scaley);
                            uVar26 = __subdf3(uVar26, 328.18);
                            Sprites[10].scaley = (short)__fixdfsi(uVar26);
                        }
                    }
                }
                else
                {
                    iVar11 = Sprites[0x14].x + 0xc;
                    if (Sprites[0x14].x < -0x50)
                    {
                        Sprites[0x14].x = (short)iVar11;

                        // `iVar11 * 0x10000 >> 0x10` is the low halfword sign-extended, i.e. the
                        // value just stored into sprite[0x14].x.
                        if (Sprites[0x28].x < ((iVar11 * 0x10000) >> 0x10))
                        {
                            Sprites[0x28].attribute = 0;
                        }

                        if (Sprites[0x29].x < Sprites[0x14].x)
                        {
                            Sprites[0x29].attribute = 0;
                        }

                        if (Sprites[0x2a].x < Sprites[0x14].x)
                        {
                            Sprites[0x2a].attribute = 0;
                        }
                    }

                    Sprites[0x15].x = (short)(Sprites[0x15].x + -0x10);
                    Sprites[0x16].x = (short)(Sprites[0x16].x + -0x10);
                    Sprites[0x18].x = (short)(Sprites[0x18].x + -0x10);
                    Sprites[0x17].x = (short)(Sprites[0x17].x + -0x10);
                    if (Sprites[0x15].x < Sprites[0x2b].x)
                    {
                        Sprites[0x31].attribute = 0;
                        Sprites[0x2e].attribute = 0;
                        Sprites[0x2b].attribute = 0;
                        if ((PsxRam.ReadI32(unchecked((int)0x801FF018)) & 2) != 0)
                        {
                            Sprites[0x34].attribute = 0;
                        }
                    }

                    if (Sprites[0x15].x < Sprites[0x2c].x)
                    {
                        Sprites[0x32].attribute = 0;
                        Sprites[0x2f].attribute = 0;
                        Sprites[0x2c].attribute = 0;
                        if ((PsxRam.ReadI32(unchecked((int)0x801FF018)) & 2) != 0)
                        {
                            Sprites[0x35].attribute = 0;
                        }
                    }

                    if (Sprites[0x15].x < Sprites[0x2d].x)
                    {
                        Sprites[0x33].attribute = 0;
                        Sprites[0x30].attribute = 0;
                        Sprites[0x2d].attribute = 0;
                        if ((PsxRam.ReadI32(unchecked((int)0x801FF018)) & 2) != 0)
                        {
                            Sprites[0x36].attribute = 0;
                        }
                    }

                    iVar11 = Sprites[0x1d].y + 8;
                    if (Sprites[0x1d].y < -0x30)
                    {
                        Sprites[0x1d].y = (short)iVar11;
                        Sprites[0x19].y = (short)(Sprites[0x19].y + 8);
                        Sprites[0x1a].y = (short)(Sprites[0x1a].y + 8);
                        Sprites[0x1b].y = (short)(Sprites[0x1b].y + 8);
                        Sprites[0x1c].y = (short)(Sprites[0x1c].y + 8);
                        if (Sprites[0x37].y < ((iVar11 * 0x10000) >> 0x10))
                        {
                            Sprites[0x37].attribute = 0x1000000;
                        }

                        if (Sprites[0x38].y < Sprites[0x1d].y)
                        {
                            Sprites[0x38].attribute = 0x1000000;
                        }

                        if (Sprites[0x39].y < Sprites[0x1d].y)
                        {
                            Sprites[0x39].attribute = 0x1000000;
                        }
                    }
                }

                FrameStep.FUN_800344a4();

                // -- sub-block 2 --------------------------------------------------------------
                Sprites[0x3d].x = (short)(Sprites[0x3d].x + -0xf);
                Sprites[0x3e].x = (short)(Sprites[0x3e].x + -0xf);
                Sprites[0x3f].x = (short)(Sprites[0x3f].x + -0xf);
                Sprites[0x40].x = (short)(Sprites[0x40].x + -0xf);
                Sprites[0x41].x = (short)(Sprites[0x41].x + -0xf);
                Sprites[0x47].x = (short)(Sprites[0x47].x + 0xf);
                Sprites[0x48].x = (short)(Sprites[0x48].x + 0xf);
                if (Sprites[0x18].x < -0x87)
                {
                    iVar11 = 0;
                    if ((Sprites[0].x < 0x9f) || (Sprites[10].x < 0x9f))
                    {
                        Sprites[10].attribute = 0x1000000;
                        int pGVar18 = pGVar25;
                        int pGVar15 = 0;
                        do
                        {
                            CopySprite(Sprites[pGVar15], Sprites[pGVar18]);
                            pGVar18 = pGVar18 + 1;
                            iVar11 = iVar11 + 1;
                            pGVar15 = pGVar15 + 1;
                        }
                        while (iVar11 < 10);

                        if (Sprites[10].x < 0x9f)
                        {
                            Sprites[10].y = (short)(Sprites[10].y + 8);
                            Sprites[10].x = (short)(Sprites[10].x + 8);
                        }

                        if (0x1000 < Sprites[10].scalex)
                        {
                            // 0x407EB800_00000000 = 491.5
                            uVar26 = __floatsidf(Sprites[10].scalex);
                            uVar26 = __subdf3(uVar26, 491.5);
                            Sprites[10].scalex = (short)__fixdfsi(uVar26);
                            uVar26 = __floatsidf(Sprites[10].scaley);
                            uVar26 = __subdf3(uVar26, 491.5);
                            Sprites[10].scaley = (short)__fixdfsi(uVar26);
                        }
                    }
                }
                else
                {
                    if (Sprites[0x14].x < -0x50)
                    {
                        Sprites[0x14].x = (short)(Sprites[0x14].x + 4);
                    }

                    Sprites[0x15].x = (short)(Sprites[0x15].x + -4);
                    Sprites[0x17].x = (short)(Sprites[0x17].x + -4);
                    Sprites[0x16].x = (short)(Sprites[0x16].x + -4);
                    Sprites[0x18].x = (short)(Sprites[0x18].x + -4);
                    if (Sprites[0x1d].y < -0x30)
                    {
                        Sprites[0x19].y = (short)(Sprites[0x19].y + 4);
                        Sprites[0x1a].y = (short)(Sprites[0x1a].y + 4);
                        Sprites[0x1b].y = (short)(Sprites[0x1b].y + 4);
                        Sprites[0x1c].y = (short)(Sprites[0x1c].y + 4);
                        Sprites[0x1d].y = (short)(Sprites[0x1d].y + 4);
                    }
                }

                FrameStep.FUN_800344a4();

                // -- sub-block 3, byte for byte the same as sub-block 2 -----------------------
                Sprites[0x3d].x = (short)(Sprites[0x3d].x + -0xf);
                Sprites[0x3e].x = (short)(Sprites[0x3e].x + -0xf);
                Sprites[0x3f].x = (short)(Sprites[0x3f].x + -0xf);
                Sprites[0x40].x = (short)(Sprites[0x40].x + -0xf);
                Sprites[0x41].x = (short)(Sprites[0x41].x + -0xf);
                Sprites[0x47].x = (short)(Sprites[0x47].x + 0xf);
                Sprites[0x48].x = (short)(Sprites[0x48].x + 0xf);
                if (Sprites[0x18].x < -0x87)
                {
                    iVar11 = 0;
                    if ((Sprites[0].x < 0x9f) || (Sprites[10].x < 0x9f))
                    {
                        Sprites[10].attribute = 0x1000000;
                        int pGVar18 = pGVar25;
                        int pGVar15 = 0;
                        do
                        {
                            CopySprite(Sprites[pGVar15], Sprites[pGVar18]);
                            pGVar18 = pGVar18 + 1;
                            iVar11 = iVar11 + 1;
                            pGVar15 = pGVar15 + 1;
                        }
                        while (iVar11 < 10);

                        if (Sprites[10].x < 0x9f)
                        {
                            Sprites[10].y = (short)(Sprites[10].y + 8);
                            Sprites[10].x = (short)(Sprites[10].x + 8);
                        }

                        if (0x1000 < Sprites[10].scalex)
                        {
                            uVar26 = __floatsidf(Sprites[10].scalex);
                            uVar26 = __subdf3(uVar26, 491.5);
                            Sprites[10].scalex = (short)__fixdfsi(uVar26);
                            uVar26 = __floatsidf(Sprites[10].scaley);
                            uVar26 = __subdf3(uVar26, 491.5);
                            Sprites[10].scaley = (short)__fixdfsi(uVar26);
                        }
                    }
                }
                else
                {
                    if (Sprites[0x14].x < -0x50)
                    {
                        Sprites[0x14].x = (short)(Sprites[0x14].x + 4);
                    }

                    Sprites[0x15].x = (short)(Sprites[0x15].x + -4);
                    Sprites[0x17].x = (short)(Sprites[0x17].x + -4);
                    Sprites[0x16].x = (short)(Sprites[0x16].x + -4);
                    Sprites[0x18].x = (short)(Sprites[0x18].x + -4);
                    if (Sprites[0x1d].y < -0x30)
                    {
                        Sprites[0x19].y = (short)(Sprites[0x19].y + 4);
                        Sprites[0x1a].y = (short)(Sprites[0x1a].y + 4);
                        Sprites[0x1b].y = (short)(Sprites[0x1b].y + 4);
                        Sprites[0x1c].y = (short)(Sprites[0x1c].y + 4);
                        Sprites[0x1d].y = (short)(Sprites[0x1d].y + 4);
                    }
                }

                FrameStep.FUN_800344a4();

                // -- sub-block 4, the band step is -0xE / +0xE here, not -0xF / +0xF ----------
                Sprites[0x3d].x = (short)(Sprites[0x3d].x + -0xe);
                Sprites[0x3e].x = (short)(Sprites[0x3e].x + -0xe);
                Sprites[0x3f].x = (short)(Sprites[0x3f].x + -0xe);
                Sprites[0x40].x = (short)(Sprites[0x40].x + -0xe);
                Sprites[0x41].x = (short)(Sprites[0x41].x + -0xe);
                Sprites[0x47].x = (short)(Sprites[0x47].x + 0xe);
                Sprites[0x48].x = (short)(Sprites[0x48].x + 0xe);
                if (Sprites[0x18].x < -0x87)
                {
                    iVar11 = 0;
                    if ((Sprites[0].x < 0x9f) || (Sprites[10].x < 0x9f))
                    {
                        Sprites[10].attribute = 0x1000000;
                        int pGVar18 = pGVar25;
                        int pGVar15 = 0;
                        do
                        {
                            CopySprite(Sprites[pGVar15], Sprites[pGVar18]);
                            pGVar18 = pGVar18 + 1;
                            iVar11 = iVar11 + 1;
                            pGVar15 = pGVar15 + 1;
                        }
                        while (iVar11 < 10);

                        if (Sprites[10].x < 0x9f)
                        {
                            Sprites[10].y = (short)(Sprites[10].y + 8);
                            Sprites[10].x = (short)(Sprites[10].x + 8);
                        }

                        if (0x1000 < Sprites[10].scalex)
                        {
                            uVar26 = __floatsidf(Sprites[10].scalex);
                            uVar26 = __subdf3(uVar26, 491.5);
                            Sprites[10].scalex = (short)__fixdfsi(uVar26);
                            uVar26 = __floatsidf(Sprites[10].scaley);
                            uVar26 = __subdf3(uVar26, 491.5);
                            Sprites[10].scaley = (short)__fixdfsi(uVar26);
                        }
                    }
                }
                else
                {
                    if (Sprites[0x14].x < -0x50)
                    {
                        Sprites[0x14].x = (short)(Sprites[0x14].x + 4);
                    }

                    Sprites[0x15].x = (short)(Sprites[0x15].x + -4);
                    Sprites[0x17].x = (short)(Sprites[0x17].x + -4);
                    Sprites[0x16].x = (short)(Sprites[0x16].x + -4);
                    Sprites[0x18].x = (short)(Sprites[0x18].x + -4);
                    if (Sprites[0x1d].y < -0x30)
                    {
                        Sprites[0x19].y = (short)(Sprites[0x19].y + 4);
                        Sprites[0x1a].y = (short)(Sprites[0x1a].y + 4);
                        Sprites[0x1b].y = (short)(Sprites[0x1b].y + 4);
                        Sprites[0x1c].y = (short)(Sprites[0x1c].y + 4);
                        Sprites[0x1d].y = (short)(Sprites[0x1d].y + 4);
                    }
                }

                FrameStep.FUN_800344a4();

                // The band recycle: the six-slot ring 0x3D..0x41 rotates one place left and the
                // pair 0x47/0x48 wraps, so the sweep never runs out of bands.
                sVar2 = Sprites[0x40].x;
                Sprites[0x3d].x = Sprites[0x3e].x;
                sVar12 = Sprites[0x3f].x;
                Sprites[0x40].x = Sprites[0x41].x;
                Sprites[0x47].x = Sprites[0x48].x;
                Sprites[0x41].x = (short)(Sprites[0x41].x + 0x3b);
                Sprites[0x3e].x = Sprites[0x3f].x;
                Sprites[0x3f].x = sVar2;
                Sprites[0x48].x = (short)(Sprites[0x48].x + -0x3b);
            }
            while (0x1000 < Sprites[10].scalex);
        }

        // ---------------------------------------------------------------------------------------
        // The settle. Bit 0 goes back off, sprites 9..0 come back on, and sprite 0 fades in over
        // four pairs of frames.
        // ---------------------------------------------------------------------------------------
        iVar11 = 0x144;
        SELECT_EXE_exe.DAT_80055b80 = SELECT_EXE_exe.DAT_80055b80 & unchecked((int)0xfffffffe);
        do
        {
            Sprites[iVar11 / 0x24].attribute = 0x1000000;
            iVar11 = iVar11 + -0x24;
        }
        while (-1 < iVar11);

        Sprites[0].tpage = 0x17;
        Sprites[0].x = -0xa0;
        Sprites[0].y = -0x78;
        Sprites[0].u = 0x40;
        Sprites[0].v = 0xbd;
        Sprites[0].w = 0x50;
        Sprites[0].h = 0x32;
        Sprites[0].cx = 0x100;
        Sprites[0].cy = 0x1f2;
        Sprites[0].scalex = 0x4000;
        Sprites[0].scaley = 0x6000;
        Sprites[0].attribute = 0x1000000;
        Sprites[0].mx = 0;
        Sprites[0].my = 0;
        Sprites[0].b = 0x80;
        Sprites[0].g = 0x80;
        Sprites[0].r = 0x80;
        FrameStep.FUN_800344a4();
        FrameStep.FUN_800344a4();
        iVar11 = 1;
        do
        {
            Sprites[iVar11].attribute = 0x80000000;
            bVar3 = iVar11 < 9;
            iVar11 = iVar11 + 1;
        }
        while (bVar3);

        Sprites[0].b = 0x60;
        Sprites[0].g = 0x60;
        Sprites[0].r = 0x60;
        FrameStep.FUN_800344a4();
        FrameStep.FUN_800344a4();
        Sprites[0].b = 0x40;
        Sprites[0].g = 0x40;
        Sprites[0].r = 0x40;
        FrameStep.FUN_800344a4();
        FrameStep.FUN_800344a4();
        Sprites[0].b = 0x20;
        Sprites[0].g = 0x20;
        Sprites[0].r = 0x20;
        FrameStep.FUN_800344a4();
        FrameStep.FUN_800344a4();
        CopySprite(Sprites[0x28], Sprites[0x3c]);

        // 0x80065AB0 = GsSPRITE_ARRAY_800654ec + 36 * 41, i.e. element 41; seventeen entries.
        SelectScreen.FUN_80030848(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 41, 0x11);
        CopySprite(Sprites[0], Sprites[10]);
        Sprites[10].attribute = 0x80000000;

        // ---------------------------------------------------------------------------------------
        // The seven orbit chains, armed through the triangular table FUN_80030698 @ 0x80030698
        // built at 0x800593B8: field +0x00 of each record is the LEADER's sprite address and +0x04
        // points at that row's satellite addresses. Row r carries r + 1 satellites, so the twenty-
        // eight v bytes of DAT_80020648 are consumed exactly.
        // ---------------------------------------------------------------------------------------
        iVar23 = 0;
        iVar24 = 0;
        iVar11 = 0;
        do
        {
            int leader = ReadI32(SelectScreen.DAT_800593b8, iVar11);
            ModeMenu.SpriteAtAddress(leader).tpage = 0x1d;
            ModeMenu.SpriteAtAddress(leader).u = 0;
            ModeMenu.SpriteAtAddress(leader).v = 0x78;
            ModeMenu.SpriteAtAddress(leader).w = 0x47;
            ModeMenu.SpriteAtAddress(leader).h = 0x46;
            ModeMenu.SpriteAtAddress(leader).mx = 0x23;
            ModeMenu.SpriteAtAddress(leader).my = 0x23;
            ModeMenu.SpriteAtAddress(leader).cx = 0;
            ModeMenu.SpriteAtAddress(leader).cy = 500;
            iVar21 = 0;
            ModeMenu.SpriteAtAddress(leader).attribute = 0x1000000;
            if (-1 < iVar24)
            {
                do
                {
                    iVar14 = iVar21 * 4;
                    int satellite = PsxRam.ReadI32(
                        iVar14 + ReadI32(SelectScreen.DAT_800593b8, iVar11 + 4));
                    ModeMenu.SpriteAtAddress(satellite).tpage = 0x1d;
                    ModeMenu.SpriteAtAddress(satellite).u = 0x4b;
                    ModeMenu.SpriteAtAddress(satellite).v = local_a8[iVar23];
                    ModeMenu.SpriteAtAddress(satellite).w = 9;
                    ModeMenu.SpriteAtAddress(satellite).h = 8;
                    ModeMenu.SpriteAtAddress(satellite).mx = 4;
                    ModeMenu.SpriteAtAddress(satellite).my = 4;
                    ModeMenu.SpriteAtAddress(satellite).cx = 0;
                    ModeMenu.SpriteAtAddress(satellite).cy = 500;
                    iVar21 = iVar21 + 1;
                    ModeMenu.SpriteAtAddress(satellite).attribute = 0x1000000;
                    iVar23 = iVar23 + 1;
                }
                while (iVar21 <= iVar24);
            }

            iVar24 = iVar24 + 1;
            iVar11 = iVar11 + 0xc;
        }
        while (iVar24 < 7);
    }
}
