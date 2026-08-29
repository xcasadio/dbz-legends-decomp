using System;
using System.Buffers.Binary;
using PsxSdkMonogame;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.LibPress;
using static PsxSdkMonogame.LibSpu;

namespace DbzLegendsRemaster.SLPS_003_55;

internal sealed class SLPS_003_55_exe
{
    private const int DAT_8004C894_ADDRESS = unchecked((int)0x8004C894);
    private const int DAT_80072094_ADDRESS = unchecked((int)0x80072094);
    private const int DAT_80097894_ADDRESS = unchecked((int)0x80097894);
    private const int DAT_8009A5C4_ADDRESS = unchecked((int)0x8009A5C4);

    // GHIDRA: DAT_8004c874 @ 0x8004C874
    private static uint DAT_8004c874;

    // GHIDRA: DAT_8004c878 @ 0x8004C878
    private static uint DAT_8004c878;

    // GHIDRA: DAT_8004c87c @ 0x8004C87C
    private static readonly CdlLOC DAT_8004c87c = new();

    // GHIDRA: DAT_8004c880 @ 0x8004C880
    private static int DAT_8004c880;

    // GHIDRA: DAT_8004c888 @ 0x8004C888
    private static readonly CdlATV DAT_8004c888 = new();

    // GHIDRA: DAT_8004c890 @ 0x8004C890
    private static int DAT_8004c890;

    // GHIDRA: DAT_8004c894 @ 0x8004C894
    internal static readonly byte[] DAT_8004c894 = new byte[0x25800];

    // GHIDRA: DAT_80072094 @ 0x80072094
    internal static readonly byte[] DAT_80072094 = new byte[0x25800];

    // GHIDRA: DAT_80097894 @ 0x80097894
    internal static readonly byte[] DAT_80097894 = new byte[0x2D00];

    // GHIDRA: DAT_8009a594 @ 0x8009A594
    private static UnkStruct_8009A594 DAT_8009a594;

    // GHIDRA: DAT_8009a5c4 @ 0x8009A5C4
    internal static readonly byte[] DAT_8009a5c4 = new byte[0x10000];

    // GHIDRA: DAT_800b1704 @ 0x800B1704
    private static int DAT_800b1704 = 0;

    // GHIDRA: SHORT_ARRAY_801ff000 @ 0x801FF000
    private static readonly short[] SHORT_ARRAY_801ff000 = new short[0x124];

    // GHIDRA: main @ 0x80020D10
    public void Main()
    {
        __main();
        ResetCallback();
        CdInit();
        PadInit(0);
        ResetGraph(0);
        SetGraphDebug(0);
        SpuInit();
        SetVolume(0x7f, 0x7f);
        FUN_8002c80c();
        FUN_80035410(0, 0, 1);
        FUN_8002c9dc(0, 0x3f, 0x3f);
        FntLoad(0x3c0, 0x100);
        int id = FntOpen(0x20, 0x20, 0x140, 200, 0, 0x200);
        SetDumpFnt(id);
        DAT_8004c890 = 0x1e;
        FUN_8002165c();

        MainLoop();

        // BLOCKED: the original never returns here because FUN_800215c0 loads MOVIE.EXE.
        // The first approved slice ends at that overlay boundary.
    }

    // GHIDRA: MainLoop @ 0x80020DE8
    private static void MainLoop()
    {
        CdlFILE local_30;
        // PARTIAL: the managed CdSearchFile adapter represents failure as null; it has no pointer
        // value corresponding to the original secondary (CdlFILE *)-1 sentinel.
        do
        {
            local_30 = CdSearchFile(new CdlFILE(), "\\MOVIE\\BANDAI.STR;1".ToCharArray());
        } while (local_30 == null);

        DAT_8004c87c.minute = local_30.pos.minute;
        DAT_8004c87c.second = local_30.pos.second;
        DAT_8004c87c.sector = local_30.pos.sector;
        FUN_800210a4(ref DAT_8009a594, 0, 0, 0, 0xf0);
        FUN_80021118(DAT_8004c87c, FUN_800211b0);
        FUN_800212e4(ref DAT_8009a594);

        uint pad;
        do
        {
            if (DAT_8004c880 == 0)
            {
                uint bufferAddress = DAT_8009a594.field_0x08 == 0
                    ? DAT_8009a594.field_0x00
                    : DAT_8009a594.field_0x04;
                DecDCTin(unchecked((int)bufferAddress), 3);
                DecDCTout(
                    unchecked((int)DAT_8009a594.field_0x0C),
                    DAT_8009a594.field_0x28 * DAT_8009a594.field_0x2A / 2);
                FUN_800212e4(ref DAT_8009a594);
                FUN_800214b0(ref DAT_8009a594);
            }

            VSync(4);

            bool displaySecondRect = DAT_8009a594.field_0x20 == 0;
            short x = displaySecondRect ? DAT_8009a594.field_0x18 : DAT_8009a594.field_0x10;
            short y = displaySecondRect ? DAT_8009a594.field_0x1A : DAT_8009a594.field_0x12;
            short w = displaySecondRect ? DAT_8009a594.field_0x1C : DAT_8009a594.field_0x14;
            short h = displaySecondRect ? DAT_8009a594.field_0x1E : DAT_8009a594.field_0x16;

            var dispEnv = new DISPENV();
            var drawEnv = new DRAWENV();
            SetDefDispEnv(dispEnv, x, y, w, h);
            SetDefDrawEnv(drawEnv, x, y, w, h);
            dispEnv.isrgb24 = 1;
            dispEnv.disp.w = (short)(dispEnv.disp.w * 2 / 3);
            PutDispEnv(dispEnv);
            PutDrawEnv(drawEnv);
            SetDispMask(1);

            pad = PadRead(1);
            if (DAT_8004c880 == 3)
            {
                break;
            }

            if (DAT_8004c880 == 1)
            {
                DAT_8004c890--;
                if (DAT_8004c890 == -1 || (pad & 0x800) != 0)
                {
                    break;
                }
            }
        } while ((pad & 0x800) == 0);

        SetDispMask(0);
        // BLOCKED: FUN_800215c0("cdrom:\\MOVIE.EXE;1") belongs to the next overlay slice.
    }

    // GHIDRA: FUN_800210a4 @ 0x800210A4
    private static void FUN_800210a4(ref UnkStruct_8009A594 param_1, short param_2,
        short param_3, short param_4, short param_5)
    {
        param_1.field_0x00 = unchecked((uint)DAT_8004C894_ADDRESS);
        param_1.field_0x04 = unchecked((uint)DAT_80072094_ADDRESS);
        param_1.field_0x0C = unchecked((uint)DAT_80097894_ADDRESS);
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

    // GHIDRA: FUN_80021118 @ 0x80021118
    private static void FUN_80021118(CdlLOC param_1, Action param_2)
    {
        DecDCTReset(0);
        DAT_8004c880 = 0;
        DecDCToutCallback(param_2);
        StSetRing(DAT_8009A5C4_ADDRESS, 0x20);
        StSetStream(1, 1, -1, null, null);
        FUN_80021574(param_1);
        DAT_8004c888.val0 = 0x80;
        DAT_8004c888.val1 = 0;
        DAT_8004c888.val2 = 0x80;
        DAT_8004c888.val3 = 0;
        CdMix(DAT_8004c888);
    }

    // GHIDRA: FUN_800211b0 @ 0x800211B0
    private static void FUN_800211b0()
    {
        if (DAT_800b1704 != 0)
        {
            StCdInterrupt();
            DAT_800b1704 = 0;
        }

        var rect = new RECT
        {
            x = DAT_8009a594.field_0x24,
            y = DAT_8009a594.field_0x26,
            w = DAT_8009a594.field_0x28,
            h = DAT_8009a594.field_0x2A,
        };
        LoadImage(rect, unchecked((int)DAT_8009a594.field_0x0C));
        DAT_8009a594.field_0x24 += DAT_8009a594.field_0x28;

        short targetX = DAT_8009a594.field_0x20 == 0
            ? DAT_8009a594.field_0x10
            : DAT_8009a594.field_0x18;
        short targetWidth = DAT_8009a594.field_0x20 == 0
            ? DAT_8009a594.field_0x14
            : DAT_8009a594.field_0x1C;
        if (DAT_8009a594.field_0x24 < targetX + targetWidth)
        {
            DecDCTout(
                unchecked((int)DAT_8009a594.field_0x0C),
                DAT_8009a594.field_0x28 * DAT_8009a594.field_0x2A / 2);
        }
        else
        {
            DAT_8009a594.field_0x2C = 1;
            DAT_8009a594.field_0x20 = DAT_8009a594.field_0x20 == 0 ? 1u : 0u;
            if (DAT_8009a594.field_0x20 == 0)
            {
                DAT_8009a594.field_0x24 = DAT_8009a594.field_0x10;
                DAT_8009a594.field_0x26 = DAT_8009a594.field_0x12;
            }
            else
            {
                DAT_8009a594.field_0x24 = DAT_8009a594.field_0x18;
                DAT_8009a594.field_0x26 = DAT_8009a594.field_0x1A;
            }
        }
    }

    // GHIDRA: FUN_800212e4 @ 0x800212E4
    private static int FUN_800212e4(ref UnkStruct_8009A594 param_1)
    {
        int timeout = 0x800000;
        do
        {
            int frameAddress = FUN_8002136c(ref param_1);
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

    // GHIDRA: FUN_8002136c @ 0x8002136C
    private static int FUN_8002136c(ref UnkStruct_8009A594 param_1)
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
                if (frameNumber > 0x59)
                {
                    DAT_8004c880 = 1;
                    DAT_8004c888.val0 = 0;
                    DAT_8004c888.val1 = 0;
                    DAT_8004c888.val2 = 0;
                    DAT_8004c888.val3 = 0;
                    CdMix(DAT_8004c888);
                    CdControlB(9, null, null);
                }

                ushort width = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0x10));
                ushort height = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0x12));
                if (DAT_8004c874 != width || DAT_8004c878 != height)
                {
                    ClearImage(new RECT { x = 0, y = 0, w = 0x280, h = 0x1e0 }, 0, 0, 0);
                    DAT_8004c874 = width;
                    DAT_8004c878 = height;
                }

                short vramWidth = (short)((DAT_8004c874 * 3) / 2);
                short frameHeight = (short)DAT_8004c878;
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

    // GHIDRA: FUN_800214b0 @ 0x800214B0
    private static void FUN_800214b0(ref UnkStruct_8009A594 param_1)
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

    // GHIDRA: FUN_80021574 @ 0x80021574
    private static void FUN_80021574(CdlLOC param_1)
    {
        while (CdControl(0x15, param_1, null) == 0)
        {
        }

        while (CdRead2(0x1c0) == 0)
        {
        }
    }

    // GHIDRA: __main @ 0x8002BFA8
    private static void __main()
    {
        // PARTIAL: compiler runtime initialization is provided by the CLR.
    }

    // GHIDRA: SetVolume @ 0x8002C98C
    private static void SetVolume(short volumeLeft, short volumeRight)
    {
        var attr = new SpuCommonAttr
        {
            mask = 3,
            mvol = new SpuVolume
            {
                left = (short)(volumeLeft * 0x81),
                right = (short)(volumeRight * 0x81),
            },
        };
        SpuSetCommonAttr(attr);
    }

    // GHIDRA: FUN_8002c80c @ 0x8002C80C
    private static void FUN_8002c80c()
    {
        FUN_8002c57c(1);
    }

    // GHIDRA: FUN_8002c57c @ 0x8002C57C
    private static void FUN_8002c57c(int param_1)
    {
        // BLOCKED: sound-sequencer timer/IRQ initialization is outside the video-only acceptance.
    }

    // GHIDRA: FUN_80035410 @ 0x80035410
    private static void FUN_80035410(byte param_1, byte param_2, uint param_3)
    {
        var attr = new SpuCommonAttr
        {
            cd = new SpuExtAttr { volume = new SpuVolume() },
            ext = new SpuExtAttr { volume = new SpuVolume() },
        };

        if (param_1 == 0)
        {
            if (param_2 == 0)
            {
                attr.mask = 0x200;
                attr.cd.mix = (int)(param_3 & 0xff);
            }
            if (param_2 == 1)
            {
                attr.mask = 0x100;
                attr.cd.reverb = (int)(param_3 & 0xff);
            }
        }
        if (param_1 == 1)
        {
            if (param_2 == 0)
            {
                attr.mask = 0x2000;
                attr.ext.mix = (int)(param_3 & 0xff);
            }
            if (param_2 == 1)
            {
                attr.mask = 0x1000;
                attr.ext.reverb = (int)(param_3 & 0xff);
            }
        }

        SpuSetCommonAttr(attr);
    }

    // GHIDRA: FUN_8002c9dc @ 0x8002C9DC
    private static void FUN_8002c9dc(byte param_1, short param_2, short param_3)
    {
        var attr = new SpuCommonAttr
        {
            cd = new SpuExtAttr { volume = new SpuVolume() },
            ext = new SpuExtAttr { volume = new SpuVolume() },
        };

        if (param_1 == 0)
        {
            attr.mask = 0xc0;
            if (param_2 > 0x7f) param_2 = 0x7f;
            if (param_3 > 0x7f) param_3 = 0x7f;
            attr.cd.volume.left = (short)(param_2 * 0x7fff / 0x7f);
            attr.cd.volume.right = (short)(param_3 * 0x7fff / 0x7f);
        }
        if (param_1 == 1)
        {
            attr.mask = 0xc00;
            if (param_2 > 0x7f) param_2 = 0x7f;
            if (param_3 > 0x7f) param_3 = 0x7f;
            attr.ext.volume.left = (short)(param_2 * 0x7fff / 0x7f);
            attr.ext.volume.right = (short)(param_3 * 0x7fff / 0x7f);
        }

        SpuSetCommonAttr(attr);
    }

    // GHIDRA: FUN_8002165c @ 0x8002165C
    private static void FUN_8002165c()
    {
        SHORT_ARRAY_801ff000[0x00] = 1;
        SHORT_ARRAY_801ff000[0x01] = 0;
        SHORT_ARRAY_801ff000[0x02] = 0;
        SHORT_ARRAY_801ff000[0x03] = 0;
        SHORT_ARRAY_801ff000[0x04] = 1;
        SHORT_ARRAY_801ff000[0x05] = 0;
        SHORT_ARRAY_801ff000[0x06] = 0;
        SHORT_ARRAY_801ff000[0x07] = 0;
        SHORT_ARRAY_801ff000[0x08] = 0;
        SHORT_ARRAY_801ff000[0x09] = 0;
        SHORT_ARRAY_801ff000[0x0A] = 0;
        SHORT_ARRAY_801ff000[0x0B] = 0;
        SHORT_ARRAY_801ff000[0x0C] = 1;
        SHORT_ARRAY_801ff000[0x0D] = 0;
        SHORT_ARRAY_801ff000[0x0E] = 0;
        SHORT_ARRAY_801ff000[0x0F] = 0;
        SHORT_ARRAY_801ff000[0x10] = 0x20;
        SHORT_ARRAY_801ff000[0x11] = 0x80;
        SHORT_ARRAY_801ff000[0x12] = 0x10;
        SHORT_ARRAY_801ff000[0x13] = 0x40;
        SHORT_ARRAY_801ff000[0x14] = 0x2000;
        SHORT_ARRAY_801ff000[0x15] = unchecked((short)0x8000);
        SHORT_ARRAY_801ff000[0x16] = 0x1000;
        SHORT_ARRAY_801ff000[0x17] = 0x4000;
        SHORT_ARRAY_801ff000[0x18] = 0x100;
        SHORT_ARRAY_801ff000[0x19] = 0x800;
        SHORT_ARRAY_801ff000[0x1A] = 8;
        SHORT_ARRAY_801ff000[0x1B] = 2;
        SHORT_ARRAY_801ff000[0x1C] = 4;
        SHORT_ARRAY_801ff000[0x1D] = 1;
        SHORT_ARRAY_801ff000[0x1E] = 0x20;
        SHORT_ARRAY_801ff000[0x1F] = 0x80;
        SHORT_ARRAY_801ff000[0x20] = 0x10;
        SHORT_ARRAY_801ff000[0x21] = 0x40;
        SHORT_ARRAY_801ff000[0x22] = 0x2000;
        SHORT_ARRAY_801ff000[0x23] = unchecked((short)0x8000);
        SHORT_ARRAY_801ff000[0x24] = 0x1000;
        SHORT_ARRAY_801ff000[0x25] = 0x4000;
        SHORT_ARRAY_801ff000[0x26] = 0x100;
        SHORT_ARRAY_801ff000[0x27] = 0x800;
        SHORT_ARRAY_801ff000[0x28] = 8;
        SHORT_ARRAY_801ff000[0x29] = 2;
        SHORT_ARRAY_801ff000[0x2A] = 4;
        SHORT_ARRAY_801ff000[0x2B] = 1;

        for (int index = 0x32, count = 0x0C; count >= 0; index -= 2, count -= 4)
        {
            SHORT_ARRAY_801ff000[index] = 0;
            SHORT_ARRAY_801ff000[index + 1] = 0;
        }

        SHORT_ARRAY_801ff000[0x34] = 0;
        SHORT_ARRAY_801ff000[0x35] = 0;
        for (int index = 0x80; index <= 0x87; index++)
        {
            SHORT_ARRAY_801ff000[index] = 0;
        }

        for (int i = 0; i < 3; i++)
        {
            int psVar3 = 0x100 + i * 4;
            int psVar2 = 0x103 + i * 4;
            int psVar1 = 0x113 + i * 8;
            SHORT_ARRAY_801ff000[psVar3] = 0;
            SHORT_ARRAY_801ff000[psVar2 - 2] = 0;
            SHORT_ARRAY_801ff000[psVar2 - 1] = 0;
            SHORT_ARRAY_801ff000[psVar2] = 0;
            for (int field = -7; field <= 0; field++)
            {
                SHORT_ARRAY_801ff000[psVar1 + field] = 0;
            }
        }
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: resolves the original PSX buffer addresses to their byte-array representations.
    internal static (byte[] Buffer, int Offset)? ResolveAddress(int address)
    {
        if (address >= DAT_8004C894_ADDRESS && address < DAT_8004C894_ADDRESS + DAT_8004c894.Length)
        {
            return (DAT_8004c894, address - DAT_8004C894_ADDRESS);
        }
        if (address >= DAT_80072094_ADDRESS && address < DAT_80072094_ADDRESS + DAT_80072094.Length)
        {
            return (DAT_80072094, address - DAT_80072094_ADDRESS);
        }
        if (address >= DAT_80097894_ADDRESS && address < DAT_80097894_ADDRESS + DAT_80097894.Length)
        {
            return (DAT_80097894, address - DAT_80097894_ADDRESS);
        }
        if (address >= DAT_8009A5C4_ADDRESS && address < DAT_8009A5C4_ADDRESS + DAT_8009a5c4.Length)
        {
            return (DAT_8009a5c4, address - DAT_8009A5C4_ADDRESS);
        }

        return null;
    }
}
