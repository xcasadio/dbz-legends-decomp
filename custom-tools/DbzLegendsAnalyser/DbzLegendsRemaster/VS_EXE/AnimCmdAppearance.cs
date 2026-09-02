using PsxSdkMonogame;
using static PsxSdkMonogame.Kernel;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.VS_EXE;

// VS.EXE's animation-script VM — the APPEARANCE family of command handlers.
//
// THE VM. ExecuteAnimStreamBatch @ 0x80036768 is a threaded interpreter over halfword streams held
// in PSX RAM:
//
//     while (uVar1 != 0) {
//         puVar2 = (*(code *)(&g_animStreamDispatchTable)[*puVar2 & 0xff])(puVar2, iVar6 >> 0x10);
//         uVar1 = *puVar2;
//     }
//
// so the opcode is the LOW BYTE of the first halfword, every handler RETURNS the address of the
// next command, and a zero halfword ends the stream. g_animStreamDispatchTable @ 0x800822F4 holds
// 51 handler pointers; a second table at 0x800823C0 holds 50 sixteen-byte ASCII names, one per
// opcode. THE BINARY NAMES ITS OWN OPCODES, and those names are the best naming evidence in the
// program. Handler 50 (0x8003EF04) has no name entry — the tables are 51 and 50 long.
//
// Table alignment was re-verified here rather than assumed, by reading both tables raw:
// index 2 -> 0x800373A0 `table_set`, index 3 -> 0x80037E30 `load_set`, index 5 -> 0x80037F20
// `anm_set`, index 9 -> 0x80038720 `cul_set`, index 10 -> 0x80038A34 `pri_set`. All five agree
// with the handlers Ghidra already names, so the two tables are index-for-index aligned and the
// opcode numbers below are exact.
//
// THIS FILE'S TEN OPCODES — colour, texture, depth and primitives:
//
//   | op | image name  | address    | Ghidra symbol                  |
//   |----|-------------|------------|--------------------------------|
//   | 10 | pri_set     | 0x80038A34 | AnimCmd_AddPrimsToOT           |
//   | 11 | colrol_set  | 0x80038BD0 | AnimCmd_AsyncLoadTexture  (*)  |
//   | 13 | tpclut_set  | 0x8003913C | AnimCmd_AnimateVertexColors(*) |
//   | 14 | rgb_set     | 0x80039600 | AnimCmd_AnimatePolyColorRGBA   |
//   | 19 | rgb2_set    | 0x8003A1AC | AnimCmd_Rgb2Set                |
//   | 32 | uv0123_set  | 0x8003C9EC | AnimCmd_Uv0123Set              |
//   | 37 | xy0123_set  | 0x8003D42C | AnimCmd_Xy0123Set              |
//   | 38 | ot_z_set    | 0x8003D974 | AnimCmd_OtZSet                 |
//   | 42 | auto_otz    | 0x8003E7C4 | LAB_8003e7c4 (no function)     |
//   | 43 | auto_rgb    | 0x8003E86C | (no symbol, no function)       |
//
// (*) two Ghidra names this slice had to adjudicate; see the two ADJUDICATION blocks below.
//
// THE PRIMITIVE THE WHOLE FAMILY EDITS IS ONE POLY_GT4. Six of the ten handlers write fields of
// the array at 0x801F7180 with a stride of 0x34 = 52 bytes, which is POLY_GT4's size and the
// fourth entry of PrimitivePools' g_PrimitiveSizeTable. Every base address the family uses lands
// on a named POLY_GT4 field, and that is what closes the opcode meanings:
//
//   0x801F7180 + 0  = tag       pri_set walks whole primitives from here
//   0x801F7180 + 4  = r0/g0/b0  rgb_set and rgb2_set, +16 / +28 / +40 for v1..v3
//   0x801F7180 + 7  = code      rgb_set's two flag bits, rgb2_set's low two bits
//   0x801F7188      = x0,y0     xy0123_set, +20 / +32 / +44 for v1..v3
//   0x801F718C      = u0,v0     uv0123_set, +24 / +36 / +48 for v1..v3
//   0x801F718E      = clut      tpclut_set, first of its two fields
//   0x801F719A      = tpage     tpclut_set, second of its two fields  (0x801F718E + 12)
//
// ADJUDICATION 1 — OPCODE 13. The image calls it `tpclut_set`; Ghidra calls it
// AnimCmd_AnimateVertexColors and carries a CERTAIN comment claiming homology with a GAME.EXE
// function of that name. THE IMAGE NAME IS THE ONE THE EVIDENCE SUPPORTS. The handler's write
// cursor starts at DAT_801f718e, that is POLY_GT4 + 14, and its inner loop advances by 6 halfwords
// = 12 bytes for exactly two iterations, so the two halfwords it writes are +14 and +26 — `clut`
// and `tpage`. Neither is a colour. The two operands it applies to them are streamPtr[2] and
// streamPtr[3], each skipped when negative, which is a CLUT id and a texture-page id being set
// independently. The handler that really animates vertex colour is opcode 14 `rgb_set` at
// 0x80039600, whose cursor starts at POLY_GT4 + 4 and writes r/g/b for four vertices. Both cannot
// be "animate vertex colors". Ghidra's symbol is kept verbatim in this file's `GHIDRA:` lines
// because that is what the project database holds; the C# name comes from the image.
//
// ADJUDICATION 2 — OPCODE 11. The image calls it `colrol_set`; Ghidra calls it
// AnimCmd_AsyncLoadTexture, with no comment behind the name. THE IMAGE NAME IS THE ONE THE
// EVIDENCE SUPPORTS, and the refutation is complete. The handler performs no CD access of any
// kind: it fills a 12-byte record in a four-slot table at DAT_80099090 and, on its other form,
// calls FUN_80061f1c @ 0x80061F1C on that record. FUN_80061f1c memmoves 0x20 bytes — sixteen
// halfwords, one 4-bit CLUT — out of the record's pointer, rotates the entries between the record's
// byte at +9 and its byte at +10 by the phase in its byte at +8, optionally forces or clears the
// STP bit per the flags at +11, and LoadImages the result back to VRAM as a 0x10 x 1 rectangle at
// the x/y in the record at +4 and +6. That is colour rolling, field for field:
//
//   record +0 = source CLUT pointer     colrol_set writes g_cdFileBufferTable[streamPtr[1]]
//   record +4 = VRAM x                  colrol_set writes streamPtr[3]
//   record +6 = VRAM y                  colrol_set writes streamPtr[4]
//   record +8 = roll phase              colrol_set writes lo(streamPtr[5]); the step form decrements it
//   record +9 = first CLUT entry rolled colrol_set writes hi(streamPtr[5])
//   record +A = last CLUT entry rolled  colrol_set writes lo(streamPtr[6])
//   record +B = flags + repeat counter  colrol_set writes hi(streamPtr[6]); the step form counts it down
//
// The source pointer is read out of an ALREADY LOADED buffer's own offset table
// (g_cdFileBufferTable indexed as words), not fetched, so nothing about the command is a load and
// nothing about it is asynchronous.
//
// pri_set NEEDS NO ADJUDICATION and the concordance is worth recording: "pri" is PRIMITIVE, not
// priority. AnimCmd_AddPrimsToOT walks whole 0x34-byte primitives and hands each to AddPrim, and
// the depth it inserts at comes from a separate array the separate opcode `ot_z_set` maintains.
// Ghidra's name and the image's name say the same thing.
//
// HOW THE STREAM IS REPRESENTED. A command stream is raw PSX memory walked by pointer, and the
// interpreter re-reads *puVar2 from the ADDRESS a handler returns. So a handler here takes and
// returns an `int` PSX address and reads through PsxRam, exactly as VS_EXE/TaskSystem.cs walks task
// nodes and VS_EXE/PrimitivePools.cs walks pool cursors. Copying a stream into a ushort[] would
// break the interpreter's contract and is not done.
//
// HANDLER SIGNATURE. The dispatch site passes two arguments, `(puVar2, iVar6 >> 0x10)`. All ten
// handlers in this family take one: Ghidra's eight defined functions all have parameterCount 1, and
// the two undefined ones derive their entry index from the stream itself (auto_otz from the header
// byte & 7, auto_rgb from streamPtr[2] & 7) rather than from the second argument. The second
// argument is therefore simply unused here, and the C# signatures show one parameter.
//
// ==== OWNERSHIP OF SHARED STATE — READ THIS BEFORE ADDING A SECOND COPY =====================
//
// This file is one family of one tranche. It touches globals and one helper that other families of
// the same VM also touch, and it may not edit their files. The rules it followed:
//
//  * SHARED BUFFERS ARE REACHED BY ADDRESS, NOT REDECLARED, AND THIS SHARES STORAGE WITH THE SLICE
//    THAT BUILDS THEM. g_renderMetadataBuffer, g_meshCountBuffer, the POLY_GT4 array at 0x801F7180,
//    the OTZ array at 0x801FA580 and g_animSharedVarTable are declared below only as `const int`
//    addresses and are read and written through PsxRam. That is not a placeholder: VS_EXE/AnimCmdMesh.cs
//    — the render family, which owns table_set @ 0x800373A0 and is what fills these buffers —
//    declares the whole render workspace once, as
//        internal static readonly byte[] RAM_801f2000 = LibGpu.RamRegion(0x801F2000, 0x8C48)
//    covering 0x801F2000..0x801FAC47, the exact extent that handler's own `bzero(&DAT_801f2000,
//    0x8c48)` clears. EVERY address this file touches in that range falls inside it — 0x801F7180,
//    0x801F7188, 0x801F718C, 0x801F718E, 0x801FA580, 0x801FA880, 0x801FA980, 0x801FAA64 — so these
//    handlers read and write the same bytes table_set wrote, with no cross-file reference and no
//    second buffer. VS_EXE/AnimCmdTransform.cs reaches them the same way.
//    Declaring a typed C# array for any of them here would silently split the buffer in two, and
//    would also break `pri_set`, which must hand AddPrim a real PSX address.
//    KNOWN DIVERGENCE, reported rather than edited: VS_EXE/AnimCmdControl.cs and
//    VS_EXE/AnimCmdSound.cs model some of these same globals as typed C# arrays instead
//    (`ushort[] g_animSharedVarTable` — and Sound sizes it 16 where Control sizes it 256), so those
//    two do NOT share storage with AnimCmdMesh or with this file. Those are not this slice's files.
//  * FUN_8003f540 IS TRANSLITERATED HERE ANYWAY, AND IS NOW THE FOURTH COPY. It is the VM's generic
//    operator, called from 42 sites across every family, and six of this file's ten handlers cannot
//    do anything without it. AnimCmdMesh, AnimCmdControl and AnimCmdTransform each already carry a
//    private transliteration of it. All four must collapse to one. Keep a version whose operator
//    parameter is at least 5 bits wide and not narrowed — this file's is `int` — because
//    `xy0123_set` passes raw five-bit operator fields that reach 0x1F and MUST miss every case and
//    fall through to the unchanged-value tail; that fall-through is the "leave this channel alone"
//    encoding, not an error path.
//  * FUN_8003f310 IS EXCLUSIVELY THIS FAMILY'S. Its only three callers, 0x8003A484, 0x8003A554 and
//    0x8003A64C, are all inside AnimCmd_Rgb2Set. It belongs here and nowhere else.
//  * FUN_80061f1c IS NOT TRANSLITERATED HERE. See AnimCmd_ColrolSet's PARTIAL note.
//  * THE WIRING GAP IS REAL AND IS NOT THIS FILE'S TO CLOSE. PsxSdkBridges installs
//    PsxRam.AddressResolver per overlay and has no VS.EXE row, and VS_EXE_exe has no
//    ResolveAddress, so AnimCmdMesh's workspace region is not yet reachable through PsxRam and
//    every address access in this file currently resolves to nothing and answers zero. AnimCmdMesh
//    states the same gap. It closes in VS_EXE_exe.cs and PsxSdkBridges.cs, not here.
internal static class AnimCmdAppearance
{
    // =====================================================================================
    // Globals — addresses only. See the ownership block above for why nothing here allocates.
    // =====================================================================================

    // g_animSharedVarTable, g_renderMetadataBuffer, g_meshCountBuffer, DAT_801f7180 and
    // AnimVm.DAT_800b305a are the VM's SHARED globals; they are declared once in AnimVm.cs and reached
    // here as AnimVm.<name>. See AnimVm.cs for the merged proof comments.

    // GHIDRA: DAT_801f7188 @ 0x801F7188 (VS.EXE)
    // 0x801F7180 + 8 — POLY_GT4.x0. xy0123_set's write cursor.
    private const int Dat801f7188Address = AnimVm.DAT_801f7180 + 8;

    // GHIDRA: DAT_801f718c @ 0x801F718C (VS.EXE)
    // 0x801F7180 + 12 — POLY_GT4.u0/v0, addressed as one halfword. uv0123_set's write cursor.
    private const int Dat801f718cAddress = AnimVm.DAT_801f7180 + 12;

    // GHIDRA: DAT_801f718e @ 0x801F718E (VS.EXE)
    // 0x801F7180 + 14 — POLY_GT4.clut. tpclut_set's write cursor; +12 bytes from it is .tpage.
    private const int Dat801f718eAddress = AnimVm.DAT_801f7180 + 14;

    // GHIDRA: DAT_801fa580 @ 0x801FA580 (VS.EXE)
    // One halfword of OTZ per primitive, parallel to the POLY_GT4 array. `ot_z_set` writes it and
    // `pri_set` reads it to choose the ordering-table slot; that pairing is what makes the two
    // opcodes one mechanism.
    private const int Dat801fa580Address = unchecked((int)0x801FA580);

    // GHIDRA: DAT_8008d420 @ 0x8008D420 (VS.EXE)
    // The active DRAWENV's address; the ordering table is at +0x70 from it. `pri_set` is the only
    // user in this file.
    // PARTIAL: the port already holds this global, as a PRIVATE C# field in VS_EXE/VS_EXE_exe.cs
    // (`private static int DAT_8008d420`, written at 0x80062328's transliteration). It is not
    // PsxRam-backed and this file may not edit that one, so the read below currently yields 0 and
    // pri_set inserts at 0x70 + (0x7ff - z) * 4 instead of at the environment's table. The flow is
    // the original's; only the value is out of reach. The fix is one line in VS_EXE_exe.cs — expose
    // the field, or back it at 0x8008D420 — and belongs to that file's owner, not here. No accessor
    // is invented for it in this file.
    private const int Dat8008d420Address = unchecked((int)0x8008D420);

    // GHIDRA: DAT_80099090 @ 0x80099090 (VS.EXE)
    // THE COLOUR-ROLL TABLE. Records of 12 bytes; `colrol_set` addresses four of them (its slot
    // index is masked & 3). The table is longer than four: ExecuteAnimStreamBatch rolls
    // DAT_800990c0 and DAT_800990cc every frame unconditionally, and those are 0x80099090 + 0x30
    // and + 0x3C, i.e. records 4 and 5. Field layout is in this file's ADJUDICATION 2 block.
    private const int Dat80099090Address = unchecked((int)0x80099090);

    // =====================================================================================
    // Opcode 10 — `pri_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_AddPrimsToOT @ 0x80038A34 (VS.EXE)
    // Opcode 10, which the image's name table calls `pri_set`. The Ghidra name and the image name
    // agree — "pri" is PRIMITIVE — and this is the only opcode in the family that submits anything.
    //
    // It finds the render entry whose metadata byte +2 matches the sign-extended header byte, then
    // walks streamPtr[1] primitives from that entry's first primitive index, reading each one's OTZ
    // out of DAT_801fa580 and splicing the primitive into the ordering table at 0x7ff - otz when
    // that OTZ is inside (0, 0x800). Two halfwords long.
    //
    // The sign extension matters and is reproduced: uVar5 starts as a SIGN-EXTENDED char widened to
    // ushort, so a negative header byte makes uVar5 0xFFxx and the byte comparison can never match
    // — the whole command becomes a no-op. Only the 0x10 branch narrows it back to four bits.
    internal static int AnimCmd_AddPrimsToOT(int streamPtr)
    {
        ushort uVar1;
        sbyte cVar2;
        int iVar3;
        uint uVar4;
        ushort uVar5;
        int iVar6;
        int p;
        int puVar7;

        cVar2 = (sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        uVar5 = (ushort)cVar2;
        if ((((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18) & 0x10) == 0)
        {
            uVar1 = PsxRam.ReadU16(streamPtr + 2);
        }
        else
        {
            uVar1 = PsxRam.ReadU16(
                AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(streamPtr + 2) * 2);
            uVar5 = (ushort)(cVar2 & 0xf);
        }
        puVar7 = streamPtr + 4;
        iVar6 = 0;
        iVar3 = 0;
        do
        {
            uVar4 = (uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + (iVar3 >> 0xe)) >> 0x18;
            if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + (iVar3 >> 0xe) + 2) == uVar5)
            {
                p = AnimVm.DAT_801f7180 + (int)(uVar4 * 0x34);
                iVar3 = 0;
                if ((short)uVar1 < 1)
                {
                    return puVar7;
                }
                do
                {
                    iVar6 = (short)PsxRam.ReadU16(
                        Dat801fa580Address + ((int)(uVar4 << 0x10) >> 0xf));
                    uVar4 = uVar4 + 1;
                    if ((iVar6 < 0x800) && (0 < iVar6))
                    {
                        AddPrim((0x7ff - iVar6) * 4 + 0x70 + PsxRam.ReadI32(Dat8008d420Address), p);
                    }
                    iVar3 = iVar3 + 1;
                    p = p + 0x34;
                } while (iVar3 * 0x10000 >> 0x10 < (int)(short)uVar1);
                return puVar7;
            }
            iVar6 = iVar6 + 1;
            iVar3 = iVar6 * 0x10000;
        } while (iVar6 * 0x10000 >> 0x10 < 0x40);
        return puVar7;
    }

    // =====================================================================================
    // Opcode 11 — `colrol_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_AsyncLoadTexture @ 0x80038BD0 (VS.EXE)
    // Opcode 11, which the image's name table calls `colrol_set`. THE TWO NAMES DISAGREE AND THE
    // IMAGE IS RIGHT; the evidence is in this file's ADJUDICATION 2 block. The C# name comes from
    // the image, the `GHIDRA:` line above states the symbol the database actually carries.
    //
    // Two forms, chosen by bit 7 of the sign-extended header byte:
    //   * set    (bit 7 on)  — seven halfwords: install a whole roll descriptor into slot n & 3;
    //   * step   (bit 7 off) — one halfword: roll the slot once, and if the header carries a repeat
    //                          count in bits 12..14, install it into the slot's own counter (bits
    //                          4..6 of +0xB) the first time and count that counter down thereafter.
    //
    // Both forms leave through the same trailing store to +0xB, which is why the original computes
    // cVar2 on each path and writes it once at the end. That shape is kept.
    internal static int AnimCmd_ColrolSet(int streamPtr)
    {
        ushort uVar1;
        sbyte cVar2;
        int iVar3;
        uint uVar4;
        int puVar5;

        uVar1 = PsxRam.ReadU16(streamPtr);
        puVar5 = streamPtr + 2;
        uVar4 = (uint)((int)((uint)uVar1 << 0x10) >> 0x18);
        iVar3 = (int)((uVar4 & 3) * 0xc);
        if ((uVar4 & 0x80) == 0)
        {
            if ((AnimVm.DAT_800b305a & 1) != 0)
            {
                return puVar5;
            }

            // PARTIAL: FUN_80061f1c @ 0x80061F1C — the roll-and-upload step itself — is NOT called
            // here, and this is the one place in the file where an original side effect is missing.
            // It is not this family's function: two of its three call sites are in
            // ExecuteAnimStreamBatch @ 0x80036768 (0x80036980 and 0x800369A0, rolling records 4 and
            // 5 every frame), and it writes the scratch RECT at 0x8008D48C which VS_EXE/FileIo.cs
            // already models as a PRIVATE field, so transliterating it here would put a second copy
            // of an existing global into the port. Its semantics ARE closed — read the field-by-field
            // account in ADJUDICATION 2, which was derived from its body — so whoever owns
            // 0x80061F1C can port it once and wire this call site to it. Everything else in this
            // handler, including every store to the descriptor, is transliterated.
            //   original: FUN_80061f1c(&DAT_80099090 + iVar3);

            if ((uVar1 & 0x7000) == 0)
            {
                return puVar5;
            }
            if (((PsxRam.ReadU8(Dat80099090Address + iVar3 + 0xb) >> 4) & 7) == 0)
            {
                PsxRam.WriteU8(Dat80099090Address + iVar3 + 0xb,
                    (byte)(PsxRam.ReadU8(Dat80099090Address + iVar3 + 0xb) | ((uVar1 >> 8) & 0x70)));
                PsxRam.WriteU8(Dat80099090Address + iVar3 + 8,
                    (byte)(PsxRam.ReadU8(Dat80099090Address + iVar3 + 8) - 1));
            }
            cVar2 = (sbyte)(PsxRam.ReadU8(Dat80099090Address + iVar3 + 0xb) - 0x10);
        }
        else
        {
            if ((AnimVm.DAT_800b305a & 1) != 0)
            {
                return streamPtr + 14;
            }
            PsxRam.WriteI32(Dat80099090Address + iVar3,
                PsxRam.ReadI32(FileIo.g_cdFileBufferTableAddress
                               + (short)PsxRam.ReadU16(puVar5) * 4));
            PsxRam.WriteU16(Dat80099090Address + iVar3 + 4, PsxRam.ReadU16(streamPtr + 6));
            PsxRam.WriteU16(Dat80099090Address + iVar3 + 6, PsxRam.ReadU16(streamPtr + 8));
            uVar1 = PsxRam.ReadU16(streamPtr + 10);
            PsxRam.WriteU8(Dat80099090Address + iVar3 + 8, (byte)uVar1);
            PsxRam.WriteU8(Dat80099090Address + iVar3 + 9, (byte)(uVar1 >> 8));
            uVar1 = PsxRam.ReadU16(streamPtr + 12);
            PsxRam.WriteU8(Dat80099090Address + iVar3 + 0xa, (byte)uVar1);
            puVar5 = streamPtr + 14;
            cVar2 = (sbyte)(uVar1 >> 8);
        }
        PsxRam.WriteU8(Dat80099090Address + iVar3 + 0xb, (byte)cVar2);
        return puVar5;
    }

    // =====================================================================================
    // Opcode 13 — `tpclut_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_AnimateVertexColors @ 0x8003913C (VS.EXE)
    // Opcode 13, which the image's name table calls `tpclut_set`. THE TWO NAMES DISAGREE AND THE
    // IMAGE IS RIGHT; see ADJUDICATION 1. The C# name comes from the image, the `GHIDRA:` line
    // states the symbol the database carries.
    //
    // Four halfwords. Sets POLY_GT4.clut (+14) from streamPtr[2] and POLY_GT4.tpage (+26) from
    // streamPtr[3], each through the VM operator in the header's low nibble and each skipped when
    // its operand is negative. Three selection modes on header bits 4..5, the family's usual set:
    //   0x00  a run of entries starting at auStack_50[1], one primitive per entry
    //   0x10  a run of entries, all of each entry's primitives
    //   0x20  scan all 64 entries for a matching tag byte, count down auStack_50[2]
    //
    // THE THIRD ARGUMENT TO FUN_8003f540 IS REAL AND GHIDRA HIDES IT. The decompiler prints
    // `FUN_8003f540(x, op)` here with two arguments, which would leave the operand undefined. The
    // disassembly says otherwise: 0x800393D8 `lh a2,0x10(v0)` loads a2 from sp+0x10 + iVar1 * 2,
    // that is auStack_50[iVar1], immediately before the `jal 0x8003f540` at 0x800393F4. The
    // decompiler dropped it because it reuses a2 for the negativity test on the line above. The
    // third argument is passed here.
    internal static int AnimCmd_TpClutSet(int streamPtr)
    {
        int iVar1;
        ushort uVar2;
        uint uVar3;
        int puVar4;
        int puVar5;
        int iVar6;
        int iVar7;

        // auStack_50[0..3] plus uStack_48, which sits at sp+0x18 — 8 bytes past auStack_50 at
        // sp+0x10 — and is therefore auStack_50[4]. The original indexes across the two as one
        // array (`*(short *)((int)auStack_50 + ((iVar1 << 0x10) >> 0xf))` with iVar1 = 3 then 4).
        ushort[] auStack_50 = new ushort[5];
        ushort uStack_40;
        ushort uStack_3e;

        auStack_50[0] = (ushort)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        auStack_50[1] = (ushort)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        auStack_50[2] = (ushort)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        auStack_50[3] = PsxRam.ReadU16(streamPtr + 4);
        auStack_50[4] = PsxRam.ReadU16(streamPtr + 6);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            if (((((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18) & 0x40) != 0)
                && (((int)(short)auStack_50[3] & 0x8000) == 0))
            {
                auStack_50[3] = PsxRam.ReadU16(
                    AnimVm.g_animSharedVarTable + (short)auStack_50[3] * 2);
            }
            if (((auStack_50[0] & 0x80) != 0) && (((int)(short)auStack_50[4] & 0x8000) == 0))
            {
                auStack_50[4] = PsxRam.ReadU16(
                    AnimVm.g_animSharedVarTable + (short)auStack_50[4] * 2);
            }
            uVar2 = (ushort)(auStack_50[0] & 0x30);
            if (uVar2 == 0x10)
            {
                uVar3 = (uint)(short)auStack_50[1];
                if (uVar3 < uVar3 + (uint)(int)(short)auStack_50[2])
                {
                    do
                    {
                        iVar6 = 0;
                        uStack_40 = PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (short)uVar3 * 2);
                        puVar4 = Dat801f718eAddress
                                 + PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer
                                                 + (short)uVar3 * 4 + 3) * 0x34;
                        if (0 < (int)((uint)PsxRam.ReadU16(
                                          AnimVm.g_meshCountBuffer + (short)uVar3 * 2) << 0x10))
                        {
                            do
                            {
                                iVar1 = 3;
                                do
                                {
                                    puVar5 = puVar4;
                                    if (((int)(short)auStack_50[iVar1] & 0x8000) == 0)
                                    {
                                        uStack_3e = (ushort)FUN_8003f540(
                                            (uint)(int)(short)PsxRam.ReadU16(puVar5),
                                            auStack_50[0] & 0xf,
                                            (uint)(int)(short)auStack_50[iVar1]);
                                        if ((int)((uint)uStack_3e << 0x10) < 0)
                                        {
                                            uStack_3e = 0;
                                        }
                                        PsxRam.WriteU16(puVar5, uStack_3e);
                                    }
                                    iVar1 = iVar1 + 1;
                                    puVar4 = puVar5 + 6 * 2;
                                } while (iVar1 * 0x10000 >> 0x10 < 5);
                                iVar6 = iVar6 + 1;
                                puVar4 = puVar5 + 0x14 * 2;
                            } while (iVar6 * 0x10000 >> 0x10 < (int)(short)uStack_40);
                        }
                        uVar3 = uVar3 + 1;
                    } while ((int)(uVar3 * 0x10000) >> 0x10
                             < (int)(short)auStack_50[1] + (int)(short)auStack_50[2]);
                }
            }
            else if (uVar2 < 0x11)
            {
                if ((auStack_50[0] & 0x30) == 0)
                {
                    uVar3 = (uint)(short)auStack_50[1];
                    puVar4 = Dat801f718eAddress + (int)(uVar3 * 0x34);
                    if (uVar3 < uVar3 + (uint)(int)(short)auStack_50[2])
                    {
                        do
                        {
                            iVar6 = 3;
                            do
                            {
                                puVar5 = puVar4;
                                if (((int)(short)auStack_50[iVar6] & 0x8000) == 0)
                                {
                                    uStack_3e = (ushort)FUN_8003f540(
                                        (uint)(int)(short)PsxRam.ReadU16(puVar5),
                                        auStack_50[0] & 0xf,
                                        (uint)(int)(short)auStack_50[iVar6]);
                                    if ((int)((uint)uStack_3e << 0x10) < 0)
                                    {
                                        uStack_3e = 0;
                                    }
                                    PsxRam.WriteU16(puVar5, uStack_3e);
                                }
                                iVar6 = iVar6 + 1;
                                puVar4 = puVar5 + 6 * 2;
                            } while (iVar6 * 0x10000 >> 0x10 < 5);
                            uVar3 = uVar3 + 1;
                            puVar4 = puVar5 + 0x14 * 2;
                        } while ((int)(uVar3 * 0x10000) >> 0x10
                                 < (int)(short)auStack_50[1] + (int)(short)auStack_50[2]);
                    }
                }
            }
            else
            {
                iVar6 = 0;
                if (uVar2 == 0x20)
                {
                    iVar1 = 0;
                    do
                    {
                        iVar1 = iVar1 >> 0x10;
                        if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar1 * 4 + 2)
                            == auStack_50[1])
                        {
                            puVar4 = Dat801f718eAddress
                                     + (int)(((uint)PsxRam.ReadI32(
                                         AnimVm.g_renderMetadataBuffer + iVar1 * 4) >> 0x18) * 0x34);
                            uStack_40 = PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar1 * 2);
                            iVar7 = 0;
                            if (0 < (int)((uint)PsxRam.ReadU16(
                                              AnimVm.g_meshCountBuffer + iVar1 * 2) << 0x10))
                            {
                                do
                                {
                                    iVar1 = 3;
                                    do
                                    {
                                        puVar5 = puVar4;
                                        if (((int)(short)auStack_50[iVar1] & 0x8000) == 0)
                                        {
                                            uStack_3e = (ushort)FUN_8003f540(
                                                (uint)(int)(short)PsxRam.ReadU16(puVar5),
                                                auStack_50[0] & 0xf,
                                                (uint)(int)(short)auStack_50[iVar1]);
                                            if ((int)((uint)uStack_3e << 0x10) < 0)
                                            {
                                                uStack_3e = 0;
                                            }
                                            PsxRam.WriteU16(puVar5, uStack_3e);
                                        }
                                        iVar1 = iVar1 + 1;
                                        puVar4 = puVar5 + 6 * 2;
                                    } while (iVar1 * 0x10000 >> 0x10 < 5);
                                    iVar7 = iVar7 + 1;
                                    puVar4 = puVar5 + 0x14 * 2;
                                } while (iVar7 * 0x10000 >> 0x10 < (int)(short)uStack_40);
                            }
                            uVar2 = auStack_50[2];
                            auStack_50[2] = (ushort)(auStack_50[2] - 1);
                            if (uVar2 == 1)
                            {
                                return streamPtr + 8;
                            }
                        }
                        iVar6 = iVar6 + 1;
                        iVar1 = iVar6 * 0x10000;
                    } while (iVar6 * 0x10000 >> 0x10 < 0x40);
                }
            }
        }
        return streamPtr + 8;
    }

    // =====================================================================================
    // Opcode 14 — `rgb_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_AnimatePolyColorRGBA @ 0x80039600 (VS.EXE)
    // Opcode 14, which the image's name table calls `rgb_set`. The two names agree in substance —
    // this IS the vertex-colour opcode — so nothing had to be adjudicated; the C# name is the
    // image's because it is the shorter statement of the same thing.
    //
    // Four halfwords. Per primitive it walks four vertices; per vertex it applies the operator in
    // the header's low nibble to r, g and b (bytes +0, +1, +2 from the vertex colour) with the three
    // operands auStack_40[3..5], clamping each result to 0..255, and then, on header bits 6 and 7,
    // forces bit 0 and bit 1 of the following byte — POLY_GT4.code for vertex 0 and the p1/p2/p3
    // pad bytes for the rest — from bits 0 and 1 of sStack_34.
    //
    // THE DOUBLE STORE TO THE FLAG BYTE IS THE ORIGINAL'S. `*pbVar5 = bVar1 & 0xfe;` then
    // `*pbVar5 = bVar1 & 0xfe | ...;` is two stores to the same byte; rule 12 forbids collapsing it.
    internal static int AnimCmd_RgbSet(int streamPtr)
    {
        byte bVar1;
        ushort uVar2;
        uint uVar3;
        uint uVar4;
        int pbVar5;
        int pbVar6;
        int iVar7;
        int iVar8;
        int iVar9;
        int iVar10;
        int puVar11;

        // auStack_40[0..3] plus sStack_38, uStack_36 and sStack_34, which sit at sp+8, +10 and +12
        // past auStack_40 — that is indices 4, 5 and 6 of the same array, which is how the original
        // reaches them (`(int)auStack_40 + ((iVar7 << 0x10) >> 0xf)` with iVar7 = 3, 4, 5).
        ushort[] auStack_40 = new ushort[7];
        ushort uStack_30;
        ushort uStack_2e;

        auStack_40[0] = (ushort)(short)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        uVar4 = (uint)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        auStack_40[2] = (ushort)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        auStack_40[1] = (ushort)uVar4;
        auStack_40[3] = (ushort)(PsxRam.ReadU16(streamPtr + 4) & 0xff);
        auStack_40[4] = (ushort)(short)(sbyte)(PsxRam.ReadU16(streamPtr + 4) >> 8);
        puVar11 = streamPtr + 8;
        auStack_40[5] = (ushort)(PsxRam.ReadU16(streamPtr + 6) & 0xff);
        auStack_40[6] = (ushort)(short)(sbyte)(PsxRam.ReadU16(streamPtr + 6) >> 8);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar3 = (uint)((((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18)) & 0x30);
            if (uVar3 == 0x10)
            {
                iVar9 = 0;
                iVar10 = 0;
                do
                {
                    uVar4 = (uint)PsxRam.ReadI32(AnimVm.g_renderMetadataBuffer + (iVar10 >> 0xe));
                    iVar9 = iVar9 + 1;
                    if ((uVar4 & 0xff) == (uint)(int)(short)auStack_40[1])
                    {
                        pbVar5 = AnimVm.DAT_801f7180 + (int)((uVar4 >> 0x18) * 0x34);
                        iVar10 = 0;
                        if (auStack_40[2] == 0)
                        {
                            return puVar11;
                        }
                        do
                        {
                            pbVar5 = pbVar5 + 4;
                            iVar9 = 0;
                            do
                            {
                                iVar7 = 3;
                                do
                                {
                                    pbVar6 = pbVar5;
                                    uStack_2e = (ushort)FUN_8003f540(
                                        PsxRam.ReadU8(pbVar6),
                                        auStack_40[0] & 0xf,
                                        (uint)(int)(short)auStack_40[iVar7]);
                                    if ((int)((uint)uStack_2e << 0x10) < 0)
                                    {
                                        uStack_2e = 0;
                                    }
                                    if (0xff < (short)uStack_2e)
                                    {
                                        uStack_2e = 0xff;
                                    }
                                    iVar7 = iVar7 + 1;
                                    PsxRam.WriteU8(pbVar6, (byte)uStack_2e);
                                    pbVar5 = pbVar6 + 1;
                                } while (iVar7 * 0x10000 >> 0x10 < 6);
                                if ((auStack_40[0] & 0x40) != 0)
                                {
                                    bVar1 = PsxRam.ReadU8(pbVar5);
                                    PsxRam.WriteU8(pbVar5, (byte)(bVar1 & 0xfe));
                                    PsxRam.WriteU8(pbVar5,
                                        (byte)((bVar1 & 0xfe) | ((byte)auStack_40[6] & 1)));
                                }
                                if ((auStack_40[0] & 0x80) != 0)
                                {
                                    bVar1 = PsxRam.ReadU8(pbVar5);
                                    PsxRam.WriteU8(pbVar5, (byte)(bVar1 & 0xfd));
                                    PsxRam.WriteU8(pbVar5,
                                        (byte)((bVar1 & 0xfd) | ((byte)auStack_40[6] & 2)));
                                }
                                iVar9 = iVar9 + 1;
                                pbVar5 = pbVar6 + 10;
                            } while (iVar9 * 0x10000 >> 0x10 < 4);
                            iVar10 = iVar10 + 1;
                        } while (iVar10 * 0x10000 >> 0x10 < (int)(short)auStack_40[2]);
                        return puVar11;
                    }
                    iVar10 = iVar9 * 0x10000;
                } while (iVar9 * 0x10000 >> 0x10 < 0x40);
            }
            else if (uVar3 < 0x11)
            {
                if (uVar3 == 0)
                {
                    pbVar5 = AnimVm.DAT_801f7180 + (int)(uVar4 * 0x34);
                    iVar10 = 0;
                    if (auStack_40[2] != 0)
                    {
                        do
                        {
                            pbVar5 = pbVar5 + 4;
                            iVar9 = 0;
                            do
                            {
                                iVar7 = 3;
                                do
                                {
                                    pbVar6 = pbVar5;
                                    uStack_2e = (ushort)FUN_8003f540(
                                        PsxRam.ReadU8(pbVar6),
                                        auStack_40[0] & 0xf,
                                        (uint)(int)(short)auStack_40[iVar7]);
                                    if ((int)((uint)uStack_2e << 0x10) < 0)
                                    {
                                        uStack_2e = 0;
                                    }
                                    if (0xff < (short)uStack_2e)
                                    {
                                        uStack_2e = 0xff;
                                    }
                                    iVar7 = iVar7 + 1;
                                    PsxRam.WriteU8(pbVar6, (byte)uStack_2e);
                                    pbVar5 = pbVar6 + 1;
                                } while (iVar7 * 0x10000 >> 0x10 < 6);
                                if ((auStack_40[0] & 0x40) != 0)
                                {
                                    bVar1 = PsxRam.ReadU8(pbVar5);
                                    PsxRam.WriteU8(pbVar5, (byte)(bVar1 & 0xfe));
                                    PsxRam.WriteU8(pbVar5,
                                        (byte)((bVar1 & 0xfe) | ((byte)auStack_40[6] & 1)));
                                }
                                if ((auStack_40[0] & 0x80) != 0)
                                {
                                    bVar1 = PsxRam.ReadU8(pbVar5);
                                    PsxRam.WriteU8(pbVar5, (byte)(bVar1 & 0xfd));
                                    PsxRam.WriteU8(pbVar5,
                                        (byte)((bVar1 & 0xfd) | ((byte)auStack_40[6] & 2)));
                                }
                                iVar9 = iVar9 + 1;
                                pbVar5 = pbVar6 + 10;
                            } while (iVar9 * 0x10000 >> 0x10 < 4);
                            iVar10 = iVar10 + 1;
                        } while (iVar10 * 0x10000 >> 0x10 < (int)(short)auStack_40[2]);
                    }
                }
            }
            else
            {
                iVar10 = 0;
                if (uVar3 == 0x20)
                {
                    iVar9 = 0;
                    do
                    {
                        iVar9 = iVar9 >> 0x10;
                        if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar9 * 4 + 2)
                            == auStack_40[1])
                        {
                            pbVar5 = AnimVm.DAT_801f7180
                                     + (int)(((uint)PsxRam.ReadI32(
                                         AnimVm.g_renderMetadataBuffer + iVar9 * 4) >> 0x18) * 0x34);
                            uStack_30 = PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar9 * 2);
                            iVar7 = 0;
                            if (0 < (int)((uint)PsxRam.ReadU16(
                                              AnimVm.g_meshCountBuffer + iVar9 * 2) << 0x10))
                            {
                                do
                                {
                                    pbVar5 = pbVar5 + 4;
                                    iVar9 = 0;
                                    do
                                    {
                                        iVar8 = 3;
                                        do
                                        {
                                            pbVar6 = pbVar5;
                                            uStack_2e = (ushort)FUN_8003f540(
                                                PsxRam.ReadU8(pbVar6),
                                                auStack_40[0] & 0xf,
                                                (uint)(int)(short)auStack_40[iVar8]);
                                            if ((int)((uint)uStack_2e << 0x10) < 0)
                                            {
                                                uStack_2e = 0;
                                            }
                                            if (0xff < (short)uStack_2e)
                                            {
                                                uStack_2e = 0xff;
                                            }
                                            iVar8 = iVar8 + 1;
                                            PsxRam.WriteU8(pbVar6, (byte)uStack_2e);
                                            pbVar5 = pbVar6 + 1;
                                        } while (iVar8 * 0x10000 >> 0x10 < 6);
                                        if ((auStack_40[0] & 0x40) != 0)
                                        {
                                            bVar1 = PsxRam.ReadU8(pbVar5);
                                            PsxRam.WriteU8(pbVar5, (byte)(bVar1 & 0xfe));
                                            PsxRam.WriteU8(pbVar5,
                                                (byte)((bVar1 & 0xfe) | ((byte)auStack_40[6] & 1)));
                                        }
                                        if ((auStack_40[0] & 0x80) != 0)
                                        {
                                            bVar1 = PsxRam.ReadU8(pbVar5);
                                            PsxRam.WriteU8(pbVar5, (byte)(bVar1 & 0xfd));
                                            PsxRam.WriteU8(pbVar5,
                                                (byte)((bVar1 & 0xfd) | ((byte)auStack_40[6] & 2)));
                                        }
                                        iVar9 = iVar9 + 1;
                                        pbVar5 = pbVar6 + 10;
                                    } while (iVar9 * 0x10000 >> 0x10 < 4);
                                    iVar7 = iVar7 + 1;
                                } while (iVar7 * 0x10000 >> 0x10 < (int)(short)uStack_30);
                            }
                            uVar2 = auStack_40[2];
                            auStack_40[2] = (ushort)(auStack_40[2] - 1);
                            if (uVar2 == 1)
                            {
                                return puVar11;
                            }
                        }
                        iVar10 = iVar10 + 1;
                        iVar9 = iVar10 * 0x10000;
                    } while (iVar10 * 0x10000 >> 0x10 < 0x40);
                }
            }
        }
        return puVar11;
    }

    // =====================================================================================
    // Opcode 19 — `rgb2_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_Rgb2Set @ 0x8003A1AC (VS.EXE)
    // Opcode 19, which the image's name table calls `rgb2_set`. Ghidra's name is the image's name
    // already; nothing to reconcile.
    //
    // Four or five halfwords. Where `rgb_set` carries one operator and three operands, this one
    // packs FOUR five-bit operator fields into streamPtr[2] — bits 0..4, 5..9, 10..14 and, through
    // bit 15, a fourth for the flag byte — and pulls their operands out of streamPtr[3] one byte at
    // a time, spilling into streamPtr[4] when more than two were consumed. An operator whose low
    // nibble is 0xF means "leave this channel alone": FUN_8003f540 has no case 0xF, so it returns
    // its input unchanged. Bit 4 of a field means its operand is a variable index.
    //
    // WHEN A CHANNEL IS SKIPPED ITS OPERAND IS NEVER WRITTEN. The original leaves in_t1, s8,
    // sStack_48 and uStack_40 holding whatever the register or stack slot held, and passes that to
    // FUN_8003f310 — where the matching operator field is 0xF and the value is discarded. C#
    // requires definite assignment, so the four are initialised to 0 here. That is a language
    // bridge, not a repair: on every path where the value can affect the result it is assigned.
    //
    // The three call sites of the per-primitive worker below are 0x8003A484, 0x8003A554 and
    // 0x8003A64C, all inside this function; that is the whole of FUN_8003f310's caller set.
    internal static int AnimCmd_Rgb2Set(int streamPtr)
    {
        bool bVar1;
        ushort uVar2;
        ushort uVar3;
        byte bVar4;
        int iVar5;
        byte bVar6;
        uint uVar7;
        uint uVar8;
        uint uVar9;
        short in_t1 = 0;
        ushort uVar10;
        int iVar11;
        int puVar12;
        ushort uVar13;
        ushort uVar14;
        short unaff_s8 = 0;
        ushort uStack_5e;
        ushort uStack_5c;
        ushort uStack_58;
        short sStack_48 = 0;
        ushort uStack_40 = 0;

        uVar2 = PsxRam.ReadU16(streamPtr);
        bVar4 = (byte)(uVar2 >> 8);
        uVar7 = (uint)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        uStack_5e = (ushort)uVar7;
        if (((((int)((uint)uVar2 << 0x10) >> 0x18)) & 0x40) != 0)
        {
            uStack_5e = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar7 * 2));
        }
        uStack_5c = (ushort)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        uVar3 = PsxRam.ReadU16(streamPtr + 4);
        uVar10 = 0;
        uStack_58 = PsxRam.ReadU16(streamPtr + 6);
        puVar12 = streamPtr + 8;
        uVar14 = (ushort)(uVar3 & 0x1f);
        if ((uVar3 & 0xf) != 0xf)
        {
            uVar7 = (uint)(uStack_58 & 0xff);
            if ((uVar3 & 0x10) != 0)
            {
                uVar7 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar7 * 2));
            }
            in_t1 = (short)uVar7;
            uStack_58 = (ushort)(uStack_58 >> 8);
            uVar10 = 1;
            uVar14 = (ushort)(uVar3 & 0xf);
        }
        uVar8 = (uint)((int)((uint)uVar3 << 0x10) >> 0x15);
        uVar7 = uVar8 & 0x1f;
        if ((uVar8 & 0xf) != 0xf)
        {
            uVar7 = (uint)(uStack_58 & 0xff);
            if ((uVar8 & 0x10) != 0)
            {
                uVar7 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar7 * 2));
            }
            unaff_s8 = (short)uVar7;
            uStack_58 = (ushort)(uStack_58 >> 8);
            uVar10 = (ushort)(uVar10 + 1);
            uVar7 = uVar8 & 0xf;
        }
        if (uVar10 == 2)
        {
            uStack_58 = PsxRam.ReadU16(puVar12);
        }
        uVar9 = (uint)((int)((uint)uVar3 << 0x10) >> 0x1a);
        uVar8 = uVar9 & 0x1f;
        if ((uVar9 & 0xf) != 0xf)
        {
            sStack_48 = (short)(uStack_58 & 0xff);
            if ((uVar9 & 0x10) != 0)
            {
                sStack_48 = (short)PsxRam.ReadU16(
                    AnimVm.g_animSharedVarTable + (uStack_58 & 0xff) * 2);
            }
            uStack_58 = (ushort)(uStack_58 >> 8);
            uVar10 = (ushort)(uVar10 + 1);
            uVar8 = uVar9 & 0xf;
        }
        uVar13 = 0xf;
        if (((int)(short)uVar3 & 0x8000) == 0)
        {
            if (uVar10 == 2)
            {
                uStack_58 = PsxRam.ReadU16(puVar12);
            }
            uVar13 = (ushort)(uStack_58 & 0xf);
            uStack_40 = (ushort)((uStack_58 >> 5) & 3);
        }
        if (2 < uVar10)
        {
            puVar12 = streamPtr + 10;
        }
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            bVar6 = (byte)(bVar4 & 0x30);
            if (bVar6 == 0x10)
            {
                iVar11 = (int)(short)uStack_5e;
                if (iVar11 < iVar11 + (short)uStack_5c)
                {
                    do
                    {
                        FUN_8003f310((ushort)(bVar4 & 0xf),
                            PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (short)iVar11 * 2),
                            AnimVm.DAT_801f7180
                            + PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + (short)iVar11 * 4 + 3)
                              * 0x34,
                            (short)uVar14, in_t1, (short)uVar7, unaff_s8, (short)uVar8, sStack_48,
                            (short)uVar13, (short)uStack_40);
                        iVar11 = iVar11 + 1;
                    } while (iVar11 * 0x10000 >> 0x10
                             < (int)(short)uStack_5e + (int)(short)uStack_5c);
                }
            }
            else if (bVar6 < 0x11)
            {
                if ((uVar2 & 0x3000) == 0)
                {
                    FUN_8003f310((ushort)(bVar4 & 0xf), uStack_5c,
                        AnimVm.DAT_801f7180 + (short)uStack_5e * 0x34,
                        (short)uVar14, in_t1, (short)uVar7, unaff_s8, (short)uVar8, sStack_48,
                        (short)uVar13, (short)uStack_40);
                }
            }
            else
            {
                iVar11 = 0;
                if (bVar6 == 0x20)
                {
                    iVar5 = 0;
                    do
                    {
                        iVar5 = iVar5 >> 0x10;
                        if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar5 * 4 + 2)
                            == uStack_5e)
                        {
                            FUN_8003f310((ushort)(bVar4 & 0xf),
                                PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar5 * 2),
                                AnimVm.DAT_801f7180
                                + (int)(((uint)PsxRam.ReadI32(
                                    AnimVm.g_renderMetadataBuffer + iVar5 * 4) >> 0x18) * 0x34),
                                (short)uVar14, in_t1, (short)uVar7, unaff_s8, (short)uVar8,
                                sStack_48, (short)uVar13, (short)uStack_40);
                            bVar1 = uStack_5c == 1;
                            uStack_5c = (ushort)(uStack_5c - 1);
                            if (bVar1)
                            {
                                return puVar12;
                            }
                        }
                        iVar11 = iVar11 + 1;
                        iVar5 = iVar11 * 0x10000;
                    } while (iVar11 * 0x10000 >> 0x10 < 0x40);
                }
            }
        }
        return puVar12;
    }

    // GHIDRA: FUN_8003f310 @ 0x8003F310 (VS.EXE)
    // AnimCmd_Rgb2Set's per-primitive worker, and nothing else's: its only three callers are the
    // three call sites inside AnimCmd_Rgb2Set. Walks param_2 primitives of 0x34 bytes; inside each,
    // four vertices at +4, +16, +28 and +40, skipping vertex n when bit n of param_1 is set. Per
    // vertex it applies three operator/operand pairs to r, g and b with a 0..255 clamp, then folds
    // the low two bits of a fourth result into the byte behind them — POLY_GT4.code for vertex 0,
    // and the p1 / p2 / p3 pad bytes for the other three.
    private static void FUN_8003f310(ushort param_1, ushort param_2, int param_3, short param_4,
        short param_5, short param_6, short param_7, short param_8, short param_9, short param_10,
        short param_11)
    {
        byte bVar1;
        byte bVar2;
        int iVar3;
        int iVar4;
        byte uVar5;
        ushort uVar6;
        int iVar7;
        int iVar8;

        iVar8 = 0;
        if (0 < (int)((uint)param_2 << 0x10))
        {
            do
            {
                param_3 = param_3 + 4;
                uVar6 = 1;
                iVar7 = 0;
                do
                {
                    if ((param_1 & uVar6) == 0)
                    {
                        iVar4 = FUN_8003f540(PsxRam.ReadU8(param_3), param_4,
                            (uint)(int)param_5);
                        iVar3 = (iVar4 << 0x10) >> 0x10;
                        if (iVar4 << 0x10 < 0)
                        {
                            iVar4 = 0;
                            iVar3 = 0;
                        }
                        uVar5 = (byte)iVar4;
                        if (0xff < iVar3)
                        {
                            uVar5 = 0xff;
                        }
                        PsxRam.WriteU8(param_3, uVar5);
                        iVar4 = FUN_8003f540(PsxRam.ReadU8(param_3 + 1), param_6,
                            (uint)(int)param_7);
                        iVar3 = (iVar4 << 0x10) >> 0x10;
                        if (iVar4 << 0x10 < 0)
                        {
                            iVar4 = 0;
                            iVar3 = 0;
                        }
                        uVar5 = (byte)iVar4;
                        if (0xff < iVar3)
                        {
                            uVar5 = 0xff;
                        }
                        PsxRam.WriteU8(param_3 + 1, uVar5);
                        iVar4 = FUN_8003f540(PsxRam.ReadU8(param_3 + 2), param_8,
                            (uint)(int)param_9);
                        iVar3 = (iVar4 << 0x10) >> 0x10;
                        if (iVar4 << 0x10 < 0)
                        {
                            iVar4 = 0;
                            iVar3 = 0;
                        }
                        uVar5 = (byte)iVar4;
                        if (0xff < iVar3)
                        {
                            uVar5 = 0xff;
                        }
                        PsxRam.WriteU8(param_3 + 2, uVar5);
                        bVar1 = PsxRam.ReadU8(param_3 + 3);
                        bVar2 = (byte)FUN_8003f540(bVar1, param_10, (uint)(int)param_11);
                        PsxRam.WriteU8(param_3 + 3, (byte)((bVar1 & 0xfc) | (bVar2 & 3)));
                    }
                    uVar6 = (ushort)(uVar6 << 1);
                    param_3 = param_3 + 0xc;
                    iVar7 = iVar7 + 1;
                    iVar4 = iVar8 + 1;
                } while (iVar7 * 0x10000 >> 0x10 < 4);
                iVar8 = iVar4;
            } while (iVar4 * 0x10000 >> 0x10 < (int)(short)param_2);
        }
    }

    // =====================================================================================
    // Opcode 32 — `uv0123_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_Uv0123Set @ 0x8003C9EC (VS.EXE)
    // Opcode 32, `uv0123_set`. Ghidra's name is the image's; nothing to reconcile.
    //
    // Six halfwords. Writes the four UV halfwords of a POLY_GT4 — +12, +24, +36 and +48, each a
    // (u, v) byte pair addressed as one short — from streamPtr[2..5], through the operator in the
    // header's low nibble. The cursor starts at DAT_801f718c = POLY_GT4 + 12 and steps 6 halfwords
    // between vertices, then 8 more to reach the next primitive's +12: 12 + 3 * 12 + 16 = 64 =
    // 0x34 + 12. Same three selection modes as the rest of the family.
    internal static int AnimCmd_Uv0123Set(int streamPtr)
    {
        ushort uVar1;
        short sVar2;
        uint uVar3;
        uint uVar4;
        int iVar5;
        int psVar6;
        int psVar7;
        int iVar8;
        int iVar9;

        // auStack_50[0..3] plus uStack_48, uStack_46 and uStack_44 at +8, +10 and +12 — indices 4,
        // 5 and 6 of the same array, which is how the original indexes them with iVar9 = 3..6.
        ushort[] auStack_50 = new ushort[7];
        ushort uStack_38;

        uVar4 = (uint)((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18);
        auStack_50[0] = (ushort)(short)(sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        uVar1 = PsxRam.ReadU16(streamPtr + 2);
        uVar3 = (uint)(uVar1 & 0xff);
        auStack_50[1] = (ushort)uVar3;
        if ((uVar4 & 0x40) != 0)
        {
            auStack_50[1] = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar3 * 2));
        }
        iVar5 = (int)((uint)uVar1 << 0x10) >> 0x18;
        auStack_50[2] = (ushort)(short)(sbyte)(uVar1 >> 8);
        auStack_50[3] = PsxRam.ReadU16(streamPtr + 4);
        auStack_50[4] = PsxRam.ReadU16(streamPtr + 6);
        auStack_50[5] = PsxRam.ReadU16(streamPtr + 8);
        auStack_50[6] = PsxRam.ReadU16(streamPtr + 10);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar4 = uVar4 & 0x30;
            if (uVar4 == 0x10)
            {
                iVar8 = (int)(short)auStack_50[1];
                if (iVar8 < iVar8 + iVar5)
                {
                    do
                    {
                        iVar5 = 0;
                        uStack_38 = PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (short)iVar8 * 2);
                        psVar6 = Dat801f718cAddress
                                 + PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer
                                                 + (short)iVar8 * 4 + 3) * 0x34;
                        if (0 < (int)((uint)PsxRam.ReadU16(
                                          AnimVm.g_meshCountBuffer + (short)iVar8 * 2) << 0x10))
                        {
                            do
                            {
                                iVar9 = 3;
                                do
                                {
                                    psVar7 = psVar6;
                                    sVar2 = (short)FUN_8003f540(
                                        (uint)(int)(short)PsxRam.ReadU16(psVar7),
                                        auStack_50[0] & 0xf,
                                        (uint)(int)(short)auStack_50[iVar9]);
                                    PsxRam.WriteU16(psVar7, (ushort)sVar2);
                                    iVar9 = iVar9 + 1;
                                    psVar6 = psVar7 + 6 * 2;
                                } while (iVar9 * 0x10000 >> 0x10 < 7);
                                iVar5 = iVar5 + 1;
                                psVar6 = psVar7 + 8 * 2;
                            } while (iVar5 * 0x10000 >> 0x10 < (int)(short)uStack_38);
                        }
                        iVar8 = iVar8 + 1;
                    } while (iVar8 * 0x10000 >> 0x10
                             < (int)(short)auStack_50[1] + (int)(short)auStack_50[2]);
                }
            }
            else if (uVar4 < 0x11)
            {
                if (uVar4 == 0)
                {
                    psVar6 = Dat801f718cAddress + (short)auStack_50[1] * 0x34;
                    iVar8 = 0;
                    if (0 < iVar5)
                    {
                        do
                        {
                            iVar5 = 3;
                            do
                            {
                                psVar7 = psVar6;
                                sVar2 = (short)FUN_8003f540(
                                    (uint)(int)(short)PsxRam.ReadU16(psVar7),
                                    auStack_50[0] & 0xf,
                                    (uint)(int)(short)auStack_50[iVar5]);
                                PsxRam.WriteU16(psVar7, (ushort)sVar2);
                                iVar5 = iVar5 + 1;
                                psVar6 = psVar7 + 6 * 2;
                            } while (iVar5 * 0x10000 >> 0x10 < 7);
                            iVar8 = iVar8 + 1;
                            psVar6 = psVar7 + 8 * 2;
                        } while (iVar8 * 0x10000 >> 0x10 < (int)(short)auStack_50[2]);
                    }
                }
            }
            else
            {
                iVar5 = 0;
                if (uVar4 == 0x20)
                {
                    iVar8 = 0;
                    do
                    {
                        iVar8 = iVar8 >> 0x10;
                        if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar8 * 4 + 2)
                            == auStack_50[1])
                        {
                            psVar6 = Dat801f718cAddress
                                     + (int)(((uint)PsxRam.ReadI32(
                                         AnimVm.g_renderMetadataBuffer + iVar8 * 4) >> 0x18) * 0x34);
                            uStack_38 = PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar8 * 2);
                            iVar9 = 0;
                            if (0 < (int)((uint)PsxRam.ReadU16(
                                              AnimVm.g_meshCountBuffer + iVar8 * 2) << 0x10))
                            {
                                do
                                {
                                    iVar8 = 3;
                                    do
                                    {
                                        psVar7 = psVar6;
                                        sVar2 = (short)FUN_8003f540(
                                            (uint)(int)(short)PsxRam.ReadU16(psVar7),
                                            auStack_50[0] & 0xf,
                                            (uint)(int)(short)auStack_50[iVar8]);
                                        PsxRam.WriteU16(psVar7, (ushort)sVar2);
                                        iVar8 = iVar8 + 1;
                                        psVar6 = psVar7 + 6 * 2;
                                    } while (iVar8 * 0x10000 >> 0x10 < 7);
                                    iVar9 = iVar9 + 1;
                                    psVar6 = psVar7 + 8 * 2;
                                } while (iVar9 * 0x10000 >> 0x10 < (int)(short)uStack_38);
                            }
                            uVar1 = auStack_50[2];
                            auStack_50[2] = (ushort)(auStack_50[2] - 1);
                            if (uVar1 == 1)
                            {
                                return streamPtr + 12;
                            }
                        }
                        iVar5 = iVar5 + 1;
                        iVar8 = iVar5 * 0x10000;
                    } while (iVar5 * 0x10000 >> 0x10 < 0x40);
                }
            }
        }
        return streamPtr + 12;
    }

    // =====================================================================================
    // Opcode 37 — `xy0123_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_Xy0123Set @ 0x8003D42C (VS.EXE)
    // Opcode 37, `xy0123_set`. Ghidra's name is the image's; nothing to reconcile.
    //
    // Variable length: five halfwords of header plus one operand halfword per channel that is not
    // "leave alone". Eight channels — x0, y0, x1, y1, x2, y2, x3, y3 — with a five-bit operator
    // field each, three packed into streamPtr[2], three into streamPtr[3] and two into streamPtr[4].
    // The prologue loop reads one operand halfword per live channel and advances puVar16, which is
    // what the command returns, so its length is data-dependent.
    //
    // A channel whose low nibble is 0xF is skipped entirely: its operand slot is not read, its
    // auStack_50 entry is left alone, and FUN_8003f540 later falls through its switch and returns
    // the field unchanged. Bit 4 means the operand halfword is a variable index, and in that case —
    // and only that case — the original narrows auStack_68[i] to its low nibble.
    //
    // The write cursor starts at DAT_801f7188 = POLY_GT4 + 8 and each vertex writes the two shorts
    // at +0 and +2 (x and y), stepping 6 halfwords between vertices and 8 more to the next
    // primitive: 8 + 3 * 12 + 16 = 60 = 0x34 + 8.
    internal static int AnimCmd_Xy0123Set(int streamPtr)
    {
        bool bVar1;
        ushort uVar2;
        ushort uVar3;
        short sVar4;
        int iVar5;
        byte bVar6;
        ushort uVar7;
        int puVar8;
        int puVar9;
        int puVar10;
        int puVar11;
        int puVar16;
        int psVar12;
        int psVar13;
        int iVar14;
        int iVar15;
        short sStack_74;

        // auStack_68[0..4] plus uStack_5e, uStack_5c and uStack_5a at +10, +12 and +14 — indices 5,
        // 6 and 7 of the same array; the prologue loop walks all eight as one run.
        ushort[] auStack_68 = new ushort[8];
        ushort uStack_58;

        // The original declares this 20 halfwords long and writes only the first eight, one operand
        // per channel. A skipped channel leaves its slot untouched, which in the original is
        // whatever the frame held; C# zeroes it. It is only ever read when its operator is 0xF,
        // where FUN_8003f540 discards it.
        ushort[] auStack_50 = new ushort[20];

        uVar2 = PsxRam.ReadU16(streamPtr);
        iVar15 = 0;
        uVar7 = (ushort)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        sStack_74 = (short)(sbyte)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        uVar3 = PsxRam.ReadU16(streamPtr + 4);
        auStack_68[0] = (ushort)(uVar3 & 0x1f);
        auStack_68[1] = (ushort)(((short)uVar3 >> 5) & 0x1f);
        auStack_68[2] = (ushort)(((short)uVar3 >> 10) & 0x1f);
        uVar3 = PsxRam.ReadU16(streamPtr + 6);
        auStack_68[3] = (ushort)(uVar3 & 0x1f);
        auStack_68[4] = (ushort)(((short)uVar3 >> 5) & 0x1f);
        auStack_68[5] = (ushort)(((short)uVar3 >> 10) & 0x1f);
        puVar16 = streamPtr + 10;
        auStack_68[6] = (ushort)(PsxRam.ReadU16(streamPtr + 8) & 0x1f);
        auStack_68[7] = (ushort)((PsxRam.ReadU16(streamPtr + 8) >> 5) & 0x1f);
        iVar5 = 0;
        do
        {
            iVar5 = iVar5 >> 0xf;
            uVar3 = (ushort)(auStack_68[iVar5 >> 1] & 0xf);
            if (uVar3 != 0xf)
            {
                if ((auStack_68[iVar5 >> 1] & 0x10) == 0)
                {
                    uVar3 = PsxRam.ReadU16(puVar16);
                }
                else
                {
                    auStack_68[iVar5 >> 1] = uVar3;
                    uVar3 = PsxRam.ReadU16(
                        AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(puVar16) * 2);
                }
                puVar16 = puVar16 + 2;
                auStack_50[iVar5 >> 1] = uVar3;
            }
            iVar15 = iVar15 + 1;
            iVar5 = iVar15 * 0x10000;
        } while (iVar15 * 0x10000 >> 0x10 < 8);
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            bVar6 = (byte)((byte)(uVar2 >> 8) & 0x30);
            if (bVar6 == 0x10)
            {
                iVar5 = (int)(short)uVar7;
                if (iVar5 < iVar5 + sStack_74)
                {
                    do
                    {
                        iVar15 = 0;
                        uStack_58 = PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (short)iVar5 * 2);
                        psVar12 = Dat801f7188Address
                                  + PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer
                                                  + (short)iVar5 * 4 + 3) * 0x34;
                        if (0 < (int)((uint)PsxRam.ReadU16(
                                          AnimVm.g_meshCountBuffer + (short)iVar5 * 2) << 0x10))
                        {
                            do
                            {
                                puVar11 = 0;
                                puVar9 = 0;
                                iVar14 = 0;
                                do
                                {
                                    psVar13 = psVar12;
                                    puVar10 = puVar11 + 1;
                                    puVar8 = puVar9 + 1;
                                    sVar4 = (short)FUN_8003f540(
                                        (uint)(int)(short)PsxRam.ReadU16(psVar13),
                                        (short)auStack_68[puVar11],
                                        (uint)(int)(short)auStack_50[puVar9]);
                                    PsxRam.WriteU16(psVar13, (ushort)sVar4);
                                    puVar11 = puVar11 + 2;
                                    puVar9 = puVar9 + 2;
                                    sVar4 = (short)FUN_8003f540(
                                        (uint)(int)(short)PsxRam.ReadU16(psVar13 + 2),
                                        (short)auStack_68[puVar10],
                                        (uint)(int)(short)auStack_50[puVar8]);
                                    PsxRam.WriteU16(psVar13 + 2, (ushort)sVar4);
                                    iVar14 = iVar14 + 1;
                                    psVar12 = psVar13 + 6 * 2;
                                } while (iVar14 * 0x10000 >> 0x10 < 4);
                                iVar15 = iVar15 + 1;
                                psVar12 = psVar13 + 8 * 2;
                            } while (iVar15 * 0x10000 >> 0x10 < (int)(short)uStack_58);
                        }
                        iVar5 = iVar5 + 1;
                    } while (iVar5 * 0x10000 >> 0x10 < (int)(short)uVar7 + (int)sStack_74);
                }
            }
            else if (bVar6 < 0x11)
            {
                if ((uVar2 & 0x3000) == 0)
                {
                    iVar5 = (int)(short)uVar7;
                    psVar12 = Dat801f7188Address + iVar5 * 0x34;
                    if (iVar5 < iVar5 + sStack_74)
                    {
                        do
                        {
                            puVar11 = 0;
                            puVar9 = 0;
                            iVar15 = 0;
                            do
                            {
                                psVar13 = psVar12;
                                puVar10 = puVar11 + 1;
                                puVar8 = puVar9 + 1;
                                sVar4 = (short)FUN_8003f540(
                                    (uint)(int)(short)PsxRam.ReadU16(psVar13),
                                    (short)auStack_68[puVar11],
                                    (uint)(int)(short)auStack_50[puVar9]);
                                PsxRam.WriteU16(psVar13, (ushort)sVar4);
                                puVar11 = puVar11 + 2;
                                puVar9 = puVar9 + 2;
                                sVar4 = (short)FUN_8003f540(
                                    (uint)(int)(short)PsxRam.ReadU16(psVar13 + 2),
                                    (short)auStack_68[puVar10],
                                    (uint)(int)(short)auStack_50[puVar8]);
                                PsxRam.WriteU16(psVar13 + 2, (ushort)sVar4);
                                iVar15 = iVar15 + 1;
                                psVar12 = psVar13 + 6 * 2;
                            } while (iVar15 * 0x10000 >> 0x10 < 4);
                            iVar5 = iVar5 + 1;
                            psVar12 = psVar13 + 8 * 2;
                        } while (iVar5 * 0x10000 >> 0x10 < (int)(short)uVar7 + (int)sStack_74);
                    }
                }
            }
            else
            {
                iVar5 = 0;
                if (bVar6 == 0x20)
                {
                    iVar15 = 0;
                    do
                    {
                        iVar15 = iVar15 >> 0x10;
                        if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar15 * 4 + 2) == uVar7)
                        {
                            psVar12 = Dat801f7188Address
                                      + (int)(((uint)PsxRam.ReadI32(
                                          AnimVm.g_renderMetadataBuffer + iVar15 * 4) >> 0x18)
                                              * 0x34);
                            uStack_58 = PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar15 * 2);
                            iVar14 = 0;
                            if (0 < (int)((uint)PsxRam.ReadU16(
                                              AnimVm.g_meshCountBuffer + iVar15 * 2) << 0x10))
                            {
                                do
                                {
                                    puVar11 = 0;
                                    puVar9 = 0;
                                    iVar15 = 0;
                                    do
                                    {
                                        psVar13 = psVar12;
                                        puVar10 = puVar11 + 1;
                                        puVar8 = puVar9 + 1;
                                        sVar4 = (short)FUN_8003f540(
                                            (uint)(int)(short)PsxRam.ReadU16(psVar13),
                                            (short)auStack_68[puVar11],
                                            (uint)(int)(short)auStack_50[puVar9]);
                                        PsxRam.WriteU16(psVar13, (ushort)sVar4);
                                        puVar11 = puVar11 + 2;
                                        puVar9 = puVar9 + 2;
                                        sVar4 = (short)FUN_8003f540(
                                            (uint)(int)(short)PsxRam.ReadU16(psVar13 + 2),
                                            (short)auStack_68[puVar10],
                                            (uint)(int)(short)auStack_50[puVar8]);
                                        PsxRam.WriteU16(psVar13 + 2, (ushort)sVar4);
                                        iVar15 = iVar15 + 1;
                                        psVar12 = psVar13 + 6 * 2;
                                    } while (iVar15 * 0x10000 >> 0x10 < 4);
                                    iVar14 = iVar14 + 1;
                                    psVar12 = psVar13 + 8 * 2;
                                } while (iVar14 * 0x10000 >> 0x10 < (int)(short)uStack_58);
                            }
                            bVar1 = sStack_74 == 1;
                            sStack_74 = (short)(sStack_74 + -1);
                            if (bVar1)
                            {
                                return puVar16;
                            }
                        }
                        iVar5 = iVar5 + 1;
                        iVar15 = iVar5 * 0x10000;
                    } while (iVar5 * 0x10000 >> 0x10 < 0x40);
                }
            }
        }
        return puVar16;
    }

    // =====================================================================================
    // Opcode 38 — `ot_z_set`
    // =====================================================================================

    // GHIDRA: AnimCmd_OtZSet @ 0x8003D974 (VS.EXE)
    // Opcode 38, `ot_z_set`. Ghidra's name is the image's; nothing to reconcile.
    //
    // Three halfwords. Applies one operator/operand pair to the OTZ halfwords in DAT_801fa580, the
    // array parallel to the POLY_GT4 pool that `pri_set` reads back when it chooses an
    // ordering-table slot. That pairing is the whole depth mechanism of this VM.
    //
    // ITS MODE FIELD IS BITS 5..6, NOT 4..5. Every other handler in this family switches on
    // header & 0x30; this one switches on header & 0x60, because bit 4 is already spoken for as the
    // "operand is a variable index" flag — and when that flag is taken the original clears it with
    // `& 0xef` before the switch. Reproduced as written.
    internal static int AnimCmd_OtZSet(int streamPtr)
    {
        bool bVar1;
        ushort uVar2;
        sbyte cVar3;
        short sVar4;
        short sVar5;
        ushort uVar6;
        ushort uVar7;
        int iVar8;
        int psVar9;
        int iVar10;
        ushort uStack_40;
        short sStack_3c;

        cVar3 = (sbyte)(PsxRam.ReadU16(streamPtr) >> 8);
        uStack_40 = (ushort)cVar3;
        uVar6 = (ushort)(PsxRam.ReadU16(streamPtr + 2) & 0xff);
        sStack_3c = (short)(sbyte)(PsxRam.ReadU16(streamPtr + 2) >> 8);
        if ((((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18) & 0x10) == 0)
        {
            uVar2 = PsxRam.ReadU16(streamPtr + 4);
        }
        else
        {
            uVar2 = PsxRam.ReadU16(
                AnimVm.g_animSharedVarTable + (short)PsxRam.ReadU16(streamPtr + 4) * 2);
            uStack_40 = (ushort)(cVar3 & 0xef);
        }
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar7 = (ushort)(uStack_40 & 0x60);
            if (uVar7 == 0x40)
            {
                iVar10 = 0;
                iVar8 = 0;
                do
                {
                    iVar8 = iVar8 >> 0x10;
                    if (PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer + iVar8 * 4 + 2) == uVar6)
                    {
                        psVar9 = Dat801fa580Address
                                 + (int)(((uint)PsxRam.ReadI32(
                                     AnimVm.g_renderMetadataBuffer + iVar8 * 4) >> 0x18) * 2);
                        sVar4 = (short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + iVar8 * 2);
                        iVar8 = 0;
                        if (0 < sVar4)
                        {
                            do
                            {
                                sVar5 = (short)FUN_8003f540(
                                    (uint)(int)(short)PsxRam.ReadU16(psVar9),
                                    uStack_40 & 0xf,
                                    (uint)(int)(short)uVar2);
                                PsxRam.WriteU16(psVar9, (ushort)sVar5);
                                iVar8 = iVar8 + 1;
                                psVar9 = psVar9 + 2;
                            } while (iVar8 * 0x10000 >> 0x10 < (int)sVar4);
                        }
                        bVar1 = sStack_3c == 1;
                        sStack_3c = (short)(sStack_3c + -1);
                        if (bVar1)
                        {
                            return streamPtr + 6;
                        }
                    }
                    iVar10 = iVar10 + 1;
                    iVar8 = iVar10 * 0x10000;
                } while (iVar10 * 0x10000 >> 0x10 < 0x40);
            }
            else if (uVar7 < 0x41)
            {
                if ((uStack_40 & 0x60) == 0)
                {
                    iVar8 = (int)(short)uVar6;
                    psVar9 = Dat801fa580Address + iVar8 * 2;
                    if (iVar8 < iVar8 + sStack_3c)
                    {
                        do
                        {
                            sVar4 = (short)FUN_8003f540(
                                (uint)(int)(short)PsxRam.ReadU16(psVar9),
                                uStack_40 & 0xf,
                                (uint)(int)(short)uVar2);
                            PsxRam.WriteU16(psVar9, (ushort)sVar4);
                            iVar8 = iVar8 + 1;
                            psVar9 = psVar9 + 2;
                        } while (iVar8 * 0x10000 >> 0x10 < (int)(short)uVar6 + (int)sStack_3c);
                    }
                }
                // The original is one condition with a comma expression inside it:
                //   else if ((uVar7 == 0x20) && (iVar8 = (int)(short)uVar6, iVar8 < iVar8 + sStack_3c))
                // C# has no comma operator. Split into the nested form, which evaluates in the same
                // order and assigns iVar8 on exactly the same paths: the assignment is reached only
                // when uVar7 == 0x20, and nothing follows this arm of the chain.
                else if (uVar7 == 0x20)
                {
                    iVar8 = (int)(short)uVar6;
                    if (iVar8 < iVar8 + sStack_3c)
                    {
                    do
                    {
                        iVar10 = 0;
                        sVar4 = (short)PsxRam.ReadU16(AnimVm.g_meshCountBuffer + (short)iVar8 * 2);
                        psVar9 = Dat801fa580Address
                                 + PsxRam.ReadU8(AnimVm.g_renderMetadataBuffer
                                                 + (short)iVar8 * 4 + 3) * 2;
                        if (0 < sVar4)
                        {
                            do
                            {
                                sVar5 = (short)FUN_8003f540(
                                    (uint)(int)(short)PsxRam.ReadU16(psVar9),
                                    uStack_40 & 0xf,
                                    (uint)(int)(short)uVar2);
                                PsxRam.WriteU16(psVar9, (ushort)sVar5);
                                iVar10 = iVar10 + 1;
                                psVar9 = psVar9 + 2;
                            } while (iVar10 * 0x10000 >> 0x10 < (int)sVar4);
                        }
                        iVar8 = iVar8 + 1;
                    } while (iVar8 * 0x10000 >> 0x10 < (int)(short)uVar6 + (int)sStack_3c);
                    }
                }
            }
        }
        return streamPtr + 6;
    }

    // =====================================================================================
    // Opcode 42 — `auto_otz`
    // =====================================================================================

    // GHIDRA: LAB_8003e7c4 @ 0x8003E7C4 (VS.EXE)
    // Opcode 42, `auto_otz`. GHIDRA HAS NO FUNCTION HERE — only the bare label the dispatch table's
    // word at 0x8008239C creates, exactly as PrimitivePools found at 0x80060CDC — so the annotation
    // above states the label, which is what the database holds. The C# name is the image's.
    //
    // Two halfwords. Writes one int at +0x140 of the render state hanging off the running task, and
    // the entry it picks comes from the header byte & 7 indexing the context's pointer array at
    // +0x18. Two forms on bit 12 of the header halfword:
    //   bit clear — depth measured from the far end: (0x800 - ctx[0x13C]) - z
    //   bit set   — depth taken literally: z
    // and bit 11 makes z a variable index. Both are the OTZ base the geometry pass adds to, which
    // is why this is the "auto" counterpart of `ot_z_set`.
    internal static int AnimCmd_AutoOtz(int streamPtr)
    {
        int iVar1;
        uint uVar2;
        ushort uVar3;

        uVar3 = PsxRam.ReadU16(streamPtr + 2);
        uVar2 = (uint)((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x1b);
        if ((uVar2 & 1) != 0)
        {
            uVar3 = PsxRam.ReadU16(
                AnimVm.g_animSharedVarTable + ((int)((uint)uVar3 << 0x10) >> 0xf));
        }
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            iVar1 = PsxRam.ReadI32(
                (int)(((uint)(((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18)) & 7) * 4)
                + PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8) + 0x18);
            if ((uVar2 & 2) == 0)
            {
                PsxRam.WriteI32(iVar1 + 0x140,
                    (0x800 - PsxRam.ReadI32(iVar1 + 0x13c)) - (int)(short)uVar3);
            }
            else
            {
                PsxRam.WriteI32(iVar1 + 0x140, (int)(short)uVar3);
            }
        }
        return streamPtr + 4;
    }

    // =====================================================================================
    // Opcode 43 — `auto_rgb`
    // =====================================================================================

    // GHIDRA: FUN_8003e86c @ 0x8003E86C (VS.EXE)
    // Opcode 43, `auto_rgb`. GHIDRA HAS NEITHER A FUNCTION NOR A SYMBOL HERE — the dispatch table's
    // word at 0x800823A0 is the only reference, and the decompiler only produces a temporary
    // UndefinedFunction_8003e86c for it. The annotation above uses the raw FUN_ form of the address
    // because that is what the database will carry the moment a function is defined; no name has
    // been invented for it. The C# name is the image's.
    //
    // Four halfwords. Applies three independent operator/operand pairs — packed as three nibbles of
    // streamPtr[1], with bits 4, 9 and 14 of the same halfword marking each operand as a variable
    // index — to the three bytes at +0x150, +0x151 and +0x152 of the same per-entry render state
    // `auto_otz` reaches, clamping each to 0..255. Three bytes in a row is an RGB triple, and the
    // entry index comes from streamPtr[2] & 7 exactly as auto_otz's comes from its header byte & 7.
    internal static int AnimCmd_AutoRgb(int streamPtr)
    {
        ushort uVar1;
        ushort uVar2;
        int iVar3;
        byte uVar4;
        uint uVar5;
        uint uVar6;
        int iVar7;
        ushort uVar8;

        uVar2 = PsxRam.ReadU16(streamPtr + 2);
        uVar5 = (uint)(PsxRam.ReadU16(streamPtr + 4) & 7);
        uVar1 = (ushort)(PsxRam.ReadU16(streamPtr + 4) >> 8);
        uVar6 = (uint)(PsxRam.ReadU16(streamPtr + 6) & 0xff);
        uVar8 = (ushort)(PsxRam.ReadU16(streamPtr + 6) >> 8);
        if (((PsxRam.ReadU16(streamPtr) >> 8) & 1) != 0)
        {
            uVar5 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar5 * 2));
        }
        if ((uVar2 & 0x10) != 0)
        {
            uVar1 = PsxRam.ReadU16(
                AnimVm.g_animSharedVarTable + ((int)((uint)uVar1 << 0x10) >> 0xf));
        }
        if ((uVar2 & 0x200) != 0)
        {
            uVar6 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar6 * 2));
        }
        if ((uVar2 & 0x4000) != 0)
        {
            uVar8 = PsxRam.ReadU16(
                AnimVm.g_animSharedVarTable + ((int)((uint)uVar8 << 0x10) >> 0xf));
        }
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            iVar7 = ((int)(uVar5 << 0x10) >> 0xe) + PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8);
            uVar1 = (ushort)FUN_8003f540(
                PsxRam.ReadU8(PsxRam.ReadI32(iVar7 + 0x18) + 0x150),
                uVar2 & 0xf,
                (uint)(int)(short)uVar1);
            iVar3 = (int)((uint)uVar1 << 0x10);
            if (0xff < (short)uVar1)
            {
                uVar1 = 0xff;
                iVar3 = 0xff0000;
            }
            uVar4 = (byte)uVar1;
            if (iVar3 < 0)
            {
                uVar4 = 0;
            }
            PsxRam.WriteU8(PsxRam.ReadI32(iVar7 + 0x18) + 0x150, uVar4);
            uVar1 = (ushort)FUN_8003f540(
                PsxRam.ReadU8(PsxRam.ReadI32(iVar7 + 0x18) + 0x151),
                ((int)((uint)uVar2 << 0x10) >> 0x15) & 0xf,
                (uint)(int)(short)uVar6);
            iVar3 = (int)((uint)uVar1 << 0x10);
            if (0xff < (short)uVar1)
            {
                uVar1 = 0xff;
                iVar3 = 0xff0000;
            }
            uVar4 = (byte)uVar1;
            if (iVar3 < 0)
            {
                uVar4 = 0;
            }
            PsxRam.WriteU8(PsxRam.ReadI32(iVar7 + 0x18) + 0x151, uVar4);
            uVar2 = (ushort)FUN_8003f540(
                PsxRam.ReadU8(PsxRam.ReadI32(iVar7 + 0x18) + 0x152),
                ((int)((uint)uVar2 << 0x10) >> 0x1a) & 0xf,
                (uint)(int)(short)uVar8);
            iVar3 = (int)((uint)uVar2 << 0x10);
            if (0xff < (short)uVar2)
            {
                uVar2 = 0xff;
                iVar3 = 0xff0000;
            }
            uVar4 = (byte)uVar2;
            if (iVar3 < 0)
            {
                uVar4 = 0;
            }
            PsxRam.WriteU8(PsxRam.ReadI32(iVar7 + 0x18) + 0x152, uVar4);
        }
        return streamPtr + 8;
    }

    // =====================================================================================
    // The VM's generic operator
    // =====================================================================================

    // GHIDRA: FUN_8003f540 @ 0x8003F540 (VS.EXE)
    // THE OPERATOR EVERY `*_set` OPCODE APPLIES. Called from 42 sites across the whole VM, six of
    // them in this file; see the ownership block at the top of the file for why it is transliterated
    // here rather than left to another slice.
    //
    // Thirteen cases on a jump table, result always narrowed back to a signed 16-bit value:
    //   0  set          4  and          9  reverse subtract (operand - value)
    //   1  add          5  xor         10  store value into g_animSharedVarTable[operand]
    //   2  subtract     6  multiply    11  add (operand & rand())
    //   3  or           7  divide      12  modulo
    // Case 8 and everything from 13 up — including the 0xF the `*0123_set` opcodes use to mean
    // "leave this channel alone", and the 0x1F they leave in place when the variable-index bit is
    // also set — fall through to the tail and return the value unchanged. That fall-through is the
    // mechanism, not an accident, and it is why a 0xF operator field is a no-op.
    //
    // Two of the original's guards are not reproduced, and neither is behaviour:
    //   * `if (false) goto switchD_8003f580_caseD_8;` is the decompiler's rendering of the jump
    //     table's default edge and executes nothing;
    //   * `if ((iVar3 == -1) && (sVar1 == -0x80000000))` compares a value that came from a short
    //     against INT_MIN and can never be true.
    // The divide-by-zero guard IS behaviour: the original executes `break 0x1C00`, which traps.
    // C# raises DivideByZeroException on the same input. Rule 12 — the original is not repaired
    // here into returning something.
    internal static int FUN_8003f540(uint param_1, int param_2, uint param_3)
    {
        short sVar1;
        uint uVar2;
        int iVar3;

        sVar1 = (short)param_1;
        switch (param_2)
        {
            case 0:
                param_1 = param_3;
                break;
            case 1:
                param_1 = param_1 + param_3;
                break;
            case 2:
                param_1 = param_1 - param_3;
                break;
            case 3:
                param_1 = param_1 | param_3;
                break;
            case 4:
                param_1 = param_1 & param_3;
                break;
            case 5:
                param_1 = param_1 ^ param_3;
                break;
            case 6:
                iVar3 = (int)(param_1 * param_3 * 0x10000);
                return iVar3 >> 0x10;
            case 7:
                iVar3 = (short)param_3;
                param_1 = (uint)((int)sVar1 / iVar3);
                break;
            case 9:
                param_1 = param_3 - param_1;
                break;
            case 10:
                PsxRam.WriteU16(
                    AnimVm.g_animSharedVarTable + ((int)(param_3 << 0x10) >> 0xf), (ushort)sVar1);
                iVar3 = (int)(param_1 << 0x10);
                return iVar3 >> 0x10;
            case 0xb:
                uVar2 = (uint)rand();
                param_1 = param_1 + (param_3 & uVar2);
                break;
            case 0xc:
                iVar3 = (short)param_3;
                param_1 = (uint)((int)sVar1 % iVar3);
                break;
        }
        iVar3 = (int)(param_1 << 0x10);
        return iVar3 >> 0x10;
    }
}
