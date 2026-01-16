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

### TitleAudioBlock (formerly Struct_gp_018c)

```c
/* Discovered in: FUN_80054dd0 / FUN_80054f04 / FUN_80054fc8 / FUN_80055110 / FUN_80053304
 * Size: 0x112 bytes (used as an array: DAT_gp_018c[0] and DAT_gp_018c[1])
 * Location: Pointer at GP+0x18C
 * Usage: Audio/CD work area (block 0 = CD/BGM state; block 1 = SFX/requests state).
 */
typedef union {
    struct {
        /* 0x0018 */ CdlFILE bgm_file;          // Used with CdSearchFile("\\SOUND\\BGM.B;1")
        /* 0x0078 */ CdlLOC cd_loc;             // Used with CdControl(CdlSetloc)
        /* 0x007C */ u32 cd_read_sectors;       // Written as 2 or 10 before CdRead
        /* 0x0090 */ CdlLOC cd_base_loc;        // Base for CdPosToInt
        /* 0x0108 */ s16 seq_id_108;            // Masked with 0x7f
        /* 0x010A */ s16 vab_id_10A;            // Sound bank id
        /* 0x0110 */ s16 timer_110;             // Set to 0x14/0x15 when zero
    } cd;

    struct {
        /* 0x0018 */ s16 cd_state_18;           // State machine used by FUN_80055110
        /* 0x001A */ s16 handles_1A[6];         // Checked against -1
        /* 0x002A */ s16 retry_counter_2A;      // Decremented, reloaded to 10
        /* 0x0030 */ u8 volume_r_30;
        /* 0x0031 */ u8 volume_l_31;
        /* 0x0032 */ u8 color_r_32;
        /* 0x0033 */ u8 color_g_33;
        /* 0x0034 */ u8 color_b_34;
        /* 0x0035 */ u8 color_a_35;
        /* 0x0036 */ u16 requests_36[6];        // 0x80 bit indicates pending request
        /* 0x0042 */ s16 sample_id_42;          // Checked against -1
        /* 0x0044 */ s16 request_kind_44;
        /* 0x0046 */ s16 voice_group_46;        // Checked against -1
        /* 0x0110 */ s16 timer_110;
    } sfx;

    /* Note: block 1 has additional unknown parameters at 0x08..0x16 and two
     * one-byte flags at 0x14..0x15 (see TitleAudioSfxBlock in include/game.h).
     */

    /* Raw view (full 0x112 bytes) */
    u8 raw[0x112];
} TitleAudioBlock;
```

### TitleMenuState (formerly Struct_53020)

```c
/* Discovered in: FUN_80053020 (+ usage observed in caller FUN_8004c168)
 * Size: At least 0x3030 bytes
 * Usage: Selects left/right cursor based on a signed balance value at 0x302C.
 */
typedef struct {
    /* 0x0002 */ s16 blink_timer;        // Decremented; often initialized to 0x10
    /* 0x0006 */ s16 countdown_06;       // Countdown used in menu state transitions
    /* 0x0010 */ u32 flags_10;           // Bitfield
    /* 0x0014 */ u16 cursor_left;        // Typically [0..5]
    /* 0x0016 */ u16 cursor_right;       // Typically [6..11]
    /* 0x0018 */ u16 selected_index;     // Current selection index
    /* 0x001A */ u16 active_index;       // Written from FUN_80053020 result
    /* 0x302C */ s32 side_balance_302C;  // Clamped to +/-30000, compared to 30000
} TitleMenuState;
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
