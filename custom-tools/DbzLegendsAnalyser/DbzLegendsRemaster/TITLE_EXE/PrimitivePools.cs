using PsxSdkMonogame;

namespace DbzLegendsRemaster.TITLE_EXE;

// The GPU primitive pools. CreatePrimitivePools @ 0x80056DC0 creates one task whose 0x60-byte context is
// three parallel arrays of eight entries — pool pointer at +0x00, element count at +0x20, and a
// third word at +0x40 — one slot per primitive type. Each slot is then filled by AllocatePrimitivePool,
// which allocates count * elementSize bytes on the heap and pre-tags every primitive in it.
//
// The pools are real memory: allocated by the same malloc as the task blocks and addressed through
// PsxRam, so a primitive written here is byte-identical to one on the console.
internal static class PrimitivePools
{
    // GHIDRA: g_PrimitiveSizeTable @ 0x8007ADF8
    // Element size per slot, read straight out of .data. They are the eight PsyQ primitive struct
    // sizes, and each one matches the pointer stride InitializePrimitivePool uses for that slot.
    private static readonly int[] g_PrimitiveSizeTable = { 0x20, 0x28, 0x28, 0x34, 0x14, 0x18, 0x1C, 0x24 };

    // GHIDRA: g_PrimitivePoolContext @ 0x800835F8
    // Address of the live pool context; every later caller of FreePrimitivePool reaches it through this.
    internal static int g_PrimitivePoolContext;

    // GHIDRA: ResetPrimitivePoolCursors @ 0x80056D84
    // The address the task block carries at +0x04. Ghidra has no function defined here — it is a
    // bare label sitting in a gap — so the address is kept as a constant and the body below is
    // decoded from the raw bytes rather than from a decompilation.
    private const int ResetPrimitivePoolCursors_Address = unchecked((int)0x80056D84);

    // GHIDRA: ResetPrimitivePoolCursors @ 0x80056D84
    // Fifteen instructions, zero callees, read out of memory at 0x80056D84 and decoded by hand.
    // The end of the label is fixed by the next word: 0x27BDFFC8 at 0x80056DC0 is CreatePrimitivePools's
    // own `addiu sp,sp,-0x38` prologue.
    //
    //   0x80056D84  addu  a0, zero, zero      ; a0 = 0, the counter
    //   0x80056D88  lui   a1, 0x8008
    //   0x80056D8C  lw    a1, 0x3224(a1)      ; a1 = g_CurrentTask, the task being executed
    //   0x80056D90  andi  v0, a0, 0xffff      <- loop head (branch target)
    //   0x80056D94  addiu a0, a0, 0x1
    //   0x80056D98  lw    v1, 0x8(a1)         ; the context pointer, re-read every iteration
    //   0x80056D9C  sll   v0, v0, 0x2
    //   0x80056DA0  addu  v0, v0, v1
    //   0x80056DA4  sw    zero, 0x40(v0)
    //   0x80056DA8  andi  v0, a0, 0xffff
    //   0x80056DAC  sltiu v0, v0, 0x7
    //   0x80056DB0  bne   v0, zero, 0x80056D90
    //   0x80056DB4  nop
    //   0x80056DB8  jr    ra
    //   0x80056DBC  nop
    //
    // So it zeroes the third word array at +0x40 of the pool context — the per-slot allocation
    // cursor DrawSpriteGroup bumps at 0x800494AC (`sw v0,0x44(v1)`, slot 1). CreateTask puts this task
    // in list 0 with counter 0, and RunFrameLoop sweeps list 0 every frame, so the cursors are
    // reset once per frame and each frame hands primitives out from the start of every pool again.
    //
    // THE BOUND IS SEVEN, AND CreatePrimitivePools AGREES — the two do NOT disagree. The instruction word
    // 0x2C420007 (`sltiu v0,v0,0x7`) appears here at 0x80056DAC and identically at 0x80056E48 in
    // CreatePrimitivePools's own +0x00 initialisation loop, and again at 0x80056F14 and 0x80056F38 in its
    // teardown and validation loops. All four use the counter's PRE-increment value as the index
    // and test the POST-increment value, so all four write indices 0..6 and never touch index 7.
    // Index 7 is a real slot: g_PrimitiveSizeTable sizes eight entries, and AllocatePrimitivePool accepts param_2
    // up to 7 and is called with 7 by CreatePrimitivePools. Reproduced verbatim — rule 12 forbids
    // repairing a behaviour of the original.
    // PARTIAL: the control flow is closed from the bytes, but WHY the eighth slot is skipped is
    // not. On the title-screen path CreatePrimitivePools is called with param_8 == 0, so slot 7 is never
    // allocated and the omission is unobservable there; nothing in the evidence I have says what
    // happens on a path that does allocate it.
    private static void ResetPrimitivePoolCursors()
    {
        // a1 at 0x80056D8C: the global is read ONCE, before the loop. The `lw v1,0x8(a1)` inside
        // the loop is what repeats.
        int task = TaskSystem.g_CurrentTask;
        ushort uVar2 = 0;
        uint uVar1 = 0;
        do
        {
            uVar2 = (ushort)(uVar2 + 1);
            PsxRam.WriteI32(PsxRam.ReadI32(task + 8) + (int)(uVar1 * 4) + 0x40, 0);
            uVar1 = uVar2;
        } while (uVar2 < 7);
    }

    // GHIDRA: CreatePrimitivePools @ 0x80056DC0
    // The CreateTask arguments were re-verified against the raw call setup at 0x80056DE8..0x80056E14:
    // a0 = 0x80056D84 (lui/addiu pair), a1 = 0, a2 = 0 (list index 0), a3 = 0x60, stack+0x10 = 0,
    // and stack+0x14 comes from `lw v0, 0x9854(0x8008_0000)` — the VALUE held in g_TaskListHead[0],
    // not the address of the array.
    internal static int CreatePrimitivePools(int param_1, int param_2, int param_3, int param_4,
        int param_5, int param_6, int param_7, int param_8)
    {
        // JUSTIFICATION: C# language bridge only
        // RELATION: the original hands &ResetPrimitivePoolCursors to CreateTask, which stores the raw pointer at
        // block+0x04. The block here still stores 0x80056D84, exactly as the console holds it; this
        // line is what lets the dispatcher turn that address back into the ported body. Same
        // pattern as TitleImages.SetupTitleScreen.
        TaskSystem.RegisterCallback(ResetPrimitivePoolCursors_Address, ResetPrimitivePoolCursors);

        int task = TaskSystem.CreateTask(ResetPrimitivePoolCursors_Address, 0, 0, 0x60, 0,
            TaskSystem.g_TaskListHead[0]);
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

            AllocatePrimitivePool(context, 0, param_1);
            AllocatePrimitivePool(context, 1, param_2);
            AllocatePrimitivePool(context, 2, param_3);
            AllocatePrimitivePool(context, 3, param_4);
            AllocatePrimitivePool(context, 4, param_5);
            AllocatePrimitivePool(context, 5, param_6);
            AllocatePrimitivePool(context, 6, param_7);
            AllocatePrimitivePool(context, 7, param_8);

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
                        FreePrimitivePool(context, uVar3);
                        uVar3 = uVar4;
                    } while (uVar4 < 7);

                    TaskSystem.DeleteTask(task, 0);
                    return -2;
                }

                uVar3 = (ushort)(uVar3 + 1);
                uVar2 = uVar3;
            } while (uVar3 < 7);

            uVar1 = 0;
            g_PrimitivePoolContext = context;
        }

        return uVar1;
    }

    // GHIDRA: AllocatePrimitivePool @ 0x80056F74
    internal static int AllocatePrimitivePool(int param_1, uint param_2, int param_3)
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
                    int pvVar2 = LibApi.malloc(param_3 * g_PrimitiveSizeTable[param_2]);
                    PsxRam.WriteI32(piVar3, pvVar2);
                    if (pvVar2 == 0)
                    {
                        uVar1 = -2;
                    }
                    else
                    {
                        PsxRam.WriteI32(piVar3 + 0x40, 0);
                        PsxRam.WriteI32(piVar3 + 0x20, param_3);
                        InitializePrimitivePool((ushort)param_2, (uint)param_3, PsxRam.ReadI32(piVar3));
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

    // GHIDRA: FreePrimitivePool @ 0x80057030
    //
    // The condition is inverted and this is NOT a decompiler artefact. Raw disassembly:
    //   lw   $v0, 0($s0)           ; the pool pointer
    //   bne  $v0, $zero, exit      ; a live pool leaves with -2, freeing nothing
    //   jal  free
    //   addu $a0, $zero, $zero     ; and the freed pointer is 0
    // So an allocated pool is never released, and the branch that does run frees NULL and clears
    // three words that are already zero. The author plainly meant `if (*p != 0) free(*p);`.
    // Reproduced as-is: rule 12 forbids repairing a bug of the original.
    internal static int FreePrimitivePool(int param_1, uint param_2)
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

    // GHIDRA: InitializePrimitivePool @ 0x80057094
    // Pre-tags every primitive of a freshly allocated pool. The per-case setter and pointer stride
    // are both read from the disassembly; each stride equals that slot's g_PrimitiveSizeTable entry.
    internal static void InitializePrimitivePool(ushort param_1, uint param_2, int param_3)
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
