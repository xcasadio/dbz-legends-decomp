#!/usr/bin/env python3
"""
DBZ Legends - decompile.py
Helper script for decompiling functions using m2c
"""

import argparse
import os
import subprocess
import sys


def decompile_function(asm_file, function_name, output_file=None):
    """
    Decompile a function from assembly using m2c.
    """
    if not os.path.exists(asm_file):
        print(f"Error: Assembly file not found: {asm_file}")
        sys.exit(1)
    
    # Build m2c command
    cmd = [
        "python3", "tools/m2c/m2c.py",
        "--target", "mipsel-none-elf",
        "--context", "include/common.h",
        asm_file
    ]
    
    try:
        result = subprocess.run(cmd, capture_output=True, text=True)
        
        if result.returncode == 0:
            output = result.stdout
            if output_file:
                with open(output_file, 'w') as f:
                    f.write(output)
                print(f"Output written to: {output_file}")
            else:
                print(output)
        else:
            print(f"Error: {result.stderr}", file=sys.stderr)
            sys.exit(1)
            
    except FileNotFoundError:
        print("Error: m2c not found. Make sure tools/m2c is set up correctly.")
        sys.exit(1)


def main():
    parser = argparse.ArgumentParser(description="Decompile PSX assembly functions")
    parser.add_argument("asm_file", help="Assembly file containing the function")
    parser.add_argument("--function", "-f", help="Function name to decompile")
    parser.add_argument("--output", "-o", help="Output file")
    
    args = parser.parse_args()
    
    decompile_function(args.asm_file, args.function, args.output)


if __name__ == "__main__":
    main()
