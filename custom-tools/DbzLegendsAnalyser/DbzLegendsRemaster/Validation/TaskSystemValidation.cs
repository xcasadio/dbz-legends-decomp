using System;
using System.Collections.Generic;
using DbzLegendsRemaster.TITLE_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: offline bench for the transliterated CreateTask / DeleteTask / ExecuteTaskList. Those
// three walk a doubly-linked list by raw offsets, and a mistake in the chaining would not crash —
// it would silently drop or repeat callbacks, which is exactly the kind of defect that is
// impossible to diagnose once the title screen is running on top of it. Everything checked here is
// stated in docs/tasks/TITLE_EXE_TASK_SYSTEM.md.
internal static class TaskSystemValidation
{
    private const int HeapBase = 0x00010000;
    private const int HeapSize = 0x10000;

    private const int ListIndex = 6;

    // Arbitrary stand-in PSX addresses; only their identity matters to the dispatch table.
    private const int CallbackA = unchecked((int)0x80037388);
    private const int CallbackB = unchecked((int)0x80021E28);
    private const int CallbackC = unchecked((int)0x80056D84);

    private static int s_failures;
    private static readonly List<string> s_calls = new();

    internal static int Run()
    {
        s_failures = 0;

        Func<int, (byte[] buffer, int offset)?> previous = PsxRam.AddressResolver;
        PsxRam.AddressResolver = PsxHeap.Resolve;
        try
        {
            TaskSystem.RegisterCallback(CallbackA, () => s_calls.Add("A"));
            TaskSystem.RegisterCallback(CallbackB, () => s_calls.Add("B"));
            TaskSystem.RegisterCallback(CallbackC, () => s_calls.Add("C"));

            CheckExecutionOrderAndChaining();
            CheckCounterGatesTheCallback();
            CheckMinusOneDestroys();
            CheckDeleteTask();
            CheckContextIsZeroedAndAddressable();
        }
        finally
        {
            PsxRam.AddressResolver = previous;
        }

        Console.WriteLine(s_failures == 0
            ? "TASKS: toutes les verifications passent"
            : $"TASKS: {s_failures} echec(s)");
        return s_failures == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            s_failures++;
            Console.WriteLine($"  ECHEC: {label}");
        }
    }

    private static void Reset()
    {
        LibApi.InitHeap(HeapBase, HeapSize);
        for (int i = 0; i < 21; i++)
        {
            TaskSystem.g_TaskListHead[i] = 0;
            TaskSystem.g_TaskListTail[i] = 0;
            TaskSystem.g_TaskListCount[i] = 0;
        }

        s_calls.Clear();
    }

    private static int Create(int callback, int contextSize, int counter)
    {
        return TaskSystem.CreateTask(callback, 0, ListIndex, contextSize, counter,
            TaskSystem.g_TaskListTail[ListIndex]);
    }

    private static void CheckExecutionOrderAndChaining()
    {
        Reset();
        int a = Create(CallbackA, 0, 0);
        int b = Create(CallbackB, 0, 0);
        int c = Create(CallbackC, 0, 0);

        Check(a != 0 && b != 0 && c != 0, "trois taches creees");
        Check(TaskSystem.g_TaskListCount[ListIndex] == 3, "le compteur de liste vaut 3");
        Check(TaskSystem.g_TaskListHead[ListIndex] == a, "la tete est la premiere creee");
        Check(TaskSystem.g_TaskListTail[ListIndex] == c, "la queue est la derniere creee");

        // Le parcours part de la tete et suit +0x14.
        Check(PsxRam.ReadI32(a + 0x14) == b, "a.next == b");
        Check(PsxRam.ReadI32(b + 0x14) == c, "b.next == c");
        Check(PsxRam.ReadI32(c + 0x14) == 0, "c.next == 0");
        Check(PsxRam.ReadI32(a + 0x10) == 0, "a.prev == 0");
        Check(PsxRam.ReadI32(b + 0x10) == a, "b.prev == a");
        Check(PsxRam.ReadI32(c + 0x10) == b, "c.prev == b");

        TaskSystem.ExecuteTaskList(ListIndex);
        Check(string.Join("", s_calls) == "ABC", $"ordre d'execution ABC, obtenu {string.Join("", s_calls)}");
    }

    private static void CheckCounterGatesTheCallback()
    {
        Reset();
        Create(CallbackA, 0, 1);
        TaskSystem.ExecuteTaskList(ListIndex);
        Check(s_calls.Count == 0, "un compteur a 1 saute le callback");

        TaskSystem.ExecuteTaskList(ListIndex);
        Check(string.Join("", s_calls) == "A", "le tour suivant appelle le callback, compteur retombe a 0");
    }

    private static void CheckMinusOneDestroys()
    {
        Reset();
        Create(CallbackA, 0, -1);
        int survivor = Create(CallbackB, 0, 0);

        TaskSystem.ExecuteTaskList(ListIndex);
        Check(TaskSystem.g_TaskListCount[ListIndex] == 1, "un compteur a -1 detruit la tache");
        Check(TaskSystem.g_TaskListHead[ListIndex] == survivor, "la tete devient la tache survivante");
        Check(TaskSystem.g_TaskListTail[ListIndex] == survivor, "la queue devient la tache survivante");
        Check(PsxRam.ReadI32(survivor + 0x10) == 0, "le survivant n'a plus de precedent");
    }

    private static void CheckDeleteTask()
    {
        Reset();
        int a = Create(CallbackA, 0, 0);
        int b = Create(CallbackB, 0, 0);
        int c = Create(CallbackC, 0, 0);

        Check(TaskSystem.DeleteTask(b, ListIndex) == 1, "DeleteTask renvoie 1");
        Check(TaskSystem.g_TaskListCount[ListIndex] == 2, "le compteur retombe a 2");
        Check(PsxRam.ReadI32(a + 0x14) == c, "a.next saute la tache retiree");
        Check(PsxRam.ReadI32(c + 0x10) == a, "c.prev saute la tache retiree");

        s_calls.Clear();
        TaskSystem.ExecuteTaskList(ListIndex);
        Check(string.Join("", s_calls) == "AC", $"execution AC apres retrait, obtenu {string.Join("", s_calls)}");

        Check(TaskSystem.DeleteTask(0, ListIndex) == 0, "DeleteTask(0) renvoie 0");

        // Le drapeau 0x2 protege la tache de la destruction.
        int guarded = Create(CallbackA, 0, 0);
        PsxRam.WriteU16(guarded + 0x02, 2);
        Check(TaskSystem.DeleteTask(guarded, ListIndex) == 2, "le drapeau 0x2 refuse la destruction");
    }

    private static void CheckContextIsZeroedAndAddressable()
    {
        Reset();
        int withContext = Create(CallbackA, 0x70, 0);
        Check(withContext != 0, "tache avec contexte creee");

        int context = PsxRam.ReadI32(withContext + 0x08);
        Check(context == withContext + 0x18, "le pointeur de contexte vise le bloc + 0x18");

        bool allZero = true;
        for (int offset = 0; offset < 0x70; offset += 4)
        {
            if (PsxRam.ReadI32(context + offset) != 0)
            {
                allZero = false;
            }
        }

        Check(allZero, "le contexte est entierement mis a zero");

        PsxRam.WriteI32(context + 0x10, unchecked((int)0xDEADBEEF));
        Check(PsxRam.ReadI32(context + 0x10) == unchecked((int)0xDEADBEEF), "le contexte est adressable");

        Reset();
        int noContext = Create(CallbackA, 0, 0);
        Check(PsxRam.ReadI32(noContext + 0x08) == 0, "une taille de contexte nulle laisse le pointeur a 0");
    }
}
