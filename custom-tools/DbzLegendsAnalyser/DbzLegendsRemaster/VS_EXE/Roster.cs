using PsxSdkMonogame;
using static PsxSdkMonogame.LibCd;

namespace DbzLegendsRemaster.VS_EXE;

// THE ROSTER — how the characters chosen in SELECT.EXE become the six fighters of a match.
//
// One function, FUN_8005cbe0 @ 0x8005CBE0, and it has exactly one caller: main @ 0x80062134, at
// 0x80062428. Its place in the boot order is what makes it the roster's front door:
//
//     CreateTask(LAB_80055e3c, 0x51, 9, 0x3034, 0, g_TaskListTail[9])   the battle manager
//     uVar6 = *(undefined4 *)(iVar7 + 8)                               its workspace
//     FUN_8005cbe0()                        <- this file
//     FUN_80034d98()
//     FUN_800511a8(uVar6)                   VS_EXE/FighterSetup.cs, which creates the six fighters
//
// so the roster is resolved BEFORE any fighter exists.
//
// WHAT IT ACTUALLY DOES, in two halves that share nothing but the record they meet in:
//
//   1. It reads the six halfwords SELECT.EXE left at 0x801FF102..0x801FF10C — three per team — and
//      fills a 300-byte record at 0x80083CF0, reached through PTR_DAT_800844b8. Per non-zero entry
//      it counts one fighter, ORs two flag words, copies the character id, and stamps a running
//      1-based ordinal. The two per-team counts land at +0x00 and +0x02.
//
//   2. It then loads the portraits: \CHR_DATA\FACE.B;1 is searched once, its start turned into a
//      sector number, and the twelve id slots at +0x52 are walked. Each non-zero id n seeks to
//      base + (n - 1) * 2, reads two sectors, and pushes four blocks into VRAM. Afterwards
//      \CHR_DATA\OV_CHR_A.B;1 is read, LZSS-decoded, and six more blocks go up.
//
// HALF TWO IS TITLE.EXE'S LoadFACE_B @ 0x80052D68 RELINKED, and that is a byte count rather than a
// resemblance: TITLE_EXE/FaceImages.cs carries the same loop over the same twelve-entry id table at
// record + 0x52, the same three 0xC x 0x30 tiles out of the CD buffer at +0x20, +0x4A0 and +0x920,
// the same 0x10 x 1 CLUT strip whose x advances 0xA0, 0xB0, 0xC0..., the same six blocks tiling the
// LZSS staging buffer at +0x000, +0x100, +0x300, +0x2300, +0x4600 and +0x6400, and a coordinate
// table whose 144 bytes at 0x80084184 are IDENTICAL to TITLE.EXE's at 0x8007A220. What VS.EXE adds
// in front is half one: TITLE.EXE picks a pre-baked record out of PTR_DAT_8007a554 by
// DAT_1f80012c, VS.EXE builds one from the handover block instead.
//
// That is also the self-check on the record's layout. TITLE.EXE's three records are static data
// this port already read out of the image, and every field VS.EXE writes has a matching non-zero
// value in them: +0x00 / +0x02 hold the two team counts, +0x0A / +0x16 the 0x81-flag runs, +0x22 /
// +0x2E the 0x1E20 and 0x2D08 runs, +0x52 / +0x5E the character ids, +0x112 / +0x11E the ordinals.
// Two independent programs, one structure.
//
// WHAT IT DOES NOT DO, since the mandate asked the question directly: it never reads DAT_801FF100.
// The cursor is loaded as `lui a1, 0x801F; ori a1, a1, 0xF102` at 0x8005CBE4..0x8005CBE8 and only
// ever advances, so the mode-and-result word one halfword in front of the ids is untouched here.
// The word matters — FUN_800512cc branches on it to place the fighters, as VS_EXE/BattleState.cs
// records — but the branch is that function's, not this one's.
//
// NOT WIRED, and reported rather than patched around: VS_EXE_exe.cs still carries a private
// `FUN_8005cbe0()` stub with the same `// GHIDRA: FUN_8005cbe0 @ 0x8005CBE0` annotation, and main
// calls THAT one. Until the stub is removed and main calls Roster.FUN_8005cbe0, this file is dead
// code and two functions in the port claim one address. That is exactly the defect
// VS_EXE/FighterSetup.cs hit with FUN_800511a8, and the fix belongs in VS_EXE_exe.cs, which is not
// this slice's file to touch.
internal static class Roster
{
    // =====================================================================================
    // The handover block
    // =====================================================================================

    // GHIDRA: DAT_801ff102 @ 0x801FF102 (VS.EXE)
    // THE SIX CHARACTER IDS SELECT.EXE EXPORTS, as six halfwords at 0x801FF102, 0x104, 0x106,
    // 0x108, 0x10A and 0x10C. The first loop below reads the first three, the second loop the last
    // three, off one cursor that is never reset — which is why the two teams are three apart in the
    // block and six apart in the record.
    //
    // Only the ADDRESS is declared here. The storage is DbzLegendsRemaster.SharedHighRam's
    // RAM_801ff000, which models 0x801FF000..0x801FF247 for all three overlays, and
    // VS_EXE_exe.ResolveAddress already chains SharedHighRam.Resolve — so a PsxRam read at this
    // address lands in the same bytes SELECT.EXE wrote. Declaring a second region here is precisely
    // what the address-resolution rule forbids, and adding a named accessor to SharedHighRam would
    // commit TITLE.EXE and SELECT.EXE as well, so neither is done: the ids are reached by raw
    // address through the roaming cursor the original uses, and the missing accessor is reported up.
    //
    // PARTIAL: the ids run 1..38 on the reconnaissance's evidence (FACE.B is 76 sectors at two per
    // portrait; the AT table has 38 entries). Nothing below bounds-checks, because the original
    // does not — a value of 39 here would seek two sectors past the end of the file and upload
    // whatever landed in the buffer. Rule 12: reproduced, not corrected.
    private const int Dat801ff102Address = unchecked((int)0x801FF102);

    // =====================================================================================
    // The record the roster is written into
    // =====================================================================================

    // GHIDRA: PTR_DAT_800844b8 @ 0x800844B8 (VS.EXE)
    // A pointer word in initialised .data holding 0x80083CF0, read out of the image with
    // read-memory. The original loads it once (`lw s1, 0x44B8(s1)` at 0x8005CBF8) and works off the
    // register; nothing anywhere writes it. Two other functions load the same word — FUN_800594b4
    // @ 0x800594B4 at 0x800594C0 and FUN_8005a104 @ 0x8005A104 at 0x8005A108 — and those three
    // reads are its only three references in the overlay.
    internal static readonly int PTR_DAT_800844b8 = unchecked((int)0x80083CF0);

    // GHIDRA: DAT_80083cf0 @ 0x80083CF0 (VS.EXE)
    // THE BATTLE SETUP RECORD, 0x12C bytes of initialised .data, lifted out of the overlay image.
    //
    // The extent is closed twice over. TITLE.EXE holds three records of this shape as static data
    // and its own pointer table spaces them 0x12C apart (0x80079C60 - 0x80079B34 = 0x12C, and
    // 0x80079D8C - 0x80079C60 = 0x12C); and the bytes here stop looking like the record exactly
    // there — 0x80083E1C onwards reads 00 00, 0A 00, 32 00, which is not the continuation of any
    // field below.
    //
    // What the shipped contents are is worth stating, because FUN_8005cbe0 does NOT clear the
    // record before filling it. It is a pre-baked 1-versus-1: counts 1 and 1 at +0x00 and +0x02,
    // character id 1 at +0x52 and id 9 at +0x5E, ordinals 1 and 2 at +0x112 and +0x11E, and the
    // flag words already carrying 0x0089, 0x0089, 0x1E20 and 0x2D08. The two flag runs are ORed
    // rather than assigned, so those bits survive; 0x89 | 0x81 is 0x89, which is why the first slot
    // of each team looks untouched. Every slot past the ones a real roster fills keeps whatever the
    // image shipped. Rule 12 again — that is the original's behaviour and it is reproduced.
    //
    // OWNERSHIP CAVEAT, in the shape VS_EXE/FighterSetup.cs and VS_EXE/FileIo.cs already use. This
    // record is not this file's alone: FUN_800594b4 reads its id array at +0x52 and copies each
    // entry into the battle context, and FUN_8005a104 reads its 0x1E20 word at +0x22. Neither is
    // ported yet. When they land they must use THIS array and THESE offsets, not a second set at
    // the same addresses — two spellings of one field is the defect VS_EXE/BattleState.cs exists to
    // prevent, and if the offsets are needed by more than one slice they belong there rather than
    // here.
    private const int Dat80083cf0Address = unchecked((int)0x80083CF0);

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: the record is addressed as raw PSX memory from start to finish — the original walks
    // it with five separate roaming cursors — so it is declared through LibGpu.RamRegion, which is
    // what VS_EXE_exe.ResolveAddress consults FIRST. No row has to be added to that chain, and no
    // second region may ever be declared on 0x80083CF0: RamRegion pairs by reference, so a second
    // declaration appends a row instead of replacing one and resolution can then elect the wrong
    // buffer.
    internal static readonly byte[] DAT_80083cf0 = LibGpu.RamRegion(Dat80083cf0Address, new byte[]
    {
        0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x89, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x89, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x1E,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x2D,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x05, 0x00, 0x06, 0x00,
        0x07, 0x00, 0x08, 0x00, 0x09, 0x00, 0x0A, 0x00, 0x0B, 0x00, 0x40, 0x06,
        0x80, 0x3E, 0x10, 0x27, 0x40, 0x06, 0x80, 0x3E, 0x10, 0x27, 0x40, 0x06,
        0x80, 0x3E, 0x10, 0x27, 0x40, 0x06, 0x80, 0x3E, 0x10, 0x27, 0x40, 0x06,
        0x80, 0x3E, 0x10, 0x27, 0x40, 0x06, 0x80, 0x3E, 0x10, 0x27, 0x40, 0x06,
        0x80, 0x3E, 0x10, 0x27, 0x40, 0x06, 0x80, 0x3E, 0x10, 0x27, 0x40, 0x06,
        0x80, 0x3E, 0x10, 0x27, 0x40, 0x06, 0x80, 0x3E, 0x10, 0x27, 0x40, 0x06,
        0x80, 0x3E, 0x10, 0x27, 0x40, 0x06, 0x80, 0x3E, 0x10, 0x27, 0xA0, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xA0, 0x00, 0x00, 0x00, 0x9C, 0xFF, 0xA0, 0x00,
        0x00, 0x00, 0x64, 0x00, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x00,
        0x00, 0x00, 0x9C, 0xFF, 0xF0, 0x00, 0x00, 0x00, 0x64, 0x00, 0x60, 0xFF,
        0x00, 0x00, 0x00, 0x00, 0x60, 0xFF, 0x00, 0x00, 0x64, 0x00, 0x60, 0xFF,
        0x00, 0x00, 0x9C, 0xFF, 0x10, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x10, 0xFF,
        0x00, 0x00, 0x64, 0x00, 0x10, 0xFF, 0x00, 0x00, 0x9C, 0xFF, 0x01, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    });

    // =====================================================================================
    // The portrait coordinate table
    // =====================================================================================

    // GHIDRA: DAT_80084184 @ 0x80084184 (VS.EXE)
    // Twelve rows of six halfwords on a 12-byte stride — three (x, y) VRAM pairs per portrait slot.
    // Both ends are closed by the loop itself: it runs twelve times and advances its cursors by
    // 0xC each turn, so it reads 12 * 12 = 144 bytes, and the x values it yields are the VRAM
    // columns 0x180..0x1B0 and 0x240..0x270 of a 0xC-halfword tile against y rows 0x000..0x1C0 of a
    // 0x30-line tile.
    //
    // These 144 bytes are IDENTICAL to TITLE.EXE's g_FaceVramCoordTable @ 0x8007A220, which
    // TITLE_EXE/FaceImages.cs already lifted out of that overlay's image. Same object code, same
    // constant pool, two link addresses. It is NOT reused from there: TITLE_EXE is a separate
    // program at separate addresses, and reaching into it would make this file's `GHIDRA:` lines
    // false.
    private const int Dat80084184Address = unchecked((int)0x80084184);

    // GHIDRA: DAT_80084186 @ 0x80084186 (VS.EXE) — column 1 of the same table.
    private const int Dat80084186Address = unchecked((int)0x80084186);

    // GHIDRA: DAT_80084188 @ 0x80084188 (VS.EXE) — column 2 of the same table.
    private const int Dat80084188Address = unchecked((int)0x80084188);

    // GHIDRA: DAT_8008418a @ 0x8008418A (VS.EXE) — column 3 of the same table.
    private const int Dat8008418aAddress = unchecked((int)0x8008418A);

    // GHIDRA: DAT_8008418c @ 0x8008418C (VS.EXE) — column 4 of the same table.
    private const int Dat8008418cAddress = unchecked((int)0x8008418C);

    // GHIDRA: DAT_8008418e @ 0x8008418E (VS.EXE) — column 5 of the same table.
    // Ghidra labels all six columns separately because the original takes six independent addresses
    // into one 12-byte-stride table: 0x80084184 and 0x8008418E are walked as roaming cursors (s3
    // and s4, both advanced by 0xC at 0x8005CE30 and 0x8005CE2C), while the middle four are read as
    // label + a byte offset that advances by 0xC.
    private const int Dat8008418eAddress = unchecked((int)0x8008418E);

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: same reason as the record above — the table is only ever reached by raw PSX
    // address, so LibGpu.RamRegion declares it and VS_EXE_exe.ResolveAddress finds it without a new
    // chain entry.
    internal static readonly byte[] DAT_80084184 = LibGpu.RamRegion(Dat80084184Address, new byte[]
    {
        0x80, 0x01, 0x00, 0x00, 0x90, 0x01, 0x00, 0x00, 0xA0, 0x01, 0x00, 0x00,
        0xB0, 0x01, 0x00, 0x00, 0x80, 0x01, 0x40, 0x00, 0x90, 0x01, 0x40, 0x00,
        0xA0, 0x01, 0x40, 0x00, 0xB0, 0x01, 0x40, 0x00, 0x80, 0x01, 0x80, 0x00,
        0x90, 0x01, 0x80, 0x00, 0xA0, 0x01, 0x80, 0x00, 0xB0, 0x01, 0x80, 0x00,
        0x80, 0x01, 0xC0, 0x00, 0x90, 0x01, 0xC0, 0x00, 0xA0, 0x01, 0xC0, 0x00,
        0xB0, 0x01, 0xC0, 0x00, 0x80, 0x01, 0x00, 0x01, 0x90, 0x01, 0x00, 0x01,
        0xA0, 0x01, 0x00, 0x01, 0xB0, 0x01, 0x00, 0x01, 0x80, 0x01, 0x40, 0x01,
        0x90, 0x01, 0x40, 0x01, 0xA0, 0x01, 0x40, 0x01, 0xB0, 0x01, 0x40, 0x01,
        0x60, 0x02, 0x00, 0x01, 0x70, 0x02, 0x00, 0x01, 0x60, 0x02, 0x40, 0x01,
        0x70, 0x02, 0x40, 0x01, 0x40, 0x02, 0x80, 0x01, 0x50, 0x02, 0x80, 0x01,
        0x60, 0x02, 0x80, 0x01, 0x70, 0x02, 0x80, 0x01, 0x40, 0x02, 0xC0, 0x01,
        0x50, 0x02, 0xC0, 0x01, 0x60, 0x02, 0xC0, 0x01, 0x70, 0x02, 0xC0, 0x01,
    });

    // =====================================================================================
    // The addresses inside buffers other files own
    // =====================================================================================
    // Ghidra labels each of these separately, and the original reaches every one of them as a raw
    // address. Only the addresses are declared here; the BYTES are VS_EXE/FileIo.cs's two buffers,
    // and FileIo.Resolve already answers for both spans. Nothing below allocates storage a second
    // time.

    // GHIDRA: DAT_801d2020 @ 0x801D2020 (VS.EXE)
    // g_cdFileBufferTable + 0x20 — the first 0xC x 0x30 portrait tile of the two sectors just read.
    private const int Dat801d2020Address = unchecked((int)0x801D2020);

    // GHIDRA: DAT_801d24a0 @ 0x801D24A0 (VS.EXE)
    // + 0x4A0, the second tile. 0x4A0 - 0x20 = 0x480 = 0xC * 0x30 * 2, so the three tiles are
    // contiguous.
    private const int Dat801d24a0Address = unchecked((int)0x801D24A0);

    // GHIDRA: DAT_801d2920 @ 0x801D2920 (VS.EXE)
    // + 0x920, the third tile, ending at +0xDA0 inside the 0x1000 bytes the read asks for.
    private const int Dat801d2920Address = unchecked((int)0x801D2920);

    // GHIDRA: DAT_800a1058 @ 0x800A1058 (VS.EXE) — DAT_800a0d58 + 0x300.
    private const int Dat800a1058Address = unchecked((int)0x800A1058);

    // GHIDRA: DAT_800a3058 @ 0x800A3058 (VS.EXE) — DAT_800a0d58 + 0x2300.
    private const int Dat800a3058Address = unchecked((int)0x800A3058);

    // GHIDRA: DAT_800a5358 @ 0x800A5358 (VS.EXE) — DAT_800a0d58 + 0x4600.
    private const int Dat800a5358Address = unchecked((int)0x800A5358);

    // GHIDRA: DAT_800a7158 @ 0x800A7158 (VS.EXE) — DAT_800a0d58 + 0x6400.
    private const int Dat800a7158Address = unchecked((int)0x800A7158);

    // GHIDRA: DAT_800a0d58 @ 0x800A0D58 (VS.EXE) — the LZSS staging buffer's own head.
    // The six blocks tile it exactly: +0x000 (0x80 x 1), +0x100 (0x100 x 1), +0x300 (0x20 x 0x80),
    // +0x2300 (0x28 x 0x70), +0x4600 (0x28 x 0x60) and +0x6400 (0x40 x 0x20), each ending where the
    // next begins and the last at +0x7400, inside the 0x8000 FileIo declares. That tiling is the
    // self-check that the six absolute addresses were read correctly.
    private const int Dat800a0d58Address = unchecked((int)0x800A0D58);

    // GHIDRA: DAT_800a0e58 @ 0x800A0E58 (VS.EXE) — DAT_800a0d58 + 0x100.
    private const int Dat800a0e58Address = unchecked((int)0x800A0E58);

    // =====================================================================================
    // FUN_8005cbe0
    // =====================================================================================

    // GHIDRA: FUN_8005cbe0 @ 0x8005CBE0 (VS.EXE)
    // 920 bytes, one caller (main @ 0x80062428), six callees.
    //
    // The local names are Ghidra's and stay Ghidra's: puVar1 is the record base, psVar4 doubles as
    // the handover cursor in the first half and the record's id cursor in the second, sVar7 is the
    // running ordinal and sVar8 the per-team count. Nothing here is renamed to something friendlier,
    // because nothing here has a closed meaning beyond what the writes themselves say.
    //
    // THE ORDINAL IS NOT RESET BETWEEN THE TEAMS. sVar7 is set to 1 once, at 0x8005CBFC, and the
    // second loop's prologue at 0x8005CC94..0x8005CCA8 reloads every cursor and zeroes sVar8 but
    // leaves sVar7 alone. So a full 3-versus-3 stamps 1, 2, 3 into +0x112..+0x116 and 4, 5, 6 into
    // +0x11E..+0x122 — one sequence across both teams, which is what TITLE.EXE's shipped records
    // show too (record 0: 1, 2, 3 for its three, then 4 for the lone opponent).
    //
    // THE LOOP COUNTER ADVANCES IN A DELAY SLOT. `addiu s0, s0, 1` sits in the delay slot of the
    // `beq` that skips a zero entry (0x8005CC40 / 0x8005CCB4) and `addiu a1, a1, 2` in the delay
    // slot of the loop-bottom branch, so both the count and the handover cursor advance on a skipped
    // entry as well. The same is true of the twelve-loop: `addiu s6, s6, 2` is in the delay slot at
    // 0x8005CD60 and the CLUT column iVar11 is bumped outside the `if`, so a zero id still consumes
    // its slot and its column. Every one of those is reproduced below by putting the increment
    // outside the branch, exactly where the decompiler puts it.
    internal static void FUN_8005cbe0()
    {
        int puVar1;
        int puVar2;
        int puVar3;
        int psVar4;
        int psVar5;
        int psVar6;
        short sVar7;
        short sVar8;
        int iVar9;
        int iVar10;
        int iVar11;

        // The original's CdlFILE embeds its CdlLOC by value and the decompiler prints the two
        // halves separately — `CdlLOC CStack_60` with `undefined4 local_5c` right behind it at +4,
        // which is the size field FUN_80061d98 divides into sectors. The port's CdlFILE holds its
        // pos as a reference field, so each stack record is given one here. Same treatment as
        // TITLE_EXE/FaceImages.cs gives the identical pair.
        CdlFILE CStack_60 = new() { pos = new CdlLOC() };
        CdlFILE CStack_48 = new() { pos = new CdlLOC() };
        int local_30;

        puVar1 = PTR_DAT_800844b8;
        psVar4 = Dat801ff102Address;
        sVar8 = 0;
        sVar7 = 1;
        iVar9 = 0;
        psVar6 = PTR_DAT_800844b8 + 0x52;
        puVar3 = PTR_DAT_800844b8 + 0x22;
        puVar2 = PTR_DAT_800844b8 + 10;
        psVar5 = PTR_DAT_800844b8 + 0x112;

        // TEAM A — handover ids 0..2 into record slots 0..2.
        // The reads are `lhu` in the image (0x8005CC38 and 0x8005CC68) even though Ghidra types the
        // cursor `short *`; ReadU16 is what the hardware does and the `!= 0` test cannot tell the
        // two apart anyway.
        do
        {
            iVar9 = iVar9 + 1;
            if (PsxRam.ReadU16(psVar4) != 0)
            {
                sVar8 = (short)(sVar8 + 1);
                PsxRam.WriteU16(puVar2, (ushort)(PsxRam.ReadU16(puVar2) | 0x81));
                puVar2 = puVar2 + 2;
                PsxRam.WriteU16(puVar3, (ushort)(PsxRam.ReadU16(puVar3) | 0x1e20));
                puVar3 = puVar3 + 2;
                PsxRam.WriteU16(psVar6, PsxRam.ReadU16(psVar4));
                psVar6 = psVar6 + 2;
                PsxRam.WriteU16(psVar5, (ushort)sVar7);
                psVar5 = psVar5 + 2;
                sVar7 = (short)(sVar7 + 1);
            }

            psVar4 = psVar4 + 2;
        }
        while (iVar9 < 3);

        PsxRam.WriteU16(puVar1, (ushort)sVar8);

        // TEAM B — the cursors move six halfwords along inside the record (0x0A -> 0x16,
        // 0x22 -> 0x2E, 0x52 -> 0x5E, 0x112 -> 0x11E, twelve bytes each), while psVar4 simply keeps
        // going in the handover block. The OR constant changes from 0x1E20 to 0x2D08; the 0x81 does
        // not.
        psVar6 = puVar1 + 0x5e;
        puVar3 = puVar1 + 0x2e;
        puVar2 = puVar1 + 0x16;
        psVar5 = puVar1 + 0x11e;
        sVar8 = 0;
        iVar9 = 3;
        do
        {
            iVar9 = iVar9 + 1;
            if (PsxRam.ReadU16(psVar4) != 0)
            {
                sVar8 = (short)(sVar8 + 1);
                PsxRam.WriteU16(puVar2, (ushort)(PsxRam.ReadU16(puVar2) | 0x81));
                puVar2 = puVar2 + 2;
                PsxRam.WriteU16(puVar3, (ushort)(PsxRam.ReadU16(puVar3) | 0x2d08));
                puVar3 = puVar3 + 2;
                PsxRam.WriteU16(psVar6, PsxRam.ReadU16(psVar4));
                psVar6 = psVar6 + 2;
                PsxRam.WriteU16(psVar5, (ushort)sVar7);
                psVar5 = psVar5 + 2;
                sVar7 = (short)(sVar7 + 1);
            }

            psVar4 = psVar4 + 2;
        }
        while (iVar9 < 6);

        PsxRam.WriteU16(puVar1 + 2, (ushort)sVar8);

        // THE PORTRAITS. One search of FACE.B, then twelve seeks off its start sector.
        FileIo.WaitSearchFile("\\CHR_DATA\\FACE.B;1".ToCharArray(), CStack_60);
        psVar4 = puVar1 + 0x52;
        local_30 = CdPosToInt(CStack_60.pos);
        iVar9 = 0;
        iVar11 = 0xa00000;
        puVar2 = Dat80084184Address;
        puVar3 = Dat8008418eAddress;
        iVar10 = 0;

        // CStack_48 is never searched: CdIntToPos writes its position and this line writes its
        // size, 0x1000, which is the two sectors a portrait occupies. Set once, outside the loop
        // (0x8005CD4C..0x8005CD50).
        CStack_48.size = 0x1000;

        do
        {
            sVar7 = (short)PsxRam.ReadU16(psVar4);
            psVar4 = psVar4 + 2;
            if (sVar7 != 0)
            {
                CdIntToPos(local_30 + ((sVar7 + -1) * 2), CStack_48.pos);
                FileIo.ReadCDData(CStack_48, FileIo.g_cdFileBufferTableAddress, 0);
                FileIo.LoadImage_ReturnTPageOrClutId(
                    Dat801d2020Address,
                    PsxRam.ReadU16(puVar2),
                    PsxRam.ReadU16(Dat80084186Address + iVar10), 0xc, 0x30, 0);
                FileIo.LoadImage_ReturnTPageOrClutId(
                    Dat801d24a0Address,
                    PsxRam.ReadU16(Dat80084188Address + iVar10),
                    PsxRam.ReadU16(Dat8008418aAddress + iVar10), 0xc, 0x30, 0);
                FileIo.LoadImage_ReturnTPageOrClutId(
                    Dat801d2920Address,
                    PsxRam.ReadU16(Dat8008418cAddress + iVar10),
                    PsxRam.ReadU16(puVar3), 0xc, 0x30, 0);

                // The CLUT strip out of the head of the buffer. iVar11 starts at 0xA00000 and grows
                // by 0x100000 every turn — the increment is OUTSIDE this `if` — and the upload takes
                // its top halfword, so x runs 0xA0, 0xB0, 0xC0 ... one strip per slot whether or not
                // the slot was filled.
                FileIo.LoadImage_ReturnTPageOrClutId(
                    FileIo.g_cdFileBufferTableAddress,
                    (ushort)((uint)iVar11 >> 0x10), 0x1e6, 0x10, 1, 1);
            }

            iVar11 = iVar11 + 0x100000;
            puVar3 = puVar3 + 12;
            puVar2 = puVar2 + 12;
            iVar9 = iVar9 + 1;
            iVar10 = iVar10 + 0xc;
        }
        while (iVar9 < 0xc);

        // THE SHARED OVERLAY ART. CStack_60 is REUSED, and the size CdSearchFile just filled in is
        // overwritten with a hard 0x3800 — seven sectors — before the read. The store is in the
        // delay slot of the call at 0x8005CE68, so it lands first, exactly as printed.
        FileIo.WaitSearchFile("\\CHR_DATA\\OV_CHR_A.B;1".ToCharArray(), CStack_60);
        CStack_60.size = 0x3800;
        FileIo.ReadCDData(CStack_60, FileIo.g_cdFileBufferTableAddress, 0);
        FileIo.DecompressLZSS(FileIo.g_cdFileBufferTable, 0, FileIo.DAT_800a0d58, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(Dat800a1058Address, 0x240, 0x100, 0x20, 0x80, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(Dat800a3058Address, 0x158, 0x180, 0x28, 0x70, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(Dat800a5358Address, 0x398, 0x180, 0x28, 0x60, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(Dat800a7158Address, 0x380, 0x1e0, 0x40, 0x20, 0);
        FileIo.LoadImage_ReturnTPageOrClutId(Dat800a0d58Address, 0, 0x1e6, 0x80, 1, 1);
        FileIo.LoadImage_ReturnTPageOrClutId(Dat800a0e58Address, 0, 0x1ec, 0x100, 1, 1);
    }
}
