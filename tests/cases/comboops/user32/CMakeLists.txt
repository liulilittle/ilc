# user32/CMakeLists.txt -- system-DLL shim for the comboops P/Invoke case.
# Same pattern as winforms1/winevent/wmresize: the transpiler links a target
# named after the P/Invoke module; this INTERFACE target forwards to the
# system user32 import library (RegisterClassExW / CreateWindowExW /
# SendMessageW / PostMessageW / PeekMessageW / TranslateMessage /
# DispatchMessageW / DefWindowProcW / DestroyWindow / PostQuitMessage).
cmake_minimum_required(VERSION 3.10)
project(user32 C)
add_library(user32 INTERFACE)
target_link_libraries(user32 INTERFACE "user32")
target_compile_features(user32 INTERFACE cxx_std_17)
