cmake_minimum_required(VERSION 3.10)
project(perf_cpp CXX)

#------------------------------------------------------------------------------
# Build driver for the transpiled perf suite -- same CAPILLIB_DIR pattern as
# tests/bench/CMakeLists.txt. The harness (scripts/run-perf.ps1) passes the
# generated project root; the generated project add_subdirectory's
# tests/perf/benchlib itself (P/Invoke module discovery: the transpiler walks
# up from the assembly directory to the folder holding <module>/CMakeLists.txt).
#
# Numbers are only meaningful for optimized builds:
#   MinGW: -DCMAKE_BUILD_TYPE=Release
#   MSVC : --config Release
# -O0 would measure the absence of the optimizer, not the transpiled code
# (that is exactly what -RedProof does on purpose).
#------------------------------------------------------------------------------
if(NOT DEFINED CAPILLIB_DIR)
    message(FATAL_ERROR "CAPILLIB_DIR must point at the generated il2cpp_gen project root")
endif()

add_subdirectory(${CAPILLIB_DIR} ${CMAKE_BINARY_DIR}/gen)
