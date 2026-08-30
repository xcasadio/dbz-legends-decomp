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

    // GHIDRA: FUN_80037388 @ 0x80037388
    // Its own PSX address, kept so a task block holds exactly what the console holds.
    private const int FUN_80037388_Address = unchecked((int)0x80037388);

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

    // GHIDRA: DAT_80083454 @ 0x80083454
    // State word of the display/fade machine FUN_80038228 @ 0x80038228, still open. Read here
    // because FUN_80037388 gates its AddPrim on it.
    internal static int DAT_80083454;

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
        TaskSystem.RegisterCallback(FUN_80037388_Address, FUN_80037388);
        TaskSystem.CreateTask(FUN_80037388_Address, 0, 0, 0, 0, TaskSystem.g_TaskListHead[0]);
        FUN_80037388();

        // BLOCKED: the rest of main is not transliterated yet. It continues with
        //   FUN_80056dc0(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0);
        //   DAT_80083544 = 0; FUN_80038228(8, 0); FUN_80058d64();
        //   do { ... FUN_80021dd0(); RunFrameLoop(); FUN_80058a9c(); ... } while (true);
        // None of those four is closed, and RunFrameLoop reaches FUN_80038228, FUN_80056b30 and
        // FUN_80056d00 as well. Porting the frame loop now would mean inventing their bodies.
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
        GteScratch.MATRIX_1f8000e4.m[6] = 0x1000;
        GteScratch.MATRIX_1f8000e4.m[3] = 0x1000;
        GteScratch.MATRIX_1f8000e4.m[0] = 0x1000;
        GteScratch.MATRIX_1f8000e4.m[8] = 0;
        GteScratch.MATRIX_1f8000e4.m[7] = 0;
        GteScratch.MATRIX_1f8000e4.m[5] = 0;
        GteScratch.MATRIX_1f8000e4.m[4] = 0;
        GteScratch.MATRIX_1f8000e4.m[2] = 0;
        GteScratch.MATRIX_1f8000e4.m[1] = 0;
        SetColorMatrix(GteScratch.MATRIX_1f8000e4);
        GteScratch.SVECTOR_1f800104.vx = 0;
        GteScratch.SVECTOR_1f800104.vy = 0;
        GteScratch.SVECTOR_1f800104.vz = 0;
        RotMatrix(GteScratch.SVECTOR_1f800104, GteScratch.MATRIX_1f800000);
        SetLightMatrix(GteScratch.MATRIX_1f800000);
        GteScratch.SVECTOR_1f80007c.vx = rx;
        GteScratch.SVECTOR_1f80007c.vy = ry;
        GteScratch.SVECTOR_1f80007c.vz = rz;
        GteScratch.DAT_1f800084 = rx;
        GteScratch.DAT_1f800086 = ry;
        GteScratch.DAT_1f800088 = rz;
        RotMatrix(GteScratch.SVECTOR_1f80007c, GteScratch.MATRIX_1f800000);
        SetRotMatrix(GteScratch.MATRIX_1f800000);
        GteScratch._DAT_1f8000b4 = param_4;
        GteScratch.DAT_1f8000b8 = param_5;
        GteScratch._DAT_1f8000bc = param_6;
        GteScratch.DAT_1f8000c4 = param_4;
        GteScratch.DAT_1f8000c8 = param_5;
        GteScratch.DAT_1f8000cc = param_6;
        GteScratch.DAT_1f8000d4 = param_4;
        GteScratch.DAT_1f8000d8 = param_5;
        GteScratch.DAT_1f8000dc = param_6;
        GteScratch.DAT_1f800114 = ofx;
        GteScratch.DAT_1f800124 = ofx;
        GteScratch.DAT_1f80011c = ofx;
        GteScratch.DAT_1f800110 = ofy;
        GteScratch.DAT_1f800120 = ofy;
        GteScratch.DAT_1f800118 = ofy;
        GteScratch.DAT_1f8000d0 = param_7;
        GteScratch._DAT_1f8000c0 = param_7;
        GteScratch.DAT_1f8000e0 = param_7;
    }

    // GHIDRA: FUN_80037388 @ 0x80037388
    // Registered as a task by main and then called directly, so it runs once per frame. It swaps
    // the live camera triplets with their pending counterparts, rebuilds the rotation and
    // translation matrices, and derives DAT_1f800128 from the projected depth.
    internal static void FUN_80037388()
    {
        var local_38 = new LibGte.SVECTOR();
        int[] alStack_30 = new int[2];

        GteScratch.DAT_1f80008c = GteScratch.SVECTOR_1f80007c.vx;
        GteScratch.DAT_1f80008e = GteScratch.SVECTOR_1f80007c.vy;
        GteScratch.DAT_1f800090 = GteScratch.SVECTOR_1f80007c.vz;
        GteScratch.SVECTOR_1f80007c.vx = GteScratch.DAT_1f800084;
        GteScratch.SVECTOR_1f80007c.vy = GteScratch.DAT_1f800086;
        GteScratch.SVECTOR_1f80007c.vz = GteScratch.DAT_1f800088;
        GteScratch.DAT_1f8000d4 = GteScratch._DAT_1f8000b4;
        GteScratch.DAT_1f8000d8 = GteScratch.DAT_1f8000b8;
        GteScratch.DAT_1f8000dc = GteScratch._DAT_1f8000bc;
        GteScratch.DAT_1f8000e0 = GteScratch._DAT_1f8000c0;
        GteScratch._DAT_1f8000b4 = GteScratch.DAT_1f8000c4;
        GteScratch.DAT_1f8000b8 = GteScratch.DAT_1f8000c8;
        GteScratch._DAT_1f8000bc = GteScratch.DAT_1f8000cc;
        GteScratch._DAT_1f8000c0 = GteScratch.DAT_1f8000d0;
        GteScratch.DAT_1f80011c = GteScratch.DAT_1f800114;
        GteScratch.DAT_1f800118 = GteScratch.DAT_1f800110;
        GteScratch.DAT_1f800114 = GteScratch.DAT_1f800124;
        GteScratch.DAT_1f800110 = GteScratch.DAT_1f800120;
        RotMatrix(GteScratch.SVECTOR_1f80007c, GteScratch.MATRIX_1f800000);
        GteScratch.MATRIX_1f800000.t[2] = 0;
        GteScratch.MATRIX_1f800000.t[1] = 0;
        GteScratch.MATRIX_1f800000.t[0] = 0;
        SetRotMatrix(GteScratch.MATRIX_1f800000);
        SetTransMatrix(GteScratch.MATRIX_1f800000);
        local_38.vx = 0;
        local_38.vz = 0;
        local_38.vy = (short)GteScratch.DAT_1f8000b8;
        RotTrans(local_38, GteScratch.VECTOR_1f800094, alStack_30);
        GteScratch.VECTOR_1f800094.vz = GteScratch.VECTOR_1f800094.vz + GteScratch._DAT_1f8000c0;
        SetGeomOffset(GteScratch.DAT_1f800114, GteScratch.DAT_1f800110);
        TransMatrix(GteScratch.MATRIX_1f800000, GteScratch.VECTOR_1f800094);
        SetTransMatrix(GteScratch.MATRIX_1f800000);
        SetRotMatrix(GteScratch.MATRIX_1f800000);
        PushMatrix();
        GteScratch.SVECTOR_1f800020.vy = 0;
        GteScratch.SVECTOR_1f800020.vz = 0;
        GteScratch.SVECTOR_1f800020.vx = GteScratch.SVECTOR_1f80007c.vx;
        RotMatrix(GteScratch.SVECTOR_1f800020, GteScratch.MATRIX_1f800000);
        GteScratch.MATRIX_1f800000.t[2] = 0;
        GteScratch.MATRIX_1f800000.t[1] = 0;
        GteScratch.MATRIX_1f800000.t[0] = 0;
        SetTransMatrix(GteScratch.MATRIX_1f800000);
        SetRotMatrix(GteScratch.MATRIX_1f800000);
        GteScratch.SVECTOR_1f800020.vx = 0;
        GteScratch.SVECTOR_1f800020.vy = 0;
        GteScratch.SVECTOR_1f800020.vz = (short)(GteScratch._DAT_1f8000c0 + 0x9d8);
        RotTrans(GteScratch.SVECTOR_1f800020, GteScratch.VECTOR_1f800048, GteScratch.DAT_1f800078);
        int lVar1 = GteScratch.VECTOR_1f800048.vz;
        if (GteScratch.VECTOR_1f800048.vz < 0)
        {
            lVar1 = GteScratch.VECTOR_1f800048.vz + 3;
        }

        GteScratch.DAT_1f800128 = 0x800 - (lVar1 >> 2);
        if (GteScratch.DAT_1f800128 < 0)
        {
            GteScratch.DAT_1f800128 = 0;
        }

        PopMatrix();
        if (1 < DAT_80083454)
        {
            // BLOCKED: AddPrim(DAT_800834e0 + 0x206C, &POLY_GT4_800b9518). Neither the ordering
            // table root DAT_800834e0 nor the primitive POLY_GT4_800b9518 is transliterated, and
            // DAT_80083454 only exceeds 1 once FUN_80038228 has run, which is still open. The
            // guard is reproduced so the branch surfaces the day those are closed.
        }
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
