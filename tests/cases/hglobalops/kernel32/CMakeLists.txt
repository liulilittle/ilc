# kernel32/CMakeLists.txt -- system-DLL shim for the hglobalops P/Invoke case.
# Same pattern as clipops/menuevt/mdievt: the transpiler links a target named
# after the P/Invoke module; this INTERFACE target forwards to the system
# kernel32 import library (GlobalFree -- the hglobal free side under test).
cmake_minimum_required(VERSION 3.10)
project(kernel32 C)
add_library(kernel32 INTERFACE)
target_link_libraries(kernel32 INTERFACE "kernel32")
target_compile_features(kernel32 INTERFACE cxx_std_17)
