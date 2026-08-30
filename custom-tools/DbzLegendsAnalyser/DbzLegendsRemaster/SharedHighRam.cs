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
// DAT_801ff020 and DAT_801ff03c — and 0x801FF000 + 0x10*2 = 0x801FF020, 0x801FF000 + 0x1E*2 =
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

    // GHIDRA: DAT_801ff058 @ 0x801FF058 .. DAT_801ff05d @ 0x801FF05D
    // Six bytes written as two groups of three, one per port, when a pad holds the 0x1d0 button
    // combination on the title screen.
    internal static byte DAT_801ff058 { get => RAM_801ff000[0x58]; set => RAM_801ff000[0x58] = value; }

    internal static byte DAT_801ff059 { get => RAM_801ff000[0x59]; set => RAM_801ff000[0x59] = value; }

    internal static byte DAT_801ff05a { get => RAM_801ff000[0x5a]; set => RAM_801ff000[0x5a] = value; }

    internal static byte DAT_801ff05b { get => RAM_801ff000[0x5b]; set => RAM_801ff000[0x5b] = value; }

    internal static byte DAT_801ff05c { get => RAM_801ff000[0x5c]; set => RAM_801ff000[0x5c] = value; }

    internal static byte DAT_801ff05d { get => RAM_801ff000[0x5d]; set => RAM_801ff000[0x5d] = value; }

    // GHIDRA: DAT_801ff068 @ 0x801FF068
    // The memory card probe's result, stored straight from FUN_80022780's return.
    internal static int DAT_801ff068
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
