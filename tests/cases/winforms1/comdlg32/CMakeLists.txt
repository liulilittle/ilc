# comdlg32/CMakeLists.txt -- system-DLL shim for the winforms1 P/Invoke case
# (phase 25: the Open-file dialog -- GetOpenFileNameW lives in comdlg32).
# Same INTERFACE pattern as kernel32/user32/gdi32/comctl32/shell32, with the
# comctl32 difference: comdlg32 is NOT resolvable by a bare target name (CMake
# would pick the INTERFACE target itself, a no-op), so the shim contributes
# the raw library per compiler: MinGW -> -lcomdlg32 (resolved via its import
# library), MSVC -> comdlg32.lib (resolved via the LIB env from vcvars).
cmake_minimum_required(VERSION 3.10)
project(comdlg32 C)
add_library(comdlg32 INTERFACE)
if(MSVC)
  target_link_libraries(comdlg32 INTERFACE "comdlg32.lib")
else()
  target_link_libraries(comdlg32 INTERFACE "-lcomdlg32")
endif()
target_compile_features(comdlg32 INTERFACE cxx_std_17)
