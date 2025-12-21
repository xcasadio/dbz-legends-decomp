/**
 * @file gpu.c
 * @brief GPU/VRAM utility functions
 * 
 * Functions for GPU and VRAM operations in DBZ Legends
 */

#include "common.h"
#include "psxsdk/libgpu.h"

/* Global RECT used for LoadImage operations */
RECT g_LoadImageRect;  /* 0x8009ABA8 */

/**
 * Load image to VRAM and return TPage or ClutId
 * 
 * @param buffer Pointer to image data
 * @param x X position in VRAM
 * @param y Y position in VRAM
 * @param w Width of image
 * @param h Height of image
 * @param isClut If non-zero, calculate ClutId; otherwise calculate TPage
 * @return TPage ID or Clut ID depending on isClut parameter
 * @address 0x80067178
 */
u32 LoadImage_ReturnTPageOrClutId(u_long *buffer, s16 x, s16 y, s16 w, s16 h, s8 isClut) {
    s32 xdiv, ydiv;
    
    /* Setup RECT and load image */
    g_LoadImageRect.x = x;
    g_LoadImageRect.y = y;
    g_LoadImageRect.w = w;
    g_LoadImageRect.h = h;
    LoadImage(&g_LoadImageRect, buffer);
    
    if (isClut == 0) {
        /* Calculate TPage ID */
        /* TPage = (x / 64) + ((y / 256) * 16) */
        xdiv = x;
        if (x < 0) {
            xdiv = x + 63;
        }
        ydiv = y;
        if (y < 0) {
            ydiv = y + 255;
        }
        return (u32)(((xdiv >> 6) + ((ydiv >> 8) << 4)) & 0xFFFF);
    } else {
        /* Calculate Clut ID */
        /* ClutId = (x / 16) + (y * 64) */
        xdiv = x;
        if (x < 0) {
            xdiv = x + 15;
        }
        return (u32)(((xdiv >> 4) + (y << 6)) & 0xFFFF);
    }
}
