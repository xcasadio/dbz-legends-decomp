/**
 * @file cd.c
 * @brief CD-ROM utility functions
 * 
 * Functions for CD-ROM access in DBZ Legends
 */

#include "common.h"
#include "psxsdk/libcd.h"
#include "psxsdk/libetc.h"

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

/**
 * Load a file from CD into buffer
 * 
 * @param cdlFile Pointer to CdlFILE structure with file info
 * @param buffer Destination buffer for file data
 * @param async If non-zero, return immediately (async read)
 * @return Number of sectors read
 * @address 0x80067404
 */
s32 LoadFileIntoBuffer(CdlFILE *cdlFile, u_long *buffer, s16 async) {
    s32 syncResult;
    u_char result[8];
    s32 unused;  /* padding for stack alignment */
    
    /* Calculate number of sectors (file size + 0x7FF) / 0x800 */
    s32 sectors = (cdlFile->size + 0x7FF) >> 11;
    s32 successCode = 1;
    s32 retryCode = 5;
    s32 cdMode = 0x80;

retry:
    /* Seek to file position (CdlSetloc = 2) */
    CdControl(CdlSetloc, (u_char *)cdlFile, result);
    
    /* Wait for seek to complete */
    do {
        syncResult = CdSync(0, result);
    } while (syncResult == 0);
    
    /* Retry on error 5 */
    if (syncResult == retryCode) {
        goto retry;
    }
    
    /* Start reading - retry until success */
    do {
    } while (CdRead(sectors, buffer, cdMode) != successCode);
    
    /* If async mode, return immediately */
    if ((s16)async != 0) {
        return 0;
    }
    
    /* Wait for read to complete */
    do {
        syncResult = CdReadSync(0, result);
        if (syncResult > 0) {
            VSync(0);
        }
    } while (syncResult > 0);
    
    /* If error (-1), restart from seek */
    if (syncResult == -1) {
        goto retry;
    }
    
    return sectors;
}
