namespace DbzLegendsRemaster.TITLE_EXE;

internal sealed class TITLE_EXE_exe
{
    // GHIDRA: main @ 0x800581DC
    public void Main()
    {
        // BLOCKED: the TITLE.EXE runtime is not transliterated yet.
        //
        // main @ 0x800581DC is entered from start @ 0x80068FF4 (jal at 0x80069090) and never
        // returns: its body ends on do { ... } while (true). Its opening sequence is
        //   __main, FUN_80070b64 (syscall(0)), ResetCallback, ResetGraph(0), InitGeom,
        //   SetDispMask(0), FUN_80057508, PadInit(0), CdInit,
        //   do { CdSearchFile(&DAT_800a8860, "\\SELECT.EXE;1"); } while (result == NULL),
        //   ReadFile("\\SUB\\TITLE.B;1", &DAT_80110000, 0), InitHeap, srand,
        //   FUN_80070e44, FntLoad(0x3c0, 0x100), FntOpen(0x10, 0x10, 0x100, 200, 0, 0x200).
        //
        // None of the FUN_ callees on that path are closed, so nothing is ported here. Porting a
        // truncated prefix would enter the CdSearchFile retry loop with no exit and no VSync.
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: no TITLE.EXE global is transliterated yet, so no PSX address range resolves to a
    // managed buffer. The bridge still switches to this resolver so the MOVIE.EXE ranges it
    // overlaps stop answering, matching what LoadExec does to resident RAM.
    internal static (byte[] Buffer, int Offset)? ResolveAddress(int address)
    {
        return null;
    }
}
