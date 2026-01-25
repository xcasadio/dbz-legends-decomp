/**
 * DBZ Legends - Title screen overlay header (TITLE.EXE)
 */

#ifndef TITLE_H
#define TITLE_H

#include "common.h"
#include "game.h"
#include "psxsdk/libcd.h"
#include "psxsdk/libetc.h"
#include "psxsdk/libspu.h"
#include "psxsdk/kernel.h"

/* ============================================================================
 * Macros
 * ============================================================================ */
#define ReadFile FUN_80057df4

/* ============================================================================
 * Structures
 * ============================================================================ */

/* Structure for FUN_800671e4 parameter */
typedef struct {
    u16 field_0x0;
    u16 field_0x2;
    u16 field_0x4;
    u16 field_0x6;
    u16 field_0x8;
    u16 field_0xa;
    u16 field_0xc;
    u16 field_0xe;
    u16 field_0x10;
} Struct_800671e4_Param;

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

typedef struct {
    u8 pad_000[0x134];
    u32 flags_134;
    u8 pad_138[0x224 - 0x138];
    u8 byte_224;
} UnkStruct_80044754;

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

typedef struct {
    s16 mode_00;
    u8 pad_002[0x2C - 0x02];
    s16 field_2C;
    s16 field_2E;
} UnkStruct_8003287c;


typedef struct {
    u32 unk0;
    u32 unk4;
    u8 pad_08[0x18];
    void* ptr_20;
} Unk_800836d4_Entry;


/* ============================================================================
 * External Functions
 * ============================================================================ */

extern void SpuStQuit(void); /* PSX SDK - SPU streaming quit */
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
extern s16 FUN_80038228(s32 arg0, s32 arg1);  /* 0x80038228 */
extern void FUN_80058d64(void);           /* 0x80058d64 */
extern void FUN_80021dd0(void);           /* 0x80021dd0 */
extern void FUN_80022c94(void);           /* 0x80022c94 */
extern void FUN_80023290(void);           /* 0x80023290 */

extern void FUN_8002339c(void);
extern void FUN_800688b0(s16 arg0);

extern void FUN_8002cdc4(UnkStruct_8002cd70* arg0); /* 0x8002cdc4 */
extern void FUN_80032434(UnkStruct_8002cd70* arg0); /* 0x80032434 */
extern void FUN_800587a8(void);           /* 0x800587a8 */
extern void FUN_80058a9c(void);           /* 0x80058a9c */
extern s32 FUN_8005c9d8(s16 arg0);        /* 0x8005c9d8 */
extern void FUN_800607fc(s16 arg0, u16 arg1, u16 arg2, s32 arg3);  /* 0x800607fc */
extern void FUN_80064168(s16 arg0, s32 arg1);  /* 0x80064168 */
extern s32 FUN_80064368(s32 arg0, s16 arg1, s32 arg2, s32 arg3);   /* 0x80064368 */
extern void FUN_80067c74(s16 arg0, s32 arg1);  /* 0x80067c74 */
extern void FUN_800678b4(s16 arg0, s32 arg1, u8 arg2, s16 arg3);   /* 0x800678b4 */
extern void FUN_80068e34(s16 arg0, s32 arg1);  /* 0x80068e34 */
extern s16 FUN_8003bcc4(s16 arg0);             /* 0x8003bcc4 */
extern void FUN_8004be40(void* arg0, u16 arg1); /* 0x8004be40 */
extern void FUN_80050744(void* arg0);          /* 0x80050744 */
extern void FUN_8005286c(void* arg0);          /* 0x8005286c */
extern void FUN_80062760(s32 arg0, s32 arg1);  /* 0x80062760 */
extern void FUN_800627f8(s16 arg0);            /* 0x800627f8 */
extern void FUN_80062838(s16 arg0);            /* 0x80062838 */
extern void SpuSetReverbModeParam(SpuReverbAttr* attr); /* 0x80062878 */
extern s32 FUN_80070bc4(const char* arg0);     /* 0x80070bc4 */
extern s32 FUN_8006bc88(void* arg0, void* arg1); /* 0x8006bc88 */
extern void FUN_80070e34(void* arg0);           /* 0x80070e34 - TestEvent from PSX SDK */
extern void FUN_8003de38(void* arg0, s32 arg1); /* 0x8003de38 */
extern s32 FUN_80027174(void);                  /* 0x80027174 */
extern void FUN_80030ec4(void);                 /* 0x80030ec4 */
extern void FUN_80056b30(void);                 /* 0x80056b30 */
extern void FUN_80058158(const char* arg0);     /* 0x80058158 */
extern void FUN_800402dc(void* arg0);           /* 0x800402dc */
extern void FUN_80022c04(void);                 /* 0x80022c04 */
extern s32 FUN_80022b1c(void);                  /* 0x80022b1c */
extern void FUN_80057b08(void* arg0, s32 arg1, s32 arg2, s32 arg3, s32 arg4, s32 arg5); /* 0x80057b08 */
extern void LoadImageInVram(u_long* arg0, s32 arg1, s32 arg2, s32 arg3, s32 arg4, u8 arg5); /* LoadImageInVram */
extern void FUN_8004080c(UnkStruct_8004bf94* arg0, s32 arg1);   /* 0x8004080c */
extern s32 FUN_80063608(s32 arg0, s32 arg1);    /* 0x80063608 */
extern u32 FUN_800670cc(s16 arg0, s16 arg1);                  /* 0x800670cc */
extern void FUN_80062334(s32 arg0);         /* 0x80062334 */
extern void FUN_80064a78(s32 arg0, s32 arg1); /* 0x80064a78 */
extern void FUN_80068f60(s32 arg0, s32 arg1, s32 arg2); /* 0x80068f60 */
extern void FUN_80067ae8(s32 arg0, s32 arg1, s32 arg2); /* 0x80067ae8 */
extern void FUN_80063f6c(void);             /* 0x80063f6c */
extern void FUN_80064010(void);             /* 0x80064010 */
extern void FUN_800637b0(s32 arg0);
extern void FUN_800634e0(s32 arg0);
extern void FUN_80063c9c(s32 arg0);
extern void FUN_800640ec(void);
extern void FUN_8005c214(s32 arg0);


/* ============================================================================
 * External Global Variables
 * ============================================================================ */

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
extern Unk_800836d4_Entry UnkStruct_Array_800836d4[30];
extern Unk_800836d4_Entry UnkStruct_Array_80081cb4[30];

extern volatile u16 DAT_801ff10e;
extern volatile u16 DAT_801ff100;
extern volatile u32 DAT_1f80012c;

extern s32 DAT_80110004;   /* 0x80110004 - global accessed by FUN_80021dd0 */
extern s32 DAT_800898c0;   /* 0x800898c0 - global accessed by FUN_80021dd0 */
extern s32 DAT_8008337c;   /* CD seek mode */
extern s32 DAT_80083358;   /* CD sector index */
extern u8 DAT_8008f19c;    /* CD sector data base */
extern s32 DAT_8008335c;   /* CD status flags */
extern u32 DAT_800833fc;   /* CD control flags */
extern u32 DAT_800833dc;   /* Game state pointer */
extern u16 DAT_800831b8;   /* String part 2 */
extern u16 DAT_800831c0;   /* String part 4 */
extern u32 DAT_800834c8;   /* Event handle 1 */
extern u32 DAT_800834cc;   /* Event handle 2 */
extern u32 DAT_800834d0;   /* Event handle 3 */
extern u32 DAT_800834d4;   /* Event handle 4 */
extern u32 DAT_800834ec;   /* Event handle 5 */
extern u32 DAT_800834f0;   /* Event handle 6 */
extern u32 DAT_800834f4;   /* Event handle 7 */
extern u32 DAT_800834f8;   /* Event handle 8 */
extern u32 DAT_800833f4;   /* Pointer */
extern u32 DAT_80083440;   /* CD status */
extern s16 DAT_800acb1a;   /* Character index */
extern s32 INT_ARRAY_800b954c[];    /* Character data base */
extern u8 DAT_80077a50;    /* Image data base */
extern void* PTR_DAT_80077b38; /* Image pointer */
extern s32 DAT_800a8880;   /* Global data array base */
extern u8 DAT_8008f568;    /* Global array 1 */
extern u8 DAT_8008f56a;    /* Global array 2 */
extern u8 DAT_8008f6e8;    /* Global array 3 */
extern s32 DAT_8007b484;   /* SPU initialization flag */
extern s32 DAT_8007b000;   /* SPU state variable 1 */
extern s32 DAT_8007b004;   /* SPU state variable 2 */
extern u32 DAT_8007b080;   /* SPU event descriptor */
extern u16 DAT_80078b3c;   /* Lookup table base */
extern SVECTOR SVECTOR_1f80007c; /* GTE scratchpad vector */
extern CdlATV CdlATV_80083378; /* CD audio/video attenuation values */
extern u32 DAT_801fff00; /* LoadExec stack pointer parameter */
extern u16 DAT_801ff200; /* Controller input flag 1 */
extern u16 USHORT_801ff202; /* Controller input value 1 */
extern u16 DAT_801ff208; /* Controller input flag 2 */
extern u16 USHORT_801ff20a; /* Controller input value 2 */
extern u16 USHORT_801ff210; /* Controller input flag 3 */
extern u16 USHORT_801ff212; /* Controller input value 3 */


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

extern s16 DAT_80083314;  /* GP + 0x160 = 352 */
extern s16 DAT_80083318;  /* GP + 0x164 = 356 */
extern s32 DAT_8007b000;
extern s32 DAT_8007affc;

extern s32 DAT_8008335c;
extern u32 DAT_800833fc;

extern TitleAudioBlock* DAT_gp_018c;

extern s16 DAT_gp_028c;  /* GP + 0x28C */
extern u8 DAT_800acd9c;

extern s32 DAT_800813a4;

extern s16 DAT_800a6768;  /* Shared with FUN_800678a4 */
extern s16 DAT_800a8834;  /* Shared with FUN_80064274 */

#endif /* TITLE_H */
