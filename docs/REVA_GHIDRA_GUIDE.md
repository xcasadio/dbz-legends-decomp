# ReVa Ghidra Integration Guide for AI Agents

## Overview

ReVa is a Model Context Protocol (MCP) server that provides AI agents with direct access to Ghidra's reverse engineering capabilities. This integration allows agents to query decompiled code, analyze functions, manage structures, and perform various program analysis tasks without manual intervention.

## Connection Setup

### Prerequisites

1. **Ghidra with ReVa Extension**: Ghidra must be running with the ReVa MCP server extension loaded
2. **MCP Server Active**: ReVa serves on `localhost:8080` by default
3. **Program Open**: A program (executable) must be loaded in Ghidra for most operations

### Verify Connection

Check if the ReVa server is accessible:

```powershell
Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 | ForEach-Object { 
    $proc = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
    [PSCustomObject]@{
        Port = $_.LocalPort
        PID = $_.OwningProcess
        ProcessName = $proc.ProcessName
        Path = $proc.Path
    }
} | Format-List
```

Expected output:
```
Port        : 8080
PID         : 5592
ProcessName : javaw
Path        : C:\Program Files\...\javaw.exe
```

### Program Paths

In this project, the main program is located at:
- **Path**: `/TITLE.EXE` (Ghidra project path)
- **File**: `SLPS_003.55` (PSX executable)
- **Architecture**: MIPS R3000 (PSX)

## Available ReVa Functions

ReVa provides numerous MCP tools organized by category. Below are the most useful functions for decompilation workflow.

### Function Analysis

#### `mcp_reva_get-decompilation`
Get the decompiled C code for a function.

```json
{
  "programPath": "/TITLE.EXE",
  "functionAddress": "0x80058000"
}
```

**Returns**: Decompiled C code, function signature, local variables

**Use cases**:
- Retrieve function implementation for manual decompilation
- Analyze function logic before implementing in C
- Verify function signatures and parameters

#### `mcp_reva_get-functions`
List functions from the program with pagination support.

```json
{
  "programPath": "/TITLE.EXE",
  "startIndex": 0,
  "maxCount": 100,
  "filterDefaultNames": true,
  "verbose": false
}
```

**Returns**: Array of functions with name, address, size, tags, caller/callee counts

**Options**:
- `filterByTag`: Filter by specific tag
- `untagged`: Only return functions without tags
- `verbose`: Include full function details

#### `mcp_reva_get-function-count`
Get total function count before pagination.

```json
{
  "programPath": "/TITLE.EXE",
  "filterDefaultNames": true
}
```

#### `mcp_reva_get-call-tree`
Get hierarchical call tree (callers or callees).

```json
{
  "programPath": "/TITLE.EXE",
  "functionAddress": "main",
  "direction": "callees",
  "maxDepth": 3
}
```

**Directions**:
- `callers`: Who calls this function (upward traversal)
- `callees`: What this function calls (downward traversal)

#### `mcp_reva_get-undefined-function-candidates`
Find valid instructions referenced but not defined as functions.

```json
{
  "programPath": "/TITLE.EXE",
  "minReferenceCount": 1,
  "maxCandidates": 100
}
```

**Returns**: Addresses with CALL or DATA references that could be functions

#### `mcp_reva_function-tags`
Manage function tags for categorization.

```json
{
  "programPath": "/TITLE.EXE",
  "mode": "add",
  "function": "main",
  "tags": ["entry-point", "implemented"]
}
```

**Modes**: `get`, `set`, `add`, `remove`, `list`

### Structure Management

#### `mcp_reva_modify-structure-field`
Modify an existing field in a structure.

```json
{
  "programPath": "/TITLE.EXE",
  "structureName": "UnkStruct_8004bf94",
  "fieldName": "flags_138",
  "newDataType": "u32"
}
```

**Modifiable properties**: `newDataType`, `newFieldName`, `newComment`, `newLength`

#### `mcp_reva_get-data-type-archives`
Get data type archives for a program.

```json
{
  "programPath": "/TITLE.EXE"
}
```

### Memory and Data Analysis

#### `mcp_reva_read-memory`
Read memory at a specific address.

```json
{
  "programPath": "/TITLE.EXE",
  "addressOrSymbol": "0x80058000",
  "length": 64,
  "format": "hex"
}
```

**Formats**: `hex`, `bytes`, `both`

#### `mcp_reva_find-constant-uses`
Find all locations where a constant value is used.

```json
{
  "programPath": "/TITLE.EXE",
  "value": "0x7b",
  "maxResults": 500
}
```

**Use cases**: Find magic numbers, error codes, buffer sizes

### Labels and Comments

#### `mcp_reva_create-label`
Create a label at a specific address.

```json
{
  "programPath": "/TITLE.EXE",
  "addressOrSymbol": "0x80058000",
  "labelName": "InitializeGame",
  "setAsPrimary": true
}
```

### Advanced Analysis

#### `mcp_reva_analyze-vtable`
Analyze C++ virtual function tables.

```json
{
  "programPath": "/TITLE.EXE",
  "vtableAddress": "0x80070000",
  "maxEntries": 200
}
```

#### `mcp_reva_resolve-thunk`
Follow thunk chains to find actual target functions.

```json
{
  "programPath": "/TITLE.EXE",
  "address": "0x80058100"
}
```

#### `mcp_reva_rename-variables`
Rename variables in a decompiled function.

```json
{
  "programPath": "/TITLE.EXE",
  "functionNameOrAddress": "main",
  "variableMappings": {
    "iVar1": "fileHandle",
    "uVar2": "bufferSize"
  }
}
```

### Version Control

#### `mcp_reva_checkin-program`
Commit program to version control with a message.

```json
{
  "programPath": "/TITLE.EXE",
  "message": "Implemented functions 1-10",
  "keepCheckedOut": false
}
```

### Program Management

#### `mcp_reva_import-file`
Import files into the Ghidra project.

```json
{
  "path": "D:\\path\\to\\executable.exe",
  "destinationFolder": "/",
  "analyzeAfterImport": true,
  "recursive": true
}
```

**Important**: Always use absolute paths

#### `mcp_reva_change-processor`
Change processor architecture of a program.

```json
{
  "programPath": "/TITLE.EXE",
  "languageId": "x86:LE:64:default",
  "compilerSpecId": "gcc"
}
```

## Typical Decompilation Workflow

### Step 1: Retrieve Function Decompilation

Use a subagent to retrieve decompilations efficiently:

```python
runSubagent(
    description="Get decompilations",
    prompt="Retrieve decompilations for functions FUN_80058000, FUN_80058100, FUN_80058200 from /TITLE.EXE using ReVa"
)
```

The subagent will:
1. Call `mcp_reva_get-decompilation` for each function
2. Extract function signatures, local variables, and C code
3. Note function sizes, addresses, and any ReVa comments
4. Return complete decompilation data

### Step 2: Implement in C

Based on the decompilation:
1. Add extern declarations for referenced functions/globals
2. Implement the function body matching the decompiled logic
3. Add comments with address, size, and status (EQUIVALENT/MATCHING)

### Step 3: Verify and Tag

After implementation:
1. Compile and verify no syntax errors
2. (Optional) Tag functions in Ghidra: `mcp_reva_function-tags` with mode `add`

## Best Practices

### Address Formats

ReVa accepts various address formats:
- Hex with prefix: `0x80058000`
- Hex without prefix: `80058000`
- Symbol names: `main`, `FUN_80058000`

**Recommendation**: Always use `0x` prefix for clarity

### Pagination Strategy

For large function lists:
1. Call `mcp_reva_get-function-count` first
2. Request chunks of 100 functions using `startIndex` and `maxCount`
3. Process in batches to avoid timeouts

### Error Handling

Common issues:
- **"Program not found"**: Verify program is open in Ghidra
- **"Function not found"**: Check address format, ensure function exists
- **"Timeout"**: Reduce batch size or maxDepth parameters

### Performance Tips

1. **Use verbose=false** for function lists unless you need full details
2. **Filter default names** to reduce noise (FUN_, DAT_ prefixes)
3. **Limit call tree depth** to 3-5 levels maximum
4. **Batch related operations** when possible

## Integration with This Project

### Priority List Workflow

This project uses `config/priority.title.jp.txt` to track function implementation order:

1. Read next functions from priority list
2. Use ReVa to get decompilations
3. Implement in `src/title/title.c`
4. Mark progress in priority file

### Common Function Patterns

Based on ReVa decompilations, common patterns include:
- **Struct manipulation**: Functions operating on UnkStruct_* types
- **Table lookups**: Using GTE scratchpad (SVECTOR_1f80007c)
- **System calls**: PSX SDK functions (TestEvent, SpuQuit, etc.)
- **Flag operations**: Bitwise AND/OR on struct flags

### Struct Types

Key structures defined in this project:
- `UnkStruct_8004bf94`: Large struct with flags_134, flags_138, unk_04
- `UnkStruct_8003287c`: Character-related data
- `UnkStruct_8002cd70`: Game state structure

Use ReVa's structure tools to verify field offsets and types.

## Troubleshooting

### ReVa Not Responding

1. Verify Ghidra is running: Check process list for `javaw.exe`
2. Verify port 8080 is listening: Use PowerShell command above
3. Check MCP server logs in Ghidra console

### Incorrect Decompilation

ReVa decompilations may have:
- **Unreachable blocks**: Compiler optimization artifacts, ignore
- **Type mismatches**: Manually correct based on context
- **Wrong calling conventions**: Verify with assembly if needed

Use `mcp_reva_read-memory` to cross-reference assembly when uncertain.

### Function Not Found

If function address doesn't exist:
1. Check if it's a thunk: Use `mcp_reva_resolve-thunk`
2. Verify it's actually a function: Use `mcp_reva_get-undefined-function-candidates`
3. Create function manually in Ghidra UI, then retry

## Additional Resources

- **ReVa GitHub**: [ReVa MCP Server Repository](https://github.com/cyberkaida/reverse-engineering-assistant)
- **Ghidra Documentation**: [NSA Ghidra](https://ghidra-sre.org/)
- **MCP Protocol**: [Model Context Protocol](https://modelcontextprotocol.io/)

## Example: Complete Function Implementation

```python
# 1. Get decompilation via subagent
decompilation = runSubagent(
    "Get FUN_80058000 decompilation",
    "Retrieve decompilation for FUN_80058000 from /TITLE.EXE"
)

# 2. Implement in title.c
# Based on decompilation: void FUN_80058000(s32 arg0, s32 arg1)
# Size: 0x48 (72 bytes)
# Calls: FUN_80058100, FUN_80058200

# 3. Add to source file with proper formatting
"""
/* ============================================================================
 * FUN_80058000 - 0x80058000, size: 0x48 (72 bytes)
 * EQUIVALENT - Description of function purpose
 * ============================================================================ */
void FUN_80058000(s32 arg0, s32 arg1) {
    s32 result;
    
    result = FUN_80058100(arg0);
    if (result == 0) {
        FUN_80058200(arg1);
    }
}
"""

# 4. Tag in Ghidra (optional)
mcp_reva_function_tags({
    "programPath": "/TITLE.EXE",
    "mode": "add",
    "function": "0x80058000",
    "tags": ["implemented", "verified"]
})
```

---

**Last Updated**: January 2026  
**ReVa Version**: Compatible with MCP server on port 8080  
**Ghidra Project**: dbz-legends (TITLE.EXE overlay)
