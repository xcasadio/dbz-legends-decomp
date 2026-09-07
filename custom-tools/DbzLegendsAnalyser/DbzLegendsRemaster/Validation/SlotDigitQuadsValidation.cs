using System;
using DbzLegendsRemaster.VS_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: BuildSlotDigitQuads @ 0x80058338 (VS.EXE), 2440 bytes, the two-digit numeric readouts.
//
// WHY THIS BENCH EXISTS. A fresh-context review confirmed this transliteration by reading -- 112
// stores in the image against 112 writes in the C#, identical widths and offsets -- and then said
// plainly that nothing RUNS it: the thirteen acceptance benches never reach it, and the round-start
// diagnostic never gets far enough for the battle manager to call it. So the strongest evidence the
// port had for its largest closing function was a careful read. This bench makes it a measurement.
//
// It also settles, by running rather than by argument, the objection that kept the function BLOCKED
// for three sessions: its two glyph tables (PTR_DAT_80083FB4 and PTR_DAT_80084124) were thought to
// need embedding because their targets might be mutable runtime state. They do not -- PsxExeImage
// backs VS.EXE's whole extent -- and check 5 below fails loudly if those reads ever come back zero.
//
// WHAT IT ASSERTS, and where each expectation comes from. Every one is read off the DECOMPILATION
// of 0x80058338, not off the C# under test, because an expectation copied from the code it checks
// proves only that the code equals itself:
//
//   1. THE GLYPH CELL. The ones digit of each field is `field % 10` and its u0 is that times 0x18,
//      with u1 exactly 0x18 further -- a 24-unit cell. The v bytes are the constants 0xE0 and 0xFF.
//   2. LEADING-ZERO SUPPRESSION. When the tens digit is zero the whole tens quad is ZEROED (four
//      bytes and eight halfwords set to 0, with 0xE0 in all four v slots); when it is non-zero the
//      quad carries `tens * 0x18` and 0xFF in two of the four v slots. Those are two different
//      arms of one `if`, and an inverted test would swap them.
//   3. THE DEGENERATE GEOMETRY. Set the two positions at +0x38 and +0x50 EQUAL. Then the image's
//      `iVar8 = |p38 - p50|` is 0, every `(row[k] * 0) / 0x60` is 0, the `< 0x50` arm is taken so
//      nothing gains the +7, and `p50 < p38` is false so the ELSE arm runs: every x lands on the
//      origin at +0x40 and every y on the origin at +0x3A. This checks the whole coordinate
//      plumbing -- eight halfwords per field -- without depending on a single table byte.
//   4. THE 0x1C0 STRIDE. The function's own first statement is `param_1 + slot * 0x1C0 + 0x20`.
//      Writing slot 3 must leave slots 2 and 4 untouched. A wrong stride would still look correct
//      on a single slot.
//   5. THE TABLES ARE REALLY THERE. With a NON-ZERO separation the row values scale the output, so
//      the two tables become observable. Both table words must be non-zero, must differ from each
//      other, and selecting between them (tens zero versus non-zero) must actually change what is
//      written. If PsxExeImage ever stops backing that address the reads go to zero and this is the
//      check that says so.
//
// The bench drives the function against a synthetic record placed at a PSX address of its own, in
// the same style the port uses for scratch frames, so it needs no battle and no round.
internal static class SlotDigitQuadsValidation
{
    private static int _failures;

    // JUSTIFICATION: PSX hardware adaptation
    // RELATION: a synthetic record base with no counterpart in the original. The function indexes
    // `param_1 + slot * 0x1C0 + 0x20` and reads and writes through PsxRam, so it needs a backing
    // span at a PSX address. 0x807F0000 is chosen the same way the port's scratch stack frames are:
    // high RAM the overlay itself never touches, declared here and nowhere else. Twelve slots of
    // 0x1C0 plus the 0x20 base offset plus the 0x196 the last quad reaches is 0x14F6; 0x1600 is
    // rounded up from that.
    private const int RecordBase = unchecked((int)0x807F0000);

    private const int RecordSize = 0x1600;

    private static readonly byte[] Record = LibGpu.RamRegion(RecordBase, RecordSize);

    private static void Check(bool condition, string what)
    {
        if (!condition)
        {
            _failures++;
            Console.WriteLine($"  ECHEC {what}");
        }
    }

    private static void CheckEqual(int expected, int actual, string what)
    {
        if (expected != actual)
        {
            _failures++;
            Console.WriteLine($"  ECHEC {what}: attendu 0x{expected:X}, obtenu 0x{actual:X}");
        }
    }

    // The address of one slot's sub-record, exactly as the function computes it.
    private static int Slot(int slot) => RecordBase + slot * 0x1c0 + 0x20;

    private static void Clear()
    {
        Array.Clear(Record, 0, Record.Length);
    }

    // Lay down one slot's inputs. fieldA lands at +0x0A and drives the quads at 0x150..0x195;
    // fieldB lands at +0x26 and drives the quads at 0x100..0x145.
    private static void Seed(int slot, int fieldA, int fieldB, int rowA, int rowB, int x0, int y0, int p38, int p50)
    {
        int p = Slot(slot);
        PsxRam.WriteU16(p + 0x0a, (ushort)fieldA);
        PsxRam.WriteU16(p + 0x26, (ushort)fieldB);
        PsxRam.WriteU16(p + 0x2a, (ushort)rowB);
        PsxRam.WriteU16(p + 0x2c, (ushort)rowA);
        PsxRam.WriteU16(p + 0x38, (ushort)p38);
        PsxRam.WriteU16(p + 0x3a, (ushort)y0);
        PsxRam.WriteU16(p + 0x40, (ushort)x0);
        PsxRam.WriteU16(p + 0x50, (ushort)p50);
    }

    internal static int Run()
    {
        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateVsExe();

        // CHECK 5a, done first because everything else is worthless if it fails: the two glyph
        // tables must read back as real image data, not zeros.
        int tableZero = PsxRam.ReadI32(unchecked((int)0x80083fb4));
        int tableNonZero = PsxRam.ReadI32(unchecked((int)0x80084124));
        Check(tableZero != 0, "PTR_DAT_80083FB4 lit zero: l image ne porte pas la table");
        Check(tableNonZero != 0, "PTR_DAT_80084124 lit zero: l image ne porte pas la table");
        Check(tableZero != tableNonZero, "les deux tables de glyphes pointent au meme endroit");

        // ------------------------------------------------------------------------------------
        // CASE 1: slot 3, both fields single-digit, degenerate geometry.
        // fieldB = 7 -> ones 7, tens 0 (the leading-zero arm).
        // fieldA = 4 -> ones 4, tens 0.
        // ------------------------------------------------------------------------------------
        Clear();
        Seed(slot: 3, fieldA: 4, fieldB: 7, rowA: 0, rowB: 0, x0: 100, y0: 60, p38: 500, p50: 500);
        BattleManager.BuildSlotDigitQuads(RecordBase, 3);

        int p3 = Slot(3);

        // 1. the glyph cell, field B's ones digit
        CheckEqual(7 * 0x18, PsxRam.ReadU8(p3 + 0x114), "B ones u0 a +0x114");
        CheckEqual(7 * 0x18, PsxRam.ReadU8(p3 + 0x104), "B ones u0 a +0x104");
        CheckEqual(7 * 0x18 + 0x18, PsxRam.ReadU8(p3 + 0x11c), "B ones u1 a +0x11C");
        CheckEqual(7 * 0x18 + 0x18, PsxRam.ReadU8(p3 + 0x10c), "B ones u1 a +0x10C");
        CheckEqual(0xe0, PsxRam.ReadU8(p3 + 0x10d), "B ones v a +0x10D");
        CheckEqual(0xe0, PsxRam.ReadU8(p3 + 0x105), "B ones v a +0x105");
        CheckEqual(0xff, PsxRam.ReadU8(p3 + 0x11d), "B ones v a +0x11D");
        CheckEqual(0xff, PsxRam.ReadU8(p3 + 0x115), "B ones v a +0x115");

        // and field A's ones digit
        CheckEqual(4 * 0x18, PsxRam.ReadU8(p3 + 0x164), "A ones u0 a +0x164");
        CheckEqual(4 * 0x18, PsxRam.ReadU8(p3 + 0x154), "A ones u0 a +0x154");
        CheckEqual(4 * 0x18 + 0x18, PsxRam.ReadU8(p3 + 0x16c), "A ones u1 a +0x16C");
        CheckEqual(4 * 0x18 + 0x18, PsxRam.ReadU8(p3 + 0x15c), "A ones u1 a +0x15C");
        CheckEqual(0xe0, PsxRam.ReadU8(p3 + 0x15d), "A ones v a +0x15D");
        CheckEqual(0xff, PsxRam.ReadU8(p3 + 0x16d), "A ones v a +0x16D");

        // 2. leading-zero suppression: the tens quads must be fully zeroed, v bytes all 0xE0
        foreach (int off in new[] { 0x13c, 0x12c, 0x144, 0x134 })
        {
            CheckEqual(0, PsxRam.ReadU8(p3 + off), $"B tens (zero) octet a +0x{off:X}");
        }

        foreach (int off in new[] { 0x135, 0x12d, 0x145, 0x13d })
        {
            CheckEqual(0xe0, PsxRam.ReadU8(p3 + off), $"B tens (zero) v a +0x{off:X}");
        }

        foreach (int off in new[] { 0x142, 0x13a, 0x132, 0x12a, 0x140, 0x130, 0x138, 0x128 })
        {
            CheckEqual(0, PsxRam.ReadU16(p3 + off), $"B tens (zero) demi-mot a +0x{off:X}");
        }

        foreach (int off in new[] { 0x18c, 0x17c, 0x194, 0x184 })
        {
            CheckEqual(0, PsxRam.ReadU8(p3 + off), $"A tens (zero) octet a +0x{off:X}");
        }

        foreach (int off in new[] { 0x192, 0x18a, 0x182, 0x17a, 0x190, 0x180, 0x188, 0x178 })
        {
            CheckEqual(0, PsxRam.ReadU16(p3 + off), $"A tens (zero) demi-mot a +0x{off:X}");
        }

        // 3. degenerate geometry: every x on the origin at +0x40, every y on the origin at +0x3A
        foreach (int off in new[] { 0x110, 0x100, 0x118, 0x108, 0x160, 0x150, 0x168, 0x158 })
        {
            CheckEqual(100, (short)PsxRam.ReadU16(p3 + off), $"x degenere a +0x{off:X}");
        }

        foreach (int off in new[] { 0x10a, 0x102, 0x11a, 0x112, 0x15a, 0x152, 0x16a, 0x162 })
        {
            CheckEqual(60, (short)PsxRam.ReadU16(p3 + off), $"y degenere a +0x{off:X}");
        }

        // 4. the 0x1C0 stride: neither neighbour was touched
        for (int neighbour = 2; neighbour <= 4; neighbour += 2)
        {
            int q = Slot(neighbour);
            bool clean = true;
            for (int off = 0x100; off < 0x196; off++)
            {
                if (PsxRam.ReadU8(q + off) != 0)
                {
                    clean = false;
                    break;
                }
            }

            Check(clean, $"le creneau {neighbour} a ete ecrase: la foulee 0x1C0 est fausse");
        }

        // ------------------------------------------------------------------------------------
        // CASE 2: the tens digit is non-zero, so the other arm runs.
        // fieldB = 47 -> ones 7, tens 4.
        // ------------------------------------------------------------------------------------
        Clear();
        Seed(slot: 3, fieldA: 4, fieldB: 47, rowA: 0, rowB: 0, x0: 100, y0: 60, p38: 500, p50: 500);
        BattleManager.BuildSlotDigitQuads(RecordBase, 3);

        CheckEqual(7 * 0x18, PsxRam.ReadU8(p3 + 0x114), "47: ones u0 inchange");
        CheckEqual(4 * 0x18, PsxRam.ReadU8(p3 + 0x13c), "47: tens u0 a +0x13C");
        CheckEqual(4 * 0x18, PsxRam.ReadU8(p3 + 0x12c), "47: tens u0 a +0x12C");
        CheckEqual(4 * 0x18 + 0x18, PsxRam.ReadU8(p3 + 0x144), "47: tens u1 a +0x144");
        CheckEqual(4 * 0x18 + 0x18, PsxRam.ReadU8(p3 + 0x134), "47: tens u1 a +0x134");

        // the non-zero arm puts 0xE0 in two v slots and 0xFF in the other two -- the zero arm put
        // 0xE0 in all four. This is the pair of arms an inverted test would swap.
        CheckEqual(0xe0, PsxRam.ReadU8(p3 + 0x135), "47: tens v a +0x135");
        CheckEqual(0xe0, PsxRam.ReadU8(p3 + 0x12d), "47: tens v a +0x12D");
        CheckEqual(0xff, PsxRam.ReadU8(p3 + 0x145), "47: tens v a +0x145");
        CheckEqual(0xff, PsxRam.ReadU8(p3 + 0x13d), "47: tens v a +0x13D");

        // ------------------------------------------------------------------------------------
        // CASE 3 (check 5b): a NON-ZERO separation makes the glyph rows scale the output, so the
        // choice between the two tables becomes observable. Same inputs but for the tens digit,
        // which is what selects the table: 7 takes PTR_DAT_80083FB4, 47 takes PTR_DAT_80084124.
        // ------------------------------------------------------------------------------------
        Clear();
        Seed(slot: 3, fieldA: 4, fieldB: 7, rowA: 0, rowB: 0, x0: 100, y0: 60, p38: 500, p50: 300);
        BattleManager.BuildSlotDigitQuads(RecordBase, 3);
        int viaZeroTable = PsxRam.ReadU16(p3 + 0x118) | (PsxRam.ReadU16(p3 + 0x11a) << 16);

        Clear();
        Seed(slot: 3, fieldA: 4, fieldB: 47, rowA: 0, rowB: 0, x0: 100, y0: 60, p38: 500, p50: 300);
        BattleManager.BuildSlotDigitQuads(RecordBase, 3);
        int viaOtherTable = PsxRam.ReadU16(p3 + 0x118) | (PsxRam.ReadU16(p3 + 0x11a) << 16);

        Check(
            viaZeroTable != viaOtherTable,
            "les deux tables de glyphes produisent la meme geometrie: la selection est morte, "
            + "ou les lignes lues sont nulles");

        // and with a real separation the coordinates must have moved off the origin at least once
        bool moved = false;
        foreach (int off in new[] { 0x110, 0x100, 0x118, 0x108 })
        {
            if ((short)PsxRam.ReadU16(p3 + off) != 100)
            {
                moved = true;
                break;
            }
        }

        Check(moved, "separation 200 non nulle mais aucune coordonnee n a bouge: les lignes lues sont nulles");

        Console.WriteLine(
            _failures == 0
                ? "BuildSlotDigitQuads @ 0x80058338 : toutes les invariantes tiennent."
                : $"BuildSlotDigitQuads @ 0x80058338 : {_failures} echec(s).");
        return _failures == 0 ? 0 : 1;
    }
}
