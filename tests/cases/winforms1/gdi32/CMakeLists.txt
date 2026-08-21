# gdi32/CMakeLists.txt -- system-DLL shim for the winforms1 P/Invoke case
# (stage 2: GDI drawing -- BeginPaint/EndPaint/TextOutW). Same INTERFACE
# pattern as user32/: the transpiler links a target named after the P/Invoke
# module; this shim forwards to the system gdi32 import library.
cmake_minimum_required(VERSION 3.10)
project(gdi32 C)
add_library(gdi32 INTERFACE)
target_link_libraries(gdi32 INTERFACE "gdi32")
target_compile_features(gdi32 INTERFACE cxx_std_17)
