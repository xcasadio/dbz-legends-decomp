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

    // JUSTIFICATION: backend MonoGame only
    // RELATION: parses DBZ_PAD_SCRIPT into a list of (frame, rawMask) holds. Format is
    // "frame:hexmask" entries separated by commas, e.g. "300:800,520:1020,524:0" -- press R1 at
    // frame 300, then RIGHT+TRIANGLE at 520, then release at 524. Each entry REPLACES the held mask
    // from its frame onward, so a hold lasts until the next entry; a mask of 0 releases.
    //
    // WHY A SCRIPT AND NOT A SECOND PAIR OF VARIABLES. The two events a battle needs are hundreds of
    // frames apart and neither can move. R1 has to fire near the start, because it drives
    // RunBattleRound's round-start override at 0x80056358. An attack has to fire much later,
    // because a fighter is not pad-driven until the battle context word at ctx+0x10 carries bit
    // 0x100000 -- set by the round arm at 0x80056EFC (`lui v0,0x10 / or v0,a0,v0 / sw v0,0x10(s2)`)
    // -- and phase 8 then matches the acting-slot cursors at ctx+0x14 and ctx+0x16 against the
    // fighter's own slot index. That is measurable rather than theoretical: pad-port calls are 0 at
    // a 500-frame budget, 4 at 550, 54 at 700 and 120 at 900, all with the same frame-300 press.
    // A two-frame press at 300 is released two hundred frames before anything reads it.
    //
    // AND SOME COMMANDS NEED MORE THAN ONE ENTRY REGARDLESS OF TIMING. FighterInput.cs's
    // MatchFacingFaceThenOppositeFace wants two DIFFERENT face-button edges one to four frames
    // apart, which no single mask can express at any moment.
    private static (int Frame, uint Mask)[] ParsePressScript()
    {
        string? raw = Environment.GetEnvironmentVariable("DBZ_PAD_SCRIPT");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<(int, uint)>();
        }

        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var holds = new (int Frame, uint Mask)[parts.Length];
        int kept = 0;
        foreach (string part in parts)
        {
            string[] halves = part.Split(':');
            if (halves.Length != 2)
            {
                continue;
            }

            string frameText = halves[0].Trim();
            string maskText = halves[1].Trim();
            if (maskText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                maskText = maskText.Substring(2);
            }

            if (int.TryParse(frameText, out int frame)
                && uint.TryParse(maskText, System.Globalization.NumberStyles.HexNumber, null, out uint mask))
            {
                holds[kept] = (frame, mask);
                kept++;
            }
        }

        Array.Resize(ref holds, kept);
        Array.Sort(holds, (a, b) => a.Frame.CompareTo(b.Frame));
        return holds;
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
        var script = ParsePressScript();

        if (script.Length != 0)
        {
            // DBZ_PAD_SCRIPT wins over the two-variable shorthand when both are set, and the two
            // are never combined: mixing one implicit two-frame press into an explicit script would
            // make a run reproducible only to whoever wrote both.
            var holds = script;
            FrameBaton.HeadlessFrameHook = frame =>
            {
                for (int i = 0; i < holds.Length; i++)
                {
                    if (holds[i].Frame == frame)
                    {
                        // PressHeadless takes the pad's ACTIVE-LOW word, so a held mask is its
                        // complement and 0xFFFFFFFF is everything released.
                        PadInputBackend.PressHeadless(
                            holds[i].Mask == 0 ? 0xFFFFFFFFu : ~holds[i].Mask);
                    }
                }
            };
        }
        else if (pressMask != 0 && pressFrame > 0)
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

        if (script.Length != 0)
        {
            var rendered = new string[script.Length];
            for (int i = 0; i < script.Length; i++)
            {
                rendered[i] = $"{script[i].Frame}:0x{script[i].Mask:X}";
            }

            Console.WriteLine($"=== script pad : {string.Join(" -> ", rendered)}");
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

        Console.Write("  creneaux, index de cible (+0x15C0) :");
        for (int i = 0; i < 12; i++)
        {
            Console.Write($" {BattleManager.DiagSlotTargets[i],3}");
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

        Console.Write("   les sorties precoces de l'IA (FUN_80023890) :");
        {
            string[] names =
            {
                "entrees", "cible=soi", "b26 propre", "b26 cible", "0x200FF+0x20", "b19",
                "echauffement", "corps atteint",
            };
            for (int i = 0; i < names.Length; i++)
            {
                Console.Write($" {names[i]}:{FighterAi.DiagAiExits[i]}");
            }
        }

        Console.WriteLine();

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
        Console.WriteLine();
        Console.WriteLine("  LA PORTE DU BRAS D ATTAQUE DE L IA (+0x138 bit 0x10, FighterAi.cs:600):");
        Console.WriteLine(
            $"   FUN_8004b9cc appelee : {FighterAction.DiagFun8004b9ccCalls}"
            + $"   FUN_800261ec appelee : {FighterAction.DiagFun800261ecCalls}"
            + $"   dont -1 precoce : {FighterAction.DiagFun800261ecReturnedMinusOne}");
        Console.WriteLine(
            $"   FUN_8004a9e8 (seul ecrivain du bit 0x10) appelee : {FighterAction.DiagFun8004a9e8Calls}"
            + $"   dernier local_10 : {FighterAction.DiagLocal10EverSeen}");

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
            // MEASURED, AND NOT WHAT THIS MESSAGE USED TO SAY. It used to offer three suspects --
            // the task is not created, not registered, or its list is not walked -- and all three
            // are wrong. The scene task is born on RunBattleRound's ROUND-IS-OVER arm at
            // 0x800563F8, and that arm opens only when CtxFlags bit 3 is raised, which needs all
            // four conditions printed above. Exactly one of them fails: the central gauge never
            // reaches +/-30000, because the twelve +0x15B8 contributions printed above are all
            // zero, because AddSlotGaugeContribution is never called. So this is a CONSEQUENCE of
            // the gauge chain, not a second independent fault, and the console would do the same.
            Console.WriteLine(
                "  ATTENDU DANS CET ETAT, ET PAS UN DEFAUT. La tache de scene naît sur le bras");
            Console.WriteLine(
                "  << le round est fini >> (0x800563F8), qui exige le bit 3 de CtxFlags. Des quatre");
            Console.WriteLine(
                "  conditions ci-dessus, seule la jauge centrale echoue. Tant que la chaine de");
            Console.WriteLine(
                "  jauge ne seme pas, le round ne se termine pas et cette tache n'a pas lieu");
            Console.WriteLine("  d'exister. Chercher en amont, dans la chaine de jauge.");
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
