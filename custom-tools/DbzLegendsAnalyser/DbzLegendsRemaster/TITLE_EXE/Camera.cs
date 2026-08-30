using PsxSdkMonogame;
using static PsxSdkMonogame.LibGte;

namespace DbzLegendsRemaster.TITLE_EXE;

// The camera. THIS FILE IS DELIBERATELY PARTIAL.
//
// The task that owns the camera is LAB_80027f5c @ 0x80027F5C, registered by FUN_80058a9c as
// `CreateTask(&LAB_80027f5c, 0x55, 0x13, 0, 0, DAT_800798a0)`. It is the PRODUCER for the nine
// scratchpad words FUN_80037388 consumes at the top of every frame (GteScratch.DAT_1f800084/86/88,
// DAT_1f8000c4/c8/cc/d0, DAT_1f800120/124). It is NOT transliterated here. What blocks it, and the
// evidence for each blocker, is recorded at the bottom of this file under "WHY LAB_80027f5c IS NOT
// HERE". Nothing in this file guesses at it.
//
// What IS here is the closed part: the three geometry helpers LAB_80027f5c calls, and the private
// camera state block it owns. Both are settled by first-hand Ghidra evidence and neither depends on
// any unported producer.
//
// A NOTE ON THE POINTER PARAMETERS. FUN_8003bec8, FUN_8003c108 and FUN_8003d724 take `short *` in
// the original. They are spelled here as `int` PSX addresses, which is this port's standing
// convention for a pointer parameter (SpriteRenderer.FUN_80048f88, SelectScreenSetup.FUN_8004737c,
// PrimitivePools.FUN_80056dc0 all do the same) and the body reads through PsxRam exactly as one
// MIPS `lh` each. That convention is correct for the call sites that pass a real RAM address
// (LAB_80027f5c passes `actor + 0x114`; FUN_8002417c passes `&param_1->rect_114`). It is NOT
// sufficient for the call sites that pass a STACK triple — LAB_80027f5c does that four times, with
// `&uStack_108`, `&sStack_100`, `asStack_f0` and `asStack_e8`. PsxRam cannot resolve a stack
// address. Which way that is bridged is a decision that belongs to the LAB_80027f5c port, because
// only that function's own frame layout can settle it; it is listed as blocker B4 below and is NOT
// pre-empted here.
internal static class Camera
{
    // ========================================================================================
    // GTE scratch globals used by the look-at helper.
    //
    // Cross-referenced program-wide, so the sharing is measured rather than assumed:
    //   SVECTOR_800832fc  3 references, ALL inside FUN_8003c108. Nothing else in TITLE.EXE
    //                     touches it, which is what makes its `vy` closed: FUN_8003c108 writes
    //                     only vx and vz, so vy is whatever .bss was loaded with, i.e. 0, on the
    //                     console as well as here. (It is dead anyway: the matrix in play is a
    //                     pure Y rotation, so m[2][1] is 0 and vy cannot reach the only component
    //                     read back, VECTOR_800a8a08.vz.)
    //   VECTOR_800a8a08   1 reference, the RotTrans output parameter in FUN_8003c108.
    //   MATRIX_8007ad48   10 references: 4 from FUN_8003c108, 3 from FUN_8004a220 and 3 from
    //                     FUN_8004a6c8. The last two are NOT ported. It is shared GTE scratch, so
    //                     whoever ports them must use THIS declaration rather than make a second
    //                     one.
    // ========================================================================================

    // GHIDRA: SVECTOR_800832fc @ 0x800832FC
    internal static readonly LibGte.SVECTOR SVECTOR_800832fc = new();

    // GHIDRA: MATRIX_8007ad48 @ 0x8007AD48
    internal static readonly LibGte.MATRIX MATRIX_8007ad48 = new();

    // GHIDRA: VECTOR_800a8a08 @ 0x800A8A08
    internal static readonly LibGte.VECTOR VECTOR_800a8a08 = new();

    // GHIDRA: VECTOR_800a8a08.pad @ 0x800A8A14
    // JUSTIFICATION: C# language bridge only
    // RELATION: the original hands RotTrans `&VECTOR_800a8a08.pad` as its `long *flag` sink. The
    // SDK's RotTrans takes that sink as an `int[]`, and C# cannot take the address of a field, so
    // the pad word is held as its own one-element array. It is write-only: the program-wide
    // cross-reference count for 0x800A8A08 is 1 — the RotTrans call itself — so no code ever reads
    // the flag back, and nothing is lost by not mirroring it into VECTOR_800a8a08.pad.
    private static readonly int[] VECTOR_800a8a08_pad = new int[1];

    // GHIDRA: FUN_8003bec8 @ 0x8003BEC8
    // 3D distance between two short triples, returned narrowed to short.
    // Six callers program-wide: four from inside LAB_80027f5c (0x800284BC, 0x80028938, 0x80029230,
    // 0x80029240) plus FUN_8002417c @ 0x800243C8 and FUN_8004af78 @ 0x8004AFB4. It is therefore a
    // SHARED geometry helper and not camera-private; it lives here only because this is the file
    // this pass owns. It should move to a geometry file the first time a second caller is ported.
    // Its one callee, SquareRoot0, is REAL in the C# SDK.
    // NOT A BUG TO FIX (rule 12): the 0x7FFF wrap fold is applied to the X and Z components but NOT
    // to Y — `param_2[1] - param_1[1]` goes into the sum of squares raw. That asymmetry is what the
    // original does; FUN_8003c108 below folds all the components it uses. Reproduced as-is.
    internal static int FUN_8003bec8(int param_1, int param_2)
    {
        int lVar1;
        int local_18;
        int local_14;

        local_18 = unchecked((short)PsxRam.ReadU16(param_2)) - unchecked((short)PsxRam.ReadU16(param_1));
        if (local_18 < 0)
        {
            local_18 = -local_18;
        }
        if (0x7fff < local_18)
        {
            local_18 = 0xffff - local_18;
        }
        local_14 = unchecked((short)PsxRam.ReadU16(param_2 + 4)) - unchecked((short)PsxRam.ReadU16(param_1 + 4));
        if (local_14 < 0)
        {
            local_14 = -local_14;
        }
        if (0x7fff < local_14)
        {
            local_14 = 0xffff - local_14;
        }
        lVar1 = SquareRoot0(local_18 * local_18 +
                            (unchecked((short)PsxRam.ReadU16(param_2 + 2)) -
                             unchecked((short)PsxRam.ReadU16(param_1 + 2))) *
                            (unchecked((short)PsxRam.ReadU16(param_2 + 2)) -
                             unchecked((short)PsxRam.ReadU16(param_1 + 2))) +
                            local_14 * local_14);
        return (short)lVar1;
    }

    // GHIDRA: FUN_8003c108 @ 0x8003C108
    // Look-at angles from param_1 to param_2, written as a three-short triple at param_3:
    //   param_3[0] = 0, param_3[1] = yaw = ratan2(dx, dz) & 0xfff,
    //   param_3[2] = pitch = ratan2(dy, rotated z) & 0xfff.
    // Callees ratan2, PushMatrix, RotMatrixY, SetRotMatrix, SetTransMatrix, RotTrans and PopMatrix
    // are all present and REAL in the C# SDK. RotMatrixY's negative-angle branch is closed and this
    // caller never reaches it anyway: the angle handed over is 0x1000 minus a value already masked
    // to 0xfff, so it is in [1, 0x1000].
    // The statement order below is the original's, including `*param_3 = 0` landing in the middle
    // of the matrix stores rather than beside the other two param_3 writes.
    internal static void FUN_8003c108(int param_1, int param_2, int param_3)
    {
        int lVar1;
        int local_18;
        int local_14;
        int local_10;
        int local_c;

        local_18 = unchecked((short)PsxRam.ReadU16(param_2)) - unchecked((short)PsxRam.ReadU16(param_1));
        local_14 = local_18;
        if (local_18 < 0)
        {
            local_14 = -local_18;
        }
        if (0x7fff < local_14)
        {
            local_14 = 0xffff - local_14;
            if (0 < local_18)
            {
                local_14 = -local_14;
            }
            local_18 = local_14;
        }
        local_10 = unchecked((short)PsxRam.ReadU16(param_2 + 4)) - unchecked((short)PsxRam.ReadU16(param_1 + 4));
        local_c = local_10;
        if (local_10 < 0)
        {
            local_c = -local_10;
        }
        if (0x7fff < local_c)
        {
            local_c = 0xffff - local_c;
            if (0 < local_10)
            {
                local_c = -local_c;
            }
            local_10 = local_c;
        }
        SVECTOR_800832fc.vx = (short)local_18;
        SVECTOR_800832fc.vz = (short)local_10;
        lVar1 = ratan2((short)local_18, (short)local_10);
        PsxRam.WriteU16(param_3 + 2, (ushort)((ushort)lVar1 & 0xfff));
        PushMatrix();

        // The identity load below is five `sw` instructions, each covering two adjacent shorts:
        //   0x8003C2E4 -> offset 0x00  m[0][0] = 0x1000, m[0][1] = 0
        //   0x8003C300 -> offset 0x04  m[0][2] = 0,      m[1][0] = 0
        //   0x8003C2D8 -> offset 0x08  m[1][1] = 0x1000, m[1][2] = 0
        //   0x8003C2F8 -> offset 0x0C  m[2][0] = 0,      m[2][1] = 0
        //   0x8003C2CC -> offset 0x10  m[2][2] = 0x1000, and the MATRIX pad at 0x12 = 0
        // Ghidra prints that last pad write as `MATRIX_8007ad48._18_2_ = 0` — offset 18 decimal,
        // which is 0x12, the two bytes between m[3][3] and t[3]. The SDK's LibGte.MATRIX models
        // only `short[9] m` and `int[3] t`, so it has no field there and the write has NO C#
        // counterpart. It is omitted rather than invented: nothing in TITLE.EXE reads offset 0x12.
        // `t` is deliberately NOT touched here — the original does not initialise it either, and
        // SetTransMatrix below therefore loads whatever the shared global holds.
        MATRIX_8007ad48.m[8] = 0x1000;
        MATRIX_8007ad48.m[4] = 0x1000;
        MATRIX_8007ad48.m[5] = 0;
        MATRIX_8007ad48.m[0] = 0x1000;
        MATRIX_8007ad48.m[1] = 0;
        PsxRam.WriteU16(param_3, 0);
        MATRIX_8007ad48.m[6] = 0;
        MATRIX_8007ad48.m[7] = 0;
        MATRIX_8007ad48.m[2] = 0;
        MATRIX_8007ad48.m[3] = 0;
        RotMatrixY((uint)(0x1000 - unchecked((short)PsxRam.ReadU16(param_3 + 2))), MATRIX_8007ad48);
        SetRotMatrix(MATRIX_8007ad48);
        SetTransMatrix(MATRIX_8007ad48);
        RotTrans(SVECTOR_800832fc, VECTOR_800a8a08, VECTOR_800a8a08_pad);
        PopMatrix();
        lVar1 = ratan2(unchecked((short)PsxRam.ReadU16(param_2 + 2)) -
                       unchecked((short)PsxRam.ReadU16(param_1 + 2)),
                       VECTOR_800a8a08.vz);
        PsxRam.WriteU16(param_3 + 4, (ushort)((ushort)lVar1 & 0xfff));
    }

    // GHIDRA: FUN_8003d724 @ 0x8003D724
    // Polar offset: rotates the vector (param_5, 0, 0) by the yaw/pitch triple at param_1 — biased
    // by param_4 and param_3 respectively, with the yaw turned a quarter circle by the -0x400 — and
    // writes the result into param_2. Its callers inside LAB_80027f5c pass the triple FUN_8003c108
    // filled, so param_1[1] is the yaw and param_1[2] the pitch; param_1[0] is never read.
    // Callees PushMatrix, RotMatrix, SetRotMatrix, SetTransMatrix, RotTrans and PopMatrix are all
    // present and REAL in the C# SDK. RotMatrix carries a PARTIAL note about negative vx/vy/vz, and
    // it is not reachable from here: every angle handed to it is masked with 0xfff.
    internal static void FUN_8003d724(int param_1, LibGte.VECTOR param_2, short param_3, short param_4,
        short param_5)
    {
        // The original declares these three uninitialised on the stack. C# has to allocate them, so
        // they start zeroed where the console started them as frame garbage. That is not observable
        // here: RotMatrix overwrites all nine entries of MStack_40.m, the three `t` words are
        // explicitly zeroed below, local_48's vx/vy/vz are all assigned before any read, and
        // alStack_20 is a write-only flag sink.
        LibGte.SVECTOR local_48 = new();
        LibGte.MATRIX MStack_40 = new();
        int[] alStack_20 = new int[2];

        PushMatrix();
        local_48.vx = 0;
        local_48.vy = (short)((uint)(unchecked((short)PsxRam.ReadU16(param_1 + 2)) + param_4) - 0x400U & 0xfff);
        local_48.vz = (short)(unchecked((short)PsxRam.ReadU16(param_1 + 4)) + param_3 & 0xfff);
        RotMatrix(local_48, MStack_40);
        MStack_40.t[2] = 0;
        MStack_40.t[1] = 0;
        MStack_40.t[0] = 0;
        local_48.vy = 0;
        local_48.vz = 0;
        local_48.vx = param_5;
        SetTransMatrix(MStack_40);
        SetRotMatrix(MStack_40);
        RotTrans(local_48, param_2, alStack_20);
        PopMatrix();
    }

    // ========================================================================================
    // The private camera state block, 0x800831C4..0x80083216.
    //
    // Every one of these is reached by LAB_80027f5c through its own $gp block ($gp = 0x800831B4,
    // cross-checked three times against the decompilation). All 18 references to DAT_800831dc in
    // the whole program are inside LAB_80027f5c, so the block is private to it. None of these
    // existed in the C# port before this file.
    //
    // WIDTH is measured: it comes from the MIPS opcode itself (lb/lh/lw/lbu/lhu/sb/sh/sw) over
    // every load and store in 0x80027F5C..0x80029923, not from the decompiler's C types.
    // SIGNEDNESS is NOT measured. Several of these 16-bit slots are read both ways in the same
    // function — DAT_800831c4 is loaded with `lhu` at one site and used as `(short)` at another —
    // so the C# type below fixes the STORAGE WIDTH only. The transliteration of LAB_80027f5c must
    // cast at each individual use site exactly as the decompilation does, and must not lean on the
    // declared type to do it.
    //
    // THE INITIAL VALUES ARE MEASURED, not assumed. The whole block sits inside .sdata
    // (0x800831B4..0x8008330F), which the memory map reports as initialized:true, so the values
    // below are the ones the executable image actually carries rather than a zeroed .bss. The 84
    // bytes at 0x800831C4 were read straight out of the loaded program: every slot is 0 except
    // DAT_800831fe = 0x200 (bytes 00 02 at 0x800831FE) and DAT_8008320a = 1 (bytes 01 00 at
    // 0x8008320A). Both agree with the way LAB_80027f5c resets them - it writes 0x200 into
    // DAT_800831fe and 1 into DAT_8008320a at several sites.
    //
    // These are declared and not yet read, because their only consumer is the function that is
    // blocked. They are here so the block has one place to land, with its addresses recorded.
    // ========================================================================================

    // GHIDRA: DAT_800831c4 @ 0x800831C4 — target camera X. 2 bytes, 10 reads, 6 writes.
    internal static short DAT_800831c4 = 0;

    // GHIDRA: DAT_800831c6 @ 0x800831C6 — target camera Y. 2 bytes, 12 reads, 12 writes.
    internal static short DAT_800831c6 = 0;

    // GHIDRA: DAT_800831c8 @ 0x800831C8 — target camera Z. 2 bytes, 10 reads, 6 writes.
    internal static short DAT_800831c8 = 0;

    // GHIDRA: DAT_800831cc @ 0x800831CC — delta X against last frame's actor position. 2 bytes.
    internal static short DAT_800831cc = 0;

    // GHIDRA: DAT_800831ce @ 0x800831CE — delta Y. 2 bytes.
    internal static short DAT_800831ce = 0;

    // GHIDRA: DAT_800831d0 @ 0x800831D0 — delta Z. 2 bytes.
    internal static short DAT_800831d0 = 0;

    // GHIDRA: DAT_800831d4 @ 0x800831D4 — last frame's actor X, written at the very end. 2 bytes.
    internal static short DAT_800831d4 = 0;

    // GHIDRA: DAT_800831d6 @ 0x800831D6 — last frame's actor Y. 2 bytes.
    internal static short DAT_800831d6 = 0;

    // GHIDRA: DAT_800831d8 @ 0x800831D8 — last frame's actor Z. 2 bytes.
    internal static short DAT_800831d8 = 0;

    // GHIDRA: DAT_800831dc @ 0x800831DC — THE CAMERA MODE. 4 bytes, 4 reads, 14 writes. The whole
    // of LAB_80027f5c is a switch on it. Seventeen values are observed in the decompilation: 1, 2,
    // 4, 8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x200, 0x400, 0x800, 0x1000, 0x2000, 0x4000, 0x8000,
    // 0x10000, plus 0. What any of them MEANS is not evidenced and is not claimed here.
    internal static uint DAT_800831dc = 0;

    // GHIDRA: DAT_800831e0 @ 0x800831E0 — the previous frame's mode, set from DAT_800831dc at the
    // end of each pass. 4 bytes.
    internal static uint DAT_800831e0 = 0;

    // GHIDRA: DAT_800831e4 @ 0x800831E4 — scratch, yaw numerator. 4 bytes, 0 reads, 2 writes.
    internal static int DAT_800831e4 = 0;

    // GHIDRA: DAT_800831e8 @ 0x800831E8 — scratch, distance numerator. 4 bytes, 0 reads, 3 writes.
    internal static int DAT_800831e8 = 0;

    // GHIDRA: DAT_800831ec @ 0x800831EC — scratch, X interpolation numerator. 4 bytes.
    internal static int DAT_800831ec = 0;

    // GHIDRA: DAT_800831f0 @ 0x800831F0 — scratch, Y interpolation numerator. 4 bytes.
    internal static int DAT_800831f0 = 0;

    // GHIDRA: DAT_800831f4 @ 0x800831F4 — scratch, Z interpolation numerator. 4 bytes.
    internal static int DAT_800831f4 = 0;

    // GHIDRA: DAT_800831fa @ 0x800831FA — mode dwell counter, stepped by +-0x3c and 0x10. 2 bytes.
    internal static short DAT_800831fa = 0;

    // GHIDRA: DAT_800831fc @ 0x800831FC — folded yaw error: `& 0xfff`, then `^ 0xf000` above 0x800.
    // 2 bytes.
    internal static short DAT_800831fc = 0;

    // GHIDRA: DAT_800831fe @ 0x800831FE — target distance, clamped to 0xdff. 2 bytes.
    // Image value 0x200, measured at 0x800831FE.
    internal static short DAT_800831fe = 0x200;

    // GHIDRA: DAT_80083200 @ 0x80083200 — distance error. 2 bytes.
    internal static short DAT_80083200 = 0;

    // GHIDRA: DAT_80083202 @ 0x80083202 — X error. 2 bytes.
    internal static short DAT_80083202 = 0;

    // GHIDRA: DAT_80083204 @ 0x80083204 — Y error. 2 bytes.
    internal static short DAT_80083204 = 0;

    // GHIDRA: DAT_80083206 @ 0x80083206 — Z error. 2 bytes.
    internal static short DAT_80083206 = 0;

    // GHIDRA: DAT_80083208 @ 0x80083208 — the previous frame's actor index. 2 bytes.
    internal static short DAT_80083208 = 0;

    // GHIDRA: DAT_8008320a @ 0x8008320A — hit/shake ramp counter, clamped to 0x10. 2 bytes,
    // 8 reads, 10 writes. Image value 1, measured at 0x8008320A.
    internal static short DAT_8008320a = 1;

    // GHIDRA: DAT_8008320c @ 0x8008320C — intro countdown, 0x50 down, gated on `0x10 <`. 2 bytes.
    internal static short DAT_8008320c = 0;

    // GHIDRA: DAT_8008320e @ 0x8008320E — intro distance, 0x620 stepped by -4 a frame. 2 bytes.
    internal static short DAT_8008320e = 0;

    // GHIDRA: DAT_80083210 @ 0x80083210 — intro height, 0x160 stepped by -9, floor -0xc0. 2 bytes.
    internal static short DAT_80083210 = 0;

    // GHIDRA: DAT_80083212 @ 0x80083212 — intro pitch, 0x100 stepped by -1, floor 0xd0. 2 bytes.
    internal static short DAT_80083212 = 0;

    // GHIDRA: DAT_80083214 @ 0x80083214 — shake X: negated each frame, or `~` when negative.
    // 2 bytes.
    internal static short DAT_80083214 = 0;

    // GHIDRA: DAT_80083216 @ 0x80083216 — shake Y, same treatment. 2 bytes.
    internal static short DAT_80083216 = 0;

    // ========================================================================================
    // WHY LAB_80027f5c IS NOT HERE
    //
    // The scoping pass that preceded this file settled the boundary from the raw instruction
    // stream (6800 bytes read out of Ghidra and decoded offline) and from Ghidra's own reference
    // database. Four things block the transliteration. None of them is an SDK gap.
    //
    // B1  THE ADDRESS RANGE IS NOT ONE FUNCTION. The undefined gap 0x80027F5C..0x800299BB holds
    //     THREE. `jr ra` occurs at exactly 0x8002991C, 0x80029968 and 0x800299B4, each followed by
    //     a delay-slot nop and then a fresh `addiu sp,sp,-0x20` prologue, and there is no
    //     fallthrough between them. LAB_80027f5c itself is 0x80027F5C..0x80029923 (6600 bytes, 832
    //     decompiled lines). The other two, at 0x80029924 and 0x80029970, are 76-byte
    //     "spawn FUN_80029aec with mode N" helpers with NO incoming reference anywhere in the
    //     program — dead code as far as the reference database can see. They are not ported here
    //     because porting them would add dead code, and because Ghidra will not decompile them at
    //     all (the bytes are undefined data), so the only reading of them is a hand decode.
    //
    // B2  IT IS NOT CALLEE-FREE. The instruction stream contains SEVEN `jal` to three distinct
    //     targets: 0x8003BEC8 four times, 0x8003C108 once, 0x8003D724 twice. THAT IS WHAT THIS
    //     FILE PORTS. B2 is therefore discharged.
    //
    // B3  ITS TWO INPUT POINTERS HAVE NO PRODUCER IN THE PORT.
    //       DAT_800833dc @ 0x800833DC — the world/actor block. 8 reads here, and program-wide it
    //         is written EXACTLY ONCE, at 0x8004C0D8 inside FUN_8004c0b4, which Ghidra records
    //         with zero callers and which sits with the undefined LAB_8004c010 body. Not ported.
    //       DAT_80083644 @ 0x80083644 — read once, used twice as `*(ushort *)(p + 0x76)`.
    //         Program-wide it is written EXACTLY ONCE, at 0x80035830, inside a region Ghidra has
    //         not disassembled at all (it begins at LAB_80035814).
    //     Every actor read in LAB_80027f5c resolves through those two. PsxRam returns 0 for an
    //     unresolved address, so a port made today would not crash — it would run and quietly
    //     produce garbage into the nine scratchpad words FUN_80037388 consumes, which is worse.
    //     It also means there is no bench that can drive the function and no console capture to
    //     compare against, so the port could not be verified either.
    //
    // B4  THE STACK-TRIPLE CALL SITES ARE UNBRIDGED. See the note at the top of this file.
    //
    // TWO FURTHER SEMANTICS, settled here so the next pass does not have to redo them:
    //
    //   $s4 IS READ WITHOUT BEING WRITTEN ON SOME PATHS. Ghidra renders it `unaff_s4`. A scan of
    //   all 1650 instructions of 0x80027F5C..0x80029923 finds `sw s4,0x110(sp)` at 0x80027F74 and
    //   `lw s4,0x110(sp)` at 0x80029904 — ordinary callee-saved discipline, with NO initialisation
    //   at entry — then 11 writes in the body (0x800289F4, 0x80028A74, 0x80028A80, 0x80028AFC,
    //   0x80028B04, 0x80028E6C, 0x80028FD4, 0x80028FE4, 0x80029248, 0x80029398, 0x800293D0) and 6
    //   reads, all after LAB_800293b0 (`andi v0,s4,1` at 0x80029524 and 0x8002952C, `andi v0,s4,6`
    //   at 0x80029638, `andi v0,s4,4` at 0x800296AC, 0x80029750 and 0x800297F4). So the hazard is
    //   real at the instruction level. Walking the decompilation's dispatch shows every value
    //   DAT_800831dc can actually hold does write $s4 before the read, either in its own arm or
    //   through the `(DAT_800831dc & 0x7c0) != 0` block at LAB_800293b0 — the uncovered paths are
    //   the ones for values the switch cannot produce. That makes it a STRUCTURAL gap rather than
    //   an observable bug, but it is an argument, not a measurement, and C#'s definite-assignment
    //   rule will force whoever ports this to pick an initial value. That pick has to be declared
    //   as a deviation; it must not be passed off as a transliteration.
    //
    //   THE TWO FOLDED CONSTANT BRANCHES ARE ARTEFACTS, NOT CODE. Line 75 of the decompilation,
    //   `if (false) { ... }`, and line 500, `(unaff_s4 = 3, false)`, are both Ghidra renderings and
    //   not original constructs. The second one is closed: `li s4,5` at 0x80028AFC immediately
    //   followed by `li s4,3` at 0x80028B04 is the plain shape `s4 = 5; if (state == 2) { s4 = 3;
    //   ... }`, with the comma expression being how the decompiler spells the assignment that
    //   happens on the way into the else arm. The first one has not been decoded and is still open.
    //
    // RECOMMENDED ORDER FROM HERE: settle LAB_8004c010 / FUN_8004c0b4 so DAT_800833dc has a
    // producer, and 0x80035814 so DAT_80083644 has one, then come back. B2 is already done.
    //
    // ONE MORE CORRECTION TO THE BRIEF THIS FILE WAS WRITTEN FROM. LAB_80027f5c does NOT reach a
    // task context through PTR_80083224 + 8 the way TitleScreenTask.FUN_80021e28 does. The
    // contextSize FUN_80058a9c passes to CreateTask is ZERO, and the decoded global-access table
    // over all 1650 instructions contains no access to 0x80083224 at all — that address would be
    // `lw rt,0x70(gp)`, an encoding that never occurs in the range. It is a task callback that
    // ignores the task system and keeps all of its state in the block declared above.
    //
    // REGISTRATION. Nothing in this file is wired into a task list, and TITLE_EXE_exe.cs is owned
    // by another pass this round. When LAB_80027f5c is eventually ported, the one line that
    // registers it, from FUN_80058a9c line 35, is:
    //     CreateTask(&LAB_80027f5c, 0x55, 0x13, 0, 0, DAT_800798a0);
    // with DAT_800798a0 = g_TaskListHead[0x13] (g_TaskListHead @ 0x80079854; 0x800798A0 - 0x80079854
    // = 0x4C = 19 * 4), which agrees with the list index 0x13.
    // ========================================================================================
}
