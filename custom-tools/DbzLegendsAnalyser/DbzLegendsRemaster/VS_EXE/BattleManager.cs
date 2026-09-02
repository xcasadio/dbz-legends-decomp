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
        FUN_80059e94(puVar1);
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
    //   0x80055FBC  the VM suspend gate: set -> FUN_8005a5b0 + FUN_8005c6e4 and NOTHING else
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
    //   0x80057794  FUN_8005a5b0 + FUN_8005c6e4, the two that run on EVERY path
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
            FUN_8005c6e4(iVar15);
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
        FUN_8005c6e4(iVar15);

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
        FUN_8005c6e4(puVar4);
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
            BattleScene.FUN_8005f704(0, 0);
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
            FUN_80060a4c();
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
        FUN_8005c6e4(uVar1);
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
    private static int DAT_8008d3a8;

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

    // GHIDRA: FUN_8005c6e4 @ 0x8005C6E4 (VS.EXE)
    // BLOCKED: 1276 bytes. Always called immediately after FUN_8005a5b0, on all four states, and it
    // ends at 0x8005CBDF — one byte below FUN_8005cbe0, the roster consumer main calls just after
    // creating this task. The three are one compilation unit.
    private static void FUN_8005c6e4(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_800594b4 @ 0x800594B4 (VS.EXE)
    // BLOCKED: 2528 bytes. First of the three sub-initialisers state 0 runs, and it reads
    // DAT_8008d320 three times — which is why that global is written BEFORE these calls and not
    // after.
    private static void FUN_800594b4(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_80059e94 @ 0x80059E94 (VS.EXE)
    // BLOCKED: 624 bytes. Second sub-initialiser; it sits between the other two in the address
    // space, so the three are consecutive.
    private static void FUN_80059e94(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8005a104 @ 0x8005A104 (VS.EXE)
    // BLOCKED: 1196 bytes. Third sub-initialiser, ending at 0x8005A5AF, one byte below FUN_8005a5b0.
    private static void FUN_8005a104(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8005ee5c @ 0x8005EE5C (VS.EXE)
    // BLOCKED: called three times from FUN_80055f94 with (-1, -1, 0x10), (0, 0, 0x30) and
    // (0, 0, 0x28). It reads DAT_8008d320 at 0x8005EEA8, so it is one of the fifty-two consumers of
    // the pointer state 0 publishes. VS_EXE/AnimVmInterpreter.cs holds an identical private empty
    // stub; see the note above.
    internal static void FUN_8005ee5c(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
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
    // BLOCKED: 52 bytes, one caller and it is FUN_800578e0. Its body is
    //     sVar1 = *(short *)(DAT_8008d284 + 0x110);
    //     if (0xf < *(short *)(DAT_8008d284 + 0x110)) sVar1 = 0;
    //     return (int)sVar1;
    // — closed, and still stubbed rather than ported because DAT_8008d284 is
    // a shared pointer this file has no business being the first to declare. Recorded verbatim so
    // whichever slice owns that global can land it in one move.
    //
    // The stub returns 0, which does not block the hand-back; the original's answer depends on that
    // pointer.
    private static int FUN_8005ecf4()
    {
        return 0;
    }

    // GHIDRA: FUN_80060a4c @ 0x80060A4C (VS.EXE)
    // BLOCKED: 60 bytes. The first thing state 2 does once every one of its four conditions is
    // clear, immediately before the DAT_801FF100 write.
    private static void FUN_80060a4c()
    {
    }

    // GHIDRA: FUN_800290d0 @ 0x800290D0 (VS.EXE)
    // BLOCKED: 76 bytes, and the last call the manager ever makes: state 2 runs it after the
    // hand-back word is already stored and immediately before writing the terminal state 3.
    private static void FUN_800290d0()
    {
    }
}
