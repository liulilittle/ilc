#!/bin/bash
# WSL GCC wrapper — called from CMake on Windows
# Translates Windows paths to WSL paths and invokes Linux gcc
exec wsl.exe gcc "$@"
