using System;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's animation-script VM: the seven commands that move geometry.
//
// THE VM. ExecuteAnimStreamBatch @ 0x80036768 is a threaded interpreter over raw PSX memory:
//
//     while (uVar1 != 0) {
//         puVar2 = (*(code *)(&g_animStreamDispatchTable)[*puVar2 & 0xff])(puVar2, iVar6 >> 0x10);
//         uVar1 = *puVar2;
//     }
//
// so the opcode is the LOW BYTE of the first halfword, every handler RETURNS THE ADDRESS of the
// next command, and a zero halfword ends the stream. The stream is therefore modelled here exactly
// as the brief requires and as VS_EXE_exe.cs already models a task context: an `int` PSX address
// walked with PsxRam.ReadU16 / ReadI32, never a copied ushort[]. A handler that returned an index
// into a local copy could not be re-read by the interpreter.
//
// THE BINARY NAMES ITS OWN OPCODES. A table of fifty 16-byte ASCII names sits at 0x800823C0, one
// per dispatch slot, and it is the only naming vein in the program. The seven names below are read
// out of it, and each `// Opcode n, ...` line quotes it verbatim. (The dispatch table at
// 0x800822F4 holds fifty-one pointers; the fifty-first, 0x8003EF04, has no name entry.)
//
// NAMING. The `GHIDRA:` line always spells what the Ghidra database actually carries. Four of the
// seven are already named there — AnimCmd_BaseCulX/Y/Z/P, one of them with a CERTAIN comment
// crediting the GAME.EXE homologue — and keep those names. The other three, 0x800381B4, 0x8003838C
// and 0x80038554, are not even defined functions in the database: Ghidra decompiles them on demand
// as UndefinedFunction_8003xxxx. Their annotations say so, and the C# names come from the image's
// own opcode table, which is evidence, not invention.
//
// WHAT `cul` MEANS, since the dispatch brief asked for a ruling. It is `calc`, romanised the way
// this team romanised it, and the four base_cul* bodies settle it on their own: each one walks a
// table of 8-byte records and applies FUN_8003f540 — the operator dispatcher, whose twelve cases
// are set / add / sub / or / and / xor / mul / div / reverse-sub / store / rand / mod — to one
// fixed short of every record. base_culX takes the short at +0, base_culY the one at +2, base_culZ
// the one at +4, base_culP the one at +6. Four arithmetic commands over the X, Y, Z and fourth
// components of the same records. Nothing culls, and nothing touches a palette.
//
// That bears on the discordance the brief flagged one slot away, at opcode 9, which the image
// calls `cul_set` and which Ghidra carries as AnimCmd_SetMeshPaletteRange. The body at 0x80038720
// is not mine to transliterate, but it was read, and it resolves a rotation vector
// (FUN_8003f2b0), a translation vector (FUN_8003f228) or a scale vector (0x801F2100 + n*8)
// according to a two-bit selector, then hands all three, together with `0x801F2180 + slot * 0x20`
// — the very table base_culX/Y/Z/P fill — to FUN_8003f6c0. There is no CLUT, no texture page and
// no palette index anywhere in it; opcode 13 is `tpclut_set` and is a different handler. THE
// EVIDENCE SUPPORTS THE IMAGE'S `cul_set` AND DOES NOT SUPPORT AnimCmd_SetMeshPaletteRange. The
// Ghidra name reads like one carried across from GAME.EXE by table position rather than by body.
// Nothing is renamed here: this slice is read-only on the Ghidra side, and the annotations below
// still spell the symbol the database holds.
//
// THE GTE IS NOT INVOLVED. These seven commands never reach RotMatrix, ScaleMatrix or RotTransPers;
// they only edit the halfwords that the later per-frame geometry pass consumes. No GTE routine is
// re-implemented here, and PsxSdkMonogame.LibGte is untouched.
//
// OWNERSHIP CAVEAT, stated rather than hidden. Three things below are VM-wide, not
// transform-family property, and this is simply the first slice that needs them:
//
//   * FUN_8003f540 (42 call sites), FUN_8003f228 (20) and FUN_8003f2b0 (12) — shared helpers every
//     handler family calls. All seven commands here call the first, and two call the others, so
//     they have to exist for this file to compile. When a sibling slice needs them, they belong in
//     one VS_EXE/AnimStreamOps.cs and should be MOVED here-to-there, not copied.
//   * AnimVm.DAT_800b305a, the halfword whose bit 0 makes every handler skip evaluation and only advance
//     the stream. Same treatment.
//   * The PSX addresses of the four transform banks and the two render-metadata buffers. They are
//     consts, not RamRegion declarations, deliberately: this slice cannot close their extents from
//     the evidence it has, and registering a guessed extent would silently truncate a write. Until
//     the slice that owns that state declares the regions, PsxRam resolves nothing at these
//     addresses and the reads and writes below are inert — which is the same fail-soft the rest of
//     the port already relies on, not a different behaviour.
internal static class AnimCmdTransform
{
    // ===================================================================================
    // Globals
    // ===================================================================================

    // AnimVm.DAT_800b305a, AnimVm.g_animSharedVarTable, AnimVm.g_renderMetadataBuffer, AnimVm.g_meshCountBuffer,
    // AnimVm.DAT_801f2180, AnimVm.DAT_801f2000, AnimVm.UNK_801f2080 and AnimVm.DAT_801f2100 are the VM's SHARED globals; they
    // are declared once in AnimVm.cs and reached here as AnimVm.<name>. See AnimVm.cs for the
    // merged proof comments.

    // GHIDRA: DAT_801faaac @ 0x801FAAAC (VS.EXE)
    // Sixteen pointers, indexed by the low nibble of the selector. FUN_8003f228 returns entry + 0x3C
    // when the selector's bit 5 is set.
    private const int DAT_801faaac = unchecked((int)0x801FAAAC);

    // GHIDRA: DAT_1f800084 @ 0x1F800084 (VS.EXE)
    // The scratchpad halfword FUN_8003f2b0 falls back to. It is the first of the three at 0x1F800084,
    // 0x1F800086 and 0x1F800088 — the vx/vy/vz triple VS_EXE/FileIo.cs already declares for
    // SetupGeometry — which is exactly the three-short rotation vector rotate_set then walks.
    //
    // PARTIAL: FileIo.cs models those three as plain C# fields, so no PSX address resolves to them
    // and this fallback path reads and writes nothing here. Reconciling the scratchpad model is the
    // business of the slice that owns it, not of this one; nothing in FileIo.cs was touched.
    private const int DAT_1f800084 = 0x1F800084;

    // ===================================================================================
    // Opcode 6 — trans_set
    // ===================================================================================

    // GHIDRA: UndefinedFunction_800381b4 @ 0x800381B4 (VS.EXE)
    // Opcode 6, which the image's name table at 0x800823C0 calls `trans_set`. Ghidra holds no
    // defined function at this address — it decompiles the body on demand under that placeholder
    // name — so the annotation states the placeholder rather than inventing a symbol the database
    // does not have. Reached only through g_animStreamDispatchTable[6] @ 0x8008230C.
    //
    // The first halfword carries the opcode in its low byte and a target selector, sign-extended
    // from a char, in its high byte. FUN_8003f228 turns that selector into the address of a
    // three-short translation vector, or into 0. The second halfword packs three five-bit fields,
    // one per component: bits 0-3 the operator, bit 4 "the operand is an index into
    // AnimVm.g_animSharedVarTable". Operator 0xf skips the component and consumes no operand.
    //
    // The dispatcher passes a second argument, `iVar6 >> 0x10`; this handler ignores it, as do all
    // seven in this file, so none of them declares it. Ghidra's on-demand signature shows three
    // trailing junk parameters here, which are the argument registers still live at the call, not
    // values the body reads.
    //
    // The original also spills the selector to 0x10(sp) at 0x800381F0 and never reloads it: a dead
    // store with no observable effect, omitted.
    internal static int AnimCmd_TransSet(int streamPtr)
    {
        ushort uVar1;
        short sVar2;
        int psVar3;
        int iVar4;
        int psVar5;
        int puVar6;
        ushort uVar7;
        int iVar8;
        int uVar9;
        ushort uStack_26;

        // `uVar9 = *(undefined4 *)(DAT_8008d16c + 8)` — the running task's context pointer, read
        // inline as the original reads it. DAT_8008d16c is VS.EXE's g_CurrentTask, already ported
        // in VS_EXE/TaskSystem.cs; there is no accessor for +8 in the original and none is invented
        // here, exactly as VS_EXE_exe.cs already does at 0x800626xx.
        uVar9 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        uStack_26 = PsxRam.ReadU16(streamPtr + 2);
        puVar6 = streamPtr + 4;
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            psVar3 = FUN_8003f228((uint)(((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10)) >> 0x18), uVar9);
            iVar8 = 0;
            do
            {
                uVar7 = (ushort)(uStack_26 & 0xf);
                if (uVar7 == 0xf)
                {
                    // The original reaches the cursor bump at LAB_80038330 with a goto from here.
                    // C# cannot jump into a block, so the one statement that label names is
                    // written out on both paths. Same effect, same order.
                    if (psVar3 != 0)
                    {
                        psVar3 = psVar3 + 2;
                    }
                }
                else
                {
                    if ((uStack_26 & 0x10) == 0)
                    {
                        uVar1 = PsxRam.ReadU16(puVar6);
                    }
                    else
                    {
                        uVar1 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(puVar6) * 2);
                    }

                    puVar6 = puVar6 + 2;
                    if (psVar3 != 0)
                    {
                        sVar2 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar3), uVar7,
                            (uint)(int)(short)uVar1);
                        PsxRam.WriteU16(psVar3, (ushort)sVar2);
                        if (uVar7 == 8)
                        {
                            iVar4 = FUN_8003f228((uint)(int)(short)uVar1, uVar9);
                            psVar5 = iVar4 + (((iVar8 << 0x10) >> 0xf));
                            if (psVar5 != 0)
                            {
                                PsxRam.WriteU16(psVar3, PsxRam.ReadU16(psVar5));
                            }
                        }

                        psVar3 = psVar3 + 2;
                    }
                }

                uStack_26 = (ushort)((short)uStack_26 >> 5);
                iVar8 = iVar8 + 1;
            } while (((iVar8 * 0x10000) >> 0x10) < 3);
        }
        else
        {
            iVar8 = 0;
            do
            {
                if ((uStack_26 & 0xf) != 0xf)
                {
                    puVar6 = puVar6 + 2;
                }

                uStack_26 = (ushort)((short)uStack_26 >> 5);
                iVar8 = iVar8 + 1;
            } while (((iVar8 * 0x10000) >> 0x10) < 3);
        }

        return puVar6;
    }

    // ===================================================================================
    // Opcode 7 — rotate_set
    // ===================================================================================

    // GHIDRA: UndefinedFunction_8003838c @ 0x8003838C (VS.EXE)
    // Opcode 7, which the image's name table calls `rotate_set`. Same placeholder situation as
    // opcode 6. Reached only through g_animStreamDispatchTable[7] @ 0x80082310.
    //
    // Same stream shape as trans_set, with FUN_8003f2b0 resolving the rotation vector instead of
    // FUN_8003f228 the translation one, and two deliberate differences that are the original's and
    // are kept:
    //
    //   * the component cursor advances on EVERY iteration, including the skipped 0xf one, where
    //     trans_set advances it only when the vector resolved;
    //   * the main apply dereferences and stores through that cursor WITHOUT checking it against
    //     null — `lh a0,0x0(s3)` at 0x800384B0 and `sh v0,0x0(s3)` at 0x800384BC — while the
    //     operator-8 copy a few instructions later does check it, `beq s3,zero` at 0x800384DC.
    //     FUN_8003f2b0 can return 0, so the guard is genuinely missing on the hot path. Rule 12:
    //     not corrected. In this port the unresolvable address 0 simply reads 0 and writes nothing.
    internal static int AnimCmd_RotateSet(int streamPtr)
    {
        ushort uVar1;
        short sVar2;
        int psVar3;
        int iVar4;
        int psVar5;
        int puVar6;
        ushort uVar7;
        int iVar8;
        int uVar9;
        ushort uStack_26;

        uVar9 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        uStack_26 = PsxRam.ReadU16(streamPtr + 2);
        puVar6 = streamPtr + 4;
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            psVar3 = FUN_8003f2b0((uint)(((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10)) >> 0x18), uVar9);
            iVar8 = 0;
            do
            {
                uVar7 = (ushort)(uStack_26 & 0xf);
                if (uVar7 != 0xf)
                {
                    if ((uStack_26 & 0x10) == 0)
                    {
                        uVar1 = PsxRam.ReadU16(puVar6);
                    }
                    else
                    {
                        uVar1 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(puVar6) * 2);
                    }

                    puVar6 = puVar6 + 2;
                    sVar2 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar3), uVar7,
                        (uint)(int)(short)uVar1);
                    PsxRam.WriteU16(psVar3, (ushort)sVar2);
                    if (uVar7 == 8)
                    {
                        iVar4 = FUN_8003f2b0((uint)(int)(short)uVar1, uVar9);
                        psVar5 = iVar4 + (((iVar8 << 0x10) >> 0xf));
                        if ((psVar3 != 0) && (psVar5 != 0))
                        {
                            PsxRam.WriteU16(psVar3, PsxRam.ReadU16(psVar5));
                        }
                    }
                }

                psVar3 = psVar3 + 2;
                iVar8 = iVar8 + 1;
                uStack_26 = (ushort)((short)uStack_26 >> 5);
            } while (((iVar8 * 0x10000) >> 0x10) < 3);
        }
        else
        {
            iVar8 = 0;
            do
            {
                if ((uStack_26 & 0xf) != 0xf)
                {
                    puVar6 = puVar6 + 2;
                }

                uStack_26 = (ushort)((short)uStack_26 >> 5);
                iVar8 = iVar8 + 1;
            } while (((iVar8 * 0x10000) >> 0x10) < 3);
        }

        return puVar6;
    }

    // ===================================================================================
    // Opcode 8 — scale_set
    // ===================================================================================

    // GHIDRA: UndefinedFunction_80038554 @ 0x80038554 (VS.EXE)
    // Opcode 8, which the image's name table calls `scale_set`. Same placeholder situation as
    // opcodes 6 and 7. Reached only through g_animStreamDispatchTable[8] @ 0x80082314.
    //
    // The scale bank has no resolver function: the target is computed inline as
    // `0x801F2100 + (selector & 0xf) * 8`, which the disassembly shows at 0x80038604-0x80038614
    // (lui/ori 0x801F2100, andi 0xf, sll 3, addu s4).
    //
    // BLOCKED: WHEN BIT 4 OF THE SELECTOR IS CLEAR, THE TARGET CURSOR IS NEVER INITIALISED. The
    // branch at 0x800385FC skips the whole computation and nothing else in the function writes s4
    // before 0x80038578's `lh a0,0x0(s4)` reads through it, so the body runs against whatever s4
    // the interpreter left in the register — s4 is callee-saved and this function saved and will
    // restore it. Ghidra names that honestly as `unaff_s4`, and the same holds for `unaff_s5` on
    // the operator-8 path at 0x80038594. This is the original's behaviour, not a decompiler
    // artefact: it was checked instruction by instruction against the bytes at 0x80038554. The
    // script compiler presumably never emits scale_set with bit 4 clear.
    //
    // C# has no uninitialised register to reproduce, so the two cursors start at 0. That is the
    // one deviation in this file, it is forced by the language, and it is not a correction: an
    // unresolvable address reads 0 and writes nothing, so the port does nothing where the original
    // does something unpredictable. Rule 12 forbids repairing the path; it cannot forbid C# from
    // requiring definite assignment.
    internal static int AnimCmd_ScaleSet(int streamPtr)
    {
        ushort uVar1;
        short sVar2;
        ushort uVar3;
        uint uVar5;
        int iVar6;
        int unaff_s4 = 0;
        int unaff_s5 = 0;
        ushort uStack_26;

        uVar5 = (uint)(((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10)) >> 0x18);
        uStack_26 = PsxRam.ReadU16(streamPtr + 2);
        streamPtr = streamPtr + 4;
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            iVar6 = 0;
            if ((uVar5 & 0x10) != 0)
            {
                unaff_s4 = (int)(AnimVm.DAT_801f2100 + (uVar5 & 0xf) * 8);
            }

            do
            {
                uVar3 = (ushort)(uStack_26 & 0xf);
                if (uVar3 != 0xf)
                {
                    if ((uStack_26 & 0x10) == 0)
                    {
                        uVar1 = PsxRam.ReadU16(streamPtr);
                    }
                    else
                    {
                        uVar1 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(streamPtr) * 2);
                    }

                    streamPtr = streamPtr + 2;
                    sVar2 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(unaff_s4), uVar3,
                        (uint)(int)(short)uVar1);
                    PsxRam.WriteU16(unaff_s4, (ushort)sVar2);
                    if (uVar3 == 8)
                    {
                        if ((uVar1 & 0x10) != 0)
                        {
                            unaff_s5 = (int)(AnimVm.DAT_801f2100 + (uVar1 & 0xf) * 8);
                        }

                        unaff_s5 = unaff_s5 + (((iVar6 << 0x10) >> 0xf));
                        PsxRam.WriteU16(unaff_s4, PsxRam.ReadU16(unaff_s5));
                    }
                }

                unaff_s4 = unaff_s4 + 2;
                iVar6 = iVar6 + 1;
                uStack_26 = (ushort)((short)uStack_26 >> 5);
            } while (((iVar6 * 0x10000) >> 0x10) < 3);
        }
        else
        {
            iVar6 = 0;
            do
            {
                if ((uStack_26 & 0xf) != 0xf)
                {
                    streamPtr = streamPtr + 2;
                }

                uStack_26 = (ushort)((short)uStack_26 >> 5);
                iVar6 = iVar6 + 1;
            } while (((iVar6 * 0x10000) >> 0x10) < 3);
        }

        return streamPtr;
    }

    // ===================================================================================
    // Opcodes 26, 27, 28 and 49 — base_culX, base_culY, base_culZ, base_culP
    // ===================================================================================
    //
    // Four commands with one shape and four different field offsets inside the 8-byte sub-record:
    // +0, +2, +4 and +6. They are written out four times rather than folded into one parameterised
    // method: rule 3 forbids merging several original functions into one cleaner C# API, and each
    // one carries its own Ghidra address.
    //
    // The three contiguous addresses 0x8003B184, 0x8003B604 and 0x8003BA98 are opcodes 26, 27 and
    // 28, but base_culP at 0x8003BF2C — immediately after base_culZ in memory — is opcode 49. The
    // dispatch table decides, not the address order.
    //
    // Stream shape, identical for all four:
    //   halfword 0 : low byte the opcode, high byte a selector sign-extended from a char.
    //                Bits 0-1 pick the addressing mode, bit 2 makes the slot number indirect,
    //                and bits 4,5,6,7 say, one per operand, whether that operand is indirect.
    //   halfword 1 : low byte the slot number, high byte a signed count.
    //   halfword 2 : four 4-bit operator codes, one per operand.
    //   halfwords 3..6 : the four operands.

    // GHIDRA: AnimCmd_BaseCulX @ 0x8003B184 (VS.EXE)
    // Opcode 26, which the image's name table calls `base_culX`. Ghidra carries this name with a
    // CERTAIN comment: "homologous to GAME.EXE AnimCmd_BaseCulX. Reads AnimVm.g_meshCountBuffer and
    // AnimVm.g_renderMetadataBuffer to iterate active primitives; no direct CHBinMeshEntry read observed."
    // Reached only through g_animStreamDispatchTable[26] @ 0x8008235C.
    //
    // Writes the short at +0 of each of the four 8-byte sub-records of every 32-byte record it
    // walks.
    internal static int AnimCmd_BaseCulX(int streamPtr)
    {
        bool bVar1;
        ushort uVar2;
        short sVar3;
        short sVar4;
        uint uVar5;
        uint uVar6;
        int iVar7;
        int iVar8;
        int iVar9;
        int psVar10;
        int iVar11;
        int puVar12;
        ushort uStack_46;
        short sStack_44;
        ushort uStack_3e;
        Span<ushort> auStack_38 = stackalloc ushort[12];

        uVar6 = (uint)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        uVar5 = (uint)(((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10)) >> 0x18);
        uStack_46 = (ushort)uVar6;
        if ((uVar5 & 4) != 0)
        {
            uStack_46 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)uVar6 * 2);
        }

        sStack_44 = (short)(sbyte)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        uVar2 = PsxRam.ReadU16(streamPtr + 4);
        puVar12 = streamPtr + 6;
        iVar11 = 0;
        uVar6 = uVar5;
        do
        {
            // The original addresses the operand slot as a byte offset, `(iVar11 << 0x10) >> 0xf`,
            // which is iVar11 * 2 — element iVar11 of a short array.
            if ((uVar6 & 0x10) == 0)
            {
                auStack_38[iVar11] = PsxRam.ReadU16(puVar12);
            }
            else
            {
                auStack_38[iVar11] = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(puVar12) * 2);
            }

            puVar12 = puVar12 + 2;
            uVar6 = (uint)(((int)(uVar6 << 0x10)) >> 0x11);
            iVar11 = iVar11 + 1;
        } while (((iVar11 * 0x10000) >> 0x10) < 4);

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar5 = uVar5 & 3;
            if (uVar5 == 1)
            {
                iVar11 = (int)(short)uStack_46;
                psVar10 = AnimVm.DAT_801f2180 + PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar11 * 4 + 3) * 0x20;
                if (iVar11 < iVar11 + sStack_44)
                {
                    do
                    {
                        iVar7 = (int)(short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (((iVar11 << 0x10) >> 0xf)));
                        iVar9 = 0;
                        if (0 < iVar7)
                        {
                            do
                            {
                                iVar8 = 0;
                                uStack_3e = uVar2;
                                do
                                {
                                    sVar3 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar10),
                                        (ushort)(uStack_3e & 0xf), (uint)(int)(short)auStack_38[iVar8]);
                                    PsxRam.WriteU16(psVar10, (ushort)sVar3);
                                    psVar10 = psVar10 + 8;
                                    iVar8 = iVar8 + 1;
                                    uStack_3e = (ushort)((short)uStack_3e >> 4);
                                } while (((iVar8 * 0x10000) >> 0x10) < 4);

                                iVar9 = iVar9 + 1;
                            } while (((iVar9 * 0x10000) >> 0x10) < iVar7);
                        }

                        iVar11 = iVar11 + 1;
                    } while (((iVar11 * 0x10000) >> 0x10) < (int)(short)uStack_46 + (int)sStack_44);
                }
            }
            else if (uVar5 < 2)
            {
                if (uVar5 == 0)
                {
                    iVar11 = 0;
                    psVar10 = AnimVm.DAT_801f2180 + (short)uStack_46 * 0x20;
                    if (0 < sStack_44)
                    {
                        do
                        {
                            iVar7 = 0;
                            uStack_3e = uVar2;
                            do
                            {
                                sVar3 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar10),
                                    (ushort)(uStack_3e & 0xf), (uint)(int)(short)auStack_38[iVar7]);
                                PsxRam.WriteU16(psVar10, (ushort)sVar3);
                                psVar10 = psVar10 + 8;
                                iVar7 = iVar7 + 1;
                                uStack_3e = (ushort)((short)uStack_3e >> 4);
                            } while (((iVar7 * 0x10000) >> 0x10) < 4);

                            iVar11 = iVar11 + 1;
                        } while (((iVar11 * 0x10000) >> 0x10) < (int)sStack_44);
                    }
                }
            }
            else
            {
                iVar11 = 0;
                if (uVar5 == 2)
                {
                    iVar7 = 0;
                    do
                    {
                        iVar7 = iVar7 >> 0x10;
                        if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar7 * 4 + 2) == uStack_46)
                        {
                            psVar10 = AnimVm.DAT_801f2180 +
                                      (int)((uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + iVar7 * 4) >> 0x18) * 0x20;
                            sVar3 = (short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar7 * 2);
                            iVar7 = 0;
                            if (0 < sVar3)
                            {
                                do
                                {
                                    iVar9 = 0;
                                    uStack_3e = uVar2;
                                    do
                                    {
                                        sVar4 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar10),
                                            (ushort)(uStack_3e & 0xf), (uint)(int)(short)auStack_38[iVar9]);
                                        PsxRam.WriteU16(psVar10, (ushort)sVar4);
                                        psVar10 = psVar10 + 8;
                                        iVar9 = iVar9 + 1;
                                        uStack_3e = (ushort)((short)uStack_3e >> 4);
                                    } while (((iVar9 * 0x10000) >> 0x10) < 4);

                                    iVar7 = iVar7 + 1;
                                } while (((iVar7 * 0x10000) >> 0x10) < (int)sVar3);
                            }

                            bVar1 = sStack_44 == 1;
                            sStack_44 = (short)(sStack_44 + -1);
                            if (bVar1)
                            {
                                return puVar12;
                            }
                        }

                        iVar11 = iVar11 + 1;
                        iVar7 = iVar11 * 0x10000;
                    } while (((iVar11 * 0x10000) >> 0x10) < 0x40);
                }
            }
        }

        return puVar12;
    }

    // GHIDRA: AnimCmd_BaseCulY @ 0x8003B604 (VS.EXE)
    // Opcode 27, which the image's name table calls `base_culY`. Reached only through
    // g_animStreamDispatchTable[27] @ 0x80082360.
    //
    // base_culX with the field cursor started one short into each 8-byte sub-record: the short at
    // +2. Ghidra keeps the record cursor and the field cursor as two separate locals here, and that
    // is transcribed rather than collapsed.
    internal static int AnimCmd_BaseCulY(int streamPtr)
    {
        bool bVar1;
        ushort uVar2;
        short sVar3;
        short sVar4;
        uint uVar5;
        int iVar6;
        uint uVar7;
        int psVar8;
        int iVar9;
        int iVar10;
        int puVar11;
        int iVar12;
        int puVar13;
        ushort uStack_4e;
        short sStack_4c;
        ushort uStack_46;
        Span<ushort> auStack_40 = stackalloc ushort[12];

        uVar7 = (uint)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        uVar5 = (uint)(((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10)) >> 0x18);
        uStack_4e = (ushort)uVar7;
        if ((uVar5 & 4) != 0)
        {
            uStack_4e = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)uVar7 * 2);
        }

        sStack_4c = (short)(sbyte)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        uVar2 = PsxRam.ReadU16(streamPtr + 4);
        puVar13 = streamPtr + 6;
        iVar12 = 0;
        uVar7 = uVar5;
        do
        {
            if ((uVar7 & 0x10) == 0)
            {
                auStack_40[iVar12] = PsxRam.ReadU16(puVar13);
            }
            else
            {
                auStack_40[iVar12] = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(puVar13) * 2);
            }

            puVar13 = puVar13 + 2;
            uVar7 = (uint)(((int)(uVar7 << 0x10)) >> 0x11);
            iVar12 = iVar12 + 1;
        } while (((iVar12 * 0x10000) >> 0x10) < 4);

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar5 = uVar5 & 3;
            if (uVar5 == 1)
            {
                iVar12 = (int)(short)uStack_4e;
                puVar11 = AnimVm.DAT_801f2180 + PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar12 * 4 + 3) * 0x20;
                if (iVar12 < iVar12 + sStack_4c)
                {
                    do
                    {
                        iVar6 = (int)(short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (((iVar12 << 0x10) >> 0xf)));
                        iVar10 = 0;
                        if (0 < iVar6)
                        {
                            do
                            {
                                iVar9 = 0;
                                psVar8 = puVar11 + 2;
                                uStack_46 = uVar2;
                                do
                                {
                                    puVar11 = puVar11 + 8;
                                    sVar3 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar8),
                                        (ushort)(uStack_46 & 0xf), (uint)(int)(short)auStack_40[iVar9]);
                                    PsxRam.WriteU16(psVar8, (ushort)sVar3);
                                    psVar8 = psVar8 + 8;
                                    iVar9 = iVar9 + 1;
                                    uStack_46 = (ushort)((short)uStack_46 >> 4);
                                } while (((iVar9 * 0x10000) >> 0x10) < 4);

                                iVar10 = iVar10 + 1;
                            } while (((iVar10 * 0x10000) >> 0x10) < iVar6);
                        }

                        iVar12 = iVar12 + 1;
                    } while (((iVar12 * 0x10000) >> 0x10) < (int)(short)uStack_4e + (int)sStack_4c);
                }
            }
            else if (uVar5 < 2)
            {
                if (uVar5 == 0)
                {
                    iVar12 = 0;
                    puVar11 = AnimVm.DAT_801f2180 + (short)uStack_4e * 0x20;
                    if (0 < sStack_4c)
                    {
                        do
                        {
                            iVar6 = 0;
                            psVar8 = puVar11 + 2;
                            uStack_46 = uVar2;
                            do
                            {
                                puVar11 = puVar11 + 8;
                                sVar3 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar8),
                                    (ushort)(uStack_46 & 0xf), (uint)(int)(short)auStack_40[iVar6]);
                                PsxRam.WriteU16(psVar8, (ushort)sVar3);
                                psVar8 = psVar8 + 8;
                                iVar6 = iVar6 + 1;
                                uStack_46 = (ushort)((short)uStack_46 >> 4);
                            } while (((iVar6 * 0x10000) >> 0x10) < 4);

                            iVar12 = iVar12 + 1;
                        } while (((iVar12 * 0x10000) >> 0x10) < (int)sStack_4c);
                    }
                }
            }
            else
            {
                iVar12 = 0;
                if (uVar5 == 2)
                {
                    iVar6 = 0;
                    do
                    {
                        iVar6 = iVar6 >> 0x10;
                        if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar6 * 4 + 2) == uStack_4e)
                        {
                            puVar11 = AnimVm.DAT_801f2180 +
                                      (int)((uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + iVar6 * 4) >> 0x18) * 0x20;
                            sVar3 = (short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar6 * 2);
                            iVar6 = 0;
                            if (0 < sVar3)
                            {
                                do
                                {
                                    iVar10 = 0;
                                    psVar8 = puVar11 + 2;
                                    uStack_46 = uVar2;
                                    do
                                    {
                                        puVar11 = puVar11 + 8;
                                        sVar4 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar8),
                                            (ushort)(uStack_46 & 0xf), (uint)(int)(short)auStack_40[iVar10]);
                                        PsxRam.WriteU16(psVar8, (ushort)sVar4);
                                        psVar8 = psVar8 + 8;
                                        iVar10 = iVar10 + 1;
                                        uStack_46 = (ushort)((short)uStack_46 >> 4);
                                    } while (((iVar10 * 0x10000) >> 0x10) < 4);

                                    iVar6 = iVar6 + 1;
                                } while (((iVar6 * 0x10000) >> 0x10) < (int)sVar3);
                            }

                            bVar1 = sStack_4c == 1;
                            sStack_4c = (short)(sStack_4c + -1);
                            if (bVar1)
                            {
                                return puVar13;
                            }
                        }

                        iVar12 = iVar12 + 1;
                        iVar6 = iVar12 * 0x10000;
                    } while (((iVar12 * 0x10000) >> 0x10) < 0x40);
                }
            }
        }

        return puVar13;
    }

    // GHIDRA: AnimCmd_BaseCulZ @ 0x8003BA98 (VS.EXE)
    // Opcode 28, which the image's name table calls `base_culZ`. Reached only through
    // g_animStreamDispatchTable[28] @ 0x80082364.
    //
    // base_culY with the field cursor two shorts in: the short at +4 of each 8-byte sub-record.
    internal static int AnimCmd_BaseCulZ(int streamPtr)
    {
        bool bVar1;
        ushort uVar2;
        short sVar3;
        short sVar4;
        uint uVar5;
        int iVar6;
        uint uVar7;
        int psVar8;
        int iVar9;
        int iVar10;
        int puVar11;
        int iVar12;
        int puVar13;
        ushort uStack_4e;
        short sStack_4c;
        ushort uStack_46;
        Span<ushort> auStack_40 = stackalloc ushort[12];

        uVar7 = (uint)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        uVar5 = (uint)(((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10)) >> 0x18);
        uStack_4e = (ushort)uVar7;
        if ((uVar5 & 4) != 0)
        {
            uStack_4e = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)uVar7 * 2);
        }

        sStack_4c = (short)(sbyte)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        uVar2 = PsxRam.ReadU16(streamPtr + 4);
        puVar13 = streamPtr + 6;
        iVar12 = 0;
        uVar7 = uVar5;
        do
        {
            if ((uVar7 & 0x10) == 0)
            {
                auStack_40[iVar12] = PsxRam.ReadU16(puVar13);
            }
            else
            {
                auStack_40[iVar12] = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(puVar13) * 2);
            }

            puVar13 = puVar13 + 2;
            uVar7 = (uint)(((int)(uVar7 << 0x10)) >> 0x11);
            iVar12 = iVar12 + 1;
        } while (((iVar12 * 0x10000) >> 0x10) < 4);

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar5 = uVar5 & 3;
            if (uVar5 == 1)
            {
                iVar12 = (int)(short)uStack_4e;
                puVar11 = AnimVm.DAT_801f2180 + PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar12 * 4 + 3) * 0x20;
                if (iVar12 < iVar12 + sStack_4c)
                {
                    do
                    {
                        iVar6 = (int)(short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (((iVar12 << 0x10) >> 0xf)));
                        iVar10 = 0;
                        if (0 < iVar6)
                        {
                            do
                            {
                                iVar9 = 0;
                                psVar8 = puVar11 + 4;
                                uStack_46 = uVar2;
                                do
                                {
                                    puVar11 = puVar11 + 8;
                                    sVar3 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar8),
                                        (ushort)(uStack_46 & 0xf), (uint)(int)(short)auStack_40[iVar9]);
                                    PsxRam.WriteU16(psVar8, (ushort)sVar3);
                                    psVar8 = psVar8 + 8;
                                    iVar9 = iVar9 + 1;
                                    uStack_46 = (ushort)((short)uStack_46 >> 4);
                                } while (((iVar9 * 0x10000) >> 0x10) < 4);

                                iVar10 = iVar10 + 1;
                            } while (((iVar10 * 0x10000) >> 0x10) < iVar6);
                        }

                        iVar12 = iVar12 + 1;
                    } while (((iVar12 * 0x10000) >> 0x10) < (int)(short)uStack_4e + (int)sStack_4c);
                }
            }
            else if (uVar5 < 2)
            {
                if (uVar5 == 0)
                {
                    iVar12 = 0;
                    puVar11 = AnimVm.DAT_801f2180 + (short)uStack_4e * 0x20;
                    if (0 < sStack_4c)
                    {
                        do
                        {
                            iVar6 = 0;
                            psVar8 = puVar11 + 4;
                            uStack_46 = uVar2;
                            do
                            {
                                puVar11 = puVar11 + 8;
                                sVar3 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar8),
                                    (ushort)(uStack_46 & 0xf), (uint)(int)(short)auStack_40[iVar6]);
                                PsxRam.WriteU16(psVar8, (ushort)sVar3);
                                psVar8 = psVar8 + 8;
                                iVar6 = iVar6 + 1;
                                uStack_46 = (ushort)((short)uStack_46 >> 4);
                            } while (((iVar6 * 0x10000) >> 0x10) < 4);

                            iVar12 = iVar12 + 1;
                        } while (((iVar12 * 0x10000) >> 0x10) < (int)sStack_4c);
                    }
                }
            }
            else
            {
                iVar12 = 0;
                if (uVar5 == 2)
                {
                    iVar6 = 0;
                    do
                    {
                        iVar6 = iVar6 >> 0x10;
                        if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar6 * 4 + 2) == uStack_4e)
                        {
                            puVar11 = AnimVm.DAT_801f2180 +
                                      (int)((uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + iVar6 * 4) >> 0x18) * 0x20;
                            sVar3 = (short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar6 * 2);
                            iVar6 = 0;
                            if (0 < sVar3)
                            {
                                do
                                {
                                    iVar10 = 0;
                                    psVar8 = puVar11 + 4;
                                    uStack_46 = uVar2;
                                    do
                                    {
                                        puVar11 = puVar11 + 8;
                                        sVar4 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar8),
                                            (ushort)(uStack_46 & 0xf), (uint)(int)(short)auStack_40[iVar10]);
                                        PsxRam.WriteU16(psVar8, (ushort)sVar4);
                                        psVar8 = psVar8 + 8;
                                        iVar10 = iVar10 + 1;
                                        uStack_46 = (ushort)((short)uStack_46 >> 4);
                                    } while (((iVar10 * 0x10000) >> 0x10) < 4);

                                    iVar6 = iVar6 + 1;
                                } while (((iVar6 * 0x10000) >> 0x10) < (int)sVar3);
                            }

                            bVar1 = sStack_4c == 1;
                            sStack_4c = (short)(sStack_4c + -1);
                            if (bVar1)
                            {
                                return puVar13;
                            }
                        }

                        iVar12 = iVar12 + 1;
                        iVar6 = iVar12 * 0x10000;
                    } while (((iVar12 * 0x10000) >> 0x10) < 0x40);
                }
            }
        }

        return puVar13;
    }

    // GHIDRA: AnimCmd_BaseCulP @ 0x8003BF2C (VS.EXE)
    // Opcode 49, which the image's name table calls `base_culP`. Note the gap: this body sits
    // immediately after base_culZ in memory, yet its dispatch slot is 49, not 29 — reached through
    // g_animStreamDispatchTable[49] @ 0x800823B8, while slot 29 is `movexp_set` @ 0x8003C3C0.
    //
    // base_culZ with the field cursor three shorts in: the short at +6 of each 8-byte sub-record,
    // the fourth and last component of the record. What that fourth component is stays open — see
    // the PARTIAL on AnimVm.DAT_801f2180 — so the name is left as the image and Ghidra both write it.
    internal static int AnimCmd_BaseCulP(int streamPtr)
    {
        bool bVar1;
        ushort uVar2;
        short sVar3;
        short sVar4;
        uint uVar5;
        int iVar6;
        uint uVar7;
        int psVar8;
        int iVar9;
        int iVar10;
        int puVar11;
        int iVar12;
        int puVar13;
        ushort uStack_4e;
        short sStack_4c;
        ushort uStack_46;
        Span<ushort> auStack_40 = stackalloc ushort[12];

        uVar7 = (uint)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        uVar5 = (uint)(((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10)) >> 0x18);
        uStack_4e = (ushort)uVar7;
        if ((uVar5 & 4) != 0)
        {
            uStack_4e = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)uVar7 * 2);
        }

        sStack_4c = (short)(sbyte)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        uVar2 = PsxRam.ReadU16(streamPtr + 4);
        puVar13 = streamPtr + 6;
        iVar12 = 0;
        uVar7 = uVar5;
        do
        {
            if ((uVar7 & 0x10) == 0)
            {
                auStack_40[iVar12] = PsxRam.ReadU16(puVar13);
            }
            else
            {
                auStack_40[iVar12] = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(puVar13) * 2);
            }

            puVar13 = puVar13 + 2;
            uVar7 = (uint)(((int)(uVar7 << 0x10)) >> 0x11);
            iVar12 = iVar12 + 1;
        } while (((iVar12 * 0x10000) >> 0x10) < 4);

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar5 = uVar5 & 3;
            if (uVar5 == 1)
            {
                iVar12 = (int)(short)uStack_4e;
                puVar11 = AnimVm.DAT_801f2180 + PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar12 * 4 + 3) * 0x20;
                if (iVar12 < iVar12 + sStack_4c)
                {
                    do
                    {
                        iVar6 = (int)(short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (((iVar12 << 0x10) >> 0xf)));
                        iVar10 = 0;
                        if (0 < iVar6)
                        {
                            do
                            {
                                iVar9 = 0;
                                psVar8 = puVar11 + 6;
                                uStack_46 = uVar2;
                                do
                                {
                                    puVar11 = puVar11 + 8;
                                    sVar3 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar8),
                                        (ushort)(uStack_46 & 0xf), (uint)(int)(short)auStack_40[iVar9]);
                                    PsxRam.WriteU16(psVar8, (ushort)sVar3);
                                    psVar8 = psVar8 + 8;
                                    iVar9 = iVar9 + 1;
                                    uStack_46 = (ushort)((short)uStack_46 >> 4);
                                } while (((iVar9 * 0x10000) >> 0x10) < 4);

                                iVar10 = iVar10 + 1;
                            } while (((iVar10 * 0x10000) >> 0x10) < iVar6);
                        }

                        iVar12 = iVar12 + 1;
                    } while (((iVar12 * 0x10000) >> 0x10) < (int)(short)uStack_4e + (int)sStack_4c);
                }
            }
            else if (uVar5 < 2)
            {
                if (uVar5 == 0)
                {
                    iVar12 = 0;
                    puVar11 = AnimVm.DAT_801f2180 + (short)uStack_4e * 0x20;
                    if (0 < sStack_4c)
                    {
                        do
                        {
                            iVar6 = 0;
                            psVar8 = puVar11 + 6;
                            uStack_46 = uVar2;
                            do
                            {
                                puVar11 = puVar11 + 8;
                                sVar3 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar8),
                                    (ushort)(uStack_46 & 0xf), (uint)(int)(short)auStack_40[iVar6]);
                                PsxRam.WriteU16(psVar8, (ushort)sVar3);
                                psVar8 = psVar8 + 8;
                                iVar6 = iVar6 + 1;
                                uStack_46 = (ushort)((short)uStack_46 >> 4);
                            } while (((iVar6 * 0x10000) >> 0x10) < 4);

                            iVar12 = iVar12 + 1;
                        } while (((iVar12 * 0x10000) >> 0x10) < (int)sStack_4c);
                    }
                }
            }
            else
            {
                iVar12 = 0;
                if (uVar5 == 2)
                {
                    iVar6 = 0;
                    do
                    {
                        iVar6 = iVar6 >> 0x10;
                        if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar6 * 4 + 2) == uStack_4e)
                        {
                            puVar11 = AnimVm.DAT_801f2180 +
                                      (int)((uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + iVar6 * 4) >> 0x18) * 0x20;
                            sVar3 = (short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar6 * 2);
                            iVar6 = 0;
                            if (0 < sVar3)
                            {
                                do
                                {
                                    iVar10 = 0;
                                    psVar8 = puVar11 + 6;
                                    uStack_46 = uVar2;
                                    do
                                    {
                                        puVar11 = puVar11 + 8;
                                        sVar4 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar8),
                                            (ushort)(uStack_46 & 0xf), (uint)(int)(short)auStack_40[iVar10]);
                                        PsxRam.WriteU16(psVar8, (ushort)sVar4);
                                        psVar8 = psVar8 + 8;
                                        iVar10 = iVar10 + 1;
                                        uStack_46 = (ushort)((short)uStack_46 >> 4);
                                    } while (((iVar10 * 0x10000) >> 0x10) < 4);

                                    iVar6 = iVar6 + 1;
                                } while (((iVar6 * 0x10000) >> 0x10) < (int)sVar3);
                            }

                            bVar1 = sStack_4c == 1;
                            sStack_4c = (short)(sStack_4c + -1);
                            if (bVar1)
                            {
                                return puVar13;
                            }
                        }

                        iVar12 = iVar12 + 1;
                        iVar6 = iVar12 * 0x10000;
                    } while (((iVar12 * 0x10000) >> 0x10) < 0x40);
                }
            }
        }

        return puVar13;
    }

    // ===================================================================================
    // Shared VM helpers — see the OWNERSHIP CAVEAT in the file header
    // ===================================================================================

    // GHIDRA: FUN_8003f228 @ 0x8003F228 (VS.EXE)
    // Turns a command's target selector into the PSX address of a translation vector, or 0. Twenty
    // call sites across the overlay; two of them are in this file (trans_set, twice) and the rest
    // belong to other handler families.
    //
    // Three modes, in the order the original tests them:
    //   bit 4 set   : the shared bank, 0x801F2080 + (sel & 0xf) * 8.
    //   bit 5 set   : DAT_801faaac[sel & 0xf] + 0x3C, or 0 when that pointer is null.
    //   otherwise   : the task context's slot table, *(int *)(ctx + 0x18 + sel * 4) + 0x114, or 0
    //                 when the selector is above 5 or the slot is null.
    //
    // The selector arrives sign-extended from a char and the `5 < (short)param_1` test is signed,
    // so a negative selector passes it and indexes the slot table backwards. Rule 12: kept.
    internal static int FUN_8003f228(uint param_1, int param_2)
    {
        int iVar1;
        int puVar2;

        if ((param_1 & 0x10) == 0)
        {
            if ((param_1 & 0x20) == 0)
            {
                if (5 < (short)param_1)
                {
                    puVar2 = 0;
                }
                else
                {
                    iVar1 = PsxRam.ReadI32((short)param_1 * 4 + param_2 + 0x18);
                    puVar2 = iVar1 + 0x114;
                    if (iVar1 == 0)
                    {
                        puVar2 = 0;
                    }
                }
            }
            else
            {
                puVar2 = PsxRam.ReadI32(DAT_801faaac + (int)(param_1 & 0xf) * 4) + 0x3c;
                if (PsxRam.ReadI32(DAT_801faaac + (int)(param_1 & 0xf) * 4) == 0)
                {
                    puVar2 = 0;
                }
            }
        }
        else
        {
            puVar2 = (int)(AnimVm.UNK_801f2080 + (param_1 & 0xf) * 8);
        }

        return puVar2;
    }

    // GHIDRA: FUN_8003f2b0 @ 0x8003F2B0 (VS.EXE)
    // The same job for rotation vectors. Twelve call sites, two of them here (rotate_set, twice).
    //
    //   bit 4 set          : the shared bank, 0x801F2000 + (sel & 0xf) * 8.
    //   selector < 6       : *(int *)(ctx + 0x18 + sel * 4) + 0x11C, or 0 when that slot is null.
    //   selector >= 6      : the scratchpad triple at 0x1F800084. Note it CANNOT return 0 on this
    //                        branch, which is why rotate_set's missing null check does not always
    //                        bite — but the middle branch can, and rotate_set does not check it.
    internal static int FUN_8003f2b0(uint param_1, int param_2)
    {
        int iVar1;
        int puVar2;

        if ((param_1 & 0x10) == 0)
        {
            if ((short)param_1 < 6)
            {
                iVar1 = PsxRam.ReadI32((short)param_1 * 4 + param_2 + 0x18);
                puVar2 = iVar1 + 0x11c;
                if (iVar1 == 0)
                {
                    puVar2 = 0;
                }
            }
            else
            {
                puVar2 = DAT_1f800084;
            }
        }
        else
        {
            puVar2 = (int)(AnimVm.DAT_801f2000 + (param_1 & 0xf) * 8);
        }

        return puVar2;
    }

    // GHIDRA: FUN_8003f540 @ 0x8003F540 (VS.EXE)
    // THE OPERATOR DISPATCHER OF THE WHOLE SCRIPT VM: 384 bytes, 42 call sites, every command
    // family. param_2 is the 4-bit operator code a command carries per component, param_1 the
    // current value, param_3 the operand. The result is always sign-extended back to 16 bits.
    //
    //   0 set   1 add   2 sub   3 or   4 and   5 xor   6 mul   7 div
    //   9 reverse-sub   10 store-to-var-table   11 add(rand & operand)   12 mod
    //
    // 8 and any unlisted code fall through to `switchD_8003f580_caseD_8` and return param_1
    // unchanged; the callers give operator 8 its own meaning by testing for it themselves, which is
    // why 8 must be a no-op here and is left as one.
    //
    // The `goto LAB_8003f6a4` shape is the original's and is kept literally: case 6 reaches the
    // common tail with a DIFFERENT value in iVar3 than the fall-through path computes.
    internal static int FUN_8003f540(uint param_1, ushort param_2, uint param_3)
    {
        short sVar1;
        uint uVar2;
        int iVar3;

        sVar1 = (short)param_1;
        switch (param_2)
        {
            case 0:
                param_1 = param_3;
                break;
            case 1:
                param_1 = param_1 + param_3;
                break;
            case 2:
                param_1 = param_1 - param_3;
                break;
            case 3:
                param_1 = param_1 | param_3;
                break;
            case 4:
                param_1 = param_1 & param_3;
                break;
            case 5:
                param_1 = param_1 ^ param_3;
                break;
            case 6:
                iVar3 = (int)(param_1 * param_3) * 0x10000;
                goto LAB_8003f6a4;
            case 7:
                // The original divides and then checks: a zero divisor falls into `break 0x1C00`
                // and a MIN/-1 pair into `break 0x1800`, both of which halt the console. C#'s
                // DivideByZeroException is the same halt reached one instruction earlier, and the
                // MIN/-1 trap is unreachable anyway because sVar1 is a short. Rule 12: the
                // original's abort is not softened into a guard.
                param_1 = (uint)((int)sVar1 / (int)(short)param_3);
                break;
            case 9:
                param_1 = param_3 - param_1;
                break;
            case 10:
                PsxRam.WriteU16(AnimVm.g_animSharedVarTable + (((int)(param_3 << 0x10)) >> 0xf), (ushort)sVar1);
                iVar3 = (int)(param_1 << 0x10);
                goto LAB_8003f6a4;
            case 0xb:
                uVar2 = (uint)Kernel.rand();
                param_1 = param_1 + (param_3 & uVar2);
                break;
            case 0xc:
                param_1 = (uint)((int)sVar1 % (int)(short)param_3);
                break;
        }

        iVar3 = (int)(param_1 << 0x10);
    LAB_8003f6a4:
        return iVar3 >> 0x10;
    }
}
