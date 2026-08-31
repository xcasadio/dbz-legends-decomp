using static PsxSdkMonogame.LibCd;

namespace DbzLegendsRemaster.SELECT_EXE;

// The CD-DA (Redbook) service of SELECT.EXE: the once-per-frame poll the frame step ends with, and
// the globals the whole little module keeps.
//
// THE SELECT SCREEN'S MUSIC IS A DISC TRACK, NOT A SEQUENCE. LoadUSAGI_B @ 0x80030908 starts it
// with FUN_80025658 (CdGetToc + CdMix + the default track index 3), FUN_800258f0(10, 3)
// (CdlStandby + CdlSetmode + CdlPlay against that TOC entry) and FUN_80025d04 (CdlPlay again at the
// current position). Those three are still BLOCKED stubs in SelectScreen.cs — they are the same
// 0x80025658..0x80025D63 module as the function below, and they are left where the boot phase put
// them rather than moved here, because moving them would be a refactor and not a transliteration.
//
// This file carries UpdateCdAudio @ 0x80025788 and every global it reads or writes.
internal static class CdAudio
{
    // GHIDRA: g_CdSyncResult @ 0x80055AA8
    // .sbss, undefined4. The last CdSync status. FUN_80025658 zeroes it; UpdateCdAudio stores
    // CdSync(1, NULL) into it every frame.
    // PARTIAL: no reader is on this slice's path, so what consumes the status is not closed.
    internal static int g_CdSyncResult;

    // GHIDRA: g_CdReadyResult @ 0x80055AAC
    // .sbss, undefined4. The last CdReady result. Written and immediately tested by UpdateCdAudio;
    // the whole track-advance branch hangs off `== 1` (CdlDataReady).
    internal static int g_CdReadyResult;

    // GHIDRA: DAT_80055ab8 @ 0x80055AB8
    // .sbss, undefined4. The track number the drive reports, decoded from BCD. FUN_80025658 seeds
    // it with 3 and FUN_800258f0 sets it to the track it was asked to play.
    internal static int DAT_80055ab8;

    // GHIDRA: g_CdPlayTocIndex @ 0x80055ABC
    // .sbss, undefined4. The track index the module is playing — the index into the TOC array at
    // 0x80055CEC, not a physical track number. FUN_80025658 seeds it with 3, FUN_800258f0 sets it
    // from its own second argument. The compare against DAT_80055ab8 is SIGNED (`slt` at
    // 0x800257F0).
    //
    // The C# compiler flags it CS0649, "never assigned", and the flag is true of THIS PORT rather
    // than of the original: both writers — FUN_80025658 @ 0x80025658 and FUN_800258f0 @ 0x800258F0
    // — are still BLOCKED stubs in SelectScreen.cs. The warning is suppressed for this one field so
    // the build stays clean. The field is otherwise untouched, and the value it holds in the port
    // is the 0 that start's .sbss clear leaves, which indexes TOC entry 0 instead of entry 3.
#pragma warning disable CS0649
    internal static int g_CdPlayTocIndex;
#pragma warning restore CS0649

    // GHIDRA: DAT_80055ac0 @ 0x80055AC0
    // .sbss, undefined4. THE CD-DA STATE FLAGS, three of which this slice sees:
    //   bit 0 (1)  suppress the whole per-frame service. FUN_80025658 SETS it as its last act;
    //              FUN_800258f0's play branch and FUN_80025d04 clear it again.
    //   bit 1 (2)  a track is playing -> re-issue CdlPlay when the drive has run past it
    //   bit 2 (4)  pause first -> CdlPause, then latch bit 0 back on
    // FUN_800258f0(10, 3) leaves it at 0x0A (bits 1 and 3 from its first argument, bit 0 cleared),
    // which is the state the select screen runs in.
    // PARTIAL: bit 3 (8) is set by that same call and has no reader in the module.
    internal static int DAT_80055ac0;

    // GHIDRA: g_CdResultBuffer8 @ 0x80055AD4
    // .sbss. The EIGHT-BYTE libcd result buffer every command in this module is handed. Its
    // second byte — what Ghidra names DAT_80055ad5 and reads with `lbu a0,0xed(gp)` — is the BCD
    // minute CdReady reports in CdlModeDA report mode.
    // EXTENT CLOSED AT BOTH ENDS: 0x80055AD4 is the address CdReady is given, and 0x80055ADC is
    // g_CdMixVolume, the four-byte CdlATV FUN_80025658 hands to CdMix. Eight bytes, which is what a
    // libcd result block is.
    internal static readonly byte[] g_CdResultBuffer8 = new byte[8];

    // GHIDRA: DAT_80055ae0 @ 0x80055AE0
    // .sbss, undefined1. The last command byte the module issued, stored with `sb` before each
    // CdControl. PARTIAL: nothing in SELECT.EXE reads it back on any path found; the stores are
    // kept because the original makes them.
    internal static byte DAT_80055ae0;

    // GHIDRA: g_CdTocLocations @ 0x80055CEC
    // .bss. THE DISC TOC, as CdlLOC entries. FUN_80025658 @ 0x80025658 closes both the element type
    // and the stride: it does `p = (CdlLOC *)&g_CdTocLocations`, fills it with
    // `CdGetToc((CdlLOC *)&g_CdTocLocations)` and then normalises entry by entry with `p = p + 1`. The
    // byte arithmetic every other reader uses agrees — UpdateCdAudio below indexes
    // `&g_CdTocLocations + g_CdPlayTocIndex * 4` and FUN_800258f0 `&g_CdTocLocations + (track << 16 >> 14)`,
    // and 4 is sizeof(CdlLOC).
    // EXTENT: THIRTY-TWO entries, 128 bytes, 0x80055CEC..0x80055D6B. Closed by the neighbour above
    // it: 0x80055D6C is the first of the two 0x22-byte BIOS pad buffers InitializeBiosPad @ 0x800261A4
    // hands to InitPAD, and nothing between the two addresses is referenced anywhere in the program
    // (find-constants-in-range over 0x80055CED..0x80055D6B returns nothing).
    // PARTIAL: how many of the 32 CdGetToc actually fills is a property of the disc, not of the
    // code. FUN_80025658 keeps the answer in DAT_80055ab0 as `CdGetToc(...) - 1`.
    internal static readonly CdlLOC[] g_CdTocLocations = NewCdlLocArray(32);

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original's TOC is 128 zeroed .bss bytes that CdGetToc then fills as CdlLOC[32].
    // C# needs each element constructed before it can be written or passed.
    private static CdlLOC[] NewCdlLocArray(int count)
    {
        var result = new CdlLOC[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new CdlLOC();
        }

        return result;
    }

    // GHIDRA: UpdateCdAudio @ 0x80025788
    // 268 bytes, 7 call sites — the frame step DrawFrame @ 0x800344A4 once per frame, plus the
    // memory-card screens FUN_80021618, FUN_800218d4 and FUN_800276d8, which spin on it with
    // VSync(0) while they wait on the card.
    //
    // WHAT IT IS FOR: the drive is playing a CD-DA track in CdlModeRept, and this watches the BCD
    // minute the drive reports. When the reported track (DAT_80055ab8) has run past the one the
    // module asked for (g_CdPlayTocIndex), it pauses or re-issues CdlPlay at the TOC entry — i.e. it is
    // what loops the select screen's music.
    //
    // THE TRACK-ADVANCE BRANCH NEVER FIRES IN THIS PORT, AND THE MUSIC THEREFORE NEVER LOOPS.
    // That is not a transliteration choice, it is where PsxSdkMonogame currently stands:
    // LibCd.CdReady is a "Do nothing" stub returning 0, so g_CdReadyResult is 0, the `== 1` test fails
    // and the CdlPause / CdlPlay block below is dead. Everything downstream of it is dead with it —
    // the BCD decode, the compare, both CdControl calls. What still runs every frame is the
    // CdSync(1, NULL) store at the end. Stated here so the silence is read as a missing SDK body
    // and not as this function being wrong.
    //
    // SECOND, SMALLER CONSEQUENCE OF THE SAME KIND: on the console FUN_80025658 sets DAT_80055ac0
    // to 1 and FUN_800258f0 / FUN_80025d04 clear bit 0 again, so the gate is open by the time the
    // first frame is presented. In this port those three are BLOCKED stubs, so DAT_80055ac0 is
    // still 0 and the gate is open for a different reason. The observable behaviour is the same;
    // the reason is not, which is why it is written down.
    internal static void UpdateCdAudio()
    {
        int iVar1;
        byte[] auStack_18 = new byte[8];

        if ((DAT_80055ac0 & 1) == 0)
        {
            g_CdReadyResult = CdReady(1, g_CdResultBuffer8);

            // The original is one `&&` whose right operand is a comma expression, so the BCD decode
            // and the store into DAT_80055ab8 happen ONLY when CdReady reported 1. The nesting below
            // is that short circuit, not a reordering.
            if (g_CdReadyResult == 1)
            {
                // `(uint)(DAT_80055ad5 >> 4) * 10 + (DAT_80055ad5 & 0xf)` — packed BCD to binary,
                // on result byte 1.
                DAT_80055ab8 = (g_CdResultBuffer8[1] >> 4) * 10 + (g_CdResultBuffer8[1] & 0xf);
                if (g_CdPlayTocIndex < DAT_80055ab8)
                {
                    if ((DAT_80055ac0 & 4) != 0)
                    {
                        DAT_80055ae0 = 8;
                        CdControl(8, (byte[])null, null);
                        DAT_80055ac0 = 1;
                    }

                    if ((DAT_80055ac0 & 2) != 0)
                    {
                        do
                        {
                            DAT_80055ae0 = 3;

                            // `&g_CdTocLocations + g_CdPlayTocIndex * 4` on a char *, i.e. &toc[track].
                            iVar1 = CdControl(3, g_CdTocLocations[g_CdPlayTocIndex], auStack_18);
                        } while (iVar1 == 0);
                    }
                }
            }

            g_CdSyncResult = CdSync(1, null);
        }
    }
}
