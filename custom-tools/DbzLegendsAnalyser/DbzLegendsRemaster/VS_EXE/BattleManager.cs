using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE BATTLE MANAGER — task id 0x51, list 9, and the owner of the 0x3034-byte battle context.
//
// main runs task list 20, then ClearOTag, then lists 0..19, then submits. This body is list 9's,
// the six fighters are list 10's and the scene is list 12's, so inside one frame the manager has
// already moved before any fighter runs, and both have moved before the scene draws.
//
// IT IS A FOUR-STATE MACHINE, and the state is the FIRST HALFWORD of the context. LAB_80055e3c is
// nothing but the dispatch on it:
//
//   ctx+0x00 == 0   FUN_80055ee0 @ 0x80055EE0   arm the match          -> writes state 1
//   ctx+0x00 == 1   RunBattleRound @ 0x80055F94   the round, every frame -> stays 1
//   ctx+0x00 == 2   FUN_800578e0 @ 0x800578E0   the hand-back          -> writes state 3
//   ctx+0x00 == 3   FUN_80057a40 @ 0x80057A40   idle after the match   -> terminal
//
// Nothing in these four functions writes state 2. The 1 -> 2 edge is set somewhere else in the
// overlay and is NOT closed by this slice; state 2 is where the overlay decides what SELECT.EXE
// gets back in DAT_801FF100, so whatever raises it is the end-of-match detector.
//
// WHAT THE SLICE WAS ASKED TO ESTABLISH, answered from the four bodies:
//
//   * WHO INCREMENTS THE CENTRAL GAUGE at +0x302C. RunBattleRound does, once per frame, and only it.
//     It sums the per-slot contributions at +0x15B8: slots 0..5 are ADDED, slots 6..11 are
//     SUBTRACTED. The gauge is therefore a signed tug-of-war between the two teams, positive
//     towards slots 0..5. Every contribution is zeroed again at the very end of the same frame
//     (0x80057888), so +0x15B8 is a one-frame accumulator, not a running total.
//
//   * WHERE THE +/-30000 BOUND IS APPLIED. Immediately after those two loops, at 0x8005625C and
//     0x80056274 — two independent clamps, high then low, the low one writing 0xFFFF8AD0. The
//     accumulation block above them is itself SKIPPED whenever the gauge already sits on either
//     bound, so a pegged gauge stops accruing rather than being clamped repeatedly.
//
//   * HOW THE TARGET IS CHOSEN. Two cursors live in the context head — ctx+0x14 for slots 0..5 and
//     ctx+0x16 for slots 6..11 — and each slot's own target index lives in its record at +0x15C0.
//     A slot is a legal target only when its record's low halfword has BOTH bit 0 and bit 7 set;
//     that pair is tested at every one of the eleven places a cursor moves. The pad walks the
//     cursors (left/right pick the acting slot, up/down pick its target) while DAT_801FF100 says
//     the port is human; when it says otherwise, the block at 0x800575C0 assigns each of slots
//     6, 7, 8 the first of slots 0, 1, 2 that is already aiming back at it, and failing that
//     slot-6, giving slot n the fixed target n-6. Last of all, 0x8005769C sweeps every slot whose
//     current target has stopped being legal back onto the opposing cursor.
//
//   * WHAT BECOMES OF AN EMPTY SLOT. Nothing special, and that is the finding. The twelve pointer
//     slots at +0x1520 hold six task nodes and six zeros, and EVERY walk over them is twelve long
//     with a plain `if (slot != 0)` guard — the zero slots are visited and skipped, never
//     compacted, never treated as a shorter array. The record walks at +0x15B0 do not even test the
//     pointer: they read and write all twelve records unconditionally, so records 3, 4, 5 and 9,
//     10, 11 are maintained for fighters that do not exist. Rule 12: reproduced, not corrected.
//
// REPRESENTATION. The battle context is 0x3034 bytes of PSX memory reached through PsxRam at the
// offsets BattleState.cs has already closed — not a C# object with fields. Offsets BattleState
// names are used from there; offsets it does not name are written raw, exactly as Ghidra prints
// them, rather than given a private constant here that would become a second spelling of the same
// field the moment a sibling slice needs it. Nothing below redeclares a BattleState constant under
// any name.
//
// THE CONTEXT HEAD, as this slice sees it. None of these is in BattleState and none is invented
// here; they are listed so the next slice does not have to re-derive them:
//
//   +0x00  halfword   THE STATE, 0..3, the dispatch above
//   +0x02  halfword   a countdown; 0x10 is loaded at two places and 0x80056EB4 decrements it,
//                     and its expiry moves bit 0x400000 into either 0x200000 or 0x100000
//   +0x06  halfword   a second countdown, compared against 0xF and 0
//   +0x08  halfword   zeroed when the match is armed; state 2 tests bit 0x4000 of it
//   +0x10  word       THE FLAG WORD. Eighteen distinct bits are tested or written below
//   +0x14  halfword   the acting-slot cursor for slots 0..5
//   +0x16  halfword   the acting-slot cursor for slots 6..11
//   +0x18  halfword   a slot index compared against +0x2DC2 and against the +0x1520 walk index
//   +0x1A  halfword   published copy of a cursor
//   +0x2C14           an array of halfwords, stride 2, twelve entries — a SECOND per-slot table
//                     distinct from the 0x14-byte records at +0x15B0
//   +0x2D60  word     zeroed when the match is armed, later OR'd with 3
//   +0x2D64  halfword  the knock-out timer: seeded 0x80 and counted down inside the 0x10000000 arm
//   +0x2DC2  halfword  a copy of what FUN_8005cf78 returns
//   +0x2DCA  halfword  OR'd with 2 on two of the three flag paths
//
// PARTIAL, and it covers the whole file: the control flow, the offsets and the constants are
// closed — every branch below is one instruction in the image — but what most of the eighteen bits
// of +0x10 MEAN is not, and nothing here interprets them.
internal static class BattleManager
{
    // JUSTIFICATION: C# language bridge only
    // RELATION: main @ 0x80062354 hands &LAB_80055e3c to CreateTask, which stores the raw pointer in
    // the node at +0x04. The node built by this port still stores 0x80055E3C, exactly what the
    // console holds; this call is what lets TaskSystem's dispatcher turn that address back into the
    // body below when ExecuteTaskList walks list 9.
    //
    // Exposed rather than performed, in the shape VS_EXE/FighterTask.cs uses for the same problem:
    // the creator is main, which lives in VS_EXE_exe.cs and is not this slice. Whoever wires it must
    // call this immediately before that CreateTask, or list 9 will walk a live task node and
    // dispatch nothing. Registration is idempotent.
    internal static void RegisterBattleManagerTask()
    {
        TaskSystem.RegisterCallback(BattleState.BattleManagerEntry, () => UpdateBattleManager());
    }

    // GHIDRA: LAB_80055e3c @ 0x80055E3C (VS.EXE)
    // Ghidra has no user-named function here — main references it as `&LAB_80055e3c` and the
    // decompiler serves it as FUN_80055e3c. The C# name is this port's; the symbol above is what the
    // database actually holds.
    //
    // 164 bytes, 0x80055E3C..0x80055EDF. One incoming reference, and it is not a call: main takes
    // its address at 0x80062400 as CreateTask's first argument.
    //
    // `**(ushort **)(DAT_8008d16c + 8)` is two dereferences: the running task node's +0x08 is the
    // 0x3034-byte workspace, and the workspace's first halfword is the state.
    // JUSTIFICATION: backend MonoGame only
    // RELATION: diagnostic probe, read only by Validation/VsBootDiagnostic.cs. Nothing in the
    // transliterated runtime touches these. They exist because the scene task was found never to
    // run at all, and the question "does the manager run, and what state does it reach" cannot be
    // answered from a screenshot.
    internal static int DiagManagerCalls;

    internal static int DiagLastCtxState = -1;

    internal static int DiagLastCtxFlags;

    internal static int DiagCtxFlagsEverSeen;

    internal static int DiagSceneCreateAttempts;

    internal static int DiagLastGauge;

    internal static int DiagLastD458;

    internal static int DiagLastAliveCount = -1;

    internal static int DiagCond1Pass;

    internal static readonly int[] DiagContribs = new int[12];

    internal static readonly int[] DiagCtxStateVisits = new int[8];

    // JUSTIFICATION: backend MonoGame only
    // RELATION: diagnostic probes for the ONE chain that raises a fighter's +0x144 guard, which is
    // what UpdateFighter's phase 1 tests and what the port measured never to be set. The only
    // setter in the whole overlay is `sw s1,0x144(s4)` at 0x800273E4, inside FUN_80027340, whose
    // only caller is FUN_80026d98, whose only call site is the CtxRoundRequest 0x180 gate below.
    internal static int DiagRoundRequestEverSeen;

    internal static int DiagFun80026d98Calls;

    // JUSTIFICATION: backend MonoGame only
    // RELATION: the twelve slot records' own flag halfword, and the twelve fighter-slot pointers,
    // as the last manager frame saw them. FUN_80026D98's two entry loops select slots by
    // `record & 0x210` and by whether the pointer is non-zero, so both are needed to say why they
    // select nothing.
    internal static readonly int[] DiagSlotRecordFlags = new int[12];

    internal static readonly int[] DiagSlotPointers = new int[12];

    internal static void UpdateBattleManager()
    {
        DiagManagerCalls++;
        {
            int diagCtx = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
            int diagState = PsxRam.ReadU16(diagCtx + BattleState.CtxState);
            DiagLastCtxState = diagState;
            DiagLastCtxFlags = PsxRam.ReadI32(diagCtx + BattleState.CtxFlags);
            DiagCtxFlagsEverSeen |= DiagLastCtxFlags;
            DiagLastGauge = PsxRam.ReadI32(diagCtx + BattleState.CtxCentralGauge);
            DiagLastD458 = DAT_8008d458;
            DiagRoundRequestEverSeen |= PsxRam.ReadI32(diagCtx + BattleState.CtxRoundRequest);
            if (((uint)DiagLastCtxFlags & 0x18000008) == 0) { DiagCond1Pass++; }
            int diagAlive = 0;
            for (int ds = 0; ds < 12; ds++)
            {
                int rec = diagCtx + BattleState.CtxSlotRecords + ds * BattleState.CtxSlotRecordStride;
                if ((PsxRam.ReadU16(rec) & 1) != 0 && (short)PsxRam.ReadU16(rec + 2) == 0) { diagAlive++; }
            }
            DiagLastAliveCount = diagAlive;
            for (int ds = 0; ds < 12; ds++)
            {
                DiagSlotRecordFlags[ds] = PsxRam.ReadU16(
                    diagCtx + BattleState.CtxSlotRecords + ds * BattleState.CtxSlotRecordStride);
                DiagSlotPointers[ds] = PsxRam.ReadI32(
                    diagCtx + BattleState.CtxFighterSlots + ds * 4);
            }

            for (int ds = 0; ds < 12; ds++)
            {
                DiagContribs[ds] = (short)PsxRam.ReadU16(
                    diagCtx + BattleState.CtxGaugeContribution + ds * BattleState.CtxSlotRecordStride);
            }
            if (diagState >= 0 && diagState < DiagCtxStateVisits.Length)
            {
                DiagCtxStateVisits[diagState]++;
            }
        }

        ushort uVar1;

        uVar1 = PsxRam.ReadU16(PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8));
        if (uVar1 == 1)
        {
            RunBattleRound();
        }
        else if (uVar1 < 2)
        {
            if (uVar1 == 0)
            {
                FUN_80055ee0();
            }
        }
        else if (uVar1 == 2)
        {
            FUN_800578e0();
        }
        else if (uVar1 == 3)
        {
            FUN_80057a40();
        }
    }

    // GHIDRA: FUN_80055ee0 @ 0x80055EE0 (VS.EXE)
    // STATE 0 — arm the match. 180 bytes, runs exactly once: its last act but one is to write state
    // 1, and nothing writes state 0 back.
    //
    // Ghidra types the workspace pointer `undefined2 *`, so its subscripts are HALFWORD indices:
    // `puVar1 + 0x16b0` is ctx+0x2D60, `puVar1 + 8` is ctx+0x10, `puVar1[4]` is ctx+0x08 and
    // `puVar1[1]` is ctx+0x02. Verified against the image rather than trusted: the four stores are
    // `sw zero,0x2d60(s0)`, `sw v0,0x10(s0)`, `sh zero,0x8(s0)` and `sh zero,0x2(s0)`.
    //
    // The flag word is touched TWICE, and the two are not the same store: 0x4000 goes in before the
    // three sub-initialisers run, 0x8000A000 after. That ordering is the original's and is kept.
    private static void FUN_80055ee0()
    {
        int puVar1;

        puVar1 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        DAT_8008d320 = puVar1;
        PsxRam.WriteI32(puVar1 + 0x2d60, 0);
        PsxRam.WriteI32(puVar1 + 0x10, (int)((uint)PsxRam.ReadI32(puVar1 + 0x10) | 0x4000));
        FUN_800594b4(puVar1);
        InitCentralGaugeBar(puVar1);
        FUN_8005a104(puVar1);
        PsxRam.WriteU16(puVar1 + 8, 0);
        PsxRam.WriteU16(puVar1 + 2, 0);
        DAT_8008d458 = 0;
        DAT_8008d3ec = 0;
        DAT_8008d3e8 = 0;
        DAT_8008d3e4 = 0;
        DAT_8008d3a0 = 0;
        DAT_8008d57c = 0;
        DAT_8008d428 = 0;
        DAT_8008d448 = 0;
        DAT_8008d3d4 = 0;
        DAT_8008d494 = 0;
        DAT_8008d3e0 = 0;
        DAT_8008d424 = 0;
        DAT_8008d35c = 0;
        PsxRam.WriteU16(puVar1 + 0, 1);
        PsxRam.WriteI32(puVar1 + 0x10, (int)((uint)PsxRam.ReadI32(puVar1 + 0x10) | 0x8000a000));
    }

    // GHIDRA: RunBattleRound @ 0x80055F94 (VS.EXE)
    // STATE 1 — THE ROUND. 6476 bytes, the largest function in this slice by an order of magnitude,
    // and the only one that runs every frame for the length of a match.
    //
    // THE MAP — the phases in evaluation order, with the address each opens at.
    //
    //   0x80055FBC  the VM suspend gate: set -> RunBattleManagerFrame + UpdateCentralGaugeBar and NOTHING else
    //   0x80055FE4  the central gauge: count both teams, scale, accumulate  (skipped when pegged)
    //   0x8005625C  the +/-30000 clamps, high then low
    //   0x80056290  the pegged-gauge arm: raise 0x80000, and 8 as well when no slot is still live
    //   0x80056358  the two pad overrides on the flag word, gated by 0x80008000 == 0x8000
    //   0x800563D8  THE THREE-WAY on the flag word:
    //                 bit 3 clear  -> 0x80056820  the round body, itself two arms on bit 14
    //                                   0x800569A0  bit 14 clear: legality sweep + cursor repair
    //                                   0x80056834  bit 14 set:   ki refill sweep
    //                 bit 2 clear  -> 0x800563F8  the round ENDS: create the list-12 scene task
    //                 bit 1 set    -> 0x800565FC  the wind-down, on the +0x06 countdown
    //   0x80056C64  LAB_80056c64, the knock-out timer arm on bit 28
    //   0x80056EB4  the +0x02 countdown and what its expiry does to bits 22/21/20
    //   0x80056F78  the per-slot command echo: fighter +0x138 -> record flags + an 8-frame timer
    //   0x80057064  the targeting block, pad-driven or automatic on DAT_801FF100
    //   0x8005769C  the legality sweep that drags a dead target back onto the opposing cursor
    //   0x80057794  RunBattleManagerFrame + UpdateCentralGaugeBar, the two that run on EVERY path
    //   0x800577C4  the 0x8000000 acknowledgement, and the two 0xFFFC globals it writes
    //   0x80057888  zero all twelve gauge contributions
    //
    // THE LOOPS ARE ALL WRITTEN THE SAME WAY by the original compiler: a counter is multiplied by
    // 0x10000 and shifted back down by 0x10, which is how the C source's `short` induction variable
    // survives into the object code. Every one of them is reproduced in that form rather than
    // rewritten as a plain int loop, because the truncation is what bounds them.
    //
    // ONE STRUCTURAL DEPARTURE, and it is forced by C#. The original has two `goto`s. The one to
    // LAB_80056c64 jumps FORWARD out of the three-way and is written as a `goto` below, which C#
    // allows. The one to LAB_800561d4 jumps INTO the sibling arm of an if/else, which C# forbids;
    // that arm is a single `iVar6 = 0` shared by all three paths, so the if/else is written as
    // if / else-if / else with the assignment repeated. Same graph, same order, no merged branch.
    private static void RunBattleRound()
    {
        bool bVar1;
        sbyte cVar2;
        ushort uVar3;
        ushort uVar4;
        ushort uVar5;
        int iVar6;
        short sVar7 = 0;
        int iVar8;
        uint uVar9;
        short sVar10 = 0;
        uint uVar11;
        int iVar12;
        int uVar13;
        int iVar14;
        int iVar15;

        iVar15 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);

            // 0x80055FBC — the animation VM's suspend gate, the same `if ((DAT_800b305a & 1) == 0)`
        // every one of the fifty-one opcode handlers opens with. When it is up the manager still
        // runs its last two callees and returns: the frame is frozen, but whatever those two do is
        // not. The symbol is AnimVm's; it is read here, not redeclared.
        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            RunBattleManagerFrame(iVar15);
            UpdateCentralGaugeBar(iVar15);
            return;
        }

        // 0x80055FE4 — THE CENTRAL GAUGE. Three conditions guard the whole block: none of the four
        // flag bits 0x18000008 may be up, and the gauge must be on neither bound. `sVar7 = 0` sits
        // inside the original's condition as a comma operator, evaluated only once the first two
        // hold; that is why it is written between the two tests here rather than above them.
        if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x18000008) == 0
            && PsxRam.ReadI32(iVar15 + BattleState.CtxCentralGauge) != BattleState.CtxCentralGaugeLimit)
        {
            sVar7 = 0;
            if (PsxRam.ReadI32(iVar15 + BattleState.CtxCentralGauge) != -BattleState.CtxCentralGaugeLimit)
            {
                // Count the slots of each team whose record carries bit 0x200. sVar7 is team
                // 0..5, sVar10 is team 6..11.
                iVar14 = 0;
                iVar6 = 0;
                do
                {
                    iVar14 = iVar14 + 1;
                    if ((PsxRam.ReadU16(iVar15 + ((iVar6 >> 0x10) * BattleState.CtxSlotRecordStride)
                                        + BattleState.CtxSlotRecords) & 0x200) != 0)
                    {
                        sVar7 = (short)(sVar7 + 1);
                    }

                    iVar6 = iVar14 * 0x10000;
                } while (iVar14 * 0x10000 >> 0x10 < 6);

                sVar10 = 0;
                iVar14 = 6;
                iVar6 = 0x60000;
                do
                {
                    iVar14 = iVar14 + 1;
                    if ((PsxRam.ReadU16(iVar15 + ((iVar6 >> 0x10) * BattleState.CtxSlotRecordStride)
                                        + BattleState.CtxSlotRecords) & 0x200) != 0)
                    {
                        sVar10 = (short)(sVar10 + 1);
                    }

                    iVar6 = iVar14 * 0x10000;
                } while (iVar14 * 0x10000 >> 0x10 < 0xc);

                // THE HANDICAP. Equal counts scale nothing. Otherwise the team with MORE marked
                // slots has every one of its contributions multiplied by a numerator and shifted
                // right by 2, i.e. scaled by n/4: 5/4 or 8/4 when the bigger team has exactly 3,
                // and 6/4 in every other case. The `+3` before the shift is the compiler's rounding
                // fix-up for a negative dividend and is kept.
                if (sVar7 == sVar10)
                {
                    // LAB_800561d4
                    iVar6 = 0;
                }
                else if (sVar7 <= sVar10)
                {
                    if (sVar10 == 3)
                    {
                        sVar10 = 5;
                        if (sVar7 == 2)
                        {
                            sVar10 = 8;
                        }
                    }
                    else
                    {
                        sVar10 = 6;
                    }

                    iVar14 = 0;
                    iVar6 = 0;
                    do
                    {
                        iVar8 = iVar15 + ((iVar6 >> 0x10) * BattleState.CtxSlotRecordStride);
                        iVar6 = (short)PsxRam.ReadU16(iVar8 + BattleState.CtxGaugeContribution) * sVar10;
                        if (iVar6 < 0)
                        {
                            iVar6 = iVar6 + 3;
                        }

                        PsxRam.WriteU16(iVar8 + BattleState.CtxGaugeContribution, (ushort)(short)(iVar6 >> 2));
                        iVar14 = iVar14 + 1;
                        iVar6 = iVar14 * 0x10000;
                    } while (iVar14 * 0x10000 >> 0x10 < 6);

                    // goto LAB_800561d4
                    iVar6 = 0;
                }
                else
                {
                    if (sVar7 == 3)
                    {
                        sVar7 = 5;
                        if (sVar10 == 2)
                        {
                            sVar7 = 8;
                        }
                    }
                    else
                    {
                        sVar7 = 6;
                    }

                    iVar14 = 6;
                    iVar6 = 0x60000;
                    do
                    {
                        iVar8 = iVar15 + ((iVar6 >> 0x10) * BattleState.CtxSlotRecordStride);
                        iVar6 = (short)PsxRam.ReadU16(iVar8 + BattleState.CtxGaugeContribution) * sVar7;
                        if (iVar6 < 0)
                        {
                            iVar6 = iVar6 + 3;
                        }

                        PsxRam.WriteU16(iVar8 + BattleState.CtxGaugeContribution, (ushort)(short)(iVar6 >> 2));
                        iVar14 = iVar14 + 1;
                        iVar6 = iVar14 * 0x10000;
                    } while (iVar14 * 0x10000 >> 0x10 < 0xc);

                    iVar6 = 0;
                }

                // THE ACCUMULATION, and the whole shape of the bar: slots 0..5 push the gauge up,
                // slots 6..11 pull it down. The contribution is read as a SIGNED halfword and the
                // gauge is a word.
                do
                {
                    sVar7 = (short)iVar6;
                    iVar6 = iVar6 + 1;
                    PsxRam.WriteI32(iVar15 + BattleState.CtxCentralGauge,
                        (short)PsxRam.ReadU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                                              + BattleState.CtxGaugeContribution)
                        + PsxRam.ReadI32(iVar15 + BattleState.CtxCentralGauge));
                } while (iVar6 * 0x10000 >> 0x10 < 6);

                iVar6 = 6;
                do
                {
                    sVar7 = (short)iVar6;
                    iVar6 = iVar6 + 1;
                    PsxRam.WriteI32(iVar15 + BattleState.CtxCentralGauge,
                        PsxRam.ReadI32(iVar15 + BattleState.CtxCentralGauge)
                        - (short)PsxRam.ReadU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                                                + BattleState.CtxGaugeContribution));
                } while (iVar6 * 0x10000 >> 0x10 < 0xc);
            }
        }

        // 0x8005625C / 0x80056274 — THE +/-30000 CLAMPS. Two independent tests, not an else pair.
        // The low one writes 0xFFFF8AD0, which is -30000.
        if (BattleState.CtxCentralGaugeLimit < PsxRam.ReadI32(iVar15 + BattleState.CtxCentralGauge))
        {
            PsxRam.WriteI32(iVar15 + BattleState.CtxCentralGauge, BattleState.CtxCentralGaugeLimit);
        }

        if (PsxRam.ReadI32(iVar15 + BattleState.CtxCentralGauge) < -BattleState.CtxCentralGaugeLimit)
        {
            PsxRam.WriteI32(iVar15 + BattleState.CtxCentralGauge, -BattleState.CtxCentralGaugeLimit);
        }

        // 0x80056290 — the gauge has reached a bound. Raise 0x80000, and then, only while
        // DAT_8008d458 is still zero, count the slots that are live-but-idle (record bit 0 set and
        // the halfword at record+2 zero) across ALL TWELVE; finding none raises bit 3 as well, and
        // bit 3 is what the three-way below reads as "the round is over".
        uVar11 = (uint)PsxRam.ReadI32(iVar15 + 0x10);
        if ((uVar11 & 0x18000008) == 0
            && (PsxRam.ReadI32(iVar15 + BattleState.CtxCentralGauge) == BattleState.CtxCentralGaugeLimit
                || PsxRam.ReadI32(iVar15 + BattleState.CtxCentralGauge) == -BattleState.CtxCentralGaugeLimit))
        {
            bVar1 = DAT_8008d458 == 0;
            PsxRam.WriteI32(iVar15 + 0x10, (int)(uVar11 | 0x80000));
            if (bVar1)
            {
                sVar7 = 0;
                iVar14 = 0;
                iVar6 = 0;
                do
                {
                    iVar6 = iVar15 + ((iVar6 >> 0x10) * BattleState.CtxSlotRecordStride);
                    if ((PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 1) != 0
                        && (short)PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords + 2) == 0)
                    {
                        sVar7 = (short)(sVar7 + 1);
                    }

                    iVar14 = iVar14 + 1;
                    iVar6 = iVar14 * 0x10000;
                } while (iVar14 * 0x10000 >> 0x10 < 0xc);

                if (sVar7 == 0)
                {
                    PsxRam.WriteI32(iVar15 + 0x10, (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) | 8));
                }
            }

            uVar11 = (uint)PsxRam.ReadI32(iVar15 + 0x10);
        }

        // 0x80056358 — the two pad overrides. Both arms clear bits 12 and 13 and TOGGLE bit 14, so
        // pressing the button flips the round body between its two arms. The second is gated on
        // port 2 and on DAT_801FF100 saying the second side is human.
        //
        // THE GATE USED TO BE INVERTED HERE, and the comment that used to sit on this line argued
        // for the inversion. Ghidra renders the test as
        // `(undefined *)(uVar10 & 0x80008000) == &DAT_80008000`, and the earlier reading took
        // `&DAT_80008000` for the constant 0x8000 -- "bit 15 up and bit 31 down". It is not: it is
        // the ADDRESS 0x80008000, which is how Ghidra spells a bare 0x80008000 when it decides the
        // value looks like a pointer. The image settles it, at 0x8005634C..0x8005635C:
        //
        //     lui v1,0x8000        v1 = 0x80000000
        //     lw  a0,0x10(s2)      a0 = CtxFlags
        //     ori v1,v1,0x8000     v1 = 0x80008000
        //     and v0,a0,v1
        //     bne v0,v1,0x800563d8     skip unless (CtxFlags & 0x80008000) == 0x80008000
        //
        // BOTH bits must be up, and FUN_80055EE0 sets both at arming (`| 0x8000a000`), so on the
        // console this gate is OPEN for the whole round and the port had it shut for the whole
        // round. This is the "inverted arm whose header comment describes the inversion" defect the
        // repository's own notes list, caught by reading the bytes rather than the prose.
        if ((uVar11 & 0x80008000) == 0x80008000 && (uVar11 & 0x18000008) == 0)
        {
            if ((PadInput.g_PadNewlyPressed[0] & 0x800) != 0)
            {
                PsxRam.WriteI32(iVar15 + 0x10, (int)((uVar11 & 0xffffcfff) ^ 0x4000));
            }

            if (SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] == 0
                && (PadInput.g_PadNewlyPressed[1] & 0x800) != 0)
            {
                PsxRam.WriteI32(iVar15 + 0x10,
                    (int)(((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0xffffcfff) ^ 0x4000));
            }
        }

        uVar11 = (uint)PsxRam.ReadI32(iVar15 + 0x10);

        // 0x800563D8 — THE THREE-WAY. Bit 3 is the one the pegged-gauge arm above raises when no
        // slot is still live, so it reads as "the round is over"; bit 2 is raised by the arm it
        // selects, as part of the 0xC04 that arm ORs in, so it reads as "the scene task already
        // exists". PARTIAL for the third: bit 1 is not set by any of the four functions in this
        // slice, so what arms the wind-down is outside it. The order of the tests is the
        // original's.
        if ((uVar11 & 8) == 0)
        {
            if ((uVar11 & 0x4000) == 0)
            {
                if ((uVar11 & 0x1000) != 0)
                {
                    goto LAB_80056c64;
                }

                // 0x800569A0 — THE LEGALITY SWEEP. For all twelve slots: clear record bits 5..7,
                // then, only while the parallel table at +0x2C14 has bit 0x400 up for that slot,
                // copy bit 0x200 down to bit 0x80, clear 0x200, and re-raise 0x280 when the slot is
                // live and the +0x2C14 entry agrees — bit 0x80 for slots 0..5, bit 0x100 for slots
                // 6..11. Bits 0 and 7 together are what every cursor below tests for.
                iVar14 = 0;
                iVar6 = 0;
                do
                {
                    iVar6 = iVar6 >> 0x10;
                    iVar8 = iVar15 + (iVar6 * BattleState.CtxSlotRecordStride);
                    uVar5 = PsxRam.ReadU16(iVar8 + BattleState.CtxSlotRecords);
                    iVar12 = (iVar6 * 2) + iVar15;
                    uVar4 = (ushort)(uVar5 & 0xff1f);
                    PsxRam.WriteU16(iVar8 + BattleState.CtxSlotRecords, uVar4);
                    if ((PsxRam.ReadU16(iVar12 + 0x2c14) & 0x400) != 0)
                    {
                        if ((uVar5 & 0x200) != 0)
                        {
                            PsxRam.WriteU16(iVar8 + BattleState.CtxSlotRecords, (ushort)(uVar4 | 0x80));
                        }

                        uVar5 = PsxRam.ReadU16(iVar8 + BattleState.CtxSlotRecords);
                        uVar4 = (ushort)(uVar5 & 0xfdff);
                        PsxRam.WriteU16(iVar8 + BattleState.CtxSlotRecords, uVar4);
                        if ((uVar5 & 1) != 0)
                        {
                            if (iVar6 < 6)
                            {
                                if ((PsxRam.ReadU16(iVar12 + 0x2c14) & 0x80) == 0)
                                {
                                    // LAB_80056a4c
                                    PsxRam.WriteU16(iVar8 + BattleState.CtxSlotRecords, (ushort)(uVar4 | 0x280));
                                }
                            }
                            else if ((PsxRam.ReadU16(iVar12 + 0x2c14) & 0x100) != 0)
                            {
                                // LAB_80056a4c
                                PsxRam.WriteU16(iVar8 + BattleState.CtxSlotRecords, (ushort)(uVar4 | 0x280));
                            }
                        }
                    }

                    iVar14 = iVar14 + 1;
                    iVar6 = iVar14 * 0x10000;
                } while (iVar14 * 0x10000 >> 0x10 < 0xc);

                // CURSOR REPAIR. If the slot a cursor points at is no longer legal — bit 0 down, or
                // bit 7 down — scan that cursor's own half for the first slot that is, and move it
                // there. Finding none, the cursor is left where it was.
                uVar5 = PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x14)
                                                 * BattleState.CtxSlotRecordStride)
                                       + BattleState.CtxSlotRecords);
                uVar4 = 0;
                if ((uVar5 & 1) != 0)
                {
                    uVar4 = (ushort)(uVar5 >> 7 & 1);
                }

                if (uVar4 == 0)
                {
                    sVar7 = 0;
                    do
                    {
                        uVar5 = PsxRam.ReadU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                        if ((uVar5 & 1) != 0 && (uVar5 & 0x80) != 0)
                        {
                            PsxRam.WriteU16(iVar15 + 0x14, (ushort)sVar7);
                            break;
                        }

                        sVar7 = (short)(sVar7 + 1);
                    } while (sVar7 < 6);
                }

                uVar5 = PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x16)
                                                 * BattleState.CtxSlotRecordStride)
                                       + BattleState.CtxSlotRecords);
                uVar4 = 0;
                if ((uVar5 & 1) != 0)
                {
                    uVar4 = (ushort)(uVar5 >> 7 & 1);
                }

                if (uVar4 == 0)
                {
                    sVar7 = 6;
                    do
                    {
                        uVar5 = PsxRam.ReadU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                        if ((uVar5 & 1) != 0 && (uVar5 & 0x80) != 0)
                        {
                            PsxRam.WriteU16(iVar15 + 0x16, (ushort)sVar7);
                            break;
                        }

                        sVar7 = (short)(sVar7 + 1);
                    } while (sVar7 < 0xc);
                }

                // Reach into every fighter that exists and drop bit 25 of its +0x138 — the very bit
                // VS_EXE/FighterTask.cs reads at its step 9.3 to suppress the frame's command word.
                // The twelve-slot walk visits the six zero slots and skips them.
                if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x80008000) == 0x8000)
                {
                    iVar14 = 0;
                    iVar6 = 0;
                    do
                    {
                        iVar6 = PsxRam.ReadI32((iVar6 >> 0xe) + iVar15 + BattleState.CtxFighterSlots);
                        if (iVar6 != 0)
                        {
                            iVar6 = PsxRam.ReadI32(iVar6 + 8);
                            PsxRam.WriteI32(iVar6 + 0x138,
                                (int)((uint)PsxRam.ReadI32(iVar6 + 0x138) & 0xfdffffff));
                        }

                        iVar14 = iVar14 + 1;
                        iVar6 = iVar14 * 0x10000;
                    } while (iVar14 * 0x10000 >> 0x10 < 0xc);
                }

                PsxRam.WriteU16(iVar15 + 2, 0x10);
                PsxRam.WriteI32(iVar15 + 0x10,
                    (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0xffff7fff | 0x1c00));
                uVar9 = (uint)PsxRam.ReadI32(iVar15 + 0x10) & 0xffcfffff;
                PsxRam.WriteI32(iVar15 + 0x2d60, (int)((uint)PsxRam.ReadI32(iVar15 + 0x2d60) | 3));
                uVar11 = 0x400000;
            }
            else
            {
                if ((uVar11 & 0x2000) != 0)
                {
                    goto LAB_80056c64;
                }

                // 0x80056834 — THE KI REFILL. Six slots only, 0..5, walking the +0x2C14 table: an
                // entry with bit 0x200 up has bit 3 cleared and then re-set when bit 7 is up, and
                // each time bit 3 goes up the slot's ki gauge is slammed to its 16000 cap. The
                // second, longer condition refills a slot whose +0x2C14 entry has BOTH 0x400 and
                // 0x800 while its own record does not yet carry 0x200.
                iVar14 = 0;
                iVar6 = 0;
                do
                {
                    iVar6 = iVar6 >> 0x10;
                    iVar8 = (iVar6 * 2) + iVar15;
                    uVar5 = PsxRam.ReadU16(iVar8 + 0x2c14);
                    if ((uVar5 & 0x200) != 0)
                    {
                        PsxRam.WriteU16(iVar8 + 0x2c14, (ushort)(uVar5 & 0xfff7));
                        if ((uVar5 & 0x80) != 0)
                        {
                            PsxRam.WriteU16(iVar8 + 0x2c14, (ushort)(uVar5 & 0xfff7 | 8));
                            PsxRam.WriteU16(iVar15 + (iVar6 * BattleState.CtxSlotRecordStride)
                                            + BattleState.CtxKiGauge,
                                (ushort)BattleState.CtxKiGaugeCap);
                        }

                        if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x8000) != 0
                            && (PsxRam.ReadU16(iVar8 + 0x2c14) & 0xc00) == 0xc00)
                        {
                            iVar6 = iVar15 + (iVar6 * BattleState.CtxSlotRecordStride);
                            if ((PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0x200) == 0)
                            {
                                PsxRam.WriteU16(iVar8 + 0x2c14,
                                    (ushort)(PsxRam.ReadU16(iVar8 + 0x2c14) | 8));
                                PsxRam.WriteU16(iVar6 + BattleState.CtxKiGauge,
                                    (ushort)BattleState.CtxKiGaugeCap);
                            }
                        }
                    }

                    iVar14 = iVar14 + 1;
                    iVar6 = iVar14 * 0x10000;
                } while (iVar14 * 0x10000 >> 0x10 < 6);

                iVar6 = 0;
                do
                {
                    iVar14 = iVar15 + ((short)iVar6 * BattleState.CtxSlotRecordStride);
                    iVar6 = iVar6 + 1;
                    PsxRam.WriteU16(iVar14 + BattleState.CtxSlotRecords,
                        (ushort)(PsxRam.ReadU16(iVar14 + BattleState.CtxSlotRecords) & 0xff1f));
                } while (iVar6 * 0x10000 >> 0x10 < 0xc);

                PsxRam.WriteU16(iVar15 + 2, 0x10);
                PsxRam.WriteI32(iVar15 + 0x10, (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) | 0x2c00));
                uVar9 = (uint)PsxRam.ReadI32(iVar15 + 0x10) & 0xff8fffff;
                PsxRam.WriteU16(iVar15 + 0x2dca, (ushort)(PsxRam.ReadU16(iVar15 + 0x2dca) | 2));
                uVar11 = 0x40000000;
            }

            PsxRam.WriteI32(iVar15 + 0x10, (int)(uVar9 | uVar11));
            FUN_8005ee5c(-1, -1, 0x10);
        }
        else if ((uVar11 & 4) == 0)
        {
            // 0x800563F8 — THE ROUND IS OVER. This is where the SCENE TASK is born: id 0x50 on LIST
            // 12, 0x7C bytes of workspace, entry LAB_80034eac, inserted at g_TaskListTail[12]. It is
            // created ONCE — the same flag bit 2 this arm tests is raised inside it — and everything
            // that follows is conditional on CreateTask having succeeded.
            //
            // THE REGISTRATION IS THIS SLICE'S OBLIGATION, and VS_EXE/BattleScene.cs states it in
            // so many words: it exposes RegisterBattleSceneTask rather than performing it, because
            // the creator of its task is this function and not that file. Without the call, list 12
            // walks a live node every frame and dispatches nothing. It is idempotent, and it sits
            // immediately before the CreateTask exactly as PrimitivePools.CreatePrimitivePools
            // places its own.
            DiagSceneCreateAttempts++;
            BattleScene.RegisterBattleSceneTask();
            iVar6 = TaskSystem.CreateTask(BattleScene.BattleSceneEntry, 0x50, 0xc, 0x7c, 0,
                TaskSystem.g_TaskListTail[12]);
            if (iVar6 != 0)
            {
                uVar13 = 0x30;
                if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x4000) == 0)
                {
                    uVar13 = 0x28;
                }

                iVar14 = 0;
                FUN_8005ee5c(0, 0, uVar13);
                FUN_8005ef20(0, 0);

                // THE WINNER, and it is decided by the sign of the gauge alone: FUN_8005cf78
                // returns cursor +0x14 when the gauge sits at exactly +30000 and cursor +0x16
                // otherwise. Stored twice, at +0x1A and +0x2DC2.
                uVar3 = FUN_8005cf78(iVar15);
                PsxRam.WriteU16(iVar15 + 0x1a, uVar3);
                PsxRam.WriteU16(iVar15 + 0x2dc2, uVar3);

                // Every slot marked 0x200 has bit 25 raised in its fighter's +0x134 — the bit
                // FighterTask's phase 7 tests to divert the whole fighter into FUN_800501b8.
                iVar6 = 0;
                do
                {
                    if ((PsxRam.ReadU16(iVar15 + ((iVar6 >> 0x10) * BattleState.CtxSlotRecordStride)
                                        + BattleState.CtxSlotRecords) & 0x200) != 0)
                    {
                        iVar6 = PsxRam.ReadI32(((iVar6 >> 0x10) * 4) + iVar15
                                               + BattleState.CtxFighterSlots);
                        if (iVar6 != 0)
                        {
                            iVar6 = PsxRam.ReadI32(iVar6 + 8);
                            PsxRam.WriteI32(iVar6 + 0x134,
                                (int)((uint)PsxRam.ReadI32(iVar6 + 0x134) | 0x2000000));
                        }
                    }

                    iVar14 = iVar14 + 1;
                    iVar6 = iVar14 * 0x10000;
                } while (iVar14 * 0x10000 >> 0x10 < 0xc);

                if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x1000) != 0)
                {
                    iVar6 = 0;
                    do
                    {
                        iVar14 = iVar15 + ((short)iVar6 * BattleState.CtxSlotRecordStride);
                        iVar6 = iVar6 + 1;
                        PsxRam.WriteU16(iVar14 + BattleState.CtxSlotRecords,
                            (ushort)(PsxRam.ReadU16(iVar14 + BattleState.CtxSlotRecords) & 0xff7f));
                    } while (iVar6 * 0x10000 >> 0x10 < 0xc);
                }

                iVar6 = 0;
                if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x2000) != 0)
                {
                    iVar14 = 0;
                    do
                    {
                        iVar14 = (iVar14 >> 0xf) + iVar15;
                        uVar5 = PsxRam.ReadU16(iVar14 + 0x2c14);
                        uVar4 = (ushort)(uVar5 | 1);
                        if ((uVar5 & 8) == 0)
                        {
                            uVar4 = (ushort)(uVar5 & 0xfffe);
                        }

                        PsxRam.WriteU16(iVar14 + 0x2c14, uVar4);
                        iVar6 = iVar6 + 1;
                        iVar14 = iVar6 * 0x10000;
                    } while (iVar6 * 0x10000 >> 0x10 < 0xc);
                }

                iVar6 = 0;
                PsxRam.WriteI32(iVar15 + 0x10, (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) | 0xc04));
                do
                {
                    sVar7 = (short)iVar6;
                    iVar6 = iVar6 + 1;
                    PsxRam.WriteU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride) + 0x15ba, 0);
                } while (iVar6 * 0x10000 >> 0x10 < 0xc);

                PsxRam.WriteU16(iVar15 + 0x2dca, (ushort)(PsxRam.ReadU16(iVar15 + 0x2dca) | 2));
            }
        }
        else if ((uVar11 & 2) != 0)
        {
            // 0x800565FC — THE WIND-DOWN, driven by the halfword countdown at +0x06. It does its
            // work on the FIRST tick it sees (+0x06 == 0xF), coasts while the counter falls, and
            // does a second, smaller pass on the LAST tick (+0x06 == 0). Note which half of the
            // roster each pass touches: the 0xF pass walks slots 0..5, the 0 pass walks 6..11.
            iVar6 = 0;
            if ((short)PsxRam.ReadU16(iVar15 + 6) == 0xf)
            {
                iVar14 = 0;
                do
                {
                    iVar14 = PsxRam.ReadI32((iVar14 >> 0xe) + iVar15 + BattleState.CtxFighterSlots);
                    if (iVar14 != 0)
                    {
                        iVar14 = PsxRam.ReadI32(iVar14 + 8);
                        PsxRam.WriteI32(iVar14 + 0x134,
                            (int)((uint)PsxRam.ReadI32(iVar14 + 0x134) & 0xf9ffffff));
                    }

                    iVar6 = iVar6 + 1;
                    iVar14 = iVar6 * 0x10000;
                } while (iVar6 * 0x10000 >> 0x10 < 6);

                if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x1000) != 0)
                {
                    iVar14 = 0;
                    iVar6 = 0;
                    do
                    {
                        iVar6 = iVar15 + ((iVar6 >> 0x10) * BattleState.CtxSlotRecordStride);
                        uVar5 = PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords);
                        if ((uVar5 & 0x200) != 0)
                        {
                            PsxRam.WriteU16(iVar6 + BattleState.CtxSlotRecords, (ushort)(uVar5 | 0x80));
                        }

                        iVar14 = iVar14 + 1;
                        iVar6 = iVar14 * 0x10000;
                    } while (iVar14 * 0x10000 >> 0x10 < 0xc);
                }

                if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x2000) != 0)
                {
                    iVar14 = 0;
                    iVar6 = 0;
                    do
                    {
                        iVar6 = (iVar6 >> 0xf) + iVar15;
                        uVar5 = PsxRam.ReadU16(iVar6 + 0x2c14);
                        if ((uVar5 & 1) != 0)
                        {
                            PsxRam.WriteU16(iVar6 + 0x2c14, (ushort)(uVar5 | 8));
                        }

                        iVar14 = iVar14 + 1;
                        iVar6 = iVar14 * 0x10000;
                    } while (iVar14 * 0x10000 >> 0x10 < 0xc);
                }

                // THE GAUGE IS RESET TO ZERO HERE — the only place in the slice that writes it a
                // value that is neither an accumulation nor a clamp.
                PsxRam.WriteI32(iVar15 + BattleState.CtxCentralGauge, 0);
                DAT_8008d3a8 = 0;
                PsxRam.WriteI32(iVar15 + 0x10, (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0xfff8ffff));
                PsxRam.WriteI32(iVar15 + 0x10, (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) | 0xc00));

                // The winner stored at +0x2DC2 compared against the slot at +0x18. The original
                // compares an UNSIGNED halfword against a SIGNED one, which in C promotes the signed
                // side to unsigned; that promotion is reproduced rather than smoothed away.
                if ((uint)PsxRam.ReadU16(iVar15 + 0x2dc2)
                    == (uint)(int)(short)PsxRam.ReadU16(iVar15 + 0x18))
                {
                    DAT_8008d3e4 = 0x80;
                    DAT_8008d3a0 = 0x80;
                    DAT_8008d3ec = 0;
                    DAT_8008d57c = 0;
                }

                FUN_8005d1f4();
                DAT_8008d428 = PsxRam.ReadI32(iVar15 + BattleState.CtxCentralGauge);
                PsxRam.WriteU16(iVar15 + 6, (ushort)(short)((short)PsxRam.ReadU16(iVar15 + 6) - 1));
            }
            else
            {
                iVar6 = 6;
                if ((short)PsxRam.ReadU16(iVar15 + 6) == 0)
                {
                    iVar14 = 0x60000;
                    do
                    {
                        iVar14 = PsxRam.ReadI32((iVar14 >> 0xe) + iVar15 + BattleState.CtxFighterSlots);
                        if (iVar14 != 0)
                        {
                            iVar14 = PsxRam.ReadI32(iVar14 + 8);
                            PsxRam.WriteI32(iVar14 + 0x134,
                                (int)((uint)PsxRam.ReadI32(iVar14 + 0x134) & 0xf9ffffff));
                        }

                        iVar6 = iVar6 + 1;
                        iVar14 = iVar6 * 0x10000;
                    } while (iVar6 * 0x10000 >> 0x10 < 0xc);

                    PsxRam.WriteI32(iVar15 + 0x10,
                        (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0xfffffff1));
                }
                else
                {
                    PsxRam.WriteU16(iVar15 + 6, (ushort)(short)((short)PsxRam.ReadU16(iVar15 + 6) - 1));
                }
            }
        }

    LAB_80056c64:

        // 0x80056C64 — THE KNOCK-OUT TIMER, on flag bit 28. A slot marked 0x1000 is one that has
        // just gone down. The first such slot found while the timer at +0x2D64 is zero starts it at
        // 0x80 and clears its own mark; on every later frame the timer counts down and, on the way,
        //   * flips bit 27 of that fighter's +0x138 while the timer is in 5..0x17 — the very bit
        //     FighterTask's step 9.7 tests to skip its last five callees, so the fighter is being
        //     strobed on and off;
        //   * adds 0x20 to each of the three bytes at fighter+0x150..0x152 while the timer is below
        //     0x11, wrapping as a signed char — three equal channels, which reads as a fade;
        //   * fires FUN_80042054(5, 0x10) at 0x0E and FUN_80042054(4, 0x40) at 0x04, the second
        //     also forcing bit 27 back on.
        // When no slot carries 0x1000 any more the arm raises bit 29 and clears the timer.
        if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x10000000) != 0)
        {
            iVar14 = 0;
            iVar6 = 0;
            sVar7 = 0;
            do
            {
                iVar6 = iVar6 >> 0x10;
                iVar8 = iVar15 + (iVar6 * BattleState.CtxSlotRecordStride);
                uVar5 = PsxRam.ReadU16(iVar8 + BattleState.CtxSlotRecords);
                sVar10 = sVar7;
                if ((uVar5 & 0x1000) != 0)
                {
                    sVar10 = (short)(sVar7 + 1);
                    if ((short)PsxRam.ReadU16(iVar15 + 0x2d64) == 0)
                    {
                        if (PsxRam.ReadI32((iVar6 * 4) + iVar15 + BattleState.CtxFighterSlots) != 0)
                        {
                            PsxRam.WriteU16(iVar8 + BattleState.CtxSlotRecords, (ushort)(uVar5 & 0xefff));
                            if ((short)PsxRam.ReadU16(iVar15 + 0x18) == iVar6)
                            {
                                PsxRam.WriteI32(iVar15 + 0x10,
                                    (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) | 0x800000));
                            }

                            PsxRam.WriteU16(iVar15 + 0x2d64, 0x80);
                            sVar10 = sVar7;
                        }
                    }
                    else
                    {
                        // `(int)*(short *)(...) - 5U < 0x13` is an UNSIGNED comparison in the
                        // original, so it is the window 5..0x17 and an under-5 timer wraps out of
                        // it rather than entering it.
                        if ((uint)((int)(short)PsxRam.ReadU16(iVar15 + 0x2d64) - 5) < 0x13)
                        {
                            iVar6 = PsxRam.ReadI32((iVar6 * 4) + iVar15 + BattleState.CtxFighterSlots);
                            if (iVar6 != 0)
                            {
                                iVar6 = PsxRam.ReadI32(iVar6 + 8);
                                PsxRam.WriteI32(iVar6 + 0x138,
                                    (int)((uint)PsxRam.ReadI32(iVar6 + 0x138) ^ 0x8000000));
                            }
                        }

                        if ((short)PsxRam.ReadU16(iVar15 + 0x2d64) < 0x11)
                        {
                            iVar6 = PsxRam.ReadI32(((iVar14 << 0x10) >> 0xe) + iVar15
                                                   + BattleState.CtxFighterSlots);
                            if (iVar6 != 0)
                            {
                                iVar6 = PsxRam.ReadI32(iVar6 + 8);
                                cVar2 = (sbyte)((sbyte)PsxRam.ReadU8(iVar6 + 0x152) + 0x20);
                                PsxRam.WriteU8(iVar6 + 0x152, (byte)cVar2);
                                PsxRam.WriteU8(iVar6 + 0x151, (byte)cVar2);
                                PsxRam.WriteU8(iVar6 + 0x150, (byte)cVar2);
                            }
                        }

                        sVar7 = (short)PsxRam.ReadU16(iVar15 + 0x2d64);
                        if (sVar7 == 0xe)
                        {
                            BattleScene.FUN_80042054(5, 0x10);
                            sVar7 = (short)PsxRam.ReadU16(iVar15 + 0x2d64);
                        }

                        if (sVar7 == 4)
                        {
                            BattleScene.FUN_80042054(4, 0x40);
                            iVar6 = PsxRam.ReadI32(((iVar14 << 0x10) >> 0xe) + iVar15
                                                   + BattleState.CtxFighterSlots);
                            if (iVar6 != 0)
                            {
                                iVar6 = PsxRam.ReadI32(iVar6 + 8);
                                PsxRam.WriteI32(iVar6 + 0x138,
                                    (int)((uint)PsxRam.ReadI32(iVar6 + 0x138) | 0x8000000));
                            }
                        }

                        PsxRam.WriteU16(iVar15 + 0x2d64,
                            (ushort)(short)((short)PsxRam.ReadU16(iVar15 + 0x2d64) - 1));
                    }
                }

                iVar14 = iVar14 + 1;
                iVar6 = iVar14 * 0x10000;
                sVar7 = sVar10;
            } while (iVar14 * 0x10000 >> 0x10 < 0xc);

            if (sVar10 == 0)
            {
                PsxRam.WriteI32(iVar15 + 0x10, (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) | 0x20000000));
                PsxRam.WriteU16(iVar15 + 0x2d64, 0);
                if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x4000) == 0)
                {
                    iVar14 = 0;
                    iVar6 = 0;
                    do
                    {
                        iVar6 = PsxRam.ReadI32((iVar6 >> 0xe) + iVar15 + BattleState.CtxFighterSlots);
                        if (iVar6 != 0)
                        {
                            iVar6 = PsxRam.ReadI32(iVar6 + 8);
                            PsxRam.WriteI32(iVar6 + 0x138,
                                (int)((uint)PsxRam.ReadI32(iVar6 + 0x138) & 0xfdffffff));
                        }

                        iVar14 = iVar14 + 1;
                        iVar6 = iVar14 * 0x10000;
                    } while (iVar14 * 0x10000 >> 0x10 < 0xc);
                }
                else
                {
                    PsxRam.WriteI32(iVar15 + 0x10, (int)((uint)PsxRam.ReadI32(iVar15 + 0x10) | 0x8000));
                }
            }
        }

        // 0x80056EB4 — the +0x02 countdown. Only its EXPIRY does anything: bit 22 up becomes bit 20
        // with bit 22 cleared, bit 22 down becomes bit 21. Both of those bits were set by the
        // three-way above, which is what makes this a delayed acknowledgement of it.
        if ((short)PsxRam.ReadU16(iVar15 + 2) != 0)
        {
            PsxRam.WriteU16(iVar15 + 2, (ushort)(short)((short)PsxRam.ReadU16(iVar15 + 2) - 1));
            if ((short)PsxRam.ReadU16(iVar15 + 2) == 0)
            {
                uVar11 = (uint)PsxRam.ReadI32(iVar15 + 0x10);
                if ((uVar11 & 0x400000) == 0)
                {
                    uVar11 = uVar11 | 0x200000;
                }
                else
                {
                    uVar11 = uVar11 & 0xffbfffff | 0x100000;
                }

                PsxRam.WriteI32(iVar15 + 0x10, (int)uVar11);
            }
        }

        // 0x80056F78 — THE COMMAND ECHO. For every slot that holds a fighter, read that fighter's
        // +0x138 — the same word FighterTask's step 9.4 routes on — and mirror its two command
        // families into the slot's record: 0x200FF raises 0x2000, 0x7F00 raises 0x4000, and either
        // one arms an 8-frame timer at record+0x0E. When the timer runs out both bits are dropped.
        // The two ifs are not exclusive: a fighter with both families up sets the timer twice and
        // ends on 0x4000.
        iVar14 = 0;
        iVar6 = 0;
        do
        {
            iVar6 = iVar6 >> 0x10;
            iVar8 = PsxRam.ReadI32((iVar6 * 4) + iVar15 + BattleState.CtxFighterSlots);
            if (iVar8 != 0)
            {
                uVar11 = (uint)PsxRam.ReadI32(PsxRam.ReadI32(iVar8 + 8) + 0x138);
                if ((uVar11 & 0x200ff) != 0)
                {
                    iVar8 = iVar15 + (iVar6 * BattleState.CtxSlotRecordStride);
                    PsxRam.WriteU16(iVar8 + 0x15be, 8);
                    PsxRam.WriteU16(iVar8 + BattleState.CtxSlotRecords,
                        (ushort)(PsxRam.ReadU16(iVar8 + BattleState.CtxSlotRecords) & 0x9fff | 0x2000));
                }

                if ((uVar11 & 0x7f00) != 0)
                {
                    iVar8 = iVar15 + (iVar6 * BattleState.CtxSlotRecordStride);
                    PsxRam.WriteU16(iVar8 + 0x15be, 8);
                    PsxRam.WriteU16(iVar8 + BattleState.CtxSlotRecords,
                        (ushort)(PsxRam.ReadU16(iVar8 + BattleState.CtxSlotRecords) & 0x9fff | 0x4000));
                }

                iVar6 = iVar15 + (iVar6 * BattleState.CtxSlotRecordStride);
                if ((short)PsxRam.ReadU16(iVar6 + 0x15be) != 0)
                {
                    uVar11 = (uint)((int)(short)PsxRam.ReadU16(iVar6 + 0x15be) - 1);
                    PsxRam.WriteU16(iVar6 + 0x15be, (ushort)uVar11);
                    if ((uVar11 & 0xffff) == 0)
                    {
                        PsxRam.WriteU16(iVar6 + BattleState.CtxSlotRecords,
                            (ushort)(PsxRam.ReadU16(iVar6 + BattleState.CtxSlotRecords) & 0x9fff));
                    }
                }
            }

            iVar14 = iVar14 + 1;
            iVar6 = iVar14 * 0x10000;
        } while (iVar14 * 0x10000 >> 0x10 < 0xc);

        // 0x80057064 — THE TARGETING BLOCK, gated by flag bit 20 — one of the two bits the +0x02
        // countdown's expiry can raise, the one it picks when bit 22 was already up.
        //
        // DAT_801FF100, the word SELECT.EXE handed over, decides who drives which side: value 2
        // locks out the port-1 pad entirely, value 0 is the only one that gives port 2 its four
        // keys, and anything else routes team 6..8 through the automatic block at the end. That is
        // three different readings of one word in one function, and none of them is invented here.
        //
        // Each side has four keys: two move the ACTING cursor (+0x14 for slots 0..5, +0x16 for
        // 6..11) and two move the TARGET stored in that acting slot's own record at +0x15C0. Every
        // one of the eight loops skips slots that are not legal, spins at most seven times and
        // wraps within its own half of the roster — 0..5 or 6..11 — so a cursor never crosses into
        // the other team's block, and the target cursors deliberately DO: +0x15C0 of a slot in
        // 0..5 walks 6..11, and the reverse.
        if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x18000008) == 0
            && ((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x100000) != 0)
        {
            if (SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] != 2)
            {
                if ((PadInput.DAT_8008d3ac & 4) != 0)
                {
                    iVar6 = 0;
                    do
                    {
                        sVar7 = (short)((short)PsxRam.ReadU16(iVar15 + 0x14) + 1);
                        PsxRam.WriteU16(iVar15 + 0x14, (ushort)sVar7);
                        if (5 < sVar7)
                        {
                            PsxRam.WriteU16(iVar15 + 0x14, 0);
                        }

                        uVar5 = PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x14)
                                                         * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                    } while (((uVar5 & 1) == 0 || (uVar5 & 0x80) == 0)
                             && (iVar6 = iVar6 + 1) * 0x10000 >> 0x10 < 7);
                }

                if ((PadInput.DAT_8008d3ac & 1) != 0)
                {
                    iVar6 = 0;
                    do
                    {
                        uVar5 = (ushort)((short)PsxRam.ReadU16(iVar15 + 0x14) - 1);
                        PsxRam.WriteU16(iVar15 + 0x14, uVar5);
                        if ((int)((uint)uVar5 << 0x10) < 0)
                        {
                            PsxRam.WriteU16(iVar15 + 0x14, 5);
                        }

                        uVar5 = PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x14)
                                                         * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                    } while (((uVar5 & 1) == 0 || (uVar5 & 0x80) == 0)
                             && (iVar6 = iVar6 + 1) * 0x10000 >> 0x10 < 7);
                }

                if ((PadInput.DAT_8008d3ac & 8) != 0)
                {
                    iVar6 = 0;
                    sVar7 = (short)PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x14)
                                                            * BattleState.CtxSlotRecordStride)
                                                  + BattleState.CtxTargetIndex);
                    do
                    {
                        sVar7 = (short)(sVar7 + 1);
                        if (0xb < sVar7)
                        {
                            sVar7 = 6;
                        }

                        uVar5 = PsxRam.ReadU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                        if ((uVar5 & 1) != 0 && (uVar5 & 0x80) != 0)
                        {
                            PsxRam.WriteU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x14)
                                                      * BattleState.CtxSlotRecordStride)
                                            + BattleState.CtxTargetIndex, (ushort)sVar7);
                            break;
                        }

                        iVar6 = iVar6 + 1;
                    } while (iVar6 * 0x10000 >> 0x10 < 7);
                }

                if ((PadInput.DAT_8008d3ac & 2) != 0)
                {
                    iVar6 = 0;
                    sVar7 = (short)PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x14)
                                                            * BattleState.CtxSlotRecordStride)
                                                  + BattleState.CtxTargetIndex);
                    do
                    {
                        sVar7 = (short)(sVar7 - 1);
                        if (sVar7 < 6)
                        {
                            sVar7 = 0xb;
                        }

                        uVar5 = PsxRam.ReadU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                        if ((uVar5 & 1) != 0 && (uVar5 & 0x80) != 0)
                        {
                            PsxRam.WriteU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x14)
                                                      * BattleState.CtxSlotRecordStride)
                                            + BattleState.CtxTargetIndex, (ushort)sVar7);
                            break;
                        }

                        iVar6 = iVar6 + 1;
                    } while (iVar6 * 0x10000 >> 0x10 < 7);
                }
            }

            if (SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] == 0)
            {
                if ((PadInput.DAT_8008d3b0 & 8) != 0)
                {
                    iVar6 = 6;
                    do
                    {
                        sVar7 = (short)((short)PsxRam.ReadU16(iVar15 + 0x16) + 1);
                        PsxRam.WriteU16(iVar15 + 0x16, (ushort)sVar7);
                        if (0xb < sVar7)
                        {
                            PsxRam.WriteU16(iVar15 + 0x16, 6);
                        }

                        uVar5 = PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x16)
                                                         * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                    } while (((uVar5 & 1) == 0 || (uVar5 & 0x80) == 0)
                             && (iVar6 = iVar6 + 1) * 0x10000 >> 0x10 < 0xd);
                }

                if ((PadInput.DAT_8008d3b0 & 2) != 0)
                {
                    iVar6 = 6;
                    do
                    {
                        sVar7 = (short)((short)PsxRam.ReadU16(iVar15 + 0x16) - 1);
                        PsxRam.WriteU16(iVar15 + 0x16, (ushort)sVar7);
                        if (sVar7 < 6)
                        {
                            PsxRam.WriteU16(iVar15 + 0x16, 0xb);
                        }

                        uVar5 = PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x16)
                                                         * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                    } while (((uVar5 & 1) == 0 || (uVar5 & 0x80) == 0)
                             && (iVar6 = iVar6 + 1) * 0x10000 >> 0x10 < 0xd);
                }

                if ((PadInput.DAT_8008d3b0 & 4) != 0)
                {
                    iVar6 = 0;
                    sVar7 = (short)PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x16)
                                                            * BattleState.CtxSlotRecordStride)
                                                  + BattleState.CtxTargetIndex);
                    do
                    {
                        sVar7 = (short)(sVar7 + 1);
                        if (5 < sVar7)
                        {
                            sVar7 = 0;
                        }

                        uVar5 = PsxRam.ReadU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                        if ((uVar5 & 1) != 0 && (uVar5 & 0x80) != 0)
                        {
                            PsxRam.WriteU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x16)
                                                      * BattleState.CtxSlotRecordStride)
                                            + BattleState.CtxTargetIndex, (ushort)sVar7);
                            break;
                        }

                        iVar6 = iVar6 + 1;
                    } while (iVar6 * 0x10000 >> 0x10 < 7);
                }

                if ((PadInput.DAT_8008d3b0 & 1) != 0)
                {
                    iVar6 = 0;
                    uVar5 = PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x16)
                                                     * BattleState.CtxSlotRecordStride)
                                           + BattleState.CtxTargetIndex);
                    do
                    {
                        uVar5 = (ushort)(uVar5 - 1);
                        if ((int)((uint)uVar5 << 0x10) < 0)
                        {
                            uVar5 = 5;
                        }

                        uVar4 = PsxRam.ReadU16(iVar15 + ((short)uVar5 * BattleState.CtxSlotRecordStride)
                                               + BattleState.CtxSlotRecords);
                        if ((uVar4 & 1) != 0 && (uVar4 & 0x80) != 0)
                        {
                            PsxRam.WriteU16(iVar15 + ((short)PsxRam.ReadU16(iVar15 + 0x16)
                                                      * BattleState.CtxSlotRecordStride)
                                            + BattleState.CtxTargetIndex, uVar5);
                            break;
                        }

                        iVar6 = iVar6 + 1;
                    } while (iVar6 * 0x10000 >> 0x10 < 7);
                }
            }
            else
            {
                // 0x800575C0 — THE AUTOMATIC SIDE. For each of slots 6, 7 and 8 in turn: scan slots
                // 0, 1, 2 and take the FIRST one whose own +0x15C0 already points back at that
                // scanned index — a mutual lock — provided it is legal. Failing all three, the loop
                // leaves uVar5 == 3 and the slot falls back to the fixed target sVar7 - 6, i.e.
                // slot 6 aims at 0, 7 at 1, 8 at 2.
                //
                // Note the comparison: `*(short *)(iVar14 + 0x15c0) == iVar6 >> 0x10` reads the
                // target of the SCANNED slot 0..2, not of the slot being assigned. Slots 9, 10 and
                // 11 are never given a target here.
                sVar7 = 6;
                do
                {
                    uVar5 = 0;
                    iVar6 = 0;
                    do
                    {
                        iVar14 = iVar15 + ((iVar6 >> 0x10) * BattleState.CtxSlotRecordStride);
                        if ((int)(short)PsxRam.ReadU16(iVar14 + BattleState.CtxTargetIndex) == iVar6 >> 0x10)
                        {
                            uVar4 = PsxRam.ReadU16(iVar14 + BattleState.CtxSlotRecords);
                            if ((uVar4 & 1) != 0 && (uVar4 & 0x80) != 0)
                            {
                                PsxRam.WriteU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                                                + BattleState.CtxTargetIndex, uVar5);
                                break;
                            }
                        }

                        uVar5 = (ushort)(uVar5 + 1);
                        iVar6 = (int)((uint)uVar5 << 0x10);
                    } while ((short)uVar5 < 3);

                    if (uVar5 == 3)
                    {
                        PsxRam.WriteU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                                        + BattleState.CtxTargetIndex, (ushort)(short)(sVar7 - 6));
                    }

                    sVar7 = (short)(sVar7 + 1);
                } while (sVar7 < 9);
            }
        }

        // 0x8005769C — THE LEGALITY SWEEP over targets. Any of slots 0..2 whose target is no longer
        // both live (bit 0) and marked (bit 7) is dragged onto the OTHER team's cursor at +0x16, and
        // any of slots 6..8 onto +0x14. Three each, not six: slots 3, 4, 5 and 9, 10, 11 are the
        // ones the roster never fills, and this is one of only two walks in the function that know
        // it — the automatic-targeting block above is the other. Every other walk here is twelve
        // long.
        if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 0x8008008) == 0)
        {
            iVar6 = 0;
            iVar14 = 0;
            do
            {
                iVar14 = iVar15 + ((iVar14 >> 0x10) * BattleState.CtxSlotRecordStride);
                if ((PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar14 + BattleState.CtxTargetIndex)
                                              * BattleState.CtxSlotRecordStride)
                                    + BattleState.CtxSlotRecords) & 0x81) != 0x81)
                {
                    PsxRam.WriteU16(iVar14 + BattleState.CtxTargetIndex, PsxRam.ReadU16(iVar15 + 0x16));
                }

                iVar6 = iVar6 + 1;
                iVar14 = iVar6 * 0x10000;
            } while (iVar6 * 0x10000 >> 0x10 < 3);

            iVar6 = 6;
            iVar14 = 0x60000;
            do
            {
                iVar14 = iVar15 + ((iVar14 >> 0x10) * BattleState.CtxSlotRecordStride);
                if ((PsxRam.ReadU16(iVar15 + ((short)PsxRam.ReadU16(iVar14 + BattleState.CtxTargetIndex)
                                              * BattleState.CtxSlotRecordStride)
                                    + BattleState.CtxSlotRecords) & 0x81) != 0x81)
                {
                    PsxRam.WriteU16(iVar14 + BattleState.CtxTargetIndex, PsxRam.ReadU16(iVar15 + 0x14));
                }

                iVar6 = iVar6 + 1;
                iVar14 = iVar6 * 0x10000;
            } while (iVar6 * 0x10000 >> 0x10 < 9);
        }

        // 0x80057794 — the two that run on every path, the suspended one included.
        RunBattleManagerFrame(iVar15);
        UpdateCentralGaugeBar(iVar15);

        iVar6 = 0;
        if (((uint)PsxRam.ReadI32(iVar15 + 0x10) & 4) == 0)
        {
            PsxRam.WriteU16(iVar15 + 0x1a, PsxRam.ReadU16(iVar15 + 0x14));
        }

        // 0x800577C4 — the acknowledgement of bit 27 of a fighter's +0x134: fire FUN_80042054(6, 0),
        // steer 0xFFFC into one of two globals on the fighter's state byte +0x16A, then clear the
        // bit. `'('` in the decompiler's rendering is the state number 0x28, not a character.
        //
        // TWO SPELLINGS OF ONE INDEX, and it is register reuse rather than a second counter:
        // iVar14 enters the body holding the shifted count and is then CLOBBERED with the slot
        // pointer, so the last two statements have to rebuild the same offset from iVar6 instead —
        // `(iVar6 << 0x10) >> 0xe` and `iVar14 >> 0xe` are both index * 4. Kept in that shape
        // rather than collapsed to one variable.
        iVar14 = 0;
        do
        {
            iVar8 = (iVar14 >> 0xe) + iVar15;
            iVar14 = PsxRam.ReadI32(iVar8 + BattleState.CtxFighterSlots);
            if (iVar14 != 0
                && ((uint)PsxRam.ReadI32(PsxRam.ReadI32(iVar14 + 8) + 0x134) & 0x8000000) != 0)
            {
                BattleScene.FUN_80042054(6, 0);
                if ((sbyte)PsxRam.ReadU8(
                        PsxRam.ReadI32(PsxRam.ReadI32(iVar8 + BattleState.CtxFighterSlots) + 8) + 0x16a)
                    == 0x28)
                {
                    DAT_8008d15c = 0xfffc;
                }
                else
                {
                    DAT_8008d15e = 0xfffc;
                }

                iVar14 = PsxRam.ReadI32(
                    PsxRam.ReadI32(((iVar6 << 0x10) >> 0xe) + iVar15 + BattleState.CtxFighterSlots) + 8);
                PsxRam.WriteI32(iVar14 + 0x134,
                    (int)((uint)PsxRam.ReadI32(iVar14 + 0x134) & 0xf7ffffff));
            }

            iVar6 = iVar6 + 1;
            iVar14 = iVar6 * 0x10000;
        } while (iVar6 * 0x10000 >> 0x10 < 0xc);

        // 0x80057888 — every gauge contribution back to zero. This is what makes +0x15B8 a
        // one-frame accumulator: whatever a slot pushed into the central gauge this frame it must
        // push again next frame.
        iVar6 = 0;
        do
        {
            sVar7 = (short)iVar6;
            iVar6 = iVar6 + 1;
            PsxRam.WriteU16(iVar15 + (sVar7 * BattleState.CtxSlotRecordStride)
                            + BattleState.CtxGaugeContribution, 0);
        } while (iVar6 * 0x10000 >> 0x10 < 0xc);
    }

    // GHIDRA: FUN_800578e0 @ 0x800578E0 (VS.EXE)
    // STATE 2 — THE HAND-BACK. 352 bytes, and it waits: four independent conditions each bump a
    // counter, and only a frame on which the counter is still zero AND the VM is not suspended does
    // anything at all. Then it writes DAT_801FF100 and moves to state 3.
    //
    // WHAT GOES BACK TO SELECT.EXE, and it is a five-way. ctx+0x08 bit 14 splits it; inside each
    // half the value already in DAT_801FF100 splits it again:
    //
    //   bit 14 clear   DAT_8008d4f0 = 3   in 0 -> out 4      anything else -> out 5
    //   bit 14 set     DAT_8008d4f0 = 2   in 0 -> out 3      in 1 -> out 3      else -> out 5
    //
    // That closes the other half of the contract BattleState.cs records from the SELECT.EXE side:
    // three values go in and 3, 4 or 5 comes back. The store is `sh v0,0x0(v1)` at 0x80057A1C, a
    // HALFWORD, in FUN_800290d0's delay slot — so it lands before that call runs, which is the
    // order written below.
    //
    // ctx+0x08 IS NOT ctx+0x10. The read is `lhu v0,0x8(s1)` at 0x800579B0, a halfword at +0x08,
    // and the only write to +0x08 in this slice is the zero FUN_80055ee0 puts there. On the
    // evidence of these four functions alone the bit is therefore never up and the first arm always
    // wins. Whatever raises it is outside the slice; nothing here compensates for that.
    private static void FUN_800578e0()
    {
        bool bVar1;
        int iVar2;
        int iVar3;
        int puVar4;

        puVar4 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        iVar3 = 0;
        RunBattleManagerFrame(puVar4);
        UpdateCentralGaugeBar(puVar4);
        iVar2 = AnimCmdSound.FUN_80060120();
        if (iVar2 != 0)
        {
            iVar3 = 1;
            if (DAT_8008d458 == 0)
            {
                BattleScene.FUN_800600b0(2);
            }
        }

        if ((BattleScene.DAT_8008d340 & 0xc) != 0)
        {
            SoundDriver.SoundCdLoadStep(0, 0);
            iVar3 = iVar3 + 1;
        }

        iVar2 = FUN_8005ecf4();
        if (iVar2 != 0)
        {
            iVar3 = iVar3 + 1;
        }

        if (BattleScene.DAT_8008d340 != 0)
        {
            iVar3 = iVar3 + 1;
        }

        if ((AnimVm.DAT_800b305a & 1) == 0 && iVar3 == 0)
        {
            DisableReverb();
            if ((PsxRam.ReadU16(puVar4 + 8) & 0x4000) == 0)
            {
                VS_EXE_exe.DAT_8008d4f0 = 3;
                bVar1 = SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] == 0;
                SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] = 5;
                if (bVar1)
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] = 4;
                }
            }
            else
            {
                VS_EXE_exe.DAT_8008d4f0 = 2;
                if (SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] == 0)
                {
                    SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] = 3;
                }
                else
                {
                    bVar1 = SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] == 1;
                    SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] = 5;
                    if (bVar1)
                    {
                        SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] = 3;
                    }
                }
            }

            FUN_800290d0();
            PsxRam.WriteU16(puVar4 + 0, 3);
        }
    }

    // GHIDRA: FUN_80057a40 @ 0x80057A40 (VS.EXE)
    // STATE 3 — TERMINAL. 60 bytes, and it is the two callees every other state also ends on and
    // nothing else. Once the state word reaches 3 the manager does no further work of its own for
    // the rest of the overlay's life.
    private static void FUN_80057a40()
    {
        int uVar1;

        uVar1 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        RunBattleManagerFrame(uVar1);
        UpdateCentralGaugeBar(uVar1);
    }

    // GHIDRA: FUN_8005cf78 @ 0x8005CF78 (VS.EXE)
    // 40 bytes, ONE caller and it is RunBattleRound at 0x80056464, so it belongs to this slice rather
    // than being borrowed from another. Transliterated in full rather than stubbed: it reads
    // nothing but the context it is handed, so there is no shared global to fork.
    //
    // It is the whole of "who won": the gauge sitting at exactly +30000 gives the cursor at +0x14,
    // anything else gives the cursor at +0x16. Note that a gauge pegged at -30000 and a gauge
    // stopped anywhere in between are treated identically.
    private static ushort FUN_8005cf78(int param_1)
    {
        ushort uVar1;

        if (PsxRam.ReadI32(param_1 + BattleState.CtxCentralGauge) == BattleState.CtxCentralGaugeLimit)
        {
            uVar1 = PsxRam.ReadU16(param_1 + 0x14);
        }
        else
        {
            uVar1 = PsxRam.ReadU16(param_1 + 0x16);
        }

        return uVar1;
    }

    // GHIDRA: FUN_8005d1f4 @ 0x8005D1F4 (VS.EXE)
    // 8 bytes — `jr ra` and its delay slot, nothing else. THE ORIGINAL FUNCTION IS EMPTY, so this
    // empty body is the transliteration and not a stub; there is nothing here left to port. One
    // caller, RunBattleRound at 0x80056778. It ends at 0x8005D1FB, one byte below LAB_8005d1fc, the
    // list-20 task entry main creates first each frame, so the two are adjacent in one compilation
    // unit.
    private static void FUN_8005d1f4()
    {
    }

    // =====================================================================================
    // THE GLOBALS THIS SLICE TOUCHES
    // =====================================================================================
    // The widths below are not Ghidra's inference, they are the store instructions: FUN_80055ee0's
    // body is thirteen gp-relative stores and each one is either `sh` or `sw`. gp is 0x8008D0FC in
    // this overlay, which the pair (DAT_8008d458 at gp+0x35C, DAT_8008d3ec at gp+0x2F0) fixes.
    //
    // Every one of them is a raw DAT_ name and stays one: what they carry is not closed by anything
    // in this slice.

    // GHIDRA: DAT_8008d320 @ 0x8008D320 (VS.EXE)
    // The battle context's ADDRESS, published for the rest of the overlay. `sw s0,0x224(gp)` at
    // 0x80055F04.
    //
    // OWNERSHIP CAVEAT, in the shape VS_EXE/FighterSetup.cs already uses for DAT_8008da48. This
    // global has 53 references and exactly ONE writer — FUN_80055ee0 above. The other 52 are reads,
    // and one of them is already transliterated. Cette tranche a ete ecrite quand
    // VS_EXE/AnimVmInterpreter.cs declarait son propre `private static int DAT_8008d320`, une copie
    // qui ne pouvait jamais voir celle-ci: le port en tenait deux la ou la console en tient une.
    // ELLE A ETE SUPPRIMEE DEPUIS, par la passe de couture qui a integre cette tranche. Il n'existe
    // plus qu'un seul stockage, celui-ci, et AnimVmInterpreter lit `BattleManager.DAT_8008d320`.
    // Le `internal` reste necessaire pour cette raison meme. Le symbole appartient toujours a
    // BattleState.cs sur le fond; il n'y est pas encore, et ce n'est plus un fork, juste un
    // emplacement discutable.
    internal static int DAT_8008d320;

    // GHIDRA: DAT_8008d458 @ 0x8008D458 (VS.EXE)
    // `sh zero,0x35c(gp)`. Zeroed when the match is armed; gates the "no slot still live" test in
    // RunBattleRound and the FUN_800600b0(2) call in FUN_800578e0.
    private static short DAT_8008d458;

    // GHIDRA: DAT_8008d3ec @ 0x8008D3EC (VS.EXE)
    // `sh zero,0x2f0(gp)`. Zeroed twice: at arming, and again on the wind-down's winner test.
    private static short DAT_8008d3ec;

    // GHIDRA: DAT_8008d3e8 @ 0x8008D3E8 (VS.EXE)
    // `sh zero,0x2ec(gp)`. Written once in this slice, at arming.
    private static short DAT_8008d3e8;

    // GHIDRA: DAT_8008d3e4 @ 0x8008D3E4 (VS.EXE)
    // `sh zero,0x2e8(gp)`. Zeroed at arming, set to 0x80 on the wind-down's winner test.
    private static short DAT_8008d3e4;

    // GHIDRA: DAT_8008d3a0 @ 0x8008D3A0 (VS.EXE)
    // `sh zero,0x2a4(gp)`. Same pair as DAT_8008d3e4, always written with it and to the same value.
    private static short DAT_8008d3a0;

    // GHIDRA: DAT_8008d57c @ 0x8008D57C (VS.EXE)
    // `sh zero,0x480(gp)`. Zeroed at arming and again on the winner test.
    private static short DAT_8008d57c;

    // GHIDRA: DAT_8008d428 @ 0x8008D428 (VS.EXE)
    // `sw zero,0x32c(gp)`. Takes a COPY of the central gauge — the only global in this slice that
    // does — at the moment the wind-down resets it to zero, so it is the gauge as the round ended.
    private static int DAT_8008d428;

    // GHIDRA: DAT_8008d448 @ 0x8008D448 (VS.EXE)
    private static int DAT_8008d448;

    // GHIDRA: DAT_8008d3d4 @ 0x8008D3D4 (VS.EXE)
    private static int DAT_8008d3d4;

    // GHIDRA: DAT_8008d494 @ 0x8008D494 (VS.EXE)
    private static int DAT_8008d494;

    // GHIDRA: DAT_8008d3e0 @ 0x8008D3E0 (VS.EXE)
    private static int DAT_8008d3e0;

    // GHIDRA: DAT_8008d424 @ 0x8008D424 (VS.EXE)
    private static int DAT_8008d424;

    // GHIDRA: DAT_8008d35c @ 0x8008D35C (VS.EXE)
    private static int DAT_8008d35c;

    // GHIDRA: DAT_8008d3a8 @ 0x8008D3A8 (VS.EXE)
    // Ghidra types it undefined4. Zeroed in the wind-down, alongside the central gauge itself.
    // UpdateCentralGaugeBar's own PART ONE closes its role: a capped-rate (0xEB/frame) follower of
    // the live gauge at ctx+0x302C, driving that function's scroll-speed counters.
    private static int DAT_8008d3a8;

    // GHIDRA: DAT_8008d3f8 @ 0x8008D3F8 (VS.EXE)
    // `sh zero,0x2fc(gp)`. Zeroed once, at the tail of FUN_8005a104, alongside DAT_8008d458.
    private static short DAT_8008d3f8;

    // GHIDRA: DAT_8008d15c @ 0x8008D15C (VS.EXE)
    // Ghidra types it undefined2 and the image holds 0x0000. It and DAT_8008d15e are ADJACENT
    // halfwords, written the same value 0xFFFC on the two arms of one test, so they are a two-entry
    // per-side table addressed as two names.
    private static ushort DAT_8008d15c;

    // GHIDRA: DAT_8008d15e @ 0x8008D15E (VS.EXE)
    private static ushort DAT_8008d15e;

    // GHIDRA: DAT_8008d340 @ 0x8008D340 (VS.EXE)
    // NOT DECLARED HERE, and the first draft of this file got that wrong. Ghidra types it
    // undefined4. State 2 READS it twice — once masked with 0xC and once whole, each hit blocking
    // the hand-back for another frame — and holds no writer for it. VS_EXE/BattleScene.cs holds the
    // only writers in the port, phase 1 raising bit 6 and phase 4 lowering it, and declares it
    // `internal static uint DAT_8008d340` for exactly this. Both reads in state 2 therefore go to
    // BattleScene.DAT_8008d340 rather than to a private copy that could never see those writes.
    // That file's OWNERSHIP CAVEAT asked this side to do it; this is that fix, and the caveat can
    // be struck when the two files are next touched together.

    // GHIDRA: DAT_801ff100 @ 0x801FF100 (VS.EXE)
    // Not a declaration — an INDEX. The word SELECT.EXE hands over is a 16-bit global inside the
    // shared high-RAM span SharedHighRam already models (base 0x801FF000), so it is short index
    // 0x80 of SHORT_ARRAY_801ff000: the spelling SELECT_EXE/CharacterSelect.cs writes it with,
    // TITLE_EXE/SecondScreenSetup.cs reads it with and VS_EXE/FighterSetup.cs uses. Nothing new is
    // declared for it here. The width is closed by the instructions at both ends of this slice's
    // use of it: `lhu a0,0x0(v1)` at 0x800579C8 and `sh v0,0x0(v1)` at 0x80057A1C.
    private const int Dat801ff100ShortIndex = 0x80;

    // GHIDRA: LAB_80034eac @ 0x80034EAC (VS.EXE)
    // THE SCENE TASK'S ENTRY POINT — id 0x50, list 12, 0x7C bytes of workspace. NOT DECLARED HERE.
    //
    // The label has exactly ONE reference in the whole overlay, `PARAM` from RunBattleRound at
    // 0x800563F8, which is the CreateTask argument in this file — so a private const here would
    // have been defensible. It is still wrong, because VS_EXE/BattleScene.cs transliterates the
    // body behind that address and already declares it `internal const int BattleSceneEntry`,
    // registering its callback under it. Two spellings of one entry point is how a task gets
    // created under one number and dispatched under another. The CreateTask above passes
    // BattleScene.BattleSceneEntry and calls that file's RegisterBattleSceneTask first.

    // =====================================================================================
    // The twelve callees this slice reaches that are NOT in it. Each is declared so the call site
    // above is real, with the arguments the original's call setup actually passes — Ghidra carries
    // no prototype for any of them, so the argument lists come from the a0/a1/a2 loads at each jal.
    // None is invented and none is a convenience API.
    //
    // DEUX D'ENTRE ELLES EXISTAIENT AILLEURS dans le port sous forme de souches vides privees —
    // FUN_80042054 dans VS_EXE_exe.cs et FUN_8005ee5c dans AnimVmInterpreter.cs. Les avoir declarees
    // ici plutot que de les laisser decouvrir est ce qui a permis de les fusionner: LES DEUX
    // DOUBLONS SONT SUPPRIMES. FUN_80042054 vit maintenant chez BattleScene (il RETOURNE une valeur,
    // deux de ses onze appelants lisent $v0) et FUN_8005ee5c vit ici, en `internal`.
    // =====================================================================================

    // GHIDRA: RunBattleManagerFrame @ 0x8005A5B0 (VS.EXE)
    // THE BATTLE MANAGER'S PER-FRAME BODY. 8500 bytes, 1078 decompiled lines, by far the largest
    // thing this slice reaches, and it runs on EVERY path of every state including the suspended
    // one. param_1 is declared `short *`, so every Ghidra index below is a HALFWORD index; this
    // port keeps every access as a PsxRam call at the corresponding BYTE address instead, exactly
    // the discipline the rest of this file already uses.
    //
    // THE SHAPE, confirmed against the decompilation line by line:
    //   1. Top gate: everything up to the per-slot primitive submission sits inside
    //      `if ((AnimVm.DAT_800b305a & 1) == 0)` -- the VM-suspend bit clear, i.e. running.
    //   2. Zero local_b8 (twelve halfwords -- the 32768-entry auStack_100b8 Ghidra also declares
    //      does not exist on the real 200-byte frame, and is not modelled).
    //   3. Clamp four CtxSlotRecords fields per slot.
    //   4. Gated on CtxFlags bit 0x10000000: the "a slot just went idle" one-shot -- see the block
    //      comment at its own site for the full list of what it touches.
    //   5. Gated on CtxFlags bit 0x20000000: THE TEAM-WIPE BRANCH -- this is the writer of CtxState
    //      = 2 BattleState.cs's own header now records.
    //   6. Two unconditional per-team passes stamping SubRecordFlags bit 0x08 and SubRecordOrdinal.
    //   7. A per-slot reseed of subrecord+0x18/+0x1A keyed on SubRecordOrdinal, through the newly
    //      embedded DAT_80083e1c/1e table.
    //   8. THE PER-SLOT RENDER LOOP: an animated-value follower, ki/HP shadow copies, SubRecordFlags
    //      sync, and a three-way colour pick (through Roster.cs's own portrait coordinate table).
    //   9. Two small per-slot counters (subrecord+0xE, subrecord+0x16) driving local_b8's dirty bits.
    //  10. THE NUMERIC-READOUT TARGET STATE MACHINE at subrecord+0x24 -- an eleven-tier hold/advance
    //      sequence tracking CtxSlotRecords+0xA.
    //  11. The twelve-slot HUD sub-loop: FUN_80057a7c, FUN_80058120 (twice) and FUN_80058338.
    //  12. THE PER-SLOT PRIMITIVE SUBMISSION -- runs on every path, suspended VM included.
    //  13. Suspended-VM early exit: submit the tally icon's own primitives and return.
    //  14. Walk the published cursor at ctx+0x1A onto the next alive+marked slot.
    //  15. Outside state 1, force CtxRoundRequest to zero.
    //  16. CtxRoundRequest bits 0/1: THE TEAM-A/TEAM-B TALLIES.
    //  17. CtxRoundRequest bit 2: THE WIN/LOSS COMPUTE, and bits 0x100/0x180 gating FUN_80026d98
    //      (BLOCKED -- see its own declaration below).
    //  18. Gated on CtxFlags bit 0x100000: THE TALLY ICON'S OWN MAX-SCAN over CtxSlotRecords+0xA.
    //  19. THE TALLY ICON'S OWN DISPLAY STATE MACHINE on CtxTallyState, including the keyframe
    //      interpolation through the newly embedded DAT_80084234 table.
    //  20. THE FINAL SUBMISSION: ten primitives at ctx+0x2DCC, an eleventh when CtxTallyValue > 9.
    //
    // WHAT IS STILL NOT CLOSED. FUN_80058338 stays its own BLOCKED stub -- its own header explains
    // why (two pointer tables with no self-referential span to embed against). FUN_80026d98 is
    // declared below as a stub for the same reason its own comment gives: its own dependency chain
    // (FUN_80027340 and four functions past it) is unported. Most of the eighteen CtxFlags bits and
    // the sixteen ctx+0x2F3C..0x2F7E "pose" halfwords are POSITIONS this port can now name, not
    // MEANINGS this slice closes -- consistent with the file header's own PARTIAL above.
    //
    // EVERY LOOP BELOW KEEPS the file's own established `iVarN = iVarN * 0x10000; ... iVarN *
    // 0x10000 >> 0x10` idiom literally, for the same reason RunBattleRound's own header gives: it is
    // how the original's `short` induction variables survive into the object code, and the
    // truncation is what bounds them. THREE SETS OF GOTOS could not be kept as C# `goto`: two jump
    // INTO a sibling if/else arm (the SubRecordOrdinal sync at 0x8005AE38 and the numeric-readout
    // state machine's own LAB_8005b880/LAB_8005b890 pair), reproduced the same way this file's other
    // forced departures are, by duplicating the shared statement in both arms; one (LAB_8005ad30, the
    // knock-out-counter arm) collapses to an unconditional `iVar21 = 0x60000` on every path and is
    // reproduced with a boolean flag rather than a literal goto. LAB_8005c624 (three call sites, one
    // statement) and LAB_8005c6a0 (one call site reached both by fall-through and by an actual
    // forward `goto` out of the suspended-VM branch, which C# allows since it does not jump into a
    // block) are the two the port keeps as-is.
    private static void RunBattleManagerFrame(int param_1)
    {
        byte uVar1;
        sbyte cVar2;
        ushort uVar3;
        short sVar4;
        int iVar5;
        int iVar6;
        short sVar7;
        short sVar8;
        int iVar9;
        uint uVar10;
        int puVar11;
        int psVar12;
        int puVar13;
        int iVar14;
        ushort uVar15;
        bool bVar16;
        int psVar17;
        int iVar19;
        int puVar20;
        int ot;
        int iVar21;
        ushort[] local_b8 = new ushort[12];

        puVar20 = param_1 + BattleState.CtxSlotSubRecords;

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            // 0x8005A5D8 -- zero the twelve halfwords of local_b8. The 32768-entry auStack_100b8
            // Ghidra declares does not exist on the real 200-byte frame (addiu sp,sp,-0xc8); only
            // this 12-entry scratch does, and this loop is its only initialiser.
            iVar21 = 0;
            iVar5 = 0;
            do
            {
                local_b8[iVar5 >> 0x10] = 0;
                iVar21 = iVar21 + 1;
                iVar5 = iVar21 * 0x10000;
            } while (iVar21 * 0x10000 >> 0x10 < 0xc);

            // 0x8005A600 -- clamp four CtxSlotRecords fields per slot, all SIGNED halfword loads:
            // +0x2 -> [0,0x640], CtxKiGauge -> [0,16000], +0x6 -> [0,20000], +0xA -> [0,99].
            iVar21 = 0;
            iVar5 = 0;
            do
            {
                iVar5 = iVar5 >> 0x10;
                if (0x640 < (short)PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 2))
                {
                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 2, 0x640);
                }
                if ((short)PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 2) < 0)
                {
                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 2, 0);
                }
                if (16000 < (short)PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge))
                {
                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge, 16000);
                }
                if ((short)PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge) < 0)
                {
                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge, 0);
                }
                if (20000 < (short)PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 6))
                {
                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 6, 20000);
                }
                if ((short)PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 6) < 0)
                {
                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 6, 0);
                }
                if (99 < (short)PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xA))
                {
                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xA, 99);
                }
                iVar21 = iVar21 + 1;
                if ((short)PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xA) < 0)
                {
                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xA, 0);
                }
                iVar5 = iVar21 * 0x10000;
            } while (iVar21 * 0x10000 >> 0x10 < 0xc);

            // 0x8005A688 -- gated on CtxFlags bit 0x10000000: a 12-slot scan for the first slot with
            // record+2 == 0 and record flags bit 0x200 set. On a hit: OR 0x4000000 into that slot's
            // OWN fighter's +0x138 (no null guard in the original -- none added here, Rule 12), keep
            // only record-flag bits 0x81 and set 0x1000, clear SubRecordFlags bit 0x200, zero three
            // CtxSlotRecords fields (+0x6, CtxKiGauge, +0x2) and three SUBRECORD fields (+0x2, +0x4,
            // +0x6), store 0x80 (or 0x40 when CtxFlags bit 0x4000 is clear) at ctx+0x2D64, the slot
            // index at ctx+0x2D66, touch the ctx+0x2C14 per-slot halfword for this slot, OR
            // 0x10000600 into CtxFlags, then broadcast bit 0x2000000 into every fighter task's +0x138.
            if ((PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x10000000) == 0)
            {
                uVar3 = 0;
                psVar12 = param_1 + 0x22;
                iVar5 = 0;
                puVar13 = puVar20;
                do
                {
                    iVar5 = iVar5 >> 0x10;
                    if ((short)PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 2) == 0
                        && (PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x200) != 0)
                    {
                        int fighterTask = PsxRam.ReadI32(param_1 + BattleState.CtxFighterSlots + iVar5 * 4);
                        int fighter = PsxRam.ReadI32(fighterTask + 8);
                        PsxRam.WriteI32(fighter + 0x138, (int)((uint)PsxRam.ReadI32(fighter + 0x138) | 0x4000000));

                        PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords,
                            (ushort)((PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x81) | 0x1000));
                        PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) & 0xfdff));
                        PsxRam.WriteU16(psVar12 + 4, 0);
                        PsxRam.WriteU16(psVar12 + 2, 0);
                        PsxRam.WriteU16(psVar12, 0);
                        PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 6, 0);
                        PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge, 0);
                        PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 2, 0);

                        sVar8 = 0x80;
                        if ((PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x4000) == 0)
                        {
                            sVar8 = 0x40;
                        }
                        PsxRam.WriteU16(param_1 + 0x2d64, (ushort)sVar8);
                        iVar5 = (int)((uint)uVar3 << 0x10) >> 0xf;
                        PsxRam.WriteU16(param_1 + 0x2d66, uVar3);
                        uVar15 = PsxRam.ReadU16(param_1 + iVar5 + 0x2c14);
                        iVar21 = 0;
                        if ((uVar15 & 0x200) != 0)
                        {
                            PsxRam.WriteU16(param_1 + iVar5 + 0x2c14, (ushort)(uVar15 & 0xf9f7));
                        }
                        PsxRam.WriteU16(param_1 + iVar5 + 0x2c14, (ushort)(PsxRam.ReadU16(param_1 + iVar5 + 0x2c14) & 0xfbef));
                        PsxRam.WriteI32(param_1 + BattleState.CtxFlags, (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxFlags) | 0x10000600));

                        iVar5 = 0;
                        do
                        {
                            iVar5 = PsxRam.ReadI32((iVar5 >> 0xe) + param_1 + BattleState.CtxFighterSlots);
                            if (iVar5 != 0)
                            {
                                iVar5 = PsxRam.ReadI32(iVar5 + 8);
                                PsxRam.WriteI32(iVar5 + 0x138, (int)((uint)PsxRam.ReadI32(iVar5 + 0x138) | 0x2000000));
                            }
                            iVar21 = iVar21 + 1;
                            iVar5 = iVar21 * 0x10000;
                        } while (iVar21 * 0x10000 >> 0x10 < 0xc);
                    }
                    else
                    {
                        psVar12 = psVar12 + 0x1c0;
                        puVar13 = puVar13 + 0x1c0;
                    }
                    uVar3 = (ushort)(uVar3 + 1);
                    iVar5 = (int)((uint)uVar3 << 0x10);
                } while ((short)uVar3 < 0xc);
            }

            // 0x8005A788 -- gated on CtxFlags bit 0x20000000: THE TEAM-WIPE BRANCH, mirrored for
            // slots 0..5 and 6..11. uVar10 is the slot ctx+0x2D66 named (the slot the previous gate,
            // above, just marked down). Its own record is force-set to 0x10, CtxRoundRequest gets its
            // team-A (bit 0) or team-B (bit 1) tally armed, and if that slot was the active team's own
            // acting-slot cursor (CtxActingSlotTeamA / CtxActingSlotTeamB) the cursor is walked to the
            // next living+marked slot. ON A FULL TEAM WIPE -- the cursor search exhausts with no slot
            // left alive at all -- CtxState is written 2 (sh v1,0x0(s2) @ 0x8005AE04, mirrored by the
            // team-B arm's own sh v0,0x0(param_1)), which is the writer BattleState.cs records.
            if ((PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x20000000) != 0)
            {
                uVar10 = PsxRam.ReadU16(param_1 + 0x2d66);
                PsxRam.WriteU16(param_1 + (int)uVar10 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords, 0x10);
                if ((int)uVar10 < 6)
                {
                    PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                        (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) | 1));
                    if (((uint)PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x70) >> 4 == uVar10)
                    {
                        PsxRam.WriteI32(param_1 + BattleState.CtxFlags,
                            (int)(((uint)PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0xffffff8f) | 0x150));
                    }

                    if ((short)PsxRam.ReadU16(param_1 + BattleState.CtxActingSlotTeamA) == uVar10)
                    {
                        iVar21 = 0;
                        iVar5 = 0;
                        do
                        {
                            sVar8 = (short)iVar21;
                            if ((PsxRam.ReadU16(param_1 + (iVar5 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 1) != 0
                                && (PsxRam.ReadU16(param_1 + (iVar5 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x200) != 0)
                            {
                                PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamA, (ushort)sVar8);
                                break;
                            }
                            iVar21 = iVar21 + 1;
                            sVar8 = (short)iVar21;
                            iVar5 = iVar21 * 0x10000;
                        } while (iVar21 * 0x10000 >> 0x10 < 6);

                        if (sVar8 == 6)
                        {
                            iVar21 = 0;
                            iVar5 = 0;
                            do
                            {
                                iVar6 = iVar5 + 1;
                                if ((PsxRam.ReadU16(param_1 + (iVar21 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 1) != 0)
                                {
                                    break;
                                }
                                iVar21 = iVar6 * 0x10000;
                                iVar5 = iVar6;
                            } while (iVar6 * 0x10000 >> 0x10 < 6);
                            iVar21 = iVar5 << 0x10;

                            if ((short)iVar5 < 6)
                            {
                                iVar21 = 0;
                                iVar5 = 0;
                                do
                                {
                                    iVar5 = PsxRam.ReadI32((iVar5 >> 0xe) + param_1 + BattleState.CtxFighterSlots);
                                    if (iVar5 != 0)
                                    {
                                        iVar5 = PsxRam.ReadI32(iVar5 + 8);
                                        PsxRam.WriteI32(iVar5 + 0x138, (int)((uint)PsxRam.ReadI32(iVar5 + 0x138) | 0x2000000));
                                    }
                                    iVar21 = iVar21 + 1;
                                    iVar5 = iVar21 * 0x10000;
                                } while (iVar21 * 0x10000 >> 0x10 < 0xc);

                                sVar8 = 0;
                                iVar21 = 0;
                                iVar5 = 0;
                                do
                                {
                                    if ((PsxRam.ReadU16(param_1 + (iVar5 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 1) != 0
                                        && (PsxRam.ReadU16(param_1 + (iVar5 >> 0x10) + 0x2c14) & 0x80) == 0)
                                    {
                                        sVar8 = (short)(sVar8 + 1);
                                    }
                                    iVar21 = iVar21 + 1;
                                    iVar5 = iVar21 * 0x10000;
                                } while (iVar21 * 0x10000 >> 0x10 < 6);

                                if (sVar8 == 0)
                                {
                                    iVar21 = 0;
                                    iVar5 = 0;
                                    do
                                    {
                                        iVar5 = iVar5 >> 0x10;
                                        iVar21 = iVar21 + 1;
                                        if ((PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 1) != 0)
                                        {
                                            PsxRam.WriteU16(param_1 + iVar5 * 2 + 0x2c14,
                                                (ushort)(PsxRam.ReadU16(param_1 + iVar5 * 2 + 0x2c14) & 0xff1f));
                                            break;
                                        }
                                        iVar5 = iVar21 * 0x10000;
                                    } while (iVar21 * 0x10000 >> 0x10 < 6);
                                }

                                if ((PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x4000) == 0)
                                {
                                    PsxRam.WriteI32(param_1 + BattleState.CtxFlags,
                                        (int)(((uint)PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0xffffcfff) | 0x4000));
                                }
                                PsxRam.WriteI32(param_1 + BattleState.CtxFlags,
                                    (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxFlags) | 0x8000));
                                iVar21 = 0;
                            }

                            if (iVar21 >> 0x10 == 6)
                            {
                                iVar5 = 0;
                                do
                                {
                                    iVar21 = PsxRam.ReadI32(((int)(uVar10 << 0x10) >> 0xe) + param_1 + BattleState.CtxFighterSlots);
                                    if (iVar21 != 0)
                                    {
                                        iVar21 = PsxRam.ReadI32(iVar21 + 8);
                                        PsxRam.WriteI32(iVar21 + 0x138, (int)((uint)PsxRam.ReadI32(iVar21 + 0x138) | 0x80000000));
                                    }
                                    iVar5 = iVar5 + 1;
                                } while (iVar5 * 0x10000 >> 0x10 < 0xc);
                                PsxRam.WriteU16(param_1, 2);
                            }
                        }
                    }
                }
                else
                {
                    PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                        (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) | 2));

                    if ((short)PsxRam.ReadU16(param_1 + BattleState.CtxActingSlotTeamB) == uVar10)
                    {
                        sVar8 = 6;
                        do
                        {
                            if ((PsxRam.ReadU16(param_1 + sVar8 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 1) != 0
                                && (PsxRam.ReadU16(param_1 + sVar8 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x200) != 0)
                            {
                                PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamB, (ushort)sVar8);
                                break;
                            }
                            sVar8 = (short)(sVar8 + 1);
                        } while (sVar8 < 0xc);

                        if (sVar8 == 0xc)
                        {
                            iVar5 = 0x60000;
                            uVar3 = 6;
                            do
                            {
                                iVar5 = iVar5 >> 0x10;
                                uVar15 = (ushort)(uVar3 + 1);
                                if ((PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 1) != 0)
                                {
                                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords,
                                        (ushort)(PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0xff9f));
                                    PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamB, uVar3);
                                    if ((PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x2000) == 0)
                                    {
                                        PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords,
                                            (ushort)(PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) | 0x80));
                                    }
                                    PsxRam.WriteU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords,
                                        (ushort)(PsxRam.ReadU16(param_1 + iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) | 0x210));
                                    PsxRam.WriteU16(param_1 + iVar5 * 2 + 0x2c14,
                                        (ushort)(PsxRam.ReadU16(param_1 + iVar5 * 2 + 0x2c14) | 0x400));
                                    uVar15 = uVar3;
                                    break;
                                }
                                iVar5 = (int)((uint)uVar15 << 0x10);
                                uVar3 = uVar15;
                            } while ((short)uVar15 < 0xc);

                            if (uVar15 == 0xc)
                            {
                                iVar5 = 6;
                                // DAT_1f80012c @ 0x1F80012C -- VS.EXE's own scratchpad word (a
                                // TITLE.EXE 0..2 loading-picture counter reuses the same physical
                                // slot; see Scratchpad.cs's conflict note). Read by raw address, the
                                // same treatment VS_EXE/BattleScene.cs already gives it, since
                                // VS_EXE_exe.cs's own storage field is private to that file.
                                // GHIDRA: DAT_1f80012c @ 0x1F80012C (VS.EXE) -- a scratchpad word.
                                // VS_EXE_exe.cs declares the scalar, `private static int
                                // DAT_1f80012c`, and main clears it to 0 at boot; it is private, so
                                // this file cannot read that copy and declaring a second scalar would
                                // fork the storage, so the value is read BY ADDRESS instead, the same
                                // treatment VS_EXE/BattleScene.cs already gives this exact word. See
                                // Scratchpad.cs's own conflict note: TITLE.EXE keeps an unrelated 0..2
                                // loading-picture counter in this same physical slot.
                                const int Dat1f80012cAddress = 0x1F80012C;

                                bool tookGoto = false;
                                if (PsxRam.ReadI32(Dat1f80012cAddress) == 4)
                                {
                                    tookGoto = true;
                                }
                                else
                                {
                                    iVar21 = 0x60000;
                                    if ((PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x4000) == 0)
                                    {
                                        PsxRam.WriteI32(param_1 + BattleState.CtxFlags,
                                            (int)(((uint)PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0xffffcfff) ^ 0x4000));
                                        tookGoto = true;
                                    }
                                }
                                // LAB_8005ad30 -- both the counter-equals-4 test and the fall-through
                                // after clearing bit 0x4000 land here with iVar21 = 0x60000.
                                if (tookGoto)
                                {
                                    iVar21 = 0x60000;
                                }

                                do
                                {
                                    sVar8 = (short)iVar5;
                                    iVar5 = iVar5 + 1;
                                    if ((PsxRam.ReadU16(param_1 + (iVar21 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 1) != 0)
                                    {
                                        break;
                                    }
                                    iVar21 = iVar5 * 0x10000;
                                    sVar8 = (short)iVar5;
                                } while (iVar5 * 0x10000 >> 0x10 < 0xc);

                                // param_1[0xb15] is slot 6's own CtxSlotRecords+2 field (6 * 10 +
                                // 0xad9 = 0xb15) -- the same "countdown" field the top clamp reads,
                                // hardcoded to team B's own first slot. The original tests it through
                                // `-(field == 0) & 0xc == 0xc`, a boolean-to-mask compiler artifact
                                // with no edge-case divergence from the plain equality it encodes;
                                // written directly as that equality rather than reproducing the mask.
                                if (sVar8 == 0xc
                                    && (short)PsxRam.ReadU16(param_1 + 6 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 2) == 0)
                                {
                                    iVar5 = 0;
                                    do
                                    {
                                        iVar21 = PsxRam.ReadI32(((int)(uVar10 << 0x10) >> 0xe) + param_1 + BattleState.CtxFighterSlots);
                                        if (iVar21 != 0)
                                        {
                                            iVar21 = PsxRam.ReadI32(iVar21 + 8);
                                            PsxRam.WriteI32(iVar21 + 0x138, (int)((uint)PsxRam.ReadI32(iVar21 + 0x138) | 0x80000000));
                                        }
                                        iVar5 = iVar5 + 1;
                                    } while (iVar5 * 0x10000 >> 0x10 < 0xc);
                                    PsxRam.WriteU16(param_1, 2);
                                    PsxRam.WriteU16(param_1 + 8, (ushort)(PsxRam.ReadU16(param_1 + 8) | 0x4000));
                                }
                            }
                        }
                    }
                }

                PsxRam.WriteI32(param_1 + BattleState.CtxFlags, (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0xcfffffff));
            }

            // 0x8005AE38 -- UNCONDITIONAL per-team passes: sync SubRecordFlags bit 0x08 (the acting-
            // slot mark) to CtxActingSlotTeamA / CtxActingSlotTeamB, then stamp SubRecordOrdinal --
            // literal 5 for every team-A slot and 9 for every team-B slot first, THEN overwritten with
            // a sequential 0,1,2 (6,7,8 for team B) for at most three slots per team, acting slot
            // first among ties (matches SubRecordFlags bit 0x80 && bit 0x08), then the remaining
            // marked slots (bit 0x80 && !bit 0x08). Two goto targets (LAB_8005ae78, LAB_8005aee8)
            // each jump into the sibling arm of an if/else the same way LAB_800561d4 does in
            // RunBattleRound above; written the same way, as if/else-if/else with the shared write
            // repeated in both arms rather than merged.
            iVar5 = 0;
            puVar13 = puVar20;
            do
            {
                if ((short)PsxRam.ReadU16(param_1 + BattleState.CtxActingSlotTeamA) == (short)iVar5)
                {
                    if ((PsxRam.ReadU16(puVar13) & 8) == 0)
                    {
                        uVar3 = (ushort)((PsxRam.ReadU16(puVar13) & 0xfff9) | 8);
                        PsxRam.WriteU16(puVar13, uVar3);
                    }
                }
                else
                {
                    uVar3 = (ushort)(PsxRam.ReadU16(puVar13) & 0xfff1);
                    if ((PsxRam.ReadU16(puVar13) & 8) != 0)
                    {
                        PsxRam.WriteU16(puVar13, uVar3);
                    }
                }
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 6);

            iVar5 = 6;
            do
            {
                if ((short)PsxRam.ReadU16(param_1 + BattleState.CtxActingSlotTeamB) == (short)iVar5)
                {
                    if ((PsxRam.ReadU16(puVar13) & 8) == 0)
                    {
                        uVar3 = (ushort)((PsxRam.ReadU16(puVar13) & 0xfff9) | 8);
                        PsxRam.WriteU16(puVar13, uVar3);
                    }
                }
                else
                {
                    uVar3 = (ushort)(PsxRam.ReadU16(puVar13) & 0xfff1);
                    if ((PsxRam.ReadU16(puVar13) & 8) != 0)
                    {
                        PsxRam.WriteU16(puVar13, uVar3);
                    }
                }
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);

            iVar5 = 0;
            puVar13 = puVar20;
            do
            {
                PsxRam.WriteU16(puVar13 + BattleState.SubRecordOrdinal, 5);
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 6);

            psVar12 = param_1 + 0xaa0;
            iVar5 = 6;
            do
            {
                PsxRam.WriteU16(psVar12 + BattleState.SubRecordOrdinal, 9);
                iVar5 = iVar5 + 1;
                psVar12 = psVar12 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);

            uVar3 = 0;
            iVar5 = 0;
            puVar13 = puVar20;
            do
            {
                if (uVar3 == 3) break;
                if ((PsxRam.ReadU16(puVar13) & 0x80) != 0 && (PsxRam.ReadU16(puVar13) & 8) != 0)
                {
                    PsxRam.WriteU16(puVar13 + BattleState.SubRecordOrdinal, uVar3);
                    uVar3 = (ushort)(uVar3 + 1);
                }
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 6);

            iVar5 = 0;
            puVar13 = puVar20;
            do
            {
                if (uVar3 == 3) break;
                if ((PsxRam.ReadU16(puVar13) & 0x80) != 0 && (PsxRam.ReadU16(puVar13) & 8) == 0)
                {
                    PsxRam.WriteU16(puVar13 + BattleState.SubRecordOrdinal, uVar3);
                    uVar3 = (ushort)(uVar3 + 1);
                }
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 6);

            uVar3 = 6;
            puVar13 = param_1 + 0xaa0;
            iVar5 = 6;
            do
            {
                if (uVar3 == 9) break;
                if ((PsxRam.ReadU16(puVar13) & 0x80) != 0 && (PsxRam.ReadU16(puVar13) & 8) != 0)
                {
                    PsxRam.WriteU16(puVar13 + BattleState.SubRecordOrdinal, uVar3);
                    uVar3 = (ushort)(uVar3 + 1);
                }
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);

            puVar13 = param_1 + 0xaa0;
            iVar5 = 6;
            do
            {
                if (uVar3 == 9) break;
                if ((PsxRam.ReadU16(puVar13) & 0x80) != 0 && (PsxRam.ReadU16(puVar13) & 8) == 0)
                {
                    PsxRam.WriteU16(puVar13 + BattleState.SubRecordOrdinal, uVar3);
                    uVar3 = (ushort)(uVar3 + 1);
                }
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);

            // 0x8005AF64 -- when SubRecordOrdinal (subrecord+0x14) has changed since last frame (its
            // own shadow copy at subrecord+0x12 disagrees), reseed subrecord+0x18 -> +0x1C and
            // +0x1A -> +0x1E, look the new ordinal up in DAT_80083e1c/1e (12 rows of two shorts,
            // 0x80083E1C..0x80083E4C -- the gap Roster.cs's own 300-byte record closes on one side and
            // this file's own Dat80083e4cAddress table closes on the other, single-owner confirmed
            // with find-cross-references) into SubRecordNumericReadout (subrecord+0x20) and
            // subrecord+0x22, zero subrecord+0x16, and OR SubRecordFlags bit 0x100.
            iVar5 = 0;
            psVar12 = param_1 + 0x36;
            puVar13 = puVar20;
            do
            {
                if (PsxRam.ReadU16(psVar12 - 4) != PsxRam.ReadU16(psVar12 - 2))
                {
                    PsxRam.WriteU16(psVar12 - 4, PsxRam.ReadU16(psVar12 - 2));
                    PsxRam.WriteU16(psVar12 + 2, PsxRam.ReadU16(psVar12 + 6));
                    PsxRam.WriteU16(psVar12 + 4, PsxRam.ReadU16(psVar12 + 8));
                    PsxRam.WriteU16(psVar12 + 10, PsxRam.ReadU16(Dat80083e1cAddress + PsxRam.ReadU16(psVar12 - 2) * 4));
                    sVar8 = (short)PsxRam.ReadU16(Dat80083e1eAddress + PsxRam.ReadU16(psVar12 - 2) * 4);
                    PsxRam.WriteU16(psVar12, 0);
                    PsxRam.WriteU16(psVar12 + 12, (ushort)sVar8);
                    PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) | 0x100));
                }
                psVar12 = psVar12 + 0x1c0;
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);

            // 0x8005B04C -- the per-slot RENDER LOOP: an animated-value follower (subrecord+0x2
            // steps by up to +/-20 per frame towards CtxSlotRecords+0x2, arming subrecord+0x10's low
            // 5 bits as a one-shot on the first step and marking local_b8 bit 0x08), a plain shadow
            // copy of CtxKiGauge into subrecord+0x4 (marks local_b8 bit 0x10) and of CtxSlotRecords+0x6
            // into subrecord+0x6 (no mark), SubRecordFlags bits 0x80/0x200 synced to CtxSlotRecords
            // (the 0x200 sync also ORs CtxSlotRecords bit 0x10), then a three-way colour pick on
            // SubRecordFlags bit 0x6000 vs CtxSlotRecords bit 0x6000 -- match: 0x80/0xE0 threshold
            // repaint of a 3-byte colour at subrecord+0x5C..0x5E (mirrored to a per-team-A 3-byte
            // colour at ctx+iVar21*0xF0+0x179C..0x179E when iVar21<6); mismatch and subrecord+0x5C
            // (as signed char) == -0x50: force the 3-byte colour to 0xE0 (same mirror), sync
            // SubRecordFlags bits 0x6000 from CtxSlotRecords, then look CtxSlotRecords bits
            // 0x2000/0x4000 up in one of three columns of Roster.cs's own 12-byte-stride portrait
            // table (Dat80084184Address; the same table FUN_80057a7c reads its own default pair
            // from) and fan the two looked-up values out into subrecord+0x6E/+0x64/+0x74/+0x6C/+0x65/
            // +0x7C/+0x6D/+0x7D/+0x75 (and, when iVar21<6, the matching ctx+iVar21*0xF0+0x17Ax/Bx
            // mirror); mismatch and not -0x50: force the 3-byte colour to 0xB0 (same mirror). Ends
            // by syncing SubRecordFlags bit 1 to CtxSlotRecords bit 1.
            iVar5 = 0;
            psVar12 = param_1 + 0x7e;
            puVar13 = puVar20;
            do
            {
                sVar7 = (short)iVar5;
                sVar8 = (short)PsxRam.ReadU16(psVar12 - 0x5c);
                iVar6 = (int)sVar8;
                iVar21 = (int)(short)PsxRam.ReadU16(param_1 + sVar7 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 2);
                if (iVar6 != iVar21)
                {
                    if (iVar21 < iVar6)
                    {
                        if ((PsxRam.ReadU16(psVar12 - 0x4e) & 0x1f) == 0)
                        {
                            PsxRam.WriteU16(psVar12 - 0x4e, (ushort)(sVar8 * 0x20 + 0x1e));
                        }
                        sVar8 = (short)PsxRam.ReadU16(psVar12 - 0x5c);
                        sVar4 = (short)(sVar8 - 1);
                        if (0x13 < (int)sVar8 - (int)(short)PsxRam.ReadU16(param_1 + sVar7 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 2))
                        {
                            sVar4 = (short)(sVar8 - 0x14);
                        }
                    }
                    else
                    {
                        if ((PsxRam.ReadU16(psVar12 - 0x4e) & 0x1f) != 0)
                        {
                            goto LAB_8005b26c;
                        }
                        sVar4 = (short)(sVar8 + 1);
                        if (0x13 < iVar21 - iVar6)
                        {
                            sVar4 = (short)(sVar8 + 0x14);
                        }
                    }
                    PsxRam.WriteU16(psVar12 - 0x5c, (ushort)sVar4);
                    local_b8[iVar5] = (ushort)(local_b8[iVar5] | 8);
                }
            LAB_8005b26c:
                iVar21 = (int)sVar7;
                if (PsxRam.ReadU16(psVar12 - 0x5a) != PsxRam.ReadU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge))
                {
                    PsxRam.WriteU16(psVar12 - 0x5a, PsxRam.ReadU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxKiGauge));
                    local_b8[iVar21] = (ushort)(local_b8[iVar21] | 0x10);
                }
                if (PsxRam.ReadU16(psVar12 - 0x58) != PsxRam.ReadU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 6))
                {
                    PsxRam.WriteU16(psVar12 - 0x58, PsxRam.ReadU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 6));
                }
                uVar3 = PsxRam.ReadU16(puVar13);
                if ((uVar3 & 0x80) != (PsxRam.ReadU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x80))
                {
                    PsxRam.WriteU16(puVar13, (ushort)(uVar3 ^ 0x80));
                    uVar3 = PsxRam.ReadU16(puVar13);
                }
                if ((uVar3 & 0x200) != (PsxRam.ReadU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x200))
                {
                    PsxRam.WriteU16(puVar13, (ushort)((uVar3 ^ 0x200) | 0x10));
                    PsxRam.WriteU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords,
                        (ushort)(PsxRam.ReadU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) | 0x10));
                }
                iVar6 = iVar21 * 0xf0;
                if ((PsxRam.ReadU16(puVar13) & 0x6000) == (PsxRam.ReadU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x6000))
                {
                    if (0xaf < PsxRam.ReadU8(psVar12 - 2))
                    {
                        PsxRam.WriteU8(psVar12 - 2, 0x80);
                        PsxRam.WriteU8(psVar12 - 1, 0x80);
                        PsxRam.WriteU8(psVar12, 0x80);
                        if (iVar21 < 6)
                        {
                            PsxRam.WriteU8(param_1 + iVar6 + 0x179c, 0x80);
                            PsxRam.WriteU8(param_1 + iVar6 + 0x179d, 0x80);
                            PsxRam.WriteU8(param_1 + iVar6 + 0x179e, 0x80);
                        }
                    }
                }
                else if ((sbyte)PsxRam.ReadU8(psVar12 - 2) == -0x50)
                {
                    PsxRam.WriteU8(psVar12 - 2, 0xe0);
                    PsxRam.WriteU8(psVar12 - 1, 0xe0);
                    PsxRam.WriteU8(psVar12, 0xe0);
                    if (iVar21 < 6)
                    {
                        PsxRam.WriteU8(param_1 + iVar6 + 0x179c, 0xe0);
                        PsxRam.WriteU8(param_1 + iVar6 + 0x179d, 0xe0);
                        PsxRam.WriteU8(param_1 + iVar6 + 0x179e, 0xe0);
                    }
                    uVar3 = PsxRam.ReadU16(puVar13);
                    PsxRam.WriteU16(puVar13, (ushort)(uVar3 & 0x9fff));
                    uVar15 = PsxRam.ReadU16(param_1 + iVar21 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords);
                    PsxRam.WriteU16(puVar13, (ushort)((uVar3 & 0x9fff) | (uVar15 & 0x6000)));

                    int colTable0;
                    int colTable1;
                    if ((uVar15 & 0x2000) == 0)
                    {
                        if ((uVar15 & 0x4000) == 0)
                        {
                            colTable0 = Dat80084184Address;
                            colTable1 = Dat80084184Address + 2;
                        }
                        else
                        {
                            colTable0 = Dat80084184Address + 4;
                            colTable1 = Dat80084184Address + 6;
                        }
                    }
                    else
                    {
                        colTable0 = Dat80084184Address + 8;
                        colTable1 = Dat80084184Address + 0xa;
                    }
                    int col0 = colTable0 + iVar21 * 12;
                    int col1 = colTable1 + iVar21 * 12;

                    PsxRam.WriteU16(psVar12 + 0x10,
                        (ushort)((((short)PsxRam.ReadU16(col0) >> 6) + (PsxRam.ReadU16(col1) >> 4 & 0x10)) & 0x1f));
                    uVar1 = (byte)((PsxRam.ReadU16(col0) & 0x3f) << 2);
                    PsxRam.WriteU8(psVar12 + 0x16, uVar1);
                    PsxRam.WriteU8(psVar12 + 6, uVar1);
                    cVar2 = (sbyte)((sbyte)PsxRam.ReadU16(col0) * 4 + 0x2f);
                    PsxRam.WriteU8(psVar12 + 0x1e, (byte)cVar2);
                    PsxRam.WriteU8(psVar12 + 0xe, (byte)cVar2);
                    uVar3 = PsxRam.ReadU16(col1);
                    PsxRam.WriteU8(psVar12 + 0xf, (byte)uVar3);
                    PsxRam.WriteU8(psVar12 + 7, (byte)uVar3);
                    cVar2 = (sbyte)((sbyte)PsxRam.ReadU16(col1) + 0x2f);
                    PsxRam.WriteU8(psVar12 + 0x1f, (byte)cVar2);
                    PsxRam.WriteU8(psVar12 + 0x17, (byte)cVar2);
                    if (iVar21 < 6)
                    {
                        PsxRam.WriteU16(param_1 + iVar21 * 0xf0 + 0x17ae,
                            (ushort)((((short)PsxRam.ReadU16(col0) >> 6) + (PsxRam.ReadU16(col1) >> 4 & 0x10)) & 0x1f));
                        uVar1 = (byte)((PsxRam.ReadU16(col0) & 0x3f) << 2);
                        PsxRam.WriteU8(param_1 + iVar21 * 0xf0 + 0x17b4, uVar1);
                        PsxRam.WriteU8(param_1 + iVar21 * 0xf0 + 0x17a4, uVar1);
                        cVar2 = (sbyte)((sbyte)PsxRam.ReadU16(col0) * 4 + 0x2f);
                        PsxRam.WriteU8(param_1 + iVar21 * 0xf0 + 0x17bc, (byte)cVar2);
                        PsxRam.WriteU8(param_1 + iVar21 * 0xf0 + 0x17ac, (byte)cVar2);
                        uVar3 = PsxRam.ReadU16(col1);
                        PsxRam.WriteU8(param_1 + iVar6 + 0x17ad, (byte)uVar3);
                        PsxRam.WriteU8(param_1 + iVar6 + 0x17a5, (byte)uVar3);
                        cVar2 = (sbyte)((sbyte)PsxRam.ReadU16(col1) + 0x2f);
                        PsxRam.WriteU8(param_1 + iVar6 + 0x17bd, (byte)cVar2);
                        PsxRam.WriteU8(param_1 + iVar6 + 0x17b5, (byte)cVar2);
                    }
                }
                else
                {
                    PsxRam.WriteU8(psVar12 - 2, 0xb0);
                    PsxRam.WriteU8(psVar12 - 1, 0xb0);
                    PsxRam.WriteU8(psVar12, 0xb0);
                    if (iVar21 < 6)
                    {
                        PsxRam.WriteU8(param_1 + iVar6 + 0x179c, 0xb0);
                        PsxRam.WriteU8(param_1 + iVar6 + 0x179d, 0xb0);
                        PsxRam.WriteU8(param_1 + iVar6 + 0x179e, 0xb0);
                    }
                }
                psVar12 = psVar12 + 0x1c0;
                if ((PsxRam.ReadU16(puVar13) & 1) != (PsxRam.ReadU16(param_1 + sVar7 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 1))
                {
                    PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) ^ 1));
                }
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);

            // 0x8005B598 -- a per-slot countdown/count-up at subrecord+0xE, gated on SubRecordFlags
            // bits 8/2 (down while !8, up while 8 && !4), clamped to [0,4], arming SubRecordFlags bit
            // 2 (down hits 0) or bit 4 (up hits 4). Every taken path (both bit-8 arms; the bit8==0 &&
            // bit2!=0 combination does nothing at all, matching the original: the outer `if` has no
            // else) ends by marking local_b8 bit 0x3c -- reproduced by writing it once at the tail of
            // each taken arm rather than through the original's two forward gotos into a shared tail.
            iVar5 = 0;
            puVar11 = param_1 + 0x2e;
            puVar13 = puVar20;
            do
            {
                uVar3 = PsxRam.ReadU16(puVar13);
                if ((uVar3 & 8) == 0)
                {
                    if ((uVar3 & 2) == 0)
                    {
                        ushort counter = (ushort)(PsxRam.ReadU16(puVar11) - 1);
                        PsxRam.WriteU16(puVar11, counter);
                        if ((int)((uint)counter << 0x10) < 0)
                        {
                            PsxRam.WriteU16(puVar11, 0);
                        }
                        if (PsxRam.ReadU16(puVar11) == 0)
                        {
                            PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) | 2));
                        }
                        local_b8[iVar5] = (ushort)(local_b8[iVar5] | 0x3c);
                    }
                }
                else if ((uVar3 & 4) == 0)
                {
                    ushort counter = (ushort)(PsxRam.ReadU16(puVar11) + 1);
                    PsxRam.WriteU16(puVar11, counter);
                    if (4 < (short)counter)
                    {
                        PsxRam.WriteU16(puVar11, 4);
                    }
                    if (PsxRam.ReadU16(puVar11) == 4)
                    {
                        PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) | 4));
                    }
                    local_b8[iVar5] = (ushort)(local_b8[iVar5] | 0x3c);
                }
                puVar11 = puVar11 + 0x1c0;
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);

            // 0x8005B6A4 -- while SubRecordFlags bit 0x100 is up (the "ordinal just changed" mark
            // Block 6 above raises), count subrecord+0x16 up to 8 and clear the bit once it gets
            // there, marking local_b8 bit 0x3c every frame the bit is up.
            iVar5 = 0;
            psVar12 = param_1 + 0x36;
            puVar13 = puVar20;
            do
            {
                if ((PsxRam.ReadU16(puVar13) & 0x100) != 0)
                {
                    sVar8 = (short)PsxRam.ReadU16(psVar12);
                    PsxRam.WriteU16(psVar12, (ushort)(sVar8 + 1));
                    if (8 < (short)(sVar8 + 1))
                    {
                        PsxRam.WriteU16(psVar12, 8);
                    }
                    if (PsxRam.ReadU16(psVar12) == 8)
                    {
                        PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) & 0xfeff));
                    }
                    local_b8[iVar5] = (ushort)(local_b8[iVar5] | 0x3c);
                }
                psVar12 = psVar12 + 0x1c0;
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);

            // 0x8005B764 -- the per-slot NUMERIC-READOUT TARGET state machine at subrecord+0x24
            // (mode), tracking CtxSlotRecords+0xA (the same [0,99] field the top clamp bounds, read
            // here as `target`) against a shadow copy at subrecord+0xA. Mode bit 1 clear: seed the
            // shadow from `target` once it settles above 1, arming mode bit 1. Mode bit 1 set, bit 2
            // clear, bit 4 clear: a 0..4 tier counter at subrecord+0x28 that, on reaching 4, arms mode
            // bit 4 and, on reaching 8, resets itself, seeds a hold value at subrecord+0x2A / a repeat
            // count of 0xB at subrecord+0x2C, arms mode bit 2, and (when `target` is non-zero) swaps
            // the shadow to it. Mode bits 1+2 set: an 11-tier hold/advance sequence at subrecord+0x2A
            // (values 0x11 down to 0x17 select which comparison runs; reaching 0x17 clears mode bit 2
            // and sets bit 1) whose two goto targets (LAB_8005b880, LAB_8005b890) are two entries into
            // one shared "advance the tier" tail, reproduced below by running that tail once at the
            // end of each branch that reaches it rather than through the original's cross-branch
            // gotos; subrecord+0x2C then counts 0xB down towards 0, wrapping to 0xB at 0x17-worth of
            // ticks. Mode bit 1 clear... already handled above; mode bit 2 set with bit 1 clear cannot
            // occur (bit 2 is only ever armed alongside bit 1).
            iVar5 = 0;
            puVar13 = param_1 + 0x44;
            do
            {
                uVar3 = PsxRam.ReadU16(puVar13);
                sVar8 = (short)iVar5;
                if ((uVar3 & 1) == 0)
                {
                    uVar3 = PsxRam.ReadU16(param_1 + sVar8 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xa);
                    if (uVar3 != PsxRam.ReadU16(puVar13 - 0x1a) && 1 < (short)uVar3)
                    {
                        PsxRam.WriteU16(puVar13 - 0x1a, uVar3);
                        PsxRam.WriteU16(puVar13 + 2, uVar3);
                        PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) | 1));
                    }
                }
                else if ((uVar3 & 2) == 0)
                {
                    if ((uVar3 & 4) == 0)
                    {
                        uVar3 = (ushort)(PsxRam.ReadU16(puVar13 + 4) + 1);
                        PsxRam.WriteU16(puVar13 + 4, uVar3);
                        if (uVar3 == 4)
                        {
                            PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) | 8));
                        }
                        if (PsxRam.ReadU16(puVar13 + 4) == 8)
                        {
                            PsxRam.WriteU16(puVar13 + 6, 0);
                            PsxRam.WriteU16(puVar13 + 8, 0xb);
                            PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) | 4));
                            if (PsxRam.ReadU16(param_1 + sVar8 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xa) != 0)
                            {
                                PsxRam.WriteU16(puVar13 + 2, PsxRam.ReadU16(puVar13 - 0x1a));
                                PsxRam.WriteU16(puVar13 - 0x1a, PsxRam.ReadU16(param_1 + sVar8 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xa));
                            }
                        }
                    }
                    else
                    {
                        ushort target = PsxRam.ReadU16(param_1 + sVar8 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xa);
                        bool advance;
                        if (target == PsxRam.ReadU16(puVar13 - 0x1a))
                        {
                            advance = PsxRam.ReadU16(puVar13 + 6) != 0x11;
                        }
                        else
                        {
                            if (PsxRam.ReadU16(puVar13 + 6) == 0x11)
                            {
                                if (target == 0)
                                {
                                    PsxRam.WriteU16(puVar13 + 6, 0x12);
                                }
                                else
                                {
                                    PsxRam.WriteU16(puVar13 + 6, 6);
                                    PsxRam.WriteU16(puVar13 + 8, 0xc);
                                    PsxRam.WriteU16(puVar13 + 2, PsxRam.ReadU16(puVar13 - 0x1a));
                                    if (target != 0)
                                    {
                                        PsxRam.WriteU16(puVar13 - 0x1a, target);
                                    }
                                }
                            }
                            advance = true;
                        }
                        if (advance)
                        {
                            uVar3 = (ushort)(PsxRam.ReadU16(puVar13 + 6) + 1);
                            PsxRam.WriteU16(puVar13 + 6, uVar3);
                            if (uVar3 == 5 || uVar3 == 0xb)
                            {
                                PsxRam.WriteU16(puVar13 + 6, 0x11);
                                PsxRam.WriteU16(puVar13 + 2, PsxRam.ReadU16(puVar13 - 0x1a));
                            }
                            if (PsxRam.ReadU16(puVar13 + 6) == 0x17)
                            {
                                PsxRam.WriteU16(puVar13, (ushort)((PsxRam.ReadU16(puVar13) & 0xfffb) | 2));
                            }
                        }
                        if ((short)PsxRam.ReadU16(puVar13 + 8) != 0xb)
                        {
                            iVar21 = (short)PsxRam.ReadU16(puVar13 + 8) + 1;
                            PsxRam.WriteU16(puVar13 + 8, (ushort)iVar21);
                            if (iVar21 * 0x10000 >> 0x10 == 0x11)
                            {
                                PsxRam.WriteU16(puVar13 + 8, 0xb);
                            }
                        }
                    }
                }
                else
                {
                    uVar3 = (ushort)(PsxRam.ReadU16(puVar13 + 4) - 1);
                    PsxRam.WriteU16(puVar13 + 4, uVar3);
                    if (uVar3 == 4)
                    {
                        PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) & 0xfff7));
                    }
                    if (PsxRam.ReadU16(puVar13 + 4) == 0xffff)
                    {
                        PsxRam.WriteU16(puVar13 + 4, 0);
                        PsxRam.WriteU16(puVar13 + 2, 0);
                        PsxRam.WriteU16(puVar13 - 0x1a, 0);
                        PsxRam.WriteU16(puVar13, (ushort)(PsxRam.ReadU16(puVar13) & 0xfff8));
                    }
                }
                iVar5 = iVar5 + 1;
                puVar13 = puVar13 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);

            // 0x8005BA88 -- the twelve-slot HUD/portrait sub-loop: FUN_80057a7c and the two
            // FUN_80058120 calls receive the per-slot sub-record itself (puVar13, walking
            // CtxSlotSubRecords same as puVar20 below), FUN_80058338 the raw context plus the slot
            // index (it does its own `ctx + slot*0x1C0 + 0x20` indexing, per its own header). Then
            // subrecord+0x10's low 5 bits count down by one, the same field Block 7's threshold reset
            // seeds to `value*0x20+0x1e`.
            iVar5 = 0;
            puVar11 = param_1 + 0x30;
            puVar13 = puVar20;
            do
            {
                FUN_80057a7c(puVar13, (short)iVar5);
                FUN_80058120(puVar13, 0);
                FUN_80058120(puVar13, 1);
                FUN_80058338(param_1, (short)iVar5);
                puVar13 = puVar13 + 0x1c0;
                if ((PsxRam.ReadU16(puVar11) & 0x1f) != 0)
                {
                    PsxRam.WriteU16(puVar11, (ushort)(PsxRam.ReadU16(puVar11) - 1));
                }
                iVar5 = iVar5 + 1;
                puVar11 = puVar11 + 0x1c0;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);
        }

        // 0x8005BAF4 -- THE PER-SLOT PRIMITIVE SUBMISSION, and it runs on EVERY path, suspended VM
        // included: `ot` is the same `(0x7ff - otz) * 4 + 0x70 + DAT_8008d420` ordering-table formula
        // UpdateCentralGaugeBar's own PART FOUR already carries (see its header for why
        // PsxRam.ReadI32(Dat8008d420Address) currently answers zero), here keyed on the ctx-head
        // halfword at +0x1C rather than ctx+0x3030 -- a second, function-local OTZ this slice has not
        // seen named or written anywhere, so it stays a raw offset. Ten primitives per slot, all
        // drawn from the subrecord FUN_80057a7c/FUN_80058120 above just filled in: subrecord+0x80
        // (only when subrecord+0x24 bit 8 is clear) or +0x198/+0xD0 for the swapped body sprite,
        // +0xA8, +0x58, +0x30 unconditionally, and four more (+0x148, +0x170, +0xF8 and the raw
        // psVar12 cursor at subrecord+0x120) only when subrecord+0x24 bit 4 is set.
        const int Dat8008d420Address = unchecked((int)0x8008D420);

        iVar5 = 0;
        psVar12 = param_1 + 0x140;
        ot = (int)((0x7ff - (uint)PsxRam.ReadU16(param_1 + 0x1c)) * 4 + 0x70 + PsxRam.ReadI32(Dat8008d420Address));
        do
        {
            if ((PsxRam.ReadU16(psVar12 - 0xfc) & 8) == 0)
            {
                LibGpu.AddPrim(ot, puVar20 + 0x80);
                puVar13 = puVar20 + 0x198;
            }
            else
            {
                puVar13 = puVar20 + 0xd0;
            }
            LibGpu.AddPrim(ot, puVar13);
            LibGpu.AddPrim(ot, puVar20 + 0xa8);
            LibGpu.AddPrim(ot, puVar20 + 0x58);
            LibGpu.AddPrim(ot, puVar20 + 0x30);
            if ((PsxRam.ReadU16(psVar12 - 0xfc) & 4) != 0)
            {
                LibGpu.AddPrim(ot, puVar20 + 0x148);
                LibGpu.AddPrim(ot, puVar20 + 0x170);
                LibGpu.AddPrim(ot, puVar20 + 0xf8);
                LibGpu.AddPrim(ot, psVar12);
            }
            psVar12 = psVar12 + 0x1c0;
            iVar5 = iVar5 + 1;
            puVar20 = puVar20 + 0x1c0;
        } while (iVar5 * 0x10000 >> 0x10 < 0xc);

        // 0x8005BC1C -- while the animation VM is suspended, the manager's only remaining act is the
        // tally icon's own primitive submission (see the tally-state block far below for what
        // CtxTallyState bit 4 means): no bit, nothing else runs at all this frame; bit set, submit
        // the ten tally primitives at ctx+0x2DCC and fall into the same LAB_8005c6a0 tail every other
        // path reaches (the extra-digit check at the very end of the function).
        iVar5 = 0;
        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            psVar12 = param_1 + 0x2dcc;
            if ((PsxRam.ReadU16(param_1 + BattleState.CtxTallyState) & 4) == 0)
            {
                return;
            }
            iVar5 = 0;
            do
            {
                LibGpu.AddPrim(ot, psVar12);
                iVar5 = iVar5 + 1;
                psVar12 = psVar12 + 0x28;
            } while (iVar5 * 0x10000 >> 0x10 < 10);
            goto LAB_8005c6a0;
        }

        // 0x8005BC70 -- walk the published cursor at ctx+0x1A forward, mod 12, until it lands on a
        // slot that is both alive (record bit 0) and marked (record bit 0x200), or exhausts twelve
        // tries.
        do
        {
            if ((PsxRam.ReadU16(param_1 + (short)PsxRam.ReadU16(param_1 + 0x1a) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 1) != 0
                && (PsxRam.ReadU16(param_1 + (short)PsxRam.ReadU16(param_1 + 0x1a) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x200) != 0)
            {
                break;
            }
            iVar21 = (short)PsxRam.ReadU16(param_1 + 0x1a) + 1;
            PsxRam.WriteU16(param_1 + 0x1a, (ushort)iVar21);
            iVar5 = iVar5 + 1;
            if (0xb < iVar21 * 0x10000 >> 0x10)
            {
                PsxRam.WriteU16(param_1 + 0x1a, 0);
            }
        } while (iVar5 * 0x10000 >> 0x10 < 0xc);

        // 0x8005BCF8 -- outside state 1 (THE ROUND), CtxRoundRequest is force-cleared every frame.
        if (PsxRam.ReadU16(param_1 + BattleState.CtxState) != 1)
        {
            PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest, 0);
        }

        // 0x8005BD1C -- CtxRoundRequest bit 0: THE TEAM-A TALLY. Count marked (0x200) and just-downed
        // (0x1000) slots 0..5; subtract 3 (the "3 vs the rest" handicap the central-gauge scaling
        // above also uses); while more than 3 remain just-downed, walk slots 0..5 again converting
        // excess 0x1000 marks to a plain 0x10 (clearing 0x1000 and 0x200 on both CtxSlotRecords and,
        // in lockstep, SubRecordFlags bit 0x200) until the excess is used up. CtxRoundRequest bit 2
        // is armed either way. Bit 1: THE TEAM-B TALLY, the same shape over slots 6..11.
        if (PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) != 0)
        {
            iVar5 = 0;
            if ((PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 1) != 0)
            {
                iVar6 = 0;
                iVar21 = 0;
                do
                {
                    if ((PsxRam.ReadU16(param_1 + (iVar21 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x200) != 0)
                    {
                        iVar5 = iVar5 + 1;
                    }
                    iVar6 = iVar6 + 1;
                    if ((PsxRam.ReadU16(param_1 + (iVar21 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x1000) != 0)
                    {
                        iVar5 = iVar5 + 1;
                    }
                    iVar21 = iVar6 * 0x10000;
                } while (iVar6 * 0x10000 >> 0x10 < 6);
                iVar5 = iVar5 + -3;
                iVar21 = 0;
                if (0 < iVar5 * 0x10000)
                {
                    iVar6 = 0;
                    do
                    {
                        iVar6 = iVar6 >> 0x10;
                        iVar21 = iVar21 + 1;
                        if ((PsxRam.ReadU16(param_1 + iVar6 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x1000) != 0)
                        {
                            PsxRam.WriteU16(param_1 + iVar6 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords,
                                (ushort)((PsxRam.ReadU16(param_1 + iVar6 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0xedff) | 0x10));
                            iVar5 = iVar5 + -1;
                            PsxRam.WriteU16(param_1 + iVar6 * BattleState.CtxSlotSubRecordStride + BattleState.CtxSlotSubRecords,
                                (ushort)(PsxRam.ReadU16(param_1 + iVar6 * BattleState.CtxSlotSubRecordStride + BattleState.CtxSlotSubRecords) & 0xfdff));
                        }
                        iVar6 = iVar21 * 0x10000;
                    } while (0 < iVar5 << 0x10);
                }
                PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                    (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 0xfffffffe | 4));
            }
            iVar5 = 0;
            if ((PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 2) != 0)
            {
                iVar6 = 6;
                iVar21 = 0x60000;
                do
                {
                    if ((PsxRam.ReadU16(param_1 + (iVar21 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x200) != 0)
                    {
                        iVar5 = iVar5 + 1;
                    }
                    iVar6 = iVar6 + 1;
                    if ((PsxRam.ReadU16(param_1 + (iVar21 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x1000) != 0)
                    {
                        iVar5 = iVar5 + 1;
                    }
                    iVar21 = iVar6 * 0x10000;
                } while (iVar6 * 0x10000 >> 0x10 < 0xc);
                iVar5 = iVar5 + -3;
                iVar21 = 6;
                if (0 < iVar5 * 0x10000)
                {
                    iVar6 = 0x60000;
                    do
                    {
                        iVar6 = iVar6 >> 0x10;
                        iVar21 = iVar21 + 1;
                        if ((PsxRam.ReadU16(param_1 + iVar6 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x1000) != 0)
                        {
                            PsxRam.WriteU16(param_1 + iVar6 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords,
                                (ushort)((PsxRam.ReadU16(param_1 + iVar6 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0xedff) | 0x10));
                            iVar5 = iVar5 + -1;
                            PsxRam.WriteU16(param_1 + iVar6 * BattleState.CtxSlotSubRecordStride + BattleState.CtxSlotSubRecords,
                                (ushort)(PsxRam.ReadU16(param_1 + iVar6 * BattleState.CtxSlotSubRecordStride + BattleState.CtxSlotSubRecords) & 0xfdff));
                        }
                        iVar6 = iVar21 * 0x10000;
                    } while (0 < iVar5 << 0x10);
                }
                PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                    (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 0xfffffffd | 4));
            }

            // 0x8005BF10 -- CtxRoundRequest bit 2: THE WIN/LOSS COMPUTE. uVar3 is the OR of every
            // slot's own record flags. Bit 0x10 up anywhere means at least one slot is marked
            // "gone" (record bit 4, 0x10): sweep all twelve slots for the pattern record&0x210==0x10,
            // arming CtxRoundRequest bit 0x20 the instant any slot matches it (before the 0x8000 test
            // that follows), and latch bVar16 true only when that same slot's own bit 0x8000 is also
            // up. bVar16 selects CtxRoundRequest bit 8 + CtxFlags bit 0x8000000 (a hit was found) or
            // bit 0x80 (none was). The mirrored second sweep does the same for pattern
            // record&0x210==0x210 into bit 0x10 / bit 0x100, its own CtxFlags 0x8000000 raise gated on
            // bit 8 not already being set by the first sweep. Bit 2 itself is cleared unconditionally
            // at the end. Bits 0x100/0x180 then gate FUN_80026d98 and are cleared right after it runs;
            // FUN_80026d98 itself is BLOCKED (see its own declaration below) so this call reproduces
            // the gate but not the callee.
            uVar3 = 0;
            if ((PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 4) != 0)
            {
                iVar5 = 0;
                do
                {
                    sVar8 = (short)iVar5;
                    iVar5 = iVar5 + 1;
                    uVar3 = (ushort)(uVar3 | PsxRam.ReadU16(param_1 + sVar8 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords));
                } while (iVar5 * 0x10000 >> 0x10 < 0xc);

                bVar16 = false;
                if ((uVar3 & 0x10) != 0)
                {
                    iVar21 = 0;
                    iVar5 = 0;
                    do
                    {
                        if ((PsxRam.ReadU16(param_1 + (iVar5 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x210) == 0x10)
                        {
                            PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                                (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) | 0x20));
                            if ((PsxRam.ReadU16(param_1 + (iVar5 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x8000) != 0)
                            {
                                bVar16 = true;
                            }
                        }
                        iVar21 = iVar21 + 1;
                        iVar5 = iVar21 * 0x10000;
                    } while (iVar21 * 0x10000 >> 0x10 < 0xc);

                    if (bVar16)
                    {
                        PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                            (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) | 8));
                        PsxRam.WriteI32(param_1 + BattleState.CtxFlags,
                            (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxFlags) | 0x8000000));
                    }
                    else
                    {
                        PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                            (int)(((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 0xffffffdf) | 0x80));
                    }

                    bVar16 = false;
                    iVar21 = 0;
                    iVar5 = 0;
                    do
                    {
                        if ((PsxRam.ReadU16(param_1 + (iVar5 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x210) == 0x210)
                        {
                            PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                                (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) | 0x40));
                            if ((PsxRam.ReadU16(param_1 + (iVar5 >> 0x10) * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x8000) != 0)
                            {
                                bVar16 = true;
                            }
                        }
                        iVar21 = iVar21 + 1;
                        iVar5 = iVar21 * 0x10000;
                    } while (iVar21 * 0x10000 >> 0x10 < 0xc);

                    if (bVar16)
                    {
                        PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                            (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) | 0x10));
                        PsxRam.WriteI32(param_1 + BattleState.CtxFlags,
                            (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxFlags) | 0x8000000));
                        if ((PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 8) == 0)
                        {
                            PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                                (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) | 0x100));
                        }
                    }
                    else if ((PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 8) == 0)
                    {
                        PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                            (int)(((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 0xffffffbf) | 0x100));
                    }
                }
                PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                    (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 0xfffffffb));
            }

            if ((PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 0x180) != 0)
            {
                DiagFun80026d98Calls++;
                FighterSubstitution.RunFighterSubstitution(
                    param_1, (uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest));
                PsxRam.WriteI32(param_1 + BattleState.CtxRoundRequest,
                    (int)((uint)PsxRam.ReadI32(param_1 + BattleState.CtxRoundRequest) & 0xfffffe7f));
            }
        }

        // 0x8005C048 -- gated on CtxFlags bit 0x100000 (and none of 0x18000008), THE TALLY ICON'S OWN
        // MAX-SCAN: walk up to twelve slots (DAT_801ff100 != 0 -- SELECT.EXE's own handover word, read
        // through SharedHighRam the same way the pad-override block above does -- stops the walk after
        // seven when a mode is armed) looking for a new maximum of CtxSlotRecords+0xA (the same [0,99]
        // numeric-readout target field). On a new maximum: store it in CtxTallyValue, arm CtxTallyPhase
        // = 2 and CtxTallyHold = 0x40, seed the first digit's four glyph-position bytes at
        // ctx+0x2F40..0x2F58 from the ones place, seed its four coordinate halfwords at
        // ctx+0x2F3C..0x2F58 from DAT_800842b0/b2 (part of the same contiguous keyframe span embedded
        // below), and -- only when the value is 10 or more -- the second digit's own four glyph bytes
        // at ctx+0x2F68..0x2F80 and four coordinate halfwords at ctx+0x2F64..0x2F7E from
        // DAT_800842b4/b6. Ends by arming CtxTallyState bit 0 (growing) when bit 2 is clear, and
        // demoting bit 3 (shrinking) to bit 0 when the icon was already shrinking.
        if ((PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x100000) != 0
            && (PsxRam.ReadI32(param_1 + BattleState.CtxFlags) & 0x18000008) == 0)
        {
            iVar5 = 0;
            psVar12 = param_1 + 0x2f4e;
            do
            {
                if (SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] != 0 && 6 < (short)iVar5)
                {
                    break;
                }
                psVar17 = psVar12;
                if ((int)(uint)PsxRam.ReadU16(param_1 + BattleState.CtxTallyValue)
                    < (int)(short)PsxRam.ReadU16(param_1 + (short)iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xa))
                {
                    PsxRam.WriteU16(param_1 + BattleState.CtxTallyValue,
                        PsxRam.ReadU16(param_1 + (short)iVar5 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords + 0xa));
                    sVar8 = (short)PsxRam.ReadU16(param_1 + BattleState.CtxTallyValue);
                    PsxRam.WriteU16(param_1 + BattleState.CtxTallyPhase, 2);
                    PsxRam.WriteU16(param_1 + BattleState.CtxTallyHold, 0x40);
                    cVar2 = (sbyte)((sbyte)((uint)((sVar8 % 10) * 0x10000) >> 0x10) * 0x18);
                    PsxRam.WriteU8(psVar12 + 2, (byte)cVar2);
                    PsxRam.WriteU8(psVar12 - 0xe, (byte)cVar2);
                    PsxRam.WriteU8(psVar12 + 0xa, (byte)(cVar2 + 0x18));
                    PsxRam.WriteU8(psVar12 - 6, (byte)(cVar2 + 0x18));
                    sVar7 = (short)PsxRam.ReadU16(Dat800842b0Address);
                    iVar21 = ((int)sVar8 / 10) * 0x10000;
                    PsxRam.WriteU16(psVar12 - 2, (ushort)PsxRam.ReadU16(Dat800842b0Address));
                    PsxRam.WriteU16(psVar12 - 0x12, (ushort)sVar7);
                    sVar8 = (short)PsxRam.ReadU16(Dat800842b2Address);
                    PsxRam.WriteU16(psVar12 - 8, 0xa8);
                    PsxRam.WriteU16(psVar12 - 0x10, 0xa8);
                    PsxRam.WriteU16(psVar12 + 8, 0xb8);
                    PsxRam.WriteU16(psVar12, 0xb8);
                    PsxRam.WriteU16(psVar12 + 6, (ushort)sVar8);
                    PsxRam.WriteU16(psVar12 - 0xa, (ushort)sVar8);
                    if (iVar21 >> 0x10 != 0)
                    {
                        psVar17 = psVar12 + 0x28;
                        cVar2 = (sbyte)((sbyte)((uint)iVar21 >> 0x10) * 0x18);
                        PsxRam.WriteU8(psVar12 + 0x2a, (byte)cVar2);
                        PsxRam.WriteU8(psVar12 + 0x1a, (byte)cVar2);
                        PsxRam.WriteU8(psVar12 + 0x32, (byte)(cVar2 + 0x18));
                        PsxRam.WriteU8(psVar12 + 0x22, (byte)(cVar2 + 0x18));
                        sVar8 = (short)PsxRam.ReadU16(Dat800842b4Address);
                        PsxRam.WriteU16(psVar12 + 0x26, PsxRam.ReadU16(Dat800842b4Address));
                        PsxRam.WriteU16(psVar12 + 0x16, (ushort)sVar8);
                        sVar8 = (short)PsxRam.ReadU16(Dat800842b6Address);
                        PsxRam.WriteU16(psVar12 + 0x20, 0xa8);
                        PsxRam.WriteU16(psVar12 + 0x18, 0xa8);
                        PsxRam.WriteU16(psVar12 + 0x30, 0xb8);
                        PsxRam.WriteU16(psVar17, 0xb8);
                        PsxRam.WriteU16(psVar12 + 0x2e, (ushort)sVar8);
                        PsxRam.WriteU16(psVar12 + 0x1e, (ushort)sVar8);
                    }
                    uVar3 = PsxRam.ReadU16(param_1 + BattleState.CtxTallyState);
                    if ((uVar3 & 4) == 0)
                    {
                        PsxRam.WriteU16(param_1 + BattleState.CtxTallyState, (ushort)((uVar3 & 0xfff5) | 5));
                        uVar3 = PsxRam.ReadU16(param_1 + BattleState.CtxTallyState);
                    }
                    if ((uVar3 & 8) != 0)
                    {
                        PsxRam.WriteU16(param_1 + BattleState.CtxTallyState, (ushort)((uVar3 & 0xfff5) | 1));
                    }
                }
                iVar5 = iVar5 + 1;
                psVar12 = psVar17;
            } while (iVar5 * 0x10000 >> 0x10 < 0xc);
        }

        // 0x8005C0D0 -- THE TALLY ICON'S OWN DISPLAY STATE MACHINE, on CtxTallyState. Bit 2 clear:
        // nothing left to do this frame, return outright (no primitive submission at all -- the two
        // callers that reach this function with the VM suspended already special-cased that, see the
        // early-return block above). Bit 1 set: THE INSTANT SHOW -- when neither bit 0 (growing) nor
        // bit 3 (shrinking) is up, snap all sixteen ctx+0x2F3C..0x2F7E pose halfwords to their held
        // values (0xA8 or 0xB8, matching InitCentralGaugeBar's own neutral-pose convention); either
        // way, force the state to bit 3 (shrinking) and write it through the shared LAB_8005c624 tail
        // (duplicated at each of its three call sites below rather than reached by goto, since C#
        // cannot jump into a sibling branch the way the original does here twice).
        //
        // Bit 1 clear, bit 0 clear (shrinking): once per 8-tick hold (CtxTallyPhase != 4 gates a
        // one-shot), step every one of those sixteen halfwords by its own signed delta and add 2 to
        // CtxTallyPhase; then count CtxTallyHold down, and once it hits zero arm bit 1 (instant-show
        // pending) through the shared tail. Bit 0 set, CtxTallyPhase == 0x10: the hold has fully
        // drained -- clear bit 0, reset CtxTallyPhase and reseed CtxTallyHold to 0x40. Bit 0 set,
        // CtxTallyPhase != 0x10 (shrinking, not yet home): the SHRINK keyframe interpolation, reading
        // DAT_80084234's own pose table with an 11-step (0 or 8-based) selector and subtracting the
        // interpolated delta from each of the eleven ctx+0x2DDC-based primitive slots; decrement
        // CtxTallyPhase by 2.
        //
        // Bit 1 clear, bit 0 set (growing): the mirrored GROW keyframe interpolation. "Mirrored" is
        // ONLY the phase direction -- CtxTallyPhase is incremented by 2 here and decremented by 2 on
        // the shrink path. The interpolation itself is IDENTICAL: both loops SUBTRACT the delta,
        // `sVar8 - (short)(iVar14 / iVar19)`, against the same table and the same selector. An
        // earlier version of this comment said the grow loop added; the code never did, and reading
        // "mirrored" as "opposite sign" is exactly the wrong inference to leave lying here.
        uVar3 = PsxRam.ReadU16(param_1 + BattleState.CtxTallyState);
        if ((uVar3 & 4) == 0)
        {
            return;
        }
        if ((uVar3 & 2) != 0)
        {
            if ((uVar3 & 9) == 0)
            {
                PsxRam.WriteU16(param_1 + BattleState.CtxTallyPhase, 0xe);
                PsxRam.WriteU16(param_1 + 0x2f46, 0xa8);
                PsxRam.WriteU16(param_1 + 0x2f3e, 0xa8);
                PsxRam.WriteU16(param_1 + 0x2f56, 0xb8);
                PsxRam.WriteU16(param_1 + 0x2f4e, 0xb8);
                PsxRam.WriteU16(param_1 + 0x2f6e, 0xa8);
                PsxRam.WriteU16(param_1 + 0x2f66, 0xa8);
                PsxRam.WriteU16(param_1 + 0x2f7e, 0xb8);
                PsxRam.WriteU16(param_1 + 0x2f76, 0xb8);
            }
            uVar3 = (ushort)((PsxRam.ReadU16(param_1 + BattleState.CtxTallyState) & 0xfffc) | 8);
            PsxRam.WriteU16(param_1 + BattleState.CtxTallyState, uVar3);
        }
        else if ((uVar3 & 1) == 0)
        {
            if ((uVar3 & 8) == 0)
            {
                if (PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) != 4)
                {
                    PsxRam.WriteU16(param_1 + 0x2f3c, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f3c) - 1));
                    PsxRam.WriteU16(param_1 + 0x2f44, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f44) + 7));
                    PsxRam.WriteU16(param_1 + 0x2f4c, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f4c) - 1));
                    PsxRam.WriteU16(param_1 + 0x2f3e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f3e) - 4));
                    PsxRam.WriteU16(param_1 + 0x2f54, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f54) + 7));
                    PsxRam.WriteU16(param_1 + 0x2f4e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f4e) + 4));
                    PsxRam.WriteU16(param_1 + 0x2f46, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f46) - 4));
                    PsxRam.WriteU16(param_1 + 0x2f64, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f64) - 7));
                    PsxRam.WriteU16(param_1 + 0x2f56, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f56) + 4));
                    PsxRam.WriteU16(param_1 + 0x2f6c, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f6c) + 1));
                    PsxRam.WriteU16(param_1 + 0x2f74, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f74) - 7));
                    PsxRam.WriteU16(param_1 + 0x2f66, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f66) - 4));
                    PsxRam.WriteU16(param_1 + 0x2f7c, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f7c) + 1));
                    PsxRam.WriteU16(param_1 + 0x2f76, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f76) + 4));
                    PsxRam.WriteU16(param_1 + 0x2f6e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f6e) - 4));
                    PsxRam.WriteU16(param_1 + 0x2f7e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x2f7e) + 4));
                    PsxRam.WriteU16(param_1 + BattleState.CtxTallyPhase, (ushort)(PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) + 2));
                }
                PsxRam.WriteU16(param_1 + BattleState.CtxTallyHold, (ushort)((short)PsxRam.ReadU16(param_1 + BattleState.CtxTallyHold) - 1));
                if (PsxRam.ReadU16(param_1 + BattleState.CtxTallyHold) == 0)
                {
                    uVar3 = (ushort)(PsxRam.ReadU16(param_1 + BattleState.CtxTallyState) | 2);
                    PsxRam.WriteU16(param_1 + BattleState.CtxTallyState, uVar3);
                }
            }
            else if (PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) == 0)
            {
                uVar3 = (ushort)(uVar3 & 0xfff0);
                PsxRam.WriteU16(param_1 + BattleState.CtxTallyState, uVar3);
            }
            else
            {
                iVar5 = 0;
                if ((ushort)PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) < 9)
                {
                    sVar8 = (short)PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase);
                    iVar21 = 0x16;
                }
                else
                {
                    iVar5 = 0x16;
                    iVar21 = 0x2c;
                    sVar8 = (short)(PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) - 8);
                }
                iVar6 = 0;
                iVar19 = (int)sVar8;
                psVar12 = param_1 + 0x2ddc;

                // trap(0x1c00) / trap(0x1800) -- the same safe-division trap pair FUN_80058120's own
                // header documents above, guarding a runtime-variable divisor the compiler could not
                // fold into a magic multiply. `iVar14 / iVar19` below reaches the same halt through
                // C#'s own DivideByZeroException / OverflowException instead of a guarded break.
                do
                {
                    sVar8 = (short)PsxRam.ReadU16(Dat80084234Address + iVar21 * 2);
                    iVar14 = (int)sVar8 - (int)(short)PsxRam.ReadU16(Dat80084234Address + iVar5 * 2);
                    sVar7 = (short)PsxRam.ReadU16(Dat80084234Address + (iVar21 + 1) * 2);
                    iVar9 = (int)sVar7 - (int)(short)PsxRam.ReadU16(Dat80084234Address + (iVar5 + 1) * 2);
                    iVar21 = iVar21 + 2;
                    iVar5 = iVar5 + 2;
                    iVar6 = iVar6 + 1;
                    sVar8 = (short)(sVar8 - (short)(iVar14 / iVar19));
                    PsxRam.WriteU16(psVar12 + 8, (ushort)sVar8);
                    PsxRam.WriteU16(psVar12 - 8, (ushort)sVar8);
                    sVar7 = (short)(sVar7 - (short)(iVar9 / iVar19));
                    PsxRam.WriteU16(psVar12 + 0x10, (ushort)sVar7);
                    PsxRam.WriteU16(psVar12, (ushort)sVar7);
                    psVar12 = psVar12 + 0x28;
                } while (iVar6 * 0x10000 >> 0x10 < 0xb);
                PsxRam.WriteU16(param_1 + BattleState.CtxTallyPhase, (ushort)(PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) - 2));
            }
        }
        else if (PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) == 0x10)
        {
            PsxRam.WriteU16(param_1 + BattleState.CtxTallyState, (ushort)(uVar3 & 0xfffe));
            PsxRam.WriteU16(param_1 + BattleState.CtxTallyPhase, 0);
            PsxRam.WriteU16(param_1 + BattleState.CtxTallyHold, 0x40);
        }
        else
        {
            iVar5 = 0;
            if ((ushort)PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) < 9)
            {
                sVar8 = (short)PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase);
                iVar21 = 0x16;
            }
            else
            {
                iVar5 = 0x16;
                iVar21 = 0x2c;
                sVar8 = (short)(PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) - 8);
            }
            iVar6 = 0;
            iVar19 = (int)sVar8;
            psVar12 = param_1 + 0x2ddc;
            do
            {
                sVar8 = (short)PsxRam.ReadU16(Dat80084234Address + iVar21 * 2);
                iVar14 = (int)sVar8 - (int)(short)PsxRam.ReadU16(Dat80084234Address + iVar5 * 2);
                sVar7 = (short)PsxRam.ReadU16(Dat80084234Address + (iVar21 + 1) * 2);
                iVar9 = (int)sVar7 - (int)(short)PsxRam.ReadU16(Dat80084234Address + (iVar5 + 1) * 2);
                iVar21 = iVar21 + 2;
                iVar5 = iVar5 + 2;
                iVar6 = iVar6 + 1;
                sVar8 = (short)(sVar8 - (short)(iVar14 / iVar19));
                PsxRam.WriteU16(psVar12 + 8, (ushort)sVar8);
                PsxRam.WriteU16(psVar12 - 8, (ushort)sVar8);
                sVar7 = (short)(sVar7 - (short)(iVar9 / iVar19));
                PsxRam.WriteU16(psVar12 + 0x10, (ushort)sVar7);
                PsxRam.WriteU16(psVar12, (ushort)sVar7);
                psVar12 = psVar12 + 0x28;
            } while (iVar6 * 0x10000 >> 0x10 < 0xb);
            PsxRam.WriteU16(param_1 + BattleState.CtxTallyPhase, (ushort)(PsxRam.ReadU16(param_1 + BattleState.CtxTallyPhase) + 2));
        }

        // 0x8005C674 -- THE FINAL SUBMISSION, on every path that reaches here: ten primitives at
        // ctx+0x2DCC (stride 0x28), then, only while CtxTallyValue is 10 or more, the eleventh --
        // LAB_8005c6a0, the same tail the suspended-VM early exit above jumps into directly.
        psVar12 = param_1 + 0x2dcc;
        iVar5 = 0;
        do
        {
            LibGpu.AddPrim(ot, psVar12);
            iVar5 = iVar5 + 1;
            psVar12 = psVar12 + 0x28;
        } while (iVar5 * 0x10000 >> 0x10 < 10);
    LAB_8005c6a0:
        if (9 < (ushort)PsxRam.ReadU16(param_1 + BattleState.CtxTallyValue))
        {
            LibGpu.AddPrim(ot, psVar12);
        }
    }

    // GHIDRA: DAT_80083e1c @ 0x80083E1C (VS.EXE) -- RunBattleManagerFrame's own per-ordinal reseed table,
    // twelve rows of two signed halfwords, 4-byte stride, indexed by SubRecordOrdinal (values 0, 1,
    // 2, 5, 6, 7, 8, 9 are the only ones the function ever stores there; the table itself runs the
    // full twelve rows, the same shape every other twelve-slot table in this file uses). Checked
    // with find-cross-references before embedding: both DAT_80083e1c and DAT_80083e1e have exactly
    // ONE reference in the whole overlay, both from this function (0x8005B124 and 0x8005B13C). The
    // span is closed on both sides by neighbours this port already owns: VS_EXE/Roster.cs's own
    // DAT_80083cf0 record ends at 0x80083E1C exactly (0x80083CF0 + 0x12C), and this file's own
    // Dat80083e4cAddress table begins at 0x80083E4C -- 0x80083E1C..0x80083E4C is the whole gap, and
    // this table fills every byte of it.
    private const int Dat80083e1cAddress = unchecked((int)0x80083E1C);
    private const int Dat80083e1eAddress = unchecked((int)0x80083E1E);

    internal static readonly byte[] DAT_80083e1c = LibGpu.RamRegion(Dat80083e1cAddress, new byte[]
    {
        0x0A, 0x00, 0x32, 0x00, 0x0A, 0x00, 0x5A, 0x00, 0x0A, 0x00, 0x82, 0x00, 0x74, 0xFF, 0x50, 0x00,
        0x74, 0xFF, 0x50, 0x00, 0x74, 0xFF, 0x50, 0x00, 0x36, 0x01, 0x32, 0x00, 0x36, 0x01, 0x5A, 0x00,
        0x36, 0x01, 0x82, 0x00, 0xCC, 0x01, 0x50, 0x00, 0xCC, 0x01, 0x50, 0x00, 0xCC, 0x01, 0x50, 0x00,
    });

    // GHIDRA: DAT_80084234 @ 0x80084234 (VS.EXE) -- the tally icon's own keyframe/pose table, read
    // by the CtxTallyState interpolation as pairs of signed halfwords (x, y) at a plain 2-byte
    // stride, and ALSO read by the tally icon's own max-scan (0x8005C10C) as four individually-named
    // scalars -- DAT_800842b0/b2/b4/b6 are simply the LAST FOUR HALFWORDS of this same 132-byte span,
    // not a second table: 0x80084234 + 0x7C = 0x800842B0, and Ghidra gives each of the four its own
    // symbol only because that later code reaches them by direct name rather than by the
    // interpolation loop's own computed offset. The whole span is single-owner (find-cross-references
    // on 0x80084234 and on 0x800842B0 each return exactly this function) and closes exactly on this
    // file's own already-declared Dat800842b8Address (0x80084234 + 0x84 = 0x800842B8) -- the same
    // kind of exact-boundary check PTR_DAT_80083f90's own comment above already uses.
    private const int Dat80084234Address = unchecked((int)0x80084234);
    private const int Dat800842b0Address = Dat80084234Address + 0x7C;
    private const int Dat800842b2Address = Dat80084234Address + 0x7E;
    private const int Dat800842b4Address = Dat80084234Address + 0x80;
    private const int Dat800842b6Address = Dat80084234Address + 0x82;

    internal static readonly byte[] DAT_80084234 = LibGpu.RamRegion(Dat80084234Address, new byte[]
    {
        0xE0, 0xFF, 0xF0, 0xFF, 0xE0, 0xFF, 0xF0, 0xFF, 0xE0, 0xFF, 0xF0, 0xFF, 0xE0, 0xFF, 0xF0, 0xFF,
        0xE0, 0xFF, 0xF0, 0xFF, 0xE0, 0xFF, 0xF0, 0xFF, 0xE0, 0xFF, 0xF0, 0xFF, 0xE0, 0xFF, 0xF0, 0xFF,
        0xE0, 0xFF, 0xF0, 0xFF, 0x60, 0x01, 0x70, 0x01, 0x50, 0x01, 0x60, 0x01, 0x10, 0x00, 0x20, 0x00,
        0x10, 0x00, 0x20, 0x00, 0x10, 0x00, 0x20, 0x00, 0x10, 0x00, 0x20, 0x00, 0x10, 0x00, 0x20, 0x00,
        0x10, 0x00, 0x20, 0x00, 0x10, 0x00, 0x20, 0x00, 0x10, 0x00, 0x20, 0x00, 0x10, 0x00, 0x20, 0x00,
        0x20, 0x01, 0x30, 0x01, 0x10, 0x01, 0x20, 0x01, 0x10, 0x00, 0x20, 0x00, 0x20, 0x00, 0x30, 0x00,
        0x30, 0x00, 0x40, 0x00, 0x40, 0x00, 0x50, 0x00, 0x50, 0x00, 0x60, 0x00, 0x60, 0x00, 0x70, 0x00,
        0x70, 0x00, 0x80, 0x00, 0x80, 0x00, 0x90, 0x00, 0x90, 0x00, 0xA0, 0x00, 0x10, 0x01, 0x2A, 0x01,
        0x02, 0x01, 0x10, 0x01,
    });

    // GHIDRA: FUN_80026d98 @ 0x80026D98 (VS.EXE)
    // NO LONGER DECLARED HERE. It is CLOSED, in VS_EXE/FighterSubstitution.cs, under the name
    // RunFighterSubstitution, together with the FUN_80027340 it depends on -- the one function in
    // the whole overlay that writes a fighter's +0x144, which is the guard FighterTask's phase 1
    // tests and was measured to fail on every frame.
    //
    // The stub that used to sit here took ONE parameter. The original takes two: `lw a1,0x2d60(s2)`
    // at 0x8005BFDC loads CtxRoundRequest into a1 immediately before the `jal`, and the callee
    // switches on four of its bits. The call site below now passes it.

    // =====================================================================================
    // THREE OF RunBattleManagerFrame's OWN CALLEES — per-slot HUD/portrait helpers, called from inside its
    // per-slot sub-loop at 0x8005BA88..0x8005BAD8, twelve iterations, `puVar13` walking
    // BattleState.CtxSlotSubRecords at BattleState.CtxSlotSubRecordStride per slot (the base RunBattleManagerFrame
    // itself now closes). None of these three is RunBattleManagerFrame itself.
    // =====================================================================================

    // GHIDRA: DAT_80083e4c @ 0x80083E4C (VS.EXE) — the first of FUN_80057a7c's two per-character
    // tables, nine rows of eight signed halfwords, stride 0x10. Checked with find-cross-references
    // rather than assumed: PTR_DAT_80083edc and PTR_DAT_80083f90 below each have exactly ONE
    // reference in the whole overlay, both from FUN_80057a7c, so the whole contiguous span
    // 0x80083E4C..0x80083FB4 (824 bytes: two 9x16-byte tables and their two 9x4-byte pointer
    // tables) belongs to that function alone — nothing else in the port can already own it, and
    // nothing here redeclares a byte any other file already claims. The bytes are read straight out
    // of the image with read-memory and declared explicitly, the same way VS_EXE/Roster.cs already
    // declares its own coordinate table, rather than left to PsxExeImage's fallback resolution:
    // explicit here, reviewable in the source, and correct even on a path that reaches this file
    // before the image is armed.
    private const int Dat80083e4cAddress = unchecked((int)0x80083E4C);

    internal static readonly byte[] DAT_80083e4c = LibGpu.RamRegion(Dat80083e4cAddress, new byte[]
    {
        0x00, 0x00, 0xF6, 0xFF, 0x40, 0x00, 0xF6, 0xFF, 0x00, 0x00, 0x08, 0x00, 0x40, 0x00, 0x08, 0x00,
        0x00, 0x00, 0xF9, 0xFF, 0x40, 0x00, 0xF9, 0xFF, 0x00, 0x00, 0x06, 0x00, 0x40, 0x00, 0x06, 0x00,
        0x01, 0x00, 0xFB, 0xFF, 0x3F, 0x00, 0xFB, 0xFF, 0xFF, 0xFF, 0x04, 0x00, 0x41, 0x00, 0x04, 0x00,
        0x01, 0x00, 0xFE, 0xFF, 0x3F, 0x00, 0xFE, 0xFF, 0xFF, 0xFF, 0x02, 0x00, 0x41, 0x00, 0x02, 0x00,
        0x02, 0x00, 0x00, 0x00, 0x3E, 0x00, 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x42, 0x00, 0x00, 0x00,
        0xFF, 0xFF, 0xFE, 0xFF, 0x3F, 0x00, 0xFE, 0xFF, 0x01, 0x00, 0x02, 0x00, 0x3F, 0x00, 0x02, 0x00,
        0xFF, 0xFF, 0xFB, 0xFF, 0x3F, 0x00, 0xFB, 0xFF, 0x01, 0x00, 0x04, 0x00, 0x3F, 0x00, 0x04, 0x00,
        0x00, 0x00, 0xF9, 0xFF, 0x40, 0x00, 0xF9, 0xFF, 0x00, 0x00, 0x06, 0x00, 0x40, 0x00, 0x06, 0x00,
        0x00, 0x00, 0xF6, 0xFF, 0x40, 0x00, 0xF6, 0xFF, 0x00, 0x00, 0x08, 0x00, 0x40, 0x00, 0x08, 0x00,
    });

    // GHIDRA: PTR_DAT_80083edc @ 0x80083EDC (VS.EXE) — the pointer table into the rows above, one
    // entry per row, stride 4. The nine targets are exactly Dat80083e4cAddress + row * 0x10, and
    // the double indirection is kept — read through this table rather than computed from the
    // stride — so an out-of-range row index reads whatever byte actually follows here, matching the
    // original instead of a formula that quietly assumes the index never leaves 0..8.
    private const int Dat80083edcAddress = unchecked((int)0x80083EDC);

    internal static readonly byte[] PTR_DAT_80083edc = LibGpu.RamRegion(Dat80083edcAddress, new byte[]
    {
        0x4C, 0x3E, 0x08, 0x80, 0x5C, 0x3E, 0x08, 0x80, 0x6C, 0x3E, 0x08, 0x80,
        0x7C, 0x3E, 0x08, 0x80, 0x8C, 0x3E, 0x08, 0x80, 0x9C, 0x3E, 0x08, 0x80,
        0xAC, 0x3E, 0x08, 0x80, 0xBC, 0x3E, 0x08, 0x80, 0xCC, 0x3E, 0x08, 0x80,
    });

    // GHIDRA: DAT_80083f00 @ 0x80083F00 (VS.EXE) — the second of the two tables, same shape as
    // DAT_80083e4c above.
    private const int Dat80083f00Address = unchecked((int)0x80083F00);

    internal static readonly byte[] DAT_80083f00 = LibGpu.RamRegion(Dat80083f00Address, new byte[]
    {
        0x00, 0x00, 0xFE, 0xFF, 0x22, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x04, 0x00, 0x22, 0x00, 0x04, 0x00,
        0x01, 0x00, 0xFE, 0xFF, 0x21, 0x00, 0xFE, 0xFF, 0xFF, 0xFF, 0x03, 0x00, 0x23, 0x00, 0x03, 0x00,
        0x02, 0x00, 0xFF, 0xFF, 0x20, 0x00, 0xFF, 0xFF, 0xFE, 0xFF, 0x02, 0x00, 0x24, 0x00, 0x02, 0x00,
        0x03, 0x00, 0xFF, 0xFF, 0x1F, 0x00, 0xFF, 0xFF, 0xFD, 0xFF, 0x01, 0x00, 0x25, 0x00, 0x01, 0x00,
        0x04, 0x00, 0x00, 0x00, 0x1E, 0x00, 0x00, 0x00, 0xFC, 0xFF, 0x00, 0x00, 0x26, 0x00, 0x00, 0x00,
        0xFD, 0xFF, 0xFF, 0xFF, 0x25, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x01, 0x00, 0x1F, 0x00, 0x01, 0x00,
        0xFE, 0xFF, 0xFF, 0xFF, 0x24, 0x00, 0xFF, 0xFF, 0x02, 0x00, 0x02, 0x00, 0x20, 0x00, 0x02, 0x00,
        0xFF, 0xFF, 0xFE, 0xFF, 0x23, 0x00, 0xFE, 0xFF, 0x01, 0x00, 0x03, 0x00, 0x21, 0x00, 0x03, 0x00,
        0x00, 0x00, 0xFE, 0xFF, 0x22, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x04, 0x00, 0x22, 0x00, 0x04, 0x00,
    });

    // GHIDRA: PTR_DAT_80083f90 @ 0x80083F90 (VS.EXE) — same shape as PTR_DAT_80083edc, targets
    // Dat80083f00Address + row * 0x10. Its own table ends at 0x80083FB4, exactly where
    // FUN_80058338's own BLOCKED comment's PTR_DAT_80083fb4 begins — adjacent, unrelated tables,
    // and the boundary is itself the check that this table's own extent is exactly nine rows.
    private const int Dat80083f90Address = unchecked((int)0x80083F90);

    internal static readonly byte[] PTR_DAT_80083f90 = LibGpu.RamRegion(Dat80083f90Address, new byte[]
    {
        0x00, 0x3F, 0x08, 0x80, 0x10, 0x3F, 0x08, 0x80, 0x20, 0x3F, 0x08, 0x80,
        0x30, 0x3F, 0x08, 0x80, 0x40, 0x3F, 0x08, 0x80, 0x50, 0x3F, 0x08, 0x80,
        0x60, 0x3F, 0x08, 0x80, 0x70, 0x3F, 0x08, 0x80, 0x80, 0x3F, 0x08, 0x80,
    });

    // GHIDRA: DAT_80084184 @ 0x80084184 (VS.EXE) — VS_EXE/Roster.cs's own portrait coordinate
    // table. NOT declared a second time here: Roster.cs's own field is `private`, but the bytes it
    // registers through LibGpu.RamRegion are keyed on this address in a resolver every reader
    // shares, so the raw literal is the correct way to reach them from outside that file without a
    // second declaration on the same span. See Roster.cs's own comment on DAT_80084184 for what the
    // table holds and how its 12-byte, six-column stride was closed.
    private const int Dat80084184Address = unchecked((int)0x80084184);

    // GHIDRA: FUN_80057a7c @ 0x80057A7C (VS.EXE)
    // 1700 bytes, one caller — RunBattleManagerFrame (BLOCKED above, at 0x8005B9F8), which calls it once per
    // slot inside its own still-BLOCKED per-slot loop. param_1 is NOT the battle context: Ghidra
    // types the caller's own cursor `short *`, and the caller advances it by 0xE0 (in the caller's
    // own halfword-pointer units) once per slot, so param_1 here is a per-slot PORTRAIT-BOX
    // sub-record the caller reaches from ctx by an offset this slice cannot see — the caller itself
    // is still a stub. Every offset below is relative to THAT record, not to ctx, and none of it is
    // in BattleState for that reason: BattleState models the battle context, and this record is
    // reached from it through a base this slice does not have. Raw offsets throughout, exactly as
    // the mandate asks for a struct nobody has named yet.
    //
    // param_2 is the slot index 0..11 the caller's own loop counter supplies
    // (`FUN_80057a7c(puVar13,(int)(short)iVar5)` at the call site); the `param_2 < 6` test below is
    // the same team split BattleManager's other functions use throughout this file.
    //
    // THE TWO PER-CHARACTER TABLES. `*(short *)(param_1 + 0x28)` selects a row out of the two
    // parallel tables declared above. WHAT THE NINE ROWS SELECT is not closed — `param_1 + 0x28`
    // was not traced to its writer — so no reading of the index is asserted as fact. The three
    // scalars DAT_80083f00, DAT_80083f0c and DAT_80083f0e used below without going through the
    // pointer table are simply row 0 of the second table at fixed offsets 0x00, 0x0C and 0x0E;
    // DAT_80083e4c and DAT_80083e58 are row 0 of the first table at 0x00 and 0x0C, and DAT_80083e5a
    // is that same row 0 at 0x0E. Same reason in both cases: the original reaches row 0 directly
    // rather than through the pointer, and the port does too, by adding the fixed byte offset to
    // the row-zero address instead of declaring five more named constants for bytes already
    // embedded above.
    //
    // THE TAIL closes on ground this slice already owns: `DAT_8008d320 + param_2 * 0x14 + 0x15c0`
    // is BattleManager.DAT_8008d320 (the ctx address) plus BattleState.CtxSlotRecordStride times
    // the slot plus BattleState.CtxTargetIndex — this slot's own current TARGET — and the value it
    // reads there indexes straight into VS_EXE/Roster.cs's own portrait coordinate table. So the
    // tail is a portrait-icon lookup keyed on the CURRENT TARGET, plausibly the icon FUN_80058338
    // or a sibling primitive later draws over this slot's HUD box; not asserted as closed fact.
    //
    // EVERY SHIFT BELOW IS KEPT IN ITS ORIGINAL FORM. `(int)(((uint)a - (uint)b) * 0x10000) >> 0x10`
    // is this file's own established sign-extend-the-low-halfword idiom (see RunBattleRound's
    // header), and the multiply-by-N-then-`>> 2`-with-a-`+3`-fixup pairs are the compiler's
    // rounding fix-up for a negative dividend, identical in shape to RunBattleRound's own central-
    // gauge handicap scaling. Neither is simplified to an equivalent expression, for the same
    // reason UpdateCentralGaugeBar's own header gives elsewhere in this file: the two are only
    // arithmetically identical, and the original never computes it the simpler way.
    private static void FUN_80057a7c(int param_1, short param_2)
    {
        ushort uVar1;
        ushort uVar2;
        ushort uVar6;
        bool bVar3;
        sbyte cVar4;
        sbyte cVar5;
        sbyte cVar9;
        int iVar7;
        short sVar8;
        int iVar10;
        int iVar11;
        short sVar12;
        short sVar13;
        short sVar14;
        int psVar15Addr;
        int psVar16Addr;
        int rowIndex;

        rowIndex = (short)PsxRam.ReadU16(param_1 + 0x28);
        psVar16Addr = PsxRam.ReadI32(Dat80083edcAddress + rowIndex * 4);
        psVar15Addr = PsxRam.ReadI32(Dat80083f90Address + rowIndex * 4);

        iVar10 = (int)(((uint)PsxRam.ReadU16(param_1 + 0x20) - (uint)PsxRam.ReadU16(param_1 + 0x18)) * 0x10000) >> 0x10;
        iVar10 = iVar10 * (short)PsxRam.ReadU16(param_1 + 0x16);
        sVar14 = (short)PsxRam.ReadU16(param_1 + 0xe);
        if (iVar10 < 0)
        {
            iVar10 = iVar10 + 7;
        }

        iVar7 = (int)(((uint)PsxRam.ReadU16(param_1 + 0x22) - (uint)PsxRam.ReadU16(param_1 + 0x1a)) * 0x10000) >> 0x10;
        iVar7 = iVar7 * (short)PsxRam.ReadU16(param_1 + 0x16);
        sVar12 = (short)(PsxRam.ReadU16(param_1 + 0x18) + (short)(iVar10 >> 3));
        if (iVar7 < 0)
        {
            iVar7 = iVar7 + 7;
        }

        iVar10 = (int)((uint)PsxRam.ReadU16(param_1 + 0x1a) + (uint)(iVar7 >> 3));
        PsxRam.WriteU16(param_1 + 0x1c, (ushort)sVar12);
        sVar8 = (short)iVar10;
        PsxRam.WriteU16(param_1 + 0x1e, (ushort)sVar8);

        if (param_2 < 6)
        {
            iVar11 = (int)sVar14;
            iVar7 = (short)PsxRam.ReadU16(psVar15Addr) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            PsxRam.WriteU16(param_1 + 0x38,
                (ushort)(sVar12 + (short)PsxRam.ReadU16(psVar16Addr) + (short)(iVar7 >> 2)));
            iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 4) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            PsxRam.WriteU16(param_1 + 0x40,
                (ushort)(sVar12 + (short)PsxRam.ReadU16(psVar16Addr + 4) + (short)(iVar7 >> 2)));
            iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 8) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            PsxRam.WriteU16(param_1 + 0x48,
                (ushort)(sVar12 + (short)PsxRam.ReadU16(psVar16Addr + 8) + (short)(iVar7 >> 2)));
            iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 0xc) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            PsxRam.WriteU16(param_1 + 0x50,
                (ushort)(sVar12 + (short)PsxRam.ReadU16(psVar16Addr + 0xc) + (short)(iVar7 >> 2)));
            iVar7 = (short)PsxRam.ReadU16(Dat80083f00Address) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            iVar11 = (short)PsxRam.ReadU16(Dat80083f00Address + 0xc) * iVar11;
            sVar13 = (short)(sVar12 + (short)PsxRam.ReadU16(Dat80083e4cAddress) + (short)(iVar7 >> 2));
            if (iVar11 < 0)
            {
                iVar11 = iVar11 + 3;
            }

            sVar12 = (short)(sVar12 + (short)PsxRam.ReadU16(Dat80083e4cAddress + 0xc) + (short)(iVar11 >> 2));
        }
        else
        {
            iVar11 = (int)sVar14;
            iVar7 = (short)PsxRam.ReadU16(psVar15Addr) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            PsxRam.WriteU16(param_1 + 0x38,
                (ushort)((sVar12 - (short)PsxRam.ReadU16(psVar16Addr)) - (short)(iVar7 >> 2)));
            iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 4) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            PsxRam.WriteU16(param_1 + 0x40,
                (ushort)((sVar12 - (short)PsxRam.ReadU16(psVar16Addr + 4)) - (short)(iVar7 >> 2)));
            iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 8) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            PsxRam.WriteU16(param_1 + 0x48,
                (ushort)((sVar12 - (short)PsxRam.ReadU16(psVar16Addr + 8)) - (short)(iVar7 >> 2)));
            iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 0xc) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            PsxRam.WriteU16(param_1 + 0x50,
                (ushort)((sVar12 - (short)PsxRam.ReadU16(psVar16Addr + 0xc)) - (short)(iVar7 >> 2)));
            iVar7 = (short)PsxRam.ReadU16(Dat80083f00Address) * iVar11;
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 3;
            }

            iVar11 = (short)PsxRam.ReadU16(Dat80083f00Address + 0xc) * iVar11;
            sVar13 = (short)((sVar12 - (short)PsxRam.ReadU16(Dat80083e4cAddress)) - (short)(iVar7 >> 2));
            if (iVar11 < 0)
            {
                iVar11 = iVar11 + 3;
            }

            sVar12 = (short)((sVar12 - (short)PsxRam.ReadU16(Dat80083e4cAddress + 0xc)) - (short)(iVar11 >> 2));
        }

        iVar11 = (int)sVar14;
        iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 2) * iVar11;
        if (iVar7 < 0)
        {
            iVar7 = iVar7 + 3;
        }

        PsxRam.WriteU16(param_1 + 0x3a,
            (ushort)(sVar8 + (short)PsxRam.ReadU16(psVar16Addr + 2) + (short)(iVar7 >> 2)));
        iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 6) * iVar11;
        if (iVar7 < 0)
        {
            iVar7 = iVar7 + 3;
        }

        PsxRam.WriteU16(param_1 + 0x42,
            (ushort)(sVar8 + (short)PsxRam.ReadU16(psVar16Addr + 6) + (short)(iVar7 >> 2)));
        iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 0xa) * iVar11;
        if (iVar7 < 0)
        {
            iVar7 = iVar7 + 3;
        }

        PsxRam.WriteU16(param_1 + 0x4a,
            (ushort)(sVar8 + (short)PsxRam.ReadU16(psVar16Addr + 0xa) + (short)(iVar7 >> 2)));
        iVar7 = (short)PsxRam.ReadU16(psVar15Addr + 0xe) * iVar11;
        if (iVar7 < 0)
        {
            iVar7 = iVar7 + 3;
        }

        iVar10 = iVar10 + PsxRam.ReadU16(psVar16Addr + 0xe) + (iVar7 >> 2);
        PsxRam.WriteU16(param_1 + 0x52, (ushort)(short)iVar10);
        iVar11 = (short)PsxRam.ReadU16(Dat80083f00Address + 0xe) * iVar11;
        if (iVar11 < 0)
        {
            iVar11 = iVar11 + 3;
        }

        sVar14 = (short)(sVar8 + (short)PsxRam.ReadU16(Dat80083e4cAddress + 0xe) + (short)(iVar11 >> 2));

        iVar7 = (int)sVar13 - (int)sVar12;
        if (iVar7 < 0)
        {
            iVar7 = -iVar7;
        }

        sVar12 = (short)((((iVar7 << 0x10) >> 0x10) - ((iVar7 << 0x10) >> 0x1f) >> 1) + -1);
        iVar10 = (int)(short)PsxRam.ReadU16(param_1 + 0x3a) - (iVar10 * 0x10000 >> 0x10);
        if (iVar10 < 0)
        {
            iVar10 = -iVar10;
        }

        sVar8 = sVar12;
        if ((short)PsxRam.ReadU16(param_1 + 0x50) < (short)PsxRam.ReadU16(param_1 + 0x38))
        {
            sVar8 = (short)(-sVar12);
        }

        PsxRam.WriteU16(param_1 + 0x70, (ushort)sVar13);
        PsxRam.WriteU16(param_1 + 0x60, (ushort)sVar13);
        PsxRam.WriteU16(param_1 + 0x78, (ushort)(sVar13 + sVar8));
        PsxRam.WriteU16(param_1 + 0x68, (ushort)(sVar13 + sVar8));
        sVar8 = (short)iVar7;
        PsxRam.WriteU16(param_1 + 0x72, (ushort)sVar14);
        PsxRam.WriteU16(param_1 + 0x7a, (ushort)sVar14);
        PsxRam.WriteU16(param_1 + 0x62, (ushort)(sVar14 - sVar12));
        PsxRam.WriteU16(param_1 + 0x6a, (ushort)(sVar14 - sVar12));

        iVar10 = ((iVar10 << 0x10) >> 0xc) / 0x18;
        sVar14 = (short)PsxRam.ReadU16(param_1 + 0x50);
        sVar12 = (short)((sVar8 * 0x38) / 0x60);
        if (sVar14 < (short)PsxRam.ReadU16(param_1 + 0x38))
        {
            PsxRam.WriteU16(param_1 + 0xe8, (ushort)sVar14);
            PsxRam.WriteU16(param_1 + 0xd8, (ushort)sVar14);
            sVar12 = (short)((short)PsxRam.ReadU16(param_1 + 0x40) + sVar12);
        }
        else
        {
            sVar12 = (short)((short)PsxRam.ReadU16(param_1 + 0x40) - sVar12);
            PsxRam.WriteU16(param_1 + 0xe8, (ushort)sVar12);
            PsxRam.WriteU16(param_1 + 0xd8, (ushort)sVar12);
            sVar12 = (short)PsxRam.ReadU16(param_1 + 0x50);
        }

        PsxRam.WriteU16(param_1 + 0xf0, (ushort)sVar12);
        PsxRam.WriteU16(param_1 + 0xe0, (ushort)sVar12);

        iVar7 = iVar10 * 0x10000 >> 0x10;
        if (iVar7 < 0)
        {
            iVar7 = iVar7 + 3;
        }

        sVar12 = (short)(sVar8 / 3);
        int notLess = (sVar8 < 0x50) ? 0 : 1;
        sVar14 = (short)((short)PsxRam.ReadU16(param_1 + 0x3a) + (short)(iVar7 >> 2) + notLess * -4);
        PsxRam.WriteU16(param_1 + 0xda, (ushort)sVar14);
        PsxRam.WriteU16(param_1 + 0xe2, (ushort)sVar14);
        sVar14 = (short)((short)iVar10 + (short)PsxRam.ReadU16(param_1 + 0xda));
        PsxRam.WriteU16(param_1 + 0xea, (ushort)sVar14);
        PsxRam.WriteU16(param_1 + 0xf2, (ushort)sVar14);

        sVar14 = (short)PsxRam.ReadU16(param_1 + 0x50);
        bVar3 = (short)PsxRam.ReadU16(param_1 + 0x38) <= sVar14;
        if (bVar3)
        {
            PsxRam.WriteU16(param_1 + 0x1b0, (ushort)(sVar14 + sVar12));
            PsxRam.WriteU16(param_1 + 0x1a0, (ushort)(sVar14 + sVar12));
            uVar6 = PsxRam.ReadU16(param_1 + 0x50);
        }
        else
        {
            PsxRam.WriteU16(param_1 + 0x1b0, (ushort)(sVar14 - sVar12));
            PsxRam.WriteU16(param_1 + 0x1a0, (ushort)(sVar14 - sVar12));
            uVar6 = PsxRam.ReadU16(param_1 + 0x50);
        }

        PsxRam.WriteU16(param_1 + 0x1b8, uVar6);
        PsxRam.WriteU16(param_1 + 0x1a8, uVar6);
        sVar12 = (short)((short)PsxRam.ReadU16(param_1 + 0x52) - sVar12);
        PsxRam.WriteU16(param_1 + 0x1a2, (ushort)sVar12);
        PsxRam.WriteU16(param_1 + 0x1aa, (ushort)sVar12);
        PsxRam.WriteU16(param_1 + 0x1b2, PsxRam.ReadU16(param_1 + 0x52));
        PsxRam.WriteU16(param_1 + 0x1ba, PsxRam.ReadU16(param_1 + 0x52));

        // THE TAIL — see the header comment. sVar14 here is a THIRD, unrelated meaning: this slot's
        // current target, read through BattleManager.DAT_8008d320 and BattleState's own stride and
        // target-index constants, then looked up in VS_EXE/Roster.cs's portrait coordinate table.
        sVar14 = (short)PsxRam.ReadU16(
            DAT_8008d320 + param_2 * BattleState.CtxSlotRecordStride + BattleState.CtxTargetIndex);
        uVar1 = PsxRam.ReadU16(Dat80084184Address + sVar14 * 12);
        uVar2 = PsxRam.ReadU16(Dat80084184Address + 2 + sVar14 * 12);
        PsxRam.WriteU16(param_1 + 0x1a6, (ushort)(sVar14 + 0x798a));
        PsxRam.WriteU16(param_1 + 0x1ae, (ushort)((((short)uVar1 >> 6) + (uVar2 >> 4 & 0x10)) & 0x1f));
        cVar4 = (sbyte)((uVar1 & 0x3f) << 2);
        cVar5 = (sbyte)((cVar4 - ((bVar3 ? 1 : 0) + -0x30)) + -1);
        cVar9 = (sbyte)uVar2;
        PsxRam.WriteU8(param_1 + 0x1a5, (byte)cVar9);
        PsxRam.WriteU8(param_1 + 0x1ad, (byte)cVar9);
        PsxRam.WriteU8(param_1 + 0x1a4, (byte)cVar4);
        PsxRam.WriteU8(param_1 + 0x1ac, (byte)cVar5);
        PsxRam.WriteU8(param_1 + 0x1b4, (byte)cVar4);
        PsxRam.WriteU8(param_1 + 0x1b5, (byte)(cVar9 + 0x2f));
        PsxRam.WriteU8(param_1 + 0x1bc, (byte)cVar5);
        PsxRam.WriteU8(param_1 + 0x1bd, (byte)(cVar9 + 0x2f));
    }

    // GHIDRA: DAT_80084214 / DAT_80084216 @ 0x80084214 (VS.EXE) — FUN_80058120's own small table,
    // two rows, stride 4. Immediately past VS_EXE/Roster.cs's own DAT_80084184 table
    // (0x80084184 + 0x90 = 0x80084214, that table's own closed extent) and has exactly one
    // reference in the whole overlay — FUN_80058120, at 0x800581A0.
    private const int Dat80084214Address = unchecked((int)0x80084214);
    private const int Dat80084216Address = unchecked((int)0x80084216);

    internal static readonly byte[] DAT_80084214 = LibGpu.RamRegion(Dat80084214Address, new byte[]
    {
        0x40, 0x06, 0x03, 0x00, 0x80, 0x3E, 0x0D, 0x00,
    });

    // GHIDRA: FUN_80058120 @ 0x80058120 (VS.EXE)
    // 536 bytes, one caller — RunBattleManagerFrame (BLOCKED above, at 0x8005BA04 and 0x8005BA10) — called
    // TWICE per slot, back to back, with param_2 = 0 then 1. param_1 is the same per-slot
    // PORTRAIT-BOX record FUN_80057a7c above receives and partly fills in (+0x38, +0x3a, +0x40,
    // +0x48, +0x50, +0x52); see that function's header for what param_1 is and is not.
    //
    // THE DIVISION is the same safe-division trap pair VS_EXE/AnimVmInterpreter.cs and
    // VS_EXE/AnimCmdTransform.cs already port as a bare C# `/`: the original computes the quotient
    // and only THEN checks for a zero divisor (`break 0x1c00`) and the MIN_VALUE/-1 pair
    // (`break 0x1800`), both of which halt the console. C#'s own DivideByZeroException /
    // OverflowException reach the same halt one instruction earlier. Rule 12: the original's abort
    // is not softened into a guard, so no check is added here either.
    private static void FUN_80058120(int param_1, short param_2)
    {
        short sVar1;
        int iVar2;
        short sVar3;
        ushort uVar4;
        short sVar5;
        int iVar6;
        short sVar7;
        int iVar8;
        int iVar9;
        int iVar10;
        int iVar11;
        int iVar12;
        int iVar13;

        sVar5 = (short)PsxRam.ReadU16(param_1 + 0x38);
        iVar13 = (int)sVar5;
        iVar12 = (int)(short)PsxRam.ReadU16(param_1 + 0x50);
        iVar9 = iVar13 - iVar12;
        if (iVar9 < 0)
        {
            iVar9 = -iVar9;
        }

        iVar11 = (iVar9 << 0x10) >> 0x10;
        iVar8 = (int)param_2;
        iVar2 = (int)(short)PsxRam.ReadU16(param_1 + iVar8 * 2 + 2) * (((iVar11 * 0x28) / 0x60) * 0x10000 >> 0x10);
        iVar6 = (int)(short)PsxRam.ReadU16(Dat80084214Address + iVar8 * 4);

        // trap(0x1c00) / trap(0x1800) — the safe-division trap pair; see the comment above the
        // function. `sVar3 = iVar2 / iVar6` below reaches the same halt through C#'s own
        // DivideByZeroException instead of a guarded `break`.

        sVar1 = (short)PsxRam.ReadU16(Dat80084216Address + iVar8 * 4);
        iVar10 = (int)((uint)PsxRam.ReadU16(param_1 + 0x52) - (uint)PsxRam.ReadU16(param_1 + 0x3a));
        iVar8 = param_1 + iVar8 * 0x28 + 0x80;
        uVar4 = 0;
        if (iVar12 - iVar13 < 0)
        {
            uVar4 = (ushort)(iVar11 < 0x50 ? 1 : 0);
        }

        sVar7 = (short)((iVar11 - ((iVar9 << 0x10) >> 0x1f)) >> 1);
        sVar3 = (short)(iVar2 / iVar6);
        if (iVar12 < iVar13)
        {
            PsxRam.WriteU16(iVar8 + 8, (ushort)((sVar5 - sVar7) + (uVar4 - 1)));
            sVar5 = (short)(((short)PsxRam.ReadU16(param_1 + 0x48) - sVar7) + (uVar4 - 1));
            PsxRam.WriteU16(iVar8 + 0x18, (ushort)sVar5);
            PsxRam.WriteU16(iVar8 + 0x10, (ushort)((sVar5 - sVar3) + -1));
            sVar7 = (short)(((short)PsxRam.ReadU16(iVar8 + 8) - sVar3) + -1);
        }
        else
        {
            PsxRam.WriteU16(iVar8 + 8, (ushort)(sVar7 + sVar5 + 1));
            sVar5 = (short)(sVar7 + (short)PsxRam.ReadU16(param_1 + 0x48) + 1);
            PsxRam.WriteU16(iVar8 + 0x18, (ushort)sVar5);
            sVar7 = (short)(sVar3 + (short)PsxRam.ReadU16(iVar8 + 8) + 1);
            PsxRam.WriteU16(iVar8 + 0x10, (ushort)(sVar3 + sVar5 + 1));
        }

        PsxRam.WriteU16(iVar8 + 0x20, (ushort)sVar7);
        sVar5 = (short)((iVar11 < 0x50 ? 1 : 0)
            + (short)(((int)sVar1 * ((iVar10 * 0x10000) >> 0x10)) / 0x18)
            + (short)PsxRam.ReadU16(param_1 + 0x3a));
        PsxRam.WriteU16(iVar8 + 0x12, (ushort)sVar5);
        PsxRam.WriteU16(iVar8 + 10, (ushort)sVar5);

        // The compiler's own magic-multiply implementation of a signed divide-by-3, kept in its
        // literal shifted-64-bit form rather than rewritten to `/3` — same reason FUN_80057a7c's
        // own header gives for keeping its shift idioms literal.
        long magic = (long)((iVar10 * 0x10000) >> 0x10) * 0x55555556L;
        short div3 = (short)((ulong)magic >> 0x20);
        int signCorrection = (short)iVar10 >> 0xf;
        sVar5 = (short)((int)sVar5 + (div3 - signCorrection) + -1);
        PsxRam.WriteU16(iVar8 + 0x22, (ushort)sVar5);
        PsxRam.WriteU16(iVar8 + 0x1a, (ushort)sVar5);
    }

    // GHIDRA: FUN_80058338 @ 0x80058338 (VS.EXE)
    // BLOCKED: 2440 bytes, one caller — RunBattleManagerFrame (BLOCKED above, at 0x8005BA1C) — called once
    // per slot with the RAW battle context (not the per-slot record FUN_80057a7c/FUN_80058120
    // share): `param_1 = param_1 + param_2 * 0x1c0 + 0x20;` is the function's own first statement,
    // indexing straight off ctx at a 0x1C0-byte stride this slice has not seen named anywhere else.
    //
    // WHAT IT DOES, on the evidence of the shape alone: it splits TWO fields of that per-slot
    // sub-record (at +0x0A and +0x26) into tens and ones by dividing by 10, then for each digit
    // looks up a glyph-position row through one of two pointer tables — PTR_DAT_80083fb4 when the
    // tens digit is zero, PTR_DAT_80084124 otherwise — and lays out four short values from that row
    // into the digit's own on-screen box (a leading-zero-suppressed two-digit numeric readout,
    // plausibly a per-slot HP or KI number given the /10 split and the repeated `* '\x18'`, a 24-
    // pixel glyph-cell width). That reading is not closed to fact; it is stated only to spare a
    // later slice re-deriving the shape.
    //
    // WHY IT IS BLOCKED, unlike its two siblings above. FUN_80057a7c's two per-character tables sit
    // in one contiguous, self-referential 824-byte span (0x80083E4C..0x80083FB4) with exactly one
    // reference each — closed and embedded above. These two pointer tables do NOT: checked with
    // find-cross-references, PTR_DAT_80083fb4's own first entry is DAT_8008d188 and
    // PTR_DAT_80084124's is DAT_80084014 — neither sits inside the other table's own span, and
    // 0x8008D188 in particular falls in the same gp-relative small-data region this very file's own
    // scalar globals occupy (DAT_8008d15c through DAT_8008d494 all sit within a few hundred bytes
    // of it), which on every other piece of evidence in this port is MUTABLE RUNTIME STATE, not a
    // baked-in glyph table. Ghidra gives no other reference to either pointer table's own contents
    // anywhere in the overlay, so there is no cross-check available the way Roster.cs's own table
    // gave FUN_80057a7c's tail one. Embedding raw bytes here on the strength of two addresses
    // alone, without knowing whether the target is static or a runtime record some other
    // not-yet-ported function populates, is precisely the invented semantics rule 10 forbids. Left
    // unperformed; param_1/param_2 are kept so the (still BLOCKED) caller's call site needs no
    // change when this closes.
    private static void FUN_80058338(int param_1, short param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: DAT_800842b8 @ 0x800842B8 (VS.EXE) / DAT_800843b8 @ 0x800843B8 (VS.EXE)
    // The two source tables PART TWO of both InitCentralGaugeBar and UpdateCentralGaugeBar below
    // reads from. Checked with find-cross-references before embedding, the same discipline
    // Dat80083e4cAddress above already follows: each symbol has exactly TWO readers in the whole
    // overlay — InitCentralGaugeBar (once, at arming) and UpdateCentralGaugeBar (once, every
    // frame) — and neither function, nor anything else in the overlay, ever WRITES either span. So
    // this is baked-in `.data`, not runtime state some other not-yet-ported function populates —
    // the objection the two functions' own earlier BLOCKED comments raised (and which is now
    // withdrawn, not repeated: PsxRam.RamRegion is exactly the operation those comments said did
    // not exist, and this port already uses it for a stack scratch region in four other places —
    // see the DEVIATION below).
    //
    // SIZE, SETTLED: each table is 0x100 bytes (128 halfwords), not the 64-halfword pair
    // InitCentralGaugeBar's own PART TWO used to describe. Three independent pieces of evidence
    // agree:
    //   1. The two symbols sit exactly 0x100 bytes apart (0x800843B8 - 0x800842B8), consistent with
    //      DAT_800842b8 itself being one full 0x100-byte table rather than a 0x80-byte table
    //      followed by 0x80 bytes of something else.
    //   2. DAT_800843b8's own span ends exactly at 0x800844B8 — VS_EXE/Roster.cs's own
    //      PTR_DAT_800844b8, a pointer variable that file already owns and declares. An
    //      independently-owned symbol picking up immediately where this table's 0x100th byte would
    //      fall is the same kind of boundary check PTR_DAT_80083f90's own comment above already
    //      uses to confirm a table's true extent.
    //   3. UpdateCentralGaugeBar's own PART TWO masks its sampling cursor into each table with
    //      `& 0x7f` before every read (`uVar11 = uVar11 + 1 & 0x7f`, `uVar12 = uVar11 & 0x7f`) — a
    //      cursor that ranges 0..127. That only stays inside each table's own bounds every frame if
    //      the table actually holds 128 halfwords; a 64-halfword table would read past its own end
    //      on more than half that range.
    // UpdateCentralGaugeBar's own PART TWO comment ("indexed mod 0x80, i.e. two 128-entry tables")
    // is therefore the correct one. InitCentralGaugeBar's "two 64-halfword tables" was describing
    // something real but different: how much of each table ITS OWN copy loop reads (the first 64
    // halfwords only, a fixed sub-range — see its own PART TWO below), not the tables' actual
    // declared size. Both tables are declared here at their full 0x100-byte extent so both readers
    // can index into the same bytes.
    private const int Dat800842b8Address = unchecked((int)0x800842B8);
    private const int Dat800843b8Address = unchecked((int)0x800843B8);

    internal static readonly byte[] DAT_800842b8 = LibGpu.RamRegion(Dat800842b8Address, new byte[]
    {
        0xFE, 0x7F, 0xDD, 0x7F, 0xDD, 0x7F, 0xBC, 0x7F, 0xBC, 0x7F, 0x9B, 0x7F, 0x9B, 0x7F, 0x7A, 0x7F,
        0x7A, 0x7F, 0x59, 0x7F, 0x59, 0x7F, 0x38, 0x7F, 0x38, 0x7F, 0x17, 0x7F, 0x17, 0x7F, 0xF7, 0x7E,
        0xF6, 0x7E, 0xD6, 0x7E, 0xD5, 0x7E, 0xB5, 0x7E, 0xB4, 0x7E, 0x94, 0x7E, 0x93, 0x7E, 0x73, 0x7E,
        0x72, 0x7E, 0x52, 0x7E, 0x51, 0x7E, 0x31, 0x7E, 0x10, 0x7E, 0x0F, 0x7E, 0xEF, 0x7D, 0xEE, 0x7D,
        0xCE, 0x7D, 0xCD, 0x7D, 0xAD, 0x7D, 0x8C, 0x7D, 0x8B, 0x7D, 0x6B, 0x7D, 0x6A, 0x7D, 0x4A, 0x7D,
        0x29, 0x7D, 0x08, 0x7D, 0x07, 0x7D, 0xE6, 0x7C, 0xE7, 0x7C, 0xC6, 0x7C, 0xA5, 0x7C, 0xA5, 0x7C,
        0x84, 0x78, 0x84, 0x78, 0x84, 0x74, 0x63, 0x70, 0x63, 0x70, 0x63, 0x6C, 0x42, 0x6C, 0x42, 0x68,
        0x42, 0x68, 0x21, 0x64, 0x21, 0x64, 0x21, 0x60, 0x00, 0x60, 0x00, 0x5C, 0x00, 0x5C, 0x00, 0x5C,
        0x00, 0x5C, 0x00, 0x60, 0x21, 0x60, 0x21, 0x64, 0x21, 0x64, 0x42, 0x68, 0x42, 0x68, 0x42, 0x6C,
        0x63, 0x6C, 0x63, 0x70, 0x63, 0x70, 0x84, 0x74, 0x84, 0x74, 0x84, 0x78, 0xA5, 0x7C, 0xA5, 0x7C,
        0xA5, 0x7C, 0xC6, 0x7C, 0xE6, 0x7C, 0xE7, 0x7C, 0x08, 0x7D, 0x28, 0x7D, 0x29, 0x7D, 0x49, 0x7D,
        0x6A, 0x7D, 0x6B, 0x7D, 0x8B, 0x7D, 0xAC, 0x7D, 0xAC, 0x7D, 0xCD, 0x7D, 0xEE, 0x7D, 0xEF, 0x7D,
        0xEF, 0x7D, 0x10, 0x7E, 0x10, 0x7E, 0x31, 0x7E, 0x31, 0x7E, 0x52, 0x7E, 0x52, 0x7E, 0x73, 0x7E,
        0x73, 0x7E, 0x94, 0x7E, 0x94, 0x7E, 0xB5, 0x7E, 0xB5, 0x7E, 0xD6, 0x7E, 0xF7, 0x7E, 0x17, 0x7F,
        0x17, 0x7F, 0x38, 0x7F, 0x38, 0x7F, 0x59, 0x7F, 0x59, 0x7F, 0x7A, 0x7F, 0x7A, 0x7F, 0x9B, 0x7F,
        0x9B, 0x7F, 0xBC, 0x7F, 0xBC, 0x7F, 0xDD, 0x7F, 0xDD, 0x7F, 0xFE, 0x7F, 0xFF, 0x7F, 0xFF, 0x7F,
    });

    internal static readonly byte[] DAT_800843b8 = LibGpu.RamRegion(Dat800843b8Address, new byte[]
    {
        0xFF, 0x7B, 0xDF, 0x77, 0xDF, 0x77, 0xBF, 0x73, 0xBF, 0x73, 0x9F, 0x6F, 0x9F, 0x6F, 0x7F, 0x6B,
        0x7F, 0x6B, 0x5F, 0x67, 0x5F, 0x67, 0x3F, 0x63, 0x3F, 0x63, 0x1F, 0x5F, 0x1F, 0x5F, 0xFF, 0x5E,
        0xDF, 0x5A, 0xDF, 0x5A, 0xBF, 0x56, 0x9F, 0x52, 0x9F, 0x52, 0x7F, 0x4E, 0x7F, 0x4E, 0x5F, 0x4A,
        0x5F, 0x4A, 0x3F, 0x46, 0x3F, 0x46, 0x1F, 0x42, 0x1F, 0x42, 0xFF, 0x3D, 0xFF, 0x3D, 0xFF, 0x39,
        0xDF, 0x35, 0xBF, 0x31, 0xBF, 0x31, 0x9F, 0x2D, 0x7F, 0x2D, 0x7F, 0x29, 0x5F, 0x25, 0x3F, 0x25,
        0x3F, 0x21, 0x1F, 0x21, 0xFF, 0x1C, 0xFF, 0x18, 0xDF, 0x18, 0xBF, 0x14, 0xBF, 0x14, 0xBF, 0x14,
        0x9E, 0x10, 0x9E, 0x10, 0x9D, 0x10, 0x7C, 0x0C, 0x7C, 0x0C, 0x7B, 0x0C, 0x5B, 0x08, 0x5A, 0x08,
        0x5A, 0x08, 0x39, 0x04, 0x39, 0x04, 0x38, 0x04, 0x18, 0x00, 0x17, 0x00, 0x17, 0x00, 0x17, 0x00,
        0x17, 0x00, 0x18, 0x00, 0x38, 0x04, 0x39, 0x04, 0x39, 0x04, 0x5A, 0x08, 0x5A, 0x08, 0x5B, 0x08,
        0x7B, 0x0C, 0x7C, 0x0C, 0x7C, 0x0C, 0x9D, 0x10, 0x9D, 0x10, 0x9E, 0x10, 0xBF, 0x14, 0xBF, 0x14,
        0xBF, 0x14, 0xDF, 0x18, 0xFF, 0x18, 0xFF, 0x1C, 0x1F, 0x21, 0x3F, 0x21, 0x3F, 0x25, 0x5F, 0x25,
        0x7F, 0x29, 0x7F, 0x2D, 0x9F, 0x2D, 0xBF, 0x31, 0xBF, 0x31, 0xDF, 0x35, 0xFF, 0x39, 0xFF, 0x3D,
        0xFF, 0x3D, 0x1F, 0x42, 0x1F, 0x42, 0x3F, 0x46, 0x3F, 0x46, 0x5F, 0x4A, 0x5F, 0x4A, 0x7F, 0x4E,
        0x7F, 0x4E, 0x9F, 0x52, 0x9F, 0x52, 0xBF, 0x56, 0xBF, 0x56, 0xDF, 0x5A, 0xFF, 0x5E, 0x1F, 0x5F,
        0x1F, 0x5F, 0x1F, 0x63, 0x3F, 0x63, 0x3F, 0x67, 0x5F, 0x67, 0x5F, 0x6B, 0x7F, 0x6B, 0x7F, 0x6F,
        0x9F, 0x6F, 0x9F, 0x73, 0xBF, 0x73, 0xBF, 0x77, 0xDF, 0x77, 0xDF, 0x7B, 0xFF, 0x7F, 0xFF, 0x7F,
    });

    // DEVIATION: the 256-byte gauge-strip scratch buffer both functions' own PART TWO builds and
    // hands to LoadImage_ReturnTPageOrClutId. In the original this is a STACK local — `local_128`
    // in InitCentralGaugeBar, `local_120` in UpdateCentralGaugeBar — freshly allocated on each
    // function's own frame. THE CLAIM THIS FILE USED TO MAKE — that PsxRam has no operation that
    // hands out a fresh, PSX-addressable scratch region for a C# local, and that adding one would
    // be new architecture — was false and is withdrawn, not repeated: the mechanism already exists
    // and this port already uses it in four other places for exactly this problem (giving a stack
    // local a PSX address so it can be handed to something that takes one): BattleScene.cs's
    // Local18Address (0x807FFFD0), AnimCmdEffects.cs's auStack_10Address (0x807FFFE0),
    // AnimVmInterpreter.cs's Local30Address (0x807FFFF0), and AnimCmdControl.cs's VStack80Address
    // (0x807FFFC0). crt0 starts SP at 0x807FFFF8, so all four are genuinely stack memory on the
    // console, and each sits at a distinct address so none can ever alias another.
    //
    // ONE address is declared here, at 0x807FFEC0 — ending exactly where VStack80Address begins
    // (0x807FFEC0 + 0x100 == 0x807FFFC0), so it overlaps none of the four above — and BOTH
    // InitCentralGaugeBar and UpdateCentralGaugeBar use it, even though on the console they are two
    // separate functions' separate stack frames. Sharing one address between two ORIGINALLY
    // DISTINCT stack locals is itself a deviation from the original's storage, called out here
    // rather than left implicit, and it is safe only because: the two functions never run inside
    // the same frame (InitCentralGaugeBar runs once, at arming, before the round starts;
    // UpdateCentralGaugeBar runs every frame of the round that follows, and never again before the
    // next arming), and nothing ever reads the buffer back — both functions only WRITE it, once,
    // immediately before handing its address to LoadImage_ReturnTPageOrClutId in that same call,
    // and that call's own return value is unused at every call site here. A second, distinct
    // address per function was considered and rejected as an unforced two-spellings-of-one-buffer
    // risk, given the two never interleave and nothing outlives either call.
    private const int GaugeStripBufferAddress = unchecked((int)0x807FFEC0);

    private static readonly byte[] RAM_gaugeStrip = LibGpu.RamRegion(GaugeStripBufferAddress, 0x100);

    // GHIDRA: UpdateCentralGaugeBar @ 0x8005C6E4 (VS.EXE)
    // 1276 bytes. Always called immediately after RunBattleManagerFrame, on all four states, and it ends at
    // 0x8005CBDF — one byte below FUN_8005cbe0, the roster consumer main calls just after creating
    // this task. The three are one compilation unit. Ghidra already names the parameter `ctx`; kept
    // rather than reverted to `param_1`, since that rename is the database's own, not this port's.
    //
    // Four parts, all closed.
    //
    // PART ONE, closed: the scroll-speed follower. ctx+0x3028 and ctx+0x302a are a pair of 1..8
    // counters, one of which decays toward 1 while the other climbs toward 8, and which of the two
    // climbs is decided by comparing DAT_8008d3a8 against the live gauge at ctx+0x302C
    // (BattleState.CtxCentralGauge) — equal holds both where they are, DAT_8008d3a8 lagging behind
    // means the counters swap roles from the previous frame's. DAT_8008d3a8 itself is a smoothed
    // copy of the gauge: every frame it steps 0xEB (235) towards ctx+0x302C and is clamped to the
    // target the instant a step would pass it, so it is a capped-rate follower, not the gauge
    // itself — the two-register dance the original compiles this into (`iVar8`/`bVar1`) is kept
    // rather than simplified, matching how this file already keeps the equivalent shared-assignment
    // duplicated across sibling arms of an if/else for LAB_80056c64 above; C# forbids the original's
    // `goto` into that shared statement the same way here. ctx+0x3024 and ctx+0x3026 are then
    // advanced by the two counters, each wrapped mod 0x80 — the "0..128 scroll index" the two-tone
    // strip PART TWO would sample from.
    //
    // PART TWO, closed. The original scales ctx+0x302C into a 0..128 split point
    // (`((ctx+0x302C + 30000) * 0x80) / 60000`, kept as `iVar8`), samples that many halfwords from
    // `DAT_800842b8` starting at the ctx+0x3024 cursor — wrapping mod 0x80 as that cursor's own
    // producer (PART ONE, above) already keeps it — and fills the REST of a 128-halfword (256-byte)
    // strip from `DAT_800843b8`, starting at a second cursor derived from the same scale value and
    // the ctx+0x3026 cursor, also wrapped mod 0x80. See the GHIDRA comment on DAT_800842b8 /
    // DAT_800843b8 above (declared just before this function) for why each table is a 128-entry
    // (0x100-byte) span, not the 64-halfword pair InitCentralGaugeBar's own PART TWO copies a fixed
    // prefix of — these two functions read the same bytes two different ways, and both are
    // reproduced as the original shapes them, not reconciled into one. The 256-byte strip itself is
    // GaugeStripBufferAddress (0x807FFEC0) rather than a stack local — see the DEVIATION on that
    // declaration for why one address serves both this function and InitCentralGaugeBar. The
    // completed strip is then handed to `LoadImage_ReturnTPageOrClutId(local_120, 0, 0x1ed, 0x80, 1,
    // '\0')`, a CLUT upload whose return value is unused at this call site, matching
    // InitCentralGaugeBar's own upload.
    //
    // PART THREE, closed: the growth/shrink of the bar's own geometry, and the colour pulse. Both
    // are gated behind the animation VM's suspend flag, the same `(AnimVm.DAT_800b305a & 1) != 0`
    // gate RunBattleRound opens with — when it is up, this function's only remaining act is PART FOUR.
    //
    // The four POLY_FT4 quads InitCentralGaugeBar built at ctx+0x2f84 (stride 0x28) are quads 0..3 in
    // address order; every offset below is named against that layout (get-structure-info: y0 @ +0xA,
    // y1 @ +0x12, y2 @ +0x1A, y3 @ +0x22, r0/g0/b0 @ +4/+5/+6) rather than given a private field name,
    // for the same reason the file header gives for every other un-named ctx offset. Quads 0 and 1
    // are the clut-0x7B00 pair InitCentralGaugeBar built first, quads 2 and 3 the clut-0x7B40 pair —
    // the two teams' halves of the bar.
    //
    // ctx+0x10 bits 0x10000/0x20000/0x40000 pick one of three outcomes: bit 0x40000 clear selects a
    // SHRINK of quads 0/1 (both y0/y1 and both y2/y3 move together by -8, read from quad 1 before the
    // write and mirrored onto quad 0) once bit 0x20000 confirms it has not already finished (quad
    // 0's own y0 below 200 means it has, and only the flag bit is then written back); bit 0x40000 set
    // selects the matching GROW of quads 2/3's opposite pair, gated the same way through bit 0x30000
    // and quad 0's y0 against 0x107. The original writes the updated flag word back to ctx+0x10 ONLY
    // on the two "already finished" exits (`LAB_8005ca90`); the branch that actually performs a
    // shrink or a grow falls straight through to PART THREE's second half WITHOUT storing the flag
    // update its own local copy computed — the next read of ctx+0x10 a few lines below re-reads the
    // stale value from memory. That looks like a mistake, and rule 12 keeps it: nothing here corrects
    // it, the asymmetry is reproduced exactly as `beq`/`bne` place it.
    //
    // The colour pulse follows, on ctx+0x10 bit 0x80000 (reloaded fresh, not the local copy from the
    // geometry step above): when clear, a one-shot reset — quads 0 and 1's own g0 bit 0 is the pulse
    // latch, and finding it set snaps both quads' r0/g0/b0 back to neutral 0x80. When set, the pulse
    // itself: quad 0/1's r0 is XORed with 0x3f while the gauge sits at or past its own team's zero
    // (ctx+0x302C <= 0), their b0 XORed with 0x7f otherwise, and their g0 OR'd with 1 either way —
    // the latch PART THREE's other half reads.
    //
    // PART FOUR, closed modulo the same PARTIAL VS_EXE/AnimCmdAppearance.cs already records for this
    // exact global: DAT_8008d420 (the active DRAWENV's address) is a PRIVATE C# field in
    // VS_EXE/VS_EXE_exe.cs, not PsxRam-backed, so `PsxRam.ReadI32(Dat8008d420Address)` currently
    // yields 0 here for the same reason it does there — the fix is one line in VS_EXE_exe.cs and
    // belongs to that file's owner. The four AddPrim calls submit the four quads into the same
    // bucket, `DAT_8008d420 - (ctx+0x3030 * 4 + -0x206c)`, which is the environment's own
    // `(0x7ff - otz) * 4 + 0x70` ordering-table formula AnimCmdAppearance.cs's pri_set already uses,
    // with ctx+0x3030 as the OTZ; kept in the original's own subtraction form rather than rewritten
    // to that equivalent, since the two are only arithmetically identical and the original never
    // computes it that way.
    private static void UpdateCentralGaugeBar(int ctx)
    {
        const int Dat8008d420Address = unchecked((int)0x8008D420);

        // PART ONE — the scroll-speed follower and the two scroll cursors.
        if (DAT_8008d3a8 == PsxRam.ReadI32(ctx + 0x302c))
        {
            if ((short)PsxRam.ReadU16(ctx + 0x3028) != 1)
            {
                short iVar8 = (short)((short)PsxRam.ReadU16(ctx + 0x3028) - 1);
                PsxRam.WriteU16(ctx + 0x3028, (ushort)iVar8);
                if (iVar8 < 1)
                {
                    PsxRam.WriteU16(ctx + 0x3028, 1);
                }
            }

            if ((short)PsxRam.ReadU16(ctx + 0x302a) != 1)
            {
                short iVar8 = (short)((short)PsxRam.ReadU16(ctx + 0x302a) - 1);
                PsxRam.WriteU16(ctx + 0x302a, (ushort)iVar8);
                if (iVar8 < 1)
                {
                    PsxRam.WriteU16(ctx + 0x302a, 1);
                }
            }
        }
        else if (PsxRam.ReadI32(ctx + 0x302c) < DAT_8008d3a8)
        {
            short uVar2 = (short)((short)PsxRam.ReadU16(ctx + 0x3028) - 1);
            PsxRam.WriteU16(ctx + 0x3028, (ushort)uVar2);
            if (uVar2 < 1)
            {
                PsxRam.WriteU16(ctx + 0x3028, 1);
            }

            short sVar3 = (short)((short)PsxRam.ReadU16(ctx + 0x302a) + 1);
            PsxRam.WriteU16(ctx + 0x302a, (ushort)sVar3);
            if (8 < sVar3)
            {
                PsxRam.WriteU16(ctx + 0x302a, 8);
            }
        }
        else
        {
            short uVar2 = (short)((short)PsxRam.ReadU16(ctx + 0x302a) - 1);
            PsxRam.WriteU16(ctx + 0x302a, (ushort)uVar2);
            if (uVar2 < 1)
            {
                PsxRam.WriteU16(ctx + 0x302a, 1);
            }

            short sVar3 = (short)((short)PsxRam.ReadU16(ctx + 0x3028) + 1);
            PsxRam.WriteU16(ctx + 0x3028, (ushort)sVar3);
            if (8 < sVar3)
            {
                PsxRam.WriteU16(ctx + 0x3028, 8);
            }
        }

        // C# scopes a block's local names across every nested block inside it, so `iVar8` above
        // (declared twice, once per sibling `if`) cannot be reused here even though this statement
        // is textually later and neither sibling block is still open -- the same forced deviation
        // this file's header already names for LAB_800561d4's goto. Ghidra's own decompiler reuses
        // `iVar8` for this register too; `iVar8_2` is the smallest departure that still compiles.
        int iVar8_2 = DAT_8008d3a8;
        if (DAT_8008d3a8 == PsxRam.ReadI32(ctx + 0x302c))
        {
            DAT_8008d3a8 = iVar8_2;
        }
        else
        {
            bool bVar1;
            if (DAT_8008d3a8 < PsxRam.ReadI32(ctx + 0x302c))
            {
                iVar8_2 = PsxRam.ReadI32(ctx + 0x302c);
                DAT_8008d3a8 = DAT_8008d3a8 + 0xeb;
                bVar1 = iVar8_2 < DAT_8008d3a8;
            }
            else
            {
                iVar8_2 = PsxRam.ReadI32(ctx + 0x302c);
                bVar1 = DAT_8008d3a8 + -0xeb < iVar8_2;
                DAT_8008d3a8 = DAT_8008d3a8 + -0xeb;
            }

            if (bVar1)
            {
                DAT_8008d3a8 = iVar8_2;
            }
        }

        PsxRam.WriteU16(ctx + 0x3024,
            (ushort)(((short)PsxRam.ReadU16(ctx + 0x3024) - (short)PsxRam.ReadU16(ctx + 0x3028)) & 0x7f));
        PsxRam.WriteU16(ctx + 0x3026,
            (ushort)(((short)PsxRam.ReadU16(ctx + 0x3026) + (short)PsxRam.ReadU16(ctx + 0x302a)) & 0x7f));

        // PART TWO — the 128-halfword gauge strip. See the comment above the function.
        {
            uint uVar11 = PsxRam.ReadU16(ctx + 0x3024);
            int iVar8 = ((PsxRam.ReadI32(ctx + 0x302c) + 30000) * 0x80) / 60000;
            int iVar4 = (short)iVar8;
            int iVar13 = 0;

            if (0 < iVar4)
            {
                do
                {
                    PsxRam.WriteU16(GaugeStripBufferAddress + iVar13 * 2,
                        PsxRam.ReadU16(Dat800842b8Address + (int)uVar11 * 2));
                    uVar11 = (uVar11 + 1) & 0x7f;
                    iVar13 = iVar13 + 1;
                } while ((short)iVar13 < iVar4);
            }

            uVar11 = (uint)(iVar8 + (PsxRam.ReadU16(ctx + 0x3026) - 0x80));
            if ((short)iVar13 < 0x80)
            {
                do
                {
                    uint uVar12 = uVar11 & 0x7f;
                    PsxRam.WriteU16(GaugeStripBufferAddress + iVar13 * 2,
                        PsxRam.ReadU16(Dat800843b8Address + (int)uVar12 * 2));
                    uVar11 = uVar12 + 1;
                    iVar13 = iVar13 + 1;
                } while ((short)iVar13 < 0x80);
            }

            FileIo.LoadImage_ReturnTPageOrClutId(GaugeStripBufferAddress, 0, 0x1ed, 0x80, 1, 0);
        }

        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            goto LAB_8005cb40;
        }

        // PART THREE — geometry (shrink/grow) then the colour pulse.
        {
            uint uVar11 = (uint)PsxRam.ReadI32(ctx + 0x10);
            if ((uVar11 & 0x40000) == 0)
            {
                if ((uVar11 & 0x20000) == 0)
                {
                    uVar11 = uVar11 | 0x20000;
                    if ((short)PsxRam.ReadU16(ctx + 0x2f8e) < 200)
                    {
                        PsxRam.WriteI32(ctx + 0x10, (int)uVar11);
                    }
                    else
                    {
                        short sVar3 = (short)((short)PsxRam.ReadU16(ctx + 0x2fbe) - 8); // quad1.y1
                        short sVar5 = (short)((short)PsxRam.ReadU16(ctx + 0x2fce) - 8); // quad1.y3
                        short sVar7 = (short)((short)PsxRam.ReadU16(ctx + 0x300e) - 8); // quad3.y1
                        short sVar10 = (short)((short)PsxRam.ReadU16(ctx + 0x301e) - 8); // quad3.y3

                        PsxRam.WriteU16(ctx + 0x2fbe, (ushort)sVar3);   // quad1.y1
                        PsxRam.WriteU16(ctx + 0x2fb6, (ushort)sVar3);   // quad1.y0
                        PsxRam.WriteU16(ctx + 0x2f96, (ushort)sVar3);   // quad0.y1
                        PsxRam.WriteU16(ctx + 0x2f8e, (ushort)sVar3);   // quad0.y0
                        PsxRam.WriteU16(ctx + 0x2fce, (ushort)sVar5);   // quad1.y3
                        PsxRam.WriteU16(ctx + 0x2fc6, (ushort)sVar5);   // quad1.y2
                        PsxRam.WriteU16(ctx + 0x2fa6, (ushort)sVar5);   // quad0.y3
                        PsxRam.WriteU16(ctx + 0x2f9e, (ushort)sVar5);   // quad0.y2
                        PsxRam.WriteU16(ctx + 0x300e, (ushort)sVar7);   // quad3.y1
                        PsxRam.WriteU16(ctx + 0x3006, (ushort)sVar7);   // quad3.y0
                        PsxRam.WriteU16(ctx + 0x2fe6, (ushort)sVar7);   // quad2.y1
                        PsxRam.WriteU16(ctx + 0x2fde, (ushort)sVar7);   // quad2.y0
                        PsxRam.WriteU16(ctx + 0x301e, (ushort)sVar10);  // quad3.y3
                        PsxRam.WriteU16(ctx + 0x3016, (ushort)sVar10);  // quad3.y2
                        PsxRam.WriteU16(ctx + 0x2ff6, (ushort)sVar10);  // quad2.y3
                        PsxRam.WriteU16(ctx + 0x2fee, (ushort)sVar10);  // quad2.y2
                    }
                }
            }
            else if ((uVar11 & 0x30000) == 0)
            {
                if ((short)PsxRam.ReadU16(ctx + 0x2f8e) < 0x107)
                {
                    short sVar3 = (short)((short)PsxRam.ReadU16(ctx + 0x2fbe) + 8); // quad1.y1
                    short sVar5 = (short)((short)PsxRam.ReadU16(ctx + 0x2fce) + 8); // quad1.y3
                    short sVar7 = (short)((short)PsxRam.ReadU16(ctx + 0x300e) + 8); // quad3.y1
                    short sVar10 = (short)((short)PsxRam.ReadU16(ctx + 0x301e) + 8); // quad3.y3

                    PsxRam.WriteU16(ctx + 0x2fbe, (ushort)sVar3);   // quad1.y1
                    PsxRam.WriteU16(ctx + 0x2fb6, (ushort)sVar3);   // quad1.y0
                    PsxRam.WriteU16(ctx + 0x2f96, (ushort)sVar3);   // quad0.y1
                    PsxRam.WriteU16(ctx + 0x2f8e, (ushort)sVar3);   // quad0.y0
                    PsxRam.WriteU16(ctx + 0x2fce, (ushort)sVar5);   // quad1.y3
                    PsxRam.WriteU16(ctx + 0x2fc6, (ushort)sVar5);   // quad1.y2
                    PsxRam.WriteU16(ctx + 0x2fa6, (ushort)sVar5);   // quad0.y3
                    PsxRam.WriteU16(ctx + 0x2f9e, (ushort)sVar5);   // quad0.y2
                    PsxRam.WriteU16(ctx + 0x300e, (ushort)sVar7);   // quad3.y1
                    PsxRam.WriteU16(ctx + 0x3006, (ushort)sVar7);   // quad3.y0
                    PsxRam.WriteU16(ctx + 0x2fe6, (ushort)sVar7);   // quad2.y1
                    PsxRam.WriteU16(ctx + 0x2fde, (ushort)sVar7);   // quad2.y0
                    PsxRam.WriteU16(ctx + 0x301e, (ushort)sVar10);  // quad3.y3
                    PsxRam.WriteU16(ctx + 0x3016, (ushort)sVar10);  // quad3.y2
                    PsxRam.WriteU16(ctx + 0x2ff6, (ushort)sVar10);  // quad2.y3
                    PsxRam.WriteU16(ctx + 0x2fee, (ushort)sVar10);  // quad2.y2
                }
                else
                {
                    uVar11 = uVar11 | 0x10000;
                    PsxRam.WriteI32(ctx + 0x10, (int)uVar11);
                }
            }
        }

        if ((PsxRam.ReadI32(ctx + 0x10) & 0x80000) == 0)
        {
            if ((PsxRam.ReadU8(ctx + 0x2f89) & 1) != 0) // quad0.g0 bit 0 — the pulse latch
            {
                PsxRam.WriteU8(ctx + 0x2f8a, 0x80); // quad0.b0
                PsxRam.WriteU8(ctx + 0x2f89, 0x80); // quad0.g0
                PsxRam.WriteU8(ctx + 0x2f88, 0x80); // quad0.r0
                PsxRam.WriteU8(ctx + 0x2fb2, 0x80); // quad1.b0
                PsxRam.WriteU8(ctx + 0x2fb1, 0x80); // quad1.g0
                PsxRam.WriteU8(ctx + 0x2fb0, 0x80); // quad1.r0
            }
        }
        else
        {
            if (PsxRam.ReadI32(ctx + 0x302c) < 1)
            {
                PsxRam.WriteU8(ctx + 0x2f88, (byte)(PsxRam.ReadU8(ctx + 0x2f88) ^ 0x3f)); // quad0.r0
                PsxRam.WriteU8(ctx + 0x2fb0, (byte)(PsxRam.ReadU8(ctx + 0x2fb0) ^ 0x3f)); // quad1.r0
            }
            else
            {
                PsxRam.WriteU8(ctx + 0x2f8a, (byte)(PsxRam.ReadU8(ctx + 0x2f8a) ^ 0x7f)); // quad0.b0
                PsxRam.WriteU8(ctx + 0x2fb2, (byte)(PsxRam.ReadU8(ctx + 0x2fb2) ^ 0x7f)); // quad1.b0
            }

            PsxRam.WriteU8(ctx + 0x2f89, (byte)(PsxRam.ReadU8(ctx + 0x2f89) | 1)); // quad0.g0
            PsxRam.WriteU8(ctx + 0x2fb1, (byte)(PsxRam.ReadU8(ctx + 0x2fb1) | 1)); // quad1.g0
        }

    LAB_8005cb40:

        // PART FOUR — submit the four quads. PARTIAL: see the comment above the function.
        LibGpu.AddPrim(PsxRam.ReadI32(Dat8008d420Address) - (PsxRam.ReadI32(ctx + 0x3030) * 4 + -0x206c), ctx + 0x2f84);
        LibGpu.AddPrim(PsxRam.ReadI32(Dat8008d420Address) - (PsxRam.ReadI32(ctx + 0x3030) * 4 + -0x206c), ctx + 0x2fac);
        LibGpu.AddPrim(PsxRam.ReadI32(Dat8008d420Address) - (PsxRam.ReadI32(ctx + 0x3030) * 4 + -0x206c), ctx + 0x2fd4);
        LibGpu.AddPrim(PsxRam.ReadI32(Dat8008d420Address) - (PsxRam.ReadI32(ctx + 0x3030) * 4 + -0x206c), ctx + 0x2ffc);
    }

    // GHIDRA: the character-file name table @ 0x8008330E (VS.EXE)
    // Not owned by any file yet. Eighteen (0x12) bytes per entry, a NUL-terminated CD path with
    // trailing zero padding, indexed from 1 — read-memory shows entry 1 at 0x80083320 as
    // "\AT1\GKN.B;1" and entry 2 at 0x80083332 as "\AT2\GKS.B;1", exactly base + n*0x12 for n=1,2.
    // Index 0 is never used by either caller below (Roster.cs's own ids run 1..38): it would land
    // on the eight halfwords immediately before the table, which are something else entirely.
    private const int CharacterFileNameTableAddress = unchecked((int)0x8008330E);

    // JUSTIFICATION: C# language bridge only
    // RELATION: identical in shape and purpose to VS_EXE/BattleScene.cs's own private
    // `PsxStringAt` — FUN_80061d4c (FileIo.ReadFile) is transliterated to take the `char[]` its
    // own callee WaitSearchFile needs, so every caller that hands it a raw PSX string address, as
    // FUN_80026d08 below does, needs this conversion. Not reused from BattleScene.cs: that copy is
    // private to its own file, and the two ownerships stay separate rather than one file reaching
    // into another's private helper. No behaviour is added beyond the pointer-to-array conversion
    // C# forces, and it reads through PsxRam like every other memory access in this file.
    //
    // PARTIAL, for the same reason BattleScene.cs's copy is: the program image's .rodata is not
    // modelled by this port, so every byte reads back zero and the array comes out empty today.
    // The address arithmetic is the original's and becomes correct the moment the image is
    // modelled. Capped at the table's own 0x12-byte stride rather than BattleScene.cs's generic
    // 0x1b, since every entry here is known to sit on that stride.
    private static char[] PsxStringAt(int address)
    {
        int length = 0;
        while (length < 0x12 && PsxRam.ReadU8(address + length) != 0)
        {
            length = length + 1;
        }

        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = (char)PsxRam.ReadU8(address + i);
        }

        return chars;
    }

    // GHIDRA: FUN_80026d08 @ 0x80026D08 (VS.EXE)
    // 144 bytes, leaf. Its only callee is FUN_80061d4c, already ported as FileIo.ReadFile.
    // param_1 is ctx, param_2 the destination buffer address in the CD staging region FUN_80026ac0
    // below walks, param_3 the slot index (only its low halfword is read, matching the `& 0xffff`
    // the decompiler prints at every use).
    //
    // The record it reads from is ctx + slot * CtxSlotRecordStride + 0x15BC — unnamed in
    // BattleState.cs, four bytes past CtxGaugeContribution and four before CtxTargetIndex — which
    // FUN_800594b4's own first loop below is what seeds with the roster character id. On success
    // this stores the buffer address at ctx + slot*4 + 0x16A0 (a second array BattleState.cs does
    // not name either; FUN_80026ac0 below is its only other reader/writer in this slice) and the
    // returned value at record+0x12 (0x15C2, ctx + slot*0x14 + 0x15C2); on failure it returns -1
    // and touches neither. Per FileIo.ReadFile's own note, ReadCDData returns an unsigned sector
    // count or 0, so the `< 0` test can never fire on the console. Reproduced, not corrected.
    private static int FUN_80026d08(int param_1, int param_2, uint param_3)
    {
        int iVar2 = param_1 + (int)(param_3 & 0xffff) * BattleState.CtxSlotRecordStride;
        int iVar1 = (int)FileIo.ReadFile(
            PsxStringAt(CharacterFileNameTableAddress + (short)PsxRam.ReadU16(iVar2 + 0x15bc) * 0x12),
            param_2, 0);

        if (iVar1 < 0)
        {
            iVar1 = -1;
        }
        else
        {
            PsxRam.WriteI32((int)(param_3 & 0xffff) * 4 + param_1 + 0x16a0, param_2);
            PsxRam.WriteU16(iVar2 + 0x15c2, (ushort)iVar1);
        }

        return iVar1;
    }

    // GHIDRA: FUN_80026ac0 @ 0x80026AC0 (VS.EXE)
    // 584 bytes. THE TARGET-REASSIGNMENT SCAN. One caller, FUN_800594b4 below, which always passes
    // param_3 == 0 and param_2 == Roster.PTR_DAT_800844b8 + 0x112 — the ordinal array Roster.cs's
    // own OWNERSHIP CAVEAT already earmarks for this function by name. Only the param_3 == 0 arm is
    // ever exercised on the console; the else arm is transliterated anyway, faithfully, since
    // Ghidra decompiles it as live code and the mandate is a full port, not a reachability prune.
    //
    // param_4 IS THE FOURTH GHIDRA PARAMETER, but the call site passes only three arguments — the
    // fourth register carries over whatever FUN_800594b4's own prior work left in it. That dead
    // value is safe to discard: inside the param_3 == 0 arm, `bVar1` resets false at the top of
    // every outer uVar6 iteration, and the only read of param_4 (the `if (bVar1)` branch) can only
    // be reached after FUN_80026d08 has already reassigned it earlier in that same iteration. The
    // incoming value is never read. It is passed as 0 below.
    //
    // `puVar7 = &DAT_80110000` is VS_EXE_exe.cs's own CD staging region at 0x80110000 — that file's
    // own comment on its DAT_80110000 declaration already names FUN_80026ac0 as one of its unported
    // readers. Reached here by the raw address rather than a second declaration: PsxRam resolves
    // through the one installed resolver regardless of which file spells the literal, so this is
    // the same backing bytes, not a second store.
    private static void FUN_80026ac0(int param_1, int param_2, short param_3, int param_4)
    {
        if (param_3 == 0)
        {
            int puVar7 = unchecked((int)0x80110000);
            ushort uVar6 = 1;
            do
            {
                bool bVar1 = false;
                ushort uVar4 = 0;
                int puVar5 = param_2;
                do
                {
                    ushort slotVal = PsxRam.ReadU16(puVar5);
                    if (slotVal != 0 && slotVal == uVar6)
                    {
                        if (bVar1)
                        {
                            PsxRam.WriteI32((int)((uint)uVar4 * 4) + param_1 + 0x16a0, puVar7 + param_4 * -0x800);
                            PsxRam.WriteU16(
                                param_1 + (int)((uint)uVar4 * BattleState.CtxSlotRecordStride) + 0x15c2, (ushort)param_4);
                        }
                        else
                        {
                            do
                            {
                                param_4 = FUN_80026d08(param_1, puVar7, uVar4);
                            } while (param_4 == 0);

                            puVar7 = puVar7 + param_4 * 0x800;
                            bVar1 = true;
                        }
                    }

                    uVar4 = (ushort)(uVar4 + 1);
                    puVar5 = puVar5 + 2;
                } while (uVar4 < 0xc);

                uVar6 = (ushort)(uVar6 + 1);
            } while (uVar6 < 0xd);
        }
        else
        {
            ushort uVar6 = 0xb;
            ushort uVar4 = 0;
            int puVar5 = param_2;
            do
            {
                ushort uVar2 = PsxRam.ReadU16(puVar5);
                puVar5 = puVar5 + 2;
                if ((uVar2 & 0x8000) != 0)
                {
                    uVar2 = (ushort)(uVar2 & 0x7fff);
                    if (uVar2 < uVar6)
                    {
                        uVar6 = uVar2;
                    }
                }

                uVar4 = (ushort)(uVar4 + 1);
            } while (uVar4 < 0xc);

            uVar4 = 0;
            puVar5 = param_2;
            do
            {
                ushort uVar2 = PsxRam.ReadU16(puVar5);
                puVar5 = puVar5 + 2;
                if (uVar6 == uVar2)
                {
                    break;
                }

                uVar4 = (ushort)(uVar4 + 1);
            } while (uVar4 < 0xc);

            int iVar8 = PsxRam.ReadI32((int)((uint)uVar4 * 4) + param_1 + 0x16a0);
            uVar4 = 0;
            do
            {
                ushort uVar2 = 0;
                puVar5 = param_2;
                do
                {
                    ushort raw = PsxRam.ReadU16(puVar5);
                    uint uVar3 = (uint)(raw & 0x7fff);
                    if ((raw & 0x8000) != 0)
                    {
                        if (uVar3 == uVar6)
                        {
                            do
                            {
                                param_4 = FUN_80026d08(param_1, iVar8, uVar2);
                            } while (param_4 == 0);

                            iVar8 = iVar8 + param_4 * 0x800;
                            uVar6 = (ushort)(uVar6 + 1);
                        }
                        else if (uVar3 == (uint)(uVar6 - 1))
                        {
                            PsxRam.WriteI32((int)((uint)uVar2 * 4) + param_1 + 0x16a0, iVar8 + param_4 * -0x800);
                            PsxRam.WriteU16(
                                param_1 + (int)((uint)uVar2 * BattleState.CtxSlotRecordStride) + 0x15c2, (ushort)param_4);
                        }
                    }

                    uVar2 = (ushort)(uVar2 + 1);
                    puVar5 = puVar5 + 2;
                } while (uVar2 < 0xc);

                uVar4 = (ushort)(uVar4 + 1);
            } while (uVar4 < 0xc);
        }
    }

    // GHIDRA: FUN_800594b4 @ 0x800594B4 (VS.EXE)
    // 2528 bytes. First of the three sub-initialisers state 0 runs, and it reads DAT_8008d320
    // three times — which is why that global is written BEFORE these calls and not after.
    //
    // PARTIAL. Four callees: three are real SDK (SetPolyFT4/SetSemiTrans/SetShadeTex, all called
    // only inside the blocked tail below) and the fourth, FUN_80026ac0 above, is now ported. Every
    // STATE write this function makes onto the battle context is closed and transliterated below:
    // the roster's character ids seeded into each slot record (feeding FUN_80026d08's filename
    // lookup), the two acting-slot cursors and the +0x18 index, the flag-run copy from Roster's
    // own record into both per-slot tables, the FUN_80026ac0 call itself, and the two default
    // target-index sweeps that follow it.
    //
    // BLOCKED, and it is everything after: from 0x80059640 (inside the flag-run loop) through
    // 0x80059e8c, the original builds roughly thirty POLY_FT4 quads — a per-slot HUD pair anchored
    // on Roster's own DAT_80084184/DAT_80084186 portrait-coordinate columns plus two small local
    // tables (DAT_80083e30, DAT_80083e40 — two (short,short) pairs, each read twice, both reads
    // inside this function and nowhere else in the overlay) and a numeric-tally element anchored on
    // DAT_80084220 and a byte table the decompiler renders as the string literal
    // "s_H_HPH_HpHpH0XPH_X_80084221". Ghidra fails to recognise most of these stores as POLY_FT4
    // fields at all — they decompile as a `char *`/`undefined2 *` walk with wraparound negative
    // offsets (`pcVar19[-0xffffffff0000005f]`), unlike the clean `p->field` shape
    // InitCentralGaugeBar's own PART ONE gets for the same primitive type. Hand-mapping roughly 250
    // lines of that arithmetic without a working disassembly cross-check is exactly the situation
    // FUN_8005a104's own inner block above already declined for a smaller, cleaner case; this one is
    // both larger and messier, so it is left unperformed the same way, with the loop shell that
    // carries the two closed writes kept so the boundary is visible. The trailing
    // `*(undefined2 *)(param_1 + 0x2dc4) = 1` (CtxTallyValue = 1) is included in the block rather
    // than ported on its own: it is the "these primitives are ready" flag for geometry this function
    // never builds, and setting it without the geometry risks a worse outcome than leaving it at 0.
    private static void FUN_800594b4(int param_1)
    {
        int puVar6 = Roster.PTR_DAT_800844b8;

        // 0x800594D4 — seed every slot record's own +0xC (0x15BC, unnamed) with the roster
        // character id, and the matching sub-record's own +0xC (unnamed; SubRecordOrdinal is
        // +0x14, this is not that field) with a difficulty-scaled variant of the same id.
        {
            int psVar24 = puVar6 + 0x52;
            short sVar20 = (short)(SharedHighRam.DAT_801ff01c * 0x25);
            int local_38 = param_1 + BattleState.CtxSlotSubRecords;

            for (int uVar13 = 0; uVar13 < 0xc; uVar13++)
            {
                short id = (short)PsxRam.ReadU16(psVar24);
                PsxRam.WriteU16(param_1 + uVar13 * BattleState.CtxSlotRecordStride + 0x15bc, (ushort)id);
                psVar24 += 2;
                PsxRam.WriteU16(local_38 + 0xc, (ushort)(sVar20 - 1 + id));
                local_38 += BattleState.CtxSlotSubRecordStride;
            }
        }

        // 0x80059568 — THE ACTING-SLOT CURSORS. First slot of each team whose record+0xA flag run
        // carries bit 3, defaulting to the team's own first slot when none does.
        {
            ushort teamACursor = 0;
            do
            {
                if ((PsxRam.ReadU16(puVar6 + teamACursor * 2 + 10) & 8) != 0)
                {
                    break;
                }

                teamACursor++;
            } while (teamACursor < 6);

            if (teamACursor == 6)
            {
                teamACursor = 0;
            }

            PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamA, teamACursor);
            // +0x18, unnamed: this file's own header calls it "a slot index compared against
            // +0x2DC2 and against the +0x1520 walk index" — here it is stamped with the same value
            // as the team-A cursor, at arming time only.
            PsxRam.WriteU16(param_1 + 0x18, teamACursor);

            ushort teamBCursor = 6;
            do
            {
                if ((PsxRam.ReadU16(puVar6 + teamBCursor * 2 + 10) & 8) != 0)
                {
                    break;
                }

                teamBCursor++;
            } while (teamBCursor < 0xc);

            if (teamBCursor == 0xc)
            {
                teamBCursor = 6;
            }

            PsxRam.WriteU16(param_1 + BattleState.CtxActingSlotTeamB, teamBCursor);
        }

        // 0x8005961C — copy Roster's own flag-triplet run (record+0x82, twelve rows of three
        // halfwords) into each slot record's own +2 / CtxKiGauge(+4) / +6, AND into three fixed
        // ctx halfwords (+0x22, +0x24, +0x26 — unnamed, adjacent to the +0x20 sub-record base)
        // that are overwritten every turn of the loop, so only the LAST slot's triplet survives
        // there. Reproduced exactly as printed, not corrected.
        {
            PsxRam.WriteU16(param_1 + 0x1c, 0); // unnamed, next to +0x18/+0x1A in the head
            int puVar25 = puVar6 + 0x82;

            for (uint uVar13 = 0; uVar13 < 0xc; uVar13++)
            {
                ushort v0 = PsxRam.ReadU16(puVar25);
                PsxRam.WriteU16(param_1 + 0x22, v0);
                int iVar11 = param_1 + (int)uVar13 * BattleState.CtxSlotRecordStride;
                PsxRam.WriteU16(iVar11 + 0x15b2, v0); // CtxSlotRecords + 2, unnamed

                ushort v1 = PsxRam.ReadU16(puVar25 + 2);
                PsxRam.WriteU16(param_1 + 0x24, v1);
                PsxRam.WriteU16(iVar11 + BattleState.CtxKiGauge, v1);

                ushort v2 = PsxRam.ReadU16(puVar25 + 4);
                PsxRam.WriteU16(param_1 + 0x26, v2);
                PsxRam.WriteU16(iVar11 + 0x15b6, v2); // CtxSlotRecords + 6, unnamed

                puVar25 += 6;
            }

            // 0x80059688..0x800596CC — A SECOND TWELVE-SLOT LOOP, and a first version of this port
            // dropped it entirely while its own header claimed every state write was closed. The
            // instructions leave no doubt:
            //     000210C0  sll  v0,v0,3          ; stride 8, NOT the 0x14 of CtxSlotRecords
            //     00E21021  addu v0,a3,v0
            //     A4431550  sh   v1,0x1550(v0)
            //     A4431552  sh   v1,0x1552(v0)
            //     A4431554  sh   v1,0x1554(v0)
            //     2C42000C  sltiu v0,v0,0xC       ; twelve iterations
            // It continues the SAME puVar25 cursor the flag-triplet loop above left behind, which is
            // why it has to sit exactly here and not anywhere else.
        //
            // The region it fills, 0x1550..0x15AF, is the 0x60-byte gap between CtxFighterSlots
            // (0x1520, twelve pointers ending at 0x1550) and CtxSlotRecords (0x15B0). It is a real
            // twelve-entry array of stride 8, not padding -- but only three of its eight bytes per
            // entry are written here, so it is left as raw offsets rather than named in BattleState.cs
            // on the strength of one writer.
            for (uint uVar13 = 0; uVar13 < 0xc; uVar13++)
            {
                int iVar11 = param_1 + (int)uVar13 * 8;
                PsxRam.WriteU16(iVar11 + 0x1550, PsxRam.ReadU16(puVar25));
                PsxRam.WriteU16(iVar11 + 0x1552, PsxRam.ReadU16(puVar25 + 2));
                PsxRam.WriteU16(iVar11 + 0x1554, PsxRam.ReadU16(puVar25 + 4));
                puVar25 += 6;
            }
        }

        // 0x800596D4 — THE CALL THIS SLICE EXISTS TO UNBLOCK. param_2 is Roster's own ordinal
        // array (+0x112); param_3 = 0 selects the arm that scans it. param_4's incoming value is
        // dead (see the comment above FUN_80026ac0) and is passed as 0.
        FUN_80026ac0(param_1, puVar6 + 0x112, 0, 0);

        // 0x800596E4 — TEAM A'S DEFAULT TARGET. For slots 0..2, default CtxTargetIndex to 6 when
        // team B's matching id slot (record+0x5E) is empty, else to a running 6, 7, 8...
        {
            int teamBIds = puVar6 + 0x5e;
            short seq = 6;
            for (ushort uVar22 = 0; uVar22 < 3; uVar22++)
            {
                if (PsxRam.ReadU16(teamBIds) == 0)
                {
                    PsxRam.WriteU16(
                        param_1 + uVar22 * BattleState.CtxSlotRecordStride + BattleState.CtxTargetIndex, 6);
                    teamBIds = puVar6 + 0x60;
                    seq = 7;
                }
                else
                {
                    PsxRam.WriteU16(
                        param_1 + uVar22 * BattleState.CtxSlotRecordStride + BattleState.CtxTargetIndex,
                        (ushort)seq);
                    teamBIds += 2;
                    seq++;
                }
            }
        }

        // 0x80059758 — TEAM B'S DEFAULT TARGET, mirroring the block above off team A's ids
        // (record+0x52). CLOSED but UNEXPLAINED: the bound is 0xF against a cursor that starts at
        // 6, nine iterations (slots 6..14), not the three (6..8) the team-A block above uses for
        // its own three real slots — verified against both the decompiled C and the disassembly's
        // own `sltiu v0,v0,0xf` at 0x800597bc, not assumed. Slots 9..11 match this file's own
        // established Rule 12 (records kept for fighters that do not exist); slots 12..14 write
        // three CtxTargetIndex-shaped halfwords past the twelve-slot record array entirely, into
        // memory this port does not otherwise name. Reproduced as printed, not corrected.
        {
            int teamAIds = puVar6 + 0x52;
            short seq = 0;
            for (ushort uVar22 = 6; uVar22 < 0xf; uVar22++)
            {
                if (PsxRam.ReadU16(teamAIds) == 0)
                {
                    PsxRam.WriteU16(
                        param_1 + uVar22 * BattleState.CtxSlotRecordStride + BattleState.CtxTargetIndex, 0);
                    teamAIds = puVar6 + 0x54;
                    seq = 1;
                }
                else
                {
                    PsxRam.WriteU16(
                        param_1 + uVar22 * BattleState.CtxSlotRecordStride + BattleState.CtxTargetIndex,
                        (ushort)seq);
                    teamAIds += 2;
                    seq++;
                }
            }
        }

        // 0x80059758..0x80059CE8 — the loop shell is kept because two of its writes are closed
        // (Roster's own flags run, copied into both per-slot tables' own +0x00); everything else
        // this loop's body builds is the blocked POLY_FT4 pair the comment above the function
        // describes, and is not performed.
        {
            int puVar25 = puVar6 + 0xa;
            int local_38 = param_1 + BattleState.CtxSlotSubRecords;

            for (uint uVar22 = 0; uVar22 < 0xc; uVar22++)
            {
                ushort flagRun = PsxRam.ReadU16(puVar25);
                puVar25 += 2;
                PsxRam.WriteU16(
                    param_1 + (int)uVar22 * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords, flagRun);
                PsxRam.WriteU16(local_38 + BattleState.SubRecordFlags, flagRun);

                // BLOCKED — see the comment above the function: the per-slot POLY_FT4 pair this
                // iteration also builds (0x80059640..0x80059ce8 in the image) is not performed.

                local_38 += BattleState.CtxSlotSubRecordStride;
            }
        }

        // BLOCKED — 0x80059CF4..0x80059E8C: two further POLY_FT4 loops (nine iterations off
        // DAT_80084220 / the "H HPH..." byte table, then two more off pPVar17[1].y2) building the
        // numeric-tally HUD element, and the CtxTallyValue = 1 store that would announce it ready.
        // See the comment above the function for why none of it is performed.
    }

    // GHIDRA: InitCentralGaugeBar @ 0x80059E94 (VS.EXE)
    // Second sub-initialiser state 0 runs; it sits between FUN_800594b4 and FUN_8005a104 in the
    // address space, so the three are consecutive. 624 bytes, in three parts, all closed.
    //
    // PART ONE, closed: four POLY_FT4 packets at ctx+0x2F84, stride 0x28 — the ONLY primitive pool
    // this function's own POLY_FT4 pointer walks, so this is the central-gauge bar's own geometry,
    // built once at arming time. `p` is `(POLY_FT4 *)(param_1 + 0x2f84)`; the decompiler also
    // tracks a SECOND, `undefined2 *` cursor `puVar6` at `param_1 + 0x2f9e`, i.e. `p + 0x1a`
    // (26 bytes), advancing in lock-step with `p` every iteration (both step one record, 0x28
    // bytes, per turn) — so every `puVar6[k]` / `*(undefined1 *)(puVar6+k)` below is just another
    // named POLY_FT4 field reached through a second pointer, and is ported as that field. Two of
    // the literals looked, at first read, like the SetShadeTex/SetSemiTrans calls' own return
    // values leaking into the next store (`puVar6[-2] = 0x9e`, `uVar1 = 0xe2` / `= 0xd7`) — they
    // are not: the raw bytes at 0x80059f04 are a plain `ori v0,zero,0x9e`, between the `jal` and
    // the store, that overwrites v0 first. Checked because the decompiler's own line-to-instruction
    // pairing is imprecise across this stretch and the first read looked like the call's own
    // result.
    //
    // The first two of the four iterations (iVar7 < 2) and the last two tag the quad differently —
    // clut 0x7B00 vs 0x7B40, a different u/v rectangle — so the pool is two pairs of segments,
    // plausibly the two teams' halves of the tug-of-war bar this file's header already closes
    // (ctx+0x302C). Nothing downstream of arming is in THIS slice to confirm that reading, so it
    // stays a plausibility, not a closed fact.
    //
    // PART TWO, closed. The original copies the FIRST 64 halfwords of `DAT_800842b8` and the FIRST
    // 64 halfwords of `DAT_800843b8` — a fixed prefix of each 128-halfword table, not the circular
    // full-table sample UpdateCentralGaugeBar's own PART TWO does every frame — into a 256-byte
    // STACK buffer (`local_128` then `local_a8`, contiguous by construction: two half-copies of one
    // region) and hands that stack address to
    // `LoadImage_ReturnTPageOrClutId(local_128, 0, 0x1ed, 0x80, 1, '\0')` — a CLUT upload, isClut
    // truthy per FileIo's own signature for that call. See the GHIDRA comment on DAT_800842b8 /
    // DAT_800843b8 below (declared just before UpdateCentralGaugeBar, which comes first in this
    // file) for the cross-reference check that closes both tables and for why each is 128
    // halfwords, not 64, despite this function reading only the first 64 of each. The 256-byte
    // buffer itself is GaugeStripBufferAddress (0x807FFEC0) rather than a stack local — see the
    // DEVIATION on that declaration for why one address serves both this function and
    // UpdateCentralGaugeBar. The call's return value is unused at this call site in the original, so
    // nothing later in this function depends on what the upload produces.
    //
    // PART THREE, closed: six trailing stores, independent of the blocked upload. ctx+0x302C is
    // BattleState.CtxCentralGauge — the same word RunBattleRound accumulates into and clamps
    // elsewhere in this file — so this is the gauge's OWN zero, ahead of arming, distinct from the
    // wind-down's later reset.
    private static void InitCentralGaugeBar(int param_1)
    {
        var resolved = PsxRam.AddressResolver?.Invoke(param_1);
        if (resolved != null)
        {
            (byte[] buffer, int offset) = resolved.Value;
            POLY_FT4Ref basePoly = new POLY_FT4Ref(buffer, offset + 0x2f84);

            sbyte cVar9 = -0x60;
            sbyte cVar10 = -0x51;
            short sVar8 = 0xa0;
            short sVar11 = 0x20;

            for (int iVar7 = 0; iVar7 < 4; iVar7++)
            {
                POLY_FT4Ref p = basePoly[iVar7];
                LibGpu.SetPolyFT4(p);
                LibGpu.SetSemiTrans(p, 0);
                LibGpu.SetShadeTex(p, 0);
                p.tpage = 0x9e;
                p.r0 = 0x80;
                p.g0 = 0x80;
                p.b0 = 0x80;

                ushort uVar1;
                if (iVar7 < 2)
                {
                    sbyte cVar4 = (sbyte)(iVar7 * 0x20);
                    sbyte cVar5 = (sbyte)(cVar4 - 0x80);
                    cVar4 = (sbyte)(cVar4 - 0x61);
                    short sVar3 = (short)((1 - iVar7) * -0x50 + 0xa0);

                    p.u2 = 0x30;
                    p.clut = 0x7b00;
                    p.x3 = sVar8;
                    p.x1 = sVar8;
                    p.y1 = 0xc2;
                    p.y0 = 0xc2;
                    uVar1 = 0xe2;
                    p.u0 = 0x30;
                    p.u3 = 0x80;
                    p.u1 = 0x80;
                    p.v1 = (byte)cVar5;
                    p.v0 = (byte)cVar5;
                    p.v3 = (byte)cVar4;
                    p.v2 = (byte)cVar4;
                    p.x2 = sVar3;
                    p.x0 = sVar3;
                }
                else
                {
                    short sVar3 = (short)((3 - iVar7) * -0x40 + 0xa0);

                    p.u2 = 0x30;
                    p.clut = 0x7b40;
                    p.u3 = 0x6f;
                    p.u1 = 0x6f;
                    p.v1 = (byte)cVar9;
                    p.v0 = (byte)cVar9;
                    p.v3 = (byte)cVar10;
                    p.v2 = (byte)cVar10;
                    p.x3 = sVar11;
                    p.x1 = sVar11;
                    p.y1 = 199;
                    p.y0 = 199;
                    uVar1 = 0xd7;
                    p.u0 = 0x30;
                    p.x2 = sVar3;
                    p.x0 = sVar3;
                }

                p.y3 = (short)uVar1;
                p.y2 = (short)uVar1;

                sVar11 = (short)(sVar11 + 0x40);
                cVar10 = (sbyte)(cVar10 + 0x10);
                cVar9 = (sbyte)(cVar9 + 0x10);
                sVar8 = (short)(sVar8 + 0x50);
            }
        }

        // PART TWO — copy the first 64 halfwords of each table into the two halves of the 256-byte
        // strip, then upload it. See the comment above the function.
        for (int i = 0; i < 0x40; i++)
        {
            PsxRam.WriteU16(GaugeStripBufferAddress + i * 2, PsxRam.ReadU16(Dat800842b8Address + i * 2));
        }

        for (int i = 0; i < 0x40; i++)
        {
            PsxRam.WriteU16(GaugeStripBufferAddress + 0x80 + i * 2, PsxRam.ReadU16(Dat800843b8Address + i * 2));
        }

        FileIo.LoadImage_ReturnTPageOrClutId(GaugeStripBufferAddress, 0, 0x1ed, 0x80, 1, 0);

        PsxRam.WriteU16(param_1 + 0x3024, 0);
        PsxRam.WriteU16(param_1 + 0x3026, 0);
        PsxRam.WriteU16(param_1 + 0x3028, 1);
        PsxRam.WriteU16(param_1 + 0x302a, 1);
        DAT_8008d3a8 = 0;
        PsxRam.WriteI32(param_1 + BattleState.CtxCentralGauge, 0);
        PsxRam.WriteI32(param_1 + 0x3030, 0);
    }

    // GHIDRA: FUN_8005a104 @ 0x8005A104 (VS.EXE)
    // Third sub-initialiser, ending at 0x8005A5AF, one byte below RunBattleManagerFrame. 1196 bytes, and
    // ALMOST ALL of it is one primitive-setup block this slice cannot close.
    //
    // THE OUTER LOOP, closed: twelve iterations (sVar12 = 0..11), and on every one of them —
    // whether or not the inner block below runs — a halfword is copied from
    // Roster.PTR_DAT_800844b8+0x22 (the battle setup record VS_EXE/Roster.cs already owns and
    // names; its own OWNERSHIP CAVEAT names this exact read) into ctx+0x2C14, this file's own
    // "SECOND per-slot table" its header already documents. So THIS is where that table's bit 0x40
    // — the one the inner block below tests — is first populated, once, at arming.
    //
    // THE INNER BLOCK (sVar12 < 6), BLOCKED. `p = (POLY_FT4 *)(param_1 + sVar12*0xf0 + 0x16d0)` is
    // a group of (at least) six POLY_FT4 packets per slot — a per-fighter HUD element, given the
    // loop only ever reaches six of the twelve slots. Ghidra types `p` as a REAL `POLY_FT4 *` here
    // (unlike InitCentralGaugeBar), so every `p->field` / `p[n].field` below already names its own
    // POLY_FT4Ref field one-for-one — including the two the decompiler prints as `p->_2` / `p->_3`,
    // which get-structure-info resolves to `v0` (offset 0x0D) and `v1` (offset 0x15) of the
    // `psyq330` POLY_FT4 layout, i.e. POLY_FT4Ref's own `v0`/`v1`. None of THAT is what blocks it.
    //
    // What blocks it is the ANCHOR every one of those six packets is positioned from:
    //     sVar1 = *(short *)(&DAT_80084578 + DAT_800845d0 * 4);
    //     sVar2 = *(short *)(&DAT_8008457a + DAT_800845d0 * 4);
    // a stride-4 (x, y) pair table indexed by DAT_800845d0, plus a second table pair —
    // `&DAT_80084184 + sVar12*6` / `&DAT_80084186 + sVar12*6`, a 12-byte-stride row per slot — that
    // feeds the sixth packet's tpage/u/v. The second pair is VS_EXE/Roster.cs's own "portrait
    // coordinate table" (its GHIDRA: DAT_80084184 comment names the same 12-byte stride and the
    // same twelve rows), so it is not new; but DAT_80084578, DAT_8008457a and DAT_800845d0 are not
    // declared ANYWHERE in this port. They sit 0x3F4 bytes past DAT_80084184 in the image — plausibly
    // one more column of Roster's own table domain, an anchor per formation rather than per slot —
    // but that is a guess this slice has no evidence to close, and every other function this port
    // has ported treats a table like this as belonging to whichever file already owns its
    // neighbours. Declaring it here, in a file that owns none of Roster's other tables, would risk
    // exactly the two-spellings-of-one-field defect VS_EXE/BattleState.cs exists to prevent. So the
    // whole `if (sVar12 < 6)` body is left unperformed; every packet it would have built keeps
    // whatever the heap/image already holds there.
    //
    // THE TAIL, closed: two more of this file's own gp-relative globals, zeroed unconditionally
    // after the loop.
    private static void FUN_8005a104(int param_1)
    {
        for (short sVar12 = 0; sVar12 < 0xc; sVar12++)
        {
            PsxRam.WriteU16(param_1 + 0x2c14 + sVar12 * 2,
                PsxRam.ReadU16(Roster.PTR_DAT_800844b8 + 0x22 + sVar12 * 2));

            // BLOCKED for sVar12 < 6 — see the comment above the function: the six-packet
            // POLY_FT4 group anchored on DAT_80084578/DAT_8008457a/DAT_800845d0 is not built.
        }

        DAT_8008d458 = 0;
        DAT_8008d3f8 = 0;
    }

    // GHIDRA: FUN_8005ee5c @ 0x8005EE5C (VS.EXE)
    // Called three times from RunBattleRound with (-1, -1, 0x10), (0, 0, 0x30) and (0, 0, 0x28), plus
    // once from ExecuteAnimStreamBatch with (0, 0, 0x30). VS_EXE/AnimVmInterpreter.cs held an
    // identical private empty stub for the same address; that duplicate is already gone (see the
    // note above) and this is the one surviving copy.
    //
    // 196 bytes, closed in full: nothing but halfword/byte stores into fixed offsets of
    // SoundState.DAT_8008d284 — the sound workspace VS_EXE/SoundState.cs and
    // VS_EXE/SoundDriver.cs already name and own — one signed-halfword read that decides the last
    // branch, and one word read of ctx+0x10, THIS file's own flag word, through
    // BattleManager.DAT_8008d320. Every load/store width below is the instruction's own — `lhu`/`sh`
    // for the halfwords, `sb` for the bytes, `lw` for the ctx+0x10 test.
    //
    // Ghidra types param_1/param_2 `ushort`, but the signature already established here (and shared
    // with every call site in this file) is `int`, and one call site passes -1 for both. The
    // ORIGINAL register holds 0xFFFFFFFF too, and the callee's own comparison is against the 16-bit
    // constant 0xFFFF, so the comparison below truncates param_1 to 16 bits first — comparing the
    // untruncated 32-bit -1 against 0xffff would never be true, and would silently take the wrong
    // branch on every call this function actually receives -1 from.
    internal static void FUN_8005ee5c(int param_1, int param_2, int param_3)
    {
        PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x11e, PsxRam.ReadU16(SoundState.DAT_8008d284 + 0x11a));
        PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x120, PsxRam.ReadU16(SoundState.DAT_8008d284 + 0x11c));

        if ((ushort)param_1 == 0xffff)
        {
            PsxRam.WriteU8(SoundState.DAT_8008d284 + 0x143, 0x40);
            PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x122, 0x52);
            PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x124, 0x52);
            PsxRam.WriteU8(SoundState.DAT_8008d284 + 0x142, 0x40);

            if ((PsxRam.ReadI32(DAT_8008d320 + 0x10) & 0x2008) != 0)
            {
                PsxRam.WriteU8(SoundState.DAT_8008d284 + 0x143, 0x10);
                PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x122, 0x36);
                PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x124, 0x36);
                PsxRam.WriteU8(SoundState.DAT_8008d284 + 0x142, 0x10);
            }
        }
        else
        {
            PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x122, (ushort)(param_1 & 0x7f));
            PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x124, (ushort)(param_2 & 0x7f));
        }

        short sVar2 = (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + 0x110);
        PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x128, (ushort)param_3);
        PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x126, 1);
        if (sVar2 == 0)
        {
            PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x110, 0x15);
        }
    }

    // GHIDRA: FUN_8005ef20 @ 0x8005EF20 (VS.EXE)
    // BLOCKED: 328 bytes. Called once, with (0, 0), immediately after FUN_8005ee5c on the
    // round-is-over path.
    internal static void FUN_8005ef20(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_80060120 @ 0x80060120 (VS.EXE)
    // NOT STUBBED AND NOT REDECLARED. 12 bytes — `lh v0,0x288(gp)` and `jr ra`, i.e.
    // `return (int)DAT_8008d384;` — with FIVE call sites across the overlay, one of them state 2
    // above. VS_EXE/AnimCmdSound.cs already transliterates it as `internal static int
    // FUN_80060120()` over its own `DAT_8008d384`, and gives its reason: a stub returning 0 would
    // be an INVENTED value rather than a blocked one, because callers branch on the result. State 2
    // calls that one. A second copy here would have answered 0 for ever while the sound driver's
    // copy moved.

    // GHIDRA: FUN_8005ecf4 @ 0x8005ECF4 (VS.EXE)
    // 52 bytes, one caller and it is FUN_800578e0. Formerly stubbed because DAT_8008d284 — the
    // sound workspace — had no owner yet; VS_EXE/SoundState.cs now declares it, so this closes.
    // `lh v0,0x110(v1)` is a SIGNED halfword load (not `lhu`), matching the `short` type and the
    // signed comparison `slti v0,v0,0x10` that decides the clamp.
    private static int FUN_8005ecf4()
    {
        short sVar1 = (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + 0x110);
        if (0xf < sVar1)
        {
            sVar1 = 0;
        }

        return sVar1;
    }

    // GHIDRA: DisableReverb @ 0x80060A4C (VS.EXE)
    // 60 bytes. The first thing state 2 does once every one of its four conditions is clear,
    // immediately before the DAT_801FF100 write. Four PSYQ sound-library calls, nothing else — all
    // four already live in PsxSdkMonogame.LibSnd as documented do-nothing stubs (no reverb hardware
    // is modelled), so the call is faithful even though it currently has no audible effect.
    private static void DisableReverb()
    {
        LibSnd.SsUtSetReverbDepth(0, 0);
        LibSnd.SsUtSetReverbFeedback(0);
        LibSnd.SsUtSetReverbDelay(0);
        LibSnd.SsUtReverbOff();
    }

    // GHIDRA: FUN_800290d0 @ 0x800290D0 (VS.EXE)
    // 76 bytes, and the last call the manager ever makes: state 2 runs it after the hand-back word
    // is already stored and immediately before writing the terminal state 3.
    //
    // CLOSED. FUN_80053330 is TaskSystem.CreateTask (see VS_EXE/TaskSystem.cs), already called the
    // same way from this file's own state 2 (0x800563F8, the scene-task birth). `&LAB_80029200` is
    // Ghidra's own label for the callback, address-of'd and never called here, exactly like
    // `&LAB_80055e3c` in main — Ghidra has not even promoted it to a named function, only a label,
    // and nothing at 0x80029200 belongs to BattleManager: this call's list index is 5, not 9, this
    // file's own list. It is passed through as the raw address CreateTask stores and is not ported
    // here.
    //
    // The insertion point matches every other CreateTask call site already in this file:
    // DAT_80083ba4 sits 0x14 bytes into TaskSystem.g_TaskListTail — five ints past its base — i.e.
    // g_TaskListTail[5], the tail of the SAME list index (5) this call passes, so the new node is
    // appended to the end of list 5. Checked against the image rather than assumed: DAT_80083ba4 -
    // TaskSystem's own g_TaskListTail base (0x80083B90) is exactly 0x14.
    //
    // `**(undefined4 **)(iVar1 + 8) = 2;` is two dereferences, not one: `iVar1 + 8` is the new
    // node's TaskContext field CreateTask itself just populated (raw offset 8, the same one
    // FUN_80055ee0 above reads off TaskSystem.g_CurrentTask), so the first read fetches the fresh
    // 0xc-byte workspace's address, and the write lands the constant 2 in THAT workspace's own
    // first word — one word inside the 0xc bytes CreateTask zeroed for it, not into the task node.
    private static void FUN_800290d0()
    {
        const int LAB_80029200 = unchecked((int)0x80029200);

        int iVar1 = TaskSystem.CreateTask(LAB_80029200, 0, 5, 0xc, 0, TaskSystem.g_TaskListTail[5]);
        PsxRam.WriteI32(PsxRam.ReadI32(iVar1 + 8), 2);
    }
}
