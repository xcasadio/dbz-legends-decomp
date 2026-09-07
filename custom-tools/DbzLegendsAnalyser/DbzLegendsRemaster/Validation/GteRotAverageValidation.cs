using System;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: bench for LibGte.RotAverage3 @ 0x800772E4 (VS.EXE) and its sibling RotAverage4
// @ 0x8006D5D8.
//
// WHY THIS BENCH EXISTS, AND WHY ITS PREMISE CHANGED. It was written when RotAverage3 had no known
// body in any image and was RECONSTRUCTED from the PSY-Q prototype, from RotAverage4's shape and
// from Avsz3. A bench restating a reconstruction proves nothing -- it agrees with the
// implementation by construction and keeps agreeing if both are wrong together -- so the
// load-bearing assertion was made a CROSS-CHECK AGAINST THE CERTAIN SIBLING instead.
//
// The real body has since been found and decoded, and the cross-check held: same three vertices,
// same single RTPT, same three screen points. Keeping it is still worth the lines, because it is
// now a cross-check between two independently decoded routines rather than a proxy for one.
//
// WHAT THE RECONSTRUCTION MISSED, and what this bench therefore did not catch, is recorded here as
// a limit rather than quietly fixed: the real routine has EIGHT parameters, writing IR0 and FLAG
// through two out-pointers, and the reconstruction had six. Every assertion below passed anyway,
// because the sole caller discards both outputs and the OTZ is identical either way. A bench that
// compares two routines cannot see a parameter neither of them is asked for. The out-parameter
// assertion added at the end is the narrow guard against that class, and its real answer was the
// symbol table.
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

        // Poisoned on purpose: the routine must WRITE these, and a version that ignored its two
        // out-parameters -- which is exactly what the reconstruction did by not having them -- would
        // leave the poison in place.
        byte[] tri = new byte[0x40];
        int[] p3 = { 0x5A5A5A5A };
        int[] flag3 = { 0x5A5A5A5A };
        int otz3 = LibGte.RotAverage3(v0, v1, v2, tri, 0x00, 0x08, 0x10, p3, flag3);

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

        // The two out-parameters the reconstruction did not have. They are written, and both are
        // PARTIAL zeros because this port models neither IR0 nor the FLAG register -- the same gap
        // RotAverage4 carries at the same point. The assertion is that they are WRITTEN, not that
        // the value is meaningful: when someone models IR0 and FLAG, this is the check that fails
        // and the signal to close both routines together.
        Check(p3[0] != 0x5A5A5A5A, "RotAverage3 ecrit son parametre de sortie p (stdp @0x80077324)");
        Check(flag3[0] != 0x5A5A5A5A,
            "RotAverage3 ecrit son parametre de sortie flag (sw @0x80077328)");
        Check(p3[0] == 0 && flag3[0] == 0,
            "PARTIAL assume: IR0 et FLAG ne sont pas modelises, donc zero comme RotAverage4");

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
