using System;
using DbzLegendsRemaster.TITLE_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: bench for the title screen's image upload. It runs main to stage TITLE.B, then walks
// the load script the way FUN_80057c80 @ 0x80057C80 does and checks two things that matter.
//
// First, the transliterated LZSS of FUN_80035778 @ 0x80035778 is compared byte for byte against
// PsxTools.LzssDecompressor, an independent reading of the same format written for the analyser.
// Two readings agreeing is much stronger evidence than one.
//
// Second, FUN_80021dd0 is run for real and VRAM is checked to have actually changed, which is what
// proves the uploads land.
internal static class TitleImagesValidation
{
    private static int s_failures;

    internal static int Run()
    {
        s_failures = 0;

        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateTitleExe();

        // main ne rend jamais la main: sa boucle de frame tourne jusqu'au mode attract. Le banc
        // s'arrete au premier balayage de RunFrameLoop, donc apres toute l'initialisation.
        FrameBaton.ResetHeadless(1);
        try
        {
            new TITLE_EXE_exe().Main();
        }
        catch (GameShutdownException)
        {
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  ECHEC: main a leve {exception.GetType().Name}: {exception.Message}");
            Console.WriteLine("TITLE-IMAGES: echec");
            return 1;
        }

        byte[] file = TITLE_EXE_exe.DAT_80110000;
        int scriptOffset = (int)ReadU32(file, 4);
        Check(scriptOffset == 0x08, $"offset du script 0x08, lu 0x{scriptOffset:X}");

        uint count = ReadU32(file, scriptOffset);
        Check(count == 6, $"le script porte 6 entrees, lu {count}");

        int lzssEntries = 0;
        int rawEntries = 0;
        int entry = scriptOffset + 4;
        for (uint i = 0; i < count; i++, entry += 0x1c)
        {
            uint kind = ReadU32(file, entry);
            uint dataOffset = ReadU32(file, entry + 4);
            uint widthWords = ReadU32(file, entry + 0x10);
            uint height = ReadU32(file, entry + 0x14);

            Check(kind <= 1, $"entree {i}: kind 0 ou 1, lu {kind}");
            Check(dataOffset < (uint)file.Length,
                $"entree {i}: dataOffset 0x{dataOffset:X} dans le fichier");

            if (kind == 0)
            {
                lzssEntries++;

                // Notre translitteration.
                byte[] mine = new byte[0x8000];
                TitleImages.FUN_80035778(file, (int)dataOffset, mine, 0);

                // L'oracle independant.
                byte[] packed = new byte[file.Length - (int)dataOffset];
                Array.Copy(file, (int)dataOffset, packed, 0, packed.Length);
                byte[] theirs = PsxTools.LzssDecompressor.Decompress(packed);

                Check(theirs.Length <= mine.Length,
                    $"entree {i}: sortie de {theirs.Length} octets tient dans le tampon");

                int mismatch = -1;
                for (int b = 0; b < theirs.Length && b < mine.Length; b++)
                {
                    if (theirs[b] != mine[b])
                    {
                        mismatch = b;
                        break;
                    }
                }

                Check(mismatch < 0,
                    mismatch < 0
                        ? $"entree {i}: {theirs.Length} octets identiques a l'oracle"
                        : $"entree {i}: divergence a 0x{mismatch:X}, attendu {theirs[mismatch]:X2}, lu {mine[mismatch]:X2}");

                Console.WriteLine(
                    $"  entree {i}: LZSS, {theirs.Length} octets decompresses, {widthWords}x{height} en VRAM");
            }
            else
            {
                rawEntries++;
                Console.WriteLine($"  entree {i}: brute, {widthWords}x{height} en VRAM");
            }
        }

        Check(lzssEntries > 0, $"au moins une entree LZSS, compte {lzssEntries}");
        Check(rawEntries > 0, $"au moins une entree brute, compte {rawEntries}");

        // Et l'upload lui-meme: la VRAM doit changer.
        Array.Clear(LibGpu.Vram, 0, LibGpu.Vram.Length);
        TitleImages.FUN_80021dd0();

        int nonZero = 0;
        for (int i = 0; i < LibGpu.Vram.Length; i++)
        {
            if (LibGpu.Vram[i] != 0)
            {
                nonZero++;
            }
        }

        Check(nonZero > 0x1000,
            $"la VRAM porte {nonZero} cellules non nulles apres l'upload");
        Console.WriteLine($"  VRAM: {nonZero} cellules non nulles");

        // main a deja appele FUN_80021dd0 une fois, et le banc vient de le rappeler pour mesurer
        // l'upload, d'ou deux taches en liste 6.
        Check(TaskSystem.g_TaskListCount[6] == 2,
            $"deux taches en liste 6 apres le second appel, compte {TaskSystem.g_TaskListCount[6]}");

        Console.WriteLine(s_failures == 0
            ? "TITLE-IMAGES: toutes les verifications passent"
            : $"TITLE-IMAGES: {s_failures} echec(s)");
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

    private static uint ReadU32(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | (buffer[offset + 1] << 8)
               | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
}
