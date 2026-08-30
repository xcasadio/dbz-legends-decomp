using PsxSdkMonogame;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.TITLE_EXE;

// The stage backdrop. FUN_800376c0 @ 0x800376C0 is the sixth step of the select-screen build-up
// FUN_80058a9c @ 0x80058A9C performs, and find-cross-references reports exactly ONE reference to it
// in the whole overlay: the call at 0x80058CF4.
//
// It creates two more tasks in list 1, and proceeds only if the SECOND one was created. Then it
// reads one \STG\STGnTX.B;1 texture archive off the disc into BYTE_ARRAY_801d2000, runs the load
// script that sits at the head of that same buffer through FUN_80057c80 @ 0x80057C80, copies the
// stage's three background-colour components out of INT_ARRAY_80078948 into the DRAWENV globals,
// creates a third task through FUN_80037104 @ 0x80037104, and finally lays out a 23 x 23 grid of
// world-space quads at 0x800ACDA0.
internal static class StageBackdrop
{
    // GHIDRA: BYTE_ARRAY_801d2000 @ 0x801D2000
    // Its PSX address. The buffer is LoadingScreen.BYTE_ARRAY_801d2000; this function hands the raw
    // address to ReadFile and then to FUN_80057c80 twice, once as the script pointer and once as
    // the base every entry's dataOffset is measured from.
    private const int ByteArray801d2000Address = unchecked((int)0x801D2000);

    // GHIDRA: STGxMD_FileNames @ 0x800788B8
    // char[8][18] in Ghidra, and the bytes agree: read-memory 0x800788B8 x144 gives eight
    // NUL-terminated names on an 18-byte stride, each 15 characters plus a NUL plus two zero bytes
    // of padding. The eight are, verbatim,
    //   \STG\STG1TX.B;1 \STG\STG2TX.B;1 \STG\STG3TX.B;1 \STG\STG4TX.B;1
    //   \STG\STG5TX.B;1 \STG\STG6TX.B;1 \STG\STG7TX.B;1 \STG\STG8TX.B;1
    // and 0x800788B8 + 8 * 18 = 0x80078948, which is exactly where INT_ARRAY_80078948 begins — so
    // the table has eight entries and not one more.
    //
    // The name Ghidra gives the symbol says MD; the contents say TX. The Ghidra name is kept.
    //
    // The index is DAT_1f80012c, which main @ 0x800581DC keeps in 0..2, so only the first three are
    // ever reached on the title path.
    private static readonly char[][] STGxMD_FileNames =
    {
        "\\STG\\STG1TX.B;1".ToCharArray(),
        "\\STG\\STG2TX.B;1".ToCharArray(),
        "\\STG\\STG3TX.B;1".ToCharArray(),
        "\\STG\\STG4TX.B;1".ToCharArray(),
        "\\STG\\STG5TX.B;1".ToCharArray(),
        "\\STG\\STG6TX.B;1".ToCharArray(),
        "\\STG\\STG7TX.B;1".ToCharArray(),
        "\\STG\\STG8TX.B;1".ToCharArray(),
    };

    // GHIDRA: INT_ARRAY_80078948 @ 0x80078948
    // Initialised .data (0x80074FB4..0x800831B3), lifted out of the overlay image with read-memory.
    // Twenty-four ints, walked as [index * 3], [index * 3 + 1], [index * 3 + 2] — eight stages of
    // three components each. The extent is closed at both ends: it starts where STGxMD_FileNames
    // ends, and the 96 bytes after 0x80078948 are followed at 0x800789A8 by unrelated data
    // (3D 10 33 00 35 23 ...) rather than by more values of this shape.
    //
    // The three ints feed DAT_80083450, DAT_8008344c and DAT_80083448 in that order, which
    // main @ 0x800581DC zeroes together and which FrameLoop's DRAWENV carries as r0/g0/b0.
    // Nothing read here says which component is which, so nothing below claims one.
    internal static readonly int[] INT_ARRAY_80078948 =
    {
        0xa0, 0xd0, 0xf8,
        0x70, 0xc8, 0x80,
        0x20, 0x28, 0x18,
        0xc0, 0xc0, 0xf8,
        0xa8, 0xd0, 0xf8,
        0x98, 0xe0, 0xf0,
        0xb0, 0xb8, 0xf8,
        0xc0, 0xa0, 0xf0,
    };

    // GHIDRA: astruct_1_800acda0 @ 0x800ACDA0
    // The backdrop grid. 23 x 23 elements of 0x48 bytes = 0x94C8 bytes, all of it inside .bss
    // (0x800836BC..0x800B9EEF) and clear of every other modelled block: it ends at 0x800B6267, well
    // below POLY_GT4_800b9518 @ 0x800B9518.
    //
    // The element is a POLY_FT4 followed by four SVECTORs, closed from the store offsets rather than
    // assumed. InitializePolyFt4 writes the psyq POLY_FT4 fields at +0x04..+0x25, and the grid loop
    // writes twelve halfwords at +0x28, +0x2A, +0x2C, +0x30, +0x32, +0x34, +0x38, +0x3A, +0x3C,
    // +0x40, +0x42 and +0x44 — four vx/vy/vz triples on an 8-byte stride, whose fourth halfword
    // (the SVECTOR pad at +0x2E, +0x36, +0x3E, +0x46) is never touched:
    //   +0x00  POLY_FT4, 40 bytes
    //   +0x28  SVECTOR 0   (x, 0, z)
    //   +0x30  SVECTOR 1   (x + 0x100, 0, z)
    //   +0x38  SVECTOR 2   (x, 0, z + 0x100)
    //   +0x40  SVECTOR 3   (x + 0x100, 0, z + 0x100)
    // Ghidra's own name for the +0x38 member, field31_0x38, is what fixes the SVECTOR boundary: the
    // loop cursor is &field31_0x38.vz, that is element + 0x3C.
    //
    // Registered with LibGpu.RamRegion so each packet carries a real PSX address for the AddPrim
    // that LAB_80037bf0 / LAB_800378d8 would perform, AND reachable through
    // TITLE_EXE_exe.ResolveAddress so the raw-address halfword stores below land in it. Both maps
    // hand back this same array.
    private const int Astruct1800acda0Address = unchecked((int)0x800ACDA0);

    internal static readonly byte[] astruct_1_800acda0 =
        RamRegion(Astruct1800acda0Address, 0x17 * 0x17 * 0x48);

    // GHIDRA: DAT_80083458 @ 0x80083458
    // Sixteen bits: the store at 0x80037108 is `sh a0,0x2A4(gp)` with gp = 0x800831B4, not a `sw`,
    // and Ghidra types the label undefined2.
    internal static ushort DAT_80083458;

    // GHIDRA: DAT_80083490 @ 0x80083490
    // Sixteen bits as well: `sh zero,0x2DC(gp)` at 0x8003712C.
    internal static ushort DAT_80083490;

    // GHIDRA: LAB_80037bf0 @ 0x80037BF0
    // The callback of FUN_800376c0's first CreateTask, stored and never called here.
    //
    // BLOCKED: Ghidra has no function boundary at this address, so there is no closed size and no
    // decompilation was read. Not registered with TaskSystem, so the task block exists, carries the
    // console's own callback address at +0x04 and is walked by ExecuteTaskList, and does nothing
    // when its turn comes.
    private const int LAB_80037bf0_Address = unchecked((int)0x80037BF0);

    // GHIDRA: LAB_800378d8 @ 0x800378D8
    // The callback of FUN_800376c0's second CreateTask — the one whose success gates the whole rest
    // of the function. It begins immediately after FUN_800376c0's own last byte (0x800378D7).
    //
    // BLOCKED: no function boundary, no size, body not read. Not registered with TaskSystem.
    private const int LAB_800378d8_Address = unchecked((int)0x800378D8);

    // GHIDRA: LAB_8003714c @ 0x8003714C
    // The callback FUN_80037104 creates in list 13 with a 0x0C-byte context. It begins immediately
    // after FUN_80037104's own last byte (0x8003714B).
    //
    // BLOCKED: no function boundary, no size, body not read. Not registered with TaskSystem.
    private const int LAB_8003714c_Address = unchecked((int)0x8003714C);

    // GHIDRA: FUN_800376c0 @ 0x800376C0
    // The two CreateTask sites, argument by argument, decoded from the image:
    //   site 1, jal @0x80037708: callback 0x80037BF0 (lui 0x8003 @0x800376CC + addiu 0x7BF0
    //     @0x800376D0), id 0x100, listIndex 1, contextSize 4, param_5 0, insertPoint
    //     *(0x800798AC) — and 0x800798AC - 0x800798A8 = 4 = 1 * 4, so g_TaskListTail[1];
    //   site 2, jal @0x8003772C: callback 0x800378D8, id 0x54, listIndex 1, contextSize 0,
    //     param_5 0, insertPoint the SAME g_TaskListTail[1], re-loaded from memory at 0x80037720.
    // The `beq v0,zero,0x800378B0` at 0x80037734 is what makes everything after site 2 conditional
    // on site 2's return, and its delay slot `andi s0,s1,0xffff` runs on both paths — which is why
    // uVar1 is assigned outside the `if`, exactly as Ghidra prints it.
    //
    // The file name is addressed with an 18-byte stride: `sll a0,s0,3 / addu a0,a0,s0 / sll a0,a0,1`
    // at 0x8003773C..0x80037744 computes index * 18, then 0x80037750 adds 0x800788B8.
    internal static void FUN_800376c0(uint fileIndex)
    {
        short sVar1;
        int fileSize;
        int j;
        short index;
        ushort uVar3;
        uint uVar1;
        int psVar4;
        int i;
        int iVar2;
        int polyFt4;
        short sVar6;

        TaskSystem.CreateTask(LAB_80037bf0_Address, 0x100, 1, 4, 0, TaskSystem.g_TaskListTail[1]);
        fileSize = TaskSystem.CreateTask(
            LAB_800378d8_Address, 0x54, 1, 0, 0, TaskSystem.g_TaskListTail[1]);
        uVar1 = fileIndex & 0xffff;

        // `fileSize` is the decompiler's name for the second CreateTask's return value. It is a task
        // block address, not a size; the name is Ghidra's and is kept rather than improved.
        if (fileSize != 0)
        {
            // DEVIATION: the console reads past the end of the table for an out-of-range index and
            // gets whatever follows it in .rodata; C# throws instead. uVar1 comes from
            // DAT_1f80012c, which main @ 0x800581DC clamps to 0..2, so no observed path reaches it.
            // Stated rather than guarded, so the divergence stays visible.
            TITLE_EXE_exe.ReadFile(STGxMD_FileNames[uVar1], ByteArray801d2000Address, 0);

            // Both arguments are the SAME address here. The archive carries its load script at
            // offset 0 and every entry's dataOffset is measured from that same offset 0, which is
            // why the two pointers coincide — unlike FUN_80021dd0 @ 0x80021DD0, where the script
            // sits at &DAT_80110000 + DAT_80110004 and the base is &DAT_80110000.
            TitleImages.FUN_80057c80(ByteArray801d2000Address, ByteArray801d2000Address);
            polyFt4 = Astruct1800acda0Address;
            TITLE_EXE_exe.DAT_80083450 = INT_ARRAY_80078948[uVar1 * 3];
            TITLE_EXE_exe.DAT_8008344c = INT_ARRAY_80078948[uVar1 * 3 + 1];
            TITLE_EXE_exe.DAT_80083448 = INT_ARRAY_80078948[uVar1 * 3 + 2];
            sVar6 = 0x100;

            // Ghidra prints this call with no argument because it declares the callee void-param,
            // but the callee reads a0. See the note on FUN_80037104 below: a0 is uVar1, loaded by
            // `addu a0,s0,zero` at 0x80037778 and untouched up to the `jal` at 0x800377DC.
            FUN_80037104((ushort)uVar1);
            iVar2 = 0;
            do
            {
                i = 0;

                // &(polyFt4->field31_0x38).vz, that is element + 0x38 + 4. Every store below is
                // written as this cursor plus the exact byte displacement the `sh` carries.
                psVar4 = polyFt4 + 0x3c;
                do
                {
                    InitializePolyFt4(PolyFt4At(polyFt4), 1, 0xb, 0x7880,
                        (byte)((i % 2) * 0x20), (byte)((iVar2 % 2) * 0x20), 0x1f, 0x1f);
                    j = i << 8;
                    i = i + 1;
                    index = (short)(i * 0x100);
                    sVar1 = (short)j;

                    // Machine store order, 0x80037854..0x80037884, kept exactly:
                    //   -0x04 -0x14  the column X, twice        (SVECTOR 2 .vx, SVECTOR 0 .vx)
                    //   +0x06 -0x02 -0x0A -0x12  four zero Y    (all four SVECTOR .vy)
                    //   +0x04 -0x0C  the next column X, twice   (SVECTOR 3 .vx, SVECTOR 1 .vx)
                    //   -0x08 -0x10  the row Z, twice           (SVECTOR 1 .vz, SVECTOR 0 .vz)
                    //   +0x08 +0x00  the next row Z, twice      (SVECTOR 3 .vz, SVECTOR 2 .vz)
                    PsxRam.WriteU16(psVar4 + -4, (ushort)sVar1);
                    PsxRam.WriteU16(psVar4 + -0x14, (ushort)sVar1);
                    PsxRam.WriteU16(psVar4 + 6, 0);
                    PsxRam.WriteU16(psVar4 + -2, 0);
                    PsxRam.WriteU16(psVar4 + -10, 0);
                    PsxRam.WriteU16(psVar4 + -0x12, 0);
                    PsxRam.WriteU16(psVar4 + 4, (ushort)index);
                    PsxRam.WriteU16(psVar4 + -0xc, (ushort)index);
                    uVar3 = (ushort)(iVar2 << 8);
                    PsxRam.WriteU16(psVar4 + -8, uVar3);
                    PsxRam.WriteU16(psVar4 + -0x10, uVar3);
                    PsxRam.WriteU16(psVar4 + 8, (ushort)sVar6);
                    PsxRam.WriteU16(psVar4, (ushort)sVar6);

                    // psVar4 is a short *, so the original's `+ 0x24` is 0x48 bytes — the element
                    // stride, the same one polyFt4 advances by.
                    psVar4 = psVar4 + 0x48;
                    polyFt4 = polyFt4 + 0x48;

                    // `addiu v1,s2,1` at 0x8003789C sits in the DELAY SLOT of the inner loop's
                    // branch, so it runs on every inner iteration including the last. Ghidra hoists
                    // it to the bottom of the inner body, which is where it stays here.
                    j = iVar2 + 1;
                } while (i < 0x17);

                sVar6 = (short)(sVar6 + 0x100);
                iVar2 = j;
            } while (j < 0x17);
        }
    }

    // GHIDRA: FUN_80037104 @ 0x80037104
    // Seventy-two bytes, decoded word for word out of the image.
    //
    // ITS ARGUMENT IS CLOSED, and it is not what the decompiled call site shows. Ghidra prints
    // `FUN_80037104()` inside FUN_800376c0 yet gives the function itself the signature
    // `void FUN_80037104(undefined2 param_1)`, because the very first instruction of the body,
    // `sh a0,0x2A4(gp)` at 0x80037108, stores a0 into DAT_80083458. The value a0 holds at the call
    // is the stage index: `addu a0,s0,zero` at 0x80037778 puts uVar1 there immediately after the
    // FUN_80057c80 call returns, and the eighteen instructions from 0x8003777C to 0x800377D8 write
    // only s3, s2, v0, at, v1 and a1 — a0 is untouched all the way to the `jal 0x80037104` at
    // 0x800377DC, whose delay slot is `ori s4,zero,0x100`, the sVar6 initialiser, not an argument.
    internal static void FUN_80037104(ushort param_1)
    {
        // Machine order: DAT_80083458 first (0x80037108), DAT_80083490 second (0x8003712C). Ghidra
        // prints them the other way round. They are distinct addresses, so the order is not
        // observable; the machine's is the one written here.
        DAT_80083458 = param_1;
        DAT_80083490 = 0;

        // DAT_800798dc @ 0x800798DC is g_TaskListTail[13]: g_TaskListTail sits at 0x800798A8 and
        // (0x800798DC - 0x800798A8) / 4 = 13, matching the list index 0xd passed as a2.
        TaskSystem.CreateTask(LAB_8003714c_Address, 0, 0xd, 0xc, 0, TaskSystem.g_TaskListTail[0xd]);
    }

    // GHIDRA: InitializePolyFt4 @ 0x800572AC
    // 216 bytes, one caller in the whole overlay (the grid loop above). Every field offset below is
    // the one the store actually carries, read off the synchronized disassembly:
    //   sh v0,0x4(s0)    r0 and g0 together, one halfword of 0x8080
    //   sh s4,0x16(s0)   tpage      sh s5,0xe(s0)    clut       sb v0,0x6(s0)   b0
    //   sb v1,0xc(s0)    u0         sb v0,0xd(s0)    v0
    //   sb s1,0x14(s0)   u1         sb v0,0x15(s0)   v1
    //   sb v1,0x1c(s0)   u2         sb s2,0x1d(s0)   v2
    //   sb s1,0x24(s0)   u3         sb s2,0x25(s0)   v3
    // Ghidra spells v0 and v1 as `_2` and `_3` in its own POLY_FT4; +0x0D and +0x15 are those two
    // fields at the psyq offsets, which is what the port writes.
    //
    // u1Prime and v1Prime are `addu` then `addiu -1` (0x80057324..0x80057330), so the decompiler's
    // `+ 0xff` is a minus one truncated by the `sb`. Both spellings are the same byte; the
    // decompiler's is kept.
    internal static void InitializePolyFt4(POLY_FT4Ref polyFt4, int transparency, ushort texturePage,
        ushort colorLookupTable, byte u1, byte v1, sbyte u2, sbyte v2)
    {
        byte u1Prime;
        byte v1Prime;

        SetPolyFT4(polyFt4);
        SetSemiTrans(polyFt4, transparency);

        // The console writes r0 and g0 with a single `sh` of 0x8080; two byte stores land the same
        // two bytes.
        polyFt4.r0 = 0x80;
        polyFt4.g0 = 0x80;
        polyFt4.tpage = texturePage;
        polyFt4.clut = colorLookupTable;
        polyFt4.b0 = 0x80;
        u1Prime = (byte)(u1 + u2 + 0xff);
        v1Prime = (byte)(v1 + v2 + 0xff);
        polyFt4.u0 = u1;
        polyFt4.v0 = v1;
        polyFt4.u1 = u1Prime;
        polyFt4.v1 = v1;
        polyFt4.u2 = u1;
        polyFt4.v2 = v1Prime;
        polyFt4.u3 = u1Prime;
        polyFt4.v3 = v1Prime;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original hands InitializePolyFt4 the raw `POLY_FT4 *` its grid cursor holds. A
    // primitive in this port is a (byte[], offset) pair, so the PSX address is split back into one
    // against the grid's own base — the same split SpriteRenderer.FUN_80048f88 performs for its
    // pool pointer, done here against a known base because the grid is a single registered block.
    private static POLY_FT4Ref PolyFt4At(int address) =>
        new(astruct_1_800acda0, address - Astruct1800acda0Address);

    // JUSTIFICATION: C# language bridge only
    // RELATION: lets the overlay's address resolver map the grid, since the twelve halfword stores
    // in FUN_800376c0 address it by raw PSX address exactly as the original does. The same array is
    // already registered with LibGpu.RamRegion at its declaration, so both address maps agree.
    internal static (byte[] Buffer, int Offset)? Resolve(int address)
    {
        int offset = address - Astruct1800acda0Address;
        return offset >= 0 && offset < astruct_1_800acda0.Length
            ? (astruct_1_800acda0, offset)
            : null;
    }
}
