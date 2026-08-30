using PsxSdkMonogame;
using static PsxSdkMonogame.LibCd;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.TITLE_EXE;

// The title screen itself. FUN_80021e28 @ 0x80021E28 is the task FUN_80021dd0 registers in list 6,
// and it runs once per frame for as long as the title screen is up.
//
// Its task context is 0x70 bytes, which it treats as three POLY_FT4 slots:
//   p[0]  the upper background band, y 0 to 0x58
//   p[1]  the lower background band, y 0xbc to 0xf0
//   p[2]  NOT a primitive: 0x20 bytes of scratch state, addressed through POLY_FT4 field names.
//         0x28 + 0x28 + 0x20 = 0x70 exactly, so the third slot is deliberately short.
//
// The scratch fields, each read straight off the original's stores:
//   tag low half    the state index this switch runs on, 0 to 5
//   tag high half   a frame counter, reset on each state change
//   r0 | g0         one 16-bit logo offset, read as a pair
//   b0 | code       one 16-bit background offset, read as a pair
//   x0              the horizontal slide of both background bands
//   u1 | v1         a 16-bit blink flag, toggled every eighth frame
//   u2              the fade level the sprite groups are drawn at, 0 to 0x80
//   tpage           0xffff once Start is pressed, which routes the exit to SELECT.EXE
//
// The five states: 0 sets everything up, 1 fades in, 2 slides the bands into place, 3 waits for
// Start while blinking PRESS START, 4 fades out, 5 leaves.
internal static class TitleScreenTask
{
    // GHIDRA: DAT_80110000 @ 0x80110000 — the TITLE.B staging buffer's address, which the sprite
    // group offsets are relative to. The original spells every one of them as
    // .
    private const int TitleBBufferAddress = unchecked((int)0x80110000);

    // GHIDRA: FUN_80021e28 @ 0x80021E28
    internal static int FUN_80021e28()
    {
        POLY_FT4Ref p = TaskContext();

        // JUSTIFICATION: C# language bridge only
        // RELATION: p_00 is the original's own name for p + 1. p_02 is the same idea for p[2]:
        // C# cannot assign through an indexer that hands back a struct by value, so the two slots
        // the original reaches by subscript are named once here. Both alias the same packet bytes.
        POLY_FT4Ref p_00 = p[1];
        POLY_FT4Ref p_02 = p[2];

        int iVar10 = p_02.ReadHalf(0);

        uint uVar7 = 0;
        bool tookDefault = false;

        switch (iVar10)
        {
            case 0:
                FUN_80022630();
                SharedHighRam.DAT_801ff068 = FUN_80022780(0);
                // JUSTIFICATION: C# language bridge only
                // RELATION: the original reaches LAB_80021F98 two ways, once by falling into the
                // else arm and once by a goto out of the then arm. C# forbids jumping into another
                // block, so the shared tail is lifted out behind a flag. Same three paths, same
                // order.
                bool clearSaveTable = false;
                if (SharedHighRam.DAT_801ff068 == 0)
                {
                    iVar10 = FUN_80023374();
                    if (iVar10 == 0)
                    {
                        SharedHighRam.DAT_801ff068 = 2;
                    }
                    else
                    {
                        int piVar14 = 0;
                        iVar10 = 0;
                        SharedHighRam.DAT_801ff002 = 0;
                        do
                        {
                            ushort uVar9 = SharedHighRam.INT_ARRAY_801ff200.ReadU16At(piVar14);
                            piVar14 = piVar14 + 8;
                            if ((uVar9 & 1) != 0)
                            {
                                uVar9 = SharedHighRam.INT_ARRAY_801ff200.ReadU16At(iVar10 + 2);
                                if (SharedHighRam.DAT_801ff002 < uVar9)
                                {
                                    SharedHighRam.DAT_801ff002 = uVar9;
                                }
                            }

                            iVar10 = iVar10 + 8;
                        } while (iVar10 < 0x18);

                        SharedHighRam.DAT_801ff00a = 0;
                        iVar10 = 0;
                        int puVar13 = 0x1a;
                        do
                        {
                            iVar10 = iVar10 + 1;
                            ushort uVar9 = SharedHighRam.DAT_801ff00a;
                            if ((SharedHighRam.INT_ARRAY_801ff200.ReadU16At(puVar13 - 2) & 1) == 0)
                            {
                                SharedHighRam.DAT_801ff00a = uVar9;
                            }
                            else
                            {
                                uVar9 = SharedHighRam.INT_ARRAY_801ff200.ReadU16At(puVar13);
                                if (SharedHighRam.DAT_801ff00a < uVar9)
                                {
                                    SharedHighRam.DAT_801ff00a = uVar9;
                                }
                            }

                            puVar13 = puVar13 + 0x10;
                        } while (iVar10 < 3);
                    }

                    if (SharedHighRam.DAT_801ff068 != 0)
                    {
                        clearSaveTable = true;
                    }
                }
                else
                {
                    clearSaveTable = true;
                }

                // LAB_80021F98
                if (clearSaveTable)
                {
                    iVar10 = 0x11;
                    int piVar14b = 0x11;
                    do
                    {
                        SharedHighRam.INT_ARRAY_801ff200[piVar14b] = 0;
                        iVar10 = iVar10 + -1;
                        piVar14b = piVar14b + -1;
                    } while (-1 < iVar10);
                }

                FUN_80022680();
                SetPolyFT4(p);
                SetPolyFT4(p_00);
                p.tpage = 0x46;
                p_00.tpage = 0x46;
                ushort uVar5 = GetClut(0x180, 0xfe);
                p.clut = uVar5;
                uVar5 = GetClut(0x180, 0xfe);
                p_00.clut = uVar5;
                SetSemiTrans(p, 1);
                SetSemiTrans(p_00, 1);
                SetShadeTex(p, 0);
                SetShadeTex(p_00, 0);
                p.r0 = 0x60;
                p.g0 = 0x60;
                p.b0 = 0x60;
                p_00.r0 = 0x60;
                p_00.g0 = 0x60;
                p_00.b0 = 0x60;
                p.u0 = 0;
                p.v0 = 0xff;
                p.u1 = 0;
                p.v1 = 0xff;
                p.u2 = 0;
                p.v2 = 0xff;
                p.u3 = 0;
                p.v3 = 0xff;
                p_00.v0 = 0xff;
                p_00.v1 = 0xff;
                p_00.v2 = 0xff;
                p_00.v3 = 0xff;
                p_00.u0 = 0;
                p_00.u1 = 0;
                p_00.u2 = 0;
                p_00.u3 = 0;

                // One 16-bit pixel, all ones, uploaded to (0x180, 0xfe). That single white texel is
                // the CLUT the two background bands were just given.
                ulong[] local_38 = { 0xffffUL };
                DisplayMachine.LoadImageInVram(local_38, 0x180, 0xfe, 1, 1, '\0');

                int iVar12 = 0xf;
                int iVar10b = unchecked((int)0x801ff00f);
                p_02.b0 = 0x80;
                p_02.code = 2;
                p_02.r0 = 0x80;
                p_02.g0 = 2;
                p_02.x0 = 0x140;
                do
                {
                    // 0x801ff00f + 0x58 down to +0x58 - 15, that is 0x801FF067 through 0x801FF058:
                    // the sixteen bytes the two 0x1d0 combinations write in state 3.
                    PsxRam.WriteU8(iVar10b + 0x58, 0);
                    iVar12 = iVar12 + -1;
                    iVar10b = iVar10b + -1;
                } while (-1 < iVar12);

                p_02.WriteHalf(0, 1);
                break;

            case 1:
                byte uVar4 = (byte)(p_02.u2 + 8);
                p_02.u2 = uVar4;
                if (uVar4 == 0x80)
                {
                    p_02.WriteHalf(0, 2);
                }

                break;

            case 2:
                // b0 and code are one halfword at +6, the background offset.
                ushort uVar9b = (ushort)(p_02.ReadHalf(6) - 0xa0);
                p_02.WriteHalf(6, (short)uVar9b);
                p_02.x0 = (short)(p_02.x0 + -0x50);
                if ((int)((uint)uVar9b << 0x10) >> 0x10 < 1)
                {
                    p_02.WriteHalf(0, 3);
                    p_02.b0 = 0;
                    p_02.code = 0;
                    p_02.x2 = 0;
                    p_02.u1 = 1;
                    p_02.v1 = 0;
                }

                break;

            case 3:
                if (((PadInput.DAT_800834fc[0] | PadInput.DAT_800834fc[1]) & 0x800) == 0)
                {
                    uint uVar11 = (uint)(ushort)p_02.ReadHalf(2) + 1;
                    p_02.WriteHalf(2, (short)uVar11);
                    if ((int)(uVar11 * 0x10000) >> 0x10 < 0x191)
                    {
                        if ((uVar11 & 7) == 0)
                        {
                            // u1 and v1 are one halfword at +20, the blink flag.
                            ushort uVar9c = (ushort)(p_02.ReadHalf(20) ^ 1);
                            p_02.u1 = (byte)uVar9c;
                            p_02.v1 = (byte)(uVar9c >> 8);
                            if (uVar9c == 0)
                            {
                                p_02.r0 = 0;
                                p_02.g0 = 0;
                            }
                            else
                            {
                                p_02.r0 = 0x50;
                                p_02.g0 = 2;
                            }
                        }

                        if ((PadInput.DAT_800835dc[0] & 0x1d0) == 0x1d0)
                        {
                            SharedHighRam.DAT_801ff058 = 0xce;
                            SharedHighRam.DAT_801ff059 = 0xce;
                            SharedHighRam.DAT_801ff05a = 0xce;
                        }

                        if ((PadInput.DAT_800835dc[1] & 0x1d0) == 0x1d0)
                        {
                            SharedHighRam.DAT_801ff05b = 0xce;
                            SharedHighRam.DAT_801ff05c = 0xce;
                            SharedHighRam.DAT_801ff05d = 0xce;
                        }
                    }
                    else
                    {
                        p_02.WriteHalf(0, 4);
                        p_02.WriteHalf(2, 0);
                    }
                }
                else
                {
                    p_02.tpage = 0xffff;
                    p_02.r0 = 0;
                    p_02.g0 = 0;
                    p_02.WriteHalf(2, 0);
                    p_02.WriteHalf(0, 4);

                    // CdlSetloc on the SELECT.EXE entry TITLE.EXE searched for at startup: the seek
                    // is started here so the drive is already in place when state 5 hands over.
                    CdControl(2, TITLE_EXE_exe.CdlFILE_800a8860.pos, auStack_30);
                }

                break;

            case 4:
                if (p_02.u2 == 0)
                {
                    p_02.WriteHalf(0, 5);
                }
                else
                {
                    p_02.u2 = (byte)(p_02.u2 + 0xf8);
                }

                break;

            case 5:
                short sVar6b = (short)(p_02.ReadHalf(2) + 1);
                p_02.WriteHalf(2, sVar6b);
                if (sVar6b == 2)
                {
                    if (p_02.tpage == 0xffff)
                    {
                        FrameLoop.ShutdownAndLoadExecutable("cdrom:\\SELECT.EXE;1");
                    }
                    else
                    {
                        FrameLoop.DAT_800835b4 = sVar6b;
                        TaskSystem.DeleteTask(TaskSystem.PTR_80083224,
                            (uint)(ushort)TaskSystem.PTR_ARRAY_80083228);
                    }
                }

                break;

            default:
                // The original's goto jumps past the reload below, so iVar10 keeps the value the
                // switch was entered with.
                tookDefault = true;
                break;
        }

        if (!tookDefault)
        {
            iVar10 = p_02.ReadHalf(0);
        }

        byte[] file = TITLE_EXE_exe.DAT_80110000;
        int iVar12b = MipsMemory.ReadI32(file, 0);
        if (iVar10 == 5)
        {
            return 0;
        }

        // TITLE.B's first word is the offset of a five-entry table of sprite-group offsets, all
        // relative to the file base.
        int piVar14c = iVar12b;
        if (iVar10 == 3)
        {
            ushort uVar1 = (ushort)p_02.ReadHalf(4);
            SpriteRenderer.FUN_80048f88(
                TitleBBufferAddress + MipsMemory.ReadI32(file, iVar12b + 0x10),
                (short)((int)((uVar1 - 0x50) * 0x10000) >> 0x10), 0, 0x1000, 0, 0, 0, 0x1000, 0x1000,
                0, 0, 0, 0, 0, p_02.u2, p_02.u2, p_02.u2, unchecked((int)0xffffe890));
        }

        ushort uVar2 = (ushort)p_02.ReadHalf(6);
        SpriteRenderer.FUN_80048f88(TitleBBufferAddress + MipsMemory.ReadI32(file, iVar12b + 0x08),
            (short)((int)((-0x50 - (uint)uVar2) * 0x10000) >> 0x10), 0, 0x1000, 0, 0, 0, 0x1000,
            0x1000, 0, 0, 0, 0, 0, p_02.u2, p_02.u2, p_02.u2, unchecked((int)0xffffe890));

        ushort uVar3 = (ushort)p_02.ReadHalf(6);
        int iVar8 = SpriteRenderer.FUN_80048f88(
            TitleBBufferAddress + MipsMemory.ReadI32(file, iVar12b + 0x0c),
            (short)((int)((uVar3 - 0x50) * 0x10000) >> 0x10), 0, 0x1000, 0, 0, 0, 0x1000, 0x1000, 0,
            0, 0, 0, 0, p_02.u2, p_02.u2, p_02.u2, unchecked((int)0xffffe890));

        short sVar6 = p_02.x0;
        p.y0 = 0;
        p.x0 = (short)-sVar6;
        sVar6 = p_02.x0;
        p.y1 = 0;
        p.x1 = (short)(0x140 - sVar6);
        sVar6 = p_02.x0;
        p.y2 = 0x58;
        p.x2 = (short)-sVar6;
        sVar6 = p_02.x0;
        p.y3 = 0x58;
        p.x3 = (short)(0x140 - sVar6);
        sVar6 = p_02.x0;
        p_00.y0 = 0xbc;
        p_00.x0 = sVar6;
        sVar6 = p_02.x0;
        p_00.y1 = 0xbc;
        iVar10 = FrameLoop.DAT_800834e0;
        p_00.x1 = (short)(sVar6 + 0x140);
        sVar6 = p_02.x0;

        // The bucket for both background bands, derived from the third FUN_80048f88 call's return
        // exactly the way FUN_80048f88 derives its own. That return is now a real OT index rather
        // than the constant 0 the stub used to give back, so the bands no longer always land in
        // bucket 0.
        // PARTIAL: when FUN_80048f88 returns -1 - no record was added, or the last one failed the
        // OT range test - this evaluates to DAT_800834e0 + 0x6c, which is FOUR BYTES BELOW the
        // ordering table at DAT_800834e0 + 0x70. On the console that writes into the tail of
        // DRAWENV_800a67c0. In this port only the table itself is a registered RAM region, so
        // AddPrim cannot resolve that address and silently does nothing. The original is not
        // corrected here; the difference is in what the unmodelled write lands on.
        iVar8 = (iVar8 * 4) + 0x70;
        p_00.y2 = 0xf0;
        p_00.x2 = sVar6;
        sVar6 = p_02.x0;
        p_00.y3 = 0xf0;
        p_00.x3 = (short)(sVar6 + 0x140);
        AddPrim(iVar8 + iVar10, p);
        AddPrim(iVar8 + FrameLoop.DAT_800834e0, p_00);

        SpriteRenderer.FUN_80048f88(TitleBBufferAddress + MipsMemory.ReadI32(file, piVar14c),
            (short)unchecked((int)0xffffffb0), 0, 0x1000, 0, 0,
            0, 0x1000, 0x1000, 0, 0, 0, 0, 0, p_02.u2, p_02.u2, p_02.u2, unchecked((int)0xffffe890));
        uVar7 = (uint)SpriteRenderer.FUN_80048f88(
            TitleBBufferAddress + MipsMemory.ReadI32(file, iVar12b + 0x04),
            0x1b0, 0, 0x1000, 0, 0,
            0, 0x1000, 0x1000, 0, 0, 0, 0, 0, p_02.u2, p_02.u2, p_02.u2, unchecked((int)0xffffe890));

        return (int)uVar7;
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: `p = *(POLY_FT4 **)(PTR_80083224 + 8)` in one step. The task block's +8 word is its
    // context pointer, and the context is heap memory, so the address resolves to a real packet.
    private static POLY_FT4Ref TaskContext()
    {
        int context = PsxRam.ReadI32(TaskSystem.PTR_80083224 + 8);
        return RamResolve(context, out byte[] buffer, out int offset)
            ? new POLY_FT4Ref(buffer, offset)
            : default;
    }

    // GHIDRA: auStack_30 — the eight-byte result buffer CdControl fills.
    private static readonly byte[] auStack_30 = new byte[8];

    // GHIDRA: FUN_80022630 @ 0x80022630
    private static void FUN_80022630()
    {
        // BLOCKED: memory card bring-up — InitCARD(1), StartCARD, _bu_init, FUN_80022c94,
        // _card_auto(0), ChangeClearPAD(0), FUN_80023290. libcard is not modelled by the SDK, and
        // none of the seven is closed.
    }

    // GHIDRA: FUN_80022680 @ 0x80022680
    private static void FUN_80022680()
    {
        // BLOCKED: memory card teardown — eight DisableEvent then eight CloseEvent inside a
        // critical section, StopCARD, then StartPAD and ChangeClearPAD(0) to hand the port back to
        // the pad. Nothing is torn down here because FUN_80022630 started nothing.
    }

    // GHIDRA: FUN_80022780 @ 0x80022780
    private static int FUN_80022780(int param_1)
    {
        // BLOCKED: the memory card probe — _card_info then _card_load, each retried up to five
        // times through FUN_80022b1c, with a _card_clear in between when the status is 4.
        //
        // The return decides the branch, so it was measured rather than guessed. Breaking at
        // 0x80021EA8 on the real title screen in PCSX-Redux, with the emulator's default memory
        // card and no DBZ save on it, gives v0 = 0.
        return 0;
    }

    // GHIDRA: FUN_80023374 @ 0x80023374
    private static int FUN_80023374()
    {
        // BLOCKED: the save-slot scan, which fills INT_ARRAY_801ff200 from the card.
        //
        // Measured the same way, breaking at 0x80021EC0: v0 = 0, meaning no valid save was found.
        // The caller then sets DAT_801ff068 = 2 and clears the table, which is the state C# statics
        // already start in.
        return 0;
    }
}
