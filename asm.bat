@echo off
REM Compile a C file and show the generated assembly
REM Usage: asm.bat <overlay> <file>
REM Example: asm.bat main cd

setlocal
set OVERLAY=%1
set FILE=%2

if "%OVERLAY%"=="" (
    echo Usage: asm.bat ^<overlay^> ^<file^>
    echo Example: asm.bat main cd
    exit /b 1
)

if "%FILE%"=="" (
    echo Usage: asm.bat ^<overlay^> ^<file^>
    echo Example: asm.bat main cd
    exit /b 1
)

docker run --rm -v "%cd%:/project" -w /project dbz-legends-build /bin/bash -c ^
    "mips-linux-gnu-cpp -Iinclude -Iinclude/psxsdk -undef -D__GNUC__=2 -D__OPTIMIZE__ -DPSX src/%OVERLAY%/%FILE%.c -o /tmp/%FILE%.i && /usr/local/bin/cc1-psx-26 -O2 -G0 -quiet -mcpu=3000 -mgas -msoft-float /tmp/%FILE%.i -o -"
