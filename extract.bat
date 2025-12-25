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

REM Map overlay to executable file
set EXE_FILE=data/SLPS_003.55
if /i "%OVERLAY%"=="game" set EXE_FILE=data/GAME.EXE
if /i "%OVERLAY%"=="title" set EXE_FILE=data/TITLE.EXE
if /i "%OVERLAY%"=="select" set EXE_FILE=data/SELECT.EXE
if /i "%OVERLAY%"=="vs" set EXE_FILE=data/VS.EXE
if /i "%OVERLAY%"=="sp" set EXE_FILE=data/SP.EXE
if /i "%OVERLAY%"=="demo" set EXE_FILE=data/DEMO.EXE
if /i "%OVERLAY%"=="movie" set EXE_FILE=data/MOVIE.EXE
if /i "%OVERLAY%"=="ending" set EXE_FILE=data/ENDING.EXE

docker run --rm -v "%cd%:/project" -w /project dbz-legends-build python3 tools/extract_asm.py %EXE_FILE% %START% %END% --overlay %OVERLAY%
