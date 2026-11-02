cmake_minimum_required(VERSION 3.10)
project(bvalarrayprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve). The
# difftest harness builds the same source into the net48 oracle's directory.
add_library(bvalarrayprobe SHARED bvalarrayprobe.cpp)
set_target_properties(bvalarrayprobe PROPERTIES PREFIX "" OUTPUT_NAME bvalarrayprobe)
# MSVC does not export DLL symbols without __declspec(dllexport)/.def; both
# consumers (net48 marshal stub and the transpiled resolve) look the entry
# points up at runtime, so export the extern "C" surface on that generator.
if(MSVC)
  set_target_properties(bvalarrayprobe PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
endif()
target_compile_features(bvalarrayprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(bvalarrayprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(bvalarrayprobe PRIVATE -Wall -Wextra)
endif()
