using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE FIGHTER'S DRAW-AND-REPORT TAIL — three of step 9.8's five callees, plus the command
// recogniser FUN_8004b9cc reaches when the fighter is flagged for a slot lookup.
//
// WHAT THIS FILE IS. FighterTask.cs's UpdateFighter runs a numbered phase ladder; step 9.8 is the
// "+0x138 bit 27 clear" quintet, run from FIVE different call sites in that file
// (FUN_800501b8 / FUN_80050514 / FUN_80050658 / FUN_8005070c / FUN_80050824) and once more from
// the main body at 0x8005112c..0x80051170. The quintet is, in evaluation order:
//
//   FUN_80047740   0x80047740   ported in FighterTask.cs (the three bytes at +0x150..+0x152)
//   FUN_800477ec   0x800477EC   HERE  — the fighter's own sprite/primitive submission
//   FUN_80047a24   0x80047A24   HERE  — the fighter's shadow, a second submission at y = 0
//   FUN_80047b10   0x80047B10   ported in FighterTask.cs (the texture reload)
//   FUN_8004fd24   0x8004FD24   HERE  — the per-frame report to FUN_800340a8
//
// FUN_8004b68c @ 0x8004B68C is not part of that quintet; it is step 9.3's neighbour, the fourth
// recogniser path FUN_8004b9cc @ 0x8004B9CC takes when the fighter's +0x138 has either of bits
// 0x30000000 set. It is here rather than in FighterInput.cs only because it was handed to this
// slice; FighterAction.cs already carries a private BLOCKED stub for the SAME address, which is
// the duplicate this file's arrival creates and which the wiring pass has to delete. See the
// note on the function itself.
//
// THE TWO SUBMISSION CALLS SHARE ONE CALLEE. FUN_800477ec and FUN_80047a24 both end in a call to
// FUN_80052db4 @ 0x80052DB4, the "primitive pool" drawer VS_EXE/PrimitivePools.cs names in passing
// and FighterCombat.cs already carries as a precise 18-parameter no-op stub. Its real prototype,
// from Ghidra's own callee-side analysis, is
//
//   int FUN_80052db4(int *param_1, short param_2, short param_3, short param_4, ushort param_5,
//                    undefined2 param_6, undefined2 param_7, int param_8, int param_9,
//                    int param_10, short param_11, short param_12, char param_13, char param_14,
//                    undefined1 param_15, undefined1 param_16, undefined1 param_17, int param_18);
//
// EIGHTEEN parameters, and both call sites were checked instruction by instruction against that
// count rather than against Ghidra's per-call-site guess — Ghidra prints NINETEEN arguments at
// FUN_800477ec's call site, and that nineteenth is wrong. See FUN_800477ec's own note.
//
// WHAT param_2 IS. Every one of the five call sites passes `iVar3 + 0x114` — the fighter's own
// position triple, three signed halfwords at +0x114 / +0x116 / +0x118, the same triple
// FighterTask.cs's phase 2 clamps against FighterBoundsMin / FighterBoundsMax. Ghidra types it
// `ushort *param_2`, so `*param_2` is +0x114, `param_2[1]` is +0x116 and `param_2[2]` is +0x118.
// BattleState names the base of that region FighterZeroedFrom114 but names no member of the
// triple, and param_2 arrives here as an already-computed address anyway, so the three members are
// reached as raw +0 / +2 / +4 from the passed pointer, exactly as the original reaches them.
//
// THE SCRATCHPAD PAIR IS READ SIXTEEN BITS WIDE. Both submissions subtract 0x1F8000B4 from the x
// component and 0x1F8000BC from the z component. Scratchpad._DAT_1f8000b4 / _DAT_1f8000bc are
// declared `int` there because FileIo.SetupGeometry writes them with a full word (`sw`), but every
// read in THIS file is `lhu` — 0x800478E8 `lhu v1,0xb4(v1)` and 0x80047918 `lhu a0,0xbc(a0)` in
// FUN_800477ec, 0x80047A50 and 0x80047A74 in FUN_80047a24. The low halfword is what the hardware
// takes, so each read is masked to `ushort` here. That is a load-width fact read off the
// instructions, not an inference from the decompiler's `(uint)` cast.
internal static class FighterMotion
{
    // GHIDRA: FUN_800477ec @ 0x800477EC (VS.EXE)
    // 568 bytes, 0x800477EC..0x80047A23. Six callers: the five step-9.8 call sites in
    // FighterTask.cs's own early-out functions plus the main body's 0x8005112c. Ported in full.
    //
    // ONE CALL, EIGHTEEN ARGUMENTS, AND ONE DEAD STORE. Ghidra's decompilation of this function
    // prints NINETEEN arguments to FUN_80052db4, the last being `*(uint *)(param_1 + 0x134) & 0x1f`.
    // That is a decompiler artefact and it was checked against the raw instructions rather than
    // believed. The argument marshalling runs 0x80047934..0x800479F4 and writes exactly fourteen
    // stack slots, sp+0x10 through sp+0x44, plus a0..a3 — eighteen arguments, the last being
    // DAT_1f800128 at sp+0x44 (0x800479EC `sw a0,0x44(sp)`), with NOTHING stored at sp+0x48. The
    // `& 0x1f` value is instead written back into the caller's OWN stack local at s8+0x48
    // (0x8004783C `andi v1,v0,0x1f` / 0x80047840 `sw v1,0x48(s8)`) and never read again: that local
    // was last read at 0x80047820 to build the +0x4c argument. It is a DEAD STORE in the original.
    // It is reproduced below as a dead assignment rather than dropped, per rule 12 — the original's
    // pointless work is the original's, not a bug to fix. The sibling FUN_80047a24 below, whose
    // marshalling is straight-line and unambiguous, independently confirms the eighteen-slot shape:
    // its last store is also sp+0x44 = DAT_1f800128.
    //
    // THE FIVE STACK LOCALS. The original builds five values into its own frame before the call
    // and reads each one back at a specific WIDTH, which is what fixes the C# casts below:
    //
    //   s8+0x4c   `sh`  written, `lh`  read   ((*(param_1+0x134) & 0xff) << 8) & 0xC000, i.e.
    //                                          Ghidra's `((ushort)*(uint*)(param_1+0x134) & 0xc0) << 8`
    //   s8+0x4e   `sh`  written, `lhu` read   (+0x156 >> 6) + ((+0x158 >> 8) & 0xffff) * 0x10
    //   s8+0x50   `sh`  written, `lbu` read   (+0x156 & 0x3f) << 2          — Ghidra's local_18
    //   s8+0x52   `sh`  written, `lbu` read   +0x158 & 0xff                 — Ghidra's local_16
    //   s8+0x54 / s8+0x58  `sw` written, `lw` read   both the constant 0x249
    //
    // The two `sh`-written / `lbu`-read locals are why Ghidra types local_18 and local_16
    // `undefined1`: only the low byte of each halfword ever reaches the callee. Both values fit in
    // a byte anyway (0x3f << 2 = 0xfc, and 0xff), so the truncation is visible but never bites.
    //
    // +0x156 AND +0x158 ARE RE-READ THREE TIMES. The original reloads param_1 and re-issues the
    // `lhu` for each of the three locals it derives from them (0x80047850, 0x80047864, 0x80047890,
    // 0x800478B4). They are plain reads with no side effect, so folding them would be invisible —
    // they are kept separate anyway, because collapsing repeated loads is the kind of tidying rule
    // 1 and rule 7 forbid.
    //
    // BLOCKED: the return value. The original stores FUN_80052db4's result into +0x13c
    // (0x80047A00 `lw v1,0x68(s8)` / 0x80047A08 `sw v0,0x13c(v1)`) — a primitive/handle the drawer
    // hands back. FighterCombat.FUN_80052db4 is a no-op stub that returns `void`, so there is no
    // handle to store and 0 is written instead. That is not a fabricated value: the workspace is
    // zeroed from +0x114 up by FUN_800512cc, so +0x13c is already 0 on every frame this port has
    // ever run, and the one reader of the field — FighterTask.FUN_80047b10's `0 < +0x13c` gate —
    // therefore behaves exactly as it does today. When FUN_80052db4 is really ported it must
    // return its int and this store must carry it.
    internal static void FUN_800477ec(int param_1, int param_2)
    {
        // s8+0x48: (*(uint *)(param_1 + 0x134)) & 0xff, then read back as a halfword to build the
        // +0x4c argument. 0x800477FC..0x80047820.
        int local_48 = PsxRam.ReadI32(param_1 + 0x134) & 0xff;

        // s8+0x4c: `sll v1,v0,0x8` then `and v0,v0,0xffffc000`, stored with `sh` and read back with
        // `lh` — a SIGNED halfword, which is why the value reaches the callee sign-extended.
        short local_4c = (short)(((local_48 & 0xffff) << 8) & unchecked((int)0xffffc000));

        // DEAD STORE, reproduced. 0x8004783C `andi v1,v0,0x1f` / 0x80047840 `sw v1,0x48(s8)`.
        // Nothing reads s8+0x48 after this point. Ghidra mistakes it for a nineteenth call
        // argument; the instruction stream shows it is not. See this function's header note.
        local_48 = local_48 & 0x1f;
        _ = local_48;

        // s8+0x4e: 0x80047850..0x80047888, read back with `lhu` at 0x80047980.
        short local_4e = (short)((PsxRam.ReadU16(param_1 + 0x156) >> 6)
            + ((PsxRam.ReadU16(param_1 + 0x158) >> 8) & 0xffff) * 0x10);

        // s8+0x50 — Ghidra's local_18. 0x80047890..0x800478AC, read back with `lbu` at 0x8004798C.
        short local_50 = (short)((PsxRam.ReadU16(param_1 + 0x156) & 0x3f) << 2);

        // s8+0x52 — Ghidra's local_16. 0x800478B4..0x800478C8, read back with `lbu` at 0x80047998.
        short local_52 = (short)(PsxRam.ReadU16(param_1 + 0x158) & 0xff);

        // s8+0x58 then s8+0x54, in that order, both 0x249. 0x800478CC..0x800478D8.
        int local_58 = 0x249;
        int local_54 = 0x249;

        // BLOCKED: FighterCombat.FUN_80052db4 is the 18-parameter no-op stub for 0x80052DB4 and it
        // returns void. It is called qualified rather than redeclared here — one Ghidra address,
        // one C# body — and it has to become `internal` (it is `private` today) for this to build.
        FighterCombat.FUN_80052db4(
            PsxRam.ReadI32(param_1 + 0x98),
            (short)(PsxRam.ReadU16(param_2) - (ushort)Scratchpad._DAT_1f8000b4),
            (short)PsxRam.ReadU16(param_2 + 2),
            (short)(PsxRam.ReadU16(param_2 + 4) - (ushort)Scratchpad._DAT_1f8000bc),
            local_4c,
            0,
            0,
            local_54,
            local_58,
            PsxRam.ReadI32(param_1 + 0x140),
            PsxRam.ReadU16(param_1 + 0x15a),
            (ushort)local_4e,
            (byte)local_50,
            (byte)local_52,
            PsxRam.ReadU8(param_1 + 0x150),
            PsxRam.ReadU8(param_1 + 0x151),
            PsxRam.ReadU8(param_1 + 0x152),
            VS_EXE_exe.DAT_1f800128);

        // See the BLOCKED paragraph in this function's header note for why the stored value is 0.
        PsxRam.WriteI32(param_1 + 0x13c, 0);
    }

    // GHIDRA: FUN_80047a24 @ 0x80047A24 (VS.EXE)
    // 236 bytes, 0x80047A24..0x80047B0F. The same six callers as FUN_800477ec, always immediately
    // after it and with the same two arguments. Ported in full.
    //
    // Straight-line: no local, no branch, one call, no return value used. The whole body is the
    // marshalling of eighteen arguments to FUN_80052db4 — verified store by store,
    // 0x80047A88 `sw a0,0x10(sp)` through 0x80047AE0 `sw a0,0x44(sp)`, then a0..a3 at
    // 0x80047AE4..0x80047AEC. This is the call site that fixes the callee's real arity at eighteen.
    //
    // WHAT IT DRAWS. param_1 is not the fighter here but the address constant 0x8007F7B8
    // (0x80047AE4 `lui a0,0x8008` / 0x80047AE8 `addiu a0,a0,-0x848`), which Ghidra prints as
    // `&DAT_8007f7b8` — the ADDRESS as a constant, not a small number. Nothing in this port
    // declares that address yet, so it is passed as the raw literal. The y argument is a hard 0
    // while x and z are the fighter's own, and the colour/scale block is all zeroes with a 0x800
    // scale pair and a negative 10 at param_10 — a second submission pinned to the ground plane
    // under the fighter. The name that suggests is not written into the code: the evidence closes
    // the ARGUMENTS, not the meaning.
    //
    // FUN_80052db4 returns an int here too and this caller discards it (no `sw` after the `jal`),
    // so the void stub costs this function nothing.
    internal static void FUN_80047a24(int param_1, int param_2)
    {
        // param_1 is unread by the original: 0x80047A30 stores it to s8+0x50 and nothing loads it
        // back. Every caller still passes the fighter. Kept in the signature for that reason.
        _ = param_1;

        FighterCombat.FUN_80052db4(
            unchecked((int)0x8007F7B8),
            (short)(PsxRam.ReadU16(param_2) - (ushort)Scratchpad._DAT_1f8000b4),
            0,
            (short)(PsxRam.ReadU16(param_2 + 4) - (ushort)Scratchpad._DAT_1f8000bc),
            0xb9c,
            0,
            0,
            0x800,
            0x800,
            -10,
            32000,
            0,
            0,
            0,
            0,
            0,
            0,
            VS_EXE_exe.DAT_1f800128);
    }

    // GHIDRA: FUN_8004fd24 @ 0x8004FD24 (VS.EXE)
    // 712 bytes, 0x8004FD24..0x8004FFEB. The same six callers, last of step 9.8's five. Ported in
    // full — the body is small; the 712 bytes are the eight-clause condition chain below, which the
    // compiler expanded into one compare-and-branch pair per clause with no sharing.
    //
    // TWO FLAGS AND ONE REPORT. When +0x138 bit 18 (0x40000) is set it raises local_10, tests the
    // fighter's own state byte +0x16a against eight values while +4 is 1 and raises local_c if any
    // matches, and applies the Ki-gauge decrement unless the anim VM is suspended. Then, on EVERY
    // path including the one where bit 18 was clear, it calls FUN_800340a8 with both flags.
    //
    // THE EIGHT STATES are 0x13, 0x14, 0x25, 0x23, 0x24, 0x26, 0x27 and 0x28, in that order in the
    // original's `||` chain, each paired with its own re-read of the +4 halfword. FighterInput.cs's
    // own header note lists 0x13/0x14, 0x23/0x24/0x25 and 0x26/0x27/0x28 as command opcodes step 9.4
    // routes on, so the set is the attack states; the chain is left as eight literal comparisons in
    // the original's order rather than collapsed into a range or a table.
    //
    // +4 IS A SIGNED HALFWORD. Ghidra prints `*(short *)(param_1 + 4) == 1`; FighterTask.cs and
    // FighterAction.cs already read that same field as `(short)PsxRam.ReadU16(param_1 + 4)`, and
    // the same form is used here.
    //
    // FUN_8004a108 IS CALLED WITH TWO ARGUMENTS AND READS ONE. 0x8004FF58 loads +0x16a into v1 and
    // 0x8004FF5C moves it into a1 before the `jal`, but FighterCombat.FUN_8004a108's own header
    // note records that no path through that body reads a1 — Ghidra's callee-side signature is
    // one-parameter and that is settled analysis. The second argument's LOAD is a plain read with
    // no side effect, so it is simply not issued here; nothing is dropped.
    //
    // THE FIRST ARGUMENT IS A HALFWORD OFF THE CURRENT TASK NODE. Ghidra prints `*DAT_8008d16c`,
    // which reads as if DAT_8008d16c were a pointer variable. The instructions settle it:
    // 0x8004FF6C `lui v1,0x8009` / 0x8004FF70 `lw v1,-0x2e94(v1)` loads the WORD at 0x8008D16C —
    // TaskSystem.g_CurrentTask, the running task node — and 0x8004FF78 `lhu v0,0x0(v1)` then reads
    // an UNSIGNED HALFWORD at that node's own offset 0. So it is ReadU16(g_CurrentTask), not
    // ReadI32, and not the node's +8 context pointer that AnimCmdTransform.cs and AnimCmdSound.cs
    // read from the same global.
    internal static void FUN_8004fd24(int param_1, int param_2)
    {
        int local_10 = 0;
        int local_c = 0;

        if ((PsxRam.ReadI32(param_1 + 0x138) & 0x40000) != 0)
        {
            local_10 = 1;

            if ((PsxRam.ReadU8(param_1 + 0x16a) == 0x13 && (short)PsxRam.ReadU16(param_1 + 4) == 1)
                || (PsxRam.ReadU8(param_1 + 0x16a) == 0x14 && (short)PsxRam.ReadU16(param_1 + 4) == 1)
                || (PsxRam.ReadU8(param_1 + 0x16a) == 0x25 && (short)PsxRam.ReadU16(param_1 + 4) == 1)
                || (PsxRam.ReadU8(param_1 + 0x16a) == 0x23 && (short)PsxRam.ReadU16(param_1 + 4) == 1)
                || (PsxRam.ReadU8(param_1 + 0x16a) == 0x24 && (short)PsxRam.ReadU16(param_1 + 4) == 1)
                || (PsxRam.ReadU8(param_1 + 0x16a) == 0x26 && (short)PsxRam.ReadU16(param_1 + 4) == 1)
                || (PsxRam.ReadU8(param_1 + 0x16a) == 0x27 && (short)PsxRam.ReadU16(param_1 + 4) == 1)
                || (PsxRam.ReadU8(param_1 + 0x16a) == 0x28 && (short)PsxRam.ReadU16(param_1 + 4) == 1))
            {
                local_c = 1;
            }

            if ((AnimVm.DAT_800b305a & 1) == 0)
            {
                FighterCombat.FUN_8004a108(param_1);
            }
        }

        // BLOCKED: FUN_800340a8 @ 0x800340A8 is not ported anywhere — see the stub below.
        FUN_800340a8(
            PsxRam.ReadU16(TaskSystem.g_CurrentTask),
            PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex),
            param_2,
            param_1 + 0x11c,
            PsxRam.ReadU8(param_1 + 0x16a),
            local_10,
            local_c);
    }

    // GHIDRA: FUN_800340a8 @ 0x800340A8 (VS.EXE)
    // BLOCKED: 1764 bytes, 0x800340A8..0x8003478B, out of this slice. Eleven callees of its own
    // (rand, SquareRoot0, ratan2 and eight more FUN_8003xxxx functions, none in this port), in an
    // address range nothing this port owns has entered yet. FighterTask.cs's own note on
    // FUN_8004fd24 already named this function as the exact one that would unblock it.
    //
    // Seven arguments, all verified against the marshalling at 0x8004FF6C..0x8004FFCC:
    //   param_1  ushort  the halfword at TaskSystem.g_CurrentTask + 0        (`lhu`)
    //   param_2  byte    the fighter's BattleState.FighterSlotIndex (+0x173) (`lbu`)
    //   param_3  int     FUN_8004fd24's own param_2, i.e. fighter + 0x114 — the position triple
    //   param_4  int     fighter + 0x11c, a second address inside the same zeroed region
    //   param_5  byte    the fighter's state byte +0x16a                     (`lbu`)
    //   param_6  int     local_10, the +0x138 bit-18 flag
    //   param_7  int     local_c, the attack-state flag
    // Kept as a precise no-op so the caller's own argument computation — real PsxRam reads with no
    // side effects of their own — still runs exactly where the original runs it.
    private static void FUN_800340a8(int param_1, int param_2, int param_3, int param_4,
        int param_5, int param_6, int param_7)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
        _ = param_4;
        _ = param_5;
        _ = param_6;
        _ = param_7;
    }

    // GHIDRA: FUN_8004b68c @ 0x8004B68C (VS.EXE)
    // 484 bytes, 0x8004B68C..0x8004B89F. One caller, FUN_8004b9cc @ 0x8004B9CC (ported in
    // FighterAction.cs): `else { local_10 = FUN_8004b68c(param_1); }`, the arm taken when the
    // fighter's +0x138 has either of bits 0x30000000 set. Ported in full.
    //
    // DUPLICATE ADDRESS — FighterAction.cs ALREADY DECLARES THIS ONE. That file carries a
    // `private static int FUN_8004b68c(int param_1) { return -1; }` BLOCKED stub for the same
    // 0x8004B68C, written when this function was out of every slice. This file was told not to edit
    // it, so the stub is still there and C# will bind FUN_8004b9cc's unqualified call to it — the
    // exact "empty stub silently beats a real body" defect this repository keeps shipping. The
    // wiring pass MUST delete FighterAction.cs's stub and qualify its call site to
    // FighterMotion.FUN_8004b68c. Reported upward; not fixed here.
    //
    // WHAT IT DOES. Scans the fighter's pad EDGE ring (+0x1D0, BattleState.FighterPadEdgeHistory)
    // for the first of its first four words with bit 0x20 set; if none of the four has it, returns
    // the -1 "no command" sentinel outright. From that index it forms TWO addresses at the SAME
    // index — one into the edge ring at +0x1D0, one into the state ring at +0x180
    // (BattleState.FighterPadStateHistory) — and runs six recognisers in a fixed order, returning
    // the first one's opcode:
    //
    //   MatchClockwiseFaceSweep         0x800487DC   -> 0x23
    //   MatchCounterClockwiseFaceSweep  0x800489FC   -> 0x24
    //   FUN_8004b5ac                    0x8004B5AC   -> 0x25
    //   MatchFaceButton1000Within3      0x80047FB8   -> 0x26
    //   MatchFaceButton4000Within3      0x8004808C   -> 0x27
    //   FUN_8004b4a4                    0x8004B4A4   -> 0x28
    //
    // and -1 when all six fail. That is the same 0x23..0x28 block FighterInput.cs's own header note
    // describes for the other two decode paths, produced here from a ring index that is NOT
    // necessarily 0 — which is the whole difference between this path and DecodeCommandFlagsClear.
    // All six live in files that already own their address and are called qualified.
    //
    // FUN_8004b5ac RETURNS bool IN THIS PORT. Ghidra writes `iVar1 = FUN_8004b5ac(...); if
    // (iVar1 == 1)`; FighterAction.FUN_8004b5ac was ported as `bool` returning `2 < local_10`, which
    // is the same predicate — the original's only non-zero return IS 1. Tested as a bool here
    // rather than re-widened to an int, which would be a cast with no meaning behind it.
    //
    // THE LOOP GATE READS THE RING WORD-WIDE. `*(uint *)(local_18 * 4 + param_1 + 0x1d0)` is a `lw`;
    // the ring is twenty 32-bit words, as PushFighterPadHistory writes it, so ReadI32 is the right
    // width and the sign of the loaded word never reaches a comparison — only `& 0x20` does.
    internal static int FUN_8004b68c(int param_1)
    {
        int local_18 = -1;
        do
        {
            local_18 = local_18 + 1;
            if (local_18 == 4)
            {
                return -1;
            }
        }
        while ((PsxRam.ReadI32(local_18 * 4 + param_1 + BattleState.FighterPadEdgeHistory) & 0x20) == 0);

        int edgeRing = param_1 + local_18 * 4 + BattleState.FighterPadEdgeHistory;
        int stateRing = param_1 + local_18 * 4 + BattleState.FighterPadStateHistory;

        int uVar2;
        int iVar1 = FighterInput.MatchClockwiseFaceSweep(edgeRing, stateRing);
        if (iVar1 == 1)
        {
            uVar2 = 0x23;
        }
        else
        {
            iVar1 = FighterInput.MatchCounterClockwiseFaceSweep(edgeRing, stateRing);
            if (iVar1 == 1)
            {
                uVar2 = 0x24;
            }
            else if (FighterAction.FUN_8004b5ac(edgeRing))
            {
                uVar2 = 0x25;
            }
            else
            {
                iVar1 = FighterInput.MatchFaceButton1000Within3(edgeRing);
                if (iVar1 == 1)
                {
                    uVar2 = 0x26;
                }
                else
                {
                    iVar1 = FighterInput.MatchFaceButton4000Within3(edgeRing);
                    if (iVar1 == 1)
                    {
                        uVar2 = 0x27;
                    }
                    else
                    {
                        iVar1 = FighterAction.FUN_8004b4a4(edgeRing, PsxRam.ReadI32(param_1 + 0x138));
                        if (iVar1 == 1)
                        {
                            uVar2 = 0x28;
                        }
                        else
                        {
                            uVar2 = -1;
                        }
                    }
                }
            }
        }

        return uVar2;
    }
}
