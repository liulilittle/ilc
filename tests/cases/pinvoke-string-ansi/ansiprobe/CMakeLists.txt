cmake_minimum_required(VERSION 3.10)
project(ansiprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve). The
# difftest harness builds the same source into the net48 oracle's directory.
add_library(ansiprobe SHARED ansiprobe.cpp)
set_target_properties(ansiprobe PROPERTIES PREFIX "" OUTPUT_NAME ansiprobe)
target_compile_features(ansiprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(ansiprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(ansiprobe PRIVATE -Wall -Wextra)
endif()
