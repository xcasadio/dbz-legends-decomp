# Decompilation Guide

This guide explains how to decompile PSX functions for DBZ Legends and achieve matching code.

## Overview

The decompilation process:
1. **Extract** the original assembly
2. **Analyze** the assembly to understand the function
3. **Write** C code that should compile to the same assembly
4. **Compile** and compare
5. **Iterate** until matching (max 10 iterations)

---

## Step 1: Extract the Function

```bash
python tools/extract_func.py <overlay> <address> <size> --name <name> --save
```

Example:
```bash
python tools/extract_func.py title 0x80021DD0 0x58 --name FUN_80021dd0 --save
```

Output: `asm/jp/title/FUN_80021dd0.s`

---

## Step 2: Analyze the Assembly

### Key Things to Identify

| Pattern | Meaning |
|---------|---------|
| `addiu sp,sp,-N` | Function prologue, N = stack frame size |
| `sw ra,X(sp)` | Non-leaf function (calls other functions) |
| `jr ra` | Function return |
| `jal 0xXXXXXXXX` | Function call |
| `lui + addiu/ori` | Loading 32-bit address or constant |
| `lw/sw X(gp)` | Global variable access (GP-relative) |
| `lhu/sh` | 16-bit (halfword) access |
| `lb/sb` | 8-bit (byte) access |

### Register Conventions (MIPS o32)

| Register | Name | Usage |
|----------|------|-------|
| $0 | zero | Always 0 |
| $2-$3 | v0-v1 | Return values |
| $4-$7 | a0-a3 | Function arguments |
| $8-$15 | t0-t7 | Temporaries (caller-saved) |
| $16-$23 | s0-s7 | Saved registers (callee-saved) |
| $28 | gp | Global pointer |
| $29 | sp | Stack pointer |
| $31 | ra | Return address |

### Stack Frame Layout

```
High addresses
+------------------+
| Argument 5+      | <- sp + frame_size + 16
+------------------+
| Saved ra         | <- sp + frame_size - 4
+------------------+
| Saved s0-sN      |
+------------------+
| Local variables  |
+------------------+
| Outgoing args    | <- sp + 0 to sp + 16
+------------------+
Low addresses
```

---

## Step 3: Write C Code

Create or edit `src/<overlay>/<overlay>.c`:

```c
#include "common.h"
#include "game.h"

/* Declare external functions */
extern void SomeFunction(s32 arg);

/* Declare external globals */
extern s32 g_SomeGlobal;

/* The function to decompile */
void FUN_80021dd0(void) {
    // Your decompiled code here
}
```

### Common Patterns

**Function with no args, no return:**
```c
void FuncName(void) {
    // ...
}
```

**Function with args:**
```c
s32 FuncName(s32 a0, s32 a1, void* a2) {
    // a0 = $4, a1 = $5, a2 = $6
    return result;  // result goes to $2 (v0)
}
```

**Stack args (5+ arguments):**
```c
void FuncName(s32 a0, s32 a1, s32 a2, s32 a3, s32 stack_arg1, s32 stack_arg2) {
    // stack_arg1 at sp+16, stack_arg2 at sp+20
}
```

---

## Step 4: Compile and Compare

### Compile to Assembly

```bash
docker run --rm -v "$(pwd):/project" -w /project dbz-legends-build /bin/bash -c \
  "mips-linux-gnu-cpp -Iinclude -Iinclude/psxsdk -undef -D__GNUC__=2 -D__OPTIMIZE__ -DPSX \
   src/<overlay>/<overlay>.c -o /tmp/out.i && \
   /usr/local/bin/cc1-psx-26 -O2 -G0 -quiet -mcpu=3000 -mgas -msoft-float \
   /tmp/out.i -o /project/build/<overlay>_compiled.s"
```

### Quick Comparison

**View original:**
```bash
cat asm/jp/<overlay>/<function>.s
```

**View compiled:**
```bash
grep -A 100 "^<function>:" build/<overlay>_compiled.s
```

### Side-by-Side Diff

```bash
# Extract just the function from compiled output
# Compare instruction sequences
```

---

## Step 5: Iterate (Max 10 Attempts)

### Iteration Checklist

For each iteration, check:

- [ ] **Frame size matches** (`addiu sp,sp,-N`)
- [ ] **Same registers saved** (s0-s7, ra)
- [ ] **Same function call order**
- [ ] **Same branch structure**
- [ ] **Correct variable types** (s32 vs u32 vs s16 vs u16)
- [ ] **Correct pointer types**
- [ ] **Volatile where needed**

### Common Fixes

| Problem | Solution |
|---------|----------|
| Wrong frame size | Adjust local variables, remove unused ones |
| Extra registers saved | Reduce variable usage, use temporaries |
| Different branch order | Restructure if/else, try negating conditions |
| Wrong instruction | Change variable type (signed vs unsigned) |
| Missing delay slot fill | Compiler optimization, try reordering statements |

### When to Stop

- ✅ **MATCHING**: All bytes identical
- ⚠️ **EQUIVALENT**: Same logic, minor instruction differences (acceptable temporarily)
- ❌ **FAILED after 10 iterations**: Mark as NON_MATCHING, move on

---

## Compiler Flags Reference

| Flag | Meaning |
|------|---------|
| `-O2` | Optimization level 2 |
| `-G0` | No GP-relative addressing for small data |
| `-mcpu=3000` | Target MIPS R3000 (PSX CPU) |
| `-mgas` | Output GAS-compatible assembly |
| `-msoft-float` | Software floating point |

### Trying Different Optimization

Sometimes `-O1` or `-O0` works better:
```bash
/usr/local/bin/cc1-psx-26 -O1 -G0 ...  # Less optimization
```

---

## Marking Function Status

In the source file, use comments:

```c
/* MATCHING - Verified byte-identical */
void FUN_80021dd0(void) { ... }

/* EQUIVALENT - Logic matches, minor differences */
void FUN_80022630(void) { ... }

/* NON_MATCHING - Needs more work */
void FUN_80037388(void) { ... }

/* TODO - Not yet attempted */
// INCLUDE_ASM("asm/jp/title", FUN_80038228);
```

---

## For AI Assistants

### Decompilation Workflow

1. **Extract**: `python tools/extract_func.py <overlay> <addr> <size> --name <name> --save`

2. **Analyze**: Read the assembly, identify:
   - Function signature (args, return type)
   - Called functions
   - Global variables accessed
   - Control flow (loops, branches)

3. **Write C**: Create initial decompilation

4. **Compile**: 
```bash
docker run --rm -v "$(pwd):/project" -w /project dbz-legends-build /bin/bash -c \
  "mips-linux-gnu-cpp -Iinclude -Iinclude/psxsdk -undef -D__GNUC__=2 -D__OPTIMIZE__ -DPSX \
   src/<overlay>/<overlay>.c -o /tmp/out.i && \
   /usr/local/bin/cc1-psx-26 -O2 -G0 -quiet -mcpu=3000 -mgas -msoft-float \
   /tmp/out.i -o -" 2>&1 | grep -A 50 "^<funcname>:"
```

5. **Compare**: Check prologue first (frame size, saved regs), then body

6. **Iterate**: Max 10 attempts, then mark NON_MATCHING

### Key Rules

- **Always check `docs/DECOMPILATION_NOTES.md`** for known patterns
- **Update notes** when discovering new patterns
- **Frame size = vars + regs + args + extra** (from `.frame` directive)
- **Delay slots**: Instruction after `jal`/`j`/`b*` executes before branch

---

## Example: Full Decompilation Session

### 1. Extract
```bash
python tools/extract_func.py title 0x80021DD0 0x58 --name FUN_80021dd0 --save
```

### 2. Assembly Analysis
```
80021dd0: addiu sp,sp,-32       # Frame = 32 bytes
80021de4: sw ra,24(sp)          # Saves ra (non-leaf)
80021de8: jal 0x80057c80        # Calls FUN_80057c80
80021e10: jal 0x80049504        # Calls FUN_80049504
80021e20: jr ra                 # Returns
```

### 3. Initial C Code
```c
extern void FUN_80057c80(void* arg);
extern void FUN_80049504(void*, s32, s32, s32, s32, s32);
extern s32 DAT_80110004;
extern s32 DAT_800898c0;

void FUN_80021dd0(void) {
    FUN_80057c80((void*)(DAT_80110004 + 0x80110000));
    FUN_80049504((void*)0x80021e28, 0, 6, 0x70, 0, DAT_800898c0);
}
```

### 4. Compile and Check
```bash
docker run --rm ... # compile command
```

### 5. Compare and Adjust
- If frame size differs: adjust variables
- If instructions differ: adjust types, order
- Repeat until matching or 10 iterations
