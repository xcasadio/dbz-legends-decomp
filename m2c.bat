@echo off
REM Decompile assembly with m2c
REM Usage: m2c.bat <asm_file>
REM Example: m2c.bat asm/jp/main/cd.s

setlocal
set ASM_FILE=%1

if "%ASM_FILE%"=="" (
    echo Usage: m2c.bat ^<asm_file^>
    echo Example: m2c.bat asm/jp/main/cd.s
    exit /b 1
)

docker run --rm -v "%cd%:/project" -w /project dbz-legends-build python3 tools/m2c/m2c.py --target mipsel-gcc-c %ASM_FILE%
