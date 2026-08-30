using PsxSdkMonogame;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.MipsMemory;

namespace DbzLegendsRemaster.SELECT_EXE;

// The "select.c" module of SELECT.EXE — the functions emitted between 0x8002EA8C and 0x800315C0,
// in that order: FUN_8002ea8c (intro animation), main, FUN_80030698 (graphics + CD bring-up),
// FUN_80030848 (GsSPRITE array initialiser), FUN_800308bc (ClearVram), FUN_80030908 (the USAGI.B
// load), FUN_80030a6c (menu wrapper) and the four state handlers.
//
// This file carries the four of those that belong to the boot chain, plus the .bss/.data they own.
// main lives in SELECT_EXE_exe.cs with start; the animation, the menu wrapper and the four state
// handlers are BLOCKED there.
internal static class SelectScreen
{
    // GHIDRA: DAT_800b0000 @ 0x800B0000
    // THE RAW FILE BUFFER. FUN_80030908 reads the whole of \SUB\USAGI.B;1 here in one CdRead of
    // ceil(size / 2048) sectors. The file is 301056 bytes = 147 sectors exactly, so the live span
    // is 0x800B0000..0x800F97FF.
    // EXTENT: 0x50000. The lower bound is the address CdRead is given; the upper bound is closed by
    // the next address any SELECT.EXE code uses above it, 0x80100000 — the BGM.B VAB body that
    // FUN_80022994 reads in menu state 3. Nothing is modelled between the two.
    internal const int DAT_800b0000_Address = unchecked((int)0x800B0000);

    internal static readonly byte[] DAT_800b0000 = new byte[0x50000];

    // GHIDRA: DAT_80090000 @ 0x80090000
    // THE DECOMPRESSION SCRATCH. Records 0..17 are decoded here and immediately uploaded with
    // LoadImage, so only one record is live at a time.
    // EXTENT: 0x20000. The largest record is 160 x 240 VRAM halfwords = 76800 bytes (records 0..3),
    // and the upper bound is closed by DAT_800b0000 above it.
    internal const int DAT_80090000_Address = unchecked((int)0x80090000);

    internal static readonly byte[] DAT_80090000 = new byte[0x20000];

    // GHIDRA: g_UsagiChunk18DecodedTiles @ 0x80080000
    // Record 18 is decoded here and is NOT uploaded to VRAM. Ghidra types the symbol
    // ushort[20160] = 40320 bytes, which is 35 tiles of 12 x 48 words — 48 x 48 pixels at 4bpp —
    // the form FUN_80031e98 @ 0x80031E98 later consumes as `0x80080000 + tileIndex * 0x480`.
    // That typing is the extent; the upper bound is also closed by DAT_80090000 above it.
    internal const int UsagiChunk18DecodedTilesAddress = unchecked((int)0x80080000);

    internal static readonly byte[] g_UsagiChunk18DecodedTiles = new byte[40320];

    // GHIDRA: DAT_80059744 @ 0x80059744
    // The CdlFILE \SUB\USAGI.B;1 is resolved into. Its size field is what Ghidra names
    // DAT_80059748 — CdlFILE is { CdlLOC pos; u_long size; char name[16]; }, so +4 is the size.
    internal static readonly CdlFILE CdlFILE_80059744 = new CdlFILE();

    // GHIDRA: DAT_80058e08 @ 0x80058E08
    // The flat backing store of the triangular table FUN_80030698 builds: TWENTY-EIGHT words.
    // The count is closed by the loop itself — seven rows, row i holding i + 1 entries, so the row
    // bases are the triangular numbers 0, 1, 3, 6, 10, 15, 21 and the last entry is word 27. The
    // extent 0x70 also lands exactly on 0x80058E78, which is libgs's own DAT_80058e78.
    // A resolvable region because the loop writes into it THROUGH the pointer it just stored, the
    // way the original does.
    internal const int DAT_80058e08_Address = unchecked((int)0x80058E08);

    internal static readonly byte[] DAT_80058e08 = new byte[28 * 4];

    // GHIDRA: DAT_800593b8 @ 0x800593B8
    // Seven records of twelve bytes, 0x800593B8..0x8005940B. FUN_80030698 writes two of the three
    // fields per record: +0x00 (Ghidra's DAT_800593b8) and +0x04 (Ghidra's DAT_800593bc, the row
    // pointer into DAT_80058e08). +0x08 is never written by anything in this slice.
    // The record count is the loop bound (`while (iVar5 < 7)`) and the stride is its own increment
    // (`iVar6 = iVar6 + 0xc`).
    internal const int DAT_800593b8_Address = unchecked((int)0x800593B8);

    internal static readonly byte[] DAT_800593b8 = new byte[7 * 12];

    // GHIDRA: DAT_80065484 @ 0x80065484
    // FOUR GsLINE of sixteen bytes: 0x80065484, 0x80065494, 0x800654A4, 0x800654B4. The count and
    // the stride are the frame step's own — FUN_800344a4 @ 0x800344A4 sorts exactly these four,
    // all at priority 1. The array ends at 0x800654C4, which is GsOT[0]'s handle.
    // FUN_80030698 arms all four with attribute 0x80000000; libgs's GsSortLine gates its whole
    // body on `if (-1 < (int)attribute)`, so as armed here all four are SUPPRESSED. That is the
    // original's own state and is reproduced, not corrected — rule 12.
    internal static readonly LibGs.GsLINE[] GsLINE_ARRAY_80065484 =
    {
        new LibGs.GsLINE(),
        new LibGs.GsLINE(),
        new LibGs.GsLINE(),
        new LibGs.GsLINE(),
    };

    // GHIDRA: g_UsagiBChunkTable @ 0x8004F380
    // NINETEEN records of twelve bytes, 0x8004F380..0x8004F463, laid out
    // { u32 fileOffset; s16 x; s16 y; s16 w; s16 h; }. The bytes below were read out of the image
    // with read-memory and are reproduced verbatim; the decoded values in the comments are what
    // those bytes mean, not a second source.
    // BOTH ENDS ARE CLOSED: 0x8004F380 is where the SE note table at 0x8004F328 ends
    // (22 x 4 bytes), and 0x8004F464 — the byte after record 18 — is the first entry of the
    // 451-entry sine table, which the same read-memory call shows as 0000 0047 008E 00D6 011D
    // 0164, i.e. round(4096 * sin(0..5 degrees)).
    // w and h are VRAM HALFWORD counts, which is what LoadImage takes.
    internal static readonly byte[] g_UsagiBChunkTable =
    {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0xA0, 0x00, 0xF0, 0x00, // 00  0x000000  (  0,256) 160x240
        0x04, 0x17, 0x01, 0x00, 0xA0, 0x00, 0x00, 0x01, 0xA0, 0x00, 0xF0, 0x00, // 01  0x011704  (160,256) 160x240
        0xA8, 0x73, 0x01, 0x00, 0x40, 0x01, 0x00, 0x01, 0xA0, 0x00, 0xF0, 0x00, // 02  0x0173A8  (320,256) 160x240
        0x34, 0x29, 0x02, 0x00, 0xE0, 0x01, 0x00, 0x01, 0xA0, 0x00, 0xF0, 0x00, // 03  0x022934  (480,256) 160x240
        0x04, 0x9C, 0x02, 0x00, 0x40, 0x03, 0x78, 0x01, 0x2C, 0x00, 0x48, 0x00, // 04  0x029C04  (832,376)  44x 72
        0xA8, 0xA7, 0x02, 0x00, 0x80, 0x02, 0x00, 0x00, 0x40, 0x00, 0x00, 0x01, // 05  0x02A7A8  (640,  0)  64x256
        0x94, 0xB5, 0x02, 0x00, 0x80, 0x03, 0x00, 0x00, 0x80, 0x00, 0xF0, 0x00, // 06  0x02B594  (896,  0) 128x240
        0x18, 0x1B, 0x03, 0x00, 0xC0, 0x02, 0x00, 0x00, 0x40, 0x00, 0x00, 0x01, // 07  0x031B18  (704,  0)  64x256
        0xCC, 0x3B, 0x03, 0x00, 0x40, 0x03, 0x28, 0x01, 0x40, 0x00, 0x50, 0x00, // 08  0x033BCC  (832,296)  64x 80
        0x58, 0x45, 0x03, 0x00, 0x40, 0x03, 0x00, 0x01, 0x40, 0x00, 0x28, 0x00, // 09  0x034558  (832,256)  64x 40
        0x7C, 0x4C, 0x03, 0x00, 0x00, 0x03, 0x00, 0x00, 0x40, 0x00, 0x00, 0x01, // 10  0x034C7C  (768,  0)  64x256
        0x84, 0x60, 0x03, 0x00, 0x00, 0x03, 0x00, 0x01, 0x40, 0x00, 0xD0, 0x00, // 11  0x036084  (768,256)  64x208
        0xE0, 0x77, 0x03, 0x00, 0x40, 0x03, 0x00, 0x00, 0x40, 0x00, 0x00, 0x01, // 12  0x0377E0  (832,  0)  64x256
        0xF8, 0xA9, 0x03, 0x00, 0x80, 0x03, 0x00, 0x01, 0x40, 0x00, 0xC0, 0x00, // 13  0x03A9F8  (896,256)  64x192
        0x1C, 0xBB, 0x03, 0x00, 0x80, 0x02, 0x00, 0x01, 0x78, 0x00, 0xC0, 0x00, // 14  0x03BB1C  (640,256) 120x192
        0x70, 0x03, 0x04, 0x00, 0x00, 0x00, 0xF0, 0x01, 0x00, 0x01, 0x10, 0x00, // 15  0x040370  (  0,496) 256x 16
        0x14, 0x13, 0x04, 0x00, 0x00, 0x01, 0xF0, 0x01, 0x00, 0x01, 0x10, 0x00, // 16  0x041314  (256,496) 256x 16
        0x48, 0x21, 0x04, 0x00, 0xC0, 0x03, 0x80, 0x01, 0x40, 0x00, 0x80, 0x00, // 17  0x042148  (960,384)  64x128
        0x18, 0x3B, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 18  0x043B18  rect zero — decode only
    };

    // GHIDRA: FUN_800308bc @ 0x800308BC
    // ClearVram. One of the four routines whose body is identical to TITLE.EXE's — TITLE's
    // ClearVram @ 0x80057508 has the same RECT{0,0,0x400,0x200}, the same ClearImage and the same
    // DrawSync(0). Called from main line 14, in the middle of the shared prologue.
    internal static void FUN_800308bc()
    {
        var local_10 = new RECT
        {
            w = 0x400,
            h = 0x200,
            x = 0,
            y = 0,
        };
        ClearImage(local_10, 0, 0, 0);
        DrawSync(0);
    }

    // GHIDRA: FUN_80030698 @ 0x80030698
    // Graphics and CD bring-up, called from main line 22 — genuinely before the loop, unlike the
    // asset load. Four parts: the libgs video mode, the USAGI.B lookup, a triangular table of
    // GsSPRITE addresses, and the four GsLINE.
    //
    // ON THE VIDEO MODE, because it changes what every 2D coordinate in this overlay means:
    // GsInitGraph(0x140, 0xF0, 0, 0, 0) is 320x240, interlace off, dither off, 15-bit; then
    // GsDefDispBuff(0, 0, 0x140, 0) puts buffer 0 at VRAM (0,0) and buffer 1 at (320,0); then
    // GsInit3D moves the sort origin to the centre of the screen (LibGs sets DAT_800593b0 =
    // width / 2 and DAT_800593b2 = height / 2). From that point a GsSPRITE's x and y are offsets
    // from (160,120) plus the target buffer's VRAM origin, not absolute VRAM coordinates.
    internal static void FUN_80030698()
    {
        ResetCallback();
        ResetGraph(0);
        LibGs.GsInitGraph(0x140, 0xf0, 0, 0, 0);

        // GHIDRA: FUN_8004879c @ 0x8004879C — Ghidra leaves it unnamed and plates it
        // "Possible GS_103.OBJ/GsDefDispBuff"; PsxSdkMonogame/LibGs.cs carries it under that name.
        LibGs.GsDefDispBuff(0, 0, 0x140, 0);

        LibGs.GsInit3D();
        SetDispMask(0);
        CdInit();

        CdlFILE pCVar1;
        int iVar7;
        do
        {
            pCVar1 = CdSearchFile(CdlFILE_80059744, "\\SUB\\USAGI.B;1".ToCharArray());
            iVar7 = 0;
        } while (pCVar1 == null);

        // THE TRIANGULAR POINTER TABLE. Seven records at DAT_800593b8; record i gets a row pointer
        // at +4 into DAT_80058e08 at word offset T(i) = i(i+1)/2, the row holds i + 1 entries, and
        // the record's own +0 field takes one more. Thirty-five values in all.
        //
        // WHAT THE VALUES ARE: iVar4 starts at 0x80065D5C and advances by 0x24 = sizeof(GsSPRITE)
        // per store, so they are the PSX addresses of GsSPRITE_ARRAY_800654ec[60] through [94] —
        // 0x80065D5C - 0x800654EC = 0x870 = 36 * 60, and the last store lands on 0x80066224 =
        // element 94, inside the hundred-element array.
        // PARTIAL: what the seven rows ARE is NOT ESTABLISHED. Every consumer of this table is in
        // a screen body, none of which is in this slice, so the values are written as the raw
        // addresses the original writes and nothing is inferred from their shape.
        int iVar4 = unchecked((int)0x80065d5c);
        int iVar5 = 0;
        int iVar6 = 0;
        do
        {
            iVar7 = iVar7 + iVar5;
            WriteI32(DAT_800593b8, iVar6 + 4, DAT_80058e08_Address + (iVar7 * 4));
            int iVar3 = 0;
            if (-1 < iVar5)
            {
                do
                {
                    int iVar2 = iVar3 * 4;
                    iVar3 = iVar3 + 1;

                    // The original dereferences the row pointer it just stored. Kept as a write
                    // through that pointer rather than collapsed into an index, so the two levels
                    // of indirection stay visible.
                    PsxRam.WriteI32(iVar2 + ReadI32(DAT_800593b8, iVar6 + 4), iVar4);
                    iVar4 = iVar4 + 0x24;
                } while (iVar3 <= iVar5);
            }

            WriteI32(DAT_800593b8, iVar6, iVar4);
            iVar4 = iVar4 + 0x24;
            iVar5 = iVar5 + 1;
            iVar6 = iVar6 + 0xc;
        } while (iVar5 < 7);

        // The four GsLINE, in the original's own store order. The byte addresses map onto the
        // GsLINE layout LibGs closed from GsSortLine: +0x0C r, +0x0D g, +0x0E b, +0x00 attribute.
        GsLINE_ARRAY_80065484[1].b = 0xff;                              // DAT_800654a2
        GsLINE_ARRAY_80065484[0].b = 0xff;                              // DAT_80065492
        GsLINE_ARRAY_80065484[3].r = 0xff;                              // DAT_800654c0
        GsLINE_ARRAY_80065484[2].r = 0xff;                              // DAT_800654b0
        GsLINE_ARRAY_80065484[3].g = 0xff;                              // DAT_800654c1
        GsLINE_ARRAY_80065484[2].g = 0xff;                              // DAT_800654b1
        GsLINE_ARRAY_80065484[1].r = 0x80;                              // DAT_800654a0
        GsLINE_ARRAY_80065484[0].r = 0x80;                              // DAT_80065490
        GsLINE_ARRAY_80065484[1].g = 0x80;                              // DAT_800654a1
        GsLINE_ARRAY_80065484[0].g = 0x80;                              // DAT_80065491
        GsLINE_ARRAY_80065484[3].b = 0x80;                              // DAT_800654c2
        GsLINE_ARRAY_80065484[2].b = 0x80;                              // DAT_800654b2
        GsLINE_ARRAY_80065484[3].attribute = 0x80000000;                // DAT_800654b4
        GsLINE_ARRAY_80065484[2].attribute = 0x80000000;                // DAT_800654a4
        GsLINE_ARRAY_80065484[1].attribute = 0x80000000;                // DAT_80065494
        GsLINE_ARRAY_80065484[0].attribute = 0x80000000;                // DAT_80065484

        FUN_800261a4();
    }

    // GHIDRA: FUN_800261a4 @ 0x800261A4
    private static void FUN_800261a4()
    {
        // BLOCKED: the pad bring-up, 64 bytes, and the whole of it is
        //     InitPAD(&DAT_80055d6c, 0x22, &DAT_80055d8e, 0x22); StartPAD(); ChangeClearPAD(0);
        // SELECT.EXE does NOT use libetc's PadRead — PadRead has zero callers in the whole
        // overlay. It installs the BIOS pad driver over two 0x22-byte buffers at 0x80055D6C and
        // 0x80055D8E and reads them directly (FUN_800261e4 @ 0x800261E4 and FUN_80026208
        // @ 0x80026208, the edge/repeat reader). LibApi's InitPAD / StartPAD / ChangeClearPAD are
        // all no-ops today, so a faithful transliteration would install a driver that never fills
        // those buffers and no button would ever be seen.
        // Not in this slice: the buffers and their reader belong with the screen bodies that
        // consume them. Nothing on the boot path reads a button.
    }

    // GHIDRA: FUN_80030848 @ 0x80030848
    // The GsSPRITE array initialiser, 116 bytes, 16 call sites. Fifteen stores per element, in the
    // original's own order.
    //
    // NOTE ON THE GHIDRA PLATE, which is wrong and is not followed: it claims cx/cy are zeroed.
    // They are not — the fifteen stores cover +0x04, +0x06, +0x0E, +0x0F, +0x08, +0x0A, +0x14,
    // +0x15, +0x16, +0x18, +0x1A, +0x1C, +0x1E, +0x20 and +0x00. Neither cx (+0x10), cy (+0x12)
    // nor tpage (+0x0C) is touched, so this leaves whatever was there. The code is what is
    // transliterated.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: the original takes one GsSPRITE * and walks it with `param_1 = param_1 + 9` on an
    // undefined4 *. Its call sites pass either the base of GsSPRITE_ARRAY_800654ec or an address
    // inside it (main's second call passes 0x80065AB0 = element 41), so the pointer becomes an
    // array plus a start index. No behaviour is added: startIndex + param_2 is the same span.
    internal static void FUN_80030848(LibGs.GsSPRITE[] param_1, int startIndex, int param_2)
    {
        int iVar1 = 0;
        if (0 < param_2)
        {
            do
            {
                LibGs.GsSPRITE p = param_1[startIndex + iVar1];
                p.x = 0;
                p.y = 0;
                p.u = 0;
                p.v = 0;
                p.w = 0x10;
                p.h = 0x10;
                p.r = 0x80;
                p.g = 0x80;
                p.b = 0x80;
                p.mx = 0;
                p.my = 0;
                p.scalex = 0x1000;
                p.scaley = 0x1000;
                p.rotate = 0;
                p.attribute = 0x80000000;
                iVar1 = iVar1 + 1;
            } while (iVar1 < param_2);
        }
    }

    // GHIDRA: FUN_80030908 @ 0x80030908
    // THE CD LOAD, called from main line 40 whenever bit 2 of DAT_80055b80 is set. It reads the
    // whole of \SUB\USAGI.B;1 in one shot, starts the CD-DA track, then decodes and uploads
    // eighteen chunks and decodes a nineteenth into RAM.
    //
    // THE READ IS SATISFIABLE BY THIS PORT TODAY, and each step is closed:
    //   CdControlB(CdlSetmode 0x0E, {0x80}) — 0x80 is double speed. LibCd.CdControlB returns
    //     CdSync(0, result) == CdlComplete, i.e. 1, so the `while (r == 0)` retry terminates.
    //   CdControl(CdlSetloc 0x02, &CdlFILE.pos) — LibCd's CD_cw latches the MSF as the seek target.
    //   CdSync(1) — returns CdlComplete = 2, which is what both enclosing loops test for.
    //   CdRead(sectors, 0x800B0000, 0x80) — the (int, int psxAddress, int) overload, which reads
    //     from the latched position through LibDs and writes into modelled PSX RAM. The ulong[]
    //     overload is a stub and must not be used.
    //   CdReadSync(1) — returns 0, and the loop is `while (r != 0)`. The desktop read is already
    //     synchronous, so there is nothing to wait for.
    // PARTIAL, and it is a deployment gap rather than a code one: LibDs resolves the file through
    // PsxSdkBridges' DiscFileResolver, which looks under <output>/data. DbzLegendsRemaster.csproj
    // copies MOVIE/BANDAI.STR, MOVIE/DBZ_OP.STR, SUB/TITLE.B and the three overlays, but NOT
    // SUB/USAGI.B. Until that Content item exists CdSearchFile returns null and FUN_80030698's
    // `do { ... } while (p == NULL)` above will not terminate. The csproj is not this slice's to
    // edit; the file itself is present at data/SUB/USAGI.B, 301056 bytes.
    internal static void FUN_80030908()
    {
        byte[] local_28 = new byte[8];
        byte[] auStack_20 = new byte[8];

        local_28[0] = 0x80;
        int iVar2;
        do
        {
            iVar2 = CdControlB(0x0e, local_28, null);
        } while (iVar2 == 0);

        CdControl(0x02, CdlFILE_80059744.pos, auStack_20);
        do
        {
            do
            {
                iVar2 = CdSync(1, auStack_20);
            } while (iVar2 == 0);
        } while ((iVar2 == 5) || (iVar2 != 2));

        CdRead((int)(((uint)CdlFILE_80059744.size + 0x7ffU) >> 0xb), DAT_800b0000_Address, 0x80);
        do
        {
            iVar2 = CdReadSync(1, auStack_20);
        } while (iVar2 != 0);

        iVar2 = 0;
        FUN_80025658();
        FUN_800258f0(10, 3);
        int iVar3 = 0;
        FUN_80025d04();

        // `rect = (RECT *)&DAT_8004f384;` — the RECT half of record 0, i.e. table byte offset 4 —
        // advanced by `rect = (RECT *)&rect[1].w;`, which is +12, the record stride.
        int rect = 4;
        do
        {
            int piVar1 = iVar3;
            iVar3 = iVar3 + 0xc;
            iVar2 = iVar2 + 1;
            Decompressor.DecompressLzss(
                DAT_800b0000, ReadI32(g_UsagiBChunkTable, piVar1), DAT_80090000, 0);
            LoadImage(RectAt(rect), DAT_80090000, 0);
            DrawSync(0);
            rect = rect + 0xc;
        } while (iVar2 < 0x12);

        // Record 18, outside the loop: decoded and never uploaded. `(&g_UsagiBChunkTable)[iVar2*3]`
        // is int-indexed, so iVar2 = 0x12 selects byte offset 18 * 12 = 216.
        Decompressor.DecompressLzss(
            DAT_800b0000, ReadI32(g_UsagiBChunkTable, iVar2 * 0xc), g_UsagiChunk18DecodedTiles, 0);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original hands LoadImage a RECT * that points straight into
    // g_UsagiBChunkTable. LibGpu.LoadImage takes a RECT object, so the four halfwords at that
    // offset are read into one. The table itself stays raw bytes and the cursor stays a byte
    // offset, so the walk is still the original's.
    private static RECT RectAt(int byteOffset)
    {
        return new RECT
        {
            x = ReadI16(g_UsagiBChunkTable, byteOffset),
            y = ReadI16(g_UsagiBChunkTable, byteOffset + 2),
            w = ReadI16(g_UsagiBChunkTable, byteOffset + 4),
            h = ReadI16(g_UsagiBChunkTable, byteOffset + 6),
        };
    }

    // GHIDRA: FUN_80025658 @ 0x80025658
    private static void FUN_80025658()
    {
        // BLOCKED: the CD-DA / sound-mix bring-up. It sets the CdlSetmode byte at DAT_80055ad0 to
        // 5 (CdlModeDA | CdlModeRept), calls CdGetToc into the TOC array at 0x80055CEC,
        // normalises every entry through CdPosToInt/CdIntToPos, defaults the track index to 3, and
        // then picks CD mix volumes from bit 0x801FF01E (the stereo/mono option) through CdMix
        // plus the libsnd wrappers SsSetMVol / SsSetSerialAttr / SsSetSerialVol and
        // SsSetStereo/SsSetMono.
        // LibCd.CdGetToc is a stub returning 0 and PsxSdkMonogame's LibSnd is entirely
        // unimplemented, so the TOC would come back empty. Not in this slice.
    }

    // GHIDRA: FUN_800258f0 @ 0x800258F0
    private static void FUN_800258f0(int param_1, int param_2)
    {
        // BLOCKED: the CD-DA play/seek helper. FUN_80030908 calls it as (10, 3) — mode bit 3 set,
        // track index 3 — which takes the CdlStandby / CdlSetmode / CdlPlay branch against the TOC
        // entry FUN_80025658 would have filled. Depends on that TOC, so it is blocked with it.
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_80025d04 @ 0x80025D04
    private static void FUN_80025d04()
    {
        // BLOCKED: re-issues CdControl(CdlPlay) at the current TOC position. Same dependency.
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: lets SELECT_EXE_exe.ResolveAddress map the PSX ranges this module declares, so
    // CdRead's raw 0x800B0000 destination and FUN_80030698's write through a stored row pointer
    // reach them the way they do on the console.
    internal static (byte[] Buffer, int Offset)? Resolve(int address)
    {
        int offset = address - DAT_800b0000_Address;
        if (offset >= 0 && offset < DAT_800b0000.Length)
        {
            return (DAT_800b0000, offset);
        }

        offset = address - DAT_80090000_Address;
        if (offset >= 0 && offset < DAT_80090000.Length)
        {
            return (DAT_80090000, offset);
        }

        offset = address - UsagiChunk18DecodedTilesAddress;
        if (offset >= 0 && offset < g_UsagiChunk18DecodedTiles.Length)
        {
            return (g_UsagiChunk18DecodedTiles, offset);
        }

        offset = address - DAT_80058e08_Address;
        if (offset >= 0 && offset < DAT_80058e08.Length)
        {
            return (DAT_80058e08, offset);
        }

        offset = address - DAT_800593b8_Address;
        if (offset >= 0 && offset < DAT_800593b8.Length)
        {
            return (DAT_800593b8, offset);
        }

        return null;
    }
}
