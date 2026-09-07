using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE SCENE-TRANSITION SLICE OF VS.EXE, plus the small leaves that hang off it.
//
// Two of the functions here are TASK ENTRY POINTS that VS.EXE creates every match and that, until
// now, dispatched to nothing because no body existed at their address:
//
//   LAB_80029200  list 5, id 0, 0xc bytes of context, created by BattleManager.FUN_800290d0 as the
//                 very last thing the battle manager ever does. A four-state transition task: it
//                 fades the screen out, tears the whole battle overlay down (ordering table, VRAM,
//                 six primitive pools, twenty task lists), loads the next SUB overlay's images off
//                 the CD, and starts the task that owns the screen after this one. The task never
//                 deletes itself -- state 3 is terminal and falls straight out of the dispatcher's
//                 `if (iVar1 != 2) return;` every frame thereafter.
//
//   LAB_80026888  list 0xb, id 0, no per-node workspace, created by VS_EXE_exe.FUN_80026a68 at
//                 boot. A thirty-slot effect drawer over VS_EXE_exe's own DAT_8008D610 block: one
//                 0x24-byte record per slot, each with its own countdown, and each live slot is
//                 handed to SpriteDrawer.DrawSpriteGroup once per frame until its countdown goes
//                 negative, at which point the slot is retired and a byte at +0x227 of the record's
//                 owner is decremented.
//
// Both are `LAB_` and not `FUN_` in Ghidra: the analyser never promoted either to a function, so
// both were decompiled BY ADDRESS and came back as `UndefinedFunction_800XXXXX` previews. That is
// the reason neither has a Ghidra-assigned parameter list and the reason the two C# bodies below
// take no arguments -- the console reaches them through the task dispatcher's indirect
// `(*(code *)*puVar1)()`, which passes nothing and returns into the same place.
//
// REGISTRATION IS NOT DONE HERE, and that is deliberate: this file owns no CreateTask call site.
// VS_EXE_exe.cs creates LAB_80026888 and BattleManager.cs creates LAB_80029200, and each of those
// files must add the one `TaskSystem.RegisterCallback` line beside its own CreateTask -- see this
// file's report. Without those two lines both tasks are still born every match and still dispatch
// to nothing, exactly as before.
//
// THE REST OF THE FILE is the leaves the two entries reach plus the neighbouring small functions of
// the same 0x80029000..0x80032000 stretch: the transition's own fade accumulator (FUN_800299e8),
// its full-screen fade quad (FUN_800297b0), the list-wide task teardown (FUN_80053840), and a
// cluster of sub-state steppers (FUN_80031dc8, FUN_80031e34, FUN_80031eac, FUN_80031f14,
// FUN_80031f70, FUN_80031fe0, FUN_800304f0, FUN_80030548, FUN_8002c424, FUN_8002c478) that the
// still-unported SUB-overlay entry points drive. Two more from the brief are NOT here and their
// absence is a decision, not an omission -- see the BLOCKED notes at the end of the file for
// BuildSlotDigitQuads and FUN_800261ec, and see FUN_80051758's note for why it is not here at all.
internal static class SceneTransition
{
    // ================================================================================================
    // Globals
    // ================================================================================================

    // GHIDRA: DAT_8008d3a4 @ 0x8008D3A4, DAT_8008d3a5 @ 0x8008D3A5, DAT_8008d3a6 @ 0x8008D3A6 (VS.EXE)
    // The three fade channels, one byte each, in the gp-relative small-data region (gp = 0x8008D0FC
    // for VS.EXE, so these are gp + 0x2A8 / 0x2A9 / 0x2AA). Nothing else in this port declares them
    // -- checked by searching all three addresses across VS_EXE/ before writing this file, which is
    // the duplicate-symbol discipline this repository asks for.
    //
    // They are the SOURCE of the fade, not the fade quad itself: FUN_800299e8 ramps them up toward
    // 0xFF and reports when all three have arrived, FUN_80031fe0 ramps them back down toward 0 in
    // steps of 8, and LAB_80029200 slams all three to 0x00 or 0xFF outright at its state
    // boundaries. Plain C# bytes rather than a PsxRam span, exactly as TaskSystem.cs models
    // g_CurrentTask at 0x8008D16C and VS_EXE_exe.cs models DAT_8008D38C: these are scalars the
    // original addresses only through gp, never as part of a structure some other function walks.
    internal static byte DAT_8008d3a4;

    internal static byte DAT_8008d3a5;

    internal static byte DAT_8008d3a6;

    // GHIDRA: DAT_800b2f30 @ 0x800B2F30 (VS.EXE)
    // FUN_800297b0's full-screen quad, and a POLY_FT4 -- which is not an assumption but what that
    // function's own first line says: `SetPolyFT4((POLY_FT4 *)&DAT_800b2f30)`. Every one of the
    // twenty-one raw DAT_ symbols FUN_800297b0 writes lands on a POLY_FT4 field at the right offset
    // from this base, which is the cross-check rather than the claim:
    //
    //   DAT_800b2f34/35/36  +0x04/05/06  r0 g0 b0     DAT_800b2f38  +0x08  x0    DAT_800b2f3a  +0x0A  y0
    //   DAT_800b2f3c/3d     +0x0C/0D     u0 v0        DAT_800b2f3e  +0x0E  clut
    //   DAT_800b2f40        +0x10        x1           DAT_800b2f42  +0x12  y1
    //   DAT_800b2f44/45     +0x14/15     u1 v1        DAT_800b2f46  +0x16  tpage
    //   DAT_800b2f48        +0x18        x2           DAT_800b2f4a  +0x1A  y2
    //   DAT_800b2f4c/4d     +0x1C/1D     u2 v2
    //   DAT_800b2f50        +0x20        x3           DAT_800b2f52  +0x22  y3
    //   DAT_800b2f54/55     +0x24/25     u3 v3
    //
    // and the values it writes are a 0x140 x 0xF0 rectangle at the origin -- the whole 320x240
    // screen -- which is what a fade quad is. Same declaration shape as VS_EXE_exe.cs's own
    // POLY_FT4_800b2f74 twenty-one records further up in the same block.
    //
    // IT DOES NOT ALIAS ANYTHING ALREADY DECLARED. VS_EXE_exe.cs's OT_800b0f28 ends at 0x800B2F28
    // (its own comment derives that from DAT_800b2f24 being bucket 0x7FF) and its POLY_FT4_800b2f74
    // starts at 0x800B2F74; this packet occupies 0x800B2F30..0x800B2F57, between the two, and no
    // other file in VS_EXE/ mentions any address in that range.
    private const int PolyFt4800b2f30Address = unchecked((int)0x800B2F30);

    private static readonly POLY_FT4Ref POLY_FT4_800b2f30 =
        new(LibGpu.RamRegion(PolyFt4800b2f30Address, POLY_FT4Ref.Size), 0);

    // GHIDRA: DAT_800c3bfc @ 0x800C3BFC (VS.EXE)
    // The twelve bytes LAB_80029200 zeroes at 0x800C3C00/01/02, 0C/0D/0E, 18/19/1A and 24/25/26.
    // VS_EXE_exe.cs already carries this ADDRESS as a private constant (its FUN_800411b4 submits it
    // through `AddPrim(DAT_8008d420 + 0x206c, ...)`) but declares no STORAGE for it, so nothing in
    // the port can currently write those bytes; this is the first declaration of the span itself,
    // and later readers must use it rather than declare a second one.
    //
    // The extent is 0x38, one POLY_GT4: the four byte triples above sit at +0x04, +0x10, +0x1C and
    // +0x28 from this base, which is exactly a POLY_GT4's four r/g/b triples, and BattleScene.cs's
    // own UpdateScreenFade comment already reads the same twelve bytes the same way from the other
    // end (it calls them the fade ramp and names this packet as VS.EXE's g_FadeQuad).
    //
    // NOT retyped as a POLY_GT4Ref here. BattleScene.cs calls that reading "circumstantial
    // confirmation" and explicitly leaves it PROPOSED rather than applied; adopting the field names
    // on the strength of a proposal from a file this slice does not own would be exactly the
    // speculative naming the mandate forbids. The bytes are written below through PsxRam at
    // base + offset, with the raw DAT_ symbol spelled beside each store.
    private const int Dat800c3bfcAddress = unchecked((int)0x800C3BFC);

    private static readonly byte[] DAT_800c3bfc = LibGpu.RamRegion(Dat800c3bfcAddress, 0x38);

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: FUN_800297b0's `undefined2 local_10 [4]` is a STACK local whose ADDRESS is handed to
    // LoadImage_ReturnTPageOrClutId as the source pixels. C# cannot give a local a PSX address, so
    // the port gives it a real one, the way AnimCmdEffects.cs (0x807FFFE0), AnimVmInterpreter.cs
    // (0x807FFFF0) and BattleScene.cs (0x807FFFD0) already do for the same problem.
    //
    // 0x807FFD60 is genuinely stack and aliases none of them: crt0 starts SP at 0x807FFFF8 and the
    // stack runs down 0x8000 bytes, and the lowest synthetic frame any other file in this port
    // claims is BattleCamera.cs's FrameBase at 0x807FFD70. This block sits eight bytes BELOW that
    // one, outside the whole 0x807FFD70..0x807FFFF8 chain those files describe as packed.
    private const int Local10Address = unchecked((int)0x807FFD60);

    private static readonly byte[] local_10 = LibGpu.RamRegion(Local10Address, 8);

    // GHIDRA: LAB_800298e8 @ 0x800298E8 (VS.EXE)
    // The task FUN_800297b0 creates on list 5 alongside the fade quad. Ghidra has no function here
    // either, only a label; it is not ported in this slice, so the raw address is passed through to
    // CreateTask exactly as the original passes `&LAB_800298e8` and no callback is registered for
    // it. See this file's report: it is a third unbodied task entry, not one of the two in scope.
    private const int Lab800298e8Address = unchecked((int)0x800298E8);

    // GHIDRA: LAB_80029a98 @ 0x80029A98, LAB_8002ab78 @ 0x8002AB78, LAB_800313f4 @ 0x800313F4,
    // GHIDRA: LAB_800305ec @ 0x800305EC, LAB_8002c504 @ 0x8002C504, LAB_8002e650 @ 0x8002E650 (VS.EXE)
    // The six per-mode entry points LAB_80029200's switch creates on list 10, one per value of the
    // transition's own mode word. All six are bare labels in Ghidra, none is ported in this slice,
    // and each is therefore passed to CreateTask as the raw address the console stores -- the same
    // treatment VS_EXE_exe.cs already gives Lab80026888Address and StageBackdrop.cs gives
    // Lab80040f78Address. They are the SUB overlays' own screen tasks (ZCEXT, VSEXT, SPENT, SPEXT),
    // which no slice has reached yet.
    private const int Lab80029a98Address = unchecked((int)0x80029A98);

    private const int Lab8002ab78Address = unchecked((int)0x8002AB78);

    private const int Lab800313f4Address = unchecked((int)0x800313F4);

    private const int Lab800305ecAddress = unchecked((int)0x800305EC);

    private const int Lab8002c504Address = unchecked((int)0x8002C504);

    private const int Lab8002e650Address = unchecked((int)0x8002E650);

    // GHIDRA: FUN_800411b4 @ 0x800411B4 (VS.EXE)
    // The camera/geometry task LAB_80029200 both creates and calls directly. VS_EXE_exe.cs carries
    // the real body and already carries this same constant privately; the address is repeated here
    // only so the CreateTask call site below stores what the console stores. The DIRECT call goes
    // through VS_EXE_exe.FUN_800411b4 itself -- see the call site below the CreateTask.
    private const int Fun800411b4Address = unchecked((int)0x800411B4);

    // GHIDRA: DAT_8008d610 @ 0x8008D610 (VS.EXE)
    // THE ADDRESS ONLY, not the storage. VS_EXE_exe.cs owns the 0x438-byte span (its own
    // `RamRegion(Dat8008d610Address, 0x438)`) and its OWNERSHIP CAVEAT asks this slice to use that
    // array rather than declare a second one -- which is exactly what happens: every access in
    // LAB_80026888 goes through PsxRam at a PSX address, and the resolver hands it VS_EXE_exe's own
    // byte[]. The constant is repeated here only because VS_EXE_exe.cs declares ITS copy `private`;
    // this is the same "reach them by the raw literal rather than declare a second span" move
    // BattleManager.cs already makes for Roster.cs's DAT_80084184. VS_EXE_exe.Dat8008d610Address is
    // `internal`; this copy is kept only so the accesses below read as one local constant.
    private const int Dat8008d610Address = unchecked((int)0x8008D610);

    // ================================================================================================
    // The two task entry points
    // ================================================================================================

    // GHIDRA: LAB_80029200 @ 0x80029200 (VS.EXE)
    // 165 decompiled lines. Not a `FUN_` in Ghidra: decompiled by address, and the preview came back
    // as `UndefinedFunction_80029200`. Referenced exactly once, as a PARAMETER rather than a call
    // target -- BattleManager.FUN_800290d0's `TaskSystem.CreateTask(LAB_80029200, 0, 5, 0xc, 0,
    // g_TaskListTail[5])` -- which is why it takes and returns nothing here.
    //
    // THE CONTEXT IS THREE WORDS, the 0xc bytes CreateTask zeroed for it, reached through
    // `*(int *)(TaskSystem.g_CurrentTask + 8)`:
    //   ctx[0]  the MODE, 0..5, written by the creator before the task first runs
    //           (FUN_800290d0's `**(undefined4 **)(iVar1 + 8) = 2` puts 2 there for the VS path);
    //   ctx[1]  the STATE, 0/1/2/3, stepped by this function;
    //   ctx[2]  the fade STEP handed to FUN_800299e8, set to 4 by state 0 and to 0x80 by state 1
    //           once the fade has already passed 0x7F.
    //
    // THE FOUR STATES, in the order the dispatcher walks them:
    //   0  arm: clear the fade quad's colour, force the three fade channels to 0x00 (or to 0xFF for
    //      mode 3), build the fade quad, kill reverb, poke the sound driver, set the step to 4, ++;
    //   1  ramp: accelerate the step to 0x80 once the red channel is past 0x7F, add the step to all
    //      three channels, and only when ALL THREE have saturated at 0xFF stop the music and ++;
    //   2  the teardown and the reload, below. Falls out WITHOUT stepping the state;
    //   3  and anything else: `if (iVar1 != 2) return;` -- nothing at all, every frame, forever.
    //
    // STATE 2 IS THE WHOLE TRANSITION and it runs exactly once, because it never touches ctx[1]:
    // ctx[1] is still 2 on the next frame, and state 2 runs AGAIN. That is what the decompilation
    // says and it is reproduced without correction under rule 12 -- the state word is stepped on
    // exactly two of the four paths (state 0 and state 1's completion), and the state-2 path has no
    // increment on it. The escape is elsewhere: state 2's own `FUN_80053840` sweep deletes every
    // task on lists 0..0x13, list 5 included, so the sweep destroys THIS task's own node before the
    // second frame ever arrives. The apparent missing increment is therefore not a missing
    // increment; the task ends by deleting itself out from under the dispatcher, from inside a loop
    // that does not know it is doing so.
    //
    // THE ORDER OF THE TEARDOWN IS LOAD-BEARING and is not reordered: the ordering table is cleared
    // before VRAM, VRAM before the primitive pools, the pools before the task lists, and the task
    // lists before the geometry is re-armed -- because FUN_80053840 releases task nodes back to the
    // same game allocator the pools were just returned to, and because the CreateTask calls further
    // down would otherwise be swept away by the very loop that precedes them.
    //
    // SEVEN HOLES, each marked BLOCKED at its own line and each listed in this slice's report. Six
    // are one-word visibility fixes in files this slice does not own (a `private` member that must
    // become `internal`); one, FUN_80060a88, is a function nothing in this port has transliterated
    // yet. Not one of them is a semantic gap: every line around them is the full translation.
    internal static void LAB_80029200()
    {
        int piVar2 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
        int iVar1 = PsxRam.ReadI32(piVar2 + 4);
        int iVar8 = PsxRam.ReadI32(piVar2);

        if (iVar1 == 1)
        {
            if (0x7f < DAT_8008d3a4)
            {
                PsxRam.WriteI32(piVar2 + 8, 0x80);
            }

            // The context pointer is re-read here rather than reused: the original spells
            // `FUN_800299e8(*(undefined4 *)(*(int *)(DAT_8008d16c + 8) + 8))`, a fresh double
            // dereference, not `piVar2[2]`. Kept as it stands.
            bool bVar1 = FUN_800299e8(PsxRam.ReadI32(PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8) + 8));
            if (bVar1 == false)
            {
                return;
            }

            // BLOCKED: FUN_80060a88 @ 0x80060A88 is not transliterated anywhere in this port. It is
            // GAME code, not SDK -- 0x80060A88 is below rule 13's 0x800632C4 boundary -- and belongs
            // to the sound-driver module whose front half VS_EXE/SoundDriver.cs already describes
            // and leaves for a later slice (that file's own comment names it at 0x80060B7C, the
            // VSync busy-wait inside it). Called here with no arguments, immediately after the fade
            // has saturated: on the evidence of where it sits, the music stop. Not invented here.
            // UNBLOCKED: VS_EXE/SoundEffects.cs now carries the body at 0x80060A88, `internal`.
            SoundEffects.FUN_80060a88();

            int iVar4 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
            PsxRam.WriteI32(iVar4 + 4, PsxRam.ReadI32(iVar4 + 4) + 1);
            return;
        }

        if (iVar1 == 0)
        {
            POLY_FT4_800b2f30.r0 = 0;   // DAT_800b2f34
            POLY_FT4_800b2f30.g0 = 0;   // DAT_800b2f35
            POLY_FT4_800b2f30.b0 = 0;   // DAT_800b2f36

            // MODE 3 STARTS THE TRANSITION ALREADY WHITE and every other mode starts it black.
            // Which of the two is "already faded" is not established here and no name is put on it.
            if (iVar8 == 3)
            {
                DAT_8008d3a6 = 0xff;
                DAT_8008d3a5 = 0xff;
                DAT_8008d3a4 = 0xff;
            }
            else
            {
                DAT_8008d3a6 = 0;
                DAT_8008d3a5 = 0;
                DAT_8008d3a4 = 0;
            }

            FUN_800297b0();
            LibSnd.SsUtReverbOff();
            BattleManager.FUN_8005ee5c(0, 0, 0x10);

            PsxRam.WriteI32(PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8) + 8, 4);
            int iVar4 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
            PsxRam.WriteI32(iVar4 + 4, PsxRam.ReadI32(iVar4 + 4) + 1);
            return;
        }

        if (iVar1 != 2)
        {
            return;
        }

        uint uVar6 = 0;
        LibGpu.ClearOTag(VS_EXE_exe.DAT_8008d420 + 0x70, 0x800);
        FileIo.ClearVram();

        // SIX primitive pools, not eight: CreatePrimitivePools allocates eight slots but the last
        // two are created with a count of zero, and this loop stops at six. The original's own
        // bound, not a transcription slip.
        do
        {
            PrimitivePools.FreePrimitivePool(PrimitivePools.g_PrimitivePoolContext, uVar6 & 0xffff);
            uVar6 = uVar6 + 1;
        } while ((int)(uVar6 * 0x10000) >> 0x10 < 6);

        // TWENTY task lists out of the twenty-one that exist. List 0x14 is left alone.
        uVar6 = 0;
        do
        {
            FUN_80053840(uVar6 & 0xffff);
            uVar6 = uVar6 + 1;
        } while ((int)(uVar6 * 0x10000) >> 0x10 < 0x14);

        // The battle's own geometry was armed with (0xa0, 0xef, 0x200, ...) by main; this re-arms it
        // with a different screen distance and a different projection depth for whatever comes next.
        FileIo.SetupGeometry(0xa0, 0x80, 0x1000, 0, 0, 0, 0x1000, 0, 0, 0);

        // The twelve fade-ramp bytes, written high to low exactly as the original writes them.
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x2a, 0);   // DAT_800c3c26
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x29, 0);   // DAT_800c3c25
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x28, 0);   // DAT_800c3c24
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x1e, 0);   // DAT_800c3c1a
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x1d, 0);   // DAT_800c3c19
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x1c, 0);   // DAT_800c3c18
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x12, 0);   // DAT_800c3c0e
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x11, 0);   // DAT_800c3c0d
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x10, 0);   // DAT_800c3c0c
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x06, 0);   // DAT_800c3c02
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x05, 0);   // DAT_800c3c01
        PsxRam.WriteU8(Dat800c3bfcAddress + 0x04, 0);   // DAT_800c3c00

        // THE BACKGROUND-COLOUR TRIPLET, owned by VS_EXE_exe.cs (its DAT_8008d38c / DAT_8008d390 /
        // DAT_8008d394, whose own comment records that main and FUN_80042054's variant table write
        // them). Those three fields are now `internal` and are written HERE THROUGH THAT CLASS,
        // never shadowed with a second declaration over the same three addresses: a shadow would
        // compile and then run this function against a private copy while VS_EXE_exe's own DRAWENV
        // update kept reading the real one.
        VS_EXE_exe.DAT_8008d38c = 0;
        VS_EXE_exe.DAT_8008d390 = 0;
        VS_EXE_exe.DAT_8008d394 = 0;
        // Historical note, kept because it names the defect class this avoided:
        // These three are the background-colour triplet VS_EXE_exe.cs already declares (its
        // DAT_8008d38c / DAT_8008d390 / DAT_8008d394, whose own comment records that main and
        // FUN_80042054's variant table write them). All three are `private` to that class, and this
        // slice may not edit that file. NOT shadowed with a second declaration over the same three
        // addresses: that would compile and then run this function against a private copy while
        // VS_EXE_exe's own DRAWENV update kept reading the real one -- the silent behavioural fork
        // this repository's duplicate-symbol rule exists to prevent. The fix is one word on each of
        // the three fields, `private` -> `internal`. See the report.

        // MODES 0, 1, 4 AND 5 PROBE THE MEMORY CARD; modes 2 and 3 do not. The condition is written
        // exactly as Ghidra prints it rather than collapsed to a set test, because the three
        // comparisons are three separate branches in the original and the shape is the evidence.
        if ((-1 < iVar8) && ((iVar8 < 2 || ((iVar8 < 6 && (3 < iVar8))))))
        {
            MemoryCard.InitializeMemoryCard();
            SharedHighRam.g_CardProbeResult = MemoryCard.ProbeMemoryCard(0);
            if (SharedHighRam.g_CardProbeResult == 0)
            {
                int iVar3 = MemoryCard.FUN_80022a88();
                if (iVar3 == 0)
                {
                    // The retry FAILED and the probe result is forced to 2. Reading that as "no
                    // card" is not asserted; the constant is reproduced, not interpreted.
                    SharedHighRam.g_CardProbeResult = 2;
                }

                if (SharedHighRam.g_CardProbeResult == 0)
                {
                    goto LAB_8002950c;
                }
            }

            // Eighteen words at 0x801FF200. `(int)&DAT_801ff200 + (iVar1 >> 0xe)` with
            // `iVar1 = iVar7 * 0x10000` is the compiler's way of writing `iVar7 * 4`: the counter is
            // kept shifted left sixteen and the byte offset is recovered by shifting right fourteen,
            // not sixteen. Transliterated as the word index it is, over SharedHighRam's own window
            // -- the same eighteen words that file's own comment says the clear path zeroes.
            int iVar7 = 0;
            do
            {
                SharedHighRam.INT_ARRAY_801ff200[iVar7] = 0;
                iVar7 = iVar7 + 1;
            } while (iVar7 * 0x10000 >> 0x10 < 0x12);
        }

    LAB_8002950c:
        // Initialised at their declaration only because C#'s definite-assignment rule reaches them
        // through the `default:` arm's jump; the original leaves the two registers undefined on that
        // path and never reads them there either.
        int uVar4 = 0;
        int uVar5 = 0;
        int puVar3 = 0;

        switch (iVar8)
        {
            case 0:
                FileIo.ReadFile("\\SUB\\ZCEXT.B;1".ToCharArray(), VS_EXE_exe.Dat80110000Address, 0);
                TaskSystem.CreateTask(Lab80029a98Address, 0, 10, 0x78, 0, TaskSystem.g_TaskListTail[10]);
                // UNBLOCKED: VS_EXE/SoundEffects.cs now carries the real body at 0x80060364,
                // `internal`, and SoundDriver.cs's own copy is a comment-only cross-reference.
                SoundEffects.FUN_80060364();
                // Historical note: SoundDriver.cs carried this address, but its body there
                // is itself an empty BLOCKED stub AND it is `private`. Calling it would be a no-op
                // today; declaring a second body here would be the duplicate-address defect. Needs
                // `private` -> `internal` on SoundEffects.FUN_80060364, and then its real body.
                uVar4 = 4;
                uVar5 = 5;
                break;

            case 1:
                // DEVIATION: the image shares one basic block between cases 1 and 5. The original
                // loads the entry point into a register (`puVar3 = &LAB_8002ab78`) and jumps to
                // LAB_800296B4, where a single CreateTask consumes it; cases 1 and 5 differ in
                // nothing else but the file name and that one address. The port spells the shared
                // tail TWICE instead, once per case, and the reason is not style: passing a task
                // entry point to CreateTask through a VARIABLE is exactly the shape
                // custom-tools/scripts/check_task_registration.py cannot see through, and that
                // checker exists because "a task created with no registered callback dispatches to
                // nothing, silently, with a green build" has already happened three times in this
                // port. A variable there makes the invariant unverifiable for both arms at once.
                // Nothing else changes: same two calls, same order, same six arguments, same
                // constants.
                FileIo.ReadFile("\\SUB\\ZCEXT.B;1".ToCharArray(), VS_EXE_exe.Dat80110000Address, 0);
                puVar3 = Lab8002ab78Address;
                TaskSystem.CreateTask(Lab8002ab78Address, 0, 10, 0x78, 0, TaskSystem.g_TaskListTail[10]);
                SoundEffects.FUN_80060364();   // see case 0.
                uVar4 = 4;
                uVar5 = 6;
                break;

            case 2:
                FileIo.ReadFile("\\SUB\\VSEXT.B;1".ToCharArray(), VS_EXE_exe.Dat80110000Address, 0);
                TaskSystem.CreateTask(Lab800313f4Address, 0, 10, 0x78, 0, TaskSystem.g_TaskListTail[10]);
                SoundEffects.FUN_80060364();   // see case 0.
                uVar4 = 4;
                uVar5 = 5;
                break;

            case 3:
                FileIo.ReadFile("\\SUB\\SPENT.B;1".ToCharArray(), VS_EXE_exe.Dat80110000Address, 0);
                TaskSystem.CreateTask(Lab800305ecAddress, 0, 10, 0x78, 0, TaskSystem.g_TaskListTail[10]);
                SoundEffects.FUN_80060364();   // see case 0.
                uVar4 = 2;
                uVar5 = 4;
                break;

            case 4:
                FileIo.ReadFile("\\SUB\\SPEXT.B;1".ToCharArray(), VS_EXE_exe.Dat80110000Address, 0);
                TaskSystem.CreateTask(Lab8002c504Address, 0, 10, 0x78, 0, TaskSystem.g_TaskListTail[10]);
                SoundEffects.FUN_80060364();   // see case 0.
                uVar4 = 4;
                uVar5 = 5;
                break;

            case 5:
                // MODES 4 AND 5 LOAD THE SAME FILE and differ only in the task and the two
                // constants. This is the second half of the block case 1 above shares with this one
                // in the image (the original's LAB_800296B4); see case 1's DEVIATION note for why
                // the port spells it twice.
                FileIo.ReadFile("\\SUB\\SPEXT.B;1".ToCharArray(), VS_EXE_exe.Dat80110000Address, 0);
                puVar3 = Lab8002e650Address;
                TaskSystem.CreateTask(Lab8002e650Address, 0, 10, 0x78, 0, TaskSystem.g_TaskListTail[10]);
                SoundEffects.FUN_80060364();   // see case 0.
                uVar4 = 4;
                uVar5 = 6;
                break;

            default:
                goto switchD_80029528_default;
        }

        // UNBLOCKED: both addresses are GAME code below rule 13's boundary and both now live in
        // VS_EXE/SoundEffects.cs. FUN_800605d8 takes two `ushort` there (its own prototype); the
        // two constants uVar4/uVar5 the switch just computed are its only consumers.
        // The two `_ =` discards below are gone with them; `puVar3` keeps its discard because the
        // DEVIATION recorded at case 1/5 spells the shared tail twice instead of jumping through it.
        SoundEffects.FUN_800605d8((ushort)uVar4, (ushort)uVar5);
        SoundEffects.FUN_800609ec();
        _ = puVar3;

    switchD_80029528_default:
        // FUN_80061bd8((int)&DAT_80110000 + DAT_80110004, &DAT_80110000) -- the table-driven image
        // loader that unpacks whatever the ReadFile above just staged at 0x80110000. VS_EXE_exe.cs
        // carries the real body and it is now `internal`. The first word of the staged file is the
        // byte offset of its own record table, which is what the first argument adds.
        VS_EXE_exe.FUN_80061bd8(
            PsxRam.ReadI32(VS_EXE_exe.Dat80110000Address + 4) + VS_EXE_exe.Dat80110000Address,
            VS_EXE_exe.Dat80110000Address);

        PrimitivePools.CreatePrimitivePools(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0);

        // NOTE THE INSERTION POINT: DAT_80083b3c is g_TaskListHead[0], not g_TaskListTail[0]. Every
        // other CreateTask call site in this port passes a TAIL. This one passes the head, and it is
        // what the image says -- the same symbol PrimitivePools.cs's own CreatePrimitivePools call
        // site passes for the same list. Reproduced, not corrected.
        TaskSystem.CreateTask(Fun800411b4Address, 0, 0, 0, 0, TaskSystem.g_TaskListHead[0]);

        // The task is created above AND called once directly, immediately, so its first frame runs
        // before the dispatcher ever reaches it. VS_EXE_exe.cs carries the real body, now `internal`.
        VS_EXE_exe.FUN_800411b4();

        POLY_FT4_800b2f30.r0 = 0xff;   // DAT_800b2f34
        POLY_FT4_800b2f30.g0 = 0xff;   // DAT_800b2f35
        POLY_FT4_800b2f30.b0 = 0xff;   // DAT_800b2f36
        DAT_8008d3a6 = 0xff;
        DAT_8008d3a5 = 0xff;
        DAT_8008d3a4 = 0xff;

        // The quad is rebuilt a SECOND time, after it was already built in state 0 and after
        // FUN_80053840 has just deleted the list-5 task the first build created. That second build
        // creates a second list-5 task. The original does this and the port does it too.
        FUN_800297b0();
    }

    // GHIDRA: LAB_80026888 @ 0x80026888 (VS.EXE)
    // Not a `FUN_` in Ghidra either: decompiled by address, preview `UndefinedFunction_80026888`,
    // and referenced once as a PARAMETER -- VS_EXE_exe.FUN_80026a68's
    // `TaskSystem.CreateTask(Lab80026888Address, 0, 0xb, 0, 1, g_TaskListTail[0xb])`. Created with
    // contextSize 0, so it has no workspace and reads none; everything it touches is the global
    // block.
    //
    // THE BLOCK IS VS_EXE_exe.DAT_8008D610, thirty 0x24-byte records, 0x438 bytes -- the extent
    // that file's own OWNERSHIP CAVEAT closed (FUN_80026a68 memsets exactly 0x438, and
    // 0x8008D610 + 0x438 lands exactly on FighterSetup.DAT_8008da48). That caveat asked the slice
    // that ported LAB_80026888 to use THAT array rather than declare a second one, and this does:
    // the records are reached by PsxRam at their PSX addresses, which resolve into the very byte[]
    // VS_EXE_exe.cs registered. No second declaration exists anywhere in this file.
    //
    // THE RECORD LAYOUT, closed by what this function reads and by nothing else:
    //   +0x00  int    live flag; zero means the slot is free
    //   +0x04  int    countdown, decremented every frame; going negative retires the slot
    //   +0x08  ushort x, offset by the scratchpad's own geometry origin
    //   +0x0A  short  y, taken raw
    //   +0x0C  ushort z, offset by the scratchpad's own geometry origin
    //   +0x10  int    the sprite-group record handed to the drawer as its param_1
    //   +0x14  int    the drawer's param_10, its ordering-table bias
    //   +0x18  short  the drawer's param_5 -- rotation in bits 0..11 plus its three flag bits
    //   +0x1A  ushort the drawer's param_11 (clut bias), OR'd with 0x8000 on the way in
    //   +0x1C  ushort packs the drawer's param_12 (tpage bias) in bits 6..15 and param_13 (a u bias)
    //                 in bits 0..5
    //   +0x1E  ushort low byte is the drawer's param_14 (a v bias); high byte is a second tpage term
    //   +0x20  int    a pointer to the record's OWNER; the byte at owner+0x227 is a live-slot count
    //                 this function decrements when the slot retires
    //
    // THE ORIGINAL'S TWO POINTERS, `piVar3 = &DAT_8008d610` and `puVar2 = 0x8008d620`, walk the same
    // records 0x10 bytes apart and step by 9 words each. Both are folded into one record base here;
    // that is a spelling change and not a control-flow one -- every load below is at the same
    // absolute address the original computes.
    //
    // THE COLOUR IS THE COUNTDOWN. param_15/16/17 are all `(countdown & 7) << 5`, read AFTER the
    // decrement, so the sprite's grey level cycles 0/0x20/.../0xE0 once every eight frames for the
    // whole life of the slot. That is a flicker, and it is the original's.
    //
    // THE RETURN IS DROPPED. Ghidra types it `undefined4` and its last statement is `return 0`, but
    // the dispatcher reaches it through `(*(code *)*puVar1)()` and never looks at v0 -- and
    // TaskSystem.RegisterCallback takes an `Action`, the same contract StageBackdrop.LAB_80040f78
    // already satisfies as a `void`. The constant zero has no reader on either side.
    internal static void LAB_80026888()
    {
        int piVar3 = Dat8008d610Address;
        int iVar4 = 0;

        do
        {
            if (PsxRam.ReadI32(piVar3) != 0)
            {
                int iVar1 = PsxRam.ReadI32(piVar3 + 4);
                PsxRam.WriteI32(piVar3 + 4, iVar1 + -1);
                if (iVar1 + -1 < 0)
                {
                    PsxRam.WriteI32(piVar3 + 4, 0);
                    PsxRam.WriteI32(piVar3, 0);

                    // A SIGNED byte decrement with no floor: the original reads it as `char`, and a
                    // count that was already zero wraps to -1. Reproduced.
                    int owner = PsxRam.ReadI32(piVar3 + 0x20);
                    PsxRam.WriteU8(owner + 0x227,
                        (byte)((sbyte)PsxRam.ReadU8(owner + 0x227) + -1));
                }
                else
                {
                    ushort uVar1c = PsxRam.ReadU16(piVar3 + 0x1c);
                    ushort uVar1e = PsxRam.ReadU16(piVar3 + 0x1e);

                    SpriteDrawer.DrawSpriteGroup(
                        PsxRam.ReadI32(piVar3 + 0x10),
                        (short)(PsxRam.ReadU16(piVar3 + 8) - (ushort)Scratchpad._DAT_1f8000b4),
                        (short)PsxRam.ReadU16(piVar3 + 0xa),
                        (short)(PsxRam.ReadU16(piVar3 + 0xc) - (ushort)Scratchpad._DAT_1f8000bc),
                        (ushort)(short)PsxRam.ReadU16(piVar3 + 0x18),
                        0,
                        0,
                        0x249,
                        0x249,
                        PsxRam.ReadI32(piVar3 + 0x14),
                        (short)(PsxRam.ReadU16(piVar3 + 0x1a) | 0x8000),
                        (short)(((uVar1c >> 6) + ((uVar1e >> 8) * 0x10)) | 0x20),
                        (sbyte)((uVar1c & 0x3f) << 2),
                        (sbyte)PsxRam.ReadU8(piVar3 + 0x1e),
                        (byte)((PsxRam.ReadI32(piVar3 + 4) & 7) << 5),
                        (byte)((PsxRam.ReadI32(piVar3 + 4) & 7) << 5),
                        (byte)((PsxRam.ReadI32(piVar3 + 4) & 7) << 5),
                        VS_EXE_exe.DAT_1f800128);
                }
            }

            piVar3 = piVar3 + 0x24;
            iVar4 = iVar4 + 1;
        } while (iVar4 < 0x1e);
    }

    // ================================================================================================
    // The transition's own leaves
    // ================================================================================================

    // GHIDRA: FUN_800297b0 @ 0x800297B0 (VS.EXE)
    // 312 bytes. Builds the full-screen fade quad at 0x800B2F30 and starts the list-5 task that
    // presumably submits it. Two callers, both in LAB_80029200 above (state 0 and the tail of state
    // 2), so it runs twice per transition and creates a second list-5 task the second time.
    //
    // THE TEXTURE IS ONE WHITE PIXEL. `local_10[0] = 0xffff` is a single 16-bit texel with every
    // bit set, uploaded as a 1x1 image at VRAM (0, 0x1FD) and then used as this quad's CLUT --
    // GetClut(0, 0x1FD) names the same coordinates. A one-entry palette of solid white, stretched
    // over the whole screen and semi-transparent: that is how the fade darkens or brightens the
    // frame without a texture of its own.
    //
    // THE UPLOAD HAPPENS AFTER the clut id is already stored at +0x0E. GetClut is pure arithmetic on
    // the coordinates -- it does not read VRAM -- so the order is harmless, and it is kept.
    //
    // NOTE THE WRITE ORDER: tpage (+0x16) is written second, before the clut, and the four v
    // coordinates are written before the four u coordinates. Not reordered into field order.
    internal static void FUN_800297b0()
    {
        LibGpu.SetPolyFT4(POLY_FT4_800b2f30);
        POLY_FT4_800b2f30.tpage = 0x50;                          // DAT_800b2f46
        POLY_FT4_800b2f30.clut = LibGpu.GetClut(0, 0x1fd);       // DAT_800b2f3e
        LibGpu.SetSemiTrans(POLY_FT4_800b2f30, 1);
        POLY_FT4_800b2f30.v0 = 0xff;                             // DAT_800b2f3d
        POLY_FT4_800b2f30.v1 = 0xff;                             // DAT_800b2f45
        POLY_FT4_800b2f30.v2 = 0xff;                             // DAT_800b2f4d
        POLY_FT4_800b2f30.v3 = 0xff;                             // DAT_800b2f55
        POLY_FT4_800b2f30.y2 = 0xf0;                             // DAT_800b2f4a
        POLY_FT4_800b2f30.y3 = 0xf0;                             // DAT_800b2f52

        MipsMemory.WriteU16(local_10, 0, 0xffff);

        POLY_FT4_800b2f30.u0 = 0;                                // DAT_800b2f3c
        POLY_FT4_800b2f30.u1 = 0;                                // DAT_800b2f44
        POLY_FT4_800b2f30.u2 = 0;                                // DAT_800b2f4c
        POLY_FT4_800b2f30.u3 = 0;                                // DAT_800b2f54
        POLY_FT4_800b2f30.x0 = 0;                                // DAT_800b2f38
        POLY_FT4_800b2f30.y0 = 0;                                // DAT_800b2f3a
        POLY_FT4_800b2f30.x1 = 0x140;                            // DAT_800b2f40
        POLY_FT4_800b2f30.y1 = 0;                                // DAT_800b2f42
        POLY_FT4_800b2f30.x2 = 0;                                // DAT_800b2f48
        POLY_FT4_800b2f30.x3 = 0x140;                            // DAT_800b2f50

        FileIo.LoadImage_ReturnTPageOrClutId(Local10Address, 0, 0x1fd, 1, 1, 0);
        TaskSystem.CreateTask(Lab800298e8Address, 0, 5, 0, 0, TaskSystem.g_TaskListTail[5]);
    }

    // GHIDRA: FUN_800299e8 @ 0x800299E8 (VS.EXE)
    // 176 bytes, one caller (LAB_80029200's state 1). Adds param_1 to each of the three fade
    // channels, clamps each at 0xFF, and returns true only when all three were ALREADY at 0xFF on
    // entry -- not when they arrive there. So the caller needs one extra frame after saturation
    // before it moves on, and that off-by-one is the original's, reproduced.
    //
    // THE THREE ARMS ARE NOT WRITTEN THE SAME WAY and are not made uniform here: for the red channel
    // the sum is computed inside the `else`, for green and blue the compiler hoisted it above the
    // `== 0xff` test. Arithmetically identical, textually not, and the mandate asks for the text.
    //
    // Ghidra types the return `bool`; the caller compares it against 0. Kept as `bool` with the
    // caller's comparison written as `== false`.
    internal static bool FUN_800299e8(int param_1)
    {
        int iVar1;
        int iVar2 = 0;

        if (DAT_8008d3a4 == 0xff)
        {
            iVar2 = 1;
        }
        else
        {
            iVar1 = DAT_8008d3a4 + param_1;
            if (0xfe < iVar1)
            {
                iVar1 = 0xff;
            }

            DAT_8008d3a4 = (byte)iVar1;
        }

        iVar1 = DAT_8008d3a5 + param_1;
        if (DAT_8008d3a5 == 0xff)
        {
            iVar2 = iVar2 + 1;
        }
        else
        {
            if (0xfe < iVar1)
            {
                iVar1 = 0xff;
            }

            DAT_8008d3a5 = (byte)iVar1;
        }

        param_1 = DAT_8008d3a6 + param_1;
        if (DAT_8008d3a6 == 0xff)
        {
            iVar2 = iVar2 + 1;
        }
        else
        {
            if (0xfe < param_1)
            {
                param_1 = 0xff;
            }

            DAT_8008d3a6 = (byte)param_1;
        }

        return iVar2 == 3;
    }

    // GHIDRA: FUN_80053840 @ 0x80053840 (VS.EXE)
    // 304 bytes. Deletes EVERY deletable task on one list, by repeatedly unlinking whatever is at
    // the head until the list's count reaches zero. TaskSystem.cs's DeleteTask @ 0x8005354C is the
    // same unlink, node by node; this is the sweep.
    //
    // WHY IT IS HERE AND NOT IN TaskSystem.cs: this slice may create no file but its own, and
    // TaskSystem.cs does not currently declare 0x80053840 -- checked by searching the address across
    // all of VS_EXE/ before writing this. If a later slice moves it beside its siblings, it must
    // MOVE it, not copy it.
    //
    // THE ARGUMENT OF THE RELEASE CALL is closed by disassembly, not by the decompilation: Ghidra
    // prints a bare `FUN_800631c8()` because the callee has no applied prototype, and its `iVar2`
    // has already been reassigned to the successor by then. The raw instructions settle it --
    // `lw a0, 0x0(a1)` at the top of the body loads the head node into a0, and every instruction
    // between there and `jal 0x800631C8` reads through a0 (`lhu v0,0x2(a0)`, `lw v0,0x10(a0)`,
    // `lw v1,0x10(a0)`, `lw v0,0x14(a0)`) without ever writing it. The argument is the node. Same
    // evidence, same shape, and the same conclusion TaskSystem.DeleteTask's own comment reached.
    //
    // THIS LOOPS FOREVER on a list whose count is non-zero but whose head is either null or flagged
    // undeletable (+0x02 bit 1). The `while` re-tests the count, the count is only decremented
    // inside the guard, and nothing advances a cursor: if the guard fails once it fails every time.
    // That is a hang in the original and it is reproduced under rule 12 -- not converted into a
    // cursor walk, not given a bail-out.
    internal static void FUN_80053840(uint param_1)
    {
        int psVar3 = (int)(param_1 & 0xffff);

        if (TaskSystem.g_TaskListCount[psVar3] != 0)
        {
            do
            {
                // `pTVar` is the a0 the disassembly note above describes: the head node, loaded once
                // and never rewritten. Ghidra reuses its own `iVar2` for both the node and, later,
                // the successor, which is why the release call in its output has no argument left to
                // print. TaskSystem.ExecuteTaskList carries the identical `pTVar3` for the identical
                // reason.
                int pTVar = TaskSystem.g_TaskListHead[(int)(param_1 & 0xffff)];
                int iVar2 = pTVar;
                if ((iVar2 != 0) && ((PsxRam.ReadU16(iVar2 + 2) & 2) == 0))
                {
                    if (iVar2 == TaskSystem.g_CurrentTask)
                    {
                        TaskSystem.g_CurrentTask = PsxRam.ReadI32(iVar2 + 0x10);
                    }

                    int iVar1 = PsxRam.ReadI32(iVar2 + 0x10);
                    iVar2 = PsxRam.ReadI32(iVar2 + 0x14);
                    if (iVar1 == 0)
                    {
                        TaskSystem.g_TaskListHead[(int)(param_1 & 0xffff)] = iVar2;
                    }
                    else
                    {
                        PsxRam.WriteI32(iVar1 + 0x14, iVar2);
                    }

                    if (iVar2 == 0)
                    {
                        TaskSystem.g_TaskListTail[(int)(param_1 & 0xffff)] = iVar1;
                    }
                    else
                    {
                        PsxRam.WriteI32(iVar2 + 0x10, iVar1);
                    }

                    Heap.FUN_800631c8(pTVar);

                    TaskSystem.g_TaskListCount[psVar3] =
                        (short)(TaskSystem.g_TaskListCount[psVar3] - 1);
                }
            } while (TaskSystem.g_TaskListCount[psVar3] != 0);
        }
    }

    // ================================================================================================
    // The two functions the brief asked for that are EMPTY STUBS in files this slice does not own
    // ================================================================================================

    // GHIDRA: FUN_80026424 @ 0x80026424 (VS.EXE)
    // 180 bytes. THE REAL BODY. VS_EXE/FighterCombat.cs currently carries an empty
    // `private static void FUN_80026424(int param_1)` at the same address, with one call site in
    // that file; the main session must delete that stub and qualify the call as
    // `SceneTransition.FUN_80026424(fighter)`. See the report for the exact lines.
    //
    // A ONE-FRAME HISTORY SHIFT REGISTER. Two bytes at +0x232 and +0x233 are each shifted left by
    // one every call, and bit 0 of each is set when this frame's action id (the byte at +0x16A)
    // falls in that byte's own set. So each byte remembers the last eight frames of one predicate:
    //   +0x232  action id 0x13 or 0x14
    //   +0x233  action id 0x26, 0x27 or 0x28
    // Which predicates those are is NOT closed -- no name is put on them here.
    //
    // FOUR ACTION IDS SKIP THE SHIFT ENTIRELY: 0, 2, 0x0A and 0x2A return before it, so a frame
    // spent in one of those neither records a hit nor ages the history. That asymmetry is the
    // original's; the flag at +0x22C bit 0 is still set for all of them, because that store happens
    // before the tests.
    //
    // THE SHIFT IS SIGNED-THEN-TRUNCATED. `*(char *)(param_1 + 0x232) << 1` reads the byte as a
    // SIGNED char, shifts in 32 bits and stores back one byte; the visible effect is the same as an
    // unsigned shift, and the form is kept so the read width stays what the image says it is.
    //
    // THE STORE HAPPENS TWICE per byte -- once with the shifted value, once with bit 0 possibly
    // added -- because the original stores after the shift and again after the conditional OR. Both
    // stores are reproduced; collapsing them into one would be an optimisation.
    internal static void FUN_80026424(int param_1)
    {
        uint uVar1 = PsxRam.ReadU8(param_1 + 0x16a);
        PsxRam.WriteU8(param_1 + 0x22c, (byte)(PsxRam.ReadU8(param_1 + 0x22c) | 1));

        if (uVar1 != 2)
        {
            if (uVar1 < 3)
            {
                if (uVar1 == 0)
                {
                    return;
                }
            }
            else
            {
                if (uVar1 == 10)
                {
                    return;
                }

                if (uVar1 == 0x2a)
                {
                    return;
                }
            }

            byte bVar2 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x232) << 1);
            PsxRam.WriteU8(param_1 + 0x232, bVar2);
            if (uVar1 - 0x13 < 2)
            {
                bVar2 = (byte)(bVar2 | 1);
            }

            PsxRam.WriteU8(param_1 + 0x232, bVar2);

            bVar2 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x233) << 1);
            PsxRam.WriteU8(param_1 + 0x233, bVar2);
            if ((uVar1 - 0x26 < 2) || (uVar1 == 0x28))
            {
                bVar2 = (byte)(bVar2 | 1);
            }

            PsxRam.WriteU8(param_1 + 0x233, bVar2);
        }
    }

    // ================================================================================================
    // The sub-state steppers of the 0x8002C000..0x80032000 stretch
    // ================================================================================================

    // GHIDRA: FUN_80031dc8 @ 0x80031DC8 (VS.EXE)
    // 108 bytes, four callers (0x800294A4, 0x80031DE4 -- itself, per FighterAiSupport.cs's own note
    // -- LAB_8002B29C and LAB_8002EDB4), none of them ported. Re-checks the memory card and, when
    // the answer is no, wipes the eighteen save-table words at 0x801FF200.
    //
    // THE PROBE RESULT 4 IS THE ONLY ONE THAT RE-ASKS: result 0 means "card present" and returns
    // true without asking again, anything else but 4 returns false, and 4 alone runs
    // MemoryCard.FUN_80022a88 and reports what it says. The three constants are reproduced without
    // interpretation.
    //
    // THE CLEAR LOOP walks DOWNWARD from `&DAT_801ff244` for eighteen words, which is
    // INT_ARRAY_801ff200[0x11] down to [0], the same eighteen words SharedHighRam.cs's own comment
    // says the clear path zeroes and the same eighteen LAB_80029200 above zeroes upward. Written
    // downward here because that is the direction the original walks; the result is identical and
    // the direction is free, but the mandate asks for the original's form.
    internal static bool FUN_80031dc8()
    {
        bool bVar3 = true;

        if (SharedHighRam.g_CardProbeResult == 4)
        {
            int iVar1 = MemoryCard.FUN_80022a88();
            bVar3 = iVar1 != 0;
        }
        else if (SharedHighRam.g_CardProbeResult != 0)
        {
            bVar3 = false;
        }

        if (bVar3 == false)
        {
            int iVar1 = 0x11;
            int puVar2 = 0x11;
            do
            {
                SharedHighRam.INT_ARRAY_801ff200[puVar2] = 0;
                iVar1 = iVar1 + -1;
                puVar2 = puVar2 + -1;
            } while (-1 < iVar1);
        }

        return bVar3;
    }

    // GHIDRA: FUN_80031e34 @ 0x80031E34 (VS.EXE)
    // 120 bytes. Subtracts param_3/param_4/param_5 from three halfwords of a record and floors each
    // at zero.
    //
    // THE RECORD IS SELECTED BY A HALFWORD INDEX: `param_1 = ((param_2 << 0x10) >> 0xf) + param_1`
    // is the low sixteen bits of param_2, sign-extended, times TWO -- shift left sixteen then
    // arithmetic-right FIFTEEN, not sixteen. So the stride is two bytes and the three fields it then
    // reaches (+0x30, +0x38, +0x40) are eight bytes apart in a structure whose rows are two bytes
    // apart, which is not a contradiction but a base offset the caller supplies; no reading of the
    // layout is asserted here.
    //
    // THE FLOOR TEST IS `x * 0x10000 < 1`, i.e. the sign-extended low halfword is <= 0. Reproduced
    // in that form rather than as `<= 0` on a short, because the value stored a line earlier is a
    // truncated short while the value TESTED is the untruncated 32-bit difference re-truncated --
    // for a subtraction that cannot overflow the two agree, and the form is the evidence.
    internal static void FUN_80031e34(int param_1, int param_2, int param_3, int param_4, int param_5)
    {
        param_1 = ((param_2 << 0x10) >> 0xf) + param_1;

        param_3 = PsxRam.ReadU16(param_1 + 0x30) - param_3;
        PsxRam.WriteU16(param_1 + 0x30, (ushort)(short)param_3);
        if (param_3 * 0x10000 < 1)
        {
            PsxRam.WriteU16(param_1 + 0x30, 0);
        }

        param_4 = PsxRam.ReadU16(param_1 + 0x38) - param_4;
        PsxRam.WriteU16(param_1 + 0x38, (ushort)(short)param_4);
        if (param_4 * 0x10000 < 1)
        {
            PsxRam.WriteU16(param_1 + 0x38, 0);
        }

        param_5 = PsxRam.ReadU16(param_1 + 0x40) - param_5;
        PsxRam.WriteU16(param_1 + 0x40, (ushort)(short)param_5);
        if (param_5 * 0x10000 < 1)
        {
            PsxRam.WriteU16(param_1 + 0x40, 0);
        }
    }

    // GHIDRA: FUN_80031eac @ 0x80031EAC (VS.EXE)
    // 104 bytes. Subtracts one signed byte from each of three bytes at +0x23, +0x24 and +0x25 and
    // floors each at zero. The three bytes are the same trio SceneGeometry.FUN_80031ab8 @ 0x80031AB8
    // writes (0x80/0x40 per its selector), which is where their meaning would have to be closed;
    // nothing here closes it and nothing here names them.
    //
    // THE FLOOR IS A SIGN-BIT TEST ON THE STORED BYTE, `(bVar1 & 0x80) != 0`, not a comparison
    // against zero: subtracting 8 from 0x84 gives 0x7C and passes, subtracting 8 from 4 gives 0xFC
    // and is floored. So a value in 0x80..0xFF is treated as already negative and is zeroed on the
    // spot. Reproduced exactly.
    internal static void FUN_80031eac(int param_1, sbyte param_2)
    {
        byte bVar1 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x23) - param_2);
        PsxRam.WriteU8(param_1 + 0x23, bVar1);
        if ((bVar1 & 0x80) != 0)
        {
            PsxRam.WriteU8(param_1 + 0x23, 0);
        }

        bVar1 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x24) - param_2);
        PsxRam.WriteU8(param_1 + 0x24, bVar1);
        if ((bVar1 & 0x80) != 0)
        {
            PsxRam.WriteU8(param_1 + 0x24, 0);
        }

        bVar1 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x25) - param_2);
        PsxRam.WriteU8(param_1 + 0x25, bVar1);
        if ((bVar1 & 0x80) != 0)
        {
            PsxRam.WriteU8(param_1 + 0x25, 0);
        }
    }

    // GHIDRA: FUN_80031f14 @ 0x80031F14 (VS.EXE)
    // 92 bytes. One frame of a two-halfword slide toward zero, at 0x80 per frame: +0x2C climbs and
    // is snapped to 0 the moment it stops being negative, +0x2E falls and is snapped to 0 the moment
    // it stops being positive. When BOTH have landed -- tested as one 32-bit read of the pair being
    // zero, not as two halfword tests -- the record's state word at +0x00 becomes 5.
    //
    // THE TWO GUARDS ARE NOT SYMMETRIC: the first is `-1 < (sign-extended low halfword)`, the second
    // is `(sign-extended low halfword) < 1`. Zero satisfies both, which is why the pair can land at
    // all; the asymmetry is the original's and is not tidied.
    internal static void FUN_80031f14(int param_1)
    {
        short sVar1 = (short)PsxRam.ReadU16(param_1 + 0x2c);
        PsxRam.WriteU16(param_1 + 0x2c, (ushort)(sVar1 + 0x80));
        if (-1 < (int)((uint)(ushort)(sVar1 + 0x80) << 0x10))
        {
            PsxRam.WriteU16(param_1 + 0x2c, 0);
        }

        sVar1 = (short)PsxRam.ReadU16(param_1 + 0x2e);
        PsxRam.WriteU16(param_1 + 0x2e, (ushort)(sVar1 - 0x80));
        if ((int)((uint)(ushort)(sVar1 - 0x80) << 0x10) < 1)
        {
            PsxRam.WriteU16(param_1 + 0x2e, 0);
        }

        if (PsxRam.ReadI32(param_1 + 0x2c) == 0)
        {
            PsxRam.WriteU16(param_1, 5);
        }
    }

    // GHIDRA: FUN_80031f70 @ 0x80031F70 (VS.EXE)
    // 112 bytes. The mirror image of FUN_80031f14: the same two halfwords slide the same 0x80 per
    // frame, but AWAY from zero and with a WRAP instead of a clamp -- +0x2C wraps to 0xFDB0 once it
    // passes 0x24F, +0x2E wraps to 0x250 once it passes -0x24F. The terminal test is again one
    // 32-bit read of the pair, this time against 0x0250FDB0, which is exactly the two wrap constants
    // side by side (low halfword 0xFDB0 at +0x2C, high halfword 0x0250 at +0x2E). The state word
    // then becomes 2.
    //
    // SO THE PAIR ONLY TERMINATES ON THE FRAME BOTH WRAP AT ONCE, and since they start from the
    // values FUN_800304f0 and FUN_8002c424 write (0xFDB0 and 0x250) and step by the same 0x80 in
    // opposite directions, they wrap together on every pass. The apparent fragility is not a bug in
    // the transcription.
    internal static void FUN_80031f70(int param_1)
    {
        short sVar1 = (short)PsxRam.ReadU16(param_1 + 0x2c);
        PsxRam.WriteU16(param_1 + 0x2c, (ushort)(sVar1 + 0x80));
        if (0x24f < (short)(sVar1 + 0x80))
        {
            PsxRam.WriteU16(param_1 + 0x2c, 0xfdb0);
        }

        sVar1 = (short)PsxRam.ReadU16(param_1 + 0x2e);
        PsxRam.WriteU16(param_1 + 0x2e, (ushort)(sVar1 + -0x80));
        if ((short)(sVar1 + -0x80) < -0x24f)
        {
            PsxRam.WriteU16(param_1 + 0x2e, 0x250);
        }

        if (PsxRam.ReadI32(param_1 + 0x2c) == 0x250fdb0)
        {
            PsxRam.WriteU16(param_1, 2);
        }
    }

    // GHIDRA: FUN_80031fe0 @ 0x80031FE0 (VS.EXE)
    // 340 bytes, the largest of the steppers. A TWO-PHASE fade-out on the record's own sub-state at
    // +0x2A:
    //
    //   sub-state 0  ramp the three GLOBAL fade channels down by 8 per frame, each floored at zero,
    //                and count frames at +0x26; on the 0x21st frame (`0x1f < counter + 1`) step the
    //                sub-state -- by INCREMENTING it, so the counter at +0x26 is left at 0x20 and is
    //                only reset by the other phase;
    //   sub-state 1  ramp the record's OWN three bytes at +0x23, +0x24 and +0x25 down by 8 per
    //                frame, floored by the same sign-bit test FUN_80031eac uses, and when the first
    //                of the three reaches zero reset the sub-state and the counter and step the
    //                record's state word at +0x00.
    //
    // NOTE WHICH THREE BYTES PHASE 1 TOUCHES: +0x23, then the LOW BYTE of the halfword at +0x24
    // (Ghidra spells it `(char)param_1[0x12]`, a byte read through a short lvalue), then +0x25. The
    // middle one is the same +0x24 byte FUN_80031eac ramps; the port reads and writes it as a byte,
    // which is what the image does.
    //
    // AND NOTE WHICH ONE ENDS THE PHASE: only +0x23 is tested. If +0x24 or +0x25 started higher they
    // are simply left wherever the last frame put them. The original's, unaltered.
    //
    // THE GLOBAL RAMP'S GUARD IS `!= 0`, NOT `> 0`: a channel already at zero is skipped entirely
    // rather than clamped, so the subtraction's own floor only ever fires on a channel in 1..7.
    internal static void FUN_80031fe0(int param_1)
    {
        if (PsxRam.ReadU16(param_1 + 0x2a) == 0)
        {
            int iVar3 = DAT_8008d3a4 - 8;
            if (DAT_8008d3a4 != 0)
            {
                if (iVar3 < 0)
                {
                    iVar3 = 0;
                }

                DAT_8008d3a4 = (byte)iVar3;
            }

            iVar3 = DAT_8008d3a5 - 8;
            if (DAT_8008d3a5 != 0)
            {
                if (iVar3 < 0)
                {
                    iVar3 = 0;
                }

                DAT_8008d3a5 = (byte)iVar3;
            }

            iVar3 = DAT_8008d3a6 - 8;
            if (DAT_8008d3a6 != 0)
            {
                if (iVar3 < 0)
                {
                    iVar3 = 0;
                }

                DAT_8008d3a6 = (byte)iVar3;
            }

            short sVar1 = (short)PsxRam.ReadU16(param_1 + 0x26);
            PsxRam.WriteU16(param_1 + 0x26, (ushort)(sVar1 + 1));
            if (0x1f < (short)(sVar1 + 1))
            {
                PsxRam.WriteU16(param_1 + 0x2a, (ushort)(PsxRam.ReadU16(param_1 + 0x2a) + 1));
            }
        }
        else if (PsxRam.ReadU16(param_1 + 0x2a) == 1)
        {
            byte bVar2 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x23) - 8);
            PsxRam.WriteU8(param_1 + 0x23, bVar2);
            if ((bVar2 & 0x80) != 0)
            {
                PsxRam.WriteU8(param_1 + 0x23, 0);
            }

            bVar2 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x24) - 8);
            PsxRam.WriteU8(param_1 + 0x24, bVar2);
            if ((bVar2 & 0x80) != 0)
            {
                PsxRam.WriteU8(param_1 + 0x24, 0);
            }

            bVar2 = (byte)((sbyte)PsxRam.ReadU8(param_1 + 0x25) - 8);
            PsxRam.WriteU8(param_1 + 0x25, bVar2);
            if ((bVar2 & 0x80) != 0)
            {
                PsxRam.WriteU8(param_1 + 0x25, 0);
            }

            if ((sbyte)PsxRam.ReadU8(param_1 + 0x23) == 0)
            {
                PsxRam.WriteU16(param_1 + 0x2a, 0);
                PsxRam.WriteU16(param_1 + 0x26, 0);
                PsxRam.WriteU16(param_1, (ushort)(PsxRam.ReadU16(param_1) + 1));
            }
        }
    }

    // GHIDRA: FUN_80030548 @ 0x80030548 (VS.EXE)
    // 164 bytes. Copies three save-slot fields out of the shared high-RAM table into a record's
    // +0x10, +0x14 and +0x18, or zeroes them.
    //
    // THE LOAD WIDTHS ARE FROM THE IMAGE, NOT FROM GHIDRA'S PRINTOUT. Raw MIPS at 0x80030548 onward:
    //   lui v0,0x8020 / lhu v0,0xF218(v0)   -> a 16-bit UNSIGNED read of 0x801FF218
    //   andi v0,v0,1 / beq v0,zero,+7
    //   lui v0,0x8020 / lhu v0,0xF21A(v0)   -> 16-bit unsigned read of 0x801FF21A
    //   addiu v0,v0,1 / sw v0,0x10(a0)      -> stored as a WORD, one greater
    // and the same three-instruction shape again for 0x801FF228/0x801FF22A into +0x14 and for
    // 0x801FF238/0x801FF23A into +0x18. So every source is a halfword and every destination a word.
    //
    // THE `+ 1` IS ONLY IN THIS FUNCTION. Its near-twin FUN_8002C478 below reads the same shaped
    // table eight halfwords lower and stores the value unchanged. The two are not merged and the
    // difference is not reconciled.
    //
    // Reached through SharedHighRam's byte region by PSX address rather than through
    // INT_ARRAY_801ff200: the reads are halfword reads at odd word offsets (0x218, 0x21A) which that
    // int window cannot spell.
    internal static void FUN_80030548(int param_1)
    {
        if ((PsxRam.ReadU16(unchecked((int)0x801FF218)) & 1) == 0)
        {
            PsxRam.WriteI32(param_1 + 0x10, 0);
        }
        else
        {
            PsxRam.WriteI32(param_1 + 0x10, PsxRam.ReadU16(unchecked((int)0x801FF21A)) + 1);
        }

        if ((PsxRam.ReadU16(unchecked((int)0x801FF228)) & 1) == 0)
        {
            PsxRam.WriteI32(param_1 + 0x14, 0);
        }
        else
        {
            PsxRam.WriteI32(param_1 + 0x14, PsxRam.ReadU16(unchecked((int)0x801FF22A)) + 1);
        }

        if ((PsxRam.ReadU16(unchecked((int)0x801FF238)) & 1) == 0)
        {
            PsxRam.WriteI32(param_1 + 0x18, 0);
        }
        else
        {
            PsxRam.WriteI32(param_1 + 0x18, PsxRam.ReadU16(unchecked((int)0x801FF23A)) + 1);
        }
    }

    // GHIDRA: FUN_8002c478 @ 0x8002C478 (VS.EXE)
    // 140 bytes. FUN_80030548's twin, over the FIRST three save slots (0x801FF200/202,
    // 0x801FF208/20A, 0x801FF210/212) and WITHOUT the `+ 1`.
    //
    // Same evidence, from the raw MIPS at 0x8002C478: `lui v0,0x8020 / lhu v0,0xF200(v0) /
    // andi v0,v0,1 / beq +5 / lui / lhu v0,0xF202(v0) / j / sw v0,0x10(a0)` and then the same shape
    // twice more. Halfword sources, word destinations, no increment anywhere.
    //
    // The three flag halfwords are eight bytes apart and the three value halfwords two bytes above
    // each flag: that is the six-entry, eight-byte save-slot table SharedHighRam.cs's own comment on
    // INT_ARRAY_801ff200 describes, read here as three of its six rows.
    internal static void FUN_8002c478(int param_1)
    {
        if ((PsxRam.ReadU16(unchecked((int)0x801FF200)) & 1) == 0)
        {
            PsxRam.WriteI32(param_1 + 0x10, 0);
        }
        else
        {
            PsxRam.WriteI32(param_1 + 0x10, PsxRam.ReadU16(unchecked((int)0x801FF202)));
        }

        if ((PsxRam.ReadU16(unchecked((int)0x801FF208)) & 1) == 0)
        {
            PsxRam.WriteI32(param_1 + 0x14, 0);
        }
        else
        {
            PsxRam.WriteI32(param_1 + 0x14, PsxRam.ReadU16(unchecked((int)0x801FF20A)));
        }

        if ((PsxRam.ReadU16(unchecked((int)0x801FF210)) & 1) == 0)
        {
            PsxRam.WriteI32(param_1 + 0x18, 0);
        }
        else
        {
            PsxRam.WriteI32(param_1 + 0x18, PsxRam.ReadU16(unchecked((int)0x801FF212)));
        }
    }

    // GHIDRA: FUN_800304f0 @ 0x800304F0 (VS.EXE)
    // 88 bytes. Arms the record FUN_80031f70 then slides: seeds the halfword pair at +0x2C with the
    // very wrap constants 0xFDB0 and 0x250 that that function tests for, clears +0x30, +0x32 and
    // +0x34, runs SceneGeometry.FUN_80031ab8 over the record's own +0x22..+0x25 byte quartet, clears
    // the sub-state at +0x2A, and sets the state word to 2.
    //
    // THE ARGUMENT OF THE FIRST CALL is closed by disassembly, not by Ghidra's printout: Ghidra
    // prints `FUN_80030548()` with no argument because the callee has no applied prototype. The raw
    // instructions of the identical sibling FUN_8002c424 (below, same shape, same two calls) show
    // `jal FUN_8002c478` with `addu s0,a0,zero` in the DELAY SLOT -- a0 is still the incoming
    // param_1 when the branch is taken, and is only copied to s0 after. Same construction here. The
    // argument is param_1.
    internal static void FUN_800304f0(int param_1)
    {
        FUN_80030548(param_1);
        PsxRam.WriteU16(param_1 + 0x2c, 0xfdb0);
        PsxRam.WriteU16(param_1 + 0x2e, 0x250);
        PsxRam.WriteU16(param_1 + 0x30, 0);
        PsxRam.WriteU16(param_1 + 0x32, 0);
        PsxRam.WriteU16(param_1 + 0x34, 0);
        SceneGeometry.FUN_80031ab8(param_1);
        PsxRam.WriteU16(param_1 + 0x2a, 0);
        PsxRam.WriteU16(param_1, 2);
    }

    // GHIDRA: FUN_8002c424 @ 0x8002C424 (VS.EXE)
    // 84 bytes. FUN_800304f0's twin, four bytes shorter for exactly one reason: it does NOT clear
    // +0x34. Everything else -- the seed values, the FUN_80031ab8 call, the cleared sub-state, the
    // state word 2 -- is identical, and the sibling it opens with is FUN_8002c478 rather than
    // FUN_80030548.
    //
    // The raw MIPS of this function is what closed the argument of both openings; see FUN_800304f0's
    // note above for the decode.
    internal static void FUN_8002c424(int param_1)
    {
        FUN_8002c478(param_1);
        PsxRam.WriteU16(param_1 + 0x2c, 0xfdb0);
        PsxRam.WriteU16(param_1 + 0x2e, 0x250);
        PsxRam.WriteU16(param_1 + 0x30, 0);
        PsxRam.WriteU16(param_1 + 0x32, 0);
        SceneGeometry.FUN_80031ab8(param_1);
        PsxRam.WriteU16(param_1 + 0x2a, 0);
        PsxRam.WriteU16(param_1, 2);
    }

    // GHIDRA: FUN_80055dfc @ 0x80055DFC (VS.EXE)
    // 64 bytes, the smallest function in the brief. Clears the TOP BIT of the word at +0x134 of a
    // fighter record, but only when that bit is already set AND the pointer at +0x0F4 equals
    // `*(int *)(record + 0xac) + 0xf8`.
    //
    // WHAT THE SECOND TEST MEANS is closed by a shape this port already uses elsewhere:
    // FighterAction.cs and FighterCombat.cs both reach the fighter's OPPONENT as
    // `*(int *)(*(int *)(fighter + 0xac) + 8)` -- +0xAC is a pointer to a shared battle record and
    // +0x08 of that record is the current fighter. Here the offset is +0xF8, not +0x08, so the
    // comparison is against a DIFFERENT field of that same shared record, and no reading is asserted
    // beyond "the record at +0x0F4 is the one the shared record names at +0xF8". Left raw.
    //
    // BOTH the guard's sign test and the clear use 0x7FFFFFFF / the sign bit, so the field at +0x134
    // is a flag word whose top bit is a one-shot; nothing here names it.
    internal static void FUN_80055dfc(int param_1)
    {
        if ((PsxRam.ReadI32(param_1 + 0x134) < 0) &&
            (PsxRam.ReadI32(param_1 + 0xf4) == PsxRam.ReadI32(PsxRam.ReadI32(param_1 + 0xac) + 8) + 0xf8))
        {
            PsxRam.WriteI32(param_1 + 0x134,
                (int)((uint)PsxRam.ReadI32(param_1 + 0x134) & 0x7fffffff));
        }
    }

    // ================================================================================================
    // NOT PORTED, and why
    // ================================================================================================

    // GHIDRA: BuildSlotDigitQuads @ 0x80058338 (VS.EXE), 2440 bytes
    // NOT WRITTEN HERE, and not because of the tables. VS_EXE/BattleManager.cs ALREADY DECLARES this
    // address, as a two-parameter BLOCKED stub with its own long note. Writing a second body over
    // the same address in this file would be precisely defect class 1 -- one address declared in two
    // files, with the empty stub silently winning at its one call site inside BattleManager. The
    // function belongs to the file that already owns it and must be completed there.
    //
    // ON WHETHER ITS TWO POINTER TABLES CAN BE CLOSED NOW, which is the question the brief actually
    // asks. They are two different problems and only one of them is still open. Read from the image:
    //
    //   PTR_DAT_80084124 @ 0x80084124 -- entries 0x80084014, 0x80084024, 0x80084034 ... rising by
    //   0x10, i.e. sixteen-byte rows in INITIALISED .data sitting IMMEDIATELY BELOW the pointer
    //   table that indexes them: the rows run 0x80084014..0x80084123 and the table starts at
    //   0x80084124, with no gap. That is exactly the "contiguous, self-referential span" test
    //   BattleManager's own note applies to FUN_80057a7c's tables and accepts. This table CAN be
    //   closed and embedded today.
    //
    //   PTR_DAT_80083fb4 @ 0x80083FB4 -- entries 0x8008D188, 0x8008D190, 0x8008D198 ... rising by 8.
    //   Those targets are NOT in .data. 0x8008D188 is inside the gp-relative small-data region
    //   (gp = 0x8008D0FC for VS.EXE, so this is gp + 0x8C) whose other occupants in this port are
    //   all mutable runtime state -- TaskSystem.g_CurrentTask at 0x8008D16C is gp + 0x70, thirty-two
    //   bytes away. Embedding a byte image of a span that something else writes at runtime would
    //   bake one frame of state in as if it were a constant. That table CANNOT be closed on the
    //   evidence available here, and the objection is unchanged from BattleManager's own note.
    //
    // WHAT WOULD CLOSE IT, smallest step first: find the WRITER of 0x8008D188..(0x8008D188 + 8 * n).
    // Ghidra's find-cross-references on the pointer table itself gives nothing beyond BuildSlotDigitQuads,
    // but the targets are addressable through gp and a gp-relative store will not show as a
    // reference to the symbol. The search that would settle it is a scan of the overlay's
    // instructions for `sb`/`sh`/`sw` with base gp and an offset in 0x8C..0x8C + 8 * n -- decoded
    // from data/VS.EXE directly (load 0x80020000, header 0x800) rather than asked of Ghidra. If
    // nothing writes them, the span is .bss that is only ever read, the eight-byte rows are constant
    // zero, and the function closes; if something does write them, the port needs that writer first
    // and BuildSlotDigitQuads stays blocked until it exists. Either way the answer is one scan away, and it
    // is a scan this slice did not run because the function is not this slice's to write.

    // GHIDRA: FUN_800261ec @ 0x800261EC (VS.EXE), 304 bytes
    // BLOCKED, and this one IS a table problem. The body itself is trivial -- four early returns,
    // then a pointer chosen out of one of three tables, then a fixed offset added to it by a
    // six-arm switch on the byte at +0x16B, then `FUN_8002631c(param_1, thatPointer)`. What it
    // cannot be given is the pointer.
    //
    // THE CHAIN IS THREE LEVELS DEEP, read from the image:
    //   the byte at record+0x22D indexes PTR_DAT_800807a4 @ 0x800807A4 (entries 0x800802DC,
    //   0x800802E8, 0x800802F4 ... rising by 0x0C);
    //   BYTE [8] of the twelve-byte row that lands on indexes PTR_DAT_800802c4 @ 0x800802C4
    //   (entries 0x8008012C, 0x80080150, 0x80080174, 0x80080198, 0x800801BC, 0x800801E0 -- rising by
    //   0x24);
    //   and the 0x24-byte block THAT lands on is what the switch offsets by 0, 6, 0x0C, 0x12, 0x18 or
    //   0x1E -- six rows of six bytes -- before FighterAi.FUN_8002631c reads five bytes out of it.
    // The two flag-driven alternatives are single pointers, PTR_DAT_80080a58 and PTR_DAT_80080a7c,
    // and BOTH hold 0x800809C0: the `& 0x40` branch that appears to choose between them chooses the
    // same block either way. That is what the .data says and it would be reproduced, not repaired.
    //
    // WHY IT IS NOT EMBEDDED. The first two levels have no closed EXTENT. PTR_DAT_800807a4's row
    // count is bounded only by the range of the byte at record+0x22D, which nothing in this slice
    // traces to a writer, so there is no honest size for the RamRegion; and PTR_DAT_800802c4's own
    // six entries do not reach the address the table itself starts at (0x800801E0 + 0x24 = 0x80080204
    // against a table base of 0x800802C4), so the "contiguous self-referential span" cross-check that
    // closed FUN_80057a7c's tables does not close these. Embedding a guessed extent is the invented
    // semantics rule 10 forbids.
    //
    // WHAT WOULD CLOSE IT: the range of record+0x22D. One find-cross-references on that field's
    // writers -- it is a per-fighter byte, so FighterSetup.cs's roster load is the place to look --
    // gives the row count of PTR_DAT_800807a4, and from there every other extent falls out by the
    // strides already read above. That is one bounded question, not an investigation.
    //
    // ITS EMPTY STUB IS IN VS_EXE/FighterAction.cs, which this slice may not edit. Because no body
    // is written here, that stub must STAY -- deleting it would leave the call site unresolved. See
    // the report: FUN_800261ec is the one function in the brief whose stub must NOT be removed.

    // GHIDRA: FUN_80051758 @ 0x80051758 (VS.EXE), 136 bytes
    // NOT WRITTEN HERE. VS_EXE/StageBackdrop.cs already carries the full body at this address, with
    // its own call site. The brief listed it, but searching the address across VS_EXE/ first -- which
    // is what defect class 1 asks for -- turns it up immediately. Nothing to do; noted so the
    // omission reads as a decision rather than a miss. Its declaration there is `private`, which is
    // correct as long as StageBackdrop remains its only caller; nothing in this file calls it.
}
