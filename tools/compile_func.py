#!/usr/bin/env python3
"""
compile_func.py - Compile a C file and extract a specific function's assembly

Usage:
    python tools/compile_func.py <overlay> <function_name> [options]

Examples:
    python tools/compile_func.py title main
    python tools/compile_func.py title FUN_80021dd0 --compare 0x80021DD0 0x58
"""

import argparse
import os
import re
import subprocess
import sys
import tempfile

# Docker configuration
DOCKER_IMAGE = "dbz-legends-build"

# Compiler settings
CPP_FLAGS = "-Iinclude -Iinclude/psxsdk -undef -D__GNUC__=2 -D__OPTIMIZE__ -DPSX"
CC1_FLAGS = "-O2 -G0 -quiet -mcpu=3000 -mgas -msoft-float"

# VRAM and EXE configuration (duplicated from extract_func.py for independence)
VRAM_START = {
    "main": 0x80020000, "game": 0x80020000, "title": 0x80020000,
    "select": 0x80020000, "vs": 0x80020000, "sp": 0x80020000,
    "demo": 0x80020000, "movie": 0x80020000, "ending": 0x80010000,
}

EXE_FILES = {
    "main": "SLPS_003.55", "game": "GAME.EXE", "title": "TITLE.EXE",
    "select": "SELECT.EXE", "vs": "VS.EXE", "sp": "SP.EXE",
    "demo": "DEMO.EXE", "movie": "MOVIE.EXE", "ending": "ENDING.EXE",
}

PSX_HEADER_SIZE = 0x800


def get_base_dir():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    return os.path.dirname(script_dir)


def compile_to_asm(overlay, base_dir, opt_level="O2"):
    """Compile the overlay C file and return the assembly."""
    src_file = f"src/{overlay}/{overlay}.c"
    
    cmd = [
        "docker", "run", "--rm",
        "-v", f"{base_dir}:/project",
        "-w", "/project",
        DOCKER_IMAGE,
        "/bin/bash", "-c",
        f"mips-linux-gnu-cpp {CPP_FLAGS} {src_file} -o /tmp/out.i 2>&1 && "
        f"/usr/local/bin/cc1-psx-26 -{opt_level} -G0 -quiet -mcpu=3000 -mgas -msoft-float /tmp/out.i -o - 2>&1"
    ]
    
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
    
    if "error:" in result.stdout.lower() or "error:" in result.stderr.lower():
        return None, result.stdout + result.stderr
    
    return result.stdout, None


def extract_function_asm(full_asm, func_name):
    """Extract a specific function from the full assembly output."""
    lines = full_asm.split('\n')
    
    # Find function start
    in_function = False
    func_lines = []
    
    for i, line in enumerate(lines):
        # Function start patterns
        if f".ent\t{func_name}" in line or f".ent {func_name}" in line:
            in_function = True
            # Include some context before .ent
            start = max(0, i - 5)
            for j in range(start, i):
                if lines[j].strip() and not lines[j].startswith('#'):
                    func_lines.append(lines[j])
        
        if in_function:
            func_lines.append(line)
            
            # Function end
            if f".end\t{func_name}" in line or f".end {func_name}" in line:
                break
    
    return '\n'.join(func_lines) if func_lines else None


def extract_original(overlay, address, size, base_dir):
    """Extract and disassemble original function bytes."""
    vram_start = VRAM_START[overlay]
    offset = address - vram_start + PSX_HEADER_SIZE
    exe_path = os.path.join(base_dir, "data", EXE_FILES[overlay])
    
    # Read bytes
    with open(exe_path, "rb") as f:
        f.seek(offset)
        data = f.read(size)
    
    # Write to temp file and disassemble
    with tempfile.NamedTemporaryFile(delete=False, suffix=".bin", dir=base_dir) as f:
        f.write(data)
        temp_path = f.name
    
    try:
        rel_path = os.path.relpath(temp_path, base_dir)
        cmd = [
            "docker", "run", "--rm",
            "-v", f"{base_dir}:/project",
            "-w", "/project",
            DOCKER_IMAGE,
            "mips-linux-gnu-objdump",
            "-D", "-b", "binary", "-m", "mips:3000",
            f"--adjust-vma=0x{address:X}",
            rel_path.replace("\\", "/")
        ]
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
        return result.stdout
    finally:
        os.unlink(temp_path)


def parse_instructions(asm_text, is_objdump=False):
    """Parse assembly text into list of (address, instruction) tuples."""
    instructions = []
    
    for line in asm_text.split('\n'):
        line = line.strip()
        if not line:
            continue
        
        if is_objdump:
            # objdump format: "80021dd0:  e0ffbd27  addiu sp,sp,-32"
            if ':' in line and '\t' in line:
                parts = line.split('\t')
                if len(parts) >= 2:
                    addr = parts[0].split(':')[0].strip()
                    instr = parts[-1].strip() if len(parts) > 2 else parts[1].strip()
                    # Skip the hex bytes
                    if not all(c in '0123456789abcdefABCDEF ' for c in instr):
                        instructions.append((addr, instr))
        else:
            # GCC output format: various patterns
            # Skip directives, labels, comments
            if line.startswith('.') or line.startswith('#') or line.endswith(':'):
                continue
            if line.startswith('$'):  # label
                continue
            # Get instruction
            parts = line.split('#')[0].strip()  # Remove comments
            if parts and not parts.startswith('.'):
                instructions.append(('', parts))
    
    return instructions


def compare_functions(original_asm, compiled_asm, func_name):
    """Compare original and compiled assembly."""
    orig_instrs = parse_instructions(original_asm, is_objdump=True)
    comp_instrs = parse_instructions(compiled_asm, is_objdump=False)
    
    print(f"\n{'='*60}")
    print(f"COMPARISON: {func_name}")
    print(f"{'='*60}")
    print(f"Original instructions: {len(orig_instrs)}")
    print(f"Compiled instructions: {len(comp_instrs)}")
    
    # Quick frame size check
    orig_frame = None
    comp_frame = None
    
    for _, instr in orig_instrs:
        if 'addiu' in instr and 'sp,sp,-' in instr:
            match = re.search(r'sp,sp,(-?\d+)', instr)
            if match:
                orig_frame = int(match.group(1))
                break
    
    for _, instr in comp_instrs:
        if 'sp,sp,-' in instr or 'sp,$sp,-' in instr:
            match = re.search(r'sp.*,(-?\d+)', instr)
            if match:
                comp_frame = int(match.group(1))
                break
    
    print(f"\nFrame size - Original: {orig_frame}, Compiled: {comp_frame}")
    if orig_frame == comp_frame:
        print("  ✓ Frame size matches!")
    else:
        print("  ✗ Frame size DIFFERS")
    
    # Show first 10 instructions of each
    print(f"\n--- Original (first 15) ---")
    for i, (addr, instr) in enumerate(orig_instrs[:15]):
        print(f"  {addr}: {instr}")
    
    print(f"\n--- Compiled (first 15) ---")
    for i, (_, instr) in enumerate(comp_instrs[:15]):
        print(f"  {i:04d}: {instr}")
    
    # Simple match check
    if len(orig_instrs) == len(comp_instrs):
        matching = sum(1 for (_, a), (_, b) in zip(orig_instrs, comp_instrs) 
                      if normalize_instr(a) == normalize_instr(b))
        pct = (matching / len(orig_instrs)) * 100
        print(f"\nInstruction match: {matching}/{len(orig_instrs)} ({pct:.1f}%)")
        
        if pct == 100:
            print("\n✓✓✓ PERFECT MATCH! ✓✓✓")
            return True
        elif pct >= 90:
            print("\n⚠ Very close! Check differences carefully.")
        else:
            print("\n✗ Significant differences remain.")
    else:
        print(f"\n✗ Instruction count differs ({len(orig_instrs)} vs {len(comp_instrs)})")
    
    return False


def normalize_instr(instr):
    """Normalize an instruction for comparison."""
    # Remove extra whitespace
    instr = ' '.join(instr.split())
    # Normalize register names
    instr = instr.replace('$sp', 'sp').replace('$ra', 'ra')
    instr = instr.replace('$zero', 'zero').replace('$0', 'zero')
    # Remove comments
    if '#' in instr:
        instr = instr.split('#')[0].strip()
    return instr.lower()


def main():
    parser = argparse.ArgumentParser(description="Compile and optionally compare function")
    parser.add_argument("overlay", choices=list(EXE_FILES.keys()))
    parser.add_argument("function", help="Function name to extract")
    parser.add_argument("--compare", "-c", nargs=2, metavar=("ADDR", "SIZE"),
                       help="Compare with original at ADDR with SIZE")
    parser.add_argument("--opt", "-O", default="O2", choices=["O0", "O1", "O2", "O3"],
                       help="Optimization level (default: O2)")
    parser.add_argument("--full", "-f", action="store_true",
                       help="Show full compiled function")
    
    args = parser.parse_args()
    base_dir = get_base_dir()
    
    # Compile
    print(f"Compiling src/{args.overlay}/{args.overlay}.c with -{args.opt}...")
    full_asm, error = compile_to_asm(args.overlay, base_dir, args.opt)
    
    if error:
        print(f"Compilation error:\n{error}")
        return 1
    
    # Extract function
    func_asm = extract_function_asm(full_asm, args.function)
    
    if not func_asm:
        print(f"Function '{args.function}' not found in compiled output!")
        print("Available functions:")
        for line in full_asm.split('\n'):
            if '.ent' in line:
                print(f"  {line.strip()}")
        return 1
    
    if args.full:
        print(f"\n{'='*60}")
        print(f"COMPILED: {args.function}")
        print(f"{'='*60}")
        print(func_asm)
    
    # Compare if requested
    if args.compare:
        addr = int(args.compare[0], 16)
        size = int(args.compare[1], 16)
        
        print(f"\nExtracting original from 0x{addr:08X}, size 0x{size:X}...")
        orig_asm = extract_original(args.overlay, addr, size, base_dir)
        
        return 0 if compare_functions(orig_asm, func_asm, args.function) else 1
    else:
        print(func_asm)
    
    return 0


if __name__ == "__main__":
    sys.exit(main())
