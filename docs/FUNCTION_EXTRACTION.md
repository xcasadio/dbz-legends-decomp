# Function Extraction Guide

This document explains how to extract assembly code from PSX executables for decompilation.

## Overview

DBZ Legends uses multiple overlay executables that are loaded into PSX memory. Each function has:
- A **VRAM address** (e.g., `0x800581DC`) - where it lives in PSX memory
- A **size** in bytes (e.g., `0x20C` = 524 bytes)
- A **file offset** in the EXE file

## Quick Reference

### Extract a Function (One Command)

```bash
python tools/extract_func.py <overlay> <address> <size> [--name <name>] [--save]
```

**Examples:**
```bash
# Extract and print to console
python tools/extract_func.py title 0x800581DC 0x20C

# Extract with custom name and save to file
python tools/extract_func.py title 0x800581DC 0x20C --name main --save

# Extract in GNU AS format (for INCLUDE_ASM)
python tools/extract_func.py title 0x800581DC 0x20C --name main --gnu-as --save
```

### Overlays

| Overlay | EXE File | VRAM Start |
|---------|----------|------------|
| main | SLPS_003.55 | 0x80020000 |
| game | GAME.EXE | 0x80020000 |
| title | TITLE.EXE | 0x80020000 |
| select | SELECT.EXE | 0x80020000 |
| vs | VS.EXE | 0x80020000 |
| sp | SP.EXE | 0x80020000 |
| demo | DEMO.EXE | 0x80020000 |
| movie | MOVIE.EXE | 0x80020000 |
| ending | ENDING.EXE | 0x80010000 |

---

## Detailed Process

### 1. Find the Function in Symbol File

Symbol files are in `config/symbols.<overlay>.jp.txt`:

```
main = 0x800581DC; // size: 0x20C
FUN_80021dd0 = 0x80021DD0; // size: 0x58
```

The format is: `name = address; // size: size`

### 2. Calculate File Offset

```
file_offset = (vram_address - vram_start) + psx_header_size
```

Where:
- `vram_address` = Function address (e.g., `0x800581DC`)
- `vram_start` = Overlay's VRAM start (usually `0x80020000`)
- `psx_header_size` = `0x800` (2048 bytes)

**Example for `main` in TITLE.EXE:**
```
offset = (0x800581DC - 0x80020000) + 0x800
offset = 0x000381DC + 0x800
offset = 0x000389DC = 231900 (decimal)
```

### 3. Extract Bytes

Using Python:
```python
with open("data/TITLE.EXE", "rb") as f:
    f.seek(0x389DC)
    data = f.read(0x20C)  # 524 bytes
```

Using PowerShell:
```powershell
$bytes = [System.IO.File]::ReadAllBytes("data\TITLE.EXE")
$offset = 0x389DC
$size = 0x20C
$func = New-Object byte[] $size
[Array]::Copy($bytes, $offset, $func, 0, $size)
[System.IO.File]::WriteAllBytes("func.bin", $func)
```

### 4. Disassemble

Using Docker (recommended):
```bash
docker run --rm -v "$(pwd):/project" -w /project dbz-legends-build \
    mips-linux-gnu-objdump -D -b binary -m mips:3000 \
    --adjust-vma=0x800581DC func.bin
```

The `--adjust-vma` flag sets the base address for the disassembly output.

---

## Script Reference: `extract_func.py`

### Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `overlay` | Overlay name | `title`, `game`, `main` |
| `address` | Function VRAM address | `0x800581DC` |
| `size` | Function size in bytes | `0x20C` |

### Options

| Option | Description |
|--------|-------------|
| `--name`, `-n` | Function name (default: `FUN_<address>`) |
| `--save`, `-s` | Save to `asm/<version>/<overlay>/<name>.s` |
| `--raw`, `-r` | Also save raw bytes as `.bin` file |
| `--gnu-as`, `-g` | Output in GNU AS format |
| `--quiet`, `-q` | Suppress status messages |
| `--version`, `-v` | Game version (default: `jp`) |

### Output Locations

When using `--save`:
- Assembly: `asm/jp/<overlay>/<name>.s`
- Binary: `asm/jp/<overlay>/<name>.bin` (with `--raw`)

---

## For AI Assistants

When asked to extract a function, use this command:

```bash
python tools/extract_func.py <overlay> <address> <size> --name <name> --save
```

**Required Information:**
1. **Overlay name** - Check which EXE the function is in
2. **Function address** - From symbol file or user request
3. **Function size** - From symbol file (after `// size:`)
4. **Function name** - From symbol file or user request

**Example workflow:**
```bash
# User asks: "Extract FUN_80021dd0 from title"

# 1. Check config/symbols.title.jp.txt for:
#    FUN_80021dd0 = 0x80021DD0; // size: 0x58

# 2. Run extraction:
python tools/extract_func.py title 0x80021DD0 0x58 --name FUN_80021dd0 --save

# 3. Result saved to: asm/jp/title/FUN_80021dd0.s
```

---

## PSX Memory Map Reference

```
0x00000000 - 0x0000FFFF : Kernel (64KB)
0x00010000 - 0x001FFFFF : User Memory (2MB - 64KB)
0x80000000 - 0x801FFFFF : KSEG0 (cached mirror of 0x00000000-0x001FFFFF)
0x80020000              : Typical overlay load address
0x1F800000 - 0x1F8003FF : Scratchpad RAM (1KB)
0x1F801000 - 0x1F802FFF : Hardware I/O ports
```

## PSX-EXE Header (0x800 bytes)

| Offset | Size | Description |
|--------|------|-------------|
| 0x000 | 8 | Magic "PS-X EXE" |
| 0x010 | 4 | Initial PC (entry point) |
| 0x018 | 4 | Load address (t_addr) |
| 0x01C | 4 | File size (t_size) |
| 0x030 | 4 | Initial SP |
| 0x800+ | - | Code/Data section |

---

## Troubleshooting

### "Address is below VRAM start"
The function address doesn't belong to this overlay. Check the correct overlay.

### "Extraction range exceeds file size"
The size is too large or the address is wrong. Verify in Ghidra or the symbol file.

### Docker errors
Make sure Docker is running and the image exists:
```bash
docker images | grep dbz-legends-build
```

If not, build it:
```bash
docker build -t dbz-legends-build .
```
