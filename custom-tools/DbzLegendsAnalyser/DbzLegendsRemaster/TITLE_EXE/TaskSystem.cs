using System;
using System.Collections.Generic;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.TITLE_EXE;

// The callback-object system TITLE.EXE is built on. Twenty-one doubly-linked lists of blocks
// allocated on the heap; see docs/tasks/TITLE_EXE_TASK_SYSTEM.md for the evidence behind every
// offset and every branch below.
//
// The blocks stay real memory, reached through PsxRam at PSX addresses, and the 0x10/0x14 chaining
// is the original's. Nothing here is a .NET collection standing in for a linked list.
internal static class TaskSystem
{
    // Offsets inside a task block. Closed from CreateTask/DeleteTask/ExecuteTaskList.
    private const int TaskId = 0x00;
    private const int TaskFlags = 0x02;
    private const int TaskCallback = 0x04;
    private const int TaskContext = 0x08;
    private const int TaskCounter = 0x0C;
    private const int TaskPrev = 0x10;
    private const int TaskNext = 0x14;
    private const int TaskHeaderSize = 0x18;

    // GHIDRA: g_TaskListHead @ 0x80079854
    internal static readonly int[] g_TaskListHead = new int[21];

    // GHIDRA: g_TaskListTail @ 0x800798A8
    internal static readonly int[] g_TaskListTail = new int[21];

    // GHIDRA: g_TaskListCount @ 0x800798FC
    internal static readonly short[] g_TaskListCount = new short[21];

    // GHIDRA: PTR_80083224 @ 0x80083224
    // The object ExecuteTaskList is currently standing on. Ghidra types it TitleAudioBlock *, which
    // is wrong and inherited from an earlier analysis; it is a task block.
    private static int PTR_80083224;

    // GHIDRA: PTR_ARRAY_80083228 @ 0x80083228
    // Only its first halfword is used, and only to carry the list index being walked.
    private static short PTR_ARRAY_80083228;

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original stores a raw function pointer at +0x04 and calls it indirectly through
    // `(**(code **)(block + 4))()`. C# cannot put a managed delegate inside a byte[], so the block
    // keeps the ORIGINAL PSX address — 0x80037388 and friends, exactly what the console holds — and
    // this table turns that address back into the ported method at call time. A task block dumped
    // from this port therefore still compares byte for byte against one dumped from PCSX-Redux.
    private static readonly Dictionary<int, Action> s_callbackDispatch = new();

    // JUSTIFICATION: C# language bridge only
    // RELATION: registers one ported method under the PSX address its original occupies. Called
    // once per callback as the overlay's functions get transliterated.
    internal static void RegisterCallback(int psxAddress, Action ported)
    {
        s_callbackDispatch[psxAddress] = ported;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the indirect call itself. An address with no ported method yet is skipped rather
    // than crashing, so a partially transliterated overlay still runs its known tasks.
    private static void InvokeCallback(int psxAddress)
    {
        if (s_callbackDispatch.TryGetValue(psxAddress, out Action ported))
        {
            ported();
        }
    }

    // GHIDRA: CreateTask @ 0x80049504
    internal static int CreateTask(int callback, int id, int listIndex, int contextSize, int param_5,
        int insertPoint)
    {
        int iVar1 = -(contextSize & 3) + 4;
        int iVar5 = iVar1;
        if (iVar1 < 0)
        {
            iVar5 = -(contextSize & 3) + 7;
        }

        int puVar2 = LibApi.malloc((contextSize & 0xffff) + iVar1 + ((iVar5 >> 2) * -4) + 0x18);
        if (puVar2 == -1)
        {
            // BLOCKED: the original spins here forever on allocation failure. Kept as a fault so a
            // desktop run reports the exhausted heap instead of hanging with no diagnosis.
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

                iVar5 = listIndex & 0xffff;
            }

            g_TaskListTail[iVar5] = puVar7;
            g_TaskListCount[listIndex & 0xffff] = (short)(g_TaskListCount[listIndex & 0xffff] + 1);
            PsxRam.WriteU16(puVar2 + TaskFlags, 0);
            puVar7 = puVar2;
        }

        return puVar7;
    }

    // GHIDRA: DeleteTask @ 0x80049720
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
                if (task == PTR_80083224)
                {
                    PTR_80083224 = PsxRam.ReadI32(task + TaskPrev);
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

                LibApi.free(task);
                uVar1 = 1;
                g_TaskListCount[listIndex & 0xffff] =
                    (short)(g_TaskListCount[listIndex & 0xffff] - 1);
            }
        }

        return uVar1;
    }

    // GHIDRA: ExecuteTaskList @ 0x800497FC
    internal static int ExecuteTaskList(ushort listIndex)
    {
        if (g_TaskListCount[listIndex] == 0)
        {
            return 0;
        }

        PTR_80083224 = g_TaskListHead[listIndex];
        PTR_ARRAY_80083228 = (short)listIndex;
        if (PTR_80083224 == 0)
        {
            return 0;
        }

        do
        {
            int pTVar3 = PTR_80083224;
            int iVar4 = PsxRam.ReadI32(PTR_80083224 + TaskCounter);
            if (iVar4 < 1)
            {
                if (iVar4 + 1 == 0)
                {
                    if (PTR_80083224 != 0 && (PsxRam.ReadU16(PTR_80083224 + TaskFlags) & 2) == 0)
                    {
                        int iVar7 = PsxRam.ReadI32(PTR_80083224 + TaskPrev);
                        iVar4 = PsxRam.ReadI32(PTR_80083224 + TaskNext);
                        if (iVar7 == 0)
                        {
                            PTR_80083224 = PsxRam.ReadI32(PTR_80083224 + TaskPrev);
                            g_TaskListHead[listIndex] = iVar4;
                        }
                        else
                        {
                            PTR_80083224 = PsxRam.ReadI32(PTR_80083224 + TaskPrev);
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

                        LibApi.free(pTVar3);
                        g_TaskListCount[listIndex] = (short)(g_TaskListCount[listIndex] - 1);
                    }
                }
                else
                {
                    ushort uVar1 = PsxRam.ReadU16(PTR_80083224 + TaskFlags);
                    int pbVar2 = PTR_80083224;
                    if ((uVar1 & 3) == 0)
                    {
                        if (iVar4 < 0)
                        {
                            PsxRam.WriteI32(PTR_80083224 + TaskCounter, iVar4 + 1);
                        }

                        InvokeCallback(PsxRam.ReadI32(pbVar2 + TaskCallback));
                    }
                    else if ((uVar1 & 3) == 1)
                    {
                        PsxRam.WriteU16(PTR_80083224 + TaskFlags, (ushort)(uVar1 & 0xfffd));
                        if (pTVar3 != 0)
                        {
                            PTR_80083224 = PsxRam.ReadI32(pTVar3 + TaskPrev);
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

                            LibApi.free(pTVar3);
                            g_TaskListCount[listIndex] = (short)(g_TaskListCount[listIndex] - 1);
                        }
                    }
                    else
                    {
                        PsxRam.WriteU16(PTR_80083224 + TaskFlags, (ushort)((uVar1 & 0xfffd) | 1));
                    }
                }
            }
            else
            {
                PsxRam.WriteI32(PTR_80083224 + TaskCounter, iVar4 - 1);
            }

            if (PTR_80083224 == 0)
            {
                PTR_80083224 = g_TaskListHead[listIndex];
            }
            else
            {
                PTR_80083224 = PsxRam.ReadI32(PTR_80083224 + TaskNext);
            }
        } while (PTR_80083224 != 0);

        return 0;
    }
}
