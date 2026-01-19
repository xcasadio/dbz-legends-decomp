/**
 * DBZ Legends - Title screen overlay (TITLE.EXE)
 * 
 * Title screen display and menu handling.
 */

#include "common.h"
#include "game.h"
#include "psxsdk/libcd.h"
#include "psxsdk/libetc.h"
#include "psxsdk/libspu.h"
#include "psxsdk/kernel.h"

#define ReadFile FUN_80057df4

/* External game functions from TITLE.EXE */
extern void FUN_80070b64(void);           /* 0x80070b64 - callback reset? */
extern void __main(void);
extern void InitCARD(long val);
extern long StartCARD(void);
extern long _card_auto(long val);
extern void ChangeClearPAD(long val);
extern void FUN_80057e40(CdlFILE* cdlFile, u8* buffer, u16 mode);        /* 0x80057e40 */
void FUN_80057df4(char* fileName, u8* buffer, u16 mode);                 /* 0x80057df4 */
extern void FUN_80070e44(void);           /* 0x80070e44 */
extern void FUN_800742cc(s32 arg0, s32 arg1);  /* 0x800742cc */
extern s32 FUN_80074370(s32 arg0, s32 arg1, s32 arg2, s32 arg3, s32 arg4, s32 arg5);  /* 0x80074370 */
extern void FUN_80057674(s32 arg0, s32 arg1, s32 arg2, s32 arg3, s32 arg4, s32 arg5, s32 arg6, s32 arg7, s32 arg8, s32 arg9);  /* 0x80057674 */
extern void* FUN_80049504(void* callback, s32 arg1, s32 arg2, s32 arg3, s32 arg4, s32 arg5);  /* 0x80049504 */
extern void FUN_80057c80(void* arg0);     /* 0x80057c80 */
extern void FUN_80037388(void);           /* 0x80037388 */
extern void FUN_80056dc0(s32 arg0, s32 arg1, s32 arg2, s32 arg3, s32 arg4, s32 arg5, s32 arg6, s32 arg7);  /* 0x80056dc0 */
extern void FUN_80038228(s32 arg0, s32 arg1);  /* 0x80038228 */
extern void FUN_80058d64(void);           /* 0x80058d64 */
extern void FUN_80021dd0(void);           /* 0x80021dd0 */
extern void FUN_80022c94(void);           /* 0x80022c94 */
extern void FUN_80023290(void);           /* 0x80023290 */
extern void FUN_800587a8(void);           /* 0x800587a8 */
extern void FUN_80058a9c(void);           /* 0x80058a9c */
extern s32 FUN_8005c9d8(s16 arg0);        /* 0x8005c9d8 */
extern void FUN_800607fc(s16 arg0, u16 arg1, u16 arg2, s32 arg3);  /* 0x800607fc */
extern void FUN_80064168(s16 arg0, s32 arg1);  /* 0x80064168 */
extern s32 FUN_80064368(s32 arg0, s16 arg1, s32 arg2, s32 arg3);   /* 0x80064368 */
extern void FUN_80067c74(s16 arg0, s32 arg1);  /* 0x80067c74 */
extern void FUN_800678b4(s16 arg0, s32 arg1, u8 arg2, s16 arg3);   /* 0x800678b4 */
extern void FUN_80068e34(s16 arg0, s32 arg1);  /* 0x80068e34 */
extern s32 FUN_8003bcc4(s16 arg0);             /* 0x8003bcc4 */
extern void FUN_8004be40(void* arg0, u16 arg1); /* 0x8004be40 */
extern void FUN_80050744(void* arg0);          /* 0x80050744 */
extern void FUN_8005286c(void* arg0);          /* 0x8005286c */
extern void FUN_80062760(s32 arg0, s32 arg1);  /* 0x80062760 */
extern void FUN_800627f8(s16 arg0);            /* 0x800627f8 */
extern void FUN_80062838(s16 arg0);            /* 0x80062838 */
extern void SpuSetReverbModeParam(SpuReverbAttr* attr); /* 0x80062878 */
extern s32 FUN_80070bc4(const char* arg0);     /* 0x80070bc4 */
extern s32 FUN_8006bc88(void* arg0, void* arg1); /* 0x8006bc88 */
extern void FUN_80070e34(void* arg0);           /* 0x80070e34 */
extern void FUN_8003de38(void* arg0, s32 arg1); /* 0x8003de38 */
extern s32 FUN_80027174(void);                  /* 0x80027174 */
extern void FUN_80030ec4(void);                 /* 0x80030ec4 */

/* External global variables - need to match exact addresses */
extern u32 DAT_80083498;   /* Result from FUN_80074370 */
extern s32 DAT_8008344c;
extern s32 DAT_80083450;
extern s32 DAT_80083448;
extern u32 DAT_80083504;   /* cleared in loop */
extern s32 DAT_80083544;
extern s32 DAT_800835b4;
extern s32 PTR_80079854;
extern s32 PTR_800798bc;
extern s32 PTR_800798dc;
extern s32 DAT_800798d4;

extern CdlFILE DAT_800a8860;
extern u8 DAT_80110000;

extern void* DAT_80083224;

extern char DAT_800831b4[];
extern char DAT_800831bc[];

extern SpuReverbAttr DAT_80096650;

typedef struct {
    u32 unk0;
    u32 unk4;
    u8 pad_08[0x18];
    void* ptr_20;
} Unk_800836d4_Entry;

extern Unk_800836d4_Entry UnkStruct_Array_8004bf94[30];

extern volatile u16 DAT_801ff10e;
extern volatile u16 DAT_801ff100;
extern volatile u32 DAT_1f80012c;

extern s32 DAT_80110004;   /* 0x80110004 - global accessed by FUN_80021dd0 */
extern s32 DAT_800898c0;   /* 0x800898c0 - global accessed by FUN_80021dd0 */

extern void* DAT_gp_0314;  /* GP + 0x314 = 788 */
extern void* DAT_gp_0318;  /* GP + 0x318 = 792 */
extern void* DAT_gp_031c;  /* GP + 0x31C = 796 */
extern void* DAT_gp_0320;  /* GP + 0x320 = 800 */
extern void* DAT_gp_0338;  /* GP + 0x338 = 824 */
extern void* DAT_gp_033c;  /* GP + 0x33C = 828 */
extern void* DAT_gp_0340;  /* GP + 0x340 = 832 */
extern void* DAT_gp_0344;  /* GP + 0x344 = 836 */

extern s16 DAT_gp_02a4;    /* GP + 0x2A4 = 676 */
extern s16 DAT_gp_02dc;    /* GP + 0x2DC = 732 */

/* ============================================================================
 * FUN_80053020 - 0x80053020, size: 0x28 (40 bytes)
 * EQUIVALENT - Returns field based on struct comparison
 * Structure: side_balance_302C compared to 0x7530, returns cursor_left or cursor_right
 * (Branch condition inverted by compiler)
 * ============================================================================ */
u16 FUN_80053020(TitleMenuState* state) {
    if (state->side_balance_302C == 0x7530) {
        return state->cursor_left;
    }
    return state->cursor_right;
}

/* ============================================================================
 * FUN_8004dbac - 0x8004DBAC, size: 0x3C (60 bytes)
 * EQUIVALENT - Loads ptr at DAT_80083224->+8 and calls two routines on it
 * ============================================================================ */
void FUN_8004dbac(void) {
    void* ptr = *(void**)((u8*)DAT_80083224 + 8);

    FUN_80050744(ptr);
    FUN_8005286c(ptr);
}

/* ============================================================================
 * FUN_8004bf94 - 0x8004BF94, size: 0x3C (60 bytes)
 * EQUIVALENT - Reads s16 at +0x11E, transforms it, then calls FUN_8004be40
 * ============================================================================ */
typedef struct {
    u8 pad_00[4];
    u16 unk_04;
    u8 pad_06[0x92];
    void* unk_98;
    u8 pad_9C[0x78];
    RECT rect_114;
    u8 unk_11C[2];
    s16 value_11E;
    u8 pad_120[0x14];
    u32 flags_134;
    u32 flags_138;
    u32 unk_13C;
    u32 unk_140;
    u8 pad_144[0x0C];
    u8 color_r_150;
    u8 color_g_151;
    u8 color_b_152;
    u8 pad_153[3];
    u16 field_156;
    u16 field_158;
    u16 field_15A;
    u8 pad_15C[0x0E];
    u8 code_16A;
    u8 pad_16B[0x08];
    u8 code_173;
} UnkStruct_8004bf94;

void FUN_8004bf94(UnkStruct_8004bf94* arg0) {
    s16 value = arg0->value_11E;
    u16 result = (u16)FUN_8003bcc4(value);

    FUN_8004be40(arg0, result);
}

/* ============================================================================
 * FUN_80044754 - 0x80044754, size: 0x30 (48 bytes)
 * EQUIVALENT - Clears bit 0x20000000 at +0x134, clears byte at +0x224
 * ============================================================================ */
typedef struct {
    u8 pad_000[0x134];
    u32 flags_134;
    u8 pad_138[0x224 - 0x138];
    u8 byte_224;
} UnkStruct_80044754;

void FUN_80044754(UnkStruct_80044754* arg0) {
    arg0->flags_134 &= 0xDFFFFFFF;
    arg0->byte_224 = 0;
}

/* ============================================================================
 * FUN_8006420c - 0x8006420C, size: 0x28 (40 bytes)
 * EQUIVALENT - Sign-extends s16 arg and calls FUN_80064168(arg0, 0)
 * Note: compiler uses subu/addu for stack adjust vs addiu.
 * ============================================================================ */
void FUN_8006420c(s16 arg0) {
    FUN_80064168(arg0, 0);
}

/* ============================================================================
 * FUN_80064300 - 0x80064300, size: 0x34 (52 bytes)
 * EQUIVALENT - Sign-extends s16 arg1, calls FUN_80064368(arg0,arg1,1,arg2), sign-extends return
 * Notes: stack adjust differs (subu/addu vs addiu)
 * ============================================================================ */
s16 FUN_80064300(s32 arg0, s32 arg1, s32 arg2) {
    return (s16)FUN_80064368(arg0, (s16)arg1, 1, arg2);
}

/* ============================================================================
 * FUN_80064a78 - 0x80064A78, size: 0x50 (80 bytes)
 * EQUIVALENT - Sets main SPU volume L/R to (s16)arg*129 via SpuSetCommonAttr
 * Notes: uses sll/sra sign-extend + (x<<7)+x multiply pattern
 * ============================================================================ */
void FUN_80064a78(s32 arg0, s32 arg1) {
    SpuCommonAttr attr;
    s32 vol_l = (s16)arg0;
    s32 vol_r = (s16)arg1;

    attr.mask = 3;
    attr.mvol_l = (s16)((vol_l << 7) + vol_l);
    attr.mvol_r = (s16)((vol_r << 7) + vol_r);

    SpuSetCommonAttr(&attr);
}

/* ============================================================================
 * FUN_80067c48 - 0x80067C48, size: 0x2C (44 bytes)
 * EQUIVALENT - Sign-extends s16 arg, calls FUN_8005c9d8, sign-extends return
 * Notes: stack adjust differs (subu/addu vs addiu)
 * ============================================================================ */
s16 FUN_80067c48(s32 arg0) {
    return (s16)FUN_8005c9d8((s16)arg0);
}

/* ============================================================================
 * FUN_80067de0 - 0x80067DE0, size: 0x28 (40 bytes)
 * EQUIVALENT - Sign-extends s16 arg and calls FUN_80067c74(arg0, 0)
 * Notes: stack adjust differs (subu/addu vs addiu)
 * ============================================================================ */
void FUN_80067de0(s16 arg0) {
    FUN_80067c74(arg0, 0);
}

/* ============================================================================
 * FUN_80068f0c - 0x80068F0C, size: 0x28 (40 bytes)
 * EQUIVALENT - Sign-extends s16 arg and calls FUN_80068e34(arg0, 0)
 * Notes: stack adjust differs (subu/addu vs addiu)
 * ============================================================================ */
void FUN_80068f0c(s16 arg0) {
    FUN_80068e34(arg0, 0);
}

/* ============================================================================
 * FUN_80023320 - 0x80023320, size: 0x28 (40 bytes)
 * EQUIVALENT - Clears two GP vars and calls FUN_8002339c
 * ============================================================================ */
extern s16 DAT_80083314;  /* GP + 0x160 = 352 */
extern s16 DAT_80083318;  /* GP + 0x164 = 356 */
extern void FUN_8002339c(void);

void FUN_80023320(void) {
    DAT_80083314 = 0;
    DAT_80083318 = 0;
    FUN_8002339c();
}

/* ============================================================================
 * FUN_80023348 - 0x80023348, size: 0x2C (44 bytes)
 * EQUIVALENT - Sets GP+0x160 to 1, clears GP+0x164, then calls FUN_8002339c
 * ============================================================================ */
void FUN_80023348(void) {
    DAT_80083314 = 1;
    DAT_80083318 = 0;
    FUN_8002339c();
}

/* ============================================================================
 * FUN_80023374 - 0x80023374, size: 0x28 (40 bytes)
 * MATCHING - Sets GP+0x164 to 1 and calls FUN_8002339c
 * ============================================================================ */
void FUN_80023374(void) {
    DAT_80083318 = 1;
    FUN_8002339c();
}

/* ============================================================================
 * FUN_80068a2c - 0x80068A2C, size: 0x24 (36 bytes)
 * MATCHING - Wrapper that sign-extends arg and calls FUN_800688b0
 * ============================================================================ */
extern void FUN_800688b0(s16 arg0);

void FUN_80068a2c(s16 value) {
    FUN_800688b0(value);
}

/* ============================================================================
 * FUN_8005c974 - 0x8005C974, size: 0x24 (36 bytes)
 * EQUIVALENT - Sets DAT_8007b000 if different from arg
 * (Compiler optimizes delay slot differently)
 * ============================================================================ */
extern s32 DAT_8007b000;
extern s32 DAT_8007affc;

void FUN_8005c974(s32 value) {
    if (value != DAT_8007b000) {
        DAT_8007b000 = value;
    }
}

/* ============================================================================
 * FUN_8005d248 - 0x8005D248, size: 0x2C (44 bytes)
 * EQUIVALENT - Sets DAT_8007affc to 0 if arg0 == 1, else sets it to 1
 * Notes: compiler may schedule the store in the branch delay slot.
 * ============================================================================ */
void FUN_8005d248(s32 value) {
    if (value == 1) {
        DAT_8007affc = 0;
        return;
    }

    DAT_8007affc = 1;
}

/* ============================================================================
 * FUN_80056af4 - 0x80056AF4, size: 0x3C (60 bytes)
 * EQUIVALENT - Calls a small sequence of reset/config routines
 * ============================================================================ */
void FUN_80056af4(void) {
    FUN_80062760(0, 0);
    FUN_800627f8(0);
    FUN_80062838(0);
    FUN_8006268c();
}

/* ============================================================================
 * FUN_80057508 - 0x80057508, size: 0x4C (76 bytes)
 * EQUIVALENT - Clears full screen to black via ClearImage + DrawSync
 * ============================================================================ */
void FUN_80057508(void) {
    RECT rect;

    rect.w = 0x400;
    rect.h = 0x200;
    rect.x = 0;
    rect.y = 0;

    ClearImage(&rect, 0, 0, 0);
    DrawSync(0);
}

/* ============================================================================
 * FUN_80054dd0 - 0x80054DD0, size: 0x24 (36 bytes)
 * MATCHING - Sets field at offset 0x110 if zero
 * Structure access: DAT_gp_018c->cd.timer_110
 * ============================================================================ */
extern TitleAudioBlock* DAT_gp_018c;

void FUN_80054dd0(void) {
    if (DAT_gp_018c->cd.timer_110 == 0) {
        DAT_gp_018c->cd.timer_110 = 0x14;
    }
}

/* ============================================================================
 * FUN_80054d9c - 0x80054D9C, size: 0x34 (52 bytes)
 * EQUIVALENT - Returns DAT_gp_018c->cd.timer_110 if < 0x10 (signed), else 0
 * ============================================================================ */
s16 FUN_80054d9c(void) {
    s16 timer_s = (s16)DAT_gp_018c->cd.timer_110;
    u16 timer_u = (u16)DAT_gp_018c->cd.timer_110;

    if (timer_s >= 0x10) {
        timer_u = 0;
    }

    return (s16)timer_u;
}

/* ============================================================================
 * FUN_80063714 - 0x80063714, size: 0x30 (48 bytes)
 * EQUIVALENT - Sign-extends s16 arg0, masks arg1/arg2 to u16, calls FUN_800607fc
 * ============================================================================ */
void FUN_80063714(s32 arg0, s32 arg1, s32 arg2) {
    FUN_800607fc((s16)arg0, (u16)arg1, (u16)arg2, 0);
}

/* ============================================================================
 * FUN_800638fc - 0x800638FC, size: 0x20 (32 bytes)
 * MATCHING - Calls FUN_800637b0(0)
 * ============================================================================ */
extern void FUN_800637b0(s32 arg0);

void FUN_800638fc(void) {
    FUN_800637b0(0);
}

/* ============================================================================
 * FUN_80063f2c - 0x80063F2C, size: 0x20 (32 bytes)
 * MATCHING - Calls FUN_80063c9c(1)
 * ============================================================================ */
extern void FUN_80063c9c(s32 arg0);

void FUN_80063f2c(void) {
    FUN_80063c9c(1);
}

/* ============================================================================
 * FUN_80064010 - 0x80064010, size: 0x20 (32 bytes)
 * MATCHING - Calls FUN_800640ec()
 * ============================================================================ */
extern void FUN_800640ec(void);

void FUN_80064010(void) {
    FUN_800640ec();
}

/* ============================================================================
 * FUN_800640ac - 0x800640AC, size: 0x20 (32 bytes)
 * MATCHING - Calls FUN_8005c214(1)
 * ============================================================================ */
extern void FUN_8005c214(s32 arg0);

void FUN_800640ac(void) {
    FUN_8005c214(1);
}

/* ============================================================================
 * FUN_800640cc - 0x800640CC, size: 0x20 (32 bytes)
 * MATCHING - Calls FUN_8005c214(0)
 * ============================================================================ */
void FUN_800640cc(void) {
    FUN_8005c214(0);
}

/* ============================================================================
 * FUN_8006268c - 0x8006268C, size: 0x20 (32 bytes)
 * MATCHING - Calls FUN_800634e0(0)
 * ============================================================================ */
extern void FUN_800634e0(s32 arg0);

void FUN_8006268c(void) {
    FUN_800634e0(0);
}

/* ============================================================================
 * FUN_800627f8 - 0x800627F8, size: 0x40 (64 bytes)
 * EQUIVALENT - Writes request mask 0x10 and feedback, then calls SpuSetReverbModeParam
 * ============================================================================ */
void FUN_800627f8(s16 arg0) {
    DAT_80096650.mask = 0x10;
    DAT_80096650.feedback = (s32)arg0;
    SpuSetReverbModeParam(&DAT_80096650);
}

/* ============================================================================
 * FUN_80062838 - 0x80062838, size: 0x40 (64 bytes)
 * EQUIVALENT - Writes request mask 0x8 and delay, then calls SpuSetReverbModeParam
 * ============================================================================ */
void FUN_80062838(s16 arg0) {
    DAT_80096650.mask = 0x8;
    DAT_80096650.delay = (s32)arg0;
    SpuSetReverbModeParam(&DAT_80096650);
}

/* ============================================================================
 * FUN_8006767c - 0x8006767C, size: 0x14 (20 bytes)
 * MATCHING - Sets DAT_800a6768 to 2
 * ============================================================================ */
extern s16 DAT_800a6768;  /* Shared with FUN_800678a4 */

void FUN_8006767c(void) {
    DAT_800a6768 = 2;
}

/* ============================================================================
 * FUN_80064260 - 0x80064260, size: 0x14 (20 bytes)
 * MATCHING - Sets DAT_800a8834 to 1
 * ============================================================================ */
extern s16 DAT_800a8834;  /* Shared with FUN_80064274 */

void FUN_80064260(void) {
    DAT_800a8834 = 1;
}

/* ============================================================================
 * FUN_80070e44 - 0x80070E44, size: 0x10 (16 bytes)
 * MATCHING - Syscall with a0=2 (ExitCriticalSection)
 * ============================================================================ */
void FUN_80070e44(void) {
    ExitCriticalSection();
}

/* ============================================================================
 * FUN_80070b64 - 0x80070B64, size: 0x10 (16 bytes)
 * MATCHING - Syscall with a0=1 (EnterCriticalSection)
 * ============================================================================ */
void FUN_80070b64(void) {
    EnterCriticalSection();
}

/* ============================================================================
 * FUN_8006fe58 - 0x8006FE58, size: 0x10 (16 bytes)
 * MATCHING - Getter for DAT_800813a4
 * ============================================================================ */
extern s32 DAT_800813a4;

s32 FUN_8006fe58(void) {
    return DAT_800813a4;
}

/* ============================================================================
 * FUN_800678a4 - 0x800678A4, size: 0x10 (16 bytes)
 * MATCHING - Sets DAT_800a6768 to 0
 * ============================================================================ */
/* extern s16 DAT_800a6768; - declared above in FUN_8006767c */

void FUN_800678a4(void) {
    DAT_800a6768 = 0;
}

/* ============================================================================
 * FUN_800679b4 - 0x800679B4, size: 0x38 (56 bytes)
 * EQUIVALENT - Sign-extends s16 arg0/arg2, masks arg1 to u8, calls FUN_800678b4(arg0,0,arg1,arg2)
 * ============================================================================ */
void FUN_800679b4(s32 arg0, s32 arg1, s32 arg2) {
    FUN_800678b4((s16)arg0, 0, (u8)arg1, (s16)arg2);
}

/* ============================================================================
 * FUN_800642bc - 0x800642BC, size: 0x10 (16 bytes)
 * MATCHING - Setter for DAT_800acd9c (byte)
 * ============================================================================ */
extern u8 DAT_800acd9c;

void FUN_800642bc(u8 value) {
    DAT_800acd9c = value;
}

/* ============================================================================
 * FUN_80064274 - 0x80064274, size: 0x10 (16 bytes)
 * MATCHING - Sets DAT_800a8834 to 0
 * ============================================================================ */
/* extern s16 DAT_800a8834; - declared above in FUN_80064260 */

void FUN_80064274(void) {
    DAT_800a8834 = 0;
}

/* ============================================================================
 * FUN_8005d274 - 0x8005D274, size: 0x10 (16 bytes)
 * MATCHING - Returns true if DAT_8007affc == 0
 * ============================================================================ */
s32 FUN_8005d274(void) {
    return DAT_8007affc == 0;
}

/* ============================================================================
 * FUN_800561c8 - 0x800561C8, size: 0xC (12 bytes)
 * MATCHING - Getter for GP-relative global (offset 0x28C = 652)
 * ============================================================================ */
extern s16 DAT_gp_028c;  /* GP + 0x28C */

s16 FUN_800561c8(void) {
    return DAT_gp_028c;
}

/* ============================================================================
 * FUN_8005329c - 0x8005329C, size: 0x8 (8 bytes)
 * MATCHING - Empty function (just jr ra + nop)
 * ============================================================================ */
void FUN_8005329c(void) {
}

/* ============================================================================
 * FUN_80021dd0 - 0x80021DD0, size: 0x58 (88 bytes)
 * EQUIVALENT - Logic matches, minor instruction order difference
 * Original: lui a1,0x8011 / lui v0,0x8011 / lw / lui a0,0x8011 / sw ra / jal / addu
 * Compiled: subu sp / lw / li a0 / sw ra / jal / addu
 * ============================================================================ */
void FUN_80021dd0(void) {
    FUN_80057c80((void*)(DAT_80110004 + 0x80110000));
    FUN_80049504((void*)0x80021e28, 0, 6, 0x70, 0, DAT_800898c0);
}

/* ============================================================================
 * FUN_80022630 - 0x80022630, size: 0x50 (80 bytes)
 * EQUIVALENT - Initializes memory card handling and input clearing
 * ============================================================================ */
void FUN_80022630(void) {
    InitCARD(1);
    StartCARD();
    _bu_init();
    FUN_80022c94();
    _card_auto(0);
    ChangeClearPAD(0);
    FUN_80023290();
}

/* ============================================================================
 * FUN_800229e0 - 0x800229E0, size: 0x40 (64 bytes)
 * EQUIVALENT - Calls FUN_80070bc4 with one of two strings; returns (ret == 0)
 * ============================================================================ */
s32 FUN_800229e0(s32 arg0) {
    const char* str = arg0 ? DAT_800831bc : DAT_800831b4;
    return FUN_80070bc4(str) == 0;
}

/* ============================================================================
 * FUN_800469a4 - 0x800469A4, size: 0x54 (84 bytes)
 * EQUIVALENT - Calls FUN_8003de38, FUN_80027314, then sets flags_138 to 0x4000000
 * ============================================================================ */
void FUN_800469a4(UnkStruct_8004bf94* arg0) {
    FUN_8003de38(arg0, 0x22);
    FUN_80027314(arg0);
    arg0->flags_138 = 0x4000000;
}

/* ============================================================================
 * FUN_8002cd70 - 0x8002CD70, size: 0x54 (84 bytes)
 * EQUIVALENT - Initializes several fields then calls FUN_80032434
 * ============================================================================ */
typedef struct {
    s16 mode_00;
    u8 pad_002[0x10 - 0x02];
    u32 field_10;
    u32 field_14;
    u32 field_18;
    u8 pad_01C[0x20 - 0x1C];
    s16 state_20;
    u8 pad_022[0x2A - 0x22];
    s16 field_2A;
    s16 field_2C;
    s16 field_2E;
    s16 field_30;
    s16 field_32;
} UnkStruct_8002cd70;

extern void FUN_8002cdc4(UnkStruct_8002cd70* arg0); /* 0x8002cdc4 */
extern void FUN_80032434(UnkStruct_8002cd70* arg0); /* 0x80032434 */

void FUN_8002cd70(UnkStruct_8002cd70* arg0) {
    FUN_8002cdc4(arg0);

    arg0->field_2C = -0x250;
    arg0->field_2E = 0x250;
    arg0->field_30 = 0;
    arg0->field_32 = 0;

    FUN_80032434(arg0);

    arg0->field_2A = 0;
    arg0->mode_00 = 2;
}

/* ============================================================================
 * FUN_80030e6c - 0x80030E6C, size: 0x58 (88 bytes)
 * EQUIVALENT - Initializes several fields then calls FUN_80032434
 * Uses same struct layout as FUN_8002cd70
 * ============================================================================ */
void FUN_80030e6c(UnkStruct_8002cd70* arg0) {
    FUN_80030ec4();

    arg0->field_2C = -0x250;
    arg0->field_2E = 0x250;
    arg0->field_30 = 0;
    arg0->field_32 = 0;

    FUN_80032434(arg0);

    arg0->field_2A = 0;
    arg0->mode_00 = 2;
}

/* ============================================================================
 * FUN_80040c18 - 0x80040C18, size: 0x58 (88 bytes)
 * EQUIVALENT - Calls FUN_8003de38 and sets bit 1 in flags_138
 * ============================================================================ */
void FUN_80040c18(UnkStruct_8004bf94* arg0) {
    FUN_8003de38(arg0, 0x21);
    arg0->flags_138 |= 2;
}

/* ============================================================================
 * FUN_8004247c - 0x8004247C, size: 0x58 (88 bytes)
 * EQUIVALENT - Calls FUN_8003de38 and sets bit 15 in flags_138
 * ============================================================================ */
void FUN_8004247c(UnkStruct_8004bf94* arg0) {
    FUN_8003de38(arg0, 0x1F);
    arg0->flags_138 |= 0x8000;
}

/* ============================================================================
 * FUN_8003287c - 0x8003287C, size: 0x5C (92 bytes)
 * EQUIVALENT - Updates fields at +0x2C and +0x2E, sets mode to 5 when both are zero
 * ============================================================================ */
typedef struct {
    s16 mode_00;
    u8 pad_002[0x2C - 0x02];
    s16 field_2C;
    s16 field_2E;
} UnkStruct_8003287c;

void FUN_8003287c(UnkStruct_8003287c* arg0) {
    s16 val1;
    s16 val2;

    val1 = arg0->field_2C;
    arg0->field_2C = val1 + 0x80;
    if (arg0->field_2C >= 0) {
        arg0->field_2C = 0;
    }

    val2 = arg0->field_2E;
    arg0->field_2E = val2 - 0x80;
    if (arg0->field_2E <= 0) {
        arg0->field_2E = 0;
    }

    if ((arg0->field_2C | arg0->field_2E) == 0) {
        arg0->mode_00 = 5;
    }
}

/* ============================================================================
 * FUN_80040bbc - 0x80040BBC, size: 0x5C (92 bytes)
 * EQUIVALENT - Calls FUN_8003de38 with parameter and sets bit 4 in flags_138
 * ============================================================================ */
void FUN_80040bbc(UnkStruct_8004bf94* arg0, s32 arg1) {
    FUN_8003de38(arg0, arg1);
    arg0->flags_138 |= 0x10;
}

/* ============================================================================
 * FUN_80056624 - 0x80056624, size: 0x5C (92 bytes)
 * EQUIVALENT - Sends CD control commands 0x0A and 0x08, resets DAT_8008335c
 * ============================================================================ */
extern s32 DAT_8008335c;
extern u32 DAT_800833fc;

void FUN_80056624(void) {
    s32 result;

    do {
        result = CdControlB(0x0A, (u_char*)0, (u_char*)0);
    } while (result == 0);

    do {
        result = CdControlB(0x08, (u_char*)0, (u_char*)0);
    } while (result == 0);

    DAT_8008335c = 0;
    DAT_800833fc &= 0xFFFFFFFD;
}

/* ============================================================================
 * FUN_80067188 - 0x80067188, size: 0x5C (92 bytes)
 * EQUIVALENT - Extracts bit fields from two u32 params into u16 array
 * ============================================================================ */
void FUN_80067188(u32 arg0, u32 arg1, u16* arg2) {
    u16 val1;

    arg2[5] = (u16)arg0 & 0x8000;
    val1 = (u16)arg1;
    arg2[6] = val1 & 0x8000;
    arg2[8] = val1 & 0x4000;
    arg2[7] = val1 & 0x20;
    arg2[0] = (u16)((arg0 & 0xFFFF) >> 8) & 0x7F;
    arg2[1] = (u16)((arg0 & 0xFFFF) >> 4) & 0xF;
    arg2[2] = (u16)arg0 & 0xF;
    arg2[3] = (u16)(arg1 >> 6) & 0x7F;
    arg2[4] = val1 & 0x1F;
}

/* ============================================================================
 * FUN_80049b44 - 0x80049B44, size: 0x60 (96 bytes)
 * EQUIVALENT - Clears unk_04, computes offsets based on sign and index
 * ============================================================================ */
void FUN_80049b44(UnkStruct_8004bf94* arg0, s32 arg1, u32 arg2) {
    u32 value;
    s32 offset;

    arg0->unk_04 = 0;

    offset = arg1;
    if (arg1 >= 0) {
        offset = arg1 + *(s32*)arg0->pad_00;
    }

    value = *(u32*)(offset + (arg2 & 0xFFFF) * 4);
    *(u32*)(arg0->pad_06 + 2) = value;

    if (value < 0x80000000) {
        *(u32*)(arg0->pad_06 + 2) = value + *(s32*)arg0->pad_00;
    }

    arg0->pad_06[0] = 0;
    arg0->pad_06[1] = 0;
}

/* ============================================================================
 * FUN_80027314 - 0x80027314, size: 0x40 (64 bytes)
 * EQUIVALENT - Clears entries referencing arg0 in a 30-element table; clears byte at arg0+0x227
 * ============================================================================ */
void FUN_80027314(void* arg0) {
    s32 i;

    for (i = 0; i < 30; i++) {
        if (UnkStruct_Array_8004bf94[i].ptr_20 == arg0) {
            UnkStruct_Array_8004bf94[i].unk4 = 0;
            UnkStruct_Array_8004bf94[i].unk0 = 0;
        }
    }

    *(u8*)((u8*)arg0 + 0x227) = 0;
}

/* ============================================================================
 * FUN_80027354 - 0x80027354, size: 0x58 (88 bytes)
 * EQUIVALENT - Clears UnkStruct_Array_8004bf94 table (0x438 bytes) and creates object
 * ============================================================================ */
void FUN_80027354(void) {
    memset(UnkStruct_Array_8004bf94, 0, 0x438);
    FUN_80049504((void*)FUN_80027174, 0, 0xB, 0, 1, DAT_800798d4);
}

/* ============================================================================
 * FUN_80022c04 - 0x80022C04, size: 0x48 (72 bytes)
 * EQUIVALENT - Calls FUN_80070e34 on four GP-relative pointers
 * ============================================================================ */
void FUN_80022c04(void) {
    FUN_80070e34(DAT_gp_0314);
    FUN_80070e34(DAT_gp_0318);
    FUN_80070e34(DAT_gp_031c);
    FUN_80070e34(DAT_gp_0320);
}

/* ============================================================================
 * FUN_80022c4c - 0x80022C4C, size: 0x48 (72 bytes)
 * EQUIVALENT - Calls FUN_80070e34 on four GP-relative pointers
 * ============================================================================ */
void FUN_80022c4c(void) {
    FUN_80070e34(DAT_gp_0338);
    FUN_80070e34(DAT_gp_033c);
    FUN_80070e34(DAT_gp_0340);
    FUN_80070e34(DAT_gp_0344);
}

/* ============================================================================
 * FUN_80057f80 - 0x80057F80, size: 0x44 (68 bytes)
 * EQUIVALENT - Calls FUN_8006bc88(arg1, arg0) in a loop until it returns nonzero
 * ============================================================================ */
void FUN_80057f80(void* arg0, void* arg1) {
    while (FUN_8006bc88(arg1, arg0) == 0) {
    }
}

/* ============================================================================
 * FUN_80057df4 - 0x80057DF4, size: 0x4C (76 bytes)
 * EQUIVALENT - Waits for file info then reads CD data into buffer
 * Calls: FUN_80057f80(fileName, &cdlFile), FUN_80057e40(&cdlFile, buffer, mode)
 * ============================================================================ */
void FUN_80057df4(char* fileName, u8* buffer, u16 mode) {
    CdlFILE cdlFile;

    FUN_80057f80(fileName, &cdlFile);
    FUN_80057e40(&cdlFile, buffer, mode);
}

/* ============================================================================
 * FUN_800299bc - 0x800299BC, size: 0x4C (76 bytes)
 * EQUIVALENT - Creates/gets an object via FUN_80049504 and writes 2 to *(obj->+8)
 * ============================================================================ */
void FUN_800299bc(void) {
    void* obj;
    s32* field_08;

    obj = FUN_80049504((void*)0x80029aec, 0, 5, 0xC, 0, PTR_800798bc);
    field_08 = *(s32**)((u8*)obj + 8);
    *field_08 = 2;
}

/* ============================================================================
 * FUN_80037104 - 0x80037104, size: 0x48 (72 bytes)
 * EQUIVALENT - Stores arg0 into GP, clears another GP var, then calls FUN_80049504
 * ============================================================================ */
void FUN_80037104(s16 arg0) {
    DAT_gp_02a4 = arg0;
    DAT_gp_02dc = 0;

    FUN_80049504((void*)0x8003714c, 0, 0xD, 0xC, 0, PTR_800798dc);
}

/* Title main function - 0x800581DC, size: 0x20C (524 bytes)
 * EQUIVALENT - Decompiled from Ghidra; not yet assembly-matched.
 */
void main(void) {
    CdlFILE* cd_file;
    u32 seed;

    __main();
    FUN_80070b64();
    ResetCallback();
    ResetGraph(0);
    InitGeom();
    SetDispMask(0);
    FUN_80057508();
    PadInit(0);
    CdInit();

    do {
        cd_file = CdSearchFile(&DAT_800a8860, "\\SELECT.EXE;1");
    } while (cd_file == (CdlFILE*)0);

    ReadFile("\\SUB\\TITLE.B;1", &DAT_80110000, 0);

    seed = 0x10000;
    InitHeap((void*)0x10000, 0x10000);
    srand(seed);
    FUN_80070e44();
    FUN_800742cc(0x3c0, 0x100);

    DAT_80083498 = FUN_80074370(0x10, 0x10, 0x100, 200, 0, 0x200);
    DAT_8008344c = 0;
    DAT_80083450 = 0;
    DAT_80083448 = 0;

    FUN_80057674(0xa8, 0x80, 0x1000, 0, 0, 0, 0x1000, 0, 0, 0);
    FUN_80049504((void*)FUN_80037388, 0, 0, 0, 0, PTR_80079854);
    FUN_80037388();
    FUN_80056dc0(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0);
    DAT_80083544 = 0;

    FUN_80038228(8, 0);
    FUN_80058d64();

    while (1) {
        if (2 < DAT_801ff10e) {
            DAT_801ff10e = 0;
        }

        DAT_1f80012c = (u32)DAT_801ff10e;
        DAT_801ff100 = 2;
        DAT_801ff10e = DAT_801ff10e + 1;

        FUN_80038228(8, 0);
        DAT_800835b4 = 1;
        FUN_80021dd0();
        FUN_800587a8();
        FUN_80058a9c();

        FUN_80038228(2, 4);
        DAT_80083504 = 0;
        FUN_800587a8();
    }
}
