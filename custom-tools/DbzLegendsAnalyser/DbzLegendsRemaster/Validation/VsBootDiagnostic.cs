using System;
using DbzLegendsRemaster.VS_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: boots VS.EXE headlessly and reports where the battle-scene machine stops.
//
// WHY IT EXISTS. The symptom is "the loading screen shows, then the screen is the draw
// environment's background". That single observation is consistent with at least four different
// causes -- the scene task never created, the dispatcher never reached, the phase machine stalling
// at 0 or 1, or the machine running fine while nothing is submitted -- and guessing between them
// from the outside is exactly how a wrong diagnosis gets shipped. One such guess already was: the
// blue screen was attributed to an unported RunBattleManagerFrame when it was a libsnd stub, and fixing that
// stub did not clear it either.
//
// So this asks the machine instead. BattleScene carries a probe that counts dispatcher visits per
// phase; every counter zero means the scene task never ran at all, and a single phase carrying
// every visit names the stall.
internal static class VsBootDiagnostic
{
    internal static int Run(string[] args)
    {
        int budget = 240;
        if (args.Length > 1 && int.TryParse(args[1], out int parsed))
        {
            budget = parsed;
        }

        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateVsExe();

        // THE HANDOVER, seeded on purpose. SELECT.EXE writes a mode word at 0x801FF100 before it
        // LoadExecs into VS.EXE -- BattleState.cs records that it takes one of three values in and
        // 3/4/5 back out -- and booting VS.EXE on its own leaves it zero. A probe that measures a
        // scenario the game never produces answers the wrong question, so the mode is an explicit
        // argument here and is printed with the result. 0x801FF100 sits outside the bss range
        // start() clears (0x8008D254..0x800C3DD4), so seeding it before start() survives.
        int mode = -1;
        if (args.Length > 2 && int.TryParse(args[2], out int parsedMode))
        {
            mode = parsedMode;
            PsxRam.WriteU16(unchecked((int)0x801FF100), (ushort)mode);
        }

        FrameBaton.ResetHeadless(budget);

        string stopped = "budget epuise";
        try
        {
            new VS_EXE_exe().start();
            stopped = "start() a rendu la main";
        }
        catch (GameShutdownException)
        {
        }
        catch (LoadExecTransferException)
        {
            stopped = "LoadExec";
        }
        catch (Exception exception)
        {
            stopped = $"{exception.GetType().Name}: {exception.Message}";
        }

        Console.WriteLine(
            $"=== VS.EXE, budget {budget} frames, mode de relais 0x801FF100 = "
            + (mode < 0 ? "non ensemence" : mode.ToString())
            + $", arret: {stopped} ===");
        Console.WriteLine();
        Console.WriteLine($"appels au repartiteur MANAGER  : {BattleManager.DiagManagerCalls}");
        if (BattleManager.DiagManagerCalls == 0)
        {
            Console.WriteLine(
                "  LE MANAGER NON PLUS. Le blocage est avant lui: sa tache n'est pas creee, ou la");
            Console.WriteLine(
                "  liste 9 n'est pas parcourue, ou main n'atteint jamais sa boucle de frames.");
        }
        else
        {
            for (int i = 0; i < BattleManager.DiagCtxStateVisits.Length; i++)
            {
                if (BattleManager.DiagCtxStateVisits[i] != 0)
                {
                    Console.WriteLine(
                        $"  etat {i} : {BattleManager.DiagCtxStateVisits[i]} visite(s)");
                }
            }

            Console.WriteLine($"  dernier CtxState : {BattleManager.DiagLastCtxState}");
            Console.WriteLine($"  CtxFlags dernier : 0x{BattleManager.DiagLastCtxFlags:X8}");
            Console.WriteLine($"  CtxFlags cumules : 0x{BattleManager.DiagCtxFlagsEverSeen:X8}");
            Console.WriteLine(
                $"  bit 3 (0x8, il ouvre le bras qui cree la scene) : "
                + (((BattleManager.DiagCtxFlagsEverSeen & 8) != 0) ? "VU" : "JAMAIS VU"));
            Console.WriteLine(
                $"  tentatives de creation de la scene : {BattleManager.DiagSceneCreateAttempts}");
            Console.WriteLine();
            Console.WriteLine("  LES QUATRE CONDITIONS QUI LEVENT LE BIT 3 (FUN_80055f94 l.117-134):");
            Console.WriteLine(
                $"   1. (CtxFlags & 0x18000008) == 0        : "
                + (BattleManager.DiagCond1Pass > 0 ? $"OUI ({BattleManager.DiagCond1Pass} frames)" : "NON"));
            Console.WriteLine(
                $"   2. CtxCentralGauge == +/-30000         : {BattleManager.DiagLastGauge}"
                + (System.Math.Abs(BattleManager.DiagLastGauge) == 30000 ? "  OUI" : "  <-- NON"));
            Console.WriteLine(
                $"   3. DAT_8008d458 == 0                   : {BattleManager.DiagLastD458}"
                + (BattleManager.DiagLastD458 == 0 ? "  OUI" : "  <-- NON"));
            Console.WriteLine(
                $"   4. aucun slot (bit0 && champ+2 == 0)   : {BattleManager.DiagLastAliveCount}"
                + (BattleManager.DiagLastAliveCount == 0 ? "  OUI" : "  <-- NON"));
            Console.WriteLine();
            Console.WriteLine(
                "  ET CE QUI ALIMENTE LA JAUGE (FUN_80055f94 l.96-106): les contributions +0x15B8,");
            Console.WriteLine("  equipe A (0-5) additionnee, equipe B (6-11) soustraite:");
            Console.Write("   ");
            for (int i = 0; i < 12; i++)
            {
                Console.Write($"{BattleManager.DiagContribs[i],7}");
                if (i == 5) { Console.Write("  |"); }
            }
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine($"appels au repartiteur de scene : {BattleScene.DiagDispatcherCalls}");

        if (BattleScene.DiagDispatcherCalls == 0)
        {
            Console.WriteLine(
                "  LA TACHE DE SCENE N'A JAMAIS TOURNE. Le blocage est en amont du repartiteur:");
            Console.WriteLine(
                "  soit la tache n'est pas creee, soit elle n'est pas enregistree aupres du");
            Console.WriteLine("  scheduler, soit sa liste n'est pas parcourue.");
        }
        else
        {
            for (int i = 0; i < BattleScene.DiagPhaseVisits.Length; i++)
            {
                if (BattleScene.DiagPhaseVisits[i] != 0)
                {
                    Console.WriteLine($"  phase {i} : {BattleScene.DiagPhaseVisits[i]} visite(s)");
                }
            }

            Console.WriteLine($"derniere phase vue      : {BattleScene.DiagLastPhase}");
            Console.WriteLine($"dernier sous-etat (+0x78): {BattleScene.DiagLastSubStep}");
            Console.WriteLine(
                $"drapeau de suspension VM : 0x{BattleScene.DiagLastSuspendFlag:X4}"
                + ((BattleScene.DiagLastSuspendFlag & 1) != 0
                    ? "  <-- bit 0 LEVE: toutes les phases sautent leur corps"
                    : ""));
        }

        return 0;
    }
}
