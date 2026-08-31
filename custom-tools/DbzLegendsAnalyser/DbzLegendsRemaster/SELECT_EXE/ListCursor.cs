namespace DbzLegendsRemaster.SELECT_EXE;

// THE SHARED LIST CURSOR — RunListSelect @ 0x80033D34, 1612 bytes, three call sites:
//     RunDemoModeScreen @ 0x80030AF8 (DEMO)  RunListSelect(&g_DemoListCursor, 0x10, 4, 2)
//     RunVsModeScreen @ 0x80030EF8 (VS)    RunListSelect(&g_VsSubMenuCursor, 2,    3, 1)
//     RunSpModeScreen @ 0x800310A8 (SP)    RunListSelect(&g_SpListCursor, 0x10, 4, 3)
// It owns a blocking frame loop of its own and returns only three kinds of answer:
//     the cursor value   Circle was pressed
//     -1                 Cross was pressed
//     -2                 the memory card changed state and the caller must reload its list
// It is the only thing between each branch's list and that branch's LoadExec.
//
// param_4 is the SCREEN ID, and it selects behaviour twice over. `bVar2 = 1 < param_4 - 2U` is
// false for 2 and 3 and true for everything else, and where it is false the cursor SKIPS rows whose
// availability byte is zero and drags two companion sprites' colour with it. DAT_80055B50 — main's
// own screen-id word — separately gates the per-frame memory-card re-poll to screens 0 and 2 and
// picks the auto-repeat cadence (four frames on 0/2, eight on 1).
internal static class ListCursor
{
    // GHIDRA: g_PrevCardProbeResult @ 0x80055B4C
    // .sbss, undefined4. The PREVIOUS memory-card status, latched each pass before the re-poll so
    // the two "-2" tests can spot a transition rather than a level.
    private static int g_PrevCardProbeResult;

    // GHIDRA: g_ListRowAvailable4 @ 0x80055B84
    // .sbss, undefined1. Row 0's availability — both card pickers set it to 1 unconditionally, so
    // the "no card" row is always selectable.
    internal static byte g_ListRowAvailable4;

    // GHIDRA: DAT_80055b85 @ 0x80055B85
    // .sbss, undefined1. Row 1's availability: bit 0 of the first save record.
    internal static byte DAT_80055b85;

    // GHIDRA: DAT_80055b86 @ 0x80055B86
    // .sbss, undefined1. Row 2's availability.
    internal static byte DAT_80055b86;

    // GHIDRA: DAT_80055b87 @ 0x80055B87
    // .sbss, undefined1. Row 3's availability.
    internal static byte DAT_80055b87;

    // JUSTIFICATION: C# language bridge only
    // RELATION: RunListSelect addresses the four bytes above as an array from two different bases —
    // `(&g_ListRowAvailable4)[*param_1]` when the cursor moves UP, and `(&DAT_80055b85)[iVar6]` when it
    // moves DOWN (moving down from row c lands on row c + 1, whose byte is 0x80055B84 + c + 1).
    // A third spelling, `*(char *)((int)&DAT_80055b80 + iVar6 + 3)`, is the same 0x80055B84 + iVar6.
    // This helper is that indexing and nothing else; the four globals keep their own names at every
    // write site.
    //
    // INDEX 4 IS A ONE-BYTE OVERREAD IN THE ORIGINAL, and it is reproduced, not corrected — rule 12.
    // The DOWN path reads 0x80055B85 + cursor with no upper guard before the first read, so a cursor
    // of 3 on a four-row list reads 0x80055B88 — one past the four, and the first byte of .bss
    // (the sound module's state block, which nothing on any ported path has written).
    // IT CANNOT CHANGE THE OUTCOME, and that is provable rather than assumed: with cursor 3 and
    // param_3 = 4 the code has already stored 4 into the cursor before the read. If the byte is
    // non-zero the loop is skipped and `param_3 <= iVar6` (4 <= 4) fires; if it is zero the loop
    // runs one pass and takes the same `param_3 <= iVar6` exit. Both arms store 0. So 0 is returned
    // for that index here, and the two paths still agree.
    internal static byte AvailabilityByte(int indexFromDAT_80055b84)
    {
        switch (indexFromDAT_80055b84)
        {
            case 0: return g_ListRowAvailable4;
            case 1: return DAT_80055b85;
            case 2: return DAT_80055b86;
            case 3: return DAT_80055b87;
            default: return 0;
        }
    }

    // GHIDRA: RunListSelect @ 0x80033D34
    //
    // BLOCKED, AND THE ONE THING IN THIS FUNCTION THAT IS NOT CLOSED: `unaff_s4` and `unaff_s5` are
    // read before they are written. The prologue at 0x80033D34 saves s4 and s5 to the stack
    // (`sw s4,0x48(sp)` @ 0x80033D88, `sw s5,0x4C(sp)` @ 0x80033D84) and NEVER initialises them —
    // Ghidra's `unaff_` prefix marks exactly that. The original therefore starts its auto-repeat
    // frame counter and phase from whatever the caller happened to leave in those two registers.
    // A C# local cannot carry a leftover machine register, so both start at 0 here.
    // THE DIVERGENCE IS BOUNDED: the very first pass through the loop resets both to 0 as soon as
    // FUN_80026208 reports an empty pad (`if (g_PadButtonWord == 0) { unaff_s5 = 0; unaff_s4 = 0; }`),
    // so the only frames that can differ are those between entry and the first release of every
    // button. All three callers reach this after a Circle press that has already been consumed
    // (RunDemoModeScreen runs eight frame steps first; RunModeMenu's outro runs a further 0x207).
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: three shapes could not be spelled literally. (1) `*param_1` is a pointer to one of
    // three .sdata/.sbss cursors, so it becomes `ref int`. (2) The label LAB_80033ebc is a bare
    // `bVar3 = true;` reached by a `goto` from the other arm; it is written out in both arms instead
    // of naming a join C# cannot address. (3) The `goto LAB_80034084` inside the DOWN loop is
    // written as `break`, which is exactly equivalent: the loop's own guard has just assigned
    // iVar6 = *param_1, the statement after the loop assigns the same value again, and the shared
    // `if (param_3 <= iVar6)` immediately below then fires on the same test that produced the jump.
    internal static int RunListSelect(ref int param_1, int param_2, int param_3, int param_4)
    {
        byte cVar1;
        bool bVar2;
        bool bVar3;
        bool bVar4;
        uint uVar5;
        int iVar6;
        int iVar7;
        uint unaff_s4;
        ushort unaff_s5;

        bVar3 = false;
        bVar2 = 1 < (uint)(param_4 - 2);
        bVar4 = true;

        // BLOCKED: see the note above. The original inherits these two.
        unaff_s4 = 0;
        unaff_s5 = 0;

        do
        {
            if ((SELECT_EXE_exe.g_CurrentMenuState == 0) || (SELECT_EXE_exe.g_CurrentMenuState == 2))
            {
                // Ghidra prints the call as RepollMemoryCard() with no argument, but the register is set:
                // `lui a0,0x8020 / lw a0,-0x0f98(a0)` at 0x80033DAC-0x80033DB0 loads g_CardProbeResult
                // into a0, `sw a0,0x164(gp)` at 0x80033DB8 is this very store into g_PrevCardProbeResult, and
                // the jal at 0x80033DBC has a nop delay slot. So the argument is the OLD status —
                // which is what RepollMemoryCard's own `param_1 != 2` / `param_1 == 4` tests need.
                g_PrevCardProbeResult = SharedHighRam.g_CardProbeResult;
                SharedHighRam.g_CardProbeResult = MemoryCard.RepollMemoryCard(g_PrevCardProbeResult);
                if ((SharedHighRam.g_CardProbeResult == 4) && (g_PrevCardProbeResult != 4))
                {
                    return -2;
                }

                if ((SharedHighRam.g_CardProbeResult != 0) && (g_PrevCardProbeResult == 0))
                {
                    return -2;
                }
            }

            uVar5 = PadInput.FUN_80026208(3);
            SELECT_EXE_exe.g_PadButtonWord = (int)(uVar5 & 0xffff);
            unaff_s4 = unaff_s4 + 1;
            if (SELECT_EXE_exe.g_PadButtonWord == 0)
            {
                bVar3 = true;
                unaff_s5 = 0;
                unaff_s4 = 0;
            }

            if ((SELECT_EXE_exe.g_CurrentMenuState == 0) || (SELECT_EXE_exe.g_CurrentMenuState == 2))
            {
                if ((uVar5 & 0x60) == 0)
                {
                    // 0x5000 is Up|Down; the cadence is "after seven frames held, once every four".
                    if (((uVar5 & 0x5000) != 0) && (7 < (short)unaff_s4) &&
                        ((int)(unaff_s4 & 3) == (int)(short)unaff_s5))
                    {
                        bVar3 = true;
                        unaff_s5 = (ushort)((unaff_s5 - 1) & 3);
                    }
                }
                else
                {
                    // LAB_80033ebc
                    bVar3 = true;
                }
            }
            else if (SELECT_EXE_exe.g_CurrentMenuState == 1)
            {
                if ((uVar5 & 0x60) != 0)
                {
                    // LAB_80033ebc
                    bVar3 = true;
                }
                else if (((uVar5 & 0x5000) != 0) && (0xf < (short)unaff_s4) &&
                         ((int)(unaff_s4 & 7) == (int)(short)unaff_s5))
                {
                    bVar3 = true;
                    unaff_s5 = (ushort)((unaff_s5 - 1) & 7);
                }
            }

            if (bVar3 && (SELECT_EXE_exe.g_PadButtonWord != 0))
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_2 + param_1].r = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_2 + param_1].g = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_2 + param_1].b = 0x40;
                bVar3 = false;
                if (!bVar2)
                {
                    iVar7 = param_1;
                    iVar6 = iVar7 + 9;
                    if (iVar7 != 0)
                    {
                        iVar7 = iVar7 + 6;
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar6].b = 0x40;
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar6].g = 0x40;
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar6].r = 0x40;
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7].b = 0x40;
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7].g = 0x40;
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7].r = 0x40;
                    }
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x4000) != 0)
                {
                    if (bVar2)
                    {
                        iVar6 = param_1 + 1;
                        param_1 = iVar6;
                    }
                    else
                    {
                        iVar6 = param_1;
                        param_1 = iVar6 + 1;
                        cVar1 = AvailabilityByte(1 + iVar6);
                        while (cVar1 == 0)
                        {
                            iVar6 = param_1;
                            if (param_3 <= iVar6)
                            {
                                break;   // LAB_80034084, reached through the shared test below
                            }

                            param_1 = iVar6 + 1;
                            cVar1 = AvailabilityByte(1 + iVar6);
                        }

                        iVar6 = param_1;
                    }

                    if (param_3 <= iVar6)
                    {
                        // LAB_80034084
                        param_1 = 0;
                    }
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x1000) != 0)
                {
                    if (bVar2)
                    {
                        iVar6 = param_1;
                        param_1 = iVar6 + -1;
                        if (iVar6 + -1 < 0)
                        {
                            param_1 = param_3 + -1;
                        }
                    }
                    else
                    {
                        iVar6 = param_1;
                        param_1 = iVar6 + -1;
                        if (iVar6 + -1 < 0)
                        {
                            param_1 = param_3 + -1;
                        }

                        // This walk has NO lower guard in the original. It terminates only because
                        // g_ListRowAvailable4 — index 0 — is set to 1 unconditionally by both card pickers
                        // before they call in. Reproduced as written.
                        cVar1 = AvailabilityByte(param_1);
                        iVar6 = param_1;
                        while (cVar1 == 0)
                        {
                            param_1 = iVar6 + -1;
                            cVar1 = AvailabilityByte(iVar6 + -1);
                            iVar6 = iVar6 + -1;
                        }
                    }
                }

                if (((SELECT_EXE_exe.g_CurrentMenuState == 0) || (SELECT_EXE_exe.g_CurrentMenuState == 2)) &&
                    (SharedHighRam.g_CardProbeResult != 0))
                {
                    param_1 = 0;
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x20) != 0)
                {
                    bVar4 = false;
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x40) != 0)
                {
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_2 + param_1].r = 0x80;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_2 + param_1].g = 0x80;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_2 + param_1].b = 0x80;
                    if (!bVar2)
                    {
                        iVar7 = param_1;
                        iVar6 = iVar7 + 9;
                        if (iVar7 != 0)
                        {
                            iVar7 = iVar7 + 6;
                            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar6].b = 0x80;
                            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar6].g = 0x80;
                            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar6].r = 0x80;
                            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7].b = 0x80;
                            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7].g = 0x80;
                            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7].r = 0x80;
                        }
                    }

                    return -1;
                }
            }

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_2 + param_1].r = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_2 + param_1].g = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_2 + param_1].b = 0x80;
            if (!bVar2)
            {
                iVar7 = param_1;
                iVar6 = iVar7 + 9;
                if (iVar7 != 0)
                {
                    iVar7 = iVar7 + 6;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar6].b = 0x80;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar6].g = 0x80;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar6].r = 0x80;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7].b = 0x80;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7].g = 0x80;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7].r = 0x80;
                }
            }

            FrameStep.DrawFrame();
            if (!bVar4)
            {
                return param_1;
            }
        }
        while (true);
    }
}
