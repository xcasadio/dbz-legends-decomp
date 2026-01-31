namespace PsxTools2;

public static class BinaryReaderHelper
{
    public static ushort[] ReadUShortArrayFast(byte[] data, int offset, int count)
    {
        ushort[] arr = new ushort[count];
        Buffer.BlockCopy(data, offset, arr, 0, count * 2);
        return arr;
    }
}