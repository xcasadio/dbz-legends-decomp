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
