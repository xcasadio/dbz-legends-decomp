using System.Runtime.InteropServices;

namespace DbzLegendsRemaster.Types;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 0x8)]
internal struct RECT
{
    public short x;
    public short y;
    public short w;
    public short h;
}

[StructLayout(LayoutKind.Explicit, Size = 0x30)]
internal struct MoviePlaybackState
{
    [FieldOffset(0x00)] public uint vlcBuffer0;
    [FieldOffset(0x04)] public uint vlcBuffer1;
    [FieldOffset(0x08)] public uint vlcBufferIndex;
    [FieldOffset(0x0C)] public uint mdecOutputBuffer;
    [FieldOffset(0x10)] public RECT frameBuffer0Rect;
    [FieldOffset(0x18)] public RECT frameBuffer1Rect;
    [FieldOffset(0x20)] public uint writeBufferIndex;
    [FieldOffset(0x24)] public RECT mdecOutputRect;
    [FieldOffset(0x2C)] public uint frameUploadComplete;
}