@echo off
setlocal
set "MAPMAVEN_DATA_DIRECTORY=%~dp0data"
start "" /D "%~dp0" "%~dp0MapMaven.exe"
