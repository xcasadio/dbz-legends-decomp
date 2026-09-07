using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's sound driver module, 0x8005EE5C..0x800602DB. This file holds the one function of it
// that the battle scene depends on; the rest of the module is a slice of its own, as
// AnimCmdSound.cs already records.
//
// EVIDENCE, in the order it arrived. This was FIRST transliterated from the running image through
// PCSX-Redux, gp = 0x8008D0FC, because Ghidra was unreachable at the time; that channel was checked
// before being trusted (FUN_80061ed8 @ 0x80061ED8 disassembles to exactly the 68-byte CdSearchFile
// retry loop this port documents) and the emulator was halted with the workspace zeroed, so it gave
// STATIC evidence -- the instructions -- and no live value.
//
// Ghidra came back afterwards and the port was re-checked against it. That second pass is what the
// class comment below records: the control flow held, the callee bindings did not. Both channels
// are named here because the difference between them is the point -- raw disassembly proved the
// SHAPE of this function, and only the symbol table could prove what it was TALKING TO.
internal static class SoundDriver
{
    // CORRECTED AGAINST GHIDRA. This function was first transliterated from raw disassembly with
    // Ghidra unreachable, and ten of its callees were left as unnamed FUN_ addresses with guessed
    // roles. Ghidra came back and named every one of them; the guesses were wrong in ways worth
    // recording, because they show what raw disassembly cannot tell you:
    //
    //   0x80073894 was "returns the base of a table"     -> CdPosToInt(CdlLOC *)
    //   0x80073790 was "hands a table entry to the block"-> CdIntToPos(int, CdlLOC *)
    //   0x8007328c was "issues the request", 2 arguments -> CdControl(com, param, result), THREE
    //   0x80073204 was "polls; 2 ready, 5 retry"         -> CdSync -- 2 is CdlComplete, 5 CdlDiskError
    //   0x800736f0 was "the other poll; 0 done, -1 retry"-> CdReadSync
    //   0x80073710 was "reads the block"                 -> CdRead(sectors, buf, mode)
    //   0x80072a64 was "releases a handle"               -> SsVabClose(short)
    //
    // The control flow survived that correction unchanged -- eight cases, the regressions, the
    // shared countdown -- but the bindings did not, and one of them was a real defect: CdControl's
    // result buffer had been dropped as "a stack local this port has nothing to put in". It is the
    // eight-byte response block every other CdControl call site in this port already passes.
    //
    // The three libsnd names were guessed too, from argument shape, and those three turned out
    // right: Ghidra confirms SsVabOpenHeadSticky(uchar*, short, ulong), SsVabTransBody(uchar*,
    // short) and SsVabTransCompleted(short) at the addresses called here. Being right by luck is
    // not the same as being right by evidence, which is why they were labelled as inference until
    // this check.
    //
    // SO THIS IS A CD LOAD, not an opaque request machine: seek with CdlSetloc, wait on CdSync,
    // CdRead into a buffer, wait on CdReadSync, twice over, then open the VAB head and transfer
    // its body. It reads the CHSE bank's CdlFILE at workspace+0xF0 and indexes it by (id - 1) * 62.
    //
    // WHAT STILL DOES NOT DRAW. The libcd half is real in PsxSdkMonogame, so the machine now walks
    // 0 -> 1 -> ... -> 7 for real. State 7 is where it stops: SsVabTransCompleted is a
    // `return default` stub in LibSnd, and 0 is exactly state 7's "not yet". The remaining blocker
    // is libsnd.

    // GHIDRA: FUN_8005f704 @ 0x8005F704 (VS.EXE)
    // CERTAIN as to control flow: the whole body was read instruction by instruction,
    // 0x8005F704..0x8005FB98, exactly 0x490 = 1168 bytes, and the eight-entry jump table at
    // 0x80020A84 was read as raw bytes and decodes to
    //   {0x8005F754, 0x8005F784, 0x8005F870, 0x8005F8F4, 0x8005F9B0, 0x8005FA34, 0x8005FAEC, 0x8005FB54}
    //
    // WHAT IT IS. The CD-load step machine of the sound driver. The caller passes the state back in
    // on every call and runs it until it returns 8 or more -- BattleScene @ 0x800356DC says exactly
    // that in its own comment. THE STATE IS THE RETURN VALUE: there is no separate error code. Out
    // of range (>= 8) fails the `sltiu a1,8` at 0x8005F720 and falls straight to the exit, echoing
    // the state back, which is what makes 8 terminal and sticky.
    //
    // It returns 0 in exactly two circumstances, both of which mean "start over":
    //   state 0 while the workspace gate at +0x12A is non-zero -- nothing has happened yet;
    //   the shared failure countdown at +0x13C reaching 0 in state 6 (or state 5 falling into it).
    //
    // THE SIGNATURE IS KEPT AS IT WAS FOUND, `ushort` return, because three call sites already cast
    // it. The original returns v0 sign-extended from 16 bits (`sll`/`sra` at 0x8005FB74), which for
    // the 0..8 range these states occupy is the same value either way.
    internal static ushort FUN_8005f704(int param_1, int param_2)
    {
        int s2 = param_1;
        int s0 = param_2;

        // 0x8005F718..0x8005F720: the dispatch index is a1 sign-extended from 16 bits, and the
        // range test is UNSIGNED, so a negative state also falls through to the exit.
        int index = (short)param_2;
        if ((uint)index >= 8)
        {
            return (ushort)(short)s0;
        }

        int ws;

        switch (index)
        {
            // ---- state 0 @ 0x8005F754 -- wait on the gate, then clear two bits and advance.
            case 0:
                ws = SoundState.DAT_8008d284;
                if ((short)PsxRam.ReadU16(ws + SoundState.Gate12A) != 0)
                {
                    return (ushort)(short)s0;
                }

                // 0x8005F76C..0x8005F778: gp+0x244 &= ~0xC. BattleScene owns that word.
                BattleScene.DAT_8008d340 &= unchecked((uint)~0xC);
                s0 = 1;
                break;

            // ---- state 1 @ 0x8005F784 -- release every voice, then arm the first table read.
            case 1:
                ws = SoundState.DAT_8008d284;

                // 0x8005F78C..0x8005F7E8: six slots, i = 0..5 inclusive (`slti v0,6` @0x8005F7E0).
                for (int i = 0; i < SoundState.VoiceSlotCount; i++)
                {
                    int slot = i * SoundState.VoiceSlotStride;
                    if ((short)PsxRam.ReadU16(ws + SoundState.VoiceHandles + slot) >= 0)
                    {
                        LibSnd.SsVabClose((short)PsxRam.ReadU16(ws + SoundState.VoiceHandles + slot));
                        PsxRam.WriteU16(ws + SoundState.VoiceHandles + slot, 0xFFFF);
                        PsxRam.WriteU16(
                            ws + SoundState.VoiceFlags + slot,
                            (ushort)(PsxRam.ReadU16(ws + SoundState.VoiceFlags + slot) | 0x80));
                    }
                }

                // 0x8005F7EC..0x8005F818. `li s0,2` sits in the branch delay slot at 0x8005F800, so
                // the state advances whether or not the handle was live -- reproduced, not tidied.
                s0 = 2;
                if ((short)PsxRam.ReadU16(ws + SoundState.PendingVabHandle) >= 0)
                {
                    LibSnd.SsVabClose((short)PsxRam.ReadU16(ws + SoundState.PendingVabHandle));
                    PsxRam.WriteU16(ws + SoundState.PendingVabHandle, 0xFFFF);
                }

                // 0x8005F818..0x8005F868. The store of 2 into +0xDC is in the delay slot of the
                // call, so it is NOT that call's result.
                PsxRam.WriteI32(ws + SoundState.SectorCount, 2);
                SeekToEntry(ws, 0, s2);
                s0 = ArmCountdownAndReturn(ws, s0);
                break;

            // ---- state 2 @ 0x8005F870 -- poll, then read the block into 0x801C4000.
            case 2:
                ws = SoundState.DAT_8008d284;
                s0 = SyncThenRead(ws, s0, unchecked((int)0x801C4000), 3);
                break;

            // ---- state 3 @ 0x8005F8F4 -- second poll, arms the second table read (+2 header skip).
            case 3:
                ws = SoundState.DAT_8008d284;
                {
                    int r = LibCd.CdReadSync(1, s_result);
                    if (r == 0)
                    {
                        s0 = 4;
                        PsxRam.WriteI32(ws + SoundState.SectorCount, 0x3C);
                        SeekToEntry(ws, 2, s2);
                        s0 = ArmCountdownAndReturn(ws, s0);
                    }
                    else if (r == -1)
                    {
                        // 0x8005F974..0x8005F9AC: on the countdown expiring this REGRESSES to 2.
                        s0 = CountdownOr(ws, s0, 2);
                    }
                }

                break;

            // ---- state 4 @ 0x8005F9B0 -- structurally identical to state 2, other buffer.
            case 4:
                ws = SoundState.DAT_8008d284;
                s0 = SyncThenRead(ws, s0, FileIo.g_cdFileBufferTableAddress, 5);
                break;

            // ---- state 5 @ 0x8005FA34 -- open the VAB head, then transfer its body.
            case 5:
                ws = SoundState.DAT_8008d284;
                {
                    int r = LibCd.CdReadSync(1, s_result);
                    if (r == 0)
                    {
                        short opened = LibSnd.SsVabOpenHeadSticky(
                            unchecked((int)0x801C4000), -1, 0x0005E000);
                        PsxRam.WriteU16(ws + SoundState.PendingVabHandle, (ushort)opened);

                        if (opened < 0)
                        {
                            // 0x8005FA78 jumps INTO state 6's countdown, with the state still 5.
                            s0 = FailureCountdown(ws, s0);
                        }
                        else
                        {
                            short body = LibSnd.SsVabTransBody(FileIo.g_cdFileBufferTableAddress, opened);
                            s0 = 6;

                            // 0x8005FA9C: the countdown is re-armed in the delay slot, so it
                            // happens on both paths.
                            PsxRam.WriteU16(ws + SoundState.RetryCountdown, 0x0A);
                            if (body >= 0)
                            {
                                s0 = 7;
                            }
                        }
                    }
                    else if (r == -1)
                    {
                        // 0x8005FAB0..0x8005FAE8: on expiry this REGRESSES to 4.
                        s0 = CountdownOr(ws, s0, 4);
                    }
                }

                break;

            // ---- state 6 @ 0x8005FAEC -- retry the body transfer until it takes.
            case 6:
                ws = SoundState.DAT_8008d284;
                {
                    short body = LibSnd.SsVabTransBody(
                        FileIo.g_cdFileBufferTableAddress,
                        (short)PsxRam.ReadU16(ws + SoundState.PendingVabHandle));
                    if (body >= 0)
                    {
                        s0 = 7;
                        PsxRam.WriteU16(ws + SoundState.RetryCountdown, 0x0A);
                    }
                    else
                    {
                        s0 = FailureCountdown(ws, s0);
                    }
                }

                break;

            // ---- state 7 @ 0x8005FB54 -- wait for the transfer to complete, then latch and finish.
            case 7:
                if (LibSnd.SsVabTransCompleted(0) == 0)
                {
                    return (ushort)(short)s0;
                }

                ws = SoundState.DAT_8008d284;
                s0 = 8;
                PsxRam.WriteU16(ws + SoundState.CompletedRequestId, (ushort)s2);
                break;
        }

        return (ushort)(short)s0;
    }

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: the original's `u_char auStack_20[8]`, the eight-byte response block every
    // CdControl / CdSync / CdReadSync in this function shares. It is a stack local on the console;
    // here it is a field because C# cannot take the address of a local, and the calls only ever
    // pass it through. Dropping it -- which the first version of this file did, calling CdControl
    // with two arguments -- silently removed the buffer the drive writes its status into.
    private static readonly byte[] s_result = new byte[8];

    // JUSTIFICATION: C# language bridge only
    // RELATION: scratch for the position the workspace holds as bytes. One instance, reused, the
    // way the original reuses one stack slot.
    private static readonly LibCd.CdlLOC s_loc = new();

    // JUSTIFICATION: C# language bridge only
    // RELATION: the tail shared by states 1, 2, 3, 4 and 5 at 0x8005FB10 -- re-arm the countdown,
    // then fall into the common exit. Extracted because five cases jump to the same instruction.
    private static int ArmCountdownAndReturn(int ws, int state)
    {
        PsxRam.WriteU16(ws + SoundState.RetryCountdown, 0x0A);
        return state;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the seek that states 1 and 3 share (0x8005F818..0x8005F868 and
    // 0x8005F910..0x8005F964). Both read the CHSE bank's CdlFILE position, index it by
    // (id - 1) * 62, convert back to a CdlLOC in the workspace and issue CdlSetloc. State 3 adds a
    // two-sector header skip, which is the only difference and is the parameter.
    private static void SeekToEntry(int ws, int headerSkip, int id)
    {
        LoadLoc(ws + SoundState.ChseBankSlot, s_loc);
        int lba = LibCd.CdPosToInt(s_loc);
        LibCd.CdIntToPos(lba + headerSkip + (((short)id) - 1) * 0x3E, s_loc);

        // The original's CdIntToPos writes THROUGH the pointer into the workspace, so the position
        // it just computed is left at +0xD8 for the retry path to reissue. Mirrored here rather
        // than kept only in the C# object, or a retry would seek to whatever was there before.
        StoreLoc(ws + SoundState.SeekPosition, s_loc);
        LibCd.CdControl(2, s_loc, s_result);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the port models a CdlLOC as an object while the workspace holds it as four bytes of
    // PSX memory, which is what the original passes by pointer. These two move it across.
    private static void LoadLoc(int address, LibCd.CdlLOC loc)
    {
        loc.minute = PsxRam.ReadU8(address);
        loc.second = PsxRam.ReadU8(address + 1);
        loc.sector = PsxRam.ReadU8(address + 2);
        loc.track = PsxRam.ReadU8(address + 3);
    }

    private static void StoreLoc(int address, LibCd.CdlLOC loc)
    {
        PsxRam.WriteU8(address, loc.minute);
        PsxRam.WriteU8(address + 1, loc.second);
        PsxRam.WriteU8(address + 2, loc.sector);
        PsxRam.WriteU8(address + 3, loc.track);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: states 2 and 4 are identical but for the destination and the next state
    // (0x8005F870 and 0x8005F9B0). CdSync's 2 is CdlComplete and its 5 is CdlDiskError; anything
    // else means the seek is still running, and the state is returned untouched.
    private static int SyncThenRead(int ws, int state, int destination, int nextState)
    {
        int status = LibCd.CdSync(1, s_result);
        if (status == 2)
        {
            LibCd.CdRead(PsxRam.ReadI32(ws + SoundState.SectorCount), destination, 0x80);
            return ArmCountdownAndReturn(ws, nextState);
        }

        if (status != 5)
        {
            return state;
        }

        return CountdownOr(ws, state, state);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the decrement-and-maybe-reseek block states 2, 3, 4 and 5 share. The original tests
    // the value BEFORE the decrement against 1 (`if (sVar2 != 1)`), which is the same condition as
    // testing the decremented value against 0. On expiry it re-issues CdlSetloc and re-arms.
    // `onExpiry` is the state each case lands in: its own for 2 and 4, one back for 3 and 5.
    private static int CountdownOr(int ws, int state, int onExpiry)
    {
        ushort left = (ushort)(PsxRam.ReadU16(ws + SoundState.RetryCountdown) - 1);
        PsxRam.WriteU16(ws + SoundState.RetryCountdown, left);
        if (left != 0)
        {
            return state;
        }

        // The reissue reads the position back out of the workspace, exactly as the original does:
        // `CdControl(2, (u_char *)(iVar5 + 0xd8), auStack_20)`.
        LoadLoc(ws + SoundState.SeekPosition, s_loc);
        LibCd.CdControl(2, s_loc, s_result);
        return ArmCountdownAndReturn(ws, onExpiry);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the hard-reset countdown at 0x8005FB28, reached from state 6's failed transfer and
    // jumped into directly by state 5's failed open. On expiry the machine returns to state 0.
    private static int FailureCountdown(int ws, int state)
    {
        ushort left = (ushort)(PsxRam.ReadU16(ws + SoundState.RetryCountdown) - 1);
        PsxRam.WriteU16(ws + SoundState.RetryCountdown, left);
        if (left != 0)
        {
            return state;
        }

        PsxRam.WriteU16(ws + SoundState.RetryCountdown, 0x0A);
        return 0;
    }
}
