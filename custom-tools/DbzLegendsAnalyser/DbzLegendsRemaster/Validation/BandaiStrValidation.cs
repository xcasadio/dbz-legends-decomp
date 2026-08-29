using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DbzLegendsRemaster.Types;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: validation tooling only; this is not part of the transliterated runtime.
internal static class BandaiStrValidation
{
    private const int RawSectorSize = 2352;
    private const int UserDataOffset = 24;
    private const int StrHeaderSize = 32;
    private const int StrPayloadSize = 2016;
    private const uint VideoSectorMagic = 0x80010160;
    private const int MemoryBaseAddress = unchecked((int)0x80010000);
    private const int MemorySize = 0x60000;
    private const int RingAddress = unchecked((int)0x80010000);
    private const int RingSlots = 0x20;
    private const int RleAddress = unchecked((int)0x80030000);
    private const int RleCapacityBytes = 0x20000;
    private const int StripAddress = unchecked((int)0x80060000);

    private sealed class MovieSpec
    {
        public string LogPrefix;
        public string TempFilePrefix;
        public string IsoPath;
        public int ExpectedTotalSectors;
        public int ExpectedVideoSectors;
        public int ExpectedAudioSectors;
        public int ExpectedOtherSectors;
        public int ExpectedFrames;
        public int LastFrameBeforeStop;
        public int MdecMode;
        public int[] SampleFrames;
        public int[] ExportFrames;
    }

    private sealed class RawFrameInfo
    {
        public int ChunkCount;
        public uint DemuxSize;
        public readonly HashSet<int> SeenChunks = new();
    }

    private static readonly MovieSpec BandaiSpec = new()
    {
        LogPrefix = "bandai-str",
        TempFilePrefix = "dbz-bandai",
        IsoPath = "\\MOVIE\\BANDAI.STR;1",
        ExpectedTotalSectors = 911,
        ExpectedVideoSectors = 788,
        ExpectedAudioSectors = 113,
        ExpectedOtherSectors = 10,
        ExpectedFrames = 90,
        LastFrameBeforeStop = 89,
        MdecMode = 3,
        SampleFrames = new[] { 1, 8, 30, 89 },
        ExportFrames = new[] { 8, 89 },
    };

    private static readonly MovieSpec DbzOpSpec = new()
    {
        LogPrefix = "dbz-op-str",
        TempFilePrefix = "dbz-op",
        IsoPath = "\\MOVIE\\DBZ_OP.STR;1",
        ExpectedTotalSectors = 9479,
        ExpectedVideoSectors = 8269,
        ExpectedAudioSectors = 1184,
        ExpectedOtherSectors = 26,
        ExpectedFrames = 945,
        LastFrameBeforeStop = 929,
        MdecMode = 1,
        SampleFrames = new[] { 1, 300, 600, 929 },
        ExportFrames = new[] { 600, 929 },
    };

    // JUSTIFICATION: validation tooling only; exercises the existing decoder against one real DBZ frame.
    internal static int Run(string path)
    {
        if (!ValidateRuntimeStructure(out string layoutError))
        {
            Console.Error.WriteLine($"[bandai-str] runtime layout failed: {layoutError}");
            return 1;
        }

        return RunMovie(path, BandaiSpec);
    }

    // JUSTIFICATION: validation tooling only; validates the second original FMV overlay input.
    internal static int RunDbzOp(string path)
    {
        if (!ValidateMovieRuntimeStructure(out string layoutError))
        {
            Console.Error.WriteLine($"[dbz-op-str] runtime layout failed: {layoutError}");
            return 1;
        }

        return RunMovie(path, DbzOpSpec);
    }

    // JUSTIFICATION: validation tooling only; locks the MOVIE.EXE C# mirror to its Ghidra layout.
    private static bool ValidateMovieRuntimeStructure(out string error)
    {
        error = null;
        if (Marshal.SizeOf<MoviePlaybackState>() != 0x30 || Marshal.SizeOf<RECT>() != 0x8)
        {
            error = $"state/RECT sizes are 0x{Marshal.SizeOf<MoviePlaybackState>():X}/" +
                $"0x{Marshal.SizeOf<RECT>():X}, expected 0x30/0x8";
            return false;
        }

        (string Name, int Expected)[] fields =
        {
            (nameof(MoviePlaybackState.vlcBuffer0), 0x00),
            (nameof(MoviePlaybackState.vlcBuffer1), 0x04),
            (nameof(MoviePlaybackState.vlcBufferIndex), 0x08),
            (nameof(MoviePlaybackState.mdecOutputBuffer), 0x0C),
            (nameof(MoviePlaybackState.frameBuffer0Rect), 0x10),
            (nameof(MoviePlaybackState.frameBuffer1Rect), 0x18),
            (nameof(MoviePlaybackState.writeBufferIndex), 0x20),
            (nameof(MoviePlaybackState.mdecOutputRect), 0x24),
            (nameof(MoviePlaybackState.frameUploadComplete), 0x2C),
        };

        foreach ((string name, int expected) in fields)
        {
            int actual = Marshal.OffsetOf<MoviePlaybackState>(name).ToInt32();
            if (actual != expected)
            {
                error = $"{name} is at 0x{actual:X}, expected 0x{expected:X}";
                return false;
            }
        }

        return true;
    }

    // JUSTIFICATION: validation tooling only; verifies XA decoder state across an overlay file switch.
    internal static int RunXaTransition(string bandaiPath, string dbzOpPath)
    {
        if (!File.Exists(bandaiPath) || !File.Exists(dbzOpPath))
        {
            Console.Error.WriteLine("[xa-transition] one or both movie files are missing");
            return 1;
        }

        var memory = new byte[MemorySize];
        string fullBandaiPath = Path.GetFullPath(bandaiPath);
        string fullDbzOpPath = Path.GetFullPath(dbzOpPath);
        PsxRam.AddressResolver = address =>
        {
            int offset = address - MemoryBaseAddress;
            return offset >= 0 && offset < memory.Length ? (memory, offset) : null;
        };
        LibDs.DiscFileResolver = isoPath =>
        {
            if (string.Equals(isoPath, BandaiSpec.IsoPath, StringComparison.OrdinalIgnoreCase))
            {
                return fullBandaiPath;
            }
            if (string.Equals(isoPath, DbzOpSpec.IsoPath, StringComparison.OrdinalIgnoreCase))
            {
                return fullDbzOpPath;
            }
            return null;
        };

        XaAudio.Flush();
        if (!CaptureFirstFrameAudio(DbzOpSpec.IsoPath, out byte[] cleanPcm, out string error))
        {
            Console.Error.WriteLine($"[xa-transition] clean DBZ_OP capture failed: {error}");
            return 1;
        }
        LibCd.StUnSetRing();

        if (!OpenStream(BandaiSpec.IsoPath, out error))
        {
            Console.Error.WriteLine($"[xa-transition] Bandai open failed: {error}");
            return 1;
        }

        for (int frame = 1; frame <= BandaiSpec.ExpectedFrames; frame++)
        {
            if (LibCd.StGetNext(out int frameAddress, out _) != 0 || LibCd.StFreeRing(frameAddress) != 0)
            {
                Console.Error.WriteLine($"[xa-transition] Bandai frame {frame} was not delivered/freed");
                LibCd.StUnSetRing();
                return 1;
            }
        }

        LibCd.CdControlB(9, null, null);
        XaAudio.DrainAllForTest(new short[88200 * 2]);

        if (!CaptureFirstFrameAudio(DbzOpSpec.IsoPath, out byte[] chainedPcm, out error))
        {
            Console.Error.WriteLine($"[xa-transition] chained DBZ_OP capture failed: {error}");
            LibCd.StUnSetRing();
            return 1;
        }
        LibCd.StUnSetRing();

        string cleanHash = Convert.ToHexString(SHA256.HashData(cleanPcm));
        string chainedHash = Convert.ToHexString(SHA256.HashData(chainedPcm));
        bool equal = cleanPcm.AsSpan().SequenceEqual(chainedPcm);
        Console.WriteLine(
            $"[xa-transition] clean={cleanHash}, chained={chainedHash}, equal={equal}, bytes={cleanPcm.Length}");
        if (!equal)
        {
            return 1;
        }

        if (!ValidateSameFileSeekPreservesXa(DbzOpSpec.IsoPath, out error))
        {
            Console.Error.WriteLine($"[xa-transition] same-file seek failed: {error}");
            return 1;
        }

        return 0;
    }

    // JUSTIFICATION: validation tooling only; guards predictor continuity for a same-file recovery seek.
    private static bool ValidateSameFileSeekPreservesXa(string isoPath, out string error)
    {
        error = null;
        XaAudio.Flush();
        if (!OpenStream(isoPath, out error) ||
            LibCd.StGetNext(out int frameAddress, out _) != 0 ||
            LibCd.StFreeRing(frameAddress) != 0)
        {
            error ??= "could not consume the first frame before the seek";
            LibCd.StUnSetRing();
            return false;
        }

        int submittedBeforeSeek = XaAudio.SubmittedSectorCountForTest;
        var resume = new LibCd.CdlLOC();
        LibCd.StGetBackloc(resume);
        LibCd.CdControlB(9, null, null);
        LibCd.StSetRing(RingAddress, RingSlots);
        LibCd.StSetStream(1, 2, -1, null, null);
        bool armed = LibCd.CdControl(0x15, resume, null) == 1 && LibCd.CdRead2(0x1c0) == 1;
        int submittedAfterArm = XaAudio.SubmittedSectorCountForTest;
        LibCd.StUnSetRing();

        if (!armed || submittedBeforeSeek <= 0 || submittedAfterArm != submittedBeforeSeek)
        {
            error = $"armed={armed}, XA sectors before/after={submittedBeforeSeek}/{submittedAfterArm}";
            return false;
        }

        Console.WriteLine(
            $"[xa-transition] same-file seek preserved XA state at {submittedBeforeSeek} submitted sector(s)");
        return true;
    }

    // JUSTIFICATION: validation tooling only; opens one SDK stream and captures its first XA packet.
    private static bool CaptureFirstFrameAudio(string isoPath, out byte[] pcm, out string error)
    {
        pcm = null;
        if (!OpenStream(isoPath, out error))
        {
            return false;
        }

        if (LibCd.StGetNext(out int frameAddress, out _) != 0)
        {
            error = "first video frame was not delivered";
            return false;
        }
        LibCd.StFreeRing(frameAddress);

        var samples = new short[4096 * 2];
        int frames = XaAudio.DrainAllForTest(samples);
        if (frames <= 0)
        {
            error = "no XA frames were decoded before the first video frame completed";
            return false;
        }

        pcm = new byte[frames * 4];
        Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
        return true;
    }

    // JUSTIFICATION: validation tooling only; reproduces the original CdSearch/CdControl/CdRead2 arm sequence.
    private static bool OpenStream(string isoPath, out string error)
    {
        error = null;
        var file = new LibCd.CdlFILE();
        if (LibCd.CdSearchFile(file, isoPath.ToCharArray()) == null)
        {
            error = $"CdSearchFile missed {isoPath}";
            return false;
        }

        LibCd.StSetRing(RingAddress, RingSlots);
        LibCd.StSetStream(1, 1, -1, null, null);
        if (LibCd.CdControl(0x15, file.pos, null) != 1 || LibCd.CdRead2(0x1c0) != 1)
        {
            error = $"CdControl/CdRead2 did not arm {isoPath}";
            return false;
        }
        return true;
    }

    // JUSTIFICATION: validation tooling only; shares format checks without merging runtime functions.
    private static int RunMovie(string path, MovieSpec spec)
    {
        string prefix = $"[{spec.LogPrefix}]";

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{prefix} file not found: {path}");
            return 1;
        }

        if (!ValidateRawFile(path, spec, out string error))
        {
            Console.Error.WriteLine($"{prefix} raw stream validation failed: {error}");
            return 1;
        }

        byte[] frameData = ReadFrame(path, 1, out int chunkCount, out error);
        if (frameData == null)
        {
            Console.Error.WriteLine($"{prefix} frame extraction failed: {error}");
            return 1;
        }

        var decoder = new MdecCore();
        ushort[] decoded = decoder.VlcDecode(frameData, out int macroblocksDecoded, out error);
        if (decoded == null)
        {
            Console.Error.WriteLine(
                $"{prefix} decode failed after {macroblocksDecoded} macroblocks: {error}");
            return 1;
        }

        Console.WriteLine(
            $"{prefix} frame 1 decoded: chunks={chunkCount}, " +
            $"macroblocks={macroblocksDecoded}, rleWords={decoded.Length}");

        if (!ValidateStreaming(path, spec, out error))
        {
            Console.Error.WriteLine($"{prefix} stream validation failed: {error}");
            return 1;
        }

        return 0;
    }

    // JUSTIFICATION: validation tooling only; verifies raw-sector and chunk invariants independently.
    private static bool ValidateRawFile(string path, MovieSpec spec, out string error)
    {
        error = null;
        long length = new FileInfo(path).Length;
        if (length % RawSectorSize != 0)
        {
            error = $"length {length} is not divisible by {RawSectorSize}";
            return false;
        }

        int totalSectors = 0;
        int videoSectors = 0;
        int audioSectors = 0;
        int otherSectors = 0;
        var frames = new Dictionary<int, RawFrameInfo>();

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sector = new byte[RawSectorSize];
        while (stream.Read(sector, 0, sector.Length) == sector.Length)
        {
            totalSectors++;
            if ((sector[18] & 0x04) != 0)
            {
                audioSectors++;
                if (sector[19] != 0x01)
                {
                    error = $"audio sector {totalSectors - 1} has coding-info 0x{sector[19]:X2}";
                    return false;
                }
                continue;
            }

            ReadOnlySpan<byte> data = sector.AsSpan(UserDataOffset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(data) != VideoSectorMagic)
            {
                otherSectors++;
                continue;
            }

            videoSectors++;
            int frame = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
            int chunkIndex = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            int chunkCount = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            uint demuxSize = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            int width = BinaryPrimitives.ReadUInt16LittleEndian(data[16..]);
            int height = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
            int version = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]);

            if (frame < 1 || frame > spec.ExpectedFrames || chunkCount <= 0 ||
                chunkCount > RingSlots || chunkIndex < 0 || chunkIndex >= chunkCount ||
                width != 320 || height != 240 || version != 3 || demuxSize > chunkCount * StrPayloadSize)
            {
                error = $"invalid video header at sector {totalSectors - 1}: frame={frame}, " +
                        $"chunk={chunkIndex}/{chunkCount}, size={width}x{height}, v={version}, demux={demuxSize}";
                return false;
            }

            if (!frames.TryGetValue(frame, out RawFrameInfo info))
            {
                info = new RawFrameInfo { ChunkCount = chunkCount, DemuxSize = demuxSize };
                frames.Add(frame, info);
            }

            if (info.ChunkCount != chunkCount || info.DemuxSize != demuxSize ||
                !info.SeenChunks.Add(chunkIndex))
            {
                error = $"inconsistent or duplicate chunk for frame {frame}, chunk {chunkIndex}";
                return false;
            }
        }

        if (totalSectors != spec.ExpectedTotalSectors || videoSectors != spec.ExpectedVideoSectors ||
            audioSectors != spec.ExpectedAudioSectors || otherSectors != spec.ExpectedOtherSectors ||
            frames.Count != spec.ExpectedFrames)
        {
            error = $"counts total/video/audio/other/frames={totalSectors}/{videoSectors}/" +
                    $"{audioSectors}/{otherSectors}/{frames.Count}";
            return false;
        }

        for (int frame = 1; frame <= spec.ExpectedFrames; frame++)
        {
            if (!frames.TryGetValue(frame, out RawFrameInfo info) ||
                info.SeenChunks.Count != info.ChunkCount)
            {
                error = $"frame {frame} is missing or incomplete";
                return false;
            }
        }

        Console.WriteLine(
            $"[{spec.LogPrefix}] raw sectors={totalSectors}, video={videoSectors}, " +
            $"audio={audioSectors}, other={otherSectors}, completeFrames={frames.Count}");
        return true;
    }

    // JUSTIFICATION: validation tooling only; locks the C# mirror to the proven Ghidra layout.
    private static bool ValidateRuntimeStructure(out string error)
    {
        error = null;
        if (Marshal.SizeOf<MoviePlaybackState>() != 0x30 || Marshal.SizeOf<RECT>() != 0x8)
        {
            error = $"state/RECT sizes are 0x{Marshal.SizeOf<MoviePlaybackState>():X}/" +
                $"0x{Marshal.SizeOf<RECT>():X}, expected 0x30/0x8";
            return false;
        }

        (string Name, int Expected)[] fields =
        {
            (nameof(MoviePlaybackState.vlcBuffer0), 0x00),
            (nameof(MoviePlaybackState.vlcBuffer1), 0x04),
            (nameof(MoviePlaybackState.vlcBufferIndex), 0x08),
            (nameof(MoviePlaybackState.mdecOutputBuffer), 0x0C),
            (nameof(MoviePlaybackState.frameBuffer0Rect), 0x10),
            (nameof(MoviePlaybackState.frameBuffer1Rect), 0x18),
            (nameof(MoviePlaybackState.writeBufferIndex), 0x20),
            (nameof(MoviePlaybackState.mdecOutputRect), 0x24),
            (nameof(MoviePlaybackState.frameUploadComplete), 0x2C),
        };

        foreach ((string name, int expected) in fields)
        {
            int actual = Marshal.OffsetOf<MoviePlaybackState>(name).ToInt32();
            if (actual != expected)
            {
                error = $"{name} is at 0x{actual:X}, expected 0x{expected:X}";
                return false;
            }
        }

        return true;
    }

    // JUSTIFICATION: validation tooling only; protects the pre-existing STR v2 decoder path.
    internal static int RunV2Smoke(string path)
    {
        byte[] frameData = ReadFrame(path, 1, out int chunkCount, out string error);
        if (frameData == null)
        {
            Console.Error.WriteLine($"[str-v2] frame extraction failed: {error}");
            return 1;
        }

        int version = BinaryPrimitives.ReadUInt16LittleEndian(frameData.AsSpan(6));
        var decoder = new MdecCore();
        ushort[] decoded = decoder.VlcDecode(frameData, out int macroblocksDecoded, out error);
        if (version != 2 || decoded == null || macroblocksDecoded != MdecCore.MacroblockCount)
        {
            Console.Error.WriteLine(
                $"[str-v2] failed: version={version}, macroblocks={macroblocksDecoded}, error={error}");
            return 1;
        }

        Console.WriteLine(
            $"[str-v2] frame 1 decoded: chunks={chunkCount}, " +
            $"macroblocks={macroblocksDecoded}, rleWords={decoded.Length}");
        if (!ValidateV2Streaming(path, out error))
        {
            Console.Error.WriteLine($"[str-v2] stream validation failed: {error}");
            return 1;
        }

        return 0;
    }

    // JUSTIFICATION: validation tooling only; protects the original 2336-byte stream source path.
    private static bool ValidateV2Streaming(string path, out string error)
    {
        error = null;
        var memory = new byte[MemorySize];
        string fullPath = Path.GetFullPath(path);

        PsxRam.AddressResolver = address =>
        {
            int offset = address - MemoryBaseAddress;
            return offset >= 0 && offset < memory.Length ? (memory, offset) : null;
        };
        LibDs.DiscFileResolver = isoPath =>
            string.Equals(isoPath, "\\FMV1\\FMV000.STR;1", StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;

        var file = new LibCd.CdlFILE();
        if (LibCd.CdSearchFile(file, "\\FMV1\\FMV000.STR;1".ToCharArray()) == null)
        {
            error = "CdSearchFile did not resolve the v2 fixture";
            return false;
        }

        LibCd.StSetRing(RingAddress, RingSlots);
        LibCd.StSetStream(1, 1, -1, null, null);
        if (LibCd.CdControl(0x15, file.pos, null) != 1 || LibCd.CdRead2(0x1c0) != 1 ||
            LibCd.StGetNext(out int frameAddress, out int headerAddress) != 0)
        {
            error = "the 2336-byte source did not deliver its first frame";
            LibCd.StUnSetRing();
            return false;
        }

        byte[] header = PsxRam.ReadBytes(headerAddress, StrHeaderSize);
        int version = header == null ? -1 : BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        bool freed = LibCd.StFreeRing(frameAddress) == 0;
        LibCd.StUnSetRing();
        if (version != 2 || !freed)
        {
            error = $"first streamed frame version={version}, freed={freed}";
            return false;
        }

        Console.WriteLine("[str-v2] 2336-byte stream delivered and freed frame 1");
        return true;
    }

    // JUSTIFICATION: validation tooling only; exercises the SDK streaming contract end to end.
    private static bool ValidateStreaming(string path, MovieSpec spec, out string error)
    {
        error = null;
        var memory = new byte[MemorySize];
        string fullPath = Path.GetFullPath(path);

        PsxRam.AddressResolver = address =>
        {
            int offset = address - MemoryBaseAddress;
            return offset >= 0 && offset < memory.Length ? (memory, offset) : null;
        };
        LibDs.DiscFileResolver = isoPath =>
            string.Equals(isoPath, spec.IsoPath, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;

        var file = new LibCd.CdlFILE();
        if (LibCd.CdSearchFile(file, spec.IsoPath.ToCharArray()) == null)
        {
            error = $"CdSearchFile did not resolve {spec.IsoPath}";
            return false;
        }

        LibCd.StSetRing(RingAddress, RingSlots);
        LibCd.StSetStream(1, 1, -1, null, null);
        LibPress.DecDCTReset(0);
        LibPress.DecDCTvlcSize(RleCapacityBytes / 2 - 1);
        XaAudio.Flush();
        if (LibCd.CdControl(0x15, file.pos, null) != 1 || LibCd.CdRead2(0x1c0) != 1)
        {
            error = "CdControl/CdRead2 did not arm the stream";
            LibCd.StUnSetRing();
            return false;
        }

        int expectedFrame = 1;
        int firstStopFrame = -1;
        var frameHashes = new HashSet<string>(StringComparer.Ordinal);
        int decodedFrames = 0;
        bool anyNonUniformFrame = false;
        while (LibCd.StGetNext(out int frameAddress, out int headerAddress) == 0)
        {
            byte[] header = PsxRam.ReadBytes(headerAddress, StrHeaderSize);
            if (header == null)
            {
                error = $"could not read ring header for frame {expectedFrame}";
                LibCd.StUnSetRing();
                return false;
            }

            int frame = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
            int width = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(16));
            int height = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(18));
            int version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
            if (frame != expectedFrame || width != 320 || height != 240 || version != 3)
            {
                error = $"unexpected frame header: frame={frame}, size={width}x{height}, version={version}";
                LibCd.StUnSetRing();
                return false;
            }

            if (firstStopFrame < 0 && frame > spec.LastFrameBeforeStop)
            {
                firstStopFrame = frame;
            }

            if (Array.IndexOf(spec.SampleFrames, frame) >= 0)
            {
                if (!DecodeFrame(frameAddress, frame, out string hash, out bool isUniform,
                        out byte[] image, out error, spec.MdecMode, spec.LogPrefix))
                {
                    LibCd.StUnSetRing();
                    return false;
                }

                frameHashes.Add(hash);
                decodedFrames++;
                anyNonUniformFrame |= !isUniform;
                if (Array.IndexOf(spec.ExportFrames, frame) >= 0)
                {
                    string rawPath = Path.Combine(
                        Path.GetTempPath(),
                        $"{spec.TempFilePrefix}-frame-{frame:D4}.rgb");
                    File.WriteAllBytes(rawPath, image);
                    Console.WriteLine($"[{spec.LogPrefix}] frame {frame} RGB24: {rawPath}");
                }
            }

            if (LibCd.StFreeRing(frameAddress) != 0)
            {
                error = $"StFreeRing failed for frame {frame}";
                LibCd.StUnSetRing();
                return false;
            }

            expectedFrame++;
        }

        int submittedAudioSectors = XaAudio.SubmittedSectorCountForTest;
        var audio = new short[44100 * 2 * 2];
        int audioFrames = XaAudio.DrainAllForTest(audio);
        int audioPeak = 0;
        for (int i = 0; i < audioFrames * 2; i++)
        {
            audioPeak = Math.Max(audioPeak, Math.Abs((int)audio[i]));
        }

        LibCd.StUnSetRing();
        if (expectedFrame != spec.ExpectedFrames + 1)
        {
            error = $"delivered {expectedFrame - 1} frames instead of {spec.ExpectedFrames}";
            return false;
        }

        if (firstStopFrame != spec.LastFrameBeforeStop + 1)
        {
            error = $"first stop frame is {firstStopFrame}, expected {spec.LastFrameBeforeStop + 1}";
            return false;
        }

        if (decodedFrames != spec.SampleFrames.Length || frameHashes.Count < 2 || !anyNonUniformFrame)
        {
            error = $"decoded samples invalid: count={decodedFrames}, distinct={frameHashes.Count}, " +
                    $"anyNonUniform={anyNonUniformFrame}";
            return false;
        }

        if (submittedAudioSectors != spec.ExpectedAudioSectors || audioFrames == 0 || audioPeak == 0)
        {
            error = $"XA output invalid: sectors={submittedAudioSectors}, frames={audioFrames}, peak={audioPeak}";
            return false;
        }

        Console.WriteLine(
            $"[{spec.LogPrefix}] stream delivered frames 1..{spec.ExpectedFrames}; " +
            $"stopFrame={firstStopFrame}; sampled RGB24 output changes; XA sectors={submittedAudioSectors}, " +
            $"bufferedFrames={audioFrames}, peak={audioPeak}");
        return true;
    }

    // JUSTIFICATION: validation tooling only; drives the public libpress contract for one frame.
    private static bool DecodeFrame(int frameAddress, int frameNumber, out string hash,
        out bool isUniform, out byte[] image, out string error, int mdecMode, string logPrefix)
    {
        hash = null;
        error = null;
        isUniform = true;
        image = null;

        if (LibPress.DecDCTvlc(frameAddress, RleAddress) != 0)
        {
            error = $"DecDCTvlc failed for frame {frameNumber}";
            return false;
        }

        if (LibPress.DecDCTin(RleAddress, mdecMode) != 0)
        {
            error = $"DecDCTin failed for frame {frameNumber}";
            return false;
        }

        int callbackCount = 0;
        LibPress.DecDCToutCallback(() => callbackCount++);
        image = new byte[MdecCore.FrameWidth * MdecCore.FrameHeight * 3];
        int rowBytes = MdecCore.MacroblockSize * 3;
        try
        {
            for (int stripIndex = 0; stripIndex < MdecCore.MacroblockCols; stripIndex++)
            {
                if (LibPress.DecDCTout(StripAddress, MdecCore.Strip24PixelBytes / 4) != 0)
                {
                    error = $"DecDCTout failed for frame {frameNumber}, strip {stripIndex}";
                    return false;
                }

                byte[] strip = PsxRam.ReadBytes(StripAddress, MdecCore.Strip24PixelBytes);
                if (strip == null)
                {
                    error = $"strip {stripIndex} could not be read for frame {frameNumber}";
                    return false;
                }

                for (int row = 0; row < MdecCore.FrameHeight; row++)
                {
                    Buffer.BlockCopy(
                        strip,
                        row * rowBytes,
                        image,
                        (row * MdecCore.FrameWidth + stripIndex * MdecCore.MacroblockSize) * 3,
                        rowBytes);
                }
            }
        }
        finally
        {
            LibPress.DecDCToutCallback(null);
        }

        if (callbackCount != MdecCore.MacroblockCols)
        {
            error = $"frame {frameNumber} produced {callbackCount} callbacks instead of 20";
            return false;
        }

        byte first = image[0];
        for (int i = 1; i < image.Length; i++)
        {
            if (image[i] != first)
            {
                isUniform = false;
                break;
            }
        }

        hash = Convert.ToHexString(SHA256.HashData(image));
        Console.WriteLine($"[{logPrefix}] frame {frameNumber}: sha256={hash}, uniform={isUniform}");
        return true;
    }

    // JUSTIFICATION: validation tooling only; demultiplexes one standard raw-sector STR frame.
    private static byte[] ReadFrame(string path, uint targetFrame, out int chunkCount, out string error)
    {
        chunkCount = 0;
        error = null;
        byte[][] chunks = null;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        (int sectorSize, int userDataOffset) = DetectSectorLayout(stream);
        var sector = new byte[sectorSize];
        while (stream.Read(sector, 0, sector.Length) == sector.Length)
        {
            int subheaderOffset = userDataOffset - 8;
            byte submode = sector[subheaderOffset + 2];
            if ((submode & 0x04) != 0)
            {
                continue;
            }

            ReadOnlySpan<byte> data = sector.AsSpan(userDataOffset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(data) != VideoSectorMagic ||
                BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) != targetFrame)
            {
                continue;
            }

            int chunkIndex = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            int thisChunkCount = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            if (chunks == null)
            {
                chunkCount = thisChunkCount;
                chunks = new byte[chunkCount][];
            }

            if (thisChunkCount != chunkCount || chunkIndex < 0 || chunkIndex >= chunkCount)
            {
                error = $"inconsistent chunk header: index={chunkIndex}, count={thisChunkCount}";
                return null;
            }

            chunks[chunkIndex] = data.Slice(StrHeaderSize, StrPayloadSize).ToArray();

            bool complete = true;
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i] == null)
                {
                    complete = false;
                    break;
                }
            }

            if (complete)
            {
                var result = new byte[chunkCount * StrPayloadSize];
                for (int i = 0; i < chunks.Length; i++)
                {
                    Buffer.BlockCopy(chunks[i], 0, result, i * StrPayloadSize, StrPayloadSize);
                }

                return result;
            }
        }

        error = $"frame {targetFrame} was not complete";
        return null;
    }

    // JUSTIFICATION: validation tooling only; recognizes the two raw-sector layouts used by the fixtures.
    private static (int SectorSize, int UserDataOffset) DetectSectorLayout(FileStream stream)
    {
        var header = new byte[16];
        int read = stream.Read(header, 0, header.Length);
        stream.Position = 0;

        bool fullRawSector = read == header.Length && header[0] == 0 && header[11] == 0 && header[15] == 2;
        if (fullRawSector)
        {
            for (int i = 1; i < 11; i++)
            {
                fullRawSector &= header[i] == 0xff;
            }
        }

        return fullRawSector ? (RawSectorSize, UserDataOffset) : (2336, 8);
    }
}