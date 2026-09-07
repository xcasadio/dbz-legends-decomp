using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE FIGHTER COMBAT-RESOLUTION FAMILY — 0x8004Axxx..0x8004Exxx, the leaves that seed and drain
// the two per-slot meters FighterTask.cs and BattleManager.cs only ever read: CtxKiGauge
// (0x15B4) and CtxGaugeContribution (0x15B8), the running sum that feeds the tug-of-war gauge
// at ctx+0x302C. This file is the home for that family that is not already in FighterTask.cs.
//
// WHY FUN_8004e108 IS THE PRIORITY. It is the only function anywhere in the image that writes
// CtxGaugeContribution — the fact the workflow that asked for this file was built to establish.
// Everything else here is either its sibling on the Ki side (FUN_8004a108/FUN_8004a518, the same
// table-lookup shape draining/filling CtxKiGauge instead) or plumbing shared by its two ROOT
// callers (FUN_8004e580, FUN_8004e5d0, FUN_8004d0fc, FUN_80025f38) or the state-machine setter
// fifteen other functions call (FighterSetState, already named by Ghidra).
//
// THE TWO ROOTS THEMSELVES are also in this file now, at the bottom, in the order the task that
// asked for them named them: FUN_8004ee48 (applies one attack-event record to its target, then
// reaches FUN_8004e108 through its own +0x3c/+0x8 attacker chain) and FUN_8004e758 (the larger
// dispatcher FighterTask.cs's own step 9.6 calls — see that function's header note for the empty
// duplicate declaration still sitting in FighterTask.cs, which this file's real body replaces
// but cannot remove, since FighterTask.cs is not this file's to edit).
//
// THE SHARED TABLE-LOOKUP SHAPE. FUN_8004e108 and FUN_8004a108 both do: pick a ROW by looking up
// this fighter's own slot in the battle context's CtxFighterSlots array, dereferencing the
// pointer stored there and reading its first ushort (an indirection that resolves back to the
// fighter's own field 0 in the ordinary case, but is reproduced exactly as written per rule 1 —
// see FUN_8004e108's own note); pick a COLUMN from the fighter's +0x138 flags and/or +0x16A state
// byte; read one byte from a fixed table at ROW*stride+COLUMN; scale it; and apply the result to
// one of the two meters. Neither table has a Ghidra symbol — both bases were confirmed by
// decoding the surrounding LWL/ADDIU/LW sequence by hand and cross-checking the resulting address
// against the console's own data (both read back as small, table-shaped integers), not assumed
// from the decompiler's rendering alone.
//
// WHAT IS DELIBERATELY LEFT OPEN. Neither table is named (what a "row" selects — a fighter
// archetype? a hit-strength class? — is not established beyond the arithmetic that locates it).
// FUN_80025f38's two archetype-lookup tables (0x800807a4, 0x80080204) and its two flat data
// blocks (0x80080a5c, 0x80080a38) are the same: the pointer-chasing that reaches them is
// confirmed instruction-by-instruction, but what they represent is not. FUN_8004d0fc has one
// spot (marked DEVIATION below) where the original itself reads an uninitialized local; that bug
// is kept, not fixed, and only given a defined value because C# requires one. FUN_80047c64's two
// callees (FUN_80053970, FUN_80026424) are out of this slice and stay BLOCKED stubs — one of them
// already has an independent stub in AnimCmdEffects.cs with a different call shape; this file
// owns its own copy rather than reaching into that file's private members.
//
// TIER 2 (FUN_8004a97c, FUN_8004a638, FUN_8004de90, FUN_8004dfc4). Same posture. FUN_8004a638
// reads two more unnamed byte-pair tables (0x8008302C, 0x8008307C), indexed by the CURRENT
// TASK's own Id field rather than by anything fighter-shaped — confirmed the same way as the
// tier-1 tables, by decoding the address build and reading the bytes back off the console.
// FUN_8004dfc4's three callees (FUN_8004d32c, FUN_8004d9f4, FUN_8004d694 — 584/1180/864 bytes)
// are out of this slice and stay BLOCKED stubs, this file's own copies, called exactly where the
// original calls them.
//
// WAVE 2 — eight standalone leaves FighterTask.cs's own remaining stubs wait on, added below the
// tier-1/tier-2 family above. Every one of the eight is reached from FUN_80047688 (FighterTask.cs's
// own BLOCKED stub, step 9.5) except FUN_8004ffec, reached from FUN_800501b8 (step 7's arm); none
// is otherwise called from anywhere already in this port. Two of the eight (FUN_80045998,
// FUN_80045a38) are the push/unlink halves of one intrusive doubly-linked list anchored at
// DAT_80083cb4 — the SAME global AnimCmdEffects.cs already declares (as a private
// DAT_80083cb4Address constant) for an unrelated opcode's own use of the SAME chain, cross-
// referenced here rather than redeclared, per this project's duplicate-symbol rule: compare
// addresses, not names.
//
// TWO OF THE EIGHT ARE NOT ACTUALLY LEAVES, once decompiled — the workflow that named them as such
// could not see this without decompiling them, which this port now has:
//   FUN_800539d0 dispatches, through a 16-entry function-pointer table at 0x80083C10 (no Ghidra
//   symbol resolves any of the sixteen targets to more than an UndefinedFunction preview — Ghidra
//   has not analyzed any of them), to one of sixteen substantial, wholly unanalyzed handler
//   functions. That dispatch is BLOCKED below (DispatchHitStreamRecord); the list-walk driving it
//   is not, and is ported in full.
//   FUN_80049e30 dispatches to four callees (FUN_80047cf8, FUN_800496a8, FUN_80049a24,
//   FUN_80049534 — 288/892/996/372 bytes) that Ghidra HAS fully analyzed but that are far outside
//   this slice. Same treatment as FUN_8004dfc4's own three callees above: the dispatcher is ported
//   in full, each callee stays a BLOCKED private stub carrying its real size.
// Two more (FUN_80045af0, FUN_80055c6c) are not among the eight named addresses at all — they are
// FUN_80055dc0's own two callees, both genuine leaves (Ghidra shows zero callees for either), added
// here so FUN_80055dc0 itself is not left calling into nothing.
internal static class FighterCombat
{
    // JUSTIFICATION: backend MonoGame only
    // RELATION: diagnostic probes, read only by Validation/VsBootDiagnostic.cs. Nothing in the
    // transliterated runtime touches them. They exist because FUN_8004e108 is the sole writer of
    // the gauge contribution that gates the whole battle scene, so "is it reached" is the one
    // question worth being able to answer without a screenshot.
    internal static int DiagE108Calls;

    internal static int DiagE758Calls;

    internal static int DiagEe48Calls;

    // GHIDRA: FUN_8004e108 @ 0x8004E108 (VS.EXE)
    // 1144 bytes. Two callers: FUN_8004e758 (`FUN_8004e108(param_1,0)`) and FUN_8004ee48
    // (`FUN_8004e108(*(int*)(*(int*)(param_1+0x3c)+8), 1)` — a task-node +8 workspace resolved
    // off ANOTHER node, not param_1 itself). Both callers are in this file, below.
    //
    // THE ONLY WRITER OF CtxGaugeContribution IN THE WHOLE IMAGE — the fact this whole workflow
    // exists to establish. It writes with `+=`, not `=`.
    //
    // THE SHAPE: pick a row/column pair from a small byte table, scale by 0x6F (111), apply up
    // to three divisions gated by the fighter's own +0x138 flags and by param_2, then add the low
    // 16 bits of (that value * 6) into this fighter's own CtxGaugeContribution slot.
    //
    //   ROW    the ushort at offset 0 of the fighter resolved through THIS fighter's own slot
    //          index in ctx's CtxFighterSlots array (`*(ushort*)(*(int*)(ctx+CtxFighterSlots+
    //          slot*4)))`). CtxFighterSlots is filled slot-for-slot at creation (BattleState's
    //          note on FUN_800511A8), so in the ordinary case this resolves back to the fighter's
    //          OWN field at offset 0 — but the lookup goes through the table rather than reading
    //          param_1 directly, and that indirection is reproduced as written (rule 1: no
    //          simplifying to "self"). Offset 0 of a fighter's workspace has no name in
    //          BattleState (its self-pointer table starts at +0x0C), so it stays a raw literal.
    //   COLUMN local_28, 0..15, selected by the state byte +0x16A (cases 0x13/0x14 -> 0, 0x23..
    //          0x28 -> 2/3/4/5/6/7, doubled to 8..13/(the 0x23..0x28 arm actually spans 2..10,
    //          see the switch below) when +0x138 bit 0x40000 is set; any other state -> -1,
    //          meaning "skip the table, contribution is 0 before scaling"), OR forced to 1 when
    //          param_2 != 0 (skipping the state switch outright), OR forced to 0xe/0xf by +0x138
    //          bit 0x80 (also skipping the switch, checked before it).
    //   TABLE  base 0x800835E4, stride 0x10 (16) bytes per row. No Ghidra symbol names it —
    //          confirmed by decoding the LUI/ADDIU pair that builds the address and reading the
    //          bytes back off the console: row 0 is {20,25,10,15,20,10,15,15,25,25,25,15,30,20,
    //          0,0}, row 1 is {1,1,5,4,4,6,6,6,6,7,7,8,10,10,4,6}.
    //
    // param_2 has two effects, both reproduced verbatim: it forces local_28 = 1 (skipping the
    // whole state switch) and it adds a fifth division (/5) after the /50 and /6 that +0x138
    // bits 4 and 0 gate. What param_2 itself represents is not established beyond that.
    internal static void FUN_8004e108(int param_1, int param_2)
    {
        DiagE108Calls++;

        int local_28;

        if (param_2 != 0)
        {
            local_28 = 1;
        }
        else if ((PsxRam.ReadI32(param_1 + 0x138) & 0x80) != 0)
        {
            local_28 = (PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0 ? 0xe : 0xf;
        }
        else
        {
            switch ((sbyte)PsxRam.ReadU8(param_1 + 0x16a))
            {
                case 0x13:
                case 0x14:
                    local_28 = 0;
                    break;
                case 0x23:
                    local_28 = (PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0 ? 6 : 0xc;
                    break;
                case 0x24:
                    local_28 = (PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0 ? 7 : 0xd;
                    break;
                case 0x25:
                    local_28 = (PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0 ? 5 : 0xb;
                    break;
                case 0x26:
                    local_28 = (PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0 ? 3 : 9;
                    break;
                case 0x27:
                    local_28 = (PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0 ? 4 : 10;
                    break;
                case 0x28:
                    local_28 = (PsxRam.ReadI32(param_1 + 0x138) & 0x40000) == 0 ? 2 : 8;
                    break;
                default:
                    local_28 = -1;
                    break;
            }
        }

        int local_20;
        if (local_28 == -1)
        {
            local_20 = 0;
        }
        else
        {
            local_20 = PsxRam.ReadU8(
                unchecked((int)0x800835e4) + local_28 +
                PsxRam.ReadU16(
                    PsxRam.ReadI32(
                        PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                        + BattleState.CtxFighterSlots
                        + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * 4)) * 0x10);
        }

        local_20 *= 0x6f;

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x10) != 0)
        {
            local_20 /= 0x32;
        }

        if ((PsxRam.ReadI32(param_1 + 0x138) & 1) != 0)
        {
            local_20 /= 6;
        }

        if (param_2 != 0)
        {
            local_20 /= 5;
        }

        // `local_20._0_2_ = (short)local_20 * 6;` in the decompilation — a 16-bit truncating
        // store into the low half of local_20, whose upper 16 bits are never read again.
        local_20 = (short)((short)local_20 * 6);

        int gaugeAddr =
            PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
            + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
            + BattleState.CtxGaugeContribution;

        PsxRam.WriteU16(gaugeAddr,
            unchecked((ushort)((short)PsxRam.ReadU16(gaugeAddr) + (short)local_20)));
    }

    // GHIDRA: FUN_8004e580 @ 0x8004E580 (VS.EXE)
    // 80 bytes. Two callers, FUN_8004e758 and FUN_8004ee48 (both in this file, below), both calling
    // it unconditionally alongside their own FUN_8004e108 call. Shared cleanup: drop +0x134 bit
    // 0x20000000 and zero the byte at +0x224. Neither offset is named in BattleState.
    internal static void FUN_8004e580(int param_1)
    {
        PsxRam.WriteI32(param_1 + 0x134,
            (int)((uint)PsxRam.ReadI32(param_1 + 0x134) & 0xdfffffff));
        PsxRam.WriteU8(param_1 + 0x224, 0);
    }

    // GHIDRA: FUN_8004e5d0 @ 0x8004E5D0 (VS.EXE)
    // 392 bytes. One caller, FUN_8004e758 (now in this file, below), taken when the hit descriptor's
    // high byte is 0x80 — the alternate-target hit path the family's inventory names it for.
    //
    // GHIDRA DECLARES THIS AS FOUR PARAMETERS (param_1..param_4), BUT ONLY TWO ARE REAL. Its one
    // call site passes exactly two (`FUN_8004e5d0(param_1,param_2);`), and the decompiler's
    // param_3/param_4 are an artifact of how it renders the LWL/LWR unaligned-load idiom below:
    // it treats the register a3/t0 read by the FIRST half of each pair (LWL) as an incoming
    // value, because it does not model that the SECOND half (LWR, targeting the same register)
    // unconditionally overwrites whatever was there. param_1 and the pointer resolved from
    // *(param_1+0xf4) are always 4-byte aligned in this engine (fighter/task workspaces), so
    // every LWL/LWR and SWL/SWR pair below resolves to the SAME aligned word on both halves —
    // checked address by address against the disassembly, not assumed — and the "other operand"
    // (param_4, in_t0, or the previous partial word) is shifted out to zero at every site. What
    // survives is a plain 0x10-byte block copy. The signature this port exposes is the
    // two-argument one the console actually calls.
    internal static int FUN_8004e5d0(int param_1, int param_2)
    {
        if (PsxRam.ReadI32(param_1 + 0xf4) == 0)
        {
            return -1;
        }

        if (PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0xf4) + 0xc) == -1)
        {
            return -1;
        }

        int iVar4 = PsxRam.ReadI32(param_1 + 0xf4);

        PsxRam.WriteU16(iVar4 + 0x18, PsxRam.ReadU16(param_1 + param_2 * 0x10 + 0xdc));
        PsxRam.WriteI32(iVar4 + 8, TaskSystem.g_CurrentTask); // DAT_8008d16c, VS.EXE's g_CurrentTask

        // The LWL/LWR/SWL/SWR block reduces to this 0x10-byte copy: param_1+0x124..0x134 into
        // iVar4+0x2c..0x3c, four aligned words. Order does not matter — the ranges never overlap.
        PsxRam.WriteI32(iVar4 + 0x2c, PsxRam.ReadI32(param_1 + 0x124));
        PsxRam.WriteI32(iVar4 + 0x30, PsxRam.ReadI32(param_1 + 0x128));
        PsxRam.WriteI32(iVar4 + 0x34, PsxRam.ReadI32(param_1 + 0x12c));
        PsxRam.WriteI32(iVar4 + 0x38, PsxRam.ReadI32(param_1 + 0x130));

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x400) != 0)
        {
            PsxRam.WriteU16(param_1 + 0x15e,
                unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x15e) + 0x14)));
        }

        return 0;
    }

    // GHIDRA: FUN_8004d0fc @ 0x8004D0FC (VS.EXE)
    // 552 bytes. One caller, FUN_8004e758 (now in this file, below): `local_10 = FUN_8004d0fc(uVar4,
    // param_1);` where uVar4 is a fighter workspace FUN_8004e758 resolves through its own
    // +0xf4/+0xc task-node chain (the OPPOSING side) and param_1 is FUN_8004e758's own first
    // argument (the ACTING fighter). So here param_1 is the opposing fighter (whose +0x1d0 table
    // and +0x138 flags are read) and param_2 is the acting fighter (whose +0x16a state byte
    // selects which bit the scan tests for).
    //
    // THE SCAN: param_1+0x1d0 is a table of five 4-byte words. The first loop finds the first
    // entry with bit 0x40 set (local_18, 0..4; no match anywhere -> false). The second loop,
    // starting from that SAME entry and continuing forward, finds the first entry (index
    // local_18+local_14, itself capped at local_18+4) with any of bits 0xf000 set; running off
    // the table (index reaches 5) in either loop returns false outright.
    //
    // DEVIATION: the original leaves local_10 UNINITIALIZED whenever the acting fighter's state
    // byte is anything other than 0x26, 0x27 or 0x28 — the bVar1==0x27 / bVar1<0x28-with-
    // bVar1==0x26 / bVar1==0x28 chain has no else, and the uninitialized local is then ANDed into
    // the return value. That is a genuine bug in the original (rule 12 keeps it), but C# requires
    // definite assignment, so this port initializes local_10 to 0 for the unmatched case — itself
    // arbitrary, since the console would have read whatever the stack slot happened to hold. Any
    // caller relying on a specific non-zero garbage value in that state range is not reproduced.
    internal static bool FUN_8004d0fc(int param_1, int param_2)
    {
        int local_18 = -1;
        do
        {
            local_18++;
            if (local_18 == 5)
            {
                return false;
            }
        } while ((PsxRam.ReadI32(local_18 * 4 + param_1 + 0x1d0) & 0x40) == 0);

        int local_14 = -1;
        do
        {
            local_14++;
            if (local_14 == 5)
            {
                return false;
            }
        } while ((PsxRam.ReadI32((local_18 + local_14) * 4 + param_1 + 0x1d0) & 0xf000) == 0);

        byte bVar1 = PsxRam.ReadU8(param_2 + 0x16a);
        int local_10 = 0; // DEVIATION: see header note above -- original leaves this uninitialized here.

        if (bVar1 == 0x27)
        {
            local_10 = 0x4000;
        }
        else if (bVar1 < 0x28)
        {
            if (bVar1 == 0x26)
            {
                local_10 = 0x1000;
            }
        }
        else if (bVar1 == 0x28)
        {
            local_10 = (PsxRam.ReadI32(param_1 + 0x138) & 0x40000000) == 0 ? 0x8000 : 0x2000;
        }

        return (PsxRam.ReadI32((local_18 + local_14) * 4 + param_1 + 0x1d0) & local_10) != 0;
    }

    // GHIDRA: FUN_80025f38 @ 0x80025F38 (VS.EXE)
    // 692 bytes. One caller, FUN_8004e758 (now in this file, below): `local_10 = FUN_80025f38(uVar4,
    // param_1);`, gated on `(*(uint*)(uVar4+0x138) & 0x30000000) == 0`. Same argument order as
    // FUN_8004d0fc, deduced the same way: param_1 is the OPPOSING fighter (whose +0x22c/+0x22d
    // character-lookup bytes drive the roll), param_2 is the ACTING fighter (whose ki gauge,
    // state byte and +0x22e/+0x22f/+0x230 memory are read and updated).
    //
    // THE TABLE WALK (`puVar6 = (&PTR_DAT_80080204)[(byte)*(&PTR_DAT_800807a4)[charId]]`) LOOKED
    // LIKE A PLAIN BYTE-TABLE INDEX AT FIRST GLANCE, but the disassembly settles it as a
    // three-level POINTER walk instead, confirmed instruction by instruction:
    //   charId       = *(byte*)(param_1+0x22d)
    //   categoryPtr  = *(int*)(0x800807a4 + charId*4)       -- table1 is pointer-strided (sll #2)
    //   category     = *(byte*)(categoryPtr)                -- dereferenced to a byte
    //   puVar6       = *(int*)(0x80080204 + category*4)     -- table2, also pointer-strided
    // Neither table has a name in this port; what they select (character archetype -> some
    // per-archetype counter/block row) is not established beyond this arithmetic.
    //
    // bVar8 = puVar6[2] is the row's base percentage, then SharedHighRam.DAT_801ff01c
    // (difficulty, 0/1/2) biases it by a fixed amount UNLESS the acting fighter has already
    // countered/blocked into an unbroken chain against the SAME state twice (the
    // +0x22e/+0x22f/+0x16a comparison and the +0x22c bit-7 guard), in which case the bias step is
    // skipped and only the byte-wraparound clamp still applies. The final roll is
    // `rand() % 101 < (sbyte)bVar8` — a bVar8 >= 128 (top bit set) makes the roll un-winnable,
    // which is why the bias step clamps to 100 whenever the addition would push it that high.
    internal static bool FUN_80025f38(int param_1, int param_2)
    {
        byte uVar1;
        byte uVar2;
        short sVar3;
        bool bVar4 = false;
        int iVar5;
        int puVar6;
        int iVar7 = 0;
        byte bVar8;

        iVar5 = Kernel.rand();

        if (param_2 == param_1)
        {
            return false;
        }

        if ((PsxRam.ReadI32(param_2 + 0x138) & 0x4000000) != 0)
        {
            return false;
        }

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x4000000) != 0)
        {
            return false;
        }

        sVar3 = (short)PsxRam.ReadU16(
            PsxRam.ReadI32(param_2 + BattleState.FighterBattleContext)
            + PsxRam.ReadU8(param_2 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
            + BattleState.CtxKiGauge);

        if ((PsxRam.ReadU8(param_1 + 0x22c) & 0xc0) == 0)
        {
            if (SharedHighRam.DAT_801ff01c != 0 && sVar3 < 0x10)
            {
                return false;
            }

            puVar6 = PsxRam.ReadI32(
                unchecked((int)0x80080204)
                + PsxRam.ReadU8(
                      PsxRam.ReadI32(unchecked((int)0x800807a4) + PsxRam.ReadU8(param_1 + 0x22d) * 4))
                  * 4);
        }
        else
        {
            puVar6 = PsxRam.ReadI32(unchecked((int)0x80080a5c));
            if ((PsxRam.ReadU8(param_1 + 0x22c) & 0x40) != 0)
            {
                puVar6 = PsxRam.ReadI32(unchecked((int)0x80080a38));
                if (sVar3 < 0x10)
                {
                    return false;
                }
            }
        }

        bVar8 = PsxRam.ReadU8(puVar6 + 2);

        if ((PsxRam.ReadI32(param_2 + 0x138) & 0x10000000) == 0)
        {
            goto LAB_80026194;
        }

        if (PsxRam.ReadU8(
                PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
                + 0x15bc)
            != PsxRam.ReadU8(param_2 + 0x230))
        {
            PsxRam.WriteU8(param_2 + 0x22e, 0);
            PsxRam.WriteU8(param_2 + 0x22f, 0);
            goto LAB_80026194;
        }

        if ((PsxRam.ReadI32(param_2 + 0x138) & 0x40) == 0)
        {
            goto LAB_80026194;
        }

        if (PsxRam.ReadU8(param_2 + 0x16a) == PsxRam.ReadU8(param_2 + 0x22e)
            || PsxRam.ReadU8(param_2 + 0x16a) == PsxRam.ReadU8(param_2 + 0x22f))
        {
            bVar4 = true;
        }

        if (((PsxRam.ReadU8(param_1 + 0x22c) & 0x80) != 0) || !bVar4)
        {
            goto LAB_80026194;
        }

        if (SharedHighRam.DAT_801ff01c == 1)
        {
            bVar8 = (byte)(bVar8 + 0x23);
            goto LAB_80026174;
        }
        else if (SharedHighRam.DAT_801ff01c < 2)
        {
            iVar7 = (int)((uint)bVar8 << 0x18);
            if (SharedHighRam.DAT_801ff01c == 0)
            {
                bVar8 = (byte)(bVar8 + 0x19);
                goto LAB_80026174;
            }
        }
        else
        {
            iVar7 = (int)((uint)bVar8 << 0x18);
            if (SharedHighRam.DAT_801ff01c == 2)
            {
                bVar8 = (byte)(bVar8 + 0x2d);
                goto LAB_80026174;
            }
        }

        goto AfterBiasClamp;

    LAB_80026174:
        iVar7 = (int)((uint)bVar8 << 0x18);

    AfterBiasClamp:
        if (iVar7 < 0)
        {
            bVar8 = 100;
        }

    LAB_80026194:
        uVar1 = PsxRam.ReadU8(
            PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
            + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
            + 0x15bc);
        uVar2 = PsxRam.ReadU8(param_2 + 0x22e);
        PsxRam.WriteU8(param_2 + 0x22e, PsxRam.ReadU8(param_2 + 0x16a));
        PsxRam.WriteU8(param_2 + 0x22f, uVar2);
        PsxRam.WriteU8(param_2 + 0x230, uVar1);

        return (((iVar5 % 0x65) * 0x1000000) >> 0x18) < (sbyte)bVar8;
    }

    // GHIDRA: FUN_8004a108 @ 0x8004A108 (VS.EXE)
    // 1040 bytes, six callers (FUN_8004a910, FUN_8004a97c, FUN_8004aa9c, FUN_8004bf50,
    // FUN_8004dfc4, FUN_8004fd24 — none in this slice), every one passing a second argument. But
    // Ghidra's own signature is `void FUN_8004a108(int param_1)`, ONE parameter: no path through
    // this body reads a1. That is settled analysis (the switch below reads *(param_1+0x16a)
    // directly, never an argument), not a gap, so this port exposes the one-parameter signature
    // the body actually uses.
    //
    // KI-GAUGE DECREMENT, CLAMPED AT 0. Same shape as FUN_8004e108's CtxGaugeContribution write:
    // pick a row/column pair from a small byte table, scale, apply the +0x138-bit-4 /50 division,
    // then apply the result to CtxKiGauge (0x15B4) — here as a SUBTRACTION, floored at zero,
    // rather than FUN_8004e108's addition into CtxGaugeContribution.
    //
    //   ROW    the same indirect lookup as FUN_8004e108 (this fighter's own slot looked up in
    //          ctx's CtxFighterSlots, the stored pointer dereferenced, its first ushort read).
    //          Reproduced as written for the same reason — see FUN_8004e108's header note.
    //   COLUMN local_18, 0..11, selected by +0x138 bits 0x40000/0x80000/0x80/0x20000 (8/9/10/11,
    //          tested BEFORE the state switch and skipping it outright) or by the state byte
    //          +0x16A (0x13/0x14 -> 0, 0x21 -> 1, 0x23..0x28 -> 6/7/5/3/4/2); any other state ->
    //          local_10 = 0.
    //   TABLE  base 0x80083968, stride 0xc (12) bytes per row — confirmed the same way as
    //          FUN_8004e108's table. No Ghidra symbol names it. Row 0 read back all zero, row 1
    //          is {12,16,14,14,14,14,14,14,12,40,24,40}.
    //
    // Column 1 (state 0x21) additionally divides by 8 with a round-toward-zero adjustment
    // (`if (value < 0) value += 7;` before the arithmetic shift) that Ghidra folds to `if (false)`
    // because the value flowing in — a table byte times 100 — is always non-negative at this
    // point. The branch is real in the disassembly (`bgez`); it is kept literal rather than
    // erased, per rule 7/12, even though it cannot fire with the inputs this function ever sees.
    internal static void FUN_8004a108(int param_1)
    {
        int local_18;

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x40000) != 0)
        {
            local_18 = 8;
        }
        else if ((PsxRam.ReadI32(param_1 + 0x138) & 0x80000) != 0)
        {
            local_18 = 9;
        }
        else if ((PsxRam.ReadI32(param_1 + 0x138) & 0x80) != 0)
        {
            local_18 = 10;
        }
        else if ((PsxRam.ReadI32(param_1 + 0x138) & 0x20000) != 0)
        {
            local_18 = 0xb;
        }
        else
        {
            switch ((sbyte)PsxRam.ReadU8(param_1 + 0x16a))
            {
                case 0x13:
                case 0x14:
                    local_18 = 0;
                    break;
                case 0x21:
                    local_18 = 1;
                    break;
                case 0x23:
                    local_18 = 6;
                    break;
                case 0x24:
                    local_18 = 7;
                    break;
                case 0x25:
                    local_18 = 5;
                    break;
                case 0x26:
                    local_18 = 3;
                    break;
                case 0x27:
                    local_18 = 4;
                    break;
                case 0x28:
                    local_18 = 2;
                    break;
                default:
                    local_18 = -1;
                    break;
            }
        }

        int local_10;
        if (local_18 == -1)
        {
            local_10 = 0;
        }
        else
        {
            local_10 = PsxRam.ReadU8(
                unchecked((int)0x80083968) + local_18 +
                PsxRam.ReadU16(
                    PsxRam.ReadI32(
                        PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                        + BattleState.CtxFighterSlots
                        + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * 4)) * 0xc);
        }

        local_10 *= 100;

        if (local_18 == 8)
        {
            local_10 /= 0x1c;
        }

        if (local_18 == 1)
        {
            if (local_10 < 0) // real branch (see header note); unreachable with this function's inputs
            {
                local_10 += 7;
            }

            local_10 >>= 3;
        }

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x10) != 0)
        {
            local_10 /= 0x32;
        }

        int gaugeAddr =
            PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
            + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
            + BattleState.CtxKiGauge;

        PsxRam.WriteU16(gaugeAddr,
            unchecked((ushort)((short)PsxRam.ReadU16(gaugeAddr) - (short)local_10)));

        if ((short)PsxRam.ReadU16(gaugeAddr) < 0)
        {
            PsxRam.WriteU16(gaugeAddr, 0);
        }
    }

    // GHIDRA: FUN_8004a518 @ 0x8004A518 (VS.EXE)
    // 288 bytes. One caller, FUN_8004a638 (ported below, this file): `if (param_2 == 0x1d)
    // FUN_8004a518(param_1);`. Ki-gauge increment: +300, capped at BattleState.CtxKiGaugeCap
    // (16000) — the same cap CtxKiGauge's own note already documents.
    internal static void FUN_8004a518(int param_1)
    {
        int gaugeAddr =
            PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
            + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
            + BattleState.CtxKiGauge;

        PsxRam.WriteU16(gaugeAddr, unchecked((ushort)((short)PsxRam.ReadU16(gaugeAddr) + 300)));

        if (BattleState.CtxKiGaugeCap < (short)PsxRam.ReadU16(gaugeAddr))
        {
            PsxRam.WriteU16(gaugeAddr, (ushort)BattleState.CtxKiGaugeCap);
        }
    }

    // GHIDRA: FUN_8004a97c @ 0x8004A97C (VS.EXE)
    // 108 bytes, 5 callers: FUN_8004b098, FUN_8004b9cc, FUN_8004bd3c, FUN_8004c3e0 (none in this
    // slice) and this file's own FUN_8004de90 below (twice). Every caller passes a state opcode
    // in the same 0..0x28 range FighterSetState's own callers use.
    //
    // FORCES A FIGHTER STATE: calls FighterSetState(fighter, state), sets +0x138 bit 8, then
    // calls FUN_8004a108 — the Ki-gauge decrement already ported above. Ghidra's decompiler
    // prints the FUN_8004a108 call with param_2 forwarded (`FUN_8004a108(param_1,param_2)`), but
    // FUN_8004a108's own header note already closes that as a rendering artifact of a call site
    // passing an unread second argument — its real signature takes ONE parameter, so this port
    // calls it that way.
    internal static void FUN_8004a97c(int param_1, int param_2)
    {
        FighterSetState(param_1, (ushort)param_2);
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 8);
        FUN_8004a108(param_1);
    }

    // GHIDRA: FUN_8004a638 @ 0x8004A638 (VS.EXE)
    // 728 bytes, 17 callers across the FUN_8004Axxx..FUN_8005xxx families (none in this slice).
    // Ghidra's own parameter type is `uint param_2`; every observed call site passes a literal
    // FSM opcode (0 or 0x1d) or one relayed unchanged from a caller's own param_2 — the same
    // opcode space FighterSetState's own second argument already uses.
    //
    // THREE INDEPENDENT PIECES, none gating the others:
    //  1. STATE TRANSITION. If the fighter is already in state param_2, and param_2 is 2 or 10
    //     (the same pair piece 2 below keys off) and the halfword at +4 (raw literal; no
    //     BattleState name covers offset 4) reads 0, the PREVIOUS-state byte (+0x16B, the pair
    //     FighterSetState's own header note already closes) is re-stamped with the CURRENT state.
    //     Any other current state calls FighterSetState(fighter, param_2) as usual.
    //  2. RECOVERY-TIMER SEED, gated on param_2 being 2 or 10 (independent of piece 1's branch —
    //     a first-time transition into state 2/10 reaches this piece too, since FighterSetState
    //     was just called above). Reads a small table indexed by the CURRENT TASK's own Id field
    //     (`*(ushort *)g_CurrentTask` — DAT_8008d16c dereferenced; TaskSystem's own +0x00 Id
    //     offset is private to that file, so this reads TaskSystem.g_CurrentTask directly rather
    //     than naming the offset here), from one of two tables with no Ghidra symbol — base
    //     0x8008302C for param_2==2, 0x8008307C for param_2==10 — confirmed by decoding the
    //     LUI/ADDIU/SLL address build and reading the bytes back off the console: the first 32
    //     entries of 0x8008302C are {40,37,38,39,35,39,36,38,35,36,37,37,35,37,36,35,38,40,36,37,
    //     34,35,33,36,33,31,37,35,37,37,33,34}, of 0x8008307C are {40,35,35,36,33,37,35,37,32,33,
    //     35,35,32,35,35,36,32,37,34,33,31,34,33,31,30,30,35,35,34,34,36,37}. +0x138 bit 0x40000
    //     scales the looked-up value by 15/10; the result is divided by 4 with a round-toward-zero
    //     adjustment (`if (value < 0) value += 3;` before the arithmetic shift, a real branch per
    //     the disassembly's `bgez` — unlike FUN_8004a108's equivalent adjustment, Ghidra does not
    //     fold this one to `if (false)`, so it is kept as a live branch rather than annotated
    //     unreachable). If the PREVIOUS-state byte (+0x16B) is 2, 10 or 0x1C, the quarter is ADDED
    //     to the running value at +0x162 (raw literal; no BattleState name covers it) and then
    //     clamped down to the freshly-read table value if it overshoots; any other previous state
    //     simply OVERWRITES +0x162 with the quarter.
    //  3. KI TOP-UP on param_2 == 0x1D: calls FUN_8004a518 (the +300/cap-16000 increment already
    //     in this file) and drops +0x138 bit 0x40000 — the SAME bit piece 2 tests, so this only
    //     affects a FUTURE call into this function, never the one currently running.
    internal static void FUN_8004a638(int param_1, int param_2)
    {
        if (PsxRam.ReadU8(param_1 + 0x16a) == param_2)
        {
            if ((param_2 == 2 || param_2 == 10) && (short)PsxRam.ReadU16(param_1 + 4) == 0)
            {
                PsxRam.WriteU8(param_1 + 0x16b, PsxRam.ReadU8(param_1 + 0x16a));
            }
        }
        else
        {
            FighterSetState(param_1, (ushort)param_2);
        }

        if (param_2 == 2 || param_2 == 10)
        {
            // *DAT_8008d16c: the current task's own Id field (TaskSystem's private +0x00 offset).
            int taskId = PsxRam.ReadU16(TaskSystem.g_CurrentTask);

            short local_10 = (short)PsxRam.ReadU16(
                (param_2 == 2 ? unchecked((int)0x8008302c) : unchecked((int)0x8008307c)) + taskId * 2);

            if ((PsxRam.ReadI32(param_1 + 0x138) & 0x40000) != 0)
            {
                local_10 = (short)((local_10 * 0xf) / 10);
            }

            int iVar2 = local_10;
            if (iVar2 < 0)
            {
                iVar2 += 3;
            }

            short sVar1 = (short)(iVar2 >> 2);

            byte prevState = PsxRam.ReadU8(param_1 + 0x16b); // +0x16B: FighterSetState's own previous-state byte
            if (prevState == 2 || prevState == 10 || prevState == 0x1c)
            {
                PsxRam.WriteU16(param_1 + 0x162, // raw literal; no BattleState name covers +0x162
                    unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x162) + sVar1)));

                if (local_10 < (short)PsxRam.ReadU16(param_1 + 0x162))
                {
                    PsxRam.WriteU16(param_1 + 0x162, unchecked((ushort)local_10));
                }
            }
            else
            {
                PsxRam.WriteU16(param_1 + 0x162, unchecked((ushort)sVar1));
            }
        }

        if (param_2 == 0x1d)
        {
            FUN_8004a518(param_1);
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffbffff));
        }
    }

    // GHIDRA: FUN_8004de90 @ 0x8004DE90 (VS.EXE)
    // 308 bytes. One caller, FUN_8004e758 (now in this file, below): `FUN_8004de90(uVar4,bVar3);`,
    // where uVar4 is the OPPOSING fighter (the same variable FUN_8004d0fc's header note already
    // closes — FUN_8004e758 resolves it once, through its own +0xf4/+0xc task-node chain, and
    // reuses it across every callee in that stretch). So this function's param_1 is the fighter
    // BEING HIT, not the one attacking.
    //
    // HIT-REACTION FLAG BLOCK. Unconditionally clears +0x138's low-mid bits (the bits the
    // 0xfffd8000 mask drops) and sets bit 0x80, and clears +0x134 bits 0x80000000|0x20000000 (the
    // bits the 0x5fffffff mask drops), all three on the fighter being hit. Then, ONLY for param_2
    // in {4, 5, 6}, forces a state through FUN_8004a97c: param_2==5 picks 0x26 or 0x27 depending
    // on whether the halfword at +0x116 (inside FighterZeroedFrom114's zeroed range; no more
    // specific BattleState name covers it) is zero; param_2 4 or 6 both force 0x28. Any other
    // param_2 value only applies the flag block above and returns.
    internal static void FUN_8004de90(int param_1, int param_2)
    {
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffd8000));
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x80);
        PsxRam.WriteI32(param_1 + 0x134, PsxRam.ReadI32(param_1 + 0x134) & 0x5fffffff);

        if (param_2 == 5)
        {
            int state = (short)PsxRam.ReadU16(param_1 + 0x116) == 0 ? 0x26 : 0x27;
            FUN_8004a97c(param_1, state);
        }
        else
        {
            if (param_2 < 6)
            {
                if (param_2 != 4)
                {
                    return;
                }
            }
            else if (param_2 != 6)
            {
                return;
            }

            FUN_8004a97c(param_1, 0x28);
        }
    }

    // GHIDRA: FUN_8004dfc4 @ 0x8004DFC4 (VS.EXE)
    // 324 bytes. One caller, FUN_8004e758 (now in this file, below): `FUN_8004dfc4(param_1,uVar4)`,
    // where param_1 is FUN_8004e758's own first argument (the ACTING fighter, same naming
    // FUN_8004d0fc's header note already closes) and uVar4 is the OPPOSING fighter it resolves
    // through its own +0xf4/+0xc task-node chain. So here param_1 is the acting fighter and
    // param_2 is the target.
    //
    // DISPATCHES ON THE ACTING FIGHTER'S ATTACK-RECORD TYPE BYTE — byte 1 (the `>>8` half) of the
    // word at param_1+0xdc, a field with no BattleState name — to one of three callees not in
    // this slice, each passed (target, a literal FSM-shaped opcode, actor): type 1 or 2 ->
    // FUN_8004d32c with opcode 0x16; type 3 -> FUN_8004d9f4, also opcode 0x16; type 4 ->
    // FUN_8004d694 opcode 0x19; type 5 -> FUN_8004d694 opcode 0x1a; type 6 -> FUN_8004d694 opcode
    // 0x18. Any other type byte dispatches nothing. Afterward, UNCONDITIONALLY (not part of the
    // switch), type 3 additionally calls FUN_8004a108 on the acting fighter — Ghidra prints a
    // second argument (`*(undefined1*)(param_1+0x16a)`) that FUN_8004a108's own header note
    // already closes as unread; this port calls it with the one parameter its body actually uses.
    // This is FUN_8004a108's sixth and last caller this file's own header note already counted.
    //
    // FUN_8004d32c/FUN_8004d9f4/FUN_8004d694 are NOT in this slice (584/1180/864 bytes) and stay
    // BLOCKED stubs below, called exactly where the original calls them.
    internal static void FUN_8004dfc4(int param_1, int param_2)
    {
        sbyte cVar1 = (sbyte)(PsxRam.ReadI32(param_1 + 0xdc) >> 8);

        switch (cVar1)
        {
            case 1:
            case 2:
                FUN_8004d32c(param_2, 0x16, param_1);
                break;
            case 3:
                FUN_8004d9f4(param_2, 0x16, param_1);
                break;
            case 4:
                FUN_8004d694(param_2, 0x19, param_1);
                break;
            case 5:
                FUN_8004d694(param_2, 0x1a, param_1);
                break;
            case 6:
                FUN_8004d694(param_2, 0x18, param_1);
                break;
        }

        if (cVar1 == 3)
        {
            FUN_8004a108(param_1);
        }
    }

    // GHIDRA: FUN_8004d32c @ 0x8004D32C (VS.EXE)
    // BLOCKED: 584 bytes, out of this slice. Called from FUN_8004dfc4 above as
    // FUN_8004d32c(target, 0x16, actor) on attack-record type 1 or 2.
    private static void FUN_8004d32c(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FUN_8004d9f4 @ 0x8004D9F4 (VS.EXE)
    // BLOCKED: 1180 bytes, out of this slice. Called from FUN_8004dfc4 above as
    // FUN_8004d9f4(target, 0x16, actor) on attack-record type 3.
    private static void FUN_8004d9f4(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FUN_8004d694 @ 0x8004D694 (VS.EXE)
    // BLOCKED: 864 bytes, out of this slice. Called from FUN_8004dfc4 above as
    // FUN_8004d694(target, opcode, actor) on attack-record type 4, 5 or 6 (opcode 0x19/0x1a/0x18
    // respectively).
    private static void FUN_8004d694(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FighterSetState @ 0x80047C64 (VS.EXE)
    // Already named and partly documented in the Ghidra database itself: the decompiler comment
    // there identifies fighter+0x16A/+0x16B as the state/previous-state pair, which is why this
    // port keeps that name and reads it as settled rather than speculative (rule 6/11 — this is
    // not a name this port is proposing).
    //
    // 148 bytes, 15 callers across the FUN_8004Axxx..FUN_8004Dxxx family plus FUN_80050514 and
    // FUN_800507d0 (none in this slice), always with a literal FSM opcode as the second argument
    // (0x1c, 0x1f, 0x20, 0x21, 0x22, 0x2a, ...) — the same values FighterTask.cs and this file's
    // own switches test +0x16A against. Only the low byte of the ushort argument is stored.
    internal static void FighterSetState(int fighter, ushort state)
    {
        FUN_80053970(fighter, PsxRam.ReadI32(PsxRam.ReadI32(fighter + 0x148) + 0x38), state);

        PsxRam.WriteU8(fighter + 0x16b, PsxRam.ReadU8(fighter + 0x16a));
        PsxRam.WriteU8(fighter + 0x16a, (byte)state);

        FUN_80026424(fighter);
    }

    // GHIDRA: FUN_80053970 @ 0x80053970 (VS.EXE)
    // BLOCKED: 96 bytes, out of this slice. AnimCmdEffects.cs already carries an independent
    // private stub for this SAME address (called there from AnimCmd_EffSet's re-arm path with a
    // different argument shape); that stub is local to that file's class and not reachable from
    // here, so this is FighterCombat's own placeholder, called as FighterSetState actually calls
    // it: (fighter, *(int*)(*(int*)(fighter+0x148)+0x38), state).
    private static void FUN_80053970(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FUN_80026424 @ 0x80026424 (VS.EXE)
    // BLOCKED: out of this slice. FighterSetState's tail call, run after every state/prevState
    // transition.
    private static void FUN_80026424(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8004d574 @ 0x8004D574 (VS.EXE)
    // BLOCKED: 288 bytes, out of this slice. One caller, FUN_8004ee48 below. Ghidra's OWN
    // analysis of this function's body settles a two-parameter signature —
    // `void FUN_8004d574(int param_1, ushort param_2)` — that reads neither parameter for
    // anything the body keeps; its real work runs off two globals (DAT_8008d160/DAT_8008d164,
    // both out of this slice) and a call to FUN_8005ef20, none of it analyzed here. The CALL
    // SITE inside FUN_8004ee48 renders with five arguments in Ghidra's decompilation of that
    // caller (`FUN_8004d574(iVar7,0x16,param_3,uVar9,uVar3)`) — an artifact of the caller's own
    // side (see FUN_8004ee48's header note on its own unread param_3), not evidence of a wider
    // callee signature: the three trailing values never reach a parameter this function's body
    // reads. Called the way the console's own two real arguments call it: (target, 0x16).
    private static void FUN_8004d574(int param_1, ushort param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_8004ee48 @ 0x8004EE48 (VS.EXE)
    // 1292 bytes. Declared nowhere else in the port. One caller: the call itself sits at
    // 0x80042adc (`iVar3 = FUN_8004ee48(iVar4);`) inside a stretch of code Ghidra has not yet
    // carved into a named, analyzed function (it previews under the placeholder name
    // UndefinedFunction_800429a8) — out of this slice either way.
    //
    // GHIDRA DECLARES FOUR PARAMETERS FOR THIS FUNCTION (param_1..param_4, the last `uint`), BUT
    // ONLY ONE IS REAL. The one call site passes exactly ONE argument, so param_2/param_3/param_4
    // are never genuine caller-supplied values: param_2 is never read anywhere in this body
    // (checked line by line); param_4 only feeds an LWL/LWR unaligned-load pair (see the
    // block-copy note below — the ISA makes that pair's result independent of whatever was
    // already in the register); and param_3, though it IS forwarded to one call
    // (`FUN_8004d574(iVar7,0x16,param_3,uVar9,uVar3)` in Ghidra's own rendering of that call
    // site), is forwarded to a parameter slot FUN_8004d574's OWN analyzed signature
    // (`void FUN_8004d574(int, ushort)`) never reads — see that function's header note. So
    // param_3's value can never affect behavior either. This port exposes the signature the body
    // and its one caller actually use: a single parameter.
    //
    // APPLIES ONE ATTACK-EVENT RECORD (param_1 itself — a standalone record with its own +0x3c/
    // +0x50../+0x6c/+0x70/+0x78/+0xbc fields, not a fighter workspace) TO ITS TARGET. param_1+0x70
    // resolves (one dereference) to a task node this function stamps the record onto, and, one
    // MORE hop via that node's own +0xc/+8, to the TARGET FIGHTER; param_1+0x3c resolves (one hop
    // via +8) to the ACTING fighter — the "attacker" the workflow that asked for this function
    // names. Neither chain is the already-documented "+0xf4/+0xc" shape FUN_8004e758's own
    // callees below use — this record has its own, unrelated "+0x70/+0xc" and "+0x3c/+0x8" pair
    // of chains — so neither gets that name here.
    //
    // VALIDATION (all early-return -1, all SKIPPED when the record's own type byte, +0x6c>>8, is
    // 0x80 — the same "alternate target" value FUN_8004e5d0's own header note already names):
    //   - the task node reached through param_1+0x70's own +0xc hop must differ from param_1+0x3c
    //     itself, checked BEFORE the 0x80 skip even applies (a self-target guard).
    //   - unless the record's own +0x78 bit 0x4000000 is set, the attacker's and target's own
    //     FighterSlotIndex (+0x173, read through the two chains above) must fall on OPPOSITE
    //     sides of the 0..5 / 6..11 half — both on the same half bails (a same-team guard).
    //   - the target fighter's own +0x138 bit 0x8000, +0x134 bits 0x6000000 (only when the
    //     RECORD's own +0xbc byte is 3), +0x138 bit 0x4000000, and +0x138 bits 0x3800 (unless the
    //     RECORD's own +0x78 bit 0x10 or 0x40 is set) each bail on their own, in that order.
    //
    // APPLICATION (unconditional once validation passes): stamps the record's own type halfword
    // (+0x6c) onto the task node's +0x18; when the record's +0x78 bit 0x4000000 is set, also
    // stores the task node pointer into the ATTACKER's own +0x30; links the task node's own +8
    // back to param_1+0x3c; and copies two word pairs from the record onto the task node's SAME
    // relative offsets (+0x58/+0x5c -> +0x34/+0x38, +0x50/+0x54 -> +0x2c/+0x30 — see the
    // block-copy note below).
    //
    // THEN, UNLESS THE RECORD'S +0x78 BIT 0x4000000 IS SET, AND UNLESS THE RECORD ITSELF IS THE
    // 0x80 ALTERNATE-TARGET TYPE (either one returns 0 with no further effect): re-resolves the
    // target fighter, copies the record's own +0x50/+0x54 word pair onto the target's +0xc0/+0xc4
    // (the SAME two source words as the task-node copy above, a second destination), and — only
    // when the target's own +0x138 bits 0x40000 and 1 are BOTH clear (else this returns 1,
    // meaning "blocked") — calls FUN_8004d574(target, 0x16) and FUN_8004e580(target)
    // unconditionally, then, unless the target's own state (+0x16A) is 0x17, calls
    // FUN_8004e108(attacker, 1) — THE gauge-contribution seed this whole workflow exists to
    // reach — and, when the target's own +0x138 bits 0x30000000 are clear, stamps the target's
    // own +0xac/+0x22a "current task" bookkeeping pair the same way FUN_8004e758 does below.
    //
    // THE LWL/LWR/SWL/SWR BLOCKS collapse to plain word copies for a stronger reason than the
    // alignment argument FUN_8004e5d0's own header note gives: an LWL into a register followed by
    // an LWR into that SAME register — every pair below is exactly that — always yields precisely
    // `*(uint*)address` by the MIPS instruction set's own definition, for ANY address, aligned or
    // not. That pairing is the architecture's generic unaligned-load idiom; it does not depend on
    // this engine's structures happening to be aligned. The matching SWL/SWR pairs on the write
    // side reduce the same way. Checked instruction by instruction against the disassembly, not
    // assumed.
    internal static int FUN_8004ee48(int param_1)
    {
        DiagEe48Calls++;

        if (PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0x70) + 0xc) == PsxRam.ReadI32(param_1 + 0x3c))
        {
            return -1;
        }

        if ((PsxRam.ReadI32(param_1 + 0x78) & 0x4000000) == 0
            && PsxRam.ReadI32(param_1 + 0x6c) >> 8 != 0x80)
        {
            byte bVar1 = PsxRam.ReadU8(
                PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0x3c) + 8) + BattleState.FighterSlotIndex);
            byte bVar2 = PsxRam.ReadU8(
                PsxRam.ReadI32(PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0x70) + 0xc) + 8)
                + BattleState.FighterSlotIndex);

            if ((bVar1 < 6 && bVar2 < 6) || (bVar1 > 5 && bVar2 > 5))
            {
                return -1;
            }
        }

        if (PsxRam.ReadI32(param_1 + 0x6c) >> 8 != 0x80)
        {
            int iVar7 = PsxRam.ReadI32(PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0x70) + 0xc) + 8);

            if ((PsxRam.ReadI32(iVar7 + 0x138) & 0x8000) != 0)
            {
                return -1;
            }

            if ((PsxRam.ReadI32(iVar7 + 0x134) & 0x6000000) != 0 && (sbyte)PsxRam.ReadU8(param_1 + 0xbc) == 3)
            {
                return -1;
            }

            if ((PsxRam.ReadI32(iVar7 + 0x138) & 0x4000000) != 0)
            {
                return -1;
            }

            if ((PsxRam.ReadI32(iVar7 + 0x138) & 0x3800) != 0)
            {
                return -1;
            }
        }

        PsxRam.WriteU16(PsxRam.ReadI32(param_1 + 0x70) + 0x18, PsxRam.ReadU16(param_1 + 0x6c));

        if ((PsxRam.ReadI32(param_1 + 0x78) & 0x4000000) != 0)
        {
            PsxRam.WriteI32(
                PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0x3c) + 8) + 0x30, PsxRam.ReadI32(param_1 + 0x70));
        }

        PsxRam.WriteI32(PsxRam.ReadI32(param_1 + 0x70) + 8, PsxRam.ReadI32(param_1 + 0x3c));

        // Block copy onto the task node's OWN relative offsets: record+0x58/+0x5c -> node+0x34/
        // +0x38, record+0x50/+0x54 -> node+0x2c/+0x30.
        int iVar7b = PsxRam.ReadI32(param_1 + 0x70);
        PsxRam.WriteI32(iVar7b + 0x34, PsxRam.ReadI32(param_1 + 0x58));
        PsxRam.WriteI32(iVar7b + 0x38, PsxRam.ReadI32(param_1 + 0x5c));
        PsxRam.WriteI32(iVar7b + 0x2c, PsxRam.ReadI32(param_1 + 0x50));
        PsxRam.WriteI32(iVar7b + 0x30, PsxRam.ReadI32(param_1 + 0x54));

        if ((PsxRam.ReadI32(param_1 + 0x78) & 0x4000000) != 0)
        {
            return 0;
        }

        if ((sbyte)((uint)PsxRam.ReadI32(param_1 + 0x6c) >> 8) == -0x80)
        {
            return 0;
        }

        int targetFighter = PsxRam.ReadI32(PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0x70) + 0xc) + 8);

        // Second destination for the SAME source word pair: record+0x50/+0x54 -> target+0xc0/
        // +0xc4.
        PsxRam.WriteI32(targetFighter + 0xc0, PsxRam.ReadI32(param_1 + 0x50));
        PsxRam.WriteI32(targetFighter + 0xc4, PsxRam.ReadI32(param_1 + 0x54));

        if ((PsxRam.ReadI32(targetFighter + 0x138) & 0x40000) != 0
            || (PsxRam.ReadI32(targetFighter + 0x138) & 1) != 0)
        {
            return 1;
        }

        FUN_8004d574(targetFighter, 0x16);
        FUN_8004e580(targetFighter);

        if (PsxRam.ReadU8(targetFighter + 0x16a) != 0x17)
        {
            FUN_8004e108(PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0x3c) + 8), 1);

            if ((PsxRam.ReadI32(targetFighter + 0x138) & 0x30000000) == 0)
            {
                PsxRam.WriteI32(targetFighter + 0xac, PsxRam.ReadI32(param_1 + 0x3c));
                PsxRam.WriteU16(targetFighter + 0x22a, 0x3c);
            }
        }

        return 0;
    }

    // GHIDRA: FUN_8004e758 @ 0x8004E758 (VS.EXE)
    // 1776 bytes. Two named callers (FUN_800501b8, once; FUN_80050824, twice — neither in this
    // slice) plus the one FighterTask.cs itself carries, at its own step 9.6: `FUN_8004e758(iVar3,
    // 0);`, guarded there by +0x134 bit 31 set and bit 29 clear.
    //
    // FIGHTERTASK.CS STILL DECLARES AN EMPTY STUB FOR THIS SAME ADDRESS. That file is not this
    // one's to edit (see this port's own file-level mandate), so the real body lives HERE, under
    // its own GHIDRA annotation, called by FighterTask.cs's existing (out-of-file) call site
    // exactly as before. FighterTask.cs's copy at its own `private static void
    // FUN_8004e758(int param_1, int param_2) { _ = param_1; _ = param_2; }` is now a stale
    // duplicate declaration of this same address and needs removing by whoever owns that file —
    // flagged here, not fixed here, the same way this project treats every other intra-VS
    // duplicate.
    //
    // GHIDRA DECLARES FOUR PARAMETERS (`uint FUN_8004e758(int param_1,int param_2,undefined4
    // param_3,uint param_4)`), BUT ONLY TWO ARE REAL. param_3 is never read anywhere in this
    // body; param_4 only feeds one LWL/LWR unaligned-load pair, the same architecture-level
    // artifact FUN_8004ee48's own header note above closes. Every observed call site (the three
    // named callers above and FighterTask.cs's own) passes exactly two arguments — a fighter and
    // a small record-slot index (0 or 1) — so this port exposes that two-argument signature.
    //
    // param_1 is the ACTING fighter; param_2 selects which of (at least two) 0x10-byte
    // attack-event records at param_1+0xdc to process. uVar4, resolved once near the top through
    // param_1's own +0xf4/+0xc/+8 task-node chain, is the OPPOSING fighter — the SAME chain and
    // the SAME role FUN_8004d0fc's, FUN_80025f38's and FUN_8004dfc4's own header notes already
    // document from their caller's side; this is that caller.
    //
    // THE SHAPE: read the selected record's type byte (bVar3, byte 1 of the record's own first
    // word — the SAME "attack-record type byte" FUN_8004dfc4's own header note already names,
    // read here one level higher up, off the record array directly rather than off the fighter's
    // +0xdc field FUN_8004dfc4 reads). Byte 0 and byte 1 both zero means no record at all (-1,
    // immediately). Type 0x80 is the SAME "alternate target" case FUN_8004e5d0's own header note
    // names — this function's entire job for that type is to call FUN_8004e5d0(param_1,param_2)
    // and return 0. Every OTHER type runs a chain of validation gates against the OPPOSING
    // fighter's own +0x138/+0x134 flags and the ACTING fighter's own +0x138 flags (each gate an
    // independent -1 early-out; the FIRST of them, uVar4's own +0x138 bit 0x80000, instead
    // triggers a FUN_8004a638(param_1,0) call — with a +0x138 bit-4 clear first — when the
    // ACTING fighter's own +0x138 bit 4 is set, before its own -1 return).
    //
    // ONCE VALIDATION PASSES: marks the acting fighter's own +0x134 bit 0x20000000; stamps the
    // opposing fighter's own +0x100 with the current task and +0x110 with the record's type
    // halfword; copies the acting fighter's own +0x124..+0x133 attack descriptor (four words)
    // onto the OPPOSING fighter's SAME relative offsets (see FUN_8004ee48's block-copy note for
    // why the LWL/LWR/SWL/SWR pairs below reduce to plain word copies, address alignment aside).
    //
    // THEN, ONLY for record types 4/5/6, AND ONLY when the opposing fighter's own Ki gauge
    // (BattleState.CtxKiGauge, read through its own slot) exceeds 399, AND its own +0x138 bit
    // 0x4000 is clear, AND its own +0xac already equals the current task: rolls a counter/block
    // check — FUN_80025f38 when the opposing fighter's own +0x138 bits 0x30000000 are clear,
    // FUN_8004d0fc otherwise (both already ported above, called with the SAME (opposing, acting)
    // argument order their own header notes document) — and, if that check succeeds (local_10 !=
    // 0, a genuine block/counter), calls FUN_8004de90(opposing, bVar3) and the rest of this
    // function treats local_10 as "blocked" from here on, skipping straight to the trailing
    // return.
    //
    // WHEN NOT BLOCKED (local_10 still 0): calls FUN_8004dfc4(acting, opposing) unconditionally,
    // then — only when the opposing fighter's own state (+0x16A) is not 0x17 — calls
    // FUN_8004e580(opposing) and, for record types 3/4/5/6, drops the opposing fighter's own
    // +0x138 bit 0x4000. Still only when NOT blocked and state != 0x17, calls
    // FUN_8004e108(acting,0) — the OTHER of FUN_8004e108's two callers this file's own header
    // note on that function already counted. Still only when not blocked, and only for record
    // types 4/5/6 with state != 0x17, sets the ACTING fighter's own +0x134 bit 0x8000000; if
    // blocked instead, drops the ACTING fighter's own +0x138 bit 0x100000.
    //
    // THE FINAL RETURN VALUE (also local_10): when not blocked AND (the record type is one of
    // {1,3,4,5,6} OR the type itself equals 2) AND the opposing fighter's own +0x138 bits
    // 0x30000000 are (freshly re-read and found) clear, stamps the opposing fighter's own +0xac/
    // +0x22a "current task" bookkeeping pair the SAME way FUN_8004ee48 does above, and returns
    // the OPPOSING FIGHTER'S OWN ADDRESS as the result — not a boolean. Every other path returns
    // -1 (rejected before validation passed), 0 (validation passed but nothing further to report,
    // or the type-0x80 shortcut), or whatever the +0x138-bits-0x30000000 mask evaluated to when
    // that mask was nonzero (local_10 keeps that masked value rather than the fighter's address).
    // None of this function's four real callers inspect the return value, so this port keeps it
    // faithfully rather than simplifying it to void.
    internal static int FUN_8004e758(int param_1, int param_2)
    {
        DiagE758Calls++;

        byte bVar3 = (byte)((uint)PsxRam.ReadI32(param_1 + param_2 * 0x10 + 0xdc) >> 8);

        if (PsxRam.ReadU8(param_1 + param_2 * 0x10 + 0xdc) == 0 && bVar3 == 0)
        {
            return -1;
        }

        if (bVar3 == 0x80)
        {
            FUN_8004e5d0(param_1, param_2);
            return 0;
        }

        int uVar4 = PsxRam.ReadI32(PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0xf4) + 0xc) + 8);

        if ((PsxRam.ReadI32(uVar4 + 0x138) & 0x80000) != 0)
        {
            if ((PsxRam.ReadI32(param_1 + 0x138) & 0x10) != 0)
            {
                PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffefffaf));
                FUN_8004a638(param_1, 0);
            }

            return -1;
        }

        if ((PsxRam.ReadI32(uVar4 + 0x138) & 0x8000) != 0 && (PsxRam.ReadI32(param_1 + 0x138) & 0x60) == 0)
        {
            return -1;
        }

        if ((PsxRam.ReadI32(uVar4 + 0x134) & 0x6000000) != 0)
        {
            return -1;
        }

        if ((PsxRam.ReadI32(uVar4 + 0x138) & 0x4000000) != 0)
        {
            return -1;
        }

        if ((PsxRam.ReadI32(uVar4 + 0x138) & 0x3800) != 0
            && (PsxRam.ReadI32(param_1 + 0x138) & 0x10) == 0
            && (PsxRam.ReadI32(param_1 + 0x138) & 0x40) == 0)
        {
            return -1;
        }

        PsxRam.WriteI32(param_1 + 0x134, PsxRam.ReadI32(param_1 + 0x134) | 0x20000000);
        PsxRam.WriteI32(uVar4 + 0x100, TaskSystem.g_CurrentTask);
        PsxRam.WriteU16(uVar4 + 0x110, PsxRam.ReadU16(param_1 + param_2 * 0x10 + 0xdc));

        // Block copy: acting fighter's own +0x124..+0x133 attack descriptor (four words) onto
        // the opposing fighter's SAME relative offsets. See FUN_8004ee48's block-copy note.
        PsxRam.WriteI32(uVar4 + 0x124, PsxRam.ReadI32(param_1 + 0x124));
        PsxRam.WriteI32(uVar4 + 0x128, PsxRam.ReadI32(param_1 + 0x128));
        PsxRam.WriteI32(uVar4 + 0x12c, PsxRam.ReadI32(param_1 + 0x12c));
        PsxRam.WriteI32(uVar4 + 0x130, PsxRam.ReadI32(param_1 + 0x130));

        // THE SAME TWO SOURCE WORDS GO TO A SECOND DESTINATION, and a first version of this port
        // dropped them. They are unconditional and sit in the same straight-line run as the four
        // above; the bytes at 0x8004EA70 leave no room for reading them as anything else:
        //     244200C0  addiu v0,v0,0xC0        ; destination + 0xC0
        //     24630124  addiu v1,v1,0x124       ; source + 0x124
        //     88670003 / 98670000  lwl/lwr a3   ; a3 = *(source + 0x124)
        //     88680007 / 98680004  lwl/lwr t0   ; t0 = *(source + 0x128)
        //     A8470003 / B8470000  swl/swr a3   ; *(destination + 0xC0) = a3
        //     A8480007 / B8480004  swl/swr t0   ; *(destination + 0xC4) = t0
        // The LWL/LWR and SWL/SWR pairs are the compiler's unaligned-access idiom; both halves
        // resolve to the same aligned word here, so each pair is one plain 32-bit move. The sibling
        // FUN_8004ee48 already carries this identical second-destination copy, which is what made
        // the omission visible: the same shape appeared in one function and not the other.
        PsxRam.WriteI32(uVar4 + 0xc0, PsxRam.ReadI32(param_1 + 0x124));
        PsxRam.WriteI32(uVar4 + 0xc4, PsxRam.ReadI32(param_1 + 0x128));

        int local_10 = 0;

        if ((bVar3 == 5 || bVar3 == 6 || bVar3 == 4)
            && 399 < (short)PsxRam.ReadU16(
                PsxRam.ReadI32(uVar4 + BattleState.FighterBattleContext)
                + PsxRam.ReadU8(uVar4 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
                + BattleState.CtxKiGauge)
            && (PsxRam.ReadI32(uVar4 + 0x138) & 0x4000) == 0
            && PsxRam.ReadI32(uVar4 + 0xac) == TaskSystem.g_CurrentTask)
        {
            if ((PsxRam.ReadI32(uVar4 + 0x138) & 0x30000000) == 0)
            {
                local_10 = FUN_80025f38(uVar4, param_1) ? 1 : 0;
            }
            else
            {
                local_10 = FUN_8004d0fc(uVar4, param_1) ? 1 : 0;
            }

            if (local_10 != 0)
            {
                FUN_8004de90(uVar4, bVar3);
            }
        }

        if (local_10 == 0)
        {
            FUN_8004dfc4(param_1, uVar4);

            if (PsxRam.ReadU8(uVar4 + 0x16a) != 0x17)
            {
                FUN_8004e580(uVar4);

                if (bVar3 == 5 || bVar3 == 6 || bVar3 == 4 || bVar3 == 3)
                {
                    PsxRam.WriteI32(uVar4 + 0x138, PsxRam.ReadI32(uVar4 + 0x138) & unchecked((int)0xfffbffff));
                }
            }
        }

        if (local_10 == 0 && PsxRam.ReadU8(uVar4 + 0x16a) != 0x17)
        {
            FUN_8004e108(param_1, 0);
        }

        if (local_10 == 0)
        {
            if (PsxRam.ReadU8(uVar4 + 0x16a) != 0x17 && (bVar3 == 5 || bVar3 == 6 || bVar3 == 4))
            {
                PsxRam.WriteI32(param_1 + 0x134, PsxRam.ReadI32(param_1 + 0x134) | 0x8000000);
            }
        }
        else
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffefffff));
        }

        if (local_10 == 0)
        {
            bool matched = bVar3 == 5 || bVar3 == 6 || bVar3 == 4 || bVar3 == 3 || bVar3 == 1;

            if (!matched)
            {
                local_10 = bVar3;
                matched = local_10 == 2;
            }

            if (matched)
            {
                local_10 = PsxRam.ReadI32(uVar4 + 0x138) & 0x30000000;

                if (local_10 == 0)
                {
                    PsxRam.WriteI32(uVar4 + 0xac, TaskSystem.g_CurrentTask);
                    PsxRam.WriteU16(uVar4 + 0x22a, 0x3c);
                    local_10 = uVar4;
                }
            }
        }

        return local_10;
    }

    // =====================================================================================
    // WAVE 2 — eight standalone leaves, plus two of FUN_80055dc0's own callees. See this file's
    // header note above ("WAVE 2") for how each was reached and what, if anything, is BLOCKED.
    // =====================================================================================

    // GHIDRA: FUN_80045814 @ 0x80045814 (VS.EXE)
    // 388 bytes, zero callees. One caller, FUN_80047688 (FighterTask.cs's own BLOCKED stub, step
    // 9.5): `FUN_80045998(&DAT_80083cb4,param_1+0xf8,&DAT_80101ba4); FUN_80045814(param_1+0xf8);`
    // — so this runs against the SAME +0xf8 sub-record FUN_80045998/FUN_80045a38 below manage.
    //
    // TWO INDEPENDENT, IDENTICALLY-SHAPED CONVERSIONS: +0x1C -> +0x14, and +0x20 -> +0x16. Each
    // takes a signed 16-bit fixed-point value (9-bit fraction, i.e. /512) and stores its rounded
    // integer part, but the two directions round differently:
    //   NEGATIVE input: value >> 9 (arithmetic shift, i.e. floor-toward-negative-infinity) — the
    //     original computes `value - 0x1FF` first and only falls back to plain `value` when that
    //     is still negative, which — given the input is already negative here — it always is
    //     (value < 0 implies value-0x1FF <= -0x200 < 0), so the `-0x1FF` arm can never survive to
    //     the shift. Kept literal rather than simplified away, per rule 12/7, the same way this
    //     file already keeps FUN_8004a108's and FUN_8004a638's own unreachable rounding arms.
    //     Only the +0x14/+0x16 result from THIS branch is then floor-clamped: values below -0x40
    //     get +0x80 added.
    //   NON-NEGATIVE input: value >> 9 directly (the original's own `if (iVar2 < 0) iVar2 += 0x1FF`
    //     guard here can never fire either, since iVar2 IS the non-negative input at that point —
    //     same dead-arm shape, same reason it stays literal). No +0x40/+0x80 clamp on this side.
    // Neither +0x1C/+0x20 (the fixed-point inputs) nor +0x14/+0x16 (the rounded outputs) has a
    // BattleState name; raw literals throughout.
    internal static void FUN_80045814(int param_1)
    {
        short in1c = (short)PsxRam.ReadU16(param_1 + 0x1c);
        if (in1c < 0)
        {
            int iVar2 = in1c - 0x1ff;
            if (iVar2 < 0) // always true here (in1c < 0 implies in1c-0x1ff < 0) -- see header note
            {
                iVar2 = in1c;
            }

            short result = (short)(iVar2 >> 9);
            PsxRam.WriteU16(param_1 + 0x14, unchecked((ushort)result));

            if (result < -0x40)
            {
                PsxRam.WriteU16(param_1 + 0x14,
                    unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x14) + 0x80)));
            }
        }
        else
        {
            int iVar2 = in1c;
            if (iVar2 < 0) // real branch (bltz); unreachable -- in1c >= 0 on this side, see header note
            {
                iVar2 += 0x1ff;
            }

            PsxRam.WriteU16(param_1 + 0x14, unchecked((ushort)(iVar2 >> 9)));
        }

        short in20 = (short)PsxRam.ReadU16(param_1 + 0x20);
        if (in20 < 0)
        {
            int iVar2 = in20 - 0x1ff;
            if (iVar2 < 0) // always true here (in20 < 0 implies in20-0x1ff < 0) -- see header note
            {
                iVar2 = in20;
            }

            short result = (short)(iVar2 >> 9);
            PsxRam.WriteU16(param_1 + 0x16, unchecked((ushort)result));

            if (result < -0x40)
            {
                PsxRam.WriteU16(param_1 + 0x16,
                    unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x16) + 0x80)));
            }
        }
        else
        {
            int iVar2 = in20;
            if (iVar2 < 0) // real branch (bltz); unreachable -- in20 >= 0 on this side, see header note
            {
                iVar2 += 0x1ff;
            }

            PsxRam.WriteU16(param_1 + 0x16, unchecked((ushort)(iVar2 >> 9)));
        }
    }

    // GHIDRA: FUN_80045998 @ 0x80045998 (VS.EXE)
    // 160 bytes, zero callees. Three callers: FUN_80047688 (FighterTask.cs's own BLOCKED stub,
    // step 9.5) — `FUN_80045998(&DAT_80083cb4, param_1+0xf8, &DAT_80101ba4)`; FUN_80027340 and
    // FUN_800438c0 (neither in this slice).
    //
    // INTRUSIVE DOUBLY-LINKED LIST PUSH-FRONT — the insertion half of the pair FUN_80045a38 below
    // unlinks. param_1 is the LIST-HEAD RECORD, not a node: its own +4 is the head-pointer slot.
    // The one ported call site passes &DAT_80083cb4 as param_1 -- the SAME global
    // VS_EXE/AnimCmdEffects.cs already declares privately as `DAT_80083cb4Address` for an unrelated
    // opcode's own reader of this same chain (that file's own comment: "the head of the second
    // record's chain. AnimCmd_HitzSet walks it through each node's word 0"). Cross-referenced here,
    // not redeclared, per this project's duplicate-symbol rule.
    //
    // param_2 is the NODE being pushed -- the ported call site's own param_2 is the caller's own
    // +0xf8 sub-record (node+0x00 = next, node+0x04 = prev, node+0x10 = a payload word this
    // function stamps in from param_3). param_3 is opaque here; the one ported caller passes
    // &DAT_80101ba4 -- an ADDRESS, not a value, the same "address matters, contents do not"
    // convention AnimCmdEffects.cs's own PTR_DAT_800217f0 already uses.
    //
    //   newNode->next(+0)  = head;
    //   if (head != 0) head->prev(+4) = newNode;
    //   head                = newNode;
    //   newNode->payload(+0x10) = param_3;
    //   newNode->prev(+4)  = 0;
    internal static void FUN_80045998(int param_1, int param_2, int param_3)
    {
        int head = PsxRam.ReadI32(param_1 + 4);
        PsxRam.WriteI32(param_2, head);

        if (head != 0)
        {
            PsxRam.WriteI32(head + 4, param_2);
        }

        PsxRam.WriteI32(param_1 + 4, param_2);
        PsxRam.WriteI32(param_2 + 0x10, param_3);
        PsxRam.WriteI32(param_2 + 4, 0);
    }

    // GHIDRA: FUN_80045a38 @ 0x80045A38 (VS.EXE)
    // 184 bytes, zero callees. Three callers: FUN_80047688 (FighterTask.cs's own BLOCKED stub,
    // step 9.5, immediately before its own FUN_800539d0 call below) —
    // `FUN_80045a38(&DAT_80083cb4, param_1+0xf8)`; FUN_80026d98 and FUN_800438c0 (neither in this
    // slice).
    //
    // THE UNLINK HALF of FUN_80045998's push-front above, against the SAME node layout (+0x00
    // next, +0x04 prev) and the SAME DAT_80083cb4 list head (its own +4 the head-pointer slot):
    //
    //   if (node->next(+0) != 0) node->next->prev(+4) = node->prev(+4);
    //   if (node->prev(+4) == 0) head = node->next(+0);
    //   else                     node->prev->next(+0) = node->next(+0);
    internal static void FUN_80045a38(int param_1, int param_2)
    {
        int next = PsxRam.ReadI32(param_2);
        int prev = PsxRam.ReadI32(param_2 + 4);

        if (next != 0)
        {
            PsxRam.WriteI32(next + 4, prev);
        }

        if (prev == 0)
        {
            PsxRam.WriteI32(param_1 + 4, next);
        }
        else
        {
            PsxRam.WriteI32(prev, next);
        }
    }

    // GHIDRA: FUN_800539d0 @ 0x800539D0 (VS.EXE)
    // 272 bytes, zero callees OF ITS OWN CODE -- but one INDIRECT call through a 16-entry
    // function-pointer table at 0x80083C10 (`PTR_LAB_80083c10`), decoded off the raw bytes: 0x80054E24,
    // 0x80054C10, 0x800554E8, 0x8005574C, 0x80054990, 0x8005515C, 0x80055098, 0x80054AEC,
    // 0x8005404C, 0x80055104, 0x800558F8, 0x80053AE8, 0x80053B1C, 0x80053F04, 0x800553E8,
    // 0x80054DA4. Ghidra has not analyzed ANY of the sixteen -- each resolves only to an
    // "UndefinedFunction" preview, not a real Function -- so none can be ported this wave; see
    // DispatchHitStreamRecord below. Two callers this port sees: FUN_80047688 (FighterTask.cs's
    // own BLOCKED stub, step 9.5) — `FUN_800539d0(param_1)`, the fighter workspace itself, NOT the
    // +0xf8 sub-record FUN_80045998/FUN_80045a38 above use — and FUN_80027340 (not in this slice).
    //
    // A KEYFRAME-STREAM SCANNER against a record array whose cursor lives at fighter+8, walked one
    // 6-byte record at a time: record+0/+1 = start-frame (0xFFFF terminates the stream), +2/+3 =
    // end-frame, +4 = byte step to the record AFTER this one's 6-byte header, +5 = a handler index
    // 0..15 into the table above. Fighter+4 is the frame counter this function itself increments
    // every call -- the SAME raw offset FUN_8004a638's own header note above already names ("the
    // halfword at +4... no BattleState name covers offset 4"), now with a second, consistent use.
    // Fighter+6 is a one-shot "started" flag this function sets once and never clears.
    //
    // For each record whose [start,end] window contains the (freshly incremented) frame counter,
    // this stashes the cursor just past the record's 6-byte header, calls the indexed handler
    // (record data, fighter, &local scratch pair) through the table, then RE-READS both the cursor
    // and the frame counter from the fighter afterward -- the handler may advance either. Whether
    // matched or not, the next record is reached by stepping the (possibly handler-updated) cursor
    // forward by the CURRENT record's own byte-4 step. At stream end (start-frame 0xFFFF), the
    // cursor is restored to where it stood before the FIRST record this call examined, and the
    // frame counter is reset to 0 if the terminator's own end-frame (word 1 of the sentinel record)
    // is not still ahead of it.
    internal static void FUN_800539d0(int param_1)
    {
        int puVar6 = PsxRam.ReadI32(param_1 + 8);
        ushort uVar4 = unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 4) + 1));
        PsxRam.WriteU16(param_1 + 4, uVar4);

        ushort uVar2 = PsxRam.ReadU16(puVar6);

        // local_18[0] in the original -- the cursor snapshot restored at the end. Passed by
        // reference to the (BLOCKED) handler below exactly as the original passes &local_18.
        int[] local_18 = new int[2];
        local_18[0] = puVar6;

        while (uVar2 != 0xffff)
        {
            int puVar5 = puVar6 + 6;                    // puVar6 + 3 ushorts
            ushort uVar3 = PsxRam.ReadU16(puVar6 + 4);   // puVar6[2], used as a byte step below
            byte bVar1 = PsxRam.ReadU8(puVar6 + 5);

            if (uVar2 <= uVar4 && uVar4 <= PsxRam.ReadU16(puVar6 + 2)) // puVar6[1]
            {
                PsxRam.WriteI32(param_1 + 8, puVar5);
                DispatchHitStreamRecord(bVar1, puVar5, param_1, local_18);
                puVar5 = PsxRam.ReadI32(param_1 + 8);
                uVar4 = PsxRam.ReadU16(param_1 + 4);
            }

            puVar6 = puVar5 + (byte)uVar3;
            uVar2 = PsxRam.ReadU16(puVar6);
        }

        uVar2 = PsxRam.ReadU16(puVar6 + 2); // puVar6[1], the terminator record's own end-frame
        PsxRam.WriteI32(param_1 + 8, local_18[0]);

        if (uVar2 <= uVar4)
        {
            PsxRam.WriteU16(param_1 + 4, 0);
        }

        if ((short)PsxRam.ReadU16(param_1 + 6) == 0)
        {
            PsxRam.WriteU16(param_1 + 6, 1);
        }
    }

    // GHIDRA: none -- this is FUN_800539d0's own indirect call through PTR_LAB_80083c10, given a
    // name because the original call site has none (an indirect call has no symbol to inherit).
    // BLOCKED: all sixteen real targets (listed on FUN_800539d0's own header note above) are
    // wholly unanalyzed by Ghidra -- not even decompiled once, let alone in this slice. `local_18`
    // is passed by reference exactly as the original passes &local_18, so a future port of any of
    // the sixteen handlers can read or write local_18[1] (local_18[0] is FUN_800539d0's own cursor
    // snapshot, already meaningful before this call and read again after it) without this
    // signature changing.
    private static void DispatchHitStreamRecord(byte handlerIndex, int recordPtr, int param_1, int[] local_18)
    {
        _ = handlerIndex;
        _ = recordPtr;
        _ = param_1;
        _ = local_18;
    }

    // GHIDRA: FUN_80049e30 @ 0x80049E30 (VS.EXE)
    // 276 bytes. Three callers, all FUN_80049f54 (FighterTask.cs's own BLOCKED stub, step 9.3 --
    // the frame's command word): `FUN_80049e30(param_1,0)` (twice, gated on +0x138 bits
    // 0x10000000/0x20000000) and `FUN_80049e30(param_1,0)` again in that function's own default
    // arm. None of the three passes 1 in the decompilation this port can see, though Ghidra's own
    // signature takes a general int.
    //
    // FOUR CALLEES, NONE IN THIS SLICE: FUN_80047cf8 (288 bytes, called unconditionally first) and
    // then exactly one of FUN_80049a24 (996 bytes), FUN_800496a8 (892 bytes) or FUN_80049534 (372
    // bytes), chosen by the SAME +0x138 bit groups (0x7F00, then 0x200FF) FighterTask.cs's own
    // UpdateFighter step 9.4 already tests. Same treatment as FUN_8004dfc4's own three callees
    // above: this dispatcher is ported in full, each callee stays a BLOCKED private stub below,
    // carrying its real size.
    //
    // param_2 selects a PORT (0 or 1) into two pad-state pairs: `(&DAT_8008d3b8)[param_2]` and
    // `(&DAT_8008d3ac)[param_2]`. VS_EXE/PadInput.cs already declares all four of DAT_8008d3b8
    // (port 1)/DAT_8008d3bc (port 1 + 4 = port 2) and DAT_8008d3ac (port 1)/DAT_8008d3b0 (port 2)
    // as individual scalar fields rather than arrays -- its own header note explains why some
    // adjacent pairs there are `uint[2]` and others are not -- so the array index here is expressed
    // as a port selector against those existing internal fields instead of a second array
    // declaration over the same addresses.
    internal static int FUN_80049e30(int param_1, int param_2)
    {
        uint padState = param_2 == 0 ? PadInput.DAT_8008d3b8 : PadInput.DAT_8008d3bc;
        uint padEdge = param_2 == 0 ? PadInput.DAT_8008d3ac : PadInput.DAT_8008d3b0;
        FUN_80047cf8(param_1, (int)padState, (int)padEdge);

        int result;
        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x7f00) == 0)
        {
            if ((PsxRam.ReadI32(param_1 + 0x138) & 0x200ff) == 0)
            {
                result = FUN_80049a24(param_1);
            }
            else
            {
                result = FUN_800496a8(param_1);
            }
        }
        else
        {
            result = FUN_80049534(param_1);
        }

        return result;
    }

    // GHIDRA: FUN_80047cf8 @ 0x80047CF8 (VS.EXE)
    // BLOCKED: 288 bytes, out of this slice. Called from FUN_80049e30 above, unconditionally,
    // first: FUN_80047cf8(fighter, portPadState, portPadEdge).
    private static void FUN_80047cf8(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FUN_80049a24 @ 0x80049A24 (VS.EXE)
    // BLOCKED: 996 bytes, out of this slice. Called from FUN_80049e30 above when +0x138 bits
    // 0x7F00 and 0x200FF are both clear.
    //
    // The stub returns 0, not the original's value -- FUN_80049e30's own caller (FighterTask.cs's
    // own FUN_80049f54, out of this slice) is not ported either, so nothing here yet depends on
    // the real result.
    private static int FUN_80049a24(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_800496a8 @ 0x800496A8 (VS.EXE)
    // BLOCKED: 892 bytes, out of this slice. Called from FUN_80049e30 above when +0x138 bits
    // 0x7F00 is clear and 0x200FF is set. The stub returns 0; see FUN_80049a24's own note above.
    private static int FUN_800496a8(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_80049534 @ 0x80049534 (VS.EXE)
    // BLOCKED: 372 bytes, out of this slice. Called from FUN_80049e30 above when +0x138 bit
    // 0x7F00 is set. The stub returns 0; see FUN_80049a24's own note above.
    private static int FUN_80049534(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_80026a28 @ 0x80026A28 (VS.EXE)
    // 64 bytes, zero callees. Three callers: FUN_800501b8, FUN_800507d0, FUN_80050824 --
    // FighterTask.cs's own BLOCKED stubs for phases 7, 5(conditional half) and 5(unconditional
    // half) respectively.
    //
    // CLEARS EVERY TABLE ENTRY OWNED BY THIS FIGHTER in the thirty-record, 0x24-byte-stride table
    // at DAT_8008d610 -- VS_EXE_exe.cs's own array, declared there (`private static readonly
    // byte[] DAT_8008d610 = RamRegion(Dat8008d610Address, 0x438)`) and registered with PsxRam's
    // address resolver through LibGpu.RamRegion, so this function reaches the SAME backing storage
    // through PsxRam.Read/WriteI32 at the raw address rather than needing access to that private
    // field. VS_EXE_exe.cs's own OWNERSHIP CAVEAT on that array asks any later slice that touches
    // it to reuse it rather than redeclare -- this does, via the resolver, without redeclaring
    // anything. That file's own note already flags the array as PARTIAL ("what the thirty 0x24-byte
    // records... hold is not established by this function, which only clears them") -- this
    // function is the SAME kind of partial: entry+0x20 is a fighter-pointer field this function
    // compares against param_1, and clears entry+0/entry+4 on a match, but what the OTHER 0x20
    // bytes of each record hold is not established here either.
    //
    // Finally clears one byte at param_1+0x227, unconditionally -- no BattleState name covers it.
    internal static void FUN_80026a28(int param_1)
    {
        for (int i = 0; i < 0x1e; i++)
        {
            int entryBase = unchecked((int)0x8008d610) + i * 0x24;

            if (PsxRam.ReadI32(entryBase + 0x20) == param_1)
            {
                PsxRam.WriteI32(entryBase + 4, 0);
                PsxRam.WriteI32(entryBase, 0);
            }
        }

        PsxRam.WriteU8(param_1 + 0x227, 0);
    }

    // GHIDRA: FUN_80045af0 @ 0x80045AF0 (VS.EXE)
    // 128 bytes, zero callees. Two callers this port sees: FUN_80055dc0 below and FUN_80042f74
    // (not in this slice). Not one of the eight named leaves -- FUN_80055dc0's own callee, added
    // here per this file's header note above ("WAVE 2").
    //
    // TABLE LOOKUP KEYED ON A SCRATCHPAD PHASE COUNTER. Ghidra prints the scratchpad read as
    // `DAT_1f80007e` -- VS_EXE/AnimCmdControl.cs already closes this exact address as the vy field
    // of Scratchpad.SVECTOR_1f80007c (0x1F80007C + 2 = 0x1F80007E: "the second halfword of the
    // scratchpad SVECTOR at 0x1F80007C, which FileIo already declares and RotMatrix already
    // consumes as an SVECTOR"), cross-referenced here rather than re-declared.
    //
    //   row = (Scratchpad.SVECTOR_1f80007c.vy + param_1) & 0xFFF;   -- always 0..0xFFF
    //   -- Ghidra folds the negative-adjustment arm below to `if (false)`: an AND-0xFFF result can
    //      never be negative, so the `row += 0xFF` correction is dead code, kept per rule 12/7
    //      rather than erased (same posture as FUN_80045814's own dead rounding arms above).
    //   return *(ushort*)(0x80082E24 + (row >> 8) * 2);   -- 16 ushort-entry table, no Ghidra symbol.
    internal static ushort FUN_80045af0(short param_1)
    {
        int row = (Scratchpad.SVECTOR_1f80007c.vy + param_1) & 0xfff;

        if (row < 0) // real branch (bgez); unreachable -- row is always 0..0xFFF, see header note
        {
            row += 0xff;
        }

        return PsxRam.ReadU16(unchecked((int)0x80082e24) + (row >> 8) * 2);
    }

    // GHIDRA: FUN_80055c6c @ 0x80055C6C (VS.EXE)
    // 340 bytes, zero callees. One caller, FUN_80055dc0 below: `FUN_80055c6c(param_1, uVar1)`,
    // where uVar1 is FUN_80045af0's table-lookup result above. Not one of the eight named leaves --
    // FUN_80055dc0's own second callee, added here per this file's header note ("WAVE 2").
    //
    // STAMPS THE LOW BYTE OF +0x134 FROM param_2, THEN CHASES FOUR TABLE INDIRECTIONS off a base
    // pointer read from +0x148. Each of the four shares one shape: read a raw value, store it
    // as-is, and if its sign bit is CLEAR (i.e. it reads like a small positive offset rather than
    // an absolute KSEG0 pointer or a negative sentinel already resolved), ALSO add the object's own
    // base pointer (+0x00) and overwrite the field with that sum instead. None of the four fields
    // (+0x84, +0x8C, +0xA4, +0x98/+0x94), the two byte selectors (+0xA8, +0xAA) or the base table
    // pointer (+0x148) has a name anywhere else in this port; +0x80 is read here but never written,
    // presumably seeded by a caller outside this slice.
    //
    //   +0x134 low byte       = param_2 & 0xFF (upper 24 bits of +0x134 preserved)
    //   idx                   = (param_2 & 0x3F) * 4
    //   +0x84  (raw/resolved) = *(int*)(+0x148 + idx)
    //   +0x8C  (raw/resolved) = *(int*)(+0x148 + idx + 0x1C)
    //   +0xA4  (raw/resolved) = *(int*)( *(byte*)(+0xA8) * 4 + [+0x8C, post-resolve] )
    //   +0x98  (raw/resolved) = *(int*)( *(byte*)( *(byte*)(+0xAA)*3 + [+0xA4,post-resolve] + 1) * 4
    //                                    + +0x80 )
    //   +0x94  (raw/resolved) = *(int*)( *(byte*)( *(byte*)(+0xAA)*3 + [+0xA4,post-resolve] + 2) * 4
    //                                    + [+0x84, post-resolve] )
    internal static void FUN_80055c6c(int param_1, uint param_2)
    {
        PsxRam.WriteI32(param_1 + 0x134,
            unchecked((int)(((uint)PsxRam.ReadI32(param_1 + 0x134) & 0xffffff00u) | (param_2 & 0xff))));

        int idx = (int)(param_2 & 0x3f) * 4;
        int tableBase = PsxRam.ReadI32(param_1 + 0x148);

        int iVar1 = PsxRam.ReadI32(tableBase + idx);
        PsxRam.WriteI32(param_1 + 0x84, iVar1);
        if (iVar1 >= 0)
        {
            PsxRam.WriteI32(param_1 + 0x84, iVar1 + PsxRam.ReadI32(param_1));
        }

        uint uVar2 = (uint)PsxRam.ReadI32(tableBase + idx + 0x1c);
        PsxRam.WriteI32(param_1 + 0x8c, unchecked((int)uVar2));
        if (uVar2 < 0x80000000)
        {
            PsxRam.WriteI32(param_1 + 0x8c, unchecked((int)(uVar2 + (uint)PsxRam.ReadI32(param_1))));
        }

        uVar2 = (uint)PsxRam.ReadI32(PsxRam.ReadU8(param_1 + 0xa8) * 4 + PsxRam.ReadI32(param_1 + 0x8c));
        PsxRam.WriteI32(param_1 + 0xa4, unchecked((int)uVar2));
        if (uVar2 < 0x80000000)
        {
            PsxRam.WriteI32(param_1 + 0xa4, unchecked((int)(uVar2 + (uint)PsxRam.ReadI32(param_1))));
        }

        uVar2 = (uint)PsxRam.ReadI32(
            PsxRam.ReadU8(PsxRam.ReadU8(param_1 + 0xaa) * 3 + PsxRam.ReadI32(param_1 + 0xa4) + 1) * 4
            + PsxRam.ReadI32(param_1 + 0x80));
        PsxRam.WriteI32(param_1 + 0x98, unchecked((int)uVar2));
        if (uVar2 < 0x80000000)
        {
            PsxRam.WriteI32(param_1 + 0x98, unchecked((int)(uVar2 + (uint)PsxRam.ReadI32(param_1))));
        }

        uVar2 = (uint)PsxRam.ReadI32(
            PsxRam.ReadU8(PsxRam.ReadU8(param_1 + 0xaa) * 3 + PsxRam.ReadI32(param_1 + 0xa4) + 2) * 4
            + PsxRam.ReadI32(param_1 + 0x84));
        PsxRam.WriteI32(param_1 + 0x94, unchecked((int)uVar2));
        if (uVar2 < 0x80000000)
        {
            PsxRam.WriteI32(param_1 + 0x94, unchecked((int)(uVar2 + (uint)PsxRam.ReadI32(param_1))));
        }
    }

    // GHIDRA: FUN_80055dc0 @ 0x80055DC0 (VS.EXE)
    // 60 bytes. One caller, FUN_8005070c (FighterTask.cs's own BLOCKED stub, phase 4's arm on
    // +0x138 bit 31): `FUN_80055dc0(param_1, 0)` -- Ghidra's own signature here is ONE parameter
    // (`void FUN_80055dc0(int param_1)`); the caller's second literal argument (0) is never read
    // by this body, matching this port's existing convention of exposing the signature the body
    // actually uses (see FUN_8004a108's own header note for the precedent).
    //
    // Chains this file's own FUN_80045af0 (the scratchpad-keyed table lookup) into FUN_80055c6c
    // (the four-indirection resolver), both above: looks up a table row keyed on the fighter's own
    // +0x11E halfword, then hands that row straight to FUN_80055c6c as its param_2.
    internal static void FUN_80055dc0(int param_1)
    {
        ushort uVar1 = FUN_80045af0((short)PsxRam.ReadU16(param_1 + 0x11e));
        FUN_80055c6c(param_1, uVar1);
    }

    // GHIDRA: FUN_8004ffec @ 0x8004FFEC (VS.EXE)
    // 460 bytes -- the largest of the eight, taken last. One callee, FUN_8004a638, already ported
    // above in this file (the state-transition/recovery-timer function). One caller, FUN_800501b8
    // (FighterTask.cs's own BLOCKED stub, phase 7's arm on +0x134 bit 25): `FUN_8004ffec(param_1);`.
    //
    // SCANS ALL TWELVE BATTLE SLOTS (0..11) via BattleState.CtxFighterSlots, skipping the caller's
    // own slot (FighterSlotIndex) and any empty slot. For each OTHER slot with a live fighter
    // pointer AND bit 0x200 set in a per-slot ushort at ctx+slot*CtxSlotRecordStride+0x15B0 (no
    // BattleState name; raw literal, four bytes before CtxKiGauge at the same stride), resolves
    // that slot's fighter through its own task-node +8 hop (the same shape FUN_8004e758's own
    // header note already documents) and, ONLY when THAT fighter's own +0x138 bit 26 is CLEAR,
    // ANDs a running accumulator (seeded 0x4000000, i.e. bit 26 set) with that fighter's own +0x134
    // word -- then, only when that SAME fighter's own +0x134 bit 26 is SET, calls
    // FUN_8004a638(fighter, 0).
    //
    // THE RETURN VALUE is the final accumulator shifted right 26 bits (>> 0x1A): whatever bit 26
    // was left holding across every matching fighter's own +0x134 word, ANDed together starting
    // from 1 -- i.e. 1 only if EVERY matching fighter still had +0x134 bit 26 set, 0 the moment any
    // one of them did not. The caller (FUN_800501b8, out of this slice) is not itself ported, so
    // what it does with this 0/1 result is not established here.
    internal static uint FUN_8004ffec(int param_1)
    {
        uint local_14 = 0x4000000;

        for (int local_10 = 0; local_10 < 0xc; local_10++)
        {
            if (PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) == local_10)
            {
                continue;
            }

            if (PsxRam.ReadI32(local_10 * 4
                    + PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                    + BattleState.CtxFighterSlots) == 0)
            {
                continue;
            }

            // ctx+slot*CtxSlotRecordStride+0x15B0: a per-slot ushort with no BattleState name, four
            // bytes before CtxKiGauge (+0x15B4) at the same stride.
            if ((PsxRam.ReadU16(
                    PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                    + local_10 * BattleState.CtxSlotRecordStride + 0x15b0) & 0x200) == 0)
            {
                continue;
            }

            int iVar1 = PsxRam.ReadI32(
                PsxRam.ReadI32(
                    local_10 * 4
                    + PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                    + BattleState.CtxFighterSlots) + 8);

            if ((PsxRam.ReadI32(iVar1 + 0x138) & 0x4000000) != 0)
            {
                continue;
            }

            local_14 &= (uint)PsxRam.ReadI32(iVar1 + 0x134);

            if ((PsxRam.ReadI32(iVar1 + 0x134) & 0x4000000) != 0)
            {
                FUN_8004a638(iVar1, 0);
            }
        }

        return local_14 >> 0x1a;
    }
}
