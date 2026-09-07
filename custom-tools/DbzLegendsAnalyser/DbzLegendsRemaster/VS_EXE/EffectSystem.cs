using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE EFFECTS / ATTACK-ZONE FAMILY of VS.EXE, nine functions that until now existed in this port
// only as empty stubs scattered across four other files. Every one of them is transliterated IN
// FULL below from its own Ghidra decompilation of /VS.EXE; none is a placeholder.
//
// WHAT IS IN HERE, and why these nine belong together:
//
//   0x80045130  FUN_80045130   1764  the attack-zone registration AnimCmd_HitzSet fires twice
//   0x80042054  FUN_80042054   1116  the screen-fade CONTROLLER (five callers, two read $v0)
//   0x800424B0  FUN_800424b0    932  the screen-fade FRAME, the task 0x80042054 creates
//   0x80042F74  FUN_80042f74    784  a GTE-placed effect anchor (zero callers in the image)
//   0x8003F994  FUN_8003f994    756  the per-frame transform interpolator opcode 12 drives
//   0x80045B70  FUN_80045b70    388  the facing/orientation table lookup
//   0x800461FC  FUN_800461fc    228  a GTE rotate/translate helper
//   0x800437EC  FUN_800437ec    212  creates the task at LAB_800436D0 and seeds its workspace
//   0x8003FE98  FUN_8003fe98    192  creates the task at LAB_8003FC88 and seeds its workspace
//
// THE DUPLICATE-SYMBOL RULE, applied before a single line was written. Every one of the nine
// addresses was grepped across the whole of VS_EXE/ first, and eight of the nine were ALREADY
// DECLARED somewhere as an empty stub:
//
//   AnimCmdEffects.cs   FUN_8003f994, FUN_8003fe98, FUN_80045130   (private)
//   BattleScene.cs      FUN_80042054, FUN_800424b0 (UpdateScreenFade)   (internal)
//   FighterCombat.cs    FUN_80045b70, FUN_800461fc
//   FighterCombatArms.cs FUN_800437ec   (private)
//
// This slice may not edit those files, so those eight stubs are STILL THERE while this file is
// read. That is the exact defect this repository has shipped four times: C# binds an unqualified
// call to the enclosing class first, so until the main session DELETES each stub and QUALIFIES its
// call sites onto EffectSystem, the real bodies below are dead code for those callers. The list of
// file+line to delete is in this task's report, not hidden here.
//
// THE ONE ADDRESS NOT REDECLARED HERE is FUN_80045af0 @ 0x80045AF0 — FighterCombat.cs carries its
// real 128-byte body, and FUN_80042f74 below reaches it by qualified name.
//
// LOAD WIDTH AND SIGN. Every PSX-memory access below goes through PsxRam, which offers only
// ReadU8 / ReadU16 / ReadI32; a signed halfword is written `(short)PsxRam.ReadU16(addr)` and a
// signed byte `(sbyte)PsxRam.ReadU8(addr)`, at every single site, so the width and the sign of the
// original's own `lh` / `lhu` / `lb` / `lbu` stay visible in the C#.
internal static class EffectSystem
{
    // =====================================================================================
    // Globals this family owns. Everything already declared elsewhere in VS_EXE/ is reached by
    // qualified name instead, per the duplicate-storage rule: one PSX address, one C# storage.
    //
    // ALREADY DECLARED ELSEWHERE, reached qualified below rather than redeclared here:
    //   AnimCmdEffects.DAT_80099058 / _5c / _60 / _62 / _64 / _66   (opcode 12's own latches)
    //   Scratchpad.SVECTOR_1f80007c / DAT_1f800084 / _86 / DAT_1f8000c4 / _c8 / _cc
    //   FileIo.DAT_1f8000d0
    //   BattleScene.DAT_8008d3f4      (the fade step, 0x8008D3F4)
    //   VS_EXE_exe.DAT_8008d398       (the fade state word, 0x8008D398)
    //   AnimVm.DAT_800b305a           (the VM-suspend gate)
    //   TaskSystem.g_CurrentTask / g_CurrentTaskListIndex / g_TaskListHead / g_TaskListTail
    // =====================================================================================

    // GHIDRA: DAT_80099068 @ 0x80099068 (VS.EXE)
    // The X accumulator of the interpolator FUN_8003f994 runs. A WORD: the original loads and
    // stores it with `lw` / `sw` and shifts it right by 4 to make the 4.12 scratchpad coordinate,
    // so it holds sixteen times the position it publishes.
    internal static int DAT_80099068;

    // GHIDRA: DAT_8009906c @ 0x8009906C (VS.EXE)
    // The Y accumulator. Same width, same 4-bit scaling.
    internal static int DAT_8009906c;

    // GHIDRA: DAT_80099070 @ 0x80099070 (VS.EXE)
    // The Z accumulator. Same width, same 4-bit scaling.
    internal static int DAT_80099070;

    // GHIDRA: DAT_80099074 @ 0x80099074 (VS.EXE)
    // The per-frame X velocity, a HALFWORD: it is the difference between the resolved target and
    // the current position, computed once on the arming frame and then added every frame.
    internal static short DAT_80099074;

    // GHIDRA: DAT_80099076 @ 0x80099076 (VS.EXE)
    // The per-frame Y velocity. NOTE THE SIGN in FUN_8003f994's arming arm: unlike X and Z it is
    // computed as `-(current) - target`, not `target - current`. Reproduced exactly, not
    // "corrected".
    internal static short DAT_80099076;

    // GHIDRA: DAT_80099078 @ 0x80099078 (VS.EXE)
    // The per-frame Z velocity.
    internal static short DAT_80099078;

    // GHIDRA: DAT_8009907a @ 0x8009907A (VS.EXE)
    // The frame budget. Armed to 0x10 and counted down once per frame; the frame it reaches zero
    // is the frame FUN_8003f994 returns 1 instead of 0.
    internal static short DAT_8009907a;

    // GHIDRA: DAT_8009907c @ 0x8009907C (VS.EXE)
    // The yaw accumulator, a WORD holding the angle shifted left by 4 (the arming arm builds it as
    // `(angle << 16) >> 12`, i.e. sign-extend the halfword then scale by 16).
    internal static int DAT_8009907c;

    // GHIDRA: DAT_80099080 @ 0x80099080 (VS.EXE)
    // The per-frame yaw STEP, and the one field whose stored width the decompilation makes
    // visible: it is written as a full word (the `& 0xfff` and the `^ 0xf000` both operate on more
    // than sixteen bits) and read back through an explicit `(short)` truncation. Declared `int` for
    // that reason; narrowing it to `short` would change the `0x800 < DAT_80099080` test.
    internal static int DAT_80099080;

    // GHIDRA: DAT_80099084 @ 0x80099084 (VS.EXE)
    // A second position accumulator (the scratchpad slot at 0x1F8000D0), same 4-bit scaling.
    internal static int DAT_80099084;

    // GHIDRA: DAT_80099088 @ 0x80099088 (VS.EXE)
    // The pitch accumulator, built like DAT_8009907c and published to 0x1F800084.
    internal static int DAT_80099088;

    // GHIDRA: DAT_8009908c @ 0x8009908C (VS.EXE)
    // The per-frame delta for DAT_80099084. A halfword.
    internal static short DAT_8009908c;

    // GHIDRA: DAT_8009908e @ 0x8009908E (VS.EXE)
    // The per-frame delta for DAT_80099088. A halfword.
    internal static short DAT_8009908e;

    // GHIDRA: DAT_800c3bfc @ 0x800C3BFC (VS.EXE)
    // The full-screen fade quad's own TAG address — the packet FUN_80042054 case 8 initialises and
    // FUN_800424b0 ramps. VS_EXE_exe.cs already carries this same constant privately, for its own
    // `AddPrim(DAT_8008d420 + 0x206c, &DAT_800c3bfc)` call; a constant is not storage, so repeating
    // the address here is not the duplicate-storage defect. NO RamRegion is registered over it, and
    // that is deliberate: 0x800C3BFC lies inside VS.EXE's own image extent (load 0x80020000, file
    // 0xE6000 bytes less the 0x800 header = 0x80020000..0x80105800), so VS_EXE_exe.ResolveAddress
    // already answers for it through PsxExeImage. A RamRegion here would be RESOLVED FIRST and
    // would therefore shadow that with a second, empty copy of the same twelve bytes.
    //
    // THE PACKET IS A POLY_GT4 and every DAT_800c3cXX name below is one of its fields, read off the
    // layout rather than guessed — case 8 writes all of them and the offsets line up exactly:
    //   +0x00 tag   +0x04 r0 g0 b0 code   +0x08 x0 y0   +0x0C u0 v0 clut
    //   +0x10 r1 g1 b1 p1   +0x14 x1 y1   +0x18 u1 v1 tpage
    //   +0x1C r2 g2 b2 p2   +0x20 x2 y2   +0x24 u2 v2 pad
    //   +0x28 r3 g3 b3 p3   +0x2C x3 y3   +0x30 u3 v3 pad
    // so DAT_800c3c00/01/02 are r0/g0/b0, 0c/0d/0e r1/g1/b1, 18/19/1a r2/g2/b2, 24/25/26 r3/g3/b3 —
    // the twelve ramp bytes — 0x800c3c0a is the clut, 0x800c3c16 the tpage, and 04/06/10/12/1c/1e/
    // 28/2a the four x/y pairs. The names stay Ghidra's, per the naming rules; the offsets are
    // spelled as `Dat800c3bfcAddress + 0x..` so the layout is auditable at every store.
    private const int Dat800c3bfcAddress = unchecked((int)0x800C3BFC);

    // GHIDRA: LAB_800436d0 @ 0x800436D0 (VS.EXE)
    // The task entry FUN_800437ec creates. Ghidra never promoted it to a function because the only
    // reference is the address FUN_80053330 stores in the task node.
    //
    // NOT REGISTERED HERE, and this is a real gap, reported rather than papered over: the body at
    // 0x800436D0 is not ported anywhere in VS_EXE/, so there is nothing for
    // TaskSystem.RegisterCallback to map this address onto. The task IS created and IS linked into
    // list 0xB exactly as the original creates it; when ExecuteTaskList reaches it,
    // TaskSystem.InvokeCallback finds no entry and the node does nothing. Registering a wrong body
    // would be worse than registering none.
    private const int Lab800436d0Address = unchecked((int)0x800436D0);

    // GHIDRA: LAB_8003fc88 @ 0x8003FC88 (VS.EXE)
    // The task entry FUN_8003fe98 creates. Same situation and same reporting as LAB_800436D0: the
    // body is not ported, so no RegisterCallback line can be written honestly here.
    private const int Lab8003fc88Address = unchecked((int)0x8003FC88);

    // GHIDRA: PTR_DAT_800217f0 @ 0x800217F0 (VS.EXE)
    // The vtable-like block both task seeders hand to FUN_80053970. AnimCmdEffects.cs declares the
    // same address as its own private constant for its own call; a constant is not storage.
    private const int PTR_DAT_800217f0Address = unchecked((int)0x800217F0);

    // GHIDRA: DAT_80082e44 @ 0x80082E44 (VS.EXE)
    // FUN_80045b70's first table: sixteen bytes in .data, read straight out of the image through
    // PsxRam (VS_EXE_exe.ResolveAddress chains PsxExeImage last, exactly for tables like this one).
    // Measured with read-memory: 00 01 01 02 02 03 03 04 04 05 05 06 06 07 07 00 — so its result is
    // always 0..7, which is what bounds the second table to eight rows.
    private const int Dat80082e44Address = unchecked((int)0x80082E44);

    // GHIDRA: DAT_80082e54 @ 0x80082E54 (VS.EXE)
    // FUN_80045b70's second table, and the reason that function's return statement looks like a
    // pointer bug. Ghidra prints the base as the constant -0x7ff7d1ac; as an unsigned 32-bit value
    // that is 0x80082E54, i.e. the sixteen bytes immediately after DAT_80082e44. Eight rows of
    // sixteen bytes (0x80082E54..0x80082ED3), indexed row-major by
    // `DAT_80082e44[uVar1] * 0x10 + local_12`. Verified against the image, first row
    // 00 45 45 4A 4A 49 49 04 04 09 09 0A 0A 05 05 00.
    private const int Dat80082e54Address = unchecked((int)0x80082E54);

    // GHIDRA: DAT_80082c8a @ 0x80082C8A (VS.EXE)
    // FUN_80042f74's offset table: six SIGNED bytes per character (`(short)(char)` in the
    // decompilation is a sign-extending `lb`), indexed `charId * 6 + {0, 2, 4}` for the three
    // camera-distance bands. Read out of the image the same way.
    private const int Dat80082c8aAddress = unchecked((int)0x80082C8A);

    // JUSTIFICATION: C# language bridge only
    // RELATION: stands for FUN_80042054's own two stack halfwords, local_18 and local_16, whose
    // ADDRESS is handed to LoadImage_ReturnTPageOrClutId as the upload source. A C# local has no
    // address, so the pair is given one, the same bridge FighterCombatArms.cs and AnimCmdEffects.cs
    // already use for their own by-address locals.
    //
    // THE ADDRESS IS REAL STACK MEMORY and it aliases nothing already modelled. crt0 starts SP at
    // 0x807FFFF8 and the stack runs down 0x8000 bytes, and the blocks this port has already carved
    // out of it run upward from 0x807FFD70 with no gaps: BattleCamera 0x807FFD70..0x807FFE80,
    // FighterCombatArms 0x807FFE80..0x807FFEC0, BattleManager 0x807FFEC0..0x807FFFC0,
    // AnimCmdControl 0x807FFFC0, BattleScene 0x807FFFD0, AnimCmdEffects 0x807FFFE0,
    // AnimVmInterpreter 0x807FFFF0. This block sits BELOW all of them, at 0x807FFD60, so it can
    // never overlap one. Layout, rebased to 0: +0x00 local_18, +0x02 local_16.
    private const int Fun80042054FrameAddress = unchecked((int)0x807FFD60);

    private static readonly byte[] Fun80042054Frame =
        LibGpu.RamRegion(Fun80042054FrameAddress, 0x10);

    // =====================================================================================
    // FUN_80045130 — the attack-zone registration
    // =====================================================================================

    // GHIDRA: FUN_80045130 @ 0x80045130 (VS.EXE)
    // 1764 bytes, 0x80045130..0x80045813. AnimCmd_HitzSet is the caller this port sees, twice per
    // invocation, once per 0x3C-byte list record.
    //
    // WHAT IT DOES, closed from the control flow alone: it walks the singly linked chain whose head
    // is at `param_1 + 4`, and for each node whose two coarse cell coordinates (+0x14 and +0x16)
    // fall inside a box derived from the query position, it walks that node's own geometry table and
    // measures the distance from the query point to each vertex. On the FIRST vertex within
    // `param_4` it raises the node's priority byte at +0x18 to `param_2` — but only if `param_2` is
    // strictly greater than what is already there — and reports 1. The return is "at least one node
    // was raised".
    //
    // THE BOX IS BUILT WITH A ROUNDING IDIOM, four times over, and it is transliterated literally
    // rather than folded into a division: `x >> 9` with the numerator pre-biased by +1 / +0x200 on
    // the negative side and -1 / +0x1FE on the positive side. That is the compiler's own
    // round-toward-something expansion of a divide by 0x200, and rewriting it as `/ 0x200` would be
    // an optimisation, which this port does not do. Note that the two arms are NOT symmetric (the
    // negative arm subtracts one from the shifted result); whatever that does at the boundary is
    // the original's behaviour and is reproduced.
    //
    // THE PRIORITY COMPARISON IS BYTE-WIDE BUT THE STORE IS HALFWORD-WIDE. `local_38 = (byte)param_2`
    // then `if (*(byte *)(node + 6) < local_38) *(undefined2 *)(node + 6) = param_2;` — the test
    // reads ONE byte at +0x18 and the store writes TWO bytes at +0x18, so a param_2 above 0xFF also
    // clobbers +0x19 while comparing only its low byte. Reproduced exactly. AnimCmd_HitzSet's own
    // chain walk immediately afterwards reads BOTH +0x18 and +0x19, so this is observable.
    //
    // PARAMETER TYPES ARE GHIDRA'S, not the stub's. param_2 is an `undefined2` and param_4 a
    // `ushort`, and the difference matters: the distance test is `(int)(short)sqrt <= (int)(uint)
    // param_4`, an UNSIGNED widening of param_4. The stub this replaces took four plain `int`s, and
    // AnimCmd_HitzSet's two call sites pass `(short)uVar6` for param_4 — with a signed short that
    // widening produces a huge positive bound and the test always passes. Those call sites must
    // drop the `(short)` cast when they are qualified onto this body; it is in the report.
    internal static ushort FUN_80045130(int param_1, ushort param_2, int param_3, ushort param_4)
    {
        short sVar1;
        int iVar4;
        int iVar5;
        int iVar6;
        byte local_38;
        short local_34;
        short local_32;
        short local_30;
        short local_2e;
        ushort local_2c;
        ushort local_2a;
        int local_24;
        short local_20;
        short local_1e;

        // `*param_3` — param_3 is a `short *`, so this is a signed halfword at +0x00, and the
        // subtraction is done in short width before the rounding.
        sVar1 = (short)((short)PsxRam.ReadU16(param_3) - param_4);
        if (sVar1 < 0)
        {
            iVar4 = sVar1 + 1;
            if (iVar4 < 0)
            {
                iVar4 = sVar1 + 0x200;
            }

            local_34 = (short)((short)(iVar4 >> 9) + -1);
        }
        else
        {
            iVar4 = sVar1 + -1;
            if (iVar4 < 0)
            {
                iVar4 = sVar1 + 0x1fe;
            }

            local_34 = (short)(iVar4 >> 9);
        }

        sVar1 = (short)((short)PsxRam.ReadU16(param_3) + param_4);
        if (sVar1 < 0)
        {
            iVar4 = sVar1 + 1;
            if (iVar4 < 0)
            {
                iVar4 = sVar1 + 0x200;
            }

            local_30 = (short)((short)(iVar4 >> 9) + -1);
        }
        else
        {
            iVar4 = sVar1 + -1;
            if (iVar4 < 0)
            {
                iVar4 = sVar1 + 0x1fe;
            }

            local_30 = (short)(iVar4 >> 9);
        }

        // `param_3[2]` — the THIRD halfword of the query vector, at +0x04. The box is built on
        // components 0 and 2 only; component 1 (+0x02) is used in the distance test but not here.
        sVar1 = (short)((short)PsxRam.ReadU16(param_3 + 4) - param_4);
        if (sVar1 < 0)
        {
            iVar4 = sVar1 + 1;
            if (iVar4 < 0)
            {
                iVar4 = sVar1 + 0x200;
            }

            local_32 = (short)((short)(iVar4 >> 9) + -1);
        }
        else
        {
            iVar4 = sVar1 + -1;
            if (iVar4 < 0)
            {
                iVar4 = sVar1 + 0x1fe;
            }

            local_32 = (short)(iVar4 >> 9);
        }

        sVar1 = (short)((short)PsxRam.ReadU16(param_3 + 4) + param_4);
        if (sVar1 < 0)
        {
            iVar4 = sVar1 + 1;
            if (iVar4 < 0)
            {
                iVar4 = sVar1 + 0x200;
            }

            local_2e = (short)((short)(iVar4 >> 9) + -1);
        }
        else
        {
            iVar4 = sVar1 + -1;
            if (iVar4 < 0)
            {
                iVar4 = sVar1 + 0x1fe;
            }

            local_2e = (short)(iVar4 >> 9);
        }

        local_2c = 0;
        local_24 = PsxRam.ReadI32(param_1 + 4);
        do
        {
            if (local_24 == 0)
            {
                return local_2c;
            }

            // The node's own two coarse coordinates: +0x14 (`local_24 + 5` on an `undefined4 *`)
            // and +0x16. Both signed halfwords.
            if (local_34 <= (short)PsxRam.ReadU16(local_24 + 0x14)
                && (short)PsxRam.ReadU16(local_24 + 0x14) <= local_30
                && local_32 <= (short)PsxRam.ReadU16(local_24 + 0x16)
                && (short)PsxRam.ReadU16(local_24 + 0x16) <= local_2e)
            {
                // `local_24[4]` is +0x10, a POINTER to the node's geometry table, and the first
                // halfword of that table is the outer loop count. The original loads the same
                // pointer into three separate registers (iVar4/iVar5/iVar6) and uses each with a
                // different fixed bias (+0, +8, +0x10) — three parallel halfword arrays, not one
                // interleaved one. Kept as three variables so that stays visible.
                local_20 = (short)PsxRam.ReadU16(PsxRam.ReadI32(local_24 + 0x10));
                iVar4 = PsxRam.ReadI32(local_24 + 0x10);
                iVar5 = PsxRam.ReadI32(local_24 + 0x10);
                iVar6 = PsxRam.ReadI32(local_24 + 0x10);
                local_2a = 1;
                for (; local_20 != 0; local_20 = (short)(local_20 + -1))
                {
                    for (local_1e = (short)PsxRam.ReadU16((int)((uint)local_2a * 2)
                             + PsxRam.ReadI32(local_24 + 0x10));
                         local_1e != 0;
                         local_1e = (short)(local_1e + -1))
                    {
                        ushort uVar2 = (ushort)(local_1e + local_2a + 3);

                        // The three components of (vertex + node origin - query point). The node
                        // origin is at +0x1C, +0x1E and +0x20; the query point at +0x00, +0x02 and
                        // +0x04. Every load is a signed halfword.
                        int dx = ((int)(short)PsxRam.ReadU16((int)((uint)uVar2 * 2) + iVar4)
                                  + (short)PsxRam.ReadU16(local_24 + 0x1c))
                                 - (short)PsxRam.ReadU16(param_3);
                        int dy = ((int)(short)PsxRam.ReadU16((int)((uint)uVar2 * 2) + iVar5 + 8)
                                  + (short)PsxRam.ReadU16(local_24 + 0x1e))
                                 - (short)PsxRam.ReadU16(param_3 + 2);
                        int dz = ((int)(short)PsxRam.ReadU16((int)((uint)uVar2 * 2) + iVar6 + 0x10)
                                  + (short)PsxRam.ReadU16(local_24 + 0x20))
                                 - (short)PsxRam.ReadU16(param_3 + 4);

                        // SquareRoot0 is libgte, at or above 0x800632C4 — called, not ported.
                        int lVar3 = LibGte.SquareRoot0(dx * dx + dy * dy + dz * dz);

                        // `(int)(short)lVar3` TRUNCATES the square root to sixteen signed bits
                        // before comparing. That is the original's own cast and it is a latent bug
                        // for a squared distance above 0x7FFF*0x7FFF, where the truncation can turn
                        // a far vertex into a near or negative one. Reproduced, not fixed.
                        if ((int)(short)lVar3 <= (int)(uint)param_4)
                        {
                            local_38 = (byte)param_2;
                            if (PsxRam.ReadU8(local_24 + 0x18) < local_38)
                            {
                                // Halfword store over a byte-wide test — see the header note.
                                PsxRam.WriteU16(local_24 + 0x18, param_2);
                                local_2c = 1;
                            }

                            goto LAB_800457c4;
                        }
                    }

                    local_2a = (ushort)(local_2a + 0x1c);
                }
            }

            LAB_800457c4:
            local_24 = PsxRam.ReadI32(local_24);
        }
        while (true);
    }

    // =====================================================================================
    // The screen fade: FUN_80042054 (the controller) and FUN_800424b0 (the frame)
    // =====================================================================================

    // GHIDRA: FUN_80042054 @ 0x80042054 (VS.EXE)
    // 1116 bytes, five callers in the image (eleven call sites), of which TWO read $v0 and compare
    // it against 7 — RenderBattleScene3D's camera arm and phase 4's sub-step 3 — so the return
    // value is live and the `void` stubs this port used to carry were losing information.
    //
    // THIS IS TITLE.EXE's ControlScreenFade @ 0x80038228 RECOMPILED, and the port below was written
    // against VS.EXE's own decompilation with TITLE_EXE/DisplayMachine.cs used as a cross-check
    // rather than as the source. The twinning is already recorded in docs/tasks/VS_EXE_TRANCHE4.md
    // and in BattleScene.cs's own note on this address; every TITLE address maps to a VS one:
    //   DAT_800a897a & 1  -> AnimVm.DAT_800b305a & 1     (the VM-suspend gate)
    //   DAT_80083454      -> VS_EXE_exe.DAT_8008d398     (the state word)
    //   _DAT_800834b4     -> BattleScene.DAT_8008d3f4    (the fade step)
    //   g_FadeQuad        -> the packet at 0x800C3BFC    (see Dat800c3bfcAddress above)
    // The two decompilations differ in no arm.
    //
    // THE NAME STAYS RAW. TITLE's C# name is ControlScreenFade, but this project's rule is to keep
    // the Ghidra symbol when it is raw in the program being ported, and it is raw in /VS.EXE. The
    // twin is documented instead of adopted, the same posture BattleScene.cs took.
    //
    // ARGUMENT AND RETURN TYPES. Ghidra's own recovered signature is
    // `short FUN_80042054(undefined2 param_1, undefined2 param_2)`. The C# signature keeps `int`
    // for both parameters and `int` for the return, matching the BattleScene.cs stub this replaces
    // so that the nine existing call sites need only be qualified, not rewritten; param_1 is only
    // ever switched on and param_2 only ever stored as a halfword, so the widening is not
    // observable. The store applies its own `(ushort)` narrowing below, where the original's `sh`
    // is.
    internal static int FUN_80042054(int param_1, int param_2)
    {
        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            return 1;
        }

        short sVar1 = 0;
        switch (param_1)
        {
            case 0:
                // 0x4003: state 3 with bit 14 set. Bit 14 is the "there is no task of ours on any
                // list" flag — cases 0 and 7 are precisely the two arms that call FUN_800424b0
                // DIRECTLY instead of handing it to CreateTask, so the frame function must skip its
                // own DeleteTask. Nothing ever clears the bit; the terminal states written by the
                // frame function are the plain 0, 1 and 7, so it survives exactly one invocation.
                VS_EXE_exe.DAT_8008d398 = 0x4003;
                PsxRam.WriteU16(Dat800c3bfcAddress + 0x1a, 0x50); // DAT_800c3c16, the tpage
                BattleScene.DAT_8008d3f4 = 0xff;
                FUN_800424b0();
                LibGpu.SetDispMask(0);
                return 0;

            case 1:
                PsxRam.WriteU16(Dat800c3bfcAddress + 0x1a, 0x50); // DAT_800c3c16
                VS_EXE_exe.DAT_8008d398 = 2;
                goto LAB_8004243c;

            case 2:
                // The `&&` short-circuit is the original's own: CreateTask is NOT called unless the
                // state word is already 0. Kept as one nested test rather than flattened.
                if (VS_EXE_exe.DAT_8008d398 == 0)
                {
                    int iVar2 = CreateFadeTask();
                    if (iVar2 != 0)
                    {
                        PsxRam.WriteU16(Dat800c3bfcAddress + 0x1a, 0x50); // DAT_800c3c16
                        VS_EXE_exe.DAT_8008d398 = 2;
                        BattleScene.DAT_8008d3f4 = (ushort)param_2;
                        LibGpu.SetDispMask(1);
                        return 0;
                    }
                }

                break;

            case 3:
                if (VS_EXE_exe.DAT_8008d398 == 1)
                {
                    int iVar2 = CreateFadeTask();
                    if (iVar2 != 0)
                    {
                        VS_EXE_exe.DAT_8008d398 = 3;
                        BattleScene.DAT_8008d3f4 = (ushort)param_2;
                        PsxRam.WriteU16(Dat800c3bfcAddress + 0x1a, 0x50); // DAT_800c3c16
                        return 0;
                    }
                }

                break;

            case 4:
                // Two entry states. From 7 it has to create the task; from 5 the task is already
                // running and only the state and the step change. The asymmetry is the original's.
                if (VS_EXE_exe.DAT_8008d398 == 7)
                {
                    int iVar2 = CreateFadeTask();
                    if (iVar2 != 0)
                    {
                        VS_EXE_exe.DAT_8008d398 = 4;
                        BattleScene.DAT_8008d3f4 = (ushort)param_2;
                        PsxRam.WriteU16(Dat800c3bfcAddress + 0x1a, 0x30); // DAT_800c3c16
                        return 0;
                    }
                }
                else if (VS_EXE_exe.DAT_8008d398 == 5)
                {
                    VS_EXE_exe.DAT_8008d398 = 4;
                    BattleScene.DAT_8008d3f4 = (ushort)param_2;
                    return 0;
                }

                break;

            case 5:
                if (VS_EXE_exe.DAT_8008d398 == 1)
                {
                    int iVar2 = CreateFadeTask();
                    if (iVar2 != 0)
                    {
                        VS_EXE_exe.DAT_8008d398 = 5;
                        BattleScene.DAT_8008d3f4 = (ushort)param_2;
                        PsxRam.WriteU16(Dat800c3bfcAddress + 0x1a, 0x30); // DAT_800c3c16
                        return 0;
                    }
                }
                else if (VS_EXE_exe.DAT_8008d398 == 4)
                {
                    VS_EXE_exe.DAT_8008d398 = 5;
                    BattleScene.DAT_8008d3f4 = (ushort)param_2;
                    return 0;
                }

                break;

            case 6:
                // Case 6 IGNORES param_2 and forces the step to 0: in state 6 the step is a frame
                // COUNTER (0 -> 1 -> 2) rather than a per-frame delta, as FUN_800424b0's own case 6
                // shows. The original's own asymmetry, reproduced.
                if (VS_EXE_exe.DAT_8008d398 == 1)
                {
                    int iVar2 = CreateFadeTask();
                    if (iVar2 != 0)
                    {
                        VS_EXE_exe.DAT_8008d398 = 6;
                        BattleScene.DAT_8008d3f4 = 0;
                        PsxRam.WriteU16(Dat800c3bfcAddress + 0x1a, 0x30); // DAT_800c3c16
                        return 0;
                    }
                }

                break;

            case 7:
                VS_EXE_exe.DAT_8008d398 = 0x4005;
                PsxRam.WriteU16(Dat800c3bfcAddress + 0x1a, 0x30); // DAT_800c3c16
                LAB_8004243c:
                BattleScene.DAT_8008d3f4 = 0xff;
                FUN_800424b0();
                LibGpu.SetDispMask(1);
                return 0;

            case 8:
                {
                    // THE INITIALISATION CASE, the one main reaches as FUN_80042054(8, 0). It
                    // stamps the fade quad's FULL geometry — not just the twelve ramp bytes — and
                    // uploads a two-halfword CLUT.
                    //
                    // local_18 / local_16: the two halfwords whose ADDRESS goes to
                    // LoadImage_ReturnTPageOrClutId. 0xFFFF and 0x1111 — a 2x1 16-bit image at
                    // VRAM (0, 0x1FE), which the clut field 0x7F80 below points at
                    // (0x7F80 = (0x1FE << 6) | (0 >> 4)).
                    PsxRam.WriteU16(Fun80042054FrameAddress + 0, 0xffff);
                    PsxRam.WriteU16(Fun80042054FrameAddress + 2, 0x1111);

                    // JUSTIFICATION: C# language bridge only
                    // RELATION: SetPolyGT4 / SetSemiTrans / SetShadeTex take the packet's byte
                    // buffer plus an offset, and the original passes `&DAT_800c3bfc`, a raw PSX
                    // address. The installed resolver is what turns one into the other; FileIo.cs
                    // already resolves a raw address this same way for its own upload path. No new
                    // storage is created — the buffer that comes back is whichever one already
                    // stands for 0x800C3BFC.
                    var packet = PsxRam.AddressResolver?.Invoke(Dat800c3bfcAddress);
                    if (packet != null)
                    {
                        LibGpu.SetPolyGT4(packet.Value.buffer, packet.Value.offset);
                        LibGpu.SetSemiTrans(packet.Value.buffer, packet.Value.offset, 1);
                        LibGpu.SetShadeTex(packet.Value.buffer, packet.Value.offset, 0);
                    }

                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x0d, 0xff);  // DAT_800c3c09, v0
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x19, 0xff);  // DAT_800c3c15, v1
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x25, 0xff);  // DAT_800c3c21, v2
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x31, 0xff);  // DAT_800c3c2d, v3
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x18, 1);     // DAT_800c3c14, u1
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x30, 1);     // DAT_800c3c2c, u3
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x08, 0xfffe); // DAT_800c3c04, x0 = -2
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x0a, 0xfffe); // DAT_800c3c06, y0 = -2
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x16, 0xfffe); // DAT_800c3c12, y1 = -2
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x20, 0xfffe); // DAT_800c3c1c, x2 = -2
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x22, 0x00f2); // DAT_800c3c1e, y2 = 242
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x2e, 0x00f2); // DAT_800c3c2a, y3 = 242
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x1a, 0x10);   // DAT_800c3c16, tpage
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x0e, 0x7f80); // DAT_800c3c0a, clut
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x2a, 0x80);  // DAT_800c3c26, b3
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x29, 0x80);  // DAT_800c3c25, g3
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x28, 0x80);  // DAT_800c3c24, r3
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x1e, 0x80);  // DAT_800c3c1a, b2
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x1d, 0x80);  // DAT_800c3c19, g2
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x1c, 0x80);  // DAT_800c3c18, r2
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x12, 0x80);  // DAT_800c3c0e, b1
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x11, 0x80);  // DAT_800c3c0d, g1
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x10, 0x80);  // DAT_800c3c0c, r1
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x06, 0x80);  // DAT_800c3c02, b0
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x05, 0x80);  // DAT_800c3c01, g0
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x04, 0x80);  // DAT_800c3c00, r0
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x0c, 0);     // DAT_800c3c08, u0
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x24, 0);     // DAT_800c3c20, u2
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x14, 0x142); // DAT_800c3c10, x1 = 322
                    PsxRam.WriteU16(Dat800c3bfcAddress + 0x2c, 0x142); // DAT_800c3c28, x3 = 322

                    // LoadImage_ReturnTPageOrClutId @ 0x80061B0C is a GAME function, not SDK
                    // (0x80061B0C < 0x800632C4), and VS_EXE/FileIo.cs already ports it.
                    FileIo.LoadImage_ReturnTPageOrClutId(Fun80042054FrameAddress, 0, 0x1fe, 2, 1, 0);
                    LibGpu.SetDispMask(0);
                    VS_EXE_exe.DAT_8008d398 = 0;
                    return 0;
                }

            case 9:
                // The only READ arm: hands the caller the current state word. Two call sites in the
                // image compare its result against 7.
                sVar1 = (short)VS_EXE_exe.DAT_8008d398;
                goto switchD_800420a8_default;

            default:
                goto switchD_800420a8_default;
        }

        // Reached only by the four `break`s above — the arms whose precondition failed. The
        // original falls out of the switch onto this store, so "the state was wrong" and "the task
        // could not be created" both report 1.
        sVar1 = 1;

        switchD_800420a8_default:
        return sVar1;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original spells this inline as
    // `FUN_80053330(FUN_800424b0, 0x56, 1, 0, 0, DAT_80083b40)` at five call sites inside
    // FUN_80042054. DAT_80083b40 is TaskSystem.g_TaskListHead + 4, i.e. the head of list 1, which
    // matches the listIndex of 1 — checked against the image the way FighterSetup.cs and
    // BattleManager.cs check their own insert points: the head array starts at 0x80083B3C, so
    // (0x80083B40 - 0x80083B3C) / 4 = 1.
    //
    // The RegisterCallback line is the same bridge TITLE_EXE/DisplayMachine.CreateFadeTask uses:
    // the task node stores the raw PSX address and TaskSystem's dispatch table is what turns that
    // address back into the ported method. Re-registering the same pair is harmless, so the five
    // original call sites keep their shape rather than being hoisted.
    private static int CreateFadeTask()
    {
        TaskSystem.RegisterCallback(Fun800424b0Address, FUN_800424b0);
        return TaskSystem.CreateTask(Fun800424b0Address, 0x56, 1, 0, 0,
            TaskSystem.g_TaskListHead[1]);
    }

    // GHIDRA: FUN_800424b0 @ 0x800424B0 (VS.EXE)
    // The task entry's own PSX address, as the node holds it.
    private const int Fun800424b0Address = unchecked((int)0x800424B0);

    // GHIDRA: FUN_800424b0 @ 0x800424B0 (VS.EXE)
    // 932 bytes, 0x800424B0..0x80042853. ONE FRAME of the fade, and TITLE.EXE's UpdateScreenFade
    // @ 0x80038684 recompiled — see FUN_80042054's own header for the address correspondence and
    // for where the twinning is recorded. It is both a task body (created by CreateFadeTask above)
    // and a direct call from FUN_80042054's cases 0 and 7, exactly as the original does it with a
    // `jal` rather than through the node's function pointer.
    //
    // It walks the twelve RGB bytes of the fade quad toward 0x00 or toward 0xFF, one step per
    // frame, and on the frame the ramp lands it rewrites the state word and deletes its own task.
    // ONLY r0 (0x800C3C00) IS EVER READ BACK; the other eleven bytes are write-only mirrors of it.
    //
    // TWO MASKS, TWO DIFFERENT JOBS. `& 0xbfff` strips bit 14 before the dispatch, so 0x4003 enters
    // case 3 and 0x4005 enters case 5; `& 0x4000` is then tested on its own before each DeleteTask,
    // because bit 14 means "FUN_80042054 called me directly and there is no task of mine on any
    // list". The jump table covers exactly [2..6]; any other state falls through with bVar1 still
    // false and this function does nothing at all.
    //
    // GHIDRA PRINTS `unaff_s0` because the switch is an indirect `jr v0` through a jump table and
    // the decompiler cannot see the register's definition on every path. It is in fact assigned on
    // every path that later reads it. C# demands a definite assignment, so it is zeroed at the top;
    // no original path observes the value chosen here, because the three case-6 arms that leave it
    // alone also leave bVar1 false and the tail is skipped.
    //
    // THE STEP IS READ AT TWO DIFFERENT WIDTHS, and that is the original's own inconsistency, not a
    // porting slip: every range comparison loads 0x8008D3F4 with `lhu` and every piece of ramp
    // arithmetic loads it with `lbu`. BattleScene.DAT_8008d3f4 is declared `int` for exactly that
    // reason, and each site below re-applies the width its instruction used. A step above 0xFF
    // would make the two disagree; VS.EXE only ever passes 0x20, 0x40 and 0xFF.
    internal static void FUN_800424b0()
    {
        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            return;
        }

        int unaff_s0 = 0;
        bool bVar1 = false;

        switch (VS_EXE_exe.DAT_8008d398 & 0xbfff)
        {
            case 2:
                // `lhu` compare against the ramp byte r0.
                if ((ushort)BattleScene.DAT_8008d3f4 < PsxRam.ReadU8(Dat800c3bfcAddress + 4))
                {
                    goto LAB_80042528;
                }

                unaff_s0 = 0;
                if ((VS_EXE_exe.DAT_8008d398 & 0x4000) == 0)
                {
                    // FUN_8005354c(DAT_8008d16c, DAT_8008d170) — TaskSystem.DeleteTask with
                    // g_CurrentTask and g_CurrentTaskListIndex, both already ported.
                    TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                        (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                }

                VS_EXE_exe.DAT_8008d398 = 1;
                break;

            // 0x80042528. Case 4 branches back here; the binary shares this one block between the
            // two arms rather than duplicating it, so the C# shares it too.
            LAB_80042528:
                // `lbu` arithmetic on the same global the compare above read as a halfword.
                unaff_s0 = PsxRam.ReadU8(Dat800c3bfcAddress + 4) - (byte)BattleScene.DAT_8008d3f4;
                break;

            case 3:
                if (0xfe < (uint)PsxRam.ReadU8(Dat800c3bfcAddress + 4)
                    + (uint)(ushort)BattleScene.DAT_8008d3f4)
                {
                    LibGpu.SetDispMask(0);
                    unaff_s0 = 0xff;
                    if ((VS_EXE_exe.DAT_8008d398 & 0x4000) == 0)
                    {
                        TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                            (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                    }

                    VS_EXE_exe.DAT_8008d398 = 0;
                    bVar1 = true;
                    goto switchD_80042504_default;
                }

            // 0x80042640. It physically sits inside case 5's block; case 3 branches forward into it
            // and case 5 falls into it, so the increment is written once for both.
            LAB_80042640:
                unaff_s0 = PsxRam.ReadU8(Dat800c3bfcAddress + 4) + (byte)BattleScene.DAT_8008d3f4;
                break;

            case 4:
                // Behaviourally identical to case 2 — the binary duplicates the block instead of
                // sharing it. Kept as two blocks, per the "do not merge" rule.
                if ((ushort)BattleScene.DAT_8008d3f4 < PsxRam.ReadU8(Dat800c3bfcAddress + 4))
                {
                    goto LAB_80042528;
                }

                unaff_s0 = 0;
                if ((VS_EXE_exe.DAT_8008d398 & 0x4000) == 0)
                {
                    TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                        (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                }

                VS_EXE_exe.DAT_8008d398 = 1;
                break;

            case 5:
                // Same saturating add and same 0xFF threshold as case 3. The differences: no
                // SetDispMask here, and the terminal state is 7 rather than 0.
                if ((uint)PsxRam.ReadU8(Dat800c3bfcAddress + 4)
                    + (uint)(ushort)BattleScene.DAT_8008d3f4 < 0xff)
                {
                    goto LAB_80042640;
                }

                unaff_s0 = 0xff;
                if ((VS_EXE_exe.DAT_8008d398 & 0x4000) == 0)
                {
                    TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                        (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                }

                VS_EXE_exe.DAT_8008d398 = 7;
                break;

            case 6:
                // Here the step is a COUNTER, not a per-frame delta: FUN_80042054 case 6 forces it
                // to 0 and this block drives it 0 -> 1 -> 2. Three frames, and the first two write
                // the twelve bytes themselves and leave bVar1 false so the tail is skipped.
                if ((ushort)BattleScene.DAT_8008d3f4 == 1)
                {
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x2a, 8); // b3
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x29, 8); // g3
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x28, 8); // r3
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x06, 8); // b0
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x05, 8); // g0
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x04, 8); // r0
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x1e, 8); // b2
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x1d, 8); // g2
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x1c, 8); // r2
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x12, 8); // b1
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x11, 8); // g1
                    PsxRam.WriteU8(Dat800c3bfcAddress + 0x10, 8); // r1
                    BattleScene.DAT_8008d3f4 = 2;
                    bVar1 = false;
                }
                else if ((ushort)BattleScene.DAT_8008d3f4 < 2)
                {
                    if ((ushort)BattleScene.DAT_8008d3f4 == 0)
                    {
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x2a, 0x20); // b3
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x29, 0x20); // g3
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x28, 0x20); // r3
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x06, 0x20); // b0
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x05, 0x20); // g0
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x04, 0x20); // r0
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x1e, 0x20); // b2
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x1d, 0x20); // g2
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x1c, 0x20); // r2
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x12, 0x20); // b1
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x11, 0x20); // g1
                        PsxRam.WriteU8(Dat800c3bfcAddress + 0x10, 0x20); // r1
                        BattleScene.DAT_8008d3f4 = 1;
                        bVar1 = false;
                    }
                    else
                    {
                        // PARTIAL: the `< 2` test is a signed `slti` applied to a value the
                        // original loaded with `lhu`, so reaching this arm needs a negative
                        // halfword and a zero-extending load cannot produce one. Kept because it
                        // exists in the binary; calling it dead outright would need an audit of
                        // every writer of 0x8008D3F4 outside this file. Same PARTIAL that
                        // TITLE_EXE/DisplayMachine.cs records on the twin.
                        bVar1 = false;
                    }
                }
                else if ((ushort)BattleScene.DAT_8008d3f4 == 2)
                {
                    unaff_s0 = 0;
                    VS_EXE_exe.DAT_8008d398 = 1;
                    bVar1 = true;

                    // NO 0x4000 GUARD on this one, unlike the four above, and the asymmetry is in
                    // the machine code rather than in the decompiler: there is no `andi 0x4000` in
                    // this block. It is consistent with the transitions — state 6 is only ever
                    // entered through FUN_80042054 case 6, which always goes through CreateTask.
                    TaskSystem.DeleteTask(TaskSystem.g_CurrentTask,
                        (uint)(ushort)TaskSystem.g_CurrentTaskListIndex);
                }
                else
                {
                    bVar1 = false;
                }

                // Ghidra prints case 6 as falling into the default label. It does not: all four
                // arms jump past the default target, which only recomputes the test each arm has
                // already done. Same destination in effect.
                goto switchD_80042504_default;

            default:
                goto switchD_80042504_default;
        }

        bVar1 = true;

        switchD_80042504_default:
        if (bVar1)
        {
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x04, (byte)unaff_s0); // DAT_800c3c00, r0
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x05, (byte)unaff_s0); // DAT_800c3c01, g0
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x06, (byte)unaff_s0); // DAT_800c3c02, b0
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x10, (byte)unaff_s0); // DAT_800c3c0c, r1
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x11, (byte)unaff_s0); // DAT_800c3c0d, g1
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x12, (byte)unaff_s0); // DAT_800c3c0e, b1
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x1c, (byte)unaff_s0); // DAT_800c3c18, r2
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x1d, (byte)unaff_s0); // DAT_800c3c19, g2
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x1e, (byte)unaff_s0); // DAT_800c3c1a, b2
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x28, (byte)unaff_s0); // DAT_800c3c24, r3
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x29, (byte)unaff_s0); // DAT_800c3c25, g3
            PsxRam.WriteU8(Dat800c3bfcAddress + 0x2a, (byte)unaff_s0); // DAT_800c3c26, b3
        }
    }

    // =====================================================================================
    // FUN_80042f74 — the GTE-placed effect anchor
    // =====================================================================================

    // GHIDRA: FUN_80042f74 @ 0x80042F74 (VS.EXE)
    // 784 bytes, 0x80042F74..0x80043283, and ZERO CALLERS in the image. Reported as such rather
    // than silently ported as if it were live: Ghidra finds no reference to this address from any
    // function in /VS.EXE, so it is either reached only through a pointer this analysis has not
    // resolved, or it is dead code the linker kept. Its body is complete and unambiguous, so it is
    // transliterated in full; nothing in the port calls it yet, and nothing should be invented to.
    //
    // WHAT IT DOES: picks one of three camera-distance bands from the fighter's own +0x120, walks
    // two levels of relocated table (the `< 0x80000000` tests are the image's own "this word is a
    // file offset, rebase it onto the block base at *param_2" idiom), reads a two-byte offset pair
    // out of DAT_80082c8a keyed on the character id and the band, adds it to a bias taken from the
    // resolved record's +6/+7, rotates the result about Y by a per-state angle less the camera
    // yaw, and writes the transformed point into param_1 + 0x40/0x42/0x44 relative to a base
    // position read through param_2[3].
    //
    // THE RELOCATION IDIOM appears three times with two different tests — `-1 < iVar2` (signed, on
    // a word already loaded as int) and `uVar4 < 0x80000000` (unsigned) — which is the same test
    // written twice by the compiler: a word whose top bit is clear is an offset and gets *param_2
    // added, a word whose top bit is set is already an absolute pointer. Both forms are kept as
    // they are printed.
    internal static void FUN_80042f74(int param_1, int param_2)
    {
        // Ghidra's `in_t1` — the band's byte offset inside the 6-byte per-character row. It is a
        // register the decompiler could not see initialised on every path; the three arms below
        // cover uVar6 in {10, 11, 12}, which is every value uVar6 can hold, so this initialiser is
        // never observed. C# demands the definite assignment.
        int in_t1 = 0;

        // Ghidra's `unaff_s1` — the rotation angle. THE ORIGINAL LEAVES IT UNDEFINED on several
        // paths: bVar1 values 5..0x80, 0x84 and above, and 0x80 itself, all fall through the
        // cascade below without assigning s1, and the PushMatrix block then rotates by whatever
        // the caller happened to leave in that register. That is a real defect in the original and
        // it is reproduced as far as C# allows: the value is zeroed here, which is one specific
        // choice where the console has none, and this DEVIATION is stated rather than hidden.
        // DEVIATION: C# definite assignment forces a value on paths where the original has none.
        short unaff_s1 = 0;

        LibGte.SVECTOR local_68 = new();
        LibGte.SVECTOR local_60 = new();
        LibGte.VECTOR local_58 = new();
        LibGte.MATRIX MStack_48 = new();
        int[] alStack_28 = new int[4];

        int iVar7 = PsxRam.ReadI32(PsxRam.ReadI32(param_2 + 0xc) + 0x24);
        uint uVar6 = 10;

        // Two UNSIGNED range tests written as a subtract-and-compare. `x - 0x800 < 0x601` is
        // 0x800 <= x <= 0xE00; `x - 0x100 < 0x701` is 0x100 <= x <= 0x800. The second overrides the
        // first at exactly x == 0x800, which is the original's own ordering.
        if ((uint)(PsxRam.ReadU16(iVar7 + 0x120) - 0x800) < 0x601)
        {
            uVar6 = 0xc;
        }

        if ((uint)(PsxRam.ReadU16(iVar7 + 0x120) - 0x100) < 0x701)
        {
            uVar6 = 0xb;
        }

        int iVar2 = PsxRam.ReadI32(PsxRam.ReadI32(iVar7 + 0x148) + 0x24);
        if (-1 < iVar2)
        {
            iVar2 = iVar2 + PsxRam.ReadI32(param_2);
        }

        uint uVar4 = (uint)PsxRam.ReadI32((int)(uVar6 * 4) + iVar2);
        if (uVar4 < 0x80000000)
        {
            uVar4 = (uint)((int)uVar4 + PsxRam.ReadI32(param_2));
        }

        uint uVar3 = (uint)PsxRam.ReadI32(PsxRam.ReadI32(iVar7 + 0x148) + 0x14);
        if (uVar3 < 0x80000000)
        {
            uVar3 = (uint)((int)uVar3 + PsxRam.ReadI32(param_2));
        }

        uVar4 = (uint)PsxRam.ReadI32((int)((uint)PsxRam.ReadU8((int)uVar4 + 1) * 4 + uVar3));
        if (uVar4 < 0x80000000)
        {
            uVar4 = (uint)((int)uVar4 + PsxRam.ReadI32(param_2));
        }

        local_60.vz = 0;
        if (uVar6 == 10)
        {
            in_t1 = 0;
        }
        else if (9 < uVar6)
        {
            if (uVar6 == 0xb)
            {
                in_t1 = 4;
            }
            else if (uVar6 == 0xc)
            {
                in_t1 = 2;
            }
        }

        // `**(ushort **)(iVar7 + 0x104)` — two dereferences: the pointer at +0x104, then the
        // halfword it points at. That halfword is the character id, and the row is six SIGNED bytes
        // wide. The bias is a byte read minus 0x80, i.e. an unsigned byte re-centred on zero, NOT a
        // signed byte; both widths are the original's.
        int rowBase = Dat80082c8aAddress
            + in_t1
            + (int)((uint)PsxRam.ReadU16(PsxRam.ReadI32(iVar7 + 0x104)) * 6);
        local_60.vx = (short)((sbyte)PsxRam.ReadU8(rowBase)
                              + (PsxRam.ReadU8((int)uVar4 + 6) - 0x80));
        local_60.vy = (short)((sbyte)PsxRam.ReadU8(rowBase + 1)
                              + (PsxRam.ReadU8((int)uVar4 + 7) - 0x80));

        // FUN_80045af0 @ 0x80045AF0 is already ported, in FighterCombat.cs, and is reached by
        // qualified name rather than redeclared. It returns a ushort there; the original truncates
        // it to a byte on the way into this cascade (`lbu` off the returned $v0), which is why the
        // 0x81/0x82/0x83 arms below can be reached at all.
        byte bVar1 = (byte)FighterCombat.FUN_80045af0((short)PsxRam.ReadU16(iVar7 + 0x11e));
        if (bVar1 < 5)
        {
            if (bVar1 < 3)
            {
                // Ghidra prints the guard as the constant `true` and folds the assignment into the
                // condition with a comma operator: `if ((true) && (unaff_s1 = -0x200, 1 < bVar1))`.
                // C# has no comma operator, so the store is written before the test — which is
                // exactly the order the machine code uses, since the store sits in the branch's
                // delay slot. Values 0 and 1 keep -0x200; value 2 overwrites it with 0.
                unaff_s1 = -0x200;
                if (1 < bVar1)
                {
                    unaff_s1 = 0;
                }
            }
            else
            {
                unaff_s1 = 0x200;
            }
        }
        else if (bVar1 == 0x82)
        {
            unaff_s1 = 0x800;
        }
        else if (bVar1 < 0x83)
        {
            if (bVar1 == 0x81)
            {
                unaff_s1 = -0x600;
            }

            // No else: 5..0x80 leave s1 undefined. See the DEVIATION note above.
        }
        else if (bVar1 == 0x83)
        {
            unaff_s1 = 0x600;
        }

        LibGte.PushMatrix();
        local_68.vx = 0;
        local_68.vz = 0;

        // DAT_1f80007e is the vy field of the scratchpad SVECTOR at 0x1F80007C — the camera yaw.
        // AnimCmdControl.cs and FighterCombat.FUN_80045af0 already close this address the same way.
        local_68.vy = (short)(unaff_s1 - Scratchpad.SVECTOR_1f80007c.vy);
        LibGte.RotMatrix(local_68, MStack_48);
        MStack_48.t[2] = 0;
        MStack_48.t[1] = 0;
        MStack_48.t[0] = 0;
        LibGte.SetRotMatrix(MStack_48);
        LibGte.SetTransMatrix(MStack_48);
        LibGte.RotTrans(local_60, local_58, alStack_28);
        LibGte.PopMatrix();

        // `psVar5 = *(short **)param_2[3]` — the base position, three signed halfwords, reached
        // through the same +0x0C the function opened with.
        int psVar5 = PsxRam.ReadI32(PsxRam.ReadI32(param_2 + 0xc));
        PsxRam.WriteU16(param_1 + 0x40,
            (ushort)((short)PsxRam.ReadU16(psVar5) + (short)local_58.vx));
        PsxRam.WriteU16(param_1 + 0x42,
            (ushort)((short)PsxRam.ReadU16(psVar5 + 2) + (short)local_58.vy));
        PsxRam.WriteU16(param_1 + 0x44,
            (ushort)((short)PsxRam.ReadU16(psVar5 + 4) + (short)local_58.vz));
    }

    // =====================================================================================
    // FUN_8003f994 — the per-frame transform interpolator
    // =====================================================================================

    // GHIDRA: FUN_8003f994 @ 0x8003F994 (VS.EXE)
    // 756 bytes, 0x8003F994..0x8003FC87. AnimCmd_CheffWait (opcode 12) calls it once per frame.
    //
    // TWO ARMS, selected by DAT_80099066 — the flag opcode 12's init path raises:
    //
    //   ARMING (DAT_80099066 != 0). Differences the two resolved targets against the CURRENT
    //   scratchpad position and rotation to make five per-frame deltas, scales the current values
    //   by sixteen into the accumulators, arms the frame budget at 0x10, and clears the flag.
    //
    //   INTEGRATING (DAT_80099066 == 0 and the budget is not yet spent). Adds each delta into its
    //   accumulator, publishes the accumulator back to the scratchpad as a 4.12 value (`>> 4` with
    //   a +0xF pre-bias on the negative side — the compiler's own round-toward-zero expansion of a
    //   divide by 16, kept literal rather than folded into a division), and counts the budget down.
    //   The frame the budget reaches zero it returns 1; on every other frame it returns 0.
    //
    // THE YAW STEP IS WRAPPED, not clamped: `& 0xfff` then, if the result exceeds 0x800, `^ 0xf000`
    // — an XOR, not a subtraction, which for a 12-bit value in [0x801, 0xFFF] produces
    // 0xF801..0xFFFF, i.e. the negative complement once the `(short)` read below truncates it. That
    // is why DAT_80099080 must be stored wider than sixteen bits and read back through a cast.
    //
    // THE Y VELOCITY'S SIGN IS INVERTED relative to X and Z (`-(current) - target` where the others
    // are `target - current`). Reproduced as printed; whether it is a bug in the original is not
    // closed by this evidence, and it is not "fixed".
    //
    // GHIDRA GIVES IT `undefined4` for a return that is only ever 0 or 1. Kept as `int`; the one
    // caller in this port (AnimCmd_CheffWait) discards it, but the value is real and is not
    // silenced.
    internal static int FUN_8003f994()
    {
        uint uVar1;
        int iVar2;
        int uVar3;

        uVar3 = 0;
        if (AnimCmdEffects.DAT_80099066 == 0)
        {
            if (DAT_8009907a != 0)
            {
                DAT_80099068 = DAT_80099074 + DAT_80099068;
                DAT_8009906c = DAT_80099076 + DAT_8009906c;
                DAT_80099070 = DAT_80099078 + DAT_80099070;

                iVar2 = DAT_80099068;
                if (DAT_80099068 < 0)
                {
                    iVar2 = DAT_80099068 + 0xf;
                }

                Scratchpad.DAT_1f8000c4 = iVar2 >> 4;

                iVar2 = DAT_8009906c;
                if (DAT_8009906c < 0)
                {
                    iVar2 = DAT_8009906c + 0xf;
                }

                Scratchpad.DAT_1f8000c8 = iVar2 >> 4;

                iVar2 = DAT_80099070;
                if (DAT_80099070 < 0)
                {
                    iVar2 = DAT_80099070 + 0xf;
                }

                Scratchpad.DAT_1f8000cc = iVar2 >> 4;

                DAT_80099084 = DAT_8009908c + DAT_80099084;

                // The yaw accumulator DECREASES by the step; the others increase by theirs. The
                // step is read back through a `(short)` truncation — see the header note on the
                // XOR wrap.
                DAT_8009907c = DAT_8009907c - (int)(short)DAT_80099080;

                // Published as a 4.12 angle: mask the low four bits off, then arithmetic-shift the
                // whole word right by four. The `(int)(x & 0xfff0) >> 4` form is the original's.
                Scratchpad.DAT_1f800086 = (short)(ushort)((DAT_8009907c & 0xfff0) >> 4);

                iVar2 = DAT_80099084;
                if (DAT_80099084 < 0)
                {
                    iVar2 = DAT_80099084 + 0xf;
                }

                FileIo.DAT_1f8000d0 = iVar2 >> 4;

                DAT_80099088 = DAT_8009908e + DAT_80099088;

                iVar2 = DAT_80099088;
                if (DAT_80099088 < 0)
                {
                    iVar2 = DAT_80099088 + 0xf;
                }

                Scratchpad.DAT_1f800084 = (short)(ushort)(iVar2 >> 4);

                // The countdown, and the ONE place the return value can become 1: the original
                // widens the halfword to int, subtracts, stores the low half back, and then tests
                // the LOW HALF for zero (`uVar1 & 0xffff`). Written exactly that way.
                uVar1 = (uint)((int)DAT_8009907a - 1);
                DAT_8009907a = (short)uVar1;
                uVar3 = 0;
                if ((uVar1 & 0xffff) == 0)
                {
                    uVar3 = 1;
                }
            }
        }
        else
        {
            // DAT_80099058 and DAT_8009905c are POINTERS to the two resolved targets — opcode 12's
            // own init arm fills them from AnimCmdTransform.FUN_8003f228 / FUN_8003f2b0.
            // AnimCmdEffects.cs already declares both as `int`, and they are reached qualified
            // rather than redeclared here.
            DAT_80099074 = (short)((short)PsxRam.ReadU16(AnimCmdEffects.DAT_80099058)
                                   - (short)Scratchpad.DAT_1f8000c4);
            DAT_80099076 = (short)(-(short)Scratchpad.DAT_1f8000c8
                                   - (short)PsxRam.ReadU16(AnimCmdEffects.DAT_80099058 + 2));
            DAT_8009906c = Scratchpad.DAT_1f8000c8 << 4;
            DAT_80099068 = Scratchpad.DAT_1f8000c4 << 4;
            DAT_80099078 = (short)((short)PsxRam.ReadU16(AnimCmdEffects.DAT_80099058 + 4)
                                   - (short)Scratchpad.DAT_1f8000cc);
            DAT_80099070 = Scratchpad.DAT_1f8000cc << 4;

            // `(int)((uint)DAT_1f800086 << 0x10) >> 0xc` — sign-extend the halfword through a
            // 16-bit left shift and a 12-bit arithmetic right shift, which is the same as
            // multiplying the signed angle by sixteen. Left in that form.
            DAT_8009907c = (int)((uint)(ushort)Scratchpad.DAT_1f800086 << 0x10) >> 0xc;

            DAT_80099080 = ((short)PsxRam.ReadU16(AnimCmdEffects.DAT_8009905c + 2)
                            + Scratchpad.DAT_1f800086
                            - AnimCmdEffects.DAT_80099060) & 0xfff;
            if (0x800 < DAT_80099080)
            {
                DAT_80099080 = DAT_80099080 ^ 0xf000;
            }

            DAT_8009907a = 0x10;
            AnimCmdEffects.DAT_80099066 = 0;
            DAT_8009908c = (short)(AnimCmdEffects.DAT_80099062 - (short)FileIo.DAT_1f8000d0);
            DAT_80099084 = FileIo.DAT_1f8000d0 << 4;
            DAT_8009908e = (short)(AnimCmdEffects.DAT_80099064 - Scratchpad.DAT_1f800084);
            DAT_80099088 = (int)((uint)(ushort)Scratchpad.DAT_1f800084 << 0x10) >> 0xc;
            uVar3 = 0;
        }

        return uVar3;
    }

    // =====================================================================================
    // FUN_80045b70 — the facing / orientation table lookup
    // =====================================================================================

    // GHIDRA: FUN_80045b70 @ 0x80045B70 (VS.EXE)
    // 388 bytes, 0x80045B70..0x80045CF3. NO LONGER BLOCKED: the two tables it reads are in .data
    // and were read straight out of the image, which is all that was missing.
    //
    // Two angles in, one byte out. It quantises `camera_yaw + param_2` into one of sixteen
    // sectors, uses that sector to pick a ROW through DAT_80082e44 (whose sixteen bytes are
    // 00 01 01 02 02 03 03 04 04 05 05 06 06 07 07 00, so the row is always 0..7), quantises
    // `param_1 -/+ camera_yaw` into one of sixteen COLUMNS, and returns
    // DAT_80082e54[row * 0x10 + column].
    //
    // THE SECOND TABLE'S BASE is printed by Ghidra as the constant -0x7ff7d1ac. As an unsigned
    // 32-bit value that is 0x80082E54 — the sixteen bytes immediately after the first table — and
    // that is how the address was decoded rather than guessed. Eight rows of sixteen bytes reach
    // 0x80082ED3, and the image's own bytes change character at 0x80082ED4, which corroborates the
    // extent.
    //
    // THE COLUMN IS COMPUTED TWICE, and the second computation silently discards the first: the
    // `if (2 < uVar1 && uVar1 < 5 || 10 < uVar1 && uVar1 < 0xd)` arm overwrites local_12 with
    // `(short)(param_1 & 0xfff) >> 8` — a completely different formula that ignores the camera yaw
    // altogether. Kept in that order; it is not dead code, it is a deliberate override for four of
    // the sixteen sectors.
    //
    // SIGNEDNESS OF param_1. Ghidra types it `ushort` and the C# signature keeps the `short` of the
    // FighterCombat.cs stub this replaces, so its two call sites and FighterCombatArms.cs's two
    // need no rewrite. That is safe rather than convenient: every use of param_1 below is either
    // through an explicit `(short)` cast, which is the identity on a short, or under a `& 0xfff`
    // mask, which depends only on the bit pattern. The two types are bit-identical here.
    internal static byte FUN_80045b70(short param_1, short param_2)
    {
        ushort local_12;

        // DAT_1f80007e — the vy of the scratchpad SVECTOR at 0x1F80007C, the camera yaw.
        ushort uVar1 = (ushort)(((uint)((int)Scratchpad.SVECTOR_1f80007c.vy + (int)param_2) >> 8)
                                & 0xf);

        if (uVar1 < 5 || 10 < uVar1)
        {
            local_12 = (ushort)((uint)((int)param_1 - (int)Scratchpad.SVECTOR_1f80007c.vx) >> 8);
        }
        else
        {
            local_12 = (ushort)((uint)((int)param_1 + (int)Scratchpad.SVECTOR_1f80007c.vx) >> 8);
        }

        local_12 = (ushort)(local_12 & 0xf);
        if ((2 < uVar1 && uVar1 < 5) || (10 < uVar1 && uVar1 < 0xd))
        {
            local_12 = (ushort)((short)(param_1 & 0xfff) >> 8);
        }

        return PsxRam.ReadU8((int)((uint)PsxRam.ReadU8(Dat80082e44Address + uVar1) * 0x10)
                             + Dat80082e54Address
                             + (int)(uint)local_12);
    }

    // =====================================================================================
    // FUN_800461fc — the GTE rotate/translate helper
    // =====================================================================================

    // GHIDRA: FUN_800461fc @ 0x800461FC (VS.EXE)
    // 228 bytes, 0x800461FC..0x800462DF. Ghidra's signature is
    // `void FUN_800461fc(SVECTOR *param_1, ushort *param_2, VECTOR *param_3)`.
    //
    // It builds a rotation from three angles held at param_2 — masking each to twelve bits and
    // biasing the middle one by -0x400, i.e. a quarter turn — installs it as BOTH the rotation and
    // the translation matrix with a zero translation, and rotates param_1 into param_3. Every
    // callee is libgte, at or above 0x800632C4, so all six are CALLED and none is ported.
    //
    // THE PARAMETER SHAPE FOLLOWS THE STUB THIS REPLACES rather than Ghidra's prototype, and the
    // reason is recorded in FighterCombat.cs's own note on the address: its caller,
    // UpdateAttackEventTask, builds param_1's three halfwords on its OWN C stack
    // (local_18/local_16/local_14), and this port has no PSX address for a C# local. Passing the
    // three values instead of a pointer is EXACTLY equivalent here, because param_1 is only ever
    // READ — RotTrans's source — and never written. param_2 and param_3 stay raw PSX addresses,
    // which is what they are on the console (workspace+0x48 and workspace+0x60).
    // JUSTIFICATION: C# language bridge only
    internal static void FUN_800461fc(short param1Vx, short param1Vy, short param1Vz, int param_2,
        int param_3)
    {
        LibGte.MATRIX MStack_38 = new();
        int[] alStack_18 = new int[2];
        LibGte.SVECTOR local_10 = new();

        local_10.vx = (short)(PsxRam.ReadU16(param_2) & 0xfff);
        local_10.vy = (short)((PsxRam.ReadU16(param_2 + 2) - 0x400) & 0xfff);
        local_10.vz = (short)(PsxRam.ReadU16(param_2 + 4) & 0xfff);

        LibGte.PushMatrix();
        LibGte.RotMatrix(local_10, MStack_38);
        MStack_38.t[2] = 0;
        MStack_38.t[1] = 0;
        MStack_38.t[0] = 0;

        // SetTransMatrix BEFORE SetRotMatrix here, the opposite of FUN_80042f74's order above.
        // Both are the originals' own; the order is not normalised.
        LibGte.SetTransMatrix(MStack_38);
        LibGte.SetRotMatrix(MStack_38);

        // JUSTIFICATION: C# language bridge only
        // RELATION: param_1 is the caller's stack SVECTOR, passed here as its three components, and
        // param_3 is a raw PSX VECTOR the SDK cannot write into directly. A local VECTOR takes the
        // result and its three words are stored back at param_3 + 0/4/8, which is byte for byte
        // what RotTrans does on the console.
        LibGte.SVECTOR local_p1 = new() { vx = param1Vx, vy = param1Vy, vz = param1Vz };
        LibGte.VECTOR local_p3 = new();
        LibGte.RotTrans(local_p1, local_p3, alStack_18);
        PsxRam.WriteI32(param_3 + 0, local_p3.vx);
        PsxRam.WriteI32(param_3 + 4, local_p3.vy);
        PsxRam.WriteI32(param_3 + 8, local_p3.vz);

        LibGte.PopMatrix();
    }

    // =====================================================================================
    // The two task seeders
    // =====================================================================================

    // GHIDRA: FUN_800437ec @ 0x800437EC (VS.EXE)
    // 212 bytes, 0x800437EC..0x800438BF. Seven call sites, all four of them inside
    // FighterCombatArms.cs's four functions.
    //
    // It creates ONE task — id 0, list 0xB, 0x58 bytes of workspace, entry LAB_800436D0 — and then
    // seeds that workspace: three halfwords copied from param_1 to +0x3C/+0x3E/+0x40, three more
    // from param_2 to +0x44/+0x46/+0x48, and a three-node pointer web (+0x0C -> +0x50,
    // +0x50 -> +0x10, +0x54 -> +0x3C) that makes the workspace point into itself. Finally it hands
    // the workspace to FUN_80053970 with the block at 0x800217F0 and the caller's mode.
    //
    // THE STORE ORDER IS THE ORIGINAL'S and it is not tidied: param_2's THIRD halfword is loaded
    // into uVar1 BEFORE the three pointer stores and only written to +0x48 after them. That is the
    // compiler's scheduling, it is visible in the decompilation, and re-ordering it would be an
    // optimisation.
    //
    // THE TASK ENTRY NEEDS A RegisterCallback AND DOES NOT HAVE ONE. See Lab800436d0Address above:
    // the body at 0x800436D0 is not ported anywhere in VS_EXE/, so there is nothing to register.
    // The node is created and linked exactly as the original creates it; dispatch will find no
    // ported body and do nothing. Reported rather than papered over with an invented body.
    internal static void FUN_800437ec(int param_1, int param_2, ushort param_3)
    {
        // FUN_80053330 is TaskSystem.CreateTask; DAT_80083bbc is g_TaskListTail[0xB] — the tail
        // array starts at 0x80083B90, so (0x80083BBC - 0x80083B90) / 4 = 11, which matches the
        // listIndex of 0xB. Same check FighterSetup.cs and BattleManager.cs apply to their own.
        int iVar2 = TaskSystem.CreateTask(Lab800436d0Address, 0, 0xb, 0x58, 0,
            TaskSystem.g_TaskListTail[0xb]);
        if (iVar2 != 0)
        {
            iVar2 = PsxRam.ReadI32(iVar2 + 8);
            PsxRam.WriteU16(iVar2 + 0x3c, PsxRam.ReadU16(param_1));
            PsxRam.WriteU16(iVar2 + 0x3e, PsxRam.ReadU16(param_1 + 2));
            PsxRam.WriteU16(iVar2 + 0x40, PsxRam.ReadU16(param_1 + 4));
            PsxRam.WriteU16(iVar2 + 0x44, PsxRam.ReadU16(param_2));
            PsxRam.WriteU16(iVar2 + 0x46, PsxRam.ReadU16(param_2 + 2));
            ushort uVar1 = PsxRam.ReadU16(param_2 + 4);
            PsxRam.WriteI32(iVar2 + 0xc, iVar2 + 0x50);
            PsxRam.WriteI32(iVar2 + 0x50, iVar2 + 0x10);
            PsxRam.WriteI32(iVar2 + 0x54, iVar2 + 0x3c);
            PsxRam.WriteU16(iVar2 + 0x48, uVar1);
            FighterCombat.FUN_80053970(iVar2, PTR_DAT_800217f0Address, param_3);
        }
    }

    // GHIDRA: FUN_8003fe98 @ 0x8003FE98 (VS.EXE)
    // 192 bytes, 0x8003FE98..0x8003FF57. AnimCmd_EffSet's effect constructor: the same shape as
    // FUN_800437ec above but a different entry (LAB_8003FC88), a different workspace size (0x5C),
    // only TWO halfwords copied out of param_1 plus a third into +0x40, a pointer web one word
    // shorter (+0x0C -> +0x48, +0x48 -> +0x10, +0x4C -> +0x3C), and param_2 stored at +0x54 as well
    // as handed to FUN_80053970.
    //
    // IT RETURNS THE TASK NODE, not the workspace: iVar2 is the CreateTask result and iVar3 the
    // workspace read out of its +0x08, and every store goes to iVar3 while the return is iVar2.
    // AnimCmd_EffSet then reads `+8` off it, which is that same workspace pointer. Two different
    // variables, one letter apart in Ghidra's naming, and getting them the wrong way round would
    // be silent — stated here for that reason.
    //
    // THE TASK ENTRY NEEDS A RegisterCallback AND DOES NOT HAVE ONE — same situation as
    // FUN_800437ec, for LAB_8003FC88 this time. Reported.
    //
    // The `(int, int)` signature and `int` return are the stub's, kept so AnimCmd_EffSet's one call
    // site needs only to be qualified. param_2 is an `undefined2` on the console and is narrowed at
    // the two places it is actually stored.
    internal static int FUN_8003fe98(int param_1, int param_2)
    {
        int iVar2 = TaskSystem.CreateTask(Lab8003fc88Address, 0, 0xb, 0x5c, 0,
            TaskSystem.g_TaskListTail[0xb]);
        if (iVar2 == 0)
        {
            iVar2 = 0;
        }
        else
        {
            int iVar3 = PsxRam.ReadI32(iVar2 + 8);
            PsxRam.WriteU16(iVar3 + 0x3c, PsxRam.ReadU16(param_1));
            PsxRam.WriteU16(iVar3 + 0x3e, PsxRam.ReadU16(param_1 + 2));
            ushort uVar1 = PsxRam.ReadU16(param_1 + 4);
            PsxRam.WriteI32(iVar3 + 0xc, iVar3 + 0x48);
            PsxRam.WriteI32(iVar3 + 0x48, iVar3 + 0x10);
            PsxRam.WriteI32(iVar3 + 0x4c, iVar3 + 0x3c);
            PsxRam.WriteU16(iVar3 + 0x54, (ushort)param_2);
            PsxRam.WriteU16(iVar3 + 0x40, uVar1);
            FighterCombat.FUN_80053970(iVar3, PTR_DAT_800217f0Address, (uint)param_2);
        }

        return iVar2;
    }
}
