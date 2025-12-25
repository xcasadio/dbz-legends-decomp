# Structures & Types

This file documents all structures, enums, and custom types discovered during decompilation.
**Update this file whenever a new structure is identified.**

---

## How to Document Structures

When you identify a structure in a function:

1. Add it to this file with:
   - Structure name
   - Field offsets and types
   - Size
   - Source function where discovered
   - Any known field names

2. Add the structure to `include/game.h` for use in decompilation

---

## Discovered Structures

### Template

```c
/* Discovered in: FUN_XXXXXXXX
 * Size: 0xXX bytes
 * Usage: Description of what this structure represents
 */
typedef struct {
    /* 0x00 */ u32 field_00;
    /* 0x04 */ s16 field_04;
    /* 0x06 */ s16 field_06;
    /* 0x08 */ void* field_08;
    /* 0x0C */ u8 field_0C[4];
} StructName; /* size = 0x10 */
```

---

## Game Structures

*(Add structures here as they are discovered)*

### Struct_gp_018c

```c
/* Discovered in: FUN_80054dd0
 * Size: At least 0x112 bytes
 * Location: Pointer at GP+0x18C
 * Usage: Unknown, field_110 initialized to 0x14
 */
typedef struct {
    /* 0x000 */ u8 pad[0x110];
    /* 0x110 */ s16 field_110;    // Set to 0x14 if zero
} Struct_gp_018c;
```

### Struct_53020

```c
/* Discovered in: FUN_80053020
 * Size: At least 0x3030 bytes
 * Usage: Returns field_14 or field_16 based on field_302C == 0x7530
 */
typedef struct {
    /* 0x0000 */ u8 pad0[0x14];
    /* 0x0014 */ u16 field_14;
    /* 0x0016 */ u16 field_16;
    /* 0x0018 */ u8 pad1[0x302C - 0x18];
    /* 0x302C */ s32 field_302C;    // Compared to 0x7530 (30000)
} Struct_53020;
```

### Example: Unknown Structure 1

```c
/* Discovered in: FUN_XXXXXXXX
 * Size: Unknown
 * Usage: Unknown
 *
 * Evidence:
 * - lw v0,0x04(a0)  -> field at offset 0x04
 * - sh t0,0x10(a0)  -> halfword at offset 0x10
 */
typedef struct {
    /* 0x00 */ u32 field_00;
    /* 0x04 */ u32 field_04;     // accessed as word
    /* 0x08 */ u32 field_08;
    /* 0x0C */ u32 field_0C;
    /* 0x10 */ s16 field_10;     // accessed as halfword
    /* 0x12 */ s16 field_12;
} UnknownStruct1;
```

---

## Enums

*(Add enums here as they are discovered)*

### Template

```c
/* Discovered in: FUN_XXXXXXXX
 * Usage: Description
 */
typedef enum {
    ENUM_VALUE_0 = 0,
    ENUM_VALUE_1 = 1,
    ENUM_VALUE_2 = 2,
} EnumName;
```

---

## Global Data Structures

*(Document known global arrays and their element types)*

### Template

```c
/* Address: 0x800XXXXX
 * Discovered in: FUN_XXXXXXXX
 * Element size: 0xXX
 * Count: XX elements
 */
extern StructType g_ArrayName[COUNT];
```

---

## Detection Patterns

### How to Identify Structures in Assembly

| Pattern | Meaning |
|---------|---------|
| `lw v0,0xNN(a0)` | Field at offset 0xNN, word size |
| `lh v0,0xNN(a0)` | Field at offset 0xNN, signed halfword |
| `lhu v0,0xNN(a0)` | Field at offset 0xNN, unsigned halfword |
| `lb v0,0xNN(a0)` | Field at offset 0xNN, signed byte |
| `lbu v0,0xNN(a0)` | Field at offset 0xNN, unsigned byte |
| `sw v0,0xNN(a0)` | Store word at offset 0xNN |
| `addiu t0,a0,0xNN` | Pointer to sub-structure at offset 0xNN |
| `sll t0,v0,2` then `addu t0,a0` | Array access with 4-byte elements |
| `sll t0,v0,3` then `addu t0,a0` | Array access with 8-byte elements |

### Common Structure Sizes

| Size | Likely Type |
|------|-------------|
| 0x04 | Simple word/pointer |
| 0x08 | Pair of words, VECTOR2 |
| 0x0C | SVECTOR (3 shorts + padding) |
| 0x10 | VECTOR (3 words + padding) |
| 0x20 | MATRIX (3x3 rotation + translation) |

---

## Notes

- Field names like `field_XX` are placeholders until purpose is known
- Update field names when their purpose becomes clear
- Cross-reference with Ghidra for additional context
