# kernel32/CMakeLists.txt -- system-DLL shim for the clipops P/Invoke case.
# Same pattern as menuevt/mdievt: the transpiler links a target named after the
# P/Invoke module; this INTERFACE target forwards to the system kernel32 import
# library (GetModuleHandleW / GlobalAlloc / GlobalLock / GlobalUnlock /
# GlobalFree).
cmake_minimum_required(VERSION 3.10)
project(kernel32 C)
add_library(kernel32 INTERFACE)
target_link_libraries(kernel32 INTERFACE "kernel32")
target_compile_features(kernel32 INTERFACE cxx_std_17)
