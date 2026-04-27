# TITLE.B File Format Analysis

## Summary

`SUB/TITLE.B` is a small title/menu resource container loaded by `TITLE.EXE` into `DAT_80110000`.
The file size is `0x25000` bytes.

The first two words are not palette data:

| Offset | Value | Meaning |
| --- | --- | --- |
| `0x0000` | `0x000001A4` | Offset of the 5-entry sprite-group table |
| `0x0004` | `0x00000008` | Offset of the image load script interpreted by `FUN_80057c80` |

This gives the file two top-level sections:

1. A 6-entry VRAM load script at `0x0008`
2. A 5-entry sprite-group table at `0x01A4`, with the group bodies stored earlier in the file at `0x00B4..0x018C`

## Top-Level Layout

| Range | Purpose |
| --- | --- |
| `0x0000..0x0007` | Header with offsets to the group table and load script |
| `0x0008..0x00B3` | `TitleBLoadScript` (`count = 6`, then 6 entries of 7 dwords) |
| `0x00B4..0x018B` | 5 sprite-group bodies used by the title screen |
| `0x018C..0x01A3` | Final sprite-group body |
| `0x01A4..0x01B7` | 5-entry sprite-group offset table: `[0xB4, 0xCC, 0xE4, 0x138, 0x18C]` |
| `0x01B8..0x03B7` | 256-color CLUT for the 320x240 background |
| `0x03B8..0x05B7` | 256-color CLUT for the 256x256 logo/UI atlas |
| `0x05B8..0x05D7` | 16-color CLUT for the compressed 256x128 message atlas |
| `0x05D8..0x131D7` | Raw 8bpp image, `320x240` |
| `0x131D8..0x231D7` | Raw 8bpp image, `256x256` |
| `0x231D8..0x24FFF` | LZSS-compressed 4bpp image, decompresses to `0x4000` bytes (`256x128`) |

## Image Load Script

The structure at `0x0008` matches the helper used by `FUN_80021dd0`:

```c
typedef struct {
    uint32_t kind;       // 0 = LZSS-compressed, 1 = raw
    uint32_t dataOffset; // Offset inside TITLE.B
    uint32_t vramX;
    uint32_t vramY;
    uint32_t widthWords; // PSX VRAM width in 16-bit words
    uint32_t height;
    uint32_t isClut;     // 0 = image upload, 1 = CLUT upload
} TitleBLoadEntry;

typedef struct {
    uint32_t count;
    TitleBLoadEntry entries[count];
} TitleBLoadScript;
```

For every entry, the uploaded byte count is:

`widthWords * height * 2`

Verified entries:

| Index | Raw values | Meaning |
| --- | --- | --- |
| `0` | `{1, 0x5D8, 0x180, 0x000, 0x0A0, 0x0F0, 0}` | Raw image upload at `(384, 0)`, `320x240`, 8bpp |
| `1` | `{1, 0x1B8, 0x180, 0x0F0, 0x100, 0x001, 1}` | Raw 256-color CLUT at `(384, 240)` for entry 0 |
| `2` | `{1, 0x131D8, 0x180, 0x100, 0x080, 0x100, 0}` | Raw image upload at `(384, 256)`, `256x256`, 8bpp |
| `3` | `{1, 0x3B8, 0x180, 0x0F1, 0x100, 0x001, 1}` | Raw 256-color CLUT at `(384, 241)` for entry 2 |
| `4` | `{0, 0x231D8, 0x2C0, 0x100, 0x040, 0x080, 0}` | LZSS image upload at `(704, 256)`, `256x128`, 4bpp |
| `5` | `{1, 0x5B8, 0x2C0, 0x180, 0x010, 0x001, 1}` | Raw 16-color CLUT at `(704, 384)` for entry 4 |

The three real image resources are therefore:

1. Background image: `320x240`, 8bpp, uses entry 1 CLUT
2. Logo/UI atlas: `256x256`, 8bpp, uses entry 3 CLUT
3. Memory-card/status atlas: `256x128`, 4bpp, LZSS-compressed, uses entry 5 CLUT

Visual confirmation already exists under `TITLE_exports/`:

| File | Meaning |
| --- | --- |
| `TITLE_exports/TITLE_img0_320x240_8bpp_pal0_idx0transparent.png` | Large Goku title-screen background |
| `TITLE_exports/TITLE_img2_256x256_8bpp_pal1_idx0transparent.png` | Dragon Ball Z logo, copyright text, and `PRESS START BUTTON` atlas |
| `TITLE_exports/TITLE_img4_256x128_4bpp_pal2_idx0transparent.png` | Japanese memory-card/status message atlas |
| `TITLE_exports/TITLE_contact_sheet.png` | Combined verification sheet |

## Sprite-Group Table

The word at `0x0000` points to 5 group offsets:

```c
uint32_t groupOffsets[5] = { 0xB4, 0xCC, 0xE4, 0x138, 0x18C };
```

Only these five valid groups were found in the file.
They are used by the title screen composition code reached through `FUN_80021e28`.

| Group | Offset | Count | Observed use |
| --- | --- | --- | --- |
| `0` | `0x0B4` | `1` | Left/main `256x240` slice of the background |
| `1` | `0x0CC` | `1` | Right `65x240` background strip |
| `2` | `0x0E4` | `4` | Top logo composite from the `256x256` atlas |
| `3` | `0x138` | `4` | Lower logo/copyright composite from the `256x256` atlas |
| `4` | `0x18C` | `1` | `PRESS START BUTTON` strip |

The memory-card/status atlas from entries 4 and 5 is not referenced by this initial 5-group table.
It is a separate title/menu resource, not part of the first static title-screen composition.

## Sprite Record Layout

`FUN_80048f88` matches the on-disk group format:

```c
typedef struct {
    uint8_t u;
    uint8_t v;
    uint8_t localX;
    uint8_t localY;
    uint16_t clutId;
    uint16_t packedTPage;
    // Optional when ((packedTPage >> 9) & 0x78) == 0:
    uint16_t width;
    uint16_t height;
    uint16_t rotZ;
    uint16_t aux;
    uint16_t scaleX;
    uint16_t scaleY;
} TitleBSpriteRecord;

typedef struct {
    uint32_t count;
    TitleBSpriteRecord sprites[count];
} TitleBSpriteGroup;
```

Notes:

1. `packedTPage` low 9 bits are the PSX TPage value.
2. `((packedTPage >> 9) & 0x78)` carries an implicit square size when non-zero.
3. All sprite records observed in `TITLE.B` use the explicit `width` and `height` form, so their record size is `20` bytes.
4. `rotZ` is always `0` in this file.
5. `scaleX` and `scaleY` are always `0x1000` in this file.
6. `aux` is observed as `0` or `1`; its exact meaning is still unproven.

Verified groups:

| Group | Sprite summary |
| --- | --- |
| `0 @ 0x0B4` | 1 sprite, `clut=0x3C18`, `tpage=0x086`, explicit size `(256, 240)` |
| `1 @ 0x0CC` | 1 sprite, `clut=0x3C18`, `tpage=0x088`, explicit size `(65, 240)` |
| `2 @ 0x0E4` | 4 sprites, `clut=0x3C58`, `tpage=0x0F6`, sizes `(160,80)`, `(160,8)`, `(160,8)`, `(160,80)` |
| `3 @ 0x138` | 4 sprites, `clut=0x3C58`, `tpage=0x0F6`, sizes `(161,40)`, `(161,8)`, `(160,8)`, `(160,40)` |
| `4 @ 0x18C` | 1 sprite, `clut=0x3C58`, `tpage=0x0F6`, explicit size `(160, 16)` |

## Runtime Linkage

The currently verified runtime path is:

1. `TITLE.EXE` `main` calls `ReadFile("\\SUB\\TITLE.B;1", &DAT_80110000, 0)`
2. `FUN_80021dd0` reads `DAT_80110004`, which is the `0x08` load-script offset
3. `FUN_80021dd0` passes `DAT_80110000 + 0x08` to `FUN_80057c80`
4. `FUN_80057c80` uploads the three image/CLUT pairs into VRAM
5. `FUN_80021e28` uses `DAT_80110000 + 0x1A4` as the 5-entry sprite-group table
6. `FUN_80048f88` consumes the group records and builds the title-screen sprites

## LZSS Note

The last image block (`0x231D8`) uses the same format as `custom-tools/DbzLegendsAnalyser/PsxTools/LzssDecompressor.cs`.
The compressed block begins with a 16-bit command count and decompresses to exactly `0x4000` bytes, matching a `4bpp 256x128` image.

## Open Point

The only field still not fully explained is the high 16-bit halfword stored next to `rotZ` in each sprite record (`aux` above).
For `TITLE.B` it is only observed as `0` or `1`, and the rest of the format is already stable without it.