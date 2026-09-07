using PsxSdkMonogame;
using static PsxSdkMonogame.LibEtc;

namespace DbzLegendsRemaster.VS_EXE;

// THE CPU CONTROLLER — 0x80023890..0x80026737, the AI-side twin of FighterInput.cs.
//
// WHAT THIS FAMILY IS FOR. FighterTask.SelectFighterCommand produces the frame's COMMAND WORD for
// every fighter. For a fighter marked pad-driven it calls FighterInput.ReadFighterPadCommand; for
// every other fighter it calls FUN_80023890 below, and the two return values live in the SAME
// vocabulary — step 9.4 of the fighter task routes on them without knowing which side produced
// them. The opcodes this file returns are exactly the ones FighterInput.cs's header enumerates:
// 0x13/0x14, 0x17, 0x1c, 0x1d, 0x21, 0x23..0x28, 0x2a, plus 2, 10 and 0 and the "no command" -1.
//
// THE THREE-LEVEL BEHAVIOUR TABLE. FUN_80023890 picks nine byte-table pointers per frame:
//
//   row      = *(int*)(0x800807A4 + fighter[0x22D] * 4)        -- one 12-byte profile row
//   ptr[k]   = *(int*)(tableBase[k] + row[k] * 4),  k = 0..8
//   tableBase = { 0x80080204, 0x8008021C, 0x80080234, 0x8008024C, 0x80080264,
//                 0x8008027C, 0x80080294, 0x800802AC, 0x800802C4 }
//
// fighter+0x22D is the AI profile index FighterSubstitution.cs documents (its step 5: "the slot's
// sub-record +0x0C ... the byte FUN_80023890 uses to pick a behaviour profile row out of
// PTR_DAT_800807A4"). FighterCombat.FUN_80025F38 already walks the FIRST TWO levels of the same
// structure, and reads them straight through PsxRam at the raw addresses rather than embedding
// them; this file does the same, for the reason spelled out under WHAT IS NOT EMBEDDED below.
//
// WHAT IS CLOSED ABOUT THE TABLES, measured with read-memory against the image, not assumed:
//   * The nine pointer tables are CONTIGUOUS and six entries each. 0x80080204 + 6*4 = 0x8008021C,
//     which is the next table's own base, and so on for all nine; the ninth ends at
//     0x800802C4 + 6*4 = 0x800802DC.
//   * 0x800802DC is exactly PTR_DAT_800807A4[0], so the PROFILE ROWS begin where the ninth pointer
//     table ends. Successive entries of PTR_DAT_800807A4 step by 12 (0x800802DC, 0x800802E8,
//     0x800802F4, 0x80080300, ...), so a row is 12 bytes of which the code reads the first nine.
//   * The six entries of table 0 are 0x8007FF34, 0x8007FF38, 0x8007FF3C, 0x8007FF40, 0x8007FF44,
//     0x8007FF48 — four bytes apart, i.e. four-byte leaves; table 2's are eight apart, table 3's
//     twenty apart, table 8's 0x24 apart. The leaf widths differ per table, which is consistent
//     with the byte indices the callees below use (local_44[3] is read as far as [0x10],
//     local_44[8] as far as [0x1E + 4]).
//
// PARTIAL: WHAT IS NOT CLOSED, and therefore WHAT IS NOT EMBEDDED. The LENGTH of
// PTR_DAT_800807A4 is not closed by anything read for this port — the entries stay row-shaped for
// at least the 64 read, and no next symbol bounds them — and the leaf tables' lengths are bounded
// only from below, by the largest index a callee happens to use. Embedding either would be
// embedding a guess. So NOTHING here is embedded: every table read goes through PsxRam at the raw
// PSX address, which VS_EXE_exe.ResolveAddress answers from PsxExeImage — the image's own .data,
// the same bytes the console reads. Nothing about a length has to be decided to do that.
//
// THE 48-BYTE ARGUMENT BLOCK, and why one C# struct is the faithful shape. FUN_80023890's locals
// local_4c, local_48[2] and local_44[13] occupy sp+0x4C onward, and the decompilation contains TEN
// identical do/while loops that each copy 48 bytes (sp+0x4C..sp+0x7B) to sp+0x10. sp+0x10 is the
// MIPS o32 OUTGOING-ARGUMENT AREA: arguments 1..4 travel in a0..a3 and everything past them is
// stored there. Every one of those loops therefore is not program logic at all — it is the
// compiler storing arguments 5..16 immediately before a call, which is why each is followed by
// `jal`. That is settled by the callees: they name their extra arguments in_stack_00000010,
// in_stack_00000014, in_stack_00000018 ... in_stack_00000038, and the offsets line up word for
// word with local_4c, local_48, local_44[0], ... local_44[8]. The disassembly at 0x800239B4
// confirms the a1/a2/a3 reloads that follow each loop: `lw a1,0x40(sp)` `lw a2,0x44(sp)`
// `lw a3,0x48(sp)` — local_58, local_54, local_50.
//
// So the block is modelled as ONE by-value struct, StackArgs, and each of the ten copy loops is
// one by-value pass of it. Nothing is dropped and nothing is invented: a C# struct pass copies the
// same twelve words the loop copies, in the same place in the instruction stream.
//
// OWNERSHIP. FUN_80045CF4 lives in AnimCmdMesh.cs and rand() in PsxSdkMonogame; both are called by
// qualified name rather than redeclared. PadInput.DAT_8008d518 is likewise this port's single
// storage for 0x8008D518 and is reached through PadInput, not re-declared.
//
// DUPLICATE-ADDRESS NOTE, reported rather than fixed because this wave may write only this file:
// FighterTask.cs still carries its own `private static int FUN_80023890(int param_1)` BLOCKED stub
// (annotated `// GHIDRA: FUN_80023890 @ 0x80023890 (VS.EXE)`). Because C# binds the unqualified
// calls inside FighterTask.SelectFighterCommand to the enclosing class first, THAT STUB STILL WINS
// and this file is dead code until it is deleted and the three call sites are re-pointed at
// FighterAi.FUN_80023890. That is precisely the defect the address checker exists to catch, and it
// will report it.
internal static class FighterAi
{
    // JUSTIFICATION: C# language bridge only
    // RELATION: the 48-byte MIPS o32 outgoing-argument block at sp+0x10..sp+0x3B that FUN_80023890
    // builds from its own sp+0x4C..sp+0x7B locals before each of its ten calls, and that
    // FUN_80024C78 passes on unchanged to FUN_80025570.
    //
    // The field names are FUN_80023890's own Ghidra locals, because FUN_80023890 is the only writer
    // of them. Each field's comment gives the offset the CALLEES see it at, which is how their own
    // in_stack_000000XX names map onto it:
    //
    //   local_4c   -> in_stack_00000010 (= param_5 where a callee has one)
    //   local_48   -> in_stack_00000014 (= param_6, a short, i.e. local_48[0])
    //   local_44_0 -> in_stack_00000018        local_44_5 -> in_stack_0000002c
    //   local_44_1 -> in_stack_0000001c        local_44_6 -> in_stack_00000030
    //   local_44_2 -> in_stack_00000020        local_44_7 -> in_stack_00000034
    //   local_44_3 -> in_stack_00000024        local_44_8 -> in_stack_00000038
    //   local_44_4 -> in_stack_00000028        local_44_9 -> in_stack_0000003c
    private struct StackArgs
    {
        // sp+0x10. Low half: the distance AnimCmdMesh.FUN_80045cf4 returns between the two
        // fighters' position triples. High half: the acting fighter's Ki gauge divided by 100.
        public uint local_4c;

        // sp+0x14, low half: the absolute difference of the two fighters' +0x116 halfwords.
        public ushort local_48_0;

        // sp+0x14, high half. NEVER WRITTEN by FUN_80023890 — the original leaves whatever the
        // stack held. No callee reads it (they all take offset 0x14 as a `short`).
        public ushort local_48_1;

        // sp+0x18..sp+0x38 — the nine behaviour-table leaf POINTERS, as PSX addresses.
        public uint local_44_0;
        public uint local_44_1;
        public uint local_44_2;
        public uint local_44_3;
        public uint local_44_4;
        public uint local_44_5;
        public uint local_44_6;
        public uint local_44_7;
        public uint local_44_8;

        // DEVIATION: sp+0x3C. local_44[9] is inside the 48 bytes every copy loop moves but is
        // NEVER WRITTEN by FUN_80023890, so on the console it is uninitialised stack. No callee
        // reads offset 0x3C, so it is inert; C# forces it to 0 where the original forces nothing.
        public uint local_44_9;
    }

    // GHIDRA: FUN_80023890 @ 0x80023890 (VS.EXE)
    // 5096 bytes, 629 decompiled lines, ten callees. THE CPU CONTROLLER. Its three call sites are
    // all inside FighterTask.SelectFighterCommand, and param_1 is the fighter workspace.
    //
    // SHAPE, top to bottom, reproduced in this order and not reordered:
    //   1. local_58 = *(int*)(*(int*)(fighter+0xAC) + 8) — the OPPONENT, reached through this
    //      fighter's task node (BattleState.FighterTaskNode) and its +8 field.
    //   2. A per-frame countdown of +0x238, gated on the state byte and on +0x138 bits 8..14.
    //   3. Four early exits: self-as-opponent, either side's +0x138 bit 0x4000000, then the
    //      FUN_800264D8 side call, then +0x138's 0x200FF/0x20 pair and bit 0x80000.
    //   4. The +0x22C bookkeeping bits and the two frame counters +0x234 / +0x236.
    //   5. The distance / gauge / height triple, then the nine-pointer table walk.
    //   6. THREE DECODERS chosen exactly as FighterInput.cs's own three are: +0x138 & 0x7F00,
    //      else +0x138 & 0x200FF, else the state-byte switch that ends in FUN_80024C78.
    //
    // PARTIAL: the +0x138 bit GROUPS and the +0x22C bookkeeping bits are named after their masks,
    // not after a meaning — nothing read for this wave establishes what state they describe. Same
    // posture, and for the same reason, as FighterInput.cs's header takes on the identical masks.
    //
    // OFFSETS. BattleState covers +0xAC (FighterTaskNode), +0xF0 (FighterBattleContext), +0x160
    // (FighterIndex), +0x173 (FighterSlotIndex), +0x114 (FighterZeroedFrom114) and the context's
    // +0x15B4 / stride 0x14 (CtxKiGauge, CtxSlotRecordStride), and those are used below. It has no
    // name for +0x116, +0x138, +0x16A, +0x16B, +0x224, +0x22C, +0x22D, +0x230..+0x238, so those
    // stay raw literals here. +0x22C, +0x22D and the +0x231/+0x232/+0x233 bit-history bytes are
    // used by enough of this family that they deserve BattleState names; adding them is not this
    // file's to do.
    internal static int FUN_80023890(int param_1)
    {
        short sVar1;
        byte bVar2;
        int iVar3;
        byte bVar4;
        ushort uVar5;
        uint pbVar6;
        uint uVar7;
        int iVar8;
        sbyte cVar11;

        // DEVIATION: `char unaff_s0` in the decompilation is a read of the SAVED REGISTER s0 before
        // anything in this function writes it — the two difficulty ladders below leave it untouched
        // when DAT_801FF01C is none of 0, 1, 2, and the second ladder inherits whatever the first
        // left. A single C# local reproduces the inheritance exactly. It cannot reproduce the
        // uninitialised first read, which C# forbids; 0 is chosen because it makes the guarded
        // `rand() % 0x65 < unaff_s0` fail, i.e. it takes the no-action arm.
        sbyte unaff_s0 = 0;

        StackArgs local_88 = default;
        int local_58;
        uint local_54;
        uint local_50;

        local_58 = PsxRam.ReadI32(PsxRam.ReadI32(param_1 + BattleState.FighterTaskNode) + 8);
        local_54 = PsxRam.ReadU8(param_1 + 0x16a);

        // The decompiler prints this as the SIGNED `1 < *(byte*)(fighter+0x16a) - 0x1d`, which is
        // not what the image does. 0x800238D0 is `addiu v0,v0,-0x1D` followed at 0x800238D4 by
        // `sltiu v0,v0,2` — an UNSIGNED compare, so a state byte BELOW 0x1D wraps to a huge value
        // and takes the decrementing arm, where the signed reading would skip it. Written unsigned.
        if (unchecked((uint)(PsxRam.ReadU8(param_1 + 0x16a) - 0x1d)) >= 2
            && ((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x7f00) == 0)
        {
            PsxRam.WriteU16(param_1 + 0x238, (ushort)((short)PsxRam.ReadU16(param_1 + 0x238) - 1));
        }

        if (param_1 == local_58)
        {
            goto LAB_8002393c;
        }

        if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x4000000) != 0)
        {
            return -1;
        }

        if (((uint)PsxRam.ReadI32(local_58 + 0x138) & 0x4000000) != 0)
        {
            goto LAB_8002393c;
        }

        // (&DAT_801ff058)[fighter+0x160] — SharedHighRam's six-byte 0x801FF058..0x801FF05D block,
        // indexed by the FIGHTER index (0..5, per BattleState.FighterIndex), so the index is in
        // range by construction. The comparison is against -0x32, i.e. the element is a signed
        // char; SharedHighRam models the block as bytes, so the read is cast.
        if ((sbyte)SharedHighRam.RAM_801ff000[0x58 + (short)PsxRam.ReadU16(param_1 + BattleState.FighterIndex)] == -0x32)
        {
            // DEVIATION: the copy loop that builds this call's argument block runs BEFORE local_4c,
            // local_48 and local_44[] are ever assigned — they are first written 40 lines further
            // down. On the console FUN_800264D8 is therefore handed 48 bytes of stack garbage, and
            // so is local_50, whose own assignment also comes later. It reads none of them, which
            // is why the bug is invisible; it is reproduced rather than tidied away, and `default`
            // (zeros) is the only initial value C# will let this port give it.
            FUN_800264d8(param_1, local_58, local_54, local_50 = 0, local_88);
        }

        if ((((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x200ff) != 0)
            && (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x20) != 0))
        {
            return -1;
        }

        if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x80000) != 0)
        {
            return -1;
        }

        if ((PsxRam.ReadU8(param_1 + 0x22c) & 1) == 0)
        {
            PsxRam.WriteU16(param_1 + 0x234, (ushort)((short)PsxRam.ReadU16(param_1 + 0x234) + 1));
            PsxRam.WriteU16(param_1 + 0x236, (ushort)((short)PsxRam.ReadU16(param_1 + 0x236) + 1));

            if ((PsxRam.ReadU8(param_1 + 0x22c) & 2) == 0)
            {
                if ((short)PsxRam.ReadU16(param_1 + 0x234) < 0x28)
                {
                    return -1;
                }

                if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x27fff) == 0)
                {
                    PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 6));
                    PsxRam.WriteU16(param_1 + 0x236, 0);
                    PsxRam.WriteU16(param_1 + 0x234, 0);
                }

                return -1;
            }
        }
        else
        {
            PsxRam.WriteU8(param_1 + 0x22c, (byte)((PsxRam.ReadU8(param_1 + 0x22c) & 0xfe) | 6));
            PsxRam.WriteU16(param_1 + 0x236, 0);
            PsxRam.WriteU16(param_1 + 0x234, 0);

            if ((sbyte)PsxRam.ReadU8(param_1 + 0x16a) != '\n')
            {
                PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0xf7));
            }

            // The shift-in of a one-bit history at +0x231. The original stores the shifted byte
            // UNCONDITIONALLY first and then stores it a second time inside both arms of the test,
            // which is redundant on the "clear" side; kept as written.
            bVar4 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x231) << 1);
            PsxRam.WriteU8(param_1 + 0x231, bVar4);

            if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x200) == 0)
            {
                PsxRam.WriteU8(param_1 + 0x231, bVar4);
            }
            else
            {
                PsxRam.WriteU8(param_1 + 0x231, (byte)(bVar4 | 1));
            }
        }

        local_50 = PsxRam.ReadU8(param_1 + 0x16b);

        // Only the LOW half of local_4c is written here; the high half is still stack garbage until
        // the second assignment three statements later. Nothing reads it in between.
        local_88.local_4c =
            (local_88.local_4c & 0xffff0000u)
            | (uint)(ushort)AnimCmdMesh.FUN_80045cf4(
                param_1 + BattleState.FighterZeroedFrom114,
                local_58 + BattleState.FighterZeroedFrom114);

        iVar3 = (int)(short)PsxRam.ReadU16(param_1 + 0x116) - (int)(short)PsxRam.ReadU16(local_58 + 0x116);
        if (iVar3 < 0)
        {
            iVar3 = -iVar3;
        }

        local_88.local_48_0 = (ushort)(short)iVar3;

        local_88.local_4c =
            (local_88.local_4c & 0x0000ffffu)
            | ((uint)(ushort)(short)((short)PsxRam.ReadU16(
                    PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                    + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
                    + BattleState.CtxKiGauge) / 100) << 16);

        bVar4 = PsxRam.ReadU8(param_1 + 0x22c);
        iVar3 = -1;

        if ((bVar4 & 0x10) != 0)
        {
            bVar2 = (byte)(bVar4 | 0x40);
            if ((short)PsxRam.ReadU16(param_1 + 0x238) < 1)
            {
                bVar2 = (byte)(bVar4 & 0xaf);
            }

            PsxRam.WriteU8(param_1 + 0x22c, bVar2);
            bVar4 = PsxRam.ReadU8(param_1 + 0x22c);
        }

        if ((bVar4 & 0xc0) == 0)
        {
            // THE THREE-LEVEL WALK. Nine table bases, nine row bytes, nine leaf pointers.
            pbVar6 = (uint)PsxRam.ReadI32(unchecked((int)0x800807a4) + PsxRam.ReadU8(param_1 + 0x22d) * 4);
            local_88.local_44_0 = (uint)PsxRam.ReadI32(unchecked((int)0x80080204) + PsxRam.ReadU8((int)pbVar6) * 4);
            local_88.local_44_1 = (uint)PsxRam.ReadI32(unchecked((int)0x8008021c) + PsxRam.ReadU8((int)pbVar6 + 1) * 4);
            local_88.local_44_2 = (uint)PsxRam.ReadI32(unchecked((int)0x80080234) + PsxRam.ReadU8((int)pbVar6 + 2) * 4);
            local_88.local_44_3 = (uint)PsxRam.ReadI32(unchecked((int)0x8008024c) + PsxRam.ReadU8((int)pbVar6 + 3) * 4);
            local_88.local_44_4 = (uint)PsxRam.ReadI32(unchecked((int)0x80080264) + PsxRam.ReadU8((int)pbVar6 + 4) * 4);
            local_88.local_44_5 = (uint)PsxRam.ReadI32(unchecked((int)0x8008027c) + PsxRam.ReadU8((int)pbVar6 + 5) * 4);
            local_88.local_44_6 = (uint)PsxRam.ReadI32(unchecked((int)0x80080294) + PsxRam.ReadU8((int)pbVar6 + 6) * 4);
            local_88.local_44_7 = (uint)PsxRam.ReadI32(unchecked((int)0x800802ac) + PsxRam.ReadU8((int)pbVar6 + 7) * 4);
            local_88.local_44_8 = (uint)PsxRam.ReadI32(unchecked((int)0x800802c4) + PsxRam.ReadU8((int)pbVar6 + 8) * 4);
        }
        else
        {
            // The two OVERRIDE rows. These are not table walks: they are nine POINTER VARIABLES,
            // read straight out of 0x80080A38.. and 0x80080A5C.. — the same pair of blocks
            // FighterCombat.FUN_80025F38 reaches, and for the same +0x22C bit-6/bit-7 reason.
            if ((bVar4 & 0x40) == 0)
            {
                local_88.local_44_0 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a5c));
                local_88.local_44_1 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a60));
                local_88.local_44_2 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a64));
                local_88.local_44_3 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a68));
                local_88.local_44_4 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a6c));
                local_88.local_44_5 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a70));
                local_88.local_44_6 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a74));
                local_88.local_44_7 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a78));
                local_88.local_44_8 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a7c));
            }
            else
            {
                local_88.local_44_0 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a38));
                local_88.local_44_1 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a3c));
                local_88.local_44_2 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a40));
                local_88.local_44_3 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a44));
                local_88.local_44_4 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a48));
                local_88.local_44_5 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a4c));
                local_88.local_44_6 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a50));
                local_88.local_44_7 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a54));
                local_88.local_44_8 = (uint)PsxRam.ReadI32(unchecked((int)0x80080a58));
            }
        }

        uVar7 = (uint)PsxRam.ReadI32(param_1 + 0x138);

        // ================= DECODER 1: +0x138 bits 8..14 =================
        if ((uVar7 & 0x7f00) != 0)
        {
            if ((uVar7 & 0x40000) != 0
                && (PsxRam.ReadU8(param_1 + 0x22c) & 4) != 0
                && Store22cAnd(param_1, 0xfb)
                && (short)(local_88.local_4c >> 16) < (short)(ushort)PsxRam.ReadU8((int)local_88.local_44_2)
                && (iVar3 = Kernel.rand()) % 0x65 < (int)(uint)PsxRam.ReadU8((int)local_88.local_44_2 + 1))
            {
                PsxRam.WriteI32(param_1 + 0x138, unchecked((int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffbffff)));
            }

            uVar7 = (uint)PsxRam.ReadI32(param_1 + 0x138);

            if ((uVar7 & 0x3c00) == 0)
            {
                bVar4 = PsxRam.ReadU8(param_1 + 0x22c);
                if ((bVar4 & 2) == 0)
                {
                    return -1;
                }

                if ((bVar4 & 0x40) == 0 || (uVar7 & 0x100) == 0)
                {
                    PsxRam.WriteU8(param_1 + 0x22c, (byte)(bVar4 & 0xfd));

                    if ((bVar4 & 0xc0) != 0)
                    {
                        return -1;
                    }

                    if ((bVar4 & 0x10) != 0)
                    {
                        return -1;
                    }

                    // Popcount of the eight-frame history byte at +0x231. Eight passes: iVar3 runs
                    // 7,6,5,4,3,2,1,0 and stops after it reaches -1. The shift is the original's
                    // sign-extend-then-arithmetic-shift pair, kept literal.
                    cVar11 = 0;
                    iVar3 = 7;
                    uVar7 = PsxRam.ReadU8(param_1 + 0x231);
                    do
                    {
                        iVar3 = iVar3 + -1;
                        if ((uVar7 & 1) != 0)
                        {
                            cVar11 = (sbyte)(cVar11 + 1);
                        }

                        uVar7 = unchecked((uint)((int)(uVar7 << 0x18) >> 0x19));
                    }
                    while (-1 < iVar3);

                    if (5 < cVar11)
                    {
                        if (SharedHighRam.DAT_801ff01c == 1)
                        {
                            unaff_s0 = (sbyte)'(';
                        }
                        else if (SharedHighRam.DAT_801ff01c < 2)
                        {
                            if (SharedHighRam.DAT_801ff01c == 0)
                            {
                                unaff_s0 = 0x1e;
                            }
                        }
                        else if (SharedHighRam.DAT_801ff01c == 2)
                        {
                            unaff_s0 = (sbyte)'2';
                        }

                        iVar3 = Kernel.rand();
                        if (iVar3 % 0x65 < (int)unaff_s0)
                        {
                            PsxRam.WriteU16(param_1 + 0x238, 0xa0);
                            PsxRam.WriteU8(param_1 + 0x231, 0);
                            PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 0x10));
                        }
                    }

                    // The SAME popcount again, this time over the OPPONENT's +0x232 byte.
                    cVar11 = 0;
                    iVar3 = 7;
                    uVar7 = PsxRam.ReadU8(local_58 + 0x232);
                    do
                    {
                        iVar3 = iVar3 + -1;
                        if ((uVar7 & 1) != 0)
                        {
                            cVar11 = (sbyte)(cVar11 + 1);
                        }

                        uVar7 = unchecked((uint)((int)(uVar7 << 0x18) >> 0x19));
                    }
                    while (-1 < iVar3);

                    if (cVar11 < 5)
                    {
                        return -1;
                    }

                    if (SharedHighRam.DAT_801ff01c == 1)
                    {
                        unaff_s0 = (sbyte)'(';
                    }
                    else if (SharedHighRam.DAT_801ff01c < 2)
                    {
                        if (SharedHighRam.DAT_801ff01c == 0)
                        {
                            unaff_s0 = 0x1e;
                        }
                    }
                    else if (SharedHighRam.DAT_801ff01c == 2)
                    {
                        unaff_s0 = (sbyte)'2';
                    }

                    iVar3 = Kernel.rand();
                    if ((int)unaff_s0 <= iVar3 % 0x65)
                    {
                        return -1;
                    }

                    PsxRam.WriteU16(param_1 + 0x238, 100);
                    PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 0x10));
                    PsxRam.WriteU8(local_58 + 0x232, 0);
                    return -1;
                }
            }
            else
            {
                if ((PsxRam.ReadU8(param_1 + 0x22c) & 2) == 0)
                {
                    return -1;
                }

                if ((PsxRam.ReadU8(param_1 + 0x22c) & 0x40) != 0 && (uVar7 & 0x3800) != 0)
                {
                    goto LAB_8002411c;
                }
            }

            if ((short)PsxRam.ReadU16(param_1 + 0x234) < (short)(ushort)PsxRam.ReadU8((int)local_88.local_44_0))
            {
                return -1;
            }

        LAB_8002411c:
            PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0xfd));

            if ((short)(local_88.local_4c >> 16) < 0x14)
            {
                return -1;
            }

            iVar3 = FUN_80025b10(param_1, local_58, local_54, local_50, local_88) ? 1 : 0;
            if (iVar3 == 0)
            {
                return -1;
            }

            FUN_80025dc4(param_1);
            return 0x1c;
        }

        // ================= DECODER 2: +0x138 bits 0..7 and 17 =================
        if ((uVar7 & 0x200ff) != 0)
        {
            if ((uVar7 & 0x40000) != 0
                && (PsxRam.ReadU8(param_1 + 0x22c) & 4) != 0
                && Store22cAnd(param_1, 0xfb)
                && (short)(local_88.local_4c >> 16) < (short)(ushort)PsxRam.ReadU8((int)local_88.local_44_2 + 2)
                && (iVar3 = Kernel.rand()) % 0x65 < (int)(uint)PsxRam.ReadU8((int)local_88.local_44_2 + 3))
            {
                PsxRam.WriteI32(param_1 + 0x138, unchecked((int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffbffff)));
            }

            uVar7 = (uint)PsxRam.ReadI32(param_1 + 0x138);

            if ((uVar7 & 2) != 0)
            {
                if ((short)local_88.local_4c < 0x30)
                {
                    uVar5 = PsxRam.ReadU8((int)local_88.local_44_3 + 0xf);
                }
                else if ((short)local_88.local_4c < 0x60)
                {
                    uVar5 = PsxRam.ReadU8((int)local_88.local_44_4 + 10);
                }
                else
                {
                    // NOTE: this arm OVERWRITES local_44[6] with local_44[5] rather than using a
                    // temporary — the mirrored code in the 2/10/0x1B switch arm below does use a
                    // temporary. Both are reproduced as written. Every path out of this branch
                    // returns, so the overwrite is not observable further down.
                    if ((short)local_88.local_4c < 0x200)
                    {
                        local_88.local_44_6 = local_88.local_44_5;
                    }

                    uVar5 = PsxRam.ReadU8((int)local_88.local_44_6 + 5);
                }

                if ((sbyte)PsxRam.ReadU8(param_1 + 0x224) < 0x1e
                    && (short)PsxRam.ReadU16(param_1 + 0x234) < (short)uVar5)
                {
                    return -1;
                }

                PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0xfd));
                PsxRam.WriteI32(
                    param_1 + 0x138,
                    unchecked((int)(((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffffffd) | 4)));
                return -1;
            }

            if ((uVar7 & 8) == 0)
            {
                if ((uVar7 & 0x10) != 0)
                {
                    if (((uint)PsxRam.ReadI32(local_58 + 0x138) & 0x3800) == 0)
                    {
                        return -1;
                    }

                    if ((uVar7 & 0x100000) == 0)
                    {
                        return -1;
                    }

                    if ((PsxRam.ReadU8(param_1 + 0x22c) & 0x40) == 0)
                    {
                        sVar1 = (short)PsxRam.ReadU16(param_1 + 0x234);
                        iVar3 = Kernel.rand();
                        if ((int)sVar1 < iVar3 % 100)
                        {
                            return -1;
                        }
                    }
                    else if ((short)PsxRam.ReadU16(param_1 + 0x236) < 0x10)
                    {
                        return -1;
                    }

                    iVar3 = FUN_8002575c(param_1, local_58, local_54, local_50, local_88);
                    if (iVar3 == -1)
                    {
                        PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0xfd));

                        iVar3 = FUN_80025a3c(param_1, local_58, local_54, local_50, local_88) ? 1 : 0;
                        if (iVar3 != 0)
                        {
                            // Ghidra prints an `if (true)` around this switch; it is the
                            // compiler's own range check, not a branch of the program.
                            switch (local_54)
                            {
                                case 0x23:
                                    local_88.local_44_8 = local_88.local_44_8 + 0x18;
                                    iVar3 = FUN_8002631c(param_1, local_88.local_44_8);
                                    return iVar3;
                                case 0x25:
                                    local_88.local_44_8 = local_88.local_44_8 + 0x12;
                                    iVar3 = FUN_8002631c(param_1, local_88.local_44_8);
                                    return iVar3;
                                case 0x26:
                                    local_88.local_44_8 = local_88.local_44_8 + 6;
                                    iVar3 = FUN_8002631c(param_1, local_88.local_44_8);
                                    return iVar3;
                                case 0x27:
                                    local_88.local_44_8 = local_88.local_44_8 + 0xc;
                                    iVar3 = FUN_8002631c(param_1, local_88.local_44_8);
                                    return iVar3;
                                case 0x28:
                                    iVar3 = FUN_8002631c(param_1, local_88.local_44_8);
                                    return iVar3;
                            }

                            local_88.local_44_8 = local_88.local_44_8 + 0x1e;
                            iVar3 = FUN_8002631c(param_1, local_88.local_44_8);
                            return iVar3;
                        }

                        return -1;
                    }

                    return iVar3;
                }

                if (((uint)PsxRam.ReadI32(local_58 + 0x138) & 0x100) != 0)
                {
                    if ((short)PsxRam.ReadU16(param_1 + 0x236) < (short)(ushort)PsxRam.ReadU8((int)local_88.local_44_3 + 4))
                    {
                        return -1;
                    }

                    PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0xfd));

                    if (0x5f < (short)local_88.local_4c)
                    {
                        return -1;
                    }

                    iVar3 = Kernel.rand();
                    iVar3 = (iVar3 % 0x65) * 0x1000000 >> 0x18;

                    // local_44[3][5] is read SIGNED here and at the next accumulation, while [6]
                    // and [7] are read unsigned. That asymmetry is the original's.
                    if (iVar3 - (sbyte)PsxRam.ReadU8((int)local_88.local_44_3 + 5) < 0)
                    {
                        return 0x25;
                    }

                    iVar8 = (int)(sbyte)PsxRam.ReadU8((int)local_88.local_44_3 + 5)
                            + (int)(uint)PsxRam.ReadU8((int)local_88.local_44_3 + 6);
                    if (iVar3 - (iVar8 * 0x1000000 >> 0x18) < 0)
                    {
                        return 0x23;
                    }

                    if (-1 < iVar3 - (unchecked((iVar8 + (int)(uint)PsxRam.ReadU8((int)local_88.local_44_3 + 7)) * 0x1000000) >> 0x18))
                    {
                        return -1;
                    }

                    return 0x24;
                }
            }
            else if (((uint)PsxRam.ReadI32(local_58 + 0x138) & 0x400) != 0 && (uVar7 & 0x100000) != 0)
            {
                if ((PsxRam.ReadU8(param_1 + 0x22c) & 0x40) == 0)
                {
                    sVar1 = (short)PsxRam.ReadU16(param_1 + 0x236);
                    iVar3 = Kernel.rand();
                    if ((int)sVar1 < iVar3 % 100)
                    {
                        return -1;
                    }
                }
                else if ((short)PsxRam.ReadU16(param_1 + 0x236) < 4)
                {
                    return -1;
                }

                iVar3 = FUN_8002575c(param_1, local_58, local_54, local_50, local_88);
                if (iVar3 != -1)
                {
                    return iVar3;
                }

                PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0xfd));

                iVar3 = FUN_80025a3c(param_1, local_58, local_54, local_50, local_88) ? 1 : 0;
                if (iVar3 == 0)
                {
                    return -1;
                }

                return 0x2a;
            }

            PsxRam.WriteU16(param_1 + 0x236, 0);
            return -1;
        }

        // ================= DECODER 3: the state-byte switch =================
        // Ghidra hoists the switch's DEFAULT arm out as an `if (false) { switchD_80024818_caseD_3: }`
        // block reached only by `default: goto`. It is written below as the default arm itself; no
        // statement is added, removed or reordered by doing so.
        switch (local_54)
        {
            case 0:
            case 1:
            case 0x29:
                FUN_80025494(param_1, local_58, local_54, local_50, local_88);

                if ((short)PsxRam.ReadU16(param_1 + 0x236) < (short)(ushort)PsxRam.ReadU8((int)local_88.local_44_1))
                {
                    return -1;
                }

                PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0xfd));
                break;

            case 2:
            case 10:
            case 0x1b:
                if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x40000) != 0
                    && (PsxRam.ReadU8(param_1 + 0x22c) & 4) != 0
                    && Store22cAnd(param_1, 0xfb)
                    && (short)(local_88.local_4c >> 16) < (short)(ushort)PsxRam.ReadU8((int)local_88.local_44_2 + 4)
                    && (iVar3 = Kernel.rand()) % 0x65 < (int)(uint)PsxRam.ReadU8((int)local_88.local_44_2 + 5))
                {
                    PsxRam.WriteI32(
                        param_1 + 0x138,
                        unchecked((int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffbffff)));
                }

                if (local_54 == 10 && (PsxRam.ReadU8(param_1 + 0x22c) & 8) != 0)
                {
                    if ((short)PsxRam.ReadU16(param_1 + 0x236) < 0x14)
                    {
                        return -1;
                    }

                    PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0xf7));
                    return 0x1d;
                }

                if ((short)local_88.local_4c < 0x30)
                {
                    uVar5 = PsxRam.ReadU8((int)local_88.local_44_3 + 0x10);
                }
                else if ((short)local_88.local_4c < 0x60)
                {
                    uVar5 = PsxRam.ReadU8((int)local_88.local_44_4 + 0xb);
                }
                else
                {
                    // The temporary the mirrored arm in decoder 2 does NOT use. Kept as written.
                    pbVar6 = local_88.local_44_6;
                    if ((short)local_88.local_4c < 0x200)
                    {
                        pbVar6 = local_88.local_44_5;
                    }

                    uVar5 = PsxRam.ReadU8((int)pbVar6 + 6);
                }

                if ((short)PsxRam.ReadU16(param_1 + 0x236) < (short)uVar5)
                {
                    return -1;
                }

                break;

            case 0x17:
                FUN_80025494(param_1, local_58, local_54, local_50, local_88);

                if ((short)PsxRam.ReadU16(param_1 + 0x236) < 10)
                {
                    return -1;
                }

                break;

            case 0x1e:
                iVar3 = 0x1d;
                goto case 0x1d;

            case 0x1d:
                // FALLTHROUGH from 0x1E in the original, reproduced with `goto case`. Reaching 0x1D
                // directly leaves iVar3 at the -1 assigned before the table walk, which is the
                // value the two `return iVar3` exits below then yield.
                PsxRam.WriteI32(
                    param_1 + 0x138,
                    unchecked((int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffbffff)));

                if ((short)(local_88.local_4c >> 16) < 0xa0)
                {
                    return iVar3;
                }

                if ((PsxRam.ReadU8(param_1 + 0x22c) & 0x80) != 0)
                {
                    if ((VS_EXE_exe.DAT_8008d444 & 0xf) != 0)
                    {
                        return iVar3;
                    }

                    return 0;
                }

                break;

            default:
                FUN_80025494(param_1, local_58, local_54, local_50, local_88);
                break;
        }

        iVar3 = FUN_80024c78(param_1, local_58, local_54, local_50, local_88);
        return iVar3;

    LAB_8002393c:
        // `-(local_54 - 0x1d < 2 ^ 1)`: 0 when the state byte is 0x1D or 0x1E, -1 otherwise. The
        // subtraction is unsigned, same `sltiu` as at the head of the function.
        return -((local_54 - 0x1d < 2 ? 1 : 0) ^ 1);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: the `*(byte*)(p+0x22c) = *(byte*)(p+0x22c) & mask` STORE that Ghidra folds into the
    // middle of a `&&` chain (`(*(byte *)(param_1 + 0x22c) = ... & 0xfb, <comparison>)`, three
    // sites). C# has no comma operator, so the store cannot be written inline in the condition; it
    // is performed here and `true` returned so the chain's short-circuit order is unchanged. The
    // store is unconditional at that point in the chain in the original too — it happens whenever
    // the two preceding tests pass and before the two that follow, which is exactly what this
    // preserves. It adds no logic of its own.
    private static bool Store22cAnd(int param_1, byte mask)
    {
        PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & mask));
        return true;
    }

    // GHIDRA: FUN_80024c78 @ 0x80024C78 (VS.EXE)
    // 2076 bytes. One caller, FUN_80023890's decoder-3 exit above. The RANGE DECODER: it splits on
    // the distance (param_5's low half) at 0x30 / 0x60 / 0x200 and rolls one `rand() % 101` against
    // a cumulative run of percentage bytes to pick the command.
    //
    // in_stack_00000024 = local_44_3, in_stack_00000028 = local_44_4, in_stack_00000030 =
    // local_44_6. param_5 = local_4c (low half distance, high half gauge/100), param_6 = local_48_0.
    //
    // NOTE THE COLUMN ASYMMETRY, kept as written: the near band (< 0x30) reads its cumulative run
    // from in_stack_00000024[8..0xE], the mid band from in_stack_00000028[4..9]. The two bands
    // share LAB_80024F34 and LAB_800251B4, so the near band's [0xD] test jumps into the mid band's
    // "return 2" and its [0xE] into the shared tail.
    //
    // The far band (>= 0x200) falls through the whole chain to a re-copy of the argument block and
    // FUN_80025570 whenever the distance is under 0x200 — i.e. the 0x60..0x1FF band has no roll of
    // its own here at all, it delegates. Same for either near band once param_6 >= 0x19.
    private static int FUN_80024c78(int param_1, int param_2, uint param_3, uint param_4, StackArgs args)
    {
        int iVar1;
        uint uVar2;
        uint uVar5;
        int iVar9;
        uint uVar10;

        uint param_5 = args.local_4c;
        short param_6 = (short)args.local_48_0;
        uint in_stack_00000024 = args.local_44_3;
        uint in_stack_00000028 = args.local_44_4;
        uint in_stack_00000030 = args.local_44_6;

        iVar1 = Kernel.rand();
        iVar9 = -1;
        uVar10 = (uint)(iVar1 % 0x65);

        if ((short)param_5 < 0x30)
        {
            if ((short)(ushort)PsxRam.ReadU8((int)in_stack_00000024) <= (short)(param_5 >> 16))
            {
                if (param_6 < 0x19)
                {
                    if (((uint)PsxRam.ReadI32(param_2 + 0x138) & 0x200ff) != 0
                        && (iVar1 = Kernel.rand()) % 0x65 < (int)(uint)PsxRam.ReadU8((int)in_stack_00000024 + 3))
                    {
                        goto LAB_80025100;
                    }

                    uVar2 = uVar10 & 0xff;
                    if (uVar2 < PsxRam.ReadU8((int)in_stack_00000024 + 8))
                    {
                        goto LAB_8002512c;
                    }

                    uVar5 = (uint)PsxRam.ReadU8((int)in_stack_00000024 + 8)
                            + (uint)PsxRam.ReadU8((int)in_stack_00000024 + 9);
                    if (uVar2 < (uVar5 & 0xff))
                    {
                        goto LAB_80025150;
                    }

                    uVar5 = uVar5 + PsxRam.ReadU8((int)in_stack_00000024 + 10);
                    if (uVar2 < (uVar5 & 0xff))
                    {
                        goto LAB_80025174;
                    }

                    uVar5 = uVar5 + PsxRam.ReadU8((int)in_stack_00000024 + 0xb);
                    if (uVar2 < (uVar5 & 0xff))
                    {
                        iVar9 = 0x14;
                        uVar10 = (uint)Kernel.rand();
                        if ((uVar10 & 1) != 0)
                        {
                            iVar9 = 0x13;
                        }

                        goto LAB_800253c4;
                    }

                    uVar5 = uVar5 + PsxRam.ReadU8((int)in_stack_00000024 + 0xc);
                    if ((uVar5 & 0xff) <= uVar2)
                    {
                        uVar5 = uVar5 + PsxRam.ReadU8((int)in_stack_00000024 + 0xd);
                        if (uVar2 < (uVar5 & 0xff))
                        {
                            goto LAB_800251b4;
                        }

                        uVar5 = PsxRam.ReadU8((int)in_stack_00000024 + 0xe) + uVar5;
                        goto LAB_80024f34;
                    }

                    goto LAB_800252ec;
                }

                goto LAB_80025244;
            }

            if (((uint)PsxRam.ReadI32(param_2 + 0x138) & 0x4000) != 0)
            {
                goto LAB_800252c8;
            }

            iVar1 = Kernel.rand();
            if ((int)(uint)PsxRam.ReadU8((int)in_stack_00000024 + 1) <= iVar1 % 0x65)
            {
                goto LAB_800253c4;
            }

            PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 8));
            iVar9 = 10;
            if ((short)(param_5 >> 16) < 0x14)
            {
                goto LAB_800253c4;
            }

            iVar1 = Kernel.rand();
            uVar10 = PsxRam.ReadU8((int)in_stack_00000024 + 2);
            iVar1 = iVar1 % 0x65;
        }
        else if ((short)param_5 < 0x60)
        {
            if ((short)(ushort)PsxRam.ReadU8((int)in_stack_00000028) <= (short)(param_5 >> 16))
            {
                if (param_6 < 0x19)
                {
                    if (((uint)PsxRam.ReadI32(param_2 + 0x138) & 0x200ff) != 0
                        && (iVar1 = Kernel.rand()) % 0x65 < (int)(uint)PsxRam.ReadU8((int)in_stack_00000028 + 3))
                    {
                        goto LAB_80025100;
                    }

                    uVar2 = uVar10 & 0xff;
                    if (uVar2 < PsxRam.ReadU8((int)in_stack_00000028 + 4))
                    {
                        goto LAB_8002512c;
                    }

                    uVar5 = (uint)PsxRam.ReadU8((int)in_stack_00000028 + 4)
                            + (uint)PsxRam.ReadU8((int)in_stack_00000028 + 5);
                    if (uVar2 < (uVar5 & 0xff))
                    {
                        goto LAB_80025150;
                    }

                    uVar5 = uVar5 + PsxRam.ReadU8((int)in_stack_00000028 + 6);
                    if (uVar2 < (uVar5 & 0xff))
                    {
                        goto LAB_80025174;
                    }

                    uVar5 = uVar5 + PsxRam.ReadU8((int)in_stack_00000028 + 7);
                    if ((uVar5 & 0xff) <= uVar2)
                    {
                        uVar5 = uVar5 + PsxRam.ReadU8((int)in_stack_00000028 + 8);
                        if ((uVar5 & 0xff) <= uVar2)
                        {
                            uVar5 = PsxRam.ReadU8((int)in_stack_00000028 + 9) + uVar5;
                            goto LAB_80024f34;
                        }

                        goto LAB_800251b4;
                    }

                    goto LAB_800252ec;
                }

                goto LAB_80025244;
            }

            if (((uint)PsxRam.ReadI32(param_2 + 0x138) & 0x4000) != 0)
            {
                goto LAB_800252c8;
            }

            iVar1 = Kernel.rand();
            if ((int)(uint)PsxRam.ReadU8((int)in_stack_00000028 + 1) <= iVar1 % 0x65)
            {
                goto LAB_800253c4;
            }

            PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 8));
            iVar9 = 10;
            if ((short)(param_5 >> 16) < 0x14)
            {
                goto LAB_800253c4;
            }

            iVar1 = Kernel.rand();
            uVar10 = PsxRam.ReadU8((int)in_stack_00000028 + 2);
            iVar1 = iVar1 % 0x65;
        }
        else
        {
            if ((short)param_5 < 0x200)
            {
                goto LAB_80025244;
            }

            if ((short)(param_5 >> 16) < (short)(ushort)PsxRam.ReadU8((int)in_stack_00000030)
                && (iVar1 = Kernel.rand()) % 0x65 < (int)(uint)PsxRam.ReadU8((int)in_stack_00000030 + 1))
            {
                goto LAB_800252c8;
            }

            uVar10 = uVar10 & 0xff;
            if (uVar10 < PsxRam.ReadU8((int)in_stack_00000030 + 2))
            {
                goto LAB_800252ec;
            }

            uVar2 = (uint)PsxRam.ReadU8((int)in_stack_00000030 + 2)
                    + (uint)PsxRam.ReadU8((int)in_stack_00000030 + 3);
            if ((uVar2 & 0xff) <= uVar10)
            {
                if (uVar10 < ((PsxRam.ReadU8((int)in_stack_00000030 + 4) + uVar2) & 0xff))
                {
                    iVar9 = 10;
                    goto LAB_800253c4;
                }

                goto LAB_800253b4;
            }

            iVar9 = 2;
            if ((short)(param_5 >> 16) < (short)(ushort)PsxRam.ReadU8((int)in_stack_00000030 + 7))
            {
                goto LAB_800253c4;
            }

            iVar1 = Kernel.rand();
            uVar10 = PsxRam.ReadU8((int)in_stack_00000030 + 8);
            iVar1 = iVar1 % 0x65;
        }

        if (iVar1 < (int)uVar10)
        {
            PsxRam.WriteI32(param_1 + 0x138, unchecked((int)((uint)PsxRam.ReadI32(param_1 + 0x138) | 0x40000)));
        }

    LAB_800253c4:
        // The +0x22C bit-7 REMAP. When that bit is set, three of the picked opcodes are rewritten:
        // 0x26 -> 0x17, 0x28 -> 0x1D, and 10 -> 0x13/0x14 at long range. 0x21 additionally runs
        // FUN_80025DC4 and becomes 0x1C — the same pairing FUN_80023890's own decoder-1 exit makes.
        if ((PsxRam.ReadU8(param_1 + 0x22c) & 0x80) != 0)
        {
            if (iVar9 == 0x26)
            {
                iVar9 = 0x17;
            }
            else if (iVar9 < 0x27)
            {
                if (iVar9 == 10)
                {
                    if (0x60 < (short)param_5)
                    {
                        uVar10 = (uint)Kernel.rand();
                        iVar9 = 0x14;
                        if ((uVar10 & 1) != 0)
                        {
                            iVar9 = 0x13;
                        }
                    }
                }
                else if (iVar9 == 0x21)
                {
                    FUN_80025dc4(param_1);
                    iVar9 = 0x1c;
                    PsxRam.WriteI32(
                        param_1 + 0x138,
                        unchecked((int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfff7ffff)));
                }
            }
            else if (iVar9 == 0x28)
            {
                iVar9 = 0x1d;
            }
        }

        return iVar9;

        // THE SHARED LABEL BODIES. In the image these sit inline, at the point the decompilation
        // shows them; C# forbids a `goto` from jumping INTO a nested block, so each is hoisted to
        // method scope here, unchanged and in the decompilation's own order. Every variable they
        // touch (iVar9, uVar5, uVar10) is a method-level local, so no value crosses a scope it did
        // not already cross in the original.
    LAB_80024f34:
        if ((uVar10 & 0xff) < (uVar5 & 0xff))
        {
            iVar9 = 10;
            goto LAB_800253c4;
        }

        goto LAB_800253b4;

    LAB_80025100:
        iVar9 = 0x17;
        goto LAB_800253c4;

    LAB_8002512c:
        iVar9 = 0x28;
        goto LAB_800253c4;

    LAB_80025150:
        iVar9 = 0x26;
        goto LAB_800253c4;

    LAB_80025174:
        iVar9 = 0x27;
        goto LAB_800253c4;

    LAB_800251b4:
        iVar9 = 2;
        goto LAB_800253c4;

    LAB_80025244:
        // The block is copied through unchanged; FUN_80025570 reads in_stack_0000002c out of it,
        // which FUN_80024C78 itself never touches. FUN_80025570's return is `undefined4` in Ghidra
        // and yields 0xFFFFFFFF for "no command"; the cast is the reinterpretation the image does
        // for free by leaving the value in $v0, not a change of value.
        iVar9 = unchecked((int)FUN_80025570(param_1, param_2, param_3, param_4, args));
        goto LAB_800253c4;

    LAB_800252c8:
        iVar9 = 0x1d;
        goto LAB_800253c4;

    LAB_800252ec:
        iVar9 = 0x21;
        goto LAB_800253c4;

    LAB_800253b4:
        PsxRam.WriteU16(param_1 + 0x236, 0);
        PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 2));
        goto LAB_800253c4;
    }

    // GHIDRA: FUN_80025494 @ 0x80025494 (VS.EXE)
    // 220 bytes. Three callers, all in FUN_80023890's decoder-3 switch (cases 0/1/0x29, case 0x17,
    // and the default arm). Returns nothing — a pure side effect on +0x22C bit 2 and +0x138 bit 18.
    // in_stack_00000010 = local_4c, in_stack_00000020 = local_44_2.
    //
    // This is the same "clear +0x22C bit 2, then maybe clear +0x138 bit 0x40000" pair that
    // FUN_80023890 open-codes at the head of its decoder 1 and decoder 2, and again in the switch's
    // 2/10/0x1B arm — four columns of the same table (offsets 0/1, 2/3, 4/5, and here 6/7 of
    // local_44_2). Note the guard shape differs from the open-coded copies: here the +0x22C store
    // happens INSIDE the `if`, after the distance test, where in the open-coded copies it happens
    // between the second and third test. Reproduced as written rather than unified.
    private static void FUN_80025494(int param_1, int param_2, uint param_3, uint param_4, StackArgs args)
    {
        int iVar1;

        uint in_stack_00000010 = args.local_4c;
        uint in_stack_00000020 = args.local_44_2;

        if (((uint)PsxRam.ReadI32(param_1 + 0x138) & 0x40000) != 0
            && (PsxRam.ReadU8(param_1 + 0x22c) & 4) != 0
            && (short)(in_stack_00000010 >> 16) < (short)(ushort)PsxRam.ReadU8((int)in_stack_00000020 + 6))
        {
            PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0xfb));
            iVar1 = Kernel.rand();
            if (iVar1 % 0x65 < (int)(uint)PsxRam.ReadU8((int)in_stack_00000020 + 7))
            {
                PsxRam.WriteI32(
                    param_1 + 0x138,
                    unchecked((int)((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffbffff)));
            }
        }
    }

    // GHIDRA: FUN_80025570 @ 0x80025570 (VS.EXE)
    // 492 bytes. One caller, FUN_80024C78's LAB_80025244 — the mid/far band's delegate and the
    // near/mid bands' `param_6 >= 0x19` delegate. in_stack_0000002c = local_44_5,
    // in_stack_00000010 = local_4c.
    //
    // NOTE THE FIRST rand(): it is called BEFORE the guard that may never use it, and its value is
    // what the cumulative run below is rolled against — the second rand() inside the guard is a
    // different roll. Order preserved.
    private static uint FUN_80025570(int param_1, int param_2, uint param_3, uint param_4, StackArgs args)
    {
        int iVar1;
        int iVar2;
        uint uVar3;
        uint uVar4;
        uint uVar5;

        uint in_stack_00000010 = args.local_4c;
        uint in_stack_0000002c = args.local_44_5;

        iVar1 = Kernel.rand();
        uVar5 = 0xffffffff;

        if ((short)(in_stack_00000010 >> 16) < (short)(ushort)PsxRam.ReadU8((int)in_stack_0000002c)
            && (iVar2 = Kernel.rand()) % 0x65 < (int)(uint)PsxRam.ReadU8((int)in_stack_0000002c + 1))
        {
            uVar5 = 0x1d;
        }
        else
        {
            uVar4 = (uint)(iVar1 % 0x65) & 0xff;
            if (uVar4 < PsxRam.ReadU8((int)in_stack_0000002c + 2))
            {
                uVar5 = 0x21;
            }
            else
            {
                uVar3 = (uint)PsxRam.ReadU8((int)in_stack_0000002c + 2)
                        + (uint)PsxRam.ReadU8((int)in_stack_0000002c + 3);
                if (uVar4 < (uVar3 & 0xff))
                {
                    uVar5 = 2;
                    if ((short)(ushort)PsxRam.ReadU8((int)in_stack_0000002c + 7) <= (short)(in_stack_00000010 >> 16)
                        && (iVar1 = Kernel.rand()) % 0x65 < (int)(uint)PsxRam.ReadU8((int)in_stack_0000002c + 8))
                    {
                        PsxRam.WriteI32(
                            param_1 + 0x138,
                            unchecked((int)((uint)PsxRam.ReadI32(param_1 + 0x138) | 0x40000)));
                    }
                }
                else if (uVar4 < ((PsxRam.ReadU8((int)in_stack_0000002c + 4) + uVar3) & 0xff))
                {
                    uVar5 = 10;
                }
                else
                {
                    PsxRam.WriteU16(param_1 + 0x236, 0);
                    PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 2));
                }
            }
        }

        return uVar5;
    }

    // GHIDRA: FUN_8002575c @ 0x8002575C (VS.EXE)
    // 736 bytes. Two callers, both in FUN_80023890's decoder 2 (the +0x138 bit 4 arm and the bit 3
    // arm). Returns 0x1D, 10, or -1. in_stack_00000024/28/2c/30 = local_44_3/4/5/6.
    //
    // The four distance bands here are the SAME 0x30 / 0x60 / 0x200 splits FUN_80024C78 uses, but
    // read column 0 and 1 of a DIFFERENT set of leaves. Only the two near bands can return 10 and
    // set +0x22C bit 3; the two far ones only ever return 0x1D or -1.
    //
    // THE FIRST rand() RESULT IS DISCARDED — `rand();` with no assignment, at 0x80025784. It is the
    // original's, kept: the sequence advance is observable through every later roll in the frame.
    //
    // `iVar2 + ((iVar1 >> 4) - (iVar2 >> 0x1f)) * -0x65` with `iVar1` the high word of
    // `iVar2 * 0x288DF0CB` is the compiler's magic-number form of `iVar2 % 0x65`. Written literally
    // rather than folded, so that the port cannot silently disagree with the image on a corner case.
    private static int FUN_8002575c(int param_1, int param_2, uint param_3, uint param_4, StackArgs args)
    {
        int iVar1;
        int iVar2;

        uint param_5 = args.local_4c;
        uint in_stack_00000024 = args.local_44_3;
        uint in_stack_00000028 = args.local_44_4;
        uint in_stack_0000002c = args.local_44_5;
        uint in_stack_00000030 = args.local_44_6;

        Kernel.rand();

        if ((short)param_5 < 0x30)
        {
            if ((short)(ushort)PsxRam.ReadU8((int)in_stack_00000024) <= (short)(param_5 >> 16))
            {
                return -1;
            }

            if (((uint)PsxRam.ReadI32(param_2 + 0x138) & 0x4000) == 0)
            {
                iVar1 = Kernel.rand();
                if ((int)(uint)PsxRam.ReadU8((int)in_stack_00000024 + 1) <= iVar1 % 0x65)
                {
                    return -1;
                }

                PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 8));
                if ((short)(param_5 >> 16) < 0x14)
                {
                    return 10;
                }

                iVar2 = Kernel.rand();
                iVar1 = (int)((ulong)((long)iVar2 * 0x288df0cbL) >> 0x20);
                goto LAB_80025928;
            }
        }
        else if ((short)param_5 < 0x60)
        {
            if ((short)(ushort)PsxRam.ReadU8((int)in_stack_00000028) <= (short)(param_5 >> 16))
            {
                return -1;
            }

            if (((uint)PsxRam.ReadI32(param_2 + 0x138) & 0x4000) == 0)
            {
                iVar1 = Kernel.rand();
                if ((int)(uint)PsxRam.ReadU8((int)in_stack_00000028 + 1) <= iVar1 % 0x65)
                {
                    return -1;
                }

                PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 8));
                if ((short)(param_5 >> 16) < 0x14)
                {
                    return 10;
                }

                iVar2 = Kernel.rand();
                iVar1 = (int)((ulong)((long)iVar2 * 0x288df0cbL) >> 0x20);
                in_stack_00000024 = in_stack_00000028;
                goto LAB_80025928;
            }
        }
        else
        {
            if ((short)param_5 < 0x200)
            {
                if ((short)(ushort)PsxRam.ReadU8((int)in_stack_0000002c) <= (short)(param_5 >> 16))
                {
                    return -1;
                }

                iVar2 = Kernel.rand();
                iVar1 = (int)((ulong)((long)iVar2 * 0x288df0cbL) >> 0x20);
            }
            else
            {
                if ((short)(ushort)PsxRam.ReadU8((int)in_stack_00000030) <= (short)(param_5 >> 16))
                {
                    return -1;
                }

                iVar2 = Kernel.rand();
                iVar1 = (int)((ulong)((long)iVar2 * 0x288df0cbL) >> 0x20);
                in_stack_0000002c = in_stack_00000030;
            }

            if ((int)(uint)PsxRam.ReadU8((int)in_stack_0000002c + 1)
                <= iVar2 + ((iVar1 >> 4) - (iVar2 >> 0x1f)) * -0x65)
            {
                return -1;
            }
        }

        return 0x1d;

        // Hoisted to method scope for the reason FUN_80024C78's own label block gives: C# forbids a
        // `goto` into a nested block. Both entries reassign in_stack_00000024 first, exactly as the
        // decompilation does at its second entry.
    LAB_80025928:
        if ((int)(uint)PsxRam.ReadU8((int)in_stack_00000024 + 2)
            <= iVar2 + ((iVar1 >> 4) - (iVar2 >> 0x1f)) * -0x65)
        {
            return 10;
        }

        PsxRam.WriteI32(
            param_1 + 0x138,
            unchecked((int)((uint)PsxRam.ReadI32(param_1 + 0x138) | 0x40000)));
        return 10;
    }

    // GHIDRA: FUN_80025a3c @ 0x80025A3C (VS.EXE)
    // 212 bytes. Two callers, both in FUN_80023890's decoder 2. A single percentage roll whose
    // COLUMN is chosen by the slot record's own byte at ctx + slot*0x14 + 0x15BA — that is
    // BattleState.CtxKiGauge + 6, and BattleState has no name for it, so it stays a raw literal.
    // in_stack_00000034 = local_44_7.
    //
    // The column is `value - 1` clamped: 8 when the byte is >= 10, and 0 when `value - 1` has its
    // low-byte sign bit set (i.e. when the byte is 0, giving -1). The `iVar2 * 0x1000000 < 0` test
    // is the original's sign-extend-and-test, kept literal.
    private static bool FUN_80025a3c(int param_1, int param_2, uint param_3, uint param_4, StackArgs args)
    {
        int iVar1;
        int iVar2;
        sbyte cVar3;

        uint in_stack_00000034 = args.local_44_7;

        iVar1 = (int)(sbyte)PsxRam.ReadU8(
            PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
            + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
            + 0x15ba);

        iVar2 = iVar1 + -1;
        cVar3 = (sbyte)iVar2;

        if (iVar1 < 10)
        {
            if (unchecked(iVar2 * 0x1000000) < 0)
            {
                cVar3 = 0;
            }
        }
        else
        {
            cVar3 = 8;
        }

        iVar1 = Kernel.rand();
        return iVar1 % 0x65 < (int)(uint)PsxRam.ReadU8((int)in_stack_00000034 + cVar3);
    }

    // GHIDRA: FUN_80025b10 @ 0x80025B10 (VS.EXE)
    // 692 bytes. One caller, FUN_80023890's decoder-1 exit (LAB_8002411C), whose non-zero result
    // becomes command 0x1C after FUN_80025DC4. in_stack_00000018 = local_44_0.
    //
    // TWO INDEPENDENT PARTS, and the second runs whatever the first decided:
    //   1. A percentage bVar7, seeded from local_44_0[1] and biased by difficulty (+10 / +15 / +20
    //      for DAT_801FF01C 0 / 1 / 2), but ONLY when the opponent's +0x138 has bits 0x10000000,
    //      8 and 0x40 all set, its +0x230 byte equals the slot record's own halfword at
    //      ctx + slot*0x14 + 0x15BC, this fighter's +0x22C bit 7 is clear, and the opponent's state
    //      byte matches one of its two remembered states at +0x22E / +0x22F. The bias saturates to
    //      100 when the addition pushes the low byte negative — the same clamp
    //      FighterCombat.FUN_80025F38's own comment describes for its own roll.
    //   2. A popcount of the OPPONENT's +0x233 history byte which, above 4, may stamp a 0x78
    //      countdown into +0x238, clear +0x233 and set +0x22C bit 4. It OVERWRITES bVar7 with the
    //      raw difficulty percentage when it fires, so part 2's roll can replace part 1's.
    //
    // NOTE the difficulty ladder in part 1 computes `iVar3 = bVar7 << 0x18` in EVERY arm including
    // the ones that then bias bVar7 and recompute it; the final `if (iVar3 < 0)` therefore tests
    // the BIASED value on the three in-range arms and the unbiased one otherwise. Kept as written.
    private static bool FUN_80025b10(int param_1, int param_2, uint param_3, uint param_4, StackArgs args)
    {
        bool bVar1;
        int iVar2;
        int iVar3;
        uint uVar4;
        uint uVar5;
        int iVar6;
        byte bVar7;

        uint in_stack_00000018 = args.local_44_0;

        iVar2 = Kernel.rand();
        bVar7 = PsxRam.ReadU8((int)in_stack_00000018 + 1);
        uVar5 = (uint)PsxRam.ReadI32(param_2 + 0x138);
        bVar1 = false;

        if ((uVar5 & 0x10000000) == 0
            || (uVar5 & 8) == 0
            || (uVar5 & 0x40) == 0
            || PsxRam.ReadU16(
                   PsxRam.ReadI32(param_1 + BattleState.FighterBattleContext)
                   + PsxRam.ReadU8(param_1 + BattleState.FighterSlotIndex) * BattleState.CtxSlotRecordStride
                   + 0x15bc)
               != (ushort)PsxRam.ReadU8(param_2 + 0x230))
        {
            goto LAB_80025c80;
        }

        if ((sbyte)PsxRam.ReadU8(param_2 + 0x16a) == (sbyte)PsxRam.ReadU8(param_2 + 0x22e)
            || (sbyte)PsxRam.ReadU8(param_2 + 0x16a) == (sbyte)PsxRam.ReadU8(param_2 + 0x22f))
        {
            bVar1 = true;
        }

        if ((PsxRam.ReadU8(param_1 + 0x22c) & 0x80) != 0 || !bVar1)
        {
            goto LAB_80025c80;
        }

        if (SharedHighRam.DAT_801ff01c == 1)
        {
            bVar7 = (byte)(bVar7 + 0xf);
            goto LAB_80025c70;
        }

        if (SharedHighRam.DAT_801ff01c < 2)
        {
            iVar3 = unchecked((int)((uint)bVar7 << 0x18));
            if (SharedHighRam.DAT_801ff01c == 0)
            {
                bVar7 = (byte)(bVar7 + 10);
                goto LAB_80025c70;
            }
        }
        else
        {
            iVar3 = unchecked((int)((uint)bVar7 << 0x18));
            if (SharedHighRam.DAT_801ff01c == 2)
            {
                bVar7 = (byte)(bVar7 + 0x14);
                goto LAB_80025c70;
            }
        }

        goto LAB_80025c78;

    LAB_80025c70:
        iVar3 = unchecked((int)((uint)bVar7 << 0x18));

    LAB_80025c78:
        if (iVar3 < 0)
        {
            bVar7 = 100;
        }

    LAB_80025c80:
        if ((PsxRam.ReadU8(param_1 + 0x22c) & 0xc0) == 0)
        {
            iVar3 = 0;
            if ((PsxRam.ReadU8(param_1 + 0x22c) & 0x10) == 0)
            {
                iVar6 = 7;
                uVar5 = PsxRam.ReadU8(param_2 + 0x233);
                do
                {
                    uVar4 = uVar5 & 1;
                    uVar5 = uVar5 >> 1;
                    if (uVar4 != 0)
                    {
                        iVar3 = iVar3 + 1;
                    }

                    iVar6 = iVar6 + -1;
                }
                while (-1 < iVar6);

                if (4 < iVar3)
                {
                    if (SharedHighRam.DAT_801ff01c == 1)
                    {
                        bVar7 = 0x28;
                    }
                    else if (SharedHighRam.DAT_801ff01c < 2)
                    {
                        if (SharedHighRam.DAT_801ff01c == 0)
                        {
                            bVar7 = 0x1e;
                        }
                    }
                    else if (SharedHighRam.DAT_801ff01c == 2)
                    {
                        bVar7 = 0x32;
                    }

                    iVar3 = Kernel.rand();
                    if (iVar3 % 0x65 < (int)(sbyte)bVar7)
                    {
                        PsxRam.WriteU16(param_1 + 0x238, 0x78);
                        PsxRam.WriteU8(param_1 + 0x233, 0);
                        PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 0x10));
                    }
                }
            }
        }

        return unchecked((iVar2 % 0x65) * 0x1000000) >> 0x18 < (int)(sbyte)bVar7;
    }

    // GHIDRA: FUN_80025dc4 @ 0x80025DC4 (VS.EXE)
    // 372 bytes. Two callers: FUN_80023890's decoder-1 exit and FUN_80024C78's 0x21 remap. Both
    // pair it with command 0x1C, so this is the state change that command carries.
    //
    // It stamps state byte 5 at +0x228, rewrites +0x138 to `(flags & 0xFFFD8000) | 0x80000`, and
    // then sets a velocity pair at +0xC8 / +0xCA / +0xCC. Which pair depends on the fighter's
    // +0x116 halfword: below 0x80 it rolls `rand() % 3`, at or above it rolls `rand() % 4` — the
    // extra case being a +0x400 sideways step instead of a -0x400 one. The two ladders share their
    // 0 and 1 exits through `joined_r0x80025eb8`, which is why the 0 case of the near ladder writes
    // the SAME 0xFC00 the far ladder's 1 case writes 0x400 for. Reproduced branch for branch.
    //
    // The `% 4` is written as `iVar3 + (iVar4 >> 2) * -4` with `iVar4` pre-biased by 3 when
    // negative — the compiler's power-of-two signed remainder. Kept literal, like FUN_8002575C's.
    private static void FUN_80025dc4(int param_1)
    {
        short sVar1;
        ushort uVar2;
        int iVar3;
        int iVar4;

        PsxRam.WriteU8(param_1 + 0x228, 5);
        PsxRam.WriteI32(
            param_1 + 0x138,
            unchecked((int)(((uint)PsxRam.ReadI32(param_1 + 0x138) & 0xfffd8000) | 0x80000)));

        if ((short)PsxRam.ReadU16(param_1 + 0x116) < 0x80)
        {
            iVar3 = Kernel.rand();
            iVar3 = iVar3 % 3;
            if (iVar3 == 1)
            {
                PsxRam.WriteU16(param_1 + 200, 0);
                PsxRam.WriteU16(param_1 + 0xcc, 0);
                sVar1 = (short)((short)PsxRam.ReadU16(param_1 + 0x11e) + -0x400);
                goto LAB_80025f20;
            }

            if (iVar3 < 2)
            {
                goto joined_r0x80025eb8;
            }

            if (iVar3 != 2)
            {
                return;
            }
        }
        else
        {
            iVar3 = Kernel.rand();
            iVar4 = iVar3;
            if (iVar3 < 0)
            {
                iVar4 = iVar3 + 3;
            }

            iVar3 = iVar3 + (iVar4 >> 2) * -4;
            if (iVar3 == 1)
            {
                uVar2 = 0x400;
                goto LAB_80025eec;
            }

            if (iVar3 < 2)
            {
                goto joined_r0x80025eb8;
            }

            if (iVar3 == 2)
            {
                PsxRam.WriteU16(param_1 + 200, 0);
                PsxRam.WriteU16(param_1 + 0xcc, 0);
                sVar1 = (short)((short)PsxRam.ReadU16(param_1 + 0x11e) + -0x400);
                goto LAB_80025f20;
            }

            if (iVar3 != 3)
            {
                return;
            }
        }

        PsxRam.WriteU16(param_1 + 200, 0);
        PsxRam.WriteU16(param_1 + 0xcc, 0);
        sVar1 = (short)((short)PsxRam.ReadU16(param_1 + 0x11e) + 0x400);

    LAB_80025f20:
        PsxRam.WriteU16(param_1 + 0xca, (ushort)sVar1);
        return;

    joined_r0x80025eb8:
        uVar2 = 0xfc00;
        if (iVar3 != 0)
        {
            return;
        }

    LAB_80025eec:
        PsxRam.WriteU16(param_1 + 200, 0);
        PsxRam.WriteU16(param_1 + 0xca, 0);
        PsxRam.WriteU16(param_1 + 0xcc, uVar2);
    }

    // GHIDRA: FUN_8002631c @ 0x8002631C (VS.EXE)
    // 264 bytes. Seven callers: the six in FUN_80023890's decoder-2 0x23/0x25/0x26/0x27/0x28/other
    // ladder, and one at 0x80026304 inside FUN_800261EC, which is NOT in this port.
    //
    // A five-way cumulative roll over param_2[0..4] returning 0x28 / 0x26 / 0x27 / 0x25 / 0x23 /
    // 0x24 — the SAME six attack opcodes FighterInput.cs's decoders produce. param_1 is accepted
    // and never used; that is the original's signature, not an omission here.
    //
    // param_2[0] is read SIGNED (`char *`) and [1]..[4] unsigned, and every accumulation is
    // re-truncated to a signed byte by the `* 0x1000000 >> 0x18` pair. Reproduced literally: a run
    // that passes 127 wraps negative, and that wrap is the original's behaviour.
    private static int FUN_8002631c(int param_1, uint param_2)
    {
        int iVar1;
        int uVar2;
        int iVar3;

        iVar1 = Kernel.rand();
        iVar1 = unchecked((iVar1 % 0x65) * 0x1000000) >> 0x18;
        uVar2 = 0x28;

        if (-1 < iVar1 - (sbyte)PsxRam.ReadU8((int)param_2))
        {
            iVar3 = (int)(sbyte)PsxRam.ReadU8((int)param_2) + (int)(uint)PsxRam.ReadU8((int)param_2 + 1);
            uVar2 = 0x26;
            if (-1 < iVar1 - (unchecked(iVar3 * 0x1000000) >> 0x18))
            {
                iVar3 = iVar3 + (int)(uint)PsxRam.ReadU8((int)param_2 + 2);
                uVar2 = 0x27;
                if (-1 < iVar1 - (unchecked(iVar3 * 0x1000000) >> 0x18))
                {
                    iVar3 = iVar3 + (int)(uint)PsxRam.ReadU8((int)param_2 + 3);
                    if (iVar1 - (unchecked(iVar3 * 0x1000000) >> 0x18) < 0)
                    {
                        uVar2 = 0x25;
                    }
                    else
                    {
                        uVar2 = 0x23;
                        if (-1 < iVar1 - (unchecked((iVar3 + (int)(uint)PsxRam.ReadU8((int)param_2 + 4)) * 0x1000000) >> 0x18))
                        {
                            uVar2 = 0x24;
                        }
                    }
                }
            }
        }

        return uVar2;
    }

    // GHIDRA: FUN_800264d8 @ 0x800264D8 (VS.EXE)
    // 608 bytes. One caller, FUN_80023890's early `(&DAT_801FF058)[fighter index] == -0x32` gate.
    //
    // A DEBUG / CHEAT HOOK, and read that way from its shape rather than named: it consults ONE raw
    // pad word and, on six different button combinations, forces the +0x22C bit-6 / bit-7 pair on
    // either fighter — the very bits that make FUN_80023890 abandon the profile tables for the two
    // override rows at 0x80080A38 / 0x80080A5C, and that make FUN_80024C78 remap its opcodes. It
    // then plays one of two sounds and burns three frames. Nothing else in this port writes
    // 0x801FF058, and SharedHighRam records that the six bytes are written "when a pad holds the
    // 0x1d0 button combination on the title screen".
    //
    // THE PAD WORD. `(&DAT_8008d518)[(fighter+0x160 + 1) >> 2]` is WORD-strided, not byte-strided:
    // 0x800264FC is `sll v1,v0,2` and 0x80026510 is `lw v1,0(at)`. PadInput.DAT_8008d518 is this
    // port's single storage for 0x8008D518 and is a uint[2], which is exactly the range this index
    // can reach: BattleState.FighterIndex is 0..5, so `(index + 1) >> 2` is 0 or 1. It is NOT read
    // through PsxRam, because PsxRam would answer for 0x8008D518 out of the image rather than out
    // of the live pad state PadInput maintains.
    //
    // BLOCKED: FUN_8005FD9C is the sound call. AnimCmdSound.cs owns 0x8005FD9C, but declares it
    // `private`, so it cannot be called from here; the stub below is this file's own and MUST be
    // deleted once AnimCmdSound.FUN_8005fd9c is made `internal`. See the file header.
    private static void FUN_800264d8(int param_1, int param_2, uint param_3, uint param_4, StackArgs args)
    {
        int iVar1;
        uint uVar2;
        int iVar3;

        iVar1 = (short)PsxRam.ReadU16(param_1 + BattleState.FighterIndex) + 1 >> 2;
        iVar3 = 0;

        if ((PadInput.DAT_8008d518[iVar1] & 5) == 5 && (PsxRam.ReadU8(param_1 + 0x22c) & 0xc0) != 0)
        {
            PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) & 0x3f));
            iVar3 = 2;
        }

        if ((PadInput.DAT_8008d518[iVar1] & 10) == 10 && (PsxRam.ReadU8(param_2 + 0x22c) & 0xc0) != 0)
        {
            PsxRam.WriteU8(param_2 + 0x22c, (byte)(PsxRam.ReadU8(param_2 + 0x22c) & 0x3f));
            iVar3 = 2;
        }

        if ((PadInput.DAT_8008d518[iVar1] & 0xd4) == 0xd4 && (PsxRam.ReadU8(param_1 + 0x22c) & 0x40) == 0)
        {
            PsxRam.WriteU8(param_1 + 0x22c, (byte)((PsxRam.ReadU8(param_1 + 0x22c) & 0x7f) | 0x40));
            iVar3 = 1;
        }

        if ((PadInput.DAT_8008d518[iVar1] & 0xd1) == 0xd1 && (PsxRam.ReadU8(param_2 + 0x22c) & 0x80) == 0)
        {
            PsxRam.WriteU8(param_2 + 0x22c, (byte)(PsxRam.ReadU8(param_2 + 0x22c) | 0x80));
            iVar3 = 1;
            PsxRam.WriteU8(param_2 + 0x22c, (byte)(PsxRam.ReadU8(param_2 + 0x22c) & 0xbf));
        }

        if ((PadInput.DAT_8008d518[iVar1] & 0xd8) == 0xd8 && (PsxRam.ReadU8(param_2 + 0x22c) & 0x40) == 0)
        {
            PsxRam.WriteU8(param_2 + 0x22c, (byte)(PsxRam.ReadU8(param_2 + 0x22c) | 0x40));
            iVar3 = 1;
            PsxRam.WriteU8(param_2 + 0x22c, (byte)(PsxRam.ReadU8(param_2 + 0x22c) & 0x7f));
        }

        if ((PadInput.DAT_8008d518[iVar1] & 0xd2) == 0xd2 && (PsxRam.ReadU8(param_1 + 0x22c) & 0x80) == 0)
        {
            PsxRam.WriteU8(param_1 + 0x22c, (byte)((PsxRam.ReadU8(param_1 + 0x22c) & 0xbf) | 0x80));
            iVar3 = 1;
        }

        if (iVar3 != 0)
        {
            uVar2 = 9;
            if (iVar3 == 1)
            {
                uVar2 = 0xb;
            }

            AnimCmdSound.FUN_8005fd9c(uVar2, 6, unchecked((short)0xe0));
            VSync(0);
            VSync(0);
            VSync(0);
        }
    }

    // GHIDRA: FUN_8005fd9c @ 0x8005FD9C (VS.EXE)
    // NOT DECLARED HERE. VS_EXE/AnimCmdSound.cs already carries this address (still its own BLOCKED
    // stub, 448 bytes); this file reaches it by qualified name rather than adding a second empty
    // body at the same address.

}
