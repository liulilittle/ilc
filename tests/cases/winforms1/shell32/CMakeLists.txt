# shell32/CMakeLists.txt -- system-DLL shim for the winforms1 P/Invoke case
# (phase 24: the NotifyIcon tray icon -- Shell_NotifyIconW lives in shell32).
# Same INTERFACE pattern as kernel32/user32/gdi32/comctl32, with the comctl32
# difference: shell32 is NOT resolvable by a bare target name (CMake would
# pick the INTERFACE target itself, a no-op), so the shim contributes the raw
# library per compiler: MinGW -> -lshell32 (resolved via its import library),
# MSVC -> shell32.lib (resolved via the LIB env from vcvars).
cmake_minimum_required(VERSION 3.10)
project(shell32 C)
add_library(shell32 INTERFACE)
if(MSVC)
  target_link_libraries(shell32 INTERFACE "shell32.lib")
else()
  target_link_libraries(shell32 INTERFACE "-lshell32")
endif()
target_compile_features(shell32 INTERFACE cxx_std_17)
