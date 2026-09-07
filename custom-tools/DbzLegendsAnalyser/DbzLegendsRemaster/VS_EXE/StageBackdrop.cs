using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// THE STAGE BACKGROUND OF VS.EXE. Three task entry points that FUN_800414ec and FUN_80040f30
// create every match and that, until now, dispatched to nothing:
//
//   LAB_80040F78  list 0xd, id 0, 0xc bytes of context. Draws 0x50 sprites out of the 12-byte
//                 table at DAT_80082750 through SpriteDrawer.DrawSpriteGroup.
//   LAB_80041704  list 1, id 0x54, no context. A one-line thunk to FUN_80041724, the 23 x 23
//                 depth-shaded ground grid at DAT_800B7484.
//   LAB_80041A1C  list 1, id 0x100, 4 bytes of context. A two-state task: state 0 builds the eight
//                 POLY_FT4 packets at DAT_800B733C, state 1 transforms and submits them.
//
// FUN_80041724 IS THE RELINKED TWIN of TITLE.EXE's LAB_80037BF0, whose grid TITLE_EXE/StageBackdrop.cs
// already lays out: the same 23 x 23 elements of 0x48 bytes, the same POLY_FT4-plus-four-SVECTORs
// element, the same InitializePolyFt4 seeding. TITLE's file only ever declared the grid; this one
// is the consumer, and the two agree field for field. Where the overlays differ, VS.EXE wins: VS
// reaches its ordering table through VS_EXE_exe.DAT_8008d420 + 0x78, one bucket-array offset
// further in than the +0x70 main uses for DrawOTag, which is the ordering table's first entry.
//
// THE OFFSETS GHIDRA COULD NOT PRINT. FUN_80041aa8's decompilation shows six stores as
// `pcVar3[-0xffffffff0000000f]`-style 64-bit indices, which are decompiler artifacts and not
// offsets. Decoded from the image (PSX-EXE, load 0x80020000, 0x800 header, so file offset =
// address - 0x80020000 + 0x800), the six collapse to two halfword stores:
//   0x80041C80  sh v0,-15(s0)   with v0 = 0x008f  -> pcVar3 - 0x0f, the POLY_FT4 tpage
//   0x80041C94  sh v0,-23(s0)   with v0 = 0x7900  -> pcVar3 - 0x17, the POLY_FT4 clut
// Ghidra split each `sh` into its two constituent bytes and then lost the base, which is why it
// prints four char writes instead of two halfwords. Everything else in that function is a plain
// `sb`/`sh` at a small negative displacement off s0 = 0x800B7361, and those are used verbatim.
//
// WHAT THE STORE OFFSETS PROVE ABOUT THE EIGHT PACKETS. s0 = 0x800B7361 is packet 0 + 0x25, so the
// displacements -0x21..0x00 land exactly on psyq's POLY_FT4 fields: -0x21/-0x20/-0x1f are r0/g0/b0,
// -0x19/-0x18 u0/v0, -0x17 clut, -0x11/-0x10 u1/v1, -0x0f tpage, -0x09/-0x08 u2/v2, -0x01/0x00
// u3/v3. That is what closes the block layout below rather than any guess about its shape.
internal static class StageBackdrop
{
    // JUSTIFICATION: C# language bridge only
    // RELATION: forces this class's static constructor, and with it the two LibGpu.RamRegion rows
    // below. C# runs a static constructor lazily -- on the first access to the class -- and for a
    // task body that first access is the DISPATCH, not the RegisterCallback that merely takes the
    // method group. VS_EXE_exe.FUN_800414EC writes twelve halfwords per grid cell through PsxRam
    // before any of these bodies runs, so the regions have to exist first. VS_EXE_exe.ArmRegions
    // calls this from the resolver's own class initialiser, which is the earliest point every one
    // of those stores must already pass through.
    internal static void EnsureRegions()
    {
    }

    // ============================================================================================
    // The .bss block the three tasks share, 0x800B7216 .. 0x800B747B
    // ============================================================================================

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: backing storage for the contiguous .bss block FUN_80041aa8 / FUN_80041d20 /
    // FUN_80041724 address by raw PSX address. It is ONE block in the original -- the scalars at
    // 0x800B7216..0x800B7233, the eight-entry halfword table at 0x800B7234 (0x20 stride), the int
    // at 0x800B7334, and the eight POLY_FT4 packets at 0x800B733C (0x28 stride, last one ending at
    // 0x800B747B) -- and the code walks it with computed offsets rather than with named fields, so
    // it is modelled as one byte[] and read through PsxRam exactly as the original reads it. Split
    // into named C# scalars it would stop being addressable by `base + i * 0x20`, which is how
    // FUN_80041d20's first loop and FUN_80041aa8's body both reach it.
    //
    // The base is rounded down to 0x800B7210 so the block starts word-aligned; the six bytes below
    // 0x800B7216 belong to no symbol this slice touches.
    private const int BlockBase = unchecked((int)0x800B7210);

    private static readonly byte[] RAM_800b7210 = LibGpu.RamRegion(BlockBase, 0x270);

    // GHIDRA: DAT_800b7216 @ 0x800B7216 (VS.EXE)
    // A halfword flag word. FUN_80041aa8 ORs bit 0 into it once the packets are built; LAB_80041A1C
    // tests that same bit before it will run FUN_80041d20. It is the handshake between the task's
    // two states, and it is checked EVERY frame rather than only once, so clearing bit 0 from
    // anywhere would stop the backdrop being submitted.
    private const int Dat800b7216Address = unchecked((int)0x800B7216);

    // GHIDRA: DAT_800b7218 @ 0x800B7218, DAT_800b7219 @ 0x800B7219, DAT_800b721a @ 0x800B721A (VS.EXE)
    // Three bytes, one per colour component, written by FUN_80041aa8 (all three 0x80) and rewritten
    // by FUN_80041724 (all three the brightest depth shade the grid produced this frame). Read back
    // by FUN_80041aa8 into each packet's r0/g0/b0.
    private const int Dat800b7218Address = unchecked((int)0x800B7218);
    private const int Dat800b7219Address = unchecked((int)0x800B7219);
    private const int Dat800b721aAddress = unchecked((int)0x800B721A);

    // GHIDRA: DAT_800b721c @ 0x800B721C, DAT_800b721e @ 0x800B721E, DAT_800b7220 @ 0x800B7220,
    //         DAT_800b7222 @ 0x800B7222 (VS.EXE)
    // Four halfwords FUN_80041aa8 zeroes. FUN_80041d20 reads 0x800B721C and 0x800B7220 as the
    // rotation offsets of its own transform.
    //
    // DEVIATION -- NO, A BUG OF THE ORIGINAL, reproduced. FUN_80041d20 builds local_60 as
    // (DAT_1f80007c - DAT_800b721c, -DAT_800b7220, DAT_1f800080 - DAT_800b7220): the y and z
    // components BOTH subtract 0x800B7220, and 0x800B721E is never read by anything in this slice.
    // The shape of the surrounding code (one offset per axis, three axes) says 0x800B721E was meant
    // to be the y one. The instructions settle what actually happens -- 0x80041EAC `lhu a2,0x7220`
    // is the only load, and both `subu` at 0x80041EC0 and 0x80041EC4 use it -- so that is what this
    // port does. Rule 12: not corrected.
    private const int Dat800b721cAddress = unchecked((int)0x800B721C);
    private const int Dat800b721eAddress = unchecked((int)0x800B721E);
    private const int Dat800b7220Address = unchecked((int)0x800B7220);
    private const int Dat800b7222Address = unchecked((int)0x800B7222);

    // GHIDRA: DAT_800b7224 @ 0x800B7224, DAT_800b7226 @ 0x800B7226, DAT_800b7228 @ 0x800B7228,
    //         DAT_800b722a @ 0x800B722A (VS.EXE)
    // Four more halfwords FUN_80041aa8 zeroes; the first three are FUN_80041d20's translation
    // triple. 0x800B7226 is special: FUN_80041d20's FIRST action is to store DAT_1f800098 into it,
    // and its own use of it a few lines later is `DAT_800b7226 - DAT_1f800098`, which is therefore
    // always zero. Reproduced as written -- the store is observable by anything else reading the
    // word, and the subtraction is the original's.
    private const int Dat800b7224Address = unchecked((int)0x800B7224);
    private const int Dat800b7226Address = unchecked((int)0x800B7226);
    private const int Dat800b7228Address = unchecked((int)0x800B7228);
    private const int Dat800b722aAddress = unchecked((int)0x800B722A);

    // GHIDRA: DAT_800b722c @ 0x800B722C, DAT_800b722e @ 0x800B722E, DAT_800b7230 @ 0x800B7230,
    //         DAT_800b7232 @ 0x800B7232 (VS.EXE)
    // The scale triple FUN_80041aa8 seeds with (0x7000, 0x7000, 0x1000) and FUN_80041d20 hands to
    // ScaleMatrix, plus one trailing halfword zeroed and never read. FUN_80041d20 loads the three
    // with `lh` (0x80041F48/4C/58), i.e. SIGNED, which is why they are read as short here.
    private const int Dat800b722cAddress = unchecked((int)0x800B722C);
    private const int Dat800b722eAddress = unchecked((int)0x800B722E);
    private const int Dat800b7230Address = unchecked((int)0x800B7230);
    private const int Dat800b7232Address = unchecked((int)0x800B7232);

    // GHIDRA: DAT_800b7234 @ 0x800B7234, DAT_800b723c @ 0x800B723C, DAT_800b7244 @ 0x800B7244,
    //         DAT_800b724c @ 0x800B724C (VS.EXE)
    // The four corner SVECTORs of one backdrop quad, eight quads deep on a 0x20 stride --
    // 0x800B7234 .. 0x800B7333, which is exactly where DAT_800B7334 begins, so the table's extent
    // is closed at both ends. FUN_80041aa8 fills every field; FUN_80041d20 rewrites the x of the
    // first and third corners and the x of the second and fourth each frame and then hands the four
    // straight to RotAverage4.
    private const int Dat800b7234Address = unchecked((int)0x800B7234);
    private const int Dat800b723cAddress = unchecked((int)0x800B723C);
    private const int Dat800b7244Address = unchecked((int)0x800B7244);
    private const int Dat800b724cAddress = unchecked((int)0x800B724C);

    // The three remaining per-corner fields FUN_80041aa8 writes, expressed as their own bases so the
    // `+ iVar4` cursor below reads the way the original's does.
    private const int Dat800b7236Address = unchecked((int)0x800B7236);
    private const int Dat800b7238Address = unchecked((int)0x800B7238);
    private const int Dat800b723eAddress = unchecked((int)0x800B723E);
    private const int Dat800b7240Address = unchecked((int)0x800B7240);
    private const int Dat800b7246Address = unchecked((int)0x800B7246);
    private const int Dat800b7248Address = unchecked((int)0x800B7248);
    private const int Dat800b724eAddress = unchecked((int)0x800B724E);
    private const int Dat800b7250Address = unchecked((int)0x800B7250);

    // GHIDRA: DAT_800b7334 @ 0x800B7334 (VS.EXE)
    // A full WORD -- `sw v0,0x7334(at)` at 0x80041DF8, not a `sh`. FUN_80041d20 computes it once per
    // frame from the camera angle and the two world offsets, then reads back only its LOW HALFWORD
    // (`lhu v1,0x0(t0)` at 0x80041E28) to seed the eight quads' scrolling x. The truncation is the
    // original's and is what the & 0x3ff below operates on.
    private const int Dat800b7334Address = unchecked((int)0x800B7334);

    // GHIDRA: DAT_800b733c @ 0x800B733C (VS.EXE)
    // The eight POLY_FT4 packets, 0x28 apart, 0x800B733C .. 0x800B747B. FUN_80041aa8 builds them,
    // FUN_80041d20 projects into them and AddPrims them.
    private const int Dat800b733cAddress = unchecked((int)0x800B733C);

    // GHIDRA: DAT_800b7361 @ 0x800B7361 (VS.EXE)
    // Not a symbol of the original: the cursor register s0 that FUN_80041aa8 walks the packets with,
    // which is packet 0 + 0x25 (the v3 field). Every field store in that function is a small
    // negative displacement off it, so the port keeps the same cursor rather than re-basing the
    // displacements and losing the correspondence with the instructions.
    private const int Dat800b7361Address = unchecked((int)0x800B7361);

    // GHIDRA: DAT_800b7484 @ 0x800B7484 (VS.EXE)
    // The 23 x 23 grid of 0x48-byte records FUN_800414ec seeds (VS_EXE_exe.cs already declares this
    // address as its own Dat800b7484Address const and writes twelve halfwords per cell through
    // PsxRam) and FUN_80041724 draws. The element is a POLY_FT4 at +0x00 followed by four SVECTORs
    // at +0x28/+0x30/+0x38/+0x40, which is TITLE_EXE/StageBackdrop.cs's astruct_1_800acda0 exactly.
    //
    // REGISTERED AS A RamRegion HERE, and it is the first registration this address has ever had.
    // VS_EXE_exe.FUN_800414ec writes into it through PsxRam by raw address, and PsxRam resolves
    // through VS_EXE_exe.ResolveAddress, which consults LibGpu.RamResolve first -- so with no region
    // declared, every one of those twelve halfword stores per cell silently resolved to nothing.
    // FUN_80041724 cannot read vertices that were never stored, so the region has to exist for the
    // grid to draw at all. Sized 0x17 * 0x17 * 0x48 = 0x94C8, the same extent TITLE's twin declares.
    private const int Dat800b7484Address = unchecked((int)0x800B7484);

    private static readonly byte[] RAM_800b7484 = LibGpu.RamRegion(Dat800b7484Address, 0x17 * 0x17 * 0x48);

    // GHIDRA: DAT_80082720 @ 0x80082720, DAT_80082738 @ 0x80082738 (VS.EXE)
    // Two sprite-group records in .data, in the layout SpriteDrawer.DrawSpriteGroup consumes: a count
    // word (1 in both) followed by one entry. Read back from the image, the two differ only in
    // their tpage/uv block, so they are the same sprite drawn from two different texture pages --
    // which is what the DAT_8008d39c == 2 || == 6 test below selects between. Passed as raw
    // addresses: DrawSpriteGroup takes an int and reads the record through PsxRam, and .data resolves
    // through PsxExeImage.
    private const int Dat80082720Address = unchecked((int)0x80082720);
    private const int Dat80082738Address = unchecked((int)0x80082738);

    // GHIDRA: DAT_80082750 @ 0x80082750, DAT_80082754 @ 0x80082754, DAT_80082758 @ 0x80082758 (VS.EXE)
    // ONE table, not three symbols: 0x50 records of 12 bytes, three ints each, at 0x80082750. The
    // extent is closed at both ends -- 0x80082750 + 0x50 * 0xc = 0x80082B10, and the bytes at
    // 0x80082B10 are the `\STG\STG1MD.B;1` file-name strings, not more values of this shape.
    //
    // THE LOADS ARE `lhu`, NOT `lw` (0x80040FC8, 0x80040FE0, 0x80040FF8): the task takes only the
    // LOW HALFWORD of each int. The stored values are small negative ints (record 0 is
    // -0x258, -0x64, -0x258), so the halfword read is their sign-extended low half either way, but
    // the narrowing is the original's and is written as such.
    private const int Dat80082750Address = unchecked((int)0x80082750);
    private const int Dat80082754Address = unchecked((int)0x80082754);
    private const int Dat80082758Address = unchecked((int)0x80082758);

    // ============================================================================================
    // Bridges
    // ============================================================================================

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original hands RotAverage4 four `SVECTOR *` that point straight into .bss. This
    // port's LibGte.RotAverage4 takes SVECTOR objects, so the four are lifted out of PSX RAM first.
    // Nothing is written back -- RotAverage4 only reads them.
    private static void LoadSVector(int address, LibGte.SVECTOR dest)
    {
        dest.vx = (short)PsxRam.ReadU16(address);
        dest.vy = (short)PsxRam.ReadU16(address + 2);
        dest.vz = (short)PsxRam.ReadU16(address + 4);
        dest.pad = (short)PsxRam.ReadU16(address + 6);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: SetPolyFT4 / SetSemiTrans / SetShadeTex take a primitive; in this port a primitive is
    // a (byte[], offset) pair while the original holds a raw `POLY_FT4 *`. The eight packets live in
    // one registered block, so the split is against that block's own base -- the same shape
    // TITLE_EXE/StageBackdrop.PolyFt4At uses for its grid.
    private static POLY_FT4Ref PolyFt4At(int address) => new(RAM_800b7210, address - BlockBase);

    // ============================================================================================
    // LAB_80040F78 -- the 0x50-sprite task
    // ============================================================================================

    // GHIDRA: LAB_80040f78 @ 0x80040F78 (VS.EXE)
    // A TASK ENTRY POINT Ghidra never promoted to a function, so it was decompiled by address and
    // every offset below was checked against the instructions at 0x80040F78..0x800411AC.
    // VS_EXE_exe.cs owns its CreateTask (id 0, list 0xd, 0xc bytes of context, from FUN_80040f30);
    // it needs a matching TaskSystem.RegisterCallback, which VS_EXE_exe.cs must add.
    //
    // THE RECORD POINTER IS LOADED ONCE, OUTSIDE THE LOOP, and that is the original. `lw s0,0x8(v0)`
    // sits at 0x80040FB8 while the loop's back-branch targets 0x80040FBC, one instruction later, so
    // s0 -- the task's own 0xc-byte context block -- is the SAME six bytes for all 0x50 iterations.
    // Each iteration overwrites the previous one's three halfwords and then draws from them, so the
    // context is scratch, not an array. Nothing here is corrected; rule 12.
    //
    // THE GATE IS UNSIGNED. `sltiu v0,a0,0x3e8` at 0x80041090 compares DAT_1f8000b8 as UNSIGNED
    // against 1000, so a negative value of that word passes the test as a very large one. Written as
    // (uint) here for that reason, not as Ghidra's `999 < DAT_1f8000b8`.
    internal static void LAB_80040f78()
    {
        uint uVar7 = 0;
        int iVar6 = 0;
        int iVar5 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);

        do
        {
            PsxRam.WriteU16(iVar5 + 4, PsxRam.ReadU16(Dat80082750Address + iVar6));
            PsxRam.WriteU16(iVar5 + 6, PsxRam.ReadU16(Dat80082754Address + iVar6));
            PsxRam.WriteU16(iVar5 + 8, PsxRam.ReadU16(Dat80082758Address + iVar6));

            // `(x / 2000) * 2000` -- the multiply-high-shift sequence at 0x80041010..0x80041030 is
            // exactly a truncating divide by 2000 followed by a multiply back by 2000, i.e. the
            // world offset snapped DOWN to a 2000-unit tile boundary. C#'s int division truncates
            // toward zero the same way the sign correction there does.
            PsxRam.WriteU16(iVar5 + 4, (ushort)(unchecked((short)PsxRam.ReadU16(iVar5 + 4))
                + (short)(Scratchpad._DAT_1f8000b4 / 2000) * 2000));
            PsxRam.WriteU16(iVar5 + 8, (ushort)(unchecked((short)PsxRam.ReadU16(iVar5 + 8))
                + (short)(Scratchpad._DAT_1f8000bc / 2000) * 2000));

            short sVar2 = VS_EXE_exe.DAT_8008d39c;
            if ((uint)Scratchpad.DAT_1f8000b8 >= 1000)
            {
                int iVar3 = PsxRam.ReadU16(iVar5 + 6)
                            + (Scratchpad.DAT_1f8000b8 / 1000) * -1000
                            + 0xfe0c;
                bool bVar1 = VS_EXE_exe.DAT_8008d39c == 2;
                PsxRam.WriteU16(iVar5 + 6, unchecked((ushort)(short)iVar3));

                // The two stage variants that use the second record. DAT_8008d39c is the variant
                // index FUN_80040f30 latched; VS_EXE_exe.cs owns it and it must become internal.
                int puVar4 = (bVar1 || sVar2 == 6) ? Dat80082738Address : Dat80082720Address;

                SpriteDrawer.DrawSpriteGroup(
                    puVar4,
                    (short)(PsxRam.ReadU16(iVar5 + 4) - (Scratchpad._DAT_1f8000b4 & 0xffff)),
                    (short)iVar3,
                    (short)(PsxRam.ReadU16(iVar5 + 8) - (Scratchpad._DAT_1f8000bc & 0xffff)),
                    0, 0, 0, 0x4000, 0x4000, 0, 0, 0, 0, 0, 0x80, 0x80, 0x80,
                    VS_EXE_exe.DAT_1f800128);
            }

            uVar7 = uVar7 + 1;
            iVar6 = iVar6 + 0xc;
        } while (uVar7 < 0x50);
    }

    // ============================================================================================
    // LAB_80041704 -- the thunk, and FUN_80041724 -- the ground grid
    // ============================================================================================

    // GHIDRA: LAB_80041704 @ 0x80041704 (VS.EXE)
    // A TASK ENTRY POINT Ghidra never promoted. Thirty-two bytes of which the only useful ones are
    // `jal 0x80041724` at 0x8004170C: it builds a stack frame, calls, tears it down, returns. It
    // still needs its own TaskSystem.RegisterCallback in VS_EXE_exe.cs -- the CreateTask there
    // (id 0x54, list 1, no context) names THIS address, not FUN_80041724's.
    internal static void LAB_80041704()
    {
        FUN_80041724();
    }

    // GHIDRA: FUN_80041724 @ 0x80041724 (VS.EXE)
    // 760 bytes, five callees, one caller (LAB_80041704 above). The 23 x 23 ground grid: every cell
    // is projected through the GTE, depth-shaded from its own OTZ, and spliced into the ordering
    // table at DAT_8008d420 + 0x78.
    //
    // THE CAMERA IS SNAPPED TWICE, at two different granularities, and the two are independent. The
    // 0x2000 wrap at the top (`x % 0x2000`, with the extra `+ 0x2000` when the truncated remainder's
    // low halfword is negative) is the tile the grid is drawn in; the 0x200 wrap that follows is the
    // sub-tile scroll fed to FUN_80051758 as the translation. The `iVar1 * 0x10000 < 0` test is a
    // sign test on the LOW HALFWORD -- `sll v0,v1,16 / bgez v0` at 0x8004176C -- so it fires on the
    // remainder's 16-bit sign, not its 32-bit one.
    //
    // THE BUCKET TEST IS ASYMMETRIC, and this is the original's. The lower bound is checked against
    // `iVar7 + 0x840` and the upper against `iVar7 + 0x800`: 0x40 buckets of slack on the near side
    // only. SpriteDrawer.DrawSpriteGroup's own test uses one offset for both ends. Not reconciled.
    //
    // THE ON-SCREEN TEST IS A BOUNDING TEST ON THE PROJECTED VERTICES, not a clip: if ANY of the
    // four projected x/y pairs is inside (x < 0x208, y < 0x1b8) the cell is drawn. The comparisons
    // are `sltiu` -- UNSIGNED against the 16-bit screen coordinate -- so a vertex projected to a
    // negative coordinate reads as a huge positive one and fails, which is what makes an off-screen
    // cell drop out. Reproduced with (uint) rather than "corrected" to a signed range test.
    private static void FUN_80041724()
    {
        LibGte.SVECTOR local_38 = new();
        LibGte.VECTOR local_30 = new();
        int[] lStack_20 = new int[1];
        int[] lStack_1c = new int[1];
        LibGte.SVECTOR v0 = new();
        LibGte.SVECTOR v1 = new();
        LibGte.SVECTOR v2 = new();
        LibGte.SVECTOR v3 = new();

        int p = Dat800b7484Address;
        uint uVar11 = 0x80;

        int iVar1 = Scratchpad._DAT_1f8000b4;
        if (Scratchpad._DAT_1f8000b4 < 0)
        {
            iVar1 = Scratchpad._DAT_1f8000b4 + 0x1fff;
        }

        iVar1 = Scratchpad._DAT_1f8000b4 + (iVar1 >> 0xd) * -0x2000;
        short sVar9 = (short)iVar1;
        if (iVar1 * 0x10000 < 0)
        {
            sVar9 = (short)(sVar9 + 0x2000);
        }

        iVar1 = Scratchpad._DAT_1f8000bc;
        if (Scratchpad._DAT_1f8000bc < 0)
        {
            iVar1 = Scratchpad._DAT_1f8000bc + 0x1fff;
        }

        iVar1 = Scratchpad._DAT_1f8000bc + (iVar1 >> 0xd) * -0x2000;
        short sVar6 = (short)iVar1;
        if (iVar1 * 0x10000 < 0)
        {
            sVar6 = (short)(sVar6 + 0x2000);
        }

        int iVar3 = sVar9;
        iVar1 = iVar3;
        if (iVar3 < 0)
        {
            iVar1 = iVar3 + 0x1ff;
        }

        int iVar7 = sVar6;
        local_30.vx = -0xc00 - ((iVar3 + (iVar1 >> 9) * -0x200) * 0x10000 >> 0x10);
        local_30.vy = 0;

        iVar1 = iVar7;
        if (iVar7 < 0)
        {
            iVar1 = iVar7 + 0x1ff;
        }

        local_30.vz = -0xc00 - ((iVar7 + (iVar1 >> 9) * -0x200) * 0x10000 >> 0x10);

        local_38.vx = 0;
        local_38.vy = 0;
        local_38.vz = 0;

        LibGte.PushMatrix();
        FUN_80051758(local_38, local_30);

        iVar1 = 0;
        do
        {
            iVar3 = 0;
            int puVar10 = p + 4;
            do
            {
                LoadSVector(p + 0x28, v0);
                LoadSVector(p + 0x30, v1);
                LoadSVector(p + 0x38, v2);
                LoadSVector(p + 0x40, v3);

                // The four sxy destinations are words inside this cell's own POLY_FT4, so they are
                // handed over as the grid buffer plus four offsets, exactly as
                // VS_EXE/SpriteDrawer.cs does for its pool packet.
                if (!LibGpu.RamResolve(p, out byte[] pBuf, out int pOff))
                {
                    // PARTIAL: unreachable while RAM_800b7484 above is registered; the grid IS the
                    // region, so this only fires if the block is ever unregistered.
                    pBuf = RAM_800b7484;
                    pOff = 0;
                }

                int lVar2 = LibGte.RotAverage4(v0, v1, v2, v3,
                    pBuf, pOff + 8, pOff + 0x10, pOff + 0x18, pOff + 0x20,
                    lStack_20, lStack_1c);

                iVar7 = -lVar2;
                int iVar8 = iVar7 + 0x800;

                if (VS_EXE_exe.DAT_1f800128 < iVar7 + 0x840 && iVar8 < 0x800
                    && ((PsxRam.ReadU16(puVar10 + 6) < 0x1b8 && PsxRam.ReadU16(puVar10 + 4) < 0x208)
                        || (PsxRam.ReadU16(puVar10 + 0xe) < 0x1b8 && PsxRam.ReadU16(puVar10 + 0xc) < 0x208)
                        || (PsxRam.ReadU16(puVar10 + 0x16) < 0x1b8 && PsxRam.ReadU16(puVar10 + 0x14) < 0x208)
                        || (PsxRam.ReadU16(puVar10 + 0x1e) < 0x1b8 && PsxRam.ReadU16(puVar10 + 0x1c) < 0x208)))
                {
                    // `>> 4` with the toward-zero correction the `bgez / addiu 15 / sra 4` sequence
                    // at 0x80041950 performs. 0xff minus that is the shade: bucket 0 (nearest) is
                    // full brightness, bucket 0xff0 and beyond is black.
                    if (iVar8 < 0)
                    {
                        iVar8 = iVar7 + 0x80f;
                    }

                    uint uVar4 = (uint)(0xff - (iVar8 >> 4));
                    if (0xff < (uVar4 & 0xffff))
                    {
                        uVar4 = 0xff;
                    }

                    byte uVar5 = (byte)uVar4;
                    PsxRam.WriteU8(puVar10 + 2, uVar5);
                    PsxRam.WriteU8(puVar10 + 1, uVar5);
                    PsxRam.WriteU8(puVar10, uVar5);

                    // The running maximum, seeded at 0x80 and compared as (running & 0xff) against
                    // the CLAMPED shade -- both paths of the clamp above leave the same value in
                    // the register the `sltu` at 0x80041988 reads.
                    if ((uVar11 & 0xff) < (uVar4 & 0xffff))
                    {
                        uVar11 = uVar4;
                    }

                    LibGpu.AddPrim(VS_EXE_exe.DAT_8008d420 + 0x78, p);
                }

                puVar10 = puVar10 + 0x48;
                iVar3 = iVar3 + 1;
                p = p + 0x48;
            } while (iVar3 < 0x17);

            iVar1 = iVar1 + 1;
        } while (iVar1 < 0x17);

        // Machine order, 0x800419DC/E4/EC: 0x800B721A first, then 0x800B7219, then 0x800B7218. All
        // three take the SAME running maximum -- Ghidra prints the second and third as copies of
        // the first, which is the same bytes by a different route.
        PsxRam.WriteU8(Dat800b721aAddress, (byte)uVar11);
        PsxRam.WriteU8(Dat800b7219Address, (byte)uVar11);
        PsxRam.WriteU8(Dat800b7218Address, (byte)uVar11);

        LibGte.PopMatrix();
    }

    // GHIDRA: FUN_80051758 @ 0x80051758 (VS.EXE)
    // 136 bytes, six callees, ALL of them libgte, and one caller -- FUN_80041724 above. A leaf
    // helper in every sense, so it is transliterated here rather than stubbed: it composes the
    // rotation the GTE currently holds with a fresh translate-then-rotate matrix and installs the
    // result as both the rotation and the translation matrix.
    //
    // TransMatrix WRITES ONLY THE TRANSLATION COLUMN and RotMatrix only the 3x3, so the order
    // TransMatrix-then-RotMatrix on the same MStack_38 is not a lost store -- both survive, and
    // CompMatrix then folds the caller's rotation in front of it. Not reordered.
    private static void FUN_80051758(LibGte.SVECTOR param_1, LibGte.VECTOR param_2)
    {
        LibGte.MATRIX MStack_78 = new();
        LibGte.MATRIX MStack_58 = new();
        LibGte.MATRIX MStack_38 = new();

        LibGte.ReadRotMatrix(MStack_58);
        LibGte.TransMatrix(MStack_38, param_2);
        LibGte.RotMatrix(param_1, MStack_38);
        LibGte.CompMatrix(MStack_58, MStack_38, MStack_78);
        LibGte.SetRotMatrix(MStack_78);
        LibGte.SetTransMatrix(MStack_78);
    }

    // ============================================================================================
    // LAB_80041A1C -- the two-state backdrop-quad task
    // ============================================================================================

    // GHIDRA: LAB_80041a1c @ 0x80041A1C (VS.EXE)
    // A TASK ENTRY POINT Ghidra never promoted. VS_EXE_exe.cs creates it with id 0x100 on list 1
    // with a 4-byte context; it needs its own TaskSystem.RegisterCallback there.
    //
    // The 4-byte context IS the state word. State 0 builds the packets and advances to 1; state 1
    // runs the per-frame transform, but only while DAT_800b7216 bit 0 is set -- the bit FUN_80041aa8
    // itself raised. Any other state value does nothing, which is the `j 0x80041A94` fall-through at
    // 0x80041A50.
    internal static void LAB_80041a1c()
    {
        int piVar1 = PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);

        if (PsxRam.ReadI32(piVar1) == 0)
        {
            FUN_80041aa8();
            PsxRam.WriteI32(piVar1, PsxRam.ReadI32(piVar1) + 1);
        }
        else if (PsxRam.ReadI32(piVar1) == 1 && (PsxRam.ReadU16(Dat800b7216Address) & 1) != 0)
        {
            FUN_80041d20();
        }
    }

    // GHIDRA: FUN_80041aa8 @ 0x80041AA8 (VS.EXE)
    // 632 bytes, three callees (SetPolyFT4, SetSemiTrans, SetShadeTex), one caller. Builds the eight
    // POLY_FT4 packets at DAT_800B733C and the eight four-corner SVECTOR sets at DAT_800B7234, then
    // raises DAT_800b7216 bit 0 so LAB_80041A1C's state 1 will start submitting them.
    //
    // The eight quads form one strip: each is 0x100 wide in x (sVar5/sVar6 step by 0x80 and the
    // corners are laid out at -0x200/-0x180 growing) at a fixed z of 0x9d8, with y at -124 on the
    // near edge and 4 on the far one. The texture is tpage 0x008f, clut 0x7900, and the u/v pairs
    // alternate 0x00/0x7f against 0x80/0xff on odd packets -- the `(iVar7 & 1) << 7` the
    // `andi v1,s5,1 / sll v1,v1,7` at 0x80041C90 computes, which Ghidra prints as the equivalent
    // `(char)iVar7 * -0x80`.
    //
    // See this file's header for the two offsets recovered from the instructions where Ghidra printed
    // 64-bit index artifacts: -0x0f (tpage, 0x008f) and -0x17 (clut, 0x7900).
    private static void FUN_80041aa8()
    {
        int p = Dat800b733cAddress;
        int iVar7 = 0;
        short sVar6 = -0x180;
        short sVar5 = -0x200;
        int iVar4 = 0;
        int pcVar3 = Dat800b7361Address;

        PsxRam.WriteU8(Dat800b7218Address, 0x80);
        PsxRam.WriteU8(Dat800b7219Address, 0x80);
        PsxRam.WriteU8(Dat800b721aAddress, 0x80);
        PsxRam.WriteU16(Dat800b7224Address, 0);
        PsxRam.WriteU16(Dat800b7226Address, 0);
        PsxRam.WriteU16(Dat800b7228Address, 0);
        PsxRam.WriteU16(Dat800b722aAddress, 0);
        PsxRam.WriteU16(Dat800b721cAddress, 0);
        PsxRam.WriteU16(Dat800b721eAddress, 0);
        PsxRam.WriteU16(Dat800b7220Address, 0);
        PsxRam.WriteU16(Dat800b7222Address, 0);
        PsxRam.WriteU16(Dat800b722cAddress, 0x7000);
        PsxRam.WriteU16(Dat800b722eAddress, 0x7000);
        PsxRam.WriteU16(Dat800b7230Address, 0x1000);
        PsxRam.WriteU16(Dat800b7232Address, 0);
        PsxRam.WriteU16(Dat800b7216Address, (ushort)(PsxRam.ReadU16(Dat800b7216Address) | 1));

        do
        {
            POLY_FT4Ref packet = PolyFt4At(p);
            LibGpu.SetPolyFT4(packet);
            LibGpu.SetSemiTrans(packet, 0);
            LibGpu.SetShadeTex(packet, 0);
            p = p + 0x28;

            PsxRam.WriteU16(Dat800b723cAddress + iVar4, unchecked((ushort)sVar6));
            PsxRam.WriteU16(Dat800b724cAddress + iVar4, unchecked((ushort)sVar6));
            sVar6 = (short)(sVar6 + 0x80);
            PsxRam.WriteU16(Dat800b7236Address + iVar4, 0xff84);
            PsxRam.WriteU16(Dat800b7234Address + iVar4, unchecked((ushort)sVar5));
            PsxRam.WriteU16(Dat800b7238Address + iVar4, 0x9d8);
            PsxRam.WriteU16(Dat800b723eAddress + iVar4, 0xff84);
            PsxRam.WriteU16(Dat800b7240Address + iVar4, 0x9d8);
            PsxRam.WriteU16(Dat800b7244Address + iVar4, unchecked((ushort)sVar5));
            PsxRam.WriteU16(Dat800b7246Address + iVar4, 4);
            PsxRam.WriteU16(Dat800b7248Address + iVar4, 0x9d8);
            PsxRam.WriteU16(Dat800b724eAddress + iVar4, 4);
            PsxRam.WriteU16(Dat800b7250Address + iVar4, 0x9d8);

            // The two halfwords Ghidra printed as four char stores at 64-bit indices. See the
            // header: `sh v0,-15(s0)` with 0x008f and `sh v0,-23(s0)` with 0x7900.
            PsxRam.WriteU16(pcVar3 - 0x0f, 0x008f);
            PsxRam.WriteU16(pcVar3 - 0x17, 0x7900);

            sVar5 = (short)(sVar5 + 0x80);
            iVar4 = iVar4 + 0x20;
            int cVar2 = (iVar7 & 1) << 7;

            PsxRam.WriteU8(pcVar3 - 0x21, PsxRam.ReadU8(Dat800b7218Address));
            PsxRam.WriteU8(pcVar3 - 0x20, PsxRam.ReadU8(Dat800b7219Address));
            iVar7 = iVar7 + 1;

            byte cVar1 = PsxRam.ReadU8(Dat800b721aAddress);
            PsxRam.WriteU8(pcVar3 - 0x18, (byte)cVar2);
            PsxRam.WriteU8(pcVar3 - 0x10, (byte)cVar2);
            PsxRam.WriteU8(pcVar3 - 0x11, 0x7f);
            PsxRam.WriteU8(pcVar3 - 1, 0x7f);
            PsxRam.WriteU8(pcVar3 - 0x19, 0);
            PsxRam.WriteU8(pcVar3 - 9, 0);
            PsxRam.WriteU8(pcVar3 - 8, (byte)(cVar2 + 0x7f));
            PsxRam.WriteU8(pcVar3, (byte)(cVar2 + 0x7f));
            PsxRam.WriteU8(pcVar3 - 0x1f, cVar1);

            pcVar3 = pcVar3 + 0x28;
        } while (iVar7 < 8);
    }

    // GHIDRA: FUN_80041d20 @ 0x80041D20 (VS.EXE)
    // 820 bytes, eleven callees. The per-frame half of LAB_80041A1C: it scrolls the eight quads
    // sideways from the camera's own angle and world offset, transforms all four corners of each,
    // and splices all eight into the ordering table at DAT_8008d420 + 0x78.
    //
    // THE SCROLL. DAT_800b7334 is (DAT_1f80008e / 2) minus two 2^-27 terms built from
    // rsin/rcos of the camera angle times the two world offsets -- the shifts at 0x80041DD0 and
    // 0x80041DEC are `sra 27` with the `+ 0x07ffffff` toward-zero correction, so they are truncating
    // divisions by 0x8000000 and not scalings this port may simplify. Its LOW HALFWORD then drives
    // the eight quads: quad i sits at ((DAT_800b7334 + i * 0x80) & 0x3ff) - 0x200 on the near edge
    // and the same - 0x180 on the far one, which is what wraps the strip.
    //
    // ROTAVERAGE4'S RETURN IS DISCARDED HERE, unlike in FUN_80041724 -- there is no per-quad depth
    // test and no bucket arithmetic. All eight go into the SAME bucket, DAT_8008d420 + 0x78, in a
    // separate loop after PopMatrix. Two loops, not one; not merged.
    //
    // THE TWO FLAG SINKS ARE CROSSED. RotTrans is given lStack_20 (sp+104) as its flag output, and
    // RotAverage4 is given lStack_1c (sp+108) as `p` and lStack_20 (sp+104) as `flag` -- so the
    // vector call and the projection call share sp+104. Nothing reads either back. Written as the
    // instructions have it (`sw v0,0x20(sp)` with v0 = sp+104 at 0x80041FC4).
    private static void FUN_80041d20()
    {
        LibGte.SVECTOR local_60 = new();
        LibGte.SVECTOR local_58 = new();
        LibGte.VECTOR local_50 = new();
        LibGte.MATRIX auStack_40 = new();
        int[] lStack_20 = new int[1];
        int[] lStack_1c = new int[1];
        LibGte.SVECTOR c0 = new();
        LibGte.SVECTOR c1 = new();
        LibGte.SVECTOR c2 = new();
        LibGte.SVECTOR c3 = new();

        PsxRam.WriteU16(Dat800b7226Address, unchecked((ushort)VS_EXE_exe.DAT_1f800098));

        int iVar2 = LibGte.rsin(Scratchpad.SVECTOR_1f80007c.vy);
        int iVar3 = LibGte.rcos(Scratchpad.SVECTOR_1f80007c.vy);

        iVar2 = (iVar2 / 2) * Scratchpad._DAT_1f8000bc;
        if (iVar2 < 0)
        {
            iVar2 = iVar2 + 0x7ffffff;
        }

        iVar3 = (iVar3 / 2) * Scratchpad._DAT_1f8000b4;
        if (iVar3 < 0)
        {
            iVar3 = iVar3 + 0x7ffffff;
        }

        PsxRam.WriteI32(Dat800b7334Address,
            ((VS_EXE_exe.DAT_1f80008e - (VS_EXE_exe.DAT_1f80008e >> 0x1f)) >> 1)
            - (iVar2 >> 0x1b) - (iVar3 >> 0x1b));

        iVar2 = 0;
        do
        {
            short sVar4 = (short)iVar2;
            iVar2 = iVar2 + 1;
            iVar3 = sVar4;

            ushort uVar1 = (ushort)((PsxRam.ReadU16(Dat800b7334Address) + sVar4 * 0x80) & 0x3ff);
            short sVar5 = (short)(uVar1 - 0x200);
            sVar4 = (short)(uVar1 - 0x180);

            PsxRam.WriteU16(Dat800b7244Address + iVar3 * 0x20, unchecked((ushort)sVar5));
            PsxRam.WriteU16(Dat800b7234Address + iVar3 * 0x20, unchecked((ushort)sVar5));
            PsxRam.WriteU16(Dat800b724cAddress + iVar3 * 0x20, unchecked((ushort)sVar4));
            PsxRam.WriteU16(Dat800b723cAddress + iVar3 * 0x20, unchecked((ushort)sVar4));
        } while (iVar2 * 0x10000 >> 0x10 < 8);

        iVar2 = 0;
        LibGte.PushMatrix();
        int puVar6 = Dat800b733cAddress;

        local_60.vx = (short)(Scratchpad.SVECTOR_1f80007c.vx - PsxRam.ReadU16(Dat800b721cAddress));
        local_60.vy = (short)-PsxRam.ReadU16(Dat800b7220Address);
        local_60.vz = (short)(Scratchpad.SVECTOR_1f80007c.vz - PsxRam.ReadU16(Dat800b7220Address));

        LibGte.RotMatrix(local_60, auStack_40);
        LibGte.SetRotMatrix(auStack_40);

        local_58.vx = (short)(PsxRam.ReadU16(Dat800b7224Address) - VS_EXE_exe.DAT_1f800094);
        local_58.vy = (short)(PsxRam.ReadU16(Dat800b7226Address) - VS_EXE_exe.DAT_1f800098);
        local_58.vz = (short)(PsxRam.ReadU16(Dat800b7228Address)
                              - BattleScene.DAT_1f80009c + Scratchpad._DAT_1f8000c0);

        LibGte.RotTrans(local_58, auStack_40.t, lStack_20);

        local_50.vx = (short)PsxRam.ReadU16(Dat800b722cAddress);
        local_50.vy = (short)PsxRam.ReadU16(Dat800b722eAddress);
        local_50.vz = (short)PsxRam.ReadU16(Dat800b7230Address);

        LibGte.ScaleMatrix(auStack_40, local_50);
        LibGte.SetTransMatrix(auStack_40);
        LibGte.SetRotMatrix(auStack_40);

        do
        {
            iVar3 = (iVar2 << 0x10) >> 0xb;

            LoadSVector(Dat800b7234Address + iVar3, c0);
            LoadSVector(Dat800b723cAddress + iVar3, c1);
            LoadSVector(Dat800b7244Address + iVar3, c2);
            LoadSVector(Dat800b724cAddress + iVar3, c3);

            if (!LibGpu.RamResolve(puVar6, out byte[] pBuf, out int pOff))
            {
                // PARTIAL: unreachable while RAM_800b7210 above is registered.
                pBuf = RAM_800b7210;
                pOff = 0;
            }

            LibGte.RotAverage4(c0, c1, c2, c3,
                pBuf, pOff + 8, pOff + 0x10, pOff + 0x18, pOff + 0x20,
                lStack_1c, lStack_20);

            iVar2 = iVar2 + 1;
            puVar6 = puVar6 + 0x28;
        } while (iVar2 * 0x10000 >> 0x10 < 8);

        LibGte.PopMatrix();

        iVar2 = 0;
        puVar6 = Dat800b733cAddress;
        do
        {
            LibGpu.AddPrim(VS_EXE_exe.DAT_8008d420 + 0x78, puVar6);
            iVar2 = iVar2 + 1;
            puVar6 = puVar6 + 0x28;
        } while (iVar2 * 0x10000 >> 0x10 < 8);
    }
}
