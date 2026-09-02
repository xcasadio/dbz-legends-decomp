using PsxSdkMonogame;

namespace DbzLegendsRemaster.VS_EXE;

// The sound opcodes of VS.EXE's animation-stream VM, plus the one handler the image does not name.
//
// The VM is the threaded interpreter ExecuteAnimStreamBatch @ 0x80036768:
//
//     while (uVar1 != 0) {
//       puVar2 = (*(code *)(&g_animStreamDispatchTable)[*puVar2 & 0xff])(puVar2, iVar6 >> 0x10);
//       uVar1 = *puVar2;
//     }
//
// so the opcode is the LOW BYTE of the first halfword, every handler RETURNS THE POINTER TO THE
// NEXT COMMAND, and the stream ends on a zero halfword.
//
// THE IMAGE NAMES ITS OWN OPCODES. g_animStreamDispatchTable @ 0x800822F4 holds 51 handler
// pointers; a table of 16-byte ASCII names sits at 0x800823C0 and holds only 50. Index n's name is
// at 0x800823C0 + n*16 and its handler at 0x800822F4 + n*4. Read out of the image, the five
// entries this file covers are:
//
//   opcode  dispatch slot  handler      name slot     name
//   45      0x800823A8     0x8003EB20   0x80082690    "chse_call"
//   46      0x800823AC     0x8003EC04   0x800826A0    "chse_vol"
//   47      0x800823B0     0x8003ED84   0x800826B0    "voice_call"
//   48      0x800823B4     0x8003F044   0x800826C0    "atse_call"
//   50      0x800823BC     0x8003EF04   -             (none: the table stops at index 49)
//
// Index 49 is 0x8003BF2C / "base_culP", index 44 is "cheff_wait"; neither is in this slice. Index
// 50 is the 51st and last handler pointer and it is past the end of the name table, which is why
// 0x8003EF04 has no name. IT IS NOT NAMED BY NEIGHBOURHOOD HERE. What it does is described below
// and its C# name stays the raw address.
//
// NONE OF THE FIVE IS A GHIDRA FUNCTION. Ghidra has not promoted any of these five addresses; each
// is reached only by the DATA reference from its dispatch slot, and the decompilation comes back
// as an undefined-function preview named UndefinedFunction_<addr>. That is what the `GHIDRA:` lines
// below spell, because that is what the project database actually carries. The C# names come from
// the image's own name table, which is evidence, not decoration. Neither is a rename in Ghidra:
// nothing in this slice writes to the Ghidra database.
//
// HANDLER SIGNATURE. The interpreter calls with two arguments, `(puVar2, iVar6 >> 0x10)`, but all
// five of these handlers read only the first: the decompiler prints `ushort *f(ushort *param_1)`
// for each, and the disassembly of 0x8003EB20 confirms a1 is written (`sra a1,v0,0x10`) before it
// is ever read. The mesh index in the second argument is simply not used by the sound opcodes. The
// ported signature is therefore one parameter, matching both Ghidra and the one handler in this
// family Ghidra HAS named, AnimCmd_ChEffSet @ 0x8003DCBC (`ushort *AnimCmd_ChEffSet(ushort *)`).
//
// THE STREAM IS PSX MEMORY, NOT A COPY. streamPtr is the raw PSX address the interpreter is
// standing on, halfwords are read through PsxRam, and the return value is an ADDRESS the
// interpreter re-reads. No ushort[] stands in for the stream.
//
// ============================================================================================
// NO SOUND WILL COME OUT OF THIS FILE, AND THE REASON IS NOT THE SDK STUBS
// ============================================================================================
//
// These four opcodes never touch libsnd or libspu directly. Every one of them calls into VS.EXE's
// OWN sound driver front-end, a module occupying 0x8005EE5C..0x800602DB, and that module is not
// transliterated. Its entry points, and what each is:
//
//   0x8005FB9C  FUN_8005fb9c(uint,ushort,short)   chse_call's target. Allocates a voice out of the
//                                                 22-slot bank (DAT_8008d214 walking 0x11..0x16),
//                                                 then FUN_8006b4a0 / FUN_8006bdd8 against the VAB
//                                                 id at DAT_8008d284 + 0x158.
//   0x8005FCEC  FUN_8005fcec(uint,short)          chse_vol's target. Sets volume on one voice, or
//                                                 on all six of 0x11..0x16 when the channel is 0.
//   0x8005FD9C  FUN_8005fd9c(uint,ushort,short)   atse_call's target. Same shape as FUN_8005fb9c
//                                                 but against the OTHER VAB, DAT_8008d284 + 0x154,
//                                                 and with a per-sound pitch/ADSR triple read out
//                                                 of DAT_80084C10.
//   0x8005FF5C  FUN_8005ff5c(short,uint,ushort)   voice_call's target. Streams an ADPCM voice clip:
//                                                 SpuSetVoiceAttr on &DAT_800B0DDC pointed at the
//                                                 buffer &DAT_801C1000, then DAT_8008d384 = 1.
//   0x80060120  FUN_80060120(void)                returns DAT_8008d384.
//   0x8006012C  FUN_8006012c(void)                DAT_8008d384 |= 0x40.
//   0x80060144  FUN_80060144(uint)                0x8003EF04's target. A three-state machine over
//                                                 DAT_8008d284 + 0x15C that walks a per-character
//                                                 voice-line table at DAT_80084AD4 (3 halfwords per
//                                                 character) and feeds FUN_8005ff5c.
//
// Porting that module is a slice of its own: it is called from at least eight sites outside the
// animation VM (FUN_800264d8, FUN_8002a038, FUN_8002b5c8, FUN_8002ccb0, FUN_8002f0f8, FUN_800578e0,
// FUN_80035030, FUN_800356dc), and it drags in the DAT_8008D284 configuration block, the voice
// allocator FUN_8006b4a0 / FUN_8006b88c / FUN_8006bdd8, four data tables and SpuSetVoiceAttr.
//
// So five of those seven stand below as BLOCKED stubs. The stream is walked faithfully, every
// command is consumed with the right width, every branch is taken as the original takes it — and
// nothing is audible. That is the assumed state of this port, not a defect being papered over.
//
// THE TWO OBSERVABLE DIVERGENCES THIS CAUSES, stated rather than hidden:
//   * voice_call's 0x40 and 0xC0 sub-opcodes branch on FUN_80060120's result, i.e. on DAT_8008d384.
//     FUN_80060120 and FUN_8006012c ARE transliterated below (see the ownership caveat), so the
//     0x80 sub-opcode really does set bit 6 and the 0xC0 wait really does see it. But the two
//     writers that would set DAT_8008d384 to 1 and to 0x49 are the blocked FUN_8005ff5c and
//     FUN_80060144, so on this port the word never reaches 9 and the 0x40 wait never completes its
//     store into g_animSharedVarTable. On the console it does.
//   * 0x8003EF04's 0x80 sub-opcode branches on FUN_80060144's result, which is blocked and returns
//     0, so its store into g_animSharedVarTable never happens either. On the console the store
//     happens when the driver reports 1.
// Both are the blocked driver showing through, and both disappear when it is transliterated.
//
// OWNERSHIP CAVEAT, the same kind FileIo.cs records for the GTE scratchpad. FUN_80060120 (12 bytes)
// and FUN_8006012c (24 bytes) and the global they share, DAT_8008d384, belong to the sound driver
// module, not to the animation VM. They are transliterated HERE because this slice is the first
// code to need them and because they are complete and closed — 12 and 24 bytes, three instructions
// and five, no callees. Stubbing FUN_80060120 to `return 0` instead would have INVENTED the value
// voice_call branches on, which is worse than misplacing three lines. When VS_EXE/SoundDriver.cs
// exists, these three move there as they are.
internal static class AnimCmdSound
{
    // g_animSharedVarTable and AnimVm.DAT_800b305a are the VM's SHARED globals; they are declared once in
    // AnimVm.cs and reached here as AnimVm.<name>, by address through PsxRam rather than as a
    // managed array. See AnimVm.cs for the merged proof comments — the extent this file had closed
    // at sixteen halfwords (0x801FAA64..0x801FAA83, from every index here being `(x & 0xf) * 2` and
    // Ghidra's separate DAT_801faa84 label) is folded into AnimVm's comment.

    // GHIDRA: DAT_801fac40 @ 0x801FAC40 (VS.EXE)
    // Signed slope of the running channel-volume ramp; 0 means no ramp is active. Written by
    // chse_vol as a 5-bit sign-extended field, cleared by chse_call and atse_call, and stepped by
    // FUN_8003ecfc @ 0x8003ECFC, which ExecuteAnimStreamBatch calls once per frame at 0x80036970.
    // FUN_8003ecfc reads it signed (`if (iVar2 < 0)`), hence sbyte.
    internal static sbyte DAT_801fac40;

    // GHIDRA: DAT_801fac41 @ 0x801FAC41 (VS.EXE)
    // The channel the ramp is running on, 0..7. Read unsigned — the instruction at 0x8003EBB4 is
    // `lbu v1,-0x53BF(v1)` against the 0x80200000 base, i.e. a plain byte load of this address.
    internal static byte DAT_801fac41;

    // GHIDRA: DAT_801fac42 @ 0x801FAC42 (VS.EXE)
    // The ramp's current volume. FUN_8003ecfc adds the slope to it every frame.
    internal static byte DAT_801fac42;

    // GHIDRA: DAT_801fac43 @ 0x801FAC43 (VS.EXE)
    // The ramp's target volume. FUN_8003ecfc stops the ramp when the current value passes it.
    internal static byte DAT_801fac43;

    // GHIDRA: DAT_8008d384 @ 0x8008D384 (VS.EXE)
    // The voice driver's state word. `lh v0,0x288(gp)` at 0x80060120 loads it SIGNED, `lhu` at
    // 0x8006012C loads it unsigned; a short models both. FUN_8005ff5c sets it to 1, FUN_8006012c
    // ORs 0x40 into it, FUN_80060144 sets it to 0x49; the last two of those three are blocked here.
    //
    // OWNERSHIP: sound-driver state, declared here for the reason given in the file header.
    internal static short DAT_8008d384;

    // GHIDRA: UndefinedFunction_8003eb20 @ 0x8003EB20 (VS.EXE)
    // Opcode 45, which the image's name table calls `chse_call` (0x80082690). Ghidra has NOT
    // promoted this address to a function; the name above is what its decompilation preview
    // carries, and the only reference to it is the DATA reference from dispatch slot 0x800823A8.
    //
    // Two halfwords. h0: low byte is the opcode, high byte is the channel operand. h1: low byte is
    // the sound index, high byte is the volume operand. Either operand byte with bit 7 set is an
    // indirection into AnimVm.g_animSharedVarTable[byte & 0xf] instead of a literal.
    internal static int AnimCmd_ChseCall(int streamPtr)
    {
        ushort uVar1;
        uint uVar2;
        ushort uVar3;
        ushort uVar4;

        ushort h0 = PsxRam.ReadU16(streamPtr);
        uVar2 = (uint)((int)((uint)h0 << 0x10) >> 0x18);
        uVar3 = (ushort)(h0 >> 8);
        if ((uVar2 & 0x80) != 0)
        {
            uVar3 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar2 & 0xf) * 2);
        }

        uVar1 = PsxRam.ReadU16(streamPtr + 2);
        uVar2 = (uint)((int)((uint)uVar1 << 0x10) >> 0x18);
        uVar4 = (ushort)(uVar1 >> 8);
        if ((uVar2 & 0x80) != 0)
        {
            uVar4 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar2 & 0xf) * 2);
        }

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            // The original compares `(int)(short)uVar3 == (uint)DAT_801fac41`, an int against an
            // unsigned, so C converts the int to unsigned before comparing. The unsigned cast below
            // reproduces that conversion literally; the outcome is the same either way, because
            // DAT_801fac41 is 0..255 and a negative channel can never match it.
            if ((uint)(int)(short)uVar3 == (uint)DAT_801fac41)
            {
                DAT_801fac40 = 0;
                DAT_801fac41 = 0;
                DAT_801fac42 = 0;
                DAT_801fac43 = 0;
            }

            FUN_8005fb9c(uVar1 & 0xffu, (ushort)(short)uVar3, (short)uVar4);
        }

        return streamPtr + 4;
    }

    // GHIDRA: UndefinedFunction_8003ec04 @ 0x8003EC04 (VS.EXE)
    // Opcode 46, which the image's name table calls `chse_vol` (0x800826A0). Ghidra has not
    // promoted this address either; the only reference is the DATA reference from slot 0x800823AC.
    //
    // Arms the volume ramp FUN_8003ecfc steps once per frame. h0's top 5 bits are the slope, sign
    // extended by the `^ 0xffe0` below; h0's high byte masked to 3 bits is the channel; h1's low
    // byte is the starting volume and h1's high byte the target, each optionally indirected through
    // g_animSharedVarTable. A slope of 0 means "no ramp", and only then does it push the volume
    // straight to the driver.
    //
    // NOTE THE ASYMMETRY, which is the original's and is kept: the starting-volume indirection
    // tests bit 7 of h1's LOW byte and indexes with h1's LOW nibble, while the target-volume
    // indirection tests the sign-extended HIGH byte and indexes with that byte's low nibble.
    internal static int AnimCmd_ChseVol(int streamPtr)
    {
        ushort uVar1;
        uint uVar2;
        ushort uVar3;
        uint uVar4;
        ushort uVar5;

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            ushort h0 = PsxRam.ReadU16(streamPtr);
            uVar5 = (ushort)(h0 >> 0xb);
            if ((uVar5 & 0x10) != 0)
            {
                uVar5 = (ushort)(uVar5 ^ 0xffe0);
            }

            uVar1 = PsxRam.ReadU16(streamPtr + 2);
            uVar2 = (uint)((int)((uint)h0 << 0x10) >> 0x18) & 7;
            uVar3 = (ushort)(uVar1 & 0xff);
            if ((uVar1 & 0x80) != 0)
            {
                uVar3 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar1 & 0xf) * 2);
            }

            uVar4 = (uint)((int)((uint)uVar1 << 0x10) >> 0x18);
            DAT_801fac43 = (byte)(uVar1 >> 8);
            if ((uVar4 & 0x80) != 0)
            {
                DAT_801fac43 = (byte)PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar4 & 0xf) * 2);
            }

            DAT_801fac40 = (sbyte)uVar5;
            DAT_801fac41 = (byte)uVar2;
            DAT_801fac42 = (byte)uVar3;
            if (uVar5 == 0)
            {
                FUN_8005fcec(uVar2, (short)uVar3);
            }
        }

        return streamPtr + 4;
    }

    // GHIDRA: UndefinedFunction_8003ed84 @ 0x8003ED84 (VS.EXE)
    // Opcode 47, which the image's name table calls `voice_call` (0x800826B0). Ghidra has not
    // promoted this address; the only reference is the DATA reference from slot 0x800823B0.
    //
    // FOUR SUB-OPCODES, selected by bits 6-7 of h0's high byte, and they do not all consume the
    // same number of halfwords. That is the original's shape and it is preserved exactly:
    //   0x00  start the clip whose id is h1 & 0x7fff        consumes 2 halfwords
    //   0x40  wait until the driver state reads 9, then OR h1 into AnimVm.g_animSharedVarTable[n & 0xf]
    //                                                       consumes 2 halfwords
    //   0x80  set bit 6 of the driver state                 consumes 1 halfword
    //   0xC0  wait until the driver state reads 0, then the same OR
    //                                                       consumes 2 halfwords
    // A wait that is not satisfied still consumes 2 halfwords and simply skips the OR, so the
    // stream does not stall on it — the retry comes from the VM re-running the stream next frame.
    //
    // When AnimVm.DAT_800b305a bit 0 is set, 0x00 / 0x40 / 0xC0 consume 2 halfwords and do nothing while
    // 0x80 consumes 1. The original's early returns say so and are transcribed one for one.
    internal static int AnimCmd_VoiceCall(int streamPtr)
    {
        ushort uVar1;
        int iVar2;
        uint uVar3;
        int puVar4;
        uint uVar5;

        uVar5 = (uint)((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18);
        uVar3 = uVar5 & 0xc0;
        puVar4 = streamPtr + 2;
        if (uVar3 == 0x40)
        {
            uVar1 = PsxRam.ReadU16(puVar4);
            if ((AnimVm.DAT_800b305a & 1) != 0)
            {
                goto LAB_8003eee4;
            }

            iVar2 = FUN_80060120();
            if (iVar2 != 9)
            {
                return streamPtr + 4;
            }
        }
        else
        {
            if (uVar3 < 0x41)
            {
                if (uVar3 != 0)
                {
                    return puVar4;
                }

                if ((AnimVm.DAT_800b305a & 1) == 0)
                {
                    FUN_8005ff5c(0, 0, (ushort)(PsxRam.ReadU16(puVar4) & 0x7fff));
                    return streamPtr + 4;
                }

                goto LAB_8003eee4;
            }

            if (uVar3 == 0x80)
            {
                if ((AnimVm.DAT_800b305a & 1) != 0)
                {
                    return puVar4;
                }

                FUN_8006012c();
                return puVar4;
            }

            if (uVar3 != 0xc0)
            {
                return puVar4;
            }

            uVar1 = PsxRam.ReadU16(puVar4);
            if ((AnimVm.DAT_800b305a & 1) != 0)
            {
                goto LAB_8003eee4;
            }

            iVar2 = FUN_80060120();
            if (iVar2 != 0)
            {
                return streamPtr + 4;
            }
        }

        PsxRam.WriteU16(AnimVm.g_animSharedVarTable + (int)(uVar5 & 0xf) * 2, (ushort)(uVar1 | PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar5 & 0xf) * 2)));

    LAB_8003eee4:
        return streamPtr + 4;
    }

    // GHIDRA: UndefinedFunction_8003f044 @ 0x8003F044 (VS.EXE)
    // Opcode 48, which the image's name table calls `atse_call` (0x800826C0). Ghidra has not
    // promoted this address; the only reference is the DATA reference from slot 0x800823B4.
    //
    // Byte for byte the same body as AnimCmd_ChseCall above — same operand decode, same clearing of
    // the ramp quad, same widths — with one difference: it calls FUN_8005fd9c instead of
    // FUN_8005fb9c, and those two differ in which VAB they play out of (DAT_8008D284 + 0x154 rather
    // than + 0x158) and in that FUN_8005fd9c also reads a per-sound pitch/ADSR triple out of
    // DAT_80084C10. The duplication is the original's; the two are NOT merged here.
    internal static int AnimCmd_AtseCall(int streamPtr)
    {
        ushort uVar1;
        uint uVar2;
        ushort uVar3;
        ushort uVar4;

        ushort h0 = PsxRam.ReadU16(streamPtr);
        uVar2 = (uint)((int)((uint)h0 << 0x10) >> 0x18);
        uVar3 = (ushort)(h0 >> 8);
        if ((uVar2 & 0x80) != 0)
        {
            uVar3 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar2 & 0xf) * 2);
        }

        uVar1 = PsxRam.ReadU16(streamPtr + 2);
        uVar2 = (uint)((int)((uint)uVar1 << 0x10) >> 0x18);
        uVar4 = (ushort)(uVar1 >> 8);
        if ((uVar2 & 0x80) != 0)
        {
            uVar4 = PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar2 & 0xf) * 2);
        }

        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            if ((uint)(int)(short)uVar3 == (uint)DAT_801fac41)
            {
                DAT_801fac40 = 0;
                DAT_801fac41 = 0;
                DAT_801fac42 = 0;
                DAT_801fac43 = 0;
            }

            FUN_8005fd9c(uVar1 & 0xffu, (ushort)(short)uVar3, (short)uVar4);
        }

        return streamPtr + 4;
    }

    // GHIDRA: UndefinedFunction_8003ef04 @ 0x8003EF04 (VS.EXE)
    // Opcode 50 — dispatch slot 0x800823BC, the 51st and last handler pointer, and THE ONLY ONE OF
    // THE 51 WITH NO ENTRY IN THE IMAGE'S NAME TABLE, which stops at index 49. Ghidra has not
    // promoted this address either; the only reference to it is that DATA reference.
    //
    // NO NAME IS GIVEN TO IT HERE. Sitting between voice_call (0x8003ED84) and atse_call
    // (0x8003F044) in address order is a neighbourhood argument, not evidence, and it is not used.
    // The C# name is the raw address.
    //
    // WHAT IT DOES, which IS closed from the body: it drives FUN_80060144 @ 0x80060144, the same
    // voice-line state machine that ends up calling voice_call's FUN_8005ff5c. Its argument on two
    // of the three paths is
    //     *(ushort *)**(undefined4 **)(DAT_8008d16c + 8) & 0x7f
    // that is: the current task node's context pointer at +0x08, dereferenced once more, and the
    // first halfword there masked to 7 bits. FUN_80060144 then indexes DAT_80084AD4 at that value
    // times 6 — a table of three halfwords per entry. So the operand is a 0..127 id taken from the
    // running actor rather than from the stream. Three sub-opcodes, again on bits 6-7 of h0's high
    // byte:
    //   0x00  fire, with bit 15 of the argument clear      consumes 1 halfword
    //   0x40  fire, with bit 15 of the argument set        consumes 1 halfword
    //   0x80  poll; on a result of 1, OR h1 into AnimVm.g_animSharedVarTable[n & 0xf]
    //                                                      consumes 2 halfwords
    // and a fourth encoding, 0xC0, which falls out through `if (uVar3 != 0x80) return puVar4;` and
    // consumes 1 halfword doing nothing.
    //
    // BLOCKED: what bit 15 of the argument selects inside FUN_80060144 is only half legible — it
    // reaches `if ((param_1 >> 0xe & 2) == 0)`, choosing the third halfword of the character's
    // table entry instead of the alternating first/second pair — and the meaning of those three
    // halfwords is not closed by this slice. The control flow is transcribed anyway; nothing about
    // it is guessed.
    //
    // PARTIAL: with AnimVm.DAT_800b305a bit 0 set, the original runs the `else if` and consumes 1 halfword
    // for everything except 0x80, which consumes 2 without polling. Transcribed as written.
    internal static int FUN_8003ef04(int streamPtr)
    {
        ushort uVar1;
        int iVar2;
        uint uVar3;
        int puVar4;
        uint uVar5;

        uVar5 = (uint)((int)((uint)PsxRam.ReadU16(streamPtr) << 0x10) >> 0x18);
        puVar4 = streamPtr + 2;
        if ((AnimVm.DAT_800b305a & 1) == 0)
        {
            uVar3 = uVar5 & 0xc0;
            if (uVar3 == 0x40)
            {
                FUN_80060144((uint)(PsxRam.ReadU16(
                    PsxRam.ReadI32(PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8))) & 0x7f | 0x8000));
                return puVar4;
            }

            if (uVar3 < 0x41)
            {
                if (uVar3 != 0)
                {
                    return puVar4;
                }

                FUN_80060144((uint)(PsxRam.ReadU16(
                    PsxRam.ReadI32(PsxRam.ReadI32(TaskSystem.g_CurrentTask + 8))) & 0x7f));
                return puVar4;
            }

            if (uVar3 != 0x80)
            {
                return puVar4;
            }

            uVar1 = PsxRam.ReadU16(puVar4);
            iVar2 = FUN_80060144(0);
            if (iVar2 != 1)
            {
                return streamPtr + 4;
            }

            PsxRam.WriteU16(AnimVm.g_animSharedVarTable + (int)(uVar5 & 0xf) * 2, (ushort)(uVar1 | PsxRam.ReadU16(AnimVm.g_animSharedVarTable + (int)(uVar5 & 0xf) * 2)));
        }
        else if ((uVar5 & 0xc0) != 0x80)
        {
            return puVar4;
        }

        return streamPtr + 4;
    }

    // GHIDRA: FUN_80060120 @ 0x80060120 (VS.EXE)
    // `lh v0,0x288(gp)` / `jr ra`. Three instructions, 12 bytes, five call sites. Transliterated
    // here rather than stubbed because voice_call BRANCHES on its result: a stub returning 0 would
    // have been an invented value, not a blocked one. See the ownership caveat in the file header.
    internal static int FUN_80060120()
    {
        return DAT_8008d384;
    }

    // GHIDRA: FUN_8006012c @ 0x8006012C (VS.EXE)
    // `lhu` / `ori 0x40` / `sh` / `jr ra` / `addu v0,zero,zero`. 24 bytes, one call site — the 0x80
    // sub-opcode of voice_call above. Same reason for porting it here as FUN_80060120.
    internal static int FUN_8006012c()
    {
        DAT_8008d384 = (short)(DAT_8008d384 | 0x40);
        return 0;
    }

    // GHIDRA: FUN_8005fb9c @ 0x8005FB9C (VS.EXE)
    // BLOCKED: 336 bytes of the sound driver module, not of the animation VM. It walks the six-slot
    // voice bank at DAT_8008d214 (0x11..0x16), keys off through FUN_8006b88c when the sound index
    // is 0, and otherwise keys on through FUN_8006b4a0 against the VAB id at DAT_8008d284 + 0x158
    // and sets the volume through FUN_8006bdd8. Porting it means porting the whole module — see the
    // file header for why that is a separate slice.
    // The original returns 0, or -1 when the VAB id is negative; the one caller in this file
    // discards the result, so the 0 below is not load-bearing.
    private static int FUN_8005fb9c(uint param_1, ushort param_2, short param_3)
    {
        return 0;
    }

    // GHIDRA: FUN_8005fcec @ 0x8005FCEC (VS.EXE)
    // BLOCKED: 176 bytes of the same module. Sets the volume of one voice through FUN_8006bdd8, or
    // of all six of 0x11..0x16 when the channel is 0. Four call sites, only one of which is in this
    // file; ExecuteAnimStreamBatch and FUN_8003ecfc are two of the others. Its result is discarded
    // at every one of them.
    private static int FUN_8005fcec(uint param_1, short param_2)
    {
        return 0;
    }

    // GHIDRA: FUN_8005fd9c @ 0x8005FD9C (VS.EXE)
    // BLOCKED: 448 bytes of the same module, and the atse counterpart of FUN_8005fb9c — the other
    // VAB (DAT_8008d284 + 0x154), plus a per-sound triple read out of DAT_80084C10 and a single
    // retry of FUN_8006b4a0 when the first key-on returns -1. Six call sites, five of them outside
    // the animation VM. Result discarded at the call site in this file.
    private static int FUN_8005fd9c(uint param_1, ushort param_2, short param_3)
    {
        return 0;
    }

    // GHIDRA: FUN_8005ff5c @ 0x8005FF5C (VS.EXE)
    // BLOCKED: 340 bytes of the same module, and the one that actually starts an ADPCM voice clip —
    // it fills the SpuVoiceAttr at &DAT_800B0DDC, points it at the clip buffer &DAT_801C1000, calls
    // SpuSetVoiceAttr, and sets DAT_8008d384 to 1. Two call sites: voice_call's 0x00 sub-opcode
    // here, and FUN_80060144. Because it is blocked, DAT_8008d384 never takes the value 1 in this
    // port, which is the first of the two divergences listed in the file header. Result discarded
    // at the call site in this file.
    private static int FUN_8005ff5c(short param_1, uint param_2, ushort param_3)
    {
        return 0;
    }

    // GHIDRA: FUN_80060144 @ 0x80060144 (VS.EXE)
    // BLOCKED: 408 bytes of the same module — the voice-line state machine over DAT_8008d284 +
    // 0x15C described in the FUN_8003ef04 comment above. All three of its call sites are in
    // FUN_8003ef04.
    // ITS RESULT IS LOAD-BEARING: FUN_8003ef04's 0x80 sub-opcode stores into g_animSharedVarTable
    // only when it returns 1. The 0 below is the blocked stub's value and NOT the original's
    // behaviour; it is the second divergence listed in the file header. It is 0 rather than 1
    // because 0 is what every one of the original's own early returns yields, and 1 is reached only
    // through state this port does not maintain.
    private static int FUN_80060144(uint param_1)
    {
        return 0;
    }
}
