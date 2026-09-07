using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE FIGHTER COMBAT-RESOLUTION FAMILY — 0x8004Axxx..0x8004Exxx, the leaves that seed and drain
// the two per-slot meters FighterTask.cs and BattleManager.cs only ever read: CtxKiGauge
// (0x15B4) and CtxGaugeContribution (0x15B8), the running sum that feeds the tug-of-war gauge
// at ctx+0x302C. This file is the home for that family that is not already in FighterTask.cs.
//
// WHY AddSlotGaugeContribution IS THE PRIORITY. It is the only function anywhere in the image that writes
// CtxGaugeContribution — the fact the workflow that asked for this file was built to establish.
// Everything else here is either its sibling on the Ki side (FUN_8004a108/FUN_8004a518, the same
// table-lookup shape draining/filling CtxKiGauge instead) or plumbing shared by its two ROOT
// callers (FUN_8004e580, FUN_8004e5d0, FUN_8004d0fc, FUN_80025f38) or the state-machine setter
// fifteen other functions call (FighterSetState, already named by Ghidra).
//
// THE TWO ROOTS THEMSELVES are also in this file now, at the bottom, in the order the task that
// asked for them named them: FUN_8004ee48 (applies one attack-event record to its target, then
// reaches AddSlotGaugeContribution through its own +0x3c/+0x8 attacker chain) and FUN_8004e758 (the larger
// dispatcher FighterTask.cs's own step 9.6 calls — see that function's header note for the empty
// duplicate declaration still sitting in FighterTask.cs, which this file's real body replaces
// but cannot remove, since FighterTask.cs is not this file's to edit).
//
// THE SHARED TABLE-LOOKUP SHAPE. AddSlotGaugeContribution and FUN_8004a108 both do: pick a ROW by looking up
// this fighter's own slot in the battle context's CtxFighterSlots array, dereferencing the
// pointer stored there and reading its first ushort (an indirection that resolves back to the
// fighter's own field 0 in the ordinary case, but is reproduced exactly as written per rule 1 —
// see AddSlotGaugeContribution's own note); pick a COLUMN from the fighter's +0x138 flags and/or +0x16A state
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
// callees (FUN_80053970, FUN_80026424) were out of this slice — FUN_80053970 is now closed here
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
//   FUN_80049534 — 288/892/996/372 bytes) that Ghidra HAS fully analyzed but that were far outside
//   this slice. A LATER WAVE CLOSED THEM, and moved the dispatcher out with them: all five now
//   live in FighterInput.cs, under the names ReadFighterPadCommand, PushFighterPadHistory,
//   DecodeCommandFlags200FF, DecodeCommandFlagsClear and DecodeCommandFlags7F00. This file no
//   longer declares any of the five, and nothing here calls them.
// Two more (FUN_80045af0, FUN_80055c6c) are not among the eight named addresses at all — they are
// FUN_80055dc0's own two callees, both genuine leaves (Ghidra shows zero callees for either), added
// here so FUN_80055dc0 itself is not left calling into nothing.
//
// WAVE 3 — the callees of FighterTask.cs step 9.4's THIRD arm, FUN_8004cea0 (+0x138 bits
// 0x7F00 set; still a FighterTask.cs stub, not this file's to touch — this wave ports what it
// calls, not the dispatcher itself). Six direct callees (FUN_8004c9cc, FUN_8004ca54,
// FUN_8004cb24, FUN_8004cd84, FUN_8004cc64, FUN_8004c3e0) plus FUN_8004c300 (FUN_8004c3e0's own
// callee) and FUN_8004c7fc (FUN_8004cc64's own callee, and one of the two functions anywhere in
// the image that clear fighter +0x134 bits 31 and 29 together — FUN_8004de90 above is the
// other). Two more small state-forcing helpers this cluster reaches (FUN_8004aa9c, FUN_8004c2a8)
// were not named in the wave but are ported in full rather than stubbed: each calls only
// FighterSetState and/or FUN_8004a108 (both already in this file), and FUN_8004a108's own header
// note already counted them among its six callers as "none in this slice" — this wave is what
// makes one of the six no longer true. Two others this cluster reaches the same way
// (FUN_8004a910, FUN_8004ad0c) turned out to already have real bodies in FighterAction.cs, a
// concurrent agent's file covering the neighbouring 0x8004A6xx..0x8004BFxx range — this file
// cross-references FighterAction.FUN_8004a910 / FighterAction.FUN_8004ad0c rather than
// redeclaring them, per this project's duplicate-symbol rule: compare addresses, not names.
internal static class FighterCombat
{
    // JUSTIFICATION: backend MonoGame only
    // RELATION: diagnostic probes, read only by Validation/VsBootDiagnostic.cs. Nothing in the
    // transliterated runtime touches them. They exist because AddSlotGaugeContribution is the sole writer of
    // the gauge contribution that gates the whole battle scene, so "is it reached" is the one
    // question worth being able to answer without a screenshot.
    internal static int DiagE108Calls;

    internal static int DiagE758Calls;

    internal static int DiagEe48Calls;

    // GHIDRA: AddSlotGaugeContribution @ 0x8004E108 (VS.EXE)
    // 1144 bytes. Two callers: FUN_8004e758 (`AddSlotGaugeContribution(param_1,0)`) and FUN_8004ee48
    // (`AddSlotGaugeContribution(*(int*)(*(int*)(param_1+0x3c)+8), 1)` — a task-node +8 workspace resolved
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
    internal static void AddSlotGaugeContribution(int param_1, int param_2)
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
    // it unconditionally alongside their own AddSlotGaugeContribution call. Shared cleanup: drop +0x134 bit
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
    // KI-GAUGE DECREMENT, CLAMPED AT 0. Same shape as AddSlotGaugeContribution's CtxGaugeContribution write:
    // pick a row/column pair from a small byte table, scale, apply the +0x138-bit-4 /50 division,
    // then apply the result to CtxKiGauge (0x15B4) — here as a SUBTRACTION, floored at zero,
    // rather than AddSlotGaugeContribution's addition into CtxGaugeContribution.
    //
    //   ROW    the same indirect lookup as AddSlotGaugeContribution (this fighter's own slot looked up in
    //          ctx's CtxFighterSlots, the stored pointer dereferenced, its first ushort read).
    //          Reproduced as written for the same reason — see AddSlotGaugeContribution's header note.
    //   COLUMN local_18, 0..11, selected by +0x138 bits 0x40000/0x80000/0x80/0x20000 (8/9/10/11,
    //          tested BEFORE the state switch and skipping it outright) or by the state byte
    //          +0x16A (0x13/0x14 -> 0, 0x21 -> 1, 0x23..0x28 -> 6/7/5/3/4/2); any other state ->
    //          local_10 = 0.
    //   TABLE  base 0x80083968, stride 0xc (12) bytes per row — confirmed the same way as
    //          AddSlotGaugeContribution's table. No Ghidra symbol names it. Row 0 read back all zero, row 1
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
                FighterCombatArms.FUN_8004d32c(param_2, 0x16, param_1);
                break;
            case 3:
                FighterCombatArms.FUN_8004d9f4(param_2, 0x16, param_1);
                break;
            case 4:
                FighterCombatArms.FUN_8004d694(param_2, 0x19, param_1);
                break;
            case 5:
                FighterCombatArms.FUN_8004d694(param_2, 0x1a, param_1);
                break;
            case 6:
                FighterCombatArms.FUN_8004d694(param_2, 0x18, param_1);
                break;
        }

        if (cVar1 == 3)
        {
            FUN_8004a108(param_1);
        }
    }

    // GHIDRA: FUN_8004d32c @ 0x8004D32C (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/FighterCombatArms.cs, which holds the four
    // attack-resolution arms this file used to stub. Call sites below reach it by qualified
    // name; declaring the same address twice is what makes an empty stub beat a real body.

    // GHIDRA: FUN_8004d9f4 @ 0x8004D9F4 (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/FighterCombatArms.cs, which holds the four
    // attack-resolution arms this file used to stub. Call sites below reach it by qualified
    // name; declaring the same address twice is what makes an empty stub beat a real body.

    // GHIDRA: FUN_8004d694 @ 0x8004D694 (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/FighterCombatArms.cs, which holds the four
    // attack-resolution arms this file used to stub. Call sites below reach it by qualified
    // name; declaring the same address twice is what makes an empty stub beat a real body.

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

        SceneTransition.FUN_80026424(fighter);
    }

    // GHIDRA: FUN_80053970 @ 0x80053970 (VS.EXE)
    // CLOSED (was BLOCKED) -- 96 bytes, 7 call sites total, two of them in THIS class
    // (FighterSetState above, and CreateAttackEventTask below).
    //
    // THIS IS THE ONLY DECLARATION OF 0x80053970 IN THE PORT, and it took a fix to become so.
    // AnimCmdEffects.cs used to carry an INDEPENDENT empty private stub for the same address, and
    // the note here used to argue that was harmless because each copy was private to its own
    // class. It was not harmless: AnimCmd_EffSet's re-arm path called ITS class's stub, so that one
    // call site silently did nothing. The stub is gone and that call site now says
    // FighterCombat.FUN_80053970, which is why this member is `internal`.
    //
    // Ghidra's own decompilation closes the body without ambiguity:
    //
    //   *(ushort*)(param_1+4) = 0;
    //   if (param_2 >= 0) param_2 = param_2 + *(int*)param_1;
    //   uint table = *(uint*)((param_3 & 0xffff) * 4 + param_2);
    //   *(int*)(param_1+8) = (int)table;
    //   if (table < 0x80000000) *(int*)(param_1+8) = (int)(table + *(int*)param_1);
    //   *(ushort*)(param_1+6) = 0;
    //
    // param_1 is a task workspace -- FighterSetState's own fighter, or CreateAttackEventTask's own freshly
    // built attack-event record below. param_2 is a table BASE: either an absolute address (as
    // CreateAttackEventTask passes it, unaffected by the `param_2 >= 0` add since *param_1 would only be
    // folded in for a relative offset) or a value ADDED to param_1's own +0 word first when
    // non-negative (as FighterSetState passes it: *(int*)(*(int*)(fighter+0x148)+0x38), a table
    // pointer, always non-negative, so the add always fires there). param_3 selects a 4-byte entry
    // in that table by its own low 16 bits. Neither table has a name in this port.
    internal static void FUN_80053970(int param_1, int param_2, uint param_3)
    {
        PsxRam.WriteU16(param_1 + 4, 0);

        if (param_2 >= 0)
        {
            param_2 += PsxRam.ReadI32(param_1);
        }

        uint table = (uint)PsxRam.ReadI32((int)((param_3 & 0xffff) * 4) + param_2);
        PsxRam.WriteI32(param_1 + 8, unchecked((int)table));

        if (table < 0x80000000)
        {
            PsxRam.WriteI32(param_1 + 8, unchecked((int)(table + (uint)PsxRam.ReadI32(param_1))));
        }

        PsxRam.WriteU16(param_1 + 6, 0);
    }

    // GHIDRA: FUN_80026424 @ 0x80026424 (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/SceneTransition.cs (180 bytes). FighterSetState's
    // tail call below is qualified; an empty stub in the enclosing class silently beats a real body
    // elsewhere.
    // GHIDRA: FUN_8004d574 @ 0x8004D574 (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/FighterCombatArms.cs, which holds the four
    // attack-resolution arms this file used to stub. Call sites below reach it by qualified
    // name; declaring the same address twice is what makes an empty stub beat a real body.

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
    // (`void FighterCombatArms.FUN_8004d574(int, ushort)`) never reads — see that function's header note. So
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
    // meaning "blocked") — calls FighterCombatArms.FUN_8004d574(target, 0x16) and FUN_8004e580(target)
    // unconditionally, then, unless the target's own state (+0x16A) is 0x17, calls
    // AddSlotGaugeContribution(attacker, 1) — THE gauge-contribution seed this whole workflow exists to
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

        FighterCombatArms.FUN_8004d574(targetFighter, 0x16);
        FUN_8004e580(targetFighter);

        if (PsxRam.ReadU8(targetFighter + 0x16a) != 0x17)
        {
            AddSlotGaugeContribution(PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0x3c) + 8), 1);

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
    // AddSlotGaugeContribution(acting,0) — the OTHER of AddSlotGaugeContribution's two callers this file's own header
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
            AddSlotGaugeContribution(param_1, 0);
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

    // =====================================================================================
    // THE ATTACK-EVENT TASK -- the OTHER path into FUN_8004ee48 (see this file's own header note
    // on that function: its one caller was, until now, an unanalyzed stretch of code Ghidra
    // previewed as UndefinedFunction_800429a8). CreateAttackEventTask CREATES the task; UpdateAttackEventTask IS
    // its per-frame entry; FUN_8004ee48 above is what it eventually calls to actually apply the
    // attack. CreateAttackEventTask's own call site closes a piece of FUN_8004ee48's own open evidence too:
    // that function's header note left its own param_1+0x3c/+0x8 "attacker" chain as "reproduced
    // exactly as written" without saying what +0x3c holds; CreateAttackEventTask below stamps it with
    // `TaskSystem.g_CurrentTask` at CREATION time, i.e. whatever task was running the anim-stream
    // interpreter (AnimCmd_ChDanSet) that asked for this attack-event -- the attacking fighter's
    // own task, +8 of which FUN_8004ee48 already documents as "the ACTING fighter".
    //
    // proposedNames (see this task's own report, not applied to the code): CreateAttackEventTask as
    // something like CreateAttackEventTask, UpdateAttackEventTask as UpdateAttackEventTask -- the evidence
    // that closes this: CreateAttackEventTask is AnimCmd_ChDanSet's own "clear" arm's registration call
    // (opcode 40, `ch_dan_set`), builds a 0xC0-byte workspace from two RESOLVED TARGETS plus a
    // type halfword and a flag byte, and its entry (UpdateAttackEventTask) redraws that workspace every
    // frame (DrawSpriteGroup) until its own +0x78 sign bit is set, at which point it hands the whole
    // workspace to FUN_8004ee48 -- THE gauge-contribution seed this whole wave exists to reach --
    // then deletes its own task (TaskSystem.DeleteTask, list 0xb).
    // =====================================================================================

    // GHIDRA: UpdateAttackEventTask @ 0x800429A8 (VS.EXE)
    private const int UpdateAttackEventTask_Address = unchecked((int)0x800429A8);

    // GHIDRA: CreateAttackEventTask @ 0x80043598 (VS.EXE)
    // 312 bytes. One caller: AnimCmd_ChDanSet's "clear" arm in VS_EXE/AnimCmdEffects.cs
    // (`CreateAttackEventTask(iVar2, iVar8, (short)uVar6, uVar1 & 0xff);`, opcode 40's own two resolved
    // targets, word 2 sign-extended, and the flag byte).
    //
    // DUPLICATE DECLARATION, NOT FIXED HERE. AnimCmdEffects.cs's own call site above is
    // UNQUALIFIED, so it keeps binding to that file's own private stub of this same address --
    // this port's own duplicate-symbol defect, flagged in this slice's own report rather than
    // fixed, since AnimCmdEffects.cs is not this file's to edit (see this file's own top-of-file
    // ownership note). This IS the real body; AnimCmdEffects.cs's copy is dead code from its own
    // call site's perspective until that file's own stub is removed by whoever owns it.
    //
    // Two callees: FUN_80053330 (TaskSystem.CreateTask, already ported) and FUN_80053970 (this
    // file's own copy above, now closed). Creates the task (id 0, list 0xb, 0xC0-byte workspace,
    // entry UpdateAttackEventTask, inserted at g_TaskListTail[0xb]); on success, resolves the node's own +8
    // workspace and stamps it: param_1's three halfwords onto +0x40/+0x42/+0x44, param_2's first
    // two onto +0x48/+0x4a (its third, param_2[2], stays in `targetZ` and is written to +0x4c only
    // at the very end -- exactly where Ghidra's own decompilation places that store, kept literal
    // rather than moved up next to the other two), the CURRENT task (the caller's own task, i.e.
    // the ATTACKING fighter's, per this section's own header note) onto +0x3c, param_3 onto +0x7c,
    // a fixed 0x4000000 onto +0x78 (bit 26 -- NOT the sign bit UpdateAttackEventTask tests before calling
    // FUN_8004ee48, so that call never fires on the task's first frame), and a fixed table of
    // twelve intra-workspace pointers (+0xc, +0x80, +0x84, +0x88, +0x8c, +0x90, +0x94, +0x9c,
    // +0xa0, +0xa4, +0xa8) whose targets Ghidra's own arithmetic gives directly -- no further
    // meaning asserted for what each slot is FOR, only that each points where the original points
    // it. Finally calls FUN_80053970(workspace, &PTR_DAT_800217f0, param_4) to seed +8 from that
    // table, exactly as FUN_80053970's own header above documents.
    internal static void CreateAttackEventTask(int param_1, int param_2, short param_3, uint param_4)
    {
        TaskSystem.RegisterCallback(UpdateAttackEventTask_Address, UpdateAttackEventTask);

        int taskNode = TaskSystem.CreateTask(
            UpdateAttackEventTask_Address, 0, 0xb, 0xc0, 0, TaskSystem.g_TaskListTail[0xb]);

        if (taskNode != 0)
        {
            int workspace = PsxRam.ReadI32(taskNode + 8);

            PsxRam.WriteU16(workspace + 0x40, PsxRam.ReadU16(param_1));
            PsxRam.WriteU16(workspace + 0x42, PsxRam.ReadU16(param_1 + 2));
            PsxRam.WriteU16(workspace + 0x44, PsxRam.ReadU16(param_1 + 4));
            PsxRam.WriteU16(workspace + 0x48, PsxRam.ReadU16(param_2));
            PsxRam.WriteU16(workspace + 0x4a, PsxRam.ReadU16(param_2 + 2));

            int currentTask = TaskSystem.g_CurrentTask;
            ushort targetZ = PsxRam.ReadU16(param_2 + 4); // param_2[2] -- write deferred to +0x4c below, exactly as decompiled

            PsxRam.WriteU16(workspace + 0x7c, unchecked((ushort)param_3));
            PsxRam.WriteI32(workspace + 0x3c, currentTask);
            PsxRam.WriteI32(workspace + 0x78, 0x4000000);
            PsxRam.WriteI32(workspace + 0xc, workspace + 0x80);
            PsxRam.WriteI32(workspace + 0x80, workspace + 0x10);
            PsxRam.WriteI32(workspace + 0x84, workspace + 0x40);
            PsxRam.WriteI32(workspace + 0x88, workspace + 0x48);
            PsxRam.WriteI32(workspace + 0x8c, workspace + 0x60);
            PsxRam.WriteI32(workspace + 0x90, workspace + 0x78);
            PsxRam.WriteI32(workspace + 0x94, workspace + 0x7e);
            PsxRam.WriteI32(workspace + 0x9c, workspace + 0x70);
            PsxRam.WriteI32(workspace + 0xa0, workspace + 0x50);
            PsxRam.WriteI32(workspace + 0xa4, workspace + 0x58);
            PsxRam.WriteI32(workspace + 0xa8, workspace + 0x7c);
            PsxRam.WriteU16(workspace + 0x4c, targetZ);

            // GHIDRA: PTR_DAT_800217f0 @ 0x800217F0 (VS.EXE) -- AnimCmdEffects.cs already declares
            // a private const for this exact address (PTR_DAT_800217f0Address) for its own,
            // independent call into its own stub of FUN_80053970; that const is private to that
            // file's own class and not reachable from here. Only the ADDRESS is used below, as a
            // table base FUN_80053970 adds an offset onto -- never its contents -- so a second
            // numeric literal for the same immutable address duplicates no STATE the way a second
            // DAT_ storage cell would; there is nothing here for two declarations to disagree
            // about. Raw literal per rule 1, rather than a second same-named private const.
            FUN_80053970(workspace, unchecked((int)0x800217F0), param_4);
        }
    }

    // GHIDRA: UpdateAttackEventTask @ 0x800429A8 (VS.EXE)
    // 592 bytes, 7 callees. Never called directly anywhere in the image (Ghidra's own
    // cross-reference for this function shows exactly one incoming reference, and its type is
    // PARAM, not CALL: CreateAttackEventTask above takes its ADDRESS and hands it to CreateTask as the
    // task's entry point). This is the task's own per-frame body, dispatched purely through
    // TaskSystem's callback table -- the same mechanism FighterTask.UpdateFighter,
    // BattleScene.UpdateBattleScene and PrimitivePools.ResetPrimitivePoolCursors already use in
    // this port, always through TaskSystem.RegisterCallback's own zero-argument Action delegate.
    //
    // EVERY FRAME (while the anim VM is not globally paused, AnimVm.DAT_800b305a bit 0 clear):
    // sets the workspace's own +0x78 bit 1; computes an orientation byte at +0x7e from its own
    // +0x4c/+0x4a fields (FUN_80045b70, BLOCKED below -- an unnamed table lookup); calls
    // FUN_800539d0(workspace) -- this file's own already-ported keyframe-stream scanner; recomputes
    // +0x7e the SAME way a second time (kept literal, not de-duplicated, exactly as Ghidra's own
    // decompilation renders it twice); and folds the freshly recomputed +0x7e's own top two bits
    // into `param_1` for the draw call below.
    //
    // EVERY FRAME REGARDLESS: draws the workspace via DrawSpriteGroup (BLOCKED below -- a "primitive
    // pool" call PrimitivePools.cs's own header note already names in passing), passing +0x28, a
    // sign-extended position delta computed from +0x40 against the live camera-offset scratchpad
    // triple (Scratchpad._DAT_1f8000b4/_bc -- the same idiom BattleManager.cs's own
    // FUN_80057a7c already establishes for this exact `(int)(((uint)a-(uint)b)*0x10000)>>0x10`
    // sign-extend shape), +0x42 as a plain signed halfword, +0x74, `param_1`'s own top bits, and a
    // run of literal constants (a 0x200 scale pair, three 0x80 RGB-neutral bytes) that match a
    // sprite-draw call's usual shape.
    //
    // DEVIATION -- `param_1` ITSELF. Ghidra infers a first parameter from a0's value at function
    // entry, but per the incoming-reference evidence above, this function's only real "caller" is
    // the task dispatcher, which -- like every other task callback in this port -- invokes it with
    // NO real argument. a0 therefore holds whatever the PREVIOUS call left behind on the console:
    // genuine uninitialized register content, not a value any caller supplies. It is read in
    // exactly one place (the draw call's own 5th argument, `param_1 >> 0x10`, and only when the
    // anim VM is globally paused so the block that would otherwise overwrite it never runs), and
    // DrawSpriteGroup is itself a BLOCKED stub that discards every argument, so the value can never be
    // observed downstream. Modelled as a local starting at 0 -- the same DEVIATION posture this
    // file already takes for FUN_8004d0fc's own uninitialized local: a defined value only because
    // C# requires one, not a claim about what the console actually held there.
    //
    // ONLY WHEN THE VM IS NOT PAUSED, AFTERWARD: when +0x78's own SIGN BIT is set (never true on
    // the task's first frame -- CreateAttackEventTask above seeds +0x78 with 0x4000000, bit 26, not bit
    // 31), calls FUN_8004ee48(workspace) above -- THE call this whole wave exists to reach. A -1
    // result leaves the record as is; a 0 result jumps straight to LAB_80042bd0 (self-deletion,
    // below), skipping the reposition block AND the trailing +0x78 sign-bit clear; any OTHER
    // result rerolls the workspace's own +0x4a/+0x4c position pair through two rand() draws (each
    // reduced mod 0xc00 with a -0x600 bias, matching this file's own established
    // "int/short-truncating idiom stays literal" posture elsewhere) and calls FUN_800461fc
    // (BLOCKED below -- a GTE rotate/translate) on the result, then always clears +0x78's own sign
    // bit.
    //
    // FINALLY: unless the workspace's own +4 halfword is nonzero, falls into LAB_80042bd0 and
    // calls TaskSystem.DeleteTask(TaskSystem.g_CurrentTask, 0xb) -- the task deletes ITSELF, on
    // its own list. The `goto` is the original's own control flow and is kept literal: the
    // FUN_8004ee48-returned-0 path reaches this same deletion call WITHOUT running the +0x78
    // sign-bit clear or the +4 gate the fall-through path runs first.
    private static void UpdateAttackEventTask()
    {
        int param_1 = 0;

        int iVar4 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            PsxRam.WriteI32(iVar4 + 0x78, PsxRam.ReadI32(iVar4 + 0x78) | 2);

            byte bVar1 = EffectSystem.FUN_80045b70((short)PsxRam.ReadU16(iVar4 + 0x4c), (short)PsxRam.ReadU16(iVar4 + 0x4a));
            PsxRam.WriteU8(iVar4 + 0x7e, (byte)(bVar1 & 0x3f));

            FUN_800539d0(iVar4);

            byte uVar2 = EffectSystem.FUN_80045b70((short)PsxRam.ReadU16(iVar4 + 0x4c), (short)PsxRam.ReadU16(iVar4 + 0x4a));
            PsxRam.WriteU8(iVar4 + 0x7e, uVar2);

            param_1 = unchecked((PsxRam.ReadU8(iVar4 + 0x7e) & 0xc0) << 0x18);
        }

        SpriteDrawer.DrawSpriteGroup(
            PsxRam.ReadI32(iVar4 + 0x28),
            (short)((int)(((uint)PsxRam.ReadU16(iVar4 + 0x40) - (uint)Scratchpad._DAT_1f8000b4) * 0x10000) >> 0x10),
            (short)PsxRam.ReadU16(iVar4 + 0x42),
            (short)((int)(((uint)PsxRam.ReadU16(iVar4 + 0x44) - (uint)Scratchpad._DAT_1f8000bc) * 0x10000) >> 0x10),
            (ushort)(param_1 >> 0x10),
            0,
            0,
            0x200,
            0x200,
            PsxRam.ReadI32(iVar4 + 0x74),
            0,
            0,
            0,
            0,
            0x80,
            0x80,
            0x80,
            // GHIDRA: DAT_1f800128 @ 0x1F800128 (VS.EXE) -- the depth-projected table offset
            // FUN_800411B4 computes every frame, declared in VS_EXE_exe.cs and now `internal`.
            // It used to be passed as a literal 0 with a note explaining that the stub discarded
            // every argument anyway; the drawer is real now, and this parameter is the near-plane
            // cutoff it compares the bucket index against, so the real value goes in.
            VS_EXE_exe.DAT_1f800128);

        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            return;
        }

        if (PsxRam.ReadI32(iVar4 + 0x78) < 0)
        {
            int iVar3 = FUN_8004ee48(iVar4);

            if (iVar3 != -1)
            {
                if (iVar3 == 0)
                {
                    goto LAB_80042bd0;
                }

                int rand1 = Kernel.rand();
                PsxRam.WriteU16(iVar4 + 0x4a, unchecked((ushort)(
                    (short)PsxRam.ReadU16(iVar4 + 0x4a) - 0x600 + (short)rand1 + (short)(rand1 / 0xc00) * -0xc00)));

                int rand2 = Kernel.rand();
                PsxRam.WriteU16(iVar4 + 0x4a, (ushort)(PsxRam.ReadU16(iVar4 + 0x4a) & 0xfff));

                PsxRam.WriteU16(iVar4 + 0x4c, unchecked((ushort)(
                    (short)PsxRam.ReadU16(iVar4 + 0x4c) - 0x600 + (short)rand2 + (short)(rand2 / 0xc00) * -0xc00)));

                short local_18 = (short)PsxRam.ReadU16(iVar4 + 0x7c);
                PsxRam.WriteU16(iVar4 + 0x4c, (ushort)(PsxRam.ReadU16(iVar4 + 0x4c) & 0xfff));
                short local_16 = 0;
                short local_14 = 0;

                EffectSystem.FUN_800461fc(local_18, local_16, local_14, iVar4 + 0x48, iVar4 + 0x60);
            }

            PsxRam.WriteI32(iVar4 + 0x78, PsxRam.ReadI32(iVar4 + 0x78) & 0x7fffffff);
        }

        if (PsxRam.ReadU16(iVar4 + 4) != 0)
        {
            return;
        }

    LAB_80042bd0:
        TaskSystem.DeleteTask(TaskSystem.g_CurrentTask, 0xb);
    }

    // GHIDRA: FUN_800461fc @ 0x800461FC (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/EffectSystem.cs. The call sites in this file
    // reach it by qualified name; an empty stub in the enclosing class silently beats a real
    // body elsewhere, which is the defect check_function_addresses.py exists to catch.

    // ============================================================================================
    // WAVE 3 — FighterTask.cs step 9.4's third arm (FUN_8004cea0) and everything it reaches.
    // See this file's own top-of-file header note for the shape of the whole cluster.
    // ============================================================================================

    // GHIDRA: FUN_8004a910 @ 0x8004A910 (VS.EXE)
    // Already ported, as a real body, in FighterAction.cs (the concurrent agent that owns it in
    // this same wave) — cross-referenced here as FighterAction.FUN_8004a910 rather than
    // redeclared, per this project's duplicate-symbol rule: compare addresses, not names. This
    // cluster's own FUN_8004ca54 below is one of its two callers (state 0x17).
    //
    // GHIDRA: FUN_8004aa9c @ 0x8004AA9C (VS.EXE)
    // 624 bytes. Three callers, all in this file: FighterTask.cs's own FUN_8004b098 (step 9.4's
    // default arm, state 0x1c) and this cluster's own FUN_8004cb24 / FUN_8004cc64 below (both
    // also state 0x1c).
    //
    // Forces state 0x1c, applies the Ki-gauge decrement (FUN_8004a108) when +0x138 bit 0x80000 is
    // ALREADY set (read before this call sets bits 0x800000|0x40000 unconditionally right after),
    // then picks a knockback direction/timer pair. +0x220 (raw literal; no BattleState name covers
    // it) selects the pair: 0x2000 -> +0xca (raw literal) = +0x11e (raw literal, halfword) + 0x400,
    // cleared +0xc8/+0xcc; 0x1000 (the only value in 0..0x2000 this switch actually matches) ->
    // zeroed +0xc8/+0xca, +0xcc = 0xfc00; 0x4000 -> zeroed +0xc8/+0xca, +0xcc = 0x400; 0x8000 ->
    // +0xca = +0x11e minus 0x400, zeroed +0xc8/+0xcc; any other value in a band falls through with
    // no direction/timer write at all — reproduced exactly, not a gap. +0x228 and +0x162 (both raw
    // literals) are then stamped 5 and 0x32 regardless of which (if any) sub-case matched. The
    // bit-0x80000-CLEAR branch instead stamps +0x228 = 0xf and +0x162 = 0x32 and skips the whole
    // knockback switch outright.
    internal static void FUN_8004aa9c(int param_1)
    {
        FighterSetState(param_1, 0x1c);

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x80000) != 0)
        {
            FUN_8004a108(param_1);
        }

        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x840000);

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x80000) == 0)
        {
            PsxRam.WriteU8(param_1 + 0x228, 0xf); // raw literal; no BattleState name covers +0x228
            PsxRam.WriteU16(param_1 + 0x162, 0x32); // raw literal; no BattleState name covers +0x162
        }
        else
        {
            uint uVar1 = (uint)PsxRam.ReadI32(param_1 + 0x220); // raw literal; no BattleState name covers +0x220

            if (uVar1 == 0x2000)
            {
                PsxRam.WriteU16(param_1 + 0xc8, 0); // raw literal
                PsxRam.WriteU16(param_1 + 0xca, unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x11e) + 0x400))); // raw literal
                PsxRam.WriteU16(param_1 + 0xcc, 0); // raw literal
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
                PsxRam.WriteU16(param_1 + 0xca, 0);
                PsxRam.WriteU16(param_1 + 0xcc, 0x400);
            }
            else if (uVar1 == 0x8000)
            {
                PsxRam.WriteU16(param_1 + 0xc8, 0);
                PsxRam.WriteU16(param_1 + 0xca, unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x11e) - 0x400)));
                PsxRam.WriteU16(param_1 + 0xcc, 0);
            }

            PsxRam.WriteU8(param_1 + 0x228, 5);
            PsxRam.WriteU16(param_1 + 0x162, 0x32);
        }
    }

    // GHIDRA: FUN_8004ad0c @ 0x8004AD0C (VS.EXE)
    // Already ported, as a real body, in FighterAction.cs (the concurrent agent that owns it in
    // this same wave) — cross-referenced here as FighterAction.FUN_8004ad0c rather than
    // redeclared, per this project's duplicate-symbol rule: compare addresses, not names. This
    // cluster's own FUN_8004cb24 and FUN_8004cc64 below are two of its three callers.
    //
    // GHIDRA: FUN_8004c2a8 @ 0x8004C2A8 (VS.EXE)
    // 88 bytes. One caller, this cluster's own FUN_8004cb24 below. Forces state 0x1f and sets
    // +0x138 bit 0x8000.
    internal static void FUN_8004c2a8(int param_1)
    {
        FighterSetState(param_1, 0x1f);
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x8000);
    }

    // GHIDRA: FUN_8004c300 @ 0x8004C300 (VS.EXE)
    // 216 bytes. One caller, this cluster's own FUN_8004c3e0 below, which passes `param_1 + 0x1d0`
    // (raw literal; no BattleState name covers it) — a run of twenty 4-byte entries. Counts how
    // many of the twenty have bit 0x40 set and reports whether more than four do.
    internal static bool FUN_8004c300(int param_1)
    {
        int local_10 = 0;

        for (int local_c = 0; local_c < 0x14; local_c++)
        {
            if ((PsxRam.ReadI32(param_1 + local_c * 4) & 0x40) != 0)
            {
                local_10++;
            }
        }

        return 4 < local_10;
    }

    // GHIDRA: FUN_8004c9cc @ 0x8004C9CC (VS.EXE)
    // 136 bytes. One caller, FUN_8004cea0 (step 9.4's third arm — still a FighterTask.cs stub, not
    // this file's to touch), gated on +0x138 bit 0x200. Only when the fighter has never taken
    // damage (+4, raw literal, reads 0) but +6 (raw literal) is non-zero: clears +0x138 bit 0x100
    // and re-issues state 0 through FUN_8004a638.
    internal static void FUN_8004c9cc(int param_1)
    {
        if ((short)PsxRam.ReadU16(param_1 + 4) == 0 && (short)PsxRam.ReadU16(param_1 + 6) != 0)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffffeff));
            FUN_8004a638(param_1, 0);
        }
    }

    // GHIDRA: FUN_8004ca54 @ 0x8004CA54 (VS.EXE)
    // 208 bytes. One caller, FUN_8004cea0 (step 9.4's third arm; see FUN_8004c9cc's own header
    // note above). param_2 == 0x17 clears +0x138 bit 0x200 and forces state 0x17 through
    // FUN_8004a910; otherwise the same +4/+6 guard FUN_8004c9cc above uses clears the same bit and
    // re-issues state 0 through FUN_8004a638. The two arms are mutually exclusive — param_2==0x17
    // short-circuits the guard.
    internal static void FUN_8004ca54(int param_1, int param_2)
    {
        if (param_2 == 0x17)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffffdff));
            FighterAction.FUN_8004a910(param_1, 0x17);
        }
        else if ((short)PsxRam.ReadU16(param_1 + 4) == 0 && (short)PsxRam.ReadU16(param_1 + 6) != 0)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffffdff));
            FUN_8004a638(param_1, 0);
        }
    }

    // GHIDRA: FUN_8004cd84 @ 0x8004CD84 (VS.EXE)
    // 284 bytes. One caller, FUN_8004cea0's own final `else` arm (step 9.4's third arm; see
    // FUN_8004c9cc's own header note above). Counts down the halfword at +0x15e (raw literal; no
    // BattleState name covers it — the same field FUN_8004cb24 and FUN_8004cc64 below both read)
    // and, only on the frame it reaches (signed) zero, clears +0x138 bit 0x4000, re-issues state 0
    // through FUN_8004a638, and tops the Ki gauge up to 400 if it is currently under that floor.
    internal static void FUN_8004cd84(int param_1)
    {
        short sVar1 = (short)((short)PsxRam.ReadU16(param_1 + 0x15e) - 1);
        PsxRam.WriteU16(param_1 + 0x15e, unchecked((ushort)sVar1));

        if (sVar1 < 0)
        {
            PsxRam.WriteU16(param_1 + 0x15e, 0);
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffffbfff));
            FUN_8004a638(param_1, 0);

            int ctx = PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext);
            int slot = PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex);
            int kiAddr = ctx + slot * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge;

            if ((short)PsxRam.ReadU16(kiAddr) < 400)
            {
                PsxRam.WriteU16(kiAddr, 400);
            }
        }
    }

    // GHIDRA: FUN_8004cc64 @ 0x8004CC64 (VS.EXE)
    // 288 bytes. One caller, FUN_8004cea0 (step 9.4's third arm; see FUN_8004c9cc's own header
    // note above). param_2 == 0x1c clears +0x138 bits 0x3800, sets bit 0x80000, and forces the
    // knockback through FUN_8004aa9c followed by the slot-broadcast in FUN_8004c7fc below.
    // Otherwise the same +4/+6 guard the rest of this cluster uses clears the same 0x3800 bits,
    // then picks between FUN_8004a638 (state 0, when the countdown at +0x15e — shared with
    // FUN_8004cd84 above — has already reached zero) and FUN_8004ad0c (still counting down).
    internal static void FUN_8004cc64(int param_1, int param_2)
    {
        if (param_2 == 0x1c)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffffc7ff));
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x80000);
            FUN_8004aa9c(param_1);
            FUN_8004c7fc(param_1);
        }
        else if ((short)PsxRam.ReadU16(param_1 + 4) == 0 && (short)PsxRam.ReadU16(param_1 + 6) != 0)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xffffc7ff));

            if ((short)PsxRam.ReadU16(param_1 + 0x15e) == 0)
            {
                FUN_8004a638(param_1, 0);
            }
            else
            {
                FighterAction.FUN_8004ad0c(param_1);
            }
        }
    }

    // GHIDRA: FUN_8004cb24 @ 0x8004CB24 (VS.EXE)
    // 320 bytes. One caller, FUN_8004cea0 (step 9.4's third arm; see FUN_8004c9cc's own header
    // note above). Same shape as FUN_8004cc64 above — param_2==0x1c clears +0x138 bit 0x400 (not
    // 0x3800), sets bit 0x80000, and forces the knockback through FUN_8004aa9c, but WITHOUT the
    // FUN_8004c7fc slot broadcast that function's own 0x1c arm makes. Otherwise the same +4/+6
    // guard clears bit 0x400 and, on a live countdown (+0x15e != 0, the same field FUN_8004cd84
    // and FUN_8004cc64 both count down), checks a second countdown at +0x116 (raw literal; inside
    // FighterZeroedFrom114's zeroed range) to choose between FUN_8004c2a8 and FUN_8004a638; a
    // finished countdown (+0x15e == 0) instead calls FUN_8004ad0c.
    internal static void FUN_8004cb24(int param_1, int param_2)
    {
        if (param_2 == 0x1c)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffffbff));
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x80000);
            FUN_8004aa9c(param_1);
        }
        else if ((short)PsxRam.ReadU16(param_1 + 4) == 0 && (short)PsxRam.ReadU16(param_1 + 6) != 0)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfffffbff));

            if ((short)PsxRam.ReadU16(param_1 + 0x15e) == 0)
            {
                if ((short)PsxRam.ReadU16(param_1 + 0x116) == 0)
                {
                    FUN_8004c2a8(param_1);
                }
                else
                {
                    FUN_8004a638(param_1, 0);
                }
            }
            else
            {
                FighterAction.FUN_8004ad0c(param_1);
            }
        }
    }

    // GHIDRA: FUN_8004c7fc @ 0x8004C7FC (VS.EXE)
    // 464 bytes. One caller, this cluster's own FUN_8004cc64 above (its param_2==0x1c arm, right
    // after FUN_8004aa9c). One of the two functions anywhere in this image that clear fighter
    // +0x134 bits 31 and 29 together (0x5fffffff — FUN_8004de90 above is the other).
    //
    // Scans all twelve CtxFighterSlots for a fighter OTHER than this one (by slot index), with its
    // own per-slot record (CtxSlotRecords) bit 0x200 set, whose task node's own FighterTaskNode
    // field (+0xAC) matches THIS fighter's own FighterTaskNodeAlias field (+0x104) — the same node
    // stored at two different offsets BattleState.cs already documents — and whose own +0x138 bit
    // 0x10 is set. For every slot that matches: clears +0x138 mask 0xffefffaf (bits 0x100000,
    // 0x40 and 0x10) and +0x134 mask 0x5fffffff on THAT fighter, then re-issues its state 0
    // through FUN_8004a638. This never touches param_1's own +0x134/+0x138 — every write lands on
    // the OTHER fighter(s) the scan finds.
    internal static void FUN_8004c7fc(int param_1)
    {
        int ctx = PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext);

        for (uint local_c = 0; (int)local_c < BattleState.CtxFighterSlotCount; local_c++)
        {
            int slotFighterPtr = PsxRam.ReadI32(ctx + BattleState.CtxFighterSlots + (int)local_c * 4);

            if (PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) != local_c
                && slotFighterPtr != 0
                && (PsxRam.ReadU16(ctx + BattleState.CtxSlotRecords + (int)local_c * BattleState.CtxSlotRecordStride) & 0x200) != 0)
            {
                int iVar1 = PsxRam.ReadI32(slotFighterPtr + 8);

                if (PsxRam.ReadI32(iVar1 + BattleState.FighterTaskNode) == PsxRam.ReadI32(param_1 + BattleState.FighterTaskNodeAlias)
                    && (PsxRam.ReadI32(iVar1 + 0x138) & 0x10) != 0)
                {
                    PsxRam.WriteI32(iVar1 + 0x138, PsxRam.ReadI32(iVar1 + 0x138) & unchecked((int)0xffefffaf));
                    PsxRam.WriteI32(iVar1 + 0x134, PsxRam.ReadI32(iVar1 + 0x134) & 0x5fffffff);
                    FUN_8004a638(iVar1, 0);
                }
            }
        }
    }

    // GHIDRA: FUN_8004c3e0 @ 0x8004C3E0 (VS.EXE)
    // 1052 bytes, the largest of the third arm's callees. One caller, FUN_8004cea0 (step 9.4's
    // third arm — still a FighterTask.cs stub, not this file's to touch), called only when the
    // +0x138 bits-0x7C00 gate is set.
    //
    // Returns false immediately if +0x138 bits 0x7C00 are ALL clear, or if FUN_8004c300 (on
    // +0x1d0, raw literal) reports false. Otherwise scans the acting fighter's OWN TEAM half of
    // CtxFighterSlots (slots 0..5 or 6..11, picked by whether its own FighterSlotIndex is below 6)
    // for the BEST qualifying teammate: per-slot record bit 0x200 set, slot occupied, the slot's
    // own task node NOT the currently running task (TaskSystem.g_CurrentTask — DAT_8008d16c's own
    // bare/undereferenced form; see FUN_8004a638's own header note for the dereferenced form of
    // the same global), state byte (+0x16A) not 0x22 ('"'), and +0x138 bits 0x7F00 clear (the SAME
    // mask step 9.4's own dispatch tests, on the CANDIDATE rather than the acting fighter). Among
    // every slot that passes, the one with the highest Ki gauge (CtxKiGauge) wins; ties keep the
    // first one found.
    //
    // With no qualifying teammate, returns false. With one: links that teammate's own
    // FighterTaskNode (+0xAC) to the acting fighter's, sets its own +0x138 bit 0x20000, then rolls
    // a random state through FUN_8004a97c — 0x26 or 0x28 when the acting fighter's own +0x116
    // halfword (raw literal; inside FighterZeroedFrom114's zeroed range) is zero, 0x27 or 0x28
    // otherwise, the choice within each pair made by rand() bit 0 — and returns true.
    internal static bool FUN_8004c3e0(int param_1)
    {
        bool uVar1;
        int local_24 = -1;

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x7c00) == 0)
        {
            uVar1 = false;
        }
        else if (!FUN_8004c300(param_1 + 0x1d0))
        {
            uVar1 = false;
        }
        else
        {
            int ctx = PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext);
            int local_28 = PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) < 6 ? 0 : 6;
            int end = local_28 + 6;

            for (; local_28 < end; local_28++)
            {
                int slotFighterPtr = PsxRam.ReadI32(ctx + BattleState.CtxFighterSlots + local_28 * 4);

                if ((PsxRam.ReadU16(ctx + BattleState.CtxSlotRecords + local_28 * BattleState.CtxSlotRecordStride) & 0x200) != 0
                    && slotFighterPtr != 0
                    && slotFighterPtr != TaskSystem.g_CurrentTask)
                {
                    int iVar3 = PsxRam.ReadI32(slotFighterPtr + 8);

                    if (PsxRam.ReadU8(iVar3 + 0x16a) != 0x22
                        && (PsxRam.ReadI32(iVar3 + 0x138) & 0x7f00) == 0)
                    {
                        if (local_24 == -1)
                        {
                            local_24 = local_28;
                        }
                        else if ((short)PsxRam.ReadU16(ctx + local_24 * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge)
                                 < (short)PsxRam.ReadU16(ctx + local_28 * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge))
                        {
                            local_24 = local_28;
                        }
                    }
                }
            }

            if (local_24 == -1)
            {
                uVar1 = false;
            }
            else
            {
                int chosenFighterPtr = PsxRam.ReadI32(ctx + BattleState.CtxFighterSlots + local_24 * 4);
                int iVar2 = PsxRam.ReadI32(chosenFighterPtr + 8);

                PsxRam.WriteI32(iVar2 + BattleState.FighterTaskNode, PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode));
                PsxRam.WriteI32(iVar2 + 0x138, PsxRam.ReadI32(iVar2 + 0x138) | 0x20000);

                int ownWorkspace = PsxRam.ReadI32(PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode) + 8);
                int local_1c;
                uint rnd;

                if ((short)PsxRam.ReadU16(ownWorkspace + 0x116) == 0) // raw literal; inside FighterZeroedFrom114's zeroed range
                {
                    rnd = (uint)Kernel.rand();
                    local_1c = (rnd & 1) == 0 ? 0x26 : 0x28;
                }
                else
                {
                    rnd = (uint)Kernel.rand();
                    local_1c = (rnd & 1) == 0 ? 0x27 : 0x28;
                }

                FUN_8004a97c(iVar2, local_1c);
                uVar1 = true;
            }
        }

        return uVar1;
    }
}
