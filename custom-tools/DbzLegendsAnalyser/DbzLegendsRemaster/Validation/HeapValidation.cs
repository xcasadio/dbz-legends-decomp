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
