#!/usr/bin/env python3
"""
extract_func.py - Extract and disassemble a function from a PSX executable

This script extracts raw bytes from a PSX executable and disassembles them
using mips-linux-gnu-objdump via Docker.

Usage:
    python tools/extract_func.py <overlay> <address> <size> [options]

Examples:
    python tools/extract_func.py title 0x800581DC 0x20C
    python tools/extract_func.py title 0x800581DC 0x20C --name main
    python tools/extract_func.py game 0x80021DD0 0x58 --name FUN_80021dd0 --save

Arguments:
    overlay     Overlay name: main, game, title, select, vs, sp, demo, movie, ending
    address     Function address in hex (e.g., 0x800581DC)
    size        Function size in hex (e.g., 0x20C)

Options:
    --name      Function name (default: derived from address)
    --save      Save disassembly to asm/<version>/<overlay>/<name>.s
    --raw       Also save raw bytes to .bin file
    --no-docker Use local objdump instead of Docker (if available)
    --version   Game version (default: jp)
"""

import argparse
import os
import subprocess
import sys
import tempfile

# =============================================================================
# CONFIGURATION
# =============================================================================

# VRAM start addresses for each overlay
VRAM_START = {
    "main":   0x80020000,
    "game":   0x80020000,
    "title":  0x80020000,
    "select": 0x80020000,
    "vs":     0x80020000,
    "sp":     0x80020000,
    "demo":   0x80020000,
    "movie":  0x80020000,
    "ending": 0x80010000,
}

# EXE files for each overlay
EXE_FILES = {
    "main":   "SLPS_003.55",
    "game":   "GAME.EXE",
    "title":  "TITLE.EXE",
    "select": "SELECT.EXE",
    "vs":     "VS.EXE",
    "sp":     "SP.EXE",
    "demo":   "DEMO.EXE",
    "movie":  "MOVIE.EXE",
    "ending": "ENDING.EXE",
}

# PSX executable header size
PSX_HEADER_SIZE = 0x800

# Docker image name
DOCKER_IMAGE = "dbz-legends-build"

# =============================================================================
# UTILITY FUNCTIONS
# =============================================================================

def get_base_dir():
    """Get the base directory of the project."""
    script_dir = os.path.dirname(os.path.abspath(__file__))
    return os.path.dirname(script_dir)

def parse_hex(value):
    """Parse a hex string (with or without 0x prefix) to integer."""
    if isinstance(value, int):
        return value
    value = value.strip()
    if value.startswith("0x") or value.startswith("0X"):
        return int(value, 16)
    # Try hex first, then decimal
    try:
        return int(value, 16)
    except ValueError:
        return int(value)

def calculate_file_offset(address, overlay):
    """Calculate the file offset from a VRAM address."""
    vram_start = VRAM_START.get(overlay)
    if vram_start is None:
        raise ValueError(f"Unknown overlay: {overlay}")
    
    if address < vram_start:
        raise ValueError(f"Address 0x{address:08X} is below VRAM start 0x{vram_start:08X}")
    
    offset = address - vram_start + PSX_HEADER_SIZE
    return offset

def get_exe_path(overlay, base_dir):
    """Get the full path to the EXE file for an overlay."""
    exe_name = EXE_FILES.get(overlay)
    if exe_name is None:
        raise ValueError(f"Unknown overlay: {overlay}")
    return os.path.join(base_dir, "data", exe_name)

# =============================================================================
# EXTRACTION FUNCTIONS
# =============================================================================

def extract_bytes(exe_path, offset, size):
    """Extract bytes from an EXE file."""
    if not os.path.exists(exe_path):
        raise FileNotFoundError(f"EXE file not found: {exe_path}")
    
    file_size = os.path.getsize(exe_path)
    if offset + size > file_size:
        raise ValueError(
            f"Extraction range (0x{offset:X} + 0x{size:X} = 0x{offset+size:X}) "
            f"exceeds file size (0x{file_size:X})"
        )
    
    with open(exe_path, "rb") as f:
        f.seek(offset)
        data = f.read(size)
    
    return data

def disassemble_with_docker(data, base_address, base_dir):
    """Disassemble bytes using objdump via Docker."""
    # Write bytes to a temporary file
    with tempfile.NamedTemporaryFile(delete=False, suffix=".bin", dir=base_dir) as f:
        f.write(data)
        temp_path = f.name
    
    try:
        # Get relative path for Docker
        rel_path = os.path.relpath(temp_path, base_dir)
        
        # Run objdump in Docker
        cmd = [
            "docker", "run", "--rm",
            "-v", f"{base_dir}:/project",
            "-w", "/project",
            DOCKER_IMAGE,
            "mips-linux-gnu-objdump",
            "-D", "-b", "binary", "-m", "mips:3000",
            f"--adjust-vma=0x{base_address:X}",
            rel_path.replace("\\", "/")
        ]
        
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
        
        if result.returncode != 0:
            raise RuntimeError(f"objdump failed: {result.stderr}")
        
        return result.stdout
    finally:
        os.unlink(temp_path)

def disassemble_local(data, base_address):
    """Disassemble bytes using local objdump."""
    with tempfile.NamedTemporaryFile(delete=False, suffix=".bin") as f:
        f.write(data)
        temp_path = f.name
    
    try:
        cmd = [
            "mips-linux-gnu-objdump",
            "-D", "-b", "binary", "-m", "mips:3000",
            f"--adjust-vma=0x{base_address:X}",
            temp_path
        ]
        
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
        
        if result.returncode != 0:
            raise RuntimeError(f"objdump failed: {result.stderr}")
        
        return result.stdout
    finally:
        os.unlink(temp_path)

def format_disassembly(raw_output, func_name, address, size):
    """Format objdump output into a cleaner assembly listing."""
    lines = raw_output.split('\n')
    
    # Find where the actual disassembly starts
    output_lines = []
    in_disasm = False
    
    for line in lines:
        # Skip header lines
        if '<.data>:' in line:
            in_disasm = True
            continue
        
        if in_disasm and line.strip():
            # Parse the line: "800581dc:  d0ffbd27  addiu sp,sp,-48"
            parts = line.split('\t')
            if len(parts) >= 2:
                output_lines.append(line)
    
    # Create header
    header = f"""# ============================================================================
# Function: {func_name}
# Address:  0x{address:08X}
# Size:     0x{size:X} ({size} bytes)
# ============================================================================

"""
    
    return header + '\n'.join(output_lines)

def create_gnu_as_format(raw_output, func_name, address):
    """Convert objdump output to GNU assembler format suitable for INCLUDE_ASM."""
    lines = raw_output.split('\n')
    output_lines = []
    
    output_lines.append(f".set noat")
    output_lines.append(f".set noreorder")
    output_lines.append(f"")
    output_lines.append(f"glabel {func_name}")
    
    in_disasm = False
    for line in lines:
        if '<.data>:' in line:
            in_disasm = True
            continue
        
        if in_disasm and line.strip():
            # Parse: "800581dc:	d0ffbd27 	addiu	sp,sp,-48"
            # The format uses tabs, split carefully
            line = line.strip()
            if ':' in line:
                # Get address part
                addr_part = line.split(':')[0].strip()
                rest = line.split(':', 1)[1].strip()
                
                # Split rest by whitespace - first part is hex, rest is instruction
                parts = rest.split(None, 1)
                if len(parts) >= 2:
                    instruction = parts[1].strip()
                    output_lines.append(f"/* {addr_part}: */ {instruction}")
                elif len(parts) == 1:
                    # Just hex bytes, no instruction (shouldn't happen)
                    output_lines.append(f"/* {addr_part}: */ .word 0x{parts[0]}")
    
    return '\n'.join(output_lines)

# =============================================================================
# MAIN
# =============================================================================

def main():
    parser = argparse.ArgumentParser(
        description="Extract and disassemble a function from a PSX executable",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s title 0x800581DC 0x20C
  %(prog)s title 0x800581DC 0x20C --name main --save
  %(prog)s game 0x80021DD0 0x58 --save --raw
        """
    )
    
    parser.add_argument("overlay", 
                        choices=list(EXE_FILES.keys()),
                        help="Overlay name")
    parser.add_argument("address", 
                        help="Function address (hex, e.g., 0x800581DC)")
    parser.add_argument("size", 
                        help="Function size (hex, e.g., 0x20C)")
    parser.add_argument("--name", "-n",
                        help="Function name (default: FUN_<address>)")
    parser.add_argument("--save", "-s", 
                        action="store_true",
                        help="Save disassembly to asm/<version>/<overlay>/<name>.s")
    parser.add_argument("--raw", "-r",
                        action="store_true",
                        help="Also save raw bytes to .bin file")
    parser.add_argument("--gnu-as", "-g",
                        action="store_true",
                        help="Output in GNU AS format (for INCLUDE_ASM)")
    parser.add_argument("--no-docker",
                        action="store_true",
                        help="Use local objdump instead of Docker")
    parser.add_argument("--version", "-v",
                        default="jp",
                        help="Game version (default: jp)")
    parser.add_argument("--quiet", "-q",
                        action="store_true",
                        help="Only output the disassembly (no status messages)")
    
    args = parser.parse_args()
    
    # Parse arguments
    try:
        address = parse_hex(args.address)
        size = parse_hex(args.size)
    except ValueError as e:
        print(f"Error parsing arguments: {e}", file=sys.stderr)
        return 1
    
    # Derive function name if not provided
    func_name = args.name or f"FUN_{address:08x}"
    
    # Get paths
    base_dir = get_base_dir()
    
    try:
        exe_path = get_exe_path(args.overlay, base_dir)
        offset = calculate_file_offset(address, args.overlay)
    except ValueError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1
    
    # Status output
    if not args.quiet:
        print(f"Extracting function: {func_name}")
        print(f"  Overlay:  {args.overlay}")
        print(f"  EXE:      {EXE_FILES[args.overlay]}")
        print(f"  Address:  0x{address:08X}")
        print(f"  Size:     0x{size:X} ({size} bytes)")
        print(f"  Offset:   0x{offset:X}")
        print()
    
    # Extract bytes
    try:
        data = extract_bytes(exe_path, offset, size)
        if not args.quiet:
            print(f"Extracted {len(data)} bytes from {EXE_FILES[args.overlay]}")
    except (FileNotFoundError, ValueError) as e:
        print(f"Error extracting bytes: {e}", file=sys.stderr)
        return 1
    
    # Disassemble
    try:
        if args.no_docker:
            raw_disasm = disassemble_local(data, address)
        else:
            raw_disasm = disassemble_with_docker(data, address, base_dir)
        
        if not args.quiet:
            print("Disassembly complete")
            print()
    except Exception as e:
        print(f"Error during disassembly: {e}", file=sys.stderr)
        return 1
    
    # Format output
    if args.gnu_as:
        output = create_gnu_as_format(raw_disasm, func_name, address)
    else:
        output = format_disassembly(raw_disasm, func_name, address, size)
    
    # Save or print
    if args.save:
        asm_dir = os.path.join(base_dir, "asm", args.version, args.overlay)
        os.makedirs(asm_dir, exist_ok=True)
        
        asm_path = os.path.join(asm_dir, f"{func_name}.s")
        with open(asm_path, "w") as f:
            f.write(output)
        
        if not args.quiet:
            print(f"Saved disassembly to: {asm_path}")
        
        if args.raw:
            bin_path = os.path.join(asm_dir, f"{func_name}.bin")
            with open(bin_path, "wb") as f:
                f.write(data)
            if not args.quiet:
                print(f"Saved raw bytes to: {bin_path}")
    else:
        print(output)
    
    return 0

if __name__ == "__main__":
    sys.exit(main())
