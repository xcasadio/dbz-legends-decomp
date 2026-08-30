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
        //   __main, EnterCriticalSection, ResetCallback, ResetGraph(0), InitGeom,
        //   SetDispMask(0), ClearVram, PadInit(0), CdInit,
        //   do { CdSearchFile(&CdlFILE_800a8860, "\\SELECT.EXE;1"); } while (result == NULL),
        //   ReadFile("\\SUB\\TITLE.B;1", &DAT_80110000, 0), InitHeap(0x10000, 0x10000),
        //   srand(0x10000), ExitCriticalSection, FntLoad(0x3c0, 0x100),
        //   FntOpen(0x10, 0x10, 0x100, 200, 0, 0x200), SetupGeometry, then CreateTask.
        //
        // The task system underneath is closed: CreateTask @ 0x80049504, DeleteTask @ 0x80049720
        // and ExecuteTaskList @ 0x800497FC over the 21 lists anchored at g_TaskListHead,
        // g_TaskListTail and g_TaskListCount. What is still open is the content: FUN_80037388,
        // FUN_80056dc0, FUN_80038228 and FUN_80021dd0. The SDK also still lacks InitGeom,
        // SetFarColor and srand. Nothing is ported here yet.
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
