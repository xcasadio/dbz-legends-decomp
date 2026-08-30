using PsxSdkMonogame;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.TITLE_EXE;

// Image upload for the title screen. FUN_80021dd0 @ 0x80021DD0 hands TITLE.B's load script to
// FUN_80057c80 @ 0x80057C80, which walks it and pushes each image into VRAM, decompressing the
// LZSS-packed ones through FUN_80035778 @ 0x80035778 on the way.
internal static class TitleImages
{
    // GHIDRA: DAT_80096664 @ 0x80096664
    // Staging buffer every decompression lands in before being uploaded. Sized generously: the
    // largest single use is the 0x4000-byte 256x128 4bpp image TITLE.B carries, and LoadFACE_B
    // addresses this same buffer as far out as +0x4600.
    private const int Dat80096664Address = unchecked((int)0x80096664);
    internal static readonly byte[] DAT_80096664 = new byte[0x8000];

    // GHIDRA: FUN_80021e28 @ 0x80021E28
    // The title screen task. The block carries its address; TitleScreenTask holds the body.
    private const int FUN_80021e28 = unchecked((int)0x80021E28);

    // GHIDRA: RECT_80083550 @ 0x80083550
    private static readonly RECT RECT_80083550 = new();

    // GHIDRA: FUN_80021dd0 @ 0x80021DD0
    internal static void FUN_80021dd0()
    {
        // DAT_80110004 is TITLE.B's second word, the load-script offset, which
        // TITLE_B_FILE_FORMAT_ANALYSIS.md measured as 0x008.
        int scriptOffset = PsxRam.ReadI32(unchecked((int)0x80110004));
        FUN_80057c80(scriptOffset, 0);
        TaskSystem.RegisterCallback(FUN_80021e28, () => TitleScreenTask.FUN_80021e28());
        TaskSystem.CreateTask(FUN_80021e28, 0, 6, 0x70, 0, TaskSystem.g_TaskListTail[6]);
    }

    // GHIDRA: FUN_80057c80 @ 0x80057C80
    // The load-script interpreter. Both arguments are offsets into TITLE.B: the first points at the
    // script, the second at the file base that every entry's dataOffset is relative to.
    //
    // Entry layout, seven dwords, confirmed against the pointer arithmetic:
    //   +0x00 kind (0 = LZSS, 1 = raw)   +0x04 dataOffset   +0x08 vramX   +0x0C vramY
    //   +0x10 widthWords                 +0x14 height       +0x18 isClut
    internal static void FUN_80057c80(int scriptOffset, int baseOffset)
    {
        byte[] file = TITLE_EXE_exe.DAT_80110000;
        uint uVar7 = ReadU32(file, scriptOffset);
        uint uVar11 = 0;
        if (uVar7 == 0)
        {
            return;
        }

        int entry = scriptOffset + 4;
        do
        {
            uint kind = ReadU32(file, entry);
            uint dataOffset = ReadU32(file, entry + 4);
            uint vramX = ReadU32(file, entry + 8);
            uint vramY = ReadU32(file, entry + 0x0c);
            uint widthWords = ReadU32(file, entry + 0x10);
            uint height = ReadU32(file, entry + 0x14);
            uint isClut = ReadU32(file, entry + 0x18);

            if (kind == 0)
            {
                FUN_80035778(file, baseOffset + (int)dataOffset, DAT_80096664, 0);
                DisplayMachine.LoadImageInVram(
                    ToWordBuffer(DAT_80096664, (int)widthWords * (int)height * 2),
                    (ushort)vramX, (ushort)vramY, (short)widthWords, (short)height, (char)isClut);
                DrawSync(0);
            }
            else if (kind == 1)
            {
                RECT_80083550.x = (short)vramX;
                RECT_80083550.y = (short)vramY;
                RECT_80083550.w = (short)widthWords;
                RECT_80083550.h = (short)height;
                LoadImage(RECT_80083550, file, baseOffset + (int)dataOffset);
            }

            uVar11 = uVar11 + 1;
            entry = entry + 0x1c;
        } while (uVar11 < uVar7);
    }

    // GHIDRA: FUN_80057b08 @ 0x80057B08
    // Decompresses one LZSS block into the staging buffer and uploads it in a single step.
    //
    // Parameters, closed from the prologue: a0 is the PSX address of the compressed block; a1/a2/a3
    // are sign-extended to 16 bits (`sll 16` then `sra 16`, 0x80057B50..0x80057B64) and become
    // x, y, w; the fifth argument is loaded from 0x48(sp) (`lw s0` at 0x80057B10), sign-extended the
    // same way (0x80057B68/0x80057B6C) and stored to 0x10(sp) as h; the sixth is read from 0x4C(sp)
    // with `lbu` (0x80057B3C) and stored to 0x14(sp) as mode.
    //
    // The staging buffer is DAT_80096664, the SAME one FUN_80057c80 decodes into, not a second
    // buffer: 0x80057B30 holds `lui s2,0x8009` and 0x80057B34 `addiu s2,s2,0x6664`, and that one
    // register is both the second argument of FUN_80035778 (`addu a1,s2,zero` at 0x80057B48) and
    // the first of LoadImageInVram (`addu a0,s2,zero` at 0x80057B4C).
    //
    // Call order is closed from the jal sequence and is FUN_80035778 (0x80057B44), then
    // LoadImageInVram (0x80057B74), then DrawSync (0x80057B80) — the DrawSync comes AFTER the
    // upload here, the reverse of the order FUN_80057c80 uses. The upload's result is captured in
    // the DrawSync delay slot (`addu s0,v0,zero` at 0x80057B84), so DrawSync's own return value is
    // discarded, and the function returns the low 16 bits (`andi v0,s0,0xffff` at 0x80057B88).
    //
    // All three call sites pass mode 0 and discard the result: FUN_80058a9c @ 0x80058A9C twice
    // (0x801d20a0 and &DAT_801d555c, both 0x40 x 0x100), FUN_80035700 @ 0x80035700 once
    // (&DAT_80077a50, 0x10 x 0x40), and FUN_8003dce4 @ 0x8003DCE4 once with a pointer field and a
    // width already shifted right by 2.
    internal static uint FUN_80057b08(int param_1, ushort x, ushort y, short w, short h, char mode)
    {
        // JUSTIFICATION: C# language bridge only
        // RELATION: the original passes a1 straight to FUN_80035778 as a `uchar *`. The ported
        // FUN_80035778 takes a (byte[], offset) pair, so the raw PSX address is turned back into
        // one through the overlay's resolver, the same pattern PrimitivePools.FUN_80057094 uses.
        //
        // PARTIAL: none of the three call sites is transliterated yet, and the addresses they pass
        // (0x801D20A0, 0x801D555C, 0x80077A50) are not in TITLE_EXE_exe.ResolveAddress today, so
        // this resolve would fail if it were reached. Bailing out is not the original's behaviour —
        // the original would simply dereference — but there is nothing to decompress from until
        // those ranges are modelled.
        var resolved = PsxRam.AddressResolver?.Invoke(param_1);
        if (resolved == null)
        {
            return 0;
        }

        (byte[] buffer, int offset) = resolved.Value;
        FUN_80035778(buffer, offset, DAT_80096664, 0);
        uint result = DisplayMachine.LoadImageInVram(
            ToWordBuffer(DAT_80096664, w * h * 2), x, y, w, h, mode);
        DrawSync(0);
        return result & 0xffff;
    }

    // GHIDRA: FUN_80035778 @ 0x80035778
    // LZSS. A 16-bit header gives the command count; every eighth command is preceded by a flag
    // byte whose top bit selects literal or back-reference, walked through the sign bit of a word
    // shifted left by 24. A back-reference is two bytes: length is (first >> 2) + 1 and the
    // distance is ((first & 3) << 8) | second, read back from one before that.
    //
    // Cross-checked against PsxTools.LzssDecompressor, an independent reading of the same format.
    internal static void FUN_80035778(byte[] src, int srcOffset, byte[] dst, int dstOffset)
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

    private static uint ReadU32(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | (buffer[offset + 1] << 8)
               | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));

    // JUSTIFICATION: C# language bridge only
    // RELATION: lets the address resolver map the staging buffer, since LoadFACE_B and others
    // address it by raw PSX address rather than through this class.
    internal static (byte[] Buffer, int Offset)? Resolve(int address)
    {
        int offset = address - Dat80096664Address;
        return offset >= 0 && offset < DAT_80096664.Length ? (DAT_80096664, offset) : null;
    }
}
