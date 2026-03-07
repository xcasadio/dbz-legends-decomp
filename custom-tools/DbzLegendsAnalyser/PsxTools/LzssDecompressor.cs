namespace PsxTools;

/// <summary>
/// LZSS decompression matching the game's FUN_80034e34.
/// Flag-byte driven, 8 commands per flag, back-references with 6-bit length + 10-bit offset.
/// Copied from PsxTools2 (no GDI+ dependency).
/// </summary>
public static class LzssDecompressor
{
    public static byte[] Decompress(byte[] src)
    {
        using var ms = new MemoryStream(src, writable: false);
        using var br = new BinaryReader(ms);

        if (ms.Length < 2)
            throw new InvalidDataException("Input too short (header 16-bit).");

        int commandCount = br.ReadUInt16();
        var output = new List<byte>(Math.Max(256, commandCount));

        int flagsReg = 0;

        for (int cmd = 0; cmd < commandCount; cmd++)
        {
            if ((cmd & 7) == 0)
            {
                if (ms.Position >= ms.Length)
                    throw new InvalidDataException("EOF during reading flagByte.");
                flagsReg = br.ReadByte() << 24;
            }

            if (ms.Position >= ms.Length)
                throw new InvalidDataException("EOF during reading.");

            byte b = br.ReadByte();

            bool isBackRef = flagsReg < 0;
            flagsReg <<= 1;

            if (!isBackRef)
            {
                output.Add(b);
                continue;
            }

            int len = (b >> 2) + 1;

            if (ms.Position >= ms.Length)
                throw new InvalidDataException("EOF pendant lecture offset backref.");

            int off = ((b & 0x03) << 8) | br.ReadByte();

            int srcIndex = output.Count - off - 1;
            if (srcIndex < 0)
                throw new InvalidDataException($"Invalid backref: off={off}, outCount={output.Count}.");

            for (int i = 0; i < len; i++)
                output.Add(output[srcIndex + i]);
        }

        return output.ToArray();
    }
}
