namespace DbzLegendsRemaster.SELECT_EXE;

// The LZSS decoder SELECT.EXE's chunk loader uses. One function, and it needs a class to live in.
//
// IT IS THE SAME ROUTINE AS TITLE.EXE's DecompressLzss @ 0x80035778, CONFIRMED HERE BY BYTES, not
// taken on the diff's word. Both bodies are 156 bytes; read-memory over both returns the same 156
// bytes except for ONE word, at offset 0x50:
//     SELECT 0x80026158:  67 98 00 08  =  j 0x8002619C
//     TITLE  0x800357C8:  03 D6 00 08  =  j 0x8003580C
// which is the same `j` to the same place — 0x80026108 + 0x94 and 0x80035778 + 0x94, the function's
// own `jr ra` — relocated to each image's link address. Every other instruction is byte-identical.
// So the two are one source function compiled once and linked into two overlays, and this is
// named DecompressLzss for that reason rather than left as FUN_80026108.
//
// WHY THERE IS A SECOND COPY IN THIS PORT AND NOT A CALL INTO TITLE_EXE/: the console has two
// copies, one per overlay image, and calling across overlays would model a control transfer that
// does not exist — TITLE.EXE's image is gone by the time SELECT.EXE runs. TITLE_EXE/TitleImages.cs
// is also owned by a different slice.
//
// ONE OBSERVED DIFFERENCE FROM THAT COPY, reported and not acted on: TITLE_EXE/TitleImages.cs's
// DecompressLzss opens with `if (uVar3 == 0) return;`. No such test exists in the instructions of
// either image — 0x80026108+0x18 is `andi v1,t0,7` / `bne v1,zero` and there is no `beq t1,zero`
// anywhere in the 156 bytes. This transliteration follows the bytes and has no guard.
internal static class Decompressor
{
    // GHIDRA: DecompressLzss @ 0x80026108 (SELECT.EXE)
    // Ghidra's own plate on the function states the format: "u16 codeCount, then 8-code flag
    // groups processed MSB-first. Flag bit 0 copies one literal byte. Flag bit 1 copies a
    // back-reference with length = ((b0 >> 2) + 1) and distance = (((b0 & 3) << 8) | b1) + 1."
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: the original takes `ushort *src` and `uchar *dst` and does pointer arithmetic on
    // both. Each becomes a (byte[], offset) pair. The distinction between the two cursor variables
    // is kept: puVar5 is where the current code byte was found and puVar6 is the next unread byte,
    // and the back-reference branch advances from puVar5, not from puVar6 — `puVar6 = puVar5 + 1`
    // on a ushort *, i.e. two bytes past the code byte.
    //
    // PARTIAL on the back-reference: the original computes `iVar7 = (int)dst - distance` as an
    // absolute address and reads `*(iVar7 - 1)`. Every distance the format can encode is at most
    // 0x3FF + 1, and both call sites pass dstOffset 0, so a distance larger than the bytes already
    // written would read BEHIND the destination buffer. On the console that returns whatever
    // preceded it; here it throws IndexOutOfRangeException. Reproducing the console's garbage read
    // is not possible without modelling the neighbouring RAM, and inventing a clamp would be
    // fixing the original — rule 12. It is left to fault, and stated.
    internal static void DecompressLzss(byte[] src, int srcOffset, byte[] dst, int dstOffset)
    {
        uint uVar8 = 0;
        ushort uVar3 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8));
        int puVar6 = srcOffset + 2;

        // in_t2 is a live register on entry in the decompilation, never initialised by the
        // function. It cannot be read before it is written: uVar8 starts at 0, so `(uVar8 & 7) ==
        // 0` is true on the first pass and the flag byte is loaded into it there.
        int in_t2 = 0;

        do
        {
            uint uVar4;
            int puVar5;
            while (true)
            {
                uVar4 = src[puVar6];
                puVar5 = puVar6;
                if ((uVar8 & 7) == 0)
                {
                    puVar5 = puVar6 + 1;
                    in_t2 = (int)(uVar4 << 0x18);
                    uVar4 = src[puVar5];
                }

                puVar6 = puVar5 + 1;
                if (in_t2 < 0)
                {
                    break;
                }

                dst[dstOffset] = (byte)uVar4;
                dstOffset = dstOffset + 1;
                uVar8 = uVar8 + 1;
                in_t2 = in_t2 << 1;
                if (uVar8 == uVar3)
                {
                    return;
                }
            }

            uint uVar9 = uVar4 >> 2;
            byte bVar2 = src[puVar6];
            puVar6 = puVar5 + 2;
            int iVar7 = dstOffset - (int)(((uVar4 & 3) << 8) | bVar2);
            do
            {
                int puVar1 = iVar7 + -1;
                iVar7 = iVar7 + 1;
                dst[dstOffset] = dst[puVar1];
                uVar9 = uVar9 - 1;
                dstOffset = dstOffset + 1;
            } while (-1 < (int)uVar9);

            uVar8 = uVar8 + 1;
            in_t2 = in_t2 << 1;
        } while (uVar8 != uVar3);
    }
}
