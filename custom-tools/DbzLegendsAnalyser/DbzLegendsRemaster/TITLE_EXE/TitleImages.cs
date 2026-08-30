using PsxSdkMonogame;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.TITLE_EXE;

// Image upload for the title screen. SetupTitleScreen @ 0x80021DD0 hands TITLE.B's load script to
// LoadImageListInVram @ 0x80057C80, which walks it and pushes each image into VRAM, decompressing the
// LZSS-packed ones through DecompressLzss @ 0x80035778 on the way.
internal static class TitleImages
{
    // GHIDRA: g_ImageDecodeBuffer @ 0x80096664
    // Staging buffer every decompression lands in before being uploaded.
    //
    // The 0x8000 below is not generous, it is EXACT, and two independent readings say so. The load
    // scripts of \STG\STG1TX.B;1, \STG\STG2TX.B;1 and \STG\STG3TX.B;1 — the three archives
    // FUN_800376c0 @ 0x800376C0 can reach — each contain an entry of 0x40 halfwords by 0x100 rows,
    // that is 0x8000 bytes decoded in one call, and none of their entries is larger. Independently,
    // LoadFACE_B @ 0x80052D68 uploads six blocks out of this buffer whose absolute addresses tile
    // +0x000 .. +0x7400 without a gap.
    private const int g_ImageDecodeBufferAddress = unchecked((int)0x80096664);
    internal static readonly byte[] g_ImageDecodeBuffer = new byte[0x8000];

    // GHIDRA: DAT_80110000 @ 0x80110000
    // Its PSX address. The buffer is TITLE_EXE_exe.DAT_80110000, the TITLE.B staging area.
    // SetupTitleScreen hands LoadImageListInVram two pointers INTO it, so the address is what is needed here,
    // not the array.
    private const int Dat80110000Address = unchecked((int)0x80110000);

    // GHIDRA: UpdateTitleScreen @ 0x80021E28
    // The title screen task. The block carries its address; TitleScreenTask holds the body.
    private const int UpdateTitleScreen = unchecked((int)0x80021E28);

    // GHIDRA: RECT_80083550 @ 0x80083550
    private static readonly RECT RECT_80083550 = new();

    // GHIDRA: SetupTitleScreen @ 0x80021DD0
    internal static void SetupTitleScreen()
    {
        // DAT_80110004 is TITLE.B's second word, the load-script offset, which
        // TITLE_B_FILE_FORMAT_ANALYSIS.md measured as 0x008. The original spells the call
        // `LoadImageListInVram((astruct_3 *)((int)&DAT_80110000 + DAT_80110004), (astruct_3 *)&DAT_80110000)`
        // — the script pointer is the buffer plus that offset, and the base is the buffer itself.
        int scriptOffset = PsxRam.ReadI32(unchecked((int)0x80110004));
        LoadImageListInVram(Dat80110000Address + scriptOffset, Dat80110000Address);
        TaskSystem.RegisterCallback(UpdateTitleScreen, () => TitleScreenTask.UpdateTitleScreen());
        TaskSystem.CreateTask(UpdateTitleScreen, 0, 6, 0x70, 0, TaskSystem.g_TaskListTail[6]);
    }

    // GHIDRA: LoadImageListInVram @ 0x80057C80
    // The load-script interpreter. Ghidra's prototype is
    //     void LoadImageListInVram(astruct_3 *param_1, astruct_3 *param_2)
    // — two POINTERS, not two offsets, and not into one fixed buffer. param_1 points at the script
    // (a u32 count followed by the entries) and param_2 at the base every entry's dataOffset is
    // measured from. All three call sites in the overlay are needed to see why both matter:
    //     SetupTitleScreen @ 0x80021DD0 (&DAT_80110000 + DAT_80110004, &DAT_80110000)
    //     FUN_80029aec @ 0x80029AEC  the same pair
    //     FUN_800376c0 @ 0x800376C0 (BYTE_ARRAY_801d2000, BYTE_ARRAY_801d2000)
    // so the buffer is NOT always TITLE.B and the two arguments are NOT always equal. This port
    // therefore takes the two PSX addresses the original takes.
    //
    // Entry layout, seven dwords on a 0x1C stride, confirmed against the decompiler's own cursor
    // arithmetic (puVar9 = param_1 + 4 is the kind, puVar8 = param_1 + 0x1C is the last field, and
    // both advance by seven words per turn):
    //   +0x00 kind (0 = LZSS, 1 = raw)   +0x04 dataOffset   +0x08 vramX   +0x0C vramY
    //   +0x10 widthWords                 +0x14 height       +0x18 isClut
    internal static void LoadImageListInVram(int param_1, int param_2)
    {
        uint uVar7 = (uint)PsxRam.ReadI32(param_1);
        uint uVar11 = 0;
        if (uVar7 == 0)
        {
            return;
        }

        // JUSTIFICATION: C# language bridge only
        // RELATION: the original passes `(int)&param_2->field0_0x0 + dataOffset` straight to
        // DecompressLzss as a `uchar *` and to LoadImage as a `u_long *`. Both ported callees take a
        // (byte[], offset) pair, so param_2 is turned back into one here — once, because the base
        // is a single buffer at every call site. The script itself is still read through raw
        // addresses, so param_1 needs no buffer at all.
        var resolved = PsxRam.AddressResolver?.Invoke(param_2);
        if (resolved == null)
        {
            // PARTIAL: bailing out is not the original's behaviour — the original would simply
            // dereference. Both live call sites resolve today (0x80110000 through
            // TITLE_EXE_exe.DAT_80110000 and 0x801D2000 through LoadingScreen.BYTE_ARRAY_801d2000),
            // so this arm means the overlay's resolver lost a range, not that the disc is missing.
            return;
        }

        (byte[] baseBuffer, int baseOffset) = resolved.Value;

        int entry = param_1 + 4;
        do
        {
            uint kind = (uint)PsxRam.ReadI32(entry);
            uint dataOffset = (uint)PsxRam.ReadI32(entry + 4);
            uint vramX = (uint)PsxRam.ReadI32(entry + 8);
            uint vramY = (uint)PsxRam.ReadI32(entry + 0x0c);
            uint widthWords = (uint)PsxRam.ReadI32(entry + 0x10);
            uint height = (uint)PsxRam.ReadI32(entry + 0x14);
            uint isClut = (uint)PsxRam.ReadI32(entry + 0x18);

            if (kind == 0)
            {
                DecompressLzss(baseBuffer, baseOffset + (int)dataOffset, g_ImageDecodeBuffer, 0);
                DisplayMachine.LoadImageInVram(
                    ToWordBuffer(g_ImageDecodeBuffer, (int)widthWords * (int)height * 2),
                    (ushort)vramX, (ushort)vramY, (short)widthWords, (short)height, (char)isClut);
                DrawSync(0);
            }
            else if (kind == 1)
            {
                RECT_80083550.x = (short)vramX;
                RECT_80083550.y = (short)vramY;
                RECT_80083550.w = (short)widthWords;
                RECT_80083550.h = (short)height;
                LoadImage(RECT_80083550, baseBuffer, baseOffset + (int)dataOffset);
            }

            uVar11 = uVar11 + 1;
            entry = entry + 0x1c;
        } while (uVar11 < uVar7);
    }

    // GHIDRA: LoadCompressedImageInVram @ 0x80057B08
    // Decompresses one LZSS block into the staging buffer and uploads it in a single step.
    //
    // Parameters, closed from the prologue: a0 is the PSX address of the compressed block; a1/a2/a3
    // are sign-extended to 16 bits (`sll 16` then `sra 16`, 0x80057B50..0x80057B64) and become
    // x, y, w; the fifth argument is loaded from 0x48(sp) (`lw s0` at 0x80057B10), sign-extended the
    // same way (0x80057B68/0x80057B6C) and stored to 0x10(sp) as h; the sixth is read from 0x4C(sp)
    // with `lbu` (0x80057B3C) and stored to 0x14(sp) as mode.
    //
    // The staging buffer is g_ImageDecodeBuffer, the SAME one LoadImageListInVram decodes into, not a second
    // buffer: 0x80057B30 holds `lui s2,0x8009` and 0x80057B34 `addiu s2,s2,0x6664`, and that one
    // register is both the second argument of DecompressLzss (`addu a1,s2,zero` at 0x80057B48) and
    // the first of LoadImageInVram (`addu a0,s2,zero` at 0x80057B4C).
    //
    // Call order is closed from the jal sequence and is DecompressLzss (0x80057B44), then
    // LoadImageInVram (0x80057B74), then DrawSync (0x80057B80) — the DrawSync comes AFTER the
    // upload here, the reverse of the order LoadImageListInVram uses. The upload's result is captured in
    // the DrawSync delay slot (`addu s0,v0,zero` at 0x80057B84), so DrawSync's own return value is
    // discarded, and the function returns the low 16 bits (`andi v0,s0,0xffff` at 0x80057B88).
    //
    // All three call sites pass mode 0 and discard the result: FUN_80058a9c @ 0x80058A9C twice
    // (0x801d20a0 and &DAT_801d555c, both 0x40 x 0x100), FUN_80035700 @ 0x80035700 once
    // (&DAT_80077a50, 0x10 x 0x40), and FUN_8003dce4 @ 0x8003DCE4 once with a pointer field and a
    // width already shifted right by 2.
    internal static uint LoadCompressedImageInVram(int param_1, ushort x, ushort y, short w, short h, char mode)
    {
        // JUSTIFICATION: C# language bridge only
        // RELATION: the original passes a1 straight to DecompressLzss as a `uchar *`. The ported
        // DecompressLzss takes a (byte[], offset) pair, so the raw PSX address is turned back into
        // one through the overlay's resolver, the same pattern PrimitivePools.InitializePrimitivePool uses.
        //
        // PARTIAL: bailing out is not the original's behaviour — the original would simply
        // dereference. Three of the four call sites are transliterated now and all three resolve:
        // 0x801D20A0 and 0x801D555C fall inside LoadingScreen.BYTE_ARRAY_801d2000, which
        // TITLE_EXE_exe.ResolveAddress answers for, and 0x80077A50 is SecondScreenSetup's own
        // baked .data block. FUN_8003dce4 @ 0x8003DCE4, the fourth, is not ported.
        var resolved = PsxRam.AddressResolver?.Invoke(param_1);
        if (resolved == null)
        {
            return 0;
        }

        (byte[] buffer, int offset) = resolved.Value;
        DecompressLzss(buffer, offset, g_ImageDecodeBuffer, 0);
        uint result = DisplayMachine.LoadImageInVram(
            ToWordBuffer(g_ImageDecodeBuffer, w * h * 2), x, y, w, h, mode);
        DrawSync(0);
        return result & 0xffff;
    }

    // GHIDRA: DecompressLzss @ 0x80035778
    // LZSS. A 16-bit header gives the command count; every eighth command is preceded by a flag
    // byte whose top bit selects literal or back-reference, walked through the sign bit of a word
    // shifted left by 24. A back-reference is two bytes: length is (first >> 2) + 1 and the
    // distance is ((first & 3) << 8) | second, read back from one before that.
    //
    // Cross-checked against PsxTools.LzssDecompressor, an independent reading of the same format.
    internal static void DecompressLzss(byte[] src, int srcOffset, byte[] dst, int dstOffset)
    {
        uint uVar7 = 0;
        ushort uVar3 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8));
        srcOffset = srcOffset + 2;
        int in_t2 = 0;

        if (uVar3 == 0)
        {
            return;
        }

        do
        {
            uint uVar4;
            int puVar5;
            while (true)
            {
                uVar4 = src[srcOffset];
                puVar5 = srcOffset;
                if ((uVar7 & 7) == 0)
                {
                    puVar5 = srcOffset + 1;
                    in_t2 = (int)(uVar4 << 0x18);
                    uVar4 = src[puVar5];
                }

                srcOffset = puVar5 + 1;
                if (in_t2 < 0)
                {
                    break;
                }

                dst[dstOffset] = (byte)uVar4;
                dstOffset = dstOffset + 1;
                uVar7 = uVar7 + 1;
                in_t2 = in_t2 << 1;
                if (uVar7 == uVar3)
                {
                    return;
                }
            }

            uint uVar8 = uVar4 >> 2;
            byte bVar2 = src[srcOffset];
            srcOffset = puVar5 + 2;
            int iVar6 = dstOffset - (int)(((uVar4 & 3) << 8) | bVar2);
            do
            {
                int puVar1 = iVar6 - 1;
                iVar6 = iVar6 + 1;
                dst[dstOffset] = dst[puVar1];
                uVar8 = uVar8 - 1;
                dstOffset = dstOffset + 1;
            } while (-1 < (int)uVar8);

            uVar7 = uVar7 + 1;
            in_t2 = in_t2 << 1;
        } while (uVar7 != uVar3);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: LoadImageInVram takes the u_long* form, so the staging bytes are packed into PSX
    // words for it. Everything past that point takes the ordinary byte path.
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

    // JUSTIFICATION: C# language bridge only
    // RELATION: lets the address resolver map the staging buffer, since LoadFACE_B and others
    // address it by raw PSX address rather than through this class.
    internal static (byte[] Buffer, int Offset)? Resolve(int address)
    {
        int offset = address - g_ImageDecodeBufferAddress;
        return offset >= 0 && offset < g_ImageDecodeBuffer.Length ? (g_ImageDecodeBuffer, offset) : null;
    }
}
