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

        // main cree deux taches dans la liste 0: la camera, puis les pools de primitives.
        Check(TaskSystem.g_TaskListCount[0] == 2,
            $"deux taches dans la liste 0, compte={TaskSystem.g_TaskListCount[0]}");
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

        // FUN_80056dc0(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0) alloue six pools sur huit.
        int ctx = PrimitivePools.DAT_800835f8;
        Check(ctx != 0, "le contexte des pools est enregistre");
        if (ctx != 0)
        {
            int[] wanted = { 0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0 };
            for (int slot = 0; slot < 8; slot++)
            {
                int pool = PsxRam.ReadI32(ctx + (slot * 4));
                int count = PsxRam.ReadI32(ctx + (slot * 4) + 0x20);
                if (wanted[slot] == 0)
                {
                    Check(pool == 0, $"slot {slot} sans pool, lu 0x{pool:X8}");
                }
                else
                {
                    Check(pool != 0, $"slot {slot} alloue");
                    Check(count == wanted[slot],
                        $"slot {slot} compte {wanted[slot]}, lu {count}");
                }
            }

            // Le slot 3 est POLY_GT4: 12 mots, code 0x3C, plus le bit semi-transparent.
            int gt4 = PsxRam.ReadI32(ctx + (3 * 4));
            if (gt4 != 0)
            {
                byte[] tag = PsxRam.ReadBytes(gt4, 8);
                Check(tag != null && tag[3] == 12,
                    $"POLY_GT4 pre-tague a 12 mots, lu {(tag == null ? -1 : tag[3])}");
                Check(tag != null && tag[7] == 0x3E,
                    $"POLY_GT4 code 0x3C avec semi-trans, lu 0x{(tag == null ? 0 : tag[7]):X2}");
            }
        }

        // FUN_80038228(8, 0) configure le quad plein ecran du fondu puis remet l'etat a zero.
        var fade = DisplayMachine.POLY_GT4_800b9518;
        Check(fade.x0 == -2 && fade.y0 == -2, $"coin haut gauche (-2,-2), lu ({fade.x0},{fade.y0})");
        Check(fade.x1 == 0x142 && fade.x3 == 0x142, $"bord droit 0x142, lu 0x{fade.x1:X}");
        Check(fade.y2 == 0xf2 && fade.y3 == 0xf2, $"bord bas 0xf2, lu 0x{fade.y2:X}");
        Check(fade.tpage == 0x10, $"tpage 0x10, lu 0x{fade.tpage:X}");
        Check(fade.clut == 0x7f80, $"clut 0x7f80, lu 0x{fade.clut:X}");
        Check(fade.r0 == 0x80 && fade.g3 == 0x80, "couleurs a 0x80");
        Check(fade.v0 == 0xff && fade.v3 == 0xff, "v0 et v3 a 0xff");
        Check(fade.u1 == 1 && fade.u3 == 1, "u1 et u3 a 1");
        Check(TITLE_EXE_exe.DAT_80083454 == 0,
            $"etat du fondu remis a 0, lu {TITLE_EXE_exe.DAT_80083454}");

        // Et surtout: la texture 2x1 a bien ete televersee en VRAM a (0, 0x1FE).
        Check(LibGpu.Vram[(0x1fe * 1024) + 0] == 0xffff,
            $"VRAM (0,0x1FE) = 0xFFFF, lu 0x{LibGpu.Vram[(0x1fe * 1024) + 0]:X4}");
        Check(LibGpu.Vram[(0x1fe * 1024) + 1] == 0x1111,
            $"VRAM (1,0x1FE) = 0x1111, lu 0x{LibGpu.Vram[(0x1fe * 1024) + 1]:X4}");

        // FUN_80058d64 a pose les cinq quads du titre.
        var quad = TITLE_EXE_exe.POLY_FT4_ARRAY_800a8894[4];
        Check(quad.clut == 0x7985 && quad.tpage == 0x19,
            $"cinquieme quad: clut 0x7985 tpage 0x19, lu 0x{quad.clut:X} 0x{quad.tpage:X}");
        Check(quad.x0 == 0x50 && quad.x1 == 0x5a && quad.y3 == 0x5a, "cinquieme quad positionne");
        Check(TITLE_EXE_exe.DAT_800a897a == 0, "le verrou d'affichage est libere");

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
