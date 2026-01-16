/**
 * DBZ Legends - Title screen overlay (TITLE.EXE)
 * 
 * Title screen display and menu handling.
 */

#include "common.h"
#include "game.h"
#include "psxsdk/libcd.h"
#include "psxsdk/libetc.h"
#include "psxsdk/kernel.h"

/* External game functions from TITLE.EXE */
extern void FUN_80070b64(void);           /* 0x80070b64 - callback reset? */
extern void FUN_80071648(s32 arg0);       /* 0x80071648 */
extern void FUN_80057508(void);           /* 0x80057508 */
extern void FUN_80071a4c(s32 arg0);       /* 0x80071a4c */
extern void FUN_80057df4(u8* arg0, u8* arg1, s32 arg2);  /* 0x80057df4 */
extern void FUN_80059160(s32 arg0, s32 arg1);  /* 0x80059160 */
extern void FUN_80070e44(void);           /* 0x80070e44 */
extern void FUN_800742cc(s32 arg0, s32 arg1);  /* 0x800742cc */
extern s32 FUN_80074370(s32 arg0, s32 arg1, s32 arg2, s32 arg3, s32 arg4, s32 arg5);  /* 0x80074370 */
extern void FUN_80057674(s32 arg0, s32 arg1, s32 arg2, s32 arg3, s32 arg4, s32 arg5, s32 arg6, s32 arg7, s32 arg8, s32 arg9);  /* 0x80057674 */
extern void FUN_80049504(u8* arg0, s32 arg1, s32 arg2, s32 arg3, s32 arg4, s32 arg5);  /* 0x80049504 */
extern void FUN_80057c80(void* arg0);     /* 0x80057c80 */
extern void FUN_80037388(void);           /* 0x80037388 */
extern void FUN_80056dc0(s32 arg0, s32 arg1, s32 arg2, s32 arg3, s32 arg4, s32 arg5, s32 arg6, s32 arg7);  /* 0x80056dc0 */
extern void FUN_80038228(s32 arg0, s32 arg1);  /* 0x80038228 */
extern void FUN_80058d64(void);           /* 0x80058d64 */
extern void FUN_80021dd0(void);           /* 0x80021dd0 */
extern void FUN_800587a8(void);           /* 0x800587a8 */
extern void FUN_80058a9c(void);           /* 0x80058a9c */
extern void FUN_80064168(s16 arg0, s32 arg1);  /* 0x80064168 */

/* External global variables - need to match exact addresses */
extern u32 DAT_80083498;   /* Result from FUN_80074370 */
extern u32 DAT_80083504;   /* cleared in loop */
extern u16 DAT_800ef10e;   /* Counter */
extern s32 DAT_80110004;   /* 0x80110004 - global accessed by FUN_80021dd0 */
extern s32 DAT_800898c0;   /* 0x800898c0 - global accessed by FUN_80021dd0 */

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
 * FUN_8006420c - 0x8006420C, size: 0x28 (40 bytes)
 * EQUIVALENT - Sign-extends s16 arg and calls FUN_80064168(arg0, 0)
 * Note: compiler uses subu/addu for stack adjust vs addiu.
 * ============================================================================ */
void FUN_8006420c(s16 arg0) {
    FUN_80064168(arg0, 0);
}

/* ============================================================================
 * FUN_80023374 - 0x80023374, size: 0x28 (40 bytes)
 * MATCHING - Sets GP+0x164 to 1 and calls FUN_8002339c
 * ============================================================================ */
extern s16 DAT_80083318;  /* GP + 0x164 = 356 - shared with FUN_80023320 */
extern void FUN_8002339c(void);

void FUN_80023374(void) {
    DAT_80083318 = 1;
    FUN_8002339c();
}

/* ============================================================================
 * FUN_80023320 - 0x80023320, size: 0x28 (40 bytes)
 * EQUIVALENT - Clears two GP vars and calls FUN_8002339c
 * ============================================================================ */
extern s16 DAT_80083314;  /* GP + 0x160 = 352 */
/* extern s16 DAT_80083318; - declared above */
/* extern void FUN_8002339c(void); - declared above */

void FUN_80023320(void) {
    DAT_80083314 = 0;
    DAT_80083318 = 0;
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

void FUN_8005c974(s32 value) {
    if (value != DAT_8007b000) {
        DAT_8007b000 = value;
    }
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
extern s32 DAT_8007affc;

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
    FUN_80049504((u8*)0x80021e28, 0, 6, 0x70, 0, DAT_800898c0);
}

/* Title main function - 0x800581DC, size: 0x20C (524 bytes) 
 * Note: This is a NON_MATCHING placeholder - the real function
 * has complex initialization that requires understanding more globals
 */
int main(void) {
    CdlFILE file;
    s32 result;
    volatile u16* counter = (volatile u16*)0x800ef10e;
    volatile u16* gpu_reg = (volatile u16*)0x801ff100;
    volatile u32* hw_reg = (volatile u32*)0x1f80012c;
    
    /* System initialization sequence */
    FUN_80070b64();
    ResetCallback();
    FUN_80071648(0);
    InitGeom();
    FUN_80071a4c(0);
    FUN_80057508();
    PadInit(0);
    CdInit();
    
    /* CD file search loop */
    do {
        result = (s32)CdSearchFile(&file, "\\AT1\\GT.B");
    } while (result == 0);
    
    /* File/memory setup */
    FUN_80057df4((u8*)0x800a8860, (u8*)0x80020ab8, 0);
    
    /* Display setup */
    FUN_80059160(0x10000, 0x10000);
    srand(1);
    FUN_80070e44();
    FUN_800742cc(960, 256);
    
    /* Extended display init */
    result = FUN_80074370(16, 16, 256, 200, 0, 512);
    DAT_80083498 = result;
    
    /* More setup */
    FUN_80057674(168, 128, 4096, 0, 0, 0, 0, 4096, 0, 0);
    FUN_80049504((u8*)0x80037388, 0, 0, 0, 0, DAT_80083498);
    FUN_80037388();
    FUN_80056dc0(20, 200, 100, 350, 20, 20, 0, 0);
    
    /* Initial state */
    FUN_80038228(8, 0);
    FUN_80058d64();
    
    /* Main loop */
    while (1) {
        u16 cnt = *counter;
        
        if (cnt >= 3) {
            *counter = 0;
        }
        
        cnt = *counter;
        *gpu_reg = 2;
        *hw_reg = cnt;
        *counter = cnt + 1;
        
        FUN_80038228(8, 0);
        /* Something set at GP+1024 */
        FUN_80021dd0();
        FUN_800587a8();
        FUN_80058a9c();
        
        FUN_80038228(2, 4);
        DAT_80083504 = 0;
        FUN_800587a8();
    }
}
