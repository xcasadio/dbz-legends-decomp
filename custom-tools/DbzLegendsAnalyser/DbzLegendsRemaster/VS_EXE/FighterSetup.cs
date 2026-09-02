using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// PUTTING THE SIX FIGHTERS ON THE FLOOR. Two functions, and they run once, between the battle
// context's own creation and the first frame of the match:
//
//     main @ 0x80062354 ... CreateTask(LAB_80055e3c, 0x51, 9, 0x3034, 0, g_TaskListTail[9])
//                           uVar6 = *(undefined4 *)(iVar7 + 8)
//                           FUN_8005cbe0()      the roster consumer
//                           FUN_80034d98()      memset(&DAT_8008da48, 0, 0xb610) + two uploads
//                           FUN_800511a8(uVar6) <- this file
//
// FUN_800511a8 creates six fighters and files them into the twelve-slot array at context + 0x1520.
// FUN_800512cc creates one: a 0x240-byte task on list 10, then about twenty scalar stores, a
// two-way branch on the word SELECT.EXE handed over, twenty-six interior pointers at
// +0x0C..+0x7C, and one registration through FUN_8003478c.
//
// NOTHING IN THIS FILE DECLARES A SHARED OFFSET. The size of a fighter, the size of the context,
// the slot array, the placement fields, the slot-index byte and the two task entry points are all
// VS_EXE/BattleState.cs's, and they are used from there by name. Offsets BattleState does not name
// are written raw, exactly as Ghidra prints them, rather than given a private constant here that
// would become a second spelling of the same field the moment a sibling slice needs it. The mixed
// look of the body is deliberate: named where the evidence closed a name, raw where it did not.
//
// PARTIAL, and it covers the whole file. The control flow, the offsets and the constants are
// closed — every store below is one instruction in the image. What a fighter's sub-blocks MEAN is
// not: neither the twenty-four self-pointers, nor the two index arguments, nor the six halfwords
// the placement branch writes are interpreted anywhere here. TITLE.EXE carries the same C source
// relinked (FUN_8004737c / FUN_800474a0 / FUN_800350f4, ported in TITLE_EXE/SecondScreenSetup.cs)
// and it closed no more than this. Nothing below calls into that overlay: it is a separate link at
// separate addresses, and a call into it would make every `GHIDRA:` line in this file false.
//
// WIRING GAP, inherited and stated rather than papered over: PsxSdkBridges installs
// PsxRam.AddressResolver per overlay and still has no VS.EXE row, so the reads and writes below
// resolve to nothing and answer zero until one exists. That is the same gap AnimVm, AnimCmdMesh and
// FileIo already record, and closing it means editing PsxSdkBridges and VS_EXE_exe, not this file.
internal static class FighterSetup
{
    // GHIDRA: DAT_8008da48 @ 0x8008DA48 (VS.EXE)
    // Six slots of 0x1E58 bytes, 0x8008DA48..0x80099057. The extent is CLOSED, not assumed, by two
    // readings that agree: FUN_80034d98 @ 0x80034D98 does `memset(&DAT_8008da48, '\0', 0xb610)`,
    // and 0xB610 = 6 * 0x1E58 is exactly the stride and count FUN_8003478c walks below.
    //
    // OWNERSHIP CAVEAT, in the shape VS_EXE/AnimCmdMesh.cs and VS_EXE/FileIo.cs already use. This
    // block is not this file's alone — FUN_80034d98 clears it and lives in VS_EXE_exe.cs, where it
    // is still a BLOCKED stub. It is declared here because this is the first VS.EXE code to reach
    // it. When that memset lands it must use THIS array, not a second one at the same address:
    // two declarations of one PSX address is the exact defect BattleState.cs exists to prevent.
    //
    // A plain byte[] rather than LibGpu.RamRegion, for the reason TITLE_EXE/SecondScreenSetup.cs
    // gives for the same block in that overlay: nothing in this cluster AddPrims out of it.
    private const int Dat8008da48Address = unchecked((int)0x8008DA48);

    internal static readonly byte[] DAT_8008da48 = new byte[0xb610];

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: this class's row for the overlay address resolver, in the shape TITLE_EXE_exe's
    // per-module `Resolve` chain uses and VS_EXE/AnimVm.cs already copies. Not installed anywhere
    // yet — see the wiring gap noted above.
    internal static (byte[] Buffer, int Offset)? Resolve(int address)
    {
        int offset = address - Dat8008da48Address;
        return offset >= 0 && offset < DAT_8008da48.Length ? (DAT_8008da48, offset) : null;
    }

    // GHIDRA: FUN_800511a8 @ 0x800511A8 (VS.EXE)
    // Six calls to FUN_800512cc and six zeroed slots, in one straight run with no loop. `param_1`
    // is the +0x08 workspace of the 0x3034-byte task main creates on list 9 — main reads that field
    // inline and hands it over; there is no accessor for it in the original and none is invented
    // here.
    //
    // The twelve destinations are BattleState.CtxFighterSlots + 0x00 .. + 0x2C, spelled below as
    // that base plus the byte offset so the absolute value stays readable against Ghidra:
    //     +0x00 = 0x1520   +0x04 = 0x1524   +0x08 = 0x1528   +0x0C = 0x152C
    //     +0x10 = 0x1530   +0x14 = 0x1534   +0x18 = 0x1538   +0x1C = 0x153C
    //     +0x20 = 0x1540   +0x24 = 0x1544   +0x28 = 0x1548   +0x2C = 0x154C
    //
    // The (param_2, param_3) pairs are (0,0) (1,1) (2,2) (3,6) (4,7) (5,8): the first counts 0..5
    // without a gap, the second skips 3, 4 and 5. What the two numbers select is NOT closed by
    // anything read here, and nothing below interprets them.
    //
    // The store ORDER is the original's and is kept: the three zero stores of each half run
    // downwards (0x1534, 0x1530, 0x152C — then 0x154C, 0x1548, 0x1544) and they come after the
    // group of creations they follow, not before.
    internal static void FUN_800511a8(int param_1)
    {
        int uVar1;

        uVar1 = FUN_800512cc(param_1, 0, 0);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x00, uVar1);
        uVar1 = FUN_800512cc(param_1, 1, 1);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x04, uVar1);
        uVar1 = FUN_800512cc(param_1, 2, 2);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x08, uVar1);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x14, 0);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x10, 0);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x0c, 0);
        uVar1 = FUN_800512cc(param_1, 3, 6);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x18, uVar1);
        uVar1 = FUN_800512cc(param_1, 4, 7);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x1c, uVar1);
        uVar1 = FUN_800512cc(param_1, 5, 8);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x20, uVar1);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x2c, 0);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x28, 0);
        PsxRam.WriteI32(param_1 + BattleState.CtxFighterSlots + 0x24, 0);
    }

    // GHIDRA: FUN_800512cc @ 0x800512CC (VS.EXE)
    // One fighter. Returns the task node, or 0 when CreateTask refused, and the caller stores that
    // return straight into the slot array — so a failed creation leaves a zero slot there, which is
    // the same value the six unused slots carry. The original does not distinguish the two cases
    // and nothing here does either.
    //
    // `DAT_80083bb8` is g_TaskListTail[10]: the tail array starts at 0x80083B90 and
    // (0x80083BB8 - 0x80083B90) / 4 = 10, matching the list index 10 in the same argument list.
    //
    // `iVar2 = *(int *)(puVar1 + 4)` is `undefined2 *` arithmetic, so it is node + 8 — verified in
    // the image, `lw v1,0x8(v0)` at 0x80051350. That is TaskSystem's +0x08 context field, so iVar2
    // is the 0x240-byte workspace. The read is written inline, as the original writes it; there is
    // no accessor for that field in the original and inventing one is what rule 15 forbids.
    //
    // `*puVar1` in the final call is the halfword at node + 0x00, TaskSystem's TaskId, verified as
    // `lhu v0,0x0(v1)` at 0x80051710 — and CreateTask was passed id 0 four lines above, so what
    // FUN_8003478c receives there is 0 on all six calls.
    //
    // `iVar2 + 300` and `iVar2 + 200` are decimal in Ghidra's output, that is +0x12C and +0xC8;
    // `iVar2 + 100` is the destination +0x64. Ghidra's spelling is kept so the line-for-line
    // comparison holds.
    //
    // PARTIAL, and worth stating precisely because BattleState.cs's own note is narrower: the
    // self-pointer table is NOT seven entries at +0x0C..+0x24. It is TWENTY-SIX stores covering
    // +0x0C..+0x7C — counted off the decompilation, which leaves exactly three words of that span
    // alone: +0x50, +0x60 and +0x6C. BattleState names the first seven because those are the ones
    // whose targets it also names; the rest are written raw below.
    //
    // Two of the twenty-six do not point INTO the workspace the way the others do: +0x34 takes the
    // workspace base itself, and +0x64 takes the task NODE, which is the second of the two places
    // that node is filed after +0xAC. And +0x10 and +0x18 both point at +0x114: that is the
    // original's, not a transcription slip, and it is reproduced under rule 12 rather than
    // corrected. The store ORDER is the original's too — it runs 0x0C..0x48 ascending, then doubles
    // back to 0x30, 0x54, 0x64, 0x68, 0x4C, 0x70, 0x58, 0x5C, 0x74, 0x78, 0x7C — and it is kept.
    internal static int FUN_800512cc(int param_1, ushort param_2, byte param_3)
    {
        int puVar1;
        int iVar2;

        puVar1 = TaskSystem.CreateTask(
            BattleState.FighterEntry, 0, 10, BattleState.FighterSize, 1, TaskSystem.g_TaskListTail[10]);
        if (puVar1 != 0)
        {
            iVar2 = PsxRam.ReadI32(puVar1 + 8);
            PsxRam.WriteI32(iVar2 + BattleState.FighterTaskNode, puVar1);
            PsxRam.WriteI32(iVar2 + BattleState.FighterBattleContext, param_1);
            PsxRam.WriteU8(iVar2 + BattleState.FighterSlotIndex, param_3);
            PsxRam.WriteU16(iVar2 + 0x154, 0);
            PsxRam.WriteU16(iVar2 + BattleState.FighterZeroedFrom114, 0);
            PsxRam.WriteU16(iVar2 + 0x164, 0);
            PsxRam.WriteU16(iVar2 + 0x116, 0);
            PsxRam.WriteU16(iVar2 + 0x166, 0);
            PsxRam.WriteU16(iVar2 + 0x118, 0);
            PsxRam.WriteU16(iVar2 + 0x168, 0);
            PsxRam.WriteU16(iVar2 + 0x11c, 0);
            PsxRam.WriteU16(iVar2 + 0x11e, 0);
            PsxRam.WriteU16(iVar2 + 0x120, 0);

            // DAT_801ff100 @ 0x801FF100, the word SELECT.EXE hands over. It is a 16-bit global
            // inside the shared high-RAM span SharedHighRam models (base 0x801FF000, 0x248 bytes),
            // so it is short index 0x80 of SHORT_ARRAY_801ff000 — the spelling
            // SELECT_EXE/CharacterSelect.cs writes it with and TITLE_EXE/SecondScreenSetup.cs reads
            // it with. Nothing new is declared for it here.
            if (SharedHighRam.SHORT_ARRAY_801ff000[0x80] == 1)
            {
                PsxRam.WriteU16(iVar2 + BattleState.FighterPlacementScale, 20000);
                PsxRam.WriteU16(iVar2 + 0xba, 0x78);
                PsxRam.WriteU16(iVar2 + 0xbc, 20000);
                PsxRam.WriteU16(iVar2 + BattleState.FighterPlacementRot, 0xb1e0);
                PsxRam.WriteU16(iVar2 + 0xb2, 0xf448);
                PsxRam.WriteU16(iVar2 + 0xb4, 0xb1e0);
            }
            else
            {
                PsxRam.WriteU16(iVar2 + BattleState.FighterPlacementScale, 0x1e0);
                PsxRam.WriteU16(iVar2 + 0xba, 0x78);
                PsxRam.WriteU16(iVar2 + 0xbc, 0x1e0);
                PsxRam.WriteU16(iVar2 + BattleState.FighterPlacementRot, 0xfe20);
                PsxRam.WriteU16(iVar2 + 0xb2, 0xfd00);
                PsxRam.WriteU16(iVar2 + 0xb4, 0xfe20);
            }

            PsxRam.WriteU16(iVar2 + 0x110, 0);
            PsxRam.WriteI32(iVar2 + BattleState.FighterTaskNodeAlias, puVar1);
            PsxRam.WriteI32(iVar2 + 0xf8, 0);
            PsxRam.WriteI32(iVar2 + 0xfc, 0);
            PsxRam.WriteI32(iVar2 + BattleState.FighterSelfPointers, iVar2 + 0x10);
            PsxRam.WriteI32(iVar2 + 0x10, iVar2 + BattleState.FighterZeroedFrom114);
            PsxRam.WriteI32(iVar2 + 0x14, iVar2 + 0x11c);
            PsxRam.WriteI32(iVar2 + 0x18, iVar2 + BattleState.FighterZeroedFrom114);
            PsxRam.WriteI32(iVar2 + 0x1c, iVar2 + BattleState.FighterBlock80);
            PsxRam.WriteI32(iVar2 + 0x20, iVar2 + BattleState.FighterBlockD0);
            PsxRam.WriteI32(iVar2 + 0x24, iVar2 + BattleState.FighterPlacement);
            PsxRam.WriteI32(iVar2 + 0x28, iVar2 + BattleState.FighterPlacementScale);
            PsxRam.WriteI32(iVar2 + 0x2c, iVar2 + 0x134);
            PsxRam.WriteI32(iVar2 + 0x34, iVar2);
            PsxRam.WriteI32(iVar2 + 0x38, iVar2 + 0xf4);
            PsxRam.WriteI32(iVar2 + 0x3c, iVar2 + 0x124);
            PsxRam.WriteI32(iVar2 + 0x40, iVar2 + 300);
            PsxRam.WriteI32(iVar2 + 0x44, iVar2 + 0xe0);
            PsxRam.WriteI32(iVar2 + 0x48, iVar2 + 0xc0);
            PsxRam.WriteI32(iVar2 + 0x30, iVar2 + 0x16b);
            PsxRam.WriteI32(iVar2 + 0x54, iVar2 + 0x16a);
            PsxRam.WriteI32(iVar2 + 100, puVar1);
            PsxRam.WriteI32(iVar2 + 0x68, iVar2 + 0x160);
            PsxRam.WriteI32(iVar2 + 0x4c, iVar2 + 0x162);
            PsxRam.WriteI32(iVar2 + 0x70, iVar2 + 0x138);
            PsxRam.WriteI32(iVar2 + 0x58, iVar2 + 0x226);
            PsxRam.WriteI32(iVar2 + 0x5c, iVar2 + 0x176);
            PsxRam.WriteI32(iVar2 + 0x74, iVar2 + 200);
            PsxRam.WriteI32(iVar2 + 0x78, iVar2 + 0x228);
            PsxRam.WriteI32(iVar2 + 0x7c, iVar2 + 0x224);
            PsxRam.WriteU16(iVar2 + 0x160, param_2);
            PsxRam.WriteI32(iVar2 + 0x144, 0);
            PsxRam.WriteU8(iVar2 + 0x174, 0);
            FUN_8003478c(PsxRam.ReadU16(puVar1), PsxRam.ReadU8(iVar2 + BattleState.FighterSlotIndex));
        }

        return puVar1;
    }

    // GHIDRA: FUN_8003478c @ 0x8003478C (VS.EXE)
    // Scans the six 0x1E58 slots at DAT_8008da48 FROM THE LAST ONE DOWN, takes the first whose
    // leading int is zero, and writes two halfwords into its head: param_1 & 0xff at +0x00 and
    // param_2 & 0xff at +0x02. Reports 0 on success and 0xFFFFFFFF when all six are taken. On the
    // six calls FUN_800512cc makes, param_1 is the task id (always 0) and param_2 the slot index,
    // so what lands in the slot head is (0, 0) (0, 1) (0, 2) (0, 6) (0, 7) (0, 8).
    //
    // The scan uses the magic constant -0x7ff74410 and the exit path uses the symbol. The two
    // agree exactly: with iVar4 = 0xb610 the first form gives 0xb610 + 0x8008BBF0 = 0x80097200, and
    // 0x8008DA48 + 5 * 0x1E58 is the same address. Both spellings are kept, because both are what
    // the machine does.
    //
    // The masks are &0xff on values already narrowed to a byte by the caller — the slot index comes
    // out of a `lbu` — and the store is `sh`, a HALFWORD, so a byte value is written into two
    // bytes. That is what the image does at 0x800347F4 and 0x80034800; rule 12, reproduced, not
    // tidied.
    //
    // PARTIAL: what a 0x1E58-byte slot holds is not closed by anything read here. Only its head is
    // touched, and only the leading int is tested for occupancy.
    internal static uint FUN_8003478c(ushort param_1, ushort param_2)
    {
        int piVar1;
        uint uVar2;
        int iVar3;
        int iVar4;
        int puVar5;

        iVar3 = 6;
        iVar4 = 0xb610;
        do
        {
            iVar3 = iVar3 + -1;
            if (iVar3 < 0)
            {
                puVar5 = 0;
                goto LAB_800347e8;
            }

            piVar1 = unchecked(iVar4 + -0x7ff74410);
            iVar4 = iVar4 + -0x1e58;
        }
        while (PsxRam.ReadI32(piVar1) != 0);

        puVar5 = unchecked(Dat8008da48Address + (iVar3 * 0x1e58));

    LAB_800347e8:
        uVar2 = 0;
        if (puVar5 == 0)
        {
            uVar2 = 0xffffffff;
        }
        else
        {
            PsxRam.WriteU16(puVar5, (ushort)(param_1 & 0xff));
            PsxRam.WriteU16(puVar5 + 2, (ushort)(param_2 & 0xff));
        }

        return uVar2;
    }
}
