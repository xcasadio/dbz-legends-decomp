using PsxSdkMonogame;

namespace DbzLegendsRemaster;

// JUSTIFICATION: PSX hardware adaptation only
// RELATION: models the stretch of high RAM at 0x801FF000. No overlay's text or data segment covers
// it — TITLE.EXE, the largest, ends at 0x800B9EEF — so it survives LoadExec, and the overlays use
// it to talk to each other.
//
// This is not a guess. SLPS_003.55 fills the button-remap tables there through FUN_8002165c
// @ 0x8002165C, writing fourteen masks at index 0x10 and fourteen more at 0x1E. TITLE.EXE then
// reads those very tables back in ProcessPadInput @ 0x800578A8, which addresses them as
// g_PadRemapTable0 and g_PadRemapTable1 — and 0x801FF000 + 0x10*2 = 0x801FF020, 0x801FF000 + 0x1E*2 =
// 0x801FF03C. The bootstrap configures the pad for the whole game; every later overlay inherits it.
//
// Bytes rather than a short array, because the region is read at three widths. The title task
// UpdateTitleScreen @ 0x80021E28 writes single bytes at 0x58..0x5d, a word at 0x68, and walks the save
// table at 0x200 as both ints and shorts. Only bytes can carry all three.
//
// The extent, 0x248, is what the code actually touches and no more: LAB_80021F98 clears
// INT_ARRAY_801ff200[0] through [0x11], that is 0x200 + 18 * 4 = 0x248.
internal static class SharedHighRam
{
    private const int Base = unchecked((int)0x801FF000);

    internal const int Size = 0x248;

    internal static readonly byte[] RAM_801ff000 = LibGpu.RamRegion(Base, Size);

    // GHIDRA: SHORT_ARRAY_801ff000 @ 0x801FF000
    internal static readonly ShortWindow SHORT_ARRAY_801ff000 = new(RAM_801ff000, 0);

    // GHIDRA: INT_ARRAY_801ff200 @ 0x801FF200
    // The save-slot table the memory card fills. Six entries of eight bytes are read as short
    // pairs; the clear path zeroes eighteen words.
    internal static readonly IntWindow INT_ARRAY_801ff200 = new(RAM_801ff000, 0x200);

    // GHIDRA: DAT_801ff002 @ 0x801FF002
    internal static ushort DAT_801ff002
    {
        get => MipsMemory.ReadU16(RAM_801ff000, 0x02);
        set => MipsMemory.WriteU16(RAM_801ff000, 0x02, value);
    }

    // GHIDRA: DAT_801ff00a @ 0x801FF00A
    internal static ushort DAT_801ff00a
    {
        get => MipsMemory.ReadU16(RAM_801ff000, 0x0a);
        set => MipsMemory.WriteU16(RAM_801ff000, 0x0a, value);
    }

    // GHIDRA: g_OptionsRecord64 @ 0x801FF018
    // The 64-byte options block. SELECT.EXE's memory-card load path copies it out of the card
    // record; the button-remap tables live inside it at +8 and +0x24. RunOptionsScreen reads bit 2
    // of its first word, and that bit alone decides whether the sound test exists: the confirm on
    // row 0 only reaches RunSoundTestScreen when `(g_OptionsRecord64 & 4) != 0`.
    internal static int g_OptionsRecord64
    {
        get => MipsMemory.ReadI32(RAM_801ff000, 0x18);
        set => MipsMemory.WriteI32(RAM_801ff000, 0x18, value);
    }

    // GHIDRA: DAT_801ff01c @ 0x801FF01C
    // Field +4 of the options block: the difficulty, and the options screen cycles it over exactly
    // three values. Right wraps 2 -> 0; left decrements and wraps 0 -> 2, which the original does by
    // testing for zero BEFORE the decrement and overwriting the underflow afterwards.
    // Row 1 of the screen (難易度) shows it, and the three value boxes it selects differ in width:
    // x = {-40, 28, 93}, u = {176, 168, 168}, w = {64, 56, 56}, read live off the console.
    internal static ushort DAT_801ff01c
    {
        get => MipsMemory.ReadU16(RAM_801ff000, 0x1c);
        set => MipsMemory.WriteU16(RAM_801ff000, 0x1c, value);
    }

    // GHIDRA: _DAT_801ff01e @ 0x801FF01E
    // Field +6 of the options block. Zero is STEREO, non-zero is MONO — closed by InitializeCdAudio
    // @ 0x80025658, which on zero writes the crossed CdlATV {0x7F, 0x08, 0x7F, 0x08} and calls
    // SsSetStereo(), and otherwise writes {0x3F, 0x3F, 0x3F, 0x3F} and calls SsSetMono().
    // Measured on the console in this very screen: the word is 0 and row 0 shows ステレオ.
    // The name stays raw. The meaning is closed, but the address is inside the block the three
    // overlays share, so naming it belongs to the cross-overlay 0x801FFxxx pass, not to SELECT.EXE
    // alone — the same reason g_UnlockTier @ 0x801FF002 was held back.
    internal static ushort _DAT_801ff01e
    {
        get => MipsMemory.ReadU16(RAM_801ff000, 0x1e);
        set => MipsMemory.WriteU16(RAM_801ff000, 0x1e, value);
    }

    // GHIDRA: DAT_801ff058 @ 0x801FF058 .. DAT_801ff05d @ 0x801FF05D
    // Six bytes written as two groups of three, one per port, when a pad holds the 0x1d0 button
    // combination on the title screen.
    internal static byte DAT_801ff058 { get => RAM_801ff000[0x58]; set => RAM_801ff000[0x58] = value; }

    internal static byte DAT_801ff059 { get => RAM_801ff000[0x59]; set => RAM_801ff000[0x59] = value; }

    internal static byte DAT_801ff05a { get => RAM_801ff000[0x5a]; set => RAM_801ff000[0x5a] = value; }

    internal static byte DAT_801ff05b { get => RAM_801ff000[0x5b]; set => RAM_801ff000[0x5b] = value; }

    internal static byte DAT_801ff05c { get => RAM_801ff000[0x5c]; set => RAM_801ff000[0x5c] = value; }

    internal static byte DAT_801ff05d { get => RAM_801ff000[0x5d]; set => RAM_801ff000[0x5d] = value; }

    // GHIDRA: g_CardProbeResult @ 0x801FF068
    // The memory card probe's result, stored straight from FUN_80022780's return.
    internal static int g_CardProbeResult
    {
        get => MipsMemory.ReadI32(RAM_801ff000, 0x68);
        set => MipsMemory.WriteI32(RAM_801ff000, 0x68, value);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: lets an overlay's address resolver map this span, so the byte-at-a-time writes the
    // original does here reach it through PsxRam like any other PSX address.
    internal static (byte[] Buffer, int Offset)? Resolve(int address)
    {
        int offset = address - Base;
        return offset >= 0 && offset < RAM_801ff000.Length ? (RAM_801ff000, offset) : null;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original names these regions as typed arrays and indexes them. These windows
    // keep that spelling over the byte region, so a call site still reads
    // SHORT_ARRAY_801ff000[0x10] while the storage underneath is the shared bytes.
    internal readonly struct ShortWindow
    {
        private readonly byte[] _buf;
        private readonly int _offset;

        internal ShortWindow(byte[] buf, int offset)
        {
            _buf = buf;
            _offset = offset;
        }

        internal short this[int index]
        {
            get => MipsMemory.ReadI16(_buf, _offset + (index * 2));
            set => MipsMemory.WriteI16(_buf, _offset + (index * 2), value);
        }

        // The original also reads this table at byte granularity through a roaming pointer.
        internal ushort ReadU16At(int byteOffset) => MipsMemory.ReadU16(_buf, _offset + byteOffset);
    }

    internal readonly struct IntWindow
    {
        private readonly byte[] _buf;
        private readonly int _offset;

        internal IntWindow(byte[] buf, int offset)
        {
            _buf = buf;
            _offset = offset;
        }

        internal int this[int index]
        {
            get => MipsMemory.ReadI32(_buf, _offset + (index * 4));
            set => MipsMemory.WriteI32(_buf, _offset + (index * 4), value);
        }

        // UpdateTitleScreen walks this table by raw byte offset, reading halfwords out of it.
        internal ushort ReadU16At(int byteOffset) => MipsMemory.ReadU16(_buf, _offset + byteOffset);
    }
}
