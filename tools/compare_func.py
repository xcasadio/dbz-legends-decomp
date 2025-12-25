#!/usr/bin/env python3
"""
compare_func.py - Compare original function bytes with compiled output

Usage:
    python tools/compare_func.py --overlay title --func main --addr 0x800581DC --size 0x20C
"""

import argparse
import subprocess
import os
import sys
import tempfile

# VRAM start addresses for each overlay
VRAM_START = {
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

# EXE files for each overlay
EXE_FILES = {
    "main": "SLPS_003.55",
    "game": "GAME.EXE",
    "title": "TITLE.EXE",
    "select": "SELECT.EXE",
    "vs": "VS.EXE",
    "sp": "SP.EXE",
    "demo": "DEMO.EXE",
    "movie": "MOVIE.EXE",
    "ending": "ENDING.EXE",
}

PSX_HEADER_SIZE = 0x800

def get_base_dir():
    """Get the base directory of the project."""
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def extract_original_bytes(overlay, addr, size):
    """Extract original function bytes from the EXE."""
    base_dir = get_base_dir()
    exe_path = os.path.join(base_dir, "data", EXE_FILES[overlay])
    
    if not os.path.exists(exe_path):
        print(f"Error: EXE file not found: {exe_path}")
        sys.exit(1)
    
    vram_start = VRAM_START[overlay]
    offset = addr - vram_start + PSX_HEADER_SIZE
    
    with open(exe_path, "rb") as f:
        f.seek(offset)
        return f.read(size)

def compile_and_extract(overlay, func_name, addr, size):
    """Compile the C file and extract the function bytes."""
    base_dir = get_base_dir()
    src_file = os.path.join(base_dir, "src", overlay, f"{overlay}.c")
    
    if not os.path.exists(src_file):
        print(f"Error: Source file not found: {src_file}")
        sys.exit(1)
    
    # Compile with Docker
    docker_cmd = [
        "docker", "run", "--rm",
        "-v", f"{base_dir}:/project",
        "-w", "/project",
        "dbz-legends-build",
        "/bin/bash", "-c",
        f"mips-linux-gnu-cpp -Iinclude -Iinclude/psxsdk -undef -D__GNUC__=2 -D__OPTIMIZE__ -DPSX src/{overlay}/{overlay}.c -o /tmp/{overlay}.i && "
        f"/usr/local/bin/cc1-psx-26 -O2 -G0 -quiet -mcpu=3000 -mgas -msoft-float /tmp/{overlay}.i -o /tmp/{overlay}.s && "
        f"mips-linux-gnu-as -march=r3000 -mabi=32 -no-pad-sections /tmp/{overlay}.s -o /tmp/{overlay}.o && "
        f"mips-linux-gnu-objcopy -O binary /tmp/{overlay}.o /tmp/{overlay}.bin && "
        f"cat /tmp/{overlay}.bin | od -A n -t x1 | tr -d ' \\n'"
    ]
    
    try:
        result = subprocess.run(docker_cmd, capture_output=True, text=True, timeout=60)
        if result.returncode != 0:
            print(f"Compilation error:\n{result.stderr}")
            return None
        
        # Parse the hex output
        hex_output = result.stdout.strip().replace('\n', '')
        return bytes.fromhex(hex_output)
    except subprocess.TimeoutExpired:
        print("Error: Compilation timed out")
        return None
    except Exception as e:
        print(f"Error during compilation: {e}")
        return None

def disassemble_bytes(data, base_addr):
    """Disassemble bytes using objdump."""
    base_dir = get_base_dir()
    
    with tempfile.NamedTemporaryFile(delete=False, suffix=".bin") as f:
        f.write(data)
        temp_path = f.name
    
    try:
        # Convert Windows path to Unix path for Docker
        temp_unix = temp_path.replace("\\", "/")
        
        # Use objdump to disassemble
        cmd = [
            "docker", "run", "--rm",
            "-v", f"{os.path.dirname(temp_path)}:/tmp/work",
            "-w", "/tmp/work",
            "dbz-legends-build",
            "mips-linux-gnu-objdump",
            "-D", "-b", "binary", "-m", "mips:3000",
            f"--adjust-vma={hex(base_addr)}",
            os.path.basename(temp_path)
        ]
        
        result = subprocess.run(cmd, capture_output=True, text=True)
        return result.stdout
    finally:
        os.unlink(temp_path)

def compare_bytes(original, compiled, addr):
    """Compare two byte sequences and show differences."""
    if original == compiled:
        print("\n✓ MATCH! Function matches byte-for-byte.")
        return True
    
    print(f"\n✗ MISMATCH!")
    print(f"Original size: {len(original)} bytes")
    print(f"Compiled size: {len(compiled)} bytes")
    
    # Find first difference
    min_len = min(len(original), len(compiled))
    for i in range(min_len):
        if original[i] != compiled[i]:
            print(f"\nFirst difference at offset 0x{i:X} (address 0x{addr + i:08X}):")
            
            # Show context
            start = max(0, i - 8)
            end = min(min_len, i + 12)
            
            print(f"\nOriginal (offset 0x{start:X} - 0x{end:X}):")
            print("  " + original[start:end].hex(' '))
            
            print(f"\nCompiled (offset 0x{start:X} - 0x{end:X}):")
            print("  " + compiled[start:end].hex(' '))
            break
    
    return False

def main():
    parser = argparse.ArgumentParser(description="Compare function matching")
    parser.add_argument("--overlay", "-o", default="title", help="Overlay name")
    parser.add_argument("--func", "-f", default="main", help="Function name")
    parser.add_argument("--addr", "-a", required=True, help="Function address (hex)")
    parser.add_argument("--size", "-s", required=True, help="Function size (hex)")
    parser.add_argument("--disasm", "-d", action="store_true", help="Show disassembly")
    
    args = parser.parse_args()
    
    addr = int(args.addr, 16)
    size = int(args.size, 16)
    
    print(f"Comparing function '{args.func}' in {args.overlay}")
    print(f"Address: 0x{addr:08X}, Size: 0x{size:X} ({size} bytes)")
    print("-" * 60)
    
    # Extract original
    print("\nExtracting original bytes...")
    original = extract_original_bytes(args.overlay, addr, size)
    print(f"  Got {len(original)} bytes from {EXE_FILES[args.overlay]}")
    
    # Compile and extract
    print("\nCompiling source and extracting...")
    compiled = compile_and_extract(args.overlay, args.func, addr, size)
    if compiled is None:
        print("  Failed to compile!")
        return 1
    print(f"  Got {len(compiled)} bytes from compiled output")
    
    # Compare
    match = compare_bytes(original, compiled, addr)
    
    # Show disassembly if requested
    if args.disasm:
        print("\n" + "=" * 60)
        print("ORIGINAL DISASSEMBLY:")
        print("=" * 60)
        print(disassemble_bytes(original, addr))
        
        print("\n" + "=" * 60)
        print("COMPILED DISASSEMBLY:")
        print("=" * 60)
        print(disassemble_bytes(compiled, addr))
    
    return 0 if match else 1

if __name__ == "__main__":
    sys.exit(main())
