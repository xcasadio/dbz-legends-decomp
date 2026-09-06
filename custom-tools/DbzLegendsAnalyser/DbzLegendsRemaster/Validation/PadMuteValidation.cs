using System;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: bench for PadInputBackend.MuteUntilRelease, the latch that replaced LibCd.WaitDiscLoad.
// The latch makes one physical press reach exactly one overlay: an overlay switch arms it, and it
// stays shut until the pad reads all-released. Get it wrong in the shut direction and one Start
// press skips both startup movies again; get it wrong in the open direction and the pad goes dead
// for good the first time the game changes overlay.
//
// THE POINT OF THIS BENCH IS THE SECOND FAILURE, and it is the one a naive bench cannot see. With
// no key held the published word is 0xFFFFFFFF whether the latch is shut or open, so a bench that
// only watched PadInputBackend.PublishedActiveLow would pass just as happily against an
// implementation that never lets go. That is why PadInputBackend.MuteActive exists and why both
// branches below assert the latch's TRANSITION rather than the word it publishes.
//
// Two branches, chosen by the environment, and the bench prints which one it took so a run that
// silently took the wrong one is visible:
//   bare                      nothing is held, so one Poll must OPEN the latch;
//   DBZ_PAD_FORCE=0x0800      Start is held for the life of the process, so it must stay SHUT.
// The forced mask is folded into the sampled word inside Poll, which is exactly what makes it a
// stand-in for a finger on the button here.
//
// NEGATIVE CONTROL, run during implementation and recorded in docs/tasks/DISC_LOAD_LATENCY.md:
// stub out the `s_muteUntilRelease = false;` line in Poll and the bare branch must fail. If it
// still passes, this file is decoration.
internal static class PadMuteValidation
{
    private const uint AllReleased = 0xFFFFFFFF;
    private const uint Start = 0x0800;

    private static int s_failures;

    internal static int Run()
    {
        s_failures = 0;

        string forced = Environment.GetEnvironmentVariable("DBZ_PAD_FORCE");
        bool held = !string.IsNullOrWhiteSpace(forced);

        // A held button from a previous test would poison the first branch, so start from a known
        // state: poll once and let any latch left over from an earlier call settle.
        PadInputBackend.Poll();

        PadInputBackend.MuteUntilRelease();
        Check(PadInputBackend.MuteActive, "le verrou est arme par MuteUntilRelease");
        Check(PadInputBackend.PublishedActiveLow == AllReleased,
            "MuteUntilRelease publie un etat relache sans attendre le prochain Poll");

        PadInputBackend.Poll();

        if (held)
        {
            Console.WriteLine($"PAD-MUTE: branche BOUTON MAINTENU (DBZ_PAD_FORCE={forced})");

            Check(PadInputBackend.MuteActive,
                "le verrou reste arme tant que le bouton est maintenu");

            // LE POINT DE CE BANC, cote asymetrie: le verrou est PAR PORT. Start est force sur le
            // port 1 seulement, et rien n'est tenu sur le port 2, donc apres un Poll le port 1 doit
            // rester muet et le port 2 doit s'etre libere. Un verrou global les laisserait tous les
            // deux armes, et MuteActive seul ne saurait pas les distinguer.
            Check(PadInputBackend.MutePort1Active,
                "port 1 muet: c'est lui qui tient Start");
            Check(!PadInputBackend.MutePort2Active,
                "port 2 libere: il ne tient rien, il ne doit pas subir le port 1");
            Check(PadInputBackend.PublishedActiveLow == AllReleased,
                "rien n'est publie tant que le verrou est arme");
            Check((~PadInputBackend.PublishedActiveLow & Start) == 0,
                "Start maintenu est retenu, pas transmis a l'overlay entrant");

            // A second Poll must not quietly give up either: the latch has no timeout.
            PadInputBackend.Poll();
            Check(PadInputBackend.MuteActive, "toujours arme au deuxieme Poll");
        }
        else
        {
            Console.WriteLine("PAD-MUTE: branche AUCUN BOUTON (relancer avec DBZ_PAD_FORCE=0x0800"
                + " pour exercer l'autre branche)");

            Check(!PadInputBackend.MuteActive,
                "le verrou s'ouvre au premier Poll qui lit un etat relache");
            Check(!PadInputBackend.MutePort1Active && !PadInputBackend.MutePort2Active,
                "les deux ports se sont liberes");
            Check(PadInputBackend.PublishedActiveLow == AllReleased,
                "la publication a repris (rien n'est enfonce, donc etat relache)");

            // And it stays open: arming is the switch's job, not Poll's.
            PadInputBackend.Poll();
            Check(!PadInputBackend.MuteActive, "il ne se rearme pas tout seul");
        }

        Console.WriteLine(s_failures == 0
            ? "PAD-MUTE: toutes les verifications passent"
            : $"PAD-MUTE: {s_failures} echec(s)");
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
