/**
 * @file cd.c
 * @brief CD-ROM utility functions
 * 
 * Functions for CD-ROM access in DBZ Legends
 */

#include "common.h"
#include "psxsdk/libcd.h"

/* External callbacks */
extern void data_ready_callback(void);  /* 0x8002AF88 */
extern void StCdInterrupt(void);        /* 0x8002A29C */

/* Global variables */
s32 g_CdReadMode;                       /* 0x800B16B4 */

/**
 * Extended CD read function
 * Sets up CD mode and callbacks, then starts streaming read
 * 
 * @param mode CD read mode flags
 * @return Result from CdControl
 * @address 0x80026D3C
 */
int CdRead2(s32 mode) {
    u8 param;
    
    param = (u8)mode;
    CdControl(CdlSetmode, &param, NULL);  /* 0x0E */
    
    if (mode & 0x100) {
        if (mode & 0x20) {
            g_CdReadMode = 0;
        } else {
            g_CdReadMode = 1;
        }
        CdDataCallback(data_ready_callback);
        CdReadyCallback(StCdInterrupt);
    }
    
    return CdControl(CdlReadS, NULL, NULL);  /* 0x1B */
}

/**
 * Wait for CD seek and start reading
 * 
 * @param pos Pointer to CD location (CdlLOC)
 * @address 0x80021574
 */
void CdSeekAndRead(s32 pos) {
    do {
    } while (CdControl(0x15, (u8 *)pos, NULL) == 0);
    
    do {
    } while (CdRead2(0x1C0) == 0);
}
