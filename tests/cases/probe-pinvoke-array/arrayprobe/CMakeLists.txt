cmake_minimum_required(VERSION 3.10)
project(arrayprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve), the
# same dynamic shape the WinForms corpus uses. The difftest harness builds the
# same source into the net48 oracle's output directory.
add_library(arrayprobe SHARED arrayprobe.cpp)
set_target_properties(arrayprobe PROPERTIES PREFIX "" OUTPUT_NAME arrayprobe)
target_compile_features(arrayprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(arrayprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(arrayprobe PRIVATE -Wall -Wextra)
endif()
