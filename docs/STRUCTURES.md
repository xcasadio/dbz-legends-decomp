# DBZ Legends - Data Structures Documentation

This document describes the various data structures used in Dragon Ball Z: Legends.

## Graphics Structures

### UnknownGraphicsStruct

**Address Range:** Used in FUN_80032c38 (0x80032C38)  
**Size:** Variable (contains graphics primitive data)  
**Purpose:** Graphics primitive structure containing vertex color data and rendering information.

#### Structure Layout

```c
typedef struct {
    u8 pad_00[0x06];           // 0x00 - Unknown padding
    u8 start_index;            // 0x06 - Starting primitive index
    u8 pad_07[0x02];           // 0x07 - Padding
    u8 primitive_count;        // 0x09 - Number of primitives to process
    u8 pad_0A[0x6E];           // 0x0A - Padding to graphics data
    
    // Graphics primitive data starts at offset 0x78 (120)
    struct {
        u8 r1, g1, b1;         // 0x78 - Vertex 1 RGB color
        u8 pad_7B[0x09];       // 0x7B - Padding
        u8 r2, g2, b2;         // 0x84 - Vertex 2 RGB color  
        u8 pad_87[0x09];       // 0x87 - Padding
        u8 r3, g3, b3;         // 0x90 - Vertex 3 RGB color
        u8 pad_93[0x09];       // 0x93 - Padding
        u8 r4, g4, b4;         // 0x9C - Vertex 4 RGB color
        u8 pad_9F[0x35];       // 0x9F - Padding to next primitive (52 bytes total)
    } primitives[];            // Array of graphics primitives
} UnknownGraphicsStruct;
```

#### Usage

This structure is used by `FUN_80032c38` to set vertex colors for graphics primitives. The function:

1. Reads the `start_index` (offset 0x06) and `primitive_count` (offset 0x09)
2. Loops through `primitive_count` primitives starting from `start_index`
3. Sets the same RGB color values to all 4 vertices of each primitive
4. Each primitive is 52 bytes (0x34) apart in memory

#### Function Signature

```c
void FUN_80032c38(u32 offset, UnknownGraphicsStruct* graphics_struct, u8 r, u8 g, u8 b);
```

**Parameters:**
- `offset`: Byte offset into the structure
- `graphics_struct`: Pointer to the graphics structure
- `r`, `g`, `b`: RGB color values to set for all vertices

#### Notes

- This appears to be related to PlayStation 1 graphics primitives (likely quads/triangles)
- The 4 vertices per primitive suggest quad rendering
- The structure may contain additional data beyond what's documented here
- Used in the title screen overlay (TITLE.EXE)