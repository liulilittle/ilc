cmake_minimum_required(VERSION 3.10)
project(il2cpp_gen CXX)
add_subdirectory(../../../../src/runtime ${CMAKE_BINARY_DIR}/runtime)
add_subdirectory(../kernel32 ${CMAKE_BINARY_DIR}/kernel32)
add_subdirectory(../user32 ${CMAKE_BINARY_DIR}/user32)
add_subdirectory(../comctl32 ${CMAKE_BINARY_DIR}/comctl32)
add_subdirectory(../gdi32 ${CMAKE_BINARY_DIR}/gdi32)
add_library(il2cpp_gen STATIC
    src/WfControl.cpp
    src/WfPropControl.cpp
    src/WfDialogControl.cpp
    src/WfMenuItem.cpp
    src/WfMdiChild.cpp
    src/WfTabControl.cpp
    src/Cases_Winforms1.cpp
    src/WNDCLASSEX.cpp
    src/MSG.cpp
    src/PAINTSTRUCT.cpp
    src/RECT.cpp
    src/SIZE.cpp
    src/INITCOMMONCONTROLSEX.cpp
    src/WndProc.cpp
    src/ClickHandler.cpp
    src/entry.cpp
)
target_include_directories(il2cpp_gen PUBLIC include/internal)
target_link_libraries(il2cpp_gen PUBLIC il2cpp_runtime kernel32 user32 comctl32 gdi32)
target_compile_features(il2cpp_gen PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(il2cpp_gen PRIVATE /W4 /EHsc /bigobj)
else()
  target_compile_options(il2cpp_gen PRIVATE -Wall -Wextra -fexceptions)
endif()
add_executable(il2cpp_gen_exe
    src/host.cpp
)
target_include_directories(il2cpp_gen_exe PRIVATE include/internal)
target_link_libraries(il2cpp_gen_exe PRIVATE il2cpp_gen)
target_compile_features(il2cpp_gen_exe PRIVATE cxx_std_17)
if(MSVC)
  target_compile_options(il2cpp_gen_exe PRIVATE /W4 /EHsc /bigobj)
else()
  target_compile_options(il2cpp_gen_exe PRIVATE -Wall -Wextra -fexceptions)
endif()
