#!/usr/bin/env python3
"""
DBZ Legends - diff_settings.py
Configuration for asm-differ tool
"""


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
    
    # Base configuration
    config["baseimg"] = f"disks/jp/{get_disk_path(overlay)}"
    config["myimg"] = f"build/jp/{overlay}.bin"
    config["mapfile"] = f"build/jp/{overlay}.map"
    config["source_directories"] = [f"src/{overlay}"]
    config["build_dir"] = "build/"
    config["expected_dir"] = "expected/"
    config["objdump_executable"] = "mipsel-linux-gnu-objdump"
    config["arch"] = "mipsel"
    config["makeflags"] = []


def get_disk_path(overlay):
    """Get the disk path for a given overlay."""
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
