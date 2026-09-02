using PsxSdkMonogame;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE SAVE-RECORD DISPATCHER AND ITS TWO DRIVERS — the read side, the write side and the four card
// primitives the write side needs. FUN_800276d8 @ 0x800276D8 is the single entry point the two save
// pickers and the options screen call; everything else here is reached only through it.
//     0x800213B8  LoadSaveRecords   READ  the six records off the card into 0x801FF200 / 0x801FF218
//     0x800218D4  RunSaveWriteFlow   WRITE the seven blocks back, formatting or creating if needed
//     0x80022024  GetFreeCardBlocks   free blocks on the card = 15 - sum(size >> 13)
//     0x80022094  FormatMemoryCard   format the card
//     0x80022138  CreateSaveFile   create "BISLPS-00355DRAGON" and stamp its header
//     0x800224B0  WriteSaveHeader   write the 512-byte icon/title header into blocks 0..3 of the file
//     0x800226F8  WriteSaveRecord   write ONE 128-byte record, with the same checksum ReadSaveRecord reads
// THE SEVEN OTHERS THEY CALL ARE ALREADY PORTED and live in MemoryCard.cs, which owns this module's
// state (DAT_80055A78/80/84/88/8C): InitializeMemoryCard, ShutdownMemoryCard, ProbeMemoryCard, RunSaveLoadFlow,
// QueryCardStatus, IsSaveFileMissing, ReadSaveRecord and the message overlay ShowCardMessage (still BLOCKED
// there). CdAudio.UpdateCdAudio and PadInput.FUN_80026208 are ported too.
//
// THE ADDRESS RUNS, so the module boundary is visible: 0x800213B8..0x80021617 and
// 0x800218D4..0x80021CE3 sit inside MemoryCard.cs's own two .text runs, and 0x80022024..0x800220D3,
// 0x80022138..0x800221CF, 0x800224B0..0x8002280F sit between the functions it already lists.
// FUN_800276d8 itself is emitted with the screen module, at 0x800276D8, between the CD-DA stop
// StopCdAudio and the message overlay ShowCardMessage — but everything it does is card work.
//
// WHICH BLOCK HOLDS WHAT, closed by reading the two sides against each other:
//     block 0   the 64-byte options record at 0x801FF018 (read by MemoryCard.RunSaveLoadFlow)
//     block 1,2,3   the three EIGHT-byte DEMO records at 0x801FF200 / 0x801FF208 / 0x801FF210
//     block 4,5,6   the three SIXTEEN-byte SP records at 0x801FF218 / 0x801FF228 / 0x801FF238
// LoadSaveRecords's read cases 0..2 write `(&g_DemoSaveRecords3)[i * 2]` = 0x801FF200 + i * 8 and its cases
// 3..5 write `(&DAT_801ff1e8)[i * 4]` = 0x801FF1E8 + i * 16, which for i = 3, 4, 5 is exactly
// 0x801FF218 / 0x801FF228 / 0x801FF238. RunSaveWriteFlow's write cases 1..3 use 0x801FF1F8 + n * 8 and
// its cases 4..6 use 0x801FF1D8 + n * 16 — the same six addresses from the other direction.
//
// WHICH PATH THIS PORT TAKES, AND WHY IT IS CORRECT RATHER THAN A DEFECT — traced state by state,
// not assumed. Both pickers call FUN_800276d8 with mode 0 or 1, which runs LoadSaveRecords.
// DAT_80055A84 is 0 when they do: main's pre-loop MemoryCard.RunSaveLoadFlow leaves it at 0 on every
// one of its exits. So LoadSaveRecords goes state 0 -> state 7, and state 7 asks
// MemoryCard.IsSaveFileMissing whether "bu00:BISLPS-00355DRAGON" is in the card directory.
// PsxSdkMonogame's LibMcrd models each card as a folder of files next to the executable
// (memorycard1/, created and filled with sixteen blank slotN.bin on first probe — LibMcrd.cs
// EnsureAllSlotsMaterialized). THE CARD IS PRESENT; THE SAVE FILE IS NOT. firstfile finds no match,
// IsSaveFileMissing returns 1, state 7 takes its `else` arm, sets DAT_80055A84 = 0 and RETURNS 0 without
// ever reaching MemoryCard.ReadSaveRecord. The dispatcher then runs its own failure arm — EIGHTEEN
// words zeroed walking DOWN from 0x801FF244, i.e. 0x801FF200..0x801FF247, which is both lists — and
// returns. Both pickers therefore see three records with bit 0 clear, mark all three rows absent
// and preselect the "no card" row.
// THAT IS THE CONSOLE'S OWN NO-SAVE BEHAVIOUR, produced here by the original's own code rather than
// by a stub, and it is not a fabricated slot. Drop a real "BISLPS-00355DRAGON" file into
// memorycard1/ and state 7 answers the other way: state 0xF runs six MemoryCard.ReadSaveRecord reads
// against blocks 1..6 and fills both lists from the file.
//
// MODE 2 IS REACHED, and this header said the opposite until the options screen was ported.
//
// The old claim was that FUN_80031c8c's call at 0x80031CB4 passes its own param_1, which that arm
// has already tested as zero. It does not. The instruction immediately before the call OVERWRITES
// a0 with the literal 2:
//     0x80031C90  beqz  a0, 0x80031CB0      param_1 == 0 branches forward
//     0x80031CB0  li    a0, 0x0002          <- a0 is no longer param_1
//     0x80031CB4  jal   0x800276D8
//     0x80031CB8  addiu a1, sp, 0x10        (delay slot)
// So param_1 == 0 selects MODE 2, the save side, and param_1 != 0 selects mode 3 at 0x80031CA0.
//
// The decompilation shows `FUN_800276d8(2);` there today, so the C alone is enough to see it now.
// It was not always: a reading taken an hour before this correction printed a bare
// `FUN_800276d8();` at the same site, with no arguments at all, and a re-analysis is what changed
// it. That is worth recording, because the whole error rests on it — a decompiler view is a
// derived artefact and can differ between two readings of the same unchanged program. Only the
// bytes at 0x80031CB0 are stable.
//
// FUN_800276d8 takes ONE parameter and that is not damage. Its prologue destroys the incoming a1
// before reading it — `lui a1, 0x8002` / `addiu a1, 0x05ac` at 0x800276E8 — so the pointer every
// caller sets up in a1 is dead on arrival. The stored `undefined FUN_800276d8(void)` is the state
// of every function in this image, not a wiped prototype, and forcing a two-parameter signature
// would assert an argument the bytes disprove.
//
// RunOptionsScreen closes the loop: row 3 of the options screen calls FUN_80031c8c(DAT_80055b14),
// and DAT_80055b14 == 0 is the value that draws セーブ. Saving reaches mode 2.
//
// RunSaveWriteFlow and the routines only it reaches are therefore live code, not transliterated
// ballast.
internal static class CardRecords
{
    // GHIDRA: g_OptionsRecord64 @ 0x801FF018
    // The 64-byte options record — block 0 of the save file. MemoryCard.cs documents its extent;
    // this file only ever hands its ADDRESS to WriteSaveRecord, which reads 64 bytes from it.
    private const int g_OptionsRecord64_Address = unchecked((int)0x801FF018);

    // GHIDRA: DAT_801ff1d8 @ 0x801FF1D8
    // Not a record: the BASE RunSaveWriteFlow adds `DAT_80055a8c << 4` to for cases 4, 5 and 6, giving
    // 0x801FF218 / 0x801FF228 / 0x801FF238 — the three sixteen-byte SP records.
    private const int DAT_801ff1d8_Address = unchecked((int)0x801FF1D8);

    // GHIDRA: DAT_801ff1f8 @ 0x801FF1F8
    // The same trick for the DEMO list: base plus `DAT_80055a8c << 3` for cases 1, 2 and 3 gives
    // 0x801FF200 / 0x801FF208 / 0x801FF210.
    private const int DAT_801ff1f8_Address = unchecked((int)0x801FF1F8);

    // GHIDRA: DAT_801ff1e8 @ 0x801FF1E8
    // The read side's mirror of DAT_801ff1d8: `(&DAT_801ff1e8)[i * 4]` on an undefined4 * is
    // 0x801FF1E8 + i * 16, and the loop only ever reaches it with i = 3, 4, 5.
    private const int DAT_801ff1e8_Address = unchecked((int)0x801FF1E8);

    // GHIDRA: g_DemoSaveRecords3 @ 0x801FF200
    // The first of the three eight-byte DEMO records. ModeBranches.cs documents the list.
    private const int g_DemoSaveRecords3_Address = unchecked((int)0x801FF200);

    // GHIDRA: DAT_801ff244 @ 0x801FF244
    // The LAST word of the SP list (record 2 at 0x801FF218 + 0x20, field +0x0C). The dispatcher's
    // failure arm walks DOWN from it, one word at a time, eighteen times — 0x801FF244 - 17 * 4 =
    // 0x801FF200, so the cleared span is 0x801FF200..0x801FF247 and it is both lists exactly.
    private const int DAT_801ff244_Address = unchecked((int)0x801FF244);

    // GHIDRA: g_SaveFileHeader512 @ 0x80020144
    // FIVE HUNDRED AND TWELVE BYTES of .rdata, 0x80020144..0x80020343, read out of the image with
    // read-memory and reproduced verbatim. It is the PlayStation save file's own header: the "SC"
    // magic at +0x000, the Shift-JIS title that follows it, and then the 16-colour CLUT and the
    // three 16-by-16 4bpp icon frames.
    // ITS EXTENT IS THE CODE'S OWN: WriteSaveHeader copies 0x80020144..0x800201C3 into its first stack
    // buffer and 0x800201C4..0x80020343 into the next, and 0x80 + 0x180 = 0x200.
    private static readonly byte[] g_SaveFileHeader512 =
    {
        0x53, 0x43, 0x13, 0x01, 0x83, 0x68, 0x83, 0x89, 0x83, 0x53, 0x83, 0x93, 0x83, 0x7B, 0x81, 0x5B, // +0x000
        0x83, 0x8B, 0x82, 0x79, 0x88, 0xCC, 0x91, 0xE5, 0x82, 0xC8, 0x82, 0xE9, 0x83, 0x68, 0x83, 0x89, // +0x010
        0x83, 0x53, 0x83, 0x93, 0x83, 0x7B, 0x81, 0x5B, 0x83, 0x8B, 0x93, 0x60, 0x90, 0xE0, 0x00, 0x00, // +0x020
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // +0x030
        0x00, 0x00, 0x00, 0x00, 0x74, 0x68, 0x69, 0x73, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // +0x040
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // +0x050
        0x00, 0x00, 0x6A, 0x00, 0x71, 0x19, 0xFF, 0x4B, 0x5B, 0x32, 0xA7, 0x08, 0x93, 0x01, 0x38, 0x2A, // +0x060
        0x5F, 0x53, 0x9B, 0x1A, 0xBE, 0x3A, 0x7D, 0x1F, 0xDE, 0x6F, 0xF3, 0x01, 0x03, 0x03, 0xFF, 0x7F, // +0x070
        0x63, 0x3B, 0xB3, 0x69, 0x66, 0xB6, 0x99, 0x95, 0xB3, 0x36, 0xB3, 0x9B, 0x36, 0x93, 0x59, 0x99, // +0x080
        0x33, 0x6B, 0x36, 0x6B, 0xB6, 0x63, 0xA6, 0x96, 0x36, 0x63, 0x68, 0x66, 0x6B, 0x33, 0xA6, 0x55, // +0x090
        0x6B, 0xC6, 0x88, 0xA8, 0xD6, 0x36, 0xAB, 0x95, 0xBB, 0xF6, 0x88, 0x88, 0xAA, 0x6A, 0x63, 0x99, // +0x0A0
        0xDB, 0xC6, 0x8F, 0x48, 0xAA, 0x4A, 0xB6, 0x96, 0x6B, 0x67, 0xFF, 0x88, 0xA4, 0x88, 0x54, 0x66, // +0x0B0
        0x86, 0x78, 0x22, 0xF2, 0xA6, 0x2C, 0x22, 0x64, 0x76, 0x88, 0xF8, 0x2E, 0x55, 0xE2, 0x4C, 0x64, // +0x0C0
        0x6B, 0x27, 0x87, 0xAA, 0x2C, 0xAA, 0x6A, 0x54, 0xBB, 0x66, 0x82, 0x88, 0xA8, 0xAA, 0x54, 0x95, // +0x0D0
        0xBB, 0x6B, 0x72, 0x78, 0x22, 0x47, 0x52, 0x69, 0xDB, 0x69, 0x78, 0x82, 0x88, 0x2A, 0x55, 0x99, // +0x0E0
        0x99, 0x66, 0x88, 0x2A, 0xAA, 0x52, 0x42, 0x95, 0x69, 0x88, 0x78, 0xA8, 0x22, 0x25, 0x22, 0x55, // +0x0F0
        0x36, 0x33, 0xBB, 0x69, 0xBB, 0x6B, 0x99, 0x95, 0x39, 0x33, 0xBB, 0x69, 0xBB, 0x96, 0x99, 0x95, // +0x100
        0x63, 0x33, 0xB3, 0x69, 0x6B, 0x96, 0x59, 0x99, 0x93, 0x3B, 0xB3, 0x6B, 0x36, 0x93, 0x56, 0x99, // +0x110
        0x33, 0x66, 0x66, 0x6B, 0xBB, 0x63, 0xA4, 0x56, 0x33, 0x63, 0x8F, 0x66, 0x6B, 0xB3, 0xAD, 0x95, // +0x120
        0x66, 0xC6, 0x88, 0xA8, 0x66, 0x3B, 0xA6, 0x95, 0xBB, 0xF6, 0x8F, 0x88, 0xAA, 0x36, 0x46, 0x95, // +0x130
        0x6B, 0xC6, 0x8F, 0x48, 0xAA, 0xBD, 0x46, 0x96, 0x6B, 0x67, 0xFF, 0x88, 0xA4, 0xD4, 0x66, 0x6A, // +0x140
        0x86, 0x28, 0x22, 0xFA, 0xA6, 0x4C, 0xA2, 0x64, 0x76, 0x28, 0x2A, 0x22, 0x55, 0x22, 0x4C, 0x54, // +0x150
        0x6B, 0x27, 0xA7, 0xAA, 0x2C, 0xAA, 0x54, 0x95, 0xBB, 0x26, 0x85, 0xA8, 0xA8, 0xAA, 0x54, 0x66, // +0x160
        0xBB, 0x2B, 0x52, 0x88, 0x22, 0x47, 0x52, 0x95, 0xB6, 0x59, 0x78, 0x75, 0x88, 0x27, 0x25, 0x52, // +0x170
        0x63, 0x3B, 0xB3, 0x69, 0xBB, 0x63, 0x9D, 0x95, 0x33, 0x63, 0x68, 0x66, 0x6B, 0x33, 0x63, 0x95, // +0x180
        0x63, 0xC6, 0x8F, 0x68, 0xA6, 0xD6, 0x33, 0x95, 0xB6, 0xF6, 0x88, 0x88, 0xAA, 0x4A, 0xD6, 0x53, // +0x190
        0xDB, 0x86, 0x8F, 0x48, 0xAA, 0x44, 0x54, 0x96, 0x6B, 0x67, 0x42, 0x8A, 0xA4, 0x48, 0x52, 0x92, // +0x1A0
        0x8B, 0x78, 0xFF, 0xF2, 0xAA, 0x2C, 0x4C, 0x94, 0x7B, 0x88, 0xFF, 0x2E, 0x55, 0xE2, 0x4C, 0x64, // +0x1B0
        0x66, 0x27, 0x87, 0xAA, 0x2C, 0x4A, 0x54, 0x65, 0xBD, 0x66, 0x82, 0x88, 0xA8, 0xA8, 0x54, 0x69, // +0x1C0
        0xBB, 0x6B, 0x82, 0x88, 0x55, 0xAA, 0x54, 0x99, 0x6B, 0x69, 0x72, 0x28, 0x55, 0x42, 0x52, 0x99, // +0x1D0
        0x2B, 0x62, 0x78, 0x88, 0x55, 0x24, 0x55, 0x96, 0x2B, 0x28, 0x88, 0x42, 0xAA, 0x54, 0x42, 0x55, // +0x1E0
        0x82, 0x28, 0x78, 0x28, 0x22, 0x25, 0x42, 0x25, 0x82, 0x88, 0x78, 0xA8, 0x22, 0x25, 0x22, 0x22, // +0x1F0
    };

    // GHIDRA: FUN_800276d8 @ 0x800276D8
    // 352 bytes, six call sites. THE DISPATCHER — a four-way switch on param_1 and nothing else.
    //
    // NOTE ON param_2 — THE ORIGINAL IGNORES IT. Every caller passes a list base in a1, but Ghidra
    // recovers `void FUN_800276d8(int param_1)` and nothing in the body reads a1. It cannot matter:
    // LoadSaveRecords hard-codes both list bases itself, so the read fills the same two lists whichever
    // one the caller named. The parameter is kept because the call sites pass it.
    //
    // NOTE ON THE TWO RECTs — THEY ARE DEAD. The prologue copies four words of .rdata at 0x800205AC
    // into the stack frame as two RECTs, {0x280, 0, 320, 240} and {0, 0, 320, 240}, and NOTHING in
    // any of the four arms reads them back. They are kept because the stores are (rule 12); their
    // shape is the same full-frame VRAM save/restore pair MemoryCard.ShowCardMessage uses.
    internal static void FUN_800276d8(int param_1, int param_2)
    {
        int iVar5;
        int iVar6;
        RECT auStack_18 = new RECT();
        RECT auStack_10 = new RECT();

        _ = param_2;

        auStack_18.x = 0x280;
        auStack_18.y = 0;
        auStack_18.w = 0x140;
        auStack_18.h = 0xf0;
        auStack_10.x = 0;
        auStack_10.y = 0;
        auStack_10.w = 0x140;
        auStack_10.h = 0xf0;

        if (param_1 == 2)
        {
            // The save side. FUN_80031c8c @ 0x80031CB0 loads the literal 2 into a0 and calls here,
            // which the options screen reaches on row 3 when DAT_80055b14 == 0 (セーブ). See the header.
            MemoryCard.ShowCardMessage(5);
            iVar6 = 0;
            MemoryCard.ShutdownMemoryCard();
            do
            {
                iVar6 = iVar6 + 1;
                CdAudio.UpdateCdAudio();
                VSync(0);
            }
            while (iVar6 < 0x1e);

            MemoryCard.InitializeMemoryCard();
            SharedHighRam.g_CardProbeResult = MemoryCard.ProbeMemoryCard(0);
            RunSaveWriteFlow();
        }
        else if (param_1 < 3)
        {
            if (-1 < param_1)
            {
                iVar6 = LoadSaveRecords();
                iVar5 = 0x11;
                if (iVar6 == 0)
                {
                    // Eighteen words, not seventeen: the body runs, THEN decrements, THEN tests
                    // `-1 < iVar5`, so iVar5 = 0x11 down to 0 inclusive.
                    int puVar4 = DAT_801ff244_Address;
                    do
                    {
                        PsxRam.WriteI32(puVar4, 0);
                        iVar5 = iVar5 + -1;
                        puVar4 = puVar4 + -4;
                    }
                    while (-1 < iVar5);
                }
            }
        }
        else if (param_1 == 3)
        {
            MemoryCard.ShowCardMessage(1);
            iVar6 = 1;
            do
            {
                CdAudio.UpdateCdAudio();
                VSync(0);
                iVar6 = iVar6 + 1;
            }
            while ((short)iVar6 < 0xf);

            MemoryCard.RunSaveLoadFlow();
        }
    }

    // GHIDRA: LoadSaveRecords @ 0x800213B8
    // 168 bytes. THE READ SIDE — the same state machine shape as MemoryCard.RunSaveLoadFlow, run to
    // completion inside its own blocking do/while, but with SIX passes instead of one and with no
    // message screens at all: every failure goes straight to state 0x10 and returns 0.
    //   state 0     -> 7, reset the pass counter, keep looping
    //   state 1     -> 0 and RETURN 0 when the probe said something other than 0 or 4; otherwise -> 7
    //   state 7     the file exists -> 0xF; it does not -> 0 and RETURN 0
    //   state 0xF   passes 0..2 read blocks 1..3 into the DEMO list, passes 3..5 read blocks 4..6
    //               into the SP list, pass 6 -> 0 and RETURN 1. Any short/corrupt record -> 0x10.
    //   state 0x10  reset the pass counter and RETURN 0
    //
    // THE TWO LEAKED ARGUMENTS ARE TRACED, not assumed. `QueryCardStatus()` at 0x800213E0 has
    // `addu s3, zero, zero` in its delay slot and this function's prologue never touches a0, so a0
    // is whatever FUN_800276d8 left: the jal at 0x80027760 also has a nop in its delay slot, and a0
    // there is the third word of the RECT constant block loaded at 0x80027738 (`lwl a0, 0xB(a1)` /
    // `lwr a0, 8(a1)`), which is 0x00000000. So QueryCardStatus(0) below is a measured value.
    internal static int LoadSaveRecords()
    {
        bool bVar4;
        short sVar5;
        int iVar6;
        int uVar7;

        // ReadSaveRecord fills 0x80 bytes from &uStack_98; Ghidra names only the first four words of
        // that span (uStack_98, uStack_94, uStack_90, uStack_8c) because those are the only ones
        // read back. The buffer is the record.
        byte[] uStack_98 = new byte[0x80];

        bVar4 = false;
        uVar7 = 0;
        MemoryCard.DAT_80055a80 = 1;
        sVar5 = 0;

        // `DAT_80055a78._0_2_ = QueryCardStatus();` — an `sh` into the low half of an undefined4.
        MemoryCard.DAT_80055a78 =
            (MemoryCard.DAT_80055a78 & unchecked((int)0xffff0000)) |
            (ushort)MemoryCard.QueryCardStatus(0);
        if ((short)MemoryCard.DAT_80055a78 == 2)
        {
            MemoryCard.g_CardReprobeRequest = 1;
            MemoryCard.g_CardOperationState = 1;
        }

        do
        {
            if (MemoryCard.g_CardReprobeRequest == 1)
            {
                MemoryCard.g_CardReprobeRequest = 0;
                sVar5 = (short)MemoryCard.ProbeMemoryCard(0);
            }

            // Ghidra's `if (false) goto switchD_80021458_caseD_2;` sits here and is dead.
            switch (MemoryCard.g_CardOperationState)
            {
                case 0:
                    MemoryCard.g_CardOperationState = 7;
                    MemoryCard.DAT_80055a8c = 0;
                    bVar4 = true;
                    break;
                case 1:
                    MemoryCard.DAT_80055a8c = 0;
                    if ((sVar5 != 0) && (sVar5 != 4))
                    {
                        MemoryCard.g_CardOperationState = 0;
                        goto LAB_800215e8;
                    }

                    MemoryCard.g_CardOperationState = 7;
                    goto LAB_800214b4;
                case 7:
                    iVar6 = MemoryCard.IsSaveFileMissing(0);
                    MemoryCard.g_CardOperationState = 0xf;
                    if (iVar6 == 0)
                    {
                        goto LAB_800214b4;
                    }

                    MemoryCard.g_CardOperationState = 0;
                    goto LAB_800215e8;
                case 0xf:
                    bVar4 = true;
                    switch (MemoryCard.DAT_80055a8c)
                    {
                        case 0:
                        case 1:
                        case 2:
                            iVar6 = MemoryCard.ReadSaveRecord(
                                0, MemoryCard.DAT_80055a8c + 1, uStack_98);
                            if (iVar6 == 0x80)
                            {
                                // `(&g_DemoSaveRecords3)[iVar6 * 2]` on an undefined4 * — 0x801FF200 +
                                // pass * 8, two words. Ghidra prints each store twice, once as the
                                // unaligned SWL/SWR pair the compiler emitted and once as the
                                // aligned store; both write the same four bytes.
                                iVar6 = (int)MemoryCard.DAT_80055a8c;
                                PsxRam.WriteI32(
                                    g_DemoSaveRecords3_Address + (iVar6 * 8),
                                    MipsMemory.ReadI32(uStack_98, 0));
                                PsxRam.WriteI32(
                                    g_DemoSaveRecords3_Address + (iVar6 * 8) + 4,
                                    MipsMemory.ReadI32(uStack_98, 4));
                            }
                            else
                            {
                                goto LAB_800215b0;
                            }

                            break;
                        case 3:
                        case 4:
                        case 5:
                            iVar6 = MemoryCard.ReadSaveRecord(
                                0, MemoryCard.DAT_80055a8c + 1, uStack_98);
                            if (iVar6 != 0x80)
                            {
                                goto LAB_800215b0;
                            }

                            // `(&DAT_801ff1e8)[iVar6 * 4]` — 0x801FF1E8 + pass * 16, four words.
                            // With pass 3, 4, 5 that is 0x801FF218 / 0x801FF228 / 0x801FF238.
                            iVar6 = (int)MemoryCard.DAT_80055a8c;
                            PsxRam.WriteI32(
                                DAT_801ff1e8_Address + (iVar6 * 0x10),
                                MipsMemory.ReadI32(uStack_98, 0));
                            PsxRam.WriteI32(
                                DAT_801ff1e8_Address + (iVar6 * 0x10) + 4,
                                MipsMemory.ReadI32(uStack_98, 4));
                            PsxRam.WriteI32(
                                DAT_801ff1e8_Address + (iVar6 * 0x10) + 8,
                                MipsMemory.ReadI32(uStack_98, 8));
                            PsxRam.WriteI32(
                                DAT_801ff1e8_Address + (iVar6 * 0x10) + 0xc,
                                MipsMemory.ReadI32(uStack_98, 0xc));
                            break;
                        case 6:
                            MemoryCard.g_CardOperationState = 0;
                            uVar7 = 1;
                            bVar4 = false;
                            break;
                        default:
                            break;
                    }

                    MemoryCard.DAT_80055a8c = (ushort)(MemoryCard.DAT_80055a8c + 1);
                    goto LAB_800215f0;
                case 0x10:
                    MemoryCard.DAT_80055a8c = 0;
                    goto LAB_800215e8;
                default:
                    break;
            }

            goto LAB_800215f0;

        LAB_800215b0:
            MemoryCard.g_CardOperationState = 0x10;
            MemoryCard.DAT_80055a8c = (ushort)(MemoryCard.DAT_80055a8c + 1);
            goto LAB_800215f0;

        LAB_800214b4:
            bVar4 = true;
            goto LAB_800215f0;

        LAB_800215e8:
            uVar7 = 0;
            bVar4 = false;

        LAB_800215f0:
            if (!bVar4)
            {
                return uVar7;
            }
        }
        while (true);
    }

    // GHIDRA: RunSaveWriteFlow @ 0x800218D4
    // 1040 bytes. THE WRITE SIDE — the state machine FUN_800276d8's mode 2 drives, over the same
    // DAT_80055A84 / DAT_80055A8C pair.
    //   state 0     -> 3
    //   state 1     probe said 0 or 4 -> 3, otherwise -> 2 and the "wrong card" message
    //   state 2     read the pad and fall into the shared exit test
    //   state 3     probe says 4 (a new/unformatted card) -> 4 and the "format?" message, else -> 7
    //   state 4     Circle formats (-> 5), Cross gives up
    //   state 5     format succeeded -> 0xB, failed -> 6 and the failure message
    //   state 7     the file exists -> 0xD (nothing to create), else -> 9
    //   state 9     no free blocks -> 0xB, otherwise -> 10 and the "not enough room" message
    //   state 0xB   create the file: success -> 0xC, failure -> 0xE and the failure message
    //   state 0xC   passes 0..6 write blocks 0..6, pass 7 RETURNS 1
    //   state 0xD   the file already existed: pass 0 writes block 0, pass 1 RETURNS 1
    //   state 0xE / 6 / 10 / 2   the message screens, leaving with RETURN 2 on Circle
    //
    // THE RETURN VALUE IS A LEAKED REGISTER, and it is reproduced as 0 here. Ghidra reports
    // `undefined4 unaff_s2` — the prologue at 0x800218F4 saves s2 and NEVER initialises it, so on
    // the console the "still running" iterations return whatever the caller had in s2. IT CANNOT BE
    // OBSERVED: the only call site, FUN_800276d8 @ 0x800277D4, DISCARDS the value (Ghidra renders it
    // as a bare `RunSaveWriteFlow();`, and the image has no `sw v0` after that jal). The two paths that
    // do assign it write 1 and 2 before returning.
    //
    // THE LEAKED a0 IS TRACED. This function's prologue never writes a0, and the jal at 0x800277D4
    // has a nop in its delay slot; the last write to a0 before it is `addu a0, zero, zero` at
    // 0x800277C8, the delay slot of the ProbeMemoryCard(0) call one line above. So a0 = 0 and
    // QueryCardStatus(0) below is measured, not chosen.
    internal static int RunSaveWriteFlow()
    {
        bool bVar1;
        short sVar2;
        int iVar3;
        uint uVar4;
        int iVar5;
        int puVar6;
        int puVar7;
        int unaff_s2 = 0;

        bVar1 = true;
        sVar2 = 0;
        MemoryCard.DAT_80055a78 =
            (MemoryCard.DAT_80055a78 & unchecked((int)0xffff0000)) |
            (ushort)MemoryCard.QueryCardStatus(0);
        if ((short)MemoryCard.DAT_80055a78 == 2)
        {
            MemoryCard.g_CardReprobeRequest = 1;
            MemoryCard.g_CardOperationState = 1;
        }

        do
        {
            if (MemoryCard.g_CardReprobeRequest == 1)
            {
                MemoryCard.g_CardReprobeRequest = 0;
                sVar2 = (short)MemoryCard.ProbeMemoryCard(0);
            }

            switch (MemoryCard.g_CardOperationState)
            {
                case 0:
                    MemoryCard.g_CardOperationState = 3;
                    MemoryCard.DAT_80055a8c = 0;
                    break;
                case 1:
                    MemoryCard.DAT_80055a8c = 0;
                    if ((sVar2 == 0) || (sVar2 == 4))
                    {
                        MemoryCard.g_CardOperationState = 3;
                    }
                    else
                    {
                        MemoryCard.g_CardOperationState = 2;
                        MemoryCard.ShowCardMessage(2);
                    }

                    break;
                case 2:
                    uVar4 = PadInput.FUN_80026208(3);
                    goto LAB_80021c8c;
                case 3:
                    iVar3 = MemoryCard.ProbeMemoryCard(0);
                    if (iVar3 == 4)
                    {
                        MemoryCard.ShowCardMessage(6);
                        MemoryCard.g_CardOperationState = 4;
                    }
                    else
                    {
                        MemoryCard.g_CardOperationState = 7;
                    }

                    break;
                case 4:
                    uVar4 = PadInput.FUN_80026208(3);

                    // `(ushort)((short)DAT_80055a78 - 1U) < 2` is "the probe code is 1 or 2".
                    if (((ushort)((short)MemoryCard.DAT_80055a78 - 1U) < 2) || ((uVar4 & 0x40) != 0))
                    {
                        goto LAB_80021c94;
                    }

                    if ((uVar4 & 0x20) != 0)
                    {
                        MemoryCard.g_CardOperationState = 5;
                    }

                    goto LAB_80021ca0;
                case 5:
                    iVar3 = FormatMemoryCard(0);
                    if (iVar3 == 0)
                    {
                        goto LAB_80021b08;
                    }

                    MemoryCard.g_CardOperationState = 6;
                    MemoryCard.ShowCardMessage(7);
                    break;
                case 6:
                    goto LAB_80021c64;
                case 7:
                    iVar3 = MemoryCard.IsSaveFileMissing(0);
                    if (iVar3 == 0)
                    {
                        MemoryCard.g_CardOperationState = 0xd;
                        iVar3 = 1;
                        do
                        {
                            CdAudio.UpdateCdAudio();
                            VSync(0);
                            iVar3 = iVar3 + 1;
                        }
                        while ((short)iVar3 < 0x14);
                    }
                    else
                    {
                        MemoryCard.g_CardOperationState = 9;
                    }

                    break;
                case 9:
                    iVar3 = GetFreeCardBlocks(0);
                    if (iVar3 != 0)
                    {
                        goto LAB_80021b08;
                    }

                    MemoryCard.g_CardOperationState = 10;
                    MemoryCard.ShowCardMessage(8);
                    break;
                case 10:
                    goto LAB_80021c64;
                case 0xb:
                    iVar3 = CreateSaveFile(0);
                    if (iVar3 == 0)
                    {
                        MemoryCard.g_CardOperationState = 0xc;
                        iVar3 = 1;
                        do
                        {
                            CdAudio.UpdateCdAudio();
                            VSync(0);
                            iVar3 = iVar3 + 1;
                        }
                        while ((short)iVar3 < 0x14);
                    }
                    else
                    {
                        MemoryCard.g_CardOperationState = 0xe;
                        MemoryCard.ShowCardMessage(9);
                    }

                    break;
                case 0xc:
                    switch (MemoryCard.DAT_80055a8c)
                    {
                        case 0:
                            goto switchD_80021ba4_caseD_0;
                        case 1:
                        case 2:
                        case 3:
                            puVar6 = DAT_801ff1f8_Address;
                            iVar3 = (int)MemoryCard.DAT_80055a8c << 3;
                            goto LAB_80021bd8;
                        case 4:
                        case 5:
                        case 6:
                            puVar6 = DAT_801ff1d8_Address;
                            iVar3 = (int)MemoryCard.DAT_80055a8c << 4;
                            goto LAB_80021bd8;
                        case 7:
                            goto switchD_80021ba4_caseD_7;
                        default:
                            goto switchD_80021ba4_default;
                    }

                case 0xd:
                    if (MemoryCard.DAT_80055a8c == 0)
                    {
                        goto switchD_80021ba4_caseD_0;
                    }

                    if (MemoryCard.DAT_80055a8c == 1)
                    {
                        goto switchD_80021ba4_caseD_7;
                    }

                    goto switchD_80021ba4_default;
                case 0xe:
                    MemoryCard.DAT_80055a8c = 0;
                    goto LAB_80021c64;
                default:
                    break;
            }

            goto switchD_8002196c_caseD_8;

        LAB_80021b08:
            MemoryCard.g_CardOperationState = 0xb;
            goto switchD_8002196c_caseD_8;

        switchD_80021ba4_caseD_0:
            iVar5 = 0;
            puVar7 = g_OptionsRecord64_Address;
            goto LAB_80021c10;

        LAB_80021bd8:
            iVar5 = (int)MemoryCard.DAT_80055a8c;
            puVar7 = puVar6 + iVar3;

        LAB_80021c10:
            iVar3 = WriteSaveRecord(0, iVar5, puVar7);
            if (iVar3 != 0x80)
            {
                MemoryCard.g_CardOperationState = 0xe;
                MemoryCard.ShowCardMessage(9);
            }

            goto switchD_80021ba4_default;

        switchD_80021ba4_caseD_7:
            MemoryCard.g_CardOperationState = 0;
            unaff_s2 = 1;
            bVar1 = false;

        switchD_80021ba4_default:
            MemoryCard.DAT_80055a8c = (ushort)(MemoryCard.DAT_80055a8c + 1);
            goto switchD_8002196c_caseD_8;

        LAB_80021c64:
            uVar4 = PadInput.FUN_80026208(3);
            if ((ushort)((short)MemoryCard.DAT_80055a78 - 1U) < 2)
            {
                goto LAB_80021c94;
            }

        LAB_80021c8c:
            if ((uVar4 & 0x40) != 0)
            {
                goto LAB_80021c94;
            }

            goto LAB_80021ca0;

        LAB_80021c94:
            MemoryCard.g_CardOperationState = 0;
            unaff_s2 = 2;
            bVar1 = false;

        LAB_80021ca0:
            CdAudio.UpdateCdAudio();
            VSync(0);

        switchD_8002196c_caseD_8:
            if (!bVar1)
            {
                return unaff_s2;
            }
        }
        while (true);
    }

    // GHIDRA: GetFreeCardBlocks @ 0x80022024
    // 112 bytes. HOW MANY BLOCKS ARE FREE — walk the whole card directory, add up each entry's size
    // in 8 KB blocks (`size >> 13`) and return 15 minus the total. RunSaveWriteFlow's state 9 tests it as
    // `!= 0`, i.e. "there is at least one block free". A full card returns 0.
    //
    // ON THE SIZE FIELD: the original reads the word at +0x18 of the BIOS DIRENTRY, which is `size`.
    // PsxSdkMonogame's LibMcrd.DIRENTRY is a different C# shape (status / reserved[3] / name[20] /
    // pad) and LibMcrd.CardFileNext publishes the file's length in reserved[0]. That is the same
    // value the BIOS puts at +0x18; the field name differs, the quantity does not.
    private static int GetFreeCardBlocks(int param_1)
    {
        int iVar1;
        string pcVar2;
        int iVar3;
        LibMcrd.DIRENTRY auStack_30 = new LibMcrd.DIRENTRY();

        iVar3 = 0;
        if (param_1 == 0)
        {
            pcVar2 = "bu00:*.*";
        }
        else
        {
            pcVar2 = "bu10:*.*";
        }

        iVar1 = firstfile(pcVar2, auStack_30);
        while (iVar1 != 0)
        {
            iVar3 = iVar3 + (auStack_30.reserved[0] >> 0xd);
            iVar1 = nextfile(auStack_30);
        }

        return 0xf - iVar3;
    }

    // GHIDRA: FormatMemoryCard @ 0x80022094
    // 64 bytes. FORMAT THE CARD. Ghidra types it `bool` because the body is `return iVar1 == 0;`
    // compiled as `sltiu v0, v0, 1` over a format() that answers non-zero on success — so this
    // RETURNS 1 WHEN THE FORMAT FAILED, and RunSaveWriteFlow's state 5 reads `iVar3 == 0` as "it
    // worked". Kept as an int for the same reason MemoryCard.IsSaveFileMissing is.
    private static int FormatMemoryCard(int param_1)
    {
        int iVar1;
        string puVar2;

        if (param_1 == 0)
        {
            puVar2 = MemoryCard.g_CardDevicePort1;
        }
        else
        {
            puVar2 = MemoryCard.g_CardDevicePort2;
        }

        iVar1 = format(puVar2);
        return iVar1 == 0 ? 1 : 0;
    }

    // GHIDRA: CreateSaveFile @ 0x80022138
    // 152 bytes. CREATE THE SAVE FILE. `open(name, 0x10200)` is create-one-block: 0x200 is the create
    // flag and the 1 in the high halfword is the block count. -1 means the file already existed, and
    // the original still stamps the header on it but reports 1 (failure) — that asymmetry is the
    // original's and is reproduced.
    // The `close()` Ghidra prints with no argument is `close(iVar1)`: the image has
    // `addu a0, v0, zero` at 0x8002218C, right after the open, and nothing rewrites a0 before the
    // `jal 0x8004EB24` at 0x8002219C.
    private static int CreateSaveFile(int param_1)
    {
        int iVar1;
        int uVar2;
        string local_a8;

        if (param_1 == 0)
        {
            local_a8 = MemoryCard.g_CardDevicePort1;
        }
        else
        {
            local_a8 = MemoryCard.g_CardDevicePort2;
        }

        local_a8 = MemoryCard.strcat(local_a8, "BISLPS-00355DRAGON");
        iVar1 = open(local_a8, 0x10200);
        if (iVar1 == -1)
        {
            WriteSaveHeader(local_a8);
            uVar2 = 1;
        }
        else
        {
            close(iVar1);
            uVar2 = WriteSaveHeader(local_a8);
        }

        return uVar2;
    }

    // GHIDRA: WriteSaveHeader @ 0x800224B0
    // 584 bytes. STAMP THE 512-BYTE SAVE HEADER into blocks 0..3 of the file — the "SC" record the
    // console's own memory-card browser reads to draw the icon and the title.
    //
    // THE FOUR STACK BUFFERS ARE ONE 512-BYTE RUN, and that is why the last two look uninitialised.
    // Ghidra names them local_210[32], local_190[32], auStack_110[128] and auStack_90[128] — 512
    // contiguous bytes at -0x210..-0x11. The first copy fills local_210 with 0x80020144..0x800201C3
    // (0x80 bytes). The SECOND copy starts at local_190 and runs until its source pointer reaches
    // 0x80020344, which is 0x180 bytes — three times local_190's declared size — so it spills through
    // auStack_110 and auStack_90 as well. All 512 bytes are written; the compiler simply lost the
    // array bound. Modelled here as the single 512-byte run the code actually addresses.
    private static int WriteSaveHeader(string param_1)
    {
        int iVar4;
        int iVar5;
        byte[] local_210 = new byte[0x200];

        int puVar10 = 0;
        int puVar9 = 0;
        do
        {
            local_210[puVar10 + 0] = g_SaveFileHeader512[puVar9 + 0];
            local_210[puVar10 + 1] = g_SaveFileHeader512[puVar9 + 1];
            local_210[puVar10 + 2] = g_SaveFileHeader512[puVar9 + 2];
            local_210[puVar10 + 3] = g_SaveFileHeader512[puVar9 + 3];
            local_210[puVar10 + 4] = g_SaveFileHeader512[puVar9 + 4];
            local_210[puVar10 + 5] = g_SaveFileHeader512[puVar9 + 5];
            local_210[puVar10 + 6] = g_SaveFileHeader512[puVar9 + 6];
            local_210[puVar10 + 7] = g_SaveFileHeader512[puVar9 + 7];
            local_210[puVar10 + 8] = g_SaveFileHeader512[puVar9 + 8];
            local_210[puVar10 + 9] = g_SaveFileHeader512[puVar9 + 9];
            local_210[puVar10 + 10] = g_SaveFileHeader512[puVar9 + 10];
            local_210[puVar10 + 11] = g_SaveFileHeader512[puVar9 + 11];
            local_210[puVar10 + 12] = g_SaveFileHeader512[puVar9 + 12];
            local_210[puVar10 + 13] = g_SaveFileHeader512[puVar9 + 13];
            local_210[puVar10 + 14] = g_SaveFileHeader512[puVar9 + 14];
            local_210[puVar10 + 15] = g_SaveFileHeader512[puVar9 + 15];
            puVar9 = puVar9 + 0x10;
            puVar10 = puVar10 + 0x10;
        }
        while (puVar9 != 0x80);

        // `puVar10 = local_190` — 0x80 bytes into the run — and the loop runs to 0x80020344.
        puVar10 = 0x80;
        do
        {
            local_210[puVar10 + 0] = g_SaveFileHeader512[puVar9 + 0];
            local_210[puVar10 + 1] = g_SaveFileHeader512[puVar9 + 1];
            local_210[puVar10 + 2] = g_SaveFileHeader512[puVar9 + 2];
            local_210[puVar10 + 3] = g_SaveFileHeader512[puVar9 + 3];
            local_210[puVar10 + 4] = g_SaveFileHeader512[puVar9 + 4];
            local_210[puVar10 + 5] = g_SaveFileHeader512[puVar9 + 5];
            local_210[puVar10 + 6] = g_SaveFileHeader512[puVar9 + 6];
            local_210[puVar10 + 7] = g_SaveFileHeader512[puVar9 + 7];
            local_210[puVar10 + 8] = g_SaveFileHeader512[puVar9 + 8];
            local_210[puVar10 + 9] = g_SaveFileHeader512[puVar9 + 9];
            local_210[puVar10 + 10] = g_SaveFileHeader512[puVar9 + 10];
            local_210[puVar10 + 11] = g_SaveFileHeader512[puVar9 + 11];
            local_210[puVar10 + 12] = g_SaveFileHeader512[puVar9 + 12];
            local_210[puVar10 + 13] = g_SaveFileHeader512[puVar9 + 13];
            local_210[puVar10 + 14] = g_SaveFileHeader512[puVar9 + 14];
            local_210[puVar10 + 15] = g_SaveFileHeader512[puVar9 + 15];
            puVar9 = puVar9 + 0x10;
            puVar10 = puVar10 + 0x10;
        }
        while (puVar9 != 0x200);

        iVar4 = open(param_1, 2);
        if (iVar4 != -1)
        {
            lseek(iVar4, 0, 0);
            iVar5 = write(iVar4, local_210, 0, 0x80);
            if (iVar5 == 0x80)
            {
                lseek(iVar4, 0x80, 0);
                iVar5 = write(iVar4, local_210, 0x80, 0x80);
                if (iVar5 == 0x80)
                {
                    lseek(iVar4, 0x100, 0);
                    iVar5 = write(iVar4, local_210, 0x100, 0x80);
                    if (iVar5 == 0x80)
                    {
                        lseek(iVar4, 0x180, 0);
                        iVar5 = write(iVar4, local_210, 0x180, 0x80);
                        if (iVar5 == 0x80)
                        {
                            close(iVar4);
                            return 0;
                        }
                    }
                }
            }

            close(iVar4);
        }

        return 1;
    }

    // GHIDRA: WriteSaveRecord @ 0x800226F8
    // 280 bytes. WRITE ONE 128-BYTE RECORD — the exact inverse of MemoryCard.ReadSaveRecord, and the
    // two agree field for field:
    //     byte 0        the magic '.', written here as 0x2E
    //     bytes 1..64   the 64-byte payload copied from param_3
    //     bytes 65..126 never written by this function — whatever the stack held
    //     byte 127      the running XOR of bytes 1..64
    // The loop bound is `while ((int)pbVar4 < (int)auStack_4f)`, i.e. until the destination reaches
    // byte 65, which is 64 iterations.
    //
    // PARTIAL: bytes 65..126 are uninitialised stack in the original and are zero here, because a C#
    // local array is zero-initialised and there is no stack garbage to model. ReadSaveRecord never
    // reads them and neither does the checksum, so nothing in this program can observe the
    // difference; a save written by the console and one written by this port differ in those 62
    // bytes.
    //
    // PARTIAL: param_3 is a PSX address and the read is 64 bytes long, but three of its seven call
    // sites hand it a SIXTEEN-byte SP record — 0x801FF238 + 64 runs to 0x801FF277, past the
    // 0x801FF000..0x801FF247 SharedHighRam models. PsxRam.ReadU8 answers 0 for an address it cannot
    // resolve, so those bytes read as zero here and as whatever followed in RAM on the console. The
    // overread is the original's; only its filling differs.
    private static int WriteSaveRecord(int param_1, int param_2, int param_3)
    {
        byte bVar1;
        int iVar2;
        int iVar3;
        int pbVar4;
        string local_b0;
        byte[] local_90 = new byte[0x80];
        byte local_11;

        if (param_1 == 0)
        {
            local_b0 = MemoryCard.g_CardDevicePort1;
        }
        else
        {
            local_b0 = MemoryCard.g_CardDevicePort2;
        }

        local_b0 = MemoryCard.strcat(local_b0, "BISLPS-00355DRAGON");
        iVar2 = open(local_b0, 2);
        if (iVar2 != -1)
        {
            lseek(iVar2, (param_2 * 0x80) + 0x200, 0);
            pbVar4 = 1;
            local_11 = 0;
            local_90[0] = 0x2e;
            do
            {
                bVar1 = PsxRam.ReadU8(param_3);
                local_90[pbVar4] = bVar1;
                pbVar4 = pbVar4 + 1;
                local_11 = (byte)(bVar1 ^ local_11);
                param_3 = param_3 + 1;
            }
            while (pbVar4 < 0x41);

            // local_11 IS byte 127 of the record; the original accumulates the XOR into the stack
            // slot the write then sends. Kept as the separate local Ghidra recovered, stored back
            // where the write reads it.
            local_90[0x7f] = local_11;
            iVar3 = write(iVar2, local_90, 0, 0x80);
            if (iVar3 == 0x80)
            {
                close(iVar2);
                return 0x80;
            }

            close(iVar2);
        }

        return 0;
    }
}
