/**
 * DBZ Legends - Ghidra Import Script
 * 
 * This script automates the import and initial setup of DBZ Legends binaries
 * in Ghidra, including memory layout configuration and symbol import.
 * 
 * @author DBZ Legends Decompilation Team
 * @category DBZ
 */

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSpace;
import ghidra.program.model.listing.Program;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.SymbolTable;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SourceType;
import ghidra.util.exception.InvalidInputException;
import java.io.File;
import java.io.IOException;
import java.util.HashMap;
import java.util.Map;

public class ImportDBZLegends extends GhidraScript {
    
    // Memory layout constants for DBZ Legends overlays
    private static final Map<String, Long> OVERLAY_VRAM_ADDRESSES = new HashMap<String, Long>() {{
        put("main", 0x80020000L);
        put("game", 0x80020000L);
        put("title", 0x80020000L);
        put("select", 0x80020000L);
        put("vs", 0x80020000L);
        put("sp", 0x80020000L);
        put("demo", 0x80020000L);
        put("movie", 0x80020000L);
        put("ending", 0x80010000L);
    }};
    
    // PSX memory regions
    private static final long RAM_START = 0x80000000L;
    private static final long RAM_SIZE = 0x00200000L; // 2MB
    private static final long BIOS_START = 0xBFC00000L;
    private static final long BIOS_SIZE = 0x00080000L; // 512KB
    private static final long HW_REGS_START = 0x1F800000L;
    private static final long HW_REGS_SIZE = 0x00001000L; // 4KB
    
    @Override
    public void run() throws Exception {
        println("=== DBZ Legends Ghidra Import Script ===");
        
        Program program = getCurrentProgram();
        if (program == null) {
            printerr("No program is currently open!");
            return;
        }
        
        // Setup memory layout
        setupMemoryLayout(program);
        
        // Import symbols if available
        importSymbols(program);
        
        // Setup data types
        setupDataTypes(program);
        
        println("DBZ Legends import completed successfully!");
    }
    
    /**
     * Setup PSX memory layout with proper segments
     */
    private void setupMemoryLayout(Program program) throws Exception {
        println("Setting up PSX memory layout...");
        
        Memory memory = program.getMemory();
        AddressSpace space = program.getAddressFactory().getDefaultAddressSpace();
        
        // Create RAM segment (if not exists)
        Address ramStart = space.getAddress(RAM_START);
        if (memory.getBlock(ramStart) == null) {
            try {
                MemoryBlock ramBlock = memory.createInitializedBlock(
                    "RAM", ramStart, RAM_SIZE, (byte) 0, monitor, false);
                ramBlock.setRead(true);
                ramBlock.setWrite(true);
                ramBlock.setExecute(true);
                println("Created RAM block: 0x" + Long.toHexString(RAM_START) + 
                       " - 0x" + Long.toHexString(RAM_START + RAM_SIZE));
            } catch (Exception e) {
                println("RAM block may already exist: " + e.getMessage());
            }
        }
        
        // Create BIOS segment
        Address biosStart = space.getAddress(BIOS_START);
        if (memory.getBlock(biosStart) == null) {
            try {
                MemoryBlock biosBlock = memory.createInitializedBlock(
                    "BIOS", biosStart, BIOS_SIZE, (byte) 0, monitor, false);
                biosBlock.setRead(true);
                biosBlock.setWrite(false);
                biosBlock.setExecute(true);
                println("Created BIOS block: 0x" + Long.toHexString(BIOS_START) + 
                       " - 0x" + Long.toHexString(BIOS_START + BIOS_SIZE));
            } catch (Exception e) {
                println("BIOS block may already exist: " + e.getMessage());
            }
        }
        
        // Create Hardware Registers segment
        Address hwRegsStart = space.getAddress(HW_REGS_START);
        if (memory.getBlock(hwRegsStart) == null) {
            try {
                MemoryBlock hwRegsBlock = memory.createInitializedBlock(
                    "HW_REGS", hwRegsStart, HW_REGS_SIZE, (byte) 0, monitor, false);
                hwRegsBlock.setRead(true);
                hwRegsBlock.setWrite(true);
                hwRegsBlock.setExecute(false);
                println("Created HW_REGS block: 0x" + Long.toHexString(HW_REGS_START) + 
                       " - 0x" + Long.toHexString(HW_REGS_START + HW_REGS_SIZE));
            } catch (Exception e) {
                println("HW_REGS block may already exist: " + e.getMessage());
            }
        }
    }
    
    /**
     * Import known symbols from the project
     */
    private void importSymbols(Program program) throws Exception {
        println("Importing symbols...");
        
        SymbolTable symbolTable = program.getSymbolTable();
        AddressSpace space = program.getAddressFactory().getDefaultAddressSpace();
        
        // Add some known PSX system symbols
        addSymbolIfNotExists(symbolTable, space, 0x80000000L, "RAM_START");
        addSymbolIfNotExists(symbolTable, space, 0xBFC00000L, "BIOS_START");
        addSymbolIfNotExists(symbolTable, space, 0x1F800000L, "HW_REGS_START");
        
        // Add overlay entry points (these would be discovered during analysis)
        for (Map.Entry<String, Long> entry : OVERLAY_VRAM_ADDRESSES.entrySet()) {
            String overlayName = entry.getKey();
            Long address = entry.getValue();
            addSymbolIfNotExists(symbolTable, space, address, overlayName + "_start");
        }
        
        println("Symbol import completed.");
    }
    
    /**
     * Helper method to add symbol if it doesn't exist
     */
    private void addSymbolIfNotExists(SymbolTable symbolTable, AddressSpace space, 
                                    long address, String name) {
        try {
            Address addr = space.getAddress(address);
            Symbol existing = symbolTable.getPrimarySymbol(addr);
            if (existing == null || existing.getName().startsWith("FUN_") || 
                existing.getName().startsWith("DAT_")) {
                symbolTable.createLabel(addr, name, SourceType.USER_DEFINED);
                println("Added symbol: " + name + " at 0x" + Long.toHexString(address));
            }
        } catch (InvalidInputException e) {
            println("Failed to add symbol " + name + ": " + e.getMessage());
        }
    }
    
    /**
     * Setup common data types used in DBZ Legends
     */
    private void setupDataTypes(Program program) throws Exception {
        println("Setting up data types...");
        
        // This would typically involve importing C header files
        // or manually creating structures. For now, we'll just log
        // that this step would be performed.
        
        println("Data types setup completed.");
        println("Note: Import your C header files (common.h, game.h) manually");
        println("using File -> Parse C Source... for complete type information.");
    }
}