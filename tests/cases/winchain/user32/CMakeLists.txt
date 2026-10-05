# user32/CMakeLists.txt -- system-DLL shim for the scrollops P/Invoke case.
# Same pattern as kernel32/: the INTERFACE target forwards to the system
# user32 import library (RegisterClassExW / CreateWindowExW / GetMessageW /
# DispatchMessageW / DefWindowProcW / PostMessageW / DestroyWindow /
# PostQuitMessage / PeekMessageW / SetScrollRange / SetScrollPos /
# GetScrollPos / SendMessageW).
cmake_minimum_required(VERSION 3.10)
project(user32 C)
add_library(user32 INTERFACE)
target_link_libraries(user32 INTERFACE "user32")
target_compile_features(user32 INTERFACE cxx_std_17)
