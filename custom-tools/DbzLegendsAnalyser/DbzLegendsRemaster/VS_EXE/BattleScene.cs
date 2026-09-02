using System;
using PsxSdkMonogame;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibGte;

namespace DbzLegendsRemaster.VS_EXE;

// THE BATTLE SCENE — task list 12, and the five phases the fight actually runs through.
//
// WHERE IT COMES FROM. Nothing in main creates this task. FUN_80055f94 @ 0x80055F94 — the battle
// manager's body, reached from LAB_80055e3c on list 9 — creates it at 0x800563F8 with
//
//     FUN_80053330(&LAB_80034eac, 0x50, 0xc, 0x7c, 0, DAT_80083bc0)
//
// which is CreateTask(entry, id 0x50, list 12, 0x7C bytes of workspace, 0, g_TaskListTail[12]):
// DAT_80083BC0 is 0x80083B90 + 12*4, an ELEMENT of TaskSystem.g_TaskListTail, not a global of its
// own. So the scene is created by the manager, one list after it, and destroyed by its own phase 4
// through FUN_8005354c(DAT_8008d16c, 0xc) — DeleteTask on list 12. It exists only for the length of
// one exchange.
//
// The frame order main fixes is list 20, ClearOTag, lists 0..19, submit. List 9 is the manager,
// list 10 the six fighters, list 12 this — so within one frame the manager has decided, all six
// fighters have moved, and only then does the scene build its primitives, all before DrawOTag.
//
// THE STATE MACHINE IS NOT DRIVEN FROM THE CALLBACK, and this was checked rather than assumed.
// LAB_80034eac only READS workspace+0x76 and dispatches on it. Every advance is written by the
// phase body itself, and two of those bodies are not in this file:
//
//   phase 0  FUN_80035030          writes +0x76 = 1   `sh v0,0x76(s0)` @ 0x800356C4, the
//            instruction before its epilogue
//   phase 1  FUN_800356dc          writes +0x76 = 2   only when CdReadSync returns 0
//   phase 2  RenderBattleScene3D   writes +0x76 = 3   unconditionally, `sh v0,0x76(t2)` @
//            0x80036508, the instruction before its epilogue
//   phase 3  ExecuteAnimStreamBatch  writes +0x76 = 4 — IN ANOTHER FILE. VS_EXE/AnimVmInterpreter.cs
//            RunBatchTail's last statement is `PsxRam.WriteU16(iVar9 + 0x76, 4)`, on the same
//            `PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8)` workspace this file dispatches on, and
//            it fires only when the VM is not suspended AND no stream ran (sVar8 == 0).
//   phase 4  FUN_80036a64          writes +0x76 = 0 to restart the machine, and otherwise runs its
//            own four sub-steps on +0x78 before deleting the task.
//
// So the claim that RenderBattleScene3D and ExecuteAnimStreamBatch advance the phase THEMSELVES is
// confirmed on both counts, from the two bodies, not from the dispatcher. The dispatcher contains
// no assignment to +0x76 at all. Phase 3 is also the only arm that is NOT gated on
// AnimVm.DAT_800b305a bit 0: the other four skip their body when the VM is suspended, phase 3 calls
// ExecuteAnimStreamBatch unconditionally and lets that function do its own gating.
//
// THE WORKSPACE, 0x7C bytes. Reached through PsxRam at raw offsets, because the mandate keeps the
// original's layout — it is not a C# object with fields. Closed from the four bodies that write it:
//
//   +0x00 .. +0x17   six fighter TASK NODE pointers        piVar15[0..5]
//   +0x18 .. +0x2F   six fighter WORKSPACE pointers        piVar15[6..11], each = node[+8]
//   +0x34 .. +0x63   six position triples, stride 8        x at +0x34+8i, y at +0x36+8i, z at +0x38+8i
//   +0x64,+0x66,+0x68   the placement MINIMA, copied off fighter 0's +0xB0/+0xB2/+0xB4
//   +0x6C,+0x6E,+0x70   the placement MAXIMA, copied off fighter 0's +0xB8/+0xBA/+0xBC
//   +0x74            the scene id (ushort) — indexes CH_BIN file names and DAT_8008222c
//   +0x76            THE PHASE
//   +0x78            the sub-step inside phases 1 and 4 (short)
//
// 0x78 + 2 = 0x7A, inside the 0x7C the creator reserves. The two placement triples are the same six
// values BattleState documents at FighterPlacementRot / FighterPlacementScale, copied out of
// fighter 0 by RenderBattleScene3D and pushed back into all six by phase 4 — which is why they are
// read through BattleState's constants here and not renamed.
//
// WHAT THIS FILE DOES NOT DECLARE. BattleState.cs owns the battle-context and fighter offsets and
// they are used from there. AnimVm.cs owns the animation workspace at 0x801F2000 and every symbol
// inside it. FileIo.cs owns the CD buffer at 0x801D2000 and the GTE scratchpad. TaskSystem.cs owns
// the scheduler. AnimVmInterpreter.ExecuteAnimStreamBatch is phase 3 and is CALLED, not rewritten.
// Address CONSTANTS that another file already spells are repeated where needed — a const is a
// number, not a storage, and both resolve through PsxRam into the one region that backs them, which
// is the distinction AnimVmInterpreter.cs drew for g_renderFlushFlag.
internal static class BattleScene
{
    // =====================================================================================
    // The task entry point
    // =====================================================================================

    // GHIDRA: LAB_80034eac @ 0x80034EAC (VS.EXE)
    // Ghidra has no function defined here; FUN_80055f94 takes its address as CreateTask's first
    // argument and the decompiler serves the body as `UndefinedFunction_80034eac`. CreateTask stores
    // the raw pointer in the node at +0x04, so the number has to exist.
    internal const int BattleSceneEntry = unchecked((int)0x80034EAC);

    // JUSTIFICATION: C# language bridge only
    // RELATION: TaskSystem keeps the original PSX address in the node and turns it back into a
    // ported method at dispatch time. FUN_80055f94 — the creator — is not in this slice, so the
    // registration is exposed rather than performed: whoever transliterates FUN_80055f94 must call
    // this immediately before its CreateTask, exactly as FighterTask.RegisterFighterTask asks of
    // FUN_800512cc. Without it list 12 walks a live node and dispatches nothing. Idempotent.
    internal static void RegisterBattleSceneTask()
    {
        TaskSystem.RegisterCallback(BattleSceneEntry, () => UpdateBattleScene());
    }

    // =====================================================================================
    // Globals
    // =====================================================================================

    // GHIDRA: DAT_8008d580 @ 0x8008D580 (VS.EXE)
    // The scene workspace, cached in a global by the dispatcher on every frame. Ghidra types it
    // undefined4. The dispatcher is the only writer; it re-reads the workspace out of the task node
    // rather than trusting the cache, so the global is a publication for other code, not a shortcut.
    internal static int DAT_8008d580;

    // GHIDRA: DAT_8008d320 @ 0x8008D320 (VS.EXE)
    // NOT DECLARED HERE, AND THAT IS THE POINT. This is THE BATTLE CONTEXT POINTER — the 0x3034-byte
    // workspace BattleState.cs describes. That it holds exactly that is closed by FUN_80035030
    // below, which indexes it as
    //     DAT_8008d320 + 0x1520 + n*4        BattleState.CtxFighterSlots, the twelve slots
    //     DAT_8008d320 + n*0x14 + 0x15B0     BattleState.CtxSlotRecords, stride CtxSlotRecordStride
    //     DAT_8008d320 + n*0x14 + 0x15C0     BattleState.CtxTargetIndex
    // — three offsets BattleState already closed against the battle context and nothing else.
    //
    // VS_EXE/BattleManager.cs declares it `internal static int DAT_8008d320` and is its ONE WRITER:
    // FUN_80055ee0 publishes the context address there, `sw s0,0x224(gp)` at 0x80055F04. Every read
    // in this file goes to THAT field, `BattleManager.DAT_8008d320`, rather than to a copy of my own.
    // A first draft of this file did declare one, which would have made three C# storages for a word
    // the console holds one of — VS_EXE/AnimVmInterpreter.cs already holds a private copy at its
    // line 246 that can never see the writer. Two is one too many already; three is the tranche-1
    // defect happening again while its post-mortem is still in the file header. Reported upward.

    // GHIDRA: g_cdFileBaseOffset @ 0x8008D26C (VS.EXE)
    // NOT DECLARED HERE either, for the same reason and with the roles reversed. RenderBattleScene3D
    // below is its ONLY WRITER program-wide — `sw v0,0x170(gp)` at 0x80035AF0, so gp = 0x8008D0FC —
    // and sixteen other references read it. VS_EXE/AnimCmdMesh.cs already declares it
    // `internal static int g_cdFileBaseOffset` for AnimCmd_RenderEntryGroup's own reads, so the
    // write below and the three helpers' reads all go to THAT field. One storage: the value this
    // file writes is the value the animation family reads, which is what the console does.

    // GHIDRA: DAT_8008d340 @ 0x8008D340 (VS.EXE)
    // Ghidra types it undefined4. PARTIAL: bit 6 (0x40) is set by phase 1 as a latch and cleared by
    // phase 4 on its way out, and bits 2|3 (0xC) gate phase 0's early call to FUN_8005f704. What
    // sets those two is outside this slice.
    //
    // OWNERSHIP CAVEAT, and this one is NOT resolved. VS_EXE/BattleManager.cs holds
    // `private static int DAT_8008d340` at its line 1658 and reads it twice in its state 2 — once
    // masked with 0xC and once whole — each hit blocking the hand-back for another frame. This file
    // holds the only WRITERS in the port: phase 1 raises bit 6 and phase 4 lowers it. So the manager
    // is waiting on a latch it cannot see. It is declared `internal` here so the manager's slice can
    // point at it, and reported upward; it cannot be fixed from this side, because BattleManager.cs
    // is not this file's to edit.
    internal static uint DAT_8008d340;

    // GHIDRA: DAT_8008d53c @ 0x8008D53C (VS.EXE)
    // Ghidra types it undefined4. Phase 0 copies fighter 0's +0x6C into it and nothing in this
    // slice reads it back. BLOCKED beyond that.
    internal static int DAT_8008d53c;

    // GHIDRA: DAT_1f80012c @ 0x1F80012C (VS.EXE)
    // A scratchpad word phase 0 compares one image byte against. ONLY THE ADDRESS is spelled here:
    // VS_EXE/VS_EXE_exe.cs declares the scalar, `private static int DAT_1f80012c`, and main clears
    // it to 0 at boot. It is private, so this file cannot read that copy, and declaring a second
    // scalar would fork the storage — so the value is read BY ADDRESS instead, which adds no
    // storage at all. PARTIAL: the scratchpad is not a modelled region either, so the read answers
    // zero, which is what main leaves the word at and what VS_EXE_exe's field also holds. The two
    // agree today; when the scratchpad gets its VS_EXE/GteScratch.cs the address read becomes the
    // live one.
    private const int DAT_1f80012c = 0x1F80012C;

    // GHIDRA: DAT_1f80009c @ 0x1F80009C (VS.EXE)
    // A GTE scratchpad word the dispatcher multiplies the sine by. VS_EXE/FileIo.cs models the
    // scratchpad words SetupGeometry touches and states an ownership caveat for the rest; 0x1F80009C
    // is not one of them, so it is declared here. When a VS_EXE/GteScratch.cs lands it moves there
    // with the others, unchanged.
    internal static int DAT_1f80009c;

    // GHIDRA: DAT_800990c0 @ 0x800990C0 (VS.EXE)
    // TWO TWELVE-BYTE RECORDS, at 0x800990C0 and 0x800990CC, and they are shared with the animation
    // VM: RenderBattleScene3D initialises all fourteen fields and AnimVmInterpreter's RunBatchTail
    // then feeds both to FUN_80061f1c every batch, decrementing +0x08 of the first and, on its low
    // bit, incrementing +0x08 of the second. The shape, from the fourteen stores below and Ghidra's
    // own types (undefined4 at +0, undefined2 at +4 and +6, undefined1 at +8..+11):
    //
    //     +0x00  pointer   0x800826E0 for the first record, 0x80082700 for the second
    //     +0x04  short     0xA0 / 0xB0
    //     +0x06  short     0x1E3 for both
    //     +0x08  byte      0 for both — the field RunBatchTail counts on
    //     +0x09  byte      2 for both
    //     +0x0A  byte      8 for both
    //     +0x0B  byte      0 for both
    //
    // Modelled as PSX MEMORY through one LibGpu.RamRegion rather than as fourteen C# scalars,
    // because the original writes it at four different widths and because one region is one storage:
    // AnimVmInterpreter.cs currently keeps private scalars of its own for +0x08 and +0x14, which is
    // a second copy of the same bytes, and that divergence is reported rather than duplicated
    // further here. No other file declares a region over this address.
    private const int DAT_800990c0 = unchecked((int)0x800990C0);

    private const int DAT_800990cc = unchecked((int)0x800990CC);

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: gives the twenty-four bytes above real storage at their real addresses, so that a
    // read through PsxRam from any file lands on the same bytes this one writes. A field initialiser,
    // so it is armed before any method of this class can run.
    internal static readonly byte[] RAM_800990c0 = LibGpu.RamRegion(DAT_800990c0, 0x18);

    // GHIDRA: DAT_800826e0 @ 0x800826E0, DAT_80082700 @ 0x80082700 (VS.EXE)
    // The two blocks the records above point at: sixteen halfwords each, read straight out of the
    // image — 0000 FFFF 873A 9B5A AF7B C39C D7BD EBDE FFFF EE9E D15D B41D 61BE 6E9E 735E 7FFF for
    // the first. Sixteen 16-bit entries is the shape of a 4-bit CLUT. PARTIAL: the consumer,
    // FUN_80061f1c @ 0x80061F1C, is outside this slice, so only the addresses are transliterated.
    private const int DAT_800826e0 = unchecked((int)0x800826E0);

    private const int DAT_80082700 = unchecked((int)0x80082700);

    // GHIDRA: DAT_801f4180 @ 0x801F4180, DAT_801f5180 @ 0x801F5180 (VS.EXE)
    // Two staging areas inside the animation workspace AnimVm.cs owns: four colour words per
    // primitive at 0x801F4180, and the second vertex quad at 0x801F5180 on the same 0x20-byte stride
    // as AnimVm.DAT_801f2180. VS_EXE/AnimCmdMesh.cs spells the same two numbers privately for
    // AnimCmd_RenderEntryGroup, which builds the same primitives from the animation side. These are
    // CONSTS, not storages: both resolve through PsxRam into AnimVm.RAM_801f2000, the single region
    // that backs 0x801F2000..0x801FAC47.
    private const int DAT_801f4180 = unchecked((int)0x801F4180);

    private const int DAT_801f5180 = unchecked((int)0x801F5180);

    // GHIDRA: g_meshEntryFlagsHiBuf @ 0x801FA800 (VS.EXE)
    // Sixty-four shorts, one per render entry. Same const-not-storage note as above; AnimCmdMesh.cs
    // spells it too.
    private const int g_meshEntryFlagsHiBuf = unchecked((int)0x801FA800);

    // GHIDRA: g_cdFileBufferTable @ 0x801D2000 (VS.EXE)
    // Only the ADDRESS is spelled here; the storage is FileIo.g_cdFileBufferTable, which owns it.
    // RenderBattleScene3D reads three separate things out of its head, all through PsxRam:
    //   0x801D2000  a word — low halfword is the relocation count, the SIGN of the whole word picks
    //               the fighter-reorder path
    //   0x801D2004  DAT_801d2004, a halfword: the CH_BIN entry count
    //   0x801D2008  g_chBinEntryTableBasePtr, a word holding the entry table's address AFTER the
    //               relocation loop has added g_cdFileBaseOffset to it. The load is `lw t2,8(a0)` at
    //               0x80035B40 — the VALUE at 0x801D2008, not the address of it, which is what
    //               settles the reading of that line.
    private const int g_cdFileBufferTableAddress = unchecked((int)0x801D2000);

    private const int DAT_801d2004 = unchecked((int)0x801D2004);

    private const int g_chBinEntryTableBasePtr = unchecked((int)0x801D2008);

    // GHIDRA: s__CH_BIN1_CH_NO_BIN_1_80081a50 @ 0x80081A50 (VS.EXE)
    // A table of fixed-width file names, stride 0x1B: "\CH_BIN1\CH_NO.BIN;1" at index 0,
    // "\CH_BIN1\CH_01.BIN;1" at index 1, and so on, each NUL-padded to 27 bytes. Phase 1 indexes it
    // by the scene id at workspace+0x74 and falls back to index 0 when the read fails.
    private const int s__CH_BIN1_CH_NO_BIN_1_80081a50 = unchecked((int)0x80081A50);

    // GHIDRA: DAT_8008222c @ 0x8008222C (VS.EXE)
    // A BYTE table (Ghidra types it undefined, length 1) indexed by the scene id and handed to
    // FUN_8005ec4c. Its first bytes in the image are 00 00 01 03 03 02 01 01 00 00 01 02 …
    private const int DAT_8008222c = unchecked((int)0x8008222C);

    // GHIDRA: DAT_80082164 @ 0x80082164, DAT_800821dc @ 0x800821DC, DAT_800821dd @ 0x800821DD,
    // DAT_80082264 @ 0x80082264, DAT_800822d0 @ 0x800822D0 (VS.EXE)
    // The five image tables phase 0 and phase 4 index. 0x80082164 is bytes on a stride of 3 with
    // bit 7 used as a "already taken" latch; 0x800821DC/0x800821DD are the two bytes of a stride-2
    // record, again with bit 7 as a latch; 0x80082264 is shorts; 0x800822D0 is a stride-6 record of
    // three shorts, one per fighter index.
    //
    // PARTIAL, and it is the same gap every VS.EXE slice has: this port does not model the program
    // image's .rodata, so a PsxRam read at any of these addresses resolves to nothing and answers
    // zero. The addresses and strides are transliterated exactly; the CONTENTS are not available to
    // the running port. Nothing here invents them.
    private const int DAT_80082164 = unchecked((int)0x80082164);

    private const int DAT_800821dc = unchecked((int)0x800821DC);

    private const int DAT_800821dd = unchecked((int)0x800821DD);

    private const int DAT_80082264 = unchecked((int)0x80082264);

    private const int DAT_800822d0 = unchecked((int)0x800822D0);

    // GHIDRA: DAT_801faaac @ 0x801FAAAC (VS.EXE)
    // Sixteen words phase 4 walks, writing 1 into +0x50 of each non-null entry. Inside
    // AnimVm.RAM_801f2000; AnimVm.cs notes this address as the point where the shared variable
    // table's higher indices begin to alias its neighbours. Const, not storage.
    private const int DAT_801faaac = unchecked((int)0x801FAAAC);

    // JUSTIFICATION: C# language bridge only
    // RELATION: phase 4 builds a four-halfword STACK local and hands it to
    // AnimCmdEffects.AnimCmd_SetCharRenderState, which — like every ported opcode handler — takes a
    // PSX address and reads it through PsxRam. The local therefore needs an address. This stands for
    // this file's stack frame and nothing else, and it is deliberately distinct from the two other
    // such locals already modelled — AnimCmdEffects.cs at 0x807FFFE0 and AnimVmInterpreter.cs at
    // 0x807FFFF0 — so the three can never alias. The stack really does live here: crt0 starts SP at
    // 0x807FFFF8.
    private const int Local18Address = unchecked((int)0x807FFFD0);

    private static readonly byte[] RAM_local18 = LibGpu.RamRegion(Local18Address, 8);

    // =====================================================================================
    // THE DISPATCHER
    // =====================================================================================

    // GHIDRA: LAB_80034eac @ 0x80034EAC (VS.EXE)
    // 388 bytes, 0x80034EAC..0x8003502F — its `jr ra` is at 0x80035028 and FUN_80035030 starts on
    // the next word. One incoming reference and it is not a call: FUN_80055f94
    // takes its address at 0x800563F8. The C# name is this port's; the Ghidra symbol above is what
    // the database holds.
    //
    // A jump-table switch on the phase — `lhu v1,0x76(v0)` then `jr v0` at 0x80034EC4/0x80034EF0 —
    // with cases 0..4 and no default. A phase outside 0..4 falls straight through to the return and
    // the frame does nothing, which is the original's behaviour and is kept.
    //
    // Cases 3 and 4 SHARE A TAIL at LAB_80034FB0 and differ only in which scratchpad halfword they
    // load: case 3 takes DAT_1f800084 after running the VM, case 4 takes DAT_1f80007c — read `lh`,
    // signed — after running phase 4. The tail turns that angle into the horizon Y at
    // DAT_1f800120, clamped to 0..0xF0, and pins DAT_1f800124 at 0xA0. Cases 0, 1 and 2 do not run
    // it at all.
    internal static void UpdateBattleScene()
    {
        short sVar1;
        int iVar2;

        DAT_8008d580 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        switch (PsxRam.ReadU16(DAT_8008d580 + 0x76))
        {
            case 0:
                if ((AnimVm.DAT_800b305a & 1) == 0)
                {
                    FUN_80035030();
                }

                break;
            case 1:
                if ((AnimVm.DAT_800b305a & 1) == 0)
                {
                    FUN_800356dc();
                }

                break;
            case 2:
                if ((AnimVm.DAT_800b305a & 1) == 0)
                {
                    RenderBattleScene3D();
                }

                break;
            case 3:
                // NOT gated on DAT_800b305a — the only arm that is not. ExecuteAnimStreamBatch does
                // its own gating, and it is the statement that moves the phase on to 4.
                AnimVmInterpreter.ExecuteAnimStreamBatch();
                sVar1 = FileIo.DAT_1f800084;
                goto LAB_80034fb0;
            case 4:
                sVar1 = FileIo.SVECTOR_1f80007c.vx;
                if ((AnimVm.DAT_800b305a & 1) == 0)
                {
                    FUN_80036a64();
                    sVar1 = FileIo.SVECTOR_1f80007c.vx;
                }

            LAB_80034fb0:
                iVar2 = rsin(-(int)sVar1);
                iVar2 = iVar2 * DAT_1f80009c;
                if (iVar2 < 0)
                {
                    iVar2 = iVar2 + 0xfff;
                }

                FileIo.DAT_1f800120 = (iVar2 >> 0xc) + 0xf0;
                if (0xf0 < FileIo.DAT_1f800120)
                {
                    FileIo.DAT_1f800120 = 0xf0;
                }

                if (FileIo.DAT_1f800120 < 0)
                {
                    FileIo.DAT_1f800120 = 0;
                }

                FileIo.DAT_1f800124 = 0xa0;
                break;
        }
    }

    // =====================================================================================
    // PHASE 0 — pick the exchange, gather its participants, choose the scene id
    // =====================================================================================

    // GHIDRA: FUN_80035030 @ 0x80035030 (VS.EXE)
    // 1708 bytes, 0x80035030..0x800356DB. One caller: the dispatcher's case 0.
    //
    // It clears the sub-step, then does NOTHING AT ALL unless every fighter that is both alive
    // (+0x138 bit 26 clear) and flagged (+0x134 bit 25 set) agrees on bit 26 of +0x134 — uVar7
    // starts at 0x4000000 and is ANDed down across the twelve slots, so one dissenter zeroes it and
    // the whole body is skipped for the frame. That is a barrier: the scene waits until the fighters
    // have all reached the same point.
    //
    // Once through, it: wipes the animation workspace, builds the six-deep participant list into the
    // workspace head (attacker first, then its target at index 3, then the attacker's team-mates at
    // 1..2 and the target's at 4..5), chooses the SCENE ID by one of four different table walks, and
    // finally writes +0x76 = 1.
    private static void FUN_80035030()
    {
        byte bVar1;
        ushort uVar2;
        int iVar3;
        uint uVar4;
        int iVar5;
        int pbVar6;
        uint uVar7;
        uint uVar8;
        ushort uVar9;
        ushort uVar10;
        uint uVar11;
        ushort uVar12;
        uint uVar13;
        uint uVar14;
        int piVar15;

        piVar15 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        PsxRam.WriteU16(piVar15 + 0x78, 0);
        iVar3 = AnimCmdSound.FUN_80060120();
        if (iVar3 != 0)
        {
            FUN_800600b0(2);
        }

        if ((DAT_8008d340 & 0xc) != 0)
        {
            uVar2 = FUN_8005f704((short)PsxRam.ReadI32(piVar15 + 0x74), 0);
            PsxRam.WriteU16(piVar15 + 0x78, uVar2);
        }

        // THE BARRIER. 0x4000000 ANDed down across the twelve slots.
        uVar7 = 0x4000000;
        uVar12 = 0;
        uVar4 = 0;
        do
        {
            iVar3 = PsxRam.ReadI32((int)(uVar4 * 4) + BattleManager.DAT_8008d320 + BattleState.CtxFighterSlots);
            uVar12 = (ushort)(uVar12 + 1);
            if (iVar3 != 0)
            {
                iVar3 = PsxRam.ReadI32(iVar3 + 8);
                if (((uint)PsxRam.ReadI32(iVar3 + 0x138) & 0x4000000) == 0)
                {
                    uVar4 = (uint)PsxRam.ReadI32(iVar3 + 0x134);
                    if ((uVar4 & 0x2000000) != 0)
                    {
                        uVar7 = uVar7 & uVar4;
                    }
                }
            }

            uVar4 = uVar12;
        }
        while (uVar12 < 0xc);

        if (uVar7 != 0)
        {
            // bzero(&DAT_801f2000, 0x8c48) — the animation workspace's own extent, the same clear
            // AnimCmd_RenderEntryGroup performs. AnimVm.cs owns the region.
            Array.Clear(AnimVm.RAM_801f2000, 0, 0x8C48);
            AnimCmdSound.FUN_8005fcec(0, 0);

            uVar12 = 0;
            uVar4 = 0;
            do
            {
                uVar12 = (ushort)(uVar12 + 1);
                PsxRam.WriteI32(piVar15 + 0x18 + (int)uVar4 * 4, 0);
                PsxRam.WriteI32(piVar15 + (int)uVar4 * 4, 0);
                iVar3 = BattleManager.DAT_8008d320;
                uVar4 = uVar12;
            }
            while (uVar12 < 6);

            // The attacker: the slot the context names at +0x2DC2, and its target at
            // BattleState.CtxTargetIndex of that slot's record.
            uVar7 = PsxRam.ReadU16(BattleManager.DAT_8008d320 + 0x2dc2);
            iVar5 = PsxRam.ReadI32((int)(uVar7 * 4) + BattleManager.DAT_8008d320 + BattleState.CtxFighterSlots);
            PsxRam.WriteI32(piVar15, iVar5);
            PsxRam.WriteI32(piVar15 + 0x18, PsxRam.ReadI32(iVar5 + 8));
            uVar4 = PsxRam.ReadU16(iVar3 + (int)uVar7 * BattleState.CtxSlotRecordStride + BattleState.CtxTargetIndex);
            iVar3 = PsxRam.ReadI32((int)(uVar4 * 4) + iVar3 + BattleState.CtxFighterSlots);
            PsxRam.WriteI32(piVar15 + 0x0c, iVar3);
            PsxRam.WriteI32(piVar15 + 0x24, PsxRam.ReadI32(iVar3 + 8));

            // Each of the two points the other's task node at its own +0xAC.
            PsxRam.WriteI32(PsxRam.ReadI32(piVar15 + 0x18) + BattleState.FighterTaskNode,
                PsxRam.ReadI32(piVar15 + 0x0c));
            PsxRam.WriteI32(PsxRam.ReadI32(piVar15 + 0x24) + BattleState.FighterTaskNode,
                PsxRam.ReadI32(piVar15));

            iVar3 = BattleManager.DAT_8008d320;
            if (uVar7 < 6)
            {
                uVar11 = 0;
                uVar13 = 6;
            }
            else
            {
                uVar11 = 6;
                uVar13 = 0xc;
            }

            // The attacker's own team fills slots 1..2 — every member whose slot record carries bit
            // 9 and is neither the attacker nor the target.
            // `while (iVar5 = DAT_8008d320, uVar8 < uVar13)` — the assignment is part of the
            // CONDITION, so it runs before every test INCLUDING THE FAILING ONE. That placement is
            // load-bearing and a first draft of this file got it wrong: the body reassigns iVar5 to
            // a fighter workspace pointer, so hoisting the reload to the top of the body would leave
            // iVar5 holding that pointer on exit, and the next statement writes iVar5 back into
            // DAT_8008d320. The console reloads on the failing test and writes the global back to
            // itself; the hoisted version would have published a fighter's address as the battle
            // context. Spelled as while(true) + break so the order is the original's.
            uVar14 = 1;
            uVar8 = uVar11;
            while (true)
            {
                iVar5 = BattleManager.DAT_8008d320;
                if (uVar8 >= uVar13)
                {
                    break;
                }

                uVar11 = uVar11 + 1;
                if ((uVar8 != uVar7) && (uVar8 != uVar4))
                {
                    iVar5 = PsxRam.ReadI32((int)(uVar8 * 4) + iVar3 + BattleState.CtxFighterSlots);
                    if (iVar5 != 0
                        && (PsxRam.ReadU16(iVar3 + (int)uVar8 * BattleState.CtxSlotRecordStride
                                + BattleState.CtxSlotRecords) & 0x200) != 0)
                    {
                        PsxRam.WriteI32(piVar15 + (int)(uVar14 & 0xffff) * 4, iVar5);
                        iVar5 = PsxRam.ReadI32(iVar5 + 8);
                        PsxRam.WriteI32(piVar15 + 0x18 + (int)(uVar14 & 0xffff) * 4, iVar5);
                        uVar14 = uVar14 + 1;
                        PsxRam.WriteI32(iVar5 + BattleState.FighterTaskNode, PsxRam.ReadI32(piVar15));
                    }
                }

                uVar8 = uVar11 & 0xffff;
            }

            // PARTIAL: the `DAT_8008d320 = iVar5` in this loop's condition, and the identical one
            // below, are how the decompiler spells a register that is reloaded from the global on
            // every test. Both write back the value the same register was just loaded with, so both
            // are no-ops on the console. They are transliterated as printed rather than dropped —
            // rule 12 — because dropping a store on the strength of an inference is exactly the
            // correction the mandate forbids.
            for (; (uVar14 & 0xffff) < 2; uVar14 = uVar14 + 1)
            {
                BattleManager.DAT_8008d320 = iVar5;
                PsxRam.WriteI32(piVar15 + (int)(uVar14 & 0xffff) * 4, 0);
                PsxRam.WriteI32(piVar15 + 0x18 + (int)(uVar14 & 0xffff) * 4, 0);
                iVar5 = BattleManager.DAT_8008d320;
            }

            BattleManager.DAT_8008d320 = iVar5;

            // Then the other team into slots 4..5, walking the six slots on the far side.
            uVar14 = 4;
            if ((uVar11 & 0xffff) == 0xc)
            {
                uVar11 = 0;
                uVar13 = 6;
            }
            else
            {
                uVar13 = uVar13 + 6;
            }

            uVar8 = uVar11 & 0xffff;
            iVar3 = BattleManager.DAT_8008d320;
            while (uVar8 < uVar13)
            {
                uVar11 = uVar11 + 1;
                if ((uVar8 != uVar7) && (uVar8 != uVar4))
                {
                    iVar3 = PsxRam.ReadI32((int)(uVar8 * 4) + iVar5 + BattleState.CtxFighterSlots);
                    if (iVar3 != 0
                        && (PsxRam.ReadU16(iVar5 + (int)uVar8 * BattleState.CtxSlotRecordStride
                                + BattleState.CtxSlotRecords) & 0x200) != 0)
                    {
                        PsxRam.WriteI32(piVar15 + (int)(uVar14 & 0xffff) * 4, iVar3);
                        iVar3 = PsxRam.ReadI32(iVar3 + 8);
                        PsxRam.WriteI32(piVar15 + 0x18 + (int)(uVar14 & 0xffff) * 4, iVar3);
                        uVar14 = uVar14 + 1;
                        PsxRam.WriteI32(iVar3 + BattleState.FighterTaskNode, PsxRam.ReadI32(piVar15));
                    }
                }

                uVar8 = uVar11 & 0xffff;
                iVar3 = BattleManager.DAT_8008d320;
            }

            for (; (uVar14 & 0xffff) < 5; uVar14 = uVar14 + 1)
            {
                BattleManager.DAT_8008d320 = iVar3;
                PsxRam.WriteI32(piVar15 + (int)(uVar14 & 0xffff) * 4, 0);
                PsxRam.WriteI32(piVar15 + 0x18 + (int)(uVar14 & 0xffff) * 4, 0);
                iVar3 = BattleManager.DAT_8008d320;
            }

            BattleManager.DAT_8008d320 = iVar3;

            // THE SCENE ID, chosen four different ways. uVar4 keeps the context's +0x10 as it was
            // BEFORE the rewrite, which is what the `(uVar4 & 8) == 0` test below reads.
            uVar4 = (uint)PsxRam.ReadI32(iVar3 + 0x10);
            PsxRam.WriteI32(iVar3 + 0x10, unchecked((int)(uVar4 & 0xfff4ffff | 0x40000)));
            uVar7 = PsxRam.ReadU16(PsxRam.ReadI32(piVar15));

            if ((uVar4 & 8) == 0)
            {
                if ((PsxRam.ReadU16(iVar3 + (int)PsxRam.ReadU16(iVar3 + 0x2dc2)
                        * BattleState.CtxSlotRecordStride + BattleState.CtxSlotRecords) & 0x200) == 0)
                {
                    uVar12 = 0x41;
                    if (5 < PsxRam.ReadU8(PsxRam.ReadI32(piVar15 + 0x18) + BattleState.FighterSlotIndex))
                    {
                        uVar12 = 0x42;
                    }
                }
                else
                {
                    pbVar6 = DAT_800821dc + (int)uVar7 * 2;
                    uVar12 = PsxRam.ReadU8(pbVar6);
                    if (((PsxRam.ReadU8(pbVar6) & 0x80) == 0)
                        && (PsxRam.ReadU8(DAT_800821dd + (int)uVar7 * 2) == PsxRam.ReadI32(DAT_1f80012c)))
                    {
                        PsxRam.WriteU8(pbVar6, (byte)(PsxRam.ReadU8(pbVar6) | 0x80));
                    }
                    else
                    {
                        uVar12 = 0x40;
                    }
                }
            }
            else if (uVar7 == 0x24)
            {
                if ((short)PsxRam.ReadU16(PsxRam.ReadI32(piVar15 + 4)) == 0x24)
                {
                    uVar12 = 0x34;
                    if ((short)PsxRam.ReadU16(PsxRam.ReadI32(piVar15 + 8)) == 0x24)
                    {
                        uVar12 = 0x35;
                    }
                }
                else
                {
                    uVar12 = 0x33;
                }
            }
            else
            {
                // Three candidates per id, stride 3, bit 7 as an "already used" latch. When all
                // three are already latched the whole run is cleared and the first is taken —
                // without its latch being set again, which is the original's behaviour and is kept.
                uVar9 = 0;
                uVar4 = 0;
                do
                {
                    pbVar6 = DAT_80082164 + (int)uVar4 + (int)uVar7 * 3;
                    bVar1 = PsxRam.ReadU8(pbVar6);
                    uVar12 = bVar1;
                    if ((bVar1 != 0) && ((bVar1 & 0x80) == 0))
                    {
                        PsxRam.WriteU8(pbVar6, (byte)(PsxRam.ReadU8(pbVar6) | 0x80));
                        break;
                    }

                    uVar9 = (ushort)(uVar9 + 1);
                    uVar4 = uVar9;
                }
                while (uVar9 < 3);

                uVar10 = 1;
                if (uVar9 == 3)
                {
                    uVar4 = 1;
                    do
                    {
                        uVar10 = (ushort)(uVar10 + 1);
                        PsxRam.WriteU8(DAT_80082164 + (int)uVar4 + (int)uVar7 * 3,
                            (byte)(PsxRam.ReadU8(DAT_80082164 + (int)uVar4 + (int)uVar7 * 3) & 0x7f));
                        uVar4 = uVar10;
                    }
                    while (uVar10 < 3);

                    uVar12 = (ushort)(PsxRam.ReadU8(DAT_80082164 + (int)uVar7 * 3) & 0x7f);
                }
            }

            iVar3 = BattleManager.DAT_8008d320;
            PsxRam.WriteU16(piVar15 + 0x74, uVar12);
            PsxRam.WriteU16(iVar3 + 4, uVar12);

            if ((short)PsxRam.ReadU16(piVar15 + 0x74) == 0x2a)
            {
                PsxRam.WriteU16(PsxRam.ReadI32(piVar15 + 0x18) + 0x162, 0x1e);
                PsxRam.WriteU16(PsxRam.ReadI32(piVar15 + 0x24) + 0x162, 0x1a);
            }
            else if ((short)PsxRam.ReadU16(piVar15 + 0x74) == 0x32)
            {
                PsxRam.WriteU16(PsxRam.ReadI32(piVar15 + 0x18) + 0x162, 0x24);
                PsxRam.WriteU16(PsxRam.ReadI32(piVar15 + 0x24) + 0x162, 0);
            }
            else
            {
                iVar3 = PsxRam.ReadI32(piVar15 + 0x18);
                PsxRam.WriteU16(PsxRam.ReadI32(piVar15 + 0x24) + 0x162, 0x1a);
                PsxRam.WriteU16(iVar3 + 0x162, 0x1a);
            }

            // Snapshot each participant's position triple into the workspace at +0x34 + i*8.
            uVar12 = 0;
            uVar4 = 0;
            do
            {
                uVar12 = (ushort)(uVar12 + 1);
                if (PsxRam.ReadI32(piVar15 + 0x18 + (int)uVar4 * 4) != 0)
                {
                    PsxRam.WriteU16(piVar15 + 0x34 + (int)uVar4 * 8,
                        PsxRam.ReadU16(PsxRam.ReadI32(piVar15 + 0x18 + (int)uVar4 * 4) + 0x114));
                    PsxRam.WriteU16(piVar15 + 0x36 + (int)uVar4 * 8,
                        PsxRam.ReadU16(PsxRam.ReadI32(piVar15 + 0x18 + (int)uVar4 * 4) + 0x116));
                    PsxRam.WriteU16(piVar15 + 0x38 + (int)uVar4 * 8,
                        PsxRam.ReadU16(PsxRam.ReadI32(piVar15 + 0x18 + (int)uVar4 * 4) + 0x118));
                }

                uVar4 = uVar12;
            }
            while (uVar12 < 6);

            DAT_8008d53c = PsxRam.ReadI32(PsxRam.ReadI32(piVar15 + 0x18) + 0x6c);
            if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
            {
                PsxRam.WriteU16(PsxRam.ReadI32(piVar15 + 0x18) + 4, 0);
                PsxRam.WriteU8(PsxRam.ReadI32(piVar15 + 0x18) + 0x16a, 0x1d);
            }

            // THE PHASE ADVANCE, and the last statement of the function — `sh v0,0x76(s0)` @
            // 0x800356C4, the instruction before the epilogue.
            PsxRam.WriteU16(piVar15 + 0x76, 1);
        }
    }

    // =====================================================================================
    // PHASE 1 — load the CH_BIN for the chosen scene
    // =====================================================================================

    // GHIDRA: FUN_800356dc @ 0x800356DC (VS.EXE)
    // 476 bytes, 0x800356DC..0x800358B7 — immediately before RenderBattleScene3D. One caller: the
    // dispatcher's case 1.
    //
    // A three-step machine on the sub-step at +0x78, guarded by bit 6 of DAT_8008d340, which this
    // function raises on entry and phase 4 lowers on its way out.
    //
    //   below 8   run FUN_8005f704 until it returns 8 or more; a scene id of 0x40 or more skips
    //             straight to 8
    //   == 8      start the read of "\CH_BIN1\CH_xx.BIN;1", falling back to CH_NO.BIN on -1, and
    //             step to 9 on success
    //   == 9      poll CdReadSync; 0 advances the PHASE to 2, -1 steps BACK to 8 and retries
    //
    // The `iVar2 == -1` test on FUN_80061d4c's result can never fire — VS_EXE/FileIo.cs records why,
    // ReadCDData returns an unsigned sector count or 0 — so the CH_NO.BIN fallback is dead on the
    // console. Reproduced, not corrected.
    private static void FUN_800356dc()
    {
        short sVar1;
        int iVar2;
        uint uVar3;
        int iVar4;
        byte[] auStack_18 = new byte[8];

        iVar4 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        iVar2 = AnimCmdSound.FUN_80060120();
        if (iVar2 == 0)
        {
            uVar3 = DAT_8008d340 | 0x40;
            if ((DAT_8008d340 & 0xffffffbf) == 0)
            {
                DAT_8008d340 = uVar3;
                if ((short)PsxRam.ReadU16(iVar4 + 0x78) < 8)
                {
                    if (0x3f < PsxRam.ReadU16(iVar4 + 0x74))
                    {
                        PsxRam.WriteU16(iVar4 + 0x78, 8);
                    }

                    sVar1 = (short)FUN_8005f704((short)PsxRam.ReadU16(iVar4 + 0x74),
                        (short)PsxRam.ReadU16(iVar4 + 0x78));
                    PsxRam.WriteU16(iVar4 + 0x78, (ushort)sVar1);
                    if (sVar1 < 8)
                    {
                        return;
                    }

                    if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
                    {
                        FUN_8005ed28();
                    }
                }

                sVar1 = (short)PsxRam.ReadU16(iVar4 + 0x78);
                if (sVar1 == 8)
                {
                    if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
                    {
                        FUN_80042054(5, 0x20);
                    }

                    iVar2 = (int)FileIo.ReadFile(
                        PsxStringAt(s__CH_BIN1_CH_NO_BIN_1_80081a50 + PsxRam.ReadU16(iVar4 + 0x74) * 0x1b),
                        g_cdFileBufferTableAddress, 1);
                    if (iVar2 == -1)
                    {
                        iVar2 = (int)FileIo.ReadFile(PsxStringAt(s__CH_BIN1_CH_NO_BIN_1_80081a50),
                            g_cdFileBufferTableAddress, 1);
                    }

                    if (iVar2 == 0)
                    {
                        PsxRam.WriteU16(iVar4 + 0x78, (ushort)(PsxRam.ReadU16(iVar4 + 0x78) + 1));
                    }

                    sVar1 = (short)PsxRam.ReadU16(iVar4 + 0x78);
                }

                if (sVar1 == 9)
                {
                    iVar2 = CdReadSync(1, auStack_18);
                    if (iVar2 == 0)
                    {
                        // THE PHASE ADVANCE.
                        PsxRam.WriteU16(iVar4 + 0x76, 2);
                    }
                    else if (iVar2 == -1)
                    {
                        PsxRam.WriteU16(iVar4 + 0x78, (ushort)(PsxRam.ReadU16(iVar4 + 0x78) - 1));
                    }
                }
            }
        }
        else
        {
            FUN_800600b0(2);
        }
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original hands FUN_80061d4c a raw pointer into the fixed-width name table at
    // 0x80081A50; VS_EXE/FileIo.cs's transliteration of that function takes the char[] its own
    // callee WaitSearchFile needs. This reads the NUL-terminated name at the PSX address and hands
    // over the same characters. No behaviour is added: it is the pointer-to-array conversion C#
    // forces, and it reads through PsxRam like every other memory access in this file.
    //
    // PARTIAL: the program image's .rodata is not modelled by this port, so today every byte reads
    // back zero and the array comes out empty. The address arithmetic is the original's and becomes
    // correct the moment the image is modelled.
    private static char[] PsxStringAt(int address)
    {
        int length = 0;
        while (length < 0x1b && PsxRam.ReadU8(address + length) != 0)
        {
            length = length + 1;
        }

        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = (char)PsxRam.ReadU8(address + i);
        }

        return chars;
    }

    // =====================================================================================
    // PHASE 2 — build the scene's primitives out of the CH_BIN
    // =====================================================================================

    // GHIDRA: RenderBattleScene3D @ 0x800358B8 (VS.EXE)
    // 3208 bytes, 0x800358B8..0x8003653F. One caller: the dispatcher's case 2.
    //
    // ITS NAME AND ITS PROOF ARE NOT THIS SLICE'S. A previous session named it and left a pre-comment
    // on 0x800358B8 in the Ghidra database, read back here verbatim rather than restated:
    //
    //   "CERTAIN: homologous to GAME.EXE RenderBattleScene3D. Same CH_BIN relocation base
    //    (g_cdFileBaseOffset=0x2e800), same initialization of
    //    g_renderMetadataBuffer/g_meshCountBuffer/g_meshStreamPtrBuffer/g_meshOffsetBuffer, and same
    //    per-entry traversal order over the CH_BIN entry table at DAT_801d2008."
    //
    // and a second, on 0x800363F4:
    //
    //   "CERTAIN: g_meshCountBuffer receives primitive_count_packed.low16 only on this overlay path;
    //    no proven copy of primitive_count_packed.high16 here."
    //
    // The second one is visible in the code below as `*local_58 = (short)uVar8;` — the low half of
    // the entry's first word and nothing else.
    //
    // WHAT IT DOES, in four movements:
    //
    //   1. The mode split on the battle context's +0x10 bit 3. Clear — the ordinary path — and it
    //      raises bit 27 of +0x138 on all six fighters. Set, and it instead demands
    //      FUN_80042054(9,0) == 7 before doing anything, hard-codes two rotations, copies fighter 0's
    //      placement box into the workspace, then flattens ALL six fighters to one shared box
    //      (min -32767/-3000/-32767, max 32767/120/32767) and points the scratchpad camera at
    //      (0xFFFFB1E0, 0, 0xFFFF98F1). That is the replay/camera mode; the ordinary path leaves the
    //      per-fighter boxes alone.
    //   2. The relocation. g_cdFileBaseOffset = 0x2E800, then every word of the CD buffer from index
    //      2 up to the count in its first halfword has 0x2E800 added to it, turning file offsets into
    //      addresses. Then, if that first word is NEGATIVE, the six fighter pointers are re-sorted by
    //      team: whichever team fighter 0 belongs to lands at indices 0..2, the other at 3..5.
    //   3. THE EMITTER, one 32-byte vertex block and one POLY_GT4 per primitive per entry. Positions
    //      come from a 6-byte-per-vertex table, normals from a second one, colours from a run of
    //      words, UVs from an 8-byte record read as origin + size. The three FUN_800365xx helpers
    //      below advance the four parallel streams.
    //   4. The tail: the two twelve-byte records at 0x800990C0, an optional pair of calls in camera
    //      mode, and THE PHASE ADVANCE to 3.
    //
    // THE POLY_GT4 FIELD OFFSETS the emitter reaches through `puVar13`, which the decompiler anchors
    // on `&local_b0->v3` — that is base + 49, and every index below is relative to it:
    //   -0x2D -> +4  r0g0b0code    -0x2A -> +7  code      -0x25 -> +12 u0      -0x24 -> +13 v0
    //   -0x21 -> +16 r1g1b1p1      -0x19 -> +24 u1        -0x18 -> +25 v1
    //   -0x15 -> +28 r2g2b2p2      -0x0D -> +36 u2        -0x0C -> +37 v2
    //   -0x09 -> +40 r3g3b3p3      -0x01 -> +48 u3         0x00 -> +49 v3
    // and the stride from one primitive's v3 to the next is 0x34, POLY_GT4's size.
    //
    // `puVar13[-0x2a] = puVar13[-0x2a];` is a SELF-ASSIGNMENT of the code byte. It is the original's
    // — the compiler reloading and restoring `code` around the colour word it has just written over
    // — and rule 12 keeps it rather than deleting it as dead.
    internal static void RenderBattleScene3D()
    {
        int pbVar1;
        byte bVar2;
        ushort uVar3;
        int iVar4;
        uint uVar5;
        uint uVar6;
        int uVar7;
        uint uVar8;
        int iVar9;
        int puVar10;
        int puVar11;
        int puVar12;
        int puVar13;
        int puVar14;
        int puVar15;
        int iVar16;
        ushort uVar17;
        int puVar18;
        byte local_f0;
        byte local_ee;
        byte local_ec;
        byte local_ea;
        int[] local_e8 = new int[6];
        int local_d0;
        int local_cc;
        int local_c8;
        int local_c4;
        int local_c0;
        int local_bc;
        int local_b8;
        int local_b0;
        ushort local_a8;
        ushort local_a0;
        int local_98;
        short local_90;
        short local_88;
        short local_80;
        short local_78;
        short local_70;
        short local_68;
        int local_60;
        int local_58;
        int local_50;
        int local_48;
        ushort local_40;
        int local_38;
        int local_30;

        local_b0 = AnimVm.DAT_801f7180;
        local_b8 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);

        // ---- 1. the mode split on the battle context's +0x10 bit 3 -------------------------
        if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) == 0)
        {
            uVar17 = 0;
            uVar5 = 0;
            do
            {
                iVar4 = PsxRam.ReadI32((int)(uVar5 * 4) + local_b8 + 0x18);
                uVar17 = (ushort)(uVar17 + 1);
                if (iVar4 != 0)
                {
                    PsxRam.WriteI32(iVar4 + 0x138,
                        unchecked((int)((uint)PsxRam.ReadI32(iVar4 + 0x138) | 0x8000000)));
                }

                uVar5 = uVar17;
            }
            while (uVar17 < 6);
        }
        else
        {
            iVar4 = FUN_80042054(9, 0);
            if (iVar4 != 7)
            {
                return;
            }

            PsxRam.WriteU16(PsxRam.ReadI32(local_b8 + 0x18) + 0x114, 0xb1e0);
            PsxRam.WriteU16(PsxRam.ReadI32(local_b8 + 0x18) + 0x116, 0);
            PsxRam.WriteU16(PsxRam.ReadI32(local_b8 + 0x18) + 0x118, 0x98f1);
            PsxRam.WriteU16(PsxRam.ReadI32(local_b8 + 0x24) + 0x114, 0x98f1);
            PsxRam.WriteU16(PsxRam.ReadI32(local_b8 + 0x24) + 0x116, 0);
            PsxRam.WriteU16(PsxRam.ReadI32(local_b8 + 0x24) + 0x118, 0x98f1);

            // Fighter 0's placement box, copied into the workspace at +0x64 / +0x6C.
            PsxRam.WriteU16(local_b8 + 100,
                PsxRam.ReadU16(PsxRam.ReadI32(local_b8 + 0x18) + BattleState.FighterBoundsMin));
            uVar17 = 0;
            PsxRam.WriteU16(local_b8 + 0x66,
                PsxRam.ReadU16(PsxRam.ReadI32(local_b8 + 0x18) + BattleState.FighterBoundsMin + 2));
            PsxRam.WriteU16(local_b8 + 0x68,
                PsxRam.ReadU16(PsxRam.ReadI32(local_b8 + 0x18) + BattleState.FighterBoundsMin + 4));
            PsxRam.WriteU16(local_b8 + 0x6c,
                PsxRam.ReadU16(PsxRam.ReadI32(local_b8 + 0x18) + BattleState.FighterBoundsMax));
            PsxRam.WriteU16(local_b8 + 0x6e,
                PsxRam.ReadU16(PsxRam.ReadI32(local_b8 + 0x18) + BattleState.FighterBoundsMax + 2));
            PsxRam.WriteU16(local_b8 + 0x70,
                PsxRam.ReadU16(PsxRam.ReadI32(local_b8 + 0x18) + BattleState.FighterBoundsMax + 4));

            uVar5 = 0;
            do
            {
                iVar4 = (int)(uVar5 * 4) + local_b8;
                uVar17 = (ushort)(uVar17 + 1);
                if (PsxRam.ReadI32(iVar4 + 0x18) != 0)
                {
                    PsxRam.WriteU16(PsxRam.ReadI32(iVar4 + 0x18) + BattleState.FighterBoundsMin, 0x8001);
                    PsxRam.WriteU16(PsxRam.ReadI32(iVar4 + 0x18) + BattleState.FighterBoundsMin + 2, 0xf448);
                    PsxRam.WriteU16(PsxRam.ReadI32(iVar4 + 0x18) + BattleState.FighterBoundsMin + 4, 0x8001);
                    PsxRam.WriteU16(PsxRam.ReadI32(iVar4 + 0x18) + BattleState.FighterBoundsMax, 0x7fff);
                    PsxRam.WriteU16(PsxRam.ReadI32(iVar4 + 0x18) + BattleState.FighterBoundsMax + 2, 0x78);
                    PsxRam.WriteU16(PsxRam.ReadI32(iVar4 + 0x18) + BattleState.FighterBoundsMax + 4, 0x7fff);
                }

                uVar5 = uVar17;
            }
            while (uVar17 < 6);

            FileIo.DAT_1f8000c4 = unchecked((int)0xffffb1e0);
            FileIo.DAT_1f8000c8 = 0;
            FileIo.DAT_1f8000cc = unchecked((int)0xffff98f1);
            FUN_80042054(4, 0x20);
            PsxRam.WriteU16(PsxRam.ReadI32(local_b8 + 0x18) + 4, 0);
            PsxRam.WriteU8(PsxRam.ReadI32(local_b8 + 0x18) + 0x16a, 0);
        }

        // ---- 2. the relocation, and the team re-sort -------------------------------------
        AnimCmdMesh.g_cdFileBaseOffset = 0x2e800;
        uVar5 = 2;
        if (2 < (ushort)PsxRam.ReadI32(g_cdFileBufferTableAddress))
        {
            uVar8 = 2;
            do
            {
                uVar5 = uVar5 + 1;
                PsxRam.WriteI32(g_cdFileBufferTableAddress + (int)uVar8 * 4,
                    PsxRam.ReadI32(g_cdFileBufferTableAddress + (int)uVar8 * 4) + AnimCmdMesh.g_cdFileBaseOffset);
                uVar8 = uVar5 & 0xffff;
            }
            while ((uVar5 & 0xffff) < ((uint)PsxRam.ReadI32(g_cdFileBufferTableAddress) & 0xffff));
        }

        local_98 = PsxRam.ReadI32(g_chBinEntryTableBasePtr);
        local_40 = PsxRam.ReadU16(DAT_801d2004);
        if (PsxRam.ReadI32(g_cdFileBufferTableAddress) < 0)
        {
            uVar17 = 0;
            do
            {
                uVar5 = uVar17;
                uVar17 = (ushort)(uVar17 + 1);
                iVar4 = (int)(uVar5 * 4) + local_b8;
                local_e8[uVar5] = PsxRam.ReadI32(iVar4 + 0x18);
                PsxRam.WriteI32(iVar4 + 0x18, 0);
            }
            while (uVar17 < 6);

            uVar5 = 3;
            if (PsxRam.ReadU8(local_e8[0] + BattleState.FighterSlotIndex) < 6)
            {
                local_a8 = 0;
            }
            else
            {
                local_a8 = 3;
                uVar5 = 0;
            }

            uVar17 = 0;
            uVar8 = 0;
            do
            {
                iVar4 = local_e8[uVar8];
                if (iVar4 != 0)
                {
                    uVar6 = uVar5 & 0xffff;
                    if (PsxRam.ReadU8(iVar4 + BattleState.FighterSlotIndex) < 6)
                    {
                        uVar6 = local_a8;
                        local_a8 = (ushort)(local_a8 + 1);
                    }
                    else
                    {
                        uVar5 = uVar5 + 1;
                    }

                    PsxRam.WriteI32((int)(uVar6 * 4) + local_b8 + 0x18, iVar4);
                    local_e8[uVar8] = 0;
                }

                uVar17 = (ushort)(uVar17 + 1);
                uVar8 = uVar17;
            }
            while (uVar17 < 6);
        }

        // ---- 3. the emitter --------------------------------------------------------------
        puVar10 = AnimVm.DAT_801f2180;
        puVar14 = DAT_801f4180;
        puVar18 = DAT_801f5180;
        local_60 = AnimVm.g_renderMetadataBuffer;
        local_58 = AnimVm.g_meshCountBuffer;
        local_50 = AnimVm.g_meshStreamPtrBuffer;
        local_48 = AnimVm.g_meshOffsetBuffer;
        uVar5 = 0;
        local_a0 = 0;
        if (0 < (int)((uint)local_40 << 0x10))
        {
            local_38 = local_98 + 4;
            do
            {
                uVar6 = uVar5 & 0xffff;
                uVar8 = (uint)PsxRam.ReadI32(local_98);
                PsxRam.WriteU16(AnimVm.g_meshXOffsetBuffer + (int)uVar6 * 2, 0);
                PsxRam.WriteU16(g_meshEntryFlagsHiBuf + (int)uVar6 * 2, (ushort)(short)(uVar8 >> 0x10));
                PsxRam.WriteI32(local_60,
                    unchecked((int)(uVar6 + (uVar8 & 0xffff) * 0x100 + (uint)local_a0 * 0x1000000)));
                local_c8 = PsxRam.ReadI32(local_38 + 3 * 4) + AnimCmdMesh.g_cdFileBaseOffset;
                local_d0 = PsxRam.ReadI32(local_38 + 2 * 4) + AnimCmdMesh.g_cdFileBaseOffset;
                local_cc = PsxRam.ReadI32(local_d0) + AnimCmdMesh.g_cdFileBaseOffset;
                local_60 = local_60 + 4;
                local_88 = (short)((uint)PsxRam.ReadI32(local_d0 + 4) >> 0x10);
                local_c4 = PsxRam.ReadI32(local_c8) + AnimCmdMesh.g_cdFileBaseOffset;
                local_90 = (short)PsxRam.ReadI32(local_d0 + 4);
                local_c0 = PsxRam.ReadI32(local_38 + 4 * 4) + AnimCmdMesh.g_cdFileBaseOffset;
                local_78 = (short)((uint)PsxRam.ReadI32(local_c8 + 3 * 4) >> 0x10);
                local_bc = PsxRam.ReadI32(local_c0) + AnimCmdMesh.g_cdFileBaseOffset;
                local_80 = (short)PsxRam.ReadI32(local_c8 + 3 * 4);
                local_68 = (short)((uint)PsxRam.ReadI32(local_c0 + 4) >> 0x10);
                iVar16 = PsxRam.ReadI32(local_c8 + 4) + AnimCmdMesh.g_cdFileBaseOffset;
                uVar8 = (uint)PsxRam.ReadI32(local_38 + 5 * 4);
                iVar4 = PsxRam.ReadI32(local_c8 + 2 * 4) + AnimCmdMesh.g_cdFileBaseOffset;
                local_70 = (short)PsxRam.ReadI32(local_c0 + 4);
                if (uVar8 != 0)
                {
                    // The entry carries a command stream: its address is published into
                    // g_meshStreamPtrBuffer and its frame countdown into g_meshOffsetBuffer, which
                    // is precisely what ExecuteAnimStreamBatch walks in phase 3.
                    PsxRam.WriteI32(local_50, unchecked((int)uVar8));
                    iVar9 = unchecked((int)uVar8) + AnimCmdMesh.g_cdFileBaseOffset;
                    PsxRam.WriteI32(local_50, iVar9 + 2);
                    PsxRam.WriteU16(local_48, PsxRam.ReadU16(iVar9 + 2));
                    local_48 = local_48 + 2;
                    PsxRam.WriteI32(local_50, PsxRam.ReadI32(local_50) + 2);
                    local_50 = local_50 + 4;
                }

                local_a8 = 0;
                if (0 < (short)PsxRam.ReadI32(local_38))
                {
                    puVar13 = local_b0 + 49;
                    puVar12 = puVar18 + 2 * 2;
                    puVar11 = puVar10 + 2 * 2;
                    local_30 = puVar10;
                    do
                    {
                        bVar2 = PsxRam.ReadU8(local_c4 + 8);
                        PsxRam.WriteU16(puVar11 + 1 * 2, bVar2);
                        if (bVar2 == 0)
                        {
                            SetPolyGT4(local_b0);
                        }
                        else
                        {
                            SetPolyGT3(local_b0);
                        }

                        uVar17 = 0;
                        do
                        {
                            puVar15 = puVar14;
                            uVar17 = (ushort)(uVar17 + 1);
                            PsxRam.WriteI32(puVar15, PsxRam.ReadI32(local_cc));
                            local_cc = local_cc + 4;
                            uVar7 = FUN_80036540(local_88, (uint)local_90, ref local_d0, ref local_cc);
                            local_88 = (short)((uint)uVar7 >> 0x10);
                            local_90 = (short)uVar7;
                            puVar14 = puVar15 + 4;
                        }
                        while (uVar17 < 4);

                        PsxRam.WriteI32(puVar13 - 0x2d, PsxRam.ReadI32(puVar15 - 3 * 4));
                        PsxRam.WriteU8(puVar13 - 0x2a, PsxRam.ReadU8(puVar13 - 0x2a));
                        PsxRam.WriteI32(puVar13 - 0x21, PsxRam.ReadI32(puVar15 - 2 * 4));
                        PsxRam.WriteI32(puVar13 - 0x15, PsxRam.ReadI32(puVar15 - 1 * 4));
                        PsxRam.WriteI32(puVar13 - 9, PsxRam.ReadI32(puVar15));
                        SetShadeTex(local_b0, 0);
                        SetSemiTrans(local_b0, 1);

                        PsxRam.WriteU16(local_30, PsxRam.ReadU16(PsxRam.ReadU8(local_c4) * 6 + iVar16));
                        PsxRam.WriteU16(puVar11 - 1 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4) * 6 + iVar16 + 2));
                        PsxRam.WriteU16(puVar11, PsxRam.ReadU16(PsxRam.ReadU8(local_c4) * 6 + iVar16 + 4));
                        PsxRam.WriteU16(local_30 + 4 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 1) * 6 + iVar16));
                        PsxRam.WriteU16(puVar11 + 3 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 1) * 6 + iVar16 + 2));
                        PsxRam.WriteU16(puVar11 + 4 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 1) * 6 + iVar16 + 4));
                        PsxRam.WriteU16(local_30 + 8 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 2) * 6 + iVar16));
                        PsxRam.WriteU16(puVar11 + 7 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 2) * 6 + iVar16 + 2));
                        PsxRam.WriteU16(puVar11 + 8 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 2) * 6 + iVar16 + 4));
                        local_30 = local_30 + 0xc * 2;
                        PsxRam.WriteU16(local_30, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 3) * 6 + iVar16));
                        PsxRam.WriteU16(puVar11 + 0xb * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 3) * 6 + iVar16 + 2));
                        PsxRam.WriteU16(puVar11 + 0xc * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 3) * 6 + iVar16 + 4));

                        PsxRam.WriteU16(puVar18, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 4) * 6 + iVar4));
                        PsxRam.WriteU16(puVar12 - 1 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 4) * 6 + iVar4 + 2));
                        PsxRam.WriteU16(puVar12, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 4) * 6 + iVar4 + 4));
                        puVar14 = puVar15 + 4;
                        PsxRam.WriteU16(puVar18 + 4 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 5) * 6 + iVar4));
                        PsxRam.WriteU16(puVar12 + 3 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 5) * 6 + iVar4 + 2));
                        local_a0 = (ushort)(local_a0 + 1);
                        PsxRam.WriteU16(puVar12 + 4 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 5) * 6 + iVar4 + 4));
                        local_a8 = (ushort)(local_a8 + 1);
                        PsxRam.WriteU16(puVar18 + 8 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 6) * 6 + iVar4));
                        PsxRam.WriteU16(puVar12 + 7 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 6) * 6 + iVar4 + 2));
                        PsxRam.WriteU16(puVar12 + 8 * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 6) * 6 + iVar4 + 4));
                        PsxRam.WriteU16(puVar18 + 0xc * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 7) * 6 + iVar4));
                        PsxRam.WriteU16(puVar12 + 0xb * 2, PsxRam.ReadU16(PsxRam.ReadU8(local_c4 + 7) * 6 + iVar4 + 2));
                        pbVar1 = local_c4 + 7;
                        local_b0 = local_b0 + 0x34;
                        local_c4 = local_c4 + 0xc;
                        PsxRam.WriteU16(puVar12 + 0xc * 2, PsxRam.ReadU16(PsxRam.ReadU8(pbVar1) * 6 + iVar4 + 4));
                        uVar7 = FUN_800365f8(local_78, (uint)local_80, ref local_c8, ref local_c4);
                        puVar11 = puVar11 + 0x10 * 2;
                        puVar18 = puVar18 + 0x10 * 2;
                        puVar12 = puVar12 + 0x10 * 2;

                        // The UV record: four halfwords, low byte of each. Origin, then the two
                        // sizes added on to make the far corners.
                        uVar3 = PsxRam.ReadU16(local_bc + 1 * 2);
                        local_ec = (byte)((sbyte)PsxRam.ReadU16(local_bc) + (sbyte)PsxRam.ReadU16(local_bc + 2 * 2));
                        local_f0 = (byte)PsxRam.ReadU16(local_bc);
                        local_ea = (byte)((sbyte)PsxRam.ReadU16(local_bc + 1 * 2) + (sbyte)PsxRam.ReadU16(local_bc + 3 * 2));
                        PsxRam.WriteU8(puVar13 - 0x25, local_f0);
                        local_ee = (byte)uVar3;
                        PsxRam.WriteU8(puVar13 - 0x24, local_ee);
                        local_78 = (short)((uint)uVar7 >> 0x10);
                        PsxRam.WriteU8(puVar13 - 0x19, local_ec);
                        local_80 = (short)uVar7;
                        PsxRam.WriteU8(puVar13 - 0x18, local_ee);
                        PsxRam.WriteU8(puVar13 - 0xd, local_f0);
                        PsxRam.WriteU8(puVar13 - 0xc, local_ea);
                        local_30 = local_30 + 4 * 2;
                        PsxRam.WriteU8(puVar13 - 1, local_ec);
                        PsxRam.WriteU8(puVar13, local_ea);
                        local_bc = local_bc + 4 * 2;
                        uVar7 = FUN_800366b0(local_68, (uint)local_70, ref local_c0, ref local_bc);
                        local_68 = (short)((uint)uVar7 >> 0x10);
                        local_70 = (short)uVar7;
                        puVar13 = puVar13 + 0x34;
                        puVar10 = local_30;
                    }
                    while ((int)(uint)local_a8 < (int)(short)PsxRam.ReadI32(local_38));
                }

                uVar8 = (uint)PsxRam.ReadI32(local_38);
                local_38 = local_38 + 7 * 4;
                local_98 = local_98 + 7 * 4;
                uVar5 = uVar5 + 1;

                // The second CERTAIN comment's subject: the LOW half of the entry word, and no
                // proven copy of the high half on this overlay path.
                PsxRam.WriteU16(local_58, (ushort)(short)uVar8);
                local_58 = local_58 + 2;
            }
            while ((int)(uVar5 & 0xffff) < (int)(short)local_40);
        }

        // ---- 4. the tail -----------------------------------------------------------------
        PsxRam.WriteI32(DAT_800990c0 + 0, DAT_800826e0);
        PsxRam.WriteU16(DAT_800990c0 + 4, 0xa0);
        PsxRam.WriteU16(DAT_800990c0 + 6, 0x1e3);
        PsxRam.WriteU8(DAT_800990c0 + 8, 0);
        PsxRam.WriteU8(DAT_800990c0 + 9, 2);
        PsxRam.WriteU8(DAT_800990c0 + 10, 8);
        PsxRam.WriteU8(DAT_800990c0 + 11, 0);
        PsxRam.WriteI32(DAT_800990cc + 0, DAT_80082700);
        PsxRam.WriteU16(DAT_800990cc + 4, 0xb0);
        PsxRam.WriteU16(DAT_800990cc + 6, 0x1e3);
        PsxRam.WriteU8(DAT_800990cc + 8, 0);
        PsxRam.WriteU8(DAT_800990cc + 9, 2);
        PsxRam.WriteU8(DAT_800990cc + 10, 8);
        PsxRam.WriteU8(DAT_800990cc + 11, 0);

        if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
        {
            FUN_8005ec4c(PsxRam.ReadU8(DAT_8008222c + PsxRam.ReadU16(local_b8 + 0x74)));
            FUN_8005ed70(-1, -1);
        }

        // THE PHASE ADVANCE, and the last statement of the function — `sh v0,0x76(t2)` @
        // 0x80036508, the instruction before the epilogue. Unconditional:
        // whichever mode the context is in, and whether or not a single primitive was emitted,
        // phase 2 runs exactly once and hands over to the animation VM.
        PsxRam.WriteU16(local_b8 + 0x76, 3);
    }

    // =====================================================================================
    // The three stream-advance helpers
    // =====================================================================================
    // 184 bytes each, one call site each, all three inside the emitter above, and all three the same
    // function with two constants changed. They advance a pair of parallel cursors — a RECORD cursor
    // (param_3) and a DATA cursor (param_4) — under a two-level countdown packed into one int: the
    // outer count in the high halfword, the inner in the low.
    //
    //   outer hits zero -> take the next data pointer at record[+K] and reload the inner count from
    //                      record[+K-4], advancing the record cursor by K
    //   inner still live -> reload the data pointer from record[+0] and the outer count from the
    //                      halfword at record[+K-2]
    //
    // K is 8 for 0x80036540 and 0x800366B0, and 0x10 for 0x800365F8 — the only difference between
    // the three, and it is why the vertex and normal streams walk 8-byte records while the face
    // stream walks 16-byte ones. The whole packed pair comes back as `high * 0x10000 + (short)low`,
    // which the caller splits again.
    //
    // param_3 and param_4 are `int *` in the original: pointers to the caller's own locals, written
    // through. `ref int` is the mechanical C# spelling of that and changes nothing.

    // GHIDRA: FUN_80036540 @ 0x80036540 (VS.EXE)
    private static int FUN_80036540(int param_1, uint param_2, ref int param_3, ref int param_4)
    {
        int iVar1;
        uint uVar2;

        uVar2 = (uint)(param_1 - 1);
        if ((uVar2 & 0xffff) == 0)
        {
            param_2 = param_2 - 1;
            if ((param_2 & 0xffff) == 0)
            {
                iVar1 = param_3;
                param_3 = iVar1 + 8;
                iVar1 = PsxRam.ReadI32(iVar1 + 8);
                param_4 = iVar1;
                param_4 = iVar1 + AnimCmdMesh.g_cdFileBaseOffset;
                param_2 = (uint)PsxRam.ReadI32(param_3 + 4);
                uVar2 = param_2 >> 0x10;
            }
            else
            {
                iVar1 = PsxRam.ReadI32(param_3);
                param_4 = iVar1;
                param_4 = iVar1 + AnimCmdMesh.g_cdFileBaseOffset;
                uVar2 = PsxRam.ReadU16(param_3 + 6);
            }
        }

        return (int)(uVar2 * 0x10000) + (short)param_2;
    }

    // GHIDRA: FUN_800365f8 @ 0x800365F8 (VS.EXE)
    private static int FUN_800365f8(int param_1, uint param_2, ref int param_3, ref int param_4)
    {
        int iVar1;
        uint uVar2;

        uVar2 = (uint)(param_1 - 1);
        if ((uVar2 & 0xffff) == 0)
        {
            param_2 = param_2 - 1;
            if ((param_2 & 0xffff) == 0)
            {
                iVar1 = param_3;
                param_3 = iVar1 + 0x10;
                iVar1 = PsxRam.ReadI32(iVar1 + 0x10);
                param_4 = iVar1;
                param_4 = iVar1 + AnimCmdMesh.g_cdFileBaseOffset;
                param_2 = (uint)PsxRam.ReadI32(param_3 + 0xc);
                uVar2 = param_2 >> 0x10;
            }
            else
            {
                iVar1 = PsxRam.ReadI32(param_3);
                param_4 = iVar1;
                param_4 = iVar1 + AnimCmdMesh.g_cdFileBaseOffset;
                uVar2 = PsxRam.ReadU16(param_3 + 0xe);
            }
        }

        return (int)(uVar2 * 0x10000) + (short)param_2;
    }

    // GHIDRA: FUN_800366b0 @ 0x800366B0 (VS.EXE)
    // Ends at 0x80036767, the byte before ExecuteAnimStreamBatch.
    private static int FUN_800366b0(int param_1, uint param_2, ref int param_3, ref int param_4)
    {
        int iVar1;
        uint uVar2;

        uVar2 = (uint)(param_1 - 1);
        if ((uVar2 & 0xffff) == 0)
        {
            param_2 = param_2 - 1;
            if ((param_2 & 0xffff) == 0)
            {
                iVar1 = param_3;
                param_3 = iVar1 + 8;
                iVar1 = PsxRam.ReadI32(iVar1 + 8);
                param_4 = iVar1;
                param_4 = iVar1 + AnimCmdMesh.g_cdFileBaseOffset;
                param_2 = (uint)PsxRam.ReadI32(param_3 + 4);
                uVar2 = param_2 >> 0x10;
            }
            else
            {
                iVar1 = PsxRam.ReadI32(param_3);
                param_4 = iVar1;
                param_4 = iVar1 + AnimCmdMesh.g_cdFileBaseOffset;
                uVar2 = PsxRam.ReadU16(param_3 + 6);
            }
        }

        return (int)(uVar2 * 0x10000) + (short)param_2;
    }

    // =====================================================================================
    // PHASE 4 — the exchange plays out, and the scene ends
    // =====================================================================================

    // GHIDRA: FUN_80036a64 @ 0x80036A64 (VS.EXE)
    // 2312 bytes, 0x80036A64..0x8003736B. One caller: the dispatcher's case 4.
    //
    // FOUR SUB-STEPS on +0x78, and the function increments +0x78 on every path that reaches its
    // tail — so it walks 0, 1, 2, 3 across four frames and then, on sub-step 3, either restarts the
    // whole machine or destroys the task:
    //
    //   0  wait for sound, FUN_800602dc and FUN_8005f530 to all report ready — each returns early
    //      without touching +0x78, so the step repeats until they do. In camera mode it also drains
    //      the target's slot record +0x15B2 by a per-scene amount from the table at 0x80082264,
    //      clamps it at zero, and refills the ki gauge of every slot that still has any left.
    //   1  push the workspace's placement box into all six fighters (camera mode only), set each
    //      one's +0x16A from whether its slot still has +0x15B2, whiten their +0x150..+0x152, and
    //      arm sixteen objects at 0x801FAAAC.
    //   2  re-point the two teams' +0xAC at each other's lead.
    //   3  restore the six positions from the workspace snapshot phase 0 took, then EITHER hand the
    //      whole thing back to phase 0 (+0x76 = 0) because another exchange is queued, OR fall
    //      through to `FUN_8005354c(DAT_8008d16c, 0xc)` — DeleteTask on this very task, on list 12 —
    //      which is how the scene ends.
    //
    // A sub-step outside 0..3 returns without incrementing, and so does sub-step 0 whenever one of
    // its three readiness calls says no. Both are the original's.
    private static void FUN_80036a64()
    {
        int puVar1;
        byte bVar2;
        ushort uVar3;
        bool bVar4;
        int iVar5;
        int iVar6;
        int iVar7;
        int piVar8;
        int iVar9;
        short sVar10;
        int iVar11;
        ushort uVar12;
        int puVar13;

        iVar5 = BattleManager.DAT_8008d320;
        puVar13 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        sVar10 = (short)PsxRam.ReadU16(puVar13 + 0x78);

        if (sVar10 == 1)
        {
            PsxRam.WriteU16(Local18Address, 0x8000);
            AnimCmdEffects.AnimCmd_SetCharRenderState(Local18Address);
            iVar11 = 0;
            iVar5 = 0;
            do
            {
                iVar5 = iVar5 >> 0xe;
                iVar6 = PsxRam.ReadI32(puVar13 + iVar5 + 0x18);
                if (iVar6 != 0)
                {
                    bVar2 = PsxRam.ReadU8(iVar6 + BattleState.FighterSlotIndex);
                    if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
                    {
                        PsxRam.WriteU16(iVar6 + 4, 0);
                        PsxRam.WriteI32(iVar6 + 0x140, 0);
                        PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + iVar5 + 0x18) + BattleState.FighterBoundsMin,
                            PsxRam.ReadU16(puVar13 + 0x64));
                        PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + iVar5 + 0x18) + BattleState.FighterBoundsMin + 2,
                            PsxRam.ReadU16(puVar13 + 0x66));
                        PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + iVar5 + 0x18) + BattleState.FighterBoundsMin + 4,
                            PsxRam.ReadU16(puVar13 + 0x68));
                        PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + iVar5 + 0x18) + BattleState.FighterBoundsMax,
                            PsxRam.ReadU16(puVar13 + 0x6c));
                        PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + iVar5 + 0x18) + BattleState.FighterBoundsMax + 2,
                            PsxRam.ReadU16(puVar13 + 0x6e));
                        PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + iVar5 + 0x18) + BattleState.FighterBoundsMax + 4,
                            PsxRam.ReadU16(puVar13 + 0x70));
                    }

                    if ((short)PsxRam.ReadU16(BattleManager.DAT_8008d320 + bVar2 * BattleState.CtxSlotRecordStride
                            + BattleState.CtxSlotRecords + 2) == 0)
                    {
                        PsxRam.WriteU8(PsxRam.ReadI32(puVar13 + iVar5 + 0x18) + 0x16a, 0x22);
                    }
                    else
                    {
                        PsxRam.WriteU8(PsxRam.ReadI32(puVar13 + iVar5 + 0x18) + 0x16a, 0);
                    }

                    iVar5 = PsxRam.ReadI32(puVar13 + ((iVar11 << 0x10) >> 0xe) + 0x18);
                    PsxRam.WriteU8(iVar5 + 0x152, 0x80);
                    PsxRam.WriteU8(iVar5 + 0x151, 0x80);
                    PsxRam.WriteU8(iVar5 + 0x150, 0x80);
                }

                iVar11 = iVar11 + 1;
                iVar5 = iVar11 * 0x10000;
            }
            while (iVar11 * 0x10000 >> 0x10 < 6);

            piVar8 = DAT_801faaac;
            iVar5 = 0;
            do
            {
                iVar11 = PsxRam.ReadI32(piVar8);
                piVar8 = piVar8 + 4;
                if (iVar11 != 0)
                {
                    PsxRam.WriteU16(iVar11 + 0x50, 1);
                }

                iVar5 = iVar5 + 1;
            }
            while (iVar5 * 0x10000 >> 0x10 < 0x10);

            if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
            {
                FUN_8005ec8c();
            }
        }
        else if (sVar10 < 2)
        {
            if (sVar10 != 0)
            {
                return;
            }

            if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
            {
                FUN_80042054(5, 0x20);
            }

            PsxRam.WriteU16(Local18Address, 0x8000);
            AnimCmdEffects.AnimCmd_SetCharRenderState(Local18Address);
            iVar5 = FUN_800600b0(2);
            if (iVar5 == 1)
            {
                return;
            }

            iVar5 = FUN_800602dc();
            if (iVar5 == 0)
            {
                return;
            }

            iVar5 = FUN_8005f530();
            if (iVar5 == 0)
            {
                return;
            }

            if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
            {
                FUN_8005ed28();
                iVar5 = BattleManager.DAT_8008d320;
                if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 8) != 0)
                {
                    bVar2 = PsxRam.ReadU8(PsxRam.ReadI32(puVar13 + 0x24) + BattleState.FighterSlotIndex);
                    iVar11 = BattleManager.DAT_8008d320 + bVar2 * BattleState.CtxSlotRecordStride;
                    sVar10 = (short)(PsxRam.ReadU16(iVar11 + BattleState.CtxSlotRecords + 2)
                        - PsxRam.ReadU16(DAT_80082264 + PsxRam.ReadU16(puVar13 + 0x74) * 2));
                    PsxRam.WriteU16(iVar11 + BattleState.CtxSlotRecords + 2, (ushort)sVar10);
                    if (((short)PsxRam.ReadU16(puVar13 + 0x74) == 1)
                        && ((short)PsxRam.ReadU16(iVar5 + PsxRam.ReadU16(iVar5 + 0x2dc2)
                                * BattleState.CtxSlotRecordStride + 0x15bc) == 2))
                    {
                        PsxRam.WriteU16(iVar11 + BattleState.CtxSlotRecords + 2, (ushort)(sVar10 + -5));
                    }

                    iVar11 = BattleManager.DAT_8008d320 + (short)(ushort)bVar2 * BattleState.CtxSlotRecordStride;
                    iVar5 = 0;
                    if ((short)PsxRam.ReadU16(iVar11 + BattleState.CtxSlotRecords + 2) < 0)
                    {
                        PsxRam.WriteU16(iVar11 + BattleState.CtxSlotRecords + 2, 0);
                    }

                    iVar11 = BattleManager.DAT_8008d320;
                    iVar6 = 0;
                    do
                    {
                        iVar7 = iVar11 + (iVar6 >> 0x10) * BattleState.CtxSlotRecordStride;
                        if (((PsxRam.ReadU16(iVar7 + BattleState.CtxSlotRecords) & 1) != 0)
                            && ((short)PsxRam.ReadU16(iVar7 + BattleState.CtxSlotRecords + 2) != 0))
                        {
                            PsxRam.WriteU16(iVar7 + BattleState.CtxKiGauge, BattleState.CtxKiGaugeCap);
                            PsxRam.WriteU16(iVar11 + (iVar6 >> 0x10) * 0x1c0 + 0x24, BattleState.CtxKiGaugeCap);
                        }

                        iVar5 = iVar5 + 1;
                        iVar6 = iVar5 * 0x10000;
                    }
                    while (iVar5 * 0x10000 >> 0x10 < 0xc);
                }
            }
        }
        else
        {
            if (sVar10 != 2)
            {
                iVar11 = 0;
                if (sVar10 != 3)
                {
                    return;
                }

                iVar6 = 0;
                do
                {
                    iVar6 = iVar6 >> 0x10;
                    iVar7 = PsxRam.ReadI32(puVar13 + 0x18 + iVar6 * 4);
                    if (iVar7 != 0)
                    {
                        if (((uint)PsxRam.ReadI32(iVar5 + 0x10) & 8) == 0)
                        {
                            PsxRam.WriteU16(iVar7 + 0x114, PsxRam.ReadU16(puVar13 + 0x34 + iVar6 * 8));
                            PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + 0x18 + iVar6 * 4) + 0x116,
                                PsxRam.ReadU16(puVar13 + 0x36 + iVar6 * 8));
                            PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + 0x18 + iVar6 * 4) + 0x118,
                                PsxRam.ReadU16(puVar13 + 0x38 + iVar6 * 8));
                        }
                        else if ((short)PsxRam.ReadU16(iVar5
                                + PsxRam.ReadU8(iVar7 + BattleState.FighterSlotIndex)
                                    * BattleState.CtxSlotRecordStride
                                + BattleState.CtxSlotRecords + 2) == 0)
                        {
                            PsxRam.WriteU16(iVar7 + 0x114, 0x98f1);
                            PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + 0x18 + iVar6 * 4) + 0x116, 0);
                            PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + 0x18 + iVar6 * 4) + 0x118, 0x98f1);
                        }
                        else
                        {
                            iVar9 = iVar6 * 6;
                            PsxRam.WriteU16(iVar7 + 0x114, PsxRam.ReadU16(iVar9 + DAT_800822d0));
                            PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + 0x18 + iVar6 * 4) + 0x116,
                                PsxRam.ReadU16(iVar9 + DAT_800822d0 + 2));
                            PsxRam.WriteU16(PsxRam.ReadI32(puVar13 + 0x18 + iVar6 * 4) + 0x118,
                                PsxRam.ReadU16(iVar9 + DAT_800822d0 + 4));
                        }
                    }

                    iVar7 = BattleManager.DAT_8008d320;
                    iVar11 = iVar11 + 1;
                    uVar12 = (ushort)iVar11;
                    iVar6 = iVar11 * 0x10000;
                }
                while (iVar11 * 0x10000 >> 0x10 < 6);

                if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) & 0x8000000) == 0)
                {
                    iVar5 = FUN_80042054(9, 0);
                    if (iVar5 != 7)
                    {
                        return;
                    }

                    FUN_80042054(4, 0x40);
                    PsxRam.WriteI32(BattleManager.DAT_8008d320 + 0x10,
                        unchecked((int)((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) | 2)));
                    FUN_8005ed70(-1, -1);
                    uVar12 = 0xf;
                }
                else
                {
                    iVar5 = BattleManager.DAT_8008d320 + PsxRam.ReadU16(BattleManager.DAT_8008d320 + 0x2dc2) * BattleState.CtxSlotRecordStride;
                    PsxRam.WriteU16(iVar5 + BattleState.CtxSlotRecords,
                        (ushort)(PsxRam.ReadU16(iVar5 + BattleState.CtxSlotRecords) & 0x7fff));
                    iVar5 = BattleManager.DAT_8008d320;
                    bVar4 = false;
                    if (((uint)PsxRam.ReadI32(iVar7 + 0x2d60) & 0x20) != 0)
                    {
                        iVar6 = 0;
                        iVar11 = 0;
                        do
                        {
                            uVar12 = (ushort)iVar6;
                            uVar3 = PsxRam.ReadU16(iVar7 + (iVar11 >> 0x10) * BattleState.CtxSlotRecordStride
                                + BattleState.CtxSlotRecords);
                            if (((uVar3 & 0x210) == 0x10) && ((uVar3 & 0x8000) != 0))
                            {
                                bVar4 = true;
                                break;
                            }

                            iVar6 = iVar6 + 1;
                            uVar12 = (ushort)iVar6;
                            iVar11 = iVar6 * 0x10000;
                        }
                        while (iVar6 * 0x10000 >> 0x10 < 0xc);

                        if (bVar4)
                        {
                            puVar1 = BattleManager.DAT_8008d320 + 0x2d60;
                            PsxRam.WriteU16(BattleManager.DAT_8008d320 + 0x2dc2, uVar12);
                            PsxRam.WriteI32(iVar5 + 0x2d60,
                                unchecked((int)((uint)PsxRam.ReadI32(puVar1) | 0x80)));

                            // BACK TO PHASE 0: another exchange is queued, so the whole machine
                            // restarts rather than the task being destroyed.
                            PsxRam.WriteU16(puVar13 + 0x76, 0);
                        }
                        else
                        {
                            PsxRam.WriteI32(BattleManager.DAT_8008d320 + 0x2d60,
                                unchecked((int)((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x2d60) & 0xffffffdf | 0x80)));
                        }
                    }

                    iVar5 = BattleManager.DAT_8008d320;
                    if (bVar4)
                    {
                        return;
                    }

                    bVar4 = false;
                    if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x2d60) & 0x40) != 0)
                    {
                        iVar6 = 0;
                        iVar11 = 0;
                        do
                        {
                            uVar12 = (ushort)iVar6;
                            uVar3 = PsxRam.ReadU16(BattleManager.DAT_8008d320 + (iVar11 >> 0x10) * BattleState.CtxSlotRecordStride
                                + BattleState.CtxSlotRecords);
                            if (((uVar3 & 0x210) == 0x210) && ((uVar3 & 0x8000) != 0))
                            {
                                bVar4 = true;
                                break;
                            }

                            iVar6 = iVar6 + 1;
                            uVar12 = (ushort)iVar6;
                            iVar11 = iVar6 * 0x10000;
                        }
                        while (iVar6 * 0x10000 >> 0x10 < 0xc);
                    }

                    if (bVar4)
                    {
                        puVar1 = BattleManager.DAT_8008d320 + 0x2d60;
                        PsxRam.WriteU16(BattleManager.DAT_8008d320 + 0x2dc2, uVar12);
                        PsxRam.WriteI32(iVar5 + 0x2d60, unchecked((int)((uint)PsxRam.ReadI32(puVar1) | 0x100)));
                        PsxRam.WriteU16(puVar13 + 0x76, 0);
                        return;
                    }

                    iVar5 = 0;
                    if (((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x2d60) & 0x40) != 0)
                    {
                        PsxRam.WriteI32(BattleManager.DAT_8008d320 + 0x2d60,
                            unchecked((int)((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x2d60) & 0xffffffbf | 0x100)));
                    }

                    do
                    {
                        iVar11 = (iVar5 << 0x10) >> 0xe;
                        iVar6 = PsxRam.ReadI32(puVar13 + iVar11 + 0x18);
                        if (iVar6 != 0)
                        {
                            PsxRam.WriteI32(iVar6 + 0x134,
                                unchecked((int)((uint)PsxRam.ReadI32(iVar6 + 0x134) & 0xf9ffffff)));
                            iVar11 = PsxRam.ReadI32(puVar13 + iVar11 + 0x18);
                            PsxRam.WriteI32(iVar11 + 0x138,
                                unchecked((int)((uint)PsxRam.ReadI32(iVar11 + 0x138) & 0xf7ffffff)));
                        }

                        iVar5 = iVar5 + 1;
                    }
                    while (iVar5 * 0x10000 >> 0x10 < 6);

                    PsxRam.WriteI32(BattleManager.DAT_8008d320 + 0x10,
                        unchecked((int)((uint)PsxRam.ReadI32(BattleManager.DAT_8008d320 + 0x10) | 0x2000000)));
                    FUN_8005ed70(-1, -1);
                    uVar12 = 5;
                }

                iVar5 = TaskSystem.g_CurrentTask;
                DAT_8008d340 = DAT_8008d340 & 0xffffffbf;
                PsxRam.WriteU16(BattleManager.DAT_8008d320 + 6, uVar12);

                // THE SCENE ENDS HERE: DeleteTask on its own node, list 12. Note the early return —
                // +0x78 is NOT incremented on this path.
                TaskSystem.DeleteTask(iVar5, 0xc);
                return;
            }

            iVar11 = 1;
            iVar5 = 0x10000;
            do
            {
                iVar5 = PsxRam.ReadI32(puVar13 + (iVar5 >> 0xe) + 0x18);
                if (iVar5 != 0)
                {
                    PsxRam.WriteI32(iVar5 + BattleState.FighterTaskNode, PsxRam.ReadI32(puVar13 + 0x0c));
                }

                iVar11 = iVar11 + 1;
                iVar5 = iVar11 * 0x10000;
            }
            while (iVar11 * 0x10000 >> 0x10 < 3);

            iVar11 = 4;
            iVar5 = 0x40000;
            do
            {
                iVar5 = PsxRam.ReadI32(puVar13 + (iVar5 >> 0xe) + 0x18);
                if (iVar5 != 0)
                {
                    PsxRam.WriteI32(iVar5 + BattleState.FighterTaskNode, PsxRam.ReadI32(puVar13));
                }

                iVar11 = iVar11 + 1;
                iVar5 = iVar11 * 0x10000;
            }
            while (iVar11 * 0x10000 >> 0x10 < 6);
        }

        PsxRam.WriteU16(puVar13 + 0x78, (ushort)(PsxRam.ReadU16(puVar13 + 0x78) + 1));
    }

    // =====================================================================================
    // C# bridges to the SDK's primitive helpers
    // =====================================================================================

    // JUSTIFICATION: C# language bridge only
    // RELATION: LibGpu implements SetPolyGT4 / SetPolyGT3 / SetShadeTex / SetSemiTrans in object and
    // byte[]+offset forms, and already carries an ADDRESS form of AddPrim for exactly this reason:
    // the game hands these routines a raw PSX address — here `&DAT_801f7180 + n * 0x34`, a primitive
    // living in the animation workspace, not a managed object. Each of the four resolves the address
    // and calls the SDK's own buffer form; not one line of SDK behaviour is reimplemented here, which
    // is what rule 13 asks.
    private static void SetPolyGT4(int primAddress)
    {
        if (LibGpu.RamResolve(primAddress, out byte[] buffer, out int offset))
        {
            LibGpu.SetPolyGT4(buffer, offset);
        }
    }

    // JUSTIFICATION: C# language bridge only
    private static void SetPolyGT3(int primAddress)
    {
        if (LibGpu.RamResolve(primAddress, out byte[] buffer, out int offset))
        {
            LibGpu.SetPolyGT3(buffer, offset);
        }
    }

    // JUSTIFICATION: C# language bridge only
    private static void SetShadeTex(int primAddress, int tge)
    {
        if (LibGpu.RamResolve(primAddress, out byte[] buffer, out int offset))
        {
            LibGpu.SetShadeTex(buffer, offset, tge);
        }
    }

    // JUSTIFICATION: C# language bridge only
    private static void SetSemiTrans(int primAddress, int abe)
    {
        if (LibGpu.RamResolve(primAddress, out byte[] buffer, out int offset))
        {
            LibGpu.SetSemiTrans(buffer, offset, abe);
        }
    }

    // =====================================================================================
    // NOT IN THIS SLICE
    // =====================================================================================
    // The callees the five phases reach that belong to other subsystems. Declared with their
    // addresses and what the call sites prove about them, rather than silently omitted: the shape of
    // each phase is the deliverable, and a phase that quietly dropped half its calls would not be it.

    // GHIDRA: FUN_80042054 @ 0x80042054 (VS.EXE)
    // BLOCKED. Called with (9,0), (4,0x20), (5,0x20) and (4,0x40) from this file, and with (8,0) and
    // (2,4) from main. Two of those call sites READ THE RESULT and compare it against 7 —
    // RenderBattleScene3D's camera arm and phase 4's sub-step 3 — so the return value is live.
    //
    // DUPLICATE STUB, declared rather than hidden: VS_EXE_exe.cs carries a private `void` stub for
    // the same address, which is why two functions in this port now bear the same `GHIDRA:
    // FUN_80042054` annotation. Neither is a real transliteration, so nothing is silently dead the
    // way FUN_800511a8 was; but the signature differs (this one returns the value the callers read)
    // and the two must become one when the function lands. Reported upward.
    //
    // CONSEQUENCE OF THE STUB, stated so it is not mistaken for a port defect: returning 0 makes
    // both `!= 7` tests fire, so the camera-mode arm of RenderBattleScene3D and the closing arm of
    // phase 4 return early. Both are behind the battle context's +0x10 bit 3, which nothing in this
    // port sets today, so neither is reached in the first place.
    internal static int FUN_80042054(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
        return 0;
    }

    // GHIDRA: FUN_800600b0 @ 0x800600B0 (VS.EXE)
    // BLOCKED: the sound subsystem's stop/query on channel 2. Phase 0 calls it for effect, phase 4
    // sub-step 0 reads its result and returns early on 1, so the value is live.
    internal static int FUN_800600b0(int param_1)
    {
        _ = param_1;
        return 0;
    }

    // GHIDRA: FUN_8005f704 @ 0x8005F704 (VS.EXE)
    // BLOCKED: given (scene id, sub-step) and returns the next sub-step. It is the loader's own
    // step machine and it is what walks phase 1's +0x78 up to 8.
    internal static ushort FUN_8005f704(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
        return 0;
    }

    // GHIDRA: FUN_8005ed28 @ 0x8005ED28 (VS.EXE)
    // BLOCKED: camera mode only. Called by phase 1 once the loader reaches 8, and by phase 4.
    private static void FUN_8005ed28()
    {
    }

    // GHIDRA: FUN_8005ec4c @ 0x8005EC4C (VS.EXE)
    // BLOCKED: camera mode only, fed one byte of the table at 0x8008222C picked by the scene id.
    private static void FUN_8005ec4c(int param_1)
    {
        _ = param_1;
    }

    // GHIDRA: FUN_8005ec8c @ 0x8005EC8C (VS.EXE)
    // BLOCKED: camera mode only, phase 4 sub-step 1.
    private static void FUN_8005ec8c()
    {
    }

    // GHIDRA: FUN_8005ed70 @ 0x8005ED70 (VS.EXE)
    // BLOCKED: always called as (-1, -1) from this file — twice by phase 4 and once by
    // RenderBattleScene3D's camera arm.
    private static void FUN_8005ed70(int param_1, int param_2)
    {
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: FUN_800602dc @ 0x800602DC @ (VS.EXE)
    // BLOCKED: a readiness query phase 4 sub-step 0 waits on. Zero means not ready.
    private static int FUN_800602dc()
    {
        return 0;
    }

    // GHIDRA: FUN_8005f530 @ 0x8005F530 (VS.EXE)
    // BLOCKED: the second readiness query of the same sub-step. Zero means not ready.
    private static int FUN_8005f530()
    {
        return 0;
    }
}
