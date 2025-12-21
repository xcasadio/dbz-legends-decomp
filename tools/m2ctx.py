#!/usr/bin/env python3
"""
DBZ Legends - m2ctx.py
Generate context for decomp.me from a source file
"""

import argparse
import os
import subprocess
import sys


def get_include_dirs():
    """Get include directories for the project."""
    return [
        "include",
        "include/psxsdk",
    ]


def generate_context(source_file, overlay="main"):
    """Generate context file for decomp.me."""
    include_flags = " ".join([f"-I{d}" for d in get_include_dirs()])
    
    # Read source file
    with open(source_file, 'r') as f:
        source = f.read()
    
    # Generate preprocessed output
    cmd = f"mipsel-linux-gnu-gcc -E {include_flags} -DUSE_INCLUDE_ASM {source_file}"
    
    try:
        result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
        if result.returncode == 0:
            print(result.stdout)
        else:
            print(f"Error: {result.stderr}", file=sys.stderr)
            sys.exit(1)
    except FileNotFoundError:
        print("Error: mipsel-linux-gnu-gcc not found. Please install the MIPS cross-compiler.")
        sys.exit(1)


def main():
    parser = argparse.ArgumentParser(description="Generate decomp.me context")
    parser.add_argument("source_file", help="Source file to process")
    parser.add_argument("--overlay", default="main", help="Overlay name")
    args = parser.parse_args()
    
    if not os.path.exists(args.source_file):
        print(f"Error: File not found: {args.source_file}")
        sys.exit(1)
    
    generate_context(args.source_file, args.overlay)


if __name__ == "__main__":
    main()
