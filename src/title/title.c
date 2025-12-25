/**
 * DBZ Legends - Title screen overlay (TITLE.EXE)
 * 
 * Title screen display and menu handling.
 */

#include "common.h"
#include "game.h"
#include "psxsdk/libcd.h"
#include "psxsdk/libetc.h"

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

/* External global variables - need to match exact addresses */
extern u32 DAT_80083498;   /* Result from FUN_80074370 */
extern u32 DAT_80083504;   /* cleared in loop */
extern u16 DAT_800ef10e;   /* Counter */
extern s32 DAT_80110004;   /* 0x80110004 - global accessed by FUN_80021dd0 */
extern s32 DAT_800898c0;   /* 0x800898c0 - global accessed by FUN_80021dd0 */

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
extern s16 DAT_800a8834;

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
