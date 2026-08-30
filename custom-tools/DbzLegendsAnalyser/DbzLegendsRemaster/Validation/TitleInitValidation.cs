using System;
using System.IO;
using DbzLegendsRemaster.TITLE_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: drives main @ 0x800581DC's opening path head-on, with no window, and checks the one
// externally verifiable thing it produces: TITLE.B staged in PSX RAM at 0x80110000. That exercises
// the whole chain at once — CdSearchFile, ReadFile, WaitSearchFile, ReadCDData, CdControl(SetLoc),
// the new CdRead, and LibDs's data-sector support — against the real file on disc.
internal static class TitleInitValidation
{
    private static int s_failures;

    internal static int Run()
    {
        s_failures = 0;

        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateTitleExe();

        try
        {
            new TITLE_EXE_exe().Main();
        }
        catch (Exception exception)
        {
            s_failures++;
            Console.WriteLine($"  ECHEC: main a leve {exception.GetType().Name}: {exception.Message}");
            Console.WriteLine("TITLE-INIT: echec");
            return 1;
        }

        string source = Path.Combine(AppContext.BaseDirectory, "data", "SUB", "TITLE.B");
        if (!File.Exists(source))
        {
            Console.WriteLine($"  ECHEC: fichier de reference absent: {source}");
            Console.WriteLine("TITLE-INIT: echec");
            return 1;
        }

        byte[] expected = File.ReadAllBytes(source);
        byte[] staged = TITLE_EXE_exe.DAT_80110000;

        Check(expected.Length == 0x25000, $"TITLE.B fait 0x25000 octets, mesure {expected.Length:X}");
        Check(staged.Length == 0x25000, "le buffer PSX fait 0x25000 octets");

        int firstMismatch = -1;
        int compared = Math.Min(expected.Length, staged.Length);
        for (int i = 0; i < compared; i++)
        {
            if (expected[i] != staged[i])
            {
                firstMismatch = i;
                break;
            }
        }

        Check(firstMismatch < 0,
            firstMismatch < 0
                ? "contenu identique"
                : $"premier octet different a 0x{firstMismatch:X}: attendu {expected[firstMismatch]:X2}, lu {staged[firstMismatch]:X2}");

        // Les deux premiers mots sont les offsets documentes par TITLE_B_FILE_FORMAT_ANALYSIS.md.
        int groupTableOffset = PsxRam.ReadI32(unchecked((int)0x80110000));
        int loadScriptOffset = PsxRam.ReadI32(unchecked((int)0x80110004));
        Check(groupTableOffset == 0x1A4, $"offset de la table de groupes 0x1A4, lu 0x{groupTableOffset:X}");
        Check(loadScriptOffset == 0x008, $"offset du script de chargement 0x8, lu 0x{loadScriptOffset:X}");

        // main enregistre la tache camera puis l'appelle une fois.
        Check(TaskSystem.g_TaskListCount[0] == 1,
            $"une tache dans la liste 0, compte={TaskSystem.g_TaskListCount[0]}");
        int task = TaskSystem.g_TaskListHead[0];
        Check(task != 0, "la tete de la liste 0 est non nulle");
        if (task != 0)
        {
            int callback = PsxRam.ReadI32(task + 0x04);
            Check(callback == unchecked((int)0x80037388),
                $"le bloc porte l'adresse PSX d'origine du callback, lu 0x{callback:X8}");
        }

        // SetupGeometry(0xa8, 0x80, 0x1000, 0,0,0, 0x1000, 0,0,0) remplit le scratchpad.
        Check(GteScratch.DAT_1f800114 == 0xa8, $"offset X 0xa8, lu 0x{GteScratch.DAT_1f800114:X}");
        Check(GteScratch.DAT_1f800110 == 0x80, $"offset Y 0x80, lu 0x{GteScratch.DAT_1f800110:X}");
        Check(GteScratch._DAT_1f8000c0 == 0x1000,
            $"distance 0x1000, lu 0x{GteScratch._DAT_1f8000c0:X}");
        Check(GteScratch.MATRIX_1f8000e4.m[0] == 0x1000
              && GteScratch.MATRIX_1f8000e4.m[3] == 0x1000
              && GteScratch.MATRIX_1f8000e4.m[6] == 0x1000,
            "matrice couleur chargee a 0x1000 sur ses trois positions");

        // FUN_80037388 derive cette valeur de la profondeur projetee, bornee a zero.
        Check(GteScratch.DAT_1f800128 >= 0,
            $"profondeur derivee non negative, lu {GteScratch.DAT_1f800128}");

        Console.WriteLine(s_failures == 0
            ? "TITLE-INIT: toutes les verifications passent"
            : $"TITLE-INIT: {s_failures} echec(s)");
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
