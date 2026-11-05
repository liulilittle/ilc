cmake_minimum_required(VERSION 3.10)
project(mylib CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve). The
# difftest harness builds the same source into the net48 oracle's output dir.
add_library(mylib SHARED mylib.cpp)
set_target_properties(mylib PROPERTIES PREFIX "" OUTPUT_NAME mylib)
target_compile_features(mylib PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(mylib PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(mylib PRIVATE -Wall -Wextra)
endif()