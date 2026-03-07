# STGxMD.B File Format Analysis

## Overview
This document describes the **fully understood** format of STGxMD.B files used in DBZ Legends (SLPS-003.55) for storing 3D stage/environment mesh data.

## Analysis Tools Used
- Ghidra (via ReVa MCP) decompilation of GAME.EXE
- Live RAM reads via PCSX-Redux MCP while STG1MD.B was loaded in-game
  (capture taken at the story mode character selection screen, stage visible and camera rotating)
- Binary analysis of actual file data

---

## Complete File Structure

```
+--- STGxMD.B File Layout -------------------------------------------+
| Offset | Size   | Description                                       |
|--------|--------|---------------------------------------------------|
| 0x00   | 4      | meshTableOffset (= 0x08)                          |
| 0x04   | 4      | particleListOffset (relative to file start)       |
| 0x08   | 128    | MeshTable[16] (16 × 8 bytes)                      |
| 0x88   | varies | ParticleList (2 + count×6 bytes)                  |
| ...    | 4      | [before each mesh blob]: renderTableRelOffset     |
| ...    | varies | Mesh blob N (SVECTOR data + embedded RenderTable) |
+--------------------------------------------------------------------+
```

### 1. Main Header (8 bytes)
```c
struct STGxMD_Header {
    uint32 meshTableOffset;      // +0x00: always 0x08
    uint32 particleListOffset;   // +0x04: offset to particle list from file start
};
```

**STG1MD.B values:**
- meshTableOffset = 0x08
- particleListOffset = 0x88

---

### 2. Mesh Table (128 bytes = 16 × 8 bytes)

```c
struct MeshTableEntry {
    uint32 meshDataOffset;  // Offset in file; relocated to (0x80106000 + offset) in RAM
    uint32 type;            // 1 = normal static mesh, 2 = animated (multi-frame)
};

MeshTableEntry meshTable[16];  // Always 16 entries; unused entries have offset=0
```

**STG1MD.B — file offsets and RAM addresses after relocation (base 0x80106000):**

| # | File offset | RAM address   | type |
|---|-------------|---------------|------|
| 0 | 0x0184      | 0x80106184    | 1    |
| 1 | 0x0CE0      | 0x80106CE0    | 1    |
| 2 | 0x15D4      | 0x801075D4    | 1    |
| 3 | 0x1F04      | 0x80107F04    | 1    |
| 4 | 0x29C4      | 0x801089C4    | 1    |
| 5–15 | 0x0000   | 0x80106000    | 0    | (unused — base ptr)

**Relocation (FUN_80041640, lines 32-36):**
```c
for (int i = 0; i < 16; i++)
    meshTable[i].meshDataOffset += 0x80106000;
```

---

### 3. Particle List

Located at file offset = `particleListOffset` (e.g. 0x88 for STG1).

```c
struct ParticleNodeList {
    uint16 count;                // Number of particles (e.g. 41)
    ParticleNodeEntry entries[]; // count × 6 bytes
};

struct ParticleNodeEntry {
    int16 meshIndex;  // Index into mesh table (0–15)
    int16 posX;       // World X position (PSX units)
    int16 posZ;       // World Z position (PSX units)
};
```

**STG1MD.B:**
- count = 41 particles
- Each references one of the 5 valid meshes (index 0–4)
- Particle list occupies 2 + 41×6 = 248 bytes  → ends exactly at file offset 0x180

The 4 bytes immediately following the particle list (file offset 0x180 = `meshStart[0] − 4`)
are the **renderTableRelOffset** for mesh #0 (see next section).

**Mesh type=2 (animated):**
When `meshTableEntry[i].type == 2`, `FUN_800402d8` (SetupParticles) inserts 4 consecutive
`g_particleArray` nodes with different `lifetimeCounter` values (4, −1, −2, −3), creating a
multi-frame animation effect for that particle.

---

### 4. Mesh Entry Layout (per mesh blob)

Each mesh blob is preceded by exactly **4 bytes** at `meshDataOffset − 4` in RAM
(= file offset `meshOffset − 4`). These 4 bytes encode the `renderTableRelOffset`:

```
file:  ... [renderTableRelOffset: uint32] [--- meshData blob ---]
                    ^                              ^
          meshDataOffset - 4             meshDataOffset (in MeshTable)
```

```c
// At (meshDataOffset - 4):
uint32 renderTableRelOffset;   // byte offset from meshDataOffset to modelDataPtr
                               // (= to the uint32 just BEFORE the RenderTable header)
```

**Examples from STG1MD.B:**
| Mesh | meshDataOffset (RAM) | renderTableRelOffset | modelDataPtr (RAM)       |
|------|----------------------|----------------------|--------------------------|
| 0    | 0x80106184           | 0x270 (624)          | 0x80106184 + 0x270 = 0x801063F4 |
| 1    | 0x80106CE0           | 0x2E0 (736)          | 0x80106CE0 + 0x2E0 = 0x80106FC0 |

The actual **meshDataPtr** used by `RenderAndUpdateParticles` (the render dispatch pointer):
```c
meshDataPtr = modelDataPtr − 4;   // points to RenderTable.partCount
// = 0x80106184 + 0x270 - 4 = 0x801063F0  (for mesh #0)
```

---

### 5. Mesh Blob Internal Structure

```
[meshDataOffset + 0x000]:  SVECTOR data (normals, positions, UVs/colors packed as int16[])
                            ... (renderTableRelOffset − 4) bytes of raw vertex data ...
[meshDataOffset + renderTableRelOffset − 4]:
        ┌── RenderTable ──────────────────────────────────────────────┐
        │  uint32 partCount           ; e.g. 1                       │
        │  uint32 offsets[partCount]  ; offsets from &partCount      │
        │    e.g. offsets[0] = 8     ; → first MeshPart at +8        │
        └─────────────────────────────────────────────────────────────┘
[meshDataOffset + renderTableRelOffset + offsets[i]]:
        ┌── MeshPart (passed directly to RenderMesh) ─────────────────┐
        │  uint32 numSections         ; e.g. 2                       │
        │  Section[numSections]:                                      │
        │    uint16 primitiveCount    ; e.g. 20                      │
        │    uint16 typeFlags         ; lower 3 bits = type enum      │
        │    byte   primitiveData[]   ; count × sizeof(type) bytes    │
        └─────────────────────────────────────────────────────────────┘
```

**Confirmed from live RAM (mesh #0 at RenderTable, RAM 0x801063F0):**
```
0x801063F0: 01 00 00 00  → partCount = 1
0x801063F4: 08 00 00 00  → offset[0] = 8 (first MeshPart at 0x801063F8)
0x801063F8: 02 00 00 00  → numSections = 2
0x801063FC: 14 00        → Section[0].primitiveCount = 20
0x801063FE: 02 00        → Section[0].typeFlags = 0x0002 → type 2 = POLY_GT3
0x80106400: [1200 bytes of POLY_GT3 vertex data: 20 × 60 bytes]
...                      → Section[1] follows
```

---

### 6. RenderMesh Calling Convention

`RenderAndUpdateParticles` (0x800400D4) calls `RenderMesh` for each active particle:

```c
// In RenderAndUpdateParticles:
modelDataPtr  = g_particle.modelDataPtr;                    // = meshDataOffset (from InsertNodeToList)
modelDataPtr  = *(int*)(modelDataPtr - 4) + modelDataPtr;   // = meshDataOffset + renderTableRelOffset
meshDataPtr   = (uint*)(modelDataPtr - 4);                  // = pointer to RenderTable.partCount

for (meshPartIndex = 0; meshPartIndex < *meshDataPtr; meshPartIndex++) {
    RenderMesh((int*)((int)meshDataPtr + meshDataPtr[1 + meshPartIndex]), renderFlags);
}
```

---

### 7. RenderMesh Internal Logic

**Signature:** `void RenderMesh(int* meshData, uint renderFlags)` @ 0x80051CF4

```c
// meshData → MeshPart (int32* pointer)
uint32 numSections = *meshData;         // Section count for this part
short* svector     = (short*)(meshData + 1);  // First section header

while (numSections != 0) {
    uint16 primitiveCount = *svector++;   // lhu: read count
    uint16 typeFlags      = *svector++;   // lhu: read type flags

    switch (typeFlags & 7) {             // Lower 3 bits = primitive type
        case 0: // POLY_FT3 — flat textured tri
            render_POLY_FT3(svector, primitiveCount, renderFlags);
            svector += primitiveCount * 44 / 2;  // advance 44 bytes/primitive
            break;
        case 1: // POLY_FT4 — flat textured quad
            svector += primitiveCount * 52 / 2;
            break;
        case 2: // POLY_GT3 — gouraud textured tri
            svector += primitiveCount * 60 / 2;
            break;
        case 3: // POLY_GT4 — gouraud textured quad
            svector += primitiveCount * 76 / 2;
            break;
        case 4: // POLY_F3  — flat colored tri
            svector += primitiveCount * 36 / 2;
            break;
        case 5: // POLY_F4  — flat colored quad
            svector += primitiveCount * 44 / 2;
            break;
        case 6: // POLY_G3  — gouraud colored tri
            svector += primitiveCount * 60 / 2;
            break;
        case 7: // POLY_G4  — gouraud colored quad
            svector += primitiveCount * 80 / 2;
            break;
    }
    numSections--;
}
```

#### Primitive Source Data Sizes (vertex input before PSX transform):

| type | PSX Primitive | Source bytes/prim | Layout |
|------|---------------|-------------------|--------|
| 0    | POLY_FT3      | 44                | 3×SVECTOR(8) + normal(8) + clut+tpage+3×UV(12) |
| 1    | POLY_FT4      | 52                | 4×SVECTOR(8) + normal(8) + clut+tpage+4×UV(16) |
| 2    | POLY_GT3      | 60                | 3×SVECTOR(8) + 3×normal(8) + clut+tpage+3×UV(12) |
| 3    | POLY_GT4      | 76                | 4×SVECTOR(8) + 4×normal(8) + clut+tpage+4×UV(20) |
| 4    | POLY_F3       | 36                | 3×SVECTOR(8) + normal(8) + color(4) |
| 5    | POLY_F4       | 44                | 4×SVECTOR(8) + normal(8) + color(4) |
| 6    | POLY_G3       | 60                | 3×SVECTOR(8) + 3×normal(8) + 3×color(12) |
| 7    | POLY_G4       | 80                | 4×SVECTOR(8) + 4×normal(8) + 4×color(16) |

---

## Complete C Structure Definitions

```c
// ---- File header ----
struct STGxMD_Header {
    uint32 meshTableOffset;      // Always 0x08
    uint32 particleListOffset;   // Offset to ParticleNodeList from file start
};

// ---- Mesh table (128 bytes) ----
struct MeshTableEntry {
    uint32 meshDataOffset;  // File offset, relocated to RAM: 0x80106000 + offset
    uint32 type;            // 1 = static, 2 = animated (4 frames)
};
// MeshTableEntry meshTable[16];

// ---- Particle list ----
struct ParticleNodeEntry {
    int16 meshIndex;  // 0–15, index into meshTable
    int16 posX;       // World X
    int16 posZ;       // World Z
};

struct ParticleNodeList {
    uint16 count;
    ParticleNodeEntry entries[/* count */];
};

// ---- Per-mesh layout (accessed via meshDataOffset) ----
// NOTE: at (meshDataOffset - 4) in RAM:
//   uint32 renderTableRelOffset  — offset from meshDataOffset to modelDataPtr

struct RenderTable {
    uint32 partCount;
    uint32 offsets[/* partCount */];  // each: offset from &partCount to MeshPart
};

struct MeshSection {
    uint16 primitiveCount;
    uint16 typeFlags;           // bits[2:0] = type 0..7
    uint8  primitiveData[/* primitiveCount × sizeOf(type) */];
};

struct MeshPart {
    uint32 numSections;
    MeshSection sections[/* numSections */];
};
```

---

## Function Reference

### FUN_80041640 — Load Stage
**Address:** 0x80041640  
Loads STGxTX.B (textures) then STGxMD.B to 0x80106000, relocates mesh pointers,
then calls `FUN_800402d8`.

### FUN_800402d8 — Setup Particles
**Address:** 0x800402d8  
**Signature:** `void FUN_800402d8(MeshTableEntry* meshTableEntry, ParticleNodeList* particleNodeList)`  
Creates the `RenderAndUpdateParticles` task. For each particle entry, calls
`InsertNodeToList` storing `meshTableEntry[meshIndex].meshDataOffset` as the node's
`modelDataPtr`, then sets `posX`/`posZ` from the entry.  
For `type == 2` meshes, inserts 4 nodes with `lifetimeCounter` = {4, −1, −2, −3}.

### RenderAndUpdateParticles — Runtime Dispatch
**Address:** 0x800400D4  
Each frame, for every active `g_particleArray` entry: applies GTE matrix, then
iterates `RenderTable.partCount` parts and calls `RenderMesh` for each.

### RenderMesh — Draw Primitive Batch
**Address:** 0x80051CF4  
**Signature:** `void RenderMesh(int* meshData, uint renderFlags)`  
Iterates `numSections` in a `MeshPart`; for each section runs GTE transform
(`RotAverageNclip3` for tri, etc.) + lighting (`NormalColorDpq`) and adds
resulting PSX primitives to the OT (ordering table) via `AddPrim`.

---

## Related Files

- `src/game/LoadFileIntoBuffer.c` — File loading
- `config/symbols.game.jp.txt` — Symbol definitions
- `custom-tools/DbzLegendsAnalyser/PsxTools2/StgMdLoader.cs` — C# loader implementation

## References

- [DECOMPILATION_NOTES.md](DECOMPILATION_NOTES.md)
- [REVA_GHIDRA_GUIDE.md](REVA_GHIDRA_GUIDE.md)
