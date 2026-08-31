using PsxSdkMonogame;
using static PsxSdkMonogame.LibGcc;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE MENU WRAPPER AND THE THREE BRANCHES main DISPATCHES INTO — the four functions emitted at
// 0x80030A6C, 0x80030AF8, 0x80030EF8 and 0x800310A8, in that order, immediately after the USAGI.B
// loader and immediately before the options screen RunOptionsScreen @ 0x800315C0.
//
// Each of the three branches is a BLOCKING SCREEN in this overlay's style: it owns a do/while (or a
// while(true)) that calls the frame step FrameStep.DrawFrame to present, and it ends by handing
// the machine to the next overlay through OverlayExit.ShutdownAndLoadExecutable — the only LoadExec call site in
// the program. Those three calls are the only ways out of SELECT.EXE.
//     RunDemoModeScreen  "cdrom:\\DEMO.EXE;1"  @ 0x80020674
//     RunVsModeScreen  "cdrom:\\VS.EXE;1"    @ 0x80020688
//     RunSpModeScreen  "cdrom:\\SP.EXE;1"    @ 0x80020698
//
// THE SPRITE INDICES BELOW ARE ARITHMETIC, NOT GUESSES. Ghidra spells the redraw loops as byte
// offsets from whichever .bss symbol happens to precede the field, so the same cursor names two
// different elements depending on the base:
//     &GsSPRITE_ARRAY_800654ec = 0x800654EC, stride 36
//     &DAT_80065480 + n  ->  element (n - 0x6C) / 36        (0x800654EC - 0x80065480 = 0x6C)
//     &g_GsLineArray4 + n  ->  element (n - 0x68) / 36, field +4  (x)
//     &DAT_80065488 + n  ->  ... field +8   (w)      &DAT_8006548a + n  ->  +0x0A (h)
//     &DAT_8006548c + n  ->  ... field +0x0C (tpage) &DAT_8006548e + n  ->  +0x0E (u), +1 -> v
//     &DAT_80065490 + n  ->  ... field +0x10 (cx)    &DAT_80065492 + n  ->  +0x12 (cy)
//     &DAT_80065494 + n  ->  ... field +0x14 (r)     &DAT_80065498 + n  ->  +0x18 (mx)
//     &DAT_8006549a + n  ->  ... field +0x1A (my)
//     (&GsSPRITE_ARRAY_800654ec[0].b)[n] -> element n / 36, field +0x16
// With the loop's own iVar5 = 0x168 that is element 7 through the shifted bases and element 10
// through the array base — the two rows the card pickers light up together.
internal static class ModeBranches
{
    // GHIDRA: g_DemoListCursor @ 0x80055B08
    // .sbss, undefined4. THE DEMO PICKER'S CURSOR, 0..3 over "no card" plus three save slots.
    private static int g_DemoListCursor;

    // GHIDRA: g_VsSubMenuCursor @ 0x80055A40
    // .sdata, undefined4, image value 0 (read with get-data). THE VS SUB-MENU'S CURSOR, 0..2, and
    // also the value RunVsTeamSelect exports as the VS sub-mode at 0x801FF100.
    private static int g_VsSubMenuCursor;

    // GHIDRA: g_SpListCursor @ 0x80055A44
    // .sdata, undefined4, image value 0. THE SP PICKER'S CURSOR, 0..3.
    private static int g_SpListCursor;

    // GHIDRA: g_SpBranchDigitCellRect @ 0x80055A48
    // .sdata, undefined4, image value 0x00DD0000 (read with get-data; the bytes at 0x80055A48 are
    // 00 00 DD 00). It is the FIRST HALF of a RECT constant: x = 0x0000, y = 0x00DD.
    private static readonly uint g_SpBranchDigitCellRect = 0x00DD0000;

    // GHIDRA: DAT_80055a4c @ 0x80055A4C
    // .sdata, undefined4, image value 0x00100004 (bytes 04 00 10 00). The SECOND half of that RECT:
    // w = 0x0004, h = 0x0010 — a four-by-sixteen digit cell.
    private static readonly uint DAT_80055a4c = 0x00100004;

    // GHIDRA: DAT_801ff000 @ 0x801FF000
    // The 24-byte LAUNCH PARAMETER BLOCK the next overlay reads. The two card pickers write it and
    // nothing in SELECT.EXE reads it back, which is why every access below goes through the raw PSX
    // address: SELECT_EXE_exe.ResolveAddress chains SharedHighRam, whose region covers
    // 0x801FF000..0x801FF247, so these land in the same bytes LoadExec leaves behind.
    //     +0x00 .. +0x06   four halfwords, written by RunDemoModeScreen for DEMO.EXE
    //     +0x08 .. +0x16   u16, u16, u32, u32, u16, u16, written by RunSpModeScreen for SP.EXE
    private const int DAT_801ff000_Address = unchecked((int)0x801FF000);

    // GHIDRA: g_DemoSaveRecords3 @ 0x801FF200
    // THREE EIGHT-BYTE SAVE RECORDS, 0x801FF200 / 0x801FF208 / 0x801FF210 — the DEMO list.
    // Bit 0 of each record's first byte is "this slot exists" and bit 1 is "select this slot by
    // default"; the four halfwords at +0, +2, +4 and +6 are what gets copied to 0x801FF000.
    // PARTIAL: what those four halfwords MEAN is not closed here. They are copied verbatim, and the
    // decoding belongs to DEMO.EXE.
    private const int g_DemoSaveRecords3_Address = unchecked((int)0x801FF200);

    // GHIDRA: g_SpSaveRecords3 @ 0x801FF218
    // THREE SIXTEEN-BYTE SAVE RECORDS, 0x801FF218 / 0x801FF228 / 0x801FF238 — the SP list. Same two
    // bits at +0; the halfword at +2 is the only field SELECT.EXE interprets, as the two decimal
    // digits RunSpModeScreen blits per row.
    private const int g_SpSaveRecords3_Address = unchecked((int)0x801FF218);

    // GHIDRA: FUN_80030a6c @ 0x80030A6C
    // One hundred and forty bytes. It swaps the menu artwork between the four-item and the
    // three-item layout, then tail-calls the driver and returns its value.
    // The gate is bit 1 of the options word at 0x801FF018 — the SAME bit main tests one line later
    // to redirect item 2 to state 3, and the same bit ModeMenu.RunModeMenu tests to choose between
    // three and four items. Sprite 0x18's attribute is what hides the fourth label: bit 31 set means
    // LibGs.GsSortSprite drops it.
    internal static int FUN_80030a6c()
    {
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].v = (byte)'X';
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].u = 0xa0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].attribute = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].cy = 0x1ff;
        if ((PsxRam.ReadI32(unchecked((int)0x801FF018)) & 2) == 0)
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x17].v = (byte)'p';
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x18].attribute = 0x80000000;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].u = (byte)'P';
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x1b].cy = 0x1fe;
        }

        return ModeMenu.RunModeMenu();
    }

    // GHIDRA: RunDemoModeScreen @ 0x80030AF8
    // 1024 bytes. main's state 0 — the DEMO save-slot picker.
    //
    // Shape: load the three eight-byte records, publish their "exists" bits into
    // ListCursor.g_ListRowAvailable4..87, preselect the last record whose bit 1 is set, draw the list, let
    // eight frames settle, then loop on ListCursor.RunListSelect(cursor, spriteBase 0x10, 4 rows,
    // screen 2). -1 unwinds back to the mode menu; -2 means the card changed and the list is rebuilt
    // in place; anything else is the chosen row. The chosen row's eight bytes are copied to
    // 0x801FF000, or {3, 0, 0, 0} when the "no card" row was taken, and then the overlay hands over.
    internal static void RunDemoModeScreen()
    {
        ushort uVar1;
        ushort uVar2;
        int iVar3;
        int puVar4;
        int iVar5;
        short sVar6;
        short sVar7;
        int iVar8;
        int iVar9;
        int iVar10;
        int iVar11;

        CardRecords.FUN_800276d8(0, g_DemoSaveRecords3_Address);
        iVar10 = 0;
        ListCursor.g_ListRowAvailable4 = 1;
        puVar4 = g_DemoSaveRecords3_Address;
        g_DemoListCursor = 0;
        ListCursor.DAT_80055b85 = (byte)(PsxRam.ReadU8(g_DemoSaveRecords3_Address) & 1);
        ListCursor.DAT_80055b86 = (byte)(PsxRam.ReadU8(g_DemoSaveRecords3_Address + 8) & 1);
        ListCursor.DAT_80055b87 = (byte)(PsxRam.ReadU8(g_DemoSaveRecords3_Address + 0x10) & 1);
        do
        {
            uVar1 = PsxRam.ReadU16(puVar4);

            // `puVar4 = puVar4 + 4` on a ushort * — eight bytes, the record stride.
            puVar4 = puVar4 + 8;
            if ((uVar1 & 2) != 0)
            {
                g_DemoListCursor = iVar10 + 1;
            }

            iVar10 = iVar10 + 1;
        }
        while (iVar10 < 3);

        iVar10 = 0;
        ScreenDecoration.BuildDemoSaveSlotScreen(g_DemoListCursor, g_DemoSaveRecords3_Address);
        do
        {
            iVar10 = iVar10 + 1;
            FrameStep.DrawFrame();
        }
        while (iVar10 < 8);

        while (true)
        {
            iVar10 = ListCursor.RunListSelect(ref g_DemoListCursor, 0x10, 4, 2);
            if (iVar10 == -1)
            {
                ScreenDecoration.UnwindDemoSaveSlotScreen();
                return;
            }

            if (iVar10 != -2)
            {
                break;
            }

            CardRecords.FUN_800276d8(0, g_DemoSaveRecords3_Address);
            iVar10 = 0;
            ListCursor.g_ListRowAvailable4 = 1;
            puVar4 = g_DemoSaveRecords3_Address;
            g_DemoListCursor = 0;
            ListCursor.DAT_80055b85 = (byte)(PsxRam.ReadU8(g_DemoSaveRecords3_Address) & 1);
            ListCursor.DAT_80055b87 = (byte)(PsxRam.ReadU8(g_DemoSaveRecords3_Address + 0x10) & 1);
            ListCursor.DAT_80055b86 = (byte)(PsxRam.ReadU8(g_DemoSaveRecords3_Address + 8) & 1);
            do
            {
                uVar1 = PsxRam.ReadU16(puVar4);
                puVar4 = puVar4 + 8;
                if ((uVar1 & 2) != 0)
                {
                    g_DemoListCursor = iVar10 + 1;
                }

                iVar10 = iVar10 + 1;
            }
            while (iVar10 < 3);

            iVar11 = 0;
            iVar5 = 0x168;
            iVar9 = 0x50;
            iVar8 = 10;
            sVar7 = 6;
            sVar6 = 0x38;
            iVar10 = 0xfc;
            do
            {
                // iVar5 / 0x24 is element 10, 11, 12 (the row label); iVar10 / 0x24 is element
                // 7, 8, 9 (the row's icon). See the table at the top of this file.
                int e7 = iVar10 / 0x24;
                int e10 = iVar5 / 0x24;

                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].x = sVar6;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].y = sVar7;

                // -0x7fe00e4e is 0x801FF1B2, so with iVar9 = 0x50, 0x58, 0x60 this reads the
                // halfword at +2 of records 0, 1 and 2 (0x801FF202 / 20A / 212).
                uVar2 = PsxRam.ReadU16(iVar9 + unchecked((int)0x801FF1B2));
                iVar9 = iVar9 + 8;
                sVar7 = (short)(sVar7 + 0x18);
                sVar6 = (short)(sVar6 + 8);
                iVar11 = iVar11 + 1;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].h = 0xd;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].mx = unchecked((short)0xffde);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].v = 0xdd;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].w = 0x10;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].my = 5;

                // `(char)uVar2 << 4` stored back into a char keeps the low eight bits.
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].u = (byte)(uVar2 << 4);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].b = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].g = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].r = 0x40;
                iVar3 = iVar8 + 3;
                iVar8 = iVar8 + 1;

                // `&DAT_80055b78 + iVar3` with iVar3 = 13, 14, 15 is 0x80055B85/86/87, i.e. the
                // availability bytes at index iVar3 - 0xC from 0x80055B84. An absent row gets
                // attribute bit 31, which is how GsSortSprite drops it.
                iVar3 = (int)((uint)(ListCursor.AvailabilityByte(iVar3 - 0xc) == 0 ? 1 : 0) << 0x1f);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].attribute = (uint)iVar3;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].attribute = (uint)iVar3;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].b = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].g = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].r = 0x40;
                iVar5 = iVar5 + 0x24;
                iVar10 = iVar10 + 0x24;
            }
            while (iVar11 < 3);

            iVar5 = 0;
            iVar10 = 0x240;
            do
            {
                // 0x240 / 0x24 = element 16 — the first of the four selectable rows.
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar10 / 0x24].b = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar10 / 0x24].g = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar10 / 0x24].r = 0x40;
                iVar5 = iVar5 + 1;
                iVar10 = iVar10 + 0x24;
            }
            while (iVar5 < 4);

            FrameStep.DrawFrame();
        }

        iVar10 = g_DemoListCursor + -1;
        if (g_DemoListCursor == 0)
        {
            PsxRam.WriteU16(DAT_801ff000_Address + 0, 3);
            PsxRam.WriteU16(DAT_801ff000_Address + 2, 0);
            PsxRam.WriteU16(DAT_801ff000_Address + 4, 0);
            PsxRam.WriteU16(DAT_801ff000_Address + 6, 0);
        }
        else
        {
            // `&g_DemoSaveRecords3 + iVar10 * 2` on an undefined4 * is 0x801FF200 + iVar10 * 8; the four
            // halfwords at +0, +2, +4 and +6 of that record become the launch block.
            PsxRam.WriteU16(
                DAT_801ff000_Address + 0, PsxRam.ReadU16(g_DemoSaveRecords3_Address + (iVar10 * 8) + 0));
            PsxRam.WriteU16(
                DAT_801ff000_Address + 2, PsxRam.ReadU16(g_DemoSaveRecords3_Address + (iVar10 * 8) + 2));
            PsxRam.WriteU16(
                DAT_801ff000_Address + 4, PsxRam.ReadU16(g_DemoSaveRecords3_Address + (iVar10 * 8) + 4));
            PsxRam.WriteU16(
                DAT_801ff000_Address + 6, PsxRam.ReadU16(g_DemoSaveRecords3_Address + (iVar10 * 8) + 6));
        }

        ScreenDecoration.FUN_8002cc04(0);
        StopCdAudio();
        OverlayExit.ShutdownAndLoadExecutable("cdrom:\\DEMO.EXE;1");
    }

    // GHIDRA: RunVsModeScreen @ 0x80030EF8
    // 432 bytes. main's state 1 — the VS branch.
    //
    // It is a three-item sub-menu (cursor g_VsSubMenuCursor, sprite base 2, screen id 1) wrapped around
    // the 3-on-3 character select RunVsTeamSelect @ 0x80031E98. Confirming a sub-menu item runs the
    // character select; if that came back with bit 2 of DAT_80055b80 set — its "selection confirmed"
    // signal — the branch leaves and hands over to VS.EXE. Otherwise it plays the zoom-back-out and
    // returns to the sub-menu. Cancel at the sub-menu unwinds to the mode menu.
    internal static void RunVsModeScreen()
    {
        int iVar1;
        uint uVar2;
        int iVar3;
        double uVar4;

        if (g_VsSubMenuCursor == 0)
        {
            iVar1 = PadInput.GetPadStatus(1);
            if (iVar1 != 0)
            {
                g_VsSubMenuCursor = 1;
            }
        }

        ScreenDecoration.FUN_8002a178(g_VsSubMenuCursor);
        while (true)
        {
            uVar2 = (uint)ListCursor.RunListSelect(ref g_VsSubMenuCursor, 2, 3, 1);

            // `(uVar2 < 2) || (uVar2 == 2)` — an unsigned test, so the -1 and -2 answers
            // (0xFFFFFFFF and 0xFFFFFFFE) both fall past it.
            if ((uVar2 < 2) || (uVar2 == 2))
            {
                g_VsSubMenuCursor = (int)uVar2;
                CharacterSelect.RunVsTeamSelect((int)uVar2);
                iVar1 = 0xb;
                if ((SELECT_EXE_exe.DAT_80055b80 & 4) != 0)
                {
                    break;
                }

                iVar3 = 0x240;
                do
                {
                    // 0x240 down to 0x0B4 by 0x24 — elements 16 down to 5, hidden all at once.
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar3 / 0x24].attribute = 0x80000000;
                    iVar1 = iVar1 + -1;
                    iVar3 = iVar3 + -0x24;
                }
                while (-1 < iVar1);

                iVar1 = 9;
                do
                {
                    uVar4 = __floatsidf(iVar1);
                    iVar1 = iVar1 + -1;

                    // 0x40799999_9999999A = 409.6
                    uVar4 = __muldf3(uVar4, 409.6);
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].scalex = (short)__fixdfsi(uVar4);
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x12].scalex =
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x11].scalex;
                    FrameStep.DrawFrame();
                }
                while (-1 < iVar1);

                iVar1 = 0;
                do
                {
                    iVar1 = iVar1 + 4;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex =
                        (short)(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex + 0x80);
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[2].scalex =
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[3].scalex =
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[4].scalex =
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[1].scalex;
                    FrameStep.DrawFrame();
                }
                while (iVar1 < 0x80);
            }

            if ((SELECT_EXE_exe.DAT_80055b80 & 4) != 0)
            {
                break;
            }

            if (uVar2 == 0xffffffff)
            {
                ScreenDecoration.FUN_8002a6f8();
                return;
            }
        }

        // `jal 0x8002CC04` at 0x80031064 has a NOP in its delay slot and nothing in this function
        // writes a0 before it, so the argument here is whatever the preceding code left in the
        // register — the same leaked-argument shape MemoryCard.cs records for RunSaveLoadFlow. IT
        // CANNOT MATTER, and that is now closed rather than assumed: Ghidra recovers FUN_8002cc04's
        // signature as `void FUN_8002cc04(void)` and its body never reads a0 — the first instruction
        // to touch that register is the `jal InitializeSpriteArray` at the top. The two card pickers pass 0
        // and 1 explicitly; the 0 here satisfies C# and is not a claim.
        ScreenDecoration.FUN_8002cc04(0);
        StopCdAudio();
        OverlayExit.ShutdownAndLoadExecutable("cdrom:\\VS.EXE;1");
    }

    // GHIDRA: RunSpModeScreen @ 0x800310A8
    // 1304 bytes. main's state 2 — the SP save-slot picker.
    //
    // The same shape as RunDemoModeScreen over the OTHER list (three sixteen-byte records at 0x801FF218)
    // and with one extra job: on every rebuild it decodes the halfword at +2 of each record as a
    // decimal number and blits its two digits out of a strip in VRAM with MoveImage. The source cell
    // is the RECT constant in .sdata at 0x80055A48 — (0, 0xDD), 4 by 16 — with x re-aimed at
    // 0x300 + digit * 4 before each blit; the destinations are (0x3E4 + row * 8, 0x100) for the tens
    // digit and (0x3E8 + row * 8, 0x100) for the units.
    internal static void RunSpModeScreen()
    {
        ushort uVar2;
        uint uVar3;
        int iVar5;
        int iVar6;
        int iVar7;
        short sVar8;
        sbyte cVar9;
        int iVar10;
        int puVar11;
        int iVar12;
        RECT local_20 = new RECT();

        // `local_20._0_4_ = g_SpBranchDigitCellRect; local_20._4_4_ = DAT_80055a4c;` — Ghidra renders each word
        // twice, once as the unaligned SWL/SWR pair the compiler emitted for the struct copy and
        // once as the aligned store. Both write the same four bytes.
        local_20.x = (short)(g_SpBranchDigitCellRect & 0xffff);
        local_20.y = (short)(g_SpBranchDigitCellRect >> 16);
        local_20.w = (short)(DAT_80055a4c & 0xffff);
        local_20.h = (short)(DAT_80055a4c >> 16);

        CardRecords.FUN_800276d8(1, g_SpSaveRecords3_Address);
        iVar10 = 0;
        ListCursor.g_ListRowAvailable4 = 1;
        iVar7 = 0;
        g_SpListCursor = 0;
        ListCursor.DAT_80055b85 = (byte)(PsxRam.ReadU8(g_SpSaveRecords3_Address) & 1);
        ListCursor.DAT_80055b86 = (byte)(PsxRam.ReadU8(g_SpSaveRecords3_Address + 0x10) & 1);
        ListCursor.DAT_80055b87 = (byte)(PsxRam.ReadU8(g_SpSaveRecords3_Address + 0x20) & 1);
        do
        {
            puVar11 = g_SpSaveRecords3_Address + iVar7;
            iVar7 = iVar7 + 0x10;
            if ((PsxRam.ReadU16(puVar11) & 2) != 0)
            {
                g_SpListCursor = iVar10 + 1;
            }

            iVar10 = iVar10 + 1;
        }
        while (iVar10 < 3);

        ScreenDecoration.BuildSpSaveSlotScreen(g_SpListCursor, g_SpSaveRecords3_Address);
        while (true)
        {
            iVar7 = ListCursor.RunListSelect(ref g_SpListCursor, 0x10, 4, 3);
            if (iVar7 == -1)
            {
                ScreenDecoration.UnwindSpSaveSlotScreen();
                return;
            }

            if (iVar7 != -2)
            {
                break;
            }

            CardRecords.FUN_800276d8(1, g_SpSaveRecords3_Address);
            iVar10 = 0;
            ListCursor.g_ListRowAvailable4 = 1;
            iVar7 = 0;
            g_SpListCursor = 0;
            ListCursor.DAT_80055b85 = (byte)(PsxRam.ReadU8(g_SpSaveRecords3_Address) & 1);
            ListCursor.DAT_80055b87 = (byte)(PsxRam.ReadU8(g_SpSaveRecords3_Address + 0x20) & 1);
            ListCursor.DAT_80055b86 = (byte)(PsxRam.ReadU8(g_SpSaveRecords3_Address + 0x10) & 1);
            do
            {
                puVar11 = g_SpSaveRecords3_Address + iVar7;
                iVar7 = iVar7 + 0x10;
                if ((PsxRam.ReadU16(puVar11) & 2) != 0)
                {
                    g_SpListCursor = iVar10 + 1;
                }

                iVar10 = iVar10 + 1;
            }
            while (iVar10 < 3);

            iVar10 = 0;

            // &DAT_801ff21a — field +2 of record 0; `puVar11 + 8` on a ushort * is +0x10.
            puVar11 = g_SpSaveRecords3_Address + 2;
            iVar12 = 0x3e4;
            iVar7 = 1000;
            do
            {
                uVar3 = (uint)((PsxRam.ReadU16(puVar11) + 1) / 10);
                iVar5 = (int)uVar3 - 1;
                if (uVar3 == 0)
                {
                    iVar5 = 9;
                }

                // `(short)((iVar5 << 0x10) >> 0xe)` is iVar5 * 4 taken through the low halfword.
                local_20.x = (short)((short)((iVar5 << 0x10) >> 0xe) + 0x300);
                MoveImage(local_20, iVar12, 0x100);
                uVar2 = PsxRam.ReadU16(puVar11);
                puVar11 = puVar11 + 0x10;
                iVar12 = iVar12 + 8;
                iVar10 = iVar10 + 1;
                local_20.x = (short)(((uVar2 % 10) * 4) + 0x300);
                MoveImage(local_20, iVar7, 0x100);
                iVar7 = iVar7 + 8;
            }
            while (iVar10 < 3);

            iVar5 = 0;
            iVar7 = 0x168;
            iVar12 = 10;
            cVar9 = -0x70;
            sVar8 = -0x2b;
            iVar10 = 0xfc;
            do
            {
                int e7 = iVar10 / 0x24;
                int e10 = iVar7 / 0x24;

                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].h = 0x10;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].tpage = 0x1f;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].y = sVar8;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].u = (byte)cVar9;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].w = 0x20;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].b = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].g = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].r = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].cx = 0x170;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].cx = 0x170;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].cy = 0x1fd;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].cy = 0x1fd;
                iVar6 = iVar12 + 3;
                iVar12 = iVar12 + 1;
                cVar9 = (sbyte)(cVar9 + 0x20);
                sVar8 = (short)(sVar8 + 0x18);
                iVar10 = iVar10 + 0x24;
                iVar5 = iVar5 + 1;
                iVar6 = (int)((uint)(ListCursor.AvailabilityByte(iVar6 - 0xc) == 0 ? 1 : 0) << 0x1f);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].attribute = (uint)iVar6;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e7].attribute = (uint)iVar6;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].b = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].g = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e10].r = 0x40;
                iVar7 = iVar7 + 0x24;
            }
            while (iVar5 < 3);

            iVar10 = 2;
            iVar7 = 0x240;
            do
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7 / 0x24].b = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7 / 0x24].g = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar7 / 0x24].r = 0x40;
                iVar10 = iVar10 + 1;
                iVar7 = iVar7 + 0x24;
            }
            while (iVar10 < 6);
        }

        iVar7 = g_SpListCursor + -1;
        if (g_SpListCursor == 0)
        {
            PsxRam.WriteU16(DAT_801ff000_Address + 0x08, 3);
            PsxRam.WriteU16(DAT_801ff000_Address + 0x0a, 0);
            PsxRam.WriteI32(DAT_801ff000_Address + 0x0c, 0);
            PsxRam.WriteI32(DAT_801ff000_Address + 0x10, 0);
            PsxRam.WriteU16(DAT_801ff000_Address + 0x14, 0);
            PsxRam.WriteU16(DAT_801ff000_Address + 0x16, 0);
        }
        else
        {
            // The four folded displacements Ghidra prints resolve as -0x7fe00de4 = 0x801FF21C,
            // -0x7fe00de0 = 0x801FF220, -0x7fe00ddc = 0x801FF224 and -0x7fe00dda = 0x801FF226 —
            // fields +4, +8, +12 and +14 of record 0. iVar10 = iVar7 * 0x10 is the record stride.
            iVar10 = iVar7 * 0x10;
            PsxRam.WriteU16(
                DAT_801ff000_Address + 0x08, PsxRam.ReadU16(g_SpSaveRecords3_Address + (iVar7 * 0x10)));
            PsxRam.WriteU16(
                DAT_801ff000_Address + 0x0a,
                PsxRam.ReadU16(g_SpSaveRecords3_Address + 2 + (iVar7 * 0x10)));
            PsxRam.WriteI32(
                DAT_801ff000_Address + 0x0c, PsxRam.ReadI32(iVar10 + unchecked((int)0x801FF21C)));
            PsxRam.WriteI32(
                DAT_801ff000_Address + 0x10, PsxRam.ReadI32(iVar10 + unchecked((int)0x801FF220)));
            PsxRam.WriteU16(
                DAT_801ff000_Address + 0x14, PsxRam.ReadU16(iVar10 + unchecked((int)0x801FF224)));
            PsxRam.WriteU16(
                DAT_801ff000_Address + 0x16, PsxRam.ReadU16(iVar10 + unchecked((int)0x801FF226)));
        }

        ScreenDecoration.FUN_8002cc04(1);
        StopCdAudio();
        OverlayExit.ShutdownAndLoadExecutable("cdrom:\\SP.EXE;1");
    }

    // THE EIGHT STUBS THAT USED TO STAND HERE ARE GONE. FUN_800276d8 @ 0x800276D8 is transliterated
    // in CardRecords.cs, the module it belongs to; BuildDemoSaveSlotScreen, UnwindDemoSaveSlotScreen, FUN_8002a178,
    // FUN_8002a6f8, BuildSpSaveSlotScreen, UnwindSpSaveSlotScreen and FUN_8002cc04 are transliterated in
    // ScreenDecoration.cs, which is their own emission block (0x80029684..0x8002EA8B). The call
    // sites above now name those classes.

    // GHIDRA: StopCdAudio @ 0x80025894
    private static void StopCdAudio()
    {
        // BLOCKED: CdControlB(CdlInit) then CdControlB(CdlStop), 92 bytes, five call sites — it stops
        // the CD-DA track before the overlay hands over. It belongs to the CD module (CdAudio.cs),
        // whose TOC-dependent half is already blocked there because the drive's response FIFO is not modelled, so CdGetToc yields an all-zero table.
    }

}
