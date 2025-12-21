@echo off
REM Extract original assembly from the game executable
REM Usage: extract.bat <start_addr> <end_addr> [overlay]
REM Example: extract.bat 0x80021574 0x800215c0 main

setlocal
set START=%1
set END=%2
set OVERLAY=%3

if "%START%"=="" (
    echo Usage: extract.bat ^<start_addr^> ^<end_addr^> [overlay]
    echo Example: extract.bat 0x80021574 0x800215c0 main
    exit /b 1
)

if "%END%"=="" (
    echo Usage: extract.bat ^<start_addr^> ^<end_addr^> [overlay]
    echo Example: extract.bat 0x80021574 0x800215c0 main
    exit /b 1
)

if "%OVERLAY%"=="" set OVERLAY=main

docker run --rm -v "%cd%:/project" -w /project dbz-legends-build python3 tools/extract_asm.py data/SLPS_003.55 %START% %END% --overlay %OVERLAY%
