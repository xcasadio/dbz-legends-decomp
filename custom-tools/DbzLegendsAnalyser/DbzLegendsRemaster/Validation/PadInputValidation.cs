using System;
using DbzLegendsRemaster.TITLE_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: bench for ProcessPadInput @ 0x800578A8. It derives rising edges, runs an auto-repeat
// that switches from edge to held after seven frames, and remaps the raw hardware bits through the
// tables the bootstrap leaves in high RAM. None of that would crash if it were wrong — it would
// just make the title screen respond oddly, which is hard to trace back here.
//
// Needs a held button to exercise anything, and the window has no focus in a headless run, so it
// drives the same DBZ_PAD_FORCE injection the pad backend already exposes:
//   $env:DBZ_PAD_FORCE = "0x0800"
internal static class PadInputValidation
{
    private const ushort Start = 0x0800;

    private static int s_failures;

    internal static int Run()
    {
        s_failures = 0;

        // The bootstrap fills these through FUN_8002165c @ 0x8002165C; the identity mapping is what
        // it writes, and TITLE.EXE reads it straight back.
        ushort[] masks =
        {
            0x0020, 0x0080, 0x0010, 0x0040, 0x2000, 0x8000, 0x1000,
            0x4000, 0x0100, 0x0800, 0x0008, 0x0002, 0x0004, 0x0001,
        };
        for (int i = 0; i < masks.Length; i++)
        {
            SharedHighRam.SHORT_ARRAY_801ff000[0x10 + i] = (short)masks[i];
            SharedHighRam.SHORT_ARRAY_801ff000[0x1E + i] = (short)masks[i];
        }

        LibEtc.PadInit(0);
        PadInputBackend.Poll();
        uint sampled = ~PadInputBackend.PublishedActiveLow;

        if ((sampled & Start) == 0)
        {
            Console.WriteLine("PAD-INPUT: ignore, aucun bouton force.");
            Console.WriteLine("  relancer avec DBZ_PAD_FORCE=0x0800 pour exercer le banc.");
            return 0;
        }

        // Premiere frame: le bouton vient d'apparaitre, donc front montant.
        PadInput.ProcessPadInput(0);
        Check((PadInput.DAT_800835dc[0] & Start) != 0, "l'etat courant porte Start");
        Check((PadInput.g_PadNewlyPressed[0] & Start) != 0, "front montant sur la premiere frame");
        Check((PadInput.DAT_800835f0[0] & Start) != 0, "la sortie porte le front");
        Check((PadInput.DAT_80083478 & Start) != 0,
            $"Start remappe vers lui-meme, lu 0x{PadInput.DAT_80083478:X}");
        Check((PadInput.DAT_8008346c & Start) != 0, "front montant remappe");

        // Deuxieme frame: le bouton est maintenu, il n'y a plus de front.
        PadInputBackend.Poll();
        PadInput.ProcessPadInput(0);
        Check((PadInput.g_PadNewlyPressed[0] & Start) == 0,
            "plus de front tant que le bouton reste enfonce");
        Check((PadInput.DAT_800835f0[0] & Start) == 0,
            "la sortie reste vide pendant l'attente de repetition");

        // Le compteur passe la sortie de front a maintenu au septieme tour.
        for (int frame = 2; frame < 8; frame++)
        {
            PadInputBackend.Poll();
            PadInput.ProcessPadInput(0);
        }

        Check(PadInput.g_PadHoldFrames[0] >= 7,
            $"compteur de repetition a 7 ou plus, lu {PadInput.g_PadHoldFrames[0]}");
        Check((PadInput.DAT_800835f0[0] & Start) != 0,
            "la repetition automatique remet Start dans la sortie");

        Console.WriteLine(s_failures == 0
            ? "PAD-INPUT: toutes les verifications passent"
            : $"PAD-INPUT: {s_failures} echec(s)");
        return s_failures == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            s_failures++;
            Console.WriteLine($"  ECHEC: {label}");
        }
    }
}
