using PsxSdkMonogame;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.VS_EXE;

// The allocator VS.EXE runs on, and the one way out of the overlay.
//
// =====================================================================================
// THE ALLOCATOR QUESTION, CLOSED — AND THE ANSWER IS NOT THE ONE THE SLICE WAS HANDED
// =====================================================================================
//
// Two earlier reports contradicted each other. One announced "InitHeap(0x10000, 0x10000) from
// libapi"; the other said VS.EXE links no heap at all, neither InitHeap nor malloc. The brief that
// opened this slice split the difference and concluded that because `main` calls
// FUN_80062f54(0x10000, 0x10000) — a function inside the program rather than a BIOS vector —
// "VS.EXE has its own allocator", to be transliterated verbatim if its algorithm differed from the
// SDK's.
//
// It does not have its own allocator. The three functions in this slice are the PsyQ heap, the
// same object code TITLE.EXE links, and that is measured, not inferred.
//
// THE MEASUREMENT. Both overlays are PS-EXEs with t_addr = 0x80020000 and t_size = 0xE5800, so a
// PSX address maps to a file offset by `0x800 + addr - 0x80020000`. Reading the bytes out of
// data/TITLE.EXE and data/VS.EXE at the four addresses below and comparing them word by word —
// treating a word as "relocated" only when the two differ in a j/jal 26-bit target, or in the
// 16-bit immediate of an lui/lw/sw/addiu while opcode, rs and rt are identical — gives:
//
//   TITLE.EXE                       VS.EXE                       words   non-relocation diffs
//   InitHeap      @ 0x80059160      FUN_80062f54 @ 0x80062F54       16                      0
//   malloc        @ 0x800591A0      FUN_80062f94 @ 0x80062F94      141                      0
//   free          @ 0x800593D4      FUN_800631c8 @ 0x800631C8        5                      0
//   _ExpAllocArea @ 0x80058EC4      FUN_80062cb8 @ 0x80062CB8       61                      0
//
// Zero. Every differing word is an address the linker patched. The relocation delta is a constant
// +0x9DF4 across all four, which is why the three sit at exactly the same distances from each
// other in both images: InitHeap is 64 bytes, malloc 0x234 bytes, and free begins on malloc's last
// byte + 1 in both programs. Ghidra names them in TITLE.EXE and leaves them raw in VS.EXE; that is
// a difference in how much markup each program received, not a difference in the code.
//
// TITLE.EXE's Ghidra splits malloc into MALLOC_OBJ_* fragments and reports it as 464 bytes.
// VS.EXE's Ghidra recovers the whole 564-byte function in one piece, so the cleaner reading of the
// PsyQ allocator in this project is the VS.EXE one. It is the classic four-byte-header scheme:
// header = payload size rounded to 4 with bit 0 as the free flag, 0xFFFFFFFE as the end-of-heap
// sentinel, first fit with split-on-oversize and merge-with-next-if-free, and _ExpAllocArea as the
// sbrk that walks the head pointer forward until it meets the limit.
//
// WHAT FOLLOWS FROM THAT. Rule 13 of the mandate — do not transliterate PSX SDK routines as if they
// belonged to the game runtime — and the instruction that the SDK is never duplicated both point
// the same way, so these three do NOT get a second implementation here. They go to
// PsxSdkMonogame's LibApi, which already routes InitHeap/malloc/free to PsxHeap and already carries
// its own `// GHIDRA:` annotations naming the TITLE.EXE addresses. PsxHeap is deliberately an
// observable-contract stand-in rather than a transliteration — its block header is 8 bytes, not 4 —
// and the addresses it hands out are therefore not the ones the console hands out. That difference
// was accepted for TITLE.EXE and it is accepted here for the same reason: no VS.EXE call site reads
// the value of a returned pointer for anything but a null/-1 test and a store. The one thing that
// does matter, and that a hand-rolled heap here would have had to duplicate, is that PsxHeap
// registers its span with LibGpu.RamRegion — without which a primitive living in a malloc'd pool
// has no address AddPrim can splice into the ordering table, and it silently never draws.
//
// WHY THE RAW NAMES SURVIVE ANYWAY. The wrappers below keep FUN_80062f54 / FUN_80062f94 /
// FUN_800631c8 because that is what Ghidra carries on VS.EXE and what the sibling slices already
// call. They are one C# method per original function, not an aggregation, and each forwards to the
// single SDK routine it is a relocation of. Nothing is merged.
//
// =====================================================================================
// THE HEAP'S OWN GLOBALS IN VS.EXE, for whoever writes VS_EXE_exe.ResolveAddress
// =====================================================================================
//
// These are the PsyQ heap's statics as the VS.EXE linker placed them. Their state lives inside
// PsxHeap, so no field is declared for them here — declaring storage nothing reads would be
// inventing state. They are recorded because a resolver that has to answer for .bss needs to know
// the range is spoken for, and because they are how the four functions above were tied together.
//
//   0x8008D2E0  head pointer, the next unbroken byte      (TITLE.EXE 0x8008339C)
//   0x8008D2E8  remaining size, set to size - 4 by init   (TITLE.EXE 0x800833A4)
//   0x8008D2F0  limit, base + (size & ~3) + 4             (TITLE.EXE 0x800833AC)
//   0x8008D2F8  "heap has been broken into once" flag     (TITLE.EXE 0x800833B4)
//   0x800B0D6C  the cursor malloc walks the block list on (TITLE.EXE 0x800A6678)
//   0x800B0EB0  the first block, latched on first malloc  (TITLE.EXE 0x800A67B8)
internal static class Heap
{
    // GHIDRA: FUN_80062f54 @ 0x80062F54 (VS.EXE)
    // This is the PsyQ InitHeap, byte-identical to TITLE.EXE's InitHeap @ 0x80059160 modulo
    // relocation; the C# name would come from there, but the Ghidra symbol on VS.EXE is still raw
    // and the annotation names what Ghidra carries. Sixteen words:
    //
    //     head  = param_1;  *param_1 = 0;
    //     limit = param_1 + (param_2 & 0xfffffffc) + 4;
    //     size  = param_2 - 4;
    //     broken = 0;
    //
    // TWO CALL SITES, and the second overrides the first.
    //
    //   start @ 0x80072FD8 arms the SNMAIN heap at _end + 4 = 0x800C3DD8.
    //   main  @ 0x800621A4 arms 0x00010000 with 0x10000 bytes — the 64 KB immediately below the
    //          0x80020000 load address, the same window TITLE.EXE uses. Nothing allocates between
    //          the two calls, so the crt0 heap is armed and discarded unused.
    //
    // THE ARITY AT THE crt0 SITE IS SETTLED, and settled here because VS_EXE_exe.start left it
    // BLOCKED. Ghidra prints that call with one argument, but the instruction stream shows two:
    // $a1 is computed at 0x80072FA8-0x80072FAC (`subu a1, v0, v1` then `subu a1, a1, a0`, the same
    // formula stored to 0x800858C0 one instruction later) and nothing between there and the `jal`
    // at 0x80072FD8 touches $a1 — the intervening writes are to $at, $a0, $ra, $gp and $fp. The
    // delay slot is `addiu a0, a0, 4`, which is where _end + 4 comes from. So the prototype is
    // two-argument at both sites; the decompiler simply lost the first one.
    //
    // PARTIAL: the crt0 size is a runtime value — (stack top - 8) - DAT_800858DC - 0xC3DD4 — and is
    // not statically known, so it is not reproduced. VS_EXE_exe.start currently passes 0 for it.
    // PsxHeap treats a size below one header plus one payload as "disarm", which is what a 0
    // produces, and main re-arms four statements later before anything allocates, so the observable
    // outcome is the same. It is left exactly as the sibling slice wrote it: correcting a call site
    // in another file is not this slice's to do, and the note above is the evidence for doing it.
    internal static void FUN_80062f54(int param_1, int param_2)
    {
        InitHeap(param_1, param_2);
    }

    // GHIDRA: FUN_80062f94 @ 0x80062F94 (VS.EXE)
    // The PsyQ malloc, 564 bytes, byte-identical to TITLE.EXE's malloc @ 0x800591A0 modulo
    // relocation. Its two callers are the two the recon predicted from TITLE.EXE:
    //   FUN_80053330 @ 0x80053394 — CreateTask, allocating the 0x18-byte node plus the workspace;
    //   FUN_80060ecc @ 0x80060F38 — the primitive-pool allocator.
    //
    // The single callee, FUN_80062cb8 @ 0x80062CB8, is _ExpAllocArea: the sbrk that pushes the head
    // pointer forward, writes the 0xFFFFFFFE sentinel behind it, and refuses once the limit is
    // reached. It has no separate wrapper here because nothing outside malloc calls it — one
    // caller, two call sites, both inside malloc.
    //
    // RETURN VALUE, and rule 12 applies to it. The original returns 0 on failure, never -1, yet
    // CreateTask tests its result against 0xFFFFFFFF. That test can never fire, and a real
    // exhaustion falls through into CreateTask writing through a null pointer. That is the
    // original's behaviour and it is not corrected: LibApi.malloc likewise returns 0 on failure.
    internal static int FUN_80062f94(int param_1)
    {
        return malloc(param_1);
    }

    // GHIDRA: FUN_800631c8 @ 0x800631C8 (VS.EXE)
    // The PsyQ free, five words, byte-identical to TITLE.EXE's free @ 0x800593D4 modulo
    // relocation. The whole function is `*(uint *)(param_1 - 4) |= 1` — it sets the free bit in the
    // block header and returns. There is no coalescing on this side; malloc does the merging when
    // it next walks the list.
    //
    // Four call sites, all in the task system: FUN_8005354c @ 0x800535E8, FUN_80053628 @ 0x8005376C
    // and FUN_80053840 @ 0x80053918 — the delete/auto-delete paths — plus FUN_80060f88 @ 0x80060FC0
    // in the primitive-pool teardown.
    //
    // PARTIAL: the original computes the header address itself from the payload pointer, because on
    // the console the header is the four bytes in front of the block. PsxHeap owns its own block
    // layout, so LibApi.free takes the payload address and does that arithmetic on its own terms.
    // The `- 4` is therefore not written here; writing it would address PsxHeap's storage wrongly.
    internal static void FUN_800631c8(int param_1)
    {
        free(param_1);
    }

    // GHIDRA: DAT_801fff00 @ 0x801FFF00
    // The EXEC header scratch handed to LoadExec. Outside the 0x801FF000..0x801FF247 cross-overlay
    // block SharedHighRam models, and the two other overlays pass the same address.
    // PARTIAL: only the address reaches LoadExec; nothing in VS.EXE reads the contents.
    private const int DAT_801fff00 = unchecked((int)0x801FFF00);

    // GHIDRA: FUN_800620b0 @ 0x800620B0 (VS.EXE)
    // This is TITLE.EXE's ShutdownAndLoadExecutable @ 0x80058158 to the word — 33 words, zero
    // differences outside the nine relocated jal targets — so the C# name comes from there while the
    // annotation names the raw symbol Ghidra still carries on VS.EXE.
    //
    // THE ONLY WAY OUT OF THE OVERLAY. main is an infinite frame loop with no break and no return;
    // this is called from inside a task and never comes back.
    //
    // Six call sites, all in one mode dispatcher — the undefined function at 0x80031B5C, state 5 —
    // and five distinct targets. VS.EXE holds exactly five "cdrom:" strings and this is where all
    // five go:
    //     case 0        0x80031D50  "cdrom:\\DEMO.EXE;1"    @ 0x80020464
    //     case 1        0x80031D60  "cdrom:\\GAME.EXE;1"    @ 0x80020478
    //     case 4, 5     0x80031D70  "cdrom:\\SP.EXE;1"      @ 0x8002048C
    //     case 2, 6     0x80031D80  "cdrom:\\TITLE.EXE;1"   @ 0x8002049C
    //     case 3        0x80031D90  "cdrom:\\TITLE.EXE;1"   @ 0x8002049C
    //     case 8, 9     0x80031DA0  "cdrom:\\ENDING.EXE;1"  @ 0x800204B0
    // Case 7 does not leave: it sets DAT_8008D4F0 = 1 instead. The dispatcher is not this slice's.
    //
    // AGAINST THE TWO OTHER OVERLAYS, which the brief asked for explicitly:
    //   TITLE.EXE @ 0x80058158 — the same compiled function. Same eight calls, same order.
    //   SELECT.EXE @ 0x8003472C — a different build of the same source, and the differences are
    //     real: it opens with ShutdownMemoryCard and VSync(0), it calls CdFlush before
    //     StopCallback, and it runs PadStop before ResetGraph rather than after. VS.EXE has none of
    //     those three extra calls and keeps TITLE.EXE's ResetGraph-then-PadStop order.
    //   Call-site count differs from TITLE.EXE even though the body does not: TITLE.EXE reaches its
    //     copy from nine places (the frame loop, the title screen, a movie path and its own
    //     dispatcher), VS.EXE only from the six above.
    internal static void ShutdownAndLoadExecutable(string exeFileName)
    {
        StopRCnt(unchecked((long)0xf2000000));
        StopRCnt(unchecked((long)0xf2000001));
        StopRCnt(unchecked((long)0xf2000002));
        StopRCnt(unchecked((long)0xf2000003));
        ResetGraph(0);
        PadStop();
        StopCallback();
        _96_init();
        LoadExec(exeFileName, DAT_801fff00, 0);
    }

    // GHIDRA: LoadExec @ 0x8007AB80 (VS.EXE)
    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: A0(0x51) replaces the resident executable and transfers control permanently, so it
    // never returns to its caller — which is why every original call site is followed by
    // unreachable code. LibApi.LoadExec is a no-op, so the transfer is modelled at the game layer,
    // exactly as SLPS_003_55, MOVIE_EXE, TITLE_EXE and SELECT_EXE already do. This is the
    // per-overlay half of that pattern, not a fifth copy of an SDK routine: the SDK half is
    // LibApi.LoadExec and it is not reimplemented here.
    //
    // PARTIAL: nothing is wired behind the transfer. Four of the five targets — DEMO.EXE, GAME.EXE,
    // SP.EXE, ENDING.EXE — are not transliterated at all. The fifth, TITLE.EXE, IS transliterated
    // and is the one overlay VS.EXE can hand control back to, but wiring it is deliberately left
    // out of this file: this slice was instructed not to reach into DbzLegendsRemaster.TITLE_EXE,
    // so the hand-back belongs to whoever owns the dispatcher and PsxSdkBridges. The shape it takes
    // is already established — PsxSdkBridges.ActivateTitleExe() followed by the overlay's start.
    private static void LoadExec(string exeFileName, int param_2, int param_3)
    {
        _ = param_2;
        _ = param_3;

        // JUSTIFICATION: PSX hardware adaptation only
        // RELATION: see LibCd.WaitDiscLoad — the drive spends real time fetching the overlay, and
        // without it a held button carries straight into the next screen. It returns immediately
        // when the file is absent, which is the case for four of the five targets today.
        WaitDiscLoad(exeFileName);

        throw new LoadExecTransferException();
    }
}
