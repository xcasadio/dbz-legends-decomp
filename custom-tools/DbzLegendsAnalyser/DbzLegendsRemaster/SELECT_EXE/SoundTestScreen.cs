using PsxSdkMonogame;
using static PsxSdkMonogame.LibGpu;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE SOUND TEST — RunSoundTestScreen @ 0x80026420 (1788 bytes) and the menu driver it runs,
// RunSoundTestMenu @ 0x80026B1C (3004 bytes).
//
// HOW IT IS REACHED, read off the one incoming reference Ghidra reports for 0x80026420:
// RunOptionsScreen @ 0x800315C0 calls it at 0x80031A08 under
//     if ((g_OptionsCursor == 0) && ((g_OptionsRecord64 & 4) != 0)) RunSoundTestScreen();
// — the options screen's cursor parked on row 0 (音楽, the music row) AND bit 2 of the shared
// options word at 0x801FF018. It is the only call site in the program.
//
// NO SOUND WILL COME OUT OF THIS PORT TODAY, and that is the expected state, not a defect being
// papered over. Mandate rule 13 says the PSX SDK is not transliterated as game runtime: these two
// functions are game code and are transliterated here, but everything they reach for audio —
// InitializeSoundSystem @ 0x80022994 and its 36 callees (SpuInit, SpuStInit, SpuSetVoiceAttr,
// SpuMallocWithStartAddr, SsSetReservedVoice, SsUtGetVBaddrInSB, SsVabOpenHeadSticky,
// SsVabTransBody, the three SpuSt* callback registrations and six CdSearchFile/CdRead/CdReadSync
// groups for the \SOUND\*.B VABs), ShutdownSoundSystem @ 0x80025F2C and its twelve, UpdateSound
// @ 0x800231AC, and libspu's SpuRGetAllKeysStatus — lives in PsxSdkMonogame/LibSnd.cs and
// LibSpu.cs, where essentially every body is `// Do nothing PSX SDK`. Those stubs are CALLED, not
// reimplemented. So the control flow below is faithful and the SPU stays silent.
//
// THE OBSERVABLE CONSEQUENCE of the libspu stub, stated rather than hidden: LibSpu's
// SpuRGetAllKeysStatus does nothing and never writes the status buffer, so the three key-on scans
// in RunSoundTestMenu always count zero. Sprites 0x41..0x44 (the four "voices busy" lamps) will
// therefore sit on their idle frame and DAT_80055b00 will only ever count down.
//
// SPRITE MAP — ARITHMETIC, NOT GUESSES. Ghidra spells the build loops as byte offsets from
// whichever .bss symbol precedes the field, so every one of them was resolved back to
// element = (address + cursor - 0x800654EC) / 36 and field = the same expression mod 36. The
// five bases that appear, and what they turn out to be at the loop's own first cursor value
// iVar18 = 0xBF4:
//     iVar18 - 0x7FF9AE98 .. -0x7FF9AE80   -> element 0x3C + i, fields +0x00/04/06/08/0A/0E/0F/14/18
//     iVar18 - 0x7FF9ADE4 .. -0x7FF9ADCC   -> element 0x41 + i, fields +0x00/04/08/0A/0E/0F/18
//     iVar18 - 0x7FF9AD30 .. -0x7FF9AD18   -> element 0x46 + i, fields +0x00/04/06/08/0A/0E/0F/18
//     &g_OrderingTableTags1[5..7].p + iVar18, &DAT_8006539c + iVar18
//                                          -> element 0x4B + i, fields +0x00/04/06/08/0A/0E/0F/18
//     &GsSPRITE_ARRAY_800654ec[0].<field> + iVar18
//                                          -> element 0x55 + i, fields +0x00/04/06/08/0A/0C/0E/0F/10/12/18
// The four ordering-table constants close the map on their own: [5].p, [6].p, [6].p+2, [7].p and
// [7].p+2 land on element 0x4B's +0x00, +0x04, +0x06, +0x08 and +0x0A — attribute, x, y, w, h in
// field order. That is not a coincidence a wrong base could produce.
// Two more cursors run alongside iVar18 and stay a fixed distance below it for the whole loop:
// iVar19 = iVar18 - 0x384 (element 0x3C + i, used for the .b/.g writes) and iVar14 = iVar18 -
// 0x2D0 (element 0x41 + i, used for the .y write). Both start at that distance and both advance
// by the same 0x24, so the identity holds on every iteration.
//
// SO THE SCREEN IS FIVE ROWS OF FIVE SPRITES: a label (0x3C..0x40), a lamp (0x41..0x45), a second
// lamp (0x46..0x4A), a marker (0x4B..0x4F) and a wide 0xD0 x 0x18 value strip (0x55..0x59), each
// row 0x20 pixels below the last. Rows 0..3 are CD-DA, BGM, SE and a fourth bank; row 4 is the
// exit. The maxima are hard-coded in the driver as { 0xF, 0x12, 0x15, 0x66A, 0 }.
internal static class SoundTestScreen
{
    // GHIDRA: DAT_80055a00 @ 0x80055A00
    // THE SOUND-TEST ROW CURSOR, 0..4 — param_1 of RunSoundTestMenu. .sdata, image value 0
    // (get-data: hexBytes 00 00 00 00). find-cross-references reports EXACTLY TWO references in
    // the whole program and both are inside RunSoundTestScreen: the read that brightens element
    // DAT_80055a00 + 0x3C, and the address-of that becomes param_1. Nothing else touches it, so
    // the value survives across visits to this screen.
    internal static int DAT_80055a00;

    // GHIDRA: DAT_80055a04 @ 0x80055A04
    // .sdata, image value 0x01200000 (get-data hexBytes 00 00 20 01).
    private static readonly uint DAT_80055a04 = 0x01200000;

    // GHIDRA: DAT_80055a08 @ 0x80055A08
    // .sdata, image value 0x00200006 (get-data hexBytes 06 00 20 00).
    // The two words are one eight-byte LibGpu.RECT the driver copies onto its stack with an
    // swl/swr pair and then reuses for all eleven MoveImage calls: x = 0x0000, y = 0x0120,
    // w = 0x0006, h = 0x0020 — a six-by-thirty-two digit cell. Only x is ever rewritten.
    private static readonly uint DAT_80055a08 = 0x00200006;

    // GHIDRA: DAT_80055aa0 @ 0x80055AA0
    // The base of the per-row flag byte the build loop reads:
    //     cVar1 = *(char *)((int)&DAT_80055aa0 + iVar24 + 3)
    // with iVar24 running 0x55..0x59, so the five bytes actually touched are
    // 0x80055AF8..0x80055AFC. Ghidra labels the first of them DAT_80055af8, and
    // find-cross-references on it reports EXACTLY ONE reference in the whole program — this read,
    // at 0x800266A0. There is no writer anywhere in SELECT.EXE and the address is inside .sbss
    // (which starts at 0x80055A78 and start's zero loop clears), so cVar1 is 0 on every one of
    // the five iterations and `-(cVar1 == '\0') & 0x38` below always yields 0x38.
    // The read is transliterated anyway — rule 12 — rather than folded to its constant.
    // NOTE: CdAudio.cs models three of its own .sbss words inside this same PSX span (0x80055AB8,
    // 0x80055AC0, 0x80055AE0) as plain C# statics, so nothing in the port aliases this array. It
    // exists only to carry the five bytes this one expression indexes, and is never written.
    private static readonly byte[] DAT_80055aa0 = new byte[0x5D];

    // GHIDRA: DAT_80055b00 @ 0x80055B00
    // THE KEY-ON HOLD COUNTER, clamped to 0..0x30 by the driver's own two guards. .sbss.
    // find-cross-references reports SIX references and all six are in these two functions:
    // RunSoundTestScreen zeroes it before entering the driver (0x800269F8) and the driver writes
    // it at 0x80026F74 / 0x800270AC / 0x800271D4 and reads it at 0x80027190 / 0x800271B4.
    // It drives sprite 0x42's frame: at 0 the lamp shows u = 0x38, at 0x30 it shows u = 0.
    internal static int DAT_80055b00;

    // GHIDRA: DAT_80055b38 @ 0x80055B38
    // THE SOUND MODULE'S STATE FLAG WORD. .sbss. RunSoundTestMenu only READS it, once, as
    // `(DAT_80055b38 & 0x3a) == 0` gating the CD-DA row's play at 0x80026FAC.
    // find-cross-references reports 28 references; every writer is inside the unported sound/CD
    // module — StopCdAudio @ 0x80025894, FUN_800258f0 @ 0x800258F0, PlayCdCurrentTrack
    // @ 0x80025D04, FUN_8002494c @ 0x8002494C, UpdateSound @ 0x800231AC, FUN_80025248
    // @ 0x80025248, StepBgmState @ 0x80023640, ShutdownSoundSystem @ 0x80025F2C and FUN_800253a4
    // @ 0x800253A4.
    // PARTIAL: what the bits in mask 0x3A mean is NOT closed. The word is declared here because
    // this file is currently the only ported reader; it belongs to the sound module when that
    // lands. With every writer stubbed it stays 0, so the CD-DA play branch is taken.
    // The explicit 0 is the value start's .bss clear leaves — this port has no ported writer, and
    // spelling it out says so rather than leaving the reader to wonder.
    internal static int DAT_80055b38 = 0;

    // GHIDRA: DAT_80055dbc @ 0x80055DBC
    // THE FIVE SOUND-TEST VALUES — param_2 of RunSoundTestMenu, one per row. .bss (at or above
    // 0x80055B88), so start's zero loop leaves all five at 0.
    // EXTENT: five ints. The driver indexes it only with *param_1, and *param_1's own two wrap
    // branches keep it inside [0, 4] (`4 < iVar6 + 1 -> 0` and `iVar6 - 1 < 0 -> 4`); the display
    // block reads indices 0, 1, 2 and 3 by name. Nothing in the program reaches further.
    internal static readonly int[] DAT_80055dbc = new int[5];

    // JUSTIFICATION: C# language bridge only
    // RELATION: the original copies whole 36-byte GsSPRITEs — the save/restore pass in
    // RunSoundTestScreen does it as `{ 4 words; 4 words; 1 word }` per element, and the two
    // 0x3B <-> 0x5A stashes do it as `{ 16 bytes; 16 bytes; the rotate word }` through a pointer
    // the compiler re-types each pass. Both are 36-byte struct copies. LibGs.GsSPRITE is a CLASS
    // in this port, so `a = b` would ALIAS where the original copies; the copy has to be spelled
    // field by field. All twelve fields, in layout order.
    // The one byte the original moves that this cannot is the pad at +0x17, between b and mx.
    // Nothing in SELECT.EXE reads it and LibGs.GsSPRITE has no field for it.
    private static void CopyGsSprite(LibGs.GsSPRITE dst, LibGs.GsSPRITE src)
    {
        dst.attribute = src.attribute;
        dst.x = src.x;
        dst.y = src.y;
        dst.w = src.w;
        dst.h = src.h;
        dst.tpage = src.tpage;
        dst.u = src.u;
        dst.v = src.v;
        dst.cx = src.cx;
        dst.cy = src.cy;
        dst.r = src.r;
        dst.g = src.g;
        dst.b = src.b;
        dst.mx = src.mx;
        dst.my = src.my;
        dst.scalex = src.scalex;
        dst.scaley = src.scaley;
        dst.rotate = src.rotate;
    }

    // GHIDRA: RunSoundTestScreen @ 0x80026420
    // 1788 bytes, ten callees, one caller.
    //
    // Shape, in the original's order:
    //   1. stash elements 0x3C..0x5A (thirty-one sprites, 36 bytes each) in a 1120-byte stack
    //      buffer — `ulong local_478 [280]`, and 31 * 9 words = 279 of those 280;
    //   2. stash element 0x3B inside element 0x5A (which was itself just stashed in step 1) and
    //      blank 0x3B with attribute bit 31;
    //   3. re-initialise elements 0x1E..0x31 and 0x3C..0x59 through SelectScreen.InitializeSpriteArray;
    //   4. build the five rows;
    //   5. play the screen in (FUN_8002d330), blank four markers, bring the sound system up;
    //   6. run the driver;
    //   7. tear the sound system down, restart the CD-DA track, play the screen out
    //      (FUN_8002d908), and undo steps 2 and 1 in that order.
    internal static void RunSoundTestScreen()
    {
        sbyte cVar1;
        short sVar8;
        int pGVar15;
        int puVar16;
        int iVar14;
        short sVar17;
        int iVar18;
        int iVar19;
        int iVar20;
        int iVar24;
        short sVar21;
        sbyte cVar22;
        sbyte cVar23;
        short sVar25;
        byte uVar26;

        // JUSTIFICATION: C# language bridge only
        // RELATION: `ulong local_478 [280]` is 1120 bytes of stack the save loop fills with whole
        // GsSPRITEs and the restore loop reads back. GsSPRITE is a class here, so the shadow
        // needs its thirty-one instances constructed before a field-by-field copy can write into
        // them. The count and the stride are the original's own: `iVar18 < 0x1f` and
        // `puVar16 = puVar16 + 9` on a four-byte pointer.
        LibGs.GsSPRITE[] local_478 = new LibGs.GsSPRITE[0x1f];
        for (int slot = 0; slot < 0x1f; slot++)
        {
            local_478[slot] = new LibGs.GsSPRITE();
        }

        iVar18 = 0;
        pGVar15 = 0x3c;
        puVar16 = 0;
        do
        {
            // The inner do/while is the compiler's word-wise struct copy: two four-word blocks
            // plus the trailing `rotate` word, thirty-six bytes in all. See CopyGsSprite.
            CopyGsSprite(local_478[puVar16], SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[pGVar15]);
            pGVar15 = pGVar15 + 1;
            iVar18 = iVar18 + 1;
            puVar16 = puVar16 + 1;
        }
        while (iVar18 < 0x1f);

        // Element 0x3B parked inside element 0x5A for the duration — the same 36-byte copy, and
        // 0x5A's own contents are already in local_478 from the loop above. Undone at the tail.
        CopyGsSprite(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x5a], SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3b]);
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3b].attribute = 0x80000000;

        // `InitializeSpriteArray(&GsSPRITE_ARRAY_800654ec[0x1e].attribute, 0x14)` and
        // `InitializeSpriteArray(&GsSPRITE_ARRAY_800654ec[0x3c].attribute, 0x1e)` — the C# form takes the
        // array plus a start index, which is the same span.
        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 0x1e, 0x14);
        SelectScreen.InitializeSpriteArray(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec, 0x3c, 0x1e);

        iVar19 = 0;
        sVar8 = -0x40;
        iVar18 = 0x870;
        do
        {
            // iVar18 / 0x24 is element 0x3C .. 0x59 — the thirty sprites this loop lays out on a
            // 0x20-pixel pitch, all at x = -0x80 on tpage 0x1E.
            int e = iVar18 / 0x24;

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].y = sVar8;
            sVar8 = (short)(sVar8 + 0x20);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].tpage = 0x1e;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].x = unchecked((short)0xff80);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].cy = 0x1f7;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e].attribute = 0;
            iVar19 = iVar19 + 1;
            iVar18 = iVar18 + 0x24;
        }
        while (iVar19 < 0x1e);

        iVar20 = 0;
        uVar26 = (byte)'@';
        sVar25 = -0x40;
        iVar18 = 0xbf4;
        iVar24 = 0x55;
        cVar23 = 0;
        cVar22 = 0;
        sVar21 = -0x48;
        sVar17 = -0x38;
        sVar8 = -0x3c;
        iVar14 = 0x924;
        iVar19 = 0x870;
        do
        {
            // The five bases resolved. See the map in this file's header: iVar19 == iVar18 -
            // 0x384 and iVar14 == iVar18 - 0x2D0 hold on every iteration, so e3C and e41 could be
            // written off iVar18 too; they are written off their own cursors because that is the
            // cursor the original's store uses.
            int e3C = iVar19 / 0x24;
            int e41 = iVar14 / 0x24;
            int e46 = (iVar18 - 0x21c) / 0x24;
            int e4B = (iVar18 - 0x168) / 0x24;
            int e55 = iVar18 / 0x24;

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].b = (byte)'@';
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].g = (byte)'@';
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].r = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].x = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e41].x = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e46].x = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e4B].x = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e41].y = sVar8;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].y = sVar8;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e46].y = sVar17;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e4B].y = sVar21;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].u = (byte)cVar22;

            // `cVar1 = *(char *)((int)&DAT_80055aa0 + iVar24 + 3)` — the five .sbss bytes at
            // 0x80055AF8..0x80055AFC, which nothing in SELECT.EXE ever writes. See the global.
            cVar1 = (sbyte)DAT_80055aa0[iVar24 + 3];
            iVar24 = iVar24 + 1;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e4B].u = (byte)cVar23;
            cVar23 = (sbyte)(cVar23 + 0x30);
            cVar22 = (sbyte)(cVar22 + 0x28);
            sVar21 = (short)(sVar21 + 0x20);
            sVar17 = (short)(sVar17 + 0x20);
            sVar8 = (short)(sVar8 + 0x20);
            iVar14 = iVar14 + 0x24;
            iVar19 = iVar19 + 0x24;
            iVar20 = iVar20 + 1;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e46].u = 200;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e4B].v = 0xb8;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].w = 0x28;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e4B].w = 0x30;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].mx = 0x78;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].v = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e41].v = 0x10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e46].v = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e41].w = 0x38;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e46].w = 0x20;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].h = 0x10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e41].h = 0x10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e46].h = 0x10;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e4B].h = 0x20;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e3C].attribute = 0x80000000;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e41].attribute = 0x80000000;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e46].attribute = 0x80000000;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e4B].attribute = 0x80000000;

            // `-(cVar1 == '\0') & 0x38` — the comparison as 0 or -1, masked. cVar1 is provably 0
            // (see DAT_80055aa0), so this is 0x38 on all five rows, but the expression is kept.
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e41].u = (byte)((cVar1 == 0 ? -1 : 0) & 0x38);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e41].mx = 0x40;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].v = uVar26;
            uVar26 = (byte)(uVar26 + 0x18);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].y = sVar25;
            sVar25 = (short)(sVar25 + 0x20);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e4B].mx = unchecked((short)0xffe0);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].tpage = 0x1e;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].w = 0xd0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].h = 0x18;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].cx = 0x170;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].cy = 0x1f8;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e46].mx = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].x = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].u = 0;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].mx = unchecked((short)0x80);
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[e55].attribute = 0x80000000;
            iVar18 = iVar18 + 0x24;
        }
        while (iVar20 < 5);

        // The last three value strips are narrower than the 0xD0 the loop gave them.
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x57].w = 0xe8;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x58].w = 0x100;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x59].w = 0x38;

        // The row the cursor is parked on comes back to full brightness — the loop above left
        // every label at 0x40.
        iVar18 = DAT_80055a00 + 0x3c;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar18].b = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar18].g = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar18].r = 0x80;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x4d].w = 0x48;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x4e].w = 0x60;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x4e].u = 0;
        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x4e].v = 0xd8;

        FUN_8002d330();

        // `iVar18 = 0xaf8` is element 0x4E; the loop walks down by 0x24 while `-1 < iVar19` with
        // iVar19 from 3, so it clears the attribute of elements 0x4E, 0x4D, 0x4C, 0x4B — the four
        // markers the play-in above just finished with.
        iVar19 = 3;
        iVar18 = 0xaf8;
        do
        {
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[iVar18 / 0x24].attribute = 0;
            iVar19 = iVar19 + -1;
            iVar18 = iVar18 + -0x24;
        }
        while (-1 < iVar19);

        InitializeSoundSystem();
        DAT_80055b00 = 0;

        // The original discards the driver's return value. It is discarded here too.
        RunSoundTestMenu(ref DAT_80055a00, DAT_80055dbc);

        ShutdownSoundSystem();
        StopCdAudio();
        InitializeCdAudio();
        FUN_800258f0(10, 3);
        PlayCdCurrentTrack();
        FUN_8002d908();

        // Undo the 0x3B stash, then the thirty-one-sprite stash — in that order, so 0x5A's own
        // saved contents overwrite the parked copy.
        CopyGsSprite(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x3b], SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x5a]);

        iVar18 = 0;
        puVar16 = 0;
        pGVar15 = 0x3c;
        do
        {
            CopyGsSprite(SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[pGVar15], local_478[puVar16]);
            puVar16 = puVar16 + 1;
            iVar18 = iVar18 + 1;
            pGVar15 = pGVar15 + 1;
        }
        while (iVar18 < 0x1f);
    }

    // GHIDRA: RunSoundTestMenu @ 0x80026B1C
    // 3004 bytes, fourteen callees, one caller. Ghidra recovers
    // `int RunSoundTestMenu(int *param_1,int *param_2)`; RunSoundTestScreen passes
    // &DAT_80055a00 and &DAT_80055dbc.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: param_1 is an `int *` the body both reads and WRITES through (`*param_1 = 0`,
    // `*param_1 = 4`), pointing at a single global. C# cannot store an interior pointer to a
    // static, so it becomes `ref int`. param_2 is a five-element array and stays an array. No
    // behaviour changes: the caller passes the same two globals the original takes the address of.
    //
    // Shape: one blocking do/while that reads the pad through PadInput.FUN_80026208(4), runs the
    // repeat cadence (twelve frames held, then every fifth), applies the edits, polls the SPU key
    // state into the four lamps, blits eleven decimal digits with MoveImage and presents through
    // FrameStep.DrawFrame. It leaves only through row 4 + Circle.
    //
    // THE FIVE MAXIMA ARE HARD-CODED HERE, on the stack, as local_50 = { 0xF, 0x12, 0x15, 0x66A,
    // 0 }. Row 4's maximum of 0 is why its value can never leave 0.
    //
    // THE BUTTON BITS are PadInput's table: 0x1000 Up, 0x4000 Down, 0x8000 Left, 0x2000 Right,
    // 0x20 Circle, 0x40 Cross. Up/Down move the row, Left/Right step the value by one, and
    // Cross/Square (0x08 / 0x04, the two the original tests as `& 8` and `& 4`) step it by a
    // hundred but only on rows 2, 3 and 4 (`1 < *param_1`). Circle plays, Cross stops.
    // PARTIAL: bits 0x08 and 0x04 are read here as the +100 / -100 pair from the code alone; the
    // pad-bit table in PadInput.cs names 0x40 Cross and 0x80 Square and does not name 0x08 or
    // 0x04, so which physical buttons those two are is not closed. The masks are transliterated
    // as written.
    internal static int RunSoundTestMenu(ref int param_1, int[] param_2)
    {
        byte bVar2;
        bool bVar3;
        bool bVar4;
        uint uVar5;
        int iVar6;
        int pbVar7;
        int iVar8;
        int iVar9;
        int local_30;

        // JUSTIFICATION: C# language bridge only
        // RELATION: `char cStack_68; byte local_67 [16]; byte local_57 [6]; byte local_51;` are
        // FOUR NAMES FOR ONE CONTIGUOUS 24-BYTE STACK SLOT — the frame offsets say so: 0x68, 0x67
        // (one byte on), 0x57 (sixteen further on) and 0x51 (six further on). All three
        // SpuRGetAllKeysStatus calls are handed &cStack_68, the base; the three scans read from
        // the three interior names. One byte[24] with the three offsets spelled out is the only
        // way to keep both facts in C#.
        // WORTH RECORDING, NOT FIXING (rule 12): every scan starts one byte ABOVE the buffer the
        // SDK was told to fill, and the second and third scans read bytes 17..22 and 23 of a
        // buffer libspu refills from byte 0 each time. That is what the original does.
        const int local_67_Index = 1;
        const int local_57_Index = 17;
        const int local_51_Index = 23;
        byte[] cStack_68 = new byte[24];

        int[] local_50 = new int[6];
        RECT local_38 = new RECT();

        local_50[0] = 0xf;
        local_50[1] = 0x12;
        local_50[2] = 0x15;
        local_50[3] = 0x66a;
        local_50[4] = 0;

        // The two `swl`/`swr` pairs at 0x80026B98..0x80026BA4 (`swl v0,0x43(sp)` / `swr
        // v0,0x40(sp)`, then the same for v1 at 0x47/0x44) are one unaligned eight-byte copy of
        // DAT_80055a04 and DAT_80055a08 onto the stack. Ghidra prints the partial-word algebra and
        // then the whole-word result; the net effect is these four halfwords.
        local_38.x = (short)DAT_80055a04;
        local_38.y = (short)(DAT_80055a04 >> 0x10);
        local_38.w = (short)DAT_80055a08;
        local_38.h = (short)(DAT_80055a08 >> 0x10);

        iVar8 = 0;
        iVar9 = 0;
        bVar4 = false;
        local_30 = 1;
        do
        {
            uVar5 = PadInput.FUN_80026208(4);
            SELECT_EXE_exe.g_PadButtonWord = (int)(uVar5 & 0xffff);
            if (SELECT_EXE_exe.g_PadButtonWord == 0)
            {
                bVar4 = true;
                iVar8 = 0;
                iVar9 = 0;
            }

            UpdateSound();
            bVar3 = 0xc < iVar9;
            iVar9 = iVar9 + 1;

            // `((bVar3) && (iVar8 = iVar8 + 1, iVar8 == 1)) || ((bVar4 && (g_PadButtonWord != 0)))`
            // — the comma operator's increment only runs when bVar3 holds, and the right operand
            // has no side effect, so the short circuit is carried in a local instead.
            bool acceptEdit;
            if (bVar3)
            {
                iVar8 = iVar8 + 1;
                acceptEdit = iVar8 == 1;
            }
            else
            {
                acceptEdit = false;
            }

            if (!acceptEdit)
            {
                acceptEdit = bVar4 && (SELECT_EXE_exe.g_PadButtonWord != 0);
            }

            if (acceptEdit)
            {
                if (bVar4)
                {
                    bVar4 = false;
                }

                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1 + 0x3c].r = (byte)'@';
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1 + 0x3c].g = (byte)'@';
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1 + 0x3c].b = (byte)'@';

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x4000) != 0)
                {
                    iVar6 = param_1;
                    param_1 = iVar6 + 1;
                    if (4 < iVar6 + 1)
                    {
                        param_1 = 0;
                    }
                }

                if (((SELECT_EXE_exe.g_PadButtonWord & 8) != 0) && (1 < param_1))
                {
                    iVar6 = param_2[param_1] + 100;
                    param_2[param_1] = iVar6;
                    if (local_50[param_1] < iVar6)
                    {
                        param_2[param_1] = param_2[param_1] % 100;
                    }
                }

                if (((SELECT_EXE_exe.g_PadButtonWord & 4) != 0) && (1 < param_1))
                {
                    iVar6 = param_2[param_1] + -100;
                    param_2[param_1] = iVar6;
                    if (iVar6 < 0)
                    {
                        param_2[param_1] = param_2[param_1] + 100 + (local_50[param_1] / 100) * 100;
                    }
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x1000) != 0)
                {
                    iVar6 = param_1;
                    param_1 = iVar6 + -1;
                    if (iVar6 + -1 < 0)
                    {
                        param_1 = 4;
                    }
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x2000) != 0)
                {
                    iVar6 = param_2[param_1] + 1;
                    param_2[param_1] = iVar6;
                    if (local_50[param_1] < iVar6)
                    {
                        param_2[param_1] = 0;
                    }
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x8000) != 0)
                {
                    iVar6 = param_2[param_1] + -1;
                    param_2[param_1] = iVar6;
                    if (iVar6 < 0)
                    {
                        param_2[param_1] = local_50[param_1];
                    }
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x20) != 0)
                {
                    if (param_1 == 4)
                    {
                        local_30 = 0;
                    }
                    else
                    {
                        SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1 + 0x41].u = 0;
                        DAT_80055b00 = 0x30;
                        switch (param_1)
                        {
                            case 0:
                                if ((DAT_80055b38 & 0x3a) == 0)
                                {
                                    // `(int)((*(ushort *)(param_2 + *param_1) + 3) * 0x10000) >> 0x10`
                                    // — the row's value read as a ushort, plus three, sign-extended
                                    // back down from a short.
                                    FUN_800258f0(0xc, (short)((ushort)param_2[param_1] + 3));
                                    PlayCdCurrentTrack();
                                }

                                break;
                            case 1:
                                RequestBgmPlay((short)param_2[param_1]);
                                break;
                            case 2:
                                PlaySoundEffect((short)((ushort)param_2[param_1] + 1), 0, 0x7f);
                                break;
                            case 3:
                                FUN_80025248(0, 0, (short)param_2[param_1]);
                                break;
                        }
                    }
                }

                if ((SELECT_EXE_exe.g_PadButtonWord & 0x40) != 0)
                {
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1 + 0x41].u = (byte)'8';
                    DAT_80055b00 = 0;

                    // Ghidra prints an `if (true)` around this switch — a folded guard with no
                    // surviving test. Recorded, not emitted.
                    switch (param_1)
                    {
                        case 0:
                            StopCdAudio();
                            break;
                        case 1:
                            RequestBgmStop();
                            break;
                        case 2:
                            PlaySoundEffect(0, 0, 0x7f);
                            break;
                        case 3:
                            FUN_800253a4(0);
                            break;
                    }
                }
            }

            iVar6 = FUN_80025d64();
            if (iVar6 == 0)
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x41].u = (byte)'8';
            }
            else
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x41].u = 0;
            }

            // The three key-state polls. LibSpu.SpuRGetAllKeysStatus is a `// Do nothing PSX SDK`
            // stub, so the buffer stays zero and every count below comes out 0.
            LibSpu.SpuRGetAllKeysStatus(1, 0x10000, cStack_68);
            iVar6 = 0;
            pbVar7 = local_67_Index;
            do
            {
                bVar2 = cStack_68[pbVar7];
                pbVar7 = pbVar7 + 1;
                if ((bVar2 & 3) != 0)
                {
                    iVar6 = iVar6 + 1;
                }
            }
            while (pbVar7 < local_57_Index);

            if (iVar6 == 0)
            {
                iVar6 = DAT_80055b00 + -1;
                if (DAT_80055b00 == 0)
                {
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x42].u = (byte)'8';
                    iVar6 = DAT_80055b00;
                }
            }
            else
            {
                iVar6 = DAT_80055b00 + 1;
                if (DAT_80055b00 == 0x30)
                {
                    SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x42].u = 0;
                    iVar6 = DAT_80055b00;
                }
            }

            DAT_80055b00 = iVar6;

            LibSpu.SpuRGetAllKeysStatus(0x20000, 0x400000, cStack_68);
            iVar6 = 0;
            pbVar7 = local_57_Index;
            do
            {
                bVar2 = cStack_68[pbVar7];
                pbVar7 = pbVar7 + 1;
                if ((bVar2 & 3) != 0)
                {
                    iVar6 = iVar6 + 1;
                }
            }
            while (pbVar7 < local_51_Index);

            if (iVar6 == 0)
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x43].u = (byte)'8';
            }
            else
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x43].u = 0;
            }

            LibSpu.SpuRGetAllKeysStatus(0x800000, 0x800000, cStack_68);
            if ((cStack_68[local_51_Index] & 3) == 0)
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x44].u = (byte)'8';
            }
            else
            {
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[0x44].u = 0;
            }

            if (0xc < iVar9)
            {
                iVar8 = iVar8 % 5;
            }

            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1 + 0x3c].r = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1 + 0x3c].g = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[param_1 + 0x3c].b = 0x80;

            // ELEVEN DIGIT BLITS out of one six-by-thirty-two source cell whose x is rewritten
            // per digit: `digit * 6 + 0x380`, so the digit strip lives at VRAM (0x380, 0x120).
            // Row 0 gets two digits at (0x380, 0x1B8) and (0x386, 0x1B8), row 1 two more, row 2
            // three, row 3 four at y = 0x1D8. Rows 0..3 only — row 4 is the exit and has none.
            // The `x / 10 * -10 + x` forms are the compiler's `x % 10`.
            local_38.x = (short)((short)(param_2[0] / 10) * 6 + 0x380);
            MoveImage(local_38, 0x380, 0x1b8);
            local_38.x = (short)(((short)param_2[0] + (short)(param_2[0] / 10) * -10) * 6 + 0x380);
            MoveImage(local_38, 0x386, 0x1b8);
            local_38.x = (short)((short)(param_2[1] / 10) * 6 + 0x380);
            MoveImage(local_38, 0x38c, 0x1b8);
            local_38.x = (short)(((short)param_2[1] + (short)(param_2[1] / 10) * -10) * 6 + 0x380);
            MoveImage(local_38, 0x392, 0x1b8);
            local_38.x = (short)((short)(param_2[2] / 100) * 6 + 0x380);
            MoveImage(local_38, 0x398, 0x1b8);
            local_38.x = (short)(((short)(param_2[2] / 10) + (short)((param_2[2] / 10) / 10) * -10) * 6 + 0x380);
            MoveImage(local_38, 0x39e, 0x1b8);
            local_38.x = (short)(((short)param_2[2] + (short)(param_2[2] / 10) * -10) * 6 + 0x380);
            MoveImage(local_38, 0x3a4, 0x1b8);
            local_38.x = (short)((short)(param_2[3] / 1000) * 6 + 0x380);
            MoveImage(local_38, 0x380, 0x1d8);
            local_38.x = (short)(((short)(param_2[3] / 100) + (short)((param_2[3] / 100) / 10) * -10) * 6 + 0x380);
            MoveImage(local_38, 0x386, 0x1d8);
            local_38.x = (short)(((short)(param_2[3] / 10) + (short)((param_2[3] / 10) / 10) * -10) * 6 + 0x380);
            MoveImage(local_38, 0x38c, 0x1d8);
            local_38.x = (short)(((short)param_2[3] + (short)(param_2[3] / 10) * -10) * 6 + 0x380);
            MoveImage(local_38, 0x392, 0x1d8);

            FrameStep.DrawFrame();
        }
        while (local_30 != 0);

        return param_1;
    }

    // ---------------------------------------------------------------------------------------
    // The callees of the two functions above that are NOT in this slice. Each one is declared so
    // the call site above is real and in the original's order; none of them is invented here.
    // ---------------------------------------------------------------------------------------

    // GHIDRA: InitializeSoundSystem @ 0x80022994
    private static void InitializeSoundSystem()
    {
        // BLOCKED: 2072 bytes, THIRTY-SIX callees, and out of this slice's surface. It brings the
        // whole audio stack up: SpuInit, SpuStInit, SpuSetVoiceAttr, SpuMallocWithStartAddr,
        // SpuSetTransferMode / SpuSetTransferStartAddr / SpuWrite0 / SpuIsTransferCompleted,
        // SsSetReservedVoice, SsSetTableSize, SsSetTickMode, SsUtGetVBaddrInSB,
        // SsVabOpenHeadSticky / SsVabTransBody / SsVabTransCompleted, SsUtReverbOn /
        // SsUtSetReverbType / SsUtSetReverbDepth, SsSetMVol, the three SpuSt* callback
        // registrations and VSyncCallback, over six CdSearchFile / CdRead / CdReadSync groups that
        // load the \SOUND\*.B VABs. It also calls InitializeCdAudio @ 0x80025658.
        // Everything it drives is a `// Do nothing PSX SDK` stub in PsxSdkMonogame/LibSnd.cs and
        // LibSpu.cs, so porting it would produce no audio either; it is left for the sound slice.
    }

    // GHIDRA: ShutdownSoundSystem @ 0x80025F2C
    private static void ShutdownSoundSystem()
    {
        // BLOCKED: 472 bytes, twelve callees — SsUtKeyOffV, SsVabClose x4, SsEnd, SpuQuit,
        // SpuStQuit, SsSetMVol, UpdateSound and two VSyncs. Same slice as InitializeSoundSystem.
    }

    // GHIDRA: UpdateSound @ 0x800231AC
    private static void UpdateSound()
    {
        // BLOCKED: the per-frame sound service, called once per driver iteration. It is one of the
        // writers of DAT_80055b38. Same slice as InitializeSoundSystem.
    }

    // GHIDRA: PlaySoundEffect @ 0x80025088
    private static void PlaySoundEffect(int param_1, int param_2, int param_3)
    {
        // BLOCKED: the sound-effect trigger. Called twice from the driver — (value + 1, 0, 0x7F)
        // on Circle and (0, 0, 0x7F) on Cross. Same slice as InitializeSoundSystem.
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: RequestBgmPlay @ 0x800240A8
    private static void RequestBgmPlay(int param_1)
    {
        // BLOCKED: the BGM start request, row 1's Circle. Same slice as InitializeSoundSystem.
        _ = param_1;
    }

    // GHIDRA: RequestBgmStop @ 0x80024168
    private static void RequestBgmStop()
    {
        // BLOCKED: the BGM stop request, row 1's Cross. Same slice as InitializeSoundSystem.
    }

    // GHIDRA: FUN_80025248 @ 0x80025248
    private static void FUN_80025248(int param_1, int param_2, short param_3)
    {
        // BLOCKED: row 3's Circle. It is one of the readers and writers of DAT_80055b38. Its 0x66A
        // maximum is the largest of the five, which is what makes row 3 the four-digit row.
        // PARTIAL: what it plays is not closed — only that the driver hands it (0, 0, value).
        _ = param_1;
        _ = param_2;
        _ = param_3;
    }

    // GHIDRA: FUN_800253a4 @ 0x800253A4
    private static void FUN_800253a4(int param_1)
    {
        // BLOCKED: row 3's Cross, the counterpart of FUN_80025248. Also a DAT_80055b38 writer.
        _ = param_1;
    }

    // GHIDRA: FUN_80025d64 @ 0x80025D64
    private static int FUN_80025d64()
    {
        // BLOCKED: polled once per driver iteration; its result drives sprite 0x41's frame — 0
        // shows u = 0x38, non-zero shows u = 0. Same slice as InitializeSoundSystem.
        // PARTIAL: the zero returned here is the stub's, not a measured answer, so lamp 0x41
        // stays on its idle frame.
        return 0;
    }

    // GHIDRA: StopCdAudio @ 0x80025894
    private static void StopCdAudio()
    {
        // BLOCKED: CdControlB(CdlInit) then CdControlB(CdlStop), 92 bytes, five call sites. It
        // belongs to the CD module (CdAudio.cs), whose TOC-dependent half is blocked there because
        // the drive's response FIFO is not modelled and CdGetToc yields an all-zero table.
        // ModeBranches.cs carries the same private declaration for its own three call sites.
    }

    // GHIDRA: InitializeCdAudio @ 0x80025658
    private static void InitializeCdAudio()
    {
        // BLOCKED: the CD-DA / sound-mix bring-up — CdGetToc into the TOC array at 0x80055CEC,
        // CdPosToInt/CdIntToPos normalisation, then the CD mix volumes chosen from bit 0x801FF01E
        // (the stereo/mono option) through CdMix plus SsSetMVol / SsSetSerialAttr /
        // SsSetSerialVol and SsSetStereo/SsSetMono. SelectScreen.cs carries the same private
        // declaration for its own call site. Blocked on the same missing TOC.
    }

    // GHIDRA: FUN_800258f0 @ 0x800258F0
    private static void FUN_800258f0(int param_1, int param_2)
    {
        // BLOCKED: the CD-DA play/seek helper, and a DAT_80055b38 writer. RunSoundTestScreen calls
        // it (10, 3) on the way out; the driver calls it (0xC, value + 3) for row 0. Depends on
        // the TOC InitializeCdAudio would have filled, so it is blocked with it. SelectScreen.cs
        // carries the same private declaration.
        _ = param_1;
        _ = param_2;
    }

    // GHIDRA: PlayCdCurrentTrack @ 0x80025D04
    private static void PlayCdCurrentTrack()
    {
        // BLOCKED: re-issues CdControl(CdlPlay) at the current TOC position; also a DAT_80055b38
        // writer. Same dependency. SelectScreen.cs carries the same private declaration.
    }

    // GHIDRA: FUN_8002d330 @ 0x8002D330
    private static void FUN_8002d330()
    {
        // BLOCKED: 1496 bytes, ONE caller (this file). The sound test's PLAY-IN transition — the
        // same shape as every build/unwind pair in ScreenDecoration.cs: __floatsidf / __muldf3 /
        // __adddf3 / __subdf3 / __divdf3 / __fixdfsi soft-float over the sprite fields, presenting
        // through FrameStep.DrawFrame, no audio callee at all.
        // It sits inside ScreenDecoration.cs's own emission block (0x80029684..0x8002EA8B), in the
        // gap between FUN_8002cc04 and FUN_8002dec0, and belongs to that file, not to this one.
    }

    // GHIDRA: FUN_8002d908 @ 0x8002D908
    private static void FUN_8002d908()
    {
        // BLOCKED: 1464 bytes, ONE caller (this file). The sound test's PLAY-OUT transition, the
        // counterpart of FUN_8002d330 and the same seven callees. Same emission block, same owner.
    }
}
