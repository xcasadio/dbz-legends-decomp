using PsxSdkMonogame;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.TITLE_EXE;

// The loading screen. ShowLoadingScreen @ 0x800583FC reads \CHR_DATA\LOAD.B;1 off the disc, decodes it,
// uploads the picture and its CLUT to VRAM, then builds two textured bands and draws them once.
//
// Its only caller is FUN_80058a9c @ 0x80058A9C, which is not transliterated. This file holds the
// callee only, so that caller can land on it later without changing anything here.
internal static class LoadingScreen
{
    // GHIDRA: DAT_80110000 @ 0x80110000 — its PSX address, which CdRead is handed directly.
    // The buffer itself is TITLE_EXE_exe.DAT_80110000; this overlay reuses the same staging area
    // that already holds TITLE.B, and the CdRead below overwrites it. That overwrite is the
    // original's, not an accident of the port.
    private const int Dat80110000Address = unchecked((int)0x80110000);

    // GHIDRA: BYTE_ARRAY_801d2000 @ 0x801D2000
    // Destination of the LZSS decode, and the source of the first VRAM upload.
    //
    // PARTIAL: the extent of this symbol is not closed. It lives in an uninitialised RAM block, so
    // Ghidra records no size for it (the `byte[4096]` it prints is the default type it gives an
    // undefined label, not evidence). The size below is the largest extent any evidence in this
    // overlay demands: this function uploads 0xa0 halfwords by 0xf0 rows out of it, that is
    // 0xa0 * 0xf0 * 2 = 0x12C00 bytes. The other users of the same symbol stay inside that —
    // FUN_80058a9c @ 0x80058A9C addresses it as far as 0x801D555C for a 0x40 x 0x100 block, which
    // ends at +0xB55C. If a LOAD.B chunk ever decodes past 0x12C00 this will throw rather than
    // silently corrupt the next symbol, which is the honest failure.
    //
    // Not registered with RamRegion: nothing here takes its address, and the raw-address resolver
    // that LoadCompressedImageInVram would need is TITLE_EXE_exe.ResolveAddress, which this file does not own.
    internal static readonly byte[] BYTE_ARRAY_801d2000 = new byte[0xa0 * 0xf0 * 2];

    // GHIDRA: DISPENV_800a681c @ 0x800A681C
    // PARTIAL: the console has ONE display environment at this address, and FrameLoop @ 0x800587A8
    // already holds it — as a private field this file cannot reach. Both objects stand for the same
    // PSX global. The duplication is not observable, because SetDefDrawEnv's companion
    // SetDefDispEnv writes every field of a DISPENV before PutDispEnv reads any of them, so no
    // state crosses between the two holders. Collapsing them is a visibility change in FrameLoop,
    // not a semantic one.
    private static readonly DISPENV DISPENV_800a681c = new();

    // GHIDRA: OT_800a6830 @ 0x800A6830 — its PSX address. The table itself is FrameLoop.OT_800a6830.
    private const int Ot800a6830Address = unchecked((int)0x800A6830);

    // GHIDRA: DAT_800a7830 @ 0x800A7830
    // NOT a second ordering table: it is bucket 0x400 of the one at 0x800A6830. Closed from this
    // function alone — it clears 0x800 entries at 0x800A6830, which spans 0x800A6830..0x800A882F,
    // it submits with DrawOTag(&DAT_800a6830), and 0x800A7830 - 0x800A6830 = 0x1000 = 0x400 * 4.
    // Ghidra also shows the label has exactly two references in the whole overlay, both of them the
    // lui/addiu pair of the AddPrim calls below, so nothing else treats it as a table of its own.
    // Forward-linked by ClearOTag, bucket 0x400 draws in the middle of the table.
    private const int Ot800a7830Address = unchecked((int)0x800A7830);

    // GHIDRA: POLY_FT4_800b9dd4 @ 0x800B9DD4
    // The left band. Real memory rather than an object: AddPrim splices the packet's PSX address
    // into the bucket, so a primitive without an address cannot be drawn. Both packets are one
    // contiguous 0x50-byte run in .bss, exactly as the original's `p = p + 1` walks them.
    private const int PolyFt4800b9dd4Address = unchecked((int)0x800B9DD4);

    internal static readonly POLY_FT4Ref POLY_FT4_800b9dd4 =
        new(RamRegion(PolyFt4800b9dd4Address, POLY_FT4Ref.Size * 2), 0);

    // GHIDRA: POLY_FT4_800b9dfc @ 0x800B9DFC
    // The right band, the second packet of the same run.
    internal static readonly POLY_FT4Ref POLY_FT4_800b9dfc =
        new(POLY_FT4_800b9dd4.Buf, POLY_FT4Ref.Size);

    // GHIDRA: ShowLoadingScreen @ 0x800583FC
    internal static void ShowLoadingScreen()
    {
        DeclareOrderingTableAddress();

        CdlFILE pCVar1;
        int iVar2;
        POLY_FT4Ref p;
        short sVar3;
        int iVar4;
        CdlFILE CStack_38 = new();
        byte[] local_20 = new byte[8];

        local_20[0] = 0x80;
        CdControlB(0x0e, local_20, null);
        do
        {
            pCVar1 = CdSearchFile(CStack_38, "\\CHR_DATA\\LOAD.B;1".ToCharArray());
        } while (pCVar1 == null);

        CStack_38.size = 10;
        iVar2 = CdPosToInt(CStack_38.pos);

        // This seek used to be lost: CdPosToInt @ 0x80069938 and CdIntToPos @ 0x80069834 were
        // do-nothing stubs in the SDK, so CdIntToPos never wrote back into CStack_38.pos and the
        // read below always started at the file's first sector. Both are transliterated from the
        // image now, so the skip lands. It is DAT_1f80012c * 10 sectors, ten per chunk, and
        // DAT_1f80012c is the 0..2 counter main @ 0x800581DC keeps in SHORT_ARRAY_801ff000[0x87] —
        // so this picks one of three loading pictures, as it does on the console.
        CdIntToPos(iVar2 + (int)(GteScratch.DAT_1f80012c * 10), CStack_38.pos);

        // The original passes `(u_char *)&CStack_38`, and CdlFILE begins with its CdlLOC, so the
        // bytes the drive sees are exactly CStack_38.pos.
        CdControl(2, CStack_38.pos, local_20);
        do
        {
            do
            {
                iVar2 = CdSync(1, local_20);
            } while (iVar2 == 0);
        } while (iVar2 == 5 || iVar2 != 2);

        // Status 5 is CdlDiskError, and the loop above retries it for ever rather than reporting
        // it. That is the original's own shape and is kept.
        CdRead(CStack_38.size, Dat80110000Address, 0x80);
        do
        {
            iVar2 = CdReadSync(1, local_20);
        } while (iVar2 != 0);

        // The first 0x200 bytes of LOAD.B are the 256-entry CLUT, uploaded below; the LZSS payload
        // starts at +0x200.
        TitleImages.DecompressLzss(TITLE_EXE_exe.DAT_80110000, 0x200, BYTE_ARRAY_801d2000, 0);
        DisplayMachine.LoadImageInVram(
            ToWordBuffer(BYTE_ARRAY_801d2000, 0xa0 * 0xf0 * 2), 0x140, 0, 0xa0, 0xf0, '\0');
        DisplayMachine.LoadImageInVram(
            ToWordBuffer(TITLE_EXE_exe.DAT_80110000, 0x100 * 1 * 2), 0, 0x1e0, 0x100, 1, '\x01');
        SetDispMask(1);
        SetDefDrawEnv(FrameLoop.DRAWENV_800a67c0, 0, 0, 0x140, 0xf0);
        SetDefDispEnv(DISPENV_800a681c, 0, 0, 0x140, 0xf0);

        // DAT_800a67d4 is DRAWENV + 0x14, that is DRAWENV.tpage. Written here, before the table is
        // cleared, exactly where the `sh` sits at 0x80058570.
        FrameLoop.DRAWENV_800a67c0.tpage = 0x85;
        ClearOTag(FrameLoop.OT_800a6830, 0, 0x800);
        iVar4 = 0;
        sVar3 = 0x85;
        p = POLY_FT4_800b9dd4;
        iVar2 = 0;
        do
        {
            SetPolyFT4(p);
            SetSemiTrans(p, 0);
            SetShadeTex(p, 1);

            // The original keeps two cursors over the same two packets: `p`, a POLY_FT4 *, and
            // `iVar2`, a byte offset walked from the raw addresses &DAT_800b9dea and &DAT_800b9de2.
            // Those two are POLY_FT4 + 0x16 (tpage) and POLY_FT4 + 0x0e (clut) of the packet `p`
            // currently points at. Both cursors are kept, and the byte-offset ones go through the
            // packet bytes so the addressing stays the original's.
            POLY_FT4_800b9dd4.WriteHalf(0x16 + iVar2, sVar3);
            sVar3 = (short)(sVar3 + 2);
            POLY_FT4_800b9dd4.WriteHalf(0x0e + iVar2, 0x7800);
            p.r0 = 0x80;
            p.g0 = 0x80;
            p.b0 = 0x80;
            p = p[1];
            iVar4 = iVar4 + 1;
            iVar2 = iVar2 + 0x28;
        } while (iVar4 < 2);

        // The remaining stores are absolute in the original. Every address is a field of one of the
        // two packets, at the psyq POLY_FT4 offsets, and the order below is the machine order
        // (0x8005861c..0x80058720), not a regrouping:
        //
        //   0x800B9DDC x0   0x800B9DDE y0   0x800B9DE0 u0   0x800B9DE1 v0   0x800B9DE2 clut
        //   0x800B9DE4 x1   0x800B9DE6 y1   0x800B9DE8 u1   0x800B9DE9 v1   0x800B9DEA tpage
        //   0x800B9DEC x2   0x800B9DEE y2   0x800B9DF0 u2   0x800B9DF1 v2
        //   0x800B9DF4 x3   0x800B9DF6 y3   0x800B9DF8 u3   0x800B9DF9 v3    (packet 0x800B9DD4)
        //
        //   0x800B9E04 x0   0x800B9E06 y0   0x800B9E08 u0   0x800B9E09 v0
        //   0x800B9E0C x1   0x800B9E0E y1   0x800B9E10 u1   0x800B9E11 v1
        //   0x800B9E14 x2   0x800B9E16 y2   0x800B9E18 u2   0x800B9E19 v2
        //   0x800B9E1C x3   0x800B9E1E y3   0x800B9E20 u3   0x800B9E21 v3    (packet 0x800B9DFC)
        //
        // The result is two bands over the 320x240 screen, 0..256 and 256..320, sampling the 8bpp
        // picture the first upload put at VRAM (320, 0) through tpages 0x85 and 0x87 and the CLUT
        // the second upload put at (0, 480), which is what clut 0x7800 addresses.
        POLY_FT4_800b9dd4.x2 = 0;
        POLY_FT4_800b9dd4.x0 = 0;
        POLY_FT4_800b9dd4.x3 = 0x100;
        POLY_FT4_800b9dd4.x1 = 0x100;
        POLY_FT4_800b9dfc.x2 = 0x100;
        POLY_FT4_800b9dfc.x0 = 0x100;
        POLY_FT4_800b9dfc.x3 = 0x140;
        POLY_FT4_800b9dfc.x1 = 0x140;
        POLY_FT4_800b9dd4.y3 = 0xf0;
        POLY_FT4_800b9dd4.y2 = 0xf0;
        POLY_FT4_800b9dfc.y3 = 0xf0;
        POLY_FT4_800b9dfc.y2 = 0xf0;
        POLY_FT4_800b9dd4.u3 = 0xff;
        POLY_FT4_800b9dd4.u1 = 0xff;
        POLY_FT4_800b9dd4.y1 = 0;
        POLY_FT4_800b9dd4.y0 = 0;
        POLY_FT4_800b9dfc.y1 = 0;
        POLY_FT4_800b9dfc.y0 = 0;
        POLY_FT4_800b9dd4.u2 = 0;
        POLY_FT4_800b9dd4.u0 = 0;
        POLY_FT4_800b9dd4.v1 = 0;
        POLY_FT4_800b9dd4.v0 = 0;
        POLY_FT4_800b9dd4.v3 = 0xef;
        POLY_FT4_800b9dd4.v2 = 0xef;
        POLY_FT4_800b9dfc.u2 = 0;
        POLY_FT4_800b9dfc.u0 = 0;
        POLY_FT4_800b9dfc.u3 = 0x41;
        POLY_FT4_800b9dfc.u1 = 0x41;
        POLY_FT4_800b9dfc.v1 = 0;
        POLY_FT4_800b9dfc.v0 = 0;
        POLY_FT4_800b9dfc.v3 = 0xef;
        POLY_FT4_800b9dfc.v2 = 0xef;
        AddPrim(Ot800a7830Address, POLY_FT4_800b9dd4);
        AddPrim(Ot800a7830Address, POLY_FT4_800b9dfc);

        // DRAWENV + 0x16, 0x18, 0x19, 0x1a, 0x1b: dtd, isbg, r0, g0, b0. Written after the two
        // AddPrims, which is where the `sb` stores sit at 0x80058740..0x80058760. DRAWENV.dfe, at
        // +0x17, is not touched here and keeps whatever SetDefDrawEnv left.
        FrameLoop.DRAWENV_800a67c0.dtd = 0;
        FrameLoop.DRAWENV_800a67c0.isbg = 1;
        FrameLoop.DRAWENV_800a67c0.r0 = 0;
        FrameLoop.DRAWENV_800a67c0.g0 = 0;
        FrameLoop.DRAWENV_800a67c0.b0 = 0;
        PutDispEnv(DISPENV_800a681c);
        PutDrawEnv(FrameLoop.DRAWENV_800a67c0);
        DrawOTag(Ot800a6830Address);
        DrawSync(0);
    }

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: declares the ordering table's PSX address, so AddPrim can write a real link into
    // bucket 0x800A7830 and DrawOTag can resolve 0x800A6830 back to the buffer holding it. On the
    // console the table simply is at that address and nothing has to say so. FrameLoop does the
    // same registration on entry to its own loop; this function runs before that loop is ever
    // entered, and re-registering the same buffer object updates its base rather than adding a
    // second row, so both callers are safe.
    private static void DeclareOrderingTableAddress()
    {
        RamRegion(Ot800a6830Address, FrameLoop.OT_800a6830);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: LoadImageInVram @ 0x80057BB4 takes the u_long * form, so the staging bytes are
    // packed into PSX words for it. Same bridge as TitleImages' own ToWordBuffer, which is private
    // to that class.
    private static ulong[] ToWordBuffer(byte[] source, int byteCount)
    {
        if (byteCount <= 0 || byteCount > source.Length)
        {
            byteCount = source.Length;
        }

        int words = (byteCount + 3) / 4;
        ulong[] result = new ulong[words];
        for (int i = 0; i < words; i++)
        {
            int o = i * 4;
            uint word = source[o];
            if (o + 1 < byteCount) word |= (uint)source[o + 1] << 8;
            if (o + 2 < byteCount) word |= (uint)source[o + 2] << 16;
            if (o + 3 < byteCount) word |= (uint)source[o + 3] << 24;
            result[i] = word;
        }

        return result;
    }
}
