using System;
using System.Collections.Generic;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: offline bench for PsxHeap, the desktop stand-in for the PsyQ allocator. The task
// system of TITLE.EXE rests entirely on it, so a defect here would surface as unexplainable
// corruption inside CreateTask/DeleteTask rather than as an allocator bug. Exercises the contract
// the game depends on: 4-aligned addresses, no overlap, in-range, 0 on exhaustion, and reuse after
// free including coalescing.
internal static class HeapValidation
{
    private const int HeapBase = 0x00010000;
    private const int HeapSize = 0x10000;

    private static int s_failures;

    internal static int Run()
    {
        s_failures = 0;

        CheckAlignmentAndRange();
        CheckNoOverlap();
        CheckExhaustionReturnsZero();
        CheckReuseAfterFree();
        CheckCoalescing();
        CheckWritebackThroughResolver();
        CheckRearmKeepsOneRegion();

        Console.WriteLine(s_failures == 0
            ? "HEAP: toutes les verifications passent"
            : $"HEAP: {s_failures} echec(s)");
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

    private static void CheckAlignmentAndRange()
    {
        LibApi.InitHeap(HeapBase, HeapSize);
        foreach (int size in new[] { 1, 3, 4, 0x18, 0x70, 0x194, 0x3034 })
        {
            int p = LibApi.malloc(size);
            Check(p != 0, $"malloc({size}) non nul");
            Check((p & 3) == 0, $"malloc({size}) aligne sur 4");
            Check(p >= HeapBase && p + size <= HeapBase + HeapSize, $"malloc({size}) dans la plage");
        }
    }

    private static void CheckNoOverlap()
    {
        LibApi.InitHeap(HeapBase, HeapSize);
        var spans = new List<(int start, int end)>();
        for (int i = 0; i < 40; i++)
        {
            int size = 0x18 + (i * 4 % 0x100);
            int p = LibApi.malloc(size);
            Check(p != 0, $"bloc {i} alloue");
            if (p == 0)
            {
                return;
            }

            foreach (var (start, end) in spans)
            {
                Check(p + size <= start || p >= end, $"bloc {i} ne chevauche pas un bloc vivant");
            }

            spans.Add((p, p + size));
        }
    }

    private static void CheckExhaustionReturnsZero()
    {
        LibApi.InitHeap(HeapBase, HeapSize);
        Check(LibApi.malloc(HeapSize * 2) == 0, "une demande plus grande que le heap renvoie 0");

        LibApi.InitHeap(HeapBase, HeapSize);
        int served = 0;
        while (LibApi.malloc(0x1000) != 0)
        {
            served++;
            if (served > 1000)
            {
                break;
            }
        }

        Check(served > 0 && served <= 16, $"le heap de 64 Kio sert {served} blocs de 4 Kio puis 0");
    }

    private static void CheckReuseAfterFree()
    {
        LibApi.InitHeap(HeapBase, HeapSize);
        int first = LibApi.malloc(0x100);
        LibApi.free(first);
        int again = LibApi.malloc(0x100);
        Check(first == again, "un bloc libere de meme taille est repris");
    }

    private static void CheckCoalescing()
    {
        LibApi.InitHeap(HeapBase, HeapSize);
        int a = LibApi.malloc(0x2000);
        int b = LibApi.malloc(0x2000);
        int c = LibApi.malloc(0x2000);
        Check(a != 0 && b != 0 && c != 0, "trois blocs de 8 Kio alloues");

        LibApi.free(a);
        LibApi.free(b);
        LibApi.free(c);

        int big = LibApi.malloc(0x5800);
        Check(big != 0, "un bloc de 22 Kio passe apres fusion des trois blocs liberes");
        Check(big == a, "la fusion redonne l'adresse du premier bloc");
    }

    // TITLE.EXE re-arms the heap once per pass through main's loop, at FUN_80058a9c @ 0x80058A9C.
    // That used to leave the PREVIOUS span registered with LibGpu: RamRegion matches on
    // ReferenceEquals, so a fresh array claiming an address a live row already held added a SECOND
    // row, and RamResolve's tie-break is a strict greater-than on the base - with two rows sharing
    // one base the first found wins for ever, so the stale buffer kept answering and every
    // primitive allocated after the re-arm became unreachable to the rasterizer. The registry is
    // also a fixed 64 rows, so main's loop exhausted it.
    private static void CheckRearmKeepsOneRegion()
    {
        // PsxHeap ecrit dans SON tampon vivant; LibGpu doit resoudre vers le meme. C'est
        // exactement ce que la ligne perimee cassait.
        Func<int, (byte[] buffer, int offset)?> previousResolver = PsxRam.AddressResolver;
        PsxRam.AddressResolver = PsxHeap.Resolve;
        try
        {
            CheckRearmKeepsOneRegionBody();
        }
        finally
        {
            PsxRam.AddressResolver = previousResolver;
        }
    }

    private static void CheckRearmKeepsOneRegionBody()
    {
        LibApi.InitHeap(HeapBase, HeapSize);
        int first = LibApi.malloc(0x40);
        Check(first != 0, "premier bloc avant le re-armement");
        Check(LibGpu.RamResolve(first, out byte[] firstBuf, out int firstOff),
            "le premier bloc se resout avant le re-armement");

        // Le meme armement, comme le fait FUN_80058a9c: meme base, meme taille.
        LibApi.InitHeap(HeapBase, HeapSize);
        int second = LibApi.malloc(0x40);
        Check(second != 0, "bloc alloue apres le re-armement");
        Check(LibGpu.RamResolve(second, out byte[] secondBuf, out int secondOff),
            "le bloc alloue apres le re-armement se resout");

        // Le point decisif: la resolution doit rendre le tampon VIVANT, pas l'ancien.
        PsxRam.WriteU8(second + 4, 0x5A);
        Check(secondBuf != null && secondBuf[secondOff + 4] == 0x5A,
            $"la resolution rend le tampon vivant, lu 0x{(secondBuf == null ? 0 : secondBuf[secondOff + 4]):X2}");

        // Et un lien de table d'affichage doit atteindre la meme memoire, par l'un ou l'autre
        // miroir: TITLE.EXE arme son tas a 0x00010000, sans bit de segment.
        int address = LibGpu.RamAddressOf(secondBuf, secondOff);
        Check(address == second, $"l'adresse PSX du bloc est la sienne, attendu 0x{second:X}, lu 0x{address:X}");
        Check(LibGpu.RamResolveLink((uint)second & 0x00ffffff, out byte[] linkBuf, out int linkOff),
            "un lien 24 bits atteint le bloc");
        Check(linkBuf != null && linkOff == secondOff && ReferenceEquals(linkBuf, secondBuf),
            "le lien atteint exactement le meme tampon et le meme decalage");

        // Re-armer en boucle ne doit pas epuiser le registre de 64 lignes.
        for (int i = 0; i < 80; i++)
        {
            LibApi.InitHeap(HeapBase, HeapSize);
        }

        int afterMany = LibApi.malloc(0x40);
        Check(afterMany != 0, "allocation encore possible apres 80 re-armements");
        Check(LibGpu.RamResolve(afterMany, out byte[] manyBuf, out int manyOff),
            "la resolution tient encore apres 80 re-armements");
        PsxRam.WriteU8(afterMany + 4, 0xA5);
        Check(manyBuf != null && manyBuf[manyOff + 4] == 0xA5,
            "apres 80 re-armements la resolution rend toujours le tampon vivant");
    }

    private static void CheckWritebackThroughResolver()
    {
        LibApi.InitHeap(HeapBase, HeapSize);
        int p = LibApi.malloc(0x40);
        Check(p != 0, "bloc alloue pour le test de resolution");

        Func<int, (byte[] buffer, int offset)?> previous = PsxRam.AddressResolver;
        PsxRam.AddressResolver = PsxHeap.Resolve;
        try
        {
            PsxRam.WriteU16(p + 8, 0xBEEF);
            Check(PsxRam.ReadU16(p + 8) == 0xBEEF, "ecriture puis relecture via PsxRam");

            var resolved = PsxHeap.Resolve(p);
            Check(resolved != null, "l'adresse du payload se resout");

            Check(PsxHeap.Resolve(HeapBase - 4) == null, "une adresse sous le heap ne se resout pas");
            Check(PsxHeap.Resolve(HeapBase + HeapSize) == null, "une adresse au-dela du heap ne se resout pas");
        }
        finally
        {
            PsxRam.AddressResolver = previous;
        }
    }
}
