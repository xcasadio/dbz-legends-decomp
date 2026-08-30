using System;
using System.Buffers.Binary;
using DbzLegendsRemaster.Types;
using PsxSdkMonogame;
using static PsxSdkMonogame.LibApi;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibEtc;
using static PsxSdkMonogame.LibGpu;
using static PsxSdkMonogame.LibPress;

namespace DbzLegendsRemaster.MOVIE_EXE;

internal sealed class MOVIE_EXE_exe
{
    private const int MovieVlcBuffer0Address = unchecked((int)0x8003FF30);
    private const int MovieVlcBuffer1Address = unchecked((int)0x80065730);
    private const int MovieMdecOutputBufferAddress = unchecked((int)0x8008AF30);
    private const int MovieStreamRingAddress = unchecked((int)0x8008DC60);

    // GHIDRA: g_MovieFrameWidth @ 0x8003FF10
    private static uint g_MovieFrameWidth;

    // GHIDRA: g_MovieFrameHeight @ 0x8003FF14
    private static uint g_MovieFrameHeight;

    // GHIDRA: g_MovieStartLocation @ 0x8003FF18
    private static readonly CdlLOC g_MovieStartLocation = new();

    // GHIDRA: g_MovieStatus @ 0x8003FF1C
    private static int g_MovieStatus;

    // GHIDRA: g_MovieCdAudioMix @ 0x8003FF24
    private static readonly CdlATV g_MovieCdAudioMix = new();

    // GHIDRA: g_MovieEndCountdown @ 0x8003FF2C
    private static int g_MovieEndCountdown;

    // GHIDRA: g_MovieVlcBuffer0 @ 0x8003FF30
    internal static readonly byte[] g_MovieVlcBuffer0 = new byte[0x25800];

    // GHIDRA: g_MovieVlcBuffer1 @ 0x80065730
    internal static readonly byte[] g_MovieVlcBuffer1 = new byte[0x25800];

    // GHIDRA: g_MovieMdecOutputBuffer @ 0x8008AF30
    internal static readonly byte[] g_MovieMdecOutputBuffer = new byte[0x2D00];

    // GHIDRA: g_MoviePlayback @ 0x8008DC30
    private static MoviePlaybackState g_MoviePlayback;

    // GHIDRA: g_MovieStreamRing @ 0x8008DC60
    internal static readonly byte[] g_MovieStreamRing = new byte[0x10000];

    // GHIDRA: g_StCdInterruptPending @ 0x800A45F4
    private static int g_StCdInterruptPending;

    // GHIDRA: DAT_801fff00 @ 0x801FFF00
    // PARTIAL: only the address of this global reaches LoadExec; its contents are never read here.
    private const int DAT_801fff00 = unchecked((int)0x801FFF00);

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
        int fontId = FntOpen(0x20, 0x20, 0x140, 200, 0, 0x200);
        SetDumpFnt(fontId);
        g_MovieEndCountdown = 0x1e;
        do
        {
            PlayDbzOpeningMovie();
        } while (true);
    }

    // GHIDRA: PlayDbzOpeningMovie @ 0x80020A90
    private static void PlayDbzOpeningMovie()
    {
        CdlFILE movieFile;
        // PARTIAL: the managed CdSearchFile adapter represents failure as null; it has no pointer
        // value corresponding to the original secondary (CdlFILE *)-1 sentinel.
        do
        {
            movieFile = CdSearchFile(new CdlFILE(), "\\MOVIE\\DBZ_OP.STR;1".ToCharArray());
        } while (movieFile == null);

        g_MovieStartLocation.minute = movieFile.pos.minute;
        g_MovieStartLocation.second = movieFile.pos.second;
        g_MovieStartLocation.sector = movieFile.pos.sector;
        InitializeMoviePlaybackState(ref g_MoviePlayback, 0, 0, 0, 0xf0);
        StartMovieStream(g_MovieStartLocation, MovieMdecOutputCallback);
        DecodeNextMovieFrameVlc(ref g_MoviePlayback);

        while (true)
        {
            if (g_MovieStatus == 0)
            {
                uint bufferAddress = g_MoviePlayback.vlcBufferIndex == 0
                    ? g_MoviePlayback.vlcBuffer0
                    : g_MoviePlayback.vlcBuffer1;
                DecDCTin(unchecked((int)bufferAddress), 1);
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

            if (g_MovieStatus == 3)
            {
                break;
            }

            if (g_MovieStatus == 1)
            {
                g_MovieEndCountdown--;
                if (g_MovieEndCountdown == -1 || (PadRead(1) & 0x800) != 0)
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
        ShutdownAndLoadExecutable("cdrom:\\TITLE.EXE;1");
    }

    // GHIDRA: InitializeMoviePlaybackState @ 0x80020D58
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

    // GHIDRA: StartMovieStream @ 0x80020DCC
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

    // GHIDRA: MovieMdecOutputCallback @ 0x80020E64
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

    // GHIDRA: DecodeNextMovieFrameVlc @ 0x80020F98
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

    // GHIDRA: GetNextMovieFrame @ 0x80021020
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
                if (frameNumber > 0x3a1)
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

    // GHIDRA: WaitForMovieFrameUpload @ 0x80021164
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

    // GHIDRA: SeekAndStartMovieStream @ 0x80021228
    private static void SeekAndStartMovieStream(CdlLOC startLocation)
    {
        while (CdControl(0x15, startLocation, null) == 0)
        {
        }

        while (CdRead2(0x1c0) == 0)
        {
        }
    }

    // GHIDRA: ShutdownAndLoadExecutable @ 0x80021274
    private static void ShutdownAndLoadExecutable(string exeFileName)
    {
        StopRCnt(unchecked((long)0xf2000000));
        StopRCnt(unchecked((long)0xf2000001));
        StopRCnt(unchecked((long)0xf2000002));
        StopRCnt(unchecked((long)0xf2000003));
        PadStop();
        ResetGraph(0);
        CdFlush();
        StopCallback();
        _96_init();
        LoadExec(exeFileName, DAT_801fff00, 0);
    }

    // GHIDRA: _96_init @ 0x80021300
    private static void _96_init()
    {
        // PARTIAL: compiler overlay runtime initialization is provided by the CLR.
    }

    // GHIDRA: LoadExec @ 0x80021310
    // PARTIAL: the BIOS A0(0x51) prototype is not closed in Ghidra, so the two stack arguments
    // keep raw names. No overlay is wired behind this call site yet.
    private static void LoadExec(string exeFileName, int param_2, int param_3)
    {
        // JUSTIFICATION: PSX hardware adaptation only
        // RELATION: A0(0x51) replaces the resident executable and transfers control permanently, so
        // it never returns to its caller. Returning here would resume the original's unreachable
        // code and re-enter the caller's do/while(true) loop.
        throw new LoadExecTransferException();
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