# user32/CMakeLists.txt -- system-DLL shim for the menuevt P/Invoke case.
# Same pattern as kernel32/: the transpiler links a target named after the
# P/Invoke module; this INTERFACE target forwards to the system user32 import
# library (RegisterClassExW / CreateWindowExW / PostMessageW / PeekMessageW /
# DispatchMessageW / DefWindowProcW / CreateMenu / CreatePopupMenu /
# AppendMenuW / SetMenu / GetMenu / GetMenuItemCount / GetMenuStringW /
# CheckMenuItem / EnableMenuItem / GetMenuState).
cmake_minimum_required(VERSION 3.10)
project(user32 C)
add_library(user32 INTERFACE)
target_link_libraries(user32 INTERFACE "user32")
target_compile_features(user32 INTERFACE cxx_std_17)
