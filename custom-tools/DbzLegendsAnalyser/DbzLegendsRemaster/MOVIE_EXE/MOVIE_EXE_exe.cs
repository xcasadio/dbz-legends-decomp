using System;
using System.Buffers.Binary;
using PsxSdkMonogame;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.LibPress;

namespace DbzLegendsRemaster.MOVIE_EXE;

internal sealed class MOVIE_EXE_exe
{
    private const int DAT_8003FF30_ADDRESS = unchecked((int)0x8003FF30);
    private const int DAT_80065730_ADDRESS = unchecked((int)0x80065730);
    private const int DAT_8008AF30_ADDRESS = unchecked((int)0x8008AF30);
    private const int DAT_8008DC60_ADDRESS = unchecked((int)0x8008DC60);

    // GHIDRA: DAT_8003ff10 @ 0x8003FF10
    private static uint DAT_8003ff10;

    // GHIDRA: DAT_8003ff14 @ 0x8003FF14
    private static uint DAT_8003ff14;

    // GHIDRA: DAT_8003ff18 @ 0x8003FF18
    private static readonly CdlLOC DAT_8003ff18 = new();

    // GHIDRA: DAT_8003ff1c @ 0x8003FF1C
    private static int DAT_8003ff1c;

    // GHIDRA: DAT_8003ff24 @ 0x8003FF24
    private static readonly CdlATV DAT_8003ff24 = new();

    // GHIDRA: DAT_8003ff2c @ 0x8003FF2C
    private static int DAT_8003ff2c;

    // GHIDRA: DAT_8003ff30 @ 0x8003FF30
    internal static readonly byte[] DAT_8003ff30 = new byte[0x25800];

    // GHIDRA: DAT_80065730 @ 0x80065730
    internal static readonly byte[] DAT_80065730 = new byte[0x25800];

    // GHIDRA: DAT_8008af30 @ 0x8008AF30
    internal static readonly byte[] DAT_8008af30 = new byte[0x2D00];

    // GHIDRA: DAT_8008dc30 @ 0x8008DC30
    private static UnkStruct_8008DC30 DAT_8008dc30;

    // GHIDRA: DAT_8008dc60 @ 0x8008DC60
    internal static readonly byte[] DAT_8008dc60 = new byte[0x10000];

    // GHIDRA: DAT_800a45f4 @ 0x800A45F4
    private static int DAT_800a45f4;

    // GHIDRA: main @ 0x800209FC
    public void Main()
    {
        __main();
        ResetCallback();
        CdInit();
        PadInit(0);
        ResetGraph(0);
        SetGraphDebug(0);
        FntLoad(0x3c0, 0x100);
        int id = FntOpen(0x20, 0x20, 0x140, 200, 0, 0x200);
        SetDumpFnt(id);
        DAT_8003ff2c = 0x1e;

        Mainloop();

        // BLOCKED: the original FUN_80021274 loads TITLE.EXE and does not return.
    }

    // GHIDRA: Mainloop @ 0x80020A90
    private static void Mainloop()
    {
        CdlFILE local_30;
        // PARTIAL: the managed CdSearchFile adapter represents failure as null; it has no pointer
        // value corresponding to the original secondary (CdlFILE *)-1 sentinel.
        do
        {
            local_30 = CdSearchFile(new CdlFILE(), "\\MOVIE\\DBZ_OP.STR;1".ToCharArray());
        } while (local_30 == null);

        DAT_8003ff18.minute = local_30.pos.minute;
        DAT_8003ff18.second = local_30.pos.second;
        DAT_8003ff18.sector = local_30.pos.sector;
        FUN_80020d58(ref DAT_8008dc30, 0, 0, 0, 0xf0);
        FUN_80020dcc(DAT_8003ff18, FUN_80020e64);
        FUN_80020f98(ref DAT_8008dc30);

        while (true)
        {
            if (DAT_8003ff1c == 0)
            {
                uint bufferAddress = DAT_8008dc30.field_0x08 == 0
                    ? DAT_8008dc30.field_0x00
                    : DAT_8008dc30.field_0x04;
                DecDCTin(unchecked((int)bufferAddress), 1);
                DecDCTout(
                    unchecked((int)DAT_8008dc30.field_0x0C),
                    DAT_8008dc30.field_0x28 * DAT_8008dc30.field_0x2A / 2);
                FUN_80020f98(ref DAT_8008dc30);
                FUN_80021164(ref DAT_8008dc30);
            }

            VSync(4);

            bool displaySecondRect = DAT_8008dc30.field_0x20 == 0;
            short x = displaySecondRect ? DAT_8008dc30.field_0x18 : DAT_8008dc30.field_0x10;
            short y = displaySecondRect ? DAT_8008dc30.field_0x1A : DAT_8008dc30.field_0x12;
            short w = displaySecondRect ? DAT_8008dc30.field_0x1C : DAT_8008dc30.field_0x14;
            short h = displaySecondRect ? DAT_8008dc30.field_0x1E : DAT_8008dc30.field_0x16;

            var dispEnv = new DISPENV();
            var drawEnv = new DRAWENV();
            SetDefDispEnv(dispEnv, x, y, w, h);
            SetDefDrawEnv(drawEnv, x, y, w, h);
            dispEnv.isrgb24 = 1;
            dispEnv.disp.w = (short)(dispEnv.disp.w * 2 / 3);
            PutDispEnv(dispEnv);
            PutDrawEnv(drawEnv);
            SetDispMask(1);

            if (DAT_8003ff1c == 3)
            {
                break;
            }

            if (DAT_8003ff1c == 1)
            {
                DAT_8003ff2c--;
                if (DAT_8003ff2c == -1 || (PadRead(1) & 0x800) != 0)
                {
                    break;
                }
            }

            if ((PadRead(1) & 0x800) != 0)
            {
                break;
            }
        }

        SetDispMask(0);
        // BLOCKED: FUN_80021274("cdrom:\\TITLE.EXE;1") belongs to the next overlay slice.
    }

    // GHIDRA: FUN_80020d58 @ 0x80020D58
    private static void FUN_80020d58(ref UnkStruct_8008DC30 param_1, short param_2,
        short param_3, short param_4, short param_5)
    {
        param_1.field_0x00 = unchecked((uint)DAT_8003FF30_ADDRESS);
        param_1.field_0x04 = unchecked((uint)DAT_80065730_ADDRESS);
        param_1.field_0x0C = unchecked((uint)DAT_8008AF30_ADDRESS);
        param_1.field_0x08 = 0;
        param_1.field_0x20 = 0;
        param_1.field_0x2C = 0;
        param_1.field_0x10 = param_2;
        param_1.field_0x12 = param_3;
        param_1.field_0x14 = 0x3c0;
        param_1.field_0x16 = 0xf0;
        param_1.field_0x18 = param_4;
        param_1.field_0x1A = param_5;
        param_1.field_0x1C = 0x3c0;
        param_1.field_0x1E = 0xf0;
        param_1.field_0x24 = param_2;
        param_1.field_0x26 = param_3;
        param_1.field_0x28 = 0x18;
        param_1.field_0x2A = 0xf0;
    }

    // GHIDRA: FUN_80020dcc @ 0x80020DCC
    private static void FUN_80020dcc(CdlLOC param_1, Action param_2)
    {
        DecDCTReset(0);
        DAT_8003ff1c = 0;
        DecDCToutCallback(param_2);
        StSetRing(DAT_8008DC60_ADDRESS, 0x20);
        StSetStream(1, 1, -1, null, null);
        FUN_80021228(param_1);
        DAT_8003ff24.val0 = 0x80;
        DAT_8003ff24.val1 = 0;
        DAT_8003ff24.val2 = 0x80;
        DAT_8003ff24.val3 = 0;
        CdMix(DAT_8003ff24);
    }

    // GHIDRA: FUN_80020e64 @ 0x80020E64
    private static void FUN_80020e64()
    {
        if (DAT_800a45f4 != 0)
        {
            StCdInterrupt();
            DAT_800a45f4 = 0;
        }

        var rect = new RECT
        {
            x = DAT_8008dc30.field_0x24,
            y = DAT_8008dc30.field_0x26,
            w = DAT_8008dc30.field_0x28,
            h = DAT_8008dc30.field_0x2A,
        };
        LoadImage(rect, unchecked((int)DAT_8008dc30.field_0x0C));
        DAT_8008dc30.field_0x24 += DAT_8008dc30.field_0x28;

        short targetX = DAT_8008dc30.field_0x20 == 0
            ? DAT_8008dc30.field_0x10
            : DAT_8008dc30.field_0x18;
        short targetWidth = DAT_8008dc30.field_0x20 == 0
            ? DAT_8008dc30.field_0x14
            : DAT_8008dc30.field_0x1C;
        if (DAT_8008dc30.field_0x24 < targetX + targetWidth)
        {
            DecDCTout(
                unchecked((int)DAT_8008dc30.field_0x0C),
                DAT_8008dc30.field_0x28 * DAT_8008dc30.field_0x2A / 2);
        }
        else
        {
            DAT_8008dc30.field_0x2C = 1;
            DAT_8008dc30.field_0x20 = DAT_8008dc30.field_0x20 == 0 ? 1u : 0u;
            if (DAT_8008dc30.field_0x20 == 0)
            {
                DAT_8008dc30.field_0x24 = DAT_8008dc30.field_0x10;
                DAT_8008dc30.field_0x26 = DAT_8008dc30.field_0x12;
            }
            else
            {
                DAT_8008dc30.field_0x24 = DAT_8008dc30.field_0x18;
                DAT_8008dc30.field_0x26 = DAT_8008dc30.field_0x1A;
            }
        }
    }

    // GHIDRA: FUN_80020f98 @ 0x80020F98
    private static int FUN_80020f98(ref UnkStruct_8008DC30 param_1)
    {
        int timeout = 0x800000;
        do
        {
            int frameAddress = FUN_80021020(ref param_1);
            timeout--;
            if (frameAddress != 0)
            {
                param_1.field_0x08 = param_1.field_0x08 == 0 ? 1u : 0u;
                uint bufferAddress = param_1.field_0x08 == 0 ? param_1.field_0x00 : param_1.field_0x04;
                DecDCTvlc(frameAddress, unchecked((int)bufferAddress));
                StFreeRing(frameAddress);
                return 0;
            }
        } while (timeout != 0);

        return -1;
    }

    // GHIDRA: FUN_80021020 @ 0x80021020
    private static int FUN_80021020(ref UnkStruct_8008DC30 param_1)
    {
        int timeout = 0x800000;
        do
        {
            int status = StGetNext(out int frameAddress, out int headerAddress);
            timeout--;
            if (status == 0)
            {
                byte[] header = PsxRam.ReadBytes(headerAddress, 0x20);
                if (header == null)
                {
                    return 0;
                }

                uint frameNumber = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
                if (frameNumber > 0x3a1)
                {
                    DAT_8003ff1c = 1;
                    DAT_8003ff24.val0 = 0;
                    DAT_8003ff24.val1 = 0;
                    DAT_8003ff24.val2 = 0;
                    DAT_8003ff24.val3 = 0;
                    CdMix(DAT_8003ff24);
                    CdControlB(9, null, null);
                }

                ushort width = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0x10));
                ushort height = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0x12));
                if (DAT_8003ff10 != width || DAT_8003ff14 != height)
                {
                    ClearImage(new RECT { x = 0, y = 0, w = 0x280, h = 0x1e0 }, 0, 0, 0);
                    DAT_8003ff10 = width;
                    DAT_8003ff14 = height;
                }

                short vramWidth = (short)((DAT_8003ff10 * 3) / 2);
                short frameHeight = (short)DAT_8003ff14;
                param_1.field_0x14 = vramWidth;
                param_1.field_0x1C = vramWidth;
                param_1.field_0x16 = frameHeight;
                param_1.field_0x1E = frameHeight;
                param_1.field_0x2A = frameHeight;
                return frameAddress;
            }
        } while (timeout != 0);

        return 0;
    }

    // GHIDRA: FUN_80021164 @ 0x80021164
    private static void FUN_80021164(ref UnkStruct_8008DC30 param_1)
    {
        int timeout = 0x800000;
        while (param_1.field_0x2C == 0)
        {
            timeout--;
            if (timeout == 0)
            {
                Console.WriteLine("time out in decoding !");
                param_1.field_0x2C = 1;
                param_1.field_0x20 = param_1.field_0x20 == 0 ? 1u : 0u;
                if (param_1.field_0x20 == 0)
                {
                    param_1.field_0x24 = param_1.field_0x10;
                    param_1.field_0x26 = param_1.field_0x12;
                }
                else
                {
                    param_1.field_0x24 = param_1.field_0x18;
                    param_1.field_0x26 = param_1.field_0x1A;
                }
            }
        }

        param_1.field_0x2C = 0;
    }

    // GHIDRA: FUN_80021228 @ 0x80021228
    private static void FUN_80021228(CdlLOC param_1)
    {
        while (CdControl(0x15, param_1, null) == 0)
        {
        }

        while (CdRead2(0x1c0) == 0)
        {
        }
    }

    // GHIDRA: __main @ 0x8002B9FC
    private static void __main()
    {
        // PARTIAL: compiler runtime initialization is provided by the CLR.
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: resolves the MOVIE.EXE PSX buffer addresses after the overlay switch.
    internal static (byte[] Buffer, int Offset)? ResolveAddress(int address)
    {
        if (address >= DAT_8003FF30_ADDRESS && address < DAT_8003FF30_ADDRESS + DAT_8003ff30.Length)
        {
            return (DAT_8003ff30, address - DAT_8003FF30_ADDRESS);
        }
        if (address >= DAT_80065730_ADDRESS && address < DAT_80065730_ADDRESS + DAT_80065730.Length)
        {
            return (DAT_80065730, address - DAT_80065730_ADDRESS);
        }
        if (address >= DAT_8008AF30_ADDRESS && address < DAT_8008AF30_ADDRESS + DAT_8008af30.Length)
        {
            return (DAT_8008af30, address - DAT_8008AF30_ADDRESS);
        }
        if (address >= DAT_8008DC60_ADDRESS && address < DAT_8008DC60_ADDRESS + DAT_8008dc60.Length)
        {
            return (DAT_8008dc60, address - DAT_8008DC60_ADDRESS);
        }

        return null;
    }
}