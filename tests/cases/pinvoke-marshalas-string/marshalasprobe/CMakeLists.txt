cmake_minimum_required(VERSION 3.10)
project(marshalasprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve). The
# difftest harness builds the same source into the net48 oracle's directory.
add_library(marshalasprobe SHARED marshalasprobe.cpp)
set_target_properties(marshalasprobe PROPERTIES PREFIX "" OUTPUT_NAME marshalasprobe)
target_compile_features(marshalasprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(marshalasprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(marshalasprobe PRIVATE -Wall -Wextra)
endif()
