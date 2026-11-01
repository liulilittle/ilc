cmake_minimum_required(VERSION 3.10)
project(nameclashprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve). The
# difftest harness builds the same source into the net48 oracle's directory.
add_library(nameclashprobe SHARED nameclashprobe.cpp)
set_target_properties(nameclashprobe PROPERTIES PREFIX "" OUTPUT_NAME nameclashprobe)
target_compile_features(nameclashprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(nameclashprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(nameclashprobe PRIVATE -Wall -Wextra)
endif()
