namespace PsxTools2;

public static class LzssDecompressor
{
    public static byte[] Decompress(byte[] src)
    {
        using var ms = new MemoryStream(src, writable: false);
        using var br = new BinaryReader(ms);

        if (ms.Length < 2)
        {
            throw new InvalidDataException("Input too short (header 16-bit).");
        }

        int commandCount = br.ReadUInt16(); // little-endian
        var output = new List<byte>(Math.Max(256, commandCount));

        int flagsReg = 0; // équivalent t2 = flagByte << 24

        for (int cmd = 0; cmd < commandCount; cmd++)
        {
            if ((cmd & 7) == 0)
            {
                if (ms.Position >= ms.Length)
                {
                    throw new InvalidDataException("EOF during reading flagByte.");
                }

                flagsReg = br.ReadByte() << 24;
            }

            if (ms.Position >= ms.Length)
            {
                throw new InvalidDataException("EOF during reading.");
            }

            byte b = br.ReadByte();

            bool isBackRef = flagsReg < 0; // bltz t2
            flagsReg <<= 1;                // sll t2, 1

            if (!isBackRef)
            {
                // literal
                output.Add(b);
                continue;
            }

            // backref
            int len = (b >> 2) + 1;

            if (ms.Position >= ms.Length)
            {
                throw new InvalidDataException("EOF pendant lecture offset backref.");
            }

            int off = ((b & 0x03) << 8) | br.ReadByte();

            int srcIndex = output.Count - off - 1;
            if (srcIndex < 0)
            {
                throw new InvalidDataException($"Invalid backref: off={off}, outCount={output.Count}.");
            }

            // Copie LZ avec overlap autorisé
            for (int i = 0; i < len; i++)
            {
                output.Add(output[srcIndex + i]);
            }
        }

        return output.ToArray();
    }
}