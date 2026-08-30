using PsxSdkMonogame;

namespace DbzLegendsRemaster.TITLE_EXE;

// The camera and lighting state TITLE.EXE keeps in the PSX scratchpad, the 1 KiB of fast RAM the
// console maps at 0x1F800000. SetupGeometry @ 0x80057674 fills it and FUN_80037388 @ 0x80037388
// permutes and consumes it.
//
// These are held as typed objects rather than as raw scratchpad bytes. The port already does this
// for CdlLOC, CdlATV and MoviePlaybackState, and the SDK's GTE entry points take MATRIX / SVECTOR /
// VECTOR directly, so a byte-addressed model would need a conversion at every single call. Each
// declaration carries its scratchpad address, and the types are not guessed: the original casts
// them itself, `SetColorMatrix((MATRIX *)&DAT_1f8000e4)` and
// `RotMatrix((SVECTOR *)&DAT_1f800104, ...)`.
internal static class GteScratch
{
    // GHIDRA: MATRIX_1f800000 @ 0x1F800000
    internal static readonly LibGte.MATRIX MATRIX_1f800000 = new();

    // GHIDRA: SVECTOR_1f800020 @ 0x1F800020
    // 0x1F800020..0x1F80003F is one contiguous four-element SVECTOR array: DrawSpriteGroup
    // @ 0x80048F88 fills it with the four corners of the sprite quad and hands all four to
    // RotAverage4 in that order. This one is corner 0, the top-left.
    internal static readonly LibGte.SVECTOR SVECTOR_1f800020 = new();

    // GHIDRA: SVECTOR_1f800028 @ 0x1F800028
    // Corner 1, the top-right: vx = localX + width. Written at 0x800492E0 / 0x800492E8 / 0x800492F0.
    internal static readonly LibGte.SVECTOR SVECTOR_1f800028 = new();

    // GHIDRA: SVECTOR_1f800030 @ 0x1F800030
    // Corner 2, the bottom-left: vy = localY + height. Written at 0x800492F8 / 0x80049300 /
    // 0x80049308.
    internal static readonly LibGte.SVECTOR SVECTOR_1f800030 = new();

    // GHIDRA: SVECTOR_1f800038 @ 0x1F800038
    // Corner 3, the bottom-right. Written at 0x80049310 / 0x80049318 / 0x80049320, its vx and vy
    // copied from corner 1 and corner 2 rather than recomputed.
    internal static readonly LibGte.SVECTOR SVECTOR_1f800038 = new();

    // GHIDRA: VECTOR_1f800048 @ 0x1F800048
    internal static readonly LibGte.VECTOR VECTOR_1f800048 = new();

    // GHIDRA: SVECTOR_1f800058 @ 0x1F800058
    // The RotMatrix input, reused twice per sprite record by DrawSpriteGroup: first as the per-record
    // flip/spin (flipX ? 0x800 : 0, flipY ? 0x800 : 0, record.rotZ), then as the group-wide
    // rotation (param_5 & 0xfff, param_6, param_7). Written at 0x8004922C, 0x8004927C, 0x8004928C,
    // 0x800492A0, 0x800492B0, 0x80049368, 0x80049378 and 0x80049380.
    internal static readonly LibGte.SVECTOR SVECTOR_1f800058 = new();

    // GHIDRA: VECTOR_1f800060 @ 0x1F800060
    // The ScaleMatrix vector: vx = record.scaleX + param_8 (0x80049240), vy = record.scaleY +
    // param_9 (0x80049260), vz = the hard-coded 0x1000 at 0x8004924C / 0x80049254.
    internal static readonly LibGte.VECTOR VECTOR_1f800060 = new();

    // GHIDRA: DAT_1f800074 @ 0x1F800074
    // PARTIAL: RotAverage4's (long *) p argument — the `stdp` depth-cue readback. Its address is
    // materialised at 0x8004941C. A cross-reference search over TITLE.EXE finds exactly that one
    // reference, so it is a write-only sink: nothing in the overlay ever reads it back.
    internal static readonly int[] DAT_1f800074 = new int[1];

    // GHIDRA: DAT_1f800078 @ 0x1F800078
    // PARTIAL: passed to RotTrans as its (long *) flag argument; only ever written by the callee.
    // DrawSpriteGroup @ 0x80048F88 passes it to RotAverage4 in the same role.
    internal static readonly int[] DAT_1f800078 = new int[1];

    // GHIDRA: SVECTOR_1f80007c @ 0x1F80007C
    internal static readonly LibGte.SVECTOR SVECTOR_1f80007c = new();

    // GHIDRA: DAT_1f800084 @ 0x1F800084
    internal static short DAT_1f800084;

    // GHIDRA: DAT_1f800086 @ 0x1F800086
    internal static short DAT_1f800086;

    // GHIDRA: DAT_1f800088 @ 0x1F800088
    internal static short DAT_1f800088;

    // GHIDRA: DAT_1f80008c @ 0x1F80008C
    internal static short DAT_1f80008c;

    // GHIDRA: DAT_1f80008e @ 0x1F80008E
    internal static short DAT_1f80008e;

    // GHIDRA: DAT_1f800090 @ 0x1F800090
    internal static short DAT_1f800090;

    // GHIDRA: VECTOR_1f800094 @ 0x1F800094
    internal static readonly LibGte.VECTOR VECTOR_1f800094 = new();

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

    // GHIDRA: DAT_1f8000d0 @ 0x1F8000D0
    internal static int DAT_1f8000d0;

    // GHIDRA: DAT_1f8000d4 @ 0x1F8000D4
    internal static int DAT_1f8000d4;

    // GHIDRA: DAT_1f8000d8 @ 0x1F8000D8
    internal static int DAT_1f8000d8;

    // GHIDRA: DAT_1f8000dc @ 0x1F8000DC
    internal static int DAT_1f8000dc;

    // GHIDRA: DAT_1f8000e0 @ 0x1F8000E0
    internal static int DAT_1f8000e0;

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

    // GHIDRA: DAT_1f800120 @ 0x1F800120
    internal static int DAT_1f800120;

    // GHIDRA: DAT_1f800124 @ 0x1F800124
    internal static int DAT_1f800124;

    // GHIDRA: DAT_1f800128 @ 0x1F800128
    internal static int DAT_1f800128;

    // GHIDRA: DAT_1f80012c @ 0x1F80012C
    // Read by main @ 0x800581DC and FUN_80058a9c as an index; written from DAT_801ff10e.
    internal static uint DAT_1f80012c;
}
