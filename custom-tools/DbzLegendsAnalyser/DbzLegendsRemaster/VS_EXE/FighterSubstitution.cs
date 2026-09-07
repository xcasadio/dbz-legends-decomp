using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE SUBSTITUTION MANAGER — who is on the field, and who takes their place.
//
// WHY THIS FILE EXISTS, AND WHAT IT UNBLOCKS. FighterTask.UpdateFighter's phase 1 tests
// `fighter + 0x144` and returns immediately when it is zero. The port measured that guard failing
// on every one of 792 fighter-task calls over 400 frames, so phases 2..9 -- the whole of the
// fighter, the command decoder included -- had never run at all. A byte-level scan of the image
// finds exactly four stores to +0x144: three write zero (FighterSetup's creation path, and
// RunFighterSubstitution's own removal arm below), and ONE writes a value, `sw s1,0x144(s4)` at
// 0x800273E4, inside ActivateFighterInSlot. Its only caller is RunFighterSubstitution, whose only
// call site is RunBattleManagerFrame's CtxRoundRequest 0x180 gate. That is the whole of the path,
// and this file is both of its halves.
//
// WHAT +0x144 TURNS OUT TO BE. Not a boolean: ActivateFighterInSlot writes the fighter's own
// character-data buffer pointer into it, read out of the per-slot table at ctx + 0x1620 + slot*4
// that BattleManager's own loader fills. So phase 1's guard reads "this fighter's character data is
// loaded", which is exactly the right thing for a fighter task to refuse to run without.
//
// THE ROUND-REQUEST WORD IS AN ARGUMENT, AND IT USED TO BE DROPPED. Ghidra types
// FUN_80026D98 with two parameters and the call site confirms it: `lw a1,0x2d60(s2)` at
// 0x8005BFDC loads CtxRoundRequest into a1 immediately before `jal 0x80026d98` with `a0 = ctx`.
// The port's call site passed only the context, so every one of the four bits this function
// switches on -- 0x20, 0x40, 0x80, 0x100 -- would have read whatever happened to be in the second
// C# parameter. Fixed at the call site in BattleManager.cs, which now passes the word it just
// tested.
//
// THE FOUR BITS, from the body below:
//   0x80   run the REMOVAL pass: any slot marked `record & 0x210 == 0x10` has its fighter parked
//          (position saved into ctx + 0x1550 + slot*8, state byte and +0x144 zeroed, flag words
//          cleared, colour forced to 0x808080) and its team cursor moved to the next legal slot.
//   0x100  run the two ENTRY passes, slots 0..5 then 6..11: any slot marked
//          `record & 0x210 == 0x210` gets a fighter -- borrowed from another slot of the same team
//          when its own pointer is null -- activated through ActivateFighterInSlot and placed at
//          the position the removal pass saved.
//   0x20   in the removal pass, stop after the FIRST slot removed.
//   0x40   in the entry passes, stop after the first slot whose record has bit 15; and, once team
//          A has stopped that way, skip team B's pass entirely.
// Nothing runs at all unless at least one of the twelve records carries bit 4 (0x10).
internal static class FighterSubstitution
{
    // GHIDRA: ctx + 0x1550 (VS.EXE)
    // Twelve position triples, stride 8, one per slot: the x/y/z the removal pass copies out of a
    // fighter's own +0x114 and the entry pass copies back into the replacement's. Only six bytes of
    // each eight are used; the other two are never read by this function.
    private const int CtxSlotParkedPosition = 0x1550;

    // GHIDRA: ctx + 0x16A0 (VS.EXE)
    // Twelve words, one per slot: the address of that slot's loaded character data. BattleManager's
    // own loader is the writer (`ctx + slot*4 + 0x16a0 = buffer`, three sites); ActivateFighterInSlot
    // is the reader, and what it reads is what lands in the fighter's +0x144.
    private const int CtxSlotCharacterData = 0x16a0;

    // GHIDRA: DAT_80080A80 @ 0x80080A80 (VS.EXE)
    // Six eight-byte rows, indexed by the fighter's own +0x160 (BattleState.FighterIndex, 0..5 --
    // NOT the slot index at +0x173). Read back byte for byte from the image; the six rows are the
    // only ones this table has, because the next eight bytes at 0x80080AA0 are 01 02 02 01 01 01
    // 01 00, which is not of this shape and belongs to whatever follows.
    //
    //   row 0  40 01 00 00 50 01 7F 00     row 3  60 01 00 00 70 01 7F 00
    //   row 1  40 01 00 01 50 01 FF 00     row 4  60 01 80 00 70 01 FF 00
    //   row 2  40 01 80 00 50 01 7F 01     row 5  60 01 00 01 70 01 7F 01
    //
    // As four signed halfwords per row that is (320|352, 0|128|256, 336|368, 127|255|383): an
    // x/y pair copied into the fighter's own +0x156/+0x158, and a second x/y pair handed to
    // LoadImage_ReturnTPageOrClutId as the VRAM position of a 32x1 CLUT. Two columns of three,
    // which is one framebuffer column per team and one row per fighter.
    private static readonly short[] DAT_80080a80 =
    {
        320, 0, 336, 127,
        320, 256, 336, 255,
        320, 128, 336, 383,
        352, 0, 368, 127,
        352, 128, 368, 255,
        352, 256, 368, 383,
    };

    // GHIDRA: DAT_80082ED4 @ 0x80082ED4 (VS.EXE)
    // Not a declaration -- a BASE ADDRESS. ActivateFighterInSlot stores
    // `(characterId - 1) * 8 + 0x80082ED4` into the fighter's own +0x6C, so what matters is the
    // address arithmetic, not the contents; nothing in this file dereferences it. The original
    // writes it as the assembler's own `-0x7ff7d12c`, which is the same 32-bit value.
    private const int Dat80082ed4Address = unchecked((int)0x80082ED4);

    // GHIDRA: RunFighterSubstitution @ 0x80026D98 (VS.EXE)
    // 1448 bytes. One caller, BattleManager.RunBattleManagerFrame, once per frame, gated on
    // CtxRoundRequest bits 0x100/0x180 and with those bits cleared immediately after it returns.
    //
    // param_2 IS CtxRoundRequest, not a spare. See this file's own header note for the evidence and
    // for what each of the four bits selects.
    //
    // THE `|| (true)` GHIDRA PRINTS in the first entry loop is not a dropped condition. The image
    // has the same two-part test in both entry loops -- `andi v0,s8,0x40; beq v0,zero,<skip>` then
    // `lw a3,0x10(sp); beq a3,1,<next slot>` at 0x80027004 and again at 0x80027190 -- and the
    // decompiler folds the first one because bVar2 is provably still 0 there (it is set only on a
    // path that breaks out of that same loop). Both are written out in full below, which is what
    // the machine code does and is equivalent in the first loop.
    internal static void RunFighterSubstitution(int param_1, uint param_2)
    {
        ushort uVar3 = 0;
        int iVar10 = 0;
        int iVar6 = param_1;
        do
        {
            iVar10 = iVar10 + 1;
            uVar3 = (ushort)(uVar3 | PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords));
            iVar6 = iVar6 + BattleState.CtxSlotRecordStride;
        } while (iVar10 < 0xc);

        if ((uVar3 & 0x10) == 0)
        {
            return;
        }

        if ((param_2 & 0x80) != 0)
        {
            int iVar11 = 0;
            iVar6 = param_1;
            iVar10 = param_1;
            do
            {
                if ((PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0x210) == 0x10)
                {
                    int iVar8 = PsxRam.ReadI32(iVar11 * 4 + param_1 + BattleState.CtxFighterSlots);
                    if (iVar8 != 0)
                    {
                        iVar8 = PsxRam.ReadI32(iVar8 + 8);

                        // Park the fighter's position triple in the slot's own saved triple, so the
                        // replacement can be dropped exactly where the outgoing one stood.
                        PsxRam.WriteU16(iVar10 + CtxSlotParkedPosition, PsxRam.ReadU16(iVar8 + 0x114));
                        PsxRam.WriteU16(iVar10 + CtxSlotParkedPosition + 2, PsxRam.ReadU16(iVar8 + 0x116));
                        PsxRam.WriteU16(iVar10 + CtxSlotParkedPosition + 4, PsxRam.ReadU16(iVar8 + 0x118));

                        PsxRam.WriteU8(iVar8 + 0x16a, 0);
                        PsxRam.WriteI32(iVar8 + 0x144, 0);
                        FighterCombat.FUN_80045a38(unchecked((int)0x80083cb4), iVar8 + 0xf8);
                        PsxRam.WriteU16(iVar8 + 0x15e, 0xffff);
                        PsxRam.WriteU16(iVar8 + 4, 0);
                        PsxRam.WriteU8(iVar8 + 0x171, 0);
                        PsxRam.WriteU8(iVar8 + 0x170, 0);
                        PsxRam.WriteU16(iVar8 + 0x15c, 0);
                        PsxRam.WriteU8(iVar8 + 0x16d, 0);
                        PsxRam.WriteI32(iVar8 + 0x138, 0);
                        PsxRam.WriteI32(iVar8 + 0x134, 0);
                        PsxRam.WriteU8(iVar8 + 0x152, 0x80);
                        PsxRam.WriteU8(iVar8 + 0x151, 0x80);
                        PsxRam.WriteU8(iVar8 + 0x150, 0x80);

                        PsxRam.WriteU16(iVar6 + BattleState.CtxSlotRecords,
                            (ushort)(PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0xff6f));

                        // Team A's cursor: if it pointed at the slot just vacated, walk to the first
                        // slot of the whole twelve carrying 0x81, and failing that the first
                        // carrying 0x201. Note the walk runs 0..5 in both passes -- team A's half.
                        if (iVar11 == (short)PsxRam.ReadU16(param_1 + BattleState.CtxActingSlotTeamA))
                        {
                            int iVar4 = 0;
                            int iVar9 = param_1;
                            do
                            {
                                if ((PsxRam.ReadU16(iVar9 + BattleState.CtxSlotRecords) & 0x81) == 0x81)
                                {
                                    PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamA, (ushort)(short)iVar4);
                                    break;
                                }

                                iVar4 = iVar4 + 1;
                                iVar9 = iVar9 + BattleState.CtxSlotRecordStride;
                            } while (iVar4 < 6);

                            if (iVar4 == 6)
                            {
                                iVar4 = 0;
                                iVar9 = param_1;
                                do
                                {
                                    if ((PsxRam.ReadU16(iVar9 + BattleState.CtxSlotRecords) & 0x201) == 0x201)
                                    {
                                        PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamA, (ushort)(short)iVar4);
                                        break;
                                    }

                                    iVar4 = iVar4 + 1;
                                    iVar9 = iVar9 + BattleState.CtxSlotRecordStride;
                                } while (iVar4 < 6);
                            }
                        }

                        // Team B's cursor, the mirror image over slots 6..11.
                        if (iVar11 == (short)PsxRam.ReadU16(param_1 + BattleState.CtxActingSlotTeamB))
                        {
                            int iVar8b = 6;
                            int iVar4 = param_1 + 0x78;
                            do
                            {
                                if ((PsxRam.ReadU16(iVar4 + BattleState.CtxSlotRecords) & 0x81) == 0x81)
                                {
                                    PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamB, (ushort)(short)iVar8b);
                                    break;
                                }

                                iVar8b = iVar8b + 1;
                                iVar4 = iVar4 + BattleState.CtxSlotRecordStride;
                            } while (iVar8b < 0xc);

                            if (iVar8b == 0xc)
                            {
                                iVar8b = 6;
                                iVar4 = param_1 + 0x78;
                                do
                                {
                                    if ((PsxRam.ReadU16(iVar4 + BattleState.CtxSlotRecords) & 0x201) == 0x201)
                                    {
                                        PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamB, (ushort)(short)iVar8b);
                                        break;
                                    }

                                    iVar8b = iVar8b + 1;
                                    iVar4 = iVar4 + BattleState.CtxSlotRecordStride;
                                } while (iVar8b < 0xc);
                            }
                        }

                        if ((param_2 & 0x20) != 0)
                        {
                            break;
                        }
                    }
                }

                iVar10 = iVar10 + 8;
                iVar11 = iVar11 + 1;
                iVar6 = iVar6 + BattleState.CtxSlotRecordStride;
            } while (iVar11 < 0xc);
        }

        if ((param_2 & 0x100) == 0)
        {
            return;
        }

        uint uVar12 = 0;
        bool bVar2 = false;
        int iVar8_2 = 0;
        iVar6 = param_1;
        iVar10 = param_1;
        int iVar11_2 = param_1;
        do
        {
            if (((param_2 & 0x40) == 0 || !bVar2)
                && (PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0x210) == 0x210)
            {
                // Its own pointer is null: borrow the first team-A slot that is not marked 0x200,
                // whose pointer is non-null and whose fighter is NOT already on the field.
                if (PsxRam.ReadI32(iVar8_2 + param_1 + BattleState.CtxFighterSlots) == 0)
                {
                    int iVar5 = 0;
                    int iVar4 = param_1;
                    int iVar9 = param_1;
                    do
                    {
                        if ((PsxRam.ReadU16(iVar4 + BattleState.CtxSlotRecords) & 0x200) == 0)
                        {
                            int iVar7 = PsxRam.ReadI32(iVar9 + BattleState.CtxFighterSlots);
                            if (iVar7 != 0 && PsxRam.ReadI32(PsxRam.ReadI32(iVar7 + 8) + 0x144) == 0)
                            {
                                PsxRam.WriteI32(iVar11_2 + BattleState.CtxFighterSlots, iVar7);
                                PsxRam.WriteI32(iVar9 + BattleState.CtxFighterSlots, 0);
                                break;
                            }
                        }

                        iVar5 = iVar5 + 1;
                        iVar4 = iVar4 + BattleState.CtxSlotRecordStride;
                        iVar9 = iVar9 + 4;
                    } while (iVar5 < 6);
                }

                ActivateFighterInSlot(PsxRam.ReadI32(iVar11_2 + BattleState.CtxFighterSlots), uVar12 & 0xffff);

                int iVar4b = PsxRam.ReadI32(PsxRam.ReadI32(iVar11_2 + BattleState.CtxFighterSlots) + 8);
                PsxRam.WriteU16(iVar4b + 0x114, PsxRam.ReadU16(iVar10 + CtxSlotParkedPosition));
                PsxRam.WriteU16(iVar4b + 0x116, PsxRam.ReadU16(iVar10 + CtxSlotParkedPosition + 2));
                PsxRam.WriteU16(iVar4b + 0x118, PsxRam.ReadU16(iVar10 + CtxSlotParkedPosition + 4));

                uVar3 = (ushort)(PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0xffef);
                PsxRam.WriteU16(iVar6 + BattleState.CtxSlotRecords, uVar3);
                if ((PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x2000) == 0)
                {
                    PsxRam.WriteU16(iVar6 + BattleState.CtxSlotRecords, (ushort)(uVar3 | 0x80));
                }

                if ((PsxRam.ReadU16(param_1
                        + (short)PsxRam.ReadU16(param_1 + BattleState.CtxActingSlotTeamA)
                          * BattleState.CtxSlotRecordStride
                        + BattleState.CtxSlotRecords) & 0x81) != 0x81)
                {
                    PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamA, (ushort)(short)uVar12);
                }

                if ((param_2 & 0x40) != 0
                    && ((int)(short)PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0x8000) != 0)
                {
                    bVar2 = true;
                    break;
                }
            }

            iVar11_2 = iVar11_2 + 4;
            iVar8_2 = iVar8_2 + 4;
            iVar6 = iVar6 + BattleState.CtxSlotRecordStride;
            uVar12 = uVar12 + 1;
            iVar10 = iVar10 + 8;
        } while ((int)uVar12 < 6);

        uVar12 = 6;
        iVar11_2 = param_1 + 0x18;
        iVar8_2 = 0x18;
        iVar6 = param_1 + 0x78;
        iVar10 = param_1 + 0x30;
        do
        {
            if (((param_2 & 0x40) == 0 || !bVar2)
                && (PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0x210) == 0x210)
            {
                if (PsxRam.ReadI32(iVar8_2 + param_1 + BattleState.CtxFighterSlots) == 0)
                {
                    int iVar4 = 6;
                    int iVar5 = param_1 + 0x18;
                    int iVar9 = param_1 + 0x78;
                    do
                    {
                        ushort puVar1 = PsxRam.ReadU16(iVar9 + BattleState.CtxSlotRecords);
                        iVar9 = iVar9 + BattleState.CtxSlotRecordStride;
                        if ((puVar1 & 0x200) == 0)
                        {
                            int iVar7 = PsxRam.ReadI32(iVar5 + BattleState.CtxFighterSlots);
                            if (iVar7 != 0 && PsxRam.ReadI32(PsxRam.ReadI32(iVar7 + 8) + 0x144) == 0)
                            {
                                PsxRam.WriteI32(iVar11_2 + BattleState.CtxFighterSlots, iVar7);
                                PsxRam.WriteI32(iVar5 + BattleState.CtxFighterSlots, 0);
                                break;
                            }
                        }

                        iVar4 = iVar4 + 1;
                        iVar5 = iVar5 + 4;
                    } while (iVar4 < 0xc);
                }

                ActivateFighterInSlot(PsxRam.ReadI32(iVar11_2 + BattleState.CtxFighterSlots), uVar12 & 0xffff);

                int iVar4b = PsxRam.ReadI32(PsxRam.ReadI32(iVar11_2 + BattleState.CtxFighterSlots) + 8);
                PsxRam.WriteU16(iVar4b + 0x114, PsxRam.ReadU16(iVar10 + CtxSlotParkedPosition));
                PsxRam.WriteU16(iVar4b + 0x116, PsxRam.ReadU16(iVar10 + CtxSlotParkedPosition + 2));
                PsxRam.WriteU16(iVar4b + 0x118, PsxRam.ReadU16(iVar10 + CtxSlotParkedPosition + 4));

                uVar3 = (ushort)(PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0xffef);
                PsxRam.WriteU16(iVar6 + BattleState.CtxSlotRecords, uVar3);
                if ((PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x2000) == 0)
                {
                    PsxRam.WriteU16(iVar6 + BattleState.CtxSlotRecords, (ushort)(uVar3 | 0x80));
                }

                if ((PsxRam.ReadU16(param_1
                        + (short)PsxRam.ReadU16(param_1 + BattleState.CtxActingSlotTeamB)
                          * BattleState.CtxSlotRecordStride
                        + BattleState.CtxSlotRecords) & 0x201) != 0x201)
                {
                    PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamB, (ushort)(short)uVar12);
                }

                if ((param_2 & 0x40) != 0
                    && ((int)(short)PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0x8000) != 0)
                {
                    return;
                }
            }

            iVar11_2 = iVar11_2 + 4;
            iVar8_2 = iVar8_2 + 4;
            iVar6 = iVar6 + BattleState.CtxSlotRecordStride;
            uVar12 = uVar12 + 1;
            iVar10 = iVar10 + 8;
        } while ((int)uVar12 < 0xc);
    }

    // GHIDRA: ActivateFighterInSlot @ 0x80027340 (VS.EXE)
    // 816 bytes. Two callers, both in RunFighterSubstitution above, one per team pass.
    //
    // param_1 IS THE TASK NODE, not the fighter: Ghidra types it `ushort *` and the body's first
    // act is `puVar6 = *(undefined4 **)(param_1 + 4)`, which on a ushort* is byte offset 8 -- the
    // task workspace, i.e. the fighter, exactly as TaskSystem stores it. Both call sites pass
    // `ctx + 0x1520 + slot*4`'s contents, which is a task node. param_2 is the slot index.
    //
    // WHAT IT DOES, in the original's order:
    //   1. The slot record's own +0x0C (ctx + slot*0x14 + 0x15BC) is the roster character id.
    //      Hand it and the fighter's own +0x160 to the sound bank switcher.
    //   2. FUN_80034818 -- BLOCKED below -- builds the character's primitive buffers.
    //   3. Publish: node+0 = character id, ctx + 0x1520 + slot*4 = this node, fighter +0x173 = slot.
    //   4. THE GUARD PHASE 1 TESTS: fighter +0x144 = ctx + 0x16A0 + slot*4, the loaded character
    //      data. Everything after this reads through that pointer.
    //   5. The AI profile index: fighter +0x22D = the slot's sub-record +0x0C. That is the byte
    //      FUN_80023890 uses to pick a behaviour profile row out of PTR_DAT_800807A4.
    //   6. The VRAM row for this fighter (DAT_80080A80, six rows by +0x160) into +0x156/+0x158,
    //      and a 32x1 CLUT uploaded at the row's second pair; the returned id goes to +0x15A.
    //   7. Relocate five pointers out of the data's own sub-header at data + *data: +0x84 from
    //      sub[0], +0x80 from sub[5], +0x88 from sub[6], +0x8C from sub[7], +0x90 from sub[12].
    //      Two different tests decide whether the base is added -- `-1 < value` for the first and
    //      `value < 0x80000000` for the other four. Both are the same test on a PSX address and the
    //      asymmetry is the original's, reproduced under rule 12.
    //   8. Point +0x50 at the slot's own Ki gauge, unlink the fighter's list node, clear the
    //      per-fighter state block, and set +0x134/+0x138 from the record's bit 15 and from two
    //      CtxFlags bits.
    //   9. Seed the animation cursor through FighterCombat.FUN_80053970 and run FUN_800539d0.
    internal static void ActivateFighterInSlot(int param_1, uint param_2)
    {
        uint uVar5 = param_2 & 0xffff;
        int puVar6 = PsxRam.ReadI32(param_1 + 8);
        int iVar7 = (int)(uVar5 * BattleState.CtxSlotRecordStride);
        int ctx = PsxRam.ReadI32(puVar6 + BattleState.FighterBattleContext);

        ushort uVar1 = PsxRam.ReadU16(ctx + iVar7 + 0x15bc);
        FUN_8005f5c4((short)uVar1, (short)PsxRam.ReadU16(puVar6 + BattleState.FighterIndex));
        FUN_80034818(
            PsxRam.ReadU16(param_1),
            PsxRam.ReadU8(puVar6 + BattleState.FighterSlotIndex),
            uVar1,
            (ushort)uVar5,
            puVar6 + 0x114);

        PsxRam.WriteU16(param_1, uVar1);
        PsxRam.WriteI32((int)(uVar5 * 4) + ctx + BattleState.CtxFighterSlots, param_1);
        PsxRam.WriteU8(puVar6 + BattleState.FighterSlotIndex, (byte)param_2);

        int piVar4 = PsxRam.ReadI32((int)(uVar5 * 4) + ctx + CtxSlotCharacterData);
        PsxRam.WriteI32(puVar6 + 0x144, piVar4);
        PsxRam.WriteU8(puVar6 + 0x174, (byte)PsxRam.ReadU16(ctx + iVar7 + 0x15c2));
        PsxRam.WriteU8(puVar6 + 0x22d,
            PsxRam.ReadU8(ctx + (int)(uVar5 * BattleState.CtxSlotSubRecordStride)
                + BattleState.CtxSlotSubRecords + 0x0c));

        int iVar2 = PsxRam.ReadI32(piVar4);
        int row = (short)PsxRam.ReadU16(puVar6 + BattleState.FighterIndex) * 4;
        PsxRam.WriteU16(puVar6 + 0x156, (ushort)DAT_80080a80[row]);
        int piVar3 = piVar4 + iVar2;
        PsxRam.WriteU16(puVar6 + 0x158, (ushort)DAT_80080a80[row + 1]);

        uVar5 = FileIo.LoadImage_ReturnTPageOrClutId(
            piVar4 + PsxRam.ReadI32(piVar3 + 0x3c),
            (ushort)DAT_80080a80[row + 2],
            (ushort)DAT_80080a80[row + 3],
            0x20,
            1,
            1);
        PsxRam.WriteU16(puVar6 + 0x15a, (ushort)(short)uVar5);

        PsxRam.WriteI32(puVar6, piVar4);
        PsxRam.WriteI32(puVar6 + 0x148, piVar3);

        iVar2 = PsxRam.ReadI32(piVar3);
        PsxRam.WriteI32(puVar6 + 0x84, iVar2);
        if (-1 < iVar2)
        {
            PsxRam.WriteI32(puVar6 + 0x84, piVar4 + iVar2);
        }

        uVar5 = (uint)PsxRam.ReadI32(piVar3 + 0x14);
        PsxRam.WriteI32(puVar6 + 0x80, unchecked((int)uVar5));
        if (uVar5 < 0x80000000)
        {
            PsxRam.WriteI32(puVar6 + 0x80, unchecked((int)(piVar4 + uVar5)));
        }

        uVar5 = (uint)PsxRam.ReadI32(piVar3 + 0x18);
        PsxRam.WriteI32(puVar6 + 0x88, unchecked((int)uVar5));
        if (uVar5 < 0x80000000)
        {
            PsxRam.WriteI32(puVar6 + 0x88, unchecked((int)(piVar4 + uVar5)));
        }

        uVar5 = (uint)PsxRam.ReadI32(piVar3 + 0x1c);
        PsxRam.WriteI32(puVar6 + 0x8c, unchecked((int)uVar5));
        if (uVar5 < 0x80000000)
        {
            PsxRam.WriteI32(puVar6 + 0x8c, unchecked((int)(piVar4 + uVar5)));
        }

        uVar5 = (uint)PsxRam.ReadI32(piVar3 + 0x30);
        PsxRam.WriteI32(puVar6 + 0x90, unchecked((int)uVar5));
        if (uVar5 < 0x80000000)
        {
            PsxRam.WriteI32(puVar6 + 0x90, unchecked((int)(piVar4 + uVar5)));
        }

        PsxRam.WriteI32(puVar6 + 0x50, iVar7 + ctx + BattleState.CtxKiGauge);
        FighterCombat.FUN_80045998(
            unchecked((int)0x80083cb4), puVar6 + 0xf8, unchecked((int)0x80101ba4));

        PsxRam.WriteU16(puVar6 + 0x15e, 0xffff);
        PsxRam.WriteU16(puVar6 + 4, 0);
        PsxRam.WriteU8(puVar6 + 0x171, 0);
        PsxRam.WriteU8(puVar6 + 0x170, 0);
        PsxRam.WriteU16(puVar6 + 0x15c, 0);
        PsxRam.WriteU8(puVar6 + 0x16d, 0);
        PsxRam.WriteI32(puVar6 + 0x134, 0);
        PsxRam.WriteU8(puVar6 + 0x152, 0x80);
        PsxRam.WriteU8(puVar6 + 0x151, 0x80);
        PsxRam.WriteU8(puVar6 + 0x150, 0x80);

        if (((int)(short)PsxRam.ReadU16(ctx + iVar7 + BattleState.CtxSlotRecords) & 0x8000) == 0)
        {
            PsxRam.WriteI32(puVar6 + 0x138, 0);
        }
        else
        {
            PsxRam.WriteI32(puVar6 + 0x138, 0x8000000);
        }

        if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + BattleState.CtxFlags) & 0x80008000) != 0)
        {
            PsxRam.WriteI32(puVar6 + 0x138, PsxRam.ReadI32(puVar6 + 0x138) | 0x2000000);
        }

        if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + BattleState.CtxFlags) & 0x8000000) != 0)
        {
            PsxRam.WriteI32(puVar6 + 0x134, PsxRam.ReadI32(puVar6 + 0x134) | 0x2000000);
        }

        PsxRam.WriteI32(puVar6 + 0x6c, (uVar1 - 1) * 8 + Dat80082ed4Address);
        PsxRam.WriteU8(puVar6 + 0x16a, 0);
        PsxRam.WriteU8(puVar6 + 0x16b, 0);

        FighterCombat.FUN_80053970(puVar6, PsxRam.ReadI32(PsxRam.ReadI32(puVar6 + 0x148) + 0x38), 0);
        FighterCombat.FUN_800539d0(puVar6);
        PsxRam.WriteU8(puVar6 + 0xaa, 0);
    }

    // GHIDRA: FUN_8005F5C4 @ 0x8005F5C4 (VS.EXE)
    // 156 bytes, one caller, ActivateFighterInSlot above. Its one callee is libspu's SsVabClose,
    // already in PsxSdkMonogame.
    //
    // Per-side sound bank switch. param_2 is the fighter's own +0x160, and `(param_2 << 16) >> 15`
    // is that index times two -- a HALFWORD stride into the record array at DAT_8008D284, which
    // SoundState.cs already owns. A record whose +0x12C is negative has no bank open, so the wanted
    // id is simply recorded at +0x148 with bit 7 set; otherwise, when the id already matches the low
    // seven bits of +0x148 there is nothing to do, and when it does not the open bank is closed,
    // +0x12C reset to -1 and the new id recorded. Either way the request bit 2 goes up in
    // DAT_8008D340.
    //
    // PARTIAL: the two records are addressed through DAT_8008D284, which SoundState.cs declares but
    // which nothing in the port writes yet -- its one writer is the sound init FUN_8005D25C, still
    // absent. Until that lands this function reads a null base, so it is declared here with its
    // real body and its one hardware call left to the SDK.
    private static void FUN_8005f5c4(short param_1, int param_2)
    {
        int iVar2 = (param_2 << 0x10) >> 0xf;
        int iVar1 = iVar2 + SoundState.DAT_8008d284;

        if ((short)PsxRam.ReadU16(iVar1 + 300) < 0)
        {
            PsxRam.WriteU16(iVar1 + 0x148, (ushort)(short)(param_1 + 0x80));
        }
        else
        {
            if ((PsxRam.ReadU16(iVar1 + 0x148) & 0x7f) == param_1)
            {
                return;
            }

            LibSnd.SsVabClose((short)PsxRam.ReadU16(iVar1 + 300));
            iVar2 = iVar2 + SoundState.DAT_8008d284;
            PsxRam.WriteU16(iVar2 + 300, 0xffff);
            PsxRam.WriteU16(iVar2 + 0x148, (ushort)(short)(param_1 + 0x80));
        }

        BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 | 4;
    }

    // GHIDRA: FUN_80034818 @ 0x80034818 (VS.EXE)
    // BLOCKED: 1408 bytes. ActivateFighterInSlot's second act, and the builder of the character's
    // on-screen primitive buffers: it finds this character's own 0x1E58-byte slot in the six-slot
    // array at DAT_8008DA48 (the array FighterSetup.cs's own PARTIAL note already flags), zeroes the
    // header, then fills two runs of 0x1A-halfword primitive records with GPU command bytes,
    // tpage/clut words and a screen-mode-dependent constant read back through GetGraphType. It has
    // its own callee FUN_80032134 (328 bytes, three call sites here) and its own two-byte-per-
    // character table at DAT_800817D8, neither of which is in this slice.
    //
    // Its return value is DISCARDED by the caller (`FUN_80034818(...)` with no assignment), and
    // nothing it writes is read by the rest of ActivateFighterInSlot, so leaving it a stub does not
    // change what +0x144 receives -- which is the point of this slice. What it does change is that
    // the character has no primitives to draw.
    private static int FUN_80034818(ushort param_1, byte param_2, ushort param_3, ushort param_4, int param_5)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
        _ = param_4;
        _ = param_5;
        return -1;
    }
}
