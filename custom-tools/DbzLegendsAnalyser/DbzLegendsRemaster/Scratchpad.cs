using PsxSdkMonogame;

namespace DbzLegendsRemaster;

// JUSTIFICATION: PSX hardware adaptation only
// RELATION: the PSX scratchpad, the 1 KiB of fast RAM the console maps at 0x1F800000 -- but ONLY
// the words that TITLE.EXE and VS.EXE provably use for the SAME THING. It is one physical
// kilobyte, shared by every overlay the way 0x801FF000 is, so a symbol both overlays agree on
// belongs in one declaration. This file is that declaration, on the model of SharedHighRam.
//
// WHY IT IS NOT IN THE SDK, since that is the obvious first guess. The ADDRESS is hardware; the
// CONTENTS are the game's. What lives here is not "scratchpad word 8", it is TITLE's sprite-quad
// corner and the camera triple-buffer -- transliterated globals carrying GHIDRA: annotations and
// game-specific meaning. Moving them into PsxSdkMonogame would put runtime state inside the
// hardware adaptation layer, which the mandate's hard boundary forbids, and would force one
// title's memory layout on every other port sharing the SDK. The SDK agrees: its only mention of
// this region is LibGte.cs's own "no literal 0x1F800000 scratchpad" convention, and no SDK code
// reads or writes any address here.
//
// WHAT IS DELIBERATELY *NOT* HERE, and this is the part that departs from the tranche-4 plan.
// That plan said VS uses "the same 124 symbols" and directed a wholesale move. Checking address by
// address refuted it. The scratchpad is REUSABLE fast RAM: two overlays storing different things
// at one address is entirely legitimate on the console, and merging those would be wrong.
//
//   0x1F80012C   CONFLICT. TITLE keeps its 0..2 loading-picture counter here, read by
//                LoadingScreen, FaceImages and StageBackdrop. VS uses the same word for an
//                unrelated purpose. Each overlay keeps its own; see both declarations.
//   0x1F800120   CONFLICT. TITLE's SetupGeometry seeds it as the pending geometry offset Y that
//                its camera task promotes into 0x1F800110 once a frame. VS's BattleScene computes
//                a horizon Y into it from rsin(-angle) * DAT_1f80009c. Same slot, different
//                producers, and no evidence they agree.
//   0x1F80009C   NOT a duplication at all, and worse than one: TITLE declares VECTOR_1f800094, a
//                16-byte LibGte.VECTOR, so byte 0x9C is that vector's .vz field. VS declares a
//                separate int DAT_1f80009c over the same byte and READS it while nothing anywhere
//                writes it. That is an aliasing bug, recorded at its site rather than papered over
//                by a merge.
//   0x1F8000D0..E0  UNKNOWN, so not merged. Both SetupGeometry copies write these five words
//                identically, but only TITLE ever reads them back; VS's consumer is unported. The
//                write side agreeing proves nothing about meaning for reusable RAM -- the read
//                side is what fixes it. They stay duplicated until VS's reader exists.
//
// Everything below is proven SAME by both overlays' reads, not by their writes. The comments come
// with the declarations from TITLE_EXE/GteScratch.cs, unchanged: they are the evidence.
internal static class Scratchpad
{
    // GHIDRA: MATRIX_1f800000 @ 0x1F800000
    internal static readonly LibGte.MATRIX MATRIX_1f800000 = new();

    // GHIDRA: SVECTOR_1f80007c @ 0x1F80007C
    internal static readonly LibGte.SVECTOR SVECTOR_1f80007c = new();

    // GHIDRA: DAT_1f800084 @ 0x1F800084
    internal static short DAT_1f800084;

    // GHIDRA: DAT_1f800086 @ 0x1F800086
    internal static short DAT_1f800086;

    // GHIDRA: DAT_1f800088 @ 0x1F800088
    internal static short DAT_1f800088;

    // GHIDRA: _DAT_1f8000b4 @ 0x1F8000B4
    internal static int _DAT_1f8000b4;

    // GHIDRA: DAT_1f8000b8 @ 0x1F8000B8
    internal static int DAT_1f8000b8;

    // GHIDRA: _DAT_1f8000bc @ 0x1F8000BC
    internal static int _DAT_1f8000bc;

    // GHIDRA: _DAT_1f8000c0 @ 0x1F8000C0
    internal static int _DAT_1f8000c0;

    // GHIDRA: DAT_1f8000c4 @ 0x1F8000C4
    internal static int DAT_1f8000c4;

    // GHIDRA: DAT_1f8000c8 @ 0x1F8000C8
    internal static int DAT_1f8000c8;

    // GHIDRA: DAT_1f8000cc @ 0x1F8000CC
    internal static int DAT_1f8000cc;

    // GHIDRA: MATRIX_1f8000e4 @ 0x1F8000E4
    // The colour matrix SetupGeometry hands to SetColorMatrix. Its nine shorts sit at 0xE4, 0xE6,
    // 0xE8, 0xEA, 0xEC, 0xEE, 0xF0, 0xF2 and 0xF4, which is the m[0..8] order used below.
    internal static readonly LibGte.MATRIX MATRIX_1f8000e4 = new();

    // GHIDRA: SVECTOR_1f800104 @ 0x1F800104
    // Written as DAT_1f800104 / DAT_1f800106 / DAT_1f800108 then cast to SVECTOR * by the original
    // when it reaches RotMatrix.
    internal static readonly LibGte.SVECTOR SVECTOR_1f800104 = new();

    // GHIDRA: DAT_1f800110 @ 0x1F800110
    internal static int DAT_1f800110;

    // GHIDRA: DAT_1f800114 @ 0x1F800114
    internal static int DAT_1f800114;

    // GHIDRA: DAT_1f800118 @ 0x1F800118
    internal static int DAT_1f800118;

    // GHIDRA: DAT_1f80011c @ 0x1F80011C
    internal static int DAT_1f80011c;

    // GHIDRA: DAT_1f800124 @ 0x1F800124
    internal static int DAT_1f800124;
}
