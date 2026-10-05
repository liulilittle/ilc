# comctl32/CMakeLists.txt -- system-DLL shim for the statusprog P/Invoke case
# (StatusBar/ProgressBar common controls -- InitCommonControlsEx / SB_* /
# PBM_* live in comctl32). Same INTERFACE pattern as kernel32/user32, with
# ONE difference: comctl32 is NOT in CMake's default Windows system-library
# set, so the shim must contribute the raw library itself. A bare "comctl32"
# in target_link_libraries would resolve to the INTERFACE target ITSELF (a
# no-op); "-lcomctl32" breaks the MSVC linker (/lcomctl32 is not a valid
# flag). Per-compiler raw form: MinGW -> -lcomctl32 (resolved via its import
# library), MSVC -> comctl32.lib (resolved via the LIB env from vcvars).
cmake_minimum_required(VERSION 3.10)
project(comctl32 C)
add_library(comctl32 INTERFACE)
if(MSVC)
  target_link_libraries(comctl32 INTERFACE "comctl32.lib")
else()
  target_link_libraries(comctl32 INTERFACE "-lcomctl32")
endif()
target_compile_features(comctl32 INTERFACE cxx_std_17)
