#ifndef PSXSDK_LIBCD_H
#define PSXSDK_LIBCD_H

/**
 * PSX SDK - CD-ROM Library
 * CD-ROM access functions and structures
 */

#include "types.h"

/* CD-ROM commands */
#define CdlNop          0x01
#define CdlSetloc       0x02
#define CdlPlay         0x03
#define CdlForward      0x04
#define CdlBackward     0x05
#define CdlReadN        0x06
#define CdlStandby      0x07
#define CdlStop         0x08
#define CdlPause        0x09
#define CdlInit         0x0A
#define CdlMute         0x0B
#define CdlDemute       0x0C
#define CdlSetfilter    0x0D
#define CdlSetmode      0x0E
#define CdlGetparam     0x0F
#define CdlGetlocL      0x10
#define CdlGetlocP      0x11
#define CdlReadS        0x1B
#define CdlReset        0x1C
#define CdlReadToc      0x1E

/* CD-ROM modes */
#define CdlModeSpeed    0x80    /* Double speed */
#define CdlModeRT       0x40    /* ADPCM */
#define CdlModeSize1    0x20    /* Sector size 2340 */
#define CdlModeSize0    0x10    /* Sector size 2048 */
#define CdlModeSF       0x08    /* Subheader filter */
#define CdlModeRept     0x04    /* Report */
#define CdlModeAP       0x02    /* Audio pause */
#define CdlModeDA       0x01    /* CD-DA */

/* CD-ROM status */
#define CdlStatPlay     0x80
#define CdlStatSeek     0x40
#define CdlStatRead     0x20
#define CdlStatShellOpen 0x10
#define CdlStatSeekError 0x04
#define CdlStatStandby  0x02
#define CdlStatError    0x01

/* Sector position */
typedef struct CdlLOC {
    u8 minute;
    u8 second;
    u8 sector;
    u8 track;
} CdlLOC;

/* File entry */
typedef struct CdlFILE {
    CdlLOC pos;
    u32 size;
    char name[16];
} CdlFILE;

/* Filter */
typedef struct CdlFILTER {
    u8 file;
    u8 chan;
    u16 pad;
} CdlFILTER;

/* Callback types */
typedef void (*CdlCB)(u8 status, u8* result);

/*---------------------------------------------------------------------------
 * Function Prototypes
 *---------------------------------------------------------------------------*/

void CdInit(void);
s32 CdReset(s32 mode);
s32 CdFlush(void);

s32 CdControl(u8 com, u8* param, u8* result);
s32 CdControlB(u8 com, u8* param, u8* result);
s32 CdControlF(u8 com, u8* param);

s32 CdRead(s32 sectors, u32* buf, s32 mode);
s32 CdReadSync(s32 mode, u8* result);
s32 CdReady(s32 mode, u8* result);

CdlFILE* CdSearchFile(CdlFILE* fp, char* name);
CdlLOC* CdIntToPos(s32 i, CdlLOC* p);
s32 CdPosToInt(CdlLOC* p);

CdlCB CdReadCallback(CdlCB func);
CdlCB CdReadyCallback(CdlCB func);
CdlCB CdSyncCallback(CdlCB func);
CdlCB CdDataCallback(CdlCB func);

s32 CdGetSector(void* madr, s32 size);

#endif /* PSXSDK_LIBCD_H */
