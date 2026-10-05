# kernel32/CMakeLists.txt -- system-DLL shim for the menuevt P/Invoke case.
# The transpiler's CMake emission adds a subdirectory per P/Invoke module and
# links a target named after the module. For SYSTEM DLLs (kernel32/user32 on
# Windows) there is no local source tree -- this shim declares an INTERFACE
# target that forwards to the system import library of the same name, so the
# generated extern "C" declarations (GetModuleHandleW etc.) link against the
# platform library.
cmake_minimum_required(VERSION 3.10)
project(kernel32 C)
add_library(kernel32 INTERFACE)
target_link_libraries(kernel32 INTERFACE "kernel32")
target_compile_features(kernel32 INTERFACE cxx_std_17)
