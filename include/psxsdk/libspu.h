#ifndef PSXSDK_LIBSPU_H
#define PSXSDK_LIBSPU_H

/**
 * PSX SDK - SPU Library
 * Sound Processing Unit functions and structures
 */

#include "types.h"

/* SPU voice attributes */
#define SPU_VOICE_VOLL      (1 << 0)
#define SPU_VOICE_VOLR      (1 << 1)
#define SPU_VOICE_VOLMODEL  (1 << 2)
#define SPU_VOICE_VOLMODER  (1 << 3)
#define SPU_VOICE_PITCH     (1 << 4)
#define SPU_VOICE_NOTE      (1 << 5)
#define SPU_VOICE_SAMPLE_NOTE (1 << 6)
#define SPU_VOICE_WDSA      (1 << 7)
#define SPU_VOICE_ADSR_AMODE (1 << 8)
#define SPU_VOICE_ADSR_SMODE (1 << 9)
#define SPU_VOICE_ADSR_RMODE (1 << 10)
#define SPU_VOICE_ADSR_AR   (1 << 11)
#define SPU_VOICE_ADSR_DR   (1 << 12)
#define SPU_VOICE_ADSR_SR   (1 << 13)
#define SPU_VOICE_ADSR_RR   (1 << 14)
#define SPU_VOICE_ADSR_SL   (1 << 15)
#define SPU_VOICE_LSAX      (1 << 16)
#define SPU_VOICE_ADSR_ADSR1 (1 << 17)
#define SPU_VOICE_ADSR_ADSR2 (1 << 18)

/* Common attributes */
#define SPU_COMMON_MVOLL    (1 << 0)
#define SPU_COMMON_MVOLR    (1 << 1)
#define SPU_COMMON_MVOLMODEL (1 << 2)
#define SPU_COMMON_MVOLMODER (1 << 3)
#define SPU_COMMON_RVOLL    (1 << 4)
#define SPU_COMMON_RVOLR    (1 << 5)
#define SPU_COMMON_CDVOLL   (1 << 6)
#define SPU_COMMON_CDVOLR   (1 << 7)
#define SPU_COMMON_CDREV    (1 << 8)
#define SPU_COMMON_CDMIX    (1 << 9)
#define SPU_COMMON_EXTVOLL  (1 << 10)
#define SPU_COMMON_EXTVOLR  (1 << 11)
#define SPU_COMMON_EXTREV   (1 << 12)
#define SPU_COMMON_EXTMIX   (1 << 13)

/* Transfer modes */
#define SpuTransByDMA       0
#define SpuTransByIO        1

/* SPU voice attribute structure */
typedef struct SpuVoiceAttr {
    u32 voice;
    u32 mask;
    s16 volume_l;
    s16 volume_r;
    s16 volmode_l;
    s16 volmode_r;
    u16 pitch;
    u16 note;
    u16 sample_note;
    s16 envx;
    u32 addr;
    u32 loop_addr;
    s16 ar;
    s16 dr;
    s16 sr;
    s16 rr;
    s16 sl;
    s16 adsr1;
    s16 adsr2;
} SpuVoiceAttr;

/* SPU common attribute structure */
typedef struct SpuCommonAttr {
    u32 mask;
    s16 mvol_l;
    s16 mvol_r;
    s16 mvolmode_l;
    s16 mvolmode_r;
    s16 rvol_l;
    s16 rvol_r;
    s16 cd_vol_l;
    s16 cd_vol_r;
    s16 cd_rev;
    s16 cd_mix;
    s16 ext_vol_l;
    s16 ext_vol_r;
    s16 ext_rev;
    s16 ext_mix;
} SpuCommonAttr;

/*---------------------------------------------------------------------------
 * Function Prototypes
 *---------------------------------------------------------------------------*/

void SpuInit(void);
void SpuStart(void);
void SpuQuit(void);

u32 SpuSetTransferMode(u32 mode);
u32 SpuSetTransferStartAddr(u32 addr);
u32 SpuWrite(u8* data, u32 size);
u32 SpuRead(u8* data, u32 size);
s32 SpuIsTransferCompleted(s32 flag);
u32 SpuWritePartly(u8* data, u32 size);

void SpuSetKey(u32 on_off, u32 voice_bit);
void SpuSetKeyOnWithAttr(SpuVoiceAttr* attr);
s32 SpuGetKeyStatus(u32 voice_bit);

void SpuSetVoiceAttr(SpuVoiceAttr* attr);
void SpuGetVoiceAttr(SpuVoiceAttr* attr);
void SpuSetCommonAttr(SpuCommonAttr* attr);

void SpuSetReverb(s32 on_off);
void SpuSetReverbModeType(s32 type);
void SpuSetReverbDepth(s16 depth_l, s16 depth_r);
u32 SpuSetReverbVoice(s32 on_off, u32 voice_bit);

u32 SpuMalloc(u32 size);
void SpuFree(u32 addr);
void SpuInitMalloc(s32 max, u8* buf);

#endif /* PSXSDK_LIBSPU_H */
