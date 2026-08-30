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
internal static class SharedHighRam
{
    // GHIDRA: SHORT_ARRAY_801ff000 @ 0x801FF000
    internal static readonly short[] SHORT_ARRAY_801ff000 = new short[0x124];
}
