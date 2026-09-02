using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's animation-script VM — the globals SHARED across every opcode family.
//
// ExecuteAnimStreamBatch @ 0x80036768 dispatches through g_animStreamDispatchTable @ 0x800822F4 to
// fifty-one handler functions, split across AnimCmdTransform.cs, AnimCmdMesh.cs,
// AnimCmdAppearance.cs, AnimCmdControl.cs, AnimCmdEffects.cs and AnimCmdSound.cs. Those six families
// were transliterated in parallel and each redeclared its own copy of the globals the VM shares
// across families — twelve symbols, seven of them with C# types that disagreed file to file. On the
// console there is exactly ONE copy of each: one family writes it, another reads it, and the
// interpreter itself reads DAT_800b305a and g_meshStreamPtrBuffer. This file is the single
// declaration every AnimCmd*.cs file now points at.
//
// TWO DIFFERENT MODELS, BY WHAT THE SYMBOL ACTUALLY IS:
//
//   * DAT_800b305a lives in the .bss and is read and written directly — never indexed — so a single
//     C# field is faithful. It is NOT part of the 0x801Fxxxx workspace below.
//   * The other eleven symbols are PSX RAM inside the animation workspace at 0x801F2000, reached by
//     ADDRESS through PsxRam rather than by a managed array. That is deliberate, not a placeholder:
//     the VM writes several of them from one family and reads them from another, several of the
//     indices used against them are SIGNED shorts with no enforced bound (a fixed-size C# array
//     would silently truncate one of those in the negative direction), and by-address access is
//     already this port's convention for PSX memory reached through computed offsets — see
//     TITLE_EXE's task-scheduling table and VS_EXE/FileIo.cs's own buffers.
internal static class AnimVm
{
    // =====================================================================================
    // The workspace, 0x801F2000..0x801FAAAC and up — declared as ONE region so every address
    // below resolves through the same backing bytes, no matter which family reads or writes it.
    // =====================================================================================

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: models PSX RAM 0x801F2000..0x801FAFFF as real bytes so pointer arithmetic done on
    // PSX addresses lands where the console lands, and so every AnimCmd* handler that computes an
    // address inside this range — whichever family owns the handler — reads and writes the same
    // storage as every other one.
    //
    // THE EXTENT IS CLOSED, and by the game itself. AnimCmd_RenderEntryGroup @ 0x800373A0 clears
    // this workspace with `bzero(&DAT_801f2000, 0x8c48)`, so the block runs 0x801F2000..0x801FAC47
    // and not one byte further. A first pass here carried 0x9000 as a margin with the bound declared
    // open; the margin was never needed — the clear call had already been found by the render family
    // and was sitting in this very comment. Guessing a bound while the evidence for it is in the
    // file is how a PARTIAL outlives its cause.
    //
    // This span CONTAINS the 0x801FAA64..0x801FAC3F block the effects family documents from the
    // GAME.EXE analysis in docs/structure-ch-bin-files.history.md. One region owns it, here; nothing
    // else may declare a second one over the same addresses. Two RamRegion rows for one PSX address
    // is the defect that cost this port a heap re-arm earlier: RamRegion matches on reference, so a
    // second array adds a row rather than replacing one, and resolution can elect the wrong storage.
    private const int WorkspaceBase = unchecked((int)0x801F2000);
    private const int WorkspaceSize = 0x8C48; // 0x801F2000..0x801FAC47, from the bzero above

    // JUSTIFICATION: PSX hardware adaptation only
    // A field initializer runs from this class's static constructor on first access to AnimVm,
    // i.e. before any AnimCmd* handler can read or write through the addresses below.
    internal static readonly byte[] RAM_801f2000 = LibGpu.RamRegion(WorkspaceBase, WorkspaceSize);

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: this class's row for the overlay address resolver, in the shape TITLE_EXE_exe's
    // per-module `Resolve` chain already uses. Not installed anywhere yet: PsxSdkBridges has no
    // VS.EXE row and VS_EXE_exe has no ResolveAddress for it to install into, so every read and
    // write through the constants below currently resolves to nothing and answers zero. That gap is
    // real and is not this file's to close.
    internal static (byte[] Buffer, int Offset)? Resolve(int address)
    {
        if (address >= WorkspaceBase && address < WorkspaceBase + WorkspaceSize)
        {
            return (RAM_801f2000, address - WorkspaceBase);
        }

        return null;
    }

    // =====================================================================================
    // The twelve globals. Addresses only — see the model note above for why nothing here
    // allocates a second array. Comments merged from every family that had written proof for the
    // symbol; where two files read the evidence differently, both readings are kept below.
    // =====================================================================================

    // GHIDRA: DAT_800b305a @ 0x800B305A (VS.EXE)
    // The gate every handler in the VM opens with, `if ((DAT_800b305a & 1) == 0)`. Ghidra types it
    // undefined2. 98 references program-wide; written only by FUN_80062a1c @ 0x80062A1C and
    // FUN_80062b5c @ 0x80062B5C, neither of which is any one opcode family's own code. Bit 0 is
    // closed as the VM's global suspend flag: every handler tests only bit 0 and skips its side
    // effects when it is set, while still advancing the stream pointer.
    //
    // PARTIAL: what the other bits mean, and what exactly sets bit 0, is not closed by any of the
    // six families' evidence. AnimCmdEffects.cs adds one lead worth keeping: FUN_80062b5c toggles
    // bit 2 off a pad test, sets bit 0 from it, latches bit 1, forces the word to 0 when
    // DAT_8008d4f0 != 1, and — when bit 2 is up — submits five primitives from 0x800B2F74 through
    // AddPrim, which reads as the freeze the pause overlay drives. "Pause" is a reading, not a
    // closed symbol.
    //
    // NOT part of the 0x801Fxxxx workspace below: it is a single halfword of .bss, addressed
    // directly rather than indexed, so it is modelled as one C# field instead of an address
    // constant.
    internal static ushort DAT_800b305a;

    // GHIDRA: DAT_801f2000 @ 0x801F2000 (VS.EXE)
    // The base of the render workspace and, at the same time, the first of three 16-slot operand
    // tables: the rotation-vector bank FUN_8003f2b0 hands out as `&DAT_801f2000 + (n & 0xf) * 8`,
    // sixteen slots of eight bytes.
    internal const int DAT_801f2000 = unchecked((int)0x801F2000);

    // GHIDRA: UNK_801f2080 @ 0x801F2080 (VS.EXE)
    // Operand table 2, the translation-vector bank FUN_8003f228 hands out as
    // `&UNK_801f2080 + (n & 0xf) * 8`, sixteen slots of eight bytes.
    internal const int UNK_801f2080 = unchecked((int)0x801F2080);

    // GHIDRA: DAT_801f2100 @ 0x801F2100 (VS.EXE)
    // Operand table 3, the scale-vector bank. Unlike the other two this one has no resolver
    // function: handlers compute `&DAT_801f2100 + (sel & 0xf) * 8` inline. AnimCmd_CulSet indexes
    // it directly, without a resolver either.
    internal const int DAT_801f2100 = unchecked((int)0x801F2100);

    // GHIDRA: DAT_801f2180 @ 0x801F2180 (VS.EXE)
    // Ghidra types it undefined2. TWO READINGS OF THE SAME BYTES, kept both:
    //   * Four SVECTOR-shaped vertices per primitive, 0x20 bytes: x,y,z at +0,+2,+4 and the
    //     primitive-kind byte in the first vertex's pad at +6, which is what FUN_8003f6c0 reads as
    //     `v3[-3].pad` to pick RotAverage4 or RotAverage3 (AnimCmdMesh's reading).
    //   * The transform record table base_culX/Y/Z/P edit, indexed as `&DAT_801f2180 + n * 0x10`
    //     through a short pointer — n * 0x20 bytes, i.e. the same 32-byte stride — one 32-byte
    //     record per mesh holding four 8-byte sub-records of four shorts each; the four base_cul*
    //     commands each write one short of every sub-record: X at +0, Y at +2, Z at +4, P at +6
    //     (AnimCmdTransform's and AnimCmdControl's reading). AnimCmdControl also notes the region is
    //     written by base_culX/Y/Z @ 0x8003B184-0x8003BA98, AnimCmd_ChEffSet @ 0x8003DCBC and
    //     RenderBattleScene3D @ 0x800358B8.
    // Both readings describe the same 32-byte-stride record; they were not reconciled further than
    // that because the sub-record consumer, FUN_8003f6c0, is outside every one of these slices.
    //
    // PARTIAL: the extent is not closed. 256 records is what parts_link (AnimCmdControl) and
    // FUN_8003f6c0's caller (AnimCmdMesh) both imply from their own index bounds, but no symbol
    // beyond it in Ghidra confirms the count.
    internal const int DAT_801f2180 = unchecked((int)0x801F2180);

    // GHIDRA: DAT_801f7180 @ 0x801F7180 (VS.EXE)
    // THE POLY_GT4 ARRAY, stride 0x34 = 52 bytes (POLY_GT4's size, the fourth entry of
    // PrimitivePools' g_PrimitiveSizeTable) everywhere it is indexed. AnimCmdAppearance closes the
    // count at 256 from 0x801FA580 (the OTZ array) - 0x801F7180 = 0x3400 = 0x34 * 256. It is the
    // primitive array the battle renderer draws from — referenced by RenderBattleScene3D
    // @ 0x800358B8 and by AnimCmd_RenderEntryGroup @ 0x800373A0 (`table_set`) — and every base
    // address the appearance family's ten handlers use lands on a named POLY_GT4 field:
    //   +0  = tag       pri_set walks whole primitives from here
    //   +4  = r0/g0/b0  rgb_set and rgb2_set, +16 / +28 / +40 for v1..v3
    //   +7  = code      rgb_set's two flag bits, rgb2_set's low two bits
    //   +8  = x0,y0     xy0123_set, +20 / +32 / +44 for v1..v3
    //   +12 = u0,v0     uv0123_set, +24 / +36 / +48 for v1..v3
    //   +14 = clut      tpclut_set, first of its two fields
    //   +26 = tpage     tpclut_set, second of its two fields
    internal const int DAT_801f7180 = unchecked((int)0x801F7180);

    // GHIDRA: g_animSharedVarTable @ 0x801FAA64 (VS.EXE)
    // The VM's shared variable file: a halfword array every family indirects through when a command
    // header sets its "this operand is a variable index" bit. 98 references program-wide.
    //
    // PARTIAL on the extent and on the signedness of the index — not fully closed, and the six
    // families' own evidence disagrees on how far to trust it:
    //   * AnimCmdSound closes the low end at sixteen halfwords (0x801FAA64..0x801FAA83): every
    //     index in that family is `(x & 0xf) * 2`, and Ghidra carries a separate label
    //     DAT_801faa84 @ 0x801FAA84 with six references of its own, so the table cannot run past
    //     it there. AnimCmdEffects's opcodes only ever reach the same sixteen slots the same way.
    //   * AnimCmdControl reads a wider index: 256 entries is the widest an opcode encoding in that
    //     family can express (if_set reaches it as a zero-extended byte), so that family used 256
    //     as its bound — noting that on the console, entries from index 0x24 up alias the
    //     neighbouring globals starting at DAT_801faaac @ 0x801FAAAC, an overlap this port does not
    //     model. Control also notes a second, SIGNED spelling of the index in some handlers
    //     (`(short)(char)(cmd >> 8)`) that would reach -128..-1, i.e. bytes belonging to
    //     g_renderFlushFlag and g_meshOffsetBuffer — no evidence in that slice shows a script
    //     actually using one.
    //   * AnimCmdTransform found the index passed as a signed short with no bound enforced by any
    //     handler in that family, and asserted no extent at all rather than guess one.
    // These are not reconciled into one number here; the widest binding evidence (Control's 256) is
    // the one to trust for a bound if one is needed, but by-address access means no bound is
    // enforced by this declaration either way.
    internal const int g_animSharedVarTable = unchecked((int)0x801FAA64);

    // GHIDRA: g_renderMetadataBuffer @ 0x801FA880 (VS.EXE)
    // Sixty-four words, one per overlaid render entry — every family's loop bounds at 0x40 and
    // indexes by `i * 4`. AnimCmdMesh closes the packing: AnimCmd_RenderEntryGroup fills each entry
    // as `entryIndex | (entryDword0.low16 << 8) | (runningPrimitiveIndex << 24)`. Byte +2 is the
    // entry's match tag — the id `pri_set`, `rgb_set`, base_cul* and friends compare a command's tag
    // byte against — and byte +3 is the entry's first primitive index. 0x801FA880 + 0x40*4 =
    // 0x801FA980, exactly where g_meshCountBuffer starts, closing the extent.
    internal const int g_renderMetadataBuffer = unchecked((int)0x801FA880);

    // GHIDRA: g_meshCountBuffer @ 0x801FA980 (VS.EXE)
    // One halfword per g_renderMetadataBuffer entry: how many primitives (or, in AnimCmdControl and
    // AnimCmdTransform's base_cul* reading, how many 32-byte transform records) that entry owns.
    // 0x801FA980 + 64*2 = 0x801FAA00, where g_meshStreamPtrBuffer starts.
    internal const int g_meshCountBuffer = unchecked((int)0x801FA980);

    // GHIDRA: g_meshStreamPtrBuffer @ 0x801FAA00 (VS.EXE)
    // Sixteen PSX addresses, one per mesh/stream slot — the array ExecuteAnimStreamBatch walks and
    // AnimCmd_RenderEntryGroup scans (`uVar22 < 0x10`) to pick a destination slot. Holds stream
    // pointers, so it is read and written as `int`s, never copied into a managed reference. Ends
    // exactly where g_meshOffsetBuffer begins, which is what fixes the count at sixteen.
    internal const int g_meshStreamPtrBuffer = unchecked((int)0x801FAA00);

    // GHIDRA: g_meshOffsetBuffer @ 0x801FAA40 (VS.EXE)
    // Sixteen halfwords, one per stream slot — the per-slot frame countdown
    // ExecuteAnimStreamBatch decrements after each stream runs. Setting it to 1 (end_set and
    // bit_chk both do) makes the next decrement hit the `uVar1 == 1` arm at 0x800368D8.
    // 0x801FAA40 + 16*2 = 0x801FAA60, the next symbol (g_renderFlushFlag).
    internal const int g_meshOffsetBuffer = unchecked((int)0x801FAA40);

    // GHIDRA: g_meshXOffsetBuffer @ 0x801FA780 (VS.EXE)
    // Sixty-four shorts. Cleared per entry by AnimCmd_RenderEntryGroup, advanced and clamped by
    // AnimCmd_XAddSet, summed by objlong_get over a run of consecutive entries, and added to a
    // linked part's coordinate by parts_link.
    //
    // PARTIAL: parts_link indexes it in lockstep with the two 64-entry buffers beside it, which is
    // what fixes the count at 64 here — the gap to g_meshEntryFlagsHiBuf @ 0x801FA800 allows exactly
    // that. objlong_get's index is a signed byte plus a length byte and can in principle run past
    // 63; that is not enforced by this by-address declaration.
    internal const int g_meshXOffsetBuffer = unchecked((int)0x801FA780);
}
