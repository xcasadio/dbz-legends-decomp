using PsxSdkMonogame;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.TITLE_EXE;

// The display / fade machine. ControlScreenFade @ 0x80038228 is a state machine over DAT_80083454,
// driving one full-screen POLY_GT4 that is composited over the frame, with UpdateScreenFade registered
// as the task that animates it.
//
// Callers select an operation through the first argument; the second carries the fade intensity.
// main reaches it as ControlScreenFade(8, 0), which is the initialisation case.
internal static class DisplayMachine
{
    // GHIDRA: g_FadeQuad @ 0x800B9518
    // The full-screen quad. Every field offset below was checked against the raw stores rather than
    // taken from the decompiler, which rendered v0 and v1 as the unnamed _2 and _3.
    //
    // Real memory rather than an object: FUN_80037388 @ 0x80037388 submits it with
    // AddPrim(g_ActiveDrawEnvAddress + 0x206c, &g_FadeQuad), and the bucket stores the packet's PSX
    // address. The field names below are unchanged; only the storage under them is.
    private const int Poly800b9518Address = unchecked((int)0x800B9518);

    internal static readonly POLY_GT4Ref g_FadeQuad =
        new(RamRegion(Poly800b9518Address, POLY_GT4Ref.Size), 0);

    // GHIDRA: _DAT_800834b4 @ 0x800834B4
    // Fade intensity handed to the animating task.
    internal static int _DAT_800834b4;

    // GHIDRA: RECT_80083550 @ 0x80083550
    // Scratch rect LoadImageInVram fills before every upload.
    private static readonly RECT RECT_80083550 = new();

    // GHIDRA: UpdateScreenFade @ 0x80038684
    // The task that animates the fade. The task block carries this PSX address, exactly as the
    // console holds it; the body is below.
    private const int UpdateScreenFade_Address = unchecked((int)0x80038684);

    // GHIDRA: ControlScreenFade @ 0x80038228
    internal static short ControlScreenFade(ushort param_1, ushort param_2)
    {
        if ((TITLE_EXE_exe.DAT_800a897a & 1) != 0)
        {
            return 1;
        }

        short sVar1 = 0;
        switch (param_1)
        {
            case 0:
                TITLE_EXE_exe.DAT_80083454 = 0x4003;
                g_FadeQuad.tpage = 0x50;
                _DAT_800834b4 = 0xff;
                UpdateScreenFade();
                SetDispMask(0);
                return 0;

            case 1:
                g_FadeQuad.tpage = 0x50;
                TITLE_EXE_exe.DAT_80083454 = 2;
                goto LAB_80038610;

            case 2:
                if (TITLE_EXE_exe.DAT_80083454 == 0
                    && CreateFadeTask() != 0)
                {
                    g_FadeQuad.tpage = 0x50;
                    TITLE_EXE_exe.DAT_80083454 = 2;
                    _DAT_800834b4 = param_2;
                    SetDispMask(1);
                    return 0;
                }

                break;

            case 3:
                if (TITLE_EXE_exe.DAT_80083454 == 1
                    && CreateFadeTask() != 0)
                {
                    TITLE_EXE_exe.DAT_80083454 = 3;
                    _DAT_800834b4 = param_2;
                    g_FadeQuad.tpage = 0x50;
                    return 0;
                }

                break;

            case 4:
                if (TITLE_EXE_exe.DAT_80083454 == 7)
                {
                    if (CreateFadeTask() != 0)
                    {
                        TITLE_EXE_exe.DAT_80083454 = 4;
                        _DAT_800834b4 = param_2;
                        g_FadeQuad.tpage = 0x30;
                        return 0;
                    }
                }
                else if (TITLE_EXE_exe.DAT_80083454 == 5)
                {
                    TITLE_EXE_exe.DAT_80083454 = 4;
                    _DAT_800834b4 = param_2;
                    return 0;
                }

                break;

            case 5:
                if (TITLE_EXE_exe.DAT_80083454 == 1)
                {
                    if (CreateFadeTask() != 0)
                    {
                        TITLE_EXE_exe.DAT_80083454 = 5;
                        _DAT_800834b4 = param_2;
                        g_FadeQuad.tpage = 0x30;
                        return 0;
                    }
                }
                else if (TITLE_EXE_exe.DAT_80083454 == 4)
                {
                    TITLE_EXE_exe.DAT_80083454 = 5;
                    _DAT_800834b4 = param_2;
                    return 0;
                }

                break;

            case 6:
                if (TITLE_EXE_exe.DAT_80083454 == 1
                    && CreateFadeTask() != 0)
                {
                    TITLE_EXE_exe.DAT_80083454 = 6;
                    _DAT_800834b4 = 0;
                    g_FadeQuad.tpage = 0x30;
                    return 0;
                }

                break;

            case 7:
                TITLE_EXE_exe.DAT_80083454 = 0x4005;
                g_FadeQuad.tpage = 0x30;
                LAB_80038610:
                _DAT_800834b4 = 0xff;
                UpdateScreenFade();
                SetDispMask(1);
                return 0;

            case 8:
                // Two consecutive halfwords on the stack, so one little-endian PSX word.
                ulong[] local_18 = { 0x1111FFFFUL };
                SetPolyGT4(g_FadeQuad);
                SetSemiTrans(g_FadeQuad, 1);
                SetShadeTex(g_FadeQuad, 0);
                g_FadeQuad.v0 = 0xff;
                g_FadeQuad.v1 = 0xff;
                g_FadeQuad.v2 = 0xff;
                g_FadeQuad.v3 = 0xff;
                g_FadeQuad.u1 = 1;
                g_FadeQuad.u3 = 1;
                g_FadeQuad.x0 = -2;
                g_FadeQuad.y0 = -2;
                g_FadeQuad.y1 = -2;
                g_FadeQuad.x2 = -2;
                g_FadeQuad.y2 = 0xf2;
                g_FadeQuad.y3 = 0xf2;
                g_FadeQuad.tpage = 0x10;
                g_FadeQuad.clut = 0x7f80;
                g_FadeQuad.b3 = 0x80;
                g_FadeQuad.g3 = 0x80;
                g_FadeQuad.r3 = 0x80;
                g_FadeQuad.b2 = 0x80;
                g_FadeQuad.g2 = 0x80;
                g_FadeQuad.r2 = 0x80;
                g_FadeQuad.b1 = 0x80;
                g_FadeQuad.g1 = 0x80;
                g_FadeQuad.r1 = 0x80;
                g_FadeQuad.b0 = 0x80;
                g_FadeQuad.g0 = 0x80;
                g_FadeQuad.r0 = 0x80;
                g_FadeQuad.u0 = 0;
                g_FadeQuad.u2 = 0;
                g_FadeQuad.x1 = 0x142;
                g_FadeQuad.x3 = 0x142;
                LoadImageInVram(local_18, 0, 0x1fe, 2, 1, '\0');
                SetDispMask(0);
                TITLE_EXE_exe.DAT_80083454 = 0;
                return 0;

            case 9:
                sVar1 = (short)TITLE_EXE_exe.DAT_80083454;
                goto switchD_8003827c_default;

            default:
                goto switchD_8003827c_default;
        }

        sVar1 = 1;
        switchD_8003827c_default:
        return sVar1;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original spells this inline as
    // `CreateTask(UpdateScreenFade, 0x56, 1, 0, 0, DAT_80079858)` at five call sites. DAT_80079858 is
    // g_TaskListHead + 4, that is the head of list 1, which matches the listIndex of 1.
    //
    // The RegisterCallback line is the same bridge TitleImages.SetupTitleScreen uses: the block stores
    // the raw PSX address, and this table is what turns that address back into the ported method
    // when ExecuteTaskList dispatches it. Assigning the same pair repeatedly is harmless, so the
    // five original call sites keep their shape.
    private static int CreateFadeTask()
    {
        TaskSystem.RegisterCallback(UpdateScreenFade_Address, UpdateScreenFade);
        return TaskSystem.CreateTask(UpdateScreenFade_Address, 0x56, 1, 0, 0,
            TaskSystem.g_TaskListHead[1]);
    }

    // GHIDRA: UpdateScreenFade @ 0x80038684
    // One frame of the fade. It is the task body CreateFadeTask registers in list 1, and cases 0
    // and 7 above also reach it as a plain call — the original does that with a direct `jal` at
    // 0x80038648 and 0x80038614, not through the block's function pointer, so this is a direct
    // call here too.
    //
    // It walks the twelve RGB bytes of g_FadeQuad toward 0x00 or toward 0xff, one step per
    // frame, and on the frame the ramp lands it rewrites the state word and deletes its own task.
    // Only r0 is ever read back: it is the machine's current level, and the other eleven bytes are
    // write-only mirrors of it.
    //
    // Ghidra prints s0 and s1 as `unaff_s0` and `bVar1` because the switch is an indirect `jr v0`
    // through the jump table at 0x800206DC and it could not see their definitions on every path.
    // Both are in fact assigned here: s1 is zeroed in the delay slot of the bounds branch at
    // 0x800386BC, and s0 on every path that later reads it.
    internal static void UpdateScreenFade()
    {
        if ((TITLE_EXE_exe.DAT_800a897a & 1) != 0)
        {
            return;
        }

        // s0. On the three case-6 arms that do not set it the original leaves it holding whatever
        // the caller had; those arms also leave bVar1 false, so the tail never reads it. C# wants a
        // definite assignment, and no original path observes the value chosen here.
        int unaff_s0 = 0;
        bool bVar1 = false;

        // The two masks select different things, and the transitions say which.
        //
        // 0xbfff is the complement of 0x4000 inside sixteen bits, so the switch dispatches on the
        // state word with bit 14 stripped: state 0x4003 enters case 3 and 0x4005 enters case 5.
        // The jump table covers exactly [2..6]; every other value falls through to the merge with
        // bVar1 still false and this function does nothing at all.
        //
        // 0x4000 is then tested on its own before each DeleteTask below, so bit 14 is a flag riding
        // on the state word rather than part of the state. Its only two writers are ControlScreenFade
        // cases 0 and 7, which are precisely the two that call this function directly instead of
        // handing it to CreateTask — so when it is set there is no task of ours on any list and the
        // delete has to be skipped. Nothing ever clears it explicitly; the terminal states written
        // below are the plain 0, 1 and 7, so it survives exactly one invocation.
        //
        // The cast is the hardware load width: the original reads the state word with `lhu`.
        switch ((ushort)TITLE_EXE_exe.DAT_80083454 & 0xbfff)
        {
            case 2:
                // Unsigned compare, and it reads the step as a halfword (`lhu` at 0x800386E8).
                if ((ushort)_DAT_800834b4 < g_FadeQuad.r0)
                {
                    goto LAB_800386fc;
                }

                unaff_s0 = 0;
                if (((ushort)TITLE_EXE_exe.DAT_80083454 & 0x4000) == 0)
                {
                    TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                        (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                }

                TITLE_EXE_exe.DAT_80083454 = 1;
                break;

            // 0x800386FC. Case 4 branches back here; the binary shares this one block.
            LAB_800386fc:
                // The arithmetic reads the same global as a byte (`lbu` at 0x800386FC) while the
                // comparison above read it as a halfword. Both widths are the original's, and a
                // step above 0xff would make the two disagree. TITLE.EXE only ever passes 4, 0x10
                // and 0x40, so the divergence is not exercised in this overlay.
                unaff_s0 = g_FadeQuad.r0 - (byte)_DAT_800834b4;
                break;

            case 3:
                if (0xfe < g_FadeQuad.r0 + (ushort)_DAT_800834b4)
                {
                    SetDispMask(0);
                    unaff_s0 = 0xff;
                    if (((ushort)TITLE_EXE_exe.DAT_80083454 & 0x4000) == 0)
                    {
                        TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                            (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                    }

                    TITLE_EXE_exe.DAT_80083454 = 0;
                    bVar1 = true;
                    goto switchD_800386d8_default;
                }

            // 0x80038814. It physically sits inside case 5's block; case 3 branches forward into it
            // and case 5 falls into it, so the increment is written once for both.
            LAB_80038814:
                unaff_s0 = g_FadeQuad.r0 + (byte)_DAT_800834b4;
                break;

            case 4:
                // Behaviourally identical to case 2 — the binary duplicates the block instead of
                // sharing it, and only the branch sense differs. Kept as two blocks.
                if ((ushort)_DAT_800834b4 < g_FadeQuad.r0)
                {
                    goto LAB_800386fc;
                }

                unaff_s0 = 0;
                if (((ushort)TITLE_EXE_exe.DAT_80083454 & 0x4000) == 0)
                {
                    TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                        (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                }

                TITLE_EXE_exe.DAT_80083454 = 1;
                break;

            case 5:
                // Same saturating add and same 0xff threshold as case 3. The differences are that
                // there is no SetDispMask here and that the terminal state is 7, not 0.
                if (g_FadeQuad.r0 + (ushort)_DAT_800834b4 < 0xff)
                {
                    goto LAB_80038814;
                }

                unaff_s0 = 0xff;
                if (((ushort)TITLE_EXE_exe.DAT_80083454 & 0x4000) == 0)
                {
                    TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                        (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                }

                TITLE_EXE_exe.DAT_80083454 = 7;
                break;

            case 6:
                // Here _DAT_800834b4 is a step counter, not a per-frame delta: ControlScreenFade case 6
                // forces it to 0 and this block drives it 0 -> 1 -> 2. Three frames, and the first
                // two write the colours themselves and leave bVar1 false so the tail is skipped.
                if ((ushort)_DAT_800834b4 == 1)
                {
                    g_FadeQuad.b3 = 0x08;
                    g_FadeQuad.g3 = 0x08;
                    g_FadeQuad.r3 = 0x08;
                    g_FadeQuad.b0 = 0x08;
                    g_FadeQuad.g0 = 0x08;
                    g_FadeQuad.r0 = 0x08;
                    g_FadeQuad.b2 = 0x08;
                    g_FadeQuad.g2 = 0x08;
                    g_FadeQuad.r2 = 0x08;
                    g_FadeQuad.b1 = 0x08;
                    g_FadeQuad.g1 = 0x08;
                    g_FadeQuad.r1 = 0x08;
                    _DAT_800834b4 = 2;
                    bVar1 = false;
                }
                else if ((ushort)_DAT_800834b4 < 2)
                {
                    if ((ushort)_DAT_800834b4 == 0)
                    {
                        g_FadeQuad.b3 = 0x20;
                        g_FadeQuad.g3 = 0x20;
                        g_FadeQuad.r3 = 0x20;
                        g_FadeQuad.b0 = 0x20;
                        g_FadeQuad.g0 = 0x20;
                        g_FadeQuad.r0 = 0x20;
                        g_FadeQuad.b2 = 0x20;
                        g_FadeQuad.g2 = 0x20;
                        g_FadeQuad.r2 = 0x20;
                        g_FadeQuad.b1 = 0x20;
                        g_FadeQuad.g1 = 0x20;
                        g_FadeQuad.r1 = 0x20;
                        _DAT_800834b4 = 1;
                        bVar1 = false;
                    }
                    else
                    {
                        // PARTIAL: the `< 2` test is a signed `slti` at 0x80038870 applied to a
                        // value the original loaded with `lhu`, so this arm needs a negative
                        // halfword and a zero-extending load cannot produce one. It is kept because
                        // it exists in the binary; a full audit of every writer of 0x800834B4
                        // outside this file would be needed to call it dead outright.
                        bVar1 = false;
                    }
                }
                else if ((ushort)_DAT_800834b4 == 2)
                {
                    unaff_s0 = 0;
                    TITLE_EXE_exe.DAT_80083454 = 1;
                    bVar1 = true;

                    // No 0x4000 guard on this one, unlike the four above. That asymmetry is in the
                    // machine code: there is no `andi 0x4000` anywhere in the block at 0x80038984.
                    // Consistent with the transitions, since state 6 is only ever entered through
                    // ControlScreenFade case 6, which always goes through CreateTask.
                    TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                        (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                }
                else
                {
                    bVar1 = false;
                }

                // Ghidra prints case 6 as falling into the default label. It does not: all four
                // arms are explicit jumps to 0x800389A8, past the default target at 0x800389A4.
                // Same destination in effect, since 0x800389A4 only recomputes the s1 test that
                // each arm already did in its own delay slot.
                goto switchD_800386d8_default;

            default:
                goto switchD_800386d8_default;
        }

        bVar1 = true;
        switchD_800386d8_default:
        if (bVar1)
        {
            // Store order taken from the twelve `sb` at 0x800389B0..0x80038A0C. Ghidra prints them
            // in the opposite order; all twelve take the same byte, so it is not observable either
            // way. The `sb` is what truncates the 32-bit register, hence the cast.
            g_FadeQuad.b3 = (byte)unaff_s0;
            g_FadeQuad.g3 = (byte)unaff_s0;
            g_FadeQuad.r3 = (byte)unaff_s0;
            g_FadeQuad.b2 = (byte)unaff_s0;
            g_FadeQuad.g2 = (byte)unaff_s0;
            g_FadeQuad.r2 = (byte)unaff_s0;
            g_FadeQuad.b1 = (byte)unaff_s0;
            g_FadeQuad.g1 = (byte)unaff_s0;
            g_FadeQuad.r1 = (byte)unaff_s0;
            g_FadeQuad.b0 = (byte)unaff_s0;
            g_FadeQuad.g0 = (byte)unaff_s0;
            g_FadeQuad.r0 = (byte)unaff_s0;
        }
    }

    // GHIDRA: LoadImageInVram @ 0x80057BB4
    // Uploads an image and returns the tpage when mode is 0, or the CLUT id otherwise.
    internal static uint LoadImageInVram(ulong[] imageBuffer, ushort x, ushort y, short w, short h,
        char mode)
    {
        RECT_80083550.h = h;
        RECT_80083550.x = (short)x;
        RECT_80083550.y = (short)y;
        RECT_80083550.w = w;
        LoadImage(RECT_80083550, imageBuffer);

        int result_y = (int)((uint)x << 0x10) >> 0x10;
        int result_final;
        if (mode == '\0')
        {
            if (result_y < 0)
            {
                result_y = result_y + 0x3f;
            }

            int result_x = (short)y;
            result_final = result_y >> 6;
            if (result_x < 0)
            {
                result_x = result_x + 0xff;
            }

            result_y = (result_x >> 8) << 4;
        }
        else
        {
            result_final = (int)((uint)x << 0x10) >> 0x14;
            if (result_y < 0)
            {
                result_final = (result_y + 0xf) >> 4;
            }

            result_y = (int)((uint)y << 0x10) >> 10;
        }

        return (uint)(result_final + result_y) & 0xffff;
    }
}
