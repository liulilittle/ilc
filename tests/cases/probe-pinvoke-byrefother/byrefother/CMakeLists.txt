cmake_minimum_required(VERSION 3.10)
project(byrefother CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve), the
# same dynamic shape the WinForms corpus uses. The difftest harness builds the
# same source into the net48 oracle's output directory.
add_library(byrefother SHARED byrefother.cpp)
set_target_properties(byrefother PROPERTIES PREFIX "" OUTPUT_NAME byrefother)
target_compile_features(byrefother PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(byrefother PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(byrefother PRIVATE -Wall -Wextra)
endif()
