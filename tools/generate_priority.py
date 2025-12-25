#!/usr/bin/env python3
"""
Generate priority file for decompilation.

Reads a symbols file, filters out SDK functions, and generates a priority
file sorted by function size (smallest first) for easier decompilation.

Usage:
    python tools/generate_priority.py <overlay>
    python tools/generate_priority.py title
    python tools/generate_priority.py game
    python tools/generate_priority.py --all
"""

import re
import sys
import os
from pathlib import Path


def get_priority_level(size: int) -> str:
    """Get priority level based on function size."""
    if size <= 0x40:
        return 'EASY'
    elif size <= 0x80:
        return 'SIMPLE'
    elif size <= 0x150:
        return 'MEDIUM'
    elif size <= 0x300:
        return 'HARD'
    else:
        return 'EXPERT'


def generate_priority(overlay: str, config_dir: Path) -> dict:
    """Generate priority file for an overlay."""
    
    symbols_file = config_dir / f"symbols.{overlay}.jp.txt"
    priority_file = config_dir / f"priority.{overlay}.jp.txt"
    
    if not symbols_file.exists():
        print(f"Error: {symbols_file} not found")
        return None
    
    # Read symbols file
    with open(symbols_file, 'r') as f:
        content = f.read()
    
    # Parse functions with sizes
    pattern = r'(\w+)\s*=\s*(0x[0-9A-Fa-f]+);\s*//\s*size:\s*(0x[0-9A-Fa-f]+)'
    matches = re.findall(pattern, content)
    
    if not matches:
        print(f"Warning: No functions with size comments found in {symbols_file}")
        return None
    
    # Separate game and SDK functions
    game_functions = []
    
    for name, addr, size_hex in matches:
        size_dec = int(size_hex, 16)
        entry = (name, addr, size_hex, size_dec)
        game_functions.append(entry)
    
    # Sort by size (ascending)
    game_sorted = sorted(game_functions, key=lambda x: x[3])
    
    # Generate output
    output_lines = [
        f"# DBZ Legends (Japan) - {overlay.upper()} overlay - Function Priority List",
        "# Sorted by size (ascending) - Start with smallest functions for easier decompilation",
        "# SDK/System functions have been filtered out",
        "#",
        "# PRIORITY LEVELS:",
        "#   [EASY]   0x00-0x40   (0-64 bytes)    - Trivial functions, likely getters/setters",
        "#   [SIMPLE] 0x41-0x80   (65-128 bytes)  - Simple logic, few branches",
        "#   [MEDIUM] 0x81-0x150  (129-336 bytes) - Moderate complexity",
        "#   [HARD]   0x151-0x300 (337-768 bytes) - Complex logic, many branches",
        "#   [EXPERT] 0x301+      (769+ bytes)    - Very complex, consider splitting analysis",
        "",
    ]
    
    current_level = None
    rank = 1
    
    for name, addr, size_hex, size_dec in game_sorted:
        level = get_priority_level(size_dec)
        
        if level != current_level:
            output_lines.append("")
            output_lines.append(f"# ============ [{level}] ============")
            current_level = level
        
        output_lines.append(f"{rank:3d}. {name} = {addr}; // size: {size_hex} ({size_dec} bytes)")
        rank += 1
    
    # Write priority file
    with open(priority_file, 'w') as f:
        f.write('\n'.join(output_lines))
    
    # Calculate statistics
    stats = {
        'overlay': overlay,
        'total': len(matches),
        'game': len(game_sorted),
        'easy': len([f for f in game_sorted if f[3] <= 0x40]),
        'simple': len([f for f in game_sorted if 0x40 < f[3] <= 0x80]),
        'medium': len([f for f in game_sorted if 0x80 < f[3] <= 0x150]),
        'hard': len([f for f in game_sorted if 0x150 < f[3] <= 0x300]),
        'expert': len([f for f in game_sorted if f[3] > 0x300]),
        'output_file': priority_file,
    }
    
    return stats


def print_stats(stats: dict):
    """Print statistics for an overlay."""
    print(f"\n=== {stats['overlay'].upper()} ===")
    print(f"Total functions: {stats['total']}")
    print(f"SDK filtered:    {stats['sdk']}")
    print(f"Game functions:  {stats['game']}")
    print(f"")
    print(f"Priority breakdown:")
    print(f"  [EASY]   {stats['easy']:3d} functions (0-64 bytes)")
    print(f"  [SIMPLE] {stats['simple']:3d} functions (65-128 bytes)")
    print(f"  [MEDIUM] {stats['medium']:3d} functions (129-336 bytes)")
    print(f"  [HARD]   {stats['hard']:3d} functions (337-768 bytes)")
    print(f"  [EXPERT] {stats['expert']:3d} functions (769+ bytes)")
    print(f"")
    print(f"Output: {stats['output_file']}")


def main():
    # Find project root
    script_dir = Path(__file__).parent
    project_root = script_dir.parent
    config_dir = project_root / "config"
    
    if not config_dir.exists():
        print(f"Error: config directory not found at {config_dir}")
        sys.exit(1)
    
    # Parse arguments
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    
    overlay = sys.argv[1].lower()
    
    # List of all overlays
    all_overlays = ['main', 'game', 'title', 'select', 'vs', 'sp', 'demo', 'movie', 'ending']
    
    if overlay == '--all':
        # Generate for all overlays that have symbol files
        for ov in all_overlays:
            symbols_file = config_dir / f"symbols.{ov}.jp.txt"
            if symbols_file.exists():
                stats = generate_priority(ov, config_dir)
                if stats:
                    print_stats(stats)
    else:
        if overlay not in all_overlays:
            print(f"Warning: '{overlay}' is not a standard overlay name")
        
        stats = generate_priority(overlay, config_dir)
        if stats:
            print_stats(stats)
        else:
            sys.exit(1)


if __name__ == "__main__":
    main()
