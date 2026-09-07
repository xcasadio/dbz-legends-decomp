using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// The REST of VS.EXE's sound module: everything below the 0x800632C4 SDK line that SoundDriver.cs,
// AnimCmdSound.cs, BattleScene.cs and BattleManager.cs had left standing as empty stubs, plus the
// CD-audio half of the module (0x800603xx..0x80060Cxx) which nothing in the port had touched at all.
//
// WHAT THIS FILE IS FOR. Four sibling files each reached this family from a different side and each
// wrote a BLOCKED stub with an honest note saying "next slice's work". Those notes are now spent:
// every one of the fifteen game-code functions below was decompiled IN FULL out of Ghidra program
// /VS.EXE and transliterated here. Nothing in this file is a stub of a game function. The only
// stubs it declares are three libsnd entries above the 0x800632C4 line that PsxSdkMonogame does not
// export under any name, and they are marked as such.
//
// THE ONE ADDRESS RULE THAT SHAPED THE FILE. Rule 13 of runtime_port_agent.md: at or above
// 0x800632C4 is PSX SDK and is CALLED, never ported. That is why FUN_8006b4a0, FUN_8006b88c,
// FUN_8006bdd8 and FUN_8006bcd0 are reached through SoundDriver's existing stubs rather than
// re-declared here (one address, one declaration -- see the duplicate-symbol note below), and why
// SsSetNck / SsUtSetReverb* / SsSetSerialAttr / SpuStQuit / CdMix / CdGetToc / CdReady and the rest
// go straight to LibSnd, LibSpu, LibCd and LibEtc.
//
// DUPLICATE SYMBOLS. Before a line was written, every address in this file was searched across the
// whole of VS_EXE/. Six of the fifteen already had an EMPTY body somewhere else -- FUN_80060364,
// FUN_80060478 and FUN_8006071c in SoundDriver.cs; FUN_8005fb9c, FUN_8005fcec, FUN_8005fd9c,
// FUN_8005ff5c and FUN_80060144 in AnimCmdSound.cs; FUN_8005ef20 in BattleManager.cs; FUN_8005f530
// in BattleScene.cs. This slice may not edit those files, so those stubs are STILL THERE and an
// empty body silently beats a real one at every one of their call sites. The report that accompanies
// this file lists each of them by file and line for the main session to delete. Until it does, this
// file is dead code for those ten addresses -- that is stated here rather than left to be
// discovered.
//
// SIGNATURES. Where a sibling's stub and Ghidra's prototype disagree, GHIDRA WINS and the report
// names the call sites that need a cast. Two disagreements are real and neither is cosmetic:
//   * FUN_8005ef20 -- Ghidra's decompiled prototype has ONE parameter because the body never reads
//     $a1. Eleven call sites (FighterCombatArms.cs x10, BattleManager.cs:808) set $a1 all the same,
//     so the parameter is real and is kept here, unread, with the evidence written above it.
//   * FUN_8006071c -- SoundDriver's stub is (int, int); Ghidra's is (undefined2, uint). The second
//     argument is used unsigned in three of the four cases and sign-extended in one, so it is
//     declared uint and the sole call site casts.
//
// EVIDENCE CHANNEL. Every function below came from mcp ReVa get-decompilation against /VS.EXE, at
// full length, no truncation. Six load/store widths that the decompiler's printout left ambiguous
// were settled by reading the instruction bytes with read-memory rather than guessed:
//     0x800603A0  sb  v0,0x1b4(gp)    DAT_8008d2b0 is a BYTE
//     0x800603B0  sw  v0,0x194(gp)    DAT_8008d290 is a WORD
//     0x8006040C  sb  v1,0x1c0(gp)    DAT_8008d2bc..bf are four BYTES, not a word
//     0x800606B4  sb  v0,0x1c4(gp)    DAT_8008d2c0 is a BYTE
//     0x800607C8  lhu v0,-0xfe2(v0)   DAT_801ff01e is an UNSIGNED HALFWORD
//     0x800604B4  lbu a0,0x1b9(gp)    DAT_8008d2b5 is a BYTE, and it is DAT_8008d2b4[1]
// A seventh is the one that would have been silently wrong: at 0x800607A8 the shift in FUN_8006071c
// case 1 is `srl a2,v0,4` -- a LOGICAL shift -- even though the `bgez`/`addiu 0xf` pair in front of
// it is the compiler's signed-divide-by-16 idiom. Ghidra prints it as `uVar4 >> 4` on a uint and
// that printout is literally right. Case 3 four instructions later uses `sra`. The original is
// inconsistent with itself; rule 12 says reproduce it, so both are reproduced exactly and the
// inconsistency is noted at each site instead of being smoothed over.
internal static class SoundEffects
{
    // ==============================================================================================
    // GLOBALS. Every address below was searched across VS_EXE/ first and is declared in NO other
    // file. The gp base for this program is 0x8008D0FC, so a `0xNNN(gp)` in the disassembly quoted
    // in a comment resolves to 0x8008D0FC + 0xNNN.
    // ==============================================================================================

    // GHIDRA: DAT_8008d288 @ 0x8008D288 (VS.EXE) -- gp+0x18C
    // The last CdSync(1, ...) status. `sw` at 0x8006045C and 0x80060560, so a WORD. FUN_80060364
    // zeroes it and FUN_80060478 refreshes it once per frame; nothing in this slice reads it, which
    // is faithful -- its readers are in the 0x80029xxx CD-audio caller family, not ported here.
    internal static int DAT_8008d288;

    // GHIDRA: DAT_8008d28c @ 0x8008D28C (VS.EXE) -- gp+0x190
    // The last CdReady(1, ...) status. `sw v0,0x190(gp)` at 0x800604A4. Compared against 1
    // immediately afterwards, which is CdlDataReady.
    internal static int DAT_8008d28c;

    // GHIDRA: DAT_8008d290 @ 0x8008D290 (VS.EXE) -- gp+0x194
    // CdGetToc's return MINUS ONE, i.e. the number of real audio tracks, the lead-in entry dropped.
    // `addiu v0,v0,-0x1` then `sw v0,0x194(gp)` at 0x800603AC..0x800603B0.
    internal static int DAT_8008d290;

    // GHIDRA: DAT_8008d294 @ 0x8008D294 (VS.EXE) -- gp+0x198
    // Set to 3 by FUN_80060364 and never read anywhere in this slice. PARTIAL: it is written beside
    // DAT_8008d298 and DAT_8008d29c with the same constant in the same three instructions, which
    // makes it look like "the first playable track", but that is a reading of a neighbourhood and
    // not of a use, so no name is put on it.
    internal static int DAT_8008d294;

    // GHIDRA: DAT_8008d298 @ 0x8008D298 (VS.EXE) -- gp+0x19C
    // The track the drive reports it is ON. FUN_80060478 recomputes it every frame from the BCD
    // byte CdReady leaves in the result block; FUN_800605d8 forces it to the track it just asked
    // for. Both stores are `sw`.
    internal static int DAT_8008d298;

    // GHIDRA: DAT_8008d29c @ 0x8008D29C (VS.EXE) -- gp+0x1A0
    // The track the game WANTS. FUN_800605d8 sets it, FUN_800609ec and FUN_80060478 seek to it, and
    // FUN_80060478's "drive has run past the end of the track" test is exactly
    // `DAT_8008d29c < DAT_8008d298`. That pair is what makes CD audio loop.
    internal static int DAT_8008d29c;

    // GHIDRA: DAT_8008d2a0 @ 0x8008D2A0 (VS.EXE) -- gp+0x1A4
    // The CD-audio mode word, and the only three bits this slice can see used are these:
    //     bit 0  set = "do not run the per-frame service"     FUN_80060478 returns immediately
    //     bit 1  set = "re-seek on loop"                      FUN_80060478 issues CdlPlay again
    //     bit 2  set = "stop first"                           FUN_80060478 issues CdlStop first
    // FUN_80060364 initialises it to 1. FUN_800605d8 ORs the caller's flags in and CLEARS bit 0 on
    // its CdlPlay path (so the service runs) while LEAVING it on its CdlSetloc path.
    internal static int DAT_8008d2a0;

    // GHIDRA: DAT_8008d2b0 @ 0x8008D2B0 (VS.EXE) -- gp+0x1B4
    // The CdlSetmode PARAMETER BLOCK, one byte wide -- `sb v0,0x1b4(gp)` at 0x800603A0 -- and
    // handed to CdControlB(0x0E, &DAT_8008d2b0, ...) at two sites in FUN_800605d8. 0x0E is
    // CdlSetmode and its parameter is a single mode byte, so the array is one meaningful byte long.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: the original passes the ADDRESS of this byte; CdControlB's prototype is u_char*, so
    // the storage is an array and index 0 is the byte at 0x8008D2B0. Four bytes rather than one
    // because the console's parameter block is word-aligned and libcd copies from it by length.
    internal static readonly byte[] DAT_8008d2b0 = new byte[4];

    // GHIDRA: DAT_8008d2b4 @ 0x8008D2B4 (VS.EXE) -- gp+0x1B8
    // The shared eight-byte drive RESULT block. Every CdControlB / CdControl / CdSync / CdReady call
    // in this family passes it, and it is a single static block rather than a stack local, unlike
    // the ones SoundDriver.cs had to model.
    //
    // DAT_8008d2b5 @ 0x8008D2B5 IS NOT SEPARATE STORAGE: it is this array's index 1, and the
    // instruction proves it -- `lbu a0,0x1b9(gp)` at 0x800604B4, gp+0x1B9 = 0x8008D2B5 = the second
    // byte of the block at 0x8008D2B4. Ghidra labels it separately only because .bss carries no
    // type. Declaring it twice would have been exactly the duplicate-symbol defect this repository
    // keeps shipping, so it is read as DAT_8008d2b4[1] and nothing is declared for it.
    internal static readonly byte[] DAT_8008d2b4 = new byte[8];

    // GHIDRA: DAT_8008d2bc @ 0x8008D2BC (VS.EXE) -- gp+0x1C0
    // GHIDRA: DAT_8008d2bd @ 0x8008D2BD (VS.EXE) -- gp+0x1C1
    // GHIDRA: DAT_8008d2be @ 0x8008D2BE (VS.EXE) -- gp+0x1C2
    // GHIDRA: DAT_8008d2bf @ 0x8008D2BF (VS.EXE) -- gp+0x1C3
    // FOUR CONSECUTIVE BYTES that together ARE the CdlATV the CD-audio-to-SPU routing matrix takes:
    // all three call sites spell it `CdMix((CdlATV *)&DAT_8008d2bc)`. The width is settled by the
    // instructions, not by the type -- `sb v1,0x1c0(gp)` / `sb v0,0x1c1(gp)` / `sb v1,0x1c2(gp)` /
    // `sb v0,0x1c3(gp)` at 0x8006040C..0x80060418.
    //
    // THEY ARE DECLARED AS FOUR BYTES AND NOT AS ONE CdlATV, deliberately. FUN_8006071c reads and
    // writes them individually more than twenty times, always by their own names, and the whole
    // point of that function is the four independent clamps it applies to them. Folding them into
    // one object would have replaced four original global names with four field accesses on an
    // invented aggregate; the object is built at the CdMix call instead (see MixCdlATV below).
    //
    // WHAT THE FOUR CHANNELS ARE, from FUN_80060364's own two presets and nowhere else: with
    // DAT_801ff01e == 0 the values are (0x7F, 0x08, 0x7F, 0x08) and with it non-zero they are
    // (0x3F, 0x3F, 0x3F, 0x3F). A 0x7F/0x08 pair repeated twice is a hard-panned stereo matrix
    // (L->L strong, L->R weak, R->R strong, R->L weak) and 0x3F across the board is the mono
    // downmix. PARTIAL: which of val0..val3 is which cross-term is libcd's business and is not
    // asserted here.
    internal static byte DAT_8008d2bc;
    internal static byte DAT_8008d2bd;
    internal static byte DAT_8008d2be;
    internal static byte DAT_8008d2bf;

    // GHIDRA: DAT_8008d2c0 @ 0x8008D2C0 (VS.EXE) -- gp+0x1C4
    // The last CD command CODE issued, stored one instruction BEFORE the CdControl/CdControlB that
    // issues it. `sb v0,0x1c4(gp)` at 0x800606B4 and 0x80060650, so a BYTE. Values seen: 2
    // (CdlSetloc), 3 (CdlPlay), 8 (CdlStop). Nothing in this slice reads it back; it is a trace
    // cell for the drive-error paths in the 0x80029xxx family, which is not ported.
    internal static byte DAT_8008d2c0;

    // GHIDRA: DAT_8008d2e0 @ 0x8008D2E0 (VS.EXE) -- gp+0x1E4, the PsyQ heap HEAD pointer
    // GHIDRA: DAT_8008d2e8 @ 0x8008D2E8 (VS.EXE) -- gp+0x1EC, the remaining size
    // GHIDRA: DAT_8008d2f0 @ 0x8008D2F0 (VS.EXE) -- gp+0x1F4, the limit
    //
    // Heap.cs already names all three -- see its "THE HEAP'S OWN GLOBALS IN VS.EXE" table -- and
    // deliberately declares NO storage for them, because in this port InitHeap/malloc/free are
    // routed to PsxSdkMonogame's PsxHeap and the console's three cells have no reader. That decision
    // stands and this file does not touch Heap.cs.
    //
    // The storage is declared HERE because FUN_80062cb8 below is a transliteration of the function
    // that owns them, and a transliteration with its globals removed is not a transliteration. See
    // that function's own DEVIATION note for what follows: it is faithful and it is unreachable.
    internal static int DAT_8008d2e0;
    internal static int DAT_8008d2e8;
    internal static int DAT_8008d2f0;

    // GHIDRA: DAT_800990d8 @ 0x800990D8 (VS.EXE)
    // THE TABLE OF CONTENTS, an array of CdlLOC. CdGetToc fills it in FUN_80060364 and three
    // separate seek paths index it: FUN_800605d8 by `&DAT_800990d8 + ((param_2 << 16) >> 14)`,
    // which is entry [(short)param_2] at four bytes each, and FUN_800609ec / FUN_80060478 by
    // `&DAT_800990d8 + DAT_8008d29c * 4`, the same scaling written the other way.
    //
    // PARTIAL: THE LENGTH IS NOT CLOSED. Nothing in the image bounds the index, and the array's
    // extent in .bss was not read. 100 entries is the Red Book maximum (99 tracks plus the lead-in
    // entry CdGetToc writes at [0]) and is what LibCd.CdGetToc2's own loop can produce at most, so
    // it is the smallest length that cannot be overrun by a correct disc. It is a bound, not a
    // measurement, and it is labelled as such.
    internal static readonly LibCd.CdlLOC[] DAT_800990d8 = CreateTocArray();

    // GHIDRA: DAT_801ff01e @ 0x801FF01E (VS.EXE)
    // THE MONO / STEREO SETTING, and it is an UNSIGNED HALFWORD: `lui v0,0x8020` then
    // `lhu v0,-0xfe2(v0)` at 0x800607C4..0x800607C8, and 0x80200000 - 0x0FE2 = 0x801FF01E exactly.
    // The decompiler prints a bare `DAT_801ff01e == 0` with no width, which is precisely the kind of
    // printout the mandate says to decode from the bytes rather than assume.
    //
    // WHAT IT SELECTS is closed by the two presets it gates in FUN_80060364 and by the four volume
    // arithmetics it gates in FUN_8006071c: ZERO takes the 0x7F/0x08 hard-panned path with a
    // 0..0x7F range and a divide-by-16 for the cross terms; NON-ZERO takes the flat 0x3F path with a
    // 0..0x3F range and no cross-term arithmetic at all. That is stereo versus mono. The cell itself
    // is written nowhere in this slice -- its writer is the options screen, which is not ported --
    // so on this port it reads 0 and the stereo path is always taken.
    internal const int Dat801ff01eAddress = unchecked((int)0x801FF01E);

    // GHIDRA: DAT_8008d214 @ 0x8008D214 (VS.EXE) -- gp+0x118
    // THE ROUND-ROBIN SE VOICE CURSOR. AnimCmdSound.cs already describes it from the other side --
    // "a 22-slot bank (DAT_8008d214 walking 0x11..0x16)" -- but declares no storage for it, and no
    // other file in VS_EXE/ does either, so it is declared here.
    //
    // The walk is the same in FUN_8005fb9c and FUN_8005fd9c and it is written as a POST-increment
    // with a WRAP TEST THAT IS OFF BY FIVE: `DAT_8008d214 = DAT_8008d214 + 1; if (0x16 < DAT_8008d214)
    // DAT_8008d214 = 0x11;`. Six slots, 0x11..0x16, and it reaches 0x17 for exactly one comparison
    // before snapping back to 0x11 -- the value 0x17 is never USED, because the wrap happens before
    // the read. That is the original's shape and it is reproduced, not tidied.
    //
    // INITIAL VALUE: 0. The first call therefore uses slot 1, not slot 0x11. That is what the image
    // does -- the cell is .bss and nothing seeds it -- and rule 12 forbids seeding it here. The
    // sibling cursors DAT_8008d210 and DAT_8008d212 in SoundDriver.cs DO carry image initialisers
    // (0x11 and 0x15); this one does not, and the difference is real.
    internal static short DAT_8008d214;

    // GHIDRA: DAT_80084c10 @ 0x80084C10 (VS.EXE), and its neighbours DAT_80084c11 / DAT_80084c12
    // A TABLE OF FOUR-BYTE ROWS holding the (prog, tone, note, pad) triple libsnd's key-on is keyed
    // on -- the same shape SoundDriver.cs already documents for DAT_80084c50, which is the very same
    // table 0x40 bytes further in. Read out of the image at 0x80084C10, sixteen rows:
    //     00 00 18 00 | 00 01 19 00 | 00 02 1A 00 | 00 03 1B 00
    //     00 04 1C 00 | 00 05 1D 00 | 00 06 1F 00 | 00 07 23 00
    //     00 08 24 00 | 00 09 26 00 | 01 00 29 00 | 01 01 2B 00
    //     01 02 2D 00 | 01 03 2F 00 | 01 04 30 00 | 01 05 32 00
    // so prog 0 for rows 0..9 and prog 1 from row 10, tone counting up inside each prog, note
    // climbing monotonically across the whole table. A chromatic run of sound effects.
    //
    // THE INDEX SCALING IS THE THING TO GET RIGHT, and the two readers scale it two different ways
    // that come to the same place: FUN_8005fd9c uses `(int)((param_1 - 1) * 0x10000) >> 0xe` and
    // FUN_8005ef20 uses `(int)((uint)(ushort)(param_1 - 1) << 0x10) >> 0xe`. A left shift of 16
    // followed by an arithmetic right shift of 14 is "sign-extend from 16 bits, then multiply by
    // four" -- the row stride. Both therefore address row (short)(param_1 - 1).
    internal const int Dat80084c10Address = unchecked((int)0x80084C10);
    internal const int Dat80084c11Address = unchecked((int)0x80084C11);
    internal const int Dat80084c12Address = unchecked((int)0x80084C12);

    // GHIDRA: DAT_80084ad4 @ 0x80084AD4 (VS.EXE), and its neighbours DAT_80084ad6 / DAT_80084ad8
    // A TABLE OF SIX-BYTE ROWS, three HALFWORDS each, indexed by `(param_1 & 0x7f) * 6` in
    // FUN_80060144. Read out of the image at 0x80084AD4, the first eight rows:
    //     0000 0000 0000 | 05D2 05D3 05D4 | 05D2 05D3 05D4 | 05D2 05D3 05D4
    //     05DA 05DA 05DB | 05DA 05DA 05DB | 05E0 05E1 05E2 | 05E0 05E1 05E2
    // Row 0 is all zeroes -- the "no line" row -- and every other row holds three clip indices
    // around 0x5D0. The values are small enough that bit 15 and bit 14 are free, which is exactly
    // what FUN_80060144 uses them for: it writes bit 15 back INTO the table as a "this alternative
    // has been used" latch, and reads bit 14 out of the pitch cell as a "there is a second half
    // pending" flag. The table is therefore MUTABLE STATE, not constant data, and it lives in the
    // executable image -- which is why it is reached through PsxRam and not copied.
    internal const int Dat80084ad4Address = unchecked((int)0x80084AD4);
    internal const int Dat80084ad6Address = unchecked((int)0x80084AD6);
    internal const int Dat80084ad8Address = unchecked((int)0x80084AD8);

    // GHIDRA: DAT_80084868 @ 0x80084868 (VS.EXE)
    // A TABLE OF BYTES indexed by `param_2 & 0xffff` in FUN_8005ff5c, whose value is then used as a
    // ROW NUMBER into the range table below -- `((tbl[param_2] - 1) * 0x10 + ...)`. Zero means "this
    // character has no voice set" and the function bails. Read out of the image at 0x80084868:
    //     00 01 02 03 04 07 01 09 02 02 0C 0D 07 00 01 02
    //     0E 07 05 06 08 00 00 0A 0B 0F 10 00 00 00 11 12
    //     00 00 01 07 12 00 02 08 00 07 10 00
    // Repeats are the point: several characters share a voice bank (0x07 appears five times).
    internal const int Dat80084868Address = unchecked((int)0x80084868);

    // GHIDRA: DAT_80084894 @ 0x80084894 (VS.EXE) and DAT_80084896 @ 0x80084896 (VS.EXE)
    // THE VOICE-CLIP RANGE TABLE, a pair of HALFWORDS per entry: start at +0, end at +2. The index
    // arithmetic in FUN_8005ff5c is `((row - 1) * 0x10 + (ushort)(param_1 - 1) * 2) * 2`, which is
    // (row - 1) * 32 + (param_1 - 1) * 4 bytes -- so eight entries of four bytes per row, and
    // param_1 selects one of them. Read out of the image from 0x80084894:
    //     0000 0028 | 0028 0053 | 0053 006B | 006B 00A7 | 00A7 00CC | ...
    // Consecutive half-open ranges, each entry's start equal to the previous entry's end. The
    // function picks a clip inside one with `start + (param_3 % (end - start))`, and the `end != 0`
    // test in front of it is how an unpopulated slot is skipped.
    internal const int Dat80084894Address = unchecked((int)0x80084894);
    internal const int Dat80084896Address = unchecked((int)0x80084896);

    // ==============================================================================================
    // C# BRIDGES. Three, all mechanical, none carrying business logic.
    // ==============================================================================================

    // JUSTIFICATION: C# language bridge only
    // RELATION: C gives DAT_800990d8 a fixed extent in .bss with every CdlLOC already existing; C#
    // reference types need each element instantiated before CdGetToc2 can write through it.
    private static LibCd.CdlLOC[] CreateTocArray()
    {
        LibCd.CdlLOC[] toc = new LibCd.CdlLOC[100];
        for (int i = 0; i < toc.Length; i++)
        {
            toc[i] = new LibCd.CdlLOC();
        }

        return toc;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: `CdMix((CdlATV *)&DAT_8008d2bc)`, the three sites in FUN_80060364, FUN_8006071c and
    // FUN_80060c58. On the console the four bytes ARE the structure and the cast is free; LibCd.CdMix
    // takes the object, so the four bytes are copied into one on the way through. No value is
    // changed and nothing is remembered -- the instance is rebuilt per call, exactly as the console
    // re-reads the four bytes per call.
    private static void MixCdlATV()
    {
        LibCd.CdMix(new LibCd.CdlATV
        {
            val0 = DAT_8008d2bc,
            val1 = DAT_8008d2bd,
            val2 = DAT_8008d2be,
            val3 = DAT_8008d2bf,
        });
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: `CdControlB(com, &DAT_800990d8 + n * 4, result)`, the two sites in FUN_800605d8.
    // CdControlB's parameter is u_char* and LibCd offers no CdlLOC overload of it (CdControl does,
    // and the two sites that use CdControl take it directly), so the four bytes of the TOC entry
    // are laid out here in the order the console has them: minute, second, sector, track.
    private static byte[] TocEntryAsParam(int index)
    {
        LibCd.CdlLOC entry = DAT_800990d8[index];
        return new[] { entry.minute, entry.second, entry.sector, entry.track };
    }

    // ==============================================================================================
    // THE TRANSLITERATIONS, in address order.
    // ==============================================================================================

    // GHIDRA: FUN_8005ef20 @ 0x8005EF20 (VS.EXE)
    // 328 bytes. THE COMBAT SOUND-EFFECT CUE, and the busiest entry point in this file: eleven call
    // sites, ten of them in FighterCombatArms.cs and one in BattleManager.cs:808. It plays one row
    // of the DAT_80084c10 table on the ATSE bank through libsnd's key-on, or -- when the cue id is
    // zero -- keys OFF four voices and plays nothing.
    //
    // THE SECOND PARAMETER. Ghidra's decompiled prototype is `FUN_8005ef20(short param_1)`, one
    // parameter, because the body never reads $a1. The call sites disagree and they are the better
    // evidence: every one of the eleven sets it, and it is always `fighter + 0x114` -- the same
    // fighter-relative pointer FighterCombatArms.cs passes to FUN_800437ec beside it. So the
    // parameter EXISTS in the prototype and is DEAD in the body. Both facts are reproduced: it is
    // declared, it is discarded, and the call sites do not have to change.
    //
    // THE CUE ID GOES INTO THE WORKSPACE AT +0x156 BEFORE ANYTHING ELSE, including before the
    // zero test, so even the key-off path records which cue caused it. Nothing in this slice reads
    // +0x156 back; its reader is FUN_8005da78's per-frame sweep, already ported in SoundDriver.cs.
    //
    // THE VOICE CURSOR IS DAT_8008d210, NOT DAT_8008d214 -- a different cell from the one
    // FUN_8005fb9c and FUN_8005fd9c walk, with a different range (0x12..0x14, wrapping at 0x14) and
    // a different initial value (0x11, seeded from the image). SoundDriver.cs owns it and it is
    // reached qualified. Two round-robin cursors over overlapping voice numbers is not a mistake
    // being ported; it is what the original has.
    internal static int FUN_8005ef20(short param_1, int param_2)
    {
        int uVar1;
        int iVar2;
        int iVar3;

        // The second parameter is part of the prototype and is never read by the body. See above.
        _ = param_2;

        iVar3 = SoundState.DAT_8008d284;

        // The ATSE bank must be open. -1 is "no VAB", the value SoundDriver's init writes at +0x154.
        if ((short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.AbtlVabHandle) < 0)
        {
            uVar1 = -1;
        }
        else
        {
            PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x156, (ushort)param_1);

            if (param_1 == 0)
            {
                // 0x8005EF6C..0x8005EF9C: FOUR voices, 0x11..0x14, keyed off. Not six -- the loop
                // bound is `iVar2 < 4`, unlike the six-slot loops in FUN_8005fb9c / FUN_8005fcec /
                // FUN_8005f530, which key off 0x11..0x16. The mismatch is the original's.
                iVar2 = 0;
                iVar3 = 0x110000;
                uVar1 = 0;
                do
                {
                    SoundDriver.FUN_8006b88c(iVar3 >> 0x10);
                    iVar3 = iVar3 + 0x10000;
                    iVar2 = iVar2 + 1;
                    uVar1 = 0;
                }
                while (iVar2 < 4);
            }
            else
            {
                // 0x8005EFC0: `sll v0,v0,0x10 / sra v0,v0,0xe` -- sign-extend from 16 bits then
                // multiply by 4, the four-byte row stride of DAT_80084c10.
                iVar2 = (int)((uint)(ushort)(param_1 - 1) << 0x10) >> 0xe;

                // The triple is COPIED INTO THE WORKSPACE at +0x144..+0x146 first and only then read
                // back out of it to build the call. That double handling is in the original and is
                // kept: the workspace copy is what FUN_8005da78's sweep re-keys from later.
                PsxRam.WriteU8(iVar3 + 0x144, PsxRam.ReadU8(Dat80084c10Address + iVar2));
                PsxRam.WriteU8(SoundState.DAT_8008d284 + 0x145, PsxRam.ReadU8(Dat80084c11Address + iVar2));
                PsxRam.WriteU8(SoundState.DAT_8008d284 + 0x146, PsxRam.ReadU8(Dat80084c12Address + iVar2));
                PsxRam.WriteU8(SoundState.DAT_8008d284 + 0x147, 0xff);

                // 0x8005F008: the cursor, 0x12..0x14. Post-increment, wrap when it exceeds 0x14.
                SoundDriver.DAT_8008d210 = (short)(SoundDriver.DAT_8008d210 + 1);
                if (0x14 < SoundDriver.DAT_8008d210)
                {
                    SoundDriver.DAT_8008d210 = 0x12;
                }

                SoundDriver.FUN_8006b4a0(
                    SoundDriver.DAT_8008d210,
                    (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.AbtlVabHandle),
                    PsxRam.ReadU8(SoundState.DAT_8008d284 + 0x144),
                    PsxRam.ReadU8(SoundState.DAT_8008d284 + 0x145),
                    PsxRam.ReadU8(SoundState.DAT_8008d284 + 0x146),
                    0,
                    PsxRam.ReadU8(SoundState.DAT_8008d284 + 0x147),
                    PsxRam.ReadU8(SoundState.DAT_8008d284 + 0x147));

                // +0x143 and +0x142, in THAT order -- left volume from the higher address. Neither
                // is written by this function; both are the per-frame volume the sweep maintains.
                SoundDriver.FUN_8006bdd8(
                    SoundDriver.DAT_8008d210,
                    PsxRam.ReadU8(SoundState.DAT_8008d284 + 0x143),
                    PsxRam.ReadU8(SoundState.DAT_8008d284 + 0x142));

                uVar1 = 0;
            }
        }

        return uVar1;
    }

    // GHIDRA: FUN_8005f530 @ 0x8005F530 (VS.EXE)
    // 148 bytes. THE CHSE BANK TEARDOWN: key off the six SE voices, close the VAB at +0x158, mark
    // the slot empty, and raise bit 2 of the scene's mode word. Its one caller is FUN_80036a64 at
    // 0x80036B20 and it tests the result against 0 -- which can never be true, because this function
    // returns a literal 1 on every path, including the path where the bank was already closed and
    // nothing at all happened. Rule 12: that is not corrected. The caller's `if (iVar5 == 0)` branch
    // is dead in the original and stays dead here.
    //
    // BattleScene.cs:2294 carries an EMPTY `private static int FUN_8005f530()` for this address and
    // calls it at line 1632. That stub must be deleted and the call site qualified, or this body
    // never runs.
    internal static int FUN_8005f530()
    {
        int iVar1;
        int iVar2;
        int iVar3;

        if (-1 < (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.PendingVabHandle))
        {
            // Six voices, 0x11..0x16. The loop counter is compared through a 16-bit round trip
            // (`iVar1 * 0x10000 >> 0x10 < 6`), which is the compiler keeping the counter short; the
            // values never reach 0x8000 so the round trip is the identity and is written plainly.
            iVar2 = 0x11;
            iVar3 = 0;
            do
            {
                SoundDriver.FUN_8006b88c((short)iVar2);
                iVar1 = iVar3 + 1;
                iVar2 = iVar3 + 0x12;
                iVar3 = iVar1;
            }
            while (iVar1 * 0x10000 >> 0x10 < 6);

            LibSnd.SsVabClose((short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.PendingVabHandle));
            PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.PendingVabHandle, 0xffff);
        }

        BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 | 4;
        return 1;
    }

    // GHIDRA: FUN_8005fb9c @ 0x8005FB9C (VS.EXE)
    // 336 bytes. chse_call's target -- AnimCmdSound.cs reaches it from the animation VM's opcode 45
    // and describes it from that side. It plays one SE out of the CHSE bank (+0x158), or keys off.
    //
    // THE THREE PATHS, in the order the body decides them:
    //   * bank not open (+0x158 < 0)          -> return -1, nothing else happens
    //   * param_1's low halfword is 0         -> KEY OFF. With param_2 == 0 all six voices
    //                                            0x11..0x16; otherwise the single voice param_2
    //                                            selects. Returns 0.
    //   * otherwise                           -> KEY ON one voice with prog 0, tone (param_1 - 1)
    //                                            and note (tone * 2 + 0x24), at volume param_3 on
    //                                            both channels.
    //
    // THE VOICE NUMBER comes from one of two places and this is where FUN_8005fb9c and FUN_8005fd9c
    // are identical: param_2 == 0 takes the round-robin cursor DAT_8008d214, param_2 != 0 takes
    // `param_2 & 7 | 0x10`, i.e. an explicit slot 0x10..0x17 chosen by the caller. Note that the
    // explicit form can produce 0x10 and 0x17, which are OUTSIDE the 0x11..0x16 the cursor and every
    // key-off loop use. That is the original's arithmetic; nothing here narrows it.
    //
    // THE NOTE FORMULA IS NOT TABLE-DRIVEN, unlike FUN_8005fd9c's. `(tone * 2 + 0x24) * 0x10000 >>
    // 0x10` is a chromatic ramp computed on the spot, sign-truncated to 16 bits. prog is a hard 0.
    // That is the whole difference in the key-on between the two functions, and it is why CHSE
    // sounds are a scale and ATSE sounds are a table.
    //
    // AnimCmdSound.cs:550 carries an EMPTY `private static int FUN_8005fb9c(uint, ushort, short)`
    // for this address and calls it at line 189. That stub must be deleted.
    internal static int FUN_8005fb9c(uint param_1, ushort param_2, short param_3)
    {
        int uVar1;
        int iVar2;
        ushort uVar3;
        int iVar4;
        int iVar5;

        if ((short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.PendingVabHandle) < 0)
        {
            uVar1 = -1;
        }
        else
        {
            if (param_2 == 0)
            {
                // The wrap test lets the cursor reach 0x17 for one comparison before snapping to
                // 0x11. It never gets USED at 0x17 because the snap happens before the read -- but
                // it is stored at 0x17 for the length of one instruction, and if it is reproduced
                // any other way the cell's observable value differs. Written exactly as found.
                DAT_8008d214 = (short)(DAT_8008d214 + 1);
                uVar3 = (ushort)DAT_8008d214;
                if (0x16 < DAT_8008d214)
                {
                    DAT_8008d214 = 0x11;
                    uVar3 = (ushort)DAT_8008d214;
                }
            }
            else
            {
                uVar3 = (ushort)((param_2 & 7) | 0x10);
            }

            if ((param_1 & 0xffff) == 0)
            {
                if (param_2 == 0)
                {
                    iVar4 = 0x11;
                    iVar5 = 0;
                    do
                    {
                        SoundDriver.FUN_8006b88c((short)iVar4);
                        iVar2 = iVar5 + 1;
                        iVar4 = iVar5 + 0x12;
                        iVar5 = iVar2;
                    }
                    while (iVar2 * 0x10000 >> 0x10 < 6);

                    uVar1 = 0;
                }
                else
                {
                    SoundDriver.FUN_8006b88c((short)uVar3);
                    uVar1 = 0;
                }
            }
            else
            {
                iVar5 = (int)((param_1 - 1) * 0x10000) >> 0x10;
                SoundDriver.FUN_8006b4a0(
                    (short)uVar3,
                    (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.PendingVabHandle),
                    0,
                    iVar5,
                    (iVar5 * 2 + 0x24) * 0x10000 >> 0x10,
                    0,
                    0xff,
                    0xff);
                SoundDriver.FUN_8006bdd8((short)uVar3, param_3, param_3);
                uVar1 = 0;
            }
        }

        return uVar1;
    }

    // GHIDRA: FUN_8005fcec @ 0x8005FCEC (VS.EXE)
    // 176 bytes. chse_vol's target. Sets the volume of one CHSE voice, or of all six. Four call
    // sites: FUN_80035030 @ 0x80035120 and ExecuteAnimStreamBatch @ 0x800369F8 both call it as
    // (0, 0) -- silence everything -- and AnimCmdSound's StepVolumeRamp @ 0x8003ED6C and the
    // chse_vol handler @ 0x8003ECDC drive it per frame from the ramp quad.
    //
    // THE VOICE SELECTION IS NOT THE SAME AS FUN_8005fb9c'S, and the difference is easy to miss.
    // Here it is `(param_1 + 0x10) * 0x10000 >> 0x10` -- an ADDITION with no mask -- where
    // FUN_8005fb9c uses `param_2 & 7 | 0x10`. So chse_vol's argument is an INDEX from 1 and can
    // address any voice at all, while chse_call's is masked into 0x10..0x17. Two functions in the
    // same family disagreeing about how to name a voice is the original's, not a porting slip.
    //
    // AnimCmdSound.cs:563 carries an EMPTY `internal static void FUN_8005fcec(uint, short)` for
    // this address and calls it at line 245. Note the RETURN TYPE differs too: the original returns
    // 0 or -1 and the stub returns void. No caller reads it, but the stub must still go.
    internal static int FUN_8005fcec(uint param_1, short param_2)
    {
        int uVar1;
        int iVar2;
        int iVar3;
        int iVar4;

        if ((short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.PendingVabHandle) < 0)
        {
            uVar1 = -1;
        }
        else if ((param_1 & 0xffff) == 0)
        {
            iVar3 = 0x11;
            iVar4 = 0;
            do
            {
                SoundDriver.FUN_8006bdd8((short)iVar3, param_2, param_2);
                iVar2 = iVar4 + 1;
                iVar3 = iVar4 + 0x12;
                iVar4 = iVar2;
            }
            while (iVar2 * 0x10000 >> 0x10 < 6);

            uVar1 = 0;
        }
        else
        {
            SoundDriver.FUN_8006bdd8((int)((param_1 + 0x10) * 0x10000) >> 0x10, param_2, param_2);
            uVar1 = 0;
        }

        return uVar1;
    }

    // GHIDRA: FUN_8005fd9c @ 0x8005FD9C (VS.EXE)
    // 448 bytes. atse_call's target, and the ATSE twin of FUN_8005fb9c: same three paths, same
    // cursor, same masks, same key-off loops. THREE things differ and all three matter.
    //
    //   1. THE BANK. +0x154 (ATSE) instead of +0x158 (CHSE), in both the guard and the key-on.
    //   2. THE KEY-ON PARAMETERS COME FROM DAT_80084c10, not from arithmetic. The row is
    //      `(int)((param_1 - 1) * 0x10000) >> 0xe` -- sign-extend from 16 bits, times four -- and
    //      the three bytes at +0, +1, +2 of that row become prog, tone and note. This is the same
    //      table FUN_8005ef20 reads, addressed the same way.
    //   3. THE KEY-ON IS RETRIED ONCE. `if (sVar4 == -1) FUN_8006b4a0(<the identical eight
    //      arguments>);` -- when the first key-on reports -1 (no voice), it is issued again with
    //      nothing changed and the second result is DISCARDED. Retrying an identical call after a
    //      failure with no state change between them cannot succeed unless the SDK has a side
    //      effect on failure. It is in the image and it is reproduced; the report lists it as one of
    //      the things this slice could not close.
    //
    // FUN_8006b4a0's declared return in SoundDriver.cs is `int`; the original narrows it to a short
    // for the -1 test (`sVar4`), and that narrowing is kept.
    //
    // SIX CALL SITES, one of them AnimCmdSound's atse_call handler at 0x8003F108 and five of them in
    // the 0x8002xxxx menu/UI family. AnimCmdSound.cs:574 carries an EMPTY
    // `internal static int FUN_8005fd9c(uint, ushort, short)` for this address, called at line 389.
    internal static int FUN_8005fd9c(uint param_1, ushort param_2, short param_3)
    {
        int uVar1;
        int uVar2;
        int uVar3;
        short sVar4;
        int uVar5;
        int iVar6;
        int iVar7;
        ushort uVar8;
        int iVar9;

        if ((short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.AbtlVabHandle) < 0)
        {
            uVar5 = -1;
        }
        else
        {
            if (param_2 == 0)
            {
                DAT_8008d214 = (short)(DAT_8008d214 + 1);
                uVar8 = (ushort)DAT_8008d214;
                if (0x16 < DAT_8008d214)
                {
                    DAT_8008d214 = 0x11;
                    uVar8 = (ushort)DAT_8008d214;
                }
            }
            else
            {
                uVar8 = (ushort)((param_2 & 7) | 0x10);
            }

            if ((param_1 & 0xffff) == 0)
            {
                if (param_2 == 0)
                {
                    iVar9 = 0x11;
                    iVar6 = 0;
                    do
                    {
                        SoundDriver.FUN_8006b88c((short)iVar9);
                        iVar7 = iVar6 + 1;
                        iVar9 = iVar6 + 0x12;
                        iVar6 = iVar7;
                    }
                    while (iVar7 * 0x10000 >> 0x10 < 6);

                    uVar5 = 0;
                }
                else
                {
                    SoundDriver.FUN_8006b88c((short)uVar8);
                    uVar5 = 0;
                }
            }
            else
            {
                iVar6 = (int)((param_1 - 1) * 0x10000) >> 0xe;
                uVar1 = PsxRam.ReadU8(Dat80084c10Address + iVar6);
                iVar9 = (short)uVar8;
                uVar2 = PsxRam.ReadU8(Dat80084c11Address + iVar6);
                uVar3 = PsxRam.ReadU8(Dat80084c12Address + iVar6);
                sVar4 = (short)SoundDriver.FUN_8006b4a0(
                    iVar9,
                    (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.AbtlVabHandle),
                    uVar1, uVar2, uVar3, 0, 0xff, 0xff);

                if (sVar4 == -1)
                {
                    SoundDriver.FUN_8006b4a0(
                        iVar9,
                        (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.AbtlVabHandle),
                        uVar1, uVar2, uVar3, 0, 0xff, 0xff);
                }

                SoundDriver.FUN_8006bdd8(iVar9, param_3, param_3);
                uVar5 = 0;
            }
        }

        return uVar5;
    }

    // GHIDRA: FUN_8005ff5c @ 0x8005FF5C (VS.EXE)
    // 340 bytes. THE STREAMED VOICE STARTER -- voice_call's target, and the one function in this
    // family that touches the SPU directly rather than through libsnd. AnimCmdSound.cs named it
    // "the one that actually starts an ADPCM voice clip" from its call sites, and the body agrees.
    //
    // WHAT IT DOES, in the original's order:
    //   1. GATE. `(DAT_8008d340 & 0x3a) == 0` -- bits 1, 3, 4 and 5 of the scene mode word all
    //      clear. Bit 5 is the one this same function SETS on its way out, so it is self-excluding:
    //      a stream already running blocks a second start.
    //   2. PICK THE CLIP. With param_1 == 0 the clip index is param_3, used raw. Otherwise
    //      DAT_80084868[param_2] gives a row, the DAT_80084894/96 pair gives that row's half-open
    //      range, and the clip is `start + (param_3 % (end - start))`. Either way it lands in the
    //      workspace at +0x138.
    //   3. ARM THE VOICE. Overwrite three fields of the SpuVoiceAttr at 0x800B0DDC -- voice
    //      0x800000 (bit 23, voice 0x17), mask 0x80, addr = the SPU buffer handle DAT_8008d280 --
    //      and call SpuSetVoiceAttr. NOTE THE MASK: the init leaves 0xFF93 there and this
    //      overwrites it with 0x80, so only ONE attribute field is actually applied per call.
    //   4. ARM THE STREAM. Through the SpuStEnv pointer at DAT_8008d338: +0x180 = &DAT_801C1000
    //      (the ADPCM buffer), +0x174 = 6, +0x178 = 0.
    //   5. PUBLISH. DAT_8008d384 = 1 and bit 5 of DAT_8008d340 set. AnimCmdSound.cs's own note said
    //      DAT_8008d384 "never takes the value 1 in this port" because this function was blocked;
    //      that is no longer true once its stub is deleted.
    //
    // THE TWO `trap()` CALLS IN THE DECOMPILATION ARE NOT INSTRUCTIONS. Ghidra emits trap(0x1c00)
    // and trap(0x1800) as its model of what MIPS `div` does on a zero divisor and on the
    // INT_MIN / -1 overflow. Neither is in the image -- the image has a bare `div` -- so neither is
    // transliterated. The overflow one is unreachable anyway: it guards `param_3 == 0x80000000` and
    // param_3 is a ushort.
    //
    // BLOCKED: what happens when end == start. The `end != 0` test in front of the division does not
    // rule it out, and on the console a zero divisor leaves the `div` result undefined rather than
    // faulting. In C# the `%` throws. No image evidence closes what the original then does, so
    // nothing is invented: the expression is written plainly and this note is the record. Every row
    // read out of the image has a non-empty range, so the case is not reachable with the shipped
    // table.
    //
    // AnimCmdSound.cs:586 carries an EMPTY `private static int FUN_8005ff5c(short, uint, ushort)`
    // for this address and calls it at line 305; FUN_80060144 below is its other caller.
    internal static int FUN_8005ff5c(short param_1, uint param_2, ushort param_3)
    {
        int iVar1;
        int iVar2;

        if ((BattleScene.DAT_8008d340 & 0x3a) == 0)
        {
            if (param_1 == 0)
            {
                PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x138, param_3);
                goto LAB_8006003c;
            }

            if (PsxRam.ReadU8(Dat80084868Address + (int)(param_2 & 0xffff)) != 0)
            {
                iVar1 = ((PsxRam.ReadU8(Dat80084868Address + (int)(param_2 & 0xffff)) - 1) * 0x10 +
                         (ushort)(param_1 - 1) * 2) * 2;

                if ((short)PsxRam.ReadU16(Dat80084896Address + iVar1) != 0)
                {
                    iVar2 = (int)((uint)((short)PsxRam.ReadU16(Dat80084896Address + iVar1) -
                                         (uint)PsxRam.ReadU16(Dat80084894Address + iVar1)) * 0x10000) >> 0x10;

                    // Ghidra's trap(0x1c00) / trap(0x1800) stood here. See the note above: they are
                    // the decompiler's model of MIPS `div`, not instructions, and are not ported.
                    PsxRam.WriteU16(
                        SoundState.DAT_8008d284 + 0x138,
                        (ushort)(PsxRam.ReadU16(Dat80084894Address + iVar1) +
                                 (short)((int)(uint)param_3 % iVar2)));
                    goto LAB_8006003c;
                }
            }
        }

        return -1;

    LAB_8006003c:

        // The three SpuVoiceAttr fields, in the original's own order: mask before voice.
        // SoundDriver.cs owns the structure at 0x800B0DDC; it is reached qualified, never rebuilt.
        SoundDriver.DAT_800b0ddc.mask = 0x80;
        SoundDriver.DAT_800b0ddc.voice = 0x800000;
        SoundDriver.DAT_800b0ddc.addr = (uint)SoundState.DAT_8008d280;
        LibSpu.SpuSetVoiceAttr(SoundDriver.DAT_800b0ddc);

        iVar1 = SoundState.DAT_8008d338;
        PsxRam.WriteI32(SoundState.DAT_8008d338 + 0x180, SoundState.Dat801c1000Address);
        PsxRam.WriteU8(iVar1 + 0x174, 6);
        AnimCmdSound.DAT_8008d384 = 1;
        PsxRam.WriteI32(SoundState.DAT_8008d338 + 0x178, 0);
        BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 | 0x20;
        return 0;
    }

    // GHIDRA: FUN_80060144 @ 0x80060144 (VS.EXE)
    // 408 bytes. THE VOICE-LINE STATE MACHINE, three states over the workspace pair at +0x15C
    // (state) and +0x15E (pitch/pending). AnimCmdSound.cs's FUN_8003ef04 drives it from three call
    // sites and branches on its result; SoundState already names both fields.
    //
    // THE THREE STATES:
    //   0  IDLE -- pick a line and start it. Advances to 1 and returns 0.
    //   1  WAITING FOR THE STREAM TO REPORT READY. Returns 0 unless DAT_8008d384 is exactly 9, in
    //      which case it writes 0x49 there and advances to 2. 9 is what the SPU stream callback
    //      leaves behind; 0x49 is 9 | 0x40, the "acknowledged" bit AnimCmdSound's FUN_8006012c also
    //      sets. This is the writer AnimCmdSound.cs's note said was blocked.
    //   2  WAITING FOR THE STREAM TO FINISH. Returns 0 unless DAT_8008d384 is back to 0. Then: if
    //      bit 14 of the pitch cell is clear the line is over -- state back to 0, RETURN 1, the only
    //      non-zero return in the whole function. If bit 14 is set there is a SECOND HALF: the clip
    //      index is (pitch & 0x3fff) + 1, bit 14 is cleared, and the machine falls through to start
    //      it and go back to state 1.
    //
    // HOW STATE 0 PICKS A LINE, which is where bit 15 of the argument goes -- AnimCmdSound.cs
    // recorded this as "only half legible" and it is now closed:
    //   * `(param_1 >> 0xe & 2) == 0` tests BIT 15 of param_1. Set means "take the THIRD halfword
    //     of the row, DAT_80084ad8, unconditionally". That is the fixed alternative.
    //   * Clear means CYCLE THE FIRST TWO through their own high bits, and the table is the storage:
    //       - first halfword's bit 15 clear -> SET it in the table, use the first halfword;
    //       - else second halfword's bit 15 clear -> SET it, use the second;
    //       - else CLEAR the second's bit 15 in the table, and use the first halfword masked to
    //         0x7fff -- WITHOUT clearing the first's bit in the table.
    //     So the pair cycles first, second, first, second, ... and after the first full round the
    //     first entry's latch bit stays set forever. Whether that asymmetry is intended is not
    //     knowable from the image; it is reproduced exactly.
    //   * Either way the chosen halfword goes into +0x15E WHOLE (latch bits and all) and only its
    //     low 14 bits are passed to FUN_8005ff5c.
    //
    // AnimCmdSound.cs:600 carries an EMPTY `private static int FUN_80060144(uint)` for this address
    // and calls it at lines 443, 455 and 466 -- all three inside FUN_8003ef04.
    internal static int FUN_80060144(uint param_1)
    {
        ushort uVar1;
        int iVar2;
        int puVar3;
        int puVar4;
        ushort uVar5;

        uVar1 = PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.VoiceLineState);

        if (uVar1 == 1)
        {
            if (AnimCmdSound.DAT_8008d384 != 9)
            {
                return 0;
            }

            AnimCmdSound.DAT_8008d384 = 0x49;
            PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.VoiceLineState, 2);
            return 0;
        }

        if (uVar1 < 2)
        {
            if (uVar1 != 0)
            {
                return 0;
            }

            if ((param_1 >> 0xe & 2) == 0)
            {
                iVar2 = (int)(param_1 & 0x7f) * 6;
                puVar4 = Dat80084ad4Address + iVar2;
                uVar1 = PsxRam.ReadU16(puVar4);
                if ((uVar1 & 0x8000) == 0)
                {
                    PsxRam.WriteU16(puVar4, (ushort)(uVar1 | 0x8000));
                }
                else
                {
                    puVar3 = Dat80084ad6Address + iVar2;
                    uVar1 = PsxRam.ReadU16(puVar3);
                    if ((uVar1 & 0x8000) == 0)
                    {
                        PsxRam.WriteU16(puVar3, (ushort)(uVar1 | 0x8000));
                    }
                    else
                    {
                        PsxRam.WriteU16(puVar3, (ushort)(uVar1 & 0x7fff));
                        uVar1 = (ushort)(PsxRam.ReadU16(puVar4) & 0x7fff);
                    }
                }
            }
            else
            {
                uVar1 = PsxRam.ReadU16(Dat80084ad8Address + (int)(param_1 & 0x7f) * 6);
            }

            uVar5 = (ushort)(uVar1 & 0x3fff);
            PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.VoiceLinePitch, uVar1);
        }
        else
        {
            if (uVar1 != 2)
            {
                return 0;
            }

            if (AnimCmdSound.DAT_8008d384 != 0)
            {
                return 0;
            }

            uVar1 = PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.VoiceLinePitch);
            if ((uVar1 & 0x4000) == 0)
            {
                PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.VoiceLineState, 0);
                return 1;
            }

            uVar5 = (ushort)((uVar1 & 0x3fff) + 1);
            PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.VoiceLinePitch, (ushort)(uVar1 & 0xbfff));
        }

        FUN_8005ff5c(0, 0, uVar5);
        PsxRam.WriteU16(SoundState.DAT_8008d284 + SoundState.VoiceLineState, 1);
        return 0;
    }

    // GHIDRA: FUN_80060364 @ 0x80060364 (VS.EXE)
    // 276 bytes. THE CD-AUDIO INITIALISER. Six call sites: the sound task's own init
    // (FUN_8005d25c @ 0x8005DA2C, which SoundDriver.cs already ports and which calls the stub) plus
    // five in the 0x80029xxx family.
    //
    // WHAT IT DOES, and every step is closed:
    //   1. SsSetSerialAttr(0, 0, 1) and SsSetSerialVol(0, 0x32, 0x32) -- open serial input 0 and set
    //      both its volumes to 50. Serial input 0 is the CD's digital feed into the SPU.
    //   2. DAT_8008d2b0 = 5 -- the CdlSetmode byte FUN_800605d8 will later send. 5 is
    //      CdlModeDA | CdlModeRT in the libcd headers, i.e. "CD-DA playback, report position".
    //   3. CdGetToc into DAT_800990d8 and store the track count MINUS ONE.
    //   4. WALK THE WHOLE TOC THROUGH CdPosToInt AND STRAIGHT BACK THROUGH CdIntToPos, in place,
    //      storing the result over the entry it came from. This is a NORMALISATION PASS, not a
    //      computation: CdGetToc leaves the BCD fields as the drive reported them (and, in this
    //      port's CdGetToc2, leaves `sector` zeroed), and the round trip re-derives them through
    //      the 150-sector lead-in arithmetic both routines share. It also silently writes `sector`
    //      for every entry. LibCd's CdIntToPos never touches `track`, so track survives.
    //   5. Three constants: DAT_8008d294 = DAT_8008d298 = DAT_8008d29c = 3. Track 3 is where the
    //      music starts on this disc.
    //   6. The stereo/mono fork on DAT_801ff01e, setting the four CdlATV bytes and calling one of
    //      two libsnd reverb setters.
    //   7. CdMix, mode word to 1 (service disabled until FUN_800605d8 enables it), sync status 0.
    //
    // SoundDriver.cs:2474 carries an EMPTY `private static void FUN_80060364()` for this address and
    // calls it at line 1096. That stub must be deleted and the call site qualified.
    internal static void FUN_80060364()
    {
        int iVar1;
        int i;
        int p;

        LibSnd.SsSetSerialAttr(0, 0, 1);
        LibSnd.SsSetSerialVol(0, 0x32, 0x32);
        p = 0;
        DAT_8008d2b0[0] = 5;
        iVar1 = LibCd.CdGetToc(DAT_800990d8);
        DAT_8008d290 = iVar1 + -1;
        iVar1 = 0;
        if (0 < DAT_8008d290)
        {
            do
            {
                i = LibCd.CdPosToInt(DAT_800990d8[p]);
                LibCd.CdIntToPos(i, DAT_800990d8[p]);
                iVar1 = iVar1 + 1;
                p = p + 1;
            }
            while (iVar1 < DAT_8008d290);
        }

        DAT_8008d294 = 3;
        DAT_8008d298 = 3;
        DAT_8008d29c = 3;

        if (PsxRam.ReadU16(Dat801ff01eAddress) == 0)
        {
            DAT_8008d2bc = 0x7f;
            DAT_8008d2bd = 8;
            DAT_8008d2be = 0x7f;
            DAT_8008d2bf = 8;
            FUN_8006de1c();
        }
        else
        {
            DAT_8008d2bc = 0x3f;
            DAT_8008d2bd = 0x3f;
            DAT_8008d2be = 0x3f;
            DAT_8008d2bf = 0x3f;
            FUN_8006de08();
        }

        MixCdlATV();
        DAT_8008d2a0 = 1;
        DAT_8008d288 = 0;
    }

    // GHIDRA: FUN_80060478 @ 0x80060478 (VS.EXE)
    // 260 bytes. THE PER-FRAME CD-AUDIO SERVICE, called unconditionally once per frame by
    // FUN_8005da78 @ 0x8005E1A0. It is the loop keeper: it asks the drive where it is and re-seeks
    // when the disc has run past the end of the requested track.
    //
    // THE WHOLE FUNCTION IS ONE `if ((DAT_8008d2a0 & 1) == 0)`. Bit 0 set means "not playing", and
    // FUN_80060364 sets it, so nothing happens until FUN_800605d8's CdlPlay path clears it.
    //
    // THE POSITION COMES OUT OF CdReady'S RESULT BLOCK, byte 1, IN BCD: `(b >> 4) * 10 + (b & 0xf)`.
    // That is the track number the drive is currently reading. `DAT_8008d29c < DAT_8008d298` --
    // current track has passed the requested one -- is the loop condition, and it fires exactly when
    // the drive falls off the end of the track into the next one.
    //
    // THE COMMA IN THE DECOMPILATION IS LOAD-BEARING: `(DAT_8008d28c == 1) && (DAT_8008d298 = ...,
    // DAT_8008d29c < DAT_8008d298)`. The store to DAT_8008d298 happens ONLY when CdReady returned
    // CdlDataReady. When it did not, the position cell keeps its previous value. Written below as
    // nested ifs so that ordering survives.
    //
    // THE RETRY LOOP `while (iVar1 == 0)` around CdlPlay has no bound. On the console it spins until
    // libcd accepts the command. LibCd.CdControl returns 1 on the desktop path, so it runs once.
    //
    // SoundDriver.cs:2482 carries an EMPTY `private static void FUN_80060478()` for this address and
    // calls it at line 1549.
    internal static void FUN_80060478()
    {
        int iVar1;

        // JUSTIFICATION: C# language bridge only
        // RELATION: "u_char auStack_18[8]" -- the stack response block of the CdlPlay retry. It is a
        // SECOND result block, separate from DAT_8008d2b4 which the CdReady above uses, and the
        // originals are separate storage, so they stay separate here.
        byte[] auStack_18 = new byte[8];

        if ((DAT_8008d2a0 & 1) == 0)
        {
            DAT_8008d28c = LibCd.CdReady(1, DAT_8008d2b4);
            if (DAT_8008d28c == 1)
            {
                DAT_8008d298 = (DAT_8008d2b4[1] >> 4) * 10 + (DAT_8008d2b4[1] & 0xf);
                if (DAT_8008d29c < DAT_8008d298)
                {
                    if ((DAT_8008d2a0 & 4) != 0)
                    {
                        DAT_8008d2c0 = 8;
                        LibCd.CdControl(8, (byte[])null, null);
                    }

                    if ((DAT_8008d2a0 & 2) != 0)
                    {
                        do
                        {
                            DAT_8008d2c0 = 3;
                            iVar1 = LibCd.CdControl(3, DAT_800990d8[DAT_8008d29c], auStack_18);
                        }
                        while (iVar1 == 0);
                    }
                }
            }

            DAT_8008d288 = LibCd.CdSync(1, null);
        }
    }

    // GHIDRA: FUN_800605d8 @ 0x800605D8 (VS.EXE)
    // 324 bytes. THE CD-AUDIO TRACK STARTER. One caller, LAB_800296E0 in the 0x80029xxx family.
    // param_1 is a flag word ORed into DAT_8008d2a0; param_2 is the track.
    //
    // BIT 3 OF param_1 SELECTS THE MODE, and the two arms are not symmetrical:
    //   CLEAR -- SEEK ONLY. CdlSetmode (0x0E) until accepted, then CdlSetloc (0x02) to the track's
    //            position, then CdSync(0). Sets bit 1 of the scene mode word, and ORs param_1 into
    //            DAT_8008d2a0 WITHOUT clearing bit 0 -- so FUN_80060478's per-frame service stays
    //            disabled. The disc is positioned and not playing.
    //   SET   -- PLAY. CdlPause (0x0A) until accepted, then CdlSetmode until accepted, then CdlPlay
    //            (0x03) to the track's position. ORs param_1 in and CLEARS bit 0, enabling the
    //            per-frame service. Does NOT touch the scene mode word and does NOT CdSync.
    //
    // Both arms then set DAT_8008d298 and DAT_8008d29c to the requested track, which is what arms
    // FUN_80060478's loop test.
    //
    // THE TWO CdControlB RETRY LOOPS HAVE NO BOUND. See FUN_80060478's note: on the desktop path
    // LibCd.CdControlB answers on the first try, so each runs once.
    //
    // THE INDEX SCALING `((uint)param_2 << 0x10) >> 0xe` is "sign-extend from 16 bits, times four",
    // the same idiom as DAT_80084c10's row stride and here the four-byte CdlLOC stride. Entry
    // (short)param_2.
    internal static int FUN_800605d8(ushort param_1, ushort param_2)
    {
        int iVar1;

        if ((param_1 & 8) == 0)
        {
            do
            {
                iVar1 = LibCd.CdControlB(0x0e, DAT_8008d2b0, DAT_8008d2b4);
            }
            while (iVar1 == 0);

            DAT_8008d2c0 = 2;
            LibCd.CdControlB(0x02, TocEntryAsParam((int)((uint)param_2 << 0x10) >> 0x10), DAT_8008d2b4);
            LibCd.CdSync(0, DAT_8008d2b4);
            BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 | 2;
            DAT_8008d2a0 = (short)param_1 | DAT_8008d2a0;
        }
        else
        {
            do
            {
                iVar1 = LibCd.CdControlB(0x0a, null, DAT_8008d2b4);
            }
            while (iVar1 == 0);

            do
            {
                iVar1 = LibCd.CdControlB(0x0e, DAT_8008d2b0, DAT_8008d2b4);
            }
            while (iVar1 == 0);

            DAT_8008d2c0 = 3;
            LibCd.CdControlB(0x03, TocEntryAsParam((int)((uint)param_2 << 0x10) >> 0x10), DAT_8008d2b4);
            DAT_8008d2a0 = ((short)param_1 | DAT_8008d2a0) & unchecked((int)0xfffffffe);
        }

        DAT_8008d298 = (short)param_2;
        DAT_8008d29c = (short)param_2;
        return 0;
    }

    // GHIDRA: FUN_8006071c @ 0x8006071C (VS.EXE)
    // 720 bytes, the largest in this file. THE CD-AUDIO VOLUME MACHINE: five commands over the four
    // CdlATV bytes, applied through CdMix, with the result LATCHED IN THE WORKSPACE when it is a
    // ramp and CLEARED when it is not. Twelve call sites -- FUN_8005da78 @ 0x8005E1BC drives the
    // latched ramp once per frame, the other eleven are one-shots from the 0x8002xxxx family.
    //
    // THE DISPATCH. `sltiu v0,a0,5` at 0x80060750 then a five-entry jump table at 0x80020AA4. The
    // test is UNSIGNED on a sign-extended value, so a negative command falls to the default, and so
    // does command 0 (its table slot IS the default target). Ghidra prints the default arm as
    // `if (false) goto switchD_80060774_caseD_0;`, which is its way of drawing an edge it cannot
    // spell; the real guard is the range test and it is written as one below.
    //
    // THE FIVE COMMANDS:
    //   0 / out of range  do nothing new -- fall to the tail with the four bytes unchanged, which
    //                     still re-applies them through CdMix and still CLEARS the latch.
    //   1  SET ABSOLUTE   all four channels to param_2. In stereo the two cross channels are
    //                     divided by 16 first.
    //   2  SET PRESET     the same two presets FUN_80060364 uses: (8, 8, 0x7f, 0x7f) in stereo,
    //                     0x3f across the board in mono. NOTE THE ORDER IS NOT THE INIT'S: here the
    //                     8s go to uVar3/uVar4 and the 0x7fs to uVar5/uVar6, which the tail then
    //                     writes as (0x7f, 8, 0x7f, 8) -- the same four bytes the init writes, by a
    //                     different route.
    //   3  FADE UP        add param_2, clamp at the ceiling by falling into command 2's preset.
    //   4  FADE DOWN      subtract param_2, and if the result is <= 0 zero all four and stop.
    //
    // THE LATCH IS THE RETURN VALUE. iVar7 starts at 1 and is set to 0 only by LAB_800608E0, which
    // is reached from the two RAMP commands (3 and 4) when they did NOT hit a limit. So:
    //   returns 0 -> ramp still in progress -> the command and its step are STORED at +0x190/+0x192
    //                and FUN_8005da78 will call this function again next frame with them;
    //   returns 1 -> finished or one-shot     -> +0x190 and +0x192 are ZEROED and FUN_8005da78's
    //                `if (*(short *)(ws + 0x190) != 0)` stops calling.
    // That is the whole mechanism by which a fade runs to completion across frames.
    //
    // TWO SHIFTS THAT DISAGREE WITH EACH OTHER, and this is the defect-class-4 trap in this
    // function. Command 1 does `bgez` / `addiu v0,v0,0xf` / `srl a2,v0,4` at 0x8006079C..0x800607A8
    // -- the +15 correction of a signed divide followed by a LOGICAL shift, which gives a huge
    // positive number for any negative input instead of the intended -1. Command 3 four instructions
    // later does the same correction with an ARITHMETIC shift. Ghidra prints the first on a uint and
    // the second on an int, and both printouts are literally what the bytes say. Rule 12: the
    // inconsistency is the original's and both are reproduced exactly as printed.
    //
    // THE FOUR CLAMPS IN THE TAIL are also asymmetrical in the printout -- the first computes
    // `iVar1 = (int)(uVar6 << 0x10) >> 0x10` up front while the other three keep `iVar1` unshifted
    // and shift it inside the comparison. Same arithmetic, different spelling, and rule 7 says do
    // not normalise it, so it is not normalised.
    //
    // SoundDriver.cs:2490 carries an EMPTY `private static void FUN_8006071c(int, int)` for this
    // address and calls it at line 1552. Note the SIGNATURE and RETURN differ: the original takes
    // (undefined2, uint) and returns the latch. The call site needs `(uint)` on the second argument.
    internal static int FUN_8006071c(int param_1, uint param_2)
    {
        int iVar1;
        int iVar2;
        uint uVar3;
        uint uVar4;
        uint uVar5;
        uint uVar6;
        int iVar7;

        iVar7 = 1;
        uVar5 = DAT_8008d2be;
        uVar3 = DAT_8008d2bf;
        uVar4 = DAT_8008d2bd;
        uVar6 = DAT_8008d2bc;

        // 0x80060750: `sltiu v0,a0,5` -- UNSIGNED, so a negative command falls through here too.
        if ((uint)param_1 >= 5)
        {
            goto switchD_80060774_caseD_0;
        }

        switch (param_1)
        {
            case 0:
                goto switchD_80060774_caseD_0;

            case 1:
                uVar3 = param_2;
                uVar4 = param_2;
                uVar5 = param_2;
                uVar6 = param_2;
                if (PsxRam.ReadU16(Dat801ff01eAddress) == 0)
                {
                    uVar4 = (uint)(short)param_2;
                    if ((int)uVar4 < 0)
                    {
                        uVar4 = uVar4 + 0xf;
                    }

                    // 0x800607A8: `srl a2,v0,4`. LOGICAL. See the note above.
                    uVar3 = uVar4 >> 4;
                    uVar4 = uVar4 >> 4;
                }

                break;

            case 2:
                if (PsxRam.ReadU16(Dat801ff01eAddress) == 0)
                {
                    goto LAB_80060828;
                }

                goto LAB_80060860;

            case 3:
                if (PsxRam.ReadU16(Dat801ff01eAddress) == 0)
                {
                    iVar2 = (int)((uVar5 + param_2) * 0x10000) >> 0x10;
                    iVar1 = iVar2;
                    if (iVar2 < 0)
                    {
                        iVar1 = iVar2 + 0xf;
                    }

                    // ARITHMETIC here, unlike command 1. The original's own inconsistency.
                    uVar4 = uVar3 + (uint)(iVar1 >> 4);
                    uVar6 = uVar5 + param_2;
                    if (0x7e < iVar2)
                    {
                        goto LAB_80060828;
                    }
                }
                else
                {
                    uVar4 = uVar3 + param_2;
                    uVar6 = uVar4;
                    if (0x3e < (int)(uVar4 * 0x10000) >> 0x10)
                    {
                        goto LAB_80060860;
                    }
                }

                goto LAB_800608e0;

            case 4:
                uVar4 = uVar3 - param_2;
                uVar6 = uVar4;
                if (PsxRam.ReadU16(Dat801ff01eAddress) == 0)
                {
                    iVar1 = (int)((uVar5 - param_2) * 0x10000) >> 0x10;
                    if (iVar1 < 0)
                    {
                        iVar1 = iVar1 + 0xf;
                    }

                    uVar4 = uVar3 - (uint)(iVar1 >> 4);
                    uVar6 = uVar5 - param_2;
                }

                if ((int)(uVar6 << 0x10) < 1)
                {
                    uVar3 = 0;
                    uVar4 = 0;
                    uVar5 = 0;
                    uVar6 = 0;
                    break;
                }

                goto LAB_800608e0;
        }

        goto switchD_80060774_caseD_0;

    LAB_80060828:
        uVar3 = 8;
        uVar4 = 8;
        uVar5 = 0x7f;
        uVar6 = 0x7f;
        goto switchD_80060774_caseD_0;

    LAB_80060860:
        uVar3 = 0x3f;
        uVar4 = 0x3f;
        uVar5 = 0x3f;
        uVar6 = 0x3f;
        goto switchD_80060774_caseD_0;

    LAB_800608e0:
        iVar7 = 0;
        uVar3 = uVar4;
        uVar5 = uVar6;

    switchD_80060774_caseD_0:

        // ---- The four clamps. Each: sign-extend from 16 bits, floor at 0, store the byte, then
        // overwrite with 0xff if the 16-bit value exceeded 0xff. The STORE HAPPENS BEFORE THE
        // CEILING TEST in all four, which means the byte is momentarily the truncated value; that
        // ordering is the original's and is kept.
        iVar1 = (int)(uVar6 << 0x10) >> 0x10;
        if ((int)(uVar6 << 0x10) < 0)
        {
            uVar6 = 0;
            iVar1 = 0;
        }

        DAT_8008d2bc = (byte)uVar6;
        if (0xff < iVar1)
        {
            DAT_8008d2bc = 0xff;
        }

        iVar1 = (int)(uVar4 << 0x10);
        if ((int)(uVar4 << 0x10) < 0)
        {
            uVar4 = 0;
            iVar1 = 0;
        }

        DAT_8008d2bd = (byte)uVar4;
        if (0xff < iVar1 >> 0x10)
        {
            DAT_8008d2bd = 0xff;
        }

        iVar1 = (int)(uVar5 << 0x10);
        if ((int)(uVar5 << 0x10) < 0)
        {
            uVar5 = 0;
            iVar1 = 0;
        }

        DAT_8008d2be = (byte)uVar5;
        if (0xff < iVar1 >> 0x10)
        {
            DAT_8008d2be = 0xff;
        }

        iVar1 = (int)(uVar3 << 0x10);
        if ((int)(uVar3 << 0x10) < 0)
        {
            uVar3 = 0;
            iVar1 = 0;
        }

        DAT_8008d2bf = (byte)uVar3;
        if (0xff < iVar1 >> 0x10)
        {
            DAT_8008d2bf = 0xff;
        }

        MixCdlATV();

        iVar1 = SoundState.DAT_8008d284;
        if (iVar7 == 0)
        {
            PsxRam.WriteU16(SoundState.DAT_8008d284 + 400, (ushort)param_1);
            PsxRam.WriteU16(iVar1 + 0x192, (ushort)(short)param_2);
        }
        else if (iVar7 == 1)
        {
            PsxRam.WriteU16(SoundState.DAT_8008d284 + 0x192, 0);
            PsxRam.WriteU16(iVar1 + 400, 0);
        }

        return iVar7;
    }

    // GHIDRA: FUN_800609ec @ 0x800609EC (VS.EXE)
    // 96 bytes. RESUME PLAYBACK OF THE CURRENT TRACK: CdlPlay to DAT_800990d8[DAT_8008d29c], no
    // result block, then enable the per-frame service (clear bit 0 of DAT_8008d2a0) and clear bit 1
    // of the scene mode word -- the bit FUN_800605d8's seek-only arm set. So this is the "the seek
    // is done, start the music" half of the pair. One caller, at 0x800296E8, immediately after the
    // FUN_800605d8 call at 0x800296E0.
    //
    // NO RETRY LOOP HERE, unlike every other CdlPlay in this file, and no result block either. The
    // command is issued once and its answer discarded.
    internal static void FUN_800609ec()
    {
        DAT_8008d2c0 = 3;
        LibCd.CdControl(3, DAT_800990d8[DAT_8008d29c], null);
        DAT_8008d2a0 = DAT_8008d2a0 & unchecked((int)0xfffffffe);
        BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 & 0xfffffffd;
    }

    // GHIDRA: FUN_80060a88 @ 0x80060A88 (VS.EXE)
    // 464 bytes. THE SOUND TEARDOWN, and the one function in this file that BLOCKS. One caller, at
    // 0x80029320. It silences everything, then WAITS for the streaming voice to finish, then closes
    // every VAB the module holds.
    //
    // THE ORDER, which is the whole content:
    //   1. 24 voices through FUN_8006bcd0 (voice, 0, 0) -- 0x18 of them, voices 0..0x17, the entire
    //      SPU. Not the six-voice SE bank: everything.
    //   2. 6 voices keyed off, 0x11..0x16.
    //   3. FUN_8006bf1c(0) and the four reverb setters to zero, then SsUtReverbOff.
    //   4. `*(DAT_8008d338 + 0x174) = 2` -- the SpuStEnv field FUN_8005ff5c sets to 6 when it starts
    //      a stream. 2 is presumably the idle state; that is not asserted, only noted.
    //   5. THE ACKNOWLEDGE. If DAT_8008d384 is non-zero, is not 0x0B in its low six bits, has bit 7
    //      clear, AND is exactly 9 or exactly 0x10 -- then clear it and clear bit 5 of the scene
    //      mode word. Note the redundancy: the first three tests are all implied by the fourth (9
    //      and 0x10 are both non-zero, neither has 0x0B in its low six bits, neither has bit 7).
    //      Three dead conditions in a row. Rule 12: reproduced as written.
    //   6. THE WAIT. `while ((DAT_8008d340 & 0x20) != 0) { VSync(0); FUN_8005da78(); }` -- spin on
    //      the scene mode word's bit 5, pumping the sound task by hand each frame. Bit 5 is the one
    //      FUN_8005ff5c sets when it starts a stream, and step 5 above is the only thing in this
    //      function that clears it. An UNBOUNDED wait driven by another function's state.
    //   7. SsSetNck from the workspace's +0x108 (masked to 7 bits) if it is non-negative, then
    //      SsVabClose on +0x10A (TWICE, nested, testing the same unchanged value both times -- so
    //      the same VAB id is closed twice), on +0x154, and on +0x12C.
    //
    // BLOCKED: THE WAIT LOOP CANNOT TERMINATE ON THIS PORT. Bit 5 of DAT_8008d340 is set by
    // FUN_8005ff5c above and cleared here only via DAT_8008d384 reaching 9 or 0x10. Nothing in the
    // port writes 9 or 0x10 into DAT_8008d384 -- that value comes from libspu's streaming callback,
    // which PsxSdkMonogame does not run, and AnimCmdSound.cs already records the same gap from the
    // other side ("the word never reaches 9"). So if a stream was started, this function spins
    // forever. The condition is FAITHFUL -- it is what the original tests -- and the missing piece
    // is the SPU stream callback, not this code. It is transliterated as found and reported.
    //
    // THE DOUBLE SsVabClose at step 7 is likewise reproduced. The inner test re-reads the same
    // halfword the outer one read, and nothing between them writes it, so both closes always fire.
    //
    // `*(short *)(DAT_8008d284 + 300)` is +0x12C, which SoundState names VoiceHandles -- slot 0 of
    // the six-slot voice-handle array, not a field of its own.
    internal static void FUN_80060a88()
    {
        int iVar1;
        int iVar2;

        iVar2 = 0;
        iVar1 = 0;
        do
        {
            SoundDriver.FUN_8006bcd0(iVar1 >> 0x10, 0, 0);
            iVar2 = iVar2 + 1;
            iVar1 = iVar2 * 0x10000;
        }
        while (iVar2 < 0x18);

        iVar1 = 0;
        iVar2 = 0x110000;
        do
        {
            SoundDriver.FUN_8006b88c(iVar2 >> 0x10);
            iVar2 = iVar2 + 0x10000;
            iVar1 = iVar1 + 1;
        }
        while (iVar1 < 6);

        FUN_8006bf1c(0);
        LibSnd.SsUtSetReverbDepth(0, 0);
        LibSnd.SsUtSetReverbFeedback(0);
        LibSnd.SsUtSetReverbDelay(0);
        LibSnd.SsUtReverbOff();
        PsxRam.WriteU8(SoundState.DAT_8008d338 + 0x174, 2);

        if (AnimCmdSound.DAT_8008d384 != 0 &&
            (AnimCmdSound.DAT_8008d384 & 0x3f) != 0xb &&
            (AnimCmdSound.DAT_8008d384 & 0x80) == 0 &&
            (AnimCmdSound.DAT_8008d384 == 9 || AnimCmdSound.DAT_8008d384 == 0x10))
        {
            AnimCmdSound.DAT_8008d384 = 0;
            BattleScene.DAT_8008d340 = BattleScene.DAT_8008d340 & 0xffffffdf;
        }

        while ((BattleScene.DAT_8008d340 & 0x20) != 0)
        {
            LibEtc.VSync(0);
            SoundDriver.FUN_8005da78();
        }

        LibEtc.VSync(0);

        if (-1 < (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + 0x108))
        {
            LibSnd.SsSetNck((short)(PsxRam.ReadU16(SoundState.DAT_8008d284 + 0x108) & 0x7f));
        }

        if (-1 < (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.BgmVabHandle))
        {
            LibSnd.SsVabClose((short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.BgmVabHandle));
            if (-1 < (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.BgmVabHandle))
            {
                LibSnd.SsVabClose((short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.BgmVabHandle));
            }
        }

        if (-1 < (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.AbtlVabHandle))
        {
            LibSnd.SsVabClose((short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.AbtlVabHandle));
        }

        if (-1 < (short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.VoiceHandles))
        {
            LibSnd.SsVabClose((short)PsxRam.ReadU16(SoundState.DAT_8008d284 + SoundState.VoiceHandles));
        }
    }

    // GHIDRA: FUN_80060c58 @ 0x80060C58 (VS.EXE)
    // 132 bytes. THE HARD SHUTDOWN, called from four arms of the switch at 0x80031BE8..0x80031C8C.
    // Twelve statements, no branches, no state of its own: mute libsnd, zero the CdlATV and apply
    // it, close the serial input, then SpuStQuit, SsEnd, SsQuit in that order.
    //
    // IT DOES NOT WAIT AND IT DOES NOT CLOSE ANY VAB, which is what separates it from FUN_80060a88
    // above. FUN_80060a88 is the orderly teardown; this is the one taken when the scene is being
    // torn down under it.
    internal static void FUN_80060c58()
    {
        FUN_8006bf1c(0);
        LibSnd.SsSetMVol(0, 0);
        DAT_8008d2bc = 0;
        DAT_8008d2bd = 0;
        DAT_8008d2be = 0;
        DAT_8008d2bf = 0;
        MixCdlATV();
        LibSnd.SsSetSerialAttr(0, 0, 0);
        LibSnd.SsSetSerialVol(0, 0, 0);
        LibSpu.SpuStQuit();
        LibSnd.SsEnd();
        LibSnd.SsQuit();
    }

    // GHIDRA: FUN_80062cb8 @ 0x80062CB8 (VS.EXE)
    // 244 bytes. _ExpAllocArea, the PsyQ heap's sbrk. Heap.cs already identifies it by that name
    // from the TITLE.EXE side (_ExpAllocArea @ 0x80058EC4) and gives the whole scheme: a four-byte
    // header per block holding `size | free-bit`, and 0xFFFFFFFE as the end-of-heap sentinel.
    //
    // WHAT IT DOES. Two entry conditions, then one common tail:
    //   param_1 == 0  -- "just check there is room". Fails if head has reached the limit.
    //   param_1 != 0  -- "make room for param_1 bytes". `uVar1` and `param_1` are SWAPPED so that
    //                    param_1 ends up the LARGER of (requested, DAT_8008d2e8) and uVar1 the
    //                    smaller; the remaining space is `((limit - head) >> 2) * 4 - 8`; and the
    //                    request fails only if BOTH the larger and the smaller exceed it. The
    //                    second test reassigns param_1 to the smaller as its side effect, so a
    //                    request that is too big but whose fallback fits is granted AT THE FALLBACK
    //                    SIZE. That is the original's own comma-expression bargain and it is kept.
    //   tail          -- write the 0xFFFFFFFE sentinel one aligned block ahead, write
    //                    `param_1 | 1` (the free bit) into the header at head - 4, and advance head
    //                    by (param_1 + 4) rounded down to a multiple of four.
    //
    // THE TWO `if (x < 0) x = x + 3` / `x + 7` CORRECTIONS are the signed-divide-by-4 idiom, and
    // both feed an arithmetic `>> 2` followed by `* 4`. They are the compiler's, not a rounding
    // policy, and they are reproduced rather than folded into a mask.
    //
    // DEVIATION, AND IT IS TOTAL: THIS FUNCTION IS UNREACHABLE ON THIS PORT. Its only caller is
    // malloc @ 0x80062F94, and Heap.FUN_80062f94 forwards to PsxSdkMonogame's PsxHeap instead of
    // transliterating malloc's body -- a decision Heap.cs argues at length and which this slice has
    // no mandate to revisit. So the three globals below are written by nothing but this function and
    // read by nothing at all. It is transliterated because it was asked for, because it is below the
    // 0x800632C4 line, and because a faithful body is worth more than a stub if malloc is ever
    // ported for real. It is NOT wired into anything, and Heap.cs is not edited.
    internal static int FUN_80062cb8(uint param_1)
    {
        uint uVar1;
        int iVar2;

        if (param_1 == 0)
        {
            param_1 = 0;
            if (DAT_8008d2f0 <= DAT_8008d2e0)
            {
                return -1;
            }
        }
        else
        {
            uVar1 = (uint)DAT_8008d2e8;
            if (param_1 <= (uint)DAT_8008d2e8)
            {
                uVar1 = param_1;
                param_1 = (uint)DAT_8008d2e8;
            }

            iVar2 = ((DAT_8008d2f0 - DAT_8008d2e0) >> 2) * 4 + -8;
            if (iVar2 < (int)param_1)
            {
                param_1 = uVar1;
                if (iVar2 < (int)uVar1)
                {
                    return -1;
                }
            }
        }

        uVar1 = param_1;
        if ((int)param_1 < 0)
        {
            uVar1 = param_1 + 3;
        }

        iVar2 = (int)param_1 + 4;
        PsxRam.WriteI32(((int)uVar1 >> 2) * 4 + DAT_8008d2e0, unchecked((int)0xfffffffe));
        PsxRam.WriteI32(DAT_8008d2e0 - 4, (int)(param_1 | 1));
        if (iVar2 < 0)
        {
            iVar2 = (int)param_1 + 7;
        }

        DAT_8008d2e0 = (iVar2 >> 2) * 4 + DAT_8008d2e0;
        return 0;
    }

    // ==============================================================================================
    // SDK ENTRIES THIS FILE HAS TO CALL AND CANNOT PORT. All three are at or above 0x800632C4, so
    // rule 13 forbids transliterating them, and PsxSdkMonogame exports nothing at those addresses
    // under any name -- so there is nothing to call either. They stand as BLOCKED stubs so that the
    // control flow around them is preserved and so that the gap is countable.
    //
    // The other four SDK entries this family reaches -- FUN_8006b4a0, FUN_8006b88c, FUN_8006bdd8 and
    // FUN_8006bcd0 -- already have exactly these stubs in SoundDriver.cs and are called there by
    // qualified name. They are NOT re-declared here. They are `private` in that file, which is the
    // one blocking edit this slice needs from the main session.
    // ==============================================================================================

    // GHIDRA: FUN_8006bf1c @ 0x8006BF1C (VS.EXE)
    // BLOCKED: SDK by address. Called as FUN_8006bf1c(0) from two places in this file --
    // FUN_80060a88's teardown, immediately before the four reverb setters, and FUN_80060c58's
    // shutdown, as the very first statement before SsSetMVol(0, 0). Both positions are "silence
    // everything before you take the driver down", which makes it a libsnd master control of some
    // kind. WHICH one is not closed and is not guessed: one argument, always zero, and no other
    // call site in the image narrows it.
    private static void FUN_8006bf1c(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8006de08 @ 0x8006DE08 (VS.EXE)
    // BLOCKED: SDK by address. Called with no arguments by FUN_80060364 on the MONO branch, paired
    // with the 0x3F-across-the-board CdlATV preset.
    private static void FUN_8006de08()
    {
    }

    // GHIDRA: FUN_8006de1c @ 0x8006DE1C (VS.EXE)
    // BLOCKED: SDK by address. The twin of the above, called on the STEREO branch, paired with the
    // 0x7F/0x08 hard-panned preset. Twenty bytes after FUN_8006de08 and reached from the opposite
    // arm of the same `if`, which is the shape of a mono/stereo pair of libsnd mode setters. That is
    // as far as the evidence goes; neither is named.
    private static void FUN_8006de1c()
    {
    }
}
