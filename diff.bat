@echo off
REM Compare compiled code with original
REM Usage: diff.bat <overlay> <file> <func_start> <func_end>
REM Example: diff.bat game cd 80067404 800674fc

setlocal
set OVERLAY=%1
set FILE=%2
set START=%3
set END=%4

if "%END%"=="" (
    echo Usage: diff.bat ^<overlay^> ^<file^> ^<func_start^> ^<func_end^>
    echo Example: diff.bat game cd 80067404 800674fc
    exit /b 1
)

echo === Original ASM ===
call extract.bat %START% %END% %OVERLAY%

echo.
echo === Compiled ASM ===
call asm.bat %OVERLAY% %FILE%
