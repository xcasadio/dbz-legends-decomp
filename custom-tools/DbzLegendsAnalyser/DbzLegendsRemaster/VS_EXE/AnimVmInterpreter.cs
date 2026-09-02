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
            FUN_8003ecfc();
        }

        FUN_80061f1c(DAT_800990c0);
        DAT_800990c8 = DAT_800990c8 - 1;
        FUN_80061f1c(DAT_800990cc);
        if ((DAT_800990c8 & 1) != 0)
        {
            DAT_800990d4 = (byte)(DAT_800990d4 + 1);
        }

        if (((AnimVm.DAT_800b305a & 1) == 0) && (sVar8 == 0))
        {
            PsxRam.WriteU16(iVar9 + 0x78, 0);
            FUN_8005fcec(0, 0);
            if ((PsxRam.ReadI32(DAT_8008d320 + 0x10) & 8) != 0)
            {
                FUN_8005ee5c(0, 0, 0x30);
            }

            PsxRam.WriteU16(iVar9 + 0x76, 4);
        }
    }

    // =====================================================================================
    // NOT IN THIS TRANCHE
    // =====================================================================================
    // The tail's five remaining callees belong to the battle and scene subsystems — tranche 2 — and
    // are declared here with their addresses rather than omitted, so the tail's shape is the shape
    // the original has.

    // GHIDRA: DAT_800990c0 @ 0x800990C0, DAT_800990cc @ 0x800990CC (VS.EXE)
    private const int DAT_800990c0 = unchecked((int)0x800990C0);

    private const int DAT_800990cc = unchecked((int)0x800990CC);

    // GHIDRA: DAT_800990c8 @ 0x800990C8 (VS.EXE)
    // Decremented once per batch, and its low bit gates the counter below.
    private static int DAT_800990c8;

    // GHIDRA: DAT_800990d4 @ 0x800990D4 (VS.EXE)
    private static byte DAT_800990d4;

    // GHIDRA: DAT_8008d320 @ 0x8008D320 (VS.EXE)
    private static int DAT_8008d320;

    // GHIDRA: FUN_8003ecfc @ 0x8003ECFC (VS.EXE)
    // BLOCKED: called only when the render-state reset above runs.
    private static void FUN_8003ecfc()
    {
    }

    // GHIDRA: FUN_80061f1c @ 0x80061F1C (VS.EXE)
    // BLOCKED: given two different .bss addresses in succession.
    private static void FUN_80061f1c(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8005fcec @ 0x8005FCEC (VS.EXE)
    // BLOCKED: tranche 2. AnimCmdSound.cs carries a private transliteration of the same address for
    // its own call sites; this one is left as a stub rather than reaching into another family's
    // private member.
    private static void FUN_8005fcec(uint param_1, short param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_8005ee5c @ 0x8005EE5C (VS.EXE)
    // BLOCKED: tranche 2.
    private static void FUN_8005ee5c(int param_1, int param_2, int param_3)
    {
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }
}
