cmake_minimum_required(VERSION 3.10)
project(charrefprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve), the
# same dynamic shape the WinForms corpus uses. The difftest harness builds the
# same source into the net48 oracle's output directory.
add_library(charrefprobe SHARED charrefprobe.cpp)
set_target_properties(charrefprobe PROPERTIES PREFIX "" OUTPUT_NAME charrefprobe)
target_compile_features(charrefprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(charrefprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(charrefprobe PRIVATE -Wall -Wextra)
endif()
