cmake_minimum_required(VERSION 3.10)
project(sbansiprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve). The
# difftest harness builds the same source into the net48 oracle's output dir.
add_library(sbansiprobe SHARED sbansiprobe.cpp)
set_target_properties(sbansiprobe PROPERTIES PREFIX "" OUTPUT_NAME sbansiprobe)
target_compile_features(sbansiprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(sbansiprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(sbansiprobe PRIVATE -Wall -Wextra)
endif()
