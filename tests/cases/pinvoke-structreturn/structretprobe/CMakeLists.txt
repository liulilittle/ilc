cmake_minimum_required(VERSION 3.10)
project(structretprobe CXX)
# SHARED: the generated project builds this dll but does NOT link it -- the
# DllImport call sites resolve it at RUNTIME (il2cpp_pinvoke_resolve), the
# same dynamic shape a normal app uses. The difftest harness builds the same
# source into the net48 oracle's output directory.
add_library(structretprobe SHARED structretprobe.cpp)
set_target_properties(structretprobe PROPERTIES PREFIX "" OUTPUT_NAME structretprobe)
# MSVC does not export DLL symbols without __declspec(dllexport)/.def; both
# consumers (net48 marshal stub and the transpiled resolve) look the entry
# points up at runtime, so export the extern "C" surface on that generator.
if(MSVC)
  set_target_properties(structretprobe PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
endif()
target_compile_features(structretprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(structretprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(structretprobe PRIVATE -Wall -Wextra)
endif()
