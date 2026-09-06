using System;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: bench for LibGte.RotAverage3, which is the one member of its family that was NOT
// decoded from an image.
//
// WHY THIS BENCH EXISTS AND WHAT IT IS ALLOWED TO ASSERT. RotAverage4 is CERTAIN: its 124-byte
// body was read instruction by instruction at 0x8006D5D8. RotAverage3 has no such body anywhere in
// the images -- it is reconstructed from the PSY-Q prototype, from RotAverage4's shape, and from
// Avsz3. A bench that simply restated my own reconstruction would prove nothing: it would agree
// with the implementation by construction and keep agreeing if both were wrong together.
//
// So the load-bearing assertion here is a CROSS-CHECK AGAINST THE CERTAIN SIBLING, not against
// arithmetic of my own. Given the same three vertices, RotAverage3 and RotAverage4 run the same
// single RTPT over them, so the three screen points RotAverage3 writes must be byte-identical to
// the first three RotAverage4 writes. That comparison is decided by decoded code on one side.
//
// The OTZ assertion is weaker on purpose and says so: it checks that RotAverage3's return equals
// Avsz3's own published formula over the three Z results, which pins the CHOICE of AVSZ3 over
// AVSZ4 -- the single most likely way to get this routine wrong, since the two differ only in
// which Z slots they sum and by which scale factor.
internal static class GteRotAverageValidation
{
    private static int s_failures;

    internal static int Run()
    {
        s_failures = 0;

        LibGte.InitGeom();
        LibGte.SetGeomOffset(0xa8, 0x80);
        LibGte.SetGeomScreen(0x100);

        // Identity rotation, a translation that pushes the vertices well in front of the eye so
        // nothing clips and every Z is positive and distinct.
        var m = new LibGte.MATRIX();
        m.m[0] = 0x1000; m.m[1] = 0; m.m[2] = 0;
        m.m[3] = 0; m.m[4] = 0x1000; m.m[5] = 0;
        m.m[6] = 0; m.m[7] = 0; m.m[8] = 0x1000;
        m.t[0] = 0; m.t[1] = 0; m.t[2] = 0x400;
        LibGte.SetRotMatrix(m);
        LibGte.SetTransMatrix(m);

        // Three vertices at three different depths, so the Z-FIFO slots are distinguishable and a
        // routine that summed the wrong ones would produce a different OTZ.
        var v0 = new LibGte.SVECTOR { vx = -0x80, vy = -0x40, vz = 0x20 };
        var v1 = new LibGte.SVECTOR { vx = 0x90, vy = -0x30, vz = 0x60 };
        var v2 = new LibGte.SVECTOR { vx = 0x10, vy = 0x70, vz = -0x50 };
        var v3 = new LibGte.SVECTOR { vx = 0, vy = 0, vz = 0 };

        // RotAverage4 first, on the same three vertices plus a fourth. Its own three sxy stores are
        // the reference: they come from decoded code.
        byte[] quad = new byte[0x40];
        int[] pOut = new int[1];
        int[] flagOut = new int[1];
        LibGte.RotAverage4(v0, v1, v2, v3, quad, 0x00, 0x08, 0x10, 0x18, pOut, flagOut);

        byte[] tri = new byte[0x40];
        int otz3 = LibGte.RotAverage3(v0, v1, v2, tri, 0x00, 0x08, 0x10);

        // THE ASSERTION THAT MATTERS: same vertices, same single RTPT, so the same screen points.
        for (int i = 0; i < 3; i++)
        {
            int off = i * 8;
            bool same = true;
            for (int b = 0; b < 4; b++)
            {
                if (quad[off + b] != tri[off + b])
                {
                    same = false;
                }
            }

            Check(same, $"sommet {i}: RotAverage3 projette comme RotAverage4 (offset 0x{off:X2})");
        }

        // The AVSZ3-versus-AVSZ4 choice, pinned. Recompute Avsz3's published formula from the same
        // GTE state RotAverage3 left behind and require the returned OTZ to match it.
        int otzExpected = ExpectedAvsz3();
        Check(otz3 == otzExpected,
            $"OTZ = saturation(0x155 * (SZ1+SZ2+SZ3) >> 12), lu {otz3}, attendu {otzExpected}");

        // And it must NOT be the four-term answer: if RotAverage3 had been written with Avsz4 it
        // would sum a stale SZ0 as well. This check is what makes the previous one non-vacuous.
        int otzFourTerm = ExpectedAvsz4();
        Check(otzExpected != otzFourTerm,
            $"le banc discrimine: AVSZ3 ({otzExpected}) et AVSZ4 ({otzFourTerm}) different sur ce cas");
        Check(otz3 != otzFourTerm, "RotAverage3 n'utilise pas AVSZ4");

        Console.WriteLine(s_failures == 0
            ? "GTE-ROTAVG: toutes les verifications passent"
            : $"GTE-ROTAVG: {s_failures} echec(s)");
        return s_failures == 0 ? 0 : 1;
    }

    // JUSTIFICATION: backend MonoGame only — recomputes Avsz3's documented formula from the Z slots
    // the port exposes, so the bench does not have to trust RotAverage3's own call to it.
    private static int ExpectedAvsz3()
    {
        int sum = LibGte.PeekSz(1) + LibGte.PeekSz(2) + LibGte.PeekSz(3);
        int otz = (0x155 * sum) >> 12;
        return otz > 0xFFFF ? 0xFFFF : otz < 0 ? 0 : otz;
    }

    // JUSTIFICATION: backend MonoGame only — the four-term sibling, used only to prove the two
    // answers differ on this input so the AVSZ3 assertion is discriminating.
    private static int ExpectedAvsz4()
    {
        int sum = LibGte.PeekSz(0) + LibGte.PeekSz(1) + LibGte.PeekSz(2) + LibGte.PeekSz(3);
        int otz = (0x100 * sum) >> 12;
        return otz > 0xFFFF ? 0xFFFF : otz < 0 ? 0 : otz;
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
