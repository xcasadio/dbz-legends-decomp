using PsxSdkMonogame;
using static PsxSdkMonogame.LibGcc;
using static PsxSdkMonogame.MipsMemory;

namespace DbzLegendsRemaster.SELECT_EXE;

// THE MODE MENU — FUN_800283a0 @ 0x800283A0 — and the satellite fan-out FUN_80033630 @ 0x80033630
// that it drives twice per frame, plus the 451-entry sine table both of them read.
//
// FUN_800283a0 is the function main's `switch` value comes from. It owns the cursor DAT_80055A0C
// @ 0x80055A0C outright: find-cross-references reports FOURTEEN references to that global and every
// single one is inside this function. It clamps the cursor to [0, itemCount - 1] on both edges — up
// wraps to itemCount - 1, down wraps to 0 — so the value it returns is never negative, and THAT is
// why main's `case -1` (the only path out of main's do/while) is unreachable. Nothing in this file
// may widen that range; if it ever does, main gains an exit it does not have on the console.
//
// The item count is 3, or 4 when bit 1 of the options word at 0x801FF018 is set — the same gate
// main applies to redirect item 2, and the same gate FUN_80030a6c applies to the artwork.
//
// WHAT THE ANIMATION IS: seven sprite CHAINS orbiting the screen centre. FUN_80030698 @ 0x80030698
// built a triangular table of 35 GsSPRITE addresses at 0x800593B8 — seven records of twelve bytes,
// record i holding a LEADER address at +0x00 and a pointer at +0x04 to a row of i + 1 SATELLITE
// addresses inside 0x80058E08. This function positions the seven leaders on an ellipse from the
// sine/cosine table, and FUN_80033630 then copies each leader's position, plus a fixed per-satellite
// offset, onto that leader's satellites. 7 leaders + 28 satellites = 35, which is exactly the count
// FUN_80030698 wrote (GsSPRITE elements 60..94).
//
// THE ARITHMETIC IS DOUBLE-PRECISION, in the original as much as here: GCC lowered it to the libgcc
// soft-float calls PsxSdkMonogame/LibGcc.cs stands in for. x = -sin(a) * r / 4096 * k, y = cos(a) * r / 4096 * k',
// with (k, k') swapping between (1.2, 0.8) and (0.8, 1.2) as the phase flag flips — the ellipse
// turns on its side.
internal static class ModeMenu
{
    // GHIDRA: DAT_80055a0c @ 0x80055A0C
    // .sdata, undefined4, image value 0 (read with get-data). THE MODE-MENU CURSOR and main's
    // switch value. Fourteen references, all inside FUN_800283a0 below.
    internal static int DAT_80055a0c;

    // GHIDRA: DAT_8004f464 @ 0x8004F464
    // THE SINE TABLE. 451 signed halfwords, one per degree, scaled by 4096. Read out of the image
    // with read-memory (902 bytes at 0x8004F464) and verified against round(4096 * sin(deg)): the
    // maximum deviation over all 451 entries is 1, and the peak is clamped to 4095 rather than 4096.
    // The values below are the image's, not the formula's.
    //
    // BOTH ENDS ARE CLOSED. 0x8004F464 is the byte after g_UsagiBChunkTable's last record
    // (0x8004F380 + 19 * 12 = 0x8004F464, and SelectScreen.cs already closes that table there).
    // 0x8004F464 + 902 = 0x8004F7EA, and 0x8004F7EC — two bytes of padding later — is the six-word
    // VS slot array the data pass measured. 451 entries and not 360 because the COSINE base is the
    // same table 90 entries in: Ghidra names it DAT_8004f518, and 0x8004F518 - 0x8004F464 = 0xB4 =
    // 180 bytes = 90 halfwords. cos(359) therefore reads entry 449, which is why the table has to
    // run to 450.
    internal static readonly short[] DAT_8004f464 =
    {
        0, 71, 142, 214, 285, 356, 428, 499, 570, 640,
        711, 781, 851, 921, 990, 1060, 1128, 1197, 1265, 1333,
        1400, 1467, 1534, 1600, 1665, 1730, 1795, 1859, 1922, 1985,
        2047, 2109, 2170, 2230, 2290, 2349, 2407, 2464, 2521, 2577,
        2632, 2687, 2740, 2793, 2845, 2896, 2946, 2995, 3043, 3091,
        3137, 3183, 3227, 3271, 3313, 3355, 3395, 3435, 3473, 3510,
        3547, 3582, 3616, 3649, 3681, 3712, 3741, 3770, 3797, 3823,
        3848, 3872, 3895, 3916, 3937, 3956, 3974, 3990, 4006, 4020,
        4033, 4045, 4056, 4065, 4073, 4080, 4086, 4090, 4093, 4095,
        4095, 4095, 4093, 4090, 4086, 4080, 4073, 4065, 4056, 4045,
        4033, 4020, 4006, 3991, 3974, 3956, 3937, 3917, 3895, 3872,
        3849, 3824, 3797, 3770, 3741, 3712, 3681, 3649, 3616, 3582,
        3547, 3511, 3473, 3435, 3395, 3355, 3313, 3271, 3227, 3183,
        3137, 3091, 3044, 2995, 2946, 2896, 2845, 2793, 2740, 2687,
        2633, 2577, 2521, 2465, 2407, 2349, 2290, 2231, 2170, 2109,
        2048, 1986, 1923, 1859, 1795, 1731, 1666, 1600, 1534, 1468,
        1401, 1333, 1266, 1197, 1129, 1060, 991, 921, 851, 781,
        711, 641, 570, 499, 428, 357, 286, 214, 143, 71,
        0, -71, -142, -213, -285, -356, -427, -498, -569, -640,
        -710, -781, -851, -921, -990, -1059, -1128, -1197, -1265, -1333,
        -1400, -1467, -1533, -1600, -1665, -1730, -1795, -1859, -1922, -1985,
        -2047, -2109, -2170, -2230, -2290, -2348, -2407, -2464, -2521, -2577,
        -2632, -2686, -2740, -2793, -2844, -2895, -2946, -2995, -3043, -3090,
        -3137, -3182, -3227, -3270, -3313, -3354, -3395, -3434, -3473, -3510,
        -3546, -3582, -3616, -3649, -3681, -3712, -3741, -3770, -3797, -3823,
        -3848, -3872, -3895, -3916, -3937, -3956, -3974, -3990, -4006, -4020,
        -4033, -4045, -4056, -4065, -4073, -4080, -4085, -4090, -4093, -4095,
        -4095, -4095, -4093, -4090, -4086, -4080, -4073, -4065, -4056, -4045,
        -4033, -4020, -4006, -3991, -3974, -3956, -3937, -3917, -3895, -3873,
        -3849, -3824, -3797, -3770, -3742, -3712, -3681, -3649, -3616, -3582,
        -3547, -3511, -3473, -3435, -3396, -3355, -3314, -3271, -3228, -3183,
        -3138, -3091, -3044, -2996, -2946, -2896, -2845, -2793, -2741, -2687,
        -2633, -2578, -2522, -2465, -2408, -2349, -2291, -2231, -2171, -2110,
        -2048, -1986, -1923, -1860, -1796, -1731, -1666, -1601, -1535, -1468,
        -1401, -1334, -1266, -1198, -1129, -1060, -991, -922, -852, -782,
        -711, -641, -570, -499, -428, -357, -286, -215, -143, -72,
        0, 71, 142, 214, 285, 356, 428, 499, 570, 640,
        711, 781, 851, 921, 990, 1060, 1128, 1197, 1265, 1333,
        1400, 1467, 1534, 1600, 1665, 1730, 1795, 1859, 1922, 1985,
        2047, 2109, 2170, 2230, 2290, 2349, 2407, 2464, 2521, 2577,
        2632, 2687, 2740, 2793, 2845, 2896, 2946, 2995, 3043, 3091,
        3137, 3183, 3227, 3271, 3313, 3355, 3395, 3435, 3473, 3510,
        3547, 3582, 3616, 3649, 3681, 3712, 3741, 3770, 3797, 3823,
        3848, 3872, 3895, 3916, 3937, 3956, 3974, 3990, 4006, 4020,
        4033, 4045, 4056, 4065, 4073, 4080, 4086, 4090, 4093, 4095,
        4095,
    };

    // GHIDRA: DAT_800205e4 @ 0x800205E4
    // .rdata, eight halfwords { 0, 50, 100, 150, 200, 250, 300, 0 }, read out of the image. The
    // compiler did NOT reference it from FUN_800283a0 — it materialised the first seven entries as
    // three word immediates plus one halfword and stored them into the function's own 14-byte stack
    // block (Ghidra: local_68 = 0x320000, auStack_64 = 0x960064, auStack_60 = 0xfa00c8,
    // local_5c = 300, each word store rendered as the unaligned SWL/SWR pair the compiler emitted).
    // 0x00320000 little-endian is { 0x0000, 0x0032 }, 0x00960064 is { 0x0064, 0x0096 } and
    // 0x00FA00C8 is { 0x00C8, 0x00FA }, so the seven halfwords are exactly the first seven of the
    // .rdata block. It is spelled out here so the constant has a name and a provenance; the copy
    // below is still into the function's own local, as in the original.
    // The eighth entry is not used by this function: local_30 walks SEVEN entries.
    private static readonly ushort[] DAT_800205e4 = { 0, 50, 100, 150, 200, 250, 300, 0 };

    // JUSTIFICATION: C# language bridge only
    // RELATION: FUN_80030698 @ 0x80030698 stored raw GsSPRITE PSX ADDRESSES into the tables at
    // 0x800593B8 and 0x80058E08 — 0x80065D5C stepping by 0x24, that is elements 60..94 of
    // GsSPRITE_ARRAY_800654ec. This function and FUN_80033630 read them back and write through them.
    // The port models the sprite array as GsSPRITE objects, not as bytes (FrameStep.cs records why),
    // so an address read out of those tables has to be turned back into an element. The division is
    // the original's own stride: 0x800654EC is the array base and sizeof(GsSPRITE) is 36.
    // This is the address-to-element bridge FrameStep.cs flagged as missing.
    internal static LibGs.GsSPRITE SpriteAtAddress(int psxAddress)
    {
        return SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[
            (psxAddress - unchecked((int)0x800654EC)) / LibGs.GsSPRITE.SizeOf];
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: field +0x00 of record `row` of the seven twelve-byte records at 0x800593B8 — the
    // LEADER sprite's address. Ghidra names the seven of them DAT_800593b8, DAT_800593c4,
    // DAT_800593d0, DAT_800593dc, DAT_800593e8, DAT_800593f4 and DAT_80059400; they are one array
    // and SelectScreen.DAT_800593b8 is its raw bytes.
    // internal rather than private since ScreenDecoration.FUN_8002dec0 @ 0x8002DEC0 walks the same
    // seven records; the table has one home and this is it.
    internal static int LeaderAddress(int row)
    {
        return ReadI32(SelectScreen.DAT_800593b8, (row * 0xc) + 0);
    }

    // JUSTIFICATION: C# language bridge only
    // RELATION: field +0x04 of the same record — the pointer to that row's satellite addresses
    // inside SelectScreen.DAT_80058e08. Ghidra names these DAT_800593bc, DAT_800593c8, DAT_800593d4,
    // DAT_800593e0, DAT_800593ec, DAT_800593f8 and DAT_80059404, and dereferences them as
    // `*DAT_800593c8` / `DAT_800593c8[1]` — a pointer to an array of sprite addresses.
    // internal for the same reason as LeaderAddress above.
    internal static LibGs.GsSPRITE SatelliteSprite(int row, int index)
    {
        int rowBase = ReadI32(SelectScreen.DAT_800593b8, (row * 0xc) + 4);
        return SpriteAtAddress(PsxRam.ReadI32(rowBase + (index * 4)));
    }

    // GHIDRA: FUN_80033630 @ 0x80033630
    // 1796 bytes, no loop of any kind — 28 fully unrolled satellite updates, one per entry of the
    // triangular table, each with its own hard-coded offset pair. Called twice per frame by
    // FUN_800283a0 (once in the interactive loop, once in the outro).
    //
    // Row 0 has one satellite, row 6 has seven; 1+2+3+4+5+6+7 = 28, which is exactly the word count
    // of SelectScreen.DAT_80058e08. Every store is `satellite.x = leader.x + dx` /
    // `satellite.y = leader.y + dy`, and rows 0..2 leave some of those deltas at zero.
    internal static void FUN_80033630()
    {
        // row 0 — *DAT_800593bc from DAT_800593b8
        SatelliteSprite(0, 0).x = SpriteAtAddress(LeaderAddress(0)).x;
        SatelliteSprite(0, 0).y = SpriteAtAddress(LeaderAddress(0)).y;

        // row 1 — DAT_800593c8[0..1] from DAT_800593c4
        SatelliteSprite(1, 0).x = (short)(SpriteAtAddress(LeaderAddress(1)).x + -10);
        SatelliteSprite(1, 0).y = SpriteAtAddress(LeaderAddress(1)).y;
        SatelliteSprite(1, 1).x = (short)(SpriteAtAddress(LeaderAddress(1)).x + 10);
        SatelliteSprite(1, 1).y = SpriteAtAddress(LeaderAddress(1)).y;

        // row 2 — DAT_800593d4[0..2] from DAT_800593d0
        SatelliteSprite(2, 0).x = (short)(SpriteAtAddress(LeaderAddress(2)).x + -10);
        SatelliteSprite(2, 0).y = (short)(SpriteAtAddress(LeaderAddress(2)).y + 10);
        SatelliteSprite(2, 1).x = (short)(SpriteAtAddress(LeaderAddress(2)).x + 10);
        SatelliteSprite(2, 1).y = (short)(SpriteAtAddress(LeaderAddress(2)).y + 10);
        SatelliteSprite(2, 2).x = SpriteAtAddress(LeaderAddress(2)).x;
        SatelliteSprite(2, 2).y = (short)(SpriteAtAddress(LeaderAddress(2)).y + -10);

        // row 3 — DAT_800593e0[0..3] from DAT_800593dc
        SatelliteSprite(3, 0).x = (short)(SpriteAtAddress(LeaderAddress(3)).x + -0xe);
        SatelliteSprite(3, 0).y = (short)(SpriteAtAddress(LeaderAddress(3)).y + -8);
        SatelliteSprite(3, 1).x = (short)(SpriteAtAddress(LeaderAddress(3)).x + 9);
        SatelliteSprite(3, 1).y = (short)(SpriteAtAddress(LeaderAddress(3)).y + -9);
        SatelliteSprite(3, 2).x = (short)(SpriteAtAddress(LeaderAddress(3)).x + -0xf);
        SatelliteSprite(3, 2).y = (short)(SpriteAtAddress(LeaderAddress(3)).y + 0xc);
        SatelliteSprite(3, 3).x = (short)(SpriteAtAddress(LeaderAddress(3)).x + 10);
        SatelliteSprite(3, 3).y = (short)(SpriteAtAddress(LeaderAddress(3)).y + 0xc);

        // row 4 — DAT_800593ec[0..4] from DAT_800593e8
        SatelliteSprite(4, 0).x = (short)(SpriteAtAddress(LeaderAddress(4)).x + -2);
        SatelliteSprite(4, 0).y = (short)(SpriteAtAddress(LeaderAddress(4)).y + -0xe);
        SatelliteSprite(4, 1).x = (short)(SpriteAtAddress(LeaderAddress(4)).x + 0xf);
        SatelliteSprite(4, 1).y = (short)(SpriteAtAddress(LeaderAddress(4)).y + -4);
        SatelliteSprite(4, 2).x = (short)(SpriteAtAddress(LeaderAddress(4)).x + -0xc);
        SatelliteSprite(4, 2).y = (short)(SpriteAtAddress(LeaderAddress(4)).y + 0xf);
        SatelliteSprite(4, 3).x = (short)(SpriteAtAddress(LeaderAddress(4)).x + 9);
        SatelliteSprite(4, 3).y = (short)(SpriteAtAddress(LeaderAddress(4)).y + 0xe);
        SatelliteSprite(4, 4).x = (short)(SpriteAtAddress(LeaderAddress(4)).x + -0x12);
        SatelliteSprite(4, 4).y = (short)(SpriteAtAddress(LeaderAddress(4)).y + -2);

        // row 5 — DAT_800593f8[0..5] from DAT_800593f4
        SatelliteSprite(5, 0).x = (short)(SpriteAtAddress(LeaderAddress(5)).x + -2);
        SatelliteSprite(5, 0).y = (short)(SpriteAtAddress(LeaderAddress(5)).y + -0xe);
        SatelliteSprite(5, 1).x = (short)(SpriteAtAddress(LeaderAddress(5)).x + 0xf);
        SatelliteSprite(5, 1).y = (short)(SpriteAtAddress(LeaderAddress(5)).y + -4);
        SatelliteSprite(5, 2).x = (short)(SpriteAtAddress(LeaderAddress(5)).x + -0xc);
        SatelliteSprite(5, 2).y = (short)(SpriteAtAddress(LeaderAddress(5)).y + 0xf);
        SatelliteSprite(5, 3).x = (short)(SpriteAtAddress(LeaderAddress(5)).x + 9);
        SatelliteSprite(5, 3).y = (short)(SpriteAtAddress(LeaderAddress(5)).y + 0xe);
        SatelliteSprite(5, 4).x = (short)(SpriteAtAddress(LeaderAddress(5)).x + -2);
        SatelliteSprite(5, 4).y = (short)(SpriteAtAddress(LeaderAddress(5)).y + 1);
        SatelliteSprite(5, 5).x = (short)(SpriteAtAddress(LeaderAddress(5)).x + -0x12);
        SatelliteSprite(5, 5).y = (short)(SpriteAtAddress(LeaderAddress(5)).y + -2);

        // row 6 — DAT_80059404[0..6] from DAT_80059400
        SatelliteSprite(6, 0).x = (short)(SpriteAtAddress(LeaderAddress(6)).x + -2);
        SatelliteSprite(6, 0).y = (short)(SpriteAtAddress(LeaderAddress(6)).y + -0x10);
        SatelliteSprite(6, 1).x = (short)(SpriteAtAddress(LeaderAddress(6)).x + 0xe);
        SatelliteSprite(6, 1).y = (short)(SpriteAtAddress(LeaderAddress(6)).y + -8);
        SatelliteSprite(6, 2).x = (short)(SpriteAtAddress(LeaderAddress(6)).x + -0x11);
        SatelliteSprite(6, 2).y = (short)(SpriteAtAddress(LeaderAddress(6)).y + 10);
        SatelliteSprite(6, 3).x = (short)(SpriteAtAddress(LeaderAddress(6)).x + 0xe);
        SatelliteSprite(6, 3).y = (short)(SpriteAtAddress(LeaderAddress(6)).y + 10);
        SatelliteSprite(6, 4).x = (short)(SpriteAtAddress(LeaderAddress(6)).x + -2);
        SatelliteSprite(6, 4).y = (short)(SpriteAtAddress(LeaderAddress(6)).y + 1);
        SatelliteSprite(6, 5).x = (short)(SpriteAtAddress(LeaderAddress(6)).x + -0x11);
        SatelliteSprite(6, 5).y = (short)(SpriteAtAddress(LeaderAddress(6)).y + -6);
        SatelliteSprite(6, 6).x = (short)(SpriteAtAddress(LeaderAddress(6)).x + -1);
        SatelliteSprite(6, 6).y = (short)(SpriteAtAddress(LeaderAddress(6)).y + 0x12);
    }

    // GHIDRA: FUN_800283a0 @ 0x800283A0
    // 1944 bytes. THE MENU DRIVER. Two loops: the interactive one, which runs until Circle is seen,
    // and a fixed outro that flings the seven chains off-screen. It returns DAT_80055a0c and nothing
    // else — see the header note on why that makes main's `case -1` unreachable.
    //
    // THE CURSOR CADENCE, exactly as written: the highlight moves when EITHER the auto-repeat has
    // matured (more than 12 frames with a button held, and only on the pass where the repeat counter
    // first reaches 1) OR this is the first frame of a fresh press (local_48, armed whenever the pad
    // reads empty). Up is 0x1000, Down is 0x4000, Circle (0x20) ends the loop. Sprite
    // [cursor + 0x15] carries the highlight colour and [cursor + 0x19] the "selected" attribute.
    //
    // THE RADIUS, uVar13: it starts at 250 and falls by one per frame, through zero and on into
    // negative numbers, until it drops below -260 (or below -312 while the phase flag is set), at
    // which point it is reloaded with 260 (or 312). The phase flag flips when the radius passes
    // exactly zero, which is what swaps the two ellipse scale factors. The seven angles advance by
    // two degrees a frame and wrap at 360.
    //
    // JUSTIFICATION: C# language bridge only
    // RELATION: five of the conditions Ghidra prints use C's comma operator inside a short-circuit
    // `&&` (`(iVar12 = iVar12 + 1, iVar12 == 1)`, `(DAT_80055a0c = iVar11 + 1, ...)`,
    // `(uVar5 = 0x104, bVar2)`, and the `while (sVar4 = (short)uVar13, ...)` head). C# has no comma
    // operator, so each assignment stands on its own line inside the arm that already guarded it.
    // The order of the stores and the tests, and the short-circuiting, are unchanged.
    internal static int FUN_800283a0()
    {
        bool bVar2;
        short sVar4;
        uint uVar5;
        int iVar9;
        int iVar11;
        int iVar12;
        uint uVar13;
        double uVar14;
        double uVar15;
        double uVar16;
        double uVar7;
        int piVar10;
        int puVar6;

        // The 14-byte stack block, filled with the seven angles. See DAT_800205e4 above for how the
        // compiler spelled this: three unaligned word stores and one halfword store of immediates
        // that reproduce the .rdata block's first seven entries.
        ushort[] local_68 = new ushort[7];
        local_68[0] = DAT_800205e4[0];
        local_68[1] = DAT_800205e4[1];
        local_68[2] = DAT_800205e4[2];
        local_68[3] = DAT_800205e4[3];
        local_68[4] = DAT_800205e4[4];
        local_68[5] = DAT_800205e4[5];
        local_68[6] = DAT_800205e4[6];

        uVar13 = 0xfa;
        bVar2 = false;
        int local_50 = 1;
        int local_58 = 3;
        if ((PsxRam.ReadI32(unchecked((int)0x801FF018)) & 2) != 0)
        {
            local_58 = 4;
        }

        iVar12 = 0;

        // local_30 = (ushort *)local_68 — the same block, walked as a cursor. Kept as the byte
        // offset the original increments (iVar11 += 2, puVar6 += 1 on a ushort *).
        int local_30 = 0;
        int local_40 = 0;
        int local_48 = 0;
        do
        {
            uVar5 = PadInput.FUN_80026208(4);
            SELECT_EXE_exe.DAT_80055b6c = (int)(uVar5 & 0xffff);
            if (SELECT_EXE_exe.DAT_80055b6c == 0)
            {
                iVar12 = 0;
                local_48 = 1;
                local_40 = 0;
            }

            if ((uVar5 & 0x20) != 0)
            {
                local_50 = 0;
            }

            bool bMove;
            if (0xc < local_40)
            {
                iVar12 = iVar12 + 1;
                bMove = iVar12 == 1;
            }
            else
            {
                bMove = false;
            }

            if (!bMove)
            {
                bMove = local_48 != 0 && SELECT_EXE_exe.DAT_80055b6c != 0;
            }

            if (bMove)
            {
                if (local_48 != 0)
                {
                    local_48 = 0;
                }

                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[DAT_80055a0c + 0x15].r = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[DAT_80055a0c + 0x15].g = 0x40;
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[DAT_80055a0c + 0x15].b = 0x40;
                iVar11 = DAT_80055a0c;
                uVar5 = (uint)(SELECT_EXE_exe.DAT_80055b6c & 0x4000);
                SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[DAT_80055a0c + 0x19].attribute = 0x80000000;
                if (uVar5 != 0)
                {
                    DAT_80055a0c = iVar11 + 1;
                    if (local_58 + -1 < iVar11 + 1)
                    {
                        DAT_80055a0c = 0;
                    }
                }

                if ((SELECT_EXE_exe.DAT_80055b6c & 0x1000) != 0)
                {
                    DAT_80055a0c = DAT_80055a0c + -1;
                    if (DAT_80055a0c < 0)
                    {
                        DAT_80055a0c = local_58 + -1;
                    }
                }

                if ((SELECT_EXE_exe.DAT_80055b6c & 0x20) != 0)
                {
                    local_50 = 0;
                }
            }

            if (0xc < local_40 + 1)
            {
                iVar12 = iVar12 % 5;
            }

            int local_38 = (short)uVar13;
            iVar11 = 0;
            local_40 = local_40 + 1;
            uVar14 = __floatsidf(local_38);
            piVar10 = 0;
            do
            {
                // -sin(angle) * radius / 4096 * (bVar2 ? 0.8 : 1.2) -> leader.x
                uVar15 = __floatsidf(-DAT_8004f464[local_68[(iVar11 + local_30) >> 1]]);
                uVar15 = __muldf3(uVar15, uVar14);
                uVar15 = __divdf3(uVar15, 4096.0);
                iVar9 = ReadI32(SelectScreen.DAT_800593b8, piVar10);
                uVar7 = 1.2;
                if (bVar2)
                {
                    uVar7 = 0.8;
                }

                uVar15 = __muldf3(uVar15, uVar7);
                SpriteAtAddress(iVar9).x = (short)__fixdfsi(uVar15);

                // &DAT_8004f518 is the same table ninety entries in, i.e. cos(angle).
                uVar15 = __floatsidf(DAT_8004f464[90 + local_68[(iVar11 + local_30) >> 1]]);
                uVar16 = __floatsidf(local_38);
                uVar15 = __muldf3(uVar15, uVar16);
                uVar15 = __divdf3(uVar15, 4096.0);
                iVar9 = ReadI32(SelectScreen.DAT_800593b8, piVar10);
                uVar7 = 0.8;
                if (bVar2)
                {
                    uVar7 = 1.2;
                }

                piVar10 = piVar10 + 0xc;
                uVar15 = __muldf3(uVar15, uVar7);
                SpriteAtAddress(iVar9).y = (short)__fixdfsi(uVar15);
                iVar11 = iVar11 + 2;

                // `while ((int)piVar10 < -0x7ffa6bf4)` — 0x8005940C, which is 0x800593B8 + 7 * 12.
            }
            while (piVar10 < 7 * 0xc);

            uVar14 = __floatsidf((short)uVar13);
            if (bVar2)
            {
                // 0xC0738000_00000000 = -312.0
                iVar11 = __ltdf2(uVar14, -312.0);
            }
            else
            {
                // 0xC0704000_00000000 = -260.0
                iVar11 = __ltdf2(uVar14, -260.0);
            }

            uVar5 = uVar13;
            if (iVar11 < 0)
            {
                uVar5 = 0x104;
                if (bVar2)
                {
                    uVar5 = 0x138;
                }
            }

            iVar11 = 0;
            puVar6 = local_30;
            if ((uVar5 & 0xffff) == 0)
            {
                bVar2 = !bVar2;
            }

            do
            {
                local_68[puVar6 >> 1] = (ushort)(local_68[puVar6 >> 1] + 2);
                iVar11 = iVar11 + 1;
                if (0x167 < local_68[puVar6 >> 1])
                {
                    local_68[puVar6 >> 1] = 0;
                }

                puVar6 = puVar6 + 2;
            }
            while (iVar11 < 7);

            uVar13 = uVar5 - 1;
            FUN_80033630();
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[DAT_80055a0c + 0x15].r = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[DAT_80055a0c + 0x15].g = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[DAT_80055a0c + 0x15].b = 0x80;
            SELECT_EXE_exe.GsSPRITE_ARRAY_800654ec[DAT_80055a0c + 0x19].attribute = 0x1000000;
            FrameStep.FUN_800344a4();
            uVar5 = uVar5 + 0x102;
        }
        while (local_50 != 0);

        // THE OUTRO. Same seven-chain body, but the radius now runs AWAY from zero by eight a frame,
        // in whichever direction it already had, and the loop ends when radius + 0x103 no longer
        // fits under 0x207 as a halfword — that is, when |radius| has passed 260.
        while (true)
        {
            sVar4 = (short)uVar13;
            if (0x207 <= (uVar5 & 0xffff))
            {
                break;
            }

            uVar14 = __floatsidf(sVar4);
            piVar10 = 0;
            iVar12 = 0;
            do
            {
                uVar15 = __floatsidf(-DAT_8004f464[local_68[iVar12 >> 1]]);
                uVar15 = __muldf3(uVar15, uVar14);
                uVar15 = __divdf3(uVar15, 4096.0);
                iVar11 = ReadI32(SelectScreen.DAT_800593b8, piVar10);
                uVar7 = 1.2;
                if (bVar2)
                {
                    uVar7 = 0.8;
                }

                uVar15 = __muldf3(uVar15, uVar7);
                SpriteAtAddress(iVar11).x = (short)__fixdfsi(uVar15);
                uVar15 = __floatsidf(DAT_8004f464[90 + local_68[iVar12 >> 1]]);
                uVar16 = __floatsidf(sVar4);
                uVar15 = __muldf3(uVar15, uVar16);
                uVar15 = __divdf3(uVar15, 4096.0);
                iVar11 = ReadI32(SelectScreen.DAT_800593b8, piVar10);
                uVar7 = 0.8;
                if (bVar2)
                {
                    uVar7 = 1.2;
                }

                piVar10 = piVar10 + 0xc;
                uVar15 = __muldf3(uVar15, uVar7);
                SpriteAtAddress(iVar11).y = (short)__fixdfsi(uVar15);
                iVar12 = iVar12 + 2;
            }
            while (piVar10 < 7 * 0xc);

            iVar12 = sVar4;
            uVar13 = (uint)(iVar12 + 8);
            if (iVar12 < 0)
            {
                uVar13 = (uint)(iVar12 - 8);
            }

            FUN_80033630();
            FrameStep.FUN_800344a4();
            uVar5 = uVar13 + 0x103;
        }

        return DAT_80055a0c;
    }
}
