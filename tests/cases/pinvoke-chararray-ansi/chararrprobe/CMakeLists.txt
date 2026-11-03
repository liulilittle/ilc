cmake_minimum_required(VERSION 3.10)
project(chararrprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve). The
# difftest harness builds the same source into the net48 oracle's output dir.
add_library(chararrprobe SHARED chararrprobe.cpp)
set_target_properties(chararrprobe PROPERTIES PREFIX "" OUTPUT_NAME chararrprobe)
target_compile_features(chararrprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(chararrprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(chararrprobe PRIVATE -Wall -Wextra)
endif()
