# Decompilation Notes & Knowledge Base

This document contains patterns, discoveries, and reference information accumulated during decompilation.
**Update this file whenever you discover something useful!**

---

## Table of Contents

1. [Compiler Behavior](#compiler-behavior)
2. [Common Patterns](#common-patterns)
3. [SDK Functions](#sdk-functions)
4. [Global Variables](#global-variables)
5. [Data Structures](#data-structures)
6. [Known Issues](#known-issues)
7. [Tips & Tricks](#tips--tricks)

---

## Compiler Behavior

### GCC 2.6.3 PSX Specifics

| Behavior | Notes |
|----------|-------|
| **Double `__main` call** | If you call `__main()` explicitly in `main()`, the compiler adds another one. Don't call it manually. |
| **Frame size calculation** | `frame = vars + (saved_regs * 4) + outgoing_args + extra`. Frame is always 8-byte aligned. |
| **Delay slot optimization** | Compiler moves instructions into delay slots aggressively. May reorder code. |
| **Register allocation** | Prefers s0-s7 for variables that span function calls. Uses t0-t7 for temporaries. |
| **Volatile pointers** | Using `volatile` may force extra registers to be saved. Avoid if not needed. |

### Optimization Levels

| Level | Effect |
|-------|--------|
| `-O0` | No optimization, large code, predictable |
| `-O1` | Basic optimization |
| `-O2` | Full optimization (default for game) |
| `-O3` | Aggressive optimization (may generate different code) |

### Known Compiler Quirks

1. **`lui + ori` vs `lui + addiu`**: 
   - `ori` for values 0x0000-0x7FFF (positive immediate)
   - `addiu` for values that can be sign-extended

2. **Small constants**:
   - Constants -32768 to 32767 use `li reg, imm` (really `addiu reg, $0, imm`)
   - Constants 0 to 65535 can use `ori reg, $0, imm`

3. **Loop variable placement**:
   - Loop counters often go in s0-s7 if the loop contains function calls

---

## Common Patterns

### Function Prologue/Epilogue

**Leaf function (no calls):**
```asm
# No stack frame needed if no locals
# Uses only t0-t7, a0-a3, v0-v1
jr ra
nop
```

**Non-leaf function:**
```asm
addiu sp, sp, -FRAME_SIZE
sw ra, FRAME_SIZE-4(sp)
sw s0, FRAME_SIZE-8(sp)    # if used
...
lw ra, FRAME_SIZE-4(sp)
lw s0, FRAME_SIZE-8(sp)
jr ra
addiu sp, sp, FRAME_SIZE   # delay slot!
```

### Infinite Loop

```asm
$L8:
    ... loop body ...
    j $L8
    nop
```
```c
while (1) {
    // loop body
}
```

### If-Else

```asm
    beqz v0, $L_else
    nop
    # if body
    j $L_end
    nop
$L_else:
    # else body
$L_end:
```
```c
if (condition) {
    // if body
} else {
    // else body
}
```

### Do-While Loop

```asm
$L_loop:
    # loop body
    bnez v0, $L_loop
    nop
```
```c
do {
    // loop body
} while (condition);
```

### Switch Statement

Look for jump tables (`jr` with computed address) or cascading `beq`/`bne`.

---

## SDK Functions

### Identified SDK Functions (TITLE.EXE)

| Address | Name | Signature | Notes |
|---------|------|-----------|-------|
| 0x8006909c | `__main` | `void __main(void)` | C runtime init, called automatically |
| 0x8006fe68 | `ResetCallback` | `void ResetCallback(void)` | Reset interrupt callbacks |
| 0x8006fda0 | `PadInit` | `void PadInit(s32 mode)` | Initialize controllers |
| 0x8006bb80 | `CdInit` | `void CdInit(void)` | Initialize CD-ROM |
| 0x8006bc88 | `CdSearchFile` | `CdlFILE* CdSearchFile(CdlFILE*, char*)` | Search file on CD |
| 0x8006fc80 | `srand` | `void srand(u32 seed)` | Seed RNG |
| 0x8006e1f0 | `InitGeom` | `void InitGeom(void)` | Initialize GTE |
| 0x80070b64 | FUN_80070b64 | `void FUN_80070b64(void)` | Unknown, called after __main |
| 0x80071648 | FUN_80071648 | `void FUN_80071648(s32)` | Unknown, takes 0 |
| 0x80071a4c | FUN_80071a4c | `void FUN_80071a4c(s32)` | Unknown, takes 0 |
| 0x80070e44 | FUN_80070e44 | `void FUN_80070e44(void)` | Display-related |
| 0x800742cc | FUN_800742cc | `void FUN_800742cc(s32, s32)` | Takes (960, 256) - screen setup |
| 0x80074370 | FUN_80074370 | `s32 FUN_80074370(s32,s32,s32,s32,s32,s32)` | Returns value stored globally |

### SDK Function Patterns

**CdSearchFile loop:**
```c
CdlFILE file;
do {
    result = CdSearchFile(&file, "\\PATH\\FILE.EXT");
} while (result == 0);
```

**Double-buffered display init:**
```c
SetDefDispEnv(&dispEnv[0], 0, 0, 320, 240);
SetDefDispEnv(&dispEnv[1], 0, 240, 320, 240);
SetDefDrawEnv(&drawEnv[0], 0, 240, 320, 240);
SetDefDrawEnv(&drawEnv[1], 0, 0, 320, 240);
```

---

## Global Variables

### Memory Layout (TITLE.EXE)

| Address | Name | Type | Notes |
|---------|------|------|-------|
| 0x80083498 | DAT_80083498 | u32 | Result from FUN_80074370 |
| 0x80083504 | DAT_80083504 | u32 | Cleared in main loop |
| 0x800ef10e | DAT_800ef10e | u16 | Frame counter (0-2 range) |
| 0x801ff100 | GPU_STATUS | u16* | GPU status/control write |
| 0x1f80012c | HW_REG_012C | u32* | Hardware register |

### GP-Relative Accesses

The Global Pointer (GP/$28) is used for efficient access to global variables.
Pattern: `lw/sw reg, offset(gp)`

To find the actual address:
```
actual_address = GP_value + signed_offset
```

**GP value varies per overlay!** Check the executable header or find it during analysis.

---

## Data Structures

### CdlFILE (24 bytes)

```c
typedef struct CdlFILE {
    CdlLOC pos;      // 4 bytes - CD position
    u32 size;        // 4 bytes - file size
    char name[16];   // 16 bytes - filename
} CdlFILE;
```

### DISPENV (20 bytes)

```c
typedef struct DISPENV {
    RECT disp;       // 8 bytes - display area
    RECT screen;     // 8 bytes - screen area
    u8 isinter;      // 1 byte
    u8 isrgb24;      // 1 byte
    u8 pad0, pad1;   // 2 bytes padding
} DISPENV;
```

### DRAWENV (92 bytes)

```c
typedef struct DRAWENV {
    RECT clip;       // 8 bytes
    s16 ofs[2];      // 4 bytes
    RECT tw;         // 8 bytes
    u16 tpage;       // 2 bytes
    u8 dtd;          // 1 byte
    u8 dfe;          // 1 byte
    u8 isbg;         // 1 byte
    u8 r0, g0, b0;   // 3 bytes
    DR_ENV dr_env;   // 64 bytes
} DRAWENV;
```

---

## Known Issues

### Matching Difficulties

1. **`main()` function**: Very hard to match due to:
   - Automatic `__main` insertion
   - GP-relative variable accesses
   - Many saved registers

2. **Infinite loops**: Compiler may optimize differently based on code around the loop

3. **Struct access**: Field order and padding affect generated code

### Workarounds

1. **Use INCLUDE_ASM** for stubborn functions:
   ```c
   INCLUDE_ASM("asm/jp/title", FUN_80037388);
   ```

2. **Try different optimization levels**: Some functions compile better with `-O1`

3. **Volatile for hardware registers**: 
   ```c
   *(volatile u16*)0x801ff100 = 2;
   ```

---

## Tips & Tricks

### Quick Matching Checks

1. **Compare frame size first**: `addiu sp,sp,-N`
2. **Count saved registers**: Each `sw sX, Y(sp)` in prologue
3. **Check call order**: `jal` instructions should be in same sequence

### Decompilation Shortcuts

1. **Address calculation**: `lui + addiu` = 32-bit address
   ```asm
   lui a0, 0x8003
   addiu a0, a0, 0x7388
   # a0 = 0x80037388
   ```

2. **Branch delay slots**: Instruction after branch/jump executes BEFORE the branch
   ```asm
   jal SomeFunc
   move a0, v0    # This executes BEFORE the call! Sets up a0 for SomeFunc
   ```

3. **Sign extension tricks**:
   - `sltiu` with 3 → checking if value is 0, 1, or 2 (< 3)
   - `slti` for signed comparisons

### Common Mistakes

1. ❌ Declaring too many local variables → extra stack space
2. ❌ Using `volatile` unnecessarily → extra register saves
3. ❌ Wrong signedness → different comparison instructions
4. ❌ Calling `__main()` explicitly → double call in output

---

## Overlay-Specific Notes

### TITLE.EXE

- VRAM start: 0x80020000
- Entry point: 0x80068FF4 (start function)
- Main at: 0x800581DC
- Total game functions: ~652

### GAME.EXE

- VRAM start: 0x80020000
- Contains battle system, character logic

### MAIN (SLPS_003.55)

- Boot executable
- Loads other overlays

---

## Discovered Constants

| Value | Meaning |
|-------|---------|
| 0x3C0 | 960 - screen width in some mode |
| 0x100 | 256 - common height/size |
| 0x140 | 320 - standard screen width |
| 0xC8 | 200 - screen height variant |
| 0x1000 | 4096 - fixed point ONE (12-bit fraction) |

---

## Function Categories

### Title Screen (TITLE.EXE)

| Function | Purpose | Status |
|----------|---------|--------|
| main (0x800581DC) | Main entry, init + loop | NON_MATCHING |
| FUN_80037388 | Title state init | TODO |
| FUN_80038228 | State machine update | TODO |
| FUN_800587a8 | Title update 1 | TODO |
| FUN_80058a9c | Title update 2 | TODO |
| FUN_80021dd0 | Unknown | TODO |

---

## Update Log

| Date | Discovery |
|------|-----------|
| 2025-12-25 | Created initial document |
| 2025-12-25 | Documented main() structure in TITLE.EXE |
| 2025-12-25 | Identified SDK functions in TITLE.EXE |
| 2025-12-25 | Noted compiler double __main issue |

---

*Last updated: 2025-12-25*
