#ifndef PSXSDK_LIBETC_H
#define PSXSDK_LIBETC_H

/**
 * PSX SDK - ETC Library
 * Controller/Peripheral and utility functions
 */

#include "types.h"

/* Controller types */
#define PadTypeNacom    2   /* Namco Arcade Stick */
#define PadTypeStandard 4   /* Standard controller */
#define PadTypeAnalog   5   /* Analog Joystick */
#define PadTypeGuncon   6   /* Guncon */
#define PadTypeDualShock 7  /* DualShock */

/* Pad state */
typedef struct PADTYPE {
    u8 stat;
    u8 len : 4;
    u8 type : 4;
    u16 btn;
    u8 rs_x, rs_y;
    u8 ls_x, ls_y;
} PADTYPE;

/*---------------------------------------------------------------------------
 * Function Prototypes
 *---------------------------------------------------------------------------*/

void ResetCallback(void);
s32 VSync(s32 mode);
s32 VSyncCallback(void (*f)(void));
s32 StopCallback(void);
s32 RestartCallback(void);
s32 CheckCallback(void);

void PadInit(s32 mode);
void PadStop(void);
u32 PadRead(s32 port);
void PadInitDirect(u8* pad1, u8* pad2);
void PadInitMtap(u8* pad1, u8* pad2);
u32 PadGetState(s32 port);
s32 PadInfoMode(s32 port, s32 term, s32 offs);
s32 PadInfoAct(s32 port, s32 term, s32 offs);
s32 PadInfoComb(s32 port, s32 term, s32 offs);
s32 PadSetMainMode(s32 port, s32 offs, s32 lock);
s32 PadSetActAlign(s32 port, u8* act);

void _96_init(void);
void _96_remove(void);
s32 _bu_init(void);

#endif /* PSXSDK_LIBETC_H */
