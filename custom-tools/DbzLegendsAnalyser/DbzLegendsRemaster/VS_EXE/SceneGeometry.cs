using System;

using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE SIX AFTER-IMAGE / TRAIL RECORDS AT 0x8008DA48, AND THE STATE MACHINE THAT DRIVES THEM.
//
// WHAT THIS CLUSTER IS. FighterMotion.FUN_8004fd24 calls FUN_800340a8 once per fighter per frame
// with seven arguments; FUN_800340a8 finds ONE record out of six by a composite key, steps a small
// state machine on it, and hands the record to one of six per-state workers, each of which ends by
// asking FUN_80033c64 to project the record's primitives through the GTE and link them into the
// ordering table. Nothing here is the fighter's own mesh: the fighter's mesh goes through
// AnimCmdMesh.TransformMeshPrimitives. This is a SECOND, self-contained geometry stream that hangs
// off the fighter's position triple.
//
// THE RECORD TABLE IS CLOSED BY ARITHMETIC, not by a guess. FUN_800340a8's search walks backwards
// from a base Ghidra prints as `iVar3 + -0x7ff74410` with iVar3 = 0xB610: as an unsigned 32-bit
// addition that is 0x8008BBF0 + 0xB610 = 0x80097200, and 0x80097200 = 0x8008DA48 + 5 * 0x1E58. So
// the base is DAT_8008da48, the stride is 0x1E58 and the count is six -- which is exactly the
// extent VS_EXE/FighterSetup.cs already closed from the other side (`memset(&DAT_8008da48, 0,
// 0xb610)` with 0xB610 = 6 * 0x1E58). Two independent readings landing on the same three numbers.
//
// THE STORAGE IS NOT REDECLARED HERE. FighterSetup.DAT_8008da48 is the one byte[] for that block;
// this file only needs its ADDRESS, so it carries an address constant and reaches the bytes through
// PsxRam, the way VS_EXE/AnimCmdAppearance.cs carries `Dat8008d420Address` without redeclaring
// VS_EXE_exe's field. An address constant is not a second declaration of the storage.
//
// THE RECORD LAYOUT, closed by cross-checking five functions that all agree. Every offset below is
// used by at least two of them, which is what makes the reading safe rather than plausible:
//
//   +0x00  ushort  an index into the four .data tables at 0x800811C0 / 0x800812F8 (layer 1) and
//                  0x8008125C / 0x80081568 (layer 2). Stride 4 for the first of each pair (a short)
//                  and 0x10 for the second (four ints).
//   +0x04  byte    THE STATE. Written as 0xFF, 0, 1, 2 or 3; FUN_800340a8 switches on the SIGNED
//                  byte plus one, so 0xFF selects case 0 and 3 selects case 4.
//   +0x06  byte    layer 0's first primitive index   +0x09  byte  layer 0's primitive count
//   +0x07  byte    layer 1's first primitive index   +0x0A  byte  layer 1's primitive count
//   +0x08  byte    layer 2's first primitive index   +0x0B  byte  layer 2's primitive count
//   +0x0C  int     a frame countdown, decremented by the workers; going negative ends the state.
//   +0x10  int     THE LAYER SELECTOR, 1 or 2. Every state in FUN_800340a8 branches on it to pick
//                  between the layer-1 worker and the layer-2 worker.
//   +0x14 / +0x2C / +0x44   layer 0's rotation / translation / scale slots
//   +0x1C / +0x34 / +0x54   layer 1's
//   +0x24 / +0x3C / +0x64   layer 2's
//   +0x4C  int     a value eased towards +0x5C in steps of 500 (case 3, layer-1 path only).
//   +0x74 + i*0x34          the primitive packets: 0x34 bytes each, four screen XY pairs at +0x08,
//                           +0x14, +0x20, +0x2C and four RGB triples at +0x04, +0x10, +0x1C, +0x28.
//                           0x34 bytes with GT4 field spacing is a POLY_GT4; the port does not need
//                           that name to be right, and does not use it.
//   +0x14C4 + i*0x18        the model-space vertex quad for primitive i: four triples of shorts.
//   +0x1E24 / +0x1E28 / +0x1E2C   int, three per-axis velocities (case 4 decays them by 300/frame)
//   +0x1E34 / +0x1E38 / +0x1E3C   int, three per-axis scales, 0x1000 = 1.0
//   +0x1E44 .. +0x1E4B      the PREVIOUS frame's copy of param_3's first four shorts, stamped by
//                           FUN_800340a8's own tail on every path that reaches it.
//   +0x1E4C / +0x1E4E       two 12-bit angles, yaw and pitch towards the movement delta.
//   +0x1E54  int            the PREVIOUS frame's param_5. 0x1E54 + 4 = 0x1E58, the stride: the
//                           record's last field ends exactly on its own size.
//
// THE THREE-LAYER READING IS WHAT MAKES FUN_80033c64 AND FUN_800322d0 LEGIBLE. Both take a small
// `param_1` and index the record with it: FUN_80033c64 reads rotation at `+ param_1 * 8 + 0x14`,
// translation at `+ param_1 * 8 + 0x2c` and scale at `+ param_1 * 0x10 + 0x44`, and FUN_800322d0
// reads the first index at `+ param_1 + 6` and the count at `+ param_1 + 9`. Substituting
// param_1 = 0, 1, 2 into all five expressions reproduces exactly the offsets the six workers write,
// with nothing left over and nothing missing. That is the whole evidence for calling it a layer
// index, and it is arithmetic rather than interpretation. What a "layer" IS on screen is NOT closed
// and is not named anywhere below.
//
// THE lwl/lwr AND swl/swr PAIRS ARE ALIGNED WORD ACCESSES. Ghidra renders this cluster's word
// copies as the generic unaligned-access algebra (`*(int *)(((int)p + 3U) - uVar) << (3 - uVar) * 8
// | ...`). On MIPS little-endian, `lwl rt,3(base)` + `lwr rt,0(base)` loads the whole word when
// base is 4-aligned, and `swl rt,3(base)` + `swr rt,0(base)` stores the whole word; the "seed"
// register value Ghidra ORs in is then completely dead. Every base involved IS 4-aligned:
// param_3 is the fighter's +0x114 triple (FighterMotion.cs closes that), and the record base
// 0x8008DA48 plus a multiple of 0x1E58 plus 0x34 / 0x3C / 0x1E44 is 4-aligned in every case. So
// each of those blocks is transliterated as one ReadI32 and one WriteI32, and the dead seed is
// dropped -- which is a rendering artefact removed, not work dropped.
//
// WIRING GAP, stated the way VS_EXE/FighterSetup.cs, VS_EXE/AnimVm.cs and VS_EXE/FileIo.cs already
// state it: PsxSdkBridges installs no PsxRam.AddressResolver row for VS.EXE, so every PsxRam access
// below currently resolves to nothing and answers zero. The four .data tables this file reads
// (0x800811C0, 0x800812F8, 0x8008125C, 0x80081568) live in the overlay image and have no backing in
// this port at all yet. Closing that means editing PsxSdkBridges and VS_EXE_exe, not this file.
internal static class SceneGeometry
{
    // =====================================================================================
    // ADDRESS CONSTANTS — no storage is declared here, only addresses
    // =====================================================================================

    // GHIDRA: DAT_8008da48 @ 0x8008DA48 (VS.EXE)
    // ADDRESS ONLY. The storage is VS_EXE/FighterSetup.cs's `DAT_8008da48` byte[0xB610], and that
    // file is its one declaration. This constant exists because FUN_800340a8 does pointer
    // arithmetic on the block's ADDRESS (`&DAT_8008da48 + i * 0x1e58`) and hands the result to five
    // callees as a pointer, so the record has to be an address here, not an array index.
    private const int Dat8008da48Address = unchecked((int)0x8008DA48);

    // The literal Ghidra prints as `-0x7ff74410` in FUN_800340a8's search loop, kept as the image's
    // own constant rather than folded into the base. 0x8008BBF0 + 0xB610 = 0x80097200 = record 5.
    private const int SearchBase8008bbf0 = unchecked((int)0x8008BBF0);

    // GHIDRA: DAT_800811c0 @ 0x800811C0 (VS.EXE)
    // ADDRESS ONLY, no storage: a .data table of shorts read at stride 4 and indexed by the
    // record's +0x00. Layer 1's angular step. Read out of the image the first eight entries are
    // 0, 0x1E, 0x1E, 0x1E, 0x12, 0x12, 0x14, 0x1E -- recorded as an observation; nothing below
    // depends on those values.
    private const int Dat800811c0Address = unchecked((int)0x800811C0);

    // GHIDRA: DAT_800812f8 / DAT_800812fc / DAT_80081300 / DAT_80081304 @ 0x800812F8 (VS.EXE)
    // ADDRESS ONLY: one .data table of four-int rows, stride 0x10, indexed by the record's +0x00.
    // Layer 1's scale quad. Four separate constants because the original addresses each column
    // through its own `lui`/`addiu` pair rather than through a row pointer.
    private const int Dat800812f8Address = unchecked((int)0x800812F8);

    private const int Dat800812fcAddress = unchecked((int)0x800812FC);

    private const int Dat80081300Address = unchecked((int)0x80081300);

    private const int Dat80081304Address = unchecked((int)0x80081304);

    // GHIDRA: DAT_8008125c @ 0x8008125C (VS.EXE)
    // ADDRESS ONLY: layer 2's counterpart of DAT_800811c0, same shape, same indexing.
    private const int Dat8008125cAddress = unchecked((int)0x8008125C);

    // GHIDRA: DAT_80081568 / DAT_8008156c / DAT_80081570 / DAT_80081574 @ 0x80081568 (VS.EXE)
    // ADDRESS ONLY: layer 2's counterpart of the 0x800812F8 table, same shape, same indexing.
    private const int Dat80081568Address = unchecked((int)0x80081568);

    private const int Dat8008156cAddress = unchecked((int)0x8008156C);

    private const int Dat80081570Address = unchecked((int)0x80081570);

    private const int Dat80081574Address = unchecked((int)0x80081574);

    // GHIDRA: DAT_8008d420 @ 0x8008D420 (VS.EXE)
    // ADDRESS ONLY, and the same caveat VS_EXE/AnimCmdAppearance.cs and VS_EXE/BattleManager.cs
    // already record at their own ordering-table sites: the active DRAWENV address is a PRIVATE C#
    // field in VS_EXE/VS_EXE_exe.cs, not PsxRam-backed, so this read answers zero until that field
    // is either made reachable or backed at 0x8008D420. Reported upward rather than fixed here,
    // because the fix belongs to that file's owner.
    private const int Dat8008d420Address = unchecked((int)0x8008D420);

    // =====================================================================================
    // C# BRIDGES
    // =====================================================================================

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original passes SVECTOR* straight to the SDK, whose C# entry points take
    // SVECTOR objects, so the pointer is dereferenced here. Identical in shape and in reasoning to
    // VS_EXE/AnimCmdMesh.cs's own ReadSvector: every consumer (RotMatrix, RotAverage4) only READS
    // the vector -- it reaches the GTE through ldv3/ldv0, which load and never store back.
    private static LibGte.SVECTOR ReadSvectorFromRam(int psxAddress) => new()
    {
        vx = (short)PsxRam.ReadU16(psxAddress),
        vy = (short)PsxRam.ReadU16(psxAddress + 2),
        vz = (short)PsxRam.ReadU16(psxAddress + 4),
        pad = (short)PsxRam.ReadU16(psxAddress + 6),
    };

    // JUSTIFICATION: C# language bridge only
    // RELATION: same bridge, for the four SVECTORs FUN_80033c64 stages on its own STACK rather than
    // in PSX RAM. The stack block is modelled as a byte[] so that RotTransPers -- whose C# entry
    // point is buffer-and-offset -- and RotAverage4 -- whose C# entry point is SVECTOR objects --
    // can both be fed from the one piece of storage the console has there.
    private static LibGte.SVECTOR ReadSvectorFromBuffer(byte[] buffer, int offset) => new()
    {
        vx = BitConverter.ToInt16(buffer, offset),
        vy = BitConverter.ToInt16(buffer, offset + 2),
        vz = BitConverter.ToInt16(buffer, offset + 4),
        pad = BitConverter.ToInt16(buffer, offset + 6),
    };

    // =====================================================================================
    // THE ENTRY POINT
    // =====================================================================================

    // GHIDRA: FUN_800340a8 @ 0x800340A8 (VS.EXE)
    // 1764 bytes, 0x800340A8..0x8003478B. One caller: FighterMotion.FUN_8004fd24 at 0x8004FFCC,
    // whose own note already lists all seven arguments verified against the marshalling.
    //
    // CLASH DECLARED RATHER THAN HIDDEN. VS_EXE/FighterMotion.cs carries a PRIVATE, deliberately
    // empty stub for this same address, written when nothing had entered this address range. That
    // stub and this body are now two declarations of 0x800340A8, which is exactly the defect
    // custom-tools/scripts/check_function_addresses.py exists to catch -- and C# would bind
    // FighterMotion's own unqualified call to its own private stub, so the stub would silently win.
    // Removing it and repointing that call at SceneGeometry.FUN_800340a8 can only be done in
    // FighterMotion.cs, which this slice is not allowed to edit. It is reported upward instead.
    //
    // THE SEARCH KEY is `param_1 & 0xff | (param_2 & 0xff) << 0x10` compared against the record's
    // own first word. param_1 is the halfword at the running task node and param_2 the fighter's
    // slot index, so a record belongs to one (task, slot) pair. The scan runs record 5 down to
    // record 0 and answers -1 when none matches -- the ONLY early return in the function besides
    // state 0 (case 1 of the switch).
    internal static uint FUN_800340a8(uint param_1, uint param_2, int param_3, int param_4,
        byte param_5, int param_6, int param_7)
    {
        bool bVar1;
        int puVar2;
        int iVar3;
        int lVar4;
        int lVar5;
        uint uVar6;
        uint uVar7;
        int iVar8;
        uint uVar9;

        // Ghidra's uVar10/uVar11/uVar12 -- the two arguments the switch hands FUN_80033c64, plus a
        // third the case-3 body reuses as a table temporary. Initialised here only because C#
        // demands definite assignment before the call below; on the console they are registers the
        // default arm simply leaves stale, and the default arm jumps PAST that call.
        uint uVar10 = 0;
        uint uVar11 = 0;
        uint uVar12;

        // Initialised for the same C#-only reason: the search below assigns it on both paths, but
        // the `goto` out of the do-while defeats the compiler's flow analysis.
        int puVar13 = 0;
        uint uVar14;

        iVar8 = 6;
        uVar14 = param_5;
        iVar3 = 0xb610;
        do
        {
            iVar8 = iVar8 + -1;
            if (iVar8 < 0)
            {
                puVar13 = 0;
                goto LAB_80034148;
            }

            puVar2 = iVar3 + SearchBase8008bbf0;
            iVar3 = iVar3 + -0x1e58;
        }
        while (PsxRam.ReadI32(puVar2) != (int)((param_1 & 0xff) | ((param_2 & 0xff) << 0x10)));

        puVar13 = Dat8008da48Address + iVar8 * 0x1e58;

    LAB_80034148:
        if (puVar13 == 0)
        {
            goto switchD_80034438_caseD_1;
        }

        // The re-arm. Only when the caller says param_7 (FighterMotion's local_c, the attack-state
        // flag), the record is on layer 2, and the state byte is 2 or 3 -- `(byte)(state - 2) < 2`
        // is UNSIGNED byte arithmetic, so it is exactly the set {2, 3}.
        bVar1 = false;
        if ((param_7 != 0) && (PsxRam.ReadI32(puVar13 + 0x10) == 2))
        {
            bVar1 = (byte)(PsxRam.ReadU8(puVar13 + 4) - 2) < 2;
        }

        if (bVar1)
        {
            PsxRam.WriteU8(puVar13 + 4, 3);
            PsxRam.WriteU16(puVar13 + 0x1e34, 0x1000);
            PsxRam.WriteU16(puVar13 + 0x1e36, 0);
            PsxRam.WriteU16(puVar13 + 0x1e38, 0x1000);
            PsxRam.WriteU16(puVar13 + 0x1e3a, 0);
            PsxRam.WriteU16(puVar13 + 0x1e3c, 0x1000);
            PsxRam.WriteU16(puVar13 + 0x1e3e, 0);
            iVar3 = Kernel.rand();
            PsxRam.WriteI32(puVar13 + 0x1e24, iVar3 % 600 + 700);
            iVar3 = Kernel.rand();
            PsxRam.WriteI32(puVar13 + 0x1e28, iVar3 % 600 + 700);
            iVar3 = Kernel.rand();
            PsxRam.WriteI32(puVar13 + 0x1e2c, iVar3 % 600 + 300);
        }

        // Ghidra prints this as one `if` with a comma expression: `if ((uVar14 - 2 < 9) ||
        // (uVar14 == 0x1c) || (iVar3 = 2, uVar14 == 0x2a)) { iVar3 = 1; }`. Unrolled, that assigns
        // 1 on any of the three tests and 2 otherwise -- the comma's `iVar3 = 2` only ever survives
        // when the third test also fails. The subtraction is UNSIGNED, so `uVar14 - 2 < 9` is the
        // range 2..10 and NOT a signed window around zero.
        if ((uVar14 - 2 < 9) || (uVar14 == 0x1c) || (uVar14 == 0x2a))
        {
            iVar3 = 1;
        }
        else
        {
            iVar3 = 2;
        }

        if (param_6 == 0)
        {
            // `1 < (byte)((char)state + 1)`: the state byte read SIGNED, plus one, truncated to a
            // byte, compared unsigned. 0xFF gives 0 and 0 gives 1, so this is "state is 1, 2 or 3".
            if (1 < (byte)((sbyte)PsxRam.ReadU8(puVar13 + 4) + 1))
            {
                uVar10 = 1;
                if (PsxRam.ReadI32(puVar13 + 0x10) == 2)
                {
                    uVar10 = 2;
                }

                PsxRam.WriteU8(puVar13 + 4, 0xff);
                PsxRam.WriteU16(puVar13 + 0x0c, 10);
                PsxRam.WriteU16(puVar13 + 0x0e, 0);
                FUN_800322d0((int)uVar10, puVar13, 0x80, 0x80, 0x80);
            }
        }
        else if (iVar3 == 1)
        {
            if (((sbyte)PsxRam.ReadU8(puVar13 + 4) == 0) || (PsxRam.ReadI32(puVar13 + 0x10) == 2))
            {
                PsxRam.WriteU8(puVar13 + 4, 1);
                PsxRam.WriteU16(puVar13 + 0x0c, 3);
                PsxRam.WriteU16(puVar13 + 0x0e, 0);
                PsxRam.WriteU16(puVar13 + 0x4c, 200);
                PsxRam.WriteU16(puVar13 + 0x4e, 0);
                PsxRam.WriteU16(puVar13 + 0x10, 1);
                PsxRam.WriteU16(puVar13 + 0x12, 0);
            }

            // Five (this frame's param_5, last frame's param_5) pairs, each firing when the value
            // has just CHANGED TO one of 2, 10, 6, 0x2A, 0x1C. The stored copy is +0x1E54, which
            // this function's own tail stamps. Reproduced in the original's order.
            else if ((((uVar14 == 2) && (PsxRam.ReadI32(puVar13 + 0x1e54) != 2))
                      || ((uVar14 == 10) && (PsxRam.ReadI32(puVar13 + 0x1e54) != 10)))
                     || (((uVar14 == 6) && (PsxRam.ReadI32(puVar13 + 0x1e54) != 6))
                         || (((uVar14 == 0x2a) && (PsxRam.ReadI32(puVar13 + 0x1e54) != 0x2a))
                             || ((uVar14 == 0x1c) && (PsxRam.ReadI32(puVar13 + 0x1e54) != 0x1c)))))
            {
                PsxRam.WriteU8(puVar13 + 4, 1);
                PsxRam.WriteU16(puVar13 + 0x0c, 3);
                PsxRam.WriteU16(puVar13 + 0x0e, 0);
                PsxRam.WriteU16(puVar13 + 0x4c, 200);
                PsxRam.WriteU16(puVar13 + 0x4e, 0);
                PsxRam.WriteU16(puVar13 + 0x10, 1);
                PsxRam.WriteU16(puVar13 + 0x12, 0);
            }
        }

        // The layer-2 arm. It deliberately does NOT write +0x4C/+0x4E, unlike both layer-1 arms
        // above; that asymmetry is the original's and is not evened out here.
        else if ((iVar3 == 2)
                 && (((sbyte)PsxRam.ReadU8(puVar13 + 4) == 0) || (PsxRam.ReadI32(puVar13 + 0x10) == 1)))
        {
            PsxRam.WriteU8(puVar13 + 4, 1);
            PsxRam.WriteU16(puVar13 + 0x0c, 3);
            PsxRam.WriteU16(puVar13 + 0x0e, 0);
            PsxRam.WriteU16(puVar13 + 0x10, 2);
            PsxRam.WriteU16(puVar13 + 0x12, 0);
        }

        // `(int)(((byte)state + 1) * 0x1000000) >> 0x18` -- sign-extend the low byte of state + 1.
        // 0xFF -> 0, 0 -> 1, 1 -> 2, 2 -> 3, 3 -> 4; anything else takes the default arm.
        iVar3 = (sbyte)(byte)(PsxRam.ReadU8(puVar13 + 4) + 1);

        switch (iVar3)
        {
            case 0:
                // Ghidra prints `*(int *)(puVar13 + 8)` on a `ushort *`, which is BYTE offset 0x10
                // -- the layer selector, the same field the six other tests in this function read.
                // Transcribing that 8 as a byte offset would have read the countdown's high half
                // instead, and the arm would have picked the wrong worker for the whole of state 0.
                if (PsxRam.ReadI32(puVar13 + 0x10) == 2)
                {
                    uVar10 = FUN_800338e0(puVar13, param_4, param_3);
                    uVar11 = 2;
                }
                else
                {
                    uVar10 = FUN_80033564(puVar13, param_4, param_3);
                    uVar11 = 1;
                }

                break;

            case 1:
                goto switchD_80034438_caseD_1;

            case 2:
                if (PsxRam.ReadI32(puVar13 + 0x10) == 2)
                {
                    uVar10 = FUN_80032c24(puVar13, param_4, param_3);
                    uVar11 = 2;
                }
                else
                {
                    uVar10 = FUN_800326ac(puVar13, param_4, param_3);
                    uVar11 = 1;
                }

                break;

            case 3:
                // The two angles are recomputed only when the position triple has MOVED. The three
                // comparisons are of 16-bit values on both sides, so the fact that Ghidra prints
                // the first one signed and the next two unsigned cannot change any of them: two
                // 16-bit patterns are equal or they are not. The values compared are the caller's
                // triple against this function's own +0x1E44 copy of last frame's triple.
                //
                // THE ONE SHAPE CHANGE IN THIS FUNCTION, and it is C#'s rule, not a decision.
                // Ghidra's `goto LAB_800344c4` jumps from the THEN branch INTO the ELSE branch, and
                // C# forbids a goto that enters a block. The label is therefore lifted to this
                // scope and the "skip" path is spelled as its own forward goto. The three reachable
                // paths and the order of every read and write inside them are unchanged: position
                // equal on all three axes -> skip; equal on X but not on Y or Z -> recompute lVar4
                // then run the angle block; X different -> run the angle block with the lVar4
                // computed above.
                lVar4 = (short)PsxRam.ReadU16(param_3) - (short)PsxRam.ReadU16(puVar13 + 0x1e44);
                if ((short)PsxRam.ReadU16(puVar13 + 0x1e44) == (short)PsxRam.ReadU16(param_3))
                {
                    if (((short)PsxRam.ReadU16(puVar13 + 0x1e46) != (short)PsxRam.ReadU16(param_3 + 2))
                        || ((short)PsxRam.ReadU16(puVar13 + 0x1e48) != (short)PsxRam.ReadU16(param_3 + 4)))
                    {
                        lVar4 = (short)PsxRam.ReadU16(param_3) - (short)PsxRam.ReadU16(puVar13 + 0x1e44);
                        goto LAB_800344c4;
                    }

                    goto LAB_afterAngles_800344c4;
                }

            LAB_800344c4:
                iVar3 = (short)PsxRam.ReadU16(param_3 + 2) - (short)PsxRam.ReadU16(puVar13 + 0x1e46);
                iVar8 = (short)PsxRam.ReadU16(param_3 + 4) - (short)PsxRam.ReadU16(puVar13 + 0x1e48);
                lVar5 = LibGte.ratan2(-iVar3, iVar8);
                PsxRam.WriteU16(puVar13 + 0x1e4c, (ushort)lVar5);
                lVar5 = LibGte.SquareRoot0(iVar8 * iVar8 + iVar3 * iVar3);
                lVar4 = LibGte.ratan2(lVar4, lVar5);
                PsxRam.WriteU16(puVar13 + 0x1e4e, (ushort)lVar4);

            LAB_afterAngles_800344c4:
                _ = lVar4;

                if (PsxRam.ReadI32(puVar13 + 0x10) == 2)
                {
                    uVar10 = FUN_80033020(puVar13, param_4, param_3);
                    uVar11 = 2;
                }
                else
                {
                    // The layer-1 path of state 3 is the only place in this cluster that renders
                    // TWO layers in one frame: it fills layer 1, submits layer 1 through
                    // FUN_80033c64(1, ...), then copies layer 1 down into layer 0 and lets the
                    // switch's shared tail submit layer 0 through FUN_80033c64(0, ...).
                    PsxRam.WriteU16(puVar13 + 0x1c, PsxRam.ReadU16(puVar13 + 0x1e4c));
                    PsxRam.WriteU16(puVar13 + 0x1e, (ushort)(PsxRam.ReadU16(puVar13 + 0x1e4e) & 0xfff));

                    uVar6 = (uint)(AnimVm.DAT_800b305a & 1);
                    if ((AnimVm.DAT_800b305a & 1) == 0)
                    {
                        uVar6 = (uint)(PsxRam.ReadU16(puVar13 + 0x20) - 0xa0);
                        PsxRam.WriteU16(puVar13 + 0x20, (ushort)uVar6);
                    }

                    // Aligned word copy -- see the file header on lwl/lwr. param_3[0..3] into the
                    // layer-1 translation slot.
                    uVar7 = (uint)PsxRam.ReadI32(param_3);
                    uVar9 = (uint)PsxRam.ReadI32(param_3 + 4);
                    PsxRam.WriteI32(puVar13 + 0x34, (int)uVar7);
                    PsxRam.WriteI32(puVar13 + 0x38, (int)uVar9);

                    iVar3 = PsxRam.ReadU16(puVar13) * 0x10;
                    PsxRam.WriteU16(puVar13 + 0x36, (ushort)((short)PsxRam.ReadU16(puVar13 + 0x36)
                        - (short)PsxRam.ReadU16(Dat800811c0Address + PsxRam.ReadU16(puVar13) * 4)));

                    // uVar10/uVar11/uVar12 are reused as table temporaries here exactly as the
                    // original reuses those registers; both are overwritten again at the end of
                    // this arm, before the shared tail reads them.
                    uVar10 = (uint)PsxRam.ReadI32(Dat800812fcAddress + iVar3);
                    uVar11 = (uint)PsxRam.ReadI32(Dat80081300Address + iVar3);
                    uVar12 = (uint)PsxRam.ReadI32(Dat80081304Address + iVar3);
                    PsxRam.WriteI32(puVar13 + 0x54, PsxRam.ReadI32(Dat800812f8Address + iVar3));
                    PsxRam.WriteI32(puVar13 + 0x58, (int)uVar10);
                    PsxRam.WriteI32(puVar13 + 0x5c, (int)uVar11);
                    PsxRam.WriteI32(puVar13 + 0x60, (int)uVar12);

                    FUN_80033c64(1, puVar13, 0);

                    // Layer 1 down into layer 0: rotation 0x1C -> 0x14, translation 0x34 -> 0x2C,
                    // and only the FIRST TWO words of the scale quad, 0x54/0x58 -> 0x44/0x48. The
                    // third and fourth are not copied; that is the original's and is left alone.
                    uVar6 = (uint)PsxRam.ReadI32(puVar13 + 0x1c);
                    uVar9 = (uint)PsxRam.ReadI32(puVar13 + 0x20);
                    PsxRam.WriteI32(puVar13 + 0x14, (int)uVar6);
                    PsxRam.WriteI32(puVar13 + 0x18, (int)uVar9);
                    uVar6 = (uint)PsxRam.ReadI32(puVar13 + 0x34);
                    uVar9 = (uint)PsxRam.ReadI32(puVar13 + 0x38);
                    PsxRam.WriteI32(puVar13 + 0x2c, (int)uVar6);
                    PsxRam.WriteI32(puVar13 + 0x30, (int)uVar9);
                    PsxRam.WriteI32(puVar13 + 0x44, PsxRam.ReadI32(puVar13 + 0x54));
                    PsxRam.WriteI32(puVar13 + 0x48, PsxRam.ReadI32(puVar13 + 0x58));

                    // Ghidra folds this into one `&&` with a comma: the `+ 500` is computed inside
                    // the DAT_800b305a arm and before the inequality test, which is why it stays
                    // where it is rather than moving into the body below it.
                    if ((AnimVm.DAT_800b305a & 1) == 0)
                    {
                        iVar3 = PsxRam.ReadI32(puVar13 + 0x4c) + 500;
                        if (PsxRam.ReadI32(puVar13 + 0x4c) != PsxRam.ReadI32(puVar13 + 0x5c))
                        {
                            PsxRam.WriteI32(puVar13 + 0x4c, iVar3);
                            if (PsxRam.ReadI32(puVar13 + 0x5c) < iVar3)
                            {
                                PsxRam.WriteI32(puVar13 + 0x4c, PsxRam.ReadI32(puVar13 + 0x5c));
                            }
                        }
                    }

                    uVar11 = 0;
                    uVar10 = 0;
                }

                break;

            case 4:
                uVar10 = FUN_80033210(puVar13, param_4, param_3, uVar14);
                uVar11 = 2;
                break;

            default:
                goto switchD_80034438_default;
        }

        FUN_80033c64((int)uVar11, puVar13, (int)uVar10);

    switchD_80034438_default:

        // The tail every path but the two early returns reaches. Ghidra seeds these two aligned
        // word loads with the (v0, v1) pair FUN_80033c64 left behind -- FUN_80033c64 returns void,
        // so that pair is garbage, and it is dead for the reason the file header gives.
        uVar7 = (uint)PsxRam.ReadI32(param_3);
        uVar9 = (uint)PsxRam.ReadI32(param_3 + 4);
        PsxRam.WriteI32(puVar13 + 0x1e44, (int)uVar7);
        PsxRam.WriteI32(puVar13 + 0x1e48, (int)uVar9);
        PsxRam.WriteI32(puVar13 + 0x1e54, (int)uVar14);
        return 0;

    switchD_80034438_caseD_1:
        return 0xffffffff;
    }

    // =====================================================================================
    // THE SIX PER-STATE WORKERS
    // =====================================================================================

    // GHIDRA: FUN_800326ac @ 0x800326AC (VS.EXE)
    // 1400 bytes, 0x800326AC..0x80032C23. One caller: FUN_800340a8's state-2 layer-1 arm.
    //
    // THREE PATHS, and which one runs is decided before anything is written:
    //   * the VM is globally paused (AnimVm.DAT_800b305a bit 0) -- refresh the transform from the
    //     tables and re-tint every primitive, return 1;
    //   * the countdown at +0x0C is still non-negative after its decrement -- the same refresh plus
    //     a 0xA0 step on the third rotation component, return 1;
    //   * the countdown has gone negative -- advance the state byte to 2, recompute the two angles
    //     towards the caller's position, install them as layer 1's rotation, force every primitive
    //     to a flat 0x60 grey and return 0.
    // The first two paths are byte-for-byte the same body except for the three writes at +0x1C/
    // +0x1E/+0x20 at their head; the original emits both copies and so does this, because merging
    // them would be exactly the "do not merge functions to make a cleaner API" the mandate forbids.
    //
    // THE TINT IS A RAMP OVER THE COUNTDOWN. `(3 - countdown) * 0x20 + 0x40` truncated to a signed
    // char, written into all twelve RGB bytes of each primitive in the layer-1 range. It is
    // recomputed INSIDE the loop on every iteration although it does not depend on the iteration --
    // the original does that, and hoisting it would be an optimisation.
    internal static uint FUN_800326ac(int param_1, int param_2, int param_3)
    {
        ushort uVar1;
        byte cVar3;
        uint uVar4;
        uint uVar5;
        int iVar6;
        int lVar7;
        uint uVar8;
        uint uVar9;
        uint uVar10;
        int puVar11;
        int iVar12;
        int x;
        int uVar13;
        int uVar14;
        int uVar15;

        _ = param_2;

        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            PsxRam.WriteU16(param_1 + 0x1c, 0);
            PsxRam.WriteU16(param_1 + 0x1e, 0);

            uVar5 = (uint)PsxRam.ReadI32(param_3);
            uVar9 = (uint)PsxRam.ReadI32(param_3 + 4);
            PsxRam.WriteI32(param_1 + 0x34, (int)uVar5);
            PsxRam.WriteI32(param_1 + 0x38, (int)uVar9);

            uVar4 = PsxRam.ReadU8(param_1 + 7);
            uVar9 = uVar4 + PsxRam.ReadU8(param_1 + 0x0a);
            PsxRam.WriteU16(param_1 + 0x36, (ushort)((short)PsxRam.ReadU16(param_1 + 0x36)
                - (short)PsxRam.ReadU16(Dat800811c0Address + PsxRam.ReadU16(param_1) * 4)));
            PsxRam.WriteI32(param_1 + 0x54, (3 - PsxRam.ReadI32(param_1 + 0x0c)) * 500
                + PsxRam.ReadI32(Dat800812f8Address + PsxRam.ReadU16(param_1) * 0x10));
            PsxRam.WriteI32(param_1 + 0x58, (3 - PsxRam.ReadI32(param_1 + 0x0c)) * 500
                + PsxRam.ReadI32(Dat800812fcAddress + PsxRam.ReadU16(param_1) * 0x10));
            PsxRam.WriteI32(param_1 + 0x5c, (3 - PsxRam.ReadI32(param_1 + 0x0c)) * 500
                + PsxRam.ReadI32(Dat80081300Address + PsxRam.ReadU16(param_1) * 0x10));

            if (uVar9 <= uVar4)
            {
                return 1;
            }

            puVar11 = param_1 + (int)(uVar4 * 0x34);
            do
            {
                uVar4 = uVar4 + 1;
                cVar3 = (byte)((3 - (sbyte)PsxRam.ReadI32(param_1 + 0x0c)) * 0x20 + 0x40);
                PsxRam.WriteU8(puVar11 + 0x78, cVar3);
                PsxRam.WriteU8(puVar11 + 0x79, cVar3);
                PsxRam.WriteU8(puVar11 + 0x7a, cVar3);
                PsxRam.WriteU8(puVar11 + 0x84, cVar3);
                PsxRam.WriteU8(puVar11 + 0x85, cVar3);
                PsxRam.WriteU8(puVar11 + 0x86, cVar3);
                PsxRam.WriteU8(puVar11 + 0x90, cVar3);
                PsxRam.WriteU8(puVar11 + 0x91, cVar3);
                PsxRam.WriteU8(puVar11 + 0x92, cVar3);
                PsxRam.WriteU8(puVar11 + 0x9c, cVar3);
                PsxRam.WriteU8(puVar11 + 0x9d, cVar3);
                PsxRam.WriteU8(puVar11 + 0x9e, cVar3);
                puVar11 = puVar11 + 0x34;
            }
            while ((int)uVar4 < (int)uVar9);

            return 1;
        }

        iVar6 = PsxRam.ReadI32(param_1 + 0x0c);
        PsxRam.WriteI32(param_1 + 0x0c, iVar6 + -1);
        if (-1 < iVar6 + -1)
        {
            uVar1 = PsxRam.ReadU16(param_1 + 0x20);
            PsxRam.WriteU16(param_1 + 0x1c, 0);
            PsxRam.WriteU16(param_1 + 0x1e, 0);
            PsxRam.WriteU16(param_1 + 0x20, (ushort)(uVar1 - 0xa0));

            uVar5 = (uint)PsxRam.ReadI32(param_3);
            uVar9 = (uint)PsxRam.ReadI32(param_3 + 4);
            PsxRam.WriteI32(param_1 + 0x34, (int)uVar5);
            PsxRam.WriteI32(param_1 + 0x38, (int)uVar9);

            uVar4 = PsxRam.ReadU8(param_1 + 7);
            uVar9 = uVar4 + PsxRam.ReadU8(param_1 + 0x0a);
            PsxRam.WriteU16(param_1 + 0x36, (ushort)((short)PsxRam.ReadU16(param_1 + 0x36)
                - (short)PsxRam.ReadU16(Dat800811c0Address + PsxRam.ReadU16(param_1) * 4)));
            PsxRam.WriteI32(param_1 + 0x54, (3 - PsxRam.ReadI32(param_1 + 0x0c)) * 500
                + PsxRam.ReadI32(Dat800812f8Address + PsxRam.ReadU16(param_1) * 0x10));
            PsxRam.WriteI32(param_1 + 0x58, (3 - PsxRam.ReadI32(param_1 + 0x0c)) * 500
                + PsxRam.ReadI32(Dat800812fcAddress + PsxRam.ReadU16(param_1) * 0x10));
            PsxRam.WriteI32(param_1 + 0x5c, (3 - PsxRam.ReadI32(param_1 + 0x0c)) * 500
                + PsxRam.ReadI32(Dat80081300Address + PsxRam.ReadU16(param_1) * 0x10));

            if (uVar9 <= uVar4)
            {
                return 1;
            }

            puVar11 = param_1 + (int)(uVar4 * 0x34);
            do
            {
                uVar4 = uVar4 + 1;
                cVar3 = (byte)((3 - (sbyte)PsxRam.ReadI32(param_1 + 0x0c)) * 0x20 + 0x40);
                PsxRam.WriteU8(puVar11 + 0x78, cVar3);
                PsxRam.WriteU8(puVar11 + 0x79, cVar3);
                PsxRam.WriteU8(puVar11 + 0x7a, cVar3);
                PsxRam.WriteU8(puVar11 + 0x84, cVar3);
                PsxRam.WriteU8(puVar11 + 0x85, cVar3);
                PsxRam.WriteU8(puVar11 + 0x86, cVar3);
                PsxRam.WriteU8(puVar11 + 0x90, cVar3);
                PsxRam.WriteU8(puVar11 + 0x91, cVar3);
                PsxRam.WriteU8(puVar11 + 0x92, cVar3);
                PsxRam.WriteU8(puVar11 + 0x9c, cVar3);
                PsxRam.WriteU8(puVar11 + 0x9d, cVar3);
                PsxRam.WriteU8(puVar11 + 0x9e, cVar3);
                puVar11 = puVar11 + 0x34;
            }
            while ((int)uVar4 < (int)uVar9);

            return 1;
        }

        PsxRam.WriteU8(param_1 + 4, 2);
        iVar6 = (short)PsxRam.ReadU16(param_3);
        uVar9 = 0;
        if ((short)PsxRam.ReadU16(param_1 + 0x1e44) == iVar6)
        {
            if ((short)PsxRam.ReadU16(param_1 + 0x1e46) == (short)PsxRam.ReadU16(param_3 + 2))
            {
                uVar4 = (uint)(short)PsxRam.ReadU16(param_1 + 0x1e48);
                uVar9 = (uint)(short)PsxRam.ReadU16(param_3 + 4);
                if (uVar4 == uVar9)
                {
                    goto LAB_80032938;
                }
            }

            iVar6 = (short)PsxRam.ReadU16(param_3);
        }

        uVar1 = PsxRam.ReadU16(param_1 + 0x1e44);
        iVar12 = (short)PsxRam.ReadU16(param_3 + 2) - (short)PsxRam.ReadU16(param_1 + 0x1e46);
        x = (short)PsxRam.ReadU16(param_3 + 4) - (short)PsxRam.ReadU16(param_1 + 0x1e48);
        lVar7 = LibGte.ratan2(-iVar12, x);
        PsxRam.WriteU16(param_1 + 0x1e4c, (ushort)lVar7);
        uVar4 = (uint)(x * x);
        lVar7 = LibGte.SquareRoot0((int)uVar4 + iVar12 * iVar12);
        uVar9 = (uint)LibGte.ratan2(iVar6 - (short)uVar1, lVar7);
        PsxRam.WriteU16(param_1 + 0x1e4e, (ushort)uVar9);

    LAB_80032938:
        _ = uVar9;

        // Two aligned word copies: the angle pair at +0x1E4C/+0x1E50 becomes layer 1's rotation
        // slot at +0x1C/+0x20. Reached with uVar9 stale when the position had not moved -- dead,
        // because it is only an lwl seed.
        uVar8 = (uint)PsxRam.ReadI32(param_1 + 0x1e4c);
        uVar10 = (uint)PsxRam.ReadI32(param_1 + 0x1e50);
        PsxRam.WriteI32(param_1 + 0x1c, (int)uVar8);
        PsxRam.WriteI32(param_1 + 0x20, (int)uVar10);

        uVar5 = (uint)PsxRam.ReadI32(param_3);
        uVar9 = (uint)PsxRam.ReadI32(param_3 + 4);
        PsxRam.WriteI32(param_1 + 0x34, (int)uVar5);
        PsxRam.WriteI32(param_1 + 0x38, (int)uVar9);

        iVar6 = PsxRam.ReadU16(param_1) * 0x10;
        PsxRam.WriteU16(param_1 + 0x36, (ushort)((short)PsxRam.ReadU16(param_1 + 0x36)
            - (short)PsxRam.ReadU16(Dat800811c0Address + PsxRam.ReadU16(param_1) * 4)));
        uVar13 = PsxRam.ReadI32(Dat800812fcAddress + iVar6);
        uVar14 = PsxRam.ReadI32(Dat80081300Address + iVar6);
        uVar15 = PsxRam.ReadI32(Dat80081304Address + iVar6);
        PsxRam.WriteI32(param_1 + 0x54, PsxRam.ReadI32(Dat800812f8Address + iVar6));
        PsxRam.WriteI32(param_1 + 0x58, uVar13);
        PsxRam.WriteI32(param_1 + 0x5c, uVar14);
        PsxRam.WriteI32(param_1 + 0x60, uVar15);

        uVar4 = PsxRam.ReadU8(param_1 + 7);
        uVar9 = uVar4 + PsxRam.ReadU8(param_1 + 0x0a);
        if (uVar4 < uVar9)
        {
            param_1 = param_1 + (int)(uVar4 * 0x34);
            do
            {
                PsxRam.WriteU8(param_1 + 0x78, 0x60);
                PsxRam.WriteU8(param_1 + 0x79, 0x60);
                PsxRam.WriteU8(param_1 + 0x7a, 0x60);
                PsxRam.WriteU8(param_1 + 0x84, 0x60);
                PsxRam.WriteU8(param_1 + 0x85, 0x60);
                PsxRam.WriteU8(param_1 + 0x86, 0x60);
                PsxRam.WriteU8(param_1 + 0x90, 0x60);
                PsxRam.WriteU8(param_1 + 0x91, 0x60);
                PsxRam.WriteU8(param_1 + 0x92, 0x60);
                PsxRam.WriteU8(param_1 + 0x9c, 0x60);
                PsxRam.WriteU8(param_1 + 0x9d, 0x60);
                PsxRam.WriteU8(param_1 + 0x9e, 0x60);
                uVar4 = uVar4 + 1;
                param_1 = param_1 + 0x34;
            }
            while ((int)uVar4 < (int)uVar9);
        }

        return 0;
    }

    // GHIDRA: FUN_80032c24 @ 0x80032C24 (VS.EXE)
    // 628 bytes, 0x80032C24..0x80032E97. One caller: FUN_800340a8's state-2 layer-2 arm.
    //
    // NO GATE AT ALL -- this one runs its whole body unconditionally, which is what separates it
    // from every other worker here. It arms state 3: the three scales go to 0x11F4, the three
    // velocities to 1000/1000/600, the countdown to zero, layer 2's rotation to (0xFC00, 0, 0), the
    // transform is refreshed from the layer-2 tables, every primitive in the layer-2 range is
    // forced to a flat 0x60, and then twenty further RGB triples are zeroed.
    //
    // THOSE TWENTY ZEROED TRIPLES ARE NOT THE PRIMITIVES THIS FUNCTION JUST TINTED. Relative to
    // `param_1 + start * 0x34` they sit at 0x114 + k * 0xD0 and 0x84 + k * 0xD0 for k = 0..9. With
    // packets at 0x74 + i * 0x34 and 0xD0 = 4 * 0x34, that is packet[start + 4k + 3]'s FIRST RGB
    // and packet[start + 4k]'s SECOND RGB -- an irregular pattern that the tint loop above does not
    // produce and does not undo. It is reproduced literally, statement for statement, precisely
    // because no rule was found that would let it be written as a loop honestly.
    internal static uint FUN_80032c24(int param_1, int param_2, int param_3)
    {
        uint uVar2;
        int iVar3;
        uint uVar4;
        uint uVar5;
        int puVar6;
        int uVar7;
        int uVar8;
        int uVar9;

        _ = param_2;

        PsxRam.WriteU8(param_1 + 4, 3);
        PsxRam.WriteU16(param_1 + 0x1e34, 0x11f4);
        PsxRam.WriteU16(param_1 + 0x1e36, 0);
        PsxRam.WriteU16(param_1 + 0x1e38, 0x11f4);
        PsxRam.WriteU16(param_1 + 0x1e3a, 0);
        PsxRam.WriteU16(param_1 + 0x1e3c, 0x11f4);
        PsxRam.WriteU16(param_1 + 0x1e3e, 0);
        PsxRam.WriteU16(param_1 + 0x1e24, 1000);
        PsxRam.WriteU16(param_1 + 0x1e26, 0);
        PsxRam.WriteU16(param_1 + 0x1e28, 1000);
        PsxRam.WriteU16(param_1 + 0x1e2a, 0);
        PsxRam.WriteU16(param_1 + 0x1e2c, 600);
        PsxRam.WriteU16(param_1 + 0x1e2e, 0);
        PsxRam.WriteU16(param_1 + 0x0c, 0);
        PsxRam.WriteU16(param_1 + 0x0e, 0);
        PsxRam.WriteU16(param_1 + 0x24, 0xfc00);
        PsxRam.WriteU16(param_1 + 0x26, 0);
        PsxRam.WriteU16(param_1 + 0x28, 0);

        uVar2 = (uint)PsxRam.ReadI32(param_3);
        uVar5 = (uint)PsxRam.ReadI32(param_3 + 4);
        PsxRam.WriteI32(param_1 + 0x3c, (int)uVar2);
        PsxRam.WriteI32(param_1 + 0x40, (int)uVar5);

        iVar3 = PsxRam.ReadU16(param_1) * 0x10;
        PsxRam.WriteU16(param_1 + 0x3e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x3e)
            - (short)PsxRam.ReadU16(Dat8008125cAddress + PsxRam.ReadU16(param_1) * 4)));
        uVar7 = PsxRam.ReadI32(Dat8008156cAddress + iVar3);
        uVar8 = PsxRam.ReadI32(Dat80081570Address + iVar3);
        uVar9 = PsxRam.ReadI32(Dat80081574Address + iVar3);
        PsxRam.WriteI32(param_1 + 0x64, PsxRam.ReadI32(Dat80081568Address + iVar3));
        PsxRam.WriteI32(param_1 + 0x68, uVar7);
        PsxRam.WriteI32(param_1 + 0x6c, uVar8);
        PsxRam.WriteI32(param_1 + 0x70, uVar9);

        uVar4 = PsxRam.ReadU8(param_1 + 8);
        uVar5 = uVar4 + PsxRam.ReadU8(param_1 + 0x0b);
        if (uVar4 < uVar5)
        {
            puVar6 = param_1 + (int)(uVar4 * 0x34);
            do
            {
                PsxRam.WriteU8(puVar6 + 0x78, 0x60);
                PsxRam.WriteU8(puVar6 + 0x79, 0x60);
                PsxRam.WriteU8(puVar6 + 0x7a, 0x60);
                PsxRam.WriteU8(puVar6 + 0x84, 0x60);
                PsxRam.WriteU8(puVar6 + 0x85, 0x60);
                PsxRam.WriteU8(puVar6 + 0x86, 0x60);
                PsxRam.WriteU8(puVar6 + 0x90, 0x60);
                PsxRam.WriteU8(puVar6 + 0x91, 0x60);
                PsxRam.WriteU8(puVar6 + 0x92, 0x60);
                PsxRam.WriteU8(puVar6 + 0x9c, 0x60);
                PsxRam.WriteU8(puVar6 + 0x9d, 0x60);
                PsxRam.WriteU8(puVar6 + 0x9e, 0x60);
                uVar4 = uVar4 + 1;
                puVar6 = puVar6 + 0x34;
            }
            while ((int)uVar4 < (int)uVar5);
        }

        uVar4 = PsxRam.ReadU8(param_1 + 8);
        iVar3 = (int)(uVar4 * 0x34);
        // The twenty RGB triples described in FUN_80032c24's own note, written out
        // statement by statement in the original's order: ten at 0x114 + k * 0xD0 and then
        // ten at 0x84 + k * 0xD0, all relative to `record + first_primitive * 0x34`.
        PsxRam.WriteU8(param_1 + iVar3 + 0x114, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x115, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x116, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x1e4, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x1e5, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x1e6, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x2b4, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x2b5, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x2b6, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x384, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x385, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x386, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x454, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x455, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x456, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x524, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x525, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x526, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x5f4, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x5f5, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x5f6, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x6c4, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x6c5, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x6c6, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x794, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x795, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x796, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x864, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x865, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x866, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x84, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x85, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x86, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x154, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x155, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x156, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x224, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x225, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x226, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x2f4, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x2f5, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x2f6, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x3c4, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x3c5, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x3c6, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x494, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x495, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x496, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x564, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x565, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x566, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x634, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x635, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x636, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x704, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x705, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x706, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x7d4, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x7d5, 0);
        PsxRam.WriteU8(param_1 + iVar3 + 0x7d6, 0);
        return 0;
    }

    // GHIDRA: FUN_80033020 @ 0x80033020 (VS.EXE)
    // 496 bytes, 0x80033020..0x8003320F. One caller: FUN_800340a8's state-3 layer-2 arm.
    //
    // The plainest of the six: layer 2's rotation head, the aligned copy of the caller's position
    // into layer 2's translation, the four-column table refresh into layer 2's scale quad, and the
    // same twenty zeroed RGB triples FUN_80032c24 ends with. No countdown, no gate, no tint loop.
    // It differs from FUN_80033210 below only in that it has no scale-decay stage and takes the
    // 0xA0 rotation step unconditionally rather than under the VM-paused gate for a second time.
    internal static uint FUN_80033020(int param_1, int param_2, int param_3)
    {
        uint uVar2;
        uint uVar3;
        int iVar4;
        uint uVar5;
        int uVar6;
        int uVar7;
        int uVar8;

        _ = param_2;

        PsxRam.WriteU16(param_1 + 0x24, 0xfc00);
        PsxRam.WriteU16(param_1 + 0x26, 0);
        uVar2 = (uint)(AnimVm.DAT_800b305a & 1);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar2 = (uint)(PsxRam.ReadU16(param_1 + 0x28) - 0xa0);
            PsxRam.WriteU16(param_1 + 0x28, (ushort)uVar2);
        }

        uVar3 = (uint)PsxRam.ReadI32(param_3);
        uVar5 = (uint)PsxRam.ReadI32(param_3 + 4);
        PsxRam.WriteI32(param_1 + 0x3c, (int)uVar3);
        PsxRam.WriteI32(param_1 + 0x40, (int)uVar5);

        iVar4 = PsxRam.ReadU16(param_1) * 0x10;
        PsxRam.WriteU16(param_1 + 0x3e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x3e)
            - (short)PsxRam.ReadU16(Dat8008125cAddress + PsxRam.ReadU16(param_1) * 4)));
        uVar6 = PsxRam.ReadI32(Dat8008156cAddress + iVar4);
        uVar7 = PsxRam.ReadI32(Dat80081570Address + iVar4);
        uVar8 = PsxRam.ReadI32(Dat80081574Address + iVar4);
        PsxRam.WriteI32(param_1 + 0x64, PsxRam.ReadI32(Dat80081568Address + iVar4));
        PsxRam.WriteI32(param_1 + 0x68, uVar6);
        PsxRam.WriteI32(param_1 + 0x6c, uVar7);
        PsxRam.WriteI32(param_1 + 0x70, uVar8);

        uVar2 = PsxRam.ReadU8(param_1 + 8);
        iVar4 = (int)(uVar2 * 0x34);
        // The twenty RGB triples described in FUN_80032c24's own note, written out
        // statement by statement in the original's order: ten at 0x114 + k * 0xD0 and then
        // ten at 0x84 + k * 0xD0, all relative to `record + first_primitive * 0x34`.
        PsxRam.WriteU8(param_1 + iVar4 + 0x114, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x115, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x116, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x1e4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x1e5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x1e6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2b4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2b5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2b6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x384, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x385, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x386, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x454, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x455, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x456, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x524, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x525, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x526, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x5f4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x5f5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x5f6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x6c4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x6c5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x6c6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x794, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x795, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x796, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x864, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x865, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x866, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x84, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x85, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x86, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x154, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x155, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x156, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x224, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x225, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x226, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2f4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2f5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2f6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x3c4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x3c5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x3c6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x494, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x495, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x496, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x564, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x565, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x566, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x634, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x635, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x636, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x704, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x705, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x706, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x7d4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x7d5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x7d6, 0);
        return 0;
    }

    // GHIDRA: FUN_80033210 @ 0x80033210 (VS.EXE)
    // 852 bytes, 0x80033210..0x80033563. One caller: FUN_800340a8's state-4 arm.
    //
    // FOUR ARGUMENTS AT THE CALL SITE, THREE IN THE BODY. Ghidra prints
    // `FUN_80033210(puVar13, param_4, param_3, uVar14)` at 0x800346E8 but decompiles the callee as
    // three parameters, and reading the body confirms it: nothing reads a3. The fourth is kept on
    // this signature and discarded here so that the call site stays literal and so that a later
    // reader is not tempted to "fix" the caller by dropping an argument the image really passes.
    //
    // WHAT IT DOES BEYOND FUN_80033020: the scale-decay stage. Each of the three scale components
    // is multiplied by its own 0x1000-based scale factor with the C-style `+ 0xFFF` bias before an
    // arithmetic right shift by 12 -- the classic "round toward zero for negatives" idiom, kept as
    // an explicit branch because that is how the compiler emitted it. Then each of the three
    // velocities loses 300 per frame and is added into its scale, and any scale that has dropped
    // BELOW 0x1000 is snapped back to exactly 0x1000 with its velocity cleared. When all three have
    // snapped, the state byte goes to 2.
    //
    // NOTE THE CLAMP DIRECTION, because the arithmetic reads backwards at first glance: the scales
    // start at 0x11F4 (FUN_80032c24) with POSITIVE velocities and the velocities decay by 300 per
    // frame, so a scale rises, then falls once its velocity goes negative, and the floor at 0x1000
    // is what ends the state. The comparison is `< 0x1000`, signed, on the value AFTER the add.
    internal static uint FUN_80033210(int param_1, int param_2, int param_3, uint param_4)
    {
        uint uVar2;
        uint uVar3;
        int iVar4;
        uint uVar5;
        int uVar6;
        int iVar7;
        int uVar8;
        int uVar9;

        _ = param_2;
        _ = param_4;

        PsxRam.WriteU16(param_1 + 0x24, 0xfc00);
        PsxRam.WriteU16(param_1 + 0x26, 0);
        uVar2 = (uint)(AnimVm.DAT_800b305a & 1);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar2 = (uint)(PsxRam.ReadU16(param_1 + 0x28) - 0xa0);
            PsxRam.WriteU16(param_1 + 0x28, (ushort)uVar2);
        }

        uVar3 = (uint)PsxRam.ReadI32(param_3);
        uVar5 = (uint)PsxRam.ReadI32(param_3 + 4);
        PsxRam.WriteI32(param_1 + 0x3c, (int)uVar3);
        PsxRam.WriteI32(param_1 + 0x40, (int)uVar5);

        PsxRam.WriteU16(param_1 + 0x3e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x3e)
            - (short)PsxRam.ReadU16(Dat8008125cAddress + PsxRam.ReadU16(param_1) * 4)));

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            iVar4 = PsxRam.ReadU16(param_1) * 0x10;
            uVar6 = PsxRam.ReadI32(Dat8008156cAddress + iVar4);
            uVar8 = PsxRam.ReadI32(Dat80081570Address + iVar4);
            uVar9 = PsxRam.ReadI32(Dat80081574Address + iVar4);
            PsxRam.WriteI32(param_1 + 0x64, PsxRam.ReadI32(Dat80081568Address + iVar4));
            PsxRam.WriteI32(param_1 + 0x68, uVar6);
            PsxRam.WriteI32(param_1 + 0x6c, uVar8);
            PsxRam.WriteI32(param_1 + 0x70, uVar9);

            iVar4 = PsxRam.ReadI32(param_1 + 0x64) * PsxRam.ReadI32(param_1 + 0x1e34);
            if (iVar4 < 0)
            {
                iVar4 = iVar4 + 0xfff;
            }

            iVar7 = PsxRam.ReadI32(param_1 + 0x68) * PsxRam.ReadI32(param_1 + 0x1e38);
            PsxRam.WriteI32(param_1 + 0x64, iVar4 >> 0xc);
            if (iVar7 < 0)
            {
                iVar7 = iVar7 + 0xfff;
            }

            iVar4 = PsxRam.ReadI32(param_1 + 0x6c) * PsxRam.ReadI32(param_1 + 0x1e3c);
            PsxRam.WriteI32(param_1 + 0x68, iVar7 >> 0xc);
            if (iVar4 < 0)
            {
                iVar4 = iVar4 + 0xfff;
            }

            PsxRam.WriteI32(param_1 + 0x6c, iVar4 >> 0xc);

            PsxRam.WriteI32(param_1 + 0x1e24, PsxRam.ReadI32(param_1 + 0x1e24) + -300);
            PsxRam.WriteI32(param_1 + 0x1e28, PsxRam.ReadI32(param_1 + 0x1e28) + -300);
            iVar4 = PsxRam.ReadI32(param_1 + 0x1e34);
            PsxRam.WriteI32(param_1 + 0x1e34, iVar4 + PsxRam.ReadI32(param_1 + 0x1e24));
            PsxRam.WriteI32(param_1 + 0x1e2c, PsxRam.ReadI32(param_1 + 0x1e2c) + -300);
            if (iVar4 + PsxRam.ReadI32(param_1 + 0x1e24) < 0x1000)
            {
                PsxRam.WriteU16(param_1 + 0x1e24, 0);
                PsxRam.WriteU16(param_1 + 0x1e26, 0);
                PsxRam.WriteU16(param_1 + 0x1e34, 0x1000);
                PsxRam.WriteU16(param_1 + 0x1e36, 0);
            }

            iVar4 = PsxRam.ReadI32(param_1 + 0x1e38);
            PsxRam.WriteI32(param_1 + 0x1e38, iVar4 + PsxRam.ReadI32(param_1 + 0x1e28));
            if (iVar4 + PsxRam.ReadI32(param_1 + 0x1e28) < 0x1000)
            {
                PsxRam.WriteU16(param_1 + 0x1e28, 0);
                PsxRam.WriteU16(param_1 + 0x1e2a, 0);
                PsxRam.WriteU16(param_1 + 0x1e38, 0x1000);
                PsxRam.WriteU16(param_1 + 0x1e3a, 0);
            }

            iVar4 = PsxRam.ReadI32(param_1 + 0x1e3c);
            PsxRam.WriteI32(param_1 + 0x1e3c, iVar4 + PsxRam.ReadI32(param_1 + 0x1e2c));
            if (iVar4 + PsxRam.ReadI32(param_1 + 0x1e2c) < 0x1000)
            {
                PsxRam.WriteU16(param_1 + 0x1e2c, 0);
                PsxRam.WriteU16(param_1 + 0x1e2e, 0);
                PsxRam.WriteU16(param_1 + 0x1e3c, 0x1000);
                PsxRam.WriteU16(param_1 + 0x1e3e, 0);
            }

            if (((PsxRam.ReadI32(param_1 + 0x1e34) == 0x1000) && (PsxRam.ReadI32(param_1 + 0x1e38) == 0x1000))
                && (PsxRam.ReadI32(param_1 + 0x1e3c) == 0x1000))
            {
                PsxRam.WriteU8(param_1 + 4, 2);
            }
        }

        uVar2 = PsxRam.ReadU8(param_1 + 8);
        iVar4 = (int)(uVar2 * 0x34);
        // The twenty RGB triples described in FUN_80032c24's own note, written out
        // statement by statement in the original's order: ten at 0x114 + k * 0xD0 and then
        // ten at 0x84 + k * 0xD0, all relative to `record + first_primitive * 0x34`.
        PsxRam.WriteU8(param_1 + iVar4 + 0x114, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x115, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x116, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x1e4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x1e5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x1e6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2b4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2b5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2b6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x384, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x385, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x386, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x454, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x455, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x456, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x524, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x525, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x526, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x5f4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x5f5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x5f6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x6c4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x6c5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x6c6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x794, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x795, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x796, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x864, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x865, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x866, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x84, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x85, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x86, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x154, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x155, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x156, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x224, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x225, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x226, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2f4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2f5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x2f6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x3c4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x3c5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x3c6, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x494, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x495, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x496, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x564, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x565, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x566, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x634, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x635, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x636, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x704, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x705, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x706, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x7d4, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x7d5, 0);
        PsxRam.WriteU8(param_1 + iVar4 + 0x7d6, 0);
        return 0;
    }

    // GHIDRA: FUN_80033564 @ 0x80033564 (VS.EXE)
    // 892 bytes, 0x80033564..0x800338DF. One caller: FUN_800340a8's state-0 layer-1 arm.
    //
    // THE LAYER-1 FADE-OUT, and the return value is the state, not a status: 0 when the countdown
    // has run out and the record has been torn down, 1 otherwise. FUN_800340a8 hands that number
    // straight to FUN_80033c64 as its param_3, where a non-zero value replaces the composed
    // rotation with the uncomposed one -- so this return is a rendering decision, made here.
    //
    // THE FADE IS PER-PRIMITIVE AND CONDITIONAL. Each primitive whose LAST RGB byte (+0x9E) is
    // still non-zero has its first two RGB triples ZEROED and its last two dimmed by 0x10. That
    // asymmetry -- zero for two, minus sixteen for two -- is the original's, and it is the opposite
    // way round from FUN_800338e0's layer-2 fade, which dims all four. Both are reproduced as
    // written; neither is corrected against the other.
    internal static uint FUN_80033564(int param_1, int param_2, int param_3)
    {
        ushort uVar1;
        byte cVar3;
        uint uVar4;
        uint uVar5;
        int iVar6;
        uint uVar7;
        uint uVar8;

        _ = param_2;

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            iVar6 = PsxRam.ReadI32(param_1 + 0x0c);
            PsxRam.WriteI32(param_1 + 0x0c, iVar6 + -1);
            if (iVar6 + -1 < 0)
            {
                uVar4 = PsxRam.ReadU8(param_1 + 7);
                PsxRam.WriteU8(param_1 + 4, 0);
                PsxRam.WriteU16(param_1 + 0x1c, 0);
                PsxRam.WriteU16(param_1 + 0x1e, 0);
                PsxRam.WriteU16(param_1 + 0x20, 0);
                PsxRam.WriteU16(param_1 + 0x34, 0);
                PsxRam.WriteU16(param_1 + 0x36, 0);
                PsxRam.WriteU16(param_1 + 0x38, 0);
                PsxRam.WriteU16(param_1 + 0x54, 0);
                PsxRam.WriteU16(param_1 + 0x56, 0);
                PsxRam.WriteU16(param_1 + 0x58, 0);
                PsxRam.WriteU16(param_1 + 0x5a, 0);
                uVar7 = uVar4 + PsxRam.ReadU8(param_1 + 0x0a);
                PsxRam.WriteU16(param_1 + 0x5c, 0);
                PsxRam.WriteU16(param_1 + 0x5e, 0);
                if (uVar4 < uVar7)
                {
                    param_1 = param_1 + (int)(uVar4 * 0x34);
                    do
                    {
                        PsxRam.WriteU8(param_1 + 0x78, 0);
                        PsxRam.WriteU8(param_1 + 0x79, 0);
                        PsxRam.WriteU8(param_1 + 0x7a, 0);
                        PsxRam.WriteU8(param_1 + 0x84, 0);
                        PsxRam.WriteU8(param_1 + 0x85, 0);
                        PsxRam.WriteU8(param_1 + 0x86, 0);
                        PsxRam.WriteU8(param_1 + 0x90, 0);
                        PsxRam.WriteU8(param_1 + 0x91, 0);
                        PsxRam.WriteU8(param_1 + 0x92, 0);
                        PsxRam.WriteU8(param_1 + 0x9c, 0);
                        PsxRam.WriteU8(param_1 + 0x9d, 0);
                        PsxRam.WriteU8(param_1 + 0x9e, 0);
                        uVar4 = uVar4 + 1;
                        param_1 = param_1 + 0x34;
                    }
                    while ((int)uVar4 < (int)uVar7);
                }

                uVar8 = 0;
            }
            else
            {
                uVar1 = PsxRam.ReadU16(param_1 + 0x20);
                PsxRam.WriteU16(param_1 + 0x1c, 0);
                PsxRam.WriteU16(param_1 + 0x1e, 0);
                PsxRam.WriteU16(param_1 + 0x20, (ushort)(uVar1 - 0xa0));

                uVar5 = (uint)PsxRam.ReadI32(param_3);
                uVar7 = (uint)PsxRam.ReadI32(param_3 + 4);
                PsxRam.WriteI32(param_1 + 0x34, (int)uVar5);
                PsxRam.WriteI32(param_1 + 0x38, (int)uVar7);

                uVar4 = PsxRam.ReadU8(param_1 + 7);
                uVar7 = uVar4 + PsxRam.ReadU8(param_1 + 0x0a);
                PsxRam.WriteU16(param_1 + 0x36, (ushort)((short)PsxRam.ReadU16(param_1 + 0x36)
                    - (short)PsxRam.ReadU16(Dat800811c0Address + PsxRam.ReadU16(param_1) * 4)));
                PsxRam.WriteI32(param_1 + 0x54, (10 - PsxRam.ReadI32(param_1 + 0x0c)) * 400
                    + PsxRam.ReadI32(Dat800812f8Address + PsxRam.ReadU16(param_1) * 0x10));
                PsxRam.WriteI32(param_1 + 0x58, (10 - PsxRam.ReadI32(param_1 + 0x0c)) * 400
                    + PsxRam.ReadI32(Dat800812fcAddress + PsxRam.ReadU16(param_1) * 0x10));
                PsxRam.WriteI32(param_1 + 0x5c, (10 - PsxRam.ReadI32(param_1 + 0x0c)) * 400
                    + PsxRam.ReadI32(Dat80081300Address + PsxRam.ReadU16(param_1) * 0x10));

                if (uVar4 < uVar7)
                {
                    param_1 = param_1 + (int)(uVar4 * 0x34);
                    do
                    {
                        uVar4 = uVar4 + 1;
                        if ((sbyte)PsxRam.ReadU8(param_1 + 0x9e) != 0)
                        {
                            PsxRam.WriteU8(param_1 + 0x86, 0);
                            PsxRam.WriteU8(param_1 + 0x85, 0);
                            PsxRam.WriteU8(param_1 + 0x84, 0);
                            PsxRam.WriteU8(param_1 + 0x7a, 0);
                            PsxRam.WriteU8(param_1 + 0x79, 0);
                            PsxRam.WriteU8(param_1 + 0x78, 0);
                            cVar3 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x9e) + -0x10);
                            PsxRam.WriteU8(param_1 + 0x9e, cVar3);
                            PsxRam.WriteU8(param_1 + 0x9d, cVar3);
                            PsxRam.WriteU8(param_1 + 0x9c, cVar3);
                            PsxRam.WriteU8(param_1 + 0x92, cVar3);
                            PsxRam.WriteU8(param_1 + 0x91, cVar3);
                            PsxRam.WriteU8(param_1 + 0x90, cVar3);
                        }

                        param_1 = param_1 + 0x34;
                    }
                    while ((int)uVar4 < (int)uVar7);
                }

                uVar8 = 1;
            }
        }
        else
        {
            PsxRam.WriteU16(param_1 + 0x1c, 0);
            PsxRam.WriteU16(param_1 + 0x1e, 0);

            uVar5 = (uint)PsxRam.ReadI32(param_3);
            uVar7 = (uint)PsxRam.ReadI32(param_3 + 4);
            PsxRam.WriteI32(param_1 + 0x34, (int)uVar5);
            PsxRam.WriteI32(param_1 + 0x38, (int)uVar7);

            PsxRam.WriteU16(param_1 + 0x36, (ushort)((short)PsxRam.ReadU16(param_1 + 0x36)
                - (short)PsxRam.ReadU16(Dat800811c0Address + PsxRam.ReadU16(param_1) * 4)));
            PsxRam.WriteI32(param_1 + 0x54, (10 - PsxRam.ReadI32(param_1 + 0x0c)) * 400
                + PsxRam.ReadI32(Dat800812f8Address + PsxRam.ReadU16(param_1) * 0x10));
            uVar8 = 1;
            PsxRam.WriteI32(param_1 + 0x58, (10 - PsxRam.ReadI32(param_1 + 0x0c)) * 400
                + PsxRam.ReadI32(Dat800812fcAddress + PsxRam.ReadU16(param_1) * 0x10));
            PsxRam.WriteI32(param_1 + 0x5c, (10 - PsxRam.ReadI32(param_1 + 0x0c)) * 400
                + PsxRam.ReadI32(Dat80081300Address + PsxRam.ReadU16(param_1) * 0x10));
        }

        return uVar8;
    }

    // GHIDRA: FUN_800338e0 @ 0x800338E0 (VS.EXE)
    // 900 bytes, 0x800338E0..0x80033C63. One caller: FUN_800340a8's state-0 layer-2 arm.
    //
    // The layer-2 twin of FUN_80033564, and NOT a copy of it. Four differences, all reproduced:
    //   * it always returns 0, where FUN_80033564 returns the state (0 or 1);
    //   * its table refresh uses the layer-2 tables and a step of `(10 - countdown) * -400` on X
    //     and Y and `* -200` on Z, where FUN_80033564 uses `* 400` on all three -- opposite sign
    //     and a different Z coefficient;
    //   * its fade dims ALL FOUR RGB triples by 0x10, where FUN_80033564 zeroes two and dims two;
    //   * its paused arm writes +0x24/+0x26 as (0xFC00, 0), where FUN_80033564's writes (0, 0).
    internal static uint FUN_800338e0(int param_1, int param_2, int param_3)
    {
        ushort uVar1;
        byte cVar3;
        uint uVar4;
        int iVar5;
        uint uVar6;
        uint uVar7;

        _ = param_2;

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            iVar5 = PsxRam.ReadI32(param_1 + 0x0c);
            PsxRam.WriteI32(param_1 + 0x0c, iVar5 + -1);
            if (iVar5 + -1 < 0)
            {
                uVar7 = PsxRam.ReadU8(param_1 + 8);
                PsxRam.WriteU8(param_1 + 4, 0);
                PsxRam.WriteU16(param_1 + 0x24, 0);
                PsxRam.WriteU16(param_1 + 0x26, 0);
                PsxRam.WriteU16(param_1 + 0x28, 0);
                PsxRam.WriteU16(param_1 + 0x3c, 0);
                PsxRam.WriteU16(param_1 + 0x3e, 0);
                PsxRam.WriteU16(param_1 + 0x40, 0);
                PsxRam.WriteU16(param_1 + 0x64, 0);
                PsxRam.WriteU16(param_1 + 0x66, 0);
                PsxRam.WriteU16(param_1 + 0x68, 0);
                PsxRam.WriteU16(param_1 + 0x6a, 0);
                uVar6 = uVar7 + PsxRam.ReadU8(param_1 + 0x0b);
                PsxRam.WriteU16(param_1 + 0x6c, 0);
                PsxRam.WriteU16(param_1 + 0x6e, 0);
                if (uVar7 < uVar6)
                {
                    param_1 = param_1 + (int)(uVar7 * 0x34);
                    do
                    {
                        PsxRam.WriteU8(param_1 + 0x78, 0);
                        PsxRam.WriteU8(param_1 + 0x79, 0);
                        PsxRam.WriteU8(param_1 + 0x7a, 0);
                        PsxRam.WriteU8(param_1 + 0x84, 0);
                        PsxRam.WriteU8(param_1 + 0x85, 0);
                        PsxRam.WriteU8(param_1 + 0x86, 0);
                        PsxRam.WriteU8(param_1 + 0x90, 0);
                        PsxRam.WriteU8(param_1 + 0x91, 0);
                        PsxRam.WriteU8(param_1 + 0x92, 0);
                        PsxRam.WriteU8(param_1 + 0x9c, 0);
                        PsxRam.WriteU8(param_1 + 0x9d, 0);
                        PsxRam.WriteU8(param_1 + 0x9e, 0);
                        uVar7 = uVar7 + 1;
                        param_1 = param_1 + 0x34;
                    }
                    while ((int)uVar7 < (int)uVar6);
                }
            }
            else
            {
                uVar1 = PsxRam.ReadU16(param_1 + 0x28);
                PsxRam.WriteU16(param_1 + 0x24, 0xfc00);
                PsxRam.WriteU16(param_1 + 0x26, 0);
                PsxRam.WriteU16(param_1 + 0x28, (ushort)(uVar1 - 0xa0));

                uVar4 = (uint)PsxRam.ReadI32(param_3);
                uVar6 = (uint)PsxRam.ReadI32(param_3 + 4);
                PsxRam.WriteI32(param_1 + 0x3c, (int)uVar4);
                PsxRam.WriteI32(param_1 + 0x40, (int)uVar6);

                uVar7 = PsxRam.ReadU8(param_1 + 8);
                uVar6 = uVar7 + PsxRam.ReadU8(param_1 + 0x0b);
                PsxRam.WriteU16(param_1 + 0x3e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x3e)
                    - (short)PsxRam.ReadU16(Dat8008125cAddress + PsxRam.ReadU16(param_1) * 4)));
                PsxRam.WriteI32(param_1 + 0x64,
                    PsxRam.ReadI32(Dat80081568Address + PsxRam.ReadU16(param_1) * 0x10)
                    + (10 - PsxRam.ReadI32(param_1 + 0x0c)) * -400);
                PsxRam.WriteI32(param_1 + 0x68,
                    PsxRam.ReadI32(Dat8008156cAddress + PsxRam.ReadU16(param_1) * 0x10)
                    + (10 - PsxRam.ReadI32(param_1 + 0x0c)) * -400);
                PsxRam.WriteI32(param_1 + 0x6c,
                    PsxRam.ReadI32(Dat80081570Address + PsxRam.ReadU16(param_1) * 0x10)
                    + (10 - PsxRam.ReadI32(param_1 + 0x0c)) * -200);

                if (uVar7 < uVar6)
                {
                    param_1 = param_1 + (int)(uVar7 * 0x34);
                    do
                    {
                        uVar7 = uVar7 + 1;
                        if ((sbyte)PsxRam.ReadU8(param_1 + 0x9e) != 0)
                        {
                            cVar3 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x9e) + -0x10);
                            PsxRam.WriteU8(param_1 + 0x9e, cVar3);
                            PsxRam.WriteU8(param_1 + 0x9d, cVar3);
                            PsxRam.WriteU8(param_1 + 0x9c, cVar3);
                            PsxRam.WriteU8(param_1 + 0x92, cVar3);
                            PsxRam.WriteU8(param_1 + 0x91, cVar3);
                            PsxRam.WriteU8(param_1 + 0x90, cVar3);
                            PsxRam.WriteU8(param_1 + 0x86, cVar3);
                            PsxRam.WriteU8(param_1 + 0x85, cVar3);
                            PsxRam.WriteU8(param_1 + 0x84, cVar3);
                            PsxRam.WriteU8(param_1 + 0x7a, cVar3);
                            PsxRam.WriteU8(param_1 + 0x79, cVar3);
                            PsxRam.WriteU8(param_1 + 0x78, cVar3);
                        }

                        param_1 = param_1 + 0x34;
                    }
                    while ((int)uVar7 < (int)uVar6);
                }
            }
        }
        else
        {
            PsxRam.WriteU16(param_1 + 0x24, 0xfc00);
            PsxRam.WriteU16(param_1 + 0x26, 0);

            uVar4 = (uint)PsxRam.ReadI32(param_3);
            uVar6 = (uint)PsxRam.ReadI32(param_3 + 4);
            PsxRam.WriteI32(param_1 + 0x3c, (int)uVar4);
            PsxRam.WriteI32(param_1 + 0x40, (int)uVar6);

            PsxRam.WriteU16(param_1 + 0x3e, (ushort)((short)PsxRam.ReadU16(param_1 + 0x3e)
                - (short)PsxRam.ReadU16(Dat8008125cAddress + PsxRam.ReadU16(param_1) * 4)));
            PsxRam.WriteI32(param_1 + 0x64,
                PsxRam.ReadI32(Dat80081568Address + PsxRam.ReadU16(param_1) * 0x10)
                + (10 - PsxRam.ReadI32(param_1 + 0x0c)) * -400);
            PsxRam.WriteI32(param_1 + 0x68,
                PsxRam.ReadI32(Dat8008156cAddress + PsxRam.ReadU16(param_1) * 0x10)
                + (10 - PsxRam.ReadI32(param_1 + 0x0c)) * -400);
            PsxRam.WriteI32(param_1 + 0x6c,
                PsxRam.ReadI32(Dat80081570Address + PsxRam.ReadU16(param_1) * 0x10)
                + (10 - PsxRam.ReadI32(param_1 + 0x0c)) * -200);
        }

        return 0;
    }

    // =====================================================================================
    // THE TWO SHARED SERVICES
    // =====================================================================================

    // GHIDRA: FUN_800322d0 @ 0x800322D0 (VS.EXE)
    // 132 bytes, 0x800322D0..0x80032353. One caller: FUN_800340a8's `param_6 == 0` teardown, which
    // calls it as `FUN_800322d0(layer, record, 0x80, 0x80, 0x80)`.
    //
    // Writes one RGB triple into all four colour fields of every primitive in one layer's range.
    // param_1 is the layer index and is used twice as an offset -- `record + param_1 + 6` for the
    // first primitive and `record + param_1 + 9` for the count -- which is what pins the three
    // start bytes to +0x06/+0x07/+0x08 and the three count bytes to +0x09/+0x0A/+0x0B.
    internal static void FUN_800322d0(int param_1, int param_2, byte param_3, byte param_4, byte param_5)
    {
        uint uVar1;
        uint uVar2;

        uVar1 = PsxRam.ReadU8(param_2 + param_1 + 6);
        uVar2 = uVar1 + PsxRam.ReadU8(param_2 + param_1 + 9);
        if (uVar1 < uVar2)
        {
            param_2 = (int)(uVar1 * 0x34) + param_2;
            do
            {
                PsxRam.WriteU8(param_2 + 0x78, param_3);
                PsxRam.WriteU8(param_2 + 0x79, param_4);
                PsxRam.WriteU8(param_2 + 0x7a, param_5);
                PsxRam.WriteU8(param_2 + 0x84, param_3);
                PsxRam.WriteU8(param_2 + 0x85, param_4);
                PsxRam.WriteU8(param_2 + 0x86, param_5);
                PsxRam.WriteU8(param_2 + 0x90, param_3);
                PsxRam.WriteU8(param_2 + 0x91, param_4);
                PsxRam.WriteU8(param_2 + 0x92, param_5);
                PsxRam.WriteU8(param_2 + 0x9c, param_3);
                PsxRam.WriteU8(param_2 + 0x9d, param_4);
                PsxRam.WriteU8(param_2 + 0x9e, param_5);
                uVar1 = uVar1 + 1;
                param_2 = param_2 + 0x34;
            }
            while ((int)uVar1 < (int)uVar2);
        }
    }

    // GHIDRA: FUN_80033c64 @ 0x80033C64 (VS.EXE)
    // 796 bytes, 0x80033C64..0x80033F7F. Two callers, both in FUN_800340a8: the explicit
    // `FUN_80033c64(1, record, 0)` inside state 3's layer-1 arm at 0x80034634, and the switch's
    // shared tail at 0x80034734.
    //
    // THE GTE PIPELINE, and every SDK entry point it uses is at or above 0x800632C4, so all ten are
    // CALLED in PsxSdkMonogame and none is re-transliterated: PushMatrix, ReadRotMatrix, RotMatrix,
    // ScaleMatrix, CompMatrix, SetRotMatrix, SetTransMatrix, RotTransPers, RotAverage4, PopMatrix.
    //
    // The model matrix is the layer's rotation, its translation biased by the two camera offsets in
    // the scratchpad, and its scale; composed against whatever rotation matrix is already in the
    // GTE. When param_3 is non-zero the composed 3x3 is replaced wholesale by the UNCOMPOSED one
    // while the composed translation is kept -- the same asymmetry AnimCmdMesh's
    // TransformMeshPrimitives records at its own flag, and it is left alone here too.
    //
    // ONE OBJECT-LEVEL PROJECTION AND ONE PER PRIMITIVE, AND THEY ARE NOT INTERCHANGED. The
    // RotTransPers of the origin produces lVar2; each RotAverage4 produces lVar3. The ordering-table
    // BUCKET is chosen from lVar2 -- the object's depth, the same for every primitive -- while the
    // range test that decides whether to link at all is on lVar3, the primitive's own depth. So a
    // primitive can be rejected on its own depth and yet, when accepted, is filed under the whole
    // object's depth. That reads like a defect and it is reproduced exactly; Rule 12.
    //
    // PARTIAL: `DAT_1f800128`, the low end of the accepted depth range, is the PSX scratchpad word
    // at 0x1F800128. It is hardware, shared by every overlay, and it already has exactly one
    // declaration in this port -- TITLE_EXE/GteScratch.cs. It is referenced there rather than
    // declared a second time here, because a second declaration of one hardware address is the
    // defect check_duplicate_symbols.py exists to catch. That it lives under a TITLE_EXE namespace
    // is an artefact of which overlay was ported first; moving it beside _DAT_1f8000b4 in the
    // shared Scratchpad.cs would be right, and is reported upward rather than done here.
    internal static void FUN_80033c64(int param_1, int param_2, int param_3)
    {
        int psVar1;
        int lVar2;
        int lVar3;
        uint uVar4;
        int iVar6;
        int psVar8;
        int iVar9;
        int iVar10;
        uint uVar11;

        psVar8 = param_2 + param_1 * 8 + 0x2c;
        LibGte.PushMatrix();

        var MStack_b8 = new LibGte.MATRIX();
        LibGte.ReadRotMatrix(MStack_b8);

        var local_98 = new LibGte.MATRIX();
        LibGte.RotMatrix(ReadSvectorFromRam(param_2 + param_1 * 8 + 0x14), local_98);

        // All three are signed halfword loads; the X and Z biases are the camera offsets the
        // scratchpad carries, exactly as AnimCmdMesh's TransformMeshPrimitives applies them.
        local_98.t[0] = (short)PsxRam.ReadU16(psVar8) - Scratchpad._DAT_1f8000b4;
        local_98.t[1] = (short)PsxRam.ReadU16(psVar8 + 2);
        local_98.t[2] = (short)PsxRam.ReadU16(psVar8 + 4) - Scratchpad._DAT_1f8000bc;

        // The scale slot holds three full 32-bit words (the workers write it with `sw`), so it is
        // read as a VECTOR of ints and not as halfwords.
        var scale = new LibGte.VECTOR
        {
            vx = PsxRam.ReadI32(param_2 + param_1 * 0x10 + 0x44),
            vy = PsxRam.ReadI32(param_2 + param_1 * 0x10 + 0x48),
            vz = PsxRam.ReadI32(param_2 + param_1 * 0x10 + 0x4c),
        };
        LibGte.ScaleMatrix(local_98, scale);

        var local_78 = new LibGte.MATRIX();
        LibGte.CompMatrix(MStack_b8, local_98, local_78);

        if (param_3 != 0)
        {
            // The original writes out all nine elements one at a time; nine is the whole 3x3.
            local_78.m[0] = local_98.m[0];
            local_78.m[1] = local_98.m[1];
            local_78.m[2] = local_98.m[2];
            local_78.m[3] = local_98.m[3];
            local_78.m[4] = local_98.m[4];
            local_78.m[5] = local_98.m[5];
            local_78.m[6] = local_98.m[6];
            local_78.m[7] = local_98.m[7];
            local_78.m[8] = local_98.m[8];
        }

        LibGte.SetRotMatrix(local_78);
        LibGte.SetTransMatrix(local_78);

        // JUSTIFICATION: C# language bridge only
        // RELATION: the original's four adjacent stack SVECTORs (local_58, SStack_50, SStack_48,
        // SStack_40 -- 8 bytes apart, which is what makes them one array to the vertex loop below)
        // and the eight adjacent stack halfwords local_38..local_2a followed by lStack_28 and
        // lStack_24. Both are modelled as byte buffers at their real stack strides so the loop that
        // walks them with a pointer stays a pointer walk, and so RotTransPers -- whose C# entry
        // point is buffer-and-offset -- can write into the same 16 bytes RotAverage4 does.
        byte[] local_58 = new byte[32];
        byte[] local_38 = new byte[24];

        // local_58.vz = 0; vy = 0; vx = 0 -- the original zeroes the first SVECTOR only, in that
        // order, and projects the model origin through it.
        BitConverter.GetBytes((short)0).CopyTo(local_58, 4);
        BitConverter.GetBytes((short)0).CopyTo(local_58, 2);
        BitConverter.GetBytes((short)0).CopyTo(local_58, 0);
        lVar2 = LibGte.RotTransPers(local_58, 0, local_38, 0, 16, 20);

        uVar4 = PsxRam.ReadU8(param_2 + param_1 + 6);
        uVar11 = uVar4 + PsxRam.ReadU8(param_2 + param_1 + 9);
        if (uVar4 < uVar11)
        {
            iVar10 = (int)(uVar4 * 0x34 + 0x74);
            iVar9 = (int)(uVar4 * 0x18 + 0x14c4);
            do
            {
                iVar6 = 0;
                psVar8 = param_2 + iVar9;
                int pSVar5 = 0;
                do
                {
                    BitConverter.GetBytes(PsxRam.ReadU16(psVar8)).CopyTo(local_58, pSVar5);
                    iVar6 = iVar6 + 1;
                    BitConverter.GetBytes(PsxRam.ReadU16(psVar8 + 2)).CopyTo(local_58, pSVar5 + 2);
                    psVar1 = psVar8 + 4;
                    psVar8 = psVar8 + 6;
                    BitConverter.GetBytes(PsxRam.ReadU16(psVar1)).CopyTo(local_58, pSVar5 + 4);
                    pSVar5 = pSVar5 + 8;
                }
                while (iVar6 < 4);

                // JUSTIFICATION: C# language bridge only
                // RELATION: on the console these two are the stack words lStack_28 and lStack_24,
                // the last eight bytes of the block `local_38` models above. RotAverage4's C# entry
                // point takes them as int[1] out-params while RotTransPers takes them as offsets
                // into a buffer, so the one stack region has to be reached two ways. Nothing in
                // this function ever reads either value back -- the SDK writes both as zero and
                // says so in its own PARTIAL -- so the split is observationally exact.
                int[] lStack_28 = new int[1];
                int[] lStack_24 = new int[1];

                lVar3 = LibGte.RotAverage4(
                    ReadSvectorFromBuffer(local_58, 0), ReadSvectorFromBuffer(local_58, 8),
                    ReadSvectorFromBuffer(local_58, 0x10), ReadSvectorFromBuffer(local_58, 0x18),
                    local_38, 0, 4, 8, 12,
                    lStack_28, lStack_24);

                int puVar7 = param_2 + iVar10;
                PsxRam.WriteU16(puVar7 + 8, BitConverter.ToUInt16(local_38, 0));
                PsxRam.WriteU16(puVar7 + 10, BitConverter.ToUInt16(local_38, 2));
                PsxRam.WriteU16(puVar7 + 0x14, BitConverter.ToUInt16(local_38, 4));
                PsxRam.WriteU16(puVar7 + 0x16, BitConverter.ToUInt16(local_38, 6));
                PsxRam.WriteU16(puVar7 + 0x20, BitConverter.ToUInt16(local_38, 8));
                PsxRam.WriteU16(puVar7 + 0x22, BitConverter.ToUInt16(local_38, 10));
                PsxRam.WriteU16(puVar7 + 0x2c, BitConverter.ToUInt16(local_38, 12));
                PsxRam.WriteU16(puVar7 + 0x2e, BitConverter.ToUInt16(local_38, 14));
                iVar10 = iVar10 + 0x34;

                if ((TITLE_EXE.GteScratch.DAT_1f800128 < 0x800 - lVar3) && (0x800 - lVar3 < 0x800))
                {
                    // The ordering-table insert, spelled exactly as the image spells it: the
                    // packet's own first word keeps its top byte (the primitive's length code) and
                    // takes the bucket's current head in its low 24 bits, then the bucket takes the
                    // packet's address in ITS low 24 bits. Note `+ 100` is decimal 100 = 0x64, not
                    // the 0x70 that VS_EXE_exe's DrawOTag uses; the difference is the original's.
                    iVar6 = (0x800 - lVar2) * 4 + PsxRam.ReadI32(Dat8008d420Address);
                    PsxRam.WriteI32(puVar7, (int)(((uint)PsxRam.ReadI32(puVar7) & 0xff000000)
                        | ((uint)PsxRam.ReadI32(iVar6 + 100) & 0xffffff)));
                    PsxRam.WriteI32(iVar6 + 100, (int)(((uint)PsxRam.ReadI32(iVar6 + 100) & 0xff000000)
                        | ((uint)puVar7 & 0xffffff)));
                }

                iVar9 = iVar9 + 0x18;
            }
            while (iVar9 < (int)(uVar11 * 0x18 + 0x14c4));
        }

        LibGte.PopMatrix();
    }

    // =====================================================================================
    // TWO SMALL RECORD STEPPERS THAT BELONG TO A DIFFERENT CALLER
    // =====================================================================================
    // Neither of these is reached from FUN_800340a8 or from anything else in this file. They are
    // ported here because they sit in the same address range and were handed to this slice; their
    // callers are at 0x8002A980 / 0x8002C240 (FUN_800319ac) and 0x8002B5D0 / 0x8002C450 /
    // 0x8002F100 / 0x80030520 (FUN_80031ab8), none of which exists in this port yet. The record
    // they walk is NOT the 0x1E58-byte record above: its fields are at +0x06, +0x08, +0x0A and
    // +0x20..+0x25, and nothing establishes a relation between the two. No name is invented for it.

    // GHIDRA: FUN_800319ac @ 0x800319AC (VS.EXE)
    // 268 bytes, 0x800319AC..0x80031AB7. Two callers, neither ported.
    //
    // A five-state ramp on two shorts: +0x08 is a counter and +0x0A an accumulator, and +0x06
    // selects which of five rules runs and is itself advanced by them.
    //   0  counter++, accumulator += counter; advance when counter > 7
    //   1  counter--, accumulator += counter; advance when counter reaches 0
    //   2  counter++; when it reaches 2, reset it to 0 and advance
    //   3  counter++, accumulator -= counter; advance when counter > 7
    //   4  counter--, accumulator -= counter; when counter reaches 0, reset state to 0
    //   anything else: nothing
    //
    // TWO THINGS ARE DELIBERATELY NOT TIDIED. Case 4 has no `break` in the original and falls into
    // the default, so it never reaches the shared "advance" tail -- that is why it resets the state
    // to 0 itself. And the advance tail RE-READS +0x06 from memory rather than using the value the
    // switch dispatched on; case 2 re-reads it too, before its own store of 0 to +0x08. Both are
    // kept, because a caller that changed +0x06 between the dispatch and the tail would see the
    // difference.
    internal static void FUN_800319ac(int param_1)
    {
        // Initialised only because C# demands definite assignment across a switch whose two
        // fall-out arms are the only readers; on the console they are registers each arm writes
        // before it reaches the tail.
        short sVar1 = 0;
        short sVar2 = 0;

        switch (PsxRam.ReadU16(param_1 + 6))
        {
            case 0:
                sVar1 = (short)((short)PsxRam.ReadU16(param_1 + 8) + 1);
                PsxRam.WriteU16(param_1 + 8, (ushort)sVar1);
                sVar2 = (short)PsxRam.ReadU16(param_1 + 8);
                sVar1 = (short)((short)PsxRam.ReadU16(param_1 + 10) + sVar1);
                break;

            case 1:
                sVar2 = (short)((short)PsxRam.ReadU16(param_1 + 8) + -1);
                PsxRam.WriteU16(param_1 + 8, (ushort)sVar2);
                PsxRam.WriteU16(param_1 + 10, (ushort)((short)PsxRam.ReadU16(param_1 + 10) + sVar2));
                if ((short)PsxRam.ReadU16(param_1 + 8) != 0)
                {
                    return;
                }

                goto LAB_80031a78;

            case 2:
                sVar2 = (short)((short)PsxRam.ReadU16(param_1 + 8) + 1);
                PsxRam.WriteU16(param_1 + 8, (ushort)sVar2);
                if (sVar2 < 2)
                {
                    return;
                }

                sVar2 = (short)PsxRam.ReadU16(param_1 + 6);
                PsxRam.WriteU16(param_1 + 8, 0);
                goto code_r0x80031a80;

            case 3:
                sVar1 = (short)((short)PsxRam.ReadU16(param_1 + 8) + 1);
                PsxRam.WriteU16(param_1 + 8, (ushort)sVar1);
                sVar2 = (short)PsxRam.ReadU16(param_1 + 8);
                sVar1 = (short)((short)PsxRam.ReadU16(param_1 + 10) - sVar1);
                break;

            case 4:
                sVar2 = (short)((short)PsxRam.ReadU16(param_1 + 8) + -1);
                PsxRam.WriteU16(param_1 + 8, (ushort)sVar2);
                PsxRam.WriteU16(param_1 + 10, (ushort)((short)PsxRam.ReadU16(param_1 + 10) - sVar2));
                if ((short)PsxRam.ReadU16(param_1 + 8) == 0)
                {
                    PsxRam.WriteU16(param_1 + 6, 0);
                }

                // Falls into the default in the original; C# forbids fall-through, so the shared
                // exit is spelled as a return. Nothing is skipped: the default arm is a bare return.
                return;

            default:
                return;
        }

        // Same C# rule as in FUN_800340a8: cases 1 and 2 jump INTO this `if` body in the original,
        // which C# forbids, so the two labels are lifted to method scope and the `if` becomes a
        // forward goto. No read, no write and no test moves.
        PsxRam.WriteU16(param_1 + 10, (ushort)sVar1);
        if (7 < sVar2)
        {
            goto LAB_80031a78;
        }

        return;

    LAB_80031a78:
        sVar2 = (short)PsxRam.ReadU16(param_1 + 6);
    code_r0x80031a80:
        PsxRam.WriteU16(param_1 + 6, (ushort)(sVar2 + 1));
    }

    // GHIDRA: FUN_80031ab8 @ 0x80031AB8 (VS.EXE)
    // 164 bytes, 0x80031AB8..0x80031B5B. Four callers, none ported.
    //
    // Four bytes at +0x22..+0x25 are set from a selector at +0x20: whichever of the four the
    // selector names gets 0x80 and the other three get 0x40. Any selector outside 0..3 returns
    // without touching anything.
    //
    // THE ORIGINAL IS NOT SYMMETRIC AND THE ASYMMETRY IS REAL. Selector 0 writes all four bytes and
    // returns from inside its own arm; selectors 1, 2 and 3 fall through to a shared
    // `+0x22 = 0x40`, and 2 and 3 additionally share `+0x23 = 0x40`. The per-arm write ORDER
    // therefore differs between the four cases. Transliterated arm by arm rather than collapsed to
    // "set one to 0x80 and the rest to 0x40", because collapsing it would change the order of four
    // stores into the same four bytes.
    internal static void FUN_80031ab8(int param_1)
    {
        short sVar1;

        sVar1 = (short)PsxRam.ReadU16(param_1 + 0x20);
        if (sVar1 == 1)
        {
            PsxRam.WriteU8(param_1 + 0x23, 0x80);
            PsxRam.WriteU8(param_1 + 0x25, 0x40);
            PsxRam.WriteU8(param_1 + 0x24, 0x40);
        }
        else
        {
            if (sVar1 < 2)
            {
                if (sVar1 != 0)
                {
                    return;
                }

                PsxRam.WriteU8(param_1 + 0x22, 0x80);
                PsxRam.WriteU8(param_1 + 0x25, 0x40);
                PsxRam.WriteU8(param_1 + 0x24, 0x40);
                PsxRam.WriteU8(param_1 + 0x23, 0x40);
                return;
            }

            if (sVar1 == 2)
            {
                PsxRam.WriteU8(param_1 + 0x24, 0x80);
                PsxRam.WriteU8(param_1 + 0x25, 0x40);
            }
            else
            {
                if (sVar1 != 3)
                {
                    return;
                }

                PsxRam.WriteU8(param_1 + 0x25, 0x80);
                PsxRam.WriteU8(param_1 + 0x24, 0x40);
            }

            PsxRam.WriteU8(param_1 + 0x23, 0x40);
        }

        PsxRam.WriteU8(param_1 + 0x22, 0x40);
    }
}
