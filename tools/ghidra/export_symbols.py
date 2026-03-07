# Ghidra Python script to export game symbols
# Usage: In Ghidra, go to Window > Script Manager, then run this script
#
# This script exports game functions AND data symbols to a symbols file.
# SDK/System functions are filtered out and logged to the console (not exported).
#
# @category Export
# @author DBZ Legends Decomp

from ghidra.program.model.symbol import SymbolType
from ghidra.program.model.listing import Function
import os
import re

# =============================================================================
# PSX SDK FUNCTION PATTERNS - These will be exported to sym_extern.jp.txt
# =============================================================================
SDK_PATTERNS = [
    #global patterns
    r'^\w*_OBJ_\w*',              # Generic object patterns (e.g., GPU_OBJ_*, SPU_OBJ_*, etc.)
    r'(switchD|switchdataD|caseD_|default)\w*',       # switchD_*, caseD_*

    # ==========================================================================
    # SPU (Sound Processing Unit) - libspu
    # ==========================================================================
    r'^Spu\w+',
    r'^_(spu|Spu)_?\w+',
    r'^Ss\w+',                    # SsUtGetVBaddrInSB, SsSetReservedVoice, etc.
    
    # SPU internal object functions (Ghidra auto-named from libspu)
    r'^SPU_OBJ_\w+',              # SPU_OBJ_F58, SPU_OBJ_1090, etc.
    r'^S_M_WSA_OBJ_\w+',          # S_M_WSA_OBJ_338, S_M_WSA_OBJ_5E0, etc.
    r'^S_SCA_OBJ_\w+',            # S_SCA_OBJ_58, S_SCA_OBJ_60, etc.
    r'^S_N2P_OBJ_\w+',            # S_N2P_OBJ_EC, S_N2P_OBJ_244
    r'^S_ITC_OBJ_\w+',            # S_ITC_OBJ_88, S_ITC_OBJ_90
    r'^S_STM_OBJ_\w+',            # S_STM_OBJ_1C
    r'^S_SI_OBJ_\w+',             # S_SI_OBJ_E8, S_SI_OBJ_120
    r'^S_SK_OBJ_\w+',             # S_SK_OBJ_70
    r'^S_SIA_OBJ_\w+',            # S_SIA_OBJ_30
    r'^S_W0_OBJ_\w+',             # S_W0_OBJ_A0
    r'^S_SR_OBJ_\w+',             # S_SR_OBJ_B0, S_SR_OBJ_B4
    r'^S_SAV_OBJ_\w+',            # S_SAV_OBJ_A0
    r'^S_SRMP_OBJ_\w+',           # S_SRMP_OBJ_16C, S_SRMP_OBJ_17C, etc.
    r'^S_CRWA_OBJ_\w+',           # S_CRWA_OBJ_9C, S_CRWA_OBJ_100, etc.
    r'^S_M_INT_OBJ_\w+',          # S_M_INT_OBJ_50, S_M_INT_OBJ_C0, etc.
    r'^S_M_INIT_OBJ_\w+',         # S_M_INIT_OBJ_4C
    r'^S_M_UTIL_OBJ_\w+',         # S_M_UTIL_OBJ_18, S_M_UTIL_OBJ_94
    r'^S_M_M_OBJ_\w+',            # S_M_M_OBJ_54, S_M_M_OBJ_12C, S_M_M_OBJ_2A8
    r'^S_M_F_OBJ_\w+',            # S_M_F_OBJ_64
    r'^UT_GVBA_OBJ_\w+',          # UT_GVBA_OBJ_50
    r'^VS_SRV_OBJ_\w+',           # VS_SRV_OBJ_30
    r'^ST_OBJ_\w+',
    r'^S_SVA_\w+',
    
    # ==========================================================================
    # GTE (Geometry Transformation Engine) - libgte
    # ==========================================================================
    # GTE function wrappers
    r'^(Rot|Trans|Scale|Mul)\w*Matrix\w*',  # RotMatrix, MulRotMatrix, MulRotMatrix0, etc.
    r'^(RotTrans|TransRot|LocalLight|LightColor)\w*',
    r'^(NormalColor|ColorDpq|DpqColor)\w*',
    r'^(Square|OuterProduct|Apply\w*Matrix|CompMatrix)\w*',
    r'^(LoadAverage|AverageZ|NormalClip|Lzc)\w*',
    r'^(RotAverage|RotAverageNclip)\w*',
    r'^(Push|Pop|Read|Set)\w*Matrix\w*',    # PushMatrix, PopMatrix, SetMulMatrix, etc.
    r'^Set(Vertex|RGB|IR|SZ|SXSY|Rii|MAC|Data|Geom|Back|Far|DQ)\w*',
    r'^(Average|InitGeom|ColorCol|Intpl)\w*',
    r'^(Vector|Matrix)Normal\w*',
    r'^(IsIdMatrix|InvSquareRoot|EigenMatrix)\w*',
    r'^sin_\d+$',                 # sin_1, sin_2, etc.
    r'^_patch_gte$',
    
    # GTE internal object functions (Ghidra auto-named from libgte)
    r'^GEO_OBJ_\w+',              # GEO_OBJ_9A0, GEO_OBJ_CFC, etc.
    r'^RATAN_OBJ_\w+',            # RATAN_OBJ_180, RATAN_OBJ_BC, etc.
    r'^FGO_\d+_OBJ_\w+',          # FGO_01_OBJ_64, FGO_05_OBJ_64, etc.
    r'^MSC\d+_OBJ_\w+',           # MSC02_OBJ_FC
    
    # ==========================================================================
    # CD-ROM - libcd
    # ==========================================================================
    r'^Cd\w+',                    # CdInit, CdRead, CdSync, etc.
    r'^CD_\w+',                   # CD_init, CD_sync, CD_cw, etc.
    r'^cd_\w+',                   # cd_read, cd_read_retry
    r'^_cmp$',                    # ISO9660 string compare
    r'^(def_cbsync|def_cbready|def_cbread)$',
    r'^callback$',
    r'^(cb_read|getintr)$',
    
    # CD internal object functions (Ghidra auto-named from libcd)
    r'^BIOS_OBJ_\w+',             # BIOS_OBJ_19CC, BIOS_OBJ_530, etc.
    r'^ISO9660_OBJ_\w+',          # ISO9660_OBJ_2B4, ISO9660_OBJ_2C0, etc.
    r'^TOC_OBJ_\w+',              # TOC_OBJ_220
    r'^EVENT_OBJ_\w+',            # EVENT_OBJ_7C
    
    # ==========================================================================
    # System/API - libapi
    # ==========================================================================
    # Interrupt handling
    r'^(start|stop|restart|set|trap)Intr\w*',
    r'^INTR_OBJ_\w+',             # INTR_OBJ_6D4, INTR_OBJ_428, etc.
    r'^INTR_DMA_OBJ_\w+',         # INTR_DMA_OBJ_274
    
    # VSync
    r'^VSync\w*',
    r'^v_wait$',
    r'^VSYNC_OBJ_\w+',            # VSYNC_OBJ_1D4, VSYNC_OBJ_130, etc.
    
    # Callbacks
    r'^\w*Callback\w*$',          # ResetCallback, VSyncCallback, CheckCallback, etc.
    
    # Events
    r'^(OpenEvent|CloseEvent|EnableEvent|DisableEvent|WaitEvent|TestEvent|DeliverEvent)$',
    
    # Pad/Controller
    r'^Pad\w+',                   # PadInit, PadRead, PadStop
    r'^PAD_\w+',                  # PAD_dr
    r'^(StartPAD|StopPAD|PAD_init|ChangeClearPAD)$',
    
    # Timer/Counter
    r'^(SetRCnt|GetRCnt|StartRCnt|StopRCnt|ResetRCnt|ChangeClearRCnt)$',
    r'^COUNTER_OBJ_\w+',
    
    # Interrupt mask
    r'^(Get|Set)IntrMask$',
    
    # ==========================================================================
    # C Runtime / BIOS
    # ==========================================================================
    # Startup/exit
    r'^(start|__main|__do_global_dtors)$',
    
    # Memory functions
    r'^(malloc|free|calloc|realloc|InitHeap|_expand|memset|memcpy|memmove|bzero|bcopy|memclr)$',
    r'^MALLOC_OBJ_\w+',
    r'^_Exp\w+',
    
    # String functions
    r'^(strlen|strcat|strcmp|strncmp|strcpy|strncpy|puts|printf|sprintf)$',
    
    # Math functions
    r'^(srand|rand|setjmp|longjmp)$',
    r'^(rcos|rsin|ratan2|SquareRoot|SquareRoot0)$',
    
    # File I/O
    r'^(open|read|close|lseek|write)$',
    r'^(firstfile|nextfile|format)$',
    
    # System/BIOS
    r'^(FlushCache|LoadExec|ReturnFromException|HookEntryInt|ResetEntryInt)$',
    r'^_96_\w+',
    r'^_bu_init$',
    
    # Memory Card
    r'^(InitCARD|StartCARD|StopCARD|_new_card)$',
    r'^_card_\w+',
    
    # ==========================================================================
    # GPU / Graphics System - libgpu
    # ==========================================================================
    r'^(ResetGraph|SetGraphReverse|SetGraphDebug|SetGraphQueue|GetGraphType|GetGraphDebug)$',
    r'^(DrawSync|DrawSyncCallback|SetDispMask|DrawPrim|DrawOTag)$',
    r'^(ClearImage|LoadImage|StoreImage|MoveImage)$',
    r'^(ClearOTag|ClearOTagR)$',
    r'^(PutDrawEnv|GetDrawEnv|PutDispEnv|GetDispEnv|GetODE)$',
    r'^(SetTexWindow|SetDrawArea|SetDrawOffset|SetPriority|SetDrawMode|SetDrawEnv)$',
    r'^(SetDefDrawEnv|SetDefDispEnv)$',
    r'^SYS_OBJ_\w+',
    
    # Primitives
    r'^(GetTPage|GetClut|DumpTPage|DumpClut|LoadTPage|LoadClut)$',
    r'^(NextPrim|IsEndPrim|AddPrim|AddPrims|CatPrim|TermPrim|MargePrim)$',
    r'^(SetSemiTrans|SetShadeTex)$',
    r'^Set(Poly|Sprt|Tile|Line|Block|Draw)\w*',
    r'^(DumpDrawEnv|DumpDispEnv)$',
    r'^PRIM_OBJ_\w+',
    r'^EXT_OBJ_\w+',
    
    # Font system
    r'^(FntLoad|FntOpen|FntFlush|FntPrint|SetDumpFnt)$',
    r'^FONT_OBJ_\w+',
    
    # GPU low-level
    r'^GPU_\w+',
    r'^(_status|_otc|_clr|_dws|_drs|_ctl|_getctl|_cwb|_cwc|_param|_exeque|_reset|_sync|_version)$',
    r'^_addque\d*$',
    r'^(get_mode|get_cs|get_ce|get_ofs|get_tw|get_dx|set_alarm|get_alarm|checkRECT)$',
]

# GTE inline macros - these are NOT real functions, skip entirely
GTE_MACRO_PATTERNS = [
    r'^gte_\w+',  # All gte_ prefixed symbols are inline macros
]


def is_valid_address(addr):
    """Check if address is in valid PSX RAM range (0x80000000 - 0x807FFFFF)."""
    # Convert to unsigned 32-bit
    offset = addr & 0xFFFFFFFF
    
    # GTE fake addresses start at 0x20000000 - reject immediately
    if offset < 0x80000000:
        return False
    
    # PSX RAM is 0x80000000 - 0x801FFFFF (2MB main RAM)
    # Extended to 0x807FFFFF for larger address space / overlays
    return offset <= 0x807FFFFF


def is_gte_macro(func_name):
    """Check if function is a GTE inline macro (not a real function)."""
    for pattern in GTE_MACRO_PATTERNS:
        if re.match(pattern, func_name):
            return True
    return False


def is_sdk_function(func_name):
    """Check if function/symbol is a PSX SDK function."""
    # main is the game entry point, not SDK
    if func_name == 'main':
        return False
    
    for pattern in SDK_PATTERNS:
        if re.match(pattern, func_name):
            return True
    return False


def get_function_size(func):
    """Get the size of a function in bytes."""
    body = func.getBody()
    if body:
        return body.getNumAddresses()
    return 0


def sanitize_name(name):
    """Sanitize symbol name for output."""
    return re.sub(r'[^a-zA-Z0-9_]', '_', name)


def export_symbols():
    """Main export function - exports functions."""
    
    func_manager = currentProgram.getFunctionManager()
    
    game_functions = []
    sdk_functions = []
    skipped_gte = []
    skipped_invalid = []
    
    for func in func_manager.getFunctions(True):
        name = func.getName()
        addr = func.getEntryPoint().getOffset()
        size = get_function_size(func)
        
        # Skip thunks to external functions
        if func.isThunk():
            thunked = func.getThunkedFunction(False)
            if thunked and thunked.isExternal():
                continue
        
        # Skip external functions
        if func.isExternal():
            continue
        
        # Check address FIRST - invalid addresses are always skipped
        if not is_valid_address(addr):
            # GTE macros are at 0x20000000, track them separately
            if is_gte_macro(name) or (addr & 0xFFFFFFFF) < 0x80000000:
                skipped_gte.append((name, addr))
            else:
                skipped_invalid.append((name, addr))
            continue
        
        entry = {
            'name': sanitize_name(name),
            'addr': addr,
            'size': size,
            'type': 'function',
        }
        
        if is_sdk_function(name):
            sdk_functions.append(entry)
        else:
            game_functions.append(entry)
    
    # Sort by address
    game_functions.sort(key=lambda x: x['addr'])
    sdk_functions.sort(key=lambda x: x['addr'])
    
    return game_functions, sdk_functions, skipped_gte, skipped_invalid


def export_data_symbols():
    """Export data symbols (non-function symbols), separating game from SDK."""
    
    symbol_table = currentProgram.getSymbolTable()
    
    game_symbols = []
    sdk_symbols = []
    
    for symbol in symbol_table.getAllSymbols(True):
        # Skip function symbols (handled separately)
        if symbol.getSymbolType() == SymbolType.FUNCTION:
            continue
        
        # Skip external symbols
        if symbol.isExternal():
            continue
        
        # Skip default/dynamic symbols
        if symbol.isDynamic():
            continue
        
        name = symbol.getName()
        addr = symbol.getAddress().getOffset()
        
        # Skip invalid addresses
        if not is_valid_address(addr):
            continue
        
        # Skip labels that look like auto-generated (DAT_, LAB_, etc.)
        if re.match(r'^(DAT_|LAB_|PTR_|BYTE_|WORD_|DWORD_|undefined)', name):
            continue
        
        entry = {
            'name': sanitize_name(name),
            'addr': addr,
            'size': 0,
            'type': 'data',
        }
        
        if is_sdk_function(name):
            sdk_symbols.append(entry)
        else:
            game_symbols.append(entry)
    
    # Sort by address
    game_symbols.sort(key=lambda x: x['addr'])
    sdk_symbols.sort(key=lambda x: x['addr'])
    
    return game_symbols, sdk_symbols


def write_game_symbols(filepath, functions, symbols, overlay_name):
    """Write game symbols file (functions + data symbols)."""
    
    # Combine and sort by address
    all_entries = functions + symbols
    all_entries.sort(key=lambda x: x['addr'])
    
    # Count types
    func_count = len([e for e in all_entries if e['type'] == 'function'])
    data_count = len([e for e in all_entries if e['type'] == 'data'])
    
    with open(filepath, 'w') as f:
        f.write("# DBZ Legends (Japan) - {} overlay symbols\n".format(overlay_name.upper()))
        f.write("# Game functions and data symbols only (SDK filtered out)\n")
        f.write("#\n")
        f.write("# Format: symbol_name = 0xADDRESS; // size: 0xXX (for functions)\n")
        f.write("# Total: {} functions, {} data symbols\n".format(func_count, data_count))
        f.write("\n")
        
        for entry in all_entries:
            if entry['type'] == 'function' and entry['size'] > 0:
                f.write("{} = 0x{:08X}; // size: 0x{:X}\n".format(
                    entry['name'], entry['addr'], entry['size']))
            else:
                f.write("{} = 0x{:08X};\n".format(entry['name'], entry['addr']))


def log_sdk_symbols(functions, symbols):
    """Log filtered SDK symbols to Ghidra console (NOT exported to file)."""
    
    all_sdk = functions + symbols
    all_sdk.sort(key=lambda x: x['addr'])
    
    if not all_sdk:
        print("No SDK symbols to filter.")
        return
    
    print("")
    print("=" * 60)
    print("FILTERED SDK SYMBOLS (NOT exported - logged only)")
    print("=" * 60)
    
    for entry in all_sdk:
        type_str = "[func]" if entry.get('type') == 'function' else "[data]"
        print("  0x{:08X}: {} {}".format(entry['addr'], entry['name'], type_str))
    
    print("")
    print("Total SDK symbols filtered: {} ({} functions, {} data)".format(
        len(all_sdk),
        len([e for e in all_sdk if e.get('type') == 'function']),
        len([e for e in all_sdk if e.get('type') == 'data'])))
    print("=" * 60)


def main():
    """Main entry point."""
    from javax.swing import JOptionPane, JFileChooser
    from javax.swing.filechooser import FileNameExtensionFilter
    import java.io
    
    print("=" * 60)
    print("Ghidra Symbol Exporter for DBZ Legends Decomp")
    print("=" * 60)
    print("")
    print("This script will:")
    print("  - Export GAME functions and symbols to symbols.<overlay>.jp.txt")
    print("  - Filter SDK functions (logged to console, NOT exported)")
    print("  - Skip GTE macros and invalid addresses")
    print("")
    
    # Determine overlay name from program
    program_name = currentProgram.getName()
    base_name = os.path.splitext(program_name)[0]
    
    overlay_map = {
        "SLPS_003.55": "main",
        "SLPS_00355": "main",
        "GAME": "game",
        "TITLE": "title",
        "SELECT": "select",
        "VS": "vs",
        "SP": "sp",
        "DEMO": "demo",
        "MOVIE": "movie",
        "ENDING": "ending",
    }
    overlay_name = overlay_map.get(base_name.upper(), base_name.lower())
    
    print("Detected overlay: {}".format(overlay_name.upper()))
    print("")
    
    # Ask for output directory
    chooser = JFileChooser()
    chooser.setDialogTitle("Select output directory (config folder)")
    chooser.setFileSelectionMode(JFileChooser.DIRECTORIES_ONLY)
    
    result = chooser.showOpenDialog(None)
    if result != JFileChooser.APPROVE_OPTION:
        print("Export cancelled.")
        return
    
    output_dir = chooser.getSelectedFile().getAbsolutePath()
    
    # Export functions
    print("Analyzing functions...")
    game_funcs, sdk_funcs, skipped_gte, skipped_invalid = export_symbols()
    
    # Export data symbols
    print("Analyzing data symbols...")
    game_syms, sdk_syms = export_data_symbols()
    
    print("")
    print("Analysis results:")
    print("  Game functions:     {}".format(len(game_funcs)))
    print("  Game data symbols:  {}".format(len(game_syms)))
    print("  SDK functions:      {} (filtered)".format(len(sdk_funcs)))
    print("  SDK data symbols:   {} (filtered)".format(len(sdk_syms)))
    print("  Skipped GTE macros: {}".format(len(skipped_gte)))
    print("  Skipped invalid:    {}".format(len(skipped_invalid)))
    print("")
    
    # Log SDK symbols to console (NOT exported to file)
    log_sdk_symbols(sdk_funcs, sdk_syms)
    
    # Write ONLY game symbols file
    game_file = os.path.join(output_dir, "symbols.{}.jp.txt".format(overlay_name))
    write_game_symbols(game_file, game_funcs, game_syms, overlay_name)
    
    print("")
    print("Exported to: {}".format(game_file))
    print("")
    print("Done!")
    
    # Show summary
    from ghidra.util import Msg
    Msg.showInfo(None, None, "Export Complete",
        "Exported {} game functions and {} data symbols to:\n{}\n\n"
        "Filtered {} SDK symbols (see console for details)".format(
            len(game_funcs), len(game_syms), game_file,
            len(sdk_funcs) + len(sdk_syms)))


if __name__ == "__main__":
    main()
