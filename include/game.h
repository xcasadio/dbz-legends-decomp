#ifndef GAME_H
#define GAME_H

/**
 * DBZ Legends - Game-specific types and structures
 * 
 * This file contains game-specific type definitions, structures,
 * enumerations and constants used in DBZ Legends.
 */

#include "common.h"
#include "psxsdk/libcd.h"
#include "psxsdk/libgpu.h"
#include "psxsdk/libgte.h"

/*===========================================================================
 * Input System
 *===========================================================================*/

/* PSX Controller button masks */
#define PAD_L2       0x0001
#define PAD_R2       0x0002
#define PAD_L1       0x0004
#define PAD_R1       0x0008
#define PAD_TRIANGLE 0x0010
#define PAD_CIRCLE   0x0020
#define PAD_CROSS    0x0040
#define PAD_SQUARE   0x0080
#define PAD_SELECT   0x0100
#define PAD_L3       0x0200
#define PAD_R3       0x0400
#define PAD_START    0x0800
#define PAD_UP       0x1000
#define PAD_RIGHT    0x2000
#define PAD_DOWN     0x4000
#define PAD_LEFT     0x8000

/*===========================================================================
 * Graphics / Rendering
 *===========================================================================*/

typedef struct {
    u8 r1, g1, b1;         /* 0x78 - Vertex 1 RGB color */
    u8 pad_7B[0x09];       /* 0x7B - Padding */
    u8 r2, g2, b2;         /* 0x84 - Vertex 2 RGB color */
    u8 pad_87[0x09];       /* 0x87 - Padding */
    u8 r3, g3, b3;         /* 0x90 - Vertex 3 RGB color */
    u8 pad_93[0x09];       /* 0x93 - Padding */
    u8 r4, g4, b4;         /* 0x9C - Vertex 4 RGB color */
    u8 pad_9F[0x35];       /* 0x9F - Padding to next primitive (52 bytes total) */
} primitive; 

/* Graphics primitive structure used for vertex color manipulation */
typedef struct {
    u8 pad_00[0x06];           /* 0x00 - Unknown padding */
    u8 start_index;            /* 0x06 - Starting primitive index */
    u8 pad_07[0x02];           /* 0x07 - Padding */
    u8 primitive_count;        /* 0x09 - Number of primitives to process */
    u8 pad_0A[0x6E];           /* 0x0A - Padding to graphics data */
    primitive primitives[1];   /* 0x78 - Array of graphics primitives */
} UnknownGraphicsStruct;

/*===========================================================================
 * Title Overlay Types (TITLE.EXE)
 *===========================================================================*/

typedef struct {
    u8 pad_00[0x02];
    s16 blink_timer;        /* 0x02 - decremented each frame, often set to 0x10 */
    u8 pad_04[0x02];
    s16 countdown_06;       /* 0x06 - observed countdown (often compared to 0x0F) */
    u8 pad_08[0x08];
    u32 flags_10;           /* 0x10 - bitfield used heavily in menu logic */
    u16 cursor_left;        /* 0x14 - typically constrained to [0..5] */
    u16 cursor_right;       /* 0x16 - typically constrained to [6..11] */
    u16 selected_index;     /* 0x18 - observed as current selection index */
    u16 active_index;       /* 0x1A - set from FUN_80053020 result */
    u8 pad_1C[0x302C - 0x1C];
    s32 side_balance_302C;  /* 0x302C - clamped to +/-30000, compared to 30000 */
} TitleMenuState;

typedef struct {
    u8 pad_00[0x18];
    CdlFILE bgm_file;          /* 0x18 */
    u8 pad_30[0x78 - 0x30];
    CdlLOC cd_loc;             /* 0x78 */
    u32 cd_read_sectors;       /* 0x7C - written as 2 or 10 before CdRead */
    u8 pad_80[0x90 - 0x80];
    CdlLOC cd_base_loc;        /* 0x90 - used as base for CdPosToInt */
    u8 pad_94[0x108 - 0x94];
    s16 seq_id_108;            /* 0x108 - masked with 0x7f before FUN_80068a2c */
    s16 vab_id_10A;            /* 0x10A - sound bank id (SsUtGetVBaddrInSB) */
    u8 pad_10C[0x110 - 0x10C];
    s16 timer_110;             /* 0x110 - small timer, set when zero */
} TitleAudioCdBlock;

typedef struct {
    u8 pad_00[0x08];
    u16 unk_08;
    u16 unk_0A;
    u16 unk_0C;
    u16 unk_0E;
    u16 param_10;
    u16 param_12;
    u8 active_14;
    u8 active_15;
    u16 param_16;
    s16 cd_state_18;
    s16 handles_1A[6];         /* 0x1A - frequently checked against -1 */
    u8 pad_26[0x2A - 0x26];
    s16 retry_counter_2A;      /* 0x2A - decremented, reloaded to 10 */
    u8 pad_2C[0x30 - 0x2C];
    u8 volume_r_30;
    u8 volume_l_31;
    u8 color_r_32;
    u8 color_g_33;
    u8 color_b_34;
    u8 color_a_35;
    u16 requests_36[6];        /* 0x36 - 0x80 bit indicates pending request */
    s16 sample_id_42;
    s16 request_kind_44;
    s16 voice_group_46;
    u8 pad_48[0x110 - 0x48];
    s16 timer_110;
} TitleAudioSfxBlock;

typedef union {
    TitleAudioCdBlock cd;
    TitleAudioSfxBlock sfx;
    u8 raw[0x112];
} TitleAudioBlock;

/*===========================================================================
 * Global Variables (extern declarations)
 *===========================================================================*/



/*===========================================================================
 * Function Prototypes
 *===========================================================================*/


#endif /* GAME_H */
