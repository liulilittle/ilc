cmake_minimum_required(VERSION 3.10)
project(byrefprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve), the
# same dynamic shape the WinForms corpus uses. The difftest harness builds the
# same source into the net48 oracle's output directory.
add_library(byrefprobe SHARED byrefprobe.cpp)
set_target_properties(byrefprobe PROPERTIES PREFIX "" OUTPUT_NAME byrefprobe)
target_compile_features(byrefprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(byrefprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(byrefprobe PRIVATE -Wall -Wextra)
endif()