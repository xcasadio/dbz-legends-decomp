using static PsxSdkMonogame.LibApi;

namespace DbzLegendsRemaster.SELECT_EXE;

// SELECT.EXE'S INPUT MODULE — the FOUR functions emitted at 0x800261A4, 0x800261E4, 0x80026208 and
// 0x800263E4, plus the two BIOS status buffers they share and the four auto-repeat counters.
// (The fourth was added when the 3-on-3 character select turned out to be its only caller; the
// module's extent is contiguous — 0x80026208 + 476 = 0x800263E4.)
//
// THIS OVERLAY DOES NOT USE libetc PadRead. PadRead has zero callers in the whole image. main
// @ 0x8003045C calls libetc's PadInit(0), but the word every screen actually reads comes from the
// BIOS pad driver, installed here over two 34-byte status buffers and read out of them BY HAND.
//
// THE BUFFER LAYOUT IS THE CONTRACT, and it is closed three ways:
//   * InitializeBiosPad installs them: InitPAD(&g_PadStatusBuffers, 0x22, &DAT_80055d8e, 0x22). The two
//     addresses are 0x22 apart, so the pair is one contiguous 68-byte region at 0x80055D6C.
//   * GetPadStatus indexes STRAIGHT ACROSS BOTH as one region: `(&g_PadStatusBuffers)[param_1 * 0x22]`,
//     which only type-checks if they are adjacent. It returns byte +0 of the selected buffer, and
//     both consumers read 0 as "pad present" (RunVsModeScreen line 10; FUN_800315c0 lines 96/121).
//   * FUN_80026208 reads bytes +2 and +3 of each buffer as ~CONCAT11(hi, lo) — that is,
//     (buf[+2] << 8) | buf[+3], inverted, ACTIVE LOW.
// Byte +1 is never read by SELECT.EXE and is left alone; LibApi.cs records the same.
//
// The resulting 16-bit word is the mask the rest of the overlay switches on:
//   0x1000 Up   0x4000 Down   0x8000 Left   0x2000 Right
//   0x0010 Triangle   0x0020 Circle   0x0040 Cross   0x0080 Square
//   0x0800 Start      0x0100 Select
internal static class PadInput
{
    // GHIDRA: g_PadStatusBuffers @ 0x80055D6C
    // The FIRST status buffer, 0x22 bytes, and — because GetPadStatus walks across the boundary —
    // also the base of the second at +0x22 (Ghidra's DAT_80055d8e). Held as ONE 0x44-byte array
    // because that is what the reader assumes; LibApi.InitPAD carries an (array, offsetA, offsetB)
    // overload for exactly this shape.
    // The named bytes inside it: +0x02 DAT_80055d6e, +0x03 DAT_80055d6f (pad 1 status),
    // +0x22 DAT_80055d8e (pad 2 presence), +0x24 DAT_80055d90, +0x25 DAT_80055d91 (pad 2 status).
    internal static readonly byte[] g_PadStatusBuffers = new byte[0x44];

    // GHIDRA: g_Pad1CircleHold @ 0x80055AF0
    // .sbss, undefined1 (Ghidra types all four of these as single bytes and they sit one byte
    // apart). PAD 1's CIRCLE hold counter.
    private static byte g_Pad1CircleHold;

    // GHIDRA: g_Pad2CircleHold @ 0x80055AF1
    // .sbss, undefined1. PAD 2's CIRCLE hold counter.
    private static byte g_Pad2CircleHold;

    // GHIDRA: g_Pad1CrossHold @ 0x80055AF4
    // .sbss, undefined1. PAD 1's CROSS hold counter.
    private static byte g_Pad1CrossHold;

    // GHIDRA: g_Pad2CrossHold @ 0x80055AF5
    // .sbss, undefined1. PAD 2's CROSS hold counter.
    private static byte g_Pad2CrossHold;

    // GHIDRA: InitializeBiosPad @ 0x800261A4
    // Sixty-four bytes, three calls, the whole of SELECT.EXE's input bring-up. FUN_80030698
    // @ 0x80030698 calls it last, so it runs on main's pre-loop path.
    internal static void InitializeBiosPad()
    {
        InitPAD(g_PadStatusBuffers, 0, 0x22, 0x22, 0x22);
        StartPAD();
        ChangeClearPAD(0);
    }

    // GHIDRA: GetPadStatus @ 0x800261E4
    // Thirty-six bytes, no callees. `return (&g_PadStatusBuffers)[param_1 * 0x22];` — byte +0 of buffer
    // param_1. Ghidra types the return undefined1.
    internal static byte GetPadStatus(int param_1)
    {
        return g_PadStatusBuffers[param_1 * 0x22];
    }

    // GHIDRA: ReadPadButtons @ 0x800263E4
    // Sixty bytes, no callees, ONE call site: RunVsTeamSelect @ 0x80031E98, the 3-on-3 character
    // select, at 0x8003260C.
    //
    // `return ~CONCAT11((&DAT_80055d6e)[param_1 * 0x22], (&DAT_80055d6f)[param_1 * 0x22]);` — the
    // SAME two bytes and the SAME inversion FUN_80026208 opens with, on the SAME contiguous 68-byte
    // region, but with none of the auto-repeat masking. It is the RAW held state of pad param_1.
    // The character select uses it for pad 2 while running its own repeat cadence out of three
    // counters of its own; every other screen in the overlay goes through FUN_80026208 instead.
    internal static ushort ReadPadButtons(int param_1)
    {
        return (ushort)~(ushort)((g_PadStatusBuffers[(param_1 * 0x22) + 0x02] << 8) |
                                  g_PadStatusBuffers[(param_1 * 0x22) + 0x03]);
    }

    // GHIDRA: FUN_80026208 @ 0x80026208
    // 476 bytes, no callees, ELEVEN call sites — the edge/repeat reader every screen in the overlay
    // goes through.
    //
    // WHAT param_1 SELECTS, read off the tail:
    //     0        pad 1's word
    //     1, 2     pad 2's word, or 0 when pad 2 reports absent (DAT_80055d8e != 0)
    //     3, 4     that same pad-2 answer OR'd with pad 1's word (`param_1 - 3U < 2`)
    // and, independently, whether CIRCLE and CROSS auto-repeat:
    //     4        the button survives two held frames before being masked out
    //     anything else   the button survives one held frame
    // Only 0x20 (Circle) and 0x40 (Cross) are debounced here. The direction bits are passed through
    // raw; RunListSelect @ 0x80033D34 and RunModeMenu @ 0x800283A0 run their own cadence on them.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: the first three of the four debounce blocks are emitted with the masking arm as a
    // SHARED branch target (Ghidra prints it as LAB_8002625c / LAB_800262b4 / LAB_80026328 sitting
    // inside the `if` and reached by a `goto` from the `else`). C# cannot name that join without
    // inventing an address for it, so the arm's condition is carried out of the if/else in a local
    // and applied immediately after. Neither the order of the tests nor their results change; the
    // fourth block, which Ghidra already prints flattened, is written the way it prints.
    internal static ushort FUN_80026208(int param_1)
    {
        ushort uVar1;
        ushort uVar2;
        ushort uVar3;
        bool bMask;

        // ~CONCAT11(DAT_80055d6e, DAT_80055d6f) — buffer 1's bytes +2 and +3, high byte first,
        // then inverted because the BIOS reports the pad ACTIVE LOW.
        uVar1 = (ushort)~(ushort)((g_PadStatusBuffers[0x02] << 8) | g_PadStatusBuffers[0x03]);
        if ((uVar1 & 0x20) == 0)
        {
            g_Pad1CircleHold = 0;
        }

        if (param_1 == 4)
        {
            uVar2 = (ushort)(uVar1 & 0x20);
            bMask = 1 < g_Pad1CircleHold;
        }
        else
        {
            uVar2 = (ushort)(uVar1 & 0x20);
            bMask = g_Pad1CircleHold != 0;
        }

        if (bMask)
        {
            // LAB_8002625c
            uVar1 = (ushort)(uVar1 & 0xffdf);
            uVar2 = 0;
        }

        if (uVar2 != 0)
        {
            g_Pad1CircleHold = (byte)(g_Pad1CircleHold + 1);
        }

        if ((uVar1 & 0x40) == 0)
        {
            g_Pad1CrossHold = 0;
        }

        if (param_1 == 4)
        {
            uVar2 = (ushort)(uVar1 & 0x40);
            bMask = 1 < g_Pad1CrossHold;
        }
        else
        {
            uVar2 = (ushort)(uVar1 & 0x40);
            bMask = g_Pad1CrossHold != 0;
        }

        if (bMask)
        {
            // LAB_800262b4
            uVar1 = (ushort)(uVar1 & 0xffbf);
            uVar2 = 0;
        }

        if (uVar2 != 0)
        {
            g_Pad1CrossHold = (byte)(g_Pad1CrossHold + 1);
        }

        // ~CONCAT11(DAT_80055d90, DAT_80055d91) — buffer 2's bytes +2 and +3, i.e. +0x24 and +0x25
        // of the contiguous region.
        uVar2 = (ushort)~(ushort)((g_PadStatusBuffers[0x24] << 8) | g_PadStatusBuffers[0x25]);
        if ((uVar2 & 0x20) == 0)
        {
            g_Pad2CircleHold = 0;
        }

        if (param_1 == 4)
        {
            uVar3 = (ushort)(uVar2 & 0x20);
            bMask = 1 < g_Pad2CircleHold;
        }
        else
        {
            uVar3 = (ushort)(uVar2 & 0x20);
            bMask = g_Pad2CircleHold != 0;
        }

        if (bMask)
        {
            // LAB_80026328
            uVar2 = (ushort)(uVar2 & 0xffdf);
            uVar3 = 0;
        }

        if (uVar3 != 0)
        {
            g_Pad2CircleHold = (byte)(g_Pad2CircleHold + 1);
        }

        if ((uVar2 & 0x40) == 0)
        {
            g_Pad2CrossHold = 0;
        }

        if (param_1 == 4)
        {
            uVar3 = (ushort)(uVar2 & 0x40);
            if (g_Pad2CrossHold < 2)
            {
                goto LAB_80026388;
            }
        }
        else
        {
            uVar3 = (ushort)(uVar2 & 0x40);
            if (g_Pad2CrossHold == 0)
            {
                goto LAB_80026388;
            }
        }

        uVar2 = (ushort)(uVar2 & 0xffbf);
        uVar3 = 0;

    LAB_80026388:
        if (uVar3 != 0)
        {
            g_Pad2CrossHold = (byte)(g_Pad2CrossHold + 1);
        }

        // Ghidra folds this into `if ((param_1 != 0) && (uVar3 = uVar2, DAT_80055d8e != '\0'))`.
        // C# has no comma operator, so the assignment stands on its own line; the order of the
        // store and the test is unchanged.
        uVar3 = uVar1;
        if (param_1 != 0)
        {
            uVar3 = uVar2;
            if (g_PadStatusBuffers[0x22] != 0)
            {
                uVar3 = 0;
            }
        }

        if ((uint)(param_1 - 3) < 2)
        {
            uVar3 = (ushort)(uVar3 | uVar1);
        }

        return uVar3;
    }
}
