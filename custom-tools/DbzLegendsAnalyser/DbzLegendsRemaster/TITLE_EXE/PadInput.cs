using static PsxSdkMonogame.LibEtc;

namespace DbzLegendsRemaster.TITLE_EXE;

// Pad handling for TITLE.EXE. ProcessPadInput @ 0x800578A8 samples the pad, derives rising edges,
// runs an auto-repeat, then remaps the raw hardware bits through the configurable tables the
// bootstrap left in high RAM.
//
// The globals below are addressed by the original as adjacent pairs — DAT_800835e0 is
// DAT_800835dc + 4, DAT_80083500 is g_PadNewlyPressed + 4, and so on — with the loop walking them by
// pointer. They are kept as two-entry arrays so that indexing stays the original's.
internal static class PadInput
{
    // GHIDRA: DAT_800835dc @ 0x800835DC
    // [0] is the whole word PadRead returns; [1] is its high halfword, i.e. port 2.
    internal static readonly uint[] DAT_800835dc = new uint[2];

    // GHIDRA: g_PadNewlyPressed @ 0x800834FC
    // Rising edges, one entry per port. RunFrameLoop tests g_PadNewlyPressed & 0x800 for Start.
    internal static readonly uint[] g_PadNewlyPressed = new uint[2];

    // GHIDRA: DAT_8008338c @ 0x8008338C
    // Held-button memory driving the auto-repeat.
    internal static readonly uint[] DAT_8008338c = new uint[2];

    // GHIDRA: g_PadHoldFrames @ 0x80083394
    // Auto-repeat counter, one per port.
    internal static readonly uint[] g_PadHoldFrames = new uint[2];

    // GHIDRA: DAT_800835f0 @ 0x800835F0
    // What the rest of the game reads: the rising edge for the first seven frames a button is held,
    // then the held state itself, which is what makes a held direction repeat.
    internal static readonly uint[] DAT_800835f0 = new uint[2];

    // GHIDRA: DAT_80083478 @ 0x80083478
    internal static uint DAT_80083478;

    // GHIDRA: DAT_8008347c @ 0x8008347C
    internal static uint DAT_8008347c;

    // GHIDRA: DAT_8008346c @ 0x8008346C
    internal static uint DAT_8008346c;

    // GHIDRA: DAT_80083470 @ 0x80083470
    internal static uint DAT_80083470;

    // GHIDRA: g_PadButtonMaskTable @ 0x8007AD0C
    // The fourteen hardware button masks, read out of .data. They are identical to what
    // FUN_8002165c writes into the remap tables, so the mapping starts as the identity and only
    // differs once the player reconfigures it.
    private static readonly ushort[] g_PadButtonMaskTable =
    {
        0x0020, 0x0080, 0x0010, 0x0040, 0x2000, 0x8000, 0x1000,
        0x4000, 0x0100, 0x0800, 0x0008, 0x0002, 0x0004, 0x0001,
    };

    // Index of the two remap tables inside SHORT_ARRAY_801ff000: 0x801FF020 and 0x801FF03C.
    private const int RemapPort1Index = 0x10;
    private const int RemapPort2Index = 0x1E;

    // GHIDRA: ProcessPadInput @ 0x800578A8
    internal static void ProcessPadInput(uint playerIndex)
    {
        uint[] local_20 = new uint[2];

        playerIndex = playerIndex & 0xffff;
        local_20[playerIndex] = DAT_800835dc[playerIndex];
        uint uVar2 = PadRead((int)playerIndex);
        DAT_800835dc[playerIndex] = uVar2;
        g_PadNewlyPressed[0] = DAT_800835dc[0] ^ (local_20[0] & DAT_800835dc[0]);

        local_20[playerIndex + 1] = DAT_800835dc[playerIndex + 1];
        uint uVar3 = (ushort)(DAT_800835dc[playerIndex] >> 16);
        DAT_800835dc[playerIndex + 1] = uVar3;
        uVar3 = local_20[playerIndex + 1] & uVar3;
        local_20[playerIndex + 1] = uVar3;
        g_PadNewlyPressed[playerIndex + 1] = DAT_800835dc[playerIndex + 1] ^ uVar3;

        uint uVar1 = DAT_8008347c;
        uVar3 = DAT_80083478;

        // The original walks four parallel pointers from g_PadHoldFrames up to 0x8008339C, which is
        // two iterations: one per port.
        for (int iVar8 = 0; iVar8 < 2; iVar8++)
        {
            if ((DAT_800835dc[iVar8] & DAT_8008338c[iVar8]) == 0)
            {
                DAT_8008338c[iVar8] = DAT_800835dc[iVar8];
                g_PadHoldFrames[iVar8] = 0;
            }
            else
            {
                g_PadHoldFrames[iVar8] = g_PadHoldFrames[iVar8] + 1;
            }

            uint uVar4 = g_PadHoldFrames[iVar8] < 7 ? g_PadNewlyPressed[iVar8] : DAT_800835dc[iVar8];
            DAT_800835f0[iVar8] = uVar4;

            uVar1 = DAT_8008347c;
            uVar3 = DAT_80083478;
        }

        DAT_8008347c = 0;
        DAT_80083478 = 0;
        for (int iVar8 = 0; iVar8 < 0xe; iVar8++)
        {
            if ((g_PadButtonMaskTable[iVar8] & DAT_800835dc[0]) != 0)
            {
                DAT_80083478 = (uint)(ushort)SharedHighRam.SHORT_ARRAY_801ff000[RemapPort1Index + iVar8]
                               | DAT_80083478;
            }

            if ((g_PadButtonMaskTable[iVar8] & DAT_800835dc[1]) != 0)
            {
                DAT_8008347c = (uint)(ushort)SharedHighRam.SHORT_ARRAY_801ff000[RemapPort2Index + iVar8]
                               | DAT_8008347c;
            }
        }

        DAT_8008346c = DAT_80083478 ^ (uVar3 & DAT_80083478);
        DAT_80083470 = DAT_8008347c ^ (uVar1 & DAT_8008347c);
    }
}
