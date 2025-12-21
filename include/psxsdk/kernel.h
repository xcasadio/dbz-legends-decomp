#ifndef PSXSDK_KERNEL_H
#define PSXSDK_KERNEL_H

/**
 * PSX SDK - Kernel functions
 * BIOS and system call functions
 */

#include "types.h"

/* Event classes */
#define EvSpCONT        0x0001  /* Controller */
#define EvSpINT         0x0002  /* Interrupt */
#define EvSpIOE         0x0004  /* IO End */
#define EvSpMC          0x0011  /* Memory Card */
#define EvSpVBLANK      0x0002  /* VBlank */
#define EvSpTIMER0      0x0003  /* Timer 0 */
#define EvSpTIMER1      0x0004  /* Timer 1 */
#define EvSpTIMER2      0x0005  /* Timer 2 */

/* Event specs */
#define EvSpIOE         0x0004
#define EvSpCDROM       0x0001
#define EvSpDMA         0x0001
#define EvSpVBLANK      0x0002

/* Event modes */
#define EvMdNOINTR      0x1000
#define EvMdINTR        0x2000

/* Event status */
#define EvStUNUSED      0x0000
#define EvStWAIT        0x1000
#define EvStACTIVE      0x2000
#define EvStALREADY     0x4000

/* Thread control block */
typedef struct TCB {
    u32 status;
    u32 mode;
    u32 reg[32];
    u32 epc;
    u32 hi, lo;
    u32 sr, cause;
} TCB;

/* Execution control block */
typedef struct EXEC {
    u32 pc0;
    u32 gp0;
    u32 t_addr;
    u32 t_size;
    u32 d_addr;
    u32 d_size;
    u32 b_addr;
    u32 b_size;
    u32 s_addr;
    u32 s_size;
    u32 sp, fp, gp, ret, base;
} EXEC;

/*---------------------------------------------------------------------------
 * Function Prototypes
 *---------------------------------------------------------------------------*/

/* System initialization */
void ResetEntryInt(void);
void HookEntryInt(void* handler);

/* Thread management */
s32 OpenThread(u32 pc, u32 sp, u32 gp);
s32 CloseThread(s32 th);
s32 ChangeThread(s32 th);

/* Event management */
s32 OpenEvent(u32 desc, s32 spec, s32 mode, void (*func)(void));
s32 CloseEvent(s32 ev);
s32 EnableEvent(s32 ev);
s32 DisableEvent(s32 ev);
s32 WaitEvent(s32 ev);
s32 TestEvent(s32 ev);
void DeliverEvent(u32 desc, s32 spec);

/* Memory management */
void InitHeap(void* heap, u32 size);
void* malloc(u32 size);
void* calloc(u32 num, u32 size);
void* realloc(void* ptr, u32 size);
void free(void* ptr);

/* Executable */
s32 Load(char* name, EXEC* exec);
s32 Exec(EXEC* exec, s32 argc, char** argv);
void FlushCache(void);

/* File system */
s32 open(char* name, s32 mode);
s32 close(s32 fd);
s32 read(s32 fd, void* buf, s32 len);
s32 write(s32 fd, void* buf, s32 len);
s32 lseek(s32 fd, s32 offset, s32 whence);

/* Interrupt */
void EnterCriticalSection(void);
void ExitCriticalSection(void);

/* Misc */
s32 GPU_cw(u32 cmd);
s32 GPU_cwb(u32* cmds, s32 len);
s32 GPU_cwp(u32* cmds, s32 len);
s32 GPU_sync(void);
s32 GsGetWorkBase(void);
void SetDispMask(s32 mask);

/* Printf */
s32 printf(const char* fmt, ...);

#endif /* PSXSDK_KERNEL_H */
