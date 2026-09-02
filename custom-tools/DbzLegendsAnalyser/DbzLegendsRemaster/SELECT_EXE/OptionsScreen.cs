using PsxSdkMonogame;
using static PsxSdkMonogame.LibGcc;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE OPTIONS SCREEN'S BUILD, UNWIND AND SAVE/LOAD SHIM — the three functions
// SELECT_EXE_exe.RunOptionsScreen @ 0x800315C0 (main's case 3) calls and nothing else does.
// find-cross-references gives each of them exactly ONE incoming call, all from RunOptionsScreen:
//     0x8002B2DC  BuildOptionsScreen   2528 bytes, called at 0x8003167C before the input loop
//     0x8002BCBC  UnwindOptionsScreen   908 bytes, called at 0x80031BA4 after it
//     0x80031C8C  FUN_80031c8c    236 bytes, called at 0x80031A34 from row 3's confirm
//
// THE SCREEN, verified on the console and already written down in SELECT_EXE_exe.cs:
//     row 0  音楽      ステレオ / モノラル   _DAT_801ff01e, 0 = stereo
//     row 1  難易度    three difficulties    DAT_801ff01c over 0..2
//     row 2  操作設定  1P / 2P               DAT_80055b10  (BuildOptionsScreen's param_2)
//     row 3  設定      セーブ / ロード       DAT_80055b14  (BuildOptionsScreen's param_3)
// The row LABELS are sprites 2..5 and the highlight is `g_OptionsCursor + 2`, which is why
// BuildOptionsScreen's first act on param_1 is `param_1 = param_1 + 2`. The row VALUES are sprites
// 6 (music), 9 (difficulty), 0x0D (pad) and 0x10 (save/load), and the build arms each of them from
// the same globals the input loop re-arms every frame.
//
// THE PARAMETER TYPES ARE THE CALLER'S, NOT A GUESS. Ghidra recovers
// `void BuildOptionsScreen(int,int,int)` and `void FUN_80031c8c(int)` from register use alone; the
// call site passes g_OptionsCursor (int) and DAT_80055b10 / DAT_80055b14, which SELECT_EXE_exe.cs
// already carries as `uint` because RunOptionsScreen writes them as `(uint)(x == 0)`. The C#
// signatures keep uint for those two so the existing caller compiles unchanged.
//
// THE SPRITE INDICES ARE ARITHMETIC, NOT GUESSES — the same table ModeBranches.cs and
// ScreenDecoration.cs state, stride 36 (0x24) from 0x800654EC:
//     `(int)&GsSPRITE_ARRAY_800654ec[0].x + n`         -> element n / 0x24, field +0x04 (x)
//     `(int)&GsSPRITE_ARRAY_800654ec[0].attribute + n` -> element n / 0x24, field +0x00
//     `(&GsSPRITE_ARRAY_800654ec[0].u)[n]`             -> element n / 0x24, field +0x0E
// Both cursors this file uses divide exactly: n = 0x48 is element 2 (the four row labels, 2..5) and
// n = 0xD8 is element 6 (the thirteen value/panel sprites, 6..18).
//
// SPRITES 0x62 AND 99 ARE THE BACKGROUND, stitched out of two 4bpp bands of the USAGI.B sheet with
// palette (0, 0x1F0) — tpage 0x10 at x = -0xA0 and tpage 0x12 at x = 0x60, both 0x100 by 0xF0,
// attribute 0x1000000. Their `r` is ramped 0 -> 0x7C across the build's 32 frames and 0x80 -> 4
// across the unwind's, with `g` and `b` copied from it. That is the same construction the three
// pickers in ScreenDecoration.cs use, and USAGI_B_View.cs already cites this function for it.
//
// WHAT UNWINDOPTIONSSCREEN DOES *NOT* DO — it does NOT end with InitializeSpriteArray, unlike
// ScreenDecoration.UnwindSpSaveSlotScreen @ 0x8002B174 and UnwindDemoSaveSlotScreen @ 0x80029F9C,
// which both re-initialise elements 1..19 themselves. Here the CALLER does it: RunOptionsScreen
// runs `UnwindOptionsScreen(); InitializeSpriteArray(&GsSPRITE_ARRAY_800654ec[1].attribute, 0x13);`
// at 0x80031BA4/0x80031BAC. The port keeps that split — the call already stands in
// SELECT_EXE_exe.cs and is not repeated here.
//
// __adddf3 @ 0x8004DAEC HAS NO LibGcc.cs WRAPPER YET, so the fourteen additions below are written
// with C#'s own `+`. That is not a shortcut: every LibGcc entry point in PsxSdkMonogame is one line
// of arithmetic (`__subdf3` returns `param_1 - param_2`), so `+` is the identical bridge and only
// the name is missing. The Ghidra call is named in a comment at each site.
internal static class OptionsScreen
{
    // GHIDRA: DAT_80055a18 @ 0x80055A18, DAT_80055a1c @ 0x80055A1C
    // GHIDRA: DAT_80055a20 @ 0x80055A20, DAT_80055a24 @ 0x80055A24
    // GHIDRA: DAT_80055a28 @ 0x80055A28, DAT_80055a2c @ 0x80055A2C
    // .sdata. THREE PARALLEL TABLES OF THREE SHORTS, one entry per difficulty, that place row 1's
    // value box: x, then u, then w. Ghidra splits each six-byte table as undefined4 + undefined2,
    // which is why two symbol names cover one table; the bytes read with get-data are
    //     0x80055A18: D8 FF 1C 00 5D 00   ->  -40, 28, 93
    //     0x80055A20: B0 00 A8 00 A8 00   ->  176, 168, 168
    //     0x80055A28: 40 00 38 00 38 00   ->   64,  56,  56
    // THE SHORT SHAPE IS CLOSED BY A SECOND COPY OF THE SAME IMAGE. RunOptionsScreen uses its own
    // three tables at 0x80055A50 / 0x80055A58 / 0x80055A60, which Ghidra poses as short[4] and
    // SELECT_EXE_exe.cs already carries; their bytes are byte-for-byte these, plus a zero pad. So
    // the .sdata holds the table twice, once for the build and once for the loop, and the build's
    // indexing (`*(short *)(auStack_40 + level * 2)`, `auStack_38[level * 2]`,
    // `*(ushort *)(auStack_30 + level * 2)`) agrees with the loop's `(&RStack_40.x)[level]`.
    // THE ONE DIFFERENCE BETWEEN THE TWO USES IS THE -0xA8 the build subtracts from x: it places the
    // box a full 168 pixels left of where the loop settles it, which is the slide-in offset.
    private static readonly short[] DAT_80055a18 = { -40, 28, 93 };

    private static readonly short[] DAT_80055a20 = { 176, 168, 168 };

    private static readonly short[] DAT_80055a28 = { 64, 56, 56 };

    // GHIDRA: g_OptionsRecord64 @ 0x801FF018
    // The 64-byte options record — block 0 of the save file, and the destination FUN_80031c8c's
    // save arm copies its stack buffer into. Modelled by SharedHighRam, which PsxRam.ResolveAddress
    // chains, so it is addressed here as the raw PSX address the original stores through. The same
    // private const stands in CardRecords.cs, MemoryCard.cs and SELECT_EXE_exe.cs; it is an address
    // literal, not a second copy of the record.
    private const int g_OptionsRecord64_Address = unchecked((int)0x801FF018);

    // GHIDRA: BuildOptionsScreen @ 0x8002B2DC
    // 2528 bytes. THE OPTIONS SCREEN'S BUILD, called once at 0x8003167C with
    // (g_OptionsCursor, DAT_80055b10, DAT_80055b14).
    //
    // Shape, in order: copy the three difficulty tables to the stack, arm the two background
    // sprites, copy sprite 0x28 onto 0x3B, re-initialise elements 1..19 and give them the row
    // tilesheet (tpage 0x0B, h 0x10, clut 0x170/0x1FB), give elements 2..5 the shared row-label
    // geometry, highlight the selected row, arm the panel and every value sprite from the three
    // option globals and the two parameters, then play the whole thing in over 32 frames.
    //
    // THE 32-FRAME ANIMATION IS TWO DIFFERENT PASSES, split at iVar13 < 0x29. iVar13 counts
    // 0, 4, ... 0x7C, so the first ELEVEN frames (iVar13 = 0..0x28, n = iVar13 >> 2 = 0..10) run the
    // soft-float pass that drops elements 1..5 down from y = 136 and scales them 0xE66 -> 0x1000,
    // and the remaining twenty-one frames run the integer pass that slides elements 6..18 in from
    // the left by 8 a frame and un-hides each one the moment its x clears -0x80. Both passes run the
    // same five decoration sprites (0x14 up, 0x15..0x18 left, 0x19..0x1D right) and the same
    // background ramp every frame.
    internal static void BuildOptionsScreen(int param_1, uint param_2, uint param_3)
    {
        bool bVar2;
        short sVar12;
        int iVar13;
        int iVar16;
        int iVar19;
        double uVar14;
        double uVar20;

        // The original copies each six-byte table into its own stack slot through the unaligned
        // store duplex the compiler emits for a struct copy (the `swl`/`swr` pair Ghidra renders as
        // a mask-and-or followed by the plain store). The copy is what matters.
        short[] auStack_40 = { DAT_80055a18[0], DAT_80055a18[1], DAT_80055a18[2] };
        short[] auStack_38 = { DAT_80055a20[0], DAT_80055a20[1], DAT_80055a20[2] };
        short[] auStack_30 = { DAT_80055a28[0], DAT_80055a28[1], DAT_80055a28[2] };

        // CERTAIN: USAGI.B uses in this function are 4bpp. The observed tpage constants here are
        // 0x10, 0x12 and 0x0B; all are < 0x20, so tpage bits 7-8 stay 0 (PSX 4bpp mode). CERTAIN:
        // the large background is not a standalone chunk image. It is assembled from two 4bpp
        // sprites with palette 0x000/0x1F0: sprite 0x62 uses tpage 0x10, u/v = 0/0, w/h = 0x100/0xF0;
        // sprite 99 uses tpage 0x12, u/v = 0/0, w/h = 0x100/0xF0. This means USAGI.B record 0 must be
        // interpreted in contiguous VRAM space and record 0 alone is not a final screen image.
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].tpage = 0x10;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].x = -0xa0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].tpage = 0x12;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].u = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].v = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].cx = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].cy = 0x1f0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].attribute = 0x1000000;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].x = 0x60;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].y = -0x78;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].u = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].v = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].h = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].cx = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].cy = 0x1f0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[99].attribute = 0x1000000;

        // 0x8002B3B0-0x8002B424: sprite 0x28 is copied wholesale onto sprite 0x3B. The compiler
        // emitted it as a pointer walk in sixteen-byte steps plus a four-byte tail — two `lw`/`sw`
        // iterations covering bytes 0x00..0x1F, then `cx`/`cy` taking the source's `rotate` at
        // 0x20..0x23 — which is why Ghidra shows the same field names twice and terminates the loop
        // on `&GsSPRITE_ARRAY_800654ec[0x28].rotate`. The effect is one thirty-six-byte copy.
        // RunOptionsScreen @ 0x80031B80 runs the SAME copy in the OTHER direction on the way out,
        // and SELECT_EXE_exe.cs already carries that half.
        // GsSPRITE is a class here, so the copy has to be written field by field: assigning the
        // element would alias the source, not copy it.
        LibGs.GsSPRITE pGVar17 = SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x28];
        LibGs.GsSPRITE pGVar18 = SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3b];
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

        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 1, 0x13);

        // `do { body(iVar13); bVar2 = iVar13 < 0x13; iVar13++ } while (bVar2)` runs the body for
        // iVar13 = 1 through 0x13 INCLUSIVE — nineteen elements, the same nineteen the call above
        // re-initialised. tpage 0x0B and clut (0x170, 0x1FB) are the row tilesheet; USAGI_B_View.cs
        // cites this function for that palette.
        iVar13 = 1;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13].tpage = 0xb;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13].h = 0x10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13].cy = 0x1fb;
            bVar2 = iVar13 < 0x13;
            iVar13 = iVar13 + 1;
        }
        while (bVar2);

        // THE FOUR ROW LABELS. iVar13 walks 0x48, 0x6C, 0x90, 0xB4 as a byte offset, and
        // 0x48 / 0x24 = 2 exactly, so these are elements 2, 3, 4 and 5. They share every field but
        // `v`, which is set per row just below (0x60, 0x70, 0x80, 0x90 — four rows of the sheet).
        // r = g = b = 0x40 is the dim colour; RunOptionsScreen writes 0x80 into the selected row
        // every frame and 0x40 back into it on every accepted input.
        iVar19 = 0;
        iVar13 = 0x48;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13 / 0x24].x = -0x58;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13 / 0x24].u = 0x70;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13 / 0x24].w = 0x50;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13 / 0x24].mx = 0x28;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13 / 0x24].my = 8;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13 / 0x24].r = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13 / 0x24].g = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13 / 0x24].b = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar13 / 0x24].attribute = 0;
            iVar19 = iVar19 + 1;
            iVar13 = iVar13 + 0x24;
        }
        while (iVar19 < 4);

        // The preselected row, lit before the first frame is presented.
        param_1 = param_1 + 2;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1].r = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1].g = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1].b = 0x80;

        // Element 1 is the panel; 2..5 take their row of the sheet.
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].x = -0x3c;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].w = 0x88;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].h = 0x20;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].mx = 0x44;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].my = 0x10;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].v = 0x60;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].attribute = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].v = 0x70;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].v = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].v = 0x90;

        // ROW 0's VALUE, sprite 6: ステレオ at x = -0xC8 / v = 0xD0, モノラル at x = -0x6C / v = 0xE0.
        // _DAT_801ff01e is 0 for stereo — closed by SelectScreen.InitializeCdAudio @ 0x80025658,
        // which calls SsSetStereo() on zero and SsSetMono() otherwise. RunOptionsScreen re-arms the
        // same two fields every frame, but with x = -0x20 / 0x3C instead: these build values are the
        // slide-in position, 0xA8 further left, exactly like row 1's -0xA8 below.
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].x = -200;
        if (SharedHighRam._DAT_801ff01e != 0)
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].x = -0x6c;
        }

        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].y = -0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].u = 0x58;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].v = 0xd0;
        if (SharedHighRam._DAT_801ff01e != 0)
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].v = 0xe0;
        }

        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].w = 0x50;

        // Sprites 7 and 8 are row 0's two fixed captions.
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].v = 0xd0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].x = -0x74;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].v = 0xe0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].y = -0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].y = -0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].x = -0xd0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[7].w = 0x58;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[8].w = 0x58;

        // ROW 1's VALUE, sprite 9, out of the three tables. iVar13 is a BYTE cursor
        // (DAT_801ff01c * 2), which is why the x and w reads index shorts and the u read indexes a
        // byte at the same even offset; the C# tables are already short[3], so the index is halved
        // back. `v` is 0x30 + level * 0x10, three rows of the sheet.
        iVar13 = SharedHighRam.DAT_801ff01c * 2;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].y = -0x18;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].x = (short)(auStack_40[iVar13 / 2] + -0xa8);
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].u = (byte)auStack_38[iVar13 / 2];
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].v =
            (byte)((sbyte)SharedHighRam.DAT_801ff01c * 0x10 + 0x30);
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].w = (ushort)auStack_30[iVar13 / 2];

        // Sprites 10, 0x0B and 0x0C are row 1's three fixed captions.
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].v = 0x30;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].x = -0xd0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].w = 0x40;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].v = 0x40;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].x = -0x50;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].y = -0x18;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[10].u = 0x70;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].x = -0x8c;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].y = -0x18;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].u = 0x70;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xb].w = 0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].y = -0x18;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].u = 0x70;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].v = 0x50;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].w = 0x38;

        // ROW 2's VALUE, sprite 0x0D: 1P at x = -0xC8 / u = 0, 2P at x = -0x8C / u = 0x20. The `u`
        // is `param_2 << 5`, so the two pad choices are two 0x20-wide cells side by side on the same
        // sheet row (v = 0xF0). RunOptionsScreen re-arms x as -0x20 / 0x1C.
        if (param_2 == 0)
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xd].x = -200;
        }
        else
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xd].x = -0x8c;
        }

        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xd].u = (byte)(param_2 << 5);
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xd].v = 0xf0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xd].w = 0x20;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].v = 0x60;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xd].y = 8;

        // Sprites 0x0E and 0x0F are row 2's two fixed captions.
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].x = -0xd0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].y = 8;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].u = 0xc0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xe].w = 0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].x = -0x94;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].y = 8;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].u = 0xc0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].v = 0x70;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xf].w = 0x38;

        // ROW 3's VALUE, sprite 0x10: セーブ at x = -0xD0 / v = 0, ロード at x = -0x94 / v = 0x10.
        // `v` is `param_3 << 4`, two rows of the sheet. RunOptionsScreen re-arms x as -0x28 / 0x14.
        // `iVar13 = 0` here is the animation counter, initialised inside this arm by the scheduler.
        iVar13 = 0;
        if (param_3 == 0)
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].x = -0xd0;
        }
        else
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].x = -0x94;
        }

        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].y = 0x28;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].u = 0xc0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].v = (byte)(param_3 << 4);

        // Sprites 0x11 and 0x12 are row 3's two fixed captions.
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].x = -0xd0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].x = -0x94;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x10].w = 0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].y = 0x28;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].u = 0x88;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].v = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].w = 0x38;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].y = 0x28;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].u = 0x88;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].v = 0x10;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].w = 0x38;

        // THE PLAY-IN, 32 frames. iVar13 counts 0, 4, ... 0x7C and the loop test reads the value
        // AFTER the increment, so the background ramp `r` takes 0 .. 0x7C and never reaches 0x80.
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
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x19].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1a].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1c].x + 4);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x =
                (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1d].x + 4);

            if (iVar13 < 0x29)
            {
                // `iVar19 = iVar13 < 0 ? iVar13 + 3 : iVar13` then `>> 2` is the compiler's signed
                // divide by four. iVar13 is never negative here; the bias is kept because the
                // original computes it.
                iVar19 = iVar13;
                if (iVar13 < 0)
                {
                    iVar19 = iVar13 + 3;
                }

                // n = 0 .. 10 over the first eleven frames. Each of elements 1..5 falls from
                // y = 120 - n * k + c toward the panel, with its own k. The constants are the
                // .rdata doubles the call sites load as (lo, hi) register pairs:
                //     0x40366666_66666666 = 22.4    0x405E0000_00000000 = 120.0
                //     0x40300000_00000000 = 16.0    0x40319999_9999999A = 17.6
                //     0x40200000_00000000 = 8.0     0x402CCCCC_CCCCCCCD = 14.4
                //     0x40266666_66666666 = 11.2
                uVar20 = __floatsidf(iVar19 >> 2);
                uVar14 = uVar20;
                uVar20 = __muldf3(uVar14, 22.4);
                uVar20 = __subdf3(120.0, uVar20);
                uVar20 = uVar20 + 16.0;   // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].y = (short)__fixdfsi(uVar20);
                uVar20 = __muldf3(uVar14, 17.6);
                uVar20 = __subdf3(120.0, uVar20);
                uVar20 = uVar20 + 8.0;    // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].y = (short)__fixdfsi(uVar20);
                uVar20 = __muldf3(uVar14, 14.4);
                uVar20 = __subdf3(120.0, uVar20);
                uVar20 = uVar20 + 8.0;    // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].y = (short)__fixdfsi(uVar20);
                uVar20 = __muldf3(uVar14, 11.2);
                uVar20 = __subdf3(120.0, uVar20);
                uVar20 = uVar20 + 8.0;    // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].y = (short)__fixdfsi(uVar20);
                uVar20 = __muldf3(uVar14, 8.0);
                uVar20 = __subdf3(120.0, uVar20);
                uVar20 = uVar20 + 8.0;    // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].y = (short)__fixdfsi(uVar20);

                // 0xE66 at n = 0 up to 0x1000 at n = 10 — the panel and its four labels grow from
                // 0.9 to exactly 1.0. Only scalex is written; scaley is left where
                // InitializeSpriteArray put it.
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex =
                    (short)((short)(iVar19 >> 2) * 0x29 + 0xe66);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].scalex =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].scalex =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].scalex =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            }
            else
            {
                // THE VALUE SPRITES SLIDE IN. iVar19 is a byte cursor from 0xD8 to 0xD8 + 12 * 0x24,
                // and 0xD8 / 0x24 = 6 exactly, so this is elements 6..18 — every caption and every
                // value armed above. Each moves right by 8 a frame, and the FIRST frame on which its
                // x clears -0x80 also clears its attribute, which is what un-hides it.
                iVar16 = 0;
                iVar19 = 0xd8;
                do
                {
                    sVar12 = (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar19 / 0x24].x + 8);
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar19 / 0x24].x = sVar12;
                    iVar16 = iVar16 + 1;
                    if (-0x81 < sVar12)
                    {
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar19 / 0x24].attribute = 0;
                    }

                    iVar19 = iVar19 + 0x24;
                }
                while (iVar16 < 0xd);
            }

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r = (byte)iVar13;
            iVar13 = iVar13 + 4;
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
        while (iVar13 < 0x80);
    }

    // GHIDRA: UnwindOptionsScreen @ 0x8002BCBC
    // 908 bytes. THE OPTIONS SCREEN'S CANCEL UNWIND, called once at 0x80031BA4 when the input loop
    // sees the cancel bit (0x40). Same shape as ScreenDecoration.UnwindDemoSaveSlotScreen and
    // UnwindSpSaveSlotScreen: 32 frames counting 0x80 down to 4, the build's five decoration offsets
    // negated, and the background dimmed back down. It does NOT re-initialise the sprite array —
    // its caller does, on the next line.
    //
    // IT IS NOT THE BUILD PLAYED BACKWARDS, and the difference is not reconciled here, it is
    // reproduced. The build takes the soft-float arm for the FIRST eleven frames (iVar13 < 0x29,
    // n = 0..10) and the unwind takes it for the LAST NINE (iVar6 < 0x28, n = 9..1). Their per-row
    // formulas differ too:
    //     element 1   build: 120 - n * 22.4 + 16     unwind: 120 - n * 22.4 + 16    (the same)
    //     element 2   build: 120 - n * 17.6 + 8      unwind: n * 8.0    - 136 + 8
    //     element 3   build: 120 - n * 14.4 + 8      unwind: n * 11.2   - 136 + 8
    //     element 4   build: 120 - n * 11.2 + 8      unwind: n * 1.44   - 136 + 8
    //     element 5   build: 120 - n * 8.0  + 8      unwind: n * 1.76   - 136 + 8
    // Only element 1 is symmetric. Elements 2..5 leave by a different path than they arrived by,
    // and the two smallest multipliers (1.44, 1.76) barely move rows 3 and 4 at all. Reproduced.
    internal static void UnwindOptionsScreen()
    {
        short sVar1;
        double uVar2;
        double uVar7;
        int iVar4;
        int iVar5;
        int iVar6;

        iVar6 = 0x80;
        do
        {
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

            if (iVar6 < 0x28)
            {
                // The three VALUE sprites are hidden outright, every frame of this arm, rather than
                // being walked off the way the captions are. Elements 6, 9 and 0x0C are exactly
                // rows 0, 1 and 2's value/last caption; row 3's 0x10 is NOT in this list, and that
                // asymmetry is the original's.
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0xc].attribute = 0x80000000;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[9].attribute = 0x80000000;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[6].attribute = 0x80000000;

                iVar4 = iVar6;
                if (iVar6 < 0)
                {
                    iVar4 = iVar6 + 3;
                }

                // n = 9 down to 1 over the last nine frames. The constants, decoded from the (lo, hi)
                // pairs the call sites load:
                //     0x40366666_66666666 = 22.4    0x405E0000_00000000 = 120.0
                //     0x40300000_00000000 = 16.0    0x40200000_00000000 = 8.0
                //     0xC0610000_00000000 = -136.0  0x40266666_66666666 = 11.2
                //     0x3FF70A3D_70A3D70A = 1.44    0x3FFC28F5_C28F5C29 = 1.76
                uVar7 = __floatsidf(iVar4 >> 2);
                uVar2 = uVar7;
                uVar7 = __muldf3(uVar2, 22.4);
                uVar7 = __subdf3(120.0, uVar7);
                uVar7 = uVar7 + 16.0;     // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].y = (short)__fixdfsi(uVar7);
                uVar7 = __muldf3(uVar2, 8.0);
                uVar7 = uVar7 + -136.0;   // __adddf3 @ 0x8004DAEC
                uVar7 = uVar7 + 8.0;      // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].y = (short)__fixdfsi(uVar7);
                uVar7 = __muldf3(uVar2, 11.2);
                uVar7 = uVar7 + -136.0;   // __adddf3 @ 0x8004DAEC
                uVar7 = uVar7 + 8.0;      // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].y = (short)__fixdfsi(uVar7);
                uVar7 = __muldf3(uVar2, 1.44);
                uVar7 = uVar7 + -136.0;   // __adddf3 @ 0x8004DAEC
                uVar7 = uVar7 + 8.0;      // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].y = (short)__fixdfsi(uVar7);
                uVar7 = __muldf3(uVar2, 1.76);
                uVar7 = uVar7 + -136.0;   // __adddf3 @ 0x8004DAEC
                uVar7 = uVar7 + 8.0;      // __adddf3 @ 0x8004DAEC
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].y = (short)__fixdfsi(uVar7);

                // The SAME expression the build uses, so the scale runs 0x1000 back down to 0xE8F —
                // it does NOT return to the build's starting 0xE66, because n stops at 1 and never
                // reaches 0.
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex =
                    (short)((short)(iVar4 >> 2) * 0x29 + 0xe66);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].scalex =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].scalex =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[5].scalex =
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
            }
            else
            {
                // Elements 6..18 again (0xD8 / 0x24 = 6), sliding back off to the LEFT by 8 a frame,
                // and each one hidden the first frame its x drops below -0x7F. Note the bound is not
                // the mirror of the build's: the build un-hides on `-0x81 < x` and the unwind hides
                // on `x < -0x7F`, so x = -0x80 satisfies both. Reproduced as measured.
                iVar5 = 0;
                iVar4 = 0xd8;
                do
                {
                    sVar1 = (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar4 / 0x24].x + -8);
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar4 / 0x24].x = sVar1;
                    iVar5 = iVar5 + 1;
                    if (sVar1 < -0x7f)
                    {
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar4 / 0x24].attribute = 0x80000000;
                    }

                    iVar4 = iVar4 + 0x24;
                }
                while (iVar5 < 0xd);
            }

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x62].r = (byte)iVar6;
            iVar6 = iVar6 + -4;
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
        while (0 < iVar6);
    }

    // GHIDRA: FUN_80031c8c @ 0x80031C8C
    // 236 bytes, one caller: RunOptionsScreen's confirm on row 3, at 0x80031A34, which passes
    // DAT_80055b14 — the same save/load toggle BuildOptionsScreen draws as param_3.
    // THE NAME STAYS RAW. What the two arms DO is closed (below); what the function IS as a whole
    // is a two-way shim, and naming it would mean choosing between "save" and "load" for something
    // that is both.
    //
    // GHIDRA'S C OUTPUT FOR THIS FUNCTION IS WRONG AND THE DISASSEMBLY IS WHAT IS PORTED. Because
    // FUN_800276d8's recovered prototype is `void (void)`, the decompiler dropped the argument setup
    // on the taken branch and printed a bare `FUN_800276d8();`. The bytes at 0x80031C8C say
    // otherwise (read with read-memory, 80 bytes):
    //     0x80031C8C  27BDFFA8  addiu sp, sp, -0x58
    //     0x80031C90  10800007  beq   a0, zero, 0x80031CB0     <- param_1 == 0 jumps FORWARD
    //     0x80031C98  34040003  ori   a0, zero, 3              <- the FALL-THROUGH arm is mode 3
    //     0x80031C9C  3C05801F  lui   a1, 0x801F
    //     0x80031CA0  0C009DB6  jal   0x800276D8
    //     0x80031CA4  34A5F018  ori   a1, a1, 0xF018           <- a1 = &g_OptionsRecord64
    //     0x80031CB0  34040002  ori   a0, zero, 2              <- the TAKEN arm is MODE 2
    //     0x80031CB4  0C009DB6  jal   0x800276D8
    //     0x80031CB8  27A50010  addiu a1, sp, 0x10             <- a1 = the 64-byte stack buffer
    //     0x80031CBC  97A20010  lhu   v0, 0x10(sp)
    //     0x80031CC4  30420001  andi  v0, v0, 1
    //     0x80031CC8  10400027  beq   v0, zero, 0x80031D68
    // So param_1 == 0 runs FUN_800276d8 MODE 2 and param_1 != 0 runs MODE 3. Against
    // CardRecords.FUN_800276d8 that reads:
    //     mode 2  ShowCardMessage(5), ShutdownMemoryCard, 30 frames, InitializeMemoryCard,
    //             ProbeMemoryCard(0), RunSaveWriteFlow   ->  SAVE
    //     mode 3  ShowCardMessage(1), 14 frames, RunSaveLoadFlow                ->  LOAD
    // which matches the screen: DAT_80055b14 == 0 draws セーブ (row 3's v = 0) and calls the write
    // side; DAT_80055b14 != 0 draws ロード and calls the read side.
    //
    // THIS CONTRADICTS A CLAIM STANDING IN CardRecords.cs — its header says "MODE 2 HAS NO CALL SITE
    // ANYWHERE IN THE PROGRAM" and marks the mode-2 arm and RunSaveWriteFlow UNREACHABLE. The
    // instruction at 0x80031CB0 is the call site. Left for the main session to disposition; nothing
    // is edited in that file from here.
    //
    // THE POST-CALL COPY READS A BUFFER NOTHING FILLS — reproduced, not repaired (rule 12). After
    // mode 2 returns, the function reads the halfword at sp+0x10, tests bit 0, and on a set bit
    // copies 64 bytes from sp+0x10 over g_OptionsRecord64 at 0x801FF018. But a1 = sp+0x10 goes
    // nowhere: FUN_800276d8's body reads only param_1, and by the time it reaches RunSaveWriteFlow
    // it has run ShowCardMessage, ShutdownMemoryCard, a 30-frame VSync loop, InitializeMemoryCard
    // and ProbeMemoryCard, so a1 cannot still hold the pointer either. The 64 bytes at sp+0x10 are
    // this function's own uninitialised frame, and on the console the gate is whatever the previous
    // call left there.
    // PARTIAL: C# zero-initialises `local_48`, so bit 0 is clear and the copy arm never runs here.
    // The whole arm is transliterated anyway, because the original's is.
    internal static void FUN_80031c8c(uint param_1)
    {
        uint uVar4;
        uint uVar5;
        uint uVar6;
        int puVar7;
        int puVar8;

        // 32 halfwords = the 64 bytes at sp+0x10 .. sp+0x50. The loop bound in Ghidra is
        // `&stack0xfffffff8`, which is sp+0x50 — the assembly's own `addiu t0, sp, 0x50`.
        ushort[] local_48 = new ushort[32];

        if (param_1 == 0)
        {
            // a1 is `addiu a1, sp, 0x10` — the address of local_48. It cannot be handed over: the
            // buffer is a C# local, not PSX RAM, and CardRecords.FUN_800276d8 discards param_2
            // anyway (`_ = param_2;`), which is the whole reason the copy below has no source. 0 is
            // passed so the dead argument stays visibly dead.
            CardRecords.FUN_800276d8(2, 0);
            puVar7 = 0;
            if ((local_48[0] & 1) != 0)
            {
                puVar8 = g_OptionsRecord64_Address;

                // Ghidra folds the source-alignment test (`andi v0, a2, 3`) to `if (true)` because
                // a2 is sp + 0x10, so only this ALIGNED arm can run; the `lwl`/`swl` duplex the
                // compiler emitted for the misaligned case at 0x80031CE4 is dead code and is not
                // transliterated. Sixteen words in four passes of four.
                do
                {
                    uVar4 = ReadStackWord(local_48, puVar7 + 1);
                    uVar5 = ReadStackWord(local_48, puVar7 + 2);
                    uVar6 = ReadStackWord(local_48, puVar7 + 3);
                    PsxRam.WriteI32(puVar8, (int)ReadStackWord(local_48, puVar7));
                    PsxRam.WriteI32(puVar8 + 4, (int)uVar4);
                    PsxRam.WriteI32(puVar8 + 8, (int)uVar5);
                    PsxRam.WriteI32(puVar8 + 0xc, (int)uVar6);
                    puVar7 = puVar7 + 4;
                    puVar8 = puVar8 + 0x10;
                }
                while (puVar7 != 0x10);
            }
        }
        else
        {
            CardRecords.FUN_800276d8(3, g_OptionsRecord64_Address);
        }
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original aliases the same 64 stack bytes two ways — as the `ushort local_48[32]`
    // the flag test indexes, and as the `uint *` the copy loop walks. C# arrays have no such union,
    // so the word view is composed from the halfword pair, little-endian, exactly as the MIPS `lw`
    // at 0x80031D3C reads it.
    private static uint ReadStackWord(ushort[] buffer, int wordIndex)
    {
        return (uint)(buffer[wordIndex * 2] | (buffer[(wordIndex * 2) + 1] << 16));
    }
}
