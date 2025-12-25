#!/usr/bin/env python3
"""
Extract assembly from a PSX executable at a given address range.
Usage: python extract_asm.py <exe_file> <start_addr> <end_addr> [--overlay main]
"""

import argparse
import subprocess
import struct
import sys
import os

def get_vram_start(overlay):
    """Get the vram_start address for a given overlay."""
    addresses = {
        "main": 0x80020000,
        "game": 0x80020000,
        "title": 0x80020000,
        "select": 0x80020000,
        "vs": 0x80020000,
        "sp": 0x80020000,
        "demo": 0x80020000,
        "movie": 0x80020000,
        "ending": 0x80010000,
    }
    return addresses.get(overlay, 0x80020000)

def extract_bytes(exe_path, vram_addr, size, overlay="main"):
    """Extract bytes from PSX executable."""
    vram_start = get_vram_start(overlay)
    header_size = 0x800  # PSX-EXE header
    
    file_offset = header_size + (vram_addr - vram_start)
    
    with open(exe_path, 'rb') as f:
        f.seek(file_offset)
        data = f.read(size)
    
    return data

def disassemble(data, start_addr):
    """Disassemble MIPS code using objdump."""
    import tempfile
    
    # Write bytes to temp file
    with tempfile.NamedTemporaryFile(delete=False, suffix='.bin') as f:
        f.write(data)
        tmp_path = f.name
    
    try:
        # Disassemble
        result = subprocess.run([
            'mips-linux-gnu-objdump',
            '-D',
            '-b', 'binary',
            '-m', 'mips:3000',
            '-M', 'no-aliases',
            '--adjust-vma=' + hex(start_addr),
            tmp_path
        ], capture_output=True, text=True)
        
        return result.stdout
    finally:
        os.unlink(tmp_path)

def main():
    parser = argparse.ArgumentParser(description='Extract assembly from PSX executable')
    parser.add_argument('exe_file', help='Path to PSX executable')
    parser.add_argument('start_addr', help='Start address (hex, e.g., 0x80021574)')
    parser.add_argument('end_addr', help='End address (hex, e.g., 0x800215c0)')
    parser.add_argument('--overlay', default='main', help='Overlay name')
    
    args = parser.parse_args()
    
    start = int(args.start_addr, 16)
    end = int(args.end_addr, 16)
    size = end - start
    
    print(f"Extracting {size} bytes from {args.exe_file}")
    print(f"Address range: {hex(start)} - {hex(end)}")
    print(f"Overlay: {args.overlay}")
    print()
    
    data = extract_bytes(args.exe_file, start, size, args.overlay)
    asm = disassemble(data, start)
    
    print(asm)

if __name__ == '__main__':
    main()
