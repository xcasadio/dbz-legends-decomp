# Documentation Index

This folder contains documentation for the DBZ Legends decompilation project.

## Guides

| Document | Description |
|----------|-------------|
| [FUNCTION_EXTRACTION.md](FUNCTION_EXTRACTION.md) | How to extract assembly from PSX executables |
| [DECOMPILATION_GUIDE.md](DECOMPILATION_GUIDE.md) | Complete decompilation workflow (compile, compare, iterate) |
| [DECOMPILATION_NOTES.md](DECOMPILATION_NOTES.md) | Knowledge base with patterns, tips, and discoveries |

## Quick Reference

### Extract a function
```bash
python tools/extract_func.py <overlay> <address> <size> --name <name> --save
```

### Compile and compare
```bash
python tools/compile_func.py <overlay> <function> --compare <address> <size>
```

### Compile only
```bash
python tools/compile_func.py <overlay> <function> --full
```

## For AI Assistants

When asked to work on decompilation:

1. **Read** `docs/DECOMPILATION_NOTES.md` for known patterns and issues
2. **Follow** `docs/DECOMPILATION_GUIDE.md` for the workflow
3. **Use** `docs/FUNCTION_EXTRACTION.md` for extraction commands
4. **Update** `docs/DECOMPILATION_NOTES.md` with new discoveries

### Maximum Iterations Rule

When decompiling a function, attempt **maximum 30 iterations** to achieve matching.
After 30 attempts, mark the function as NON_MATCHING and move on.
