@echo off
REM Build script for Windows using Docker

docker build -t dbz-legends-build .
docker run --rm -v "%cd%:/project" dbz-legends-build make %*
