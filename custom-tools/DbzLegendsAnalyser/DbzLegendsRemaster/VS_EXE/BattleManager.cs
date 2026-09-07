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
//   ctx+0x00 == 1   FUN_80055f94 @ 0x80055F94   the round, every frame -> stays 1
//   ctx+0x00 == 2   FUN_800578e0 @ 0x800578E0   the hand-back          -> writes state 3
//   ctx+0x00 == 3   FUN_80057a40 @ 0x80057A40   idle after the match   -> terminal
//
// Nothing in these four functions writes state 2. The 1 -> 2 edge is set somewhere else in the
// overlay and is NOT closed by this slice; state 2 is where the overlay decides what SELECT.EXE
// gets back in DAT_801FF100, so whatever raises it is the end-of-match detector.
//
// WHAT THE SLICE WAS ASKED TO ESTABLISH, answered from the four bodies:
//
//   * WHO INCREMENTS THE CENTRAL GAUGE at +0x302C. FUN_80055f94 does, once per frame, and only it.
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
    internal static void UpdateBattleManager()
    {
        ushort uVar1;

        uVar1 = PsxRam.ReadU16(PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8));
        if (uVar1 == 1)
        {
            FUN_80055f94();
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

    // GHIDRA: FUN_80055f94 @ 0x80055F94 (VS.EXE)
    // STATE 1 — THE ROUND. 6476 bytes, the largest function in this slice by an order of magnitude,
    // and the only one that runs every frame for the length of a match.
    //
    // THE MAP — the phases in evaluation order, with the address each opens at.
    //
    //   0x80055FBC  the VM suspend gate: set -> FUN_8005a5b0 + UpdateCentralGaugeBar and NOTHING else
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
    //   0x80057794  FUN_8005a5b0 + UpdateCentralGaugeBar, the two that run on EVERY path
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
    private static void FUN_80055f94()
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
            FUN_8005a5b0(iVar15);
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

        // 0x80056358 — the two pad overrides. Ghidra renders the first test as
        // `(undefined *)(uVar11 & 0x80008000) == &DAT_80008000`, which is its way of writing the
        // constant 0x8000: bit 15 up and bit 31 down. Both arms clear bits 12 and 13 and TOGGLE bit
        // 14, so pressing the button flips the round body between its two arms. The second is
        // gated on port 2 and on DAT_801FF100 saying the second side is human.
        if ((uVar11 & 0x80008000) == 0x8000 && (uVar11 & 0x18000008) == 0)
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
        FUN_8005a5b0(iVar15);
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
        FUN_8005a5b0(puVar4);
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
        FUN_8005a5b0(uVar1);
        UpdateCentralGaugeBar(uVar1);
    }

    // GHIDRA: FUN_8005cf78 @ 0x8005CF78 (VS.EXE)
    // 40 bytes, ONE caller and it is FUN_80055f94 at 0x80056464, so it belongs to this slice rather
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
    // caller, FUN_80055f94 at 0x80056778. It ends at 0x8005D1FB, one byte below LAB_8005d1fc, the
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
    // FUN_80055f94 and the FUN_800600b0(2) call in FUN_800578e0.
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
    // The label has exactly ONE reference in the whole overlay, `PARAM` from FUN_80055f94 at
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

    // GHIDRA: FUN_8005a5b0 @ 0x8005A5B0 (VS.EXE)
    // BLOCKED: 8500 bytes, by far the largest thing this slice reaches, and it runs on EVERY path
    // of every state including the suspended one. Whatever the manager's real per-frame work is, the
    // bulk of it is in here.
    private static void FUN_8005a5b0(int param_1)
    {
        _ = param_1;
    }

    // =====================================================================================
    // THREE OF FUN_8005a5b0's OWN CALLEES — per-slot HUD/portrait helpers, called from inside its
    // still-BLOCKED per-slot loop (0x8005B9F8..0x8005BA1C, twelve iterations, `puVar13` advancing
    // 0xE0 per slot). None of these three is FUN_8005a5b0 itself and none guesses at what that
    // loop's own base offset from ctx is — that is still unknown, and BLOCKED above with it.
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
    // 1700 bytes, one caller — FUN_8005a5b0 (BLOCKED above, at 0x8005B9F8), which calls it once per
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
    // is this file's own established sign-extend-the-low-halfword idiom (see FUN_80055f94's
    // header), and the multiply-by-N-then-`>> 2`-with-a-`+3`-fixup pairs are the compiler's
    // rounding fix-up for a negative dividend, identical in shape to FUN_80055f94's own central-
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
    // 536 bytes, one caller — FUN_8005a5b0 (BLOCKED above, at 0x8005BA04 and 0x8005BA10) — called
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
    // BLOCKED: 2440 bytes, one caller — FUN_8005a5b0 (BLOCKED above, at 0x8005BA1C) — called once
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
    // 1276 bytes. Always called immediately after FUN_8005a5b0, on all four states, and it ends at
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
    // gate FUN_80055f94 opens with — when it is up, this function's only remaining act is PART FOUR.
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

    // GHIDRA: FUN_800594b4 @ 0x800594B4 (VS.EXE)
    // BLOCKED: 2528 bytes. First of the three sub-initialisers state 0 runs, and it reads
    // DAT_8008d320 three times — which is why that global is written BEFORE these calls and not
    // after.
    private static void FUN_800594b4(int param_1)
    {
        _ = param_1;
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
    // BattleState.CtxCentralGauge — the same word FUN_80055f94 accumulates into and clamps
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
    // Third sub-initialiser, ending at 0x8005A5AF, one byte below FUN_8005a5b0. 1196 bytes, and
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
    // Called three times from FUN_80055f94 with (-1, -1, 0x10), (0, 0, 0x30) and (0, 0, 0x28), plus
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
    private static void FUN_8005ef20(int param_1, int param_2)
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
