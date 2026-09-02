using System;
using System.Collections.Generic;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's task scheduler. Twenty-one doubly-linked lists of 0x18-byte nodes carved out of the
// GAME allocator (FUN_80062f94 / FUN_800631c8), not out of a PSYQ heap: VS.EXE links no malloc.
//
// This is the same C source TITLE.EXE was built from, recompiled and relinked at other addresses.
// Every offset, every branch and every guard below matches TITLE_EXE/TaskSystem.cs word for word;
// only the six global addresses and the allocator entry points differ. The two files are kept
// separate on purpose — TITLE.EXE and VS.EXE are two independently linked programs and each
// function carries its own `GHIDRA:` address, so merging them into one shared C# scheduler would
// be exactly the merge rule 3 forbids. Nothing here calls into DbzLegendsRemaster.TITLE_EXE.
//
// NAMING: Ghidra still carries raw FUN_/DAT_ names on VS.EXE. The `GHIDRA:` lines therefore state
// the raw symbol, which is what the project database actually holds; the C# names come from the
// TITLE.EXE equivalents, whose semantics the identical bodies close.
//
// The nodes stay real memory, reached through PsxRam at PSX addresses, and the 0x10/0x14 chaining
// is the original's. Nothing here is a .NET collection standing in for a linked list.
internal static class TaskSystem
{
    // Offsets inside a task node. Closed from FUN_80053330 / FUN_8005354c / FUN_80053628.
    //
    // NOTE ON +0x10 / +0x14: the dispatch brief for this slice listed "+0x10 suivant, +0x14
    // precedent". The code says the reverse and is followed here. FUN_8005354c reads +0x10 into
    // iVar3 and, when iVar3 == 0, writes the node's +0x14 successor into the list HEAD — only the
    // predecessor link can be null at the head. +0x10 is prev, +0x14 is next, as in TITLE_EXE.
    private const int TaskId = 0x00;
    private const int TaskFlags = 0x02;
    private const int TaskCallback = 0x04;
    private const int TaskContext = 0x08;
    private const int TaskCounter = 0x0C;
    private const int TaskPrev = 0x10;
    private const int TaskNext = 0x14;
    private const int TaskHeaderSize = 0x18;

    // GHIDRA: DAT_80083b3c @ 0x80083B3C (VS.EXE)
    // The g_TaskListHead of TITLE.EXE. Twenty-one int slots: the array runs to 0x80083B90 where the
    // tail array starts, i.e. 0x54 = 21 * 4 bytes. Indexed as an int array by FUN_80053628's
    // `piVar6 = &DAT_80083b3c + uVar5`.
    internal static readonly int[] g_TaskListHead = new int[21];

    // GHIDRA: DAT_80083b90 @ 0x80083B90 (VS.EXE)
    // The g_TaskListTail of TITLE.EXE. Twenty-one int slots, 0x80083B90..0x80083BE3, again 0x54
    // bytes. The original indexes it as a byte base plus `index * 4`; modelled here as an int array
    // indexed by the list index, which is the same store.
    internal static readonly int[] g_TaskListTail = new int[21];

    // GHIDRA: DAT_80083be4 @ 0x80083BE4 (VS.EXE)
    // The g_TaskListCount of TITLE.EXE. Twenty-one SHORT slots, 0x80083BE4..0x80083C0D — 0x2A bytes
    // — and the byte at 0x80083C0E is outside it. Indexed as `index * 2`.
    internal static readonly short[] g_TaskListCount = new short[21];

    // GHIDRA: DAT_8008d16c @ 0x8008D16C (VS.EXE)
    // The g_CurrentTask of TITLE.EXE: the node FUN_80053628 is currently standing on. Written by
    // FUN_80053628 and FUN_8005354c, read from 96 sites across the overlay.
    internal static int g_CurrentTask;

    // GHIDRA: DAT_8008d170 @ 0x8008D170 (VS.EXE)
    // The g_CurrentTaskListIndex of TITLE.EXE. A halfword (Ghidra: undefined2), written only by
    // FUN_80053628 at 0x80053678 and read from fourteen sites. List indices run 0..20, so the
    // signedness of the readers never bites; typed short here as in TITLE_EXE.
    internal static short g_CurrentTaskListIndex;

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original stores a raw function pointer at +0x04 and calls it indirectly through
    // `(*(code *)*puVar1)()` at 0x800537A0. C# cannot put a managed delegate inside a byte[], so the
    // node keeps the ORIGINAL PSX address — exactly what the console holds — and this table turns
    // that address back into the ported method at call time. A task node dumped from this port
    // therefore still compares byte for byte against one dumped from PCSX-Redux. Same solution, and
    // the same shape, as TITLE_EXE/TaskSystem.cs.
    private static readonly Dictionary<int, Action> s_callbackDispatch = new();

    // JUSTIFICATION: C# language bridge only
    // RELATION: registers one ported method under the PSX address its original occupies. Called
    // once per callback as VS.EXE's functions get transliterated.
    internal static void RegisterCallback(int psxAddress, Action ported)
    {
        s_callbackDispatch[psxAddress] = ported;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the indirect call itself. An address with no ported method yet is skipped rather
    // than crashing, so a partially transliterated overlay still runs its known tasks.
    internal static void InvokeCallbackByAddress(int psxAddress) => InvokeCallback(psxAddress);

    private static void InvokeCallback(int psxAddress)
    {
        if (s_callbackDispatch.TryGetValue(psxAddress, out Action ported))
        {
            ported();
        }
    }

    // GHIDRA: FUN_80053330 @ 0x80053330 (VS.EXE)
    // This is the CreateTask of TITLE.EXE (@ 0x80049504) word for word; the C# name comes from
    // there, the Ghidra symbol is still raw. 540 bytes, 49 call sites.
    //
    // The one substantive difference with TITLE.EXE: the node comes from the GAME allocator
    // FUN_80062f94, not from LibApi.malloc. VS.EXE links no PSYQ heap.
    //
    // param_5 lands in +0x0C, the field FUN_80053628 decrements as a delay counter; it is NOT the
    // flag word (+0x02), which this function clears to 0 on the way out.
    internal static int CreateTask(int callback, int id, int listIndex, int contextSize, int param_5,
        int insertPoint)
    {
        int iVar1 = -(contextSize & 3) + 4;
        int iVar5 = iVar1;
        if (iVar1 < 0)
        {
            iVar5 = -(contextSize & 3) + 7;
        }

        int puVar2 = Heap.FUN_80062f94((contextSize & 0xffff) + iVar1 + ((iVar5 >> 2) * -4) + 0x18);
        if (puVar2 == -1)
        {
            // BLOCKED: the original spins here forever — `do { } while (true)` at 0x8005335C — on
            // allocation failure. Kept as a fault, exactly as TITLE_EXE/TaskSystem.cs does, so a
            // desktop run reports the exhausted heap instead of hanging with no diagnosis. This is
            // the one place in this file where the original's behaviour is not reproduced.
            throw new InvalidOperationException("CreateTask: heap exhausted");
        }

        int puVar7 = 0;
        if (1 < (uint)(puVar2 + 1))
        {
            PsxRam.WriteU16(puVar2 + TaskId, (ushort)id);
            if ((contextSize & 0xffff) == 0)
            {
                PsxRam.WriteI32(puVar2 + TaskContext, 0);
            }
            else
            {
                int puVar6 = puVar2 + TaskHeaderSize;
                PsxRam.WriteI32(puVar2 + TaskContext, puVar6);
                uint uVar8 = (uint)(contextSize & 0xffff) >> 2;
                uint uVar3b = 0;
                if (uVar8 != 0)
                {
                    do
                    {
                        PsxRam.WriteI32(puVar6, 0);
                        uVar3b = uVar3b + 1;
                        puVar6 = puVar6 + 4;
                    } while ((uVar3b & 0xffff) < uVar8);
                }
            }

            int uVar3 = listIndex & 0xffff;
            PsxRam.WriteI32(puVar2 + TaskCounter, param_5);
            PsxRam.WriteI32(puVar2 + TaskCallback, callback);
            PsxRam.WriteI32(puVar2 + TaskNext, 0);
            puVar7 = puVar2;
            if (g_TaskListCount[uVar3] == 0)
            {
                iVar5 = uVar3;
                g_TaskListHead[iVar5] = puVar2;
                PsxRam.WriteI32(puVar2 + TaskPrev, 0);
            }
            else
            {
                int piVar4 = g_TaskListTail[uVar3];
                if (insertPoint == piVar4)
                {
                    PsxRam.WriteI32(puVar2 + TaskNext, 0);
                    PsxRam.WriteI32(piVar4 + TaskNext, puVar2);
                    PsxRam.WriteI32(puVar2 + TaskPrev, piVar4);
                }
                else
                {
                    PsxRam.WriteI32(puVar2 + TaskNext, insertPoint);
                    PsxRam.WriteI32(puVar2 + TaskPrev, PsxRam.ReadI32(insertPoint + TaskPrev));
                    PsxRam.WriteI32(insertPoint + TaskPrev, puVar2);
                    if (PsxRam.ReadI32(puVar2 + TaskPrev) == 0)
                    {
                        g_TaskListHead[uVar3] = puVar2;
                    }
                    else
                    {
                        PsxRam.WriteI32(PsxRam.ReadI32(puVar2 + TaskPrev) + TaskNext, puVar2);
                    }

                    puVar7 = g_TaskListTail[listIndex & 0xffff];
                }

                // The original computes `(param_3 & 0xffff) << 2`, a byte offset into the tail
                // array. Modelled as the element index, which the store below then uses.
                iVar5 = listIndex & 0xffff;
            }

            g_TaskListTail[iVar5] = puVar7;
            g_TaskListCount[listIndex & 0xffff] = (short)(g_TaskListCount[listIndex & 0xffff] + 1);
            PsxRam.WriteU16(puVar2 + TaskFlags, 0);
            puVar7 = puVar2;
        }

        return puVar7;
    }

    // GHIDRA: FUN_8005354c @ 0x8005354C (VS.EXE)
    // This is the DeleteTask of TITLE.EXE (@ 0x80049720) word for word; the C# name comes from
    // there, the Ghidra symbol is still raw. 220 bytes, 24 call sites.
    //
    // Returns 0 for a null node, 2 for a node whose flag bit 0x2 protects it, 1 when the node was
    // actually unlinked and released.
    //
    // The release call reads `FUN_800631c8()` with no argument in Ghidra's output, because the
    // callee has no applied prototype. The disassembly closes it: a0 is loaded with param_1 on
    // entry at 0x80053558 and is never written before the `jal 0x800631c8` at 0x800535E8 — every
    // intervening use is `lw <reg>,off(a0)`. The argument is the node.
    internal static int DeleteTask(int task, uint listIndex)
    {
        int uVar1;
        if (task == 0)
        {
            uVar1 = 0;
        }
        else
        {
            uVar1 = 2;
            if ((PsxRam.ReadU16(task + TaskFlags) & 2) == 0)
            {
                if (task == g_CurrentTask)
                {
                    g_CurrentTask = PsxRam.ReadI32(task + TaskPrev);
                }

                int iVar3 = PsxRam.ReadI32(task + TaskPrev);
                int iVar2 = PsxRam.ReadI32(task + TaskNext);
                if (iVar3 == 0)
                {
                    g_TaskListHead[listIndex & 0xffff] = iVar2;
                }
                else
                {
                    PsxRam.WriteI32(iVar3 + TaskNext, iVar2);
                }

                if (iVar2 == 0)
                {
                    g_TaskListTail[listIndex & 0xffff] = iVar3;
                }
                else
                {
                    PsxRam.WriteI32(iVar2 + TaskPrev, iVar3);
                }

                Heap.FUN_800631c8(task);
                uVar1 = 1;
                g_TaskListCount[listIndex & 0xffff] =
                    (short)(g_TaskListCount[listIndex & 0xffff] - 1);
            }
        }

        return uVar1;
    }

    // GHIDRA: FUN_80053628 @ 0x80053628 (VS.EXE)
    // This is the ExecuteTaskList of TITLE.EXE (@ 0x800497FC) word for word; the C# name comes from
    // there, the Ghidra symbol is still raw. 484 bytes, 21 call sites, all of them in main
    // @ 0x80062134 (0x80062524, then 0x80062544..0x800625DC in steps of 8) — one call per list.
    //
    // One walk of one list. Per node, reading +0x0C:
    //   > 0   the counter is decremented and the callback is skipped this frame;
    //   == -1 the node deletes itself — unlink, release, decrement the count;
    //   else  the flag word at +0x02 decides:
    //           bits 0/1 clear -> a negative counter is stepped up by one, then the callback at
    //                             +0x04 is called indirectly (0x800537A0);
    //           bits == 1      -> bit 1 is cleared and the node is unlinked and released;
    //           otherwise      -> bit 1 is cleared and bit 0 set, and the node is left in place.
    //
    // The walk resumes from g_CurrentTask, which the branches above deliberately move back to the
    // removed node's predecessor so that the `+0x14` step at the bottom lands on the right
    // successor. A null g_CurrentTask restarts the walk from the head — that is the original's own
    // recovery path for a node deleted at the head, and it is reproduced as-is.
    internal static int ExecuteTaskList(ushort listIndex)
    {
        if (g_TaskListCount[listIndex] == 0)
        {
            return 0;
        }

        g_CurrentTask = g_TaskListHead[listIndex];
        g_CurrentTaskListIndex = (short)listIndex;
        if (g_CurrentTask == 0)
        {
            return 0;
        }

        do
        {
            int pTVar3 = g_CurrentTask;
            int iVar4 = PsxRam.ReadI32(g_CurrentTask + TaskCounter);
            if (iVar4 < 1)
            {
                if (iVar4 + 1 == 0)
                {
                    if (g_CurrentTask != 0 && (PsxRam.ReadU16(g_CurrentTask + TaskFlags) & 2) == 0)
                    {
                        int iVar7 = PsxRam.ReadI32(g_CurrentTask + TaskPrev);
                        iVar4 = PsxRam.ReadI32(g_CurrentTask + TaskNext);
                        if (iVar7 == 0)
                        {
                            g_CurrentTask = PsxRam.ReadI32(g_CurrentTask + TaskPrev);
                            g_TaskListHead[listIndex] = iVar4;
                        }
                        else
                        {
                            g_CurrentTask = PsxRam.ReadI32(g_CurrentTask + TaskPrev);
                            PsxRam.WriteI32(iVar7 + TaskNext, iVar4);
                        }

                        if (iVar4 == 0)
                        {
                            g_TaskListTail[listIndex] = iVar7;
                        }
                        else
                        {
                            PsxRam.WriteI32(iVar4 + TaskPrev, iVar7);
                        }

                        Heap.FUN_800631c8(pTVar3);
                        g_TaskListCount[listIndex] = (short)(g_TaskListCount[listIndex] - 1);
                    }
                }
                else
                {
                    ushort uVar1 = PsxRam.ReadU16(g_CurrentTask + TaskFlags);
                    int pbVar2 = g_CurrentTask;
                    if ((uVar1 & 3) == 0)
                    {
                        if (iVar4 < 0)
                        {
                            PsxRam.WriteI32(g_CurrentTask + TaskCounter, iVar4 + 1);
                        }

                        InvokeCallback(PsxRam.ReadI32(pbVar2 + TaskCallback));
                    }
                    else if ((uVar1 & 3) == 1)
                    {
                        PsxRam.WriteU16(g_CurrentTask + TaskFlags, (ushort)(uVar1 & 0xfffd));
                        if (pTVar3 != 0)
                        {
                            g_CurrentTask = PsxRam.ReadI32(pTVar3 + TaskPrev);
                            int iVar7 = PsxRam.ReadI32(pTVar3 + TaskPrev);
                            iVar4 = PsxRam.ReadI32(pTVar3 + TaskNext);
                            if (iVar7 == 0)
                            {
                                g_TaskListHead[listIndex] = iVar4;
                            }
                            else
                            {
                                PsxRam.WriteI32(iVar7 + TaskNext, iVar4);
                            }

                            if (iVar4 != 0)
                            {
                                PsxRam.WriteI32(iVar4 + TaskPrev, iVar7);
                            }
                            else
                            {
                                g_TaskListTail[listIndex] = iVar7;
                            }

                            Heap.FUN_800631c8(pTVar3);
                            g_TaskListCount[listIndex] = (short)(g_TaskListCount[listIndex] - 1);
                        }
                    }
                    else
                    {
                        PsxRam.WriteU16(g_CurrentTask + TaskFlags, (ushort)((uVar1 & 0xfffd) | 1));
                    }
                }
            }
            else
            {
                PsxRam.WriteI32(g_CurrentTask + TaskCounter, iVar4 - 1);
            }

            if (g_CurrentTask == 0)
            {
                g_CurrentTask = g_TaskListHead[listIndex];
            }
            else
            {
                g_CurrentTask = PsxRam.ReadI32(g_CurrentTask + TaskNext);
            }
        } while (g_CurrentTask != 0);

        return 0;
    }
}
