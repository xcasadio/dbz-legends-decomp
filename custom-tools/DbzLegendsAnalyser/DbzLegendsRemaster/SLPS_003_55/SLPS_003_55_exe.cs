using System;
using System.Buffers.Binary;
using DbzLegendsRemaster.MOVIE_EXE;
using DbzLegendsRemaster.Types;
using PsxSdkMonogame;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.LibPress;
using static PsxSdkMonogame.LibSpu;

namespace DbzLegendsRemaster.SLPS_003_55;

internal sealed class SLPS_003_55_exe
{
    private const int MovieVlcBuffer0Address = unchecked((int)0x8004C894);
    private const int MovieVlcBuffer1Address = unchecked((int)0x80072094);
    private const int MovieMdecOutputBufferAddress = unchecked((int)0x80097894);
    private const int MovieStreamRingAddress = unchecked((int)0x8009A5C4);

    // GHIDRA: g_MovieFrameWidth @ 0x8004C874
    private static uint g_MovieFrameWidth;

    // GHIDRA: g_MovieFrameHeight @ 0x8004C878
    private static uint g_MovieFrameHeight;

    // GHIDRA: g_MovieStartLocation @ 0x8004C87C
    private static readonly CdlLOC g_MovieStartLocation = new();

    // GHIDRA: g_MovieStatus @ 0x8004C880
    private static int g_MovieStatus;

    // GHIDRA: g_MovieCdAudioMix @ 0x8004C888
    private static readonly CdlATV g_MovieCdAudioMix = new();

    // GHIDRA: g_MovieEndCountdown @ 0x8004C890
    private static int g_MovieEndCountdown;

    // GHIDRA: g_MovieVlcBuffer0 @ 0x8004C894
    internal static readonly byte[] g_MovieVlcBuffer0 = new byte[0x25800];

    // GHIDRA: g_MovieVlcBuffer1 @ 0x80072094
    internal static readonly byte[] g_MovieVlcBuffer1 = new byte[0x25800];

    // GHIDRA: g_MovieMdecOutputBuffer @ 0x80097894
    internal static readonly byte[] g_MovieMdecOutputBuffer = new byte[0x2D00];

    // GHIDRA: g_MoviePlayback @ 0x8009A594
    private static MoviePlaybackState g_MoviePlayback;

    // GHIDRA: g_MovieStreamRing @ 0x8009A5C4
    internal static readonly byte[] g_MovieStreamRing = new byte[0x10000];

    // GHIDRA: g_StCdInterruptPending @ 0x800B1704
    private static int g_StCdInterruptPending = 0;

    // GHIDRA: SHORT_ARRAY_801ff000 @ 0x801FF000
    // Held in SharedHighRam rather than here: 0x801FF000 is high RAM that no overlay segment
    // covers, so it survives LoadExec, and TITLE.EXE reads back the button-remap tables that
    // FUN_8002165c writes into it. See SharedHighRam for the addressing that proves it.
    private static readonly short[] SHORT_ARRAY_801ff000 = SharedHighRam.SHORT_ARRAY_801ff000;

    // GHIDRA: DAT_801fff00 @ 0x801FFF00
    // PARTIAL: only the address of this global reaches LoadExec; its contents are never read here.
    private const int DAT_801fff00 = unchecked((int)0x801FFF00);

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
        SetSpuInputAttribute(0, 0, 1);
        SetSpuInputVolume(0, 0x3f, 0x3f);
        FntLoad(0x3c0, 0x100);
        int fontId = FntOpen(0x20, 0x20, 0x140, 200, 0, 0x200);
        SetDumpFnt(fontId);
        g_MovieEndCountdown = 0x1e;
        FUN_8002165c();
        do
        {
            PlayBandaiMovie();
        } while (true);
    }

    // GHIDRA: PlayBandaiMovie @ 0x80020DE8
    private static void PlayBandaiMovie()
    {
        CdlFILE movieFile;
        // PARTIAL: the managed CdSearchFile adapter represents failure as null; it has no pointer
        // value corresponding to the original secondary (CdlFILE *)-1 sentinel.
        do
        {
            movieFile = CdSearchFile(new CdlFILE(), "\\MOVIE\\BANDAI.STR;1".ToCharArray());
        } while (movieFile == null);

        g_MovieStartLocation.minute = movieFile.pos.minute;
        g_MovieStartLocation.second = movieFile.pos.second;
        g_MovieStartLocation.sector = movieFile.pos.sector;
        InitializeMoviePlaybackState(ref g_MoviePlayback, 0, 0, 0, 0xf0);
        StartMovieStream(g_MovieStartLocation, MovieMdecOutputCallback);
        DecodeNextMovieFrameVlc(ref g_MoviePlayback);

        uint pad;
        do
        {
            if (g_MovieStatus == 0)
            {
                uint bufferAddress = g_MoviePlayback.vlcBufferIndex == 0
                    ? g_MoviePlayback.vlcBuffer0
                    : g_MoviePlayback.vlcBuffer1;
                DecDCTin(unchecked((int)bufferAddress), 3);
                DecDCTout(
                    unchecked((int)g_MoviePlayback.mdecOutputBuffer),
                    g_MoviePlayback.mdecOutputRect.w * g_MoviePlayback.mdecOutputRect.h / 2);
                DecodeNextMovieFrameVlc(ref g_MoviePlayback);
                WaitForMovieFrameUpload(ref g_MoviePlayback);
            }

            VSync(4);

            bool displaySecondRect = g_MoviePlayback.writeBufferIndex == 0;
            short x = displaySecondRect ? g_MoviePlayback.frameBuffer1Rect.x : g_MoviePlayback.frameBuffer0Rect.x;
            short y = displaySecondRect ? g_MoviePlayback.frameBuffer1Rect.y : g_MoviePlayback.frameBuffer0Rect.y;
            short w = displaySecondRect ? g_MoviePlayback.frameBuffer1Rect.w : g_MoviePlayback.frameBuffer0Rect.w;
            short h = displaySecondRect ? g_MoviePlayback.frameBuffer1Rect.h : g_MoviePlayback.frameBuffer0Rect.h;

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
            if (g_MovieStatus == 3)
            {
                break;
            }

            if (g_MovieStatus == 1)
            {
                g_MovieEndCountdown--;
                if (g_MovieEndCountdown == -1 || (pad & 0x800) != 0)
                {
                    break;
                }
            }
        } while ((pad & 0x800) == 0);

        SetDispMask(0);
        ShutdownAndLoadExecutable("cdrom:\\MOVIE.EXE;1");
    }

    // GHIDRA: ShutdownAndLoadExecutable @ 0x800215C0
    private static void ShutdownAndLoadExecutable(string exeFileName)
    {
        StopRCnt(unchecked((long)0xf2000000));
        StopRCnt(unchecked((long)0xf2000001));
        StopRCnt(unchecked((long)0xf2000002));
        StopRCnt(unchecked((long)0xf2000003));
        PadStop();
        FUN_8002c84c();
        FUN_8002c8f0();
        ResetGraph(0);
        CdFlush();
        StopCallback();
        _96_init();
        LoadExec(exeFileName, DAT_801fff00, 0);
    }

    // GHIDRA: FUN_8002c84c @ 0x8002C84C
    private static void FUN_8002c84c()
    {
        // PARTIAL: the paired sound-sequencer timer initialization remains BLOCKED, so no timer
        // or VSync callback exists to unregister in this slice.
    }

    // GHIDRA: FUN_8002c8f0 @ 0x8002C8F0
    private static void FUN_8002c8f0()
    {
        // PARTIAL: this wrapper calls FUN_800378c8, whose body tears down the libspu transfer
        // callback/event. The paired transfer-event initialization is not modeled in this slice;
        // the continuously running desktop SPU mixer is a separate hardware adaptation.
    }

    // GHIDRA: _96_init @ 0x800218AC
    private static void _96_init()
    {
        // PARTIAL: compiler overlay runtime initialization is provided by the CLR.
    }

    // GHIDRA: LoadExec @ 0x800218BC
    // PARTIAL: the Ghidra prototype is void LoadExec(char *, u_long, u_long). The semantics of the
    // two stack arguments are not closed, so they keep raw names. The desktop adapter handles the
    // only path currently proven at this call site.
    private static void LoadExec(string exeFileName, int param_2, int param_3)
    {
        // JUSTIFICATION: PSX hardware adaptation only
        // RELATION: the drive spends real time fetching the overlay; see LibCd.WaitDiscLoad for
        // the measurements this reproduces. Without it the next overlay starts polling the pad
        // 66 ms after this one stopped, and one Start press skips both startup movies.
        WaitDiscLoad(exeFileName);

        if (string.Equals(exeFileName, "cdrom:\\MOVIE.EXE;1", StringComparison.Ordinal))
        {
            PsxSdkBridges.ActivateMovieExe();
            new MOVIE_EXE_exe().Main();
        }

        // JUSTIFICATION: PSX hardware adaptation only
        // RELATION: A0(0x51) replaces the resident executable and transfers control permanently, so
        // it never returns to its caller. Returning here would resume the original's unreachable
        // code and re-enter the caller's do/while(true) loop.
        throw new LoadExecTransferException();
    }

    // GHIDRA: InitializeMoviePlaybackState @ 0x800210A4
    private static void InitializeMoviePlaybackState(ref MoviePlaybackState state, short frameBuffer0X,
        short frameBuffer0Y, short frameBuffer1X, short frameBuffer1Y)
    {
        state.vlcBuffer0 = unchecked((uint)MovieVlcBuffer0Address);
        state.vlcBuffer1 = unchecked((uint)MovieVlcBuffer1Address);
        state.mdecOutputBuffer = unchecked((uint)MovieMdecOutputBufferAddress);
        state.vlcBufferIndex = 0;
        state.writeBufferIndex = 0;
        state.frameUploadComplete = 0;
        state.frameBuffer0Rect.x = frameBuffer0X;
        state.frameBuffer0Rect.y = frameBuffer0Y;
        state.frameBuffer0Rect.w = 0x3c0;
        state.frameBuffer0Rect.h = 0xf0;
        state.frameBuffer1Rect.x = frameBuffer1X;
        state.frameBuffer1Rect.y = frameBuffer1Y;
        state.frameBuffer1Rect.w = 0x3c0;
        state.frameBuffer1Rect.h = 0xf0;
        state.mdecOutputRect.x = frameBuffer0X;
        state.mdecOutputRect.y = frameBuffer0Y;
        state.mdecOutputRect.w = 0x18;
        state.mdecOutputRect.h = 0xf0;
    }

    // GHIDRA: StartMovieStream @ 0x80021118
    private static void StartMovieStream(CdlLOC startLocation, Action mdecOutputCallback)
    {
        DecDCTReset(0);
        g_MovieStatus = 0;
        DecDCToutCallback(mdecOutputCallback);
        StSetRing(MovieStreamRingAddress, 0x20);
        StSetStream(1, 1, -1, null, null);
        SeekAndStartMovieStream(startLocation);
        g_MovieCdAudioMix.val0 = 0x80;
        g_MovieCdAudioMix.val1 = 0;
        g_MovieCdAudioMix.val2 = 0x80;
        g_MovieCdAudioMix.val3 = 0;
        CdMix(g_MovieCdAudioMix);
    }

    // GHIDRA: MovieMdecOutputCallback @ 0x800211B0
    private static void MovieMdecOutputCallback()
    {
        if (g_StCdInterruptPending != 0)
        {
            StCdInterrupt();
            g_StCdInterruptPending = 0;
        }

        var rect = new LibGpu.RECT
        {
            x = g_MoviePlayback.mdecOutputRect.x,
            y = g_MoviePlayback.mdecOutputRect.y,
            w = g_MoviePlayback.mdecOutputRect.w,
            h = g_MoviePlayback.mdecOutputRect.h,
        };
        LoadImage(rect, unchecked((int)g_MoviePlayback.mdecOutputBuffer));
        g_MoviePlayback.mdecOutputRect.x += g_MoviePlayback.mdecOutputRect.w;

        short targetX = g_MoviePlayback.writeBufferIndex == 0
            ? g_MoviePlayback.frameBuffer0Rect.x
            : g_MoviePlayback.frameBuffer1Rect.x;
        short targetWidth = g_MoviePlayback.writeBufferIndex == 0
            ? g_MoviePlayback.frameBuffer0Rect.w
            : g_MoviePlayback.frameBuffer1Rect.w;
        if (g_MoviePlayback.mdecOutputRect.x < targetX + targetWidth)
        {
            DecDCTout(
                unchecked((int)g_MoviePlayback.mdecOutputBuffer),
                g_MoviePlayback.mdecOutputRect.w * g_MoviePlayback.mdecOutputRect.h / 2);
        }
        else
        {
            g_MoviePlayback.frameUploadComplete = 1;
            g_MoviePlayback.writeBufferIndex = g_MoviePlayback.writeBufferIndex == 0 ? 1u : 0u;
            if (g_MoviePlayback.writeBufferIndex == 0)
            {
                g_MoviePlayback.mdecOutputRect.x = g_MoviePlayback.frameBuffer0Rect.x;
                g_MoviePlayback.mdecOutputRect.y = g_MoviePlayback.frameBuffer0Rect.y;
            }
            else
            {
                g_MoviePlayback.mdecOutputRect.x = g_MoviePlayback.frameBuffer1Rect.x;
                g_MoviePlayback.mdecOutputRect.y = g_MoviePlayback.frameBuffer1Rect.y;
            }
        }
    }

    // GHIDRA: DecodeNextMovieFrameVlc @ 0x800212E4
    private static int DecodeNextMovieFrameVlc(ref MoviePlaybackState state)
    {
        int timeout = 0x800000;
        do
        {
            int frameAddress = GetNextMovieFrame(ref state);
            timeout--;
            if (frameAddress != 0)
            {
                state.vlcBufferIndex = state.vlcBufferIndex == 0 ? 1u : 0u;
                uint bufferAddress = state.vlcBufferIndex == 0 ? state.vlcBuffer0 : state.vlcBuffer1;
                DecDCTvlc(frameAddress, unchecked((int)bufferAddress));
                StFreeRing(frameAddress);
                return 0;
            }
        } while (timeout != 0);

        return -1;
    }

    // GHIDRA: GetNextMovieFrame @ 0x8002136C
    private static int GetNextMovieFrame(ref MoviePlaybackState state)
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
                    g_MovieStatus = 1;
                    g_MovieCdAudioMix.val0 = 0;
                    g_MovieCdAudioMix.val1 = 0;
                    g_MovieCdAudioMix.val2 = 0;
                    g_MovieCdAudioMix.val3 = 0;
                    CdMix(g_MovieCdAudioMix);
                    CdControlB(9, null, null);
                }

                ushort width = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0x10));
                ushort height = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0x12));
                if (g_MovieFrameWidth != width || g_MovieFrameHeight != height)
                {
                    ClearImage(new LibGpu.RECT { x = 0, y = 0, w = 0x280, h = 0x1e0 }, 0, 0, 0);
                    g_MovieFrameWidth = width;
                    g_MovieFrameHeight = height;
                }

                short vramWidth = (short)((g_MovieFrameWidth * 3) / 2);
                short frameHeight = (short)g_MovieFrameHeight;
                state.frameBuffer0Rect.w = vramWidth;
                state.frameBuffer1Rect.w = vramWidth;
                state.frameBuffer0Rect.h = frameHeight;
                state.frameBuffer1Rect.h = frameHeight;
                state.mdecOutputRect.h = frameHeight;
                return frameAddress;
            }
        } while (timeout != 0);

        return 0;
    }

    // GHIDRA: WaitForMovieFrameUpload @ 0x800214B0
    private static void WaitForMovieFrameUpload(ref MoviePlaybackState state)
    {
        int timeout = 0x800000;
        while (state.frameUploadComplete == 0)
        {
            timeout--;
            if (timeout == 0)
            {
                Console.WriteLine("time out in decoding !");
                state.frameUploadComplete = 1;
                state.writeBufferIndex = state.writeBufferIndex == 0 ? 1u : 0u;
                if (state.writeBufferIndex == 0)
                {
                    state.mdecOutputRect.x = state.frameBuffer0Rect.x;
                    state.mdecOutputRect.y = state.frameBuffer0Rect.y;
                }
                else
                {
                    state.mdecOutputRect.x = state.frameBuffer1Rect.x;
                    state.mdecOutputRect.y = state.frameBuffer1Rect.y;
                }
            }
        }

        state.frameUploadComplete = 0;
    }

    // GHIDRA: SeekAndStartMovieStream @ 0x80021574
    private static void SeekAndStartMovieStream(CdlLOC startLocation)
    {
        while (CdControl(0x15, startLocation, null) == 0)
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

    // GHIDRA: SetSpuInputAttribute @ 0x80035410
    private static void SetSpuInputAttribute(byte inputIndex, byte attributeIndex, uint value)
    {
        var attr = new SpuCommonAttr
        {
            cd = new SpuExtAttr { volume = new SpuVolume() },
            ext = new SpuExtAttr { volume = new SpuVolume() },
        };

        if (inputIndex == 0)
        {
            if (attributeIndex == 0)
            {
                attr.mask = 0x200;
                attr.cd.mix = (int)(value & 0xff);
            }
            if (attributeIndex == 1)
            {
                attr.mask = 0x100;
                attr.cd.reverb = (int)(value & 0xff);
            }
        }
        if (inputIndex == 1)
        {
            if (attributeIndex == 0)
            {
                attr.mask = 0x2000;
                attr.ext.mix = (int)(value & 0xff);
            }
            if (attributeIndex == 1)
            {
                attr.mask = 0x1000;
                attr.ext.reverb = (int)(value & 0xff);
            }
        }

        SpuSetCommonAttr(attr);
    }

    // GHIDRA: SetSpuInputVolume @ 0x8002C9DC
    private static void SetSpuInputVolume(byte inputIndex, short leftVolume, short rightVolume)
    {
        var attr = new SpuCommonAttr
        {
            cd = new SpuExtAttr { volume = new SpuVolume() },
            ext = new SpuExtAttr { volume = new SpuVolume() },
        };

        if (inputIndex == 0)
        {
            attr.mask = 0xc0;
            if (leftVolume > 0x7f) leftVolume = 0x7f;
            if (rightVolume > 0x7f) rightVolume = 0x7f;
            attr.cd.volume.left = (short)(leftVolume * 0x7fff / 0x7f);
            attr.cd.volume.right = (short)(rightVolume * 0x7fff / 0x7f);
        }
        if (inputIndex == 1)
        {
            attr.mask = 0xc00;
            if (leftVolume > 0x7f) leftVolume = 0x7f;
            if (rightVolume > 0x7f) rightVolume = 0x7f;
            attr.ext.volume.left = (short)(leftVolume * 0x7fff / 0x7f);
            attr.ext.volume.right = (short)(rightVolume * 0x7fff / 0x7f);
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
        if (address >= MovieVlcBuffer0Address && address < MovieVlcBuffer0Address + g_MovieVlcBuffer0.Length)
        {
            return (g_MovieVlcBuffer0, address - MovieVlcBuffer0Address);
        }
        if (address >= MovieVlcBuffer1Address && address < MovieVlcBuffer1Address + g_MovieVlcBuffer1.Length)
        {
            return (g_MovieVlcBuffer1, address - MovieVlcBuffer1Address);
        }
        if (address >= MovieMdecOutputBufferAddress && address < MovieMdecOutputBufferAddress + g_MovieMdecOutputBuffer.Length)
        {
            return (g_MovieMdecOutputBuffer, address - MovieMdecOutputBufferAddress);
        }
        if (address >= MovieStreamRingAddress && address < MovieStreamRingAddress + g_MovieStreamRing.Length)
        {
            return (g_MovieStreamRing, address - MovieStreamRingAddress);
        }

        return null;
    }
}
