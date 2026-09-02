using PsxSdkMonogame;
using static PsxSdkMonogame.LibGte;

namespace DbzLegendsRemaster.VS_EXE;

// The control-flow and memory-access opcodes of VS.EXE's animation-script VM.
//
// THE VM. The interpreter is ExecuteAnimStreamBatch @ 0x80036768. Its whole engine is four
// instructions plus a branch, and the bytes at 0x80036840 say it exactly:
//
//   0x80036840  lhu   v0, 0(a0)        ; v0 = *streamPtr
//   0x80036848  sh    v0, 0x10(sp)
//   0x8003684c  andi  v0, v0, 0xff     ; opcode = low byte of the first halfword
//   0x80036850  sll   v0, v0, 2
//   0x80036854  addu  v0, v0, s6       ; s6 = &g_animStreamDispatchTable
//   0x80036858  lw    v0, 0(v0)
//   0x80036860  jalr  v0               ; call it through v0
//   0x80036864  sra   a1, s0, 0x10     ; delay slot: a1 = mesh index
//   0x80036868  addu  a0, v0, zero     ; a0 = RETURN VALUE
//   0x8003686c  lh    v0, 0(a0)
//   0x80036874  bne   v0, zero, 0x80036840
//
// So: the opcode is the low byte of the first halfword, every handler RETURNS THE ADDRESS OF THE
// NEXT COMMAND, and the stream stops on a zero halfword. A threaded interpreter.
//
// THE HANDLER ABI is `ushort *handler(ushort *streamPtr, int meshIndex)`. Most handlers ignore the
// second argument and Ghidra then prints a one-parameter prototype; the C# signatures below follow
// what Ghidra prints for each function, one at a time, rather than imposing a uniform shape the
// original does not have. The stream itself is raw PSX memory walked by pointer, so it is modelled
// here as an `int` address read through `PsxRam.ReadU16`, never as a copied `ushort[]` — the
// interpreter re-reads the returned address, so an address is what a handler must return.
//
// THE BINARY NAMES ITS OWN OPCODES. A table of fifty 16-byte ASCII names sits at 0x800823C0, one
// per dispatch slot, and it is the only naming vein in the program. Where Ghidra already carries a
// symbol, the `// GHIDRA:` line spells THAT symbol, and the image's opcode name is quoted in the
// line below it. Where Ghidra carries nothing — five of the eleven functions here were never
// promoted to functions at all — the annotation says so instead of inventing a name.
//
// ONE NAME DISCREPANCY, ARBITRATED. Opcode 15 @ 0x80038ED4 is `cmp_set` in the image and
// `AnimCmd_ConditionalBranch` in Ghidra. THE IMAGE IS RIGHT AND GHIDRA IS WRONG: the function has
// exactly one `return` value, `streamPtr + 4`, on every path including the early-out at 0x80038F14
// — it never branches the stream. What it does is compare two entries of g_animSharedVarTable and
// OR a caller-supplied bit mask into a third. That is a compare-and-set, not a branch. The C# name
// below follows the image; the `// GHIDRA:` line still spells the symbol Ghidra carries, because
// that is what a reader will find in the database.
//
// OWNERSHIP CAVEAT, stated rather than hidden. Nine of the eleven functions read AnimVm.DAT_800b305a, and
// four of them read or write the mesh buffers at 0x801FA780-0x801FAA64. Those globals are shared
// with ExecuteAnimStreamBatch and with the other forty handlers, none of which is transliterated
// yet. They are declared here because this file is the first VS.EXE code to need them. When the
// interpreter itself lands, AnimVm.DAT_800b305a, g_meshStreamPtrBuffer, g_meshOffsetBuffer and
// g_animSharedVarTable belong with it, moved as they are — a second `internal static` copy of any
// of them in a sibling handler file would be a second piece of storage, not a second view of the
// same one.
internal static class AnimCmdControl
{
    // ==== Globals =============================================================================

    // AnimVm.DAT_800b305a, g_animSharedVarTable, g_meshStreamPtrBuffer, g_meshOffsetBuffer,
    // g_meshXOffsetBuffer, g_renderMetadataBuffer, g_meshCountBuffer and DAT_801f2180 are the VM's
    // SHARED globals; they are declared once in AnimVm.cs and reached here as AnimVm.<name>, by
    // address through PsxRam rather than as a managed array. See AnimVm.cs for the merged proof
    // comments — the per-symbol notes on extent and signedness this file had written (256 entries
    // for g_animSharedVarTable from if_set's zero-extended-byte index; the signed-byte spelling
    // in cmp_set / objint_get / objlong_get that would alias g_renderFlushFlag and
    // g_meshOffsetBuffer on a negative index; 64-entry lockstep between g_meshXOffsetBuffer,
    // g_renderMetadataBuffer and g_meshCountBuffer; 256-record DAT_801f2180) are folded into
    // AnimVm's g_animSharedVarTable comment.

    // The transform records at 0x801F2180 (AnimVm.DAT_801f2180), stride 0x20 bytes. The original
    // addresses four halfwords inside each record through four separate Ghidra labels; they are
    // four fields of one record, so they are four halfword-index constants here. Every one keeps
    // its own annotation.

    // GHIDRA: DAT_801f2180 @ 0x801F2180 (VS.EXE) — halfword 0 of each record.
    // Halfword-index units: usage sites index AnimVm.DAT_801f2180 through PsxRam.ReadU16/WriteU16
    // as `AnimVm.DAT_801f2180 + (index) * 2`, so this constant stays a short-count, not a byte
    // count, exactly as it was when it indexed a managed short[].
    private const int Field_801f2180 = 0;

    // GHIDRA: DAT_801f2188 @ 0x801F2188 (VS.EXE) — halfword 4 of each record (+0x08 bytes).
    private const int Field_801f2188 = 4;

    // GHIDRA: DAT_801f2190 @ 0x801F2190 (VS.EXE) — halfword 8 of each record (+0x10 bytes).
    private const int Field_801f2190 = 8;

    // GHIDRA: DAT_801f2198 @ 0x801F2198 (VS.EXE) — halfword 12 of each record (+0x18 bytes).
    private const int Field_801f2198 = 12;

    // GHIDRA: g_cdFileBufferTable @ 0x801D2000 (VS.EXE)
    // Only the ADDRESS is declared here; the storage is FileIo.g_cdFileBufferTable, which owns it.
    // bit_chk reads a 32-bit word out of it — `(&g_cdFileBufferTable)[(short)uVar1]` on a symbol
    // Ghidra types `undefined4` — and uses that word as a stream address. It resolves through
    // PsxRam once a VS.EXE-wide ResolveAddress chains FileIo.Resolve, which FileIo.cs already
    // states is where that chaining belongs.
    private const int g_cdFileBufferTableAddress = unchecked((int)0x801D2000);

    // ==== Handlers ============================================================================

    // GHIDRA: no symbol @ 0x80037374 (VS.EXE) — Ghidra has not promoted this address to a
    //         function; the only three references to it are dispatch-table slots.
    // Opcodes 0, 4 AND 36, which the image's name table calls `dummy` at all three indices. One
    // address, three slots: 0x800822F4, 0x80082304 and 0x80082384 all hold 0x80037374. Written
    // once here, as the original is written once.
    //
    // THE BODY IS TWO INSTRUCTIONS AND IT DOES NOT SET v0:
    //     0x80037374  jr   ra
    //     0x80037378  nop
    // so the "next command address" the interpreter reads back is whatever the ABI left in v0.
    // That is not undefined here, because there is exactly one caller and it calls through v0:
    // ExecuteAnimStreamBatch loads the handler address into v0 at 0x80036858, `jalr v0` at
    // 0x80036860, and `addu a0, v0, zero` at 0x80036868. v0 still holds the handler's own address
    // on return, so the loop resumes AT 0x80037374 and reads `lh v0, 0(0x80037374)` = 0x0008, the
    // low halfword of the `jr ra` encoding — non-zero, so the loop keeps going and dispatches
    // opcode 8 on this function's own instruction bytes.
    //
    // The constant returned below is that address, and it is a property of the call site, not of
    // this function. Rule 12: this is not corrected into a `streamPtr + 2`. An opcode 0, 4 or 36
    // in a real stream runs the interpreter off into code.
    //
    // No parameters, because the two instructions read none — Ghidra prints `void (void)`.
    internal static int AnimCmd_Dummy()
    {
        return unchecked((int)0x80037374);
    }

    // GHIDRA: no symbol @ 0x8003737C (VS.EXE) — never promoted to a function; the only reference
    //         is dispatch-table slot 0x800822F8.
    // Opcode 1, which the image's name table calls `nop_set`.
    //
    // The eight instructions, decoded:
    //     lhu v1,0(a0) / addiu a0,a0,2 / lui v0,0x800b / lhu v0,0x305a(v0)
    //     sll v1,v1,0x10 / sra v1,v1,0x18 / jr ra / addu v0,a0,zero
    // Two of those results are thrown away. v1 ends up holding the sign-extended high byte of the
    // command, which no caller reads — Ghidra models it as the high half of an `undefined8` return,
    // which is a decompiler artefact of an unused $v1, not a second return value. And the
    // AnimVm.DAT_800b305a load at 0x80037388 is overwritten by the delay slot before `jr ra` retires, so
    // it is a dead read of RAM with no observable effect. Neither is reproduced; both are recorded
    // here rather than silently dropped.
    //
    // What is left is the whole of the function: advance one halfword.
    internal static int AnimCmd_NopSet(int param_1)
    {
        return param_1 + 2;
    }

    // GHIDRA: AnimCmd_ConditionalBranch @ 0x80038ED4 (VS.EXE)
    // Opcode 15, which the image's name table calls `cmp_set`. SEE THE DISCREPANCY NOTE IN THE
    // FILE HEADER: the image's name is the one the body supports. Every path returns
    // `streamPtr + 4` and nothing here touches the stream pointer, so no branch is taken; the
    // function compares two variables and ORs a mask into a third. The C# name follows `cmp_set`.
    //
    // Four halfwords: [0] opcode + comparison selector, [1] two 4-bit variable indices in the low
    // byte and the destination variable in the high byte, [2] the mask to OR in, [3] a signed
    // addend applied to the right-hand side.
    internal static int AnimCmd_CmpSet(int streamPtr)
    {
        ushort uVar1;
        ushort uVar2;
        ushort uVar3;
        ushort uVar4;
        ushort uVar5;
        short sVar6;
        int iVar7 = 0;
        int iVar8 = 0;
        ushort uStack_66;

        uVar1 = PsxRam.ReadU16(streamPtr + 2);
        uVar5 = (ushort)(uVar1 & 0xf);
        sVar6 = (short)((uVar1 & 0xf0) >> 4);
        uVar2 = PsxRam.ReadU16(streamPtr + 4);
        uStack_66 = 0;
        uVar3 = PsxRam.ReadU16(streamPtr + 6);
        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            return streamPtr + 8;
        }

        uVar4 = uStack_66;
        switch ((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18)
        {
            case 0:
                uVar4 = uVar2;
                if ((short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + ((short)uVar5) * 2) !=
                    (short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (sVar6) * 2) + (short)uVar3)
                {
                    goto LAB_80039104;
                }

                break;
            case 1:
                uVar4 = uVar2;
                if ((short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + ((short)uVar5) * 2) ==
                    (short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (sVar6) * 2) + (short)uVar3)
                {
                    goto LAB_80039104;
                }

                break;
            case 2:
                uVar4 = uVar2;
                if ((short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + ((short)uVar5) * 2) <=
                    (short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (sVar6) * 2) + (short)uVar3)
                {
                    goto LAB_80039104;
                }

                break;
            case 3:
                iVar7 = (short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + ((short)uVar5) * 2);
                iVar8 = (short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (sVar6) * 2) + (short)uVar3;
                goto LAB_800390e8;
            case 4:
                uVar4 = uVar2;
                if ((short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (sVar6) * 2) + (short)uVar3 <=
                    (short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + ((short)uVar5) * 2))
                {
                    goto LAB_80039104;
                }

                break;
            case 5:
                iVar8 = (short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + ((short)uVar5) * 2);
                iVar7 = (short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (sVar6) * 2) + (short)uVar3;
                goto LAB_800390e8;
        }

        goto LAB_afterSwitch;

        // The original reaches this tail two ways: case 3 falls through into it, and case 5 runs
        // into it. C# forbids a `goto` INTO a switch section, so the shared tail is written once
        // outside the switch and both cases jump to it. Nothing else moved: the case order, the
        // operand order (note that 3 and 5 assign iVar7/iVar8 the opposite way round) and the
        // comparison are the original's.
    LAB_800390e8:
        uVar4 = uVar2;
        if (iVar7 < iVar8)
        {
            goto LAB_80039104;
        }

    LAB_afterSwitch:
        uStack_66 = uVar4;
    LAB_80039104:
        PsxRam.WriteU16(AnimVm.g_animSharedVarTable + ((short)(sbyte)(uVar1 >> 8)) * 2, (ushort)(PsxRam.ReadU16(AnimVm.g_animSharedVarTable + ((short)(sbyte)(uVar1 >> 8)) * 2) | uStack_66));
        return streamPtr + 8;
    }

    // GHIDRA: AnimCmd_PartsLink @ 0x80039DF0 (VS.EXE)
    // Opcode 17, which the image's name table calls `parts_link`. Ghidra's pre-comment on the
    // function reads: "CERTAIN: homologous to GAME.EXE AnimCmd_PartsLink. Reads
    // g_renderMetadataBuffer, g_meshCountBuffer, and g_meshXOffsetBuffer to propagate linked values
    // across rendered primitive ranges."
    //
    // Two halfwords: [0] opcode + group tag in the high byte, [1] part number in the low byte and
    // a repeat count in the high byte.
    //
    // THE OUTER LOOP HAS NO EXIT except the `sStack_14 == 1` return. If the requested part is not
    // in the metadata buffer, the scan restarts from index 0 forever. Rule 12: not corrected.
    internal static int AnimCmd_PartsLink(int streamPtr)
    {
        ushort uVar1;
        short sVar2;
        short uVar3;
        ushort uVar4;
        int iVar5;
        short sVar6;
        uint uVar7;
        ushort uVar8;
        int iVar9;
        uint uVar10;
        ushort uVar11;
        short sStack_14;

        uVar1 = PsxRam.ReadU16(streamPtr);
        uVar8 = (ushort)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        sStack_14 = (short)(sbyte)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            return streamPtr + 4;
        }

        do
        {
            uVar11 = 0;
            do
            {
                iVar9 = (short)uVar11;
                uVar4 = (ushort)(uVar11 + 1);

                // Byte 2 of the packed word, compared against the SIGNED high byte of the command.
                // A group tag of 0x80 or above therefore never matches; that asymmetry is the
                // original's.
                if ((ushort)(byte)((uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + (iVar9) * 4) >> 0x10) == (short)(sbyte)(uVar1 >> 8))
                {
                    uVar7 = ((uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + (iVar9) * 4) & 0xff00) >> 8;
                    uVar4 = (ushort)(uVar11 + 1);
                    if (uVar7 == (uint)(short)uVar8)
                    {
                        uVar10 = (uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + (iVar9) * 4) >> 0x18;
                        sVar2 = (short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (iVar9) * 2);
                        if (uVar7 != 0)
                        {
                            // GHIDRA: DAT_801fa87f @ 0x801FA87F (VS.EXE)
                            // A one-byte load at `0x801FA87F + uVar7 * 4`, one byte BELOW
                            // g_renderMetadataBuffer. Solving 0x801FA87F + 4n = 0x801FA883 + 4k
                            // gives k = n - 1, so the byte is the TOP byte of the PREVIOUS
                            // metadata word — the same field read as `>> 0x18` above, one entry
                            // back. uVar7 is non-zero on this path, so the index is in range.
                            // C# cannot alias a uint[] byte-wise, so the identity is written out;
                            // it is the same memory and the same value, proved from the
                            // instruction at 0x80039ED0, `lbu v0,-0x7781(at)`.
                            uVar3 = (short)PsxRam.ReadU16(AnimVm.DAT_801f2180 + (
                                (int)((uint)(byte)((uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + (int)(uVar7 - 1) * 4) >> 0x18) * 0x10)
                                + Field_801f2198) * 2);
                            iVar9 = 0;
                            if (0 < sVar2)
                            {
                                iVar5 = 0;
                                do
                                {
                                    iVar9 = iVar9 + 1;
                                    iVar5 = (int)(uVar10 + (uint)(iVar5 >> 0x10));
                                    PsxRam.WriteU16(AnimVm.DAT_801f2180 + (iVar5 * 0x10 + Field_801f2180) * 2, (ushort)uVar3);
                                    PsxRam.WriteU16(AnimVm.DAT_801f2180 + (iVar5 * 0x10 + Field_801f2188) * 2, (ushort)uVar3);
                                    iVar5 = iVar9 * 0x10000;
                                }
                                while (iVar9 * 0x10000 >> 0x10 < sVar2);
                            }
                        }

                        iVar9 = 0;
                        sVar6 = (short)(PsxRam.ReadU16(AnimVm.DAT_801f2180 + ((int)uVar10 * 0x10 + Field_801f2188) * 2)
                                        + PsxRam.ReadU16(AnimVm.g_meshXOffsetBuffer + ((short)uVar11) * 2));
                        if (0 < sVar2)
                        {
                            iVar5 = 0;
                            do
                            {
                                iVar9 = iVar9 + 1;
                                iVar5 = (int)(uVar10 + (uint)(iVar5 >> 0x10));
                                PsxRam.WriteU16(AnimVm.DAT_801f2180 + (iVar5 * 0x10 + Field_801f2198) * 2, (ushort)sVar6);
                                PsxRam.WriteU16(AnimVm.DAT_801f2180 + (iVar5 * 0x10 + Field_801f2190) * 2, (ushort)sVar6);
                                iVar5 = iVar9 * 0x10000;
                            }
                            while (iVar9 * 0x10000 >> 0x10 < sVar2);
                        }

                        uVar8 = (ushort)(uVar11 + 1);
                        if (sStack_14 == 1)
                        {
                            return streamPtr + 4;
                        }

                        uVar4 = 1;
                        sStack_14 = (short)(sStack_14 + -1);
                    }
                }

                uVar11 = uVar4;
            }
            while ((short)uVar11 < 0x40);
        }
        while (true);
    }

    // GHIDRA: AnimCmd_Utility @ 0x8003A6C4 (VS.EXE)
    // Opcode 20, which the image's name table spells `utylty`. Ghidra's name and the image's name
    // agree in meaning, so no arbitration is needed; Ghidra's spelling is kept for the C# name.
    //
    // Four sub-commands selected by the sign-extended high byte of the command word. All of them
    // aim or orient one part at another through the GTE. Sub-command 2 is the only one that reads
    // a third halfword, and it consumes it on BOTH sides of the AnimVm.DAT_800b305a gate — that is why
    // the `else if (iVar10 == 2)` at the bottom exists.
    //
    // BLOCKED, and the reason the body currently computes nothing: FUN_8003f228, FUN_8003f2b0 and
    // FUN_80047550 are shared helpers of the whole handler family, not of this slice — 20, 20 and
    // many callers respectively — so they are not transliterated here. Their stubs below return 0
    // / do nothing, which makes every guarded block unreachable. The control flow is complete and
    // will come alive unchanged when those three land.
    internal static int AnimCmd_Utility(int param_1)
    {
        short sVar1;
        ushort uVar2;
        int puVar3;
        int lVar4;
        int psVar5;
        int lVar6;
        int iVar7;
        int psVar8;
        int puVar9;
        int iVar10;
        uint uVar11;
        short sVar12;
        uint uVar13;
        int iVar14;
        int uVar15;
        ushort uStack_cc;
        VECTOR VStack_80 = new();
        VECTOR VStack_70 = new();
        MATRIX MStack_60 = new();
        MATRIX MStack_40 = new();

        uVar15 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        iVar10 = (int)((uint)PsxRam.ReadU16(param_1) << 0x10) >> 0x18;
        uStack_cc = PsxRam.ReadU16(param_1 + 2);
        puVar9 = param_1 + 4;
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            if (iVar10 == 1)
            {
                psVar8 = FUN_8003f228((uint)((int)((uint)uStack_cc << 0x10) >> 0x18), uVar15);
                puVar3 = FUN_8003f2b0((uint)(uStack_cc & 0xff), uVar15);
                if (psVar8 != 0 && puVar3 != 0)
                {
                    ReadRotMatrix(MStack_60);

                    // BLOCKED: PsxSdkMonogame's LibGte has no TransposeMatrix, and adding one is
                    // an SDK change, not a runtime change — outside this file. The original's
                    // `TransposeMatrix(&MStack_60, &MStack_40)` is therefore NOT performed, and
                    // MStack_40 stays zero, so ApplyMatrixLV below yields a zero vector. This is
                    // the one place in the file where a call the original makes is absent. It is
                    // reachable only once FUN_8003f228 and FUN_8003f2b0 stop returning 0.
                    VStack_80.vx = MStack_60.t[0];
                    VStack_80.vy = MStack_60.t[1];
                    VStack_80.vz = MStack_60.t[2];
                    PushMatrix();
                    ApplyMatrixLV(MStack_40, VStack_80, VStack_70);
                    PopMatrix();
                    iVar14 = (short)PsxRam.ReadU16(psVar8) - FileIo._DAT_1f8000b4;
                    iVar10 = -(int)(short)PsxRam.ReadU16(psVar8 + 2) - VStack_70.vy;
                    iVar7 = -((short)PsxRam.ReadU16(psVar8 + 4) - FileIo._DAT_1f8000bc) - VStack_70.vz;
                    lVar6 = ratan2(-iVar10, iVar7);
                    lVar4 = SquareRoot0(iVar7 * iVar7 + iVar10 * iVar10);
                    lVar4 = ratan2(-iVar14 - VStack_70.vx, lVar4);
                    PsxRam.WriteU16(puVar3, (ushort)(short)lVar6);
                    PsxRam.WriteU16(puVar3 + 2, (ushort)(short)lVar4);
                    PsxRam.WriteU16(puVar3 + 4, 0);
                }
            }
            else if (iVar10 < 2)
            {
                if (iVar10 == 0 && (iVar10 = FUN_8003f2b0((uint)(short)uStack_cc, uVar15)) != 0)
                {
                    // GHIDRA: DAT_1f80007e @ 0x1F80007E (VS.EXE)
                    // The second halfword of the scratchpad SVECTOR at 0x1F80007C, which FileIo
                    // already declares and RotMatrix already consumes as an SVECTOR — 0x7C + 2 is
                    // its vy. Read through FileIo rather than re-declared here.
                    uVar11 = (uint)((FileIo.SVECTOR_1f80007c.vy - (short)PsxRam.ReadU16(iVar10 + 2)) & 0xfff);
                    if (0x800 < uVar11 - 0x400)
                    {
                        uVar11 = 0x1000 - uVar11;
                    }

                    PsxRam.WriteU16(
                        iVar10 + 4,
                        (ushort)(short)((short)PsxRam.ReadU16(iVar10 + 4) + 0x400 + (short)uVar11));
                }
            }
            else if (iVar10 == 2)
            {
                uVar2 = PsxRam.ReadU16(puVar9);
                puVar9 = param_1 + 6;
                psVar8 = FUN_8003f228((uint)(uStack_cc & 0xff), uVar15);
                psVar5 = FUN_8003f228((uint)(short)(sbyte)(uStack_cc >> 8), uVar15);
                iVar10 = FUN_8003f2b0((uint)(uVar2 & 0x1f), uVar15);
                if (psVar8 != 0 && psVar5 != 0 && iVar10 != 0)
                {
                    sVar12 = (short)PsxRam.ReadU16(psVar5 + 2);
                    sVar1 = (short)PsxRam.ReadU16(psVar8 + 2);
                    lVar6 = SquareRoot0(
                        ((short)PsxRam.ReadU16(psVar5) - (short)PsxRam.ReadU16(psVar8))
                        * ((short)PsxRam.ReadU16(psVar5) - (short)PsxRam.ReadU16(psVar8))
                        + ((short)PsxRam.ReadU16(psVar5 + 4) - (short)PsxRam.ReadU16(psVar8 + 4))
                        * ((short)PsxRam.ReadU16(psVar5 + 4) - (short)PsxRam.ReadU16(psVar8 + 4)));
                    PsxRam.WriteU16(
                        iVar10 + 2,
                        (ushort)(short)((short)PsxRam.ReadU16(iVar10 + 2) + -0x400));
                    lVar6 = ratan2(sVar12 - sVar1, lVar6);
                    PsxRam.WriteU16(iVar10 + 4, (ushort)(short)lVar6);
                }
            }
            else if (iVar10 == 3)
            {
                iVar10 = FUN_8003f228(0, uVar15);
                iVar7 = FUN_8003f2b0(0, uVar15);
                if (iVar10 != 0 && iVar7 != 0)
                {
                    iVar14 = 1;
                    uVar11 = (uint)(PsxRam.ReadU16(iVar7 + 2) & 0xfff);
                    iVar10 = 0x10000;
                    do
                    {
                        uVar13 = (uint)(iVar10 >> 0x10);
                        if (uVar13 != 3)
                        {
                            if ((uint)(uStack_cc & 0xf) == uVar13)
                            {
                                uStack_cc = (ushort)((short)uStack_cc >> 4);
                            }
                            else
                            {
                                psVar8 = FUN_8003f228(uVar13, uVar15);
                                if (psVar8 != 0 && (iVar10 = FUN_8003f2b0(uVar13, uVar15)) != 0)
                                {
                                    uVar13 = (uint)(PsxRam.ReadU16(iVar10 + 2) & 0xfff);
                                    sVar12 = 0x800;
                                    if (uVar13 < uVar11)
                                    {
                                        iVar7 = (int)((uVar11 - uVar13) * 0x10000) >> 0x10;
                                        if (iVar7 < 0x100)
                                        {
                                            sVar12 = 0x400;
                                        }

                                        if (0xf00 < iVar7)
                                        {
                                            sVar12 = (short)(sVar12 + 0x400);
                                        }

                                        if (0x700 < iVar7)
                                        {
                                            if (iVar7 < 0x800)
                                            {
                                                goto LAB_8003ab08;
                                            }

                                            if (iVar7 < 0x900)
                                            {
                                                goto LAB_8003ab54;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        iVar7 = (int)((uVar13 - uVar11) * 0x10000) >> 0x10;
                                        if (iVar7 < 0x100)
                                        {
                                            sVar12 = 0xc00;
                                        }

                                        if (0xf00 < iVar7)
                                        {
                                            sVar12 = (short)(sVar12 + -0x400);
                                        }

                                        if (0x700 < iVar7)
                                        {
                                            if (iVar7 < 0x800)
                                            {
                                                goto LAB_8003ab54;
                                            }

                                            if (iVar7 < 0x900)
                                            {
                                                goto LAB_8003ab08;
                                            }
                                        }
                                    }

                                    goto LAB_8003ab5c;

                                    // The original's two labels sit one inside each arm and each
                                    // arm jumps into the OTHER one. C# cannot jump into a block,
                                    // so both are hoisted to the common level. Each still performs
                                    // exactly the single addition the original performs, and the
                                    // arm that reaches it is unchanged.
                                LAB_8003ab08:
                                    sVar12 = (short)(sVar12 + 0x400);
                                    goto LAB_8003ab5c;
                                LAB_8003ab54:
                                    sVar12 = (short)(sVar12 + -0x400);
                                LAB_8003ab5c:
                                    FUN_80047550(iVar10, VStack_80, 0, sVar12, 0x10);
                                    PsxRam.WriteU16(
                                        psVar8,
                                        (ushort)(short)((short)PsxRam.ReadU16(psVar8) + (short)VStack_80.vx));
                                    PsxRam.WriteU16(
                                        psVar8 + 4,
                                        (ushort)(short)((short)PsxRam.ReadU16(psVar8 + 4) + (short)VStack_80.vz));
                                }
                            }
                        }

                        iVar14 = iVar14 + 1;
                        iVar10 = iVar14 * 0x10000;
                    }
                    while (iVar14 * 0x10000 >> 0x10 < 6);
                }
            }
        }
        else if (iVar10 == 2)
        {
            puVar9 = param_1 + 6;
        }

        return puVar9;
    }

    // GHIDRA: no symbol @ 0x8003ABE4 (VS.EXE) — never promoted to a function; the only reference
    //         is dispatch-table slot 0x80082348.
    // Opcode 21, which the image's name table calls `objint_get`.
    //
    // Two halfwords: [0] opcode + first object id in the high byte, [1] second object id in the
    // low byte and the destination variable in the high byte. It fetches the two objects'
    // coordinate triples, subtracts them component by component and stores the length of the
    // difference into g_animSharedVarTable.
    //
    // Ghidra's decompilation of this function prints two enormous stack arrays
    // (`apsStack_20020[16380]`, `asStack_10030[32768]`). They are artefacts of unrecovered stack
    // extent — nothing reads or writes them — and are not reproduced. The real locals are the
    // three-short id array, the three-short difference array and the two pointers.
    internal static int AnimCmd_ObjIntGet(int param_1)
    {
        int psVar1;
        int lVar2;
        int iVar3;
        short sVar4;
        int iVar5;
        int uVar6;
        short[] asStack_30 = new short[4];
        short[] asStack_28 = new short[4];
        int[] apsStack_20 = new int[2];

        uVar6 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        asStack_30[0] = (short)(sbyte)(PsxRam.ReadU16(param_1) >> 8);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            iVar5 = 0;
            asStack_30[1] = (short)(PsxRam.ReadU16(param_1 + 2) & 0xff);
            asStack_30[2] = (short)(sbyte)(PsxRam.ReadU16(param_1 + 2) >> 8);
            do
            {
                sVar4 = (short)iVar5;
                psVar1 = FUN_8003f228((uint)asStack_30[sVar4], uVar6);
                iVar5 = iVar5 + 1;
                apsStack_20[sVar4] = psVar1;
            }
            while (iVar5 * 0x10000 >> 0x10 < 2);

            iVar5 = 0;
            do
            {
                iVar3 = iVar5 << 0x10;
                iVar5 = iVar5 + 1;

                // The original dereferences both pointers WITHOUT a null check; FUN_8003f228 can
                // return 0 and does so for every call while it is a blocked stub. PsxRam answers
                // an unresolved address with 0 rather than faulting, so the shape is preserved.
                // The original spells the destination as a BYTE offset from the array base,
                // `(int)asStack_28 + (iVar3 >> 0xf)`, i.e. iVar5 * 2. Indexing a short[] takes
                // that offset divided by two, which is `iVar3 >> 0x10`.
                asStack_28[iVar3 >> 0x10] = (short)((short)PsxRam.ReadU16(apsStack_20[0])
                                                    - (short)PsxRam.ReadU16(apsStack_20[1]));
                apsStack_20[0] = apsStack_20[0] + 2;
                apsStack_20[1] = apsStack_20[1] + 2;
            }
            while (iVar5 * 0x10000 >> 0x10 < 3);

            lVar2 = SquareRoot0(asStack_28[0] * asStack_28[0]
                                + asStack_28[1] * asStack_28[1]
                                + asStack_28[2] * asStack_28[2]);
            PsxRam.WriteU16(AnimVm.g_animSharedVarTable + (asStack_30[2]) * 2, (ushort)(short)lVar2);
        }

        return param_1 + 4;
    }

    // GHIDRA: no symbol @ 0x8003AD80 (VS.EXE) — never promoted to a function; the only reference
    //         is dispatch-table slot 0x8008234C.
    // Opcode 22, which the image's name table calls `objlong_get`.
    //
    // Two halfwords: [0] opcode + a SIGNED start index in the high byte, [1] a run length in the
    // low byte and the destination variable in the high byte. It sums that run of
    // g_meshXOffsetBuffer entries into one variable.
    //
    // The loop below looks redundant because it is: the original reuses iVar2 both as the loop
    // comparand and as the shift temporary for the index, and recomputes the comparand from the
    // ALREADY-INCREMENTED counter at the bottom. It is transliterated as written.
    internal static int AnimCmd_ObjLongGet(int param_1)
    {
        int iVar1;
        int iVar2;
        short sVar3;
        int iVar4;

        iVar4 = (int)((uint)PsxRam.ReadU16(param_1) << 0x10) >> 0x18;
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            sVar3 = 0;
            iVar1 = iVar4 + (PsxRam.ReadU16(param_1 + 2) & 0xff);
            iVar2 = iVar4;
            while (iVar2 < iVar1)
            {
                iVar2 = iVar4 << 0x10;
                iVar4 = iVar4 + 1;
                // Same byte-offset spelling as objint_get: `(int)&g_meshXOffsetBuffer +
                // (iVar2 >> 0xf)` is iVar4 * 2 bytes, so the short[] index is `iVar2 >> 0x10`.
                // Note that the index is the value of iVar4 BEFORE the increment above, truncated
                // to sixteen bits and sign-extended.
                sVar3 = (short)(sVar3 + PsxRam.ReadU16(AnimVm.g_meshXOffsetBuffer + (iVar2 >> 0x10) * 2));
                iVar2 = iVar4 * 0x10000 >> 0x10;
            }

            PsxRam.WriteU16(AnimVm.g_animSharedVarTable + ((short)(sbyte)(PsxRam.ReadU16(param_1 + 2) >> 8)) * 2, (ushort)sVar3);
        }

        return param_1 + 4;
    }

    // GHIDRA: AnimCmd_BitChk @ 0x8003AE50 (VS.EXE)
    // Opcode 23, which the image's name table calls `bit_chk` — Ghidra's symbol and the image
    // agree. Ghidra's pre-comment reads: "CERTAIN: homologue de GAME.EXE AnimCmd_BitChk. Lit
    // g_animSharedVarTable puis pilote uniquement des branches/rewind de stream via
    // g_meshOffsetBuffer et g_meshStreamPtrBuffer."
    //
    // THIS IS THE VM'S REAL BRANCH, and its return value is the whole point. Two or four
    // halfwords: [0] opcode + a mode/variable byte, [1] the bit mask to test, and — only when
    // bits 6-7 of that byte are 0b10 — [2] an index into the word table at g_cdFileBufferTable
    // holding the address to jump to.
    //
    // The test: variable = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (byte & 0xf) * 2), optionally complemented (bit 4);
    // bit 5 selects ALL-bits-set instead of ANY-bit-set. When the test FAILS the handler simply
    // returns the address after the operands. When it SUCCEEDS, bits 6-7 pick the branch:
    //   0b01  scan forward to the next zero halfword and stop the stream this frame
    //   0b00  rewind to PsxRam.ReadI32(AnimVm.g_meshStreamPtrBuffer + (mesh) * 4) - 4 and stop the stream this frame
    //   0b10  jump to the table address and stop the stream this frame
    //   0b11  fall through with the pointer already past the operands
    internal static int AnimCmd_BitChk(int streamPtr, ushort meshIndex)
    {
        ushort uVar1;
        ushort uVar2;
        int puVar3;
        ushort uVar4;
        uint uVar5;

        // `in_t1` in Ghidra's output: the decompiler could not prove it is assigned on every path
        // into the `uVar4 == 0x80` arm. It is. Both the assignment and the use are gated on the
        // SAME two bits of the SAME byte — `(uVar5 & 0xc0) == 0x80` above and `uVar4 == 0x80`
        // below, with uVar5 and uVar2 both being that byte — so the arm is unreachable unless the
        // assignment ran. Initialised to 0 here only because C# demands it.
        int in_t1 = 0;
        ushort uStack_a;

        uVar5 = (uint)((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18);
        uVar2 = (ushort)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        uVar4 = PsxRam.ReadU16(streamPtr + 2);
        puVar3 = streamPtr + 4;
        if ((uVar5 & 0xc0) == 0x80)
        {
            uVar1 = PsxRam.ReadU16(puVar3);
            puVar3 = streamPtr + 8;
            in_t1 = PsxRam.ReadI32(g_cdFileBufferTableAddress + (short)uVar1 * 4);
        }

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uStack_a = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar5 & 0xf) * 2);
            if ((uVar5 & 0x10) != 0)
            {
                uStack_a = (ushort)~PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar5 & 0xf) * 2);
            }

            if ((uVar5 & 0x20) == 0)
            {
                if ((uStack_a & uVar4) == 0)
                {
                    return puVar3;
                }
            }
            else if ((uStack_a & uVar4) != uVar4)
            {
                return puVar3;
            }

            uVar4 = (ushort)(uVar2 & 0xc0);
            if (uVar4 == 0x40)
            {
                while (PsxRam.ReadU16(puVar3) != 0)
                {
                    puVar3 = puVar3 + 2;
                }

                PsxRam.WriteU16(AnimVm.g_meshOffsetBuffer + ((short)meshIndex) * 2, 1);
            }
            else if (uVar4 < 0x41)
            {
                // The original re-tests the same two bits it has already reduced into uVar4. Left
                // as written.
                if ((uVar2 & 0xc0) == 0)
                {
                    PsxRam.WriteU16(AnimVm.g_meshOffsetBuffer + ((short)meshIndex) * 2, 1);
                    puVar3 = PsxRam.ReadI32(AnimVm.g_meshStreamPtrBuffer + ((short)meshIndex) * 4) + -4;
                }
            }
            else if (uVar4 == 0x80)
            {
                PsxRam.WriteU16(AnimVm.g_meshOffsetBuffer + ((short)meshIndex) * 2, 1);
                puVar3 = in_t1;
            }
        }

        return puVar3;
    }

    // GHIDRA: no symbol @ 0x8003AFF4 (VS.EXE) — never promoted to a function; the only reference
    //         is dispatch-table slot 0x80082354.
    // Opcode 24, which the image's name table calls `bit_set`.
    //
    // Three halfwords: [0] opcode + an operation selector in the high byte, [1] the destination
    // variable index, [2] either an immediate operand or — when bit 4 of the selector is set — the
    // index of a variable to read the operand from, in which case the selector itself is narrowed
    // to its low nibble.
    //
    // BLOCKED: FUN_8003f540 @ 0x8003F540 is the VM's arithmetic/logic unit — one switch over the
    // selector implementing assign, add, subtract, or, and, xor, multiply, divide, reverse-
    // subtract and more. Forty-two functions call it, so it belongs to a shared slice rather than
    // to this one; the stub below returns 0.
    internal static int AnimCmd_BitSet(int param_1)
    {
        ushort uVar1;
        sbyte cVar2;
        short sVar3;
        ushort uVar4;
        int psVar5;

        cVar2 = (sbyte)(PsxRam.ReadU16(param_1) >> 8);
        uVar4 = (ushort)cVar2;
        if (((int)((uint)PsxRam.ReadU16(param_1) << 0x10) >> 0x18 & 0x10) == 0)
        {
            uVar1 = PsxRam.ReadU16(param_1 + 4);
        }
        else
        {
            uVar4 = (ushort)((short)cVar2 & 0xf);
            uVar1 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + ((short)PsxRam.ReadU16(param_1 + 4)) * 2);
        }

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            // The original computes a POINTER into g_animSharedVarTable once and writes through it
            // twice: `&g_animSharedVarTable + ((int)((uint)param_1[1] << 0x10) >> 0xf)`, a BYTE
            // offset of (short)param_1[1] * 2. Halved, that is the element index, carried here in
            // the same single local the original keeps the pointer in.
            psVar5 = (int)((uint)PsxRam.ReadU16(param_1 + 2) << 0x10) >> 0x10;
            sVar3 = (short)FUN_8003f540(
                (uint)(short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (psVar5) * 2), (short)uVar4, (uint)(short)uVar1);
            PsxRam.WriteU16(AnimVm.g_animSharedVarTable + (psVar5) * 2, (ushort)sVar3);
            if ((short)uVar4 == 8)
            {
                PsxRam.WriteU16(AnimVm.g_animSharedVarTable + (psVar5) * 2, PsxRam.ReadU16(AnimVm.g_animSharedVarTable + ((short)uVar1) * 2));
            }
        }

        return param_1 + 6;
    }

    // GHIDRA: AnimCmd_EndSet @ 0x8003B10C (VS.EXE)
    // Opcode 25, which the image's name table calls `end_set` — Ghidra's symbol and the image
    // agree. Ghidra's pre-comment reads: "CERTAIN: homologue de GAME.EXE AnimCmd_EndSet. Termine
    // ou rewinde le stream courant via g_meshStreamPtrBuffer et g_meshOffsetBuffer."
    //
    // ONE HALFWORD, TWO OPPOSITE OUTCOMES, and the returned pointer is what distinguishes them:
    //   bit 0 of AnimVm.DAT_800b305a clear — clear this slot's stream pointer, arm its countdown at 1,
    //     and return streamPtr + 2. Next frame ExecuteAnimStreamBatch decrements the countdown
    //     from 1, takes the `uVar1 == 1` arm at 0x800368D8, sees the null slot and drops the
    //     stream. THIS IS HOW A STREAM ENDS.
    //   bit 0 set — return the slot's SAVED stream pointer minus four bytes and touch nothing.
    //     A rewind.
    // Note the asymmetry: the rewind path reads the slot the other path clears.
    internal static int AnimCmd_EndSet(int streamPtr, ushort meshIndex)
    {
        int puVar1;

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            puVar1 = streamPtr + 2;
            PsxRam.WriteI32(AnimVm.g_meshStreamPtrBuffer + ((short)meshIndex) * 4, 0);
            PsxRam.WriteU16(AnimVm.g_meshOffsetBuffer + ((short)meshIndex) * 2, 1);
        }
        else
        {
            puVar1 = PsxRam.ReadI32(AnimVm.g_meshStreamPtrBuffer + ((short)meshIndex) * 4) + -4;
        }

        return puVar1;
    }

    // GHIDRA: no symbol @ 0x8003D2FC (VS.EXE) — never promoted to a function; the only reference
    //         is dispatch-table slot 0x80082380.
    // Opcode 35, which the image's name table calls `if_set`.
    //
    // THE SECOND OF THE TWO REAL BRANCHES, and unlike bit_chk it branches by SCANNING THE STREAM
    // for a label rather than by loading an address. Bits 14-15 of the command word select the
    // form:
    //   0b00  a conditional. [1] carries the variable index in its high byte plus the same two
    //         modifier bits bit_chk uses (0x10 complement, 0x20 all-bits), [2] the mask. On a
    //         FAILED test — the `bVar3` path — it scans forward for a halfword whose low 12 bits
    //         equal the low 12 bits of the command word, then steps ONE MORE halfword past it.
    //         That trailing step is why the label halfword itself is never executed.
    //   0b01  no operand at all: fall through at streamPtr + 2. Neither branch of the outer `if`
    //         is taken, and the initial `puVar4` stands.
    //   0b1x  an unconditional scan for the 16-bit marker `(cmd & 0xfff) - 0x8000`, which is
    //         always negative and so cannot collide with a plain data halfword.
    internal static int AnimCmd_IfSet(int param_1)
    {
        ushort uVar1;
        ushort uVar2;
        bool bVar3;
        int puVar4;
        ushort uStack_8;
        ushort uStack_2;

        uStack_8 = PsxRam.ReadU16(param_1);
        puVar4 = param_1 + 2;
        if ((uStack_8 & 0xc000) == 0)
        {
            uVar1 = PsxRam.ReadU16(puVar4);
            uStack_2 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (uVar1 >> 8) * 2);
            uVar2 = PsxRam.ReadU16(param_1 + 4);
            puVar4 = param_1 + 6;
            if ((uVar1 & 0x10) != 0)
            {
                uStack_2 = (ushort)~uStack_2;
            }

            bVar3 = false;
            if ((uVar1 & 0x20) == 0)
            {
                if ((uStack_2 & uVar2) == 0)
                {
                    bVar3 = true;
                }
            }
            else
            {
                bVar3 = (uStack_2 & uVar2) != uVar2;
            }

            if (bVar3)
            {
                // The `uStack_8 &= 0xfff` sits in the condition section of the original's `for`,
                // so it is re-evaluated every iteration. Idempotent, and kept where it is.
                for (; ; puVar4 = puVar4 + 2)
                {
                    uStack_8 = (ushort)(uStack_8 & 0xfff);
                    if ((PsxRam.ReadU16(puVar4) & 0xfff) == uStack_8)
                    {
                        break;
                    }
                }

                puVar4 = puVar4 + 2;
            }
        }
        else if ((uStack_8 & 0x4000) != 0)
        {
            do
            {
                uVar1 = PsxRam.ReadU16(puVar4);
                puVar4 = puVar4 + 2;
            }
            while ((short)uVar1 != (int)(((uStack_8 & 0xfff) - 0x8000) * 0x10000) >> 0x10);
        }

        return puVar4;
    }

    // ==== Callees this slice does not own =====================================================
    // Each of the four below is a shared helper of the whole handler family, reached from many
    // opcodes outside this file, so transliterating it here would claim ownership of code another
    // slice must write. They are declared with their Ghidra address and left empty; the bodies
    // above are complete around them.

    // GHIDRA: FUN_8003f228 @ 0x8003F228 (VS.EXE)
    // BLOCKED: resolves an object id to the address of its coordinate triple. Twenty callers.
    // Three sources depending on bits 4 and 5 of the id: the eight-byte records at
    // UNK_801f2080 @ 0x801F2080, the pointer table at DAT_801faaac @ 0x801FAAAC (+0x3C), or the
    // task-context slots at contextPtr + 0x18 + id*4 (+0x114).
    private static int FUN_8003f228(uint param_1, int param_2)
    {
        return 0;
    }

    // GHIDRA: FUN_8003f2b0 @ 0x8003F2B0 (VS.EXE)
    // BLOCKED: the rotation counterpart of FUN_8003f228 — same three sources, offsets +0x11C and
    // the eight-byte records at DAT_801f2000 @ 0x801F2000, falling back to
    // DAT_1f800084 @ 0x1F800084.
    private static int FUN_8003f2b0(uint param_1, int param_2)
    {
        return 0;
    }

    // GHIDRA: FUN_8003f540 @ 0x8003F540 (VS.EXE)
    // BLOCKED: the VM's arithmetic/logic unit, 384 bytes, FORTY-TWO callers. One switch over the
    // operation selector: 0 assign, 1 add, 2 subtract, 3 or, 4 and, 5 xor, 6 multiply, 7 divide,
    // 9 reverse-subtract, 10 store-through, and more past the window read here.
    private static int FUN_8003f540(uint param_1, short param_2, uint param_3)
    {
        return 0;
    }

    // GHIDRA: FUN_80047550 @ 0x80047550 (VS.EXE)
    // BLOCKED: 312 bytes. Called by AnimCmd_Utility's sub-command 3 with a rotation pointer, an
    // output VECTOR, 0, an angle and 0x10; it fills the vector, which the caller then adds to a
    // part's x and z. The prototype is not printed by Ghidra and is taken from the call site.
    private static void FUN_80047550(int param_1, VECTOR param_2, int param_3, short param_4, int param_5)
    {
    }
}
