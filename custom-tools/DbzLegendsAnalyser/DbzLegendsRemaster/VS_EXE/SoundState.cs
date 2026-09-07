namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's sound task: the globals its driver module shares, and the layout of the workspace they
// all reach through. Nothing here is behaviour -- it is the one place the sound module's state is
// named, so that the functions of that module can be transliterated without each one inventing
// names for the same offsets. Tranche 4 calls this the foundation, and it exists because skipping
// it once already produced twelve duplicated symbols and seven divergent types.
//
// EVIDENCE. Ghidra was unreachable, so every address and width below was read out of the running
// image through PCSX-Redux's debugger, with gp = 0x8008D0FC resolving each `0xNNN(gp)` access. The
// channel was checked before being trusted: FUN_80061ed8 @ 0x80061ED8 disassembles to exactly the
// 68-byte CdSearchFile retry loop this port already documents.
//
// ONE LIMIT, STATED PLAINLY: the emulator was halted on the exception vector (PC 0x80000080) with
// the workspace still zeroed, so this is STATIC evidence -- what the code stores -- and not a
// reading of live values. Field SIZES and WIDTHS come from the instructions, which is sound; field
// MEANINGS come from how they are used, and the uncertain ones say so rather than guess.
internal static class SoundState
{
    // GHIDRA: DAT_8008d284 @ 0x8008D284 (VS.EXE)
    // THE WORKSPACE POINTER, and it is not a buffer of its own: it is the sound task's CONTEXT.
    // FUN_8005d25c @ 0x8005D25C reads it out of the task block with `lw s0, 0x0008(v0)` where
    // v0 = *(0x8008D16C), then publishes it here with `sw s0, 0x0188(gp)` at 0x8005D284. The task
    // block's own layout is visible at the same time: +0x00 = 0x57 (the task id tranche 4 names),
    // +0x04 = 0x8005D1FC (the dispatcher), +0x08 = this pointer.
    //
    // So the storage belongs to the heap that CreateTask allocated it from, and every access below
    // goes through PsxRam at `Workspace + offset`. That is deliberate and follows this port's own
    // precedent for such regions (BattleScene's 0x800990C0): the original writes the workspace at
    // byte, halfword and word widths, and one region is one storage. Sixty C# scalars would be
    // sixty guesses about width and sixty chances to diverge.
    internal static int DAT_8008d284;

    // GHIDRA: DAT_8008d16c @ 0x8008D16C (VS.EXE)
    // The sound task's task-block pointer, which the pointer above is derived from. Read as an
    // absolute (`lui v0,0x8009 / lw v0,-0x2e94(v0)`), not gp-relative.
    internal const int Dat8008d16cAddress = unchecked((int)0x8008D16C);

    // GHIDRA: DAT_8008d280 @ 0x8008D280 (VS.EXE) — gp+0x184
    // Handle of a buffer opened at init by FUN_800666cc(0x0007D800, 0x400). On failure the init
    // prints "NO_Buffer!!!" (the literal at 0x8002090C) and bails to its epilogue, so a negative
    // value here means the sound task never armed.
    internal static int DAT_8008d280;

    // GHIDRA: DAT_8008d338 @ 0x8008D338 (VS.EXE) — gp+0x23C
    // PARTIAL: an SpuStEnv-shaped structure the init writes at +0x000/+0x17C/+0x180. It is NOT
    // part of the workspace and must not be confused with it; the recon flagged that trap
    // explicitly. Held as its PSX address because the init hands the address around.
    internal const int Dat8008d338Address = unchecked((int)0x8008D338);

    // GHIDRA: DAT_8008d210 @ 0x8008D210 (VS.EXE)
    internal const int Dat8008d210Address = unchecked((int)0x8008D210);

    // ==== The workspace, 0x194 bytes ===========================================================
    // THE SIZE IS CLOSED, two independent ways. Statically, the init's last two writes are
    // `sh r0,0x190(s0)` @0x8005DA3C and `sh r0,0x192(s0)` @0x8005DA44, so the struct ends at 0x194.
    // In live memory the allocation is followed by a visibly different structure -- a word pair
    // then a code pointer -- beginning exactly at +0x194.
    internal const int WorkspaceSize = 0x194;

    // ---- CD bank slots. Five of them, each registered at init by FUN_80075994(slot, path). The
    // paths were read as literals out of the image, so these five names are the game's own, not
    // interpretations.
    internal const int BgmBankSlot = 0x018;   // "\SOUND\BGM.B;1"  @ 0x8002091C
    internal const int CrBankSlot = 0x048;    // "\SOUND\CR.B;1"   @ 0x8002093C
    internal const int AtbBankSlot = 0x090;   // "\SOUND\ATB.B;1"  @ 0x8002094C
    internal const int AbtlBankSlot = 0x0C0;  // "\SOUND\ABTL.B;1" @ 0x8002092C
    internal const int ChseBankSlot = 0x0F0;  // "\SOUND\CHSE.B;1" @ 0x8002095C

    // Only BGM and ABTL are streamed from CD by the init itself, into fixed RAM at
    // 0x801B6000/0x801B9000 and 0x801BE000/0x801D2000 (a VH+VB pair each). CR, ATB and CHSE are
    // registered and not read, which is consistent with loading them on demand later.

    // ---- The CD-load step machine's own fields, all proven by FUN_8005f704's disassembly.
    internal const int LoadRequestScratch = 0x0D8;  // handed to FUN_80073790 / FUN_8007328c
    internal const int LoadRequestKind = 0x0DC;     // set to 2 in state 1, 0x3C in state 3
    internal const int BgmVabHandle = 0x10A;        // the BGM open's return
    internal const int TaskState = 0x10E;           // the dispatcher's own state: 0 init, 1 running, >=2 idle
    internal const int Gate12A = 0x12A;             // state 0 waits for this to read 0
    internal const int RetryCountdown = 0x13C;      // armed to 0x0A, decremented per poll
    internal const int AbtlVabHandle = 0x154;       // twin of BgmVabHandle for the ABTL bank
    internal const int PendingVabHandle = 0x158;    // SsVabOpenHeadSticky's return, -1 when idle
    internal const int CompletedRequestId = 0x15A;  // state 7 stores the request id here on success

    // ---- Two arrays whose length the evidence DISAGREES on, recorded rather than smoothed over.
    // FUN_8005f704's state 1 loops i = 0..5 inclusive -- `slti v0,6` at 0x8005F7E0 -- so it touches
    // SIX halfwords in each. The init's zero-fill covers only five. Six is what the loop proves and
    // six is what is declared; the init writing one fewer is a fact about the init, not about the
    // array, and whoever ports the init should re-read it rather than trust this note.
    internal const int VoiceHandles = 0x12C;        // 6 halfwords, -1 = no voice assigned
    internal const int VoiceFlags = 0x148;          // 6 halfwords, state 1 ORs 0x80 into each

    internal const int VoiceSlotCount = 6;
    internal const int VoiceSlotStride = 2;

    // ---- The voice-line machine, FUN_80060144's three states.
    internal const int VoiceLineState = 0x15C;
    internal const int VoiceLinePitch = 0x15E;      // low 14 bits a step value, 0x4000 a pending flag

    // GHIDRA: DAT_800b0ddc @ 0x800B0DDC (VS.EXE)
    // The SpuVoiceAttr FUN_8005ff5c points at the ADPCM buffer below before SpuSetVoiceAttr.
    internal const int Dat800b0ddcAddress = unchecked((int)0x800B0DDC);

    // GHIDRA: DAT_801c1000 @ 0x801C1000 (VS.EXE)
    // The streamed-voice RAM buffer. Its cursor is compared against 0x801C2E00, which makes the
    // live span 0x1E00 bytes.
    internal const int Dat801c1000Address = unchecked((int)0x801C1000);
    internal const int Dat801c1000End = unchecked((int)0x801C2E00);
}
