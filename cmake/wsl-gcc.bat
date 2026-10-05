@echo off
REM WSL GCC wrapper — called from CMake on Windows as the C compiler
REM Translates Windows paths to WSL paths and invokes Linux gcc
setlocal enabledelayedexpansion

set ARGS=
set SRC=
set OUT=

:parse
if "%~1"=="" goto done
set ARG=%~1
if "!ARG:~0,1!"=="-" (
    if "!ARG!"=="-c" (
        set SRC=%~2
        set ARGS=!ARGS! -c "%~f2"
        shift
    ) else if "!ARG!"=="-o" (
        set OUT=%~2
        set ARGS=!ARGS! -o "%~f2"
        shift
    ) else (
        set ARGS=!ARGS! !ARG!
    )
) else (
    if not defined SRC set SRC=%~1
)
shift
goto parse

:done
wsl.exe --cd "%CD%" gcc !ARGS! -c "%SRC%" -o "%OUT%"
