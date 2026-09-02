using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE FIGHTER'S TASK — the body that runs six times per frame.
//
// main runs task list 20, then ClearOTag, then lists 0..19, then submits. List 9 carries the battle
// manager, list 10 carries the six fighters, list 12 carries the scene. So inside one frame the
// manager has already moved when this body starts, and all six fighters have moved before the scene
// draws. This file is list 10's whole per-fighter body, and nothing else.
//
// WHAT THE WORKSPACE IS. The task node's +0x08 is the 0x240-byte workspace FUN_800512cc @ 0x800512CC
// reserved, and that workspace IS the fighter. It is PSX memory reached through PsxRam at raw
// offsets — not a C# object with fields — because the mandate keeps the original's layout. The
// offsets that BattleState.cs has already closed under an accurate name are used from there
// (FighterTaskNode, FighterBattleContext, FighterSlotIndex, FighterEntry). The rest are written as
// raw hex inline, exactly as Ghidra prints them, for two reasons: several of them are elements of a
// triple rather than standalone fields, and BattleState declares no name for them. Nothing here
// redeclares a BattleState constant under any name — the offsets this slice still needs named are
// reported upward instead.
//
// THE MAP — the phases in evaluation order, for whoever picks up the callees.
//
//   Prologue                      0x80050AE4
//   1  guard on +0x144            0x80050B14   zero -> return -1, do nothing else
//   2  position clamps            0x80050B38 .. 0x80050C84
//   3  VM suspend gate            0x80050C90   set -> FUN_80050658 @ 0x80050658
//   4  +0x138 & 0x80000000        0x80050CC4   set -> FUN_8005070c @ 0x8005070C
//   5  +0x138 & 0x04000000        0x80050CF8   set -> FUN_800507d0 + FUN_80050824
//   6  +0x134 & 0x04000000        0x80050D50   set -> FUN_80050514 @ 0x80050514
//   7  +0x134 & 0x02000000        0x80050D8C   set -> FUN_800501b8 @ 0x800501B8
//   8  targeting flags            0x80050DC0 .. 0x80050EBC
//   9  the main body              0x80050EC4 .. 0x80051170
//   10 FUN_80050a14               0x8005117C   the tail, run on the main path only
//
// Phases 3..7 are five early outs, tested in that order; each returns 0 without touching the rest.
// Phase 9's own nine steps are listed on the body below.
//
// A FRESHLY CREATED FIGHTER DOES NOTHING. FUN_800512cc writes +0x144 = 0 at 0x800512CC's tail, so
// phase 1 fails and this callback returns -1 on every frame until something else raises +0x144.
// That is the original's behaviour and is reproduced, not corrected.
//
// WHAT +0xB0 AND +0xB8 ACTUALLY ARE. BattleState names them FighterBoundsMin and
// FighterBoundsMax. Phase 2 reads them as an AXIS-ALIGNED BOX around the position triple at
// +0x114: +0xB0/+0xB2/+0xB4 are the per-axis MINIMA and +0xB8/+0xBA/+0xBC the MAXIMA, and the six
// values FUN_800512cc writes are consistent with that and with nothing else —
// (0xB1E0, 0xF448, 0xB1E0) is (-20000, -3000, -20000) against (20000, 120, 20000), and the other
// arm is (-480, -768, -480) against (480, 120, 480). The clamps below pair +0x114 with +0xB0/+0xB8,
// +0x116 with +0xB2, and +0x118 with +0xB4/+0xBC — same axis, min then max. The addresses are used
// from BattleState unchanged; the naming is reported upward rather than corrected here.
internal static class FighterTask
{
    // JUSTIFICATION: C# language bridge only
    // RELATION: FUN_800512cc @ 0x800512CC hands &LAB_80050ae4 to CreateTask at 0x80051314, which
    // stores the raw pointer in the node at +0x04. The node built by this port still stores
    // 0x80050AE4, exactly what the console holds; this call is what lets TaskSystem's dispatcher
    // turn that address back into the body below when ExecuteTaskList walks list 10.
    //
    // PrimitivePools.CreatePrimitivePools makes the same call immediately before its own CreateTask,
    // and that is the form followed here — except that the creator of a fighter, FUN_800512cc, is
    // NOT in this slice. So the registration is exposed rather than performed: whoever transliterates
    // FUN_800512cc must call this immediately before its CreateTask, or list 10 will walk six live
    // task nodes and dispatch none of them. Registration is idempotent.
    internal static void RegisterFighterTask()
    {
        TaskSystem.RegisterCallback(BattleState.FighterEntry, () => UpdateFighter());
    }

    // GHIDRA: LAB_80050ae4 @ 0x80050AE4 (VS.EXE)
    // Ghidra has no function defined at this address — FUN_800512cc references it as `&LAB_80050ae4`
    // and the decompiler serves it as `UndefinedFunction_80050ae4`. The C# name is this port's, the
    // Ghidra symbol above is what the database actually holds.
    //
    // 1732 bytes, 0x80050AE4..0x800511A7. One incoming reference, and it is not a call: FUN_800512cc
    // takes its address at 0x80051314 as CreateTask's first argument.
    //
    // The return value goes nowhere — TaskSystem's dispatcher discards it, as the original's
    // `(*(code *)*puVar1)()` does — but it is kept because the original computes it: -1 when the
    // +0x144 guard is down, 0 on every other path.
    internal static int UpdateFighter()
    {
        int uVar1;
        int iVar2;
        uint uStack_10;

        // The task workspace at node+0x08 IS the fighter, 0x240 bytes of it.
        int iVar3 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);

        // PHASE 1 @ 0x80050B14 — the guard. FUN_800512cc leaves +0x144 zero, so a fighter that has
        // just been created falls straight out here every frame.
        if (PsxRam.ReadI32(iVar3 + 0x144) == 0)
        {
            uVar1 = -1;
        }
        else
        {
            // PHASE 2 @ 0x80050B38..0x80050C84 — clamp the position triple at +0x114 into the box.
            //
            // THE Y AXIS IS NOT TREATED LIKE THE OTHER TWO, and this is the original's asymmetry,
            // reproduced under rule 12:
            //   * its upper bound is the literal 0, not +0xBA — the 120 written at +0xBA by
            //     FUN_800512cc is never read by this function;
            //   * its lower clamp against +0xB2 only applies while the state byte +0x16A is zero.
            // X and Z get the plain max-then-min pair with no state condition.
            if (0 < (short)PsxRam.ReadU16(iVar3 + 0x116))
            {
                PsxRam.WriteU16(iVar3 + 0x116, 0);
            }

            if ((sbyte)PsxRam.ReadU8(iVar3 + 0x16a) == 0
                && (short)PsxRam.ReadU16(iVar3 + 0x116)
                   < (short)PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMin + 2))
            {
                PsxRam.WriteU16(iVar3 + 0x116,
                    PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMin + 2));
            }

            if ((short)PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMax)
                < (short)PsxRam.ReadU16(iVar3 + 0x114))
            {
                PsxRam.WriteU16(iVar3 + 0x114,
                    PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMax));
            }

            if ((short)PsxRam.ReadU16(iVar3 + 0x114)
                < (short)PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMin))
            {
                PsxRam.WriteU16(iVar3 + 0x114,
                    PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMin));
            }

            if ((short)PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMax + 4)
                < (short)PsxRam.ReadU16(iVar3 + 0x118))
            {
                PsxRam.WriteU16(iVar3 + 0x118,
                    PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMax + 4));
            }

            if ((short)PsxRam.ReadU16(iVar3 + 0x118)
                < (short)PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMin + 4))
            {
                PsxRam.WriteU16(iVar3 + 0x118,
                    PsxRam.ReadU16(iVar3 + BattleState.FighterBoundsMin + 4));
            }

            // PHASE 3 @ 0x80050C90 — the animation VM's suspend gate, the same
            // `if ((DAT_800b305a & 1) == 0)` every one of the fifty-one opcode handlers opens with.
            // The symbol is AnimVm's; it is read here, not redeclared.
            if ((AnimVm.DAT_800b305a & 1) == 0)
            {
                // PHASE 4 @ 0x80050CC4 — +0x138 bit 31.
                if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x80000000) == 0)
                {
                    // PHASE 5 @ 0x80050CF8 — +0x138 bit 26.
                    if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x4000000) == 0)
                    {
                        // PHASE 6 @ 0x80050D50 — +0x134 bit 26.
                        if (((uint)PsxRam.ReadI32(iVar3 + 0x134) & 0x4000000) == 0)
                        {
                            // PHASE 7 @ 0x80050D8C — +0x134 bit 25.
                            if (((uint)PsxRam.ReadI32(iVar3 + 0x134) & 0x2000000) == 0)
                            {
                                // PHASE 8 @ 0x80050DC0..0x80050EBC — the targeting flags.
                                //
                                // The battle context at +0xF0 carries TWO halfwords, at ctx+0x14 and
                                // ctx+0x16, each holding a SLOT index; this fighter compares them
                                // against its own slot index at +0x173 — the 0/1/2 and 6/7/8 of
                                // BattleState.CtxFighterSlots, not its fighter index. A match, gated
                                // by bit 0x100000 of the context word at ctx+0x10, raises bit 28 for
                                // the first halfword or bit 29 for the second in the fighter's own
                                // +0x138. Anything else clears both (& 0xCFFFFFFF).
                                //
                                // PARTIAL: which of the two roles ctx+0x14 and ctx+0x16 name is not
                                // closed by this function. It sees only that they are slot indices
                                // and that at most one of the two bits can be up at a time.
                                if (PsxRam.ReadU16(PsxRam.ReadI32(iVar3 + BattleState.FighterBattleContext) + 0x14)
                                        == (ushort)PsxRam.ReadU8(iVar3 + BattleState.FighterSlotIndex)
                                    && ((uint)PsxRam.ReadI32(
                                            PsxRam.ReadI32(iVar3 + BattleState.FighterBattleContext) + 0x10)
                                        & 0x100000) != 0)
                                {
                                    PsxRam.WriteI32(iVar3 + 0x138,
                                        (int)((uint)PsxRam.ReadI32(iVar3 + 0x138) | 0x10000000));
                                }
                                else if (PsxRam.ReadU16(
                                             PsxRam.ReadI32(iVar3 + BattleState.FighterBattleContext) + 0x16)
                                             == (ushort)PsxRam.ReadU8(iVar3 + BattleState.FighterSlotIndex)
                                         && ((uint)PsxRam.ReadI32(
                                                 PsxRam.ReadI32(iVar3 + BattleState.FighterBattleContext) + 0x10)
                                             & 0x100000) != 0)
                                {
                                    PsxRam.WriteI32(iVar3 + 0x138,
                                        (int)((uint)PsxRam.ReadI32(iVar3 + 0x138) | 0x20000000));
                                }
                                else
                                {
                                    PsxRam.WriteI32(iVar3 + 0x138,
                                        (int)((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0xcfffffff));
                                }

                                // PHASE 9 @ 0x80050EC4..0x80051170 — the main body, nine steps in
                                // order:
                                //   9.1 0x80050EC4  FUN_8004fa8c -> +0xAC, fall back to the running
                                //                   task node; iVar2 is that node's workspace, and
                                //                   +0x18 / +0x60 are re-pointed into it
                                //   9.2 0x80050F4C  FUN_8004fbfc
                                //   9.3 0x80050F5C  FUN_80049f54 -> uStack_10, or 0, or +0x16A
                                //   9.4 0x80050FB8  one of FUN_8004cea0 / FUN_8004c198 / FUN_8004b098
                                //   9.5 0x80051038  FUN_80047688
                                //   9.6 0x80051048  the +0x134 bit-31 arm
                                //   9.7 0x800510F8  the +0x138 bit-27 gate
                                //   9.8 0x80051110  FUN_80047740 and the four that follow it
                                //   9.9 (falls through to phase 10)

                                // 9.1 — FUN_8004fa8c resolves SOME OTHER task node and parks it in
                                // +0xAC, overwriting the fighter's own node that FUN_800512cc put
                                // there. A zero result falls back to the running task, which for
                                // this callback is the fighter itself, so iVar2 == iVar3 in the
                                // ordinary case and the two stores below then reproduce exactly what
                                // FUN_800512cc already wrote at creation (+0x18 -> own +0x114).
                                // When it resolves to something else, this fighter's +0x18 points at
                                // THAT workspace's +0x114 for the rest of the frame.
                                uVar1 = FUN_8004fa8c(iVar3);
                                PsxRam.WriteI32(iVar3 + BattleState.FighterTaskNode, uVar1);
                                if (PsxRam.ReadI32(iVar3 + BattleState.FighterTaskNode) == 0)
                                {
                                    PsxRam.WriteI32(iVar3 + BattleState.FighterTaskNode,
                                        TaskSystem.g_CurrentTask);
                                }

                                iVar2 = PsxRam.ReadI32(
                                    PsxRam.ReadI32(iVar3 + BattleState.FighterTaskNode) + 8);
                                PsxRam.WriteI32(iVar3 + 0x18, iVar2 + 0x114);
                                PsxRam.WriteI32(iVar3 + 0x60, iVar2 + 0xf8);

                                // 9.2
                                FUN_8004fbfc(iVar3);

                                // 9.3 — the frame's command word. Bit 25 of +0x138 suppresses the
                                // call outright; a returned -1 falls back to the state byte +0x16A.
                                if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x2000000) == 0)
                                {
                                    uStack_10 = FUN_80049f54(iVar3);
                                }
                                else
                                {
                                    uStack_10 = 0;
                                }

                                if (uStack_10 == 0xffffffff)
                                {
                                    uStack_10 = PsxRam.ReadU8(iVar3 + 0x16a);
                                }

                                // 9.4 — three-way, on +0x138: bits 8..14 pick FUN_8004cea0; failing
                                // that, bits 0..7 or bit 17 pick FUN_8004c198; otherwise
                                // FUN_8004b098. Only the first of the three is not handed iVar2.
                                if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x7f00) == 0)
                                {
                                    if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x200ff) == 0)
                                    {
                                        FUN_8004b098(iVar3, uStack_10, iVar2);
                                    }
                                    else
                                    {
                                        FUN_8004c198(iVar3, uStack_10, iVar2);
                                    }
                                }
                                else
                                {
                                    FUN_8004cea0(iVar3, uStack_10);
                                }

                                // 9.5
                                FUN_80047688(iVar3);

                                // 9.6 — THE TEST IS ON iVar2, THE WRITE IS ON iVar3. The two are the
                                // same workspace whenever FUN_8004fa8c returned 0, and different
                                // otherwise, at which point this reads another combatant's +0x138 and
                                // +0x16A to decide whether to drop bit 20 of its OWN +0x138. That
                                // asymmetry is the original's and is reproduced verbatim.
                                if (((uint)PsxRam.ReadI32(iVar3 + 0x134) & 0x80000000) != 0)
                                {
                                    if (((uint)PsxRam.ReadI32(iVar3 + 0x134) & 0x20000000) == 0)
                                    {
                                        FUN_8004e758(iVar3, 0);
                                    }

                                    PsxRam.WriteI32(iVar3 + 0xdc, 0);
                                    if (((uint)PsxRam.ReadI32(iVar2 + 0x138) & 0x80) != 0
                                        || (sbyte)PsxRam.ReadU8(iVar2 + 0x16a) == 0x17)
                                    {
                                        PsxRam.WriteI32(iVar3 + 0x138,
                                            (int)((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0xffefffff));
                                    }
                                }

                                // 9.7 / 9.8 — bit 27 of +0x138 skips all five. The three that take a
                                // second argument are handed the fighter's own position triple at
                                // +0x114, not the one +0x18 was just re-pointed at.
                                if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x8000000) == 0)
                                {
                                    FUN_80047740(iVar3);
                                    FUN_800477ec(iVar3, iVar3 + 0x114);
                                    FUN_80047a24(iVar3, iVar3 + 0x114);
                                    FUN_80047b10(iVar3);
                                    FUN_8004fd24(iVar3, iVar3 + 0x114);
                                }

                                // PHASE 10 @ 0x8005117C — the tail. Runs on the main path only; none
                                // of the five early outs reaches it.
                                FUN_80050a14(iVar3);
                                uVar1 = 0;
                            }
                            else
                            {
                                FUN_800501b8(iVar3);
                                uVar1 = 0;
                            }
                        }
                        else
                        {
                            FUN_80050514(iVar3);
                            uVar1 = 0;
                        }
                    }
                    else
                    {
                        // 0x22 is '"' in the decompiler's rendering of the state byte; it is a state
                        // number, not a character.
                        if ((sbyte)PsxRam.ReadU8(iVar3 + 0x16a) != 0x22)
                        {
                            FUN_800507d0(iVar3);
                        }

                        FUN_80050824(iVar3);
                        uVar1 = 0;
                    }
                }
                else
                {
                    FUN_8005070c(iVar3);
                    uVar1 = 0;
                }
            }
            else
            {
                FUN_80050658(iVar3);
                uVar1 = 0;
            }
        }

        return uVar1;
    }

    // =====================================================================================
    // The twenty callees this body reaches that are NOT in this slice. Each is declared so the call
    // site above is real, in the original's order and with the arguments the original's call setup
    // actually passes — Ghidra carries no prototype for any of them, so the argument lists below
    // come from the a0/a1/a2 loads at each jal. None of them is invented, none is a convenience API,
    // and none is a substitute for the original's own entry points: they are the out-of-slice
    // functions, named exactly as Ghidra names them, with empty bodies until their own slice lands.
    //
    // Nothing else in the port transliterates any of these addresses today, so none of these stubs
    // shadows another slice's work.
    // =====================================================================================

    // GHIDRA: FUN_80050658 @ 0x80050658 (VS.EXE)
    // BLOCKED: 180 bytes. Phase 3's arm — the whole body a fighter runs while the animation VM's
    // suspend bit is up, i.e. the frozen-frame path.
    private static void FUN_80050658(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8005070c @ 0x8005070C (VS.EXE)
    // BLOCKED: 196 bytes. Phase 4's arm, on +0x138 bit 31.
    private static void FUN_8005070c(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_800507d0 @ 0x800507D0 (VS.EXE)
    // BLOCKED: 84 bytes. Phase 5's conditional half, skipped when the state byte +0x16A is 0x22.
    private static void FUN_800507d0(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_80050824 @ 0x80050824 (VS.EXE)
    // BLOCKED: 496 bytes. Phase 5's unconditional half.
    private static void FUN_80050824(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_80050514 @ 0x80050514 (VS.EXE)
    // BLOCKED: 324 bytes. Phase 6's arm, on +0x134 bit 26.
    private static void FUN_80050514(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_800501b8 @ 0x800501B8 (VS.EXE)
    // BLOCKED: 860 bytes. Phase 7's arm, on +0x134 bit 25 — the largest of the five early outs.
    private static void FUN_800501b8(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8004fa8c @ 0x8004FA8C (VS.EXE)
    // BLOCKED: 368 bytes. Step 9.1. It returns a TASK NODE, not a workspace — the caller stores it
    // in +0xAC and then dereferences +0x08 on it — and returning 0 means "no other node", at which
    // point the caller substitutes the running task. Its own slice owns whatever picks that node.
    //
    // The stub returns 0, which is the original's own no-result value and therefore leaves the
    // fallback path taking iVar2 == iVar3, the same workspace FUN_800512cc wired at creation.
    private static int FUN_8004fa8c(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_8004fbfc @ 0x8004FBFC (VS.EXE)
    // BLOCKED: 296 bytes. Step 9.2, run unconditionally between the node resolution and the command
    // word. It sits immediately after FUN_8004fa8c in the address space and immediately before
    // FUN_8004fd24, the three of them one compilation unit.
    private static void FUN_8004fbfc(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_80049f54 @ 0x80049F54 (VS.EXE)
    // BLOCKED: 388 bytes. Step 9.3 — the frame's command word for this fighter, and the one callee
    // whose RESULT the caller routes on. The caller treats 0xFFFFFFFF as "no command" and falls back
    // to the state byte +0x16A, which is the only thing this slice can say about its range.
    //
    // The stub returns 0. That is not the original's value and it does not take the -1 fallback:
    // until this is ported the trio at step 9.4 always sees command 0.
    private static uint FUN_80049f54(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_8004b098 @ 0x8004B098 (VS.EXE)
    // BLOCKED: 676 bytes. Step 9.4's default arm, taken when neither +0x138 bits 8..14 nor bits
    // 0..7/17 are set.
    private static void FUN_8004b098(int param_1, uint param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FUN_8004c198 @ 0x8004C198 (VS.EXE)
    // BLOCKED: 272 bytes. Step 9.4's arm for +0x138 & 0x200FF.
    private static void FUN_8004c198(int param_1, uint param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FUN_8004cea0 @ 0x8004CEA0 (VS.EXE)
    // BLOCKED: 604 bytes. Step 9.4's arm for +0x138 & 0x7F00, and the only one of the three that is
    // NOT handed iVar2 — it takes the fighter and the command word alone.
    private static void FUN_8004cea0(int param_1, uint param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_80047688 @ 0x80047688 (VS.EXE)
    // BLOCKED: 184 bytes. Step 9.5, unconditional, immediately after whichever of the trio ran.
    // First of the five functions this body reaches in the 0x80047xxx block.
    private static void FUN_80047688(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8004e758 @ 0x8004E758 (VS.EXE)
    // BLOCKED: 1776 bytes, the largest callee here. Step 9.6, reached only when +0x134 bit 31 is set
    // and bit 29 is clear. The literal 0 is the original's second argument at that one call site.
    private static void FUN_8004e758(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_80047740 @ 0x80047740 (VS.EXE)
    // BLOCKED: 172 bytes. Step 9.8, first of the five behind the +0x138 bit-27 gate.
    private static void FUN_80047740(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_800477ec @ 0x800477EC (VS.EXE)
    // BLOCKED: 568 bytes. Step 9.8. param_2 is the fighter's own position triple, iVar3 + 0x114.
    private static void FUN_800477ec(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_80047a24 @ 0x80047A24 (VS.EXE)
    // BLOCKED: 236 bytes. Step 9.8, same two arguments as FUN_800477ec.
    private static void FUN_80047a24(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_80047b10 @ 0x80047B10 (VS.EXE)
    // BLOCKED: 340 bytes. Step 9.8. VS_EXE/FileIo.cs already names this address in a comment as one
    // of DecompressAndLoadImage's five call sites — it passes a pointer field and a width already
    // shifted right by 2 — so this one puts an image into VRAM. FileIo carries no transliteration of
    // it, only the note, so this stub shadows nothing.
    private static void FUN_80047b10(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8004fd24 @ 0x8004FD24 (VS.EXE)
    // BLOCKED: 712 bytes. Step 9.8, last of the five, and the third to be handed iVar3 + 0x114. It
    // closes the compilation unit that FUN_8004fa8c and FUN_8004fbfc open, ending at 0x8004FFEB.
    private static void FUN_8004fd24(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_80050a14 @ 0x80050A14 (VS.EXE)
    // BLOCKED: 208 bytes. Phase 10, the tail — and it ends at 0x80050AE3, one byte below this
    // callback's own entry point, so the two are adjacent in the same compilation unit.
    private static void FUN_80050a14(int param_1)
    {
        _ = param_1;
    }
}
