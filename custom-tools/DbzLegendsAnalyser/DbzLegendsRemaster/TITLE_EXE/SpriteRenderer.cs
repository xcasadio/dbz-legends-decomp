using PsxSdkMonogame;

namespace DbzLegendsRemaster.TITLE_EXE;

// The sprite-group renderer. DrawSpriteGroup @ 0x80048F88 walks a count-prefixed block of sprite
// records, builds one textured quad per record out of slot 1 of the primitive pool, projects it
// through the GTE and files it in the ordering table. It is what actually draws the title screen:
// UpdateTitleScreen @ 0x80021E28 calls it five times a frame, for the logo, the two background layers,
// PRESS START and the copyright line.
//
// THE RECORD STREAM. param_1 is a real PSX address; on the title screen it points into the TITLE.B
// staging buffer at 0x80110000.
//   +0x00  int32  record count, then the first record at +0x04
// RECORD, fixed 8-byte head:
//   +0x00  u8   u        -> prim.u0    = param_13 + u
//   +0x01  u8   v        -> prim.v0    = param_14 + v
//   +0x02  u8   localX   -> model X    = (short)(byte + 0xFF80), i.e. byte - 128
//   +0x03  u8   localY   -> model Y, same bias
//   +0x04  u16  clut     -> prim.clut  = param_11 + clut
//   +0x06  u16  packed   -> bits 0..8   prim.tpage = param_12 + (packed & 0x1ff)
//                          bits 12..15  read as (packed >> 9) & 0x78 and used verbatim as BOTH
//                                       width and height, so a square side in pixels, necessarily
//                                       a multiple of 8 in 0..120. Bits 9..11 are never read.
// RECORD, conditional 4-byte size block, present only when that square side is 0:
//   +0x08  u16  width
//   +0x0A  u16  height
// RECORD, fixed 8-byte tail (at +0x08 with an implicit square size, at +0x0C with an explicit one):
//   +0x00  s16  rotZ     -> the per-record spin about Z
//   +0x02  u16  NEVER READ by this function. Proven negative: the tail cursor goes 0 -> +4 -> +6
//                         -> +8 and there is no load at +2 anywhere in 0x80048F88..0x80049503.
//   +0x04  s16  scaleX   -> ScaleMatrix vx = scaleX + param_8
//   +0x06  s16  scaleY   -> ScaleMatrix vy = scaleY + param_9
// A record is therefore 16 bytes with an implicit square size and 20 bytes with an explicit one.
//
// THE PRIMITIVE. Every quad comes from slot 1 of the pool context g_PrimitivePoolContext, whose element size
// is 0x28 (g_PrimitiveSizeTable[1]) and whose entries InitializePrimitivePool @ 0x80057094 pre-tags with SetPolyFT4
// followed by SetSemiTrans(p, 1). The code byte is therefore 0x2E and this function never touches
// it: every sprite it draws is a SEMI-TRANSPARENT textured quad blended in the tpage's ABR mode.
// The tag word is left alone too - AddPrim rewrites its low 24 bits.
internal static class SpriteRenderer
{
    // GHIDRA: MATRIX_8007ad28 @ 0x8007AD28
    // Read-only .data. read-memory 0x8007AD28 x32 gives
    //   00 10 00 00 00 00 00 00 00 10 00 00 00 00 00 00 00 10 00 00 00 00 00 00 00 00 00 00 00 00 00 00
    // i.e. m = diag(0x1000, 0x1000, 0x1000), the identity at ONE = 4096, and t = (0, 0, 0).
    // find-cross-references returns exactly two references in the whole overlay, the SetTransMatrix
    // and SetRotMatrix pair below, both PARAM reads and neither a write.
    private static readonly LibGte.MATRIX MATRIX_8007ad28 = new()
    {
        m = new short[] { 0x1000, 0, 0, 0, 0x1000, 0, 0, 0, 0x1000 },
        t = new int[] { 0, 0, 0 },
    };

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: stands in for the raw pointer `p` when the slot-1 pool is not addressable in this
    // port. On the console p always points at real RAM. Here the pool only becomes addressable once
    // CreatePrimitivePools @ 0x80056DC0 has allocated it on the heap, and an unmapped address has no byte
    // buffer to give RotAverage4 its four sxy destinations. Pointing them here keeps the GTE work,
    // the control flow and the ordering-table test exactly as they are, and only the four projected
    // corners land somewhere nothing reads. This path does not exist on hardware.
    private static readonly byte[] s_unmappedPrimitive = new byte[0x28];

    // GHIDRA: DrawSpriteGroup @ 0x80048F88
    //
    // The eighteen arguments, each closed against a decoded instruction (o32: param_1..param_4
    // arrive in a0..a3, params 5..18 are read back from the caller's outgoing-argument area at
    // Stack[0x10] through Stack[0x44]):
    //   param_1   sprite-group PSX address
    //   param_2   model-space X of the group origin       (sign-extended at 0x8004907C)
    //   param_3   model-space Y
    //   param_4   model-space Z                           (sh v1,0x8c(sp) at 0x80049068 -> vz)
    //   param_5   packed flag/angle word, held in s7 for the whole body:
    //               bits 0..11  X rotation angle for the outer RotMatrix (andi 0x0fff @0x80049370)
    //               bit 12      NEVER TESTED here and masked off by that 0xfff
    //               bit 13      the position is already in view space: skip RotTrans and skip
    //                           loading MATRIX_8007ad28 (andi 0x2000 @0x80049044, @0x800490A8)
    //               bit 14      flip about X: local rotation vx = 0x800, i.e. 180 degrees at
    //                           0x1000 = 360 (andi 0x4000 @0x8004926C, ori 0x800 @0x80049274)
    //               bit 15      flip about Y: local rotation vy = 0x800 (@0x80049290, @0x80049298)
    //   param_6   Y rotation angle for the outer RotMatrix
    //   param_7   Z rotation angle for the outer RotMatrix
    //   param_8   X-SCALE BIAS added to each record's own scaleX
    //   param_9   Y-SCALE BIAS added to each record's own scaleY
    //   param_10  ORDERING-TABLE BIAS added to the bucket index before the range test and AddPrim.
    //             NOT folded into the return value.
    //   param_11  CLUT-id bias added to each record's clut (lhu @0x80049178, so unsigned despite
    //             Ghidra typing it short)
    //   param_12  TPage bias added to each record's tpage  (lhu @0x80049194)
    //   param_13  U bias (lbu @0x800491A8, unsigned despite Ghidra typing it char)
    //   param_14  V bias (lbu @0x800491B4)
    //   param_15  r0 of every POLY_FT4 built
    //   param_16  g0
    //   param_17  b0
    //   param_18  EXCLUSIVE LOWER BOUND on the OT bucket index, compared signed (slt @0x80049464).
    //             The title screen passes 0xFFFFE890 = -6000, so in practice there is no floor.
    //
    // THE RETURN VALUE has three cases and they are not interchangeable:
    //   0    the primitive pool is exhausted (the early exit at 0x80049074)
    //   -1   the record count was <= 0 so the loop never ran (s2 starts at -1, 0x80048FEC), OR the
    //        LAST record failed the ordering-table range test (addiu s2,zero,-1 @0x800494B0)
    //   else 0x800 minus the OTZ RotAverage4 produced for the LAST record that was added, WITHOUT
    //        param_10 folded back in (subu s2,v1,v0 @0x80049454, v1 = 0x800 from @0x8004944C)
    // UpdateTitleScreen turns that into a bucket exactly the way this function does - `iVar8 * 4 + 0x70`
    // then AddPrim(iVar8 + g_ActiveDrawEnvAddress, p) - which is direct confirmation it is an OT index.
    //
    // The local names are the decompiler's. Where Ghidra types a local `char` or `short` this port
    // keeps it as an int and truncates at the store instead: the MIPS body holds all of these in
    // 32-bit registers and only the sb/sh narrows them, and the intermediate `+ 0xFF80` in
    // particular is a ZERO-extended `ori t0,zero,0xff80` (0x80049160) rather than a sign extension.
    internal static int DrawSpriteGroup(int param_1, short param_2, short param_3, short param_4,
        ushort param_5, short param_6, short param_7, int param_8, int param_9, int param_10,
        short param_11, short param_12, byte param_13, byte param_14, byte param_15, byte param_16,
        byte param_17, int param_18)
    {
        LibGte.MATRIX MStack_110 = new();
        LibGte.MATRIX MStack_f0 = new();
        LibGte.MATRIX MStack_d0 = new();
        LibGte.SVECTOR local_b0 = new();
        LibGte.VECTOR local_a8 = new();

        int local_88 = param_8;
        int local_80 = param_9;
        int local_78 = param_10;
        int local_38 = param_18;
        short local_98 = param_6;
        short local_90 = param_7;
        uint uVar14 = 0;
        short local_70 = param_11;
        int iVar11 = -1;
        short local_68 = param_12;
        byte local_60 = param_13;
        byte local_58 = param_14;
        byte local_50 = param_15;
        byte local_48 = param_16;
        byte local_40 = param_17;
        if ((param_5 & 0x2000) == 0)
        {
            local_b0.vx = param_2;
            local_b0.vy = param_3;
            local_b0.vz = param_4;

            // JUSTIFICATION: C# language bridge only
            // RELATION: the original passes `&local_a8.pad` as RotTrans's (long *) flag argument -
            // the VECTOR's own fourth word. C# cannot take the address of a field, so the callee
            // writes a one-element array and the value is copied back into local_a8.pad, which is
            // where the original leaves it. Nothing in TITLE.EXE reads it.
            int[] local_a8_pad = new int[1];
            LibGte.RotTrans(local_b0, local_a8, local_a8_pad);
            local_a8.pad = local_a8_pad[0];
        }
        else
        {
            local_a8.vx = param_2;
            local_a8.vy = param_3;
            local_a8.vz = param_4;
        }

        LibGte.PushMatrix();
        if ((param_5 & 0x2000) == 0)
        {
            LibGte.SetTransMatrix(MATRIX_8007ad28);
            LibGte.SetRotMatrix(MATRIX_8007ad28);
        }

        LibGte.ReadRotMatrix(MStack_d0);
        int iVar15 = PsxRam.ReadI32(param_1);
        param_1 = param_1 + 4;
        int local_30 = PsxRam.ReadI32(PrimitivePools.g_PrimitivePoolContext + 0x44) * 0x28
            + PsxRam.ReadI32(PrimitivePools.g_PrimitivePoolContext + 4);
        if (0 < iVar15)
        {
            do
            {
                if ((uint)PsxRam.ReadI32(PrimitivePools.g_PrimitivePoolContext + 0x24)
                    <= (uint)PsxRam.ReadI32(PrimitivePools.g_PrimitivePoolContext + 0x44))
                {
                    // The pool is full. This return LEAKS the outer PushMatrix above, and that is a
                    // BUG OF THE ORIGINAL reproduced deliberately - rule 12 forbids repairing it.
                    // Closed by decoding the image at those four addresses: the branch at
                    // 0x80049128 targets 0x80049074, whose word is 0x08012534 = `j 0x800494D0` with
                    // 0x00001021 = `addu v0,zero,zero` in its delay slot; 0x800494C4 is the
                    // `jal 0x8006D314` PopMatrix and 0x800494D0 is `lw ra,0x134(sp)`, the register
                    // restore. So the jump lands PAST the outer PopMatrix and the GTE matrix stack
                    // is left one entry deep.
                    // PARTIAL: this port's PushMatrix throws on overflow where the console silently
                    // wraps its own small stack, so a run that took this path repeatedly would fault
                    // rather than corrupt the matrix. Whether the path ever fires is NOT established:
                    // it depends on the slot-1 capacity CreatePrimitivePools @ 0x80056DC0 is called with, and
                    // that caller has not been traced.
                    return 0;
                }

                int iVar5 = (int)(uVar14 & 0xffff) * 0x28 + local_30;
                PsxRam.WriteU8(iVar5 + 4, local_50);
                int piVar9 = param_1 + 8;
                PsxRam.WriteU8(iVar5 + 5, local_48);
                PsxRam.WriteU8(iVar5 + 6, local_40);
                iVar11 = PsxRam.ReadU8(param_1);
                int cVar7 = PsxRam.ReadU8(param_1 + 1);
                int sVar13 = PsxRam.ReadU8(param_1 + 2) + 0xff80;
                int sVar12 = PsxRam.ReadU8(param_1 + 3) + 0xff80;
                PsxRam.WriteU16(iVar5 + 0xe, (ushort)(local_70 + PsxRam.ReadU16(param_1 + 4)));

                // One `lhu v1,0x6(t1)` at 0x80049190 feeds both the shift and the mask; Ghidra
                // prints the load twice.
                ushort uVar1b = PsxRam.ReadU16(param_1 + 6);
                ushort uVar1 = (ushort)(uVar1b >> 9);
                PsxRam.WriteU16(iVar5 + 0x16, (ushort)(local_68 + (uVar1b & 0x1ff)));
                ushort uVar8 = (ushort)(uVar1 & 0x78);
                int cVar6 = local_60 + iVar11;
                cVar7 = local_58 + cVar7;
                ushort uVar10 = uVar8;
                if ((uVar1 & 0x78) == 0)
                {
                    uVar8 = PsxRam.ReadU16(param_1 + 10);
                    piVar9 = param_1 + 12;
                    uVar10 = PsxRam.ReadU16(param_1 + 8);
                }

                int cVar4 = uVar10 + cVar6 + -1;
                int cVar3 = uVar8 + cVar7 + -1;
                iVar11 = (int)(uVar14 & 0xffff) * 0x28 + local_30;

                // The standard PSX inclusive-edge UV convention. All four pairs are `sb`, so the
                // + width - 1 / + height - 1 arithmetic WRAPS AT 8 BITS: a 256-wide sprite gives
                // u1 = u0 + 255 mod 256 = u0 - 1.
                PsxRam.WriteU8(iVar11 + 0xc, (byte)cVar6);
                PsxRam.WriteU8(iVar11 + 0xd, (byte)cVar7);
                PsxRam.WriteU8(iVar11 + 0x14, (byte)cVar4);
                PsxRam.WriteU8(iVar11 + 0x15, (byte)cVar7);
                PsxRam.WriteU8(iVar11 + 0x1c, (byte)cVar6);
                PsxRam.WriteU8(iVar11 + 0x1d, (byte)cVar3);
                PsxRam.WriteU8(iVar11 + 0x24, (byte)cVar4);
                PsxRam.WriteU8(iVar11 + 0x25, (byte)cVar3);
                GteScratch.SVECTOR_1f800058.vz = (short)PsxRam.ReadU16(piVar9);

                // `lh` at 0x80049230 and 0x80049244 - both SIGNED halfword loads.
                GteScratch.VECTOR_1f800060.vx = (short)PsxRam.ReadU16(piVar9 + 4) + local_88;
                GteScratch.VECTOR_1f800060.vz = 0x1000;
                GteScratch.VECTOR_1f800060.vy = (short)PsxRam.ReadU16(piVar9 + 6) + local_80;
                param_1 = piVar9 + 8;
                LibGte.PushMatrix();
                if ((param_5 & 0x4000) == 0)
                {
                    GteScratch.SVECTOR_1f800058.vx = 0;
                }
                else
                {
                    GteScratch.SVECTOR_1f800058.vx = 0x800;
                }

                if ((param_5 & 0x8000) == 0)
                {
                    GteScratch.SVECTOR_1f800058.vy = 0;
                }
                else
                {
                    GteScratch.SVECTOR_1f800058.vy = 0x800;
                }

                GteScratch.SVECTOR_1f800028.vx = (short)(sVar13 + uVar10);
                GteScratch.SVECTOR_1f800030.vy = (short)(sVar12 + uVar8);
                GteScratch.SVECTOR_1f800020.vz = 0;
                GteScratch.SVECTOR_1f800028.vz = 0;
                GteScratch.SVECTOR_1f800030.vz = 0;
                GteScratch.SVECTOR_1f800038.vz = 0;
                GteScratch.SVECTOR_1f800020.vx = (short)sVar13;
                GteScratch.SVECTOR_1f800020.vy = (short)sVar12;
                GteScratch.SVECTOR_1f800028.vy = (short)sVar12;
                GteScratch.SVECTOR_1f800030.vx = (short)sVar13;
                GteScratch.SVECTOR_1f800038.vx = GteScratch.SVECTOR_1f800028.vx;
                GteScratch.SVECTOR_1f800038.vy = GteScratch.SVECTOR_1f800030.vy;
                LibGte.RotMatrix(GteScratch.SVECTOR_1f800058, MStack_f0);
                GteScratch.VECTOR_1f800048.vx = 0;
                GteScratch.VECTOR_1f800048.vy = 0;
                GteScratch.VECTOR_1f800048.vz = 0;
                LibGte.TransMatrix(MStack_f0, GteScratch.VECTOR_1f800048);
                GteScratch.SVECTOR_1f800058.vy = local_98;
                GteScratch.SVECTOR_1f800058.vx = (short)(param_5 & 0xfff);
                GteScratch.SVECTOR_1f800058.vz = local_90;
                LibGte.RotMatrix(GteScratch.SVECTOR_1f800058, GteScratch.MATRIX_1f800000);
                LibGte.TransMatrix(GteScratch.MATRIX_1f800000, local_a8);
                LibGte.ScaleMatrix(GteScratch.MATRIX_1f800000, GteScratch.VECTOR_1f800060);

                // The flip matrix is composed as m1 of the first CompMatrix and its translation was
                // just nulled, so the 180-degree flips turn the quad about its own local origin, not
                // about the group origin.
                LibGte.CompMatrix(GteScratch.MATRIX_1f800000, MStack_f0, MStack_110);
                LibGte.CompMatrix(MStack_d0, MStack_110, GteScratch.MATRIX_1f800000);
                LibGte.SetTransMatrix(GteScratch.MATRIX_1f800000);
                LibGte.SetRotMatrix(GteScratch.MATRIX_1f800000);
                int p = local_30 + (int)(uVar14 & 0xffff) * 0x28;

                // JUSTIFICATION: C# language bridge only
                // RELATION: RotAverage4's four sxy destinations are `(long *)((int)p + 8)` and its
                // three neighbours - words INSIDE the POLY_FT4 packet - so the SDK entry point takes
                // the packet's byte buffer plus four offsets. The original's raw pointer has to be
                // split into that pair here.
                if (!LibGpu.RamResolve(p, out byte[] pBuf, out int pOff))
                {
                    // PARTIAL: see s_unmappedPrimitive above. Unreachable on hardware.
                    pBuf = s_unmappedPrimitive;
                    pOff = 0;
                }

                int lVar2 = LibGte.RotAverage4(
                    GteScratch.SVECTOR_1f800020, GteScratch.SVECTOR_1f800028,
                    GteScratch.SVECTOR_1f800030, GteScratch.SVECTOR_1f800038,
                    pBuf, pOff + 8, pOff + 0x10, pOff + 0x18, pOff + 0x20,
                    GteScratch.DAT_1f800074, GteScratch.DAT_1f800078);
                iVar11 = 0x800 - lVar2;
                iVar5 = iVar11 + local_78;

                // Both compares are SIGNED: `slt v0,t2,a0` at 0x80049464 and `slti v0,a0,0x800` at
                // 0x8004946C. The depth axis is inverted against OTZ - a nearer quad, with a larger
                // OTZ, gets a smaller bucket index - and 0x800 is the ordering table's own length.
                if ((local_38 < iVar5) && (iVar5 < 0x800))
                {
                    LibGpu.AddPrim(iVar5 * 4 + 0x70 + FrameLoop.g_ActiveDrawEnvAddress, p);
                    uVar14 = uVar14 + 1;
                    PsxRam.WriteI32(PrimitivePools.g_PrimitivePoolContext + 0x44,
                        PsxRam.ReadI32(PrimitivePools.g_PrimitivePoolContext + 0x44) + 1);
                }
                else
                {
                    iVar11 = -1;
                }

                iVar15 = iVar15 + -1;
                LibGte.PopMatrix();
            }
            while (0 < iVar15);
        }

        LibGte.PopMatrix();
        return iVar11;
    }
}
