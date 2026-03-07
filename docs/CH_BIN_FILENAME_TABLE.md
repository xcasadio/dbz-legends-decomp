# CH_BIN Filename Table Reference

**Status**: PARTIALLY COMPLETE - Physical files documented, code references needed  
**Date**: 2026-02-13  
**Related**: CH_BIN_FORMAT_ANALYSIS.md

## Overview

This document catalogs all CH_BIN character model files found on the game disc and tracks the relationship between `ch_bin_file_index` values and actual filenames.

## Complete File Inventory

### CH_BIN1 Directory (28 files)

Located in `\CH_BIN1\` on CD-ROM.

| Index | Filename     | Size  | Character | Status |
|-------|--------------|-------|-----------|--------|
| 01    | CH_01.BIN    | 20480 | ?         | ✅ Analyzed |
| 02    | CH_02.BIN    | 20480 | ?         | ✅ Analyzed |
| 03    | CH_03.BIN    | 20480 | ?         | Known   |
| 04    | CH_04.BIN    | 20480 | ?         | Known   |
| 05    | CH_05.BIN    | 20480 | ?         | Known   |
| 06    | CH_06.BIN    | 20480 | ?         | Known   |
| 07    | CH_07.BIN    | 20480 | ?         | Known   |
| 08    | *(missing)*  | -     | -         | ⚠️ Gap  |
| 09    | CH_09.BIN    | 20480 | ?         | Known   |
| 10    | CH_10.BIN    | 20480 | ?         | Known   |
| 11    | CH_11.BIN    | 20480 | ?         | Known   |
| 12    | CH_12.BIN    | 20480 | ?         | Known   |
| 13    | CH_13.BIN    | 20480 | ?         | Known   |
| 14    | CH_14.BIN    | 20480 | ?         | Known   |
| 15    | CH_15.BIN    | 20480 | ?         | Known   |
| 16    | CH_16.BIN    | 20480 | ?         | Known   |
| 17    | CH_17.BIN    | 20480 | ?         | Known   |
| 18    | CH_18.BIN    | 20480 | ?         | Known   |
| 19    | CH_19.BIN    | 20480 | ?         | Known   |
| 20    | CH_20.BIN    | 20480 | ?         | Known   |
| 21    | CH_21.BIN    | 20480 | ?         | Known   |
| 22    | CH_22.BIN    | 20480 | ?         | Known   |
| 23    | CH_23.BIN    | 20480 | ?         | Known   |
| 24    | CH_24.BIN    | 20480 | ?         | Known   |
| 25    | CH_25.BIN    | 20480 | ?         | Known   |
| 26    | CH_26.BIN    | 20480 | ?         | Known   |
| 27    | CH_27.BIN    | 20480 | ?         | Known   |
| 28    | CH_28.BIN    | 20480 | ?         | Known   |
| 29    | CH_29.BIN    | 20480 | ?         | Known   |
| --    | CH_NO.BIN    | 20480 | None/Dummy| Known   |

**Total**: 28 files (27 numbered + 1 special)

### CH_BIN2 Directory (22 files)

Located in `\CH_BIN2\` on CD-ROM.

| Index | Filename     | Size  | Character | Status |
|-------|--------------|-------|-----------|--------|
| 30    | CH_30.BIN    | 20480 | ?         | Known   |
| 31    | CH_31.BIN    | 20480 | ?         | Known   |
| 32    | CH_32_1.BIN  | 20480 | ? form 1  | Known   |
| 32    | CH_32_2.BIN  | 20480 | ? form 2  | Known   |
| 32    | CH_32_3.BIN  | 20480 | ? form 3  | Known   |
| 33    | CH_33.BIN    | 20480 | ?         | Known   |
| 34    | CH_34.BIN    | 20480 | ?         | Known   |
| 35    | CH_35.BIN    | 20480 | ?         | Known   |
| 36    | CH_36.BIN    | 20480 | ?         | Known   |
| 37    | CH_37.BIN    | 20480 | ?         | Known   |
| 38    | CH_38.BIN    | 20480 | ?         | Known   |
| 39    | CH_39.BIN    | 20480 | ?         | Known   |
| 40    | *(missing)*  | -     | -         | ⚠️ Gap  |
| 41    | CH_41.BIN    | 20480 | ?         | Known   |
| 42    | CH_42.BIN    | 20480 | ?         | Known   |
| 43    | CH_43.BIN    | 20480 | ?         | Known   |
| 44    | CH_44.BIN    | 20480 | ?         | Known   |
| 45    | CH_45.BIN    | 20480 | ?         | Known   |
| 46    | CH_46.BIN    | 20480 | ?         | Known   |
| 47    | CH_47.BIN    | 20480 | ?         | Known   |
| 48    | CH_48.BIN    | 20480 | ?         | Known   |
| 49    | CH_49.BIN    | 20480 | ?         | Known   |
| 50    | CH_50.BIN    | 20480 | ?         | Known   |

**Total**: 22 files (19 numbered + 3 CH_32 variants)

**Notes on CH_32 variants**:
- Likely used for character transformations (e.g., SSJ levels, forms)
- Game code probably selects variant based on transformation state
- All three are same size, suggesting similar structure

### CH_BIN3 Directory (13 files)

Located in `\CH_BIN3\` on CD-ROM.

| Index | Filename     | Size  | Character     | Usage         |
|-------|--------------|-------|---------------|---------------|
| --    | IN_01.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_02.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_03.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_04.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_05.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_06.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_07.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_08.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_09.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_10.BIN    | 20480 | ?             | Intro/Special |
| --    | IN_IN.BIN    | 20480 | ?             | Intro Scene?  |
| --    | IN_OT2.BIN   | 20480 | ?             | Outro Type 2? |
| --    | IN_OUT.BIN   | 20480 | ?             | Outro Scene?  |

**Total**: 13 files

**Hypothesis**: IN_* files used for:
- Story mode intros/outros
- Special cutscene models (different from battle models)
- Camera/audience models
- Environment characters

## File Statistics

```
Total files found: 63
- CH_BIN1:  28 files (includes CH_NO.BIN)
- CH_BIN2:  22 files (includes 3x CH_32 variants)
- CH_BIN3:  13 files (all IN_* prefixed)

Consistent size: 20480 bytes (0x5000, 10 CD sectors)
Missing indices: CH_08, CH_40
```

## Numbering Gaps

### CH_08.BIN - Missing

**Possible reasons**:
1. Development placeholder never used
2. Cut character removed late in development
3. Reserved for DLC/expansion (unlikely for PS1)
4. Index intentionally skipped for superstitious reasons

### CH_40.BIN - Missing

**Possible reasons**:
1. Similar to CH_08 - cut content
2. May have been merged into CH_39 or CH_41
3. Round number (40) reserved for special purposes

## g_ch_bin_filenames Array (CODE REFERENCE NEEDED)

**Status**: ⚠️ NOT YET LOCATED IN CODE

From `LoadCHBinFileAsync` analysis (0x80035828):
```c
// Line 38: Uses ch_bin_file_index to access filename array
char* filename = g_ch_bin_filenames[ch_bin_file_index];
SearchFileAndLoadIntoBuffer(filename, &g_cdFileBufferTable, 1);

// Line 44: Fallback on file not found
if (file_handle == 0xFFFFFFFF) {
    ch_bin_file_index = 0;
    filename = g_ch_bin_filenames[0];  // Load first file as default
}
```

**Array structure hypothesis**:
```c
char* g_ch_bin_filenames[] = {
    "\\CH_BIN1\\CH_01.BIN;1",  // [0]
    "\\CH_BIN1\\CH_02.BIN;1",  // [1]
    "\\CH_BIN1\\CH_03.BIN;1",  // [2]
    // ... continues ...
    "\\CH_BIN1\\CH_29.BIN;1",  // [28]
    "\\CH_BIN1\\CH_NO.BIN;1",  // [29] or separate?
    "\\CH_BIN2\\CH_30.BIN;1",  // [30] or [29]?
    // ... continues ...
    "\\CH_BIN3\\IN_01.BIN;1",  // [?]
    // ...
    NULL                       // Array terminator?
};
```

### TODO: Locate Array in Memory

**Actions needed**:
1. [ ] Search for string "CH_01.BIN" in GAME.EXE data section
2. [ ] Find XREF to string in LoadCHBinFileAsync decompiled code
3. [ ] Dump array contents from memory/executable
4. [ ] Determine array size (probably 63-70 entries)
5. [ ] Confirm CD-ROM path format (e.g., "\\CH_BIN1\\")

**Tools**: `grep_search`, `mcp_reva_read-memory` (when available)

## Character ID Mapping (INCOMPLETE)

**Status**: ⚠️ CHARACTER NAMES NOT YET IDENTIFIED

### Known Character Roster (DBZ Legends)

Based on general DBZ Legends knowledge (needs confirmation from game data):

**Main Characters** (likely CH_01-CH_29):
- Goku (base, SSJ, SSJ2?)
- Gohan (teen, SSJ, SSJ2?)
- Vegeta (base, SSJ, Majin?)
- Piccolo
- Trunks (future)
- Krillin
- Yamcha
- Tien
- Chiaotzu
- Android 16
- Android 17
- Android 18
- Cell (forms 1, 2, Perfect)
- Frieza (multiple forms)
- Ginyu Force members
- Nappa
- Raditz
- Saibamen

**Extended Characters** (likely CH_30-CH_50):
- Kid Goku?
- Master Roshi?
- Mr. Satan?
- Videl?
- Babidi?
- Dabura?
- Majin Buu (forms)?
- Supreme Kai?
- Additional transformations

### Transformation Hypothesis

CH_32_1/2/3 variants suggest transformation system:
```
Possible transformation mapping:
ch_bin_file_index = base_character_id + (transformation_level * variant_offset)

Example (hypothetical):
- CH_32.BIN   → Character 32 base form
- CH_32_1.BIN → Character 32 form 1 (e.g., SSJ)
- CH_32_2.BIN → Character 32 form 2 (e.g., SSJ2)  
- CH_32_3.BIN → Character 32 form 3 (e.g., SSJ3)
```

### Next Steps for Character Mapping

**Priority actions**:
1. [ ] Find character name strings in TITLE.EXE or SELECT.EXE
2. [ ] Locate character selection menu code
3. [ ] Trace from menu selection → character_id → ch_bin_file_index
4. [ ] Extract character portraits/icons to visually identify characters
5. [ ] Cross-reference with CHR_DATA directory contents
6. [ ] Check for character ID table in config files

**Tools**: 
- `semantic_search` for character selection code
- `grep_search` for character name strings
- `mcp_reva_read-memory` to dump character tables
- Custom tool analysis of CHR_DATA files

## Cross-References

### Related Data Directories

- **CHR_DATA/**: Likely contains character stats, names, move lists
  - Should contain character → CH_BIN index mapping
  - May include character name strings (Japanese)

- **AT1/, AT2/**: Attack data
  - May reference character IDs for special moves
  - Could provide clues to character order

- **STG/**: Stage data
  - Stage → Character associations might reveal character IDs
  - Story mode stage assignments

### Related Documentation

- CH_BIN_FORMAT_ANALYSIS.md - File format details
- DECOMPILATION_NOTES.md - General project notes
- LoadCHBinFileAsync source code (when decompiled)
- Character selection menu code (TITLE.EXE/SELECT.EXE)

## Code References

### Functions Using Filenames

**LoadCHBinFileAsync** (0x80035828):
- Accesses `g_ch_bin_filenames[ch_bin_file_index]`
- Passes filename to `SearchFileAndLoadIntoBuffer`
- Fallback behavior: loads index 0 on error

**SearchFileAndLoadIntoBuffer** (referenced):
- Takes filename string as parameter
- Performs CD file search and load
- Returns file handle or 0xFFFFFFFF on error

### Global Variables

```c
extern char* g_ch_bin_filenames[];  // Address: UNKNOWN
extern uint ch_bin_file_index;      // Field in GameState structure
extern uint g_fileLoadFlags;        // 0x8009AA50 - Loading state flags
```

## Summary

**Completed**:
- ✅ Full physical file inventory (63 files)
- ✅ Directory structure documented
- ✅ File size confirmed (20480 bytes constant)
- ✅ Numbering gaps identified (CH_08, CH_40 missing)
- ✅ Variant files identified (CH_32 x3)
- ✅ Code reference to g_ch_bin_filenames located

**Incomplete**:
- ⚠️ g_ch_bin_filenames array address not found in code
- ⚠️ Array contents not dumped
- ⚠️ Character names not mapped to file indices
- ⚠️ Transformation system not confirmed
- ⚠️ IN_* file purposes unclear

**Next Phase**: Priority 3 - Character ID Mapping (requires character selection code analysis)
