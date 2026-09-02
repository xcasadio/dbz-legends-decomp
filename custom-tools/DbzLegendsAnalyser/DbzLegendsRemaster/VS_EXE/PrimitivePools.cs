using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's GPU primitive pools.
//
// THIS IS THE SAME SOURCE FILE TITLE.EXE COMPILED, RELINKED AT ANOTHER ADDRESS. The five routines
// below match TITLE_EXE/PrimitivePools.cs statement for statement — same eight-slot context, same
// three parallel arrays at +0x00 / +0x20 / +0x40, same bound-of-seven loops, same inverted test in
// FreePrimitivePool, and the same eight element sizes in .data. Only the addresses moved:
//
//   | routine                    | TITLE.EXE  | VS.EXE     |
//   |----------------------------|------------|------------|
//   | ResetPrimitivePoolCursors  | 0x80056D84 | 0x80060CDC |
//   | CreatePrimitivePools       | 0x80056DC0 | 0x80060D18 |
//   | AllocatePrimitivePool      | 0x80056F74 | 0x80060ECC |
//   | FreePrimitivePool          | 0x80057030 | 0x80060F88 |
//   | InitializePrimitivePool    | 0x80057094 | 0x80060FEC |
//   | g_PrimitiveSizeTable       | 0x8007ADF8 | 0x80084D54 |
//   | g_PrimitivePoolContext     | 0x800835F8 | 0x8008D534 |
//
// VS.EXE carries a SIXTH routine the title overlay has no counterpart for: InitializePolyFt4 @
// 0x80061204, which stamps one textured quad. It is transliterated here because it sits inside the
// same run of code, between InitializePrimitivePool and the end of the compilation unit at
// 0x800612DB, and because it is the same kind of work — pre-tagging a POLY_FT4.
//
// WHY THIS IS A SECOND FILE RATHER THAN A CALL INTO TITLE_EXE. The two overlays are separately
// linked programs; their globals live at different addresses and every function below carries its
// own `// GHIDRA:` line naming its own address. Calling TITLE_EXE's copy would make those lines
// false and would fold two original functions into one C# entry point, which rule 3 forbids. The
// SDK, by contrast, is genuinely shared: every LibGpu / LibApi call below goes to PsxSdkMonogame
// unchanged.
//
// The pools are real memory, allocated by the same malloc as the task blocks and addressed through
// PsxRam, so a primitive written here is byte-identical to one on the console.
internal static class PrimitivePools
{
    // GHIDRA: DAT_80084d54 @ 0x80084D54 (VS.EXE)
    // This is the g_PrimitiveSizeTable of TITLE.EXE @ 0x8007ADF8, byte for byte; the C# name comes
    // from there, the Ghidra symbol is still raw. Element size per slot, read out of .data one word
    // at a time and confirmed against the pointer stride InitializePrimitivePool uses for each slot.
    private static readonly int[] g_PrimitiveSizeTable = { 0x20, 0x28, 0x28, 0x34, 0x14, 0x18, 0x1C, 0x24 };

    // GHIDRA: DAT_8008d534 @ 0x8008D534 (VS.EXE)
    // This is the g_PrimitivePoolContext of TITLE.EXE @ 0x800835F8; the C# name comes from there,
    // the Ghidra symbol is still raw. Written by exactly one instruction in the program — 0x80060E9C,
    // the tail of CreatePrimitivePools — and read 28 times, all of them from the sprite path
    // (FUN_80052DB4 @ 0x80052DB4 and its neighbours, which walk +0x04 / +0x24 / +0x44 for slot 1).
    internal static int g_PrimitivePoolContext;

    // GHIDRA: LAB_80060cdc @ 0x80060CDC (VS.EXE)
    // The address the task block carries at +0x04. Ghidra has no function defined here — it is a
    // bare label, exactly as at TITLE.EXE's 0x80056D84 — so the address is kept as a constant and
    // the body below is decoded from the raw instructions rather than from a decompilation.
    private const int ResetPrimitivePoolCursors_Address = unchecked((int)0x80060CDC);

    // GHIDRA: LAB_80060cdc @ 0x80060CDC (VS.EXE)
    // This is the ResetPrimitivePoolCursors of TITLE.EXE @ 0x80056D84 at the word; the C# name comes
    // from there, the Ghidra symbol is still a bare label. Fifteen instructions, zero callees:
    //
    //   0x80060CDC  addu  a0, zero, zero      ; a0 = 0, the counter
    //   0x80060CE0  lui   a1, 0x8009
    //   0x80060CE4  lw    a1, ...(a1)         ; a1 = g_CurrentTask @ 0x8008D16C, the running task
    //   0x80060CE8  andi  v0, a0, 0xffff      <- loop head (branch target)
    //   0x80060CEC  addiu a0, a0, 0x1
    //   0x80060CF0  lw    v1, 0x8(a1)         ; the context pointer, re-read every iteration
    //   0x80060CF4  sll   v0, v0, 0x2
    //   0x80060CF8  addu  v0, v0, v1
    //   0x80060CFC  sw    zero, 0x40(v0)
    //   0x80060D00  andi  v0, a0, 0xffff
    //   0x80060D04  sltiu v0, v0, 0x7
    //   0x80060D08  bne   v0, zero, 0x80060CE8
    //   0x80060D0C  nop
    //   0x80060D10  jr    ra
    //   0x80060D14  nop
    //
    // The end of the label is fixed by the next word: 0x27BDFFC8 at 0x80060D18 is
    // CreatePrimitivePools's own `addiu sp,sp,-0x38` prologue.
    //
    // So it zeroes the third word array at +0x40 of the pool context — the per-slot allocation
    // cursor the sprite path bumps. CreateTask puts this task in list 0 with counter 0, and main's
    // frame loop sweeps list 0 every frame, so the cursors are reset once per frame and each frame
    // hands primitives out from the start of every pool again.
    //
    // THE BOUND IS SEVEN, AND CreatePrimitivePools AGREES — the two do NOT disagree. Both use the
    // counter's PRE-increment value as the index and test the POST-increment value, so both write
    // indices 0..6 and never touch index 7. Index 7 is a real slot: g_PrimitiveSizeTable sizes eight
    // entries, and AllocatePrimitivePool accepts param_2 up to 7 and is called with 7 by
    // CreatePrimitivePools. Reproduced verbatim — rule 12 forbids repairing a behaviour of the
    // original, and this is the identical omission TITLE.EXE ships.
    // PARTIAL: the control flow is closed from the bytes, but WHY the eighth slot is skipped is not.
    // main calls CreatePrimitivePools with param_8 == 0, so slot 7 is never allocated on the versus
    // path either and the omission stays unobservable; nothing in the evidence says what happens on
    // a path that does allocate it.
    private static void ResetPrimitivePoolCursors()
    {
        // a1 at 0x80060CE4: the global is read ONCE, before the loop. The `lw v1,0x8(a1)` inside the
        // loop is what repeats.
        int task = TaskSystem.g_CurrentTask;
        ushort uVar3 = 0;
        uint uVar2 = 0;
        do
        {
            uVar3 = (ushort)(uVar3 + 1);
            PsxRam.WriteI32(PsxRam.ReadI32(task + 8) + (int)(uVar2 * 4) + 0x40, 0);
            uVar2 = uVar3;
        } while (uVar3 < 7);
    }

    // GHIDRA: FUN_80060d18 @ 0x80060D18 (VS.EXE)
    // This is the CreatePrimitivePools of TITLE.EXE @ 0x80056DC0; the C# name comes from there, the
    // Ghidra symbol is still raw.
    //
    // main @ 0x80062134 calls it once, at 0x80062328, as
    // FUN_80060d18(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0) — eight counts, one per primitive slot,
    // in g_PrimitiveSizeTable order: 20 POLY_FT3, 200 POLY_FT4, 100 POLY_GT3, 350 POLY_GT4,
    // 20 POLY_F3, 20 POLY_F4, 0 POLY_G3, 0 POLY_G4. A count of zero leaves that slot unallocated —
    // AllocatePrimitivePool returns -1 without touching it — which is why the two shaded-untextured
    // slots cost nothing.
    //
    // The CreateTask arguments are the call setup at 0x80060D40..0x80060D68: a0 = 0x80060CDC
    // (lui/addiu pair), a1 = 0, a2 = 0 (list index 0), a3 = 0x60, stack+0x10 = 0, and stack+0x14
    // is DAT_80083b3c, which FUN_80053330's own body proves is g_TaskListHead[0] — its line
    // `(&DAT_80083b3c)[uVar3] = puVar2` indexes that address as the head array, and the tail array
    // sits 0x54 bytes above it at 0x80083B90, one word per list for 21 lists.
    internal static int CreatePrimitivePools(int param_1, int param_2, int param_3, int param_4,
        int param_5, int param_6, int param_7, int param_8)
    {
        // JUSTIFICATION: C# language bridge only
        // RELATION: the original hands &LAB_80060cdc to CreateTask, which stores the raw pointer at
        // block+0x04. The block here still stores 0x80060CDC, exactly as the console holds it; this
        // line is what lets the dispatcher turn that address back into the ported body.
        TaskSystem.RegisterCallback(ResetPrimitivePoolCursors_Address, ResetPrimitivePoolCursors);

        int iVar1 = TaskSystem.CreateTask(ResetPrimitivePoolCursors_Address, 0, 0, 0x60, 0,
            TaskSystem.g_TaskListHead[0]);
        ushort uVar4 = 0;
        int uVar2;
        if (iVar1 == 0)
        {
            uVar2 = -1;
        }
        else
        {
            uint uVar3 = 0;
            do
            {
                uVar4 = (ushort)(uVar4 + 1);
                PsxRam.WriteI32((int)(uVar3 * 4) + PsxRam.ReadI32(iVar1 + 8), 0);
                uVar3 = uVar4;
            } while (uVar4 < 7);

            uVar4 = 0;
            AllocatePrimitivePool(PsxRam.ReadI32(iVar1 + 8), 0, param_1);
            AllocatePrimitivePool(PsxRam.ReadI32(iVar1 + 8), 1, param_2);
            AllocatePrimitivePool(PsxRam.ReadI32(iVar1 + 8), 2, param_3);
            AllocatePrimitivePool(PsxRam.ReadI32(iVar1 + 8), 3, param_4);
            AllocatePrimitivePool(PsxRam.ReadI32(iVar1 + 8), 4, param_5);
            AllocatePrimitivePool(PsxRam.ReadI32(iVar1 + 8), 5, param_6);
            AllocatePrimitivePool(PsxRam.ReadI32(iVar1 + 8), 6, param_7);
            AllocatePrimitivePool(PsxRam.ReadI32(iVar1 + 8), 7, param_8);
            uVar3 = 0;
            do
            {
                if (PsxRam.ReadI32((int)(uVar3 * 4) + PsxRam.ReadI32(iVar1 + 8)) < -1)
                {
                    uVar4 = 0;
                    ushort uVar5;
                    do
                    {
                        uVar5 = (ushort)(uVar4 + 1);
                        FreePrimitivePool(PsxRam.ReadI32(iVar1 + 8), uVar4);
                        uVar4 = uVar5;
                    } while (uVar5 < 7);

                    TaskSystem.DeleteTask(iVar1, 0);
                    return -2;
                }

                uVar4 = (ushort)(uVar4 + 1);
                uVar3 = uVar4;
            } while (uVar4 < 7);

            uVar2 = 0;
            g_PrimitivePoolContext = PsxRam.ReadI32(iVar1 + 8);
        }

        return uVar2;
    }

    // GHIDRA: FUN_80060ecc @ 0x80060ECC (VS.EXE)
    // This is the AllocatePrimitivePool of TITLE.EXE @ 0x80056F74; the C# name comes from there, the
    // Ghidra symbol is still raw.
    //
    // ONE SUBSTANTIVE DIVERGENCE FROM THE TITLE.EXE PORT, and it is deliberate. TITLE_EXE's copy
    // reaches LibApi.malloc. VS.EXE links no PSYQ heap: the call at 0x80060F38 goes to
    // FUN_80062f94 @ 0x80062F94, the game-side allocator, which is the very same routine
    // CreateTask calls at 0x80053394 for its task blocks. Pools and task nodes therefore come out of
    // one arena on the console, and they must here too, or the PSX addresses this file stores in
    // the pool context would not belong to the heap the rest of the overlay walks.
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
                    int iVar2 = Heap.FUN_80062f94(param_3 * g_PrimitiveSizeTable[param_2]);
                    PsxRam.WriteI32(piVar3, iVar2);
                    if (iVar2 == 0)
                    {
                        uVar1 = -2;
                    }
                    else
                    {
                        // piVar3[0x10] and piVar3[8] in the decompilation: int-indexed, so +0x40
                        // (the cursor) and +0x20 (the element count).
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

    // GHIDRA: FUN_80060f88 @ 0x80060F88 (VS.EXE)
    // This is the FreePrimitivePool of TITLE.EXE @ 0x80057030; the C# name comes from there, the
    // Ghidra symbol is still raw.
    //
    // The condition is inverted, and this is NOT a decompiler artefact. Raw disassembly:
    //   0x80060FB0  lw   v0, 0x0(s0)          ; the pool pointer
    //   0x80060FB8  bne  v0, zero, 0x80060FD8 ; a live pool leaves with -2, freeing nothing
    //   0x80060FC0  jal  0x800631C8           ; free
    //   0x80060FC8  clear v0                  ; and the freed pointer is 0
    // So an allocated pool is never released, and the branch that does run frees NULL and clears
    // three words that are already zero. The author plainly meant `if (*p != 0) free(*p);`.
    // Reproduced as-is: rule 12 forbids repairing a bug of the original, and TITLE.EXE ships the
    // identical inversion at 0x80057030.
    //
    // FUN_800631c8 @ 0x800631C8 is the game-side free that pairs with FUN_80062f94 — five
    // instructions, `*(uint *)(param_1 - 4) |= 1`. Called here with a literal 0, so it marks the
    // word at address -4, which is what the console does too.
    internal static int FreePrimitivePool(int param_1, uint param_2)
    {
        int uVar1;
        if ((param_2 & 0xffff) < 8)
        {
            int piVar2 = (int)((param_2 & 0xffff) * 4) + param_1;
            uVar1 = -2;
            if (PsxRam.ReadI32(piVar2) == 0)
            {
                Heap.FUN_800631c8(0);
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

    // GHIDRA: FUN_80060fec @ 0x80060FEC (VS.EXE)
    // This is the InitializePrimitivePool of TITLE.EXE @ 0x80057094; the C# name comes from there,
    // the Ghidra symbol is still raw. Pre-tags every primitive of a freshly allocated pool. The
    // per-case setter and the pointer stride are both read from the decompilation, and each stride
    // equals that slot's g_PrimitiveSizeTable entry: 0x20, 0x28, 0x28, 0x34, 0x14, 0x18, 0x1C, 0x24.
    //
    // SetPolyFT3 .. SetPolyG4 and SetSemiTrans are the real psyq inline macros and live in the SDK:
    // VS.EXE reaches them at 0x8007AFAC..0x8007B038, TITLE.EXE at its own addresses, same code.
    internal static void InitializePrimitivePool(ushort param_1, uint param_2, int param_3)
    {
        // JUSTIFICATION: PSX hardware adaptation only
        // RELATION: param_3 is a raw PSX pointer into the heap. The SDK's primitive setters take the
        // backing byte array and an offset, so the address is resolved once here and walked with the
        // original's own stride. An address with no mapping cannot be written on desktop; the
        // original has no such case.
        var resolved = PsxRam.AddressResolver?.Invoke(param_3);
        if (resolved == null)
        {
            return;
        }

        (byte[] buffer, int offset) = resolved.Value;
        uint uVar1;

        switch (param_1)
        {
            case 0:
                uVar1 = 0;
                if (param_2 != 0)
                {
                    do
                    {
                        LibGpu.SetPolyFT3(buffer, offset);
                        LibGpu.SetSemiTrans(buffer, offset, 1);
                        uVar1 = uVar1 + 1;
                        offset = offset + 0x20;
                    } while (uVar1 < param_2);
                }

                break;

            case 1:
                uVar1 = 0;
                if (param_2 != 0)
                {
                    do
                    {
                        LibGpu.SetPolyFT4(buffer, offset);
                        LibGpu.SetSemiTrans(buffer, offset, 1);
                        uVar1 = uVar1 + 1;
                        offset = offset + 0x28;
                    } while (uVar1 < param_2);
                }

                break;

            case 2:
                uVar1 = 0;
                if (param_2 != 0)
                {
                    do
                    {
                        LibGpu.SetPolyGT3(buffer, offset);
                        LibGpu.SetSemiTrans(buffer, offset, 1);
                        uVar1 = uVar1 + 1;
                        offset = offset + 0x28;
                    } while (uVar1 < param_2);
                }

                break;

            case 3:
                uVar1 = 0;
                if (param_2 != 0)
                {
                    do
                    {
                        LibGpu.SetPolyGT4(buffer, offset);
                        LibGpu.SetSemiTrans(buffer, offset, 1);
                        uVar1 = uVar1 + 1;
                        offset = offset + 0x34;
                    } while (uVar1 < param_2);
                }

                break;

            case 4:
                uVar1 = 0;
                if (param_2 != 0)
                {
                    do
                    {
                        LibGpu.SetPolyF3(buffer, offset);
                        LibGpu.SetSemiTrans(buffer, offset, 1);
                        uVar1 = uVar1 + 1;
                        offset = offset + 0x14;
                    } while (uVar1 < param_2);
                }

                break;

            case 5:
                uVar1 = 0;
                if (param_2 != 0)
                {
                    do
                    {
                        LibGpu.SetPolyF4(buffer, offset);
                        LibGpu.SetSemiTrans(buffer, offset, 1);
                        uVar1 = uVar1 + 1;
                        offset = offset + 0x18;
                    } while (uVar1 < param_2);
                }

                break;

            case 6:
                uVar1 = 0;
                if (param_2 != 0)
                {
                    do
                    {
                        LibGpu.SetPolyG3(buffer, offset);
                        LibGpu.SetSemiTrans(buffer, offset, 1);
                        uVar1 = uVar1 + 1;
                        offset = offset + 0x1c;
                    } while (uVar1 < param_2);
                }

                break;

            case 7:
                uVar1 = 0;
                if (param_2 != 0)
                {
                    do
                    {
                        LibGpu.SetPolyG4(buffer, offset);
                        LibGpu.SetSemiTrans(buffer, offset, 1);
                        uVar1 = uVar1 + 1;
                        offset = offset + 0x24;
                    } while (uVar1 < param_2);
                }

                break;
        }
    }

    // GHIDRA: FUN_80061204 @ 0x80061204 (VS.EXE)
    // NO TITLE.EXE COUNTERPART — the title overlay's copy of this compilation unit ends after
    // InitializePrimitivePool, and no ported TITLE_EXE file contains this body. So the C# name is
    // NOT borrowed from a closed equivalent: it is read off the body itself, which does nothing but
    // stamp one POLY_FT4. The Ghidra symbol stays raw.
    //
    // One caller, FUN_800414ec @ 0x800414EC, at 0x80041668:
    //   FUN_80061204(puVar7, 1, 0xb, 0x7880, (iVar6 % 2) * 0x20, (iVar2 % 2) * 0x20, 0x1f, 0x1f)
    // walking a 24x24 grid of packets in .bss from &DAT_800B7484 with a 0x48-byte stride, so the
    // POLY_FT4 is embedded in a larger record there. param_7/param_8 are the tile extent: with 0x1f
    // the quad spans u0..u0+0x1e.
    //
    // Field offsets are the psyq POLY_FT4 layout and were taken from the store instructions rather
    // than from the decompiler's field names, which spell v0 and v1 as `_2` and `_3`:
    //   sh v0,0x4   -> r0,g0 = 0x80,0x80    sb v0,0x6  -> b0 = 0x80
    //   sh s4,0x16  -> tpage = param_3      sh s5,0xe  -> clut  = param_4
    //   sb .,0xc/0xd -> u0,v0               sb .,0x14/0x15 -> u1,v1
    //   sb .,0x1c/0x1d -> u2,v2             sb .,0x24/0x25 -> u3,v3
    // The four x/y vertices are NOT written here; the caller fills them itself right after.
    internal static void InitializePolyFt4(int param_1, int param_2, ushort param_3, ushort param_4,
        byte param_5, byte param_6, sbyte param_7, sbyte param_8)
    {
        // JUSTIFICATION: PSX hardware adaptation only
        // RELATION: param_1 is a raw PSX POLY_FT4 pointer. POLY_FT4Ref is the SDK's model of exactly
        // that — the psyq field names over the real byte packet — so the address is resolved once
        // and every store below lands on the same bytes the GPU reads. An address with no mapping
        // cannot be written on desktop; the original has no such case.
        var resolved = PsxRam.AddressResolver?.Invoke(param_1);
        if (resolved == null)
        {
            return;
        }

        (byte[] buffer, int offset) = resolved.Value;
        POLY_FT4Ref p = new POLY_FT4Ref(buffer, offset);

        LibGpu.SetPolyFT4(p);
        LibGpu.SetSemiTrans(p, param_2);
        p.r0 = 0x80;
        p.g0 = 0x80;
        p.tpage = param_3;
        p.clut = param_4;
        p.b0 = 0x80;
        byte uVar1 = (byte)(param_5 + param_7 + 0xff);
        byte uVar2 = (byte)(param_6 + param_8 + 0xff);
        p.u0 = param_5;
        p.v0 = param_6;
        p.u1 = uVar1;
        p.v1 = param_6;
        p.u2 = param_5;
        p.v2 = uVar2;
        p.u3 = uVar1;
        p.v3 = uVar2;
    }
}
