namespace DbzLegendsRemaster.VS_EXE;

// THE SHAPE OF A BATTLE — the offsets and shared globals every tranche-2 slice reads, declared once,
// before any of them is written.
//
// This file exists because of what tranche 1 cost. Six agents wrote six opcode families in
// parallel, and each redeclared the VM's shared globals for itself: twelve symbols duplicated,
// seven with divergent types, one of them existing four times — twice as a PSX address and twice as
// a managed array. It all compiled, and on the console there is one of each, so one family would
// write and another read its own copy and see nothing. Fixing it afterwards took two more passes
// and turned up two further instances nobody had listed.
//
// The lesson was that splitting by BEHAVIOUR is the right axis for code and the wrong one for
// STATE. So the state is settled here first, and the behaviour slices take it as given.
//
// Nothing in this file is a struct declaration. The mandate keeps the original's memory layout
// intact, so a fighter is 0x240 bytes of PSX memory reached through PsxRam at the offsets below —
// not a C# object with fields. These constants only give those offsets names where the evidence
// closes one.
internal static class BattleState
{
    // =====================================================================================
    // THE BATTLE CONTEXT — 0x3034 bytes
    // =====================================================================================
    // Created by main as a task workspace: CreateTask(LAB_80055E3C, 0x51, 9, 0x3034, 0,
    // g_TaskListTail[9]). List 9 runs before list 10, so the manager has moved before any fighter
    // does, inside one frame.

    internal const int BattleContextSize = 0x3034;

    // GHIDRA: battleContext + 0x1520 (VS.EXE)
    // TWELVE fighter slots, four bytes each — but only six ever hold a fighter. FUN_800511A8
    // @ 0x800511A8 fills them in one straight run and the pattern is the whole match format:
    //     FUN_800512CC(ctx, 0, 0) -> +0x1520      FUN_800512CC(ctx, 3, 6) -> +0x1538
    //     FUN_800512CC(ctx, 1, 1) -> +0x1524      FUN_800512CC(ctx, 4, 7) -> +0x153C
    //     FUN_800512CC(ctx, 2, 2) -> +0x1528      FUN_800512CC(ctx, 5, 8) -> +0x1540
    //     +0x152C, +0x1530, +0x1534 = 0           +0x1544, +0x1548, +0x154C = 0
    // Fighter indices 0..5; SLOT indices 0,1,2 and 6,7,8. Three against three, with the two teams
    // six slots apart and the gaps written zero rather than left alone.
    internal const int CtxFighterSlots = 0x1520;

    internal const int CtxFighterSlotCount = 12;

    // GHIDRA: battleContext + 0x15B0 (VS.EXE)
    // Per-slot records of 0x14 bytes.
    internal const int CtxSlotRecords = 0x15B0;

    internal const int CtxSlotRecordStride = 0x14;

    // GHIDRA: battleContext + 0x15B4 (VS.EXE)
    // The ki gauge, capped at 16000.
    internal const int CtxKiGauge = 0x15B4;

    internal const int CtxKiGaugeCap = 16000;

    // GHIDRA: battleContext + 0x15B8 (VS.EXE)
    // What this slot contributes to the central gauge.
    internal const int CtxGaugeContribution = 0x15B8;

    // GHIDRA: battleContext + 0x15C0 (VS.EXE)
    // The current target's index.
    internal const int CtxTargetIndex = 0x15C0;

    // GHIDRA: battleContext + 0x302C (VS.EXE)
    // THE CENTRAL GAUGE, bounded to +/-30000 — the tug-of-war bar between the two teams, and the
    // last field of the context (0x302C + 4 = 0x3030, inside the 0x3034 the task reserves).
    internal const int CtxCentralGauge = 0x302C;

    internal const int CtxCentralGaugeLimit = 30000;

    // =====================================================================================
    // A FIGHTER — 0x240 bytes
    // =====================================================================================
    // Created by FUN_800512CC @ 0x800512CC as a task workspace:
    // CreateTask(LAB_80050AE4, 0, 10, 0x240, 1, g_TaskListTail[10]). Every offset below is written
    // by that function's own prologue, which is why they are closed and the rest of the 0x240 is
    // not.

    internal const int FighterSize = 0x240;

    // GHIDRA: fighter + 0x0C .. + 0x7C (VS.EXE)
    // A table of SELF-POINTERS: the creator stores addresses of the fighter's own sub-blocks into
    // its head, so later code can reach them without knowing the offsets.
    //     +0x0C -> +0x10      +0x10 -> +0x114     +0x14 -> +0x11C
    //     +0x18 -> +0x114     +0x1C -> +0x80      +0x20 -> +0xD0      +0x24 -> +0xB0
    // +0x10 and +0x18 both point at +0x114; that is the original's, not a transcription slip.
    //
    // CORRECTED: this comment first said the table was those seven entries and stopped at +0x24.
    // It runs to +0x7C — TWENTY-SIX stores, the last being `sw a0, 0x7c(v0)` @ 0x800516DC — and
    // leaves exactly three words of the span alone: +0x50, +0x60, +0x6C. Two of the twenty-six do
    // not point into the workspace the way the rest do: +0x34 takes the fighter's own base, and
    // +0x64 takes the TASK NODE, which is the node's third resting place after +0xAC and +0x104.
    // The seven named above were not wrong, the extent was.
    internal const int FighterSelfPointers = 0x0C;

    internal const int FighterSelfPointersEnd = 0x7C;

    // GHIDRA: fighter + 0x80, + 0xB0, + 0xD0 (VS.EXE)
    // The three sub-blocks the self-pointer table addresses. +0xB0 is FighterBoundsMin below;
    // it had a second name here, which is one offset with two spellings and exactly the kind of
    // fork this file exists to stop.
    internal const int FighterBlock80 = 0x80;

    internal const int FighterBlockD0 = 0xD0;

    // GHIDRA: fighter + 0xAC, + 0x104 (VS.EXE)
    // Its own task node, stored twice.
    internal const int FighterTaskNode = 0xAC;

    internal const int FighterTaskNodeAlias = 0x104;

    // GHIDRA: fighter + 0xF0 (VS.EXE)
    // Back-pointer to the battle context that created it.
    internal const int FighterBattleContext = 0xF0;

    // GHIDRA: fighter + 0x173 (VS.EXE)
    // Its SLOT index — 0, 1, 2, 6, 7 or 8 — not its fighter index. One byte, written `sb`.
    internal const int FighterSlotIndex = 0x173;

    // GHIDRA: fighter + 0x160 (VS.EXE)
    // Its FIGHTER index — 0, 1, 2, 3, 4, 5. A halfword, written `sh v1, 0x160(v0)` @ 0x800516EC
    // from FUN_800512CC's second argument, and the self-pointer at +0x68 addresses it.
    //
    // THIS IS NOT THE SLOT INDEX ABOVE, and the two agree only for the first three fighters. The
    // creator is called (0,0) (1,1) (2,2) (3,6) (4,7) (5,8), so fighters 3, 4 and 5 carry index
    // 3/4/5 here and slot 6/7/8 at +0x173. Confusing the two is precisely the mistake this file
    // exists to prevent, which is why both are named rather than one being left raw.
    internal const int FighterIndex = 0x160;

    // GHIDRA: fighter + 0x114 .. + 0x120, + 0x154, + 0x164 .. + 0x168 (VS.EXE)
    // The halfwords the creator explicitly zeroes.
    internal const int FighterZeroedFrom114 = 0x114;

    // =====================================================================================
    // THE PLACEMENT SET, AND THE CROSS-OVERLAY WORD THAT PICKS IT
    // =====================================================================================
    // FUN_800512CC branches on DAT_801FF100 and writes one of two triples into +0xB0..+0xBC:
    //
    //            DAT_801FF100 == 1            otherwise        signed
    //   +0xB0    0xB1E0                       0xFE20            min X: -20000 / -480
    //   +0xB2    0xF448                       0xFD00            min Y:  -3000 / -768
    //   +0xB4    0xB1E0                       0xFE20            min Z: -20000 / -480
    //   +0xB8    20000                        0x1E0             max X:  20000 /  480
    //   +0xBA    0x78                         0x78              max Y:    120 /  120
    //   +0xBC    20000                        0x1E0             max Z:  20000 /  480
    //
    // DAT_801FF100 IS THE HANDOVER WORD SELECT.EXE WRITES. The reconnaissance closed that it is a
    // mode-and-result word, not a character id — three values in on entry, 3/4/5 written back on
    // exit — and the six character ids live behind it at 0x801FF102..0x801FF10C. So the mode
    // SELECT.EXE hands over decides where the fighters stand. Both halves of that contract are now
    // visible from both sides.
    //
    // It is read through SharedHighRam, which already models the 0x801FF000 block for every
    // overlay: this file does not redeclare it.
    // CORRECTED, AND THE CORRECTION IS MINE TO OWN. These were first named FighterPlacementRot and
    // FighterPlacementScale, on nothing better than their neighbours and the fact that a fighter has
    // to be placed somewhere. The fighter task reads them as the two corners of an AXIS-ALIGNED BOX
    // it clamps the position triple into, one comparison per axis, and the values agree: the default
    // set spans (-480, -768, -480) to (480, 120, 480), and the DAT_801FF100 == 1 set spans
    // (-20000, -3000, -20000) to (20000, 120, 20000) — a wider arena, same floor height.
    // Nothing rotates and nothing scales. Rule 11: a speculative name that hides an unknown is worse
    // than a raw one, and this pair had been hiding a clamp box.
    //
    // The slice that found it reported it upward instead of renaming, which is what the ownership
    // rule asks of a slice that does not own this file.
    internal const int FighterBoundsMin = 0xB0;

    internal const int FighterBoundsMax = 0xB8;

    // =====================================================================================
    // THE TASK ENTRY POINTS
    // =====================================================================================
    // Raw PSX addresses, because CreateTask stores them verbatim in the node at +0x04 — a node
    // built by this port compares byte for byte with one read out of PCSX-Redux.

    // GHIDRA: LAB_80055e3c @ 0x80055E3C (VS.EXE)
    // The battle manager's task callback. Task id 0x51, list 9.
    internal const int BattleManagerEntry = unchecked((int)0x80055E3C);

    // GHIDRA: LAB_80050ae4 @ 0x80050AE4 (VS.EXE)
    // A fighter's task callback. Task id 0, list 10, one per fighter, six live at once.
    internal const int FighterEntry = unchecked((int)0x80050AE4);

    // GHIDRA: FUN_800511a8 @ 0x800511A8 (VS.EXE)
    internal const int CreateAllFightersAddress = unchecked((int)0x800511A8);

    // GHIDRA: FUN_800512cc @ 0x800512CC (VS.EXE)
    internal const int CreateFighterAddress = unchecked((int)0x800512CC);
}
