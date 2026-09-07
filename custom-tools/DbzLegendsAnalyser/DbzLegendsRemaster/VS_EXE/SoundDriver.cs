using System;
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

    // GHIDRA: SoundCdLoadStep @ 0x8005F704 (VS.EXE)
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
    internal static ushort SoundCdLoadStep(int param_1, int param_2)
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

    // ==========================================================================================
    // THE SOUND-DRIVER FAMILY, 0x8005D1FC..0x8005F703
    // ==========================================================================================
    //
    // Seven functions, transliterated from Ghidra's decompilation of /VS.EXE read IN FULL (254,
    // 243, 372, 122, 16, 22 and 28 lines respectively; every one was paged to its `totalLines`).
    //
    // WHAT THIS FAMILY IS. FUN_8005d1fc is the task callback VS_EXE_exe.cs creates as task id 0x57
    // on list 0x14 with an 0x194-byte context. It is a two-way switch on the workspace's own state
    // halfword at +0x10E: 0 runs the one-shot init FUN_8005d25c, 1 runs the per-frame service
    // FUN_8005da78. The init's last statement increments +0x10E, which is how the task moves from
    // one to the other and never goes back.
    //
    // FUN_8005da78 in turn drives FUN_8005e1ec (the BGM sequence machine, 22 states over the CD and
    // libsnd sequence calls) and FUN_8005f660 (the voice-flag sweep), which drives FUN_8005f068
    // (the per-voice ATB bank loader, 7 states). FUN_8005f480 is a separate entry, called once from
    // 0x80055BFC, that keys a loaded voice on.
    //
    // RULE 13 WAS APPLIED LITERALLY. Every libspu / libsnd / libcd / libetc entry below is CALLED
    // through PsxSdkMonogame -- LibSnd, LibSpu, LibCd, LibEtc, Kernel -- and none is re-ported. The
    // overlay's own SDK region begins at 0x800632C4, and the five unnamed FUN_ callees at
    // 0x8006B4A0, 0x8006B88C, 0x8006BCD0, 0x8006BDD8 and 0x8006DE64 are all above it: they are
    // libsnd's voice allocator and its volume/pan setters. They are NOT re-transliterated here and
    // they are NOT in PsxSdkMonogame under any name either, so each stands below as a BLOCKED stub
    // whose comment carries its address, its byte size and what it is called with. That is the one
    // honest option: porting them would break rule 13, and inventing a libsnd entry for them would
    // invent semantics.
    //
    // WHAT IS AUDIBLE AFTER THIS SLICE: still nothing. Every libsnd body in PsxSdkMonogame is
    // "Do nothing PSX SDK", so SsVabOpenHeadSticky answers 0, SsVabTransBody answers 0 and
    // SsSeqPlay does nothing. What the slice DOES buy is that SoundState.DAT_8008d284 finally gets
    // written, the five \SOUND\*.B CdlFILE slots are finally searched and filled, and the workspace
    // fields SoundCdLoadStep and FighterSubstitution already read are finally initialised.

    // ---- The overlay data tables this family indexes. All three are INITIALISED DATA inside the
    // VS.EXE image, so they resolve through PsxExeImage and are read with PsxRam rather than being
    // copied into C# arrays. Contents below were read out of the image with read-memory; they are
    // recorded because the INDEX SCALING is what a wrong reading gets wrong, not the address.

    // GHIDRA: PTR_DAT_8008481c @ 0x8008481C (VS.EXE)
    // An array of WORDS, indexed "(&PTR_DAT_8008481c)[n]" i.e. n * 4 bytes. Its first eight entries
    // are POINTERS -- 0x801B9000, 0x801B9974, 0x801BA204, 0x801BAE68, 0x801BBB58, 0x801BC018,
    // 0x801BCCAC, 0x801BD478, the eight sequences inside the SEQ buffer this file loads at
    // 0x801B9000 -- and the entries FROM INDEX 8 ON are LBA OFFSETS: 0x000000B0, 0x000000B6,
    // 0x000000BB, 0x000000C1, ... FUN_8005d25c and FUN_8005e1ec's case 0xC add them to
    // CdPosToInt of the BGM bank; FUN_8005e1ec's case 0x10 takes the first eight as pointers and
    // hands them to SsSeqOpen. The SAME array is read both ways, and the index decides which --
    // "if (iVar30 < 8)" at 0x8005E6C8 is the original's own test, not an interpretation here.
    private const int PtrDat8008481cAddress = unchecked((int)0x8008481C);

    // GHIDRA: DAT_80084bc0 @ 0x80084BC0 (VS.EXE)
    // A table of HALFWORDS, read as "*(ushort *)(&DAT_80084bc0 + iVar8)" where iVar8 is the sound
    // id already doubled by the sll 0x10 / sra 0xf pair at 0x8005F080 -- so element n is at n * 2.
    // Image contents, sixteen entries: 0, 0, 0, 0, 0x0C, 0x0C, 0x18, 0x18, 0x24, 0x30, 0x30, 0x30,
    // 0x48, 0x3C, 0x6C, 0x78. They are sector offsets into \SOUND\ATB.B.
    private const int Dat80084bc0Address = unchecked((int)0x80084BC0);

    // GHIDRA: DAT_80084c50 @ 0x80084C50 (VS.EXE), and its neighbours up to DAT_80084c5e
    // BYTES, read one at a time as the (prog, tone, note) triple FUN_8006b4a0 is keyed on. Image
    // contents from 0x80084C50: 02 00 34 00 | 02 01 35 00 | 02 02 37 00 | 02 03 39 00. The three
    // triples FUN_8005da78 uses are therefore (2, 0, 0x34) at +0x00, (2, 1, 0x35) at +0x04 and
    // (2, 3, 0x39) at +0x0C.
    private const int Dat80084c50Address = unchecked((int)0x80084C50);
    private const int Dat80084c51Address = unchecked((int)0x80084C51);
    private const int Dat80084c52Address = unchecked((int)0x80084C52);
    private const int Dat80084c54Address = unchecked((int)0x80084C54);
    private const int Dat80084c55Address = unchecked((int)0x80084C55);
    private const int Dat80084c56Address = unchecked((int)0x80084C56);
    private const int Dat80084c5cAddress = unchecked((int)0x80084C5C);
    private const int Dat80084c5dAddress = unchecked((int)0x80084C5D);
    private const int Dat80084c5eAddress = unchecked((int)0x80084C5E);

    // GHIDRA: DAT_800c2e88 @ 0x800C2E88 (VS.EXE)
    // The sequence/VAB table SsSetTableSize is handed, sized (4, 5) by the init. Its BYTES belong
    // to libsnd, which owns everything it does with them; only the address crosses the call.
    private const int Dat800c2e88Address = unchecked((int)0x800C2E88);

    // ---- The five fixed RAM buffers this family reads CD data into. NONE of them was declared
    // anywhere in the port before this file, so each is registered here with LibGpu.RamRegion.
    //
    // WHY THEY OVERLAP VS_EXE_exe.DAT_80110000 AND WHY THAT IS SAFE. That region was sized 0xB1000
    // -- 0x80110000..0x801C1000 -- on the reasoning that 0x801C1000 is "the next address VS.EXE
    // itself uses". It is not: 0x801B6000, 0x801B9000 and 0x801BE000 are inside it and are used by
    // this very function. LibGpu.RamResolve resolves an overlapped address to the region with the
    // HIGHEST BASE that covers it, so every access below lands on the buffer declared here and not
    // on the staging area. That is the mechanism the port already relies on elsewhere; it is
    // recorded rather than relied on silently, because the sizing comment in VS_EXE_exe.cs is now
    // known to be wrong about its own upper bound and the main session may want to narrow it.

    // GHIDRA: DAT_801b6000 @ 0x801B6000 (VS.EXE)
    // The BGM VAB HEADER. "CdRead(6, &DAT_801b6000, 0x80)" in the init and again in FUN_8005e1ec's
    // case 3, so six sectors: 6 * 0x800 = 0x3000, which is also exactly the distance to the next
    // buffer at 0x801B9000. Size closed twice over.
    private const int Dat801b6000Address = unchecked((int)0x801B6000);
    private static readonly byte[] DAT_801b6000 = LibGpu.RamRegion(Dat801b6000Address, 0x3000);

    // GHIDRA: DAT_801b9000 @ 0x801B9000 (VS.EXE)
    // The SEQ buffer: "CdRead(10, &DAT_801b9000, 0x80)" is ten sectors = 0x5000, and 0x801B9000 +
    // 0x5000 = 0x801BE000, the next buffer. The eight pointers at PTR_DAT_8008481c[0..7] all fall
    // inside this span (0x801B9000..0x801BD478), which is the independent confirmation.
    private const int Dat801b9000Address = unchecked((int)0x801B9000);
    private static readonly byte[] DAT_801b9000 = LibGpu.RamRegion(Dat801b9000Address, 0x5000);

    // GHIDRA: DAT_801be000 @ 0x801BE000 (VS.EXE)
    // The second SEQ buffer, "CdRead(6, &DAT_801be000, 0x80)" in the init and in FUN_8005e1ec's
    // case 0xE: six sectors = 0x3000, ending at 0x801C1000 where the ADPCM clip buffer starts.
    private const int Dat801be000Address = unchecked((int)0x801BE000);
    private static readonly byte[] DAT_801be000 = LibGpu.RamRegion(Dat801be000Address, 0x3000);

    // GHIDRA: DAT_801c1000 @ 0x801C1000 (VS.EXE)
    // The ADPCM clip buffer SoundState already names (Dat801c1000Address). Its BYTES were never
    // declared, so they are declared here. SIZE: SoundState records a live span of 0x1E00 from the
    // cursor test against 0x801C2E00, but FUN_8005da78's case 3/7/0xE reads "*(int *)(ws + 0x34)"
    // sectors into it and that field holds 4 in case 5's path -- 4 * 0x800 = 0x2000. The region is
    // therefore 0x2000, up to 0x801C3000 where the next buffer begins; 0x1E00 is how much of it the
    // player walks, not how much the loader writes.
    private static readonly byte[] DAT_801c1000 = LibGpu.RamRegion(SoundState.Dat801c1000Address, 0x2000);

    // GHIDRA: DAT_80110000 @ 0x80110000 (VS.EXE)
    // NOT DECLARED HERE. The CD staging area's bytes and its address both belong to VS_EXE_exe.cs,
    // whose own comment names FUN_8005D25C as one of the unported readers it was sized for -- and
    // the init below is the biggest of them, reading 0xA0 sectors = 0x50000 bytes of \SOUND\BGM.B
    // into it. The address const there is now `internal` and this file uses it, so there is one
    // spelling of 0x80110000 in the overlay rather than two.
    //
    // This file is also what forced that region to SHRINK: it declares buffers at 0x801B6000,
    // 0x801B9000 and 0x801BE000, all of which sat inside the 0xB1000 the staging area had claimed.

    // GHIDRA: DAT_801c1a22 @ 0x801C1A22 (VS.EXE)
    // A HALFWORD inside the buffer above (Ghidra: undefined2), at +0xA22. FUN_8005da78's
    // case 4/8/0xF loads it UNCONDITIONALLY -- the load sits above the branch that decides whether
    // to store it. Reproduced in that order below.
    private const int Dat801c1a22Address = unchecked((int)0x801C1A22);

    // GHIDRA: DAT_801c3000 @ 0x801C3000 (VS.EXE)
    // The ABTL VAB HEADER: "CdRead(*(int *)(ws + 0x34), &DAT_801c3000, 0x80)" with that field just
    // set to 2, so two sectors = 0x1000, ending at 0x801C4000.
    private const int Dat801c3000Address = unchecked((int)0x801C3000);
    private static readonly byte[] DAT_801c3000 = LibGpu.RamRegion(Dat801c3000Address, 0x1000);

    // GHIDRA: DAT_801c4000 @ 0x801C4000 (VS.EXE)
    // The per-voice ATB header buffers. FUN_8005f068 addresses them "&DAT_801c4000 + iVar6 * 0x1000"
    // for iVar6 = the voice slot 0..5, reading two sectors (0x1000) into each, so the whole array is
    // 6 * 0x1000 = 0x6000 and ends at 0x801CA000 -- clear of g_cdFileBufferTable at 0x801D2000.
    // SoundCdLoadStep above already read and wrote this address as a bare literal; it now has a name.
    private const int Dat801c4000Address = unchecked((int)0x801C4000);
    private static readonly byte[] DAT_801c4000 = LibGpu.RamRegion(Dat801c4000Address, 0x6000);

    // ---- gp-relative globals of the sound module that no other file declares. Every width below
    // is Ghidra's own data type at the address (get-data: "undefined2" for all seven), and the two
    // initialised ones carry the image's own value.

    // GHIDRA: DAT_8008d210 @ 0x8008D210 (VS.EXE)
    // The BGM/ATB voice cursor. Image value 0x11. SoundState declares the ADDRESS
    // (Dat8008d210Address) and nothing declares the STORAGE, so it is declared here. FUN_8005da78
    // walks it 0x11..0x14 and snaps back to 0x12 -- "if (0x14 < DAT_8008d210) DAT_8008d210 = 0x12".
    internal static short DAT_8008d210 = 0x11;

    // GHIDRA: DAT_8008d212 @ 0x8008D212 (VS.EXE)
    // The other voice cursor, image value 0x15, walked by FUN_8005f480 with the same snap-back
    // shape. NOTE THE ASYMMETRY, which is the original's and is reproduced rather than smoothed:
    // this one tests "0x16 <" and resets to 0x15, so it alternates over two voices, while
    // DAT_8008d210 tests "0x14 <" and resets to 0x12 and cycles over three.
    internal static short DAT_8008d212 = 0x15;

    // GHIDRA: DAT_8008d216 @ 0x8008D216 (VS.EXE)
    // FUN_8005e1ec case 5's consecutive-failure counter for SsVabOpenHeadSticky. Image value 0.
    // Cleared on success; on the 33rd consecutive failure ("0x20 <") the machine returns 0.
    internal static short DAT_8008d216;

    // GHIDRA: DAT_8008d270 @ 0x8008D270 (VS.EXE)
    // FUN_8005e1ec case 0xA's target VAB id, copied out of the workspace's +0x10A. .bss, undefined2.
    internal static short DAT_8008d270;

    // GHIDRA: DAT_8008d274 @ 0x8008D274 (VS.EXE)
    // SsVabTransBodyPartly's last answer, compared against DAT_8008d270 to decide whether the body
    // is finished. .bss, undefined2.
    internal static short DAT_8008d274;

    // GHIDRA: DAT_8008d3c0 @ 0x8008D3C0 (VS.EXE)
    // A .bss halfword cleared by FUN_8005da78's cases 0xB and 0xC. PARTIAL: only zero-stores are
    // visible from this family, so what it MEANS is not closed; its writers elsewhere in the
    // overlay are not transliterated.
    internal static short DAT_8008d3c0;

    // GHIDRA: DAT_8008d418 @ 0x8008D418 (VS.EXE)
    // A .bss halfword, cleared by case 0xC and TESTED by case 0xB to decide whether the workspace's
    // +0x13E / +0x140 pair is cleared with it. PARTIAL for the same reason as DAT_8008d3c0.
    internal static short DAT_8008d418;

    // GHIDRA: DAT_800b0ddc @ 0x800B0DDC (VS.EXE)
    // THE SpuVoiceAttr, and it is one structure, not fourteen globals. Ghidra labels each written
    // word separately because .bss carries no type, but the offsets are libspu's SpuVoiceAttr field
    // for field and the fit is exact:
    //     +0x00 DAT_800b0ddc = 0x800000  voice      (bit 23 -> voice 0x17, the streaming voice)
    //     +0x04 DAT_800b0de0 = 0xff93    mask
    //     +0x08 DAT_800b0de4 = 0x3fff    volume.left     +0x0A DAT_800b0de6 = 0x3fff volume.right
    //     +0x14 DAT_800b0df0 = 0x400     pitch
    //     +0x1C DAT_800b0df8 = <buffer>  addr
    //     +0x24 DAT_800b0e00 = 1  a_mode   +0x28 DAT_800b0e04 = 1  s_mode
    //     +0x2C DAT_800b0e08 = 3  r_mode
    //     +0x30 DAT_800b0e0c = 0 ar   +0x32 dr   +0x34 sr   +0x36 rr   +0x38 DAT_800b0e14 = 0xf sl
    // 0x800000 in the voice field and voice[0x17] in the SpuStEnv two statements earlier are the
    // same voice named two ways, which is what closes the reading.
    //
    // SoundState declares the ADDRESS (Dat800b0ddcAddress); the STORAGE is declared here, as the
    // libspu structure it is, because SpuSetVoiceAttr takes the object. The three SpuVolume members
    // are instantiated because C makes them inline members of the struct and C# does not.
    internal static readonly LibSpu.SpuVoiceAttr DAT_800b0ddc = new()
    {
        volume = new LibSpu.SpuVolume(),
        volmode = new LibSpu.SpuVolume(),
        volumex = new LibSpu.SpuVolume(),
    };

    // ---- The four addresses this family hands to the SDK as CALLBACKS. Ghidra has promoted none
    // of them to a function -- each is a bare LAB_ reached only by the instruction that pushes it --
    // so none is transliterated here. They are routed through TaskSystem's address-to-method table,
    // which is this port's existing answer for "the console holds a raw PSX address here": the
    // registration is real, the dispatch is real, and nothing happens until somebody registers a
    // body for the address.
    private const int Lab8005e88cAddress = unchecked((int)0x8005E88C);
    private const int Lab8005e954Address = unchecked((int)0x8005E954);
    private const int Lab8005eae8Address = unchecked((int)0x8005EAE8);
    private const int Lab8005eb14Address = unchecked((int)0x8005EB14);

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: the original's second stack response block, "u_char auStack_48[40]" in FUN_8005da78
    // and "auStack_40[40]" in FUN_8005e1ec. Same role as s_result above -- the drive's status block
    // -- and separate for the same reason the originals are separate stack slots. Forty bytes on
    // the console; only the first eight are ever written by libcd, and every call here passes it
    // straight through.
    private static readonly byte[] s_result48 = new byte[40];

    // JUSTIFICATION: C# language bridge only
    // RELATION: "CdlLOC local_38" in FUN_8005d25c, the one stack position the init reuses for every
    // seek. One instance, reused, exactly as the original reuses one stack slot.
    private static readonly LibCd.CdlLOC s_initLoc = new();

    // JUSTIFICATION: C# language bridge only
    // RELATION: the CdlFILE the workspace holds inline at +0x18 / +0x48 / +0x90 / +0xC0 / +0xF0.
    // CdSearchFile in PsxSdkMonogame takes a managed CdlFILE; the original hands it a pointer INTO
    // the workspace and then reads the position back out of that memory. This scratch object
    // carries the call, and SearchFileIntoWorkspace writes the result back where the original left
    // it, so the following CdPosToInt / CdControl see the same bytes they see on the console.
    private static readonly LibCd.CdlFILE s_searchFile = new() { pos = new LibCd.CdlLOC() };

    // JUSTIFICATION: C# language bridge only
    // RELATION: the SpuStEnv the init obtains from SpuStInit and publishes into DAT_8008d338. The
    // global is an int POINTER CELL in this port (SoundState.DAT_8008d338), and a managed SpuStEnv
    // has no PSX address to put in it, so the object is held here instead. BLOCKED: LibSpu.SpuStInit
    // is a "Do nothing PSX SDK" stub that returns null, so this stays null, DAT_8008d338 stays 0,
    // and the three field writes the init makes through it do not happen -- which is exactly what
    // SoundState's own comment on DAT_8008d338 already predicts for its sixteen readers.
    private static LibSpu.SpuStEnv s_spuStEnv;

    // JUSTIFICATION: C# language bridge only
    // RELATION: the retry pair the original writes around every CdSearchFile call --
    //     do { do { p = CdSearchFile(slot, name); } while (p == 0); } while (p == -1);
    // -- five times in FUN_8005d25c. The inner loop spins until the search answers non-null; the
    // outer one re-runs it if the answer was the sentinel -1, which libcd's CdSearchFile never
    // actually returns, so on the console the outer loop runs exactly once.
    //
    // DEVIATION, and it is this port's standing rule rather than a decision taken here: the port
    // does not simulate a CD drive, so a missing file is not something to spin on. MOVIE_EXE and
    // SELECT_EXE already throw by name in the same situation (SelectScreen.cs @ \SUB\USAGI.B;1),
    // and the same is done below. Spinning would hang the desktop build with no diagnosis.
    //
    // The result is written BACK INTO THE WORKSPACE at "slotAddress", four bytes of CdlLOC then the
    // four-byte size, because that is where the original leaves it: CdSearchFile is handed a
    // pointer into the task context and the following CdPosToInt / CdControl read those same bytes.
    private static void SearchFileIntoWorkspace(int slotAddress, string name)
    {
        LibCd.CdlFILE found = LibCd.CdSearchFile(s_searchFile, name.ToCharArray());
        if (found == null)
        {
            throw new InvalidOperationException(
                "CdSearchFile could not resolve " + name + " for the VS.EXE sound task");
        }

        StoreLoc(slotAddress, found.pos);
        PsxRam.WriteI32(slotAddress + 4, found.size);
    }

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: "do { sVar3 = SsVabTransCompleted(0); } while (sVar3 == 0);" -- the init blocks on
    // the SPU transfer twice, at 0x8005D5B4 (BGM) and at 0x8005D95C (ABTL).
    //
    // DEVIATION, stated because it is a real one. LibSnd.SsVabTransCompleted in PsxSdkMonogame is
    // a "Do nothing PSX SDK" body that returns default, i.e. 0, which is exactly this loop's
    // "not yet". Transliterated literally the init would spin for ever on the first VAB and VS.EXE
    // would never boot -- the same trap LibCd.CdSync's own comment records having fallen into and
    // fixed on the libcd side by answering CdlComplete. The libsnd side has not had that pass, and
    // this slice may not edit PsxSdkMonogame, so the wait is written as ONE poll: ask once, then
    // continue whatever the answer. On a console where the stub is filled in, replacing the body of
    // this helper with the original loop is the whole change.
    //
    // SoundDriver.SoundCdLoadStep above hits the same stub in its state 7 and is NOT affected: it
    // polls and returns rather than spinning, so it simply never leaves state 7. That is the
    // faithful outcome there, and it stays.
    private static void WaitVabTransCompleted()
    {
        LibSnd.SsVabTransCompleted(0);
    }

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: SpuMallocWithStartAddr @ 0x800666CC, libspu's "reserve size bytes of sound RAM at
    // exactly this address". PsxSdkMonogame does NOT export it -- LibSpu.cs carries only the
    // commented-out prototype "long* SpuMallocWithStartAddr(long* addr, long size);" next to
    // SpuInitMalloc -- and this slice may not edit that project, so the entry is stubbed here.
    //
    // WHAT IT RETURNS AND WHY. libspu answers the reserved address on success and -1 on failure.
    // The init tests exactly that: "if (uVar4 == 0xffffffff) printf(NO_Buffer!!!)" and otherwise
    // runs its whole body. Returning -1 would send every VS.EXE boot down the failure arm and skip
    // the entire sound bring-up, which is a behaviour change; returning the address is the success
    // answer libspu gives when the region is free, and sound RAM is untouched at this point in the
    // boot because SsInit has only just run. So the address is returned.
    //
    // BLOCKED all the same: no allocation is actually recorded anywhere, so a later SpuMalloc
    // would not know this block is taken. Nothing in the port calls SpuMalloc yet.
    private static uint SpuMallocWithStartAddr(uint addr, int size)
    {
        _ = size;
        return addr;
    }

    // GHIDRA: FUN_8005d1fc @ 0x8005D1FC (VS.EXE)
    // 96 bytes, two callees, ONE incoming reference and it is not a call: main @ 0x80062280 passes
    // this address to CreateTask as a PARAMETER. VS_EXE_exe.cs already does that --
    // "TaskSystem.CreateTask(Lab8005d1fcAddress, 0x57, 0x14, 0x194, 0, g_TaskListTail[20])" -- and
    // stores the raw address in the node, so this method is unreachable until the main session adds
    // "TaskSystem.RegisterCallback(0x8005D1FC, SoundDriver.FUN_8005d1fc)". THAT IS THE ONE WIRING
    // STEP THIS SLICE CANNOT TAKE, because VS_EXE_exe.cs is outside it.
    //
    // THE DISPATCH IS ON THE WORKSPACE, NOT ON A LOCAL. "*(int *)(DAT_8008d16c + 8)" is the CURRENT
    // TASK NODE's context pointer: 0x8008D16C holds the node, +8 is its context field. The port
    // keeps that cell in TaskSystem.g_CurrentTask (SoundState.Dat8008d16cAddress names the address
    // only), and CreateTask really does write the context pointer at node+8, so the read below is
    // the same read. Note it does NOT go through SoundState.DAT_8008d284: the original re-reads the
    // task node every tick and only the init publishes the pointer to the global.
    internal static void FUN_8005d1fc()
    {
        short sVar1 = (short)PsxRam.ReadU16(
            PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8) + SoundState.TaskState);
        if (sVar1 == 0)
        {
            FUN_8005d25c();
        }
        else if (sVar1 == 1)
        {
            FUN_8005da78();
        }
    }

    // GHIDRA: FUN_8005d25c @ 0x8005D25C (VS.EXE)
    // 2076 bytes, 37 callees, decompiled in full (254 lines). THE SOUND INIT, run once, on the tick
    // where the workspace's state halfword at +0x10E is still 0. Its very last statement increments
    // that halfword, which is what hands the task over to FUN_8005da78 for good.
    //
    // THIRTY-FOUR OF THE 37 CALLEES ARE SDK and are called, not ported: SsInit, SsSetTableSize,
    // SsSetTickMode, SsSetReservedVoice, SsSetMVol, SsStart, SsVabOpenHeadSticky, SsVabTransBody,
    // SsVabTransCompleted, SsUtGetVBaddrInSB, SsUtSetReverbType, SsUtReverbOn, SsUtSetReverbDepth,
    // SpuSetTransferMode, SpuSetTransferStartAddr, SpuWrite0, SpuIsTransferCompleted, SpuStInit,
    // the three SpuStSet*Callback registrations, SpuMallocWithStartAddr, SpuSetVoiceAttr,
    // CdSearchFile, CdControl, CdSync, CdRead, CdReadSync, CdPosToInt, CdIntToPos, VSyncCallback,
    // printf and rand. The remaining three are the overlay's own FUN_8006bcd0, FUN_8006bdd8 and
    // FUN_8006de64 -- all three above 0x800632C4, hence SDK by the mandate's own line, hence the
    // BLOCKED stubs at the foot of this file rather than transliterations -- plus FUN_80060364,
    // which is game code below that line and belongs to the next slice.
    //
    // WHAT IT LOADS, in the order it loads it. \SOUND\BGM.B in three pieces: 0xA0 sectors of body
    // into the staging area at 0x80110000, six sectors of VAB header at +0xA0 into 0x801B6000, ten
    // sectors of sequences at +0xA6 into 0x801B9000; then one of eight BGM tracks chosen by
    // "rand() & 7 | 8" into 0x801BE000; then \SOUND\ABTL.B as a two-sector header into 0x801C3000
    // and a 0x19-sector body into g_cdFileBufferTable. \SOUND\CR.B, \SOUND\ATB.B and \SOUND\CHSE.B
    // are only SEARCHED -- their CdlFILE slots at +0x48, +0x90 and +0xF0 are filled and nothing is
    // read -- which is what lets FUN_8005f068 and SoundCdLoadStep seek into them on demand later.
    //
    // THE RETRY LOOPS ARE KEPT, and they all terminate on this port for reasons that are the SDK's,
    // not this file's: LibCd.CdSync answers CdlComplete (2), so "while (iVar6 == 0)" and
    // "while (iVar6 == 5 || iVar6 != 2)" both fall through; LibCd.CdReadSync answers 0, so
    // "while (iVar6 != 0)" falls through; SsVabOpenHeadSticky and SsVabTransBody answer 0, so their
    // two do/while loops run once. The ONE loop that would not terminate is the SsVabTransCompleted
    // wait, and it is routed through WaitVabTransCompleted above with the deviation spelled out
    // there. Everything else below is the original's control flow, statement for statement.
    internal static void FUN_8005d25c()
    {
        LibSpu.SpuStEnv pSVar1;
        ushort uVar2;
        short sVar3;
        uint uVar4;
        int iVar6;
        int iVar8;
        int pCVar9;

        iVar8 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        SoundState.DAT_8008d284 = iVar8;
        LibSnd.SsInit();
        LibSnd.SsSetTableSize(Dat800c2e88Address, 4, 5);
        LibSnd.SsSetTickMode(1);
        FUN_8006de64(0);
        LibSnd.SsSetReservedVoice(0x11);
        LibSnd.SsSetMVol(0x7f, 0x7f);
        FUN_8006bcd0(0x11, 0x7fff, 0x7fff);
        FUN_8006bcd0(0x12, 0x7fff, 0x7fff);
        FUN_8006bcd0(0x13, 0x7fff, 0x7fff);
        FUN_8006bcd0(0x14, 0x7fff, 0x7fff);
        FUN_8006bcd0(0x15, 0x7fff, 0x7fff);
        FUN_8006bcd0(0x16, 0x7fff, 0x7fff);
        FUN_8006bdd8(0x11, 0x40, 0x40);
        FUN_8006bdd8(0x12, 0x40, 0x40);
        FUN_8006bdd8(0x13, 0x40, 0x40);
        FUN_8006bdd8(0x14, 0x40, 0x40);

        // The last two voices are set to 0x38, not 0x40. Not a transcription slip: 0x8005D368 and
        // 0x8005D378 really do load 0x38 where the four above load 0x40.
        FUN_8006bdd8(0x15, 0x38, 0x38);
        FUN_8006bdd8(0x16, 0x38, 0x38);
        LibSpu.SpuSetTransferMode(0);
        LibSpu.SpuSetTransferStartAddr(0);
        LibSpu.SpuWrite0(0x80000);
        LibSpu.SpuIsTransferCompleted(1);
        LibSnd.SsStart();

        // DAT_8008d338 = SpuStInit(0). SoundState models that global as an int POINTER CELL, and
        // there is nothing to put in it: LibSpu.SpuStInit is a stub that returns null, and a
        // managed SpuStEnv has no PSX address. BLOCKED: the cell stays 0, exactly as SoundState's
        // own comment already predicts, so the sixteen readers of *(DAT_8008d338 + n) elsewhere in
        // the overlay keep writing to an unmapped address and keep being dropped. The object itself
        // is kept below so the three field writes the original makes are visible and real the day
        // the SDK grows a body.
        pSVar1 = LibSpu.SpuStInit(0);
        s_spuStEnv = pSVar1;
        LibSpu.SpuStSetPreparationFinishedCallback(
            (voiceBit, status) => TaskSystem.InvokeCallbackByAddress(Lab8005e88cAddress));
        LibSpu.SpuStSetTransferFinishedCallback(
            (voiceBit, status) => TaskSystem.InvokeCallbackByAddress(Lab8005e954Address));
        LibSpu.SpuStSetStreamFinishedCallback(
            (voiceBit, status) => TaskSystem.InvokeCallbackByAddress(Lab8005eae8Address));
        uVar4 = SpuMallocWithStartAddr(0x7d800, 0x400);
        SoundState.DAT_8008d280 = unchecked((int)uVar4);
        if (uVar4 == 0xffffffff)
        {
            // printf("NO_Buffer!!!\n") -- the literal at 0x8002090C. The port has no printf bridge
            // in VS_EXE; the message is written to the console, which is the same observable.
            Console.WriteLine("NO_Buffer!!!");
        }
        else
        {
            pCVar9 = iVar8 + SoundState.BgmBankSlot;

            // The three SpuStEnv writes. Guarded because pSVar1 is null on this port (see above);
            // this is the one place in the function where the original's stores do not happen, and
            // it is the SDK stub's doing, not a simplification.
            if (pSVar1 != null)
            {
                pSVar1.size = 0x400;
                pSVar1.voice[0x17] ??= new LibSpu.SpuStVoiceAttr();
                pSVar1.voice[0x17].buf_addr = uVar4;
                pSVar1.voice[0x17].data_addr = unchecked((uint)SoundState.Dat801c1000Address);
            }

            DAT_800b0ddc.mask = 0xff93;
            DAT_800b0ddc.voice = 0x800000;
            DAT_800b0ddc.volume.left = 0x3fff;
            DAT_800b0ddc.volume.right = 0x3fff;
            DAT_800b0ddc.pitch = 0x400;
            DAT_800b0ddc.a_mode = 1;
            DAT_800b0ddc.s_mode = 1;
            DAT_800b0ddc.r_mode = 3;
            DAT_800b0ddc.ar = 0;
            DAT_800b0ddc.dr = 0;
            DAT_800b0ddc.sr = 0;
            DAT_800b0ddc.rr = 0;
            DAT_800b0ddc.sl = 0xf;
            DAT_800b0ddc.addr = uVar4;
            LibSpu.SpuSetVoiceAttr(DAT_800b0ddc);
            LibSnd.SsUtSetReverbType(2);

            SearchFileIntoWorkspace(pCVar9, "\\SOUND\\BGM.B;1");

            // local_34 = 0xa0, then local_38 is filled BYTE BY BYTE from the bank's CdlLOC. The
            // original does not call CdIntToPos here: it copies the position it just found and
            // seeks straight to it.
            int local_34 = 0xa0;
            LoadLoc(pCVar9, s_initLoc);
            LibCd.CdControl(2, s_initLoc, s_result48);
            do
            {
                do
                {
                    iVar6 = LibCd.CdSync(1, s_result48);
                }
                while (iVar6 == 0);
            }
            while ((iVar6 == 5) || (iVar6 != 2));

            LibCd.CdRead(local_34, VS_EXE_exe.Dat80110000Address, 0x80);
            do
            {
                iVar6 = LibCd.CdReadSync(1, s_result48);
            }
            while (iVar6 != 0);

            local_34 = 6;
            LoadLoc(pCVar9, s_initLoc);
            iVar6 = LibCd.CdPosToInt(s_initLoc);
            LibCd.CdIntToPos(iVar6 + 0xa0, s_initLoc);
            LibCd.CdControl(2, s_initLoc, s_result48);
            do
            {
                do
                {
                    iVar6 = LibCd.CdSync(1, s_result48);
                }
                while (iVar6 == 0);
            }
            while ((iVar6 == 5) || (iVar6 != 2));

            LibCd.CdRead(local_34, Dat801b6000Address, 0x80);
            do
            {
                iVar6 = LibCd.CdReadSync(1, s_result48);
            }
            while (iVar6 != 0);

            do
            {
                uVar2 = unchecked((ushort)LibSnd.SsVabOpenHeadSticky(Dat801b6000Address, -1, 0x1800));
                PsxRam.WriteU16(iVar8 + SoundState.BgmVabHandle, uVar2);
            }
            while ((int)((uint)uVar2 << 0x10) < 0);

            do
            {
                sVar3 = LibSnd.SsVabTransBody(
                    VS_EXE_exe.Dat80110000Address,
                    (short)PsxRam.ReadU16(iVar8 + SoundState.BgmVabHandle));
                PsxRam.WriteU16(iVar8 + SoundState.BgmVabHandle, unchecked((ushort)sVar3));
            }
            while (sVar3 == -1);

            WaitVabTransCompleted();
            LibSnd.SsUtGetVBaddrInSB((short)PsxRam.ReadU16(iVar8 + SoundState.BgmVabHandle));

            local_34 = 10;
            LoadLoc(pCVar9, s_initLoc);
            iVar6 = LibCd.CdPosToInt(s_initLoc);
            LibCd.CdIntToPos(iVar6 + 0xa6, s_initLoc);
            LibCd.CdControl(2, s_initLoc, s_result48);
            do
            {
                do
                {
                    iVar6 = LibCd.CdSync(1, s_result48);
                }
                while (iVar6 == 0);
            }
            while ((iVar6 == 5) || (iVar6 != 2));

            LibCd.CdRead(local_34, Dat801b9000Address, 0x80);
            do
            {
                iVar6 = LibCd.CdReadSync(1, s_result48);
            }
            while (iVar6 != 0);

            // +0x112 is cleared and the SAME random track number is written to all three of +0x114,
            // +0x116 and +0x118. rand() & 7 | 8 lands in 8..15, which is exactly the range of
            // PTR_DAT_8008481c that holds LBA offsets rather than pointers.
            PsxRam.WriteU16(iVar8 + 0x112, 0);
            iVar6 = Kernel.rand();
            uVar2 = (ushort)(((ushort)iVar6 & 7) | 8);
            PsxRam.WriteU16(iVar8 + 0x114, uVar2);
            PsxRam.WriteU16(iVar8 + 0x116, uVar2);
            PsxRam.WriteU16(iVar8 + 0x118, uVar2);

            local_34 = 6;
            LoadLoc(pCVar9, s_initLoc);
            iVar6 = LibCd.CdPosToInt(s_initLoc);
            LibCd.CdIntToPos(
                PsxRam.ReadI32(
                    PtrDat8008481cAddress + (short)PsxRam.ReadU16(iVar8 + 0x114) * 4) + iVar6,
                s_initLoc);
            LibCd.CdControl(2, s_initLoc, s_result48);
            do
            {
                do
                {
                    iVar6 = LibCd.CdSync(1, s_result48);
                }
                while (iVar6 == 0);
            }
            while ((iVar6 == 5) || (iVar6 != 2));

            LibCd.CdRead(local_34, Dat801be000Address, 0x80);
            do
            {
                iVar6 = LibCd.CdReadSync(1, s_result48);
            }
            while (iVar6 != 0);

            PsxRam.WriteU16(iVar8 + 0x108, 0xffff);
            PsxRam.WriteU16(iVar8 + 0x11a, 0x36);
            PsxRam.WriteU16(iVar8 + 0x11c, 0x36);
            LibSnd.SsUtSetReverbDepth(5, 5);
            pCVar9 = iVar8 + SoundState.AbtlBankSlot;
            LibSnd.SsUtReverbOn();

            SearchFileIntoWorkspace(pCVar9, "\\SOUND\\ABTL.B;1");

            // From here the sector count lives in the workspace at +0x34 rather than in a local,
            // and the position is copied byte by byte from the ABTL bank's CdlLOC at +0xC0 into the
            // machine's own CdlLOC at +0x30. The CdControl that follows is nevertheless issued on
            // +0xC0, not on +0x30 -- the original's own choice, reproduced.
            PsxRam.WriteI32(iVar8 + 0x34, 2);
            PsxRam.WriteU8(iVar8 + 0x30, PsxRam.ReadU8(pCVar9));
            PsxRam.WriteU8(iVar8 + 0x31, PsxRam.ReadU8(iVar8 + 0xc1));
            PsxRam.WriteU8(iVar8 + 0x32, PsxRam.ReadU8(iVar8 + 0xc2));
            PsxRam.WriteU8(iVar8 + 0x33, PsxRam.ReadU8(iVar8 + 0xc3));
            LoadLoc(pCVar9, s_initLoc);
            LibCd.CdControl(2, s_initLoc, s_result48);
            do
            {
                do
                {
                    iVar6 = LibCd.CdSync(1, s_result48);
                }
                while (iVar6 == 0);
            }
            while ((iVar6 == 5) || (iVar6 != 2));

            LibCd.CdRead(PsxRam.ReadI32(iVar8 + 0x34), Dat801c3000Address, 0x80);
            do
            {
                iVar6 = LibCd.CdReadSync(1, s_result48);
            }
            while (iVar6 != 0);

            PsxRam.WriteI32(iVar8 + 0x34, 0x19);
            LoadLoc(pCVar9, s_initLoc);
            iVar6 = LibCd.CdPosToInt(s_initLoc);
            LibCd.CdIntToPos(iVar6 + 2, s_initLoc);

            // CdIntToPos writes THROUGH the pointer into the workspace's own CdlFILE at +0xC0, and
            // the CdControl on the next line reads it back from there. Mirrored, or the retry would
            // seek to whatever was in the slot before.
            StoreLoc(pCVar9, s_initLoc);
            LibCd.CdControl(2, s_initLoc, s_result48);
            do
            {
                do
                {
                    iVar6 = LibCd.CdSync(1, s_result48);
                }
                while (iVar6 == 0);
            }
            while ((iVar6 == 5) || (iVar6 != 2));

            LibCd.CdRead(PsxRam.ReadI32(iVar8 + 0x34), FileIo.g_cdFileBufferTableAddress, 0x80);
            do
            {
                iVar6 = LibCd.CdReadSync(1, s_result48);
            }
            while (iVar6 != 0);

            do
            {
                uVar2 = unchecked((ushort)LibSnd.SsVabOpenHeadSticky(Dat801c3000Address, -1, 0x51800));
                PsxRam.WriteU16(iVar8 + SoundState.AbtlVabHandle, uVar2);
            }
            while ((int)((uint)uVar2 << 0x10) < 0);

            do
            {
                sVar3 = LibSnd.SsVabTransBody(
                    FileIo.g_cdFileBufferTableAddress,
                    (short)PsxRam.ReadU16(iVar8 + SoundState.AbtlVabHandle));
                PsxRam.WriteU16(iVar8 + SoundState.AbtlVabHandle, unchecked((ushort)sVar3));
            }
            while (sVar3 == -1);

            WaitVabTransCompleted();
            LibSnd.SsUtGetVBaddrInSB((short)PsxRam.ReadU16(iVar8 + SoundState.AbtlVabHandle));

            // TWELVE slots of +0x160 and +0x178 are cleared here, not six. The pairs at +0x12C and
            // +0x148 further down really are six -- the loop counts are different and both are
            // reproduced as written.
            int iVar7 = 0;
            PsxRam.WriteU16(iVar8 + 0x156, 0);
            iVar6 = iVar8;
            do
            {
                PsxRam.WriteU16(iVar6 + 0x160, 0);
                PsxRam.WriteU16(iVar6 + 0x178, 0);
                iVar7 = iVar7 + 1;
                iVar6 = iVar6 + 2;
            }
            while (iVar7 < 0xc);

            PsxRam.WriteU8(iVar8 + 0x142, 0x40);
            PsxRam.WriteU8(iVar8 + 0x143, 0x40);

            SearchFileIntoWorkspace(iVar8 + SoundState.CrBankSlot, "\\SOUND\\CR.B;1");
            iVar6 = 0;

            PsxRam.WriteU16(iVar8 + 0x138, 0);
            AnimCmdSound.DAT_8008d384 = 0;
            DAT_8008d3c0 = 0;
            DAT_8008d418 = 0;
            PsxRam.WriteU16(iVar8 + 0x13a, 10);

            // SIX slots here, and the zero-fill of the VoiceFlags array is five wide in the sense
            // SoundState's comment warns about: this loop really does run i = 0..5, so both arrays
            // get six entries. SoundState's note that "the init's zero-fill covers only five" is
            // the reading it asked the porter of this function to re-check, and the answer is six.
            int iVar7b = iVar8;
            do
            {
                PsxRam.WriteU16(iVar7b + SoundState.VoiceHandles, 0xffff);
                PsxRam.WriteU16(iVar7b + SoundState.VoiceFlags, 0);
                iVar6 = iVar6 + 1;
                iVar7b = iVar7b + 2;
            }
            while (iVar6 < 6);

            PsxRam.WriteU16(iVar8 + SoundState.Gate12A, 0);

            SearchFileIntoWorkspace(iVar8 + SoundState.AtbBankSlot, "\\SOUND\\ATB.B;1");
            SearchFileIntoWorkspace(iVar8 + SoundState.ChseBankSlot, "\\SOUND\\CHSE.B;1");

            PsxRam.WriteU16(iVar8 + SoundState.PendingVabHandle, 0xffff);
            PsxRam.WriteU16(iVar8 + SoundState.CompletedRequestId, 0);
            SoundEffects.FUN_80060364();
            PsxRam.WriteU16(iVar8 + 0x190, 0);
            PsxRam.WriteU16(iVar8 + 0x192, 0);
            LibEtc.VSyncCallback(() => TaskSystem.InvokeCallbackByAddress(Lab8005eb14Address));
            PsxRam.WriteU16(
                iVar8 + SoundState.TaskState,
                unchecked((ushort)((short)PsxRam.ReadU16(iVar8 + SoundState.TaskState) + 1)));
        }
    }

    // GHIDRA: FUN_8005da78 @ 0x8005DA78 (VS.EXE)
    // 1908 bytes, fifteen callees, decompiled in full (243 lines). THE PER-FRAME SOUND SERVICE, run
    // on every tick once the init has moved the workspace's state to 1. Two callers: FUN_8005d1fc
    // above, and FUN_80060a88 @ 0x80060B7C which calls it after a VSync inside a busy wait.
    //
    // IT IS THREE MACHINES IN SEQUENCE, and they are not merged here:
    //   1. the BGM request latch (+0x118 vs +0x112) and one step of FUN_8005e1ec;
    //   2. the streamed-clip state machine, a switch on "DAT_8008d384 & 0x3f" over cases 1..0x10
    //      that walks a CdlSetloc / CdSync / CdRead / CdReadSync chain into 0x801C1000 and falls
    //      through to "DAT_8008d384 + 1" on every case that BREAKS rather than jumping to the exit;
    //   3. a twelve-slot sweep of the battle context that keys the "hit" sound on and off.
    //
    // THE SWITCH IS ON THE LOW SIX BITS and the state word is a short (AnimCmdSound.DAT_8008d384,
    // whose own comment closes the width from the lh / lhu pair at 0x80060120 and 0x8006012C). Bit
    // 0x40 is a separate request flag that case 9/0x10 tests, and bit 0x80 is a "reset" that the
    // common exit turns into state 0xB. Cases 2/6/0xD, 3/7/0xE and 4/8/0xF share one body each --
    // that is the original's own jump table, not a factoring introduced here.
    //
    // TWO THINGS THAT LOOK LIKE DEFECTS AND ARE THE ORIGINAL'S, reproduced rather than corrected:
    //   * case 5 sets pCVar8 to the workspace's SECOND CdlLOC at +0x60 and hands that to
    //     CdIntToPos, but the CdControl on the shared tail is issued on +0x30 regardless -- the
    //     instruction at 0x8005DE6C loads +0x30 unconditionally. So case 5 computes a position into
    //     +0x60 and then seeks to whatever is in +0x30.
    //   * case 4/8/0xF loads DAT_801c1a22 into uVar1 BEFORE testing CdReadSync's result, then in
    //     the "== 4" arm stores the freshly re-read DAT_801c1a22 into +0x13E and the STALE uVar1
    //     into +0x140. Both loads are the same halfword and there is no store between them, so the
    //     two values agree; the double read is kept because removing it would be a simplification.
    internal static void FUN_8005da78()
    {
        ushort uVar1;
        int iVar2;
        int iVar3;
        byte uVar4;
        byte uVar5;
        int iVar6;
        int iVar7;
        int pCVar8;
        int iVar9;
        int iVar10;
        short sVar11;

        iVar7 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        if (((short)PsxRam.ReadU16(iVar7 + 0x118) != (short)PsxRam.ReadU16(iVar7 + 0x112))
            && ((short)PsxRam.ReadU16(iVar7 + 0x110) == 0)
            && (AnimCmdSound.DAT_8008d384 == 0))
        {
            PsxRam.WriteU16(iVar7 + 0x112, PsxRam.ReadU16(iVar7 + 0x118));
            PsxRam.WriteU16(iVar7 + 0x110, 0x10);
        }

        pCVar8 = iVar7 + 0x30;
        if ((PsxRam.ReadU16(iVar7 + 0x10c) & 1) == 0)
        {
            ushort uVarSeq = unchecked((ushort)FUN_8005e1ec(
                iVar7, (ushort)(short)PsxRam.ReadU16(iVar7 + 0x110), 0));
            PsxRam.WriteU16(iVar7 + 0x110, uVarSeq);
        }

        // The switch's break arms fall into "DAT_8008d384 + 1"; the arms that must NOT advance the
        // state jump straight to the common exit, which is the goto-free "advanced" flag below.
        // Ghidra prints that exit as switchD_8005db38_caseD_0 and every "goto" in the decompilation
        // targets it; C# has no such label reachable from a switch, so the one boolean carries it.
        // No arm was reordered and no test was inverted: an arm that gotos sets advanced = false.
        bool advanced = true;
        switch (AnimCmdSound.DAT_8008d384 & 0x3f)
        {
            default:
                advanced = false;
                break;

            case 1:
                sVar11 = (short)PsxRam.ReadU16(iVar7 + 0x110);
                if (((sVar11 != 0) && (sVar11 != 0xc)) && (sVar11 < 0x11))
                {
                    advanced = false;
                    break;
                }

                PsxRam.WriteI32(iVar7 + 0x34, 2);
                LoadLoc(iVar7 + SoundState.CrBankSlot, s_loc);
                iVar2 = LibCd.CdPosToInt(s_loc);
                iVar10 = (short)PsxRam.ReadU16(iVar7 + 0x138);
                iVar2 = iVar2 + iVar10 * 0x22;

                // Seven cumulative corrections to the LBA, each a plain "greater than" on the same
                // index. They are additive: an index above 0x5D6 collects every one of them.
                if (0x115 < iVar10)
                {
                    iVar2 = iVar2 + 0x22;
                }

                if (0x1ed < iVar10)
                {
                    iVar2 = iVar2 + 0x22;
                }

                if (0x488 < iVar10)
                {
                    iVar2 = iVar2 + 0x22;
                }

                if (0x489 < iVar10)
                {
                    iVar2 = iVar2 + 0x88;
                }

                if (0x48a < iVar10)
                {
                    iVar2 = iVar2 + 0x22;
                }

                if (0x503 < iVar10)
                {
                    iVar2 = iVar2 + 0x44;
                }

                if (0x5d6 < iVar10)
                {
                    iVar2 = iVar2 + 0x22;
                }

                // LAB_8005de60: the shared tail resets pCVar8 to +0x30 before CdIntToPos.
                pCVar8 = iVar7 + 0x30;
                SeekTailDa78(iVar7, pCVar8, iVar2);
                break;

            case 2:
            case 6:
            case 0xd:
                iVar2 = LibCd.CdSync(1, s_result48);
                if (iVar2 != 2)
                {
                    sVar11 = (short)PsxRam.ReadU16(iVar7 + 0x13a);
                    if (iVar2 == 5)
                    {
                        PsxRam.WriteU16(iVar7 + 0x13a, unchecked((ushort)(sVar11 - 1)));
                        if (sVar11 == 1)
                        {
                            LoadLoc(pCVar8, s_loc);
                            LibCd.CdControl(2, s_loc, s_result48);
                            PsxRam.WriteU16(iVar7 + 0x13a, 10);
                        }
                    }

                    advanced = false;
                }

                break;

            case 3:
            case 7:
            case 0xe:
                LibCd.CdRead(
                    PsxRam.ReadI32(iVar7 + 0x34), SoundState.Dat801c1000Address, 0x80);
                break;

            case 4:
            case 8:
            case 0xf:
                iVar2 = LibCd.CdReadSync(1, s_result48);
                uVar1 = PsxRam.ReadU16(Dat801c1a22Address);
                if (iVar2 != 0)
                {
                    sVar11 = (short)PsxRam.ReadU16(iVar7 + 0x13a);
                    if (iVar2 == -1)
                    {
                        PsxRam.WriteU16(iVar7 + 0x13a, unchecked((ushort)(sVar11 - 1)));
                        if (sVar11 == 1)
                        {
                            LoadLoc(pCVar8, s_loc);
                            LibCd.CdControl(2, s_loc, s_result48);
                            PsxRam.WriteU16(iVar7 + 0x13a, 10);
                            AnimCmdSound.DAT_8008d384 =
                                unchecked((short)(AnimCmdSound.DAT_8008d384 - 2));
                        }
                    }

                    advanced = false;
                    break;
                }

                if ((AnimCmdSound.DAT_8008d384 & 0x3f) == 4)
                {
                    PsxRam.WriteU16(iVar7 + 0x13e, PsxRam.ReadU16(Dat801c1a22Address));
                    PsxRam.WriteU16(iVar7 + 0x140, uVar1);
                    PsxRam.WriteU16(
                        iVar7 + 0x13e,
                        unchecked((ushort)((short)PsxRam.ReadU16(iVar7 + 0x13e) - 0x40)));
                }

                break;

            case 5:
                PsxRam.WriteI32(iVar7 + 0x34, 4);
                LoadLoc(pCVar8, s_loc);
                iVar2 = LibCd.CdPosToInt(s_loc);
                iVar2 = iVar2 + 2;
                LibCd.CdIntToPos(iVar2, s_loc);
                StoreLoc(pCVar8, s_loc);

                // pCVar8 moves to the SECOND CdlLOC, and LAB_8005de64 is entered without passing
                // through LAB_8005de60, so the reset to +0x30 does not happen. See the header note.
                pCVar8 = iVar7 + 0x60;
                SeekTailDa78(iVar7, pCVar8, iVar2);
                break;

            case 9:
            case 0x10:
                if ((AnimCmdSound.DAT_8008d384 & 0x40) != 0)
                {
                    PsxRam.WriteI32(iVar7 + 0x34, 2);
                    LoadLoc(pCVar8, s_loc);
                    iVar2 = LibCd.CdPosToInt(s_loc);
                    LibCd.CdIntToPos(iVar2 + 4, s_loc);
                    StoreLoc(pCVar8, s_loc);
                    AnimCmdSound.DAT_8008d384 = 10;
                    LibSpu.SpuStTransfer(4, 0x800000);
                    DAT_800b0ddc.mask = 3;
                    DAT_800b0ddc.voice = 0x800000;
                    DAT_800b0ddc.volume.left = 0x3fff;
                    DAT_800b0ddc.volume.right = 0x3fff;
                    LibSpu.SpuSetVoiceAttr(DAT_800b0ddc);
                }

                advanced = false;
                break;

            case 0xb:
                AnimCmdSound.DAT_8008d384 = 0;
                DAT_8008d3c0 = 0;
                if (DAT_8008d418 == 0)
                {
                    PsxRam.WriteU16(iVar7 + 0x140, 0);
                    PsxRam.WriteU16(iVar7 + 0x13e, 0);
                }

                BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 & 0xffffffdf;
                advanced = false;
                break;

            case 0xc:
                sVar11 = (short)PsxRam.ReadU16(iVar7 + 0x110);
                if (((sVar11 != 0) && (sVar11 != 0xc)) && (sVar11 < 0x11))
                {
                    advanced = false;
                    break;
                }

                PsxRam.WriteI32(iVar7 + 0x34, 4);
                LoadLoc(iVar7 + 0x60, s_loc);
                iVar2 = LibCd.CdPosToInt(s_loc);
                if (PsxRam.ReadU16(iVar7 + 0x13e) < 0x81)
                {
                    AnimCmdSound.DAT_8008d384 = 0;
                    DAT_8008d3c0 = 0;
                    DAT_8008d418 = 0;
                    PsxRam.WriteU16(iVar7 + 0x13e, 0);
                    advanced = false;
                    break;
                }

                // The signed correction on an UNSIGNED difference is the original's: both operands
                // are read with lhu, subtracted as ints, and only then tested for negativity. The
                // "+ 0xff" is a rounding term before the arithmetic shift by 8, not a mask.
                iVar10 = PsxRam.ReadU16(iVar7 + 0x140) - PsxRam.ReadU16(iVar7 + 0x13e);
                if (iVar10 < 0)
                {
                    iVar10 = iVar10 + 0xff;
                }

                iVar2 = iVar2 + (iVar10 >> 8);
                if ((PsxRam.ReadU16(iVar7 + 0x13e) & 0x80) != 0)
                {
                    PsxRam.WriteU16(
                        iVar7 + 0x13e, unchecked((ushort)(PsxRam.ReadU16(iVar7 + 0x13e) - 0x40)));
                }

                pCVar8 = iVar7 + 0x30;
                SeekTailDa78(iVar7, pCVar8, iVar2);
                break;
        }

        if (advanced)
        {
            AnimCmdSound.DAT_8008d384 = unchecked((short)(AnimCmdSound.DAT_8008d384 + 1));
        }

        if ((AnimCmdSound.DAT_8008d384 & 0x80) != 0)
        {
            AnimCmdSound.DAT_8008d384 = 0xb;
        }

        // ---- The twelve-slot battle-context sweep.
        //
        // "*DAT_8008d320" is an lhu at offset 0 of the battle context (0x8005DEB0), and
        // "*(uint *)(DAT_8008d320 + 8)" is an lw at offset 0x10 (0x8005DEC0). The doubling is
        // Ghidra printing pointer arithmetic on a short*; AnimVmInterpreter.cs already reads that
        // same word as "PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10)", which is the
        // independent confirmation. The slot table index resolves the same way:
        // "(iVar2 >> 0x10) * 2 + 0xa90" in short units is (iVar2 >> 0x10) * 4 + 0x1520 in bytes,
        // and 0x1520 is BattleState.CtxFighterSlots, the array BattleScene.cs already documents.
        if ((PsxRam.ReadU16(BattleManager.DAT_8008d320) == 1)
            && (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 0x8000008) == 0))
        {
            sVar11 = 0;
            iVar10 = 0;
            iVar2 = 0;
            do
            {
                int slot = PsxRam.ReadI32(
                    BattleManager.DAT_8008d320 + (iVar2 >> 0x10) * 4 + BattleState.CtxFighterSlots);
                if (slot != 0)
                {
                    iVar6 = PsxRam.ReadI32(slot + 8);
                    if ((PsxRam.ReadU8(iVar6 + 0x16a) == 0x1e)
                        || (((uint)PsxRam.ReadI32(iVar6 + 0x138) & 0x40000) != 0))
                    {
                        if ((AnimVm.DAT_800b305a & 1) == 0)
                        {
                            uVar4 = PsxRam.ReadU8(iVar7 + 0x143);
                            uVar5 = PsxRam.ReadU8(iVar7 + 0x142);
                        }
                        else
                        {
                            uVar4 = 0;
                            uVar5 = 0;
                        }

                        sVar11 = (short)(sVar11 + 1);
                        FUN_8006bdd8(0x11, uVar4, uVar5);
                        iVar2 = SoundState.DAT_8008d284;
                        iVar9 = (iVar10 << 0x10) >> 0xf;
                        iVar3 = iVar9 + SoundState.DAT_8008d284;
                        if ((short)PsxRam.ReadU16(iVar3 + 0x160) == 0)
                        {
                            PsxRam.WriteU16(iVar3 + 0x160, 1);
                            DAT_8008d210 = (short)(DAT_8008d210 + 1);
                            if (0x14 < DAT_8008d210)
                            {
                                DAT_8008d210 = 0x12;
                            }

                            FUN_8006b4a0(
                                DAT_8008d210,
                                (short)PsxRam.ReadU16(iVar2 + SoundState.AbtlVabHandle),
                                PsxRam.ReadU8(Dat80084c50Address),
                                PsxRam.ReadU8(Dat80084c51Address),
                                PsxRam.ReadU8(Dat80084c52Address),
                                0,
                                0xff,
                                0xff);
                            FUN_8006bdd8(
                                DAT_8008d210,
                                PsxRam.ReadU8(iVar7 + 0x143),
                                PsxRam.ReadU8(iVar7 + 0x142));
                        }
                        else
                        {
                            if (((uint)PsxRam.ReadI32(iVar6 + 0x138) & 0x40000) == 0)
                            {
                                if ((short)PsxRam.ReadU16(iVar3 + 0x160) == 4)
                                {
                                    FUN_8006b4a0(
                                        0x11,
                                        (short)PsxRam.ReadU16(
                                            SoundState.DAT_8008d284 + SoundState.AbtlVabHandle),
                                        PsxRam.ReadU8(Dat80084c54Address),
                                        PsxRam.ReadU8(Dat80084c55Address),
                                        PsxRam.ReadU8(Dat80084c56Address),
                                        0,
                                        0xff,
                                        0xff);
                                    FUN_8006bdd8(
                                        0x11,
                                        PsxRam.ReadU8(iVar7 + 0x143),
                                        PsxRam.ReadU8(iVar7 + 0x142));
                                }
                                else if ((short)PsxRam.ReadU16(iVar3 + 0x178) != 0)
                                {
                                    PsxRam.WriteU16(iVar3 + 0x160, 4);
                                    PsxRam.WriteU16(iVar3 + 0x178, 0);
                                }
                            }
                            else if ((short)PsxRam.ReadU16(iVar3 + 0x178) == 0)
                            {
                                FUN_8006b4a0(
                                    0x11,
                                    (short)PsxRam.ReadU16(
                                        SoundState.DAT_8008d284 + SoundState.AbtlVabHandle),
                                    PsxRam.ReadU8(Dat80084c5cAddress),
                                    PsxRam.ReadU8(Dat80084c5dAddress),
                                    PsxRam.ReadU8(Dat80084c5eAddress),
                                    0,
                                    0xff,
                                    0xff);
                                FUN_8006bdd8(
                                    0x11,
                                    PsxRam.ReadU8(iVar7 + 0x143),
                                    PsxRam.ReadU8(iVar7 + 0x142));
                                iVar9 = iVar9 + SoundState.DAT_8008d284;
                                PsxRam.WriteU16(
                                    iVar9 + 0x178,
                                    unchecked((ushort)((short)PsxRam.ReadU16(iVar9 + 0x178) + 1)));
                            }

                            iVar2 = ((iVar10 << 0x10) >> 0xf) + SoundState.DAT_8008d284;
                            PsxRam.WriteU16(
                                iVar2 + 0x160,
                                unchecked((ushort)((short)PsxRam.ReadU16(iVar2 + 0x160) + 1)));
                        }
                    }
                    else
                    {
                        // iVar2 still holds the SHIFTED index here -- this arm never overwrites it
                        // -- so "(iVar2 >> 0x10) * 2" is the slot's byte offset, the same
                        // iVar10 * 2 the other arm reaches through iVar9.
                        iVar2 = (iVar2 >> 0x10) * 2 + SoundState.DAT_8008d284;
                        PsxRam.WriteU16(iVar2 + 0x160, 0);
                        PsxRam.WriteU16(iVar2 + 0x178, 0);
                    }
                }

                iVar10 = iVar10 + 1;
                iVar2 = iVar10 * 0x10000;
            }
            while (iVar10 * 0x10000 >> 0x10 < 0xc);

            if (sVar11 == 0)
            {
                FUN_8006b88c(0x11);
            }
        }

        if (((BattleScene.DAT_8008d340 & 4) != 0) && ((BattleScene.DAT_8008d340 & 0x32) == 0))
        {
            BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 & 0xfffffffb | 8;
        }

        if ((BattleScene.DAT_8008d340 & 8) != 0)
        {
            FUN_8005f660();
        }

        SoundEffects.FUN_80060478();
        if ((short)PsxRam.ReadU16(iVar7 + 0x190) != 0)
        {
            // The callee's second parameter is `uint` in the image (SoundEffects.cs's own note
            // decodes it), so the halfword is widened rather than sign-extended into it.
            SoundEffects.FUN_8006071c(
                (short)PsxRam.ReadU16(iVar7 + 0x190), PsxRam.ReadU16(iVar7 + 0x192));
        }
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: LAB_8005de64 in FUN_8005da78, the two-instruction tail cases 1, 5 and 0xC all jump
    // to -- "CdIntToPos(iVar2, pCVar8); CdControl(2, (u_char *)(iVar7 + 0x30), auStack_48);".
    // Extracted because three arms of one switch land on the same instruction and C# has no goto
    // into a switch section. NOTE THE ASYMMETRY, which is the original's: CdIntToPos writes through
    // pCVar8, whatever that currently is, while CdControl always reads +0x30.
    private static void SeekTailDa78(int workspace, int pCVar8, int lba)
    {
        LibCd.CdIntToPos(lba, s_loc);
        StoreLoc(pCVar8, s_loc);
        LoadLoc(workspace + 0x30, s_loc);
        LibCd.CdControl(2, s_loc, s_result48);
    }

    // GHIDRA: FUN_8005e1ec @ 0x8005E1EC (VS.EXE)
    // 1696 bytes, seventeen callees, decompiled in full (372 lines). THE BGM SEQUENCE MACHINE: one
    // step per call, state in and state out, driven only by FUN_8005da78 at 0x8005DB00 under
    // "if ((*(ushort *)(ws + 0x10c) & 1) == 0)".
    //
    // THE SIGNATURE. Ghidra types it "int FUN_8005e1ec(CdlLOC *param_1, ushort param_2, short
    // param_3)" and param_1 is the WORKSPACE BASE, not a CdlLOC -- the caller passes iVar7, the
    // task context. Ghidra typed it CdlLOC* because the first four bytes of the workspace ARE a
    // CdlLOC (the machine's own seek position), and everything else it touches is then printed as
    // "param_1[n].field", i.e. byte n * 4 + the field's own 0..3. That indexing is unwound below
    // into plain workspace byte offsets, which is what the instructions actually encode. The
    // mapping was checked against three independent anchors before any of it was trusted:
    //     param_1 + 6      -> ws + 0x18  = SoundState.BgmBankSlot, the \SOUND\BGM.B CdlFILE
    //     param_1[0x42].sector/.track  -> ws + 0x10A = SoundState.BgmVabHandle, which the init
    //                                     writes with SsVabOpenHeadSticky's return
    //     param_1[0x43].minute/.second -> ws + 0x10C, the very halfword the caller masks with 1
    // All three land on fields this port already had names for. That is the confirmation.
    //
    // param_3 IS A "DRY RUN" FLAG. Every side effect in the machine -- the CdControls, the CdReads,
    // the SsVabOpenHeadSticky -- sits under "if (param_3 == 0)". Its only caller passes 0, so the
    // whole machine runs; the flag is carried anyway rather than folded away.
    //
    // THE RETURN IS THE NEXT STATE, sign-extended from 16 bits by the "iVar30 >> 0x10" on an
    // "iVar30 = state << 0x10" -- which is why the arms that want to return WITHOUT changing the
    // state assign "(uint)param_2 << 0x10" and jump to the exit. Reproduced as an explicit local so
    // the two exits stay distinguishable: some arms return a state they never wrote into param_2.
    //
    // THE DIVISIONS IN CASE 0x15 CAN TRAP, and that is preserved. The original emits an explicit
    // "trap(0x1c00)" for a zero divisor and "trap(0x1800)" for the -1 / INT_MIN overflow, which is
    // just MIPS spelling out what the divide instruction does. C# throws DivideByZeroException and
    // OverflowException for exactly those two cases, so the plain division below reproduces both
    // faults without a hand-written check -- and, importantly, without swallowing them.
    internal static int FUN_8005e1ec(int param_1, ushort param_2, short param_3)
    {
        short sVar27;
        ushort uVar31;
        int iVar25;
        int iVar28;
        int iVar29;
        int iVar30;
        int puVar33;
        short sVar32;
        short sVar34;
        short sVar35;
        byte uVar26;

        int p = param_1 + 6 * 4;

        switch (param_2)
        {
            case 1:
                if (param_3 == 0)
                {
                    LoadLoc(p, s_loc);
                    iVar30 = LibCd.CdPosToInt(s_loc);
                    LibCd.CdIntToPos(iVar30 + 0xa0, s_loc);
                    StoreLoc(param_1, s_loc);
                    LibCd.CdControl(2, s_loc, s_result48);
                }

                // 0x20000 is state 2 already shifted left by 16: this arm returns 2 without ever
                // writing param_2. The same shape recurs at 0x40000, 0x70000, 0xD0000 and 0xF0000.
                return 0x20000 >> 0x10;

            case 2:
                iVar28 = LibCd.CdSync(1, s_result48);
                iVar30 = (int)((uint)param_2 << 0x10);
                if (iVar28 == 0)
                {
                    return iVar30 >> 0x10;
                }

                param_2 = 3;
                break;

            case 3:
                if (param_3 == 0)
                {
                    PsxRam.WriteU8(param_1 + 4, 6);
                    PsxRam.WriteU8(param_1 + 5, 0);
                    PsxRam.WriteU8(param_1 + 6, 0);
                    PsxRam.WriteU8(param_1 + 7, 0);
                    LibCd.CdRead(6, Dat801b6000Address, 0x80);
                }

                return 0x40000 >> 0x10;

            case 4:
                iVar28 = LibCd.CdReadSync(1, s_result48);
                iVar30 = (int)((uint)param_2 << 0x10);
                if (iVar28 != 0)
                {
                    return iVar30 >> 0x10;
                }

                param_2 = 5;
                break;

            case 5:
                if (param_3 == 0)
                {
                    sVar27 = LibSnd.SsVabOpenHeadSticky(Dat801b6000Address, -1, 0x1800);
                    PsxRam.WriteU16(param_1 + 0x10a, unchecked((ushort)sVar27));
                    if (sVar27 < 0)
                    {
                        // printf("SsVabOpenHead [2]: Can NOT open header [%d].\n") -- the original
                        // passes no argument for the %d either; the format string wants one and the
                        // call site supplies none. Reproduced as the bug it is, message and all.
                        Console.WriteLine("SsVabOpenHead [2]: Can NOT open header [%d].");
                        DAT_8008d216 = (short)(DAT_8008d216 + 1);
                        iVar30 = (int)((uint)param_2 << 0x10);
                        if (0x20 < DAT_8008d216)
                        {
                            return 0;
                        }

                        return iVar30 >> 0x10;
                    }

                    DAT_8008d216 = 0;
                    PsxRam.WriteU8(param_1 + 4, 0xa0);
                    PsxRam.WriteU8(param_1 + 5, 0);
                    PsxRam.WriteU8(param_1 + 6, 0);
                    PsxRam.WriteU8(param_1 + 7, 0);
                    PsxRam.WriteU8(param_1 + 0, PsxRam.ReadU8(p + 0));
                    PsxRam.WriteU8(param_1 + 1, PsxRam.ReadU8(p + 1));
                    PsxRam.WriteU8(param_1 + 2, PsxRam.ReadU8(p + 2));
                    PsxRam.WriteU8(param_1 + 3, PsxRam.ReadU8(p + 3));
                }

                param_2 = 6;
                break;

            case 6:
                if (param_3 == 0)
                {
                    LoadLoc(param_1, s_loc);
                    LibCd.CdControl(2, s_loc, s_result48);
                }

                return 0x70000 >> 0x10;

            case 7:
                iVar28 = LibCd.CdSync(1, s_result48);
                iVar30 = (int)((uint)param_2 << 0x10);
                if (iVar28 == 0)
                {
                    return iVar30 >> 0x10;
                }

                param_2 = 8;
                break;

            case 8:
                // Unconditional -- case 8 has no param_3 guard, unlike its neighbours.
                LibCd.CdRead(8, FileIo.g_cdFileBufferTableAddress, 0x80);
                param_2 = 9;
                break;

            case 9:
                iVar28 = LibCd.CdReadSync(1, s_result48);
                iVar30 = (int)((uint)param_2 << 0x10);
                if (iVar28 != 0)
                {
                    return iVar30 >> 0x10;
                }

                param_2 = 10;
                break;

            case 10:
                if (param_3 == 0)
                {
                    DAT_8008d270 = (short)PsxRam.ReadU16(param_1 + 0x10a);
                }

                DAT_8008d274 = LibSnd.SsVabTransBodyPartly(
                    FileIo.g_cdFileBufferTableAddress, 0x4000, DAT_8008d270);
                if ((DAT_8008d274 != DAT_8008d270) && (DAT_8008d274 != -2))
                {
                    iVar30 = (int)((uint)param_2 << 0x10);
                    if (DAT_8008d274 == -1)
                    {
                        Console.WriteLine("SsVabTransBodyPartly [2]: failed !!!");
                        iVar30 = (int)((uint)param_2 << 0x10);
                    }

                    return iVar30 >> 0x10;
                }

                param_2 = 0xb;
                break;

            case 0xb:
                sVar27 = LibSnd.SsVabTransCompleted(0);
                iVar30 = (int)((uint)param_2 << 0x10);
                if (sVar27 == 0)
                {
                    return iVar30 >> 0x10;
                }

                if (DAT_8008d274 == DAT_8008d270)
                {
                    // LAB_8005e624
                    param_2 = 0xc;
                }
                else
                {
                    param_2 = 6;
                    if (param_3 == 0)
                    {
                        LoadLoc(param_1, s_loc);
                        iVar30 = LibCd.CdPosToInt(s_loc);
                        LibCd.CdIntToPos(iVar30 + 8, s_loc);
                        StoreLoc(param_1, s_loc);

                        // goto LAB_8005e4c0, which is "param_2 = 6" -- already set above, so the
                        // jump changes nothing here. Kept visible rather than deleted.
                        param_2 = 6;
                    }
                }

                break;

            case 0xc:
                iVar30 = (int)((uint)param_2 << 0x10);
                if ((param_3 == 0) && ((BattleScene.DAT_8008d340 & 0x6a) == 0))
                {
                    uVar26 = PsxRam.ReadU8(param_1 + 0x113);
                    BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 | 0x10;
                    PsxRam.WriteU8(param_1 + 0x114, PsxRam.ReadU8(param_1 + 0x112));
                    PsxRam.WriteU8(param_1 + 0x115, uVar26);
                    LoadLoc(p, s_loc);
                    iVar30 = LibCd.CdPosToInt(s_loc);
                    short sVar1 = (short)PsxRam.ReadU16(param_1 + 0x112);
                    LibCd.CdIntToPos(
                        PsxRam.ReadI32(PtrDat8008481cAddress + sVar1 * 4) + iVar30, s_loc);
                    StoreLoc(param_1, s_loc);
                    LibCd.CdControl(2, s_loc, s_result48);
                    iVar30 = 0xd0000;
                }

                return iVar30 >> 0x10;

            case 0xd:
                iVar28 = LibCd.CdSync(1, s_result48);
                iVar30 = (int)((uint)param_2 << 0x10);
                if (iVar28 == 0)
                {
                    return iVar30 >> 0x10;
                }

                param_2 = 0xe;
                break;

            case 0xe:
                if (param_3 == 0)
                {
                    PsxRam.WriteU8(param_1 + 4, 6);
                    PsxRam.WriteU8(param_1 + 5, 0);
                    PsxRam.WriteU8(param_1 + 6, 0);
                    PsxRam.WriteU8(param_1 + 7, 0);
                    LibCd.CdRead(6, Dat801be000Address, 0x80);
                }

                return 0xf0000 >> 0x10;

            case 0xf:
                iVar28 = LibCd.CdReadSync(1, s_result48);
                iVar30 = (int)((uint)param_2 << 0x10);
                if (iVar28 != 0)
                {
                    return iVar30 >> 0x10;
                }

                // goto LAB_8005e684 with iVar30 = 0x10 -- the clear of bit 4 and a return of 0x10.
                iVar30 = 0x10;
                BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 & 0xffffffef;
                return (iVar30 << 0x10) >> 0x10;

            case 0x10:
            {
                ushort uVar19 = PsxRam.ReadU16(param_1 + 0x10c);
                uVar31 = PsxRam.ReadU16(param_1 + 0x108);
                PsxRam.WriteU16(param_1 + 0x10c, (ushort)(uVar19 & 0xfffd));
                if ((short)uVar31 < 0)
                {
                    short sVar2 = (short)PsxRam.ReadU16(param_1 + 0x112);
                    iVar30 = sVar2;
                    if (iVar30 < 8)
                    {
                        // The first eight entries of the table are POINTERS to the sequences
                        // already in RAM; SsSeqOpen is handed one of them.
                        puVar33 = PsxRam.ReadI32(PtrDat8008481cAddress + iVar30 * 4);
                    }
                    else
                    {
                        short sVar3 = (short)PsxRam.ReadU16(param_1 + 0x114);
                        if (iVar30 != sVar3)
                        {
                            // goto LAB_8005e624
                            param_2 = 0xc;
                            break;
                        }

                        puVar33 = Dat801be000Address;
                    }

                    short sVar4 = (short)PsxRam.ReadU16(param_1 + 0x10a);
                    param_2 = 0x11;
                    sVar27 = LibSnd.SsSeqOpen(puVar33, sVar4);
                    PsxRam.WriteU16(param_1 + 0x108, unchecked((ushort)sVar27));
                }
                else
                {
                    if ((uVar31 & 0x80) == 0)
                    {
                        // goto LAB_8005e6b8, inside case 0x12. The two statements it runs are
                        // repeated here rather than jumped to; that is a C# limitation, not a
                        // change of order -- nothing between the label and the switch exit is
                        // skipped, because the label IS the last thing case 0x12's else arm does.
                        LibSnd.SsSeqStop((short)uVar31);
                        ushort uVar21b = PsxRam.ReadU16(param_1 + 0x108);
                        PsxRam.WriteU16(param_1 + 0x108, (ushort)(uVar21b | 0x80));
                        break;
                    }

                    LibSnd.SsSetNck((short)(uVar31 & 0x7f));
                    PsxRam.WriteU16(param_1 + 0x108, 0xffff);
                }

                break;
            }

            case 0x11:
            {
                short sVar5 = (short)PsxRam.ReadU16(param_1 + 0x108);
                iVar30 = 0;
                if (-1 < sVar5)
                {
                    LibSnd.SsSeqPlay(sVar5, 1, 1);
                    short sVar6 = (short)PsxRam.ReadU16(param_1 + 0x108);
                    short sVar7 = (short)PsxRam.ReadU16(param_1 + 0x11a);
                    short sVar8 = (short)PsxRam.ReadU16(param_1 + 0x11c);
                    LibSnd.SsSeqSetVol(sVar6, sVar7, sVar8);
                }

                ushort uVar20 = PsxRam.ReadU16(param_1 + 0x10c);
                PsxRam.WriteU16(param_1 + 0x10c, (ushort)(uVar20 & 0xfffd));

                // LAB_8005e684
                BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 & 0xffffffef;
                return (iVar30 << 0x10) >> 0x10;
            }

            case 0x12:
                uVar31 = PsxRam.ReadU16(param_1 + 0x108);
                if (((short)uVar31 < 0) || ((uVar31 & 0x80) != 0))
                {
                    ushort uVar22 = PsxRam.ReadU16(param_1 + 0x10c);
                    param_2 = 0;
                    PsxRam.WriteU16(param_1 + 0x10c, (ushort)(uVar22 & 0xfffd));
                }
                else
                {
                    // LAB_8005e6b8
                    LibSnd.SsSeqStop((short)uVar31);
                    ushort uVar21 = PsxRam.ReadU16(param_1 + 0x108);
                    PsxRam.WriteU16(param_1 + 0x108, (ushort)(uVar21 | 0x80));
                }

                break;

            case 0x13:
                sVar32 = (short)PsxRam.ReadU16(param_1 + 0x108);
                param_2 = 0;
                if (-1 < sVar32)
                {
                    sVar34 = (short)PsxRam.ReadU16(param_1 + 0x11a);
                    sVar35 = (short)PsxRam.ReadU16(param_1 + 0x11c);

                    // LAB_8005e788 then LAB_8005e790
                    LibSnd.SsSeqSetVol(sVar32, sVar34, sVar35);
                    param_2 = 0;
                }

                break;

            case 0x14:
            {
                ushort uVar23 = PsxRam.ReadU16(param_1 + 0x10c);
                if ((uVar23 & 2) == 0)
                {
                    LibSnd.SsSeqPause((short)PsxRam.ReadU16(param_1 + 0x108));
                }
                else
                {
                    LibSnd.SsSeqReplay((short)PsxRam.ReadU16(param_1 + 0x108));
                }

                ushort uVar24 = PsxRam.ReadU16(param_1 + 0x10c);
                param_2 = 0;
                PsxRam.WriteU16(param_1 + 0x10c, (ushort)(uVar24 ^ 2));
                break;
            }

            case 0x15:
                sVar27 = (short)PsxRam.ReadU16(param_1 + 0x126);
                iVar30 = (short)PsxRam.ReadU16(param_1 + 0x128);
                if (sVar27 == iVar30)
                {
                    uVar26 = PsxRam.ReadU8(param_1 + 0x123);
                    sVar35 = (short)PsxRam.ReadU16(param_1 + 0x124);
                    sVar32 = (short)PsxRam.ReadU16(param_1 + 0x108);
                    PsxRam.WriteU8(param_1 + 0x11a, PsxRam.ReadU8(param_1 + 0x122));
                    PsxRam.WriteU8(param_1 + 0x11b, uVar26);
                    PsxRam.WriteU8(param_1 + 0x11c, (byte)sVar35);
                    PsxRam.WriteU8(param_1 + 0x11d, (byte)((ushort)sVar35 >> 8));
                    if (-1 < sVar32)
                    {
                        sVar34 = (short)PsxRam.ReadU16(param_1 + 0x11a);

                        // LAB_8005e788
                        LibSnd.SsSeqSetVol(sVar32, sVar34, sVar35);
                    }

                    // LAB_8005e790
                    param_2 = 0;
                    break;
                }

                {
                    short sVar12 = (short)PsxRam.ReadU16(param_1 + 0x122);
                    iVar28 = (sVar12 - (short)PsxRam.ReadU16(param_1 + 0x11e)) * sVar27;

                    short sVar13 = (short)PsxRam.ReadU16(param_1 + 0x120);
                    short sVar14 = (short)PsxRam.ReadU16(param_1 + 0x124);
                    short sVar15 = (short)PsxRam.ReadU16(param_1 + 0x126);
                    iVar25 = (sVar14 - sVar13) * sVar15;
                    short sVar16 = (short)PsxRam.ReadU16(param_1 + 0x128);
                    iVar29 = sVar16;
                    short sVar17 = (short)PsxRam.ReadU16(param_1 + 0x108);

                    // Both divisions can fault exactly where the original traps; see the header.
                    PsxRam.WriteU16(
                        param_1 + 0x11a,
                        unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x11e)
                            + (short)(iVar28 / iVar30))));
                    iVar30 = sVar13 + iVar25 / iVar29;
                    PsxRam.WriteU16(param_1 + 0x11c, unchecked((ushort)iVar30));
                    if (-1 < sVar17)
                    {
                        short sVar18 = (short)PsxRam.ReadU16(param_1 + 0x11a);
                        LibSnd.SsSeqSetVol(
                            sVar17, sVar18, (short)((uint)(iVar30 * 0x10000) >> 0x10));
                    }

                    PsxRam.WriteU16(
                        param_1 + 0x126,
                        unchecked((ushort)((short)PsxRam.ReadU16(param_1 + 0x126) + 1)));
                }

                break;
        }

        // switchD_8005e244_caseD_0: also the destination of state 0 and of every state above 0x15.
        return (int)((uint)param_2 << 0x10) >> 0x10;
    }

    // GHIDRA: FUN_8005f068 @ 0x8005F068 (VS.EXE)
    // 1048 bytes, ten callees, decompiled in full (122 lines). THE PER-VOICE ATB BANK LOADER: a
    // seven-state machine whose state lives in the SHARED workspace gate at +0x12A -- the same
    // halfword SoundCdLoadStep's state 0 waits on, which is why only one of the two can be running.
    //
    // ITS LOOP IS THE POINT. The whole switch sits inside "do { ... if (param_3 != 0) return 1; }
    // while (true)": with param_3 non-zero it runs ONE state per call and reports 1 for "still
    // busy"; with param_3 zero it spins until state 6 completes and returns 0. Its only caller,
    // FUN_8005f660, passes 1, so the spin never happens on this port either.
    //
    // THE THREE INDEX FORMS, all read off the instructions rather than inferred:
    //   iVar6 = param_2                the voice slot, 0..5
    //   iVar7 = param_2 * 2            its halfword offset into the +0x12C / +0x148 arrays
    //   iVar8 = (short)param_1 * 2     the sound id doubled, indexing DAT_80084bc0
    // and the per-slot CD buffer is "&DAT_801c4000 + iVar6 * 0x1000", one 0x1000 block per slot.
    //
    // THE STATE ADVANCES BY +1 AND REGRESSES BY -1, never by assignment, except for the two hard
    // resets to 0 in states 4 and 5. There is no default arm: a state outside 0..6 simply falls to
    // the param_3 test, which is what makes an out-of-range gate value hang a param_3 == 0 call.
    // Reproduced.
    internal static int FUN_8005f068(ushort param_1, short param_2, short param_3)
    {
        // Ghidra's own "int iVar2" is not declared here: in the decompilation it exists only inside
        // the two shared countdown labels, which are the two helpers below, and it is declared
        // there instead. Nothing else in this body reads it.
        ushort uVar3;
        short sVar4;
        int iVar5;
        int iVar6;
        int iVar7;
        int iVar8;

        iVar6 = param_2;
        iVar7 = iVar6 * 2;
        iVar8 = (int)((uint)param_1 << 0x10) >> 0xf;
        do
        {
            switch (PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.Gate12A))
            {
                case 0:
                    sVar4 = (short)PsxRam.ReadU16(
                        iVar7 + SoundState.DAT_8008d284 + SoundState.VoiceHandles);
                    if (-1 < sVar4)
                    {
                        LibSnd.SsVabClose(sVar4);
                        PsxRam.WriteU16(
                            iVar7 + SoundState.DAT_8008d284 + SoundState.VoiceHandles, 0xffff);
                    }

                    LoadLoc(SoundState.DAT_8008d284 + SoundState.AtbBankSlot, s_loc);
                    iVar5 = LibCd.CdPosToInt(s_loc);
                    LibCd.CdIntToPos(
                        iVar5 + PsxRam.ReadU16(Dat80084bc0Address + iVar8), s_loc);
                    StoreLoc(SoundState.DAT_8008d284 + 0x78, s_loc);
                    LibCd.CdControl(2, s_loc, s_result);
                    iVar5 = SoundState.DAT_8008d284;
                    PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.RetryCountdown, 10);
                    PsxRam.WriteU16(
                        iVar5 + SoundState.Gate12A,
                        unchecked((ushort)((short)PsxRam.ReadU16(iVar5 + SoundState.Gate12A) + 1)));
                    break;

                case 1:
                    iVar5 = LibCd.CdSync(1, s_result);
                    if (iVar5 == 2)
                    {
                        PsxRam.WriteI32(SoundState.DAT_8008d284 + 0x7c, 2);
                        LibCd.CdRead(2, Dat801c4000Address + iVar6 * 0x1000, 0x80);
                        iVar5 = SoundState.DAT_8008d284;
                        AdvanceGateAndArm(iVar5);
                        break;
                    }

                    // LAB_8005f268, shared with state 3's non-ready arm.
                    SyncFailedCountdown(iVar5);
                    break;

                case 2:
                    iVar5 = LibCd.CdReadSync(1, s_result);
                    if (iVar5 == 0)
                    {
                        LoadLoc(SoundState.DAT_8008d284 + SoundState.AtbBankSlot, s_loc);
                        iVar5 = LibCd.CdPosToInt(s_loc);
                        LibCd.CdIntToPos(
                            iVar5 + PsxRam.ReadU16(Dat80084bc0Address + iVar8) + 2, s_loc);
                        StoreLoc(SoundState.DAT_8008d284 + 0x78, s_loc);
                        LibCd.CdControl(2, s_loc, s_result);
                        iVar5 = SoundState.DAT_8008d284;
                        AdvanceGateAndArm(iVar5);
                        break;
                    }

                    // LAB_8005f338, shared with state 4's non-ready arm. Note it REGRESSES the
                    // gate by one on expiry, where LAB_8005f268 leaves it alone.
                    ReadSyncFailedCountdown(iVar5);
                    break;

                case 3:
                    iVar5 = LibCd.CdSync(1, s_result);
                    if (iVar5 != 2)
                    {
                        SyncFailedCountdown(iVar5);
                        break;
                    }

                    PsxRam.WriteI32(SoundState.DAT_8008d284 + 0x7c, 10);
                    LibCd.CdRead(10, FileIo.g_cdFileBufferTableAddress, 0x80);
                    iVar5 = SoundState.DAT_8008d284;
                    AdvanceGateAndArm(iVar5);
                    break;

                case 4:
                    iVar5 = LibCd.CdReadSync(1, s_result);
                    if (iVar5 != 0)
                    {
                        ReadSyncFailedCountdown(iVar5);
                        break;
                    }

                    uVar3 = unchecked((ushort)LibSnd.SsVabOpenHeadSticky(
                        Dat801c4000Address + iVar6 * 0x1000,
                        -1,
                        unchecked((uint)(iVar6 * 0x5000 + 0x5e000))));
                    iVar5 = SoundState.DAT_8008d284;
                    PsxRam.WriteU16(
                        iVar7 + SoundState.DAT_8008d284 + SoundState.VoiceHandles, uVar3);
                    if (-1 < (int)((uint)uVar3 << 0x10))
                    {
                        AdvanceGateAndArm(iVar5);
                        break;
                    }

                    sVar4 = (short)PsxRam.ReadU16(iVar5 + SoundState.RetryCountdown);
                    PsxRam.WriteU16(
                        iVar5 + SoundState.RetryCountdown, unchecked((ushort)(sVar4 - 1)));
                    if (sVar4 == 1)
                    {
                        PsxRam.WriteU16(iVar5 + SoundState.RetryCountdown, 10);
                        PsxRam.WriteU16(iVar5 + SoundState.Gate12A, 0);
                    }

                    break;

                case 5:
                    sVar4 = LibSnd.SsVabTransBody(
                        FileIo.g_cdFileBufferTableAddress,
                        (short)PsxRam.ReadU16(
                            iVar7 + SoundState.DAT_8008d284 + SoundState.VoiceHandles));
                    iVar5 = SoundState.DAT_8008d284;
                    if (sVar4 != -1)
                    {
                        AdvanceGateAndArm(iVar5);
                        break;
                    }

                    sVar4 = (short)PsxRam.ReadU16(
                        SoundState.DAT_8008d284 + SoundState.RetryCountdown);
                    PsxRam.WriteU16(
                        SoundState.DAT_8008d284 + SoundState.RetryCountdown,
                        unchecked((ushort)(sVar4 - 1)));
                    if (sVar4 == 1)
                    {
                        // The two stores are in the opposite order to state 4's; kept as found.
                        PsxRam.WriteU16(iVar5 + SoundState.Gate12A, 0);
                        PsxRam.WriteU16(iVar5 + SoundState.RetryCountdown, 10);
                    }

                    break;

                case 6:
                    sVar4 = LibSnd.SsVabTransCompleted(0);
                    iVar5 = SoundState.DAT_8008d284;
                    if (sVar4 == 1)
                    {
                        PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.Gate12A, 0);
                        PsxRam.WriteU16(iVar7 + iVar5 + SoundState.VoiceFlags, param_1);
                        return 0;
                    }

                    break;
            }

            if (param_3 != 0)
            {
                return 1;
            }
        }
        while (true);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: code_r0x8005f3fc in FUN_8005f068 -- "*(iVar5 + 0x13c) = 10; *(iVar5 + 0x12a) += 1;"
    // -- the two-instruction tail states 1, 2, 3, 4 and 5 all jump to on success.
    private static void AdvanceGateAndArm(int workspace)
    {
        PsxRam.WriteU16(workspace + SoundState.RetryCountdown, 10);
        PsxRam.WriteU16(
            workspace + SoundState.Gate12A,
            unchecked((ushort)((short)PsxRam.ReadU16(workspace + SoundState.Gate12A) + 1)));
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: LAB_8005f268 in FUN_8005f068, shared by states 1 and 3. On CdlDiskError only, tick
    // the countdown down; when the value BEFORE the decrement was 1 -- the same condition as the
    // decremented value reaching 0 -- reissue CdlSetloc from +0x78 and re-arm. The gate is NOT
    // moved, so the same state runs again.
    private static void SyncFailedCountdown(int status)
    {
        int iVar2 = SoundState.DAT_8008d284;
        if (status != 5)
        {
            return;
        }

        short sVar4 = (short)PsxRam.ReadU16(
            SoundState.DAT_8008d284 + SoundState.RetryCountdown);
        PsxRam.WriteU16(
            SoundState.DAT_8008d284 + SoundState.RetryCountdown,
            unchecked((ushort)(sVar4 - 1)));
        if (sVar4 != 1)
        {
            return;
        }

        LoadLoc(iVar2 + 0x78, s_loc);
        LibCd.CdControl(2, s_loc, s_result);
        PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.RetryCountdown, 10);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: LAB_8005f338 in FUN_8005f068, shared by states 2 and 4. Same shape as the one
    // above but keyed on CdReadSync's -1 rather than CdSync's 5, and it REGRESSES the gate by one
    // on expiry. That difference is the whole reason the original has two labels and not one.
    private static void ReadSyncFailedCountdown(int status)
    {
        int iVar2 = SoundState.DAT_8008d284;
        if (status != -1)
        {
            return;
        }

        short sVar4 = (short)PsxRam.ReadU16(
            SoundState.DAT_8008d284 + SoundState.RetryCountdown);
        PsxRam.WriteU16(
            SoundState.DAT_8008d284 + SoundState.RetryCountdown,
            unchecked((ushort)(sVar4 - 1)));
        if (sVar4 != 1)
        {
            return;
        }

        LoadLoc(iVar2 + 0x78, s_loc);
        LibCd.CdControl(2, s_loc, s_result);
        int iVar5 = SoundState.DAT_8008d284;
        PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.RetryCountdown, 10);
        PsxRam.WriteU16(
            iVar5 + SoundState.Gate12A,
            unchecked((ushort)((short)PsxRam.ReadU16(iVar5 + SoundState.Gate12A) - 1)));
    }

    // GHIDRA: FUN_8005f480 @ 0x8005F480 (VS.EXE)
    // 176 bytes, two callees, decompiled in full (22 lines). ONE incoming reference, an
    // unconditional call at 0x80055BFC from a function this port has not transliterated.
    //
    // KEY ONE ALREADY-LOADED VOICE ON. param_1 is the voice slot (doubled by the same
    // "sll 0x10 / sra 0xf" pair as everywhere else in this family) and param_2 is a note offset:
    // the fifth argument to FUN_8006b4a0 is "(param_2 * 2 + 0x24)" truncated to 16 bits, which is
    // the note, while param_2 itself goes in as the fourth. The slot's handle at +0x12C must be
    // non-negative -- i.e. the bank must already be open -- or the function reports -1 and does
    // nothing at all.
    //
    // Returns 0 on success and -1 on "no voice loaded in that slot"; Ghidra types it undefined4 and
    // the caller is not transliterated, so int is what the value is, not what it means.
    internal static int FUN_8005f480(int param_1, short param_2)
    {
        int uVar1;
        int iVar2;

        iVar2 = ((param_1 << 0x10) >> 0xf) + SoundState.DAT_8008d284;
        uVar1 = -1;
        if (-1 < (short)PsxRam.ReadU16(iVar2 + SoundState.VoiceHandles))
        {
            DAT_8008d212 = (short)(DAT_8008d212 + 1);
            if (0x16 < DAT_8008d212)
            {
                DAT_8008d212 = 0x15;
            }

            FUN_8006b4a0(
                DAT_8008d212,
                (short)PsxRam.ReadU16(iVar2 + SoundState.VoiceHandles),
                0,
                param_2,
                (short)((param_2 * 2 + 0x24) * 0x10000 >> 0x10),
                0,
                0xff,
                0xff);
            FUN_8006bdd8(DAT_8008d212, 0x38, 0x38);
            uVar1 = 0;
        }

        return uVar1;
    }

    // GHIDRA: FUN_8005f660 @ 0x8005F660 (VS.EXE)
    // 164 bytes, one callee, decompiled in full (28 lines). ONE caller: FUN_8005da78 at 0x8005E198,
    // under "if ((DAT_8008d340 & 8) != 0)".
    //
    // THE VOICE-FLAG SWEEP. It walks the six halfwords at +0x148 looking for the first one with bit
    // 0x80 set -- the "this slot wants loading" mark that SoundCdLoadStep's state 1 sets on every
    // slot it releases -- and hands the low seven bits of it to FUN_8005f068 as the sound id, one
    // step. It BREAKS on the first such slot, so at most one load is driven per frame.
    //
    // THE CLEAR IS GUARDED BY BOTH CONDITIONS, and the pair is exact: bit 3 of DAT_8008d340 is
    // cleared only when the loop ran to completion (iVar4 == 6, i.e. no slot wanted loading) AND
    // iVar3 is still 1. iVar3 starts at 1 and is only ever overwritten by FUN_8005f068's return,
    // which is 1 for "still busy" and 0 for "done" -- but the break means a slot that returns 0
    // leaves iVar4 < 6, so the second test is the one that can never independently fail. Both are
    // reproduced anyway.
    internal static void FUN_8005f660()
    {
        ushort uVar1;
        int iVar2;
        int iVar3;
        int iVar4;

        iVar3 = 1;
        iVar4 = 0;
        do
        {
            iVar2 = iVar4 * 2 + SoundState.DAT_8008d284;
            if ((short)PsxRam.ReadU16(iVar2 + SoundState.VoiceFlags) != 0)
            {
                uVar1 = PsxRam.ReadU16(iVar2 + SoundState.VoiceFlags);
                if ((uVar1 & 0x80) != 0)
                {
                    iVar3 = FUN_8005f068((ushort)(uVar1 & 0x7f), (short)iVar4, 1);
                    break;
                }
            }

            iVar4 = iVar4 + 1;
        }
        while (iVar4 < 6);

        if ((iVar4 == 6) && (iVar3 == 1))
        {
            BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 & 0xfffffff7;
        }
    }

    // ==========================================================================================
    // The callees this family reaches that are NOT transliterated. None is invented; each stub
    // carries its Ghidra address, its byte size and the exact call shapes seen above.
    // ==========================================================================================

    // GHIDRA: FUN_8006bcd0 @ 0x8006BCD0 (VS.EXE)
    // BLOCKED: 124 bytes, and it is SDK, not game code -- 0x8006BCD0 is above the 0x800632C4 line
    // the mandate draws, so rule 13 forbids transliterating it here. It is a libsnd voice setter,
    // called six times by the init as (voice, 0x7FFF, 0x7FFF) for voices 0x11..0x16, i.e. "set both
    // volumes to maximum". PsxSdkMonogame exports no entry at this address under any name, so there
    // is nothing to call either; the volumes are simply never applied.
    internal static void FUN_8006bcd0(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FUN_8006bdd8 @ 0x8006BDD8 (VS.EXE)
    // BLOCKED: 156 bytes, SDK by the same address rule. AnimCmdSound.cs already records its shape
    // from the other side of the module -- "(ushort voice, short volL, short volR) -> int" -- and
    // the eight call sites in this file agree: (0x11..0x16, 0x40, 0x40) and (voice, 0x38, 0x38)
    // from the init, (voice, ws+0x143, ws+0x142) from the per-frame sweep, (cursor, 0x38, 0x38)
    // from FUN_8005f480. It is the per-voice volume/pan setter. Not exported by PsxSdkMonogame.
    internal static int FUN_8006bdd8(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
        return 0;
    }

    // GHIDRA: FUN_8006de64 @ 0x8006DE64 (VS.EXE)
    // BLOCKED: 16 bytes -- four instructions -- and SDK by address. Called once by the init as
    // FUN_8006de64(0), between SsSetTickMode(1) and SsSetReservedVoice(0x11). Its position in that
    // sequence and its size make it one of libsnd's small mode setters, but WHICH one is not closed
    // and is not guessed here.
    private static void FUN_8006de64(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8006b4a0 @ 0x8006B4A0 (VS.EXE)
    // BLOCKED: 1004 bytes, SDK by address, and the biggest single gap in this slice. It is libsnd's
    // KEY-ON: eight arguments, and both call shapes here are consistent with
    // (voice, vabId, prog, tone, note, fine, volL, volR) --
    //     FUN_8005da78: (DAT_8008d210, ws+0x154, DAT_80084c50, DAT_80084c51, DAT_80084c52,
    //                    0, 0xff, 0xff)   and twice more against 0x11 with the +0x04 / +0x0C triples
    //     FUN_8005f480: (DAT_8008d212, ws+0x12C, 0, param_2, (param_2 * 2 + 0x24), 0, 0xff, 0xff)
    // AnimCmdSound.cs names it "the key-ON counterpart" of FUN_8006b88c from its own side of the
    // module, independently. The argument NAMES above are the shape the call sites impose, not a
    // closed reading of the body, so nothing downstream should rely on them.
    internal static int FUN_8006b4a0(
        int param_1, int param_2, int param_3, int param_4,
        int param_5, int param_6, int param_7, int param_8)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
        _ = param_4;
        _ = param_5;
        _ = param_6;
        _ = param_7;
        _ = param_8;
        return 0;
    }

    // GHIDRA: FUN_8006b88c @ 0x8006B88C (VS.EXE)
    // BLOCKED: 280 bytes, SDK by address. The KEY-OFF, called once per frame as FUN_8006b88c(0x11)
    // when the twelve-slot sweep found nothing to sound. AnimCmdSound.cs records the same
    // "(ushort voice) -> int" shape and the same role from its own call sites.
    internal static int FUN_8006b88c(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_80060364 @ 0x80060364 (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/SoundEffects.cs. The call sites in this file
    // reach it by qualified name; an empty stub in the enclosing class silently beats a real
    // body elsewhere.

    // GHIDRA: FUN_80060478 @ 0x80060478 (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/SoundEffects.cs. The call sites in this file
    // reach it by qualified name; an empty stub in the enclosing class silently beats a real
    // body elsewhere.

    // GHIDRA: FUN_8006071c @ 0x8006071C (VS.EXE)
    // NO LONGER DECLARED HERE. Closed in VS_EXE/SoundEffects.cs. The call sites in this file
    // reach it by qualified name; an empty stub in the enclosing class silently beats a real
    // body elsewhere.

}
