cmake_minimum_required(VERSION 3.10)
project(bench_cpp CXX)

#------------------------------------------------------------------------------
# Benchmark build driver for the transpiled bench.dll.
#
# Pattern: tests/capi/CMakeLists.txt -- the harness (scripts/run-bench.ps1)
# passes the generated project's source root as CAPILLIB_DIR. The generated
# project (gen/) builds:
#   * il2cpp_gen      : static library (transpiled Bench::Bench + Item)
#   * il2cpp_gen_exe  : the benchmark executable -- its host main() runs the
#                       transpiled Main(), which is the same bench.cs source
#                       that the net48 reference run executes.
#
# OPTIMIZATION (important): benchmark numbers are only meaningful for
# optimized builds (iron rule 2j -- performance first). Configure with
#   MinGW: -DCMAKE_BUILD_TYPE=Release   (-O3 -DNDEBUG for every target)
#   MSVC : --config Release             (/O2)
# -O0 builds would measure optimizer absence, not the transpiled code.
#------------------------------------------------------------------------------
if(NOT DEFINED CAPILLIB_DIR)
    message(FATAL_ERROR "CAPILLIB_DIR must point at the generated il2cpp_gen project root")
endif()

add_subdirectory(${CAPILLIB_DIR} ${CMAKE_BINARY_DIR}/gen)
