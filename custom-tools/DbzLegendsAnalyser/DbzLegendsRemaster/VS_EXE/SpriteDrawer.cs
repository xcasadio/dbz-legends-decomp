using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE SPRITE DRAWER — one function, 136 incoming references, and until now an empty stub.
//
// DrawSpriteGroup is how VS.EXE puts anything on the screen that is not the battle HUD. Its five
// callers between them account for every sprite in the mode: FUN_80022AB0 (7 call sites),
// FUN_80023314 (6), FighterMotion.FUN_800477EC and FUN_80047A24 (the fighter's own body and its
// shadow), and FighterCombat.UpdateAttackEventTask. Ghidra counts 136 references to the address in
// total. While it did nothing, the port could run the whole battle machine and still show a bare
// clear colour.
//
// WHAT IT DOES, per sprite record:
//   * transform the caller's position through the GTE, or take it as-is when param_5 bit 13 is set;
//   * take one 0x28-byte POLY_FT4 out of the slot-1 primitive pool;
//   * fill its colour, its clut and tpage, and its four UV pairs from the record;
//   * build a matrix from the record's own scale and the caller's rotation, compose it with the
//     rotation the GTE already holds, and RotAverage4 the four corners into the packet;
//   * AddPrim it into the ordering table at `0x800 - otz + param_10`, if that lands inside the
//     table.
// The return is `0x800 - otz` for the LAST quad added, or -1. That is NOT the bucket AddPrim
// used: the bucket is `0x800 - otz + param_10` (built into $a0 at 0x80053284), while the
// returned value is $s2, set one instruction earlier at 0x80053280 without the bias. The
// two differ by param_10 whenever a caller passes a non-zero depth offset.
//
// THIS IS THE RELINKED TWIN of TITLE.EXE's DrawSpriteGroup @ 0x80048F88, which
// TITLE_EXE/SpriteRenderer.cs already ports. The two decompile to the same shape and the same
// scratchpad offsets, which is a cross-check rather than a shortcut: every line below was read from
// VS.EXE's own decompilation, and where the two overlays differ the VS one wins. They differ in
// exactly one visible way -- TITLE reaches its ordering table through FrameLoop's own
// g_ActiveDrawEnvAddress, VS through DAT_8008D420, which this port already declares.
//
// THE POOL-FULL RETURN LEAKS THE OUTER PushMatrix, in both overlays, and it is the original's bug:
// the early `return 0` jumps past the outer PopMatrix and leaves the GTE matrix stack one entry
// deep. Reproduced under rule 12, with the same caveat TITLE_EXE/SpriteRenderer.cs already records
// -- this port's PushMatrix throws on overflow where the console silently wraps its own small
// stack, so a run that took this path repeatedly would fault rather than corrupt the matrix.
internal static class SpriteDrawer
{
    // GHIDRA: DAT_80084c84 @ 0x80084C84 (VS.EXE)
    // A MATRIX in initialised .data, read back from the image and IDENTITY: m = 0x1000, 0, 0, 0,
    // 0x1000, 0, 0, 0, 0x1000 with t = 0, 0, 0. DrawSpriteGroup is the only reader, and it loads it
    // into BOTH the rotation and the translation register when the caller has NOT asked for the
    // raw-coordinate path -- which is how the sprite's own transform starts from a clean slate
    // rather than from whatever the previous drawer left in the GTE.
    private static readonly LibGte.MATRIX MATRIX_80084c84 = new()
    {
        m = { [0] = 0x1000, [4] = 0x1000, [8] = 0x1000 },
    };

    // JUSTIFICATION: backend MonoGame only
    // RELATION: diagnostic probes, read only by Validation/VsBootDiagnostic.cs. Nothing in the
    // transliterated runtime touches them. "Called" and "submitted" are different questions: a
    // drawer that runs but whose every quad falls outside the ordering table draws nothing, and
    // from a screenshot the two look identical.
    internal static int DiagCalls;

    internal static int DiagQuadsSubmitted;

    internal static int DiagPoolFull;

    internal static int DiagUnresolvedPacket;

    // JUSTIFICATION: C# language bridge only
    // RELATION: RotAverage4 writes its four screen-coordinate results INTO the packet, so the SDK
    // entry point takes the packet's own byte buffer plus four offsets rather than a pointer. When
    // the packet address does not resolve to a modelled region the call still has to go somewhere;
    // it goes here, and nothing reads it back. Same shape and same reason as
    // TITLE_EXE/SpriteRenderer.cs's own s_unmappedPrimitive.
    private static readonly byte[] s_unmappedPrimitive = new byte[0x28];

    // GHIDRA: DrawSpriteGroup @ 0x80052DB4 (VS.EXE)
    // 1404 bytes, twelve callees, all of them libgte or libgpu. MOVED HERE from FighterCombat.cs,
    // which carried it as an eighteen-parameter empty stub; that file no longer declares the
    // address and its one call site is qualified.
    //
    // THE PARAMETERS KEEP GHIDRA'S OWN TYPES rather than the stub's uniform `int`, because the
    // narrowing is observable: param_2..param_4 are `short` and param_13/param_14 `char`, so a
    // caller passing 0x10000 in a position or 0x180 in a UV bias gets the truncated value on the
    // console. The three call sites in the port pass explicit casts for that reason.
    //
    // param_5 IS A FLAG WORD AND AN ANGLE AT ONCE: bits 0..11 are the Z rotation handed to
    // RotMatrix, bit 13 selects raw coordinates over a GTE transform, and bits 14 and 15 are the
    // horizontal and vertical flips, each contributing half a turn (0x800) to its own axis.
    internal static int DrawSpriteGroup(int param_1, short param_2, short param_3, short param_4,
        ushort param_5, short param_6, short param_7, int param_8, int param_9, int param_10,
        short param_11, short param_12, sbyte param_13, sbyte param_14, byte param_15,
        byte param_16, byte param_17, int param_18)
    {
        DiagCalls++;

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
        sbyte local_60 = param_13;
        sbyte local_58 = param_14;
        byte local_50 = param_15;
        byte local_48 = param_16;
        byte local_40 = param_17;

        if ((param_5 & 0x2000) == 0)
        {
            local_b0.vx = param_2;
            local_b0.vy = param_3;
            local_b0.vz = param_4;

            // JUSTIFICATION: C# language bridge only
            // RELATION: the original passes `&local_a8.pad` as RotTrans's flag sink -- the VECTOR's
            // own fourth word. C# cannot take the address of a field, so the call gets a throwaway
            // one-element array, exactly as FighterTask.FUN_8004FBFC already does for the same
            // callee. Nothing reads it back here either.
            LibGte.RotTrans(local_b0, local_a8, new int[1]);
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
            LibGte.SetTransMatrix(MATRIX_80084c84);
            LibGte.SetRotMatrix(MATRIX_80084c84);
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
                    // The pool is full. See this file's header: this return LEAKS the outer
                    // PushMatrix, and that is the original's own bug, not a transcription slip.
                    DiagPoolFull++;
                    return 0;
                }

                int iVar5 = (int)(uVar14 & 0xffff) * 0x28 + local_30;
                PsxRam.WriteU8(iVar5 + 4, local_50);
                int piVar9 = param_1 + 8;
                PsxRam.WriteU8(iVar5 + 5, local_48);
                PsxRam.WriteU8(iVar5 + 6, local_40);
                iVar11 = PsxRam.ReadU8(param_1);
                int cVar7 = (sbyte)PsxRam.ReadU8(param_1 + 1);
                int sVar13 = PsxRam.ReadU8(param_1 + 2) - 0x80;
                int sVar12 = PsxRam.ReadU8(param_1 + 3) - 0x80;
                PsxRam.WriteU16(iVar5 + 0xe, (ushort)(local_70 + (short)PsxRam.ReadU16(param_1 + 4)));

                // One halfword load feeds both the shift and the mask; the decompiler prints it
                // twice.
                ushort uVar1b = PsxRam.ReadU16(param_1 + 6);
                ushort uVar1 = (ushort)(uVar1b >> 9);
                PsxRam.WriteU16(iVar5 + 0x16, (ushort)(local_68 + (uVar1b & 0x1ff)));
                ushort uVar8 = (ushort)(uVar1 & 0x78);
                int cVar6 = local_60 + (sbyte)iVar11;
                cVar7 = local_58 + cVar7;
                ushort uVar10 = uVar8;

                // A ZERO SIZE IN THE TPAGE HALFWORD MEANS "the size is a whole halfword each",
                // which is also what makes the record two words longer.
                if ((uVar1 & 0x78) == 0)
                {
                    uVar8 = PsxRam.ReadU16(param_1 + 10);
                    piVar9 = param_1 + 12;
                    uVar10 = PsxRam.ReadU16(param_1 + 8);
                }

                int cVar4 = uVar10 + cVar6 + -1;
                int cVar3 = uVar8 + cVar7 + -1;
                iVar11 = (int)(uVar14 & 0xffff) * 0x28 + local_30;

                // The standard PSX inclusive-edge UV convention. All four pairs are byte stores, so
                // the `+ width - 1` arithmetic WRAPS AT 8 BITS: a 256-wide sprite gives
                // u1 = u0 + 255 mod 256 = u0 - 1.
                PsxRam.WriteU8(iVar11 + 0xc, (byte)cVar6);
                PsxRam.WriteU8(iVar11 + 0xd, (byte)cVar7);
                PsxRam.WriteU8(iVar11 + 0x14, (byte)cVar4);
                PsxRam.WriteU8(iVar11 + 0x15, (byte)cVar7);
                PsxRam.WriteU8(iVar11 + 0x1c, (byte)cVar6);
                PsxRam.WriteU8(iVar11 + 0x1d, (byte)cVar3);
                PsxRam.WriteU8(iVar11 + 0x24, (byte)cVar4);
                PsxRam.WriteU8(iVar11 + 0x25, (byte)cVar3);

                VS_EXE_exe.SVECTOR_1f800058.vz = (short)PsxRam.ReadU16(piVar9);
                VS_EXE_exe.VECTOR_1f800060.vx = (short)PsxRam.ReadU16(piVar9 + 4) + local_88;
                VS_EXE_exe.VECTOR_1f800060.vz = 0x1000;
                VS_EXE_exe.VECTOR_1f800060.vy = (short)PsxRam.ReadU16(piVar9 + 6) + local_80;
                param_1 = piVar9 + 8;

                LibGte.PushMatrix();
                VS_EXE_exe.SVECTOR_1f800058.vx = (param_5 & 0x4000) == 0 ? (short)0 : (short)0x800;
                VS_EXE_exe.SVECTOR_1f800058.vy = (param_5 & 0x8000) == 0 ? (short)0 : (short)0x800;

                VS_EXE_exe.SVECTOR_1f800028.vx = (short)(sVar13 + uVar10);
                VS_EXE_exe.SVECTOR_1f800030.vy = (short)(sVar12 + uVar8);
                VS_EXE_exe.SVECTOR_1f800020.vz = 0;
                VS_EXE_exe.SVECTOR_1f800028.vz = 0;
                VS_EXE_exe.SVECTOR_1f800030.vz = 0;
                VS_EXE_exe.SVECTOR_1f800038.vz = 0;
                VS_EXE_exe.SVECTOR_1f800020.vx = (short)sVar13;
                VS_EXE_exe.SVECTOR_1f800020.vy = (short)sVar12;
                VS_EXE_exe.SVECTOR_1f800028.vy = (short)sVar12;
                VS_EXE_exe.SVECTOR_1f800030.vx = (short)sVar13;
                VS_EXE_exe.SVECTOR_1f800038.vx = VS_EXE_exe.SVECTOR_1f800028.vx;
                VS_EXE_exe.SVECTOR_1f800038.vy = VS_EXE_exe.SVECTOR_1f800030.vy;

                LibGte.RotMatrix(VS_EXE_exe.SVECTOR_1f800058, MStack_f0);
                VS_EXE_exe.VECTOR_1f800048.vx = 0;
                VS_EXE_exe.VECTOR_1f800048.vy = 0;
                VS_EXE_exe.VECTOR_1f800048.vz = 0;
                LibGte.TransMatrix(MStack_f0, VS_EXE_exe.VECTOR_1f800048);

                VS_EXE_exe.SVECTOR_1f800058.vy = local_98;
                VS_EXE_exe.SVECTOR_1f800058.vx = (short)(param_5 & 0xfff);
                VS_EXE_exe.SVECTOR_1f800058.vz = local_90;
                LibGte.RotMatrix(VS_EXE_exe.SVECTOR_1f800058, Scratchpad.MATRIX_1f800000);
                LibGte.TransMatrix(Scratchpad.MATRIX_1f800000, local_a8);
                LibGte.ScaleMatrix(Scratchpad.MATRIX_1f800000, VS_EXE_exe.VECTOR_1f800060);

                // The flip matrix is composed as m1 of the first CompMatrix and its translation was
                // just nulled, so the half-turn flips turn the quad about its own local origin, not
                // about the group origin.
                LibGte.CompMatrix(Scratchpad.MATRIX_1f800000, MStack_f0, MStack_110);
                LibGte.CompMatrix(MStack_d0, MStack_110, Scratchpad.MATRIX_1f800000);
                LibGte.SetTransMatrix(Scratchpad.MATRIX_1f800000);
                LibGte.SetRotMatrix(Scratchpad.MATRIX_1f800000);

                int p = local_30 + (int)(uVar14 & 0xffff) * 0x28;
                if (!LibGpu.RamResolve(p, out byte[] pBuf, out int pOff))
                {
                    // PARTIAL: see s_unmappedPrimitive above. Unreachable while the pool lives in a
                    // modelled region.
                    DiagUnresolvedPacket++;
                    pBuf = s_unmappedPrimitive;
                    pOff = 0;
                }

                int lVar2 = LibGte.RotAverage4(
                    VS_EXE_exe.SVECTOR_1f800020, VS_EXE_exe.SVECTOR_1f800028,
                    VS_EXE_exe.SVECTOR_1f800030, VS_EXE_exe.SVECTOR_1f800038,
                    pBuf, pOff + 8, pOff + 0x10, pOff + 0x18, pOff + 0x20,
                    VS_EXE_exe.DAT_1f800074, VS_EXE_exe.DAT_1f800078);

                iVar11 = 0x800 - lVar2;
                iVar5 = iVar11 + local_78;

                // Both compares are SIGNED. The depth axis is inverted against OTZ -- a nearer quad,
                // with a larger OTZ, gets a smaller bucket index -- and 0x800 is the ordering
                // table's own length.
                if (local_38 < iVar5 && iVar5 < 0x800)
                {
                    LibGpu.AddPrim(iVar5 * 4 + 0x70 + VS_EXE_exe.DAT_8008d420, p);
                    DiagQuadsSubmitted++;
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
            } while (0 < iVar15);
        }

        LibGte.PopMatrix();
        return iVar11;
    }
}
