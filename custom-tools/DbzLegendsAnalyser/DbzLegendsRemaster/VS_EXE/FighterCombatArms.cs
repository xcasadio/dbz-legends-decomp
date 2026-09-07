using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE FOUR "ARM THE HIT REACTION" LEAVES — 0x8004D32C, 0x8004D574, 0x8004D694, 0x8004D9F4.
//
// WHAT THEY ARE. FighterCombat.cs's FUN_8004dfc4 switches on the acting fighter's attack-record
// type byte and dispatches to three of these four; the fourth (FUN_8004d574) is reached from
// FUN_8004ee48, the attack-event applier in the same file. Until now all four were empty private
// BLOCKED stubs inside FighterCombat.cs — see the WIRING NOTE at the bottom of this header, which
// is the one thing the main session must act on before any of this runs.
//
// THEY ARE FOUR SPELLINGS OF ONE SHAPE, and reading them side by side is what makes them legible:
//
//   1. copy the 8 bytes at DAT_8008d160/DAT_8008d164 into an 8-byte stack local (an SVECTOR-shaped
//      {vx, vy, vz, pad} — FUN_800437ec reads exactly three shorts out of it);
//   2. fire a sound/effect cue through SoundEffects.FUN_8005ef20(<cue id>, fighter + 0x114), the cue id being a
//      literal that differs per function and, in two of the four, per branch;
//   3. IF the target's state byte (+0x16A) is already 0x17 — and, in two of the four, the target's
//      task node also already points at this attacker — do nothing but re-fire FUN_800437ec with
//      mode 2 and return WITHOUT touching state or flags. This is the "already reacting" early-out.
//   4. OTHERWISE: build a direction argument, call FUN_800437ec with the function's own mode
//      literal, force the target's FSM state through FighterCombat.FighterSetState, and rewrite
//      the target's +0x138 flag word as `(flags & 0xFA640000) | <one bit>`.
//
// The four bits are 0x100 (FUN_8004d32c), 0x200 (FUN_8004d574), 0x400 (FUN_8004d694) and, for
// FUN_8004d9f4 alone, one of 0x800 / 0x1000 / 0x2000 chosen from the ATTACKER's own state byte
// (0x25 -> 0x800, 0x23 -> 0x1000, 0x24 -> 0x2000). That last mapping is also read back on the very
// next hit: FUN_8004d9f4's own guard skips the streak bookkeeping when the bit already set matches
// the attacker's current state, i.e. when the same special is landing twice in a row.
//
// THE MASK 0xFA640000 IS NOT A TYPO AND IS NOT NARROWED. It clears every low bit (all 16, plus
// bits 16-24 and 25/27/28/30 of the high half) and keeps 0xFA640000's set bits. Every one of the
// four writes it as two separate stores — `flags &= 0xFA640000;` then `flags |= <bit>;` — with a
// real load between them, exactly as the original does; they are not folded into one store.
//
// THE STREAK COUNTER, shared by FUN_8004d694 and FUN_8004d9f4 verbatim. Both compute
//     psVar = ctx + 0x15BA + slot * 0x14
// where ctx is the ATTACKER's battle context (attacker + 0xF0) and slot the ATTACKER's slot index
// (attacker + 0x173). BattleState.CtxSlotRecords (0x15B0) and CtxSlotRecordStride (0x14) cover the
// base and the stride; the +0xA field inside the record has NO name in BattleState.cs — it sits
// between CtxGaugeContribution (+0x8) and CtxTargetIndex (+0x10) — so it is written here as the
// raw literal 0xA with this note. It deserves a name; adding one is not this file's to do.
// The bookkeeping itself: when the attacker's +0x138 bit 0x40 is clear the counter is zeroed and
// the target's +0x15E halfword is zeroed; when it is set the counter is incremented and, if the
// increment landed on exactly 1, forced to 2 (so the counter's value 1 is unreachable — reproduced,
// not "fixed", per rule 12), and the target's +0x15E gains 0xC once the counter passes 1.
// The +0x40 bit is READ TWICE, in two separate `if`s, with the counter update in between; that is
// the original's own shape and is kept as two tests rather than one if/else.
//
// THE 8-BYTE COPY IS AN UNALIGNED COPY IN THE ORIGINAL, and it was decoded from the raw bytes
// rather than trusted from the decompiler's rendering, because Ghidra prints it as a frightening
// mask-and-shift expression followed by a plain assignment that appears to overwrite it. At
// 0x8004D594 (and identically at 0x8004D354, 0x8004D6B4, 0x8004DA1C):
//     3C038009  lui   v1, 0x8009
//     2463D160  addiu v1, v1, -0x2EA0      ; v1 = 0x8008D160
//     88680003  lwl   t0, 0x3(v1)          \ one unaligned 32-bit load of DAT_8008d160
//     98680000  lwr   t0, 0x0(v1)          /
//     88690007  lwl   t1, 0x7(v1)          \ one unaligned 32-bit load of DAT_8008d164
//     98690004  lwr   t1, 0x4(v1)          /
//     A8480003  swl   t0, 0x3(v0)          \ one unaligned 32-bit store to the local
//     B8480000  swr   t0, 0x0(v0)          /
//     A8490007  swl   t1, 0x7(v0)          \ and one to the local's second word
//     B8490004  swr   t1, 0x4(v0)          /
// i.e. a plain 8-byte struct copy that the compiler emitted unaligned because the struct's
// declared alignment did not promise otherwise. Both ends are word-aligned in practice, so the
// observable effect is exactly two 32-bit stores, which is what this port writes. Ghidra's
// mask/shift line and its following assignment are ONE copy, not two writes; transliterating both
// lines literally would have written the same location twice.
//
// DAT_8008d160 AND DAT_8008d164 ARE READ-ONLY AND ZERO. find-cross-references reports 8 references
// to each, every one a READ, and all sixteen come from these four functions and nowhere else in
// VS.EXE. read-memory at 0x8008D160 gives `00 00 00 00 00 00 00 00` (the neighbouring word at
// 0x8008D168 is 0x00808080, so this is initialised .data, not a zero-filled .bss hole whose value
// would be a guess). They are therefore transliterated as two C# statics holding the image's own
// value, with no writer, rather than as PsxRam reads of an address nothing in the port populates.
//
// WHAT IS NOT CLOSED. What the eight zero bytes MEAN is not established — FUN_800437ec is out of
// this slice, so the three shorts it reads out of them are unnamed. Nor is the meaning of
// FUN_8005ef20's cue-id argument (1..9 across these four), nor FUN_800437ec's third argument
// (0, 2, 0xC, 0xF, 0x11 here). Neither is named or guessed at below.
//
// WIRING NOTE FOR THE MAIN SESSION — three items, none of which this file may perform itself:
//   1. FighterCombat.cs still declares its own empty private FUN_8004d32c / FUN_8004d574 /
//      FUN_8004d694 / FUN_8004d9f4. Those four stubs must be DELETED and their call sites in
//      FUN_8004dfc4 and FUN_8004ee48 pointed at FighterCombatArms.<name>. Until then C# binds the
//      unqualified calls to FighterCombat's own empty bodies and nothing here runs — the exact
//      defect this repository has shipped before.
//   2. AnimCmdMesh.ComputeYawPitchToTarget @ 0x80045F34 IS ALREADY PORTED IN FULL, but it is
//      `private`, so this file cannot reach it and carries a no-op stub instead (below). That stub
//      is the single largest piece of dropped behaviour in this file: FUN_8004d694 and
//      FUN_8004d9f4 both feed its output straight into FUN_80045b70 and into local_18. Making
//      AnimCmdMesh's member `internal` and replacing this file's stub with a qualified call is the
//      correct fix; it needs an edit to AnimCmdMesh.cs, which is out of this file's scope.
//   3. FUN_800437ec @ 0x800437EC (stubbed below) calls TaskSystem.CreateTask with the entry point
//      &LAB_800436D0. Whoever ports it must ALSO register that entry point as a callback, or the
//      node it creates will dispatch nothing. Flagged here because this file is where the seven
//      call sites of FUN_800437ec live (all seven are inside these four functions).
internal static class FighterCombatArms
{
    // GHIDRA: DAT_8008d160 @ 0x8008D160 (VS.EXE)
    // First word of the 8-byte block all four functions copy onto their stack. Image value 0,
    // measured with read-memory; 8 references in the whole overlay, all reads, all from this file's
    // four functions. See this file's header for the full evidence.
    private static int DAT_8008d160;

    // GHIDRA: DAT_8008d164 @ 0x8008D164 (VS.EXE)
    // Second word of the same block. Same evidence, same posture.
    private static int DAT_8008d164;

    // JUSTIFICATION: C# language bridge only
    // RELATION: stands for the stack frame of FUN_8004d32c @ 0x8004D32C. Its locals are passed BY
    // ADDRESS to FUN_800437ec, and a C# local has no address, so the frame is given one.
    //
    // THE LAYOUT IS THE ORIGINAL'S OWN, taken from the instructions, not invented. At 0x8004D348
    // `addiu v0, s8, 0x10` makes v0 the destination of the 8-byte copy, so auStack_18 sits at
    // s8+0x10 and auStack_14 at s8+0x14; at 0x8004D4DC `addiu v0, s8, 0x18` / `addiu a1, s8, 0x10`
    // builds the two pointer arguments of `EffectSystem.FUN_800437ec(&local_10, auStack_18, 2)`, so local_10 is
    // at s8+0x18 and its two neighbours local_e / local_c follow it as halfwords. Rebased to 0
    // here, the frame is: +0x00 auStack_18, +0x04 auStack_14, +0x08 local_10, +0x0A local_e,
    // +0x0C local_c — the same relative distances the console uses.
    //
    // THE ADDRESS IS REAL STACK MEMORY, in the range crt0 puts the stack in (SP starts at
    // 0x807FFFF8 and runs down 0x8000 bytes), and it aliases nothing: the four blocks this file
    // declares occupy 0x807FFE80..0x807FFEC0, ending exactly where BattleManager.cs's own
    // GaugeStripBufferAddress (0x807FFEC0) begins, which in turn ends where AnimCmdControl.cs's
    // VStack80Address (0x807FFFC0) begins, followed by BattleScene.cs (0x807FFFD0),
    // AnimCmdEffects.cs (0x807FFFE0) and AnimVmInterpreter.cs (0x807FFFF0).
    //
    // ONE BLOCK PER FUNCTION rather than one shared block, because these four are four distinct
    // frames on the console and giving them one address would be a deviation with no need behind
    // it. They never nest (none of the four calls another), so no block is ever live twice.
    private const int Fun8004d32cFrameAddress = unchecked((int)0x807FFE80);

    private static readonly byte[] Fun8004d32cFrame =
        LibGpu.RamRegion(Fun8004d32cFrameAddress, 0x10);

    // JUSTIFICATION: C# language bridge only
    // RELATION: stands for the stack frame of FUN_8004d574 @ 0x8004D574. Two words, auStack_10 at
    // +0x00 and uStack_c at +0x04, matching `addiu a1, s8, 0x10` at the FUN_800437ec call sites and
    // the swl/swr pair that targets the same s8+0x10. Same address-range reasoning as above.
    private const int Fun8004d574FrameAddress = unchecked((int)0x807FFE90);

    private static readonly byte[] Fun8004d574Frame =
        LibGpu.RamRegion(Fun8004d574FrameAddress, 8);

    // JUSTIFICATION: C# language bridge only
    // RELATION: stands for the stack frame of FUN_8004d694 @ 0x8004D694. `addiu v0, s8, 0x10` at
    // 0x8004D6B0 puts local_18 at s8+0x10 and local_14 (12 bytes) immediately after at s8+0x14;
    // rebased to 0 that is +0x00 and +0x04. ComputeYawPitchToTarget writes three halfwords starting
    // at +0x00, so its third result lands in local_14's first halfword — which is why the code
    // below reads +0x04 to get it. Same address-range reasoning as above.
    private const int Fun8004d694FrameAddress = unchecked((int)0x807FFEA0);

    private static readonly byte[] Fun8004d694Frame =
        LibGpu.RamRegion(Fun8004d694FrameAddress, 0x10);

    // JUSTIFICATION: C# language bridge only
    // RELATION: stands for the stack frame of FUN_8004d9f4 @ 0x8004D9F4 — same 4 + 12 layout as
    // FUN_8004d694 above, from the same instruction shape at 0x8004DA18. Same address-range
    // reasoning as above.
    private const int Fun8004d9f4FrameAddress = unchecked((int)0x807FFEB0);

    private static readonly byte[] Fun8004d9f4Frame =
        LibGpu.RamRegion(Fun8004d9f4FrameAddress, 0x10);

    // GHIDRA: FUN_8004d32c @ 0x8004D32C (VS.EXE)
    // 584 bytes. One caller: FighterCombat.FUN_8004dfc4, `FUN_8004d32c(param_2, 0x16, param_1)` on
    // attack-record type 1 or 2 — so param_1 is the TARGET, param_2 the FSM opcode 0x16, param_3
    // the ATTACKER. Ghidra's own signature is `void FUN_8004d32c(int param_1, ushort param_2,
    // int param_3)` and is kept.
    //
    // THE ONE THING THIS FUNCTION DOES THAT ITS THREE SIBLINGS DO NOT: it does not aim. Instead of
    // calling ComputeYawPitchToTarget it builds a JITTERED COPY OF THE TARGET'S OWN POSITION, three
    // halfwords read from target + 0x114 / + 0x116 / + 0x118 (inside BattleState's
    // FighterZeroedFrom114 range; no more specific name covers the individual components) with an
    // independent `rand() % 10` added to each and a per-component constant subtracted: -5 on X,
    // -0x23 (-35) on Y, -5 on Z. So X and Z jitter over [-5, +4] around the target's own position
    // and Y over [-35, -26] — an offset well above or below the body, not a centred jitter. Three
    // separate rand() calls, in that order; the order matters because Kernel.rand is the game's own
    // LCG and every draw advances the shared seed.
    //
    // THE MODULO IS WRITTEN AS GHIDRA PRINTS IT — `(short)iVar3 + (short)(iVar3 / 10) * -10` — not
    // collapsed to `% 10`. rand() returns 0..0x7FFF so the two forms agree, but the original's
    // divide-multiply-subtract is what the instructions do and rule 1 says not to optimise it away.
    //
    // THE EARLY-OUT TESTS TWO THINGS, both on the target: state byte +0x16A already 0x17, AND the
    // word at *(target + 0xAC) + 8 equal to param_3, i.e. the target's TASK NODE
    // (BattleState.FighterTaskNode) already carries this very attacker in its +0x8 slot. Only when
    // BOTH hold does it take the mode-2 path and leave state and flags untouched. Note the jittered
    // position is computed BEFORE the test and is passed to FUN_800437ec on both paths.
    internal static void FUN_8004d32c(int param_1, ushort param_2, int param_3)
    {
        // The 8-byte unaligned copy from DAT_8008d160/DAT_8008d164 — one copy, two words. See the
        // header note: Ghidra's mask/shift line and the assignment under it are the same store.
        PsxRam.WriteI32(Fun8004d32cFrameAddress + 0x00, DAT_8008d160);
        PsxRam.WriteI32(Fun8004d32cFrameAddress + 0x04, DAT_8008d164);

        SoundEffects.FUN_8005ef20(1, param_1 + BattleState.FighterZeroedFrom114);

        // Each component is stored the moment it is computed, in the original's own order, rather
        // than computed into three C# locals and flushed at the end: the three rand() draws share
        // one seed, so the order is observable, and keeping the stores interleaved keeps the
        // transliteration statement-for-statement.
        int iVar3 = Kernel.rand();
        PsxRam.WriteU16(Fun8004d32cFrameAddress + 0x08,
            (ushort)(short)((short)PsxRam.ReadU16(param_1 + BattleState.FighterZeroedFrom114)
                + (short)iVar3 + (short)(iVar3 / 10) * -10 + -5));

        iVar3 = Kernel.rand();
        // +0x116 and +0x118: raw literals. BattleState.FighterZeroedFrom114 names the RANGE that
        // starts at 0x114; it has no per-component names, and this port does not add any.
        PsxRam.WriteU16(Fun8004d32cFrameAddress + 0x0A,
            (ushort)(short)((short)PsxRam.ReadU16(param_1 + 0x116)
                + (short)iVar3 + (short)(iVar3 / 10) * -10 + -0x23));

        iVar3 = Kernel.rand();
        PsxRam.WriteU16(Fun8004d32cFrameAddress + 0x0C,
            (ushort)(short)((short)PsxRam.ReadU16(param_1 + 0x118)
                + (short)iVar3 + (short)(iVar3 / 10) * -10 + -5));

        // +0x16A: the fighter state byte. Named in the Ghidra database itself (see
        // FighterCombat.FighterSetState's note) but with no BattleState constant, so raw here.
        if ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 0x17
            && PsxRam.ReadI32(PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode) + 8) == param_3)
        {
            EffectSystem.FUN_800437ec(Fun8004d32cFrameAddress + 0x08, Fun8004d32cFrameAddress + 0x00, 2);
        }
        else
        {
            EffectSystem.FUN_800437ec(Fun8004d32cFrameAddress + 0x08, Fun8004d32cFrameAddress + 0x00, 0);
            FighterCombat.FighterSetState(param_1, param_2);

            // Two stores with a real load between them, exactly as the original writes them.
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfa640000));
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x100);
        }
    }

    // GHIDRA: FUN_8004d574 @ 0x8004D574 (VS.EXE)
    // 288 bytes. One caller, FighterCombat.FUN_8004ee48, whose decompilation renders the call with
    // five arguments (`FUN_8004d574(iVar7,0x16,param_3,uVar9,uVar3)`); Ghidra's analysis of THIS
    // function's own body settles a two-parameter signature and the three trailing values never
    // reach a parameter the body reads. That reading is FighterCombat.cs's own, recorded on the
    // stub this file replaces, and it is unchanged here: the signature stays (int, ushort).
    //
    // THE SIMPLEST OF THE FOUR. No aiming, no jitter, no streak counter: cue 9, then the early-out
    // on state 0x17 ALONE (no task-node test, unlike FUN_8004d32c and FUN_8004d694), then mode 0xF,
    // state param_2, flag bit 0x200. The direction argument is the fighter's own +0x124 field in
    // both branches — a raw literal, no BattleState constant covers it; the same field the other
    // three pass as FUN_800437ec's first argument.
    internal static void FUN_8004d574(int param_1, ushort param_2)
    {
        PsxRam.WriteI32(Fun8004d574FrameAddress + 0x00, DAT_8008d160);
        PsxRam.WriteI32(Fun8004d574FrameAddress + 0x04, DAT_8008d164);

        SoundEffects.FUN_8005ef20(9, param_1 + BattleState.FighterZeroedFrom114);

        if ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 0x17)
        {
            EffectSystem.FUN_800437ec(param_1 + 0x124, Fun8004d574FrameAddress + 0x00, 2);
        }
        else
        {
            EffectSystem.FUN_800437ec(param_1 + 0x124, Fun8004d574FrameAddress + 0x00, 0xf);
            FighterCombat.FighterSetState(param_1, param_2);

            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfa640000));
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x200);
        }
    }

    // GHIDRA: FUN_8004d694 @ 0x8004D694 (VS.EXE)
    // 864 bytes. Three callers, all FighterCombat.FUN_8004dfc4: attack-record type 4 ->
    // `FUN_8004d694(param_2, 0x19, param_1)`, type 5 -> opcode 0x1a, type 6 -> opcode 0x18. So
    // param_1 is the TARGET, param_2 the opcode, param_3 the ATTACKER. Ghidra's signature is
    // `void FUN_8004d694(int param_1, int param_2, int param_3)` — param_2 is an int here (it is
    // compared against three literals before being narrowed for FighterSetState), unlike its
    // ushort in the two siblings, and that is kept rather than harmonised.
    //
    // THE CUE ID IS PICKED FROM THE OPCODE, through a three-way chain Ghidra renders as
    // `if (0x19) ... else if (< 0x1a) { if (0x18) ... } else if (0x1a) ...`. Written out that is
    // 0x18 -> cue 5, 0x19 -> cue 6, 0x1a -> cue 7, and any other opcode fires NO cue at all while
    // still running everything below. The chain is transliterated in the original's own order and
    // shape, not collapsed into a switch: the `else if (param_2 < 0x1a)` arm has no else of its
    // own, which is exactly why an unexpected opcode falls through silently.
    //
    // THE AIMING STEP. On the non-early-out path it calls AnimCmdMesh.ComputeYawPitchToTarget(target + 0x114,
    // attacker + 0x114, local_18) — note the order: FROM the target's position TO the attacker's,
    // so the angles point the target back at whoever hit it. The two halfwords it then feeds to
    // FUN_80045b70 are read as SIGNED halfwords (`lh v0,0x14(s8)` / `lh v1,0x12(s8)` at
    // 0x8004D818), which is why both reads below are `(short)PsxRam.ReadU16(...)` and not raw
    // ushorts; +0x14 is the third angle (in local_14) and +0x12 the second (in local_18).
    // FUN_80045b70's byte result is then reduced to a single bit: `(result & 0x80) << 8`, stored as
    // a halfword, so only 0x8000 or 0 ever reaches local_18's first halfword. local_18's second
    // halfword is cleared first. Both writes overwrite angles ComputeYawPitchToTarget just wrote.
    //
    // WHAT THIS PORT CURRENTLY DROPS HERE: ComputeYawPitchToTarget is a no-op stub in this file
    // (see the WIRING NOTE in the header), so local_18/local_14 still hold the zeroed 8-byte copy
    // when FUN_80045b70 is called. Stated rather than hidden.
    internal static void FUN_8004d694(int param_1, int param_2, int param_3)
    {
        PsxRam.WriteI32(Fun8004d694FrameAddress + 0x00, DAT_8008d160);
        PsxRam.WriteI32(Fun8004d694FrameAddress + 0x04, DAT_8008d164);

        if (param_2 == 0x19)
        {
            SoundEffects.FUN_8005ef20(6, param_1 + BattleState.FighterZeroedFrom114);
        }
        else if (param_2 < 0x1a)
        {
            if (param_2 == 0x18)
            {
                SoundEffects.FUN_8005ef20(5, param_1 + BattleState.FighterZeroedFrom114);
            }
        }
        else if (param_2 == 0x1a)
        {
            SoundEffects.FUN_8005ef20(7, param_1 + BattleState.FighterZeroedFrom114);
        }

        if ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 0x17
            && PsxRam.ReadI32(PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode) + 8) == param_3)
        {
            EffectSystem.FUN_800437ec(param_1 + 0x124, Fun8004d694FrameAddress + 0x00, 2);
        }
        else
        {
            AnimCmdMesh.ComputeYawPitchToTarget(
                param_1 + BattleState.FighterZeroedFrom114,
                param_3 + BattleState.FighterZeroedFrom114,
                Fun8004d694FrameAddress + 0x00);

            ushort uVar3 = EffectSystem.FUN_80045b70(
                (short)PsxRam.ReadU16(Fun8004d694FrameAddress + 0x04),
                (short)PsxRam.ReadU16(Fun8004d694FrameAddress + 0x02));

            PsxRam.WriteU16(Fun8004d694FrameAddress + 0x02, 0);
            PsxRam.WriteU16(Fun8004d694FrameAddress + 0x00, (ushort)((uVar3 & 0x80) << 8));

            EffectSystem.FUN_800437ec(param_1 + 0x124, Fun8004d694FrameAddress + 0x00, 0xc);
            FighterCombat.FighterSetState(param_1, (ushort)param_2);

            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfa640000));
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x400);

            // The streak counter, on the ATTACKER's own slot record. 0x15BA = CtxSlotRecords
            // (0x15B0) + 0xA; the +0xA field has no name in BattleState.cs, so the literal is kept.
            int psVar4 = PsxRam.ReadU8(param_3 + BattleState.FighterSlotIndex)
                * BattleState.CtxSlotRecordStride
                + PsxRam.ReadI32(param_3 + BattleState.FighterBattleContext)
                + BattleState.CtxSlotRecords + 0xa;

            if ((PsxRam.ReadI32(param_3 + 0x138) & 0x40) == 0)
            {
                PsxRam.WriteU16(psVar4, 0);
            }
            else
            {
                PsxRam.WriteU16(psVar4, (ushort)((short)PsxRam.ReadU16(psVar4) + 1));
                if ((short)PsxRam.ReadU16(psVar4) == 1)
                {
                    PsxRam.WriteU16(psVar4, 2);
                }
            }

            // The +0x40 bit is re-tested here rather than folded into the branch above: the
            // original's own shape, kept.
            if ((PsxRam.ReadI32(param_3 + 0x138) & 0x40) == 0)
            {
                // +0x15E: raw literal, no BattleState name covers it.
                PsxRam.WriteU16(param_1 + 0x15e, 0);
            }
            else if (1 < (short)PsxRam.ReadU16(psVar4))
            {
                PsxRam.WriteU16(param_1 + 0x15e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x15e) + 0xc));
            }
        }
    }

    // GHIDRA: FUN_8004d9f4 @ 0x8004D9F4 (VS.EXE)
    // 1180 bytes. One caller: FighterCombat.FUN_8004dfc4, `FUN_8004d9f4(param_2, 0x16, param_1)` on
    // attack-record type 3 — param_1 the TARGET, param_2 opcode 0x16, param_3 the ATTACKER.
    //
    // THE ODD ONE OF THE FOUR, in three ways, all of them load-bearing:
    //
    //   1. IT HAS NO EARLY-OUT. There is no `state == 0x17` test at all; it always aims, always
    //      calls FUN_800437ec (mode 0x11, once, not twice), always sets state, always rewrites the
    //      flag word. The `if` it does have guards only the streak counter.
    //   2. THE CUE ID COMES FROM rand() % 4, not from the opcode: 0 -> cue 1, 1 -> cue 2, 2 -> cue
    //      3, 3 -> cue 4. The modulo is written the way the compiler emitted it — a negative-
    //      correcting `+3` before an arithmetic `>> 2`, i.e. C truncation toward zero — even though
    //      rand() is never negative here, because that is the code that exists.
    //   3. THE FLAG BIT COMES FROM THE ATTACKER'S STATE BYTE, not from a constant: 0x23 -> 0x1000,
    //      0x24 -> 0x2000, 0x25 -> 0x800, and any other value leaves the flag word holding only
    //      `flags & 0xFA640000` with NO bit set. Same three-way `if / else if (<) / else if` shape
    //      as FUN_8004d694's cue chain, with the same silent fall-through, kept as written.
    //
    // THE STREAK GUARD is the mirror of point 3 and reads the bits point 3 wrote on the PREVIOUS
    // hit: the counter is only touched when NOT (bit 0x800 set and attacker state 0x25), NOT (bit
    // 0x1000 set and attacker state 0x23), NOT (bit 0x2000 set and attacker state 0x24) — i.e. when
    // the same special is not landing twice in a row. Ghidra prints it as one three-clause `&&` of
    // negations; it is transliterated in that exact form rather than inverted into a positive test,
    // because inverting it by hand is precisely how an arm ends up backwards.
    //
    // The counter body itself is byte-for-byte the same as FUN_8004d694's; see that function's note
    // for the 0x15BA / +0xA naming point and for the deliberately unreachable counter value 1.
    //
    // WHAT THIS PORT CURRENTLY DROPS HERE: same as FUN_8004d694 — ComputeYawPitchToTarget is a
    // no-op stub in this file, so the two halfwords fed to FUN_80045b70 are the zeroed copy.
    internal static void FUN_8004d9f4(int param_1, ushort param_2, int param_3)
    {
        PsxRam.WriteI32(Fun8004d9f4FrameAddress + 0x00, DAT_8008d160);
        PsxRam.WriteI32(Fun8004d9f4FrameAddress + 0x04, DAT_8008d164);

        int iVar5 = Kernel.rand();
        int iVar6 = iVar5;
        if (iVar5 < 0)
        {
            iVar6 = iVar5 + 3;
        }

        iVar5 = iVar5 + (iVar6 >> 2) * -4;

        if (iVar5 == 1)
        {
            SoundEffects.FUN_8005ef20(2, param_1 + BattleState.FighterZeroedFrom114);
        }
        else if (iVar5 < 2)
        {
            if (iVar5 == 0)
            {
                SoundEffects.FUN_8005ef20(1, param_1 + BattleState.FighterZeroedFrom114);
            }
        }
        else if (iVar5 == 2)
        {
            SoundEffects.FUN_8005ef20(3, param_1 + BattleState.FighterZeroedFrom114);
        }
        else if (iVar5 == 3)
        {
            SoundEffects.FUN_8005ef20(4, param_1 + BattleState.FighterZeroedFrom114);
        }

        AnimCmdMesh.ComputeYawPitchToTarget(
            param_1 + BattleState.FighterZeroedFrom114,
            param_3 + BattleState.FighterZeroedFrom114,
            Fun8004d9f4FrameAddress + 0x00);

        ushort uVar4 = EffectSystem.FUN_80045b70(
            (short)PsxRam.ReadU16(Fun8004d9f4FrameAddress + 0x04),
            (short)PsxRam.ReadU16(Fun8004d9f4FrameAddress + 0x02));

        PsxRam.WriteU16(Fun8004d9f4FrameAddress + 0x02, 0);
        PsxRam.WriteU16(Fun8004d9f4FrameAddress + 0x00, (ushort)((uVar4 & 0x80) << 8));

        EffectSystem.FUN_800437ec(param_1 + 0x124, Fun8004d9f4FrameAddress + 0x00, 0x11);
        FighterCombat.FighterSetState(param_1, param_2);

        if (((PsxRam.ReadI32(param_1 + 0x138) & 0x800) == 0 || (sbyte)PsxRam.ReadU8(param_3 + 0x16a) != 0x25)
            && ((PsxRam.ReadI32(param_1 + 0x138) & 0x1000) == 0 || (sbyte)PsxRam.ReadU8(param_3 + 0x16a) != 0x23)
            && ((PsxRam.ReadI32(param_1 + 0x138) & 0x2000) == 0 || (sbyte)PsxRam.ReadU8(param_3 + 0x16a) != 0x24))
        {
            int psVar7 = PsxRam.ReadU8(param_3 + BattleState.FighterSlotIndex)
                * BattleState.CtxSlotRecordStride
                + PsxRam.ReadI32(param_3 + BattleState.FighterBattleContext)
                + BattleState.CtxSlotRecords + 0xa;

            if ((PsxRam.ReadI32(param_3 + 0x138) & 0x40) == 0)
            {
                PsxRam.WriteU16(psVar7, 0);
            }
            else
            {
                PsxRam.WriteU16(psVar7, (ushort)((short)PsxRam.ReadU16(psVar7) + 1));
                if ((short)PsxRam.ReadU16(psVar7) == 1)
                {
                    PsxRam.WriteU16(psVar7, 2);
                }
            }

            if ((PsxRam.ReadI32(param_3 + 0x138) & 0x40) == 0)
            {
                PsxRam.WriteU16(param_1 + 0x15e, 0);
            }
            else if (1 < (short)PsxRam.ReadU16(psVar7))
            {
                PsxRam.WriteU16(param_1 + 0x15e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x15e) + 0xc));
            }
        }

        // Unconditional, and OUTSIDE the guard above — this store runs even when the streak
        // bookkeeping is skipped. Dropping it would be exactly the "work silently lost" defect.
        PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) & unchecked((int)0xfa640000));

        byte bVar2 = PsxRam.ReadU8(param_3 + 0x16a);
        if (bVar2 == 0x24)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x2000);
        }
        else if (bVar2 < 0x25)
        {
            if (bVar2 == 0x23)
            {
                PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x1000);
            }
        }
        else if (bVar2 == 0x25)
        {
            PsxRam.WriteI32(param_1 + 0x138, PsxRam.ReadI32(param_1 + 0x138) | 0x800);
        }
    }

    // GHIDRA: FUN_800437ec @ 0x800437EC (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/EffectSystem.cs. The call sites in this file
    // reach it by qualified name; an empty stub in the enclosing class silently beats a real
    // body elsewhere, which is the defect check_function_addresses.py exists to catch.

    // GHIDRA: ComputeYawPitchToTarget @ 0x80045F34 (VS.EXE)
    // NOT DECLARED HERE, AND THIS ONE WAS A REAL LOSS while it was. AnimCmdMesh.cs ports it IN FULL
    // (712 bytes); it was merely `private`, so the two callers below fed FUN_80045B70 an untouched
    // zero block instead of the two computed angles. The member is now `internal` and the calls
    // below are qualified.

    // GHIDRA: FUN_80045b70 @ 0x80045B70 (VS.EXE)
    // NOT DECLARED HERE. FighterCombat.cs already carries this address (still a BLOCKED stub of its
    // own, 388 bytes); this file reaches it by qualified name. Same rule as FUN_8005EF20 above.

}
