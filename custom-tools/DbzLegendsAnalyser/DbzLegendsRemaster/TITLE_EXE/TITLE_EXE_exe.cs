using PsxSdkMonogame;
using static PsxSdkMonogame.Kernel;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.LibGte;

namespace DbzLegendsRemaster.TITLE_EXE;

internal sealed class TITLE_EXE_exe
{
    private const int TitleBBufferAddress = unchecked((int)0x80110000);
    private const int HeapBaseAddress = 0x00010000;

    // GHIDRA: DAT_80110000 @ 0x80110000
    // Destination of ReadFile("\\SUB\\TITLE.B;1", ...). The file is 0x25000 bytes, the size
    // docs/TITLE_B_FILE_FORMAT_ANALYSIS.md measured independently.
    internal static readonly byte[] DAT_80110000 = new byte[0x25000];

    // GHIDRA: CdlFILE_800a8860 @ 0x800A8860
    private static readonly CdlFILE CdlFILE_800a8860 = new();

    // GHIDRA: DAT_80083498 @ 0x80083498
    private static int DAT_80083498;

    // GHIDRA: DAT_8008344c @ 0x8008344C
    private static int DAT_8008344c;

    // GHIDRA: DAT_80083450 @ 0x80083450
    private static int DAT_80083450;

    // GHIDRA: DAT_80083448 @ 0x80083448
    private static int DAT_80083448;

    // GHIDRA: main @ 0x800581DC
    public void Main()
    {
        __main();
        EnterCriticalSection();
        ResetCallback();
        ResetGraph(0);
        InitGeom();
        SetDispMask(0);
        ClearVram();
        PadInit(0);
        CdInit();

        CdlFILE pCVar1;
        do
        {
            pCVar1 = CdSearchFile(CdlFILE_800a8860, "\\SELECT.EXE;1".ToCharArray());
        } while (pCVar1 == null);

        ReadFile("\\SUB\\TITLE.B;1".ToCharArray(), TitleBBufferAddress, 0);
        uint uVar2 = 0x10000;
        InitHeap(HeapBaseAddress, 0x10000);
        srand(uVar2);
        ExitCriticalSection();
        FntLoad(0x3c0, 0x100);
        DAT_80083498 = FntOpen(0x10, 0x10, 0x100, 200, 0, 0x200);
        DAT_8008344c = 0;
        DAT_80083450 = 0;
        DAT_80083448 = 0;
        SetupGeometry(0xa8, 0x80, 0x1000, 0, 0, 0, 0x1000, 0, 0, 0);

        // BLOCKED: the rest of main is not transliterated yet. It continues with
        //   CreateTask(FUN_80037388, 0, 0, 0, 0, g_TaskListHead[0]); FUN_80037388();
        //   FUN_80056dc0(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0);
        //   DAT_80083544 = 0; FUN_80038228(8, 0); FUN_80058d64();
        //   do { ... FUN_80021dd0(); RunFrameLoop(); FUN_80058a9c(); ... } while (true);
        // The task system underneath is ported and benched (see TaskSystem), but none of those
        // FUN_ callees is closed, and RunFrameLoop itself calls FUN_80038228, FUN_80056b30 and
        // FUN_80056d00. Porting the frame loop before them would mean inventing their bodies.
    }

    // GHIDRA: ClearVram @ 0x80057508
    private static void ClearVram()
    {
        var local_10 = new LibGpu.RECT
        {
            w = 0x400,
            h = 0x200,
            x = 0,
            y = 0,
        };
        ClearImage(local_10, 0, 0, 0);
        DrawSync(0);
    }

    // GHIDRA: SetupGeometry @ 0x80057674
    private static void SetupGeometry(int ofx, int ofy, int h, int param_4, int param_5,
        int param_6, int param_7, short rx, short ry, short rz)
    {
        SetGeomOffset(ofx, ofy);
        SetGeomScreen(h);
        SetFarColor(0x80, 0x80, 0x80);
        SetBackColor(0x80, 0x80, 0x80);

        // PARTIAL: the original then writes the colour matrix, the light matrix and the rotation
        // matrix straight into COP2 scratch at 0x1F8000E4..0x1F800124 before handing them to
        // SetColorMatrix / SetLightMatrix / SetRotMatrix. Those scratch addresses are not modelled
        // by this port, and no closed call site reads them back, so only the four calls above are
        // reproduced. The remaining arguments are carried but unused, exactly as their names say.
    }

    // GHIDRA: ReadFile @ 0x80057DF4
    private static void ReadFile(char[] fileName, int buffer, ushort mode)
    {
        var cdlFile = new CdlFILE();
        WaitSearchFile(fileName, cdlFile);
        ReadCDData(cdlFile, buffer, (short)mode);
    }

    // GHIDRA: WaitSearchFile @ 0x80057F80
    private static void WaitSearchFile(char[] fileName, CdlFILE cdlFile)
    {
        CdlFILE result;
        do
        {
            result = CdSearchFile(cdlFile, fileName);
        } while (result == null);
    }

    // GHIDRA: ReadCDData @ 0x80057E40
    private static uint ReadCDData(CdlFILE cdlFile, int buffer, short mode)
    {
        uint sectors = (uint)(cdlFile.size + 0x7ff) >> 0xb;
        byte[] result = new byte[8];
        int readBytes;
        int status;

        while (true)
        {
            do
            {
                CdControl(2, cdlFile.pos, result);
                do
                {
                    status = CdSync(0, result);
                } while (status == 0);
            } while (status == 5);

            do
            {
                readBytes = CdRead((int)sectors, buffer, 0x80);
            } while (readBytes != 1);

            if (mode != 0)
            {
                break;
            }

            // The original spells this as `while (readBytes = CdReadSync(0, result), 0 < readBytes)`;
            // C# has no comma operator, so the assignment is lifted out unchanged.
            readBytes = CdReadSync(0, result);
            while (0 < readBytes)
            {
                VSync(0);
                readBytes = CdReadSync(0, result);
            }

            if (readBytes != -1)
            {
                return sectors;
            }
        }

        return 0;
    }

    // GHIDRA: __main @ 0x8006909C
    private static void __main()
    {
        // PARTIAL: compiler runtime initialization is provided by the CLR.
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: resolves the PSX ranges this overlay models — the TITLE.B staging buffer, and the
    // heap armed by InitHeap where every task block lives. Switching to this resolver also makes
    // the overlapping MOVIE.EXE ranges stop answering, matching what LoadExec does to resident RAM.
    internal static (byte[] Buffer, int Offset)? ResolveAddress(int address)
    {
        if (address >= TitleBBufferAddress && address < TitleBBufferAddress + DAT_80110000.Length)
        {
            return (DAT_80110000, address - TitleBBufferAddress);
        }

        return PsxHeap.Resolve(address);
    }
}
