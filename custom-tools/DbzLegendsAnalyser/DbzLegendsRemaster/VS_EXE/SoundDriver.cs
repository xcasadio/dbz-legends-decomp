using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's sound driver module, 0x8005EE5C..0x800602DB. This file holds the one function of it
// that the battle scene depends on; the rest of the module is a slice of its own, as
// AnimCmdSound.cs already records.
//
// EVIDENCE, and its limit. Ghidra was unreachable, so this was transliterated from the running
// image through PCSX-Redux, gp = 0x8008D0FC. The channel was checked first: FUN_80061ed8 @
// 0x80061ED8 disassembles to exactly the 68-byte CdSearchFile retry loop this port documents. The
// emulator was halted on the exception vector with the workspace still zeroed, so this is STATIC
// evidence -- the instructions -- and no live value was observed.
internal static class SoundDriver
{
    // THE THREE LIBSND NAMES BELOW ARE INFERENCE, NOT A SYMBOL READ, and that distinction is worth
    // keeping visible. There is no symbol table for VS.EXE here; what closes them is the argument
    // shape at the call sites matching the PSY-Q prototypes exactly, in a module that is manifestly
    // the sound driver:
    //   0x8006DEA8(0x801C4000, -1, 0x0005E000)  -> SsVabOpenHeadSticky(vh addr, vabid, spu addr)
    //   0x8006EA04(0x801D2000, handle)          -> SsVabTransBody(vb addr, vabid)
    //   0x80071860(0)                           -> SsVabTransCompleted(immediateFlag)
    // The same three names appear in SELECT_EXE/SoundTestScreen.cs's own BLOCKED list, derived
    // independently, which is corroboration rather than proof. If a symbol table ever lands and
    // disagrees, this comment is what to check against.
    //
    // A CONSEQUENCE WORTH STATING BEFORE ANYONE EXPECTS A PICTURE FROM THIS: all three are
    // unimplemented stubs in PsxSdkMonogame.LibSnd, each `return default`, i.e. 0. Trace it through
    // and this machine reaches state 7 and stays there -- SsVabTransCompleted returning 0 is
    // exactly state 7's "not yet" condition, forever. So porting this function does NOT by itself
    // make the battle scene draw. The remaining blocker is libsnd, not this state machine.

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
                        FUN_80072a64((short)PsxRam.ReadU16(ws + SoundState.VoiceHandles + slot));
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
                    FUN_80072a64((short)PsxRam.ReadU16(ws + SoundState.PendingVabHandle));
                    PsxRam.WriteU16(ws + SoundState.PendingVabHandle, 0xFFFF);
                }

                // 0x8005F818..0x8005F868. The store of 2 into +0xDC is in the delay slot of the
                // call, so it is NOT that call's result.
                PsxRam.WriteI32(ws + SoundState.LoadRequestKind, 2);
                ArmTableRead(ws, FUN_80073894(ws + SoundState.ChseBankSlot), s2);
                s0 = ArmCountdownAndReturn(ws, s0);
                break;

            // ---- state 2 @ 0x8005F870 -- poll, then read the block into 0x801C4000.
            case 2:
                ws = SoundState.DAT_8008d284;
                s0 = PollThenRead(ws, s0, unchecked((int)0x801C4000), 3);
                break;

            // ---- state 3 @ 0x8005F8F4 -- second poll, arms the second table read (+2 header skip).
            case 3:
                ws = SoundState.DAT_8008d284;
                {
                    int r = FUN_800736f0(1);
                    if (r == 0)
                    {
                        s0 = 4;
                        PsxRam.WriteI32(ws + SoundState.LoadRequestKind, 0x3C);
                        ArmTableRead(ws, FUN_80073894(ws + SoundState.ChseBankSlot) + 2, s2);
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
                s0 = PollThenRead(ws, s0, unchecked((int)0x801D2000), 5);
                break;

            // ---- state 5 @ 0x8005FA34 -- open the VAB head, then transfer its body.
            case 5:
                ws = SoundState.DAT_8008d284;
                {
                    int r = FUN_800736f0(1);
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
                            short body = LibSnd.SsVabTransBody(unchecked((int)0x801D2000), opened);
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
                        unchecked((int)0x801D2000),
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

    // JUSTIFICATION: C# language bridge only
    // RELATION: the tail shared by states 1, 2, 3, 4 and 5 at 0x8005FB10 -- re-arm the countdown,
    // then fall into the common exit. Extracted because five cases jump to the same two
    // instructions; it aggregates nothing and changes no control flow.
    private static int ArmCountdownAndReturn(int ws, int state)
    {
        PsxRam.WriteU16(ws + SoundState.RetryCountdown, 0x0A);
        return state;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the table-read arming shared by states 1 and 3 (0x8005F82C..0x8005F868 and
    // 0x8005F924..0x8005F964). The index arithmetic is the original's: (id - 1) * 62, built there
    // as (v1 << 5) - v1 then doubled.
    private static void ArmTableRead(int ws, int tableBase, int id)
    {
        int entry = tableBase + (((short)id) - 1) * 62;
        FUN_80073790(entry, ws + SoundState.LoadRequestScratch);
        FUN_8007328c(2, ws + SoundState.LoadRequestScratch);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: states 2 and 4 are byte-identical but for the destination address and the next
    // state (0x8005F870 and 0x8005F9B0). One body, two call sites, no behaviour merged.
    private static int PollThenRead(int ws, int state, int destination, int nextState)
    {
        int r = FUN_80073204(1);
        if (r == 2)
        {
            FUN_80073710(PsxRam.ReadI32(ws + SoundState.LoadRequestKind), destination, 0x80);
            return ArmCountdownAndReturn(ws, nextState);
        }

        if (r != 5)
        {
            return state;
        }

        // r == 5: count down, and on expiry re-issue without changing state.
        return CountdownOr(ws, state, state);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the decrement-and-maybe-reissue block states 2, 3, 4 and 5 share. On expiry it
    // re-issues FUN_8007328c and re-arms the countdown; `onExpiry` is the state each case regresses
    // to, which is its own state for 2 and 4, one back for 3 and 5.
    private static int CountdownOr(int ws, int state, int onExpiry)
    {
        ushort left = (ushort)(PsxRam.ReadU16(ws + SoundState.RetryCountdown) - 1);
        PsxRam.WriteU16(ws + SoundState.RetryCountdown, left);
        if (left != 0)
        {
            return state;
        }

        FUN_8007328c(2, ws + SoundState.LoadRequestScratch);
        return ArmCountdownAndReturn(ws, onExpiry);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the hard-reset countdown at 0x8005FB20..0x8005FB52, reached from state 6's failure
    // and jumped into directly by state 5's. On expiry the machine goes back to state 0.
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

    // ==== Callees, all still outside this slice ================================================
    // BLOCKED, every one of them. They live in the 0x8007xxxx band, which in VS.EXE is the PSX SDK
    // region -- CdSearchFile is at 0x80075994 there, proven by WaitSearchFile's own `jal`. Naming
    // them from their argument shapes alone would be a guess, so they keep their raw addresses.

    // GHIDRA: FUN_80072a64 @ 0x80072A64 (VS.EXE)
    // BLOCKED: takes a voice handle and releases it. Called seven times from state 1.
    private static void FUN_80072a64(int param_1) => _ = param_1;

    // GHIDRA: FUN_80073894 @ 0x80073894 (VS.EXE)
    // BLOCKED: given a bank slot, returns the base of a table the (id - 1) * 62 index walks.
    private static int FUN_80073894(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_80073790 @ 0x80073790 (VS.EXE)
    // BLOCKED: hands a table entry to the load request block.
    private static void FUN_80073790(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_8007328c @ 0x8007328C (VS.EXE)
    // BLOCKED: issues the request. The original takes a third argument, a stack local this port has
    // nothing to put in; it is omitted rather than invented, and that is why this is BLOCKED.
    private static void FUN_8007328c(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_80073204 @ 0x80073204 (VS.EXE)
    // BLOCKED: polls the request. 2 means ready, 5 means retry, anything else means keep waiting.
    private static int FUN_80073204(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_800736f0 @ 0x800736F0 (VS.EXE)
    // BLOCKED: the other poll. 0 means done, -1 means retry.
    private static int FUN_800736f0(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_80073710 @ 0x80073710 (VS.EXE)
    // BLOCKED: reads the block to a destination address.
    private static void FUN_80073710(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }
}
