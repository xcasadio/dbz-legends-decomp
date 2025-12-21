@echo off
REM Open a shell inside the Docker container
REM Usage: shell.bat

docker run --rm -it -v "%cd%:/project" -w /project dbz-legends-build /bin/bash
