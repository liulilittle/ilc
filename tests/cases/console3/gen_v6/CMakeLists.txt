cmake_minimum_required(VERSION 3.10)
project(il2cpp_gen CXX)
add_subdirectory(../../../../src/runtime ${CMAKE_BINARY_DIR}/runtime)
add_library(il2cpp_gen STATIC
    src/Cases_Console3.cpp
    src/entry.cpp
)
target_include_directories(il2cpp_gen PUBLIC include/internal)
target_link_libraries(il2cpp_gen PUBLIC il2cpp_runtime)
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
