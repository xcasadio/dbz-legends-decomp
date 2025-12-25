@echo off
REM Extract ASM for all functions from an overlay
REM Usage: extract_all.bat <overlay> [options]
REM Examples:
REM   extract_all.bat game              - Extract all functions from GAME.EXE
REM   extract_all.bat game --list       - List functions only
REM   extract_all.bat game -s CdRead2   - Extract only CdRead2

python tools/extract_all_asm.py %*
