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

# =============================================================================
# SDK PATTERNS - Functions to filter out (Sony PSX SDK, C runtime, etc.)
# =============================================================================
SDK_PATTERNS = [
    # =========================================================================
    # SPU (Sound Processing Unit) - libspu
    # =========================================================================
    r'^Spu\w+', r'^_spu_\w+', r'^Ss\w+',
    r'^SPU_OBJ_\w+', r'^S_M_WSA_OBJ_\w+', r'^S_SCA_OBJ_\w+',
    r'^S_N2P_OBJ_\w+', r'^S_ITC_OBJ_\w+', r'^S_STM_OBJ_\w+',
    r'^S_SI_OBJ_\w+', r'^S_SK_OBJ_\w+', r'^S_SIA_OBJ_\w+',
    r'^S_W0_OBJ_\w+', r'^S_SR_OBJ_\w+', r'^S_SAV_OBJ_\w+',
    r'^S_SRMP_OBJ_\w+', r'^S_CRWA_OBJ_\w+', r'^S_M_INT_OBJ_\w+',
    r'^S_M_INIT_OBJ_\w+', r'^S_M_UTIL_OBJ_\w+', r'^S_M_M_OBJ_\w+',
    r'^S_M_F_OBJ_\w+', r'^UT_GVBA_OBJ_\w+', r'^VS_SRV_OBJ_\w+',
    r'^ST_OBJ_\w+', r'^S_SVA_\w+',
    
    # =========================================================================
    # GTE (Geometry Transformation Engine) - libgte
    # =========================================================================
    r'^(RotMatrix|TransMatrix|ScaleMatrix|MulMatrix)\w*',
    r'^(RotTrans|TransRot|LocalLight|LightColor)\w*',
    r'^(NormalColor|ColorDpq|DpqColor)\w*',
    r'^(Square|OuterProduct|Apply\w*Matrix|CompMatrix)\w*',
    r'^(LoadAverage|AverageZ|NormalClip|Lzc)\w*',
    r'^(RotAverage|RotAverageNclip)\w*',
    r'^(Push|Pop|Read|Set)(Rot|Light|Color|Trans)Matrix\w*',
    r'^Set(Vertex|RGB|IR|SZ|SXSY|Rii|MAC|Data|Geom|Back|Far|DQ)\w*',
    r'^(Average|InitGeom|ColorCol|Intpl)\w*',
    r'^(Vector|Matrix)Normal\w*',
    r'^(IsIdMatrix|InvSquareRoot|EigenMatrix)\w*',
    r'^sin_\d+$', r'^_patch_gte$',
    r'^GEO_OBJ_\w+', r'^RATAN_OBJ_\w+', r'^FGO_\d+_OBJ_\w+', r'^MSC\d+_OBJ_\w+',
    
    # =========================================================================
    # CD-ROM - libcd
    # =========================================================================
    r'^Cd\w+', r'^CD_\w+', r'^cd_\w+',
    r'^_cmp$', r'^(def_cbsync|def_cbready|def_cbread)$',
    r'^callback$', r'^(cb_read|getintr)$',
    r'^BIOS_OBJ_\w+', r'^ISO9660_OBJ_\w+', r'^TOC_OBJ_\w+', r'^EVENT_OBJ_\w+',
    
    # =========================================================================
    # System/API - libapi
    # =========================================================================
    r'^(start|stop|restart|set|trap)Intr\w*',
    r'^INTR_OBJ_\w+', r'^INTR_DMA_OBJ_\w+',
    r'^VSync\w*', r'^v_wait$', r'^VSYNC_OBJ_\w+',
    r'^\w*Callback\w*$',
    r'^(OpenEvent|CloseEvent|EnableEvent|DisableEvent|WaitEvent|TestEvent|DeliverEvent)$',
    r'^Pad\w+', r'^PAD_\w+', r'^(StartPAD|StopPAD|PAD_init|ChangeClearPAD)$',
    r'^(SetRCnt|GetRCnt|StartRCnt|StopRCnt|ResetRCnt|ChangeClearRCnt)$',
    r'^COUNTER_OBJ_\w+', r'^(Get|Set)IntrMask$',
    
    # =========================================================================
    # C Runtime / BIOS
    # =========================================================================
    r'^(start|__main|__do_global_dtors)$',
    r'^(malloc|free|calloc|realloc|InitHeap|_expand|memset|memcpy|memmove|bzero|bcopy|memclr)$',
    r'^MALLOC_OBJ_\w+', r'^_Exp\w+',
    r'^(strlen|strcat|strcmp|strncmp|strcpy|strncpy|puts|printf|sprintf)$',
    r'^(srand|rand|setjmp|longjmp)$',
    r'^(rcos|rsin|ratan2|SquareRoot|SquareRoot0)$',
    r'^(open|read|close|lseek|write)$',
    r'^(firstfile|nextfile|format)$',
    r'^(FlushCache|LoadExec|ReturnFromException|HookEntryInt|ResetEntryInt)$',
    r'^_96_\w+', r'^_bu_init$',
    r'^(InitCARD|StartCARD|StopCARD|_new_card)$', r'^_card_\w+',
    
    # =========================================================================
    # GPU / Graphics System - libgpu
    # =========================================================================
    r'^(ResetGraph|SetGraphReverse|SetGraphDebug|SetGraphQueue|GetGraphType|GetGraphDebug)$',
    r'^(DrawSync|DrawSyncCallback|SetDispMask|DrawPrim|DrawOTag)$',
    r'^(ClearImage|LoadImage|StoreImage|MoveImage)$',
    r'^(ClearOTag|ClearOTagR)$',
    r'^(PutDrawEnv|GetDrawEnv|PutDispEnv|GetDispEnv|GetODE)$',
    r'^(SetTexWindow|SetDrawArea|SetDrawOffset|SetPriority|SetDrawMode|SetDrawEnv)$',
    r'^(SetDefDrawEnv|SetDefDispEnv)$',
    r'^SYS_OBJ_\w+',
    r'^(GetTPage|GetClut|DumpTPage|DumpClut|LoadTPage|LoadClut)$',
    r'^(NextPrim|IsEndPrim|AddPrim|AddPrims|CatPrim|TermPrim|MargePrim)$',
    r'^(SetSemiTrans|SetShadeTex)$',
    r'^Set(Poly|Sprt|Tile|Line|Block|Draw)\w*',
    r'^(DumpDrawEnv|DumpDispEnv)$',
    r'^PRIM_OBJ_\w+', r'^EXT_OBJ_\w+',
    r'^(FntLoad|FntOpen|FntFlush|FntPrint|SetDumpFnt)$',
    r'^FONT_OBJ_\w+',
    r'^GPU_\w+',
    r'^(_status|_otc|_clr|_dws|_drs|_ctl|_getctl|_cwb|_cwc|_param|_exeque|_reset|_sync|_version)$',
    r'^_addque\d*$',
    r'^(get_mode|get_cs|get_ce|get_ofs|get_tw|get_dx|set_alarm|get_alarm|checkRECT)$',
]


def is_sdk_function(name: str) -> bool:
    """Check if function name matches SDK patterns."""
    # main is the game entry point, not SDK
    if name == 'main':
        return False
    
    for pattern in SDK_PATTERNS:
        if re.match(pattern, name):
            return True
    return False


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
    sdk_functions = []
    
    for name, addr, size_hex in matches:
        size_dec = int(size_hex, 16)
        entry = (name, addr, size_hex, size_dec)
        
        if is_sdk_function(name):
            sdk_functions.append(entry)
        else:
            game_functions.append(entry)
    
    # Sort by size (ascending)
    game_sorted = sorted(game_functions, key=lambda x: x[3])
    
    # Generate output
    output_lines = [
        f"# DBZ Legends (Japan) - {overlay.upper()} overlay - Function Priority List",
        "# Sorted by size (ascending) - Start with smallest functions for easier decompilation",
        "# SDK/System functions have been filtered out",
        "#",
        f"# Total GAME functions: {len(game_sorted)}",
        f"# (Filtered {len(sdk_functions)} SDK functions)",
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
        'sdk': len(sdk_functions),
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
