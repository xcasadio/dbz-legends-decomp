using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE BATTLE CAMERA — task id 0x55, list 0x13, and the gate on everything else.
//
// WHAT IT IS. main @ 0x80062134 creates it at 0x80062300 with
// `CreateTask(&FUN_80027670, 0x55, 0x13, 0, 0, g_TaskListTail[0x13])`; Ghidra records the entry
// only as a PARAM reference from main, which is why the function shows zero callers. It runs once
// per frame for the whole match and writes the view: the rotation triple at 0x1F800084/86/88, the
// translation triple at 0x1F8000C4/C8/CC and the projection distance at 0x1F8000D0, all of which
// VS_EXE_exe.cs's own frame loop copies into the live GTE set.
//
// WHY IT MATTERS BEYOND THE VIEW, and why it is ported now rather than with the rest of the
// rendering. Two bits that stop the whole battle are cleared HERE and nowhere else:
//
//   CtxFlags bit 31. `and v0,v0,a0` at 0x80027A7C followed by `sw v0,0x10(v1)` at 0x80027A80,
//   with the 0x7FFFFFFF mask built at 0x80027A64/0x80027A70, is the only place in the overlay that
//   clears it. A byte scan for a mask of that shape finds thirteen `lui _,0x7fff` INSTRUCTIONS (a
//   fourteenth match at 0x80083124 is data, not code), and this is the one that lands on the
//   battle context. Until it clears, RunBattleRound's own
//   `(CtxFlags & 0x80008000) == 0x8000` gate stays shut -- note that this is the gate that wants
//   bit 31 CLEAR and bit 15 SET, which is a DIFFERENT test from the pad override at 0x80056358,
//   where the compare register holds 0x80008000 and BOTH bits must be up.
//
//   Every fighter's +0x138 bit 25. The loop four instructions later walks all twelve slots and
//   clears it. UpdateFighter skips step 9.3 -- the frame's command word -- while that bit is up,
//   and ActivateFighterInSlot sets it on every fighter it activates, so without this loop the
//   command word is 0 for ever and no attack ever starts.
//
// THE ROUND-START SEQUENCE, end to end, as this file completes it:
//   1. FUN_80055EE0 arms the match with CtxFlags bits 13, 14, 15 and 31 set.
//   2. The camera sees `flags & 0x80008000 == 0x80008000` and holds the opening pose, resetting
//      its own countdown at DAT_8008D154 to 0x50.
//   3. A pad press drives RunBattleRound's override, which reaches the legality-sweep arm; that arm
//      clears bit 15 and marks the slot records, and the substitution manager brings the fighters
//      on to the field.
//   4. The camera now sees `flags & 0x80008000 == 0x80000000` and runs the countdown -- a fly-in
//      that pulls the eye down and in over about eighty frames.
//   5. On the last tick it clears CtxFlags bit 31 and every fighter's +0x138 bit 25, and the match
//      is live.
//
// THE CAMERA MODE WORD is DAT_8008D124, with the previous frame's value in DAT_8008D128. Its
// values are a set of single bits (1, 2, 4, 8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x200, 0x400, 0x800,
// 0x1000, 0x2000, 0x4000, 0x8000, 0x10000) and each selects a different rule for where the eye
// goes. What each one MEANS is not established by this function alone -- it is chosen partly here
// and partly by the state of the fighter it is following -- so the switch below is transliterated
// on its constants and named after nothing.
//
// WHAT IS DELIBERATELY LEFT OPEN. DAT_8008D580 + 0x76 is a halfword of the scene workspace this
// function branches on four ways; BattleScene.cs owns that workspace and does not name +0x76, so
// it stays a raw offset here too.
internal static class BattleCamera
{
    // =====================================================================================
    // THE CAMERA'S OWN GLOBALS — 0x8008D10C..0x8008D15A, gp-relative small data
    // =====================================================================================
    // None of these is declared anywhere else in the port; this file is their only reader and
    // writer, which is what the cross-references say (every one of them is touched by this function
    // alone, except DAT_8008D15C/DAT_8008D15E, which BattleManager.cs already owns and which this
    // function only negates). Ghidra's own widths are used: undefined2 becomes ushort or short by
    // how the body reads it back, undefined4 becomes int.

    // GHIDRA: DAT_8008d10c / DAT_8008d10e / DAT_8008d110 @ 0x8008D10C (VS.EXE)
    // The point the camera is looking at, in arena coordinates. Copied out of a fighter's own
    // +0x114 triple, then pushed around by the mode machine.
    private static ushort DAT_8008d10c;

    private static ushort DAT_8008d10e;

    private static ushort DAT_8008d110;

    // GHIDRA: DAT_8008d114 / DAT_8008d116 / DAT_8008d118 @ 0x8008D114 (VS.EXE)
    // How far the followed fighter moved since last frame: the previous triple below minus its
    // current one. Signed, and only mode 1 uses it.
    private static short DAT_8008d114;

    private static short DAT_8008d116;

    private static short DAT_8008d118;

    // GHIDRA: DAT_8008d11c / DAT_8008d11e / DAT_8008d120 @ 0x8008D11C (VS.EXE)
    // The followed fighter's position as of the END of the previous frame; the tail stamps it.
    private static ushort DAT_8008d11c;

    private static ushort DAT_8008d11e;

    private static ushort DAT_8008d120;

    // GHIDRA: DAT_8008d124 @ 0x8008D124 (VS.EXE)
    // THE CAMERA MODE. See the file header: a set of single bits, each selecting one rule.
    private static int DAT_8008d124;

    // GHIDRA: DAT_8008d128 @ 0x8008D128 (VS.EXE)
    // The previous frame's mode, stamped by the tail. Read in exactly two places, both to notice
    // that the mode has just changed.
    private static int DAT_8008d128;

    // GHIDRA: DAT_8008d12c / DAT_8008d130 / DAT_8008d134 / DAT_8008d138 / DAT_8008d13c
    // @ 0x8008D12C (VS.EXE)
    // Five 32-bit scratch cells the original writes and reads back inside one expression rather
    // than using a register. They are globals in the image, so they are globals here: a later
    // function may read one, and collapsing them into locals would hide that.
    private static int DAT_8008d12c;

    private static int DAT_8008d130;

    private static int DAT_8008d134;

    private static int DAT_8008d138;

    private static int DAT_8008d13c;

    // GHIDRA: DAT_8008d142 @ 0x8008D142 (VS.EXE)
    // A signed frame counter the mode machine runs up or down; reaching zero is what lets the mode
    // step to its neighbour. Also reused as the divisor of the 0x2000 interpolation.
    private static short DAT_8008d142;

    // GHIDRA: DAT_8008d144 @ 0x8008D144 (VS.EXE)
    // A 12-bit angle difference, wrapped through `^ 0xF000` when it exceeds half a turn.
    private static ushort DAT_8008d144;

    // GHIDRA: DAT_8008d146 @ 0x8008D146 (VS.EXE)
    // The projection distance the tail eases 0x1F8000D0 towards, clamped to 0xDFF.
    private static ushort DAT_8008d146;

    // GHIDRA: DAT_8008d148 @ 0x8008D148 (VS.EXE)
    private static short DAT_8008d148;

    // GHIDRA: DAT_8008d14a / DAT_8008d14c / DAT_8008d14e @ 0x8008D14A (VS.EXE)
    // The three axis errors between where the eye is and where it should be, recomputed by the
    // tail and used to pick a fast or a slow ease per axis.
    private static short DAT_8008d14a;

    private static short DAT_8008d14c;

    private static short DAT_8008d14e;

    // GHIDRA: DAT_8008d150 @ 0x8008D150 (VS.EXE)
    // The slot the camera followed last frame; a change forces mode 0x2000, the interpolated cut.
    private static short DAT_8008d150;

    // GHIDRA: DAT_8008d152 @ 0x8008D152 (VS.EXE)
    // A 1..0x10 ramp that grows while the followed fighter is in state 2 or 10 and resets to 1
    // otherwise. It scales the shake this function feeds to FUN_80047550.
    private static short DAT_8008d152;

    // GHIDRA: DAT_8008d154 / DAT_8008d156 / DAT_8008d158 / DAT_8008d15a @ 0x8008D154 (VS.EXE)
    // THE OPENING FLY-IN. 0x154 is the countdown, seeded to 0x50 by the hold-pose arm; the other
    // three are the eye's height, distance and projection as it comes in, each stepping towards a
    // floor on every tick.
    // Both of these are loaded with `lh` and tested with `slti` in the image (0x80027974,
    // 0x800279F4, 0x80027A3C), so every read below sign-extends. The declared width stays `ushort`
    // because the stores are `sh` and the values live in 0x00..0x100, where the two agree; the
    // casts are there so the code says which one the original does.
    private static ushort DAT_8008d154;

    private static short DAT_8008d156;

    private static short DAT_8008d158;

    private static ushort DAT_8008d15a;

    // GHIDRA: DAT_801ff100 @ 0x801FF100 (VS.EXE)
    // Not a declaration — an INDEX, the same one BattleManager.cs and FighterTask.cs already carry.
    // 0x801FF100 - 0x801FF000 = 0x100 bytes = short index 0x80 of SharedHighRam.SHORT_ARRAY_801ff000.
    private const int Dat801ff100ShortIndex = 0x80;

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: this function's own stack frame, given a real PSX address because three of its
    // locals are passed BY ADDRESS to functions this port models as taking PSX addresses --
    // AnimCmdMesh.ComputeYawPitchToTarget and AnimCmdMesh.DistanceBetweenPositions read triples through a
    // pointer, and AnimCmdControl.FUN_80047550 takes both an angle triple and a VECTOR that way.
    //
    // 0x807FFD70 + 0x110 = 0x807FFE80, which is exactly where FighterCombatArms.cs's own frames
    // begin, and those run to 0x807FFEC0 where BattleManager.cs's gauge strip begins, which runs to
    // AnimCmdControl.cs's 0x807FFFC0. So the whole chain 0x807FFD70..0x807FFFF8 is packed with no
    // overlap and no gap large enough to matter. The stack really does live here: crt0 starts SP at
    // 0x807FFFF8.
    //
    // THE OFFSETS INSIDE IT ARE THE ORIGINAL'S. Ghidra names the locals by their frame offset, so
    // local_108 sits 0x108 below the frame top and local_E8 sits 0xE8 below it; every one of them is
    // placed at `FrameBase + (0x110 - offset)`, which reproduces the spacing between them exactly.
    // That matters for the two position triples: `&local_100` and `&local_F8` are each read as three
    // consecutive halfwords, and local_F0 / local_E8 as four each.
    private const int FrameBase = unchecked((int)0x807FFD70);

    private static readonly byte[] RAM_cameraFrame = LibGpu.RamRegion(FrameBase, 0x110);

    private const int Local108Address = FrameBase + (0x110 - 0x108);

    private const int Local100Address = FrameBase + (0x110 - 0x100);

    private const int LocalF8Address = FrameBase + (0x110 - 0xf8);

    private const int LocalF0Address = FrameBase + (0x110 - 0xf0);

    private const int LocalE8Address = FrameBase + (0x110 - 0xe8);

    // The VECTOR. Ghidra does not name its frame offset, and nothing in the body relates its address
    // to any other local's, so it is placed after local_E8's four halfwords rather than guessed at.
    private const int VectorAddress = FrameBase + (0x110 - 0xe0);

    // GHIDRA: RunBattleCameraTask @ 0x80027670 (VS.EXE)
    // 6600 bytes, 830 decompiled lines, the largest function in the overlay that this port had not
    // touched. Three callees, all already ported: AnimCmdMesh.DistanceBetweenPositions (four call sites),
    // AnimCmdControl.FUN_80047550 (two) and AnimCmdMesh.ComputeYawPitchToTarget (one).
    //
    // DEVIATION: `unaff_s4` IS A REGISTER THE ORIGINAL READS WITHOUT ALWAYS WRITING. Ghidra names it
    // that because register s4 reaches the tail unset on at least one path -- when DAT_8008D124 is 0,
    // the mode switch falls straight through to LAB_80028AC4 and `DAT_8008D124 & 0x7C0` is also 0, so
    // neither writer runs. DAT_8008D124 really can be 0: the 0x2000 arm sets it there when its
    // interpolation finishes. On the console the tail then tests whatever the task dispatcher left in
    // s4. C# has no way to read a caller's callee-saved register and no way to leave a local
    // definitely-unassigned, so it starts at 0, which makes both tail tests (`& 1` and `& 6`) fall
    // through. That is a choice, not a reading of the original, and it is the only one in this file.
    //
    // Two `if` blocks Ghidra prints with a constant-false condition are UNREACHABLE code the
    // compiler left in the image; they are noted where they occur rather than written out, because
    // writing `if (false)` in C# would be a lie about which of the two forms is the original.
    internal static void RunBattleCameraTask()
    {
        if ((AnimVm.DAT_800b305a & 1) != 0)
        {
            return;
        }

        int ctx = BattleManager.DAT_8008d320;
        uint uVar16 = PsxRam.ReadU16(ctx + 0x1a);

        // 0x800276E4 — THE SCENE ARM. When CtxFlags carries either of bits 3 or 27 and neither of
        // bits 1 or 25, the battle-scene machine owns the view and this task only feeds it a fixed
        // pose keyed on the scene workspace's own +0x76.
        if (((uint)PsxRam.ReadI32(ctx + BattleState.CtxFlags) & 0x8000008) != 0
            && ((uint)PsxRam.ReadI32(ctx + BattleState.CtxFlags) & 0x2000002) == 0)
        {
            if (((uint)PsxRam.ReadI32(ctx + BattleState.CtxFlags) & 8) == 0)
            {
                DAT_8008d124 = 0x1000;
                DAT_8008d128 = 0x1000;
                return;
            }

            if (2 < PsxRam.ReadU16(BattleScene.DAT_8008d580 + 0x76)
                && PsxRam.ReadU16(BattleScene.DAT_8008d580 + 0x76) != 4)
            {
                DAT_8008d124 = 0x1000;
                DAT_8008d128 = 0x1000;
                return;
            }

            int iVar15s = PsxRam.ReadI32(
                PsxRam.ReadI32(((int)(uVar16 << 0x10) >> 0xe) + ctx + BattleState.CtxFighterSlots) + 8);
            Scratchpad.DAT_1f8000c4 = (short)PsxRam.ReadU16(iVar15s + 0x114);
            Scratchpad.DAT_1f8000c8 = -0x40 - (short)PsxRam.ReadU16(iVar15s + 0x116);
            Scratchpad.DAT_1f8000cc = (short)PsxRam.ReadU16(iVar15s + 0x118);

            ushort uVar5s = PsxRam.ReadU16(BattleScene.DAT_8008d580 + 0x76);
            if (2 < uVar5s)
            {
                if (uVar5s != 4)
                {
                    DAT_8008d124 = 0x1000;
                    DAT_8008d128 = 0x1000;
                    DAT_8008d146 = 0x200;
                    return;
                }

                Scratchpad.DAT_1f800084 = 0xc0;
                FileIo.DAT_1f8000d0 = 0x280;
                DAT_8008d124 = 0x1000;
                DAT_8008d128 = 0x1000;
                DAT_8008d146 = 0x200;
                return;
            }

            if (uVar5s == 0)
            {
                // The original has a second, constant-false test here whose body repeats the three
                // stores above; the compiler left it in the image and it cannot be reached. Not
                // written out — see this function's own header note.
                FileIo.DAT_1f8000d0 = 0x280;
            }
            else
            {
                DAT_8008d144 = (ushort)(((short)PsxRam.ReadU16(iVar15s + 0x11e)
                    + (ushort)Scratchpad.DAT_1f800086 - 0x800) & 0xfff);
                if (0x800 < DAT_8008d144)
                {
                    DAT_8008d144 = (ushort)(DAT_8008d144 ^ 0xf000);
                }

                DAT_8008d12c = (int)((uint)(ushort)Scratchpad.DAT_1f800086 << 0x10) >> 0xc;
                int iVar15b = DAT_8008d12c - (short)DAT_8008d144;
                if (iVar15b < 0)
                {
                    iVar15b = iVar15b + 0xf;
                }

                Scratchpad.DAT_1f800086 = (short)(ushort)(iVar15b >> 4);

                iVar15b = 0x200 - (FileIo.DAT_1f8000d0 & 0xffff);
                DAT_8008d130 = (iVar15b * 0x10000 >> 0x10) + FileIo.DAT_1f8000d0 * 0x10;
                DAT_8008d148 = (short)iVar15b;
                iVar15b = DAT_8008d130;
                if (DAT_8008d130 < 0)
                {
                    iVar15b = DAT_8008d130 + 0xf;
                }

                FileIo.DAT_1f8000d0 = iVar15b >> 4;
            }

            Scratchpad.DAT_1f800084 = 0xc0;
            DAT_8008d124 = 0x1000;
            DAT_8008d128 = 0x1000;
            DAT_8008d146 = 0x200;
            return;
        }

        // 0x8002789C — pick the slot to follow. Normally ctx+0x1A, the published cursor; but while
        // CtxFlags bit 28 is up, the first slot whose record carries 0x1000 (one that has just gone
        // down) wins instead. The loop's own exit assignment is the original's: it sets uVar17 back
        // to the cursor on the iteration that does NOT break, so falling out of the loop leaves the
        // cursor and breaking out leaves the loop index.
        uint uVar11 = 0;
        int iVar15 = ctx;
        uint uVar17 = uVar16;
        if (((uint)PsxRam.ReadI32(ctx + BattleState.CtxFlags) & 0x10000000) != 0)
        {
            do
            {
                uVar17 = uVar11;
                if ((PsxRam.ReadU16(iVar15 + BattleState.CtxSlotRecords) & 0x1000) != 0)
                {
                    break;
                }

                uVar11 = uVar11 + 1;
                iVar15 = iVar15 + BattleState.CtxSlotRecordStride;
                uVar17 = uVar16;
            } while (uVar11 < 0xc);
        }

        uint puVar8 = (uint)PsxRam.ReadI32(ctx + BattleState.CtxFlags) & 0x80008000;

        // 0x800278F8 — THE OPENING POSE, held while both bit 31 and bit 15 are up. It also seeds the
        // fly-in below: 0x50 ticks, starting high, far and wide.
        if (puVar8 == 0x80008000)
        {
            Scratchpad.DAT_1f800084 = 0x100;
            Scratchpad.DAT_1f800086 = (short)(Scratchpad.DAT_1f800086 + 6);
            Scratchpad.DAT_1f800088 = 0;
            Scratchpad.DAT_1f8000c4 = 0;
            Scratchpad.DAT_1f8000c8 = 0x160;
            Scratchpad.DAT_1f8000cc = 0;
            FileIo.DAT_1f8000d0 = 0x620;
            DAT_8008d154 = 0x50;
            DAT_8008d156 = 0x620;
            DAT_8008d158 = 0x160;
            DAT_8008d15a = 0x100;
            return;
        }

        short sVar4 = (short)uVar17;

        // 0x8002796C — THE FLY-IN, run while bit 31 is up and bit 15 has been cleared by
        // RunBattleRound's legality-sweep arm. It eases the eye down and in on every tick, and on the
        // LAST tick it does the two things the whole battle waits for: clears CtxFlags bit 31 and
        // clears every fighter's +0x138 bit 25. It also sets +0x22C bit 0 on every fighter, which is
        // what the CPU controller reads as "the round has begun".
        if (puVar8 == 0x80000000)
        {
            if (0x10 < (short)DAT_8008d154)
            {
                Scratchpad.DAT_1f8000c8 = DAT_8008d158;
                FileIo.DAT_1f8000d0 = DAT_8008d156;
                Scratchpad.DAT_1f8000c4 = 0;
                Scratchpad.DAT_1f8000cc = 0;
                Scratchpad.DAT_1f800088 = 0;
                DAT_8008d158 = (short)(Scratchpad.DAT_1f8000c8 - 9);
                Scratchpad.DAT_1f800084 = (short)DAT_8008d15a;
                if ((int)((Scratchpad.DAT_1f8000c8 - 9) * 0x10000) >> 0x10 < -0xc0)
                {
                    DAT_8008d158 = -0xc0;
                }

                DAT_8008d15a = (ushort)(DAT_8008d15a - 1);
                if ((short)DAT_8008d15a < 0xd0)
                {
                    DAT_8008d15a = 0xd0;
                }

                DAT_8008d156 = (short)(DAT_8008d156 - 4);
            }

            ushort uVar5f = (ushort)(Scratchpad.DAT_1f800086 + 0x20);

            // THE COMPARE IS UNSIGNED, and getting that wrong was a real defect this file shipped
            // once. The image at 0x80027A18..0x80027A24 is
            //     andi  v0,a0,0xfff ; addiu v0,v0,-0x7f0 ; sltiu v0,v0,0x21 ; bne v0,zero,<skip>
            // -- `sltiu`, so the block runs whenever `(angle & 0xFFF) - 0x7F0` is 0x21 or more AS AN
            // UNSIGNED value, which includes every angle below 0x7F0 (the subtraction wraps). C#
            // promotes `ushort & int` to `int`, so the natural spelling is SIGNED and skips the
            // whole lower half of the circle. The cast is what makes it the original's test, and
            // this block is the one that decides which frame the fly-in ends on -- and therefore
            // which frame the round starts on.
            if (0x20 < (uint)((((ushort)Scratchpad.DAT_1f800086) & 0xfff) - 0x7f0))
            {
                Scratchpad.DAT_1f800086 = (short)uVar5f;
                if ((short)DAT_8008d154 < 0x11)
                {
                    DAT_8008d154 = (ushort)(DAT_8008d154 + 1);
                }
            }

            if (DAT_8008d154 != 1)
            {
                DAT_8008d154 = (ushort)(DAT_8008d154 - 1);
                return;
            }

            uVar16 = 0;
            DAT_8008d154 = (ushort)(DAT_8008d154 - 1);

            // THE CLEAR OF CtxFlags BIT 31 — `and v0,v0,a0` at 0x80027A7C and `sw v0,0x10(v1)` at
            // 0x80027A80, with the 0x7FFFFFFF mask built at 0x80027A64/0x80027A70. Nothing else in
            // the overlay does this.
            PsxRam.WriteI32(ctx + BattleState.CtxFlags,
                (int)((uint)PsxRam.ReadI32(ctx + BattleState.CtxFlags) & 0x7fffffff));

            do
            {
                iVar15 = PsxRam.ReadI32((int)(uVar16 * 4) + ctx + BattleState.CtxFighterSlots);
                uVar16 = uVar16 + 1;
                if (iVar15 != 0)
                {
                    iVar15 = PsxRam.ReadI32(iVar15 + 8);
                    PsxRam.WriteU8(iVar15 + 0x22c, (byte)(PsxRam.ReadU8(iVar15 + 0x22c) | 1));
                }
            } while (uVar16 < 0xc);

            uVar16 = 0;
            iVar15 = ctx;
            do
            {
                // THE CLEAR OF EVERY FIGHTER'S +0x138 BIT 25. Two separate twelve-slot walks, not
                // one: the first sets +0x22C bit 0 and the second clears this bit, and the original
                // really does walk the table twice.
                int piVar1 = PsxRam.ReadI32(iVar15 + BattleState.CtxFighterSlots);
                iVar15 = iVar15 + 4;
                if (piVar1 != 0)
                {
                    int iVar6b = PsxRam.ReadI32(piVar1 + 8);
                    PsxRam.WriteI32(iVar6b + 0x138,
                        (int)((uint)PsxRam.ReadI32(iVar6b + 0x138) & 0xfdffffff));
                }

                uVar16 = uVar16 + 1;
            } while (uVar16 < 0xc);

            return;
        }

        // 0x80027B84 — THE LIVE CAMERA.
        iVar15 = PsxRam.ReadI32(sVar4 * 4 + ctx + BattleState.CtxFighterSlots);
        if (iVar15 == 0)
        {
            return;
        }

        iVar15 = PsxRam.ReadI32(iVar15 + 8);
        DAT_8008d114 = (short)(DAT_8008d11c - (short)PsxRam.ReadU16(iVar15 + 0x114));
        DAT_8008d116 = (short)(DAT_8008d11e - (short)PsxRam.ReadU16(iVar15 + 0x116));
        DAT_8008d118 = (short)(DAT_8008d120 - (short)PsxRam.ReadU16(iVar15 + 0x118));

        // The other fighter: this one's own task node when it has one, otherwise the OPPOSING team's
        // acting-slot cursor — team B's for a slot below 6, team A's for one at or above it.
        int iVar6;
        if (PsxRam.ReadI32(iVar15 + BattleState.FighterTaskNode) == 0)
        {
            short vectorXs;
            if (sVar4 < 6)
            {
                vectorXs = (short)PsxRam.ReadU16(ctx + BattleState.CtxActingSlotTeamB);
            }
            else
            {
                vectorXs = (short)PsxRam.ReadU16(ctx + BattleState.CtxActingSlotTeamA);
            }

            iVar6 = PsxRam.ReadI32(
                PsxRam.ReadI32(vectorXs * 4 + ctx + BattleState.CtxFighterSlots) + 8);
        }
        else
        {
            iVar6 = PsxRam.ReadI32(PsxRam.ReadI32(iVar15 + BattleState.FighterTaskNode) + 8);
        }

        ushort uVar5 = (ushort)AnimCmdMesh.DistanceBetweenPositions(iVar15 + 0x114, iVar6 + 0x114);
        AnimCmdMesh.ComputeYawPitchToTarget(iVar15 + 0x114, iVar6 + 0x114, Local108Address);
        PsxRam.WriteU16(Local108Address + 2, (ushort)(PsxRam.ReadU16(Local108Address + 2) & 0xfff));

        sbyte cVar2 = (sbyte)PsxRam.ReadU8(iVar15 + 0x16a);
        ushort uVar10 = (ushort)(((short)PsxRam.ReadU16(iVar15 + 0x11e)
            + (ushort)Scratchpad.DAT_1f800086) & 0xfff);

        // 0x80027C20 — the mode ladder, driven by the distance between the two fighters and by
        // DAT_8008D142's own countdown. Below 0x2C1 apart the mode is forced; above it, the mode
        // steps one place towards its neighbour each time the counter reaches zero.
        if ((short)uVar5 < 0x2c1)
        {
            if (((uint)PsxRam.ReadI32(iVar15 + 0x138) & 0xe8) == 0
                || ((uint)PsxRam.ReadI32(iVar15 + 0x134) & 0x20000000) == 0)
            {
                DAT_8008d124 = 0x800;
            }
            else
            {
                DAT_8008d124 = 1;
            }
        }
        else if ((DAT_8008d124 & 0x7c0) == 0)
        {
            if (uVar10 < 0x800)
            {
                uVar16 = 0x100;
                if (0x100 < uVar10)
                {
                    uVar16 = 0x80;
                    if (0x2ff < uVar10)
                    {
                        uVar16 = 0x40;
                    }
                }
            }
            else
            {
                uVar16 = 0x100;
                if (0x900 < uVar10)
                {
                    uVar16 = 0x200;
                    if (0xaff < uVar10)
                    {
                        uVar16 = 0x400;
                    }
                }
            }

            DAT_8008d124 = (int)uVar16;
            DAT_8008d142 = 0x3c;
        }
        else
        {
            int iVar12s = DAT_8008d142;
            if (iVar12s < 1)
            {
                DAT_8008d142 = (short)(iVar12s + 1);
                if (((uint)(iVar12s + 1) & 0xffff) == 0)
                {
                    if (DAT_8008d124 == 0x100)
                    {
                        DAT_8008d124 = 0x80;
                        DAT_8008d142 = -0x3c;
                    }
                    else if (DAT_8008d124 < 0x101)
                    {
                        if (DAT_8008d124 == 0x40)
                        {
                            DAT_8008d124 = 0x80;
                            DAT_8008d142 = 0x3c;
                        }
                        else if (DAT_8008d124 == 0x80)
                        {
                            DAT_8008d124 = 0x40;
                            DAT_8008d142 = -0x3c;
                        }
                    }
                    else if (DAT_8008d124 == 0x200)
                    {
                        DAT_8008d124 = 0x100;
                        DAT_8008d142 = -0x3c;
                    }
                    else if (DAT_8008d124 == 0x400)
                    {
                        DAT_8008d124 = 0x200;
                        DAT_8008d142 = -0x3c;
                    }
                }
            }
            else
            {
                DAT_8008d142 = (short)(iVar12s - 1);
                if (((uint)(iVar12s - 1) & 0xffff) == 0)
                {
                    if (DAT_8008d124 == 0x100)
                    {
                        DAT_8008d124 = 0x200;
                        DAT_8008d142 = 0x3c;
                    }
                    else if (DAT_8008d124 < 0x101)
                    {
                        if (DAT_8008d124 == 0x40)
                        {
                            DAT_8008d124 = 0x80;
                            DAT_8008d142 = 0x3c;
                        }
                        else if (DAT_8008d124 == 0x80)
                        {
                            DAT_8008d124 = 0x100;
                            DAT_8008d142 = 0x3c;
                        }
                    }
                    else if (DAT_8008d124 == 0x200)
                    {
                        DAT_8008d124 = 0x400;
                        DAT_8008d142 = 0x3c;
                    }
                    else if (DAT_8008d124 == 0x400)
                    {
                        DAT_8008d124 = 0x200;
                        DAT_8008d142 = -0x3c;
                    }
                }
            }
        }

        // 0x80027E14 — three overrides, in the original's order. A slot change forces the
        // interpolated cut; a handover mode other than 1 forces 0x8000; CtxFlags bit 28 forces
        // 0x10000.
        uVar16 = 0x4000;
        if (((uint)PsxRam.ReadI32(ctx + BattleState.CtxFlags) & 0x2000) == 0)
        {
            uVar16 = (uint)DAT_8008d124;
            if (sVar4 != DAT_8008d150)
            {
                DAT_8008d142 = 0x10;
                uVar16 = 0x2000;
            }
        }

        DAT_8008d124 = (int)uVar16;
        if (SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] != 1)
        {
            DAT_8008d124 = 0x8000;
        }

        if (((uint)PsxRam.ReadI32(ctx + BattleState.CtxFlags) & 0x10000000) != 0)
        {
            DAT_8008d124 = 0x10000;
        }

        int iVar12 = ctx + sVar4 * BattleState.CtxSlotRecordStride;
        ushort uVar14 = 0x800;
        if ((PsxRam.ReadU16(iVar12 + BattleState.CtxSlotRecords) & 0x1200) != 0)
        {
            DAT_8008d10c = PsxRam.ReadU16(iVar15 + 0x114);
            DAT_8008d110 = PsxRam.ReadU16(iVar15 + 0x118);
            DAT_8008d10e = (ushort)((short)PsxRam.ReadU16(iVar15 + 0x116) + 0x60);
            if ((PsxRam.ReadU16(iVar12 + BattleState.CtxSlotRecords) & 0x1000) != 0)
            {
                DAT_8008d10e = (ushort)((short)PsxRam.ReadU16(iVar15 + 0x116) + 0x40);
            }
        }

        DAT_8008d146 = 0x200;
        Scratchpad.DAT_1f800084 = 0xc0;

        // See the DEVIATION on this function: the original reads register s4 here on one path
        // without ever writing it.
        uint unaff_s4 = 0;
        short vectorX = 0;
        bool bVar3;
        int iVar9;
        int iVar13;
        int iVar7;

        if (DAT_8008d124 == 0x100)
        {
            uVar14 = 0;
        }
        else if (0x100 < DAT_8008d124)
        {
            if (DAT_8008d124 == 0x1000)
            {
                unaff_s4 = 0;
            }
            else if (0x1000 < DAT_8008d124)
            {
                if (DAT_8008d124 == 0x4000)
                {
                    unaff_s4 = 1;
                    uVar14 = 0xc00;
                    if (uVar10 < 0x801)
                    {
                        uVar14 = 0x400;
                    }

                    Scratchpad.DAT_1f8000c4 = (short)DAT_8008d10c;
                    DAT_8008d146 = 0x300;
                    Scratchpad.DAT_1f800084 = 0x100;
                    Scratchpad.DAT_1f8000cc = (short)DAT_8008d110;
                    Scratchpad.DAT_1f8000c8 = -0x20 - (short)DAT_8008d10e;
                }
                else if (DAT_8008d124 < 0x4001)
                {
                    if (DAT_8008d124 == 0x2000)
                    {
                        // THE INTERPOLATED CUT. DAT_8008D142 is the divisor and it counts down, so
                        // each frame moves one nth of the remaining distance. The two `trap`
                        // instructions are the compiler's own divide-by-zero and MIN/-1 guards,
                        // emitted AFTER the divide; kept in that order and as traps, the same way
                        // BattleManager.cs keeps its own pair.
                        int iVar6b = DAT_8008d142;
                        iVar12 = DAT_8008d10c - (Scratchpad.DAT_1f8000c4 & 0xffff);
                        DAT_8008d134 = (iVar12 * 0x10000 >> 0x10) + iVar6b * Scratchpad.DAT_1f8000c4;
                        Scratchpad.DAT_1f8000c4 = SafeDivide(DAT_8008d134, iVar6b);

                        iVar9 = -(Scratchpad.DAT_1f8000c8 & 0xffff) - DAT_8008d10e;
                        DAT_8008d138 = (iVar9 * 0x10000 >> 0x10) + iVar6b * Scratchpad.DAT_1f8000c8;
                        Scratchpad.DAT_1f8000c8 = SafeDivide(DAT_8008d138, iVar6b);

                        iVar13 = DAT_8008d110 - (Scratchpad.DAT_1f8000cc & 0xffff);
                        DAT_8008d13c = (iVar13 * 0x10000 >> 0x10) + iVar6b * Scratchpad.DAT_1f8000cc;
                        Scratchpad.DAT_1f8000cc = SafeDivide(DAT_8008d13c, iVar6b);

                        DAT_8008d14a = (short)iVar12;
                        DAT_8008d14c = (short)iVar9;
                        DAT_8008d14e = (short)iVar13;
                        DAT_8008d142 = (short)(iVar6b - 1);
                        unaff_s4 = 0;
                        if (((uint)(iVar6b - 1) & 0xffff) == 0)
                        {
                            DAT_8008d124 = 0;
                        }
                    }
                }
                else if (DAT_8008d124 == 0x8000)
                {
                    // THE WIDE SHOT. Fit every marked, acting or targeted slot into one box, put the
                    // eye at its centre and pull the projection back by the box's own size.
                    short local_100 = (short)PsxRam.ReadU16(iVar15 + 0x114);
                    uVar16 = 0;
                    short local_fe = (short)PsxRam.ReadU16(iVar15 + 0x116);
                    short local_fc = (short)PsxRam.ReadU16(iVar15 + 0x118);
                    int iVar6c = ctx;
                    iVar12 = ctx;
                    short local_f8 = local_100;
                    short local_f6 = local_fe;
                    short local_f4 = local_fc;
                    do
                    {
                        if (PsxRam.ReadI32(iVar6c + BattleState.CtxFighterSlots) != 0
                            && (PsxRam.ReadU16(iVar12 + BattleState.CtxSlotRecords) & 0x200) != 0
                            && (uVar16 == (uint)(short)PsxRam.ReadU16(ctx + BattleState.CtxActingSlotTeamA)
                                || uVar16 == (uint)(short)PsxRam.ReadU16(ctx + BattleState.CtxActingSlotTeamB)
                                || uVar16 == (uint)(short)PsxRam.ReadU16(
                                    ctx + (short)PsxRam.ReadU16(ctx + BattleState.CtxActingSlotTeamA)
                                        * BattleState.CtxSlotRecordStride + BattleState.CtxTargetIndex)
                                || uVar16 == (uint)(short)PsxRam.ReadU16(
                                    ctx + (short)PsxRam.ReadU16(ctx + BattleState.CtxActingSlotTeamB)
                                        * BattleState.CtxSlotRecordStride + BattleState.CtxTargetIndex)))
                        {
                            int iVar9b = PsxRam.ReadI32(
                                PsxRam.ReadI32(iVar6c + BattleState.CtxFighterSlots) + 8);
                            if ((short)PsxRam.ReadU16(iVar9b + 0x114) < local_100)
                            {
                                local_100 = (short)PsxRam.ReadU16(iVar9b + 0x114);
                            }

                            if ((short)PsxRam.ReadU16(iVar9b + 0x116) < local_fe)
                            {
                                local_fe = (short)PsxRam.ReadU16(iVar9b + 0x116);
                            }

                            if ((short)PsxRam.ReadU16(iVar9b + 0x118) < local_fc)
                            {
                                local_fc = (short)PsxRam.ReadU16(iVar9b + 0x118);
                            }

                            if (local_f8 < (short)PsxRam.ReadU16(iVar9b + 0x114))
                            {
                                local_f8 = (short)PsxRam.ReadU16(iVar9b + 0x114);
                            }

                            if (local_f6 < (short)PsxRam.ReadU16(iVar9b + 0x116))
                            {
                                local_f6 = (short)PsxRam.ReadU16(iVar9b + 0x116);
                            }

                            if (local_f4 < (short)PsxRam.ReadU16(iVar9b + 0x118))
                            {
                                local_f4 = (short)PsxRam.ReadU16(iVar9b + 0x118);
                            }
                        }

                        iVar6c = iVar6c + 4;
                        uVar16 = uVar16 + 1;
                        iVar12 = iVar12 + BattleState.CtxSlotRecordStride;
                    } while (uVar16 < 0xc);

                    // The two triples and the two four-halfword arrays go into the frame region,
                    // because DistanceBetweenPositions reads them through a pointer.
                    PsxRam.WriteU16(Local100Address, (ushort)local_100);
                    PsxRam.WriteU16(Local100Address + 2, (ushort)local_fe);
                    PsxRam.WriteU16(Local100Address + 4, (ushort)local_fc);
                    PsxRam.WriteU16(LocalF8Address, (ushort)local_f8);
                    PsxRam.WriteU16(LocalF8Address + 2, (ushort)local_f6);
                    PsxRam.WriteU16(LocalF8Address + 4, (ushort)local_f4);

                    PsxRam.WriteU16(LocalF0Address + 2, 0);
                    PsxRam.WriteU16(LocalE8Address + 2, 0);
                    int vectorVx = (local_100 - local_f8) / 2;
                    PsxRam.WriteU16(LocalF0Address, (ushort)local_100);
                    PsxRam.WriteU16(LocalE8Address, (ushort)local_f8);
                    PsxRam.WriteU16(LocalE8Address + 4, (ushort)local_f4);
                    int vectorVy = (local_fe - local_f6) / 2;
                    int vectorVz = (local_fc - local_f4) / 2;
                    PsxRam.WriteU16(LocalF0Address + 4, (ushort)local_fc);
                    PsxRam.WriteI32(VectorAddress, vectorVx);
                    PsxRam.WriteI32(VectorAddress + 4, vectorVy);
                    PsxRam.WriteI32(VectorAddress + 8, vectorVz);

                    uVar16 = (uint)AnimCmdMesh.DistanceBetweenPositions(Local100Address, LocalF8Address);
                    iVar12 = AnimCmdMesh.DistanceBetweenPositions(LocalF0Address, LocalE8Address);
                    unaff_s4 = 4;
                    iVar9 = (iVar12 << 0x10) >> 0x10;
                    int iVar6d = vectorVy;
                    if (vectorVy < 0)
                    {
                        iVar6d = -vectorVy;
                    }

                    iVar13 = iVar6d;
                    if (iVar6d < 0)
                    {
                        iVar13 = iVar6d + 7;
                    }

                    FileIo.DAT_1f8000d0 = iVar9 * 3 + iVar6d * 0x10 + 0x240 + (iVar13 >> 3);
                    if (0x1c00 < FileIo.DAT_1f8000d0)
                    {
                        FileIo.DAT_1f8000d0 = 0x1c00;
                    }

                    DAT_8008d10c = (ushort)(local_100 - (short)vectorVx);
                    iVar13 = iVar9 - ((iVar12 << 0x10) >> 0x1f);
                    iVar12 = iVar9;
                    if (iVar9 < 0)
                    {
                        iVar12 = iVar9 + 3;
                    }

                    if (iVar9 < 0)
                    {
                        iVar9 = iVar9 + 7;
                    }

                    vectorX = (short)iVar6d;
                    iVar7 = iVar6d;
                    if (iVar6d < 0)
                    {
                        iVar7 = iVar6d + 3;
                    }

                    if (iVar6d < 0)
                    {
                        iVar6d = iVar6d + 0x1f;
                    }

                    DAT_8008d10e = (ushort)((local_fe - (short)vectorVy)
                        + (((((short)(iVar13 >> 1) - (short)(iVar12 >> 2)) + (short)(iVar9 >> 3)
                             + vectorX * 2) - (short)(iVar7 >> 2)) - (short)(iVar6d >> 5)));

                    if ((short)uVar16 < 0x100)
                    {
                        int iVar6e = -(int)(uVar16 & 0xff) + 0x100;
                        if (iVar6e < 0)
                        {
                            iVar6e = -(int)(uVar16 & 0xff) + 0x103;
                        }

                        DAT_8008d10e = (ushort)(DAT_8008d10e + (short)(iVar6e >> 2));
                    }

                    DAT_8008d110 = (ushort)(local_fc - (short)vectorVz);
                }
                else if (DAT_8008d124 == 0x10000)
                {
                    Scratchpad.DAT_1f8000c4 = (short)DAT_8008d10c;
                    Scratchpad.DAT_1f8000cc = (short)DAT_8008d110;
                    unaff_s4 = 0;
                    Scratchpad.DAT_1f8000c8 = -(short)DAT_8008d10e;
                }
            }
            else if (DAT_8008d124 == 0x400)
            {
                uVar14 = 0xc00;
            }
            else if (DAT_8008d124 < 0x401)
            {
                if (DAT_8008d124 == 0x200)
                {
                    uVar14 = 0xe00;
                }
            }
            else if (DAT_8008d124 == 0x800)
            {
                // 0x8002822C — THE CLOSE SHOT, the one the game spends most of a fight in. It runs
                // FUN_80047550 to turn the angle triple plus a shake amplitude into an offset, then
                // pulls the eye towards the midpoint between the two fighters.
                if (cVar2 != 0x1c)
                {
                    bVar3 = uVar10 < 0x801;
                    if (((uint)PsxRam.ReadI32(iVar15 + 0x138) & 0x10) == 0)
                    {
                        uVar10 = 0xc00;
                        if (bVar3)
                        {
                            uVar10 = 0x400;
                        }
                    }
                }

                unaff_s4 = 5;
                if ((sbyte)PsxRam.ReadU8(iVar15 + 0x16a) != 2)
                {
                    if ((sbyte)PsxRam.ReadU8(iVar15 + 0x16a) == 10)
                    {
                        DAT_8008d152 = (short)(DAT_8008d152 + 1);
                        if (0x10 < DAT_8008d152)
                        {
                            DAT_8008d152 = 0x10;
                        }

                        vectorX = (short)(((short)uVar5 * (DAT_8008d152 * 3) * 0x80) / 0x2c00);
                    }
                    else
                    {
                        DAT_8008d152 = 1;
                        vectorX = 0;
                    }
                }
                else
                {
                    unaff_s4 = 3;
                    DAT_8008d152 = (short)(DAT_8008d152 + 1);
                    if (0x10 < DAT_8008d152)
                    {
                        DAT_8008d152 = 0x10;
                    }

                    vectorX = (short)(((short)uVar5 * (int)DAT_8008d152 * 0x80) / 0x2c00);
                }

                AnimCmdControl.FUN_80047550(Local108Address, VectorAddress, 0, 0, vectorX);

                iVar13 = (short)DAT_8008d10e;
                int vx = ((((short)DAT_8008d10c - (short)PsxRam.ReadU16(iVar6 + 0x114))
                    + PsxRam.ReadI32(VectorAddress)) / 2);
                PsxRam.WriteI32(VectorAddress, vx);
                iVar12 = iVar13 - (short)PsxRam.ReadU16(iVar6 + 0x116);
                iVar9 = iVar12 - (iVar12 >> 0x1f);
                int vy = iVar12 / 2;
                PsxRam.WriteI32(VectorAddress + 4, vy);
                vectorX = (short)(ushort)FileIo.DAT_1f8000d0;
                iVar12 = vy;
                if (vy < 0)
                {
                    iVar12 = -vy;
                }

                iVar7 = (int)((uint)uVar5 << 0x10) >> 0x10;
                FileIo.DAT_1f8000d0 =
                    iVar7 + ((iVar7 - ((int)((uint)uVar5 << 0x10) >> 0x1f)) >> 1) + iVar12 + 0x1b0;
                int vz = ((((short)DAT_8008d110 - (short)PsxRam.ReadU16(iVar6 + 0x118))
                    + PsxRam.ReadI32(VectorAddress + 8)) / 2);
                PsxRam.WriteI32(VectorAddress + 8, vz);
                DAT_8008d146 = (ushort)FileIo.DAT_1f8000d0;

                vy = PsxRam.ReadI32(VectorAddress + 4);
                if (vy < 1)
                {
                    if (iVar13 < 0x100)
                    {
                        if ((short)(ushort)FileIo.DAT_1f8000d0 < 0x240)
                        {
                            int iVar6f = (short)vy;
                            iVar12 = vy + -0x40;
                            if (0x40 < iVar6f)
                            {
                                iVar6f = 0x40;
                            }

                            iVar6f = iVar6f << 0x10;
                            PsxRam.WriteI32(VectorAddress + 4, iVar12 - (iVar6f >> 0x10));
                        }
                        else
                        {
                            int iVar6f = (short)vy;
                            iVar12 = vy + 0x80;
                            if (iVar6f < 0x41)
                            {
                                iVar6f = 0x40;
                            }

                            iVar6f = iVar6f << 0x10;
                            PsxRam.WriteI32(VectorAddress + 4, iVar12 - (iVar6f >> 0x10));
                        }
                    }
                }
                else
                {
                    vy = vy - (((vy - (iVar9 >> 0x1f)) >> 1) + (iVar9 >> 3));
                    PsxRam.WriteI32(VectorAddress + 4, vy);
                    if (iVar13 < 0x100)
                    {
                        if ((short)(ushort)FileIo.DAT_1f8000d0 < 0x240)
                        {
                            int iVar6f = (short)vy;
                            iVar12 = vy + 0x40;
                            if (0x40 < iVar6f)
                            {
                                iVar6f = 0x40;
                            }

                            iVar6f = iVar6f << 0x10;
                            PsxRam.WriteI32(VectorAddress + 4, iVar12 - (iVar6f >> 0x10));
                        }
                        else
                        {
                            int iVar6f = (short)vy << 0x10;
                            if (0x40 < (short)vy)
                            {
                                iVar6f = 0xc00000;
                            }

                            PsxRam.WriteI32(VectorAddress + 4, vy - (iVar6f >> 0x10));
                        }
                    }
                }

                if (cVar2 == 0x19)
                {
                    vy = PsxRam.ReadI32(VectorAddress + 4);
                    if (vy < 0)
                    {
                        PsxRam.WriteI32(VectorAddress + 4, vy * 3);
                    }
                    else
                    {
                        PsxRam.WriteI32(VectorAddress + 4, -vy);
                    }
                }

                uVar14 = uVar10;

                // Both arms write the same three stores; only the second also eases the projection.
                // Kept as two arms because that is what the image has.
                if (DAT_8008d128 == 0x800)
                {
                    DAT_8008d10c = (ushort)(DAT_8008d10c - (short)PsxRam.ReadI32(VectorAddress));
                    DAT_8008d10e = (ushort)((DAT_8008d10e + 0x30)
                        - (short)PsxRam.ReadI32(VectorAddress + 4));
                    DAT_8008d110 = (ushort)(DAT_8008d110 - (short)PsxRam.ReadI32(VectorAddress + 8));
                }
                else
                {
                    DAT_8008d10c = (ushort)(DAT_8008d10c - (short)PsxRam.ReadI32(VectorAddress));
                    DAT_8008d10e = (ushort)((DAT_8008d10e + 0x30)
                        - (short)PsxRam.ReadI32(VectorAddress + 4));
                    DAT_8008d110 = (ushort)(DAT_8008d110 - (short)PsxRam.ReadI32(VectorAddress + 8));
                    FileIo.DAT_1f8000d0 =
                        vectorX - (vectorX - (short)(ushort)FileIo.DAT_1f8000d0) / 2;
                }
            }
        }
        else if (DAT_8008d124 == 8)
        {
            uVar14 = 0xc00;
            unaff_s4 = 3;
        }
        else if (DAT_8008d124 < 9)
        {
            if (DAT_8008d124 == 2)
            {
                unaff_s4 = 3;
            }
            else if (DAT_8008d124 < 3)
            {
                if (DAT_8008d124 == 1)
                {
                    // 0x80027F1C — THE LOCK-ON. The two positions are flattened to the XZ plane
                    // before the distance is taken, and the eye's height chases the target's.
                    bVar3 = uVar10 < 0x801;
                    if (((uint)PsxRam.ReadI32(iVar15 + 0x138) & 0x10) == 0)
                    {
                        uVar10 = 0xc00;
                        if (bVar3)
                        {
                            uVar10 = 0x400;
                        }
                    }

                    PsxRam.WriteU16(LocalF0Address, PsxRam.ReadU16(iVar15 + 0x114));
                    PsxRam.WriteU16(LocalF0Address + 2, 0);
                    PsxRam.WriteU16(LocalF0Address + 4, PsxRam.ReadU16(iVar15 + 0x118));
                    PsxRam.WriteU16(LocalE8Address, PsxRam.ReadU16(iVar6 + 0x114));
                    PsxRam.WriteU16(LocalE8Address + 2, 0);
                    PsxRam.WriteU16(LocalE8Address + 4, PsxRam.ReadU16(iVar6 + 0x118));
                    vectorX = (short)AnimCmdMesh.DistanceBetweenPositions(LocalF0Address, LocalE8Address);

                    iVar9 = (short)DAT_8008d10e;
                    int vy1 = (iVar9 - (short)PsxRam.ReadU16(iVar6 + 0x116)) / 2;
                    PsxRam.WriteI32(VectorAddress + 4, vy1);
                    iVar12 = vy1;
                    if (vy1 < 0)
                    {
                        iVar12 = -vy1;
                    }

                    if (cVar2 == 0x2a)
                    {
                        bool skip = false;
                        ushort uVar5b;
                        if (iVar9 < (short)PsxRam.ReadU16(iVar6 + 0x116))
                        {
                            uVar5b = (ushort)(DAT_8008d10e + 0xc0);
                            if (0x1f < vectorX)
                            {
                                skip = true;
                            }
                        }
                        else
                        {
                            uVar5b = (ushort)(DAT_8008d10e - 0x80);
                            if (0xf < vectorX)
                            {
                                uVar5b = (ushort)(DAT_8008d10e - 0x20);
                            }
                        }

                        if (!skip)
                        {
                            DAT_8008d10e = uVar5b;
                        }
                    }
                    else if (0x10 < (short)iVar12)
                    {
                        ushort uVar5b = (ushort)(DAT_8008d10e - 0xc);
                        if ((short)PsxRam.ReadU16(iVar6 + 0x116) <= iVar9)
                        {
                            uVar5b = (ushort)(DAT_8008d10e - 0x12);
                        }

                        DAT_8008d10e = uVar5b;
                    }

                    unaff_s4 = 1;
                    uVar14 = uVar10;
                    if (DAT_8008d128 == 1)
                    {
                        Scratchpad.DAT_1f8000c4 = (short)DAT_8008d10c + DAT_8008d114;
                        Scratchpad.DAT_1f8000c8 = -((short)DAT_8008d10e + DAT_8008d116);
                        Scratchpad.DAT_1f8000cc = (short)DAT_8008d110 + DAT_8008d118;
                    }
                    else
                    {
                        DAT_8008d10c = (ushort)(DAT_8008d10c + DAT_8008d114);
                        DAT_8008d10e = (ushort)(DAT_8008d10e + DAT_8008d116);
                        DAT_8008d110 = (ushort)(DAT_8008d110 + DAT_8008d118);
                        unaff_s4 = 5;
                    }
                }
            }
            else if (DAT_8008d124 == 4)
            {
                uVar14 = 0x400;
                unaff_s4 = 3;
            }
        }
        else if (DAT_8008d124 == 0x20)
        {
            uVar14 = 0xe00;
            unaff_s4 = 3;
        }
        else if (0x20 < DAT_8008d124)
        {
            if (DAT_8008d124 == 0x40)
            {
                uVar14 = 0x400;
            }
            else if (DAT_8008d124 == 0x80)
            {
                uVar14 = 0x200;
            }
        }
        else if (DAT_8008d124 == 0x10)
        {
            uVar14 = 0x200;
            unaff_s4 = 3;
        }

        // 0x80028AC4 — LAB_80028AC4, the tail. Every arm above reaches it.
        if ((DAT_8008d124 & 0x7c0) != 0)
        {
            unaff_s4 = 5;
            if ((sbyte)PsxRam.ReadU8(iVar15 + 0x16a) == 2)
            {
                DAT_8008d152 = (short)(DAT_8008d152 + 1);
                if (0x10 < DAT_8008d152)
                {
                    DAT_8008d152 = 0x10;
                }

                vectorX = (short)(DAT_8008d152 * 10);
            }
            else if ((sbyte)PsxRam.ReadU8(iVar15 + 0x16a) == 10)
            {
                DAT_8008d152 = (short)(DAT_8008d152 + 1);
                if (0x10 < DAT_8008d152)
                {
                    DAT_8008d152 = 0x10;
                }

                vectorX = (short)(DAT_8008d152 << 1);
            }
            else
            {
                DAT_8008d152 = 1;
                vectorX = 0;
            }

            PsxRam.WriteU16(Local108Address, 0);
            PsxRam.WriteU16(Local108Address + 4, 0);
            AnimCmdControl.FUN_80047550(Local108Address, VectorAddress, 0, 0, vectorX);
            DAT_8008d10c = (ushort)(DAT_8008d10c + (short)PsxRam.ReadI32(VectorAddress));
            DAT_8008d10e = (ushort)(DAT_8008d10e + (short)PsxRam.ReadI32(VectorAddress + 4));
            DAT_8008d110 = (ushort)(DAT_8008d110 + (short)PsxRam.ReadI32(VectorAddress + 8));
            if (-(short)DAT_8008d10e < Scratchpad.DAT_1f8000c8)
            {
                Scratchpad.DAT_1f8000c8 = -(short)DAT_8008d10e;
            }

            DAT_8008d146 = 0x280;
        }

        if (SharedHighRam.SHORT_ARRAY_801ff000[Dat801ff100ShortIndex] == 1)
        {
            int iVar6g = (short)DAT_8008d146;
            if (iVar6g < 0)
            {
                iVar6g = iVar6g + 7;
            }

            if (iVar6g >> 3 < (short)DAT_8008d10e)
            {
                DAT_8008d10e = (ushort)(iVar6g >> 3);
            }
        }

        if ((unaff_s4 & 1) != 0)
        {
            DAT_8008d144 = (ushort)(((short)PsxRam.ReadU16(iVar15 + 0x11e)
                + (ushort)Scratchpad.DAT_1f800086 - uVar14) & 0xfff);
            if (0x800 < DAT_8008d144)
            {
                DAT_8008d144 = (ushort)(DAT_8008d144 ^ 0xf000);
            }

            DAT_8008d12c = (int)((uint)(ushort)Scratchpad.DAT_1f800086 << 0x10) >> 0xe;
            int iVar6h = DAT_8008d12c - (short)DAT_8008d144;
            if (iVar6h < 0)
            {
                iVar6h = iVar6h + 3;
            }

            Scratchpad.DAT_1f800086 = (short)(ushort)(iVar6h >> 2);
        }

        if (0xdff < (short)DAT_8008d146)
        {
            DAT_8008d146 = 0xdff;
        }

        int iVar6i = DAT_8008d146 - (FileIo.DAT_1f8000d0 & 0xffff);
        iVar12 = iVar6i * 0x10000 >> 0x10;
        if (iVar12 < 0x10)
        {
            DAT_8008d130 = iVar12 + FileIo.DAT_1f8000d0 * 2;
            FileIo.DAT_1f8000d0 = DAT_8008d130 / 2;
        }
        else
        {
            DAT_8008d130 = iVar12 + FileIo.DAT_1f8000d0 * 8;
            iVar12 = DAT_8008d130;
            if (DAT_8008d130 < 0)
            {
                iVar12 = DAT_8008d130 + 7;
            }

            FileIo.DAT_1f8000d0 = iVar12 >> 3;
        }

        // 0x80028DEC — the per-axis ease. A small error is closed at a sixteenth per frame, a large
        // one at a half; mode 0x8000 (the wide shot) always takes the fast path because it sets bit
        // 2 of unaff_s4.
        if ((unaff_s4 & 6) != 0)
        {
            iVar12 = DAT_8008d10c - (Scratchpad.DAT_1f8000c4 & 0xffff);
            DAT_8008d14a = (short)iVar12;
            DAT_8008d14c = (short)(-(short)Scratchpad.DAT_1f8000c8 - DAT_8008d10e);
            DAT_8008d14e = (short)(DAT_8008d110 - (short)Scratchpad.DAT_1f8000cc);

            vectorX = DAT_8008d14a;
            if (iVar12 * 0x10000 < 1)
            {
                vectorX = (short)-DAT_8008d14a;
            }

            if (vectorX < 0x10 && (unaff_s4 & 4) == 0)
            {
                DAT_8008d134 = DAT_8008d14a + Scratchpad.DAT_1f8000c4 * 0x10;
                iVar12 = DAT_8008d134;
                if (DAT_8008d134 < 0)
                {
                    iVar12 = DAT_8008d134 + 0xf;
                }

                Scratchpad.DAT_1f8000c4 = iVar12 >> 4;
            }
            else
            {
                DAT_8008d134 = DAT_8008d14a + Scratchpad.DAT_1f8000c4 * 2;
                Scratchpad.DAT_1f8000c4 = DAT_8008d134 / 2;
            }

            vectorX = DAT_8008d14c;
            if (DAT_8008d14c < 1)
            {
                vectorX = (short)-DAT_8008d14c;
            }

            if (vectorX < 0x20 && (unaff_s4 & 4) == 0)
            {
                DAT_8008d138 = DAT_8008d14c + Scratchpad.DAT_1f8000c8 * 0x10;
                iVar12 = DAT_8008d138;
                if (DAT_8008d138 < 0)
                {
                    iVar12 = DAT_8008d138 + 0xf;
                }

                Scratchpad.DAT_1f8000c8 = iVar12 >> 4;
            }
            else
            {
                DAT_8008d138 = DAT_8008d14c + Scratchpad.DAT_1f8000c8 * 2;
                Scratchpad.DAT_1f8000c8 = DAT_8008d138 / 2;
            }

            vectorX = DAT_8008d14e;
            if (DAT_8008d14e < 1)
            {
                vectorX = (short)-DAT_8008d14e;
            }

            if (vectorX < 0x11 && (unaff_s4 & 4) == 0)
            {
                DAT_8008d13c = DAT_8008d14e + Scratchpad.DAT_1f8000cc * 0x10;
                iVar12 = DAT_8008d13c;
                if (DAT_8008d13c < 0)
                {
                    iVar12 = DAT_8008d13c + 0xf;
                }

                Scratchpad.DAT_1f8000cc = iVar12 >> 4;
            }
            else
            {
                DAT_8008d13c = DAT_8008d14e + Scratchpad.DAT_1f8000cc * 2;
                Scratchpad.DAT_1f8000cc = DAT_8008d13c / 2;
            }
        }

        // 0x80028FA4 — the shake pair alternates sign every frame, and the two projection offsets
        // ride on it. `~x` and `-x` are the original's two arms, and they differ by one.
        // BattleManager.cs declares both as `ushort` and the original reads them back through a
        // SIGNED halfword load (`lh`), so every test below sign-extends first and every store
        // truncates back. That asymmetry is Ghidra's own and is kept rather than smoothed away.
        iVar9 = (short)BattleManager.DAT_8008d15c;
        iVar12 = (short)BattleManager.DAT_8008d15e;
        ushort uVar5t = BattleManager.DAT_8008d15c;
        if (iVar9 != 0)
        {
            uVar5t = (ushort)-(short)BattleManager.DAT_8008d15c;
            if (iVar9 < 0)
            {
                uVar5t = (ushort)~BattleManager.DAT_8008d15c;
            }
        }

        BattleManager.DAT_8008d15c = uVar5t;
        if (BattleManager.DAT_8008d15e != 0)
        {
            if ((short)BattleManager.DAT_8008d15e < 0)
            {
                BattleManager.DAT_8008d15e = (ushort)~BattleManager.DAT_8008d15e;
            }
            else
            {
                BattleManager.DAT_8008d15e = (ushort)-(short)BattleManager.DAT_8008d15e;
            }
        }

        DAT_8008d150 = sVar4;
        DAT_8008d148 = (short)iVar6i;
        DAT_8008d128 = DAT_8008d124;
        DAT_8008d120 = PsxRam.ReadU16(iVar15 + 0x118);
        DAT_8008d11e = PsxRam.ReadU16(iVar15 + 0x116);
        DAT_8008d11c = PsxRam.ReadU16(iVar15 + 0x114);
        Scratchpad.DAT_1f800124 = iVar9 + 0xa0;
        FileIo.DAT_1f800120 = iVar12 + 0xf0;
        Scratchpad.DAT_1f800088 = 0;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the compiler's divide-with-traps idiom, in one place instead of three.
    //
    // MIPS `div` has no exception, so the compiler emits the divide and THEN two guards: `break
    // 0x1C00` when the divisor is zero and `break 0x1800` for the MIN_VALUE / -1 pair. C# throws on
    // both instead of trapping. The order matters and is kept: the quotient is taken first, exactly
    // as the image does, so a caller that would have trapped still computes the same value first.
    // BattleManager.cs keeps its own copies of this pair inline; this file has three of them in one
    // expression each, which is why it is a helper here.
    private static int SafeDivide(int dividend, int divisor)
    {
        if (divisor == 0)
        {
            throw new System.DivideByZeroException(
                "RunBattleCameraTask: DAT_8008D142 reached zero as a divisor; the original traps here (break 0x1C00).");
        }

        if (divisor == -1 && dividend == int.MinValue)
        {
            throw new System.OverflowException(
                "RunBattleCameraTask: MIN_VALUE / -1; the original traps here (break 0x1800).");
        }

        return dividend / divisor;
    }
}
