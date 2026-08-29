using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DbzLegendsRemaster.SLPS_003_55;
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

    // JUSTIFICATION: validation tooling only; exercises the existing decoder against one real DBZ frame.
    internal static int Run(string path)
    {
        if (!ValidateRuntimeStructure(out string layoutError))
        {
            Console.Error.WriteLine($"[bandai-str] runtime layout failed: {layoutError}");
            return 1;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[bandai-str] file not found: {path}");
            return 1;
        }

        byte[] frameData = ReadFrame(path, 1, out int chunkCount, out string error);
        if (frameData == null)
        {
            Console.Error.WriteLine($"[bandai-str] frame extraction failed: {error}");
            return 1;
        }

        var decoder = new MdecCore();
        ushort[] decoded = decoder.VlcDecode(frameData, out int macroblocksDecoded, out error);
        if (decoded == null)
        {
            Console.Error.WriteLine(
                $"[bandai-str] decode failed after {macroblocksDecoded} macroblocks: {error}");
            return 1;
        }

        Console.WriteLine(
            $"[bandai-str] frame 1 decoded: chunks={chunkCount}, " +
            $"macroblocks={macroblocksDecoded}, rleWords={decoded.Length}");

        if (!ValidateStreaming(path, out error))
        {
            Console.Error.WriteLine($"[bandai-str] stream validation failed: {error}");
            return 1;
        }

        return 0;
    }

    // JUSTIFICATION: validation tooling only; locks the C# mirror to the proven Ghidra layout.
    private static bool ValidateRuntimeStructure(out string error)
    {
        error = null;
        if (Marshal.SizeOf<UnkStruct_8009A594>() != 0x30)
        {
            error = $"size is 0x{Marshal.SizeOf<UnkStruct_8009A594>():X}, expected 0x30";
            return false;
        }

        (string Name, int Expected)[] fields =
        {
            (nameof(UnkStruct_8009A594.field_0x00), 0x00),
            (nameof(UnkStruct_8009A594.field_0x04), 0x04),
            (nameof(UnkStruct_8009A594.field_0x08), 0x08),
            (nameof(UnkStruct_8009A594.field_0x0C), 0x0C),
            (nameof(UnkStruct_8009A594.field_0x10), 0x10),
            (nameof(UnkStruct_8009A594.field_0x18), 0x18),
            (nameof(UnkStruct_8009A594.field_0x20), 0x20),
            (nameof(UnkStruct_8009A594.field_0x24), 0x24),
            (nameof(UnkStruct_8009A594.field_0x2C), 0x2C),
        };

        foreach ((string name, int expected) in fields)
        {
            int actual = Marshal.OffsetOf<UnkStruct_8009A594>(name).ToInt32();
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
    private static bool ValidateStreaming(string path, out string error)
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
            string.Equals(isoPath, "\\MOVIE\\BANDAI.STR;1", StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;

        var file = new LibCd.CdlFILE();
        if (LibCd.CdSearchFile(file, "\\MOVIE\\BANDAI.STR;1".ToCharArray()) == null)
        {
            error = "CdSearchFile did not resolve BANDAI.STR";
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

            if (frame == 1 || frame == 8 || frame == 30 || frame == 89)
            {
                if (!DecodeFrame(frameAddress, frame, out string hash, out bool isUniform,
                        out byte[] image, out error))
                {
                    LibCd.StUnSetRing();
                    return false;
                }

                frameHashes.Add(hash);
                decodedFrames++;
                anyNonUniformFrame |= !isUniform;
                if (frame == 8 || frame == 89)
                {
                    string rawPath = Path.Combine(
                        Path.GetTempPath(),
                        $"dbz-bandai-frame-{frame:D4}.rgb");
                    File.WriteAllBytes(rawPath, image);
                    Console.WriteLine($"[bandai-str] frame {frame} RGB24: {rawPath}");
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
        if (expectedFrame != 91)
        {
            error = $"delivered {expectedFrame - 1} frames instead of 90";
            return false;
        }

        if (decodedFrames != 4 || frameHashes.Count < 2 || !anyNonUniformFrame)
        {
            error = $"decoded samples invalid: count={decodedFrames}, distinct={frameHashes.Count}, " +
                    $"anyNonUniform={anyNonUniformFrame}";
            return false;
        }

        if (submittedAudioSectors != 113 || audioFrames == 0 || audioPeak == 0)
        {
            error = $"XA output invalid: sectors={submittedAudioSectors}, frames={audioFrames}, peak={audioPeak}";
            return false;
        }

        Console.WriteLine(
            $"[bandai-str] stream delivered frames 1..90; sampled RGB24 output changes; " +
            $"XA sectors={submittedAudioSectors}, bufferedFrames={audioFrames}, peak={audioPeak}");
        return true;
    }

    // JUSTIFICATION: validation tooling only; drives the public libpress contract for one frame.
    private static bool DecodeFrame(int frameAddress, int frameNumber, out string hash,
        out bool isUniform, out byte[] image, out string error)
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

        if (LibPress.DecDCTin(RleAddress, 3) != 0)
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
        Console.WriteLine($"[bandai-str] frame {frameNumber}: sha256={hash}, uniform={isUniform}");
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