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
    // JUSTIFICATION: backend MonoGame only
    // RELATION: reads one environment variable as hex or decimal, 0 when absent or unparsable.
    private static uint ParseHexEnvironment(string name)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        raw = raw.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(raw.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out uint hex)
                ? hex
                : 0;
        }

        return uint.TryParse(raw, out uint dec) ? dec : 0;
    }

    internal static int Run(string[] args)
    {
        int budget = 240;
        if (args.Length > 1 && int.TryParse(args[1], out int parsed))
        {
            budget = parsed;
        }

        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateVsExe();

        // THE HANDOVER, AND WHY IT IS NOT ENOUGH TO SEED THE MODE WORD ALONE.
        //
        // This probe used to write only 0x801FF100 and leave the rest of the shared high-RAM block
        // zero, and the note here said a probe that measures a scenario the game never produces
        // answers the wrong question. It was still measuring one. Two whole pieces of the handover
        // were missing, and each on its own is enough to stop the battle dead:
        //
        //   THE ROSTER. Roster.FUN_8005cbe0 reads six halfwords at 0x801FF102..0x801FF10C -- three
        //   character ids per team, written by SELECT_EXE/CharacterSelect.cs at short indices
        //   0x81..0x86 -- and every one of them being zero is why the battle context marks no slot,
        //   why no slot record ever carries the 0x210 the substitution manager needs, and therefore
        //   why no fighter's +0x144 is ever set and every fighter task stops at phase 1.
        //
        //   THE PAD REMAP TABLES. SLPS_003.55's own bootstrap FUN_8002165C is the only writer of
        //   the fourteen masks at 0x801FF020 and the fourteen at 0x801FF03C. VS_EXE/PadInput.cs's
        //   remap loop ORs those halfwords into the word the whole game reads, so with the tables
        //   zero the remapped pad is zero no matter what the player presses -- and the entire
        //   command decoder in FighterInput.cs reads only the remapped word.
        //
        // So the probe now runs the real bootstrap first, then writes the mode and the six ids the
        // way SELECT.EXE does. 0x801FF000..0x801FF247 sits outside the bss range start() clears
        // (0x8008D254..0x800C3DD4), so all of it survives into VS.EXE.
        //
        // Usage: --diag-vs [frames] [mode] [id0 id1 id2 id3 id4 id5]
        // The six ids default to 1..6, which is a FIXTURE CHOICE, not a fact about the game: any
        // value in the roster's own 1..38 range is a character, and the probe prints which ones it
        // used so a run is reproducible.
        SLPS_003_55.SLPS_003_55_exe.FUN_8002165c();

        int mode = -1;
        if (args.Length > 2 && int.TryParse(args[2], out int parsedMode))
        {
            mode = parsedMode;
        }

        SharedHighRam.SHORT_ARRAY_801ff000[0x80] = (short)(mode < 0 ? 0 : mode);

        short[] rosterIds = { 1, 2, 3, 4, 5, 6 };
        for (int i = 0; i < 6; i++)
        {
            if (args.Length > 3 + i && short.TryParse(args[3 + i], out short parsedId))
            {
                rosterIds[i] = parsedId;
            }

            SharedHighRam.SHORT_ARRAY_801ff000[0x81 + i] = rosterIds[i];
        }

        FrameBaton.ResetHeadless(budget);

        // THE PAD, PRESSED ON PURPOSE. RunBattleRound's two overrides at 0x80056358 are the only
        // writers in the whole overlay that clear CtxFlags bits 12/13 and toggle bit 14, and until
        // one of them fires the round body reaches its `goto LAB_80056c64` on every frame and does
        // nothing else. FUN_80055EE0 arms the match with bits 13, 14, 15 and 31 all set, so that is
        // the state a match starts in and a button is what leaves it.
        //
        // A headless run has no host thread and therefore no pad at all, so a probe without this
        // cannot tell "the port is wrong" from "nobody pressed anything". The mask is the pad's own
        // active-low word: DBZ_PAD_PRESS_MASK names the raw libetc button bits to hold (0x800 is
        // R1, which is what both overrides test through g_PadNewlyPressed), and
        // DBZ_PAD_PRESS_FRAME the headless frame to hold them on. Held for exactly two frames, so
        // ProcessPadInput sees one rising edge and no auto-repeat.
        //
        // Off by default: with neither variable set the run is exactly what it was.
        uint pressMask = ParseHexEnvironment("DBZ_PAD_PRESS_MASK");
        int pressFrame = (int)ParseHexEnvironment("DBZ_PAD_PRESS_FRAME");
        if (pressMask != 0 && pressFrame > 0)
        {
            FrameBaton.HeadlessFrameHook = frame =>
            {
                if (frame == pressFrame)
                {
                    PadInputBackend.PressHeadless(~pressMask);
                }
                else if (frame == pressFrame + 2)
                {
                    PadInputBackend.PressHeadless(0xFFFFFFFFu);
                }
            };
        }

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
            + (mode < 0 ? "0 (defaut)" : mode.ToString())
            + $", roster {string.Join(",", rosterIds)}"
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
            Console.WriteLine("  LES QUATRE CONDITIONS QUI LEVENT LE BIT 3 (RunBattleRound l.117-134):");
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
                "  ET CE QUI ALIMENTE LA JAUGE (RunBattleRound l.96-106): les contributions +0x15B8,");
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
        int bgR = VS_EXE_exe.DiagBackgroundR;
        int bgG = VS_EXE_exe.DiagBackgroundG;
        int bgB = VS_EXE_exe.DiagBackgroundB;
        Console.WriteLine(
            $"fond du DRAWENV : RGB({bgR}, {bgG}, {bgB})"
            + ((bgR == 0 && bgG == 0 && bgB == 200)
                ? "   <-- le defaut code en dur: FUN_800414ec n'a PAS tourne"
                : "   <-- une variante de VariantBackgroundColorTable: FUN_800414ec a tourne"));

        Console.WriteLine();
        Console.WriteLine(
            $"  PAD : ProcessPadInput x{PadInput.DiagProcessCalls}"
            + $"   brut cumule 0x{PadInput.DiagRawEverSeen:X8}"
            + $"   fronts cumules 0x{PadInput.DiagEdgeEverSeen:X8}"
            + $"   remappe cumule 0x{PadInput.DiagRemappedEverSeen:X8}");

        Console.Write("  creneaux, drapeaux du dossier (+0x15B0) :");
        for (int i = 0; i < 12; i++)
        {
            Console.Write($" {BattleManager.DiagSlotRecordFlags[i]:X4}");
        }

        Console.WriteLine();
        Console.Write("  creneaux, pointeur de combattant (+0x1520) :");
        for (int i = 0; i < 12; i++)
        {
            Console.Write($" {(BattleManager.DiagSlotPointers[i] != 0 ? "X" : ".")}");
        }

        Console.WriteLine();

        Console.WriteLine(
            $"  CtxRoundRequest cumule : 0x{BattleManager.DiagRoundRequestEverSeen:X8}"
            + $"   porte 0x180 franchie (FUN_80026d98) : {BattleManager.DiagFun80026d98Calls}");

        Console.WriteLine();
        Console.Write("  LES PHASES DE UpdateFighter (entrees par phase) :");
        for (int i = 1; i < FighterTask.DiagPhaseEntries.Length; i++)
        {
            Console.Write($" {i}:{FighterTask.DiagPhaseEntries[i]}");
        }

        Console.WriteLine();

        Console.WriteLine();
        Console.WriteLine("  ETAPE 9.3, LE MOT DE COMMANDE DE LA FRAME (SelectFighterCommand @ 0x80049F54):");
        Console.WriteLine(
            $"   pad port 1 : {FighterTask.DiagCommandSourceCalls[0]}"
            + $"   pad port 2 : {FighterTask.DiagCommandSourceCalls[1]}"
            + $"   IA : {FighterTask.DiagCommandSourceCalls[2]}"
            + $"   sortie -1 : {FighterTask.DiagCommandSourceCalls[3]}");
        Console.WriteLine(
            $"   +0x138 cumules : 0x{FighterTask.DiagFighterFlagsEverSeen:X8}"
            + "   bit 0x10000000 (pad 1) : "
            + (((FighterTask.DiagFighterFlagsEverSeen & 0x10000000) != 0) ? "VU" : "JAMAIS VU")
            + "   bit 0x20000000 (pad 2) : "
            + (((FighterTask.DiagFighterFlagsEverSeen & 0x20000000) != 0) ? "VU" : "JAMAIS VU"));

        bool anyCommand = false;
        for (int i = 0; i < FighterTask.DiagCommandWords.Length; i++)
        {
            if (FighterTask.DiagCommandWords[i] != 0)
            {
                if (!anyCommand)
                {
                    Console.Write("   mots de commande vus :");
                    anyCommand = true;
                }

                Console.Write($" 0x{i:X2}x{FighterTask.DiagCommandWords[i]}");
            }
        }

        Console.WriteLine(anyCommand ? string.Empty : "   AUCUN mot de commande n'a ete produit.");

        Console.WriteLine();
        Console.WriteLine(
            $"  LE DESSINEUR DE SPRITES (SpriteDrawer @ 0x80052DB4) : appels {SpriteDrawer.DiagCalls}"
            + $"   quads soumis {SpriteDrawer.DiagQuadsSubmitted}"
            + $"   pool plein {SpriteDrawer.DiagPoolFull}"
            + $"   paquet non resolu {SpriteDrawer.DiagUnresolvedPacket}");
        if (SpriteDrawer.DiagCalls != 0 && SpriteDrawer.DiagQuadsSubmitted == 0)
        {
            Console.WriteLine(
                "  IL TOURNE ET NE SOUMET RIEN: chaque quad tombe hors de la table d'ordonnancement,");
            Console.WriteLine(
                "  ou son paquet n'atterrit dans aucune region modelisee. Les deux se voient ici.");
        }

        Console.WriteLine();
        Console.WriteLine("  LA CHAINE DE LA JAUGE, maillon par maillon:");
        Console.WriteLine($"   UpdateFighter (la tache combattant) : {FighterTask.DiagUpdateFighterCalls}");
        Console.WriteLine($"   FUN_8004ee48 (racine A) appelee : {FighterCombat.DiagEe48Calls}");
        Console.WriteLine($"   FUN_8004e758 (racine B) appelee : {FighterCombat.DiagE758Calls}");
        Console.WriteLine($"   AddSlotGaugeContribution (le semeur) appelee: {FighterCombat.DiagE108Calls}");

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
