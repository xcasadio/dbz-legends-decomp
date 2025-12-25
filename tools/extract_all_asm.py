#!/usr/bin/env python3
"""
Extract assembly for all functions from symbols file.
Usage: python extract_all_asm.py <overlay> [--output-dir asm/]

This script reads the symbols file, sorts functions by address,
and extracts ASM for each function (using the next function's address as end).
"""

import argparse
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

# Project root (parent of tools/)
PROJECT_ROOT = Path(__file__).parent.parent

# Try to load paths from jp.yaml, fallback to defaults
def load_config():
    """Load overlay configuration from jp.yaml."""
    import yaml
    
    config_path = PROJECT_ROOT / "config" / "jp.yaml"
    if not config_path.exists():
        return None
    
    with open(config_path, 'r') as f:
        return yaml.safe_load(f)


def get_overlay_config(overlay_name, config=None):
    """Get overlay file path and vram_start from config."""
    if config is None:
        config = load_config()
    
    if config:
        for ov in config.get('overlays', []):
            if ov.get('name') == overlay_name:
                return {
                    'disk_path': ov.get('disk_path'),
                    'vram_start': ov.get('vram_start', 0x80020000),
                }
    
    # Fallback defaults
    defaults = {
        "main": {"disk_path": "data/SLPS_003.55", "vram_start": 0x80020000},
        "game": {"disk_path": "data/SUB/GAME.B", "vram_start": 0x80020000},
        "title": {"disk_path": "data/SUB/TITLE.B", "vram_start": 0x80020000},
        "ending": {"disk_path": "data/SUB/ENDING.BIN", "vram_start": 0x80010000},
    }
    return defaults.get(overlay_name, {"disk_path": None, "vram_start": 0x80020000})


def parse_symbols(symbols_path):
    """
    Parse a symbols file and return a sorted list of (name, address) tuples.
    Only returns functions (no 'data' or 'bss' type).
    """
    functions = []
    
    with open(symbols_path, 'r') as f:
        for line in f:
            line = line.strip()
            
            # Skip comments and empty lines
            if not line or line.startswith('#'):
                continue
            
            # Skip data/bss symbols
            if 'data' in line.lower() or 'bss' in line.lower():
                # Check if it's a type annotation, not part of the name
                if re.search(r'\b(data|bss)\s*$', line, re.IGNORECASE):
                    continue
            
            # Match: name = 0xADDRESS;
            match = re.match(r'^(\w+)\s*=\s*(0x[0-9A-Fa-f]+)\s*;', line)
            if match:
                name = match.group(1)
                addr = int(match.group(2), 16)
                
                # Skip global variables (usually in different address ranges)
                # Functions are typically in 0x800xxxxx, data in higher addresses
                if addr < 0x80080000:  # Heuristic: functions below this
                    functions.append((name, addr))
    
    # Sort by address
    functions.sort(key=lambda x: x[1])
    
    return functions


def extract_bytes(exe_path, vram_addr, size, vram_start=0x80020000):
    """Extract bytes from PSX executable."""
    header_size = 0x800  # PSX-EXE header
    
    file_offset = header_size + (vram_addr - vram_start)
    
    with open(exe_path, 'rb') as f:
        f.seek(file_offset)
        data = f.read(size)
    
    return data


def disassemble(data, start_addr, use_docker=True):
    """Disassemble MIPS code using objdump."""
    
    # Write bytes to temp file
    with tempfile.NamedTemporaryFile(delete=False, suffix='.bin') as f:
        f.write(data)
        tmp_path = f.name
    
    try:
        if use_docker:
            # Use Docker with the build container
            # Convert Windows path to Docker-compatible
            tmp_name = os.path.basename(tmp_path)
            tmp_dir = os.path.dirname(tmp_path)
            
            # On Windows, mount the temp directory
            if sys.platform == 'win32':
                # Convert Windows path to Docker format
                tmp_dir_docker = '/' + tmp_dir.replace('\\', '/').replace(':', '')
                mount_path = f"{tmp_dir}:{tmp_dir_docker}"
                tmp_path_docker = f"{tmp_dir_docker}/{tmp_name}"
            else:
                tmp_path_docker = tmp_path
                mount_path = f"{tmp_dir}:{tmp_dir}"
            
            cmd = [
                'docker', 'run', '--rm',
                '-v', mount_path,
                'dbz-legends-build',
                'mips-linux-gnu-objdump',
                '-D',
                '-b', 'binary',
                '-m', 'mips:3000',
                '-M', 'no-aliases',
                f'--adjust-vma={hex(start_addr)}',
                tmp_path_docker
            ]
        else:
            # Direct objdump (if available)
            cmd = [
                'mips-linux-gnu-objdump',
                '-D',
                '-b', 'binary',
                '-m', 'mips:3000',
                '-M', 'no-aliases',
                f'--adjust-vma={hex(start_addr)}',
                tmp_path
            ]
        
        result = subprocess.run(cmd, capture_output=True, text=True)
        
        if result.returncode != 0:
            print(f"Error disassembling: {result.stderr}", file=sys.stderr)
            return None
        
        return result.stdout
    finally:
        os.unlink(tmp_path)


def format_asm_for_gnu(asm_output, func_name):
    """
    Convert objdump output to GNU AS compatible format.
    """
    lines = []
    lines.append(f".global {func_name}")
    lines.append(f"{func_name}:")
    
    # Skip header lines from objdump
    in_disasm = False
    for line in asm_output.split('\n'):
        if '<.data>:' in line:
            in_disasm = True
            continue
        
        if in_disasm and line.strip():
            # Parse: "80021574:	27bdffe8 	addiu	sp,sp,-24"
            match = re.match(r'\s*([0-9a-f]+):\s+([0-9a-f]+)\s+(.+)', line)
            if match:
                addr = match.group(1)
                opcode = match.group(2)
                instr = match.group(3).strip()
                
                # Format for GNU AS
                lines.append(f"/* {addr} {opcode} */ {instr}")
    
    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(
        description='Extract assembly for all functions from symbols file'
    )
    parser.add_argument('overlay', help='Overlay name (main, game, title, etc.)')
    parser.add_argument('--output-dir', '-o', default='asm',
                        help='Output directory for .s files')
    parser.add_argument('--max-size', type=int, default=0x2000,
                        help='Maximum function size (default: 8KB)')
    parser.add_argument('--single', '-s', metavar='FUNC',
                        help='Extract only this function')
    parser.add_argument('--no-docker', action='store_true',
                        help='Use native objdump instead of Docker')
    parser.add_argument('--list', '-l', action='store_true',
                        help='List functions only, do not extract')
    parser.add_argument('--exe', '-e', metavar='PATH',
                        help='Path to executable (overrides jp.yaml)')
    
    args = parser.parse_args()
    
    # Find symbols file
    symbols_path = PROJECT_ROOT / f"config/symbols.{args.overlay}.jp.txt"
    if not symbols_path.exists():
        print(f"Error: Symbols file not found: {symbols_path}", file=sys.stderr)
        sys.exit(1)
    
    # Find executable from config
    config = load_config()
    overlay_config = get_overlay_config(args.overlay, config)
    
    vram_start = overlay_config.get('vram_start', 0x80020000)
    
    # Use --exe if provided, otherwise use config
    if args.exe:
        exe_path = Path(args.exe)
    else:
        exe_rel_path = overlay_config.get('disk_path')
        if not exe_rel_path:
            print(f"Error: Unknown overlay: {args.overlay}", file=sys.stderr)
            sys.exit(1)
        exe_path = PROJECT_ROOT / exe_rel_path
    
    if not exe_path.exists():
        print(f"Error: Executable not found: {exe_path}", file=sys.stderr)
        print(f"  Use --exe to specify path, or extract files from ISO to disks/jp/")
        sys.exit(1)
    
    # Parse symbols
    functions = parse_symbols(symbols_path)
    
    if not functions:
        print(f"No functions found in {symbols_path}", file=sys.stderr)
        sys.exit(1)
    
    print(f"Found {len(functions)} functions in {symbols_path}")
    print(f"Executable: {exe_path}")
    print(f"VRAM start: {hex(vram_start)}")
    
    # List mode
    if args.list:
        for name, addr in functions:
            print(f"  {name} = {hex(addr)}")
        sys.exit(0)
    
    # Create output directory
    output_dir = PROJECT_ROOT / args.output_dir / args.overlay
    output_dir.mkdir(parents=True, exist_ok=True)
    
    # Extract each function
    for i, (name, start_addr) in enumerate(functions):
        # Skip if --single specified and this isn't it
        if args.single and name != args.single:
            continue
        
        # Determine end address (next function or max_size)
        if i + 1 < len(functions):
            next_addr = functions[i + 1][1]
            size = min(next_addr - start_addr, args.max_size)
        else:
            size = args.max_size
        
        end_addr = start_addr + size
        
        print(f"Extracting {name}: {hex(start_addr)} - {hex(end_addr)} ({size} bytes)")
        
        # Extract bytes
        try:
            data = extract_bytes(str(exe_path), start_addr, size, vram_start)
        except Exception as e:
            print(f"  Error extracting bytes: {e}", file=sys.stderr)
            continue
        
        # Disassemble
        asm = disassemble(data, start_addr, use_docker=not args.no_docker)
        if not asm:
            print(f"  Error disassembling", file=sys.stderr)
            continue
        
        # Format and save
        formatted = format_asm_for_gnu(asm, name)
        
        output_file = output_dir / f"{name}.s"
        with open(output_file, 'w') as f:
            f.write(formatted)
        
        print(f"  Saved to {output_file}")
    
    print(f"\nDone! ASM files saved to {output_dir}")


if __name__ == '__main__':
    main()
