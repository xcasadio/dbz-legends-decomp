using PsxSdkMonogame;
using static PsxSdkMonogame.Kernel;

namespace DbzLegendsRemaster.TITLE_EXE;

// The direct callees of FUN_80058a9c @ 0x80058A9C that this pass could close, plus the RAM they
// address. Grouped by their caller, not by a claimed role: every function below keeps its raw
// FUN_ name and nothing here is named for what it might mean.
//
// FUN_80058a9c's two remaining direct callees are NOT here and are recorded at their call site in
// TITLE_EXE_exe.FUN_80058a9c: LoadFACE_B @ 0x80052D68 (needs CdIntToPos/CdPosToInt, both
// do-nothing stubs in LibCd) and FUN_800376c0 @ 0x800376C0 (needs FUN_80057c80 to take its file
// buffer as a parameter, which is a change inside TitleImages.cs).
internal static class SelectScreenSetup
{
    // GHIDRA: UnkStruct_Array_800836d4 @ 0x800836D4
    // Uninitialised RAM. Two disjoint stretches, and the extent below is their sum, closed by three
    // readings that agree:
    //   FUN_80027354 @ 0x80027354 memsets 0x438 at 0x800836D4 — thirty 0x24-byte records;
    //   FUN_80035700 @ 0x80035700 memsets 0xB610 at 0x80083B0C = 0x800836D4 + 0x438;
    //   FUN_800350f4 @ 0x800350F4 walks six 0x1E58 slots from 0x80083B0C, and 6 * 0x1E58 = 0xB610.
    // Total 0x438 + 0xB610 = 0xBA48, so 0x800836D4..0x8008F11B, inside .bss (0x800836BC..0x800B9EEF).
    //
    // PARTIAL: Ghidra types the symbol UnkTable_800836d4[30] = 32400 bytes, which ends at
    // 0x8008B563 and is SHORT of what FUN_80035700's own memset covers. That type is a guess on an
    // undefined label; the two memsets are instructions. The memsets are what is modelled here.
    //
    // Not registered with LibGpu.RamRegion: nothing in this cluster AddPrims out of it.
    private const int UnkStructArray800836d4Address = unchecked((int)0x800836D4);

    internal static readonly byte[] UnkStruct_Array_800836d4 = new byte[0xba48];

    // GHIDRA: DAT_80077a50 @ 0x80077A50
    // Initialised .data (0x80074FB4..0x800831B3), lifted out of the overlay image with read-memory.
    // One LZSS block in FUN_80035778's format: command count 0x0095 = 149 in the first halfword.
    //
    // The extent is CLOSED, not assumed. Two independent facts agree: the next label in the image
    // is PTR_DAT_80077b38 at +0xE8 = 232 bytes, and running FUN_80035778's own decode over these
    // 232 bytes consumes exactly 232 source bytes and produces exactly 0x800 output bytes — which
    // is exactly the 0x10 halfwords by 0x40 rows that FUN_80035700's upload asks for.
    private const int Dat80077a50Address = unchecked((int)0x80077A50);

    internal static readonly byte[] DAT_80077a50 =
    {
        0x95, 0x00, 0x7F, 0x88, 0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00,
        0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00, 0xFF, 0xFC, 0x00, 0xFC, 0x00, 0xFC,
        0x00, 0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00, 0xFF,
        0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00, 0xFC, 0x00,
        0xFC, 0x00, 0xFC, 0x00, 0xC9, 0xFC, 0x00, 0xA8, 0x00, 0x68, 0x86, 0x74,
        0x0F, 0x66, 0x68, 0x48, 0x0F, 0xD5, 0x08, 0x24, 0x0C, 0x26, 0x86, 0x0C,
        0x09, 0x86, 0x44, 0x0F, 0x66, 0x14, 0x02, 0x41, 0x68, 0x08, 0x05, 0x66,
        0x66, 0x86, 0x68, 0x66, 0x3C, 0x0F, 0x69, 0x86, 0x0C, 0x05, 0x08, 0x16,
        0x68, 0x0C, 0x1B, 0x86, 0x66, 0x40, 0x0F, 0x89, 0x0C, 0x00, 0x86, 0x66,
        0x65, 0x10, 0x09, 0x06, 0x66, 0x54, 0x0F, 0x08, 0x68, 0x56, 0x66, 0x68,
        0x0C, 0x07, 0x00, 0x66, 0x66, 0x42, 0x56, 0x3C, 0x0F, 0x56, 0x55, 0x00,
        0x65, 0x14, 0x25, 0x65, 0x51, 0x00, 0x08, 0x1C, 0x05, 0x3C, 0x0F, 0x55,
        0x00, 0x50, 0x10, 0x0D, 0x00, 0x60, 0x65, 0x00, 0x60, 0x65, 0x66, 0x56,
        0x00, 0x80, 0x3C, 0x0F, 0x00, 0x00, 0x06, 0x56, 0x66, 0x65, 0x66, 0x14,
        0x06, 0x50, 0x06, 0x0C, 0x18, 0x05, 0x48, 0x0F, 0x60, 0x65, 0x80, 0x08,
        0x10, 0x00, 0x06, 0x00, 0x00, 0x65, 0x56, 0x60, 0x42, 0x00, 0x48, 0x0F,
        0x50, 0x66, 0x65, 0x56, 0x10, 0x17, 0x60, 0x22, 0x05, 0x06, 0x4C, 0x0F,
        0x65, 0x50, 0x06, 0x0C, 0x46, 0x00, 0x62, 0x00, 0x10, 0x15, 0x44, 0x0F,
        0x06, 0x06, 0x60, 0x14, 0x08, 0x05, 0xD8, 0x50, 0x0F, 0x08, 0x0C, 0x06,
        0x1C, 0x08, 0x48, 0x0F,
    };

    // GHIDRA: PTR_DAT_80077b38 @ 0x80077B38
    // Initialised .data, lifted out of the overlay image with read-memory. Uploaded RAW, no
    // decompression: 0xA0 halfwords by 1 row = 320 bytes, which is exactly the gap to the next
    // thing in the image. The `PTR_` in Ghidra's name is its pointer guess on the first word
    // (0x80000000); the code takes the label's ADDRESS and hands it to a VRAM upload.
    private const int PtrDat80077b38Address = unchecked((int)0x80077B38);

    internal static readonly byte[] PTR_DAT_80077b38 =
    {
        0x00, 0x00, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x1F, 0x80,
        0x16, 0x80, 0x00, 0x80, 0xB0, 0x94, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x84, 0x00, 0x80, 0x00, 0x80, 0x00, 0x00, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x7B, 0xD7, 0x59, 0xDB, 0x00, 0x80,
        0xEE, 0xB5, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x80, 0x00, 0x00, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x80, 0xB5, 0x82, 0x31, 0x96, 0x00, 0x80, 0x4A, 0xA9, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x00, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0xFB, 0xEC,
        0xF5, 0xD4, 0x00, 0x80, 0x0F, 0xB9, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x84, 0x00, 0x80, 0x00, 0x80, 0x00, 0x00, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x02, 0xD0, 0x03, 0xB8, 0x00, 0x80,
        0x4A, 0xA9, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x84,
        0x00, 0x80, 0x00, 0x80, 0x00, 0x00, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x80, 0x69, 0x9F, 0xAA, 0x9E, 0x00, 0x80, 0xC7, 0xA1, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x84, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x00, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0xEC,
        0x00, 0xD4, 0x00, 0x80, 0x6A, 0xB9, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x84, 0x00, 0x80, 0x00, 0x80, 0x00, 0x00, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x5F, 0xBD, 0xDA, 0xAC, 0x00, 0x80,
        0x4E, 0xA9, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x84,
        0x00, 0x80, 0x00, 0x80, 0x00, 0x00, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x80, 0xE0, 0xFF, 0x00, 0xC2, 0x00, 0x80, 0x4A, 0xA9, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x84, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x00, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0xAD, 0x81,
        0xE7, 0x80, 0x00, 0x80, 0x4A, 0xA9, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80,
        0x00, 0x80, 0x00, 0x84, 0x00, 0x80, 0x00, 0x80,
    };

    // GHIDRA: FUN_80027174 @ 0x80027174
    // Its own PSX address, so the task block FUN_80027354 creates carries exactly what the console
    // stores at +0x04.
    private const int FUN_80027174_Address = unchecked((int)0x80027174);

    // GHIDRA: FUN_80046cb8 @ 0x80046CB8
    // Its own PSX address, stored by FUN_800474a0 and never called here.
    //
    // BLOCKED: the body is 1724 bytes of flag cascade whose every arm calls one of twenty callees,
    // none of them transliterated (FUN_8004682C, FUN_800468E0, FUN_800469A4, FUN_800469F8,
    // FUN_800466E8, FUN_8004638C, FUN_80045C60, FUN_80045DD0, FUN_80040128, FUN_8004126C,
    // FUN_8004236C, FUN_80043074, FUN_8003D85C, FUN_8004492C, FUN_8003D914, FUN_8003D9C0,
    // FUN_8003DBF8, FUN_8003DCE4, FUN_80045EF8, FUN_80046BE8). It is therefore NOT registered with
    // TaskSystem, and the tasks FUN_800474a0 creates do nothing when their turn comes. Their blocks
    // and their 0x240-byte contexts are byte-identical to the console's either way.
    private const int FUN_80046cb8_Address = unchecked((int)0x80046CB8);

    // GHIDRA: FUN_80027354 @ 0x80027354
    // Zeroes the thirty 0x24-byte records and creates the task that walks them. param_5 is 1 here,
    // the only non-zero param_5 in this subtree: TaskSystem's counter semantics make the callback
    // skip its first turn.
    internal static void FUN_80027354()
    {
        memset(UnkStruct_Array_800836d4, 0, '\0', 0x438);
        TaskSystem.RegisterCallback(FUN_80027174_Address, () => FUN_80027174());

        // DAT_800798d4 @ 0x800798D4 is g_TaskListTail[11]: g_TaskListTail sits at 0x800798A8 and
        // (0x800798D4 - 0x800798A8) / 4 = 11, matching the list index 0xb passed as a2.
        TaskSystem.CreateTask(FUN_80027174_Address, 0, 0xb, 0, 1, TaskSystem.g_TaskListTail[0xb]);
    }

    // GHIDRA: FUN_80027174 @ 0x80027174
    // Walks the thirty records. For each ACTIVE one it decrements a countdown; when that goes
    // negative it clamps it to 0, clears the record, and decrements a byte at +0x227 of the owner
    // the record points at. Otherwise it renders the record through FUN_80048f88.
    //
    // Record layout, 0x24 bytes, read off the synchronized disassembly (s1 is the record base and
    // s0 = s1 + 0x10, so every N(s0) in the listing is record + 0x10 + N):
    //   +0x00 u32 active flag      +0x04 u32 countdown, also the r/g/b source as (n & 7) << 5
    //   +0x08 u16 world X          +0x0A s16 world Y        +0x0C u16 world Z
    //   +0x0E     not read here
    //   +0x10 u32 sprite group     +0x14 u32 ordering bias  +0x18 s16 packed flag/angle
    //   +0x1A u16 clut bias        +0x1C u16 two fields     +0x1E u16 / u8 two more
    //   +0x20 u32 owner pointer
    //
    // HONEST LIMIT: nothing in this port writes a non-zero +0x00 into any record, because the
    // producer side of the list is not transliterated. Thirty idle iterations and a 0 return is the
    // correct behaviour for an empty list, and it is also all that can be observed today.
    internal static int FUN_80027174()
    {
        uint uVar1;
        int puVar2;
        int pUVar3;
        int iVar4;
        int iVar5;

        pUVar3 = UnkStructArray800836d4Address;
        iVar4 = 0;
        puVar2 = UnkStructArray800836d4Address + 0x10;
        do
        {
            if (PsxRam.ReadI32(pUVar3) != 0)
            {
                uVar1 = (uint)PsxRam.ReadI32(puVar2 + -0xc);
                PsxRam.WriteI32(puVar2 + -0xc, (int)(uVar1 - 1));
                if ((int)(uVar1 - 1) < 0)
                {
                    PsxRam.WriteI32(puVar2 + -0xc, 0);
                    PsxRam.WriteI32(pUVar3, 0);

                    // The owner pointer is loaded once, into v1 at 0x800271D0; the decompiler
                    // prints it twice because the byte is read and written back.
                    iVar5 = PsxRam.ReadI32(puVar2 + 0x10);
                    PsxRam.WriteU8(iVar5 + 0x227, (byte)(PsxRam.ReadU8(iVar5 + 0x227) + -1));
                }
                else
                {
                    // DAT_1f8000b4 @ 0x1F8000B4 and DAT_1f8000bc @ 0x1F8000BC are read here with
                    // lhu (0x800272B8 and 0x800272D0), that is the LOW halfword of the 32-bit
                    // scratchpad globals GteScratch spells _DAT_1f8000b4 / _DAT_1f8000bc.
                    //
                    // The casts below are forced by C# and none of them is observable: the
                    // original's `(int)(x * 0x10000) >> 0x10` is a sign-extension of the low 16
                    // bits, param_11 receives a value with bit 15 set, and param_5 is loaded with a
                    // signed lh but every use inside FUN_80048f88 is a mask.
                    SpriteRenderer.FUN_80048f88(
                        PsxRam.ReadI32(puVar2),
                        unchecked((short)((uint)PsxRam.ReadU16(puVar2 + -8)
                                          - (uint)(ushort)GteScratch._DAT_1f8000b4)),
                        unchecked((short)PsxRam.ReadU16(puVar2 + -6)),
                        unchecked((short)((uint)PsxRam.ReadU16(puVar2 + -4)
                                          - (uint)(ushort)GteScratch._DAT_1f8000bc)),
                        PsxRam.ReadU16(puVar2 + 8),
                        0,
                        0,
                        0x249,
                        0x249,
                        PsxRam.ReadI32(puVar2 + 4),
                        unchecked((short)(PsxRam.ReadU16(puVar2 + 10) | 0x8000)),
                        unchecked((short)(((PsxRam.ReadU16(puVar2 + 0xc) >> 6)
                                           + ((PsxRam.ReadU16(puVar2 + 0xe) >> 8) * 0x10)) | 0x20)),
                        (byte)((PsxRam.ReadU16(puVar2 + 0xc) & 0x3f) << 2),
                        PsxRam.ReadU8(puVar2 + 0xe),
                        (byte)(((uint)PsxRam.ReadI32(puVar2 + -0xc) & 7) << 5),
                        (byte)(((uint)PsxRam.ReadI32(puVar2 + -0xc) & 7) << 5),
                        (byte)(((uint)PsxRam.ReadI32(puVar2 + -0xc) & 7) << 5),
                        GteScratch.DAT_1f800128);
                }
            }

            pUVar3 = pUVar3 + 0x24;
            puVar2 = puVar2 + 0x24;
            iVar4 = iVar4 + 1;
        } while (iVar4 < 0x1e);

        return 0;
    }

    // GHIDRA: FUN_800350f4 @ 0x800350F4
    // Scans six slots from the last one down, takes the first whose leading int is zero, and writes
    // two bytes into it. Reports 0 on success and -1 when all six are taken.
    //
    // The loop addresses the slots with the magic constant -0x7ff7e34c and the exit path addresses
    // them with the symbol. The two agree exactly: with iVar1 = 0xb610 the first form gives
    // 0xb610 + 0x80081cb4 = 0x8008D2C4, and 0x800836D4 + 5 * 0x1e58 + 0x438 is the same address.
    // Both spellings are kept, because both are what the machine does.
    internal static uint FUN_800350f4(ushort param_1, ushort param_2)
    {
        int slot_ptr;
        uint result;
        int slot_index;
        int iVar1;
        int data_ptr;

        slot_index = 6;
        iVar1 = 0xb610;
        do
        {
            slot_index = slot_index + -1;
            if (slot_index < 0)
            {
                data_ptr = 0;
                goto LAB_80035150;
            }

            slot_ptr = unchecked(iVar1 + -0x7ff7e34c);
            iVar1 = iVar1 + -0x1e58;
        } while (PsxRam.ReadI32(slot_ptr) != 0);

        data_ptr = unchecked(UnkStructArray800836d4Address + (slot_index * 0x1e58) + 0x438);

    LAB_80035150:
        result = 0;
        if (data_ptr == 0)
        {
            result = 0xffffffff;
        }
        else
        {
            PsxRam.WriteU16(data_ptr, (ushort)(param_1 & 0xff));
            PsxRam.WriteU16(data_ptr + 2, (ushort)(param_2 & 0xff));
        }

        return result;
    }

    // GHIDRA: FUN_80035700 @ 0x80035700
    // Zeroes the six 0x1E58 slots, decompresses one small texture straight into VRAM, and uploads
    // one raw strip.
    //
    // The memset target is 0x80083B0C, NOT 0x800836D4: `UnkStruct_Array_800836d4 + 1` is pointer
    // arithmetic on a 0x438-byte element type, and the cross-reference from 0x80035708 resolves to
    // 0x80083B0C. 0x800836D4 + 0x438 = 0x80083B0C, and 0xB610 = 6 * 0x1E58 — the same six slots
    // FUN_800350f4 walks.
    internal static void FUN_80035700()
    {
        memset(UnkStruct_Array_800836d4, 0x438, '\0', 0xb610);
        TitleImages.FUN_80057b08(Dat80077a50Address, 0x380, 0x180, 0x10, 0x40, '\0');
        DisplayMachine.LoadImageInVram(
            ToWordBuffer(PTR_DAT_80077b38, 0xa0 * 1 * 2), 0, 0x1ea, 0xa0, 1, '\0');
    }

    // GHIDRA: FUN_8004737c @ 0x8004737C
    // Creates six sub-objects and files their task-block addresses into two three-slot tables inside
    // its argument, zeroing a third table beside each. Its argument is the +0x08 context of the
    // 0x3034-byte task FUN_80058a9c creates for LAB_8004c010.
    //
    // The (param_2, param_3) pairs are (0,0) (1,1) (2,2) (3,6) (4,7) (5,8): param_2 counts 0..5
    // while param_3 skips 3, 4 and 5. What the two numbers select is NOT closed by anything read
    // here, so nothing below interprets them.
    internal static void FUN_8004737c(int param_1)
    {
        int uVar1;

        uVar1 = FUN_800474a0(param_1, 0, 0);
        PsxRam.WriteI32(param_1 + 0x1520, uVar1);
        uVar1 = FUN_800474a0(param_1, 1, 1);
        PsxRam.WriteI32(param_1 + 0x1524, uVar1);
        uVar1 = FUN_800474a0(param_1, 2, 2);
        PsxRam.WriteI32(param_1 + 0x1528, uVar1);
        PsxRam.WriteI32(param_1 + 0x1534, 0);
        PsxRam.WriteI32(param_1 + 0x1530, 0);
        PsxRam.WriteI32(param_1 + 0x152c, 0);
        uVar1 = FUN_800474a0(param_1, 3, 6);
        PsxRam.WriteI32(param_1 + 0x1538, uVar1);
        uVar1 = FUN_800474a0(param_1, 4, 7);
        PsxRam.WriteI32(param_1 + 0x153c, uVar1);
        uVar1 = FUN_800474a0(param_1, 5, 8);
        PsxRam.WriteI32(param_1 + 0x1540, uVar1);
        PsxRam.WriteI32(param_1 + 0x154c, 0);
        PsxRam.WriteI32(param_1 + 0x1548, 0);
        PsxRam.WriteI32(param_1 + 0x1544, 0);
    }

    // GHIDRA: FUN_800474a0 @ 0x800474A0
    // Creates one 0x240-byte task in list 10 and initialises its context: about twenty scalar
    // fields, a branch that picks one of two six-halfword blocks, twenty-four interior self-pointers
    // at +0x0C..+0x7C, and a registration through FUN_800350f4.
    //
    // `puVar1` is the task block and `*(int *)(puVar1 + 4)` is `undefined2 *` arithmetic, so it is
    // block + 8 — verified in the image: `lw v1,0x8(v0)` at 0x80047524. That is the +0x08 context
    // field TaskSystem names TaskContext, so `iVar1` is the context address. `*puVar1` in the final
    // call is the halfword at block + 0x00, TaskSystem's TaskId, verified as `lhu v0,0x0(v1)` at
    // 0x800478E8; CreateTask was passed id 0.
    //
    // `iVar1 + 300` and `iVar1 + 200` are decimal in Ghidra's output, that is +0x12C and +0xC8.
    //
    // PARTIAL: what any of the twenty-four self-pointers stands for is not established by anything
    // read here, and neither is the meaning of the two index arguments. The stores are transcribed,
    // not interpreted.
    internal static int FUN_800474a0(int param_1, ushort param_2, byte param_3)
    {
        int puVar1;
        int iVar1;

        // DAT_800798d0 @ 0x800798D0 is g_TaskListTail[10]: (0x800798D0 - 0x800798A8) / 4 = 10,
        // matching the list index 10 passed as a2.
        puVar1 = TaskSystem.CreateTask(
            FUN_80046cb8_Address, 0, 10, 0x240, 1, TaskSystem.g_TaskListTail[10]);
        if (puVar1 != 0)
        {
            iVar1 = PsxRam.ReadI32(puVar1 + 8);
            PsxRam.WriteI32(iVar1 + 0xac, puVar1);
            PsxRam.WriteI32(iVar1 + 0xf0, param_1);
            PsxRam.WriteU8(iVar1 + 0x173, param_3);
            PsxRam.WriteU16(iVar1 + 0x154, 0);
            PsxRam.WriteU16(iVar1 + 0x114, 0);
            PsxRam.WriteU16(iVar1 + 0x164, 0);
            PsxRam.WriteU16(iVar1 + 0x116, 0);
            PsxRam.WriteU16(iVar1 + 0x166, 0);
            PsxRam.WriteU16(iVar1 + 0x118, 0);
            PsxRam.WriteU16(iVar1 + 0x168, 0);
            PsxRam.WriteU16(iVar1 + 0x11c, 0);
            PsxRam.WriteU16(iVar1 + 0x11e, 0);
            PsxRam.WriteU16(iVar1 + 0x120, 0);

            // DAT_801ff100 @ 0x801FF100 is a 16-bit global inside the shared high-RAM span
            // SharedHighRam models (base 0x801FF000, 0x248 bytes), so it is short index 0x80 of
            // SHORT_ARRAY_801ff000. main @ 0x800581DC writes 2 there on every pass of its loop,
            // so on this path the else branch is the one taken.
            if (SharedHighRam.SHORT_ARRAY_801ff000[0x80] == 1)
            {
                PsxRam.WriteU16(iVar1 + 0xb8, 20000);
                PsxRam.WriteU16(iVar1 + 0xba, 0x78);
                PsxRam.WriteU16(iVar1 + 0xbc, 20000);
                PsxRam.WriteU16(iVar1 + 0xb0, 0xb1e0);
                PsxRam.WriteU16(iVar1 + 0xb2, 0xf448);
                PsxRam.WriteU16(iVar1 + 0xb4, 0xb1e0);
            }
            else
            {
                PsxRam.WriteU16(iVar1 + 0xb8, 0x1e0);
                PsxRam.WriteU16(iVar1 + 0xba, 0x78);
                PsxRam.WriteU16(iVar1 + 0xbc, 0x1e0);
                PsxRam.WriteU16(iVar1 + 0xb0, 0xfe20);
                PsxRam.WriteU16(iVar1 + 0xb2, 0xfd00);
                PsxRam.WriteU16(iVar1 + 0xb4, 0xfe20);
            }

            PsxRam.WriteU16(iVar1 + 0x110, 0);
            PsxRam.WriteI32(iVar1 + 0x104, puVar1);
            PsxRam.WriteI32(iVar1 + 0xf8, 0);
            PsxRam.WriteI32(iVar1 + 0xfc, 0);
            PsxRam.WriteI32(iVar1 + 0xc, iVar1 + 0x10);
            PsxRam.WriteI32(iVar1 + 0x10, iVar1 + 0x114);
            PsxRam.WriteI32(iVar1 + 0x14, iVar1 + 0x11c);
            PsxRam.WriteI32(iVar1 + 0x18, iVar1 + 0x114);
            PsxRam.WriteI32(iVar1 + 0x1c, iVar1 + 0x80);
            PsxRam.WriteI32(iVar1 + 0x20, iVar1 + 0xd0);
            PsxRam.WriteI32(iVar1 + 0x24, iVar1 + 0xb0);
            PsxRam.WriteI32(iVar1 + 0x28, iVar1 + 0xb8);
            PsxRam.WriteI32(iVar1 + 0x2c, iVar1 + 0x134);
            PsxRam.WriteI32(iVar1 + 0x34, iVar1);
            PsxRam.WriteI32(iVar1 + 0x38, iVar1 + 0xf4);
            PsxRam.WriteI32(iVar1 + 0x3c, iVar1 + 0x124);
            PsxRam.WriteI32(iVar1 + 0x40, iVar1 + 300);
            PsxRam.WriteI32(iVar1 + 0x44, iVar1 + 0xe0);
            PsxRam.WriteI32(iVar1 + 0x48, iVar1 + 0xc0);
            PsxRam.WriteI32(iVar1 + 0x30, iVar1 + 0x16b);
            PsxRam.WriteI32(iVar1 + 0x54, iVar1 + 0x16a);
            PsxRam.WriteI32(iVar1 + 100, puVar1);
            PsxRam.WriteI32(iVar1 + 0x68, iVar1 + 0x160);
            PsxRam.WriteI32(iVar1 + 0x4c, iVar1 + 0x162);
            PsxRam.WriteI32(iVar1 + 0x70, iVar1 + 0x138);
            PsxRam.WriteI32(iVar1 + 0x58, iVar1 + 0x226);
            PsxRam.WriteI32(iVar1 + 0x5c, iVar1 + 0x176);
            PsxRam.WriteI32(iVar1 + 0x74, iVar1 + 200);
            PsxRam.WriteI32(iVar1 + 0x78, iVar1 + 0x228);
            PsxRam.WriteI32(iVar1 + 0x7c, iVar1 + 0x224);
            PsxRam.WriteU16(iVar1 + 0x160, param_2);
            PsxRam.WriteI32(iVar1 + 0x144, 0);
            PsxRam.WriteU8(iVar1 + 0x174, 0);
            FUN_800350f4(PsxRam.ReadU16(puVar1), PsxRam.ReadU8(iVar1 + 0x173));
        }

        return puVar1;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: LoadImageInVram @ 0x80057BB4 takes the u_long * form, so the source bytes are packed
    // into PSX words for it. Same bridge as the private ones in TitleImages and LoadingScreen.
    private static ulong[] ToWordBuffer(byte[] source, int byteCount)
    {
        if (byteCount <= 0 || byteCount > source.Length)
        {
            byteCount = source.Length;
        }

        int words = (byteCount + 3) / 4;
        ulong[] result = new ulong[words];
        for (int i = 0; i < words; i++)
        {
            int o = i * 4;
            uint word = source[o];
            if (o + 1 < byteCount) word |= (uint)source[o + 1] << 8;
            if (o + 2 < byteCount) word |= (uint)source[o + 2] << 16;
            if (o + 3 < byteCount) word |= (uint)source[o + 3] << 24;
            result[i] = word;
        }

        return result;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: lets the overlay's address resolver map the three spans above, since every function
    // in this file addresses them by raw PSX address exactly as the original does.
    internal static (byte[] Buffer, int Offset)? Resolve(int address)
    {
        int offset = address - UnkStructArray800836d4Address;
        if (offset >= 0 && offset < UnkStruct_Array_800836d4.Length)
        {
            return (UnkStruct_Array_800836d4, offset);
        }

        offset = address - Dat80077a50Address;
        if (offset >= 0 && offset < DAT_80077a50.Length)
        {
            return (DAT_80077a50, offset);
        }

        offset = address - PtrDat80077b38Address;
        if (offset >= 0 && offset < PTR_DAT_80077b38.Length)
        {
            return (PTR_DAT_80077b38, offset);
        }

        return null;
    }
}
