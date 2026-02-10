/**
 * DBZ Legends - Overlay Analysis Script
 * 
 * This script helps analyze specific overlays by setting up proper memory
 * mapping and function boundaries for each overlay type.
 * 
 * @author DBZ Legends Decompilation Team
 * @category DBZ
 */

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSpace;
import ghidra.program.model.listing.Program;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.symbol.SymbolTable;
import ghidra.program.model.symbol.SourceType;
import ghidra.util.exception.InvalidInputException;
import java.util.HashMap;
import java.util.Map;

public class AnalyzeOverlay extends GhidraScript {
    
    // Overlay information
    private static final Map<String, OverlayInfo> OVERLAY_INFO = new HashMap<String, OverlayInfo>() {{
        put("main", new OverlayInfo(0x80020000L, "SLPS_003.55", "Main executable"));
        put("game", new OverlayInfo(0x80020000L, "GAME.EXE", "Game overlay"));
        put("title", new OverlayInfo(0x80020000L, "TITLE.EXE", "Title screen overlay"));
        put("select", new OverlayInfo(0x80020000L, "SELECT.EXE", "Select screen overlay"));
        put("vs", new OverlayInfo(0x80020000L, "VS.EXE", "VS mode overlay"));
        put("sp", new OverlayInfo(0x80020000L, "SP.EXE", "Special mode overlay"));
        put("demo", new OverlayInfo(0x80020000L, "DEMO.EXE", "Demo overlay"));
        put("movie", new OverlayInfo(0x80020000L, "MOVIE.EXE", "Movie player overlay"));
        put("ending", new OverlayInfo(0x80010000L, "ENDING.EXE", "Ending overlay"));
    }};
    
    private static class OverlayInfo {
        public final long vramStart;
        public final String filename;
        public final String description;
        
        public OverlayInfo(long vramStart, String filename, String description) {
            this.vramStart = vramStart;
            this.filename = filename;
            this.description = description;
        }
    }
    
    @Override
    public void run() throws Exception {
        println("=== DBZ Legends Overlay Analysis Script ===");
        
        Program program = getCurrentProgram();
        if (program == null) {
            printerr("No program is currently open!");
            return;
        }
        
        // Ask user which overlay to analyze
        String[] overlayNames = OVERLAY_INFO.keySet().toArray(new String[0]);
        String selectedOverlay = askChoice("Select Overlay", 
            "Which overlay would you like to analyze?", overlayNames, overlayNames[0]);
        
        if (selectedOverlay == null) {
            println("Analysis cancelled.");
            return;
        }
        
        OverlayInfo overlayInfo = OVERLAY_INFO.get(selectedOverlay);
        println("Analyzing overlay: " + selectedOverlay + " (" + overlayInfo.description + ")");
        
        // Perform overlay-specific analysis
        analyzeOverlay(program, selectedOverlay, overlayInfo);
        
        println("Overlay analysis completed!");
    }
    
    /**
     * Analyze a specific overlay
     */
    private void analyzeOverlay(Program program, String overlayName, OverlayInfo info) throws Exception {
        println("Setting up analysis for " + overlayName + " overlay...");
        
        AddressSpace space = program.getAddressFactory().getDefaultAddressSpace();
        SymbolTable symbolTable = program.getSymbolTable();
        FunctionManager functionManager = program.getFunctionManager();
        
        // Set entry point symbol
        Address entryPoint = space.getAddress(info.vramStart);
        try {
            symbolTable.createLabel(entryPoint, overlayName + "_entry", SourceType.USER_DEFINED);
            println("Set entry point: " + overlayName + "_entry at 0x" + 
                   Long.toHexString(info.vramStart));
        } catch (InvalidInputException e) {
            println("Entry point may already exist: " + e.getMessage());
        }
        
        // Create function at entry point if it doesn't exist
        Function entryFunction = functionManager.getFunctionAt(entryPoint);
        if (entryFunction == null) {
            try {
                entryFunction = functionManager.createFunction(overlayName + "_main", 
                    entryPoint, null, SourceType.USER_DEFINED);
                println("Created entry function: " + overlayName + "_main");
            } catch (Exception e) {
                println("Could not create entry function: " + e.getMessage());
            }
        }
        
        // Add overlay-specific symbols based on common PSX patterns
        addCommonPSXSymbols(symbolTable, space, info.vramStart, overlayName);
        
        // Suggest analysis options
        println("\nSuggested next steps for " + overlayName + ":");
        println("1. Run Auto Analysis (Analysis -> Auto Analyze...)");
        println("2. Look for string references to identify functions");
        println("3. Analyze function calls and jumps");
        println("4. Import symbols from project files if available");
        
        if (overlayName.equals("main")) {
            println("5. Look for PSX system calls (BIOS functions)");
            println("6. Identify CD-ROM access functions");
        } else {
            println("5. Look for overlay initialization code");
            println("6. Identify overlay-specific data structures");
        }
    }
    
    /**
     * Add common PSX symbols for an overlay
     */
    private void addCommonPSXSymbols(SymbolTable symbolTable, AddressSpace space, 
                                   long baseAddress, String overlayName) {
        // Common offsets for PSX executables (these are typical patterns)
        long[] commonOffsets = {0x0, 0x10, 0x20, 0x100, 0x200, 0x800, 0x1000};
        String[] commonNames = {"entry", "init", "main_loop", "data_start", 
                               "bss_start", "stack_init", "heap_start"};
        
        for (int i = 0; i < commonOffsets.length && i < commonNames.length; i++) {
            long address = baseAddress + commonOffsets[i];
            String symbolName = overlayName + "_" + commonNames[i];
            
            try {
                Address addr = space.getAddress(address);
                if (addr != null && program.getMemory().contains(addr)) {
                    symbolTable.createLabel(addr, symbolName, SourceType.ANALYSIS);
                    println("Added symbol: " + symbolName + " at 0x" + Long.toHexString(address));
                }
            } catch (Exception e) {
                // Symbol may already exist, continue
            }
        }
    }
}