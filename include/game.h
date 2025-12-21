#ifndef GAME_H
#define GAME_H

/**
 * DBZ Legends - Game-specific types and structures
 * 
 * This file contains game-specific type definitions, structures,
 * enumerations and constants used in DBZ Legends.
 */

#include "common.h"
#include "psxsdk/libgpu.h"
#include "psxsdk/libgte.h"

/*===========================================================================
 * Game Constants
 *===========================================================================*/

#define MAX_PLAYERS         2
#define MAX_CHARACTERS      32      /* TODO: Determine actual count */
#define MAX_ATTACKS         64      /* TODO: Determine actual count */

/* Screen dimensions */
#define SCREEN_WIDTH        320
#define SCREEN_HEIGHT       240

/* Fixed point scale (4.12 format) */
#define FP_SHIFT            12
#define FP_ONE              (1 << FP_SHIFT)

/*===========================================================================
 * Game States / Modes
 *===========================================================================*/

typedef enum GameState {
    STATE_INIT = 0,
    STATE_TITLE,
    STATE_MENU,
    STATE_SELECT,
    STATE_BATTLE,
    STATE_VS,
    STATE_DEMO,
    STATE_ENDING,
    STATE_MAX
} GameState;

typedef enum GameMode {
    MODE_STORY = 0,
    MODE_VS,
    MODE_TRAINING,
    /* TODO: Add more modes as discovered */
} GameMode;

/*===========================================================================
 * Character Data
 *===========================================================================*/

typedef enum CharacterId {
    CHAR_GOKU = 0,
    CHAR_VEGETA,
    CHAR_PICCOLO,
    CHAR_GOHAN,
    CHAR_TRUNKS,
    CHAR_FRIEZA,
    CHAR_CELL,
    CHAR_BUU,
    /* TODO: Add all characters as discovered */
    CHAR_MAX
} CharacterId;

typedef struct CharacterStats {
    s16 hp_max;
    s16 ki_max;
    s16 attack;
    s16 defense;
    s16 speed;
    s16 power;
    /* TODO: Add more stats as discovered */
} CharacterStats;

typedef struct Character {
    u8 id;
    u8 state;
    s16 hp;
    s16 ki;
    s16 pos_x;
    s16 pos_y;
    s16 pos_z;
    s16 vel_x;
    s16 vel_y;
    s16 vel_z;
    s16 rotation;
    u16 flags;
    CharacterStats* stats;
    /* TODO: Add more fields as discovered */
} Character;

/*===========================================================================
 * Battle System
 *===========================================================================*/

typedef enum BattlePhase {
    BATTLE_PHASE_INIT = 0,
    BATTLE_PHASE_INTRO,
    BATTLE_PHASE_FIGHT,
    BATTLE_PHASE_ATTACK,
    BATTLE_PHASE_RESULT,
    BATTLE_PHASE_END,
} BattlePhase;

typedef struct BattleState {
    u8 phase;
    u8 turn;
    u8 round;
    u8 flags;
    Character* player1;
    Character* player2;
    /* TODO: Add more fields as discovered */
} BattleState;

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

typedef struct PadData {
    u16 current;
    u16 pressed;
    u16 released;
    u16 held;
} PadData;

/*===========================================================================
 * Graphics / Rendering
 *===========================================================================*/

typedef union GpuPrimitive {
    void* raw;
    POLY_FT4* poly_ft4;
    POLY_GT4* poly_gt4;
    POLY_F4* poly_f4;
    POLY_G4* poly_g4;
    SPRT* sprt;
    TILE* tile;
    LINE_F2* line_f2;
    LINE_G2* line_g2;
} GpuPrimitive;

typedef struct Sprite {
    s16 x;
    s16 y;
    s16 w;
    s16 h;
    u16 clut;
    u16 tpage;
    u8 u;
    u8 v;
} Sprite;

/*===========================================================================
 * Memory / File System
 *===========================================================================*/

typedef struct FileEntry {
    u32 sector;     /* CD sector location */
    u32 size;       /* File size in bytes */
} FileEntry;

/*===========================================================================
 * Global Variables (extern declarations)
 *===========================================================================*/

/* TODO: Add global variable declarations as discovered */
// extern GameState g_GameState;
// extern BattleState g_BattleState;
// extern PadData g_PadData[MAX_PLAYERS];

/*===========================================================================
 * Function Prototypes
 *===========================================================================*/

/* Main */
// void main(void);
// void MainLoop(void);

/* Game state */
// void ChangeState(GameState newState);
// void UpdateState(void);

/* Battle */
// void BattleInit(void);
// void BattleUpdate(void);
// void BattleRender(void);

/* Input */
// void PadInit(void);
// void PadUpdate(void);

#endif /* GAME_H */
