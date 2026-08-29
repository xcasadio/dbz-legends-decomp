# PCSX-Redux MCP Server — Tool Reference

This document lists all tools exposed by the PCSX-Redux MCP server.
An AI agent can call these tools to inspect and control the PSX emulator.

## Setup

The MCP server communicates with PCSX-Redux over HTTP (default port 8080).
Configure the port with the `PCSX_PORT` environment variable.

In VS Code (`.vscode/mcp.json`):
```json
{
  "servers": {
    "pcsx-redux": {
      "type": "stdio",
      "command": "node",
      "args": ["/path/to/pcsx-redux/tools/mcp-server/dist/index.js"],
      "env": { "PCSX_HOST": "localhost", "PCSX_PORT": "8081" }
    }
  }
}
```

PCSX-Redux must have its **Web Server** enabled (Configuration → Emulation → Web Server).

---

## Execution Control

### `pcsx_get_status`
Returns the current emulator state.

**Returns:** `{ running: bool, isDynarec: bool, "8mb": bool, debugger: bool }`

---

### `pcsx_pause`
Pauses emulation. Required before reading registers or stepping through code.

---

### `pcsx_resume`
Resumes emulation after a pause or breakpoint hit.

---

### `pcsx_reset`
Resets the emulator.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `type` | `"soft"` \| `"hard"` | `"soft"` | Soft = keep memory; Hard = full reset |

---

### `pcsx_flush_cache`
Flushes the dynarec recompiler cache. Use after writing code into RAM.

---

## Memory

### `pcsx_read_memory`
Reads a region of PSX RAM and returns a hex dump with ASCII.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | `number` | required | Address (hex string like `"0x80010000"` or decimal) |
| `size` | `number` | `256` | Bytes to read (1–65536) |

**Note:** PSX RAM mirrors: `0x80000000`, `0xa0000000`, `0x00000000` all map to the same 2 MB WRAM.

---

### `pcsx_read_memory_raw`
Reads a region of PSX RAM and returns raw bytes as base64.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | `number` | required | Start address |
| `size` | `number` | `256` | Bytes to read (1–65536) |

---

### `pcsx_write_memory`
Writes bytes into PSX RAM.

| Parameter | Type | Description |
|-----------|------|-------------|
| `address` | `number` | Destination address |
| `data` | `string` | Hex string of bytes, e.g. `"deadbeef"` |

---

### `pcsx_read_word`
Reads a single 32-bit word from PSX RAM.

| Parameter | Type | Description |
|-----------|------|-------------|
| `address` | `number` | Address (must be 4-byte aligned) |

**Returns:** decimal and hex value of the word.

---

### `pcsx_read_string`
Reads a null-terminated C string from PSX RAM.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | `number` | required | Start address |
| `maxLength` | `number` | `256` | Max bytes to scan |

---

### `pcsx_search_memory`
Searches PSX RAM for a byte pattern.

| Parameter | Type | Description |
|-----------|------|-------------|
| `pattern` | `string` | Hex string to find, e.g. `"8f800000"` |

**Returns:** List of matching addresses (up to 20 results).

---

## Registers & Disassembly

### `pcsx_get_registers`
Returns all CPU registers. Emulation should be paused first.

**Returns:**
```json
{
  "pc": 0x80012345,
  "cycle": 12345678,
  "gpr": { "r0": 0, "at": 0, "v0": 1, ..., "sp": 0x801fffb0, "ra": 0x80010000 },
  "cp0": { "Index": 0, "Status": 0x30000000, ... },
  "cp2d": { ... },
  "cp2c": { ... }
}
```

GPR names: `r0 at v0 v1 a0 a1 a2 a3 t0 t1 t2 t3 t4 t5 t6 t7 s0 s1 s2 s3 s4 s5 s6 s7 t8 t9 k0 k1 gp sp s8 ra lo hi`

---

### `pcsx_get_pc`
Returns just the current Program Counter (PC) value. Quick shorthand.

---

### `pcsx_disassemble`
Disassembles PSX MIPS instructions at a given address.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | `number` | required | Start address to disassemble |
| `count` | `number` | `20` | Number of instructions |

**Returns:** List of `{ address, addressHex, opcode, opcodeHex, disassembly, symbol? }`

---

### `pcsx_analyze_function`
Disassembles a function starting at the given address, stopping at the first `jr $ra` (return instruction). Useful for reverse engineering subroutines.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | `number` | required | Function entry point |
| `maxInstructions` | `number` | `200` | Safety limit |

---

## Breakpoints

### `pcsx_list_breakpoints`
Lists all currently set breakpoints.

**Returns:** List of `{ address, addressHex, type, width, label, enabled, source }`

---

### `pcsx_add_breakpoint`
Adds a breakpoint.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | `number` | required | Address to break at |
| `type` | `"exec"` \| `"read"` \| `"write"` | `"exec"` | Breakpoint type |
| `width` | `number` | `4` | Memory region width in bytes |
| `label` | `string` | `""` | Optional label |

---

### `pcsx_remove_breakpoint`
Removes a specific breakpoint.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | `number` | required | Address of the breakpoint |
| `type` | `"exec"` \| `"read"` \| `"write"` | `"exec"` | Breakpoint type |

---

### `pcsx_toggle_breakpoint`
Enables or disables a breakpoint without removing it.

| Parameter | Type | Description |
|-----------|------|-------------|
| `address` | `number` | Breakpoint address |
| `enable` | `boolean` | `true` to enable, `false` to disable |

---

### `pcsx_remove_all_breakpoints`
Removes all breakpoints at once.

---

### `pcsx_wait_for_break`
Resumes emulation and waits until a breakpoint is hit or a timeout expires.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `timeout_ms` | `number` | `5000` | Max wait time in ms |

**Returns:** `{ hit: bool, pc: number }` — PC where execution stopped.

---

## Controller Input

PSX buttons use **active-low** logic internally. The MCP server manages *override* bits that force buttons pressed regardless of the physical controller.

**Button names:** `select`, `start`, `up`, `right`, `down`, `left`, `l1`, `l2`, `r1`, `r2`, `triangle`, `circle`, `cross`, `square`

---

### `pcsx_press_button`
Simulates a button tap: holds the button for `duration_ms` then releases automatically.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `button` | string | required | Button name |
| `port` | `1` \| `2` | `1` | Controller port |
| `duration_ms` | `number` | `100` | Hold duration in ms |

---

### `pcsx_hold_button`
Holds a button pressed indefinitely. Must be released with `pcsx_release_button` or `pcsx_release_all_buttons`.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `button` | string | required | Button name |
| `port` | `1` \| `2` | `1` | Controller port |

---

### `pcsx_release_button`
Releases the override on one button.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `button` | string | required | Button name |
| `port` | `1` \| `2` | `1` | Controller port |

---

### `pcsx_release_all_buttons`
Clears all button overrides on a port. Always call this on cleanup.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `port` | `1` \| `2` | `1` | Controller port |

---

### `pcsx_get_pad_state`
Returns the current override state for both controller ports.

**Returns:**
```
port1: cross, circle (raw: 0x9fff)
port2: no overrides (raw: 0xffff)
```
`0xffff` = no overrides. A bit set to `0` = that button is forced pressed.

---

## CD-ROM

### `pcsx_cdrom_info`
Returns information about the currently loaded disc.

**Returns:** `{ id, label, iso: { TN, tracktype, TD } }`

---

### `pcsx_cdrom_read_file`
Reads a file from the ISO 9660 filesystem of the loaded disc. Returns base64-encoded content.

| Parameter | Type | Description |
|-----------|------|-------------|
| `filename` | `string` | ISO path, e.g. `"SLUS_012.34"` or `"BATTLE/ENEMY.TIM"` |

---

### `pcsx_upload_symbols`
Uploads a symbol map to PCSX-Redux for use in disassembly output.

| Parameter | Type | Description |
|-----------|------|-------------|
| `symbols` | `Record<string, string>` | Map of `{ "0x80010000": "main", ... }` |

---

### `pcsx_reset_symbols`
Clears all uploaded symbols.

---

## Save States

### `pcsx_savestate_list`
Lists all save state slots (0–9) and named save states.

**Returns:**
```
Save state slots:
  Slot 0: USED
  Slot 1: empty
  ...
Named save states:
  "before_boss" → C:\pcsx-redux\saves\before_boss.sstate
```

---

### `pcsx_savestate_save`
Saves the current emulator state.

| Parameter | Type | Description |
|-----------|------|-------------|
| `slot` | `number` (0–9) | Slot number (provide `slot` OR `name`, not both) |
| `name` | `string` | Named save state (letters, digits, hyphens only) |

---

### `pcsx_savestate_load`
Loads a previously saved state. Instantly restores CPU, RAM, GPU, and SPU state.

| Parameter | Type | Description |
|-----------|------|-------------|
| `slot` | `number` (0–9) | Slot number (provide `slot` OR `name`, not both) |
| `name` | `string` | Named save state |

---

## VRAM & Screenshot

### `pcsx_get_vram`
Dumps the full GPU VRAM (1 MB, 1024×512 pixels, 16 bpp). Returns base64-encoded raw pixel data (little-endian RGB555 / ABGR1555).

---

### `pcsx_screenshot`
Takes a screenshot of the current PSX display. Returns a PNG image.

---

## Typical Debugging Workflow

```
1. pcsx_get_status          → check emulator is running
2. pcsx_pause               → stop execution
3. pcsx_get_pc              → see where we are
4. pcsx_get_registers       → inspect all registers
5. pcsx_disassemble         → read code at PC
6. pcsx_add_breakpoint      → set breakpoint at interesting address
7. pcsx_resume              → let it run
8. pcsx_wait_for_break      → wait for breakpoint hit
9. pcsx_read_memory         → inspect RAM around SP or a pointer
10. pcsx_savestate_save     → snapshot current state
11. pcsx_press_button       → simulate player input
12. pcsx_savestate_load     → restore snapshot and try again
```
