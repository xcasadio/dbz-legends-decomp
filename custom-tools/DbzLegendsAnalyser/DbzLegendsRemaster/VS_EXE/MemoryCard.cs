using PsxSdkMonogame;
using static PsxSdkMonogame.Kernel;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.VS_EXE;

// THE MEMORY CARD MODULE OF VS.EXE — 0x80021D44..0x8002388F.
//
// THE SLICE WAS COMMISSIONED UNDER THE WRONG NAME, on the reasonable guess that a cluster sitting
// immediately below FighterAi.cs in the image would be its relatives. It is not: every function in
// this range is a memory-card routine, and the proof is arithmetic rather than interpretation. The
// file was renamed from MemoryCard.cs to MemoryCard.cs once that was established.
//
//   SELECT.EXE (already ported, SELECT_EXE/MemoryCard.cs)      VS.EXE (this file)     delta
//     InitializeMemoryCard        @ 0x80021CE4                 FUN_80021d44           +0x60
//     ShutdownMemoryCard          @ 0x80021D34                 FUN_80021d94           +0x60
//     ProbeMemoryCard             @ 0x80021E34                 FUN_80021e94           +0x60
//     RepollMemoryCard            @ 0x80021F0C                 FUN_80021f6c           +0x60
//     QueryCardStatus             @ 0x80021FB4                 FUN_80022014           +0x60
//     IsSaveFileMissing           @ 0x800220D4                 FUN_80022134           +0x60
//     FUN_800221d0 (0xF4.. poll)  @ 0x800221D0                 FUN_80022230           +0x60
//     FUN_80022244 (0xF0.. poll)  @ 0x80022244                 FUN_800222a4           +0x60
//     FUN_800222b8 (0xF4.. drain) @ 0x800222B8                 FUN_80022318           +0x60
//     FUN_80022300 (0xF0.. drain) @ 0x80022300                 FUN_80022360           +0x60
//     OpenMemoryCardEvents        @ 0x80022348                 FUN_800223a8           +0x60
//     ReadSaveRecord              @ 0x80022810                 FUN_80022870           +0x60
//
// Twelve functions, one constant offset, bodies that match statement for statement. The two
// overlays link the same object file at a slightly different address. So the SEMANTICS of this
// cluster are already closed by a port that exists in this repository; this file only had to check
// each body against VS.EXE's own decompilation, which it does, and to transliterate the FOUR
// FUNCTIONS VS.EXE HAS THAT SELECT.EXE'S SLICE DID NOT COVER: the save WRITER FUN_80022758, the
// save-file CREATOR FUN_80022510, the FREE-BLOCK count FUN_80022084, and the two big drivers
// FUN_80022ab0 and FUN_80023314.
//
// NAMES ARE LEFT RAW ANYWAY. The SELECT.EXE twins carry closed names, and it would be tempting to
// copy them across. They are not copied: these are DIFFERENT ADDRESSES IN A DIFFERENT OVERLAY, the
// wiring is the main session's, and rule 6 asks for raw names until the evidence is decisive about
// THIS symbol. The correspondence table above is the evidence, recorded here rather than spent on
// a rename this slice does not own.
//
// WHO CALLS WHAT — the two questions the brief asked first.
//   FUN_80022ab0 has exactly THREE callers and all three are the tiny wrappers at the bottom of
//   this file: FUN_80022a34 (0x80022A44), FUN_80022a5c (0x80022A70), FUN_80022a88 (0x80022A98).
//   They differ only in the two mode halfwords they set first — (d258=0, d25c=0), (d258=1,
//   d25c=0), (d25c=1) — so FUN_80022ab0 is one driver with three entry modes. Those wrappers are
//   in turn called from 0x8002B948, 0x8002F50C, 0x800294A4, 0x8002B29C, 0x8002EDB4 and
//   FUN_80031dc8 @ 0x80031DE4 — five of the six inside the 0x80029000..0x80032000 stretch that
//   Ghidra has NOT split into functions yet (get-decompilation on those addresses answers
//   "UndefinedFunction_80029A98" / "UndefinedFunction_8002C504"). That stretch is where this
//   overlay's menu/flow code lives, and it is unported.
//
//   FUN_80023314 has NO caller in the sense the brief expected either: find-cross-references
//   direction "to" returns TWO UNCONDITIONAL_CALL references, from 0x8002A228 and 0x8002CEC4, and
//   both are inside that same undisassembled stretch. So it is not an address-taken callback and
//   nothing holds a pointer to it — it is a plain jal target from code Ghidra has not yet made
//   into functions, which is why the function's callerCount reads 0.
//
// WHAT THE TWO DRIVERS DO. Both are the same shape as SELECT.EXE's RunSaveLoadFlow: a state
// machine on DAT_8008d260, run in a `do { } while (bVar)` so that a state which sets the "keep
// going" flag advances again inside the SAME call, and every state that does not draws one
// full-screen quad and returns. FUN_80022ab0 is the richer one (states 0..0xF, save AND load AND
// format AND create); FUN_80023314 is a cut-down variant with the same state numbering and a
// different mix. Their return values are 0 / 1 / 2, which is what the undisassembled callers route
// on.
//
// THE SEVEN SAVE RECORDS, closed by putting the two drivers' save and load sides side by side:
//     record 0  <- 0x801FF018, 64 bytes   (SharedHighRam's g_OptionsRecord64 — the options block)
//     record 1  <- 0x801FF200, record 2 <- 0x801FF208, record 3 <- 0x801FF210   (stride 8)
//     record 4  <- 0x801FF218, record 5 <- 0x801FF228, record 6 <- 0x801FF238   (stride 16)
// The save side reaches records 1..3 as `&DAT_801ff1f8 + i*8` for i = 1,2,3 and records 4..6 as
// `&DAT_801ff1d8 + i*16` for i = 4,5,6; both land exactly on the addresses above, which is what
// closes the two bases. The load side (FUN_80022ab0 state 0xE) writes 8 bytes back for records
// 1..3 and 16 bytes back for records 4..6, at those same addresses. Every record on the card is
// 128 bytes regardless, so the save side READS 64 bytes from each of those bases and the load side
// KEEPS only the first 8 or 16 — the original's own asymmetry, reproduced.
//
// REPORTED UPWARD, NOT FIXED HERE: SharedHighRam.Size is 0x248, i.e. 0x801FF000..0x801FF247, and
// the save side reads 64 bytes from 0x801FF238, which runs to 0x801FF277. PsxRam answers an
// unresolved read with 0 and drops an unresolved write, so records 5 and 6 will save as zero-tails
// rather than crash. Growing that region is SharedHighRam.cs's owner's call.
//
// OWNERSHIP AND CROSS-CALLS. SpriteDrawer.FUN_80052db4 (the sprite drawer) and
// PadInput.g_PadNewlyPressed (DAT_8008d43c) are called and read by qualified name and NOT
// redeclared. VS_EXE_exe's DAT_8008d420 is `private`, so its address is read through PsxRam
// exactly as AnimCmdAppearance.cs already does for the same word; both files want it `internal`.
internal static class MemoryCard
{
    // =====================================================================================
    // GLOBALS
    // =====================================================================================

    // GHIDRA: DAT_8008d0fc @ 0x8008D0FC (VS.EXE)
    // .sdata, six bytes read straight out of the image with read-memory:
    // 62 75 30 30 3A 00 = "bu00:", the BIOS device name for memory card port 1. This is also VS.EXE's
    // GP BASE (gp = 0x8008D0FC, the project's established value), which is why every state global
    // below is a small positive gp offset.
    // The five callers copy it into a stack buffer as an undefined4 + undefined2 pair
    // (DAT_8008d0fc + DAT_8008d100) and strcat the file name on; the port models that buffer as a
    // C# string, the convention LibApi's open(string, int) / firstfile(string, ...) exist for.
    internal const string DAT_8008d0fc = "bu00:";

    // GHIDRA: DAT_8008d104 @ 0x8008D104 (VS.EXE)
    // 62 75 31 30 3A 00 = "bu10:", port 2. Every call site in this file passes param_1 == 0, so no
    // reachable branch loads it; it is kept because every `if (param_1 == 0) ... else ...` is kept.
    internal const string DAT_8008d104 = "bu10:";

    // GHIDRA: DAT_8008d254 @ 0x8008D254 (VS.EXE)
    // gp+0x158, a SIGNED HALFWORD. Both drivers open with `sh v0,0x158(gp)` followed by
    // `sll v0,v0,0x10 / sra v0,v0,0x10` (read at 0x80022AD4-0x80022ADC), which is what settles the
    // width and the sign: it holds FUN_80022014's card-status code and is then tested as
    // `== 2` and as the unsigned range `(uint)(x - 1) < 2`, i.e. 1 or 2.
    private static short DAT_8008d254;

    // GHIDRA: DAT_8008d258 @ 0x8008D258 (VS.EXE)
    // gp+0x15C, halfword (`sh` at 0x80022A40 and 0x80022A68). Set 0 by FUN_80022a34 and 1 by
    // FUN_80022a5c. FUN_80022ab0 state 0xC reads it to pick WHICH GROUP of records to save:
    // 0 -> records 1..3, non-zero -> records 4..6.
    private static short DAT_8008d258;

    // GHIDRA: DAT_8008d25c @ 0x8008D25C (VS.EXE)
    // gp+0x160, halfword. Set 0 by FUN_80022a34 / FUN_80022a5c and 1 by FUN_80022a88.
    // FUN_80022ab0 reads it in states 0, 1 and 7 to pick between the SAVE path and the LOAD path.
    private static short DAT_8008d25c;

    // GHIDRA: DAT_8008d260 @ 0x8008D260 (VS.EXE)
    // gp+0x164, halfword — THE STATE WORD both drivers switch on. Read as `lh v1,0x164(gp)` and
    // bounded by `sltiu v0,v1,0x11`, so the jump table at 0x8002022C has seventeen entries, 0..0x10;
    // 2 and 0x10 have no case body in FUN_80022ab0 and fall to the default.
    private static short DAT_8008d260;

    // GHIDRA: DAT_8008d264 @ 0x8008D264 (VS.EXE)
    // gp+0x168, halfword. The "re-probe the card on the next pass" request, consumed at the head of
    // both drivers' loops exactly like SELECT.EXE's g_CardReprobeRequest.
    private static short DAT_8008d264;

    // GHIDRA: DAT_8008d268 @ 0x8008D268 (VS.EXE)
    // gp+0x16C, halfword. The PASS COUNTER inside the record-walking states: it selects which record
    // this pass handles and is incremented once per pass, so a state that only handles passes 0..2
    // still idles until the pass number its exit test names.
    private static short DAT_8008d268;

    // GHIDRA: DAT_8008d408 @ 0x8008D408, DAT_8008d40c @ 0x8008D40C,
    //         DAT_8008d410 @ 0x8008D410, DAT_8008d414 @ 0x8008D414 (VS.EXE)
    // The four open event descriptors on the card-driver class 0xF4000001, at specs 0x0004, 0x8000,
    // 0x0100 and 0x2000. FUN_80022230 maps them to codes 0, 1, 2 and 4.
    private static int DAT_8008d408;

    private static int DAT_8008d40c;

    private static int DAT_8008d410;

    private static int DAT_8008d414;

    // GHIDRA: DAT_8008d41c @ 0x8008D41C (VS.EXE)
    // Set 1 by FUN_80021e94 when the probe took its _card_clear arm and 0 otherwise.
    // PARTIAL: WRITE-ONLY, exactly as SELECT.EXE's DAT_80055b68 is. Nothing in this cluster reads
    // it back; the stores are kept because the original makes them. CS0414 is suppressed for that
    // reason — there is no reader to port, so the warning is about the ORIGINAL, not about the port.
#pragma warning disable CS0414
    private static short DAT_8008d41c;
#pragma warning restore CS0414

    // GHIDRA: DAT_8008d42c @ 0x8008D42C, DAT_8008d430 @ 0x8008D430,
    //         DAT_8008d434 @ 0x8008D434, DAT_8008d438 @ 0x8008D438 (VS.EXE)
    // The four open event descriptors on the card-write/clear class 0xF0000011, same four specs.
    private static int DAT_8008d42c;

    private static int DAT_8008d430;

    private static int DAT_8008d434;

    private static int DAT_8008d438;

    // GHIDRA: DAT_8008d5f8 @ 0x8008D5F8 (VS.EXE)
    // The one POLY_F4 both drivers submit at the end of every non-continuing pass — a full-screen
    // 320x256 semi-transparent quad, i.e. the dimming layer the card dialogue draws over.
    // It is 24 bytes of .bss addressed BY ADDRESS (SetSemiTrans and AddPrim both take a pointer),
    // so it is a RamRegion-backed byte array rather than a C# object: LibGpu.AddPrim(int, int)
    // resolves 0x8008D5F8 back to these bytes.
    // The field offsets FUN_800229a4 writes are the standard POLY_F4 layout and they tile exactly:
    //   +4 DAT_8008d5fc r0   +5 DAT_8008d5fd g0   +6 DAT_8008d5fe b0
    //   +8 DAT_8008d600 x0   +0x0A DAT_8008d602 y0   +0x0C DAT_8008d604 x1   +0x0E DAT_8008d606 y1
    //   +0x10 DAT_8008d608 x2   +0x12 DAT_8008d60a y2   +0x14 DAT_8008d60c x3   +0x16 DAT_8008d60e y3
    private const int Dat8008d5f8Address = unchecked((int)0x8008D5F8);

    private static readonly byte[] DAT_8008d5f8 = RamRegion(Dat8008d5f8Address, 24);

    // GHIDRA: DAT_8008d420 @ 0x8008D420 (VS.EXE)
    // The active DRAWENV's ADDRESS. VS_EXE_exe.cs already declares this word — as `private static
    // int DAT_8008d420` — so this file must NOT declare a second storage for it, and reads it
    // through PsxRam at the raw address instead. That is the same workaround AnimCmdAppearance.cs
    // already carries for the same word and for the same reason.
    // REPORTED: VS_EXE_exe.DAT_8008d420 needs to become `internal` (or gain an accessor); until it
    // does, two files reach the same global by two different routes and only one of them is the
    // one main writes.
    private const int Dat8008d420Address = unchecked((int)0x8008D420);

    // GHIDRA: PTR_DAT_8007ff0c @ 0x8007FF0C, PTR_DAT_8007ff18 @ 0x8007FF18,
    //         PTR_DAT_8007ff1c @ 0x8007FF1C, PTR_DAT_8007ff24 @ 0x8007FF24,
    //         PTR_DAT_8007ff2c @ 0x8007FF2C (VS.EXE)
    // .data POINTER SLOTS, not the data. The drivers do `lui a0,0x8008 / lw a0,-0x00e4(a0)`
    // (0x80022BF4), i.e. a LOAD from the slot, so the value handed to the drawer is what the slot
    // holds. Read out of the image: 0x8007FF0C -> 0x8007FCC8, 0x8007FF18 -> 0x8007FD10,
    // 0x8007FF1C -> 0x8007FD50, 0x8007FF24 -> 0x8007FDE4, 0x8007FF2C -> 0x8007FE64. The nine slots
    // 0x8007FF0C..0x8007FF2C are consecutive and step by 0x18/0x40/0x54-sized descriptors, so they
    // are one table of message/sprite descriptors; WHICH message each one is, is not closed here,
    // which is why they stay raw addresses and the loads stay loads.
    private const int PtrDat8007ff0cAddress = unchecked((int)0x8007FF0C);

    private const int PtrDat8007ff18Address = unchecked((int)0x8007FF18);

    private const int PtrDat8007ff1cAddress = unchecked((int)0x8007FF1C);

    private const int PtrDat8007ff24Address = unchecked((int)0x8007FF24);

    private const int PtrDat8007ff2cAddress = unchecked((int)0x8007FF2C);

    // GHIDRA: DAT_801ff018 @ 0x801FF018 (VS.EXE)
    // Save record 0's payload — SharedHighRam's g_OptionsRecord64, the same 64-byte options block
    // SELECT.EXE's RunSaveLoadFlow loads into. Reached by raw address through PsxRam, which
    // VS_EXE_exe.ResolveAddress chains to SharedHighRam; SharedHighRam is not redeclared here.
    private const int Dat801ff018Address = unchecked((int)0x801FF018);

    // GHIDRA: DAT_801ff1f8 @ 0x801FF1F8, DAT_801ff1d8 @ 0x801FF1D8 (VS.EXE)
    // The two BASES the save side indexes: `base + i*8` for i = 1,2,3 lands on 0x801FF200 /
    // 0x801FF208 / 0x801FF210, and `base + i*16` for i = 4,5,6 lands on 0x801FF218 / 0x801FF228 /
    // 0x801FF238. Neither base is itself touched — only the indexed forms are — which is why they
    // are kept as the bases the original computes from rather than replaced by the six addresses.
    private const int Dat801ff1f8Address = unchecked((int)0x801FF1F8);

    private const int Dat801ff1d8Address = unchecked((int)0x801FF1D8);

    // GHIDRA: DAT_801ff200 @ 0x801FF200, DAT_801ff204 @ 0x801FF204,
    //         DAT_801ff218 @ 0x801FF218, DAT_801ff1e8 @ 0x801FF1E8 (VS.EXE)
    // The forms the LOAD side and FUN_80022ab0's state 0xC write through, kept separate from the
    // bases above because the original spells them separately.
    private const int Dat801ff200Address = unchecked((int)0x801FF200);

    private const int Dat801ff204Address = unchecked((int)0x801FF204);

    private const int Dat801ff218Address = unchecked((int)0x801FF218);

    private const int Dat801ff1e8Address = unchecked((int)0x801FF1E8);

    // GHIDRA: DAT_8002002c @ 0x8002002C .. 0x8002022B (VS.EXE)
    // THE MEMORY-CARD FILE HEADER, 512 bytes of .data that FUN_80022510 copies to the card as its
    // first four 128-byte frames. Closed by reading the first bytes out of the image:
    // 53 43 13 01 then Shift-JIS text — "SC", the BIOS magic for a save file's title frame, block
    // count 1, then the title. Frames 1..3 are the icon bitmap and palette. This is the standard
    // PSX save-file header, which is why FUN_80022510 writes it before any record exists.
    // The extents are the loop bounds themselves: the first loop stops at 0x800200AC and the second
    // at the switch table 0x8002022C.
    private const int Dat8002002cAddress = unchecked((int)0x8002002C);

    private const int Dat800200acAddress = unchecked((int)0x800200AC);

    private const int SwitchdataD8002022cAddress = unchecked((int)0x8002022C);

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: FUN_80022ab0's `local_a8 / local_a4 / local_a0 / local_9c` stack block, which
    // FUN_80022870 fills with 64 bytes by POINTER. The original passes &local_a8; the port gives
    // that local a real PSX address in the real stack region (crt0 starts sp at 0x807FFFF8) and
    // backs it with a region, which is this port's established way of handing a C# local a PSX
    // address. The address is distinct from every other in-use scratch address
    // (0x807FFFC0 AnimCmdControl, 0x807FFFD0 BattleScene, 0x807FFFE0 AnimCmdEffects,
    // 0x807FFFF0 AnimVmInterpreter) so it can never alias one.
    private const int Local_a8Address = unchecked((int)0x807FFF40);

    private static readonly byte[] RAM_local_a8 = RamRegion(Local_a8Address, 64);

    // JUSTIFICATION: C# language bridge only
    // RELATION: the BIOS/libc strcat at 0x80079A7C that five functions below call. PsxSdkMonogame
    // provides no strcat, and rule 13 forbids transliterating an SDK routine into a game file, so
    // this is only the bridge that lets those five call sites keep their original shape — the fixed
    // stack buffer they strcat into is modelled as a C# string, exactly as LibApi's open(string,
    // int) overload already assumes. SELECT_EXE/MemoryCard.cs carries the identical one-liner for
    // the identical reason. REPORTED as a missing SDK routine rather than hidden here.
    private static string strcat(string param_1, string param_2)
    {
        return param_1 + param_2;
    }

    // =====================================================================================
    // BRING-UP AND TEARDOWN
    // =====================================================================================

    // GHIDRA: FUN_80021d44 @ 0x80021D44 (VS.EXE)
    // Eighty bytes, seven calls, no locals. THE BRING-UP, and the same seven calls in the same order
    // as SELECT.EXE's InitializeMemoryCard @ 0x80021CE4 and TITLE.EXE's @ 0x80022630.
    // One caller, LAB_80029484 @ 0x80029484, inside the undisassembled stretch.
    internal static void FUN_80021d44()
    {
        InitCARD(1);
        StartCARD();
        _bu_init();
        FUN_800223a8();
        _card_auto(0);
        ChangeClearPAD(0);
        FUN_800229a4();
    }

    // GHIDRA: FUN_800223a8 @ 0x800223A8 (VS.EXE)
    // Three hundred and sixty bytes. Eight OpenEvent calls inside a critical section, then eight
    // EnableEvent calls outside it. The split is load-bearing on the console — the table is built
    // with the ISR masked and only armed once a delivery may safely land — and LibApi's OpenEvent
    // honours it by NOT arming the descriptor it returns.
    // FUN_8007a940 and FUN_8007ac10 are EnterCriticalSection / ExitCriticalSection: three-instruction
    // `li a0,1 / syscall 0 / jr ra` bodies at those addresses, above 0x800632C4, so SDK per rule 13.
    // ALL EIGHT CALLBACKS ARE NULL, which is what makes these poll-only events.
    internal static void FUN_800223a8()
    {
        EnterCriticalSection();
        DAT_8008d408 = (int)OpenEvent(0xf4000001, 4, 0x2000, null);
        DAT_8008d40c = (int)OpenEvent(0xf4000001, 0x8000, 0x2000, null);
        DAT_8008d410 = (int)OpenEvent(0xf4000001, 0x100, 0x2000, null);
        DAT_8008d414 = (int)OpenEvent(0xf4000001, 0x2000, 0x2000, null);
        DAT_8008d42c = (int)OpenEvent(0xf0000011, 4, 0x2000, null);
        DAT_8008d430 = (int)OpenEvent(0xf0000011, 0x8000, 0x2000, null);
        DAT_8008d434 = (int)OpenEvent(0xf0000011, 0x100, 0x2000, null);
        DAT_8008d438 = (int)OpenEvent(0xf0000011, 0x2000, 0x2000, null);
        ExitCriticalSection();
        EnableEvent(DAT_8008d408);
        EnableEvent(DAT_8008d40c);
        EnableEvent(DAT_8008d410);
        EnableEvent(DAT_8008d414);
        EnableEvent(DAT_8008d42c);
        EnableEvent(DAT_8008d430);
        EnableEvent(DAT_8008d434);
        EnableEvent(DAT_8008d438);
    }

    // GHIDRA: FUN_80021d94 @ 0x80021D94 (VS.EXE)
    // Two hundred and fifty-six bytes — the mirror image of FUN_80021d44 + FUN_800223a8. Eight
    // DisableEvent OUTSIDE the critical section, eight CloseEvent inside it, then the card stopped
    // and the pad handed back to the BIOS driver. Three callers, at 0x80031BF0, 0x80031C18 and
    // 0x80031C94, all inside the undisassembled stretch.
    internal static void FUN_80021d94()
    {
        DisableEvent(DAT_8008d408);
        DisableEvent(DAT_8008d40c);
        DisableEvent(DAT_8008d410);
        DisableEvent(DAT_8008d414);
        DisableEvent(DAT_8008d42c);
        DisableEvent(DAT_8008d430);
        DisableEvent(DAT_8008d434);
        DisableEvent(DAT_8008d438);
        EnterCriticalSection();
        CloseEvent(DAT_8008d408);
        CloseEvent(DAT_8008d40c);
        CloseEvent(DAT_8008d410);
        CloseEvent(DAT_8008d414);
        CloseEvent(DAT_8008d42c);
        CloseEvent(DAT_8008d430);
        CloseEvent(DAT_8008d434);
        CloseEvent(DAT_8008d438);
        ExitCriticalSection();
        StopCARD();
        StartPAD();
        ChangeClearPAD(0);
    }

    // GHIDRA: FUN_800229a4 @ 0x800229A4 (VS.EXE)
    // One hundred and forty-four bytes. Resets the state machine AND builds the full-screen quad in
    // one go — it is both SELECT.EXE's ResetCardOperationState and the primitive setup that overlay's
    // slice never reached.
    // The eleven stores after SetPolyF4 were decoded from the bytes rather than trusted: every
    // vertex store is `sh` (opcode 0xA4) and every colour store is `sb` (opcode 0xA0), read at
    // 0x800229C4..0x80022A20. The quad is (0,0) (0x140,0) (0,0x100) (0x140,0x100) — 320 x 256, the
    // whole PAL screen — in RGB (0,0,0), i.e. a black dimming layer. The two state halfwords are
    // `sh zero,0x164(gp)` and `sh zero,0x168(gp)`, which is what fixes gp at 0x8008D0FC.
    internal static void FUN_800229a4()
    {
        DAT_8008d260 = 0;
        DAT_8008d264 = 0;
        SetPolyF4(DAT_8008d5f8, 0);
        MipsMemory.WriteU16(DAT_8008d5f8, 8, 0);
        MipsMemory.WriteU16(DAT_8008d5f8, 0x0a, 0);
        MipsMemory.WriteU16(DAT_8008d5f8, 0x0c, 0x140);
        MipsMemory.WriteU16(DAT_8008d5f8, 0x0e, 0);
        MipsMemory.WriteU16(DAT_8008d5f8, 0x10, 0);
        MipsMemory.WriteU16(DAT_8008d5f8, 0x12, 0x100);
        MipsMemory.WriteU16(DAT_8008d5f8, 0x14, 0x140);
        MipsMemory.WriteU16(DAT_8008d5f8, 0x16, 0x100);
        DAT_8008d5f8[4] = 0;
        DAT_8008d5f8[5] = 0;
        DAT_8008d5f8[6] = 0;
    }

    // =====================================================================================
    // THE KERNEL-EVENT HANDSHAKE
    // =====================================================================================

    // GHIDRA: FUN_80022318 @ 0x80022318 (VS.EXE)
    // Seventy-two bytes: four TestEvent calls on the 0xF4000001 handles WITH EVERY RESULT THROWN
    // AWAY. A DRAIN, issued immediately before each _card_info / _card_load so the poll that follows
    // cannot read a stale delivery. It is dead code unless TestEvent CONSUMES the flag it reports —
    // which is exactly how LibApi implements it, and this function is part of the evidence for that.
    // Six call sites, all in this file.
    internal static void FUN_80022318()
    {
        TestEvent(DAT_8008d408);
        TestEvent(DAT_8008d40c);
        TestEvent(DAT_8008d410);
        TestEvent(DAT_8008d414);
    }

    // GHIDRA: FUN_80022360 @ 0x80022360 (VS.EXE)
    // Seventy-two bytes, the 0xF0000011 twin of FUN_80022318. Issued immediately before _card_clear.
    internal static void FUN_80022360()
    {
        TestEvent(DAT_8008d42c);
        TestEvent(DAT_8008d430);
        TestEvent(DAT_8008d434);
        TestEvent(DAT_8008d438);
    }

    // GHIDRA: FUN_80022230 @ 0x80022230 (VS.EXE)
    // One hundred and sixteen bytes, six call sites. THE 0xF4000001 POLL: spin until one of the four
    // handles fires and map it to a code — 0x0004 -> 0, 0x8000 -> 1, 0x0100 -> 2, 0x2000 -> 4.
    // The do/while has NO bail-out. It terminates in this port because every call site issues a card
    // command first and LibApi's _card_* deliver synchronously; a call site that polled without
    // issuing one would hang, and that would be the honest answer.
    internal static int FUN_80022230()
    {
        int iVar1;

        do
        {
            iVar1 = (int)TestEvent(DAT_8008d408);
            if (iVar1 == 1)
            {
                return 0;
            }

            iVar1 = (int)TestEvent(DAT_8008d40c);
            if (iVar1 == 1)
            {
                return 1;
            }

            iVar1 = (int)TestEvent(DAT_8008d410);
            if (iVar1 == 1)
            {
                return 2;
            }

            iVar1 = (int)TestEvent(DAT_8008d414);
        }
        while (iVar1 != 1);

        return 4;
    }

    // GHIDRA: FUN_800222a4 @ 0x800222A4 (VS.EXE)
    // One hundred and sixteen bytes, the 0xF0000011 twin of FUN_80022230, same code map. Its two
    // call sites both follow a _card_clear.
    internal static int FUN_800222a4()
    {
        int iVar1;

        do
        {
            iVar1 = (int)TestEvent(DAT_8008d42c);
            if (iVar1 == 1)
            {
                return 0;
            }

            iVar1 = (int)TestEvent(DAT_8008d430);
            if (iVar1 == 1)
            {
                return 1;
            }

            iVar1 = (int)TestEvent(DAT_8008d434);
            if (iVar1 == 1)
            {
                return 2;
            }

            iVar1 = (int)TestEvent(DAT_8008d438);
        }
        while (iVar1 != 1);

        return 4;
    }

    // =====================================================================================
    // THE CARD QUERIES
    // =====================================================================================

    // GHIDRA: FUN_80021e94 @ 0x80021E94 (VS.EXE)
    // Two hundred and sixteen bytes, five call sites (0x8002948C plus two in each driver).
    // THE PROBE. Two retry loops of at most five passes each with an optional _card_clear between:
    //   pass 1  drain, _card_info(chan), poll. Break as soon as the code is not 1 ("failed").
    //   if the code is 4 (new / unformatted card): drain the 0xF0000011 side, _card_clear, poll.
    //   pass 2  drain, _card_load(chan), poll. Return the first code that is not 1.
    // Both loops return 1 only by exhausting five failing attempts.
    // Byte-identical in shape to SELECT.EXE's ProbeMemoryCard @ 0x80021E34.
    internal static int FUN_80021e94(int param_1)
    {
        int iVar1;
        int iVar2;

        iVar2 = 0;
        do
        {
            FUN_80022318();
            _card_info(param_1);
            iVar1 = FUN_80022230();
            if (iVar1 != 1)
            {
                break;
            }

            iVar2 = iVar2 + 1;
        }
        while (iVar2 < 5);

        DAT_8008d41c = 0;
        iVar2 = 0;
        if (iVar1 == 4)
        {
            DAT_8008d41c = 1;
            FUN_80022360();
            _card_clear(param_1);
            FUN_800222a4();
        }

        do
        {
            FUN_80022318();
            _card_load(param_1);
            iVar1 = FUN_80022230();
            if (iVar1 != 1)
            {
                return iVar1;
            }

            iVar2 = iVar2 + 1;
        }
        while (iVar2 < 5);

        return 1;
    }

    // GHIDRA: FUN_80021f6c @ 0x80021F6C (VS.EXE)
    // One hundred and sixty-eight bytes, EIGHT call sites — all of them (0x80029B04, 0x80029D00,
    // 0x8002B20C, 0x8002B384, 0x8002C574, 0x8002C978, 0x8002ED1C, 0x8002EEA0) inside the
    // undisassembled 0x80029000..0x80032000 stretch, so NOTHING IN THIS FILE CALLS IT. It is ported
    // because it is inside the commissioned range and because it is the twin of SELECT.EXE's
    // RepollMemoryCard @ 0x80021F0C, which that overlay's ListCursor calls once per frame while a
    // card picker is up so that inserting or removing a card is noticed.
    // param_1 is the PREVIOUS status. Its two arms: a code-4 answer is only cleared when the caller
    // was ALREADY at 4, and a code-2 answer is retried once; neither fires when param_1 is 2.
    internal static int FUN_80021f6c(int param_1)
    {
        int iVar1;

        FUN_80022318();
        _card_info(0);
        iVar1 = FUN_80022230();
        if (((iVar1 == 4) && (param_1 != 2)) && (param_1 == 4))
        {
            FUN_80022360();
            _card_clear(0);
            iVar1 = FUN_800222a4();
        }

        if ((iVar1 == 2) && (param_1 != 2))
        {
            FUN_80022318();
            _card_info(0);
            iVar1 = FUN_80022230();
        }

        return iVar1;
    }

    // GHIDRA: FUN_80022014 @ 0x80022014 (VS.EXE)
    // One hundred and twelve bytes, two call sites — the head of each driver. A single _card_info,
    // retried exactly once when the code came back 2.
    // PARTIAL ON param_1. Both call sites leave a0 UNTOUCHED: FUN_80022ab0's prologue
    // (0x80022AB0..0x80022AE4, read byte for byte) writes s0/s1/s8 and the saved registers and then
    // `jal` with `sw s2,0xd0(sp)` in the delay slot, so a0 holds whatever the caller of the DRIVER
    // left there — and the drivers' own callers are undisassembled. Ghidra prints the call with no
    // argument for exactly that reason. 0 is passed here because that is the only value this port
    // can name honestly; it is read ONLY inside `(iVar1 == 2) && (param_1 != 2)`, and code 2 means
    // spec 0x0100, which nothing in this port ever delivers, so no reachable behaviour depends on it.
    internal static int FUN_80022014(int param_1)
    {
        int iVar1;

        FUN_80022318();
        _card_info(0);
        iVar1 = FUN_80022230();
        if ((iVar1 == 2) && (param_1 != 2))
        {
            FUN_80022318();
            _card_info(0);
            iVar1 = FUN_80022230();
        }

        return iVar1;
    }

    // GHIDRA: FUN_800220f4 @ 0x800220F4 (VS.EXE)
    // Sixty-four bytes. FORMAT THE CARD: one `format("bu00:")` on the bare device name — note the
    // file name is NOT appended here, unlike every other user of DAT_8008d0fc.
    // Two call sites, state 5 of each driver.
    // Ghidra types it `bool` and the body is `return iVar1 == 0;`, compiled as `sltiu v0,v0,1`, so
    // it RETURNS 1 WHEN format FAILED. Both call sites read it as `if (iVar == 0)` = "the format
    // worked, go on"; the int form below keeps that reading exact.
    // NOT IN THE COMMISSIONED LIST but inside the commissioned 0x80021D44..0x80023313 range and a
    // callee of both drivers, so it is ported in full rather than stubbed.
    internal static int FUN_800220f4(int param_1)
    {
        int iVar1;
        string puVar2;

        if (param_1 == 0)
        {
            puVar2 = DAT_8008d0fc;
        }
        else
        {
            puVar2 = DAT_8008d104;
        }

        iVar1 = format(puVar2);
        return iVar1 == 0 ? 1 : 0;
    }

    // GHIDRA: FUN_80022134 @ 0x80022134 (VS.EXE)
    // One hundred bytes. Builds "bu00:BISLPS-00355DRAGON" in a 32-byte stack buffer and asks the
    // BIOS card directory whether it is there. RETURNS 1 WHEN THE FILE IS ABSENT: the original is
    // `return iVar1 == 0;` over a firstfile that answers 0 when the directory has no match. Both
    // drivers' state 7 reads it as `if (iVar == 0)` = "the save exists".
    // The DIRENTRY it fills (`undefined1 auStack_30[40]`) is never looked at.
    internal static int FUN_80022134(int param_1)
    {
        int iVar1;
        string local_50;
        LibMcrd.DIRENTRY auStack_30 = new LibMcrd.DIRENTRY();

        if (param_1 == 0)
        {
            local_50 = DAT_8008d0fc;
        }
        else
        {
            local_50 = DAT_8008d104;
        }

        local_50 = strcat(local_50, "BISLPS-00355DRAGON");
        iVar1 = firstfile(local_50, auStack_30);
        return iVar1 == 0 ? 1 : 0;
    }

    // GHIDRA: FUN_80022084 @ 0x80022084 (VS.EXE)
    // One hundred and twelve bytes. THE FREE-BLOCK COUNT: walk the whole card directory with
    // firstfile("bu00:*.*") / nextfile, sum each entry's size divided by 8192, and return
    // 15 minus that sum. A PSX card holds 15 usable blocks of 8 KB (frame 0 is the directory), so
    // the answer is "how many blocks are still free". Both drivers' state 8 test it against 0.
    //
    // BLOCKED: `local_18` is DIRENTRY + 0x18, which in the psyq struct
    // (`char name[20]; long attr; long size; ...`) is the SIZE field. PsxSdkMonogame's
    // LibMcrd.DIRENTRY does not model that layout — it is `int status; int[3] reserved;
    // byte[20] name; byte pad`, with no size anywhere — so this port has nothing to read.
    // The accumulation is transliterated with the size taken as 0, which makes this function
    // return a constant 0xF, "the card is empty". The loop, its bounds and the call pair are
    // reproduced exactly so that only the one unavailable field is missing.
    // REPORTED: LibMcrd.DIRENTRY needs a `size` field (and CardFileFirst/CardFileNext need to fill
    // it) before this function can answer truthfully. That is an SDK change, not a game-file one.
    internal static int FUN_80022084(int param_1)
    {
        int iVar1;
        string pcVar2;
        int iVar3;
        LibMcrd.DIRENTRY auStack_30 = new LibMcrd.DIRENTRY();
        int local_18;

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
            // BLOCKED: DIRENTRY + 0x18 (psyq `size`) has no counterpart in LibMcrd.DIRENTRY.
            local_18 = 0;
            iVar3 = iVar3 + (local_18 >> 0xd);
            iVar1 = nextfile(auStack_30);
        }

        return 0xf - iVar3;
    }

    // =====================================================================================
    // THE RECORD I/O
    // =====================================================================================

    // GHIDRA: FUN_80022870 @ 0x80022870 (VS.EXE)
    // Three hundred and eight bytes, two call sites (both in FUN_80022ab0's state 0xE). THE RECORD
    // READ. Opens "bu00:BISLPS-00355DRAGON", seeks to 0x200 + param_2 * 0x80, reads ONE 128-byte
    // record, validates it, and copies its 64-byte payload to param_3.
    //
    // THE RECORD LAYOUT, closed from the stack frame Ghidra recovered — `char local_90` at -0x90,
    // `byte local_8f[64]` at -0x8f, `undefined1 auStack_4f[62]` at -0x4f, `byte local_11` at -0x11,
    // and the read is 0x80 bytes into &local_90, so those four names tile the record exactly:
    //     byte 0        magic, must be '.' (0x2E)
    //     bytes 1..64   the payload — copied to param_3
    //     bytes 65..126 not read by this function
    //     byte 127      the checksum byte
    // The check is `buf[127] ^ buf[1] ^ ... ^ buf[64] == 0`, i.e. it covers only 64 of the 128
    // bytes. That is what the code does and it is reproduced, not corrected (rule 12).
    // The 0x200 offset is the four header frames FUN_80022510 writes, which is why record N lives at
    // 0x200 + N * 0x80.
    //
    // RETURNS 0x80 for a good record, -1 for a bad magic or a bad checksum, 0 when the file will not
    // open or the read is short. `close` is called on BOTH the short-read path and the good path and
    // NOT on the open-failure path — three call sites, matching the image's three.
    //
    // param_3 IS A PSX ADDRESS here, because its only caller passes the address of a stack local
    // and the copy is a byte loop through it.
    internal static int FUN_80022870(int param_1, int param_2, int param_3)
    {
        byte bVar1;
        int iVar2;
        int uVar3;
        int iVar4;
        int pbVar5;
        string local_b0;
        byte[] local_90 = new byte[0x80];

        if (param_1 == 0)
        {
            local_b0 = DAT_8008d0fc;
        }
        else
        {
            local_b0 = DAT_8008d104;
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
                    // local_8f is local_90 + 1; auStack_4f is local_90 + 0x41, so the loop runs the
                    // sixty-four payload bytes and no more. local_11 is local_90[0x7F], the checksum
                    // byte the read itself brought in — it is XOR-folded, not initialised.
                    pbVar5 = 1;
                    do
                    {
                        bVar1 = local_90[pbVar5];
                        pbVar5 = pbVar5 + 1;
                        PsxRam.WriteU8(param_3, bVar1);
                        local_90[0x7f] = (byte)(bVar1 ^ local_90[0x7f]);
                        param_3 = param_3 + 1;
                    }
                    while (pbVar5 < 0x41);

                    uVar3 = 0x80;
                    if (local_90[0x7f] != 0)
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

    // GHIDRA: FUN_80022758 @ 0x80022758 (VS.EXE)
    // Two hundred and eighty bytes, three call sites (two in FUN_80022ab0, one in FUN_80023314).
    // THE RECORD WRITE — the exact inverse of FUN_80022870, and the function SELECT.EXE's slice
    // never had. Opens the same file for read/write, seeks to 0x200 + param_2 * 0x80, stamps the
    // magic '.', copies 64 bytes from param_3 while XOR-folding them into byte 127, and writes the
    // whole 128-byte record.
    // RETURNS 0x80 on success and 0 on any failure. Both callers test `!= 0x80`.
    //
    // DEVIATION: bytes 65..126 of the record are NEVER WRITTEN by the original — they are whatever
    // the stack held, and the console commits that garbage to the card. C# zero-initialises the
    // buffer, so this port writes deterministic zeroes where the console writes stack residue. The
    // record's own two checked fields (magic and checksum) do not cover that span, so nothing
    // downstream reads it; the difference is recorded rather than papered over.
    //
    // param_3 IS A PSX ADDRESS: its three call sites pass 0x801FF018 and the two indexed bases.
    internal static int FUN_80022758(int param_1, int param_2, int param_3)
    {
        byte bVar1;
        int iVar2;
        int iVar3;
        int pbVar4;
        string local_b0;
        byte[] local_90 = new byte[0x80];

        if (param_1 == 0)
        {
            local_b0 = DAT_8008d0fc;
        }
        else
        {
            local_b0 = DAT_8008d104;
        }

        local_b0 = strcat(local_b0, "BISLPS-00355DRAGON");
        iVar2 = open(local_b0, 2);
        if (iVar2 != -1)
        {
            lseek(iVar2, param_2 * 0x80 + 0x200, 0);
            pbVar4 = 1;
            local_90[0x7f] = 0;
            local_90[0] = 0x2e;
            do
            {
                bVar1 = PsxRam.ReadU8(param_3);
                local_90[pbVar4] = bVar1;
                pbVar4 = pbVar4 + 1;
                local_90[0x7f] = (byte)(bVar1 ^ local_90[0x7f]);
                param_3 = param_3 + 1;
            }
            while (pbVar4 < 0x41);

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

    // GHIDRA: FUN_80022510 @ 0x80022510 (VS.EXE)
    // Five hundred and eighty-four bytes, two call sites, both inside FUN_80022198. THE SAVE-FILE
    // HEADER WRITE: copy 512 bytes of .data into a 512-byte stack frame and write them to the card
    // as four consecutive 128-byte frames at offsets 0, 0x80, 0x100 and 0x180.
    //
    // WHAT THE 512 BYTES ARE. The first loop copies 0x8002002C..0x800200AB (128 bytes) and the
    // second copies 0x800200AC..0x8002022B (384 bytes); the two destinations are `local_210[32]`
    // and `local_190[32]`, which are 0x80 apart, so THE SECOND LOOP DELIBERATELY RUNS OFF THE END OF
    // local_190 and fills auStack_110 and auStack_90 as well. 128 + 384 = 512 and the four stack
    // buffers are contiguous at sp-0x210, -0x190, -0x110, -0x90, so the frame is tiled exactly and
    // nothing is uninitialised. Read out of the image, 0x8002002C begins `53 43 13 01` then
    // Shift-JIS text — "SC", the BIOS magic for a PSX save file's title frame — so this is the
    // standard four-frame header (title + palette + three icon bitmaps).
    //
    // THE `if (true) { ... } else { ... }` PAIR is Ghidra's rendering of one compile-time-resolved
    // alignment choice: the taken branch is the aligned four-word copy, the untaken one the swl/swr
    // unaligned variant. Only the taken branch exists in the instruction stream, so only it is
    // ported; the dead arm is recorded here rather than transliterated as a second code path.
    //
    // JUSTIFICATION: C# language bridge only — the four stack buffers are modelled as ONE
    // `byte[0x200]` because the second copy loop writes across all four of them, which no set of
    // four separate C# arrays can express. Offsets 0, 0x80, 0x100, 0x180 are local_210, local_190,
    // auStack_110, auStack_90 respectively, and the four `write` calls use exactly those offsets.
    //
    // RETURNS 0 when all four frames went out and 1 on any failure. `close` on the good path and on
    // the failure path, not on the open-failure path — two call sites, matching the image's two.
    internal static int FUN_80022510(string param_1)
    {
        int iVar5;
        int iVar6;
        int puVar13;
        int puVar15;
        byte[] auStack_210 = new byte[0x200];

        puVar15 = 0;
        puVar13 = Dat8002002cAddress;
        do
        {
            MipsMemory.WriteI32(auStack_210, puVar15, PsxRam.ReadI32(puVar13));
            MipsMemory.WriteI32(auStack_210, puVar15 + 4, PsxRam.ReadI32(puVar13 + 4));
            MipsMemory.WriteI32(auStack_210, puVar15 + 8, PsxRam.ReadI32(puVar13 + 8));
            MipsMemory.WriteI32(auStack_210, puVar15 + 12, PsxRam.ReadI32(puVar13 + 12));
            puVar13 = puVar13 + 16;
            puVar15 = puVar15 + 16;
        }
        while (puVar13 != Dat800200acAddress);

        // The second destination is local_190, i.e. sp-0x190 = auStack_210 + 0x80.
        puVar13 = 0x80;
        puVar15 = Dat800200acAddress;
        do
        {
            MipsMemory.WriteI32(auStack_210, puVar13, PsxRam.ReadI32(puVar15));
            MipsMemory.WriteI32(auStack_210, puVar13 + 4, PsxRam.ReadI32(puVar15 + 4));
            MipsMemory.WriteI32(auStack_210, puVar13 + 8, PsxRam.ReadI32(puVar15 + 8));
            MipsMemory.WriteI32(auStack_210, puVar13 + 12, PsxRam.ReadI32(puVar15 + 12));
            puVar15 = puVar15 + 16;
            puVar13 = puVar13 + 16;
        }
        while (puVar15 != SwitchdataD8002022cAddress);

        iVar5 = open(param_1, 2);
        if (iVar5 != -1)
        {
            lseek(iVar5, 0, 0);
            iVar6 = write(iVar5, auStack_210, 0, 0x80);
            if (iVar6 == 0x80)
            {
                lseek(iVar5, 0x80, 0);
                iVar6 = write(iVar5, auStack_210, 0x80, 0x80);
                if (iVar6 == 0x80)
                {
                    lseek(iVar5, 0x100, 0);
                    iVar6 = write(iVar5, auStack_210, 0x100, 0x80);
                    if (iVar6 == 0x80)
                    {
                        lseek(iVar5, 0x180, 0);
                        iVar6 = write(iVar5, auStack_210, 0x180, 0x80);
                        if (iVar6 == 0x80)
                        {
                            close(iVar5);
                            return 0;
                        }
                    }
                }
            }

            close(iVar5);
        }

        return 1;
    }

    // GHIDRA: FUN_80022198 @ 0x80022198 (VS.EXE)
    // One hundred and fifty-two bytes, two call sites, state 10 of each driver. CREATE OR REFRESH
    // THE SAVE FILE. `open(name, 0x10200)` is the BIOS create call — the low 0x200 is the block
    // count field (one block) and 0x10000 is the create bit — so:
    //   the open FAILS  -> the file did not exist and could not be made: write the header anyway
    //                      and return 1 regardless of what the write said;
    //   the open WORKS  -> the file exists now: close it and return whatever the header write says.
    // Both arms call FUN_80022510, which is why the callee shows a call count of 2. The first arm
    // DISCARDS the return value and forces 1; that asymmetry is the original's and is kept.
    internal static int FUN_80022198(int param_1)
    {
        int iVar1;
        int uVar2;
        string local_a8;

        if (param_1 == 0)
        {
            local_a8 = DAT_8008d0fc;
        }
        else
        {
            local_a8 = DAT_8008d104;
        }

        local_a8 = strcat(local_a8, "BISLPS-00355DRAGON");
        iVar1 = open(local_a8, 0x10200);
        if (iVar1 == -1)
        {
            FUN_80022510(local_a8);
            uVar2 = 1;
        }
        else
        {
            close(iVar1);
            uVar2 = FUN_80022510(local_a8);
        }

        return uVar2;
    }

    // =====================================================================================
    // THE TWO DRIVERS
    // =====================================================================================

    // JUSTIFICATION: C# language bridge only
    // RELATION: the eighteen-argument call both drivers make eleven times between them, always with
    // the SAME sixteen trailing constants and differing only in the descriptor pointer. It is here
    // so the constants are written once and cannot drift between call sites; it adds no logic, it
    // takes no decision, and it returns exactly what the callee returns.
    //
    // THE RETURN VALUE IS THE ORDERING-TABLE INDEX. The original does `addu s3,v0,zero` immediately
    // after the `jal` (read byte for byte at 0x80022C34-0x80022C38), and the tail then computes
    // `s3 * 4 + 0x70 + DAT_8008d420` as AddPrim's bucket, so the drawer's answer picks the depth the
    // dimming quad is submitted at. SpriteDrawer.FUN_80052db4 is a real implementation returning
    // int, so that value is carried through here rather than lost.
    //
    // The parameter types are SpriteDrawer's, which are Ghidra's: param_2..param_4 short, param_5
    // ushort, param_11/param_12 short, param_13/param_14 sbyte, param_15..param_17 byte. The
    // literals are the ones the image passes; nothing here narrows a value that would not narrow on
    // the console.
    private static int CallFun80052db4(int descriptorPointer)
    {
        return SpriteDrawer.FUN_80052db4(
            descriptorPointer,
            0,
            0,
            0x1000,
            0,
            0,
            0,
            0x1000,
            0x1000,
            0,
            0,
            0,
            0,
            0,
            0xff,
            0xff,
            0xff,
            unchecked((int)0xffffe890));
    }

    // GHIDRA: FUN_80022ab0 @ 0x80022AB0 (VS.EXE)
    // 2148 bytes, 287 decompiled lines, eleven callees, THREE CALLERS — FUN_80022a34, FUN_80022a5c
    // and FUN_80022a88 at the foot of this file, and nothing else in the image.
    //
    // THE FULL SAVE / LOAD / FORMAT DRIVER. One `do { } while (bVar4)` around a seventeen-entry
    // switch on DAT_8008d260; a state that sets bVar4 advances again inside the SAME call, a state
    // that clears it draws the dimming quad and returns. The return value is 0, 1 or 2:
    //   0  the flow ended without doing anything (state 0xF, and state 7's "no file, loading" arm)
    //   1  a save or a load completed
    //   2  the user backed out (pad bit 0x40) or the card went away (status 1 or 2)
    //
    // THE STATES, in the order the switch lists them:
    //   0   entry. -> 3 when DAT_8008d25c is 0 (SAVE side), -> 7 otherwise (LOAD side)
    //   1   the card was re-probed. status 0 or 4 -> same fork as state 0; anything else -> 0 and
    //       return 2 (or 0 when DAT_8008d25c is set)
    //   2   no case body: falls to the default and to the tail with bVar4 unchanged
    //   3   probe again; status 4 (unformatted) -> 4, else -> 7
    //   4   "unformatted card" prompt. 0x40 backs out, 0x20 formats
    //   5   format the card. success -> 6, failure -> 10
    //   6   "formatted" prompt
    //   7   does the save file exist. LOAD side: yes -> 8, no -> 0xC. SAVE side: no -> 0xE,
    //       yes -> 0 returning 0
    //   8   free-block count. zero free -> 9 ("card full"), otherwise -> 10
    //   9   the "card full" prompt
    //   10  create or refresh the file. success -> 0xB, failure -> 0xD
    //   0xB the SEVEN-RECORD SAVE, one record per pass, driven by DAT_8008d268
    //   0xC the PARTIAL SAVE: records 1..3 when DAT_8008d258 is 0, records 4..6 otherwise
    //   0xD the failure prompt
    //   0xE the SEVEN-RECORD LOAD
    //   0xF give up, return 0
    //
    // TWO PROPERTIES OF THE ORIGINAL, REPRODUCED AND NOT CORRECTED (rule 12):
    //   * `unaff_s3` — the ordering-table index the tail feeds to AddPrim — is only ever assigned by
    //     a FUN_80052db4 call. States 0, 1, 5, 7, 8 and 10 return or continue without making one, so
    //     on the console the tail submits the quad at whatever bucket the previous pass computed,
    //     and on the very first pass at whatever s3 held on entry. C# forbids reading an unassigned
    //     local, so it is initialised to 0 here; that is a DEVIATION and it is the only place this
    //     function differs.
    //   * State 0xC's two arms are asymmetric: the DAT_8008d258 == 0 arm writes records 1..3 on
    //     passes 0..2 and then waits until pass 9 to finish, while the other arm writes records 4..6
    //     and finishes on pass 3. Six idle passes on one side and none on the other. That is what
    //     the image does.
    internal static int FUN_80022ab0()
    {
        uint local_a8;
        uint local_a4;
        int local_a0;
        int local_9c;
        bool bVar4;
        short sVar5;
        int puVar6;
        int iVar7;
        int puVar8;
        int iVar9;
        int iVar10;

        bool doCall;

        // DEVIATION: see the note above — the original leaves s3 holding a previous pass's value.
        int unaff_s3 = 0;

        bVar4 = false;
        iVar10 = 0;
        sVar5 = 0;
        DAT_8008d254 = (short)FUN_80022014(0);
        if (DAT_8008d254 == 2)
        {
            DAT_8008d264 = 1;
            DAT_8008d260 = 1;
        }

        do
        {
            if (DAT_8008d264 == 1)
            {
                DAT_8008d264 = 0;
                sVar5 = (short)FUN_80021e94(0);
            }

            switch (DAT_8008d260)
            {
                case 0:
                    DAT_8008d260 = 7;
                    if (DAT_8008d25c == 0)
                    {
                        DAT_8008d260 = 3;
                    }

                    DAT_8008d268 = 0;
                    bVar4 = true;
                    break;

                case 1:
                {
                    DAT_8008d268 = 0;
                    // `if ((sVar5 == 0) || (bVar4 = false, sVar5 == 4))` — the assignment sits in
                    // the second operand of the ||, so it only runs when sVar5 != 0.
                    bool cond;
                    if (sVar5 == 0)
                    {
                        cond = true;
                    }
                    else
                    {
                        bVar4 = false;
                        cond = sVar5 == 4;
                    }

                    if (cond)
                    {
                        DAT_8008d260 = 7;
                        if (DAT_8008d25c == 0)
                        {
                            DAT_8008d260 = 3;
                        }

                        // LAB_80022df4
                        bVar4 = true;
                        break;
                    }

                    DAT_8008d260 = 0;
                    iVar10 = (DAT_8008d25c == 0 ? 1 : 0) << 1;
                    break;
                }

                case 3:
                    iVar9 = FUN_80021e94(0);
                    if (iVar9 == 4)
                    {
                        unaff_s3 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff1cAddress));
                        DAT_8008d260 = 4;
                    }
                    else
                    {
                        DAT_8008d260 = 7;
                    }

                    bVar4 = false;
                    break;

                case 4:
                    unaff_s3 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff1cAddress));
                    if ((uint)(DAT_8008d254 - 1) < 2 || (PadInput.g_PadNewlyPressed[0] & 0x40) != 0)
                    {
                        DAT_8008d260 = 0;
                        iVar10 = 2;
                    }
                    else if ((PadInput.g_PadNewlyPressed[0] & 0x20) != 0)
                    {
                        DAT_8008d260 = 5;
                        bVar4 = false;
                        break;
                    }

                    // LAB_8002329c
                    bVar4 = false;
                    break;

                case 5:
                    iVar9 = FUN_800220f4(0);
                    DAT_8008d260 = 6;
                    if (iVar9 == 0)
                    {
                        DAT_8008d260 = 10;
                    }

                    bVar4 = false;
                    break;

                case 6:
                    unaff_s3 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff1cAddress));
                    if ((uint)(DAT_8008d254 - 1) < 2)
                    {
                        DAT_8008d260 = 0;
                        iVar10 = 2;
                        bVar4 = false;
                        break;
                    }

                    bVar4 = false;
                    if ((PadInput.g_PadNewlyPressed[0] & 0x40) != 0)
                    {
                        DAT_8008d260 = 0;
                        iVar10 = 2;
                        bVar4 = false;
                    }

                    break;

                case 7:
                    iVar9 = FUN_80022134(0);
                    if (DAT_8008d25c == 0)
                    {
                        DAT_8008d260 = 8;
                        if (iVar9 == 0)
                        {
                            DAT_8008d260 = 0xc;
                        }
                    }
                    else
                    {
                        DAT_8008d260 = 0xe;
                        if (iVar9 != 0)
                        {
                            DAT_8008d260 = 0;
                            iVar10 = 0;
                            // LAB_8002329c
                            bVar4 = false;
                            break;
                        }
                    }

                    // LAB_80022df4
                    bVar4 = true;
                    break;

                case 8:
                    iVar9 = FUN_80022084(0);
                    DAT_8008d260 = 10;
                    if (iVar9 == 0)
                    {
                        DAT_8008d260 = 9;
                    }

                    // LAB_80022df4
                    bVar4 = true;
                    break;

                case 9:
                    unaff_s3 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff24Address));
                    if ((uint)(DAT_8008d254 - 1) < 2)
                    {
                        DAT_8008d260 = 0;
                        iVar10 = 2;
                        bVar4 = false;
                        break;
                    }

                    bVar4 = false;
                    if ((PadInput.g_PadNewlyPressed[0] & 0x40) != 0)
                    {
                        DAT_8008d260 = 0;
                        iVar10 = 2;
                        bVar4 = false;
                    }

                    break;

                case 10:
                    iVar9 = FUN_80022198(0);
                    if (iVar9 == 0)
                    {
                        DAT_8008d260 = 0xb;
                        bVar4 = true;
                    }
                    else
                    {
                        DAT_8008d260 = 0xd;
                        bVar4 = true;
                    }

                    break;

                case 0xb:
                    unaff_s3 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff0cAddress));

                    // The inner switch has cases 0, 1..3, 4..6 and 0xD; 7..0xC and anything else
                    // fall to switchD_80022f2c_caseD_7, which is the bare increment below. The three
                    // live arms all converge on LAB_80022f78, the single FUN_80022758 call, which is
                    // why one `doCall` flag reproduces them without triplicating the call.
                    doCall = false;
                    iVar7 = 0;
                    puVar6 = 0;
                    if (DAT_8008d268 == 0)
                    {
                        iVar7 = 0;
                        puVar6 = Dat801ff018Address;
                        doCall = true;
                    }
                    else if (DAT_8008d268 >= 1 && DAT_8008d268 <= 3)
                    {
                        puVar8 = Dat801ff1f8Address;
                        iVar9 = DAT_8008d268 << 3;
                        iVar7 = DAT_8008d268;
                        puVar6 = puVar8 + iVar9;
                        doCall = true;
                    }
                    else if (DAT_8008d268 >= 4 && DAT_8008d268 <= 6)
                    {
                        puVar8 = Dat801ff1d8Address;
                        iVar9 = DAT_8008d268 << 4;
                        iVar7 = DAT_8008d268;
                        puVar6 = puVar8 + iVar9;
                        doCall = true;
                    }
                    else if (DAT_8008d268 == 0xd)
                    {
                        DAT_8008d260 = 0;
                        iVar10 = 1;
                    }

                    if (doCall)
                    {
                        iVar9 = FUN_80022758(0, iVar7, puVar6);
                        if (iVar9 != 0x80)
                        {
                            DAT_8008d260 = 0xd;
                        }
                    }

                    // switchD_80022f2c_caseD_7 then switchD_8002317c_default, in that order.
                    bVar4 = false;
                    DAT_8008d268 = (short)(DAT_8008d268 + 1);
                    break;

                case 0xc:
                    unaff_s3 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff0cAddress));

                    // ONE `jal`, NOT TWO. find-cross-references reports exactly two call sites to
                    // FUN_80022758 inside this function, at 0x80022F78 and 0x80023084, so both arms
                    // below reach the SAME instruction: the image computes the base, the record id
                    // and the offset in each arm and then falls into the shared LAB_80023084. The
                    // `doCall` flag reproduces that convergence instead of spelling the call twice.
                    doCall = false;
                    iVar7 = 0;
                    puVar6 = 0;
                    iVar9 = 0;
                    if (DAT_8008d258 == 0)
                    {
                        iVar9 = DAT_8008d268;
                        if (-1 < iVar9)
                        {
                            if (iVar9 < 3)
                            {
                                puVar6 = Dat801ff200Address;
                                iVar7 = iVar9 + 1;
                                iVar9 = iVar9 << 3;
                                doCall = true;
                            }
                            else if (iVar9 == 9)
                            {
                                // LAB_800230a0
                                DAT_8008d260 = 0;
                                iVar10 = 1;
                            }
                        }
                    }
                    else
                    {
                        iVar9 = DAT_8008d268;
                        if (-1 < iVar9)
                        {
                            if (iVar9 < 3)
                            {
                                puVar6 = Dat801ff218Address;
                                iVar7 = iVar9 + 4;
                                iVar9 = iVar9 << 4;
                                doCall = true;
                            }
                            else if (iVar9 == 3)
                            {
                                // LAB_800230a0
                                DAT_8008d260 = 0;
                                iVar10 = 1;
                            }
                        }
                    }

                    if (doCall)
                    {
                        // LAB_80023084
                        iVar9 = FUN_80022758(0, iVar7, iVar9 + puVar6);
                        if (iVar9 != 0x80)
                        {
                            DAT_8008d260 = 0xd;
                        }
                    }

                    DAT_8008d268 = (short)(DAT_8008d268 + 1);
                    bVar4 = false;
                    break;

                case 0xd:
                    DAT_8008d268 = 0;
                    unaff_s3 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff2cAddress));
                    if ((uint)(DAT_8008d254 - 1) < 2)
                    {
                        DAT_8008d260 = 0;
                        iVar10 = 2;
                        bVar4 = false;
                        break;
                    }

                    bVar4 = false;
                    if ((PadInput.g_PadNewlyPressed[0] & 0x40) != 0)
                    {
                        DAT_8008d260 = 0;
                        iVar10 = 2;
                        bVar4 = false;
                    }

                    break;

                case 0xe:
                    bVar4 = true;
                    if (DAT_8008d268 >= 0 && DAT_8008d268 <= 2)
                    {
                        iVar9 = FUN_80022870(0, DAT_8008d268 + 1, Local_a8Address);
                        if (iVar9 == 0x80)
                        {
                            iVar9 = DAT_8008d268;
                            local_a8 = (uint)MipsMemory.ReadI32(RAM_local_a8, 0);
                            local_a4 = (uint)MipsMemory.ReadI32(RAM_local_a8, 4);

                            // The original spells each of these two stores as an swl/swr PAIR
                            // (`swl rt,3(base)` + `swr rt,0(base)`), which Ghidra prints as the
                            // mask-and-or on 0x801FF203 + i*8 followed by the plain word store on
                            // 0x801FF200 + i*8. Both halves of a pair store the SAME word to the
                            // SAME base, so the pair is one 32-bit store — and PsxRam.WriteI32
                            // writes those four bytes at any alignment, which is exactly what the
                            // pair does. Two pairs in, two stores out; nothing is dropped.
                            PsxRam.WriteI32(Dat801ff200Address + iVar9 * 8, (int)local_a8);
                            PsxRam.WriteI32(Dat801ff204Address + iVar9 * 8, (int)local_a4);
                        }
                        else
                        {
                            // LAB_80023264
                            DAT_8008d260 = 0xf;
                        }
                    }
                    else if (DAT_8008d268 >= 3 && DAT_8008d268 <= 5)
                    {
                        iVar9 = FUN_80022870(0, DAT_8008d268 + 1, Local_a8Address);
                        if (iVar9 != 0x80)
                        {
                            // LAB_80023264
                            DAT_8008d260 = 0xf;
                        }
                        else
                        {
                            iVar9 = DAT_8008d268;
                            local_a8 = (uint)MipsMemory.ReadI32(RAM_local_a8, 0);
                            local_a4 = (uint)MipsMemory.ReadI32(RAM_local_a8, 4);
                            local_a0 = MipsMemory.ReadI32(RAM_local_a8, 8);
                            local_9c = MipsMemory.ReadI32(RAM_local_a8, 12);
                            PsxRam.WriteI32(Dat801ff1e8Address + iVar9 * 16, (int)local_a8);
                            PsxRam.WriteI32(Dat801ff1e8Address + 4 + iVar9 * 16, (int)local_a4);
                            PsxRam.WriteI32(Dat801ff1e8Address + 8 + iVar9 * 16, local_a0);
                            PsxRam.WriteI32(Dat801ff1e8Address + 12 + iVar9 * 16, local_9c);
                        }
                    }
                    else if (DAT_8008d268 == 6)
                    {
                        DAT_8008d260 = 0;
                        iVar10 = 1;
                        bVar4 = false;
                    }

                    // switchD_8002317c_default
                    DAT_8008d268 = (short)(DAT_8008d268 + 1);
                    break;

                case 0xf:
                    DAT_8008d268 = 0;
                    iVar10 = 0;
                    // LAB_8002329c
                    bVar4 = false;
                    break;

                default:
                    // switchD_80022b68_caseD_2 — states 2 and 0x10 and anything the jump table does
                    // not cover. bVar4 is left as it was, which is the original's own behaviour and
                    // the reason a state 2 reached with bVar4 set would spin.
                    break;
            }

            // LAB_800232a4
            if (!bVar4)
            {
                SetSemiTrans(DAT_8008d5f8, 0, 1);
                AddPrim(unaff_s3 * 4 + 0x70 + PsxRam.ReadI32(Dat8008d420Address), Dat8008d5f8Address);
                return iVar10;
            }
        }
        while (true);
    }

    // GHIDRA: FUN_80023314 @ 0x80023314 (VS.EXE)
    // 1404 bytes, 175 decompiled lines, ten callees. Its two references are UNCONDITIONAL_CALLs from
    // 0x8002A228 and 0x8002CEC4 — plain jal targets inside the undisassembled stretch, NOT an
    // address-taken entry point: nothing in the image stores a pointer to it, and Ghidra reports a
    // caller count of 0 only because it has not made those two sites into functions yet.
    //
    // THE CUT-DOWN DRIVER. Same state word, same numbering, same do/while, and the states that
    // exist here behave as they do in FUN_80022ab0. The differences, stated because they are the
    // whole point of there being two:
    //   * state 0 goes straight to 3 — no DAT_8008d25c fork, so this driver is SAVE-side only;
    //   * state 2 EXISTS here (it draws PTR_DAT_8007ff18 and waits for the back button) where
    //     FUN_80022ab0 leaves it to the default;
    //   * states 6, 9 and 0xD share ONE tail (LAB_800237AC) that differs only in which descriptor
    //     it draws — 0x8007FF1C, 0x8007FF24, 0x8007FF2C;
    //   * state 0xC writes RECORD 0 ONLY, on pass 0, and then idles until pass 7;
    //   * there is no state 0xE and no state 0xF: this driver never loads.
    //
    // STATE 3 DOES NOT TOUCH bVar1. It jumps straight to the tail test (LAB_80023828), so the flag
    // carries over from whichever state ran before — and the only states that reach 3 set it true,
    // so state 3 always continues. That is transliterated as written rather than "fixed" by adding
    // the assignment the other driver has.
    //
    // The same `unaff_s2` DEVIATION as FUN_80022ab0 applies and for the same reason.
    internal static int FUN_80023314()
    {
        bool bVar1;
        short sVar2;
        short uVar3;
        int iVar4;
        int iVar5;
        int puVar6;
        int puVar7;
        int uVar8;

        bool doCall;

        // DEVIATION: see FUN_80022ab0 — the original leaves s2 holding a previous pass's value.
        int unaff_s2 = 0;

        bVar1 = false;
        uVar8 = 0;
        sVar2 = 0;
        DAT_8008d254 = (short)FUN_80022014(0);
        if (DAT_8008d254 == 2)
        {
            DAT_8008d264 = 1;
            DAT_8008d260 = 1;
        }

        do
        {
            if (DAT_8008d264 == 1)
            {
                DAT_8008d264 = 0;
                sVar2 = (short)FUN_80021e94(0);
            }

            switch (DAT_8008d260)
            {
                case 0:
                    DAT_8008d260 = 3;
                    DAT_8008d268 = 0;
                    bVar1 = true;
                    break;

                case 1:
                    DAT_8008d268 = 0;
                    if ((sVar2 == 0) || (sVar2 == 4))
                    {
                        DAT_8008d260 = 3;
                        // LAB_80023610
                        bVar1 = true;
                        break;
                    }

                    // LAB_8002381c
                    uVar8 = 2;
                    bVar1 = false;
                    break;

                case 2:
                    unaff_s2 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff18Address));
                    if ((PadInput.g_PadNewlyPressed[0] & 0x40) != 0)
                    {
                        // LAB_80023818
                        DAT_8008d260 = 0;
                        uVar8 = 2;
                    }

                    // LAB_80023820
                    bVar1 = false;
                    break;

                case 3:
                    iVar4 = FUN_80021e94(0);
                    if (iVar4 == 4)
                    {
                        unaff_s2 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff1cAddress));
                        DAT_8008d260 = 4;
                    }
                    else
                    {
                        DAT_8008d260 = 7;
                    }

                    // goto LAB_80023828 — bVar1 is NOT touched here. See the header note.
                    goto LAB_80023828;

                case 4:
                    unaff_s2 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff1cAddress));
                    if ((uint)(DAT_8008d254 - 1) < 2 || (PadInput.g_PadNewlyPressed[0] & 0x40) != 0)
                    {
                        // LAB_80023818
                        DAT_8008d260 = 0;
                        uVar8 = 2;
                        bVar1 = false;
                        break;
                    }

                    uVar3 = 5;
                    if ((PadInput.g_PadNewlyPressed[0] & 0x20) == 0)
                    {
                        // LAB_80023820
                        bVar1 = false;
                        break;
                    }

                    // LAB_80023578
                    bVar1 = false;
                    DAT_8008d260 = uVar3;
                    break;

                case 5:
                    iVar4 = FUN_800220f4(0);
                    uVar3 = 6;
                    if (iVar4 == 0)
                    {
                        uVar3 = 10;
                    }

                    // LAB_80023578
                    bVar1 = false;
                    DAT_8008d260 = uVar3;
                    break;

                case 6:
                    puVar6 = PsxRam.ReadI32(PtrDat8007ff1cAddress);
                    goto LAB_800237ac;

                case 7:
                    iVar4 = FUN_80022134(0);
                    DAT_8008d260 = 8;
                    if (iVar4 == 0)
                    {
                        DAT_8008d260 = 0xc;
                    }

                    // LAB_80023610
                    bVar1 = true;
                    break;

                case 8:
                    iVar4 = FUN_80022084(0);
                    DAT_8008d260 = 10;
                    if (iVar4 == 0)
                    {
                        DAT_8008d260 = 9;
                    }

                    // LAB_80023610
                    bVar1 = true;
                    break;

                case 9:
                    puVar6 = PsxRam.ReadI32(PtrDat8007ff24Address);
                    goto LAB_800237ac;

                case 10:
                    iVar4 = FUN_80022198(0);
                    DAT_8008d260 = 0xd;
                    if (iVar4 == 0)
                    {
                        DAT_8008d260 = 0xb;
                    }

                    // LAB_80023610
                    bVar1 = true;
                    break;

                case 0xb:
                    unaff_s2 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff0cAddress));

                    // Same inner shape as FUN_80022ab0's state 0xB, and the same single call site
                    // (LAB_80023754) that all three live arms converge on -- AND SO DOES STATE 0xC.
                    // find-cross-references reports exactly ONE call site to FUN_80022758 inside
                    // this function, at 0x80023754, so state 0xC's record-0 write is not a second
                    // call: it is a `goto` into this one. The label below is that instruction and
                    // state 0xC jumps to it, which is why the two states share this tail verbatim.
                    doCall = false;
                    iVar5 = 0;
                    puVar7 = 0;
                    if (DAT_8008d268 == 0)
                    {
                        // switchD_80023694_caseD_0
                        iVar5 = 0;
                        puVar7 = Dat801ff018Address;
                        doCall = true;
                    }
                    else if (DAT_8008d268 >= 1 && DAT_8008d268 <= 3)
                    {
                        puVar6 = Dat801ff1f8Address;
                        iVar4 = DAT_8008d268 << 3;
                        iVar5 = DAT_8008d268;
                        puVar7 = puVar6 + iVar4;
                        doCall = true;
                    }
                    else if (DAT_8008d268 >= 4 && DAT_8008d268 <= 6)
                    {
                        puVar6 = Dat801ff1d8Address;
                        iVar4 = DAT_8008d268 << 4;
                        iVar5 = DAT_8008d268;
                        puVar7 = puVar6 + iVar4;
                        doCall = true;
                    }
                    else if (DAT_8008d268 == 0xd)
                    {
                        // switchD_80023694_caseD_d
                        DAT_8008d260 = 0;
                        uVar8 = 1;
                    }

                LAB_80023754:
                    if (doCall)
                    {
                        iVar4 = FUN_80022758(0, iVar5, puVar7);
                        if (iVar4 != 0x80)
                        {
                            DAT_8008d260 = 0xd;
                        }
                    }

                    // switchD_80023694_caseD_7 — increment first here, unlike FUN_80022ab0.
                    DAT_8008d268 = (short)(DAT_8008d268 + 1);
                    bVar1 = false;
                    break;

                case 0xc:
                    unaff_s2 = CallFun80052db4(PsxRam.ReadI32(PtrDat8007ff0cAddress));
                    doCall = false;
                    iVar5 = 0;
                    puVar7 = 0;
                    if (DAT_8008d268 != 0)
                    {
                        if (DAT_8008d268 == 7)
                        {
                            // switchD_80023694_caseD_d
                            DAT_8008d260 = 0;
                            uVar8 = 1;
                        }

                        // goto switchD_80023694_caseD_7 — with doCall clear, LAB_80023754 falls
                        // straight through to it.
                    }
                    else
                    {
                        // switchD_80023694_caseD_0
                        iVar5 = 0;
                        puVar7 = Dat801ff018Address;
                        doCall = true;
                    }

                    goto LAB_80023754;

                case 0xd:
                    DAT_8008d268 = 0;
                    puVar6 = PsxRam.ReadI32(PtrDat8007ff2cAddress);

                LAB_800237ac:
                    unaff_s2 = CallFun80052db4(puVar6);
                    if ((uint)(DAT_8008d254 - 1) < 2)
                    {
                        // LAB_80023818
                        DAT_8008d260 = 0;
                        uVar8 = 2;
                        bVar1 = false;
                        break;
                    }

                    bVar1 = false;
                    if ((PadInput.g_PadNewlyPressed[0] & 0x40) != 0)
                    {
                        // LAB_80023818
                        DAT_8008d260 = 0;
                        uVar8 = 2;
                        bVar1 = false;
                    }

                    break;

                default:
                    // switchD_800233bc_caseD_e — the states this driver has no body for.
                    break;
            }

        LAB_80023828:
            if (!bVar1)
            {
                SetSemiTrans(DAT_8008d5f8, 0, 1);
                AddPrim(unaff_s2 * 4 + 0x70 + PsxRam.ReadI32(Dat8008d420Address), Dat8008d5f8Address);
                return uVar8;
            }
        }
        while (true);
    }

    // =====================================================================================
    // THE THREE ENTRY MODES OF FUN_80022ab0
    // =====================================================================================
    // All three are `addiu sp,-0x18 / sw ra / <two or one sh> / jal FUN_80022ab0 / lw ra /
    // addiu sp,0x18 / jr ra`, read byte for byte at 0x80022A34..0x80022AAF. NOTHING TOUCHES v0
    // BETWEEN THE `jal` AND THE `jr ra`, so FUN_80022ab0's return value propagates to their own
    // caller unchanged. Ghidra types them `void` because it cannot see a consumer — the six call
    // sites are all inside the undisassembled stretch — but the register traffic is unambiguous,
    // so they are typed `int` here and the value is passed through. That is the only inference in
    // this group and it is settled by the instruction stream, not by a guess about intent.

    // GHIDRA: FUN_80022a34 @ 0x80022A34 (VS.EXE)
    // Forty bytes. Mode SAVE-GROUP-A: DAT_8008d258 = 0, DAT_8008d25c = 0, so FUN_80022ab0 takes its
    // save fork and state 0xC writes records 1..3.
    // One caller, LAB_8002B948 @ 0x8002B948.
    internal static int FUN_80022a34()
    {
        DAT_8008d258 = 0;
        DAT_8008d25c = 0;
        return FUN_80022ab0();
    }

    // GHIDRA: FUN_80022a5c @ 0x80022A5C (VS.EXE)
    // Forty-four bytes — four more than its neighbour, which is the `ori v0,zero,1` that supplies
    // the 1. Mode SAVE-GROUP-B: DAT_8008d258 = 1, DAT_8008d25c = 0, so state 0xC writes records 4..6.
    // One caller, LAB_8002F50C @ 0x8002F50C.
    internal static int FUN_80022a5c()
    {
        DAT_8008d258 = 1;
        DAT_8008d25c = 0;
        return FUN_80022ab0();
    }

    // GHIDRA: FUN_80022a88 @ 0x80022A88 (VS.EXE)
    // Forty bytes. Mode LOAD: DAT_8008d25c = 1 and DAT_8008d258 IS LEFT ALONE — the only one of the
    // three that does not set both, so the group selector keeps whatever the previous mode left.
    // That is the original's, not an omission in this transliteration; the byte-for-byte read at
    // 0x80022A88 has exactly one `sh` (`sh v0,0x160(gp)`).
    // Four callers: 0x800294A4, FUN_80031dc8 @ 0x80031DE4, LAB_8002B29C, LAB_8002EDB4.
    internal static int FUN_80022a88()
    {
        DAT_8008d25c = 1;
        return FUN_80022ab0();
    }
}
