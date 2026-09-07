using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE FIGHTER ACTION-SELECTION FAMILY — 0x8004A6xx..0x8004BFxx. Distinct from the combat-
// RESOLUTION family already in FighterCombat.cs (AddSlotGaugeContribution, the Ki-gauge leaves,
// FighterSetState, CreateAttackEventTask and the two attack-event roots): this file is the home
// for the smaller leaves that pick WHICH attack/guard/special state a fighter enters, reached
// from FighterTask.cs step 9.4 through three dispatchers this port does not touch —
// FUN_8004b098, FUN_8004c198, FUN_8004cea0, all still empty stubs, all somebody else's slice in
// this wave (two other agents are working on FighterTask.cs and its immediate callees while this
// file is written). This file owns none of those three and edits nothing outside itself.
//
// THE EIGHT NAMED LEAVES, smallest first, in the order transliterated below:
//   0x8004AA44 (88B), 0x8004A910 (108B), 0x8004AD0C (116B), 0x8004B024 (116B),
//   0x8004B3D0 (212B), 0x8004B5AC (216B), 0x8004B4A4 (264B), 0x8004B8A0 (300B).
// All eight are genuine leaves or call only functions FighterCombat.cs already exposes
// (FighterSetState, FUN_8004a108, FUN_8004a638) — cross-referenced there by address per this
// project's duplicate-symbol rule, never redeclared here.
//
// ONE ADDITIONAL LEAF NOT AMONG THE EIGHT: FUN_8004a9e8 (92 bytes). FUN_8004b8a0's own body
// calls it directly (`FUN_8004a9e8(param_1,param_2);`); leaving that call site pointing at
// nothing would either fail to build or silently drop the call, both defect classes this repo
// has already shipped once. Ghidra's decompilation of FUN_8004a9e8 is two statements
// (FighterSetState, then one flag-bit OR) — decisive enough to port in full rather than leave a
// stub, the same treatment FighterCombat.cs's own header note gives FUN_80045af0/FUN_80055c6c
// for the same reason (a callee not on the original named list, added so its caller is not left
// calling into nothing). It is placed immediately before FUN_8004b8a0, its only caller in this
// file.
//
// A SECOND WAVE through this file, ported after the eight above, adds five more leaves —
// FUN_8004b9cc, FUN_8004bb70, FUN_8004bd3c, FUN_8004bf50, FUN_8004ad80 — plus one more decisive
// supporting leaf (FUN_8004b33c) and two BLOCKED stubs (FUN_800261ec, FUN_8004b68c) for callees
// this file's own scope does not reach. See the "WAVE 2" header comment further down, right
// before FUN_8004b9cc, for the full account, including why a sixth address named in that wave's
// brief (FUN_8004aa9c) is deliberately absent from this file.
//
// WHAT IS DELIBERATELY LEFT OPEN. FUN_8004b3d0, FUN_8004b5ac and FUN_8004b4a4 all walk a small
// table at fighter+0x1D0 (a fighter's own command/attack-slot buffer — confirmed only by the
// arithmetic that locates it, the same way FighterCombat.cs's own header note documents for the
// tables AddSlotGaugeContribution and FUN_8004a108 read) testing bits 0x20/0xa000/0x8000/0x2000
// per slot. What those bits MEAN (a button held? a combo window? an input buffered this frame?)
// is not established beyond the arithmetic that tests them — the callers that would give that
// context (FUN_80048e88, FUN_8004b68c) are out of this slice, read only far enough to fix each
// leaf's own parameter shape and confirm none of the eight calls another. FUN_8004b3d0's own
// caller passes `param_1 + 0x1d0` for a fighter — not the fighter's own base — so this port keeps
// the parameter as a plain address (already offset by the caller), never assumes it is a fighter
// pointer itself.
internal static class FighterAction
{
    // GHIDRA: FUN_8004aa44 @ 0x8004AA44 (VS.EXE)
    // 88 bytes. One caller, FUN_8004b098 (`else if (param_2 == 0x21) { FUN_8004aa44(param_1);
    // FUN_8004bf50(param_1); ... }` — both siblings out of this slice). Forces state 0x21, then
    // sets +0x138 bit 1 (0x2).
    internal static void FUN_8004aa44(int param_1)
    {
        FighterCombat.FighterSetState(param_1, 0x21);
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 2);
    }

    // GHIDRA: FUN_8004a910 @ 0x8004A910 (VS.EXE)
    // 108 bytes. Two callers: FUN_8004b098 (`FUN_8004a910(param_1,param_2);`) and FUN_8004ca54
    // (`FUN_8004a910(param_1,0x17);`, after clearing +0x138 bit 9). Stamps the given state, sets
    // +0x138 bit 0 (0x1), then calls FighterCombat.FUN_8004a108 — the Ki-gauge decrement. Ghidra
    // prints that call with param_2 forwarded (`FUN_8004a108(param_1,param_2)`), but
    // FUN_8004a108's own header note in FighterCombat.cs already closes that as a rendering
    // artifact of the call site: the real body reads only one argument.
    internal static void FUN_8004a910(int param_1, int param_2)
    {
        FighterCombat.FighterSetState(param_1, (ushort)param_2);
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 1);
        FighterCombat.FUN_8004a108(param_1);
    }

    // GHIDRA: FUN_8004ad0c @ 0x8004AD0C (VS.EXE)
    // 116 bytes. Three callers: FUN_8004b098, FUN_8004cb24, FUN_8004cc64 (all `else { FUN_8004ad0c
    // (param_1); }` or equivalent — none in this slice). Forces state 0x20, then masks +0x138
    // with 0xfa640000 (confirmed against the raw `lui v1,0xfa64 / and a0,a0,v1` pair — the value
    // is a genuine `lui`-built constant, not a decompiler artifact) before setting bit 14
    // (0x4000).
    internal static void FUN_8004ad0c(int param_1)
    {
        FighterCombat.FighterSetState(param_1, 0x20);
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfa640000));
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x4000);
    }

    // GHIDRA: FUN_8004b024 @ 0x8004B024 (VS.EXE)
    // 116 bytes. One caller, FUN_8004b098 (`else { FUN_8004b024(param_1); }`). When the fighter's
    // +4 halfword is zero, clears +0x138 bit 15 (0x8000) and calls FighterCombat.FUN_8004a638
    // (fighter, 0) — the same "re-stamp current state, opcode 0" call shape FighterCombat.cs's
    // own header note documents for FUN_8004a638's other callers.
    internal static void FUN_8004b024(int param_1)
    {
        if ((short)PsxRam.ReadU16(param_1 + 4) == 0)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffff7fff));
            FighterCombat.FUN_8004a638(param_1, 0);
        }
    }

    // GHIDRA: FUN_8004b3d0 @ 0x8004B3D0 (VS.EXE)
    // 212 bytes. One caller, FUN_80048e88 (`iVar3 = FUN_8004b3d0(iVar4); if (iVar3 == 1) ...`,
    // where iVar4 = param_1 + 0x1d0 — a fighter's own command/attack-slot buffer, NOT the fighter
    // base itself; see this file's own header note). If the first slot's bit 0x20 is clear,
    // returns 0 outright. Otherwise scans slots 0..3 in order for the first one with bit 0xa000
    // set, returning 1 on a match and 0 if none of the four slots has it.
    internal static int FUN_8004b3d0(int param_1)
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
                if (local_10 == 4)
                {
                    return 0;
                }
                if ((PsxRam.ReadI32(param_1 + local_10 * 4) & 0xa000) != 0)
                {
                    break;
                }
            }

            uVar1 = 1;
        }

        return uVar1;
    }

    // GHIDRA: FUN_8004b5ac @ 0x8004B5AC (VS.EXE)
    // 216 bytes. One caller, FUN_8004b68c (`iVar1 = FUN_8004b5ac(iVar3); if (iVar1 == 1) ...`,
    // where iVar3 is the same kind of +0x1d0-offset slot-buffer address FUN_8004b3d0 receives).
    // Counts how many of 10 consecutive slots have bit 0x20 set and returns whether that count is
    // strictly greater than 2 (i.e. 3 or more).
    internal static bool FUN_8004b5ac(int param_1)
    {
        int local_10 = 0;

        for (int local_c = 0; local_c < 10; local_c = local_c + 1)
        {
            if ((PsxRam.ReadI32(local_c * 4 + param_1) & 0x20) != 0)
            {
                local_10 = local_10 + 1;
            }
        }

        return 2 < local_10;
    }

    // GHIDRA: FUN_8004b4a4 @ 0x8004B4A4 (VS.EXE)
    // 264 bytes. Two callers: FUN_80048e88 (`FUN_8004b4a4(iVar4,*(undefined4*)(param_1+0x138))`)
    // and FUN_8004b68c (`FUN_8004b4a4(iVar3,*(undefined4*)(param_1+0x138))`) — both pass the same
    // kind of +0x1d0-offset slot-buffer address as param_1 and the fighter's own +0x138 flag word
    // as param_2. Same shape as FUN_8004b3d0 above (bit 0x20 gate on slot 0, then a 4-slot scan
    // returning 1 on the first match / 0 if none), except the bit tested per slot is chosen by
    // param_2 bit 30 (0x40000000): 0x8000 when clear, 0x2000 when set.
    internal static int FUN_8004b4a4(int param_1, int param_2)
    {
        int uVar1;

        if ((PsxRam.ReadI32(param_1) & 0x20) == 0)
        {
            uVar1 = 0;
        }
        else
        {
            int local_10 = (param_2 & 0x40000000) == 0 ? 0x8000 : 0x2000;
            int local_18 = -1;
            while (true)
            {
                local_18 = local_18 + 1;
                if (local_18 == 4)
                {
                    return 0;
                }
                if ((PsxRam.ReadI32(param_1 + local_18 * 4) & local_10) != 0)
                {
                    break;
                }
            }

            uVar1 = 1;
        }

        return uVar1;
    }

    // GHIDRA: FUN_8004a9e8 @ 0x8004A9E8 (VS.EXE)
    // 92 bytes. NOT one of the eight named addresses for this wave — added because FUN_8004b8a0
    // below calls it directly and, per this file's own header note, leaving that call site
    // pointing at nothing is the dropped-call defect this repo has already shipped once. Three
    // callers total (FUN_8004b8a0 here; FUN_8004b9cc and FUN_8004bd3c, both out of this slice).
    // Two statements: stamp the given state, then set +0x138 bit 4 (0x10).
    internal static void FUN_8004a9e8(int param_1, ushort param_2)
    {
        FighterCombat.FighterSetState(param_1, param_2);
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x10);
    }

    // GHIDRA: FUN_8004b8a0 @ 0x8004B8A0 (VS.EXE)
    // 300 bytes, the largest of the eight, taken last. One caller, FUN_8004c198 (out of this
    // slice): `if ((*(uint*)(param_1+0x138) & 6) == 0) { FUN_8004b8a0(param_1,param_2,param_3); }`.
    // Two independent arms:
    //   +4 halfword == 0: clears +0x138 bit 0 and +0x134 bit 29 (0xdfffffff, the same mask
    //     FighterCombat.cs's own AddSlotGaugeContribution note already uses for +0x134), then
    //     calls FighterCombat.FUN_8004a638(param_1, 0).
    //   otherwise, when param_3's own +0x138 bit 8 (0x100) is set AND param_2 is 0x23, 0x24 or
    //     0x25: clears +0x138 bit 0 on param_1, then calls FUN_8004a9e8(param_1, param_2) —
    //     param_2 narrowed to ushort, matching FUN_8004a9e8's own parameter type above.
    // Neither arm reads or applies FighterSetState's usual param_1 fighter fields beyond +0x138/
    // +0x134/+4; param_3 here is a SEPARATE fighter (the one whose own +0x138 flag gates the
    // second arm), never dereferenced beyond that one flag read.
    internal static void FUN_8004b8a0(int param_1, int param_2, int param_3)
    {
        if ((short)PsxRam.ReadU16(param_1 + 4) == 0)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffffffe));
            PsxRam.WriteI32(param_1 + 0x134, PsxRam.ReadI32(param_1 + 0x134) & unchecked((int)0xdfffffff));
            FighterCombat.FUN_8004a638(param_1, 0);
        }
        else if (((PsxRam.ReadI32(param_3 + 0x138) & 0x100) != 0) &&
                 ((param_2 == 0x25 || param_2 == 0x23) || param_2 == 0x24))
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffffffe));
            FUN_8004a9e8(param_1, (ushort)param_2);
        }
    }

    // =====================================================================================
    // WAVE 2 — the second slice through this same family, smallest first:
    //   0x8004B9CC (420B), 0x8004BB70 (460B), 0x8004BD3C (532B), 0x8004BF50 (584B, called from
    //   BOTH step-9.4 arms), 0x8004AD80 (676B). A sixth address named in this wave's own brief,
    //   0x8004AA9C (624B), is SKIPPED here: FighterCombat.cs already carries it as a real body —
    //   its own header note there counts this file's FUN_8004b098 as one of its three callers —
    //   so porting it again here would be exactly the duplicate-declaration defect this
    //   project's own rules call out. It is cross-referenced as FighterCombat.FUN_8004aa9c,
    //   never redeclared.
    //
    // TWO MORE CALLEES ADDED FOR THE SAME REASON FUN_8004a9e8 WAS IN WAVE 1: FUN_8004b9cc's own
    // body calls FUN_800261ec and FUN_8004b68c directly, and leaving those call sites pointing
    // at nothing is either a build break or a dropped call, both defect classes this repo has
    // already shipped. Both are themselves substantial state machines over the fighter's
    // +0x1d0 command-slot buffer (304 and 484 bytes respectively — bigger than every one of
    // this wave's own five ported targets except FUN_8004ad80), squarely the "read only far
    // enough for context, not ported" territory WAVE 1's own header note already draws around
    // FUN_8004b68c by name. They are declared below as BLOCKED stubs, matching the precedent
    // FighterCombat.cs sets for its own FUN_80049a24/FUN_800496a8/FUN_80049534: the call site is
    // real, the body is honestly empty rather than invented. FUN_8004b33c, by contrast, is a
    // genuine 148-byte leaf (FighterSetState plus three flag-word writes, one caller) needed by
    // FUN_8004bb70 below — decisive enough to port in full, the same treatment FUN_8004a9e8 got
    // in WAVE 1, and placed the same way: immediately before its only caller in this file.
    // =====================================================================================

    // GHIDRA: FUN_8004b9cc @ 0x8004B9CC (VS.EXE)
    // 420 bytes. One caller, FUN_8004c198 (out of this slice): `else { FUN_8004b9cc(param_1); }`.
    // Gated on the fighter's own +4 halfword being zero (else a no-op). Clears +0x138 bits
    // 0x20/0x40 (mask 0xffffff9f) and +0x134 bit 0x20000000 (0xdfffffff, the same mask this file
    // already uses elsewhere), then picks a slot value: when the fighter's OWN task node's
    // fighter (+0xac -> +8, the same "task node's own workspace fighter" chain
    // FighterCombat.cs's FUN_8004ee48 and FighterTask.cs already read through
    // BattleState.FighterTaskNode) has state byte (+0x16a) equal to 0x16, the slot is forced to
    // -1; otherwise, when +0x138 bits 0x30000000 are BOTH clear, the slot comes from
    // FUN_800261ec(fighter) (below, BLOCKED); when either bit is set, the slot comes from
    // FighterMotion.FUN_8004b68c(fighter) instead (also below, BLOCKED). A slot of -1 re-stamps the current
    // state via FighterCombat.FUN_8004a638(fighter, 0); any other slot instead sets +0x138 bit
    // 0x40 and, only when the slot is in 0x23..0x28, dispatches it onward: 0x23..0x25 ->
    // FUN_8004a9e8(fighter, slot) (above, this file); 0x26..0x28 ->
    // FighterCombat.FUN_8004a97c(fighter, slot). A slot of 0x22 or below, or 0x29 and above,
    // dispatches nowhere — reproduced exactly, not a gap.
    internal static void FUN_8004b9cc(int param_1)
    {
        if ((short)PsxRam.ReadU16(param_1 + 4) == 0)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffffff9f));
            PsxRam.WriteI32(param_1 + 0x134, PsxRam.ReadI32(param_1 + 0x134) & unchecked((int)0xdfffffff));

            int local_10;
            int ownTaskFighter = PsxRam.ReadI32(PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode) + 8);

            if (PsxRam.ReadU8(ownTaskFighter + 0x16a) == 0x16)
            {
                local_10 = -1;
            }
            else if ((PsxRam.ReadI32(param_1 + 0x138) & 0x30000000) == 0)
            {
                local_10 = FUN_800261ec(param_1);
            }
            else
            {
                local_10 = FighterMotion.FUN_8004b68c(param_1);
            }

            if (local_10 == -1)
            {
                FighterCombat.FUN_8004a638(param_1, 0);
            }
            else
            {
                PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x40);
                if (0x22 < local_10)
                {
                    if (local_10 < 0x26)
                    {
                        FUN_8004a9e8(param_1, (ushort)local_10);
                    }
                    else if (local_10 < 0x29)
                    {
                        FighterCombat.FUN_8004a97c(param_1, local_10);
                    }
                }
            }
        }
    }

    // GHIDRA: FUN_800261ec @ 0x800261EC (VS.EXE)
    // BLOCKED: 304 bytes, out of this slice. Called from FUN_8004b9cc above as
    // FUN_800261ec(fighter) when +0x138 bits 0x30000000 are both clear. Its own body opens by
    // re-deriving the same task-node-fighter chain FUN_8004b9cc already reads
    // (`*(int*)(*(int*)(param_1+0xac)+8)`) and comparing it against param_1 itself — a real
    // command-slot decision this port does not carry out. The stub returns -1, which is
    // FUN_8004b9cc's OWN "no slot" sentinel, so a caller sees the same "re-stamp current state"
    // outcome the original takes on plenty of its own early-out paths, not a fabricated slot
    // number.
    private static int FUN_800261ec(int param_1)
    {
        _ = param_1;
        return -1;
    }

    // GHIDRA: FUN_8004b68c @ 0x8004B68C (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/FighterMotion.cs. The call sites in this file
    // reach it by qualified name: an empty stub in the enclosing class silently beats a real
    // body elsewhere, which is what check_function_addresses.py exists to catch.

    // GHIDRA: FUN_8004b33c @ 0x8004B33C (VS.EXE)
    // 148 bytes. One caller, FUN_8004bb70 below (`if (param_2 == 0x2a) { FUN_8004b33c(param_1);
    // }`). NOT one of this wave's six named addresses — added because FUN_8004bb70's own body
    // calls it directly, the same reason FUN_8004a9e8 was added in WAVE 1. Four statements:
    // forces state 0x2a, clears +0x138 bit 0x100000 (mask 0xffefffff), clears +0x138 bits
    // 0x08/0x10/0x80 (mask 0xffffff67), then sets bit 0x20.
    internal static void FUN_8004b33c(int param_1)
    {
        FighterCombat.FighterSetState(param_1, 0x2a);
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffefffff));
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffffff67));
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x20);
    }

    // GHIDRA: FUN_8004bb70 @ 0x8004BB70 (VS.EXE)
    // 460 bytes. One caller, FUN_8004c198 (out of this slice): `else { FUN_8004bb70(param_1,
    // param_2); }`.
    //
    // TWO INDEPENDENT ARMS:
    //   +4 halfword == 0 AND +6 halfword != 0: clears +0x138 bits 0x08/0x40/0x80/0x20000 (mask
    //     0xfffdff37) and +0x134 bit 0x20000000 (0xdfffffff, this file's usual mask); when the
    //     fighter's own state (+0x16a) is 0x27 AND +0x116 is less than +0xb2 (both raw literals;
    //     no BattleState name covers either), copies +0xb2 onto +0x116; then calls
    //     FighterCombat.FUN_8004a638(fighter, 0) unconditionally.
    //   otherwise, gated on +0x138 bit 0x100000 being set AND a slot-table lookup — the SAME
    //     `*(int*)(param_1+0xf0) + slotIndex*0x14 + 0x15b4` chain FUN_8004bd3c below also reads,
    //     slotIndex being BattleState.FighterSlotIndex, neither +0xf0 nor +0x15b4 named anywhere
    //     in this port — reading above 399: param_2==0x2a calls FUN_8004b33c(fighter) above;
    //     otherwise, when +0x1d0 bits 0xf000 are set (a raw literal on the same
    //     command/attack-slot buffer WAVE 1's own header note already names), clears +0x138 bit
    //     0x100000.
    internal static void FUN_8004bb70(int param_1, int param_2)
    {
        if ((short)PsxRam.ReadU16(param_1 + 4) == 0 && (short)PsxRam.ReadU16(param_1 + 6) != 0)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffdff37));
            PsxRam.WriteI32(param_1 + 0x134, PsxRam.ReadI32(param_1 + 0x134) & unchecked((int)0xdfffffff));

            if (PsxRam.ReadU8(param_1 + 0x16a) == 0x27
                && (short)PsxRam.ReadU16(param_1 + 0x116) < (short)PsxRam.ReadU16(param_1 + 0xb2))
            {
                PsxRam.WriteU16(param_1 + 0x116, PsxRam.ReadU16(param_1 + 0xb2));
            }

            FighterCombat.FUN_8004a638(param_1, 0);
        }
        else if ((PsxRam.ReadI32(param_1 + 0x138) & 0x100000) != 0
            && 399 < (short)PsxRam.ReadU16(PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext) // +0x15b4 below is the unnamed half
                + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * 0x14 + 0x15b4))
        {
            if (param_2 == 0x2a)
            {
                FUN_8004b33c(param_1);
            }
            else if ((PsxRam.ReadI32(param_1 + 0x1d0) & 0xf000) != 0)
            {
                PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffefffff));
            }
        }
    }

    // GHIDRA: FUN_8004bd3c @ 0x8004BD3C (VS.EXE)
    // 532 bytes. One caller, FUN_8004c198 (out of this slice): `else { FUN_8004bd3c(param_1,
    // param_2); }`.
    //
    // First computes local_10 = 0xffffffff (the "no slot" sentinel), and overwrites it with
    // param_2 ONLY when all three hold: the same slot-table lookup FUN_8004bb70 above reads
    // (`*(int*)(param_1+0xf0) + slotIndex*0x14 + 0x15b4`, > 399), param_2 < 0x29, and param_2 >
    // 0x25 — i.e. param_2 in 0x26..0x28. Then: if param_2 equals the fighter's own current state
    // (+0x16a) OR local_10 is still the sentinel, and the +4 halfword is zero, clears +0x138
    // bits 0x10/0x40 (mask 0xffffffaf) and +0x134 bit 0x20000000 (0xdfffffff), then calls
    // FighterCombat.FUN_8004a638(fighter, 0). OTHERWISE (param_2 differs from the current state
    // AND local_10 got the overwrite): clears +0x138 bits 0x10/0x100000 (mask 0xffefffef) and
    // +0x134 bit 0x20000000, sets +0x138 bit 0x40, and — the same 0x22 < slot < 0x29 dispatch
    // FUN_8004b9cc above already carries out — 0x23..0x25 -> FUN_8004a9e8(fighter, slot);
    // 0x26..0x28 -> FighterCombat.FUN_8004a97c(fighter, slot). Since local_10 only ever holds the
    // sentinel or a value already known to be 0x26..0x28 by construction, the 0x23..0x25 arm is
    // unreachable in practice; kept exactly as Ghidra decompiles it rather than pruned.
    internal static void FUN_8004bd3c(int param_1, uint param_2)
    {
        uint local_10 = 0xffffffff;

        if (399 < (short)PsxRam.ReadU16(PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext) // +0x15b4 below is the unnamed half
                + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * 0x14 + 0x15b4)
            && (int)param_2 < 0x29 && 0x25 < (int)param_2)
        {
            local_10 = param_2;
        }

        if (param_2 == PsxRam.ReadU8(param_1 + 0x16a) || local_10 == 0xffffffff)
        {
            if ((short)PsxRam.ReadU16(param_1 + 4) == 0)
            {
                PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffffffaf));
                PsxRam.WriteI32(param_1 + 0x134, PsxRam.ReadI32(param_1 + 0x134) & unchecked((int)0xdfffffff));
                FighterCombat.FUN_8004a638(param_1, 0);
            }
        }
        else
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffefffef));
            PsxRam.WriteI32(param_1 + 0x134, PsxRam.ReadI32(param_1 + 0x134) & unchecked((int)0xdfffffff));
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x40);

            if (0x22 < (int)local_10)
            {
                if ((int)local_10 < 0x26)
                {
                    FUN_8004a9e8(param_1, (ushort)local_10);
                }
                else if ((int)local_10 < 0x29)
                {
                    FighterCombat.FUN_8004a97c(param_1, (int)local_10);
                }
            }
        }
    }

    // GHIDRA: FUN_8004bf50 @ 0x8004BF50 (VS.EXE)
    // 584 bytes. Two callers, both out of this slice — FUN_8004b098 (right after FUN_8004aa44
    // above: `FUN_8004aa44(param_1); FUN_8004bf50(param_1);`) and FUN_8004c198 (`else {
    // FUN_8004bf50(param_1); }`) — the pair this whole family's own header note already flags as
    // "called from BOTH step-9.4 arms".
    //
    // Gated on +0x138 bit 1 (0x2):
    //   CLEAR: a small per-frame decay step. Reads the signed byte at +0x225: 0 -> step 0;
    //     negative -> step equals the SAME value (so the field below jumps straight to 0 in one
    //     call rather than decrementing by 1 — reproduced exactly, not a gap); positive -> step
    //     1. Stores the step to +0x226, subtracts it from +0x225, then calls
    //     FighterCombat.FUN_8004a108(fighter) (Ghidra prints a second argument, 0x21, but
    //     FUN_8004a108's own header note in FighterCombat.cs already closes that as a rendering
    //     artifact — the real body reads one argument). When the fighter's +4 halfword is zero,
    //     also zeroes +0x224/+0x226, clears +0x138 bit 2 (0x4, mask 0xfffffffb) and +0x134 bit
    //     0x20000000 (0xdfffffff), then calls FighterCombat.FUN_8004a638(fighter, 0).
    //   SET: increments the signed byte at +0x224 (clamped to at most 0x28), then re-derives
    //     +0x225 from a raw-literal table (DAT_800835cb, no BattleState/name covers it) indexed
    //     by the CURRENT TASK's own Id field — `*DAT_8008d16c`, the same TaskSystem.g_CurrentTask
    //     dereference FighterCombat.cs's own FUN_8004a638 header note already documents —
    //     multiplied by the (now-clamped) +0x224 and divided by 0x28; when that comes out 0,
    //     forces it to 1.
    internal static void FUN_8004bf50(int param_1)
    {
        if ((PsxRam.ReadI32(param_1 + 0x138) & 2) == 0)
        {
            sbyte v225 = (sbyte)PsxRam.ReadU8(param_1 + 0x225);
            byte step;
            if (v225 == 0)
            {
                step = 0;
            }
            else if (v225 < 1)
            {
                step = (byte)v225;
            }
            else
            {
                step = 1;
            }

            PsxRam.WriteU8(param_1 + 0x226, step);
            PsxRam.WriteU8(param_1 + 0x225, (byte)(v225 - (sbyte)step));

            FighterCombat.FUN_8004a108(param_1);

            if ((short)PsxRam.ReadU16(param_1 + 4) == 0)
            {
                PsxRam.WriteU8(param_1 + 0x224, 0);
                PsxRam.WriteU8(param_1 + 0x226, 0);
                PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffffffb));
                PsxRam.WriteI32(param_1 + 0x134, PsxRam.ReadI32(param_1 + 0x134) & unchecked((int)0xdfffffff));
                FighterCombat.FUN_8004a638(param_1, 0);
            }
        }
        else
        {
            sbyte cVar1 = (sbyte)((sbyte)PsxRam.ReadU8(param_1 + 0x224) + 1);
            PsxRam.WriteU8(param_1 + 0x224, (byte)cVar1);
            if (0x28 < cVar1)
            {
                cVar1 = 0x28;
                PsxRam.WriteU8(param_1 + 0x224, 0x28);
            }

            int taskId = PsxRam.ReadU16(TaskSystem.g_CurrentTask); // *DAT_8008d16c: current task's own Id field
            uint tableVal = PsxRam.ReadU8(unchecked((int)0x800835cb) + taskId); // DAT_800835cb, raw literal table
            int product = unchecked((int)(tableVal * (uint)cVar1));
            PsxRam.WriteU8(param_1 + 0x225, unchecked((byte)(product / 0x28)));

            if ((sbyte)PsxRam.ReadU8(param_1 + 0x225) == 0)
            {
                PsxRam.WriteU8(param_1 + 0x225, 1);
            }
        }
    }

    // GHIDRA: FUN_8004ad80 @ 0x8004AD80 (VS.EXE)
    // 676 bytes, the largest of this wave's ported targets. One caller, FUN_8004b098 (out of
    // this slice), reached from TWO different call sites in step 9.4's own body: once bare
    // (`else { FUN_8004ad80(param_1); }`) and once right after FighterCombat.FUN_8004aa9c
    // (`FUN_8004aa9c(param_1); FUN_8004ad80(param_1);`).
    //
    // TWO INDEPENDENT ARMS:
    //   +4 halfword == 0 AND +6 halfword != 0: clears +0x138 bits 0x80000/0x800000 (mask
    //     0xff77ffff), then calls FighterCombat.FUN_8004a638(fighter, 0). Unlike every other
    //     leaf in this family, this arm touches only +0x138 — no +0x134 mask here.
    //   otherwise: decrements the signed byte at +0x228 (raw literal; no BattleState name covers
    //     it) by 1. Then, only when +0x138 bit 0x80000 is CLEAR, reads the SAME +0x220 selector
    //     FighterCombat.FUN_8004aa9c already switches on (raw literal; no BattleState name
    //     covers it) and writes the SAME +0xc8/+0xca/+0xcc knockback direction/timer triple (raw
    //     literals) that function's own header note documents — but with DIFFERENT constants for
    //     the 0x4000 case (+0xca copies +0x11e unchanged rather than zeroing it, and +0xcc is set
    //     to 0xfa00/64000 rather than 0x400; the 0x2000/0x1000/0x8000 cases match
    //     FUN_8004aa9c's own byte-for-byte) — any other selector value writes nothing, reproduced
    //     exactly. Finally, when +0x138 bit 0x40000 is ALSO clear, zeroes +0x228 back out.
    internal static void FUN_8004ad80(int param_1)
    {
        if ((short)PsxRam.ReadU16(param_1 + 4) == 0 && (short)PsxRam.ReadU16(param_1 + 6) != 0)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xff77ffff));
            FighterCombat.FUN_8004a638(param_1, 0);
        }
        else
        {
            PsxRam.WriteU8(param_1 + 0x228, (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x228) - 1));

            if ((PsxRam.ReadI32(param_1 + 0x138) & 0x80000) == 0)
            {
                uint uVar1 = (uint)PsxRam.ReadI32(param_1 + 0x220); // raw literal; no BattleState name covers +0x220

                if (uVar1 == 0x2000)
                {
                    PsxRam.WriteU16(param_1 + 0xc8, 0);
                    PsxRam.WriteU16(param_1 + 0xca, unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x11e) + 0x400)));
                    PsxRam.WriteU16(param_1 + 0xcc, 0);
                }
                else if (uVar1 < 0x2001)
                {
                    if (uVar1 == 0x1000)
                    {
                        PsxRam.WriteU16(param_1 + 0xc8, 0);
                        PsxRam.WriteU16(param_1 + 0xca, 0);
                        PsxRam.WriteU16(param_1 + 0xcc, 0xfc00);
                    }
                }
                else if (uVar1 == 0x4000)
                {
                    PsxRam.WriteU16(param_1 + 0xc8, 0);
                    PsxRam.WriteU16(param_1 + 0xca, PsxRam.ReadU16(param_1 + 0x11e));
                    PsxRam.WriteU16(param_1 + 0xcc, 0xfa00); // 64000
                }
                else if (uVar1 == 0x8000)
                {
                    PsxRam.WriteU16(param_1 + 0xc8, 0);
                    PsxRam.WriteU16(param_1 + 0xca, unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x11e) - 0x400)));
                    PsxRam.WriteU16(param_1 + 0xcc, 0);
                }

                if ((PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0)
                {
                    PsxRam.WriteU8(param_1 + 0x228, 0);
                }
            }
        }
    }
}
