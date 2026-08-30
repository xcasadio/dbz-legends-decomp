using PsxSdkMonogame;

namespace DbzLegendsRemaster.TITLE_EXE;

// The GPU primitive pools. FUN_80056dc0 @ 0x80056DC0 creates one task whose 0x60-byte context is
// three parallel arrays of eight entries — pool pointer at +0x00, element count at +0x20, and a
// third word at +0x40 — one slot per primitive type. Each slot is then filled by FUN_80056f74,
// which allocates count * elementSize bytes on the heap and pre-tags every primitive in it.
//
// The pools are real memory: allocated by the same malloc as the task blocks and addressed through
// PsxRam, so a primitive written here is byte-identical to one on the console.
internal static class PrimitivePools
{
    // GHIDRA: DAT_8007adf8 @ 0x8007ADF8
    // Element size per slot, read straight out of .data. They are the eight PsyQ primitive struct
    // sizes, and each one matches the pointer stride FUN_80057094 uses for that slot.
    private static readonly int[] DAT_8007adf8 = { 0x20, 0x28, 0x28, 0x34, 0x14, 0x18, 0x1C, 0x24 };

    // GHIDRA: DAT_800835f8 @ 0x800835F8
    // Address of the live pool context; every later caller of FUN_80057030 reaches it through this.
    internal static int DAT_800835f8;

    // GHIDRA: LAB_80056d84 @ 0x80056D84
    // The task callback FUN_80056dc0 registers. Its body is not transliterated yet, so nothing is
    // registered against this address and the dispatcher skips it.
    private const int LAB_80056d84 = unchecked((int)0x80056D84);

    // GHIDRA: FUN_80056dc0 @ 0x80056DC0
    internal static int FUN_80056dc0(int param_1, int param_2, int param_3, int param_4,
        int param_5, int param_6, int param_7, int param_8)
    {
        int task = TaskSystem.CreateTask(LAB_80056d84, 0, 0, 0x60, 0, TaskSystem.g_TaskListHead[0]);
        ushort uVar3 = 0;
        int uVar1;
        if (task == 0)
        {
            uVar1 = -1;
        }
        else
        {
            int context = PsxRam.ReadI32(task + 0x08);
            uint uVar2 = 0;
            do
            {
                uVar3 = (ushort)(uVar3 + 1);
                PsxRam.WriteI32(context + (int)(uVar2 * 4), 0);
                uVar2 = uVar3;
            } while (uVar3 < 7);

            FUN_80056f74(context, 0, param_1);
            FUN_80056f74(context, 1, param_2);
            FUN_80056f74(context, 2, param_3);
            FUN_80056f74(context, 3, param_4);
            FUN_80056f74(context, 4, param_5);
            FUN_80056f74(context, 5, param_6);
            FUN_80056f74(context, 6, param_7);
            FUN_80056f74(context, 7, param_8);

            uVar3 = 0;
            uVar2 = 0;
            do
            {
                if (PsxRam.ReadI32(context + (int)(uVar2 * 4)) < -1)
                {
                    ushort uVar4;
                    uVar3 = 0;
                    do
                    {
                        uVar4 = (ushort)(uVar3 + 1);
                        FUN_80057030(context, uVar3);
                        uVar3 = uVar4;
                    } while (uVar4 < 7);

                    TaskSystem.DeleteTask(task, 0);
                    return -2;
                }

                uVar3 = (ushort)(uVar3 + 1);
                uVar2 = uVar3;
            } while (uVar3 < 7);

            uVar1 = 0;
            DAT_800835f8 = context;
        }

        return uVar1;
    }

    // GHIDRA: FUN_80056f74 @ 0x80056F74
    internal static int FUN_80056f74(int param_1, uint param_2, int param_3)
    {
        int uVar1;
        if (param_3 == 0)
        {
            uVar1 = -1;
        }
        else
        {
            param_2 = param_2 & 0xffff;
            if (param_2 < 8)
            {
                int piVar3 = (int)(param_2 * 4) + param_1;
                uVar1 = -3;
                if (PsxRam.ReadI32(piVar3) == 0)
                {
                    int pvVar2 = LibApi.malloc(param_3 * DAT_8007adf8[param_2]);
                    PsxRam.WriteI32(piVar3, pvVar2);
                    if (pvVar2 == 0)
                    {
                        uVar1 = -2;
                    }
                    else
                    {
                        PsxRam.WriteI32(piVar3 + 0x40, 0);
                        PsxRam.WriteI32(piVar3 + 0x20, param_3);
                        FUN_80057094((ushort)param_2, (uint)param_3, PsxRam.ReadI32(piVar3));
                        uVar1 = 0;
                    }
                }
            }
            else
            {
                uVar1 = -4;
            }
        }

        return uVar1;
    }

    // GHIDRA: FUN_80057030 @ 0x80057030
    //
    // The condition is inverted and this is NOT a decompiler artefact. Raw disassembly:
    //   lw   $v0, 0($s0)           ; the pool pointer
    //   bne  $v0, $zero, exit      ; a live pool leaves with -2, freeing nothing
    //   jal  free
    //   addu $a0, $zero, $zero     ; and the freed pointer is 0
    // So an allocated pool is never released, and the branch that does run frees NULL and clears
    // three words that are already zero. The author plainly meant `if (*p != 0) free(*p);`.
    // Reproduced as-is: rule 12 forbids repairing a bug of the original.
    internal static int FUN_80057030(int param_1, uint param_2)
    {
        int uVar1;
        if ((param_2 & 0xffff) < 8)
        {
            int piVar2 = (int)((param_2 & 0xffff) * 4) + param_1;
            uVar1 = -2;
            if (PsxRam.ReadI32(piVar2) == 0)
            {
                LibApi.free(0);
                uVar1 = 0;
                PsxRam.WriteI32(piVar2, 0);
                PsxRam.WriteI32(piVar2 + 0x20, 0);
                PsxRam.WriteI32(piVar2 + 0x40, 0);
            }
        }
        else
        {
            uVar1 = -4;
        }

        return uVar1;
    }

    // GHIDRA: FUN_80057094 @ 0x80057094
    // Pre-tags every primitive of a freshly allocated pool. The per-case setter and pointer stride
    // are both read from the disassembly; each stride equals that slot's DAT_8007adf8 entry.
    internal static void FUN_80057094(ushort param_1, uint param_2, int param_3)
    {
        var resolved = PsxRam.AddressResolver?.Invoke(param_3);
        if (resolved == null)
        {
            return;
        }

        (byte[] buffer, int offset) = resolved.Value;
        uint uVar1 = 0;
        if (param_2 == 0)
        {
            return;
        }

        switch (param_1)
        {
            case 0:
                do
                {
                    LibGpu.SetPolyFT3(buffer, offset);
                    LibGpu.SetSemiTrans(buffer, offset, 1);
                    uVar1 = uVar1 + 1;
                    offset = offset + 0x20;
                } while (uVar1 < param_2);
                break;

            case 1:
                do
                {
                    LibGpu.SetPolyFT4(buffer, offset);
                    LibGpu.SetSemiTrans(buffer, offset, 1);
                    uVar1 = uVar1 + 1;
                    offset = offset + 0x28;
                } while (uVar1 < param_2);
                break;

            case 2:
                do
                {
                    LibGpu.SetPolyGT3(buffer, offset);
                    LibGpu.SetSemiTrans(buffer, offset, 1);
                    uVar1 = uVar1 + 1;
                    offset = offset + 0x28;
                } while (uVar1 < param_2);
                break;

            case 3:
                do
                {
                    LibGpu.SetPolyGT4(buffer, offset);
                    LibGpu.SetSemiTrans(buffer, offset, 1);
                    uVar1 = uVar1 + 1;
                    offset = offset + 0x34;
                } while (uVar1 < param_2);
                break;

            case 4:
                do
                {
                    LibGpu.SetPolyF3(buffer, offset);
                    LibGpu.SetSemiTrans(buffer, offset, 1);
                    uVar1 = uVar1 + 1;
                    offset = offset + 0x14;
                } while (uVar1 < param_2);
                break;

            case 5:
                do
                {
                    LibGpu.SetPolyF4(buffer, offset);
                    LibGpu.SetSemiTrans(buffer, offset, 1);
                    uVar1 = uVar1 + 1;
                    offset = offset + 0x18;
                } while (uVar1 < param_2);
                break;

            case 6:
                do
                {
                    LibGpu.SetPolyG3(buffer, offset);
                    LibGpu.SetSemiTrans(buffer, offset, 1);
                    uVar1 = uVar1 + 1;
                    offset = offset + 0x1c;
                } while (uVar1 < param_2);
                break;

            case 7:
                do
                {
                    LibGpu.SetPolyG4(buffer, offset);
                    LibGpu.SetSemiTrans(buffer, offset, 1);
                    uVar1 = uVar1 + 1;
                    offset = offset + 0x24;
                } while (uVar1 < param_2);
                break;
        }
    }
}
