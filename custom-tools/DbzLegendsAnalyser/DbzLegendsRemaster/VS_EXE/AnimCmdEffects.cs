using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's ANIMATION-SCRIPT VM — the effects family. Eight of the fifty-one handlers reached
// through g_animStreamDispatchTable @ 0x800822F4 by ExecuteAnimStreamBatch @ 0x80036768.
//
// THE CALLING CONTRACT, read off the interpreter itself (0x80036768, lines 41..45):
//
//     while (uVar1 != 0) {
//       puVar2 = (*(code *)(&g_animStreamDispatchTable)[*puVar2 & 0xff])(puVar2, iVar6 >> 0x10);
//       uVar1 = *puVar2;
//     }
//
// so the opcode is the LOW BYTE of the first halfword, every handler RETURNS THE POINTER TO THE
// NEXT COMMAND, and a zero halfword stops the stream. A threaded interpreter. The second argument
// is the mesh-slot index 0..15. NONE OF THE EIGHT HANDLERS BELOW READS IT: six carry a locked
// one-parameter prototype in Ghidra, and the seventh, 0x8003E60C, has no function at all — the
// decompiler recovered `ushort *(ushort *param_1)` from the code by itself, one parameter. The
// eighth was checked in the disassembly: AnimCmd_CheffWait's only mention of a1 is
// `addu v0,v0,a1` at 0x8003EACC, where a1 has already been reloaded with &DAT_801FAB30 for the
// clear loop. So the C# signatures take the stream pointer alone, as Ghidra spells them.
//
// THE BINARY NAMES ITS OWN OPCODES. 0x800823C0 holds fifty 16-byte ASCII entries, one per opcode,
// and they are the engine author's own labels. Read straight out of the image, the eight in this
// file are: 5 `anm_set`, 12 `eye_set`, 33 `eff_set`, 34 `att_set`, 39 `ch_eff_set`,
// 40 `ch_dan_set`, 41 `hitz_set`, 44 `cheff_wait`. Note the table has FIFTY names for FIFTY-ONE
// handlers: the last pointer, 0x8003EF04, has no name entry.
//
// ONE NAME DISCREPANCY FALLS IN THIS FAMILY AND IS ADJUDICATED, NOT HARMONISED — opcode 12. The
// image calls it `eye_set`; Ghidra carries AnimCmd_ApplyCharEffect. See the note on that handler:
// the body decides it, and the Ghidra name is the one the body supports.
//
// THE STREAM IS RAW PSX MEMORY, walked by pointer. It is modelled here as an `int` address read
// through PsxRam, never as a copied ushort[], because the handler has to hand an ADDRESS back for
// the interpreter to re-read. VS_EXE/PrimitivePools.cs already walks the task contexts this way.
// Every `streamPtr + n` of the decompilation is `+ n*2` here: Ghidra's pointer is `ushort *`.
//
// PARTIAL — ONE INFRASTRUCTURE GAP, STATED RATHER THAN PAPERED OVER. VS_EXE_exe has no
// ResolveAddress, so PsxRam.AddressResolver never answers for VS.EXE and every PsxRam read below
// returns 0 until one exists. That is out of this slice's file scope. It has one visible
// consequence, flagged again at AnimCmd_CheffWait: a synthetic command built in a caller's stack
// frame cannot be handed over as an address, so the 0x8000 update command reads back as 0 and the
// receiving handler takes the init arm instead of the update arm. Nothing calls into this file yet
// — the interpreter is not ported — so the divergence is latent, not live.
//
// OWNERSHIP CAVEAT, in the shape VS_EXE/FileIo.cs already uses for the GTE scratchpad. The globals
// below at 0x801FAA64..0x801FAC3F, 0x801FA880, 0x801F2180 and 0x801F7180 are NOT this family's
// alone. g_animSharedVarTable has 98 references across the whole opcode set, DAT_800B305A gates
// every handler in the VM, and the two rendering buffers belong to RenderBattleScene3D
// @ 0x800358B8 and to AnimCmd_RenderEntryGroup @ 0x800373A0 (`table_set`). They are declared here
// because this file is the whole of the slice. When a sibling family lands they belong in one
// shared VS_EXE/AnimStream.cs, MOVED AS THEY ARE — the addresses and the `GHIDRA:` lines travel
// with them, and two classes must never end up owning two different copies of one PSX address.
//
// Nothing here calls DbzLegendsRemaster.TITLE_EXE or .SELECT_EXE: those are separately linked
// overlays at other addresses, and calling into them would make every annotation below false.
internal static class AnimCmdEffects
{
    // =====================================================================================
    // The anim-stream global block, 0x801FAA64..0x801FAC3F
    //
    // ONE BYTE REGION, NOT TWELVE ARRAYS, and the reason is the original's own indexing. The
    // handlers reach g_animSharedVarTable with masks of 0xf (anm_set, eff_set, att_set), 0x3f
    // (ch_dan_set) and 0xff (ch_dan_set, hitz_set, ch_eff_set), and eye_set reaches it with a
    // sign-extended stream word. A ushort[16] would throw on index 16; the console simply writes
    // into the neighbouring global. Modelling the run as bytes keeps that aliasing, which rule 12
    // says not to correct. SharedHighRam @ 0x801FF000 is the same pattern.
    //
    // THE RUN TILES EXACTLY, which is what closes every extent in it. Each symbol's size is the
    // distance to the next, and each distance agrees with how the code indexes it:
    //     0x801FAA64  g_animSharedVarTable      0x20  = 16 ushort   (masks of 0xf)
    //     0x801FAA84  DAT_801faa84/88/8c        0x0C  = 3 words     (three named symbols)
    //     0x801FAAAC  DAT_801faaac              0x60             (indexed [x & 0xf], 16 used)
    //     0x801FAB0C  g_charRenderStateBuf      0x18  = 6 uint     (the tick loop runs 6)
    //     0x801FAB24  g_charSharedVarMaskBuf    0x0C  = 6 ushort   (same 6)
    //     0x801FAB30  DAT_801fab30              0x40  = 16 uint
    //     0x801FAB70  DAT_801fab70              0x40  = 16 words
    //     0x801FABB0  DAT_801fabb0              0x40  = 16 words
    //     0x801FABF0  DAT_801fabf0              0x10  = 16 bytes
    //     0x801FAC00  DAT_801fac00              0x40  = 16 words
    // The last five are one 16-slot record split into five parallel columns; the four loops that
    // walk them all run `while (i < 0x10)`. docs/structure-ch-bin-files.history.md carries the
    // same sizes as CERTAIN for g_animSharedVarTable (uint16[16]), DAT_801faaac (uint32[16]),
    // g_charRenderStateBuf (uint32[6]), g_charSharedVarMaskBuf (uint16[6]) and DAT_801fab30
    // (uint32[16]), from the GAME.EXE analysis of the same high-RAM layout.
    // =====================================================================================

    private const int AnimStreamBlockBase = unchecked((int)0x801FAA64);

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: the backing store for every global from 0x801FAA64 to 0x801FAC3F. This range is
    // entirely inside AnimVm's single workspace region (0x801F2000..0x801FAC47, RAM_801f2000), so
    // it is reached through PsxRam by address rather than through a second byte[] region of its
    // own — a second RamRegion over the same PSX addresses is exactly the duplicate-storage defect
    // AnimVm.cs exists to close. The one place the original stores a pointer INTO this block
    // (AnimCmd_EffSet's `*(int **)(*piVar10 + 0x58) = piVar10`) still stores the console's own
    // number, since AnimStreamBlockBase is the same PSX address either way.

    // GHIDRA: g_animSharedVarTable @ 0x801FAA64 (VS.EXE)
    // The VM's sixteen shared halfword variables — the indirection table every opcode family reads
    // and the bit bus the combat handlers OR their result masks onto. 98 references.
    // PARTIAL: an index above 0xEE would run past the modelled run and throw where the console
    // would carry on into the next global. Nothing in the eight handlers here can produce one from
    // its own masks; only a stream word could.
    internal static ushort ReadAnimSharedVar(int index) =>
        PsxRam.ReadU16(AnimStreamBlockBase + (index * 2));

    // JUSTIFICATION: C# language bridge only
    // RELATION: the write half of the pair above. The original spells both halves as
    // `*(ushort *)(&g_animSharedVarTable + index * 2)`; C# needs a named accessor over the byte
    // region to say the same thing.
    internal static void WriteAnimSharedVar(int index, ushort value) =>
        PsxRam.WriteU16(AnimStreamBlockBase + (index * 2), value);

    // GHIDRA: DAT_801faa84 @ 0x801FAA84 (VS.EXE)
    // GHIDRA: DAT_801faa88 @ 0x801FAA88 (VS.EXE)
    // GHIDRA: DAT_801faa8c @ 0x801FAA8C (VS.EXE)
    // AnimCmd_AttSet reads the LOW HALFWORD of each into its three-short argument vector and, on
    // one flag, zeroes all three whole words. docs/structure-ch-bin-files.history.md closes the
    // trio as the per-frame X/Y/Z movement delta (CERTAIN, from AnimCmd_MovexpSet, which is in
    // another family). Three separate words, four bytes apart, not a packed short[3].
    private const int Off_801faa84 = 0x020;
    private const int Off_801faa88 = 0x024;
    private const int Off_801faa8c = 0x028;

    // GHIDRA: DAT_801faaac @ 0x801FAAAC (VS.EXE)
    // Sixteen slots holding the address of an effect object. AnimCmd_EffSet fills and tears them
    // down; FUN_8003f228 @ 0x8003F228 resolves a target out of them at +0x3C.
    private const int Off_801faaac = 0x048;

    // JUSTIFICATION: C# language bridge only
    // RELATION: AnimCmd_EffSet stores the ADDRESS of one of these slots into the effect object it
    // just created (`*(int **)(*piVar10 + 0x58) = piVar10`). A C# array element has no address, so
    // the slot's PSX address is spelled out and the same number the console writes is written.
    private const int DAT_801faaacAddress = unchecked((int)0x801FAAAC);

    // GHIDRA: g_charRenderStateBuf @ 0x801FAB0C (VS.EXE)
    // Six packed render-state words, one per character slot. AnimCmd_SetCharRenderState is the
    // only writer and the only reader — three references in the whole overlay, all in it.
    private const int Off_charRenderStateBuf = 0x0A8;

    // GHIDRA: g_charSharedVarMaskBuf @ 0x801FAB24 (VS.EXE)
    // Six halfword masks, the value each slot ORs onto g_animSharedVarTable when its render state
    // retires. ONE reference in the whole overlay: AnimCmd_SetCharRenderState.
    private const int Off_charSharedVarMaskBuf = 0x0C0;

    // GHIDRA: DAT_801fab30 @ 0x801FAB30 (VS.EXE)
    // The sixteen ChEff slots' packed control word: low byte = frame countdown, byte 1 = the
    // polygon-group base index, byte 2 = the metadata id, high nibble + bit 30 = the loop and
    // restart bits. Zero means the slot is free. AnimCmd_ChEffSet owns it; AnimCmd_CheffWait
    // clears all sixteen. docs: g_charEffectSlotTable, uint32[16], CERTAIN.
    private const int Off_801fab30 = 0x0CC;

    // GHIDRA: DAT_801fab70 @ 0x801FAB70 (VS.EXE)
    // Column two of the same sixteen slots: the blob's START address, kept so bit 31 can rewind to
    // it and re-walk `(control >> 0x18) & 0xf` groups.
    private const int Off_801fab70 = 0x10C;

    // GHIDRA: DAT_801fabb0 @ 0x801FABB0 (VS.EXE)
    // Column three: the blob's CURRENT read cursor, advanced group by group each frame.
    private const int Off_801fabb0 = 0x14C;

    // GHIDRA: DAT_801fabf0 @ 0x801FABF0 (VS.EXE)
    // Column four, SIXTEEN BYTES not sixteen words — the run ends at 0x801FAC00, and the load is
    // `bVar1 = (&DAT_801fabf0)[iVar25]` into a byte. The low byte of the init word, added to each
    // group's own delay to form the next countdown.
    private const int Off_801fabf0 = 0x18C;

    // GHIDRA: DAT_801fac00 @ 0x801FAC00 (VS.EXE)
    // Column five: the 32 bits of `streamPtr[3..4]` latched at init. Its low half is the base U/V
    // byte pair, its high half the base CLUT — both stated in the CERTAIN comment Ghidra carries
    // on AnimCmd_ChEffSet.
    private const int Off_801fac00 = 0x19C;

    // AnimVm.DAT_800b305a, g_renderMetadataBuffer, DAT_801f2180 and DAT_801f7180 are the VM's SHARED
    // globals; they are declared once in AnimVm.cs and reached here as AnimVm.<name>, by address
    // through PsxRam rather than as a separate byte[] region — the three separate LibGpu.RamRegion
    // calls this file used to make for them are exactly the duplicate-storage defect AnimVm.cs
    // exists to close: they backed different bytes than AnimCmdAppearance's and AnimCmdMesh's own
    // copies of the same three PSX addresses. See AnimVm.cs for the merged proof comments,
    // including this file's own readings (g_renderMetadataBuffer's CERTAIN fill during the CH_BIN
    // overlay pass in RenderBattleScene3D; DAT_801f2180 as the projected-vertex block, four
    // 4-short vertices per 0x20-byte polygon; DAT_801f7180's nine-offset POLY_GT4 field proof).

    // GHIDRA: DAT_80099058 @ 0x80099058 (VS.EXE)
    // The translate target FUN_8003f228 resolved: a character object's +0x114, an effect object's
    // +0x3C, or a slot of UNK_801F2080. 0 means "not resolved" and makes opcode 12 give up.
    internal static int DAT_80099058;

    // GHIDRA: DAT_8009905c @ 0x8009905C (VS.EXE)
    // The rotate target FUN_8003f2b0 resolved: a character object's +0x11C, a slot of
    // DAT_801F2000, or — for a spec of 6 or more — DAT_1F800084, the GTE scratchpad rotation
    // vector SetupGeometry @ 0x800615CC maintains and VS_EXE/FileIo.cs already declares.
    internal static int DAT_8009905c;

    // GHIDRA: DAT_80099060 @ 0x80099060 (VS.EXE)
    // GHIDRA: DAT_80099062 @ 0x80099062 (VS.EXE)
    // GHIDRA: DAT_80099064 @ 0x80099064 (VS.EXE)
    // The three target rotation values opcode 12 latches, each optionally taken indirectly out of
    // g_animSharedVarTable under bits 8, 9 and 10 of the command word.
    internal static ushort DAT_80099060;
    internal static ushort DAT_80099062;
    internal static ushort DAT_80099064;

    // GHIDRA: DAT_80099066 @ 0x80099066 (VS.EXE)
    // undefined2. Set to 1 by opcode 12's init arm and consumed by FUN_8003f994, which computes
    // the per-frame deltas on the frame it sees a 1 and integrates on every frame after. The
    // GAME.EXE homologue of this quartet is documented at 0x800A6780..0x800A6786 with the flag in
    // the same +6 slot.
    internal static ushort DAT_80099066;

    // GHIDRA: DAT_80083c78 @ 0x80083C78 (VS.EXE)
    // The first of two 0x3C-byte list records. AnimCmd_AttSet picks between them with
    // `&DAT_80083c78 + uVar6 * 0x3c` where uVar6 is 0 or 1, and 0x80083C78 + 0x3C = 0x80083CB4,
    // which is exactly the second symbol Ghidra carries and the one AnimCmd_HitzSet names. That
    // arithmetic is what fixes the record size; the fields inside are not this slice's.
    private const int DAT_80083c78Address = unchecked((int)0x80083C78);

    // GHIDRA: DAT_80083cb4 @ 0x80083CB4 (VS.EXE)
    private const int DAT_80083cb4Address = unchecked((int)0x80083CB4);

    // GHIDRA: DAT_80083cb8 @ 0x80083CB8 (VS.EXE)
    // 0x80083CB4 + 4: the head of the second record's chain. AnimCmd_HitzSet walks it through
    // each node's word 0.
    private const int DAT_80083cb8Address = unchecked((int)0x80083CB8);

    // GHIDRA: PTR_DAT_800217f0 @ 0x800217F0 (VS.EXE)
    // .data, image value 0x80021908. AnimCmd_EffSet passes its ADDRESS, not its contents, so what
    // it points at does not matter here.
    private const int PTR_DAT_800217f0Address = unchecked((int)0x800217F0);

    // =====================================================================================
    // Opcode 5 — `anm_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_SetCharRenderState @ 0x80037F20 (VS.EXE)
    // Opcode 5, which the image's name table calls `anm_set`. 660 bytes. Four call sites: the
    // dispatch table entry at 0x80082308, plus three DIRECT calls that hand it a synthetic 0x8000
    // command — one at the tail of ExecuteAnimStreamBatch @ 0x80036968 and two in FUN_80036a64.
    // So it is a handler that doubles as the per-frame service routine for the six render slots,
    // the same double life AnimCmd_ChEffSet leads at the head of the same loop.
    //
    // Two arms, chosen by bit 7 of the sign-extended high byte of word 0:
    //   clear — INIT. Packs word 1's high byte, word 2's nibbles and bytes and the 0x10 and 0x40
    //           bits into one state word for slot `n`, stores word 3 as that slot's OR mask,
    //           writes word 1's low byte to the character object's +0x16A and clears its +0x04
    //           counter. Consumes four halfwords.
    //   set   — TICK. Walks the six slots: 0x40 is a one-shot that decays by 0x20; 0x20 marks a
    //           live slot; 0x10 chooses between "wait for the counter to reach a target, then load
    //           the next one and count the repeat down" and "wait for the counter to hit zero".
    //           Either way the slot retires by ORing its mask onto g_animSharedVarTable, clearing
    //           +0x16A, and zeroing itself. Consumes one halfword.
    // The `AnimVm.DAT_800b305a & 1` arm consumes FOUR halfwords whichever arm it would have taken — so a
    // frozen frame mis-advances a one-halfword tick command by three. That is the original's and
    // is kept (rule 12).
    internal static int AnimCmd_SetCharRenderState(int streamPtr)
    {
        int iVar10 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        int puVar3;

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            puVar3 = streamPtr + 2;
            uint uVar8 = unchecked((uint)(unchecked((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10)) >> 0x18));

            if ((uVar8 & 0x80) == 0)
            {
                uint uVar4 = uVar8 & 0xf;
                if ((uVar8 & 0x20) != 0)
                {
                    uVar4 = ReadAnimSharedVar((int)uVar4);
                }

                ushort uVar1 = PsxRam.ReadU16(puVar3);
                int iVar5 = (short)uVar4;
                ushort uVar2 = PsxRam.ReadU16(streamPtr + 4);

                PsxRam.WriteI32(AnimStreamBlockBase + (Off_charRenderStateBuf + (iVar5 * 4)), unchecked((int)(
                    (uVar8 & 0x10)
                    + ((uint)(unchecked((int)((uint)uVar2 << 0x10)) >> 0x1c) & 0xffU)
                    + unchecked((uint)(int)(short)(uVar1 & 0xff00))
                    + ((uint)(uVar2 & 0xff) * 0x10000)
                    + 0x40
                    + ((uint)(uVar2 & 0xf00) * 0x10000))));

                iVar10 = (iVar5 * 4) + iVar10;
                PsxRam.WriteU16(AnimStreamBlockBase + (Off_charSharedVarMaskBuf + (iVar5 * 2)), PsxRam.ReadU16(streamPtr + 6));

                byte local_16 = (byte)uVar1;
                PsxRam.WriteU8(PsxRam.ReadI32(iVar10 + 0x18) + 0x16a, local_16);
                puVar3 = streamPtr + 8;
                PsxRam.WriteU16(PsxRam.ReadI32(iVar10 + 0x18) + 4, 0);
            }
            else
            {
                int iVar9 = 0;
                int iVar5 = 0;
                do
                {
                    iVar5 = iVar5 >> 0x10;
                    int puVar6 = Off_charRenderStateBuf + (iVar5 * 4);
                    uVar8 = (uint)PsxRam.ReadI32(AnimStreamBlockBase + (puVar6));
                    int iVar7 = (iVar5 * 4) + iVar10;

                    if ((uVar8 & 0x40) == 0)
                    {
                        if ((uVar8 & 0x20) != 0)
                        {
                            if ((uVar8 & 0x10) == 0)
                            {
                                if ((short)PsxRam.ReadU16(PsxRam.ReadI32(iVar7 + 0x18) + 4) == 0)
                                {
                                    goto LAB_8003814c;
                                }
                            }
                            else if (PsxRam.ReadU16(PsxRam.ReadI32(iVar7 + 0x18) + 4) == ((uVar8 >> 8) & 0xff))
                            {
                                PsxRam.WriteU16(
                                    PsxRam.ReadI32(iVar7 + 0x18) + 4, (ushort)((uVar8 >> 0x10) & 0xff));
                                uVar8 = (uint)PsxRam.ReadI32(AnimStreamBlockBase + (puVar6));
                                if ((uVar8 & 0xff000000) == 0)
                                {
                                    WriteAnimSharedVar(
                                        (int)(uVar8 & 0xf),
                                        (ushort)(ReadAnimSharedVar((int)(uVar8 & 0xf))
                                                 | PsxRam.ReadU16(AnimStreamBlockBase + (Off_charSharedVarMaskBuf + (iVar5 * 2)))));
                                }
                                else
                                {
                                    uVar8 = uVar8 - 0x1000000;
                                    PsxRam.WriteI32(AnimStreamBlockBase + (puVar6), unchecked((int)(uVar8)));
                                    if ((uVar8 & 0xff000000) == 0)
                                    {
                                        goto LAB_8003814c;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        PsxRam.WriteI32(AnimStreamBlockBase + (puVar6), unchecked((int)(uVar8 - 0x20)));
                    }

                    // JUSTIFICATION: C# language bridge only — C# cannot jump INTO a nested block,
                    // so the block Ghidra reaches through `goto LAB_8003814c` is lifted to the loop
                    // body and jumped over here. Same three statements, same two predecessors, same
                    // fall-through into the loop tail. No control flow is added.
                    goto SlotTail;

                LAB_8003814c:
                    WriteAnimSharedVar(
                        (int)(uVar8 & 0xf),
                        (ushort)(ReadAnimSharedVar((int)(uVar8 & 0xf))
                                 | PsxRam.ReadU16(AnimStreamBlockBase + (Off_charSharedVarMaskBuf + (iVar5 * 2)))));
                    PsxRam.WriteU8(PsxRam.ReadI32(iVar7 + 0x18) + 0x16a, 0);
                    PsxRam.WriteI32(AnimStreamBlockBase + (puVar6), unchecked((int)(0)));

                SlotTail:
                    iVar9 = iVar9 + 1;
                    iVar5 = iVar9 * 0x10000;
                }
                while (((iVar9 * 0x10000) >> 0x10) < 6);
            }
        }
        else
        {
            puVar3 = streamPtr + 8;
        }

        return puVar3;
    }

    // =====================================================================================
    // Opcode 12 — `eye_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_ApplyCharEffect @ 0x80038D5C (VS.EXE)
    // Opcode 12. THE ONE NAME DISCREPANCY IN THIS FAMILY, ADJUDICATED HERE.
    //
    // The image's own name table calls opcode 12 `eye_set`. Ghidra carries AnimCmd_ApplyCharEffect.
    // The two do not say the same thing, so what the body actually does decides it:
    //   * it resolves a TRANSLATE target through FUN_8003f228 @ 0x8003F228, which returns a
    //     character object's +0x114, an effect object's +0x3C, or a slot of UNK_801F2080;
    //   * it resolves a ROTATE target through FUN_8003f2b0 @ 0x8003F2B0, which returns a character
    //     object's +0x11C, a slot of DAT_801F2000, or — for a spec of 6 or more — DAT_1F800084;
    //   * it latches three rotation values and raises DAT_80099066;
    //   * every frame it calls FUN_8003f994 @ 0x8003F994, 756 bytes, which
    //     docs/structure-ch-bin-files.history.md §26.4 closes on the GAME.EXE homologue
    //     (0x8003FAE8) as a position-and-rotation interpolator: it differences target against
    //     current on the flag frame, then integrates over the following frames.
    // That is a transform interpolation applied to a resolved character or effect. Nothing in the
    // body touches an eye, a camera matrix or a view. THE EVIDENCE SUPPORTS THE GHIDRA NAME, and
    // it is kept for both the annotation and the C# method.
    //
    // `eye_set` is NOT dismissed, because one thread of it is real: the rotate resolver's
    // out-of-range arm returns DAT_1F800084, the GTE scratchpad rotation vector SetupGeometry
    // @ 0x800615CC maintains — the viewpoint. A command whose rotate spec is 6 or more therefore
    // interpolates the VIEW rotation, and that is presumably what the author named the opcode for.
    // But that is one arm of one of the two resolvers, reached only from the stream's data; the
    // handler's own text is character-effect work. So: Ghidra's name stands, and `eye_set` is
    // recorded rather than adopted.
    //
    // AN OFF-BY-ONE IN THE ORIGINAL, KEPT (rule 12). The init arm normally consumes FIVE halfwords
    // and returns streamPtr + 5. But when either resolver returns 0 it returns streamPtr + 6, and
    // the `AnimVm.DAT_800b305a & 1` arm also returns streamPtr + 6. Three exits, two different widths for
    // the same command. Not corrected.
    internal static int AnimCmd_ApplyCharEffect(int streamPtr)
    {
        ushort uVar1 = PsxRam.ReadU16(streamPtr);
        int uVar4 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        int puVar3 = streamPtr + 2;

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            if ((uVar1 & 0x8000) == 0)
            {
                ushort uVar2 = PsxRam.ReadU16(puVar3);
                DAT_80099058 = AnimCmdTransform.FUN_8003f228((uint)(uVar2 & 0xff), uVar4);
                DAT_8009905c = AnimCmdTransform.FUN_8003f2b0((uint)((short)(sbyte)(uVar2 >> 8)), uVar4);

                if (DAT_80099058 == 0 || DAT_8009905c == 0)
                {
                    return streamPtr + 12;
                }

                DAT_80099060 = PsxRam.ReadU16(streamPtr + 4);
                if ((uVar1 & 0x100) != 0)
                {
                    DAT_80099060 = PsxRam.ReadU16(AnimStreamBlockBase + (unchecked((int)((uint)DAT_80099060 << 0x10)) >> 0xf));
                }

                DAT_80099062 = PsxRam.ReadU16(streamPtr + 6);
                if ((uVar1 & 0x200) != 0)
                {
                    DAT_80099062 = PsxRam.ReadU16(AnimStreamBlockBase + (unchecked((int)((uint)DAT_80099062 << 0x10)) >> 0xf));
                }

                DAT_80099064 = PsxRam.ReadU16(streamPtr + 8);
                puVar3 = streamPtr + 10;
                if ((uVar1 & 0x400) != 0)
                {
                    DAT_80099064 = PsxRam.ReadU16(AnimStreamBlockBase + (unchecked((int)((uint)DAT_80099064 << 0x10)) >> 0xf));
                }

                DAT_80099066 = 1;
            }

            FUN_8003f994();
        }
        else if ((uVar1 & 0x8000) == 0)
        {
            puVar3 = streamPtr + 12;
        }

        return puVar3;
    }

    // =====================================================================================
    // Opcode 33 — `eff_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_EffSet @ 0x8003CDE4 (VS.EXE)
    // Opcode 33, which the image also calls `eff_set` — name and symbol agree.
    //
    // Two arms on bit 7 of the sign-extended high byte of word 0, both three halfwords wide:
    //   clear — SPAWN or RE-ARM. Picks one of the sixteen DAT_801faaac slots, directly with the
    //           low nibble or indirectly through g_animSharedVarTable. An empty slot is filled:
    //           resolve an anchor with FUN_8003f228, build the effect through FUN_8003fe98, take
    //           its +0x08, store bit 5 of the flags into the object's +0x50 and the slot's own
    //           ADDRESS into its +0x58. An occupied slot is only re-armed, and only if bit 6 is
    //           set. Either way word 2 lands at the object's +0x52.
    //   set   — TEARDOWN. Three independent tests: bit 6 ORs 4 into an object's +0x50 and leaves
    //           the slot; bit 5 ORs 2 and releases the slot; bit 4 ORs 1 and releases the slot.
    // The two flag words Ghidra keeps, uVar9 (int) and uVar8 (ushort), hold the SAME sign-extended
    // high byte; both are transliterated rather than folded, so the decompilation reads across.
    internal static int AnimCmd_EffSet(int streamPtr)
    {
        int puVar5 = streamPtr + 2;
        ushort word0 = PsxRam.ReadU16(streamPtr);
        int uVar9 = unchecked((int)((uint)word0 << 0x10)) >> 0x18;
        ushort uVar8 = (ushort)(sbyte)(word0 >> 8);

        if ((uVar9 & 0x80) == 0)
        {
            ushort uVar1 = PsxRam.ReadU16(puVar5);
            ushort uVar6 = (ushort)(uVar1 & 0xff);
            ushort uVar2 = PsxRam.ReadU16(streamPtr + 4);
            puVar5 = streamPtr + 6;

            if ((AnimVm.DAT_800b305a & 1) == 0)
            {
                // The original splits the slot address across two locals and adds them, putting the
                // base in whichever one the branch did not use. The sum is the same index either
                // way; it is spelled here as the index the sum produces.
                int slot;
                if ((uVar9 & 0x10) == 0)
                {
                    slot = uVar9 & 0xf;
                }
                else
                {
                    slot = ReadAnimSharedVar(uVar9 & 0xf) & 0xf;
                }

                int piVar10 = Off_801faaac + (slot * 4);

                if (PsxRam.ReadI32(AnimStreamBlockBase + (piVar10)) == 0)
                {
                    int iVar4 = AnimCmdTransform.FUN_8003f228((uint)(uVar1 >> 8), PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8));
                    if (iVar4 == 0)
                    {
                        return puVar5;
                    }

                    iVar4 = FUN_8003fe98(iVar4, uVar6);
                    iVar4 = PsxRam.ReadI32(iVar4 + 8);
                    PsxRam.WriteI32(AnimStreamBlockBase + (piVar10), iVar4);
                    if (iVar4 == 0)
                    {
                        return puVar5;
                    }

                    PsxRam.WriteU16(iVar4 + 0x50, (ushort)(uVar8 & 0x20));
                    PsxRam.WriteI32(
                        PsxRam.ReadI32(AnimStreamBlockBase + (piVar10)) + 0x58,
                        DAT_801faaacAddress + (slot * 4));
                }
                else
                {
                    if ((uVar8 & 0x40) == 0)
                    {
                        return puVar5;
                    }

                    FUN_80053970(
                        PsxRam.ReadI32(AnimStreamBlockBase + (piVar10)), PTR_DAT_800217f0Address, uVar6);
                    PsxRam.WriteU16(
                        PsxRam.ReadI32(AnimStreamBlockBase + (piVar10)) + 0x50, (ushort)(uVar8 & 0x20));
                }

                PsxRam.WriteU16(PsxRam.ReadI32(AnimStreamBlockBase + (piVar10)) + 0x52, uVar2);
            }
        }
        else if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            if ((uVar9 & 0x40) != 0
                && PsxRam.ReadI32(AnimStreamBlockBase + (Off_801faaac + ((uVar9 & 0xf) * 4))) != 0)
            {
                int obj = PsxRam.ReadI32(AnimStreamBlockBase + (Off_801faaac + ((uVar9 & 0xf) * 4)));
                PsxRam.WriteU16(obj + 0x50, (ushort)(PsxRam.ReadU16(obj + 0x50) | 4));
            }

            if ((uVar8 & 0x20) != 0
                && PsxRam.ReadI32(AnimStreamBlockBase + (Off_801faaac + ((uVar8 & 0xf) * 4))) != 0)
            {
                int obj = PsxRam.ReadI32(AnimStreamBlockBase + (Off_801faaac + ((uVar8 & 0xf) * 4)));
                PsxRam.WriteU16(obj + 0x50, (ushort)(PsxRam.ReadU16(obj + 0x50) | 2));
                PsxRam.WriteI32(AnimStreamBlockBase + (Off_801faaac + ((uVar8 & 0xf) * 4)), 0);
            }

            if ((uVar8 & 0x10) != 0
                && PsxRam.ReadI32(AnimStreamBlockBase + (Off_801faaac + ((uVar8 & 0xf) * 4))) != 0)
            {
                int obj = PsxRam.ReadI32(AnimStreamBlockBase + (Off_801faaac + ((uVar8 & 0xf) * 4)));
                PsxRam.WriteU16(obj + 0x50, (ushort)(PsxRam.ReadU16(obj + 0x50) | 1));
                PsxRam.WriteI32(AnimStreamBlockBase + (Off_801faaac + ((uVar8 & 0xf) * 4)), 0);
            }
        }

        return puVar5;
    }

    // =====================================================================================
    // Opcode 34 — `att_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_AttSet @ 0x8003D0B4 (VS.EXE)
    // Opcode 34, which the image also calls `att_set` — name and symbol agree. Three halfwords,
    // always, on every path.
    //
    // PARTIAL — the combat semantics belong to a later slice, and are NOT claimed here. What the
    // control flow closes: it resolves an anchor with FUN_8003f228, hands it plus the three
    // DAT_801faa84/88/8c values to FUN_800438c0 @ 0x800438C0 (6248 bytes, out of slice) together
    // with one of the two 0x3C-byte list records, and turns the result into a bit ORed onto
    // g_animSharedVarTable. In mode 0 the bit is word 2 as given, or word 2 shifted left once when
    // the result's +0x0C is -1. In mode 1 the bit is shifted once per character slot until the
    // result's +0x0C + 0x18 matches that slot's object, up to six. One flag then zeroes the three
    // deltas and copies FUN_800438c0's three output shorts back through the anchor.
    // docs/structure-ch-bin-files.md classes this as one of the seven proven OR-writers of
    // g_animSharedVarTable and confirms the mask starts as an immediate stream word.
    //
    // THE STORE AT +0x18 HAPPENS BEFORE THE NULL TEST, AND IS KEPT (rule 12). Ghidra places
    // `*(undefined1 *)(iVar4 + 0x18) = 1;` above `if (iVar4 != 0)`, which is the MIPS branch-delay
    // slot executing on the taken branch: a null result still gets a byte written to address 0x18.
    // It is not reordered and not guarded. On desktop PsxRam cannot resolve 0x18 and the write is
    // a no-op, which is the closest observable equivalent of a store into the PSX kernel area.
    internal static int AnimCmd_AttSet(int streamPtr)
    {
        ushort uVar1 = PsxRam.ReadU16(streamPtr);
        int iVar7 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        ushort uVar2 = (ushort)(sbyte)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        ushort uStack_32 = PsxRam.ReadU16(streamPtr + 4);

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            int puVar3 = AnimCmdTransform.FUN_8003f228((uint)(PsxRam.ReadU16(streamPtr + 2) & 0xff), iVar7);
            if (puVar3 != 0)
            {
                uint uVar6 = (uint)((unchecked((int)((uint)uVar1 << 0x10)) >> 0x18) & 1);

                // JUSTIFICATION: C# language bridge only
                // RELATION: `short uStack_30[3]` and `short uStack_28[3]` are two three-halfword
                // LOCALS the original passes to FUN_800438c0 by address, one in and one out. C#
                // cannot take the address of a local, and an array reference is the language's
                // own way of passing one. Same storage, same lifetime, same two arguments.
                short[] uStack_30 = new short[3];
                short[] uStack_28 = new short[3];

                uStack_30[2] = (short)PsxRam.ReadI32(AnimStreamBlockBase + (Off_801faa8c));
                uStack_30[0] = (short)PsxRam.ReadI32(AnimStreamBlockBase + (Off_801faa84));
                uStack_30[1] = (short)PsxRam.ReadI32(AnimStreamBlockBase + (Off_801faa88));

                int iVar4 = FUN_800438c0(
                    DAT_80083c78Address + ((int)uVar6 * 0x3c), puVar3, uStack_30, uStack_28, uVar6);

                PsxRam.WriteU8(iVar4 + 0x18, 1);

                if (iVar4 != 0)
                {
                    int iVar5 = 0;
                    if (uVar6 == 0)
                    {
                        if (PsxRam.ReadI32(iVar4 + 0xc) == -1)
                        {
                            WriteAnimSharedVar(
                                uVar2 & 0xf,
                                (ushort)(ReadAnimSharedVar(uVar2 & 0xf) | (ushort)(uStack_32 << 1)));
                        }
                        else
                        {
                            goto LAB_8003d274;
                        }
                    }
                    else
                    {
                        do
                        {
                            iVar5 = iVar5 + 1;
                            if (PsxRam.ReadI32(iVar4 + 0xc) + 0x18 == PsxRam.ReadI32(iVar7 + 0x18))
                            {
                                goto LAB_8003d274;
                            }

                            uStack_32 = (ushort)(uStack_32 << 1);
                            iVar7 = iVar7 + 4;
                        }
                        while (iVar5 < 6);
                    }

                    goto AfterMask;

                LAB_8003d274:
                    WriteAnimSharedVar(
                        uVar2 & 0xf, (ushort)(ReadAnimSharedVar(uVar2 & 0xf) | uStack_32));

                AfterMask:
                    if (((unchecked((int)((uint)uVar1 << 0x10)) >> 0x1c) & 1) != 0)
                    {
                        PsxRam.WriteI32(AnimStreamBlockBase + (Off_801faa8c), 0);
                        PsxRam.WriteI32(AnimStreamBlockBase + (Off_801faa88), 0);
                        PsxRam.WriteI32(AnimStreamBlockBase + (Off_801faa84), 0);
                        PsxRam.WriteU16(puVar3, (ushort)uStack_28[0]);
                        PsxRam.WriteU16(puVar3 + 2, (ushort)uStack_28[1]);
                        PsxRam.WriteU16(puVar3 + 4, (ushort)uStack_28[2]);
                    }

                    PsxRam.WriteU8(iVar4 + 0x18, 0);
                }
            }
        }

        return streamPtr + 6;
    }

    // =====================================================================================
    // Opcode 39 — `ch_eff_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_ChEffSet @ 0x8003DCBC (VS.EXE)
    // Opcode 39, which the image also calls `ch_eff_set` — name and symbol agree. 1784 bytes, the
    // largest handler in this family.
    //
    // IT IS ALSO AN ORDINARY ROUTINE, AND THE OPCODE IS 39. The dispatch entry at 0x80082390 is
    // index 39, and 0x80082390 = 0x800822F4 + 39 * 4. Its other two callers reach it OUTSIDE the
    // table with a synthetic four-halfword command whose first word is 0x8000:
    //   * ExecuteAnimStreamBatch @ 0x800367E8, line 31 — the head of the frame, before the loop
    //     over the sixteen mesh slots;
    //   * AnimCmd_CheffWait @ 0x8003EB00, opcode 44, further down this file.
    // 0x8000 sets bit 7 of the sign-extended high byte, which is the arm that ticks all sixteen
    // ChEff slots and touches no stream. So opcode 39 arms a slot, and the same entry point,
    // called with 0x8000, is the per-frame service for every armed slot. The interpreter runs that
    // service ONCE at the head of the frame, symmetrically with the AnimCmd_SetCharRenderState
    // 0x8000 call at its tail.
    //
    //   arm  (bit clear, five halfwords) — chooses a slot: bits 8..10 of word 2 name one directly
    //        as (bits - 1), otherwise the first free of slots 4..15, falling back to 4 when all
    //        sixteen are taken. Reads the blob pointer out of g_cdFileBufferTable indexed by
    //        word 1's high byte, and stores it into both the start column and the cursor column.
    //        The metadata id comes from word 1's low byte, three ways on bits 0..1 of the flags:
    //        used as is, taken from byte 3 of a g_renderMetadataBuffer entry, or found by scanning
    //        all sixty-four entries for a matching byte 2 and taking their byte 3.
    //   tick (bit set, one halfword) — for each of the sixteen slots, decrement the countdown; on
    //        zero, consume ONE GROUP of the blob and re-arm the countdown. Ghidra's own CERTAIN
    //        comment on this function states the blob format and is reproduced here: a group is a
    //        two-halfword header (word0 low byte = record count, word0 bit 15 = terminate after
    //        this group, word1 low byte = delay to add) followed by that many five-halfword
    //        records. Bit 30 of the control word means "restart": rewind to the start column and
    //        skip (control >> 24) & 0xF whole groups; without bit 31 as well, the slot is freed.
    //        Each record writes one polygon: its low five bits are a CLUT delta on the latched
    //        base, bits 10..11 pick one of four corner permutations for the X/Y quad, and its
    //        words 3 and 4 are two signed U/V delta pairs on the latched base pair.
    internal static int AnimCmd_ChEffSet(int streamPtr)
    {
        ushort uVar2 = PsxRam.ReadU16(streamPtr);
        int puVar21 = streamPtr + 2;
        uint uVar13 = unchecked((uint)(unchecked((int)((uint)uVar2 << 0x10)) >> 0x18));
        int puVar9;

        if ((uVar13 & 0x80) == 0)
        {
            puVar9 = streamPtr + 10;

            if ((AnimVm.DAT_800b305a & 1) == 0)
            {
                ushort uVar3 = PsxRam.ReadU16(streamPtr + 4);
                uint uVar10 = (uint)(PsxRam.ReadU16(puVar21) & 0xff);
                uint uVar19 = (uint)(PsxRam.ReadU16(puVar21) >> 8);

                if ((uVar3 & 0x800) != 0)
                {
                    uVar10 = ReadAnimSharedVar((int)uVar10);
                }

                int uVar17 = PsxRam.ReadI32(streamPtr + 6);
                uint uVar14 = uVar13 & 3;
                uint uVar11;

                if (uVar14 == 1)
                {
                    uVar11 = PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer +
                        (unchecked((int)(uVar10 << 0x10)) >> 0xe) + 3);
                }
                else
                {
                    uVar11 = uVar10 & 0xff;
                    if (1 < uVar14 && uVar14 == 2)
                    {
                        int iVar25 = 0;
                        int iVar27 = 0;
                        do
                        {
                            uVar14 = (uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + (iVar27 >> 0xe));
                            iVar25 = iVar25 + 1;
                            if (((uVar14 & 0xff0000) >> 0x10) == (uint)(int)(short)uVar10)
                            {
                                uVar10 = uVar14 >> 0x18;
                                break;
                            }

                            iVar27 = iVar25 * 0x10000;
                        }
                        while (((iVar25 * 0x10000) >> 0x10) < 0x40);

                        uVar11 = uVar10 & 0xff;
                    }
                }

                short sVar26;
                if ((uVar3 & 0x700) == 0)
                {
                    int iVar27 = 4;
                    int iVar25 = 0x40000;
                    do
                    {
                        sVar26 = (short)iVar27;
                        iVar27 = iVar27 + 1;
                        if (PsxRam.ReadI32(AnimStreamBlockBase + (Off_801fab30 + (iVar25 >> 0xe))) == 0)
                        {
                            break;
                        }

                        iVar25 = iVar27 * 0x10000;
                        sVar26 = (short)iVar27;
                    }
                    while (((iVar27 * 0x10000) >> 0x10) < 0x10);

                    if (sVar26 == 0x10)
                    {
                        sVar26 = 4;
                    }
                }
                else
                {
                    sVar26 = (short)((sbyte)((byte)(uVar3 >> 8) & 7) + -1);
                }

                int uVar8 = MipsMemory.ReadI32(
                    FileIo.g_cdFileBufferTable, unchecked((int)(uVar19 << 0x10)) >> 0xe);

                PsxRam.WriteI32(AnimStreamBlockBase + (Off_801fabb0 + (sVar26 * 4)), uVar8);
                PsxRam.WriteI32(AnimStreamBlockBase + (Off_801fab70 + (sVar26 * 4)), uVar8);
                PsxRam.WriteU8(AnimStreamBlockBase + (Off_801fabf0 + sVar26), (byte)uVar3);

                PsxRam.WriteI32(AnimStreamBlockBase + (Off_801fab30 + (sVar26 * 4)), unchecked((int)(unchecked((uVar11 * 0x100) + (uVar19 * 0x10000) + 1
                              + ((uint)((unchecked((int)((uint)uVar2 << 0x10)) >> 0x1b) & 0xf)
                                 + ((uVar13 & 4) * 0x20)) * 0x1000000))));

                PsxRam.WriteI32(AnimStreamBlockBase + (Off_801fac00 + (sVar26 * 4)), uVar17);
                puVar9 = streamPtr + 10;
            }
        }
        else
        {
            puVar9 = puVar21;

            if ((AnimVm.DAT_800b305a & 1) == 0)
            {
                int iVar27 = 0;
                int iVar25 = 0;
                do
                {
                    iVar25 = iVar25 >> 0x10;
                    int iVar24 = PsxRam.ReadI32(AnimStreamBlockBase + (Off_801fab30 + (iVar25 * 4)));

                    if (iVar24 != 0)
                    {
                        uVar13 = unchecked((uint)(iVar24 - 1));
                        puVar21 = PsxRam.ReadI32(AnimStreamBlockBase + (Off_801fabb0 + (iVar25 * 4)));
                        int uVar17 = PsxRam.ReadI32(AnimStreamBlockBase + (Off_801fac00 + (iVar25 * 4)));
                        byte bVar1 = PsxRam.ReadU8(AnimStreamBlockBase + (Off_801fabf0 + iVar25));

                        if ((uVar13 & 0xff) == 0)
                        {
                            if ((uVar13 & 0x40000000) != 0)
                            {
                                if (-1 < (int)uVar13)
                                {
                                    PsxRam.WriteI32(AnimStreamBlockBase + (Off_801fab30 + (iVar25 * 4)), 0);
                                    goto LAB_8003e368;
                                }

                                puVar21 = PsxRam.ReadI32(AnimStreamBlockBase + (Off_801fab70 + (iVar25 * 4)));
                                uint uVar10r = uVar13 >> 0x18;
                                uVar13 = uVar13 & 0xbfffffff;
                                uVar10r = uVar10r & 0xf;
                                iVar25 = 0;

                                if (uVar10r != 0)
                                {
                                    do
                                    {
                                        ushort uVar2r = PsxRam.ReadU16(puVar21);
                                        puVar21 = puVar21 + 4;
                                        iVar24 = 0;
                                        if ((byte)uVar2r != 0)
                                        {
                                            do
                                            {
                                                iVar24 = iVar24 + 1;
                                                puVar21 = puVar21 + 10;
                                            }
                                            while (((iVar24 * 0x10000) >> 0x10) < (byte)uVar2r);
                                        }

                                        iVar25 = iVar25 + 1;
                                    }
                                    while (((iVar25 * 0x10000) >> 0x10) < (int)uVar10r);
                                }
                            }

                            uint uVar10 = (uVar13 >> 8) & 0xff;

                            // Byte cursors into the two rendering buffers. The original walks them
                            // as `ushort *` at stride 0x10 and `char *` at stride 0x34; every step
                            // below is that step in bytes.
                            int puVar20 = (int)uVar10 * 0x20;
                            ushort uVar2g = PsxRam.ReadU16(puVar21);
                            ushort uVar3g = PsxRam.ReadU16(puVar21 + 2);
                            puVar21 = puVar21 + 4;
                            iVar25 = 0;

                            if ((uVar2g & 0xff) != 0)
                            {
                                int pcVar23 = 0x31 + ((int)uVar10 * 0x34);
                                int puVar18 = 4 + ((int)uVar10 * 0x20);

                                do
                                {
                                    ushort uVar7 = PsxRam.ReadU16(puVar21);
                                    PsxRam.WriteU16(AnimVm.DAT_801f7180 + (pcVar23 - 0x23), (ushort)((short)((uint)uVar17 >> 0x10) + (uVar7 & 0x1f)));

                                    ushort uVar12 = (ushort)(uVar7 & 0xc00);
                                    ushort uVar4 = PsxRam.ReadU16(puVar21 + 4);
                                    int puVar22 = puVar21 + 6;
                                    ushort uVar15 = (ushort)(((PsxRam.ReadU16(puVar21 + 2) & 0xff)
                                                              - 0x80) ^ 0xff80);
                                    ushort uVar5 = (ushort)(uVar4 >> 8);
                                    sbyte cVar6 = (sbyte)(PsxRam.ReadU16(puVar21 + 2) >> 8);
                                    short sVar26 = cVar6;

                                    if (uVar12 == 0x400)
                                    {
                                        ushort uVar7a = (ushort)(uVar15 + (uVar4 & 0xff));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20), (ushort)(uVar7a));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 - 2), (ushort)(sVar26));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18), (ushort)(0));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 8), (ushort)(uVar15));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 6), (ushort)(sVar26));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 8), (ushort)(0));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 16), (ushort)(uVar7a));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 14), (ushort)(cVar6 + uVar5));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 16), (ushort)(0));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 24), (ushort)(uVar15));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 22), (ushort)(cVar6 + uVar5));
                                        goto LAB_8003e2a8;
                                    }

                                    if (0x400 < uVar12)
                                    {
                                        if (uVar12 == 0x800)
                                        {
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20), (ushort)(uVar15));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 - 2), (ushort)(cVar6 + uVar5));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18), (ushort)(0));
                                            ushort uVar7b = (ushort)(uVar15 + (uVar4 & 0xff));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 8), (ushort)(uVar7b));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 6), (ushort)(cVar6 + uVar5));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 8), (ushort)(0));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 16), (ushort)(uVar15));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 14), (ushort)(sVar26));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 16), (ushort)(0));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 24), (ushort)(uVar7b));
                                        }
                                        else
                                        {
                                            // Ghidra emits this guard although uVar12 is masked to
                                            // 0xC00 and cannot be anything but 0xC00 here. Kept as
                                            // written rather than pruned.
                                            if (uVar12 != 0xc00)
                                            {
                                                goto LAB_8003e2b4;
                                            }

                                            ushort uVar7c = (ushort)(uVar15 + (uVar4 & 0xff));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20), (ushort)(uVar7c));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 - 2), (ushort)(cVar6 + uVar5));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18), (ushort)(0));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 8), (ushort)(uVar15));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 6), (ushort)(cVar6 + uVar5));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 8), (ushort)(0));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 16), (ushort)(uVar7c));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 14), (ushort)(sVar26));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 16), (ushort)(0));
                                            PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 24), (ushort)(uVar15));
                                        }

                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 22), (ushort)(sVar26));
                                        goto LAB_8003e2a8;
                                    }

                                    if ((uVar7 & 0xc00) == 0)
                                    {
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20), (ushort)(uVar15));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 - 2), (ushort)(sVar26));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18), (ushort)(0));
                                        ushort uVar7d = (ushort)(uVar15 + (uVar4 & 0xff));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 8), (ushort)(uVar7d));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 6), (ushort)(sVar26));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 8), (ushort)(0));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 16), (ushort)(uVar15));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 14), (ushort)(cVar6 + uVar5));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 16), (ushort)(0));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar20 + 24), (ushort)(uVar7d));
                                        PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 22), (ushort)(cVar6 + uVar5));
                                        goto LAB_8003e2a8;
                                    }

                                    // JUSTIFICATION: C# language bridge only — the three arms above
                                    // reach LAB_8003e2a8 by fall-through in the decompilation, and
                                    // C# cannot jump into a nested block. The two labels are lifted
                                    // to this level and the fall-throughs spelled as jumps. Same
                                    // predecessors, same order, nothing added.
                                    goto LAB_8003e2b4;

                                LAB_8003e2a8:
                                    PsxRam.WriteU16(AnimVm.DAT_801f2180 + (puVar18 + 24), (ushort)(0));
                                    puVar18 = puVar18 + 0x20;
                                    puVar20 = puVar20 + 0x20;

                                LAB_8003e2b4:
                                    iVar25 = iVar25 + 1;
                                    sbyte cVar16 = (sbyte)((byte)PsxRam.ReadU16(puVar22)
                                                           + (byte)uVar17);
                                    uVar7 = PsxRam.ReadU16(puVar21 + 8);
                                    puVar21 = puVar21 + 10;
                                    cVar6 = (sbyte)((byte)(PsxRam.ReadU16(puVar22) >> 8)
                                                    + (byte)((uint)uVar17 >> 8));

                                    PsxRam.WriteU8(AnimVm.DAT_801f7180 + (pcVar23 - 0x25), (byte)cVar16);
                                    PsxRam.WriteU8(AnimVm.DAT_801f7180 + (pcVar23 - 0x24), (byte)cVar6);
                                    PsxRam.WriteU8(AnimVm.DAT_801f7180 + (pcVar23 - 0x18), (byte)cVar6);
                                    PsxRam.WriteU8(AnimVm.DAT_801f7180 + (pcVar23 - 0x0d), (byte)cVar16);

                                    cVar16 = (sbyte)((byte)uVar7 + (byte)cVar16);
                                    cVar6 = (sbyte)((byte)cVar6 + (byte)(uVar7 >> 8));

                                    PsxRam.WriteU8(AnimVm.DAT_801f7180 + (pcVar23 - 0x19), (byte)cVar16);
                                    PsxRam.WriteU8(AnimVm.DAT_801f7180 + (pcVar23 - 0x0c), (byte)cVar6);
                                    PsxRam.WriteU8(AnimVm.DAT_801f7180 + (pcVar23 - 0x01), (byte)cVar16);
                                    PsxRam.WriteU8(AnimVm.DAT_801f7180 + (pcVar23), (byte)cVar6);

                                    pcVar23 = pcVar23 + 0x34;
                                }
                                while (((iVar25 * 0x10000) >> 0x10) < (uVar2g & 0xff));
                            }

                            uVar13 = uVar13
                                     | (uint)((int)(((uint)bVar1 + (uint)(byte)uVar3g) * 0x10000) >> 0x10)
                                     | (((uint)uVar2g >> 8) & 0x80) << 0x17;
                        }

                        iVar25 = ((iVar27 << 0x10) >> 0xe);
                        PsxRam.WriteI32(AnimStreamBlockBase + (Off_801fabb0 + iVar25), puVar21);
                        PsxRam.WriteI32(AnimStreamBlockBase + (Off_801fab30 + iVar25), unchecked((int)(uVar13)));
                    }

                LAB_8003e368:
                    iVar27 = iVar27 + 1;
                    iVar25 = iVar27 * 0x10000;
                }
                while (((iVar27 * 0x10000) >> 0x10) < 0x10);
            }
        }

        return puVar9;
    }

    // =====================================================================================
    // Opcode 40 — `ch_dan_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_ChDanSet @ 0x8003E3B4 (VS.EXE)
    // Opcode 40, which the image also calls `ch_dan_set` — name and symbol agree. Three halfwords
    // on every path.
    //
    // PARTIAL — the combat semantics belong to a later slice. What the control flow closes:
    //   clear (bit 7 of the sign-extended high byte) — resolves TWO targets out of word 1, low
    //         byte through FUN_8003f228 and high byte through FUN_8003f2b0, and if both resolve
    //         passes them plus word 2 sign-extended and the flag byte to FUN_80043598
    //         @ 0x80043598 (312 bytes, out of slice).
    //   set   — reads the pending record at the task context's +0x30 and finalises it: clears
    //         `word2 * 0x7F` out of one g_animSharedVarTable entry, then ORs word 2 back in,
    //         either as is when the record's +0x0C is -1, or shifted once per character slot until
    //         the record's +0x0C + 0x18 matches that slot. On a match it copies the record's
    //         +0x2C/+0x2E/+0x30 through the resolved pointer, clears the record's +0x18 and drops
    //         the context's +0x30 to 0.
    // docs/structure-ch-bin-files.md lists it among the seven proven OR-writers of
    // g_animSharedVarTable, with a base mask taken from an immediate stream word.
    //
    // PARTIAL: the target index is `word1 & 0xff` and the indirection index `flags & 0x3f`, both
    // wider than the sixteen entries g_animSharedVarTable is sized at. The byte region this file
    // models lets an over-range index alias into the next global, which is what the console does.
    internal static int AnimCmd_ChDanSet(int streamPtr)
    {
        uint uVar1 = unchecked((uint)(unchecked((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10)) >> 0x18));
        int iVar8 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);

        if ((uVar1 & 0x80) == 0)
        {
            ushort uVar5 = PsxRam.ReadU16(streamPtr + 2);
            ushort uVar6 = PsxRam.ReadU16(streamPtr + 4);

            if ((AnimVm.DAT_800b305a & 1) == 0)
            {
                int iVar2 = AnimCmdTransform.FUN_8003f228((uint)(uVar5 & 0xff), iVar8);
                iVar8 = AnimCmdTransform.FUN_8003f2b0((uint)(uVar5 >> 8), iVar8);
                if (iVar2 != 0 && iVar8 != 0)
                {
                    FUN_80043598(iVar2, iVar8, (short)uVar6, uVar1 & 0xff);
                }
            }
        }
        else
        {
            ushort uVar5 = PsxRam.ReadU16(streamPtr + 2);
            uint uVar9 = (uint)(uVar5 & 0xff);
            uVar1 = unchecked((uint)(unchecked((int)((uint)uVar5 << 0x10)) >> 0x18));
            uVar5 = (ushort)(uVar5 >> 8);

            if ((uVar1 & 0x40) != 0)
            {
                uVar5 = ReadAnimSharedVar((int)(uVar1 & 0x3f));
            }

            ushort uVar6 = PsxRam.ReadU16(streamPtr + 4);

            if ((AnimVm.DAT_800b305a & 1) == 0)
            {
                int puVar3 = AnimCmdTransform.FUN_8003f228((uint)(unchecked((uint)(short)uVar5)), iVar8);
                if (puVar3 != 0)
                {
                    int puVar7 = (int)uVar9;
                    int iVar2 = PsxRam.ReadI32(iVar8 + 0x30);
                    ushort uVar5b = (ushort)(~(uVar6 * 0x7f) & ReadAnimSharedVar(puVar7));
                    WriteAnimSharedVar(puVar7, uVar5b);

                    if (iVar2 != 0)
                    {
                        if (PsxRam.ReadI32(iVar2 + 0xc) == -1)
                        {
                            WriteAnimSharedVar(puVar7, (ushort)(uVar6 | uVar5b));
                            goto LAB_8003e564;
                        }

                        int iVar4 = 0;
                        do
                        {
                            uVar6 = (ushort)(uVar6 << 1);
                            if (PsxRam.ReadI32(iVar2 + 0xc) + 0x18
                                == PsxRam.ReadI32(((iVar4 << 0x10) >> 0xe) + iVar8 + 0x18))
                            {
                                WriteAnimSharedVar(
                                    (int)uVar9, (ushort)(uVar6 | ReadAnimSharedVar((int)uVar9)));
                                goto LAB_8003e564;
                            }

                            iVar4 = iVar4 + 1;
                        }
                        while (((iVar4 * 0x10000) >> 0x10) < 6);

                        goto NotFinalised;

                    LAB_8003e564:
                        PsxRam.WriteU16(puVar3, PsxRam.ReadU16(iVar2 + 0x2c));
                        PsxRam.WriteU16(puVar3 + 2, PsxRam.ReadU16(iVar2 + 0x2e));
                        PsxRam.WriteU16(puVar3 + 4, PsxRam.ReadU16(iVar2 + 0x30));
                        PsxRam.WriteU8(PsxRam.ReadI32(iVar8 + 0x30) + 0x18, 0);
                        PsxRam.WriteI32(iVar8 + 0x30, 0);

                    NotFinalised:
                        ;
                    }
                }
            }
        }

        return streamPtr + 6;
    }

    // =====================================================================================
    // Opcode 41 — `hitz_set`
    // =====================================================================================

    // GHIDRA: LAB_8003e60c @ 0x8003E60C (VS.EXE)
    // GHIDRA HAS NO FUNCTION HERE. 0x8003E60C carries a plain Label, reached only by the dispatch
    // table's DATA reference from 0x80082398 — index 41. The annotation therefore names the label,
    // which is what the project database actually holds; the C# name comes from the image's own
    // name table, whose entry 41 is `hitz_set`.
    //
    // Because there is no set prototype, the decompiler recovered the signature itself:
    // `ushort *(ushort *param_1)`. One parameter. That is the strongest single piece of evidence
    // in this file that the dispatch contract's second argument goes unread by these handlers.
    //
    // PARTIAL — the combat semantics belong to a later slice. What the control flow closes: it
    // resolves one target with FUN_8003f228, registers it into BOTH 0x3C-byte list records through
    // FUN_80045130 @ 0x80045130 (1764 bytes, out of slice) with a bound of 0x40, then walks the
    // second record's chain from DAT_80083cb8 looking for a node whose +0x19 is 0 and whose +0x18
    // is '@' (0x40). For such a node it shifts word 3's mask once per character slot until the
    // node's +0x0C + 0x18 matches, ORs the shifted mask onto g_animSharedVarTable and clears both
    // node bytes. Four halfwords, always.
    // docs/structure-ch-bin-files.history.md records the same '@' owner code and the same pair of
    // list heads on the GAME.EXE homologue.
    //
    // PARTIAL: the destination index is `word2 & 0xff`, wider than the sixteen entries
    // g_animSharedVarTable is sized at — see the note on AnimCmd_ChDanSet.
    internal static int AnimCmd_HitzSet(int param_1)
    {
        ushort uVar6 = PsxRam.ReadU16(param_1 + 2);
        ushort uVar1 = PsxRam.ReadU16(param_1 + 4);
        int iVar7 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);

        if (((uVar1 >> 8) & 1) != 0)
        {
            uVar6 = PsxRam.ReadU16(AnimStreamBlockBase + (unchecked((int)((uint)uVar6 << 0x10)) >> 0xf));
        }

        ushort uVar2 = PsxRam.ReadU16(param_1 + 6);

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            int iVar3 = AnimCmdTransform.FUN_8003f228(
                (uint)(unchecked((uint)(unchecked((int)((uint)PsxRam.ReadU16(param_1) << 0x10)) >> 0x18))),
                iVar7);

            if (iVar3 != 0)
            {
                FUN_80045130(DAT_80083c78Address, 0x40, iVar3, (short)uVar6);
                iVar3 = FUN_80045130(DAT_80083cb4Address, 0x40, iVar3, (short)uVar6);

                if (iVar3 != 0 && PsxRam.ReadI32(DAT_80083cb8Address) != 0)
                {
                    int piVar4 = PsxRam.ReadI32(DAT_80083cb8Address);
                    do
                    {
                        if (PsxRam.ReadU8(piVar4 + 0x19) == 0
                            && (sbyte)PsxRam.ReadU8(piVar4 + 0x18) == '@')
                        {
                            int iVar5 = 0;
                            iVar3 = iVar7;
                            uVar6 = uVar2;
                            do
                            {
                                if (PsxRam.ReadI32(piVar4 + 0xc) + 0x18
                                    == PsxRam.ReadI32(iVar3 + 0x18))
                                {
                                    WriteAnimSharedVar(
                                        uVar1 & 0xff,
                                        (ushort)(uVar6 | ReadAnimSharedVar(uVar1 & 0xff)));
                                    PsxRam.WriteU8(piVar4 + 0x19, 0);
                                    PsxRam.WriteU8(piVar4 + 0x18, 0);
                                    break;
                                }

                                uVar6 = (ushort)(uVar6 << 1);
                                iVar5 = iVar5 + 1;
                                iVar3 = iVar3 + 4;
                            }
                            while (iVar5 < 6);
                        }

                        piVar4 = PsxRam.ReadI32(piVar4);
                    }
                    while (piVar4 != 0);
                }
            }
        }

        return param_1 + 8;
    }

    // =====================================================================================
    // Opcode 44 — `cheff_wait`
    // =====================================================================================

    // GHIDRA: AnimCmd_CheffWait @ 0x8003EA78 (VS.EXE)
    // Opcode 44, which the image also calls `cheff_wait` — name and symbol agree. Ghidra carries a
    // CERTAIN comment on it: homologous to GAME.EXE's AnimCmd_CheffWait, either calls
    // AnimCmd_ChEffSet with a synthetic 0x8000 command or clears the slot table, with no direct
    // CH_BIN read. The body below is exactly that. One halfword, always.
    //
    // Bit 8 of the command word chooses: clear runs one ChEff tick immediately, set frees all
    // sixteen slots.
    //
    // PARTIAL — THE SYNTHETIC COMMAND. The original builds `ushort auStack_10[4]` in its own stack
    // frame, writes 0x8000 into word 0 and passes the frame address. C# cannot take the address of
    // a local, and this file will not invent a second entry point into AnimCmd_ChEffSet to dodge
    // that (rule 15). So the four halfwords live in a static region with a real modelled address
    // and the call is made as written. Until VS_EXE_exe grows a ResolveAddress that answers for
    // this file's regions, PsxRam cannot resolve it, the callee reads 0 instead of 0x8000 and
    // takes its init arm instead of its tick arm. That gap is named at the top of this file; it is
    // one cross-file change away and nothing calls in here yet.
    internal static int AnimCmd_CheffWait(int streamPtr)
    {
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            int iVar2 = 0;
            if (((PsxRam.ReadU16(streamPtr) >> 8) & 1) == 0)
            {
                MipsMemory.WriteU16(auStack_10, 0, 0x8000);
                AnimCmd_ChEffSet(auStack_10Address);
            }
            else
            {
                int iVar1 = 0;
                do
                {
                    PsxRam.WriteI32(AnimStreamBlockBase + (Off_801fab30 + (iVar1 >> 0xe)), 0);
                    iVar2 = iVar2 + 1;
                    iVar1 = iVar2 * 0x10000;
                }
                while (((iVar2 * 0x10000) >> 0x10) < 0x10);
            }
        }

        return streamPtr + 2;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: stands for the `ushort auStack_10[4]` that AnimCmd_CheffWait — and
    // ExecuteAnimStreamBatch, at 0x800367E8 and 0x80036968 — build on the stack and pass by
    // address. A C# local has no address, so the same four halfwords are given one. Every user
    // writes word 0 and then hands the address over immediately, on one thread, so a single block
    // is observationally the same storage as the per-frame stack slot.
    //
    // THE ADDRESS IS A STACK ADDRESS, NOT A PSX SYMBOL, and it is inside the range the console
    // actually puts this slot in. VS.EXE's crt0 reads DAT_800858dc = 0x00008000 and
    // DAT_800858e0 = 0x00800000 from .data — measured with read-memory at 0x800858DC:
    // `00 80 00 00 00 00 80 00` — so SP starts at (0x00800000 - 8) | 0x80000000 = 0x807FFFF8 and
    // the stack runs down 0x8000 bytes to 0x807F8000. The heap start passes it the rest:
    // 0x800C3DD8 + (((0x00800000 - 8) - 0x00008000) - 0xC3DD4) = 0x807F7FFC, one word below the
    // stack bottom. So 0x807FFFE0 is stack, nothing else in the port models it, and no PSX global
    // is squatted on. An earlier draft put this at 0x801FAC40 and that was WRONG: Ghidra carries
    // DAT_801fac40 there, written by four functions in the 0x8003Exxx opcode block.
    private const int auStack_10Address = unchecked((int)0x807FFFE0);

    private static readonly byte[] auStack_10 =
        LibGpu.RamRegion(auStack_10Address, 8);

    // =====================================================================================
    // The callees these eight handlers reach that are NOT in this slice. Each is declared so the
    // call site above is real, in the original's order and with the original's arguments. None of
    // them is invented here and none is a convenience API: they are the out-of-slice functions,
    // named exactly as Ghidra names them.
    // =====================================================================================

    // GHIDRA: FUN_8003f994 @ 0x8003F994 (VS.EXE)
    private static void FUN_8003f994()
    {
        // BLOCKED: 756 bytes. The per-frame transform interpolator opcode 12 drives through
        // DAT_80099058..DAT_80099066. docs/structure-ch-bin-files.history.md §26.4 describes the
        // GAME.EXE homologue at 0x8003FAE8: on the frame DAT_80099066 is 1 it differences the two
        // resolved targets against the current position and rotation into a velocity triple, then
        // on every later frame integrates the accumulators, writes the scratchpad coordinates as
        // 4.12 fixed point and counts a frame budget down. It owns globals no slice has claimed.
    }

    // GHIDRA: FUN_8003fe98 @ 0x8003FE98 (VS.EXE)
    private static int FUN_8003fe98(int param_1, int param_2)
    {
        // BLOCKED: 192 bytes. AnimCmd_EffSet's effect constructor — its result's +0x08 is what
        // lands in the DAT_801faaac slot. It belongs with the effect-object slice.
        _ = param_1;
        _ = param_2;
        return 0;
    }

    // GHIDRA: FUN_80053970 @ 0x80053970 (VS.EXE)
    private static void FUN_80053970(int param_1, int param_2, int param_3)
    {
        // BLOCKED: 96 bytes. AnimCmd_EffSet's re-arm path calls it on an already-live effect
        // object with the address of PTR_DAT_800217f0. It sits in the 0x80053xxx block beside the
        // task scheduler VS_EXE/TaskSystem.cs already ports, so it belongs to that slice's
        // neighbourhood rather than to this one.
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FUN_800438c0 @ 0x800438C0 (VS.EXE)
    private static int FUN_800438c0(int param_1, int param_2, short[] param_3, short[] param_4,
        uint param_5)
    {
        // BLOCKED: 6248 BYTES — the single largest callee in this family and the whole of the
        // attack-zone resolution AnimCmd_AttSet fronts. It takes one of the two 0x3C-byte list
        // records, the resolved anchor, the movement-delta triple in and a result triple out, and
        // a mode. Its result's +0x0C is the owner the caller matches against the six character
        // slots, and its +0x18 is the byte the caller raises and lowers around the call. Combat is
        // slice 2's subject.
        _ = param_1;
        _ = param_2;
        _ = param_3;
        _ = param_4;
        _ = param_5;
        return 0;
    }

    // GHIDRA: FUN_80043598 @ 0x80043598 (VS.EXE)
    private static void FUN_80043598(int param_1, int param_2, int param_3, uint param_4)
    {
        // BLOCKED: 312 bytes. AnimCmd_ChDanSet's registration arm — it is handed the two resolved
        // targets, word 2 sign-extended and the flag byte. Its counterpart, the record it leaves
        // at the task context's +0x30, is what the same handler's other arm finalises. Slice 2.
        _ = param_1;
        _ = param_2;
        _ = param_3;
        _ = param_4;
    }

    // GHIDRA: FUN_80045130 @ 0x80045130 (VS.EXE)
    private static int FUN_80045130(int param_1, int param_2, int param_3, int param_4)
    {
        // BLOCKED: 1764 bytes. AnimCmd_HitzSet calls it twice, once per 0x3C-byte list record,
        // with a bound of 0x40. It is the registration that puts the '@'-tagged nodes on the chain
        // the same handler then walks. Slice 2.
        _ = param_1;
        _ = param_2;
        _ = param_3;
        _ = param_4;
        return 0;
    }
}
