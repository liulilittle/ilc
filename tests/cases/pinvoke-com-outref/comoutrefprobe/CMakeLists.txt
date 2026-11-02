cmake_minimum_required(VERSION 3.10)
project(comoutrefprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve). The
# difftest harness builds the same source into the net48 oracle's directory.
add_library(comoutrefprobe SHARED comoutrefprobe.cpp)
set_target_properties(comoutrefprobe PROPERTIES PREFIX "" OUTPUT_NAME comoutrefprobe)
# MSVC does not export DLL symbols without __declspec(dllexport)/.def; both
# consumers (net48 marshal stub and the transpiled resolve) look the entry
# points up at runtime, so export the extern "C" surface on that generator.
if(MSVC)
  set_target_properties(comoutrefprobe PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
endif()
target_compile_features(comoutrefprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(comoutrefprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(comoutrefprobe PRIVATE -Wall -Wextra)
endif()
