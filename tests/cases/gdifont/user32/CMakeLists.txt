# user32/CMakeLists.txt -- system-DLL shim for the gdifont P/Invoke case
# (DrawTextW is a user32 entry). Same INTERFACE pattern as winforms1.
cmake_minimum_required(VERSION 3.10)
project(user32 C)
add_library(user32 INTERFACE)
target_link_libraries(user32 INTERFACE "user32")
target_compile_features(user32 INTERFACE cxx_std_17)
