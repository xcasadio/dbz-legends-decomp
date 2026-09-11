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
//   3  VM suspend gate            0x80050C90   set -> DrawFighter @ 0x80050658
//   4  +0x138 & 0x80000000        0x80050CC4   set -> FUN_8005070c @ 0x8005070C
//   5  +0x138 & 0x04000000        0x80050CF8   set -> EnterFighterKoState + UpdateOutOfPlayFighter
//   6  +0x134 & 0x04000000        0x80050D50   set -> UpdateHeldFighter @ 0x80050514
//   7  +0x134 & 0x02000000        0x80050D8C   set -> FUN_800501b8 @ 0x800501B8
//   8  targeting flags            0x80050DC0 .. 0x80050EBC
//   9  the main body              0x80050EC4 .. 0x80051170
//   10 UpdateFighterComboTimer               0x8005117C   the tail, run on the main path only
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
    // JUSTIFICATION: backend MonoGame only
    // RELATION: diagnostic probe for Validation/VsBootDiagnostic.cs; nothing in the runtime reads it.
    internal static int DiagUpdateFighterCalls;

    // JUSTIFICATION: backend MonoGame only
    // RELATION: step 9.3's own probes, read only by Validation/VsBootDiagnostic.cs. Which of
    // SelectFighterCommand's four exits a frame takes is the difference between "the pad decoder
    // is wired and idle" and "the pad decoder is never reached", and the two look identical from
    // outside. [0] pad port 1, [1] pad port 2, [2] the AI, [3] the -1 exit.
    internal static readonly int[] DiagCommandSourceCalls = new int[4];

    // The OR of every +0x138 word SelectFighterCommand has seen, and the last one. Bits
    // 0x10000000 / 0x20000000 are what mark a fighter pad-driven; if neither is ever set, no
    // amount of pad decoding can reach a fighter.
    internal static uint DiagFighterFlagsEverSeen;

    // JUSTIFICATION: C# language bridge only
    // RELATION: diagnostic probes, read only by Validation/VsBootDiagnostic.cs. Step 9.4 reaches
    // DispatchFighterNeutralCommand -- the one dispatcher that forwards an attack command word to
    // FighterCombat.FUN_8004a97c, the writer of +0x138 bit 0x08 -- only when +0x138 & 0x200FF is
    // zero. DiagRouter200ffAlways AND-accumulates that masked word across every visit, so a
    // non-zero result names exactly which bits are NEVER clear at the moment a command arrives.
    // Seeded to -1 so the first visit sets it rather than being ANDed against zero.
    internal static int DiagRouter200ffAlways = -1;

    internal static int DiagRouter200ffEverZero;

    internal static int DiagFun8004b098Calls;

    // JUSTIFICATION: C# language bridge only
    // RELATION: diagnostic probe only. Step 9.6 calls the gauge root FighterCombat.FUN_8004e758
    // only when +0x134 bit 31 is set, so the cumulative OR of that word says whether the bit is
    // ever raised at all -- the same question, one field over, that DiagFighterFlagsEverSeen
    // answers for +0x138.
    internal static uint DiagFighter134EverSeen;

    // The distinct non-negative command words step 9.3 has produced, counted by opcode. Sized to
    // cover every opcode this port has seen named (the largest is 0x2A).
    internal static readonly int[] DiagCommandWords = new int[0x40];

    // JUSTIFICATION: backend MonoGame only
    // RELATION: one counter per phase of UpdateFighter's own ladder, incremented on ENTRY to that
    // phase, so a run of counts that stops at phase N names the gate that closed. Read only by
    // Validation/VsBootDiagnostic.cs.
    internal static readonly int[] DiagPhaseEntries = new int[10];

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
        DiagUpdateFighterCalls++;

        int uVar1;
        int iVar2;
        uint uStack_10;

        // The task workspace at node+0x08 IS the fighter, 0x240 bytes of it.
        int iVar3 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);

        DiagPhaseEntries[1]++;

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
            DiagPhaseEntries[2]++;

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
                DiagPhaseEntries[3]++;

                // PHASE 4 @ 0x80050CC4 — +0x138 bit 31.
                if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x80000000) == 0)
                {
                    DiagPhaseEntries[4]++;

                    // PHASE 5 @ 0x80050CF8 — +0x138 bit 26.
                    if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x4000000) == 0)
                    {
                        DiagPhaseEntries[5]++;

                        // PHASE 6 @ 0x80050D50 — +0x134 bit 26.
                        if (((uint)PsxRam.ReadI32(iVar3 + 0x134) & 0x4000000) == 0)
                        {
                            DiagPhaseEntries[6]++;

                            // PHASE 7 @ 0x80050D8C — +0x134 bit 25.
                            if (((uint)PsxRam.ReadI32(iVar3 + 0x134) & 0x2000000) == 0)
                            {
                                DiagPhaseEntries[7]++;

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
                                // CE PARTIAL EST LEVE, ET PAS PAR CETTE FONCTION. Il disait que le
                                // role de ctx+0x14 et ctx+0x16 n'etait pas ferme, ce qui restait
                                // vrai d'ici: cette fonction ne voit que deux index de creneau dont
                                // au plus un peut lever son bit. La transliteration du gestionnaire
                                // de combat l'a ferme depuis — 0x80057064..0x8005769C ne deplace
                                // +0x14 que dans les creneaux 0..5 et +0x16 que dans 6..11. Ce sont
                                // LES DEUX CURSEURS DE CRENEAU ACTIF, UN PAR EQUIPE, et ils sont
                                // nommes CtxActingSlotTeamA / CtxActingSlotTeamB dans BattleState.
                                // Les acces bruts ci-dessous sont laisses tels quels: les reecrire
                                // toucherait une transliteration deja verifiee pour un gain nul.
                                DiagPhaseEntries[8]++;

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
                                //   9.2 0x80050F4C  UpdateFighterFacingFlag
                                //   9.3 0x80050F5C  SelectFighterCommand -> uStack_10, or 0, or +0x16A
                                //   9.4 0x80050FB8  one of DispatchFighterReactionState / DispatchFighterActionState / DispatchFighterNeutralCommand
                                //   9.5 0x80051038  StepFighterAnimAndProximity
                                //   9.6 0x80051048  the +0x134 bit-31 arm
                                //   9.7 0x800510F8  the +0x138 bit-27 gate
                                //   9.8 0x80051110  SetFighterTint and the four that follow it
                                //   9.9 (falls through to phase 10)

                                // 9.1 — FUN_8004fa8c resolves SOME OTHER task node and parks it in
                                // +0xAC, overwriting the fighter's own node that FUN_800512cc put
                                // there. A zero result falls back to the running task, which for
                                // this callback is the fighter itself, so iVar2 == iVar3 in the
                                // ordinary case and the two stores below then reproduce exactly what
                                // FUN_800512cc already wrote at creation (+0x18 -> own +0x114).
                                // When it resolves to something else, this fighter's +0x18 points at
                                // THAT workspace's +0x114 for the rest of the frame.
                                DiagPhaseEntries[9]++;

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
                                UpdateFighterFacingFlag(iVar3);

                                // 9.3 — the frame's command word. Bit 25 of +0x138 suppresses the
                                // call outright; a returned -1 falls back to the state byte +0x16A.
                                if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x2000000) == 0)
                                {
                                    uStack_10 = SelectFighterCommand(iVar3);
                                }
                                else
                                {
                                    uStack_10 = 0;
                                }

                                if (uStack_10 == 0xffffffff)
                                {
                                    uStack_10 = PsxRam.ReadU8(iVar3 + 0x16a);
                                }

                                // JUSTIFICATION: backend MonoGame only
                                // RELATION: diagnostic probe only; the tally is read by
                                // Validation/VsBootDiagnostic.cs and by nothing in the runtime.
                                if (uStack_10 < (uint)DiagCommandWords.Length)
                                {
                                    DiagCommandWords[uStack_10]++;
                                }

                                // 9.4 — three-way, on +0x138: bits 8..14 pick DispatchFighterReactionState; failing
                                // that, bits 0..7 or bit 17 pick DispatchFighterActionState; otherwise
                                // DispatchFighterNeutralCommand. Only the first of the three is not handed iVar2.
                                int diagMasked = PsxRam.ReadI32(iVar3 + 0x138) & 0x200ff;
                                DiagRouter200ffAlways &= diagMasked;
                                if (diagMasked == 0)
                                {
                                    DiagRouter200ffEverZero++;
                                }

                                if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x7f00) == 0)
                                {
                                    if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x200ff) == 0)
                                    {
                                        DiagFun8004b098Calls++;
                                        DispatchFighterNeutralCommand(iVar3, uStack_10, iVar2);
                                    }
                                    else
                                    {
                                        DispatchFighterActionState(iVar3, uStack_10, iVar2);
                                    }
                                }
                                else
                                {
                                    DispatchFighterReactionState(iVar3, uStack_10);
                                }

                                // 9.5
                                StepFighterAnimAndProximity(iVar3);

                                // 9.6 — THE TEST IS ON iVar2, THE WRITE IS ON iVar3. The two are the
                                // same workspace whenever FUN_8004fa8c returned 0, and different
                                // otherwise, at which point this reads another combatant's +0x138 and
                                // +0x16A to decide whether to drop bit 20 of its OWN +0x138. That
                                // asymmetry is the original's and is reproduced verbatim.
                                DiagFighter134EverSeen |= (uint)PsxRam.ReadI32(iVar3 + 0x134);
                                if (((uint)PsxRam.ReadI32(iVar3 + 0x134) & 0x80000000) != 0)
                                {
                                    if (((uint)PsxRam.ReadI32(iVar3 + 0x134) & 0x20000000) == 0)
                                    {
                                        FighterCombat.FUN_8004e758(iVar3, 0);
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
                                    SetFighterTint(iVar3);
                                    FighterMotion.DrawFighterSprite(iVar3, iVar3 + 0x114);
                                    FighterMotion.DrawFighterShadow(iVar3, iVar3 + 0x114);
                                    UploadFighterTexture(iVar3);
                                    FighterMotion.DriveFighterAura(iVar3, iVar3 + 0x114);
                                }

                                // PHASE 10 @ 0x8005117C — the tail. Runs on the main path only; none
                                // of the five early outs reaches it.
                                UpdateFighterComboTimer(iVar3);
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
                            UpdateHeldFighter(iVar3);
                            uVar1 = 0;
                        }
                    }
                    else
                    {
                        // 0x22 is '"' in the decompiler's rendering of the state byte; it is a state
                        // number, not a character.
                        if ((sbyte)PsxRam.ReadU8(iVar3 + 0x16a) != 0x22)
                        {
                            EnterFighterKoState(iVar3);
                        }

                        UpdateOutOfPlayFighter(iVar3);
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
                DrawFighter(iVar3);
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

    // GHIDRA: DrawFighter @ 0x80050658 (VS.EXE)
    // CERTAIN, full decompilation, 180 bytes. Phase 3's arm — the whole body a fighter runs while
    // the animation VM's suspend bit is up, i.e. the frozen-frame path. All five callees are the
    // same +0x138-bit-27-gated quintet phase 9.7/9.8 already runs in the main body above, called
    // here on the SAME two arguments (fighter, fighter's own +0x114 position triple) — reached
    // through this file's own declarations of them, one of which (DriveFighterAura) is still its own
    // BLOCKED stub below.
    private static void DrawFighter(int fighter)
    {
        if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x8000000) == 0)
        {
            SetFighterTint(fighter);
            FighterMotion.DrawFighterSprite(fighter, fighter + 0x114);
            FighterMotion.DrawFighterShadow(fighter, fighter + 0x114);
            UploadFighterTexture(fighter);
            FighterMotion.DriveFighterAura(fighter, fighter + 0x114);
        }
    }

    // GHIDRA: FUN_8005070c @ 0x8005070C (VS.EXE)
    // CERTAIN, full decompilation, 196 bytes. Phase 4's arm, on +0x138 bit 31. Opens with an
    // unconditional call to FighterCombat.FUN_80055dc0 @ 0x80055DC0 — Ghidra's own call site here
    // passes a second literal argument (0) that FUN_80055dc0's own body never reads, matching the
    // one-parameter signature FighterCombat.cs already exposes for it. The rest is the same
    // +0x138-bit-27-gated quintet DrawFighter above runs.
    private static void FUN_8005070c(int param_1)
    {
        FighterCombat.FUN_80055dc0(param_1);

        if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x8000000) == 0)
        {
            SetFighterTint(param_1);
            FighterMotion.DrawFighterSprite(param_1, param_1 + 0x114);
            FighterMotion.DrawFighterShadow(param_1, param_1 + 0x114);
            UploadFighterTexture(param_1);
            FighterMotion.DriveFighterAura(param_1, param_1 + 0x114);
        }
    }

    // GHIDRA: EnterFighterKoState @ 0x800507D0 (VS.EXE)
    // CERTAIN, full decompilation, 84 bytes. Phase 5's conditional half, skipped when the state
    // byte +0x16A is 0x22 (see the guard at the call site above). Both callees are now ported:
    // FighterSetState @ 0x80047C64 and FighterCombat.FUN_80026a28 @ 0x80026A28. The final store is
    // a plain overwrite of +0x138, not an OR — the original clobbers whatever the caller's own
    // targeting-flags step (phase 8) had just written there.
    private static void EnterFighterKoState(int fighter)
    {
        FighterCombat.FighterSetState(fighter, 0x22);
        FighterCombat.FUN_80026a28(fighter);
        PsxRam.WriteI32(fighter + 0x138, 0x4000000);
    }

    // GHIDRA: UpdateOutOfPlayFighter @ 0x80050824 (VS.EXE)
    // CERTAIN, full decompilation, 496 bytes. Phase 5's unconditional half — runs on every frame
    // phase 5 is reached, regardless of the state-byte-0x22 guard that gates EnterFighterKoState above.
    //
    // Bit 25 of +0x134 (0x2000000) opens a block that clears/sets a run of +0x134/+0x138 bits,
    // stamps the same three +0x150/+0x151/+0x152 bytes EnterFighterKoState and FighterCombat's own
    // SetFighterTint already touch, and calls FighterCombat.FUN_80026a28. Then, unconditionally,
    // StepFighterAnimAndProximity runs, followed by a +0x134-bit-31 arm — on bit 29 clear, calls
    // FighterCombat.FUN_8004e758(fighter, 0) exactly as step 9.6 of the main body does; on bit 29
    // set, that call is skipped but +0xec and +0xdc are still both zeroed (step 9.6 in the main
    // body only ever zeroes +0xdc, never +0xec — this is a distinct write, not a repeat). The tail
    // is the same +0x138-bit-27-gated quintet the other phase arms already run.
    private static void UpdateOutOfPlayFighter(int fighter)
    {
        if (((uint)PsxRam.ReadI32(fighter + 0x134) & 0x2000000) != 0)
        {
            PsxRam.WriteI32(fighter + 0x134, (int)((uint)PsxRam.ReadI32(fighter + 0x134) & 0xfdffffff));
            PsxRam.WriteI32(fighter + 0x134, (int)((uint)PsxRam.ReadI32(fighter + 0x134) | 0x4000000));
            PsxRam.WriteI32(fighter + 0x134, (int)((uint)PsxRam.ReadI32(fighter + 0x134) & 0x5fffffff));
            PsxRam.WriteI32(fighter + 0x138, (int)((uint)PsxRam.ReadI32(fighter + 0x138) & 0xe000000));
            PsxRam.WriteU8(fighter + 0x152, 0x80);
            PsxRam.WriteU8(fighter + 0x151, 0x80);
            PsxRam.WriteU8(fighter + 0x150, 0x80);
            FighterCombat.FUN_80026a28(fighter);
        }

        StepFighterAnimAndProximity(fighter);

        if (((uint)PsxRam.ReadI32(fighter + 0x134) & 0x80000000) != 0)
        {
            if (((uint)PsxRam.ReadI32(fighter + 0x134) & 0x20000000) == 0)
            {
                FighterCombat.FUN_8004e758(fighter, 0);
                
                // AND AGAIN WITH 1. Two calls, not one, and the second was dropped by a first
                // version of this port. The bytes at 0x80050940 are unambiguous:
                //     8FC40018  lw a0,0x18(s8)
                //     00002821  addu a1,zero,zero      ; param_2 = 0
                //     0C0139D6  jal 0x8004E758
                //     8FC40018  lw a0,0x18(s8)
                //     34050001  ori a1,zero,1          ; param_2 = 1
                //     0C0139D6  jal 0x8004E758
                // The two are NOT redundant: FUN_8004e758 indexes its attack record at
                // fighter + param_2 * 0x10 + 0xDC, so 0 and 1 resolve the fighter's two SEPARATE
                // record slots at +0xDC and +0xEC. That is also why this function zeroes BOTH of
                // them afterwards while its sibling FUN_800501b8 -- which genuinely makes one call
                // -- zeroes only +0xDC. The asymmetry between the two was the tell.
                FighterCombat.FUN_8004e758(fighter, 1);
            }

            PsxRam.WriteI32(fighter + 0xec, 0);
            PsxRam.WriteI32(fighter + 0xdc, 0);
        }

        if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x8000000) == 0)
        {
            SetFighterTint(fighter);
            FighterMotion.DrawFighterSprite(fighter, fighter + 0x114);
            FighterMotion.DrawFighterShadow(fighter, fighter + 0x114);
            UploadFighterTexture(fighter);
            FighterMotion.DriveFighterAura(fighter, fighter + 0x114);
        }
    }

    // GHIDRA: UpdateHeldFighter @ 0x80050514 (VS.EXE)
    // CERTAIN, full decompilation, 324 bytes. Phase 6's arm, on +0x134 bit 26. Re-derives the
    // same +0x18/+0x60 re-point step 9.1 above performs — through FighterTaskNode (+0xAC) rather
    // than FUN_8004fa8c's own resolution — then, only when the frame counter at +4 is zero,
    // re-stamps the CURRENT state via FighterSetState (the same "re-stamp with current state"
    // pattern FighterCombat's own FighterSetState header note documents for state 2/10). The tail
    // is StepFighterAnimAndProximity followed by the same +0x138-bit-27-gated quintet, and a reset of the +0x22A
    // frame counter FUN_8004fa8c (step 9.1) itself owns and decrements.
    private static void UpdateHeldFighter(int fighter)
    {
        int iVar1 = PsxRam.ReadI32(PsxRam.ReadI32(fighter + BattleState.FighterTaskNode) + 8);
        PsxRam.WriteI32(fighter + 0x18, iVar1 + 0x114);
        PsxRam.WriteI32(fighter + 0x60, iVar1 + 0xf8);

        if ((short)PsxRam.ReadU16(fighter + 4) == 0)
        {
            FighterCombat.FighterSetState(fighter, (ushort)PsxRam.ReadU8(fighter + 0x16a));
        }

        StepFighterAnimAndProximity(fighter);

        if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x8000000) == 0)
        {
            SetFighterTint(fighter);
            FighterMotion.DrawFighterSprite(fighter, fighter + 0x114);
            FighterMotion.DrawFighterShadow(fighter, fighter + 0x114);
            UploadFighterTexture(fighter);
            FighterMotion.DriveFighterAura(fighter, fighter + 0x114);
        }

        PsxRam.WriteU16(fighter + 0x22a, 0);
    }

    // GHIDRA: FUN_800501b8 @ 0x800501B8 (VS.EXE)
    // CERTAIN, full decompilation, 860 bytes. Phase 7's arm, on +0x134 bit 25 — the largest of the
    // five early outs.
    //
    // The entry block runs when the state byte +0x16A is 0, OR when the frame counter at +4 is 0
    // AND the flag at +6 is non-zero — Ghidra's own printed condition, kept exactly rather than
    // simplified. Inside it, +0x134 has bit 25 cleared, bit 26 set, then is ANDed with 0x07F8FFFF
    // — which clears bits 31/30/29/28 along with bit 25, among others; NOTE THIS FOR THE GATE
    // FINDING: this path CLEARS +0x134 bit 31, it does not set it. The same three +0x150..+0x152
    // bytes are stamped 0x80, then FighterCombat.FUN_8004a638(fighter, 0) and
    // FighterCombat.FUN_80026a28(fighter) run.
    //
    // FighterCombat.FUN_8004ffec then runs unconditionally (its own uint result is discarded here,
    // exactly as the original computes but never consumes it). Four more single-state arms follow,
    // each testing the SAME state byte +0x16A against one fixed value and, on a match, clearing a
    // small per-state flag field, masking +0x138 down to 0x0E000000, and calling
    // FighterCombat.FUN_8004a638(fighter, 0) again — these are independent ifs, not an if/else
    // chain, matching Ghidra's own four separate branches. The tail is the same +0x134-bit-31 arm
    // and +0x138-bit-27-gated quintet the other phase arms already run, except this one zeroes only
    // +0xdc (not +0xec, unlike UpdateOutOfPlayFighter above) — that asymmetry is the original's.
    //
    // The four state values are left as raw hex with the char Ghidra prints them as, per this
    // file's own precedent at EnterFighterKoState's 0x22 guard: 0x21 '!', 0x20 ' ', 0x1c, 0x2a '*' — state
    // numbers, not characters.
    private static void FUN_800501b8(int param_1)
    {
        if ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 0
            || ((short)PsxRam.ReadU16(param_1 + 4) == 0 && (short)PsxRam.ReadU16(param_1 + 6) != 0))
        {
            PsxRam.WriteI32(param_1 + 0x134, (int)((uint)PsxRam.ReadI32(param_1 + 0x134) & 0xfdffffff));
            PsxRam.WriteI32(param_1 + 0x134, (int)((uint)PsxRam.ReadI32(param_1 + 0x134) | 0x4000000));
            PsxRam.WriteI32(param_1 + 0x134, (int)((uint)PsxRam.ReadI32(param_1 + 0x134) & 0x7f8ffff));
            PsxRam.WriteI32(param_1 + 0x138, (int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xe000000));
            PsxRam.WriteU8(param_1 + 0x152, 0x80);
            PsxRam.WriteU8(param_1 + 0x151, 0x80);
            PsxRam.WriteU8(param_1 + 0x150, 0x80);
            FighterCombat.FUN_8004a638(param_1, 0);
            FighterCombat.FUN_80026a28(param_1);
        }

        FighterCombat.FUN_8004ffec(param_1);

        // 0x21 '!'
        if ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 0x21)
        {
            PsxRam.WriteU8(param_1 + 0x224, 0);
            PsxRam.WriteI32(param_1 + 0x138, (int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xe000000));
            FighterCombat.FUN_8004a638(param_1, 0);
        }

        // 0x20 ' '
        if ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 0x20)
        {
            PsxRam.WriteU16(param_1 + 0x15e, 0);
            PsxRam.WriteI32(param_1 + 0x138, (int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xe000000));
            FighterCombat.FUN_8004a638(param_1, 0);
        }

        // 0x1c
        if ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 0x1c)
        {
            PsxRam.WriteU8(param_1 + 0x228, 0);
            PsxRam.WriteI32(param_1 + 0x138, (int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xe000000));
            FighterCombat.FUN_8004a638(param_1, 0);
        }

        // 0x2a '*'
        if ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) == 0x2a)
        {
            PsxRam.WriteI32(param_1 + 0x138, (int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xe000000));
            FighterCombat.FUN_8004a638(param_1, 0);
        }

        StepFighterAnimAndProximity(param_1);

        if (((uint)PsxRam.ReadI32(param_1 + 0x134) & 0x80000000) != 0)
        {
            if (((uint)PsxRam.ReadI32(param_1 + 0x134) & 0x20000000) == 0)
            {
                FighterCombat.FUN_8004e758(param_1, 0);
            }

            PsxRam.WriteI32(param_1 + 0xdc, 0);
        }

        if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x8000000) == 0)
        {
            SetFighterTint(param_1);
            FighterMotion.DrawFighterSprite(param_1, param_1 + 0x114);
            FighterMotion.DrawFighterShadow(param_1, param_1 + 0x114);
            UploadFighterTexture(param_1);
            FighterMotion.DriveFighterAura(param_1, param_1 + 0x114);
        }
    }

    // GHIDRA: FUN_8004fa8c @ 0x8004FA8C (VS.EXE)
    // CERTAIN, full decompilation, 368 bytes, 0x8004FA8C..0x8004FBFB. Step 9.1. It returns a TASK
    // NODE, not a workspace — the caller stores it in +0xAC and then dereferences +0x08 on it —
    // and returning 0 means "no other node", at which point the caller substitutes the running
    // task.
    //
    // Verified instruction-by-instruction against mcp__pcsx-redux__pcsx_analyze_function @
    // 0x8004fa8c, because Ghidra's own decompilation groups assembly fragments onto the wrong
    // source lines here (a rendering quirk of this MCP session, not a fact about the binary):
    //   * +0x22a's guard compare ("0 < *(short*)...") is `lh` (a genuine signed 16-bit load, no
    //     extra truncation needed — unlike the byte case UpdateFighterComboTimer below, a halfword sign-load
    //     is already the full signed value);
    //   * +0x22a's reload for the decrement is `lhu`, but since the result is stored straight
    //     back with `sh` the signedness of that particular load cannot change the outcome;
    //   * +0x173 (FighterSlotIndex) is `lbu`, unsigned, matching every other read of it in this
    //     file;
    //   * the intermediate index at ctx+slotIndex*0x14+0x15C0 is `lh`, SIGNED — it is used as a
    //     scaled array index into ctx+0x1520, so its sign matters for the resulting address;
    //   * the final fetched value is `lw`, a plain 32-bit read.
    //
    // +0xF0/+0x173/+0xAC are BattleState's FighterBattleContext/FighterSlotIndex/FighterTaskNode.
    // The two tables this reads inside the battle context — the per-slot array at ctx+0x15C0 and
    // the array of values at ctx+0x1520 the first array indexes into — are not named by
    // BattleState or anywhere else in this port; they are left as raw offsets and reported
    // upward rather than guessed at.
    private static int FUN_8004fa8c(int param_1)
    {
        int iVar1;

        if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x30000000) == 0)
        {
            if (0 < (short)PsxRam.ReadU16(param_1 + 0x22a))
            {
                PsxRam.WriteU16(param_1 + 0x22a,
                    (ushort)((short)PsxRam.ReadU16(param_1 + 0x22a) - 1));
                return PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode);
            }
        }
        else
        {
            PsxRam.WriteU16(param_1 + 0x22a, 0);
        }

        if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x27fff) == 0)
        {
            iVar1 = PsxRam.ReadI32(
                (short)PsxRam.ReadU16(
                    PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                    + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * 0x14
                    + 0x15c0) * 4
                + PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext) + 0x1520);

            if (iVar1 != PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode))
            {
                PsxRam.WriteU16(param_1 + 0x22a, 0x3c);
            }
        }
        else
        {
            iVar1 = PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode);
        }

        return iVar1;
    }

    // GHIDRA: UpdateFighterFacingFlag @ 0x8004FBFC (VS.EXE)
    // 296 bytes, 0x8004FBFC..0x8004FD23. Step 9.2, run unconditionally between the node resolution
    // and the command word. It sits immediately after FUN_8004fa8c in the address space and
    // immediately before DriveFighterAura, the three of them one compilation unit.
    //
    // CERTAIN which bytes move, from two independent readings (Ghidra's decompilation and
    // mcp__pcsx-redux__pcsx_analyze_function @ 0x8004fbfc, which agree): Ghidra renders the first
    // half as a wall of shifts and masks —
    //   auStack_40._0_4_ = (*(int *)((param_1+0x117U)-uVar2) << (3-uVar2)*8 | ...) & ... | ...
    // — but the raw instructions underneath are a plain `lwl`/`lwr` pair loading from param_1+0x114
    // followed by `swl`/`swr` storing into a local buffer, twice over (0x8004FC1C..0x8004FC38 for
    // param_1, 0x8004FC58..0x8004FC74 for the second workspace below). `lwl`/`lwr` are MIPS's
    // unaligned-load idiom: by construction they reconstruct the exact 4 source bytes regardless
    // of alignment, so this — and its `swl`/`swr` counterpart on the write side — is PROVABLY an
    // 8-byte byte-for-byte copy, not an approximation of one. What is copied is the fighter's own
    // position triple at +0x114 (the same vx/vy/vz phase 2 clamps and the caller's own +0x18
    // already use) into an SVECTOR-shaped scratch buffer, and the OTHER workspace's +0x114 into a
    // second one. That other workspace is FighterTaskNode re-dereferenced independently of the
    // caller's own copy of the same value: `PsxRam[PsxRam[param_1+0xAC]+8]`, exactly step 9.1's
    // own iVar2 computation repeated rather than passed in.
    //
    // Both SVECTORs are then rotated through RotTrans — real GHIDRA name at VS.EXE's own
    // 0x80077D7C, confirmed by decompiling THAT address and matching it instruction-for-instruction
    // (gte_ldv0/copFunction 0x480012/gte_stlvnl/gte_stFLAG) against the RotTrans already in
    // PsxSdkMonogame/LibGte.cs, which was closed against a different EXE's copy at a different
    // address — same 40-byte routine, reused here rather than re-derived. RotTrans's SVECTOR.pad is
    // never read by gte_ldv0 (see LibGte.LdV0), so it is left unset, matching every other call site
    // in this port that builds an SVECTOR from raw fields (e.g. TITLE_EXE/SpriteRenderer.cs).
    //
    // The two rotated VECTORs' vx are compared — "the other" minus "this" — to set or clear bit 30
    // of +0x138. The matrix RotTrans rotates through is whatever the GTE currently holds; this
    // function neither loads one nor is told which, so the result depends on an earlier call this
    // slice does not own.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: RotTrans's third argument is `long *flag`, the original's `&local_a8.pad`-style
    // output sink that TITLE_EXE/SpriteRenderer.cs already documents for this same callee — C#
    // cannot take the address of a field, so each call here gets its own throwaway one-element
    // array. Nothing reads either array afterward, matching the original: neither call site's
    // `alStack_10` is read again once both RotTrans calls return.
    private static void UpdateFighterFacingFlag(int fighter)
    {
        LibGte.SVECTOR svec1 = new();
        svec1.vx = (short)PsxRam.ReadU16(fighter + 0x114);
        svec1.vy = (short)PsxRam.ReadU16(fighter + 0x116);
        svec1.vz = (short)PsxRam.ReadU16(fighter + 0x118);

        int iVar3 = PsxRam.ReadI32(PsxRam.ReadI32(fighter + BattleState.FighterTaskNode) + 8);

        LibGte.SVECTOR svec2 = new();
        svec2.vx = (short)PsxRam.ReadU16(iVar3 + 0x114);
        svec2.vy = (short)PsxRam.ReadU16(iVar3 + 0x116);
        svec2.vz = (short)PsxRam.ReadU16(iVar3 + 0x118);

        LibGte.VECTOR local_30 = new();
        int[] flag1 = new int[1];
        LibGte.RotTrans(svec1, local_30, flag1);

        LibGte.VECTOR local_20 = new();
        int[] flag2 = new int[1];
        LibGte.RotTrans(svec2, local_20, flag2);

        if (local_20.vx - local_30.vx < 0)
        {
            PsxRam.WriteI32(fighter + 0x138, (int)((uint)PsxRam.ReadI32(fighter + 0x138) & 0xbfffffff));
        }
        else
        {
            PsxRam.WriteI32(fighter + 0x138, (int)((uint)PsxRam.ReadI32(fighter + 0x138) | 0x40000000));
        }
    }

    // GHIDRA: SelectFighterCommand @ 0x80049F54 (VS.EXE)
    // 388 bytes. Step 9.3 — the frame's command word for this fighter, and the one callee whose
    // RESULT the caller routes on. The caller treats 0xFFFFFFFF as "no command" and falls back to
    // the state byte +0x16A.
    //
    // IT IS A TWO-WAY SWITCH: PAD OR AI. DAT_801FF100 is the handover word SELECT.EXE writes, held
    // here as short index 0x80 of SharedHighRam.SHORT_ARRAY_801ff000 — the same spelling
    // BattleManager.cs, FighterSetup.cs and SELECT_EXE/CharacterSelect.cs already use, so nothing
    // new is declared for it. Its three in-values route as follows, and +0x138 bits 0x10000000 /
    // 0x20000000 are what mark a fighter as driven by pad port 1 / port 2:
    //
    //   1  bit 0x10000000 set -> ReadFighterPadCommand(fighter, 0); otherwise the AI.
    //   0  bit 0x10000000 set -> ReadFighterPadCommand(fighter, 0);
    //      else bit 0x20000000 set -> ReadFighterPadCommand(fighter, 1);
    //      else the AI.
    //   2  the AI, unconditionally.
    //   anything else -> -1, i.e. the caller's fall back to +0x16A.
    //
    // Note the shape of the original's `else` ladder, reproduced rather than flattened: the
    // `DAT_801FF100 < 2` arm covers the value 0 and then FALLS THROUGH to `uVar1 = 0xffffffff` for
    // any other value below 2, and the `== 2` arm returns before reaching it. Written the other way
    // round, values below 2 that are not 0 would take the wrong exit.
    internal static uint SelectFighterCommand(int fighter)
    {
        uint uVar1;

        short handover = SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex];
        DiagFighterFlagsEverSeen |= (uint)PsxRam.ReadI32(fighter + 0x138);

        if (handover == 1)
        {
            if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x10000000) == 0)
            {
                DiagCommandSourceCalls[2]++;
                uVar1 = (uint)FighterAi.FUN_80023890(fighter);
            }
            else
            {
                DiagCommandSourceCalls[0]++;
                uVar1 = (uint)FighterInput.ReadFighterPadCommand(fighter, 0);
            }
        }
        else
        {
            if (handover < 2)
            {
                if (handover == 0)
                {
                    if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x10000000) != 0)
                    {
                        DiagCommandSourceCalls[0]++;
                uVar1 = (uint)FighterInput.ReadFighterPadCommand(fighter, 0);
                        return uVar1;
                    }

                    if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x20000000) != 0)
                    {
                        DiagCommandSourceCalls[1]++;
                        uVar1 = (uint)FighterInput.ReadFighterPadCommand(fighter, 1);
                        return uVar1;
                    }

                    DiagCommandSourceCalls[2]++;
                uVar1 = (uint)FighterAi.FUN_80023890(fighter);
                    return uVar1;
                }
            }
            else if (handover == 2)
            {
                DiagCommandSourceCalls[2]++;
                uVar1 = (uint)FighterAi.FUN_80023890(fighter);
                return uVar1;
            }

            DiagCommandSourceCalls[3]++;
            uVar1 = 0xffffffff;
        }

        return uVar1;
    }

    // GHIDRA: DAT_801ff100 @ 0x801FF100 (VS.EXE)
    // Not a declaration — an INDEX, exactly as BattleManager.cs's own constant of the same name
    // documents. 0x801FF100 - 0x801FF000 = 0x100 bytes = short index 0x80 of
    // SharedHighRam.SHORT_ARRAY_801ff000. Held per file because the two constants are private to
    // their own class; the STORAGE is single, in SharedHighRam, which is what the duplicate-symbol
    // rule cares about.
    private const int Dat801ff100ShortIndex = 0x80;

    // GHIDRA: FUN_80023890 @ 0x80023890 (VS.EXE)
    // NO LONGER DECLARED HERE. THE CPU CONTROLLER IS CLOSED, in VS_EXE/FighterAi.cs, together with
    // its nine callees -- 5096 bytes of root plus about 5600 of subtree. SelectFighterCommand below
    // reaches it by qualified name; while a stub for the same address sat in THIS file, C# bound
    // these three call sites to the stub and the real body would have been dead code.

    // GHIDRA: DispatchFighterNeutralCommand @ 0x8004B098 (VS.EXE)
    // CERTAIN, full decompilation, 676 bytes. Step 9.4's default arm, taken when neither +0x138
    // bits 8..14 nor bits 0..7/17 are set. param_3 is iVar2 from the caller's step 9.1 -- the
    // OTHER fighter workspace step 9.1 may have re-pointed to, or this fighter itself in the
    // ordinary case where FUN_8004fa8c returned 0.
    //
    // THREE-WAY TOP LEVEL:
    //   1. This fighter's own battle-context Ki gauge (BattleState.CtxKiGauge, read through its
    //      own slot, the SAME "ctx + slot*CtxSlotRecordStride + CtxKiGauge" address
    //      FighterCombat.FUN_8004e758 already reads for the identical "< 400" gate) below 400
    //      forces state 0x20 (FighterAction.FUN_8004ad0c), clears +0x138 bit 0x40000, and stamps
    //      +0x15e = 0x3c -- an out-of-Ki lockout timer, not named further here.
    //   2. Failing that, a four-way OR falls back to FighterCombat.FUN_8004a638(param_1, 0) when:
    //      param_3's own +0x138 bits 5..7 (0xe0) are all clear, OR param_3's own FighterTaskNode
    //      (+0xac) is not the currently running task (TaskSystem.g_CurrentTask, VS.EXE's
    //      DAT_8008d16c -- the SAME "+0xac == g_CurrentTask" test FUN_8004e758 already makes, here
    //      negated), OR this fighter's own +0x138 bit 0x80000 is set, OR this fighter's own state
    //      byte at +0x16b is 0x1c.
    //   3. Otherwise: +0x138 bit 0x8000 routes to FighterAction.FUN_8004b024; failing that, bit
    //      0x800000 routes to FighterAction.FUN_8004ad80; failing THAT, param_2 (the frame's
    //      command word) picks one of five arms: 0x26/0x27/0x28 -> FighterCombat.FUN_8004a97c;
    //      0x21 -> FighterAction.FUN_8004aa44 then FighterAction.FUN_8004bf50; 0x13/0x14 ->
    //      FighterAction.FUN_8004a910; 0x1c -> FighterCombat.FUN_8004aa9c then
    //      FighterAction.FUN_8004ad80; anything else -> FighterCombat.FUN_8004a638(param_1,
    //      param_2).
    //
    // +0x16b and +0x15e are not named anywhere else in this port and are left as raw offsets.
    private static void DispatchFighterNeutralCommand(int fighter, uint command, int opponent)
    {
        if ((short)PsxRam.ReadU16(
                PsxRam.ReadI32(fighter + BattleState.FighterBattleContext)
                    + PsxRam.ReadU8(fighter + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
                    + BattleState.CtxKiGauge)
            < 400)
        {
            FighterAction.FUN_8004ad0c(fighter);
            PsxRam.WriteI32(fighter + 0x138, PsxRam.ReadI32(fighter + 0x138) & unchecked((int)0xfffbffff));
            PsxRam.WriteU16(fighter + 0x15e, 0x3c);
        }
        // CORRECTED: THESE TWO ARMS WERE THE WRONG WAY ROUND, and this is step 9.4's default
        // dispatch -- the arm taken on nearly every ordinary frame -- so the error was not a corner
        // case. The branches settle it:
        //     0x8004B18C  1440000D  bne v0,zero,+0xD   ; condition TRUE -> jump to the dispatch
        //     0x8004B1A4  10620007  beq v1,v0,+7       ; +0x16B == 0x1C -> jump to the dispatch
        //     0x8004B1B4  0C01298E  jal 0x8004A638     ; reached ONLY by falling through
        //     0x8004B1BC  08012CC9  j   0x8004B324     ; and then skipping the dispatch entirely
        // So ANY of the four conditions selects the DISPATCH, and the bare FUN_8004a638(fighter, 0)
        // runs only when none of them holds. The first version had it exactly inverted, and its
        // header comment described the inverted version, so the misreading came before the code.
        //
        // The test is negated here rather than the two bodies being moved: the dispatch block below
        // is long, and inverting the condition changes the one thing that was wrong.
        else if (!((((uint)PsxRam.ReadI32(opponent + 0x138) & 0xe0) == 0)
            || (PsxRam.ReadI32(opponent + BattleState.FighterTaskNode) != TaskSystem.g_CurrentTask)
            || (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x80000) != 0)
            || ((sbyte)PsxRam.ReadU8(fighter + 0x16b) == 0x1c)))
        {
            FighterCombat.FUN_8004a638(fighter, 0);
        }
        else
        {
            if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x8000) == 0)
            {
                if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x800000) == 0)
                {
                    if (command == 0x26 || command == 0x27 || command == 0x28)
                    {
                        FighterCombat.FUN_8004a97c(fighter, (int)command);
                    }
                    else if (command == 0x21)
                    {
                        FighterAction.FUN_8004aa44(fighter);
                        FighterAction.FUN_8004bf50(fighter);
                    }
                    else if (command == 0x13 || command == 0x14)
                    {
                        FighterAction.FUN_8004a910(fighter, (int)command);
                    }
                    else if (command == 0x1c)
                    {
                        FighterCombat.FUN_8004aa9c(fighter);
                        FighterAction.FUN_8004ad80(fighter);
                    }
                    else
                    {
                        FighterCombat.FUN_8004a638(fighter, (int)command);
                    }
                }
                else
                {
                    FighterAction.FUN_8004ad80(fighter);
                }
            }
            else
            {
                FighterAction.FUN_8004b024(fighter);
            }
        }
    }

    // GHIDRA: DispatchFighterActionState @ 0x8004C198 (VS.EXE)
    // CERTAIN, full decompilation, 272 bytes. Step 9.4's arm for +0x138 & 0x200FF -- a five-way
    // dispatch purely on +0x138 bits 0x20/8/0x10/6, all five arms already ported in
    // FighterAction.cs.
    private static void DispatchFighterActionState(int fighter, uint command, int opponent)
    {
        if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x20) == 0)
        {
            if (((uint)PsxRam.ReadI32(fighter + 0x138) & 8) == 0)
            {
                if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x10) == 0)
                {
                    if (((uint)PsxRam.ReadI32(fighter + 0x138) & 6) == 0)
                    {
                        FighterAction.FUN_8004b8a0(fighter, (int)command, opponent);
                    }
                    else
                    {
                        FighterAction.FUN_8004bf50(fighter);
                    }
                }
                else
                {
                    FighterAction.FUN_8004bd3c(fighter, command);
                }
            }
            else
            {
                FighterAction.FUN_8004bb70(fighter, (int)command);
            }
        }
        else
        {
            FighterAction.FUN_8004b9cc(fighter);
        }
    }

    // GHIDRA: DispatchFighterReactionState @ 0x8004CEA0 (VS.EXE)
    // CERTAIN, full decompilation, 604 bytes. Step 9.4's arm for +0x138 & 0x7F00, and the only one
    // of the three that is NOT handed iVar2 -- it takes the fighter and the command word alone.
    //
    // TWO PARTS, both unconditional relative to each other -- the first never skips the second.
    //
    // PART 1 -- gated on +0x138 bits 28/29 (0x30000000), the SAME pair phase 8 above sets when
    // this fighter's own slot matches one of the battle context's two targeting cursors
    // (CtxActingSlotTeamA / CtxActingSlotTeamB). When either is set, this scans
    // BattleState.CtxFighterSlots (ctx+0x1520) over this fighter's OWN half of the twelve-wide
    // array -- slots 0..5 when this fighter's own slot (+0x173) is < 6, slots 6..11 otherwise.
    // CtxFighterSlots' own header note already closes that team A occupies slots 0,1,2 and team B
    // occupies 6,7,8 within that same array, so this walks the fighter's own team's half, not the
    // opposing team's. Each filled slot entry is read as a task-node pointer, exactly like the
    // caller's own step 9.1 (+8 gives the task's workspace pointer), and that workspace's own
    // +0x138 bit 0x20000 is tested -- the SAME bit the caller's step 9.4 dispatch already uses to
    // route to this function's sibling DispatchFighterActionState. If NO scanned slot has that bit set,
    // FighterCombat.FUN_8004c3e0(param_1) runs; its bool return is discarded here exactly as the
    // original discards it.
    //
    // PART 2 -- unconditional, a five-way dispatch on +0x138 bits 0x4000 / 0x3800 / 0x400 / 0x200,
    // all five arms already ported in FighterCombat.cs.
    private static void DispatchFighterReactionState(int fighter, uint command)
    {
        if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x30000000) != 0)
        {
            bool bVar1 = false;
            int local_18;
            int iVar2;

            if (PsxRam.ReadU8(fighter + BattleState.FighterSlotIndex) < 6)
            {
                local_18 = 0;
                iVar2 = local_18;
            }
            else
            {
                local_18 = 6;
                iVar2 = local_18;
            }

            for (; local_18 < iVar2 + 6; local_18 = local_18 + 1)
            {
                int slotPtr = PsxRam.ReadI32(
                    PsxRam.ReadI32(fighter + BattleState.FighterBattleContext)
                        + BattleState.CtxFighterSlots + local_18 * 4);

                if (slotPtr != 0
                    && ((uint)PsxRam.ReadI32(PsxRam.ReadI32(slotPtr + 8) + 0x138) & 0x20000) != 0)
                {
                    bVar1 = true;
                }
            }

            if (!bVar1)
            {
                FighterCombat.FUN_8004c3e0(fighter);
            }
        }

        if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x4000) == 0)
        {
            if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x3800) == 0)
            {
                if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x400) == 0)
                {
                    if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x200) == 0)
                    {
                        FighterCombat.FUN_8004c9cc(fighter);
                    }
                    else
                    {
                        FighterCombat.FUN_8004ca54(fighter, (int)command);
                    }
                }
                else
                {
                    FighterCombat.FUN_8004cb24(fighter, (int)command);
                }
            }
            else
            {
                FighterCombat.FUN_8004cc64(fighter, (int)command);
            }
        }
        else
        {
            FighterCombat.FUN_8004cd84(fighter);
        }
    }

    // GHIDRA: StepFighterAnimAndProximity @ 0x80047688 (VS.EXE)
    // CERTAIN, full decompilation, 184 bytes. Step 9.5, unconditional, immediately after whichever
    // of the trio ran. First of the five functions this body reaches in the 0x80047xxx block, and
    // also called by the four early-out arms above (UpdateHeldFighter/824/501b8) and by FUN_8005070c —
    // it clears +0x134 bit 31 and then pushes/pops the fighter's own +0xf8 sub-record on the
    // DAT_80083cb4 chain FighterCombat's own FUN_80045998/FUN_80045a38 already document, and runs
    // the keyframe-stream scanner FighterCombat.FUN_800539d0.
    //
    // &DAT_80083cb4 and &DAT_80101ba4 are ADDRESSES the callees use as opaque keys, not values —
    // VS_EXE/AnimCmdEffects.cs already names the first privately as `DAT_80083cb4Address` for an
    // unrelated caller of the same chain, but that constant is private to that file and this
    // project's duplicate-symbol rule forbids redeclaring the same Ghidra address under a second
    // name, so both addresses are used here as raw literals with this comment rather than through
    // a shared constant.
    private static void StepFighterAnimAndProximity(int fighter)
    {
        PsxRam.WriteI32(fighter + 0x134, (int)((uint)PsxRam.ReadI32(fighter + 0x134) & 0x7fffffff));

        // &DAT_80083cb4 -- the list-head record FighterCombat.FUN_80045998/FUN_80045a38 use.
        FighterCombat.FUN_80045a38(unchecked((int)0x80083cb4), fighter + 0xf8);
        FighterCombat.FUN_800539d0(fighter);

        // &DAT_80083cb4 (list head) and &DAT_80101ba4 (opaque payload address).
        FighterCombat.FUN_80045998(unchecked((int)0x80083cb4), fighter + 0xf8, unchecked((int)0x80101ba4));
        FighterCombat.FUN_80045814(fighter + 0xf8);
    }

    // GHIDRA: FUN_8004e758 @ 0x8004E758 (VS.EXE)
    // MOVED, NOT DELETED. Its real 1776-byte body now lives in VS_EXE/FighterCombat.cs, with the
    // rest of the combat-resolution family it belongs to. This file kept an EMPTY private stub for
    // the same address, and because C# resolves an unqualified call to the enclosing class first,
    // step 9.6's call below was binding to that no-op rather than to the real body -- one Ghidra
    // address with two declarations, which this port treats as a defect, and the more dangerous
    // kind: it compiles, it runs, and the work silently does not happen.
    // The declaration is removed and the call site qualified. See FighterCombat.FUN_8004e758.

    // GHIDRA: SetFighterTint @ 0x80047740 (VS.EXE)
    // CERTAIN, full decompilation, 172 bytes. Step 9.8, first of the five behind the +0x138
    // bit-27 gate. Sets three consecutive bytes at +0x150/+0x151/+0x152 to one of two fixed
    // values, gated on +0x134 bit 26 and, inside that, +0x138 bit 18. No callee, no loop, no
    // open question — the three fields themselves are not named anywhere else in this port, so
    // they are left as raw offsets.
    private static void SetFighterTint(int fighter)
    {
        if (((uint)PsxRam.ReadI32(fighter + 0x134) & 0x4000000) == 0)
        {
            if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x40000) == 0)
            {
                PsxRam.WriteU8(fighter + 0x152, 0x80);
                PsxRam.WriteU8(fighter + 0x151, 0x80);
                PsxRam.WriteU8(fighter + 0x150, 0x80);
            }
            else
            {
                PsxRam.WriteU8(fighter + 0x150, 0xff);
                PsxRam.WriteU8(fighter + 0x152, 0xff);
                PsxRam.WriteU8(fighter + 0x151, 0xff);
            }
        }
    }

    // GHIDRA: DrawFighterSprite @ 0x800477EC (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/FighterMotion.cs. The call sites in this file
    // reach it by qualified name: an empty stub in the enclosing class silently beats a real
    // body elsewhere, which is what check_function_addresses.py exists to catch.

    // GHIDRA: DrawFighterShadow @ 0x80047A24 (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/FighterMotion.cs. The call sites in this file
    // reach it by qualified name: an empty stub in the enclosing class silently beats a real
    // body elsewhere, which is what check_function_addresses.py exists to catch.

    // GHIDRA: UploadFighterTexture @ 0x80047B10 (VS.EXE)
    // CERTAIN, full decompilation, 340 bytes, 0x80047B10..0x80047C63. Step 9.8. VS_EXE/FileIo.cs
    // already named this address in a comment as one of FileIo.DecompressAndLoadImage's five call
    // sites — "a pointer field and a width already shifted right by 2" — and that call is what
    // this function makes; FileIo carried no transliteration of the caller itself before this.
    //
    // Reloads a texture only when +0x94's pointer has changed since the last reload cached at
    // +0x14c AND +0x13c is positive (signed, `blez`-gated in the disassembly). The width/height
    // pair comes from a small record at +0x98: a packed field at the record's +0xa (read `lhu`,
    // shifted right 9 then masked to bits 3..6) is used directly when non-zero; when it IS zero,
    // the record's own +0xc/+0xe halfwords are used instead (also `lhu`, both unsigned). Either
    // way the width component is halved twice more (>>2) before the call — this is the "width
    // already shifted right by 2" FileIo's own comment already flagged.
    //
    // Ghidra prints the x/y loads at +0x156/+0x158 as `ushort *` but the actual instructions are
    // `lh` (signed); the sign extension is invisible here because the value only ever feeds
    // DecompressAndLoadImage's `ushort` parameter, so it is ported the same unsigned way every
    // other +0x156/+0x158 access in this port already reads them.
    private static void UploadFighterTexture(int fighter)
    {
        if (PsxRam.ReadI32(fighter + 0x94) != PsxRam.ReadI32(fighter + 0x14c)
            && 0 < PsxRam.ReadI32(fighter + 0x13c))
        {
            int iVar2 = PsxRam.ReadI32(fighter + 0x98);
            ushort uVar1 = (ushort)(PsxRam.ReadU16(iVar2 + 0xa) >> 9);
            ushort local_a = (ushort)(uVar1 & 0x78);
            ushort local_c = local_a;

            if ((uVar1 & 0x78) == 0)
            {
                local_a = PsxRam.ReadU16(iVar2 + 0xe);
                local_c = PsxRam.ReadU16(iVar2 + 0xc);
            }

            local_c = (ushort)(local_c >> 2);

            FileIo.DecompressAndLoadImage(
                PsxRam.ReadI32(fighter + 0x94),
                PsxRam.ReadU16(fighter + 0x156),
                PsxRam.ReadU16(fighter + 0x158),
                (short)local_c,
                (short)local_a,
                0);

            PsxRam.WriteI32(fighter + 0x14c, PsxRam.ReadI32(fighter + 0x94));
        }
    }

    // GHIDRA: DriveFighterAura @ 0x8004FD24 (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/FighterMotion.cs. The call sites in this file
    // reach it by qualified name: an empty stub in the enclosing class silently beats a real
    // body elsewhere, which is what check_function_addresses.py exists to catch.

    // GHIDRA: UpdateFighterComboTimer @ 0x80050A14 (VS.EXE)
    // CERTAIN, full decompilation, 208 bytes. Phase 10, the tail — and it ends at 0x80050AE3, one
    // byte below this callback's own entry point, so the two are adjacent in the same compilation
    // unit.
    //
    // A frame counter at +0x229, reset to 0x14 (20) whenever bit 6 of +0x138 is set; otherwise
    // decremented once per call. Verified against mcp__pcsx-redux__pcsx_analyze_function @
    // 0x80050a14 because Ghidra's own decompilation types the compare as `char cVar1 = ... + -1;
    // if (cVar1 < '\0')`, which reads like "only fires when the byte was 0" if the surrounding
    // `lbu` reload is taken at face value — the raw instructions show otherwise: after the `lbu`
    // reload and `addiu -1`, the result is put through `sll 0x18` then `sra 0x18`, i.e. the low
    // BYTE of the decremented value is re-sign-extended before the `bgez` branch. That is a real
    // truncate-to-signed-byte step, not a 32-bit compare, so the write below fires whenever the
    // stored byte, reread unsigned next call, decrements to something in 0x80..0xFF as well as on
    // the frame it reaches 0 — a decrement through 0 leaves 0xFF stored (0-1 truncated), and
    // 0xFF down through 0x80 all re-trigger -- 0x80 INCLUDED, since (sbyte)0x80 is -128 and the
    // test is `< 0`. That
    // double-humped firing pattern is the original's, reproduced verbatim rather than "corrected"
    // to a single fire at zero, per rule 12.
    //
    // The write clears one halfword in the battle context's table at
    // ctx+slotIndex*0x14+0x15BA — FighterBattleContext (+0xF0) and FighterSlotIndex (+0x173) are
    // BattleState's; the table itself and its +0x15BA row are not named anywhere in this port and
    // are left as a raw offset, reported upward rather than guessed at.
    private static void UpdateFighterComboTimer(int fighter)
    {
        if (((uint)PsxRam.ReadI32(fighter + 0x138) & 0x40) == 0)
        {
            sbyte cVar1 = (sbyte)(PsxRam.ReadU8(fighter + 0x229) - 1);
            PsxRam.WriteU8(fighter + 0x229, unchecked((byte)cVar1));

            if (cVar1 < 0)
            {
                PsxRam.WriteU16(
                    PsxRam.ReadI32(fighter + BattleState.FighterBattleContext)
                        + PsxRam.ReadU8(fighter + BattleState.FighterSlotIndex) * 0x14 + 0x15ba,
                    0);
            }
        }
        else
        {
            PsxRam.WriteU8(fighter + 0x229, 0x14);
        }
    }
}
