using System;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE ANIMATION SCRIPT VM's INTERPRETER — the caller the fifty-one handlers of tranche 1 were
// missing. Until this file existed they were correct and unreachable.
//
// The machine is a threaded interpreter and its whole contract is three lines of the original:
//
//     uVar1 = *puVar5;
//     puVar2 = puVar5;
//     while (uVar1 != 0) {
//         puVar2 = (*(code *)(&g_animStreamDispatchTable)[*puVar2 & 0xff])(puVar2, iVar6 >> 0x10);
//         uVar1 = *puVar2;
//     }
//
// so: the OPCODE is the low byte of the command's first halfword, each handler RETURNS THE ADDRESS
// OF THE NEXT COMMAND, and a stream ends on a zero halfword. Sixteen streams are run per call, one
// per mesh slot.
//
// Because a handler's return value is an address the interpreter re-reads, the stream cannot be a
// copied ushort[]: it is PSX memory, read through PsxRam, exactly as the six handler families
// model it.
internal static class AnimVmInterpreter
{
    // GHIDRA: g_renderFlushFlag @ 0x801FAA60 (VS.EXE)
    // Armed by table_set and read here after every stream. AnimCmdMesh.cs names the same address
    // privately, and that duplication is deliberate rather than the defect tranche 1 had to fix:
    // this is a CONST ADDRESS, not a backing store. Two consts holding one number both resolve
    // through PsxRam into AnimVm's single region. Two byte[] would have been two storages.
    private const int g_renderFlushFlag = unchecked((int)0x801FAA60);

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original passes a four-halfword STACK local to AnimCmd_ChEffSet twice
    // (0x800367F0 and 0x8003695C). A handler takes a PSX address and reads it through PsxRam, so
    // the local needs one. This is a scratch address outside every modelled region — it stands for
    // the interpreter's own stack frame and nothing else. AnimCmdEffects.cs models its own such
    // local the same way at 0x807FFFE0; this one is distinct so the two can never alias.
    private const int Local30Address = unchecked((int)0x807FFFF0);

    private static readonly byte[] RAM_local30 = LibGpu.RamRegion(Local30Address, 8);

    // JUSTIFICATION: C# language bridge only
    // RELATION: g_animStreamDispatchTable @ 0x800822F4 is an array of fifty-one function pointers
    // indexed by opcode, and this is that array. The lambdas exist only because Ghidra recovered a
    // different parameter count for different handlers — the console calls all fifty-one with the
    // same two arguments, and the ones that ignore the second simply never read a1. Adapting the
    // arity here keeps every handler's own signature honest to what its body actually reads.
    //
    // Slots 0, 4 and 36 all hold 0x80037374, whose name in the image is `dummy`: three opcodes,
    // one body. Slot 50 is the fifty-first pointer and has no name entry — the name table stops at
    // index 49 — so it keeps its raw address for a name.
    private static readonly Func<int, int, int>[] g_animStreamDispatchTable =
    {
        /* 00 dummy        */ (s, i) => AnimCmdControl.AnimCmd_Dummy(),
        /* 01 nop_set      */ (s, i) => AnimCmdControl.AnimCmd_NopSet(s),
        /* 02 table_set    */ (s, i) => AnimCmdMesh.AnimCmd_RenderEntryGroup(s, i),
        /* 03 load_set     */ (s, i) => AnimCmdMesh.AnimCmd_LoadTexture(s, i),
        /* 04 dummy        */ (s, i) => AnimCmdControl.AnimCmd_Dummy(),
        /* 05 anm_set      */ (s, i) => AnimCmdEffects.AnimCmd_SetCharRenderState(s),
        /* 06 trans_set    */ (s, i) => AnimCmdTransform.AnimCmd_TransSet(s),
        /* 07 rotate_set   */ (s, i) => AnimCmdTransform.AnimCmd_RotateSet(s),
        /* 08 scale_set    */ (s, i) => AnimCmdTransform.AnimCmd_ScaleSet(s),
        /* 09 cul_set      */ (s, i) => AnimCmdMesh.AnimCmd_CulSet(s, i),
        /* 10 pri_set      */ (s, i) => AnimCmdAppearance.AnimCmd_AddPrimsToOT(s),
        /* 11 colrol_set   */ (s, i) => AnimCmdAppearance.AnimCmd_ColrolSet(s),
        /* 12 eye_set      */ (s, i) => AnimCmdEffects.AnimCmd_ApplyCharEffect(s),
        /* 13 tpclut_set   */ (s, i) => AnimCmdAppearance.AnimCmd_TpClutSet(s),
        /* 14 rgb_set      */ (s, i) => AnimCmdAppearance.AnimCmd_RgbSet(s),
        /* 15 cmp_set      */ (s, i) => AnimCmdControl.AnimCmd_CmpSet(s),
        /* 16 x_add_set    */ (s, i) => AnimCmdMesh.AnimCmd_XAddSet(s, i),
        /* 17 parts_link   */ (s, i) => AnimCmdControl.AnimCmd_PartsLink(s),
        /* 18 x_max_set    */ (s, i) => AnimCmdMesh.AnimCmd_XMaxSet(s, i),
        /* 19 rgb2_set     */ (s, i) => AnimCmdAppearance.AnimCmd_Rgb2Set(s),
        /* 20 utylty       */ (s, i) => AnimCmdControl.AnimCmd_Utility(s),
        /* 21 objint_get   */ (s, i) => AnimCmdControl.AnimCmd_ObjIntGet(s),
        /* 22 objlong_get  */ (s, i) => AnimCmdControl.AnimCmd_ObjLongGet(s),
        /* 23 bit_chk      */ (s, i) => AnimCmdControl.AnimCmd_BitChk(s, (ushort)i),
        /* 24 bit_set      */ (s, i) => AnimCmdControl.AnimCmd_BitSet(s),
        /* 25 end_set      */ (s, i) => AnimCmdControl.AnimCmd_EndSet(s, (ushort)i),
        /* 26 base_culX    */ (s, i) => AnimCmdTransform.AnimCmd_BaseCulX(s),
        /* 27 base_culY    */ (s, i) => AnimCmdTransform.AnimCmd_BaseCulY(s),
        /* 28 base_culZ    */ (s, i) => AnimCmdTransform.AnimCmd_BaseCulZ(s),
        /* 29 movexp_set   */ (s, i) => AnimCmdMesh.AnimCmd_MovexpSet(s, i),
        /* 30 dist_set     */ (s, i) => AnimCmdMesh.AnimCmd_DistSet(s, i),
        /* 31 move_set     */ (s, i) => AnimCmdMesh.AnimCmd_MoveSet(s, i),
        /* 32 uv0123_set   */ (s, i) => AnimCmdAppearance.AnimCmd_Uv0123Set(s),
        /* 33 eff_set      */ (s, i) => AnimCmdEffects.AnimCmd_EffSet(s),
        /* 34 att_set      */ (s, i) => AnimCmdEffects.AnimCmd_AttSet(s),
        /* 35 if_set       */ (s, i) => AnimCmdControl.AnimCmd_IfSet(s),
        /* 36 dummy        */ (s, i) => AnimCmdControl.AnimCmd_Dummy(),
        /* 37 xy0123_set   */ (s, i) => AnimCmdAppearance.AnimCmd_Xy0123Set(s),
        /* 38 ot_z_set     */ (s, i) => AnimCmdAppearance.AnimCmd_OtZSet(s),
        /* 39 ch_eff_set   */ (s, i) => AnimCmdEffects.AnimCmd_ChEffSet(s),
        /* 40 ch_dan_set   */ (s, i) => AnimCmdEffects.AnimCmd_ChDanSet(s),
        /* 41 hitz_set     */ (s, i) => AnimCmdEffects.AnimCmd_HitzSet(s),
        /* 42 auto_otz     */ (s, i) => AnimCmdAppearance.AnimCmd_AutoOtz(s),
        /* 43 auto_rgb     */ (s, i) => AnimCmdAppearance.AnimCmd_AutoRgb(s),
        /* 44 cheff_wait   */ (s, i) => AnimCmdEffects.AnimCmd_CheffWait(s),
        /* 45 chse_call    */ (s, i) => AnimCmdSound.AnimCmd_ChseCall(s),
        /* 46 chse_vol     */ (s, i) => AnimCmdSound.AnimCmd_ChseVol(s),
        /* 47 voice_call   */ (s, i) => AnimCmdSound.AnimCmd_VoiceCall(s),
        /* 48 atse_call    */ (s, i) => AnimCmdSound.AnimCmd_AtseCall(s),
        /* 49 base_culP    */ (s, i) => AnimCmdTransform.AnimCmd_BaseCulP(s),
        /* 50 (no name)    */ (s, i) => AnimCmdSound.FUN_8003ef04(s),
    };

    // GHIDRA: ExecuteAnimStreamBatch @ 0x80036768 (VS.EXE)
    // 764 bytes. It runs the sixteen mesh slots' command streams, in order, once per call.
    //
    // The slot walk is written with the original's own shift arithmetic rather than tidied into an
    // index: iVar6 is the slot number shifted left by 16, so `iVar6 >> 0xe` is the slot's byte
    // offset into g_meshStreamPtrBuffer (four bytes an entry) and `iVar6 >> 0x10` is the slot
    // number the handlers receive. Keeping the shifts keeps the sign behaviour the compiler chose.
    internal static void ExecuteAnimStreamBatch()
    {
        int iVar9 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        uint uVar3 = (uint)PsxRam.ReadI32(PsxRam.ReadI32(iVar9 + 0x18) + 0x138);
        if ((uVar3 & 0x8000000) != 0)
        {
            PsxRam.WriteI32(PsxRam.ReadI32(iVar9 + 0x18) + 0x138, unchecked((int)(uVar3 & 0xf7ffffff)));
        }

        short sVar8 = 0;
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            PsxRam.WriteU16(Local30Address, 0x8000);
            AnimCmdEffects.AnimCmd_ChEffSet(Local30Address);
        }

        int iVar7 = 0;
        int iVar6 = 0;

        do
        {
            int puVar5 = PsxRam.ReadI32(AnimVm.g_meshStreamPtrBuffer + (iVar6 >> 0xe));
            if (puVar5 != 0)
            {
                sVar8 = (short)(sVar8 + 1);
                ushort uVar1 = PsxRam.ReadU16(puVar5);
                int puVar2 = puVar5;
                while (uVar1 != 0)
                {
                    puVar2 = g_animStreamDispatchTable[PsxRam.ReadU16(puVar2) & 0xff](puVar2, iVar6 >> 0x10);
                    uVar1 = PsxRam.ReadU16(puVar2);
                }

                if (PsxRam.ReadU16(g_renderFlushFlag) != 0)
                {
                    PsxRam.WriteU16(g_renderFlushFlag, 0);
                    RunBatchTail(iVar9, sVar8);
                    return;
                }

                if ((AnimVm.DAT_800b305a & 1) == 0)
                {
                    int puVar4 = AnimVm.g_meshOffsetBuffer + (short)iVar7 * 2;
                    uVar1 = PsxRam.ReadU16(puVar4);
                    PsxRam.WriteU16(puVar4, (ushort)(uVar1 - 1));
                    if (uVar1 == 1)
                    {
                        if (PsxRam.ReadI32(AnimVm.g_meshStreamPtrBuffer + (short)iVar7 * 4) == 0)
                        {
                            puVar5 = 0;
                        }
                        else
                        {
                            // The stream carries its own repeat count in the halfword after the
                            // terminator, and the next stream starts two halfwords on.
                            puVar5 = puVar2 + 2 * 2;
                            PsxRam.WriteU16(puVar4, PsxRam.ReadU16(puVar2 + 1 * 2));
                        }
                    }
                }

                PsxRam.WriteI32(AnimVm.g_meshStreamPtrBuffer + ((iVar7 << 0x10) >> 0xe), puVar5);
            }

            iVar7 = iVar7 + 1;
            iVar6 = iVar7 * 0x10000;
            if (0xf < iVar7 * 0x10000 >> 0x10)
            {
                RunBatchTail(iVar9, sVar8);
                return;
            }
        }
        while (true);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original reaches this tail two ways — by falling out of the slot loop, and by a
    // `goto LAB_80036948` taken when g_renderFlushFlag is set mid-batch. C# has no goto into a
    // sibling scope, so the shared tail is a method called from both places. The control flow is
    // unchanged: same code, same two entries, same single exit.
    private static void RunBatchTail(int iVar9, short sVar8)
    {
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            PsxRam.WriteU16(Local30Address, 0x8000);
            AnimCmdEffects.AnimCmd_SetCharRenderState(Local30Address);
            StepVolumeRamp();
        }

        // JUSTIFICATION: C# language bridge only
        // RELATION: these two bytes live INSIDE BattleScene's RAM_800990c0 region, and naming the
        // array is what ARMS it -- LibGpu.RamRegion registers the bytes from BattleScene's static
        // initialiser, and PsxRam answers 0 for any address no region covers, silently. Reaching
        // through PsxRam by address here would work only if something had already touched
        // BattleScene, which is an ordering assumption rather than a guarantee. The offsets fold to
        // 8 and 0x14, and the form matches AnimCmdMesh's `p - AnimVm.DAT_801f2000` idiom.
        byte[] rec = BattleScene.RAM_800990c0;

        RollAndUploadClutRange(DAT_800990c0);
        rec[DAT_800990c8 - DAT_800990c0] = (byte)(rec[DAT_800990c8 - DAT_800990c0] - 1);
        RollAndUploadClutRange(DAT_800990cc);
        if ((rec[DAT_800990c8 - DAT_800990c0] & 1) != 0)
        {
            rec[DAT_800990d4 - DAT_800990c0] = (byte)(rec[DAT_800990d4 - DAT_800990c0] + 1);
        }

        if (((AnimVm.DAT_800b305a & 1) == 0) && (sVar8 == 0))
        {
            PsxRam.WriteU16(iVar9 + 0x78, 0);
            AnimCmdSound.FUN_8005fcec(0, 0);
            if ((PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
            {
                BattleManager.FUN_8005ee5c(0, 0, 0x30);
            }

            PsxRam.WriteU16(iVar9 + 0x76, 4);
        }
    }

    // =====================================================================================
    // NOT IN THIS TRANCHE
    // =====================================================================================
    // The tail also reaches into the battle and scene subsystems — tranche 2 — through
    // AnimCmdSound.FUN_8005fcec and BattleManager.FUN_8005ee5c / BattleManager.DAT_8008d320, each
    // BLOCKED in its own file rather than duplicated here. StepVolumeRamp and RollAndUploadClutRange below are
    // this tranche's own functions and are now closed; DAT_800990c0/cc/c8/d4 just below are plain
    // addresses into BattleScene's already-modelled RAM_800990c0 region, not stubs.

    // GHIDRA: DAT_800990c0 @ 0x800990C0, DAT_800990cc @ 0x800990CC (VS.EXE)
    private const int DAT_800990c0 = unchecked((int)0x800990C0);

    private const int DAT_800990cc = unchecked((int)0x800990CC);

    // GHIDRA: DAT_800990c8 @ 0x800990C8, DAT_800990d4 @ 0x800990D4 (VS.EXE)
    // ADDRESSES, NOT STORAGE -- and that distinction is the whole fix. Both were `private static`
    // C# scalars here, which made them a SECOND COPY of bytes BattleScene already models as one
    // region: RAM_800990c0 spans 0x800990C0..0x800990D7, so it covers both. Writes through the
    // scalars never reached the bytes RenderBattleScene3D initialises, and vice versa.
    //
    // The width was wrong too, and that one had teeth. DAT_800990c8 was an `int`. The image says
    // BYTE, twice over:
    //     0x8003698C  lbu v0,-0x6f38(v0)   (90 42 90 C8)   ; load byte unsigned
    //     0x80036998  sb  v0,-0x6f38(at)   (A0 22 90 C8)   ; store byte
    //     0x800369C4  lbu v0,-0x6f2c(v0)                   ; DAT_800990d4, also a byte
    // A 32-bit write at +0x08 would have clobbered +0x09, +0x0A and +0x0B, which BattleScene's own
    // field map documents as three separate bytes holding 2, 8 and 0. The decrement is the only
    // reason nothing had visibly broken: it read zeros it had written itself.
    private const int DAT_800990c8 = unchecked((int)0x800990C8);

    private const int DAT_800990d4 = unchecked((int)0x800990D4);

    // GHIDRA: StepVolumeRamp @ 0x8003ECFC (VS.EXE)
    // CLOSED. Steps the channel-volume ramp AnimCmdSound.AnimCmd_ChseVol (opcode 46) arms, once per
    // frame — RunBatchTail is its one call site, exactly the comment on AnimCmdSound.DAT_801fac40
    // already says. DAT_801fac40 is the signed slope (0 == no ramp running, and the function is a
    // no-op); otherwise the slope is added to the current volume (DAT_801fac42) to get a candidate,
    // the candidate is compared against the target (DAT_801fac43) — signed, both directions, per the
    // disassembly at 0x8003ed30..0x8003ed40 (`bgez`/`slt` on opposite operand orders for the
    // slope<0 and slope>=0 cases) — and on overshoot the slope is cleared and the candidate clamped
    // to the target. The (possibly clamped) volume is written back to DAT_801fac42 and pushed to the
    // driver through AnimCmdSound.FUN_8005fcec on channel DAT_801fac41, unconditionally, every call
    // where the ramp is active. FUN_8005fcec is itself BLOCKED (sound-driver module, see
    // AnimCmdSound.cs); its argument here is exact regardless.
    private static void StepVolumeRamp()
    {
        int iVar2 = AnimCmdSound.DAT_801fac40;
        if (iVar2 != 0)
        {
            uint uVar4 = AnimCmdSound.DAT_801fac43;
            uint uVar3 = (uint)(AnimCmdSound.DAT_801fac42 + iVar2);
            bool bVar1;
            if (iVar2 < 0)
            {
                bVar1 = (int)uVar4 < (int)uVar3;
            }
            else
            {
                bVar1 = (int)uVar3 < (int)uVar4;
            }

            if (!bVar1)
            {
                AnimCmdSound.DAT_801fac40 = 0;
                uVar3 = uVar4;
            }

            AnimCmdSound.DAT_801fac42 = (byte)uVar3;
            AnimCmdSound.FUN_8005fcec(AnimCmdSound.DAT_801fac41, (short)uVar3);
        }
    }

    // GHIDRA: RollAndUploadClutRange @ 0x80061F1C (VS.EXE)
    // CLOSED. param_1 is the PSX address of a 12-byte struct; this file's two call sites pass
    // BattleScene.RAM_800990c0's two instances, 0xC apart (DAT_800990c0 and DAT_800990cc above).
    // Ghidra's own decompilation never hoists a name for any field past the first two, reading every
    // one of them inline as `*(byte *)((int)param_1 + N)`, so the offsets below are read straight off
    // the disassembly (0x80061f44..0x80062024) rather than off decompiled variable names:
    //   +0x0 int    srcPtr   PSX address of a 16-entry (32-byte) CLUT              (lw)
    //   +0x4 ushort          destination VRAM x for the CLUT upload                (lhu)
    //   +0x6 ushort          destination VRAM y                                    (lhu)
    //   +0x8 byte   bVar1    rotation phase — RunBatchTail decrements this same byte once per frame
    //                        (DAT_800990c8 / DAT_800990d4 are +0x8 of the two instances)  (lbu)
    //   +0x9 byte   loIndex  first CLUT entry the rotation touches                 (lbu)
    //   +0xa byte   hiIndex  last CLUT entry the rotation touches                  (lbu)
    //   +0xb byte   flags    bit7 forces the semi-transparency bit on, bit0 forces it off (lbu)
    //
    // memmove snapshots the whole 16-entry CLUT into a local 32-byte buffer (`u_long auStack_30[8]`,
    // ported as a plain byte[] — see the JUSTIFICATION below, it is nothing like Local30Address).
    // The loop then OVERWRITES ONLY [loIndex, hiIndex] of that snapshot with a value read back from
    // THE SOURCE table at a cyclically rotated index (loIndex + ((bVar1 % count) + i) % count) —
    // re-read through srcPtr every iteration (0x80061fe8 `lw v1,0x0(s1)`), never from the snapshot —
    // the palette-cycle effect PSX games use for water/fire animation. Entries outside the sub-range
    // pass through untouched. Up to three stores land on each touched entry, in this order — plain
    // color, then OR 0x8000 if flags bit7, then AND 0x7fff on whatever is currently stored if flags
    // bit0 — and are kept as three separate writes below rather than folded into one expression, to
    // keep the store count and order exactly what 0x80061ffc/0x80062014/0x80062038 do. Finally the
    // (partially rotated) snapshot is DMA'd to VRAM as a 16-halfword-wide, 1-row LoadImage — width
    // 0x10 and height 1 are the literal constants at 0x80062068/0x80062074, not read from the struct.
    //
    // The original divides twice (`div`) and both are followed by the PSYQ compiler's standard
    // safe-division trap pair — zero divisor traps `break 0x1c00`, MIN_VALUE/-1 traps `break 0x1800`
    // — both hardware halts, not game logic. Per the same rule AnimCmdTransform.cs's opcode-7 case
    // already applies: C#'s DivideByZeroException reaches the zero-divisor halt one instruction
    // earlier, and the MIN/-1 halt is unreachable because every operand feeding both divisions
    // (bVar1, loIndex, hiIndex) is a byte. Rule 12: the original's abort is not softened into a guard.
    //
    // AND WHERE THE FIRST DIVIDE SITS IS LOAD-BEARING, which a first version of this port got wrong
    // by nesting it inside the loop guard. The instruction order at 0x80061F5C is:
    //     92220008   lbu v0,0x8(s1)        ; bVar1
    //     24630001   addiu v1,v1,1         ; iVar4 = hiIndex - loIndex + 1
    //     0043001A   div v0,v1             ; UNCONDITIONAL
    //     1460.. 0007000D  bnez/break 0x1c00     ; zero-divisor trap
    //     .. 0006000D      break 0x1800          ; MIN/-1 trap
    //     00002010   mfhi v0               ; the remainder
    //     18600031   blez v1,...           ; only NOW the loop-skip test
    // So iVar4 == 0 halts the console whether or not the loop would have run. Putting the modulo
    // inside `if (0 < iVar4)` silently skipped the rotation instead of halting -- softening an
    // abort into a guard, which is precisely what rule 12 forbids. It is hoisted back out below.
    // Hoisting also restores the divide COUNT: the original evaluates bVar1 % iVar4 once, not once
    // per iteration.
    private static void RollAndUploadClutRange(int param_1)
    {
        int srcPtr = PsxRam.ReadI32(param_1);
        ushort dstX = PsxRam.ReadU16(param_1 + 4);
        ushort dstY = PsxRam.ReadU16(param_1 + 6);
        byte bVar1 = PsxRam.ReadU8(param_1 + 8);
        byte loIndex = PsxRam.ReadU8(param_1 + 9);
        byte hiIndex = PsxRam.ReadU8(param_1 + 10);
        byte flags = PsxRam.ReadU8(param_1 + 11);

        // JUSTIFICATION: C# language bridge only
        // RELATION: `u_long auStack_30[8]` at 0x80061f28 (sp+0x10). Unlike Local30Address above,
        // this local is never handed to another handler by PSX address — it is built here from
        // srcPtr and consumed by LoadImage below and nowhere else — so it needs no PsxRam-backed
        // region, only a plain byte[]. Sized to the snapshot's own 16 entries: the original's stack
        // frame has more bytes past it, so an out-of-range loIndex/hiIndex from bad data would
        // silently corrupt an adjacent local on the console; here it throws instead, the same
        // trade the div-by-zero note above already makes.
        byte[] local = new byte[0x20];
        for (int i = 0; i < 0x10; i++)
        {
            ushort word = PsxRam.ReadU16(srcPtr + i * 2);
            local[(i * 2) + 0] = (byte)word;
            local[(i * 2) + 1] = (byte)(word >> 8);
        }

        int iVar4 = (hiIndex - loIndex) + 1;

        // Unconditional, before the loop-skip test, exactly as `div v0,v1` at 0x80061F64 is. When
        // iVar4 is 0 this throws where the console executes `break 0x1c00`.
        int phase = bVar1 % iVar4;

        int iVar6 = 0;
        if (0 < iVar4)
        {
            do
            {
                int iVar3 = phase + iVar6;
                int srcIndex = loIndex + (iVar3 % iVar4);
                ushort uVar2 = PsxRam.ReadU16(srcPtr + srcIndex * 2);

                int destIndex = loIndex + iVar6;
                ushort stored = uVar2;
                local[(destIndex * 2) + 0] = (byte)stored;
                local[(destIndex * 2) + 1] = (byte)(stored >> 8);
                if ((flags & 0x80) != 0)
                {
                    stored = (ushort)(uVar2 | 0x8000);
                    local[(destIndex * 2) + 0] = (byte)stored;
                    local[(destIndex * 2) + 1] = (byte)(stored >> 8);
                }

                if ((flags & 1) != 0)
                {
                    stored = (ushort)(stored & 0x7fff);
                    local[(destIndex * 2) + 0] = (byte)stored;
                    local[(destIndex * 2) + 1] = (byte)(stored >> 8);
                }

                iVar6 = iVar6 + 1;
            }
            while (iVar6 < iVar4);
        }

        LibGpu.RECT rect = new LibGpu.RECT { x = (short)dstX, y = (short)dstY, w = 0x10, h = 1 };
        LibGpu.LoadImage(rect, local, 0);
    }
}
