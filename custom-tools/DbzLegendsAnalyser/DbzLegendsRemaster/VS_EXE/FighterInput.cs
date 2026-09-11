using PsxSdkMonogame;
using static PsxSdkMonogame.Kernel;

namespace DbzLegendsRemaster.VS_EXE;

// THE PLAYER INPUT DECODER — 0x80047CF8..0x80049F54, step 9.3 of the fighter task.
//
// WHAT THIS FAMILY IS FOR. FighterTask.cs's UpdateFighter runs a numbered sequence of steps per
// fighter per frame; step 9.3 produces THE FRAME'S COMMAND WORD, and step 9.4 routes on it. Every
// arm of step 9.4 that starts an attack tests that word against a non-zero opcode — 0x26/0x27/0x28,
// 0x21, 0x13/0x14, 0x1c, and in the callees 0x17, 0x23/0x24/0x25 — so while step 9.3 was a stub
// returning 0, every one of those arms took its "no attack, reset" branch on every frame. That is
// the measured reason the battle-scene task was never created: no attack means no gauge
// contribution, no gauge means CtxCentralGauge never reaches +/-30000, and that is one of the four
// conditions RunBattleRound needs before it raises CtxFlags bit 3.
//
// THE TWO RINGS THIS FILE READS. PushFighterPadHistory @ 0x80047CF8 is the only writer of the two
// twenty-word rings at fighter+0x180 and fighter+0x1D0 (BattleState.FighterPadStateHistory /
// FighterPadEdgeHistory). It shifts both up by one entry and stores this frame's remapped pad
// state and remapped rising edges at index 0. Everything else in this file is a recogniser reading
// backwards through those rings.
//
// The bits are the game's LOGICAL button bits, produced by PadInput.ProcessPadInput's fourteen-
// entry remap loop through the tables at 0x801FF020 / 0x801FF03C. SELECT.EXE seeds those tables
// with the identity, so by default a logical bit equals the libetc hardware bit it came from
// (0x0010 up, 0x0020 right, 0x0040 down, 0x0080 left, 0x1000 triangle, 0x2000 circle, 0x4000
// cross, 0x8000 square). That correspondence is stated here because it makes the recognisers
// legible, NOT relied on: the player can remap, and nothing below names a bit after a button.
//
// THE FOUR FACE BITS ARE A COMPASS. BuildFaceButtonCompassRun @ 0x800482EC collapses the state
// ring's 0xF000 nibble into a 3-bit code, and the mapping it uses is a full eight-point compass:
//
//   0x1000        -> 0      0x1000|0x2000 -> 1      0x2000 (or none) -> 2    0x4000|0x2000 -> 3
//   0x4000        -> 4      0x4000|0x8000 -> 5      0x8000           -> 6    0x8000|0x1000 -> 7
//
// i.e. the four face buttons sit at the four cardinal points and a pair of adjacent ones at each
// diagonal, numbered clockwise. That is what makes the two sweep recognisers below meaningful:
// MatchClockwiseFaceSweep accepts a run whose successive codes step forward by 1 or 2 and total at
// least 4 (half a turn or more, clockwise), MatchCounterClockwiseFaceSweep the mirror image. They
// produce commands 0x23 and 0x24.
//
// WHAT IS NOT CLOSED. The +0x138 bit GROUPS that pick between the three decoders (0x7F00, 0x200FF,
// neither) are named after the masks themselves, not after a meaning: nothing read for this wave
// establishes what state those bits describe. Likewise the gate bit 0x20 that every sequence
// recogniser tests on the newest edge word is left as the literal 0x20; it is the same bit
// DecodeCommandFlagsClear turns into commands 0x13/0x14 when no recogniser fires, and by the
// default remap it is the pad's RIGHT, but neither fact establishes what the game calls it.
//
// OWNERSHIP. FUN_8004b3d0 and FUN_8004b4a4 are already ported, in FighterAction.cs, and are called
// from here by qualified name rather than redeclared — the duplicate-address defect this repo has
// shipped four times. FUN_80049e30 and its four callees previously lived in FighterCombat.cs as
// one real dispatcher plus four empty stubs; they are MOVED here, not copied, and FighterCombat.cs
// no longer declares any of the five.
internal static class FighterInput
{
    // GHIDRA: PushFighterPadHistory @ 0x80047CF8 (VS.EXE)
    // 288 bytes, no callees. One caller, ReadFighterPadCommand below, which calls it
    // unconditionally and first, with the port's remapped pad state and remapped rising edges.
    //
    // The loop runs 0x13 down to 1 and copies `ring[i] = ring[i-1]` for BOTH rings at once — the
    // original writes it as `*(u32*)(i*4 + param_1 + 0x180) = *(u32*)(param_1 + i*4 + 0x17c)`,
    // and 0x17C is 0x180 - 4. Index 0 is then overwritten with this frame's pair. Twenty entries
    // per ring; nothing in this file reads past index 8.
    internal static void PushFighterPadHistory(int param_1, int param_2, int param_3)
    {
        for (int local_10 = 0x13; 0 < local_10; local_10 = local_10 + -1)
        {
            PsxRam.WriteI32(
                local_10 * 4 + param_1 + BattleState.FighterPadStateHistory,
                PsxRam.ReadI32(param_1 + local_10 * 4 + BattleState.FighterPadStateHistory - 4));
            PsxRam.WriteI32(
                local_10 * 4 + param_1 + BattleState.FighterPadEdgeHistory,
                PsxRam.ReadI32(param_1 + local_10 * 4 + BattleState.FighterPadEdgeHistory - 4));
        }

        PsxRam.WriteI32(param_1 + BattleState.FighterPadStateHistory, param_2);
        PsxRam.WriteI32(param_1 + BattleState.FighterPadEdgeHistory, param_3);
    }

    // GHIDRA: MatchFacingFaceThenOppositeFace @ 0x80047E18 (VS.EXE)
    // 416 bytes, no callees. One caller, DecodeCommandFlagsClear below; a match yields command
    // 0x28. param_1 is the EDGE ring's base address (the caller passes `fighter + 0x1d0`), param_2
    // the fighter's +0x138 flag word.
    //
    // Gated on bit 0x20 of the newest edge word. Then bit 30 of the flags (0x40000000) chooses
    // which of the two side face bits comes first: clear -> 0x8000 then 0x2000, set -> 0x2000 then
    // 0x8000. FighterTask.UpdateFighterFacingFlag is the writer of that bit, from a rotated-position compare
    // of the two fighters, so the pair is swapped by which side the opponent is on.
    //
    // The first scan runs indices 0..3 and gives up at 4; the second continues from THAT index
    // (`param_1[local_18 + local_14]`) for another four, so the window is relative, not absolute.
    internal static int MatchFacingFaceThenOppositeFace(int param_1, uint param_2)
    {
        int uVar1;

        if ((PsxRam.ReadI32(param_1) & 0x20) == 0)
        {
            uVar1 = 0;
        }
        else
        {
            uint local_10;
            uint local_c;
            if ((param_2 & 0x40000000) == 0)
            {
                local_10 = 0x8000;
                local_c = 0x2000;
            }
            else
            {
                local_10 = 0x2000;
                local_c = 0x8000;
            }

            int local_18 = -1;
            while (true)
            {
                local_18 = local_18 + 1;
                if (local_18 == 4)
                {
                    return 0;
                }

                if (((uint)PsxRam.ReadI32(param_1 + local_18 * 4) & local_10) != 0)
                {
                    break;
                }
            }

            int local_14 = -1;
            while (true)
            {
                local_14 = local_14 + 1;
                if (local_14 == 4)
                {
                    return 0;
                }

                if (((uint)PsxRam.ReadI32(param_1 + (local_18 + local_14) * 4) & local_c) != 0)
                {
                    break;
                }
            }

            uVar1 = 1;
        }

        return uVar1;
    }

    // GHIDRA: MatchFaceButton1000Within3 @ 0x80047FB8 (VS.EXE)
    // 212 bytes, no callees. Three callers: DecodeCommandFlagsClear and DecodeCommandWhileFlag10
    // below (both yielding command 0x26), and FUN_8004b68c, which is out of every ported slice so
    // far. Gate bit 0x20 on the newest edge word, then indices 0..2 scanned for bit 0x1000.
    internal static int MatchFaceButton1000Within3(int param_1)
    {
        int uVar1;

        if ((PsxRam.ReadI32(param_1) & 0x20) == 0)
        {
            uVar1 = 0;
        }
        else
        {
            int local_10 = -1;
            while (true)
            {
                local_10 = local_10 + 1;
                if (local_10 == 3)
                {
                    return 0;
                }

                if ((PsxRam.ReadI32(param_1 + local_10 * 4) & 0x1000) != 0)
                {
                    break;
                }
            }

            uVar1 = 1;
        }

        return uVar1;
    }

    // GHIDRA: MatchFaceButton4000Within3 @ 0x8004808C (VS.EXE)
    // 212 bytes, no callees, the same three callers as MatchFaceButton1000Within3 above and the
    // same shape byte for byte; only the tested bit differs (0x4000), and a match yields command
    // 0x27 rather than 0x26.
    internal static int MatchFaceButton4000Within3(int param_1)
    {
        int uVar1;

        if ((PsxRam.ReadI32(param_1) & 0x20) == 0)
        {
            uVar1 = 0;
        }
        else
        {
            int local_10 = -1;
            while (true)
            {
                local_10 = local_10 + 1;
                if (local_10 == 3)
                {
                    return 0;
                }

                if ((PsxRam.ReadI32(param_1 + local_10 * 4) & 0x4000) != 0)
                {
                    break;
                }
            }

            uVar1 = 1;
        }

        return uVar1;
    }

    // GHIDRA: MatchFacingFaceTwice @ 0x80048160 (VS.EXE)
    // 396 bytes, no callees. One caller, DecodeCommandFlags200FF below; a match yields command
    // 0x25. Same facing-chosen bit as MatchFacingFaceThenOppositeFace, but the SAME bit is looked
    // for twice: indices 0..2 for the first, then a further four from there for the second. Note
    // the second loop starts its counter at 0 and pre-increments, so `local_18 + local_14` is never
    // the entry the first loop stopped on.
    internal static int MatchFacingFaceTwice(int param_1, uint param_2)
    {
        int uVar1;

        if ((PsxRam.ReadI32(param_1) & 0x20) == 0)
        {
            uVar1 = 0;
        }
        else
        {
            uint local_10 = (param_2 & 0x40000000) == 0 ? 0x8000u : 0x2000u;

            int local_18 = -1;
            while (true)
            {
                local_18 = local_18 + 1;
                if (local_18 == 3)
                {
                    return 0;
                }

                if (((uint)PsxRam.ReadI32(param_1 + local_18 * 4) & local_10) != 0)
                {
                    break;
                }
            }

            int local_14 = 0;
            while (true)
            {
                local_14 = local_14 + 1;
                if (local_14 == 4)
                {
                    return 0;
                }

                if (((uint)PsxRam.ReadI32(param_1 + (local_18 + local_14) * 4) & local_10) != 0)
                {
                    break;
                }
            }

            uVar1 = 1;
        }

        return uVar1;
    }

    // GHIDRA: BuildFaceButtonCompassRun @ 0x800482EC (VS.EXE)
    // 1264 bytes, no callees. Two callers, MatchClockwiseFaceSweep and
    // MatchCounterClockwiseFaceSweep below, both passing the fighter's STATE ring
    // (`fighter + 0x180`) as param_1 and their own eight-byte stack buffer as param_2.
    //
    // WHAT IT PRODUCES. It walks indices 0..7 of the state ring, keeps only the 0xF000 nibble, and
    // records each value that DIFFERS from the previous kept one — a run-length encoding of the
    // face-button combination over the last eight frames. The number of transitions is the return
    // value; fewer than three and it returns 0 having written nothing. Otherwise entries 1..n of
    // param_2 get the 3-bit compass code of each transition (the eight-point table is in this
    // file's header note).
    //
    // THE FIRST LOOP WRITES PARAM_2 TWICE PER ENTRY, and both writes are kept because both are in
    // the image: `p[i] = p[i] & 7` at 0x80048364..0x80048370, then `p[i] = (p[i] & 0xF8) | 0` at
    // 0x80048388..0x8004839C, where the OR's operand is a register the compiler proves zero
    // (`addu a0,zero,zero; sll v1,a0,27; sra a0,v1,27; andi v1,a1,7` — a sign-extended constant 0).
    // Net effect: entries 0..5 are zeroed. Entries 6 and 7 of param_2 are NOT touched here.
    //
    // THE OUT-OF-RANGE WRITE IS THE ORIGINAL'S, AND IT IS SELF-LIMITING. auStack_30 is declared
    // with six words but `auStack_30[local_10]` is written with local_10 up to 8. The frame,
    // measured from the image (prologue `addiu sp,sp,-0x30; addu s8,sp,zero`, stores at
    // `sw v0,0x0(v1)` with base s8 and index*4), puts the array at s8+0x00..s8+0x14 and then
    // local_18 at s8+0x18, local_14 at s8+0x1C, local_10 at s8+0x20 — so index 6 IS local_18,
    // index 7 IS local_14 and index 8 IS local_10.
    //
    // Index 6 is therefore `local_18 = local_18`, a no-op, and on the NEXT iteration the guard
    // `auStack_30[local_10] != local_18` reads that same storage and is always false, so local_10
    // can never leave 6. Indices 7 and 8 are unreachable. Reproduced rather than corrected
    // (rule 12): the C# array is the declared six entries and index 6 is routed to the local it
    // aliases, which is what makes the cap fall out instead of being asserted.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: param_2 is a caller-owned stack buffer in the original, so it is a byte[] here
    // rather than a PSX address; the two callers each allocate their own, exactly as the original's
    // `abStack_18[8]` does.
    internal static int BuildFaceButtonCompassRun(int param_1, byte[] param_2)
    {
        uint[] auStack_30 = new uint[6];
        uint local_18 = 0;
        int local_14;
        int local_10;

        // The six-word array followed by local_18 in the original's frame. Only index 6 is ever
        // reached; see the note above for why 7 and 8 are not.
        uint SlotRead(int index) => index < 6 ? auStack_30[index] : local_18;
        void SlotWrite(int index, uint value)
        {
            if (index < 6)
            {
                auStack_30[index] = value;
            }
            else
            {
                local_18 = value;
            }
        }

        for (local_14 = 0; local_14 < 6; local_14 = local_14 + 1)
        {
            auStack_30[local_14] = 0;
            param_2[local_14] = (byte)(param_2[local_14] & 7);
            param_2[local_14] = (byte)(param_2[local_14] & 0xf8);
        }

        local_10 = 0;
        for (local_14 = 0; local_14 < 8; local_14 = local_14 + 1)
        {
            local_18 = (uint)PsxRam.ReadI32(local_14 * 4 + param_1) & 0xf000;
            if (local_18 != 0 && SlotRead(local_10) != local_18)
            {
                local_10 = local_10 + 1;
                SlotWrite(local_10, local_18);
            }
        }

        if (local_10 < 3)
        {
            local_10 = 0;
        }
        else
        {
            for (local_14 = 1; local_14 <= local_10; local_14 = local_14 + 1)
            {
                uint entry = SlotRead(local_14);
                if ((entry & 0x1000) == 0)
                {
                    if ((entry & 0x4000) == 0)
                    {
                        if ((entry & 0x8000) == 0)
                        {
                            param_2[local_14] = (byte)(param_2[local_14] & 0xf8 | 2);
                        }
                        else
                        {
                            param_2[local_14] = (byte)(param_2[local_14] & 0xf8 | 6);
                        }
                    }
                    else if ((entry & 0x8000) == 0)
                    {
                        if ((entry & 0x2000) == 0)
                        {
                            param_2[local_14] = (byte)(param_2[local_14] & 0xf8 | 4);
                        }
                        else
                        {
                            param_2[local_14] = (byte)(param_2[local_14] & 0xf8 | 3);
                        }
                    }
                    else
                    {
                        param_2[local_14] = (byte)(param_2[local_14] & 0xf8 | 5);
                    }
                }
                else if ((entry & 0x8000) == 0)
                {
                    if ((entry & 0x2000) == 0)
                    {
                        param_2[local_14] = (byte)(param_2[local_14] & 0xf8);
                    }
                    else
                    {
                        param_2[local_14] = (byte)(param_2[local_14] & 0xf8 | 1);
                    }
                }
                else
                {
                    // The original writes `| 7` here without the `& 0xF8` its siblings carry; the
                    // two are the same value, so this is the compiler's, not a different rule.
                    param_2[local_14] = (byte)(param_2[local_14] | 7);
                }
            }
        }

        return local_10;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the two sweep recognisers below read their compass codes back as
    // `(char)((int)((uint)b << 0x1d) >> 0x1d)` — a three-bit field sign-extended to a signed char.
    // C# has no direct spelling for that, so it is one helper used by both, rather than the
    // expression written out twice.
    private static int SignExtend3(int value) => (value & 7) << 29 >> 29;

    // GHIDRA: MatchClockwiseFaceSweep @ 0x800487DC (VS.EXE)
    // 544 bytes. Two callers: DecodeCommandFlags200FF below (a match yields command 0x23) and
    // FUN_8004b68c, out of every ported slice. param_1 is the EDGE ring, param_2 the STATE ring —
    // the gate is read from the edges, the compass run is built from the states.
    //
    // Each adjacent pair of compass codes is differenced (earlier minus later, both sign-extended
    // from three bits) and the difference must be 0, 1 or 2; anything negative or above 2 rejects
    // the whole run. The accepted differences are summed and the run matches when the total is at
    // least 4 — half a turn of the compass or more, in the increasing direction.
    //
    // local_10 IS UNINITIALISED IN THE ORIGINAL: `local_10 = local_10 & 0xf8 | (diff & 7)`. Only
    // bits 0..2 are ever read back out of it, and those are exactly the bits the OR assigns, so
    // the garbage in bits 3..7 is dead. Started at 0 here, which is the same behaviour, not a fix.
    internal static int MatchClockwiseFaceSweep(int param_1, int param_2)
    {
        int uVar1;

        if ((PsxRam.ReadI32(param_1) & 0x20) == 0)
        {
            uVar1 = 0;
        }
        else
        {
            byte[] abStack_18 = new byte[8];
            int iVar2 = BuildFaceButtonCompassRun(param_2, abStack_18);
            if (iVar2 == 0)
            {
                uVar1 = 0;
            }
            else
            {
                byte local_10 = 0;
                sbyte local_f = 0;
                for (int local_20 = 1; local_20 < iVar2; local_20 = local_20 + 1)
                {
                    local_10 = (byte)(local_10 & 0xf8
                        | (SignExtend3(abStack_18[local_20]) - SignExtend3(abStack_18[local_20 + 1])) & 7);

                    if (SignExtend3(local_10) < 0)
                    {
                        return 0;
                    }

                    if (2 < SignExtend3(local_10))
                    {
                        return 0;
                    }

                    local_f = (sbyte)(local_f + SignExtend3(local_10));
                }

                uVar1 = local_f < 4 ? 0 : 1;
            }
        }

        return uVar1;
    }

    // GHIDRA: MatchCounterClockwiseFaceSweep @ 0x800489FC (VS.EXE)
    // 548 bytes, the mirror image of MatchClockwiseFaceSweep and the same two callers (command
    // 0x24 from DecodeCommandFlags200FF, plus FUN_8004b68c). The differences must be 0, -1 or -2,
    // and it is their NEGATION that is summed, so the same "at least 4" threshold means half a
    // turn or more in the decreasing direction.
    internal static int MatchCounterClockwiseFaceSweep(int param_1, int param_2)
    {
        int uVar1;

        if ((PsxRam.ReadI32(param_1) & 0x20) == 0)
        {
            uVar1 = 0;
        }
        else
        {
            byte[] abStack_18 = new byte[8];
            int iVar2 = BuildFaceButtonCompassRun(param_2, abStack_18);
            if (iVar2 == 0)
            {
                uVar1 = 0;
            }
            else
            {
                byte local_10 = 0;
                sbyte local_f = 0;
                for (int local_20 = 1; local_20 < iVar2; local_20 = local_20 + 1)
                {
                    local_10 = (byte)(local_10 & 0xf8
                        | (SignExtend3(abStack_18[local_20]) - SignExtend3(abStack_18[local_20 + 1])) & 7);

                    if (0 < SignExtend3(local_10))
                    {
                        return 0;
                    }

                    if (2 < -SignExtend3(local_10))
                    {
                        return 0;
                    }

                    local_f = (sbyte)(local_f - SignExtend3(local_10));
                }

                uVar1 = local_f < 4 ? 0 : 1;
            }
        }

        return uVar1;
    }

    // GHIDRA: MatchRepeatedFaceButton @ 0x80048C20 (VS.EXE)
    // 616 bytes, no callees. One caller, DecodeCommandFlagsClear below, which stores the RESULT —
    // not a boolean, the matched bit itself — into BattleState.FighterRepeatedFaceButton and turns
    // a non-zero result into command 0x1c.
    //
    // Unlike every other recogniser here the gate is not bit 0x20: it fires when the flags carry
    // bit 0x40000, OR the state byte is 0x1E, OR the newest edge word has bit 0x10. Then it finds
    // the first two edge entries inside indices 0..4 and a further 0..4 that carry any 0xF000 bit,
    // and returns that bit if the two are equal AND the value is a single face bit; a pair like
    // 0x3000 falls through the switch and returns 0.
    internal static uint MatchRepeatedFaceButton(int param_1, uint param_2, sbyte param_3)
    {
        uint uVar1;

        if ((param_2 & 0x40000) == 0 && param_3 != 0x1e && ((uint)PsxRam.ReadI32(param_1) & 0x10) == 0)
        {
            uVar1 = 0;
        }
        else
        {
            int local_10 = -1;
            while (true)
            {
                local_10 = local_10 + 1;
                if (local_10 == 5)
                {
                    return 0;
                }

                if (((uint)PsxRam.ReadI32(param_1 + local_10 * 4) & 0xf000) != 0)
                {
                    break;
                }
            }

            int local_c = 0;
            while (true)
            {
                local_c = local_c + 1;
                if (local_c == 5)
                {
                    return 0;
                }

                if (((uint)PsxRam.ReadI32(param_1 + (local_10 + local_c) * 4) & 0xf000) != 0)
                {
                    break;
                }
            }

            uVar1 = (uint)PsxRam.ReadI32(param_1 + (local_10 + local_c) * 4) & 0xf000;
            if (uVar1 == ((uint)PsxRam.ReadI32(param_1 + local_10 * 4) & 0xf000))
            {
                if (uVar1 != 0x2000)
                {
                    if (uVar1 < 0x2001)
                    {
                        if (uVar1 == 0x1000)
                        {
                            return 0x1000;
                        }
                    }
                    else
                    {
                        if (uVar1 == 0x4000)
                        {
                            return 0x4000;
                        }

                        if (uVar1 == 0x8000)
                        {
                            return 0x8000;
                        }
                    }

                    uVar1 = 0;
                }
            }
            else
            {
                uVar1 = 0;
            }
        }

        return uVar1;
    }

    // GHIDRA: DecodeCommandWhileFlag10 @ 0x80048E88 (VS.EXE)
    // 368 bytes. One caller, DecodeCommandFlags200FF below, reached when the fighter's +0x138
    // carries BOTH bit 0x100000 and bit 0x10. What that pair describes is not established by
    // anything read for this wave, so the name says which gate reaches it and nothing more.
    //
    // Decodes only the three "special" commands: 0x26 and 0x27 from the same two recognisers
    // DecodeCommandFlagsClear uses, and 0x28 from one of two state-byte-selected recognisers that
    // live in FighterAction.cs (FUN_8004b3d0 for state 0x23/0x24, FUN_8004b4a4 for state 0x25).
    // Everything else is -1.
    internal static int DecodeCommandWhileFlag10(int param_1)
    {
        int uVar2;

        if (((uint)PsxRam.ReadI32(param_1 + BattleState.FighterPadEdgeHistory) & 0x20) == 0)
        {
            uVar2 = -1;
        }
        else
        {
            int iVar4 = param_1 + BattleState.FighterPadEdgeHistory;
            byte bVar1 = PsxRam.ReadU8(param_1 + 0x16a);
            int iVar3 = MatchFaceButton1000Within3(iVar4);
            if (iVar3 == 1)
            {
                uVar2 = 0x26;
            }
            else
            {
                iVar3 = MatchFaceButton4000Within3(iVar4);
                if (iVar3 == 1)
                {
                    uVar2 = 0x27;
                }
                else
                {
                    if (0x22 < bVar1)
                    {
                        if (bVar1 < 0x25)
                        {
                            iVar3 = FighterAction.FUN_8004b3d0(iVar4);
                            if (iVar3 == 1)
                            {
                                return 0x28;
                            }
                        }
                        else if (bVar1 == 0x25
                            && FighterAction.FUN_8004b4a4(iVar4, PsxRam.ReadI32(param_1 + 0x138)) == 1)
                        {
                            return 0x28;
                        }
                    }

                    uVar2 = -1;
                }
            }
        }

        return uVar2;
    }

    // GHIDRA: MatchStateGatedRepeatedFace @ 0x80049008 (VS.EXE)
    // 1316 bytes, no callees. One caller, DecodeCommandFlags7F00 below, which passes
    // `fighter + 0x220` as param_2 — BattleState.FighterRepeatedFaceButton — and turns a return of
    // 1 into command 0x1c.
    //
    // Same "two edge entries carrying 0xF000, within 0..4 and then a further 0..4" search as
    // MatchRepeatedFaceButton, but gated on bit 0x10 of the newest edge word alone, and the ACCEPTED
    // values depend on the state byte +0x16A and on whether the fighter's +0x116 is zero:
    //   state 0x16 with +0x138 bits 0x3800 set: +0x116 == 0 accepts 0x1000, 0x8000, 0x2000 in that
    //     order; otherwise 0x4000, 0x1000, 0x8000, 0x2000.
    //   state 0x18..0x19: accepts 0x8000 then 0x2000.
    //   state 0x1A: +0x116 == 0 accepts 0x1000 only; otherwise 0x4000 then 0x1000.
    //   any other state: no match.
    // The accepted value is written through param_2 and 1 returned.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: param_2 is `undefined4 *` in the original and is only ever `fighter + 0x220`, so it
    // stays a PSX address written through PsxRam rather than becoming an out parameter.
    internal static int MatchStateGatedRepeatedFace(int param_1, int param_2)
    {
        if (((uint)PsxRam.ReadI32(param_1 + BattleState.FighterPadEdgeHistory) & 0x10) == 0)
        {
            return 0;
        }

        int local_10 = -1;
        while (true)
        {
            local_10 = local_10 + 1;
            if (local_10 == 5)
            {
                return 0;
            }

            if (((uint)PsxRam.ReadI32(local_10 * 4 + param_1 + BattleState.FighterPadEdgeHistory) & 0xf000) != 0)
            {
                break;
            }
        }

        uint uVar2 = (uint)PsxRam.ReadI32(local_10 * 4 + param_1 + BattleState.FighterPadEdgeHistory) & 0xf000;

        int local_c = 0;
        while (true)
        {
            local_c = local_c + 1;
            if (local_c == 5)
            {
                return 0;
            }

            if (((uint)PsxRam.ReadI32((local_10 + local_c) * 4 + param_1 + BattleState.FighterPadEdgeHistory)
                & 0xf000) != 0)
            {
                break;
            }
        }

        uint uVar3 = (uint)PsxRam.ReadI32((local_10 + local_c) * 4 + param_1 + BattleState.FighterPadEdgeHistory)
            & 0xf000;

        byte bVar1 = PsxRam.ReadU8(param_1 + 0x16a);
        if (bVar1 < 0x1a)
        {
            if (bVar1 < 0x18)
            {
                if (bVar1 == 0x16 && (PsxRam.ReadI32(param_1 + 0x138) & 0x3800) != 0)
                {
                    if ((short)PsxRam.ReadU16(param_1 + 0x116) == 0)
                    {
                        if (uVar3 == 0x1000 && uVar2 == 0x1000)
                        {
                            PsxRam.WriteI32(param_2, 0x1000);
                            return 1;
                        }

                        if (uVar3 == 0x8000 && uVar2 == 0x8000)
                        {
                            PsxRam.WriteI32(param_2, unchecked((int)0x8000));
                            return 1;
                        }

                        if (uVar3 == 0x2000 && uVar2 == 0x2000)
                        {
                            PsxRam.WriteI32(param_2, 0x2000);
                            return 1;
                        }
                    }
                    else
                    {
                        if (uVar3 == 0x4000 && uVar2 == 0x4000)
                        {
                            PsxRam.WriteI32(param_2, 0x4000);
                            return 1;
                        }

                        if (uVar3 == 0x1000 && uVar2 == 0x1000)
                        {
                            PsxRam.WriteI32(param_2, 0x1000);
                            return 1;
                        }

                        if (uVar3 == 0x8000 && uVar2 == 0x8000)
                        {
                            PsxRam.WriteI32(param_2, unchecked((int)0x8000));
                            return 1;
                        }

                        if (uVar3 == 0x2000 && uVar2 == 0x2000)
                        {
                            PsxRam.WriteI32(param_2, 0x2000);
                            return 1;
                        }
                    }
                }
            }
            else
            {
                if (uVar3 == 0x8000 && uVar2 == 0x8000)
                {
                    PsxRam.WriteI32(param_2, unchecked((int)0x8000));
                    return 1;
                }

                if (uVar3 == 0x2000 && uVar2 == 0x2000)
                {
                    PsxRam.WriteI32(param_2, 0x2000);
                    return 1;
                }
            }
        }
        else if (bVar1 == 0x1a)
        {
            if ((short)PsxRam.ReadU16(param_1 + 0x116) == 0)
            {
                if (uVar3 == 0x1000 && uVar2 == 0x1000)
                {
                    PsxRam.WriteI32(param_2, 0x1000);
                    return 1;
                }
            }
            else
            {
                if (uVar3 == 0x4000 && uVar2 == 0x4000)
                {
                    PsxRam.WriteI32(param_2, 0x4000);
                    return 1;
                }

                if (uVar3 == 0x1000 && uVar2 == 0x1000)
                {
                    PsxRam.WriteI32(param_2, 0x1000);
                    return 1;
                }
            }
        }

        return 0;
    }

    // GHIDRA: DecodeCommandFlags7F00 @ 0x80049534 (VS.EXE)
    // 372 bytes. One caller, ReadFighterPadCommand below, taken when +0x138 bits 0x7F00 are set.
    //
    // Two outcomes. Command 0x1c when ALL THREE of: +0x138 bits 0x3C00 set, this fighter's own Ki
    // gauge (context + slot*0x14 + 0x15B4, the same address FighterCombat.DeliverPendingHitEvent gates on)
    // at least 400, and MatchStateGatedRepeatedFace returning 1. Otherwise: clear +0x138 bit
    // 0x40000 if it is set and the newest edge word carries bit 0x10, then command 0x17 if the
    // newest STATE word carries bit 0x40, else -1.
    //
    // The three-way `||` short-circuits, so MatchStateGatedRepeatedFace runs only when the two
    // cheaper tests both pass — and it is that call, not the caller, that writes
    // FighterRepeatedFaceButton. Preserved.
    internal static int DecodeCommandFlags7F00(int param_1)
    {
        uint uVar3 = (uint)PsxRam.ReadI32(param_1 + BattleState.FighterPadStateHistory);
        uint uVar4 = (uint)PsxRam.ReadI32(param_1 + BattleState.FighterPadEdgeHistory);

        int uVar2;
        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x3c00) == 0
            || (short)PsxRam.ReadU16(
                PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
                + BattleState.CtxKiGauge) < 400
            || MatchStateGatedRepeatedFace(param_1, param_1 + BattleState.FighterRepeatedFaceButton) != 1)
        {
            if ((PsxRam.ReadI32(param_1 + 0x138) & 0x40000) != 0 && (uVar4 & 0x10) != 0)
            {
                PsxRam.WriteI32(param_1 + 0x138, (int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffbffff));
            }

            uVar2 = (uVar3 & 0x40) == 0 ? -1 : 0x17;
        }
        else
        {
            uVar2 = 0x1c;
        }

        return uVar2;
    }

    // GHIDRA: DecodeCommandFlags200FF @ 0x800496A8 (VS.EXE)
    // 892 bytes. One caller, ReadFighterPadCommand below, taken when +0x138 bits 0x7F00 are clear
    // and bits 0x200FF are set.
    //
    // THREE SECTIONS, in the original's order:
    //   1. +0x138 bit 0x100000 set: either hand off wholesale to DecodeCommandWhileFlag10 (when bit
    //      0x10 is also set), or return command 0x2a for one state-byte-selected edge bit — 0x27
    //      wants 0x4000, 0x26 wants 0x1000, and 0x28 wants 0x2000 or 0x8000 depending on the
    //      facing bit 0x40000000. Anything else is -1, and the function ends here.
    //   2. The OTHER fighter — this fighter's task node's +8 — carrying +0x138 bit 0x100 opens the
    //      three sequence recognisers: MatchFacingFaceTwice -> 0x25, MatchClockwiseFaceSweep ->
    //      0x23, MatchCounterClockwiseFaceSweep -> 0x24.
    //   3. Otherwise +0x138 bit 1 with the newest STATE word's bit 0x80 clear rewrites the flags
    //      (bit 1 cleared, bit 2 set) and returns 0x21; failing that, the same "clear 0x40000 on
    //      edge bit 0x10" housekeeping DecodeCommandFlags7F00 does, and -1.
    internal static int DecodeCommandFlags200FF(int param_1)
    {
        uint uVar4 = (uint)PsxRam.ReadI32(param_1 + BattleState.FighterPadStateHistory);
        uint uVar5 = (uint)PsxRam.ReadI32(param_1 + BattleState.FighterPadEdgeHistory);

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x100000) != 0)
        {
            if ((PsxRam.ReadI32(param_1 + 0x138) & 0x10) != 0)
            {
                return DecodeCommandWhileFlag10(param_1);
            }

            byte bVar1 = PsxRam.ReadU8(param_1 + 0x16a);
            if (bVar1 == 0x27)
            {
                if ((uVar5 & 0x4000) != 0)
                {
                    return 0x2a;
                }
            }
            else if (bVar1 < 0x28)
            {
                if (bVar1 == 0x26 && (uVar5 & 0x1000) != 0)
                {
                    return 0x2a;
                }
            }
            else if (bVar1 == 0x28)
            {
                if ((PsxRam.ReadI32(param_1 + 0x138) & 0x40000000) == 0)
                {
                    if ((uVar5 & 0x8000) != 0)
                    {
                        return 0x2a;
                    }
                }
                else if ((uVar5 & 0x2000) != 0)
                {
                    return 0x2a;
                }
            }

            return -1;
        }

        if ((PsxRam.ReadI32(
                PsxRam.ReadI32(PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode) + 8) + 0x138) & 0x100) != 0)
        {
            int iVar3 = MatchFacingFaceTwice(
                param_1 + BattleState.FighterPadEdgeHistory, (uint)PsxRam.ReadI32(param_1 + 0x138));
            if (iVar3 == 1)
            {
                return 0x25;
            }

            iVar3 = MatchClockwiseFaceSweep(
                param_1 + BattleState.FighterPadEdgeHistory, param_1 + BattleState.FighterPadStateHistory);
            if (iVar3 == 1)
            {
                return 0x23;
            }

            iVar3 = MatchCounterClockwiseFaceSweep(
                param_1 + BattleState.FighterPadEdgeHistory, param_1 + BattleState.FighterPadStateHistory);
            if (iVar3 == 1)
            {
                return 0x24;
            }
        }

        int uVar2;
        if ((PsxRam.ReadI32(param_1 + 0x138) & 2) == 0 || (uVar4 & 0x80) != 0)
        {
            if ((PsxRam.ReadI32(param_1 + 0x138) & 0x40000) != 0 && (uVar5 & 0x10) != 0)
            {
                PsxRam.WriteI32(param_1 + 0x138, (int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffbffff));
            }

            uVar2 = -1;
        }
        else
        {
            PsxRam.WriteI32(param_1 + 0x138, (int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffffffd));
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 4);
            uVar2 = 0x21;
        }

        return uVar2;
    }

    // GHIDRA: DecodeCommandFlagsClear @ 0x80049A24 (VS.EXE)
    // 996 bytes. One caller, ReadFighterPadCommand below, taken when +0x138 bits 0x7F00 AND bits
    // 0x200FF are both clear — the ordinary case, and the only one of the three that can produce
    // the movement commands.
    //
    // Bit 0x80000 set short-circuits the whole body to -1. Otherwise:
    //   Housekeeping on bit 0x40000: set it when the newest STATE word has bit 0x10 and the newest
    //   EDGE word has any of 0x50A0, or when the newest edge word has bit 0x10 and the state byte
    //   is 0x02 or 0x0A; clear it, when it was already set, on edge bit 0x10 alone.
    //
    //   Then, unless bit 0x800000 is set, MatchRepeatedFaceButton's result is STORED into
    //   BattleState.FighterRepeatedFaceButton and a non-zero one sets bit 0x40000 and returns 0x1c.
    //
    //   Then the three edge-sequence recognisers, in order: MatchFacingFaceThenOppositeFace ->
    //   0x28, MatchFaceButton1000Within3 -> 0x26, MatchFaceButton4000Within3 -> 0x27.
    //
    //   Failing all three, a straight ladder over the newest words: edge 0x20 -> 0x13 or 0x14 by a
    //   coin flip off rand(); edge 0x80 -> 0x21; state 0x40 -> 0x17; state 0x1000 -> 2; the state
    //   ring's index-2 entry (+0x188) bit 0x4000 -> 10; then bit 0x40000 clear with state bit 0x10
    //   -> 0x1D; then bit 0x800000 clear -> 0, else -1.
    internal static int DecodeCommandFlagsClear(int param_1)
    {
        uint uVar3 = (uint)PsxRam.ReadI32(param_1 + BattleState.FighterPadStateHistory);
        uint uVar4 = (uint)PsxRam.ReadI32(param_1 + BattleState.FighterPadEdgeHistory);

        int uVar1;
        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x80000) == 0)
        {
            if ((PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0)
            {
                if ((uVar3 & 0x10) != 0 && (uVar4 & 0x50a0) != 0)
                {
                    PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x40000);
                }

                if ((uVar4 & 0x10) != 0
                    && ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 2 || (sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 10))
                {
                    PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x40000);
                }
            }
            else if ((uVar4 & 0x10) != 0)
            {
                PsxRam.WriteI32(param_1 + 0x138, (int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffbffff));
            }

            if ((PsxRam.ReadI32(param_1 + 0x138) & 0x800000) == 0)
            {
                uint matched = MatchRepeatedFaceButton(
                    param_1 + BattleState.FighterPadEdgeHistory,
                    (uint)PsxRam.ReadI32(param_1 + 0x138),
                    (sbyte)PsxRam.ReadU8(param_1 + 0x16a));
                PsxRam.WriteI32(param_1 + BattleState.FighterRepeatedFaceButton, (int)matched);
                if (PsxRam.ReadI32(param_1 + BattleState.FighterRepeatedFaceButton) != 0)
                {
                    PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x40000);
                    return 0x1c;
                }
            }

            int iVar2 = MatchFacingFaceThenOppositeFace(
                param_1 + BattleState.FighterPadEdgeHistory, (uint)PsxRam.ReadI32(param_1 + 0x138));
            if (iVar2 == 1)
            {
                uVar1 = 0x28;
            }
            else
            {
                iVar2 = MatchFaceButton1000Within3(param_1 + BattleState.FighterPadEdgeHistory);
                if (iVar2 == 1)
                {
                    uVar1 = 0x26;
                }
                else
                {
                    iVar2 = MatchFaceButton4000Within3(param_1 + BattleState.FighterPadEdgeHistory);
                    if (iVar2 == 1)
                    {
                        uVar1 = 0x27;
                    }
                    else if ((uVar4 & 0x20) == 0)
                    {
                        if ((uVar4 & 0x80) == 0)
                        {
                            if ((uVar3 & 0x40) == 0)
                            {
                                if ((uVar3 & 0x1000) == 0)
                                {
                                    if (((uint)PsxRam.ReadI32(
                                            param_1 + BattleState.FighterPadStateHistory + 8) & 0x4000) == 0)
                                    {
                                        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0 && (uVar3 & 0x10) != 0)
                                        {
                                            uVar1 = 0x1d;
                                        }
                                        else if ((PsxRam.ReadI32(param_1 + 0x138) & 0x800000) == 0)
                                        {
                                            uVar1 = 0;
                                        }
                                        else
                                        {
                                            uVar1 = -1;
                                        }
                                    }
                                    else
                                    {
                                        uVar1 = 10;
                                    }
                                }
                                else
                                {
                                    uVar1 = 2;
                                }
                            }
                            else
                            {
                                uVar1 = 0x17;
                            }
                        }
                        else
                        {
                            uVar1 = 0x21;
                        }
                    }
                    else
                    {
                        uVar3 = (uint)rand();
                        uVar1 = (uVar3 & 1) == 0 ? 0x14 : 0x13;
                    }
                }
            }
        }
        else
        {
            uVar1 = -1;
        }

        return uVar1;
    }

    // GHIDRA: ReadFighterPadCommand @ 0x80049E30 (VS.EXE)
    // 276 bytes. One caller, SelectFighterCommand @ 0x80049F54 in FighterTask.cs — three call
    // sites, `(fighter, 0)` twice and `(fighter, 1)` once, so param_2 really is a port selector
    // and the 1 case is live (it is the arm taken when +0x138 bit 0x20000000 is set).
    //
    // MOVED HERE FROM FighterCombat.cs, where it was one real dispatcher plus four empty stubs for
    // the callees. FighterCombat.cs no longer declares any of the five: an address declared in two
    // files is the defect this repo has shipped four times, because C# binds an unqualified call to
    // the enclosing class first and the empty stub silently wins.
    //
    // param_2 selects a PORT into two pad-state pairs: `(&DAT_8008d3b8)[param_2]` and
    // `(&DAT_8008d3ac)[param_2]`. PadInput.cs declares all four as individual scalar fields rather
    // than arrays, so the index is expressed as a port selector against those fields.
    internal static int ReadFighterPadCommand(int fighter, int port)
    {
        uint padState = port == 0 ? PadInput.DAT_8008d3b8 : PadInput.DAT_8008d3bc;
        uint padEdge = port == 0 ? PadInput.DAT_8008d3ac : PadInput.DAT_8008d3b0;
        PushFighterPadHistory(fighter, (int)padState, (int)padEdge);

        int result;
        if ((PsxRam.ReadI32(fighter + 0x138) & 0x7f00) == 0)
        {
            if ((PsxRam.ReadI32(fighter + 0x138) & 0x200ff) == 0)
            {
                result = DecodeCommandFlagsClear(fighter);
            }
            else
            {
                result = DecodeCommandFlags200FF(fighter);
            }
        }
        else
        {
            result = DecodeCommandFlags7F00(fighter);
        }

        return result;
    }
}
