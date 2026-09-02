using System;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's animation-script VM, mesh / table / movement family: eight of the fifty-one handlers
// reached through g_animStreamDispatchTable @ 0x800822F4.
//
// THE INTERPRETER (not in this file — ExecuteAnimStreamBatch @ 0x80036768) is a threaded one:
//
//     while (uVar1 != 0) {
//         puVar2 = (*(code *)(&g_animStreamDispatchTable)[*puVar2 & 0xff])(puVar2, iVar6 >> 0x10);
//         uVar1 = *puVar2;
//     }
//
// so the opcode is the low byte of the command's first halfword, every handler RETURNS THE ADDRESS
// of the next command, and a zero halfword ends the stream. Every handler is called with two
// arguments; only AnimCmd_RenderEntryGroup reads the second one. The C# methods therefore all take
// `(int streamPtr, int arg)` and return an `int` address, and the ones whose bodies never touch
// `arg` say so on the parameter.
//
// THE OPCODE NAMES BELOW ARE THE BINARY'S OWN. VS.EXE carries a table of fifty 16-byte ASCII names
// at 0x800823C0, one per dispatch slot, and it was read out of the image for this slice rather than
// assumed:
//
//     slot  2  "table_set "   -> 0x800373A0        slot 18  "x_max_set "   -> 0x8003A034
//     slot  3  "load_set "    -> 0x80037E30        slot 29  "movexp_set "  -> 0x8003C3C0
//     slot  9  "cul_set "     -> 0x80038720        slot 30  "dist_set "    -> 0x8003C4E4
//     slot 16  "x_add_set "   -> 0x80039C18        slot 31  "move_set "    -> 0x8003C5E4
//
// The dispatch table holds fifty-one pointers and the name table fifty entries; slot 50
// (0x8003EF04) is unnamed. Slots 0, 4 and 36 all point at 0x80037374 and are all named "dummy".
//
// ------------------------------------------------------------------------------------------------
// THE OPCODE 9 NAME DISCORDANCE, DECIDED ON THE BODY
// ------------------------------------------------------------------------------------------------
// Ghidra carries 0x80038720 as `AnimCmd_SetMeshPaletteRange`. The image calls slot 9 `cul_set`.
// The two do not say the same thing, and the evidence is one-sided: NOTHING in the body touches a
// CLUT, a palette, or a colour table.
//
// What the body actually does, statement by statement: it decodes three 5/6-bit fields out of
// word3 into three operand slots, resolves them through FUN_8003f228 (0x801F2080 table),
// FUN_8003f2b0 (0x801F2000 table) and a direct index into the 0x801F2100 table, finds the first of
// sixty-four AnimVm.g_renderMetadataBuffer entries whose byte at +2 matches the command's low nibble, and
// hands the whole lot to FUN_8003f6c0 @ 0x8003F6C0.
//
// FUN_8003f6c0 IS A GTE TRANSFORM, and it is called from exactly one site — this handler. Its body:
// PushMatrix, ReadRotMatrix(&DAT_1f800000), RotMatrix(param_3, ...), a translation loaded from
// param_4[0..2], ScaleMatrix with param_5[0..2], CompMatrix, SetRotMatrix, SetTransMatrix, then per
// primitive RotAverage4 or RotAverage3 (chosen on the vertex quad's `pad` field), storing the
// resulting OTZ into param_6[i] and forcing 0 to 0x801, then PopMatrix.
//
// So the three operand tables are rotation (0x801F2000), translation (0x801F2080) and scale
// (0x801F2100) — sixteen 8-byte slots each, laid out back to back and ending exactly where the
// vertex array 0x801F2180 begins — and the short array at 0x801FA580 that this handler then biases
// by word2 is the per-primitive ORDERING-TABLE Z, not a palette index.
//
// VERDICT: the evidence supports `cul_set` and refutes `SetMeshPaletteRange`. The `GHIDRA:` line
// below still states the symbol the project database holds, because that is the rule; the C#
// method is named from the image's own table. The sibling opcodes 26/27/28/49 — `base_culX`,
// `base_culY`, `base_culZ`, `base_culP` — read the same way: `cul` here is a coordinate/calculation
// family, not culling and not colour. Ghidra was NOT relabelled: this slice is read-only on the
// Ghidra side.
//
// ------------------------------------------------------------------------------------------------
// WHAT WAS REUSED RATHER THAN REDONE
// ------------------------------------------------------------------------------------------------
// Five of the eight already carried names and CERTAIN annotations from earlier sessions, and those
// carry the proof. They are quoted where they apply and the names are kept unchanged:
//   0x800373A0  AnimCmd_RenderEntryGroup   CERTAIN, word1 bit map closed against RenderBattleScene3D
//   0x80037E30  AnimCmd_LoadTexture        CERTAIN, "homologous to GAME.EXE AnimCmd_LoadTexture"
//   0x80039C18  AnimCmd_XAddSet            CERTAIN, "homologous to GAME.EXE AnimCmd_XAddSet"
//   0x8003A034  AnimCmd_XMaxSet            CERTAIN, "homologous to GAME.EXE AnimCmd_XMaxSet"
//   0x80038720  AnimCmd_SetMeshPaletteRange  (the discordance above)
// The remaining three — 0x8003C3C0, 0x8003C4E4, 0x8003C5E4 — are not functions in Ghidra at all;
// only the decompiler's temporary preview names them, and the `GHIDRA:` lines say so.
//
// ------------------------------------------------------------------------------------------------
// MEMORY MODEL
// ------------------------------------------------------------------------------------------------
// The command stream is raw PSX memory walked by pointer, and every handler must hand the
// interpreter back an ADDRESS it will dereference again. So a stream pointer here is an `int` PSX
// address and every access goes through PsxRam, never through a copied ushort[].
//
// The whole render workspace is ONE contiguous block, and the original says so itself:
// AnimCmd_RenderEntryGroup's `bzero(&AnimVm.DAT_801f2000, 0x8c48)` clears 0x801F2000..0x801FAC47, and
// every global this family touches — the three operand tables, the vertex arrays, the POLY_GT4
// packets, the OTZ array, the six per-entry buffers, the flush flag and the shared variable table —
// falls inside it. It is declared once, as one byte[], through LibGpu.RamRegion so that the
// POLY_GT4 packets built at 0x801F7180 can later be followed out of an ordering table by their real
// PSX addresses.
//
// OWNERSHIP CAVEAT, stated rather than hidden, the same way VS_EXE/FileIo.cs states its scratchpad
// one: this slice is the first VS.EXE code to need the workspace, so it is declared here. The three
// operand tables at 0x801F2000/0x801F2080/0x801F2100 and the shared variable table at 0x801FAA64
// are read by handlers in every other family too; when those land, the block belongs in a
// VS_EXE/AnimWorkspace.cs beside them, moved as it is.
//
// WIRING GAP, and it is real: PsxSdkBridges installs PsxRam.AddressResolver per overlay and has no
// ActivateVsExe yet — there is no VS_EXE_exe.ResolveAddress for it to install. `Resolve` below is
// this file's row, shaped exactly like TITLE_EXE_exe's per-module `Resolve` chain so the overlay
// resolver can chain it when it lands. Until then these reads and writes resolve to nothing and
// answer zero. Fixing that means editing PsxSdkBridges and VS_EXE_exe, which are not this file.
//
// The stream itself is NOT in this block: it lives in FileIo.g_cdFileBufferTable @ 0x801D2000,
// which the same overlay resolver must also answer for.
internal static class AnimCmdMesh
{
    // AnimVm.DAT_801f2000, AnimVm.UNK_801f2080, AnimVm.DAT_801f2100, AnimVm.DAT_801f2180, AnimVm.DAT_801f7180, AnimVm.g_renderMetadataBuffer,
    // AnimVm.g_meshCountBuffer, AnimVm.g_meshStreamPtrBuffer, AnimVm.g_meshOffsetBuffer, AnimVm.g_meshXOffsetBuffer,
    // AnimVm.g_animSharedVarTable and AnimVm.DAT_800b305a are the VM's SHARED globals; they are declared once in
    // AnimVm.cs — including the AnimVm.RAM_801f2000 backing region and the address Resolve chain, which
    // AnimVm.cs now owns instead of this file — and reached here as AnimVm.<name>. See AnimVm.cs
    // for the merged proof comments.

    // GHIDRA: DAT_801f4180 @ 0x801F4180 (VS.EXE)
    // Ghidra types it undefined4. Four colour words per primitive, staged here and then copied into
    // the POLY_GT4's four RGB fields.
    private const int DAT_801f4180 = unchecked((int)0x801F4180);

    // GHIDRA: DAT_801f5180 @ 0x801F5180 (VS.EXE)
    // Ghidra types it undefined2. The second vertex quad, same 0x20-byte stride as AnimVm.DAT_801f2180.
    private const int DAT_801f5180 = unchecked((int)0x801F5180);

    // GHIDRA: DAT_801fa580 @ 0x801FA580 (VS.EXE)
    // Ghidra types it undefined. The per-primitive ordering-table Z, one short each: FUN_8003f6c0
    // writes it from RotAverage3/4 and AnimCmd_CulSet then biases a run of it by word2.
    private const int DAT_801fa580 = unchecked((int)0x801FA580);

    // GHIDRA: g_meshEntryFlagsHiBuf @ 0x801FA800 (VS.EXE)
    // Sixty-four shorts, filled from the entry's dword0 high half. AnimCmd_XAddSet clamps against
    // it; AnimCmd_XMaxSet is the only writer that computes into it.
    private const int g_meshEntryFlagsHiBuf = unchecked((int)0x801FA800);

    // GHIDRA: g_renderFlushFlag @ 0x801FAA60 (VS.EXE)
    // Armed to 1 by word1 bit 2 of the table_set command.
    private const int g_renderFlushFlag = unchecked((int)0x801FAA60);

    // GHIDRA: DAT_801faa84 @ 0x801FAA84 (VS.EXE)
    // Ghidra types it undefined4. AnimCmd_MoveSet reads its low half as the X step and tests the
    // whole word against zero; AnimCmd_MovexpSet passes its ADDRESS to FUN_80047550, so the three
    // below are consumed as a triple.
    private const int DAT_801faa84 = unchecked((int)0x801FAA84);

    // GHIDRA: DAT_801faa88 @ 0x801FAA88 (VS.EXE)
    private const int DAT_801faa88 = unchecked((int)0x801FAA88);

    // GHIDRA: DAT_801faa8c @ 0x801FAA8C (VS.EXE)
    private const int DAT_801faa8c = unchecked((int)0x801FAA8C);

    // GHIDRA: g_cdFileBufferTable @ 0x801D2000 (VS.EXE)
    // Ghidra types it undefined4, so `(&g_cdFileBufferTable)[i]` is the word at +i*4. Slots 2..5 of
    // the CH.BIN header hold the group table pointers AnimCmd_RenderEntryGroup selects between, and
    // AnimCmd_LoadTexture takes an image pointer out of the same table.
    //
    // The BYTES behind this address are FileIo.g_cdFileBufferTable, declared by tranche 0 and NOT
    // redeclared here; only the address constant is repeated, because this file addresses it rather
    // than indexing the array.
    private const int g_cdFileBufferTable = unchecked((int)0x801D2000);

    // GHIDRA: DAT_801d2004 @ 0x801D2004 (VS.EXE)
    // Ghidra types it undefined2. The docs close 0x801D2004 as the CH.BIN header's entry_count
    // (docs/structure-ch-bin-files.md names it g_meshTableCounts).
    //
    // PARTIAL, and left exactly as the original computes it: AnimCmd_RenderEntryGroup reads the
    // GROUP POINTER at 0x801D2000 + index*4 and the COUNT at 0x801D2004 + index*4, so for index i
    // the count read lands on the low half of the word that is pointer i+1. The two reads overlap.
    // Rule 12 — this is transliterated, not corrected.
    private const int DAT_801d2004 = unchecked((int)0x801D2004);

    // GHIDRA: g_cdFileBaseOffset @ 0x8008D26C (VS.EXE)
    // Added to every pointer read out of a loaded CH.BIN, which stores them as file-relative.
    internal static int g_cdFileBaseOffset;

    // =====================================================================================
    // OPCODE 2 — `table_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_RenderEntryGroup @ 0x800373A0 (VS.EXE)
    // Opcode 2, which the image's opcode-name table @ 0x800823C0 calls `table_set`.
    //
    // Ghidra's own pre-comment on the function, kept verbatim because it carries the proof:
    //   "CERTAIN: opcode 0x02 handler. word1.high8 selects header slot 2..5 via
    //    (&g_cdFileBufferTable)[index] and (&g_meshTableCounts)[index*2]. word1.low8.bit0 clears
    //    temporary render buffers before overlay, bit1 selects the destination stream slot by
    //    scanning AnimVm.g_meshStreamPtrBuffer, bit2 arms g_renderFlushFlag. The handler overlays the
    //    selected CHBinMeshEntry group into the same derived buffers as RenderBattleScene3D and
    //    does not read CHBinMeshEntry.unknown_0x08 (+0x08)."
    //
    // The entry stride is 7 words: the outer loop advances puStack_98 by 7 and puVar12 (= entry+1)
    // by 7, and puStack_98[2] is never read — which is the "+0x08 unread" the comment states.
    //
    // This is the ONLY handler in the family that reads the interpreter's second argument.
    // Ghidra types it `short groupIndex`; it arrives as `iVar6 >> 0x10`, already sign-extended.
    internal static int AnimCmd_RenderEntryGroup(int streamPtr, int groupIndex)
    {
        int pbVar1;
        byte bVar2;
        ushort uVar3;
        short sVar4;
        ushort uVar5;
        uint uVar6;
        uint uVar7;
        uint uVar8;
        uint uVar9;
        uint uVar10;
        int puVar11;
        int puVar12;
        int p;
        int puVar13;
        int puVar14;
        int puVar15;
        int puVar16;
        int puVar17;
        int iVar18;
        int iVar19;
        int puVar20;
        int puVar21;
        ushort uVar22;
        byte uStack_d8;
        byte uStack_d6;
        byte uStack_d4;
        byte uStack_d2;
        int piStack_d0;
        int puStack_cc;
        int piStack_c8;
        int pbStack_c4;
        int piStack_c0;
        int puStack_bc;
        ushort uStack_a8;
        ushort uStack_a0;
        int puStack_98;
        int piStack_60;
        int psStack_58;
        int puStack_50;

        uStack_a0 = (ushort)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        uVar3 = PsxRam.ReadU16(streamPtr + 2);
        uVar6 = AnimVm.DAT_800b305a;
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            // (&g_cdFileBufferTable)[uVar3 >> 8] — undefined4 element, so +index*4.
            puStack_98 = PsxRam.ReadI32(g_cdFileBufferTable + (uVar3 >> 8) * 4);
            // (&DAT_801d2004)[(uVar3 >> 8) * 2] — undefined2 element, so +index*2*2. See the
            // overlap note on DAT_801d2004.
            sVar4 = (short)PsxRam.ReadU16(DAT_801d2004 + (int)(uint)(uVar3 >> 8) * 2 * 2);
            if ((uVar3 & 1) != 0)
            {
                // bzero(&AnimVm.DAT_801f2000, 0x8c48) — the original's own workspace extent, a
                // subrange of AnimVm.RAM_801f2000 now that the backing region is shared.
                Array.Clear(AnimVm.RAM_801f2000, 0, 0x8C48);
            }

            p = AnimVm.DAT_801f7180;
            if ((uVar3 & 4) != 0)
            {
                PsxRam.WriteI32(g_renderFlushFlag, 1);
            }

            puVar21 = AnimVm.DAT_801f2180;
            puVar16 = DAT_801f4180;
            puVar20 = DAT_801f5180;
            piStack_60 = AnimVm.g_renderMetadataBuffer;
            psStack_58 = AnimVm.g_meshCountBuffer;
            puStack_50 = AnimVm.g_meshOffsetBuffer;
            puVar11 = AnimVm.g_meshStreamPtrBuffer;
            uStack_a8 = 0;
            if ((int)(uVar6 & 1) < (int)sVar4)
            {
                puVar12 = puStack_98 + 4;
                do
                {
                    uVar8 = uStack_a8;
                    uVar6 = (uint)PsxRam.ReadI32(puStack_98);
                    PsxRam.WriteU16(AnimVm.g_meshXOffsetBuffer + (int)uVar8 * 2, 0);
                    PsxRam.WriteU16(g_meshEntryFlagsHiBuf + (int)uVar8 * 2, (ushort)(short)(uVar6 >> 0x10));
                    PsxRam.WriteI32(piStack_60,
                        unchecked((int)(uVar8 + (uVar6 & 0xffff) * 0x100 + (uint)uStack_a0 * 0x1000000)));
                    piStack_c8 = PsxRam.ReadI32(puVar12 + 3 * 4) + g_cdFileBaseOffset;
                    piStack_d0 = PsxRam.ReadI32(puVar12 + 2 * 4) + g_cdFileBaseOffset;
                    puStack_cc = PsxRam.ReadI32(piStack_d0) + g_cdFileBaseOffset;
                    uVar6 = (uint)PsxRam.ReadI32(piStack_d0 + 1 * 4);
                    piStack_60 = piStack_60 + 4;
                    pbStack_c4 = PsxRam.ReadI32(piStack_c8) + g_cdFileBaseOffset;
                    uVar8 = (uint)PsxRam.ReadI32(piStack_c8 + 3 * 4);
                    piStack_c0 = PsxRam.ReadI32(puVar12 + 4 * 4) + g_cdFileBaseOffset;
                    puStack_bc = PsxRam.ReadI32(piStack_c0) + g_cdFileBaseOffset;
                    uVar9 = (uint)PsxRam.ReadI32(piStack_c0 + 1 * 4);
                    iVar19 = PsxRam.ReadI32(piStack_c8 + 1 * 4) + g_cdFileBaseOffset;
                    uVar7 = (uint)PsxRam.ReadI32(puVar12 + 5 * 4);
                    iVar18 = PsxRam.ReadI32(piStack_c8 + 2 * 4) + g_cdFileBaseOffset;
                    if (uVar7 != 0)
                    {
                        // The first store is dead — the original overwrites it on the very next
                        // line. Rule 12: kept.
                        PsxRam.WriteI32(puVar11, unchecked((int)uVar7));
                        PsxRam.WriteI32(puVar11, unchecked((int)uVar7) + g_cdFileBaseOffset);
                        if ((uVar3 & 2) != 0)
                        {
                            // word1 bit 1: pick the destination stream slot by scanning
                            // AnimVm.g_meshStreamPtrBuffer. puStack_50 is rewound with it, but the scan
                            // only advances puVar11 — the two come out of the loop out of step.
                            // Rule 12: transliterated, not corrected.
                            puVar11 = AnimVm.g_meshStreamPtrBuffer;
                            puStack_50 = AnimVm.g_meshOffsetBuffer;
                            uVar22 = 0;
                            uVar7 = 0;
                            do
                            {
                                uVar22 = (ushort)(uVar22 + 1);
                                // `(int)groupIndex != uVar7` in C: the int is converted to
                                // unsigned for the comparison.
                                if ((uint)(short)groupIndex != uVar7)
                                {
                                    if (PsxRam.ReadI32(puVar11) == 0)
                                    {
                                        break;
                                    }

                                    puVar11 = puVar11 + 4;
                                }

                                uVar7 = uVar22;
                            } while (uVar22 < 0x10);
                        }

                        uVar7 = (uint)PsxRam.ReadI32(puVar11);
                        PsxRam.WriteI32(puVar11, unchecked((int)uVar7) + 2);
                        PsxRam.WriteU16(puStack_50, PsxRam.ReadU16(unchecked((int)uVar7) + 2));
                        puStack_50 = puStack_50 + 2;
                        PsxRam.WriteI32(puVar11, PsxRam.ReadI32(puVar11) + 2);
                        puVar11 = puVar11 + 4;
                    }

                    uVar7 = 0;
                    if (0 < (short)PsxRam.ReadU16(puVar12))
                    {
                        // &p->v3: POLY_GT4.v3 is the byte at +0x31.
                        puVar15 = p + 0x31;
                        // puVar20 / puVar21 are undefined2*, so "+ 2" is +4 bytes.
                        puVar14 = puVar20 + 2 * 2;
                        puVar13 = puVar21 + 2 * 2;
                        do
                        {
                            bVar2 = PsxRam.ReadU8(pbStack_c4 + 8);
                            PsxRam.WriteU16(puVar13 + 1 * 2, bVar2);
                            if (bVar2 == 0)
                            {
                                // SetPolyGT4 / SetPolyGT3 / SetShadeTex / SetSemiTrans take
                                // (buffer, offset); p is an address inside this workspace.
                                LibGpu.SetPolyGT4(AnimVm.RAM_801f2000, p - AnimVm.DAT_801f2000);
                                uVar22 = 0;
                            }
                            else
                            {
                                LibGpu.SetPolyGT3(AnimVm.RAM_801f2000, p - AnimVm.DAT_801f2000);
                                uVar22 = 0;
                            }

                            do
                            {
                                puVar17 = puVar16;
                                uVar10 = uVar6 & 0xffff;
                                uVar6 = (uVar6 >> 0x10) - 1;
                                PsxRam.WriteI32(puVar17, PsxRam.ReadI32(puStack_cc));
                                puStack_cc = puStack_cc + 4;
                                if ((uVar6 & 0xffff) == 0)
                                {
                                    uVar10 = uVar10 - 1;
                                    if ((uVar10 & 0xffff) == 0)
                                    {
                                        puStack_cc = PsxRam.ReadI32(piStack_d0 + 2 * 4) + g_cdFileBaseOffset;
                                        uVar10 = (uint)PsxRam.ReadI32(piStack_d0 + 3 * 4);
                                        uVar6 = uVar10 >> 0x10;
                                        piStack_d0 = piStack_d0 + 2 * 4;
                                    }
                                    else
                                    {
                                        puStack_cc = PsxRam.ReadI32(piStack_d0) + g_cdFileBaseOffset;
                                        uVar6 = PsxRam.ReadU16(piStack_d0 + 6);
                                    }
                                }

                                uVar6 = uVar6 * 0x10000 + (uint)(int)(short)uVar10;
                                uVar22 = (ushort)(uVar22 + 1);
                                puVar16 = puVar17 + 4;
                            } while (uVar22 < 4);

                            PsxRam.WriteI32(puVar15 - 0x2d, PsxRam.ReadI32(puVar17 - 3 * 4));
                            // A literal self-assignment in the original, at POLY_GT4.code. Rule 12.
                            PsxRam.WriteU8(puVar15 - 0x2a, PsxRam.ReadU8(puVar15 - 0x2a));
                            PsxRam.WriteI32(puVar15 - 0x21, PsxRam.ReadI32(puVar17 - 2 * 4));
                            PsxRam.WriteI32(puVar15 - 0x15, PsxRam.ReadI32(puVar17 - 1 * 4));
                            PsxRam.WriteI32(puVar15 - 9, PsxRam.ReadI32(puVar17));
                            LibGpu.SetShadeTex(AnimVm.RAM_801f2000, p - AnimVm.DAT_801f2000, 0);
                            LibGpu.SetSemiTrans(AnimVm.RAM_801f2000, p - AnimVm.DAT_801f2000, 1);
                            PsxRam.WriteU16(puVar21, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4) * 6 + iVar19));
                            PsxRam.WriteU16(puVar13 - 1 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4) * 6 + iVar19 + 2));
                            PsxRam.WriteU16(puVar13, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4) * 6 + iVar19 + 4));
                            PsxRam.WriteU16(puVar21 + 4 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 1) * 6 + iVar19));
                            PsxRam.WriteU16(puVar13 + 3 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 1) * 6 + iVar19 + 2));
                            PsxRam.WriteU16(puVar13 + 4 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 1) * 6 + iVar19 + 4));
                            PsxRam.WriteU16(puVar21 + 8 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 2) * 6 + iVar19));
                            PsxRam.WriteU16(puVar13 + 7 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 2) * 6 + iVar19 + 2));
                            PsxRam.WriteU16(puVar13 + 8 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 2) * 6 + iVar19 + 4));
                            PsxRam.WriteU16(puVar21 + 0xc * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 3) * 6 + iVar19));
                            PsxRam.WriteU16(puVar13 + 0xb * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 3) * 6 + iVar19 + 2));
                            PsxRam.WriteU16(puVar13 + 0xc * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 3) * 6 + iVar19 + 4));
                            PsxRam.WriteU16(puVar20, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 4) * 6 + iVar18));
                            PsxRam.WriteU16(puVar14 - 1 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 4) * 6 + iVar18 + 2));
                            PsxRam.WriteU16(puVar14, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 4) * 6 + iVar18 + 4));
                            PsxRam.WriteU16(puVar20 + 4 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 5) * 6 + iVar18));
                            PsxRam.WriteU16(puVar14 + 3 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 5) * 6 + iVar18 + 2));
                            puVar16 = puVar17 + 4;
                            PsxRam.WriteU16(puVar14 + 4 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 5) * 6 + iVar18 + 4));
                            puVar21 = puVar21 + 0x10 * 2;
                            PsxRam.WriteU16(puVar20 + 8 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 6) * 6 + iVar18));
                            puVar13 = puVar13 + 0x10 * 2;
                            PsxRam.WriteU16(puVar14 + 7 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 6) * 6 + iVar18 + 2));
                            uVar10 = uVar8 & 0xffff;
                            uVar8 = (uVar8 >> 0x10) - 1;
                            PsxRam.WriteU16(puVar14 + 8 * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 6) * 6 + iVar18 + 4));
                            PsxRam.WriteU16(puVar20 + 0xc * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 7) * 6 + iVar18));
                            PsxRam.WriteU16(puVar14 + 0xb * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbStack_c4 + 7) * 6 + iVar18 + 2));
                            pbVar1 = pbStack_c4 + 7;
                            puVar20 = puVar20 + 0x10 * 2;
                            pbStack_c4 = pbStack_c4 + 0xc;
                            PsxRam.WriteU16(puVar14 + 0xc * 2, PsxRam.ReadU16((int)PsxRam.ReadU8(pbVar1) * 6 + iVar18 + 4));
                            puVar14 = puVar14 + 0x10 * 2;
                            if ((uVar8 & 0xffff) == 0)
                            {
                                uVar10 = uVar10 - 1;
                                if ((uVar10 & 0xffff) == 0)
                                {
                                    pbStack_c4 = PsxRam.ReadI32(piStack_c8 + 4 * 4) + g_cdFileBaseOffset;
                                    uVar10 = (uint)PsxRam.ReadI32(piStack_c8 + 7 * 4);
                                    uVar8 = uVar10 >> 0x10;
                                    piStack_c8 = piStack_c8 + 4 * 4;
                                }
                                else
                                {
                                    pbStack_c4 = PsxRam.ReadI32(piStack_c8) + g_cdFileBaseOffset;
                                    uVar8 = PsxRam.ReadU16(piStack_c8 + 0xe);
                                }
                            }

                            uVar5 = PsxRam.ReadU16(puStack_bc + 1 * 2);
                            uStack_d4 = (byte)((sbyte)PsxRam.ReadU16(puStack_bc)
                                               + (sbyte)PsxRam.ReadU16(puStack_bc + 2 * 2));
                            uStack_d8 = (byte)PsxRam.ReadU16(puStack_bc);
                            uStack_d2 = (byte)((sbyte)PsxRam.ReadU16(puStack_bc + 1 * 2)
                                               + (sbyte)PsxRam.ReadU16(puStack_bc + 3 * 2));
                            PsxRam.WriteU8(puVar15 - 0x25, uStack_d8);
                            uStack_d6 = (byte)uVar5;
                            PsxRam.WriteU8(puVar15 - 0x24, uStack_d6);
                            PsxRam.WriteU8(puVar15 - 0x19, uStack_d4);
                            PsxRam.WriteU8(puVar15 - 0x18, uStack_d6);
                            uVar8 = uVar8 * 0x10000 + (uint)(int)(short)uVar10;
                            PsxRam.WriteU8(puVar15 - 0xd, uStack_d8);
                            PsxRam.WriteU8(puVar15 - 0xc, uStack_d2);
                            PsxRam.WriteU8(puVar15 - 1, uStack_d4);
                            uVar10 = uVar9 & 0xffff;
                            PsxRam.WriteU8(puVar15, uStack_d2);
                            uVar9 = (uVar9 >> 0x10) - 1;
                            puStack_bc = puStack_bc + 4 * 2;
                            if ((uVar9 & 0xffff) == 0)
                            {
                                uVar10 = uVar10 - 1;
                                if ((uVar10 & 0xffff) == 0)
                                {
                                    puStack_bc = PsxRam.ReadI32(piStack_c0 + 2 * 4) + g_cdFileBaseOffset;
                                    uVar10 = (uint)PsxRam.ReadI32(piStack_c0 + 3 * 4);
                                    uVar9 = uVar10 >> 0x10;
                                    piStack_c0 = piStack_c0 + 2 * 4;
                                }
                                else
                                {
                                    puStack_bc = PsxRam.ReadI32(piStack_c0) + g_cdFileBaseOffset;
                                    uVar9 = PsxRam.ReadU16(piStack_c0 + 6);
                                }
                            }

                            uVar9 = uVar9 * 0x10000 + (uint)(int)(short)uVar10;
                            puVar15 = puVar15 + 0x34;
                            uVar7 = uVar7 + 1;
                            uStack_a0 = (ushort)(uStack_a0 + 1);
                            p = p + 0x34;
                        } while ((int)(uVar7 & 0xffff) < (int)(short)PsxRam.ReadU16(puVar12));
                    }

                    puStack_98 = puStack_98 + 7 * 4;
                    uStack_a8 = (ushort)(uStack_a8 + 1);
                    PsxRam.WriteU16(psStack_58, PsxRam.ReadU16(puVar12));
                    psStack_58 = psStack_58 + 2;
                    puVar12 = puVar12 + 7 * 4;
                } while ((int)(uint)uStack_a8 < (int)sVar4);
            }
        }

        return streamPtr + 2 * 2;
    }

    // =====================================================================================
    // OPCODE 3 — `load_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_LoadTexture @ 0x80037E30 (VS.EXE)
    // Opcode 3, which the image's opcode-name table calls `load_set`.
    //
    // Ghidra's own pre-comment, kept verbatim:
    //   "CERTAIN: opcode 0x03 handler, homologous to GAME.EXE AnimCmd_LoadTexture. Same fixed
    //    7-word format and same bit0 branch: raw LoadImage_ReturnTPageOrClutId(..., isClut=1) vs
    //    DecompressAndLoadImage(..., isClut=0)."
    //
    // Seven halfwords consumed, which is what "fixed 7-word format" means here: `return streamPtr + 7`.
    // Both callees are already ported in VS_EXE/FileIo.cs and are reused, not rewritten.
    internal static int AnimCmd_LoadTexture(int streamPtr, int arg)
    {
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            if (((int)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8) & 1) == 0)
            {
                FileIo.LoadImage_ReturnTPageOrClutId(
                    PsxRam.ReadI32(g_cdFileBufferTable + (short)PsxRam.ReadU16(streamPtr + 5 * 2) * 4),
                    PsxRam.ReadU16(streamPtr + 1 * 2),
                    PsxRam.ReadU16(streamPtr + 2 * 2),
                    (short)PsxRam.ReadU16(streamPtr + 3 * 2),
                    (short)PsxRam.ReadU16(streamPtr + 4 * 2),
                    1);
                LibGpu.DrawSync(0);
            }
            else
            {
                FileIo.DecompressAndLoadImage(
                    PsxRam.ReadI32(g_cdFileBufferTable + (short)PsxRam.ReadU16(streamPtr + 5 * 2) * 4),
                    PsxRam.ReadU16(streamPtr + 1 * 2),
                    PsxRam.ReadU16(streamPtr + 2 * 2),
                    (short)PsxRam.ReadU16(streamPtr + 3 * 2),
                    (short)PsxRam.ReadU16(streamPtr + 4 * 2),
                    0);
            }
        }

        return streamPtr + 7 * 2;
    }

    // =====================================================================================
    // OPCODE 9 — `cul_set` (Ghidra: AnimCmd_SetMeshPaletteRange) — see the verdict in the header
    // =====================================================================================

    // GHIDRA: AnimCmd_SetMeshPaletteRange @ 0x80038720 (VS.EXE)
    // Opcode 9, which the image's opcode-name table @ 0x800823C0 calls `cul_set`.
    //
    // THE NAME IN THE `GHIDRA:` LINE ABOVE IS THE ONE THE PROJECT DATABASE HOLDS, AND THE EVIDENCE
    // REFUTES IT. The C# name comes from the image. The full argument is in the file header; in one
    // line: this handler resolves a rotation slot, a translation slot and a scale slot, then calls
    // FUN_8003f6c0, which is a PushMatrix / RotMatrix / ScaleMatrix / CompMatrix / RotAverage
    // geometry pass writing per-primitive OTZ. No CLUT, no palette, no colour table is touched
    // anywhere in either body.
    //
    // Four halfwords consumed. The stream pointer is captured BEFORE the work, so every early
    // return still advances the stream by the same four halfwords.
    internal static int AnimCmd_CulSet(int streamPtr, int arg)
    {
        sbyte cVar1;
        ushort uVar2;
        ushort uVar3;
        int iVar4;
        uint uVar5;
        ushort uVar6;
        uint uVar7;
        int iVar8;
        int psVar9;
        int puVar10;
        int uVar11;
        int iVar18;
        // Ghidra reports these three as "unaffected" registers because each is written on exactly
        // one pass of the three-pass decode loop below. They are always all three written before the
        // call; C# needs the initialiser anyway.
        int unaff_s3 = 0;
        int unaff_s4 = 0;
        int unaff_s5 = 0;
        ushort uStack_36;
        ushort uStack_32;

        cVar1 = (sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        uVar11 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        if (((int)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8) & 0x10) == 0)
        {
            uStack_36 = PsxRam.ReadU16(streamPtr + 1 * 2);
        }
        else
        {
            uStack_36 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(streamPtr + 1 * 2) * 2);
        }

        if (((short)cVar1 & 0x40) == 0)
        {
            uVar2 = PsxRam.ReadU16(streamPtr + 2 * 2);
        }
        else
        {
            uVar2 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(streamPtr + 2 * 2) * 2);
        }

        uVar3 = (ushort)((short)cVar1 & 0xf);
        uStack_32 = PsxRam.ReadU16(streamPtr + 3 * 2);
        puVar10 = streamPtr + 4 * 2;
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            // Three operand slots packed into word3: 6 bits for the first, then 5 bits each.
            uVar7 = 0;
            iVar4 = 0;
            do
            {
                iVar4 = iVar4 >> 0x10;
                uVar5 = uStack_32;
                uVar6 = (ushort)(uVar5 & 0x3f);
                if (iVar4 == 1)
                {
                    unaff_s3 = FUN_8003f2b0(uVar5 & 0x1f, uVar11);
                    iVar4 = unaff_s3;
                    if (iVar4 == 0)
                    {
                        return puVar10;
                    }
                }
                else if (iVar4 < 2)
                {
                    if (iVar4 == 0)
                    {
                        unaff_s4 = FUN_8003f228(uVar5 & 0x3f, uVar11);
                        iVar4 = unaff_s4;
                        if (iVar4 == 0)
                        {
                            return puVar10;
                        }
                    }
                }
                else if (iVar4 == 2)
                {
                    unaff_s5 = AnimVm.DAT_801f2100 + (int)(uVar5 & 0xf) * 8;
                }

                if ((uVar7 & 0xffff) == 0)
                {
                    uStack_32 = (ushort)((short)uStack_32 >> 6);
                }
                else
                {
                    uStack_32 = (ushort)((short)uStack_32 >> 5);
                }

                uVar7 = uVar7 + 1;
                iVar4 = unchecked((int)(uVar7 * 0x10000));
            } while (unchecked((int)(uVar7 * 0x10000)) >> 0x10 < 3);

            iVar8 = 0;
            iVar4 = 0;
            do
            {
                iVar8 = iVar8 + 1;
                // (iVar4 >> 0xe) with iVar4 == index * 0x10000 is index * 4: AnimVm.g_renderMetadataBuffer
                // is an int array and the tag byte is its +2.
                if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + (iVar4 >> 0xe) + 2) == uVar3)
                {
                    uVar7 = (uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + (iVar4 >> 0xe)) >> 0x18;
                    psVar9 = DAT_801fa580 + (int)uVar7 * 2;
                    // &AnimVm.DAT_801f2180 is undefined2*, so `+ uVar7 * 0x10` is +uVar7*0x20 bytes — the
                    // 0x20-byte vertex quad AnimCmd_RenderEntryGroup writes per primitive.
                    iVar18 = AnimVm.DAT_801f2180 + (int)uVar7 * 0x10 * 2;
                    FUN_8003f6c0(
                        AnimVm.DAT_801f7180 + (int)uVar7 * 0x34,
                        iVar18,
                        unaff_s3,
                        unaff_s4,
                        unaff_s5,
                        psVar9,
                        (ushort)(int)(short)uStack_36,
                        (short)((short)cVar1 & 0x80),
                        uVar3,
                        uVar2,
                        uVar6);
                    iVar4 = 0;
                    if ((short)uStack_36 < 1)
                    {
                        return puVar10;
                    }

                    do
                    {
                        iVar4 = iVar4 + 1;
                        PsxRam.WriteU16(psVar9, (ushort)(PsxRam.ReadU16(psVar9) + uVar2));
                        psVar9 = psVar9 + 2;
                    } while (iVar4 * 0x10000 >> 0x10 < (int)(short)uStack_36);

                    return puVar10;
                }

                iVar4 = iVar8 * 0x10000;
            } while (iVar8 * 0x10000 >> 0x10 < 0x40);
        }

        return puVar10;
    }

    // =====================================================================================
    // OPCODE 16 — `x_add_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_XAddSet @ 0x80039C18 (VS.EXE)
    // Opcode 16, which the image's opcode-name table calls `x_add_set`.
    //
    // Ghidra's own pre-comment, kept verbatim:
    //   "CERTAIN: homologous to GAME.EXE AnimCmd_XAddSet. Reads and mutates AnimVm.g_meshXOffsetBuffer,
    //    clamps against g_meshEntryFlagsHiBuf, and does not expose any direct channel for
    //    CHBinMeshEntry.primitive_count_packed.high16."
    //
    // The step is the difference between two shared variables (word1 nibbles 2 and 3); its sign
    // picks the walk direction, and the overflow past the clamp is carried into the next slot.
    // Three halfwords consumed.
    internal static int AnimCmd_XAddSet(int streamPtr, int arg)
    {
        ushort uVar1;
        ushort uVar2;
        sbyte cVar3;
        short sVar4;
        int iVar5;
        uint uVar6;
        int puVar7;
        int puVar8;
        uint uVar9;
        int iVar10;
        ushort uStack_48;
        ushort uStack_40;
        ushort uStack_36;

        cVar3 = (sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        uStack_48 = (ushort)cVar3;
        uVar2 = PsxRam.ReadU16(streamPtr + 1 * 2);
        uVar6 = (uint)(uVar2 & 0xff);
        uStack_40 = PsxRam.ReadU16(streamPtr + 2 * 2);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            if (((int)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8) & 0x80) != 0)
            {
                uStack_40 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)uStack_40 * 2);
                uStack_48 = (ushort)((short)cVar3 & 0x7f);
            }

            uStack_36 = (ushort)(PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)((uVar2 & 0xf00) >> 8) * 2)
                                 - PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uint)(uVar2 >> 0xc) * 2));
            iVar5 = (int)((PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)((uVar2 & 0xf00) >> 8) * 2)
                           - (uint)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uint)(uVar2 >> 0xc) * 2)) * 0x10000)
                    >> 0x10;
            iVar10 = -1;
            if (iVar5 < 1)
            {
                uVar9 = (uVar6 + uStack_48) - 1;
                sVar4 = (short)(uStack_48 - 1);
            }
            else
            {
                iVar10 = 1;
                if ((short)uStack_40 < iVar5)
                {
                    uStack_36 = uStack_40;
                }

                uVar9 = uStack_48;
                sVar4 = (short)(uStack_48 + (short)uVar6);
            }

            iVar5 = (short)uVar9;
            while (iVar5 != sVar4)
            {
                // ((uVar9 << 0x10) >> 0xf) is (short)uVar9 * 2 — a short index into both buffers.
                iVar5 = unchecked((int)(uVar9 << 0x10)) >> 0xf;
                puVar7 = AnimVm.g_meshXOffsetBuffer + iVar5;
                PsxRam.WriteU16(puVar7, (ushort)(PsxRam.ReadU16(puVar7) + uStack_36));
                puVar8 = g_meshEntryFlagsHiBuf + iVar5;
                if ((short)uStack_36 < 0)
                {
                    uStack_36 = 0;
                    uVar2 = PsxRam.ReadU16(puVar7);
                    if ((short)uVar2 < 0)
                    {
                        PsxRam.WriteU16(puVar7, 0);
                        uStack_36 = uVar2;
                    }
                }
                else
                {
                    uStack_36 = 0;
                    uVar2 = PsxRam.ReadU16(puVar7);
                    uVar1 = PsxRam.ReadU16(puVar8);
                    if ((short)uVar1 < (short)uVar2)
                    {
                        PsxRam.WriteU16(puVar7, PsxRam.ReadU16(puVar8));
                        uStack_36 = (ushort)(uVar2 - uVar1);
                    }
                }

                uVar9 = uVar9 + (uint)iVar10;
                iVar5 = unchecked((int)(uVar9 * 0x10000)) >> 0x10;
            }
        }

        return streamPtr + 3 * 2;
    }

    // =====================================================================================
    // OPCODE 18 — `x_max_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_XMaxSet @ 0x8003A034 (VS.EXE)
    // Opcode 18, which the image's opcode-name table calls `x_max_set`.
    //
    // Ghidra's own pre-comment, kept verbatim:
    //   "CERTAIN: homologous to GAME.EXE AnimCmd_XMaxSet. Reads and mutates g_meshEntryFlagsHiBuf
    //    only; this path is downstream of entry overlay and does not carry
    //    CHBinMeshEntry.primitive_count_packed.high16."
    //
    // word1.high8 is the operator handed to FUN_8003f540 (the VM's set/add/sub/or/and/xor/mul/div
    // dispatcher); operator 8 is not one of its cases and is special-cased here into a straight copy
    // from another slot. Three halfwords consumed.
    internal static int AnimCmd_XMaxSet(int streamPtr, int arg)
    {
        ushort uVar1;
        ushort uVar2;
        short sVar3;
        ushort uVar4;
        int iVar5;
        uint uVar6;
        int psVar7;
        ushort uStack_24;

        uVar1 = PsxRam.ReadU16(streamPtr);
        iVar5 = (sbyte)(uVar1 >> 8);
        uVar2 = PsxRam.ReadU16(streamPtr + 1 * 2);
        uVar6 = (uint)(uVar2 & 0xff);
        uStack_24 = (ushort)(uVar2 >> 8);
        uVar4 = PsxRam.ReadU16(streamPtr + 2 * 2);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            if (((int)(sbyte)(uVar2 >> 8) & 0x10) != 0)
            {
                uStack_24 = (ushort)((short)(sbyte)(uVar2 >> 8) & 0xf);
                uVar4 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)uVar4 * 2);
            }

            if (iVar5 < (int)((uint)iVar5 + uVar6))
            {
                do
                {
                    psVar7 = g_meshEntryFlagsHiBuf + ((iVar5 << 0x10) >> 0xf);
                    sVar3 = (short)FUN_8003f540((uint)(int)(short)PsxRam.ReadU16(psVar7), uStack_24,
                        (uint)(int)(short)uVar4);
                    PsxRam.WriteU16(psVar7, (ushort)sVar3);
                    if (uStack_24 == 8)
                    {
                        PsxRam.WriteU16(psVar7, PsxRam.ReadU16(g_meshEntryFlagsHiBuf + (short)uVar4 * 2));
                    }

                    iVar5 = iVar5 + 1;
                    if ((short)PsxRam.ReadU16(psVar7) < 0)
                    {
                        PsxRam.WriteU16(psVar7, 0);
                    }
                } while (iVar5 * 0x10000 >> 0x10 < (int)(short)(sbyte)(uVar1 >> 8) + (int)(short)uVar6);
            }
        }

        return streamPtr + 3 * 2;
    }

    // =====================================================================================
    // OPCODE 29 — `movexp_set`
    // =====================================================================================

    // GHIDRA: UndefinedFunction_8003c3c0 @ 0x8003C3C0 (VS.EXE)
    // GHIDRA CARRIES NO FUNCTION AT THIS ADDRESS. The dispatch table entry at 0x80082368 is its only
    // reference, and `UndefinedFunction_8003c3c0` is what the decompiler's temporary preview names
    // it — not a symbol in the database. No label was created: this slice is read-only on the
    // Ghidra side.
    //
    // Opcode 29, which the image's opcode-name table @ 0x800823C0 calls `movexp_set`. The C# name is
    // that string; nothing else about it is named speculatively.
    //
    // Three of word1's top bits redirect the operands through AnimVm.g_animSharedVarTable, the object is
    // resolved through FUN_8003f2b0, and the work is handed to FUN_80047550 together with the
    // ADDRESS of the DAT_801faa84 triple. Four halfwords consumed.
    internal static int AnimCmd_MovexpSet(int streamPtr, int arg)
    {
        int iVar1;
        ushort uStack_1e;
        ushort uStack_1c;
        ushort uStack_1a;

        uStack_1e = PsxRam.ReadU16(streamPtr + 1 * 2);
        uStack_1c = PsxRam.ReadU16(streamPtr + 2 * 2);
        uStack_1a = PsxRam.ReadU16(streamPtr + 3 * 2);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            if ((uStack_1e & 0x1000) != 0)
            {
                uStack_1a = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)uStack_1a * 2);
            }

            if ((uStack_1e & 0x2000) != 0)
            {
                uStack_1c = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (short)uStack_1c * 2);
            }

            if ((uStack_1e & 0x4000) != 0)
            {
                uStack_1e = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (uStack_1e & 0xfff) * 2);
            }

            iVar1 = FUN_8003f2b0((uint)((int)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8) & 0x3f),
                PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8));
            if (iVar1 != 0)
            {
                FUN_80047550(iVar1, DAT_801faa84, (uint)(uStack_1e & 0xfff), uStack_1c, (int)(short)uStack_1a);
            }
        }

        return streamPtr + 4 * 2;
    }

    // =====================================================================================
    // OPCODE 30 — `dist_set`
    // =====================================================================================

    // GHIDRA: UndefinedFunction_8003c4e4 @ 0x8003C4E4 (VS.EXE)
    // GHIDRA CARRIES NO FUNCTION AT THIS ADDRESS either — same situation as opcode 29, same reason
    // for the name.
    //
    // Opcode 30, which the image's opcode-name table calls `dist_set`. Two operand slots resolved
    // through FUN_8003f228 and one through FUN_8003f2b0; the three go to FUN_80045f34, and the
    // result's halfword at +2 is then biased by word2 and wrapped into twelve bits — the same
    // 0xfff angle wrap the shared-variable indices use elsewhere in the VM. Three halfwords
    // consumed.
    //
    // PARTIAL, and left as the original leaves it: Ghidra prints the first FUN_8003f228 call with
    // FIVE arguments, `FUN_8003f228(op, ctx, param_3, param_4, (short)(char)(*param_1 >> 8))`,
    // because the code stores three further words into the outgoing argument area. FUN_8003f228
    // takes two and reads no more, and two of the three extras are this function's own uninitialised
    // incoming registers. The two real arguments are passed here; the dead stores have no
    // observable effect and are not reproduced.
    internal static int AnimCmd_DistSet(int streamPtr, int arg)
    {
        ushort uVar1;
        ushort uVar2;
        int iVar3;
        int iVar4;
        int iVar5;
        int uVar6;

        uVar6 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        uVar1 = PsxRam.ReadU16(streamPtr + 1 * 2);
        uVar2 = PsxRam.ReadU16(streamPtr + 2 * 2);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            iVar3 = FUN_8003f228((uint)(int)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8), uVar6);
            iVar4 = FUN_8003f228((uint)(uVar1 & 0xff), uVar6);
            if (iVar3 != 0 && iVar4 != 0 && (iVar5 = FUN_8003f2b0((uint)(uVar1 >> 8), uVar6)) != 0)
            {
                FUN_80045f34(iVar3, iVar4, iVar5);
                PsxRam.WriteU16(iVar5 + 2, (ushort)(((short)PsxRam.ReadU16(iVar5 + 2) + uVar2) & 0xfff));
            }
        }

        return streamPtr + 3 * 2;
    }

    // =====================================================================================
    // OPCODE 31 — `move_set`
    // =====================================================================================

    // GHIDRA: UndefinedFunction_8003c5e4 @ 0x8003C5E4 (VS.EXE)
    // GHIDRA CARRIES NO FUNCTION AT THIS ADDRESS either.
    //
    // Opcode 31, which the image's opcode-name table calls `move_set`. Four halfwords consumed.
    //
    // Three object slots: the one being moved (word0.high8), the target (word1.low8) and the origin
    // (word2.low8), each optionally redirected through AnimVm.g_animSharedVarTable by bit 6 of its own
    // selector byte. When target and origin are the SAME slot the handler degenerates to a straight
    // add of the DAT_801faa84/88/8c step triple and returns.
    //
    // Otherwise each axis is stepped independently and the step is judged by a SIGN-CHANGE TEST:
    // `((target - origin) ^ (target - moved)) & 0x8000` is non-zero exactly when the moved value has
    // crossed the target, in which case it is snapped to the target and the axis is marked done.
    // Only when all three axes are done (uStack_4c == 7) is the bit mask word3 written back into the
    // shared variable selected by word2.high8, and only then — and only if word1.high8 is non-zero —
    // is the moved slot snapped onto the ORIGIN slot rather than the target.
    //
    // C# CANNOT WRITE THE ORIGINAL'S `goto LAB_8003c82c` LITERALLY: the label sits inside the `if`
    // arm and the goto sits in the matching `else` arm, and C# forbids a jump into a block. Each of
    // the three axes is therefore written as the nested-if form of the same control-flow graph — the
    // successor of every statement is unchanged, nothing is reordered, and the two `if` arms still
    // converge on the same `uStack_4c |= bit`. The one goto that jumps OUT of a block,
    // `code_r0x8003c9c4`, is kept exactly as it is.
    internal static int AnimCmd_MoveSet(int streamPtr, int arg)
    {
        short sVar1;
        ushort uVar2;
        ushort uVar3;
        short sVar4 = 0;
        int psVar5;
        int psVar6;
        int psVar7;
        short sVar8;
        ushort uVar9;
        uint uVar10;
        int uVar11;
        int puVar12;
        short sStack_60;
        ushort uStack_5e;
        ushort uStack_5a;
        ushort uStack_4c;

        uVar11 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        uVar10 = (uint)(int)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        sStack_60 = (sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        uVar2 = PsxRam.ReadU16(streamPtr + 1 * 2);
        uStack_5e = (ushort)(uVar2 & 0xff);
        uVar9 = PsxRam.ReadU16(streamPtr + 2 * 2);
        uStack_5a = (ushort)(uVar9 & 0xff);
        uVar3 = PsxRam.ReadU16(streamPtr + 3 * 2);
        streamPtr = streamPtr + 4 * 2;
        puVar12 = AnimVm.g_animSharedVarTable + (sbyte)(uVar9 >> 8) * 2;
        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            return streamPtr;
        }

        if ((uVar10 & 0x40) != 0)
        {
            sStack_60 = (short)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar10 & 0xf) * 2);
        }

        if ((uVar2 & 0x40) != 0)
        {
            uStack_5e = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (uVar2 & 0xf) * 2);
        }

        if ((uVar9 & 0x40) != 0)
        {
            uStack_5a = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (uVar9 & 0xf) * 2);
        }

        psVar5 = FUN_8003f228((uint)(int)sStack_60, uVar11);
        if (psVar5 == 0)
        {
            return streamPtr;
        }

        if ((int)(short)uStack_5e == (int)(short)uStack_5a)
        {
            PsxRam.WriteU16(psVar5,
                (ushort)((short)PsxRam.ReadU16(psVar5) + (short)PsxRam.ReadI32(DAT_801faa84)));
            PsxRam.WriteU16(psVar5 + 1 * 2,
                (ushort)((short)PsxRam.ReadU16(psVar5 + 1 * 2) + (short)PsxRam.ReadI32(DAT_801faa88)));
            sVar4 = (short)((short)PsxRam.ReadU16(psVar5 + 2 * 2) + (short)PsxRam.ReadI32(DAT_801faa8c));
            goto code_r0x8003c9c4;
        }

        psVar6 = FUN_8003f228((uint)(int)(short)uStack_5e, uVar11);
        psVar7 = FUN_8003f228((uint)(int)(short)uStack_5a, uVar11);
        if (psVar6 == 0)
        {
            return streamPtr;
        }

        if (psVar7 == 0)
        {
            return streamPtr;
        }

        uStack_4c = 0;
        sVar4 = (short)PsxRam.ReadU16(psVar6);
        if (sVar4 == (short)PsxRam.ReadU16(psVar5) || PsxRam.ReadI32(DAT_801faa84) == 0)
        {
            uStack_4c = 1;
        }
        else
        {
            sVar1 = (short)PsxRam.ReadU16(psVar7);
            if (sVar4 == sVar1)
            {
                PsxRam.WriteU16(psVar5, (ushort)sVar4);
                uStack_4c = 1;
            }
            else
            {
                sVar8 = (short)((short)PsxRam.ReadU16(psVar5) + (short)PsxRam.ReadI32(DAT_801faa84));
                PsxRam.WriteU16(psVar5, (ushort)sVar8);
                if ((((sVar4 - sVar1) ^ ((short)PsxRam.ReadU16(psVar6) - sVar8)) & 0x8000U) != 0)
                {
                    PsxRam.WriteU16(psVar5, PsxRam.ReadU16(psVar6));
                    uStack_4c = 1;
                }
            }
        }

        sVar4 = (short)PsxRam.ReadU16(psVar6 + 1 * 2);
        if (sVar4 == (short)PsxRam.ReadU16(psVar5 + 1 * 2) || PsxRam.ReadI32(DAT_801faa88) == 0)
        {
            uStack_4c = (ushort)(uStack_4c | 2);
        }
        else
        {
            sVar1 = (short)PsxRam.ReadU16(psVar7 + 1 * 2);
            if (sVar4 == sVar1)
            {
                PsxRam.WriteU16(psVar5 + 1 * 2, (ushort)sVar4);
                uStack_4c = (ushort)(uStack_4c | 2);
            }
            else
            {
                sVar8 = (short)((short)PsxRam.ReadU16(psVar5 + 1 * 2) + (short)PsxRam.ReadI32(DAT_801faa88));
                PsxRam.WriteU16(psVar5 + 1 * 2, (ushort)sVar8);
                if ((((sVar4 - sVar1) ^ ((short)PsxRam.ReadU16(psVar6 + 1 * 2) - sVar8)) & 0x8000U) != 0)
                {
                    PsxRam.WriteU16(psVar5 + 1 * 2, PsxRam.ReadU16(psVar6 + 1 * 2));
                    uStack_4c = (ushort)(uStack_4c | 2);
                }
            }
        }

        sVar4 = (short)PsxRam.ReadU16(psVar6 + 2 * 2);
        if (sVar4 == (short)PsxRam.ReadU16(psVar5 + 2 * 2) || PsxRam.ReadI32(DAT_801faa8c) == 0)
        {
            uStack_4c = (ushort)(uStack_4c | 4);
        }
        else
        {
            sVar1 = (short)PsxRam.ReadU16(psVar7 + 2 * 2);
            if (sVar4 == sVar1)
            {
                PsxRam.WriteU16(psVar5 + 2 * 2, (ushort)sVar4);
                uStack_4c = (ushort)(uStack_4c | 4);
            }
            else
            {
                sVar8 = (short)((short)PsxRam.ReadU16(psVar5 + 2 * 2) + (short)PsxRam.ReadI32(DAT_801faa8c));
                PsxRam.WriteU16(psVar5 + 2 * 2, (ushort)sVar8);
                if ((((sVar4 - sVar1) ^ ((short)PsxRam.ReadU16(psVar6 + 2 * 2) - sVar8)) & 0x8000U) != 0)
                {
                    PsxRam.WriteU16(psVar5 + 2 * 2, PsxRam.ReadU16(psVar6 + 2 * 2));
                    uStack_4c = (ushort)(uStack_4c | 4);
                }
            }
        }

        uVar9 = (ushort)(PsxRam.ReadU16(puVar12) & ~uVar3);
        PsxRam.WriteU16(puVar12, uVar9);
        if (uStack_4c != 7)
        {
            return streamPtr;
        }

        PsxRam.WriteU16(puVar12, (ushort)(uVar9 | uVar3));
        if ((sbyte)(uVar2 >> 8) == 0)
        {
            return streamPtr;
        }

        PsxRam.WriteU16(psVar5, PsxRam.ReadU16(psVar7));
        PsxRam.WriteU16(psVar5 + 1 * 2, PsxRam.ReadU16(psVar7 + 1 * 2));
        sVar4 = (short)PsxRam.ReadU16(psVar7 + 2 * 2);
    code_r0x8003c9c4:
        PsxRam.WriteU16(psVar5 + 2 * 2, (ushort)sVar4);
        return streamPtr;
    }

    // =====================================================================================
    // NOT IN THIS SLICE — the VM helpers this family calls
    // =====================================================================================
    // Every stub below is a real function of VS.EXE. They are declared with their address and what
    // is known of them rather than silently omitted, because the shape of the eight handlers is the
    // deliverable of this slice and a handler that quietly dropped half its calls would not be it.
    //
    // The first four are SHARED VM MACHINERY, not mesh-family code: FUN_8003f228 has twenty call
    // sites, FUN_8003f2b0 twelve and FUN_8003f540 forty-two, spread across every opcode family.
    // Owning them here would claim code that belongs to whoever ports the VM core. Their bodies are
    // small and fully readable, and are described below so that the next slice does not have to
    // re-derive them.

    // GHIDRA: FUN_8003f228 @ 0x8003F228 (VS.EXE)
    // BLOCKED — shared VM machinery, twenty call sites, not this family's to own.
    // Resolves an operand selector to a TRANSLATION slot address and returns 0 for "none":
    // bit 4 set -> `&AnimVm.UNK_801f2080 + (sel & 0xf) * 8`; else bit 5 set -> `(&DAT_801faaac)[sel & 0xf]`
    // plus 0x3c, 0 when that entry is 0; else selector < 6 -> `*(int *)(ctx + 0x18 + sel * 4)` plus
    // 0x114, 0 when that entry is 0; else 0.
    private static int FUN_8003f228(uint param_1, int param_2)
    {
        return 0;
    }

    // GHIDRA: FUN_8003f2b0 @ 0x8003F2B0 (VS.EXE)
    // BLOCKED — shared VM machinery, twelve call sites.
    // The ROTATION counterpart of FUN_8003f228: bit 4 set -> `&AnimVm.DAT_801f2000 + (sel & 0xf) * 8`;
    // else selector < 6 -> `*(int *)(ctx + 0x18 + sel * 4)` plus 0x11c, 0 when that entry is 0;
    // else `&DAT_1f800084`, the scratchpad short already declared in VS_EXE/FileIo.cs.
    private static int FUN_8003f2b0(uint param_1, int param_2)
    {
        return 0;
    }

    // GHIDRA: FUN_8003f540 @ 0x8003F540 (VS.EXE)
    // BLOCKED — shared VM machinery, forty-two call sites.
    // The VM's arithmetic dispatcher, `param_2` selecting the operation on (param_1, param_3):
    // 0 set, 1 add, 2 sub, 3 or, 4 and, 5 xor, 6 multiply, 7 divide (with the MIPS divide-by-zero
    // and INT_MIN/-1 traps intact), 9 reverse subtract. Case 8 is absent, which is why
    // AnimCmd_XMaxSet special-cases operator 8 into a slot copy after the call.
    private static int FUN_8003f540(uint param_1, ushort param_2, uint param_3)
    {
        return 0;
    }

    // GHIDRA: FUN_8003f6c0 @ 0x8003F6C0 (VS.EXE)
    // BLOCKED — and this one is blocked by the SDK, not by ownership: it has exactly ONE caller,
    // AnimCmd_CulSet at 0x80038998, so it is this family's function and nothing else will claim it.
    // It cannot be transliterated here because PsxSdkMonogame's LibGte provides RotAverage4 but no
    // RotAverage3, and adding it means editing LibGte.cs, which is not this file.
    //
    // What it does, and the evidence behind the opcode-9 verdict in the file header: PushMatrix;
    // ReadRotMatrix(&DAT_1f800000); RotMatrix(param_3, local) — param_3 is the rotation slot;
    // translation from param_4[0..2] with _DAT_1f8000b4 and _DAT_1f8000bc subtracted off X and Z;
    // ScaleMatrix with param_5[0..2]; CompMatrix; SetRotMatrix; SetTransMatrix; then, for param_7
    // primitives, RotAverage4 when the vertex quad's pad is 0, RotAverage3 on (v0,v1,v2) when it is
    // 1 and on (v0,v2,v3) otherwise, storing the returned OTZ into param_6[i] and forcing a zero to
    // 0x801; PopMatrix. param_8 non-zero replaces the composed rotation with the uncomposed one.
    //
    // param_9 / param_10 / param_11 are the three further argument words AnimCmd_CulSet stores; the
    // body never reads them. They are kept on the signature so the call site stays literal.
    private static void FUN_8003f6c0(int param_1, int param_2, int param_3, int param_4, int param_5,
        int param_6, ushort param_7, short param_8, ushort param_9, ushort param_10, ushort param_11)
    {
    }

    // GHIDRA: FUN_80047550 @ 0x80047550 (VS.EXE)
    // BLOCKED: 312 bytes, called by AnimCmd_MovexpSet with the resolved rotation slot, the ADDRESS
    // of the DAT_801faa84 triple, a twelve-bit value, and two more operands. Semantics not closed.
    private static void FUN_80047550(int param_1, int param_2, uint param_3, ushort param_4, int param_5)
    {
    }

    // GHIDRA: FUN_80045f34 @ 0x80045F34 (VS.EXE)
    // BLOCKED: 712 bytes, called by AnimCmd_DistSet with two translation slots and one rotation
    // slot. Semantics not closed.
    private static void FUN_80045f34(int param_1, int param_2, int param_3)
    {
    }
}
