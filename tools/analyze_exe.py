#!/usr/bin/env python3
"""Analyze PS-X EXE headers to extract memory layout information."""

import struct
import os
import hashlib

def analyze_psx_exe(filepath):
    """Extract PS-X EXE header information."""
    with open(filepath, 'rb') as f:
        data = f.read()
    
    header = data[:0x800]
    magic = header[0:8]
    
    if magic != b'PS-X EXE':
        return None
    
    # Calculate SHA1 of entire file
    sha1 = hashlib.sha1(data).hexdigest()
    
    # Parse header fields
    info = {
        'sha1': sha1,
        'entry_point': struct.unpack('<I', header[0x10:0x14])[0],
        'gp_value': struct.unpack('<I', header[0x14:0x18])[0],
        'text_addr': struct.unpack('<I', header[0x18:0x1C])[0],
        'text_size': struct.unpack('<I', header[0x1C:0x20])[0],
        'data_addr': struct.unpack('<I', header[0x20:0x24])[0],
        'data_size': struct.unpack('<I', header[0x24:0x28])[0],
        'bss_addr': struct.unpack('<I', header[0x28:0x2C])[0],
        'bss_size': struct.unpack('<I', header[0x2C:0x30])[0],
        'sp_base': struct.unpack('<I', header[0x30:0x34])[0],
        'sp_offset': struct.unpack('<I', header[0x34:0x38])[0],
        'file_size': len(data),
    }
    
    # Calculate end addresses
    info['text_end'] = info['text_addr'] + info['text_size']
    info['bss_end'] = info['bss_addr'] + info['bss_size']
    
    return info

def main():
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    data_dir = os.path.join(base_dir, 'data')
    
    files = [
        ('SLPS_003.55', 'main'),
        ('GAME.EXE', 'game'),
        ('TITLE.EXE', 'title'),
        ('SELECT.EXE', 'select'),
        ('VS.EXE', 'vs'),
        ('SP.EXE', 'sp'),
        ('DEMO.EXE', 'demo'),
        ('MOVIE.EXE', 'movie'),
        ('ENDING.EXE', 'ending'),
    ]
    
    print("=" * 100)
    print(f"{'File':<15} {'Entry Point':<12} {'vram_start':<12} {'text_size':<10} {'text_end':<12} {'bss_end':<12} {'GP Value':<12}")
    print("=" * 100)
    
    results = {}
    
    for filename, overlay_name in files:
        filepath = os.path.join(data_dir, filename)
        if os.path.exists(filepath):
            info = analyze_psx_exe(filepath)
            if info:
                results[overlay_name] = info
                print(f"{filename:<15} 0x{info['entry_point']:08X} 0x{info['text_addr']:08X} 0x{info['text_size']:06X}   "
                      f"0x{info['text_end']:08X} 0x{info['bss_end']:08X} 0x{info['gp_value']:08X}")
            else:
                print(f"{filename:<15} NOT A PS-X EXE")
        else:
            print(f"{filename:<15} FILE NOT FOUND")
    
    print("\n" + "=" * 100)
    print("YAML Configuration Values:")
    print("=" * 100)
    
    for overlay_name, info in results.items():
        print(f"\n  # {overlay_name}")
        print(f"  vram_start: 0x{info['text_addr']:08X}")
        print(f"  # gp_value: 0x{info['gp_value']:08X}")
        print(f"  # entry_point: 0x{info['entry_point']:08X}")
        print(f"  sha1: {info['sha1']}")
    
    print("\n" + "=" * 100)
    print("Detailed Segment Info:")
    print("=" * 100)
    
    for overlay_name, info in results.items():
        print(f"\n{overlay_name.upper()}:")
        print(f"  .text:  0x{info['text_addr']:08X} - 0x{info['text_end']:08X} (size: 0x{info['text_size']:X})")
        if info['data_addr'] != 0:
            data_end = info['data_addr'] + info['data_size']
            print(f"  .data:  0x{info['data_addr']:08X} - 0x{data_end:08X} (size: 0x{info['data_size']:X})")
        if info['bss_addr'] != 0:
            print(f"  .bss:   0x{info['bss_addr']:08X} - 0x{info['bss_end']:08X} (size: 0x{info['bss_size']:X})")

if __name__ == '__main__':
    main()
