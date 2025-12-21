#!/usr/bin/env python3
"""
DBZ Legends - diff_settings.py
Configuration for asm-differ tool
"""

import os

def add_custom_arguments(parser):
    """Add custom command line arguments."""
    parser.add_argument(
        "--overlay",
        default="main",
        dest="overlay",
        help="Overlay to diff (main, game, title, select, vs, sp, demo, movie, ending)"
    )


def apply(config, args):
    """Apply configuration based on arguments."""
    overlay = args.overlay
    
    # Paths
    disk_file = get_disk_path(overlay)
    base_dir = os.path.dirname(os.path.abspath(__file__))
    
    # Base configuration
    config["baseimg"] = os.path.join(base_dir, "data", disk_file)
    config["myimg"] = os.path.join(base_dir, "build", "jp", f"{overlay}.bin")
    config["mapfile"] = os.path.join(base_dir, "build", "jp", f"{overlay}.map")
    config["source_directories"] = [os.path.join(base_dir, "src", overlay)]
    config["build_dir"] = os.path.join(base_dir, "build")
    config["expected_dir"] = os.path.join(base_dir, "expected")
    
    # Symbol files
    config["symbol_addrs_paths"] = [
        os.path.join(base_dir, "config", f"symbols.{overlay}.jp.txt"),
        os.path.join(base_dir, "config", "sym_extern.jp.txt"),
    ]
    
    # Disassembler settings
    config["objdump_executable"] = "mips-linux-gnu-objdump"
    config["arch"] = "mips:3000"
    config["objdump_flags"] = ["-m", "mips:3000", "-M", "no-aliases"]
    
    # Make settings
    config["makeflags"] = [f"OVERLAY={overlay}"]
    config["make_command"] = ["make"]
    
    # Display settings
    config["show_line_numbers_default"] = True
    config["source_default"] = True
    
    # Memory map for PSX
    # .text starts after PSX-EXE header (0x800 bytes)
    vram_start = get_vram_start(overlay)
    config["base_shift"] = vram_start - 0x800  # Adjust for header offset


def get_disk_path(overlay):
    """Get the disk filename for a given overlay."""
    paths = {
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
    return paths.get(overlay, overlay)


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
