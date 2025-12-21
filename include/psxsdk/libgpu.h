#ifndef PSXSDK_LIBGPU_H
#define PSXSDK_LIBGPU_H

/**
 * PSX SDK - GPU Library
 * Graphics Processing Unit functions and structures
 */

#include "types.h"

/* Display environment */
typedef struct DISPENV {
    RECT disp;          /* Display area */
    RECT screen;        /* Screen area */
    u_char isinter;     /* Interlace mode */
    u_char isrgb24;     /* 24bit mode */
    u_char pad0, pad1;
} DISPENV;

/* Drawing environment */
typedef struct DRAWENV {
    RECT clip;          /* Clipping area */
    s16 ofs[2];         /* Drawing offset */
    RECT tw;            /* Texture window */
    u16 tpage;          /* Texture page */
    u_char dtd;         /* Dither flag */
    u_char dfe;         /* Draw to display */
    u_char isbg;        /* Enable clear */
    u_char r0, g0, b0;  /* Background color */
    DR_ENV dr_env;      /* Primitive */
} DRAWENV;

/* Rectangle */
typedef struct RECT {
    s16 x, y;
    s16 w, h;
} RECT;

/* DR_ENV */
typedef struct DR_ENV {
    u32 tag;
    u32 code[15];
} DR_ENV;

/* Texture page info */
typedef u16 TPage;

/* CLUT info */
typedef u16 Clut;

/*---------------------------------------------------------------------------
 * Primitives
 *---------------------------------------------------------------------------*/

/* Flat-shaded textured quad */
typedef struct POLY_FT4 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    u8 u0, v0; u16 clut;
    s16 x1, y1;
    u8 u1, v1; u16 tpage;
    s16 x2, y2;
    u8 u2, v2; u16 pad1;
    s16 x3, y3;
    u8 u3, v3; u16 pad2;
} POLY_FT4;

/* Gouraud-shaded textured quad */
typedef struct POLY_GT4 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    u8 u0, v0; u16 clut;
    u8 r1, g1, b1, pad1;
    s16 x1, y1;
    u8 u1, v1; u16 tpage;
    u8 r2, g2, b2, pad2;
    s16 x2, y2;
    u8 u2, v2; u16 pad3;
    u8 r3, g3, b3, pad4;
    s16 x3, y3;
    u8 u3, v3; u16 pad5;
} POLY_GT4;

/* Flat-shaded quad */
typedef struct POLY_F4 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    s16 x1, y1;
    s16 x2, y2;
    s16 x3, y3;
} POLY_F4;

/* Gouraud-shaded quad */
typedef struct POLY_G4 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    u8 r1, g1, b1, pad1;
    s16 x1, y1;
    u8 r2, g2, b2, pad2;
    s16 x2, y2;
    u8 r3, g3, b3, pad3;
    s16 x3, y3;
} POLY_G4;

/* Flat-shaded triangle */
typedef struct POLY_F3 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    s16 x1, y1;
    s16 x2, y2;
} POLY_F3;

/* Gouraud-shaded triangle */
typedef struct POLY_G3 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    u8 r1, g1, b1, pad1;
    s16 x1, y1;
    u8 r2, g2, b2, pad2;
    s16 x2, y2;
} POLY_G3;

/* Sprite */
typedef struct SPRT {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    u8 u0, v0; u16 clut;
    s16 w, h;
} SPRT;

/* Sprite 8x8 */
typedef struct SPRT_8 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    u8 u0, v0; u16 clut;
} SPRT_8;

/* Sprite 16x16 */
typedef struct SPRT_16 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    u8 u0, v0; u16 clut;
} SPRT_16;

/* Tile (solid rectangle) */
typedef struct TILE {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    s16 w, h;
} TILE;

/* 1x1 Tile */
typedef struct TILE_1 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
} TILE_1;

/* Block fill */
typedef struct BLK_FILL {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    s16 w, h;
} BLK_FILL;

/* 2-point flat line */
typedef struct LINE_F2 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    s16 x1, y1;
} LINE_F2;

/* 2-point gouraud line */
typedef struct LINE_G2 {
    u32 tag;
    u8 r0, g0, b0, code;
    s16 x0, y0;
    u8 r1, g1, b1, pad1;
    s16 x1, y1;
} LINE_G2;

/*---------------------------------------------------------------------------
 * Function Prototypes
 *---------------------------------------------------------------------------*/

DISPENV* SetDefDispEnv(DISPENV* env, s32 x, s32 y, s32 w, s32 h);
DISPENV* PutDispEnv(DISPENV* env);
DRAWENV* SetDefDrawEnv(DRAWENV* env, s32 x, s32 y, s32 w, s32 h);
DRAWENV* PutDrawEnv(DRAWENV* env);

void ResetGraph(s32 mode);
void SetGraphDebug(s32 level);
s32 DrawSync(s32 mode);
s32 VSync(s32 mode);

void ClearImage(RECT* rect, u8 r, u8 g, u8 b);
void LoadImage(RECT* rect, u32* p);
void StoreImage(RECT* rect, u32* p);
void MoveImage(RECT* rect, s32 x, s32 y);

u32* ClearOTag(u32* ot, s32 n);
u32* ClearOTagR(u32* ot, s32 n);
void DrawOTag(u32* p);

void SetPolyFT4(POLY_FT4* p);
void SetPolyGT4(POLY_GT4* p);
void SetPolyF4(POLY_F4* p);
void SetPolyG4(POLY_G4* p);
void SetSprt(SPRT* p);
void SetTile(TILE* p);

u16 GetTPage(s32 tp, s32 abr, s32 x, s32 y);
u16 GetClut(s32 x, s32 y);

#endif /* PSXSDK_LIBGPU_H */
