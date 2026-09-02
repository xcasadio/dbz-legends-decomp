using PsxSdkMonogame;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.LibGte;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's disc and VRAM front door: search a file, seek, read it, LZSS-decode it, push it into
// VRAM. Eight functions, plus the four globals they own.
//
// THE WHOLE FILE IS THE SAME OBJECT CODE AS TITLE.EXE, RELINKED. That is not an analogy, it is a
// byte count. Ghidra reports these sizes, and every pair is equal:
//
//   this file (VS.EXE)                                bytes | TITLE.EXE                    bytes
//   FUN_80061460  @ 0x80061460  ClearVram                76  | ClearVram @ 0x80057508          76
//   FUN_800615cc  @ 0x800615CC  SetupGeometry           532  | SetupGeometry @ 0x80057674     532
//   DecompressAndLoadImage @ 0x80061A60                 172  | LoadCompressedImageInVram
//                                                             |   @ 0x80057B08                172
//   LoadImage_ReturnTPageOrClutId @ 0x80061B0C          204  | LoadImageInVram @ 0x80057BB4   204
//   FUN_80061d4c  @ 0x80061D4C  ReadFile                 76  | ReadFile @ 0x80057DF4           76
//   FUN_80061d98  @ 0x80061D98  ReadCDData              248  | ReadCDData @ 0x80057E40        248
//   FUN_80061ed8  @ 0x80061ED8  WaitSearchFile           68  | WaitSearchFile @ 0x80057F80     68
//   DecompressLZSS @ 0x80034E10                         156  | DecompressLzss @ 0x80035778    156
//
// and the two decompilations agree statement for statement, differing only in the names Ghidra
// invented for the locals. So the C# names below are TITLE.EXE's names where VS.EXE's Ghidra
// symbol is still raw; the `GHIDRA:` line always spells what VS.EXE actually carries, never the
// borrowed name. Three of the eight are already named in VS.EXE itself
// (DecompressAndLoadImage, LoadImage_ReturnTPageOrClutId, DecompressLZSS) and keep those names.
//
// Deliberately NOT calling DbzLegendsRemaster.TITLE_EXE: the two overlays are linked separately,
// their globals live at different addresses, and merging them would be the "one cleaner C# API for
// several original functions" the port mandate forbids. The PSX SDK is the opposite case and is
// reused as is — CdSearchFile, CdControl, CdSync, CdRead, CdReadSync, ClearImage, LoadImage,
// DrawSync, VSync and the GTE entry points all come from PsxSdkMonogame unchanged.
//
// OWNERSHIP CAVEAT, stated rather than hidden: SetupGeometry writes twenty-four PSX scratchpad
// globals at 0x1F8000xx, and this slice is the first VS.EXE code to need any of them. They are
// declared here because this file is the whole of the slice; TITLE.EXE keeps its own copies in
// TITLE_EXE/GteScratch.cs. When a later VS.EXE slice needs the sprite corners at 0x1F800020 or the
// rest of the scratchpad, these belong in a VS_EXE/GteScratch.cs beside them, moved as they are.
internal static class FileIo
{
    // GHIDRA: g_cdFileBufferTable @ 0x801D2000 (VS.EXE)
    // The destination of every CD read in the overlay and the source of most VRAM uploads.
    //
    // PARTIAL: the extent is not closed — the symbol is in uninitialised RAM, so Ghidra records no
    // size for it. The bound below is the largest extent any evidence in VS.EXE demands, and it is
    // reached in one call: FUN_80062684 @ 0x80062684 decodes into it and then uploads
    // 0xa0 halfwords by 0xf0 rows out of it (LoadImage_ReturnTPageOrClutId(&g_cdFileBufferTable,
    // 0x140, 0, 0xa0, 0xf0, 0) at 0x8006276C), that is 0xa0 * 0xf0 * 2 = 0x12C00 bytes. Every other
    // user stays inside that: main @ 0x800620F4 reaches 0x801D555C for a 0x40 x 0x100 block, which
    // ends at +0xB55C, and FUN_8005cbe0 @ 0x8005CBE0 reaches +0x920.
    //
    // The identical symbol in TITLE.EXE sits at the identical address with the identical bound
    // (TITLE_EXE/LoadingScreen.cs, BYTE_ARRAY_801d2000), which is a second reading of the same
    // linker layout rather than a coincidence.
    internal static readonly byte[] g_cdFileBufferTable = new byte[0xa0 * 0xf0 * 2];

    // JUSTIFICATION: C# language bridge only
    // RELATION: main and FUN_8005cbe0 pass `&g_cdFileBufferTable` as a raw pointer to ReadFile,
    // ReadCDData and LoadImage_ReturnTPageOrClutId, all three of which take the address rather than
    // the array. This is that address.
    internal const int g_cdFileBufferTableAddress = unchecked((int)0x801D2000);

    // GHIDRA: DAT_800a0d58 @ 0x800A0D58 (VS.EXE)
    // The staging buffer every LZSS decode lands in before its upload. DecompressAndLoadImage
    // decodes into it and uploads out of it in one step; FUN_8005cbe0 @ 0x8005CBE0 and
    // FUN_80061bd8 @ 0x80061BD8 use the same one.
    //
    // 0x8000 is exact, not generous, and two independent readings give it:
    //   * main @ 0x800620F4 calls DecompressAndLoadImage(&DAT_801d20a0, 0x280, 0, 0x40, 0x100, 0) —
    //     one decode of 0x40 halfwords by 0x100 rows, 0x8000 bytes, uploaded out of offset 0;
    //   * FUN_8005cbe0 uploads six blocks out of this buffer whose absolute addresses tile
    //     +0x000 (0x80 x 1), +0x100 (0x100 x 1), +0x300 (0x20 x 0x80), +0x2300 (0x28 x 0x70),
    //     +0x4600 (0x28 x 0x60) and +0x6400 (0x40 x 0x20) with no gap, ending at +0x7400.
    // TITLE.EXE's g_ImageDecodeBuffer @ 0x80096664 is the same buffer relocated, and closed at the
    // same 0x8000 by the same two arguments.
    internal static readonly byte[] DAT_800a0d58 = new byte[0x8000];

    // JUSTIFICATION: C# language bridge only
    // RELATION: DecompressAndLoadImage hands `&DAT_800a0d58` straight to
    // LoadImage_ReturnTPageOrClutId, which takes the raw address. This is that address.
    private const int Dat800a0d58Address = unchecked((int)0x800A0D58);

    // GHIDRA: DAT_8008d48c @ 0x8008D48C (VS.EXE)
    // The scratch rect LoadImage_ReturnTPageOrClutId fills before every upload. The original casts
    // it itself — `LoadImage((RECT *)&DAT_8008d48c, buffer)` — and writes its four halfwords as
    // DAT_8008d48c (x), DAT_8008d48e (y), DAT_8008d490 (w) and DAT_8008d492 (h), which is the RECT
    // field order.
    private static readonly RECT RECT_8008d48c = new();

    // ==== PSX scratchpad, 0x1F800000-0x1F80012F ================================================
    // Only the words SetupGeometry touches. See the OWNERSHIP CAVEAT in the file header.
    //
    // Held as typed objects rather than as raw scratchpad bytes because the original casts them
    // itself at every use — `SetColorMatrix((MATRIX *)&DAT_1f8000e4)`,
    // `RotMatrix((SVECTOR *)&DAT_1f800104, (MATRIX *)&DAT_1f800000)` — and the SDK's GTE entry
    // points take MATRIX / SVECTOR directly.

    // GHIDRA: DAT_1f800000 @ 0x1F800000 (VS.EXE)
    // RotMatrix's output and the matrix handed to both SetLightMatrix and SetRotMatrix.
    internal static readonly MATRIX MATRIX_1f800000 = new();

    // GHIDRA: DAT_1f80007c @ 0x1F80007C (VS.EXE)
    // vx / vy / vz at 0x7C, 0x7E and 0x80; the original writes the three halfwords separately and
    // then casts the address to SVECTOR * for RotMatrix.
    internal static readonly SVECTOR SVECTOR_1f80007c = new();

    // GHIDRA: DAT_1f800084 @ 0x1F800084 (VS.EXE)
    internal static short DAT_1f800084;

    // GHIDRA: DAT_1f800086 @ 0x1F800086 (VS.EXE)
    internal static short DAT_1f800086;

    // GHIDRA: DAT_1f800088 @ 0x1F800088 (VS.EXE)
    internal static short DAT_1f800088;

    // GHIDRA: _DAT_1f8000b4 @ 0x1F8000B4 (VS.EXE)
    internal static int _DAT_1f8000b4;

    // GHIDRA: DAT_1f8000b8 @ 0x1F8000B8 (VS.EXE)
    internal static int DAT_1f8000b8;

    // GHIDRA: _DAT_1f8000bc @ 0x1F8000BC (VS.EXE)
    internal static int _DAT_1f8000bc;

    // GHIDRA: _DAT_1f8000c0 @ 0x1F8000C0 (VS.EXE)
    internal static int _DAT_1f8000c0;

    // GHIDRA: DAT_1f8000c4 @ 0x1F8000C4 (VS.EXE)
    internal static int DAT_1f8000c4;

    // GHIDRA: DAT_1f8000c8 @ 0x1F8000C8 (VS.EXE)
    internal static int DAT_1f8000c8;

    // GHIDRA: DAT_1f8000cc @ 0x1F8000CC (VS.EXE)
    internal static int DAT_1f8000cc;

    // GHIDRA: DAT_1f8000d0 @ 0x1F8000D0 (VS.EXE)
    internal static int DAT_1f8000d0;

    // GHIDRA: DAT_1f8000d4 @ 0x1F8000D4 (VS.EXE)
    internal static int DAT_1f8000d4;

    // GHIDRA: DAT_1f8000d8 @ 0x1F8000D8 (VS.EXE)
    internal static int DAT_1f8000d8;

    // GHIDRA: DAT_1f8000dc @ 0x1F8000DC (VS.EXE)
    internal static int DAT_1f8000dc;

    // GHIDRA: DAT_1f8000e0 @ 0x1F8000E0 (VS.EXE)
    internal static int DAT_1f8000e0;

    // GHIDRA: DAT_1f8000e4 @ 0x1F8000E4 (VS.EXE)
    // The colour matrix handed to SetColorMatrix. Its nine shorts sit at 0xE4, 0xE6, 0xE8, 0xEA,
    // 0xEC, 0xEE, 0xF0, 0xF2 and 0xF4 — Ghidra prints them as nine separate DAT_ labels — which is
    // the m[0..8] order used in SetupGeometry below.
    internal static readonly MATRIX MATRIX_1f8000e4 = new();

    // GHIDRA: DAT_1f800104 @ 0x1F800104 (VS.EXE)
    // Written as DAT_1f800104 / DAT_1f800106 / DAT_1f800108 and then cast to SVECTOR * by the
    // original when it reaches RotMatrix.
    internal static readonly SVECTOR SVECTOR_1f800104 = new();

    // GHIDRA: DAT_1f800110 @ 0x1F800110 (VS.EXE)
    internal static int DAT_1f800110;

    // GHIDRA: DAT_1f800114 @ 0x1F800114 (VS.EXE)
    internal static int DAT_1f800114;

    // GHIDRA: DAT_1f800118 @ 0x1F800118 (VS.EXE)
    internal static int DAT_1f800118;

    // GHIDRA: DAT_1f80011c @ 0x1F80011C (VS.EXE)
    internal static int DAT_1f80011c;

    // GHIDRA: DAT_1f800120 @ 0x1F800120 (VS.EXE)
    internal static int DAT_1f800120;

    // GHIDRA: DAT_1f800124 @ 0x1F800124 (VS.EXE)
    internal static int DAT_1f800124;

    // ==== functions ============================================================================

    // GHIDRA: FUN_80061460 @ 0x80061460 (VS.EXE)
    // This is the ClearVram of TITLE.EXE @ 0x80057508 to the word — same 76 bytes, same rect, same
    // DrawSync. The C# name comes from there; the Ghidra symbol here is still raw.
    //
    // 0x400 x 0x200 is the whole PSX frame buffer in halfwords, so this wipes all of VRAM. Called
    // twice in VS.EXE: by main @ 0x800620F4 during boot, and from 0x80029364.
    internal static void ClearVram()
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

    // GHIDRA: FUN_800615cc @ 0x800615CC (VS.EXE)
    // This is the SetupGeometry of TITLE.EXE @ 0x80057674 to the word — same 532 bytes, same
    // scratchpad offsets, same call order. The C# name and the parameter names (ofx, ofy, h, rx,
    // ry, rz; param_4..param_7 left raw) come from TITLE.EXE's closed prototype; the Ghidra symbol
    // here is still FUN_800615cc and its parameters are still param_1..param_10.
    //
    // Ten arguments, and main @ 0x800620F4 passes all ten:
    // FUN_800615cc(0xa0, 0xef, 0x200, 0, 0, 0, 0x400, 0, 0, 0) at 0x80062228 — which is the same
    // ten values TITLE.EXE's second call passes. The second call site is 0x800293E4.
    //
    // PARTIAL: param_4..param_7 are each written to three scratchpad slots and never read here.
    // Their meaning is closed only by whatever later reads 0x1F8000B4..0x1F8000E0, which no
    // transliterated VS.EXE code does yet, so they keep their raw names.
    internal static void SetupGeometry(int ofx, int ofy, int h, int param_4, int param_5,
        int param_6, int param_7, short rx, short ry, short rz)
    {
        SetGeomOffset(ofx, ofy);
        SetGeomScreen(h);
        SetFarColor(0x80, 0x80, 0x80);
        SetBackColor(0x80, 0x80, 0x80);
        MATRIX_1f8000e4.m[6] = 0x1000;
        MATRIX_1f8000e4.m[3] = 0x1000;
        MATRIX_1f8000e4.m[0] = 0x1000;
        MATRIX_1f8000e4.m[8] = 0;
        MATRIX_1f8000e4.m[7] = 0;
        MATRIX_1f8000e4.m[5] = 0;
        MATRIX_1f8000e4.m[4] = 0;
        MATRIX_1f8000e4.m[2] = 0;
        MATRIX_1f8000e4.m[1] = 0;
        SetColorMatrix(MATRIX_1f8000e4);
        SVECTOR_1f800104.vx = 0;
        SVECTOR_1f800104.vy = 0;
        SVECTOR_1f800104.vz = 0;
        RotMatrix(SVECTOR_1f800104, MATRIX_1f800000);
        SetLightMatrix(MATRIX_1f800000);
        SVECTOR_1f80007c.vx = rx;
        SVECTOR_1f80007c.vy = ry;
        SVECTOR_1f80007c.vz = rz;
        DAT_1f800084 = rx;
        DAT_1f800086 = ry;
        DAT_1f800088 = rz;
        RotMatrix(SVECTOR_1f80007c, MATRIX_1f800000);
        SetRotMatrix(MATRIX_1f800000);
        _DAT_1f8000b4 = param_4;
        DAT_1f8000b8 = param_5;
        _DAT_1f8000bc = param_6;
        DAT_1f8000c4 = param_4;
        DAT_1f8000c8 = param_5;
        DAT_1f8000cc = param_6;
        DAT_1f8000d4 = param_4;
        DAT_1f8000d8 = param_5;
        DAT_1f8000dc = param_6;
        DAT_1f800114 = ofx;
        DAT_1f800124 = ofx;
        DAT_1f80011c = ofx;
        DAT_1f800110 = ofy;
        DAT_1f800120 = ofy;
        DAT_1f800118 = ofy;
        DAT_1f8000d0 = param_7;
        _DAT_1f8000c0 = param_7;
        DAT_1f8000e0 = param_7;
    }

    // GHIDRA: DecompressAndLoadImage @ 0x80061A60 (VS.EXE)
    // Ghidra already carries this name in VS.EXE; it is TITLE.EXE's LoadCompressedImageInVram
    // @ 0x80057B08 to the word, same 172 bytes.
    //
    // Decodes one LZSS block into DAT_800a0d58 and uploads it in a single step. Call order is the
    // original's: DecompressLZSS, then LoadImage_ReturnTPageOrClutId, then DrawSync — the DrawSync
    // comes AFTER the upload here, and the upload's result is what the function returns, masked to
    // 16 bits, with DrawSync's own return value discarded.
    //
    // Five call sites, all discarding the result: main @ 0x800620F4 twice (0x801D20A0 and
    // 0x801D555C, both 0x40 x 0x100), FUN_80034d98 @ 0x80034D98 once (&DAT_80081828, 0x10 x 0x40),
    // FUN_80047b10 @ 0x80047B10 once with a pointer field and a width already shifted right by 2,
    // and 0x80037EC4.
    internal static uint DecompressAndLoadImage(int buffer, ushort x, ushort y, short w, short h,
        byte isClut)
    {
        // JUSTIFICATION: C# language bridge only
        // RELATION: the original passes a0 straight to DecompressLZSS as a `ushort *`. The ported
        // DecompressLZSS takes a (byte[], offset) pair, so the raw PSX address is turned back into
        // one through the shared resolver, the same pattern TITLE_EXE/TitleImages.cs uses for the
        // same function.
        //
        // PARTIAL: returning early is not the original's behaviour — the original would simply
        // dereference. The arm means this overlay's resolver has no row for the address, not that
        // the disc is missing anything. Two of the five call sites are inside g_cdFileBufferTable
        // (0x801D20A0 and 0x801D555C) and Resolve below answers for both; &DAT_80081828 is a .data
        // block no slice owns yet.
        var resolved = PsxRam.AddressResolver?.Invoke(buffer);
        if (resolved == null)
        {
            return 0;
        }

        (byte[] src, int srcOffset) = resolved.Value;

        DecompressLZSS(src, srcOffset, DAT_800a0d58, 0);
        uint uVar1 = LoadImage_ReturnTPageOrClutId(Dat800a0d58Address, x, y, w, h, isClut);
        DrawSync(0);
        return uVar1 & 0xffff;
    }

    // GHIDRA: LoadImage_ReturnTPageOrClutId @ 0x80061B0C (VS.EXE)
    // Ghidra already carries this name in VS.EXE; it is TITLE.EXE's LoadImageInVram @ 0x80057BB4
    // to the word, same 204 bytes.
    //
    // Fills the scratch rect, uploads, and returns a tpage id when isClut is 0 or a CLUT id
    // otherwise. 23 call sites in the overlay, which is why it is internal.
    //
    // The `buffer` parameter is `u_long *` in the original and every one of the 23 call sites hands
    // it a raw address, so this port takes the address and goes through LibGpu's LoadImage(RECT,
    // int) overload, which exists for exactly this shape.
    //
    // The arithmetic below is transliterated as printed, sign extension and all, and the local
    // names stay Ghidra's iVar1 / iVar2 / iVar3 because none of the three has a closed meaning:
    // iVar3 accumulates the x term, iVar2 the y term, and the result is their sum masked to 16
    // bits. The else branch assigns iVar3 twice — once unconditionally and again when the
    // sign-extended x is negative — which is the original's own shape, not a transcription slip.
    internal static uint LoadImage_ReturnTPageOrClutId(int buffer, ushort x, ushort y, short w,
        short h, byte isClut)
    {
        RECT_8008d48c.h = h;
        RECT_8008d48c.x = (short)x;
        RECT_8008d48c.y = (short)y;
        RECT_8008d48c.w = w;
        LoadImage(RECT_8008d48c, buffer);

        int iVar1;
        int iVar2 = (int)((uint)x << 0x10) >> 0x10;
        int iVar3;
        if (isClut == 0)
        {
            if (iVar2 < 0)
            {
                iVar2 = iVar2 + 0x3f;
            }

            iVar1 = (short)y;
            iVar3 = iVar2 >> 6;
            if (iVar1 < 0)
            {
                iVar1 = iVar1 + 0xff;
            }

            iVar2 = (iVar1 >> 8) << 4;
        }
        else
        {
            iVar3 = (int)((uint)x << 0x10) >> 0x14;
            if (iVar2 < 0)
            {
                iVar3 = (iVar2 + 0xf) >> 4;
            }

            iVar2 = (int)((uint)y << 0x10) >> 10;
        }

        return (uint)(iVar3 + iVar2) & 0xffff;
    }

    // GHIDRA: FUN_80061d4c @ 0x80061D4C (VS.EXE)
    // This is the ReadFile of TITLE.EXE @ 0x80057DF4 to the word — same 76 bytes, same two calls.
    // The C# name comes from there; the Ghidra symbol here is still raw. Twelve call sites, the
    // most-referenced function of this slice.
    //
    // PARTIAL, and it is the one place this file departs from Ghidra's printed prototype:
    // Ghidra types FUN_80061d4c as returning void, but v0 is live out of it. The last thing the
    // body does is call FUN_80061d98 and fall into the epilogue, and Ghidra's own decompilation of
    // two call sites reads the result back — FUN_80026d08 @ 0x80026D08 spells
    // `iVar1 = FUN_80061d4c(...); if (iVar1 < 0)` and FUN_800356dc @ 0x800356DC spells
    // `iVar2 = FUN_80061d4c(...); if (iVar2 == -1)`. The five epilogue instructions between the jal
    // and the `jr ra` were not read one by one, so "nothing clobbers v0" is inference; that two
    // independent callers consume the value is not.
    //
    // Reproduced without correction, per the port mandate: ReadCDData returns either an unsigned
    // sector count or 0, so BOTH of those caller tests — `< 0` and `== -1` — can never fire. That
    // is the original's behaviour and it stays.
    internal static uint ReadFile(char[] fileName, int buffer, short mode)
    {
        var auStack_28 = new CdlFILE();
        WaitSearchFile(fileName, auStack_28);
        return ReadCDData(auStack_28, buffer, mode);
    }

    // GHIDRA: FUN_80061ed8 @ 0x80061ED8 (VS.EXE)
    // This is the WaitSearchFile of TITLE.EXE @ 0x80057F80 to the word — same 68 bytes, same loop.
    // The C# name comes from there; the Ghidra symbol here is still raw.
    //
    // Retries CdSearchFile forever until it stops returning null. Internal rather than private
    // because two of its three call sites are outside this file: FUN_8005cbe0 @ 0x8005CBE0 calls it
    // directly, twice (0x8005CD10 for \CHR_DATA\FACE.B;1 and 0x8005CE4C for
    // \CHR_DATA\OV_CHR_A.B;1), because it needs the CdlFILE back — once to convert the position
    // with CdPosToInt, once to overwrite the size CdSearchFile just filled in with a hard 0x3800.
    //
    // Note the argument order: the original is FUN_80061ed8(name, fp) but CdSearchFile is
    // CdSearchFile(fp, name), so the two are swapped at the call, exactly as below.
    internal static void WaitSearchFile(char[] fileName, CdlFILE cdlFile)
    {
        CdlFILE pCVar1;
        do
        {
            pCVar1 = CdSearchFile(cdlFile, fileName);
        } while (pCVar1 == null);
    }

    // GHIDRA: FUN_80061d98 @ 0x80061D98 (VS.EXE)
    // This is the ReadCDData of TITLE.EXE @ 0x80057E40 to the word — same 248 bytes, same nested
    // retry loops. The C# name comes from there; the Ghidra symbol here is still raw.
    //
    // CdlSetloc (command 2) to the file's position, spin on CdSync until it reports something other
    // than 0, retry the whole seek while it reports 5, then spin on CdRead until it returns 1.
    // ceil(size / 2048) sectors, mode 0x80.
    //
    // `mode` non-zero means "do not wait": the original breaks straight out of the outer loop and
    // returns 0 without ever calling CdReadSync. Otherwise it drains CdReadSync, VSyncing between
    // polls, and returns the sector count — unless CdReadSync ended at -1, in which case the whole
    // seek-and-read is retried from the top.
    //
    // Internal because FUN_8005cbe0 @ 0x8005CBE0 calls it directly, twice (0x8005CD88 and
    // 0x8005CE68), with a CdlFILE whose position CdIntToPos wrote and whose size it set by hand.
    internal static uint ReadCDData(CdlFILE cdlFile, int buffer, short mode)
    {
        uint sectors = (uint)(cdlFile.size + 0x7ff) >> 0xb;
        byte[] auStack_30 = new byte[8];
        int iVar1;
        int local_28;

        while (true)
        {
            do
            {
                CdControl(2, cdlFile.pos, auStack_30);
                do
                {
                    local_28 = CdSync(0, auStack_30);
                } while (local_28 == 0);
            } while (local_28 == 5);

            do
            {
                iVar1 = CdRead((int)sectors, buffer, 0x80);
            } while (iVar1 != 1);

            if (mode != 0)
            {
                break;
            }

            // The original spells this as `while (iVar1 = CdReadSync(0, auStack_30), 0 < iVar1)`;
            // C# has no comma operator, so the assignment is lifted out unchanged.
            iVar1 = CdReadSync(0, auStack_30);
            while (0 < iVar1)
            {
                VSync(0);
                iVar1 = CdReadSync(0, auStack_30);
            }

            if (iVar1 != -1)
            {
                return sectors;
            }
        }

        return 0;
    }

    // GHIDRA: DecompressLZSS @ 0x80034E10 (VS.EXE)
    // Ghidra already carries this name in VS.EXE. It is TITLE.EXE's DecompressLzss @ 0x80035778 to
    // the word: both are 156 bytes and the two decompilations are identical line for line, the only
    // difference being that TITLE.EXE's parameters are called param_1 / param_2 and VS.EXE's are
    // called buffer / outBuffer.
    //
    // A 16-bit header gives the command count. Every eighth command is preceded by a flag byte
    // whose bits are walked through the sign of a word shifted left by 24; a set bit selects a
    // back-reference, a clear bit a literal. A back-reference is two bytes: the length is
    // (first >> 2) + 1 — the +1 falls out of the `while (-1 < uVar9)` post-test — and the distance
    // is ((first & 3) << 8) | second, copied back one byte at a time from one before that.
    //
    // TRANSLITERATED WITHOUT THE GUARD TITLE_EXE ADDED. TITLE_EXE/TitleImages.cs opens its copy
    // with `if (uVar3 == 0) return;`. That test is in NEITHER original: Ghidra prints no such
    // branch for 0x80034E10 or for 0x80035778, and the loop below is a do-while whose exit test is
    // `uVar8 != uVar3` after uVar8 has already been incremented past 0. With a zero header the
    // original therefore runs away, and so does this. Port mandate rule 12 — do not fix a
    // behaviour of the original because it looks broken. Here the runaway surfaces as an
    // IndexOutOfRangeException instead of silent RAM corruption, which is the honest failure.
    internal static void DecompressLZSS(byte[] buffer, int bufferOffset, byte[] outBuffer,
        int outBufferOffset)
    {
        uint uVar8 = 0;
        ushort uVar3 = (ushort)(buffer[bufferOffset] | (buffer[bufferOffset + 1] << 8));

        // `buffer + 1` on a ushort *, so two bytes.
        bufferOffset = bufferOffset + 2;

        // The original reads t2 before writing it on any pass where (uVar8 & 7) != 0; C# demands
        // definite assignment. uVar8 starts at 0, so the very first pass always writes it and the
        // initial value is never observed.
        int in_t2 = 0;

        do
        {
            uint uVar4;
            int puVar5;
            while (true)
            {
                uVar4 = buffer[bufferOffset];
                puVar5 = bufferOffset;
                if ((uVar8 & 7) == 0)
                {
                    puVar5 = bufferOffset + 1;
                    in_t2 = (int)(uVar4 << 0x18);
                    uVar4 = buffer[puVar5];
                }

                bufferOffset = puVar5 + 1;
                if (in_t2 < 0)
                {
                    break;
                }

                outBuffer[outBufferOffset] = (byte)uVar4;
                outBufferOffset = outBufferOffset + 1;
                uVar8 = uVar8 + 1;
                in_t2 = in_t2 << 1;
                if (uVar8 == uVar3)
                {
                    return;
                }
            }

            uint uVar9 = uVar4 >> 2;
            byte bVar2 = buffer[bufferOffset];

            // `puVar5 + 1` on a ushort *, so two bytes past puVar5.
            bufferOffset = puVar5 + 2;
            int iVar7 = outBufferOffset - (int)(((uVar4 & 3) << 8) | bVar2);
            do
            {
                int puVar1 = iVar7 - 1;
                iVar7 = iVar7 + 1;
                outBuffer[outBufferOffset] = outBuffer[puVar1];
                uVar9 = uVar9 - 1;
                outBufferOffset = outBufferOffset + 1;
            } while (-1 < (int)uVar9);

            uVar8 = uVar8 + 1;
            in_t2 = in_t2 << 1;
        } while (uVar8 != uVar3);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the two byte buffers this file owns are addressed by raw PSX address from outside
    // it — main hands 0x801D20A0 and 0x801D555C to DecompressAndLoadImage, ReadCDData hands
    // g_cdFileBufferTable's address straight to CdRead, and DecompressAndLoadImage hands
    // 0x800A0D58 to LoadImage_ReturnTPageOrClutId. A VS.EXE-wide ResolveAddress chains this the way
    // TITLE_EXE_exe.ResolveAddress chains TitleImages.Resolve.
    internal static (byte[] Buffer, int Offset)? Resolve(int address)
    {
        int offset = address - g_cdFileBufferTableAddress;
        if (offset >= 0 && offset < g_cdFileBufferTable.Length)
        {
            return (g_cdFileBufferTable, offset);
        }

        offset = address - Dat800a0d58Address;
        if (offset >= 0 && offset < DAT_800a0d58.Length)
        {
            return (DAT_800a0d58, offset);
        }

        return null;
    }
}
