using static PsxSdkMonogame.LibEtc;

namespace DbzLegendsRemaster.VS_EXE;

// Pad handling for VS.EXE. FUN_80061800 @ 0x80061800 samples the pad, derives rising edges, runs an
// auto-repeat, then remaps the raw hardware bits through the configurable tables the bootstrap left
// in high RAM.
//
// This is TITLE.EXE's ProcessPadInput @ 0x800578A8 word for word: same fourteen-entry mask table,
// same two-iteration pointer walk, same seven-frame repeat threshold, same pair of remap tables at
// 0x801FF020 / 0x801FF03C. Only the destination globals differ, because the two overlays are linked
// separately. The correspondence, address by address:
//
//   TITLE.EXE                     VS.EXE
//   DAT_800835dc  0x800835DC  ->  DAT_8008d518  0x8008D518   raw pad word, per port
//   g_PadNewlyPressed 0x800834FC-> DAT_8008d43c  0x8008D43C   rising edges
//   DAT_8008338c  0x8008338C  ->  DAT_8008d2d0  0x8008D2D0   held-button memory
//   g_PadHoldFrames 0x80083394 -> DAT_8008d2d8  0x8008D2D8   repeat counter
//   DAT_800835f0  0x800835F0  ->  DAT_8008d52c  0x8008D52C   published output
//   DAT_80083478  0x80083478  ->  DAT_8008d3b8  0x8008D3B8   remapped port 1
//   DAT_8008347c  0x8008347C  ->  DAT_8008d3bc  0x8008D3BC   remapped port 2
//   DAT_8008346c  0x8008346C  ->  DAT_8008d3ac  0x8008D3AC   remapped rising edge, port 1
//   DAT_80083470  0x80083470  ->  DAT_8008d3b0  0x8008D3B0   remapped rising edge, port 2
//   g_PadButtonMaskTable 0x8007AD0C -> DAT_80084c68 0x80084C68  fourteen hardware masks
//
// VS.EXE takes the libetc pad, not the BIOS pad SELECT.EXE uses: main @ 0x80062134 calls
// PadInit @ 0x80079B5C, and this function calls PadRead @ 0x80079BBC. So PsxSdkMonogame's LibEtc
// serves it unchanged.
//
// Ghidra carries a label `SpuInit @ 0x800617E0` on the four-instruction thunk that wraps this
// function. That label is a false positive — the thunk does nothing but `FUN_80061800(0)`, and the
// real libspu wrapper is FUN_8006DC54. The name is not used here.
//
// The globals below are addressed by the original as adjacent pairs — DAT_8008d51c is
// DAT_8008d518 + 4, DAT_8008d440 is DAT_8008d43c + 4, and so on — with the loop walking them by
// pointer. They are kept as two-entry arrays so that indexing stays the original's.
internal static class PadInput
{
    // GHIDRA: DAT_8008d518 @ 0x8008D518 (VS.EXE)
    // [0] is the whole word PadRead returns; [1] is its high halfword, i.e. port 2. The second
    // entry is DAT_8008d51c @ 0x8008D51C, which the original reaches as &DAT_8008d51c + param_1.
    internal static readonly uint[] DAT_8008d518 = new uint[2];

    // GHIDRA: DAT_8008d43c @ 0x8008D43C (VS.EXE)
    // Rising edges, one entry per port; [1] is DAT_8008d440 @ 0x8008D440. The C# name comes from
    // TITLE.EXE, where the same global is named g_PadNewlyPressed; the Ghidra symbol here is still
    // raw.
    internal static readonly uint[] g_PadNewlyPressed = new uint[2];

    // GHIDRA: DAT_8008d2d0 @ 0x8008D2D0 (VS.EXE)
    // Held-button memory driving the auto-repeat.
    internal static readonly uint[] DAT_8008d2d0 = new uint[2];

    // GHIDRA: DAT_8008d2d8 @ 0x8008D2D8 (VS.EXE)
    // Auto-repeat counter, one per port. Named g_PadHoldFrames in TITLE.EXE; raw here.
    internal static readonly uint[] g_PadHoldFrames = new uint[2];

    // GHIDRA: DAT_8008d52c @ 0x8008D52C (VS.EXE)
    // What the rest of the game reads: the rising edge for the first seven frames a button is held,
    // then the held state itself, which is what makes a held direction repeat.
    internal static readonly uint[] DAT_8008d52c = new uint[2];

    // GHIDRA: DAT_8008d3b8 @ 0x8008D3B8 (VS.EXE)
    internal static uint DAT_8008d3b8;

    // GHIDRA: DAT_8008d3bc @ 0x8008D3BC (VS.EXE)
    internal static uint DAT_8008d3bc;

    // GHIDRA: DAT_8008d3ac @ 0x8008D3AC (VS.EXE)
    internal static uint DAT_8008d3ac;

    // GHIDRA: DAT_8008d3b0 @ 0x8008D3B0 (VS.EXE)
    internal static uint DAT_8008d3b0;

    // GHIDRA: DAT_80084c68 @ 0x80084C68 (VS.EXE)
    // The fourteen hardware button masks, read out of .data. Read back byte for byte from the
    // image: 20 00 80 00 10 00 40 00 00 20 00 80 00 10 00 40 00 01 00 08 08 00 02 00 04 00 01 00.
    // Identical to TITLE.EXE's table at 0x8007AD0C, and identical to what SLPS_003.55's
    // FUN_8002165c writes into the remap tables, so the mapping starts as the identity and only
    // differs once the player reconfigures it. The C# name comes from TITLE.EXE; the Ghidra symbol
    // here is still raw.
    private static readonly ushort[] g_PadButtonMaskTable =
    {
        0x0020, 0x0080, 0x0010, 0x0040, 0x2000, 0x8000, 0x1000,
        0x4000, 0x0100, 0x0800, 0x0008, 0x0002, 0x0004, 0x0001,
    };

    // Index of the two remap tables inside SHORT_ARRAY_801ff000: the original loads &DAT_801ff020
    // and &DAT_801ff03c as ushort*, and 0x801FF020 - 0x801FF000 = 0x20 bytes = 0x10 shorts,
    // 0x801FF03C - 0x801FF000 = 0x3C bytes = 0x1E shorts. SELECT.EXE writes both halves; this is
    // the read side of the same contract.
    private const int RemapPort1Index = 0x10;
    private const int RemapPort2Index = 0x1E;

    // GHIDRA: FUN_80061800 @ 0x80061800 (VS.EXE)
    // This is the ProcessPadInput of TITLE.EXE word for word; the C# name comes from there, the
    // Ghidra symbol is still raw. Both call sites in VS.EXE pass 0 — main @ 0x8006251C and the
    // mislabelled SpuInit thunk @ 0x800617E8.
    internal static void ProcessPadInput(uint playerIndex)
    {
        uint[] local_20 = new uint[2];

        playerIndex = playerIndex & 0xffff;
        local_20[playerIndex] = DAT_8008d518[playerIndex];
        uint uVar2 = PadRead((int)playerIndex);
        DAT_8008d518[playerIndex] = uVar2;

        // The original writes index 0 here unconditionally, not [playerIndex], even though every
        // neighbouring statement is indexed. Reproduced, not corrected.
        g_PadNewlyPressed[0] = DAT_8008d518[0] ^ (local_20[0] & DAT_8008d518[0]);

        local_20[playerIndex + 1] = DAT_8008d518[playerIndex + 1];
        uint uVar3 = (ushort)(DAT_8008d518[playerIndex] >> 16);
        DAT_8008d518[playerIndex + 1] = uVar3;
        uVar3 = local_20[playerIndex + 1] & uVar3;
        local_20[playerIndex + 1] = uVar3;
        g_PadNewlyPressed[playerIndex + 1] = DAT_8008d518[playerIndex + 1] ^ uVar3;

        uint uVar1 = DAT_8008d3bc;
        uVar3 = DAT_8008d3b8;

        // The original walks four parallel pointers and stops when the counter pointer reaches
        // 0x8008D2E0 (`while ((int)puVar7 < -0x7ff72d20)`), starting at DAT_8008d2d8 @ 0x8008D2D8:
        // two iterations, one per port.
        for (int iVar8 = 0; iVar8 < 2; iVar8++)
        {
            if ((DAT_8008d518[iVar8] & DAT_8008d2d0[iVar8]) == 0)
            {
                DAT_8008d2d0[iVar8] = DAT_8008d518[iVar8];
                g_PadHoldFrames[iVar8] = 0;
            }
            else
            {
                g_PadHoldFrames[iVar8] = g_PadHoldFrames[iVar8] + 1;
            }

            uint uVar4 = g_PadHoldFrames[iVar8] < 7 ? g_PadNewlyPressed[iVar8] : DAT_8008d518[iVar8];
            DAT_8008d52c[iVar8] = uVar4;

            // Reloaded every iteration by the original and used only after the loop. Kept.
            uVar1 = DAT_8008d3bc;
            uVar3 = DAT_8008d3b8;
        }

        DAT_8008d3bc = 0;
        DAT_8008d3b8 = 0;
        for (int iVar8 = 0; iVar8 < 0xe; iVar8++)
        {
            if ((g_PadButtonMaskTable[iVar8] & DAT_8008d518[0]) != 0)
            {
                DAT_8008d3b8 = (uint)(ushort)SharedHighRam.SHORT_ARRAY_801ff000[RemapPort1Index + iVar8]
                               | DAT_8008d3b8;
            }

            if ((g_PadButtonMaskTable[iVar8] & DAT_8008d518[1]) != 0)
            {
                DAT_8008d3bc = (uint)(ushort)SharedHighRam.SHORT_ARRAY_801ff000[RemapPort2Index + iVar8]
                               | DAT_8008d3bc;
            }
        }

        DAT_8008d3ac = DAT_8008d3b8 ^ (uVar3 & DAT_8008d3b8);
        DAT_8008d3b0 = DAT_8008d3bc ^ (uVar1 & DAT_8008d3bc);
    }
}
