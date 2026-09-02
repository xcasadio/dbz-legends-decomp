using PsxSdkMonogame;
using static PsxSdkMonogame.Kernel;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibEtc;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE MEMORY CARD MODULE of SELECT.EXE — the bring-up, the kernel-event handshake, the probe, the
// boot-time save load and the teardown. main calls three of them on its pre-loop path
// (InitializeMemoryCard, ProbeMemoryCard, RunSaveLoadFlow) and the exit path ShutdownAndLoadExecutable calls the fourth
// (ShutdownMemoryCard), so nothing downstream of main's `switch` is right without this file.
//
// WHERE THE FUNCTIONS LIVE IN THE IMAGE: two runs of .text.
//   0x800213A8..0x800213B7   ResetCardOperationState, the two-store state reset
//   0x80021618..0x800218D3   RunSaveLoadFlow, the boot-time save load
//   0x80021CE4..0x80021D33   InitializeMemoryCard, the bring-up
//   0x80021D34..0x80021E33   ShutdownMemoryCard, the teardown
//   0x80021E34..0x80021F0B   ProbeMemoryCard, the probe
//   0x80021FB4..0x80022023   QueryCardStatus, the single-shot _card_info wrapper
//   0x800220D4..0x80022137   IsSaveFileMissing, "does the save file exist"
//   0x800221D0..0x80022243   FUN_800221d0, the 0xF4000001 poll
//   0x80022244..0x800222B7   FUN_80022244, the 0xF0000011 poll
//   0x800222B8..0x800222FF   FUN_800222b8, the 0xF4000001 drain
//   0x80022300..0x80022347   FUN_80022300, the 0xF0000011 drain
//   0x80022348..0x800224AF   OpenMemoryCardEvents, the eight OpenEvent calls
//   0x80022810..0x80022943   ReadSaveRecord, the record read + checksum
//
// HOW THE HANDSHAKE WORKS, AND WHY IT TERMINATES IN THIS PORT. OpenMemoryCardEvents opens eight kernel
// events with a NULL callback — four on class 0xF4000001 (the card driver) and four on class
// 0xF0000011 (the "card write/clear" side), each on spec 0x0004 / 0x8000 / 0x0100 / 0x2000 — and
// enables all eight. Every card command is then issued as: drain (FUN_800222b8 / FUN_80022300,
// four TestEvent calls whose results are thrown away), issue the command, poll (FUN_800221d0 /
// FUN_80022244, four TestEvent calls in a do/while until one returns 1). PsxSdkMonogame's LibApi
// now carries a real event table — TestEvent returns 1 only when a matching DeliverEvent has
// landed on an OPEN and ENABLED descriptor, and consumes it — and _card_info / _card_clear /
// _card_load deliver synchronously before returning. So the poll loop always has a delivery
// waiting when it runs, and always terminates on its first pass. Verified by reading LibApi.cs
// (OpenEvent/EnableEvent/DeliverEvent/TestEvent, lines 604-774, and _card_info/_card_clear/
// _card_load, lines 74-104) rather than taken from any dossier.
//
// THE SPEC-TO-CODE MAP the poll loops define, and which this port's SDK honours:
//     spec 0x0004 -> 0     spec 0x8000 -> 1     spec 0x0100 -> 2     spec 0x2000 -> 4
// LibApi._card_info delivers 0x0004 for a present card and 0x8000 for a missing one; _card_load
// delivers 0x0004; _card_clear delivers (0xF0000011, 0x0004). Nothing in this port ever delivers
// 0x0100 or 0x2000, so codes 2 and 4 are unreachable here — which is why QueryCardStatus's retry arm
// and ProbeMemoryCard's _card_clear arm are dead in the port and live on the console. Stated so the
// unexecuted branches are not read as wrong.
internal static class MemoryCard
{
    // GHIDRA: g_CardDevicePort1 @ 0x800559E8
    // .sdata, six bytes: 62 75 30 30 3A 00 = "bu00:" — the BIOS device name for memory card port 1.
    // Read out of the image with read-memory. IsSaveFileMissing and ReadSaveRecord copy it into a stack
    // buffer as `undefined4 + undefined2` (g_CardDevicePort1 + DAT_800559ec) and strcat the file name on.
    // The port models that scratch buffer as a C# string, which is the convention LibApi's
    // open(string, int) / firstfile(string, ...) overloads were added for (LibApi.cs lines 188-194).
    internal const string g_CardDevicePort1 = "bu00:";

    // GHIDRA: g_CardDevicePort2 @ 0x800559F0
    // .sdata, six bytes: 62 75 31 30 3A 00 = "bu10:" — port 2. Neither of this module's two callers
    // ever passes a non-zero port on main's path, so this string is loaded by no reachable branch
    // here; it is kept because the two `if (param_1 == 0) ... else ...` are kept.
    internal const string g_CardDevicePort2 = "bu10:";

    // GHIDRA: DAT_80055a78 @ 0x80055A78
    // .sbss, undefined4. RunSaveLoadFlow writes only its LOW HALFWORD (`sh`, which Ghidra spells
    // `DAT_80055a78._0_2_`) with QueryCardStatus's result, and reads it back as a signed short. The
    // masked store below is that `sh`, not a widening.
    // This is also the first word start's .bss clear loop covers — SELECT_EXE_exe.BssClearFirst
    // names the same address as the start of the range.
    internal static int DAT_80055a78;

    // GHIDRA: DAT_80055a80 @ 0x80055A80
    // .sbss, undefined2. RunSaveLoadFlow sets it to 1 on entry.
    // PARTIAL: WRITE-ONLY IN THE WHOLE OVERLAY. find-cross-references reports exactly two references
    // and both are writes (RunSaveLoadFlow @ 0x8002163C and LoadSaveRecords @ 0x800213DC). Nothing reads
    // it, so what it means is not closed; the store is kept because the original makes it.
    // The C# compiler flags it CS0414, "assigned but never used", and unlike CdAudio.g_CdPlayTocIndex
    // that is a property OF THE ORIGINAL and not of this port — there is no reader to port.
    // Suppressed for this one field so the build stays clean.
#pragma warning disable CS0414
    internal static ushort DAT_80055a80;
#pragma warning restore CS0414

    // GHIDRA: g_CardOperationState @ 0x80055A84
    // .sbss, undefined2. THE SAVE-LOAD STATE. ResetCardOperationState zeroes it during bring-up; RunSaveLoadFlow's
    // switch drives it. The states this slice sees: 0 (start) -> 7 (does the file exist) -> 0xF
    // (read the record) or 8 / 0x10 (the two failure screens) or 2 (the "wrong card" screen).
    internal static ushort g_CardOperationState;

    // GHIDRA: g_CardReprobeRequest @ 0x80055A88
    // .sbss, undefined2. "Re-probe the card on the next pass" — RunSaveLoadFlow's loop head consumes it
    // and calls ProbeMemoryCard. ResetCardOperationState zeroes it during bring-up.
    internal static ushort g_CardReprobeRequest;

    // GHIDRA: DAT_80055a8c @ 0x80055A8C
    // .sbss, undefined2. The pass counter inside state 0xF: pass 0 reads the record, pass 1 returns
    // 1 to the caller. It is reset to 0 by states 0 and 1.
    internal static ushort DAT_80055a8c;

    // GHIDRA: g_SwCardEvents4 @ 0x80055B54, long[4]
    // .sbss. Ghidra poses this as one four-element `long[4]` (MIPS32, so `long` is four bytes here);
    // the C# port keeps `int[4]` as the exact same-width transliteration. Formerly four separate
    // undefined4 words at 0x80055B54, 0x80055B58, 0x80055B5C and 0x80055B60, now indices:
    //   [0] OpenEvent(0xF4000001, 0x0004, 0x2000, NULL) — card command completed.
    //   [1] OpenEvent(0xF4000001, 0x8000, 0x2000, NULL) — card command failed.
    //   [2] OpenEvent(0xF4000001, 0x0100, 0x2000, NULL) — poll code 2.
    //   [3] OpenEvent(0xF4000001, 0x2000, 0x2000, NULL) — poll code 4, the "new / unformatted card"
    //       answer ProbeMemoryCard reacts to with _card_clear.
    // These are the four open event descriptors on the SwCARD class 0xF4000001.
    private static readonly int[] g_SwCardEvents4 = new int[4];

    // GHIDRA: DAT_80055b68 @ 0x80055B68
    // .sbss, undefined2. ProbeMemoryCard sets it to 1 when the probe took the _card_clear arm and to 0
    // otherwise.
    // PARTIAL: WRITE-ONLY IN THE WHOLE OVERLAY — find-cross-references reports exactly the two
    // writes inside ProbeMemoryCard (0x80021E88 and 0x80021E9C) and no read at all. CS0414 is
    // suppressed for the same reason as DAT_80055a80 above: the original has no reader either.
#pragma warning disable CS0414
    private static ushort DAT_80055b68;
#pragma warning restore CS0414

    // GHIDRA: DAT_80055b70 @ 0x80055B70
    // .sbss, undefined4. OpenEvent(0xF0000011, 0x0004, 0x2000, NULL) — the _card_clear completion.
    private static int DAT_80055b70;

    // GHIDRA: DAT_80055b74 @ 0x80055B74
    // .sbss, undefined4. OpenEvent(0xF0000011, 0x8000, 0x2000, NULL).
    private static int DAT_80055b74;

    // GHIDRA: DAT_80055b78 @ 0x80055B78
    // .sbss, undefined4. OpenEvent(0xF0000011, 0x0100, 0x2000, NULL).
    private static int DAT_80055b78;

    // GHIDRA: DAT_80055b7c @ 0x80055B7C
    // .sbss, undefined4. OpenEvent(0xF0000011, 0x2000, 0x2000, NULL).
    private static int DAT_80055b7c;

    // GHIDRA: g_OptionsRecord64 @ 0x801FF018
    // The destination RunSaveLoadFlow copies the 64-byte save record to, inside the cross-overlay block
    // SharedHighRam models. The write goes through the raw PSX address the original writes, which
    // SELECT_EXE_exe.ResolveAddress answers for by chaining SharedHighRam — the same route main's own
    // read of this word takes.
    // THE RECORD'S EXTENT IS CLOSED BY ITS NEIGHBOURS: 0x801FF018 + 0x40 = 0x801FF058, and
    // SharedHighRam.DAT_801ff058 is the next named byte. The 64 bytes therefore cover the options
    // word at +0x00 (whose bit 1 main tests to gate menu item 2) and both button-remap tables,
    // OverlayExit.g_PadRemapTable0 at +0x08 and OverlayExit.g_PadRemapTable1 at +0x24.
    private const int g_OptionsRecord64_Address = unchecked((int)0x801FF018);

    // JUSTIFICATION: C# language bridge only
    // RELATION: stands in for the BIOS/libc strcat that IsSaveFileMissing and ReadSaveRecord call through
    // the jump stub at 0x8004EA74. PsxSdkMonogame provides no strcat, and per rule 13 an SDK routine
    // is NOT transliterated into a game file — this is only the language bridge that lets the two
    // call sites keep their original shape, because the fixed stack buffer they strcat into is
    // modelled as a C# string exactly as LibApi's open(string, int) overload already assumes.
    // Reported as a missing SDK routine rather than hidden here.
    internal static string strcat(string param_1, string param_2)
    {
        return param_1 + param_2;
    }

    // GHIDRA: InitializeMemoryCard @ 0x80021CE4
    // Eighty bytes, seven calls, no locals — the memory-card bring-up. main calls it once, between
    // OverlayExit.InitializePadRemapTablePointers and the probe ProbeMemoryCard; FUN_800276d8 @ 0x800276D8 is the other
    // call site and is not on this slice's path.
    // It is the same seven calls in the same order as TITLE.EXE's InitializeMemoryCard @ 0x80022630.
    internal static void InitializeMemoryCard()
    {
        InitCARD(1);
        StartCARD();
        _bu_init();
        OpenMemoryCardEvents();
        _card_auto(0);
        ChangeClearPAD(0);
        ResetCardOperationState();
    }

    // GHIDRA: OpenMemoryCardEvents @ 0x80022348
    // Three hundred and sixty bytes. Eight OpenEvent calls inside a critical section, then eight
    // EnableEvent calls outside it — the split is load-bearing on the console (the table is built
    // with the ISR masked, and only armed once it is safe for a delivery to land) and LibApi's
    // OpenEvent honours it by NOT arming the descriptor it returns.
    // EnterCriticalSection / ExitCriticalSection are EnterCriticalSection / ExitCriticalSection — three-instruction
    // `li a0,1 / syscall 0 / jr ra` bodies in this image, not jump stubs; see the evidence block at
    // the foot of LibApi.cs. They are empty on desktop because every DeliverEvent in this port is
    // issued synchronously by the game thread, so there is no ISR to mask.
    // THE CALLBACK IS NULL FOR ALL EIGHT, which is what makes these poll-only events.
    private static void OpenMemoryCardEvents()
    {
        EnterCriticalSection();
        g_SwCardEvents4[0] = (int)OpenEvent(0xf4000001, 4, 0x2000, null);
        g_SwCardEvents4[1] = (int)OpenEvent(0xf4000001, 0x8000, 0x2000, null);
        g_SwCardEvents4[2] = (int)OpenEvent(0xf4000001, 0x100, 0x2000, null);
        g_SwCardEvents4[3] = (int)OpenEvent(0xf4000001, 0x2000, 0x2000, null);
        DAT_80055b70 = (int)OpenEvent(0xf0000011, 4, 0x2000, null);
        DAT_80055b74 = (int)OpenEvent(0xf0000011, 0x8000, 0x2000, null);
        DAT_80055b78 = (int)OpenEvent(0xf0000011, 0x100, 0x2000, null);
        DAT_80055b7c = (int)OpenEvent(0xf0000011, 0x2000, 0x2000, null);
        ExitCriticalSection();
        EnableEvent(g_SwCardEvents4[0]);
        EnableEvent(g_SwCardEvents4[1]);
        EnableEvent(g_SwCardEvents4[2]);
        EnableEvent(g_SwCardEvents4[3]);
        EnableEvent(DAT_80055b70);
        EnableEvent(DAT_80055b74);
        EnableEvent(DAT_80055b78);
        EnableEvent(DAT_80055b7c);
    }

    // GHIDRA: ResetCardOperationState @ 0x800213A8
    // Sixteen bytes and the whole of it is two halfword stores — read with read-memory:
    //     A7 80 00 9C   sh zero, 0x9C(gp)   -> g_CardOperationState (gp = 0x800559E8)
    //     A7 80 00 A0   sh zero, 0xA0(gp)   -> g_CardReprobeRequest
    //     03 E0 00 08   jr ra
    // It resets the save-load state machine that RunSaveLoadFlow (and its two siblings LoadSaveRecords and
    // RunSaveWriteFlow, neither on this slice's path) drive.
    private static void ResetCardOperationState()
    {
        g_CardOperationState = 0;
        g_CardReprobeRequest = 0;
    }

    // GHIDRA: FUN_800222b8 @ 0x800222B8
    // Seventy-two bytes: four TestEvent calls on the 0xF4000001 handles WITH EVERY RESULT THROWN
    // AWAY. It is a drain, issued immediately before each _card_info / _card_load so that the poll
    // that follows cannot read a stale delivery. It is dead code unless TestEvent consumes the flag
    // it reports — which is exactly how LibApi implements it, and this function is part of the
    // evidence for that (see the block comment above LibApi.TestEvent).
    private static void FUN_800222b8()
    {
        TestEvent(g_SwCardEvents4[0]);
        TestEvent(g_SwCardEvents4[1]);
        TestEvent(g_SwCardEvents4[2]);
        TestEvent(g_SwCardEvents4[3]);
    }

    // GHIDRA: FUN_80022300 @ 0x80022300
    // Seventy-two bytes, the 0xF0000011 twin of FUN_800222b8. Issued immediately before _card_clear.
    private static void FUN_80022300()
    {
        TestEvent(DAT_80055b70);
        TestEvent(DAT_80055b74);
        TestEvent(DAT_80055b78);
        TestEvent(DAT_80055b7c);
    }

    // GHIDRA: FUN_800221d0 @ 0x800221D0
    // One hundred and sixteen bytes, six call sites. The 0xF4000001 poll: spin until one of the four
    // handles fires, and map it to a code — 0x0004 -> 0, 0x8000 -> 1, 0x0100 -> 2, 0x2000 -> 4.
    // The do/while has NO bail-out. It terminates here because every call site issues a card command
    // first and this port's _card_* deliver synchronously; a call site that polled without issuing
    // one would hang, and that would be the honest answer, because there would be nothing to deliver.
    private static int FUN_800221d0()
    {
        int iVar1;

        do
        {
            iVar1 = (int)TestEvent(g_SwCardEvents4[0]);
            if (iVar1 == 1)
            {
                return 0;
            }

            iVar1 = (int)TestEvent(g_SwCardEvents4[1]);
            if (iVar1 == 1)
            {
                return 1;
            }

            iVar1 = (int)TestEvent(g_SwCardEvents4[2]);
            if (iVar1 == 1)
            {
                return 2;
            }

            iVar1 = (int)TestEvent(g_SwCardEvents4[3]);
        }
        while (iVar1 != 1);

        return 4;
    }

    // GHIDRA: FUN_80022244 @ 0x80022244
    // One hundred and sixteen bytes, the 0xF0000011 twin of FUN_800221d0, with the same code map.
    // Its two call sites both follow a _card_clear.
    // PARTIAL: the value is discarded at the call site inside ProbeMemoryCard (Ghidra renders it as a
    // bare `FUN_80022244();`), so only its blocking effect matters there.
    private static int FUN_80022244()
    {
        int iVar1;

        do
        {
            iVar1 = (int)TestEvent(DAT_80055b70);
            if (iVar1 == 1)
            {
                return 0;
            }

            iVar1 = (int)TestEvent(DAT_80055b74);
            if (iVar1 == 1)
            {
                return 1;
            }

            iVar1 = (int)TestEvent(DAT_80055b78);
            if (iVar1 == 1)
            {
                return 2;
            }

            iVar1 = (int)TestEvent(DAT_80055b7c);
        }
        while (iVar1 != 1);

        return 4;
    }

    // GHIDRA: ProbeMemoryCard @ 0x80021E34
    // Two hundred and sixteen bytes, six call sites — THE PROBE, and its return value is the card
    // status word main stores at 0x801FF068.
    // Two retry loops of at most five passes each, with an optional _card_clear between them:
    //   pass 1  drain, _card_info(chan), poll. Break as soon as the code is not 1 (not "failed").
    //   if the code is 4 (new/unformatted card): drain the 0xF0000011 side, _card_clear(chan), poll.
    //   pass 2  drain, _card_info's twin _card_load(chan), poll. Return the first code that is not 1.
    // Both loops return 1 only by exhausting five failing attempts.
    //
    // WHAT IT RETURNS IN THIS PORT: 0. LibMcrd.CardIsPresent is unconditionally true, so _card_info
    // delivers (0xF4000001, 0x0004) and the first poll returns 0 on its first pass; the _card_clear
    // arm is skipped (the code is 0, not 4); _card_load delivers the same pair and the second poll
    // returns 0 too. main then takes its `g_CardProbeResult == 0` arm and calls RunSaveLoadFlow.
    internal static int ProbeMemoryCard(int param_1)
    {
        int iVar1;
        int iVar2;

        iVar2 = 0;
        do
        {
            FUN_800222b8();
            _card_info(param_1);
            iVar1 = FUN_800221d0();
            if (iVar1 != 1)
            {
                break;
            }

            iVar2 = iVar2 + 1;
        }
        while (iVar2 < 5);

        DAT_80055b68 = 0;
        iVar2 = 0;
        if (iVar1 == 4)
        {
            DAT_80055b68 = 1;
            FUN_80022300();
            _card_clear(param_1);
            FUN_80022244();
        }

        do
        {
            FUN_800222b8();
            _card_load(param_1);
            iVar1 = FUN_800221d0();
            if (iVar1 != 1)
            {
                return iVar1;
            }

            iVar2 = iVar2 + 1;
        }
        while (iVar2 < 5);

        return 1;
    }

    // GHIDRA: ShutdownMemoryCard @ 0x80021D34
    // Two hundred and fifty-six bytes — the mirror image of InitializeMemoryCard + OpenMemoryCardEvents. Eight
    // DisableEvent OUTSIDE the critical section, eight CloseEvent inside it, then StopCARD and the
    // pad handed back to the BIOS driver.
    // Its two call sites are the exit path OverlayExit.ShutdownAndLoadExecutable @ 0x8003472C, which is on this
    // slice's path, and FUN_800276d8 @ 0x800276D8, which is not.
    internal static void ShutdownMemoryCard()
    {
        DisableEvent(g_SwCardEvents4[0]);
        DisableEvent(g_SwCardEvents4[1]);
        DisableEvent(g_SwCardEvents4[2]);
        DisableEvent(g_SwCardEvents4[3]);
        DisableEvent(DAT_80055b70);
        DisableEvent(DAT_80055b74);
        DisableEvent(DAT_80055b78);
        DisableEvent(DAT_80055b7c);
        EnterCriticalSection();
        CloseEvent(g_SwCardEvents4[0]);
        CloseEvent(g_SwCardEvents4[1]);
        CloseEvent(g_SwCardEvents4[2]);
        CloseEvent(g_SwCardEvents4[3]);
        CloseEvent(DAT_80055b70);
        CloseEvent(DAT_80055b74);
        CloseEvent(DAT_80055b78);
        CloseEvent(DAT_80055b7c);
        ExitCriticalSection();
        StopCARD();
        StartPAD();
        ChangeClearPAD(0);
    }

    // GHIDRA: RepollMemoryCard @ 0x80021F0C
    // One hundred and sixty-eight bytes. THE RE-POLL: a cut-down ProbeMemoryCard with no retry loops,
    // issued once per frame by ListCursor.RunListSelect @ 0x80033D34 while either card picker is up,
    // so that inserting or removing a card is noticed.
    //
    // param_1 IS PASSED, and it is the PREVIOUS status. Its only caller loads g_CardProbeResult into a0
    // (`lui a0,0x8020 / lw a0,-0x0f98(a0)` at 0x80033DAC-0x80033DB0), stores that same register into
    // g_PrevCardProbeResult, and jals here with a nop in the delay slot. Ghidra drops the argument from the
    // call site; the register does not.
    // The two arms it gates: a code-4 answer is only cleared when the caller was ALREADY at 4 (so a
    // card that has just gone unformatted is reported once before being formatted), and a code-2
    // answer is retried once. Neither arm fires when param_1 is 2.
    internal static int RepollMemoryCard(int param_1)
    {
        int iVar1;

        FUN_800222b8();
        _card_info(0);
        iVar1 = FUN_800221d0();
        if (((iVar1 == 4) && (param_1 != 2)) && (param_1 == 4))
        {
            FUN_80022300();
            _card_clear(0);
            iVar1 = FUN_80022244();
        }

        if ((iVar1 == 2) && (param_1 != 2))
        {
            FUN_800222b8();
            _card_info(0);
            iVar1 = FUN_800221d0();
        }

        return iVar1;
    }

    // GHIDRA: QueryCardStatus @ 0x80021FB4
    // One hundred and twelve bytes, three call sites, all of them the head of a save-load driver
    // (RunSaveLoadFlow here, plus LoadSaveRecords and RunSaveWriteFlow which are not on this slice's path).
    // A single _card_info, retried exactly once when the code came back 2.
    //
    // NOTE ON param_1 — IT IS NOT PASSED. RunSaveLoadFlow's call at 0x80021640 has
    // `addu s3, zero, zero` in its delay slot and its prologue never touches a0, so a0 holds
    // whatever the CALLER of RunSaveLoadFlow left there. Traced from main: the jal at 0x80030524 is
    // preceded by `addu a0, zero, zero` at 0x80030508 (the delay slot of the ProbeMemoryCard(0) call)
    // and nothing writes a0 in between, so a0 = 0 on main's path. The 0 below is that traced value,
    // not an invented argument.
    // PARTIAL: the OTHER caller of RunSaveLoadFlow, FUN_800276d8 @ 0x8002781C, is not on this slice's
    // path and its leaked a0 has not been traced. It cannot change anything reachable here: param_1
    // is read only inside `if ((iVar1 == 2) && (param_1 != 2))`, and code 2 means spec 0x0100, which
    // nothing in this port ever delivers.
    internal static int QueryCardStatus(int param_1)
    {
        int iVar1;

        FUN_800222b8();
        _card_info(0);
        iVar1 = FUN_800221d0();
        if ((iVar1 == 2) && (param_1 != 2))
        {
            FUN_800222b8();
            _card_info(0);
            iVar1 = FUN_800221d0();
        }

        return iVar1;
    }

    // GHIDRA: IsSaveFileMissing @ 0x800220D4
    // One hundred bytes. Builds "bu00:BISLPS-00355DRAGON" in a 32-byte stack buffer and asks the
    // BIOS card directory whether it is there. RETURNS 1 WHEN THE FILE IS ABSENT: the original is
    // `return iVar1 == 0;`, compiled as `sltiu v0, v0, 1`, over a firstfile that answers NULL/0 when
    // the directory has no match. RunSaveLoadFlow's state 7 reads it as `if (iVar6 == 0)` = "the save
    // exists, go read it".
    // The DIRENTRY it fills (`undefined1 auStack_30[40]`) is never looked at.
    internal static int IsSaveFileMissing(int param_1)
    {
        int iVar1;
        string local_50;
        LibMcrd.DIRENTRY auStack_30 = new LibMcrd.DIRENTRY();

        if (param_1 == 0)
        {
            local_50 = g_CardDevicePort1;
        }
        else
        {
            local_50 = g_CardDevicePort2;
        }

        local_50 = strcat(local_50, "BISLPS-00355DRAGON");
        iVar1 = firstfile(local_50, auStack_30);
        return iVar1 == 0 ? 1 : 0;
    }

    // GHIDRA: ReadSaveRecord @ 0x80022810
    // Three hundred and eight bytes, three call sites. Opens the same "bu00:BISLPS-00355DRAGON",
    // seeks to 0x200 + param_2 * 0x80, reads ONE 128-byte record, and validates it.
    //
    // THE RECORD LAYOUT, closed from the stack frame Ghidra recovered (`char local_90` at -0x90,
    // `byte local_8f[64]` at -0x8f, `undefined1 auStack_4f[62]` at -0x4f, `byte local_11` at -0x11 —
    // and the read is 0x80 bytes into &local_90, so those four names tile the record exactly):
    //     byte 0        magic, must be '.'
    //     bytes 1..64   the payload — the 64 bytes the caller copies to 0x801FF018
    //     bytes 65..126 not read by this function
    //     byte 127      the checksum byte
    // The check is `buf[127] ^ buf[1] ^ ... ^ buf[64] == 0`. It covers only 64 of the 128 bytes; that
    // is what the code does and it is reproduced, not corrected (rule 12).
    //
    // RETURNS: 0x80 for a good record, -1 for a bad magic or a bad checksum, 0 when the file will not
    // open or the read came up short. Only 0x80 makes the caller copy anything.
    //
    // NOTE THE ORDER: close(fd) happens BEFORE the magic/checksum test on the success path, and again
    // in the short-read arm. Both are kept where the original puts them.
    internal static int ReadSaveRecord(int param_1, int param_2, byte[] param_3)
    {
        byte bVar1;
        int iVar2;
        int uVar3;
        int iVar4;
        int pbVar5;
        string local_b0;
        byte[] local_90 = new byte[0x80];
        byte local_11;
        int param_3_index;

        if (param_1 == 0)
        {
            local_b0 = g_CardDevicePort1;
        }
        else
        {
            local_b0 = g_CardDevicePort2;
        }

        local_b0 = strcat(local_b0, "BISLPS-00355DRAGON");
        iVar2 = open(local_b0, 1);
        if (iVar2 == -1)
        {
            uVar3 = 0;
        }
        else
        {
            lseek(iVar2, param_2 * 0x80 + 0x200, 0);
            iVar4 = read(iVar2, local_90, 0, 0x80);
            if (iVar4 == 0x80)
            {
                close(iVar2);
                uVar3 = -1;
                if (local_90[0] == (byte)'.')
                {
                    // local_11 IS byte 127 of the record — the read filled it. The XOR accumulates
                    // into it and nothing reads that byte of the buffer again, so keeping it as a
                    // separate local is exact.
                    local_11 = local_90[0x7f];
                    param_3_index = 0;
                    pbVar5 = 1;
                    do
                    {
                        bVar1 = local_90[pbVar5];
                        pbVar5 = pbVar5 + 1;
                        param_3[param_3_index] = bVar1;
                        local_11 = (byte)(bVar1 ^ local_11);
                        param_3_index = param_3_index + 1;
                    }
                    while (pbVar5 < 0x41);

                    uVar3 = 0x80;
                    if (local_11 != 0)
                    {
                        uVar3 = -1;
                    }
                }
            }
            else
            {
                close(iVar2);
                uVar3 = 0;
            }
        }

        return uVar3;
    }

    // GHIDRA: RunSaveLoadFlow @ 0x80021618
    // Seven hundred bytes — THE BOOT-TIME SAVE LOAD, and what main calls it for. main only reaches it
    // when the probe returned 0, and it arms g_CurrentMenuState = 0xFFFF first, which is the flag this
    // function reads as "there is no menu to fall back to, just report".
    //
    // IT IS A STATE MACHINE OVER g_CardOperationState, run to completion inside its own blocking do/while:
    //   state 0     -> 7, reset the pass counter
    //   state 1     -> 7 when the probe said 0 or 4, otherwise -> 2 (the "wrong card" screen)
    //   state 7     file exists -> 0xF; no file and g_CurrentMenuState == -1 -> RETURN 0; otherwise -> 8
    //   state 0xF   pass 0 reads the record and copies it; pass 1 -> RETURN 1
    //   state 2/8/0x10  the message screens: poll the pad, service CD-DA, VSync, until O is pressed
    //   -> RETURN 2
    //
    // THE THREE RETURN VALUES, and what main does with them: 0 -> main sets g_CardProbeResult = 2;
    // 1 and 2 -> main leaves g_CardProbeResult at the probe's own result.
    //
    // WHICH PATH THIS PORT TAKES, and why it is correct rather than a defect: with no card image on
    // disk there is no "BISLPS-00355DRAGON" file, so state 7's firstfile finds nothing,
    // IsSaveFileMissing returns 1, g_CurrentMenuState is the 0xFFFF main just wrote, and this returns 0 on its
    // second loop pass without ever reaching ReadSaveRecord. main then sets g_CardProbeResult = 2 and
    // 0x801FF018 keeps the zeros start's .bss clear left, so bit 1 is clear and main redirects menu
    // item 2 to state 3. That IS the console's no-save behaviour on a blank card.
    // A save file placed in the backend's card folder takes the other branch: state 0xF, one
    // ReadSaveRecord, and the 64-byte record lands at 0x801FF018.
    internal static int RunSaveLoadFlow()
    {
        uint uVar7;
        uint uVar8;
        uint uVar9;
        bool bVar4;
        short sVar5;
        int iVar6;
        int puVar10;
        int puVar11;
        int uVar12;
        byte[] local_98 = new byte[0x80];

        bVar4 = true;
        uVar12 = 0;
        DAT_80055a80 = 1;
        sVar5 = 0;

        // `DAT_80055a78._0_2_ = QueryCardStatus();` — a halfword store into the low half of an
        // undefined4, and the 0 is the traced leaked a0 (see QueryCardStatus's own remarks).
        DAT_80055a78 = (DAT_80055a78 & unchecked((int)0xffff0000)) | (ushort)QueryCardStatus(0);
        if ((short)DAT_80055a78 == 2)
        {
            g_CardReprobeRequest = 1;
            g_CardOperationState = 1;
        }

        do
        {
            if (g_CardReprobeRequest == 1)
            {
                g_CardReprobeRequest = 0;
                sVar5 = (short)ProbeMemoryCard(0);
            }

            // Ghidra's `if (false) goto switchD_800216b0_caseD_3;` sits here and is dead.
            switch (g_CardOperationState)
            {
                case 0:
                    g_CardOperationState = 7;
                    DAT_80055a8c = 0;
                    break;
                case 1:
                    DAT_80055a8c = 0;
                    if ((sVar5 == 0) || (sVar5 == 4))
                    {
                        g_CardOperationState = 7;
                    }
                    else
                    {
                        g_CardOperationState = 2;
                        ShowCardMessage(2);
                    }

                    break;
                case 2:
                case 8:
                case 0x10:
                    // The original falls out of this arm into `default: break;`. C# forbids the
                    // fall-through spelling; the effect is identical.
                    uVar7 = PadInput.FUN_80026208(3);
                    if ((uVar7 & 0x40) != 0)
                    {
                        g_CardOperationState = 0;
                        uVar12 = 2;
                        bVar4 = false;
                    }

                    CdAudio.UpdateCdAudio();
                    VSync(0);
                    break;
                case 7:
                    iVar6 = IsSaveFileMissing(0);
                    if (iVar6 == 0)
                    {
                        g_CardOperationState = 0xf;
                    }
                    else
                    {
                        if ((short)SELECT_EXE_exe.g_CurrentMenuState == -1)
                        {
                            g_CardOperationState = 0;
                            uVar12 = 0;
                            bVar4 = false;

                            // `goto switchD_800216b0_caseD_3;` — the label IS the default arm's
                            // break, so leaving the switch here is the same jump.
                            break;
                        }

                        g_CardOperationState = 8;
                        ShowCardMessage(3);
                    }

                    break;
                case 0xf:
                    bVar4 = true;
                    if (DAT_80055a8c == 0)
                    {
                        iVar6 = ReadSaveRecord(0, 0, local_98);
                        if (iVar6 == 0x80)
                        {
                            // FOUR PASSES OF FOUR WORDS = 64 BYTES, from the stack record to
                            // 0x801FF018. The bound is the original's own `puVar10 != local_98 + 0x10`
                            // on a uint * — sixteen words past the base, i.e. offset 0x40.
                            // Ghidra renders this as `if (true) { ... } else { ... }` around an
                            // unaligned twin of the same copy; the else arm is unreachable and is not
                            // transliterated.
                            puVar10 = 0;
                            puVar11 = g_OptionsRecord64_Address;
                            do
                            {
                                uVar7 = MipsMemory.ReadU32(local_98, puVar10 + 4);
                                uVar8 = MipsMemory.ReadU32(local_98, puVar10 + 8);
                                uVar9 = MipsMemory.ReadU32(local_98, puVar10 + 0xc);
                                PsxRam.WriteI32(puVar11, MipsMemory.ReadI32(local_98, puVar10));
                                PsxRam.WriteI32(puVar11 + 4, (int)uVar7);
                                PsxRam.WriteI32(puVar11 + 8, (int)uVar8);
                                PsxRam.WriteI32(puVar11 + 0xc, (int)uVar9);
                                puVar10 = puVar10 + 0x10;
                                puVar11 = puVar11 + 0x10;
                            }
                            while (puVar10 != 0x40);
                        }
                        else
                        {
                            g_CardOperationState = 0x10;
                            ShowCardMessage(3);
                        }
                    }
                    else if (DAT_80055a8c == 1)
                    {
                        g_CardOperationState = 0;
                        uVar12 = 1;
                        bVar4 = false;
                    }

                    DAT_80055a8c = (ushort)(DAT_80055a8c + 1);
                    break;
                default:
                    break;
            }

            if (!bVar4)
            {
                return uVar12;
            }
        }
        while (true);
    }

    // FUN_80026208 @ 0x80026208 used to stand here as a BLOCKED stub returning 0. It is now
    // transliterated in PadInput.cs together with the bring-up InitializeBiosPad @ 0x800261A4 it depends
    // on, which is the module it actually belongs to; the call above goes there. The reason the
    // stub gave for blocking — that InitPAD / StartPAD never filled the two BIOS buffers — no longer
    // holds: LibApi.RefreshBiosPadBuffers publishes the real backend into them every V-BLANK.
    // CONSEQUENCE FOR RunSaveLoadFlow: bit 0x40 is now reachable, so its states 2, 8 and 0x10 can leave
    // through the Cross arm. None of the three is reachable on this port's boot path anyway — see
    // that function's own remarks.

    // GHIDRA: ShowCardMessage @ 0x80027A58
    internal static void ShowCardMessage(int param_1)
    {
        // BLOCKED: the memory-card message overlay, 2376 bytes / 288 lines, ending at 0x8002839F
        // immediately before the mode menu RunModeMenu. It saves the 320x240 frame to VRAM at
        // (0x280, 0) with StoreImage, MoveImage's it back, builds five transient GsSPRITE overlays on
        // tpage 0x1F at cx = 0x170 / cy = 0x1F6 out of GsSPRITE_ARRAY_800654ec, and drives the frame
        // step DrawFrame four times inline. Eleven call sites across the four card screens; its
        // argument selects which message.
        // Not this module and not this slice: it is a screen body, it owns the boxfill element
        // FrameStep.cs already documents as "only ever written by ShowCardMessage", and it needs the
        // sprite/VRAM work the screen-body slice owns.
        _ = param_1;
    }
}
